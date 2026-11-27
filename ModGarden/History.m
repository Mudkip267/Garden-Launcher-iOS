#import "Garden.m"

static NSString *history(NSURL *root) {
    NSString *support = [fm() URLsForDirectory:NSApplicationSupportDirectory inDomains:NSUserDomainMask].firstObject.path;
    NSString *histRoot = join(support, @"GardenHistory");
    static dispatch_once_t onceBackupExcl;
    dispatch_once(&onceBackupExcl, ^{
        mkdirs(histRoot);
        NSURL *u = [NSURL fileURLWithPath:histRoot isDirectory:YES];
        [u setResourceValue:@YES forKey:NSURLIsExcludedFromBackupKey error:nil];
    });
    NSString *key = hashData([root.path.stringByStandardizingPath dataUsingEncoding:NSUTF8StringEncoding]);
    return join(histRoot, key);
}

static void pruneOldCompletedHistory(NSURL *root, NSUInteger keepMax) {
    NSString *base = history(root);
    if (!dir(base)) return;
    NSArray<NSString *> *sorted = [[fm() contentsOfDirectoryAtPath:base error:nil] sortedArrayUsingSelector:@selector(compare:)];
    NSMutableArray<NSString *> *donePaths = [NSMutableArray array];
    for (NSString *d in sorted) {
        NSString *hp = join(base, d);
        if (dir(hp) && [fm() fileExistsAtPath:join(hp, @"done")]) {
            [donePaths addObject:hp];
        }
    }
    while (donePaths.count > keepMax) {
        sdvRemoveItemQuiet(donePaths.firstObject);
        [donePaths removeObjectAtIndex:0];
    }
}

static NSString *safeHistoryFileHash(NSString *p, BOOL *outIsDir) {
    BOOL isD = NO;
    if (!p.length || ![fm() fileExistsAtPath:p isDirectory:&isD]) {
        if (outIsDir) *outIsDir = NO;
        return @"";
    }
    if (outIsDir) *outIsDir = isD;
    if (isD) return @"";
    NSData *d = [NSData dataWithContentsOfFile:p options:NSDataReadingMappedIfSafe error:nil];
    return d ? hashData(d) : @"";
}

static void cleanupCorruptHistoryEntries(NSURL *root) {
    NSString *base = history(root);
    if (!dir(base)) return;
    NSArray<NSString *> *entries = [fm() contentsOfDirectoryAtPath:base error:nil] ?: @[];
    for (NSString *d in entries) {
        NSString *hp = join(base, d);
        if (!dir(hp)) {
            sdvRemoveItemQuiet(hp);
            continue;
        }
        NSString *jp = join(hp, @"journal.plist");
        if (![fm() fileExistsAtPath:jp] || dir(jp)) {
            sdvRemoveItemQuiet(hp);
            continue;
        }
        NSDictionary *j = [NSDictionary dictionaryWithContentsOfFile:jp];
        if (![j isKindOfClass:NSDictionary.class] || ![j[@"rows"] isKindOfClass:NSArray.class]) {
            sdvRemoveItemQuiet(hp);
        }
    }
}

static NSString *latest(NSURL *root) {
    cleanupCorruptHistoryEntries(root);
    NSString *base = history(root);
    NSArray *dirs = [[fm() contentsOfDirectoryAtPath:base error:nil] sortedArrayUsingSelector:@selector(compare:)];
    for (NSString *d in dirs.reverseObjectEnumerator) {
        NSString *hp = join(base, d);
        if ([fm() fileExistsAtPath:join(hp, @"journal.plist")]) return hp;
    }
    return nil;
}

static NSDictionary *journal(NSString *p) {
    NSDictionary *j = [NSDictionary dictionaryWithContentsOfFile:join(p, @"journal.plist")];
    if (![j isKindOfClass:NSDictionary.class] || ![j[@"rows"] isKindOfClass:NSArray.class]) {
        sdvRemoveItemQuiet(p);
        fail(@"Bản sao lưu không đọc được (đã tự động dọn dẹp mục lỗi).");
    }
    return j;
}

