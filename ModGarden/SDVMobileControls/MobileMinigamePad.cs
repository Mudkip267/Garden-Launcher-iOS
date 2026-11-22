using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using StardewValley;
using StardewValley.Menus;

namespace SDVMobileControls
{
    /// <summary>
    /// Auto-appearing touch arcade pad for keyboard & mouse-hybrid PC minigames:
    /// - Journey of the Prairie King (AbigailGame): Left Joystick (WASD) + Right 4-way Shoot Diamond (Arrows) + ITEM (Space) + PAUSE + ESC
    /// - Junimo Kart (MineCart): Big JUMP (Space + Mouse Left) + PAUSE (P) + ESC
    /// - Crane Game (CraneGame): Left Joystick (WASD) + Big GRAB (C + X + Mouse Left — fixed!) + ESC
    /// - Target Game (Stardew Fair Slingshot): Left Joystick (A/D) + Direct Drag-to-Aim Mouse Left + Pixel Crosshair + ESC
    /// - Fishing Game (Stardew Fair Fishing Tent): Left Joystick (WASD) + Big CAST/HOOK (C + Mouse Left) + ESC (yields to BobberBar)
    /// - Darts (Ginger Island Pirate Cove): Direct Hold & Drag Aim on Dartboard (Mouse Left) + ESC
    /// </summary>
    internal sealed class MobileMinigamePad
    {
        public enum ArcadeKind
        {
            None = 0,
            PrairieKing = 1,
            JunimoKart = 2,
            CraneGame = 3,
            TargetGame = 4,
            FishingGame = 5,
            Darts = 6
        }

        private ArcadeKind _kind = ArcadeKind.None;
        private readonly HashSet<Keys> _activeKeys = new HashSet<Keys>();
        private int _escPulseFrames;
        private int _pausePulseFrames;
        private bool _mouseLeftDown;
        private Point? _mousePos;

        // Left joystick (WASD)
        private int _joyTouchId = -1;
        private Vector2 _joyCenter;
        private Vector2 _joyKnob;
        private float _joyRadius = 90f;

        // Aiming touch for TargetGame & Darts
        private int _aimTouchId = -1;
        private Vector2 _aimVp;

        // Right controls
        private Rectangle _shootUp, _shootDn, _shootLt, _shootRt;
        private Rectangle _bigActionRect;
        private Rectangle _spcBtnRect;
        private Rectangle _escBtnRect;
        private Rectangle _pauseBtnRect;

        public bool MouseLeftDown => _mouseLeftDown;
        public Point? MousePos => _mousePos;

        public static ArcadeKind DetectCurrentArcadeMinigame()
        {
            var mg = Game1.currentMinigame;
            if (mg == null) return ArcadeKind.None;
            // Inside Fair FishingGame, if BobberBar menu is active, yield completely to BobberBar touch!
            if (Game1.activeClickableMenu is BobberBar) return ArcadeKind.None;

            string name = mg.GetType().Name;
            if (name.IndexOf("AbigailGame", StringComparison.OrdinalIgnoreCase) >= 0)
                return ArcadeKind.PrairieKing;
            if (name.IndexOf("MineCart", StringComparison.OrdinalIgnoreCase) >= 0)
                return ArcadeKind.JunimoKart;
            if (name.IndexOf("CraneGame", StringComparison.OrdinalIgnoreCase) >= 0)
                return ArcadeKind.CraneGame;
            if (name.IndexOf("TargetGame", StringComparison.OrdinalIgnoreCase) >= 0)
                return ArcadeKind.TargetGame;
            if (name.IndexOf("FishingGame", StringComparison.OrdinalIgnoreCase) >= 0)
                return ArcadeKind.FishingGame;
            if (name.IndexOf("Darts", StringComparison.OrdinalIgnoreCase) >= 0)
                return ArcadeKind.Darts;
            return ArcadeKind.None;
        }

        public void Reset()
        {
            _kind = ArcadeKind.None;
            _activeKeys.Clear();
            _escPulseFrames = 0;
            _pausePulseFrames = 0;
            _mouseLeftDown = false;
            _mousePos = null;
            _joyTouchId = -1;
            _aimTouchId = -1;
        }

