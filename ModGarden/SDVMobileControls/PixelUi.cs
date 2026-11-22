using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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

        /// <summary>
        /// Draws a notched-corner Stardew 16-bit pixel pill button with subtle translucent wood tint
        /// and 1-step top/left highlight bevel + bottom/right shade.
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

            int px = Step(vpH);
            if (r.Width < px * 6 || r.Height < px * 6)
                px = Math.Max(1, Math.Min(r.Width, r.Height) / 6);

            GetRolePalette(role, pressed, out Color fill, out Color outer, out Color hi, out Color lo, out Color ink);
            fill *= alphaScale;
            outer *= alphaScale;
            hi *= alphaScale;
            lo *= alphaScale;
            ink *= Math.Clamp(alphaScale + 0.12f, 0.25f, 1.0f);

            // Slight 1-pixel press depression feel via swapped bevel
            if (pressed)
            {
                Color tmp = hi;
                hi = lo;
                lo = tmp;
            }

            // 1. Notched pixel fill (cuts 4 corners by px for crisp pixel-art bevel)
            sb.Draw(pixel, new Rectangle(r.X + px, r.Y + px, r.Width - px * 2, r.Height - px * 2), fill);

            // 2. Crisp notched outer dark wood outline
            sb.Draw(pixel, new Rectangle(r.X + px, r.Y, r.Width - px * 2, px), outer);            // Top
            sb.Draw(pixel, new Rectangle(r.X + px, r.Bottom - px, r.Width - px * 2, px), outer);   // Bottom
            sb.Draw(pixel, new Rectangle(r.X, r.Y + px, px, r.Height - px * 2), outer);            // Left
            sb.Draw(pixel, new Rectangle(r.Right - px, r.Y + px, px, r.Height - px * 2), outer);   // Right

            // 3. Subtle 1-step pixel highlight (top & left) and shadow (bottom & right)
            if (r.Width > px * 5 && r.Height > px * 5)
            {
                sb.Draw(pixel, new Rectangle(r.X + px * 2, r.Y + px, r.Width - px * 4, px), hi);
                sb.Draw(pixel, new Rectangle(r.X + px, r.Y + px * 2, px, r.Height - px * 4), hi);
                sb.Draw(pixel, new Rectangle(r.X + px * 2, r.Bottom - px * 2, r.Width - px * 4, px), lo);
                sb.Draw(pixel, new Rectangle(r.Right - px * 2, r.Y + px * 2, px, r.Height - px * 4), lo);
            }

            // 4. Label text (subtle 1px dark drop-shadow for readability without visual clutter)
            if (!string.IsNullOrEmpty(label) && bridge != null)
            {
                int fs = fontSizeOverride > 0 ? fontSizeOverride : Math.Max(1, vpH / 320);
                int dy = pressed ? Math.Max(1, px / 2) : 0;
                var shadowR = new Rectangle(r.X + px / 2, r.Y + dy + Math.Max(1, px / 2), r.Width, r.Height);
                var textR = new Rectangle(r.X, r.Y + dy, r.Width, r.Height);
                bridge.DrawCenteredString(sb, pixel, label, shadowR, new Color(18, 10, 6) * (0.58f * alphaScale), fs);
                bridge.DrawCenteredString(sb, pixel, label, textR, ink, fs);
            }
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
            float fillAlpha = pressed ? 0.56f : 0.25f;
            float ringAlpha = pressed ? 0.82f : 0.44f;

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

            float fillA = (active ? 0.30f : 0.16f) * opacityScale;
            float ringA = (active ? 0.56f : 0.32f) * opacityScale;
            float tickA = (active ? 0.48f : 0.24f) * opacityScale;

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
        /// Draws a crisp notched Stardew parchment box (for Fixed Tooltip, Vertical Toolbar, Safe Hints).
        /// </summary>
        public static void DrawParchmentBox(SpriteBatch sb, Texture2D pixel, Rectangle r, int px, float alpha = 0.94f)
        {
            if (sb == null || pixel == null || r.Width < px * 6 || r.Height < px * 6)
                return;

            Color bg = new Color(246, 224, 176) * alpha;
            Color woodOuter = new Color(74, 37, 17) * alpha;
            Color woodMid = new Color(176, 108, 48) * alpha;
            Color innerHi = new Color(255, 244, 214) * alpha;

            sb.Draw(pixel, new Rectangle(r.X + px, r.Y + px, r.Width - px * 2, r.Height - px * 2), bg);

            // Notched outer dark brown wood
            sb.Draw(pixel, new Rectangle(r.X + px, r.Y, r.Width - px * 2, px), woodOuter);
            sb.Draw(pixel, new Rectangle(r.X + px, r.Bottom - px, r.Width - px * 2, px), woodOuter);
            sb.Draw(pixel, new Rectangle(r.X, r.Y + px, px, r.Height - px * 2), woodOuter);
            sb.Draw(pixel, new Rectangle(r.Right - px, r.Y + px, px, r.Height - px * 2), woodOuter);

            // Inner warm chestnut border
            var inner = new Rectangle(r.X + px, r.Y + px, r.Width - px * 2, r.Height - px * 2);
            sb.Draw(pixel, new Rectangle(inner.X, inner.Y, inner.Width, px), woodMid);
            sb.Draw(pixel, new Rectangle(inner.X, inner.Bottom - px, inner.Width, px), woodMid);
            sb.Draw(pixel, new Rectangle(inner.X, inner.Y, px, inner.Height), woodMid);
            sb.Draw(pixel, new Rectangle(inner.Right - px, inner.Y, px, inner.Height), woodMid);

            // Subtle parchment inner top highlight
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
