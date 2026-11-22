#import <Foundation/Foundation.h>
#import <CommonCrypto/CommonDigest.h>
#import <dlfcn.h>
#import <wchar.h>
#import "vendor/miniz.h"

static NSFileManager *fm(void) { return NSFileManager.defaultManager; }
static void fail(NSString *s) { @throw [NSException exceptionWithName:@"GardenError" reason:s userInfo:nil]; }
static void check(BOOL ok, NSError *error) { if (!ok) fail(error.localizedDescription ?: @"Không thể đọc hoặc ghi tệp."); }
static NSString *join(NSString *a, NSString *b) { return [a stringByAppendingPathComponent:b]; }
static BOOL dir(NSString *p) { BOOL d=NO; return [fm() fileExistsAtPath:p isDirectory:&d] && d; }
static void mkdirs(NSString *p) { NSError *e=nil; BOOL ok=[fm() createDirectoryAtPath:p withIntermediateDirectories:YES attributes:nil error:&e]; check(ok,e); }
static void removeItem(NSString *p) { if ([fm() fileExistsAtPath:p]) { NSError *e=nil; BOOL ok=[fm() removeItemAtPath:p error:&e]; check(ok,e); } }
static NSString *hashData(NSData *data) {
    unsigned char bytes[CC_SHA256_DIGEST_LENGTH]; CC_SHA256(data.bytes,(CC_LONG)data.length,bytes);
    NSMutableString *s=[NSMutableString string]; for (int i=0;i<CC_SHA256_DIGEST_LENGTH;i++) [s appendFormat:@"%02x",bytes[i]]; return s;
}
static NSData *readFile(NSString *p) { NSError *e=nil; NSData *d=[NSData dataWithContentsOfFile:p options:NSDataReadingMappedIfSafe error:&e]; check(d!=nil,e); return d; }
static void writeFile(NSData *d, NSString *p) { mkdirs(p.stringByDeletingLastPathComponent); NSError *e=nil; BOOL ok=[d writeToFile:p options:NSDataWritingAtomic error:&e]; check(ok,e); }

static NSData *stripJson5ForIOS14(NSData *raw) {
    if (!raw.length) return raw;
    const unsigned char *b = (const unsigned char *)raw.bytes;
    NSUInteger len = raw.length, start = 0;
    if (len >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) start = 3;
    NSMutableData *out = [NSMutableData dataWithCapacity:len];
    BOOL inStr = NO, esc = NO;
    for (NSUInteger i = start; i < len; ) {
        unsigned char c = b[i];
        if (inStr) {
            [out appendBytes:&c length:1];
            if (esc) esc = NO;
            else if (c == '\\') esc = YES;
            else if (c == '"') inStr = NO;
            i++;
            continue;
        }
        if (c == '"') { inStr = YES; [out appendBytes:&c length:1]; i++; continue; }
        if (c == '/' && i + 1 < len && b[i+1] == '/') {
            i += 2;
            while (i < len && b[i] != '\n' && b[i] != '\r') i++;
            continue;
        }
        if (c == '/' && i + 1 < len && b[i+1] == '*') {
            i += 2;
            while (i + 1 < len && !(b[i] == '*' && b[i+1] == '/')) i++;
            i = (i + 1 < len) ? i + 2 : len;
            continue;
        }
        if (c == ',') {
            NSUInteger j = i + 1;
            while (j < len) {
                unsigned char n = b[j];
                if (n == ' ' || n == '\t' || n == '\r' || n == '\n') { j++; continue; }
                if (n == '/' && j + 1 < len && b[j+1] == '/') { j += 2; while (j < len && b[j] != '\n') j++; continue; }
                if (n == '/' && j + 1 < len && b[j+1] == '*') { j += 2; while (j + 1 < len && !(b[j] == '*' && b[j+1] == '/')) j++; j = (j + 1 < len) ? j + 2 : len; continue; }
                break;
            }
            if (j < len && (b[j] == '}' || b[j] == ']')) { i++; continue; }
        }
        [out appendBytes:&c length:1];
        i++;
    }
    return out;
}

static id jsonAt(NSString *p) {
    NSData *raw = readFile(p);
    if (!raw.length) fail([NSString stringWithFormat:@"%@ — tệp trống.", p.lastPathComponent]);
    // Handle UTF-16LE / UTF-16BE BOM transparently (common in Windows-edited Vietnamese mods!)
    const unsigned char *b = (const unsigned char *)raw.bytes;
    if (raw.length >= 2 && ((b[0] == 0xFF && b[1] == 0xFE) || (b[0] == 0xFE && b[1] == 0xFF))) {
        NSStringEncoding enc = (b[0] == 0xFF) ? NSUTF16LittleEndianStringEncoding : NSUTF16BigEndianStringEncoding;
        NSString *s = [[NSString alloc] initWithData:[raw subdataWithRange:NSMakeRange(2, raw.length - 2)] encoding:enc];
        if (s) raw = [s dataUsingEncoding:NSUTF8StringEncoding];
    }
    // Always strip UTF-8 BOM + JSON5 comments/trailing commas for maximum compatibility across iOS 14-27+
    NSData *sanitized = stripJson5ForIOS14(raw);
    NSError *e = nil;
    id j = nil;
    if (@available(iOS 15.0, *)) {
        j = [NSJSONSerialization JSONObjectWithData:sanitized options:NSJSONReadingJSON5Allowed | NSJSONReadingAllowFragments error:&e];
        if (!j) j = [NSJSONSerialization JSONObjectWithData:raw options:NSJSONReadingJSON5Allowed error:&e];
    } else {
        j = [NSJSONSerialization JSONObjectWithData:sanitized options:NSJSONReadingAllowFragments error:&e];
    }
    if (!j) fail([NSString stringWithFormat:@"%@ — %@", p.lastPathComponent, e.localizedDescription]);
    return j;
}

static void syncBundledLidgren(NSString *docsPath) {
    if (!docsPath.length) return;
    @try {
        NSArray<NSString *> *cands = @[
            join(join(join(NSBundle.mainBundle.bundlePath, @"Frameworks"), @"SDVModGarden.framework"), @"Lidgren.Network.dll"),
            join(NSBundle.mainBundle.bundlePath, @"Lidgren.Network.dll"),
            join(join(NSBundle.mainBundle.bundlePath, @"BundledGame"), @"Lidgren.Network.dll")
        ];
        NSString *src = nil;
        for (NSString *c in cands) {
            if ([fm() fileExistsAtPath:c]) { src = c; break; }
        }
        if (!src) return;
        NSData *srcData = [NSData dataWithContentsOfFile:src options:NSDataReadingMappedIfSafe error:nil];
        if (!srcData.length) return;
        NSString *srcHash = hashData(srcData);

        NSMutableArray<NSString *> *targets = [NSMutableArray arrayWithObject:docsPath];
        NSString *subSdv = join(docsPath, @"StardewValley");
        if (dir(subSdv)) [targets addObject:subSdv];

        for (NSString *base in targets) {
            NSString *dst = join(base, @"Lidgren.Network.dll");
            NSData *dstData = [fm() fileExistsAtPath:dst] ? [NSData dataWithContentsOfFile:dst options:NSDataReadingMappedIfSafe error:nil] : nil;
            if (!dstData || dstData.length != srcData.length || ![srcHash isEqualToString:hashData(dstData)]) {
                [fm() createDirectoryAtPath:base withIntermediateDirectories:YES attributes:nil error:nil];
                [srcData writeToFile:dst options:NSDataWritingAtomic error:nil];
            }
        }
    } @catch (NSException *ex) { }
}

static BOOL dataContainsAscii(NSData *data, const char *needle) {
    if (!data.length || !needle || !needle[0]) return NO;
    NSData *pat = [NSData dataWithBytesNoCopy:(void *)needle length:strlen(needle) freeWhenDone:NO];
    return [data rangeOfData:pat options:0 range:NSMakeRange(0, data.length)].location != NSNotFound;
}

