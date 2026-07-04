using System;
using System.Collections.Generic;
using UnityEngine;
using Assets.Scripts.LevelGeneration;

namespace drgAccess.Helpers
{
    /// <summary>
    /// Replacement for Unity NavMesh pathfinding (whose interop crashes natively
    /// on the Unity 6 game build). The game map is a block grid: MineableBlock
    /// exposes its grid cell (x, y), so walkability is simply "no alive block
    /// in the cell". A* runs fully managed — it cannot crash the game.
    ///
    /// The walkable grid is rebuilt periodically (blocks disappear when mined)
    /// and the world→cell mapping is calibrated from a real block's transform,
    /// so no assumptions about map origin are needed.
    /// </summary>
    public static class GridPathHelper
    {
        // Grid state
        private static bool[,] blocked;
        private static int gridWidth, gridHeight;
        private static float worldOffsetX, worldOffsetZ; // world = cell + offset
        private static bool gridReady;
        private static float nextGridRebuildTime;
        private const float GRID_REBUILD_INTERVAL = 2.5f;

        // Path cache (same throttle contract as the old NavMesh helper)
        private static List<Vector3> lastCorners = new List<Vector3>();
        private static bool lastPathValid;
        private static float lastCalcTime;
        private const float RECALC_INTERVAL = 0.5f;

        private const float WAYPOINT_MIN_DISTANCE = 1.5f;
        // When start/target cells are blocked (e.g. the pod sits on rubble),
        // search outward this many cells for the nearest walkable one.
        private const int NEAREST_WALKABLE_RADIUS = 6;

        public static void Reset()
        {
            blocked = null;
            gridReady = false;
            nextGridRebuildTime = 0f;
            lastCorners.Clear();
            lastPathValid = false;
            lastCalcTime = 0f;
        }

        /// <summary>
        /// Same contract as NavMeshPathHelper.GetNextWaypoint: next waypoint toward
        /// the target and total remaining path distance. Falls back to direct-line
        /// guidance whenever the grid or path is unavailable.
        /// </summary>
        public static NavMeshPathHelper.PathResult GetNextWaypoint(Vector3 playerPos, Vector3 targetPos)
        {
            float directDist = Vector3.Distance(playerPos, targetPos);

            var direct = new NavMeshPathHelper.PathResult
            {
                IsValid = true,
                NextWaypoint = targetPos,
                TotalPathDistance = directDist,
                WaypointDistance = directDist,
                UsingDirectFallback = true
            };

            try
            {
                if (Time.time >= nextGridRebuildTime)
                {
                    nextGridRebuildTime = Time.time + GRID_REBUILD_INTERVAL;
                    RebuildGrid();
                }

                if (!gridReady) return direct;

                if (Time.time - lastCalcTime >= RECALC_INTERVAL || !lastPathValid)
                {
                    lastCalcTime = Time.time;
                    lastPathValid = ComputePath(playerPos, targetPos);
                }

                if (!lastPathValid || lastCorners.Count < 2) return direct;

                return SelectWaypoint(playerPos);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[GridPath] GetNextWaypoint error: {e.Message}");
                return direct;
            }
        }

        // === Grid construction ===

        private static void RebuildGrid()
        {
            try
            {
                var mapGen = UnityEngine.Object.FindObjectOfType<MapGenerator>();
                if (mapGen == null)
                {
                    gridReady = false;
                    return;
                }

                int w = mapGen.width;
                int h = mapGen.height;
                if (w <= 0 || h <= 0 || w > 1024 || h > 1024)
                {
                    gridReady = false;
                    return;
                }

                var grid = new bool[w, h];
                bool calibrated = false;

                var blocks = UnityEngine.Object.FindObjectsOfType<MineableBlock>();
                if (blocks == null)
                {
                    gridReady = false;
                    return;
                }

                foreach (var block in blocks)
                {
                    if (block == null) continue;

                    try
                    {
                        if (block.state != MineableBlock.EState.ALIVE) continue;

                        int bx = block.x;
                        int by = block.y;
                        if (bx < 0 || bx >= w || by < 0 || by >= h) continue;
                        grid[bx, by] = true;

                        // Calibrate world→cell mapping from the first alive block:
                        // world = cell + offset (block scale is 1 world unit)
                        if (!calibrated)
                        {
                            var pos = block.transform.position;
                            worldOffsetX = pos.x - bx;
                            worldOffsetZ = pos.z - by;
                            calibrated = true;
                        }
                    }
                    catch { }
                }

                if (!calibrated)
                {
                    // No alive blocks at all (fully mined map) — nothing obstructs
                    gridReady = false;
                    return;
                }

                blocked = grid;
                gridWidth = w;
                gridHeight = h;
                gridReady = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[GridPath] RebuildGrid error: {e.Message}");
                gridReady = false;
            }
        }

        private static Vector2Int WorldToCell(Vector3 world)
        {
            return new Vector2Int(
                Mathf.RoundToInt(world.x - worldOffsetX),
                Mathf.RoundToInt(world.z - worldOffsetZ));
        }

        private static Vector3 CellToWorld(int x, int y)
        {
            return new Vector3(x + worldOffsetX, 0f, y + worldOffsetZ);
        }

        private static bool IsWalkable(int x, int y)
        {
            if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight) return false;
            return !blocked[x, y];
        }

