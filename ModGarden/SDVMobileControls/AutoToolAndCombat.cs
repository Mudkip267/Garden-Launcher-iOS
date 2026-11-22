using System;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.GameData.Crops;
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
        Interact = 2   // ButtonA (NPC, Chest, Door, Harvest, Forage, Machine, Plant)
    }

    internal struct TileIntent
    {
        public TileActionKind Kind;
        public int PreferredToolSlot; // -1 = keep current tool
        public bool StopAdjacent;
        public bool Repeatable;       // Auto-repeat 1-tap chop/mine/fight until broken or defeated
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

            // 1. Living monster near tapped tile
            Monster tappedMonster = FindMonsterNear(loc, tileCenter, 78f);
            if (tappedMonster != null)
            {
                intent.Kind = TileActionKind.UseTool;
                intent.StopAdjacent = true;
                intent.Repeatable = true;
                if (autoTool)
                    intent.PreferredToolSlot = FindBestWeaponSlot(who);
                return intent;
            }

            // 2. NPC near tapped tile -> talk / gift
            if (loc.characters != null)
            {
                foreach (NPC npc in loc.characters)
                {
                    if (npc == null || npc is Monster)
                        continue;
                    if (Vector2.Distance(npc.GetBoundingBox().Center.ToVector2(), tileCenter) <= 72f)
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        return intent;
                    }
                }
            }

            // 2b. FarmAnimal near tapped tile -> pet first, then auto-select Milk Pail / Shears if ready!
            try
            {
                if (loc.Animals != null && loc.Animals.Length > 0)
                {
                    FarmAnimal hitAnimal = null;
                    float bestD = 76f;
                    foreach (FarmAnimal animal in loc.Animals.Values)
                    {
                        if (animal == null) continue;
                        Rectangle bb = animal.GetBoundingBox();
                        float d = bb.Contains((int)tileCenter.X, (int)tileCenter.Y)
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
                        intent.StopAdjacent = true;
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
                        intent.Kind = TileActionKind.UseTool;
                        intent.StopAdjacent = true;
                        intent.Repeatable = true;
                        if (autoTool)
                        {
                            int idx = rc.parentSheetIndex.Value;
                            bool isWood = idx == ResourceClump.stumpIndex ||
                                          idx == ResourceClump.hollowLogIndex ||
                                          idx == ResourceClump.greenRainBush1Index ||
                                          idx == ResourceClump.greenRainBush2Index;
                            intent.PreferredToolSlot = isWood ? FindToolSlot<Axe>(who) : FindToolSlot<Pickaxe>(who);
                        }
                        return intent;
                    }
                }
            }

            // 4. Objects on tile (chests, machines, stones, twigs, weeds, forageables, artifact spots)
            if (loc.objects != null && loc.objects.TryGetValue(tv, out SObject obj) && obj != null)
            {
                if (obj is Chest || obj is Furniture || obj.readyForHarvest.Value || obj.IsSpawnedObject)
                {
                    intent.Kind = TileActionKind.Interact;
                    intent.StopAdjacent = true;
                    return intent;
                }

                if (obj.QualifiedItemId == "(O)590" || obj.QualifiedItemId == "(O)SeedSpot")
                {
                    intent.Kind = TileActionKind.UseTool;
                    intent.StopAdjacent = true;
                    if (autoTool)
                        intent.PreferredToolSlot = FindToolSlot<Hoe>(who);
                    return intent;
                }

                if (obj.IsBreakableStone() || obj.Name == "Stone")
                {
                    intent.Kind = TileActionKind.UseTool;
                    intent.StopAdjacent = true;
                    intent.Repeatable = true;
                    if (autoTool)
                        intent.PreferredToolSlot = FindToolSlot<Pickaxe>(who);
                    return intent;
                }

                if (obj.IsTwig() || obj.Name == "Twig")
                {
                    intent.Kind = TileActionKind.UseTool;
                    intent.StopAdjacent = true;
                    intent.Repeatable = true;
                    if (autoTool)
                        intent.PreferredToolSlot = FindToolSlot<Axe>(who);
                    return intent;
                }

                if (obj.IsWeeds())
                {
                    intent.Kind = TileActionKind.UseTool;
                    intent.StopAdjacent = true;
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

            // 5. TerrainFeatures (Trees, FruitTrees, HoeDirt crops, Grass)
            if (loc.terrainFeatures != null && loc.terrainFeatures.TryGetValue(tv, out TerrainFeature tf) && tf != null)
            {
                if (tf is Tree)
                {
                    intent.Kind = TileActionKind.UseTool;
                    intent.StopAdjacent = true;
                    intent.Repeatable = true;
                    if (autoTool)
                        intent.PreferredToolSlot = FindToolSlot<Axe>(who);
                    return intent;
                }

                if (tf is FruitTree ft)
                {
                    if (ft.fruit.Count > 0)
                    {
                        intent.Kind = TileActionKind.Interact;
                        intent.StopAdjacent = true;
                        return intent;
                    }
                    intent.Kind = TileActionKind.UseTool;
                    intent.StopAdjacent = true;
                    if (autoTool)
                        intent.PreferredToolSlot = FindToolSlot<Axe>(who);
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
                                if (autoTool)
                                    intent.PreferredToolSlot = FindScytheOrWeaponSlot(who);
                            }
                            else
                            {
                                intent.Kind = TileActionKind.Interact;
                                intent.StopAdjacent = true;
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
                                intent.PreferredToolSlot = wc;
                                return intent;
                            }
                        }

                        if (hd.crop.dead.Value)
                        {
                            intent.Kind = TileActionKind.UseTool;
                            intent.StopAdjacent = true;
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

            // 8. Building / Action tile (Doors, Mailboxes, TV, Bed, Calendar, Shipping Bin, Shop Counters)
            try
            {
                string actionProp = loc.doesTileHaveProperty(tile.X, tile.Y, "Action", "Buildings");
                if (!string.IsNullOrEmpty(actionProp))
                {
                    intent.Kind = TileActionKind.Interact;
                    intent.StopAdjacent = true;
                    return intent;
                }
            }
            catch { }

            // 9. Diggable empty tile ONLY if player is actively holding a Hoe
            if (who.CurrentTool is Hoe)
            {
                try
                {
                    if (loc.doesTileHaveProperty(tile.X, tile.Y, "Diggable", "Back") != null &&
                        Math.Max(Math.Abs((int)who.Tile.X - tile.X), Math.Abs((int)who.Tile.Y - tile.Y)) <= 1)
                    {
                        intent.Kind = TileActionKind.UseTool;
                        intent.StopAdjacent = true;
                        return intent;
                    }
                }
                catch { }
            }

            return intent;
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
            int limit = Math.Min(12, who.Items.Count);
            for (int i = 0; i < limit; i++)
            {
                if (who.Items[i] is T)
                    return i;
            }
            return -1;
        }

        public static int FindScytheOrWeaponSlot(Farmer who)
        {
            int limit = Math.Min(12, who.Items.Count);
            int fallbackWeapon = -1;
            for (int i = 0; i < limit; i++)
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
            int limit = Math.Min(12, who.Items.Count);
            int bestSlot = -1;
            int bestScore = -1;
            for (int i = 0; i < limit; i++)
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
