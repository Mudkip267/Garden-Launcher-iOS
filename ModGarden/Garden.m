#import <Foundation/Foundation.h>
#import <CommonCrypto/CommonDigest.h>
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
    NSError *e = nil;
    id j = nil;
    if (@available(iOS 15.0, *)) {
        j = [NSJSONSerialization JSONObjectWithData:raw options:NSJSONReadingJSON5Allowed error:&e];
    } else {
        j = [NSJSONSerialization JSONObjectWithData:stripJson5ForIOS14(raw) options:NSJSONReadingAllowFragments error:&e];
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

static void syncBundledSmapi(NSString *docsPath) {
    if (!docsPath.length) return;
    @try {
        NSString *smapiRoot = join(join(join(NSBundle.mainBundle.bundlePath, @"Frameworks"), @"SDVModGarden.framework"), @"BundledSMAPI");
        if (!dir(smapiRoot)) return;
        [fm() createDirectoryAtPath:docsPath withIntermediateDirectories:YES attributes:nil error:nil];

        // 1. Deploy open-source StardewModdingAPI.dll (SMAPI 4.5.2, iOS-verified) if missing
        NSString *srcDll = join(smapiRoot, @"StardewModdingAPI.dll");
        NSString *dstDll = join(docsPath, @"StardewModdingAPI.dll");
        if ([fm() fileExistsAtPath:srcDll] && ![fm() fileExistsAtPath:dstDll]) {
            NSData *d = [NSData dataWithContentsOfFile:srcDll options:NSDataReadingMappedIfSafe error:nil];
            if (d.length) [d writeToFile:dstDll options:NSDataWritingAtomic error:nil];
        }

        // 2. Deploy smapi-internal/ files (preserving existing user config.json if present)
        NSString *srcInternal = join(smapiRoot, @"smapi-internal");
        NSString *dstInternal = join(docsPath, @"smapi-internal");
        if (dir(srcInternal)) {
            NSDirectoryEnumerator *en = [fm() enumeratorAtPath:srcInternal];
            for (NSString *rel in en) {
                if (![en.fileAttributes.fileType isEqualToString:NSFileTypeRegular]) continue;
                NSString *dst = join(dstInternal, rel);
                if ([fm() fileExistsAtPath:dst]) continue;
                [fm() createDirectoryAtPath:dst.stringByDeletingLastPathComponent withIntermediateDirectories:YES attributes:nil error:nil];
                NSData *d = [NSData dataWithContentsOfFile:join(srcInternal, rel) options:NSDataReadingMappedIfSafe error:nil];
                if (d) [d writeToFile:dst options:NSDataWritingAtomic error:nil];
            }
        }
    } @catch (NSException *ex) { }
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

static NSString *clean(NSString *path) {
    if (![path isKindOfClass:NSString.class] || !path.length || path.length>1400) fail(@"Đường dẫn tệp trống hoặc quá dài.");
    for (NSString *s in [path componentsSeparatedByString:@"/"]) {
        if (!s.length || [s isEqual:@"."] || [s isEqual:@".."] || [s containsString:@":"] || [s containsString:@"\\"] || [s rangeOfCharacterFromSet:NSCharacterSet.controlCharacterSet].location!=NSNotFound) fail(@"Gói chứa đường dẫn không hợp lệ.");
    }
    return path;
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
static NSString *unpack(NSURL *url, NSString *tmp) {
    mkdirs(tmp); NSString *ext=url.pathExtension.lowercaseString;
    if([ext isEqual:@"xnb"]) { writeFile(readFile(url.path),inside(tmp,url.lastPathComponent)); return tmp; }
    if(![ext isEqual:@"zip"]) fail([NSString stringWithFormat:@"Tệp '%@' không hỗ trợ. Chỉ chọn tệp .ZIP hoặc .XNB.", url.lastPathComponent]);
    mz_zip_archive z={0}; if(!mz_zip_reader_init_file(&z,url.fileSystemRepresentation,0)) fail([@"Không đọc được ZIP: " stringByAppendingString:url.lastPathComponent]);
    @try {
        uint64_t total=0; unsigned count=mz_zip_reader_get_num_files(&z); if(count>30000) fail(@"Gói vượt 30.000 mục.");
        NSMutableSet *seen=[NSMutableSet set];
        for(unsigned i=0;i<count;i++) { @autoreleasepool {
            mz_zip_archive_file_stat st; if(!mz_zip_reader_file_stat(&z,i,&st)) fail(@"ZIP bị hỏng.");
            if(st.m_is_directory) continue;
            unsigned n=mz_zip_reader_get_filename(&z,i,NULL,0); if(n>4096 || n<2) fail(@"Tên tệp ZIP không hợp lệ.");
            char name[4096]; mz_zip_reader_get_filename(&z,i,name,n);
            NSString *r=[[NSString alloc] initWithBytes:name length:n-1 encoding:NSUTF8StringEncoding]; if(!r) fail(@"Tên tệp ZIP cần dùng UTF-8.");
            r=clean([r stringByReplacingOccurrencesOfString:@"\\" withString:@"/"]);
            if([r.pathComponents containsObject:@"__MACOSX"] || [r.lastPathComponent isEqual:@".DS_Store"]) continue;
            if(st.m_is_encrypted || !st.m_is_supported || ((st.m_external_attr>>16)&0170000)==0120000) fail(@"ZIP chứa mục không hỗ trợ.");
            if(st.m_uncomp_size>1024ULL*1024*1024 || (total+=st.m_uncomp_size)>4ULL*1024*1024*1024) fail(@"Gói vượt giới hạn giải nén (4 GB).");
            // If ZIP contains duplicate case-insensitive entries, safely overwrite instead of failing
            [seen addObject:r.lowercaseString];
            NSString *p=inside(tmp,r); mkdirs(p.stringByDeletingLastPathComponent);
            if(!mz_zip_reader_extract_to_file(&z,i,p.fileSystemRepresentation,0)) fail(@"Giải nén thất bại. Kiểm tra ZIP và dung lượng trống.");
        }}
    } @finally { mz_zip_reader_end(&z); }
    return tmp;
}

static NSUInteger importGameDataZip(NSURL *url, NSString *docsPath) {
    if (!docsPath.length) fail(@"Không tìm thấy thư mục Documents của game.");
    NSString *ext = url.pathExtension.lowercaseString;
    if (![ext isEqualToString:@"zip"]) fail(@"Vui lòng chọn tệp nén định dạng .ZIP chứa dữ liệu game PC 1.6.15.");
    mz_zip_archive z = {0};
    if (!mz_zip_reader_init_file(&z, url.fileSystemRepresentation, 0)) {
        fail([@"Không mở được tệp ZIP: " stringByAppendingString:url.lastPathComponent ?: @"game.zip"]);
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
            fail(@"Gói ZIP này không chứa dữ liệu game PC (Stardew Valley.dll, Content/ hoặc SMAPI). Nếu đây là file mod, hãy dùng nút Chọn file Mod!");
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

            // Protect existing user saves & built-in touch controls mod
            if ([topLow isEqualToString:@"saves"]) continue;
            if ([topLow isEqualToString:@"mods"] && relParts.count >= 2 && [relParts[1] caseInsensitiveCompare:@"SDViOSTouchControls"] == NSOrderedSame) continue;

            // Skip desktop native binaries & desktop .NET BCL DLLs already provided by SDViOS.app for iOS
            if (relParts.count == 1) {
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
    return written;
}
static NSString *field(NSDictionary *m, NSString *key) { id v=m[key]; return [v isKindOfClass:NSString.class]?v:@""; }
static NSDictionary *manifest(NSString *p) {
    id m=jsonAt(p); if(![m isKindOfClass:NSDictionary.class] || !field(m,@"UniqueID").length) fail([@"Manifest thiếu UniqueID trong " stringByAppendingString:p.stringByDeletingLastPathComponent.lastPathComponent]); return m;
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
    for (NSString *r in items) {
        if (![r.lastPathComponent.lowercaseString isEqual:@"manifest.json"]) continue;
        NSDictionary *m = manifest(inside(stage, r));
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

    NSArray *bases = [incoming.allKeys sortedArrayUsingComparator:^NSComparisonResult(NSString *a, NSString *b) {
        return [@(b.length) compare:@(a.length)];
    }];

    // Pass 2: Check dependencies against BOTH already-installed mods AND mods in this same batch!
    for (NSString *base in bases) {
        NSDictionary *m = incoming[base][@"m"];
        NSMutableArray *deps = [NSMutableArray array];
        if ([m[@"Dependencies"] isKindOfClass:NSArray.class]) [deps addObjectsFromArray:m[@"Dependencies"]];
        if ([m[@"ContentPackFor"] isKindOfClass:NSDictionary.class]) [deps addObject:m[@"ContentPackFor"]];
        for (id dep in deps) {
            if (![dep isKindOfClass:NSDictionary.class]) continue;
            NSString *uid = field(dep, @"UniqueID");
            if (uid.length && ![dep[@"IsRequired"] isEqual:@NO] && !combinedLibrary[uid.lowercaseString]) {
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

    // Pass 3b: Map standalone i18n translation packs (matching installed OR same-batch mods!) and XNB files
    NSArray *assets = nil;
    NSMutableSet *addedExtraNames = [NSMutableSet set];
    for (NSString *r in remaining) { @autoreleasepool {
        NSArray *parts = r.pathComponents;
        NSUInteger idx = [parts indexOfObjectPassingTest:^BOOL(NSString *s, NSUInteger i, BOOL *stop) {
            return [s.lowercaseString isEqual:@"i18n"];
        }];
        if (idx != NSNotFound && [r.pathExtension.lowercaseString isEqual:@"json"]) {
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
                } else {
                    if (!combinedLibrary.count) fail(@"Cài mod gốc trước (hoặc chọn chung với mod gốc) khi thêm bản dịch i18n.");
                    translationOwner = owner;
                    break;
                }
            }
            if (![jsonAt(inside(stage, r)) isKindOfClass:NSDictionary.class]) fail(@"Tệp dịch không hợp lệ.");
            NSString *targetDir = matches[0];
            add(join(join(@"Mods", targetDir), [NSString pathWithComponents:[parts subarrayWithRange:NSMakeRange(idx, parts.count - idx)]]), r);
            NSString *lbl = [NSString stringWithFormat:@"Bản dịch i18n → %@", targetDir.lastPathComponent];
            if (![addedExtraNames containsObject:lbl]) { [addedExtraNames addObject:lbl]; [names addObject:lbl]; }
            continue;
        }

        if (![r.pathExtension.lowercaseString isEqual:@"xnb"]) continue;
        NSData *data = readFile(inside(stage, r));
        if (data.length < 10 || memcmp(data.bytes, "XNB", 3)) fail([@"Tệp XNB không hợp lệ: " stringByAppendingString:r.lastPathComponent]);
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
        if (!matches.count) fail([@"Không tìm thấy XNB tương ứng trong Content: " stringByAppendingString:r.lastPathComponent]);
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
        fail(@"Gói cần có manifest.json, bản dịch i18n hoặc tệp XNB có trong Content.");
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
