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

// High-RAM Save Game Alert Settings (85% Warning / 90% Strong Critical Alert)
-(BOOL)ramAlertEnabled;
-(void)setRamAlertEnabled:(BOOL)on;

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
    NSTimeInterval _lastLightPruneTime;
    NSTimeInterval _lastAutoGcTime;
    NSTimeInterval _lastWarn85Time;
    NSTimeInterval _lastCrit90Time;
    dispatch_queue_t _ioQueue;
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

-(NSInteger)renderScaleModeForDocs:(NSString *)docsPath {
    return 1; // v6: Always locked to crisp integer 2.0x
}

-(BOOL)trimLogsEnabledForDocs:(NSString *)docsPath {
    if (!docsPath.length) return YES;
    NSString *p = [sdvHiddenGardenDir(docsPath) stringByAppendingPathComponent:@"sdv_perf.cfg"];
    NSString *raw = [[NSString stringWithContentsOfFile:p encoding:NSUTF8StringEncoding error:nil] stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
    if (!raw.length) return YES;
    return ![raw containsString:@",0"];
}

-(void)savePerfConfig:(NSInteger)renderMode trimLogs:(BOOL)trimLogs toDocs:(NSString *)docsPath {
    if (!docsPath.length) return;
    (void)renderMode;
    NSString *gd = sdvHiddenGardenDir(docsPath);
    NSString *p = [gd stringByAppendingPathComponent:@"sdv_perf.cfg"];
    NSString *s = [NSString stringWithFormat:@"1,%d", trimLogs ? 1 : 0]; // v6 locked 2.0x
    [s writeToFile:p atomically:YES encoding:NSUTF8StringEncoding error:nil];
}

-(dispatch_queue_t)ioQueue {
    if (!_ioQueue) {
        dispatch_queue_attr_t attr = dispatch_queue_attr_make_with_qos_class(DISPATCH_QUEUE_SERIAL, QOS_CLASS_UTILITY, 0);
        _ioQueue = dispatch_queue_create("com.garden.rammonitor.io", attr);
    }
    return _ioQueue;
}

-(void)triggerPruneInDocs:(NSString *)docsPath critical:(BOOL)critical {
    if (!docsPath.length) return;
    NSString *docsCopy = [docsPath copy];
    dispatch_async([self ioQueue], ^{
        NSString *gd = sdvHiddenGardenDir(docsCopy);
        NSString *pruneFlag = [gd stringByAppendingPathComponent:@"sdv_prune_now.flag"];
        [(critical ? @"CRIT" : @"1") writeToFile:pruneFlag atomically:YES encoding:NSUTF8StringEncoding error:nil];
        if (critical) {
            NSString *flag = [gd stringByAppendingPathComponent:@"sdv_gc_now.flag"];
            [@"1" writeToFile:flag atomically:YES encoding:NSUTF8StringEncoding error:nil];
        }
    });
}

-(void)triggerManualGCInDocs:(NSString *)docsPath {
    [self triggerPruneInDocs:docsPath critical:YES];
}

-(void)appendLogLine:(NSString *)line {
    if (!_logPath.length || !line.length) return;
    NSString *pathCopy = [_logPath copy];
    NSData *data = [[line stringByAppendingString:@"\n"] dataUsingEncoding:NSUTF8StringEncoding];
    if (!data.length) return;
    dispatch_async([self ioQueue], ^{
        NSFileManager *f = NSFileManager.defaultManager;
        if (![f fileExistsAtPath:pathCopy]) {
            [f createDirectoryAtPath:pathCopy.stringByDeletingLastPathComponent withIntermediateDirectories:YES attributes:nil error:nil];
            [data writeToFile:pathCopy atomically:YES];
            return;
        }
        NSFileHandle *fh = [NSFileHandle fileHandleForWritingAtPath:pathCopy];
        if (fh) {
            @try {
                [fh seekToEndOfFile];
                [fh writeData:data];
            } @catch (NSException *ex) { }
            [fh closeFile];
        }
    });
}

-(void)startWithDocsPath:(NSString *)docsPath {
    if (_started) return;
    _started = YES;
    _docsPath = [docsPath copy];
    (void)[self ioQueue];

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
        [self appendLogLine:[NSString stringWithFormat:@"=== Garden Launcher %@ RAM Monitor Started | Device RAM: %.2f GB | Initial Budget: %@ | Alerts: 85%% Warn / 90%% Critical ===",
            sdvEffectiveAppVersion(), (double)s0.deviceBytes / (1024.0 * 1024.0 * 1024.0), sdvFmtMemMB(s0.budgetBytes)]];
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
    BOOL bigPeakJump = (s.peakBytes >= _lastLoggedPeak + 32ULL * 1024 * 1024);
    BOOL highWarn = (s.usagePct >= 88.0 && (_tickCount % 5 == 0));
    if (_tickCount % 15 == 0 || bigPeakJump || highWarn) {
        if (bigPeakJump) _lastLoggedPeak = s.peakBytes;
        static NSDateFormatter *fmt;
        static dispatch_once_t once;
        dispatch_once(&once, ^{
            fmt = [NSDateFormatter new];
            fmt.dateFormat = @"HH:mm:ss";
        });
        NSString *ts = [fmt stringFromDate:NSDate.date];
        [self appendLogLine:[NSString stringWithFormat:@"[%@] Used=%@ | Avail=%@ | Budget=%@ | Peak=%@ | %.1f%% | Thermal=%@",
            ts, sdvFmtMemMB(s.usedBytes), sdvFmtMemMB(s.availBytes), sdvFmtMemMB(s.budgetBytes),
            sdvFmtMemMB(s.peakBytes), s.usagePct, sdvThermalLabel(s.thermal)]];
    }

    // Two-tier High-RAM Save Game Alert: 85% Warning & 90% Strong Critical Alert
    if (_tickCount >= 20 && s.budgetBytes > 0) {
        BOOL isCrit90 = (s.usagePct >= 90.0);
        BOOL isWarn85 = (!isCrit90 && s.usagePct >= 85.0);
        if (isCrit90 || isWarn85) {
            NSTimeInterval now = NSDate.date.timeIntervalSinceReferenceDate;
            // 1. Auto-trigger lightweight tilesheet pruning at 85-90.9% (every 45s, zero blocking freeze),
            //    and critical Gen2 compacting GC at >=91% RAM (every 25s, independent cooldown so LIGHT prune never blocks CRIT GC!)
            BOOL isCritGc = (s.usagePct >= 91.0);
            if (_docsPath.length) {
                if (isCritGc && (now - _lastAutoGcTime >= 25.0)) {
                    _lastAutoGcTime = now;
                    _lastLightPruneTime = now;
                    [self triggerPruneInDocs:_docsPath critical:YES];
                    [self appendLogLine:[NSString stringWithFormat:@"[AUTO-GC] Triggered (CRIT) at %.1f%% (Avail=%@)",
                        s.usagePct, sdvFmtMemMB(s.availBytes)]];
                } else if (!isCritGc && (now - _lastLightPruneTime >= 45.0)) {
                    _lastLightPruneTime = now;
                    [self triggerPruneInDocs:_docsPath critical:NO];
                    [self appendLogLine:[NSString stringWithFormat:@"[AUTO-GC] Triggered (LIGHT) at %.1f%% (Avail=%@)",
                        s.usagePct, sdvFmtMemMB(s.availBytes)]];
                }
            }
            // 2. Tier 2 (90%+): Strong Critical Alert (fires immediately when crossing 90%, repeats every 20s)
            if (isCrit90 && [self ramAlertEnabled] && (now - _lastCrit90Time >= 20.0)) {
                _lastCrit90Time = now;
                _lastWarn85Time = now;
                if (_docsPath.length) {
                    NSString *inGameMsg = [NSString stringWithFormat:@"CRIT|NGUY HIEM RAM %.0f%% (Du %@)! BAM SAVE LUU GAME NGAY LAP TUC!",
                        s.usagePct, sdvFmtMemCompact(s.availBytes)];
                    NSString *docsCopy = [_docsPath copy];
                    dispatch_async([self ioQueue], ^{
                        NSString *alertFile = [sdvHiddenGardenDir(docsCopy) stringByAppendingPathComponent:@"sdv_ram_alert.txt"];
                        [inGameMsg writeToFile:alertFile atomically:YES encoding:NSUTF8StringEncoding error:nil];
                    });
                }
                NSString *bannerMsg = [NSString stringWithFormat:@"🚨 CẢNH BÁO MẠNH: RAM ĐẠT %.0f%% (%@ / %@ · Dư %@)\nSẮP TRÀN RAM VĂNG GAME! HÃY BẤM SAVE LƯU GAME NGAY LẬP TỨC!",
                    s.usagePct, sdvFmtMemCompact(s.usedBytes), sdvFmtMemCompact(s.budgetBytes), sdvFmtMemCompact(s.availBytes)];
                [NSNotificationCenter.defaultCenter postNotificationName:kSDVRamHighWarningNotification
                                                                  object:nil
                                                                userInfo:@{@"msg": bannerMsg, @"critical": @YES}];
            }
            // 3. Tier 1 (85% - 89.9%): Standard Warning (repeats every 45s)
            else if (isWarn85 && [self ramAlertEnabled] && (now - _lastWarn85Time >= 45.0)) {
                _lastWarn85Time = now;
                if (_docsPath.length) {
                    NSString *inGameMsg = [NSString stringWithFormat:@"WARN|CANH BAO RAM CAO %.0f%% (Du %@)! Hay bam SAVE luu game!",
                        s.usagePct, sdvFmtMemCompact(s.availBytes)];
                    NSString *docsCopy = [_docsPath copy];
                    dispatch_async([self ioQueue], ^{
                        NSString *alertFile = [sdvHiddenGardenDir(docsCopy) stringByAppendingPathComponent:@"sdv_ram_alert.txt"];
                        [inGameMsg writeToFile:alertFile atomically:YES encoding:NSUTF8StringEncoding error:nil];
                    });
                }
                NSString *bannerMsg = [NSString stringWithFormat:@"⚠️ CẢNH BÁO RAM CAO: %.0f%% (%@ / %@ · Dư %@)\nĐã tự dọn RAM — Hãy bấm SAVE để lưu game tránh văng!",
                    s.usagePct, sdvFmtMemCompact(s.usedBytes), sdvFmtMemCompact(s.budgetBytes), sdvFmtMemCompact(s.availBytes)];
                [NSNotificationCenter.defaultCenter postNotificationName:kSDVRamHighWarningNotification
                                                                  object:nil
                                                                userInfo:@{@"msg": bannerMsg, @"critical": @NO}];
            }
        }
    }
}
@end
