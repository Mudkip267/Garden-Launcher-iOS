// Native iOS 14–27+ External Peripheral Input Engine for Garden Launcher iOS
// Supports:
//   1. Physical Gamepads (Xbox, PS4/PS5 DualSense, Switch Pro, Backbone, MFi) via GCController (full sticks, triggers, bumpers, L3/R3, Options/Menu, D-Pad)
//   2. Physical Hardware Keyboards (Bluetooth / Smart Keyboard / USB-C) via GCKeyboard + typed-character ring buffer
//   3. Physical Mice & Trackpads (Bluetooth / USB-C / Magic Trackpad) via GCMouse + UIHoverGestureRecognizer + IndirectPointer touch filter
#import <GameController/GameController.h>
#import <CoreHaptics/CoreHaptics.h>
#import <os/lock.h>
#import <stdint.h>
#import <math.h>

typedef struct {
    int32_t structSize;
    int32_t suppressGameInput;

    // Gamepad
    int32_t gpConnected;
    uint32_t gpButtonsMask;
    float gpLeftX;
    float gpLeftY;
    float gpRightX;
    float gpRightY;
    float gpLeftTrigger;
    float gpRightTrigger;
    uint32_t gpActivitySeq;

    // Keyboard
    int32_t kbConnected;
    int32_t kbKeyCount;
    uint16_t kbKeys[32];
    int32_t textCharCount;
    uint16_t textChars[32];
    uint32_t kbActivitySeq;

    // Mouse / Trackpad
    int32_t mouseConnected;
    float mouseNormX;
    float mouseNormY;
    uint32_t mouseButtonsMask;
    int32_t mouseScrollDelta;
    uint32_t mouseActivitySeq;
} SDVExtInputSnapshot;

typedef struct {
    GCKeyCode gcCode;
    uint16_t xnaKey;
    uint16_t chNormal;
    uint16_t chShift;
} SDVKeyMapEntry;

static os_unfair_lock gExtLock = OS_UNFAIR_LOCK_INIT;
static BOOL gExtInitialized = NO;
static int32_t gNativeTextEditingCount = 0;

// Cached game window dimensions (in UIKit points) updated on main thread
static CGFloat gExtWinW = 812.0;
static CGFloat gExtWinH = 375.0;

// Mouse state
static float gMouseNormX = 0.5f;
static float gMouseNormY = 0.5f;
static uint32_t gMouseButtonsMask = 0;
static uint32_t gMouseButtonsLatchedMask = 0;
static float gMouseScrollAccum = 0.0f;
static int32_t gMouseScrollPending = 0;
static uint32_t gMouseActivitySeq = 0;
static CFTimeInterval gLastHoverTime = 0.0;
static BOOL gHasGCMouseConnected = NO;
static BOOL gHasHoverSeen = NO;

// Keyboard state
static uint32_t gKbActivitySeq = 0;
static uint16_t gKbLatchedKeys[32];
static int32_t gKbLatchedCount = 0;
static uint16_t gTextRing[64];
static int32_t gTextRingHead = 0;
static int32_t gTextRingTail = 0;

// Gamepad activity tracker & cached UIKit suppression flags (zero per-frame scene scans!)
static uint32_t gGpActivitySeq = 0;
static volatile int32_t gGardenOpenCached = 0;
static __weak UIWindow *gExtPrimaryGameWindow = nil;

// Key mapping table initialized once (GCKeyCode externs are runtime constants on iOS 14+)
static SDVKeyMapEntry gKeyMap[128];
static int32_t gKeyMapCount = 0;

static void sdvAddKeyMap(GCKeyCode gc, uint16_t xna, uint16_t chN, uint16_t chS) {
    if (gKeyMapCount >= 128) return;
    gKeyMap[gKeyMapCount].gcCode = gc;
    gKeyMap[gKeyMapCount].xnaKey = xna;
    gKeyMap[gKeyMapCount].chNormal = chN;
    gKeyMap[gKeyMapCount].chShift = chS;
    gKeyMapCount++;
}

