#import <UIKit/UIKit.h>
#import <QuartzCore/QuartzCore.h>
#import <CoreText/CoreText.h>

// ---- Color Helpers ----
static UIColor *rgb(unsigned hex) {
    return [UIColor colorWithRed:((hex >> 16) & 255) / 255.0
                           green:((hex >> 8) & 255) / 255.0
                            blue:(hex & 255) / 255.0
                           alpha:1.0];
}
static UIColor *rgba(unsigned hex, CGFloat a) {
    return [UIColor colorWithRed:((hex >> 16) & 255) / 255.0
                           green:((hex >> 8) & 255) / 255.0
                            blue:(hex & 255) / 255.0
                           alpha:a];
}

// ---- Stardew Valley Authentic Wood & Parchment Pixel Palette ----
#define kPxOutline      rgb(0x261106)
#define kPxWoodDark     rgb(0x3B1D0E)
#define kPxWoodMid      rgb(0x5C2F16)
#define kPxWoodHi       rgb(0x8D5028)
#define kPxWoodLo       rgb(0x311608)

#define kBgCanvas       rgb(0x3D1E0F)
#define kBgSidebar      rgb(0x5A2D15)
#define kBgCard         rgb(0xF5D99B)
#define kBgCardAlt      rgb(0xFCEBC2)
#define kBgCardWarm     rgb(0xFFE394)
#define kBgCardLeaf     rgb(0xDCF2C6)
#define kBgCardBerry    rgb(0xFBD2C6)
#define kBgCardDim      rgb(0xDEC086)

#define kBorderSubtle   rgb(0x6E3B1B)
#define kBorderGold     rgb(0xA85B10)
#define kBorderMint     rgb(0x2F742C)
#define kBorderBerry    rgb(0x9E2F24)

#define kInkPrimary     rgb(0x3A1C0B)
#define kInkSecondary   rgb(0x66381A)
#define kInkMuted       rgb(0x865632)
#define kInkOnWood      rgb(0xFFF3D4)
#define kInkGoldOnWood  rgb(0xFFD966)

#define kAccentMint     rgb(0x469E3B)
#define kAccentMintHi   rgb(0x267428)
#define kAccentGold     rgb(0xB35A0B)
#define kAccentGoldBtn  rgb(0xE89B22)
#define kAccentCoral    rgb(0xBD382B)

// ---- Pixel Font Registration (Handjet Bold/SemiBold + VT323 Body, 100% Vietnamese!) ----
static BOOL sPxHandjetLoaded = NO;
static BOOL sPxVT323Loaded = NO;

static void sdvRegisterFontFile(NSString *fileName, BOOL *flagOut) {
    NSArray<NSBundle *> *bundles = @[
        [NSBundle bundleForClass:NSClassFromString(@"SDVGardenManagerController") ?: NSObject.class],
        NSBundle.mainBundle
    ];
    for (NSBundle *b in bundles) {
        NSString *p = [b pathForResource:fileName.stringByDeletingPathExtension ofType:fileName.pathExtension];
        if (!p.length) continue;
        NSURL *u = [NSURL fileURLWithPath:p];
        CFErrorRef err = NULL;
        BOOL ok = CTFontManagerRegisterFontsForURL((__bridge CFURLRef)u, kCTFontManagerScopeProcess, &err);
        if (err) {
            CFIndex code = CFErrorGetCode(err);
            if (code == kCTFontManagerErrorAlreadyRegistered) ok = YES;
            CFRelease(err);
        }
        if (ok) { *flagOut = YES; return; }
    }
}

static void sdvEnsurePixelFonts(void) {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        sdvRegisterFontFile(@"Handjet.ttf", &sPxHandjetLoaded);
        sdvRegisterFontFile(@"VT323-Regular.ttf", &sPxVT323Loaded);
    });
}

static UIFont *pxFont(CGFloat size, UIFontWeight weight) {
    sdvEnsurePixelFonts();
    BOOL boldish = (weight >= UIFontWeightSemibold);
    if (boldish && sPxHandjetLoaded) {
        CGFloat wghtVal = (weight >= UIFontWeightHeavy) ? 800.0 : ((weight >= UIFontWeightBold) ? 700.0 : 600.0);
        CGFloat pt = round(size * 1.24);
        NSDictionary *axes = @{
            @(0x77676874): @(wghtVal), // 'wght'
            @(0x454C5348): @(2.0)      // 'ELSH' crisp square pixel
        };
        UIFontDescriptor *base = [UIFontDescriptor fontDescriptorWithFontAttributes:@{
            UIFontDescriptorNameAttribute: @"Handjet-Regular",
            (NSString *)kCTFontVariationAttribute: axes
        }];
        UIFont *f = [UIFont fontWithDescriptor:base size:pt];
        if (f) return f;
    }
    if (sPxVT323Loaded) {
        CGFloat pt = round(size * (boldish ? 1.34 : 1.30));
        UIFont *f = [UIFont fontWithName:@"VT323-Regular" size:pt];
        if (f) return f;
    }
    if (sPxHandjetLoaded) {
        UIFont *f = [UIFont fontWithName:@"Handjet-Regular" size:round(size * 1.22)];
        if (f) return f;
    }
    return [UIFont monospacedSystemFontOfSize:size weight:weight];
}

