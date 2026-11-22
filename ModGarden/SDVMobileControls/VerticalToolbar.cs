using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;
using StardewValley;
using StardewValley.Menus;

namespace SDVMobileControls
{
    /// <summary>
    /// Authentic Stardew Valley Mobile-style Vertical Scrollable Toolbar (all 12 / 24 / 36 slots).
    /// Supports 1-3 columns, customizable visible items per column (Auto full-height or 4-12 rows/col),
    /// live slot size & opacity, Left or Right edge docking, GPU Scissor clipping, and momentum scrolling.
    /// </summary>
    internal sealed class VerticalToolbar
    {
        private static readonly Rectangle ToolbarFrameSrc = new Rectangle(0, 256, 60, 60);
        private static readonly RasterizerState ScissorRasterizer = new RasterizerState
        {
            CullMode = CullMode.CullCounterClockwiseFace,
            ScissorTestEnable = true
        };

        private IClickableMenu _hiddenPcToolbar;
        private Rectangle _panelRect;
        private int _slotSize = 72;
        private int _slotGap = 0;
        private int _cols = 1;
        private int _totalRows = 12;
        private float _visibleRows = 9f;
        private int _pad = 12;
        private float _frameScale = 1f;
        private float _scrollRows;
        private float _scrollVel;

        private int _touchId = -1;
        private float _touchStartY;
        private float _touchLastY;
        private bool _touchDragged;
        private int _tappedSlot = -1;

        public Rectangle Bounds => _panelRect;
        public bool IsTouchActive => _touchId >= 0;
        public int Columns => _cols;
        public float VisibleRows => _visibleRows;
        public int SlotSize => _slotSize;

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
        /// Computes dynamic multi-column vertical toolbar layout with exact visible rows/col, size scale, and side docking.
        /// </summary>
        public void ComputeLayout(
            int vpW, int vpH,
            int safeLeftPx, int safeRightPx,
            float sizeScale,
            int columns,
            int visibleRowsSetting,
            int side,
            float normY)
        {
            int maxItems = Math.Max(12, Game1.player?.MaxItems ?? 12);
            _cols = Math.Clamp(columns, 1, 3);
            _totalRows = (maxItems + _cols - 1) / _cols;
            float scale = Math.Clamp(sizeScale, 0.55f, 1.50f);

            int vMargin = Math.Max(4, vpH / 160);
            int maxPanelH = Math.Max(160, vpH - vMargin * 2);

            float baseSlot = Math.Clamp(vpH / 9.2f, 44f, 128f);

            // First compute desired slot size, then derive proportional wooden 9-slice frame scale & padding
            // so Game1.menuTexture's 16px*frameScale border NEVER overlaps item slots or scrollbar!
            int panelH;
            if (visibleRowsSetting <= 0)
            {
                // Auto Full-Height mode: span full vertical height (or hug totalRows if fewer rows exist, e.g. 3 cols x 4 rows)
                _slotSize = (int)Math.Clamp(baseSlot * scale, 34f, vpH / 4.2f);
                _frameScale = Math.Clamp(_slotSize / 64f, 0.72f, 1.05f);
                _pad = Math.Max(11, (int)Math.Round(16f * _frameScale));
                _slotGap = _cols > 1 ? Math.Max(2, _slotSize / 26) : 0;
                int allRowsH = _totalRows * _slotSize + Math.Max(0, _totalRows - 1) * _slotGap + _pad * 2;
                panelH = Math.Min(maxPanelH, Math.Max(_slotSize * 2 + _pad * 2, allRowsH));
            }
            else
            {
                // Explicit Visible Items per Column mode (4..12): fits exact row count & scales cleanly with ToolbarSize!
                int targetRows = Math.Clamp(visibleRowsSetting, 4, 12);
                int effRows = Math.Min(targetRows, _totalRows);
                int roughGap = _cols > 1 ? 3 : 0;
                int desiredSlot = (int)Math.Round(baseSlot * scale);
                float estFrameScale = Math.Clamp(desiredSlot / 64f, 0.72f, 1.05f);
                int estPad = Math.Max(11, (int)Math.Round(16f * estFrameScale));
                int maxFitSlot = Math.Max(28, (maxPanelH - estPad * 2 - Math.Max(0, effRows - 1) * roughGap) / Math.Max(1, effRows));
                _slotSize = Math.Clamp(desiredSlot, 28, maxFitSlot);
                _frameScale = Math.Clamp(_slotSize / 64f, 0.72f, 1.05f);
                _pad = Math.Max(11, (int)Math.Round(16f * _frameScale));
                _slotGap = _cols > 1 ? Math.Max(2, _slotSize / 26) : 0;
                int desiredPanelH = effRows * _slotSize + Math.Max(0, effRows - 1) * _slotGap + _pad * 2;
                panelH = Math.Min(maxPanelH, desiredPanelH);
            }

            float rowPitch = Math.Max(1f, _slotSize + _slotGap);
            int innerH = Math.Max(_slotSize, panelH - _pad * 2);
            _visibleRows = Math.Max(1f, (innerH + _slotGap) / rowPitch);

            bool scrollable = _totalRows > _visibleRows + 0.05f;
            int scrollbarW = scrollable ? Math.Max(6, (int)Math.Round(8f * _frameScale)) : 0;
            int scrollbarGap = scrollbarW > 0 ? Math.Max(4, (int)Math.Round(5f * _frameScale)) : 0;
            int gridW = _cols * _slotSize + Math.Max(0, _cols - 1) * _slotGap;
            int panelW = gridW + _pad * 2 + scrollbarW + scrollbarGap;

            int panelY = (panelH >= maxPanelH - 4)
                ? vMargin
                : vMargin + (int)Math.Round((maxPanelH - panelH) * Math.Clamp(normY, 0f, 1f));

            int x;
            if (side == 1)
            {
                int rightMargin = Math.Max(safeRightPx + Math.Max(4, vpW / 240), Math.Max(4, vpW / 220));
                x = Math.Max(4, vpW - rightMargin - panelW);
            }
            else
            {
                x = Math.Max(safeLeftPx + Math.Max(4, vpW / 240), Math.Max(4, vpW / 220));
            }

            _panelRect = new Rectangle(x, panelY, panelW, panelH);

            float maxScroll = Math.Max(0f, _totalRows - _visibleRows);
            _scrollRows = Math.Clamp(_scrollRows, 0f, maxScroll);
        }