static NSString *syncBundledSmapi(NSString *docsPath) {
    if (!docsPath.length) return @"no-docs";
    @try {
        NSString *smapiRoot = join(join(join(NSBundle.mainBundle.bundlePath, @"Frameworks"), @"SDVModGarden.framework"), @"BundledSMAPI");
        if (!dir(smapiRoot)) return @"no-bundle";
        [fm() createDirectoryAtPath:docsPath withIntermediateDirectories:YES attributes:nil error:nil];

        NSString *srcDll = join(smapiRoot, @"StardewModdingAPI.dll");
        NSString *srcInternal = join(smapiRoot, @"smapi-internal");
        if (![fm() fileExistsAtPath:srcDll]) return @"no-src-dll";
        NSData *srcDllData = [NSData dataWithContentsOfFile:srcDll options:NSDataReadingMappedIfSafe error:nil];
        if (!srcDllData.length) return @"empty-src-dll";
        NSString *srcHash = hashData(srcDllData);

        NSString *srcToolkit = join(srcInternal, @"SMAPI.Toolkit.dll");
        NSData *srcToolkitData = [fm() fileExistsAtPath:srcToolkit] ? [NSData dataWithContentsOfFile:srcToolkit options:NSDataReadingMappedIfSafe error:nil] : nil;
        NSString *srcToolkitHash = srcToolkitData.length ? hashData(srcToolkitData) : nil;

        NSMutableArray<NSString *> *targets = [NSMutableArray arrayWithObject:docsPath];
        NSString *subSdv = join(docsPath, @"StardewValley");
        if (dir(subSdv) && ([fm() fileExistsAtPath:join(subSdv, @"Stardew Valley.dll")] || [fm() fileExistsAtPath:join(subSdv, @"StardewModdingAPI.dll")])) {
            [targets addObject:subSdv];
        }

        NSMutableArray<NSString *> *notes = [NSMutableArray array];
        for (NSString *base in targets) {
            NSString *dstDll = join(base, @"StardewModdingAPI.dll");
            NSString *dstInternal = join(base, @"smapi-internal");
            NSString *stampPath = join(dstInternal, @".garden_smapi_v1");

            NSString *reason = nil;
            NSData *dstData = [fm() fileExistsAtPath:dstDll] ? [NSData dataWithContentsOfFile:dstDll options:NSDataReadingMappedIfSafe error:nil] : nil;
            if (!dstData || dstData.length < 100 * 1024) {
                reason = @"missing";
            } else if (dataContainsAscii(dstData, "set_InputEncoding") || dataContainsAscii(dstData, "set_OutputEncoding")) {
                // Windows-compiled SMAPI calls Console.set_InputEncoding/OutputEncoding -> PlatformNotSupportedException on iOS!
                reason = @"windows-smapi-detected";
            } else if (!dataContainsAscii(dstData, "ConsoleColorSchemes")) {
                // Older SMAPI (<=4.3) incompatible with bundled SMAPI 4.5.2 internals
                reason = @"legacy-smapi-detected";
            } else {
                NSString *stamp = [NSString stringWithContentsOfFile:stampPath encoding:NSUTF8StringEncoding error:nil];
                stamp = [stamp stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
                if (![stamp isEqualToString:srcHash]) {
                    reason = @"stamp-mismatch";
                }
            }

            if (!reason && srcToolkitHash.length) {
                NSString *dstToolkit = join(dstInternal, @"SMAPI.Toolkit.dll");
                NSData *dstTk = [fm() fileExistsAtPath:dstToolkit] ? [NSData dataWithContentsOfFile:dstToolkit options:NSDataReadingMappedIfSafe error:nil] : nil;
                if (!dstTk || dstTk.length != srcToolkitData.length || ![hashData(dstTk) isEqualToString:srcToolkitHash]) {
                    reason = @"toolkit-mismatch";
                }
            }

            if (!reason && dir(srcInternal)) {
                NSDirectoryEnumerator *chkEn = [fm() enumeratorAtPath:srcInternal];
                for (NSString *rel in chkEn) {
                    if (![chkEn.fileAttributes.fileType isEqualToString:NSFileTypeRegular]) continue;
                    if (![fm() fileExistsAtPath:join(dstInternal, rel)]) {
                        reason = @"missing-internal";
                        break;
                    }
                }
            }

            // Also remove any stale cached StardewModdingAPI.dll inside smapi-internal/ if it is a Windows SMAPI or when re-syncing
            NSString *cachedSmapi = join(dstInternal, @"StardewModdingAPI.dll");
            if ([fm() fileExistsAtPath:cachedSmapi]) {
                NSData *cd = [NSData dataWithContentsOfFile:cachedSmapi options:NSDataReadingMappedIfSafe error:nil];
                if (reason || !cd.length || dataContainsAscii(cd, "set_InputEncoding") || dataContainsAscii(cd, "set_OutputEncoding")) {
                    [fm() removeItemAtPath:cachedSmapi error:nil];
                }
            }

            if (reason) {
                [fm() createDirectoryAtPath:base withIntermediateDirectories:YES attributes:nil error:nil];
                [srcDllData writeToFile:dstDll options:NSDataWritingAtomic error:nil];

                // Preserve user's config.user.json if present, then cleanly deploy iOS-verified smapi-internal/
                NSData *savedUserCfg = nil;
                NSString *userCfgPath = join(dstInternal, @"config.user.json");
                if ([fm() fileExistsAtPath:userCfgPath]) {
                    savedUserCfg = [NSData dataWithContentsOfFile:userCfgPath options:0 error:nil];
                }
                if (dir(srcInternal)) {
                    if ([fm() fileExistsAtPath:dstInternal]) {
                        [fm() removeItemAtPath:dstInternal error:nil];
                    }
                    [fm() createDirectoryAtPath:dstInternal withIntermediateDirectories:YES attributes:nil error:nil];
                    NSDirectoryEnumerator *en = [fm() enumeratorAtPath:srcInternal];
                    for (NSString *rel in en) {
                        if (![en.fileAttributes.fileType isEqualToString:NSFileTypeRegular]) continue;
                        NSString *dst = join(dstInternal, rel);
                        [fm() createDirectoryAtPath:dst.stringByDeletingLastPathComponent withIntermediateDirectories:YES attributes:nil error:nil];
                        NSData *d = [NSData dataWithContentsOfFile:join(srcInternal, rel) options:NSDataReadingMappedIfSafe error:nil];
                        if (d) [d writeToFile:dst options:NSDataWritingAtomic error:nil];
                    }
                    if (savedUserCfg.length) {
                        [savedUserCfg writeToFile:userCfgPath options:NSDataWritingAtomic error:nil];
                    }
                }
                [srcHash writeToFile:stampPath atomically:YES encoding:NSUTF8StringEncoding error:nil];
                [notes addObject:[NSString stringWithFormat:@"synced:%@", reason]];
            } else {
                [notes addObject:@"ok"];
            }
        }
        return notes.count ? [notes componentsJoinedByString:@","] : @"ok";
    } @catch (NSException *ex) {
        return [NSString stringWithFormat:@"err:%@", ex.reason ?: @"?"];
    }
}
static void syncBundledMobileControls(NSString *docsPath) {
    if (!docsPath.length) return;
    @try {
        NSString *srcMod = join(join(join(join(NSBundle.mainBundle.bundlePath, @"Frameworks"), @"SDVModGarden.framework"), @"BundledMods"), @"SDVMobileControls");
        if (!dir(srcMod)) return;
        NSString *modsDir = join(docsPath, @"Mods");
        // Respect user explicitly disabling via .SDVMobileControls folder
        if (dir(join(modsDir, @".SDVMobileControls"))) return;
        NSString *dstMod = join(modsDir, @"SDVMobileControls");
        [fm() createDirectoryAtPath:dstMod withIntermediateDirectories:YES attributes:nil error:nil];
        for (NSString *fn in @[@"manifest.json", @"SDVMobileControls.dll"]) {
            NSString *sp = join(srcMod, fn);
            if (![fm() fileExistsAtPath:sp]) continue;
            NSData *sd = [NSData dataWithContentsOfFile:sp options:NSDataReadingMappedIfSafe error:nil];
            if (!sd.length) continue;
            NSString *dp = join(dstMod, fn);
            NSData *dd = [fm() fileExistsAtPath:dp] ? [NSData dataWithContentsOfFile:dp options:NSDataReadingMappedIfSafe error:nil] : nil;
            if (!dd || dd.length != sd.length || ![hashData(sd) isEqualToString:hashData(dd)]) {
                [sd writeToFile:dp options:NSDataWritingAtomic error:nil];
            }
        }
    } @catch (NSException *ex) { }
}

static NSUInteger syncContentPackLocaleFallbacks(NSString *docsPath) {
    if (!docsPath.length) return 0;
    NSUInteger created = 0;
    @try {
        NSMutableArray<NSString *> *modRoots = [NSMutableArray array];
        NSString *m1 = join(docsPath, @"Mods");
        NSString *m2 = join(join(docsPath, @"StardewValley"), @"Mods");
        if (dir(m1)) [modRoots addObject:m1];
        if (dir(m2) && ![m2 isEqualToString:m1]) [modRoots addObject:m2];

        NSSet<NSString *> *assetExts = [NSSet setWithArray:@[@"png", @"xnb", @"json", @"tmx", @"tbin", @"yaml"]];
        NSSet<NSString *> *skipTop = [NSSet setWithArray:@[
            @"sdviostouchcontrols", @"sdvmobilecontrols", @"contentpatcher",
            @"savebackup", @"genericmodconfigmenu", @"consolecommands", @"errorhandler"
        ]];

        for (NSString *modsDir in modRoots) {
            NSDirectoryEnumerator *packEnum = [fm() enumeratorAtPath:modsDir];
            NSString *rel = nil;
            NSMutableArray<NSString *> *cpPackDirs = [NSMutableArray array];
            while ((rel = [packEnum nextObject])) {
                NSArray<NSString *> *parts = rel.pathComponents;
                if (!parts.count) continue;
                NSString *top = parts.firstObject;
                if ([top hasPrefix:@"."] || [skipTop containsObject:top.lowercaseString]) {
                    if (parts.count == 1) [packEnum skipDescendants];
                    continue;
                }
                if (parts.count > 4) continue;
                if (![rel.lastPathComponent.lowercaseString isEqualToString:@"manifest.json"]) continue;
                NSString *packDir = join(modsDir, rel).stringByDeletingLastPathComponent;
                if (![fm() fileExistsAtPath:join(packDir, @"content.json")]) continue;
                [cpPackDirs addObject:packDir];
            }

            for (NSString *packDir in cpPackDirs) { @autoreleasepool {
                NSDictionary *mf = nil;
                @try {
                    id parsed = jsonAt(join(packDir, @"manifest.json"));
                    if ([parsed isKindOfClass:NSDictionary.class]) mf = parsed;
                } @catch (NSException *ex) { continue; }
                if (!mf) continue;

                // Must be a Content Patcher pack
                NSDictionary *cpf = [mf[@"ContentPackFor"] isKindOfClass:NSDictionary.class] ? mf[@"ContentPackFor"] : nil;
                NSString *cpfUid = [cpf[@"UniqueID"] isKindOfClass:NSString.class] ? cpf[@"UniqueID"] : @"";
                if ([cpfUid caseInsensitiveCompare:@"Pathoschild.ContentPatcher"] != NSOrderedSame) continue;

                // Skip Vietnamese translation packs themselves
                NSString *uidStr = [mf[@"UniqueID"] isKindOfClass:NSString.class] ? (NSString *)mf[@"UniqueID"] : @"";
                NSString *nameStr = [mf[@"Name"] isKindOfClass:NSString.class] ? (NSString *)mf[@"Name"] : @"";
                NSString *uidLow = uidStr.lowercaseString;
                NSString *nameLow = nameStr.lowercaseString;
                if ([uidLow containsString:@"viethoa"] || [uidLow containsString:@"vietnam"] ||
                    [nameLow containsString:@"việt hóa"] || [nameLow containsString:@"vietnamese"] ||
                    [nameLow containsString:@"viethoa"]) continue;

                // Check if content.json (or any included json inside the pack) uses {{Language}} token
                BOOL usesLanguageToken = NO;
                NSMutableArray<NSString *> *packFiles = [NSMutableArray array];
                NSDirectoryEnumerator *fe = [fm() enumeratorAtPath:packDir];
                NSString *fRel = nil;
                while ((fRel = [fe nextObject])) {
                    NSString *full = join(packDir, fRel);
                    BOOL isD = NO;
                    if (![fm() fileExistsAtPath:full isDirectory:&isD] || isD) continue;
                    [packFiles addObject:fRel];
                    if (!usesLanguageToken && [fRel.pathExtension.lowercaseString isEqualToString:@"json"]) {
                        NSString *topSub = fRel.pathComponents.firstObject.lowercaseString;
                        if ([topSub isEqualToString:@"i18n"]) continue;
                        NSData *jd = [NSData dataWithContentsOfFile:full options:NSDataReadingMappedIfSafe error:nil];
                        if (jd.length > 0 && jd.length <= 1024 * 1024) {
                            NSString *js = [[NSString alloc] initWithData:jd encoding:NSUTF8StringEncoding];
                            if (js && [js rangeOfString:@"{{language}}" options:NSCaseInsensitiveSearch].location != NSNotFound) {
                                usesLanguageToken = YES;
                            }
                        }
                    }
                }
                if (!usesLanguageToken) continue;

                // Create 0-byte APFS hardlinks (*.en.<ext> -> *.vi.<ext>, *_en.<ext> -> *_vi.<ext>, en/ -> vi/)
                // Never overwrites any existing Vietnamese asset!
                for (NSString *fRel in packFiles) { @autoreleasepool {
                    NSString *ext = fRel.pathExtension;
                    if (!ext.length || ![assetExts containsObject:ext.lowercaseString]) continue;
                    NSString *topSub = fRel.pathComponents.firstObject.lowercaseString;
                    if ([topSub isEqualToString:@"i18n"]) continue;

                    NSString *baseName = fRel.lastPathComponent;
                    NSString *stem = [baseName stringByDeletingPathExtension];
                    NSString *dirRel = [fRel stringByDeletingLastPathComponent];
                    NSString *srcFull = join(packDir, fRel);
                    NSString *dstRel = nil;

                    if (stem.length > 3 && [stem.lowercaseString hasSuffix:@".en"]) {
                        NSString *newBase = [NSString stringWithFormat:@"%@.vi.%@", [stem substringToIndex:stem.length - 3], ext];
                        dstRel = dirRel.length ? join(dirRel, newBase) : newBase;
                    } else if (stem.length > 3 && [stem.lowercaseString hasSuffix:@"_en"]) {
                        NSString *newBase = [NSString stringWithFormat:@"%@_vi.%@", [stem substringToIndex:stem.length - 3], ext];
                        dstRel = dirRel.length ? join(dirRel, newBase) : newBase;
                    } else {
                        NSArray<NSString *> *segs = fRel.pathComponents;
                        if (segs.count >= 2) {
                            NSMutableArray<NSString *> *mSegs = [segs mutableCopy];
                            BOOL matchedSeg = NO;
                            for (NSUInteger si = 0; si + 1 < mSegs.count; si++) {
                                if ([mSegs[si].lowercaseString isEqualToString:@"en"]) {
                                    mSegs[si] = @"vi";
                                    matchedSeg = YES;
                                }
                            }
                            if (matchedSeg) dstRel = [NSString pathWithComponents:mSegs];
                        }
                    }

                    if (!dstRel.length || [dstRel isEqualToString:fRel]) continue;
                    NSString *dstFull = join(packDir, dstRel);
                    if ([fm() fileExistsAtPath:dstFull]) continue;

                    [fm() createDirectoryAtPath:dstFull.stringByDeletingLastPathComponent withIntermediateDirectories:YES attributes:nil error:nil];
                    if ([fm() linkItemAtPath:srcFull toPath:dstFull error:nil] ||
                        [fm() copyItemAtPath:srcFull toPath:dstFull error:nil]) {
                        created++;
                    }
                }}
            }}
        }
    } @catch (NSException *ex) { }
    return created;
}

static NSDictionary *gameDataStatus(NSString *docsPath) {
    if (!docsPath.length) {
        return @{@"hasSdvDll": @NO, @"hasContent": @NO, @"hasSmapi": @NO, @"hasSmapiInternal": @NO, @"ready": @NO, @"gameDir": @""};
    }
    NSString *sub = join(docsPath, @"StardewValley");
    NSString *gameDir = ([fm() fileExistsAtPath:join(docsPath, @"Stardew Valley.dll")] || ![fm() fileExistsAtPath:join(sub, @"Stardew Valley.dll")]) ? docsPath : sub;
    BOOL hasSdv = [fm() fileExistsAtPath:join(gameDir, @"Stardew Valley.dll")];
    NSString *contentDir = join(gameDir, @"Content");
    BOOL hasContent = dir(contentDir) && ([fm() contentsOfDirectoryAtPath:contentDir error:nil].count > 0);
    BOOL hasSmapi = [fm() fileExistsAtPath:join(gameDir, @"StardewModdingAPI.dll")] || [fm() fileExistsAtPath:join(docsPath, @"StardewModdingAPI.dll")];
    NSString *si1 = join(gameDir, @"smapi-internal");
    NSString *si2 = join(docsPath, @"smapi-internal");
    BOOL hasSi = (dir(si1) && [fm() contentsOfDirectoryAtPath:si1 error:nil].count > 0) ||
                 (dir(si2) && [fm() contentsOfDirectoryAtPath:si2 error:nil].count > 0);
    return @{
        @"hasSdvDll": @(hasSdv),
        @"hasContent": @(hasContent),
        @"hasSmapi": @(hasSmapi),
        @"hasSmapiInternal": @(hasSi),
        @"ready": @(hasSdv && hasContent),
        @"gameDir": gameDir
    };
}

static NSString *sanitizeFolderName(NSString *raw, NSString *fallback);

static BOOL sdvLooksLikeValidCjkName(NSString *s) {
    if (!s.length) return NO;
    NSUInteger cjkCount = 0;
    for (NSUInteger i = 0; i < s.length; i++) {
        unichar ch = [s characterAtIndex:i];
        if (ch == 0xFFFD || ch < 0x20 || (ch >= 0xE000 && ch <= 0xF8FF)) return NO;
        if ((ch >= 0x4E00 && ch <= 0x9FFF) ||   // CJK Unified Ideographs
            (ch >= 0x3400 && ch <= 0x4DBF) ||   // CJK Extension A
            (ch >= 0x3040 && ch <= 0x30FF) ||   // Hiragana / Katakana
            (ch >= 0xAC00 && ch <= 0xD7AF)) {   // Hangul Syllables
            cjkCount++;
        }
    }
    return cjkCount > 0;
}

static NSString *decodeZipEntryName(const char *bytes, unsigned len) {
    if (!bytes || len == 0) return nil;
    NSString *r = [[NSString alloc] initWithBytes:bytes length:len encoding:NSUTF8StringEncoding];
    if (r.length) return r;

    // Detect consecutive multi-byte non-ASCII runs (Chinese GB18030/Big5, Japanese ShiftJIS, Korean EUC-KR)
    // BEFORE falling back to single-byte CP1258/CP1252 (which accept any byte and garble CJK mod folder names!).
    unsigned highPairs = 0;
    const unsigned char *ub = (const unsigned char *)bytes;
    for (unsigned i = 0; i + 1 < len; i++) {
        if (ub[i] >= 0x80 && ub[i + 1] >= 0x80) {
            highPairs++;
            i++;
        }
    }
    if (highPairs >= 2) {
        NSStringEncoding cjkEncs[] = {
            CFStringConvertEncodingToNSStringEncoding(kCFStringEncodingGB_18030_2000),
            NSShiftJISStringEncoding,
            CFStringConvertEncodingToNSStringEncoding(kCFStringEncodingEUC_KR),
            CFStringConvertEncodingToNSStringEncoding(kCFStringEncodingBig5)
        };
        for (size_t k = 0; k < sizeof(cjkEncs) / sizeof(cjkEncs[0]); k++) {
            r = [[NSString alloc] initWithBytes:bytes length:len encoding:cjkEncs[k]];
            if (sdvLooksLikeValidCjkName(r)) return r;
        }
    }

    // Fallback for Windows-packed Vietnamese / Western ZIP archives (CP1258 -> CP1252 -> CP437 -> Latin1)
    NSStringEncoding encs[] = {
        CFStringConvertEncodingToNSStringEncoding(kCFStringEncodingWindowsVietnamese),
        NSWindowsCP1252StringEncoding,
        CFStringConvertEncodingToNSStringEncoding(kCFStringEncodingDOSLatinUS),
        NSISOLatin1StringEncoding
    };
    for (size_t k = 0; k < sizeof(encs) / sizeof(encs[0]); k++) {
        r = [[NSString alloc] initWithBytes:bytes length:len encoding:encs[k]];
        if (r.length) return r;
    }
    return nil;
}

static NSString *clean(NSString *path) {
    if (![path isKindOfClass:NSString.class] || !path.length || path.length > 1400) fail(@"Đường dẫn tệp trống hoặc quá dài.");
    NSString *norm = [path stringByReplacingOccurrencesOfString:@"\\" withString:@"/"];
    NSMutableArray<NSString *> *outParts = [NSMutableArray array];
    for (NSString *rawSeg in [norm componentsSeparatedByString:@"/"]) {
        if (!rawSeg.length || [rawSeg isEqualToString:@"."]) continue;
        if ([rawSeg isEqualToString:@".."] || [rawSeg rangeOfCharacterFromSet:NSCharacterSet.controlCharacterSet].location != NSNotFound) {
            fail(@"Gói chứa đường dẫn không hợp lệ.");
        }
        // Replace stray colons in archive names (Windows/macOS zip edge cases) safely instead of aborting
        NSString *seg = [rawSeg stringByReplacingOccurrencesOfString:@":" withString:@"_"];
        [outParts addObject:seg];
    }
    if (!outParts.count) fail(@"Đường dẫn tệp trống.");
    return [outParts componentsJoinedByString:@"/"];
}
static NSString *inside(NSString *root, NSString *rel) {
    NSString *p=root;
    for(NSString *s in [clean(rel) componentsSeparatedByString:@"/"]) {
        p=join(p,s);
        if([[[fm() attributesOfItemAtPath:p error:nil] fileType] isEqual:NSFileTypeSymbolicLink]) fail(@"Đường dẫn chứa liên kết tượng trưng.");
    }
    return p;
}
static NSArray *files(NSString *p) {
    NSMutableArray *a=[NSMutableArray array]; NSDirectoryEnumerator *en=[fm() enumeratorAtPath:p];
    for(NSString *r in en){ NSString *type=en.fileAttributes.fileType; if([type isEqual:NSFileTypeRegular]) [a addObject:r]; }
    return [a sortedArrayUsingSelector:@selector(compare:)];
}

static BOOL isJunkArchiveEntry(NSString *r) {
    if (!r.length) return YES;
    if ([r.pathComponents containsObject:@"__MACOSX"]) return YES;
    NSString *lastLow = r.lastPathComponent.lowercaseString;
    return [lastLow isEqualToString:@".ds_store"] || [lastLow isEqualToString:@"thumbs.db"] || [lastLow isEqualToString:@"desktop.ini"];
}

static void copyModDirectoryRecursive(NSString *srcDir, NSString *dstDir) {
    mkdirs(dstDir);
    NSDirectoryEnumerator *en = [fm() enumeratorAtPath:srcDir];
    uint64_t total = 0;
    NSUInteger count = 0;
    for (NSString *rel in en) {
        @autoreleasepool {
            NSString *type = en.fileAttributes.fileType;
            if (![type isEqualToString:NSFileTypeRegular]) continue;
            if (isJunkArchiveEntry(rel)) continue;
            if (++count > 40000) fail(@"Thư mục mod vượt quá 40.000 tệp.");
            NSString *srcFile = join(srcDir, rel);
            unsigned long long sz = [en.fileAttributes fileSize];
            if (sz > 1024ULL * 1024 * 1024 || (total += sz) > 4ULL * 1024 * 1024 * 1024) {
                fail(@"Thư mục mod vượt giới hạn dung lượng (4 GB).");
            }
            NSString *dstFile = inside(dstDir, rel);
            mkdirs(dstFile.stringByDeletingLastPathComponent);
            NSError *e = nil;
            if ([fm() fileExistsAtPath:dstFile]) [fm() removeItemAtPath:dstFile error:nil];
            BOOL ok = [fm() copyItemAtPath:srcFile toPath:dstFile error:&e];
            check(ok, e);
        }
    }
}

static BOOL isNestedArchivePath(NSString *path) {
    NSString *low = path.lastPathComponent.lowercaseString;
    if ([low hasSuffix:@".tar.gz"] || [low hasSuffix:@".tar.bz2"] || [low hasSuffix:@".tar.xz"] ||
        [low hasSuffix:@".tar.zst"] || [low hasSuffix:@".tar.lz4"]) {
        return YES;
    }
    NSString *ext = path.pathExtension.lowercaseString;
    static NSSet<NSString *> *sArchExts = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        sArchExts = [NSSet setWithArray:@[
            @"zip", @"rar", @"7z", @"tar", @"tgz", @"tbz2", @"txz", @"tzst", @"tlz4",
            @"cab", @"xar", @"cpio", @"lha", @"lzh", @"iso"
        ]];
    });
    return [sArchExts containsObject:ext];
}