// ---- Tiled Pixel Wood Plank Texture ----
static UIColor *sdvWoodPatternColor(BOOL dark) {
    static UIColor *sDark = nil;
    static UIColor *sMid = nil;
    if (dark && sDark) return sDark;
    if (!dark && sMid) return sMid;

    int pw = 32, ph = 16;
    CGFloat scale = 2.0;
    UIGraphicsBeginImageContextWithOptions(CGSizeMake(pw * scale, ph * scale), YES, 1.0);
    CGContextRef ctx = UIGraphicsGetCurrentContext();
    CGContextSetShouldAntialias(ctx, false);

    UIColor *base  = dark ? rgb(0x3D1E0F) : rgb(0x5E3017);
    UIColor *hi    = dark ? rgb(0x4D2714) : rgb(0x733B1D);
    UIColor *seam  = dark ? rgb(0x281208) : rgb(0x3B1B0B);
    UIColor *grain = dark ? rgb(0x33180B) : rgb(0x502611);

    [base setFill];
    CGContextFillRect(ctx, CGRectMake(0, 0, pw * scale, ph * scale));

    void (^px)(int, int, int, int, UIColor *) = ^(int x, int y, int w, int h, UIColor *c) {
        [c setFill];
        CGContextFillRect(ctx, CGRectMake(x * scale, y * scale, w * scale, h * scale));
    };

    // Two staggered horizontal planks
    px(0, 0, pw, 1, seam);
    px(0, 1, pw, 1, hi);
    px(0, 8, pw, 1, seam);
    px(0, 9, pw, 1, hi);
    // Vertical plank joints
    px(15, 0, 1, 8, seam);
    px(16, 1, 1, 7, hi);
    px(31, 8, 1, 8, seam);
    px(0,  9, 1, 7, hi);
    // Subtle wood grain specks
    px(3, 3, 5, 1, grain);
    px(10, 5, 3, 1, grain);
    px(20, 4, 7, 1, grain);
    px(25, 6, 4, 1, grain);
    px(6, 11, 8, 1, grain);
    px(18, 13, 6, 1, grain);
    px(27, 11, 3, 1, grain);

    UIImage *tile = UIGraphicsGetImageFromCurrentImageContext();
    UIGraphicsEndImageContext();
    UIColor *pat = [UIColor colorWithPatternImage:tile];
    if (dark) sDark = pat; else sMid = pat;
    return pat;
}

// ---- Hand-Crafted Crisp Pixel-Art Icons (CGInterpolationNone) ----
static UIImage *sdvRenderPixelGrid(NSArray<NSString *> *rows, NSDictionary<NSString *, UIColor *> *pal, CGFloat targetPt) {
    NSInteger gh = (NSInteger)rows.count;
    NSInteger gw = gh > 0 ? (NSInteger)rows[0].length : 0;
    if (gw <= 0 || gh <= 0) return nil;

    CGFloat scale = UIScreen.mainScreen.scale;
    CGFloat outPx = ceil(targetPt * scale);
    CGFloat cell = floor(outPx / MAX(gw, gh));
    if (cell < 1.0) cell = 1.0;
    CGFloat imgW = (gw * cell) / scale;
    CGFloat imgH = (gh * cell) / scale;

    UIGraphicsBeginImageContextWithOptions(CGSizeMake(imgW, imgH), NO, scale);
    CGContextRef ctx = UIGraphicsGetCurrentContext();
    CGContextSetShouldAntialias(ctx, false);
    CGContextSetInterpolationQuality(ctx, kCGInterpolationNone);

    CGFloat ptCell = cell / scale;
    for (NSInteger y = 0; y < gh; y++) {
        NSString *line = rows[y];
        for (NSInteger x = 0; x < gw && x < (NSInteger)line.length; x++) {
            NSString *ch = [line substringWithRange:NSMakeRange(x, 1)];
            if ([ch isEqualToString:@"."] || [ch isEqualToString:@" "]) continue;
            UIColor *col = pal[ch];
            if (!col) continue;
            [col setFill];
            CGContextFillRect(ctx, CGRectMake(x * ptCell, y * ptCell, ptCell, ptCell));
        }
    }
    UIImage *img = UIGraphicsGetImageFromCurrentImageContext();
    UIGraphicsEndImageContext();
    return [img imageWithRenderingMode:UIImageRenderingModeAlwaysOriginal];
}