static void sdvInitKeyMapOnce(void) {
    if (gKeyMapCount > 0) return;
    // Letters A-Z (XNA Keys 65..90)
    sdvAddKeyMap(GCKeyCodeKeyA, 65, 'a', 'A');
    sdvAddKeyMap(GCKeyCodeKeyB, 66, 'b', 'B');
    sdvAddKeyMap(GCKeyCodeKeyC, 67, 'c', 'C');
    sdvAddKeyMap(GCKeyCodeKeyD, 68, 'd', 'D');
    sdvAddKeyMap(GCKeyCodeKeyE, 69, 'e', 'E');
    sdvAddKeyMap(GCKeyCodeKeyF, 70, 'f', 'F');
    sdvAddKeyMap(GCKeyCodeKeyG, 71, 'g', 'G');
    sdvAddKeyMap(GCKeyCodeKeyH, 72, 'h', 'H');
    sdvAddKeyMap(GCKeyCodeKeyI, 73, 'i', 'I');
    sdvAddKeyMap(GCKeyCodeKeyJ, 74, 'j', 'J');
    sdvAddKeyMap(GCKeyCodeKeyK, 75, 'k', 'K');
    sdvAddKeyMap(GCKeyCodeKeyL, 76, 'l', 'L');
    sdvAddKeyMap(GCKeyCodeKeyM, 77, 'm', 'M');
    sdvAddKeyMap(GCKeyCodeKeyN, 78, 'n', 'N');
    sdvAddKeyMap(GCKeyCodeKeyO, 79, 'o', 'O');
    sdvAddKeyMap(GCKeyCodeKeyP, 80, 'p', 'P');
    sdvAddKeyMap(GCKeyCodeKeyQ, 81, 'q', 'Q');
    sdvAddKeyMap(GCKeyCodeKeyR, 82, 'r', 'R');
    sdvAddKeyMap(GCKeyCodeKeyS, 83, 's', 'S');
    sdvAddKeyMap(GCKeyCodeKeyT, 84, 't', 'T');
    sdvAddKeyMap(GCKeyCodeKeyU, 85, 'u', 'U');
    sdvAddKeyMap(GCKeyCodeKeyV, 86, 'v', 'V');
    sdvAddKeyMap(GCKeyCodeKeyW, 87, 'w', 'W');
    sdvAddKeyMap(GCKeyCodeKeyX, 88, 'x', 'X');
    sdvAddKeyMap(GCKeyCodeKeyY, 89, 'y', 'Y');
    sdvAddKeyMap(GCKeyCodeKeyZ, 90, 'z', 'Z');

    // Digits 0-9 (XNA Keys.D0=48..D9=57)
    sdvAddKeyMap(GCKeyCodeOne,   49, '1', '!');
    sdvAddKeyMap(GCKeyCodeTwo,   50, '2', '@');
    sdvAddKeyMap(GCKeyCodeThree, 51, '3', '#');
    sdvAddKeyMap(GCKeyCodeFour,  52, '4', '$');
    sdvAddKeyMap(GCKeyCodeFive,  53, '5', '%');
    sdvAddKeyMap(GCKeyCodeSix,   54, '6', '^');
    sdvAddKeyMap(GCKeyCodeSeven, 55, '7', '&');
    sdvAddKeyMap(GCKeyCodeEight, 56, '8', '*');
    sdvAddKeyMap(GCKeyCodeNine,  57, '9', '(');
    sdvAddKeyMap(GCKeyCodeZero,  48, '0', ')');

    // Core control & navigation keys
    sdvAddKeyMap(GCKeyCodeReturnOrEnter,     13,  '\r', '\r');
    sdvAddKeyMap(GCKeyCodeEscape,            27,  0,    0);
    sdvAddKeyMap(GCKeyCodeDeleteOrBackspace, 8,   '\b', '\b');
    sdvAddKeyMap(GCKeyCodeTab,               9,   '\t', '\t');
    sdvAddKeyMap(GCKeyCodeSpacebar,          32,  ' ',  ' ');
    sdvAddKeyMap(GCKeyCodeCapsLock,          20,  0,    0);

    // Punctuation / OEM
    sdvAddKeyMap(GCKeyCodeHyphen,              189, '-',  '_');
    sdvAddKeyMap(GCKeyCodeEqualSign,           187, '=',  '+');
    sdvAddKeyMap(GCKeyCodeOpenBracket,         219, '[',  '{');
    sdvAddKeyMap(GCKeyCodeCloseBracket,        221, ']',  '}');
    sdvAddKeyMap(GCKeyCodeBackslash,           220, '\\', '|');
    sdvAddKeyMap(GCKeyCodeSemicolon,           186, ';',  ':');
    sdvAddKeyMap(GCKeyCodeQuote,               222, '\'', '"');
    sdvAddKeyMap(GCKeyCodeGraveAccentAndTilde, 192, '`',  '~');
    sdvAddKeyMap(GCKeyCodeComma,               188, ',',  '<');
    sdvAddKeyMap(GCKeyCodePeriod,              190, '.',  '>');
    sdvAddKeyMap(GCKeyCodeSlash,               191, '/',  '?');

    // Function keys F1-F12 (XNA 112..123)
    sdvAddKeyMap(GCKeyCodeF1,  112, 0, 0);
    sdvAddKeyMap(GCKeyCodeF2,  113, 0, 0);
    sdvAddKeyMap(GCKeyCodeF3,  114, 0, 0);
    sdvAddKeyMap(GCKeyCodeF4,  115, 0, 0);
    sdvAddKeyMap(GCKeyCodeF5,  116, 0, 0);
    sdvAddKeyMap(GCKeyCodeF6,  117, 0, 0);
    sdvAddKeyMap(GCKeyCodeF7,  118, 0, 0);
    sdvAddKeyMap(GCKeyCodeF8,  119, 0, 0);
    sdvAddKeyMap(GCKeyCodeF9,  120, 0, 0);
    sdvAddKeyMap(GCKeyCodeF10, 121, 0, 0);
    sdvAddKeyMap(GCKeyCodeF11, 122, 0, 0);
    sdvAddKeyMap(GCKeyCodeF12, 123, 0, 0);

    // Navigation & editing
    sdvAddKeyMap(GCKeyCodePrintScreen,   44, 0, 0);
    sdvAddKeyMap(GCKeyCodeScrollLock,    145, 0, 0);
    sdvAddKeyMap(GCKeyCodePause,         19, 0, 0);
    sdvAddKeyMap(GCKeyCodeInsert,        45, 0, 0);
    sdvAddKeyMap(GCKeyCodeHome,          36, 0, 0);
    sdvAddKeyMap(GCKeyCodePageUp,        33, 0, 0);
    sdvAddKeyMap(GCKeyCodeDeleteForward, 46, 0, 0);
    sdvAddKeyMap(GCKeyCodeEnd,           35, 0, 0);
    sdvAddKeyMap(GCKeyCodePageDown,      34, 0, 0);

    // Arrows
    sdvAddKeyMap(GCKeyCodeRightArrow, 39, 0, 0);
    sdvAddKeyMap(GCKeyCodeLeftArrow,  37, 0, 0);
    sdvAddKeyMap(GCKeyCodeDownArrow,  40, 0, 0);
    sdvAddKeyMap(GCKeyCodeUpArrow,    38, 0, 0);

    // Numpad
    sdvAddKeyMap(GCKeyCodeKeypadNumLock,  144, 0, 0);
    sdvAddKeyMap(GCKeyCodeKeypadSlash,    111, '/', '/');
    sdvAddKeyMap(GCKeyCodeKeypadAsterisk, 106, '*', '*');
    sdvAddKeyMap(GCKeyCodeKeypadHyphen,   109, '-', '-');
    sdvAddKeyMap(GCKeyCodeKeypadPlus,     107, '+', '+');
    sdvAddKeyMap(GCKeyCodeKeypadEnter,    13,  '\r', '\r');
    sdvAddKeyMap(GCKeyCodeKeypad1,        97,  '1', '1');
    sdvAddKeyMap(GCKeyCodeKeypad2,        98,  '2', '2');
    sdvAddKeyMap(GCKeyCodeKeypad3,        99,  '3', '3');
    sdvAddKeyMap(GCKeyCodeKeypad4,        100, '4', '4');
    sdvAddKeyMap(GCKeyCodeKeypad5,        101, '5', '5');
    sdvAddKeyMap(GCKeyCodeKeypad6,        102, '6', '6');
    sdvAddKeyMap(GCKeyCodeKeypad7,        103, '7', '7');
    sdvAddKeyMap(GCKeyCodeKeypad8,        104, '8', '8');
    sdvAddKeyMap(GCKeyCodeKeypad9,        105, '9', '9');
    sdvAddKeyMap(GCKeyCodeKeypad0,        96,  '0', '0');
    sdvAddKeyMap(GCKeyCodeKeypadPeriod,   110, '.', '.');

    // Modifiers
    sdvAddKeyMap(GCKeyCodeLeftControl,  162, 0, 0);
    sdvAddKeyMap(GCKeyCodeLeftShift,    160, 0, 0);
    sdvAddKeyMap(GCKeyCodeLeftAlt,      164, 0, 0);
    sdvAddKeyMap(GCKeyCodeLeftGUI,      91,  0, 0);
    sdvAddKeyMap(GCKeyCodeRightControl, 163, 0, 0);
    sdvAddKeyMap(GCKeyCodeRightShift,   161, 0, 0);
    sdvAddKeyMap(GCKeyCodeRightAlt,     165, 0, 0);
    sdvAddKeyMap(GCKeyCodeRightGUI,     92,  0, 0);
    sdvAddKeyMap(GCKeyCodeApplication,  93,  0, 0);
}

static void sdvPushTextCharLocked(uint16_t ch) {
    if (!ch) return;
    int32_t next = (gTextRingTail + 1) & 63;
    if (next == gTextRingHead) {
        gTextRingHead = (gTextRingHead + 1) & 63;
    }
    gTextRing[gTextRingTail] = ch;
    gTextRingTail = next;
}

