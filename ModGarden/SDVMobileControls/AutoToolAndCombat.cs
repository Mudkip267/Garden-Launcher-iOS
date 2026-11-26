using System;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Buildings;
using StardewValley.GameData.Buildings;
using StardewValley.GameData.Crops;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace SDVMobileControls
{
    internal enum TileActionKind
    {
        None = 0,
        UseTool = 1,   // ButtonX (Pickaxe, Axe, Hoe, WateringCan, Scythe, Weapon)
        Interact = 2   // ButtonA (NPC, Chest, Door, Harvest, Forage, Machine, Plant, ShippingBin, Furniture, MineLadder)
    }

    internal struct TileIntent
    {
        public TileActionKind Kind;
        public int PreferredToolSlot; // -1 = keep current tool
        public bool StopAdjacent;
        public bool Repeatable;       // Auto-repeat 1-tap chop/mine/fight until broken or defeated
        public bool PreferCardinal;   // Walk squarely onto a cardinal tile (N/S/E/W) for Mailboxes, Doors, Buildings
        public bool AllowSameTile;    // Allow triggering when standing directly on target tile (cheb == 0: crops, monsters, ladders, beds)
        public bool HasResolvedTile;  // True when tapping a tall sprite or multi-tile building -> resolves to nearest actionable base tile
        public Point ResolvedTile;
    }

    internal static class AutoToolAndCombat
    {
        public static TileIntent InspectTile(GameLocation loc, Point tile, Farmer who, bool autoTool)
        {
            var intent = new TileIntent { Kind = TileActionKind.None, PreferredToolSlot = -1, StopAdjacent = false };
            if (loc == null || who == null)
                return intent;

            Vector2 tv = new Vector2(tile.X, tile.Y);
            Vector2 tileCenter = new Vector2(tile.X * 64f + 32f, tile.Y * 64f + 32f);

            // 0. Playable Event / Festival support (Game1.eventUp && CurrentEvent.playerControlSequence):
            //    During festivals (Egg Festival, Luau, Fair, Spirit's Eve, Feast of the Winter Star, Ice Festival…),
            //    villagers live in CurrentEvent.actors and interactive props/eggs live in CurrentEvent.props!
            if (Game1.eventUp)
            {
                try
                {
                    Event ev = Game1.CurrentEvent;
                    if (ev != null && ev.playerControlSequence)
                    {
                        if (ev.actors != null)
                        {
                            NPC evHitNpc = null;
                            float bestEvNpcD = 78f;
                            for (int i = 0; i < ev.actors.Count; i++)
                            {
                                NPC npc = ev.actors[i];
                                if (npc == null || npc is Monster)
                                    continue;
                                Rectangle nbb = npc.GetBoundingBox();
                                Rectangle nSprite = new Rectangle(nbb.X - 12, nbb.Y - 48, nbb.Width + 24, nbb.Height + 56);
                                float d = nSprite.Contains((int)tileCenter.X, (int)tileCenter.Y)
                                    ? 0f
                                    : Vector2.Distance(nbb.Center.ToVector2(), tileCenter);
                                if (d < bestEvNpcD)
                                {
                                    bestEvNpcD = d;
                                    evHitNpc = npc;
                                }
                            }
                            if (evHitNpc != null)
                            {
                                Rectangle nbb = evHitNpc.GetBoundingBox();
                                intent.Kind = TileActionKind.Interact;
                                intent.StopAdjacent = true;
                                intent.AllowSameTile = true;
                                intent.HasResolvedTile = true;
                                intent.ResolvedTile = new Point((int)Math.Floor(nbb.Center.X / 64f), (int)Math.Floor(nbb.Center.Y / 64f));
                                return intent;
                            }
                        }

                        if (ev.props != null)
                        {
                            for (int i = 0; i < ev.props.Count; i++)
                            {
                                SObject prop = ev.props[i];
                                if (prop != null && prop.TileLocation == tv)
                                {
                                    intent.Kind = TileActionKind.Interact;
                                    intent.StopAdjacent = true;
                                    intent.AllowSameTile = true;
                                    return intent;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(loc.doesTileHaveProperty(tile.X, tile.Y, "Action", "Buildings")))
                        {
                            intent.Kind = TileActionKind.Interact;
                            intent.StopAdjacent = true;
                            intent.PreferCardinal = true;
                            return intent;
                        }

                        // Unless the festival explicitly enables tools (e.g. Ice Festival fishing contest), skip normal world tool targets
                        if (!ev.canPlayerUseTool())
                            return intent;
                    }
                }
                catch { }
            }

            // 1. Living monster near tapped tile
            Monster tappedMonster = FindMonsterNear(loc, tileCenter, 78f);
            if (tappedMonster != null)
            {
                intent.Kind = TileActionKind.UseTool;
                intent.StopAdjacent = true;
                intent.Repeatable = true;
                intent.AllowSameTile = true;
                if (autoTool)
                    intent.PreferredToolSlot = FindBestWeaponSlot(who);
                return intent;
            }

            // 2. NPC (including Dog/Cat Pet!) near tapped tile -> talk / gift / pet
            if (loc.characters != null)
            {
                NPC hitNpc = null;
                float bestNpcD = 78f;
                foreach (NPC npc in loc.characters)
                {
                    if (npc == null || npc is Monster)
                        continue;
                    Rectangle nbb = npc.GetBoundingBox();
                    // Also cover upper body/head of standing NPCs & Pets (48px above feet!)
                    Rectangle nSprite = new Rectangle(nbb.X - 12, nbb.Y - 48, nbb.Width + 24, nbb.Height + 56);
                    float d = nSprite.Contains((int)tileCenter.X, (int)tileCenter.Y)
                        ? 0f
                        : Vector2.Distance(nbb.Center.ToVector2(), tileCenter);
                    if (d < bestNpcD)
                    {
                        bestNpcD = d;
                        hitNpc = npc;
                    }
                }
                if (hitNpc != null)
                {
                    Rectangle nbb = hitNpc.GetBoundingBox();
                    intent.Kind = TileActionKind.Interact;
                    intent.StopAdjacent = true;
                    intent.AllowSameTile = true;
                    intent.HasResolvedTile = true;
                    intent.ResolvedTile = new Point((int)Math.Floor(nbb.Center.X / 64f), (int)Math.Floor(nbb.Center.Y / 64f));
                    return intent;
                }
            }

            // 2b. FarmAnimal near tapped tile -> pet first (using full sprite/cursor hitbox!), then auto-select Milk Pail / Shears if ready!
            try
            {
                if (loc.Animals != null && loc.Animals.Length > 0)
                {
                    FarmAnimal hitAnimal = null;
                    float bestD = 88f;
                    foreach (FarmAnimal animal in loc.Animals.Values)
                    {
                        if (animal == null) continue;
                        Rectangle bb = animal.GetBoundingBox();
                        Rectangle petBox = animal.GetCursorPetBoundingBox();
                        petBox.Inflate(16, 16);
                        float d = (bb.Contains((int)tileCenter.X, (int)tileCenter.Y) || petBox.Contains((int)tileCenter.X, (int)tileCenter.Y))
                            ? 0f
                            : Vector2.Distance(bb.Center.ToVector2(), tileCenter);
                        if (d < bestD)
                        {
                            bestD = d;
                            hitAnimal = animal;
                        }
                    }
                    if (hitAnimal != null)
                    {
                        Rectangle bb = hitAnimal.GetBoundingBox();
                        intent.StopAdjacent = true;
                        intent.AllowSameTile = true;
                        intent.HasResolvedTile = true;
                        intent.ResolvedTile = new Point((int)Math.Floor(bb.Center.X / 64f), (int)Math.Floor(bb.Center.Y / 64f));
                        if (!hitAnimal.wasPet.Value)
                        {
                            intent.Kind = TileActionKind.Interact;
                            return intent;
                        }
                        if (autoTool && !string.IsNullOrEmpty(hitAnimal.currentProduce.Value))
                        {
                            int mpSlot = FindToolSlot<MilkPail>(who);
                            if (mpSlot >= 0 && who.Items[mpSlot] is MilkPail mp && hitAnimal.CanGetProduceWithTool(mp))
                            {
                                intent.Kind = TileActionKind.UseTool;
                                intent.PreferredToolSlot = mpSlot;
                                return intent;
                            }
                            int shSlot = FindToolSlot<Shears>(who);
                            if (shSlot >= 0 && who.Items[shSlot] is Shears sh && hitAnimal.CanGetProduceWithTool(sh))
                            {
                                intent.Kind = TileActionKind.UseTool;
                                intent.PreferredToolSlot = shSlot;
                                return intent;
                            }
                        }
                        intent.Kind = TileActionKind.Interact;
                        return intent;
                    }
                }
            }
            catch { }

            // 3. ResourceClump (stump, hollow log, boulder, meteorite, mine rock)
            if (loc.resourceClumps != null)
            {
                foreach (ResourceClump rc in loc.resourceClumps)
                {
                    if (rc != null && rc.occupiesTile(tile.X, tile.Y))
                    {
                        int idx = rc.parentSheetIndex.Value;
                        bool isWood = idx == ResourceClump.stumpIndex ||
                                      idx == ResourceClump.hollowLogIndex ||
                                      idx == ResourceClump.greenRainBush1Index ||
                                      idx == ResourceClump.greenRainBush2Index;
                        int toolSlot = autoTool
                            ? (isWood ? FindToolSlot<Axe>(who) : FindToolSlot<Pickaxe>(who))
                            : ((isWood ? (who.CurrentTool is Axe) : (who.CurrentTool is Pickaxe)) ? who.CurrentToolIndex : -1);
                        if (toolSlot < 0)
                            return intent;

                        int reqUpgrade = 0;
                        if (idx == ResourceClump.stumpIndex) reqUpgrade = 1;       // Copper Axe
                        else if (idx == ResourceClump.hollowLogIndex) reqUpgrade = 2; // Steel Axe
                        else if (idx == ResourceClump.boulderIndex) reqUpgrade = 2;   // Steel Pickaxe
                        else if (idx == ResourceClump.meteoriteIndex) reqUpgrade = 3; // Gold Pickaxe

                        Tool chosen = (toolSlot >= 0 && toolSlot < who.Items.Count) ? who.Items[toolSlot] as Tool : null;
                        bool strongEnough = chosen != null && chosen.UpgradeLevel >= reqUpgrade;

                        intent.Kind = TileActionKind.UseTool;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = true;
                        intent.Repeatable = strongEnough; // 1 feedback swing only if tool tier is too low!
                        intent.PreferredToolSlot = autoTool ? toolSlot : -1;
                        return intent;
                    }
                }
            }

            // 4. Objects on tile (chests, machines, breakable mine barrels, stones, twigs, weeds, forageables, artifact spots)
            if (loc.objects != null && loc.objects.TryGetValue(tv, out SObject obj) && obj != null)
            {
                if (obj is BreakableContainer)
                {
                    int slot = autoTool ? FindScytheOrWeaponSlot(who) : -1;
                    if (autoTool && slot < 0) slot = FindToolSlot<Pickaxe>(who);
                    if (autoTool && slot < 0) slot = FindToolSlot<Axe>(who);
                    intent.Kind = TileActionKind.UseTool;
                    intent.StopAdjacent = true;
                    intent.Repeatable = false;
                    intent.PreferredToolSlot = autoTool ? slot : -1;
                    return intent;
                }

                if (obj is Chest || obj is Furniture || obj.readyForHarvest.Value || obj.IsSpawnedObject)
                {
                    intent.Kind = TileActionKind.Interact;
                    intent.StopAdjacent = true;
                    intent.AllowSameTile = obj.IsSpawnedObject;
                    return intent;
                }

                if (obj.QualifiedItemId == "(O)590" || obj.QualifiedItemId == "(O)SeedSpot")
                {
                    int hoeSlot = autoTool ? FindToolSlot<Hoe>(who) : (who.CurrentTool is Hoe ? who.CurrentToolIndex : -1);
                    if (hoeSlot < 0) return intent;
                    intent.Kind = TileActionKind.UseTool;
                    intent.StopAdjacent = true;
                    intent.PreferCardinal = true;
                    intent.PreferredToolSlot = autoTool ? hoeSlot : -1;
                    return intent;
                }

                if (obj.IsBreakableStone() || obj.Name == "Stone" || obj.QualifiedItemId == "(O)25")
                {
                    int pickSlot = autoTool ? FindToolSlot<Pickaxe>(who) : (who.CurrentTool is Pickaxe ? who.CurrentToolIndex : -1);
                    if (pickSlot < 0) return intent;
                    intent.Kind = TileActionKind.UseTool;
                    intent.StopAdjacent = true;
                    intent.PreferCardinal = true;
                    intent.Repeatable = true;
                    intent.PreferredToolSlot = autoTool ? pickSlot : -1;
                    return intent;
                }

                if (obj.IsTwig() || obj.Name == "Twig")
                {
                    int axeSlot = autoTool ? FindToolSlot<Axe>(who) : (who.CurrentTool is Axe ? who.CurrentToolIndex : -1);
                    if (axeSlot < 0) return intent;
                    intent.Kind = TileActionKind.UseTool;
                    intent.StopAdjacent = true;
                    intent.PreferCardinal = true;
                    intent.Repeatable = false; // Twigs break in 1 hit — no wasted follow-up swing!
                    intent.PreferredToolSlot = autoTool ? axeSlot : -1;
                    return intent;
                }

                if (obj.IsWeeds())
                {
                    intent.Kind = TileActionKind.UseTool;
                    intent.StopAdjacent = true;
                    intent.AllowSameTile = true;
                    if (autoTool)
                        intent.PreferredToolSlot = FindScytheOrWeaponSlot(who);
                    return intent;
                }

                // Machines / craftables / signs / beds / shipping bins
                if (obj.bigCraftable.Value)
                {
                    intent.Kind = TileActionKind.Interact;
                    intent.StopAdjacent = true;
                    return intent;
                }
            }

            // 4b. Placed Interior / Exterior Furniture (TV, Bed, Dresser, FishTank, Catalogue, Lamp, Fireplace, Chair, Table)
            // Stardew stores placed furniture in loc.furniture (NOT loc.objects!). Skip rugs so walking across rugs works normally!
            try
            {
                if (loc.furniture != null && loc.furniture.Count > 0)
                {
                    Furniture hitFurn = null;
                    for (int fi = 0; fi < loc.furniture.Count; fi++)
                    {
                        Furniture f = loc.furniture[fi];
                        if (f == null || f.furniture_type.Value == Furniture.rug)
                            continue;
                        Rectangle fbb = f.boundingBox.Value;
                        bool directHit = fbb.Contains((int)tileCenter.X, (int)tileCenter.Y);
                        bool tallHit = !directHit &&
                            (f is TV || f is StorageFurniture || f is FishTankFurniture || f is BedFurniture ||
                             f.furniture_type.Value == Furniture.lamp || f.furniture_type.Value == Furniture.fireplace ||
                             f.furniture_type.Value == Furniture.bookcase) &&
                            fbb.Contains((int)tileCenter.X, (int)(tileCenter.Y + 64f));
                        if (!directHit && !tallHit)
                            continue;

                        bool interactive =
                            (f is TV) ||
                            (f is BedFurniture) ||
                            (f is StorageFurniture) ||
                            (f is FishTankFurniture) ||
                            f.furniture_type.Value == Furniture.lamp ||
                            f.furniture_type.Value == Furniture.fireplace ||
                            f.furniture_type.Value == Furniture.chair ||
                            f.furniture_type.Value == Furniture.bench ||
                            f.furniture_type.Value == Furniture.couch ||
                            f.furniture_type.Value == Furniture.armchair ||
                            f.furniture_type.Value == Furniture.table ||
                            f.furniture_type.Value == Furniture.longTable ||
                            f.furniture_type.Value == Furniture.other ||
                            (f.Name != null && (
                                f.Name.IndexOf("Catalog", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                f.Name.IndexOf("Phone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                f.Name.IndexOf("Calendar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                f.Name.IndexOf("Jukebox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                f.Name.IndexOf("Coffee", StringComparison.OrdinalIgnoreCase) >= 0));
                        if (interactive)
                        {
                            hitFurn = f;
                            break;
                        }
                    }

                    if (hitFurn != null)
                    {
                        Point bestTile = FindClosestRectTile(hitFurn.boundingBox.Value, who.TilePoint);
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = false;
                        intent.AllowSameTile = (hitFurn is BedFurniture || hitFurn.furniture_type.Value == Furniture.chair ||
                                                hitFurn.furniture_type.Value == Furniture.bench || hitFurn.furniture_type.Value == Furniture.couch ||
                                                hitFurn.furniture_type.Value == Furniture.armchair);
                        intent.HasResolvedTile = true;
                        intent.ResolvedTile = bestTile;
                        return intent;
                    }
                }
            }
            catch { }

            // 5. TerrainFeatures (Trees, FruitTrees, HoeDirt crops, Grass)
            if (loc.terrainFeatures != null && loc.terrainFeatures.TryGetValue(tv, out TerrainFeature tf) && tf != null)
            {
                if (tf is Tree tree)
                {
                    // Stop immediately while the tree is already falling (prevents 2-3 wasted air swings!)
                    if (tree.falling.Value)
                        return intent;

                    // Protect tapped trees from accidental Tap-to-Move chopping!
                    if (tree.tapped.Value)
                        return intent;

                    // Newly planted tree seed (stage 0): only dig up if player is already holding Axe/Pickaxe/Hoe (1 swing, no auto-repeat)
                    if (tree.growthStage.Value == 0)
                    {
                        if (who.CurrentTool is Axe || who.CurrentTool is Pickaxe || who.CurrentTool is Hoe)
                        {
                            intent.Kind = TileActionKind.UseTool;
                            intent.StopAdjacent = true;
                            intent.PreferCardinal = true;
                            intent.Repeatable = false;
                        }
                        return intent;
                    }

                    int axeSlot = autoTool ? FindToolSlot<Axe>(who) : (who.CurrentTool is Axe ? who.CurrentToolIndex : -1);
                    if (axeSlot >= 0)
                    {
                        intent.Kind = TileActionKind.UseTool;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = true;
                        intent.Repeatable = true;
                        intent.PreferredToolSlot = autoTool ? axeSlot : -1;
                        return intent;
                    }

                    // No Axe available: shake mature tree if it has a seed/item instead of whiffing wrong tools
                    if (!tree.stump.Value && tree.growthStage.Value >= 5)
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = true;
                    }
                    return intent;
                }

                if (tf is FruitTree ft)
                {
                    if (ft.fruit.Count > 0)
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = true;
                        return intent;
                    }
                    // Protect prized Fruit Trees from accidental chopping: only swing 1x if player explicitly holds an Axe
                    if (who.CurrentTool is Axe)
                    {
                        intent.Kind = TileActionKind.UseTool;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = true;
                        intent.Repeatable = false;
                    }
                    return intent;
                }

                if (tf is HoeDirt hd)
                {
                    if (hd.crop != null)
                    {
                        if (hd.readyForHarvest())
                        {
                            bool scytheHarvest = hd.crop.GetHarvestMethod() == HarvestMethod.Scythe;
                            if (scytheHarvest)
                            {
                                intent.Kind = TileActionKind.UseTool;
                                intent.StopAdjacent = true;
                                intent.AllowSameTile = true;
                                if (autoTool)
                                    intent.PreferredToolSlot = FindScytheOrWeaponSlot(who);
                            }
                            else
                            {
                                intent.Kind = TileActionKind.Interact;
                                intent.StopAdjacent = true;
                                intent.AllowSameTile = true;
                            }
                            return intent;
                        }

                        // Unwatered growing crop -> WateringCan
                        if (hd.state.Value == 0 && !hd.crop.dead.Value)
                        {
                            int wc = autoTool ? FindToolSlot<WateringCan>(who) : -1;
                            if (wc >= 0 || who.CurrentTool is WateringCan)
                            {
                                intent.Kind = TileActionKind.UseTool;
                                intent.StopAdjacent = true;
                                intent.AllowSameTile = true;
                                intent.PreferredToolSlot = wc;
                                return intent;
                            }
                        }

                        if (hd.crop.dead.Value)
                        {
                            intent.Kind = TileActionKind.UseTool;
                            intent.StopAdjacent = true;
                            intent.AllowSameTile = true;
                            if (autoTool)
                                intent.PreferredToolSlot = FindScytheOrWeaponSlot(who);
                            return intent;
                        }
                    }
                    else if (who.ActiveObject != null && (who.ActiveObject.Category == SObject.SeedsCategory || who.ActiveObject.Category == SObject.fertilizerCategory))
                    {
                        // Holding seeds or fertilizer over empty tilled soil -> plant/fertilize
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.AllowSameTile = true;
                        return intent;
                    }
                }

                if (tf is Grass)
                {
                    // Walk through grass normally unless player is already holding Scythe
                    if (who.CurrentTool is MeleeWeapon mw && mw.isScythe())
                    {
                        intent.Kind = TileActionKind.UseTool;
                        intent.StopAdjacent = true;
                        intent.AllowSameTile = true;
                        return intent;
                    }
                }
            }

            // 6. Bushes with ripe berries
            if (loc.largeTerrainFeatures != null)
            {
                foreach (LargeTerrainFeature ltf in loc.largeTerrainFeatures)
                {
                    if (ltf is Bush bush && bush.getBoundingBox().Contains((int)tileCenter.X, (int)tileCenter.Y))
                    {
                        if (bush.inBloom())
                        {
                            intent.Kind = TileActionKind.Interact;
                            intent.StopAdjacent = true;
                            return intent;
                        }
                    }
                }
            }

            // 7. Shimmering Ore Pan Spot -> auto-select Pan, or Water tile -> FishingRod / refill WateringCan
            try
            {
                Point panPt = loc.orePanPoint.Value;
                if (panPt != Point.Zero && Math.Max(Math.Abs(panPt.X - tile.X), Math.Abs(panPt.Y - tile.Y)) <= 1)
                {
                    int panSlot = autoTool ? FindToolSlot<Pan>(who) : (who.CurrentTool is Pan ? who.CurrentToolIndex : -1);
                    if (panSlot >= 0)
                    {
                        intent.Kind = TileActionKind.UseTool;
                        intent.StopAdjacent = true;
                        intent.PreferredToolSlot = panSlot;
                        return intent;
                    }
                }

                if (loc.isWaterTile(tile.X, tile.Y))
                {
                    if (who.ActiveObject is CrabPot)
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = false;
                        return intent;
                    }
                    if (who.CurrentTool is FishingRod)
                    {
                        // Do NOT auto-fire a 0%-power stub cast on short tap/arrival!
                        // Press-and-hold on water starts instant FishingRod charging in MobileControlsEngine;
                        // tapping distant water walks neatly to the shoreline ready to hold-charge.
                        intent.Kind = TileActionKind.None;
                        intent.StopAdjacent = true;
                        return intent;
                    }
                    if (autoTool)
                    {
                        int wcSlot = FindToolSlot<WateringCan>(who);
                        if (wcSlot >= 0 && who.Items[wcSlot] is WateringCan wc && wc.WaterLeft < wc.waterCanMax)
                        {
                            intent.Kind = TileActionKind.UseTool;
                            intent.StopAdjacent = true;
                            intent.PreferredToolSlot = wcSlot;
                            return intent;
                        }
                    }
                }
            }
            catch { }

            // 8. Building / Action tile (Mailboxes + floating letter envelope, Farm Buildings, Doors, TV, Bed, Calendar, Shipping Bin, Shop Counters)
            try
            {
                // 8a-0. Universal Minecarts (BusStop, Town, Quarry/Mountain, Mine Entrance, modded MinecartTransport, upper-half sprites),
                //       Ginger Island Parrot Express / Walnut Perches, and Vanilla 1.6 Hardcoded Tile-Index Actions!
                if (TryResolveMinecartOrLocationTileAction(loc, tile, who, ref intent))
                    return intent;

                // 8a. Farm main mailbox (Stardew 1.6 stores Farmhouse & Cabins as Buildings — Mailbox is NOT in map "Action" property!)
                // Also match 1 tile ABOVE the mailbox so tapping the bobbing white letter envelope opens the mail!
                if (loc is Farm farm)
                {
                    Point mainMb = farm.GetMainMailboxPosition();
                    if (mainMb.X >= 0 && mainMb.Y >= 0 &&
                        ((tile.X == mainMb.X && tile.Y == mainMb.Y) || (tile.X == mainMb.X && tile.Y == mainMb.Y - 1)))
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = true;
                        intent.HasResolvedTile = true;
                        intent.ResolvedTile = mainMb;
                        return intent;
                    }
                }

                // 8b. All Stardew 1.6 Buildings (Farmhouse, Cabin, Coop, Barn, Shed, Greenhouse, Mailbox, ShippingBin, PetBowl, FishPond, Mill, Silo, Well, Obelisks, GoldClock, JunimoHut, animal doors)
                if (loc.buildings != null)
                {
                    for (int i = 0; i < loc.buildings.Count; i++)
                    {
                        var b = loc.buildings[i];
                        if (b == null) continue;

                        Point bMb = b.getMailboxPosition();
                        if (bMb.X >= 0 && bMb.Y >= 0 &&
                            ((tile.X == bMb.X && tile.Y == bMb.Y) || (tile.X == bMb.X && tile.Y == bMb.Y - 1)))
                        {
                            intent.Kind = TileActionKind.Interact;
                            intent.StopAdjacent = true;
                            intent.PreferCardinal = true;
                            intent.HasResolvedTile = true;
                            intent.ResolvedTile = bMb;
                            return intent;
                        }

                        // Building human doors (Farmhouse, Cabin, Coop, Barn, Shed, Greenhouse, Slime Hutch) are entered
                        // via Building.doAction (loc.checkAction), NOT walk-in loc.warps! Match both door tile AND 1 tile above (tall door sprite).
                        if (b.HasIndoors())
                        {
                            Point humanDoor = b.getPointForHumanDoor();
                            if (humanDoor.X >= 0 && humanDoor.Y >= 0 &&
                                ((tile.X == humanDoor.X && tile.Y == humanDoor.Y) ||
                                 (tile.X == humanDoor.X && tile.Y == humanDoor.Y - 1)))
                            {
                                intent.Kind = TileActionKind.Interact;
                                intent.StopAdjacent = true;
                                intent.PreferCardinal = true;
                                intent.HasResolvedTile = true;
                                intent.ResolvedTile = humanDoor;
                                return intent;
                            }
                        }

                        // ShippingBin overrides doAction() WITHOUT overriding isActionableTile()!
                        // Match both base tiles (tileX..tileX+tilesWide-1, tileY..tileY+tilesHigh-1) AND lid row (tileY - 1),
                        // resolving to the closest base tile to the farmer so 1 tap opens the Shipping Bin cleanly from any angle!
                        if (b is ShippingBin sb && sb.daysOfConstructionLeft.Value <= 0)
                        {
                            int sx = sb.tileX.Value;
                            int sy = sb.tileY.Value;
                            int sw = Math.Max(1, sb.tilesWide.Value);
                            int sh = Math.Max(1, sb.tilesHigh.Value);
                            bool hitBase = tile.X >= sx && tile.X < sx + sw && tile.Y >= sy && tile.Y < sy + sh;
                            bool hitLid  = tile.X >= sx && tile.X < sx + sw && tile.Y == sy - 1;
                            if (hitBase || hitLid)
                            {
                                intent.Kind = TileActionKind.Interact;
                                intent.StopAdjacent = true;
                                intent.PreferCardinal = false;
                                intent.HasResolvedTile = true;
                                intent.ResolvedTile = FindClosestBuildingTile(sb, who.TilePoint);
                                return intent;
                            }
                        }

                        // FishPond overrides doAction() without overriding isActionableTile()!
                        if (b is FishPond fp && fp.daysOfConstructionLeft.Value <= 0)
                        {
                            bool hitPond = fp.occupiesTile(tv) ||
                                (!PathFinder.IsTileWalkable(loc, tile.X, tile.Y, who) && fp.occupiesTile(new Vector2(tile.X, tile.Y + 1)));
                            if (hitPond)
                            {
                                intent.Kind = TileActionKind.Interact;
                                intent.StopAdjacent = true;
                                intent.PreferCardinal = false;
                                intent.HasResolvedTile = true;
                                intent.ResolvedTile = FindClosestBuildingTile(fp, who.TilePoint);
                                return intent;
                            }
                        }

                        // JunimoHut overrides doAction() without overriding isActionableTile()!
                        if (b is JunimoHut jh && jh.daysOfConstructionLeft.Value <= 0 && jh.occupiesTile(tv))
                        {
                            intent.Kind = TileActionKind.Interact;
                            intent.StopAdjacent = true;
                            intent.PreferCardinal = false;
                            intent.HasResolvedTile = true;
                            intent.ResolvedTile = FindClosestBuildingTile(jh, who.TilePoint);
                            return intent;
                        }

                        // PetBowl: auto-fill with WateringCan if dry, otherwise show pet name via doAction!
                        if (b is PetBowl pb && pb.daysOfConstructionLeft.Value <= 0 && pb.occupiesTile(tv))
                        {
                            Point pbTile = FindClosestBuildingTile(pb, who.TilePoint);
                            if (!pb.watered.Value)
                            {
                                int wcSlot = autoTool ? FindToolSlot<WateringCan>(who) : (who.CurrentTool is WateringCan ? who.CurrentToolIndex : -1);
                                if (wcSlot >= 0 && who.Items[wcSlot] is WateringCan wc && wc.WaterLeft > 0)
                                {
                                    intent.Kind = TileActionKind.UseTool;
                                    intent.StopAdjacent = true;
                                    intent.PreferCardinal = true;
                                    intent.PreferredToolSlot = autoTool ? wcSlot : -1;
                                    intent.HasResolvedTile = true;
                                    intent.ResolvedTile = pbTile;
                                    return intent;
                                }
                            }
                            intent.Kind = TileActionKind.Interact;
                            intent.StopAdjacent = true;
                            intent.PreferCardinal = false;
                            intent.HasResolvedTile = true;
                            intent.ResolvedTile = pbTile;
                            return intent;
                        }

                        // Well / WaterSource building -> auto-refill WateringCan when not full!
                        if (b.daysOfConstructionLeft.Value <= 0 && b.occupiesTile(tv))
                        {
                            string waterSrc = null;
                            if (b.doesTileHaveProperty(tile.X, tile.Y, "WaterSource", "Buildings", ref waterSrc))
                            {
                                int wcSlot = autoTool ? FindToolSlot<WateringCan>(who) : (who.CurrentTool is WateringCan ? who.CurrentToolIndex : -1);
                                if (wcSlot >= 0 && who.Items[wcSlot] is WateringCan wc && wc.WaterLeft < wc.waterCanMax)
                                {
                                    intent.Kind = TileActionKind.UseTool;
                                    intent.StopAdjacent = true;
                                    intent.PreferCardinal = true;
                                    intent.PreferredToolSlot = autoTool ? wcSlot : -1;
                                    return intent;
                                }
                            }
                        }

                        if (b.isActionableTile(tile.X, tile.Y, who))
                        {
                            intent.Kind = TileActionKind.Interact;
                            intent.StopAdjacent = true;
                            intent.PreferCardinal = true;
                            return intent;
                        }

                        // Tapping any tile of a non-indoors actionable building (Silo, Mill, Obelisk, Gold Clock, etc.):
                        // resolve neatly to its closest defined ActionTile so players don't have to pixel-hunt 1 tile!
                        if (b.daysOfConstructionLeft.Value <= 0 && !b.HasIndoors() && b.occupiesTile(tv))
                        {
                            BuildingData bData = b.GetData();
                            if (bData?.ActionTiles != null && bData.ActionTiles.Count > 0)
                            {
                                Point bestAct = Point.Zero;
                                int bestDist = int.MaxValue;
                                bool foundAct = false;
                                for (int ai = 0; ai < bData.ActionTiles.Count; ai++)
                                {
                                    var at = bData.ActionTiles[ai];
                                    if (at == null || string.IsNullOrEmpty(at.Action)) continue;
                                    int ax = b.tileX.Value + at.Tile.X;
                                    int ay = b.tileY.Value + at.Tile.Y;
                                    int d = Math.Abs(ax - who.TilePoint.X) + Math.Abs(ay - who.TilePoint.Y);
                                    if (d < bestDist)
                                    {
                                        bestDist = d;
                                        bestAct = new Point(ax, ay);
                                        foundAct = true;
                                    }
                                }
                                if (foundAct)
                                {
                                    intent.Kind = TileActionKind.Interact;
                                    intent.StopAdjacent = true;
                                    intent.PreferCardinal = false;
                                    intent.HasResolvedTile = true;
                                    intent.ResolvedTile = bestAct;
                                    return intent;
                                }
                            }
                        }
                    }
                }

                string actionProp = loc.doesTileHaveProperty(tile.X, tile.Y, "Action", "Buildings");
                if (!string.IsNullOrEmpty(actionProp))
                {
                    intent.Kind = TileActionKind.Interact;
                    intent.StopAdjacent = true;
                    intent.PreferCardinal = true;
                    return intent;
                }

                // Tapping the upper half of a 2-tile-tall Town/Shop/House door (or Mailbox):
                // resolve cleanly to the bottom Action tile (tile.X, tile.Y + 1) when tapped tile itself is blocked wall/doorframe!
                if (!PathFinder.IsTileWalkable(loc, tile.X, tile.Y, who))
                {
                    string belowAction = loc.doesTileHaveProperty(tile.X, tile.Y + 1, "Action", "Buildings");
                    if (!string.IsNullOrEmpty(belowAction) &&
                        (belowAction.StartsWith("LockedDoorWarp", StringComparison.OrdinalIgnoreCase) ||
                         belowAction.StartsWith("Door", StringComparison.OrdinalIgnoreCase) ||
                         belowAction.StartsWith("Warp", StringComparison.OrdinalIgnoreCase) ||
                         belowAction.StartsWith("Enter", StringComparison.OrdinalIgnoreCase) ||
                         belowAction.StartsWith("Mailbox", StringComparison.OrdinalIgnoreCase) ||
                         belowAction.StartsWith("Mine", StringComparison.OrdinalIgnoreCase)))
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = true;
                        intent.HasResolvedTile = true;
                        intent.ResolvedTile = new Point(tile.X, tile.Y + 1);
                        return intent;
                    }
                }
            }
            catch { }

            // 8c. Tapping the vertical trunk 1 tile ABOVE a mature standing tree's base (tile.X, tile.Y + 1):
            // resolve cleanly to the tree base so players can tap the trunk directly without walking behind the tree!
            try
            {
                Vector2 belowTv = new Vector2(tile.X, tile.Y + 1);
                if (loc.terrainFeatures != null &&
                    loc.terrainFeatures.TryGetValue(belowTv, out TerrainFeature belowTf) &&
                    belowTf is Tree tallTree &&
                    tallTree.growthStage.Value >= 5 &&
                    !tallTree.stump.Value &&
                    !tallTree.falling.Value &&
                    !tallTree.tapped.Value)
                {
                    int axeSlot = autoTool ? FindToolSlot<Axe>(who) : (who.CurrentTool is Axe ? who.CurrentToolIndex : -1);
                    if (axeSlot >= 0)
                    {
                        intent.Kind = TileActionKind.UseTool;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = true;
                        intent.Repeatable = true;
                        intent.PreferredToolSlot = autoTool ? axeSlot : -1;
                        intent.HasResolvedTile = true;
                        intent.ResolvedTile = new Point(tile.X, tile.Y + 1);
                        return intent;
                    }
                }
            }
            catch { }

            // 8d. MineShaft / Skull Cavern Ladders (173), Shafts/Holes (174), Exit Ladder (115), Elevator (112), Coal Carts (194), Shrine (284), 1.6 Calico Statues (315..317)
            // MineShaft.checkAction reads Buildings layer tile indices directly (NOT map "Action" properties!).
            if (loc is MineShaft)
            {
                try
                {
                    int mIdx = loc.getTileIndexAt(tile.X, tile.Y, "Buildings", "mine");
                    if (mIdx == 173 || mIdx == 174 || mIdx == 112 || mIdx == 115 || mIdx == 194 || mIdx == 284 || (mIdx >= 315 && mIdx <= 317))
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = (mIdx == 112 || mIdx == 115);
                        intent.AllowSameTile = (mIdx == 173 || mIdx == 174);
                        return intent;
                    }
                    // Upper half (Front layer sprite) of tall exit ladder (115), elevator (112), coal cart/bag (194), shrine (284), or Calico Statue (315..317)
                    int belowIdx = loc.getTileIndexAt(tile.X, tile.Y + 1, "Buildings", "mine");
                    if (belowIdx == 112 || belowIdx == 115 || belowIdx == 194 || belowIdx == 284 || (belowIdx >= 315 && belowIdx <= 317))
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = (belowIdx == 112 || belowIdx == 115);
                        intent.HasResolvedTile = true;
                        intent.ResolvedTile = new Point(tile.X, tile.Y + 1);
                        return intent;
                    }
                }
                catch { }
            }

            // 9. Diggable empty tile when player is actively holding a Hoe (works up to 2 tiles away, including diagonals & under feet!)
            if (who.CurrentTool is Hoe)
            {
                try
                {
                    bool occupied = (loc.objects != null && loc.objects.ContainsKey(tv)) ||
                                    (loc.terrainFeatures != null && loc.terrainFeatures.ContainsKey(tv));
                    if (!occupied &&
                        loc.doesTileHaveProperty(tile.X, tile.Y, "Diggable", "Back") != null &&
                        Math.Max(Math.Abs(who.TilePoint.X - tile.X), Math.Abs(who.TilePoint.Y - tile.Y)) <= 2)
                    {
                        intent.Kind = TileActionKind.UseTool;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = false;
                        intent.AllowSameTile = true;
                        return intent;
                    }
                }
                catch { }
            }

            return intent;
        }

        private static bool IsMinecartTileAt(GameLocation loc, int tx, int ty)
        {
            if (loc == null) return false;
            try
            {
                string act = loc.doesTileHaveProperty(tx, ty, "Action", "Buildings");
                if (!string.IsNullOrEmpty(act) &&
                    (act.StartsWith("MinecartTransport", StringComparison.OrdinalIgnoreCase) ||
                     act.StartsWith("Minecart", StringComparison.OrdinalIgnoreCase)) &&
                    !act.StartsWith("MinecartGame", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // BusStop & Mountain (Quarry) check "outdoors" tilesheet indices 958, 1080, 1081 in checkAction!
                int outIdx = loc.getTileIndexAt(tx, ty, "Buildings", "outdoors");
                if (outIdx == 958 || outIdx == 1080 || outIdx == 1081)
                    return true;

                // Town (Blacksmith) checks "Landscape" tilesheet indices 958, 1080, 1081 in Town.checkAction!
                int landIdx = loc.getTileIndexAt(tx, ty, "Buildings", "Landscape");
                if (landIdx == 958 || landIdx == 1080 || landIdx == 1081)
                    return true;

                // Fallback for custom/recolored maps whose outdoor sheet id contains "outdoors" or "landscape" with different casing
                var bLayer = loc.Map?.GetLayer("Buildings");
                if (bLayer != null && tx >= 0 && ty >= 0 && tx < bLayer.LayerWidth && ty < bLayer.LayerHeight)
                {
                    var t = bLayer.Tiles[tx, ty];
                    if (t != null && (t.TileIndex == 958 || t.TileIndex == 1080 || t.TileIndex == 1081))
                    {
                        string sheetId = t.TileSheet?.Id ?? "";
                        if (sheetId.IndexOf("outdoors", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            sheetId.IndexOf("landscape", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool HasAnyWalkableNeighbor8(GameLocation loc, int gx, int gy, Farmer who)
        {
            Point pTile = who != null ? who.TilePoint : Point.Zero;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = gx + dx;
                    int ny = gy + dy;
                    if ((who != null && nx == pTile.X && ny == pTile.Y) || PathFinder.IsTileWalkable(loc, nx, ny, who))
                        return true;
                }
            }
            return false;
        }

        private static Point ResolveAccessibleMinecartTile(GameLocation loc, Point mcTile, Farmer who)
        {
            Point pTile = who != null ? who.TilePoint : mcTile;
            // Minecarts are often 2 tiles wide (1080 + 1081) tucked into rock alcoves where one half has 0 walkable neighbors.
            // Pick the Minecart tile in [mcTile.X - 1 .. mcTile.X + 1] that has a walkable neighbor and is closest to the player!
            Point best = mcTile;
            bool bestReachable = HasAnyWalkableNeighbor8(loc, mcTile.X, mcTile.Y, who);
            int bestDist = Math.Abs(mcTile.X - pTile.X) + Math.Abs(mcTile.Y - pTile.Y);

            for (int dx = -1; dx <= 1; dx += 2)
            {
                int cx = mcTile.X + dx;
                int cy = mcTile.Y;
                if (!IsMinecartTileAt(loc, cx, cy))
                    continue;
                bool reach = HasAnyWalkableNeighbor8(loc, cx, cy, who);
                int dist = Math.Abs(cx - pTile.X) + Math.Abs(cy - pTile.Y);
                if ((reach && !bestReachable) || (reach == bestReachable && dist < bestDist))
                {
                    best = new Point(cx, cy);
                    bestReachable = reach;
                    bestDist = dist;
                }
            }
            return best;
        }

        private static bool TryResolveMinecartOrLocationTileAction(GameLocation loc, Point tile, Farmer who, ref TileIntent intent)
        {
            if (loc == null || who == null) return false;
            try
            {
                // 1. Minecart on tapped tile OR 1 tile below (when player taps the upper coal/rim Front-layer sprite of a Minecart!)
                Point? hitMinecart = null;
                if (IsMinecartTileAt(loc, tile.X, tile.Y))
                    hitMinecart = tile;
                else if (!PathFinder.IsTileWalkable(loc, tile.X, tile.Y, who) && IsMinecartTileAt(loc, tile.X, tile.Y + 1))
                    hitMinecart = new Point(tile.X, tile.Y + 1);
                else if (IsMinecartTileAt(loc, tile.X, tile.Y + 1) && loc.Map?.GetLayer("Front")?.Tiles[tile.X, tile.Y] != null)
                    hitMinecart = new Point(tile.X, tile.Y + 1);

                if (hitMinecart.HasValue)
                {
                    Point resolvedMc = ResolveAccessibleMinecartTile(loc, hitMinecart.Value, who);
                    intent.Kind = TileActionKind.Interact;
                    intent.StopAdjacent = true;
                    intent.PreferCardinal = false; // Minecart works from all 8 adjacent tiles (including diagonal alcoves!)
                    intent.HasResolvedTile = true;
                    intent.ResolvedTile = resolvedMc;
                    return true;
                }

                // 2. Ginger Island Parrot Express (ParrotPlatform) & Golden Walnut Upgrade Perches
                if (loc is IslandLocation isl)
                {
                    Vector2 tv = new Vector2(tile.X, tile.Y);
                    if (isl.parrotPlatforms != null)
                    {
                        for (int i = 0; i < isl.parrotPlatforms.Count; i++)
                        {
                            ParrotPlatform pp = isl.parrotPlatforms[i];
                            if (pp != null && pp.OccupiesTile(tv))
                            {
                                // ParrotPlatform is 3x2 tiles and triggers automatically when stepping onto (baseX + 1, baseY + 1)
                                int px = (int)(pp.position.X / 64f) + 1;
                                int py = (int)(pp.position.Y / 64f) + 1;
                                intent.Kind = TileActionKind.None;
                                intent.StopAdjacent = false;
                                intent.HasResolvedTile = true;
                                intent.ResolvedTile = new Point(px, py);
                                return true;
                            }
                        }
                    }
                    if (isl.parrotUpgradePerches != null)
                    {
                        foreach (ParrotUpgradePerch perch in isl.parrotUpgradePerches)
                        {
                            if (perch == null) continue;
                            Point pt = perch.tilePosition.Value;
                            if ((tile.X == pt.X && tile.Y == pt.Y) || (tile.X == pt.X && tile.Y == pt.Y - 1))
                            {
                                intent.Kind = TileActionKind.Interact;
                                intent.StopAdjacent = true;
                                intent.PreferCardinal = false;
                                intent.HasResolvedTile = true;
                                intent.ResolvedTile = pt;
                                return true;
                            }
                        }
                    }
                }

                // 3. Hardcoded Vanilla 1.6 Location.checkAction tile indices (not stored in map "Action" properties!)
                if (loc is BusStop)
                {
                    int bIdx = loc.getTileIndexAt(tile.X, tile.Y, "Buildings", "outdoors");
                    int bBelow = loc.getTileIndexAt(tile.X, tile.Y + 1, "Buildings", "outdoors");
                    if (bIdx == 1057 || bBelow == 1057) // Pam's Desert Bus Ticket Machine
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = false;
                        intent.HasResolvedTile = true;
                        intent.ResolvedTile = (bIdx == 1057) ? tile : new Point(tile.X, tile.Y + 1);
                        return true;
                    }
                }
                else if (loc is CommunityCenter || string.Equals(loc.Name, "AbandonedJojaMart", StringComparison.Ordinal))
                {
                    int ccIdx = loc.getTileIndexAt(tile.X, tile.Y, "Buildings", null);
                    if (ccIdx == 1799 || (ccIdx >= 1824 && ccIdx <= 1833)) // Golden Bundle Scrolls & Missing Bundle!
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = false;
                        return true;
                    }
                }
                else if (loc is VolcanoDungeon)
                {
                    int vIdx = loc.getTileIndexAt(tile.X, tile.Y, "Buildings", "dungeon");
                    if (vIdx == 77 || vIdx == 367) // Leave Volcano shortcut & Volcano Dwarf Shop!
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = false;
                        return true;
                    }
                }
                else if (loc is Sewer)
                {
                    int sIdx = loc.getTileIndexAt(tile.X, tile.Y, "Buildings", "st");
                    int sBelow = loc.getTileIndexAt(tile.X, tile.Y + 1, "Buildings", "st");
                    if (sIdx == 21 || sIdx == 84 || sBelow == 21) // Sewer exit ladder & Krobus Shadow Shop!
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = false;
                        if (sIdx != 21 && sIdx != 84 && sBelow == 21)
                        {
                            intent.HasResolvedTile = true;
                            intent.ResolvedTile = new Point(tile.X, tile.Y + 1);
                        }
                        return true;
                    }
                }
                else if (loc is Beach)
                {
                    int bchIdx = loc.getTileIndexAt(tile.X, tile.Y, "Buildings", "untitled tile sheet");
                    if (bchIdx == 284 || bchIdx == 496) // Tide-pool 300-wood bridge repair & Willy sign
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = false;
                        return true;
                    }
                }
                else if (loc is Woods)
                {
                    int wIdx = loc.getTileIndexAt(tile.X, tile.Y, "Buildings", "untitled tile sheet");
                    int wBelow = loc.getTileIndexAt(tile.X, tile.Y + 1, "Buildings", "untitled tile sheet");
                    if (wIdx == 1140 || wBelow == 1140) // Old Master Cannoli statue
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = false;
                        if (wIdx != 1140)
                        {
                            intent.HasResolvedTile = true;
                            intent.ResolvedTile = new Point(tile.X, tile.Y + 1);
                        }
                        return true;
                    }
                }
                else if (loc is Railroad)
                {
                    int rIdx = loc.getTileIndexAt(tile.X, tile.Y, "Buildings", "untitled tile sheet");
                    if (rIdx == 287) // Witch's Cave Dark Talisman barrier
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = false;
                        return true;
                    }
                }
                else if (loc is Desert)
                {
                    if (tile.Y == 24 && ((tile.X >= 41 && tile.X <= 42) || (tile.X >= 34 && tile.X <= 38))) // Desert Trader & Camel
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = false;
                        return true;
                    }
                }
                else if (loc is BoatTunnel)
                {
                    if ((tile.X == 6 && tile.Y == 8) || (tile.X == 8 && tile.Y == 10)) // Willy's Boat Hull & Anchor repairs
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        intent.PreferCardinal = false;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static Point FindClosestBuildingTile(Building b, Point fromTile)
        {
            int bx = b.tileX.Value;
            int by = b.tileY.Value;
            int bw = Math.Max(1, b.tilesWide.Value);
            int bh = Math.Max(1, b.tilesHigh.Value);
            int bestX = Math.Clamp(fromTile.X, bx, bx + bw - 1);
            int bestY = Math.Clamp(fromTile.Y, by, by + bh - 1);
            return new Point(bestX, bestY);
        }

        private static Point FindClosestRectTile(Rectangle worldRect, Point fromTile)
        {
            int minX = worldRect.Left / 64;
            int minY = worldRect.Top / 64;
            int maxX = Math.Max(minX, (worldRect.Right - 1) / 64);
            int maxY = Math.Max(minY, (worldRect.Bottom - 1) / 64);
            return new Point(Math.Clamp(fromTile.X, minX, maxX), Math.Clamp(fromTile.Y, minY, maxY));
        }

        /// <summary>
        /// Mobile-style press-and-hold mining / chopping / digging target detector.
        /// Lets the player hold their finger on any rock, ore node, boulder, stump, tree, twig, weed,
        /// mine barrel, monster, artifact spot, diggable dirt (with Hoe), or adjacent breakable tile (with Pickaxe/Axe)
        /// to continuously swing until broken or finger is lifted!
        /// </summary>
        public static TileIntent InspectHoldToolTarget(GameLocation loc, Point tile, Farmer who, bool autoTool)
        {
            TileIntent intent = InspectTile(loc, tile, who, autoTool);
            if (intent.Kind == TileActionKind.UseTool)
                return intent;

            if (loc == null || who == null)
                return new TileIntent { Kind = TileActionKind.None, PreferredToolSlot = -1 };
            if (Game1.eventUp && (Game1.CurrentEvent == null || !Game1.CurrentEvent.canPlayerUseTool()))
                return new TileIntent { Kind = TileActionKind.None, PreferredToolSlot = -1 };

            Vector2 tv = new Vector2(tile.X, tile.Y);
            int cheb = Math.Max(Math.Abs(who.TilePoint.X - tile.X), Math.Abs(who.TilePoint.Y - tile.Y));

            // Holding on ANY diggable empty dirt tile while holding a Hoe -> walk up and dig it!
            if (who.CurrentTool is Hoe)
            {
                try
                {
                    bool occupied = (loc.objects != null && loc.objects.ContainsKey(tv)) ||
                                    (loc.terrainFeatures != null && loc.terrainFeatures.ContainsKey(tv));
                    if (!occupied && loc.doesTileHaveProperty(tile.X, tile.Y, "Diggable", "Back") != null)
                    {
                        return new TileIntent
                        {
                            Kind = TileActionKind.UseTool,
                            PreferredToolSlot = -1,
                            StopAdjacent = true,
                            PreferCardinal = false,
                            Repeatable = false
                        };
                    }
                }
                catch { }
            }

            // Holding directly on an adjacent tile (cheb == 1) while holding Pickaxe/Axe ->
            // lets player un-till HoeDirt or pick up placed paths/fences/sprinklers/chests/machines cleanly!
            if (cheb == 1 && (who.CurrentTool is Pickaxe || who.CurrentTool is Axe))
            {
                bool hasAnything = (loc.objects != null && loc.objects.ContainsKey(tv)) ||
                                   (loc.terrainFeatures != null && loc.terrainFeatures.ContainsKey(tv));
                if (hasAnything)
                {
                    return new TileIntent
                    {
                        Kind = TileActionKind.UseTool,
                        PreferredToolSlot = -1,
                        StopAdjacent = true,
                        PreferCardinal = false,
                        Repeatable = true
                    };
                }
            }

            return new TileIntent { Kind = TileActionKind.None, PreferredToolSlot = -1 };
        }

        public static float GetTargetHealth(GameLocation loc, Point tile)
        {
            if (loc == null) return -1f;
            try
            {
                Vector2 tv = new Vector2(tile.X, tile.Y);
                if (loc.terrainFeatures != null && loc.terrainFeatures.TryGetValue(tv, out TerrainFeature tf))
                {
                    if (tf is Tree tree)
                        return tree.falling.Value ? -1f : tree.health.Value;
                    if (tf is FruitTree ft)
                        return ft.health.Value;
                }
                if (loc.resourceClumps != null)
                {
                    for (int i = 0; i < loc.resourceClumps.Count; i++)
                    {
                        ResourceClump rc = loc.resourceClumps[i];
                        if (rc != null && rc.occupiesTile(tile.X, tile.Y))
                            return rc.health.Value;
                    }
                }
                if (loc.objects != null && loc.objects.TryGetValue(tv, out SObject obj) && obj != null)
                {
                    if (obj.IsBreakableStone() || obj.Name == "Stone" || obj.QualifiedItemId == "(O)25")
                        return obj.MinutesUntilReady > 0 ? (float)obj.MinutesUntilReady : -1f;
                }
            }
            catch { }
            return -1f;
        }

        public static bool TryAutoAttack(GameLocation loc, Farmer who, bool playerActivelyMovingAway, ref int cooldownFrames)
        {
            if (cooldownFrames > 0)
            {
                cooldownFrames--;
                return false;
            }

            if (loc == null || who == null || who.UsingTool || who.isRidingHorse() || who.swimming.Value || who.health <= 0)
                return false;
            if (Game1.eventUp && (Game1.CurrentEvent == null || !Game1.CurrentEvent.canPlayerUseTool()))
                return false;

            Vector2 playerCenter = who.GetBoundingBox().Center.ToVector2();
            Monster target = FindMonsterNear(loc, playerCenter, playerActivelyMovingAway ? 92f : 122f);
            if (target == null)
                return false;

            int weaponSlot = FindBestWeaponSlot(who);
            if (weaponSlot < 0)
                return false;

            if (who.CurrentToolIndex != weaponSlot)
                who.CurrentToolIndex = weaponSlot;

            Vector2 mCenter = target.GetBoundingBox().Center.ToVector2();
            who.faceGeneralDirection(mCenter);
            cooldownFrames = 13;
            return true;
        }

        public static Monster FindMonsterNear(GameLocation loc, Vector2 worldPoint, float maxDist)
        {
            if (loc?.characters == null)
                return null;

            Monster best = null;
            float bestDistSq = maxDist * maxDist;

            foreach (NPC c in loc.characters)
            {
                if (c is not Monster m)
                    continue;
                if (m.Health <= 0 || m.IsInvisible || m.isInvincible())
                    continue;

                Vector2 mc = m.GetBoundingBox().Center.ToVector2();
                float dSq = Vector2.DistanceSquared(mc, worldPoint);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = m;
                }
            }

            return best;
        }

        public static int FindToolSlot<T>(Farmer who) where T : Tool
        {
            if (who?.Items == null) return -1;
            int total = Math.Min(Math.Max(12, who.MaxItems), who.Items.Count);
            int row1 = Math.Min(12, total);
            // Prefer hotbar row 1 (slots 0..11) first, then scan rows 2-3 (12..35) for 36-slot Vertical Toolbar / deep backpack!
            for (int i = 0; i < row1; i++)
            {
                if (who.Items[i] is T)
                    return i;
            }
            for (int i = row1; i < total; i++)
            {
                if (who.Items[i] is T)
                    return i;
            }
            return -1;
        }

        public static int FindScytheOrWeaponSlot(Farmer who)
        {
            if (who?.Items == null) return -1;
            int total = Math.Min(Math.Max(12, who.MaxItems), who.Items.Count);
            int fallbackWeapon = -1;
            for (int i = 0; i < total; i++)
            {
                if (who.Items[i] is MeleeWeapon mw)
                {
                    if (mw.isScythe())
                        return i;
                    if (fallbackWeapon < 0)
                        fallbackWeapon = i;
                }
            }
            return fallbackWeapon;
        }

        public static int FindBestWeaponSlot(Farmer who)
        {
            if (who?.Items == null) return -1;
            int total = Math.Min(Math.Max(12, who.MaxItems), who.Items.Count);
            int bestSlot = -1;
            int bestScore = -1;
            for (int i = 0; i < total; i++)
            {
                if (who.Items[i] is MeleeWeapon mw && !mw.isScythe())
                {
                    int score = mw.minDamage.Value + mw.maxDamage.Value;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestSlot = i;
                    }
                }
            }
            return bestSlot;
        }
    }
}