// Dynamic bridge to iOS system /usr/lib/libarchive.2.dylib (supports .RAR [RAR4/RAR5], .7Z, .ZIP [Deflate64/LZMA/BZip2/Zstd], .TAR.GZ/.BZ2/.XZ, .CAB, .XAR, .ISO...)
typedef struct {
    BOOL ready;
    void *(*read_new)(void);
    int (*support_filter_all)(void *);
    int (*support_format_all)(void *);
    int (*open_filename)(void *, const char *, size_t);
    int (*next_header)(void *, void **);
    ssize_t (*read_data)(void *, void *, size_t);
    int (*data_skip)(void *);
    int (*read_free)(void *);
    const char *(*error_string)(void *);
    const char *(*entry_pathname)(void *);
    const char *(*entry_pathname_utf8)(void *);
    const wchar_t *(*entry_pathname_w)(void *);
    unsigned int (*entry_filetype)(void *);
    int (*entry_is_encrypted)(void *);
} SDVLibArchiveAPI;

static const SDVLibArchiveAPI *sdvLibArchive(void) {
    static SDVLibArchiveAPI api = {0};
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        void *h = dlopen("/usr/lib/libarchive.2.dylib", RTLD_LAZY);
        if (!h) return;
        api.read_new            = (void *(*)(void))dlsym(h, "archive_read_new");
        api.support_filter_all  = (int (*)(void *))dlsym(h, "archive_read_support_filter_all");
        api.support_format_all  = (int (*)(void *))dlsym(h, "archive_read_support_format_all");
        api.open_filename       = (int (*)(void *, const char *, size_t))dlsym(h, "archive_read_open_filename");
        api.next_header         = (int (*)(void *, void **))dlsym(h, "archive_read_next_header");
        api.read_data           = (ssize_t (*)(void *, void *, size_t))dlsym(h, "archive_read_data");
        api.data_skip           = (int (*)(void *))dlsym(h, "archive_read_data_skip");
        api.read_free           = (int (*)(void *))dlsym(h, "archive_read_free");
        api.error_string        = (const char *(*)(void *))dlsym(h, "archive_error_string");
        api.entry_pathname      = (const char *(*)(void *))dlsym(h, "archive_entry_pathname");
        api.entry_pathname_utf8 = (const char *(*)(void *))dlsym(h, "archive_entry_pathname_utf8");
        api.entry_pathname_w    = (const wchar_t *(*)(void *))dlsym(h, "archive_entry_pathname_w");
        api.entry_filetype      = (unsigned int (*)(void *))dlsym(h, "archive_entry_filetype");
        api.entry_is_encrypted  = (int (*)(void *))dlsym(h, "archive_entry_is_encrypted");
        api.ready = (api.read_new && api.support_filter_all && api.support_format_all &&
                     api.open_filename && api.next_header && api.read_data && api.read_free);
    });
    return &api;
}

