// Cross-Platform Stardew Valley Save File Manager (Import, Auto-Config, Repair & Export)
// Supports PC (Windows/macOS/Linux), Android (Mobile & SMAPI), iOS (App Store/Arcade/Garden Launcher)
// Exact iOS .NET 8 / SMAPI Live Paths (verified from System.Private.CoreLib + Stardew Valley.dll):
//   Live Saves:     <Documents>/.config/StardewValley/Saves/<FolderName>/
//   QuickSave JSON: <Documents>/Mods/SDViOSTouchControls/data/<FolderName>_quicksave.json
//   Save Slots:     <Documents>/Mods/SDVMobileControls/SaveSlots/<FolderName>/

static NSString *sdvLiveSavesDir(NSString *docsPath) {
    if (!docsPath.length) return nil;
    return join(docsPath, @".config/StardewValley/Saves");
}

static NSString *sdvQuickSaveDataDir(NSString *docsPath) {
    if (!docsPath.length) return nil;
    return join(docsPath, @"Mods/SDViOSTouchControls/data");
}

static NSString *sdvQuickSaveJsonPath(NSString *docsPath, NSString *folderName) {
    if (!docsPath.length || !folderName.length) return nil;
    return join(sdvQuickSaveDataDir(docsPath), [NSString stringWithFormat:@"%@_quicksave.json", folderName]);
}

