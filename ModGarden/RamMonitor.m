#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <mach/mach.h>
#import <os/proc.h>

static NSString *const kSDVRamHudToggleNotification = @"SDVRamHudToggleNotification";
static NSString *const kSDVRamHudResetPosNotification = @"SDVRamHudResetPosNotification";
static NSString *const kSDVRamHighWarningNotification = @"SDVRamHighWarningNotification";
static NSString *const kSDVRamHudEnabledKey = @"sdv_ram_hud_enabled_v1";
static NSString *const kSDVRamHudDxKey = @"sdv_ram_hud_dx_v1";
static NSString *const kSDVRamHudDyKey = @"sdv_ram_hud_dy_v1";
static NSString *const kSDVRamAlertEnabledKey = @"sdv_ram_alert_enabled_v1";
static NSString *const kSDVRamThresholdPctKey = @"sdv_ram_threshold_pct_v1";

typedef struct {
    uint64_t usedBytes;
    uint64_t availBytes;
    uint64_t budgetBytes;
    uint64_t peakBytes;
    uint64_t deviceBytes;
    double usagePct;
    NSProcessInfoThermalState thermal;
} SDVRamSnapshot;

static NSString *sdvFmtMemCompact(uint64_t bytes) {
    double mb = (double)bytes / (1024.0 * 1024.0);
    if (mb >= 1024.0) return [NSString stringWithFormat:@"%.2fG", mb / 1024.0];
    return [NSString stringWithFormat:@"%.0fM", mb];
}

static NSString *sdvFmtMemMB(uint64_t bytes) {
    double mb = (double)bytes / (1024.0 * 1024.0);
    return [NSString stringWithFormat:@"%.0f MB", mb];
}

static NSString *sdvThermalLabel(NSProcessInfoThermalState st) {
    switch (st) {
        case NSProcessInfoThermalStateNominal:  return @"Mát";
        case NSProcessInfoThermalStateFair:     return @"Ấm nhẹ";
        case NSProcessInfoThermalStateSerious:  return @"Nóng";
        case NSProcessInfoThermalStateCritical: return @"Rất nóng!";
    }
    return @"Mát";
}

@interface SDVRamMonitor : NSObject
+(instancetype)shared;
-(void)startWithDocsPath:(NSString *)docsPath;
-(SDVRamSnapshot)sample;
-(void)resetPeak;
-(BOOL)hudEnabled;
-(void)setHudEnabled:(BOOL)on;
-(CGPoint)savedHudOffset;
-(void)saveHudOffset:(CGPoint)offset;
-(void)resetHudOffset;

// PojavLauncher-style RAM Ceiling & High-RAM Save Game Alert Settings
-(BOOL)ramAlertEnabled;
-(void)setRamAlertEnabled:(BOOL)on;
-(NSInteger)ramThresholdPct;
-(void)setRamThresholdPct:(NSInteger)pct;
-(uint64_t)thresholdBytesForBudget:(uint64_t)budget;

// v5 Performance Config (Documents/sdv_perf.cfg -> "renderMode,trimLogs")
// renderMode: 0 = Smooth 1.5x, 1 = Balanced 2.0x (Default), 2 = Native 3.0x
-(NSInteger)renderScaleModeForDocs:(NSString *)docsPath;
-(BOOL)trimLogsEnabledForDocs:(NSString *)docsPath;
-(void)savePerfConfig:(NSInteger)renderMode trimLogs:(BOOL)trimLogs toDocs:(NSString *)docsPath;
-(void)triggerManualGCInDocs:(NSString *)docsPath;
@end

@implementation SDVRamMonitor {
    NSString *_docsPath;
    NSString *_logPath;
    uint64_t _peakBytes;
    uint64_t _lastLoggedPeak;
    NSInteger _tickCount;
    BOOL _started;
    NSTimer *_timer;
    NSTimeInterval _lastAutoGcTime;
    NSTimeInterval _lastAlertTime;
}

+(instancetype)shared {
    static SDVRamMonitor *s;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{ s = [SDVRamMonitor new]; });
    return s;
}

