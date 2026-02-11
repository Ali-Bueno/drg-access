using HarmonyLib;
using DRS.UI;
using drgAccess.Helpers;

namespace drgAccess.Patches
{
    // Announce objective when it first appears on screen
    [HarmonyPatch(typeof(UIObjective), nameof(UIObjective.Show))]
    public static class UIObjective_Show
    {
        [HarmonyPostfix]
        public static void Postfix(UIObjective __instance)
        {
            try
            {
                var desc = __instance.description;
                if (desc == null || string.IsNullOrEmpty(desc.text)) return;

                string text = "Objective: " + TextHelper.CleanText(desc.text);

                var prog = __instance.progress;
                if (prog != null && !string.IsNullOrEmpty(prog.text))
                    text += ". " + TextHelper.CleanText(prog.text);

                ScreenReader.Say(text);
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"UIObjective.Show announce error: {ex.Message}");
            }
        }
    }

    // Announce progress updates (throttled to avoid spam)
    [HarmonyPatch(typeof(UIObjective), nameof(UIObjective.OnProgress))]
    public static class UIObjective_OnProgress
    {
        private static float lastAnnouncedTime = 0f;
        private const float THROTTLE_INTERVAL = 3f;

        [HarmonyPostfix]
        public static void Postfix(UIObjective __instance)
        {
            try
            {
                float time = UnityEngine.Time.time;
                if (time - lastAnnouncedTime < THROTTLE_INTERVAL) return;

                var desc = __instance.description;
                var prog = __instance.progress;
                if (prog == null || string.IsNullOrEmpty(prog.text)) return;

                string text = "";
                if (desc != null && !string.IsNullOrEmpty(desc.text))
                    text = TextHelper.CleanText(desc.text) + ": ";
                text += TextHelper.CleanText(prog.text);

                ScreenReader.Say(text);
                lastAnnouncedTime = time;
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"UIObjective.OnProgress announce error: {ex.Message}");
            }
        }
    }

    // Announce objective completion
    [HarmonyPatch(typeof(UIObjective), nameof(UIObjective.OnObjectiveComplete))]
    public static class UIObjective_OnObjectiveComplete
    {
        [HarmonyPostfix]
        public static void Postfix(UIObjective __instance)
        {
            try
            {
                string text = "Objective complete";
                var desc = __instance.description;
                if (desc != null && !string.IsNullOrEmpty(desc.text))
                    text += ": " + TextHelper.CleanText(desc.text);

                ScreenReader.Interrupt(text);
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"UIObjective.OnObjectiveComplete announce error: {ex.Message}");
            }
        }
    }
}