static void restoreInternal(NSURL *root, NSString *h, BOOL forceBestEffort) {
    if (!h.length || !dir(h)) return;
    NSDictionary *j = [NSDictionary dictionaryWithContentsOfFile:join(h, @"journal.plist")];
    if (![j isKindOfClass:NSDictionary.class] || ![j[@"rows"] isKindOfClass:NSArray.class]) {
        sdvRemoveItemQuiet(h);
        if (!forceBestEffort) fail(@"Bản sao lưu không đọc được (đã tự động dọn dẹp mục lỗi).");
        return;
    }
    NSArray *rows = j[@"rows"];
    BOOL wasCompleted = [fm() fileExistsAtPath:join(h, @"done")];
    BOOL lenient = forceBestEffort || !wasCompleted;

    if (!lenient) {
        for (NSDictionary *row in rows) { @autoreleasepool {
            if (![row isKindOfClass:NSDictionary.class]) continue;
            NSString *relPath = [row[@"path"] isKindOfClass:NSString.class] ? row[@"path"] : @"";
            NSString *before = [row[@"before"] isKindOfClass:NSString.class] ? row[@"before"] : @"";
            NSString *after = [row[@"after"] isKindOfClass:NSString.class] ? row[@"after"] : @"";
            NSString *backup = [row[@"backup"] isKindOfClass:NSString.class] ? row[@"backup"] : @"";
            if (!relPath.length) continue;

            NSString *p = gamePath(root.path, relPath);
            BOOL isD = NO;
            NSString *hash = safeHistoryFileHash(p, &isD);
            BOOL alreadyDeletedNewFile = (!before.length && !hash.length && !isD);
            if (!alreadyDeletedNewFile && ![hash isEqualToString:before] && ![hash isEqualToString:after]) {
                fail([@"Tệp đã được sửa sau khi cài. Đã dừng hoàn tác: " stringByAppendingString:relPath]);
            }
            if (before.length) {
                NSString *bkHash = safeHistoryFileHash(join(h, backup), NULL);
                if (![bkHash isEqualToString:before]) {
                    fail(@"Bản sao lưu bị hỏng.");
                }
            }
        }}
    }

    sdvRemoveItemQuiet(join(h, @"done"));
    for (NSDictionary *row in rows.reverseObjectEnumerator) { @autoreleasepool {
        if (![row isKindOfClass:NSDictionary.class]) continue;
        NSString *relPath = [row[@"path"] isKindOfClass:NSString.class] ? row[@"path"] : @"";
        NSString *before = [row[@"before"] isKindOfClass:NSString.class] ? row[@"before"] : @"";
        NSString *after = [row[@"after"] isKindOfClass:NSString.class] ? row[@"after"] : @"";
        NSString *backup = [row[@"backup"] isKindOfClass:NSString.class] ? row[@"backup"] : @"";
        if (!relPath.length) continue;

        @try {
            NSString *p = gamePath(root.path, relPath);
            if (before.length) {
                NSString *bkPath = join(h, backup);
                NSData *bkData = (!dir(bkPath) && [fm() fileExistsAtPath:bkPath])
                    ? [NSData dataWithContentsOfFile:bkPath options:NSDataReadingMappedIfSafe error:nil]
                    : nil;
                if (bkData && (!lenient || [hashData(bkData) isEqualToString:before])) {
                    writeFile(bkData, p);
                }
            } else {
                BOOL isD = NO;
                NSString *curHash = safeHistoryFileHash(p, &isD);
                if (!lenient || (!isD && [curHash isEqualToString:after])) {
                    if (lenient) sdvRemoveItemQuiet(p);
                    else removeItem(p);
                }
            }
        } @catch (NSException *ignored) {
            if (!lenient) @throw ignored;
        }
    }}

    NSArray *createdDirs = [j[@"dirs"] isKindOfClass:NSArray.class] ? j[@"dirs"] : @[];
    for (NSString *r in createdDirs) {
        if (![r isKindOfClass:NSString.class] || !r.length) continue;
        @try {
            NSString *p = gamePath(root.path, r);
            if (dir(p) && [fm() contentsOfDirectoryAtPath:p error:nil].count == 0) {
                sdvRemoveItemQuiet(p);
            }
        } @catch (NSException *ignored) { }
    }
    if (lenient) sdvRemoveItemQuiet(h);
    else removeItem(h);
}

