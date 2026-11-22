using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace SDVMobileControls
{
    internal enum PixelRole
    {
        Neutral = 0,
        Active = 1,
        Tool = 2,
        Action = 3,
        Special = 4,
        CustomKey = 5,
        Warn = 6
    }

    /// <summary>
    /// Crisp, low-distraction Stardew Valley 16-bit Pixel-Art renderer for all in-game controls.
    /// Uses notched pixel corners, warm muted wood/parchment tones, and controlled translucency
    /// so touch controls feel native to Stardew Valley without pulling attention away from gameplay.
    /// </summary>
    internal static class PixelUi
    {
        public static float GlobalOpacity { get; set; } = 1.0f;

        private static Texture2D _pxCircleFill;
        private static Texture2D _pxCircleRing;
        private static Texture2D _pxJoyTicks;

        public static int Step(int vpH) => Math.Max(2, vpH / 380);

        public static void EnsurePixelCircleTextures(GraphicsDevice gd)
        {
            if (gd == null) return;
            if (_pxCircleFill != null && !_pxCircleFill.IsDisposed) return;

            // Crisp 24x24 pixel-art medallion (hard 1-bit pixel steps -> authentic retro silhouette!)
            const int sz = 24;
            float c = (sz - 1) * 0.5f;
            float outerR = sz * 0.5f - 0.35f;
            float innerR = outerR - 1.65f;

            var fill = new Color[sz * sz];
            var ring = new Color[sz * sz];
            var ticks = new Color[sz * sz];

            for (int y = 0; y < sz; y++)
            {
                for (int x = 0; x < sz; x++)
                {
                    float dx = x - c;
                    float dy = y - c;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    int idx = y * sz + x;

                    bool inside = d <= outerR;
                    bool onBorder = inside && d >= innerR;

                    fill[idx] = inside ? Color.White : Color.Transparent;
                    ring[idx] = onBorder ? Color.White : Color.Transparent;

                    // Subtle cardinal pixel pips (N/S/E/W) for joystick direction orientation
                    bool isTick =
                        ((x == 11 || x == 12) && (y == 3 || y == 4 || y == sz - 4 || y == sz - 5)) ||
                        ((y == 11 || y == 12) && (x == 3 || x == 4 || x == sz - 4 || x == sz - 5));
                    ticks[idx] = isTick ? Color.White : Color.Transparent;
                }
            }

            _pxCircleFill = new Texture2D(gd, sz, sz);
            _pxCircleFill.SetData(fill);

            _pxCircleRing = new Texture2D(gd, sz, sz);
            _pxCircleRing.SetData(ring);

            _pxJoyTicks = new Texture2D(gd, sz, sz);
            _pxJoyTicks.SetData(ticks);
        }

        private static readonly Rectangle NativeDialogBoxSrc = new Rectangle(0, 256, 60, 60);
        private static readonly Rectangle NativeSubBoxSrc = new Rectangle(384, 396, 15, 15);
        private static readonly Rectangle NativeButtonSrc = new Rectangle(432, 439, 9, 9);
        private static readonly Rectangle NativeCloseXSrc = new Rectangle(337, 494, 12, 12);

        /// <summary>
        /// Draws a native Stardew Valley 9-slice UI button (Game1.mouseCursors) so external UI/UX mods
        /// (like Cat Valley UI) automatically skin all custom controls, pills, and popups!
        /// </summary>
        public static void DrawPixelPill(
            SpriteBatch sb,
            Texture2D pixel,
            PadBridge bridge,
            Rectangle r,
            string label,
            PixelRole role,
            bool pressed,
            int vpH,
            int fontSizeOverride = -1,
            float alphaScale = 1.0f)
        {
            if (sb == null || pixel == null || r.Width < 8 || r.Height < 8)
                return;

            alphaScale = Math.Clamp(alphaScale * GlobalOpacity, 0.14f, 1.0f);
            int px = Step(vpH);
            if (r.Width < px * 6 || r.Height < px * 6)
                px = Math.Max(1, Math.Min(r.Width, r.Height) / 6);

            bool drewNative = false;
            try
            {
                if (Game1.mouseCursors != null)
                {
                    Color tint = GetNativeRoleTint(role, pressed);
                    float boxScale = Math.Clamp(Math.Min(r.Width, r.Height) / 14f, 1.6f, 3.4f);
                    IClickableMenu.drawTextureBox(
                        sb,
                        Game1.mouseCursors,
                        NativeButtonSrc,
                        r.X,
                        r.Y,
                        r.Width,
                        r.Height,
                        tint * alphaScale,
                        boxScale,
                        drawShadow: false);
                    drewNative = true;
                }
            }
            catch { }

            if (!drewNative)
            {
                GetRolePalette(role, pressed, out Color fill, out Color outer, out Color hi, out Color lo, out _);
                fill *= alphaScale;
                outer *= alphaScale;
                hi *= alphaScale;
                lo *= alphaScale;
                if (pressed) { Color tmp = hi; hi = lo; lo = tmp; }

                sb.Draw(pixel, new Rectangle(r.X + px, r.Y + px, r.Width - px * 2, r.Height - px * 2), fill);
                sb.Draw(pixel, new Rectangle(r.X + px, r.Y, r.Width - px * 2, px), outer);
                sb.Draw(pixel, new Rectangle(r.X + px, r.Bottom - px, r.Width - px * 2, px), outer);
                sb.Draw(pixel, new Rectangle(r.X, r.Y + px, px, r.Height - px * 2), outer);
                sb.Draw(pixel, new Rectangle(r.Right - px, r.Y + px, px, r.Height - px * 2), outer);
                if (r.Width > px * 5 && r.Height > px * 5)
                {
                    sb.Draw(pixel, new Rectangle(r.X + px * 2, r.Y + px, r.Width - px * 4, px), hi);
                    sb.Draw(pixel, new Rectangle(r.X + px, r.Y + px * 2, px, r.Height - px * 4), hi);
                    sb.Draw(pixel, new Rectangle(r.X + px * 2, r.Bottom - px * 2, r.Width - px * 4, px), lo);
                    sb.Draw(pixel, new Rectangle(r.Right - px * 2, r.Y + px * 2, px, r.Height - px * 4), lo);
                }
            }

            if (!string.IsNullOrEmpty(label) && bridge != null)
            {
                int fs = fontSizeOverride > 0 ? fontSizeOverride : Math.Max(1, vpH / 320);
                int dy = pressed ? Math.Max(1, px / 2) : 0;
                Color ink = drewNative
                    ? (Game1.textColor * Math.Clamp(alphaScale + 0.14f, 0.35f, 1.0f))
                    : (new Color(248, 238, 214) * Math.Clamp(alphaScale + 0.12f, 0.25f, 1.0f));
                Color shCol = drewNative
                    ? (new Color(255, 250, 238) * (0.36f * alphaScale))
                    : (new Color(18, 10, 6) * (0.58f * alphaScale));
                var shadowR = new Rectangle(r.X + Math.Max(1, px / 2), r.Y + dy + Math.Max(1, px / 2), r.Width, r.Height);
                var textR = new Rectangle(r.X, r.Y + dy, r.Width, r.Height);
                bridge.DrawCenteredString(sb, pixel, label, shadowR, shCol, fs);
                bridge.DrawCenteredString(sb, pixel, label, textR, ink, fs);
            }
        }

        private static Color GetNativeRoleTint(PixelRole role, bool pressed)
        {
            if (pressed)
                return new Color(255, 236, 186);
            switch (role)
            {
                case PixelRole.Active:    return new Color(255, 240, 192);
                case PixelRole.Tool:      return new Color(255, 244, 218);
                case PixelRole.Action:    return new Color(224, 250, 224);
                case PixelRole.Special:   return new Color(255, 224, 216);
                case PixelRole.CustomKey: return new Color(242, 232, 255);
                case PixelRole.Warn:      return new Color(255, 214, 204);
                default:                  return Color.White;
            }
        }

        public static void DrawNativeDialogBox(SpriteBatch sb, Texture2D pixel, Rectangle r, float alpha = 1f, bool drawShadow = true, Color? tintOverride = null)
        {
            if (sb == null || r.Width < 16 || r.Height < 16) return;
            Color col = (tintOverride ?? Color.White) * Math.Clamp(alpha, 0.05f, 1f);
            try
            {
                if (Game1.menuTexture != null)
                {
                    float s = Math.Clamp(Math.Min(r.Width, r.Height) / 240f, 0.72f, 1.0f);
                    IClickableMenu.drawTextureBox(sb, Game1.menuTexture, NativeDialogBoxSrc, r.X, r.Y, r.Width, r.Height, col, s, drawShadow);
                    return;
                }
            }
            catch { }
            DrawParchmentBoxFallback(sb, pixel, r, 2, alpha);
        }

        public static void DrawNativeSubBox(SpriteBatch sb, Texture2D pixel, Rectangle r, Color tint, float alpha = 1f)
        {
            if (sb == null || r.Width < 12 || r.Height < 12) return;
            Color col = tint * Math.Clamp(alpha, 0.05f, 1f);
            try
            {
                if (Game1.mouseCursors != null)
                {
                    float s = Math.Clamp(Math.Min(r.Width, r.Height) / 30f, 2.0f, 4.0f);
                    IClickableMenu.drawTextureBox(sb, Game1.mouseCursors, NativeSubBoxSrc, r.X, r.Y, r.Width, r.Height, col, s, drawShadow: false);
                    return;
                }
            }
            catch { }
            if (pixel != null) sb.Draw(pixel, r, col * 0.25f);
        }

        public static void DrawNativeButton(SpriteBatch sb, Texture2D pixel, PadBridge bridge, Rectangle r, string text, Color tint, float fontScale, int vpH, bool drawShadow = true)
        {
            if (sb == null || r.Width < 10 || r.Height < 10) return;
            bool drewBox = false;
            try
            {
                if (Game1.mouseCursors != null)
                {
                    float s = Math.Clamp(Math.Min(r.Width, r.Height) / 14f, 2.0f, 4.0f);
                    IClickableMenu.drawTextureBox(sb, Game1.mouseCursors, NativeButtonSrc, r.X, r.Y, r.Width, r.Height, tint, s, drawShadow);
                    drewBox = true;
                }
            }
            catch { }

            if (!drewBox && pixel != null)
                DrawParchmentBoxFallback(sb, pixel, r, Step(vpH), 0.95f);

            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                if (Game1.smallFont != null)
                {
                    Vector2 sz = Game1.smallFont.MeasureString(text) * fontScale;
                    if (sz.X > r.Width - 12 && sz.X > 1f)
                    {
                        float fit = (r.Width - 12f) / sz.X;
                        fontScale *= fit;
                        sz *= fit;
                    }
                    Vector2 pos = new Vector2(r.X + (r.Width - sz.X) * 0.5f, r.Y + (r.Height - sz.Y) * 0.5f + 1f);
                    Utility.drawTextWithShadow(sb, text, Game1.smallFont, pos, Game1.textColor, fontScale);
                    return;
                }
            }
            catch { }

            bridge?.DrawCenteredString(sb, pixel, text, r, Game1.textColor, Math.Max(1, vpH / 300));
        }

        public static void DrawNativeCloseX(SpriteBatch sb, Rectangle r, float alpha = 1f)
        {
            if (sb == null || r.Width < 8 || r.Height < 8) return;
            try
            {
                if (Game1.mouseCursors != null)
                {
                    sb.Draw(Game1.mouseCursors, r, NativeCloseXSrc, Color.White * Math.Clamp(alpha, 0.1f, 1f));
                }
            }
            catch { }
        }

        /// <summary>
        /// Draws a crisp stepped-pixel octagonal/round medallion button (for Visible 2-Button USE/ACT & Joystick).
        /// Low opacity idle so gameplay remains completely unobstructed.
        /// </summary>
        public static void DrawPixelMedallion(
            SpriteBatch sb,
            Texture2D pixel,
            PadBridge bridge,
            Rectangle r,
            string label,
            PixelRole role,
            bool pressed,
            int vpH)
        {
            if (sb == null || pixel == null) return;
            EnsurePixelCircleTextures(sb.GraphicsDevice);

            GetRolePalette(role, pressed, out Color fill, out Color outer, out Color hi, out _, out Color ink);
            float gOp = Math.Clamp(GlobalOpacity, 0.20f, 1.0f);
            float fillAlpha = (pressed ? 0.56f : 0.25f) * gOp;
            float ringAlpha = (pressed ? 0.82f : 0.44f) * gOp;

            // Keep idle super unobtrusive
            Color baseCol = pressed ? new Color(fill.R, fill.G, fill.B) : new Color(34, 24, 16);
            Color rimCol = pressed ? new Color(255, 224, 140) : new Color(216, 188, 144);
            if (!pressed && role == PixelRole.Tool) rimCol = new Color(224, 184, 118);
            if (!pressed && role == PixelRole.Action) rimCol = new Color(158, 214, 152);

            // Outer dark pixel outline (shifted +2px bigger)
            int px = Step(vpH);
            var outerR = new Rectangle(r.X - px, r.Y - px, r.Width + px * 2, r.Height + px * 2);
            if (_pxCircleRing != null && _pxCircleFill != null)
            {
                sb.Draw(_pxCircleFill, r, baseCol * fillAlpha);
                sb.Draw(_pxCircleRing, outerR, new Color(20, 12, 8) * (pressed ? 0.72f : 0.40f));
                sb.Draw(_pxCircleRing, r, rimCol * ringAlpha);
            }

            if (!string.IsNullOrEmpty(label) && bridge != null)
            {
                int fs = Math.Max(2, (int)(r.Width / 48f));
                var shadowR = new Rectangle(r.X + px / 2, r.Y + px / 2, r.Width, r.Height);
                bridge.DrawCenteredString(sb, pixel, label, shadowR, new Color(16, 10, 6) * (pressed ? 0.70f : 0.42f), fs);
                bridge.DrawCenteredString(sb, pixel, label, r, ink * (pressed ? 0.96f : 0.78f), fs);
            }
        }

        /// <summary>
        /// Draws a subtle Stardew pixel-art joystick ring + cardinal ticks + stepped pixel thumb knob.
        /// </summary>
        public static void DrawPixelJoystick(
            SpriteBatch sb,
            Vector2 center,
            float radius,
            Vector2 knob,
            bool active,
            int vpH,
            float opacityScale = 1.0f)
        {
            if (sb == null) return;
            EnsurePixelCircleTextures(sb.GraphicsDevice);
            if (_pxCircleFill == null || _pxCircleRing == null) return;

            int px = Step(vpH);
            int d = Math.Max(8, (int)(radius * 2f));
            var baseR = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), d, d);
            var outR = new Rectangle(baseR.X - px, baseR.Y - px, baseR.Width + px * 2, baseR.Height + px * 2);

            float gOp = Math.Clamp(opacityScale * GlobalOpacity, 0.16f, 1.0f);
            float fillA = (active ? 0.30f : 0.16f) * gOp;
            float ringA = (active ? 0.56f : 0.32f) * gOp;
            float tickA = (active ? 0.48f : 0.24f) * gOp;

            sb.Draw(_pxCircleFill, baseR, new Color(34, 24, 16) * fillA);
            sb.Draw(_pxCircleRing, outR, new Color(18, 11, 7) * (ringA * 0.75f));
            sb.Draw(_pxCircleRing, baseR, new Color(224, 198, 152) * ringA);
            if (_pxJoyTicks != null)
                sb.Draw(_pxJoyTicks, baseR, new Color(246, 224, 178) * tickA);

            float kr = radius * 0.40f;
            int kd = Math.Max(6, (int)(kr * 2f));
            var knobR = new Rectangle((int)(knob.X - kr), (int)(knob.Y - kr), kd, kd);
            var knobOut = new Rectangle(knobR.X - px, knobR.Y - px, knobR.Width + px * 2, knobR.Height + px * 2);
            sb.Draw(_pxCircleFill, knobR, (active ? new Color(220, 178, 96) : new Color(214, 192, 154)) * ((active ? 0.62f : 0.34f) * opacityScale));
            sb.Draw(_pxCircleRing, knobOut, new Color(26, 16, 10) * (ringA * 0.85f));
            sb.Draw(_pxCircleRing, knobR, new Color(255, 236, 184) * ((active ? 0.78f : 0.40f) * opacityScale));
        }

        /// <summary>
        /// Draws a native Stardew dialog/parchment box using Game1.menuTexture (automatically themed by UI mods like Cat Valley UI!).
        /// </summary>
        public static void DrawParchmentBox(SpriteBatch sb, Texture2D pixel, Rectangle r, int px, float alpha = 0.94f)
        {
            if (sb == null || r.Width < 12 || r.Height < 12)
                return;

            try
            {
                if (Game1.menuTexture != null)
                {
                    float s = Math.Clamp(Math.Min(r.Width, r.Height) / 140f, 0.65f, 1.0f);
                    IClickableMenu.drawTextureBox(sb, Game1.menuTexture, NativeDialogBoxSrc, r.X, r.Y, r.Width, r.Height, Color.White * Math.Clamp(alpha, 0.05f, 1f), s, drawShadow: false);
                    return;
                }
            }
            catch { }

            DrawParchmentBoxFallback(sb, pixel, r, px, alpha);
        }

        private static void DrawParchmentBoxFallback(SpriteBatch sb, Texture2D pixel, Rectangle r, int px, float alpha)
        {
            if (sb == null || pixel == null || r.Width < px * 6 || r.Height < px * 6)
                return;

            Color bg = new Color(246, 224, 176) * alpha;
            Color woodOuter = new Color(74, 37, 17) * alpha;
            Color woodMid = new Color(176, 108, 48) * alpha;
            Color innerHi = new Color(255, 244, 214) * alpha;

            sb.Draw(pixel, new Rectangle(r.X + px, r.Y + px, r.Width - px * 2, r.Height - px * 2), bg);

            sb.Draw(pixel, new Rectangle(r.X + px, r.Y, r.Width - px * 2, px), woodOuter);
            sb.Draw(pixel, new Rectangle(r.X + px, r.Bottom - px, r.Width - px * 2, px), woodOuter);
            sb.Draw(pixel, new Rectangle(r.X, r.Y + px, px, r.Height - px * 2), woodOuter);
            sb.Draw(pixel, new Rectangle(r.Right - px, r.Y + px, px, r.Height - px * 2), woodOuter);

            var inner = new Rectangle(r.X + px, r.Y + px, r.Width - px * 2, r.Height - px * 2);
            sb.Draw(pixel, new Rectangle(inner.X, inner.Y, inner.Width, px), woodMid);
            sb.Draw(pixel, new Rectangle(inner.X, inner.Bottom - px, inner.Width, px), woodMid);
            sb.Draw(pixel, new Rectangle(inner.X, inner.Y, px, inner.Height), woodMid);
            sb.Draw(pixel, new Rectangle(inner.Right - px, inner.Y, px, inner.Height), woodMid);

            if (r.Width > px * 6 && r.Height > px * 6)
                sb.Draw(pixel, new Rectangle(r.X + px * 2, r.Y + px * 2, r.Width - px * 4, px), innerHi);
        }

        // Crisp 11x11 pixel-art smiley face for the in-game Emote button (subtle warm Stardew gold)
        private static readonly string[] SmileyRows = new[]
        {
            "   XXXXX   ",
            "  XYYYYYX  ",
            " XYYYYYYYX ",
            "XYYXYYYXYYX",
            "XYYXYYYXYYX",
            "XYYYYYYYYYX",
            "XYXYYYYYXYX",
            "XYYXYYYXYYX",
            " XYYXXXYYX ",
            "  XYYYYYX  ",
            "   XXXXX   ",
        };

        public static void DrawPixelSmileyButton(SpriteBatch sb, Texture2D pixel, Rectangle r, bool pressed, int vpH, float alphaScale = 1.0f)
        {
            if (sb == null || pixel == null || r.Width < 12 || r.Height < 12)
                return;

            DrawPixelPill(sb, pixel, null, r, null, pressed ? PixelRole.Active : PixelRole.Neutral, pressed, vpH, -1, alphaScale);

            int dim = Math.Min(r.Width, r.Height);
            int cell = Math.Max(1, (dim - Math.Max(8, dim / 4)) / 11);
            int iconSz = cell * 11;
            int ox = r.X + (r.Width - iconSz) / 2;
            int oy = r.Y + (r.Height - iconSz) / 2 + (pressed ? 1 : 0);

            Color outline = new Color(56, 32, 12) * (0.92f * alphaScale);
            Color face = (pressed ? new Color(255, 224, 96) : new Color(242, 206, 108)) * (0.90f * alphaScale);

            for (int y = 0; y < 11; y++)
            {
                string row = SmileyRows[y];
                for (int x = 0; x < 11; x++)
                {
                    char ch = row[x];
                    if (ch == ' ') continue;
                    Color col = (ch == 'X') ? outline : face;
                    sb.Draw(pixel, new Rectangle(ox + x * cell, oy + y * cell, cell, cell), col);
                }
            }
        }

        /// <summary>
        /// Draws a crisp 16-bit Stardew pixel-art tile target cursor (4 notched L-corner brackets + faint fill)
        /// for Tap-to-Move destinations and Safe 2-Tap selections. Subtle and non-distracting.
        /// </summary>
        public static void DrawPixelTileTarget(SpriteBatch sb, Texture2D pixel, Rectangle r, Color accent, float fillAlpha = 0.14f)
        {
            if (sb == null || pixel == null || r.Width < 12 || r.Height < 12)
                return;

            int px = Math.Clamp(Math.Min(r.Width, r.Height) / 14, 2, 5);
            int arm = Math.Clamp(Math.Min(r.Width, r.Height) / 4, px * 2, Math.Min(r.Width, r.Height) / 3);
            Color shade = new Color(20, 14, 8) * 0.65f;

            // Very soft inner tint so terrain remains crystal clear
            sb.Draw(pixel, new Rectangle(r.X + px, r.Y + px, r.Width - px * 2, r.Height - px * 2), accent * fillAlpha);

            // Helper lambda pattern via local helper
            void DrawCorner(int cx, int cy, int dx, int dy)
            {
                // Dark pixel outer shadow (offset 1 step)
                int hx = dx > 0 ? cx : cx - arm;
                int vy = dy > 0 ? cy : cy - arm;
                sb.Draw(pixel, new Rectangle(hx, cy + (dy > 0 ? -px : px), arm, px), shade);
                sb.Draw(pixel, new Rectangle(cx + (dx > 0 ? -px : px), vy, px, arm), shade);

                // Bright pixel L-bracket
                sb.Draw(pixel, new Rectangle(hx, cy, arm, px), accent * 0.92f);
                sb.Draw(pixel, new Rectangle(cx, vy, px, arm), accent * 0.92f);
            }

            DrawCorner(r.X, r.Y, 1, 1);
            DrawCorner(r.Right - px, r.Y, -1, 1);
            DrawCorner(r.X, r.Bottom - px, 1, -1);
            DrawCorner(r.Right - px, r.Bottom - px, -1, -1);
        }

        /// <summary>
        /// Draws a crisp 16-bit pixel aiming reticle for Slingshot drag-to-aim.
        /// </summary>
        public static void DrawPixelCrosshair(SpriteBatch sb, Texture2D pixel, Vector2 center, float radius, int vpH)
        {
            if (sb == null || pixel == null) return;
            EnsurePixelCircleTextures(sb.GraphicsDevice);

            int px = Step(vpH);
            int d = Math.Max(16, (int)(radius * 2f));
            var ringR = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), d, d);
            var outR = new Rectangle(ringR.X - px, ringR.Y - px, ringR.Width + px * 2, ringR.Height + px * 2);

            Color aimRed = new Color(236, 92, 68) * 0.86f;
            Color dark = new Color(28, 14, 10) * 0.72f;
            if (_pxCircleRing != null)
            {
                sb.Draw(_pxCircleRing, outR, dark);
                sb.Draw(_pxCircleRing, ringR, aimRed);
            }

            int cx = (int)center.X;
            int cy = (int)center.Y;
            int tick = Math.Max(px * 2, (int)(radius * 0.36f));
            int gap = Math.Max(px * 2, (int)(radius * 0.35f));
            // 4 pixel crosshair arms + center pip
            sb.Draw(pixel, new Rectangle(cx - px / 2, cy - gap - tick, px, tick), aimRed);
            sb.Draw(pixel, new Rectangle(cx - px / 2, cy + gap, px, tick), aimRed);
            sb.Draw(pixel, new Rectangle(cx - gap - tick, cy - px / 2, tick, px), aimRed);
            sb.Draw(pixel, new Rectangle(cx + gap, cy - px / 2, tick, px), aimRed);
            sb.Draw(pixel, new Rectangle(cx - px, cy - px, px * 2, px * 2), new Color(255, 240, 180) * 0.94f);
        }

        private static void GetRolePalette(PixelRole role, bool pressed, out Color fill, out Color outer, out Color hi, out Color lo, out Color ink)
        {
            outer = new Color(24, 14, 8) * (pressed ? 0.88f : 0.64f);
            ink = new Color(250, 238, 208) * (pressed ? 0.98f : 0.86f);

            if (pressed || role == PixelRole.Active)
            {
                fill = new Color(184, 124, 42) * 0.76f;
                hi = new Color(255, 228, 148) * 0.82f;
                lo = new Color(96, 54, 18) * 0.78f;
                ink = new Color(255, 248, 224);
                if (role == PixelRole.Special && pressed)
                {
                    fill = new Color(186, 62, 48) * 0.84f;
                    hi = new Color(248, 148, 130) * 0.80f;
                }
                else if (role == PixelRole.Action && pressed)
                {
                    fill = new Color(76, 152, 82) * 0.80f;
                    hi = new Color(170, 235, 166) * 0.78f;
                }
                return;
            }

            switch (role)
            {
                case PixelRole.Tool:
                    fill = new Color(128, 86, 36) * 0.48f;
                    hi = new Color(244, 206, 134) * 0.34f;
                    lo = new Color(46, 26, 10) * 0.42f;
                    break;
                case PixelRole.Action:
                    fill = new Color(56, 108, 62) * 0.46f;
                    hi = new Color(172, 224, 164) * 0.32f;
                    lo = new Color(22, 48, 24) * 0.42f;
                    break;
                case PixelRole.Special:
                    fill = new Color(136, 52, 42) * 0.56f;
                    hi = new Color(238, 158, 142) * 0.36f;
                    lo = new Color(62, 20, 16) * 0.48f;
                    break;
                case PixelRole.CustomKey:
                    fill = new Color(48, 38, 28) * 0.44f;
                    hi = new Color(222, 196, 152) * 0.26f;
                    lo = new Color(18, 12, 8) * 0.36f;
                    break;
                case PixelRole.Warn:
                    fill = new Color(188, 64, 34) * 0.88f;
                    hi = new Color(255, 222, 124) * 0.85f;
                    lo = new Color(98, 24, 14) * 0.80f;
                    ink = Color.White;
                    break;
                default: // Neutral subtle Stardew dark wood
                    fill = new Color(42, 29, 19) * 0.44f;
                    hi = new Color(228, 198, 150) * 0.24f;
                    lo = new Color(16, 10, 6) * 0.34f;
                    break;
            }
        }
    }
}
