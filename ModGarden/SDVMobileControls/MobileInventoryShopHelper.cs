using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;

namespace SDVMobileControls
{
    /// <summary>
    /// Authentic Stardew Mobile Texture UI for Shopping, Crafting (Cooking & Workbench),
    /// Dresser 1-Tap, Chest 1-Tap Transfer, Long-Press Split Stack, and 1-Finger Drag-and-Drop
    /// with ZERO stuck mouse cursor. Zero hardcoded resolutions.
    /// </summary>
    internal sealed class MobileInventoryShopHelper
    {
        private static readonly Rectangle MobileBoxSrc = new Rectangle(0, 256, 60, 60);
        private static readonly MethodInfo ShopTryPurchaseMethod =
            typeof(ShopMenu).GetMethod("tryToPurchaseItem", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo ShopIsStorageField =
            typeof(ShopMenu).GetField("_isStorageShop", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo CraftClickRecipeMethod =
            typeof(CraftingPage).GetMethod("clickCraftingRecipe", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private enum PopupKind { None = 0, ShopBuy = 1, ShopSell = 2, ChestSplit = 3, BagSplit = 4, Craft = 5 }

        // Active Popup state
        private PopupKind _popupKind = PopupKind.None;
        private IClickableMenu _popupOwnerMenu;
        private ISalable _popupSaleItem;
        private Item _popupItem;
        private CraftingPage _popupCraftPage;
        private ClickableTextureComponent _popupCraftBtn;
        private CraftingRecipe _popupCraftRecipe;
        private InventoryMenu _popupSrcInv;
        private InventoryMenu _popupDstInv;
        private int _popupSrcSlot = -1;
        private Point _popupUiClickPt;
        private int _popupQty = 1;
        private int _popupMinQty = 1;
        private int _popupMaxQty = 1;
        private int _popupUnitPrice;
        private int _popupCurrency;
        private int _popupStock = ShopMenu.infiniteStock;
        private string _popupTradeItemId;
        private int _popupTradeCount;
        private bool _popupCanAfford = true;

        // Popup interactive rects (viewport pixel space)
        private Rectangle _popPanel;
        private Rectangle _popCloseX;
        private Rectangle _popSliderTrack;
        private Rectangle _popBtnM10;
        private Rectangle _popBtnM1;
        private Rectangle _popBtnP1;
        private Rectangle _popBtnP10;
        private Rectangle _popBtnMax;
        private Rectangle _popBtnConfirm;
        private Rectangle _popBtnCancel;
        private int _popSliderTouchId = -1;
        private int _popConsumeTouchId = -1;
        private int _popHoldTouchId = -1;
        private int _popHoldDelta;
        private double _popHoldStartSec;
        private double _popHoldLastStepSec;

        // Touch Drag-and-Drop & Short-Tap / Long-Press state
        private int _touchId = -1;
        private Vector2 _touchStartVp;
        private Vector2 _touchCurVp;
        private double _touchStartSec;
        private bool _isDragging;
        private bool _isShopScrolling;
        private float _shopScrollLastY;
        private float _shopScrollAccum;
        private IClickableMenu _trackedMenu;

        // Dragged & Tap-Selected slot info (NEVER uses Game1.player.CursorSlotItem!)
        private InventoryMenu _dragSrcInv;
        private int _dragSrcSlot = -1;
        private Item _dragItem;
        private int _tappedShopBtnIdx = -1;
        private int _selectedBagSlot = -1;
        private InventoryMenu _selectedBagInv;

        public bool IsPopupOpen => _popupKind != PopupKind.None;
        public bool IsDraggingItem => _isDragging && _dragItem != null;
        public bool HasSelectedSlot => _selectedBagSlot >= 0;

        public void Reset()
        {
            ClosePopup();
            ResetTouch();
            ClearSelection();
        }

        private void ClearSelection()
        {
            _selectedBagSlot = -1;
            _selectedBagInv = null;
        }

        private void ClosePopup()
        {
            _popupKind = PopupKind.None;
            _popupOwnerMenu = null;
            _popupSaleItem = null;
            _popupItem = null;
            _popupCraftPage = null;
            _popupCraftBtn = null;
            _popupCraftRecipe = null;
            _popupSrcInv = null;
            _popupDstInv = null;
            _popupSrcSlot = -1;
            _popupStock = ShopMenu.infiniteStock;
            _popSliderTouchId = -1;
            _popHoldTouchId = -1;
            _popHoldDelta = 0;
        }

        private static bool IsStorageShop(ShopMenu shop)
        {
            if (shop == null) return false;
            try
            {
                if (ShopIsStorageField != null && ShopIsStorageField.GetValue(shop) is bool b && b)
                    return true;
            }
            catch { }
            return false;
        }

        public bool OpenCraftingPopup(IClickableMenu ownerMenu, CraftingPage cp, ClickableTextureComponent recipeBtn, CraftingRecipe recipe, int consumeTouchId)
        {
            if (ownerMenu == null || cp == null || recipeBtn == null || recipe == null)
                return false;

            Item preview = null;
            try { preview = recipe.createItem(); } catch { }

            int craftable = 0;
            IList<Item> matItems = GetContainerItemsList(cp._materialContainers);
            try { craftable = recipe.getCraftableCount(matItems); } catch { craftable = 0; }
            if (craftable <= 0)
            {
                try
                {
                    if (recipe.doesFarmerHaveIngredientsInInventory(matItems))
                        craftable = 1;
                }
                catch { }
            }

            _popupKind = PopupKind.Craft;
            _popupOwnerMenu = ownerMenu;
            _popupCraftPage = cp;
            _popupCraftBtn = recipeBtn;
            _popupCraftRecipe = recipe;
            _popupItem = preview;
            _popupSaleItem = preview;
            _popupMinQty = 1;
            _popupMaxQty = Math.Clamp(Math.Max(1, craftable), 1, 999);
            _popupQty = 1;
            _popupUnitPrice = 0;
            _popupTradeItemId = null;
            _popupTradeCount = 0;
            _popupCanAfford = craftable >= 1;
            _popConsumeTouchId = consumeTouchId;
            try { Game1.playSound("smallSelect"); } catch { }
            return true;
        }

        private static int CountMatchingCraftIngredients(string rawId, IList<Item> extraItems)
        {
            if (string.IsNullOrEmpty(rawId)) return 0;
            int total = 0;
            try
            {
                var pItems = Game1.player?.Items;
                if (pItems != null)
                {
                    for (int i = 0; i < pItems.Count; i++)
                    {
                        Item it = pItems[i];
                        if (it != null && CraftingRecipe.ItemMatchesForCrafting(it, rawId))
                            total += it.Stack;
                    }
                }
                if (extraItems != null)
                {
                    for (int i = 0; i < extraItems.Count; i++)
                    {
                        Item it = extraItems[i];
                        if (it != null && CraftingRecipe.ItemMatchesForCrafting(it, rawId))
                            total += it.Stack;
                    }
                }
            }
            catch { }
            return total;
        }

        private static IList<Item> GetContainerItemsList(List<IInventory> containers)
        {
            if (containers == null || containers.Count == 0)
                return null;
            var all = new List<Item>();
            for (int i = 0; i < containers.Count; i++)
            {
                var inv = containers[i];
                if (inv == null) continue;
                for (int j = 0; j < inv.Count; j++)
                {
                    if (inv[j] != null) all.Add(inv[j]);
                }
            }
            return all;
        }

        private void ResetTouch()
        {
            _touchId = -1;
            _isDragging = false;
            _isShopScrolling = false;
            _dragSrcInv = null;
            _dragSrcSlot = -1;
            _dragItem = null;
            _tappedShopBtnIdx = -1;
        }

        public bool Update(
            IClickableMenu activeMenu,
            TouchCollection touches,
            Func<Vector2, Vector2> rawToVp,
            Func<Vector2, Point> vpToUi,
            int vpW, int vpH,
            double nowSec)
        {
            if (activeMenu == null || (IsPopupOpen && activeMenu != _popupOwnerMenu) ||
                (_popupKind == PopupKind.Craft && activeMenu is GameMenu gmCraft && !ReferenceEquals(gmCraft.GetCurrentPage(), _popupCraftPage)))
            {
                Reset();
                return false;
            }

            if (activeMenu != _trackedMenu)
            {
                _trackedMenu = activeMenu;
                ResetTouch();
                ClearSelection();
            }

            // Consume lingering touch release after popup dismiss/confirm
            if (_popConsumeTouchId >= 0)
            {
                bool stillDown = false;
                for (int i = 0; i < touches.Count; i++)
                {
                    if (touches[i].Id == _popConsumeTouchId &&
                        (touches[i].State == TouchLocationState.Pressed || touches[i].State == TouchLocationState.Moved))
                    {
                        stillDown = true;
                        break;
                    }
                }
                if (stillDown) return true;
                _popConsumeTouchId = -1;
            }

            // 1. Active Mobile Pixel Popup owns all touches cleanly
            if (IsPopupOpen)
            {
                ComputePopupLayout(vpW, vpH);
                UpdatePopupTouches(touches, rawToVp, nowSec);
                return true;
            }

            ShopMenu shop = activeMenu as ShopMenu;
            ItemGrabMenu grab = activeMenu as ItemGrabMenu;
            InventoryPage invPage = (activeMenu as GameMenu)?.GetCurrentPage() as InventoryPage;

            if (shop == null && grab == null && invPage == null)
            {
                ResetTouch();
                ClearSelection();
                return false;
            }

            if (touches.Count == 0)
            {
                ResetTouch();
                return false;
            }
            if (touches.Count > 1)
            {
                ResetTouch();
                return false;
            }

            TouchLocation tl = touches[0];
            Vector2 vp = rawToVp(tl.Position);
            Point uiPt = vpToUi(vp);

            if (shop != null)
                return UpdateShopTouch(shop, tl, vp, uiPt, vpH);

            if (grab != null)
                return UpdateInventoryTouch(grab, grab.inventory, grab.ItemsToGrabMenu, grab.trashCan, null, isChestMenu: true, tl, vp, uiPt, nowSec);

            if (invPage != null)
                return UpdateInventoryTouch(invPage, invPage.inventory, null, invPage.trashCan, invPage.equipmentIcons, isChestMenu: false, tl, vp, uiPt, nowSec);

            return false;
        }

        // =========================================================================
        // A. MOBILE SHOPPING (Pixel Quantity Slider -> Direct into Backpack!)
        // =========================================================================
        private bool UpdateShopTouch(ShopMenu shop, TouchLocation tl, Vector2 vp, Point uiPt, int vpH)
        {
            if (tl.State == TouchLocationState.Pressed)
            {
                ResetTouch();

                if (shop.forSaleButtons != null)
                {
                    for (int i = 0; i < shop.forSaleButtons.Count; i++)
                    {
                        if (shop.forSaleButtons[i].containsPoint(uiPt.X, uiPt.Y))
                        {
                            int saleIdx = shop.currentItemIndex + i;
                            if (saleIdx >= 0 && saleIdx < shop.forSale.Count)
                            {
                                _touchId = tl.Id;
                                _touchStartVp = vp;
                                _touchCurVp = vp;
                                _tappedShopBtnIdx = i;
                                _shopScrollLastY = vp.Y;
                                _shopScrollAccum = 0f;
                                return true;
                            }
                        }
                    }
                }

                if (!shop.readOnly && shop.inventory != null)
                {
                    int slot = FindSlotAt(shop.inventory, uiPt);
                    if (slot >= 0)
                    {
                        Item it = GetItemAt(shop.inventory, slot);
                        if (it != null && shop.highlightItemToSell(it))
                        {
                            _touchId = tl.Id;
                            _touchStartVp = vp;
                            _touchCurVp = vp;
                            _dragSrcInv = shop.inventory;
                            _dragSrcSlot = slot;
                            _dragItem = it;
                            return true;
                        }
                    }
                }

                return false;
            }

            if (tl.Id != _touchId)
                return false;

            if (tl.State == TouchLocationState.Moved)
            {
                _touchCurVp = vp;
                float dist = Vector2.Distance(vp, _touchStartVp);

                if (_tappedShopBtnIdx >= 0 && dist > 18f)
                {
                    _isShopScrolling = true;
                    float dy = vp.Y - _shopScrollLastY;
                    _shopScrollLastY = vp.Y;
                    _shopScrollAccum += dy;
                    float step = Math.Max(28f, vpH * 0.052f);
                    while (Math.Abs(_shopScrollAccum) >= step)
                    {
                        int wheel = _shopScrollAccum > 0 ? 120 : -120;
                        _shopScrollAccum -= Math.Sign(_shopScrollAccum) * step;
                        try { shop.receiveScrollWheelAction(wheel); } catch { }
                    }
                }
                else
                {
                    _shopScrollLastY = vp.Y;
                }
                return true;
            }

            if (tl.State == TouchLocationState.Released || tl.State == TouchLocationState.Invalid)
            {
                bool wasScrolling = _isShopScrolling;
                int btnIdx = _tappedShopBtnIdx;
                InventoryMenu sellInv = _dragSrcInv;
                int sellSlot = _dragSrcSlot;
                Item sellItem = _dragItem;
                ResetTouch();

                if (!wasScrolling)
                {
                    if (btnIdx >= 0 && shop.forSaleButtons != null && btnIdx < shop.forSaleButtons.Count)
                    {
                        int saleIdx = shop.currentItemIndex + btnIdx;
                        if (saleIdx >= 0 && saleIdx < shop.forSale.Count)
                        {
                            ISalable sale = shop.forSale[saleIdx];
                            Point uiCenter = shop.forSaleButtons[btnIdx].bounds.Center;
                            if (IsStorageShop(shop))
                            {
                                // 1-Tap Dresser withdraw directly into Backpack (zero "Mua 0g" popup!)
                                DirectBuyOneIntoBackpack(shop, sale, uiCenter);
                                return true;
                            }
                            OpenShopBuyPopup(shop, sale, uiCenter);
                            return true;
                        }
                    }
                    else if (sellInv != null && sellSlot >= 0 && sellItem != null)
                    {
                        Point uiCenter = sellInv.inventory[sellSlot].bounds.Center;
                        if (IsStorageShop(shop))
                        {
                            // 1-Tap Dresser store directly from Backpack (zero "Bán 0g" popup!)
                            try { shop.receiveLeftClick(uiCenter.X, uiCenter.Y, true); } catch { }
                            return true;
                        }
                        OpenShopSellPopup(shop, sellInv, sellSlot, sellItem, uiCenter);
                        return true;
                    }
                }
                return true;
            }

            return true;
        }

        private static void DirectBuyOneIntoBackpack(ShopMenu shop, ISalable sale, Point uiCenter)
        {
            if (shop == null || sale == null) return;
            if (shop.heldItem is Item prevHeld)
                shop.heldItem = Game1.player.addItemToInventory(prevHeld);

            if (!sale.IsRecipe && sale is Item checkItem && !Game1.player.couldInventoryAcceptThisItem(checkItem.getOne()))
            {
                try { Game1.playSound("cancel"); } catch { }
                return;
            }

            bool soldOut = false;
            try
            {
                if (ShopTryPurchaseMethod != null)
                    soldOut = (bool)ShopTryPurchaseMethod.Invoke(shop, new object[] { sale, null, 1, uiCenter.X, uiCenter.Y });
            }
            catch { soldOut = false; }

            if (soldOut)
            {
                try
                {
                    shop.itemPriceAndStock?.Remove(sale);
                    shop.forSale?.Remove(sale);
                }
                catch { }
            }
            if (shop.heldItem is Item bought)
                shop.heldItem = Game1.player.addItemToInventory(bought);
            else
                shop.heldItem = null;
        }

        private void OpenShopBuyPopup(ShopMenu shop, ISalable sale, Point uiCenter)
        {
            if (sale == null) return;

            int price = 0;
            int stock = ShopMenu.infiniteStock;
            string tradeId = null;
            int tradeCount = 0;

            if (shop.itemPriceAndStock != null && shop.itemPriceAndStock.TryGetValue(sale, out ItemStockInformation info))
            {
                price = info.Price;
                stock = info.Stock;
                tradeId = info.TradeItem;
                tradeCount = info.TradeItemCount ?? 5;
            }

            int maxByMoney = (price > 0)
                ? ShopMenu.getPlayerCurrencyAmount(Game1.player, shop.currency) / price
                : 999;

            int maxByTrade = (!string.IsNullOrEmpty(tradeId) && tradeCount > 0)
                ? Game1.player.Items.CountId(tradeId) / tradeCount
                : 999;

            int maxByStock = (stock == ShopMenu.infiniteStock) ? 999 : Math.Max(0, stock);
            int maxStack = (sale.IsRecipe || sale.maximumStackSize() <= 1) ? 1 : sale.maximumStackSize();
            int affordable = Math.Min(maxByMoney, maxByTrade);
            int maxBuy = Math.Min(Math.Min(maxByStock, maxStack), Math.Max(1, affordable));
            maxBuy = Math.Clamp(maxBuy, 1, 999);

            _popupKind = PopupKind.ShopBuy;
            _popupOwnerMenu = shop;
            _popupSaleItem = sale;
            _popupItem = sale as Item;
            _popupUiClickPt = uiCenter;
            _popupMinQty = 1;
            _popupMaxQty = maxBuy;
            _popupQty = 1;
            _popupUnitPrice = price;
            _popupCurrency = shop.currency;
            _popupStock = stock;
            _popupTradeItemId = tradeId;
            _popupTradeCount = tradeCount;
            _popupCanAfford = (affordable >= 1 && maxByStock >= 1);

            try { Game1.playSound("smallSelect"); } catch { }
        }

        private void OpenShopSellPopup(ShopMenu shop, InventoryMenu inv, int slot, Item item, Point uiCenter)
        {
            int unitPrice = 0;
            try { unitPrice = Utility.getSellToStorePriceOfItem(item, countStack: false); } catch { }
            if (unitPrice < 0) unitPrice = 0;

            _popupKind = PopupKind.ShopSell;
            _popupOwnerMenu = shop;
            _popupItem = item;
            _popupSaleItem = item;
            _popupSrcInv = inv;
            _popupSrcSlot = slot;
            _popupUiClickPt = uiCenter;
            _popupMinQty = 1;
            _popupMaxQty = Math.Max(1, item.Stack);
            _popupQty = Math.Max(1, item.Stack);
            _popupUnitPrice = unitPrice;
            _popupCurrency = shop.currency;
            _popupCanAfford = true;

            try { Game1.playSound("smallSelect"); } catch { }
        }

        private void ExecutePopupConfirm()
        {
            if (_popupKind == PopupKind.ShopBuy && _popupOwnerMenu is ShopMenu shop && _popupSaleItem != null)
            {
                if (!_popupCanAfford)
                {
                    try { Game1.playSound("cancel"); } catch { }
                    return;
                }

                // Clear any previous held cursor item cleanly into Backpack
                if (shop.heldItem is Item prevHeld)
                {
                    shop.heldItem = Game1.player.addItemToInventory(prevHeld);
                }

                // Do NOT block Clint's Tool Upgrades / special action purchases (which remove the old tool or trigger instant services!) when Backpack is full
                bool isUpgradeOrService = _popupSaleItem.IsRecipe
                    || _popupSaleItem is Tool
                    || shop.onPurchase != null
                    || (!string.IsNullOrEmpty(shop.ShopId) && shop.ShopId.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) >= 0);

                if (!isUpgradeOrService && _popupSaleItem is Item checkItem)
                {
                    Item probe = checkItem.getOne();
                    probe.Stack = _popupQty;
                    if (!Game1.player.couldInventoryAcceptThisItem(probe))
                    {
                        try { Game1.playSound("cancel"); } catch { }
                        ClosePopup();
                        return;
                    }
                }

                // NOTE: ShopMenu.tryToPurchaseItem returns `bool soldOut` (true ONLY when stock reached 0!),
                // NOT `bool purchased`! On normal infinite-stock purchases (e.g. seeds) it sets shop.heldItem
                // and returns false!
                bool soldOut = false;
                try
                {
                    if (ShopTryPurchaseMethod != null)
                    {
                        soldOut = (bool)ShopTryPurchaseMethod.Invoke(shop, new object[] { _popupSaleItem, null, _popupQty, _popupUiClickPt.X, _popupUiClickPt.Y });
                    }
                }
                catch { soldOut = false; }

                if (soldOut)
                {
                    try
                    {
                        shop.itemPriceAndStock?.Remove(_popupSaleItem);
                        shop.forSale?.Remove(_popupSaleItem);
                    }
                    catch { }
                }

                // ALWAYS move purchased item from shop.heldItem STRAIGHT into Backpack!
                if (shop.heldItem is Item bought)
                {
                    Item leftover = Game1.player.addItemToInventory(bought);
                    shop.heldItem = leftover;
                }
                else
                {
                    shop.heldItem = null;
                }
            }
            else if (_popupKind == PopupKind.Craft && _popupCraftPage != null && _popupCraftBtn != null && _popupCraftRecipe != null)
            {
                if (!_popupCanAfford)
                {
                    try { Game1.playSound("cancel"); } catch { }
                    return;
                }

                // Clear any leftover heldItem first
                if (_popupCraftPage.heldItem != null && Game1.player != null)
                {
                    try { _popupCraftPage.heldItem = Game1.player.addItemToInventory(_popupCraftPage.heldItem); } catch { }
                }

                int craftedAny = 0;
                for (int c = 0; c < _popupQty; c++)
                {
                    int avail = 0;
                    try { avail = _popupCraftRecipe.getCraftableCount(GetContainerItemsList(_popupCraftPage._materialContainers)); } catch { avail = 0; }
                    if (avail <= 0)
                        break;

                    // Verify Backpack can accept crafted item before consuming materials
                    try
                    {
                        Item testOut = _popupCraftRecipe.createItem();
                        if (testOut != null && Game1.player != null && !Game1.player.couldInventoryAcceptThisItem(testOut))
                        {
                            if (craftedAny == 0)
                                try { Game1.playSound("cancel"); } catch { }
                            break;
                        }
                    }
                    catch { }

                    try
                    {
                        if (CraftClickRecipeMethod != null)
                            CraftClickRecipeMethod.Invoke(_popupCraftPage, new object[] { _popupCraftBtn, c == 0 });
                    }
                    catch { break; }

                    if (_popupCraftPage.heldItem != null && Game1.player != null)
                    {
                        Item rem = Game1.player.addItemToInventory(_popupCraftPage.heldItem);
                        _popupCraftPage.heldItem = rem;
                        if (rem != null)
                            break;
                    }
                    craftedAny++;
                }
            }
            else if (_popupKind == PopupKind.ShopSell && _popupOwnerMenu is ShopMenu sellShop && _popupSrcInv != null && _popupSrcSlot >= 0)
            {
                Item cur = GetItemAt(_popupSrcInv, _popupSrcSlot);
                if (cur != null && _popupQty > 0)
                {
                    int sellQty = Math.Min(_popupQty, cur.Stack);
                    if (sellQty >= cur.Stack)
                    {
                        try { sellShop.receiveLeftClick(_popupUiClickPt.X, _popupUiClickPt.Y, true); } catch { }
                    }
                    else
                    {
                        int remain = cur.Stack - sellQty;
                        Item partial = cur.getOne();
                        partial.Stack = sellQty;
                        SetItemAt(_popupSrcInv, _popupSrcSlot, partial);
                        try { sellShop.receiveLeftClick(_popupUiClickPt.X, _popupUiClickPt.Y, true); } catch { }
                        Item after = GetItemAt(_popupSrcInv, _popupSrcSlot);
                        if (after == null)
                        {
                            cur.Stack = remain;
                            SetItemAt(_popupSrcInv, _popupSrcSlot, cur);
                        }
                        else
                        {
                            SetItemAt(_popupSrcInv, _popupSrcSlot, cur);
                        }
                    }
                }
            }
            else if (_popupKind == PopupKind.ChestSplit && _popupOwnerMenu is ItemGrabMenu grab && _popupSrcInv != null && _popupSrcSlot >= 0)
            {
                Item cur = GetItemAt(_popupSrcInv, _popupSrcSlot);
                if (cur != null && _popupQty > 0)
                {
                    int moveQty = Math.Min(_popupQty, cur.Stack);
                    TransferQuantityBetweenInventories(grab, _popupSrcInv, _popupSrcSlot, _popupDstInv, moveQty);
                }
            }
            else if (_popupKind == PopupKind.BagSplit && _popupSrcInv != null && _popupSrcSlot >= 0)
            {
                Item cur = GetItemAt(_popupSrcInv, _popupSrcSlot);
                if (cur != null && cur.Stack > 1 && _popupQty > 0 && _popupQty < cur.Stack)
                {
                    int emptySlot = FindFirstEmptySlot(_popupSrcInv);
                    if (emptySlot >= 0)
                    {
                        Item split = cur.getOne();
                        split.Stack = _popupQty;
                        cur.Stack -= _popupQty;
                        SetItemAt(_popupSrcInv, emptySlot, split);
                        try { Game1.playSound("dwop"); } catch { }
                    }
                    else
                    {
                        try { Game1.playSound("cancel"); } catch { }
                    }
                }
            }

            ClosePopup();
        }

        // =========================================================================
        // B. MOBILE ITEM MOVEMENT (Zero Mouse Cursor Bug!)
        // =========================================================================
        private bool UpdateInventoryTouch(
            IClickableMenu ownerMenu,
            InventoryMenu playerInv,
            InventoryMenu chestInv,
            ClickableTextureComponent trashCan,
            List<ClickableComponent> equipIcons,
            bool isChestMenu,
            TouchLocation tl,
            Vector2 vp,
            Point uiPt,
            double nowSec)
        {
            if (tl.State == TouchLocationState.Pressed)
            {
                ResetTouch();

                // If user has a Tap-Selected slot in Backpack and taps DROP button below Trash Can -> drop onto ground (great for Co-op!)
                if (!isChestMenu && _selectedBagSlot >= 0 && _selectedBagInv != null && trashCan != null)
                {
                    Rectangle dropUi = ComputeDropBtnUiRect(trashCan);
                    if (dropUi.Contains(uiPt))
                    {
                        Item selItem = GetItemAt(_selectedBagInv, _selectedBagSlot);
                        if (selItem != null && selItem.canBeDropped() && Game1.player != null)
                        {
                            SetItemAt(_selectedBagInv, _selectedBagSlot, null);
                            try { Game1.createItemDebris(selItem, Game1.player.getStandingPosition(), Game1.player.FacingDirection); } catch { }
                            try { Game1.playSound("throwDownITem"); } catch { }
                        }
                        else
                        {
                            try { Game1.playSound("cancel"); } catch { }
                        }
                        ClearSelection();
                        _popConsumeTouchId = tl.Id;
                        return true;
                    }
                }

                // If user has a Tap-Selected slot in Backpack and taps Trash Can -> trash cleanly!
                if (!isChestMenu && _selectedBagSlot >= 0 && _selectedBagInv != null &&
                    trashCan != null && trashCan.containsPoint(uiPt.X, uiPt.Y))
                {
                    Item selItem = GetItemAt(_selectedBagInv, _selectedBagSlot);
                    if (selItem != null && selItem.canBeTrashed())
                    {
                        SetItemAt(_selectedBagInv, _selectedBagSlot, null);
                        try { Utility.trashItem(selItem); } catch { }
                    }
                    ClearSelection();
                    _popConsumeTouchId = tl.Id;
                    return true;
                }

                // If user taps an Equipment slot (Ring / Boots / Hat / Shirt / Pants / Trinket):
                // - With a Backpack item selected -> equip it cleanly!
                // - With NO Backpack item selected -> unequip straight into Backpack (NEVER leaves item stuck on CursorSlotItem!)
                if (!isChestMenu && equipIcons != null && ownerMenu is InventoryPage ipEq)
                {
                    for (int i = 0; i < equipIcons.Count; i++)
                    {
                        if (equipIcons[i].containsPoint(uiPt.X, uiPt.Y))
                        {
                            if (_selectedBagSlot >= 0 && _selectedBagInv != null)
                            {
                                Item selItem = GetItemAt(_selectedBagInv, _selectedBagSlot);
                                if (selItem != null)
                                    TryEquipItem(ipEq, _selectedBagInv, _selectedBagSlot, selItem, uiPt);
                                ClearSelection();
                            }
                            else
                            {
                                TryUnequipToBackpack(ipEq, uiPt);
                            }
                            _popConsumeTouchId = tl.Id;
                            return true;
                        }
                    }
                }

                InventoryMenu hitInv = null;
                int hitSlot = -1;

                if (chestInv != null)
                {
                    int s = FindSlotAt(chestInv, uiPt);
                    if (s >= 0) { hitInv = chestInv; hitSlot = s; }
                }
                if (hitInv == null && playerInv != null)
                {
                    int s = FindSlotAt(playerInv, uiPt);
                    if (s >= 0) { hitInv = playerInv; hitSlot = s; }
                }

                if (hitInv != null && hitSlot >= 0)
                {
                    Item it = GetItemAt(hitInv, hitSlot);

                    // If tapping an EMPTY slot while another Backpack slot is selected -> instant move!
                    if (it == null && !isChestMenu && _selectedBagSlot >= 0 && _selectedBagInv != null)
                    {
                        MoveOrSwapSlots(null, _selectedBagInv, _selectedBagSlot, hitInv, hitSlot);
                        ClearSelection();
                        _popConsumeTouchId = tl.Id;
                        return true;
                    }

                    // Intercept ANY occupied slot cleanly (never sticks to mouse cursor!)
                    if (it != null && Game1.player.CursorSlotItem == null)
                    {
                        _touchId = tl.Id;
                        _touchStartVp = vp;
                        _touchCurVp = vp;
                        _touchStartSec = nowSec;
                        _dragSrcInv = hitInv;
                        _dragSrcSlot = hitSlot;
                        _dragItem = it;
                        _isDragging = false;
                        return true;
                    }
                }

                // Tapped outside slots (e.g. tab bar, organize, close X) -> clear highlight & let native UI click work
                ClearSelection();
                return false;
            }

            if (tl.Id != _touchId || _dragSrcInv == null || _dragSrcSlot < 0)
                return false;

            if (tl.State == TouchLocationState.Moved)
            {
                _touchCurVp = vp;
                float dist = Vector2.Distance(vp, _touchStartVp);

                if (!_isDragging)
                {
                    if (dist >= 14f)
                    {
                        _isDragging = true;
                        ClearSelection();
                        try { Game1.playSound("smallSelect"); } catch { }
                    }
                    else if ((nowSec - _touchStartSec) >= 0.35)
                    {
                        if (_dragItem != null && _dragItem.Stack > 1)
                        {
                            // Long-Press on stack > 1 -> Mobile Pixel Split Quantity Slider!
                            InventoryMenu srcInv = _dragSrcInv;
                            int srcSlot = _dragSrcSlot;
                            Item item = _dragItem;
                            InventoryMenu dstInv = isChestMenu ? ((srcInv == playerInv) ? chestInv : playerInv) : null;
                            _popConsumeTouchId = tl.Id;
                            ResetTouch();
                            ClearSelection();
                            OpenSplitPopup(ownerMenu, srcInv, srcSlot, dstInv, item, isChestMenu);
                            return true;
                        }
                        else if (_dragItem is Tool attTool && HasAnyAttachment(attTool) && (nowSec - _touchStartSec) >= 0.42)
                        {
                            // Long-Press on FishingRod/Slingshot with attached Bait/Tackle/Ammo -> pop attachment cleanly into Backpack!
                            if (TryDetachOneAttachment(attTool))
                            {
                                try { Game1.playSound("dwop"); } catch { }
                                _popConsumeTouchId = tl.Id;
                                ResetTouch();
                                ClearSelection();
                                return true;
                            }
                            else
                            {
                                try { Game1.playSound("cancel"); } catch { }
                                _popConsumeTouchId = tl.Id;
                                ResetTouch();
                                ClearSelection();
                                return true;
                            }
                        }
                        else if (_dragItem != null)
                        {
                            // Long-Press on single item (Stack == 1) -> smoothly lift under finger for Drag-and-Drop (NEVER transfer to mouse cursor!)
                            _isDragging = true;
                            ClearSelection();
                            try { Game1.playSound("smallSelect"); } catch { }
                        }
                    }
                }
                return true;
            }

            if (tl.State == TouchLocationState.Released || tl.State == TouchLocationState.Invalid)
            {
                bool wasDragging = _isDragging;
                InventoryMenu srcInv = _dragSrcInv;
                int srcSlot = _dragSrcSlot;
                Item item = _dragItem;
                ResetTouch();

                if (item == null || srcInv == null || srcSlot < 0)
                    return true;

                if (wasDragging)
                {
                    if (trashCan != null && trashCan.containsPoint(uiPt.X, uiPt.Y) && item.canBeTrashed())
                    {
                        SetItemAt(srcInv, srcSlot, null);
                        try { Utility.trashItem(item); } catch { }
                        return true;
                    }

                    InventoryMenu dstInv = null;
                    int dstSlot = -1;
                    if (chestInv != null)
                    {
                        int s = FindSlotAt(chestInv, uiPt);
                        if (s >= 0) { dstInv = chestInv; dstSlot = s; }
                    }
                    if (dstInv == null && playerInv != null)
                    {
                        int s = FindSlotAt(playerInv, uiPt);
                        if (s >= 0) { dstInv = playerInv; dstSlot = s; }
                    }

                    if (dstInv != null && dstSlot >= 0)
                    {
                        if (dstInv == srcInv && dstSlot == srcSlot)
                            return true;
                        if (ownerMenu is ItemGrabMenu grabDrop && srcInv == playerInv && dstInv == chestInv && !grabDrop.reverseGrab && !grabDrop.shippingBin)
                        {
                            try { Game1.playSound("cancel"); } catch { }
                            return true;
                        }
                        MoveOrSwapSlots(ownerMenu as ItemGrabMenu, srcInv, srcSlot, dstInv, dstSlot);
                        return true;
                    }

                    if (equipIcons != null && ownerMenu is InventoryPage ip)
                    {
                        for (int i = 0; i < equipIcons.Count; i++)
                        {
                            if (equipIcons[i].containsPoint(uiPt.X, uiPt.Y))
                            {
                                TryEquipItem(ip, srcInv, srcSlot, item, uiPt);
                                return true;
                            }
                        }
                    }

                    // Released outside any slot -> smoothly cancel drag back to original slot (never sticks to mouse or drops!)
                    return true;
                }
                else
                {
                    // SHORT TAP!
                    if (isChestMenu && ownerMenu is ItemGrabMenu grab)
                    {
                        if (srcInv == playerInv && !grab.reverseGrab && !grab.shippingBin)
                        {
                            // Read-only Fishing Treasure Chest / Mine Reward Chest: never push player items into treasure chest!
                            try { Game1.playSound("cancel"); } catch { }
                            return true;
                        }
                        InventoryMenu dstInv = (srcInv == playerInv) ? chestInv : playerInv;
                        TransferQuantityBetweenInventories(grab, srcInv, srcSlot, dstInv, item.Stack);
                        return true;
                    }
                    else if (ownerMenu is InventoryPage ip)
                    {
                        // Mobile Backpack Tap-Select / Tap-Swap (NEVER puts item on mouse cursor!)
                        if (_selectedBagSlot < 0)
                        {
                            _selectedBagSlot = srcSlot;
                            _selectedBagInv = srcInv;
                            try { ip.performHoverAction(uiPt.X, uiPt.Y); } catch { }
                            try { Game1.playSound("smallSelect"); } catch { }
                        }
                        else if (_selectedBagSlot == srcSlot && _selectedBagInv == srcInv)
                        {
                            ClearSelection();
                        }
                        else
                        {
                            MoveOrSwapSlots(null, _selectedBagInv, _selectedBagSlot, srcInv, srcSlot);
                            ClearSelection();
                        }
                        return true;
                    }
                }
            }

            return true;
        }

        private static Rectangle ComputeDropBtnUiRect(ClickableTextureComponent trashCan)
        {
            if (trashCan == null) return Rectangle.Empty;
            int w = Math.Max(trashCan.bounds.Width + 28, 92);
            int h = 44;
            int x = trashCan.bounds.Center.X - w / 2;
            int y = trashCan.bounds.Bottom + 10;
            if (Game1.uiViewport.Height > 0 && y + h > Game1.uiViewport.Height - 12)
                y = Math.Max(8, trashCan.bounds.Y - h - 10);
            return new Rectangle(x, y, w, h);
        }

        private static bool HasAnyAttachment(Tool tool)
        {
            if (tool == null || tool.attachments == null || tool.AttachmentSlotsCount <= 0)
                return false;
            for (int i = 0; i < tool.attachments.Length; i++)
            {
                if (tool.attachments[i] != null)
                    return true;
            }
            return false;
        }

        private static bool TryDetachOneAttachment(Tool tool)
        {
            if (tool == null || Game1.player == null || !HasAnyAttachment(tool))
                return false;
            // Ensure player inventory can accept at least 1 detached item before calling attach(null)
            StardewValley.Object firstAtt = null;
            for (int i = 0; i < tool.attachments.Length; i++)
            {
                if (tool.attachments[i] != null) { firstAtt = tool.attachments[i]; break; }
            }
            if (firstAtt == null || !Game1.player.couldInventoryAcceptThisItem(firstAtt))
                return false;

            StardewValley.Object popped = tool.attach(null);
            if (popped != null)
            {
                Item rem = Game1.player.addItemToInventory(popped);
                if (rem is StardewValley.Object remObj)
                    tool.attach(remObj);
                return true;
            }
            return false;
        }

        private static void TryEquipItem(InventoryPage ip, InventoryMenu srcInv, int srcSlot, Item item, Point uiPt)
        {
            SetItemAt(srcInv, srcSlot, null);
            Game1.player.CursorSlotItem = item;
            try { ip.receiveLeftClick(uiPt.X, uiPt.Y, true); } catch { }
            if (Game1.player.CursorSlotItem != null)
            {
                SetItemAt(srcInv, srcSlot, Game1.player.CursorSlotItem);
                Game1.player.CursorSlotItem = null;
            }
        }

        private static void TryUnequipToBackpack(InventoryPage ip, Point uiPt)
        {
            if (ip == null || Game1.player == null) return;
            Game1.player.CursorSlotItem = null;
            try { ip.receiveLeftClick(uiPt.X, uiPt.Y, true); } catch { }
            Item lifted = Game1.player.CursorSlotItem;
            if (lifted == null) return;

            Game1.player.CursorSlotItem = null;
            Item rem = null;
            try { rem = Game1.player.addItemToInventory(lifted); } catch { rem = lifted; }
            if (rem != null)
            {
                // Backpack is completely full -> re-equip back into slot so CursorSlotItem NEVER sticks!
                Game1.player.CursorSlotItem = rem;
                try { ip.receiveLeftClick(uiPt.X, uiPt.Y, true); } catch { }
                Game1.player.CursorSlotItem = null;
                try { Game1.playSound("cancel"); } catch { }
            }
            else
            {
                try { Game1.playSound("dwop"); } catch { }
            }
        }

        private void OpenSplitPopup(IClickableMenu owner, InventoryMenu srcInv, int srcSlot, InventoryMenu dstInv, Item item, bool isChest)
        {
            _popupKind = isChest ? PopupKind.ChestSplit : PopupKind.BagSplit;
            _popupOwnerMenu = owner;
            _popupItem = item;
            _popupSaleItem = item;
            _popupSrcInv = srcInv;
            _popupSrcSlot = srcSlot;
            _popupDstInv = dstInv;
            _popupMinQty = 1;
            _popupMaxQty = isChest ? item.Stack : Math.Max(1, item.Stack - 1);
            _popupQty = Math.Clamp(item.Stack / 2, _popupMinQty, _popupMaxQty);
            _popupUnitPrice = 0;
            _popupCanAfford = true;
            try { Game1.playSound("smallSelect"); } catch { }
        }

        private void MoveOrSwapSlots(ItemGrabMenu grab, InventoryMenu srcInv, int srcSlot, InventoryMenu dstInv, int dstSlot)
        {
            Item srcItem = GetItemAt(srcInv, srcSlot);
            if (srcItem == null) return;

            if (grab != null && grab.shippingBin && dstInv == grab.ItemsToGrabMenu && srcInv == grab.inventory)
            {
                TransferQuantityBetweenInventories(grab, srcInv, srcSlot, dstInv, srcItem.Stack);
                return;
            }

            if (dstInv.highlightMethod != null && !dstInv.highlightMethod(srcItem))
            {
                try { Game1.playSound("cancel"); } catch { }
                return;
            }

            Item dstItem = GetItemAt(dstInv, dstSlot);
            if (dstItem == null)
            {
                SetItemAt(dstInv, dstSlot, srcItem);
                SetItemAt(srcInv, srcSlot, null);
                try { Game1.playSound("stoneStep"); } catch { }
            }
            else if (dstItem is Tool dstTool && dstTool.AttachmentSlotsCount > 0 && srcItem is StardewValley.Object srcObj && dstTool.canThisBeAttached(srcObj))
            {
                // Direct Mobile Attach: drag/tap Bait or Tackle onto FishingRod, or Ammo onto Slingshot!
                StardewValley.Object swapped = dstTool.attach(srcObj);
                SetItemAt(srcInv, srcSlot, swapped);
                try { Game1.playSound("button1"); } catch { }
            }
            else if (srcItem is Tool srcTool && srcTool.AttachmentSlotsCount > 0 && dstItem is StardewValley.Object dstObj && srcTool.canThisBeAttached(dstObj))
            {
                // Reverse direction (drag/tap Rod onto Bait or Slingshot onto Ammo) also attaches cleanly!
                StardewValley.Object swapped = srcTool.attach(dstObj);
                SetItemAt(dstInv, dstSlot, swapped);
                try { Game1.playSound("button1"); } catch { }
            }
            else if (dstItem.canStackWith(srcItem))
            {
                int rem = dstItem.addToStack(srcItem);
                if (rem <= 0) SetItemAt(srcInv, srcSlot, null);
                else srcItem.Stack = rem;
                try { Game1.playSound("stoneStep"); } catch { }
            }
            else
            {
                if (srcInv.highlightMethod != null && !srcInv.highlightMethod(dstItem))
                {
                    try { Game1.playSound("cancel"); } catch { }
                    return;
                }
                SetItemAt(dstInv, dstSlot, srcItem);
                SetItemAt(srcInv, srcSlot, dstItem);
                try { Game1.playSound("stoneStep"); } catch { }
            }
        }

        private void TransferQuantityBetweenInventories(ItemGrabMenu grab, InventoryMenu srcInv, int srcSlot, InventoryMenu dstInv, int qty)
        {
            Item srcItem = GetItemAt(srcInv, srcSlot);
            if (srcItem == null || qty <= 0) return;

            bool fromPlayerToChest = (grab != null && srcInv == grab.inventory);

            if (fromPlayerToChest && grab != null && (grab.shippingBin || !grab.showReceivingMenu) && grab.behaviorFunction != null)
            {
                int count = Math.Min(qty, srcItem.Stack);
                Item toSend = (count >= srcItem.Stack) ? srcItem : srcItem.getOne();
                toSend.Stack = count;
                if (count >= srcItem.Stack) SetItemAt(srcInv, srcSlot, null);
                else srcItem.Stack -= count;

                try { grab.behaviorFunction(toSend, Game1.player); } catch { }
                try { Game1.playSound("Ship"); } catch { }
                return;
            }

            if (dstInv == null) return;

            if (dstInv.highlightMethod != null && !dstInv.highlightMethod(srcItem))
            {
                try { Game1.playSound("cancel"); } catch { }
                return;
            }

            int toMove = Math.Min(qty, srcItem.Stack);
            Item moving = (toMove >= srcItem.Stack) ? srcItem : srcItem.getOne();
            moving.Stack = toMove;

            int leftover = AddItemToInventoryMenu(dstInv, moving);
            int actuallyMoved = toMove - leftover;

            if (actuallyMoved > 0)
            {
                if (actuallyMoved >= srcItem.Stack) SetItemAt(srcInv, srcSlot, null);
                else srcItem.Stack -= actuallyMoved;

                if (!fromPlayerToChest && grab?.behaviorOnItemGrab != null)
                {
                    try { grab.behaviorOnItemGrab(moving, Game1.player); } catch { }
                }
                try { Game1.playSound("dwop"); } catch { }
            }
            else
            {
                try { Game1.playSound("cancel"); } catch { }
            }
        }

        private static int AddItemToInventoryMenu(InventoryMenu inv, Item item)
        {
            if (inv == null || item == null) return item?.Stack ?? 0;

            if (inv.playerInventory)
            {
                Item rem = Game1.player.addItemToInventory(item);
                return rem != null ? rem.Stack : 0;
            }

            int cap = Math.Min(inv.capacity, inv.inventory != null ? inv.inventory.Count : inv.capacity);
            EnsureInventoryListSize(inv, cap);

            for (int i = 0; i < cap; i++)
            {
                Item ex = inv.actualInventory[i];
                if (ex != null && ex.canStackWith(item))
                {
                    int rem = ex.addToStack(item);
                    item.Stack = rem;
                    if (rem <= 0) return 0;
                }
            }

            for (int i = 0; i < cap; i++)
            {
                if (inv.actualInventory[i] == null)
                {
                    inv.actualInventory[i] = item;
                    return 0;
                }
            }

            return item.Stack;
        }

        private static int FindSlotAt(InventoryMenu inv, Point uiPt)
        {
            if (inv?.inventory == null) return -1;
            int limit = Math.Min(inv.capacity, inv.inventory.Count);
            for (int i = 0; i < limit; i++)
            {
                if (inv.inventory[i] != null && inv.inventory[i].containsPoint(uiPt.X, uiPt.Y))
                    return i;
            }
            return -1;
        }

        private static int FindFirstEmptySlot(InventoryMenu inv)
        {
            if (inv?.actualInventory == null) return -1;
            int cap = Math.Min(inv.capacity, inv.inventory != null ? inv.inventory.Count : inv.capacity);
            EnsureInventoryListSize(inv, cap);
            for (int i = 0; i < cap; i++)
            {
                if (inv.actualInventory[i] == null) return i;
            }
            return -1;
        }

        private static Item GetItemAt(InventoryMenu inv, int slot)
        {
            if (inv?.actualInventory == null || slot < 0 || slot >= inv.actualInventory.Count)
                return null;
            return inv.actualInventory[slot];
        }

        private static void SetItemAt(InventoryMenu inv, int slot, Item item)
        {
            if (inv?.actualInventory == null || slot < 0) return;
            EnsureInventoryListSize(inv, slot + 1);
            inv.actualInventory[slot] = item;
        }

        private static void EnsureInventoryListSize(InventoryMenu inv, int needed)
        {
            while (inv.actualInventory.Count < needed)
                inv.actualInventory.Add(null);
        }

        // =========================================================================
        // POPUP TOUCH & LAYOUT
        // =========================================================================
        private void UpdatePopupTouches(TouchCollection touches, Func<Vector2, Vector2> rawToVp, double nowSec)
        {
            if (touches.Count == 0)
            {
                _popSliderTouchId = -1;
                _popHoldTouchId = -1;
                _popHoldDelta = 0;
                return;
            }

            TouchLocation tl = touches[0];
            Vector2 vp = rawToVp(tl.Position);
            Point pt = new Point((int)vp.X, (int)vp.Y);

            if (tl.Id == _popSliderTouchId)
            {
                if (tl.State == TouchLocationState.Pressed || tl.State == TouchLocationState.Moved)
                {
                    SetQtyFromSliderX(vp.X);
                    return;
                }
                _popSliderTouchId = -1;
                return;
            }

            // Hold-to-repeat accelerated +/- quantity stepper
            if (tl.Id == _popHoldTouchId && _popHoldDelta != 0)
            {
                if (tl.State == TouchLocationState.Moved)
                {
                    Rectangle holdR = _popHoldDelta == -10 ? _popBtnM10 :
                                      _popHoldDelta == -1  ? _popBtnM1  :
                                      _popHoldDelta == 1   ? _popBtnP1  : _popBtnP10;
                    if (holdR.Contains(pt) && _popupMaxQty > _popupMinQty)
                    {
                        double held = nowSec - _popHoldStartSec;
                        double interval = held >= 1.15 ? 0.05 : 0.11;
                        if (held >= 0.34 && (nowSec - _popHoldLastStepSec) >= interval)
                        {
                            _popHoldLastStepSec = nowSec;
                            int next = Math.Clamp(_popupQty + _popHoldDelta, _popupMinQty, _popupMaxQty);
                            if (next != _popupQty)
                            {
                                _popupQty = next;
                                TryClickSound();
                            }
                        }
                    }
                    return;
                }
                _popHoldTouchId = -1;
                _popHoldDelta = 0;
                return;
            }

            if (tl.State != TouchLocationState.Pressed)
                return;

            var sliderHit = new Rectangle(_popSliderTrack.X - 20, _popSliderTrack.Y - 28, _popSliderTrack.Width + 40, _popSliderTrack.Height + 56);
            if (_popupMaxQty > _popupMinQty && sliderHit.Contains(pt))
            {
                _popSliderTouchId = tl.Id;
                SetQtyFromSliderX(vp.X);
                return;
            }

            if (_popupMaxQty > _popupMinQty)
            {
                int stepDelta = 0;
                if (_popBtnM10.Contains(pt)) stepDelta = -10;
                else if (_popBtnM1.Contains(pt)) stepDelta = -1;
                else if (_popBtnP1.Contains(pt)) stepDelta = 1;
                else if (_popBtnP10.Contains(pt)) stepDelta = 10;

                if (stepDelta != 0)
                {
                    _popupQty = Math.Clamp(_popupQty + stepDelta, _popupMinQty, _popupMaxQty);
                    _popHoldTouchId = tl.Id;
                    _popHoldDelta = stepDelta;
                    _popHoldStartSec = nowSec;
                    _popHoldLastStepSec = nowSec;
                    TryClickSound();
                    return;
                }
                if (_popBtnMax.Contains(pt))
                {
                    _popupQty = (_popupKind == PopupKind.BagSplit) ? Math.Clamp((_popupMaxQty + 1) / 2, _popupMinQty, _popupMaxQty) : _popupMaxQty;
                    TryClickSound();
                    return;
                }
            }

            if (_popBtnConfirm.Contains(pt))
            {
                _popConsumeTouchId = tl.Id;
                ExecutePopupConfirm();
                return;
            }

            if (_popBtnCancel.Contains(pt) || _popCloseX.Contains(pt) || !_popPanel.Contains(pt))
            {
                _popConsumeTouchId = tl.Id;
                ClosePopup();
                try { Game1.playSound("cancel"); } catch { }
            }
        }

        private void SetQtyFromSliderX(float vpX)
        {
            if (_popSliderTrack.Width <= 4 || _popupMaxQty <= _popupMinQty) return;
            float frac = Math.Clamp((vpX - _popSliderTrack.X) / _popSliderTrack.Width, 0f, 1f);
            int next = _popupMinQty + (int)Math.Round(frac * (_popupMaxQty - _popupMinQty));
            if (next != _popupQty)
                _popupQty = Math.Clamp(next, _popupMinQty, _popupMaxQty);
        }

        private static void TryClickSound()
        {
            try { Game1.playSound("smallSelect"); } catch { }
        }

        private void ComputePopupLayout(int vpW, int vpH)
        {
            bool isCraft = _popupKind == PopupKind.Craft;
            bool isShop = _popupKind == PopupKind.ShopBuy || _popupKind == PopupKind.ShopSell;
            bool hasSlider = _popupMaxQty > _popupMinQty;

            float wFrac = isCraft ? 0.58f : (isShop ? 0.54f : 0.48f);
            float hFrac = isCraft
                ? (hasSlider ? 0.78f : 0.62f)
                : (isShop ? (hasSlider ? 0.68f : 0.48f) : (hasSlider ? 0.58f : 0.40f));

            int pw = (int)Math.Clamp(vpW * wFrac, 380f, vpW * 0.90f);
            int ph = (int)Math.Clamp(vpH * hFrac, 260f, vpH * 0.90f);
            int px = (vpW - pw) / 2;
            int py = (vpH - ph) / 2;
            _popPanel = new Rectangle(px, py, pw, ph);

            int closeSz = Math.Max(36, Math.Min(56, vpH / 16));
            _popCloseX = new Rectangle(_popPanel.Right - closeSz / 2 - 4, _popPanel.Y - closeSz / 3 + 4, closeSz, closeSz);

            int pad = Math.Max(28, pw / 19);
            int actH = Math.Max(48, Math.Min(74, ph / 7));
            int actY = py + ph - pad - actH;
            int qGap = Math.Max(8, pw / 60);
            int cancelW = (int)((pw - pad * 2 - qGap) * 0.32f);
            int confirmW = (pw - pad * 2 - qGap) - cancelW;
            _popBtnCancel  = new Rectangle(px + pad, actY, cancelW, actH);
            _popBtnConfirm = new Rectangle(px + pad + cancelW + qGap, actY, confirmW, actH);

            int qH = Math.Max(40, Math.Min(60, ph / 9));
            int qY = actY - Math.Max(14, ph / 32) - qH;
            int qW = (pw - pad * 2 - qGap * 4) / 5;
            _popBtnM10 = new Rectangle(px + pad, qY, qW, qH);
            _popBtnM1  = new Rectangle(px + pad + (qW + qGap) * 1, qY, qW, qH);
            _popBtnP1  = new Rectangle(px + pad + (qW + qGap) * 2, qY, qW, qH);
            _popBtnP10 = new Rectangle(px + pad + (qW + qGap) * 3, qY, qW, qH);
            _popBtnMax = new Rectangle(px + pad + (qW + qGap) * 4, qY, qW, qH);

            int trackH = Math.Max(22, Math.Min(34, ph / 20));
            int sliderY = qY - Math.Max(14, ph / 34) - trackH;
            _popSliderTrack = new Rectangle(px + pad, sliderY, pw - pad * 2, trackH);
        }

        // =========================================================================
        // STARDEW VALLEY NATIVE PIXEL ART RENDERING
        // =========================================================================
        public void Draw(
            SpriteBatch sb,
            Texture2D pixel,
            Texture2D circle,
            PadBridge bridge,
            IClickableMenu activeMenu,
            Func<Point, Vector2> uiToVp,
            Func<Vector2, Point> vpToUi,
            int vpW, int vpH,
            bool showFixedTooltip = true)
        {
            if (sb == null || pixel == null) return;

            // 0. Draw golden pixel highlight on Tap-Selected Backpack slot + DROP button + Fixed Non-Obstructive Item Tooltip
            if (_selectedBagSlot >= 0 && _selectedBagInv != null && activeMenu is GameMenu gm && gm.GetCurrentPage() is InventoryPage invP)
            {
                if (_selectedBagInv.inventory != null && _selectedBagSlot < _selectedBagInv.inventory.Count)
                {
                    Rectangle selVp = UiRectToVp(_selectedBagInv.inventory[_selectedBagSlot].bounds, uiToVp);
                    sb.Draw(pixel, selVp, new Color(255, 215, 70) * 0.22f);
                    DrawBorder(sb, pixel, selVp, Math.Max(3, vpH / 260), new Color(255, 200, 40));

                    Item selItem = GetItemAt(_selectedBagInv, _selectedBagSlot);
                    if (selItem != null)
                    {
                        if (selItem.canBeDropped() && invP.trashCan != null)
                        {
                            Rectangle dropVp = UiRectToVp(ComputeDropBtnUiRect(invP.trashCan), uiToVp);
                            PixelUi.DrawPixelPill(sb, pixel, bridge, dropVp, "DROP", PixelRole.Tool, false, vpH, Math.Max(1, vpH / 300), 0.94f);
                        }
                        if (showFixedTooltip && !IsPopupOpen && !IsDraggingItem)
                        {
                            DrawFixedItemTooltipBanner(sb, pixel, selItem, vpW, vpH);
                        }
                    }
                }
            }

            // 1. Draw Drag-and-Drop item smoothly under finger + hovered target slot highlight
            if (IsDraggingItem && activeMenu != null)
            {
                Point uiHover = vpToUi(_touchCurVp);
                ItemGrabMenu grab = activeMenu as ItemGrabMenu;
                InventoryPage invPage = (activeMenu as GameMenu)?.GetCurrentPage() as InventoryPage;

                ClickableTextureComponent trash = grab?.trashCan ?? invPage?.trashCan;
                if (trash != null && trash.containsPoint(uiHover.X, uiHover.Y))
                {
                    Rectangle trVp = UiRectToVp(trash.bounds, uiToVp);
                    sb.Draw(pixel, trVp, new Color(220, 50, 40) * 0.32f);
                    DrawBorder(sb, pixel, trVp, Math.Max(3, vpH / 260), new Color(255, 80, 65));
                }
                else
                {
                    Rectangle hoverUi = FindHoveredSlotRect(grab?.ItemsToGrabMenu, grab?.inventory ?? invPage?.inventory, uiHover);
                    if (!hoverUi.IsEmpty)
                    {
                        Rectangle hvVp = UiRectToVp(hoverUi, uiToVp);
                        sb.Draw(pixel, hvVp, new Color(90, 215, 90) * 0.24f);
                        DrawBorder(sb, pixel, hvVp, Math.Max(3, vpH / 260), new Color(90, 225, 90));
                    }
                }

                // Draw Stardew pixel slot box + item icon right under finger
                float iconScale = Math.Clamp(Math.Min(vpW, vpH) / 720f, 1.0f, 2.2f);
                int slotSize = (int)(68f * iconScale);
                var floatBox = new Rectangle((int)(_touchCurVp.X - slotSize * 0.5f), (int)(_touchCurVp.Y - slotSize * 0.5f), slotSize, slotSize);
                try
                {
                    sb.Draw(Game1.menuTexture, floatBox, Game1.getSourceRectForStandardTileSheet(Game1.menuTexture, 10), Color.White * 0.92f);
                }
                catch { }
                Vector2 drawPos = new Vector2(_touchCurVp.X - 32f * iconScale, _touchCurVp.Y - 32f * iconScale);
                try { _dragItem.drawInMenu(sb, drawPos, iconScale, 1f, 1f, StackDrawType.Draw, Color.White, true); } catch { }
            }

            // 2. Draw authentic Stardew Valley Pixel Parchment Buy / Sell / Split Popup!
            if (IsPopupOpen)
            {
                ComputePopupLayout(vpW, vpH);
                DrawStardewPixelPopup(sb, pixel, vpW, vpH);
            }
        }

        private void DrawStardewPixelPopup(SpriteBatch sb, Texture2D pixel, int vpW, int vpH)
        {
            // Warm dark vignette behind Stardew Mobile wooden dialog box
            sb.Draw(pixel, new Rectangle(0, 0, vpW, vpH), Color.Black * 0.56f);

            // Authentic Stardew Valley Mobile wooden 9-slice parchment dialog box!
            bool drewBox = false;
            try
            {
                if (Game1.menuTexture != null)
                {
                    IClickableMenu.drawTextureBox(sb, Game1.menuTexture, MobileBoxSrc,
                        _popPanel.X, _popPanel.Y, _popPanel.Width, _popPanel.Height, Color.White, 1f, drawShadow: true);
                    drewBox = true;
                }
            }
            catch { }
            if (!drewBox)
            {
                try { IClickableMenu.drawTextureBox(sb, _popPanel.X, _popPanel.Y, _popPanel.Width, _popPanel.Height, Color.White); }
                catch
                {
                    sb.Draw(pixel, _popPanel, new Color(249, 216, 156));
                    DrawBorder(sb, pixel, _popPanel, 4, new Color(90, 45, 12));
                }
            }

            // Official Stardew Valley red close 'X' button on top-right corner!
            try
            {
                if (Game1.mouseCursors != null)
                    sb.Draw(Game1.mouseCursors, _popCloseX, new Rectangle(337, 494, 12, 12), Color.White);
            }
            catch { }

            int pad = Math.Max(28, _popPanel.Width / 19);
            float fontScale = Math.Clamp(vpH / 790f, 0.78f, 1.65f);
            float iconScale = Math.Clamp(vpH / 820f, 0.90f, 1.65f);
            int slotPx = (int)(68f * iconScale);

            // Stardew Valley Mobile item slot frame (tile 10) + item sprite
            var slotBox = new Rectangle(_popPanel.X + pad, _popPanel.Y + pad, slotPx, slotPx);
            try
            {
                sb.Draw(Game1.menuTexture, slotBox, Game1.getSourceRectForStandardTileSheet(Game1.menuTexture, 10), Color.White);
            }
            catch { }

            if (_popupSaleItem != null)
            {
                Vector2 iconPos = new Vector2(slotBox.X + (slotPx - 64f * iconScale) * 0.5f, slotBox.Y + (slotPx - 64f * iconScale) * 0.5f);
                try { _popupSaleItem.drawInMenu(sb, iconPos, iconScale, 1f, 1f, StackDrawType.Hide, Color.White, true); } catch { }
            }

            // Header title & subtitle in warm Stardew wood ink
            float textX = slotBox.Right + Math.Max(14, pad / 2);
            float textY = _popPanel.Y + pad;
            float maxTextW = Math.Max(160f, (_popPanel.Right - pad - 18) - textX);

            string title = _popupKind == PopupKind.Craft && _popupCraftRecipe != null
                ? (_popupCraftRecipe.DisplayName ?? _popupCraftRecipe.name)
                : (_popupSaleItem?.DisplayName ?? _popupItem?.DisplayName ?? "Vật phẩm");
            if (_popupKind == PopupKind.Craft && _popupCraftRecipe != null && _popupCraftRecipe.numberProducedPerCraft > 1)
                title += $" (×{_popupCraftRecipe.numberProducedPerCraft})";

            DrawStardewText(sb, title, new Vector2(textX, textY), Game1.textColor, fontScale * 1.06f);
            float lineH = Math.Max(26f, vpH / 27f);
            float curY = textY + lineH * 1.08f;

            if (_popupKind == PopupKind.ShopBuy)
            {
                if (!string.IsNullOrEmpty(_popupTradeItemId) && _popupTradeCount > 0)
                {
                    int trSz = Math.Max(22, (int)(26f * fontScale));
                    string trName = "vật phẩm";
                    try
                    {
                        var td = ItemRegistry.GetDataOrErrorItem(_popupTradeItemId);
                        if (td != null)
                        {
                            trName = td.DisplayName ?? trName;
                            sb.Draw(td.GetTexture(), new Rectangle((int)textX, (int)curY - 2, trSz, trSz), td.GetSourceRect(), Color.White);
                        }
                    }
                    catch { }
                    int haveTrade = Game1.player?.Items?.CountId(_popupTradeItemId) ?? 0;
                    int needTrade = _popupTradeCount * _popupQty;
                    Color trCol = haveTrade >= needTrade ? new Color(36, 118, 42) : new Color(180, 32, 24);
                    DrawStardewText(sb, $"{trName}: {haveTrade}/{needTrade}", new Vector2(textX + trSz + 8, curY), trCol, fontScale * 0.86f);
                    curY += lineH;
                }
                else
                {
                    string unitTag = _popupCurrency == 1 ? "★" : (_popupCurrency == 2 ? "QC" : (_popupCurrency == 4 ? "Qi" : "g"));
                    string priceSub = _popupUnitPrice <= 0
                        ? $"Miễn phí · SL: {_popupQty}"
                        : $"Giá: {_popupUnitPrice:N0}{unitTag} × {_popupQty} = {(_popupUnitPrice * (long)_popupQty):N0}{unitTag}";
                    if (_popupStock != ShopMenu.infiniteStock && _popupStock > 0)
                        priceSub += $"  (Còn {_popupStock})";
                    DrawStardewText(sb, priceSub, new Vector2(textX, curY), new Color(92, 42, 16), fontScale * 0.88f);
                    curY += lineH;

                    if (_popupUnitPrice > 0)
                    {
                        int have = ShopMenu.getPlayerCurrencyAmount(Game1.player, _popupCurrency);
                        int coinSz = Math.Max(18, (int)(22f * fontScale));
                        Rectangle coinSrc = _popupCurrency == 1
                            ? new Rectangle(338, 400, 8, 8)
                            : new Rectangle(193, 373, 9, 10);
                        try { sb.Draw(Game1.mouseCursors, new Rectangle((int)textX, (int)curY + 2, coinSz, coinSz), coinSrc, Color.White); } catch { }
                        Color haveCol = _popupCanAfford ? new Color(36, 118, 42) : new Color(180, 32, 24);
                        DrawStardewText(sb, $"Hiện có: {have:N0}{unitTag}", new Vector2(textX + coinSz + 8, curY), haveCol, fontScale * 0.84f);
                        curY += lineH;
                    }
                }
            }
            else if (_popupKind == PopupKind.ShopSell)
            {
                DrawStardewText(sb, $"Bán: {_popupQty}/{_popupMaxQty} · Thu: {(_popupUnitPrice * (long)_popupQty):N0}g", new Vector2(textX, curY), new Color(92, 42, 16), fontScale * 0.88f);
                curY += lineH;
            }
            else if (_popupKind == PopupKind.Craft && _popupCraftRecipe != null)
            {
                string craftMeta = _popupCraftRecipe.isCookingRecipe
                    ? $"Đã nấu: {_popupCraftRecipe.timesCrafted} lần · Có thể nấu: {(_popupCanAfford ? _popupMaxQty : 0)}"
                    : $"Đã chế: {_popupCraftRecipe.timesCrafted} lần · Có thể chế: {(_popupCanAfford ? _popupMaxQty : 0)}";
                Color metaCol = _popupCanAfford ? new Color(36, 118, 42) : new Color(176, 36, 26);
                DrawStardewText(sb, craftMeta, new Vector2(textX, curY), metaCol, fontScale * 0.84f);
                curY += lineH;
            }
            else
            {
                DrawStardewText(sb, $"Số lượng: {_popupQty} / {_popupMaxQty}", new Vector2(textX, curY), new Color(92, 42, 16), fontScale * 0.88f);
                curY += lineH;
            }

            // Energy / Health icons for edible items (authentic Stardew Mobile tooltip icons!)
            if (_popupItem is StardewValley.Object edObj && edObj.Edibility > 0)
            {
                int en = edObj.staminaRecoveredOnConsumption();
                int hp = edObj.healthRecoveredOnConsumption();
                int statSz = Math.Max(18, (int)(22f * fontScale));
                float sx = textX;
                try { sb.Draw(Game1.mouseCursors, new Rectangle((int)sx, (int)curY + 2, statSz, statSz), new Rectangle(0, 428, 10, 10), Color.White); } catch { }
                string enTxt = $"+{en}";
                DrawStardewText(sb, enTxt, new Vector2(sx + statSz + 5, curY), new Color(34, 122, 44), fontScale * 0.82f);
                sx += statSz + 5 + MeasureWidth(enTxt, fontScale * 0.82f) + 18f;
                try { sb.Draw(Game1.mouseCursors, new Rectangle((int)sx, (int)curY + 2, statSz, statSz), new Rectangle(0, 438, 10, 10), Color.White); } catch { }
                DrawStardewText(sb, $"+{hp}", new Vector2(sx + statSz + 5, curY), new Color(180, 42, 34), fontScale * 0.82f);
                curY += lineH;
            }

            // Wooden divider below header
            int bodyTop = Math.Max(slotBox.Bottom + Math.Max(10, pad / 3), (int)curY + 4);
            int bodyBot = (_popupMaxQty > _popupMinQty ? _popSliderTrack.Y : _popBtnConfirm.Y) - Math.Max(12, pad / 3);
            sb.Draw(pixel, new Rectangle(_popPanel.X + pad, bodyTop, _popPanel.Width - pad * 2, Math.Max(2, vpH / 420)), new Color(138, 86, 42) * 0.45f);
            bodyTop += Math.Max(8, pad / 4);

            // Body area: Ingredient list for Crafting OR Description for Shop
            if (_popupKind == PopupKind.Craft && _popupCraftRecipe != null && bodyBot > bodyTop + 28)
            {
                DrawCraftingIngredientsArea(sb, pixel, _popupCraftPage, _popupCraftRecipe, _popPanel.X + pad, bodyTop, _popPanel.Width - pad * 2, bodyBot - bodyTop, fontScale);
            }
            else if ((_popupKind == PopupKind.ShopBuy || _popupKind == PopupKind.ShopSell) && bodyBot > bodyTop + 22)
            {
                string desc = "";
                try { desc = (_popupSaleItem?.getDescription() ?? _popupItem?.getDescription() ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); } catch { }
                if (!string.IsNullOrEmpty(desc))
                {
                    DrawWrappedStardewText(sb, desc, _popPanel.X + pad, bodyTop, _popPanel.Width - pad * 2, bodyBot - bodyTop, new Color(86, 46, 22), fontScale * 0.80f);
                }
            }

            // Pixel Wooden Slider Track + Stardew Pixel Knob + Step Buttons
            if (_popupMaxQty > _popupMinQty)
            {
                try
                {
                    IClickableMenu.drawTextureBox(sb, Game1.mouseCursors, new Rectangle(403, 383, 6, 6),
                        _popSliderTrack.X, _popSliderTrack.Y, _popSliderTrack.Width, _popSliderTrack.Height, Color.White, 4f, drawShadow: false);
                }
                catch
                {
                    sb.Draw(pixel, _popSliderTrack, new Color(165, 110, 62));
                }

                float frac = (float)(_popupQty - _popupMinQty) / Math.Max(1, _popupMaxQty - _popupMinQty);
                int knobW = Math.Max(36, (int)(_popSliderTrack.Height * 1.65f));
                int knobH = Math.Max(28, (int)(_popSliderTrack.Height * 1.15f));
                int usableW = Math.Max(1, _popSliderTrack.Width - knobW);
                int kx = _popSliderTrack.X + (int)(usableW * frac);
                int ky = _popSliderTrack.Center.Y - knobH / 2;

                if (kx > _popSliderTrack.X + 4)
                    sb.Draw(pixel, new Rectangle(_popSliderTrack.X + 4, _popSliderTrack.Y + 4, kx - _popSliderTrack.X, Math.Max(4, _popSliderTrack.Height - 8)), new Color(235, 178, 52) * 0.65f);

                try
                {
                    sb.Draw(Game1.mouseCursors, new Rectangle(kx, ky, knobW, knobH), new Rectangle(420, 441, 10, 6), Color.White);
                }
                catch
                {
                    sb.Draw(pixel, new Rectangle(kx, ky, knobW, knobH), new Color(120, 64, 24));
                }

                DrawStardewPixelButton(sb, _popBtnM10, "-10", Color.White, fontScale * 0.88f);
                DrawStardewPixelButton(sb, _popBtnM1,  "-1",  Color.White, fontScale * 0.88f);
                DrawStardewPixelButton(sb, _popBtnP1,  "+1",  Color.White, fontScale * 0.88f);
                DrawStardewPixelButton(sb, _popBtnP10, "+10", Color.White, fontScale * 0.88f);
                string maxLbl = _popupKind == PopupKind.BagSplit ? "1/2" : "MAX";
                DrawStardewPixelButton(sb, _popBtnMax, maxLbl, new Color(255, 232, 160), fontScale * 0.88f);
            }

            // Bottom Stardew Wooden Buttons (Hủy + Mua / Bán / Chế tạo / Chuyển / Tách)
            DrawStardewPixelButton(sb, _popBtnCancel, "Hủy", new Color(245, 200, 190), fontScale * 0.96f);

            string confirmLbl;
            Color confirmTint;
            if (_popupKind == PopupKind.ShopBuy)
            {
                if (!_popupCanAfford) { confirmLbl = "Không đủ điều kiện"; confirmTint = new Color(230, 165, 160); }
                else { confirmLbl = $"Mua ×{_popupQty}"; confirmTint = new Color(178, 242, 165); }
            }
            else if (_popupKind == PopupKind.ShopSell)
            {
                confirmLbl = $"Bán ×{_popupQty}";
                confirmTint = new Color(255, 224, 140);
            }
            else if (_popupKind == PopupKind.Craft)
            {
                bool cook = _popupCraftRecipe != null && _popupCraftRecipe.isCookingRecipe;
                if (!_popupCanAfford) { confirmLbl = "Thiếu nguyên liệu"; confirmTint = new Color(230, 165, 160); }
                else { confirmLbl = cook ? $"Nấu ×{_popupQty}" : $"Chế tạo ×{_popupQty}"; confirmTint = new Color(178, 242, 165); }
            }
            else if (_popupKind == PopupKind.ChestSplit)
            {
                confirmLbl = $"Chuyển ×{_popupQty}";
                confirmTint = new Color(175, 225, 255);
            }
            else
            {
                confirmLbl = $"Tách ×{_popupQty}";
                confirmTint = new Color(175, 225, 255);
            }
            DrawStardewPixelButton(sb, _popBtnConfirm, confirmLbl, confirmTint, fontScale * 0.98f);
        }

        private void DrawCraftingIngredientsArea(SpriteBatch sb, Texture2D pixel, CraftingPage cp, CraftingRecipe recipe, int x, int y, int w, int h, float fontScale)
        {
            if (recipe == null || recipe.recipeList == null) return;
            float descH = 0f;
            string desc = "";
            try { desc = (recipe.description ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); } catch { }
            if (!string.IsNullOrEmpty(desc) && h > 95)
            {
                int maxDescH = Math.Min((int)(h * 0.36f), Math.Max(34, (int)(54f * fontScale)));
                descH = DrawWrappedStardewText(sb, desc, x, y, w, maxDescH, new Color(86, 46, 22), fontScale * 0.76f) + 6f;
            }

            int ingTop = y + (int)descH;
            int ingAvailH = Math.Max(28, (y + h) - ingTop);
            int count = recipe.recipeList.Count;
            if (count <= 0) return;

            int cols = count >= 3 && w >= 420 ? 2 : 1;
            int rows = (count + cols - 1) / cols;
            int cellH = Math.Clamp(ingAvailH / Math.Max(1, rows), 28, Math.Max(36, (int)(44f * fontScale)));
            int cellW = cols == 2 ? (w - 14) / 2 : w;
            int iconSz = Math.Clamp((int)(cellH * 0.80f), 22, 38);

            IList<Item> extraItems = GetContainerItemsList(cp?._materialContainers);
            int idx = 0;
            foreach (var kv in recipe.recipeList)
            {
                int r = idx / cols;
                int c = idx % cols;
                idx++;
                int cy = ingTop + r * cellH;
                if (cy + iconSz > y + h + 6) break;
                int cx = x + c * (cellW + 14);

                string rawId = kv.Key;
                int need = kv.Value * Math.Max(1, _popupQty);
                int have = CountMatchingCraftIngredients(rawId, extraItems);

                // Subtle recessed parchment strip behind each ingredient row
                sb.Draw(pixel, new Rectangle(cx, cy + 1, cellW, cellH - 3), new Color(185, 132, 72) * 0.18f);

                string spriteId = rawId;
                try { spriteId = recipe.getSpriteIndexFromRawIndex(rawId); } catch { }
                string fallbackDisplayName = null;
                try
                {
                    var pdata = ItemRegistry.GetDataOrErrorItem(spriteId);
                    if (pdata != null)
                    {
                        fallbackDisplayName = pdata.DisplayName;
                        sb.Draw(pdata.GetTexture(), new Rectangle(cx + 4, cy + (cellH - iconSz) / 2, iconSz, iconSz), pdata.GetSourceRect(), Color.White);
                    }
                }
                catch { }

                string ingName = null;
                try { ingName = recipe.getNameFromIndex(rawId); } catch { }
                if (string.IsNullOrEmpty(ingName) || string.Equals(ingName, rawId, StringComparison.Ordinal))
                    ingName = !string.IsNullOrEmpty(fallbackDisplayName) ? fallbackDisplayName : rawId;
                if (ingName.Length > (cols == 2 ? 15 : 26))
                    ingName = ingName.Substring(0, cols == 2 ? 14 : 25) + ".";

                bool enough = have >= need;
                Color cCol = enough ? new Color(34, 118, 42) : new Color(186, 34, 24);
                string countStr = $"{have}/{need}";
                float cScale = fontScale * 0.78f;
                float textTop = cy + Math.Max(2f, (cellH - 22f * cScale) * 0.45f);
                DrawStardewText(sb, ingName, new Vector2(cx + iconSz + 10, textTop), Game1.textColor, cScale);
                float cntW = MeasureWidth(countStr, cScale);
                DrawStardewText(sb, countStr, new Vector2(cx + cellW - cntW - 8, textTop), cCol, cScale);
            }
        }

        private static float MeasureWidth(string text, float scale)
        {
            if (string.IsNullOrEmpty(text) || Game1.smallFont == null) return 0f;
            try { return Game1.smallFont.MeasureString(text).X * scale; } catch { return text.Length * 12f * scale; }
        }

        private static float DrawWrappedStardewText(SpriteBatch sb, string text, int x, int y, int maxW, int maxH, Color color, float scale)
        {
            if (string.IsNullOrEmpty(text) || Game1.smallFont == null || maxH <= 10) return 0f;
            string[] words = text.Split(' ');
            string line = "";
            float curY = y;
            float lh = Math.Max(18f, 26f * scale);
            for (int i = 0; i < words.Length; i++)
            {
                string cand = string.IsNullOrEmpty(line) ? words[i] : (line + " " + words[i]);
                if (MeasureWidth(cand, scale) > maxW && !string.IsNullOrEmpty(line))
                {
                    DrawStardewText(sb, line, new Vector2(x, curY), color, scale);
                    curY += lh;
                    if (curY + lh > y + maxH)
                        return curY - y;
                    line = words[i];
                }
                else
                {
                    line = cand;
                }
            }
            if (!string.IsNullOrEmpty(line) && curY + lh * 0.6f <= y + maxH)
            {
                DrawStardewText(sb, line, new Vector2(x, curY), color, scale);
                curY += lh;
            }
            return curY - y;
        }

        private static void DrawStardewPixelButton(SpriteBatch sb, Rectangle r, string text, Color tint, float scale)
        {
            try
            {
                IClickableMenu.drawTextureBox(sb, Game1.mouseCursors, new Rectangle(432, 439, 9, 9),
                    r.X, r.Y, r.Width, r.Height, tint, 4f, drawShadow: true);
            }
            catch { }

            if (string.IsNullOrEmpty(text) || Game1.smallFont == null)
                return;
            try
            {
                Vector2 sz = Game1.smallFont.MeasureString(text) * scale;
                Vector2 pos = new Vector2(r.X + (r.Width - sz.X) * 0.5f, r.Y + (r.Height - sz.Y) * 0.5f + 2f);
                Utility.drawTextWithShadow(sb, text, Game1.smallFont, pos, Game1.textColor, scale);
            }
            catch { }
        }

        private static void DrawFixedItemTooltipBanner(SpriteBatch sb, Texture2D pixel, Item item, int vpW, int vpH)
        {
            if (item == null || Game1.smallFont == null) return;
            try
            {
                int px = PixelUi.Step(vpH);
                float fScale = Math.Clamp(vpH / 980f, 0.70f, 1.25f);
                string title = item.DisplayName ?? item.Name ?? "";
                string stats = "";
                if (item is StardewValley.Object obj)
                {
                    if (obj.Edibility > 0)
                    {
                        int energy = obj.staminaRecoveredOnConsumption();
                        int health = obj.healthRecoveredOnConsumption();
                        stats = $"+{energy} NL  +{health} Mau";
                    }
                    int sell = 0;
                    try { sell = obj.sellToStorePrice(); } catch { }
                    if (sell > 0)
                        stats = string.IsNullOrEmpty(stats) ? $"{sell:N0}g" : $"{stats}  ·  {sell:N0}g";
                }
                string desc = "";
                try { desc = (item.getDescription() ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); } catch { }
                if (desc.Length > 68) desc = desc.Substring(0, 65) + "...";

                int bw = Math.Clamp((int)(vpW * 0.44f), 280, Math.Min(600, vpW - 40));
                int lineH = Math.Max(18, (int)(26f * fScale));
                int lines = 1 + (string.IsNullOrEmpty(stats) ? 0 : 1) + (string.IsNullOrEmpty(desc) ? 0 : 1);
                int bh = lines * lineH + px * 8;
                // Center along the bottom edge so it NEVER covers the top-right Close 'X' button, Context Bar, or DROP/Trash Can!
                int bx = (vpW - bw) / 2;
                int by = vpH - bh - Math.Max(12, vpH / 42);
                var box = new Rectangle(bx, by, bw, bh);

                PixelUi.DrawParchmentBox(sb, pixel, box, px, 0.94f);
                float tx = box.X + px * 4;
                float ty = box.Y + px * 3;
                DrawStardewText(sb, title, new Vector2(tx, ty), Game1.textColor, fScale * 0.96f);
                ty += lineH;
                if (!string.IsNullOrEmpty(stats))
                {
                    DrawStardewText(sb, stats, new Vector2(tx, ty), new Color(36, 118, 42), fScale * 0.84f);
                    ty += lineH;
                }
                if (!string.IsNullOrEmpty(desc))
                {
                    DrawStardewText(sb, desc, new Vector2(tx, ty), new Color(92, 48, 22), fScale * 0.78f);
                }
            }
            catch { }
        }

        private static void DrawStardewText(SpriteBatch sb, string text, Vector2 pos, Color color, float scale)
        {
            if (string.IsNullOrEmpty(text) || Game1.smallFont == null) return;
            try
            {
                Utility.drawTextWithShadow(sb, text, Game1.smallFont, pos, color, scale);
            }
            catch { }
        }

        private static Rectangle FindHoveredSlotRect(InventoryMenu a, InventoryMenu b, Point uiPt)
        {
            if (a != null)
            {
                int s = FindSlotAt(a, uiPt);
                if (s >= 0) return a.inventory[s].bounds;
            }
            if (b != null)
            {
                int s = FindSlotAt(b, uiPt);
                if (s >= 0) return b.inventory[s].bounds;
            }
            return Rectangle.Empty;
        }

        private static Rectangle UiRectToVp(Rectangle uiRect, Func<Point, Vector2> uiToVp)
        {
            Vector2 tl = uiToVp(new Point(uiRect.X, uiRect.Y));
            Vector2 br = uiToVp(new Point(uiRect.Right, uiRect.Bottom));
            return new Rectangle((int)tl.X, (int)tl.Y, Math.Max(4, (int)(br.X - tl.X)), Math.Max(4, (int)(br.Y - tl.Y)));
        }

        private static void DrawBorder(SpriteBatch sb, Texture2D pixel, Rectangle r, int t, Color c)
        {
            sb.Draw(pixel, new Rectangle(r.X, r.Y, r.Width, t), c);
            sb.Draw(pixel, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
            sb.Draw(pixel, new Rectangle(r.X, r.Y, t, r.Height), c);
            sb.Draw(pixel, new Rectangle(r.Right - t, r.Y, t, r.Height), c);
        }
    }
}
