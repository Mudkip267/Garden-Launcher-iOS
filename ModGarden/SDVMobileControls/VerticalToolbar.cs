using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;
using StardewValley;
using StardewValley.Menus;

namespace SDVMobileControls
{
    /// <summary>
    /// Authentic Stardew Valley Mobile-style Full-Height Vertical Scrollable Toolbar (all 12 / 24 / 36 slots).
    /// Uses official Game1.menuTexture wooden 9-slice frame + tile 10/56 slot textures,
    /// spans full screen height with smooth momentum scrolling, and supports live thickness & opacity adjustment.
    /// </summary>
    internal sealed class VerticalToolbar
    {
        private static readonly Rectangle ToolbarFrameSrc = new Rectangle(0, 256, 60, 60);
        private static readonly RasterizerState ScissorRasterizer = new RasterizerState { ScissorTestEnable = true };

        private IClickableMenu _hiddenPcToolbar;
        private Rectangle _panelRect;
        private int _slotSize = 72;
        private float _visibleSlots = 9f;
        private int _pad = 12;
        private float _scrollSlots;
        private float _scrollVel;

        private int _touchId = -1;
        private float _touchStartY;
        private float _touchLastY;
        private bool _touchDragged;
        private int _tappedSlot = -1;

        public Rectangle Bounds => _panelRect;
        public bool IsTouchActive => _touchId >= 0;

        public void ResetTouch()
        {
            _touchId = -1;
            _touchDragged = false;
            _tappedSlot = -1;
        }

        public void ClearCachedToolbar()
        {
            _hiddenPcToolbar = null;
            _scrollVel = 0f;
            ResetTouch();
        }

        public void SyncHorizontalToolbarVisibility(bool useVertical)
        {
            var menus = Game1.onScreenMenus;
            if (menus == null)
                return;

            if (useVertical)
            {
                for (int i = menus.Count - 1; i >= 0; i--)
                {
                    if (menus[i] is Toolbar tb)
                    {
                        _hiddenPcToolbar = tb;
                        menus.RemoveAt(i);
                    }
                }
            }
            else if (_hiddenPcToolbar != null)
            {
                bool alreadyPresent = false;
                for (int i = 0; i < menus.Count; i++)
                {
                    if (menus[i] is Toolbar)
                    {
                        alreadyPresent = true;
                        break;
                    }
                }
                if (!alreadyPresent)
                    menus.Insert(0, _hiddenPcToolbar);
                _hiddenPcToolbar = null;
            }
        }

        /// <summary>
        /// Computes full-height edge-to-edge vertical layout using user thickness scale (0.60..1.45).
        /// </summary>
        public void ComputeLayout(int vpW, int vpH, int safeLeftPx, float sizeScale)
        {
            int maxItems = Math.Max(12, Game1.player?.MaxItems ?? 12);
            float scale = Math.Clamp(sizeScale, 0.60f, 1.45f);

            int vMargin = Math.Max(4, vpH / 160);
            int panelY = vMargin;
            int panelH = Math.Max(160, vpH - vMargin * 2);

            _pad = Math.Clamp((int)(vpH / 76f * Math.Sqrt(scale)), 9, 18);
            float baseSlot = Math.Clamp(vpH / 9.2f, 48f, 128f);
            _slotSize = (int)Math.Clamp(baseSlot * scale, 36f, vpH / 4.6f);

            int innerH = Math.Max(_slotSize, panelH - _pad * 2);
            _visibleSlots = Math.Max(1f, innerH / (float)_slotSize);

            int scrollbarW = maxItems > _visibleSlots ? Math.Max(5, _pad / 2) : 0;
            int scrollbarGap = scrollbarW > 0 ? Math.Max(3, _pad / 4) : 0;
            int panelW = _slotSize + _pad * 2 + scrollbarW + scrollbarGap;
            int x = Math.Max(safeLeftPx + Math.Max(4, vpW / 240), Math.Max(4, vpW / 220));

            _panelRect = new Rectangle(x, panelY, panelW, panelH);

            float maxScroll = Math.Max(0f, maxItems - _visibleSlots);
            _scrollSlots = Math.Clamp(_scrollSlots, 0f, maxScroll);
        }