static NSString *decodeLibArchiveEntryName(const SDVLibArchiveAPI *api, void *entry) {
    if (!api || !entry) return nil;
    if (api->entry_pathname_utf8) {
        const char *u8 = api->entry_pathname_utf8(entry);
        if (u8 && u8[0]) {
            NSString *s = [NSString stringWithUTF8String:u8];
            if (s.length) return s;
        }
    }
    if (api->entry_pathname) {
        const char *raw = api->entry_pathname(entry);
        if (raw && raw[0]) {
            NSString *s = decodeZipEntryName(raw, (unsigned)strlen(raw));
            if (s.length) return s;
        }
    }
    if (api->entry_pathname_w) {
        const wchar_t *w = api->entry_pathname_w(entry);
        if (w && w[0]) {
            size_t wlen = wcslen(w);
            NSString *s = [[NSString alloc] initWithBytes:w length:wlen * sizeof(wchar_t) encoding:NSUTF32LittleEndianStringEncoding];
            if (s.length) return s;
        }
    }
    return nil;
}

static void extractArchiveFileToDir(NSString *archivePath, NSString *displayName, NSString *tmp, int depth);

static BOOL extractWithLibArchive(NSString *archivePath, NSString *displayName, NSString *tmp, int depth, NSMutableArray<NSString *> *nestedArchives, NSString **outErr) {
    const SDVLibArchiveAPI *api = sdvLibArchive();
    if (!api->ready) {
        if (outErr) *outErr = @"Không tải được bộ giải nén hệ thống iOS (libarchive).";
        return NO;
    }
    void *a = api->read_new();
    if (!a) {
        if (outErr) *outErr = @"Không khởi tạo được bộ giải nén.";
        return NO;
    }
    api->support_filter_all(a);
    api->support_format_all(a);

    if (api->open_filename(a, archivePath.fileSystemRepresentation, 65536) != 0) {
        const char *es = api->error_string ? api->error_string(a) : NULL;
        if (outErr) *outErr = [NSString stringWithFormat:@"Không mở được tệp nén '%@' (%s).", displayName, es ?: "tệp hỏng hoặc định dạng không hỗ trợ"];
        api->read_free(a);
        return NO;
    }

    uint64_t total = 0;
    unsigned count = 0;
    unsigned extracted = 0;
    uint8_t *buf = (uint8_t *)malloc(65536);
    if (!buf) {
        api->read_free(a);
        if (outErr) *outErr = @"Thiếu bộ nhớ tạm khi giải nén.";
        return NO;
    }

    BOOL ok = YES;
    void *entry = NULL;
    for (;;) {
        int rc = api->next_header(a, &entry);
        if (rc == 1) break; // ARCHIVE_EOF
        if (rc < 0 && rc != -20) { // allow ARCHIVE_WARN (-20)
            const char *es = api->error_string ? api->error_string(a) : NULL;
            if (outErr) *outErr = [NSString stringWithFormat:@"Tệp nén '%@' không đọc được header (%s).", displayName, es ?: "dữ liệu lỗi"];
            ok = NO;
            break;
        }
        if (++count > 40000) {
            if (outErr) *outErr = @"Gói nén vượt quá 40.000 mục.";
            ok = NO;
            break;
        }
        @autoreleasepool {
            NSString *rawName = decodeLibArchiveEntryName(api, entry);
            if (!rawName.length) {
                if (api->data_skip) api->data_skip(a);
                continue;
            }
            rawName = [rawName stringByReplacingOccurrencesOfString:@"\\" withString:@"/"];
            unsigned int ft = api->entry_filetype ? (api->entry_filetype(entry) & 0170000) : 0;
            // Skip directories (0040000), symlinks (0120000), and OS metadata junk
            if (ft == 0040000 || ft == 0120000 || [rawName hasSuffix:@"/"] || isJunkArchiveEntry(rawName)) {
                if (api->data_skip) api->data_skip(a);
                continue;
            }
            if (api->entry_is_encrypted && api->entry_is_encrypted(entry)) {
                if (outErr) *outErr = [NSString stringWithFormat:@"Tệp nén '%@' có đặt mật khẩu bảo vệ (mục '%@'). Hãy giải nén bằng mật khẩu trước rồi chọn thư mục Mod!", displayName, rawName.lastPathComponent];
                ok = NO;
                break;
            }
            NSString *r = nil;
            NSString *p = nil;
            @try {
                r = clean(rawName);
                p = inside(tmp, r);
                mkdirs(p.stringByDeletingLastPathComponent);
            } @catch (NSException *ex) {
                if (outErr) *outErr = ex.reason ?: @"Đường dẫn trong gói nén không hợp lệ.";
                ok = NO;
                break;
            }

            FILE *fp = fopen(p.fileSystemRepresentation, "wb");
            if (!fp) {
                if (outErr) *outErr = [NSString stringWithFormat:@"Không tạo được tệp '%@'.", r.lastPathComponent];
                ok = NO;
                break;
            }

            BOOL entryOk = YES;
            for (;;) {
                ssize_t rd = api->read_data(a, buf, 65536);
                if (rd == 0) break;
                if (rd < 0) {
                    const char *es = api->error_string ? api->error_string(a) : NULL;
                    BOOL isRar = [displayName.lowercaseString hasSuffix:@".rar"];
                    NSString *hint = isRar
                        ? @" (Gợi ý: iOS cũ có thể không hỗ trợ chuẩn nén RAR5 mới của WinRAR 6/7 — hãy đổi sang tệp .ZIP hoặc giải nén sẵn thành thư mục rồi chọn!)"
                        : @"";
                    if (outErr) *outErr = [NSString stringWithFormat:@"Không giải nén được '%@' trong '%@' (%s).%@", r.lastPathComponent, displayName, es ?: "chuẩn nén đặc biệt hoặc có mật khẩu", hint];
                    entryOk = NO;
                    break;
                }
                total += (uint64_t)rd;
                if (total > 4ULL * 1024 * 1024 * 1024) {
                    if (outErr) *outErr = @"Gói vượt giới hạn giải nén (4 GB).";
                    entryOk = NO;
                    break;
                }
                if (fwrite(buf, 1, (size_t)rd, fp) != (size_t)rd) {
                    if (outErr) *outErr = @"Không ghi được tệp khi giải nén (kiểm tra dung lượng trống trên iPhone).";
                    entryOk = NO;
                    break;
                }
            }
            fclose(fp);
            if (!entryOk) {
                [fm() removeItemAtPath:p error:nil];
                ok = NO;
                break;
            }
            extracted++;
            if (depth < 2 && isNestedArchivePath(r)) {
                [nestedArchives addObject:p];
            }
        }
    }

    free(buf);
    api->read_free(a);
    if (ok && extracted == 0 && count == 0) {
        if (outErr) *outErr = [NSString stringWithFormat:@"Tệp nén '%@' trống hoặc không chứa tệp hợp lệ.", displayName];
        return NO;
    }
    return ok;
}

static void unpackNestedArchives(NSArray<NSString *> *nestedArchives, int depth) {
    if (depth >= 2 || !nestedArchives.count) return;
    for (NSString *na in nestedArchives) {
        @try {
            NSString *baseName = [na.lastPathComponent stringByDeletingPathExtension];
            if ([baseName.lowercaseString hasSuffix:@".tar"]) {
                baseName = [baseName stringByDeletingPathExtension];
            }
            NSString *subOut = join(na.stringByDeletingLastPathComponent, sanitizeFolderName(baseName, @"SubMod"));
            mkdirs(subOut);
            extractArchiveFileToDir(na, na.lastPathComponent, subOut, depth + 1);
            [fm() removeItemAtPath:na error:nil];
        } @catch (NSException *ignored) { }
    }
}

static void extractArchiveFileToDir(NSString *archivePath, NSString *displayName, NSString *tmp, int depth) {
    if (depth > 2) return;
    NSFileHandle *fh = [NSFileHandle fileHandleForReadingAtPath:archivePath];
    NSData *hdr = fh ? [fh readDataOfLength:8] : nil;
    [fh closeFile];
    const unsigned char *hb = (const unsigned char *)hdr.bytes;
    NSUInteger hl = hdr.length;
    BOOL isZipMagic = (hl >= 4 && hb[0] == 'P' && hb[1] == 'K' && (hb[2] == 3 || hb[2] == 5 || hb[2] == 7));

    NSMutableArray<NSString *> *nestedArchives = [NSMutableArray array];

    // Fast path for standard Deflate/Store ZIP via miniz; automatically falls back to iOS system libarchive
    // if ZIP uses Deflate64/LZMA/BZip2/Zstd or streaming descriptors, AND handles .RAR / .7Z / .TAR.GZ / .TAR.XZ / .CAB / .ISO directly!
    if (isZipMagic) {
        mz_zip_archive z = {0};
        if (mz_zip_reader_init_file(&z, archivePath.fileSystemRepresentation, 0)) {
            BOOL fallbackToLibArchive = NO;
            @try {
                uint64_t total = 0;
                unsigned count = mz_zip_reader_get_num_files(&z);
                if (count > 40000) fail(@"Gói vượt 40.000 mục.");
                for (unsigned i = 0; i < count; i++) { @autoreleasepool {
                    mz_zip_archive_file_stat st;
                    if (!mz_zip_reader_file_stat(&z, i, &st)) { fallbackToLibArchive = YES; break; }
                    if (st.m_is_directory) continue;
                    unsigned n = mz_zip_reader_get_filename(&z, i, NULL, 0);
                    if (n > 4096 || n < 2) continue;
                    char name[4096];
                    mz_zip_reader_get_filename(&z, i, name, n);
                    NSString *rawName = decodeZipEntryName(name, n - 1);
                    if (!rawName.length) continue;
                    rawName = [rawName stringByReplacingOccurrencesOfString:@"\\" withString:@"/"];
                    if (isJunkArchiveEntry(rawName)) continue;
                    if (((st.m_external_attr >> 16) & 0170000) == 0120000) continue;
                    if (st.m_is_encrypted) {
                        fail([NSString stringWithFormat:@"Tệp nén '%@' có đặt mật khẩu bảo vệ (mục '%@'). Hãy giải nén bằng mật khẩu trước rồi chọn thư mục Mod!", displayName, rawName.lastPathComponent]);
                    }
                    if (!st.m_is_supported) {
                        // e.g. Deflate64 / LZMA / BZip2 / Zstd inside .zip -> let libarchive handle it!
                        fallbackToLibArchive = YES;
                        break;
                    }
                    if (st.m_uncomp_size > 1024ULL * 1024 * 1024 || (total += st.m_uncomp_size) > 4ULL * 1024 * 1024 * 1024) {
                        fail(@"Gói vượt giới hạn giải nén (4 GB).");
                    }
                    NSString *r = clean(rawName);
                    NSString *p = inside(tmp, r);
                    mkdirs(p.stringByDeletingLastPathComponent);
                    if (!mz_zip_reader_extract_to_file(&z, i, p.fileSystemRepresentation, 0)) {
                        fallbackToLibArchive = YES;
                        break;
                    }
                    if (depth < 2 && isNestedArchivePath(r)) {
                        [nestedArchives addObject:p];
                    }
                }}
            } @finally {
                mz_zip_reader_end(&z);
            }
            if (!fallbackToLibArchive) {
                unpackNestedArchives(nestedArchives, depth);
                return;
            }
            [nestedArchives removeAllObjects];
        }
    }

    NSString *laErr = nil;
    if (!extractWithLibArchive(archivePath, displayName, tmp, depth, nestedArchives, &laErr)) {
        fail(laErr ?: [NSString stringWithFormat:@"Không giải nén được '%@'.", displayName]);
    }
    unpackNestedArchives(nestedArchives, depth);
}