static UIImage *sdvPixelIcon(NSString *name, CGFloat ptSize, UIColor *tint) {
    UIColor *c = tint ?: kInkPrimary;
    UIColor *o = kPxOutline;
    if ([name isEqualToString:@"junimo"]) {
        return sdvRenderPixelGrid(@[
            @".....oo.....",
            @"....oGGo....",
            @"..oooGooo...",
            @".oGGGGGGGo..",
            @"oGGLLLLGGGo.",
            @"oGoLGGGLoGo.",
            @"oGPGGGGGPGo.",
            @"oGGGGGGGGGo.",
            @".oGGDDDGGGo.",
            @"..ooGGGoo...",
            @"..o.....o...",
            @"............"
        ], @{@"o": o, @"G": rgb(0x5EC94B), @"L": rgb(0xA4F07D), @"D": rgb(0x368A2E), @"P": rgb(0xFF94A4)}, ptSize);
    }
    if ([name isEqualToString:@"chest"] || [name isEqualToString:@"shippingbox.fill"] || [name isEqualToString:@"plus.circle.fill"]) {
        return sdvRenderPixelGrid(@[
            @"..oooooooo..",
            @".oLLLLLLLLo.",
            @"oLLBBBBBBLLo",
            @"oooooooooooo",
            @"oBBBooooBBBo",
            @"oBBBoYYoBBBo",
            @"oBBBooooBBBo",
            @"oBBBBBBBBBBo",
            @"oBDDDDDDDDBo",
            @".oooooooooo.",
            @"............",
            @"............"
        ], @{@"o": o, @"B": rgb(0xB86F33), @"L": rgb(0xE09C52), @"D": rgb(0x7D4219), @"Y": rgb(0xFFD84D)}, ptSize);
    }
    if ([name isEqualToString:@"book"] || [name isEqualToString:@"square.grid.2x2.fill"]) {
        return sdvRenderPixelGrid(@[
            @"..oooooooo..",
            @".oRRRRRRRRo.",
            @".oRPPPPPPRo.",
            @".oRPPPPPRRo.",
            @".oRRRRRRRRo.",
            @".oRYYRYYRRo.",
            @".oRRRRRRRRo.",
            @".oRDDDDDDRo.",
            @".oWWWWWWWWo.",
            @"..oooooooo..",
            @"............",
            @"............"
        ], @{@"o": o, @"R": rgb(0x4E8E3E), @"P": rgb(0x88D468), @"D": rgb(0x2E5E24), @"Y": rgb(0xFFE066), @"W": rgb(0xFCEBC2)}, ptSize);
    }
    if ([name isEqualToString:@"gamepad"] || [name isEqualToString:@"slider.horizontal.3"]) {
        return sdvRenderPixelGrid(@[
            @"............",
            @"..oooooooo..",
            @".oGGGGGGGGoo",
            @"oGGWGGGGGYYo",
            @"oGWWWGGBGYYo",
            @"oGGWGGGGGGBo",
            @"oGGGGGGGGGBo",
            @"oGGo....oGGo",
            @".oo......oo.",
            @"............",
            @"............",
            @"............"
        ], @{@"o": o, @"G": rgb(0x6D7B8D), @"W": rgb(0xFFF3D4), @"Y": rgb(0xFFD24C), @"B": rgb(0xE35B4C)}, ptSize);
    }
    if ([name isEqualToString:@"wifi"] || [name isEqualToString:@"antenna.radiowaves.left.and.right"]) {
        return sdvRenderPixelGrid(@[
            @"............",
            @"..oooooooo..",
            @".oCCCCCCCCo.",
            @"oCCooooooCCo",
            @"oo.oCCCCo.oo",
            @"...oCooCo...",
            @"....oCCo....",
            @"....oYYo....",
            @".....oo.....",
            @"............",
            @"............",
            @"............"
        ], @{@"o": o, @"C": rgb(0x4EC4E8), @"Y": rgb(0xFFD84D)}, ptSize);
    }
    if ([name isEqualToString:@"scroll"] || [name isEqualToString:@"book.fill"]) {
        return sdvRenderPixelGrid(@[
            @".oooooooo...",
            @"oPPPPPPPPo..",
            @"oPBBBBBBPo..",
            @"oPPPPPPPPoo.",
            @"oPBBBBPPPYo.",
            @"oPPPPPPPYoo.",
            @"oPBBBBPPPo..",
            @"oPPPPPPPPo..",
            @".oooooooo...",
            @"............",
            @"............",
            @"............"
        ], @{@"o": o, @"P": rgb(0xFCEBC2), @"B": rgb(0x8C5630), @"Y": rgb(0xE89B22)}, ptSize);
    }
    if ([name isEqualToString:@"heart"] || [name isEqualToString:@"heart.fill"]) {
        return sdvRenderPixelGrid(@[
            @"............",
            @".ooo....ooo.",
            @"oRRRo..oRRRo",
            @"oRLRRoORRRRo",
            @"oRLLRRRRRRRo",
            @"oRRRRRRRRRRo",
            @".oRRRRRRRRo.",
            @"..oRRRRRRo..",
            @"...oRRRRo...",
            @"....oRRo....",
            @".....oo.....",
            @"............"
        ], @{@"o": o, @"R": rgb(0xE0463A), @"L": rgb(0xFF968A), @"O": rgb(0xE0463A)}, ptSize);
    }
    if ([name isEqualToString:@"star"] || [name isEqualToString:@"viewfinder"]) {
        return sdvRenderPixelGrid(@[
            @".....oo.....",
            @"....oYYo....",
            @"....oLYo....",
            @".ooooYYoooo.",
            @"oYYYYYYYYYYo",
            @".oYYYYYYYYo.",
            @"..oYYYYYYo..",
            @".oYYYooYYYo.",
            @".oYYo..oYYo.",
            @"..oo....oo..",
            @"............",
            @"............"
        ], @{@"o": o, @"Y": rgb(0xFFCB3B), @"L": rgb(0xFFF3A1)}, ptSize);
    }
    if ([name isEqualToString:@"play"] || [name isEqualToString:@"play.fill"]) {
        return sdvRenderPixelGrid(@[
            @"..oo........",
            @"..oCo.......",
            @"..oCCo......",
            @"..oCCCo.....",
            @"..oCCCCo....",
            @"..oCCCCCo...",
            @"..oCCCCo....",
            @"..oCCCo.....",
            @"..oCCo......",
            @"..oCo.......",
            @"..oo........",
            @"............"
        ], @{@"o": o, @"C": c}, ptSize);
    }
    if ([name isEqualToString:@"check"] || [name isEqualToString:@"checkmark"] || [name isEqualToString:@"checkmark.circle.fill"]) {
        return sdvRenderPixelGrid(@[
            @"............",
            @".........oo.",
            @"........oCCo",
            @".......oCCo.",
            @".oo...oCCo..",
            @"oCCo.oCCo...",
            @".oCCoCCo....",
            @"..oCCCo.....",
            @"...oCo......",
            @"....o.......",
            @"............",
            @"............"
        ], @{@"o": o, @"C": c}, ptSize);
    }
    if ([name isEqualToString:@"warn"] || [name isEqualToString:@"exclamationmark.triangle.fill"]) {
        return sdvRenderPixelGrid(@[
            @".....oo.....",
            @"....oYYo....",
            @"...oYYYYo...",
            @"...oYoYYo...",
            @"..oYYoYYYo..",
            @"..oYYoYYYo..",
            @".oYYYYYYYYo.",
            @".oYYYooYYYo.",
            @"oYYYYYYYYYYo",
            @"oooooooooooo",
            @"............",
            @"............"
        ], @{@"o": o, @"Y": rgb(0xFFBF36)}, ptSize);
    }
    if ([name isEqualToString:@"trash"]) {
        return sdvRenderPixelGrid(@[
            @"....oooo....",
            @".oooooooooo.",
            @".oCCCCCCCCo.",
            @"..oooooooo..",
            @"..oCoCCoCo..",
            @"..oCoCCoCo..",
            @"..oCoCCoCo..",
            @"..oCoCCoCo..",
            @"..oCCCCCCo..",
            @"...oooooo...",
            @"............",
            @"............"
        ], @{@"o": o, @"C": c}, ptSize);
    }
    if ([name isEqualToString:@"plus"] || [name isEqualToString:@"doc.badge.plus"]) {
        return sdvRenderPixelGrid(@[
            @"....oooo....",
            @"....oCCo....",
            @"....oCCo....",
            @"oooooCCooooo",
            @"oCCCCCCCCCCo",
            @"oCCCCCCCCCCo",
            @"oooooCCooooo",
            @"....oCCo....",
            @"....oCCo....",
            @"....oooo....",
            @"............",
            @"............"
        ], @{@"o": o, @"C": c}, ptSize);
    }
    if ([name isEqualToString:@"xmark"]) {
        return sdvRenderPixelGrid(@[
            @".oo......oo.",
            @"oCCo....oCCo",
            @".oCCo..oCCo.",
            @"..oCCooCCo..",
            @"...oCCCCo...",
            @"...oCCCCo...",
            @"..oCCooCCo..",
            @".oCCo..oCCo.",
            @"oCCo....oCCo",
            @".oo......oo.",
            @"............",
            @"............"
        ], @{@"o": o, @"C": c}, ptSize);
    }
    if ([name isEqualToString:@"arrow.left"]) {
        return sdvRenderPixelGrid(@[
            @".....oo.....",
            @"....oCo.....",
            @"...oCCo.....",
            @"..oCCCCoooo.",
            @".oCCCCCCCCo.",
            @"..oCCCCoooo.",
            @"...oCCo.....",
            @"....oCo.....",
            @".....oo.....",
            @"............"
        ], @{@"o": o, @"C": c}, ptSize);
    }
    if ([name isEqualToString:@"arrow.right"]) {
        return sdvRenderPixelGrid(@[
            @"....oo......",
            @"....oCo.....",
            @"....oCCo....",
            @"ooooCCCCo...",
            @"oCCCCCCCCo..",
            @"ooooCCCCo...",
            @"....oCCo....",
            @"....oCo.....",
            @"....oo......",
            @"............"
        ], @{@"o": o, @"C": c}, ptSize);
    }
    if ([name isEqualToString:@"arrow.up"]) {
        return sdvRenderPixelGrid(@[
            @"....oo......",
            @"...oCCo.....",
            @"..oCCCCo....",
            @".oCCCCCCo...",
            @"oCCCCCCCCo..",
            @"oooCCCCooo..",
            @"...oCCo.....",
            @"...oCCo.....",
            @"...oooo.....",
            @"............"
        ], @{@"o": o, @"C": c}, ptSize);
    }
    if ([name isEqualToString:@"arrow.down"] || [name isEqualToString:@"arrow.down.circle.fill"]) {
        return sdvRenderPixelGrid(@[
            @"...oooo.....",
            @"...oCCo.....",
            @"...oCCo.....",
            @"oooCCCCooo..",
            @"oCCCCCCCCo..",
            @".oCCCCCCo...",
            @"..oCCCCo....",
            @"...oCCo.....",
            @"....oo......",
            @"............"
        ], @{@"o": o, @"C": c}, ptSize);
    }
    if ([name isEqualToString:@"arrow.clockwise"] || [name isEqualToString:@"arrow.counterclockwise"] || [name isEqualToString:@"arrow.uturn.backward"]) {
        return sdvRenderPixelGrid(@[
            @"...oooooo...",
            @"..oCCCCCCo..",
            @".oCCooooCCo.",
            @"oCCo....oCCo",
            @"oCCo..ooCCCo",
            @"oCCo...oCCo.",
            @".oCCoooo..o.",
            @"..oCCCCCCo..",
            @"...oooooo...",
            @"............"
        ], @{@"o": o, @"C": c}, ptSize);
    }
    if ([name isEqualToString:@"doc.on.doc"] || [name isEqualToString:@"list.bullet"] || [name isEqualToString:@"arrow.up.right.square"] || [name isEqualToString:@"bag.fill"] || [name isEqualToString:@"desktopcomputer"] || [name isEqualToString:@"folder.fill"]) {
        return sdvRenderPixelGrid(@[
            @"..ooooooo...",
            @".oCCCCCCCo..",
            @".oCooooCCo..",
            @".oCCCCCCCo..",
            @".oCoooCCCo..",
            @".oCCCCCCCo..",
            @".oCooooCCo..",
            @".oCCCCCCCo..",
            @"..ooooooo...",
            @"............"
        ], @{@"o": o, @"C": c}, ptSize);
    }
    return sdvPixelIcon(@"star", ptSize, c);
}