        public void UpdateInertia()
        {
            if (_touchId >= 0 || Math.Abs(_scrollVel) < 0.004f)
                return;
            int maxItems = Math.Max(12, Game1.player?.MaxItems ?? 12);
            float maxScroll = Math.Max(0f, maxItems - _visibleSlots);
            _scrollSlots = Math.Clamp(_scrollSlots + _scrollVel, 0f, maxScroll);
            _scrollVel *= 0.87f;
            if (_scrollSlots <= 0f || _scrollSlots >= maxScroll)
                _scrollVel = 0f;
        }

        public bool HandleTouch(TouchLocation tl, Vector2 vp, out bool selectedItem)
        {
            selectedItem = false;
            int maxItems = Math.Max(12, Game1.player?.MaxItems ?? 12);
            float maxScroll = Math.Max(0f, maxItems - _visibleSlots);
            Point pt = new Point((int)vp.X, (int)vp.Y);

            if (tl.Id == _touchId)
            {
                if (tl.State == TouchLocationState.Pressed || tl.State == TouchLocationState.Moved)
                {
                    float dy = vp.Y - _touchLastY;
                    _touchLastY = vp.Y;
                    if (!_touchDragged && Math.Abs(vp.Y - _touchStartY) >= 14f)
                        _touchDragged = true;

                    if (_touchDragged && maxScroll > 0f)
                    {
                        float deltaSlots = -dy / Math.Max(20f, _slotSize);
                        _scrollSlots = Math.Clamp(_scrollSlots + deltaSlots, 0f, maxScroll);
                        _scrollVel = deltaSlots;
                    }
                    return true;
                }

                // Released
                if (!_touchDragged && _tappedSlot >= 0 && _tappedSlot < maxItems && Game1.player != null)
                {
                    Game1.player.CurrentToolIndex = _tappedSlot;
                    selectedItem = true;
                    _scrollVel = 0f;
                    try { Game1.playSound("stoneStep"); } catch { }
                }
                ResetTouch();
                return true;
            }

            if (tl.State == TouchLocationState.Pressed && _touchId < 0 && _panelRect.Contains(pt))
            {
                _touchId = tl.Id;
                _touchStartY = vp.Y;
                _touchLastY = vp.Y;
                _touchDragged = false;
                _scrollVel = 0f;
                float relY = vp.Y - (_panelRect.Y + _pad);
                int idx = (int)Math.Floor(_scrollSlots + relY / Math.Max(1f, _slotSize));
                _tappedSlot = (idx >= 0 && idx < maxItems) ? idx : -1;
                return true;
            }

            return false;
        }

        public void Draw(SpriteBatch sb, Texture2D pixel, int vpH, float opacity)
        {
            if (sb == null || pixel == null || Game1.player == null || _panelRect.Width <= 0)
                return;

            float alpha = Math.Clamp(opacity, 0.20f, 1.00f);
            float itemAlpha = Math.Clamp(alpha + 0.14f, 0.30f, 1.00f);
            int maxItems = Math.Max(12, Game1.player.MaxItems);
            int px = PixelUi.Step(vpH);

            // 1. Official Stardew Valley Mobile wooden 9-slice toolbar frame (Game1.menuTexture)
            bool drewNativeFrame = false;
            try
            {
                if (Game1.menuTexture != null)
                {
                    IClickableMenu.drawTextureBox(
                        sb,
                        Game1.menuTexture,
                        ToolbarFrameSrc,
                        _panelRect.X,
                        _panelRect.Y,
                        _panelRect.Width,
                        _panelRect.Height,
                        Color.White * alpha,
                        1f,
                        drawShadow: false);
                    drewNativeFrame = true;
                }
            }
            catch { }
            if (!drewNativeFrame)
            {
                PixelUi.DrawParchmentBox(sb, pixel, _panelRect, px, alpha);
            }

            int contentTop = _panelRect.Y + _pad;
            int contentBot = _panelRect.Bottom - _pad;
            int slotX = _panelRect.X + _pad;
            int activeIdx = Game1.player.CurrentToolIndex;

            Rectangle normalSlotSrc = default;
            Rectangle selectedSlotSrc = default;
            bool hasMenuTex = false;
            try
            {
                if (Game1.menuTexture != null)
                {
                    normalSlotSrc = Game1.getSourceRectForStandardTileSheet(Game1.menuTexture, 10);
                    selectedSlotSrc = Game1.getSourceRectForStandardTileSheet(Game1.menuTexture, 56);
                    hasMenuTex = true;
                }
            }
            catch { }

            int firstVis = Math.Max(0, (int)Math.Floor(_scrollSlots));
            int visRows = (int)Math.Ceiling(_visibleSlots) + 1;
            float fracOffset = _scrollSlots - firstVis;

            // GPU Scissor Clip strictly inside the wooden inner frame -> silky smooth sub-pixel scrolling with ZERO popping or border bleed!
            var gd = sb.GraphicsDevice;
            Rectangle prevScissor = gd != null ? gd.ScissorRectangle : Rectangle.Empty;
            bool scissored = false;
            if (gd != null)
            {
                Rectangle vpBounds = gd.Viewport.Bounds;
                Rectangle rawClip = new Rectangle(slotX - 2, contentTop, _slotSize + 4, Math.Max(1, contentBot - contentTop));
                Rectangle clip = Rectangle.Intersect(vpBounds, rawClip);
                if (clip.Width > 0 && clip.Height > 0)
                {
                    try
                    {
                        sb.End();
                        gd.ScissorRectangle = clip;
                        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, ScissorRasterizer);
                        scissored = true;
                    }
                    catch { scissored = false; }
                }
            }