static NSString *unpack(NSURL *url, NSString *tmp) {
    mkdirs(tmp);
    BOOL isDir = NO;
    if ([fm() fileExistsAtPath:url.path isDirectory:&isDir] && isDir) {
        NSString *leaf = sanitizeFolderName(url.lastPathComponent, @"ModFolder");
        copyModDirectoryRecursive(url.path, join(tmp, leaf));
        return tmp;
    }

    NSFileHandle *fh = [NSFileHandle fileHandleForReadingAtPath:url.path];
    NSData *hdr = fh ? [fh readDataOfLength:8] : nil;
    [fh closeFile];
    const unsigned char *hb = (const unsigned char *)hdr.bytes;
    NSUInteger hl = hdr.length;

    BOOL isXnbMagic = (hl >= 3 && hb[0] == 'X' && hb[1] == 'N' && hb[2] == 'B');
    NSString *ext = url.pathExtension.lowercaseString;

    // Loose direct mod/content asset (.xnb, .json, .dll, .png, .tbin, .tmxl)
    if (isXnbMagic || [ext isEqualToString:@"xnb"] || [ext isEqualToString:@"json"] ||
        [ext isEqualToString:@"dll"] || [ext isEqualToString:@"png"] || [ext isEqualToString:@"tbin"]) {
        writeFile(readFile(url.path), inside(tmp, url.lastPathComponent));
        return tmp;
    }

    // Extract ANY archive format supported by iOS (.ZIP, .RAR, .7Z, .TAR.GZ, .TGZ, .TAR.BZ2, .TAR.XZ, .TAR, .CAB, .XAR, .ISO...)
    extractArchiveFileToDir(url.path, url.lastPathComponent ?: @"mod_archive", tmp, 0);
    return tmp;
}

static NSUInteger importGameDataWithLibArchive(NSURL *url, NSString *docsPath) {
    const SDVLibArchiveAPI *api = sdvLibArchive();
    if (!api->ready) fail(@"Không tải được bộ giải nén hệ thống iOS (libarchive).");

    // Pass 1: Detect wrapper folder prefix containing Stardew Valley.dll / Content/ / StardewModdingAPI.dll
    NSString *bestPrefix = nil;
    NSInteger bestScore = -1;
    {
        void *a = api->read_new();
        api->support_filter_all(a);
        api->support_format_all(a);
        if (api->open_filename(a, url.fileSystemRepresentation, 65536) != 0) {
            const char *es = api->error_string ? api->error_string(a) : NULL;
            NSString *msg = [NSString stringWithFormat:@"Không mở được gói dữ liệu '%@' (%s).", url.lastPathComponent ?: @"archive", es ?: "định dạng không hỗ trợ"];
            api->read_free(a);
            fail(msg);
        }
        void *entry = NULL;
        unsigned count = 0;
        for (;;) {
            int rc = api->next_header(a, &entry);
            if (rc == 1) break;
            if (rc < 0 && rc != -20) break;
            if (++count > 40000) { api->read_free(a); fail(@"Gói vượt quá 40.000 mục."); }
            @autoreleasepool {
                NSString *raw = decodeLibArchiveEntryName(api, entry);
                if (api->data_skip) api->data_skip(a);
                if (!raw.length) continue;
                raw = [raw stringByReplacingOccurrencesOfString:@"\\" withString:@"/"];
                if (isJunkArchiveEntry(raw) || [raw hasSuffix:@"/"]) continue;
                NSArray<NSString *> *parts = raw.pathComponents;
                NSString *lastLow = raw.lastPathComponent.lowercaseString;
                if ([lastLow isEqualToString:@"stardew valley.dll"]) {
                    NSString *pref = (parts.count > 1) ? [[NSString pathWithComponents:[parts subarrayWithRange:NSMakeRange(0, parts.count - 1)]] stringByAppendingString:@"/"] : @"";
                    if (bestScore < 100 || (bestScore == 100 && pref.length < bestPrefix.length)) { bestScore = 100; bestPrefix = pref; }
                } else if ([lastLow isEqualToString:@"stardewmoddingapi.dll"]) {
                    NSString *pref = (parts.count > 1) ? [[NSString pathWithComponents:[parts subarrayWithRange:NSMakeRange(0, parts.count - 1)]] stringByAppendingString:@"/"] : @"";
                    if (bestScore < 80) { bestScore = 80; bestPrefix = pref; }
                } else {
                    for (NSUInteger k = 0; k + 1 < parts.count; k++) {
                        NSString *seg = parts[k].lowercaseString;
                        if ([seg isEqualToString:@"content"] || [seg isEqualToString:@"smapi-internal"]) {
                            NSInteger sc = [seg isEqualToString:@"content"] ? 90 : 70;
                            NSString *pref = (k > 0) ? [[NSString pathWithComponents:[parts subarrayWithRange:NSMakeRange(0, k)]] stringByAppendingString:@"/"] : @"";
                            if (bestScore < sc || (bestScore == sc && pref.length < bestPrefix.length)) { bestScore = sc; bestPrefix = pref; }
                            break;
                        }
                    }
                }
            }
        }
        api->read_free(a);
    }

    if (bestScore < 0 || bestPrefix == nil) {
        fail(@"Gói nén này không chứa dữ liệu game PC (Stardew Valley.dll hoặc Content/). Nếu đây là file mod, hãy dùng nút Chọn tệp Mod!");
    }
    if (bestScore < 90) {
        fail(@"SMAPI 4.5.2 bản iOS đã tích hợp sẵn trong Garden Launcher — bạn không cần nhập gói SMAPI từ PC! Hãy chọn gói dữ liệu game PC chứa Stardew Valley.dll và thư mục Content/.");
    }

    // Pass 2: Stream-extract into Documents/
    mkdirs(docsPath);
    mkdirs(join(docsPath, @"Mods"));
    mkdirs(join(docsPath, @"Saves"));
    mkdirs(join(docsPath, @"ErrorLogs"));

    NSMutableSet<NSString *> *bundleRuntimeFiles = [NSMutableSet set];
    for (NSString *bf in ([fm() contentsOfDirectoryAtPath:NSBundle.mainBundle.bundlePath error:nil] ?: @[])) {
        if ([bf.pathExtension.lowercaseString isEqualToString:@"dll"]) [bundleRuntimeFiles addObject:bf.lowercaseString];
    }
    NSSet<NSString *> *desktopJunk = [NSSet setWithArray:@[
        @"coreclr.dll", @"clrjit.dll", @"clrgc.dll", @"clretwrc.dll",
        @"hostfxr.dll", @"hostpolicy.dll", @"ucrtbase.dll", @"msquic.dll",
        @"mscordaccore.dll", @"mscordbi.dll", @"mscorrc.dll", @"dbgshim.dll",
        @"steamworks.net.dll", @"galaxycsharp.dll", @"galaxy64.dll", @"galaxycsharpglue.dll",
        @"steam_api64.dll", @"steam_api64.cdx", @"steam_emu.ini",
        @"sdl2.dll", @"faudio.dll", @"soft_oal.dll", @"libskiasharp.dll",
        @"liblwjgl_lz4.dll", @"stb_png_writer.dll", @"system.io.compression.native.dll",
        @"system.private.corelib.dll"
    ]];

    void *a = api->read_new();
    api->support_filter_all(a);
    api->support_format_all(a);
    if (api->open_filename(a, url.fileSystemRepresentation, 65536) != 0) {
        api->read_free(a);
        fail(@"Không mở lại được gói dữ liệu để giải nén.");
    }
    uint8_t *buf = (uint8_t *)malloc(65536);
    uint64_t totalBytes = 0;
    NSUInteger written = 0;
    void *entry = NULL;
    @try {
        for (;;) {
            int rc = api->next_header(a, &entry);
            if (rc == 1) break;
            if (rc < 0 && rc != -20) break;
            @autoreleasepool {
                NSString *raw = decodeLibArchiveEntryName(api, entry);
                unsigned int ft = api->entry_filetype ? (api->entry_filetype(entry) & 0170000) : 0;
                if (!raw.length || ft == 0040000 || ft == 0120000 || (api->entry_is_encrypted && api->entry_is_encrypted(entry))) {
                    if (api->data_skip) api->data_skip(a);
                    continue;
                }
                raw = [raw stringByReplacingOccurrencesOfString:@"\\" withString:@"/"];
                if (isJunkArchiveEntry(raw) || [raw hasSuffix:@"/"]) {
                    if (api->data_skip) api->data_skip(a);
                    continue;
                }
                NSString *rel = raw;
                if (bestPrefix.length) {
                    if (![rel hasPrefix:bestPrefix]) {
                        if (api->data_skip) api->data_skip(a);
                        continue;
                    }
                    rel = [rel substringFromIndex:bestPrefix.length];
                }
                while ([rel hasPrefix:@"/"]) rel = [rel substringFromIndex:1];
                if (!rel.length) {
                    if (api->data_skip) api->data_skip(a);
                    continue;
                }
                rel = clean(rel);
                NSArray<NSString *> *relParts = rel.pathComponents;
                NSString *topLow = relParts.firstObject.lowercaseString;
                NSString *fileLow = rel.lastPathComponent.lowercaseString;
                NSString *extLow = rel.pathExtension.lowercaseString;

                if ([topLow isEqualToString:@"saves"] ||
                    [topLow isEqualToString:@"smapi-internal"] ||
                    ([topLow isEqualToString:@"mods"] && relParts.count >= 2 &&
                     ([relParts[1] caseInsensitiveCompare:@"SDViOSTouchControls"] == NSOrderedSame ||
                      [relParts[1] caseInsensitiveCompare:@"SDVMobileControls"] == NSOrderedSame)) ||
                    (relParts.count == 1 && ([fileLow hasPrefix:@"stardewmoddingapi"] || [extLow isEqualToString:@"exe"] || [extLow isEqualToString:@"so"] || [extLow isEqualToString:@"dylib"] || [extLow isEqualToString:@"pdb"] || [fileLow containsString:@".so."] || [fileLow hasPrefix:@"api-ms-win-"] || [fileLow hasPrefix:@"mscordaccore_"] || [fileLow hasPrefix:@"microsoft.diasymreader"] || [desktopJunk containsObject:fileLow] || [bundleRuntimeFiles containsObject:fileLow]))) {
                    if (api->data_skip) api->data_skip(a);
                    continue;
                }

                NSString *dst = inside(docsPath, rel);
                mkdirs(dst.stringByDeletingLastPathComponent);
                FILE *fp = fopen(dst.fileSystemRepresentation, "wb");
                if (!fp) fail(@"Không tạo được tệp dữ liệu game.");
                for (;;) {
                    ssize_t rd = api->read_data(a, buf, 65536);
                    if (rd == 0) break;
                    if (rd < 0) { fclose(fp); fail(@"Giải nén dữ liệu game thất bại."); }
                    if ((totalBytes += (uint64_t)rd) > 8ULL * 1024 * 1024 * 1024) { fclose(fp); fail(@"Vượt giới hạn dung lượng 8 GB."); }
                    if (fwrite(buf, 1, (size_t)rd, fp) != (size_t)rd) { fclose(fp); fail(@"Không ghi được dữ liệu game (kiểm tra bộ nhớ trống)."); }
                }
                fclose(fp);
                written++;
            }
        }
    } @finally {
        free(buf);
        api->read_free(a);
    }
    if (written == 0) fail(@"Không giải nén được tệp nào từ gói dữ liệu.");
    syncBundledLidgren(docsPath);
    syncBundledSmapi(docsPath);
    syncBundledMobileControls(docsPath);
    return written;
}