// ---- Stepped-Corner 9-Slice Pixel Box View (Authentic Stardew Dialogue Frame!) ----
@interface SDVPixelBoxView : UIView
@property(nonatomic, strong) UIColor *fillColor;
@property(nonatomic, strong) UIColor *outlineColor;
@property(nonatomic, strong) UIColor *hiColor;
@property(nonatomic, strong) UIColor *loColor;
@property(nonatomic, assign) CGFloat pixelStep;
@property(nonatomic, assign) BOOL showRivets;
@end

@implementation SDVPixelBoxView
-(instancetype)initWithFrame:(CGRect)frame {
    self = [super initWithFrame:frame];
    if (self) {
        self.opaque = NO;
        self.backgroundColor = UIColor.clearColor;
        self.contentMode = UIViewContentModeRedraw;
        _pixelStep = 2.0;
        _fillColor = kBgCard;
        _outlineColor = kPxOutline;
        _hiColor = rgb(0xFFF3D1);
        _loColor = rgb(0xC5944D);
    }
    return self;
}

-(void)drawRect:(CGRect)rect {
    CGContextRef ctx = UIGraphicsGetCurrentContext();
    CGContextSetShouldAntialias(ctx, false);
    CGFloat p = MAX(1.5, _pixelStep);
    CGFloat w = self.bounds.size.width;
    CGFloat h = self.bounds.size.height;
    if (w < p * 5 || h < p * 5) return;

    // 1. Interior fill (inset by p, clipped away at the 4 notched corners)
    [_fillColor setFill];
    CGContextFillRect(ctx, CGRectMake(p, p, w - 2*p, h - 2*p));

    // 2. Inner 3D pixel bevels (top & left highlight; bottom & right shadow)
    if (_hiColor) {
        [_hiColor setFill];
        CGContextFillRect(ctx, CGRectMake(2*p, p, w - 4*p, p));
        CGContextFillRect(ctx, CGRectMake(p, 2*p, p, h - 4*p));
    }
    if (_loColor) {
        [_loColor setFill];
        CGContextFillRect(ctx, CGRectMake(2*p, h - 2*p, w - 4*p, p));
        CGContextFillRect(ctx, CGRectMake(w - 2*p, 2*p, p, h - 4*p));
    }

    // 3. Stepped notched pixel outer border (Stardew signature!)
    [_outlineColor setFill];
    // Top & bottom straight rails
    CGContextFillRect(ctx, CGRectMake(p, 0, w - 2*p, p));
    CGContextFillRect(ctx, CGRectMake(p, h - p, w - 2*p, p));
    // Left & right straight rails
    CGContextFillRect(ctx, CGRectMake(0, p, p, h - 2*p));
    CGContextFillRect(ctx, CGRectMake(w - p, p, p, h - 2*p));
    // 4 inner corner notch pixels
    CGContextFillRect(ctx, CGRectMake(p, p, p, p));
    CGContextFillRect(ctx, CGRectMake(w - 2*p, p, p, p));
    CGContextFillRect(ctx, CGRectMake(p, h - 2*p, p, p));
    CGContextFillRect(ctx, CGRectMake(w - 2*p, h - 2*p, p, p));

    // 4. Optional wooden/golden Stardew corner rivets
    if (_showRivets && w > p * 12 && h > p * 10) {
        [rgb(0xB86A22) setFill];
        CGContextFillRect(ctx, CGRectMake(2*p, 2*p, p, p));
        CGContextFillRect(ctx, CGRectMake(w - 3*p, 2*p, p, p));
        CGContextFillRect(ctx, CGRectMake(2*p, h - 3*p, p, p));
        CGContextFillRect(ctx, CGRectMake(w - 3*p, h - 3*p, p, p));
    }
}
@end

