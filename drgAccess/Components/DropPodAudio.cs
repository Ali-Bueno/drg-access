using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using drgAccess.Helpers;

namespace drgAccess.Components
{
    /// <summary>
    /// Beacon mode determines the waveform character.
    /// </summary>
    public enum BeaconMode
    {
        DropPod,        // Sonar ping: metallic ringing with slow decay
        SupplyDrop,     // Warble/trill: rapid frequency oscillation, mechanical buzz
        Drill,          // Rhythmic rumble/chug: tremolo-modulated pulse, mechanical feel
        Cocoon,         // Organic pulse: heartbeat-like throb with detuned overtones
        TNT,            // Staccato tick: sharp clicking timer sound
        OmmoranCrystal  // Crystalline shimmer: glassy FM synthesis
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
        private volatile bool isDrillRunning = false;

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

        public bool DrillRunning
        {
            set => isDrillRunning = value;
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
            int beepDuration = mode switch
            {
                BeaconMode.DropPod => (int)(sampleRate * 0.10),    // 100ms sonar ping
                BeaconMode.SupplyDrop => (int)(sampleRate * 0.07), // 70ms warble trill
                BeaconMode.Drill => (int)(sampleRate * 0.12),      // 120ms drill pulse
                BeaconMode.Cocoon => (int)(sampleRate * 0.15),     // 150ms organic throb
                BeaconMode.TNT => (int)(sampleRate * 0.04),          // 40ms sharp tick
                BeaconMode.OmmoranCrystal => (int)(sampleRate * 0.08), // 80ms crystal shimmer
                _ => (int)(sampleRate * 0.10)
            };
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
                    else if (mode == BeaconMode.SupplyDrop)
                        sample = GenerateSupplyDropSample(progress);
                    else if (mode == BeaconMode.Cocoon)
                        sample = GenerateCocoonSample(progress);
                    else if (mode == BeaconMode.TNT)
                        sample = GenerateTNTSample(progress);
                    else if (mode == BeaconMode.OmmoranCrystal)
                        sample = GenerateOmmoranCrystalSample(progress);
                    else
                        sample = GenerateDrillSample(progress);

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

        /// <summary>
        /// Drill rumble: rhythmic chug with tremolo modulation.
        /// Triangle + pulse wave mix with sub-octave, clearly mechanical.
        /// Tremolo rate changes based on drill running/stopped state.
        /// </summary>
        private float GenerateDrillSample(float progress)
        {
            // Quick attack, moderate decay
            float envelope = progress < 0.03f
                ? progress / 0.03f
                : (float)Math.Exp(-(progress - 0.03) * 4.0);

            // Amplitude tremolo: chugging rhythm (3 Hz idle, 6 Hz running)
            float tremoloRate = isDrillRunning ? 6.0f : 3.0f;
            float tremolo = 0.6f + 0.4f * (float)Math.Sin(2.0 * Math.PI * tremoloRate * progress);

            // Triangle wave for smooth drill body
            double triPhase = (phase * 4.0) % 4.0;
            double triangle = triPhase < 2.0 ? triPhase - 1.0 : 3.0 - triPhase;

            // Pulse wave for mechanical character (25% duty cycle)
            double pulse = phase % 1.0 < 0.25 ? 0.5 : -0.3;

            // Sub-octave rumble
            double sub = Math.Sin(2.0 * Math.PI * phase2) * 0.25;

            double sample = triangle * 0.5 + pulse * 0.25 + sub;

            phase += currentFrequency / sampleRate;
            phase2 += (currentFrequency * 0.5) / sampleRate;
            if (phase >= 1.0) phase -= 1.0;
            if (phase2 >= 1.0) phase2 -= 1.0;

            return envelope * tremolo * (float)sample;
        }

        /// <summary>
        /// Cocoon organic pulse: heartbeat-like throb with detuned overtones.
        /// Soft attack, slow decay with amplitude pulsing for organic feel.
        /// Clearly distinct from metallic (drop pod), buzzy (supply), and mechanical (drill) sounds.
        /// </summary>
        private float GenerateCocoonSample(float progress)
        {
            // Soft attack, slow organic decay
            float envelope = progress < 0.08f
                ? progress / 0.08f
                : (float)Math.Exp(-(progress - 0.08) * 3.0);

            // Heartbeat-like amplitude pulse at 8 Hz
            float pulse = 0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * 8.0 * progress);
            pulse = pulse * pulse; // Sharpen the pulse shape

            // Main sine tone
            double s1 = Math.Sin(2.0 * Math.PI * phase);

            // Slightly detuned second oscillator for organic texture
            double detuneRatio = 1.015; // ~25 cents sharp, wider than drop pod
            double s2 = Math.Sin(2.0 * Math.PI * phase2) * 0.4;

            // Third harmonic for eerie overtone
            double s3 = Math.Sin(2.0 * Math.PI * phase * 3.0) * 0.1 * (1.0 - progress);

            phase += currentFrequency / sampleRate;
            phase2 += (currentFrequency * detuneRatio) / sampleRate;
            if (phase >= 1.0) phase -= 1.0;
            if (phase2 >= 1.0) phase2 -= 1.0;

            return envelope * pulse * (float)((s1 + s2 + s3) * 0.55);
        }

