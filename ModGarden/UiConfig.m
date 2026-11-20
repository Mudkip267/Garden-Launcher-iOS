#import <Foundation/Foundation.h>

@interface SDVUiConfig : NSObject
// Menu Bar
@property(nonatomic, assign) float menuBarScale;
@property(nonatomic, assign) float menuBarTopY;
@property(nonatomic, assign) float menuBarLeftX;
@property(nonatomic, assign) float menuBarGap;
@property(nonatomic, assign) float menuBarOpacity;

// Action Buttons (X / Y / A / B)
@property(nonatomic, assign) float abxyScale;
@property(nonatomic, assign) float abxySpacing;
@property(nonatomic, assign) float abxyOpacity;

// Shoulder Triggers (LT / RT)
@property(nonatomic, assign) float trigScale;
@property(nonatomic, assign) float trigOpacity;

// Joystick
@property(nonatomic, assign) float joyScale;
@property(nonatomic, assign) float joyOpacity;

// SDViOS Modals (Touch Control Settings + Key Picker + Hotkey Drawer)
@property(nonatomic, assign) float modalScale;
@property(nonatomic, assign) float drawerScale;

// Standard TouchOverlaySettings fields (touch_overlay_config.json)
@property(nonatomic, assign) float opacity;
@property(nonatomic, assign) float padScale;
@property(nonatomic, assign) BOOL leftHanded;
@property(nonatomic, assign) BOOL showKeyboardBtn;
@property(nonatomic, assign) BOOL showMenuBtn;
@property(nonatomic, assign) BOOL showHotkeyDrawerBtn;
@property(nonatomic, assign) NSInteger mouseMode; // 0=PointAndClick, 1=Trackpad, 2=Disabled

+(instancetype)loadFromDocuments:(NSString *)docsPath;
-(void)resetDefaults;
-(void)saveToDocuments:(NSString *)docsPath;
-(void)saveLiveCfgOnlyToDocuments:(NSString *)docsPath;
@end

static float sdvClampf(float v, float lo, float hi) {
    if (isnan(v) || isinf(v)) return lo;
    return v < lo ? lo : (v > hi ? hi : v);
}

static long long sdvNextRev(void) {
    static long long sLastRev = 0;
    long long now = (long long)(NSDate.date.timeIntervalSince1970 * 1000.0);
    if (now <= sLastRev) now = sLastRev + 1;
    sLastRev = now;
    return now;
}

@implementation SDVUiConfig

-(instancetype)init {
    self = [super init];
    if (self) [self resetDefaults];
    return self;
}

-(void)resetDefaults {
    _menuBarScale   = 1.00f;
    _menuBarTopY    = 24.0f;
    _menuBarLeftX   = 56.0f;
    _menuBarGap     = 8.0f;
    _menuBarOpacity = 0.75f;

    _abxyScale   = 1.00f;
    _abxySpacing = 1.00f;
    _abxyOpacity = 0.70f;

    _trigScale   = 1.00f;
    _trigOpacity = 0.70f;

    _joyScale   = 1.00f;
    _joyOpacity = 0.65f;

    _modalScale  = 1.55f;
    _drawerScale = 1.35f;

    _opacity = 0.65f;
    _padScale = 1.00f;
    _leftHanded = NO;
    _showKeyboardBtn = YES;
    _showMenuBtn = YES;
    _showHotkeyDrawerBtn = YES;
    _mouseMode = 0;
}

