using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace SDVMobileControls
{
    internal sealed class MobileControlsEngine
    {
        private readonly MobileConfig _cfg;
        private readonly IMonitor _monitor;
        private readonly MobileSaveSlotManager _saveSlots;
        private readonly MobileInventoryShopHelper _invShop = new MobileInventoryShopHelper();
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
        private List<Point> _path;
        private int _pathIdx;
        private Point _targetTile;
        private bool _hasTargetTile;
        private TileIntent _pendingIntent;
        private Vector2 _lastPlayerPos;
        private int _stuckFrames;
        private int _actionPulseXFrames;
        private int _actionPulseAFrames;
        private string _lastLocationName;

        // Auto-attack state (default OFF)
        private int _autoAttackCooldown;

        // Menu 1-finger tap / drag / swipe scroll state (single TouchPanel.GetState() per frame!)
        private int _menuTapTouchId = -1;
        private Vector2 _menuTapStartVp;
        private float _menuTapLastY;
        private float _menuTapAccumY;
        private bool _menuTapDragged;
        private bool _menuTapImmediateDrag;

        // Toolbar tap debounce
        private int _toolbarTouchId = -1;

        // ---- Dedicated Mobile Edit Layout Mode + In-Game PC Key Picker ----
        private enum EditTarget { None = 0, Joy = 1, Tool = 2, Act = 3, TopBar = 4, CustomKey = 5 }
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

        public MobileControlMode CurrentMode => _cfg.Mode;
        public bool IsMobileModeActive => _cfg.Mode != MobileControlMode.VirtualGamepad;

        private bool IsInvisibleBarMode =>
            _cfg.Mode == MobileControlMode.InvisibleJoystick2Btn ||
            _cfg.Mode == MobileControlMode.TapAndJoystick;

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

        public void ResetGameplayState()
        {
            _joyTouchId = -1;
            _joyActivated = false;
            _toolTouchId = -1;
            _interactTouchId = -1;
            _toolPressed = false;
            _interactPressed = false;
            _pinching = false;
            _pinchIdA = -1;
            _pinchIdB = -1;
            _tapTouchId = -1;
            _holdWalking = false;
            ClearPath();
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
            _stuckFrames = 0;
        }

        private bool IsInventoryContextMenu()
        {
            var m = Game1.activeClickableMenu;
            return m is GameMenu || m is ItemGrabMenu || m is ShopMenu || m is JunimoNoteMenu;
        }

        private bool IsTextInputMenu()
        {
            var m = Game1.activeClickableMenu;
            if (m is NamingMenu || m is TitleTextInputMenu)
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
        }

        private static bool IsImmediateDragMenu(IClickableMenu menu, Point uiPt)
        {
            if (menu is GameMenu gm)
            {
                try
                {
                    if (gm.GetCurrentPage() is OptionsPage)
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
            _editMode = false;
            _keyPickerOpen = false;
            _saveMenuOpen = false;
            ResetGameplayState();
            TickCustomButtonPulses();

            if (!IsMobileModeActive)
            {
                ResetMenuTapState();
                ClearCustomButtonTouches();
                _invShop.Reset();
                return false;
            }

            IClickableMenu activeMenu = Game1.activeClickableMenu;
            if (activeMenu == null)
            {
                ResetMenuTapState();
                _ctxTouchId = -1;
                _invShop.Reset();
                return false;
            }

            var br = EnsureBridge(pad);
            br?.CloseHotkeyDrawer(pad);
            if (br != null && (br.IsFullScreenModalOpen(pad) || br.IsQuickSaveReloading()))
            {
                ResetMenuTapState();
                return false;
            }

            // CRITICAL: Return BEFORE calling TouchPanel.GetState() for BobberBar & TitleMenu!
            // MonoGame's TouchPanel.GetState() destructively ages Pressed->Moved and clears Released on EVERY call!
            if (activeMenu is BobberBar || activeMenu is TitleMenu)
            {
                ResetMenuTapState();
                _invShop.Reset();
                return false;
            }

            if (!RefreshViewport())
                return false;
            ComputeLayout();

            bool showInvCtx = IsInventoryContextMenu();
            bool showKeyOnly = !showInvCtx && IsTextInputMenu();
            bool showEscOnly = !showInvCtx && !showKeyOnly && (activeMenu is QuestLog);
            bool allowCustomKeys = AllowCustomKeysInCurrentMenu() && _cfg.CustomKeys.Count > 0;

            // Single TouchPanel.GetState() read per frame — from here on, ALWAYS return true!
            TouchCollection touches = TouchPanel.GetState();
            double nowSec = gameTime != null ? gameTime.TotalGameTime.TotalSeconds : Environment.TickCount64 / 1000.0;

            // Always consume lingering tap release from BAG/MAP/QST or HUD Quest button
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

            // If Mobile Buy/Sell/Split Popup is open OR dragging an item, give it top priority!
            if (_cfg.MobileShopAndDrag && (_invShop.IsPopupOpen || _invShop.IsDraggingItem))
            {
                if (_invShop.Update(activeMenu, touches, MapRawToViewport, ViewportToUi, _vpW, _vpH, nowSec))
                {
                    ResetMenuTapState();
                    ApplyToPad(pad, Vector2.Zero, false, false, false);
                    return true;
                }
            }

            // Handle Custom PC Key Buttons in Inventory / Mod Menus
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

            // 1. Handle taps on Context Helper Bar (Inventory X1/1/2/XFER/KEY/ESC, TextInput KEY, QuestLog ESC-only)
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
                            if (_ctxEscRect.Contains(pt)) { _invShop.Reset(); try { Game1.exitActiveMenu(); } catch { } _ctxTouchId = tl.Id; sawCtx = true; continue; }
                        }
                        else if (showKeyOnly && _ctxX1Rect.Contains(pt))
                        {
                            br?.TriggerVirtualKeyboard(pad);
                            _ctxTouchId = tl.Id;
                            sawCtx = true;
                            continue;
                        }
                        else if (showEscOnly && _ctxQuestEscRect.Contains(pt))
                        {
                            try { Game1.playSound("bigDeSelect"); } catch { }
                            try { Game1.exitActiveMenu(); } catch { }
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
                    ApplyToPad(pad, Vector2.Zero, false, false, false);
                    return true;
                }
            }

            // 2. Mobile-style Shop (Buy/Sell pixel popup) & Inventory/Chest Drag-and-Drop / Tap-to-Transfer / Long-Press Split
            bool pcSplitActive = br != null && (br.GetSingleItemMode(pad) || br.GetHalfStackMode(pad) || br.GetQuickTransferMode(pad));
            if (_cfg.MobileShopAndDrag && !pcSplitActive)
            {
                if (_invShop.Update(activeMenu, touches, MapRawToViewport, ViewportToUi, _vpW, _vpH, nowSec))
                {
                    ResetMenuTapState();
                    ApplyToPad(pad, Vector2.Zero, false, false, false);
                    return true;
                }
            }
            else
            {
                _invShop.Reset();
            }

            // 3. Universal Direct Menu Touch Handler (QuestLog quest list & details, GameMenu tabs/Options, Dialogue, Letters, Billboard, etc.)
            // Handles taps, sliders/scrollbars, AND 1-finger vertical swipe scrolling cleanly without double-reading TouchPanel!
            if (touches.Count == 0)
            {
                ResetMenuTapState();
                ApplyToPad(pad, Vector2.Zero, false, false, false);
                return true;
            }

            for (int i = 0; i < touches.Count; i++)
            {
                TouchLocation tl = touches[i];
                Vector2 vp = MapRawToViewport(tl.Position);
                Point ui = ViewportToUi(vp);

                if (tl.State == TouchLocationState.Pressed && _menuTapTouchId < 0)
                {
                    _menuTapTouchId = tl.Id;
                    _menuTapStartVp = vp;
                    _menuTapLastY = vp.Y;
                    _menuTapAccumY = 0f;
                    _menuTapDragged = false;
                    _menuTapImmediateDrag = IsImmediateDragMenu(activeMenu, ui);

                    try { activeMenu.performHoverAction(ui.X, ui.Y); } catch { }
                    if (_menuTapImmediateDrag)
                    {
                        try { activeMenu.receiveLeftClick(ui.X, ui.Y, true); } catch { }
                    }
                }
                else if (tl.State == TouchLocationState.Moved && tl.Id == _menuTapTouchId)
                {
                    if (_menuTapImmediateDrag)
                    {
                        try { activeMenu.leftClickHeld(ui.X, ui.Y); } catch { }
                    }
                    else
                    {
                        float dy = vp.Y - _menuTapLastY;
                        _menuTapLastY = vp.Y;
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
                                try { activeMenu.receiveScrollWheelAction(wheel); } catch { }
                            }
                        }
                        else
                        {
                            try { activeMenu.performHoverAction(ui.X, ui.Y); } catch { }
                        }
                    }
                }
                else if ((tl.State == TouchLocationState.Released || tl.State == TouchLocationState.Invalid) && tl.Id == _menuTapTouchId)
                {
                    if (_menuTapImmediateDrag)
                    {
                        try { activeMenu.releaseLeftClick(ui.X, ui.Y); } catch { }
                    }
                    else if (!_menuTapDragged)
                    {
                        try { activeMenu.performHoverAction(ui.X, ui.Y); } catch { }
                        try { activeMenu.receiveLeftClick(ui.X, ui.Y, true); } catch { }
                        try { activeMenu.releaseLeftClick(ui.X, ui.Y); } catch { }
                    }
                    ResetMenuTapState();
                }
            }

            ApplyToPad(pad, Vector2.Zero, false, false, false);
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
            if (sb == null || sb.GraphicsDevice == null || Game1.activeClickableMenu == null)
                return;

            bool showInvCtx = IsInventoryContextMenu();
            bool showKeyOnly = !showInvCtx && IsTextInputMenu();
            bool showEscOnly = !showInvCtx && !showKeyOnly && (Game1.activeClickableMenu is QuestLog);
            bool allowCustomKeys = AllowCustomKeysInCurrentMenu() && _cfg.CustomKeys.Count > 0;
            bool showInvShop = _cfg.MobileShopAndDrag && (_invShop.IsPopupOpen || _invShop.IsDraggingItem || _invShop.HasSelectedSlot);
            if (!showInvCtx && !showKeyOnly && !showEscOnly && !allowCustomKeys && !showInvShop)
                return;

            EnsureTextures(sb.GraphicsDevice);
            if (!RefreshViewport()) return;
            ComputeLayout();
            var br = EnsureBridge(pad);

            if (showInvCtx)
            {
                DrawMinimalPill(sb, _ctxX1Rect, "X1", br != null && br.GetSingleItemMode(pad));
                DrawMinimalPill(sb, _ctxHalfRect, "1/2", br != null && br.GetHalfStackMode(pad));
                DrawMinimalPill(sb, _ctxXferRect, "XFER", br != null && br.GetQuickTransferMode(pad));
                DrawMinimalPill(sb, _ctxKeyRect, "KEY", false);
                DrawMinimalPill(sb, _ctxEscRect, "ESC", false);
            }
            else if (showKeyOnly)
            {
                DrawMinimalPill(sb, _ctxX1Rect, "KEY", false);
            }
            else if (showEscOnly)
            {
                DrawMinimalPill(sb, _ctxQuestEscRect, "ESC", false);
            }

            if (allowCustomKeys)
                DrawCustomKeyButtons(sb);

            if (showInvShop)
                _invShop.Draw(sb, _pixel, _circle, br, Game1.activeClickableMenu, UiToViewport, ViewportToUi, _vpW, _vpH);
        }

        public void UpdateGameplay(object pad, GameTime gameTime)
        {
            _cfg.PollReload();
            _invShop.Reset();
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

            string curLocName = Game1.currentLocation?.NameOrUniqueName;
            if (curLocName != _lastLocationName)
            {
                _lastLocationName = curLocName;
                ClearPath();
            }

            if (_toolRippleAlpha > 0f) _toolRippleAlpha = Math.Max(0f, _toolRippleAlpha - 0.12f);
            if (_interactRippleAlpha > 0f) _interactRippleAlpha = Math.Max(0f, _interactRippleAlpha - 0.12f);

            double nowSec = gameTime != null ? gameTime.TotalGameTime.TotalSeconds : Environment.TickCount64 / 1000.0;
            TouchCollection touches = TouchPanel.GetState();

            // --- 0. Process User Custom PC Key Buttons FIRST (never steal gameplay buttons!) ---
            if (_cfg.CustomKeys.Count > 0)
                ProcessCustomKeyTouches(pad, br, touches, nowSec);
            else
                ClearCustomButtonTouches();

            bool useJoy = _cfg.Mode == MobileControlMode.InvisibleJoystick2Btn ||
                          _cfg.Mode == MobileControlMode.VisibleJoystick2Btn ||
                          _cfg.Mode == MobileControlMode.TapAndJoystick;
            bool use2Btn = useJoy;
            bool useTap = _cfg.Mode == MobileControlMode.TapToMove ||
                          _cfg.Mode == MobileControlMode.TapAndJoystick;
            bool isHybrid = _cfg.Mode == MobileControlMode.TapAndJoystick;

            // --- 1. Handle 2-Finger Pinch-to-Zoom ---
            if (_cfg.PinchZoom && HandlePinchZoom(touches))
            {
                _joyTouchId = -1;
                _joyActivated = false;
                _tapTouchId = -1;
                _holdWalking = false;
                _toolTouchId = -1;
                _interactTouchId = -1;
                _toolPressed = false;
                _interactPressed = false;
                ApplyToPad(pad, Vector2.Zero, false, false, false);
                return;
            }

            bool sawJoy = false;
            bool sawTool = false;
            bool sawInteract = false;
            bool sawTap = false;
            bool sawToolbar = false;
            Vector2 stickVec = Vector2.Zero;

            for (int i = 0; i < touches.Count; i++)
            {
                TouchLocation tl = touches[i];
                if (IsTouchOwnedByCustomButton(tl.Id))
                    continue;

                Vector2 vp = MapRawToViewport(tl.Position);
                Point vpPt = new Point((int)vp.X, (int)vp.Y);
                bool active = tl.State == TouchLocationState.Pressed || tl.State == TouchLocationState.Moved;

                // Top utility bar (BAG / MAP / QST / MODS / KEY / EDIT / collapse)
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
                        if (_btnEditRect.Contains(vpPt))
                        {
                            EnterEditLayoutMode();
                            try { Game1.playSound("smallSelect"); } catch { }
                            continue;
                        }
                    }
                }

                // Direct Toolbar & Quest Journal tap
                if (tl.Id == _toolbarTouchId)
                {
                    if (active) sawToolbar = true;
                    continue;
                }
                if (tl.State == TouchLocationState.Pressed && TryHandleHudTap(vp, out bool openedMenu))
                {
                    if (openedMenu)
                    {
                        _ctxTouchId = tl.Id;
                        ClearPath();
                        ApplyToPad(pad, Vector2.Zero, false, false, false);
                        return;
                    }
                    _toolbarTouchId = tl.Id;
                    sawToolbar = true;
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

                // Existing active tap-to-move / hold-to-walk finger
                if (useTap && tl.Id == _tapTouchId)
                {
                    if (active)
                    {
                        sawTap = true;
                        _tapCurScreen = vp;
                        float moved = Vector2.Distance(vp, _tapStartScreen);
                        if (!_holdWalking && (moved >= 22f || (nowSec - _tapStartTime) >= 0.22))
                        {
                            _holdWalking = true;
                            ClearPath();
                        }
                    }
                    else
                    {
                        if (!_holdWalking)
                            HandleWorldTap(vp);
                        _holdWalking = false;
                        _tapTouchId = -1;
                    }
                    continue;
                }

                // New touch pressed
                if (tl.State == TouchLocationState.Pressed)
                {
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

                    bool inJoyZone = IsInvisibleBarMode
                        ? _invJoyRect.Contains(vpPt)
                        : (Vector2.Distance(vp, _visJoyCenter) <= _visJoyRadius * 1.65f || vp.X < _vpW * 0.44f);

                    if (useJoy && _joyTouchId < 0 && inJoyZone)
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
                        sawTap = true;
                        continue;
                    }
                }
            }

            if (!sawToolbar) _toolbarTouchId = -1;
            if (!sawJoy) { _joyTouchId = -1; _joyActivated = false; }
            if (!sawTool) _toolTouchId = -1;
            if (!sawInteract) _interactTouchId = -1;
            if (!sawTap) { _tapTouchId = -1; _holdWalking = false; }

            _toolPressed = sawTool;
            _interactPressed = sawInteract;

            // --- 2. Hold-to-Walk ---
            if (_holdWalking && stickVec == Vector2.Zero && Game1.player != null)
            {
                Vector2 playerScreen = WorldToViewport(Game1.player.GetBoundingBox().Center.ToVector2());
                Vector2 diff = _tapCurScreen - playerScreen;
                if (diff.Length() > 26f)
                {
                    diff.Normalize();
                    stickVec = diff;
                }
            }

            // --- 3. A* Tap-to-Move Path Follower ---
            if (stickVec == Vector2.Zero && !_toolPressed && !_interactPressed && _path != null)
                stickVec = StepPathFollower();

            // --- 4. Action Pulses ---
            bool fireX = _toolPressed;
            bool fireA = _interactPressed;
            if (_actionPulseXFrames > 0) { _actionPulseXFrames--; fireX = true; }
            if (_actionPulseAFrames > 0) { _actionPulseAFrames--; fireA = true; }

            // --- 5. Optional Auto-Attack (DEFAULT OFF) ---
            if (_cfg.AutoAttack && !fireX && !fireA)
            {
                bool movingAway = stickVec.LengthSquared() > 0.25f;
                if (AutoToolAndCombat.TryAutoAttack(Game1.currentLocation, Game1.player, movingAway, ref _autoAttackCooldown))
                {
                    _actionPulseXFrames = 2;
                    fireX = true;
                    ClearPath();
                }
            }

            ApplyToPad(pad, stickVec, fireX, fireA, false);
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

                    // 2. Hit-test standard Mobile controls
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
                    ApplyEditDelta(_editDragTarget, _editDragCustomId, _editResizing, d);
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

        private void ApplyEditDelta(EditTarget target, int customId, bool resize, Vector2 deltaPx)
        {
            float dx = deltaPx.X / Math.Max(1f, _vpW);
            float dy = deltaPx.Y / Math.Max(1f, _vpH);
            float unit = Math.Max(1f, Math.Min(_vpW, _vpH));

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
                        _cfg.InvJoyW = Math.Clamp(_cfg.InvJoyW + dx, 0.14f, Math.Min(0.75f, 0.99f - _cfg.InvJoyX));
                        _cfg.InvJoyH = Math.Clamp(_cfg.InvJoyH + dy, 0.20f, Math.Min(0.95f, 0.99f - _cfg.InvJoyY));
                    }
                    else
                    {
                        _cfg.InvJoyX = Math.Clamp(_cfg.InvJoyX + dx, 0.0f, 0.99f - _cfg.InvJoyW);
                        _cfg.InvJoyY = Math.Clamp(_cfg.InvJoyY + dy, 0.0f, 0.99f - _cfg.InvJoyH);
                    }
                }
                else if (logical == EditTarget.Tool)
                {
                    if (resize)
                    {
                        _cfg.InvToolW = Math.Clamp(_cfg.InvToolW + dx, 0.06f, Math.Min(0.45f, 0.99f - _cfg.InvToolX));
                        _cfg.InvToolH = Math.Clamp(_cfg.InvToolH + dy, 0.14f, Math.Min(0.95f, 0.99f - _cfg.InvToolY));
                    }
                    else
                    {
                        _cfg.InvToolX = Math.Clamp(_cfg.InvToolX + dx, 0.01f, 0.99f - _cfg.InvToolW);
                        _cfg.InvToolY = Math.Clamp(_cfg.InvToolY + dy, 0.01f, 0.99f - _cfg.InvToolH);
                    }
                }
                else if (logical == EditTarget.Act)
                {
                    if (resize)
                    {
                        _cfg.InvActW = Math.Clamp(_cfg.InvActW + dx, 0.06f, Math.Min(0.45f, 0.99f - _cfg.InvActX));
                        _cfg.InvActH = Math.Clamp(_cfg.InvActH + dy, 0.14f, Math.Min(0.95f, 0.99f - _cfg.InvActY));
                    }
                    else
                    {
                        _cfg.InvActX = Math.Clamp(_cfg.InvActX + dx, 0.01f, 0.99f - _cfg.InvActW);
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
                    else { _cfg.VisJoyX = Math.Clamp(_cfg.VisJoyX + dx, 0.06f, 0.94f); _cfg.VisJoyY = Math.Clamp(_cfg.VisJoyY + dy, 0.10f, 0.92f); }
                }
                else if (logical == EditTarget.Tool)
                {
                    if (resize) _cfg.VisToolR = Math.Clamp(_cfg.VisToolR + dr, 0.045f, 0.16f);
                    else { _cfg.VisToolX = Math.Clamp(_cfg.VisToolX + dx, 0.06f, 0.94f); _cfg.VisToolY = Math.Clamp(_cfg.VisToolY + dy, 0.10f, 0.92f); }
                }
                else if (logical == EditTarget.Act)
                {
                    if (resize) _cfg.VisActR = Math.Clamp(_cfg.VisActR + dr, 0.045f, 0.16f);
                    else { _cfg.VisActX = Math.Clamp(_cfg.VisActX + dx, 0.06f, 0.94f); _cfg.VisActY = Math.Clamp(_cfg.VisActY + dy, 0.10f, 0.92f); }
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
            if (dist < _activeJoyRadius * 0.11f)
            {
                _joyKnob = curVp;
                return Vector2.Zero;
            }
            if (dist > _activeJoyRadius)
            {
                Vector2 dir = delta / dist;
                _joyKnob = _joyOrigin + dir * _activeJoyRadius;
                return dir;
            }
            _joyKnob = curVp;
            return delta / _activeJoyRadius;
        }

        private bool HandlePinchZoom(TouchCollection touches)
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
                _pinching = false;
                _pinchIdA = -1;
                _pinchIdB = -1;
                return false;
            }

            if (_joyTouchId >= 0 && _joyActivated && _cfg.Mode != MobileControlMode.TapToMove)
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
                return true;
            }

            if (_pinchStartDist > 20f && Game1.options != null)
            {
                float ratio = dist / _pinchStartDist;
                float newZoom = Math.Clamp(_pinchStartZoom * ratio, 0.75f, 2.0f);
                newZoom = (float)Math.Round(newZoom * 50f) / 50f;
                if (Math.Abs(newZoom - Game1.options.desiredBaseZoomLevel) >= 0.015f)
                {
                    Game1.options.desiredBaseZoomLevel = newZoom;
                    _bridge?.SetTargetZoomLevel(newZoom);
                }
            }
            return true;
        }

        private bool TryHandleHudTap(Vector2 vp, out bool openedMenu)
        {
            openedMenu = false;
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
                            Game1.player.CurrentToolIndex = i;
                            try { Game1.playSound("stoneStep"); } catch { }
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

        private void HandleWorldTap(Vector2 vp)
        {
            Farmer who = Game1.player;
            GameLocation loc = Game1.currentLocation;
            if (who == null || loc == null)
                return;

            Vector2 worldPos = ViewportToWorld(vp);
            Point tappedTile = new Point((int)Math.Floor(worldPos.X / 64f), (int)Math.Floor(worldPos.Y / 64f));
            Point playerTile = who.TilePoint;

            TileIntent intent = AutoToolAndCombat.InspectTile(loc, tappedTile, who, _cfg.AutoTool);

            int cheb = Math.Max(Math.Abs(playerTile.X - tappedTile.X), Math.Abs(playerTile.Y - tappedTile.Y));
            if (intent.Kind != TileActionKind.None && cheb <= 1)
            {
                ClearPath();
                TriggerTileIntent(who, tappedTile, intent);
                return;
            }

            List<Point> path = PathFinder.FindPath(loc, playerTile, tappedTile, who, intent.StopAdjacent);
            if (path == null)
                return;

            if (path.Count == 0 && intent.Kind != TileActionKind.None)
            {
                TriggerTileIntent(who, tappedTile, intent);
                return;
            }

            _path = path;
            _pathIdx = 0;
            _targetTile = tappedTile;
            _hasTargetTile = true;
            _pendingIntent = intent;
            _lastPlayerPos = who.Position;
            _stuckFrames = 0;
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
                if (++_stuckFrames > 22)
                {
                    ClearPath();
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
            ClearPath();

            if (who != null && intent.Kind != TileActionKind.None)
            {
                int cheb = Math.Max(Math.Abs(who.TilePoint.X - target.X), Math.Abs(who.TilePoint.Y - target.Y));
                if (cheb <= 1)
                    TriggerTileIntent(who, target, intent);
            }
        }

        private void TriggerTileIntent(Farmer who, Point tile, TileIntent intent)
        {
            if (intent.PreferredToolSlot >= 0 && intent.PreferredToolSlot < who.Items.Count)
                who.CurrentToolIndex = intent.PreferredToolSlot;

            Vector2 targetCenter = new Vector2(tile.X * 64f + 32f, tile.Y * 64f + 32f);
            who.faceGeneralDirection(targetCenter);

            if (intent.Kind == TileActionKind.UseTool)
                _actionPulseXFrames = 3;
            else if (intent.Kind == TileActionKind.Interact)
                _actionPulseAFrames = 3;
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

        private void DrawSaveMenuModal(SpriteBatch sb)
        {
            int px = Math.Max(2, (int)Math.Round(_vpH / 320f));
            sb.Draw(_pixel, new Rectangle(0, 0, _vpW, _vpH), Color.Black * 0.56f);

            // Outer Stardew wood + warm parchment dialog panel
            DrawStardewPixelBox(sb, _savePanelRect, new Color(248, 223, 176), new Color(74, 37, 17), new Color(176, 108, 48), px * 2);

            int titleFs = Math.Max(2, _vpH / 230);
            int btnFs = Math.Max(1, _vpH / 290);
            int rowFs = Math.Max(1, _vpH / 320);

            var slots = _saveSlots.GetSlotsForCurrentSave();
            string title = $"SAVE MENU ({slots.Count})";
            var hdrRect = new Rectangle(_savePanelRect.X + px * 8, _savePanelRect.Y + px * 4, _savePanelRect.Width - _saveCloseRect.Width - px * 18, _saveCloseRect.Height);
            _bridge?.DrawCenteredString(sb, _pixel, title, hdrRect, new Color(74, 37, 17), titleFs);

            DrawStardewPixelButton(sb, _saveCloseRect, "X", new Color(204, 62, 48), new Color(240, 118, 102), new Color(138, 30, 22), Color.White, titleFs, px);

            // Recessed slot list container
            DrawStardewPixelBox(sb, _saveListRect, new Color(230, 194, 134), new Color(115, 65, 29), new Color(248, 226, 184), px);

            if (slots.Count == 0)
            {
                var empty1 = new Rectangle(_saveListRect.X + px * 4, _saveListRect.Y + _saveListRect.Height / 3, _saveListRect.Width - px * 8, _saveListRect.Height / 5);
                var empty2 = new Rectangle(_saveListRect.X + px * 4, empty1.Bottom + px * 2, _saveListRect.Width - px * 8, _saveListRect.Height / 5);
                _bridge?.DrawCenteredString(sb, _pixel, "CHUA CO BAN LUU NAO", empty1, new Color(115, 65, 29), btnFs);
                _bridge?.DrawCenteredString(sb, _pixel, "BAM '+ LUU MOI' DE TAO MOC THOI GIAN", empty2, new Color(142, 88, 44), rowFs);
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

                    Color rowBg = sel ? new Color(252, 214, 108) : new Color(248, 232, 198);
                    Color rowOut = sel ? new Color(196, 106, 20) : new Color(138, 86, 42);
                    Color rowIn = sel ? new Color(255, 240, 178) : new Color(255, 246, 224);
                    DrawStardewPixelBox(sb, rowRect, rowBg, rowOut, rowIn, px);

                    int pad = px * 3;
                    int halfH = Math.Max(12, (rowRect.Height - pad * 2) / 2);
                    var topLine = new Rectangle(rowRect.X + pad, rowRect.Y + pad, rowRect.Width - pad * 2, halfH);
                    var subLine = new Rectangle(rowRect.X + pad, topLine.Bottom, rowRect.Width - pad * 2, rowRect.Height - pad * 2 - halfH);

                    string prefix = $"#{slots.Count - idx} ";
                    _bridge?.DrawCenteredString(sb, _pixel, prefix + slot.FormatHeaderLine(), topLine, new Color(58, 30, 12), rowFs);
                    _bridge?.DrawCenteredString(sb, _pixel, slot.FormatDetailLine(), subLine, new Color(116, 70, 32), Math.Max(1, rowFs));
                }
            }

            // Up/Down scroll buttons when > 4 slots
            bool canUp = _saveScrollIdx > 0;
            bool canDn = _saveScrollIdx + SaveVisibleRows < slots.Count;
            DrawStardewPixelButton(sb, _saveUpBtnRect, "^", canUp ? new Color(196, 134, 68) : new Color(166, 140, 104), new Color(230, 178, 114), new Color(128, 78, 34), Color.White, btnFs, px);
            DrawStardewPixelButton(sb, _saveDnBtnRect, "v", canDn ? new Color(196, 134, 68) : new Color(166, 140, 104), new Color(230, 178, 114), new Color(128, 78, 34), Color.White, btnFs, px);

            // Bottom action buttons: + LUU MOI | TAI BAN NAY | XOA
            bool hasSel = (_saveSelectedIdx >= 0 && _saveSelectedIdx < slots.Count);
            DrawStardewPixelButton(sb, _saveNewBtnRect, "+ LUU MOI", new Color(56, 150, 67), new Color(102, 198, 112), new Color(32, 98, 40), Color.White, btnFs, px);
            DrawStardewPixelButton(sb, _saveLoadBtnRect, "TAI BAN NAY", hasSel ? new Color(210, 133, 30) : new Color(148, 132, 108), hasSel ? new Color(246, 182, 82) : new Color(172, 158, 136), hasSel ? new Color(140, 82, 14) : new Color(108, 94, 74), Color.White, btnFs, px);
            DrawStardewPixelButton(sb, _saveDelBtnRect, "XOA", hasSel ? new Color(200, 56, 44) : new Color(148, 132, 108), hasSel ? new Color(236, 112, 98) : new Color(172, 158, 136), hasSel ? new Color(132, 28, 22) : new Color(108, 94, 74), Color.White, btnFs, px);
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

            if (_editMode)
            {
                DrawEditLayoutOverlay(sb);
                return;
            }

            // 1. Tap-to-Move green target square
            if (_hasTargetTile && _path != null && Game1.currentLocation != null)
            {
                Vector2 tl = WorldToViewport(new Vector2(_targetTile.X * 64f, _targetTile.Y * 64f));
                Vector2 br = WorldToViewport(new Vector2((_targetTile.X + 1) * 64f, (_targetTile.Y + 1) * 64f));
                int w = Math.Max(8, (int)(br.X - tl.X));
                int h = Math.Max(8, (int)(br.Y - tl.Y));
                var tileRect = new Rectangle((int)tl.X, (int)tl.Y, w, h);

                sb.Draw(_pixel, tileRect, new Color(76, 217, 100) * 0.22f);
                DrawRectOutline(sb, tileRect, Math.Max(2, w / 16), new Color(98, 240, 122) * 0.85f);
            }

            // 2. Collapsible Top Utility Bar (BAG | MAP | QST | SAVE | KEY | EDIT | <)
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
                        Color warnBg = _ramCritical
                            ? (pulse ? new Color(235, 24, 24) : new Color(160, 12, 12)) * 0.96f
                            : (pulse ? new Color(230, 138, 16) : new Color(185, 86, 14)) * 0.92f;
                        Color warnBorder = pulse ? new Color(255, 232, 96) : Color.White;
                        sb.Draw(_pixel, _btnSaveRect, warnBg);
                        DrawRectOutline(sb, _btnSaveRect, Math.Max(2, _vpH / 300), warnBorder);
                        int fs = Math.Max(1, _vpH / 320);
                        _bridge?.DrawCenteredString(sb, _pixel, "SAVE!", _btnSaveRect, Color.White, fs);
                    }
                    else
                    {
                        DrawMinimalPill(sb, _btnSaveRect, "SAVE", _saveMenuOpen);
                    }
                    DrawMinimalPill(sb, _btnKeyRect, "KEY", false);
                    DrawMinimalPill(sb, _btnEditRect, "EDIT", false);
                }
                DrawMinimalPill(sb, _btnCollapseRect, _topBarCollapsed ? ">" : "<", false);
            }

            // 3. Mode-specific overlay (ShowTouchHint defaults OFF -> ZERO flashing in Invisible mode!)
            if (_cfg.Mode == MobileControlMode.VisibleJoystick2Btn)
            {
                Vector2 jCenter = (_joyTouchId >= 0 && _joyActivated) ? _joyOrigin : _visJoyCenter;
                Vector2 jKnob = (_joyTouchId >= 0 && _joyActivated) ? _joyKnob : jCenter;
                float alpha = (_joyTouchId >= 0 && _joyActivated) ? 0.55f : 0.30f;

                DrawCircle(sb, jCenter, _visJoyRadius, Color.Black * (alpha * 0.55f));
                DrawRing(sb, jCenter, _visJoyRadius, Color.White * alpha);
                DrawCircle(sb, jKnob, _visJoyRadius * 0.42f, Color.White * (alpha + 0.18f));

                DrawActionCircle(sb, _visToolRect, "USE", _toolPressed, isTool: true);
                DrawActionCircle(sb, _visActRect, "ACT", _interactPressed, isTool: false);
            }
            else if (IsInvisibleBarMode && _cfg.ShowTouchHint)
            {
                if (_joyTouchId >= 0 && _joyActivated)
                {
                    DrawRing(sb, _joyOrigin, _activeJoyRadius, Color.White * 0.22f);
                    DrawCircle(sb, _joyKnob, _activeJoyRadius * 0.34f, Color.White * 0.26f);
                }
                int borderT = Math.Max(2, _vpH / 340);
                if (_toolRippleAlpha > 0.02f)
                {
                    sb.Draw(_pixel, _invToolRect, new Color(255, 195, 77) * (_toolRippleAlpha * 0.14f));
                    DrawRectOutline(sb, _invToolRect, borderT, new Color(255, 214, 120) * (_toolRippleAlpha * 0.36f));
                }
                if (_interactRippleAlpha > 0.02f)
                {
                    sb.Draw(_pixel, _invActRect, new Color(84, 214, 128) * (_interactRippleAlpha * 0.14f));
                    DrawRectOutline(sb, _invActRect, borderT, new Color(130, 240, 165) * (_interactRippleAlpha * 0.36f));
                }
            }

            // 4. User Custom PC Key Buttons
            if (_cfg.CustomKeys.Count > 0)
                DrawCustomKeyButtons(sb);

            // 5. Stardew-Pixel Multi-Slot Save Menu Modal
            if (_saveMenuOpen)
                DrawSaveMenuModal(sb);

            // 6. Save Feedback & High-RAM Warning Toast Banner
            if (!string.IsNullOrEmpty(_saveToastText) && nowSec < _saveToastUntil)
            {
                int px = Math.Max(2, (int)Math.Round(_vpH / 320f));
                int tw = isRamWarn
                    ? Math.Clamp((int)(_vpW * 0.68f), 340, _vpW - 32)
                    : Math.Clamp((int)(_vpW * 0.42f), 260, _vpW - 40);
                int th = Math.Max(36, (int)(_vpH * 0.068f));
                var toastR = new Rectangle((_vpW - tw) / 2, _vpH - th - Math.Max(18, _vpH / 26), tw, th);
                Color boxFill = isRamWarn ? new Color(92, 18, 16) * 0.95f : new Color(46, 30, 16) * 0.94f;
                Color boxOut = isRamWarn ? new Color(255, 96, 76) : new Color(246, 198, 86);
                Color boxIn = isRamWarn ? new Color(255, 208, 92) : new Color(138, 86, 42);
                DrawStardewPixelBox(sb, toastR, boxFill, boxOut, boxIn, px);
                int fs = Math.Max(1, _vpH / 300);
                _bridge?.DrawCenteredString(sb, _pixel, _saveToastText, toastR, new Color(255, 240, 190), fs);
            }
        }

        private void DrawCustomKeyButtons(SpriteBatch sb)
        {
            int bt = Math.Max(1, _vpH / 420);
            for (int i = 0; i < _cfg.CustomKeys.Count; i++)
            {
                var btn = _cfg.CustomKeys[i];
                Rectangle r = GetCustomKeyRect(btn);
                bool held = _customBtnsHeldNow.Contains(btn.Id);

                Color bg = held ? new Color(10, 132, 255) * 0.72f : Color.Black * 0.44f;
                Color border = held ? Color.White * 0.85f : Color.White * 0.34f;
                sb.Draw(_pixel, r, bg);
                DrawRectOutline(sb, r, bt, border);

                int fontSize = Math.Max(1, Math.Min(r.Height / 18, r.Width / Math.Max(1, btn.Label.Length * 9)));
                fontSize = Math.Clamp(fontSize, 1, Math.Max(2, _vpH / 180));
                _bridge?.DrawCenteredString(sb, _pixel, btn.Label, r, Color.White * (held ? 1.0f : 0.92f), fontSize);
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
                    DrawMinimalPill(sb, _btnEditRect, "EDIT", false);
                }
                DrawMinimalPill(sb, _btnCollapseRect, _topBarCollapsed ? ">" : "<", false);
                Color tbBorder = _editSelected == EditTarget.TopBar ? Color.Yellow : Color.White * 0.75f;
                DrawRectOutline(sb, _topBarGroupRect, Math.Max(2, _vpH / 320), tbBorder);
            }

            // Top-center Edit Layout toolbar (+PHIM / XOA / RESET / HUY / LUU)
            int bt = Math.Max(2, _vpH / 360);
            sb.Draw(_pixel, _editToolbarRect, new Color(24, 24, 28) * 0.92f);
            DrawRectOutline(sb, _editToolbarRect, bt, Color.White * 0.28f);

            int fs = Math.Max(2, _vpH / 280);
            DrawToolbarActionButton(sb, _editAddKeyBtnRect, "+PHIM", new Color(175, 82, 222), fs);
            bool canDel = (_editSelected == EditTarget.CustomKey && _editSelectedCustomId > 0);
            DrawToolbarActionButton(sb, _editDelKeyBtnRect, "XOA", canDel ? new Color(255, 69, 58) : new Color(72, 72, 74), fs);
            DrawToolbarActionButton(sb, _editResetBtnRect, "RESET", new Color(142, 142, 147), fs);
            DrawToolbarActionButton(sb, _editCancelBtnRect, "HUY", new Color(255, 149, 0), fs);
            DrawToolbarActionButton(sb, _editSaveBtnRect, "LUU", new Color(48, 209, 88), fs);

            // In-Game PC Key Picker Modal
            if (_keyPickerOpen)
                DrawKeyPickerModal(sb);
        }

        private void DrawKeyPickerModal(SpriteBatch sb)
        {
            sb.Draw(_pixel, new Rectangle(0, 0, _vpW, _vpH), Color.Black * 0.55f);
            int bt = Math.Max(2, _vpH / 320);
            sb.Draw(_pixel, _pickerPanelRect, new Color(28, 28, 32) * 0.96f);
            DrawRectOutline(sb, _pickerPanelRect, bt, new Color(175, 82, 222) * 0.90f);

            int titleFs = Math.Max(2, _vpH / 250);
            var hdrRect = new Rectangle(_pickerPanelRect.X + 16, _pickerPanelRect.Y + 8, _pickerPanelRect.Width - 120, Math.Max(36, _vpH / 16));
            _bridge?.DrawCenteredString(sb, _pixel, "CHON PHIM PC DE THEM", hdrRect, Color.White, titleFs);
            DrawToolbarActionButton(sb, _pickerCloseRect, "X", new Color(255, 69, 58), titleFs);

            int cellFs = Math.Max(1, _vpH / 300);
            for (int k = 0; k < PcKeyCatalog.Length; k++)
            {
                Rectangle cell = GetKeyPickerCellRect(k);
                Color bg = (k < 12) ? new Color(10, 132, 255) * 0.45f : new Color(58, 58, 66) * 0.90f;
                sb.Draw(_pixel, cell, bg);
                DrawRectOutline(sb, cell, Math.Max(1, _vpH / 500), Color.White * 0.28f);
                _bridge?.DrawCenteredString(sb, _pixel, PcKeyCatalog[k].Label, cell, Color.White, cellFs);
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
            int t = Math.Max(3, _vpH / 240);
            sb.Draw(_pixel, r, accent * (selected ? 0.34f : 0.20f));
            DrawRectOutline(sb, r, t, selected ? Color.White : accent * 0.92f);

            int fs = Math.Max(1, Math.Min(r.Height / 18, Math.Max(2, _vpH / 240)));
            _bridge?.DrawCenteredString(sb, _pixel, label, r, Color.White, fs);

            int g = Math.Clamp(Math.Min(r.Width, r.Height) / 3, 18, Math.Max(24, _vpH / 28));
            var gripRect = new Rectangle(r.Right - g, r.Bottom - g, g, g);
            sb.Draw(_pixel, gripRect, Color.White * (selected ? 0.94f : 0.72f));
            DrawRectOutline(sb, gripRect, Math.Max(2, t / 2), accent);
        }

        private void DrawEditableCircleBox(SpriteBatch sb, Vector2 center, float radius, string label, Color accent, bool selected)
        {
            DrawCircle(sb, center, radius, accent * (selected ? 0.34f : 0.22f));
            DrawRing(sb, center, radius, selected ? Color.White : accent * 0.95f);
            int d = (int)(radius * 2f);
            var box = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), d, d);
            int fs = Math.Max(2, _vpH / 230);
            _bridge?.DrawCenteredString(sb, _pixel, label, box, Color.White, fs);

            DrawRectOutline(sb, box, Math.Max(1, _vpH / 450), Color.White * 0.35f);
            int g = Math.Max(22, _vpH / 28);
            var gripRect = new Rectangle(box.Right - g, box.Bottom - g, g, g);
            sb.Draw(_pixel, gripRect, Color.White * (selected ? 0.92f : 0.70f));
            DrawRectOutline(sb, gripRect, 2, accent);
        }

        private void DrawToolbarActionButton(SpriteBatch sb, Rectangle r, string text, Color tint, int fontSize)
        {
            sb.Draw(_pixel, r, tint * 0.88f);
            DrawRectOutline(sb, r, Math.Max(1, _vpH / 440), Color.White * 0.35f);
            _bridge?.DrawCenteredString(sb, _pixel, text, r, Color.White, fontSize);
        }

        private void DrawMinimalPill(SpriteBatch sb, Rectangle r, string label, bool active)
        {
            Color bg = active ? new Color(10, 132, 255) * 0.78f : Color.Black * 0.42f;
            sb.Draw(_pixel, r, bg);
            DrawRectOutline(sb, r, Math.Max(1, _vpH / 420), Color.White * 0.28f);
            int fontSize = Math.Max(1, _vpH / 320);
            _bridge?.DrawCenteredString(sb, _pixel, label, r, Color.White * 0.92f, fontSize);
        }

        private void DrawActionCircle(SpriteBatch sb, Rectangle r, string label, bool pressed, bool isTool)
        {
            Vector2 c = r.Center.ToVector2();
            float rad = r.Width * 0.5f;
            Color tint = isTool ? new Color(255, 184, 77) : new Color(84, 214, 128);
            float bgAlpha = pressed ? 0.52f : 0.24f;
            float ringAlpha = pressed ? 0.85f : 0.46f;

            DrawCircle(sb, c, rad, (pressed ? tint : Color.Black) * bgAlpha);
            DrawRing(sb, c, rad, Color.White * ringAlpha);
            int fontSize = Math.Max(2, (int)(rad / 24f));
            _bridge?.DrawCenteredString(sb, _pixel, label, r, Color.White * (pressed ? 1.0f : 0.86f), fontSize);
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

            // 3. Collapsible Top Utility Bar
            int leftX = (int)(_cfg.TopBarX * _vpW);
            int topY = (int)(_cfg.TopBarY * _vpH);
            int pillH = (int)Math.Clamp(unit * 0.056f, 28f, unit * 0.08f);
            int pillW = (int)(pillH * 1.85f);
            int modsW = (int)(pillH * 2.15f);
            int colW = (int)(pillH * 1.10f);
            int pillGap = Math.Max(6, pillH / 5);

            int cx = leftX;
            _btnBagRect = new Rectangle(cx, topY, pillW, pillH);
            cx += pillW + pillGap;

            if (!_topBarCollapsed)
            {
                _btnMapRect = new Rectangle(cx, topY, pillW, pillH); cx += pillW + pillGap;
                _btnQuestRect = new Rectangle(cx, topY, pillW, pillH); cx += pillW + pillGap;
                _btnSaveRect = new Rectangle(cx, topY, modsW, pillH); cx += modsW + pillGap;
                _btnKeyRect = new Rectangle(cx, topY, pillW, pillH); cx += pillW + pillGap;
                _btnEditRect = new Rectangle(cx, topY, modsW, pillH); cx += modsW + pillGap;
            }
            else
            {
                _btnMapRect = _btnQuestRect = _btnSaveRect = _btnKeyRect = _btnEditRect = Rectangle.Empty;
            }

            _btnCollapseRect = new Rectangle(cx, topY, colW, pillH);
            _topBarGroupRect = new Rectangle(leftX - 4, topY - 4, (_btnCollapseRect.Right - leftX) + 8, pillH + 8);

            // Context Menu Helper Bar
            int mx0 = (int)Math.Max(_vpW * 0.048f, unit * 0.06f);
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