        /// <summary>
        /// TNT staccato tick: very short, sharp attack with fast decay.
        /// Narrow pulse wave + high harmonic for metallic clicking quality.
        /// Sounds like a countdown timer — completely distinct from other modes.
        /// </summary>
        private float GenerateTNTSample(float progress)
        {
            // Very sharp attack (2%), fast exponential decay
            float envelope = progress < 0.02f
                ? progress / 0.02f
                : (float)Math.Exp(-(progress - 0.02) * 12.0);

            // Sharp narrow pulse wave for clicking quality (15% duty cycle)
            double pulse = phase % 1.0 < 0.15 ? 1.0 : -0.5;

            // High harmonic for metallic "tick" character
            double high = Math.Sin(2.0 * Math.PI * phase * 4.0) * 0.3;

            phase += currentFrequency / sampleRate;
            if (phase >= 1.0) phase -= 1.0;

            return envelope * (float)((pulse * 0.5 + high) * 0.8);
        }

        /// <summary>
        /// Ommoran Crystal shimmer: FM synthesis for glassy quality.
        /// Medium attack, moderate decay with high overtone sparkle.
        /// Ethereal and crystalline — distinct from metallic/organic sounds.
        /// </summary>
        private float GenerateOmmoranCrystalSample(float progress)
        {
            // Medium attack, moderate decay with sparkle fade
            float envelope = progress < 0.05f
                ? progress / 0.05f
                : (float)Math.Exp(-(progress - 0.05) * 5.0);

            // FM synthesis: modulator creates glassy sidebands
            double modulator = Math.Sin(2.0 * Math.PI * phase2 * 3.0) * 0.3;
            double carrier = Math.Sin(2.0 * Math.PI * phase * (1.0 + modulator));

            // High shimmer overtone that fades with progress
            double shimmer = Math.Sin(2.0 * Math.PI * phase * 5.0) * 0.15
                           * (1.0 - progress);

            phase += currentFrequency / sampleRate;
            phase2 += (currentFrequency * 1.5) / sampleRate;
            if (phase >= 1.0) phase -= 1.0;
            if (phase2 >= 1.0) phase2 -= 1.0;

            return envelope * (float)((carrier + shimmer) * 0.6);
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
        public bool IsBeaconActive => isBeaconActive;

        // Player references
        private Transform playerTransform;
        private Transform cameraTransform;
        private float nextPlayerSearchTime = 0f;

        // State
        private bool isInitialized = false;
        private float maxDistance = 150f;
        private const float CRITICAL_DISTANCE = 8f;
        private const float RAMP_DISTANCE = 5f;
        private const float INTERIOR_CLOSE_DISTANCE = 1.5f;
        private bool announcedCriticalProximity = false;
        private bool announcedRampProximity = false;
        private bool announcedInteriorClose = false;

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

        /// <summary>
        /// True when the player is within ramp distance of the extraction pod.
        /// Used by FootstepAudio to switch to metal footstep sounds.
        /// </summary>
        public bool IsPlayerNearPod
        {
            get
            {
                if (activePod == null || playerTransform == null) return false;
                try
                {
                    var ramp = activePod.rampDetector;
                    if (ramp != null)
                        return Vector3.Distance(playerTransform.position, ramp.position) < RAMP_DISTANCE;
                }
                catch { }
                return false;
            }
        }

        public void OnPodLanded(DropPod pod)
        {
            activePod = pod;
            isBeaconActive = true;
            announcedCriticalProximity = false;
            announcedRampProximity = false;
            announcedInteriorClose = false;
            NavMeshPathHelper.Reset();
            Plugin.Log.LogInfo("[DropPodAudio] Beacon activated - extraction pod landed!");
        }

        public void OnPlayerEntered()
        {
            isBeaconActive = false;
            beepGenerator.Active = false;
            rampToneGenerator.Volume = 0f;

            ScreenReader.Interrupt(ModLocalization.Get("pod_inside"));
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

                if (isUp && isRight) return ModLocalization.Get("dir_up_right");
                if (isUp && isLeft) return ModLocalization.Get("dir_up_left");
                if (isDown && isRight) return ModLocalization.Get("dir_down_right");
                if (isDown && isLeft) return ModLocalization.Get("dir_down_left");
                if (isUp) return ModLocalization.Get("dir_up");
                if (isDown) return ModLocalization.Get("dir_down");
                if (isRight) return ModLocalization.Get("dir_right");
                if (isLeft) return ModLocalization.Get("dir_left");
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
                bool isInRampZone = directDistance < RAMP_DISTANCE;

                // Compute distance to playerPoint (the actual departure trigger)
                Vector3 interiorPos = GetPodInteriorPosition();
                float interiorDistance = Vector3.Distance(playerTransform.position, interiorPos);

                // Interior direction info (panning and facing toward playerPoint)
                Vector3 toInterior = interiorPos - playerTransform.position;
                toInterior.y = 0;
                toInterior.Normalize();
                Vector3 camForward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                camForward.y = 0;
                camForward.Normalize();
                Vector3 camRight = new Vector3(camForward.z, 0, -camForward.x);
                float interiorPan = Mathf.Clamp(Vector3.Dot(toInterior, camRight), -1f, 1f);
                float interiorFacingDot = Vector3.Dot(camForward, toInterior);

                // Interior-specific pitch/volume modulation
                float interiorVertical = (interiorFacingDot + 1f) / 2f;
                float interiorPitchMul = 0.4f + interiorVertical * 0.6f;
                float interiorVolMul = 0.7f + interiorVertical * 0.3f;

                beepPanProvider.Pan = pan;

                // Ramp zone: continuous tone guides player toward playerPoint
                if (isInRampZone)
                {
                    // Use distance to playerPoint, scaled by the larger of ramp/interior distance for correct mapping
                    float rampScale = Mathf.Max(RAMP_DISTANCE, interiorDistance + 1f);
                    float interiorFactor = 1f - Mathf.Clamp01(interiorDistance / rampScale);
                    bool isVeryClose = interiorDistance < INTERIOR_CLOSE_DISTANCE;

                    if (isVeryClose)
                    {
                        // Very close to playerPoint: peak intensity
                        float closeFactor = 1f - Mathf.Clamp01(interiorDistance / INTERIOR_CLOSE_DISTANCE);
                        rampToneGenerator.Frequency = (1800f + closeFactor * 400f) * interiorPitchMul;
                        rampToneGenerator.Volume = (0.40f + closeFactor * 0.15f) * interiorVolMul * ModConfig.GetVolume(ModConfig.DROP_POD_BEACON);
                    }
                    else
                    {
                        // Approaching playerPoint: frequency and volume ramp up
                        rampToneGenerator.Frequency = (900f + interiorFactor * 900f) * interiorPitchMul;
                        rampToneGenerator.Volume = (0.12f + interiorFactor * 0.30f) * interiorVolMul * ModConfig.GetVolume(ModConfig.DROP_POD_BEACON);
                    }
                    rampToneVolumeProvider.Volume = 1.0f;

                    // Pan ramp tone toward playerPoint
                    rampTonePanProvider.Pan = interiorPan;

                    // Steer beacon beeps toward playerPoint too
                    beepPanProvider.Pan = interiorPan;

                    if (isVeryClose && !announcedInteriorClose)
                    {
                        announcedInteriorClose = true;
                        ScreenReader.Interrupt(ModLocalization.Get("pod_almost_inside"));
                    }

                    if (!announcedRampProximity)
                    {
                        announcedRampProximity = true;
                        ScreenReader.Interrupt(ModLocalization.Get("pod_near_ramp"));
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

                    if (isInRampZone)
                    {
                        // In ramp zone: beeps also target playerPoint with interior-based modulation
                        float intScale = Mathf.Max(RAMP_DISTANCE, interiorDistance + 1f);
                        float intFactor = 1f - Mathf.Clamp01(interiorDistance / intScale);
                        beepGenerator.Frequency = (1200 + intFactor * 600) * interiorPitchMul;
                        beepGenerator.Volume = (0.35f + intFactor * 0.20f) * interiorVolMul * ModConfig.GetVolume(ModConfig.DROP_POD_BEACON);
                        beepGenerator.Interval = Mathf.Lerp(0.10f, 0.04f, intFactor);
                    }
                    else
                    {
                        beepGenerator.Frequency = (1200 + criticalFactor * 400) * pitchMultiplier;
                        beepGenerator.Volume = (0.4f + criticalFactor * 0.15f) * facingVolumeMultiplier * ModConfig.GetVolume(ModConfig.DROP_POD_BEACON);
                        beepGenerator.Interval = Mathf.Lerp(0.12f, 0.06f, criticalFactor);
                    }
                    beepGenerator.DoubleBeep = true;
                    beepGenerator.Active = true;

                    if (!announcedCriticalProximity)
                    {
                        announcedCriticalProximity = true;
                        ScreenReader.Interrupt(ModLocalization.Get("pod_very_close"));
                    }
                }
                else
                {
                    // Normal beacon chirps - use path distance for accurate "how far to walk"
                    float proximityFactor = 1f - Mathf.Clamp01(pathDistance / maxDistance);
                    proximityFactor = proximityFactor * proximityFactor;

                    float interval = Mathf.Lerp(0.25f, 0.03f, proximityFactor);

                    beepGenerator.Frequency = (800 + proximityFactor * 600) * pitchMultiplier;
                    beepGenerator.Volume = (0.25f + proximityFactor * 0.2f) * facingVolumeMultiplier * ModConfig.GetVolume(ModConfig.DROP_POD_BEACON);
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
                if (!InputHelper.Compass()) return;
                if (playerTransform == null) return;

                Vector3 podPos = GetBeaconTargetPosition();
                if (podPos == Vector3.zero) return;

                float directDistance = Vector3.Distance(playerTransform.position, podPos);

                // When in the ramp zone, report distance to playerPoint for precision
                if (directDistance < RAMP_DISTANCE)
                {
                    Vector3 interiorPos = GetPodInteriorPosition();
                    float interiorDist = Vector3.Distance(playerTransform.position, interiorPos);

                    // Direction toward playerPoint
                    Vector3 toInterior = interiorPos - playerTransform.position;
                    toInterior.y = 0;
                    toInterior.Normalize();
                    Vector3 screenUp = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                    screenUp.y = 0;
                    screenUp.Normalize();
                    Vector3 screenRight = new Vector3(screenUp.z, 0, -screenUp.x);
                    float upDot = Vector3.Dot(toInterior, screenUp);
                    float rightDot = Vector3.Dot(toInterior, screenRight);

                    bool isUp = upDot > 0.38f;
                    bool isDown = upDot < -0.38f;
                    bool isRight = rightDot > 0.38f;
                    bool isLeft = rightDot < -0.38f;

                    string dir = "here";
                    if (isUp && isRight) dir = ModLocalization.Get("dir_up_right");
                    else if (isUp && isLeft) dir = ModLocalization.Get("dir_up_left");
                    else if (isDown && isRight) dir = ModLocalization.Get("dir_down_right");
                    else if (isDown && isLeft) dir = ModLocalization.Get("dir_down_left");
                    else if (isUp) dir = ModLocalization.Get("dir_up");
                    else if (isDown) dir = ModLocalization.Get("dir_down");
                    else if (isRight) dir = ModLocalization.Get("dir_right");
                    else if (isLeft) dir = ModLocalization.Get("dir_left");

                    string distText = interiorDist < 1f
                        ? $"{interiorDist:0.#} meters"
                        : $"{Mathf.RoundToInt(interiorDist)} meters";

                    ScreenReader.Interrupt(ModLocalization.Get("pod_compass_ramp", dir, distText));
                }
                else
                {
                    // Use path distance if available, otherwise direct distance
                    float distance = currentPathResult.IsValid
                        ? currentPathResult.TotalPathDistance
                        : directDistance;

                    string direction = GetScreenDirection();
                    int meters = Mathf.RoundToInt(distance);

                    ScreenReader.Interrupt(ModLocalization.Get("pod_compass_normal", direction, meters));
                }
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
                    announcedInteriorClose = false;
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