        public void PopulateKeys(HashSet<Keys> targetSet)
        {
            if (targetSet == null || _kind == ArcadeKind.None) return;
            foreach (Keys k in _activeKeys)
                targetSet.Add(k);
            if (_escPulseFrames > 0)
                targetSet.Add(Keys.Escape);
            if (_pausePulseFrames > 0)
                targetSet.Add(Keys.P);
        }

        public void PopulateKeysList(List<Keys> targetList)
        {
            if (targetList == null || _kind == ArcadeKind.None) return;
            foreach (Keys k in _activeKeys)
            {
                if (!targetList.Contains(k))
                    targetList.Add(k);
            }
            if (_escPulseFrames > 0 && !targetList.Contains(Keys.Escape))
                targetList.Add(Keys.Escape);
            if (_pausePulseFrames > 0 && !targetList.Contains(Keys.P))
                targetList.Add(Keys.P);
        }

        public Vector2 Update(ArcadeKind kind, TouchCollection touches, Func<Vector2, Vector2> rawToVp, int vpW, int vpH)
        {
            _kind = kind;
            ComputeLayout(vpW, vpH);
            _activeKeys.Clear();
            _mouseLeftDown = false;
            _mousePos = null;
            if (_escPulseFrames > 0) _escPulseFrames--;
            if (_pausePulseFrames > 0) _pausePulseFrames--;

            bool sawJoy = false;
            bool sawAim = false;
            Vector2 stick = Vector2.Zero;
            bool hasPauseBtn = (_kind == ArcadeKind.JunimoKart || _kind == ArcadeKind.PrairieKing);

            for (int i = 0; i < touches.Count; i++)
            {
                TouchLocation tl = touches[i];
                bool active = tl.State == TouchLocationState.Pressed || tl.State == TouchLocationState.Moved;
                Vector2 vp = rawToVp(tl.Position);
                Point pt = new Point((int)vp.X, (int)vp.Y);

                if (tl.State == TouchLocationState.Pressed && _escBtnRect.Contains(pt))
                {
                    _escPulseFrames = 2;
                    continue;
                }
                if (hasPauseBtn && tl.State == TouchLocationState.Pressed && _pauseBtnRect.Contains(pt))
                {
                    _pausePulseFrames = 2;
                    continue;
                }

                if (!active)
                {
                    if ((_kind == ArcadeKind.Darts || _kind == ArcadeKind.TargetGame) && tl.Id == _aimTouchId)
                    {
                        // Preserve exact release coordinates so releaseLeftClick shoots/throws accurately!
                        _aimVp = vp;
                        _mousePos = pt;
                        _aimTouchId = -1;
                    }
                    continue;
                }

                if (_kind == ArcadeKind.JunimoKart)
                {
                    // Tapping anywhere except top-left ESC / top-right PAUSE jumps cleanly!
                    if (!_escBtnRect.Contains(pt) && !_pauseBtnRect.Contains(pt))
                    {
                        _activeKeys.Add(Keys.Space);
                        _mouseLeftDown = true;
                        _mousePos = new Point(vpW / 2, vpH / 2);
                    }
                    continue;
                }

                if (_kind == ArcadeKind.Darts)
                {
                    // Direct hold & drag anywhere on dartboard to aim, release to throw!
                    if (tl.Id == _aimTouchId || _aimTouchId < 0)
                    {
                        _aimTouchId = tl.Id;
                        sawAim = true;
                        _aimVp = vp;
                        _mousePos = pt;
                        _mouseLeftDown = true;
                    }
                    continue;
                }

                // Left virtual joystick for PrairieKing, CraneGame, TargetGame, FishingGame
                bool isLeftZone = (_kind == ArcadeKind.TargetGame)
                    ? (Vector2.Distance(vp, _joyCenter) <= _joyRadius * 1.45f)
                    : (vp.X < vpW * 0.45f);

                if (tl.Id == _joyTouchId || (_joyTouchId < 0 && tl.Id != _aimTouchId && isLeftZone))
                {
                    _joyTouchId = tl.Id;
                    sawJoy = true;
                    Vector2 delta = vp - _joyCenter;
                    float dist = delta.Length();
                    if (dist > _joyRadius)
                        delta = (delta / dist) * _joyRadius;
                    _joyKnob = _joyCenter + delta;
                    stick = dist > _joyRadius * 0.16f ? (delta / _joyRadius) : Vector2.Zero;

                    if (stick.Y < -0.32f) _activeKeys.Add(Keys.W);
                    if (stick.Y > 0.32f) _activeKeys.Add(Keys.S);
                    if (stick.X < -0.32f) _activeKeys.Add(Keys.A);
                    if (stick.X > 0.32f) _activeKeys.Add(Keys.D);
                    continue;
                }

                // Right-side / World controls
                if (_kind == ArcadeKind.PrairieKing)
                {
                    if (_spcBtnRect.Contains(pt))
                    {
                        _activeKeys.Add(Keys.Space);
                        continue;
                    }
                    Vector2 dCenter = new Vector2((_shootLt.X + _shootRt.Right) * 0.5f, (_shootUp.Y + _shootDn.Bottom) * 0.5f);
                    float dRad = (_shootRt.Right - _shootLt.X) * 0.65f;
                    Vector2 sd = vp - dCenter;
                    if (sd.Length() <= dRad && sd.Length() >= 14f)
                    {
                        Vector2 dir = Vector2.Normalize(sd);
                        if (dir.Y < -0.36f) _activeKeys.Add(Keys.Up);
                        if (dir.Y > 0.36f) _activeKeys.Add(Keys.Down);
                        if (dir.X < -0.36f) _activeKeys.Add(Keys.Left);
                        if (dir.X > 0.36f) _activeKeys.Add(Keys.Right);
                    }
                }
                else if (_kind == ArcadeKind.CraneGame)
                {
                    // FIXED: CraneGame._UpdateInput() reads Options.useToolButton (C), Options.actionButton (X), and Mouse LeftButton — NOT Keys.Space!
                    if (_bigActionRect.Contains(pt))
                    {
                        _activeKeys.Add(Keys.C);
                        _activeKeys.Add(Keys.X);
                        _mouseLeftDown = true;
                        _mousePos = new Point(vpW / 2, vpH / 2);
                    }
                }
                else if (_kind == ArcadeKind.FishingGame)
                {
                    if (_bigActionRect.Contains(pt) || vp.X >= vpW * 0.55f)
                    {
                        _activeKeys.Add(Keys.C);
                        _mouseLeftDown = true;
                        _mousePos = pt;
                    }
                }
                else if (_kind == ArcadeKind.TargetGame)
                {
                    // Direct drag-to-aim slingshot at Fair shooting gallery!
                    if (tl.Id == _aimTouchId || _aimTouchId < 0)
                    {
                        _aimTouchId = tl.Id;
                        sawAim = true;
                        _aimVp = vp;
                        _mousePos = pt;
                        _mouseLeftDown = true;
                    }
                }
            }

            if (!sawJoy)
            {
                _joyTouchId = -1;
                _joyKnob = _joyCenter;
            }
            if (!sawAim)
            {
                _aimTouchId = -1;
            }

            return stick;
        }

