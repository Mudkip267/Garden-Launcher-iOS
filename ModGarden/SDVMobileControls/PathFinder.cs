using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;
using xTile.Dimensions;

namespace SDVMobileControls
{
    internal static class PathFinder
    {
        private const int MaxExploredNodes = 480;
        private static readonly int[] Dx = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] Dy = { -1, 0, 1, 0, -1, 1, 1, -1 };
        private static readonly float[] StepCost = { 1.0f, 1.0f, 1.0f, 1.0f, 1.414f, 1.414f, 1.414f, 1.414f };

        private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;

        public static bool IsTileWalkable(GameLocation loc, int tx, int ty, Farmer who)
        {
            if (loc?.Map == null)
                return false;

            int mapW = loc.Map.Layers[0].LayerWidth;
            int mapH = loc.Map.Layers[0].LayerHeight;
            if (tx < 0 || ty < 0 || tx >= mapW || ty >= mapH)
                return false;

            try
            {
                if (!loc.isTilePassable(new Location(tx, ty), Game1.viewport))
                    return false;

                // Exact Stardew Farmer foot hitbox sits in the BOTTOM HALF of the tile (y+32..y+64, 48x32),
                // NOT y+10! Using y+10 false-collided with walls/doorframes/house porches directly north of walkable tiles.
                // Pass pathfinding: true, skipCollisionEffects: true so A* exploration NEVER rustles distant grass/bushes!
                bool riding = who != null && who.isRidingHorse();
                var box = riding
                    ? new Microsoft.Xna.Framework.Rectangle(tx * 64 - 4, ty * 64 + 16, 72, 44)
                    : new Microsoft.Xna.Framework.Rectangle(tx * 64 + 10, ty * 64 + 34, 44, 26);
                if (loc.isCollidingPosition(
                    box,
                    Game1.viewport,
                    isFarmer: true,
                    damagesFarmer: 0,
                    glider: false,
                    character: who,
                    pathfinding: true,
                    projectile: false,
                    ignoreCharacterRequirement: false,
                    skipCollisionEffects: true))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static List<Point> FindPath(GameLocation loc, Point start, Point goal, Farmer who, bool stopAdjacent, bool preferCardinal = false)
        {
            if (loc?.Map == null || who == null)
                return null;

            if (start == goal)
                return new List<Point>();

            // Check whether at least one cardinal neighbor of the goal is walkable (or is our current start tile)
            bool requireCardinal = false;
            if (stopAdjacent && preferCardinal)
            {
                for (int i = 0; i < 4; i++)
                {
                    int cx = goal.X + Dx[i];
                    int cy = goal.Y + Dy[i];
                    if ((cx == start.X && cy == start.Y) || IsTileWalkable(loc, cx, cy, who))
                    {
                        requireCardinal = true;
                        break;
                    }
                }
            }

            if (stopAdjacent)
            {
                int adx = Math.Abs(start.X - goal.X);
                int ady = Math.Abs(start.Y - goal.Y);
                if (requireCardinal ? (adx + ady == 1) : (Math.Max(adx, ady) <= 1))
                    return new List<Point>();
            }

            // If goal itself is blocked (e.g. chest, tree, rock, NPC), automatically aim for adjacent walkable tile
            bool targetAdjacent = stopAdjacent || !IsTileWalkable(loc, goal.X, goal.Y, who);

            var open = new PriorityQueue<Point, float>();
            var cameFrom = new Dictionary<long, Point>(128);
            var gScore = new Dictionary<long, float>(128);
            var closed = new HashSet<long>();

            long startKey = Pack(start.X, start.Y);
            gScore[startKey] = 0f;
            open.Enqueue(start, Heuristic(start, goal));

            Point bestClosest = start;
            float bestClosestDist = Heuristic(start, goal);
            int explored = 0;

            while (open.Count > 0 && explored < MaxExploredNodes)
            {
                Point cur = open.Dequeue();
                long curKey = Pack(cur.X, cur.Y);
                if (!closed.Add(curKey))
                    continue;

                explored++;

                bool reached;
                if (targetAdjacent)
                {
                    int cdx = Math.Abs(cur.X - goal.X);
                    int cdy = Math.Abs(cur.Y - goal.Y);
                    reached = requireCardinal
                        ? (cdx + cdy == 1)
                        : (Math.Max(cdx, cdy) <= 1 && cur != goal);
                }
                else
                {
                    reached = (cur == goal);
                }

                if (reached)
                    return Reconstruct(cameFrom, cur, start);

                float h = Heuristic(cur, goal);
                if (h < bestClosestDist && cur != start)
                {
                    bestClosestDist = h;
                    bestClosest = cur;
                }

                float curG = gScore[curKey];

                for (int i = 0; i < 8; i++)
                {
                    int nx = cur.X + Dx[i];
                    int ny = cur.Y + Dy[i];
                    long nKey = Pack(nx, ny);
                    if (closed.Contains(nKey))
                        continue;

                    if (!IsTileWalkable(loc, nx, ny, who))
                        continue;

                    // Prevent diagonal corner cutting through solid walls/fences
                    if (i >= 4)
                    {
                        if (!IsTileWalkable(loc, cur.X + Dx[i], cur.Y, who) ||
                            !IsTileWalkable(loc, cur.X, cur.Y + Dy[i], who))
                            continue;
                    }

                    float tentativeG = curG + StepCost[i];
                    if (!gScore.TryGetValue(nKey, out float prevG) || tentativeG < prevG)
                    {
                        cameFrom[nKey] = cur;
                        gScore[nKey] = tentativeG;
                        float f = tentativeG + Heuristic(new Point(nx, ny), goal) * 1.05f;
                        open.Enqueue(new Point(nx, ny), f);
                    }
                }
            }

            // Partial best-effort path if player tapped far across the map
            if (bestClosest != start && bestClosestDist < Heuristic(start, goal) - 0.5f)
                return Reconstruct(cameFrom, bestClosest, start);

            return null;
        }

        private static float Heuristic(Point a, Point b)
        {
            int dx = Math.Abs(a.X - b.X);
            int dy = Math.Abs(a.Y - b.Y);
            return Math.Max(dx, dy) + 0.414f * Math.Min(dx, dy);
        }

        private static List<Point> Reconstruct(Dictionary<long, Point> cameFrom, Point end, Point start)
        {
            var path = new List<Point>(32);
            Point cur = end;
            while (cur != start)
            {
                path.Add(cur);
                if (!cameFrom.TryGetValue(Pack(cur.X, cur.Y), out cur))
                    break;
            }
            path.Reverse();
            return path;
        }
    }
}