        public void UpdateInertia()
        {
            if (_touchId >= 0 || Math.Abs(_scrollVel) < 0.004f)
                return;
            float maxScroll = Math.Max(0f, _totalRows - _visibleRows);
            _scrollRows = Math.Clamp(_scrollRows + _scrollVel, 0f, maxScroll);
            _scrollVel *= 0.87f;
            if (_scrollRows <= 0f || _scrollRows >= maxScroll)
                _scrollVel = 0f;
        }

        public bool HandleTouch(TouchLocation tl, Vector2 vp, out bool selectedItem)
        {
            selectedItem = false;
            int maxItems = Math.Max(12, Game1.player?.MaxItems ?? 12);
            float maxScroll = Math.Max(0f, _totalRows - _visibleRows);
            float rowPitch = Math.Max(1f, _slotSize + _slotGap);
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
                        float deltaRows = -dy / Math.Max(20f, rowPitch);
                        _scrollRows = Math.Clamp(_scrollRows + deltaRows, 0f, maxScroll);
                        _scrollVel = deltaRows;
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

                int gridW = _cols * _slotSize + Math.Max(0, _cols - 1) * _slotGap;
                float contentLeft = _panelRect.X + _pad;
                float contentTop = _panelRect.Y + _pad;
                float contentBot = _panelRect.Bottom - _pad;
                if (vp.X >= contentLeft && vp.X <= contentLeft + gridW && vp.Y >= contentTop && vp.Y <= contentBot)
                {
                    float relX = vp.X - contentLeft;
                    float relY = vp.Y - contentTop;
                    int col = Math.Clamp((int)Math.Floor(relX / rowPitch), 0, _cols - 1);
                    int row = (int)Math.Floor(_scrollRows + relY / rowPitch);
                    int idx = row * _cols + col;
                    _tappedSlot = (row >= 0 && idx >= 0 && idx < maxItems) ? idx : -1;
                }
                else
                {
                    // Tapping wooden border frame or scrollbar strip still allows drag-scrolling without selecting clipped slots
                    _tappedSlot = -1;
                }
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
            int rowPitch = _slotSize + _slotGap;

            // 1. Official Stardew Valley Mobile wooden 9-slice toolbar frame (Game1.menuTexture — auto-skinned by UI mods like Cat Valley!)
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
                        _frameScale,
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
            int slotStartX = _panelRect.X + _pad;
            int gridW = _cols * _slotSize + Math.Max(0, _cols - 1) * _slotGap;
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

            int firstVisRow = Math.Max(0, (int)Math.Floor(_scrollRows));
            int visRowCount = (int)Math.Ceiling(_visibleRows) + 1;
            float fracOffset = _scrollRows - firstVisRow;

            // GPU Scissor Clip + native 64x64 Stardew Matrix transform:
            // CRITICAL: Scale X and Y ONLY (Z = 1.0f)! Scaling Z pushes Item.drawInMenu's layerDepth (0.88f..0.8801f)
            // past 1.0f whenever _slotSize > 72px, causing GPU orthographic frustum clipping on items & WateringCan bars!
            float slotScale = Math.Max(0.25f, _slotSize / 64f);
            float pitchLocal = 64f + (_slotGap / slotScale);
            Matrix slotMatrix = Matrix.CreateScale(slotScale, slotScale, 1f) * Matrix.CreateTranslation(slotStartX, contentTop, 0f);

            var gd = sb.GraphicsDevice;
            Rectangle prevScissor = gd != null ? gd.ScissorRectangle : Rectangle.Empty;
            bool endedOrig = false;
            bool customBatch = false;
            if (gd != null)
            {
                Rectangle vpBounds = gd.Viewport.Bounds;
                Rectangle rawClip = new Rectangle(vpBounds.X + slotStartX - 2, vpBounds.Y + contentTop, gridW + 4, Math.Max(1, contentBot - contentTop));
                Rectangle clip = Rectangle.Intersect(vpBounds, rawClip);
                if (gd.RenderTargetCount == 0 && gd.Viewport.Width > 0 && gd.Viewport.Height > 0)
                {
                    int bbW = gd.PresentationParameters.BackBufferWidth;
                    int bbH = gd.PresentationParameters.BackBufferHeight;
                    if (bbW > 0 && bbH > 0 && (bbW != gd.Viewport.Width || bbH != gd.Viewport.Height))
                    {
                        float sx = bbW / (float)gd.Viewport.Width;
                        float sy = bbH / (float)gd.Viewport.Height;
                        clip = Rectangle.Intersect(
                            new Rectangle(0, 0, bbW, bbH),
                            new Rectangle(
                                (int)Math.Floor(clip.X * sx),
                                (int)Math.Floor(clip.Y * sy),
                                Math.Max(1, (int)Math.Ceiling(clip.Width * sx)),
                                Math.Max(1, (int)Math.Ceiling(clip.Height * sy))));
                    }
                }
                if (clip.Width > 0 && clip.Height > 0)
                {
                    try
                    {
                        sb.End();
                        endedOrig = true;
                        gd.ScissorRectangle = clip;
                        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, ScissorRasterizer, null, slotMatrix);
                        customBatch = true;
                    }
                    catch
                    {
                        customBatch = false;
                        if (endedOrig)
                        {
                            try
                            {
                                gd.ScissorRectangle = prevScissor;
                                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
                                endedOrig = false;
                            }
                            catch { }
                        }
                    }
                }
            }

