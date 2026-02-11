using UnityEngine;

namespace drgAccess.Helpers;

/// <summary>
/// Shared helper for computing directional pitch modulation in top-down perspective.
/// Forward (W/up on screen) = higher pitch, Behind (S/down) = lower pitch.
/// </summary>
public static class AudioDirectionHelper
{
    /// <summary>
    /// Returns a pitch multiplier: 1.0x when target is ahead (W), 0.6x when behind (S).
    /// </summary>
    public static float GetDirectionalPitchMultiplier(Vector3 cameraForward, Vector3 toTarget)
    {
        Vector3 forward = cameraForward;
        forward.y = 0;
        forward.Normalize();
        toTarget.y = 0;
        toTarget.Normalize();
        float facingDot = Vector3.Dot(forward, toTarget);
        float verticalFactor = (facingDot + 1f) / 2f;
        return 0.6f + verticalFactor * 0.4f;
    }

    // Pre-computed pitch multipliers for 8-direction systems (0=forward, 4=behind)
    private static readonly float[] _dirPitchMultipliers =
        { 1.0f, 0.94f, 0.8f, 0.66f, 0.6f, 0.66f, 0.8f, 0.94f };

    /// <summary>
    /// Returns a pitch multiplier from an 8-direction index (0=forward, 4=behind).
    /// </summary>
    public static float GetDirectionalPitchMultiplier(int dirIndex)
    {
        if (dirIndex < 0 || dirIndex >= 8) return 0.8f;
        return _dirPitchMultipliers[dirIndex];
    }
}