// ---- Tactile Chunky 3D Pixel Button (Presses Down Like Stardew Valley!) ----
@interface SDVPixelButton : UIButton
@property(nonatomic, strong) UIColor *pxFill;
@property(nonatomic, strong) UIColor *pxHi;
@property(nonatomic, strong) UIColor *pxLo;
@property(nonatomic, strong) UIColor *pxOutline;
@end

@implementation SDVPixelButton {
    UIEdgeInsets _baseInsets;
    BOOL _hasBaseInsets;
}

-(instancetype)initWithFrame:(CGRect)frame {
    self = [super initWithFrame:frame];
    if (self) {
        self.opaque = NO;
        self.backgroundColor = UIColor.clearColor;
        self.contentMode = UIViewContentModeRedraw;
        _pxOutline = kPxOutline;
    }
    return self;
}

-(void)setPxColorsFromBase:(UIColor *)base {
    CGFloat r = 0, g = 0, b = 0, a = 1;
    if ([base getRed:&r green:&g blue:&b alpha:&a]) {
        _pxFill = base;
        _pxHi = [UIColor colorWithRed:MIN(1, r + 0.18) green:MIN(1, g + 0.18) blue:MIN(1, b + 0.15) alpha:a];
        _pxLo = [UIColor colorWithRed:MAX(0, r - 0.18) green:MAX(0, g - 0.18) blue:MAX(0, b - 0.15) alpha:a];
    } else {
        _pxFill = base;
        _pxHi = base;
        _pxLo = base;
    }
    [self setNeedsDisplay];
}