        public void Draw(SpriteBatch sb, Texture2D pixel, Texture2D circle, Texture2D ring, PadBridge br, int vpW, int vpH)
        {
            if (_kind == ArcadeKind.None || sb == null || pixel == null)
                return;

            ComputeLayout(vpW, vpH);
            int fs = Math.Max(1, vpH / 300);

            // ESC pixel button top-left
            PixelUi.DrawPixelPill(sb, pixel, br, _escBtnRect, "ESC", PixelRole.Special, _escPulseFrames > 0, vpH, fs);

            if (_kind == ArcadeKind.JunimoKart)
            {
                PixelUi.DrawPixelPill(sb, pixel, br, _pauseBtnRect, "PAUSE", PixelRole.Neutral, _pausePulseFrames > 0, vpH, fs);
                PixelUi.DrawPixelPill(sb, pixel, br, _bigActionRect, "JUMP", PixelRole.Action, _activeKeys.Contains(Keys.Space), vpH, Math.Max(2, vpH / 240));
                return;
            }

            if (_kind == ArcadeKind.Darts)
            {
                if (_aimTouchId >= 0)
                    PixelUi.DrawPixelCrosshair(sb, pixel, _aimVp, Math.Max(22f, Math.Min(vpW, vpH) * 0.036f), vpH);
                return;
            }

            // Left Pixel Joystick
            PixelUi.DrawPixelJoystick(sb, _joyCenter, _joyRadius, _joyKnob, _joyTouchId >= 0, vpH, 0.95f);

            if (_kind == ArcadeKind.PrairieKing)
            {
                PixelUi.DrawPixelPill(sb, pixel, br, _pauseBtnRect, "PAUSE", PixelRole.Neutral, _pausePulseFrames > 0, vpH, fs);
                PixelUi.DrawPixelPill(sb, pixel, br, _shootUp, "^", PixelRole.Tool, _activeKeys.Contains(Keys.Up), vpH, fs);
                PixelUi.DrawPixelPill(sb, pixel, br, _shootDn, "v", PixelRole.Tool, _activeKeys.Contains(Keys.Down), vpH, fs);
                PixelUi.DrawPixelPill(sb, pixel, br, _shootLt, "<", PixelRole.Tool, _activeKeys.Contains(Keys.Left), vpH, fs);
                PixelUi.DrawPixelPill(sb, pixel, br, _shootRt, ">", PixelRole.Tool, _activeKeys.Contains(Keys.Right), vpH, fs);
                PixelUi.DrawPixelPill(sb, pixel, br, _spcBtnRect, "ITEM", PixelRole.Action, _activeKeys.Contains(Keys.Space), vpH, fs);
            }
            else if (_kind == ArcadeKind.CraneGame)
            {
                PixelUi.DrawPixelPill(sb, pixel, br, _bigActionRect, "GRAB", PixelRole.Tool, _mouseLeftDown, vpH, Math.Max(2, vpH / 240));
            }
            else if (_kind == ArcadeKind.FishingGame)
            {
                PixelUi.DrawPixelPill(sb, pixel, br, _bigActionRect, "CAST", PixelRole.Action, _mouseLeftDown, vpH, Math.Max(2, vpH / 240));
            }
            else if (_kind == ArcadeKind.TargetGame)
            {
                if (_aimTouchId >= 0)
                    PixelUi.DrawPixelCrosshair(sb, pixel, _aimVp, Math.Max(22f, Math.Min(vpW, vpH) * 0.036f), vpH);
            }
        }