static void sdvAttachKeyboardHandlers(GCKeyboard *kb) {
    if (!kb || !kb.keyboardInput) return;
    sdvInitKeyMapOnce();
    kb.keyboardInput.keyChangedHandler = ^(GCKeyboardInput *keyboard, GCControllerButtonInput *key, GCKeyCode keyCode, BOOL pressed) {
        (void)key;
        if (!pressed) return;
        BOOL shift = ([keyboard buttonForKeyCode:GCKeyCodeLeftShift].isPressed ||
                      [keyboard buttonForKeyCode:GCKeyCodeRightShift].isPressed);
        BOOL ctrlOrCmd = ([keyboard buttonForKeyCode:GCKeyCodeLeftControl].isPressed ||
                          [keyboard buttonForKeyCode:GCKeyCodeRightControl].isPressed ||
                          [keyboard buttonForKeyCode:GCKeyCodeLeftGUI].isPressed ||
                          [keyboard buttonForKeyCode:GCKeyCodeRightGUI].isPressed);
        uint16_t outChar = 0;
        uint16_t outXna = 0;
        for (int32_t i = 0; i < gKeyMapCount; i++) {
            if (gKeyMap[i].gcCode == keyCode) {
                outXna = gKeyMap[i].xnaKey;
                if (!ctrlOrCmd) {
                    outChar = shift ? gKeyMap[i].chShift : gKeyMap[i].chNormal;
                }
                break;
            }
        }
        os_unfair_lock_lock(&gExtLock);
        gKbActivitySeq++;
        if (outXna != 0 && gNativeTextEditingCount == 0 && !gGardenOpenCached) {
            // Latch key rising edge for 1 poll frame so ultra-fast taps (<16ms) are never dropped!
            BOOL dup = NO;
            for (int32_t k = 0; k < gKbLatchedCount; k++) {
                if (gKbLatchedKeys[k] == outXna) { dup = YES; break; }
            }
            if (!dup && gKbLatchedCount < 32) {
                gKbLatchedKeys[gKbLatchedCount++] = outXna;
            }
        }
        if (outChar != 0 && gNativeTextEditingCount == 0 && !gGardenOpenCached) {
            sdvPushTextCharLocked(outChar);
        }
        os_unfair_lock_unlock(&gExtLock);
    };
}

static void sdvUpdateMouseButtonsLocked(GCMouseInput *mi) {
    uint32_t mask = 0;
    if (mi.leftButton.isPressed)   mask |= (1u << 0);
    if (mi.rightButton.isPressed)  mask |= (1u << 1);
    if (mi.middleButton.isPressed) mask |= (1u << 2);
    NSArray<GCControllerButtonInput *> *aux = mi.auxiliaryButtons;
    if (aux.count > 0 && aux[0].isPressed) mask |= (1u << 3);
    if (aux.count > 1 && aux[1].isPressed) mask |= (1u << 4);
    gMouseButtonsLatchedMask |= mask;
    gMouseButtonsMask = mask;
    gMouseActivitySeq++;
}

static void sdvAttachMouseHandlers(GCMouse *mouse) {
    if (!mouse || !mouse.mouseInput) return;
    GCMouseInput *mi = mouse.mouseInput;
    os_unfair_lock_lock(&gExtLock);
    gHasGCMouseConnected = YES;
    os_unfair_lock_unlock(&gExtLock);

    mi.mouseMovedHandler = ^(GCMouseInput *m, float deltaX, float deltaY) {
        (void)m;
        if (fabsf(deltaX) < 0.001f && fabsf(deltaY) < 0.001f) return;
        CFTimeInterval now = CACurrentMediaTime();
        os_unfair_lock_lock(&gExtLock);
        // Only apply raw delta if UIHoverGestureRecognizer isn't actively providing absolute OS pointer position
        if (now - gLastHoverTime > 0.10) {
            float w = (float)(gExtWinW > 100.0 ? gExtWinW : 812.0);
            float h = (float)(gExtWinH > 100.0 ? gExtWinH : 375.0);
            // GCMouseInput deltaY is positive UP; screen normY is positive DOWN
            float sens = 1.05f;
            float nx = gMouseNormX + (deltaX * sens) / w;
            float ny = gMouseNormY - (deltaY * sens) / h;
            if (nx < 0.0f) nx = 0.0f; else if (nx > 1.0f) nx = 1.0f;
            if (ny < 0.0f) ny = 0.0f; else if (ny > 1.0f) ny = 1.0f;
            gMouseNormX = nx;
            gMouseNormY = ny;
        }
        gMouseActivitySeq++;
        os_unfair_lock_unlock(&gExtLock);
    };

    GCControllerButtonValueChangedHandler btnHandler = ^(GCControllerButtonInput *button, float value, BOOL pressed) {
        (void)button; (void)value; (void)pressed;
        GCMouse *cur = GCMouse.current ?: GCMouse.mice.firstObject;
        if (!cur.mouseInput) return;
        os_unfair_lock_lock(&gExtLock);
        sdvUpdateMouseButtonsLocked(cur.mouseInput);
        os_unfair_lock_unlock(&gExtLock);
    };
    mi.leftButton.pressedChangedHandler = btnHandler;
    if (mi.rightButton) mi.rightButton.pressedChangedHandler = btnHandler;
    if (mi.middleButton) mi.middleButton.pressedChangedHandler = btnHandler;
    for (GCControllerButtonInput *ab in mi.auxiliaryButtons) {
        ab.pressedChangedHandler = btnHandler;
    }

    mi.scroll.valueChangedHandler = ^(GCControllerDirectionPad *dpad, float xValue, float yValue) {
        (void)dpad; (void)xValue;
        if (fabsf(yValue) < 0.01f) return;
        os_unfair_lock_lock(&gExtLock);
        // Normalize mouse wheel & trackpad scroll into crisp Windows/XNA 120-unit detents
        gMouseScrollAccum += yValue * 120.0f;
        while (gMouseScrollAccum >= 60.0f) {
            gMouseScrollPending += 120;
            gMouseScrollAccum -= 120.0f;
            gMouseActivitySeq++;
        }
        while (gMouseScrollAccum <= -60.0f) {
            gMouseScrollPending -= 120;
            gMouseScrollAccum += 120.0f;
            gMouseActivitySeq++;
        }
        os_unfair_lock_unlock(&gExtLock);
    };
}

// Swizzle iOSGameView touch callbacks to ignore UITouchTypeIndirectPointer when GCMouse is actively connected,
// preventing duplicate click + touch-HUD wakeup on iPadOS when UIApplicationSupportsIndirectInputEvents=YES!
static BOOL gIndirectSwizzled = NO;
static int32_t gActiveDirectTouchCount = 0;
static void (*gOrigTouchesBegan)(id, SEL, NSSet<UITouch *> *, UIEvent *) = NULL;
static void (*gOrigTouchesMoved)(id, SEL, NSSet<UITouch *> *, UIEvent *) = NULL;
static void (*gOrigTouchesEnded)(id, SEL, NSSet<UITouch *> *, UIEvent *) = NULL;
static void (*gOrigTouchesCancelled)(id, SEL, NSSet<UITouch *> *, UIEvent *) = NULL;

static NSSet<UITouch *> *sdvFilterIndirectTouches(NSSet<UITouch *> *touches) {
    if (!gHasGCMouseConnected || touches.count == 0) return touches;
    BOOL hasIndirect = NO;
    for (UITouch *t in touches) {
        if (t.type == UITouchTypeIndirectPointer) {
            hasIndirect = YES;
            break;
        }
    }
    if (!hasIndirect) return touches;
    NSMutableSet<UITouch *> *direct = [NSMutableSet setWithCapacity:touches.count];
    for (UITouch *t in touches) {
        if (t.type != UITouchTypeIndirectPointer) {
            [direct addObject:t];
        }
    }
    return direct;
}