static NSUInteger importGameDataZip(NSURL *url, NSString *docsPath) {
    if (!docsPath.length) fail(@"Không tìm thấy thư mục Documents của game.");
    NSString *ext = url.pathExtension.lowercaseString;
    if (![ext isEqualToString:@"zip"]) {
        return importGameDataWithLibArchive(url, docsPath);
    }
    mz_zip_archive z = {0};
    if (!mz_zip_reader_init_file(&z, url.fileSystemRepresentation, 0)) {
        return importGameDataWithLibArchive(url, docsPath);
    }
    NSUInteger written = 0;
    @try {
        unsigned count = mz_zip_reader_get_num_files(&z);
        if (count == 0 || count > 40000) fail(@"Gói ZIP trống hoặc vượt quá 40.000 mục.");

        // Pass 1: Detect wrapper folder prefix containing Stardew Valley.dll / Content/ / StardewModdingAPI.dll / smapi-internal/
        NSString *bestPrefix = nil;
        NSInteger bestScore = -1;
        for (unsigned i = 0; i < count; i++) { @autoreleasepool {
            mz_zip_archive_file_stat st;
            if (!mz_zip_reader_file_stat(&z, i, &st) || st.m_is_directory) continue;
            unsigned n = mz_zip_reader_get_filename(&z, i, NULL, 0);
            if (n < 2 || n > 4096) continue;
            char buf[4096]; mz_zip_reader_get_filename(&z, i, buf, n);
            NSString *raw = [[NSString alloc] initWithBytes:buf length:n - 1 encoding:NSUTF8StringEncoding];
            if (!raw.length) continue;
            raw = [raw stringByReplacingOccurrencesOfString:@"\\" withString:@"/"];
            if ([raw.pathComponents containsObject:@"__MACOSX"] || [raw.lastPathComponent isEqualToString:@".DS_Store"]) continue;

            NSArray<NSString *> *parts = raw.pathComponents;
            NSString *lastLow = raw.lastPathComponent.lowercaseString;
            if ([lastLow isEqualToString:@"stardew valley.dll"]) {
                NSString *pref = (parts.count > 1) ? [[NSString pathWithComponents:[parts subarrayWithRange:NSMakeRange(0, parts.count - 1)]] stringByAppendingString:@"/"] : @"";
                if (bestScore < 100 || (bestScore == 100 && pref.length < bestPrefix.length)) { bestScore = 100; bestPrefix = pref; }
            } else if ([lastLow isEqualToString:@"stardewmoddingapi.dll"]) {
                NSString *pref = (parts.count > 1) ? [[NSString pathWithComponents:[parts subarrayWithRange:NSMakeRange(0, parts.count - 1)]] stringByAppendingString:@"/"] : @"";
                if (bestScore < 80) { bestScore = 80; bestPrefix = pref; }
            } else {
                for (NSUInteger k = 0; k + 1 < parts.count; k++) {
                    NSString *seg = parts[k].lowercaseString;
                    if ([seg isEqualToString:@"content"] || [seg isEqualToString:@"smapi-internal"]) {
                        NSInteger sc = [seg isEqualToString:@"content"] ? 90 : 70;
                        NSString *pref = (k > 0) ? [[NSString pathWithComponents:[parts subarrayWithRange:NSMakeRange(0, k)]] stringByAppendingString:@"/"] : @"";
                        if (bestScore < sc || (bestScore == sc && pref.length < bestPrefix.length)) { bestScore = sc; bestPrefix = pref; }
                        break;
                    }
                }
            }
        }}

        if (bestScore < 0 || bestPrefix == nil) {
            fail(@"Gói ZIP này không chứa dữ liệu game PC (Stardew Valley.dll hoặc Content/). Nếu đây là file mod, hãy dùng nút Chọn file Mod!");
        }
        if (bestScore < 90) {
            fail(@"SMAPI 4.5.2 bản iOS đã tích hợp sẵn trong Garden Launcher — bạn không cần nhập gói SMAPI từ PC! Hãy chọn gói dữ liệu game PC chứa Stardew Valley.dll và thư mục Content/.");
        }

        // Pass 2: Extract cleanly into Documents/ (never overwrite existing Saves/, skip conflicting desktop runtime binaries!)
        uint64_t totalBytes = 0;
        mkdirs(docsPath);
        mkdirs(join(docsPath, @"Mods"));
        mkdirs(join(docsPath, @"Saves"));
        mkdirs(join(docsPath, @"ErrorLogs"));

        // Build case-insensitive set of DLLs already shipped in SDViOS.app bundle (.NET 8 iOS runtime / MonoGame / Harmony)
        NSMutableSet<NSString *> *bundleRuntimeFiles = [NSMutableSet set];
        for (NSString *bf in ([fm() contentsOfDirectoryAtPath:NSBundle.mainBundle.bundlePath error:nil] ?: @[])) {
            if ([bf.pathExtension.lowercaseString isEqualToString:@"dll"]) {
                [bundleRuntimeFiles addObject:bf.lowercaseString];
            }
        }
        NSSet<NSString *> *desktopJunk = [NSSet setWithArray:@[
            @"coreclr.dll", @"clrjit.dll", @"clrgc.dll", @"clretwrc.dll",
            @"hostfxr.dll", @"hostpolicy.dll", @"ucrtbase.dll", @"msquic.dll",
            @"mscordaccore.dll", @"mscordbi.dll", @"mscorrc.dll", @"dbgshim.dll",
            @"steamworks.net.dll", @"galaxycsharp.dll", @"galaxy64.dll", @"galaxycsharpglue.dll",
            @"steam_api64.dll", @"steam_api64.cdx", @"steam_emu.ini",
            @"sdl2.dll", @"faudio.dll", @"soft_oal.dll", @"libskiasharp.dll",
            @"liblwjgl_lz4.dll", @"stb_png_writer.dll", @"system.io.compression.native.dll",
            @"system.private.corelib.dll"
        ]];

        for (unsigned i = 0; i < count; i++) { @autoreleasepool {
            mz_zip_archive_file_stat st;
            if (!mz_zip_reader_file_stat(&z, i, &st) || st.m_is_directory) continue;
            if (st.m_is_encrypted || !st.m_is_supported || ((st.m_external_attr >> 16) & 0170000) == 0120000) continue;
            unsigned n = mz_zip_reader_get_filename(&z, i, NULL, 0);
            if (n < 2 || n > 4096) continue;
            char buf[4096]; mz_zip_reader_get_filename(&z, i, buf, n);
            NSString *raw = [[NSString alloc] initWithBytes:buf length:n - 1 encoding:NSUTF8StringEncoding];
            if (!raw.length) continue;
            raw = [raw stringByReplacingOccurrencesOfString:@"\\" withString:@"/"];
            if ([raw.pathComponents containsObject:@"__MACOSX"] || [raw.lastPathComponent isEqualToString:@".DS_Store"]) continue;

            NSString *rel = raw;
            if (bestPrefix.length) {
                if (![rel hasPrefix:bestPrefix]) continue;
                rel = [rel substringFromIndex:bestPrefix.length];
            }
            while ([rel hasPrefix:@"/"]) rel = [rel substringFromIndex:1];
            if (!rel.length) continue;
            rel = clean(rel);

            NSArray<NSString *> *relParts = rel.pathComponents;
            NSString *topLow = relParts.firstObject.lowercaseString;
            NSString *fileLow = rel.lastPathComponent.lowercaseString;
            NSString *extLow = rel.pathExtension.lowercaseString;

            // Protect existing user saves, bundled iOS SMAPI 4.5.2 & built-in touch controls mods
            if ([topLow isEqualToString:@"saves"]) continue;
            if ([topLow isEqualToString:@"smapi-internal"]) continue;
            if ([topLow isEqualToString:@"mods"] && relParts.count >= 2 &&
                ([relParts[1] caseInsensitiveCompare:@"SDViOSTouchControls"] == NSOrderedSame ||
                 [relParts[1] caseInsensitiveCompare:@"SDVMobileControls"] == NSOrderedSame)) continue;

            // Skip PC Windows SMAPI, desktop native binaries & desktop .NET BCL DLLs already provided by SDViOS.app for iOS
            if (relParts.count == 1) {
                if ([fileLow hasPrefix:@"stardewmoddingapi"]) continue;
                if ([extLow isEqualToString:@"exe"] || [extLow isEqualToString:@"so"] || [extLow isEqualToString:@"dylib"] || [extLow isEqualToString:@"pdb"] || [fileLow containsString:@".so."]) continue;
                if ([fileLow hasPrefix:@"api-ms-win-"] || [fileLow hasPrefix:@"mscordaccore_"] || [fileLow hasPrefix:@"microsoft.diasymreader"]) continue;
                if ([desktopJunk containsObject:fileLow] || [bundleRuntimeFiles containsObject:fileLow]) continue;
            }

            // Allow large single files like Content/XACT/Wave Bank.xwb (~440 MB) up to 2 GB each / 8 GB total
            if (st.m_uncomp_size > 2ULL * 1024 * 1024 * 1024 || (totalBytes += st.m_uncomp_size) > 8ULL * 1024 * 1024 * 1024) {
                fail(@"Gói dữ liệu vượt quá giới hạn dung lượng (tối đa 2 GB/tệp, 8 GB tổng).");
            }
            NSString *dst = inside(docsPath, rel);
            mkdirs(dst.stringByDeletingLastPathComponent);
            if (!mz_zip_reader_extract_to_file(&z, i, dst.fileSystemRepresentation, 0)) {
                fail(@"Giải nén dữ liệu game thất bại. Kiểm tra dung lượng trống trên iPhone.");
            }
            written++;
        }}
    } @finally {
        mz_zip_reader_end(&z);
    }
    if (written == 0) fail(@"Không giải nén được tệp nào từ gói ZIP.");
    syncBundledLidgren(docsPath);
    syncBundledSmapi(docsPath);
    syncBundledMobileControls(docsPath);
    return written;
}
static id dictValueCI(NSDictionary *m, NSString *key) {
    if (![m isKindOfClass:NSDictionary.class] || !key.length) return nil;
    id v = m[key];
    if (v) return v;
    for (id k in m.allKeys) {
        if ([k isKindOfClass:NSString.class] && [(NSString *)k caseInsensitiveCompare:key] == NSOrderedSame) {
            return m[k];
        }
    }
    return nil;
}
static NSString *field(NSDictionary *m, NSString *key) {
    id v = dictValueCI(m, key);
    if ([v isKindOfClass:NSString.class]) return v;
    if ([v isKindOfClass:NSNumber.class]) return [(NSNumber *)v stringValue];
    if ([v isKindOfClass:NSDictionary.class] && [key caseInsensitiveCompare:@"Version"] == NSOrderedSame) {
        id maj = dictValueCI(v, @"MajorVersion");
        id min = dictValueCI(v, @"MinorVersion");
        id pat = dictValueCI(v, @"PatchVersion");
        if (maj || min || pat) {
            return [NSString stringWithFormat:@"%ld.%ld.%ld",
                (long)[maj integerValue], (long)[min integerValue], (long)[pat integerValue]];
        }
    }
    return @"";
}
static NSDictionary *manifest(NSString *p) {
    id m = jsonAt(p);
    if (![m isKindOfClass:NSDictionary.class] || !field(m, @"UniqueID").length) {
        fail([@"Manifest thiếu UniqueID trong " stringByAppendingString:p.stringByDeletingLastPathComponent.lastPathComponent]);
    }
    return m;
}
static NSString *parent(NSString *p) { NSRange i=[p rangeOfString:@"/" options:NSBackwardsSearch]; return i.location==NSNotFound?@"":[p substringToIndex:i.location]; }
static NSString *below(NSString *r,NSString *base) {
    if(!base.length) return r; NSString *prefix=[base stringByAppendingString:@"/"]; return [r hasPrefix:prefix]?[r substringFromIndex:prefix.length]:nil;
}
static BOOL childScope(NSString *root) {
    return [root.lastPathComponent isEqual:@"Mods"] || [root.lastPathComponent isEqual:@"Content"];
}
static NSString *gamePath(NSString *root, NSString *relative) {
    if (!childScope(root)) return inside(root,relative);
    NSArray *parts=clean(relative).pathComponents;
    if (![parts.firstObject isEqual:root.lastPathComponent]) fail([NSString stringWithFormat:@"Gói này cần thư mục %@. Nhấn Đổi thư mục rồi chọn %@ trong Stardew Mobile.", parts.firstObject, parts.firstObject]);
    if (parts.count==1) return root;
    return inside(root,[NSString pathWithComponents:[parts subarrayWithRange:NSMakeRange(1,parts.count-1)]]);
}
static BOOL isHiddenRelPath(NSString *rel) {
    for (NSString *part in rel.pathComponents) {
        if ([part hasPrefix:@"."]) return YES;
    }
    return NO;
}
static NSString *formatBytes(unsigned long long bytes) {
    if (bytes < 1024ULL) return [NSString stringWithFormat:@"%llu B", bytes];
    if (bytes < 1024ULL * 1024ULL) return [NSString stringWithFormat:@"%.1f KB", bytes / 1024.0];
    return [NSString stringWithFormat:@"%.1f MB", bytes / (1024.0 * 1024.0)];
}

