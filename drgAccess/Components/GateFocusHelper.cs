using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;
using DRS.UI;

namespace drgAccess.Components
{
    /// <summary>
    /// Keyboard/gamepad focus for biome and mission gate buttons.
    /// Gates are not reachable through the game's normal keyboard navigation
    /// (users reported having to hover them with the mouse), so Tab / R3 moves
    /// the EventSystem selection directly to the next gate button on screen.
    /// Selecting it fires the existing UIButton.OnSelect announcement, and
    /// Enter activates it through the existing OnUpdateSelected gate fix.
    /// Does nothing on screens without gates.
    /// </summary>
    public class GateFocusHelper : MonoBehaviour
    {
        public static GateFocusHelper Instance { get; private set; }

        private int cycleIndex = -1;

        static GateFocusHelper()
        {
            ClassInjector.RegisterTypeInIl2Cpp<GateFocusHelper>();
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
            Plugin.Log.LogInfo("[GateFocus] Initialized");
        }

        void Update()
        {
            try
            {
                if (!InputHelper.FocusGate()) return;
                FocusNextGate();
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[GateFocus] Update error: {e.Message}");
            }
        }

        private void FocusNextGate()
        {
            var gates = new List<GameObject>();

            try
            {
                var missionGates = UnityEngine.Object.FindObjectsOfType<UIMissionGateButton>();
                if (missionGates != null)
                {
                    foreach (var g in missionGates)
                    {
                        if (g != null && g.gameObject.activeInHierarchy)
                            gates.Add(g.gameObject);
                    }
                }
            }
            catch { }

            try
            {
                var biomeGates = UnityEngine.Object.FindObjectsOfType<UIBiomeSelectButton_Gate>();
                if (biomeGates != null)
                {
                    foreach (var g in biomeGates)
                    {
                        if (g != null && g.gameObject.activeInHierarchy)
                            gates.Add(g.gameObject);
                    }
                }
            }
            catch { }

            if (gates.Count == 0) return; // No gates on this screen — Tab stays inert

            var es = EventSystem.current;
            if (es == null) return;

            cycleIndex = (cycleIndex + 1) % gates.Count;
            es.SetSelectedGameObject(gates[cycleIndex]);
            Plugin.Log.LogInfo($"[GateFocus] Focused gate {cycleIndex + 1}/{gates.Count}: {gates[cycleIndex].name}");
        }
    }
}
