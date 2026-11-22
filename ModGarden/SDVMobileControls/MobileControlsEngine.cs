using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.Tools;

namespace SDVMobileControls
{
    internal sealed class MobileControlsEngine
    {
        private readonly MobileConfig _cfg;
        private readonly IMonitor _monitor;
        private readonly MobileSaveSlotManager _saveSlots;
        private readonly MobileInventoryShopHelper _invShop = new MobileInventoryShopHelper();
        private readonly MobileMinigamePad _minigamePad = new MobileMinigamePad();
        private readonly VerticalToolbar _verticalToolbar = new VerticalToolbar();
        private PadBridge _bridge;
        private bool _loggedFirstIntercept;

        // Textures (created lazily on GPU thread)
        private Texture2D _pixel;
        private Texture2D _circle;
        private Texture2D _ring;

        // Dynamic viewport dimensions (NEVER hardcoded to any device resolution!)
        private int _vpW;
        private int _vpH;
        private int _rawW;
        private int _rawH;

        // Gameplay Utility Bar
        private Rectangle _btnBagRect;
        private Rectangle _btnMapRect;
        private Rectangle _btnQuestRect;
        private Rectangle _btnSaveRect;
        private Rectangle _btnKeyRect;
        private Rectangle _btnEmoteRect;
        private Rectangle _btnCtlModeRect;
        private Rectangle _btnEditRect;
        private Rectangle _btnCollapseRect;
        private Rectangle _topBarGroupRect;
        private bool _topBarCollapsed;

        // Stardew-Pixel Multi-Slot Save Menu Modal (replaces legacy MODS drawer — zero old SDViOS controls!)
        private bool _saveMenuOpen;
        private int _saveSelectedIdx;
        private int _saveScrollIdx;
        private int _saveSwipeTouchId = -1;
        private float _saveSwipeStartY;
        private float _saveSwipeLastY;
        private float _saveSwipeAccumY;
        private bool _saveSwipeMoved;
        private Rectangle _savePanelRect;
        private Rectangle _saveCloseRect;
        private Rectangle _saveListRect;
        private Rectangle _saveNewBtnRect;
        private Rectangle _saveLoadBtnRect;
        private Rectangle _saveDelBtnRect;
        private Rectangle _saveUpBtnRect;
        private Rectangle _saveDnBtnRect;
        private string _saveToastText;
        private double _saveToastUntil;
        private double _ramWarningUntil;
        private bool _ramCritical;

        // Context Menu Helper Bar (only in Inventory/Chest/Shop/TextInput, plus ESC-only on QuestLog!)
        private Rectangle _ctxX1Rect;
        private Rectangle _ctxHalfRect;
        private Rectangle _ctxXferRect;
        private Rectangle _ctxKeyRect;
        private Rectangle _ctxEscRect;
        private Rectangle _ctxQuestEscRect;
        private int _ctxTouchId = -1;

        // Invisible Mode rects (Left Joystick Zone + 2 Right Vertical Rectangular Bars)
        private Rectangle _invJoyRect;
        private Rectangle _invToolRect;
        private Rectangle _invActRect;

        // Visible 2-Button Mode geometry (Joystick circle + 2 circular buttons)
        private Vector2 _visJoyCenter;
        private float _visJoyRadius = 100f;
        private Rectangle _visToolRect;
        private Rectangle _visActRect;

        // Custom PC Key Buttons runtime tracking (up to 16 simultaneous custom PC buttons)
        private readonly Dictionary<int, int> _customBtnTouchIds = new Dictionary<int, int>(16); // btnId -> touchId
        private readonly Dictionary<int, int> _customBtnPulseFrames = new Dictionary<int, int>(16); // btnId -> remaining pulse frames
        private readonly Dictionary<int, double> _customBtnLastPressSec = new Dictionary<int, double>(16); // btnId -> last press timestamp
        private readonly HashSet<int> _customBtnsHeldNow = new HashSet<int>();

        // Active joystick runtime state
        private int _joyTouchId = -1;
        private Vector2 _joyOrigin;
        private Vector2 _joyKnob;
        private Vector2 _joyTouchStart;
        private double _joyTouchStartTime;
        private bool _joyActivated;
        private float _activeJoyRadius = 100f;

        // Right-side 2 action buttons runtime state (UseTool = X, Interact = A)
        private int _toolTouchId = -1;
        private int _interactTouchId = -1;
        private bool _toolPressed;
        private bool _interactPressed;
        private float _toolRippleAlpha;
        private float _interactRippleAlpha;

        // Pinch-to-Zoom state
        private bool _pinching;
        private float _pinchStartDist;
        private float _pinchStartZoom;
        private int _pinchIdA = -1;
        private int _pinchIdB = -1;

        // Tap-to-Move / Hold-to-Walk state
        private int _tapTouchId = -1;
        private Vector2 _tapStartScreen;
        private Vector2 _tapCurScreen;
        private double _tapStartTime;
        private bool _holdWalking;
        private bool _tapChargingTool;
        private List<Point> _path;
        private int _pathIdx;
        private Point _targetTile;
        private bool _hasTargetTile;
        private TileIntent _pendingIntent;
        private bool _hasPendingWarp;
        private Point _pendingWarpTile;
        private int _warpPushFrames;
        private Vector2 _lastPlayerPos;
        private int _stuckFrames;
        private int _actionPulseXFrames;
        private int _actionPulseAFrames;
        private int _warpPushStartHudCount;
        private object _lastPadObj;
        private string _lastLocationName;
        private MobileControlMode _lastModeSeen = MobileControlMode.VirtualGamepad;
        private bool _wasInMenu;
        private Vector2 _unstickLastPos;
        private int _unstickStillTicks;

        // Slingshot touch drag-to-aim state
        private int _slingshotTouchId = -1;
        private Vector2 _slingshotRawPos;
        private Vector2 _slingshotVpPos;

        // Auto-attack state (default OFF)
        private int _autoAttackCooldown;

        // Menu 1-finger tap / drag / swipe scroll / camera pan state (single TouchPanel.GetState() per frame!)
        private int _menuTapTouchId = -1;
        private Vector2 _menuTapStartVp;
        private Vector2 _menuTapLastVp;
        private float _menuTapLastY;
        private float _menuTapAccumY;
        private bool _menuTapDragged;
        private bool _menuTapImmediateDrag;
        private bool _menuTapCameraPan;
        private double _menuTapStartSec;
        private bool _menuTapRightClicked;
        private int _holdSweepCooldown;
        private int _lastAppliedSafeRightPad = -999;
        private int _worldReadyTicks;
        private int _unstickHoldTicks;

        // Hide GUI mode (1-tap HIDE -> clean full-screen photo mode hiding HUD & buttons; double-tap -> restore!)
        private bool _guiHidden;
        private bool _preHideDisplayHud = true;
        private Rectangle _btnHideGuiRect;
        private Rectangle _btnChatRect;
        private Rectangle _btnSkipEventRect;
        private double _lastHideTapSec = -10.0;
        private Vector2 _lastHideTapVp;
        private int _hideTapTouchId = -1;
        private Vector2 _hideTapStartVp;

        // Smooth throttled Pinch-to-Zoom (avoids 50x Window_ClientSizeChanged spam!)
        private double _lastPinchApplySec;
        private float _pendingPinchZoom;
        private bool _pinchDirtySave;

        // Universal 3rd-Party Mod Menu Simulated Mouse State Machine (GMCM, SpaceCore, CJB, LookupAnything, StardewUI, etc.)
        private enum ModTouchState { None = 0, Pending = 1, MouseDrag = 2, SwipeScroll = 3, RightClicked = 4 }
        private ModTouchState _modTouchState = ModTouchState.None;
        private Point? _simMousePulsePos;
        private int _simMouseLeftPulseFrames;
        private int _simMouseRightPulseFrames;
        private Point? _lastMenuMouseRaw;

        // Safe 2-Tap Preview on Robin/Wizard CarpenterMenu (shows green/red grid before confirming!)
        private CarpenterMenu _carpenterPreviewMenu;
        private Vector2 _carpenterPreviewVp;
        private bool _hasCarpenterPreview;

        // Tap-to-Move Fishing Cast Sideways Bend (steer bobber left/right into fish bubbles!)
        private int _fishBendTouchId = -1;
        private Vector2 _fishBendStartVp;
        private float _fishBendX;

