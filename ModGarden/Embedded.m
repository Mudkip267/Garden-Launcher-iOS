#define GARDEN_IN_GAME 1
#import <AVFAudio/AVAudioSession.h>
#import <objc/runtime.h>
#import <execinfo.h>
#import <signal.h>
#import <fcntl.h>
#import <unistd.h>
#import <string.h>
#import <sys/sysctl.h>
#import "main.m"

// Self-contained compiler-rt helper for @available(...) when linking directly with LLVM ld64.lld
__attribute__((visibility("hidden")))
int32_t __isPlatformVersionAtLeast(uint32_t platform, uint32_t major, uint32_t minor, uint32_t subminor) {
    (void)platform;
    NSOperatingSystemVersion v = { (NSInteger)major, (NSInteger)minor, (NSInteger)subminor };
    return [NSProcessInfo.processInfo isOperatingSystemAtLeastVersion:v] ? 1 : 0;
}

// ---- Native Boot & Crash Logger (always writes to Documents/ErrorLogs/ even on instant crash!) ----
static char gSdvBootLogPath[1024] = {0};
static char gSdvCrashLogPath[1024] = {0};
static volatile sig_atomic_t gSdvCrashHandled = 0;

static void sdvWriteRawFd(const char *path, const char *buf, size_t len) {
    if (!path || !path[0] || !buf || len == 0) return;
    int fd = open(path, O_WRONLY | O_CREAT | O_APPEND, 0644);
    if (fd >= 0) {
        (void)write(fd, buf, len);
        fsync(fd);
        close(fd);
    }
}

static void sdvBootLog(const char *msg) {
    if (!gSdvBootLogPath[0] || !msg) return;
    sdvWriteRawFd(gSdvBootLogPath, msg, strlen(msg));
    sdvWriteRawFd(gSdvBootLogPath, "\n", 1);
}

static void sdvBootLogFmt(NSString *line) {
    if (!line.length) return;
    const char *utf8 = line.UTF8String;
    if (utf8) sdvBootLog(utf8);
}

static void sdvSignalCrashHandler(int sig, siginfo_t *info, void *ucontext) {
    (void)ucontext;
    if (gSdvCrashHandled) {
        signal(sig, SIG_DFL);
        raise(sig);
        return;
    }
    gSdvCrashHandled = 1;
    int fd = open(gSdvCrashLogPath, O_WRONLY | O_CREAT | O_APPEND, 0644);
    if (fd >= 0) {
        const char *hdr = "\n=== NATIVE SIGNAL CRASH ===\nSignal: ";
        (void)write(fd, hdr, strlen(hdr));
        char num[32];
        int n = 0;
        int v = sig;
        if (v < 0) { num[n++] = '-'; v = -v; }
        char rev[16]; int rn = 0;
        do { rev[rn++] = (char)('0' + (v % 10)); v /= 10; } while (v > 0 && rn < 15);
        while (rn > 0) num[n++] = rev[--rn];
        num[n++] = '\n';
        (void)write(fd, num, (size_t)n);
        if (info) {
            const char *ah = "Fault addr (hex): 0x";
            (void)write(fd, ah, strlen(ah));
            uintptr_t addr = (uintptr_t)info->si_addr;
            char hex[17];
            const char *hd = "0123456789abcdef";
            for (int i = 15; i >= 0; i--) { hex[15 - i] = hd[(addr >> (i * 4)) & 0xF]; }
            hex[16] = '\n';
            (void)write(fd, hex, 17);
        }
        const char *bh = "Backtrace:\n";
        (void)write(fd, bh, strlen(bh));
        void *frames[64];
        int count = backtrace(frames, 64);
        if (count > 0) backtrace_symbols_fd(frames, count, fd);
        fsync(fd);
        close(fd);
    }
    signal(sig, SIG_DFL);
    raise(sig);
}

static NSUncaughtExceptionHandler *gPrevUncaughtHandler = NULL;
static void sdvUncaughtExceptionHandler(NSException *exception) {
    @try {
        NSString *txt = [NSString stringWithFormat:@"\n=== UNCAUGHT NSException ===\nName: %@\nReason: %@\nStack:\n%@\n",
            exception.name ?: @"?", exception.reason ?: @"?",
            [exception.callStackSymbols componentsJoinedByString:@"\n"] ?: @""];
        const char *u = txt.UTF8String;
        if (u) sdvWriteRawFd(gSdvCrashLogPath, u, strlen(u));
    } @catch (NSException *ignored) { }
    if (gPrevUncaughtHandler) gPrevUncaughtHandler(exception);
}

static NSString *sdvDeviceMachineModel(void) {
    size_t sz = 0;
    if (sysctlbyname("hw.machine", NULL, &sz, NULL, 0) != 0 || sz == 0 || sz > 128) return @"unknown";
    char buf[129] = {0};
    if (sysctlbyname("hw.machine", buf, &sz, NULL, 0) != 0) return @"unknown";
    return [NSString stringWithUTF8String:buf] ?: @"unknown";
}

static void sdvInitBootAndCrashLogging(NSString *docsPath) {
    if (!docsPath.length) return;
    @try {
        NSString *errDir = [docsPath stringByAppendingPathComponent:@"ErrorLogs"];
        [fm() createDirectoryAtPath:errDir withIntermediateDirectories:YES attributes:nil error:nil];
        NSString *bootCur = [errDir stringByAppendingPathComponent:@"launcher-boot.log"];
        NSString *bootPrev = [errDir stringByAppendingPathComponent:@"launcher-boot-previous.log"];
        if ([fm() fileExistsAtPath:bootCur]) {
            [fm() removeItemAtPath:bootPrev error:nil];
            [fm() moveItemAtPath:bootCur toPath:bootPrev error:nil];
        }
        NSString *crashLog = [errDir stringByAppendingPathComponent:@"native-crash.log"];
        strlcpy(gSdvBootLogPath, bootCur.fileSystemRepresentation, sizeof(gSdvBootLogPath));
        strlcpy(gSdvCrashLogPath, crashLog.fileSystemRepresentation, sizeof(gSdvCrashLogPath));

        // Install Objective-C exception + POSIX signal handlers immediately
        gPrevUncaughtHandler = NSGetUncaughtExceptionHandler();
        NSSetUncaughtExceptionHandler(&sdvUncaughtExceptionHandler);

        int sigs[] = { SIGSEGV, SIGBUS, SIGABRT, SIGILL, SIGTRAP, SIGFPE };
        struct sigaction sa;
        memset(&sa, 0, sizeof(sa));
        sa.sa_sigaction = sdvSignalCrashHandler;
        sigemptyset(&sa.sa_mask);
        sa.sa_flags = SA_SIGINFO | SA_RESETHAND;
        for (size_t i = 0; i < sizeof(sigs) / sizeof(sigs[0]); i++) {
            sigaction(sigs[i], &sa, NULL);
        }

        NSString *osVer = NSProcessInfo.processInfo.operatingSystemVersionString ?: @"?";
        NSString *model = sdvDeviceMachineModel();
        double ramGB = (double)NSProcessInfo.processInfo.physicalMemory / (1024.0 * 1024.0 * 1024.0);
        NSString *bid = NSBundle.mainBundle.bundleIdentifier ?: @"?";
        sdvBootLogFmt([NSString stringWithFormat:
            @"[BOOT 1] Garden Launcher v7.10 | iOS %@ | Device=%@ | RAM=%.2f GB | Bundle=%@",
            osVer, model, ramGB, bid]);
    } @catch (NSException *ex) { }
}

