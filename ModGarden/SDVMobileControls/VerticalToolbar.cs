using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;
using StardewValley;
using StardewValley.Menus;

namespace SDVMobileControls
{
    /// <summary>
    /// Optional Stardew Valley Mobile-style Left-Edge Vertical Scrollable Toolbar (all 12 / 24 / 36 slots).
    /// Cleanly hides the horizontal PC Toolbar while active (restores immediately when disabled)
    /// and selects any Backpack slot directly via Farmer.CurrentToolIndex without shifting rows.
    /// </summary>
    internal sealed class VerticalToolbar
    {
        private IClickableMenu _hiddenPcToolbar;
        private Rectangle _panelRect;
        private int _slotSize = 54;
        private int _visibleCount = 7;
        private int _pad = 6;
        private float _scrollSlots;

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

        public void ComputeLayout(int vpW, int vpH, int safeLeftPx, int topStartPx)
        {
            int maxItems = Math.Max(12, Game1.player?.MaxItems ?? 12);
            int topY = Math.Max(topStartPx, (int)(vpH * 0.115f));
            int botMargin = Math.Max(14, (int)(vpH * 0.03f));
            int availH = Math.Max(200, vpH - topY - botMargin);

            _visibleCount = Math.Min(maxItems, vpH < 520 ? 6 : 7);
            _pad = Math.Max(5, vpH / 120);
            _slotSize = Math.Clamp((availH - _pad * 2) / _visibleCount, 38, Math.Max(46, vpH / 9));

            int panelH = _slotSize * _visibleCount + _pad * 2;
            int scrollbarW = maxItems > _visibleCount ? Math.Max(5, _pad) : 0;
            int panelW = _slotSize + _pad * 2 + scrollbarW;
            int x = Math.Max(safeLeftPx + Math.Max(6, vpW / 120), Math.Max(8, vpW / 100));
            int y = topY + Math.Max(0, (availH - panelH) / 2);

            _panelRect = new Rectangle(x, y, panelW, panelH);

            float maxScroll = Math.Max(0f, maxItems - _visibleCount);
            _scrollSlots = Math.Clamp(_scrollSlots, 0f, maxScroll);
        }

        public bool HandleTouch(TouchLocation tl, Vector2 vp, out bool selectedItem)
        {
            selectedItem = false;
            int maxItems = Math.Max(12, Game1.player?.MaxItems ?? 12);
            float maxScroll = Math.Max(0f, maxItems - _visibleCount);
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
                        _scrollSlots = Math.Clamp(_scrollSlots - dy / Math.Max(20f, _slotSize), 0f, maxScroll);
                    }
                    return true;
                }

                // Released
                if (!_touchDragged && _tappedSlot >= 0 && _tappedSlot < maxItems && Game1.player != null)
                {
                    Game1.player.CurrentToolIndex = _tappedSlot;
                    selectedItem = true;
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
                float relY = vp.Y - (_panelRect.Y + _pad);
                int idx = (int)Math.Floor(_scrollSlots + relY / Math.Max(1f, _slotSize));
                _tappedSlot = (idx >= 0 && idx < maxItems) ? idx : -1;
                return true;
            }

            return false;
        }

        public void Draw(SpriteBatch sb, Texture2D pixel, int vpH)
        {
            if (sb == null || pixel == null || Game1.player == null || _panelRect.Width <= 0)
                return;

            int maxItems = Math.Max(12, Game1.player.MaxItems);
            int px = PixelUi.Step(vpH);

            // Subtle translucent Stardew pixel parchment frame (low visual distraction!)
            PixelUi.DrawParchmentBox(sb, pixel, _panelRect, px, 0.86f);

            int firstVis = Math.Max(0, (int)Math.Floor(_scrollSlots));
            float fracOffset = _scrollSlots - firstVis;
            int contentTop = _panelRect.Y + _pad;
            int contentBot = _panelRect.Bottom - _pad;
            int slotX = _panelRect.X + _pad;
            int activeIdx = Game1.player.CurrentToolIndex;

            Rectangle slotTexSrc = default;
            bool hasMenuTex = false;
            try
            {
                if (Game1.menuTexture != null)
                {
                    slotTexSrc = Game1.getSourceRectForStandardTileSheet(Game1.menuTexture, 10);
                    hasMenuTex = true;
                }
            }
            catch { }

            for (int row = 0; row <= _visibleCount; row++)
            {
                int slotIdx = firstVis + row;
                if (slotIdx < 0 || slotIdx >= maxItems)
                    continue;

                int sy = contentTop + (int)Math.Round((row - fracOffset) * _slotSize);
                if (sy + _slotSize <= contentTop + 4 || sy >= contentBot - 4)
                    continue;

                // Clip slots cleanly inside vertical panel frame
                int drawTop = Math.Max(contentTop, sy);
                int drawBot = Math.Min(contentBot, sy + _slotSize);
                if (drawBot - drawTop < _slotSize * 0.55f)
                    continue;

                var slotR = new Rectangle(slotX, sy, _slotSize, _slotSize);
                bool isSel = (slotIdx == activeIdx);

                if (hasMenuTex)
                {
                    sb.Draw(Game1.menuTexture, slotR, slotTexSrc, Color.White * (isSel ? 0.96f : 0.78f));
                }
                else
                {
                    sb.Draw(pixel, slotR, (isSel ? new Color(255, 226, 148) : new Color(224, 188, 128)) * 0.84f);
                }

                // Active item crisp Stardew red/gold pixel selector border
                if (isSel)
                {
                    int bt = Math.Max(2, px);
                    Color selCol = new Color(214, 62, 34);
                    sb.Draw(pixel, new Rectangle(slotR.X, slotR.Y, slotR.Width, bt), selCol);
                    sb.Draw(pixel, new Rectangle(slotR.X, slotR.Bottom - bt, slotR.Width, bt), selCol);
                    sb.Draw(pixel, new Rectangle(slotR.X, slotR.Y, bt, slotR.Height), selCol);
                    sb.Draw(pixel, new Rectangle(slotR.Right - bt, slotR.Y, bt, slotR.Height), selCol);
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
                        item.drawInMenu(sb, iconPos, scale, isSel ? 1.0f : 0.90f, 0.88f, StackDrawType.Draw, Color.White, drawShadow: true);
                    }
                    catch { }
                }
            }

            // Thin pixel scrollbar indicator when player has 24 or 36 slots
            if (maxItems > _visibleCount)
            {
                int trackX = _panelRect.Right - _pad - Math.Max(3, px);
                int trackW = Math.Max(3, px);
                int trackH = Math.Max(20, contentBot - contentTop);
                sb.Draw(pixel, new Rectangle(trackX, contentTop, trackW, trackH), new Color(115, 65, 29) * 0.35f);

                float frac = _scrollSlots / Math.Max(1f, maxItems - _visibleCount);
                int thumbH = Math.Max(16, (int)(trackH * ((float)_visibleCount / maxItems)));
                int thumbY = contentTop + (int)Math.Round((trackH - thumbH) * frac);
                sb.Draw(pixel, new Rectangle(trackX, thumbY, trackW, thumbH), new Color(120, 62, 24) * 0.85f);
            }
        }
    }
}