static void sdvUpdateDirectTouchCount(UIEvent *event) {
    if (!event) return;
    int32_t cnt = 0;
    for (UITouch *t in event.allTouches) {
        if (t.type == UITouchTypeIndirectPointer && gHasGCMouseConnected) continue;
        if (t.phase == UITouchPhaseBegan || t.phase == UITouchPhaseMoved || t.phase == UITouchPhaseStationary) {
            cnt++;
        }
    }
    os_unfair_lock_lock(&gExtLock);
    gActiveDirectTouchCount = cnt;
    os_unfair_lock_unlock(&gExtLock);
}

static void sdvSwizTouchesBegan(id self, SEL _cmd, NSSet<UITouch *> *touches, UIEvent *event) {
    sdvUpdateDirectTouchCount(event);
    NSSet<UITouch *> *f = sdvFilterIndirectTouches(touches);
    if (f.count > 0 && gOrigTouchesBegan) gOrigTouchesBegan(self, _cmd, f, event);
}
static void sdvSwizTouchesMoved(id self, SEL _cmd, NSSet<UITouch *> *touches, UIEvent *event) {
    sdvUpdateDirectTouchCount(event);
    NSSet<UITouch *> *f = sdvFilterIndirectTouches(touches);
    if (f.count > 0 && gOrigTouchesMoved) gOrigTouchesMoved(self, _cmd, f, event);
}
static void sdvSwizTouchesEnded(id self, SEL _cmd, NSSet<UITouch *> *touches, UIEvent *event) {
    sdvUpdateDirectTouchCount(event);
    NSSet<UITouch *> *f = sdvFilterIndirectTouches(touches);
    if (f.count > 0 && gOrigTouchesEnded) gOrigTouchesEnded(self, _cmd, f, event);
}
static void sdvSwizTouchesCancelled(id self, SEL _cmd, NSSet<UITouch *> *touches, UIEvent *event) {
    sdvUpdateDirectTouchCount(event);
    NSSet<UITouch *> *f = sdvFilterIndirectTouches(touches);
    if (f.count > 0 && gOrigTouchesCancelled) gOrigTouchesCancelled(self, _cmd, f, event);
}

static void sdvInstallIndirectTouchFilterOnView(UIView *v) {
    if (gIndirectSwizzled || !v) return;
    // Walk subviews to find MonoGame's iOSGameView
    NSString *clsName = NSStringFromClass(v.class);
    if ([clsName containsString:@"iOSGameView"]) {
        Class cls = v.class;
        Method mB = class_getInstanceMethod(cls, @selector(touchesBegan:withEvent:));
        Method mM = class_getInstanceMethod(cls, @selector(touchesMoved:withEvent:));
        Method mE = class_getInstanceMethod(cls, @selector(touchesEnded:withEvent:));
        Method mC = class_getInstanceMethod(cls, @selector(touchesCancelled:withEvent:));
        if (mB && mM && mE && mC) {
            gOrigTouchesBegan     = (void (*)(id, SEL, NSSet<UITouch *> *, UIEvent *))method_setImplementation(mB, (IMP)sdvSwizTouchesBegan);
            gOrigTouchesMoved     = (void (*)(id, SEL, NSSet<UITouch *> *, UIEvent *))method_setImplementation(mM, (IMP)sdvSwizTouchesMoved);
            gOrigTouchesEnded     = (void (*)(id, SEL, NSSet<UITouch *> *, UIEvent *))method_setImplementation(mE, (IMP)sdvSwizTouchesEnded);
            gOrigTouchesCancelled = (void (*)(id, SEL, NSSet<UITouch *> *, UIEvent *))method_setImplementation(mC, (IMP)sdvSwizTouchesCancelled);
            gIndirectSwizzled = YES;
            sdvBootLog("[EXT-INPUT] Installed IndirectPointer filter on iOSGameView");
        }
        return;
    }
    for (UIView *sub in v.subviews) {
        sdvInstallIndirectTouchFilterOnView(sub);
        if (gIndirectSwizzled) return;
    }
}

// ---- Native iOS 14–27+ CoreHaptics Controller Rumble Engine (Xbox / DualSense / Switch / MFi) ----
static dispatch_queue_t gRumbleQueue = nil;
static os_unfair_lock gRumbleLock = OS_UNFAIR_LOCK_INIT;
static float gRumbleTargetL = 0.0f;
static float gRumbleTargetR = 0.0f;
static BOOL gRumbleDirty = NO;

static __weak GCController *gRumbleController = nil;
static CHHapticEngine *gRumbleEngineL = nil;
static CHHapticEngine *gRumbleEngineR = nil;
static id<CHHapticPatternPlayer> gRumblePlayerL = nil;
static id<CHHapticPatternPlayer> gRumblePlayerR = nil;
static BOOL gRumbleDualMotor = NO;
static BOOL gRumbleRunningL = NO;
static BOOL gRumbleRunningR = NO;
static BOOL gRumbleNeedsRestartL = NO;
static BOOL gRumbleNeedsRestartR = NO;
static float gRumbleAppliedL = 0.0f;
static float gRumbleAppliedR = 0.0f;
static BOOL gRumbleErrLogged = NO;

static void sdvRumbleEnsureQueue(void) {
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        gRumbleQueue = dispatch_queue_create("dev.stardewmobile.garden.rumble", DISPATCH_QUEUE_SERIAL);
    });
}

static void sdvRumbleTeardownOnQueue(void) {
    if (@available(iOS 14.0, *)) {
        @try {
            if (gRumblePlayerL) { [gRumblePlayerL stopAtTime:CHHapticTimeImmediate error:nil]; }
            if (gRumblePlayerR) { [gRumblePlayerR stopAtTime:CHHapticTimeImmediate error:nil]; }
            if (gRumbleEngineL) { [gRumbleEngineL stopWithCompletionHandler:nil]; }
            if (gRumbleEngineR) { [gRumbleEngineR stopWithCompletionHandler:nil]; }
        } @catch (NSException *ignored) { }
    }
    gRumblePlayerL = nil;
    gRumblePlayerR = nil;
    gRumbleEngineL = nil;
    gRumbleEngineR = nil;
    gRumbleController = nil;
    gRumbleDualMotor = NO;
    gRumbleRunningL = NO;
    gRumbleRunningR = NO;
    gRumbleNeedsRestartL = NO;
    gRumbleNeedsRestartR = NO;
    gRumbleAppliedL = 0.0f;
    gRumbleAppliedR = 0.0f;
    gRumbleErrLogged = NO;
}