static void restore(NSURL *root, NSString *h) {
    restoreInternal(root, h, NO);
}

static NSUInteger installBatch(NSURL *root, NSString *stage, NSDictionary *map, NSArray *obsoleteDisabled, NSString *name) {
    // Auto-recover any previously interrupted (!done) or corrupt install journal so future mod installs NEVER deadlock!
    NSString *prev = latest(root);
    while (prev && ![fm() fileExistsAtPath:join(prev, @"done")]) {
        restoreInternal(root, prev, YES);
        NSString *nextPrev = latest(root);
        if ([nextPrev isEqualToString:prev]) {
            sdvRemoveItemQuiet(prev);
            break;
        }
        prev = nextPrev;
    }

    // Remove any obsolete disabled ".FolderName" copies of mods being freshly reinstalled
    for (NSString *oldDot in obsoleteDisabled) {
        if ([oldDot hasPrefix:@"."]) {
            NSString *p = gamePath(root.path, join(@"Mods", oldDot));
            if ([fm() fileExistsAtPath:p]) removeItem(p);
        }
    }
    NSString *stamp = [NSString stringWithFormat:@"%020lld-%@", (long long)(NSDate.date.timeIntervalSince1970 * 1000000), NSUUID.UUID.UUIDString];
    NSString *h = join(history(root), stamp);
    mkdirs(h);
    NSMutableArray *rows = [NSMutableArray array];
    NSMutableSet *dirs = [NSMutableSet set];
    @try {
        for (NSString *r in [map.allKeys sortedArrayUsingSelector:@selector(compare:)]) { @autoreleasepool {
            NSString *dest = gamePath(root.path, r);
            NSString *backup = [NSString stringWithFormat:@"%lu.bin", (unsigned long)rows.count];
            NSData *replacement = readFile(inside(stage, map[r]));
            NSString *after = hashData(replacement);
            NSString *before = @"";
            if (dir(dest)) sdvRemoveItemQuiet(dest);
            if ([fm() fileExistsAtPath:dest]) {
                NSData *old = readFile(dest);
                before = hashData(old);
                if ([before isEqual:after]) continue;
                writeFile(old, join(h, backup));
            }
            for (NSString *d = parent(r); d.length && !dir(gamePath(root.path, d)); d = parent(d)) {
                [dirs addObject:d];
            }
            [rows addObject:@{@"path": r, @"backup": backup, @"before": before, @"after": after}];
        }}
        if (!rows.count) {
            sdvRemoveItemQuiet(h);
            return 0;
        }
        NSArray *ordered = [dirs.allObjects sortedArrayUsingComparator:^NSComparisonResult(NSString *a, NSString *b) {
            return [@(b.length) compare:@(a.length)];
        }];
        NSDictionary *j = @{@"name": name ?: @"Mod", @"rows": rows, @"dirs": ordered};
        if (![j writeToFile:join(h, @"journal.plist") atomically:YES]) fail(@"Không lưu được lịch sử.");
        for (NSDictionary *row in rows) { @autoreleasepool {
            NSString *r = row[@"path"];
            writeFile(readFile(inside(stage, map[r])), gamePath(root.path, r));
        }}
        writeFile(NSData.data, join(h, @"done"));
        pruneOldCompletedHistory(root, 8);
        return rows.count;
    } @catch (NSException *ex) {
        @try {
            if ([fm() fileExistsAtPath:join(h, @"journal.plist")]) {
                restoreInternal(root, h, YES);
            } else {
                sdvRemoveItemQuiet(h);
            }
        } @catch (NSException *ignored) {
            sdvRemoveItemQuiet(h);
        }
        @throw ex;
    }
}