// Active mods indexed by lowercase UniqueID (never crashes if a duplicate ID exists on disk)
static NSDictionary *installed(NSString *root) {
    if ([root.lastPathComponent isEqual:@"Content"]) return @{};
    NSMutableDictionary *d = [NSMutableDictionary dictionary];
    NSString *mods = gamePath(root, @"Mods");
    if (!dir(mods)) return d;
    for (NSString *r in files(mods)) {
        if (![r.lastPathComponent.lowercaseString isEqual:@"manifest.json"]) continue;
        if (isHiddenRelPath(r)) continue;
        @try {
            NSDictionary *m = manifest(inside(mods, r));
            NSString *key = field(m, @"UniqueID").lowercaseString;
            if (key.length && !d[key]) {
                d[key] = @{@"dir": parent(r), @"manifest": m};
            }
        } @catch (NSException *ex) { }
    }
    return d;
}

// Disabled dot-prefixed mods indexed by lowercase UniqueID -> top-level ".FolderName"
static NSDictionary<NSString *, NSString *> *disabledModsById(NSString *root) {
    if ([root.lastPathComponent isEqual:@"Content"]) return @{};
    NSMutableDictionary *d = [NSMutableDictionary dictionary];
    NSString *mods = gamePath(root, @"Mods");
    if (!dir(mods)) return d;
    for (NSString *r in files(mods)) {
        if (![r.lastPathComponent.lowercaseString isEqual:@"manifest.json"]) continue;
        NSString *top = r.pathComponents.firstObject;
        if (![top hasPrefix:@"."]) continue;
        @try {
            NSDictionary *m = manifest(inside(mods, r));
            NSString *key = field(m, @"UniqueID").lowercaseString;
            if (key.length) d[key] = top;
        } @catch (NSException *ex) { }
    }
    return d;
}

static NSArray<NSDictionary *> *modCatalog(NSString *root) {
    if (!root.length || [root.lastPathComponent isEqual:@"Content"]) return @[];
    NSString *mods = gamePath(root, @"Mods");
    if (!dir(mods)) return @[];
    NSMutableDictionary *byTopFolder = [NSMutableDictionary dictionary];
    for (NSString *r in files(mods)) {
        if (![r.lastPathComponent.lowercaseString isEqual:@"manifest.json"]) continue;
        NSArray *parts = r.pathComponents;
        if (parts.count < 2) continue;
        NSString *top = parts.firstObject;
        BOOL enabled = ![top hasPrefix:@"."];
        NSString *cleanTop = enabled ? top : [top substringFromIndex:1];
        if (!cleanTop.length) continue;
        @try {
            NSDictionary *m = manifest(inside(mods, r));
            NSString *name = field(m, @"Name");
            NSString *uid = field(m, @"UniqueID");
            NSString *ver = field(m, @"Version");
            NSString *author = field(m, @"Author");
            BOOL isSystem = [cleanTop caseInsensitiveCompare:@"SDViOSTouchControls"] == NSOrderedSame ||
                            [uid.lowercaseString containsString:@"sdviostouchcontrols"];
            NSMutableDictionary *entry = byTopFolder[top];
            if (!entry) {
                entry = [@{
                    @"topFolder": top,
                    @"cleanFolder": cleanTop,
                    @"name": name.length ? name : (uid.length ? uid : cleanTop),
                    @"uniqueID": uid,
                    @"version": ver.length ? ver : @"1.0",
                    @"author": author,
                    @"enabled": @(enabled),
                    @"protected": @(isSystem),
                    @"packCount": @(1)
                } mutableCopy];
                byTopFolder[top] = entry;
            } else {
                entry[@"packCount"] = @([entry[@"packCount"] integerValue] + 1);
                if (parts.count == 2) {
                    entry[@"name"] = name.length ? name : entry[@"name"];
                    entry[@"version"] = ver.length ? ver : entry[@"version"];
                    entry[@"author"] = author.length ? author : entry[@"author"];
                }
            }
        } @catch (NSException *ex) {
            if (!byTopFolder[top]) {
                byTopFolder[top] = [@{
                    @"topFolder": top,
                    @"cleanFolder": cleanTop,
                    @"name": cleanTop,
                    @"uniqueID": @"",
                    @"version": @"?",
                    @"author": @"",
                    @"enabled": @(enabled),
                    @"protected": @(NO),
                    @"packCount": @(1)
                } mutableCopy];
            }
        }
    }
    NSArray *sorted = [byTopFolder.allValues sortedArrayUsingComparator:^NSComparisonResult(NSDictionary *a, NSDictionary *b) {
        if ([a[@"protected"] boolValue] != [b[@"protected"] boolValue]) return [a[@"protected"] boolValue] ? NSOrderedDescending : NSOrderedAscending;
        if ([a[@"enabled"] boolValue] != [b[@"enabled"] boolValue]) return [a[@"enabled"] boolValue] ? NSOrderedAscending : NSOrderedDescending;
        return [a[@"name"] localizedCaseInsensitiveCompare:b[@"name"]];
    }];
    return sorted;
}

static void toggleModEnabled(NSString *root, NSString *topFolder) {
    NSString *cleanName = [topFolder hasPrefix:@"."] ? [topFolder substringFromIndex:1] : topFolder;
    if ([cleanName caseInsensitiveCompare:@"SDViOSTouchControls"] == NSOrderedSame) fail(@"Không thể tắt mod điều khiển hệ thống.");
    NSString *mods = gamePath(root, @"Mods");
    NSString *src = inside(mods, topFolder);
    NSString *dstName = [topFolder hasPrefix:@"."] ? cleanName : [@"." stringByAppendingString:topFolder];
    NSString *dst = inside(mods, dstName);
    if ([fm() fileExistsAtPath:dst]) removeItem(dst);
    NSError *e = nil;
    BOOL ok = [fm() moveItemAtPath:src toPath:dst error:&e];
    check(ok, e);
}

static void deleteModFolder(NSString *root, NSString *topFolder) {
    NSString *cleanName = [topFolder hasPrefix:@"."] ? [topFolder substringFromIndex:1] : topFolder;
    if ([cleanName caseInsensitiveCompare:@"SDViOSTouchControls"] == NSOrderedSame) fail(@"Không thể xóa mod điều khiển hệ thống.");
    NSString *mods = gamePath(root, @"Mods");
    // Remove requested folder AND any opposite-state leftover (.Folder / Folder) so reinstalling NEVER collides!
    removeItem(inside(mods, topFolder));
    NSString *alt = [topFolder hasPrefix:@"."] ? cleanName : [@"." stringByAppendingString:cleanName];
    NSString *altPath = inside(mods, alt);
    if ([fm() fileExistsAtPath:altPath]) removeItem(altPath);
}

// Check if an existing Mods/<folder> contains an active manifest belonging to a DIFFERENT UniqueID
static BOOL folderOwnedByOtherMod(NSString *modsRoot, NSString *folder, NSString *incomingKey) {
    NSString *dirPath = join(modsRoot, folder);
    if (!dir(dirPath)) return NO;
    for (NSString *sub in files(dirPath)) {
        if (![sub.lastPathComponent.lowercaseString isEqual:@"manifest.json"]) continue;
        @try {
            NSDictionary *m = manifest(inside(dirPath, sub));
            NSString *existingKey = field(m, @"UniqueID").lowercaseString;
            if (existingKey.length && ![existingKey isEqualToString:incomingKey]) return YES;
        } @catch (NSException *ex) { }
    }
    return NO;
}