        // Community Center bundle & safe 2-tap Dialogue state
        private static readonly System.Reflection.FieldInfo RenovateFreezeField =
            typeof(RenovateMenu).GetField("freeze", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        private JunimoNoteMenu _junimoHoverMenu;
        private ClickableComponent _junimoHoveredSlot;
        private DialogueBox _dlgHoverMenu;
        private int _dlgHoveredIdx = -1;
        private int _weatherTickCounter;

        // Auto iOS keyboard on TextBox focus
        private IKeyboardSubscriber _lastKbSubscriber;
        private double _lastAutoKbSec;

        // Toolbar tap/swipe + ROW quick-switch + Furniture ROT + Weapon SPEC button
        private int _toolbarTouchId = -1;
        private Vector2 _toolbarStartVp;
        private int _toolbarTappedSlot = -1;
        private bool _toolbarSwiped;
        private Rectangle _btnToolbarRowRect;
        private Rectangle _btnRotateRect;
        private Rectangle _btnSpecRect;

        // Auto-Repeat 1-tap chop/mine + Optional restore previous slot after Auto-Tool
        private bool _repeatActive;
        private Point _repeatTile;
        private int _repeatCooldown;
        private int _autoToolPrevSlot = -1;
        private int _autoToolRestoreTimer;

        // Auto-Combat Mode (Mines/Volcano) + Bluetooth GamePad auto-hide + Haptic + SafeArea + 30fps saver + UI Scale restore
        private bool _inCombatAutoSwitched;
        private string _combatManualOverrideLoc;
        private bool _gamepadHideHud;
        private int _gamepadPollCounter;
        private GamePadState _lastPhysicalGamePadState;
        private bool _hasPhysicalGamePadState;
        private static readonly System.Reflection.MethodInfo GamePadPlatformGetStateMethod =
            typeof(GamePad).GetMethod("PlatformGetState",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                null,
                new[] { typeof(int), typeof(GamePadDeadZone), typeof(GamePadDeadZone) },
                null);
        private static readonly object[] GamePadPlatformArgs =
            new object[] { 0, GamePadDeadZone.IndependentAxes, GamePadDeadZone.IndependentAxes };
        private bool _appliedUiScaleOverride;
        private float _origDesiredUiScale = 1.0f;
        private bool _wasFishNibbling;
        private int _safeLeftPx;
        private int _safeRightPx;
        private bool _saverFrameParity;

        // Safe periodic AutoSave state (default OFF, skips combat/mines/moving)
        private double _lastAutoSaveSec = -1;
        private double _idleSinceSec = -1;
        private int _lastAutoSaveTimeOfDay = -1;
        private string _lastAutoSaveLoc;

        // ---- Dedicated Mobile Edit Layout Mode + In-Game PC Key Picker ----
        private enum EditTarget { None = 0, Joy = 1, Tool = 2, Act = 3, TopBar = 4, CustomKey = 5, Toolbar = 6 }
        private bool _editMode;
        private EditTarget _editSelected = EditTarget.None;
        private int _editSelectedCustomId = -1;
        private EditTarget _editDragTarget = EditTarget.None;
        private int _editDragCustomId = -1;
        private bool _editResizing;
        private int _editTouchId = -1;
        private Vector2 _editDragLastVp;

        private Rectangle _editToolbarRect;
        private Rectangle _editAddKeyBtnRect;
        private Rectangle _editDelKeyBtnRect;
        private Rectangle _editResetBtnRect;
        private Rectangle _editCancelBtnRect;
        private Rectangle _editSaveBtnRect;

        // Built-in PC Key Picker Modal inside Edit Layout
        private bool _keyPickerOpen;
        private Rectangle _pickerPanelRect;
        private Rectangle _pickerCloseRect;

        private readonly struct KeyChoice
        {
            public readonly string Label;
            public readonly Keys Key;
            public KeyChoice(string label, Keys key) { Label = label; Key = key; }
        }

        private static readonly KeyChoice[] PcKeyCatalog = new[]
        {
            // Popular mod & gameplay keys first
            new KeyChoice("P", Keys.P),
            new KeyChoice("I", Keys.I),
            new KeyChoice("F1", Keys.F1),
            new KeyChoice("TAB", Keys.Tab),
            new KeyChoice("SHFT", Keys.LeftShift),
            new KeyChoice("CTRL", Keys.LeftControl),
            new KeyChoice("ALT", Keys.LeftAlt),
            new KeyChoice("SPC", Keys.Space),
            new KeyChoice("ENT", Keys.Enter),
            new KeyChoice("ESC", Keys.Escape),
            new KeyChoice("T", Keys.T),
            new KeyChoice("Y", Keys.Y),
            // Function row
            new KeyChoice("F2", Keys.F2), new KeyChoice("F3", Keys.F3), new KeyChoice("F4", Keys.F4),
            new KeyChoice("F5", Keys.F5), new KeyChoice("F6", Keys.F6), new KeyChoice("F7", Keys.F7),
            new KeyChoice("F8", Keys.F8), new KeyChoice("F9", Keys.F9), new KeyChoice("F10", Keys.F10),
            new KeyChoice("F11", Keys.F11), new KeyChoice("F12", Keys.F12), new KeyChoice("BCK", Keys.Back),
            // Letters (remaining A-Z)
            new KeyChoice("A", Keys.A), new KeyChoice("B", Keys.B), new KeyChoice("C", Keys.C),
            new KeyChoice("D", Keys.D), new KeyChoice("E", Keys.E), new KeyChoice("F", Keys.F),
            new KeyChoice("G", Keys.G), new KeyChoice("H", Keys.H), new KeyChoice("J", Keys.J),
            new KeyChoice("K", Keys.K), new KeyChoice("L", Keys.L), new KeyChoice("M", Keys.M),
            new KeyChoice("N", Keys.N), new KeyChoice("O", Keys.O), new KeyChoice("Q", Keys.Q),
            new KeyChoice("R", Keys.R), new KeyChoice("S", Keys.S), new KeyChoice("U", Keys.U),
            new KeyChoice("V", Keys.V), new KeyChoice("W", Keys.W), new KeyChoice("X", Keys.X),
            new KeyChoice("Z", Keys.Z),
            // Numbers & symbols
            new KeyChoice("1", Keys.D1), new KeyChoice("2", Keys.D2), new KeyChoice("3", Keys.D3),
            new KeyChoice("4", Keys.D4), new KeyChoice("5", Keys.D5), new KeyChoice("6", Keys.D6),
            new KeyChoice("7", Keys.D7), new KeyChoice("8", Keys.D8), new KeyChoice("9", Keys.D9),
            new KeyChoice("0", Keys.D0), new KeyChoice("-", Keys.OemMinus), new KeyChoice("+", Keys.OemPlus),
            new KeyChoice("[", Keys.OemOpenBrackets), new KeyChoice("]", Keys.OemCloseBrackets),
            new KeyChoice("~", Keys.OemTilde), new KeyChoice("UP", Keys.Up),
            new KeyChoice("DN", Keys.Down), new KeyChoice("LT", Keys.Left), new KeyChoice("RT", Keys.Right),
        };

        public MobileControlsEngine(MobileConfig cfg, IMonitor monitor, string modDir)
        {
            _cfg = cfg;
            _monitor = monitor;
            _saveSlots = new MobileSaveSlotManager(modDir, monitor);
        }

        public void OnGameSaved()
        {
            if (_saveSlots.FinalizePendingSnapshot(out string summary))
            {
                ShowSaveToast("DA LUU: " + summary, Environment.TickCount64 / 1000.0);
            }
        }

        public void ShowSaveToast(string text, double nowSec)
        {
            _saveToastText = text;
            _saveToastUntil = nowSec + 3.0;
        }

        public MobileControlMode CurrentMode => EffectiveMode;
        public bool IsMobileModeActive => _cfg.Mode != MobileControlMode.VirtualGamepad;

        private MobileControlMode EffectiveMode =>
            (_inCombatAutoSwitched && _cfg.AutoCombatMode && _cfg.Mode == MobileControlMode.TapToMove)
                ? MobileControlMode.InvisibleJoystick2Btn
                : _cfg.Mode;

        private bool IsInvisibleBarMode =>
            EffectiveMode == MobileControlMode.InvisibleJoystick2Btn ||
            EffectiveMode == MobileControlMode.TapAndJoystick;

        private void SetGuiHidden(bool hide)
        {
            if (_guiHidden == hide)
                return;
            if (hide)
            {
                _preHideDisplayHud = Game1.displayHUD;
                _guiHidden = true;
                Game1.displayHUD = false;
            }
            else
            {
                _guiHidden = false;
                Game1.displayHUD = _preHideDisplayHud;
            }
        }

        private PadBridge EnsureBridge(object pad)
        {
            if (_bridge == null && pad != null)
                _bridge = new PadBridge(pad.GetType());
            return _bridge;
        }

        private static bool IsContinuousHoldKey(int keyCode)
        {
            // Modifiers (Left/Right Shift, Ctrl, Alt), Arrow keys (Left/Up/Right/Down), Space
            return (keyCode >= 160 && keyCode <= 165) ||
                   (keyCode >= 37 && keyCode <= 40) ||
                   keyCode == 32;
        }

        public void PopulateExtraKeys(HashSet<Keys> targetSet)
        {
            if (targetSet == null || !IsMobileModeActive || _editMode)
                return;

            if (Game1.currentMinigame != null)
            {
                _minigamePad.PopulateKeys(targetSet);
                return;
            }

            for (int i = 0; i < _cfg.CustomKeys.Count; i++)
            {
                var btn = _cfg.CustomKeys[i];
                if (btn.KeyCode <= 0) continue;
                bool held = _customBtnsHeldNow.Contains(btn.Id);
                bool pulsing = _customBtnPulseFrames.TryGetValue(btn.Id, out int rem) && rem > 0;
                bool active = IsContinuousHoldKey(btn.KeyCode) ? (held || pulsing) : pulsing;
                if (active)
                {
                    targetSet.Add((Keys)btn.KeyCode);
                }
            }
        }

        public void PopulateExtraKeysList(List<Keys> targetList)
        {
            if (targetList == null || !IsMobileModeActive || _editMode)
                return;

            if (Game1.currentMinigame != null)
            {
                _minigamePad.PopulateKeysList(targetList);
                return;
            }

            for (int i = 0; i < _cfg.CustomKeys.Count; i++)
            {
                var btn = _cfg.CustomKeys[i];
                if (btn.KeyCode <= 0) continue;
                bool held = _customBtnsHeldNow.Contains(btn.Id);
                bool pulsing = _customBtnPulseFrames.TryGetValue(btn.Id, out int rem) && rem > 0;
                bool active = IsContinuousHoldKey(btn.KeyCode) ? (held || pulsing) : pulsing;
                if (active)
                {
                    Keys k = (Keys)btn.KeyCode;
                    if (!targetList.Contains(k))
                        targetList.Add(k);
                }
            }
        }

        public bool ShouldSuppressOriginalDraw(object pad)
        {
            if (!IsMobileModeActive || pad == null)
                return false;
            var br = EnsureBridge(pad);
            if (br == null)
                return false;
            // Never let legacy HotkeyDrawer resurface the old SDViOS gamepad!
            br.CloseHotkeyDrawer(pad);
            if (br.IsFullScreenModalOpen(pad) || br.IsQuickSaveReloading())
                return false;
            return true;
        }

        public bool ShouldInterceptGameplay(object pad)
        {
            if (pad == null)
                return false;

            if (Context.IsWorldReady && _cfg.TryConsumeRamAlertFlag(out string ramAlert))
            {
                double nowSec = Environment.TickCount64 / 1000.0;
                bool isCrit = ramAlert.StartsWith("CRIT|", StringComparison.Ordinal);
                if (isCrit || ramAlert.StartsWith("WARN|", StringComparison.Ordinal))
                    ramAlert = ramAlert.Substring(5);
                _ramCritical = isCrit;
                _ramWarningUntil = nowSec + (isCrit ? 11.0 : 7.5);
                _saveToastText = ramAlert;
                _saveToastUntil = nowSec + (isCrit ? 9.5 : 6.5);
                _topBarCollapsed = false;
                try { ModEntry.RamOptimizer?.RunAggressivePrune(isCrit ? "RamAlert90" : "RamAlert85"); } catch { }
                try { Game1.addHUDMessage(new HUDMessage(ramAlert, HUDMessage.error_type)); } catch { }
                try { Game1.playSound(isCrit ? "cancel" : "warn"); } catch { try { Game1.playSound("crafting"); } catch { } }
            }

            if (Context.IsWorldReady && Game1.activeClickableMenu == null && _cfg.ConsumeEditLayoutFlag())
            {
                if (_cfg.Mode == MobileControlMode.VirtualGamepad)
                {
                    _cfg.Mode = MobileControlMode.InvisibleJoystick2Btn;
                    _cfg.Save();
                }
                _saveMenuOpen = false;
                EnterEditLayoutMode();
            }

            if (!IsMobileModeActive)
                return false;
            if (!Context.IsWorldReady || Game1.player == null || Game1.currentLocation == null)
                return false;
            if (Game1.activeClickableMenu != null || Game1.eventUp || Game1.dialogueUp || Game1.paused)
                return false;
            if (Game1.currentMinigame != null && MobileMinigamePad.DetectCurrentArcadeMinigame() == MobileMinigamePad.ArcadeKind.None)
                return false;
            var br = EnsureBridge(pad);
            if (br == null)
                return false;
            br.CloseHotkeyDrawer(pad);
            if (br.IsFullScreenModalOpen(pad) || br.IsQuickSaveReloading())
                return false;
            return true;
        }

        private void EnterEditLayoutMode()
        {
            ResetGameplayState();
            _editMode = true;
            _keyPickerOpen = false;
            _editSelected = EditTarget.Tool;
            _editSelectedCustomId = -1;
            _editDragTarget = EditTarget.None;
            _editDragCustomId = -1;
            _editResizing = false;
            _editTouchId = -1;
        }

        public void TrackPadReference(object pad)
        {
            if (pad != null)
            {
                _lastPadObj = pad;
                EnsureBridge(pad);
            }
        }

        public bool IsFishingCastButtonHeld()
        {
            if (!Context.IsWorldReady || Game1.player == null ||
                Game1.activeClickableMenu != null || Game1.eventUp || Game1.dialogueUp || Game1.paused ||
                Game1.currentMinigame != null || _editMode || _saveMenuOpen || _guiHidden)
                return false;

            if (IsMobileModeActive)
            {
                if (_toolPressed || _tapChargingTool)
                    return true;
                if (_gamepadHideHud && _hasPhysicalGamePadState && _lastPhysicalGamePadState.Buttons.X == ButtonState.Pressed)
                    return true;
                return false;
            }

            // VirtualGamepad mode (handled directly by SDViOSTouchControls.TouchVirtualPad)
            if (_lastPadObj != null)
            {
                var br = EnsureBridge(_lastPadObj);
                if (br != null && br.GetButtonX(_lastPadObj))
                    return true;
            }
            return false;
        }

        public void ResetGameplayState()
        {
            _joyTouchId = -1;
            _joyActivated = false;
            _toolTouchId = -1;
            _interactTouchId = -1;
            _toolPressed = false;
            _interactPressed = false;
            _actionPulseXFrames = 0;
            _actionPulseAFrames = 0;
            _holdSweepCooldown = 0;
            _warpPushStartHudCount = 0;
            _pinching = false;
            _pinchIdA = -1;
            _pinchIdB = -1;
            _tapTouchId = -1;
            _holdWalking = false;
            _tapChargingTool = false;
            _slingshotTouchId = -1;
            _toolbarTouchId = -1;
            _toolbarTappedSlot = -1;
            _toolbarSwiped = false;
            _verticalToolbar.ResetTouch();
            _repeatActive = false;
            _repeatCooldown = 0;
            _unstickHoldTicks = 0;
            _unstickStillTicks = 0;
            _fishBendTouchId = -1;
            _fishBendX = 0f;
            _hideTapTouchId = -1;
            _minigamePad.Reset();
            ClearPath();
        }

        private static IClickableMenu GetTopmostMenu(IClickableMenu root)
        {
            IClickableMenu cur = root;
            if (cur is TitleMenu && TitleMenu.subMenu != null)
                cur = TitleMenu.subMenu;
            int guard = 0;
            while (cur != null && guard++ < 8)
            {
                IClickableMenu child = null;
                try { child = cur.GetChildMenu(); } catch { }
                if (child == null) break;
                cur = child;
            }
            return cur;
        }

        private static bool IsThirdPartyModMenu(IClickableMenu menu)
        {
            if (menu == null) return false;
            try { return menu.GetType().Assembly != typeof(Game1).Assembly; } catch { return false; }
        }

        private static bool IsSkippableCutsceneActive()
        {
            if (!Game1.eventUp || Game1.activeClickableMenu != null) return false;
            try
            {
                var ev = Game1.CurrentEvent;
                return ev != null && ev.skippable && !ev.skipped;
            }
            catch { return false; }
        }

        private void ClearCustomButtonTouches()
        {
            _customBtnTouchIds.Clear();
            _customBtnsHeldNow.Clear();
        }

        private void TickCustomButtonPulses()
        {
            if (_customBtnPulseFrames.Count == 0)
                return;
            // Decrement remaining pulse frames cleanly without allocating per frame
            for (int i = 0; i < _cfg.CustomKeys.Count; i++)
            {
                int id = _cfg.CustomKeys[i].Id;
                if (_customBtnPulseFrames.TryGetValue(id, out int rem) && rem > 0)
                    _customBtnPulseFrames[id] = rem - 1;
            }
        }

        private void ClearPath()
        {
            _path = null;
            _pathIdx = 0;
            _hasTargetTile = false;
            _pendingIntent = default;
            _hasPendingWarp = false;
            _warpPushFrames = 0;
            _stuckFrames = 0;
            _repeatActive = false;
            _repeatCooldown = 0;
        }

        private bool IsInventoryContextMenu(IClickableMenu topMenu)
        {
            if (topMenu == null || IsThirdPartyModMenu(topMenu))
                return false;
            return topMenu is GameMenu || topMenu is ItemGrabMenu || topMenu is ShopMenu || topMenu is JunimoNoteMenu;
        }

        private bool IsTextInputMenu(IClickableMenu topMenu)
        {
            if (topMenu is NamingMenu || topMenu is TitleTextInputMenu)
                return true;
            if (Game1.keyboardDispatcher != null && Game1.keyboardDispatcher.Subscriber != null)
                return true;
            return false;
        }

        private bool AllowCustomKeysInCurrentMenu()
        {
            var m = Game1.activeClickableMenu;
            if (m == null) return false;
            if (m is BobberBar || m is TitleMenu)
                return false;
            return Context.IsWorldReady;
        }

        private void ResetMenuTapState()
        {
            _menuTapTouchId = -1;
            _menuTapAccumY = 0f;
            _menuTapDragged = false;
            _menuTapImmediateDrag = false;
            _menuTapCameraPan = false;
            _menuTapRightClicked = false;
            _modTouchState = ModTouchState.None;
        }

        private void PollAutoKeyboard(object pad, PadBridge br, double nowSec)
        {
            if (!_cfg.AutoKeyboard)
                return;
            IKeyboardSubscriber cur = Game1.keyboardDispatcher?.Subscriber;
            if (cur == null)
            {
                _lastKbSubscriber = null;
                return;
            }
            if (!ReferenceEquals(cur, _lastKbSubscriber))
            {
                _lastKbSubscriber = cur;
                if ((nowSec - _lastAutoKbSec) >= 0.45)
                {
                    _lastAutoKbSec = nowSec;
                    br?.TriggerVirtualKeyboard(pad);
                }
            }
        }

        private static bool IsCameraPanMenu(IClickableMenu menu, Point uiPt)
        {
            if (menu is CarpenterMenu cm && cm.onFarm && !cm.freeze)
            {
                if (cm.cancelButton != null && cm.cancelButton.containsPoint(uiPt.X, uiPt.Y)) return false;
                if (cm.okButton != null && cm.okButton.containsPoint(uiPt.X, uiPt.Y)) return false;
                return true;
            }
            if (menu is PurchaseAnimalsMenu pam && pam.onFarm && !pam.freeze && !pam.namingAnimal)
            {
                if (pam.okButton != null && pam.okButton.containsPoint(uiPt.X, uiPt.Y)) return false;
                return true;
            }
            if (menu is AnimalQueryMenu aqm && aqm.movingAnimal)
            {
                if (aqm.okButton != null && aqm.okButton.containsPoint(uiPt.X, uiPt.Y)) return false;
                return true;
            }
            if (menu is RenovateMenu rm)
            {
                bool rmFrozen = false;
                try { if (RenovateFreezeField != null && RenovateFreezeField.GetValue(rm) is bool b) rmFrozen = b; } catch { }
                if (!rmFrozen)
                {
                    if (rm.okButton != null && rm.okButton.containsPoint(uiPt.X, uiPt.Y)) return false;
                    return true;
                }
            }
            if (menu is MuseumMenu mm && !mm.fadeIntoBlack)
            {
                if (mm.okButton != null && mm.okButton.containsPoint(uiPt.X, uiPt.Y)) return false;
                if (mm.inventory != null && mm.inventory.isWithinBounds(uiPt.X, uiPt.Y)) return false;
                return true;
            }
            return false;
        }

        private static CraftingPage GetActiveCraftingPage(IClickableMenu menu)
        {
            if (menu is CraftingPage cp)
                return cp;
            if (menu is GameMenu gm)
            {
                try { return gm.GetCurrentPage() as CraftingPage; } catch { }
            }
            return null;
        }

        private static ClickableTextureComponent FindCraftingRecipeAt(CraftingPage cp, Point uiPt, out CraftingRecipe recipe)
        {
            recipe = null;
            if (cp == null || cp.pagesOfCraftingRecipes == null)
                return null;
            int idx = cp.currentCraftingPage;
            if (idx < 0 || idx >= cp.pagesOfCraftingRecipes.Count)
                return null;
            var page = cp.pagesOfCraftingRecipes[idx];
            if (page == null) return null;
            foreach (var kv in page)
            {
                if (kv.Key != null && kv.Key.containsPoint(uiPt.X, uiPt.Y))
                {
                    recipe = kv.Value;
                    return kv.Key;
                }
            }
            return null;
        }

        private static Furniture GetHeldRotatableFurniture()
        {
            if (Game1.player?.ActiveObject is Furniture f && f.rotations != null && f.rotations.Value > 1)
                return f;
            return null;
        }

        private MeleeWeapon GetHeldSpecialWeapon()
        {
            if (!_cfg.ShowSpecButton || Game1.player?.CurrentTool is not MeleeWeapon mw || mw.isScythe())
                return null;

            bool combatCtx = (EffectiveMode == MobileControlMode.TapToMove || EffectiveMode == MobileControlMode.TapAndJoystick) ||
                             Game1.currentLocation is MineShaft ||
                             Game1.currentLocation is VolcanoDungeon ||
                             AutoToolAndCombat.FindMonsterNear(Game1.currentLocation, Game1.player.GetBoundingBox().Center.ToVector2(), 360f) != null;
            return combatCtx ? mw : null;
        }

        private static bool IsWeaponSpecialOnCooldown(MeleeWeapon mw)
        {
            if (mw == null) return true;
            if (mw.isOnSpecial) return true;
            if (mw.type.Value == MeleeWeapon.dagger) return MeleeWeapon.daggerCooldown > 0;
            if (mw.type.Value == MeleeWeapon.club) return MeleeWeapon.clubCooldown > 0;
            return MeleeWeapon.defenseCooldown > 0;
        }

        public void OnUpdateTickedGlobal()
        {
            _cfg.PollReload();
            ApplyLiveUiScaleAndWeather();

            if (Context.IsWorldReady)
            {
                if (_worldReadyTicks < 300) _worldReadyTicks++;
            }
            else
            {
                _worldReadyTicks = 0;
            }

            // 1. Auto-Pause when user pulls Notification/Control Center or switches app (singleplayer only, skips first 3s after world load!)
            // ALWAYS consume the flag even when disabled so turning AutoPause back ON later never triggers a stale pause!
            bool hasPauseFlag = _cfg.ConsumePauseFlag();
            if (_cfg.AutoPauseOnBackground && hasPauseFlag)
            {
                if (_worldReadyTicks >= 180 && Context.IsWorldReady && !Context.IsMultiplayer &&
                    Game1.activeClickableMenu == null && !Game1.paused && !Game1.eventUp &&
                    Game1.currentMinigame == null && !_editMode && !_saveMenuOpen)
                {
                    try { Game1.activeClickableMenu = new GameMenu(false); } catch { }
                }
            }

            // 2. Optional light iOS Haptic tap on fish bite edge (FishingRod.isNibbling false -> true)
            bool nibblingNow = Context.IsWorldReady && Game1.player?.CurrentTool is FishingRod fr && fr.isNibbling;
            if (nibblingNow && !_wasFishNibbling && _cfg.FishingHaptic)
                _cfg.TriggerFishBiteHaptic();
            _wasFishNibbling = nibblingNow;

            // 3. Vertical Scrollable Toolbar visibility sync & HIDE GUI cleanup on Menu / Cutscene / Return to Title!
            if (Context.IsWorldReady)
            {
                if (_guiHidden && (Game1.activeClickableMenu != null || Game1.eventUp || Game1.currentMinigame != null))
                    SetGuiHidden(false);
                _verticalToolbar.SyncHorizontalToolbarVisibility(IsMobileModeActive && (_cfg.VerticalToolbar || _guiHidden));
            }
            else
            {
                SetGuiHidden(false);
                _inCombatAutoSwitched = false;
                _combatManualOverrideLoc = null;
                _gamepadHideHud = false;
                _hasPhysicalGamePadState = false;
                _verticalToolbar.ClearCachedToolbar();
            }

            // 4. Safe Area Notch/Dynamic Island padding for DayTimeMoneyBox (live bidirectional!)
            ApplySafeAreaToDayTimeMoneyBox();

            // 5. Optional Battery Saver 30 FPS render cap (preserves 100% 60Hz logic speed; auto-suspends for Fishing cast bar/BobberBar/minigames/fades!)
            bool fishingCastAnim = Context.IsWorldReady && Game1.player?.CurrentTool is FishingRod frCast && (frCast.isTimingCast || frCast.isCasting);
            if (_cfg.BatterySaver30Fps &&
                !_editMode && !_saveMenuOpen &&
                !fishingCastAnim &&
                Game1.currentMinigame == null &&
                !(Game1.activeClickableMenu is BobberBar) &&
                !Game1.fadeToBlack)
            {
                _saverFrameParity = !_saverFrameParity;
                if (_saverFrameParity)
                {
                    try { GameRunner.instance?.SuppressDraw(); } catch { }
                }
            }
            else
            {
                _saverFrameParity = false;
            }
        }

        private void ApplySafeAreaToDayTimeMoneyBox()
        {
            if (!Context.IsWorldReady || Game1.dayTimeMoneyBox == null || _vpW <= 0)
                return;
            int rightPadVp = ComputeSafeRightPx();
            if (IsMobileModeActive && _cfg.VerticalToolbar && _cfg.ToolbarSide == 1 && !_guiHidden && _verticalToolbar.Bounds.Width > 0)
            {
                int tbRightNeed = (_vpW - _verticalToolbar.Bounds.Left) + 10;
                if (tbRightNeed > rightPadVp) rightPadVp = tbRightNeed;
            }
            if (rightPadVp == 0 && _lastAppliedSafeRightPad == 0)
                return;
            try
            {
                int uiW = Game1.uiViewport.Width;
                if (uiW <= 0) return;
                float padUi = rightPadVp * (uiW / (float)_vpW);
                float targetX = Math.Max(0f, uiW - 300f - padUi);
                if (rightPadVp != _lastAppliedSafeRightPad || Game1.dayTimeMoneyBox.position.X > targetX)
                {
                    Game1.dayTimeMoneyBox.position.X = targetX;
                    _lastAppliedSafeRightPad = rightPadVp;
                }
            }
            catch { }
        }

        private int ComputeSafeLeftPx()
        {
            if (_cfg.SafePaddingPx >= 0) return _cfg.SafePaddingPx;
            return (int)Math.Round(_cfg.NativeSafeLeftFrac * _vpW);
        }

        private int ComputeSafeRightPx()
        {
            if (_cfg.SafePaddingPx >= 0) return _cfg.SafePaddingPx;
            return (int)Math.Round(_cfg.NativeSafeRightFrac * _vpW);
        }

        private void CycleControlMode(double nowSec)
        {
            MobileControlMode cur = EffectiveMode;
            MobileControlMode next = cur switch
            {
                MobileControlMode.InvisibleJoystick2Btn => MobileControlMode.VisibleJoystick2Btn,
                MobileControlMode.VisibleJoystick2Btn => MobileControlMode.TapToMove,
                MobileControlMode.TapToMove => MobileControlMode.TapAndJoystick,
                _ => MobileControlMode.InvisibleJoystick2Btn
            };
            _cfg.Mode = next;
            _inCombatAutoSwitched = false;
            _combatManualOverrideLoc = Game1.currentLocation?.NameOrUniqueName;
            _cfg.Save();
            string name = next switch
            {
                MobileControlMode.InvisibleJoystick2Btn => "VO HINH",
                MobileControlMode.VisibleJoystick2Btn => "2 NUT",
                MobileControlMode.TapToMove => "CHAM DI",
                _ => "KET HOP"
            };
            ShowSaveToast("DIEU KHIEN: " + name, nowSec);
            try { Game1.playSound("smallSelect"); } catch { }
        }

        private void UpdateAutoCombatMode(double nowSec)
        {
            if (!_cfg.AutoCombatMode)
            {
                if (_inCombatAutoSwitched)
                {
                    _inCombatAutoSwitched = false;
                    ShowSaveToast("CHE DO CU", nowSec);
                }
                _combatManualOverrideLoc = null;
                return;
            }
            string curLoc = Game1.currentLocation?.NameOrUniqueName;
            bool isDungeon = Game1.currentLocation is MineShaft || Game1.currentLocation is VolcanoDungeon;
            if (!isDungeon)
            {
                _combatManualOverrideLoc = null;
                if (_inCombatAutoSwitched)
                {
                    _inCombatAutoSwitched = false;
                    ShowSaveToast("RA NGOAI: CHE DO CU", nowSec);
                }
                return;
            }

            // Inside MineShaft / VolcanoDungeon: keep runtime Joystick override active (immune to _cfg.Reload()!)
            if (!_inCombatAutoSwitched && _cfg.Mode == MobileControlMode.TapToMove &&
                !string.Equals(_combatManualOverrideLoc, curLoc, StringComparison.Ordinal))
            {
                _inCombatAutoSwitched = true;
                ShowSaveToast("VAO MO: TU DONG JOYSTICK", nowSec);
            }
        }

        private bool TryReadPhysicalGamePad(out GamePadState gs)
        {
            gs = default;
            if (GamePadPlatformGetStateMethod == null)
                return false;
            try
            {
                object res = GamePadPlatformGetStateMethod.Invoke(null, GamePadPlatformArgs);
                if (res is GamePadState state && state.IsConnected)
                {
                    gs = state;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private void PollGamepadAutoHide(int touchCount)
        {
            if (!_cfg.AutoHideOnGamepad)
            {
                _gamepadHideHud = false;
                _hasPhysicalGamePadState = false;
                return;
            }
            if (touchCount > 0)
            {
                _gamepadHideHud = false;
                _hasPhysicalGamePadState = false;
                return;
            }
            // Poll every frame while physical controller is actively driving gameplay; every 10 frames while idle
            if (!_gamepadHideHud && ++_gamepadPollCounter < 10)
                return;
            _gamepadPollCounter = 0;

            if (!TryReadPhysicalGamePad(out GamePadState gs))
            {
                _gamepadHideHud = false;
                _hasPhysicalGamePadState = false;
                return;
            }

            _lastPhysicalGamePadState = gs;
            _hasPhysicalGamePadState = true;
            bool used =
                gs.ThumbSticks.Left.LengthSquared() > 0.08f ||
                gs.ThumbSticks.Right.LengthSquared() > 0.08f ||
                gs.Triggers.Left > 0.2f || gs.Triggers.Right > 0.2f ||
                gs.Buttons.A == ButtonState.Pressed || gs.Buttons.B == ButtonState.Pressed ||
                gs.Buttons.X == ButtonState.Pressed || gs.Buttons.Y == ButtonState.Pressed ||
                gs.Buttons.LeftShoulder == ButtonState.Pressed || gs.Buttons.RightShoulder == ButtonState.Pressed ||
                gs.Buttons.Start == ButtonState.Pressed ||
                gs.DPad.Up == ButtonState.Pressed || gs.DPad.Down == ButtonState.Pressed ||
                gs.DPad.Left == ButtonState.Pressed || gs.DPad.Right == ButtonState.Pressed;
            if (used)
                _gamepadHideHud = true;
        }

        private static bool IsEscOnlyMenu(IClickableMenu menu)
        {
            if (menu == null) return false;
            if (menu is DialogueBox || menu is LetterViewerMenu || menu is BobberBar || menu is TitleMenu || menu is EmoteMenu)
                return false;
            // Show top-left ESC pill on ALL PC/Mod menus (GMCM, CJB, Lookup Anything, Forge, Tailoring, Museum, Carpenter, QuestLog, etc.)!
            return true;
        }

        private void ApplyLiveUiScaleAndWeather()
        {
            if (Game1.options != null)
            {
                if (_cfg.UiScaleOverride >= 0.75f)
                {
                    if (!_appliedUiScaleOverride)
                    {
                        _origDesiredUiScale = Game1.options.desiredUIScale > 0.1f ? Game1.options.desiredUIScale : 1.0f;
                        _appliedUiScaleOverride = true;
                    }
                    if (Math.Abs(Game1.options.desiredUIScale - _cfg.UiScaleOverride) > 0.01f)
                        Game1.options.desiredUIScale = _cfg.UiScaleOverride;
                }
                else if (_appliedUiScaleOverride)
                {
                    _appliedUiScaleOverride = false;
                    if (Math.Abs(Game1.options.desiredUIScale - _origDesiredUiScale) > 0.01f)
                        Game1.options.desiredUIScale = _origDesiredUiScale;
                }
                if (IsMobileModeActive && Game1.options.alwaysShowToolHitLocation != _cfg.ShowToolHitLocation)
                    Game1.options.alwaysShowToolHitLocation = _cfg.ShowToolHitLocation;
            }

            if (_cfg.ReduceWeather && ++_weatherTickCounter >= 30)
            {
                _weatherTickCounter = 0;
                try
                {
                    var dw = Game1.debrisWeather;
                    if (dw != null && dw.Count > 24)
                        dw.RemoveRange(24, dw.Count - 24);
                }
                catch { }
            }
        }

        private void SyncVanillaMouse(Vector2 vp, bool leftDown = false)
        {
            Point rawPt = ViewportToRaw(vp);
            int mx = rawPt.X;
            int my = rawPt.Y;
            try { Game1.setMousePositionRaw(mx, my); } catch { }
            try
            {
                var prev = Game1.oldMouseState;
                Game1.oldMouseState = new MouseState(
                    mx, my, prev.ScrollWheelValue,
                    leftDown ? ButtonState.Pressed : ButtonState.Released,
                    prev.MiddleButton, prev.RightButton, prev.XButton1, prev.XButton2);
            }
            catch { }
        }

        private static bool TryAutoPlaceCraftStation(IClickableMenu activeMenu, Point ui)
        {
            if (activeMenu is ForgeMenu fm && fm.inventory != null && fm.inventory.isWithinBounds(ui.X, ui.Y) && fm.heldItem == null)
            {
                try
                {
                    fm.receiveLeftClick(ui.X, ui.Y, true);
                    if (fm.heldItem != null)
                    {
                        if (fm.leftIngredientSpot != null && fm.leftIngredientSpot.item == null)
                            fm.receiveLeftClick(fm.leftIngredientSpot.bounds.Center.X, fm.leftIngredientSpot.bounds.Center.Y, true);
                        if (fm.heldItem != null && fm.rightIngredientSpot != null && fm.rightIngredientSpot.item == null)
                            fm.receiveLeftClick(fm.rightIngredientSpot.bounds.Center.X, fm.rightIngredientSpot.bounds.Center.Y, true);
                        if (fm.heldItem != null)
                            fm.receiveLeftClick(ui.X, ui.Y, true);
                    }
                    return true;
                }
                catch { }
            }
            if (activeMenu is TailoringMenu tm && tm.inventory != null && tm.inventory.isWithinBounds(ui.X, ui.Y) && tm.heldItem == null)
            {
                try
                {
                    tm.receiveLeftClick(ui.X, ui.Y, true);
                    if (tm.heldItem != null)
                    {
                        if (tm.leftIngredientSpot != null && tm.leftIngredientSpot.item == null)
                            tm.receiveLeftClick(tm.leftIngredientSpot.bounds.Center.X, tm.leftIngredientSpot.bounds.Center.Y, true);
                        if (tm.heldItem != null && tm.rightIngredientSpot != null && tm.rightIngredientSpot.item == null)
                            tm.receiveLeftClick(tm.rightIngredientSpot.bounds.Center.X, tm.rightIngredientSpot.bounds.Center.Y, true);
                        if (tm.heldItem != null)
                            tm.receiveLeftClick(ui.X, ui.Y, true);
                    }
                    return true;
                }
                catch { }
            }
            if (activeMenu is GeodeMenu gm && gm.inventory != null && gm.inventory.isWithinBounds(ui.X, ui.Y) && gm.heldItem == null && !gm.waitingForServerResponse && gm.geodeAnimationTimer <= 0)
            {
                try
                {
                    gm.receiveLeftClick(ui.X, ui.Y, true);
                    if (gm.heldItem != null && gm.geodeSpot != null)
                    {
                        gm.receiveLeftClick(gm.geodeSpot.bounds.Center.X, gm.geodeSpot.bounds.Center.Y, true);
                        if (gm.heldItem != null)
                            gm.receiveLeftClick(ui.X, ui.Y, true); // Return cleanly if bag full / <25g
                    }
                    return true;
                }
                catch { }
            }
            if (activeMenu is DyeMenu dm && dm.inventory != null && dm.inventory.isWithinBounds(ui.X, ui.Y) && dm.heldItem == null && !dm.IsBusy())
            {
                try
                {
                    dm.receiveLeftClick(ui.X, ui.Y, true);
                    if (dm.heldItem != null)
                    {
                        int potIdx = dm.GetPotIndex(dm.heldItem);
                        if (potIdx >= 0 && dm.dyePots != null && potIdx < dm.dyePots.Count)
                        {
                            var pot = dm.dyePots[potIdx];
                            dm.receiveLeftClick(pot.bounds.Center.X, pot.bounds.Center.Y, true);
                        }
                        if (dm.heldItem != null)
                            dm.receiveLeftClick(ui.X, ui.Y, true);
                    }
                    return true;
                }
                catch { }
            }
            if (activeMenu is StorageContainer sc && sc.heldItem == null)
            {
                bool inInv = sc.inventory != null && sc.inventory.isWithinBounds(ui.X, ui.Y);
                bool inGrab = sc.ItemsToGrabMenu != null && sc.ItemsToGrabMenu.isWithinBounds(ui.X, ui.Y);
                if (inInv || inGrab)
                {
                    var prevKb = Game1.oldKBState;
                    try
                    {
                        // Leverage StorageContainer's built-in Shift+LeftClick 1-tap auto-transfer (fires itemChangeBehavior cleanly for Fair Grange Display!)
                        Game1.oldKBState = new KeyboardState(Keys.LeftShift);
                        sc.receiveLeftClick(ui.X, ui.Y, true);
                        if (sc.heldItem != null)
                            sc.receiveLeftClick(ui.X, ui.Y, true);
                    }
                    catch { }
                    finally { Game1.oldKBState = prevKb; }
                    return true;
                }
            }
            if (activeMenu is FieldOfficeMenu fom && fom.inventory != null && fom.inventory.isWithinBounds(ui.X, ui.Y) && fom.heldItem == null)
            {
                try
                {
                    fom.receiveLeftClick(ui.X, ui.Y, true);
                    if (fom.heldItem != null)
                        fom.receiveLeftClick(ui.X, ui.Y, true); // Never stick non-fossil items on cursor
                    return true;
                }
                catch { }
            }
            return false;
        }

        private static void TryAutoDepositJunimoItem(JunimoNoteMenu jnm, Point ui)
        {
            if (jnm == null) return;
            var prevKb = Game1.oldKBState;
            try
            {
                jnm.performHoverAction(ui.X, ui.Y);
                // Native Shift+Click deposits directly into matching bundle slot in 1.6!
                Game1.oldKBState = new KeyboardState(Keys.LeftShift);
                jnm.receiveLeftClick(ui.X, ui.Y, true);
                Game1.oldKBState = prevKb;

                // Fallback: if still held on cursor, explicitly click each empty ingredientSlot
                if (jnm.heldItem != null && jnm.ingredientSlots != null)
                {
                    for (int i = 0; i < jnm.ingredientSlots.Count && jnm.heldItem != null; i++)
                    {
                        var slot = jnm.ingredientSlots[i];
                        if (slot != null && slot.item == null)
                            jnm.receiveLeftClick(slot.bounds.Center.X, slot.bounds.Center.Y, true);
                    }
                }
                // Return any remaining heldItem cleanly to original inventory slot (zero stuck cursor!)
                if (jnm.heldItem != null)
                    jnm.receiveLeftClick(ui.X, ui.Y, true);
                jnm.releaseLeftClick(ui.X, ui.Y);
            }
            catch { }
            finally { Game1.oldKBState = prevKb; }
        }

        private static ClickableComponent FindJunimoBundleSlotAt(JunimoNoteMenu jnm, Point ui)
        {
            if (jnm == null || !jnm.specificBundlePage || jnm.inventory?.inventory == null)
                return null;
            for (int i = 0; i < jnm.inventory.inventory.Count; i++)
            {
                var cc = jnm.inventory.inventory[i];
                if (cc != null && cc.containsPoint(ui.X, ui.Y))
                {
                    Item it = (i < jnm.inventory.actualInventory.Count) ? jnm.inventory.actualInventory[i] : null;
                    if (it != null && jnm.HighlightObjects(it))
                        return cc;
                }
            }
            return null;
        }

        private bool TryDirectAdjacentTileTap(Vector2 vp)
        {
            Farmer who = Game1.player;
            GameLocation loc = Game1.currentLocation;
            if (who == null || loc == null) return false;

            Vector2 worldPos = ViewportToWorld(vp);
            Point tappedTile = new Point((int)Math.Floor(worldPos.X / 64f), (int)Math.Floor(worldPos.Y / 64f));
            Point playerTile = who.TilePoint;
            int cheb = Math.Max(Math.Abs(playerTile.X - tappedTile.X), Math.Abs(playerTile.Y - tappedTile.Y));
            if (cheb > 1) return false;

            TileIntent intent = AutoToolAndCombat.InspectTile(loc, tappedTile, who, _cfg.AutoTool);
            if (intent.Kind != TileActionKind.None)
            {
                TriggerTileIntent(who, tappedTile, intent);
                return true;
            }

            // Empty adjacent/diagonal tile: face exact tile and trigger Interact/Place or UseTool!
            Vector2 targetCenter = new Vector2(tappedTile.X * 64f + 32f, tappedTile.Y * 64f + 32f);
            who.faceGeneralDirection(targetCenter);
            if (who.ActiveObject != null)
                _actionPulseAFrames = 2;
            else
                _actionPulseXFrames = 2;
            return true;
        }

        private static bool IsTouchOnOptionsSliderOrBar(List<ClickableComponent> slots, List<OptionsElement> opts, int curIdx, int menuRightX, Point uiPt)
        {
            // Right-edge scrollbar drag strip
            if (uiPt.X >= menuRightX - 76)
                return true;
            if (slots == null || opts == null) return false;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && slots[i].containsPoint(uiPt.X, uiPt.Y))
                {
                    int oi = curIdx + i;
                    if (oi >= 0 && oi < opts.Count && opts[oi] is OptionsSlider)
                        return true;
                    break;
                }
            }
            return false;
        }

        private static bool IsImmediateDragMenu(IClickableMenu menu, Point uiPt)
        {
            if (menu is GameMenu gm)
            {
                try
                {
                    IClickableMenu page = gm.GetCurrentPage();
                    if (page is OptionsPage op)
                        return IsTouchOnOptionsSliderOrBar(op.optionSlots, op.options, op.currentItemIndex, op.xPositionOnScreen + op.width, uiPt);
                    if (page is SocialPage sp)
                        return (sp.scrollBar != null && sp.scrollBar.containsPoint(uiPt.X, uiPt.Y)) || sp.scrollBarRunner.Contains(uiPt);
                    if (page is AnimalPage ap)
                        return (ap.scrollBar != null && ap.scrollBar.containsPoint(uiPt.X, uiPt.Y)) || ap.scrollBarRunner.Contains(uiPt);
                }
                catch { }
            }
            if (menu is AdvancedGameOptions ago)
            {
                try
                {
                    return IsTouchOnOptionsSliderOrBar(ago.optionSlots, ago.options, ago.currentItemIndex, ago.xPositionOnScreen + ago.width, uiPt);
                }
                catch { }
            }
            if (menu is CharacterCustomization cc)
            {
                try
                {
                    if ((cc.hairColorPicker != null && cc.hairColorPicker.containsPoint(uiPt.X, uiPt.Y)) ||
                        (cc.eyeColorPicker != null && cc.eyeColorPicker.containsPoint(uiPt.X, uiPt.Y)) ||
                        (cc.pantsColorPicker != null && cc.pantsColorPicker.containsPoint(uiPt.X, uiPt.Y)))
                        return true;
                }
                catch { }
            }
            if (menu is BuildingPaintMenu bpm)
            {
                try
                {
                    if (bpm.activeSlider != null) return true;
                    if (bpm.colorSliderPanel != null && bpm.colorSliderPanel.rectangle.Contains(uiPt))
                        return true;
                }
                catch { }
            }
            if (menu is ShopMenu sm)
            {
                try
                {
                    if (sm.scrollBar != null && sm.scrollBar.containsPoint(uiPt.X, uiPt.Y))
                        return true;
                }
                catch { }
            }
            if (menu is LoadGameMenu lgm)
            {
                try
                {
                    if (lgm.scrollBar != null && lgm.scrollBar.containsPoint(uiPt.X, uiPt.Y))
                        return true;
                }
                catch { }
            }
            if (menu is QuestLog ql)
            {
                try
                {
                    if (ql.scrollBarBounds.Contains(uiPt))
                        return true;
                }
                catch { }
            }
            return false;
        }

        public bool UpdateInMenu(object pad, GameTime gameTime)
        {
            _cfg.PollReload();
            ApplyLiveUiScaleAndWeather();
            _editMode = false;
            _keyPickerOpen = false;
            _saveMenuOpen = false;
            _wasInMenu = true;
            ResetGameplayState();
            TickCustomButtonPulses();

            if (!IsMobileModeActive)
            {
                ResetMenuTapState();
                ClearCustomButtonTouches();
                _invShop.Reset();
                _lastMenuMouseRaw = null;
                _simMouseLeftPulseFrames = 0;
                _simMouseRightPulseFrames = 0;
                return false;
            }

            var br = EnsureBridge(pad);
            br?.CloseHotkeyDrawer(pad);
            if (br != null && (br.IsFullScreenModalOpen(pad) || br.IsQuickSaveReloading()))
            {
                ResetMenuTapState();
                return false;
            }

            IClickableMenu rawMenu = Game1.activeClickableMenu;

            // Optional crisp SKIP pixel button during skippable PC cutscenes (Game1.eventUp && activeClickableMenu == null)
            if (rawMenu == null)
            {
                ResetMenuTapState();
                _ctxTouchId = -1;
                _invShop.Reset();
                _lastMenuMouseRaw = null;
                _hasCarpenterPreview = false;

                if (IsSkippableCutsceneActive() && RefreshViewport())
                {
                    ComputeLayout();
                    TouchCollection evTouches = TouchPanel.GetState();
                    Point? evMouse = null;
                    bool evLeft = false;
                    for (int i = 0; i < evTouches.Count; i++)
                    {
                        TouchLocation tl = evTouches[i];
                        Vector2 vp = MapRawToViewport(tl.Position);
                        Point pt = new Point((int)vp.X, (int)vp.Y);
                        if (tl.State == TouchLocationState.Pressed && _btnSkipEventRect.Contains(pt))
                        {
                            try { Game1.CurrentEvent?.skipEvent(); } catch { }
                            try { Game1.playSound("bigDeSelect"); } catch { }
                            ApplyToPad(pad, Vector2.Zero, false, false, false);
                            return true;
                        }
                        if (tl.State == TouchLocationState.Pressed || tl.State == TouchLocationState.Moved)
                        {
                            evMouse = ViewportToRaw(vp);
                            evLeft = true;
                        }
                    }
                    EnsureBridge(pad)?.ApplyInputWithMouse(pad, Vector2.Zero, false, false, false, evMouse, evLeft, false, _rawW, _rawH);
                    return true;
                }
                return false;
            }

            // CRITICAL: Return BEFORE calling TouchPanel.GetState() for BobberBar & vanilla TitleMenu screens!
            // Exception: If a 3rd-party Mod Menu (e.g. GenericModConfigMenu!) is opened as TitleMenu.subMenu, handle it cleanly!
            IClickableMenu topMenu = GetTopmostMenu(rawMenu);
            bool isThirdParty = IsThirdPartyModMenu(topMenu);
            if (rawMenu is BobberBar || (rawMenu is TitleMenu && !isThirdParty))
            {
                ResetMenuTapState();
                _invShop.Reset();
                _lastMenuMouseRaw = null;
                _hasCarpenterPreview = false;
                return false;
            }

            if (!RefreshViewport())
                return false;
            ComputeLayout();

            bool showInvCtx = IsInventoryContextMenu(topMenu);
            bool showKeyOnly = !showInvCtx && IsTextInputMenu(topMenu);
            bool showEscOnly = !showInvCtx && !showKeyOnly && IsEscOnlyMenu(topMenu);
            bool allowCustomKeys = AllowCustomKeysInCurrentMenu() && !isThirdParty && _cfg.CustomKeys.Count > 0;

            // Single TouchPanel.GetState() read per frame — from here on, ALWAYS return true!
            TouchCollection touches = TouchPanel.GetState();
            double nowSec = gameTime != null ? gameTime.TotalGameTime.TotalSeconds : Environment.TickCount64 / 1000.0;
            PollAutoKeyboard(pad, br, nowSec);

            if (!ReferenceEquals(_junimoHoverMenu, topMenu as JunimoNoteMenu))
            {
                _junimoHoverMenu = null;
                _junimoHoveredSlot = null;
            }
            if (!ReferenceEquals(_dlgHoverMenu, topMenu as DialogueBox))
            {
                _dlgHoverMenu = null;
                _dlgHoveredIdx = -1;
            }
            if (!ReferenceEquals(_carpenterPreviewMenu, topMenu as CarpenterMenu) || (topMenu is CarpenterMenu cmChk && (!cmChk.onFarm || cmChk.freeze)))
            {
                _carpenterPreviewMenu = null;
                _hasCarpenterPreview = false;
            }

            // Always consume lingering tap release from BAG/MAP/QST/Emote or HUD Quest button FIRST!
            if (_ctxTouchId >= 0)
            {
                bool sawLingeringTap = false;
                for (int i = 0; i < touches.Count; i++)
                {
                    if (touches[i].Id == _ctxTouchId)
                    {
                        if (touches[i].State == TouchLocationState.Pressed || touches[i].State == TouchLocationState.Moved)
                            sawLingeringTap = true;
                        break;
                    }
                }
                if (sawLingeringTap)
                {
                    ApplyToPad(pad, Vector2.Zero, false, false, false);
                    return true;
                }
                _ctxTouchId = -1;
            }

            // Native EmoteMenu radial wheel: AFTER lingering tap filter, and uses UI coordinates (ViewportToUi -> ModifyCoordinateFromUIScale)!
            if (topMenu is EmoteMenu em)
            {
                em.gamepadMode = false;
                for (int i = 0; i < touches.Count; i++)
                {
                    TouchLocation tl = touches[i];
                    Vector2 vp = MapRawToViewport(tl.Position);
                    Point ui = ViewportToUi(vp);
                    SyncVanillaMouse(vp);
                    if (tl.State == TouchLocationState.Pressed || tl.State == TouchLocationState.Moved)
                    {
                        try { em.performHoverAction(ui.X, ui.Y); } catch { }
                    }
                    else if (tl.State == TouchLocationState.Released)
                    {
                        try { em.performHoverAction(ui.X, ui.Y); } catch { }
                        try { em.receiveLeftClick(ui.X, ui.Y, true); } catch { }
                    }
                }
                ApplyToPad(pad, Vector2.Zero, false, false, false);
                return true;
            }

            // If Mobile Buy/Sell/Split/Crafting Popup is open OR dragging an item, give it top priority!
            if (_invShop.IsPopupOpen || (_cfg.MobileShopAndDrag && _invShop.IsDraggingItem))
            {
                if (_invShop.Update(topMenu, touches, MapRawToViewport, ViewportToUi, _vpW, _vpH, nowSec))
                {
                    ResetMenuTapState();
                    ApplyToPad(pad, Vector2.Zero, false, false, false);
                    return true;
                }
            }

            // Handle Custom PC Key Buttons in Inventory / Vanilla Menus
            if (allowCustomKeys)
            {
                bool customConsumed = ProcessCustomKeyTouches(pad, br, touches, nowSec);
                if (customConsumed)
                {
                    ResetMenuTapState();
                    ApplyToPad(pad, Vector2.Zero, false, false, false);
                    return true;
                }
            }
            else
            {
                ClearCustomButtonTouches();
            }

            // 1. Handle taps on Context Helper Bar (Inventory X1/1/2/XFER/KEY/ESC, TextInput KEY, Mod/Other ESC-only)
            if ((showInvCtx || showKeyOnly || showEscOnly) && touches.Count > 0)
            {
                bool sawCtx = false;
                for (int i = 0; i < touches.Count; i++)
                {
                    TouchLocation tl = touches[i];
                    Vector2 vp = MapRawToViewport(tl.Position);
                    Point pt = new Point((int)vp.X, (int)vp.Y);
                    bool active = tl.State == TouchLocationState.Pressed || tl.State == TouchLocationState.Moved;

                    if (tl.Id == _ctxTouchId)
                    {
                        if (active) sawCtx = true;
                        continue;
                    }

                    if (tl.State == TouchLocationState.Pressed)
                    {
                        if (showInvCtx)
                        {
                            if (_ctxX1Rect.Contains(pt)) { br?.ToggleSingleItemMode(pad); _ctxTouchId = tl.Id; sawCtx = true; try { Game1.playSound("smallSelect"); } catch { } continue; }
                            if (_ctxHalfRect.Contains(pt)) { br?.ToggleHalfStackMode(pad); _ctxTouchId = tl.Id; sawCtx = true; try { Game1.playSound("smallSelect"); } catch { } continue; }
                            if (_ctxXferRect.Contains(pt)) { br?.ToggleQuickTransferMode(pad); _ctxTouchId = tl.Id; sawCtx = true; try { Game1.playSound("smallSelect"); } catch { } continue; }
                            if (_ctxKeyRect.Contains(pt)) { br?.TriggerVirtualKeyboard(pad); _ctxTouchId = tl.Id; sawCtx = true; continue; }
                            if (_ctxEscRect.Contains(pt)) { _invShop.Reset(); CloseTopmostMenu(rawMenu, topMenu); _ctxTouchId = tl.Id; sawCtx = true; continue; }
                        }
                        else if (showKeyOnly)
                        {
                            if (_ctxX1Rect.Contains(pt))
                            {
                                br?.TriggerVirtualKeyboard(pad);
                                _ctxTouchId = tl.Id;
                                sawCtx = true;
                                continue;
                            }
                            if (_ctxHalfRect.Contains(pt) && topMenu.readyToClose())
                            {
                                CloseTopmostMenu(rawMenu, topMenu);
                                _ctxTouchId = tl.Id;
                                sawCtx = true;
                                continue;
                            }
                        }
                        else if (showEscOnly && _ctxQuestEscRect.Contains(pt))
                        {
                            CloseTopmostMenu(rawMenu, topMenu);
                            _ctxTouchId = tl.Id;
                            sawCtx = true;
                            continue;
                        }
                    }
                }
                if (!sawCtx) _ctxTouchId = -1;
                if (sawCtx)
                {
                    ResetMenuTapState();
                    _simMouseLeftPulseFrames = 0;
                    _simMouseRightPulseFrames = 0;
                    ApplyToPad(pad, Vector2.Zero, false, false, false);
                    return true;
                }
            }

            // 1b. UNIVERSAL 3RD-PARTY MOD MENU ENGINE (GenericModConfigMenu, SpaceCore, CJB, Lookup Anything, Chests Anywhere, BetterGameMenu, StardewUI, etc.)
            // Drives ALL mod menus via TRUE Simulated Mouse (Mouse.GetState / InputState / SMAPI ButtonPressed) + Horizontal Slider Drag + Vertical Swipe Scroll + Long-Press Right-Click!
            if (isThirdParty)
            {
                _invShop.Reset();
                UpdateThirdPartyModMenu(pad, topMenu, touches, nowSec);
                return true;
            }

            // 2. Mobile-style Shop (Buy/Sell/Dresser) & Inventory/Chest Drag-and-Drop / Tap-to-Transfer / Long-Press Split
            bool pcSplitActive = br != null && (br.GetSingleItemMode(pad) || br.GetHalfStackMode(pad) || br.GetQuickTransferMode(pad));
            if (_cfg.MobileShopAndDrag && !pcSplitActive)
            {
                if (_invShop.Update(topMenu, touches, MapRawToViewport, ViewportToUi, _vpW, _vpH, nowSec))
                {
                    ResetMenuTapState();
                    ApplyToPad(pad, Vector2.Zero, false, false, false);
                    return true;
                }
            }
            else if (!_invShop.IsPopupOpen)
            {
                _invShop.Reset();
            }

            // 3. Universal Direct Vanilla Menu Touch Handler with Global Mouse Sync!
            if (touches.Count == 0)
            {
                ResetMenuTapState();
                // Keep simulated mouse position at last tapped point in RAW BackBuffer pixels (LeftDown=false) so Carpenter/Museum/Tooltips stay aligned!
                EnsureBridge(pad)?.ApplyInputWithMouse(pad, Vector2.Zero, false, false, false, _lastMenuMouseRaw, false, false, _rawW, _rawH);
                return true;
            }

            for (int i = 0; i < touches.Count; i++)
            {
                TouchLocation tl = touches[i];
                Vector2 vp = MapRawToViewport(tl.Position);
                Point ui = ViewportToUi(vp);
                _lastMenuMouseRaw = ViewportToRaw(vp);

                if (tl.State == TouchLocationState.Pressed && _menuTapTouchId < 0)
                {
                    _menuTapTouchId = tl.Id;
                    _menuTapStartVp = vp;
                    _menuTapLastVp = vp;
                    _menuTapLastY = vp.Y;
                    _menuTapAccumY = 0f;
                    _menuTapDragged = false;
                    _menuTapRightClicked = false;
                    _menuTapStartSec = nowSec;
                    _menuTapImmediateDrag = IsImmediateDragMenu(topMenu, ui);
                    _menuTapCameraPan = !_menuTapImmediateDrag && IsCameraPanMenu(topMenu, ui);

                    SyncVanillaMouse(vp, _menuTapImmediateDrag);
                    try { topMenu.performHoverAction(ui.X, ui.Y); } catch { }
                    if (_menuTapImmediateDrag)
                    {
                        try { topMenu.receiveLeftClick(ui.X, ui.Y, true); } catch { }
                    }
                }
                else if (tl.State == TouchLocationState.Moved && tl.Id == _menuTapTouchId)
                {
                    if (_menuTapRightClicked)
                        continue;

                    if (_menuTapImmediateDrag)
                    {
                        SyncVanillaMouse(vp, true);
                        try { topMenu.leftClickHeld(ui.X, ui.Y); } catch { }
                    }
                    else if (_menuTapCameraPan)
                    {
                        Vector2 delta = vp - _menuTapLastVp;
                        _menuTapLastVp = vp;
                        if (!_menuTapDragged && Vector2.Distance(vp, _menuTapStartVp) >= 16f)
                        {
                            _menuTapDragged = true;
                            _hasCarpenterPreview = false;
                        }
                        if (_menuTapDragged)
                        {
                            int panX = (int)Math.Round(-delta.X * Game1.viewport.Width / Math.Max(1f, _vpW));
                            int panY = (int)Math.Round(-delta.Y * Game1.viewport.Height / Math.Max(1f, _vpH));
                            if (panX != 0 || panY != 0)
                            {
                                try { Game1.panScreen(panX, panY); } catch { }
                            }
                            SyncVanillaMouse(vp, false);
                            try { topMenu.performHoverAction(ui.X, ui.Y); } catch { }
                        }
                    }
                    else
                    {
                        float dy = vp.Y - _menuTapLastY;
                        _menuTapLastY = vp.Y;
                        float totalDist = Vector2.Distance(vp, _menuTapStartVp);
                        float totalDy = Math.Abs(vp.Y - _menuTapStartVp.Y);
                        float dragThresh = Math.Max(22f, _vpH * 0.026f);
                        if (!_menuTapDragged && totalDy >= dragThresh)
                        {
                            _menuTapDragged = true;
                            _menuTapAccumY = vp.Y - _menuTapStartVp.Y;
                        }
                        if (_menuTapDragged)
                        {
                            _menuTapAccumY += dy;
                            float step = Math.Max(26f, _vpH * 0.045f);
                            while (Math.Abs(_menuTapAccumY) >= step)
                            {
                                int wheel = _menuTapAccumY > 0 ? 120 : -120;
                                _menuTapAccumY -= Math.Sign(_menuTapAccumY) * step;
                                try { topMenu.receiveScrollWheelAction(wheel); } catch { }
                            }
                        }
                        else if (totalDist < 16f && (nowSec - _menuTapStartSec) >= 0.45 && CanLongPressRightClick(topMenu))
                        {
                            _menuTapRightClicked = true;
                            SyncVanillaMouse(vp, false);
                            try { topMenu.performHoverAction(ui.X, ui.Y); } catch { }
                            try { topMenu.receiveRightClick(ui.X, ui.Y, true); } catch { }
                            try { Game1.playSound("smallSelect"); } catch { }
                        }
                        else
                        {
                            SyncVanillaMouse(vp, false);
                            try { topMenu.performHoverAction(ui.X, ui.Y); } catch { }
                        }
                    }
                }
                else if ((tl.State == TouchLocationState.Released || tl.State == TouchLocationState.Invalid) && tl.Id == _menuTapTouchId)
                {
                    SyncVanillaMouse(vp, false);
                    if (_menuTapRightClicked)
                    {
                        ResetMenuTapState();
                        continue;
                    }
                    if (_menuTapImmediateDrag)
                    {
                        try { topMenu.releaseLeftClick(ui.X, ui.Y); } catch { }
                    }
                    else if (!_menuTapDragged)
                    {
                        CraftingPage cp = _cfg.SafeCrafting ? GetActiveCraftingPage(topMenu) : null;
                        CraftingRecipe hitCraftRecipe = null;
                        ClickableTextureComponent hitRecipe = cp != null ? FindCraftingRecipeAt(cp, ui, out hitCraftRecipe) : null;
                        JunimoNoteMenu jnm = topMenu as JunimoNoteMenu;
                        ClickableComponent hitJunimo = jnm != null ? FindJunimoBundleSlotAt(jnm, ui) : null;

                        if (cp != null && hitRecipe != null && hitCraftRecipe != null)
                        {
                            // FIXED: 1st tap NEVER crafts immediately — opens Authentic Stardew Mobile Crafting Sheet with ingredients & quantity slider!
                            _invShop.OpenCraftingPopup(topMenu, cp, hitRecipe, hitCraftRecipe, tl.Id);
                        }
                        else if (jnm != null && hitJunimo != null)
                        {
                            if (_cfg.SafeCrafting && (!ReferenceEquals(hitJunimo, _junimoHoveredSlot) || !ReferenceEquals(jnm, _junimoHoverMenu)))
                            {
                                // 1st tap on Community Center bundle item: preview before depositing!
                                _junimoHoverMenu = jnm;
                                _junimoHoveredSlot = hitJunimo;
                                try { jnm.performHoverAction(ui.X, ui.Y); } catch { }
                                try { Game1.playSound("smallSelect"); } catch { }
                            }
                            else
                            {
                                // FIXED: REAL 1-tap auto-deposit straight into matching Community Center bundle slot (zero stuck heldItem!)
                                _junimoHoverMenu = null;
                                _junimoHoveredSlot = null;
                                TryAutoDepositJunimoItem(jnm, ui);
                            }
                        }
                        else if (topMenu is CarpenterMenu cmFarm && cmFarm.onFarm && !cmFarm.freeze && _menuTapCameraPan)
                        {
                            // Safe 2-Tap Preview on Robin/Wizard building placement/move/demolish:
                            // 1st tap shows exact Green/Red blueprint at finger; 2nd tap nearby confirms!
                            if (!_hasCarpenterPreview || !ReferenceEquals(_carpenterPreviewMenu, cmFarm) || Vector2.Distance(vp, _carpenterPreviewVp) > 108f)
                            {
                                _carpenterPreviewMenu = cmFarm;
                                _carpenterPreviewVp = vp;
                                _hasCarpenterPreview = true;
                                try { cmFarm.performHoverAction(ui.X, ui.Y); } catch { }
                                try { Game1.playSound("smallSelect"); } catch { }
                            }
                            else
                            {
                                _hasCarpenterPreview = false;
                                try { cmFarm.performHoverAction(ui.X, ui.Y); } catch { }
                                try { cmFarm.receiveLeftClick(ui.X, ui.Y, true); } catch { }
                                try { cmFarm.releaseLeftClick(ui.X, ui.Y); } catch { }
                            }
                        }
                        else if (TryAutoPlaceCraftStation(topMenu, ui))
                        {
                            _junimoHoverMenu = null;
                            _junimoHoveredSlot = null;
                        }
                        else if (topMenu is MineElevatorMenu mem && mem.elevators != null && TryClickExpandedElevator(mem, ui))
                        {
                            // Expanded touch target for Mine Elevator floor buttons!
                        }
                        else if (_cfg.SafeDialogueAndTooltip && topMenu is DialogueBox db && db.isQuestion && !db.transitioning && db.safetyTimer <= 0 && db.responseCC != null && db.responseCC.Count > 0 && TrySafeDialogueResponseTap(db, ui))
                        {
                            // 1st tap highlights choice, 2nd tap confirms cleanly (prevents Heart Event mis-taps!)
                        }
                        else
                        {
                            _junimoHoverMenu = null;
                            _junimoHoveredSlot = null;
                            _dlgHoverMenu = null;
                            _dlgHoveredIdx = -1;
                            _hasCarpenterPreview = false;
                            try { topMenu.performHoverAction(ui.X, ui.Y); } catch { }
                            try { topMenu.receiveLeftClick(ui.X, ui.Y, true); } catch { }
                            try { topMenu.releaseLeftClick(ui.X, ui.Y); } catch { }
                        }
                    }
                    ResetMenuTapState();
                }
            }

            // Keep SimulatedMousePosition synced in RAW BackBuffer pixels (LeftDown=false so Game1.updateActiveMenu doesn't double-click vanilla menus!)
            EnsureBridge(pad)?.ApplyInputWithMouse(pad, Vector2.Zero, false, false, false, _lastMenuMouseRaw, false, false, _rawW, _rawH);
            return true;
        }

        private static void CloseTopmostMenu(IClickableMenu rawMenu, IClickableMenu topMenu)
        {
            try { Game1.playSound("bigDeSelect"); } catch { }
            try
            {
                if (topMenu != null && !ReferenceEquals(topMenu, rawMenu))
                {
                    topMenu.receiveKeyPress(Keys.Escape);
                    if (ReferenceEquals(GetTopmostMenu(Game1.activeClickableMenu), topMenu) && topMenu.readyToClose())
                        topMenu.exitThisMenu(false);
                    return;
                }
                if (IsThirdPartyModMenu(topMenu))
                {
                    topMenu.receiveKeyPress(Keys.Escape);
                    if (ReferenceEquals(Game1.activeClickableMenu, topMenu) && topMenu.readyToClose())
                        topMenu.exitThisMenu(false);
                    return;
                }
                Game1.exitActiveMenu();
            }
            catch
            {
                try { Game1.exitActiveMenu(); } catch { }
            }
        }

        /// <summary>
        /// Universal touch-to-mouse bridge for ALL 3rd-party SMAPI Mod UIs (GenericModConfigMenu / SpaceShared.UI,
        /// SpaceCore, CJB Cheats/ItemSpawner, Lookup Anything, Chests Anywhere, Better Game Menu, StardewUI, etc.).
        /// Drives TouchVirtualPad.SimulatedMousePosition in EXACT RAW BackBuffer pixels (matching Game1.getMouseX = X / uiScale)
        /// and distinguishes Tap (1-frame MouseLeft pulse), Horizontal Slider/Scrollbar Drag (held MouseLeft),
        /// Vertical Swipe Scroll (receiveScrollWheelAction + SpaceShared.UI.Dropdown scroll), and Long-Press Right-Click!
        /// </summary>
        private void UpdateThirdPartyModMenu(object pad, IClickableMenu modMenu, TouchCollection touches, double nowSec)
        {
            Point? outPos = _lastMenuMouseRaw;
            bool outLeft = false;
            bool outRight = false;

            // 1. Drain any queued 1-frame tap click pulse cleanly
            if (_simMouseLeftPulseFrames > 0)
            {
                _simMouseLeftPulseFrames--;
                outPos = _simMousePulsePos ?? _lastMenuMouseRaw;
                outLeft = true;
            }
            else if (_simMouseRightPulseFrames > 0)
            {
                _simMouseRightPulseFrames--;
                outPos = _simMousePulsePos ?? _lastMenuMouseRaw;
                outRight = true;
            }

            bool sawTracked = false;
            for (int i = 0; i < touches.Count; i++)
            {
                TouchLocation tl = touches[i];
                Vector2 vp = MapRawToViewport(tl.Position);
                Point rawPt = ViewportToRaw(vp);

                if (tl.State == TouchLocationState.Pressed && _menuTapTouchId < 0)
                {
                    _menuTapTouchId = tl.Id;
                    _menuTapStartVp = vp;
                    _menuTapLastVp = vp;
                    _menuTapLastY = vp.Y;
                    _menuTapAccumY = 0f;
                    _menuTapStartSec = nowSec;
                    _modTouchState = ModTouchState.Pending;
                    _lastMenuMouseRaw = rawPt;
                    outPos = rawPt;
                    sawTracked = true;
                }
                else if (tl.State == TouchLocationState.Moved && tl.Id == _menuTapTouchId)
                {
                    sawTracked = true;
                    _lastMenuMouseRaw = rawPt;
                    float dxTotal = Math.Abs(vp.X - _menuTapStartVp.X);
                    float dyTotal = Math.Abs(vp.Y - _menuTapStartVp.Y);
                    float distTotal = Vector2.Distance(vp, _menuTapStartVp);
                    float dy = vp.Y - _menuTapLastY;
                    _menuTapLastY = vp.Y;

                    if (_modTouchState == ModTouchState.Pending)
                    {
                        bool onRightScrollbar = _menuTapStartVp.X >= _vpW * 0.80f && dyTotal >= 10f;
                        bool isHorizSlider = dxTotal >= 12f && dxTotal > dyTotal * 1.15f;
                        if (isHorizSlider || onRightScrollbar)
                        {
                            // Grab slider/scrollbar at exact start point first!
                            _modTouchState = ModTouchState.MouseDrag;
                            Point startRaw = ViewportToRaw(_menuTapStartVp);
                            outPos = startRaw;
                            outLeft = true;
                        }
                        else if (dyTotal >= 22f && dyTotal >= dxTotal)
                        {
                            _modTouchState = ModTouchState.SwipeScroll;
                            _menuTapAccumY = vp.Y - _menuTapStartVp.Y;
                            outPos = rawPt;
                            outLeft = false;
                        }
                        else if (distTotal < 16f && (nowSec - _menuTapStartSec) >= 0.45)
                        {
                            _modTouchState = ModTouchState.RightClicked;
                            outPos = rawPt;
                            outRight = true;
                            try { Game1.playSound("smallSelect"); } catch { }
                        }
                        else
                        {
                            outPos = rawPt;
                            outLeft = false;
                        }
                    }
                    else if (_modTouchState == ModTouchState.MouseDrag)
                    {
                        outPos = rawPt;
                        outLeft = true;
                    }
                    else if (_modTouchState == ModTouchState.SwipeScroll)
                    {
                        _menuTapAccumY += dy;
                        float step = Math.Max(22f, _vpH * 0.036f);
                        while (Math.Abs(_menuTapAccumY) >= step)
                        {
                            int wheel = _menuTapAccumY > 0 ? 120 : -120;
                            _menuTapAccumY -= Math.Sign(_menuTapAccumY) * step;
                            if (!TryScrollActiveModDropdown(modMenu, wheel))
                            {
                                try { modMenu.receiveScrollWheelAction(wheel); } catch { }
                            }
                        }
                        outPos = rawPt;
                        outLeft = false;
                    }
                }
                else if ((tl.State == TouchLocationState.Released || tl.State == TouchLocationState.Invalid) && tl.Id == _menuTapTouchId)
                {
                    sawTracked = true;
                    if (_modTouchState == ModTouchState.Pending)
                    {
                        // Clean Tap -> 1-frame TRUE MouseLeft press at start point in RAW BackBuffer pixels!
                        // Triggers SpaceShared.UI.Element.Clicked (GMCM/SpaceCore), Game1.updateActiveMenu -> receiveLeftClick, AND SMAPI ButtonPressed!
                        Point clickRaw = ViewportToRaw(_menuTapStartVp);
                        _lastMenuMouseRaw = clickRaw;
                        _simMousePulsePos = clickRaw;
                        _simMouseLeftPulseFrames = 0; // Apply THIS frame directly!
                        outPos = clickRaw;
                        outLeft = true;
                    }
                    else
                    {
                        outPos = rawPt;
                        outLeft = false;
                    }
                    ResetMenuTapState();
                }
            }

            if (!sawTracked && _menuTapTouchId >= 0)
            {
                // Handle ultra-fast tap on 30 FPS mode where iOS drops the Released state between frames
                if (_modTouchState == ModTouchState.Pending && (nowSec - _menuTapStartSec) < 0.35)
                {
                    Point clickRaw = ViewportToRaw(_menuTapStartVp);
                    _lastMenuMouseRaw = clickRaw;
                    outPos = clickRaw;
                    outLeft = true;
                }
                ResetMenuTapState();
            }

            if (outPos.HasValue)
            {
                // Inform Stardew & SMAPI mods that cursor is mouse-driven (so GMCM/SpaceCore/LookupAnything never snap back to gamepad!)
                try { Game1.lastCursorMotionWasMouse = true; } catch { }
            }
            EnsureBridge(pad)?.ApplyInputWithMouse(pad, Vector2.Zero, false, false, false, outPos, outLeft, outRight, _rawW, _rawH);
        }

        private static bool TryScrollActiveModDropdown(IClickableMenu modMenu, int wheel)
        {
            if (modMenu == null) return false;
            try
            {
                var ddType = modMenu.GetType().Assembly.GetType("SpaceShared.UI.Dropdown", false);
                if (ddType == null) return false;
                const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                object active = ddType.GetField("ActiveDropdown", stat)?.GetValue(null)
                             ?? ddType.GetProperty("ActiveDropdown", stat)?.GetValue(null);
                if (active == null) return false;
                var m = ddType.GetMethod("ReceiveScrollWheelAction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (m != null)
                {
                    m.Invoke(active, new object[] { wheel });
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool CanLongPressRightClick(IClickableMenu menu)
        {
            if (menu == null) return false;
            if (menu is DialogueBox || menu is LetterViewerMenu || menu is BobberBar || menu is TitleMenu || menu is EmoteMenu || menu is MineElevatorMenu)
                return false;
            if (menu is GameMenu gm)
            {
                try { if (gm.GetCurrentPage() is OptionsPage) return false; } catch { }
            }
            return true;
        }

        private static bool TryClickExpandedElevator(MineElevatorMenu mem, Point ui)
        {
            ClickableComponent best = null;
            float bestDistSq = float.MaxValue;
            const int pad = 16;
            for (int i = 0; i < mem.elevators.Count; i++)
            {
                var cc = mem.elevators[i];
                if (cc == null) continue;
                var expanded = new Rectangle(cc.bounds.X - pad, cc.bounds.Y - pad, cc.bounds.Width + pad * 2, cc.bounds.Height + pad * 2);
                if (expanded.Contains(ui))
                {
                    float dx = cc.bounds.Center.X - ui.X;
                    float dy = cc.bounds.Center.Y - ui.Y;
                    float dSq = dx * dx + dy * dy;
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        best = cc;
                    }
                }
            }
            if (best == null) return false;
            try { mem.performHoverAction(best.bounds.Center.X, best.bounds.Center.Y); } catch { }
            try { mem.receiveLeftClick(best.bounds.Center.X, best.bounds.Center.Y, true); } catch { }
            return true;
        }

        private bool TrySafeDialogueResponseTap(DialogueBox db, Point ui)
        {
            int hitIdx = -1;
            for (int i = 0; i < db.responseCC.Count; i++)
            {
                var cc = db.responseCC[i];
                if (cc != null && cc.bounds.Contains(ui))
                {
                    hitIdx = i;
                    break;
                }
            }
            if (hitIdx < 0)
            {
                _dlgHoverMenu = null;
                _dlgHoveredIdx = -1;
                return false;
            }

            if (!ReferenceEquals(_dlgHoverMenu, db) || _dlgHoveredIdx != hitIdx)
            {
                _dlgHoverMenu = db;
                _dlgHoveredIdx = hitIdx;
                db.selectedResponse = hitIdx;
                try { db.performHoverAction(ui.X, ui.Y); } catch { }
                try { Game1.playSound("smallSelect"); } catch { }
                return true;
            }

            // 2nd tap on the same highlighted response -> confirm!
            _dlgHoverMenu = null;
            _dlgHoveredIdx = -1;
            db.selectedResponse = hitIdx;
            try { db.performHoverAction(ui.X, ui.Y); } catch { }
            try { db.receiveLeftClick(ui.X, ui.Y, true); } catch { }
            try { db.releaseLeftClick(ui.X, ui.Y); } catch { }
            return true;
        }

        private bool ProcessCustomKeyTouches(object pad, PadBridge br, TouchCollection touches, double nowSec)
        {
            _customBtnsHeldNow.Clear();
            bool anyCustomTouched = false;

            // Keep existing active custom-button touches
            for (int i = 0; i < touches.Count; i++)
            {
                TouchLocation tl = touches[i];
                bool active = tl.State == TouchLocationState.Pressed || tl.State == TouchLocationState.Moved;
                Vector2 vp = MapRawToViewport(tl.Position);
                Point pt = new Point((int)vp.X, (int)vp.Y);

                // Check if this touch already owns a custom button
                int ownedBtnId = -1;
                foreach (var kv in _customBtnTouchIds)
                {
                    if (kv.Value == tl.Id) { ownedBtnId = kv.Key; break; }
                }

                if (ownedBtnId >= 0)
                {
                    if (active)
                    {
                        _customBtnsHeldNow.Add(ownedBtnId);
                        anyCustomTouched = true;
                    }
                    else
                    {
                        _customBtnTouchIds.Remove(ownedBtnId);
                    }
                    continue;
                }

                if (tl.State == TouchLocationState.Pressed)
                {
                    for (int k = _cfg.CustomKeys.Count - 1; k >= 0; k--)
                    {
                        var btn = _cfg.CustomKeys[k];
                        Rectangle r = GetCustomKeyRect(btn);
                        if (r.Contains(pt))
                        {
                            _customBtnTouchIds[btn.Id] = tl.Id;
                            _customBtnsHeldNow.Add(btn.Id);
                            if (!_customBtnLastPressSec.TryGetValue(btn.Id, out double lastSec) || (nowSec - lastSec) >= 0.16)
                            {
                                _customBtnLastPressSec[btn.Id] = nowSec;
                                _customBtnPulseFrames[btn.Id] = 2; // Clean 2-tick rising-edge pulse for SMAPI + Game1 without menu double-toggling!
                                OnCustomKeyActivated(pad, br, btn);
                            }
                            anyCustomTouched = true;
                            break;
                        }
                    }
                }
            }

            // Prune stale IDs if fingers vanished
            if (_customBtnTouchIds.Count > 0 && touches.Count == 0)
                _customBtnTouchIds.Clear();

            return anyCustomTouched;
        }

        private void OnCustomKeyActivated(object pad, PadBridge br, CustomKeyButton btn)
        {
            if (btn == null || btn.KeyCode <= 0)
                return;

            if (Game1.activeClickableMenu == null)
            {
                // Tell SDViOSTouchControls that any menu opened by this key press (e.g. ESC, E, M, P, I, F6, F7) was requested by the player!
                br?.SetPlayerRequestedMenuOpen(pad, true);
            }
            else if (btn.KeyCode == (int)Keys.Escape)
            {
                br?.SetPlayerRequestedMenuOpen(pad, false);
            }
        }

        private bool IsTouchOwnedByCustomButton(int touchId)
        {
            foreach (var kv in _customBtnTouchIds)
                if (kv.Value == touchId) return true;
            return false;
        }

        public void DrawMenuContextOverlay(object pad, SpriteBatch sb)
        {
            if (sb == null || sb.GraphicsDevice == null)
                return;

            IClickableMenu rawMenu = Game1.activeClickableMenu;
            if (rawMenu == null)
            {
                // Draw crisp pixel SKIP pill during skippable cutscenes (clear of Notch/Dynamic Island & Home Bar!)
                if (IsSkippableCutsceneActive())
                {
                    EnsureTextures(sb.GraphicsDevice);
                    if (!RefreshViewport()) return;
                    ComputeLayout();
                    PixelUi.GlobalOpacity = _cfg.PixelOpacity;
                    PixelUi.DrawPixelPill(sb, _pixel, EnsureBridge(pad), _btnSkipEventRect, "SKIP", PixelRole.Special, false, _vpH, -1, 0.92f);
                }
                return;
            }

            IClickableMenu topMenu = GetTopmostMenu(rawMenu);
            bool isThirdParty = IsThirdPartyModMenu(topMenu);
            if (rawMenu is BobberBar || (rawMenu is TitleMenu && !isThirdParty))
                return;

            bool showInvCtx = IsInventoryContextMenu(topMenu);
            bool showKeyOnly = !showInvCtx && IsTextInputMenu(topMenu);
            bool showEscOnly = !showInvCtx && !showKeyOnly && IsEscOnlyMenu(topMenu);
            bool allowCustomKeys = AllowCustomKeysInCurrentMenu() && !isThirdParty && _cfg.CustomKeys.Count > 0;
            bool showInvShop = _invShop.IsPopupOpen || (_cfg.MobileShopAndDrag && (_invShop.IsDraggingItem || _invShop.HasSelectedSlot));
            bool showJunimoHint = _cfg.SafeCrafting && _junimoHoveredSlot != null && ReferenceEquals(_junimoHoverMenu, topMenu as JunimoNoteMenu);
            bool showDlgHint = _cfg.SafeDialogueAndTooltip && _dlgHoveredIdx >= 0 && ReferenceEquals(_dlgHoverMenu, topMenu as DialogueBox) && _dlgHoverMenu.responseCC != null && _dlgHoveredIdx < _dlgHoverMenu.responseCC.Count;
            bool showCarpHint = _hasCarpenterPreview && ReferenceEquals(_carpenterPreviewMenu, topMenu as CarpenterMenu);
            if (!showInvCtx && !showKeyOnly && !showEscOnly && !allowCustomKeys && !showInvShop && !showJunimoHint && !showDlgHint && !showCarpHint)
                return;

            EnsureTextures(sb.GraphicsDevice);
            if (!RefreshViewport()) return;
            ComputeLayout();
            PixelUi.GlobalOpacity = _cfg.PixelOpacity;
            var br = EnsureBridge(pad);

            if (showInvCtx)
            {
                DrawMinimalPill(sb, _ctxX1Rect, "X1", br != null && br.GetSingleItemMode(pad));
                DrawMinimalPill(sb, _ctxHalfRect, "1/2", br != null && br.GetHalfStackMode(pad));
                DrawMinimalPill(sb, _ctxXferRect, "XFER", br != null && br.GetQuickTransferMode(pad));
                DrawMinimalPill(sb, _ctxKeyRect, "KEY", false);
                PixelUi.DrawPixelPill(sb, _pixel, br, _ctxEscRect, "ESC", PixelRole.Special, false, _vpH);
            }
            else if (showKeyOnly)
            {
                DrawMinimalPill(sb, _ctxX1Rect, "KEY", false);
                if (topMenu.readyToClose())
                    PixelUi.DrawPixelPill(sb, _pixel, br, _ctxHalfRect, "ESC", PixelRole.Special, false, _vpH);
            }
            else if (showEscOnly)
            {
                PixelUi.DrawPixelPill(sb, _pixel, br, _ctxQuestEscRect, "ESC", PixelRole.Special, false, _vpH);
            }

            if (showJunimoHint || showDlgHint || showCarpHint)
            {
                if (showJunimoHint || showDlgHint)
                {
                    Rectangle rb = showJunimoHint ? _junimoHoveredSlot.bounds : _dlgHoverMenu.responseCC[_dlgHoveredIdx].bounds;
                    Vector2 tl = UiToViewport(new Point(rb.X, rb.Y));
                    Vector2 brPt = UiToViewport(new Point(rb.Right, rb.Bottom));
                    var vr = new Rectangle((int)tl.X, (int)tl.Y, Math.Max(16, (int)(brPt.X - tl.X)), Math.Max(16, (int)(brPt.Y - tl.Y)));
                    PixelUi.DrawPixelTileTarget(sb, _pixel, vr, new Color(248, 206, 86), 0.12f);
                }

                string hTxt = showJunimoHint
                    ? "CHAM LAN NUA DE BO VAO GOI"
                    : (showDlgHint ? "CHAM LAN NUA DE CHON CAU TRA LOI" : "CHAM LAN NUA DE XAC NHAN VI TRI");
                int px = PixelUi.Step(_vpH);
                int bw = Math.Clamp((int)(_vpW * 0.42f), 260, _vpW - 40);
                int bh = Math.Max(32, (int)(_vpH * 0.058f));
                var hintR = new Rectangle((_vpW - bw) / 2, _vpH - bh - Math.Max(14, _vpH / 34), bw, bh);
                PixelUi.DrawParchmentBox(sb, _pixel, hintR, px, 0.94f);
                br?.DrawCenteredString(sb, _pixel, hTxt, hintR, new Color(74, 37, 17), Math.Max(1, _vpH / 320));
            }

            if (allowCustomKeys)
                DrawCustomKeyButtons(sb);

            if (showInvShop)
                _invShop.Draw(sb, _pixel, _circle, br, topMenu, UiToViewport, ViewportToUi, _vpW, _vpH, _cfg.SafeDialogueAndTooltip);
        }

        public void UpdateGameplay(object pad, GameTime gameTime)
        {
            _cfg.PollReload();
            ApplyLiveUiScaleAndWeather();
            _invShop.Reset();
            if (_wasInMenu || EffectiveMode != _lastModeSeen)
            {
                _wasInMenu = false;
                _lastModeSeen = EffectiveMode;
                ResetGameplayState();
            }
            var br = EnsureBridge(pad);
            if (!_loggedFirstIntercept)
            {
                _loggedFirstIntercept = true;
                _monitor?.Log($"[MobileControls] LIVE INTERCEPT ACTIVE on {pad.GetType().Assembly.GetName().Name} (Mode={_cfg.Mode}, CustomKeys={_cfg.CustomKeys.Count})", LogLevel.Info);
            }

            br?.SyncMenuBarConfig(pad);

            if (!RefreshViewport())
                return;
            ComputeLayout();

            // ---- Dedicated Auto Arcade Pad for PC Minigames (Prairie King, Junimo Kart, Crane Game, Fair Slingshot/Fishing, Pirate Darts) ----
            var arcadeKind = MobileMinigamePad.DetectCurrentArcadeMinigame();
            if (arcadeKind != MobileMinigamePad.ArcadeKind.None)
            {
                ClearCustomButtonTouches();
                ResetGameplayState();
                Vector2 mgStick = _minigamePad.Update(arcadeKind, TouchPanel.GetState(), MapRawToViewport, _vpW, _vpH);
                Point? mgMouseRaw = null;
                if (_minigamePad.MousePos.HasValue)
                    mgMouseRaw = ViewportToRaw(new Vector2(_minigamePad.MousePos.Value.X, _minigamePad.MousePos.Value.Y));
                else if (_minigamePad.MouseLeftDown)
                    mgMouseRaw = new Point(_rawW / 2, _rawH / 2);
                EnsureBridge(pad)?.ApplyInputWithMouse(pad, mgStick, false, false, false, mgMouseRaw, _minigamePad.MouseLeftDown, false, _rawW, _rawH);
                return;
            }
            _minigamePad.Reset();

            // ---- Dedicated Interactive Edit Layout Mode ----
            if (_editMode)
            {
                ClearCustomButtonTouches();
                UpdateEditLayoutTouches(TouchPanel.GetState());
                ApplyToPad(pad, Vector2.Zero, false, false, false);
                return;
            }

            // ---- Dedicated Stardew-Pixel Multi-Slot Save Menu Modal ----
            if (_saveMenuOpen)
            {
                ClearCustomButtonTouches();
                ResetGameplayState();
                double smNow = gameTime != null ? gameTime.TotalGameTime.TotalSeconds : Environment.TickCount64 / 1000.0;
                UpdateSaveMenuTouches(pad, br, TouchPanel.GetState(), smNow);
                ApplyToPad(pad, Vector2.Zero, false, false, false);
                return;
            }

            TickCustomButtonPulses();
            if (_cfg.VerticalToolbar)
                _verticalToolbar.UpdateInertia();

            string curLocName = Game1.currentLocation?.NameOrUniqueName;
            double nowSec = gameTime != null ? gameTime.TotalGameTime.TotalSeconds : Environment.TickCount64 / 1000.0;
            if (curLocName != _lastLocationName)
            {
                _lastLocationName = curLocName;
                ClearPath();
            }
            UpdateAutoCombatMode(nowSec);

            if (_toolRippleAlpha > 0f) _toolRippleAlpha = Math.Max(0f, _toolRippleAlpha - 0.12f);
            if (_interactRippleAlpha > 0f) _interactRippleAlpha = Math.Max(0f, _interactRippleAlpha - 0.12f);

            PollAutoKeyboard(pad, br, nowSec);
            TouchCollection touches = TouchPanel.GetState();
            PollGamepadAutoHide(touches.Count);

            // Physical Bluetooth Controller active (PS5/Xbox/Backbone) with no screen touch:
            // hide touch overlay AND forward real GCController sticks/buttons cleanly into Stardew!
            if (_gamepadHideHud && _hasPhysicalGamePadState && touches.Count == 0 && !_guiHidden)
            {
                var gs = _lastPhysicalGamePadState;
                // Convert MonoGame cartesian Y (+up/-down) into TouchVirtualPad screen Y (-up/+down)
                Vector2 gpStick = new Vector2(gs.ThumbSticks.Left.X, -gs.ThumbSticks.Left.Y);
                if (gs.DPad.Left == ButtonState.Pressed) gpStick.X = -1f;
                else if (gs.DPad.Right == ButtonState.Pressed) gpStick.X = 1f;
                if (gs.DPad.Up == ButtonState.Pressed) gpStick.Y = -1f;
                else if (gs.DPad.Down == ButtonState.Pressed) gpStick.Y = 1f;
                if (gpStick.LengthSquared() > 1f) gpStick.Normalize();

                br?.ApplyPhysicalGamePad(
                    pad,
                    gpStick,
                    btnX: gs.Buttons.X == ButtonState.Pressed,
                    btnA: gs.Buttons.A == ButtonState.Pressed,
                    btnB: gs.Buttons.B == ButtonState.Pressed,
                    btnY: gs.Buttons.Y == ButtonState.Pressed,
                    btnLT: gs.Buttons.LeftShoulder == ButtonState.Pressed || gs.Triggers.Left > 0.35f,
                    btnRT: gs.Buttons.RightShoulder == ButtonState.Pressed || gs.Triggers.Right > 0.35f,
                    btnMenu: gs.Buttons.Start == ButtonState.Pressed);
                return;
            }

            // ---- Clean Hide GUI Mode: ALL touch buttons & HUD hidden; Double-Tap anywhere restores GUI! ----
            if (_guiHidden)
            {
                ClearCustomButtonTouches();
                if (_cfg.PinchZoom && HandlePinchZoom(touches, nowSec))
                {
                    _hideTapTouchId = -1;
                    ApplyToPad(pad, Vector2.Zero, false, false, false);
                    return;
                }
                if (touches.Count == 1)
                {
                    TouchLocation htl = touches[0];
                    Vector2 hvp = MapRawToViewport(htl.Position);
                    if (htl.State == TouchLocationState.Pressed)
                    {
                        _hideTapTouchId = htl.Id;
                        _hideTapStartVp = hvp;
                    }
                    else if ((htl.State == TouchLocationState.Released || htl.State == TouchLocationState.Invalid) && htl.Id == _hideTapTouchId)
                    {
                        _hideTapTouchId = -1;
                        if (Vector2.Distance(hvp, _hideTapStartVp) < 36f)
                        {
                            if ((nowSec - _lastHideTapSec) <= 0.38 && Vector2.Distance(hvp, _lastHideTapVp) <= 130f)
                            {
                                SetGuiHidden(false);
                                _lastHideTapSec = -10.0;
                                try { Game1.playSound("smallSelect"); } catch { }
                                ShowSaveToast("DA HIEN LAI GUI", nowSec);
                            }
                            else
                            {
                                _lastHideTapSec = nowSec;
                                _lastHideTapVp = hvp;
                            }
                        }
                    }
                }
                else if (touches.Count > 1)
                {
                    // Multi-touch (e.g. 3-finger double tap for Mod Garden) cancels single-tap restore timer
                    _hideTapTouchId = -1;
                    _lastHideTapSec = -10.0;
                }
                ApplyToPad(pad, Vector2.Zero, false, false, false);
                return;
            }

            // --- 0. Process User Custom PC Key Buttons FIRST (never steal gameplay buttons!) ---
            if (_cfg.CustomKeys.Count > 0)
                ProcessCustomKeyTouches(pad, br, touches, nowSec);
            else
                ClearCustomButtonTouches();

            MobileControlMode effMode = EffectiveMode;
            bool useJoy = effMode == MobileControlMode.InvisibleJoystick2Btn ||
                          effMode == MobileControlMode.VisibleJoystick2Btn ||
                          effMode == MobileControlMode.TapAndJoystick;
            bool use2Btn = useJoy;
            bool useTap = effMode == MobileControlMode.TapToMove ||
                          effMode == MobileControlMode.TapAndJoystick;
            bool isHybrid = effMode == MobileControlMode.TapAndJoystick;

            // --- 1. Handle 2-Finger Pinch-to-Zoom ---
            if (_cfg.PinchZoom && HandlePinchZoom(touches, nowSec))
            {
                _joyTouchId = -1;
                _joyActivated = false;
                _tapTouchId = -1;
                _holdWalking = false;
                _tapChargingTool = false;
                _toolTouchId = -1;
                _interactTouchId = -1;
                _toolPressed = false;
                _interactPressed = false;
                _slingshotTouchId = -1;
                _toolbarTouchId = -1;
                _fishBendTouchId = -1;
                _fishBendX = 0f;
                _verticalToolbar.ResetTouch();
                ApplyToPad(pad, Vector2.Zero, false, false, false);
                return;
            }

            bool sawJoy = false;
            bool sawTool = false;
            bool sawInteract = false;
            bool sawTap = false;
            bool sawToolbar = false;
            bool sawSlingshot = false;
            Vector2 stickVec = Vector2.Zero;
            Furniture heldFurn = GetHeldRotatableFurniture();
            MeleeWeapon heldSpecWeapon = GetHeldSpecialWeapon();
            bool holdingSlingshot = Game1.player?.CurrentTool is Slingshot;
            if (!holdingSlingshot) _slingshotTouchId = -1;
            bool sawVertTb = false;

            for (int i = 0; i < touches.Count; i++)
            {
                TouchLocation tl = touches[i];
                if (IsTouchOwnedByCustomButton(tl.Id))
                    continue;

                Vector2 vp = MapRawToViewport(tl.Position);
                Point vpPt = new Point((int)vp.X, (int)vp.Y);
                bool active = tl.State == TouchLocationState.Pressed || tl.State == TouchLocationState.Moved;

                // Top utility bar (BAG / MAP / QST / SAVE / KEY / EDIT / collapse)
                if (_cfg.ShowTopUtility && tl.State == TouchLocationState.Pressed)
                {
                    if (_btnBagRect.Contains(vpPt))
                    {
                        br?.SetPlayerRequestedMenuOpen(pad, true);
                        _ctxTouchId = tl.Id;
                        if (Game1.activeClickableMenu == null)
                        {
                            try { Game1.activeClickableMenu = new GameMenu(false); } catch { }
                        }
                        ClearPath();
                        ApplyToPad(pad, Vector2.Zero, false, false, false);
                        return;
                    }
                    if (_btnCollapseRect.Contains(vpPt))
                    {
                        _topBarCollapsed = !_topBarCollapsed;
                        ComputeLayout();
                        try { Game1.playSound("smallSelect"); } catch { }
                        continue;
                    }
                    if (!_topBarCollapsed)
                    {
                        if (_btnMapRect.Contains(vpPt))
                        {
                            br?.SetPlayerRequestedMenuOpen(pad, true);
                            _ctxTouchId = tl.Id;
                            if (Game1.activeClickableMenu == null)
                            {
                                try { Game1.activeClickableMenu = new GameMenu(GameMenu.mapTab, -1, false); } catch { }
                            }
                            ClearPath();
                            ApplyToPad(pad, Vector2.Zero, false, false, false);
                            return;
                        }
                        if (_btnQuestRect.Contains(vpPt))
                        {
                            br?.SetPlayerRequestedMenuOpen(pad, true);
                            _ctxTouchId = tl.Id;
                            if (Game1.activeClickableMenu == null)
                            {
                                try { Game1.activeClickableMenu = new QuestLog(); } catch { }
                            }
                            ClearPath();
                            ApplyToPad(pad, Vector2.Zero, false, false, false);
                            return;
                        }
                        if (_btnSaveRect.Contains(vpPt))
                        {
                            OpenSaveMenu();
                            ClearPath();
                            ApplyToPad(pad, Vector2.Zero, false, false, false);
                            return;
                        }
                        if (_btnKeyRect.Contains(vpPt))
                        {
                            br?.TriggerVirtualKeyboard(pad);
                            continue;
                        }
                        if (!_btnEmoteRect.IsEmpty && _btnEmoteRect.Contains(vpPt))
                        {
                            br?.SetPlayerRequestedMenuOpen(pad, true);
                            _ctxTouchId = tl.Id;
                            if (Game1.activeClickableMenu == null && Game1.player != null && Game1.player.CanEmote())
                            {
                                try
                                {
                                    var em = new EmoteMenu();
                                    em.gamepadMode = false;
                                    Game1.activeClickableMenu = em;
                                }
                                catch { }
                            }
                            ClearPath();
                            ApplyToPad(pad, Vector2.Zero, false, false, false);
                            return;
                        }
                        if (!_btnCtlModeRect.IsEmpty && _btnCtlModeRect.Contains(vpPt))
                        {
                            CycleControlMode(nowSec);
                            continue;
                        }
                        if (!_btnChatRect.IsEmpty && _btnChatRect.Contains(vpPt))
                        {
                            try
                            {
                                Game1.chatBox?.activate();
                                br?.TriggerVirtualKeyboard(pad);
                            }
                            catch { }
                            ClearPath();
                            ApplyToPad(pad, Vector2.Zero, false, false, false);
                            return;
                        }
                        if (!_btnHideGuiRect.IsEmpty && _btnHideGuiRect.Contains(vpPt))
                        {
                            SetGuiHidden(true);
                            _lastHideTapSec = -10.0;
                            _hideTapTouchId = -1;
                            ResetGameplayState();
                            try { Game1.playSound("bigDeSelect"); } catch { }
                            ShowSaveToast("DA AN GUI - CHAM 2 LAN DE HIEN LAI", nowSec);
                            ApplyToPad(pad, Vector2.Zero, false, false, false);
                            return;
                        }
                        if (_btnEditRect.Contains(vpPt))
                        {
                            EnterEditLayoutMode();
                            try { Game1.playSound("smallSelect"); } catch { }
                            continue;
                        }
                    }
                }

                // Optional Mobile Vertical Scrollable 36-Slot Toolbar (left edge)
                if (_cfg.VerticalToolbar && _verticalToolbar.HandleTouch(tl, vp, out bool vSel))
                {
                    sawVertTb = true;
                    if (vSel)
                    {
                        _autoToolPrevSlot = -1;
                        _autoToolRestoreTimer = 0;
                        _repeatActive = false;
                    }
                    continue;
                }

                // Auto-context Weapon Special Move Button (SPEC: Parry / Slam / Flurry)
                if (heldSpecWeapon != null && tl.State == TouchLocationState.Pressed && _btnSpecRect.Contains(vpPt))
                {
                    if (!IsWeaponSpecialOnCooldown(heldSpecWeapon) && Game1.player != null && !Game1.player.UsingTool)
                    {
                        try { heldSpecWeapon.animateSpecialMove(Game1.player); } catch { }
                        ClearPath();
                    }
                    _toolbarTouchId = tl.Id;
                    _toolbarTappedSlot = -1;
                    _toolbarSwiped = true;
                    sawToolbar = true;
                    continue;
                }

                // Auto-context Furniture Rotate Button (ROT)
                if (heldFurn != null && tl.State == TouchLocationState.Pressed && _btnRotateRect.Contains(vpPt))
                {
                    try { heldFurn.rotate(); } catch { }
                    try { Game1.playSound("smallSelect"); } catch { }
                    _toolbarTouchId = tl.Id;
                    _toolbarTappedSlot = -1;
                    _toolbarSwiped = true;
                    sawToolbar = true;
                    continue;
                }

                // Compact ROW quick-switch pill beside Toolbar (when MaxItems > 12)
                if (!_btnToolbarRowRect.IsEmpty && tl.State == TouchLocationState.Pressed && _btnToolbarRowRect.Contains(vpPt))
                {
                    try { Game1.player?.shiftToolbar(true); } catch { }
                    try { Game1.playSound("shwip"); } catch { }
                    _toolbarTouchId = tl.Id;
                    _toolbarTappedSlot = -1;
                    _toolbarSwiped = true;
                    sawToolbar = true;
                    continue;
                }

                // Direct Toolbar tap & horizontal swipe (swipe left/right across Toolbar shifts rows!) + Quest Journal tap
                if (tl.Id == _toolbarTouchId)
                {
                    if (active)
                    {
                        sawToolbar = true;
                        if (!_toolbarSwiped && _toolbarTappedSlot >= 0 && Game1.player != null && Game1.player.MaxItems > 12)
                        {
                            float dx = vp.X - _toolbarStartVp.X;
                            if (Math.Abs(dx) >= 36f)
                            {
                                _toolbarSwiped = true;
                                try { Game1.player.shiftToolbar(dx < 0f); } catch { }
                                try { Game1.playSound("shwip"); } catch { }
                            }
                        }
                    }
                    else
                    {
                        if (!_toolbarSwiped && _toolbarTappedSlot >= 0 && Game1.player != null)
                        {
                            Game1.player.CurrentToolIndex = _toolbarTappedSlot;
                            _autoToolPrevSlot = -1;
                            _autoToolRestoreTimer = 0;
                            try { Game1.playSound("stoneStep"); } catch { }
                        }
                        _toolbarTouchId = -1;
                        _toolbarTappedSlot = -1;
                        _toolbarSwiped = false;
                    }
                    continue;
                }
                if (tl.State == TouchLocationState.Pressed && TryHandleHudTap(vp, out bool openedMenu, out int tappedSlot))
                {
                    if (openedMenu)
                    {
                        _ctxTouchId = tl.Id;
                        ClearPath();
                        ApplyToPad(pad, Vector2.Zero, false, false, false);
                        return;
                    }
                    _toolbarTouchId = tl.Id;
                    _toolbarStartVp = vp;
                    _toolbarTappedSlot = tappedSlot;
                    _toolbarSwiped = false;
                    sawToolbar = true;
                    continue;
                }

                // Existing active Slingshot touch drag-to-aim finger
                if (holdingSlingshot && tl.Id == _slingshotTouchId)
                {
                    if (active)
                    {
                        sawSlingshot = true;
                        _slingshotRawPos = tl.Position;
                        _slingshotVpPos = vp;
                    }
                    else
                    {
                        _slingshotTouchId = -1;
                    }
                    continue;
                }

                // Existing active right buttons
                if (use2Btn && tl.Id == _toolTouchId)
                {
                    if (active)
                    {
                        sawTool = true;
                        if (_cfg.ShowTouchHint) _toolRippleAlpha = 1f;
                    }
                    continue;
                }
                if (use2Btn && tl.Id == _interactTouchId)
                {
                    if (active)
                    {
                        sawInteract = true;
                        if (_cfg.ShowTouchHint) _interactRippleAlpha = 1f;
                    }
                    continue;
                }

                // Existing active joystick finger
                if (useJoy && tl.Id == _joyTouchId)
                {
                    if (active)
                    {
                        sawJoy = true;
                        float moved = Vector2.Distance(vp, _joyTouchStart);
                        if (!_joyActivated && (!isHybrid || moved >= 16f || (nowSec - _joyTouchStartTime) >= 0.16))
                        {
                            _joyActivated = true;
                            ClearPath();
                        }
                        if (_joyActivated)
                            stickVec = ComputeStick(vp);
                    }
                    else if (isHybrid && !_joyActivated)
                    {
                        HandleWorldTap(vp);
                    }
                    continue;
                }

                // Existing active tap-to-move / hold-to-walk / long-press charge-tool finger
                if (useTap && tl.Id == _tapTouchId)
                {
                    if (active)
                    {
                        sawTap = true;
                        _tapCurScreen = vp;
                        float moved = Vector2.Distance(vp, _tapStartScreen);
                        float chargeWobble = Math.Max(52f, _vpH * 0.055f);
                        if (!_tapChargingTool && !_holdWalking)
                        {
                            bool canChargeNow = CanChargeCurrentTool();
                            bool canCharge = canChargeNow && moved < chargeWobble && (nowSec - _tapStartTime) >= 0.16;
                            if (canCharge)
                            {
                                _tapChargingTool = true;
                                ClearPath();
                                if (Game1.player != null)
                                    Game1.player.faceGeneralDirection(ViewportToWorld(_tapStartScreen));
                            }
                            else if (moved >= (canChargeNow ? chargeWobble : 24f) || (!canChargeNow && (nowSec - _tapStartTime) >= 0.22))
                            {
                                _holdWalking = true;
                                ClearPath();
                            }
                        }
                    }
                    else
                    {
                        if (!_holdWalking && !_tapChargingTool)
                            HandleWorldTap(vp);
                        _holdWalking = false;
                        _tapChargingTool = false;
                        _tapTouchId = -1;
                    }
                    continue;
                }

                // Tap-to-Move Fishing Cast Sideways Bend (drag left/right while bobber is flying to steer into bubbles!)
                if (useTap && tl.Id == _fishBendTouchId)
                {
                    if (active && Game1.player?.CurrentTool is FishingRod bendRod && (bendRod.isCasting || bendRod.castedButBobberStillInAir))
                    {
                        float dx = vp.X - _fishBendStartVp.X;
                        _fishBendX = Math.Abs(dx) >= 18f ? Math.Sign(dx) : 0f;
                    }
                    else
                    {
                        _fishBendTouchId = -1;
                        _fishBendX = 0f;
                    }
                    continue;
                }

                // New touch pressed
                if (tl.State == TouchLocationState.Pressed)
                {
                    // Tap-to-Move: steer flying fishing bobber sideways into fish bubbles!
                    if (useTap && Game1.player?.CurrentTool is FishingRod castRod && (castRod.isCasting || castRod.castedButBobberStillInAir))
                    {
                        _fishBendTouchId = tl.Id;
                        _fishBendStartVp = vp;
                        _fishBendX = 0f;
                        continue;
                    }

                    // 0ms instant Fishing Hook/Reel when bobber is in water in Tap-to-Move / Hybrid mode!
                    if (useTap && Game1.player?.CurrentTool is FishingRod curRod && (curRod.isFishing || curRod.isNibbling))
                    {
                        ClearPath();
                        _actionPulseXFrames = 3;
                        _toolbarTouchId = tl.Id;
                        _toolbarTappedSlot = -1;
                        _toolbarSwiped = true;
                        sawToolbar = true;
                        continue;
                    }

                    // Direct Touch Drag-to-Aim when holding a Slingshot (outside Joystick zone — respects Left-Handed flip!)
                    bool joyHalf = _cfg.LeftHandedMobile ? (vp.X > _vpW * 0.56f) : (vp.X < _vpW * 0.44f);
                    bool inJoyZoneCheck = useJoy && (IsInvisibleBarMode
                        ? _invJoyRect.Contains(vpPt)
                        : (Vector2.Distance(vp, _visJoyCenter) <= _visJoyRadius * 1.65f || joyHalf));

                    if (holdingSlingshot && _slingshotTouchId < 0 && !inJoyZoneCheck)
                    {
                        _slingshotTouchId = tl.Id;
                        _slingshotRawPos = tl.Position;
                        _slingshotVpPos = vp;
                        sawSlingshot = true;
                        ClearPath();
                        continue;
                    }

                    if (use2Btn)
                    {
                        bool inTool = HitTestToolZone(vp, vpPt);
                        bool inInteract = HitTestActZone(vp, vpPt);

                        if (inTool && inInteract)
                        {
                            Rectangle tr = IsInvisibleBarMode ? _invToolRect : _visToolRect;
                            Rectangle ar = IsInvisibleBarMode ? _invActRect : _visActRect;
                            float dTool = Vector2.DistanceSquared(vp, tr.Center.ToVector2());
                            float dInt = Vector2.DistanceSquared(vp, ar.Center.ToVector2());
                            inTool = dTool <= dInt;
                            inInteract = !inTool;
                        }

                        if (inTool && _toolTouchId < 0)
                        {
                            _toolTouchId = tl.Id;
                            sawTool = true;
                            if (_cfg.ShowTouchHint) _toolRippleAlpha = 1f;
                            ClearPath();
                            continue;
                        }
                        if (inInteract && _interactTouchId < 0)
                        {
                            _interactTouchId = tl.Id;
                            sawInteract = true;
                            if (_cfg.ShowTouchHint) _interactRippleAlpha = 1f;
                            ClearPath();
                            continue;
                        }
                    }

                    if (useJoy && _joyTouchId < 0 && inJoyZoneCheck)
                    {
                        _joyTouchId = tl.Id;
                        _joyTouchStart = vp;
                        _joyTouchStartTime = nowSec;
                        _joyOrigin = (!IsInvisibleBarMode && Vector2.Distance(vp, _visJoyCenter) <= _visJoyRadius * 1.45f)
                            ? _visJoyCenter
                            : vp;
                        _joyKnob = vp;
                        _joyActivated = !isHybrid;
                        if (_joyActivated)
                            ClearPath();
                        sawJoy = true;
                        continue;
                    }

                    if (useTap && _tapTouchId < 0)
                    {
                        _tapTouchId = tl.Id;
                        _tapStartScreen = vp;
                        _tapCurScreen = vp;
                        _tapStartTime = nowSec;
                        _holdWalking = false;
                        _tapChargingTool = false;
                        sawTap = true;

                        // Tap-to-Move Instant Fishing Rod Charge:
                        // When holding a FishingRod near water and pressing on water, begin charging IMMEDIATELY
                        // (0ms delay, wobble-proof) and hold ButtonX continuously until finger is lifted!
                        if (CanChargeCurrentTool() && Game1.player?.CurrentTool is FishingRod pressRod &&
                            !pressRod.isCasting && !pressRod.castedButBobberStillInAir &&
                            Game1.currentLocation != null && !Game1.player.UsingTool)
                        {
                            Vector2 wPos = ViewportToWorld(vp);
                            Point tapT = new Point((int)Math.Floor(wPos.X / 64f), (int)Math.Floor(wPos.Y / 64f));
                            if (IsWaterInFishingCastReach(Game1.currentLocation, Game1.player.TilePoint, tapT))
                            {
                                _tapChargingTool = true;
                                ClearPath();
                                Game1.player.faceGeneralDirection(wPos);
                            }
                        }
                        continue;
                    }

                    // In Invisible / 2-Button mode: tapping an adjacent/diagonal tile right next to the player
                    // outside Joystick & USE/ACT zones lets you interact with diagonal chests/machines/wall decor!
                    if (!useTap && TryDirectAdjacentTileTap(vp))
                    {
                        continue;
                    }
                }
            }

            if (!sawToolbar) { _toolbarTouchId = -1; _toolbarTappedSlot = -1; _toolbarSwiped = false; }
            if (!sawVertTb) _verticalToolbar.ResetTouch();
            if (!sawJoy) { _joyTouchId = -1; _joyActivated = false; }
            if (!sawTool) _toolTouchId = -1;
            if (!sawInteract) _interactTouchId = -1;
            if (!sawTap) { _tapTouchId = -1; _holdWalking = false; _tapChargingTool = false; }
            if (!sawSlingshot) _slingshotTouchId = -1;

            _toolPressed = sawTool || _tapChargingTool;
            _interactPressed = sawInteract;

            // Lock movement strictly to zero while charging a tool (prevents wobble cancelling FishingRod cast!)
            if (_tapChargingTool)
                stickVec = Vector2.Zero;

            // --- 2. Hold-to-Walk (+ Smooth Auto-Harvest/Machine Sweep in Tap-to-Move!) ---
            if (_holdWalking && !_tapChargingTool && stickVec == Vector2.Zero && Game1.player != null)
            {
                Vector2 playerScreen = WorldToViewport(Game1.player.GetBoundingBox().Center.ToVector2());
                Vector2 diff = _tapCurScreen - playerScreen;
                if (diff.Length() > 26f)
                {
                    diff.Normalize();
                    stickVec = diff;
                    TryHoldWalkAutoHarvest(Game1.player, Game1.currentLocation);
                }
            }

            // --- 2b. Apply Tap-to-Move Fishing Cast Sideways Bend while bobber is in flight ---
            if (_fishBendTouchId >= 0)
            {
                if (Game1.player?.CurrentTool is FishingRod frFly && (frFly.isCasting || frFly.castedButBobberStillInAir))
                {
                    if (Math.Abs(_fishBendX) > 0.1f)
                        stickVec = new Vector2(_fishBendX, 0f);
                }
                else
                {
                    _fishBendTouchId = -1;
                    _fishBendX = 0f;
                }
            }

            // --- 3. A* Tap-to-Move Path Follower + Door/Warp Exit Push ---
            if (Game1.fadeToBlack || Game1.dialogueUp || Game1.activeClickableMenu != null || Game1.eventUp)
            {
                _warpPushFrames = 0;
                _actionPulseAFrames = 0;
                _actionPulseXFrames = 0;
            }
            if (_warpPushFrames > 0 && Game1.hudMessages != null && Game1.hudMessages.Count > _warpPushStartHudCount)
                _warpPushFrames = 0;

            if (stickVec == Vector2.Zero && !_toolPressed && !_interactPressed && !sawSlingshot)
            {
                if (_path != null)
                {
                    stickVec = StepPathFollower();
                }
                else if (_warpPushFrames > 0 && Game1.player != null && !Game1.fadeToBlack)
                {
                    _warpPushFrames--;
                    Vector2 warpCenter = new Vector2(_pendingWarpTile.X * 64f + 32f, _pendingWarpTile.Y * 64f + 32f);
                    Vector2 playerCenter = Game1.player.GetBoundingBox().Center.ToVector2();
                    Vector2 diff = warpCenter - playerCenter;
                    if (diff.LengthSquared() > 4f)
                    {
                        diff.Normalize();
                        stickVec = diff;
                    }
                    else
                    {
                        // Nudge straight toward map edge if player is already centered on the warp tile
                        int mapH = Game1.currentLocation?.Map?.Layers?[0]?.LayerHeight ?? 0;
                        int mapW = Game1.currentLocation?.Map?.Layers?[0]?.LayerWidth ?? 0;
                        if (mapH > 0 && _pendingWarpTile.Y >= mapH - 2) stickVec = new Vector2(0f, 1f);
                        else if (_pendingWarpTile.Y <= 1) stickVec = new Vector2(0f, -1f);
                        else if (mapW > 0 && _pendingWarpTile.X >= mapW - 2) stickVec = new Vector2(1f, 0f);
                        else if (_pendingWarpTile.X <= 1) stickVec = new Vector2(-1f, 0f);
                    }
                }
            }

            // --- 4. Action Pulses + Auto-Repeat 1-tap chop/mine until broken ---
            bool fireX = _toolPressed || sawSlingshot;
            bool fireA = _interactPressed;
            if (_actionPulseXFrames > 0) { _actionPulseXFrames--; fireX = true; }
            if (_actionPulseAFrames > 0) { _actionPulseAFrames--; fireA = true; }

            if (_repeatActive)
            {
                Farmer rw = Game1.player;
                GameLocation rloc = Game1.currentLocation;
                if (!_cfg.AutoRepeat || rw == null || rloc == null || stickVec.LengthSquared() > 0.04f || _toolPressed || _interactPressed || sawSlingshot || rw.Stamina <= 2f)
                {
                    _repeatActive = false;
                }
                else if (!rw.UsingTool)
                {
                    if (_repeatCooldown > 0)
                    {
                        _repeatCooldown--;
                    }
                    else
                    {
                        int cheb = Math.Max(Math.Abs(rw.TilePoint.X - _repeatTile.X), Math.Abs(rw.TilePoint.Y - _repeatTile.Y));
                        if (cheb <= 1)
                        {
                            TileIntent nextIntent = AutoToolAndCombat.InspectTile(rloc, _repeatTile, rw, _cfg.AutoTool);
                            if (nextIntent.Repeatable && nextIntent.Kind == TileActionKind.UseTool)
                            {
                                if (nextIntent.PreferredToolSlot >= 0 && nextIntent.PreferredToolSlot < rw.Items.Count)
                                    rw.CurrentToolIndex = nextIntent.PreferredToolSlot;
                                rw.faceGeneralDirection(new Vector2(_repeatTile.X * 64f + 32f, _repeatTile.Y * 64f + 32f));
                                _actionPulseXFrames = 2;
                                fireX = true;
                                _repeatCooldown = 7;
                            }
                            else
                            {
                                _repeatActive = false;
                            }
                        }
                        else
                        {
                            _repeatActive = false;
                        }
                    }
                }
            }

            // --- 4b. Optional restore previous item slot after Auto-Tool finishes ---
            if (_autoToolPrevSlot >= 0)
            {
                Farmer fw = Game1.player;
                if (!_cfg.RestoreToolAfterAuto || fw == null)
                {
                    _autoToolPrevSlot = -1;
                    _autoToolRestoreTimer = 0;
                }
                else if (fw.UsingTool || _repeatActive || fireX || fireA)
                {
                    _autoToolRestoreTimer = 30;
                }
                else if (_autoToolRestoreTimer > 0)
                {
                    if (--_autoToolRestoreTimer == 0)
                    {
                        if (_autoToolPrevSlot >= 0 && _autoToolPrevSlot < fw.Items.Count)
                            fw.CurrentToolIndex = _autoToolPrevSlot;
                        _autoToolPrevSlot = -1;
                    }
                }
            }

            // --- 5. Optional Auto-Attack (DEFAULT OFF) ---
            if (_cfg.AutoAttack && !fireX && !fireA && !sawSlingshot)
            {
                bool movingAway = stickVec.LengthSquared() > 0.25f;
                if (AutoToolAndCombat.TryAutoAttack(Game1.currentLocation, Game1.player, movingAway, ref _autoAttackCooldown))
                {
                    _actionPulseXFrames = 2;
                    fireX = true;
                    ClearPath();
                }
            }

            // --- 6. Optional Safe Periodic Auto-Save (DEFAULT OFF, skips Mines/Volcano/Combat/Moving) ---
            TryPeriodicAutoSave(br, stickVec, fireX || fireA || sawSlingshot, nowSec);

            // --- 7. Tiered Auto-Unstick Watchdog (rescues ALL Stardew freeze locks WITHOUT ever cancelling normal tool swings,
            //        eating animations, or active FishingRod casts!) ---
            if (stickVec.LengthSquared() > 0.08f &&
                !_toolPressed && !_interactPressed && !sawSlingshot &&
                Game1.player != null &&
                Game1.activeClickableMenu == null &&
                !Game1.eventUp && !Game1.dialogueUp && !Game1.fadeToBlack &&
                Game1.currentMinigame == null)
            {
                Farmer uWho = Game1.player;
                FishingRod uFr = uWho.CurrentTool as FishingRod;
                bool activeFishingState = uFr != null &&
                    (uFr.isCasting || uFr.castedButBobberStillInAir || uFr.isFishing || uFr.isNibbling ||
                     uFr.isReeling || uFr.pullingOutOfWater || uFr.fishCaught || uFr.showingTreasure);
                bool stuckTimingCast = uFr != null && uFr.isTimingCast;
                bool strayKbSub = Game1.keyboardDispatcher != null && Game1.keyboardDispatcher.Subscriber != null &&
                                  (Game1.chatBox == null || !Game1.chatBox.isActive());

                int unstickThreshold = 0;
                if (activeFishingState)
                {
                    // Never interrupt an active cast/fishing animation
                    unstickThreshold = 0;
                }
                else if (stuckTimingCast)
                {
                    // Safety net ONLY if isTimingCast stays stuck with !_toolPressed for > 2.5s
                    unstickThreshold = 150;
                }
                else if (uWho.isEating)
                {
                    // Allow full ~1.5s eating animation to finish healing HP/energy before rescuing
                    unstickThreshold = 135;
                }
                else if (uWho.UsingTool)
                {
                    // Allow full ~0.55s Axe/Pickaxe/Hoe/Weapon swing to land before treating as stuck
                    unstickThreshold = 55;
                }
                else if (!uWho.CanMove ||
                         Game1.freezeControls ||
                         uWho.freezePause > 0 ||
                         (uWho.FarmerSprite != null && uWho.FarmerSprite.PauseForSingleAnimation) ||
                         uWho.usingSlingshot ||
                         uWho.controller != null ||
                         strayKbSub)
                {
                    // Pure freeze lock (not swinging/eating/fishing): rescue fast at 20 ticks (~0.33s),
                    // well before StepPathFollower's 36-tick path abort so Tap-to-Move ALSO recovers!
                    unstickThreshold = 20;
                }

                if (unstickThreshold > 0)
                {
                    if (++_unstickHoldTicks >= unstickThreshold)
                    {
                        _unstickHoldTicks = 0;
                        _unstickStillTicks = 0;
                        try
                        {
                            if (uWho.IsSitting()) uWho.StopSitting(animate: false);
                            uWho.completelyStopAnimatingOrDoingAction();
                            uWho.forceCanMove();
                            uWho.controller = null;
                            if (uFr != null && !activeFishingState)
                            {
                                uFr.isTimingCast = false;
                                uFr.isCasting = false;
                                uFr.pullingOutOfWater = false;
                            }
                            if (strayKbSub)
                                Game1.keyboardDispatcher.Subscriber = null;
                        }
                        catch { }
                    }
                }
                else
                {
                    _unstickHoldTicks = 0;
                    // Secondary watchdog: if farmer is pushing joystick/path for 36 ticks without moving even 1px
                    // while sitting/emoting, release sitting/emote cleanly!
                    if (Vector2.DistanceSquared(uWho.Position, _unstickLastPos) < 1.5f)
                    {
                        if (++_unstickStillTicks >= 36 && !activeFishingState && !uWho.isEating && !uWho.UsingTool)
                        {
                            _unstickStillTicks = 0;
                            if (uWho.IsSitting() || uWho.IsEmoting)
                            {
                                try
                                {
                                    if (uWho.IsSitting()) uWho.StopSitting(animate: false);
                                    uWho.IsEmoting = false;
                                    uWho.forceCanMove();
                                }
                                catch { }
                            }
                        }
                    }
                    else
                    {
                        _unstickLastPos = uWho.Position;
                        _unstickStillTicks = 0;
                    }
                }
            }
            else
            {
                _unstickHoldTicks = 0;
                _unstickStillTicks = 0;
                if (Game1.player != null)
                    _unstickLastPos = Game1.player.Position;
            }

            if (sawSlingshot)
            {
                Point aimRaw = new Point((int)_slingshotRawPos.X, (int)_slingshotRawPos.Y);
                EnsureBridge(pad)?.ApplyInputWithMouse(pad, stickVec, true, false, false, aimRaw, true, false, _rawW, _rawH);
            }
            else if (fireX && Game1.player?.CurrentTool is FishingRod timingRod && timingRod.isTimingCast)
            {
                // Keep MouseState.LeftButton == Pressed alongside ButtonX while charging cast bar so FishingRod.tickUpdate
                // NEVER prematurely triggers startCasting() on iOS!
                Point holdPos = new Point(Math.Max(1, _rawW / 2), Math.Max(1, _rawH / 2));
                EnsureBridge(pad)?.ApplyInputWithMouse(pad, Vector2.Zero, true, false, false, holdPos, true, false, _rawW, _rawH);
                Game1.lastCursorMotionWasMouse = false;
            }
            else
            {
                ApplyToPad(pad, stickVec, fireX, fireA, false);
            }
        }

        private static bool CanChargeCurrentTool()
        {
            var tool = Game1.player?.CurrentTool;
            if (tool is FishingRod fr)
                return !fr.isFishing && !fr.isReeling && !fr.isNibbling;
            return (tool is Hoe || tool is WateringCan) && tool.UpgradeLevel >= 1;
        }

        private void TryHoldWalkAutoHarvest(Farmer who, GameLocation loc)
        {
            if (!_cfg.AutoTool || who == null || loc == null || who.UsingTool || who.isRidingHorse())
                return;
            if (_holdSweepCooldown > 0)
            {
                _holdSweepCooldown--;
                return;
            }
            Point pt = who.TilePoint;
            int fx = who.FacingDirection == 1 ? 1 : (who.FacingDirection == 3 ? -1 : 0);
            int fy = who.FacingDirection == 2 ? 1 : (who.FacingDirection == 0 ? -1 : 0);
            Point front = new Point(pt.X + fx, pt.Y + fy);
            Vector2 tv = new Vector2(front.X, front.Y);

            bool ready = false;
            if (loc.objects != null && loc.objects.TryGetValue(tv, out StardewValley.Object obj) && obj != null)
            {
                if (obj.readyForHarvest.Value || obj.IsSpawnedObject)
                    ready = true;
            }
            else if (loc.terrainFeatures != null && loc.terrainFeatures.TryGetValue(tv, out StardewValley.TerrainFeatures.TerrainFeature tf) && tf is StardewValley.TerrainFeatures.HoeDirt hd)
            {
                if (hd.crop != null && hd.readyForHarvest() && hd.crop.GetHarvestMethod() != StardewValley.GameData.Crops.HarvestMethod.Scythe)
                    ready = true;
            }

            if (ready)
            {
                _actionPulseAFrames = 1;
                _holdSweepCooldown = 8;
            }
        }

        private void TryPeriodicAutoSave(PadBridge br, Vector2 stickVec, bool anyAction, double nowSec)
        {
            if (_cfg.AutoSaveMinutes <= 0 || br == null || Game1.player == null || Game1.currentLocation == null)
                return;

            // Initialize timer on first gameplay tick so it doesn't save immediately on load
            if (_lastAutoSaveSec < 0)
            {
                _lastAutoSaveSec = nowSec;
                _lastAutoSaveTimeOfDay = Game1.timeOfDay;
                _lastAutoSaveLoc = Game1.currentLocation.NameOrUniqueName;
                return;
            }

            // Never interrupt combat, mines, volcano, tool swing, fishing, or movement!
            bool busy = stickVec.LengthSquared() > 0.01f || anyAction ||
                        Game1.player.UsingTool || Game1.player.isMoving() ||
                        (Game1.player.CurrentTool is FishingRod fr && (fr.isFishing || fr.isCasting || fr.isReeling)) ||
                        Game1.currentLocation is MineShaft || Game1.currentLocation is VolcanoDungeon ||
                        br.IsMiddaySaving() || br.IsQuickSaveReloading();

            if (busy)
            {
                _idleSinceSec = -1;
                return;
            }

            if (_idleSinceSec < 0)
                _idleSinceSec = nowSec;

            // Wait until player stands still >= 1.0s in a safe area AND interval elapsed AND game progressed
            double intervalSec = _cfg.AutoSaveMinutes * 60.0;
            if ((nowSec - _lastAutoSaveSec) < intervalSec || (nowSec - _idleSinceSec) < 1.0)
                return;

            string curLoc = Game1.currentLocation.NameOrUniqueName;
            if (Game1.timeOfDay == _lastAutoSaveTimeOfDay && string.Equals(curLoc, _lastAutoSaveLoc, StringComparison.Ordinal))
                return;

            _lastAutoSaveSec = nowSec;
            _lastAutoSaveTimeOfDay = Game1.timeOfDay;
            _lastAutoSaveLoc = curLoc;
            _idleSinceSec = -1;

            _saveSlots.BeginPendingSnapshot(br, isAutoSave: true);
            if (br.TryQuickSave(out _))
            {
                ShowSaveToast("AUTO-SAVE...", nowSec);
            }
            else
            {
                _saveSlots.CancelPendingSnapshot();
            }
        }

        // =========================================================================
        // DEDICATED EDIT LAYOUT MODE + IN-GAME PC KEY PICKER
        // =========================================================================
        private void UpdateEditLayoutTouches(TouchCollection touches)
        {
            if (touches.Count == 0)
            {
                _editTouchId = -1;
                _editDragTarget = EditTarget.None;
                _editDragCustomId = -1;
                _editResizing = false;
                return;
            }

            // If PC Key Picker modal is open inside Edit Layout, handle taps on key tiles
            if (_keyPickerOpen)
            {
                for (int i = 0; i < touches.Count; i++)
                {
                    TouchLocation tl = touches[i];
                    if (tl.State != TouchLocationState.Pressed) continue;
                    Vector2 vp = MapRawToViewport(tl.Position);
                    Point pt = new Point((int)vp.X, (int)vp.Y);

                    if (_pickerCloseRect.Contains(pt) || !_pickerPanelRect.Contains(pt))
                    {
                        _keyPickerOpen = false;
                        _editTouchId = tl.Id;
                        _editDragTarget = EditTarget.None;
                        try { Game1.playSound("cancel"); } catch { }
                        return;
                    }

                    for (int k = 0; k < PcKeyCatalog.Length; k++)
                    {
                        Rectangle cell = GetKeyPickerCellRect(k);
                        if (cell.Contains(pt))
                        {
                            var kc = PcKeyCatalog[k];
                            var added = _cfg.AddCustomKey(kc.Label, (int)kc.Key);
                            if (added != null)
                            {
                                _editSelected = EditTarget.CustomKey;
                                _editSelectedCustomId = added.Id;
                                ComputeLayout();
                                try { Game1.playSound("coin"); } catch { }
                            }
                            _keyPickerOpen = false;
                            _editTouchId = tl.Id;
                            _editDragTarget = EditTarget.None;
                            return;
                        }
                    }
                }
                return;
            }

            for (int i = 0; i < touches.Count; i++)
            {
                TouchLocation tl = touches[i];
                Vector2 vp = MapRawToViewport(tl.Position);
                Point pt = new Point((int)vp.X, (int)vp.Y);

                if (tl.State == TouchLocationState.Pressed && _editTouchId < 0)
                {
                    // Top center Edit Layout toolbar buttons (+PHIM / XOA / RESET / HUY / LUU)
                    if (_editAddKeyBtnRect.Contains(pt))
                    {
                        _keyPickerOpen = true;
                        _editTouchId = tl.Id;
                        _editDragTarget = EditTarget.None;
                        try { Game1.playSound("smallSelect"); } catch { }
                        return;
                    }
                    if (_editDelKeyBtnRect.Contains(pt))
                    {
                        if (_editSelected == EditTarget.CustomKey && _editSelectedCustomId > 0)
                        {
                            _cfg.RemoveCustomKey(_editSelectedCustomId);
                            _editSelected = EditTarget.None;
                            _editSelectedCustomId = -1;
                            try { Game1.playSound("trashcan"); } catch { }
                        }
                        return;
                    }
                    if (_editResetBtnRect.Contains(pt))
                    {
                        _cfg.ResetLayoutDefaults();
                        _cfg.Save();
                        ComputeLayout();
                        try { Game1.playSound("smallSelect"); } catch { }
                        return;
                    }
                    if (_editCancelBtnRect.Contains(pt))
                    {
                        _cfg.Reload(force: true);
                        _editMode = false;
                        try { Game1.playSound("cancel"); } catch { }
                        return;
                    }
                    if (_editSaveBtnRect.Contains(pt))
                    {
                        _cfg.Save();
                        _editMode = false;
                        try { Game1.playSound("coin"); } catch { }
                        return;
                    }

                    // 1. Hit-test Custom PC Key buttons FIRST (resize handle then body)
                    EditTarget hit = EditTarget.None;
                    int hitCustomId = -1;
                    bool resize = false;

                    for (int k = _cfg.CustomKeys.Count - 1; k >= 0; k--)
                    {
                        var cb = _cfg.CustomKeys[k];
                        Rectangle cr = GetCustomKeyRect(cb);
                        if (HitResizeHandle(cr, vp))
                        {
                            hit = EditTarget.CustomKey;
                            hitCustomId = cb.Id;
                            resize = true;
                            break;
                        }
                        if (cr.Contains(pt))
                        {
                            hit = EditTarget.CustomKey;
                            hitCustomId = cb.Id;
                            break;
                        }
                    }

                    // 2. Hit-test Vertical Toolbar (if enabled: inner corner handle resizes ToolbarSize, body drags side Left<->Right & Y!)
                    if (hit == EditTarget.None && _cfg.VerticalToolbar && _verticalToolbar.Bounds.Width > 0)
                    {
                        Rectangle tbR = _verticalToolbar.Bounds;
                        if (HitToolbarResizeHandle(tbR, _cfg.ToolbarSide, vp))
                        {
                            hit = EditTarget.Toolbar;
                            resize = true;
                        }
                        else if (tbR.Contains(pt))
                        {
                            hit = EditTarget.Toolbar;
                        }
                    }

                    // 3. Hit-test standard Mobile controls
                    if (hit == EditTarget.None && _cfg.Mode != MobileControlMode.TapToMove)
                    {
                        Rectangle rTool = GetEditRect(EditTarget.Tool);
                        Rectangle rAct = GetEditRect(EditTarget.Act);
                        Rectangle rJoy = GetEditRect(EditTarget.Joy);

                        if (HitResizeHandle(rTool, vp)) { hit = EditTarget.Tool; resize = true; }
                        else if (HitResizeHandle(rAct, vp)) { hit = EditTarget.Act; resize = true; }
                        else if (HitResizeHandle(rJoy, vp)) { hit = EditTarget.Joy; resize = true; }
                        else if (rTool.Contains(pt)) { hit = EditTarget.Tool; }
                        else if (rAct.Contains(pt)) { hit = EditTarget.Act; }
                        else if (rJoy.Contains(pt)) { hit = EditTarget.Joy; }
                    }

                    if (hit == EditTarget.None && _cfg.ShowTopUtility && _topBarGroupRect.Contains(pt))
                        hit = EditTarget.TopBar;

                    if (hit != EditTarget.None)
                    {
                        _editTouchId = tl.Id;
                        _editDragTarget = hit;
                        _editDragCustomId = hitCustomId;
                        _editSelected = hit;
                        _editSelectedCustomId = hitCustomId;
                        _editResizing = resize;
                        _editDragLastVp = vp;
                    }
                }
                else if (tl.State == TouchLocationState.Moved && tl.Id == _editTouchId && _editDragTarget != EditTarget.None)
                {
                    Vector2 d = vp - _editDragLastVp;
                    _editDragLastVp = vp;
                    ApplyEditDelta(_editDragTarget, _editDragCustomId, _editResizing, d, vp);
                    ComputeLayout();
                }
                else if ((tl.State == TouchLocationState.Released || tl.State == TouchLocationState.Invalid) && tl.Id == _editTouchId)
                {
                    if (_editDragTarget != EditTarget.None)
                        _cfg.Save();
                    _editTouchId = -1;
                    _editDragTarget = EditTarget.None;
                    _editDragCustomId = -1;
                    _editResizing = false;
                }
            }
        }

        private Rectangle GetEditRect(EditTarget t)
        {
            switch (t)
            {
                case EditTarget.Joy:
                    if (IsInvisibleBarMode) return _invJoyRect;
                    int jd = (int)(_visJoyRadius * 2f);
                    return new Rectangle((int)(_visJoyCenter.X - _visJoyRadius), (int)(_visJoyCenter.Y - _visJoyRadius), jd, jd);
                case EditTarget.Tool:
                    return IsInvisibleBarMode ? _invToolRect : _visToolRect;
                case EditTarget.Act:
                    return IsInvisibleBarMode ? _invActRect : _visActRect;
                case EditTarget.TopBar:
                    return _topBarGroupRect;
                case EditTarget.Toolbar:
                    return _verticalToolbar.Bounds;
                default:
                    return Rectangle.Empty;
            }
        }

        private Rectangle GetCustomKeyRect(CustomKeyButton b)
        {
            int x = (int)Math.Round(b.X * _vpW);
            int y = (int)Math.Round(b.Y * _vpH);
            int w = Math.Max(36, (int)Math.Round(b.W * _vpW));
            int h = Math.Max(36, (int)Math.Round(b.H * _vpH));
            return new Rectangle(x, y, w, h);
        }

        private bool HitResizeHandle(Rectangle r, Vector2 vp)
        {
            if (r.IsEmpty) return false;
            float grip = Math.Max(38f, Math.Min(_vpW, _vpH) * 0.048f);
            return Math.Abs(vp.X - r.Right) <= grip && Math.Abs(vp.Y - r.Bottom) <= grip;
        }

        private bool HitToolbarResizeHandle(Rectangle r, int side, Vector2 vp)
        {
            if (r.IsEmpty) return false;
            float grip = Math.Max(42f, Math.Min(_vpW, _vpH) * 0.055f);
            float hx = side == 1 ? r.Left : r.Right;
            float hy = r.Bottom;
            return Math.Abs(vp.X - hx) <= grip && Math.Abs(vp.Y - hy) <= grip;
        }

        private void ApplyEditDelta(EditTarget target, int customId, bool resize, Vector2 deltaPx, Vector2 curVp)
        {
            float dx = deltaPx.X / Math.Max(1f, _vpW);
            float dy = deltaPx.Y / Math.Max(1f, _vpH);
            float unit = Math.Max(1f, Math.Min(_vpW, _vpH));

            if (target == EditTarget.Toolbar)
            {
                if (resize)
                {
                    float signedDx = (_cfg.ToolbarSide == 1 ? -deltaPx.X : deltaPx.X);
                    float dScale = (signedDx + deltaPx.Y * 0.35f) / Math.Max(120f, unit * 0.28f);
                    _cfg.ToolbarSize = Math.Clamp(_cfg.ToolbarSize + dScale, 0.55f, 1.50f);
                }
                else
                {
                    // Drag horizontally past center flips Left <-> Right edge cleanly; drag vertically repositions Y!
                    if (curVp.X > _vpW * 0.62f) _cfg.ToolbarSide = 1;
                    else if (curVp.X < _vpW * 0.38f) _cfg.ToolbarSide = 0;
                    _cfg.ToolbarY = Math.Clamp(_cfg.ToolbarY + dy * 1.35f, 0.0f, 1.0f);
                }
                return;
            }

            if (target == EditTarget.CustomKey)
            {
                for (int i = 0; i < _cfg.CustomKeys.Count; i++)
                {
                    var b = _cfg.CustomKeys[i];
                    if (b.Id != customId) continue;
                    if (resize)
                    {
                        b.W = Math.Clamp(b.W + dx, 0.035f, Math.Min(0.35f, 0.99f - b.X));
                        b.H = Math.Clamp(b.H + dy, 0.050f, Math.Min(0.45f, 0.99f - b.Y));
                    }
                    else
                    {
                        b.X = Math.Clamp(b.X + dx, 0.005f, 0.995f - b.W);
                        b.Y = Math.Clamp(b.Y + dy, 0.005f, 0.995f - b.H);
                    }
                    break;
                }
                return;
            }

            if (target == EditTarget.TopBar)
            {
                _cfg.TopBarX = Math.Clamp(_cfg.TopBarX + dx, 0.01f, 0.85f);
                _cfg.TopBarY = Math.Clamp(_cfg.TopBarY + dy, 0.005f, 0.90f);
                return;
            }

            bool mirrorX = _cfg.LeftHandedMobile;
            float moveDx = mirrorX ? -dx : dx;

            if (IsInvisibleBarMode)
            {
                bool swap = _cfg.SwapRightButtons;
                EditTarget logical = target;
                if (swap && target == EditTarget.Tool) logical = EditTarget.Act;
                else if (swap && target == EditTarget.Act) logical = EditTarget.Tool;

                if (logical == EditTarget.Joy)
                {
                    if (resize)
                    {
                        float oldW = _cfg.InvJoyW;
                        float maxW = mirrorX ? Math.Min(0.75f, _cfg.InvJoyX + _cfg.InvJoyW) : Math.Min(0.75f, 0.99f - _cfg.InvJoyX);
                        _cfg.InvJoyW = Math.Clamp(oldW + dx, 0.14f, maxW);
                        if (mirrorX) _cfg.InvJoyX = Math.Clamp(_cfg.InvJoyX - (_cfg.InvJoyW - oldW), 0.0f, 0.99f - _cfg.InvJoyW);
                        _cfg.InvJoyH = Math.Clamp(_cfg.InvJoyH + dy, 0.20f, Math.Min(0.95f, 0.99f - _cfg.InvJoyY));
                    }
                    else
                    {
                        _cfg.InvJoyX = Math.Clamp(_cfg.InvJoyX + moveDx, 0.0f, 0.99f - _cfg.InvJoyW);
                        _cfg.InvJoyY = Math.Clamp(_cfg.InvJoyY + dy, 0.0f, 0.99f - _cfg.InvJoyH);
                    }
                }
                else if (logical == EditTarget.Tool)
                {
                    if (resize)
                    {
                        float oldW = _cfg.InvToolW;
                        float maxW = mirrorX ? Math.Min(0.45f, _cfg.InvToolX + _cfg.InvToolW) : Math.Min(0.45f, 0.99f - _cfg.InvToolX);
                        _cfg.InvToolW = Math.Clamp(oldW + dx, 0.06f, maxW);
                        if (mirrorX) _cfg.InvToolX = Math.Clamp(_cfg.InvToolX - (_cfg.InvToolW - oldW), 0.01f, 0.99f - _cfg.InvToolW);
                        _cfg.InvToolH = Math.Clamp(_cfg.InvToolH + dy, 0.14f, Math.Min(0.95f, 0.99f - _cfg.InvToolY));
                    }
                    else
                    {
                        _cfg.InvToolX = Math.Clamp(_cfg.InvToolX + moveDx, 0.01f, 0.99f - _cfg.InvToolW);
                        _cfg.InvToolY = Math.Clamp(_cfg.InvToolY + dy, 0.01f, 0.99f - _cfg.InvToolH);
                    }
                }
                else if (logical == EditTarget.Act)
                {
                    if (resize)
                    {
                        float oldW = _cfg.InvActW;
                        float maxW = mirrorX ? Math.Min(0.45f, _cfg.InvActX + _cfg.InvActW) : Math.Min(0.45f, 0.99f - _cfg.InvActX);
                        _cfg.InvActW = Math.Clamp(oldW + dx, 0.06f, maxW);
                        if (mirrorX) _cfg.InvActX = Math.Clamp(_cfg.InvActX - (_cfg.InvActW - oldW), 0.01f, 0.99f - _cfg.InvActW);
                        _cfg.InvActH = Math.Clamp(_cfg.InvActH + dy, 0.14f, Math.Min(0.95f, 0.99f - _cfg.InvActY));
                    }
                    else
                    {
                        _cfg.InvActX = Math.Clamp(_cfg.InvActX + moveDx, 0.01f, 0.99f - _cfg.InvActW);
                        _cfg.InvActY = Math.Clamp(_cfg.InvActY + dy, 0.01f, 0.99f - _cfg.InvActH);
                    }
                }
            }
            else
            {
                float dr = (deltaPx.X + deltaPx.Y) * 0.35f / unit;
                bool swap = _cfg.SwapRightButtons;
                EditTarget logical = target;
                if (swap && target == EditTarget.Tool) logical = EditTarget.Act;
                else if (swap && target == EditTarget.Act) logical = EditTarget.Tool;

                if (logical == EditTarget.Joy)
                {
                    if (resize) _cfg.VisJoyR = Math.Clamp(_cfg.VisJoyR + dr, 0.07f, 0.22f);
                    else { _cfg.VisJoyX = Math.Clamp(_cfg.VisJoyX + moveDx, 0.06f, 0.94f); _cfg.VisJoyY = Math.Clamp(_cfg.VisJoyY + dy, 0.10f, 0.92f); }
                }
                else if (logical == EditTarget.Tool)
                {
                    if (resize) _cfg.VisToolR = Math.Clamp(_cfg.VisToolR + dr, 0.045f, 0.16f);
                    else { _cfg.VisToolX = Math.Clamp(_cfg.VisToolX + moveDx, 0.06f, 0.94f); _cfg.VisToolY = Math.Clamp(_cfg.VisToolY + dy, 0.10f, 0.92f); }
                }
                else if (logical == EditTarget.Act)
                {
                    if (resize) _cfg.VisActR = Math.Clamp(_cfg.VisActR + dr, 0.045f, 0.16f);
                    else { _cfg.VisActX = Math.Clamp(_cfg.VisActX + moveDx, 0.06f, 0.94f); _cfg.VisActY = Math.Clamp(_cfg.VisActY + dy, 0.10f, 0.92f); }
                }
            }
        }

        private void ApplyToPad(object pad, Vector2 stick, bool btnX, bool btnA, bool btnMenu)
        {
            EnsureBridge(pad)?.ApplyInput(pad, stick, btnX, btnA, btnMenu);
        }

        private bool HitTestToolZone(Vector2 vp, Point vpPt)
        {
            if (IsInvisibleBarMode)
                return _invToolRect.Contains(vpPt);
            float rad = _visToolRect.Width * 0.56f;
            return Vector2.DistanceSquared(vp, _visToolRect.Center.ToVector2()) <= rad * rad;
        }

        private bool HitTestActZone(Vector2 vp, Point vpPt)
        {
            if (IsInvisibleBarMode)
                return _invActRect.Contains(vpPt);
            float rad = _visActRect.Width * 0.56f;
            return Vector2.DistanceSquared(vp, _visActRect.Center.ToVector2()) <= rad * rad;
        }

        private Vector2 ComputeStick(Vector2 curVp)
        {
            Vector2 delta = curVp - _joyOrigin;
            float dist = delta.Length();
            float dz = Math.Clamp(_cfg.JoyDeadzone, 0.05f, 0.25f);
            if (dist < _activeJoyRadius * dz)
            {
                _joyKnob = curVp;
                return Vector2.Zero;
            }
            Vector2 dir = delta / Math.Max(0.0001f, dist);
            if (dist > _activeJoyRadius)
            {
                _joyKnob = _joyOrigin + dir * _activeJoyRadius;
                return dir;
            }
            _joyKnob = curVp;
            // Remap [dz .. 1.0] -> [0.42 .. 1.0] so even a gentle 45-degree diagonal pull (0.42 * 0.707 = 0.297 > 0.25)
            // ALWAYS clears SDViOSTouchControls' 0.25 DPad threshold on both axes without standing still!
            float t = Math.Clamp((dist / _activeJoyRadius - dz) / Math.Max(0.01f, 1.0f - dz), 0f, 1f);
            float mag = 0.42f + t * 0.58f;
            return dir * mag;
        }

        private bool HandlePinchZoom(TouchCollection touches, double nowSec)
        {
            int idA = -1, idB = -1;
            Vector2 posA = Vector2.Zero, posB = Vector2.Zero;
            int activeCount = 0;

            for (int i = 0; i < touches.Count; i++)
            {
                var tl = touches[i];
                if (tl.State != TouchLocationState.Pressed && tl.State != TouchLocationState.Moved)
                    continue;
                if (tl.Id == _toolTouchId || tl.Id == _interactTouchId || IsTouchOwnedByCustomButton(tl.Id))
                    return false;

                Vector2 vp = MapRawToViewport(tl.Position);
                if (activeCount == 0) { idA = tl.Id; posA = vp; activeCount = 1; }
                else if (activeCount == 1) { idB = tl.Id; posB = vp; activeCount = 2; }
                else { activeCount++; }
            }

            if (activeCount != 2)
            {
                if (_pinchDirtySave && _pendingPinchZoom >= 0.70f)
                {
                    _pinchDirtySave = false;
                    if (Game1.options != null)
                        Game1.options.desiredBaseZoomLevel = _pendingPinchZoom;
                    _bridge?.SetTargetZoomLevel(_pendingPinchZoom);
                }
                _pinching = false;
                _pinchIdA = -1;
                _pinchIdB = -1;
                return false;
            }

            if (_joyTouchId >= 0 && _joyActivated && EffectiveMode != MobileControlMode.TapToMove)
            {
                Vector2 other = (idA == _joyTouchId) ? posB : posA;
                Point opt = new Point((int)other.X, (int)other.Y);
                if (HitTestToolZone(other, opt) || HitTestActZone(other, opt))
                    return false;
            }

            float dist = Vector2.Distance(posA, posB);
            if (!_pinching || _pinchIdA != idA || _pinchIdB != idB)
            {
                if (dist < 40f)
                    return false;
                _pinching = true;
                _pinchIdA = idA;
                _pinchIdB = idB;
                _pinchStartDist = dist;
                _pinchStartZoom = Game1.options != null ? Game1.options.desiredBaseZoomLevel : 1.0f;
                _pendingPinchZoom = _pinchStartZoom;
                _pinchDirtySave = false;
                _lastPinchApplySec = nowSec;
                return true;
            }

            if (_pinchStartDist > 20f && Game1.options != null)
            {
                float ratio = dist / _pinchStartDist;
                // Quantize to smooth 0.05 steps & throttle updates to ~16Hz to avoid 50x Window_ClientSizeChanged spam!
                float newZoom = Math.Clamp(_pinchStartZoom * ratio, 0.75f, 2.0f);
                newZoom = (float)Math.Round(newZoom * 20f) / 20f;
                _pendingPinchZoom = newZoom;
                if (Math.Abs(newZoom - Game1.options.desiredBaseZoomLevel) >= 0.045f && (nowSec - _lastPinchApplySec) >= 0.06)
                {
                    _lastPinchApplySec = nowSec;
                    Game1.options.desiredBaseZoomLevel = newZoom;
                    _pinchDirtySave = true;
                }
            }
            return true;
        }

        private bool TryHandleHudTap(Vector2 vp, out bool openedMenu, out int tappedToolbarSlot)
        {
            openedMenu = false;
            tappedToolbarSlot = -1;
            if (Game1.onScreenMenus == null || Game1.player == null)
                return false;

            Point uiPt = ViewportToUi(vp);

            foreach (IClickableMenu menu in Game1.onScreenMenus)
            {
                if (menu is Toolbar tb && tb.buttons != null)
                {
                    for (int i = 0; i < tb.buttons.Count && i < 12; i++)
                    {
                        ClickableComponent btn = tb.buttons[i];
                        if (btn != null && btn.bounds.Contains(uiPt))
                        {
                            tappedToolbarSlot = i;
                            return true;
                        }
                    }
                }
                else if (menu is DayTimeMoneyBox dt)
                {
                    if (dt.questButton != null && dt.questButton.bounds.Contains(uiPt))
                    {
                        try { dt.receiveLeftClick(uiPt.X, uiPt.Y, true); } catch { }
                        openedMenu = (Game1.activeClickableMenu != null);
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryGetToolbarViewportBounds(out Rectangle tbVpRect)
        {
            tbVpRect = Rectangle.Empty;
            if (Game1.onScreenMenus == null) return false;
            foreach (IClickableMenu menu in Game1.onScreenMenus)
            {
                if (menu is Toolbar tb && tb.buttons != null && tb.buttons.Count >= 12)
                {
                    Rectangle first = tb.buttons[0].bounds;
                    Rectangle last = tb.buttons[11].bounds;
                    if (first.Width <= 0 || last.Width <= 0) return false;
                    Vector2 tl = UiToViewport(new Point(first.X, first.Y));
                    Vector2 br = UiToViewport(new Point(last.Right, last.Bottom));
                    tbVpRect = new Rectangle((int)tl.X, (int)tl.Y, Math.Max(1, (int)(br.X - tl.X)), Math.Max(1, (int)(br.Y - tl.Y)));
                    return true;
                }
            }
            return false;
        }

        private void HandleWorldTap(Vector2 vp)
        {
            Farmer who = Game1.player;
            GameLocation loc = Game1.currentLocation;
            if (who == null || loc == null)
                return;

            // Instant Hook / Reel when fishing in Tap-to-Move or Hybrid mode (tap anywhere on screen!)
            if (who.CurrentTool is FishingRod fr && (fr.isFishing || fr.isNibbling))
            {
                ClearPath();
                _actionPulseXFrames = 3;
                return;
            }

            Vector2 worldPos = ViewportToWorld(vp);
            Point tappedTile = new Point((int)Math.Floor(worldPos.X / 64f), (int)Math.Floor(worldPos.Y / 64f));
            Point playerTile = who.TilePoint;
            int cheb = Math.Max(Math.Abs(playerTile.X - tappedTile.X), Math.Abs(playerTile.Y - tappedTile.Y));

            // Tap directly on farmer (cheb == 0):
            // - If currently A* walking -> cleanly stop walking (never accidentally eat food!)
            // - If standing still -> Dismount Horse OR Eat/Drink/Trigger usable item (Food, Coffee, Bomb, Staircase, Totem)
            if (cheb == 0)
            {
                if (_path != null && _path.Count > 0)
                {
                    ClearPath();
                    return;
                }
                if (who.isRidingHorse())
                {
                    ClearPath();
                    _actionPulseAFrames = 1;
                    return;
                }
                if (IsSelfUsableObject(who.ActiveObject))
                {
                    ClearPath();
                    _actionPulseAFrames = 1;
                    return;
                }
            }

            TileIntent intent = AutoToolAndCombat.InspectTile(loc, tappedTile, who, _cfg.AutoTool);

            // Check interior/exterior loc.doors (e.g. villager bedroom doors) if not already an Action tile
            if (intent.Kind == TileActionKind.None)
            {
                try
                {
                    if (loc.doors != null && loc.doors.ContainsKey(tappedTile))
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = true;
                    }
                }
                catch { }
            }

            Point actionTile = intent.HasResolvedTile ? intent.ResolvedTile : tappedTile;
            int actionCheb = Math.Max(Math.Abs(playerTile.X - actionTile.X), Math.Abs(playerTile.Y - actionTile.Y));
            int actionManh = Math.Abs(playerTile.X - actionTile.X) + Math.Abs(playerTile.Y - actionTile.Y);

            // Detect Exit / Map-Edge Warps (FarmHouse exit rug, Shops exit, Coops/Barns exit, Mines, Map Exits!)
            // Building entry doors (Farmhouse, Coop, Barn, Shed, Town houses) are handled via Action/Building.doAction above!
            bool tappedWarp = false;
            Point warpGoal = tappedTile;
            if (intent.Kind == TileActionKind.None && TryFindNearbyWarp(loc, tappedTile, out Point foundWarp))
            {
                tappedWarp = true;
                warpGoal = foundWarp;
                intent.StopAdjacent = false;
            }

            bool closeEnoughNow = intent.PreferCardinal
                ? (actionManh == 1)
                : (actionCheb <= 1);
            if (!tappedWarp && intent.Kind != TileActionKind.None && closeEnoughNow)
            {
                ClearPath();
                TriggerTileIntent(who, actionTile, intent);
                return;
            }

            // Tap empty adjacent tile while holding a placeable object (Chest, Keg, Fence, Path, Sprinkler, Furniture, Bomb, etc.)!
            if (!tappedWarp && intent.Kind == TileActionKind.None && cheb == 1 && who.ActiveObject != null &&
                (who.ActiveObject.isPlaceable() || who.ActiveObject.bigCraftable.Value))
            {
                ClearPath();
                who.faceGeneralDirection(new Vector2(tappedTile.X * 64f + 32f, tappedTile.Y * 64f + 32f));
                _actionPulseAFrames = 1;
                return;
            }

            Point pathGoal = tappedWarp ? FindWarpApproachTile(loc, warpGoal, playerTile, who) : actionTile;
            List<Point> path = PathFinder.FindPath(loc, playerTile, pathGoal, who, intent.StopAdjacent, intent.PreferCardinal);
            if (path == null)
            {
                // Even if A* couldn't path directly to an out-of-bounds door edge, if player is right beside the warp, push through!
                if (tappedWarp && Math.Max(Math.Abs(playerTile.X - warpGoal.X), Math.Abs(playerTile.Y - warpGoal.Y)) <= 2)
                {
                    ClearPath();
                    _pendingWarpTile = warpGoal;
                    _warpPushFrames = 28;
                    _warpPushStartHudCount = Game1.hudMessages?.Count ?? 0;
                }
                return;
            }

            if (path.Count == 0)
            {
                if (tappedWarp)
                {
                    ClearPath();
                    _pendingWarpTile = warpGoal;
                    _warpPushFrames = 28;
                    _warpPushStartHudCount = Game1.hudMessages?.Count ?? 0;
                    return;
                }
                if (intent.Kind != TileActionKind.None)
                {
                    TriggerTileIntent(who, actionTile, intent);
                    return;
                }
            }

            _path = path;
            _pathIdx = 0;
            _targetTile = tappedWarp ? warpGoal : actionTile;
            _hasTargetTile = true;
            _pendingIntent = intent;
            _hasPendingWarp = tappedWarp;
            _pendingWarpTile = warpGoal;
            _warpPushFrames = 0;
            _lastPlayerPos = who.Position;
            _stuckFrames = 0;
        }

        private static bool IsWaterInFishingCastReach(GameLocation loc, Point playerTile, Point tappedTile)
        {
            if (loc == null) return false;
            try
            {
                // Must tap directly on water — OR on an impassable shoreline/cliff edge 1 tile beside water.
                // NEVER hijack taps on walkable piers, wooden docks, bridges, or beach floor!
                bool tapIsWater = loc.isWaterTile(tappedTile.X, tappedTile.Y);
                if (!tapIsWater)
                {
                    if (PathFinder.IsTileWalkable(loc, tappedTile.X, tappedTile.Y, Game1.player))
                        return false;

                    tapIsWater = loc.isWaterTile(tappedTile.X + 1, tappedTile.Y) ||
                                 loc.isWaterTile(tappedTile.X - 1, tappedTile.Y) ||
                                 loc.isWaterTile(tappedTile.X, tappedTile.Y + 1) ||
                                 loc.isWaterTile(tappedTile.X, tappedTile.Y - 1);
                    if (!tapIsWater) return false;
                }

                int cheb = Math.Max(Math.Abs(playerTile.X - tappedTile.X), Math.Abs(playerTile.Y - tappedTile.Y));
                if (cheb <= 2)
                    return true;
                if (cheb > 8)
                    return false;

                // Player tapped water 3..8 tiles away: start immediate charging ONLY if player is already standing
                // at the shore/dock/bridge (i.e. water exists within 1..3 tiles in the tapped direction).
                int dx = Math.Sign(tappedTile.X - playerTile.X);
                int dy = Math.Sign(tappedTile.Y - playerTile.Y);
                for (int step = 1; step <= 3; step++)
                {
                    if (loc.isWaterTile(playerTile.X + dx * step, playerTile.Y + dy * step))
                        return true;
                    if (dx != 0 && dy != 0)
                    {
                        if (loc.isWaterTile(playerTile.X + dx * step, playerTile.Y) ||
                            loc.isWaterTile(playerTile.X, playerTile.Y + dy * step))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // For unwalkable/out-of-bounds door warps (e.g. FarmHouse exit (3,12) or Building humanDoor),
        // pick the walkable CARDINAL tile directly in front of the door (e.g. rug (3,11)) so A* walks
        // squarely onto the rug instead of stopping diagonally against the wall!
        private static Point FindWarpApproachTile(GameLocation loc, Point warpGoal, Point playerTile, Farmer who)
        {
            if (loc == null || PathFinder.IsTileWalkable(loc, warpGoal.X, warpGoal.Y, who))
                return warpGoal;

            Point[] cardinals =
            {
                new Point(warpGoal.X, warpGoal.Y - 1),
                new Point(warpGoal.X, warpGoal.Y + 1),
                new Point(warpGoal.X - 1, warpGoal.Y),
                new Point(warpGoal.X + 1, warpGoal.Y)
            };

            Point best = warpGoal;
            int bestDistSq = int.MaxValue;
            for (int i = 0; i < cardinals.Length; i++)
            {
                Point c = cardinals[i];
                if (!PathFinder.IsTileWalkable(loc, c.X, c.Y, who))
                    continue;
                int dx = c.X - playerTile.X;
                int dy = c.Y - playerTile.Y;
                int dSq = dx * dx + dy * dy;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = c;
                }
            }
            return best;
        }

        private static bool TryFindNearbyWarp(GameLocation loc, Point tappedTile, out Point warpTile)
        {
            warpTile = tappedTile;
            if (loc == null)
                return false;

            try
            {
                int mapW = loc.Map?.Layers?[0]?.LayerWidth ?? 0;
                int mapH = loc.Map?.Layers?[0]?.LayerHeight ?? 0;
                bool tapOutOrBlocked = tappedTile.X < 0 || tappedTile.Y < 0 || tappedTile.X >= mapW || tappedTile.Y >= mapH ||
                                       !PathFinder.IsTileWalkable(loc, tappedTile.X, tappedTile.Y, Game1.player);

                int bestDistSq = int.MaxValue;
                bool found = false;

                if (loc.warps != null)
                {
                    for (int i = 0; i < loc.warps.Count; i++)
                    {
                        Warp w = loc.warps[i];
                        if (w == null || w.npcOnly.Value)
                            continue;
                        int dx = Math.Abs(w.X - tappedTile.X);
                        int dy = Math.Abs(w.Y - tappedTile.Y);
                        int cheb = Math.Max(dx, dy);

                        bool match;
                        if (cheb == 0)
                        {
                            match = true;
                        }
                        else if (tapOutOrBlocked)
                        {
                            match = (cheb <= 2);
                        }
                        else
                        {
                            // Walkable tap (e.g. FarmHouse exit rug (3,11)): ONLY match a cardinally adjacent
                            // unwalkable/out-of-bounds warp (like (3,12)) — never accidentally trigger open road edges!
                            match = (dx + dy == 1) && !PathFinder.IsTileWalkable(loc, w.X, w.Y, Game1.player);
                        }

                        if (!match) continue;
                        int dSq = dx * dx + dy * dy;
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            warpTile = new Point(w.X, w.Y);
                            found = true;
                        }
                    }
                }

                return found;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSelfUsableObject(StardewValley.Object obj)
        {
            if (obj == null) return false;
            if (obj.Edibility > -300) return true; // Any edible food or drink (Coffee, Salad, Cheese, Elixir, etc.)
            string name = obj.Name ?? "";
            if (name.IndexOf("Totem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Bomb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Staircase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Tent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Book", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Ticket", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return obj.isPlaceable();
        }

        private Vector2 StepPathFollower()
        {
            Farmer who = Game1.player;
            if (who == null || _path == null || _pathIdx >= _path.Count)
            {
                CompletePathArrival();
                return Vector2.Zero;
            }

            if (Vector2.DistanceSquared(who.Position, _lastPlayerPos) < 4f)
            {
                // 36 ticks (> Auto-Unstick's 18 ticks!) so Auto-Unstick rescues a frozen farmer BEFORE A* cancels!
                if (++_stuckFrames > 36)
                {
                    bool hadWarp = _hasPendingWarp;
                    Point warpT = _pendingWarpTile;
                    ClearPath();
                    if (hadWarp && Math.Max(Math.Abs(who.TilePoint.X - warpT.X), Math.Abs(who.TilePoint.Y - warpT.Y)) <= 2)
                    {
                        _pendingWarpTile = warpT;
                        _warpPushFrames = 26;
                        _warpPushStartHudCount = Game1.hudMessages?.Count ?? 0;
                    }
                    return Vector2.Zero;
                }
            }
            else
            {
                _lastPlayerPos = who.Position;
                _stuckFrames = 0;
            }

            Point wp = _path[_pathIdx];
            Vector2 wpCenter = new Vector2(wp.X * 64f + 32f, wp.Y * 64f + 32f);
            Vector2 playerCenter = who.GetBoundingBox().Center.ToVector2();
            Vector2 diff = wpCenter - playerCenter;

            if (diff.Length() <= 16f)
            {
                _pathIdx++;
                if (_pathIdx >= _path.Count)
                {
                    CompletePathArrival();
                    return Vector2.Zero;
                }
                wp = _path[_pathIdx];
                wpCenter = new Vector2(wp.X * 64f + 32f, wp.Y * 64f + 32f);
                diff = wpCenter - playerCenter;
            }

            if (diff.LengthSquared() < 1f)
                return Vector2.Zero;

            diff.Normalize();
            return diff;
        }

        private void CompletePathArrival()
        {
            Farmer who = Game1.player;
            Point target = _targetTile;
            TileIntent intent = _pendingIntent;
            bool hadWarp = _hasPendingWarp;
            Point warpTile = _pendingWarpTile;
            ClearPath();

            if (who == null)
                return;

            if (hadWarp)
            {
                // Walk straight into the Warp tile so GameLocation.isCollidingWithWarp exits doors reliably!
                _pendingWarpTile = warpTile;
                _warpPushFrames = 28;
                _warpPushStartHudCount = Game1.hudMessages?.Count ?? 0;
                return;
            }

            if (intent.Kind != TileActionKind.None)
            {
                int cheb = Math.Max(Math.Abs(who.TilePoint.X - target.X), Math.Abs(who.TilePoint.Y - target.Y));
                if (cheb <= 1)
                    TriggerTileIntent(who, target, intent);
            }
            else if (intent.StopAdjacent)
            {
                // e.g., walked to water shoreline while holding FishingRod -> face the water ready to hold-charge!
                who.faceGeneralDirection(new Vector2(target.X * 64f + 32f, target.Y * 64f + 32f));
            }
        }

        private void TriggerTileIntent(Farmer who, Point tile, TileIntent intent)
        {
            if (intent.PreferredToolSlot >= 0 && intent.PreferredToolSlot < who.Items.Count)
            {
                if (_cfg.RestoreToolAfterAuto && who.CurrentToolIndex != intent.PreferredToolSlot && _autoToolPrevSlot < 0)
                {
                    _autoToolPrevSlot = who.CurrentToolIndex;
                    _autoToolRestoreTimer = 36;
                }
                who.CurrentToolIndex = intent.PreferredToolSlot;
            }

            Vector2 targetCenter = new Vector2(tile.X * 64f + 32f, tile.Y * 64f + 32f);
            who.faceGeneralDirection(targetCenter);

            if (intent.Kind == TileActionKind.UseTool)
            {
                _actionPulseXFrames = 2;
                if (_cfg.AutoRepeat && intent.Repeatable)
                {
                    _repeatActive = true;
                    _repeatTile = tile;
                    _repeatCooldown = 8;
                }
            }
            else if (intent.Kind == TileActionKind.Interact)
            {
                // Directly invoke loc.checkAction ONCE (identical to PC right-click!) so Mailboxes, Building doors
                // (Farmhouse/Coop/Barn/Shed), Town shops/LockedDoorWarp, TV, Calendar, and Shipping Bins trigger
                // cleanly ONCE without ever missing on diagonals or spamming 3x locked-door/eat dialogues!
                bool handled = false;
                if (Game1.currentLocation != null &&
                    Game1.activeClickableMenu == null &&
                    !Game1.dialogueUp && !Game1.eventUp && !Game1.fadeToBlack)
                {
                    try
                    {
                        handled = Game1.currentLocation.checkAction(
                            new xTile.Dimensions.Location(tile.X, tile.Y),
                            Game1.viewport,
                            who);
                    }
                    catch { }
                }

                if (!handled)
                    _actionPulseAFrames = 1;
            }
        }

        // =========================================================================
        // STARDEW-PIXEL MULTI-SLOT SAVE MENU (Replaces legacy MODS drawer!)
        // =========================================================================
        private const int SaveVisibleRows = 4;

        private void OpenSaveMenu()
        {
            ResetGameplayState();
            _saveMenuOpen = true;
            _saveSwipeTouchId = -1;
            _saveSwipeMoved = false;
            var slots = _saveSlots.GetSlotsForCurrentSave(forceRefresh: true);
            _saveSelectedIdx = slots.Count > 0 ? 0 : -1;
            _saveScrollIdx = 0;
            try { Game1.playSound("bigSelect"); } catch { }
        }

        private Rectangle GetSaveSlotRowRect(int visibleRowIdx)
        {
            int gap = Math.Max(6, _saveListRect.Height / 40);
            int rowH = Math.Max(36, (_saveListRect.Height - gap * (SaveVisibleRows + 1)) / SaveVisibleRows);
            int y = _saveListRect.Y + gap + visibleRowIdx * (rowH + gap);
            return new Rectangle(_saveListRect.X + gap, y, _saveListRect.Width - gap * 2, rowH);
        }

        private void UpdateSaveMenuTouches(object pad, PadBridge br, TouchCollection touches, double nowSec)
        {
            var slots = _saveSlots.GetSlotsForCurrentSave();
            int maxScroll = Math.Max(0, slots.Count - SaveVisibleRows);
            _saveScrollIdx = Math.Clamp(_saveScrollIdx, 0, maxScroll);
            if (_saveSelectedIdx >= slots.Count) _saveSelectedIdx = slots.Count - 1;

            if (touches.Count == 0)
            {
                _saveSwipeTouchId = -1;
                _saveSwipeMoved = false;
                return;
            }

            for (int i = 0; i < touches.Count; i++)
            {
                TouchLocation tl = touches[i];
                Vector2 vp = MapRawToViewport(tl.Position);
                Point pt = new Point((int)vp.X, (int)vp.Y);

                if (tl.State == TouchLocationState.Pressed)
                {
                    if (_saveCloseRect.Contains(pt) || !_savePanelRect.Contains(pt))
                    {
                        _saveMenuOpen = false;
                        try { Game1.playSound("bigDeSelect"); } catch { }
                        return;
                    }
                    if (_saveNewBtnRect.Contains(pt))
                    {
                        _saveSlots.BeginPendingSnapshot(br);
                        bool ok = br != null && br.TryQuickSave(out string msg);
                        if (!ok)
                        {
                            _saveSlots.CancelPendingSnapshot();
                            ShowSaveToast(br != null ? "KHONG THE LUU LUC NAY" : "QUICKSAVE LOI", nowSec);
                            try { Game1.playSound("cancel"); } catch { }
                        }
                        else
                        {
                            ShowSaveToast("DANG LUU BAN MOI...", nowSec);
                            _saveMenuOpen = false;
                            try { Game1.playSound("coin"); } catch { }
                        }
                        return;
                    }
                    if (_saveLoadBtnRect.Contains(pt))
                    {
                        if (_saveSelectedIdx >= 0 && _saveSelectedIdx < slots.Count)
                        {
                            bool ok = _saveSlots.RestoreAndLoadSlot(slots[_saveSelectedIdx], br, out string msg);
                            ShowSaveToast(msg, nowSec);
                            if (ok)
                            {
                                _saveMenuOpen = false;
                                try { Game1.playSound("bigSelect"); } catch { }
                            }
                            else
                            {
                                try { Game1.playSound("cancel"); } catch { }
                            }
                        }
                        else if (br != null && br.HasQuickSave())
                        {
                            bool ok = br.TryQuickLoad(out string msg);
                            ShowSaveToast(msg, nowSec);
                            if (ok) _saveMenuOpen = false;
                        }
                        else
                        {
                            ShowSaveToast("CHON 1 BAN LUU DE TAI", nowSec);
                            try { Game1.playSound("cancel"); } catch { }
                        }
                        return;
                    }
                    if (_saveDelBtnRect.Contains(pt))
                    {
                        if (_saveSelectedIdx >= 0 && _saveSelectedIdx < slots.Count)
                        {
                            if (_saveSlots.DeleteSlot(slots[_saveSelectedIdx]))
                            {
                                var updated = _saveSlots.GetSlotsForCurrentSave();
                                _saveSelectedIdx = updated.Count > 0 ? Math.Min(_saveSelectedIdx, updated.Count - 1) : -1;
                                _saveScrollIdx = Math.Clamp(_saveScrollIdx, 0, Math.Max(0, updated.Count - SaveVisibleRows));
                                ShowSaveToast("DA XOA BAN LUU", nowSec);
                                try { Game1.playSound("trashcan"); } catch { }
                            }
                        }
                        return;
                    }
                    if (_saveUpBtnRect.Contains(pt))
                    {
                        if (_saveScrollIdx > 0) { _saveScrollIdx--; try { Game1.playSound("shwip"); } catch { } }
                        return;
                    }
                    if (_saveDnBtnRect.Contains(pt))
                    {
                        if (_saveScrollIdx < maxScroll) { _saveScrollIdx++; try { Game1.playSound("shwip"); } catch { } }
                        return;
                    }
                    if (_saveListRect.Contains(pt) && _saveSwipeTouchId < 0)
                    {
                        _saveSwipeTouchId = tl.Id;
                        _saveSwipeStartY = vp.Y;
                        _saveSwipeLastY = vp.Y;
                        _saveSwipeAccumY = 0f;
                        _saveSwipeMoved = false;
                    }
                }
                else if (tl.State == TouchLocationState.Moved && tl.Id == _saveSwipeTouchId)
                {
                    float dy = vp.Y - _saveSwipeLastY;
                    _saveSwipeLastY = vp.Y;
                    if (Math.Abs(vp.Y - _saveSwipeStartY) > 18f)
                        _saveSwipeMoved = true;
                    if (_saveSwipeMoved)
                    {
                        _saveSwipeAccumY += dy;
                        float step = Math.Max(34f, _saveListRect.Height / 5f);
                        while (Math.Abs(_saveSwipeAccumY) >= step)
                        {
                            int dir = _saveSwipeAccumY > 0 ? -1 : 1;
                            _saveSwipeAccumY -= Math.Sign(_saveSwipeAccumY) * step;
                            _saveScrollIdx = Math.Clamp(_saveScrollIdx + dir, 0, maxScroll);
                        }
                    }
                }
                else if ((tl.State == TouchLocationState.Released || tl.State == TouchLocationState.Invalid) && tl.Id == _saveSwipeTouchId)
                {
                    if (!_saveSwipeMoved && _saveListRect.Contains(pt))
                    {
                        for (int r = 0; r < SaveVisibleRows; r++)
                        {
                            int idx = _saveScrollIdx + r;
                            if (idx >= slots.Count) break;
                            if (GetSaveSlotRowRect(r).Contains(pt))
                            {
                                _saveSelectedIdx = idx;
                                try { Game1.playSound("smallSelect"); } catch { }
                                break;
                            }
                        }
                    }
                    _saveSwipeTouchId = -1;
                    _saveSwipeMoved = false;
                }
            }
        }

        private void DrawStardewPixelBox(SpriteBatch sb, Rectangle r, Color fill, Color outerBorder, Color innerBorder, int px)
        {
            sb.Draw(_pixel, r, fill);
            DrawRectOutline(sb, r, px, outerBorder);
            if (r.Width > px * 4 && r.Height > px * 4)
                DrawRectOutline(sb, new Rectangle(r.X + px, r.Y + px, r.Width - px * 2, r.Height - px * 2), px, innerBorder);
        }

        private void DrawStardewPixelButton(SpriteBatch sb, Rectangle r, string text, Color fill, Color hi, Color lo, Color ink, int fontSize, int px)
        {
            sb.Draw(_pixel, r, fill);
            sb.Draw(_pixel, new Rectangle(r.X + px, r.Y + px, r.Width - px * 2, px), hi);
            sb.Draw(_pixel, new Rectangle(r.X + px, r.Bottom - px * 2, r.Width - px * 2, px), lo);
            DrawRectOutline(sb, r, px, new Color(74, 37, 17));
            _bridge?.DrawCenteredString(sb, _pixel, text, r, ink, fontSize);
        }

        private static void DrawCenteredGameText(SpriteBatch sb, Texture2D pixel, PadBridge bridge, string text, Rectangle r, Color color, float scale, int fallbackFs)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                if (Game1.smallFont != null)
                {
                    Vector2 sz = Game1.smallFont.MeasureString(text) * scale;
                    if (sz.X > r.Width - 10 && sz.X > 1f)
                    {
                        float fit = (r.Width - 10f) / sz.X;
                        scale *= fit;
                        sz *= fit;
                    }
                    Vector2 pos = new Vector2(r.X + (r.Width - sz.X) * 0.5f, r.Y + (r.Height - sz.Y) * 0.5f + 1f);
                    Utility.drawTextWithShadow(sb, text, Game1.smallFont, pos, color, scale);
                    return;
                }
            }
            catch { }
            bridge?.DrawCenteredString(sb, pixel, text, r, color, fallbackFs);
        }

        private void DrawSaveMenuModal(SpriteBatch sb)
        {
            int px = Math.Max(2, (int)Math.Round(_vpH / 320f));
            sb.Draw(_pixel, new Rectangle(0, 0, _vpW, _vpH), Color.Black * 0.54f);

            // Native Stardew 9-slice dialog panel (Game1.menuTexture — automatically skinned by Cat Valley UI & UI mods!)
            PixelUi.DrawNativeDialogBox(sb, _pixel, _savePanelRect, 1f, drawShadow: true);

            float fBase = Math.Clamp(_vpH / 820f, 0.72f, 1.45f);
            int titleFs = Math.Max(2, _vpH / 230);
            int btnFs = Math.Max(1, _vpH / 290);
            int rowFs = Math.Max(1, _vpH / 320);
            Color ink = Game1.textColor;

            var slots = _saveSlots.GetSlotsForCurrentSave();
            string title = $"QUẢN LÝ LƯU NHANH ({slots.Count})";
            var hdrRect = new Rectangle(_savePanelRect.X + px * 8, _savePanelRect.Y + px * 4, _savePanelRect.Width - _saveCloseRect.Width - px * 18, _saveCloseRect.Height);
            DrawCenteredGameText(sb, _pixel, _bridge, title, hdrRect, ink, fBase * 1.02f, titleFs);

            PixelUi.DrawNativeCloseX(sb, _saveCloseRect, 1f);

            // Native Stardew recessed list container (Game1.mouseCursors)
            PixelUi.DrawNativeSubBox(sb, _pixel, _saveListRect, Color.White, 0.92f);

            if (slots.Count == 0)
            {
                var empty1 = new Rectangle(_saveListRect.X + px * 4, _saveListRect.Y + _saveListRect.Height / 3, _saveListRect.Width - px * 8, _saveListRect.Height / 5);
                var empty2 = new Rectangle(_saveListRect.X + px * 4, empty1.Bottom + px * 2, _saveListRect.Width - px * 8, _saveListRect.Height / 5);
                DrawCenteredGameText(sb, _pixel, _bridge, "Chưa có bản lưu nhanh nào", empty1, ink, fBase * 0.90f, btnFs);
                DrawCenteredGameText(sb, _pixel, _bridge, "Bấm '+ Lưu mới' để tạo mốc thời gian", empty2, ink * 0.82f, fBase * 0.78f, rowFs);
            }
            else
            {
                for (int r = 0; r < SaveVisibleRows; r++)
                {
                    int idx = _saveScrollIdx + r;
                    if (idx >= slots.Count) break;
                    SaveSlotInfo slot = slots[idx];
                    Rectangle rowRect = GetSaveSlotRowRect(r);
                    bool sel = (idx == _saveSelectedIdx);

                    PixelUi.DrawNativeSubBox(sb, _pixel, rowRect, sel ? new Color(255, 240, 188) : Color.White, sel ? 1f : 0.90f);

                    int pad = px * 4;
                    int halfH = Math.Max(14, (rowRect.Height - pad * 2) / 2);
                    var topLine = new Rectangle(rowRect.X + pad, rowRect.Y + pad, rowRect.Width - pad * 2, halfH);
                    var subLine = new Rectangle(rowRect.X + pad, topLine.Bottom, rowRect.Width - pad * 2, rowRect.Height - pad * 2 - halfH);

                    string prefix = slot.IsAutoSave ? "" : $"#{slots.Count - idx} ";
                    DrawCenteredGameText(sb, _pixel, _bridge, prefix + slot.FormatHeaderLine(), topLine, ink, fBase * 0.80f, rowFs);
                    DrawCenteredGameText(sb, _pixel, _bridge, slot.FormatDetailLine(), subLine, ink * 0.84f, fBase * 0.72f, Math.Max(1, rowFs));
                }
            }

            // Native Stardew Up/Down scroll buttons (Game1.mouseCursors arrows!)
            bool canUp = _saveScrollIdx > 0;
            bool canDn = _saveScrollIdx + SaveVisibleRows < slots.Count;
            PixelUi.DrawNativeButton(sb, _pixel, _bridge, _saveUpBtnRect, null, Color.White * (canUp ? 1f : 0.50f), fBase * 0.86f, _vpH, drawShadow: false);
            PixelUi.DrawNativeButton(sb, _pixel, _bridge, _saveDnBtnRect, null, Color.White * (canDn ? 1f : 0.50f), fBase * 0.86f, _vpH, drawShadow: false);
            try
            {
                if (Game1.mouseCursors != null)
                {
                    int arrSz = Math.Max(18, Math.Min(_saveUpBtnRect.Width, _saveUpBtnRect.Height) / 2);
                    var upR = new Rectangle(_saveUpBtnRect.Center.X - arrSz / 2, _saveUpBtnRect.Center.Y - arrSz / 2, arrSz, arrSz);
                    var dnR = new Rectangle(_saveDnBtnRect.Center.X - arrSz / 2, _saveDnBtnRect.Center.Y - arrSz / 2, arrSz, arrSz);
                    sb.Draw(Game1.mouseCursors, upR, new Rectangle(421, 459, 11, 12), Color.White * (canUp ? 1f : 0.45f));
                    sb.Draw(Game1.mouseCursors, dnR, new Rectangle(421, 472, 11, 12), Color.White * (canDn ? 1f : 0.45f));
                }
            }
            catch { }

            // Native Stardew bottom action buttons (+ Lưu mới | Tải bản này | Xóa)
            bool hasSel = (_saveSelectedIdx >= 0 && _saveSelectedIdx < slots.Count);
            PixelUi.DrawNativeButton(sb, _pixel, _bridge, _saveNewBtnRect, "+ Lưu mới", new Color(234, 255, 232), fBase * 0.90f, _vpH);
            PixelUi.DrawNativeButton(sb, _pixel, _bridge, _saveLoadBtnRect, "Tải bản này", hasSel ? new Color(255, 246, 218) : (Color.White * 0.55f), fBase * 0.90f, _vpH);
            PixelUi.DrawNativeButton(sb, _pixel, _bridge, _saveDelBtnRect, "Xóa", hasSel ? new Color(255, 232, 228) : (Color.White * 0.55f), fBase * 0.90f, _vpH);
        }

        // =========================================================================
        // RENDERING (Gameplay Overlay + Custom PC Keys + Save Menu + Edit Layout)
        // =========================================================================
        public void DrawGameplayOverlay(SpriteBatch sb)
        {
            if (sb == null || sb.GraphicsDevice == null)
                return;

            EnsureTextures(sb.GraphicsDevice);
            if (!RefreshViewport()) return;
            ComputeLayout();
            PixelUi.GlobalOpacity = _cfg.PixelOpacity;

            if (MobileMinigamePad.DetectCurrentArcadeMinigame() != MobileMinigamePad.ArcadeKind.None)
            {
                _minigamePad.Draw(sb, _pixel, _circle, _ring, _bridge, _vpW, _vpH);
                return;
            }

            if (_editMode)
            {
                DrawEditLayoutOverlay(sb);
                return;
            }

            // 1. Tap-to-Move Crisp 16-Bit Pixel Target Tile Brackets (subtle, non-distracting!)
            if (_hasTargetTile && _path != null && Game1.currentLocation != null)
            {
                Vector2 tl = WorldToViewport(new Vector2(_targetTile.X * 64f, _targetTile.Y * 64f));
                Vector2 br = WorldToViewport(new Vector2((_targetTile.X + 1) * 64f, (_targetTile.Y + 1) * 64f));
                int w = Math.Max(12, (int)(br.X - tl.X));
                int h = Math.Max(12, (int)(br.Y - tl.Y));
                var tileRect = new Rectangle((int)tl.X, (int)tl.Y, w, h);
                PixelUi.DrawPixelTileTarget(sb, _pixel, tileRect, new Color(118, 226, 128), 0.14f);
            }

            // Hide GUI mode (1-tap HIDE -> clean full-screen view; double-tap anywhere restores GUI!)
            // Also Auto-Hide all touch buttons while physical Bluetooth GamePad is active!
            if ((_guiHidden || _gamepadHideHud) && !_saveMenuOpen)
            {
                double gNow = Environment.TickCount64 / 1000.0;
                if (!string.IsNullOrEmpty(_saveToastText) && gNow < _saveToastUntil)
                    DrawToastBanner(sb, gNow < _ramWarningUntil);
                return;
            }

            // 1b. Optional Full-Height Stardew Mobile Vertical Scrollable 36-Slot Toolbar
            if (_cfg.VerticalToolbar && !_saveMenuOpen)
            {
                _verticalToolbar.Draw(sb, _pixel, _vpH, _cfg.ToolbarOpacity);
            }

            // 2. Collapsible Top Utility Bar (BAG | MAP | QST | SAVE | KEY | [Emote] | [CTL] | [CHAT] | HIDE | EDIT | <)
            double nowSec = Environment.TickCount64 / 1000.0;
            bool isRamWarn = nowSec < _ramWarningUntil;
            if (_cfg.ShowTopUtility)
            {
                DrawMinimalPill(sb, _btnBagRect, "BAG", false);
                if (!_topBarCollapsed)
                {
                    DrawMinimalPill(sb, _btnMapRect, "MAP", false);
                    DrawMinimalPill(sb, _btnQuestRect, "QST", false);
                    if (isRamWarn && !_saveMenuOpen)
                    {
                        double rate = _ramCritical ? 6.0 : 3.0;
                        bool pulse = ((int)(nowSec * rate) % 2) == 0;
                        PixelUi.DrawPixelPill(sb, _pixel, _bridge, _btnSaveRect, "SAVE!",
                            _ramCritical ? PixelRole.Special : PixelRole.Warn, pulse, _vpH, -1, 0.96f);
                    }
                    else
                    {
                        DrawMinimalPill(sb, _btnSaveRect, "SAVE", _saveMenuOpen);
                    }
                    DrawMinimalPill(sb, _btnKeyRect, "KEY", false);
                    if (!_btnEmoteRect.IsEmpty)
                        PixelUi.DrawPixelSmileyButton(sb, _pixel, _btnEmoteRect, false, _vpH);
                    if (!_btnCtlModeRect.IsEmpty)
                        DrawMinimalPill(sb, _btnCtlModeRect, "CTL", _inCombatAutoSwitched);
                    if (!_btnChatRect.IsEmpty)
                        DrawMinimalPill(sb, _btnChatRect, "CHAT", false);
                    if (!_btnHideGuiRect.IsEmpty)
                        DrawMinimalPill(sb, _btnHideGuiRect, "HIDE", false);
                    DrawMinimalPill(sb, _btnEditRect, "EDIT", false);
                }
                DrawMinimalPill(sb, _btnCollapseRect, _topBarCollapsed ? ">" : "<", false);
            }

            // 3. Mode-specific Pixel-Art Overlay (subtle & low-opacity idle -> zero distraction!)
            if (EffectiveMode == MobileControlMode.VisibleJoystick2Btn)
            {
                bool joyAct = (_joyTouchId >= 0 && _joyActivated);
                Vector2 jCenter = joyAct ? _joyOrigin : _visJoyCenter;
                Vector2 jKnob = joyAct ? _joyKnob : jCenter;
                PixelUi.DrawPixelJoystick(sb, jCenter, _visJoyRadius, jKnob, joyAct, _vpH, 0.92f);
                DrawActionCircle(sb, _visToolRect, "USE", _toolPressed, isTool: true);
                DrawActionCircle(sb, _visActRect, "ACT", _interactPressed, isTool: false);
            }
            else if (IsInvisibleBarMode && _cfg.ShowTouchHint)
            {
                if (_joyTouchId >= 0 && _joyActivated)
                {
                    PixelUi.DrawPixelJoystick(sb, _joyOrigin, _activeJoyRadius, _joyKnob, true, _vpH, 0.45f);
                }
                if (_toolRippleAlpha > 0.02f)
                {
                    PixelUi.DrawPixelTileTarget(sb, _pixel, _invToolRect, new Color(235, 186, 98) * (_toolRippleAlpha * 0.55f), 0.10f * _toolRippleAlpha);
                }
                if (_interactRippleAlpha > 0.02f)
                {
                    PixelUi.DrawPixelTileTarget(sb, _pixel, _invActRect, new Color(118, 212, 134) * (_interactRippleAlpha * 0.55f), 0.10f * _interactRippleAlpha);
                }
            }

            // 4. User Custom PC Key Buttons (Stardew Pixel style)
            if (_cfg.CustomKeys.Count > 0)
                DrawCustomKeyButtons(sb);

            // 4b. Compact ROW button beside Horizontal PC Toolbar (hidden when Vertical Toolbar is active)
            if (!_cfg.VerticalToolbar && !_btnToolbarRowRect.IsEmpty && !_saveMenuOpen)
            {
                DrawMinimalPill(sb, _btnToolbarRowRect, "ROW", false);
            }

            // 4c. Auto-Context Furniture Rotate Button (ROT)
            if (GetHeldRotatableFurniture() != null && !_saveMenuOpen)
            {
                PixelUi.DrawPixelPill(sb, _pixel, _bridge, _btnRotateRect, "ROT", PixelRole.Tool, false, _vpH, Math.Max(1, _vpH / 290), 0.92f);
            }

            // 4d. Auto-Context Weapon Special Move Button (SPEC: Parry / Slam / Flurry)
            MeleeWeapon specW = GetHeldSpecialWeapon();
            if (specW != null && !_saveMenuOpen)
            {
                bool onCd = IsWeaponSpecialOnCooldown(specW);
                PixelUi.DrawPixelPill(sb, _pixel, _bridge, _btnSpecRect, "SPEC", PixelRole.Special, false, _vpH, Math.Max(1, _vpH / 290), onCd ? 0.40f : 0.88f);
            }

            // 4e. Slingshot Pixel Aiming Reticle while dragging
            if (_slingshotTouchId >= 0 && Game1.player?.CurrentTool is Slingshot)
            {
                float cr = Math.Max(24f, Math.Min(_vpW, _vpH) * 0.036f);
                PixelUi.DrawPixelCrosshair(sb, _pixel, _slingshotVpPos, cr, _vpH);
            }

            // 5. Stardew-Pixel Multi-Slot Save Menu Modal
            if (_saveMenuOpen)
                DrawSaveMenuModal(sb);

            // 6. Save Feedback & High-RAM Warning Toast Banner
            if (!string.IsNullOrEmpty(_saveToastText) && nowSec < _saveToastUntil)
                DrawToastBanner(sb, isRamWarn);
        }

        private void DrawToastBanner(SpriteBatch sb, bool isRamWarn)
        {
            int tw = isRamWarn
                ? Math.Clamp((int)(_vpW * 0.68f), 340, _vpW - 32)
                : Math.Clamp((int)(_vpW * 0.44f), 260, _vpW - 40);
            int th = Math.Max(40, (int)(_vpH * 0.072f));
            var toastR = new Rectangle((_vpW - tw) / 2, _vpH - th - Math.Max(18, _vpH / 26), tw, th);
            Color tint = isRamWarn ? new Color(255, 206, 198) : Color.White;
            PixelUi.DrawNativeDialogBox(sb, _pixel, toastR, 0.96f, drawShadow: true, tintOverride: tint);
            float fScale = Math.Clamp(_vpH / 900f, 0.72f, 1.30f);
            int fs = Math.Max(1, _vpH / 300);
            Color txtCol = isRamWarn ? new Color(156, 22, 18) : Game1.textColor;
            DrawCenteredGameText(sb, _pixel, _bridge, _saveToastText, toastR, txtCol, fScale, fs);
        }

        private void DrawCustomKeyButtons(SpriteBatch sb)
        {
            for (int i = 0; i < _cfg.CustomKeys.Count; i++)
            {
                var btn = _cfg.CustomKeys[i];
                Rectangle r = GetCustomKeyRect(btn);
                bool held = _customBtnsHeldNow.Contains(btn.Id);
                int fontSize = Math.Max(1, Math.Min(r.Height / 18, r.Width / Math.Max(1, btn.Label.Length * 9)));
                fontSize = Math.Clamp(fontSize, 1, Math.Max(2, _vpH / 180));
                PixelUi.DrawPixelPill(sb, _pixel, _bridge, r, btn.Label, PixelRole.CustomKey, held, _vpH, fontSize, 0.96f);
            }
        }

        private void DrawEditLayoutOverlay(SpriteBatch sb)
        {
            sb.Draw(_pixel, new Rectangle(0, 0, _vpW, _vpH), Color.Black * 0.48f);
            Color gridCol = Color.White * 0.07f;
            int step = Math.Max(60, _vpH / 10);
            for (int x = step; x < _vpW; x += step)
                sb.Draw(_pixel, new Rectangle(x, 0, 1, _vpH), gridCol);
            for (int y = step; y < _vpH; y += step)
                sb.Draw(_pixel, new Rectangle(0, y, _vpW, 1), gridCol);

            // Editable Vertical Toolbar preview (drag Left<->Right or Y, drag inner bottom + corner to resize size!)
            if (_cfg.VerticalToolbar && _verticalToolbar.Bounds.Width > 0)
            {
                _verticalToolbar.Draw(sb, _pixel, _vpH, _cfg.ToolbarOpacity);
                Rectangle tbR = _verticalToolbar.Bounds;
                bool sel = (_editSelected == EditTarget.Toolbar);
                Color tbAccent = sel ? new Color(255, 224, 96) : new Color(72, 210, 235);
                DrawRectOutline(sb, tbR, sel ? 4 : 3, tbAccent);
                string tbTag = $"{_cfg.ToolbarColumns}C {(int)Math.Round(_cfg.ToolbarSize * 100)}%";
                var tagR = new Rectangle(tbR.X, tbR.Y + 4, tbR.Width, Math.Max(24, _vpH / 28));
                sb.Draw(_pixel, tagR, Color.Black * 0.65f);
                _bridge?.DrawCenteredString(sb, _pixel, tbTag, tagR, Color.White, Math.Max(1, _vpH / 320));
                int hs = Math.Max(26, _vpH / 32);
                int hx = _cfg.ToolbarSide == 1 ? tbR.Left : tbR.Right - hs;
                var hRect = new Rectangle(hx, tbR.Bottom - hs, hs, hs);
                sb.Draw(_pixel, hRect, tbAccent);
                _bridge?.DrawCenteredString(sb, _pixel, "+", hRect, Color.Black, Math.Max(2, _vpH / 280));
            }

            // Standard Mobile controls
            if (_cfg.Mode != MobileControlMode.TapToMove)
            {
                if (IsInvisibleBarMode)
                {
                    DrawEditableRectBox(sb, _invJoyRect, "JOYSTICK", new Color(10, 132, 255), _editSelected == EditTarget.Joy);
                    DrawEditableRectBox(sb, _invToolRect, "USE", new Color(255, 159, 10), _editSelected == EditTarget.Tool);
                    DrawEditableRectBox(sb, _invActRect, "ACT", new Color(48, 209, 88), _editSelected == EditTarget.Act);
                }
                else
                {
                    DrawEditableCircleBox(sb, _visJoyCenter, _visJoyRadius, "JOY", new Color(10, 132, 255), _editSelected == EditTarget.Joy);
                    DrawEditableCircleBox(sb, _visToolRect.Center.ToVector2(), _visToolRect.Width * 0.5f, "USE", new Color(255, 159, 10), _editSelected == EditTarget.Tool);
                    DrawEditableCircleBox(sb, _visActRect.Center.ToVector2(), _visActRect.Width * 0.5f, "ACT", new Color(48, 209, 88), _editSelected == EditTarget.Act);
                }
            }

            // User Custom PC Key Buttons (violet boxes — movable & resizable!)
            for (int i = 0; i < _cfg.CustomKeys.Count; i++)
            {
                var btn = _cfg.CustomKeys[i];
                bool sel = (_editSelected == EditTarget.CustomKey && _editSelectedCustomId == btn.Id);
                DrawEditableRectBox(sb, GetCustomKeyRect(btn), btn.Label, new Color(175, 82, 222), sel);
            }

            // Draggable top utility bar
            if (_cfg.ShowTopUtility)
            {
                DrawMinimalPill(sb, _btnBagRect, "BAG", false);
                if (!_topBarCollapsed)
                {
                    DrawMinimalPill(sb, _btnMapRect, "MAP", false);
                    DrawMinimalPill(sb, _btnQuestRect, "QST", false);
                    DrawMinimalPill(sb, _btnSaveRect, "SAVE", false);
                    DrawMinimalPill(sb, _btnKeyRect, "KEY", false);
                    if (!_btnEmoteRect.IsEmpty)
                        PixelUi.DrawPixelSmileyButton(sb, _pixel, _btnEmoteRect, false, _vpH);
                    if (!_btnCtlModeRect.IsEmpty)
                        DrawMinimalPill(sb, _btnCtlModeRect, "CTL", false);
                    if (!_btnChatRect.IsEmpty)
                        DrawMinimalPill(sb, _btnChatRect, "CHAT", false);
                    if (!_btnHideGuiRect.IsEmpty)
                        DrawMinimalPill(sb, _btnHideGuiRect, "HIDE", false);
                    DrawMinimalPill(sb, _btnEditRect, "EDIT", false);
                }
                DrawMinimalPill(sb, _btnCollapseRect, _topBarCollapsed ? ">" : "<", false);
                Color tbBorder = _editSelected == EditTarget.TopBar ? new Color(255, 224, 110) : new Color(228, 198, 150) * 0.78f;
                PixelUi.DrawPixelTileTarget(sb, _pixel, _topBarGroupRect, tbBorder, 0.06f);
            }

            // Top-center native Stardew Edit Layout toolbar (+PHIM / XOA / RESET / HUY / LUU)
            PixelUi.DrawNativeDialogBox(sb, _pixel, _editToolbarRect, 0.96f, drawShadow: true);

            float tbScale = Math.Clamp(_vpH / 920f, 0.70f, 1.25f);
            PixelUi.DrawNativeButton(sb, _pixel, _bridge, _editAddKeyBtnRect, "+Phím", new Color(244, 232, 255), tbScale, _vpH, drawShadow: false);
            bool canDel = (_editSelected == EditTarget.CustomKey && _editSelectedCustomId > 0);
            PixelUi.DrawNativeButton(sb, _pixel, _bridge, _editDelKeyBtnRect, "Xóa", canDel ? new Color(255, 228, 224) : (Color.White * 0.55f), tbScale, _vpH, drawShadow: false);
            PixelUi.DrawNativeButton(sb, _pixel, _bridge, _editResetBtnRect, "Reset", Color.White, tbScale, _vpH, drawShadow: false);
            PixelUi.DrawNativeButton(sb, _pixel, _bridge, _editCancelBtnRect, "Hủy", new Color(255, 244, 220), tbScale, _vpH, drawShadow: false);
            PixelUi.DrawNativeButton(sb, _pixel, _bridge, _editSaveBtnRect, "Lưu", new Color(232, 255, 230), tbScale, _vpH, drawShadow: false);

            // In-Game PC Key Picker Modal
            if (_keyPickerOpen)
                DrawKeyPickerModal(sb);
        }

        private void DrawKeyPickerModal(SpriteBatch sb)
        {
            sb.Draw(_pixel, new Rectangle(0, 0, _vpW, _vpH), Color.Black * 0.54f);
            PixelUi.DrawNativeDialogBox(sb, _pixel, _pickerPanelRect, 1f, drawShadow: true);

            float fBase = Math.Clamp(_vpH / 840f, 0.72f, 1.35f);
            int titleFs = Math.Max(2, _vpH / 250);
            var hdrRect = new Rectangle(_pickerPanelRect.X + 18, _pickerPanelRect.Y + 10, _pickerPanelRect.Width - 120, Math.Max(36, _vpH / 16));
            DrawCenteredGameText(sb, _pixel, _bridge, "Chọn phím PC để thêm", hdrRect, Game1.textColor, fBase * 0.98f, titleFs);
            PixelUi.DrawNativeCloseX(sb, _pickerCloseRect, 1f);

            int cellFs = Math.Max(1, _vpH / 300);
            for (int k = 0; k < PcKeyCatalog.Length; k++)
            {
                Rectangle cell = GetKeyPickerCellRect(k);
                PixelUi.DrawPixelPill(sb, _pixel, _bridge, cell, PcKeyCatalog[k].Label,
                    (k < 12) ? PixelRole.Tool : PixelRole.CustomKey, false, _vpH, cellFs, 0.98f);
            }
        }

        private Rectangle GetKeyPickerCellRect(int index)
        {
            const int cols = 12;
            int rows = (PcKeyCatalog.Length + cols - 1) / cols;
            int padX = Math.Max(14, _pickerPanelRect.Width / 50);
            int topOffset = Math.Max(54, _pickerPanelRect.Height / 8);
            int padB = Math.Max(14, _pickerPanelRect.Height / 28);
            int gap = Math.Max(6, _pickerPanelRect.Width / 120);

            int availW = _pickerPanelRect.Width - padX * 2 - gap * (cols - 1);
            int availH = _pickerPanelRect.Height - topOffset - padB - gap * (rows - 1);
            int cw = Math.Max(24, availW / cols);
            int ch = Math.Max(24, availH / rows);

            int r = index / cols;
            int c = index % cols;
            return new Rectangle(_pickerPanelRect.X + padX + c * (cw + gap), _pickerPanelRect.Y + topOffset + r * (ch + gap), cw, ch);
        }

        private void DrawEditableRectBox(SpriteBatch sb, Rectangle r, string label, Color accent, bool selected)
        {
            int px = PixelUi.Step(_vpH);
            Color border = selected ? new Color(255, 232, 128) : accent * 0.90f;
            sb.Draw(_pixel, r, accent * (selected ? 0.30f : 0.16f));
            PixelUi.DrawPixelTileTarget(sb, _pixel, r, border, 0.04f);
            DrawRectOutline(sb, r, px, border * 0.55f);

            int fs = Math.Max(1, Math.Min(r.Height / 18, Math.Max(2, _vpH / 240)));
            _bridge?.DrawCenteredString(sb, _pixel, label, r, Color.White, fs);

            int g = Math.Clamp(Math.Min(r.Width, r.Height) / 3, 18, Math.Max(24, _vpH / 28));
            var gripRect = new Rectangle(r.Right - g, r.Bottom - g, g, g);
            DrawStardewPixelButton(sb, gripRect, "+", selected ? new Color(224, 174, 62) : new Color(168, 122, 64), new Color(255, 224, 140), new Color(96, 56, 20), Color.White, Math.Max(1, fs - 1), Math.Max(1, px / 2));
        }

        private void DrawEditableCircleBox(SpriteBatch sb, Vector2 center, float radius, string label, Color accent, bool selected)
        {
            int d = (int)(radius * 2f);
            var box = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), d, d);
            PixelUi.DrawPixelMedallion(sb, _pixel, _bridge, box, label, selected ? PixelRole.Active : PixelRole.Neutral, selected, _vpH);
            PixelUi.DrawPixelTileTarget(sb, _pixel, box, selected ? new Color(255, 232, 128) : accent * 0.85f, 0.04f);

            int px = PixelUi.Step(_vpH);
            int g = Math.Max(22, _vpH / 28);
            var gripRect = new Rectangle(box.Right - g, box.Bottom - g, g, g);
            DrawStardewPixelButton(sb, gripRect, "+", selected ? new Color(224, 174, 62) : new Color(168, 122, 64), new Color(255, 224, 140), new Color(96, 56, 20), Color.White, 1, Math.Max(1, px / 2));
        }

        private void DrawToolbarActionButton(SpriteBatch sb, Rectangle r, string text, Color tint, int fontSize)
        {
            int px = PixelUi.Step(_vpH);
            Color hi = new Color(Math.Min(255, tint.R + 42), Math.Min(255, tint.G + 42), Math.Min(255, tint.B + 42));
            Color lo = new Color(Math.Max(0, tint.R - 46), Math.Max(0, tint.G - 46), Math.Max(0, tint.B - 46));
            DrawStardewPixelButton(sb, r, text, tint * 0.92f, hi * 0.90f, lo * 0.90f, Color.White, fontSize, px);
        }

        private void DrawMinimalPill(SpriteBatch sb, Rectangle r, string label, bool active)
        {
            PixelUi.DrawPixelPill(sb, _pixel, _bridge, r, label, active ? PixelRole.Active : PixelRole.Neutral, active, _vpH);
        }

        private void DrawActionCircle(SpriteBatch sb, Rectangle r, string label, bool pressed, bool isTool)
        {
            PixelUi.DrawPixelMedallion(sb, _pixel, _bridge, r, label, isTool ? PixelRole.Tool : PixelRole.Action, pressed, _vpH);
        }

        private void DrawCircle(SpriteBatch sb, Vector2 center, float radius, Color color)
        {
            int d = Math.Max(4, (int)(radius * 2f));
            sb.Draw(_circle, new Rectangle((int)(center.X - radius), (int)(center.Y - radius), d, d), color);
        }

        private void DrawRing(SpriteBatch sb, Vector2 center, float radius, Color color)
        {
            int d = Math.Max(4, (int)(radius * 2f));
            sb.Draw(_ring, new Rectangle((int)(center.X - radius), (int)(center.Y - radius), d, d), color);
        }

        private void DrawRectOutline(SpriteBatch sb, Rectangle r, int t, Color c)
        {
            sb.Draw(_pixel, new Rectangle(r.X, r.Y, r.Width, t), c);
            sb.Draw(_pixel, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
            sb.Draw(_pixel, new Rectangle(r.X, r.Y, t, r.Height), c);
            sb.Draw(_pixel, new Rectangle(r.Right - t, r.Y, t, r.Height), c);
        }

        private bool RefreshViewport()
        {
            try
            {
                var gd = Game1.graphics?.GraphicsDevice;
                if (gd != null)
                {
                    if (gd.Viewport.Width > 0 && gd.Viewport.Height > 0)
                    {
                        _vpW = gd.Viewport.Width;
                        _vpH = gd.Viewport.Height;
                    }
                    if (gd.PresentationParameters.BackBufferWidth > 0 && gd.PresentationParameters.BackBufferHeight > 0)
                    {
                        _rawW = gd.PresentationParameters.BackBufferWidth;
                        _rawH = gd.PresentationParameters.BackBufferHeight;
                    }
                }
                if ((_vpW <= 0 || _vpH <= 0) && Game1.viewport.Width > 0 && Game1.viewport.Height > 0)
                {
                    _vpW = Game1.viewport.Width;
                    _vpH = Game1.viewport.Height;
                }
                if (_rawW <= 0 || _rawH <= 0)
                {
                    _rawW = _vpW;
                    _rawH = _vpH;
                }
            }
            catch { }
            return _vpW > 0 && _vpH > 0;
        }

        private void ComputeLayout()
        {
            float s = _cfg.ButtonScale;
            float unit = Math.Min(_vpW, _vpH);

            // 1. Invisible Mode: Left Joystick Rect + 2 Tall Vertical Rectangular Bars on Right
            _invJoyRect = NormToRect(_cfg.InvJoyX, _cfg.InvJoyY, _cfg.InvJoyW, _cfg.InvJoyH);
            Rectangle rawToolBar = NormToRect(_cfg.InvToolX, _cfg.InvToolY, _cfg.InvToolW, _cfg.InvToolH);
            Rectangle rawActBar = NormToRect(_cfg.InvActX, _cfg.InvActY, _cfg.InvActW, _cfg.InvActH);
            if (_cfg.SwapRightButtons) { _invToolRect = rawActBar; _invActRect = rawToolBar; }
            else { _invToolRect = rawToolBar; _invActRect = rawActBar; }

            // 2. Visible 2-Button Mode: Circle Joystick + 2 Circular Buttons
            _visJoyCenter = new Vector2(_cfg.VisJoyX * _vpW, _cfg.VisJoyY * _vpH);
            _visJoyRadius = Math.Clamp(unit * _cfg.VisJoyR * s, 48f, unit * 0.25f);
            _activeJoyRadius = IsInvisibleBarMode ? Math.Clamp(unit * 0.115f * s, 48f, unit * 0.20f) : _visJoyRadius;

            int toolD = (int)Math.Clamp(unit * _cfg.VisToolR * 2f * s, 56f, unit * 0.34f);
            int actD = (int)Math.Clamp(unit * _cfg.VisActR * 2f * s, 56f, unit * 0.34f);
            Rectangle visTool = new Rectangle((int)(_cfg.VisToolX * _vpW - toolD * 0.5f), (int)(_cfg.VisToolY * _vpH - toolD * 0.5f), toolD, toolD);
            Rectangle visAct = new Rectangle((int)(_cfg.VisActX * _vpW - actD * 0.5f), (int)(_cfg.VisActY * _vpH - actD * 0.5f), actD, actD);
            if (_cfg.SwapRightButtons) { _visToolRect = visAct; _visActRect = visTool; }
            else { _visToolRect = visTool; _visActRect = visAct; }

            // 3. Safe Area + Multi-Column Vertical Toolbar FIRST (so Top Bar, Clock, Joystick & Action Buttons adapt dynamically to Left/Right edge & thickness!)
            _safeLeftPx = ComputeSafeLeftPx();
            _safeRightPx = ComputeSafeRightPx();
            if (_cfg.VerticalToolbar)
            {
                _verticalToolbar.ComputeLayout(
                    _vpW, _vpH,
                    _safeLeftPx, _safeRightPx,
                    _cfg.ToolbarSize,
                    _cfg.ToolbarColumns,
                    _cfg.ToolbarVisibleRows,
                    _cfg.ToolbarSide,
                    _cfg.ToolbarY);
            }

            // Collapsible Top Utility Bar — dynamically clears Vertical Toolbar (Left or Right) & auto-fits clear of Clock!
            int minLeftClear = (_cfg.VerticalToolbar && _cfg.ToolbarSide == 0) ? (_verticalToolbar.Bounds.Right + 10) : (_safeLeftPx + 8);
            int leftX = Math.Max(minLeftClear, (int)(_cfg.TopBarX * _vpW));
            int topY = (int)(_cfg.TopBarY * _vpH);
            int pillH = (int)Math.Clamp(unit * 0.056f, 28f, unit * 0.08f);
            bool showChatBtn = Context.IsMultiplayer;

            // Auto-fit Top Bar width so it NEVER collides with DayTimeMoneyBox (or Right-docked Vertical Toolbar!)
            int rightClockAndTb = _safeRightPx + Math.Max(215, (int)(_vpW * 0.21f));
            if (_cfg.VerticalToolbar && _cfg.ToolbarSide == 1 && _verticalToolbar.Bounds.Width > 0)
                rightClockAndTb += _verticalToolbar.Bounds.Width + 12;
            int maxTopBarRight = _vpW - rightClockAndTb;
            if (!_topBarCollapsed && maxTopBarRight > leftX + 180)
            {
                float estUnits = 1.85f * 4f + 2.15f * 3f + 1.10f
                    + (_cfg.ShowEmoteButton ? 1.25f : 0f)
                    + (_cfg.AutoCombatMode ? 1.85f : 0f)
                    + (showChatBtn ? 2.15f : 0f);
                int estSlots = 8 + (_cfg.ShowEmoteButton ? 1 : 0) + (_cfg.AutoCombatMode ? 1 : 0) + (showChatBtn ? 1 : 0);
                float estW = estUnits * pillH + (estSlots - 1) * Math.Max(4, pillH / 5);
                float availW = maxTopBarRight - leftX;
                if (estW > availW)
                {
                    float ratio = availW / Math.Max(1f, estW);
                    pillH = Math.Max(22, (int)(pillH * ratio));
                }
            }

            int pillW = (int)(pillH * 1.85f);
            int modsW = (int)(pillH * 2.15f);
            int colW = (int)(pillH * 1.10f);
            int emoteW = (int)(pillH * 1.25f);
            int pillGap = Math.Max(4, pillH / 5);

            int cx = leftX;
            _btnBagRect = new Rectangle(cx, topY, pillW, pillH);
            cx += pillW + pillGap;

            _btnEmoteRect = Rectangle.Empty;
            _btnCtlModeRect = Rectangle.Empty;
            _btnChatRect = Rectangle.Empty;
            _btnHideGuiRect = Rectangle.Empty;
            if (!_topBarCollapsed)
            {
                _btnMapRect = new Rectangle(cx, topY, pillW, pillH); cx += pillW + pillGap;
                _btnQuestRect = new Rectangle(cx, topY, pillW, pillH); cx += pillW + pillGap;
                _btnSaveRect = new Rectangle(cx, topY, modsW, pillH); cx += modsW + pillGap;
                _btnKeyRect = new Rectangle(cx, topY, pillW, pillH); cx += pillW + pillGap;
                if (_cfg.ShowEmoteButton)
                {
                    _btnEmoteRect = new Rectangle(cx, topY, emoteW, pillH);
                    cx += emoteW + pillGap;
                }
                if (_cfg.AutoCombatMode)
                {
                    _btnCtlModeRect = new Rectangle(cx, topY, pillW, pillH);
                    cx += pillW + pillGap;
                }
                if (showChatBtn)
                {
                    _btnChatRect = new Rectangle(cx, topY, modsW, pillH);
                    cx += modsW + pillGap;
                }
                _btnHideGuiRect = new Rectangle(cx, topY, modsW, pillH); cx += modsW + pillGap;
                _btnEditRect = new Rectangle(cx, topY, modsW, pillH); cx += modsW + pillGap;
            }
            else
            {
                _btnMapRect = _btnQuestRect = _btnSaveRect = _btnKeyRect = _btnEditRect = Rectangle.Empty;
            }

            _btnCollapseRect = new Rectangle(cx, topY, colW, pillH);
            _topBarGroupRect = new Rectangle(leftX - 4, topY - 4, (_btnCollapseRect.Right - leftX) + 8, pillH + 8);

            // Auto-Context Furniture Rotate (ROT), Weapon Special Move (SPEC) & Cutscene SKIP (clear of right Notch/Dynamic Island!)
            int rotW = (int)(pillH * 2.15f);
            int rotH = (int)(pillH * 1.18f);
            int rightEdgePad = Math.Max(_safeRightPx + 12, Math.Max(16, (int)(_vpW * 0.025f)));
            _btnRotateRect = new Rectangle(_vpW - rotW - rightEdgePad, (int)(_vpH * 0.34f), rotW, rotH);
            _btnSpecRect = new Rectangle(_vpW - rotW - rightEdgePad, (int)(_vpH * 0.34f), rotW, rotH);
            int skipH = (int)Math.Clamp(unit * 0.068f, 34f, 60f);
            int skipW = (int)(skipH * 2.15f);
            _btnSkipEventRect = new Rectangle(_vpW - skipW - rightEdgePad, (int)Math.Max(unit * 0.028f, 16f), skipW, skipH);

            // 1-Tap Left-Handed Mirror Layout (WYSIWYG in both Gameplay AND Edit Layout!)
            if (_cfg.LeftHandedMobile)
            {
                Rectangle MirrorX(Rectangle r) => new Rectangle(_vpW - r.Right, r.Y, r.Width, r.Height);
                _invJoyRect = MirrorX(_invJoyRect);
                _invToolRect = MirrorX(_invToolRect);
                _invActRect = MirrorX(_invActRect);
                _visJoyCenter = new Vector2(_vpW - _visJoyCenter.X, _visJoyCenter.Y);
                _visToolRect = MirrorX(_visToolRect);
                _visActRect = MirrorX(_visActRect);
                _btnRotateRect = MirrorX(_btnRotateRect);
                _btnSpecRect = MirrorX(_btnSpecRect);
            }

            // Auto-clear VerticalToolbar (BOTH Left and Right docking!) so NEITHER Joystick NOR Action Buttons overlap it!
            if (_cfg.VerticalToolbar && !_editMode && _verticalToolbar.Bounds.Width > 0)
            {
                if (_cfg.ToolbarSide == 0)
                {
                    int minX = _verticalToolbar.Bounds.Right + 8;
                    if (!_cfg.LeftHandedMobile)
                    {
                        if (_invJoyRect.X < minX && _invJoyRect.Right > minX + 60)
                        {
                            int delta = minX - _invJoyRect.X;
                            _invJoyRect = new Rectangle(minX, _invJoyRect.Y, Math.Max(60, _invJoyRect.Width - delta), _invJoyRect.Height);
                        }
                        if (_visJoyCenter.X - _visJoyRadius < minX)
                            _visJoyCenter = new Vector2(minX + _visJoyRadius + 4, _visJoyCenter.Y);
                    }
                    else
                    {
                        int leftMostInv = Math.Min(_invToolRect.X, _invActRect.X);
                        if (leftMostInv < minX)
                        {
                            int shift = minX - leftMostInv;
                            _invToolRect = new Rectangle(_invToolRect.X + shift, _invToolRect.Y, _invToolRect.Width, _invToolRect.Height);
                            _invActRect = new Rectangle(_invActRect.X + shift, _invActRect.Y, _invActRect.Width, _invActRect.Height);
                        }
                        int leftMostVis = Math.Min(_visToolRect.X, _visActRect.X);
                        if (leftMostVis < minX)
                        {
                            int shift = minX - leftMostVis;
                            _visToolRect = new Rectangle(_visToolRect.X + shift, _visToolRect.Y, _visToolRect.Width, _visToolRect.Height);
                            _visActRect = new Rectangle(_visActRect.X + shift, _visActRect.Y, _visActRect.Width, _visActRect.Height);
                        }
                        if (_btnRotateRect.X < minX) _btnRotateRect = new Rectangle(minX, _btnRotateRect.Y, _btnRotateRect.Width, _btnRotateRect.Height);
                        if (_btnSpecRect.X < minX) _btnSpecRect = new Rectangle(minX, _btnSpecRect.Y, _btnSpecRect.Width, _btnSpecRect.Height);
                    }
                }
                else
                {
                    int maxX = _verticalToolbar.Bounds.Left - 8;
                    if (!_cfg.LeftHandedMobile)
                    {
                        int rightMostInv = Math.Max(_invToolRect.Right, _invActRect.Right);
                        if (rightMostInv > maxX)
                        {
                            int shift = rightMostInv - maxX;
                            _invToolRect = new Rectangle(Math.Max(8, _invToolRect.X - shift), _invToolRect.Y, _invToolRect.Width, _invToolRect.Height);
                            _invActRect = new Rectangle(Math.Max(8, _invActRect.X - shift), _invActRect.Y, _invActRect.Width, _invActRect.Height);
                        }
                        int rightMostVis = Math.Max(_visToolRect.Right, _visActRect.Right);
                        if (rightMostVis > maxX)
                        {
                            int shift = rightMostVis - maxX;
                            _visToolRect = new Rectangle(Math.Max(8, _visToolRect.X - shift), _visToolRect.Y, _visToolRect.Width, _visToolRect.Height);
                            _visActRect = new Rectangle(Math.Max(8, _visActRect.X - shift), _visActRect.Y, _visActRect.Width, _visActRect.Height);
                        }
                        if (_btnRotateRect.Right > maxX) _btnRotateRect = new Rectangle(Math.Max(8, maxX - _btnRotateRect.Width), _btnRotateRect.Y, _btnRotateRect.Width, _btnRotateRect.Height);
                        if (_btnSpecRect.Right > maxX) _btnSpecRect = new Rectangle(Math.Max(8, maxX - _btnSpecRect.Width), _btnSpecRect.Y, _btnSpecRect.Width, _btnSpecRect.Height);
                    }
                    else
                    {
                        if (_invJoyRect.Right > maxX && maxX - _invJoyRect.X > 60)
                            _invJoyRect = new Rectangle(_invJoyRect.X, _invJoyRect.Y, Math.Max(60, maxX - _invJoyRect.X), _invJoyRect.Height);
                        if (_visJoyCenter.X + _visJoyRadius > maxX)
                            _visJoyCenter = new Vector2(Math.Max(_visJoyRadius + 8, maxX - _visJoyRadius - 4), _visJoyCenter.Y);
                    }
                }
            }

            // Compact ROW quick-switch pill right beside Toolbar (only when player has > 12 inventory slots & Horizontal PC Toolbar!)
            _btnToolbarRowRect = Rectangle.Empty;
            if (!_cfg.VerticalToolbar && Game1.player != null && Game1.player.MaxItems > 12 && TryGetToolbarViewportBounds(out Rectangle tbR))
            {
                int rowW = Math.Max(42, (int)(tbR.Height * 0.92f));
                int rowH = Math.Max(30, (int)(tbR.Height * 0.78f));
                int rx = tbR.Right + Math.Max(6, rowW / 7);
                if (rx + rowW > _vpW - _safeRightPx - 8)
                    rx = Math.Max(_safeLeftPx + 8, tbR.X - rowW - Math.Max(6, rowW / 7));
                int ry = tbR.Y + (tbR.Height - rowH) / 2;
                _btnToolbarRowRect = new Rectangle(rx, ry, rowW, rowH);
            }

            // Context Menu Helper Bar (clear of left Dynamic Island / Notch!)
            int mx0 = Math.Max(_safeLeftPx + 10, (int)Math.Max(_vpW * 0.048f, unit * 0.06f));
            int mx = mx0;
            int my = (int)Math.Max(unit * 0.022f, 12f);
            _ctxQuestEscRect = new Rectangle(mx0, my, pillW, pillH);
            _ctxX1Rect   = new Rectangle(mx, my, pillW, pillH); mx += pillW + pillGap;
            _ctxHalfRect = new Rectangle(mx, my, pillW, pillH); mx += pillW + pillGap;
            _ctxXferRect = new Rectangle(mx, my, modsW, pillH); mx += modsW + pillGap;
            _ctxKeyRect  = new Rectangle(mx, my, pillW, pillH); mx += pillW + pillGap;
            _ctxEscRect  = new Rectangle(mx, my, pillW, pillH);

            // Multi-Slot Stardew-Pixel Save Menu Modal Layout (100% dynamic, never hardcoded!)
            int spw = Math.Clamp((int)(_vpW * 0.68f), 420, _vpW - 40);
            int sph = Math.Clamp((int)(_vpH * 0.76f), 280, _vpH - 32);
            _savePanelRect = new Rectangle((_vpW - spw) / 2, (_vpH - sph) / 2, spw, sph);
            int sPad = Math.Max(14, (int)(unit * 0.022f));
            int sClose = Math.Max(34, (int)(sph * 0.105f));
            _saveCloseRect = new Rectangle(_savePanelRect.Right - sClose - sPad, _savePanelRect.Y + sPad, sClose, sClose);

            int sBtnH = Math.Max(38, (int)(sph * 0.135f));
            int sBtnY = _savePanelRect.Bottom - sPad - sBtnH;
            int sBtnGap = Math.Max(10, sPad / 2);
            int sAvailW = spw - sPad * 2 - sBtnGap * 2;
            int sNewW = (int)(sAvailW * 0.36f);
            int sLoadW = (int)(sAvailW * 0.40f);
            int sDelW = sAvailW - sNewW - sLoadW;
            _saveNewBtnRect = new Rectangle(_savePanelRect.X + sPad, sBtnY, sNewW, sBtnH);
            _saveLoadBtnRect = new Rectangle(_saveNewBtnRect.Right + sBtnGap, sBtnY, sLoadW, sBtnH);
            _saveDelBtnRect = new Rectangle(_saveLoadBtnRect.Right + sBtnGap, sBtnY, sDelW, sBtnH);

            int sScrollW = Math.Max(36, (int)(spw * 0.068f));
            int sListTop = _saveCloseRect.Bottom + Math.Max(8, sPad / 2);
            int sListH = Math.Max(120, sBtnY - sListTop - Math.Max(10, sPad / 2));
            _saveListRect = new Rectangle(_savePanelRect.X + sPad, sListTop, spw - sPad * 2 - sScrollW - sBtnGap, sListH);
            int sHalfScrollH = (sListH - sBtnGap) / 2;
            _saveUpBtnRect = new Rectangle(_saveListRect.Right + sBtnGap, sListTop, sScrollW, sHalfScrollH);
            _saveDnBtnRect = new Rectangle(_saveListRect.Right + sBtnGap, sListTop + sHalfScrollH + sBtnGap, sScrollW, sListH - sHalfScrollH - sBtnGap);

            // 4. Edit Layout top-center toolbar (+PHIM / XOA / RESET / HUY / LUU)
            int ebH = (int)Math.Clamp(unit * 0.066f, 36f, unit * 0.09f);
            int ebW = (int)(ebH * 2.05f);
            int ebGap = Math.Max(8, ebH / 5);
            int pad = Math.Max(8, ebH / 5);
            int totalW = ebW * 5 + ebGap * 4 + pad * 2;
            int totalH = ebH + pad * 2;
            int tbX = (_vpW - totalW) / 2;
            int tbY = (int)Math.Max(unit * 0.024f, 14f);

            _editToolbarRect = new Rectangle(tbX, tbY, totalW, totalH);
            int bx = tbX + pad;
            _editAddKeyBtnRect = new Rectangle(bx, tbY + pad, ebW, ebH); bx += ebW + ebGap;
            _editDelKeyBtnRect = new Rectangle(bx, tbY + pad, ebW, ebH); bx += ebW + ebGap;
            _editResetBtnRect  = new Rectangle(bx, tbY + pad, ebW, ebH); bx += ebW + ebGap;
            _editCancelBtnRect = new Rectangle(bx, tbY + pad, ebW, ebH); bx += ebW + ebGap;
            _editSaveBtnRect   = new Rectangle(bx, tbY + pad, ebW, ebH);

            // 5. In-Game PC Key Picker modal rect
            int pw = (int)(_vpW * 0.84f);
            int ph = (int)(_vpH * 0.78f);
            _pickerPanelRect = new Rectangle((_vpW - pw) / 2, (_vpH - ph) / 2, pw, ph);
            int closeS = Math.Max(34, ph / 11);
            _pickerCloseRect = new Rectangle(_pickerPanelRect.Right - closeS - 12, _pickerPanelRect.Y + 10, closeS, closeS);
        }

        private Rectangle NormToRect(float nx, float ny, float nw, float nh)
        {
            int x = (int)Math.Round(nx * _vpW);
            int y = (int)Math.Round(ny * _vpH);
            int w = Math.Max(44, (int)Math.Round(nw * _vpW));
            int h = Math.Max(56, (int)Math.Round(nh * _vpH));
            return new Rectangle(x, y, w, h);
        }

        private Vector2 MapRawToViewport(Vector2 raw)
        {
            if (_rawW <= 0 || _rawH <= 0 || _vpW <= 0 || _vpH <= 0)
                return raw;
            return new Vector2(raw.X / _rawW * _vpW, raw.Y / _rawH * _vpH);
        }

        private Point ViewportToRaw(Vector2 vp)
        {
            if (_rawW <= 0 || _rawH <= 0 || _vpW <= 0 || _vpH <= 0)
                return new Point((int)Math.Round(vp.X), (int)Math.Round(vp.Y));
            return new Point(
                Math.Clamp((int)Math.Round(vp.X / _vpW * _rawW), 0, _rawW - 1),
                Math.Clamp((int)Math.Round(vp.Y / _vpH * _rawH), 0, _rawH - 1));
        }

        private Point ViewportToUi(Vector2 vp)
        {
            int uiW = Game1.uiViewport.Width;
            int uiH = Game1.uiViewport.Height;
            if (uiW <= 0 || uiH <= 0 || _vpW <= 0 || _vpH <= 0)
                return new Point((int)vp.X, (int)vp.Y);
            return new Point((int)(vp.X * uiW / _vpW), (int)(vp.Y * uiH / _vpH));
        }

        private Vector2 UiToViewport(Point uiPt)
        {
            int uiW = Game1.uiViewport.Width;
            int uiH = Game1.uiViewport.Height;
            if (uiW <= 0 || uiH <= 0 || _vpW <= 0 || _vpH <= 0)
                return new Vector2(uiPt.X, uiPt.Y);
            return new Vector2(uiPt.X * (float)_vpW / uiW, uiPt.Y * (float)_vpH / uiH);
        }

        private Vector2 ViewportToWorld(Vector2 vp)
        {
            float wx = Game1.viewport.X + vp.X * (Game1.viewport.Width / (float)Math.Max(1, _vpW));
            float wy = Game1.viewport.Y + vp.Y * (Game1.viewport.Height / (float)Math.Max(1, _vpH));
            return new Vector2(wx, wy);
        }

        private Vector2 WorldToViewport(Vector2 world)
        {
            float sx = (world.X - Game1.viewport.X) * (_vpW / (float)Math.Max(1, Game1.viewport.Width));
            float sy = (world.Y - Game1.viewport.Y) * (_vpH / (float)Math.Max(1, Game1.viewport.Height));
            return new Vector2(sx, sy);
        }

        private void EnsureTextures(GraphicsDevice gd)
        {
            if (_pixel != null && !_pixel.IsDisposed)
                return;

            _pixel = new Texture2D(gd, 1, 1);
            _pixel.SetData(new[] { Color.White });

            const int sz = 128;
            float r = sz * 0.5f;
            var fillData = new Color[sz * sz];
            var ringData = new Color[sz * sz];

            for (int y = 0; y < sz; y++)
            {
                for (int x = 0; x < sz; x++)
                {
                    float dx = (x + 0.5f) - r;
                    float dy = (y + 0.5f) - r;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    int idx = y * sz + x;

                    float fillAlpha = Math.Clamp(r - 1.5f - d, 0f, 1f);
                    fillData[idx] = Color.White * fillAlpha;

                    float ringDist = Math.Abs(d - (r - 4.0f));
                    float ringAlpha = Math.Clamp(2.8f - ringDist, 0f, 1f);
                    ringData[idx] = Color.White * ringAlpha;
                }
            }

            _circle = new Texture2D(gd, sz, sz);
            _circle.SetData(fillData);

            _ring = new Texture2D(gd, sz, sz);
            _ring.SetData(ringData);
        }
    }
}