        /// <summary>Spiral outward from the cell to find the nearest walkable one.</summary>
        private static bool FindNearestWalkable(Vector2Int cell, out Vector2Int result)
        {
            if (IsWalkable(cell.x, cell.y))
            {
                result = cell;
                return true;
            }

            for (int r = 1; r <= NEAREST_WALKABLE_RADIUS; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue; // ring only
                        if (IsWalkable(cell.x + dx, cell.y + dy))
                        {
                            result = new Vector2Int(cell.x + dx, cell.y + dy);
                            return true;
                        }
                    }
                }
            }

            result = cell;
            return false;
        }

        // === A* ===

        private static bool ComputePath(Vector3 fromWorld, Vector3 toWorld)
        {
            if (!FindNearestWalkable(WorldToCell(fromWorld), out var start)) return false;
            if (!FindNearestWalkable(WorldToCell(toWorld), out var goal)) return false;

            if (start == goal)
            {
                lastCorners.Clear();
                lastCorners.Add(fromWorld);
                lastCorners.Add(toWorld);
                return true;
            }

            // A* with octile heuristic; diagonals allowed but never cutting corners
            int w = gridWidth, h = gridHeight;
            var gScore = new float[w, h];
            var cameFrom = new int[w, h]; // packed parent cell + 1 (0 = none)
            var closed = new bool[w, h];
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                    gScore[i, j] = float.MaxValue;

            var open = new SortedSet<(float f, int order, int x, int y)>();
            int counter = 0;

            gScore[start.x, start.y] = 0f;
            open.Add((Heuristic(start, goal), counter++, start.x, start.y));

            // 8 directions: 4 cardinal then 4 diagonal
            int[] dxs = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dys = { 0, 0, 1, -1, 1, -1, 1, -1 };

            bool found = false;
            int safety = w * h; // hard bound on expansions

            while (open.Count > 0 && safety-- > 0)
            {
                var current = open.Min;
                open.Remove(current);
                int cx = current.x, cy = current.y;

                if (closed[cx, cy]) continue;
                closed[cx, cy] = true;

                if (cx == goal.x && cy == goal.y)
                {
                    found = true;
                    break;
                }

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + dxs[d], ny = cy + dys[d];
                    if (!IsWalkable(nx, ny) || closed[nx, ny]) continue;

                    bool diagonal = d >= 4;
                    if (diagonal)
                    {
                        // No corner cutting: both adjacent cardinals must be free
                        if (!IsWalkable(cx + dxs[d], cy) || !IsWalkable(cx, cy + dys[d]))
                            continue;
                    }

                    float step = diagonal ? 1.41421356f : 1f;
                    float tentative = gScore[cx, cy] + step;
                    if (tentative < gScore[nx, ny])
                    {
                        gScore[nx, ny] = tentative;
                        cameFrom[nx, ny] = (cy * w + cx) + 1;
                        open.Add((tentative + Heuristic(new Vector2Int(nx, ny), goal), counter++, nx, ny));
                    }
                }
            }

            if (!found) return false;

            // Reconstruct path (goal → start), then reverse
            var cells = new List<Vector2Int>();
            var cur = goal;
            int guard = w * h;
            while (guard-- > 0)
            {
                cells.Add(cur);
                int packed = cameFrom[cur.x, cur.y];
                if (packed == 0) break;
                packed--;
                cur = new Vector2Int(packed % w, packed / w);
            }
            cells.Reverse();

            // Simplify: keep only cells where direction changes, then convert to world
            lastCorners.Clear();
            lastCorners.Add(fromWorld);
            for (int i = 1; i < cells.Count - 1; i++)
            {
                var prev = cells[i - 1];
                var next = cells[i + 1];
                bool straight = (next.x - cells[i].x) == (cells[i].x - prev.x) &&
                                (next.y - cells[i].y) == (cells[i].y - prev.y);
                if (!straight)
                    lastCorners.Add(CellToWorld(cells[i].x, cells[i].y));
            }
            lastCorners.Add(toWorld);
            return true;
        }

        private static float Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Math.Abs(a.x - b.x), dy = Math.Abs(a.y - b.y);
            return (dx + dy) + (1.41421356f - 2f) * Math.Min(dx, dy); // octile
        }

        private static NavMeshPathHelper.PathResult SelectWaypoint(Vector3 playerPos)
        {
            // First corner far enough from the player to be meaningful
            int selectedIndex = lastCorners.Count - 1;
            for (int i = 1; i < lastCorners.Count; i++)
            {
                if (Vector3.Distance(playerPos, lastCorners[i]) >= WAYPOINT_MIN_DISTANCE)
                {
                    selectedIndex = i;
                    break;
                }
            }

            Vector3 waypoint = lastCorners[selectedIndex];
            float waypointDist = Vector3.Distance(playerPos, waypoint);

            float remaining = waypointDist;
            for (int i = selectedIndex + 1; i < lastCorners.Count; i++)
                remaining += Vector3.Distance(lastCorners[i - 1], lastCorners[i]);

            return new NavMeshPathHelper.PathResult
            {
                IsValid = true,
                NextWaypoint = waypoint,
                TotalPathDistance = remaining,
                WaypointDistance = waypointDist,
                UsingDirectFallback = false
            };
        }
    }
}