// ---- Native iOS Audio Session Manager (guarantees Playback audio even with hardware Mute switch ON!) ----
static void sdvEnsureAudioSession(const char *reason) {
    @try {
        AVAudioSession *s = AVAudioSession.sharedInstance;
        NSError *catErr = nil;
        NSError *actErr = nil;
        [s setCategory:AVAudioSessionCategoryPlayback
                  mode:AVAudioSessionModeDefault
               options:AVAudioSessionCategoryOptionMixWithOthers
                 error:&catErr];
        [s setActive:YES error:&actErr];
        NSString *outPort = s.currentRoute.outputs.firstObject.portType ?: @"none";
        sdvBootLogFmt([NSString stringWithFormat:
            @"[AUDIO] %s | cat=%@ | out=%@ | vol=%.2f | catErr=%@ | actErr=%@",
            reason ?: "init", s.category ?: @"?", outPort, s.outputVolume,
            catErr ? catErr.localizedDescription : @"none",
            actErr ? actErr.localizedDescription : @"none"]);
    } @catch (NSException *ex) {
        sdvBootLogFmt([NSString stringWithFormat:@"[AUDIO] exception: %@", ex]);
    }
}

// ---- Home Indicator Lock & Auto-Hide (requires deliberate swipe-up instead of waking on bottom taps!) ----
static BOOL sdvAlwaysHideHomeIndicator(id self, SEL _cmd) { (void)self; (void)_cmd; return YES; }
static UIRectEdge sdvDeferAllSystemEdges(id self, SEL _cmd) { (void)self; (void)_cmd; return UIRectEdgeAll; }

static void sdvApplyAutoHideHomeIndicator(UIViewController *vc) {
    if (!vc) return;
    @try {
        Class cls = object_getClass(vc);
        if (!cls) return;
        NSString *cn = NSStringFromClass(cls);
        // CRITICAL for iOS 26/27: NEVER mutate Apple's system UIKit classes (UIViewController, UINavigationController, _UI*...)!
        if (cn.length && ![cn hasPrefix:@"UI"] && ![cn hasPrefix:@"_"]) {
            static NSMutableSet<NSString *> *patched;
            static dispatch_once_t onceToken;
            dispatch_once(&onceToken, ^{ patched = [NSMutableSet set]; });
            if (![patched containsObject:cn]) {
                [patched addObject:cn];
                class_replaceMethod(cls, @selector(prefersHomeIndicatorAutoHidden), (IMP)sdvAlwaysHideHomeIndicator, "B@:");
                class_replaceMethod(cls, @selector(preferredScreenEdgesDeferringSystemGestures), (IMP)sdvDeferAllSystemEdges, "Q@:");
            }
        }
        [vc setNeedsUpdateOfHomeIndicatorAutoHidden];
        [vc setNeedsUpdateOfScreenEdgesDeferringSystemGestures];
    } @catch (NSException *ex) { }
}

// ---- Native UIScene Lifecycle Support (iOS 14 – iOS 26 & iOS 27 Mandatory Scene Compliance) ----
@interface SDVSceneHostViewController : UIViewController
@end
@implementation SDVSceneHostViewController
-(void)viewDidLoad {
    [super viewDidLoad];
    self.view.backgroundColor = [UIColor colorWithRed:0.06 green:0.07 blue:0.10 alpha:1.0];
}
-(BOOL)prefersStatusBarHidden { return YES; }
-(BOOL)prefersHomeIndicatorAutoHidden { return YES; }
-(UIRectEdge)preferredScreenEdgesDeferringSystemGestures { return UIRectEdgeAll; }
-(UIInterfaceOrientationMask)supportedInterfaceOrientations { return UIInterfaceOrientationMaskLandscape; }
@end

@interface SDVSceneHostWindow : UIWindow
@end
@implementation SDVSceneHostWindow
@end

// ---- Pass-through Overlay Window (never steals touches outside interactiveView) ----
@interface SDVPassThroughWindow : UIWindow
@property(nonatomic, weak) UIView *interactiveView;
@end

@implementation SDVPassThroughWindow
-(UIView *)hitTest:(CGPoint)point withEvent:(UIEvent *)event {
    if (!self.interactiveView || self.interactiveView.hidden || self.interactiveView.alpha < 0.05) return nil;
    UIView *hit = [super hitTest:point withEvent:event];
    if (!hit) return nil;
    return (hit == self.interactiveView || [hit isDescendantOfView:self.interactiveView]) ? hit : nil;
}
@end

// ---- Native iOS Frosted Glass Multi-Target Live UI HUD ----
@interface SDVLiveMenuBarController : UIViewController
@property(nonatomic, copy) NSString *docsPath;
@property(nonatomic, copy) void (^onDone)(void);
@property(nonatomic, readonly) UIView *capsuleView;
@end

@implementation SDVLiveMenuBarController {
    UIView *_capsuleShadow;
    UIVisualEffectView *_capsule;
    SDVUiConfig *_cfg;
    NSInteger _target; // 0=MenuBar, 1=XYAB, 2=LT/RT, 3=Stick, 4=SET/MODS
    UISegmentedControl *_seg;

    UILabel *_lbl1;
    UISlider *_sl1;
    UILabel *_val1;

    UILabel *_lbl2;
    UISlider *_sl2;
    UILabel *_val2;

    UIStackView *_menuBarRow;
    UILabel *_posBadge;

    UIStackView *_abxyRow;
    UISlider *_slSpacing;
    UILabel *_valSpacing;

    NSTimeInterval _lastSave;
}

-(UIView *)capsuleView { return _capsuleShadow; }
-(BOOL)prefersStatusBarHidden { return YES; }
-(BOOL)prefersHomeIndicatorAutoHidden { return YES; }
-(UIRectEdge)preferredScreenEdgesDeferringSystemGestures { return UIRectEdgeAll; }
-(UIInterfaceOrientationMask)supportedInterfaceOrientations { return UIInterfaceOrientationMaskLandscape; }

-(UISlider *)makeHudSliderWithTag:(NSInteger)tag tint:(UIColor *)tint {
    UISlider *s = [UISlider new];
    s.tag = tag;
    s.minimumTrackTintColor = tint;
    s.maximumTrackTintColor = [UIColor colorWithWhite:1.0 alpha:0.22];
    [s addTarget:self action:@selector(sliderMoved:) forControlEvents:UIControlEventValueChanged];
    [s addTarget:self action:@selector(sliderDone:) forControlEvents:UIControlEventTouchUpInside | UIControlEventTouchUpOutside | UIControlEventTouchCancel];
    return s;
}

