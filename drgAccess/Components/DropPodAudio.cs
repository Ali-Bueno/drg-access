using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using UnityEngine.InputSystem;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components
{
    /// <summary>
    /// Beacon mode determines the waveform character.
    /// </summary>
    public enum BeaconMode
    {
        DropPod,    // Sonar ping: metallic ringing with slow decay
        SupplyDrop  // Warble/trill: rapid frequency oscillation, mechanical buzz
    }

    /// <summary>
    /// Beacon beep generator with distinct modes for drop pod and supply drop.
    /// DropPod: sonar-like metallic ping with reverberant ring.
    /// SupplyDrop: warbling trill with rapid frequency oscillation.
    /// </summary>
    public class BeaconBeepGenerator : ISampleProvider
    {
        private readonly WaveFormat waveFormat;
        private readonly int sampleRate;
        private double phase;
        private double phase2; // Second oscillator for detuned ring / warble

        // Beep parameters (set from main thread)
        private volatile float targetFrequency = 600f;
        private volatile float targetVolume = 0f;
        private volatile int targetIntervalSamples;
        private volatile bool active = false;
        private volatile bool doubleBeep = false;
        private volatile int modeInt = 0;

        // Internal state (audio thread only)
        private int sampleCounter = 0;
        private int intervalSamples;
        private float currentFrequency = 600f;
        private float currentVolume = 0f;

        public WaveFormat WaveFormat => waveFormat;

        public float Frequency
        {
            set => targetFrequency = Math.Max(100f, Math.Min(5000f, value));
        }

        public float Volume
        {
            set => targetVolume = Math.Max(0, Math.Min(1, value));
        }

        public float Interval
        {
            set => targetIntervalSamples = (int)(sampleRate * Math.Max(0.02f, Math.Min(2f, value)));
        }

        public bool Active
        {
            set => active = value;
        }

        public bool DoubleBeep
        {
            set => doubleBeep = value;
        }

        public BeaconMode Mode
        {
            set => modeInt = (int)value;
        }

        public BeaconBeepGenerator(int sampleRate = 44100)
        {
            this.sampleRate = sampleRate;
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            intervalSamples = (int)(sampleRate * 0.25);
            targetIntervalSamples = intervalSamples;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            currentFrequency = targetFrequency;
            currentVolume = targetVolume;
            intervalSamples = targetIntervalSamples;
            bool isDouble = doubleBeep;
            var mode = (BeaconMode)modeInt;

            // Duration depends on mode
            int beepDuration = mode == BeaconMode.DropPod
                ? (int)(sampleRate * 0.10) // 100ms sonar ping (longer ring)
                : (int)(sampleRate * 0.07); // 70ms warble trill
            int gapSamples = (int)(sampleRate * 0.03);
            int secondBeepStart = beepDuration + gapSamples;
            int secondBeepEnd = secondBeepStart + beepDuration;

            for (int i = 0; i < count; i++)
            {
                if (!active || currentVolume < 0.001f)
                {
                    buffer[offset + i] = 0;
                    continue;
                }

                bool isInFirstBeep = sampleCounter < beepDuration;
                bool isInSecondBeep = isDouble &&
                    sampleCounter >= secondBeepStart &&
                    sampleCounter < secondBeepEnd;

                if (isInFirstBeep || isInSecondBeep)
                {
                    int beepOffset = isInFirstBeep ? sampleCounter : (sampleCounter - secondBeepStart);
                    float progress = (float)beepOffset / beepDuration;

                    float sample;
                    if (mode == BeaconMode.DropPod)
                        sample = GenerateDropPodSample(progress, isInSecondBeep);
                    else
                        sample = GenerateSupplyDropSample(progress);

                    buffer[offset + i] = currentVolume * sample;
                }
                else
                {
                    buffer[offset + i] = 0;
                    phase = 0;
                    phase2 = 0;
                }

                sampleCounter++;
                if (sampleCounter >= intervalSamples)
                    sampleCounter = 0;
            }
            return count;
        }

        /// <summary>
        /// Sonar ping: sharp attack, slow reverberant decay, metallic ring from detuned oscillators.
        /// Sounds like a submarine sonar — nothing like enemy beeps.
        /// </summary>
        private float GenerateDropPodSample(float progress, bool isSecondBeep)
        {
            // Slow, smooth decay (reverberant ring)
            float envelope = (float)Math.Exp(-progress * 3.5);

            // Second beep pitched up (ascending "PING-ping")
            double freqBase = isSecondBeep ? currentFrequency * 1.25 : currentFrequency;

            // Main tone + slightly detuned copy = metallic beating/ringing
            double detuneRatio = 1.008; // ~14 cents sharp = audible beating
            double s1 = Math.Sin(2.0 * Math.PI * phase);
            double s2 = Math.Sin(2.0 * Math.PI * phase2) * 0.7;
            // Add 5th harmonic for metallic ring quality
            double s3 = Math.Sin(2.0 * Math.PI * phase * 5.0) * 0.08 * (1.0 - progress);

            phase += freqBase / sampleRate;
            phase2 += (freqBase * detuneRatio) / sampleRate;
            if (phase >= 1.0) phase -= 1.0;
            if (phase2 >= 1.0) phase2 -= 1.0;

            return envelope * (float)((s1 + s2 + s3) * 0.55);
        }

        /// <summary>
        /// Warble/trill: rapid frequency oscillation creates a mechanical buzzing trill.
        /// Completely different texture from sonar ping and enemy sine beeps.
        /// </summary>
        private float GenerateSupplyDropSample(float progress)
        {
            // Quick attack, medium decay
            float envelope = progress < 0.05f
                ? progress / 0.05f
                : (float)Math.Exp(-(progress - 0.05) * 5.0);

            // Rapid frequency warble: 18 Hz oscillation ±15% of base frequency
            double warble = Math.Sin(2.0 * Math.PI * 18.0 * progress) * 0.15;
            double freq = currentFrequency * (1.0 + warble);

            // Sawtooth-ish wave (gives mechanical/buzzy quality)
            double sawPhase = (phase * 2.0) % 2.0 - 1.0; // -1 to 1 sawtooth
            double sine = Math.Sin(2.0 * Math.PI * phase);
            double sample = sine * 0.6 + sawPhase * 0.3;

            // Add octave below for body
            double sub = Math.Sin(2.0 * Math.PI * phase2) * 0.2;

            phase += freq / sampleRate;
            phase2 += (freq * 0.5) / sampleRate;
            if (phase >= 1.0) phase -= 1.0;
            if (phase2 >= 1.0) phase2 -= 1.0;

            return envelope * (float)(sample + sub);
        }
    }

    /// <summary>
    /// Audio beacon for the Drop Pod using percussive chirp beeps.
    /// Beeps accelerate as the player approaches the pod ramp.
    /// At very close range (< 2.5m), adds a continuous ramp tone for precise guidance.
    /// </summary>
    public class DropPodAudio : MonoBehaviour
    {
        public static DropPodAudio Instance { get; private set; }

        // Shared audio output
        private WaveOutEvent outputDevice;
        private MixingSampleProvider mixer;

        // Beacon chirp (normal + critical distance)
        private BeaconBeepGenerator beepGenerator;
        private PanningSampleProvider beepPanProvider;

        // Ramp proximity tone (< 3m, continuous pulsing)
        private SineWaveGenerator rampToneGenerator;
        private PanningSampleProvider rampTonePanProvider;
        private VolumeSampleProvider rampToneVolumeProvider;

        // Drop pod reference
        private DropPod activePod;
        private bool isBeaconActive = false;

        // Player references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime = 0f;

        // State
        private bool isInitialized = false;
        private float maxDistance = 150f;
        private const float CRITICAL_DISTANCE = 8f;
        private const float RAMP_DISTANCE = 2.5f;
        private bool announcedCriticalProximity = false;
        private bool announcedRampProximity = false;

        // Pathfinding
        private NavMeshPathHelper.PathResult currentPathResult;

        // Game state
        private IGameStateProvider gameStateProvider;
        private string lastSceneName = "";

        static DropPodAudio()
        {
            ClassInjector.RegisterTypeInIl2Cpp<DropPodAudio>();
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
            Plugin.Log.LogInfo("[DropPodAudio] Initialized");
        }

        void Start()
        {
            InitializeAudio();
        }

        private void InitializeAudio()
        {
            try
            {
                var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                mixer = new MixingSampleProvider(format) { ReadFully = true };

                // Beacon sonar pings (normal navigation)
                beepGenerator = new BeaconBeepGenerator();
                beepGenerator.Mode = BeaconMode.DropPod;
                beepPanProvider = new PanningSampleProvider(beepGenerator) { Pan = 0f };
                var beepVolumeProvider = new VolumeSampleProvider(beepPanProvider) { Volume = 1.0f };
                mixer.AddMixerInput(beepVolumeProvider);

                // Ramp proximity tone (continuous pulsing, distinct from chirps)
                rampToneGenerator = new SineWaveGenerator(1600, 0f);
                rampTonePanProvider = new PanningSampleProvider(rampToneGenerator) { Pan = 0f };
                rampToneVolumeProvider = new VolumeSampleProvider(rampTonePanProvider) { Volume = 0f };
                mixer.AddMixerInput(rampToneVolumeProvider);

                outputDevice = new WaveOutEvent();
                outputDevice.Init(mixer);
                outputDevice.Play();

                isInitialized = true;
                Plugin.Log.LogInfo("[DropPodAudio] Audio initialized (beacon + ramp proximity tone)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] Failed to initialize: {e.Message}");
            }
        }

        void Update()
        {
            if (!isInitialized) return;

            try
            {
                CheckSceneChange();

                if (!IsInActiveGameplay())
                {
                    beepGenerator.Active = false;
                    rampToneGenerator.Volume = 0f;
                    return;
                }

                if (Time.time >= nextPlayerSearchTime)
                {
                    FindPlayer();
                    nextPlayerSearchTime = Time.time + 2f;
                }

                if (playerTransform == null)
                {
                    beepGenerator.Active = false;
                    rampToneGenerator.Volume = 0f;
                    return;
                }

                if (isBeaconActive && activePod != null)
                {
                    UpdatePathfinding();
                    UpdateBeacon();
                    HandleCompassKey();
                }
                else
                {
                    beepGenerator.Active = false;
                    rampToneGenerator.Volume = 0f;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] Update error: {e.Message}");
            }
        }

        public void OnPodLanded(DropPod pod)
        {
            activePod = pod;
            isBeaconActive = true;
            announcedCriticalProximity = false;
            announcedRampProximity = false;
            NavMeshPathHelper.Reset();
            Plugin.Log.LogInfo("[DropPodAudio] Beacon activated - extraction pod landed!");
        }

        public void OnPlayerEntered()
        {
            isBeaconActive = false;
            beepGenerator.Active = false;
            rampToneGenerator.Volume = 0f;

            ScreenReader.Interrupt("Inside the pod");
            Plugin.Log.LogInfo("[DropPodAudio] Player entered pod - beacon stopped");
        }

        /// <summary>
        /// Gets the target position for beacon guidance.
        /// Prefers the ramp (entry side) over the pod center, since the player
        /// needs to walk onto the ramp to enter the pod.
        /// </summary>
        private Vector3 GetBeaconTargetPosition()
        {
            try
            {
                // Prefer ramp detector position - that's where the player enters
                var ramp = activePod.rampDetector;
                if (ramp != null)
                    return ramp.position;
            }
            catch { }

            // Fall back to pod transform
            var podTransform = activePod.podTransform;
            return podTransform != null ? podTransform.position : Vector3.zero;
        }

        /// <summary>
        /// Gets the pod interior target position.
        /// playerPoint is the exact spot that triggers pod departure.
        /// Used to guide the player FROM the ramp INTO the pod.
        /// </summary>
        private Vector3 GetPodInteriorPosition()
        {
            try
            {
                // playerPoint is where the player must stand to trigger departure
                var point = activePod.playerPoint;
                if (point != null)
                    return point.position;
            }
            catch { }

            try
            {
                var center = activePod.podCenter;
                if (center != null)
                    return center.position;
            }
            catch { }

            // Fall back to pod transform
            var podTransform = activePod.podTransform;
            return podTransform != null ? podTransform.position : Vector3.zero;
        }

        private (float pathDistance, float directDistance, float pan, float facingDot) GetPodDirectionInfo()
        {
            Vector3 podPos = GetBeaconTargetPosition();
            if (podPos == Vector3.zero) return (-1f, -1f, 0f, 0f);

            Vector3 playerPos = playerTransform.position;
            float directDistance = Vector3.Distance(playerPos, podPos);

            // Direction toward next waypoint (path-guided) or pod directly
            Vector3 waypointPos = currentPathResult.IsValid
                ? currentPathResult.NextWaypoint
                : podPos;

            float pathDistance = currentPathResult.IsValid
                ? currentPathResult.TotalPathDistance
                : directDistance;

            Vector3 toWaypoint = waypointPos - playerPos;
            toWaypoint.y = 0;
            toWaypoint.Normalize();

            Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            forward.y = 0;
            forward.Normalize();
            Vector3 right = new Vector3(forward.z, 0, -forward.x);

            float pan = Mathf.Clamp(Vector3.Dot(toWaypoint, right), -1f, 1f);
            float facingDot = Vector3.Dot(forward, toWaypoint);
            return (pathDistance, directDistance, pan, facingDot);
        }

        /// <summary>
        /// Get screen-relative direction from player to pod ramp.
        /// Top-down game: camera is fixed, so "up" = W key direction, "right" = D key direction, etc.
        /// </summary>
        private string GetScreenDirection()
        {
            try
            {
                // Point toward next waypoint (path-guided) or pod directly
                Vector3 targetPos = currentPathResult.IsValid
                    ? currentPathResult.NextWaypoint
                    : GetBeaconTargetPosition();
                if (targetPos == Vector3.zero) return "unknown";

                Vector3 toTarget = targetPos - playerTransform.position;
                toTarget.y = 0;
                toTarget.Normalize();

                // Camera forward projected to XZ = "up on screen" (W key direction)
                Vector3 screenUp = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                screenUp.y = 0;
                screenUp.Normalize();
                Vector3 screenRight = new Vector3(screenUp.z, 0, -screenUp.x);

                float upDot = Vector3.Dot(toTarget, screenUp);     // >0 = up on screen
                float rightDot = Vector3.Dot(toTarget, screenRight); // >0 = right on screen

                // Determine direction using thresholds
                bool isUp = upDot > 0.38f;
                bool isDown = upDot < -0.38f;
                bool isRight = rightDot > 0.38f;
                bool isLeft = rightDot < -0.38f;

                if (isUp && isRight) return "up-right";
                if (isUp && isLeft) return "up-left";
                if (isDown && isRight) return "down-right";
                if (isDown && isLeft) return "down-left";
                if (isUp) return "up";
                if (isDown) return "down";
                if (isRight) return "right";
                if (isLeft) return "left";
                return "here";
            }
            catch { return "unknown"; }
        }

        private void UpdatePathfinding()
        {
            if (activePod == null || playerTransform == null) return;

            Vector3 targetPos = GetBeaconTargetPosition();
            if (targetPos == Vector3.zero) return;

            currentPathResult = NavMeshPathHelper.GetNextWaypoint(
                playerTransform.position, targetPos);
        }

        private void UpdateBeacon()
        {
            try
            {
                var (pathDistance, directDistance, pan, facingDot) = GetPodDirectionInfo();
                if (pathDistance < 0)
                {
                    beepGenerator.Active = false;
                    rampToneGenerator.Volume = 0f;
                    return;
                }

                // Top-down game: facingDot tells us if waypoint is "up" or "down" on screen
                float verticalFactor = (facingDot + 1f) / 2f; // 0 = bottom, 1 = top
                float pitchMultiplier = 0.4f + verticalFactor * 0.6f;
                float facingVolumeMultiplier = 0.8f + verticalFactor * 0.2f;

                // Use direct distance for proximity thresholds (physical closeness to pod)
                bool isCritical = directDistance < CRITICAL_DISTANCE;
                bool isOnRamp = directDistance < RAMP_DISTANCE;

                beepPanProvider.Pan = pan;

                // Ramp proximity: continuous tone guides player INTO the pod
                if (isOnRamp)
                {
                    float rampFactor = 1f - Mathf.Clamp01(directDistance / RAMP_DISTANCE);
                    rampToneGenerator.Frequency = 1200f + rampFactor * 400f;
                    rampToneGenerator.Volume = 0.15f + rampFactor * 0.20f;
                    rampToneVolumeProvider.Volume = 1.0f;

                    // Pan toward pod interior so the player knows which direction enters the pod
                    Vector3 interiorPos = GetPodInteriorPosition();
                    Vector3 toInterior = interiorPos - playerTransform.position;
                    toInterior.y = 0;
                    toInterior.Normalize();
                    Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                    forward.y = 0;
                    forward.Normalize();
                    Vector3 right = new Vector3(forward.z, 0, -forward.x);
                    float interiorPan = Mathf.Clamp(Vector3.Dot(toInterior, right), -1f, 1f);
                    rampTonePanProvider.Pan = interiorPan;

                    // Also steer the beacon beeps toward the interior
                    beepPanProvider.Pan = interiorPan;

                    if (!announcedRampProximity)
                    {
                        announcedRampProximity = true;
                        ScreenReader.Interrupt("On the ramp, follow the tone inside");
                    }
                }
                else
                {
                    rampToneGenerator.Volume = 0f;
                }

                if (isCritical)
                {
                    // Critical proximity: double-beep pattern, high pitch, louder
                    float criticalFactor = 1f - Mathf.Clamp01(directDistance / CRITICAL_DISTANCE);

                    beepGenerator.Frequency = (1200 + criticalFactor * 400) * pitchMultiplier;
                    beepGenerator.Volume = (0.4f + criticalFactor * 0.15f) * facingVolumeMultiplier;
                    beepGenerator.Interval = Mathf.Lerp(0.12f, 0.06f, criticalFactor);
                    beepGenerator.DoubleBeep = true;
                    beepGenerator.Active = true;

                    if (!announcedCriticalProximity)
                    {
                        announcedCriticalProximity = true;
                        ScreenReader.Interrupt("Drop pod very close");
                    }
                }
                else
                {
                    // Normal beacon chirps - use path distance for accurate "how far to walk"
                    float proximityFactor = 1f - Mathf.Clamp01(pathDistance / maxDistance);
                    proximityFactor = proximityFactor * proximityFactor;

                    float interval = Mathf.Lerp(0.25f, 0.03f, proximityFactor);

                    beepGenerator.Frequency = (800 + proximityFactor * 600) * pitchMultiplier;
                    beepGenerator.Volume = (0.25f + proximityFactor * 0.2f) * facingVolumeMultiplier;
                    beepGenerator.Interval = interval;
                    beepGenerator.DoubleBeep = false;
                    beepGenerator.Active = true;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] UpdateBeacon error: {e.Message}");
            }
        }

        private void HandleCompassKey()
        {
            try
            {
                var kb = Keyboard.current;
                if (kb == null) return;
                if (!kb[Key.F].wasPressedThisFrame) return;
                if (playerTransform == null) return;

                Vector3 podPos = GetBeaconTargetPosition();
                if (podPos == Vector3.zero) return;

                // Use path distance if available, otherwise direct distance
                float distance = currentPathResult.IsValid
                    ? currentPathResult.TotalPathDistance
                    : Vector3.Distance(playerTransform.position, podPos);

                string direction = GetScreenDirection();
                int meters = Mathf.RoundToInt(distance);

                ScreenReader.Interrupt($"Drop pod: {direction}, {meters} meters");
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"[DropPodAudio] HandleCompassKey error: {e.Message}");
            }
        }

        private void CheckSceneChange()
        {
            try
            {
                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(lastSceneName) && currentScene != lastSceneName)
                {
                    Plugin.Log.LogInfo($"[DropPodAudio] Scene changed to {currentScene} - resetting");
                    isBeaconActive = false;
                    activePod = null;
                    announcedCriticalProximity = false;
                    announcedRampProximity = false;
                    playerTransform = null;
                    cameraTransform = null;
                    nextPlayerSearchTime = 0f;
                    gameStateProvider = null;
                    NavMeshPathHelper.Reset();
                    beepGenerator.Active = false;
                    rampToneGenerator.Volume = 0f;
                }
                lastSceneName = currentScene;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DropPodAudio] CheckSceneChange error: {e.Message}");
            }
        }

        private void FindPlayer()
        {
            try
            {
                if (playerTransform == null)
                {
                    string[] playerNames = { "Player", "PlayerCharacter", "Hero", "Character" };
                    foreach (var name in playerNames)
                    {
                        var obj = GameObject.Find(name);
                        if (obj != null && !obj.name.Contains("Camera"))
                        {
                            playerTransform = obj.transform;
                            break;
                        }
                    }
                }
                if (cameraTransform == null)
                {
                    var cam = Camera.main;
                    if (cam != null) cameraTransform = cam.transform;
                }
            }
            catch { }
        }

        private bool IsInActiveGameplay()
        {
            try
            {
                if (Time.timeScale <= 0.1f) return false;

                if (gameStateProvider != null)
                {
                    var gc = gameStateProvider.TryCast<GameController>();
                    if (gc == null) gameStateProvider = null;
                }

                if (gameStateProvider == null)
                {
                    var gameController = UnityEngine.Object.FindObjectOfType<GameController>();
                    if (gameController != null)
                        gameStateProvider = gameController.Cast<IGameStateProvider>();
                    else
                        return false;
                }

                if (gameStateProvider != null)
                {
                    var state = gameStateProvider.State;
                    return state == GameController.EGameState.CORE ||
                           state == GameController.EGameState.CORE_OUTRO;
                }
            }
            catch { }
            return false;
        }

        void OnDestroy()
        {
            try
            {
                outputDevice?.Stop();
                outputDevice?.Dispose();
            }
            catch { }
            Instance = null;
            Plugin.Log.LogInfo("[DropPodAudio] Destroyed");
        }
    }
}