static NSString *sanitizeFolderName(NSString *raw, NSString *fallback) {
    NSString *s = [raw stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
    while ([s hasPrefix:@"."]) s = [s substringFromIndex:1];
    s = [s stringByReplacingOccurrencesOfString:@"/" withString:@"_"];
    s = [s stringByReplacingOccurrencesOfString:@"\\" withString:@"_"];
    s = [s stringByReplacingOccurrencesOfString:@":" withString:@"_"];
    return s.length ? s : (fallback.length ? fallback : @"Mod");
}

// Plan installing ONE OR MULTIPLE staged packages inside `stage` (each package unpacked into `stage/pkg_<i>/...`)
static NSDictionary *plan(NSString *root, NSString *stage, NSArray *preferences, NSDictionary *translations) {
    NSArray *items = files(stage);
    NSDictionary *library = installed(root);
    NSDictionary<NSString *, NSString *> *disabledMap = disabledModsById(root);
    NSMutableDictionary *combinedLibrary = [library mutableCopy];
    NSMutableDictionary *incoming = [NSMutableDictionary dictionary];
    NSMutableDictionary *out = [NSMutableDictionary dictionary];
    NSMutableSet *ids = [NSMutableSet set];
    NSMutableSet *claimedFolders = [NSMutableSet set];
    NSMutableSet *warnings = [NSMutableSet set];
    NSMutableSet *obsoleteDisabled = [NSMutableSet set];
    NSMutableArray *names = [NSMutableArray array];
    NSMutableArray *choices = [NSMutableArray array];
    NSString *translationOwner = nil;
    NSString *modsRoot = [root.lastPathComponent isEqual:@"Content"] ? nil : gamePath(root, @"Mods");

    void (^add)(NSString *, NSString *) = ^(NSString *dst, NSString *src) {
        gamePath(root, dst);
        // Safely overwrite if a later package in the same batch overrides an earlier destination (e.g., i18n patch over base mod)
        out[dst] = src;
    };

    // Pass 1: Discover all SMAPI mods (manifest.json) across all staged packages
    NSMutableArray<NSString *> *badManifests = [NSMutableArray array];
    for (NSString *r in items) {
        if (![r.lastPathComponent.lowercaseString isEqual:@"manifest.json"]) continue;
        NSDictionary *m = nil;
        @try {
            m = manifest(inside(stage, r));
        } @catch (NSException *ex) {
            [badManifests addObject:(ex.reason ?: r)];
            continue;
        }
        NSString *uid = field(m, @"UniqueID");
        NSString *key = uid.lowercaseString;
        NSString *base = parent(r);
        if ([ids containsObject:key]) {
            // User selected a duplicate copy of the same mod in one batch -> skip duplicate gracefully
            continue;
        }
        [ids addObject:key];

        // If a disabled ".Folder" copy of this mod exists, mark it for clean removal on install
        if (disabledMap[key]) {
            [obsoleteDisabled addObject:disabledMap[key]];
        }

        // Determine destination folder cleanly (NEVER fails on leftover/deleted folders!)
        NSString *folder = library[key][@"dir"];
        if (!folder.length) {
            // Extract leaf folder name inside the package (ignoring synthetic pkg_NNN wrapper if manifest is at pkg root)
            NSArray *bParts = base.length ? base.pathComponents : @[];
            NSString *candidate = (bParts.count >= 2) ? bParts.lastObject : uid;
            folder = sanitizeFolderName(candidate, sanitizeFolderName(uid, @"Mod"));
            if (modsRoot && (folderOwnedByOtherMod(modsRoot, folder, key) || [claimedFolders containsObject:folder.lowercaseString])) {
                NSString *alt = sanitizeFolderName(uid, folder);
                NSString *tryName = alt;
                int n = 2;
                while ((folderOwnedByOtherMod(modsRoot, tryName, key) || [claimedFolders containsObject:tryName.lowercaseString]) && n < 100) {
                    tryName = [NSString stringWithFormat:@"%@_%d", alt, n++];
                }
                folder = tryName;
            }
        }
        [claimedFolders addObject:folder.lowercaseString];
        incoming[base] = @{@"dest": join(@"Mods", folder), @"m": m, @"folder": folder};
        combinedLibrary[key] = @{@"dir": folder, @"manifest": m};
        [names addObject:[NSString stringWithFormat:@"%@  v%@", field(m, @"Name").length ? field(m, @"Name") : uid, field(m, @"Version").length ? field(m, @"Version") : @"1.0"]];
    }
    for (NSString *bm in badManifests) {
        [warnings addObject:[@"Bỏ qua manifest lỗi: " stringByAppendingString:bm]];
    }

    NSArray *bases = [incoming.allKeys sortedArrayUsingComparator:^NSComparisonResult(NSString *a, NSString *b) {
        return [@(b.length) compare:@(a.length)];
    }];

    // Pass 2: Check dependencies against BOTH already-installed mods AND mods in this same batch!
    for (NSString *base in bases) {
        NSDictionary *m = incoming[base][@"m"];
        NSMutableArray *deps = [NSMutableArray array];
        id rawDeps = dictValueCI(m, @"Dependencies");
        id rawCp = dictValueCI(m, @"ContentPackFor");
        if ([rawDeps isKindOfClass:NSArray.class]) [deps addObjectsFromArray:rawDeps];
        if ([rawCp isKindOfClass:NSDictionary.class]) [deps addObject:rawCp];
        for (id dep in deps) {
            if (![dep isKindOfClass:NSDictionary.class]) continue;
            NSString *uid = field(dep, @"UniqueID");
            id req = dictValueCI(dep, @"IsRequired");
            if (uid.length && ![req isEqual:@NO] && !combinedLibrary[uid.lowercaseString]) {
                [warnings addObject:[@"Thiếu mod phụ thuộc: " stringByAppendingString:uid]];
            }
        }
    }

    // Pass 3a: Map all files belonging to discovered SMAPI mods first
    NSMutableArray *remaining = [NSMutableArray array];
    for (NSString *r in items) { @autoreleasepool {
        BOOL found = NO;
        for (NSString *base in bases) {
            NSString *tail = below(r, base);
            if (!tail) continue;
            NSString *dst = join(incoming[base][@"dest"], tail);
            if (!([tail.lowercaseString isEqual:@"config.json"] && [fm() fileExistsAtPath:gamePath(root, dst)])) {
                add(dst, r);
            }
            found = YES;
            break;
        }
        if (!found) [remaining addObject:r];
    }}

    // Pass 3b: Map Content/ overrides (Vietnamese fonts, .xnb, Strings, Data), standalone i18n packs, and loose XNB files
    NSArray *assets = nil;
    NSMutableSet *addedExtraNames = [NSMutableSet set];
    for (NSString *r in remaining) { @autoreleasepool {
        NSArray *parts = r.pathComponents;
        NSString *extLow = r.pathExtension.lowercaseString;

        // Direct Content/ subfolder inside pack (e.g. LOANMODVIETHOA/Content/Fonts/..., Content/Strings/..., Content/Data/...)
        NSUInteger contentIdx = [parts indexOfObjectPassingTest:^BOOL(NSString *s, NSUInteger i, BOOL *stop) {
            return i > 0 && [s.lowercaseString isEqualToString:@"content"];
        }];
        if (contentIdx != NSNotFound && contentIdx + 1 < parts.count) {
            if ([extLow isEqualToString:@"xnb"] || [extLow isEqualToString:@"json"] || [extLow isEqualToString:@"png"] || [extLow isEqualToString:@"tmx"] || [extLow isEqualToString:@"yaml"]) {
                NSString *subRel = [NSString pathWithComponents:[parts subarrayWithRange:NSMakeRange(contentIdx + 1, parts.count - contentIdx - 1)]];
                add(join(@"Content", subRel), r);
                NSString *topSub = parts[contentIdx + 1];
                NSString *lbl = [NSString stringWithFormat:@"Content/%@ (Việt hóa / Tài nguyên)", topSub];
                if (![addedExtraNames containsObject:lbl]) { [addedExtraNames addObject:lbl]; [names addObject:lbl]; }
                continue;
            }
        }

        NSUInteger idx = [parts indexOfObjectPassingTest:^BOOL(NSString *s, NSUInteger i, BOOL *stop) {
            return [s.lowercaseString isEqual:@"i18n"];
        }];
        if (idx != NSNotFound && [extLow isEqual:@"json"]) {
            NSString *owner = (idx > 0 && ![parts[idx-1] hasPrefix:@"pkg_"]) ? parts[idx-1] : @"";
            NSMutableArray *matches = [NSMutableArray array];
            for (NSDictionary *mod in combinedLibrary.allValues) {
                NSString *mdir = mod[@"dir"];
                if (mdir.length && mdir.lastPathComponent && owner.length &&
                    [owner caseInsensitiveCompare:mdir.lastPathComponent] == NSOrderedSame) {
                    [matches addObject:mdir];
                }
            }
            if (matches.count != 1) {
                NSString *selected = translations[owner];
                if (selected && combinedLibrary[selected]) {
                    [matches removeAllObjects];
                    [matches addObject:combinedLibrary[selected][@"dir"]];
                } else if (bases.count > 0 || out.count > 0) {
                    // Multi-mod pack contains an i18n file for a mod not currently installed -> warn & continue instead of aborting!
                    if (owner.length) [warnings addObject:[NSString stringWithFormat:@"Bỏ qua bản dịch i18n cho mod chưa cài: %@", owner]];
                    continue;
                } else {
                    if (!combinedLibrary.count) fail(@"Cài mod gốc trước (hoặc chọn chung với mod gốc) khi thêm bản dịch i18n.");
                    translationOwner = owner;
                    break;
                }
            }
            @try {
                if (![jsonAt(inside(stage, r)) isKindOfClass:NSDictionary.class]) continue;
            } @catch (NSException *ex) {
                [warnings addObject:[NSString stringWithFormat:@"Bỏ qua tệp dịch lỗi: %@", r.lastPathComponent]];
                continue;
            }
            NSString *targetDir = matches[0];
            add(join(join(@"Mods", targetDir), [NSString pathWithComponents:[parts subarrayWithRange:NSMakeRange(idx, parts.count - idx)]]), r);
            NSString *lbl = [NSString stringWithFormat:@"Bản dịch i18n → %@", targetDir.lastPathComponent];
            if (![addedExtraNames containsObject:lbl]) { [addedExtraNames addObject:lbl]; [names addObject:lbl]; }
            continue;
        }

        if (![extLow isEqual:@"xnb"]) continue;
        NSData *data = readFile(inside(stage, r));
        if (data.length < 10 || memcmp(data.bytes, "XNB", 3)) {
            [warnings addObject:[@"Bỏ qua tệp XNB không hợp lệ: " stringByAppendingString:r.lastPathComponent]];
            continue;
        }
        if (!assets) assets = files(gamePath(root, @"Content"));
        NSMutableArray *matches = [NSMutableArray array], *specific = [NSMutableArray array];
        // Strip synthetic pkg_NNN prefix when comparing relative XNB paths
        NSString *relNoPkg = r;
        if (parts.count >= 2 && [parts.firstObject hasPrefix:@"pkg_"]) {
            relNoPkg = [NSString pathWithComponents:[parts subarrayWithRange:NSMakeRange(1, parts.count - 1)]];
        }
        for (NSString *a in assets) {
            if ([a.lastPathComponent caseInsensitiveCompare:r.lastPathComponent] == NSOrderedSame) {
                [matches addObject:a];
                if ([relNoPkg.lowercaseString isEqual:a.lowercaseString] || [relNoPkg.lowercaseString hasSuffix:[@"/" stringByAppendingString:a.lowercaseString]]) {
                    [specific addObject:a];
                }
            }
        }
        if (specific.count == 1) matches = specific;
        if (!matches.count) {
            // If it's a localized .vi-VN.xnb (or similar) matching a known Content subfolder (e.g. Strings/Data/Fonts), install directly!
            NSArray<NSString *> *noPkgParts = relNoPkg.pathComponents;
            NSString *knownSub = nil;
            NSSet *contentDirs = [NSSet setWithArray:@[@"fonts", @"strings", @"data", @"characters", @"portraits", @"loosesprites", @"maps", @"minigames", @"terrainfeatures", @"tilesheets", @"animals", @"buildings", @"effects"]];
            for (NSUInteger k = 0; k + 1 < noPkgParts.count; k++) {
                if ([contentDirs containsObject:noPkgParts[k].lowercaseString]) {
                    knownSub = [NSString pathWithComponents:[noPkgParts subarrayWithRange:NSMakeRange(k, noPkgParts.count - k)]];
                    break;
                }
            }
            if (knownSub.length) {
                add(join(@"Content", knownSub), r);
                NSString *lbl = [NSString stringWithFormat:@"XNB → Content/%@", knownSub];
                if (![addedExtraNames containsObject:lbl]) { [addedExtraNames addObject:lbl]; [names addObject:lbl]; }
                continue;
            }
            if (out.count > 0) {
                [warnings addObject:[@"Bỏ qua XNB không rõ thư mục Content: " stringByAppendingString:r.lastPathComponent]];
                continue;
            }
            fail([@"Không tìm thấy XNB tương ứng trong Content: " stringByAppendingString:r.lastPathComponent]);
        }
        NSString *dst = nil;
        if (matches.count == 1) dst = matches[0];
        else {
            for (NSString *choice in preferences) {
                for (NSString *a in matches) if ([parent(a) isEqual:choice]) dst = a;
                if (dst) break;
            }
            if (!dst) {
                for (NSString *a in matches) if (![choices containsObject:parent(a)]) [choices addObject:parent(a)];
                break;
            }
        }
        add(join(@"Content", dst), r);
        NSString *lbl = [NSString stringWithFormat:@"XNB → Content/%@", dst];
        if (![addedExtraNames containsObject:lbl]) { [addedExtraNames addObject:lbl]; [names addObject:lbl]; }
    }}

    if (!out.count && !choices.count && translationOwner == nil) {
        if (badManifests.count) {
            fail([@"Không tìm thấy mod hợp lệ. Lỗi manifest: " stringByAppendingString:badManifests.firstObject]);
        }
        fail(@"Gói cần có manifest.json, thư mục Content/, bản dịch i18n hoặc tệp XNB.");
    }
    return @{
        @"map": out,
        @"names": names,
        @"warnings": warnings.allObjects,
        @"choices": choices,
        @"translationOwner": translationOwner ?: NSNull.null,
        @"obsoleteDisabled": obsoleteDisabled.allObjects,
        @"combinedLibrary": combinedLibrary
    };
}