static BOOL sdvRumbleCreateEngineOnQueue(GCController *ctrl, GCHapticsLocality locality,
                                         CHHapticEngine * __strong *outEngine,
                                         BOOL isLeft, NSError **outErr) API_AVAILABLE(ios(14.0)) {
    if (!ctrl || !ctrl.haptics || !locality) return NO;
    @try {
        CHHapticEngine *eng = [ctrl.haptics createEngineWithLocality:locality];
        if (!eng) return NO;
        eng.playsHapticsOnly = YES;
        eng.resetHandler = ^{
            if (gRumbleQueue) {
                dispatch_async(gRumbleQueue, ^{
                    if (isLeft) { gRumbleNeedsRestartL = YES; gRumbleRunningL = NO; gRumblePlayerL = nil; gRumbleAppliedL = 0.0f; }
                    else        { gRumbleNeedsRestartR = YES; gRumbleRunningR = NO; gRumblePlayerR = nil; gRumbleAppliedR = 0.0f; }
                });
            }
        };
        eng.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
            (void)reason;
            if (gRumbleQueue) {
                dispatch_async(gRumbleQueue, ^{
                    if (isLeft) { gRumbleNeedsRestartL = YES; gRumbleRunningL = NO; gRumblePlayerL = nil; gRumbleAppliedL = 0.0f; }
                    else        { gRumbleNeedsRestartR = YES; gRumbleRunningR = NO; gRumblePlayerR = nil; gRumbleAppliedR = 0.0f; }
                });
            }
        };
        NSError *err = nil;
        if (![eng startAndReturnError:&err]) {
            if (outErr) *outErr = err;
            return NO;
        }
        *outEngine = eng;
        return YES;
    } @catch (NSException *ignored) {
        return NO;
    }
}

static void sdvRumbleEnsureControllerOnQueue(GCController *ctrl) API_AVAILABLE(ios(14.0)) {
    if (!ctrl || !ctrl.haptics) {
        if (gRumbleEngineL || gRumbleEngineR) sdvRumbleTeardownOnQueue();
        return;
    }
    if (gRumbleController == ctrl && (gRumbleEngineL != nil || gRumbleEngineR != nil)) {
        return;
    }
    sdvRumbleTeardownOnQueue();

    NSSet<GCHapticsLocality> *locs = ctrl.haptics.supportedLocalities;
    NSError *errL = nil, *errR = nil;
    BOOL hasBoth = [locs containsObject:GCHapticsLocalityLeftHandle] && [locs containsObject:GCHapticsLocalityRightHandle];
    if (hasBoth) {
        BOOL okL = sdvRumbleCreateEngineOnQueue(ctrl, GCHapticsLocalityLeftHandle, &gRumbleEngineL, YES, &errL);
        BOOL okR = sdvRumbleCreateEngineOnQueue(ctrl, GCHapticsLocalityRightHandle, &gRumbleEngineR, NO, &errR);
        if (okL || okR) {
            gRumbleController = ctrl;
            gRumbleDualMotor = (okL && okR);
            sdvBootLogFmt([NSString stringWithFormat:@"[EXT-RUMBLE] Bound '%@' dual=%d (okL=%d okR=%d, locs=%lu)",
                ctrl.vendorName ?: @"Controller", gRumbleDualMotor, okL, okR, (unsigned long)locs.count]);
            return;
        }
    }
    GCHapticsLocality fallbackLoc = nil;
    if ([locs containsObject:GCHapticsLocalityHandles]) fallbackLoc = GCHapticsLocalityHandles;
    else if ([locs containsObject:GCHapticsLocalityDefault]) fallbackLoc = GCHapticsLocalityDefault;
    else fallbackLoc = locs.anyObject;

    NSError *errF = nil;
    if (fallbackLoc && sdvRumbleCreateEngineOnQueue(ctrl, fallbackLoc, &gRumbleEngineL, YES, &errF)) {
        gRumbleController = ctrl;
        gRumbleDualMotor = NO;
        sdvBootLogFmt([NSString stringWithFormat:@"[EXT-RUMBLE] Bound '%@' fallback locality='%@'",
            ctrl.vendorName ?: @"Controller", fallbackLoc]);
    } else {
        sdvBootLogFmt([NSString stringWithFormat:@"[EXT-RUMBLE] Failed to bind '%@' (locs=%lu, errL=%@, errR=%@, errF=%@)",
            ctrl.vendorName ?: @"Controller", (unsigned long)locs.count,
            errL.localizedDescription ?: @"none", errR.localizedDescription ?: @"none", errF.localizedDescription ?: @"none"]);
    }
}

// SDL2-proven iOS Xbox/PS/MFi rumble driver:
// Uses standard CHHapticPatternPlayer + GCHapticDurationInfinite + direct CHHapticEventParameterIDHapticIntensity
// (Xbox Bluetooth controllers ignore CHHapticDynamicParameter & CHHapticAdvancedPatternPlayer loopEnabled on iOS!).
static void sdvRumbleApplyMotorOnQueue(CHHapticEngine *eng, id<CHHapticPatternPlayer> __strong *playerPtr,
                                       float intensity, float sharpness,
                                       BOOL *runningFlag, BOOL *needsRestartFlag, float *appliedPtr) API_AVAILABLE(ios(14.0)) {
    if (!eng || !playerPtr) return;
    @try {
        if (intensity <= 0.005f) {
            if (*playerPtr) {
                [*playerPtr stopAtTime:CHHapticTimeImmediate error:nil];
                *playerPtr = nil;
            }
            *runningFlag = NO;
            *appliedPtr = 0.0f;
            return;
        }
        // Avoid spamming BLE haptic restarts when intensity barely changed
        if (*runningFlag && *playerPtr != nil && fabsf(intensity - *appliedPtr) < 0.055f) {
            return;
        }
        if (*needsRestartFlag) {
            NSError *reErr = nil;
            if (![eng startAndReturnError:&reErr]) return;
            *needsRestartFlag = NO;
            *runningFlag = NO;
            *playerPtr = nil;
        }

        NSError *err = nil;
        CHHapticEventParameter *pInt = [[CHHapticEventParameter alloc]
            initWithParameterID:CHHapticEventParameterIDHapticIntensity value:intensity];
        CHHapticEventParameter *pShp = [[CHHapticEventParameter alloc]
            initWithParameterID:CHHapticEventParameterIDHapticSharpness value:sharpness];
        CHHapticEvent *ev = [[CHHapticEvent alloc]
            initWithEventType:CHHapticEventTypeHapticContinuous
                   parameters:@[pInt, pShp]
                 relativeTime:0.0
                     duration:(NSTimeInterval)GCHapticDurationInfinite];
        CHHapticPattern *pat = [[CHHapticPattern alloc] initWithEvents:@[ev] parameters:@[] error:&err];
        if (!pat) {
            if (!gRumbleErrLogged) {
                gRumbleErrLogged = YES;
                sdvBootLogFmt([NSString stringWithFormat:@"[EXT-RUMBLE] Pattern create error: %@", err.localizedDescription]);
            }
            return;
        }

        id<CHHapticPatternPlayer> newPlayer = [eng createPlayerWithPattern:pat error:&err];
        if (!newPlayer) {
            if ([eng startAndReturnError:&err]) {
                newPlayer = [eng createPlayerWithPattern:pat error:&err];
            }
            if (!newPlayer) return;
        }

        if (*playerPtr) {
            [*playerPtr stopAtTime:CHHapticTimeImmediate error:nil];
        }
        *playerPtr = newPlayer;

        if (![newPlayer startAtTime:CHHapticTimeImmediate error:&err]) {
            if ([eng startAndReturnError:&err] && [newPlayer startAtTime:CHHapticTimeImmediate error:&err]) {
                *runningFlag = YES;
                *appliedPtr = intensity;
            } else {
                *runningFlag = NO;
                *appliedPtr = 0.0f;
                if (!gRumbleErrLogged) {
                    gRumbleErrLogged = YES;
                    sdvBootLogFmt([NSString stringWithFormat:@"[EXT-RUMBLE] Player start error: %@", err.localizedDescription]);
                }
            }
        } else {
            *runningFlag = YES;
            *appliedPtr = intensity;
        }
    } @catch (NSException *ignored) {
        *needsRestartFlag = YES;
        *runningFlag = NO;
        *playerPtr = nil;
        *appliedPtr = 0.0f;
    }
}

