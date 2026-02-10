using DRS.UI;
using System.Text;
using drgAccess.Helpers;

namespace drgAccess.Patches;

// Pause menu weapon and artifact button text extraction
public static partial class UIButtonPatch
{
    private static string GetPauseWeaponText(UIPauseWeapon button)
    {
        try
        {
            var handler = button.weaponHandler;
            if (handler == null) return null;

            var sb = new StringBuilder();

            var data = handler.Data;
            if (data != null)
            {
                string title = data.Title;
                if (!string.IsNullOrEmpty(title))
                    sb.Append(TextHelper.CleanText(title));
            }

            int level = handler.weaponLevel;
            if (level > 0)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append($"Level {level}");
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetPauseWeaponText error: {ex.Message}");
            return null;
        }
    }

    private static string GetPauseArtifactText(UIPauseArtifact button)
    {
        try
        {
            var data = button.artifactData;
            if (data == null) return null;

            string title = data.Title;
            if (string.IsNullOrEmpty(title)) return null;

            return TextHelper.CleanText(title);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"UIButtonPatch.GetPauseArtifactText error: {ex.Message}");
            return null;
        }
    }
}
