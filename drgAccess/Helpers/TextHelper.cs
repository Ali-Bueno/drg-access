using System.Linq;
using System.Text.RegularExpressions;

namespace drgAccess.Helpers;

/// <summary>
/// Shared text cleaning utilities used across all patch files.
/// </summary>
public static class TextHelper
{
    /// <summary>
    /// Removes rich text tags, serial number patterns, and normalizes whitespace.
    /// </summary>
    public static string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Remove rich text tags (includes TMP sprite tags like <sprite=888>)
        text = Regex.Replace(text, "<[^>]+>", "");

        // Remove serial number patterns like "nº cm-718-689" or "n° XX-XXX-XXX"
        text = Regex.Replace(text, @"[Nn][º°]\s*\S+", "");

        // Clean up multiple spaces and whitespace
        text = Regex.Replace(text, @"\s+", " ");
        text = text.Trim();

        return text;
    }

    /// <summary>
    /// Checks if text is just numbers, spaces, dots, or commas (score format).
    /// </summary>
    public static bool IsJustNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (char c in text)
        {
            if (!char.IsDigit(c) && c != ' ' && c != '.' && c != ',')
                return false;
        }
        return text.Any(char.IsDigit);
    }
}
