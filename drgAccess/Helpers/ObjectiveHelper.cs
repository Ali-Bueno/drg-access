using System;
using System.Collections.Generic;
using UnityEngine;

namespace drgAccess.Helpers
{
    /// <summary>
    /// Single source of truth for the current run's objectives.
    ///
    /// The pause form never references the objective tracker (it only shows biome
    /// milestones), and the HUD tracker only exposes whatever it happens to render,
    /// so both the pause reader and the collectible scanner read the run's real
    /// objectives from <see cref="LevelObjectiveTracker"/> instead.
    /// </summary>
    public static class ObjectiveHelper
    {
        public struct ObjectiveEntry
        {
            public string Text;
            public bool IsComplete;
            public bool IsPrimary;
        }

        private static LevelObjectiveTracker cachedTracker;
        private static float nextTrackerSearchTime;
        private static int cachedRunGeneration = -1;

        // Resources the current objectives ask for. Morkite & co. are ordinary
        // MineableBlocks (only materialType tells them apart), and Apoca Bloom /
        // Boolo Cap are LevelPickups, so the scanner needs both sets.
        private static readonly HashSet<ECurrency> objectiveCurrencies = new();
        private static readonly Dictionary<LevelPickup.EType, string> objectivePickupNames = new();
        private static float nextTargetRefreshTime;

        public static LevelObjectiveTracker GetTracker()
        {
            try
            {
                // A retry swaps the whole run without changing the scene, and the old
                // objects keep answering from stale il2cpp memory — so the cache is tied
                // to the run generation, never to a null check.
                if (cachedRunGeneration != GameStateHelper.RunGeneration)
                {
                    Reset();
                    cachedRunGeneration = GameStateHelper.RunGeneration;
                }

                if (cachedTracker != null) return cachedTracker;

                if (Time.time < nextTrackerSearchTime) return null;
                nextTrackerSearchTime = Time.time + 2f;

                cachedTracker = UnityEngine.Object.FindObjectOfType<LevelObjectiveTracker>();
                return cachedTracker;
            }
            catch
            {
                cachedTracker = null;
                return null;
            }
        }

        /// <summary>
        /// All objectives of the current run, primary first, as readable lines
        /// ("Collect Morkite: 7/20"). Empty when no run is active.
        /// </summary>
        public static List<ObjectiveEntry> GetObjectives()
        {
            var result = new List<ObjectiveEntry>();

            try
            {
                var tracker = GetTracker();
                if (tracker == null) return result;

                AppendObjectives(tracker.PrimaryObjectives, result);
                AppendObjectives(tracker.SecondaryObjectives, result);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogDebug($"[ObjectiveHelper] GetObjectives error: {e.Message}");
            }

            return result;
        }

        private static void AppendObjectives(Il2CppSystem.Collections.Generic.List<ObjectiveProgress> source,
            List<ObjectiveEntry> result)
        {
            if (source == null) return;

            for (int i = 0; i < source.Count; i++)
            {
                try
                {
                    var op = source[i];
                    if (op == null) continue;

                    var objective = op.Objective;
                    if (objective == null) continue;

                    string desc = null;
                    try { desc = objective.GetObjectiveString(op.runSettingsManager); }
                    catch { }

                    if (string.IsNullOrEmpty(desc))
                    {
                        try { desc = objective.LocDescription?.GetLocalizedString(); }
                        catch { }
                    }

                    desc = TextHelper.CleanText(desc);
                    if (string.IsNullOrEmpty(desc)) continue;

                    int target = 0;
                    try { target = objective.GetValue(op.runSettingsManager); }
                    catch { }

                    string text = target > 0
                        ? ModLocalization.Get("objective_with_progress", desc, op.Progress, target)
                        : desc;

                    if (op.IsComplete)
                        text += ", " + ModLocalization.Get("ui_completed");
                    else if (op.IsFailed)
                        text += ", " + ModLocalization.Get("objective_failed");

                    result.Add(new ObjectiveEntry
                    {
                        Text = text,
                        IsComplete = op.IsComplete,
                        IsPrimary = op.IsPrimary
                    });
                }
                catch (Exception e)
                {
                    Plugin.Log?.LogDebug($"[ObjectiveHelper] objective {i} error: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Refreshes which resources the run's objectives require. Completed
        /// objectives drop out, so their resources stop being flagged.
        /// </summary>
        private static void RefreshTargets()
        {
            if (Time.time < nextTargetRefreshTime) return;
            nextTargetRefreshTime = Time.time + 3f;

            objectiveCurrencies.Clear();
            objectivePickupNames.Clear();

            try
            {
                var tracker = GetTracker();
                if (tracker == null) return;

                var objectives = tracker.Objectives;
                if (objectives == null) return;

                for (int i = 0; i < objectives.Count; i++)
                {
                    try
                    {
                        var op = objectives[i];
                        if (op == null || op.IsComplete || op.IsFailed) continue;

                        var objective = op.Objective;
                        if (objective == null) continue;

                        var collect = objective.TryCast<LevelObjectiveCollect>();
                        if (collect != null)
                        {
                            objectiveCurrencies.Add(collect.materialType);
                            continue;
                        }

                        var levelPickup = objective.TryCast<LevelObjectiveLevelPickup>();
                        if (levelPickup != null)
                        {
                            var type = levelPickup.levelPickupType;
                            string name = null;
                            try { name = TextHelper.CleanText(levelPickup.TypeToString(type)); }
                            catch { }
                            objectivePickupNames[type] = name;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.LogDebug($"[ObjectiveHelper] RefreshTargets error: {e.Message}");
            }
        }

        /// <summary>True when the run needs this mineral for an objective (e.g. Morkite).</summary>
        public static bool IsObjectiveCurrency(ECurrency currency)
        {
            RefreshTargets();
            return objectiveCurrencies.Contains(currency);
        }

        /// <summary>True when the run needs this level pickup (Apoca Bloom / Boolo Cap).</summary>
        public static bool IsObjectivePickup(LevelPickup.EType type)
        {
            RefreshTargets();
            return objectivePickupNames.ContainsKey(type);
        }

        /// <summary>Localized name of an objective pickup, from the objective's own data.</summary>
        public static string GetObjectivePickupName(LevelPickup.EType type)
        {
            RefreshTargets();
            return objectivePickupNames.TryGetValue(type, out string name) ? name : null;
        }

        /// <summary>Drops cached state when a run ends (new GameController).</summary>
        public static void Reset()
        {
            cachedTracker = null;
            nextTrackerSearchTime = 0f;
            nextTargetRefreshTime = 0f;
            objectiveCurrencies.Clear();
            objectivePickupNames.Clear();
        }
    }
}