static void sdvRumbleStopImmediate(BOOL teardown) {
    os_unfair_lock_lock(&gRumbleLock);
    gRumbleTargetL = 0.0f;
    gRumbleTargetR = 0.0f;
    gRumbleDirty = NO;
    os_unfair_lock_unlock(&gRumbleLock);

    if (!gRumbleQueue) return;
    dispatch_async(gRumbleQueue, ^{
        @autoreleasepool {
            if (teardown) {
                sdvRumbleTeardownOnQueue();
            } else if (@available(iOS 14.0, *)) {
                sdvRumbleApplyMotorOnQueue(gRumbleEngineL, &gRumblePlayerL, 0.0f, 0.15f, &gRumbleRunningL, &gRumbleNeedsRestartL, &gRumbleAppliedL);
                sdvRumbleApplyMotorOnQueue(gRumbleEngineR, &gRumblePlayerR, 0.0f, 0.65f, &gRumbleRunningR, &gRumbleNeedsRestartR, &gRumbleAppliedR);
            }
        }
    });
}

__attribute__((visibility("default")))
void sdv_ext_set_rumble(float leftMotor, float rightMotor) {
    if (isnan(leftMotor) || leftMotor < 0.0f) leftMotor = 0.0f;
    else if (leftMotor > 1.0f) leftMotor = 1.0f;
    if (isnan(rightMotor) || rightMotor < 0.0f) rightMotor = 0.0f;
    else if (rightMotor > 1.0f) rightMotor = 1.0f;

    if (gGardenOpenCached || gNativeTextEditingCount > 0) {
        leftMotor = 0.0f;
        rightMotor = 0.0f;
    }

    os_unfair_lock_lock(&gRumbleLock);
    BOOL activeNowL = (leftMotor > 0.005f);
    BOOL activeOldL = (gRumbleTargetL > 0.005f);
    BOOL activeNowR = (rightMotor > 0.005f);
    BOOL activeOldR = (gRumbleTargetR > 0.005f);
    if (activeNowL == activeOldL && activeNowR == activeOldR &&
        fabsf(leftMotor - gRumbleTargetL) < 0.015f &&
        fabsf(rightMotor - gRumbleTargetR) < 0.015f) {
        os_unfair_lock_unlock(&gRumbleLock);
        return;
    }
    gRumbleTargetL = leftMotor;
    gRumbleTargetR = rightMotor;
    BOOL shouldSchedule = !gRumbleDirty;
    gRumbleDirty = YES;
    os_unfair_lock_unlock(&gRumbleLock);

    if (!shouldSchedule) return;

    sdvRumbleEnsureQueue();
    dispatch_async(gRumbleQueue, ^{
        @autoreleasepool {
            if (@available(iOS 14.0, *)) {
                if (NSClassFromString(@"CHHapticEngine") == nil) return;
                os_unfair_lock_lock(&gRumbleLock);
                float l = gRumbleTargetL;
                float r = gRumbleTargetR;
                gRumbleDirty = NO;
                os_unfair_lock_unlock(&gRumbleLock);

                GCController *ctrl = GCController.current ?: GCController.controllers.firstObject;
                if (!ctrl || !ctrl.haptics) {
                    if (gRumbleEngineL || gRumbleEngineR) sdvRumbleTeardownOnQueue();
                    return;
                }
                if (l <= 0.005f && r <= 0.005f && !gRumbleRunningL && !gRumbleRunningR) {
                    return;
                }
                sdvRumbleEnsureControllerOnQueue(ctrl);
                if (gRumbleDualMotor) {
                    sdvRumbleApplyMotorOnQueue(gRumbleEngineL, &gRumblePlayerL, l, 0.15f, &gRumbleRunningL, &gRumbleNeedsRestartL, &gRumbleAppliedL);
                    sdvRumbleApplyMotorOnQueue(gRumbleEngineR, &gRumblePlayerR, r, 0.65f, &gRumbleRunningR, &gRumbleNeedsRestartR, &gRumbleAppliedR);
                } else {
                    float combined = fmaxf(l, r);
                    sdvRumbleApplyMotorOnQueue(gRumbleEngineL, &gRumblePlayerL, combined, 0.35f, &gRumbleRunningL, &gRumbleNeedsRestartL, &gRumbleAppliedL);
                }
            }
        }
    });
}

@interface SDVExternalInputHost : NSObject
+(instancetype)shared;
-(void)start;
-(void)attachToWindow:(UIWindow *)win;
@end

@implementation SDVExternalInputHost {
    NSHashTable<UIWindow *> *_hoverWindows;
}

+(instancetype)shared {
    static SDVExternalInputHost *inst = nil;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ inst = [SDVExternalInputHost new]; });
    return inst;
}

-(instancetype)init {
    self = [super init];
    if (self) {
        _hoverWindows = [NSHashTable weakObjectsHashTable];
    }
    return self;
}

-(void)start {
    if (gExtInitialized) return;
    gExtInitialized = YES;
    sdvInitKeyMapOnce();

    NSNotificationCenter *nc = NSNotificationCenter.defaultCenter;
    [nc addObserver:self selector:@selector(onControllerDisconnected:) name:GCControllerDidDisconnectNotification object:nil];
    [nc addObserver:self selector:@selector(onKeyboardConnected:) name:GCKeyboardDidConnectNotification object:nil];
    [nc addObserver:self selector:@selector(onKeyboardDisconnected:) name:GCKeyboardDidDisconnectNotification object:nil];
    [nc addObserver:self selector:@selector(onMouseConnected:) name:GCMouseDidConnectNotification object:nil];
    [nc addObserver:self selector:@selector(onMouseDisconnected:) name:GCMouseDidDisconnectNotification object:nil];
    [nc addObserver:self selector:@selector(onMouseBecameCurrent:) name:GCMouseDidBecomeCurrentNotification object:nil];
    [nc addObserver:self selector:@selector(onTextBegin) name:UITextFieldTextDidBeginEditingNotification object:nil];
    [nc addObserver:self selector:@selector(onTextEnd) name:UITextFieldTextDidEndEditingNotification object:nil];
    [nc addObserver:self selector:@selector(onTextBegin) name:UITextViewTextDidBeginEditingNotification object:nil];
    [nc addObserver:self selector:@selector(onTextEnd) name:UITextViewTextDidEndEditingNotification object:nil];
    [nc addObserver:self selector:@selector(onAppResignActive) name:UIApplicationWillResignActiveNotification object:nil];
    [nc addObserver:self selector:@selector(onAppResignActive) name:UISceneWillDeactivateNotification object:nil];

    if (GCKeyboard.coalescedKeyboard) {
        sdvAttachKeyboardHandlers(GCKeyboard.coalescedKeyboard);
    }
    for (GCMouse *m in GCMouse.mice) {
        sdvAttachMouseHandlers(m);
    }
    if (GCMouse.current) {
        sdvAttachMouseHandlers(GCMouse.current);
    }
    sdvBootLogFmt([NSString stringWithFormat:@"[EXT-INPUT] Started (GP=%lu KB=%d Mice=%lu)",
        (unsigned long)GCController.controllers.count,
        GCKeyboard.coalescedKeyboard != nil ? 1 : 0,
        (unsigned long)GCMouse.mice.count]);
}