-(void)setContentEdgeInsets:(UIEdgeInsets)contentEdgeInsets {
    if (!_hasBaseInsets) {
        _baseInsets = contentEdgeInsets;
        _hasBaseInsets = YES;
    }
    [super setContentEdgeInsets:contentEdgeInsets];
}

-(void)setHighlighted:(BOOL)highlighted {
    [super setHighlighted:highlighted];
    if (_hasBaseInsets) {
        UIEdgeInsets e = _baseInsets;
        if (highlighted) { e.top += 1.5; e.bottom = MAX(0, e.bottom - 1.5); }
        [super setContentEdgeInsets:e];
    }
    [self setNeedsDisplay];
}

-(void)drawRect:(CGRect)rect {
    if (!_pxFill || CGColorGetAlpha(_pxFill.CGColor) < 0.02) return;
    CGContextRef ctx = UIGraphicsGetCurrentContext();
    CGContextSetShouldAntialias(ctx, false);
    CGFloat p = 2.0;
    CGFloat w = self.bounds.size.width;
    CGFloat h = self.bounds.size.height;
    if (w < 8 || h < 8) return;

    BOOL down = self.isHighlighted;
    UIColor *topC = down ? _pxLo : _pxHi;
    UIColor *botC = down ? _pxHi : _pxLo;

    [_pxFill setFill];
    CGContextFillRect(ctx, CGRectMake(p, p, w - 2*p, h - 2*p));

    if (topC) {
        [topC setFill];
        CGContextFillRect(ctx, CGRectMake(2*p, p, w - 4*p, p));
        CGContextFillRect(ctx, CGRectMake(p, 2*p, p, h - 4*p));
    }
    if (botC) {
        [botC setFill];
        CGContextFillRect(ctx, CGRectMake(2*p, h - 2*p, w - 4*p, p));
        CGContextFillRect(ctx, CGRectMake(w - 2*p, 2*p, p, h - 4*p));
    }

    [_pxOutline setFill];
    CGContextFillRect(ctx, CGRectMake(p, 0, w - 2*p, p));
    CGContextFillRect(ctx, CGRectMake(p, h - p, w - 2*p, p));
    CGContextFillRect(ctx, CGRectMake(0, p, p, h - 2*p));
    CGContextFillRect(ctx, CGRectMake(w - p, p, p, h - 2*p));
    CGContextFillRect(ctx, CGRectMake(p, p, p, p));
    CGContextFillRect(ctx, CGRectMake(w - 2*p, p, p, p));
    CGContextFillRect(ctx, CGRectMake(p, h - 2*p, p, p));
    CGContextFillRect(ctx, CGRectMake(w - 2*p, h - 2*p, p, p));
}
@end

// ---- Pixel-Art Segmented Tab Bar (Drop-In Replacement for UISegmentedControl) ----
@interface SDVPixelSegmentControl : UIControl
@property(nonatomic, assign) NSInteger selectedSegmentIndex;
-(instancetype)initWithItems:(NSArray<NSString *> *)items;
@end

@implementation SDVPixelSegmentControl {
    SDVPixelBoxView *_frame;
    NSArray<SDVPixelButton *> *_btns;
}

-(instancetype)initWithItems:(NSArray<NSString *> *)items {
    self = [super initWithFrame:CGRectZero];
    if (self) {
        _frame = [SDVPixelBoxView new];
        _frame.fillColor = kPxWoodDark;
        _frame.hiColor = kPxWoodLo;
        _frame.loColor = kPxWoodHi;
        _frame.outlineColor = kPxOutline;
        _frame.pixelStep = 2.0;
        _frame.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_frame];
        [NSLayoutConstraint activateConstraints:@[
            [_frame.leadingAnchor constraintEqualToAnchor:self.leadingAnchor],
            [_frame.trailingAnchor constraintEqualToAnchor:self.trailingAnchor],
            [_frame.topAnchor constraintEqualToAnchor:self.topAnchor],
            [_frame.bottomAnchor constraintEqualToAnchor:self.bottomAnchor]
        ]];

        UIStackView *row = [UIStackView new];
        row.axis = UILayoutConstraintAxisHorizontal;
        row.spacing = 3;
        row.distribution = UIStackViewDistributionFillEqually;
        row.translatesAutoresizingMaskIntoConstraints = NO;
        [_frame addSubview:row];
        [NSLayoutConstraint activateConstraints:@[
            [row.leadingAnchor constraintEqualToAnchor:_frame.leadingAnchor constant:4],
            [row.trailingAnchor constraintEqualToAnchor:_frame.trailingAnchor constant:-4],
            [row.topAnchor constraintEqualToAnchor:_frame.topAnchor constant:4],
            [row.bottomAnchor constraintEqualToAnchor:_frame.bottomAnchor constant:-4]
        ]];

        NSMutableArray *bs = [NSMutableArray array];
        for (NSUInteger i = 0; i < items.count; i++) {
            SDVPixelButton *b = [SDVPixelButton buttonWithType:UIButtonTypeCustom];
            b.tag = (NSInteger)i;
            b.titleLabel.font = pxFont(11.5, UIFontWeightBold);
            b.titleLabel.adjustsFontSizeToFitWidth = YES;
            b.titleLabel.minimumScaleFactor = 0.72;
            b.titleLabel.lineBreakMode = NSLineBreakByTruncatingTail;
            [b setTitle:items[i] forState:UIControlStateNormal];
            b.contentEdgeInsets = UIEdgeInsetsMake(5, 5, 5, 5);
            [b addTarget:self action:@selector(segTap:) forControlEvents:UIControlEventTouchUpInside];
            [row addArrangedSubview:b];
            [bs addObject:b];
        }
        _btns = bs;
        self.selectedSegmentIndex = 0;
    }
    return self;
}

