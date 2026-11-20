#import "Garden.m"

static NSString *history(NSURL *root) {
    NSString *support=[fm() URLsForDirectory:NSApplicationSupportDirectory inDomains:NSUserDomainMask].firstObject.path;
    NSString *key=hashData([root.path.stringByStandardizingPath dataUsingEncoding:NSUTF8StringEncoding]);
    return join(join(support,@"GardenHistory"),key);
}
static NSString *latest(NSURL *root) {
    NSString *base=history(root);
    NSArray *dirs=[[fm() contentsOfDirectoryAtPath:base error:nil] sortedArrayUsingSelector:@selector(compare:)];
    for(NSString *d in dirs.reverseObjectEnumerator) if([fm() fileExistsAtPath:join(join(base,d),@"journal.plist")]) return join(base,d);
    return nil;
}
static NSDictionary *journal(NSString *p) { NSDictionary *j=[NSDictionary dictionaryWithContentsOfFile:join(p,@"journal.plist")]; if(![j[@"rows"] isKindOfClass:NSArray.class]) fail(@"Bản sao lưu không đọc được."); return j; }
static void restore(NSURL *root,NSString *h) {
    NSArray *rows=journal(h)[@"rows"];
    for(NSDictionary *row in rows) { @autoreleasepool {
        NSString *p=gamePath(root.path,row[@"path"]); NSString *hash=[fm() fileExistsAtPath:p]?hashData(readFile(p)):@"";
        if(![hash isEqual:row[@"before"]] && ![hash isEqual:row[@"after"]]) fail([@"Tệp đã được sửa sau khi cài. Đã dừng hoàn tác: " stringByAppendingString:row[@"path"]]);
        if([row[@"before"] length] && ![hashData(readFile(join(h,row[@"backup"]))) isEqual:row[@"before"]]) fail(@"Bản sao lưu bị hỏng.");
    }}
    removeItem(join(h,@"done"));
    for(NSDictionary *row in rows.reverseObjectEnumerator) { @autoreleasepool {
        NSString *p=gamePath(root.path,row[@"path"]);
        if([row[@"before"] length]) writeFile(readFile(join(h,row[@"backup"])),p); else removeItem(p);
    }}
    for(NSString *r in journal(h)[@"dirs"]) {
        NSString *p=gamePath(root.path,r); if(dir(p) && [fm() contentsOfDirectoryAtPath:p error:nil].count==0) removeItem(p);
    }
    removeItem(h);
}
static NSUInteger installBatch(NSURL *root, NSString *stage, NSDictionary *map, NSArray *obsoleteDisabled, NSString *name) {
    NSString *prev=latest(root); if(prev && ![fm() fileExistsAtPath:join(prev,@"done")]) fail(@"Có lần cài bị gián đoạn. Chọn Hoàn tác trước.");
    // Remove any obsolete disabled ".FolderName" copies of mods being freshly reinstalled
    for (NSString *oldDot in obsoleteDisabled) {
        if ([oldDot hasPrefix:@"."]) {
            NSString *p = gamePath(root.path, join(@"Mods", oldDot));
            if ([fm() fileExistsAtPath:p]) removeItem(p);
        }
    }
    NSString *stamp=[NSString stringWithFormat:@"%020lld-%@",(long long)(NSDate.date.timeIntervalSince1970*1000000),NSUUID.UUID.UUIDString];
    NSString *h=join(history(root),stamp); mkdirs(h);
    NSMutableArray *rows=[NSMutableArray array]; NSMutableSet *dirs=[NSMutableSet set];
    @try {
        for(NSString *r in [map.allKeys sortedArrayUsingSelector:@selector(compare:)]) { @autoreleasepool {
            NSString *dest=gamePath(root.path,r), *backup=[NSString stringWithFormat:@"%lu.bin",(unsigned long)rows.count];
            NSData *replacement=readFile(inside(stage,map[r])); NSString *after=hashData(replacement), *before=@"";
            if([fm() fileExistsAtPath:dest]) { NSData *old=readFile(dest); before=hashData(old); if([before isEqual:after]) continue; writeFile(old,join(h,backup)); }
            for(NSString *d=parent(r);d.length && !dir(gamePath(root.path,d));d=parent(d)) [dirs addObject:d];
            [rows addObject:@{@"path":r,@"backup":backup,@"before":before,@"after":after}];
        }}
        if(!rows.count) {
            removeItem(h);
            return 0;
        }
        NSArray *ordered=[dirs.allObjects sortedArrayUsingComparator:^NSComparisonResult(NSString *a,NSString *b){return [@(b.length) compare:@(a.length)];}];
        NSDictionary *j=@{@"name":name,@"rows":rows,@"dirs":ordered};
        if(![j writeToFile:join(h,@"journal.plist") atomically:YES]) fail(@"Không lưu được lịch sử.");
        for(NSDictionary *row in rows) { @autoreleasepool { NSString *r=row[@"path"]; writeFile(readFile(inside(stage,map[r])),gamePath(root.path,r)); }}
        writeFile(NSData.data,join(h,@"done"));
        return rows.count;
    } @catch(NSException *ex) {
        if([fm() fileExistsAtPath:join(h,@"journal.plist")]) restore(root,h); else removeItem(h);
        @throw ex;
    }
}
