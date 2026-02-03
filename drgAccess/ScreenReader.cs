using DavyKager;

namespace drgAccess;

/// <summary>
/// Service for screen reader output using Tolk.
/// Provides a simple interface for accessibility announcements.
/// </summary>
public static class ScreenReader
{
    /// <summary>
    /// Speaks text through the screen reader.
    /// </summary>
    /// <param name="text">Text to speak</param>
    /// <param name="interrupt">If true, interrupts current speech</param>
    public static void Say(string text, bool interrupt = false)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            Tolk.Speak(text, interrupt);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"ScreenReader.Say failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Speaks text and interrupts any current speech.
    /// </summary>
    public static void Interrupt(string text)
    {
        Say(text, true);
    }

    /// <summary>
    /// Outputs text to the screen reader's braille display.
    /// </summary>
    public static void Braille(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            Tolk.Braille(text);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"ScreenReader.Braille failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Outputs text to both speech and braille.
    /// </summary>
    public static void Output(string text, bool interrupt = false)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            Tolk.Output(text, interrupt);
        }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogError($"ScreenReader.Output failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops any current speech.
    /// </summary>
    public static void Silence()
    {
        try
        {
            Tolk.Silence();
        }
        catch { }
    }

    /// <summary>
    /// Checks if a screen reader is currently active.
    /// </summary>
    public static bool IsActive()
    {
        try
        {
            return Tolk.HasSpeech();
        }
        catch
        {
            return false;
        }
    }
}