-(uint64_t)currentFootprintBytes {
    task_vm_info_data_t info;
    mach_msg_type_number_t count = TASK_VM_INFO_COUNT;
    if (task_info(mach_task_self(), TASK_VM_INFO, (task_info_t)&info, &count) == KERN_SUCCESS) {
        return (uint64_t)info.phys_footprint;
    }
    struct mach_task_basic_info basic;
    mach_msg_type_number_t bcount = MACH_TASK_BASIC_INFO_COUNT;
    if (task_info(mach_task_self(), MACH_TASK_BASIC_INFO, (task_info_t)&basic, &bcount) == KERN_SUCCESS) {
        return (uint64_t)basic.resident_size;
    }
    return 0;
}

-(SDVRamSnapshot)sample {
    SDVRamSnapshot s;
    memset(&s, 0, sizeof(s));
    @try {
        uint64_t used = [self currentFootprintBytes];
        uint64_t avail = (uint64_t)os_proc_available_memory();
        uint64_t budget = used + avail;
        if (used > _peakBytes) _peakBytes = used;
        s.usedBytes = used;
        s.availBytes = avail;
        s.budgetBytes = budget;
        s.peakBytes = _peakBytes;
        s.deviceBytes = NSProcessInfo.processInfo.physicalMemory;
        s.usagePct = (budget > 0) ? ((double)used * 100.0 / (double)budget) : 0.0;
        s.thermal = NSProcessInfo.processInfo.thermalState;
    } @catch (NSException *ex) { }
    return s;
}

-(void)resetPeak {
    _peakBytes = [self currentFootprintBytes];
    _lastLoggedPeak = _peakBytes;
}

-(BOOL)hudEnabled {
    NSUserDefaults *ud = NSUserDefaults.standardUserDefaults;
    if ([ud objectForKey:kSDVRamHudEnabledKey] == nil) return YES; // Default ON
    return [ud boolForKey:kSDVRamHudEnabledKey];
}

-(void)setHudEnabled:(BOOL)on {
    [NSUserDefaults.standardUserDefaults setBool:on forKey:kSDVRamHudEnabledKey];
    [NSNotificationCenter.defaultCenter postNotificationName:kSDVRamHudToggleNotification object:nil];
}

-(CGPoint)savedHudOffset {
    NSUserDefaults *ud = NSUserDefaults.standardUserDefaults;
    return CGPointMake((CGFloat)[ud doubleForKey:kSDVRamHudDxKey], (CGFloat)[ud doubleForKey:kSDVRamHudDyKey]);
}

-(void)saveHudOffset:(CGPoint)offset {
    NSUserDefaults *ud = NSUserDefaults.standardUserDefaults;
    [ud setDouble:offset.x forKey:kSDVRamHudDxKey];
    [ud setDouble:offset.y forKey:kSDVRamHudDyKey];
}

-(void)resetHudOffset {
    [self saveHudOffset:CGPointZero];
    [NSNotificationCenter.defaultCenter postNotificationName:kSDVRamHudResetPosNotification object:nil];
}

-(BOOL)ramAlertEnabled {
    NSUserDefaults *ud = NSUserDefaults.standardUserDefaults;
    if ([ud objectForKey:kSDVRamAlertEnabledKey] == nil) return YES; // Default ON
    return [ud boolForKey:kSDVRamAlertEnabledKey];
}

-(void)setRamAlertEnabled:(BOOL)on {
    [NSUserDefaults.standardUserDefaults setBool:on forKey:kSDVRamAlertEnabledKey];
}

-(NSInteger)ramThresholdPct {
    NSUserDefaults *ud = NSUserDefaults.standardUserDefaults;
    if ([ud objectForKey:kSDVRamThresholdPctKey] == nil) return 80; // Default 80% (~1640 MB on 4GB iPhone)
    NSInteger v = [ud integerForKey:kSDVRamThresholdPctKey];
    if (v < 65) return 65;
    if (v > 92) return 92;
    return v;
}

-(void)setRamThresholdPct:(NSInteger)pct {
    NSInteger clamped = MAX(65, MIN(92, pct));
    [NSUserDefaults.standardUserDefaults setInteger:clamped forKey:kSDVRamThresholdPctKey];
}