-(void)attachToWindow:(UIWindow *)win {
    if (!win) return;
    if (!gExtInitialized) [self start];
    gExtPrimaryGameWindow = win;
    CGRect b = win.bounds;
    if (b.size.width > 100.0 && b.size.height > 100.0) {
        os_unfair_lock_lock(&gExtLock);
        gExtWinW = b.size.width;
        gExtWinH = b.size.height;
        os_unfair_lock_unlock(&gExtLock);
    }
    sdvInstallIndirectTouchFilterOnView(win);
    if ([_hoverWindows containsObject:win]) return;

    UIHoverGestureRecognizer *hover = [[UIHoverGestureRecognizer alloc] initWithTarget:self action:@selector(handleHover:)];
    [win addGestureRecognizer:hover];
    [_hoverWindows addObject:win];
}

-(void)handleHover:(UIHoverGestureRecognizer *)gr {
    UIView *v = gr.view;
    if (!v) return;
    CGRect b = v.bounds;
    if (b.size.width <= 1.0 || b.size.height <= 1.0) return;
    if (gr.state == UIGestureRecognizerStateBegan || gr.state == UIGestureRecognizerStateChanged) {
        CGPoint p = [gr locationInView:v];
        float nx = (float)(p.x / b.size.width);
        float ny = (float)(p.y / b.size.height);
        if (nx < 0.0f) nx = 0.0f; else if (nx > 1.0f) nx = 1.0f;
        if (ny < 0.0f) ny = 0.0f; else if (ny > 1.0f) ny = 1.0f;
        CFTimeInterval now = CACurrentMediaTime();
        os_unfair_lock_lock(&gExtLock);
        gExtWinW = b.size.width;
        gExtWinH = b.size.height;
        gMouseNormX = nx;
        gMouseNormY = ny;
        gLastHoverTime = now;
        gHasHoverSeen = YES;
        gMouseActivitySeq++;
        os_unfair_lock_unlock(&gExtLock);
    }
}

-(void)onControllerDisconnected:(NSNotification *)n {
    (void)n;
    sdvRumbleStopImmediate(YES);
    sdvBootLog("[EXT-INPUT] Physical Gamepad disconnected -> stopped CoreHaptics rumble");
}

-(void)onKeyboardConnected:(NSNotification *)n {
    GCKeyboard *kb = [n.object isKindOfClass:GCKeyboard.class] ? (GCKeyboard *)n.object : GCKeyboard.coalescedKeyboard;
    sdvAttachKeyboardHandlers(kb);
    sdvBootLog("[EXT-INPUT] Physical Keyboard connected");
}

-(void)onKeyboardDisconnected:(NSNotification *)n {
    (void)n;
    sdvBootLog("[EXT-INPUT] Physical Keyboard disconnected");
}

-(void)onMouseConnected:(NSNotification *)n {
    GCMouse *m = [n.object isKindOfClass:GCMouse.class] ? (GCMouse *)n.object : GCMouse.current;
    sdvAttachMouseHandlers(m);
    sdvBootLog("[EXT-INPUT] Physical Mouse connected");
}

-(void)onMouseBecameCurrent:(NSNotification *)n {
    GCMouse *m = [n.object isKindOfClass:GCMouse.class] ? (GCMouse *)n.object : GCMouse.current;
    sdvAttachMouseHandlers(m);
}

-(void)onMouseDisconnected:(NSNotification *)n {
    (void)n;
    BOOL anyLeft = (GCMouse.mice.count > 0 || GCMouse.current != nil);
    os_unfair_lock_lock(&gExtLock);
    gHasGCMouseConnected = anyLeft;
    if (!anyLeft) gMouseButtonsMask = 0;
    os_unfair_lock_unlock(&gExtLock);
    sdvBootLog("[EXT-INPUT] Physical Mouse disconnected");
}

-(void)onTextBegin {
    os_unfair_lock_lock(&gExtLock);
    gNativeTextEditingCount++;
    os_unfair_lock_unlock(&gExtLock);
}

-(void)onTextEnd {
    os_unfair_lock_lock(&gExtLock);
    if (gNativeTextEditingCount > 0) gNativeTextEditingCount--;
    os_unfair_lock_unlock(&gExtLock);
}

-(void)onAppResignActive {
    sdvRumbleStopImmediate(NO);
    os_unfair_lock_lock(&gExtLock);
    gActiveDirectTouchCount = 0;
    gMouseButtonsMask = 0;
    gMouseButtonsLatchedMask = 0;
    gKbLatchedCount = 0;
    os_unfair_lock_unlock(&gExtLock);
}
@end

static void sdvExtSetGardenOpen(BOOL open) {
    gGardenOpenCached = open ? 1 : 0;
    if (open) {
        sdvRumbleStopImmediate(NO);
        os_unfair_lock_lock(&gExtLock);
        gActiveDirectTouchCount = 0;
        gMouseButtonsLatchedMask = 0;
        gKbLatchedCount = 0;
        gTextRingHead = gTextRingTail;
        os_unfair_lock_unlock(&gExtLock);
    }
}

static BOOL sdvIsGameInputSuppressed(void) {
    if (gNativeTextEditingCount > 0) return YES;
    if (gGardenOpenCached) return YES;
    if (NSThread.isMainThread) {
        UIWindow *gw = gExtPrimaryGameWindow;
        if (gw && gw.rootViewController.presentedViewController != nil) {
            return YES;
        }
    }
    return NO;
}

// Exported C ABI for C# SDVMobileControls via NativeLibrary / dlsym(RTLD_DEFAULT, ...)
__attribute__((visibility("default")))
int32_t sdv_ext_get_direct_touch_count(void) {
    os_unfair_lock_lock(&gExtLock);
    int32_t c = gActiveDirectTouchCount;
    os_unfair_lock_unlock(&gExtLock);
    return c;
}

__attribute__((visibility("default")))
void sdv_ext_set_mouse_norm(float nx, float ny) {
    if (nx < 0.0f) nx = 0.0f; else if (nx > 1.0f) nx = 1.0f;
    if (ny < 0.0f) ny = 0.0f; else if (ny > 1.0f) ny = 1.0f;
    os_unfair_lock_lock(&gExtLock);
    gMouseNormX = nx;
    gMouseNormY = ny;
    os_unfair_lock_unlock(&gExtLock);
}

