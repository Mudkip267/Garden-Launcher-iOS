using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace SDVMobileControls
{
    /// <summary>
    /// Reflection bridge bound to the EXACT live SDViOSTouchControlsMod assembly loaded by SMAPI,
    /// avoiding duplicate assembly identity issues between SDViOSTouchControls.dll and SDViOSTouchControlsMod.dll.
    /// </summary>
    internal sealed class PadBridge
    {
        private readonly Type _padType;
        private readonly MethodInfo _getIsSettingsOpen;
        private readonly MethodInfo _getIsEditLayoutMode;
        private readonly MethodInfo _getIsHotkeyDrawerOpen;
        private readonly MethodInfo _setIsHotkeyDrawerOpen;
        private readonly MethodInfo _getIsKeyPickerOpen;

        private readonly MethodInfo _setLeftStick;
        private readonly MethodInfo _getButtonX;
        private readonly MethodInfo _setButtonX;
        private readonly MethodInfo _setButtonA;
        private readonly MethodInfo _setButtonB;
        private readonly MethodInfo _setButtonY;
        private readonly MethodInfo _setButtonLT;
        private readonly MethodInfo _setButtonRT;
        private readonly MethodInfo _setButtonMenu;
        private readonly MethodInfo _setPlayerRequestedMenuOpen;
        private readonly MethodInfo _resetSimulatedMouse;
        private readonly MethodInfo _setSimulatedMousePosition;
        private readonly MethodInfo _setSimulatedMouseLeftDown;
        private readonly MethodInfo _setSimulatedMouseRightDown;
        private readonly MethodInfo _forwardInputToGame;
        private readonly MethodInfo _syncMenuBarConfig;
        private readonly MethodInfo _triggerVirtualKeyboard;
        private readonly MethodInfo _getSingleItem;
        private readonly MethodInfo _setSingleItem;
        private readonly MethodInfo _getHalfStack;
        private readonly MethodInfo _setHalfStack;
        private readonly MethodInfo _getQuickTransfer;
        private readonly MethodInfo _setQuickTransfer;

        private readonly PropertyInfo _qsmInstanceProp;
        private readonly PropertyInfo _qsmIsReloadingProp;
        private readonly PropertyInfo _qsmIsMiddaySavingProp;
        private readonly MethodInfo _qsmHasSavedState;
        private readonly MethodInfo _qsmGetSavedState;
        private readonly MethodInfo _qsmCanSaveNow;
        private readonly MethodInfo _qsmQuickSave;
        private readonly MethodInfo _qsmQuickLoad;
        private readonly MethodInfo _qsmClearStateData;
        private readonly MethodInfo _qsmGetStateFilePath;
        private readonly FieldInfo _qsmCachedStateField;
        private readonly PropertyInfo _qssTimeProp;
        private readonly PropertyInfo _qssLocProp;
        private readonly PropertyInfo _qssTimestampProp;

        private readonly PropertyInfo _settingsInstanceProp;
        private readonly PropertyInfo _settingsTargetZoomProp;
        private readonly PropertyInfo _settingsMouseModeProp;
        private readonly FieldInfo _rawScreenWidthField;
        private readonly FieldInfo _rawScreenHeightField;
        private readonly FieldInfo _activeKeysListField;
        private readonly FieldInfo _simKeysSetField;
        private readonly FieldInfo _inputInstanceField;
        private readonly FieldInfo _currentKeyboardStateFieldInfo;
        private readonly FieldInfo _emptyKeyboardStateField;
        private readonly object _mouseModePointAndClick;

        private readonly MethodInfo _drawCenteredString;

        // Pre-allocated tiny argument arrays to avoid per-frame allocations
        private readonly object[] _arg1 = new object[1];
        private readonly object[] _argBoolFalse = new object[] { false };
        private readonly object[] _argBoolTrue = new object[] { true };
        private readonly object[] _argDrawStr = new object[6];

        public PadBridge(Type padType)
        {
            _padType = padType;
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            _getIsSettingsOpen = padType.GetMethod("get_IsSettingsOpen", any);
            _getIsEditLayoutMode = padType.GetMethod("get_IsEditLayoutMode", any);
            _getIsHotkeyDrawerOpen = padType.GetMethod("get_IsHotkeyDrawerOpen", any);
            _setIsHotkeyDrawerOpen = padType.GetMethod("set_IsHotkeyDrawerOpen", any);
            _getIsKeyPickerOpen = padType.GetMethod("get_IsKeyPickerOpen", any);

            _setLeftStick = padType.GetMethod("set_LeftStick", any);
            _getButtonX = padType.GetMethod("get_ButtonX", any);
            _setButtonX = padType.GetMethod("set_ButtonX", any);
            _setButtonA = padType.GetMethod("set_ButtonA", any);
            _setButtonB = padType.GetMethod("set_ButtonB", any);
            _setButtonY = padType.GetMethod("set_ButtonY", any);
            _setButtonLT = padType.GetMethod("set_ButtonLT", any);
            _setButtonRT = padType.GetMethod("set_ButtonRT", any);
            _setButtonMenu = padType.GetMethod("set_ButtonMenu", any);
            _setPlayerRequestedMenuOpen = padType.GetMethod("set_PlayerRequestedMenuOpen", any);
            _resetSimulatedMouse = padType.GetMethod("ResetSimulatedMouse", any);
            _setSimulatedMousePosition = padType.GetMethod("set_SimulatedMousePosition", any);
            _setSimulatedMouseLeftDown = padType.GetMethod("set_SimulatedMouseLeftDown", any);
            _setSimulatedMouseRightDown = padType.GetMethod("set_SimulatedMouseRightDown", any);
            _forwardInputToGame = padType.GetMethod("ForwardInputToGame", any);
            _syncMenuBarConfig = padType.GetMethod("SyncMenuBarConfig", any);
            _triggerVirtualKeyboard = padType.GetMethod("TriggerVirtualKeyboard", any);
            _getSingleItem = padType.GetMethod("get_IsSingleItemMode", any);
            _setSingleItem = padType.GetMethod("set_IsSingleItemMode", any);
            _getHalfStack = padType.GetMethod("get_IsHalfStackMode", any);
            _setHalfStack = padType.GetMethod("set_IsHalfStackMode", any);
            _getQuickTransfer = padType.GetMethod("get_IsQuickTransferMode", any);
            _setQuickTransfer = padType.GetMethod("set_IsQuickTransferMode", any);

            Assembly asm = padType.Assembly;

            Type qsmType = asm.GetType("SDViOSTouchControls.QuickSaveManager");
            _qsmInstanceProp = qsmType?.GetProperty("Instance", stat);
            _qsmIsReloadingProp = qsmType?.GetProperty("IsReloading", any);
            _qsmIsMiddaySavingProp = qsmType?.GetProperty("IsMiddaySaving", any);
            _qsmHasSavedState = qsmType?.GetMethod("HasSavedState", any);
            _qsmGetSavedState = qsmType?.GetMethod("GetSavedState", any);
            _qsmCanSaveNow = qsmType?.GetMethod("CanSaveNow", any);
            _qsmQuickSave = qsmType?.GetMethod("QuickSave", any);
            _qsmQuickLoad = qsmType?.GetMethod("QuickLoad", any);
            _qsmClearStateData = qsmType?.GetMethod("ClearStateData", any);
            _qsmGetStateFilePath = qsmType?.GetMethod("GetStateFilePath", any);
            _qsmCachedStateField = qsmType?.GetField("_cachedState", any);

            Type qssType = asm.GetType("SDViOSTouchControls.QuickSaveState");
            _qssTimeProp = qssType?.GetProperty("TimeOfDay", any);
            _qssLocProp = qssType?.GetProperty("LocationName", any);
            _qssTimestampProp = qssType?.GetProperty("TimestampText", any);

            Type setType = asm.GetType("SDViOSTouchControls.TouchOverlaySettings");
            _settingsInstanceProp = setType?.GetProperty("Instance", stat);
            _settingsTargetZoomProp = setType?.GetProperty("TargetZoomLevel", any);
            _settingsMouseModeProp = setType?.GetProperty("MouseControlMode", any);
            _rawScreenWidthField = padType.GetField("_rawScreenWidth", any);
            _rawScreenHeightField = padType.GetField("_rawScreenHeight", any);
            _activeKeysListField = padType.GetField("_activeKeysList", any);
            _simKeysSetField = padType.GetField("_simKeysSet", any);
            _inputInstanceField = padType.GetField("_inputInstance", stat);
            _currentKeyboardStateFieldInfo = padType.GetField("_currentKeyboardStateField", stat);
            _emptyKeyboardStateField = padType.GetField("_emptyKeyboardState", stat);
            if (_settingsMouseModeProp != null && _settingsMouseModeProp.PropertyType.IsEnum)
            {
                try { _mouseModePointAndClick = Enum.ToObject(_settingsMouseModeProp.PropertyType, 0); } catch { }
            }

            Type fontType = asm.GetType("SDViOSTouchControls.OverlayFont");
            _drawCenteredString = fontType?.GetMethod("DrawCenteredString", stat, null,
                new[] { typeof(SpriteBatch), typeof(Texture2D), typeof(string), typeof(Rectangle), typeof(Color), typeof(int) }, null);
        }

        public bool IsFullScreenModalOpen(object pad)
        {
            if (pad == null) return false;
            return GetBool(_getIsSettingsOpen, pad) ||
                   GetBool(_getIsEditLayoutMode, pad) ||
                   GetBool(_getIsKeyPickerOpen, pad);
        }

        public bool IsAnyOverlayOpen(object pad)
        {
            if (pad == null) return false;
            return IsFullScreenModalOpen(pad) ||
                   GetBool(_getIsHotkeyDrawerOpen, pad);
        }

        public void CloseHotkeyDrawer(object pad)
        {
            if (pad == null || _getIsHotkeyDrawerOpen == null || _setIsHotkeyDrawerOpen == null) return;
            try
            {
                if (GetBool(_getIsHotkeyDrawerOpen, pad))
                    _setIsHotkeyDrawerOpen.Invoke(pad, _argBoolFalse);
            }
            catch { }
        }

        public bool IsQuickSaveReloading()
        {
            try
            {
                object inst = _qsmInstanceProp?.GetValue(null);
                if (inst == null || _qsmIsReloadingProp == null) return false;
                return (bool)_qsmIsReloadingProp.GetValue(inst);
            }
            catch { return false; }
        }

        public bool IsMiddaySaving()
        {
            try
            {
                object inst = _qsmInstanceProp?.GetValue(null);
                if (inst == null || _qsmIsMiddaySavingProp == null) return false;
                return (bool)_qsmIsMiddaySavingProp.GetValue(inst);
            }
            catch { return false; }
        }

        public bool HasQuickSave()
        {
            try
            {
                object inst = _qsmInstanceProp?.GetValue(null);
                if (inst == null || _qsmHasSavedState == null) return false;
                return (bool)_qsmHasSavedState.Invoke(inst, null);
            }
            catch { return false; }
        }

        public string GetQuickSaveSummary()
        {
            try
            {
                object inst = _qsmInstanceProp?.GetValue(null);
                if (inst == null || _qsmGetSavedState == null) return null;
                object st = _qsmGetSavedState.Invoke(inst, null);
                if (st == null) return null;
                string ts = _qssTimestampProp?.GetValue(st) as string;
                string loc = _qssLocProp?.GetValue(st) as string;
                int tod = _qssTimeProp != null ? (int)_qssTimeProp.GetValue(st) : 0;
                if (!string.IsNullOrEmpty(ts) && !string.IsNullOrEmpty(loc))
                    return $"{ts} @ {loc}";
                if (tod > 0 && !string.IsNullOrEmpty(loc))
                {
                    int hh = (tod / 100) % 24;
                    int mm = tod % 100;
                    return $"{hh:00}:{mm:00} @ {loc}";
                }
                return !string.IsNullOrEmpty(loc) ? loc : "CO BAN LUU NHANH";
            }
            catch { return null; }
        }

        public bool TryQuickSave(out string message)
        {
            message = "KHONG THE LUU LUC NAY";
            try
            {
                object inst = _qsmInstanceProp?.GetValue(null);
                if (inst == null || _qsmQuickSave == null)
                {
                    message = "QUICKSAVE CHUA SAN SANG";
                    return false;
                }
                if (_qsmCanSaveNow != null)
                {
                    object[] args = new object[] { null };
                    bool can = (bool)_qsmCanSaveNow.Invoke(inst, args);
                    if (!can)
                    {
                        string reason = args[0] as string;
                        message = string.IsNullOrEmpty(reason) ? "KHONG THE LUU LUC NAY" : reason.ToUpperInvariant();
                        return false;
                    }
                }
                bool ok = (bool)_qsmQuickSave.Invoke(inst, null);
                message = ok ? "DA LUU NHANH!" : "LUU THAT BAI";
                return ok;
            }
            catch (Exception ex)
            {
                message = "LOI LUU: " + ex.Message;
                return false;
            }
        }

        public bool TryQuickLoad(out string message)
        {
            message = "CHUA CO QUICK SAVE";
            try
            {
                object inst = _qsmInstanceProp?.GetValue(null);
                if (inst == null || _qsmQuickLoad == null) return false;
                if (!HasQuickSave()) return false;
                bool ok = (bool)_qsmQuickLoad.Invoke(inst, null);
                message = ok ? "DANG TAI QUICK SAVE..." : "TAI THAT BAI";
                return ok;
            }
            catch (Exception ex)
            {
                message = "LOI TAI: " + ex.Message;
                return false;
            }
        }

        public bool ClearQuickSave()
        {
            try
            {
                object inst = _qsmInstanceProp?.GetValue(null);
                if (inst == null || _qsmClearStateData == null) return false;
                _qsmClearStateData.Invoke(inst, null);
                return true;
            }
            catch { return false; }
        }

        public string GetQuickSaveStateFilePath(string saveFolder)
        {
            try
            {
                object inst = _qsmInstanceProp?.GetValue(null);
                if (inst == null || _qsmGetStateFilePath == null) return null;
                return _qsmGetStateFilePath.Invoke(inst, new object[] { saveFolder }) as string;
            }
            catch { return null; }
        }

        public void InvalidateQuickSaveCache()
        {
            try
            {
                object inst = _qsmInstanceProp?.GetValue(null);
                if (inst != null && _qsmCachedStateField != null)
                    _qsmCachedStateField.SetValue(inst, null);
            }
            catch { }
        }

        public void ToggleHotkeyDrawer(object pad)
        {
            if (pad == null || _getIsHotkeyDrawerOpen == null || _setIsHotkeyDrawerOpen == null) return;
            bool cur = GetBool(_getIsHotkeyDrawerOpen, pad);
            _setIsHotkeyDrawerOpen.Invoke(pad, cur ? _argBoolFalse : _argBoolTrue);
        }

        public void SetPlayerRequestedMenuOpen(object pad, bool value)
        {
            try { _setPlayerRequestedMenuOpen?.Invoke(pad, value ? _argBoolTrue : _argBoolFalse); } catch { }
        }

        public void SyncMenuBarConfig(object pad)
        {
            try { _syncMenuBarConfig?.Invoke(pad, _argBoolFalse); } catch { }
        }

        public void TriggerVirtualKeyboard(object pad)
        {
            try { _triggerVirtualKeyboard?.Invoke(pad, null); } catch { }
        }

        public bool GetButtonX(object pad) => GetBool(_getButtonX, pad);
        public bool GetSingleItemMode(object pad) => GetBool(_getSingleItem, pad);
        public bool GetHalfStackMode(object pad) => GetBool(_getHalfStack, pad);
        public bool GetQuickTransferMode(object pad) => GetBool(_getQuickTransfer, pad);

        public void ToggleSingleItemMode(object pad)
        {
            if (pad == null) return;
            bool next = !GetSingleItemMode(pad);
            try
            {
                _setSingleItem?.Invoke(pad, next ? _argBoolTrue : _argBoolFalse);
                if (next)
                {
                    _setHalfStack?.Invoke(pad, _argBoolFalse);
                    _setQuickTransfer?.Invoke(pad, _argBoolFalse);
                }
            }
            catch { }
        }

        public void ToggleHalfStackMode(object pad)
        {
            if (pad == null) return;
            bool next = !GetHalfStackMode(pad);
            try
            {
                _setHalfStack?.Invoke(pad, next ? _argBoolTrue : _argBoolFalse);
                if (next)
                {
                    _setSingleItem?.Invoke(pad, _argBoolFalse);
                    _setQuickTransfer?.Invoke(pad, _argBoolFalse);
                }
            }
            catch { }
        }

        public void ToggleQuickTransferMode(object pad)
        {
            if (pad == null) return;
            bool next = !GetQuickTransferMode(pad);
            try
            {
                _setQuickTransfer?.Invoke(pad, next ? _argBoolTrue : _argBoolFalse);
                if (next)
                {
                    _setSingleItem?.Invoke(pad, _argBoolFalse);
                    _setHalfStack?.Invoke(pad, _argBoolFalse);
                }
            }
            catch { }
        }

        public void SetTargetZoomLevel(float zoom)
        {
            try
            {
                object inst = _settingsInstanceProp?.GetValue(null);
                if (inst != null && _settingsTargetZoomProp != null)
                    _settingsTargetZoomProp.SetValue(inst, zoom);
            }
            catch { }
        }

        public void SyncScreenBoundsAndMouseOverride(object pad, int rawW, int rawH)
        {
            if (pad == null) return;
            try
            {
                if (rawW > 0 && _rawScreenWidthField != null)
                    _rawScreenWidthField.SetValue(pad, rawW);
                if (rawH > 0 && _rawScreenHeightField != null)
                    _rawScreenHeightField.SetValue(pad, rawH);
                if (_settingsInstanceProp != null && _settingsMouseModeProp != null && _mouseModePointAndClick != null)
                {
                    object st = _settingsInstanceProp.GetValue(null);
                    if (st != null)
                    {
                        object cur = _settingsMouseModeProp.GetValue(st);
                        if (cur != null && Convert.ToInt32(cur) == 2) // 2 = MouseMode.Disabled breaks PrefixMouseGetState!
                            _settingsMouseModeProp.SetValue(st, _mouseModePointAndClick);
                    }
                }
            }
            catch { }
        }

        public void ApplyInput(object pad, Vector2 stick, bool btnX, bool btnA, bool btnMenu)
        {
            ApplyInputWithMouse(pad, stick, btnX, btnA, btnMenu, null, false);
        }

        public void ResetVirtualPad(object pad)
        {
            if (pad == null) return;
            _arg1[0] = Vector2.Zero;
            _setLeftStick?.Invoke(pad, _arg1);
            _setButtonX?.Invoke(pad, _argBoolFalse);
            _setButtonA?.Invoke(pad, _argBoolFalse);
            _setButtonB?.Invoke(pad, _argBoolFalse);
            _setButtonY?.Invoke(pad, _argBoolFalse);
            _setButtonLT?.Invoke(pad, _argBoolFalse);
            _setButtonRT?.Invoke(pad, _argBoolFalse);
            _setButtonMenu?.Invoke(pad, _argBoolFalse);
            var ext = ModEntry.ExternalInput;
            if (ext != null && ext.ShouldPushMouseStateNow)
            {
                var ms = ext.CurrentMouseState;
                _arg1[0] = new Point(ms.X, ms.Y);
                _setSimulatedMousePosition?.Invoke(pad, _arg1);
                _setSimulatedMouseLeftDown?.Invoke(pad, _argBoolFalse);
                _setSimulatedMouseRightDown?.Invoke(pad, _argBoolFalse);
            }
            else
            {
                // CRITICAL: In menus driven by native SnappyMenus, keep TouchVirtualPad.IsMouseOverridden=false
                // so SDViOSTouchControls.PrefixSetMousePositionRaw & PrefixMouseGetState never block cursor snaps!
                _resetSimulatedMouse?.Invoke(pad, null);
            }

            // Clear any leftover touch-simulated WASD keys inside TouchVirtualPad and Game1.input
            // WITHOUT invoking ForwardInputToGame() (which would call Game1.player.Halt() and
            // break the farmer's running animation when an external gamepad is driving movement!).
            try
            {
                if (_activeKeysListField?.GetValue(pad) is List<Keys> activeKeys && activeKeys.Count > 0)
                    activeKeys.Clear();
                if (_simKeysSetField?.GetValue(pad) is HashSet<Keys> simSet && simSet.Count > 0)
                    simSet.Clear();
                if (ModEntry.ExternalInput == null || !ModEntry.ExternalInput.HasPhysicalKeysPressed)
                {
                    object inp = _inputInstanceField?.GetValue(null) ?? StardewValley.Game1.input;
                    if (inp != null)
                    {
                        object emptyKb = _emptyKeyboardStateField?.GetValue(null) ?? default(KeyboardState);
                        if (_currentKeyboardStateFieldInfo?.GetValue(null) is FieldInfo curKbFld)
                            curKbFld.SetValue(inp, emptyKb);
                        FieldInfo sInputKb = inp.GetType().GetField("<KeyboardState>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        sInputKb?.SetValue(inp, emptyKb);
                    }
                }
            }
            catch { }
        }

        public void ApplyPhysicalGamePad(object pad, Vector2 stick, bool btnX, bool btnA, bool btnB, bool btnY, bool btnLT, bool btnRT, bool btnMenu)
        {
            if (pad == null) return;
            _arg1[0] = stick;
            _setLeftStick?.Invoke(pad, _arg1);
            _setButtonX?.Invoke(pad, btnX ? _argBoolTrue : _argBoolFalse);
            _setButtonA?.Invoke(pad, btnA ? _argBoolTrue : _argBoolFalse);
            _setButtonB?.Invoke(pad, btnB ? _argBoolTrue : _argBoolFalse);
            _setButtonY?.Invoke(pad, btnY ? _argBoolTrue : _argBoolFalse);
            _setButtonLT?.Invoke(pad, btnLT ? _argBoolTrue : _argBoolFalse);
            _setButtonRT?.Invoke(pad, btnRT ? _argBoolTrue : _argBoolFalse);
            _setButtonMenu?.Invoke(pad, btnMenu ? _argBoolTrue : _argBoolFalse);
            _resetSimulatedMouse?.Invoke(pad, null);
            _forwardInputToGame?.Invoke(pad, null);
        }

        public void ApplyInputWithMouse(object pad, Vector2 stick, bool btnX, bool btnA, bool btnMenu, Point? mousePos, bool mouseLeftDown, bool mouseRightDown = false, int rawW = 0, int rawH = 0)
        {
            if (pad == null) return;
            if (rawW > 0 && rawH > 0)
                SyncScreenBoundsAndMouseOverride(pad, rawW, rawH);
            _arg1[0] = stick;
            _setLeftStick?.Invoke(pad, _arg1);
            _setButtonX?.Invoke(pad, btnX ? _argBoolTrue : _argBoolFalse);
            _setButtonA?.Invoke(pad, btnA ? _argBoolTrue : _argBoolFalse);
            _setButtonB?.Invoke(pad, _argBoolFalse);
            _setButtonY?.Invoke(pad, _argBoolFalse);
            _setButtonLT?.Invoke(pad, _argBoolFalse);
            _setButtonRT?.Invoke(pad, _argBoolFalse);
            _setButtonMenu?.Invoke(pad, btnMenu ? _argBoolTrue : _argBoolFalse);
            if (mousePos.HasValue)
            {
                _arg1[0] = mousePos.Value;
                _setSimulatedMousePosition?.Invoke(pad, _arg1);
                _setSimulatedMouseLeftDown?.Invoke(pad, mouseLeftDown ? _argBoolTrue : _argBoolFalse);
                _setSimulatedMouseRightDown?.Invoke(pad, mouseRightDown ? _argBoolTrue : _argBoolFalse);
            }
            else
            {
                _resetSimulatedMouse?.Invoke(pad, null);
            }
            _forwardInputToGame?.Invoke(pad, null);
        }

        public void DrawCenteredString(SpriteBatch sb, Texture2D pixel, string text, Rectangle rect, Color color, int scale)
        {
            if (_drawCenteredString == null) return;
            try
            {
                _argDrawStr[0] = sb;
                _argDrawStr[1] = pixel;
                _argDrawStr[2] = text;
                _argDrawStr[3] = rect;
                _argDrawStr[4] = color;
                _argDrawStr[5] = scale;
                _drawCenteredString.Invoke(null, _argDrawStr);
            }
            catch { }
        }

        private static bool GetBool(MethodInfo m, object obj)
        {
            if (m == null || obj == null) return false;
            try { return (bool)m.Invoke(obj, null); } catch { return false; }
        }
    }
}
