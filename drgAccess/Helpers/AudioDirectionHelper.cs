using UnityEngine;

namespace drgAccess.Helpers;

/// <summary>
/// Shared helper for computing directional pitch modulation in top-down perspective.
/// Forward (W/up on screen) = higher pitch, Behind (S/down) = lower pitch.
/// </summary>
public static class AudioDirectionHelper
{
    /// <summary>
    /// The "forward" every spatial cue and every spoken direction is measured against.
    ///
    /// On foot this is the camera: the game is top-down, so "up" on screen is what W
    /// does. The Rock Dozer (EPlayerBehaviour.DRIVER) breaks that assumption — W is a
    /// throttle along the vehicle's nose and A/D steer it, so screen-relative cues stop
    /// matching the controls and movement "feels weird". While driving, directions are
    /// therefore measured from the vehicle's own facing, unless the player turns that
    /// off in the mod settings.
    /// </summary>
    public static Vector3 GetReferenceForward(Transform cameraTransform)
    {
        Vector3 cameraForward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
        cameraForward.y = 0;
        if (cameraForward.sqrMagnitude < 0.001f) cameraForward = Vector3.forward;
        cameraForward.Normalize();

        if (!ModConfig.GetBool(ModConfig.VEHICLE_RELATIVE_DIRECTIONS)) return cameraForward;

        if (TryGetVehicleFacing(cameraForward, out Vector3 vehicleForward))
            return vehicleForward;

        return cameraForward;
    }

    /// <summary>
    /// The vehicle's facing in world space, or false when the player is on foot.
    /// The game reports facing as a 2D vector in the same space as the move input
    /// (screen axes), so it maps onto the world through the camera's own axes.
    /// </summary>
    private static bool TryGetVehicleFacing(Vector3 cameraForward, out Vector3 facing)
    {
        facing = Vector3.zero;

        try
        {
            var player = PlayerLocator.FindPlayer();
            if (player == null) return false;

            var movement = player.PlayerMovement;
            if (movement == null) return false;
            if (movement.BehaviourType != EPlayerBehaviour.DRIVER) return false;

            Vector2 face = movement.GetFaceDirection();
            if (face.sqrMagnitude < 0.01f) return false;

            Vector3 cameraRight = new Vector3(cameraForward.z, 0, -cameraForward.x);
            facing = cameraRight * face.x + cameraForward * face.y;
            facing.y = 0;
            if (facing.sqrMagnitude < 0.001f) return false;

            facing.Normalize();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Whether the player is currently driving (Rock Dozer).</summary>
    public static bool IsDriving()
    {
        try
        {
            var player = PlayerLocator.FindPlayer();
            var movement = player?.PlayerMovement;
            return movement != null && movement.BehaviourType == EPlayerBehaviour.DRIVER;
        }
        catch
        {
            return false;
        }
    }

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

    /// <summary>
    /// Spoken 8-way direction to a target ("up-right", "left", ...), measured against
    /// the reference forward — so on foot it matches WASD on screen, and while driving
    /// it matches the vehicle's nose.
    /// </summary>
    public static string GetDirectionLabel(Vector3 forward, Vector3 toTarget)
    {
        Vector3 right = new Vector3(forward.z, 0, -forward.x);
        float dotForward = Vector3.Dot(forward, toTarget);
        float dotRight = Vector3.Dot(right, toTarget);

        const float diagThreshold = 0.38f;

        bool isForward = dotForward > diagThreshold;
        bool isBack = dotForward < -diagThreshold;
        bool isRight = dotRight > diagThreshold;
        bool isLeft = dotRight < -diagThreshold;

        if (isForward && isRight) return ModLocalization.Get("dir_up_right");
        if (isForward && isLeft) return ModLocalization.Get("dir_up_left");
        if (isBack && isRight) return ModLocalization.Get("dir_down_right");
        if (isBack && isLeft) return ModLocalization.Get("dir_down_left");
        if (isForward) return ModLocalization.Get("dir_up");
        if (isBack) return ModLocalization.Get("dir_down");
        if (isRight) return ModLocalization.Get("dir_right");
        if (isLeft) return ModLocalization.Get("dir_left");
        return ModLocalization.Get("dir_ahead");
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
