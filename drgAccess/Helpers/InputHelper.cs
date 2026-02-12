using UnityEngine.InputSystem;

namespace drgAccess.Helpers;

/// <summary>
/// Shared input helper that checks both keyboard and gamepad for mod actions.
/// Gamepad mapping: D-Pad for navigation, A=confirm, B=cancel, Y=audio menu,
/// LB=HP, RB=wallet, L3=compass.
/// </summary>
public static class InputHelper
{
    public static bool NavigateUp()
    {
        return KeyPressed(Key.W) || KeyPressed(Key.UpArrow) || DpadUp();
    }

    public static bool NavigateDown()
    {
        return KeyPressed(Key.S) || KeyPressed(Key.DownArrow) || DpadDown();
    }

    public static bool Confirm()
    {
        return KeyPressed(Key.Enter) || KeyPressed(Key.NumpadEnter) || GamepadPressed(gp => gp.buttonSouth);
    }

    public static bool Cancel()
    {
        return KeyPressed(Key.Escape) || GamepadPressed(gp => gp.buttonEast);
    }

    public static bool ToggleAudioMenu()
    {
        return KeyPressed(Key.Backspace) || GamepadPressed(gp => gp.buttonNorth);
    }

    public static bool ReadHP()
    {
        return KeyPressed(Key.H) || GamepadPressed(gp => gp.leftShoulder);
    }

    public static bool ReadWallet()
    {
        return KeyPressed(Key.G) || GamepadPressed(gp => gp.rightShoulder);
    }

    public static bool Compass()
    {
        return KeyPressed(Key.F) || GamepadPressed(gp => gp.leftStickButton);
    }

    private static bool KeyPressed(Key key)
    {
        try
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }
        catch { return false; }
    }

    private static bool DpadUp()
    {
        try
        {
            var gp = Gamepad.current;
            return gp != null && gp.dpad.up.wasPressedThisFrame;
        }
        catch { return false; }
    }

    private static bool DpadDown()
    {
        try
        {
            var gp = Gamepad.current;
            return gp != null && gp.dpad.down.wasPressedThisFrame;
        }
        catch { return false; }
    }

    private static bool GamepadPressed(System.Func<Gamepad, UnityEngine.InputSystem.Controls.ButtonControl> selector)
    {
        try
        {
            var gp = Gamepad.current;
            return gp != null && selector(gp).wasPressedThisFrame;
        }
        catch { return false; }
    }
}
