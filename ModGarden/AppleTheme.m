#import <UIKit/UIKit.h>
#import <QuartzCore/QuartzCore.h>

// ============================================================================
// NATIVE APPLE iOS DESIGN SYSTEM (Settings / Files / Control Center Aesthetic)
// 100% UIKit Dynamic System Colors · SF Pro · SF Symbols · Continuous Squircles
// Compatible with iOS 14.0 through iOS 26+ (Light & Dark Mode adaptive)
// ============================================================================

static inline UIColor *rgb(uint32_t hex) {
    return [UIColor colorWithRed:((hex >> 16) & 0xFF) / 255.0
                           green:((hex >> 8) & 0xFF) / 255.0
                            blue:(hex & 0xFF) / 255.0
                           alpha:1.0];
}

static inline UIColor *rgba(uint32_t hex, CGFloat a) {
    return [UIColor colorWithRed:((hex >> 16) & 0xFF) / 255.0
                           green:((hex >> 8) & 0xFF) / 255.0
                            blue:(hex & 0xFF) / 255.0
                           alpha:a];
}

// Dynamic Apple System Color Tokens
#define kBgCanvas          UIColor.systemGroupedBackgroundColor
#define kBgSidebar         UIColor.secondarySystemGroupedBackgroundColor
#define kBgCard            UIColor.secondarySystemGroupedBackgroundColor
#define kBgCardSubtle      UIColor.tertiarySystemFillColor
#define kBgCardDim         UIColor.tertiarySystemGroupedBackgroundColor
#define kSepColor          UIColor.separatorColor
#define kInkPrimary        UIColor.labelColor
#define kInkSecondary      UIColor.secondaryLabelColor
#define kInkTertiary       UIColor.tertiaryLabelColor

#define kTintBlue          UIColor.systemBlueColor
#define kTintGreen         UIColor.systemGreenColor
#define kTintOrange        UIColor.systemOrangeColor
#define kTintRed           UIColor.systemRedColor
#define kTintIndigo        UIColor.systemIndigoColor
#define kTintPurple        UIColor.systemPurpleColor
#define kTintTeal          UIColor.systemTealColor
#define kTintPink          UIColor.systemPinkColor

static inline UIFont *iosFont(CGFloat pt, UIFontWeight w) {
    return [UIFont systemFontOfSize:pt weight:w];
}

static inline UIFont *iosMonoDigits(CGFloat pt, UIFontWeight w) {
    return [UIFont monospacedDigitSystemFontOfSize:pt weight:w];
}

static inline UIImage *sfIcon(NSString *name, CGFloat pt, UIImageSymbolWeight w) {
    UIImageSymbolConfiguration *cfg = [UIImageSymbolConfiguration configurationWithPointSize:pt weight:w];
    UIImage *img = [UIImage systemImageNamed:name withConfiguration:cfg];
    return img ? [img imageWithRenderingMode:UIImageRenderingModeAlwaysTemplate] : nil;
}

static UIStackView *mkVStack(CGFloat spacing) {
    UIStackView *s = [UIStackView new];
    s.axis = UILayoutConstraintAxisVertical;
    s.spacing = spacing;
    s.alignment = UIStackViewAlignmentFill;
    return s;
}