__attribute__((visibility("default")))
int32_t sdv_ext_poll_input(SDVExtInputSnapshot *outSnap) {
    if (!outSnap) return 0;
    memset(outSnap, 0, sizeof(SDVExtInputSnapshot));
    outSnap->structSize = (int32_t)sizeof(SDVExtInputSnapshot);

    @autoreleasepool {
    if (!gExtInitialized) {
        dispatch_async(dispatch_get_main_queue(), ^{
            [[SDVExternalInputHost shared] start];
            if (gSdvGardenHost) {
                UIWindow *gw = [gSdvGardenHost gameWindow];
                if (gw) [[SDVExternalInputHost shared] attachToWindow:gw];
            }
        });
        return 1;
    }

    BOOL suppressed = sdvIsGameInputSuppressed();
    outSnap->suppressGameInput = suppressed ? 1 : 0;

    // 1. Poll physical Gamepad (GCController)
    GCController *ctrl = GCController.current ?: GCController.controllers.firstObject;
    if (ctrl && ctrl.extendedGamepad) {
        GCExtendedGamepad *eg = ctrl.extendedGamepad;
        outSnap->gpConnected = 1;
        if (!suppressed) {
            uint32_t mask = 0;
            if (eg.buttonA.isPressed) mask |= (1u << 0);
            if (eg.buttonB.isPressed) mask |= (1u << 1);
            if (eg.buttonX.isPressed) mask |= (1u << 2);
            if (eg.buttonY.isPressed) mask |= (1u << 3);
            if (eg.leftShoulder.isPressed)  mask |= (1u << 4);
            if (eg.rightShoulder.isPressed) mask |= (1u << 5);
            if (eg.buttonMenu.isPressed)    mask |= (1u << 6);
            if (eg.buttonOptions && eg.buttonOptions.isPressed) mask |= (1u << 7);
            if (eg.buttonHome && eg.buttonHome.isPressed)       mask |= (1u << 8);
            if (eg.leftThumbstickButton && eg.leftThumbstickButton.isPressed)   mask |= (1u << 9);
            if (eg.rightThumbstickButton && eg.rightThumbstickButton.isPressed) mask |= (1u << 10);
            if (eg.dpad.up.isPressed)    mask |= (1u << 11);
            if (eg.dpad.down.isPressed)  mask |= (1u << 12);
            if (eg.dpad.left.isPressed)  mask |= (1u << 13);
            if (eg.dpad.right.isPressed) mask |= (1u << 14);

            float lx = eg.leftThumbstick.xAxis.value;
            float ly = eg.leftThumbstick.yAxis.value;
            float rx = eg.rightThumbstick.xAxis.value;
            float ry = eg.rightThumbstick.yAxis.value;
            float lt = eg.leftTrigger.value;
            float rt = eg.rightTrigger.value;

            outSnap->gpButtonsMask = mask;
            outSnap->gpLeftX = lx;
            outSnap->gpLeftY = ly;
            outSnap->gpRightX = rx;
            outSnap->gpRightY = ry;
            outSnap->gpLeftTrigger = lt;
            outSnap->gpRightTrigger = rt;

            BOOL gpActiveNow = (mask != 0) ||
                (lx * lx + ly * ly > 0.04f) ||
                (rx * rx + ry * ry > 0.04f) ||
                (lt > 0.15f) || (rt > 0.15f);
            if (gpActiveNow) {
                gGpActivitySeq++;
            }
        }
        outSnap->gpActivitySeq = gGpActivitySeq;
    }

    // 2. Poll physical Keyboard (GCKeyboard.coalescedKeyboard) + merge latched fast taps
    GCKeyboard *kb = GCKeyboard.coalescedKeyboard;
    int32_t kCnt = 0;
    if (kb && kb.keyboardInput) {
        GCKeyboardInput *ki = kb.keyboardInput;
        outSnap->kbConnected = 1;
        if (!suppressed && ki.isAnyKeyPressed) {
            for (int32_t i = 0; i < gKeyMapCount && kCnt < 32; i++) {
                GCControllerButtonInput *btn = [ki buttonForKeyCode:gKeyMap[i].gcCode];
                if (btn && btn.isPressed) {
                    outSnap->kbKeys[kCnt++] = gKeyMap[i].xnaKey;
                }
            }
            if (kCnt > 0) {
                os_unfair_lock_lock(&gExtLock);
                gKbActivitySeq++;
                os_unfair_lock_unlock(&gExtLock);
            }
        }
    }

    // 3. Snapshot Mouse + Keyboard latched taps + text ring under lock
    GCMouse *curMouse = GCMouse.current ?: GCMouse.mice.firstObject;
    os_unfair_lock_lock(&gExtLock);
    if (!suppressed && gKbLatchedCount > 0) {
        if (!outSnap->kbConnected) outSnap->kbConnected = 1;
        for (int32_t i = 0; i < gKbLatchedCount && kCnt < 32; i++) {
            uint16_t lk = gKbLatchedKeys[i];
            BOOL dup = NO;
            for (int32_t j = 0; j < kCnt; j++) {
                if (outSnap->kbKeys[j] == lk) { dup = YES; break; }
            }
            if (!dup) outSnap->kbKeys[kCnt++] = lk;
        }
    }
    gKbLatchedCount = 0;
    outSnap->kbKeyCount = kCnt;
    outSnap->kbActivitySeq = gKbActivitySeq;

    if (!suppressed) {
        int32_t tCnt = 0;
        while (gTextRingHead != gTextRingTail && tCnt < 32) {
            outSnap->textChars[tCnt++] = gTextRing[gTextRingHead];
            gTextRingHead = (gTextRingHead + 1) & 63;
        }
        outSnap->textCharCount = tCnt;
    } else {
        // Drain pending text ring & reset touch count while suppressed so stale keys/touches don't replay on close
        gTextRingHead = gTextRingTail;
        gActiveDirectTouchCount = 0;
    }

    BOOL mConn = (curMouse != nil || gHasGCMouseConnected || gHasHoverSeen);
    outSnap->mouseConnected = mConn ? 1 : 0;
    outSnap->mouseNormX = gMouseNormX;
    outSnap->mouseNormY = gMouseNormY;
    if (!suppressed) {
        if (curMouse && curMouse.mouseInput) {
            GCMouseInput *mi = curMouse.mouseInput;
            uint32_t mask = 0;
            if (mi.leftButton.isPressed)   mask |= (1u << 0);
            if (mi.rightButton.isPressed)  mask |= (1u << 1);
            if (mi.middleButton.isPressed) mask |= (1u << 2);
            NSArray<GCControllerButtonInput *> *aux = mi.auxiliaryButtons;
            if (aux.count > 0 && aux[0].isPressed) mask |= (1u << 3);
            if (aux.count > 1 && aux[1].isPressed) mask |= (1u << 4);
            gMouseButtonsMask = mask;
        }
        outSnap->mouseButtonsMask = gMouseButtonsMask | gMouseButtonsLatchedMask;
        gMouseButtonsLatchedMask = 0;
        outSnap->mouseScrollDelta = gMouseScrollPending;
        gMouseScrollPending = 0;
    } else {
        gMouseButtonsLatchedMask = 0;
        gMouseScrollPending = 0;
    }
    outSnap->mouseActivitySeq = gMouseActivitySeq;
    os_unfair_lock_unlock(&gExtLock);

    return 1;
    }
}

static void sdvExtInputStart(void) {
    [[SDVExternalInputHost shared] start];
}

static void sdvExtInputAttachWindow(UIWindow *win) {
    [[SDVExternalInputHost shared] attachToWindow:win];
}