static NSString *sdvXmlExtractTag(NSString *xml, NSString *tag) {
    if (!xml.length || !tag.length) return nil;
    NSString *open = [NSString stringWithFormat:@"<%@>", tag];
    NSString *close = [NSString stringWithFormat:@"</%@>", tag];
    NSRange r1 = [xml rangeOfString:open];
    if (r1.location == NSNotFound) return nil;
    NSUInteger start = r1.location + r1.length;
    NSRange r2 = [xml rangeOfString:close options:0 range:NSMakeRange(start, xml.length - start)];
    if (r2.location == NSNotFound) return nil;
    NSString *val = [xml substringWithRange:NSMakeRange(start, r2.location - start)];
    return [val stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
}

static NSString *sdvSeasonLabelVN(NSString *raw) {
    if (!raw.length) return @"Xuân";
    NSString *lo = raw.lowercaseString;
    if ([lo isEqualToString:@"0"] || [lo isEqualToString:@"spring"]) return @"Xuân";
    if ([lo isEqualToString:@"1"] || [lo isEqualToString:@"summer"]) return @"Hè";
    if ([lo isEqualToString:@"2"] || [lo isEqualToString:@"fall"])   return @"Thu";
    if ([lo isEqualToString:@"3"] || [lo isEqualToString:@"winter"]) return @"Đông";
    return raw;
}

static NSData *sdvStripUtf8Bom(NSData *raw) {
    if (!raw || raw.length < 3) return raw;
    const uint8_t *b = (const uint8_t *)raw.bytes;
    NSUInteger off = 0;
    while (off + 3 <= raw.length && b[off] == 0xEF && b[off + 1] == 0xBB && b[off + 2] == 0xBF) {
        off += 3;
    }
    return off > 0 ? [raw subdataWithRange:NSMakeRange(off, raw.length - off)] : raw;
}

static NSString *sdvReadFilePrefixUtf8(NSString *path, NSUInteger maxBytes) {
    NSFileHandle *fh = [NSFileHandle fileHandleForReadingAtPath:path];
    if (!fh) return nil;
    NSData *d = [fh readDataOfLength:maxBytes];
    [fh closeFile];
    if (!d.length) return nil;
    d = sdvStripUtf8Bom(d);
    return [[NSString alloc] initWithData:d encoding:NSUTF8StringEncoding]
        ?: [[NSString alloc] initWithData:d encoding:NSISOLatin1StringEncoding];
}

// Reads the last `maxBytes` of a Stardew SaveGame XML where <dayOfMonth>, <currentSeason>, <year>,
// <uniqueIDForThisGame>, and <gameVersion> are serialized after <locations>.
static NSString *sdvReadFileTailUtf8(NSString *path, NSUInteger maxBytes) {
    NSDictionary *attrs = [fm() attributesOfItemAtPath:path error:nil];
    unsigned long long fsize = [attrs[NSFileSize] unsignedLongLongValue];
    if (fsize == 0) return nil;
    NSFileHandle *fh = [NSFileHandle fileHandleForReadingAtPath:path];
    if (!fh) return nil;
    unsigned long long off = (fsize > maxBytes) ? (fsize - maxBytes) : 0;
    if (off > 0) [fh seekToFileOffset:off];
    NSData *d = [fh readDataOfLength:maxBytes];
    [fh closeFile];
    if (!d.length) return nil;
    if (off == 0) d = sdvStripUtf8Bom(d);
    return [[NSString alloc] initWithData:d encoding:NSUTF8StringEncoding]
        ?: [[NSString alloc] initWithData:d encoding:NSISOLatin1StringEncoding];
}

static BOOL sdvLooksLikeMainSaveXml(NSString *path) {
    if (!path.length || dir(path)) return NO;
    NSString *name = path.lastPathComponent;
    NSString *lo = name.lowercaseString;
    if ([lo hasPrefix:@"."] || [lo isEqualToString:@"savegameinfo"] ||
        [lo hasSuffix:@"_old"] || [lo hasPrefix:@"emergency_save"] ||
        [lo hasSuffix:@".json"] || [lo hasSuffix:@".png"] || [lo hasSuffix:@".jpg"] ||
        [lo hasSuffix:@".txt"] || [lo hasSuffix:@".bak"] || [lo hasSuffix:@".zip"]) {
        return NO;
    }
    NSString *head = sdvReadFilePrefixUtf8(path, 2048);
    if (!head.length) return NO;
    return ([head rangeOfString:@"<SaveGame"].location != NSNotFound);
}

static BOOL sdvLooksLikeSaveGameInfoXml(NSString *path) {
    if (!path.length || dir(path)) return NO;
    NSString *head = sdvReadFilePrefixUtf8(path, 1024);
    if (!head.length) return NO;
    return ([head rangeOfString:@"<Farmer"].location != NSNotFound);
}

// Synthesizes a valid SaveGameInfo XML from the <player ...>...</player> block inside a main Stardew save file
static NSData *sdvSynthesizeSaveGameInfoFromMainData(NSData *mainData) {
    if (!mainData.length) return nil;
    NSData *cleanData = sdvStripUtf8Bom(mainData);
    const char *bytes = (const char *)cleanData.bytes;
    NSUInteger len = cleanData.length;

    NSData *openPrefix = [@"<player" dataUsingEncoding:NSUTF8StringEncoding];
    NSData *closePat = [@"</player>" dataUsingEncoding:NSUTF8StringEncoding];
    NSUInteger searchStart = 0;
    NSUInteger bodyStart = NSNotFound;
    NSUInteger scanLim = MIN(len, (NSUInteger)(4 * 1024 * 1024));
    while (searchStart < scanLim) {
        NSRange r = [cleanData rangeOfData:openPrefix options:0 range:NSMakeRange(searchStart, scanLim - searchStart)];
        if (r.location == NSNotFound) break;
        NSUInteger after = r.location + r.length;
        if (after < len && (bytes[after] == '>' || bytes[after] == ' ' || bytes[after] == '\t' || bytes[after] == '\r' || bytes[after] == '\n')) {
            // Find closing '>' of opening <player ...> tag
            NSUInteger gt = after;
            while (gt < len && bytes[gt] != '>') gt++;
            if (gt < len && bytes[gt] == '>') {
                bodyStart = gt + 1;
                break;
            }
        }
        searchStart = r.location + 1;
    }
    if (bodyStart == NSNotFound || bodyStart >= len) return nil;

    NSRange rClose = [cleanData rangeOfData:closePat options:0 range:NSMakeRange(bodyStart, len - bodyStart)];
    if (rClose.location == NSNotFound) return nil;

    NSData *inner = [NSData dataWithBytes:bytes + bodyStart length:rClose.location - bodyStart];
    NSMutableData *out = [NSMutableData dataWithCapacity:inner.length + 256];
    NSString *hdr = @"<?xml version=\"1.0\" encoding=\"utf-8\"?><Farmer xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">";
    NSString *ftr = @"</Farmer>";
    [out appendData:[hdr dataUsingEncoding:NSUTF8StringEncoding]];
    [out appendData:inner];
    [out appendData:[ftr dataUsingEncoding:NSUTF8StringEncoding]];
    return out;
}

static NSString *sdvSanitizeSaveFolderName(NSString *rawName, NSString *uniqueId) {
    NSMutableString *cleanName = [NSMutableString string];
    NSCharacterSet *bad = [NSCharacterSet characterSetWithCharactersInString:@"/\\:*?\"<>|\r\n\t"];
    for (NSUInteger i = 0; i < rawName.length; i++) {
        unichar c = [rawName characterAtIndex:i];
        if (![bad characterIsMember:c]) {
            [cleanName appendFormat:@"%C", c];
        }
    }
    NSString *trimmed = [cleanName stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
    if (!trimmed.length) trimmed = @"Farm";
    // Ensure _<digits> suffix exists (Stardew 1.6 convention)
    NSRange us = [trimmed rangeOfString:@"_" options:NSBackwardsSearch];
    BOOL hasIdSuffix = NO;
    if (us.location != NSNotFound && us.location + 1 < trimmed.length) {
        NSString *tail = [trimmed substringFromIndex:us.location + 1];
        NSCharacterSet *nonDigits = [[NSCharacterSet decimalDigitCharacterSet] invertedSet];
        hasIdSuffix = (tail.length >= 4 && [tail rangeOfCharacterFromSet:nonDigits].location == NSNotFound);
    }
    if (!hasIdSuffix) {
        NSString *uid = uniqueId.length ? uniqueId : [NSString stringWithFormat:@"%u", arc4random_uniform(899999999) + 100000000];
        trimmed = [NSString stringWithFormat:@"%@_%@", trimmed, uid];
    }
    return trimmed;
}

static unsigned long long sdvDirSizeBytes(NSString *dirPath) {
    unsigned long long total = 0;
    NSDirectoryEnumerator *e = [fm() enumeratorAtPath:dirPath];
    for (NSString *rel in e) {
        NSDictionary *a = [fm() attributesOfItemAtPath:join(dirPath, rel) error:nil];
        if ([a[NSFileType] isEqualToString:NSFileTypeRegular]) total += [a[NSFileSize] unsignedLongLongValue];
    }
    return total;
}

static NSString *sdvFormatFileSizeMB(unsigned long long bytes) {
    if (bytes < 1024 * 1024) return [NSString stringWithFormat:@"%.0f KB", MAX(1.0, (double)bytes / 1024.0)];
    return [NSString stringWithFormat:@"%.2f MB", (double)bytes / (1024.0 * 1024.0)];
}

// Auto-migrate any save folders users manually dropped into visible Documents/Saves/
// (since .config/StardewValley/Saves is hidden in the iOS Files app!) into the real live directory.
static void sdvMigrateLegacyDocumentsSaves(NSString *docsPath) {
    if (!docsPath.length) return;
    NSString *legacyDir = join(docsPath, @"Saves");
    if (!dir(legacyDir)) return;
    NSArray<NSString *> *entries = [fm() contentsOfDirectoryAtPath:legacyDir error:nil] ?: @[];
    if (!entries.count) return;

    NSString *liveDir = sdvLiveSavesDir(docsPath);
    for (NSString *entry in entries) {
        if ([entry hasPrefix:@"."] || [entry isEqualToString:@"__MACOSX"]) continue;
        NSString *srcFolder = join(legacyDir, entry);
        if (!dir(srcFolder)) continue;

        BOOL hasSaveXml = NO;
        for (NSString *sub in ([fm() contentsOfDirectoryAtPath:srcFolder error:nil] ?: @[])) {
            if (sdvLooksLikeMainSaveXml(join(srcFolder, sub))) { hasSaveXml = YES; break; }
        }
        if (!hasSaveXml) continue;

        mkdirs(liveDir);
        NSString *dstFolder = join(liveDir, entry);
        if (![fm() fileExistsAtPath:dstFolder]) {
            NSError *me = nil;
            [fm() moveItemAtPath:srcFolder toPath:dstFolder error:&me];
        }
    }
}

static NSArray<NSDictionary *> *sdvListSaves(NSString *docsPath) {
    if (!docsPath.length) return @[];
    @try { sdvMigrateLegacyDocumentsSaves(docsPath); } @catch (NSException *ignored) { }

    NSString *savesDir = sdvLiveSavesDir(docsPath);
    if (!dir(savesDir)) return @[];

    NSMutableArray<NSDictionary *> *list = [NSMutableArray array];
    NSArray<NSString *> *entries = [fm() contentsOfDirectoryAtPath:savesDir error:nil] ?: @[];
    for (NSString *entry in entries) {
        if ([entry hasPrefix:@"."] || [entry isEqualToString:@"__MACOSX"]) continue;
        NSString *folderPath = join(savesDir, entry);
        if (!dir(folderPath)) continue;

        NSString *mainPath = join(folderPath, entry);
        if (![fm() fileExistsAtPath:mainPath] || dir(mainPath)) {
            mainPath = nil;
            for (NSString *sub in ([fm() contentsOfDirectoryAtPath:folderPath error:nil] ?: @[])) {
                NSString *cand = join(folderPath, sub);
                if (sdvLooksLikeMainSaveXml(cand)) { mainPath = cand; break; }
            }
        }
        NSString *infoPath = join(folderPath, @"SaveGameInfo");
        BOOL hasInfo = [fm() fileExistsAtPath:infoPath] && !dir(infoPath);
        if (!mainPath && !hasInfo) continue;

        NSString *infoXml = hasInfo ? sdvReadFilePrefixUtf8(infoPath, 65536) : nil;
        NSString *mainHead = mainPath ? sdvReadFilePrefixUtf8(mainPath, 98304) : nil;
        NSString *mainTail = mainPath ? sdvReadFileTailUtf8(mainPath, 131072) : nil;

        NSString *farmerName = sdvXmlExtractTag(infoXml, @"name") ?: sdvXmlExtractTag(mainHead, @"name") ?: entry;
        NSString *farmName = sdvXmlExtractTag(infoXml, @"farmName") ?: sdvXmlExtractTag(mainHead, @"farmName") ?: @"Nông trại";
        NSString *day = sdvXmlExtractTag(infoXml, @"dayOfMonthForSaveGame")
            ?: sdvXmlExtractTag(mainHead, @"dayOfMonthForSaveGame")
            ?: sdvXmlExtractTag(mainTail, @"dayOfMonth")
            ?: sdvXmlExtractTag(mainHead, @"dayOfMonth") ?: @"1";
        NSString *seasonRaw = sdvXmlExtractTag(infoXml, @"seasonForSaveGame")
            ?: sdvXmlExtractTag(mainHead, @"seasonForSaveGame")
            ?: sdvXmlExtractTag(mainTail, @"currentSeason")
            ?: sdvXmlExtractTag(mainHead, @"currentSeason") ?: @"0";
        NSString *year = sdvXmlExtractTag(infoXml, @"yearForSaveGame")
            ?: sdvXmlExtractTag(mainHead, @"yearForSaveGame")
            ?: sdvXmlExtractTag(mainTail, @"year")
            ?: sdvXmlExtractTag(mainHead, @"year") ?: @"1";
        NSString *money = sdvXmlExtractTag(infoXml, @"money") ?: sdvXmlExtractTag(mainHead, @"money") ?: @"0";
        NSString *ver = sdvXmlExtractTag(infoXml, @"gameVersion")
            ?: sdvXmlExtractTag(mainTail, @"gameVersion")
            ?: sdvXmlExtractTag(mainHead, @"gameVersion") ?: @"1.6";

        // Check REAL QuickSaveManager JSON at Documents/Mods/SDViOSTouchControls/data/<folderName>_quicksave.json
        NSString *liveQsPath = sdvQuickSaveJsonPath(docsPath, entry);
        BOOL hasQuick = (liveQsPath.length && [fm() fileExistsAtPath:liveQsPath] && !dir(liveQsPath));
        if (!hasQuick) {
            for (NSString *sub in ([fm() contentsOfDirectoryAtPath:folderPath error:nil] ?: @[])) {
                if ([sub.lowercaseString containsString:@"quicksave"] && [sub.lowercaseString hasSuffix:@".json"]) {
                    hasQuick = YES; break;
                }
            }
        }

        NSDictionary *attrs = [fm() attributesOfItemAtPath:(mainPath ?: infoPath) error:nil];
        NSDate *modDate = attrs[NSFileModificationDate] ?: [NSDate distantPast];
        unsigned long long bytes = sdvDirSizeBytes(folderPath);
        BOOL caseMismatch = (mainPath != nil && ![mainPath.lastPathComponent isEqualToString:entry]);

        [list addObject:@{
            @"folderName": entry,
            @"folderPath": folderPath,
            @"mainPath": mainPath ?: @"",
            @"farmerName": farmerName,
            @"farmName": farmName,
            @"day": day,
            @"season": sdvSeasonLabelVN(seasonRaw),
            @"year": year,
            @"money": money,
            @"gameVersion": ver,
            @"hasInfo": @(hasInfo),
            @"hasQuickSave": @(hasQuick),
            @"caseMismatch": @(caseMismatch),
            @"sizeBytes": @(bytes),
            @"sizeText": sdvFormatFileSizeMB(bytes),
            @"modDate": modDate
        }];
    }

    [list sortUsingComparator:^NSComparisonResult(NSDictionary *a, NSDictionary *b) {
        return [b[@"modDate"] compare:a[@"modDate"]];
    }];
    return list;
}

// Repairs an existing save folder in place (fixes case-mismatch main save filename, strips BOM, synthesizes missing SaveGameInfo)
static NSString *sdvRepairSaveFolder(NSString *folderPath) {
    if (!folderPath.length || !dir(folderPath)) fail(@"Không tìm thấy thư mục Save cần sửa.");
    NSString *folderName = folderPath.lastPathComponent;
    NSString *expectedMain = join(folderPath, folderName);
    NSString *foundMain = ([fm() fileExistsAtPath:expectedMain] && !dir(expectedMain)) ? expectedMain : nil;
    if (!foundMain) {
        for (NSString *sub in ([fm() contentsOfDirectoryAtPath:folderPath error:nil] ?: @[])) {
            NSString *cand = join(folderPath, sub);
            if (sdvLooksLikeMainSaveXml(cand)) { foundMain = cand; break; }
        }
    }
    if (!foundMain) fail(@"Thư mục Save không có dữ liệu <SaveGame> hợp lệ.");

    NSMutableArray<NSString *> *fixes = [NSMutableArray array];
    NSData *rawMain = readFile(foundMain);
    NSData *cleanMain = sdvStripUtf8Bom(rawMain);
    if (![foundMain.lastPathComponent isEqualToString:folderName] || cleanMain.length != rawMain.length) {
        writeFile(cleanMain, expectedMain);
        if (![foundMain isEqualToString:expectedMain]) removeItem(foundMain);
        [fixes addObject:@"chuẩn hóa tên tệp & UTF-8"];
    }

    NSString *infoPath = join(folderPath, @"SaveGameInfo");
    if (![fm() fileExistsAtPath:infoPath] || dir(infoPath)) {
        // Check case-insensitive variant first
        NSString *ciInfo = nil;
        for (NSString *sub in ([fm() contentsOfDirectoryAtPath:folderPath error:nil] ?: @[])) {
            if ([sub caseInsensitiveCompare:@"SaveGameInfo"] == NSOrderedSame && ![sub isEqualToString:@"SaveGameInfo"]) {
                ciInfo = join(folderPath, sub); break;
            }
        }
        NSData *infoData = ciInfo ? sdvStripUtf8Bom(readFile(ciInfo)) : sdvSynthesizeSaveGameInfoFromMainData(cleanMain);
        if (infoData.length) {
            writeFile(infoData, infoPath);
            if (ciInfo) removeItem(ciInfo);
            [fixes addObject:ciInfo ? @"sửa tên SaveGameInfo" : @"tự tạo SaveGameInfo"];
        }
    }
    return fixes.count ? [fixes componentsJoinedByString:@" · "] : @"Save đã đạt chuẩn";
}

// Deletes a save folder AND its companion QuickSave JSON & Multi-Slot SaveSlots snapshots cleanly
static void sdvDeleteSaveFolder(NSString *folderPath, NSString *docsPath) {
    if (!folderPath.length) return;
    NSString *folderName = folderPath.lastPathComponent;
    removeItem(folderPath);
    if (docsPath.length && folderName.length) {
        NSString *qs = sdvQuickSaveJsonPath(docsPath, folderName);
        if (qs.length && [fm() fileExistsAtPath:qs]) sdvRemoveItemQuiet(qs);
        NSString *slotsDir = join(docsPath, [NSString stringWithFormat:@"Mods/SDVMobileControls/SaveSlots/%@", folderName]);
        if ([fm() fileExistsAtPath:slotsDir]) sdvRemoveItemQuiet(slotsDir);
        NSString *sanitized = [[folderName componentsSeparatedByCharactersInSet:[NSCharacterSet characterSetWithCharactersInString:@"/\\:*?\"<>|"]] componentsJoinedByString:@"_"];
        if (sanitized.length && ![sanitized isEqualToString:folderName]) {
            NSString *altSlotsDir = join(docsPath, [NSString stringWithFormat:@"Mods/SDVMobileControls/SaveSlots/%@", sanitized]);
            if ([fm() fileExistsAtPath:altSlotsDir]) sdvRemoveItemQuiet(altSlotsDir);
        }
    }
}

static NSString *sdvDetectSourcePlatform(NSString *srcDir, NSString *mainHead, BOOL sawMacJunk) {
    NSArray<NSString *> *sibs = [fm() contentsOfDirectoryAtPath:srcDir error:nil] ?: @[];
    BOOL hasEmergency = NO;
    for (NSString *s in sibs) {
        NSString *lo = s.lowercaseString;
        if ([lo hasPrefix:@"emergency_save"] || [lo hasPrefix:@"mobile_backup"]) {
            hasEmergency = YES;
            break;
        }
    }
    if (hasEmergency) return @"Mobile (iOS / Android)";
    if ([mainHead rangeOfString:@"\r\n"].location != NSNotFound) return @"PC Windows";
    if (sawMacJunk) return @"macOS / iOS";
    if ([mainHead rangeOfString:@"whichModData"].location != NSNotFound ||
        [mainHead rangeOfString:@"spacechase0"].location != NSNotFound ||
        [mainHead rangeOfString:@"Pathoschild"].location != NSNotFound) {
        return @"PC / SMAPI Modded";
    }
    return @"PC / Android / Đa nền tảng";
}

// Imports 1 or more picked URLs (ZIP/RAR/7Z/TAR.GZ archives, save folders, or raw save XML files),
// auto-detects source platform, normalizes casing/BOM/SaveGameInfo/mobile junk, backs up collisions,
// restores any companion QuickSave JSON, and installs directly into Documents/.config/StardewValley/Saves/.
static NSDictionary *sdvImportSavesFromURLs(NSArray<NSURL *> *urls, NSString *docsPath) {
    if (!urls.count || !docsPath.length) {
        fail(@"Chưa chọn tệp hoặc thư mục Save.");
    }
    NSString *tmpRoot = join(NSTemporaryDirectory(), [NSString stringWithFormat:@"GardenSaveImport_%u", arc4random_uniform(999999)]);
    removeItem(tmpRoot);
    mkdirs(tmpRoot);

    BOOL sawMacJunk = NO;
    @try {
        // 1. Stage all picked items (archives unpacked via universal miniz/libarchive, folders copied, loose files staged)
        NSString *looseDir = join(tmpRoot, @"_loose");
        NSInteger idx = 0;
        for (NSURL *u in urls) {
            BOOL scoped = [u startAccessingSecurityScopedResource];
            @try {
                NSString *p = u.path;
                if (!p.length) continue;
                BOOL isDir = NO;
                [fm() fileExistsAtPath:p isDirectory:&isDir];
                if (isDir) {
                    NSString *dst = join(tmpRoot, [NSString stringWithFormat:@"dir_%02ld/%@", (long)idx++, u.lastPathComponent ?: @"Save"]);
                    mkdirs(dst.stringByDeletingLastPathComponent);
                    NSError *ce = nil;
                    [fm() copyItemAtPath:p toPath:dst error:&ce];
                    if (ce) copyModDirectoryRecursive(p, dst);
                } else {
                    NSString *ext = p.pathExtension.lowercaseString;
                    BOOL isArchive = [@[@"zip", @"rar", @"7z", @"tar", @"gz", @"tgz", @"bz2", @"xz"] containsObject:ext];
                    if (isArchive) {
                        NSString *sub = join(tmpRoot, [NSString stringWithFormat:@"arc_%02ld", (long)idx++]);
                        mkdirs(sub);
                        unpack(u, sub);
                    } else {
                        mkdirs(looseDir);
                        NSString *dst = join(looseDir, u.lastPathComponent ?: @"SaveFile");
                        removeItem(dst);
                        NSError *ce = nil;
                        [fm() copyItemAtPath:p toPath:dst error:&ce];
                        check(ce == nil, ce);
                    }
                }
            } @finally {
                if (scoped) [u stopAccessingSecurityScopedResource];
            }
        }

        // 2. Recursively discover all main Stardew Valley save XML files
        NSMutableArray<NSString *> *mainFiles = [NSMutableArray array];
        NSDirectoryEnumerator *en = [fm() enumeratorAtPath:tmpRoot];
        for (NSString *rel in en) {
            if ([rel containsString:@"__MACOSX"] || [rel.lastPathComponent hasPrefix:@"._"] ||
                [rel.lastPathComponent isEqualToString:@".DS_Store"]) {
                sawMacJunk = YES;
                continue;
            }
            NSString *full = join(tmpRoot, rel);
            if (sdvLooksLikeMainSaveXml(full)) {
                [mainFiles addObject:full];
            }
        }

        if (!mainFiles.count) {
            fail(@"Không tìm thấy file Save Stardew Valley hợp lệ (<SaveGame>) trong gói/thư mục đã chọn. Hãy chọn file .ZIP hoặc thư mục chứa Save (VD: TenFarm_123456789).");
        }

        NSString *savesDir = sdvLiveSavesDir(docsPath);
        mkdirs(savesDir);
        NSString *backupRoot = join(docsPath, @"Saves_Backup");

        NSMutableArray<NSString *> *importedNames = [NSMutableArray array];
        NSMutableSet<NSString *> *detectedPlatforms = [NSMutableSet set];
        NSMutableArray<NSString *> *notes = [NSMutableArray array];
        NSInteger backupCount = 0;
        NSInteger synthInfoCount = 0;
        NSInteger quickRestoredCount = 0;
        NSInteger legacy15Count = 0;

        NSDateFormatter *df = [NSDateFormatter new];
        df.dateFormat = @"yyyyMMdd_HHmmss";
        NSString *ts = [df stringFromDate:NSDate.date];

        for (NSString *mainPath in mainFiles) {
            NSString *srcDir = mainPath.stringByDeletingLastPathComponent;
            NSData *rawMain = readFile(mainPath);
            NSData *cleanMain = sdvStripUtf8Bom(rawMain);
            NSString *head = sdvReadFilePrefixUtf8(mainPath, 98304) ?: @"";
            NSString *tail = sdvReadFileTailUtf8(mainPath, 131072) ?: @"";

            NSString *platform = sdvDetectSourcePlatform(srcDir, head, sawMacJunk);
            [detectedPlatforms addObject:platform];

            NSString *farmerName = sdvXmlExtractTag(head, @"name") ?: @"Farmer";
            NSString *farmName = sdvXmlExtractTag(head, @"farmName") ?: @"";
            NSString *uniqueId = sdvXmlExtractTag(tail, @"uniqueIDForThisGame") ?: sdvXmlExtractTag(head, @"uniqueIDForThisGame") ?: @"";
            NSString *gameVer = sdvXmlExtractTag(tail, @"gameVersion") ?: sdvXmlExtractTag(head, @"gameVersion") ?: @"";
            if ([gameVer hasPrefix:@"1.4"] || [gameVer hasPrefix:@"1.5"]) {
                legacy15Count++;
            }

            // Pick canonical folder name (must match main save filename with EXACT case on iOS APFS!)
            NSString *origFile = mainPath.lastPathComponent;
            NSString *parentDirName = srcDir.lastPathComponent;
            NSString *baseCand = origFile;
            if ([baseCand.lowercaseString isEqualToString:@"savegame"] || baseCand.length < 2) {
                baseCand = (![parentDirName hasPrefix:@"_"] && ![parentDirName hasPrefix:@"arc_"] && ![parentDirName hasPrefix:@"dir_"])
                    ? parentDirName : farmerName;
            }
            NSString *canonical = sdvSanitizeSaveFolderName(baseCand, uniqueId);

            // Locate sibling SaveGameInfo (case-insensitive)
            NSString *foundInfoPath = nil;
            for (NSString *sib in ([fm() contentsOfDirectoryAtPath:srcDir error:nil] ?: @[])) {
                if ([sib caseInsensitiveCompare:@"SaveGameInfo"] == NSOrderedSame) {
                    NSString *cand = join(srcDir, sib);
                    if (sdvLooksLikeSaveGameInfoXml(cand)) { foundInfoPath = cand; break; }
                }
            }

            NSData *cleanInfo = nil;
            if (foundInfoPath) {
                cleanInfo = sdvStripUtf8Bom(readFile(foundInfoPath));
            } else {
                cleanInfo = sdvSynthesizeSaveGameInfoFromMainData(cleanMain);
                if (cleanInfo.length) synthInfoCount++;
            }

            // Backup existing save if collision (saved into visible Documents/Saves_Backup/)
            NSString *dstDir = join(savesDir, canonical);
            if ([fm() fileExistsAtPath:dstDir]) {
                mkdirs(backupRoot);
                NSString *bkDir = join(backupRoot, [NSString stringWithFormat:@"%@_%@", canonical, ts]);
                removeItem(bkDir);
                NSError *me = nil;
                [fm() moveItemAtPath:dstDir toPath:bkDir error:&me];
                if (me) removeItem(dstDir);
                else backupCount++;
            }
            mkdirs(dstDir);

            // Write normalized UTF-8 main save file + SaveGameInfo with EXACT case-matching names
            writeFile(cleanMain, join(dstDir, canonical));
            if (cleanInfo.length) {
                writeFile(cleanInfo, join(dstDir, @"SaveGameInfo"));
            }

            // Restore companion QuickSave JSON directly into SDViOSTouchControls/data/<canonical>_quicksave.json!
            for (NSString *sib in ([fm() contentsOfDirectoryAtPath:srcDir error:nil] ?: @[])) {
                NSString *lo = sib.lowercaseString;
                if ([lo containsString:@"quicksave"] && [lo hasSuffix:@".json"]) {
                    NSData *qd = [NSData dataWithContentsOfFile:join(srcDir, sib)];
                    if (qd.length > 2) {
                        NSString *dstQs = sdvQuickSaveJsonPath(docsPath, canonical);
                        mkdirs(dstQs.stringByDeletingLastPathComponent);
                        writeFile(qd, dstQs);
                        quickRestoredCount++;
                        break;
                    }
                }
            }

            NSString *label = farmName.length
                ? [NSString stringWithFormat:@"%@ (%@)", farmerName, farmName]
                : canonical;
            [importedNames addObject:label];
        }

        if (synthInfoCount > 0) [notes addObject:[NSString stringWithFormat:@"tự tạo %ld SaveGameInfo bị thiếu", (long)synthInfoCount]];
        if (quickRestoredCount > 0) [notes addObject:[NSString stringWithFormat:@"khôi phục %ld QuickSave", (long)quickRestoredCount]];
        if (backupCount > 0) [notes addObject:[NSString stringWithFormat:@"sao lưu %ld bản cũ vào Saves_Backup/", (long)backupCount]];
        if (legacy15Count > 0) [notes addObject:@"Save bản 1.5 sẽ tự nâng lên 1.6 khi vào game"];

        NSString *platSummary = [[detectedPlatforms allObjects] componentsJoinedByString:@", "];
        NSString *extra = notes.count ? [NSString stringWithFormat:@" · %@", [notes componentsJoinedByString:@" · "]] : @"";
        NSString *msg = [NSString stringWithFormat:@"Đã nhập & chuẩn hóa %lu Save (%@) từ %@%@. Vào game chọn Load để chơi ngay!",
            (unsigned long)importedNames.count,
            [importedNames componentsJoinedByString:@", "],
            platSummary,
            extra];

        return @{
            @"count": @(importedNames.count),
            @"names": importedNames,
            @"platforms": platSummary,
            @"message": msg
        };
    } @finally {
        removeItem(tmpRoot);
    }
}

// Exports a save folder to a clean cross-platform .ZIP archive using miniz.
// exportMode: 0 = Universal PC/Mac/Linux/Android SMAPI/iOS, 1 = Full Garden iOS Backup (with QuickSave), 2 = Vanilla Mobile (iOS/Android)
static NSURL *sdvExportSaveToZip(NSString *saveFolderPath, NSString *docsPath, NSInteger exportMode) {
    if (!saveFolderPath.length || !dir(saveFolderPath)) {
        fail(@"Không tìm thấy thư mục Save cần xuất.");
    }
    NSString *folderName = saveFolderPath.lastPathComponent;

    // Find main save file inside folder
    NSString *mainPath = join(saveFolderPath, folderName);
    if (![fm() fileExistsAtPath:mainPath] || dir(mainPath)) {
        mainPath = nil;
        for (NSString *sub in ([fm() contentsOfDirectoryAtPath:saveFolderPath error:nil] ?: @[])) {
            NSString *cand = join(saveFolderPath, sub);
            if (sdvLooksLikeMainSaveXml(cand)) { mainPath = cand; break; }
        }
    }
    if (!mainPath) {
        fail(@"Thư mục Save bị thiếu tệp dữ liệu chính.");
    }

    NSData *cleanMain = sdvStripUtf8Bom(readFile(mainPath));
    NSString *infoPath = join(saveFolderPath, @"SaveGameInfo");
    NSData *cleanInfo = nil;
    if ([fm() fileExistsAtPath:infoPath] && !dir(infoPath)) {
        cleanInfo = sdvStripUtf8Bom(readFile(infoPath));
    } else {
        cleanInfo = sdvSynthesizeSaveGameInfoFromMainData(cleanMain);
    }

    NSString *expDir = join(NSTemporaryDirectory(), @"GardenSaveExports");
    mkdirs(expDir);
    NSString *suffix = (exportMode == 1) ? @"_iOS_Backup" : ((exportMode == 2) ? @"_Mobile" : @"_CrossPlatform");
    NSString *zipPath = join(expDir, [NSString stringWithFormat:@"%@%@.zip", folderName, suffix]);
    removeItem(zipPath);

    mz_zip_archive z = {0};
    if (!mz_zip_writer_init_file(&z, zipPath.fileSystemRepresentation, 0)) {
        fail(@"Không thể tạo tệp nén .ZIP xuất Save.");
    }

    BOOL ok = YES;
    @try {
        NSString *mainEntry = [NSString stringWithFormat:@"%@/%@", folderName, folderName];
        ok = mz_zip_writer_add_mem(&z, mainEntry.UTF8String, cleanMain.bytes, cleanMain.length, MZ_DEFAULT_COMPRESSION);
        if (!ok) fail(@"Lỗi nén dữ liệu chính của Save.");

        if (cleanInfo.length) {
            NSString *infoEntry = [NSString stringWithFormat:@"%@/SaveGameInfo", folderName];
            ok = mz_zip_writer_add_mem(&z, infoEntry.UTF8String, cleanInfo.bytes, cleanInfo.length, MZ_DEFAULT_COMPRESSION);
            if (!ok) fail(@"Lỗi nén SaveGameInfo.");
        }

        if (exportMode == 2) {
            // Vanilla Mobile compatibility: also include clean _old mirrors expected by some mobile loaders
            NSString *mainOld = [NSString stringWithFormat:@"%@/%@_old", folderName, folderName];
            mz_zip_writer_add_mem(&z, mainOld.UTF8String, cleanMain.bytes, cleanMain.length, MZ_DEFAULT_COMPRESSION);
            if (cleanInfo.length) {
                NSString *infoOld = [NSString stringWithFormat:@"%@/SaveGameInfo_old", folderName];
                mz_zip_writer_add_mem(&z, infoOld.UTF8String, cleanInfo.bytes, cleanInfo.length, MZ_DEFAULT_COMPRESSION);
            }
        } else if (exportMode == 1) {
            // Full Garden iOS backup: pack the REAL QuickSave JSON from Mods/SDViOSTouchControls/data/<folder>_quicksave.json!
            NSString *liveQs = sdvQuickSaveJsonPath(docsPath, folderName);
            BOOL packedQs = NO;
            if (liveQs.length && [fm() fileExistsAtPath:liveQs] && !dir(liveQs)) {
                NSData *qd = [NSData dataWithContentsOfFile:liveQs];
                if (qd.length > 2) {
                    NSString *entry = [NSString stringWithFormat:@"%@/%@_quicksave.json", folderName, folderName];
                    mz_zip_writer_add_mem(&z, entry.UTF8String, qd.bytes, qd.length, MZ_DEFAULT_COMPRESSION);
                    packedQs = YES;
                }
            }
            if (!packedQs) {
                for (NSString *sub in ([fm() contentsOfDirectoryAtPath:saveFolderPath error:nil] ?: @[])) {
                    NSString *lo = sub.lowercaseString;
                    if ([lo containsString:@"quicksave"] && [lo hasSuffix:@".json"]) {
                        NSData *qd = [NSData dataWithContentsOfFile:join(saveFolderPath, sub)];
                        if (qd.length > 2) {
                            NSString *entry = [NSString stringWithFormat:@"%@/%@", folderName, sub];
                            mz_zip_writer_add_mem(&z, entry.UTF8String, qd.bytes, qd.length, MZ_DEFAULT_COMPRESSION);
                            break;
                        }
                    }
                }
            }
        }

        if (!mz_zip_writer_finalize_archive(&z)) {
            fail(@"Lỗi hoàn tất tệp .ZIP.");
        }
    } @finally {
        mz_zip_writer_end(&z);
    }

    return [NSURL fileURLWithPath:zipPath];
}
