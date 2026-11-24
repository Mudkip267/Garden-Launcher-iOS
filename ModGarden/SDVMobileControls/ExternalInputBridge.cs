using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;

namespace SDVMobileControls
{
    internal enum ExternalInputMode
    {
        Touch = 0,
        Gamepad = 1,
        KeyboardMouse = 2
    }

    /// <summary>
    /// Bridges native iOS 14–27+ GameController.framework peripherals (ExternalInput.m in SDVModGarden.framework)
    /// into MonoGame, Stardew Valley, and SMAPI:
    ///   - Full Physical Gamepad (sticks, triggers, bumpers, L3/R3, Options/Menu, D-Pad, full Menu/Shop/Inventory cursor snapping)
    ///   - Full Physical Hardware Keyboard (WASD, Esc, all PC mod hotkeys, plus native TextBox/Chat typing)
    ///   - Full Physical Mouse / Trackpad (hover cursor, left/right/middle/X1/X2 clicks, scroll wheel)
    /// </summary>
    internal sealed class ExternalInputBridge
    {
        private const int ExpectedStructSize = 212;

        // Exact byte offsets of SDVExtInputSnapshot in ExternalInput.m (all 4-byte aligned on arm64)
        private const int OffStructSize = 0;
        private const int OffSuppress = 4;
        private const int OffGpConnected = 8;
        private const int OffGpButtonsMask = 12;
        private const int OffGpLeftX = 16;
        private const int OffGpLeftY = 20;
        private const int OffGpRightX = 24;
        private const int OffGpRightY = 28;
        private const int OffGpLeftTrigger = 32;
        private const int OffGpRightTrigger = 36;
        private const int OffGpActivitySeq = 40;
        private const int OffKbConnected = 44;
        private const int OffKbKeyCount = 48;
        private const int OffKbKeys = 52;       // 32 * uint16 (64 bytes -> ends 116)
        private const int OffTextCharCount = 116;
        private const int OffTextChars = 120;   // 32 * uint16 (64 bytes -> ends 184)
        private const int OffKbActivitySeq = 184;
        private const int OffMouseConnected = 188;
        private const int OffMouseNormX = 192;
        private const int OffMouseNormY = 196;
        private const int OffMouseButtonsMask = 200;
        private const int OffMouseScrollDelta = 204;
        private const int OffMouseActivitySeq = 208;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int PollInputDelegate(IntPtr outBuf);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetMouseNormDelegate(float nx, float ny);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetDirectTouchCountDelegate();

        [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "dlsym")]
        private static extern IntPtr DarwinDlsym(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string symbol);

        private static readonly IntPtr RTLD_DEFAULT = new IntPtr(-2);