            try
            {
                for (int r = 0; r <= visRowCount; r++)
                {
                    int rowIdx = firstVisRow + r;
                    if (rowIdx < 0 || rowIdx >= _totalRows)
                        continue;

                    int syScreen = contentTop + (int)Math.Round((r - fracOffset) * rowPitch);
                    if (syScreen + _slotSize <= contentTop || syScreen >= contentBot)
                        continue;

                    for (int c = 0; c < _cols; c++)
                    {
                        int slotIdx = rowIdx * _cols + c;
                        if (slotIdx < 0 || slotIdx >= maxItems)
                            continue;

                        bool isSel = (slotIdx == activeIdx);
                        Item item = (slotIdx < Game1.player.Items.Count) ? Game1.player.Items[slotIdx] : null;

                        if (customBatch)
                        {
                            int rx = (int)Math.Round(c * pitchLocal);
                            int ry = (int)Math.Round((r - fracOffset) * pitchLocal);
                            var localR = new Rectangle(rx, ry, 64, 64);

                            if (hasMenuTex)
                                sb.Draw(Game1.menuTexture, localR, isSel ? selectedSlotSrc : normalSlotSrc, Color.White * alpha);
                            else
                                sb.Draw(pixel, localR, (isSel ? new Color(255, 226, 148) : new Color(224, 188, 128)) * alpha);

                            if (item != null)
                            {
                                try
                                {
                                    // Exact Stardew Toolbar.draw convention: 64x64 box snapped to (rx, ry), 0.90f selected / 0.80f normal!
                                    item.drawInMenu(sb, new Vector2(rx, ry), isSel ? 0.90f : 0.80f, itemAlpha, 0.88f, StackDrawType.Draw, Color.White, drawShadow: true);
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            int sx = slotStartX + c * rowPitch;
                            var slotR = new Rectangle(sx, syScreen, _slotSize, _slotSize);
                            if (hasMenuTex)
                                sb.Draw(Game1.menuTexture, slotR, isSel ? selectedSlotSrc : normalSlotSrc, Color.White * alpha);
                            else
                                sb.Draw(pixel, slotR, (isSel ? new Color(255, 226, 148) : new Color(224, 188, 128)) * alpha);

                            if (item != null)
                            {
                                // Fallback without transform matrix: Item.drawInMenu centers at location + (32, 32)!
                                float scale = (_slotSize * (isSel ? 0.90f : 0.80f)) / 64f;
                                Vector2 iconPos = new Vector2(slotR.Center.X - 32f, slotR.Center.Y - 32f);
                                try
                                {
                                    item.drawInMenu(sb, iconPos, scale, itemAlpha, 0.88f, StackDrawType.Draw, Color.White, drawShadow: true);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            finally
            {
                if ((customBatch || endedOrig) && gd != null)
                {
                    try
                    {
                        if (customBatch) sb.End();
                        gd.ScissorRectangle = prevScissor;
                        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
                    }
                    catch { }
                }
            }

            // Slim native Stardew scrollbar inside the inner right margin (never overlapping the wooden border!)
            if (_totalRows > _visibleRows + 0.05f)
            {
                int trackW = Math.Max(6, (int)Math.Round(8f * _frameScale));
                int trackX = _panelRect.Right - _pad - trackW;
                int trackH = Math.Max(24, contentBot - contentTop);
                float frac = _scrollRows / Math.Max(0.01f, _totalRows - _visibleRows);
                int thumbH = Math.Max(20, (int)(trackH * Math.Clamp(_visibleRows / _totalRows, 0.14f, 0.95f)));
                int thumbY = contentTop + (int)Math.Round((trackH - thumbH) * frac);
                var trackR = new Rectangle(trackX, contentTop, trackW, trackH);
                var thumbR = new Rectangle(trackX, thumbY, trackW, thumbH);

                bool drewNativeScroll = false;
                try
                {
                    if (Game1.mouseCursors != null)
                    {
                        IClickableMenu.drawTextureBox(sb, Game1.mouseCursors, new Rectangle(403, 383, 6, 6),
                            trackR.X, trackR.Y, trackR.Width, trackR.Height, Color.White * (0.78f * alpha), 1.5f, drawShadow: false);
                        sb.Draw(Game1.mouseCursors, thumbR, new Rectangle(435, 463, 6, 10), Color.White * (0.94f * alpha));
                        drewNativeScroll = true;
                    }
                }
                catch { }

                if (!drewNativeScroll)
                {
                    sb.Draw(pixel, trackR, new Color(102, 54, 22) * (0.28f * alpha));
                    sb.Draw(pixel, thumbR, new Color(156, 86, 36) * (0.90f * alpha));
                    sb.Draw(pixel, new Rectangle(thumbR.X, thumbR.Y, thumbR.Width, Math.Max(2, px)), new Color(235, 192, 120) * (0.75f * alpha));
                }
            }
        }
    }
}