-(void)viewDidLoad {
    [super viewDidLoad];
    self.view.backgroundColor = UIColor.clearColor;
    self.overrideUserInterfaceStyle = UIUserInterfaceStyleDark;
    _cfg = [SDVUiConfig loadFromDocuments:_docsPath];
    _target = 0;

    _capsuleShadow = [UIView new];
    _capsuleShadow.layer.shadowColor = UIColor.blackColor.CGColor;
    _capsuleShadow.layer.shadowOpacity = 0.42;
    _capsuleShadow.layer.shadowRadius = 16;
    _capsuleShadow.layer.shadowOffset = CGSizeMake(0, 6);
    _capsuleShadow.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:_capsuleShadow];

    _capsule = mkGlassCard(UIBlurEffectStyleSystemChromeMaterialDark, 18.0);
    [_capsuleShadow addSubview:_capsule];
    pinAll(_capsule, _capsuleShadow, 0);

    UILayoutGuide *safe = self.view.safeAreaLayoutGuide;
    NSLayoutConstraint *widthPref = [_capsuleShadow.widthAnchor constraintEqualToConstant:610];
    widthPref.priority = UILayoutPriorityDefaultHigh;
    [NSLayoutConstraint activateConstraints:@[
        [_capsuleShadow.centerXAnchor constraintEqualToAnchor:safe.centerXAnchor],
        [_capsuleShadow.bottomAnchor constraintEqualToAnchor:safe.bottomAnchor constant:-8],
        [_capsuleShadow.leadingAnchor constraintGreaterThanOrEqualToAnchor:safe.leadingAnchor constant:12],
        [_capsuleShadow.trailingAnchor constraintLessThanOrEqualToAnchor:safe.trailingAnchor constant:-12],
        widthPref
    ]];

    UIStackView *root = mkVStack(8);
    [_capsule.contentView addSubview:root];
    pinInsets(root, _capsule.contentView, UIEdgeInsetsMake(10, 12, 10, 12));

    // Row 1: Native Segmented Control + Reset + Done
    UIStackView *row1 = mkHStack(7);
    _seg = [[UISegmentedControl alloc] initWithItems:@[@"Menu Bar", @"XYAB", @"LT/RT", @"Stick", @"SET"]];
    _seg.selectedSegmentIndex = 0;
    [_seg addTarget:self action:@selector(targetChanged:) forControlEvents:UIControlEventValueChanged];
    [row1 addArrangedSubview:_seg];

    SDVAppleButton *resetBtn = mkIOSButton(@"", @"arrow.counterclockwise", [UIColor colorWithWhite:1.0 alpha:0.14], UIColor.whiteColor, 12.5, UIEdgeInsetsMake(6, 9, 6, 9));
    resetBtn.layer.cornerRadius = 9;
    [resetBtn setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    [resetBtn addTarget:self action:@selector(resetCurrentTarget) forControlEvents:UIControlEventTouchUpInside];
    [row1 addArrangedSubview:resetBtn];

    SDVAppleButton *doneBtn = mkIOSButton(@"Xong", @"checkmark", kTintBlue, UIColor.whiteColor, 13, UIEdgeInsetsMake(6, 12, 6, 12));
    doneBtn.layer.cornerRadius = 9;
    [doneBtn setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    [doneBtn addTarget:self action:@selector(doneTapped) forControlEvents:UIControlEventTouchUpInside];
    [row1 addArrangedSubview:doneBtn];
    [root addArrangedSubview:row1];

    // Row 2: Two side-by-side native sliders
    UIStackView *row2 = mkHStack(8);
    _lbl1 = mkLabel(@"Cỡ", 12, UIFontWeightSemibold, UIColor.whiteColor);
    _lbl1.numberOfLines = 1;
    [_lbl1 setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    _sl1 = [self makeHudSliderWithTag:1 tint:kTintBlue];
    _val1 = mkBadgePill(@"100%", [UIColor colorWithWhite:1.0 alpha:0.13], UIColor.whiteColor, 11);
    _val1.font = iosMonoDigits(11, UIFontWeightSemibold);

    UIView *sep = [UIView new];
    sep.backgroundColor = [UIColor colorWithWhite:1.0 alpha:0.18];
    sep.translatesAutoresizingMaskIntoConstraints = NO;
    [sep.widthAnchor constraintEqualToConstant:0.5].active = YES;
    [sep.heightAnchor constraintEqualToConstant:18].active = YES;

    _lbl2 = mkLabel(@"Rõ", 12, UIFontWeightSemibold, UIColor.whiteColor);
    _lbl2.numberOfLines = 1;
    [_lbl2 setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    _sl2 = [self makeHudSliderWithTag:2 tint:kTintGreen];
    _val2 = mkBadgePill(@"75%", [UIColor colorWithWhite:1.0 alpha:0.13], UIColor.whiteColor, 11);
    _val2.font = iosMonoDigits(11, UIFontWeightSemibold);

    [row2 addArrangedSubview:_lbl1];
    [row2 addArrangedSubview:_sl1];
    [row2 addArrangedSubview:_val1];
    [row2 addArrangedSubview:sep];
    [row2 addArrangedSubview:_lbl2];
    [row2 addArrangedSubview:_sl2];
    [row2 addArrangedSubview:_val2];
    [_sl1.widthAnchor constraintEqualToAnchor:_sl2.widthAnchor].active = YES;
    [root addArrangedSubview:row2];

    // Row 3A (Menu Bar extra): Quick presets + arrow nudgers
    _menuBarRow = mkHStack(5);
    _posBadge = mkLabel(@"L56 · T24", 11, UIFontWeightMedium, [UIColor colorWithWhite:1.0 alpha:0.75]);
    _posBadge.font = iosMonoDigits(11, UIFontWeightMedium);
    _posBadge.numberOfLines = 1;
    [_posBadge setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    [_menuBarRow addArrangedSubview:_posBadge];

    NSArray *pTitles = @[@"75%", @"100%", @"135%", @"170%"];
    for (int i = 0; i < 4; i++) {
        SDVAppleButton *pb = mkIOSButton(pTitles[i], nil, [UIColor colorWithWhite:1.0 alpha:0.14], UIColor.whiteColor, 11.5, UIEdgeInsetsMake(4, 8, 4, 8));
        pb.layer.cornerRadius = 7.5;
        pb.tag = 200 + i;
        [pb addTarget:self action:@selector(menuPresetTapped:) forControlEvents:UIControlEventTouchUpInside];
        [_menuBarRow addArrangedSubview:pb];
    }
    [_menuBarRow addArrangedSubview:[UIView new]];

    NSArray *arrows = @[@"chevron.left", @"chevron.right", @"chevron.up", @"chevron.down"];
    for (int i = 0; i < 4; i++) {
        SDVAppleButton *ab = mkIOSButton(@"", arrows[i], [UIColor colorWithWhite:1.0 alpha:0.16], UIColor.whiteColor, 11.5, UIEdgeInsetsMake(5, 8, 5, 8));
        ab.layer.cornerRadius = 7.5;
        ab.tag = 300 + i;
        [ab addTarget:self action:@selector(nudgePos:) forControlEvents:UIControlEventTouchUpInside];
        [_menuBarRow addArrangedSubview:ab];
    }
    [root addArrangedSubview:_menuBarRow];

    // Row 3B (XYAB extra): Spacing slider + opacity presets
    _abxyRow = mkHStack(6);
    UILabel *spLbl = mkLabel(@"Giãn", 12, UIFontWeightSemibold, UIColor.whiteColor);
    spLbl.numberOfLines = 1;
    [spLbl setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    _slSpacing = [self makeHudSliderWithTag:3 tint:kTintOrange];
    _slSpacing.minimumValue = 0.60f;
    _slSpacing.maximumValue = 1.80f;
    _valSpacing = mkBadgePill(@"100%", [UIColor colorWithWhite:1.0 alpha:0.13], UIColor.whiteColor, 11);
    _valSpacing.font = iosMonoDigits(11, UIFontWeightSemibold);

    [_abxyRow addArrangedSubview:spLbl];
    [_abxyRow addArrangedSubview:_slSpacing];
    [_abxyRow addArrangedSubview:_valSpacing];

    NSArray *opTitles = @[@"15%", @"40%", @"70%", @"100%"];
    for (int i = 0; i < 4; i++) {
        SDVAppleButton *ob = mkIOSButton(opTitles[i], nil, [UIColor colorWithWhite:1.0 alpha:0.14], UIColor.whiteColor, 11.5, UIEdgeInsetsMake(4, 7, 4, 7));
        ob.layer.cornerRadius = 7.5;
        ob.tag = 400 + i;
        [ob addTarget:self action:@selector(abxyOpacityPresetTapped:) forControlEvents:UIControlEventTouchUpInside];
        [_abxyRow addArrangedSubview:ob];
    }
    [root addArrangedSubview:_abxyRow];

    [self syncTargetUI];
}

-(void)syncTargetUI {
    _menuBarRow.hidden = (_target != 0);
    _abxyRow.hidden = (_target != 1);

    switch (_target) {
        case 0:
            _lbl1.text = @"Cỡ";
            _sl1.minimumValue = 0.55f; _sl1.maximumValue = 2.20f; _sl1.value = _cfg.menuBarScale;
            _lbl2.text = @"Rõ";
            _sl2.minimumValue = 0.10f; _sl2.maximumValue = 1.00f; _sl2.value = _cfg.menuBarOpacity;
            break;
        case 1:
            _lbl1.text = @"Cỡ";
            _sl1.minimumValue = 0.50f; _sl1.maximumValue = 2.20f; _sl1.value = _cfg.abxyScale;
            _lbl2.text = @"Rõ";
            _sl2.minimumValue = 0.05f; _sl2.maximumValue = 1.00f; _sl2.value = _cfg.abxyOpacity;
            _slSpacing.value = _cfg.abxySpacing;
            break;
        case 2:
            _lbl1.text = @"Cỡ";
            _sl1.minimumValue = 0.50f; _sl1.maximumValue = 2.20f; _sl1.value = _cfg.trigScale;
            _lbl2.text = @"Rõ";
            _sl2.minimumValue = 0.05f; _sl2.maximumValue = 1.00f; _sl2.value = _cfg.trigOpacity;
            break;
        case 3:
            _lbl1.text = @"Cỡ";
            _sl1.minimumValue = 0.50f; _sl1.maximumValue = 2.20f; _sl1.value = _cfg.joyScale;
            _lbl2.text = @"Rõ";
            _sl2.minimumValue = 0.05f; _sl2.maximumValue = 1.00f; _sl2.value = _cfg.joyOpacity;
            break;
        case 4:
            _lbl1.text = @"SET";
            _sl1.minimumValue = 1.00f; _sl1.maximumValue = 2.00f; _sl1.value = _cfg.modalScale;
            _lbl2.text = @"MODS";
            _sl2.minimumValue = 0.80f; _sl2.maximumValue = 2.00f; _sl2.value = _cfg.drawerScale;
            break;
    }
    [self refreshReadout];
}

-(void)refreshReadout {
    _val1.text = [NSString stringWithFormat:@"  %d%%  ", (int)roundf(_sl1.value * 100)];
    _val2.text = [NSString stringWithFormat:@"  %d%%  ", (int)roundf(_sl2.value * 100)];
    _posBadge.text = [NSString stringWithFormat:@"L%d · T%d", (int)roundf(_cfg.menuBarLeftX), (int)roundf(_cfg.menuBarTopY)];
    _valSpacing.text = [NSString stringWithFormat:@"  %d%%  ", (int)roundf(_cfg.abxySpacing * 100)];
}

-(void)targetChanged:(UISegmentedControl *)seg {
    _target = seg.selectedSegmentIndex;
    [[[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight] impactOccurred];
    [self syncTargetUI];
}

-(void)applySliderValue:(UISlider *)s {
    float q = roundf(s.value * 20.0f) / 20.0f;
    if (s.tag == 3) {
        _cfg.abxySpacing = sdvClampf(q, 0.60f, 1.80f);
        return;
    }
    switch (_target) {
        case 0:
            if (s.tag == 1) _cfg.menuBarScale   = sdvClampf(q, 0.55f, 2.20f);
            else            _cfg.menuBarOpacity = sdvClampf(q, 0.10f, 1.00f);
            break;
        case 1:
            if (s.tag == 1) _cfg.abxyScale   = sdvClampf(q, 0.50f, 2.20f);
            else            _cfg.abxyOpacity = sdvClampf(q, 0.05f, 1.00f);
            break;
        case 2:
            if (s.tag == 1) _cfg.trigScale   = sdvClampf(q, 0.50f, 2.20f);
            else            _cfg.trigOpacity = sdvClampf(q, 0.05f, 1.00f);
            break;
        case 3:
            if (s.tag == 1) _cfg.joyScale   = sdvClampf(q, 0.50f, 2.20f);
            else            _cfg.joyOpacity = sdvClampf(q, 0.05f, 1.00f);
            break;
        case 4:
            if (s.tag == 1) _cfg.modalScale  = sdvClampf(q, 1.00f, 2.00f);
            else            _cfg.drawerScale = sdvClampf(q, 0.80f, 2.00f);
            break;
    }
}

-(void)commitNow {
    [_cfg saveLiveCfgOnlyToDocuments:_docsPath];
    [self syncTargetUI];
}

-(void)sliderMoved:(UISlider *)s {
    [self applySliderValue:s];
    [self refreshReadout];
    NSTimeInterval now = NSDate.date.timeIntervalSinceReferenceDate;
    if (now - _lastSave > 0.08) {
        _lastSave = now;
        [_cfg saveLiveCfgOnlyToDocuments:_docsPath];
    }
}

-(void)sliderDone:(UISlider *)s {
    [self applySliderValue:s];
    [self commitNow];
}

-(void)menuPresetTapped:(UIButton *)b {
    float vals[4] = {0.75f, 1.00f, 1.35f, 1.70f};
    NSInteger idx = b.tag - 200;
    if (idx < 0 || idx > 3) return;
    _cfg.menuBarScale = vals[idx];
    [[[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight] impactOccurred];
    [self commitNow];
}

-(void)abxyOpacityPresetTapped:(UIButton *)b {
    float vals[4] = {0.15f, 0.40f, 0.70f, 1.00f};
    NSInteger idx = b.tag - 400;
    if (idx < 0 || idx > 3) return;
    _cfg.abxyOpacity = vals[idx];
    [[[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight] impactOccurred];
    [self commitNow];
}

-(void)nudgePos:(UIButton *)b {
    switch (b.tag - 300) {
        case 0: _cfg.menuBarLeftX = sdvClampf(_cfg.menuBarLeftX - 12.0f, 8.0f, 240.0f); break;
        case 1: _cfg.menuBarLeftX = sdvClampf(_cfg.menuBarLeftX + 12.0f, 8.0f, 240.0f); break;
        case 2: _cfg.menuBarTopY  = sdvClampf(_cfg.menuBarTopY  - 8.0f,  4.0f, 160.0f); break;
        case 3: _cfg.menuBarTopY  = sdvClampf(_cfg.menuBarTopY  + 8.0f,  4.0f, 160.0f); break;
    }
    [[[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight] impactOccurred];
    [self commitNow];
}

-(void)resetCurrentTarget {
    switch (_target) {
        case 0:
            _cfg.menuBarScale   = 1.00f;
            _cfg.menuBarLeftX   = 56.0f;
            _cfg.menuBarTopY    = 24.0f;
            _cfg.menuBarGap     = 8.0f;
            _cfg.menuBarOpacity = 0.75f;
            break;
        case 1:
            _cfg.abxyScale   = 1.00f;
            _cfg.abxySpacing = 1.00f;
            _cfg.abxyOpacity = 0.70f;
            break;
        case 2:
            _cfg.trigScale   = 1.00f;
            _cfg.trigOpacity = 0.70f;
            break;
        case 3:
            _cfg.joyScale   = 1.00f;
            _cfg.joyOpacity = 0.65f;
            break;
        case 4:
            _cfg.modalScale  = 1.55f;
            _cfg.drawerScale = 1.35f;
            break;
    }
    [[[UINotificationFeedbackGenerator alloc] init] notificationOccurred:UINotificationFeedbackTypeSuccess];
    [self commitNow];
}

-(void)doneTapped {
    [self commitNow];
    if (self.onDone) self.onDone();
}
@end

// ---- Floating Draggable Apple Glass RAM & FPS Pill HUD ----
@interface SDVRamHudController : UIViewController
@property(nonatomic, readonly) UIView *badgeView;
-(void)stopMonitoring;
@end

@implementation SDVRamHudController {
    UIVisualEffectView *_badge;
    UIView *_led;
    UILabel *_lbl;
    NSLayoutConstraint *_centerXC;
    NSLayoutConstraint *_topC;
    CGPoint _panStart;
    CADisplayLink *_link;
    CFTimeInterval _lastFpsStamp;
    NSInteger _frames;
    NSInteger _fps;
}

-(UIView *)badgeView { return _badge; }
-(BOOL)prefersStatusBarHidden { return YES; }
-(BOOL)prefersHomeIndicatorAutoHidden { return YES; }
-(UIRectEdge)preferredScreenEdgesDeferringSystemGestures { return UIRectEdgeNone; }
-(UIInterfaceOrientationMask)supportedInterfaceOrientations { return UIInterfaceOrientationMaskLandscape; }

-(void)dealloc {
    [self stopMonitoring];
    [NSNotificationCenter.defaultCenter removeObserver:self];
}

-(void)stopMonitoring {
    [_link invalidate];
    _link = nil;
}

-(void)viewDidLoad {
    [super viewDidLoad];
    self.view.backgroundColor = UIColor.clearColor;
    self.overrideUserInterfaceStyle = UIUserInterfaceStyleDark;

    _badge = mkGlassCard(UIBlurEffectStyleSystemUltraThinMaterialDark, 12.5);
    _badge.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:_badge];

    UIStackView *row = mkHStack(6);
    _led = [UIView new];
    _led.backgroundColor = kTintGreen;
    _led.layer.cornerRadius = 3.5;
    _led.translatesAutoresizingMaskIntoConstraints = NO;
    [_led.widthAnchor constraintEqualToConstant:7].active = YES;
    [_led.heightAnchor constraintEqualToConstant:7].active = YES;

    _lbl = mkLabel(@"RAM --M · Dư --G · --FPS", 11, UIFontWeightSemibold, UIColor.whiteColor);
    _lbl.font = iosMonoDigits(11, UIFontWeightSemibold);
    _lbl.numberOfLines = 1;
    [row addArrangedSubview:_led];
    [row addArrangedSubview:_lbl];
    [_badge.contentView addSubview:row];
    pinInsets(row, _badge.contentView, UIEdgeInsetsMake(4.5, 9, 4.5, 9));

    CGPoint saved = [SDVRamMonitor.shared savedHudOffset];
    UILayoutGuide *safe = self.view.safeAreaLayoutGuide;
    _centerXC = [_badge.centerXAnchor constraintEqualToAnchor:safe.centerXAnchor constant:saved.x];
    _topC = [_badge.topAnchor constraintEqualToAnchor:safe.topAnchor constant:MAX(3.0, 5.0 + saved.y)];
    [NSLayoutConstraint activateConstraints:@[_centerXC, _topC]];

    UIPanGestureRecognizer *pan = [[UIPanGestureRecognizer alloc] initWithTarget:self action:@selector(handlePan:)];
    [_badge addGestureRecognizer:pan];

    [NSNotificationCenter.defaultCenter addObserver:self selector:@selector(resetPos) name:kSDVRamHudResetPosNotification object:nil];

    _fps = 60;
    _lastFpsStamp = CACurrentMediaTime();
    _link = [CADisplayLink displayLinkWithTarget:self selector:@selector(onFrame:)];
    [_link addToRunLoop:NSRunLoop.mainRunLoop forMode:NSRunLoopCommonModes];
    [self refreshLabel];
}

-(void)resetPos {
    _centerXC.constant = 0;
    _topC.constant = 5.0;
}

-(void)handlePan:(UIPanGestureRecognizer *)gr {
    if (gr.state == UIGestureRecognizerStateBegan) {
        _panStart = CGPointMake(_centerXC.constant, _topC.constant);
    } else if (gr.state == UIGestureRecognizerStateChanged || gr.state == UIGestureRecognizerStateEnded) {
        CGPoint t = [gr translationInView:self.view];
        CGRect safe = self.view.safeAreaLayoutGuide.layoutFrame;
        CGFloat halfW = MAX(40.0, safe.size.width * 0.5 - _badge.bounds.size.width * 0.5 - 6.0);
        CGFloat maxY = MAX(6.0, safe.size.height - _badge.bounds.size.height - 6.0);
        CGFloat nx = MAX(-halfW, MIN(halfW, _panStart.x + t.x));
        CGFloat ny = MAX(3.0, MIN(maxY, _panStart.y + t.y));
        _centerXC.constant = nx;
        _topC.constant = ny;
        if (gr.state == UIGestureRecognizerStateEnded) {
            [SDVRamMonitor.shared saveHudOffset:CGPointMake(nx, ny - 5.0)];
        }
    }
}

-(void)onFrame:(CADisplayLink *)link {
    _frames++;
    CFTimeInterval now = link.timestamp;
    CFTimeInterval dt = now - _lastFpsStamp;
    if (dt >= 0.75) {
        _fps = (NSInteger)llround((double)_frames / dt);
        _frames = 0;
        _lastFpsStamp = now;
        [self refreshLabel];
    }
}

-(void)refreshLabel {
    SDVRamSnapshot s = [SDVRamMonitor.shared sample];
    UIColor *ledCol = (s.usagePct >= 90.0) ? kTintRed : ((s.usagePct >= 85.0) ? kTintOrange : kTintGreen);
    _led.backgroundColor = ledCol;
    _lbl.textColor = (s.usagePct >= 90.0) ? rgb(0xFF8A80) : ((s.usagePct >= 85.0) ? rgb(0xFFD180) : UIColor.whiteColor);
    _lbl.text = [NSString stringWithFormat:@"RAM %@ · Dư %@ · %ldFPS",
        sdvFmtMemCompact(s.usedBytes), sdvFmtMemCompact(s.availBytes), (long)_fps];
}
@end

// ---- Dedicated Fullscreen Overlay Window for Mod Garden ----
@interface SDVGardenOverlayWindow : UIWindow
@end
@implementation SDVGardenOverlayWindow
@end

// ---- 3-Finger Double-Tap Host & Lifecycle Coordinator (iOS 14 – iOS 26+ Multi-Scene Safe) ----
@interface SDVGardenGestureHost : NSObject <UIGestureRecognizerDelegate>
@property(nonatomic, strong) NSHashTable<UIWindow *> *attachedWindows;
@property(nonatomic, strong) SDVGardenOverlayWindow *gardenWindow;
@property(nonatomic, strong) SDVPassThroughWindow *liveHudWindow;
@property(nonatomic, strong) SDVPassThroughWindow *ramHudWindow;
@property(nonatomic, strong) SDVPassThroughWindow *toastWindow;
@property(nonatomic, assign) BOOL shownStartupToast;
@property(nonatomic, assign) BOOL autoOpenedSetup;
-(void)attachToGameWindows;
@end

static SDVGardenGestureHost *gSdvGardenHost = nil;

// ---- Native UIWindowSceneDelegate for iOS 14 – iOS 26 & iOS 27 Mandatory Scene Lifecycle ----
@interface SDVSceneDelegate : UIResponder <UIWindowSceneDelegate>
@property(nonatomic, strong) UIWindow *window;
@property(nonatomic, strong) SDVSceneHostWindow *placeholderWindow;
@end

@implementation SDVSceneDelegate

-(void)adoptRealWindowForScene:(UIWindowScene *)ws {
    if (!ws) return;
    @try {
        // 1. Check if AppDelegate already created a window (e.g. ShowMissingFilesScreen / ShowErrorScreen)
        id<UIApplicationDelegate> appDel = UIApplication.sharedApplication.delegate;
        if (appDel && [appDel respondsToSelector:@selector(window)]) {
            UIWindow *dw = [appDel window];
            if (dw && ![dw isKindOfClass:SDVSceneHostWindow.class] &&
                ![dw isKindOfClass:SDVGardenOverlayWindow.class] &&
                ![dw isKindOfClass:SDVPassThroughWindow.class]) {
                if (dw.windowScene != ws) {
                    dw.windowScene = ws;
                    dw.frame = ws.coordinateSpace.bounds;
                }
                if (dw.hidden) dw.hidden = NO;
                [dw makeKeyAndVisible];
                self.window = dw;
                if (self.placeholderWindow && self.placeholderWindow != dw) {
                    self.placeholderWindow.hidden = YES;
                    self.placeholderWindow = nil;
                }
                sdvBootLog("[SCENE] Adopted AppDelegate.window onto UIWindowScene");
                return;
            }
        }
        // 2. Check if MonoGame / SDViOS already attached its own real UIWindow to this scene
        for (UIWindow *w in ws.windows) {
            if ([w isKindOfClass:SDVSceneHostWindow.class] ||
                [w isKindOfClass:SDVGardenOverlayWindow.class] ||
                [w isKindOfClass:SDVPassThroughWindow.class]) continue;
            if (w.windowLevel >= UIWindowLevelAlert || w.hidden || !w.rootViewController) continue;
            self.window = w;
            if (self.placeholderWindow) {
                self.placeholderWindow.hidden = YES;
                self.placeholderWindow = nil;
                sdvBootLog("[SCENE] Retired placeholder host window (real game window active)");
            }
            return;
        }
    } @catch (NSException *ex) { }
}

-(void)scene:(UIScene *)scene willConnectToSession:(UISceneSession *)session options:(UISceneConnectionOptions *)connectionOptions {
    (void)session; (void)connectionOptions;
    if (![scene isKindOfClass:UIWindowScene.class]) return;
    UIWindowScene *ws = (UIWindowScene *)scene;
    sdvBootLog("[BOOT 5] SDVSceneDelegate willConnectToSession");

    [self adoptRealWindowForScene:ws];
    if (!self.window) {
        SDVSceneHostWindow *hw = [[SDVSceneHostWindow alloc] initWithWindowScene:ws];
        hw.backgroundColor = [UIColor colorWithRed:0.06 green:0.07 blue:0.10 alpha:1.0];
        hw.windowLevel = UIWindowLevelNormal - 1;
        hw.rootViewController = [SDVSceneHostViewController new];
        [hw makeKeyAndVisible];
        self.placeholderWindow = hw;
        self.window = hw;
        sdvBootLog("[SCENE] Installed placeholder SDVSceneHostWindow while game initializes");
    }
    if (gSdvGardenHost) {
        dispatch_async(dispatch_get_main_queue(), ^{
            [gSdvGardenHost attachToGameWindows];
        });
    }
}

-(void)sceneDidBecomeActive:(UIScene *)scene {
    if ([scene isKindOfClass:UIWindowScene.class]) {
        [self adoptRealWindowForScene:(UIWindowScene *)scene];
    }
    if (gSdvGardenHost) {
        [gSdvGardenHost attachToGameWindows];
    }
}

-(void)sceneWillEnterForeground:(UIScene *)scene {
    if ([scene isKindOfClass:UIWindowScene.class]) {
        [self adoptRealWindowForScene:(UIWindowScene *)scene];
    }
}

@end

@implementation SDVGardenGestureHost

-(instancetype)init {
    self = [super init];
    if (self) {
        _attachedWindows = [NSHashTable weakObjectsHashTable];
        [NSNotificationCenter.defaultCenter addObserver:self selector:@selector(syncRamHudVisibility) name:kSDVRamHudToggleNotification object:nil];
        [NSNotificationCenter.defaultCenter addObserver:self selector:@selector(onHighRamWarning:) name:kSDVRamHighWarningNotification object:nil];
        [NSNotificationCenter.defaultCenter addObserver:self selector:@selector(onSceneOrAppNotification) name:UISceneDidActivateNotification object:nil];
        [NSNotificationCenter.defaultCenter addObserver:self selector:@selector(onSceneOrAppNotification) name:UIApplicationDidBecomeActiveNotification object:nil];
        [NSNotificationCenter.defaultCenter addObserver:self selector:@selector(onAudioInterruption:) name:AVAudioSessionInterruptionNotification object:nil];
        [NSNotificationCenter.defaultCenter addObserver:self selector:@selector(onAudioRouteChange:) name:AVAudioSessionRouteChangeNotification object:nil];
    }
    return self;
}

-(void)onAudioInterruption:(NSNotification *)n {
    NSNumber *t = n.userInfo[AVAudioSessionInterruptionTypeKey];
    if (t && t.unsignedIntegerValue == AVAudioSessionInterruptionTypeEnded) {
        sdvEnsureAudioSession("interruption-ended");
    }
}

-(void)onAudioRouteChange:(NSNotification *)n {
    (void)n;
    sdvEnsureAudioSession("route-change");
}

-(void)onSceneOrAppNotification {
    dispatch_async(dispatch_get_main_queue(), ^{
        sdvEnsureAudioSession("did-become-active");
        [self attachToGameWindows];
    });
}

-(NSString *)docsPath {
    return [fm() URLsForDirectory:NSDocumentDirectory inDomains:NSUserDomainMask].firstObject.path;
}

-(BOOL)isOwnWindow:(UIWindow *)w {
    if (!w) return YES;
    return w == self.gardenWindow || w == self.liveHudWindow || w == self.ramHudWindow || w == self.toastWindow ||
           [w isKindOfClass:SDVGardenOverlayWindow.class] || [w isKindOfClass:SDVPassThroughWindow.class];
}

-(UIWindowScene *)primaryWindowScene {
    UIWindowScene *fallback = nil;
    for (UIScene *scene in UIApplication.sharedApplication.connectedScenes) {
        if (![scene isKindOfClass:UIWindowScene.class]) continue;
        UIWindowScene *ws = (UIWindowScene *)scene;
        if (ws.activationState == UISceneActivationStateForegroundActive) return ws;
        if (ws.activationState == UISceneActivationStateForegroundInactive && !fallback) fallback = ws;
        if (!fallback) fallback = ws;
    }
    return fallback;
}

-(UIWindow *)gameWindow {
    UIWindow *fallback = nil;
    UIWindow *hostFallback = nil;
    for (UIScene *scene in UIApplication.sharedApplication.connectedScenes) {
        if (![scene isKindOfClass:UIWindowScene.class]) continue;
        UIWindowScene *ws = (UIWindowScene *)scene;
        UISceneActivationState st = ws.activationState;
        BOOL fg = (st == UISceneActivationStateForegroundActive || st == UISceneActivationStateForegroundInactive);
        if (@available(iOS 15.0, *)) {
            UIWindow *kw = ws.keyWindow;
            if (kw && ![self isOwnWindow:kw] && !kw.hidden && kw.windowLevel < UIWindowLevelAlert && kw.rootViewController) {
                if ([kw isKindOfClass:SDVSceneHostWindow.class]) {
                    if (!hostFallback) hostFallback = kw;
                } else if (fg) {
                    return kw;
                } else if (!fallback) {
                    fallback = kw;
                }
            }
        }
        for (UIWindow *w in ws.windows) {
            if ([self isOwnWindow:w] || w.hidden) continue;
            if (w.windowLevel >= UIWindowLevelAlert) continue;
            if (!w.rootViewController) continue;
            if ([w isKindOfClass:SDVSceneHostWindow.class]) {
                if (!hostFallback) hostFallback = w;
                continue;
            }
            if (fg && w.isKeyWindow) return w;
            if (!fallback) fallback = w;
        }
    }
    return fallback ?: hostFallback;
}

-(BOOL)isGardenOpen {
    return (self.gardenWindow != nil && !self.gardenWindow.hidden);
}

-(void)attachToGameWindows {
    @try {
        // Ensure any detached AppDelegate.window (created before scene connected) is linked to the active UIWindowScene!
        UIWindowScene *activeScene = [self primaryWindowScene];
        if (activeScene) {
            if ([activeScene.delegate isKindOfClass:SDVSceneDelegate.class]) {
                [(SDVSceneDelegate *)activeScene.delegate adoptRealWindowForScene:activeScene];
            } else {
                id<UIApplicationDelegate> appDel = UIApplication.sharedApplication.delegate;
                if (appDel && [appDel respondsToSelector:@selector(window)]) {
                    UIWindow *dw = [appDel window];
                    if (dw && !dw.windowScene && ![self isOwnWindow:dw]) {
                        dw.windowScene = activeScene;
                        dw.frame = activeScene.coordinateSpace.bounds;
                        [dw makeKeyAndVisible];
                    }
                }
            }
        }

        NSMutableArray<UIWindow *> *candidates = [NSMutableArray array];
        for (UIScene *scene in UIApplication.sharedApplication.connectedScenes) {
            if (![scene isKindOfClass:UIWindowScene.class]) continue;
            for (UIWindow *w in ((UIWindowScene *)scene).windows) {
                if ([self isOwnWindow:w]) continue;
                if (w.windowLevel >= UIWindowLevelAlert) continue;
                [candidates addObject:w];
            }
        }

        for (UIWindow *w in candidates) {
            if (w.rootViewController) sdvApplyAutoHideHomeIndicator(w.rootViewController);
            if ([self.attachedWindows containsObject:w]) continue;
            w.multipleTouchEnabled = YES;
            if (w.rootViewController.view) w.rootViewController.view.multipleTouchEnabled = YES;

            UITapGestureRecognizer *tap = [[UITapGestureRecognizer alloc] initWithTarget:self action:@selector(handleThreeFingerDoubleTap:)];
            tap.numberOfTouchesRequired = 3;
            tap.numberOfTapsRequired = 2;
            tap.cancelsTouchesInView = NO;
            tap.delaysTouchesBegan = NO;
            tap.delaysTouchesEnded = NO;
            tap.delegate = self;
            [w addGestureRecognizer:tap];
            [self.attachedWindows addObject:w];
        }

        if (self.gardenWindow && !self.gardenWindow.hidden) {
            UIWindow *game = [self gameWindow];
            if (game && !CGRectIsEmpty(game.frame)) self.gardenWindow.frame = game.frame;
        }
        [self syncRamHudVisibility];

        BOOL hasWindow = ([self gameWindow] != nil);
        if (!self.autoOpenedSetup && hasWindow && ![gameDataStatus([self docsPath])[@"ready"] boolValue]) {
            self.autoOpenedSetup = YES;
            self.shownStartupToast = YES;
            sdvBootLog("[BOOT 6] Game data missing -> scheduling Mod Garden setup UI");
            dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.45 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
                if (![gameDataStatus([self docsPath])[@"ready"] boolValue]) {
                    [self openGarden];
                }
            });
        } else if (!self.shownStartupToast && hasWindow) {
            self.shownStartupToast = YES;
            sdvBootLog("[BOOT 6] Game data ready -> scheduling startup hint toast");
            dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(1.4 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
                [self showStartupHintToast];
            });
        }
    } @catch (NSException *ex) {
        sdvBootLogFmt([NSString stringWithFormat:@"[WARN] attachToGameWindows exception: %@", ex]);
    }
}

-(BOOL)gestureRecognizer:(UIGestureRecognizer *)g shouldRecognizeSimultaneouslyWithGestureRecognizer:(UIGestureRecognizer *)other {
    return YES;
}

-(BOOL)gestureRecognizer:(UIGestureRecognizer *)g shouldReceiveTouch:(UITouch *)touch {
    return ![self isGardenOpen];
}

-(void)handleThreeFingerDoubleTap:(UITapGestureRecognizer *)gr {
    if (gr.state != UIGestureRecognizerStateRecognized) return;
    [[[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium] impactOccurred];
    [self closeLiveMenuBarHUD];
    [self openGarden];
}

-(void)showStartupHintToast {
    UIWindow *game = [self gameWindow];
    if (!game || [self isGardenOpen]) return;

    SDVPassThroughWindow *tw = game.windowScene
        ? [[SDVPassThroughWindow alloc] initWithWindowScene:game.windowScene]
        : [[SDVPassThroughWindow alloc] initWithFrame:game.frame];
    tw.backgroundColor = UIColor.clearColor;
    tw.windowLevel = UIWindowLevelNormal + 2;
    tw.userInteractionEnabled = NO;
    tw.interactiveView = nil;

    UIViewController *vc = [UIViewController new];
    vc.view.backgroundColor = UIColor.clearColor;
    vc.view.userInteractionEnabled = NO;
    tw.rootViewController = vc;

    UIVisualEffectView *pill = mkGlassCard(UIBlurEffectStyleSystemMaterialDark, 16.0);
    pill.translatesAutoresizingMaskIntoConstraints = NO;
    UIStackView *row = mkHStack(8);
    UIImageView *ic = [[UIImageView alloc] initWithImage:sfIcon(@"hand.tap.fill", 15, UIImageSymbolWeightSemibold)];
    ic.tintColor = kTintBlue;
    UILabel *lbl = mkLabel(@"Chạm 3 ngón × 2 để mở Mod Garden", 13, UIFontWeightSemibold, UIColor.whiteColor);
    lbl.numberOfLines = 1;
    [row addArrangedSubview:ic];
    [row addArrangedSubview:lbl];
    [pill.contentView addSubview:row];
    pinInsets(row, pill.contentView, UIEdgeInsetsMake(7, 14, 7, 14));

    pill.alpha = 0.0;
    pill.transform = CGAffineTransformMakeTranslation(0, 12);
    [vc.view addSubview:pill];
    [NSLayoutConstraint activateConstraints:@[
        [pill.centerXAnchor constraintEqualToAnchor:vc.view.safeAreaLayoutGuide.centerXAnchor],
        [pill.bottomAnchor constraintEqualToAnchor:vc.view.safeAreaLayoutGuide.bottomAnchor constant:-14]
    ]];

    tw.hidden = NO;
    self.toastWindow = tw;

    [UIView animateWithDuration:0.25 delay:0 options:UIViewAnimationOptionCurveEaseOut animations:^{
        pill.alpha = 1.0;
        pill.transform = CGAffineTransformIdentity;
    } completion:^(BOOL finished) {
        [UIView animateWithDuration:0.35 delay:2.8 options:UIViewAnimationOptionCurveEaseIn animations:^{
            pill.alpha = 0.0;
            pill.transform = CGAffineTransformMakeTranslation(0, 8);
        } completion:^(BOOL done) {
            self.toastWindow.hidden = YES;
            self.toastWindow = nil;
        }];
    }];
}

-(void)onHighRamWarning:(NSNotification *)n {
    NSString *msg = n.userInfo[@"msg"];
    BOOL critical = [n.userInfo[@"critical"] boolValue];
    if (!msg.length) return;
    dispatch_async(dispatch_get_main_queue(), ^{
        [self showHighRamWarningBanner:msg critical:critical];
    });
}

-(void)showHighRamWarningBanner:(NSString *)msg critical:(BOOL)critical {
    UIWindow *game = [self gameWindow];
    if (!game || [self isGardenOpen]) return;

    if (self.toastWindow) {
        self.toastWindow.hidden = YES;
        self.toastWindow = nil;
    }

    SDVPassThroughWindow *tw = game.windowScene
        ? [[SDVPassThroughWindow alloc] initWithWindowScene:game.windowScene]
        : [[SDVPassThroughWindow alloc] initWithFrame:game.frame];
    tw.backgroundColor = UIColor.clearColor;
    tw.windowLevel = UIWindowLevelNormal + 4;
    tw.userInteractionEnabled = NO;
    tw.interactiveView = nil;

    UIViewController *vc = [UIViewController new];
    vc.view.backgroundColor = UIColor.clearColor;
    vc.view.userInteractionEnabled = NO;
    tw.rootViewController = vc;

    UIVisualEffectView *pill = mkGlassCard(UIBlurEffectStyleSystemChromeMaterialDark, 16.0);
    pill.layer.borderWidth = critical ? 2.2 : 1.4;
    pill.layer.borderColor = (critical ? kTintRed : kTintOrange).CGColor;
    if (critical) {
        pill.contentView.backgroundColor = [kTintRed colorWithAlphaComponent:0.28];
    }
    pill.translatesAutoresizingMaskIntoConstraints = NO;

    UIStackView *row = mkHStack(10);
    UIImageView *ic = [[UIImageView alloc] initWithImage:sfIcon(@"exclamationmark.triangle.fill", critical ? 21 : 18, UIImageSymbolWeightBold)];
    ic.tintColor = critical ? kTintRed : kTintOrange;
    [ic setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    UILabel *lbl = mkLabel(msg, critical ? 13.0 : 12.5, critical ? UIFontWeightBold : UIFontWeightSemibold, UIColor.whiteColor);
    lbl.numberOfLines = 2;
    [row addArrangedSubview:ic];
    [row addArrangedSubview:lbl];
    [pill.contentView addSubview:row];
    pinInsets(row, pill.contentView, UIEdgeInsetsMake(critical ? 10 : 8, 15, critical ? 10 : 8, 15));

    pill.alpha = 0.0;
    pill.transform = CGAffineTransformMakeTranslation(0, -14);
    [vc.view addSubview:pill];
    [NSLayoutConstraint activateConstraints:@[
        [pill.centerXAnchor constraintEqualToAnchor:vc.view.safeAreaLayoutGuide.centerXAnchor],
        [pill.topAnchor constraintEqualToAnchor:vc.view.safeAreaLayoutGuide.topAnchor constant:28],
        [pill.widthAnchor constraintLessThanOrEqualToConstant:550]
    ]];

    tw.hidden = NO;
    self.toastWindow = tw;

    [UIView animateWithDuration:0.25 delay:0 options:UIViewAnimationOptionCurveEaseOut animations:^{
        pill.alpha = 1.0;
        pill.transform = CGAffineTransformIdentity;
    } completion:^(BOOL finished) {
        [UIView animateWithDuration:0.35 delay:(critical ? 8.0 : 5.5) options:UIViewAnimationOptionCurveEaseIn animations:^{
            pill.alpha = 0.0;
            pill.transform = CGAffineTransformMakeTranslation(0, -10);
        } completion:^(BOOL done) {
            if (self.toastWindow == tw) {
                self.toastWindow.hidden = YES;
                self.toastWindow = nil;
            }
        }];
    }];
}

-(void)openGarden {
    if ([self isGardenOpen]) return;
    UIWindow *game = [self gameWindow];
    UIWindowScene *ws = game.windowScene ?: [self primaryWindowScene];
    if (!game && !ws) return;

    CGRect bounds = (game && !CGRectIsEmpty(game.frame)) ? game.frame : (ws ? ws.coordinateSpace.bounds : CGRectZero);
    SDVGardenOverlayWindow *gw = ws
        ? [[SDVGardenOverlayWindow alloc] initWithWindowScene:ws]
        : [[SDVGardenOverlayWindow alloc] initWithFrame:bounds];
    if (!CGRectIsEmpty(bounds)) gw.frame = bounds;
    gw.autoresizingMask = UIViewAutoresizingFlexibleWidth | UIViewAutoresizingFlexibleHeight;
    gw.backgroundColor = kBgCanvas;
    gw.overrideUserInterfaceStyle = UIUserInterfaceStyleUnspecified;
    gw.windowLevel = UIWindowLevelNormal + 5;

    Home *mgr = [Home new];
    __weak typeof(self) weakSelf = self;
    mgr.onRequestClose = ^{
        [weakSelf closeGarden];
    };
    mgr.onRequestLiveMenuBarMode = ^{
        [weakSelf showLiveMenuBarHUD];
    };
    gw.rootViewController = mgr;
    gw.alpha = 0.0;
    gw.hidden = NO;
    [gw makeKeyAndVisible];
    self.gardenWindow = gw;
    sdvBootLog("[BOOT 7] Mod Garden overlay window opened");

    [UIView animateWithDuration:0.18 delay:0 options:UIViewAnimationOptionCurveEaseOut animations:^{
        gw.alpha = 1.0;
    } completion:nil];
}

-(void)closeGarden {
    SDVGardenOverlayWindow *gw = self.gardenWindow;
    if (!gw) return;
    self.gardenWindow = nil;
    [UIView animateWithDuration:0.16 delay:0 options:UIViewAnimationOptionCurveEaseIn animations:^{
        gw.alpha = 0.0;
    } completion:^(BOOL finished) {
        gw.hidden = YES;
        gw.rootViewController = nil;
    }];
}

-(void)showLiveMenuBarHUD {
    UIWindow *game = [self gameWindow];
    if (!game) return;
    [self closeLiveMenuBarHUD];

    SDVPassThroughWindow *w = game.windowScene
        ? [[SDVPassThroughWindow alloc] initWithWindowScene:game.windowScene]
        : [[SDVPassThroughWindow alloc] initWithFrame:game.frame];
    w.backgroundColor = UIColor.clearColor;
    w.windowLevel = UIWindowLevelNormal + 6;

    SDVLiveMenuBarController *ctrl = [SDVLiveMenuBarController new];
    ctrl.docsPath = [self docsPath];
    __weak typeof(self) weakSelf = self;
    ctrl.onDone = ^{
        [weakSelf closeLiveMenuBarHUD];
    };
    w.rootViewController = ctrl;
    [ctrl loadViewIfNeeded];
    w.interactiveView = ctrl.capsuleView;
    w.frame = game.frame;
    w.hidden = NO;
    self.liveHudWindow = w;
}

-(void)closeLiveMenuBarHUD {
    if (self.liveHudWindow) {
        self.liveHudWindow.hidden = YES;
        self.liveHudWindow.rootViewController = nil;
        self.liveHudWindow = nil;
    }
}

-(void)syncRamHudVisibility {
    BOOL want = SDVRamMonitor.shared.hudEnabled;
    if (!want) {
        if (self.ramHudWindow) {
            if ([self.ramHudWindow.rootViewController isKindOfClass:SDVRamHudController.class]) {
                [(SDVRamHudController *)self.ramHudWindow.rootViewController stopMonitoring];
            }
            self.ramHudWindow.hidden = YES;
            self.ramHudWindow.rootViewController = nil;
            self.ramHudWindow = nil;
        }
        return;
    }
    UIWindow *game = [self gameWindow];
    if (!game) return;
    if (self.ramHudWindow) {
        self.ramHudWindow.frame = game.frame;
        self.ramHudWindow.hidden = NO;
        return;
    }
    SDVPassThroughWindow *w = game.windowScene
        ? [[SDVPassThroughWindow alloc] initWithWindowScene:game.windowScene]
        : [[SDVPassThroughWindow alloc] initWithFrame:game.frame];
    w.backgroundColor = UIColor.clearColor;
    w.windowLevel = UIWindowLevelNormal + 3;
    SDVRamHudController *ctrl = [SDVRamHudController new];
    w.rootViewController = ctrl;
    [ctrl loadViewIfNeeded];
    w.interactiveView = ctrl.badgeView;
    w.frame = game.frame;
    w.hidden = NO;
    self.ramHudWindow = w;
}
@end

__attribute__((constructor)) static void startGardenControls(void) {
    @autoreleasepool {
        @try {
            NSString *docs = [fm() URLsForDirectory:NSDocumentDirectory inDomains:NSUserDomainMask].firstObject.path;
            sdvInitBootAndCrashLogging(docs);
            sdvEnsureAudioSession("constructor");
            syncBundledLidgren(docs);
            syncBundledSmapi(docs);
            syncBundledMobileControls(docs);
            sdvBootLog("[BOOT 2] Bundled Lidgren / SMAPI / SDVMobileControls synced");
            [SDVRamMonitor.shared startWithDocsPath:docs];
            sdvBootLog("[BOOT 3] SDVRamMonitor started");
        } @catch (NSException *ex) {
            sdvBootLogFmt([NSString stringWithFormat:@"[ERR] constructor exception: %@", ex]);
        }
    }
    dispatch_async(dispatch_get_main_queue(), ^{
        @try {
            sdvBootLog("[BOOT 4] Main runloop active -> starting SDVGardenGestureHost");
            gSdvGardenHost = [SDVGardenGestureHost new];
            [gSdvGardenHost attachToGameWindows];
            [NSTimer scheduledTimerWithTimeInterval:1.5 repeats:YES block:^(NSTimer *t) {
                (void)t;
                [gSdvGardenHost attachToGameWindows];
            }];
        } @catch (NSException *ex) {
            sdvBootLogFmt([NSString stringWithFormat:@"[ERR] main init exception: %@", ex]);
        }
    });
}