-(void)setSelectedSegmentIndex:(NSInteger)idx {
    _selectedSegmentIndex = idx;
    for (NSInteger i = 0; i < (NSInteger)_btns.count; i++) {
        SDVPixelButton *b = _btns[i];
        BOOL sel = (i == idx);
        [b setPxColorsFromBase:sel ? kAccentMint : kPxWoodMid];
        [b setTitleColor:sel ? UIColor.whiteColor : kInkOnWood forState:UIControlStateNormal];
    }
}

-(void)segTap:(UIButton *)b {
    if (_selectedSegmentIndex == b.tag) return;
    self.selectedSegmentIndex = b.tag;
    [self sendActionsForControlEvents:UIControlEventValueChanged];
}
@end

// ---- Pixel Slider Styling (Crisp Beveled Golden Knob + Square Wood Track) ----
static UIImage *sdvMakePixelThumb(UIColor *fillCol) {
    CGFloat w = 16, h = 20, p = 2;
    UIGraphicsBeginImageContextWithOptions(CGSizeMake(w, h), NO, UIScreen.mainScreen.scale);
    CGContextRef ctx = UIGraphicsGetCurrentContext();
    CGContextSetShouldAntialias(ctx, false);
    CGFloat r = 0, g = 0, b = 0, a = 1;
    [fillCol getRed:&r green:&g blue:&b alpha:&a];
    UIColor *hi = [UIColor colorWithRed:MIN(1, r + 0.22) green:MIN(1, g + 0.22) blue:MIN(1, b + 0.16) alpha:1];
    UIColor *lo = [UIColor colorWithRed:MAX(0, r - 0.22) green:MAX(0, g - 0.22) blue:MAX(0, b - 0.16) alpha:1];

    [fillCol setFill];
    CGContextFillRect(ctx, CGRectMake(p, p, w - 2*p, h - 2*p));
    [hi setFill];
    CGContextFillRect(ctx, CGRectMake(p, p, w - 2*p, p));
    CGContextFillRect(ctx, CGRectMake(p, p, p, h - 2*p));
    [lo setFill];
    CGContextFillRect(ctx, CGRectMake(p, h - 2*p, w - 2*p, p));
    CGContextFillRect(ctx, CGRectMake(w - 2*p, p, p, h - 2*p));
    [kPxOutline setFill];
    CGContextFillRect(ctx, CGRectMake(p, 0, w - 2*p, p));
    CGContextFillRect(ctx, CGRectMake(p, h - p, w - 2*p, p));
    CGContextFillRect(ctx, CGRectMake(0, p, p, h - 2*p));
    CGContextFillRect(ctx, CGRectMake(w - p, p, p, h - 2*p));
    UIImage *img = UIGraphicsGetImageFromCurrentImageContext();
    UIGraphicsEndImageContext();
    return img;
}

static UIImage *sdvMakePixelTrack(UIColor *fillCol) {
    CGFloat w = 12, h = 8, p = 2;
    UIGraphicsBeginImageContextWithOptions(CGSizeMake(w, h), NO, UIScreen.mainScreen.scale);
    CGContextRef ctx = UIGraphicsGetCurrentContext();
    CGContextSetShouldAntialias(ctx, false);
    [kPxOutline setFill];
    CGContextFillRect(ctx, CGRectMake(0, 0, w, h));
    [fillCol setFill];
    CGContextFillRect(ctx, CGRectMake(p, p, w - 2*p, h - 2*p));
    UIImage *img = UIGraphicsGetImageFromCurrentImageContext();
    UIGraphicsEndImageContext();
    return [img resizableImageWithCapInsets:UIEdgeInsetsMake(2, 4, 2, 4)];
}

static void sdvStylePixelSlider(UISlider *sl, UIColor *minTrack, UIColor *thumbCol) {
    [sl setThumbImage:sdvMakePixelThumb(thumbCol ?: kAccentGoldBtn) forState:UIControlStateNormal];
    [sl setThumbImage:sdvMakePixelThumb(rgb(0xFFE58A)) forState:UIControlStateHighlighted];
    [sl setMinimumTrackImage:sdvMakePixelTrack(minTrack ?: kAccentMint) forState:UIControlStateNormal];
    [sl setMaximumTrackImage:sdvMakePixelTrack(rgb(0x7A4824)) forState:UIControlStateNormal];
}