-(uint64_t)thresholdBytesForBudget:(uint64_t)budget {
    if (budget == 0) budget = 2050ULL * 1024 * 1024;
    return (budget * (uint64_t)[self ramThresholdPct]) / 100ULL;
}

-(NSInteger)renderScaleModeForDocs:(NSString *)docsPath {
    return 1; // v6: Always locked to crisp integer 2.0x
}

-(BOOL)trimLogsEnabledForDocs:(NSString *)docsPath {
    if (!docsPath.length) return YES;
    NSString *p = [docsPath stringByAppendingPathComponent:@"sdv_perf.cfg"];
    NSString *raw = [[NSString stringWithContentsOfFile:p encoding:NSUTF8StringEncoding error:nil] stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
    if (!raw.length) return YES;
    return ![raw containsString:@",0"];
}

-(void)savePerfConfig:(NSInteger)renderMode trimLogs:(BOOL)trimLogs toDocs:(NSString *)docsPath {
    if (!docsPath.length) return;
    (void)renderMode;
    [NSFileManager.defaultManager createDirectoryAtPath:docsPath withIntermediateDirectories:YES attributes:nil error:nil];
    NSString *p = [docsPath stringByAppendingPathComponent:@"sdv_perf.cfg"];
    NSString *s = [NSString stringWithFormat:@"1,%d", trimLogs ? 1 : 0]; // v6 locked 2.0x
    [s writeToFile:p atomically:YES encoding:NSUTF8StringEncoding error:nil];
}

-(void)triggerManualGCInDocs:(NSString *)docsPath {
    if (!docsPath.length) return;
    NSString *flag = [docsPath stringByAppendingPathComponent:@"sdv_gc_now.flag"];
    [@"1" writeToFile:flag atomically:YES encoding:NSUTF8StringEncoding error:nil];
}

-(void)appendLogLine:(NSString *)line {
    if (!_logPath.length) return;
    NSData *data = [[line stringByAppendingString:@"\n"] dataUsingEncoding:NSUTF8StringEncoding];
    if (!data.length) return;
    NSFileManager *f = NSFileManager.defaultManager;
    if (![f fileExistsAtPath:_logPath]) {
        [f createDirectoryAtPath:_logPath.stringByDeletingLastPathComponent withIntermediateDirectories:YES attributes:nil error:nil];
        [data writeToFile:_logPath atomically:YES];
        return;
    }
    NSFileHandle *fh = [NSFileHandle fileHandleForWritingAtPath:_logPath];
    if (fh) {
        @try {
            [fh seekToEndOfFile];
            [fh writeData:data];
        } @catch (NSException *ex) { }
        [fh closeFile];
    }
}

-(void)startWithDocsPath:(NSString *)docsPath {
    if (_started) return;
    _started = YES;
    _docsPath = [docsPath copy];

    if (docsPath.length) {
        BOOL trimLogs = [self trimLogsEnabledForDocs:docsPath];
        [self savePerfConfig:1 trimLogs:trimLogs toDocs:docsPath]; // v6: always enforce 2.0x locked
        NSString *errDir = [docsPath stringByAppendingPathComponent:@"ErrorLogs"];
        NSString *cur = [errDir stringByAppendingPathComponent:@"ram-monitor.log"];
        NSString *prev = [errDir stringByAppendingPathComponent:@"ram-monitor-previous.log"];
        NSFileManager *f = NSFileManager.defaultManager;
        [f createDirectoryAtPath:errDir withIntermediateDirectories:YES attributes:nil error:nil];
        if ([f fileExistsAtPath:cur]) {
            [f removeItemAtPath:prev error:nil];
            [f moveItemAtPath:cur toPath:prev error:nil];
        }
        _logPath = cur;
        SDVRamSnapshot s0 = [self sample];
        [self appendLogLine:[NSString stringWithFormat:@"=== Garden Launcher v7 RAM Monitor Started | Device RAM: %.2f GB | Initial Budget: %@ | Threshold: %ld%% ===",
            (double)s0.deviceBytes / (1024.0 * 1024.0 * 1024.0), sdvFmtMemMB(s0.budgetBytes), (long)[self ramThresholdPct]]];
    }

    dispatch_async(dispatch_get_main_queue(), ^{
        self->_timer = [NSTimer scheduledTimerWithTimeInterval:1.0 repeats:YES block:^(NSTimer *t) {
            [self tick];
        }];
    });
}

-(void)tick {
    SDVRamSnapshot s = [self sample];
    _tickCount++;
    NSInteger threshPct = [self ramThresholdPct];
    BOOL bigPeakJump = (s.peakBytes >= _lastLoggedPeak + 24ULL * 1024 * 1024);
    BOOL highWarn = (s.usagePct >= (double)threshPct && (_tickCount % 2 == 0));
    if (_tickCount % 5 == 0 || bigPeakJump || highWarn) {
        if (bigPeakJump) _lastLoggedPeak = s.peakBytes;
        static NSDateFormatter *fmt;
        static dispatch_once_t once;
        dispatch_once(&once, ^{
            fmt = [NSDateFormatter new];
            fmt.dateFormat = @"HH:mm:ss";
        });
        NSString *ts = [fmt stringFromDate:NSDate.date];
        [self appendLogLine:[NSString stringWithFormat:@"[%@] Used=%@ | Avail=%@ | Budget=%@ | Peak=%@ | %.1f%% (Target:%ld%%) | Thermal=%@",
            ts, sdvFmtMemMB(s.usedBytes), sdvFmtMemMB(s.availBytes), sdvFmtMemMB(s.budgetBytes),
            sdvFmtMemMB(s.peakBytes), s.usagePct, (long)threshPct, sdvThermalLabel(s.thermal)]];
    }

    // PojavLauncher-style Soft Memory Ceiling + High-RAM Save Game Alert
    if (_tickCount >= 20 && s.budgetBytes > 0) {
        BOOL overThresh = (s.usagePct >= (double)threshPct) || (s.availBytes > 0 && s.availBytes <= 270ULL * 1024 * 1024);
        BOOL isCritical = (s.usagePct >= 88.0) || (s.availBytes > 0 && s.availBytes <= 190ULL * 1024 * 1024);
        if (overThresh) {
            NSTimeInterval now = NSDate.date.timeIntervalSinceReferenceDate;
            // 1. Auto-trigger tilesheet pruning & Mono Gen2 compacting GC (every 15s when above ceiling)
            if (_docsPath.length && (now - _lastAutoGcTime >= 15.0)) {
                _lastAutoGcTime = now;
                [self triggerManualGCInDocs:_docsPath];
                [self appendLogLine:[NSString stringWithFormat:@"[AUTO-GC] Triggered at %.1f%% (Avail=%@, Target=%ld%%)",
                    s.usagePct, sdvFmtMemMB(s.availBytes), (long)threshPct]];
            }
            // 2. Notify player in-game + native iOS banner to SAVE GAME immediately
            NSTimeInterval cooldown = isCritical ? 22.0 : 42.0;
            if ([self ramAlertEnabled] && (now - _lastAlertTime >= cooldown)) {
                _lastAlertTime = now;
                if (_docsPath.length) {
                    NSString *inGameMsg = [NSString stringWithFormat:@"CANH BAO RAM CAO (%.0f%% - Du %@)! Hay bam SAVE luu game ngay!",
                        s.usagePct, sdvFmtMemCompact(s.availBytes)];
                    NSString *alertFile = [_docsPath stringByAppendingPathComponent:@"sdv_ram_alert.txt"];
                    [inGameMsg writeToFile:alertFile atomically:YES encoding:NSUTF8StringEncoding error:nil];
                }
                NSString *bannerMsg = [NSString stringWithFormat:@"⚠️ RAM CAO: %@ / %@ (%.0f%% · Dư %@)\nĐã tự dọn RAM — Hãy bấm SAVE lưu game ngay tránh văng!",
                    sdvFmtMemCompact(s.usedBytes), sdvFmtMemCompact(s.budgetBytes), s.usagePct, sdvFmtMemCompact(s.availBytes)];
                [NSNotificationCenter.defaultCenter postNotificationName:kSDVRamHighWarningNotification
                                                                  object:nil
                                                                userInfo:@{@"msg": bannerMsg, @"critical": @(isCritical)}];
            }
        }
    }
}
@end
