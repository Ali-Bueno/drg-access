using System;
using UnityEngine;
using UnityEngine.UI;
using Il2CppInterop.Runtime.Injection;
using DRS.UI;

namespace drgAccess.Components
{
    /// <summary>
    /// Makes gate buttons reachable through the game's normal keyboard/gamepad
    /// navigation. Every UIButton carries a UnityEngine.UI.Button (Selectable),
    /// which is what arrow-key navigation moves between; gate buttons ship with
    /// theirs non-interactable / non-navigable, so the selection can never reach
    /// them (players had to hover them with the mouse). This component enables
    /// the gate's own Button and sets its navigation to Automatic so Unity's
    /// standard navigation includes it — no extra keys, same navigation as the
    /// rest of the map. Activation still goes through the existing
    /// UIButton.OnUpdateSelected Enter fix.
    /// </summary>
    public class GateNavigationFix : MonoBehaviour
    {
        public static GateNavigationFix Instance { get; private set; }

        private float nextScanTime;
        private const float SCAN_INTERVAL = 1f;

        static GateNavigationFix()
        {
            ClassInjector.RegisterTypeInIl2Cpp<GateNavigationFix>();
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
            Plugin.Log.LogInfo("[GateNavFix] Initialized");
        }

        void Update()
        {
            try
            {
                if (Time.time < nextScanTime) return;
                nextScanTime = Time.time + SCAN_INTERVAL;

                FixGates<UIMissionGateButton>();
                FixGates<UIBiomeSelectButton_Gate>();
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[GateNavFix] Update error: {e.Message}");
            }
        }

        private void FixGates<T>() where T : UIButton
        {
            T[] gates;
            try { gates = UnityEngine.Object.FindObjectsOfType<T>(); }
            catch { return; }
            if (gates == null) return;

            foreach (var gate in gates)
            {
                if (gate == null) continue;

                try
                {
                    if (!gate.gameObject.activeInHierarchy) continue;

                    // UIButton.button is the Unity Selectable that arrow-key
                    // navigation actually moves between
                    Selectable selectable = gate.button;
                    if (selectable == null)
                        selectable = gate.GetComponentInChildren<Selectable>();

                    if (selectable == null)
                    {
                        Plugin.Log.LogWarning($"[GateNavFix] Gate '{gate.gameObject.name}' has no Selectable — cannot join navigation");
                        continue;
                    }

                    bool changed = false;

                    if (!selectable.interactable)
                    {
                        selectable.interactable = true;
                        changed = true;
                    }

                    var nav = selectable.navigation;
                    if (nav.mode != Navigation.Mode.Automatic)
                    {
                        nav.mode = Navigation.Mode.Automatic;
                        selectable.navigation = nav;
                        changed = true;
                    }

                    if (changed)
                        Plugin.Log.LogInfo($"[GateNavFix] Gate '{gate.gameObject.name}' joined normal navigation");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogDebug($"[GateNavFix] FixGates error on '{typeof(T).Name}': {e.Message}");
                }
            }
        }

        void OnDestroy()
        {
            Instance = null;
        }
    }
}