// ---- Universal Pixel UI Constructors ----
static UILabel *mkLabel(NSString *s, CGFloat size, UIFontWeight weight, UIColor *ink) {
    UILabel *v = [UILabel new];
    v.text = s;
    v.numberOfLines = 0;
    v.textColor = ink;
    v.font = pxFont(size, weight);
    return v;
}

static UIStackView *mkVStack(CGFloat gap) {
    UIStackView *s = [UIStackView new];
    s.axis = UILayoutConstraintAxisVertical;
    s.spacing = gap;
    return s;
}

static UIStackView *mkHStack(CGFloat gap) {
    UIStackView *s = [UIStackView new];
    s.axis = UILayoutConstraintAxisHorizontal;
    s.spacing = gap;
    s.alignment = UIStackViewAlignmentCenter;
    return s;
}

static void pinAll(UIView *child, UIView *parent, CGFloat inset) {
    child.translatesAutoresizingMaskIntoConstraints = NO;
    [NSLayoutConstraint activateConstraints:@[
        [child.leadingAnchor constraintEqualToAnchor:parent.leadingAnchor constant:inset],
        [child.trailingAnchor constraintEqualToAnchor:parent.trailingAnchor constant:-inset],
        [child.topAnchor constraintEqualToAnchor:parent.topAnchor constant:inset],
        [child.bottomAnchor constraintEqualToAnchor:parent.bottomAnchor constant:-inset]
    ]];
}

static void pinInsets(UIView *child, UIView *parent, UIEdgeInsets e) {
    child.translatesAutoresizingMaskIntoConstraints = NO;
    [NSLayoutConstraint activateConstraints:@[
        [child.leadingAnchor constraintEqualToAnchor:parent.leadingAnchor constant:e.left],
        [child.trailingAnchor constraintEqualToAnchor:parent.trailingAnchor constant:-e.right],
        [child.topAnchor constraintEqualToAnchor:parent.topAnchor constant:e.top],
        [child.bottomAnchor constraintEqualToAnchor:parent.bottomAnchor constant:-e.bottom]
    ]];
}

static UIView *mkCard(UIView *content, UIColor *bg, UIColor *border, CGFloat radius, UIEdgeInsets pad) {
    SDVPixelBoxView *v = [SDVPixelBoxView new];
    v.fillColor = bg ?: kBgCard;
    v.outlineColor = border ?: kPxOutline;
    CGFloat r = 0, g = 0, b = 0, a = 1;
    if ([v.fillColor getRed:&r green:&g blue:&b alpha:&a]) {
        v.hiColor = [UIColor colorWithRed:MIN(1, r + 0.14) green:MIN(1, g + 0.14) blue:MIN(1, b + 0.12) alpha:1];
        v.loColor = [UIColor colorWithRed:MAX(0, r - 0.16) green:MAX(0, g - 0.16) blue:MAX(0, b - 0.14) alpha:1];
    }
    v.showRivets = (radius >= 15.0);
    [v addSubview:content];
    UIEdgeInsets adj = UIEdgeInsetsMake(pad.top + 2, pad.left + 2, pad.bottom + 2, pad.right + 2);
    pinInsets(content, v, adj);
    return v;
}

static UILabel *mkPill(NSString *text, UIColor *bg, UIColor *fg, CGFloat fontSize) {
    UILabel *l = mkLabel([NSString stringWithFormat:@" %@ ", text], fontSize, UIFontWeightBold, fg);
    l.numberOfLines = 1;
    l.backgroundColor = bg;
    l.layer.cornerRadius = 0;
    l.layer.borderWidth = 2.0;
    l.layer.borderColor = kPxOutline.CGColor;
    l.layer.masksToBounds = YES;
    [l setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    [l setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    [l.heightAnchor constraintGreaterThanOrEqualToConstant:21].active = YES;
    return l;
}

static UIButton *mkPillButton(NSString *title, NSString *iconName, UIColor *bg, UIColor *fg, CGFloat fontSize, UIEdgeInsets pad) {
    SDVPixelButton *b = [SDVPixelButton buttonWithType:UIButtonTypeCustom];
    [b setPxColorsFromBase:bg];
    b.titleLabel.font = pxFont(fontSize, UIFontWeightBold);
    b.titleLabel.lineBreakMode = NSLineBreakByTruncatingTail;
    [b setTitleColor:fg forState:UIControlStateNormal];
    [b setTitleColor:[fg colorWithAlphaComponent:0.75] forState:UIControlStateHighlighted];
    if (title.length) [b setTitle:title forState:UIControlStateNormal];
    if (iconName.length) {
        UIImage *img = sdvPixelIcon(iconName, MAX(12.0, fontSize + 1.0), fg);
        [b setImage:img forState:UIControlStateNormal];
        b.imageView.layer.magnificationFilter = kCAFilterNearest;
    }
    CGFloat gap = (title.length && iconName.length) ? 5.0 : 0.0;
    b.contentEdgeInsets = UIEdgeInsetsMake(pad.top + 1, pad.left + 2 + (gap > 0 ? 2 : 0), pad.bottom + 1, pad.right + 2 + gap);
    if (gap > 0) {
        b.imageEdgeInsets = UIEdgeInsetsMake(0, -2, 0, 2);
        b.titleEdgeInsets = UIEdgeInsetsMake(0, gap, 0, -gap);
    }
    return b;
}