        private static readonly MethodInfo KeyboardSetKeysMethod =
            typeof(Keyboard).GetMethod("SetKeys", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(List<Keys>) }, null);

        private static readonly MethodInfo GamePadPlatformGetStateMethod =
            typeof(GamePad).GetMethod("PlatformGetState",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(int), typeof(GamePadDeadZone), typeof(GamePadDeadZone) },
                null);

        private static readonly object[] GamePadPlatformArgs =
            new object[] { 0, GamePadDeadZone.IndependentAxes, GamePadDeadZone.IndependentAxes };

        private static readonly FieldInfo InputCurrentMouseField =
            typeof(InputState).GetField("_currentMouseState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo InputCurrentGamepadField =
            typeof(InputState).GetField("_currentGamepadState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo MousePrimaryWindowField =
            typeof(Mouse).GetField("PrimaryWindow", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        private static PropertyInfo _windowMouseStateProp;
        private static FieldInfo _windowMouseStateField;
        private static bool _windowMouseMemberResolved;

        private readonly IMonitor _monitor;
        private readonly IntPtr _snapBuf;
        private PollInputDelegate _nativePoll;
        private SetMouseNormDelegate _nativeSetMouseNorm;
        private GetDirectTouchCountDelegate _nativeDirectTouches;
        private bool _nativeResolved;
        private bool _nativeAvailable;
        private bool _abiMismatchLogged;

        private int _lastPollTick = -1;
        private long _lastPollMs;

        private uint _lastGpSeq;
        private uint _lastKbSeq;
        private uint _lastMouseSeq;
        private bool _seqInitialized;

        private bool _nativeSuppressed;
        private bool _managedSuppressed;
        private bool _wasSuppressed;
        private bool _gpConnected;
        private bool _kbConnected;
        private bool _mouseConnected;
        private bool _mouseEverUsed;
        private int _scrollWheelValue;
        private int _gamepadCursorX = 640;
        private int _gamepadCursorY = 360;
        private float _gamepadCursorXf = 640f;
        private float _gamepadCursorYf = 360f;
        private bool _gamepadCursorInit;
        private long _lastRightStickStepMs;
        public bool IsRightStickSteeringGameplayCursor { get; private set; }

        private static readonly List<Keys> EmptyKeysList = new List<Keys>(0);
        private readonly List<Keys> _physicalKeys = new List<Keys>(32);
        private readonly HashSet<Keys> _prevSpecialKeys = new HashSet<Keys>();
        private readonly HashSet<Keys> _currSpecialKeys = new HashSet<Keys>();
        private readonly List<Keys> _mergeScratch = new List<Keys>(48);
        private bool _syncedEmptyMonoGameKeys = true;

        public ExternalInputMode ActiveMode { get; private set; } = ExternalInputMode.Touch;
        public GamePadState CurrentGamePadState { get; private set; }
        public MouseState CurrentMouseState { get; private set; }
        public bool IsSuppressed => _nativeSuppressed || _managedSuppressed;
        public bool IsGamepadConnected => _gpConnected;
        public bool IsKeyboardConnected => _kbConnected;
        public bool IsMouseConnected => _mouseConnected;
        public bool IsPhysicalGamepadActive => ActiveMode == ExternalInputMode.Gamepad && _gpConnected && !IsSuppressed;
        public bool IsExternalMouseActive => ActiveMode == ExternalInputMode.KeyboardMouse && _mouseConnected && _mouseEverUsed && !IsSuppressed;
        public bool IsExternalDeviceActive => ActiveMode != ExternalInputMode.Touch && !IsSuppressed;
        public bool ShouldOverrideMouse => IsExternalMouseActive || IsPhysicalGamepadActive;
        public bool HasPhysicalKeysPressed => !IsSuppressed && _physicalKeys.Count > 0;
        public IReadOnlyList<Keys> PhysicalKeys => _physicalKeys;

        public event Action<ExternalInputMode> ModeChanged;

        public ExternalInputBridge(IMonitor monitor)
        {
            _monitor = monitor;
            _snapBuf = Marshal.AllocHGlobal(256);
            ResolveNativeExports();
        }

        private void ResolveNativeExports()
        {
            if (_nativeResolved) return;
            _nativeResolved = true;
            try
            {
                IntPtr pPoll = IntPtr.Zero;
                IntPtr pSetMouse = IntPtr.Zero;
                IntPtr pDirectTouch = IntPtr.Zero;

                string[] libNames =
                {
                    "@rpath/SDVModGarden.framework/SDVModGarden",
                    "SDVModGarden.framework/SDVModGarden"
                };

                foreach (string lib in libNames)
                {
                    if (NativeLibrary.TryLoad(lib, out IntPtr h) && h != IntPtr.Zero)
                    {
                        NativeLibrary.TryGetExport(h, "sdv_ext_poll_input", out pPoll);
                        NativeLibrary.TryGetExport(h, "sdv_ext_set_mouse_norm", out pSetMouse);
                        NativeLibrary.TryGetExport(h, "sdv_ext_get_direct_touch_count", out pDirectTouch);
                        if (pPoll != IntPtr.Zero) break;
                    }
                }

                if (pPoll == IntPtr.Zero)
                {
                    try
                    {
                        pPoll = DarwinDlsym(RTLD_DEFAULT, "sdv_ext_poll_input");
                        pSetMouse = DarwinDlsym(RTLD_DEFAULT, "sdv_ext_set_mouse_norm");
                        pDirectTouch = DarwinDlsym(RTLD_DEFAULT, "sdv_ext_get_direct_touch_count");
                    }
                    catch { }
                }

                if (pPoll != IntPtr.Zero)
                {
                    _nativePoll = Marshal.GetDelegateForFunctionPointer<PollInputDelegate>(pPoll);
                    if (pSetMouse != IntPtr.Zero)
                        _nativeSetMouseNorm = Marshal.GetDelegateForFunctionPointer<SetMouseNormDelegate>(pSetMouse);
                    if (pDirectTouch != IntPtr.Zero)
                        _nativeDirectTouches = Marshal.GetDelegateForFunctionPointer<GetDirectTouchCountDelegate>(pDirectTouch);
                    _nativeAvailable = true;
                    _monitor?.Log("[ExternalInput] Bound native iOS GameController bridge (Gamepad + Keyboard + Mouse).", LogLevel.Info);
                }
                else
                {
                    _monitor?.Log("[ExternalInput] Native bridge export not found; falling back to MonoGame GamePad only.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[ExternalInput] Native bridge init fallback: {ex.Message}", LogLevel.Warn);
            }
        }

        private static float ReadFloat(IntPtr ptr, int offset)
        {
            return BitConverter.Int32BitsToSingle(Marshal.ReadInt32(ptr, offset));
        }

        private static void GetBackBufferSize(out int bw, out int bh)
        {
            bw = 1280;
            bh = 720;
            try
            {
                var gd = Game1.graphics?.GraphicsDevice;
                if (gd != null)
                {
                    var pp = gd.PresentationParameters;
                    if (pp != null && pp.BackBufferWidth > 100 && pp.BackBufferHeight > 100)
                    {
                        bw = pp.BackBufferWidth;
                        bh = pp.BackBufferHeight;
                        return;
                    }
                    if (gd.Viewport.Width > 100 && gd.Viewport.Height > 100)
                    {
                        bw = gd.Viewport.Width;
                        bh = gd.Viewport.Height;
                        return;
                    }
                }
            }
            catch { }
        }

        public void SetManagedSuppressed(bool suppressed)
        {
            if (_managedSuppressed == suppressed) return;
            bool prev = IsSuppressed;
            _managedSuppressed = suppressed;
            bool cur = IsSuppressed;
            _wasSuppressed = cur;
            if (cur != prev)
                OnSuppressionTransition(prev, cur);
        }

        private void OnSuppressionTransition(bool wasSuppressed, bool nowSuppressed)
        {
            CurrentGamePadState = default;
            _physicalKeys.Clear();
            _prevSpecialKeys.Clear();
            SyncMonoGameKeyboardKeys(forceClear: true);

            if (!nowSuppressed)
            {
                // Re-baseline native activity sequences so opening/closing Mod Garden or Edit Layout
                // never fires a stale edge on frame 1 after resume.
                _seqInitialized = false;
            }

            ApplyStardewGamepadMode();
            FlushFarmerMovementAndState(restoreAutoRun: !nowSuppressed);
        }

        /// <summary>
        /// Safely halts lingering Farmer movement/directions, clears stale Game1.oldPadState,
        /// and optionally resyncs Farmer.running with Game1.options.autoRun so mode/UI transitions
        /// never leave the farmer stuck running in place, sliding, or locked into walk animation.
        /// </summary>
        public void FlushFarmerMovementAndState(bool restoreAutoRun = true)
        {
            try { Game1.oldPadState = default; } catch { }
            try
            {
                Farmer who = Game1.player;
                if (who != null && !Game1.eventUp && who.controller == null &&
                    !who.UsingTool && !who.isEating && !who.IsSitting())
                {
                    if (who.movementDirections != null && who.movementDirections.Count > 0)
                        who.movementDirections.Clear();
                    who.Halt();
                    if (restoreAutoRun && Game1.options != null &&
                        !who.canOnlyWalk && !who.bathingClothes.Value &&
                        who.stamina > 0f && !who.isRidingHorse())
                    {
                        bool wantRun = Game1.options.autoRun;
                        if (who.running != wantRun)
                            who.setRunning(wantRun, force: false);
                    }
                }
            }
            catch { }
        }

        public KeyboardState BuildPhysicalKeyboardState()
        {
            if (!HasPhysicalKeysPressed)
                return default;
            return new KeyboardState(_physicalKeys.ToArray());
        }

        public void NotifyDirectTouch(int directTouchCount)
        {
            // Ignore touches while suppressed (Mod Garden / Edit Layout / Save Menu) so dragging UI
            // never flips ActiveMode back and forth behind the overlay.
            if (IsSuppressed) return;
            if (directTouchCount > 0 && ActiveMode != ExternalInputMode.Touch)
            {
                SetActiveMode(ExternalInputMode.Touch);
            }
        }

        private void SetActiveMode(ExternalInputMode next)
        {
            if (ActiveMode == next) return;
            ExternalInputMode prev = ActiveMode;
            ActiveMode = next;

            if (next == ExternalInputMode.Gamepad)
            {
                GetBackBufferSize(out int bw, out int bh);
                EnsureValidLocalMultiplayerWindow(bw, bh);
                if (!_gamepadCursorInit || (_gamepadCursorX == 0 && _gamepadCursorY == 0))
                {
                    _gamepadCursorX = bw / 2;
                    _gamepadCursorY = bh / 2;
                    _gamepadCursorXf = _gamepadCursorX;
                    _gamepadCursorYf = _gamepadCursorY;
                    _gamepadCursorInit = true;
                }
            }
            if (next != ExternalInputMode.Gamepad)
            {
                IsRightStickSteeringGameplayCursor = false;
                _lastRightStickStepMs = 0;
            }

            // Clear any stale touch-simulated WASD keys when switching into or out of Gamepad mode
            if (next == ExternalInputMode.Gamepad || prev == ExternalInputMode.Gamepad)
            {
                if (_physicalKeys.Count == 0)
                    SyncMonoGameKeyboardKeys(forceClear: true);
            }

            ApplyStardewGamepadMode();

            // Leaving Gamepad flips Game1.options.gamepadControls -> false, which causes Stardew
            // to skip its gamepad stick-release block. Flush movement cleanly so Farmer never
            // stays stuck with orphaned movementDirections / running-in-place animation!
            if (prev == ExternalInputMode.Gamepad)
            {
                FlushFarmerMovementAndState(restoreAutoRun: true);
            }
            else if (next == ExternalInputMode.Gamepad)
            {
                try { Game1.oldPadState = default; } catch { }
                try
                {
                    Farmer who = Game1.player;
                    if (who != null && Game1.options != null && !Game1.eventUp &&
                        who.controller == null && !who.UsingTool && !who.isEating &&
                        !who.IsSitting() && !who.canOnlyWalk && !who.bathingClothes.Value &&
                        who.stamina > 0f && !who.isRidingHorse())
                    {
                        bool wantRun = Game1.options.autoRun;
                        if (who.running != wantRun)
                            who.setRunning(wantRun, force: false);
                    }
                }
                catch { }
            }

            try { ModeChanged?.Invoke(next); } catch { }
        }

        public void ApplyStardewGamepadMode()
        {
            try
            {
                if (Game1.options == null) return;
                if (IsPhysicalGamepadActive)
                {
                    if (Game1.options.gamepadMode != Options.GamepadModes.ForceOn)
                        Game1.options.gamepadMode = Options.GamepadModes.ForceOn;
                    if (!Game1.options.gamepadControls)
                        Game1.options.gamepadControls = true;
                }
                else
                {
                    if (Game1.options.gamepadMode != Options.GamepadModes.ForceOff)
                        Game1.options.gamepadMode = Options.GamepadModes.ForceOff;
                    if (Game1.options.gamepadControls)
                        Game1.options.gamepadControls = false;
                }
            }
            catch { }
        }

        public void EnsureValidLocalMultiplayerWindow()
        {
            GetBackBufferSize(out int bw, out int bh);
            EnsureValidLocalMultiplayerWindow(bw, bh);
        }

        private static void EnsureValidLocalMultiplayerWindow(int bw, int bh)
        {
            try
            {
                var g = Game1.game1;
                if (g != null && bw > 100 && bh > 100 &&
                    (g.localMultiplayerWindow.Width <= 100 || g.localMultiplayerWindow.Height <= 100))
                {
                    g.localMultiplayerWindow = new Rectangle(0, 0, bw, bh);
                }
            }
            catch { }
        }

        public static GamePadState StripRightStick(GamePadState gs)
        {
            return new GamePadState(
                new GamePadThumbSticks(gs.ThumbSticks.Left, Vector2.Zero),
                gs.Triggers,
                gs.Buttons,
                gs.DPad);
        }

        private void StepGamepadRightStickCursor(float rx, float ry, int bw, int bh)
        {
            EnsureValidLocalMultiplayerWindow(bw, bh);
            if (!_gamepadCursorInit)
            {
                _gamepadCursorX = bw / 2;
                _gamepadCursorY = bh / 2;
                _gamepadCursorXf = _gamepadCursorX;
                _gamepadCursorYf = _gamepadCursorY;
                _gamepadCursorInit = true;
            }

            // During free gameplay (no modal menu / minigame / emote wheel), drive smooth analog
            // right-stick cursor movement directly in backbuffer coordinates so players can freely
            // glide the cursor over UI Info Suite 2 icons and world tiles without pressing anything else.
            bool canFreeCursor = Game1.activeClickableMenu == null && Game1.currentMinigame == null && Game1.emoteMenu == null;
            float magSq = rx * rx + ry * ry;
            if (canFreeCursor && magSq > 0.0001f)
            {
                long nowMs = Environment.TickCount64;
                float dt = (_lastRightStickStepMs > 0) ? (nowMs - _lastRightStickStepMs) / 1000f : (1f / 60f);
                if (dt > 0.06f) dt = 1f / 60f;
                dt = Math.Clamp(dt, 0.004f, 0.05f);
                _lastRightStickStepMs = nowMs;

                // If cursor faded away and somehow sat at (0,0) before user touched it, start smoothly near center
                if (Game1.timerUntilMouseFade <= 0 && _gamepadCursorX == 0 && _gamepadCursorY == 0)
                {
                    _gamepadCursorXf = bw * 0.5f;
                    _gamepadCursorYf = bh * 0.5f;
                }

                float mag = MathF.Sqrt(magSq);
                float clampedMag = Math.Min(1f, mag);
                float dx = rx / mag;
                float dy = ry / mag;
                // Hybrid linear + quadratic response: fine precision for small HUD icons, brisk travel at full tilt
                float curved = 0.35f * clampedMag + 0.65f * clampedMag * clampedMag;
                float speedPxPerSec = Math.Max(680f, bh * 1.15f);

                _gamepadCursorXf = Math.Clamp(_gamepadCursorXf + dx * curved * speedPxPerSec * dt, 0f, Math.Max(0, bw - 1));
                _gamepadCursorYf = Math.Clamp(_gamepadCursorYf - dy * curved * speedPxPerSec * dt, 0f, Math.Max(0, bh - 1));
                _gamepadCursorX = (int)Math.Round(_gamepadCursorXf);
                _gamepadCursorY = (int)Math.Round(_gamepadCursorYf);
                IsRightStickSteeringGameplayCursor = true;

                try
                {
                    Game1.timerUntilMouseFade = 4000;
                    Game1.mouseCursorTransparency = 1f;
                    Game1.wasMouseVisibleThisFrame = true;
                    Game1.lastCursorMotionWasMouse = false;
                    Game1.lastMousePositionBeforeFade = new Point(_gamepadCursorX, _gamepadCursorY);
                    Game1.input?.SetMousePosition(_gamepadCursorX, _gamepadCursorY);
                }
                catch { }
            }
            else
            {
                IsRightStickSteeringGameplayCursor = false;
                _lastRightStickStepMs = 0;
                _gamepadCursorX = Math.Clamp(_gamepadCursorX, 0, Math.Max(0, bw - 1));
                _gamepadCursorY = Math.Clamp(_gamepadCursorY, 0, Math.Max(0, bh - 1));
                _gamepadCursorXf = _gamepadCursorX;
                _gamepadCursorYf = _gamepadCursorY;
            }

            CurrentMouseState = new MouseState(
                _gamepadCursorX, _gamepadCursorY, _scrollWheelValue,
                ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        }

        public void OnGameSetMousePositionRaw(int x, int y)
        {
            GetBackBufferSize(out int bw, out int bh);
            int cx = Math.Clamp(x, 0, Math.Max(0, bw - 1));
            int cy = Math.Clamp(y, 0, Math.Max(0, bh - 1));
            _gamepadCursorX = cx;
            _gamepadCursorY = cy;
            _gamepadCursorXf = cx;
            _gamepadCursorYf = cy;
            _gamepadCursorInit = true;

            if (IsExternalMouseActive && _nativeSetMouseNorm != null && bw > 1 && bh > 1)
            {
                try { _nativeSetMouseNorm((float)cx / bw, (float)cy / bh); } catch { }
            }

            if (ShouldOverrideMouse)
            {
                var cur = CurrentMouseState;
                CurrentMouseState = new MouseState(cx, cy, cur.ScrollWheelValue, cur.LeftButton, cur.MiddleButton, cur.RightButton, cur.XButton1, cur.XButton2);
            }
        }

        public void PollFrame(int directTouchCount = -1)
        {
            if (directTouchCount < 0)
            {
                directTouchCount = 0;
                if (_nativeDirectTouches != null)
                {
                    try { directTouchCount = _nativeDirectTouches(); } catch { }
                }
            }
            int tick = Game1.ticks;
            long nowMs = Environment.TickCount64;
            if (tick == _lastPollTick && nowMs - _lastPollMs < 6)
            {
                if (directTouchCount > 0) NotifyDirectTouch(directTouchCount);
                // Always restore Game1.options.gamepadControls even on fast frames because
                // SDViOSTouchControls.ModEntry.OnUpdateTicking forces it false every tick!
                ApplyStardewGamepadMode();
                return;
            }
            _lastPollTick = tick;
            _lastPollMs = nowMs;

            if (directTouchCount > 0)
            {
                NotifyDirectTouch(directTouchCount);
            }

            if (!_nativeAvailable || _nativePoll == null)
            {
                PollFallbackMonoGameGamepad(directTouchCount);
                return;
            }

            int ok = 0;
            try { ok = _nativePoll(_snapBuf); } catch { ok = 0; }
            if (ok == 0)
                return;

            int sz = Marshal.ReadInt32(_snapBuf, OffStructSize);
            if (sz != ExpectedStructSize)
            {
                if (!_abiMismatchLogged)
                {
                    _abiMismatchLogged = true;
                    _monitor?.Log($"[ExternalInput] Struct size mismatch: native={sz}, managed={ExpectedStructSize}", LogLevel.Warn);
                }
                return;
            }

            bool prevSup = _wasSuppressed;
            _nativeSuppressed = Marshal.ReadInt32(_snapBuf, OffSuppress) != 0;
            bool curSup = IsSuppressed;
            _wasSuppressed = curSup;
            if (curSup != prevSup)
            {
                OnSuppressionTransition(prevSup, curSup);
            }

            _gpConnected = Marshal.ReadInt32(_snapBuf, OffGpConnected) != 0;
            _kbConnected = Marshal.ReadInt32(_snapBuf, OffKbConnected) != 0;
            _mouseConnected = Marshal.ReadInt32(_snapBuf, OffMouseConnected) != 0;

            uint gpSeq = unchecked((uint)Marshal.ReadInt32(_snapBuf, OffGpActivitySeq));
            uint kbSeq = unchecked((uint)Marshal.ReadInt32(_snapBuf, OffKbActivitySeq));
            uint mouseSeq = unchecked((uint)Marshal.ReadInt32(_snapBuf, OffMouseActivitySeq));

            bool gpActiveEdge = _seqInitialized && gpSeq != _lastGpSeq;
            bool kbActiveEdge = _seqInitialized && kbSeq != _lastKbSeq;
            bool mouseActiveEdge = _seqInitialized && mouseSeq != _lastMouseSeq;
            _lastGpSeq = gpSeq;
            _lastKbSeq = kbSeq;
            _lastMouseSeq = mouseSeq;
            _seqInitialized = true;

            // --- 1. Physical Gamepad State ---
            if (_gpConnected && !curSup)
            {
                uint mask = unchecked((uint)Marshal.ReadInt32(_snapBuf, OffGpButtonsMask));
                float lx = ApplyAxisDeadzone(ReadFloat(_snapBuf, OffGpLeftX), 0.14f);
                float ly = ApplyAxisDeadzone(ReadFloat(_snapBuf, OffGpLeftY), 0.14f);
                float rx = ApplyAxisDeadzone(ReadFloat(_snapBuf, OffGpRightX), 0.14f);
                float ry = ApplyAxisDeadzone(ReadFloat(_snapBuf, OffGpRightY), 0.14f);
                float lt = Math.Clamp(ReadFloat(_snapBuf, OffGpLeftTrigger), 0f, 1f);
                float rt = Math.Clamp(ReadFloat(_snapBuf, OffGpRightTrigger), 0f, 1f);

                CurrentGamePadState = BuildGamePadState(mask, lx, ly, rx, ry, lt, rt);

                if (directTouchCount == 0 && (gpActiveEdge || mask != 0 || lx * lx + ly * ly > 0.04f || rx * rx + ry * ry > 0.04f || lt > 0.2f || rt > 0.2f))
                {
                    SetActiveMode(ExternalInputMode.Gamepad);
                }
            }
            else
            {
                CurrentGamePadState = default;
                if (ActiveMode == ExternalInputMode.Gamepad && !_gpConnected)
                    SetActiveMode(ExternalInputMode.Touch);
            }

            // --- 2. Physical Keyboard State + Native TextBox Input ---
            _physicalKeys.Clear();
            if (_kbConnected && !curSup)
            {
                int kCnt = Math.Clamp(Marshal.ReadInt32(_snapBuf, OffKbKeyCount), 0, 32);
                for (int i = 0; i < kCnt; i++)
                {
                    ushort kCode = unchecked((ushort)Marshal.ReadInt16(_snapBuf, OffKbKeys + i * 2));
                    if (kCode > 0 && kCode <= 255)
                        _physicalKeys.Add((Keys)kCode);
                }

                if (directTouchCount == 0 && (kbActiveEdge || _physicalKeys.Count > 0))
                {
                    SetActiveMode(ExternalInputMode.KeyboardMouse);
                }

                DispatchNativeKeyboardTextInput();
            }
            else
            {
                _prevSpecialKeys.Clear();
            }

            SyncMonoGameKeyboardKeys();

            // --- 3. Physical Mouse / Trackpad State ---
            GetBackBufferSize(out int bw, out int bh);
            int scrollDelta = Marshal.ReadInt32(_snapBuf, OffMouseScrollDelta);
            if (!curSup && scrollDelta != 0)
            {
                _scrollWheelValue += scrollDelta;
            }

            if (_mouseConnected && !curSup)
            {
                float nx = Math.Clamp(ReadFloat(_snapBuf, OffMouseNormX), 0f, 1f);
                float ny = Math.Clamp(ReadFloat(_snapBuf, OffMouseNormY), 0f, 1f);
                uint mMask = unchecked((uint)Marshal.ReadInt32(_snapBuf, OffMouseButtonsMask));

                if (directTouchCount == 0 && (mouseActiveEdge || mMask != 0 || scrollDelta != 0))
                {
                    _mouseEverUsed = true;
                    SetActiveMode(ExternalInputMode.KeyboardMouse);
                }

                if (IsExternalMouseActive)
                {
                    int mx = Math.Clamp((int)Math.Round(nx * bw), 0, Math.Max(0, bw - 1));
                    int my = Math.Clamp((int)Math.Round(ny * bh), 0, Math.Max(0, bh - 1));
                    CurrentMouseState = new MouseState(
                        mx, my, _scrollWheelValue,
                        (mMask & (1u << 0)) != 0 ? ButtonState.Pressed : ButtonState.Released,
                        (mMask & (1u << 2)) != 0 ? ButtonState.Pressed : ButtonState.Released,
                        (mMask & (1u << 1)) != 0 ? ButtonState.Pressed : ButtonState.Released,
                        (mMask & (1u << 3)) != 0 ? ButtonState.Pressed : ButtonState.Released,
                        (mMask & (1u << 4)) != 0 ? ButtonState.Pressed : ButtonState.Released);
                }
            }
            else if (ActiveMode == ExternalInputMode.KeyboardMouse && !_kbConnected && !_mouseConnected)
            {
                SetActiveMode(ExternalInputMode.Touch);
            }

            if (IsPhysicalGamepadActive)
            {
                Vector2 rStick = CurrentGamePadState.ThumbSticks.Right;
                StepGamepadRightStickCursor(rStick.X, rStick.Y, bw, bh);
            }
            else
            {
                IsRightStickSteeringGameplayCursor = false;
                _lastRightStickStepMs = 0;
            }

            ApplyStardewGamepadMode();
            SyncDirectZeroHarmonyInputStates();
        }

        /// <summary>
        /// Zero-Harmony direct state push into Game1.input and MonoGame Mouse.PrimaryWindow so physical
        /// Xbox/PS/MFi controllers, R-stick cursor, and external mice work 100% even on 0% JIT where
        /// 0Harmony.dll is a no-op stub.
        /// </summary>
        public void SyncDirectZeroHarmonyInputStates()
        {
            try
            {
                if (ShouldOverrideMouse)
                {
                    MouseState ms = CurrentMouseState;
                    if (Game1.input != null && InputCurrentMouseField != null)
                        InputCurrentMouseField.SetValue(Game1.input, ms);
                    if (MousePrimaryWindowField != null)
                    {
                        object win = MousePrimaryWindowField.GetValue(null);
                        if (win != null)
                        {
                            if (!_windowMouseMemberResolved)
                            {
                                _windowMouseMemberResolved = true;
                                Type wt = win.GetType();
                                const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                                _windowMouseStateField = wt.GetField("MouseState", any);
                                _windowMouseStateProp = wt.GetProperty("MouseState", any);
                            }
                            if (_windowMouseStateField != null)
                                _windowMouseStateField.SetValue(win, ms);
                            else if (_windowMouseStateProp != null && _windowMouseStateProp.CanWrite)
                                _windowMouseStateProp.SetValue(win, ms);
                        }
                    }
                }
                if (IsPhysicalGamepadActive && Game1.input != null && InputCurrentGamepadField != null)
                {
                    GamePadState gps = IsRightStickSteeringGameplayCursor
                        ? StripRightStick(CurrentGamePadState)
                        : CurrentGamePadState;
                    InputCurrentGamepadField.SetValue(Game1.input, gps);
                }
            }
            catch { }
        }

        private void PollFallbackMonoGameGamepad(int directTouchCount)
        {
            if (IsSuppressed)
            {
                CurrentGamePadState = default;
                ApplyStardewGamepadMode();
                return;
            }
            if (GamePadPlatformGetStateMethod == null) return;
            try
            {
                object res = GamePadPlatformGetStateMethod.Invoke(null, GamePadPlatformArgs);
                if (res is GamePadState gs && gs.IsConnected)
                {
                    _gpConnected = true;
                    CurrentGamePadState = gs;
                    bool used =
                        gs.ThumbSticks.Left.LengthSquared() > 0.06f ||
                        gs.ThumbSticks.Right.LengthSquared() > 0.06f ||
                        gs.Triggers.Left > 0.2f || gs.Triggers.Right > 0.2f ||
                        gs.Buttons.A == ButtonState.Pressed || gs.Buttons.B == ButtonState.Pressed ||
                        gs.Buttons.X == ButtonState.Pressed || gs.Buttons.Y == ButtonState.Pressed ||
                        gs.Buttons.LeftShoulder == ButtonState.Pressed || gs.Buttons.RightShoulder == ButtonState.Pressed ||
                        gs.Buttons.Start == ButtonState.Pressed || gs.Buttons.Back == ButtonState.Pressed ||
                        gs.DPad.Up == ButtonState.Pressed || gs.DPad.Down == ButtonState.Pressed ||
                        gs.DPad.Left == ButtonState.Pressed || gs.DPad.Right == ButtonState.Pressed;
                    if (directTouchCount == 0 && used)
                        SetActiveMode(ExternalInputMode.Gamepad);
                    if (IsPhysicalGamepadActive)
                    {
                        GetBackBufferSize(out int fbw, out int fbh);
                        float frx = ApplyAxisDeadzone(gs.ThumbSticks.Right.X, 0.14f);
                        float fry = ApplyAxisDeadzone(gs.ThumbSticks.Right.Y, 0.14f);
                        StepGamepadRightStickCursor(frx, fry, fbw, fbh);
                    }
                    ApplyStardewGamepadMode();
                    SyncDirectZeroHarmonyInputStates();
                    return;
                }
            }
            catch { }

            _gpConnected = false;
            CurrentGamePadState = default;
            if (ActiveMode == ExternalInputMode.Gamepad)
                SetActiveMode(ExternalInputMode.Touch);
        }

        private static float ApplyAxisDeadzone(float v, float deadzone)
        {
            float av = Math.Abs(v);
            if (av <= deadzone) return 0f;
            float scaled = (av - deadzone) / (1f - deadzone);
            return Math.Sign(v) * Math.Clamp(scaled, 0f, 1f);
        }

        private static GamePadState BuildGamePadState(uint mask, float lx, float ly, float rx, float ry, float lt, float rt)
        {
            Buttons b = 0;
            if ((mask & (1u << 0)) != 0) b |= Buttons.A;
            if ((mask & (1u << 1)) != 0) b |= Buttons.B;
            if ((mask & (1u << 2)) != 0) b |= Buttons.X;
            if ((mask & (1u << 3)) != 0) b |= Buttons.Y;
            if ((mask & (1u << 4)) != 0) b |= Buttons.LeftShoulder;
            if ((mask & (1u << 5)) != 0) b |= Buttons.RightShoulder;
            if ((mask & (1u << 6)) != 0) b |= Buttons.Start;
            if ((mask & (1u << 7)) != 0) b |= Buttons.Back;
            if ((mask & (1u << 8)) != 0) b |= Buttons.BigButton;
            if ((mask & (1u << 9)) != 0) b |= Buttons.LeftStick;
            if ((mask & (1u << 10)) != 0) b |= Buttons.RightStick;

            ButtonState dUp = (mask & (1u << 11)) != 0 ? ButtonState.Pressed : ButtonState.Released;
            ButtonState dDown = (mask & (1u << 12)) != 0 ? ButtonState.Pressed : ButtonState.Released;
            ButtonState dLeft = (mask & (1u << 13)) != 0 ? ButtonState.Pressed : ButtonState.Released;
            ButtonState dRight = (mask & (1u << 14)) != 0 ? ButtonState.Pressed : ButtonState.Released;

            if (dUp == ButtonState.Pressed) b |= Buttons.DPadUp;
            if (dDown == ButtonState.Pressed) b |= Buttons.DPadDown;
            if (dLeft == ButtonState.Pressed) b |= Buttons.DPadLeft;
            if (dRight == ButtonState.Pressed) b |= Buttons.DPadRight;

            if (lt > 0.35f) b |= Buttons.LeftTrigger;
            if (rt > 0.35f) b |= Buttons.RightTrigger;

            if (ly > 0.5f) b |= Buttons.LeftThumbstickUp;
            else if (ly < -0.5f) b |= Buttons.LeftThumbstickDown;
            if (lx < -0.5f) b |= Buttons.LeftThumbstickLeft;
            else if (lx > 0.5f) b |= Buttons.LeftThumbstickRight;

            if (ry > 0.5f) b |= Buttons.RightThumbstickUp;
            else if (ry < -0.5f) b |= Buttons.RightThumbstickDown;
            if (rx < -0.5f) b |= Buttons.RightThumbstickLeft;
            else if (rx > 0.5f) b |= Buttons.RightThumbstickRight;

            return new GamePadState(
                new GamePadThumbSticks(new Vector2(lx, ly), new Vector2(rx, ry)),
                new GamePadTriggers(lt, rt),
                new GamePadButtons(b),
                new GamePadDPad(dUp, dDown, dLeft, dRight));
        }

        private void SyncMonoGameKeyboardKeys(bool forceClear = false)
        {
            if (KeyboardSetKeysMethod == null) return;
            try
            {
                if (!forceClear && _physicalKeys.Count > 0)
                {
                    KeyboardSetKeysMethod.Invoke(null, new object[] { _physicalKeys });
                    _syncedEmptyMonoGameKeys = false;
                }
                else if (forceClear || !_syncedEmptyMonoGameKeys)
                {
                    KeyboardSetKeysMethod.Invoke(null, new object[] { EmptyKeysList });
                    _syncedEmptyMonoGameKeys = true;
                }
            }
            catch { }
        }

        private void DispatchNativeKeyboardTextInput()
        {
            try
            {
                IKeyboardSubscriber sub = Game1.keyboardDispatcher?.Subscriber;
                int tCnt = Math.Clamp(Marshal.ReadInt32(_snapBuf, OffTextCharCount), 0, 32);
                if (sub != null && sub.Selected)
                {
                    for (int i = 0; i < tCnt; i++)
                    {
                        char ch = (char)unchecked((ushort)Marshal.ReadInt16(_snapBuf, OffTextChars + i * 2));
                        if (ch == '\b' || ch == '\r' || ch == '\t')
                            sub.RecieveCommandInput(ch);
                        else if (ch >= ' ')
                            sub.RecieveTextInput(ch);
                    }

                    // Edge-triggered special navigation keys (arrows, Delete, Home, End)
                    _currSpecialKeys.Clear();
                    for (int i = 0; i < _physicalKeys.Count; i++)
                    {
                        Keys k = _physicalKeys[i];
                        if (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down ||
                            k == Keys.Delete || k == Keys.Home || k == Keys.End)
                        {
                            _currSpecialKeys.Add(k);
                            if (!_prevSpecialKeys.Contains(k))
                                sub.RecieveSpecialInput(k);
                        }
                    }
                    _prevSpecialKeys.Clear();
                    foreach (Keys k in _currSpecialKeys)
                        _prevSpecialKeys.Add(k);
                }
                else
                {
                    _prevSpecialKeys.Clear();
                }
            }
            catch { }
        }

        public void PopulateExtraKeys(HashSet<Keys> targetSet)
        {
            if (targetSet == null || !HasPhysicalKeysPressed) return;
            for (int i = 0; i < _physicalKeys.Count; i++)
                targetSet.Add(_physicalKeys[i]);
        }

        public void PopulateExtraKeysList(List<Keys> targetList)
        {
            if (targetList == null || !HasPhysicalKeysPressed) return;
            for (int i = 0; i < _physicalKeys.Count; i++)
            {
                Keys k = _physicalKeys[i];
                if (!targetList.Contains(k))
                    targetList.Add(k);
            }
        }

        public KeyboardState MergeKeyboardState(KeyboardState existing)
        {
            if (!HasPhysicalKeysPressed)
                return existing;

            _mergeScratch.Clear();
            Keys[] cur = existing.GetPressedKeys();
            if (cur != null)
            {
                for (int i = 0; i < cur.Length; i++)
                    _mergeScratch.Add(cur[i]);
            }
            for (int i = 0; i < _physicalKeys.Count; i++)
            {
                Keys k = _physicalKeys[i];
                if (!_mergeScratch.Contains(k))
                    _mergeScratch.Add(k);
            }
            return new KeyboardState(_mergeScratch.ToArray());
        }
    }
}