static UIStackView *mkHStack(CGFloat spacing) {
    UIStackView *s = [UIStackView new];
    s.axis = UILayoutConstraintAxisHorizontal;
    s.spacing = spacing;
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

static UILabel *mkLabel(NSString *text, CGFloat size, UIFontWeight weight, UIColor *color) {
    UILabel *l = [UILabel new];
    l.text = text;
    l.font = iosFont(size, weight);
    l.textColor = color;
    l.numberOfLines = 0;
    return l;
}

static UILabel *mkSectionHeader(NSString *title) {
    UILabel *l = [UILabel new];
    l.text = [title uppercaseString];
    l.font = iosFont(11.5, UIFontWeightSemibold);
    l.textColor = kInkSecondary;
    l.numberOfLines = 1;
    UIView *wrap = nil;
    (void)wrap;
    return l;
}

static UIView *mkSectionTitle(NSString *title) {
    UIView *box = [UIView new];
    UILabel *l = mkSectionHeader(title);
    [box addSubview:l];
    pinInsets(l, box, UIEdgeInsetsMake(6, 12, 0, 12));
    return box;
}

static UIView *mkSectionFooter(NSString *text) {
    UIView *box = [UIView new];
    UILabel *l = mkLabel(text, 12, UIFontWeightRegular, kInkSecondary);
    [box addSubview:l];
    pinInsets(l, box, UIEdgeInsetsMake(0, 12, 4, 12));
    return box;
}

static UIView *mkHairline(void) {
    UIView *v = [UIView new];
    v.backgroundColor = kSepColor;
    v.translatesAutoresizingMaskIntoConstraints = NO;
    [v.heightAnchor constraintEqualToConstant:0.5].active = YES;
    return v;
}

static UIView *mkInsetHairline(CGFloat leftInset) {
    UIView *wrap = [UIView new];
    UIView *line = [UIView new];
    line.backgroundColor = kSepColor;
    line.translatesAutoresizingMaskIntoConstraints = NO;
    [wrap addSubview:line];
    [NSLayoutConstraint activateConstraints:@[
        [line.leadingAnchor constraintEqualToAnchor:wrap.leadingAnchor constant:leftInset],
        [line.trailingAnchor constraintEqualToAnchor:wrap.trailingAnchor],
        [line.topAnchor constraintEqualToAnchor:wrap.topAnchor],
        [line.bottomAnchor constraintEqualToAnchor:wrap.bottomAnchor],
        [line.heightAnchor constraintEqualToConstant:0.5]
    ]];
    return wrap;
}

// Apple Settings 28x28 rounded-square colored SF Symbol badge
static UIView *mkSettingsIconBadge(NSString *symbolName, UIColor *bgColor, CGFloat boxSize) {
    UIView *box = [UIView new];
    box.backgroundColor = bgColor;
    box.layer.cornerRadius = round(boxSize * 0.24);
    box.layer.cornerCurve = kCACornerCurveContinuous;
    box.translatesAutoresizingMaskIntoConstraints = NO;
    [box.widthAnchor constraintEqualToConstant:boxSize].active = YES;
    [box.heightAnchor constraintEqualToConstant:boxSize].active = YES;
    [box setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    [box setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

    UIImageView *iv = [[UIImageView alloc] initWithImage:sfIcon(symbolName, boxSize * 0.54, UIImageSymbolWeightSemibold)];
    iv.tintColor = UIColor.whiteColor;
    iv.contentMode = UIViewContentModeScaleAspectFit;
    iv.translatesAutoresizingMaskIntoConstraints = NO;
    [box addSubview:iv];
    [NSLayoutConstraint activateConstraints:@[
        [iv.centerXAnchor constraintEqualToAnchor:box.centerXAnchor],
        [iv.centerYAnchor constraintEqualToAnchor:box.centerYAnchor]
    ]];
    return box;
}

// Native Apple Inset Grouped Card Container
static UIView *mkGroupedCard(UIView *inner, UIEdgeInsets pad) {
    UIView *card = [UIView new];
    card.backgroundColor = kBgCard;
    card.layer.cornerRadius = 13.0;
    card.layer.cornerCurve = kCACornerCurveContinuous;
    card.clipsToBounds = YES;
    [card addSubview:inner];
    pinInsets(inner, card, pad);
    return card;
}

// Native Apple Inset Grouped List (joins multiple row views with 0.5pt hairline separators!)
static UIView *mkGroupedRows(NSArray<UIView *> *rows, CGFloat sepLeftInset) {
    UIStackView *st = mkVStack(0);
    for (NSUInteger i = 0; i < rows.count; i++) {
        UIView *rWrap = [UIView new];
        [rWrap addSubview:rows[i]];
        pinInsets(rows[i], rWrap, UIEdgeInsetsMake(10, 14, 10, 14));
        [st addArrangedSubview:rWrap];
        if (i + 1 < rows.count) {
            [st addArrangedSubview:mkInsetHairline(sepLeftInset)];
        }
    }
    return mkGroupedCard(st, UIEdgeInsetsZero);
}

// Native Apple Tinted Pill Tag
static UILabel *mkBadgePill(NSString *text, UIColor *bg, UIColor *fg, CGFloat size) {
    UILabel *l = [UILabel new];
    l.text = [NSString stringWithFormat:@"  %@  ", text];
    l.font = iosFont(size, UIFontWeightSemibold);
    l.textColor = fg;
    l.backgroundColor = bg;
    l.layer.cornerRadius = 7.0;
    l.layer.cornerCurve = kCACornerCurveContinuous;
    l.clipsToBounds = YES;
    l.translatesAutoresizingMaskIntoConstraints = NO;
    [l setContentHuggingPriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    [l setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
    [l.heightAnchor constraintGreaterThanOrEqualToConstant:size + 9.0].active = YES;
    return l;
}

// Native Apple Interactive Button with smooth tactile spring-opacity press feedback (iOS 14–26+)
@interface SDVAppleButton : UIButton
@property(nonatomic, strong) UIColor *normalBg;
@property(nonatomic, assign) CGFloat cornerRad;
@end

@implementation SDVAppleButton
-(instancetype)initWithFrame:(CGRect)frame {
    self = [super initWithFrame:frame];
    if (self) {
        self.layer.cornerCurve = kCACornerCurveContinuous;
        self.clipsToBounds = YES;
    }
    return self;
}
-(void)setHighlighted:(BOOL)highlighted {
    [super setHighlighted:highlighted];
    [UIView animateWithDuration:0.12 delay:0 options:UIViewAnimationOptionAllowUserInteraction | UIViewAnimationOptionBeginFromCurrentState animations:^{
        self.alpha = highlighted ? 0.65 : 1.0;
        self.transform = highlighted ? CGAffineTransformMakeScale(0.985, 0.985) : CGAffineTransformIdentity;
    } completion:nil];
}
@end

static SDVAppleButton *mkIOSButton(NSString *title, NSString *sfSymbol, UIColor *bg, UIColor *fg, CGFloat fontSize, UIEdgeInsets pad) {
    SDVAppleButton *b = [SDVAppleButton buttonWithType:UIButtonTypeCustom];
    b.normalBg = bg;
    b.backgroundColor = bg;
    b.layer.cornerRadius = 11.0;
    b.layer.cornerCurve = kCACornerCurveContinuous;
    b.tintColor = fg;
    [b setTitleColor:fg forState:UIControlStateNormal];
    b.titleLabel.font = iosFont(fontSize, UIFontWeightSemibold);
    b.titleLabel.lineBreakMode = NSLineBreakByTruncatingTail;
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    b.contentEdgeInsets = pad;
    if (title.length) [b setTitle:title forState:UIControlStateNormal];
    if (sfSymbol.length) {
        UIImage *img = sfIcon(sfSymbol, fontSize, UIImageSymbolWeightSemibold);
        if (img) {
            [b setImage:img forState:UIControlStateNormal];
            if (title.length) {
                b.imageEdgeInsets = UIEdgeInsetsMake(0, -3, 0, 3);
                b.titleEdgeInsets = UIEdgeInsetsMake(0, 3, 0, -3);
            }
        }
    }
#pragma clang diagnostic pop
    return b;
}

// Native Apple Frosted Glass Container (adapts seamlessly to iOS 14–18 Blur & iOS 26 Liquid Glass!)
static UIVisualEffectView *mkGlassCard(UIBlurEffectStyle style, CGFloat radius) {
    UIVisualEffectView *blur = [[UIVisualEffectView alloc] initWithEffect:[UIBlurEffect effectWithStyle:style]];
    blur.layer.cornerRadius = radius;
    blur.layer.cornerCurve = kCACornerCurveContinuous;
    blur.clipsToBounds = YES;
    blur.layer.borderWidth = 0.5;
    blur.layer.borderColor = [UIColor colorWithWhite:1.0 alpha:0.20].CGColor;
    return blur;
}