        private void ComputeLayout(int vpW, int vpH)
        {
            float unit = Math.Min(vpW, vpH);
            _joyRadius = Math.Clamp(unit * 0.12f, 52f, unit * 0.18f);
            _joyCenter = new Vector2(vpW * 0.14f, vpH * 0.72f);
            if (_joyTouchId < 0) _joyKnob = _joyCenter;

            int escH = (int)Math.Clamp(unit * 0.062f, 32f, 56f);
            int escW = (int)(escH * 1.9f);
            _escBtnRect = new Rectangle((int)(vpW * 0.05f), (int)(vpH * 0.03f), escW, escH);
            int pW = (int)(escH * 2.2f);
            _pauseBtnRect = new Rectangle(_escBtnRect.Right + Math.Max(8, escH / 4), _escBtnRect.Y, pW, escH);

            int btnS = (int)Math.Clamp(unit * 0.115f, 44f, 88f);
            int gap = Math.Max(8, btnS / 5);
            int cx = (int)(vpW * 0.84f);
            int cy = (int)(vpH * 0.68f);

            _shootUp = new Rectangle(cx - btnS / 2, cy - btnS - gap / 2, btnS, btnS);
            _shootDn = new Rectangle(cx - btnS / 2, cy + gap / 2, btnS, btnS);
            _shootLt = new Rectangle(cx - btnS - btnS / 2 - gap / 2, cy - btnS / 2, btnS, btnS);
            _shootRt = new Rectangle(cx + btnS / 2 + gap / 2, cy - btnS / 2, btnS, btnS);

            int spcW = (int)(btnS * 1.45f);
            int spcH = (int)(btnS * 0.78f);
            _spcBtnRect = new Rectangle(cx - spcW / 2, _shootUp.Y - spcH - gap * 2, spcW, spcH);

            int bigS = (int)Math.Clamp(unit * 0.20f, 76f, 150f);
            _bigActionRect = new Rectangle((int)(vpW * 0.82f) - bigS / 2, (int)(vpH * 0.72f) - bigS / 2, bigS, bigS);
        }
    }
}