            try
            {
                for (int row = 0; row <= visRows; row++)
                {
                    int slotIdx = firstVis + row;
                    if (slotIdx < 0 || slotIdx >= maxItems)
                        continue;

                    int sy = contentTop + (int)Math.Round((row - fracOffset) * _slotSize);
                    if (sy + _slotSize <= contentTop || sy >= contentBot)
                        continue;

                    var slotR = new Rectangle(slotX, sy, _slotSize, _slotSize);
                    bool isSel = (slotIdx == activeIdx);

                    if (hasMenuTex)
                    {
                        // Official Stardew Mobile slot texture: tile 56 (crisp red-gold border) for selected, tile 10 for normal
                        sb.Draw(Game1.menuTexture, slotR, isSel ? selectedSlotSrc : normalSlotSrc, Color.White * alpha);
                    }
                    else
                    {
                        sb.Draw(pixel, slotR, (isSel ? new Color(255, 226, 148) : new Color(224, 188, 128)) * alpha);
                    }

                    Item item = (slotIdx < Game1.player.Items.Count) ? Game1.player.Items[slotIdx] : null;
                    if (item != null)
                    {
                        float scale = (_slotSize * 0.84f) / 64f;
                        Vector2 iconPos = new Vector2(
                            slotR.X + (_slotSize - 64f * scale) * 0.5f,
                            slotR.Y + (_slotSize - 64f * scale) * 0.5f);
                        try
                        {
                            item.drawInMenu(sb, iconPos, scale, itemAlpha, 0.88f, StackDrawType.Draw, Color.White, drawShadow: true);
                        }
                        catch { }
                    }
                }
            }
            finally
            {
                if (scissored && gd != null)
                {
                    try
                    {
                        sb.End();
                        gd.ScissorRectangle = prevScissor;
                        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
                    }
                    catch { }
                }
            }

            // Slim Stardew wooden scrollbar along the inner right edge when scrollable
            if (maxItems > _visibleSlots)
            {
                int trackW = Math.Max(4, _pad / 2 - 1);
                int trackX = _panelRect.Right - _pad - trackW + 3;
                int trackH = Math.Max(24, contentBot - contentTop);
                sb.Draw(pixel, new Rectangle(trackX, contentTop, trackW, trackH), new Color(102, 54, 22) * (0.28f * alpha));

                float frac = _scrollSlots / Math.Max(0.01f, maxItems - _visibleSlots);
                int thumbH = Math.Max(20, (int)(trackH * Math.Clamp(_visibleSlots / maxItems, 0.14f, 0.95f)));
                int thumbY = contentTop + (int)Math.Round((trackH - thumbH) * frac);
                var thumbR = new Rectangle(trackX, thumbY, trackW, thumbH);
                sb.Draw(pixel, thumbR, new Color(156, 86, 36) * (0.90f * alpha));
                sb.Draw(pixel, new Rectangle(thumbR.X, thumbR.Y, thumbR.Width, Math.Max(2, px)), new Color(235, 192, 120) * (0.75f * alpha));
            }
        }
    }
}