+(instancetype)loadFromDocuments:(NSString *)docsPath {
    SDVUiConfig *cfg = [SDVUiConfig new];
    if (!docsPath.length) return cfg;

    // 1. Read sdv_menubar.cfg (14 comma-separated floats + rev)
    NSString *mbPath = [docsPath stringByAppendingPathComponent:@"sdv_menubar.cfg"];
    NSString *mbRaw = [NSString stringWithContentsOfFile:mbPath encoding:NSUTF8StringEncoding error:nil];
    if (mbRaw.length) {
        NSArray<NSString *> *p = [mbRaw componentsSeparatedByString:@","];
        float (^readAt)(NSUInteger, float, float, float) = ^float(NSUInteger idx, float defV, float lo, float hi) {
            if (idx >= p.count) return defV;
            NSString *s = [p[idx] stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
            if (!s.length) return defV;
            return sdvClampf(s.floatValue, lo, hi);
        };
        cfg.menuBarScale   = readAt(0,  1.00f, 0.55f, 2.20f);
        cfg.menuBarTopY    = readAt(1, 24.0f,  4.0f, 160.0f);
        cfg.menuBarLeftX   = readAt(2, 56.0f,  8.0f, 240.0f);
        cfg.menuBarGap     = readAt(3,  8.0f,  2.0f,  22.0f);
        if (p.count >= 14) {
            cfg.menuBarOpacity = readAt(4,  0.75f, 0.10f, 1.00f);
            cfg.abxyScale      = readAt(5,  1.00f, 0.50f, 2.20f);
            cfg.abxySpacing    = readAt(6,  1.00f, 0.60f, 1.80f);
            cfg.abxyOpacity    = readAt(7,  0.70f, 0.05f, 1.00f);
            cfg.trigScale      = readAt(8,  1.00f, 0.50f, 2.20f);
            cfg.trigOpacity    = readAt(9,  0.70f, 0.05f, 1.00f);
            cfg.joyScale       = readAt(10, 1.00f, 0.50f, 2.20f);
            cfg.joyOpacity     = readAt(11, 0.65f, 0.05f, 1.00f);
            cfg.modalScale     = readAt(12, 1.55f, 1.00f, 2.00f);
            cfg.drawerScale    = readAt(13, 1.35f, 0.80f, 2.00f);
        }
    }

    // 2. Read touch_overlay_config.json
    NSString *ovPath = [docsPath stringByAppendingPathComponent:@"touch_overlay_config.json"];
    NSString *ovRaw = [NSString stringWithContentsOfFile:ovPath encoding:NSUTF8StringEncoding error:nil];
    if (ovRaw.length) {
        NSCharacterSet *trim = [NSCharacterSet whitespaceAndNewlineCharacterSet];
        NSCharacterSet *quotes = [NSCharacterSet characterSetWithCharactersInString:@"\" "];
        for (NSString *rawLine in [ovRaw componentsSeparatedByCharactersInSet:[NSCharacterSet newlineCharacterSet]]) {
            NSString *line = [rawLine stringByTrimmingCharactersInSet:trim];
            if ([line hasSuffix:@","]) line = [line substringToIndex:line.length - 1];
            NSRange colon = [line rangeOfString:@":"];
            if (colon.location == NSNotFound) continue;
            NSString *key = [[line substringToIndex:colon.location] stringByTrimmingCharactersInSet:quotes];
            NSString *val = [[line substringFromIndex:colon.location + 1] stringByTrimmingCharactersInSet:quotes];
            if (!key.length || !val.length) continue;

            if ([key caseInsensitiveCompare:@"Opacity"] == NSOrderedSame)
                cfg.opacity = sdvClampf(val.floatValue, 0.15f, 1.00f);
            else if ([key caseInsensitiveCompare:@"Scale"] == NSOrderedSame)
                cfg.padScale = sdvClampf(val.floatValue, 0.50f, 2.00f);
            else if ([key caseInsensitiveCompare:@"LeftHanded"] == NSOrderedSame)
                cfg.leftHanded = ([val caseInsensitiveCompare:@"true"] == NSOrderedSame);
            else if ([key caseInsensitiveCompare:@"ShowKeyboardBtn"] == NSOrderedSame)
                cfg.showKeyboardBtn = ([val caseInsensitiveCompare:@"true"] == NSOrderedSame);
            else if ([key caseInsensitiveCompare:@"ShowMenuBtn"] == NSOrderedSame)
                cfg.showMenuBtn = ([val caseInsensitiveCompare:@"true"] == NSOrderedSame);
            else if ([key caseInsensitiveCompare:@"ShowHotkeyDrawerBtn"] == NSOrderedSame)
                cfg.showHotkeyDrawerBtn = ([val caseInsensitiveCompare:@"true"] == NSOrderedSame);
            else if ([key caseInsensitiveCompare:@"MouseControlMode"] == NSOrderedSame) {
                if ([val caseInsensitiveCompare:@"Trackpad"] == NSOrderedSame || [val isEqualToString:@"1"]) cfg.mouseMode = 1;
                else if ([val caseInsensitiveCompare:@"Disabled"] == NSOrderedSame || [val isEqualToString:@"2"]) cfg.mouseMode = 2;
                else cfg.mouseMode = 0;
            }
        }
    }
    return cfg;
}

-(void)saveLiveCfgOnlyToDocuments:(NSString *)docsPath {
    if (!docsPath.length) return;
    [NSFileManager.defaultManager createDirectoryAtPath:docsPath withIntermediateDirectories:YES attributes:nil error:nil];
    NSString *mbPath = [docsPath stringByAppendingPathComponent:@"sdv_menubar.cfg"];
    NSString *content = [NSString stringWithFormat:@"%.2f,%.0f,%.0f,%.0f,%.2f,%.2f,%.2f,%.2f,%.2f,%.2f,%.2f,%.2f,%.2f,%.2f,%lld",
        sdvClampf(_menuBarScale,   0.55f, 2.20f),
        sdvClampf(_menuBarTopY,    4.0f, 160.0f),
        sdvClampf(_menuBarLeftX,   8.0f, 240.0f),
        sdvClampf(_menuBarGap,     2.0f,  22.0f),
        sdvClampf(_menuBarOpacity, 0.10f, 1.00f),
        sdvClampf(_abxyScale,      0.50f, 2.20f),
        sdvClampf(_abxySpacing,    0.60f, 1.80f),
        sdvClampf(_abxyOpacity,    0.05f, 1.00f),
        sdvClampf(_trigScale,      0.50f, 2.20f),
        sdvClampf(_trigOpacity,    0.05f, 1.00f),
        sdvClampf(_joyScale,       0.50f, 2.20f),
        sdvClampf(_joyOpacity,     0.05f, 1.00f),
        sdvClampf(_modalScale,     1.00f, 2.00f),
        sdvClampf(_drawerScale,    0.80f, 2.00f),
        sdvNextRev()];
    [content writeToFile:mbPath atomically:YES encoding:NSUTF8StringEncoding error:nil];
}

-(void)saveToDocuments:(NSString *)docsPath {
    if (!docsPath.length) return;
    [NSFileManager.defaultManager createDirectoryAtPath:docsPath withIntermediateDirectories:YES attributes:nil error:nil];

    NSString *ovPath = [docsPath stringByAppendingPathComponent:@"touch_overlay_config.json"];
    NSString *mouseStr = (_mouseMode == 1) ? @"\"Trackpad\"" : ((_mouseMode == 2) ? @"\"Disabled\"" : @"\"PointAndClick\"");
    NSDictionary<NSString *, NSString *> *updates = @{
        @"Opacity": [NSString stringWithFormat:@"%.2f", sdvClampf(_opacity, 0.15f, 1.00f)],
        @"Scale": [NSString stringWithFormat:@"%.2f", sdvClampf(_padScale, 0.50f, 2.00f)],
        @"LeftHanded": _leftHanded ? @"true" : @"false",
        @"ShowKeyboardBtn": _showKeyboardBtn ? @"true" : @"false",
        @"ShowMenuBtn": _showMenuBtn ? @"true" : @"false",
        @"ShowHotkeyDrawerBtn": _showHotkeyDrawerBtn ? @"true" : @"false",
        @"MouseControlMode": mouseStr
    };

    NSMutableArray<NSString *> *keys = [NSMutableArray array];
    NSMutableDictionary<NSString *, NSString *> *vals = [NSMutableDictionary dictionary];
    NSMutableDictionary<NSString *, NSString *> *lowerToKey = [NSMutableDictionary dictionary];

    NSString *existing = [NSString stringWithContentsOfFile:ovPath encoding:NSUTF8StringEncoding error:nil];
    if (existing.length) {
        NSCharacterSet *trim = [NSCharacterSet whitespaceAndNewlineCharacterSet];
        NSCharacterSet *quotes = [NSCharacterSet characterSetWithCharactersInString:@"\" "];
        for (NSString *rawLine in [existing componentsSeparatedByCharactersInSet:[NSCharacterSet newlineCharacterSet]]) {
            NSString *line = [rawLine stringByTrimmingCharactersInSet:trim];
            if ([line hasSuffix:@","]) line = [line substringToIndex:line.length - 1];
            NSRange colon = [line rangeOfString:@":"];
            if (colon.location == NSNotFound) continue;
            NSString *k = [[line substringToIndex:colon.location] stringByTrimmingCharactersInSet:quotes];
            NSString *v = [[line substringFromIndex:colon.location + 1] stringByTrimmingCharactersInSet:trim];
            if (!k.length || !v.length) continue;
            if (!lowerToKey[k.lowercaseString]) {
                [keys addObject:k];
                lowerToKey[k.lowercaseString] = k;
            }
            vals[lowerToKey[k.lowercaseString]] = v;
        }
    }

    NSArray<NSString *> *orderedUpdates = @[@"Opacity", @"Scale", @"LeftHanded", @"ShowKeyboardBtn", @"ShowMenuBtn", @"ShowHotkeyDrawerBtn", @"MouseControlMode"];
    for (NSString *uk in orderedUpdates) {
        NSString *existingKey = lowerToKey[uk.lowercaseString];
        if (existingKey) vals[existingKey] = updates[uk];
        else { [keys addObject:uk]; lowerToKey[uk.lowercaseString] = uk; vals[uk] = updates[uk]; }
    }

    NSMutableString *outJson = [NSMutableString stringWithString:@"{\n"];
    for (NSUInteger i = 0; i < keys.count; i++) {
        NSString *k = keys[i];
        NSString *comma = (i + 1 < keys.count) ? @"," : @"";
        [outJson appendFormat:@"  \"%@\": %@%@\n", k, vals[k], comma];
    }
    [outJson appendString:@"}\n"];
    [outJson writeToFile:ovPath atomically:YES encoding:NSUTF8StringEncoding error:nil];

    [self saveLiveCfgOnlyToDocuments:docsPath];
}

@end
