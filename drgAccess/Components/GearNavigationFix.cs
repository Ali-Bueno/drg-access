using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Il2CppInterop.Runtime.Injection;
using DRS.UI;
using drgAccess.Patches;

namespace drgAccess.Components
{
    /// <summary>
    /// Fixes gear inventory navigation by setting explicit Up/Down links
    /// on UIGearViewCompact buttons, preventing random cursor jumping.
    /// Activates when gear inventory form is open.
    /// </summary>
    public class GearNavigationFix : MonoBehaviour
    {
        public static GearNavigationFix Instance { get; private set; }

        private bool lastGearOpen = false;
        private int lastFixFrame = -1;
        private int lastChildCount = 0;

        static GearNavigationFix()
        {
            ClassInjector.RegisterTypeInIl2Cpp<GearNavigationFix>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            try
            {
                bool gearOpen = UIFormPatches.GearInventoryOpen;

                // Fix navigation when form opens or tab changes (child count changes)
                if (gearOpen)
                {
                    // Re-fix when form just opened, or periodically check for changes
                    if (!lastGearOpen || Time.frameCount - lastFixFrame > 30)
                    {
                        FixGearNavigation();
                    }
                }

                lastGearOpen = gearOpen;
            }
            catch (Exception e)
            {
                if (Time.frameCount % 300 == 0)
                    Plugin.Log.LogError($"[GearNavFix] Update error: {e.Message}");
            }
        }

        private void FixGearNavigation()
        {
            try
            {
                var gearForm = UIFormPatches.CachedGearForm;
                if (gearForm == null) return;

                // Find all active gear buttons
                var allGear = gearForm.GetComponentsInChildren<UIGearViewCompact>(false);
                if (allGear == null || allGear.Length == 0) return;

                // Only visible ones (active in hierarchy)
                var visibleGear = new List<UIGearViewCompact>();
                foreach (var gear in allGear)
                {
                    if (gear != null && gear.gameObject.activeInHierarchy)
                        visibleGear.Add(gear);
                }

                if (visibleGear.Count == 0) return;
                if (visibleGear.Count == lastChildCount && Time.frameCount - lastFixFrame < 60)
                    return; // No change, skip

                lastChildCount = visibleGear.Count;
                lastFixFrame = Time.frameCount;

                // Set explicit Up/Down navigation for sequential browsing
                for (int i = 0; i < visibleGear.Count; i++)
                {
                    var selectable = visibleGear[i].GetComponent<Selectable>();
                    if (selectable == null) continue;

                    var nav = selectable.navigation;
                    nav.mode = Navigation.Mode.Explicit;

                    // Up → previous item (wrap to last)
                    if (i > 0)
                    {
                        var prevSel = visibleGear[i - 1].GetComponent<Selectable>();
                        if (prevSel != null) nav.selectOnUp = prevSel;
                    }
                    else
                    {
                        var lastSel = visibleGear[visibleGear.Count - 1].GetComponent<Selectable>();
                        if (lastSel != null) nav.selectOnUp = lastSel;
                    }

                    // Down → next item (wrap to first)
                    if (i < visibleGear.Count - 1)
                    {
                        var nextSel = visibleGear[i + 1].GetComponent<Selectable>();
                        if (nextSel != null) nav.selectOnDown = nextSel;
                    }
                    else
                    {
                        var firstSel = visibleGear[0].GetComponent<Selectable>();
                        if (firstSel != null) nav.selectOnDown = firstSel;
                    }

                    // Left/Right: keep default or null (tabs handle these)
                    nav.selectOnLeft = null;
                    nav.selectOnRight = null;

                    selectable.navigation = nav;
                }

                Plugin.Log.LogDebug($"[GearNavFix] Fixed navigation for {visibleGear.Count} gear items");
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[GearNavFix] FixGearNavigation error: {e.Message}");
            }
        }

        void OnDestroy()
        {
            Instance = null;
        }
    }
}
