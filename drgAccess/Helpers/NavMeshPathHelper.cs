using System;
using UnityEngine;
using UnityEngine.AI;

namespace drgAccess.Helpers
{
    /// <summary>
    /// NavMesh pathfinding helper for guiding players around obstacles.
    /// Calculates paths using Unity's NavMesh and returns the next waypoint to walk toward.
    /// </summary>
    public static class NavMeshPathHelper
    {
        // Cached NavMeshPath (reused to avoid GC)
        private static NavMeshPath cachedPath;

        // Last computed path data
        private static Vector3[] lastCorners = Array.Empty<Vector3>();
        private static float lastPathDistance = 0f;
        private static bool lastPathValid = false;

        // Recalculation throttle
        private static float lastCalcTime = 0f;
        private const float RECALC_INTERVAL = 0.5f;

        // NavMesh sampling parameters
        private const float SAMPLE_RADIUS = 5f;
        private const int ALL_AREAS = -1; // NavMesh.AllAreas

        // Waypoint selection
        private const float WAYPOINT_MIN_DISTANCE = 2f;
        private const float DIRECT_TARGET_DISTANCE = 8f;

        public struct PathResult
        {
            public bool IsValid;
            public Vector3 NextWaypoint;
            public float TotalPathDistance;
            public float WaypointDistance;
            public bool UsingDirectFallback;
        }

        /// <summary>
        /// Calculates the next waypoint toward a target using NavMesh pathfinding.
        /// Recalculates path every RECALC_INTERVAL seconds for performance.
        /// Falls back to direct targeting if path fails or within close range.
        /// </summary>
        public static PathResult GetNextWaypoint(Vector3 playerPos, Vector3 targetPos)
        {
            float directDist = Vector3.Distance(playerPos, targetPos);

            // Within close range, skip pathfinding for precision
            if (directDist < DIRECT_TARGET_DISTANCE)
            {
                return new PathResult
                {
                    IsValid = true,
                    NextWaypoint = targetPos,
                    TotalPathDistance = directDist,
                    WaypointDistance = directDist,
                    UsingDirectFallback = false
                };
            }

            // Throttle recalculation
            if (Time.time - lastCalcTime >= RECALC_INTERVAL || !lastPathValid)
            {
                RecalculatePath(playerPos, targetPos);
                lastCalcTime = Time.time;
            }

            // Use cached path
            if (!lastPathValid || lastCorners.Length < 2)
            {
                return new PathResult
                {
                    IsValid = true,
                    NextWaypoint = targetPos,
                    TotalPathDistance = directDist,
                    WaypointDistance = directDist,
                    UsingDirectFallback = true
                };
            }

            return SelectWaypoint(playerPos, targetPos);
        }

        /// <summary>
        /// Resets cached state. Call on scene change or beacon activation.
        /// </summary>
        public static void Reset()
        {
            lastCorners = Array.Empty<Vector3>();
            lastPathDistance = 0f;
            lastPathValid = false;
            lastCalcTime = 0f;
            cachedPath = null;
        }

        private static void RecalculatePath(Vector3 from, Vector3 to)
        {
            try
            {
                if (cachedPath == null)
                    cachedPath = new NavMeshPath();

                // Snap positions to NavMesh surface
                if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit, SAMPLE_RADIUS, ALL_AREAS))
                {
                    lastPathValid = false;
                    return;
                }

                if (!NavMesh.SamplePosition(to, out NavMeshHit toHit, SAMPLE_RADIUS, ALL_AREAS))
                {
                    lastPathValid = false;
                    return;
                }

                bool success = NavMesh.CalculatePath(fromHit.position, toHit.position, ALL_AREAS, cachedPath);

                if (!success || cachedPath.status == NavMeshPathStatus.PathInvalid)
                {
                    lastPathValid = false;
                    return;
                }

                // Copy corners to managed array (IL2CPP returns Il2CppStructArray)
                var il2cppCorners = cachedPath.corners;
                if (il2cppCorners == null || il2cppCorners.Length < 2)
                {
                    lastPathValid = false;
                    return;
                }

                int count = il2cppCorners.Length;
                if (lastCorners.Length != count)
                    lastCorners = new Vector3[count];

                for (int i = 0; i < count; i++)
                    lastCorners[i] = il2cppCorners[i];

                // Compute total path distance
                lastPathDistance = 0f;
                for (int i = 1; i < count; i++)
                    lastPathDistance += Vector3.Distance(lastCorners[i - 1], lastCorners[i]);

                lastPathValid = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[NavMeshPathHelper] RecalculatePath error: {e.Message}");
                lastPathValid = false;
            }
        }

        private static PathResult SelectWaypoint(Vector3 playerPos, Vector3 targetPos)
        {
            // Find the first corner far enough from the player to be meaningful
            // Skip corner 0 (start position)
            int selectedIndex = lastCorners.Length - 1; // default: last corner (destination)

            for (int i = 1; i < lastCorners.Length; i++)
            {
                float dist = Vector3.Distance(playerPos, lastCorners[i]);
                if (dist >= WAYPOINT_MIN_DISTANCE)
                {
                    selectedIndex = i;
                    break;
                }
            }

            Vector3 waypoint = lastCorners[selectedIndex];
            float waypointDist = Vector3.Distance(playerPos, waypoint);

            // Calculate remaining path distance from selected waypoint to destination
            float remainingDist = waypointDist;
            for (int i = selectedIndex + 1; i < lastCorners.Length; i++)
                remainingDist += Vector3.Distance(lastCorners[i - 1], lastCorners[i]);

            return new PathResult
            {
                IsValid = true,
                NextWaypoint = waypoint,
                TotalPathDistance = remainingDist,
                WaypointDistance = waypointDist,
                UsingDirectFallback = false
            };
        }
    }
}
