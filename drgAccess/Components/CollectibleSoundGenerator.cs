using System;
using NAudio.Wave;

namespace drgAccess.Components
{
    public enum CollectibleSoundType
    {
        RedSugar,       // Water-drop "bloop" (rapid descending frequency sweep)
        GearDrop,       // Two-tone chord (root + fifth)
        BuffPickup,     // Electric buzz (FM synthesis, ascending)
        CurrencyPickup, // Crystalline chime (sine + octave harmonic)
        MineralVein,    // Metallic clink (pickaxe on rock)
        LootCrate,      // Shimmering rapid alternating frequencies
        XpNearby,       // Continuous soft tone, pitch = distance to nearest XP
        BobbyFuel,      // Bubbling/gurgle for escort mission fuel blocks
        HealingZone,    // Water-drop bloop (same as RedSugar) for healing pillars
        LaunchPad,      // 8-bit "boing boing" (square wave, double pitch drop) for launch ramps
        ObjectiveRes,   // Two-note ascending bell for resources an objective asks for
        Lootbug         // Short chirping squeak for lootbugs (they run away)
    }

    /// <summary>
    /// Audio generator for collectible items and mineral veins.
    /// Beacon-style repeating beeps for pickups/crates, continuous tone for mineral veins.
    /// Each CollectibleSoundType produces a distinct waveform.
    /// </summary>
    public class CollectibleSoundGenerator : ISampleProvider
    {
        private readonly WaveFormat waveFormat;
        private readonly int sampleRate;
        private double phase;
        private double phase2; // Second oscillator for chords/shimmer

        // Parameters (set from main thread)
        private volatile float targetFrequency = 500f;
        private volatile float targetVolume = 0f;
        private volatile int targetIntervalSamples;
        private volatile bool active = false;
        private volatile int soundTypeInt = 0;

        // Internal state (audio thread only)
        private int sampleCounter = 0;
        private int beepDurationSamples;
        private int intervalSamples;
        private float currentFrequency = 500f;
        private float currentVolume = 0f;

        // Continuous mode smoothing (for MineralVein)
        private float smoothFrequency = 500f;
        private float smoothVolume = 0f;

        public WaveFormat WaveFormat => waveFormat;

        public float Frequency
        {
            set => targetFrequency = Math.Max(50f, Math.Min(5000f, value));
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

        public CollectibleSoundType SoundType
        {
            set => soundTypeInt = (int)value;
        }

        public CollectibleSoundGenerator(int sampleRate = 44100)
        {
            this.sampleRate = sampleRate;
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            beepDurationSamples = (int)(sampleRate * 0.08); // 80ms beep default
            intervalSamples = (int)(sampleRate * 0.5);
            targetIntervalSamples = intervalSamples;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            currentFrequency = targetFrequency;
            currentVolume = targetVolume;
            intervalSamples = targetIntervalSamples;
            var type = (CollectibleSoundType)soundTypeInt;

            // Adjust beep duration per type
            int duration = type switch
            {
                CollectibleSoundType.RedSugar => (int)(sampleRate * 0.15),
                CollectibleSoundType.GearDrop => (int)(sampleRate * 0.15),
                CollectibleSoundType.BuffPickup => (int)(sampleRate * 0.08),
                CollectibleSoundType.CurrencyPickup => (int)(sampleRate * 0.07),
                CollectibleSoundType.LootCrate => (int)(sampleRate * 0.12),
                CollectibleSoundType.MineralVein => (int)(sampleRate * 0.10),
                CollectibleSoundType.BobbyFuel => (int)(sampleRate * 0.18),
                CollectibleSoundType.HealingZone => (int)(sampleRate * 0.15),
                CollectibleSoundType.LaunchPad => (int)(sampleRate * 0.30), // two "boings"
                CollectibleSoundType.ObjectiveRes => (int)(sampleRate * 0.22), // two bell notes
                CollectibleSoundType.Lootbug => (int)(sampleRate * 0.09),
                _ => (int)(sampleRate * 0.08)
            };
            beepDurationSamples = duration;

            for (int i = 0; i < count; i++)
            {
                if (!active || currentVolume < 0.001f)
                {
                    buffer[offset + i] = 0;
                    // Smooth volume down for continuous types
                    smoothVolume *= 0.999f;
                    continue;
                }

                float sample;
                if (type == CollectibleSoundType.XpNearby)
                    sample = GenerateContinuous(type);
                else
                    sample = GenerateBeacon(type);

                buffer[offset + i] = sample;
            }
            return count;
        }

        private float GenerateContinuous(CollectibleSoundType type)
        {
            // Smooth transitions for continuous tone (XP nearby only)
            smoothFrequency += (currentFrequency - smoothFrequency) * 0.0001f;
            smoothVolume += (currentVolume - smoothVolume) * 0.0002f;

            // Triangle wave + 6 Hz tremolo to distinguish from wall sine tones
            double triPhase = (phase * 2.0) % 2.0;
            double triangle = triPhase < 1.0 ? (triPhase * 2.0 - 1.0) : (1.0 - (triPhase - 1.0) * 2.0);
            double tremolo = 0.7 + 0.3 * Math.Sin(2.0 * Math.PI * phase2);

            phase += smoothFrequency / sampleRate;
            phase2 += 6.0 / sampleRate; // 6 Hz tremolo rate
            if (phase >= 1.0) phase -= 1.0;
            if (phase2 >= 1.0) phase2 -= 1.0;

            return (float)(smoothVolume * triangle * tremolo);
        }

        private float GenerateBeacon(CollectibleSoundType type)
        {
            bool isInBeep = sampleCounter < beepDurationSamples;

            float sample = 0f;
            if (isInBeep)
            {
                float progress = (float)sampleCounter / beepDurationSamples;
                float envelope = ComputeEnvelope(type, progress);
                sample = currentVolume * envelope * ComputeWaveform(type, progress);
            }
            else
            {
                phase = 0;
                phase2 = 0;
            }

            sampleCounter++;
            if (sampleCounter >= intervalSamples)
                sampleCounter = 0;

            return sample;
        }

        private float ComputeEnvelope(CollectibleSoundType type, float progress)
        {
            return type switch
            {
                // Quick attack, smooth decay (water drop)
                CollectibleSoundType.RedSugar => progress < 0.03f
                    ? progress / 0.03f
                    : (float)Math.Exp(-(progress - 0.03) * 2.5),

                // Quick attack, sustained then decay (rich chord)
                CollectibleSoundType.GearDrop => progress < 0.03f
                    ? progress / 0.03f
                    : (float)Math.Exp(-(progress - 0.03) * 2.5),

                // Sharp attack, medium decay (electric buzz)
                CollectibleSoundType.BuffPickup => progress < 0.02f
                    ? progress / 0.02f
                    : (float)Math.Exp(-(progress - 0.02) * 5.0),

                // Sharp attack, medium decay (chime)
                CollectibleSoundType.CurrencyPickup => progress < 0.03f
                    ? progress / 0.03f
                    : (float)Math.Exp(-(progress - 0.03) * 5.0),

                // Moderate attack, sustained (shimmer)
                CollectibleSoundType.LootCrate => progress < 0.05f
                    ? progress / 0.05f
                    : (float)Math.Exp(-(progress - 0.05) * 2.0),

                // Sharp attack, moderate decay (metallic clink)
                CollectibleSoundType.MineralVein => progress < 0.02f
                    ? progress / 0.02f
                    : (float)Math.Exp(-(progress - 0.02) * 5.0),

                // Moderate attack, bubbly decay (gurgle)
                CollectibleSoundType.BobbyFuel => progress < 0.04f
                    ? progress / 0.04f
                    : (float)Math.Exp(-(progress - 0.04) * 2.0),

                // Same as RedSugar (water-drop bloop)
                CollectibleSoundType.HealingZone => progress < 0.03f
                    ? progress / 0.03f
                    : (float)Math.Exp(-(progress - 0.03) * 2.5),

                // Two bounces: each half of the beep is its own attack + decay
                CollectibleSoundType.LaunchPad => ComputeBoingEnvelope(progress),

                // Two bell notes: each half of the beep is struck and rings out
                CollectibleSoundType.ObjectiveRes => ComputeTwoNoteEnvelope(progress),

                // Very sharp attack, fast decay (squeak)
                CollectibleSoundType.Lootbug => progress < 0.02f
                    ? progress / 0.02f
                    : (float)Math.Exp(-(progress - 0.02) * 6.0),

                _ => 1f
            };
        }

        private float ComputeWaveform(CollectibleSoundType type, float progress)
        {
            switch (type)
            {
                case CollectibleSoundType.RedSugar:
                {
                    // Water-drop "bloop": rapid descending frequency sweep
                    // Starts at 2.5x base frequency, drops to 0.4x — distinct from any beep
                    double sweepMult = 2.5 - progress * 2.1; // 2.5 → 0.4 over duration
                    double freq = currentFrequency * sweepMult;
                    double s = Math.Sin(2.0 * Math.PI * phase);
                    // Add subtle 2nd harmonic for rounder "water" quality
                    double s2 = Math.Sin(2.0 * Math.PI * phase2) * 0.25;
                    phase += freq / sampleRate;
                    phase2 += (freq * 2.0) / sampleRate;
                    if (phase >= 1.0) phase -= 1.0;
                    if (phase2 >= 1.0) phase2 -= 1.0;
                    return (float)((s + s2) * 0.8);
                }
                case CollectibleSoundType.GearDrop:
                {
                    // Two-tone: root + perfect fifth (1.5x)
                    double freq1 = currentFrequency;
                    double freq2 = currentFrequency * 1.5;
                    double s1 = Math.Sin(2.0 * Math.PI * phase);
                    double s2 = Math.Sin(2.0 * Math.PI * phase2) * 0.6;
                    phase += freq1 / sampleRate;
                    phase2 += freq2 / sampleRate;
                    if (phase >= 1.0) phase -= 1.0;
                    if (phase2 >= 1.0) phase2 -= 1.0;
                    return (float)((s1 + s2) * 0.6);
                }
                case CollectibleSoundType.BuffPickup:
                {
                    // FM synthesis buzz: carrier + modulator creates electric/buzzy timbre
                    // Completely different texture from sine beeps and water drops
                    double freq = currentFrequency * (1.0 + progress * 0.2);
                    double modFreq = freq * 3.0; // Modulator at 3x carrier
                    double modDepth = 2.0 * (1.0 - progress * 0.5); // Modulation decreases over time
                    double modulator = Math.Sin(2.0 * Math.PI * phase2) * modDepth;
                    double s = Math.Sin(2.0 * Math.PI * phase + modulator);
                    phase += freq / sampleRate;
                    phase2 += modFreq / sampleRate;
                    if (phase >= 1.0) phase -= 1.0;
                    if (phase2 >= 1.0) phase2 -= 1.0;
                    return (float)s;
                }
                case CollectibleSoundType.CurrencyPickup:
                {
                    // Sine + octave harmonic (bell/chime)
                    double freq = currentFrequency * (1.0 - progress * 0.15);
                    double s1 = Math.Sin(2.0 * Math.PI * phase);
                    double s2 = Math.Sin(2.0 * Math.PI * phase2) * 0.4;
                    phase += freq / sampleRate;
                    phase2 += (freq * 2.0) / sampleRate;
                    if (phase >= 1.0) phase -= 1.0;
                    if (phase2 >= 1.0) phase2 -= 1.0;
                    return (float)((s1 + s2) * 0.7);
                }
                case CollectibleSoundType.LootCrate:
                {
                    // Rapid alternating between two frequencies (sparkle/shimmer)
                    double alterRate = 12.0; // 12 Hz alternation
                    double blend = 0.5 + 0.5 * Math.Sin(2.0 * Math.PI * alterRate * progress);
                    double freqA = currentFrequency;
                    double freqB = currentFrequency * 1.25;
                    double freq = freqA + (freqB - freqA) * blend;
                    double s = Math.Sin(2.0 * Math.PI * phase);
                    // Add shimmer with octave
                    double s2 = Math.Sin(2.0 * Math.PI * phase2) * 0.3;
                    phase += freq / sampleRate;
                    phase2 += (freq * 2.0) / sampleRate;
                    if (phase >= 1.0) phase -= 1.0;
                    if (phase2 >= 1.0) phase2 -= 1.0;
                    return (float)((s + s2) * 0.7);
                }
                case CollectibleSoundType.MineralVein:
                {
                    // Metallic clink — sine + inharmonic overtone (2.76x) for pickaxe-on-rock quality
                    double freq = currentFrequency;
                    double s1 = Math.Sin(2.0 * Math.PI * phase);
                    // Inharmonic partial at 2.76x creates metallic/bell character
                    double s2 = Math.Sin(2.0 * Math.PI * phase2) * 0.45;
                    phase += freq / sampleRate;
                    phase2 += (freq * 2.76) / sampleRate;
                    if (phase >= 1.0) phase -= 1.0;
                    if (phase2 >= 1.0) phase2 -= 1.0;
                    return (float)((s1 + s2) * 0.7);
                }
                case CollectibleSoundType.BobbyFuel:
                {
                    // Bubbling/gurgle: low carrier with fast amplitude modulation
                    double freq = currentFrequency * (1.0 - progress * 0.3);
                    double bubble = 0.5 + 0.5 * Math.Sin(2.0 * Math.PI * phase2); // 20 Hz bubble rate
                    double s = Math.Sin(2.0 * Math.PI * phase);
                    // Add sub-octave for fullness
                    double sub = Math.Sin(Math.PI * phase) * 0.3;
                    phase += freq / sampleRate;
                    phase2 += 20.0 / sampleRate;
                    if (phase >= 1.0) phase -= 1.0;
                    if (phase2 >= 1.0) phase2 -= 1.0;
                    return (float)((s + sub) * bubble * 0.8);
                }
                case CollectibleSoundType.HealingZone:
                {
                    // Water-drop bloop (same as RedSugar for familiar healing sound)
                    double sweepMult = 2.5 - progress * 2.1;
                    double freq = currentFrequency * sweepMult;
                    double s = Math.Sin(2.0 * Math.PI * phase);
                    double s2 = Math.Sin(2.0 * Math.PI * phase2) * 0.25;
                    phase += freq / sampleRate;
                    phase2 += (freq * 2.0) / sampleRate;
                    if (phase >= 1.0) phase -= 1.0;
                    if (phase2 >= 1.0) phase2 -= 1.0;
                    return (float)((s + s2) * 0.8);
                }
                case CollectibleSoundType.LaunchPad:
                {
                    // 8-bit "boing boing": square wave, two bounces per beep.
                    // Each bounce sweeps the pitch up fast then springs back down.
                    float bounceProgress = progress < 0.5f ? progress * 2f : (progress - 0.5f) * 2f;
                    // Pitch: quick rise (first 20%) then springy fall with wobble
                    double sweepMult;
                    if (bounceProgress < 0.2f)
                        sweepMult = 1.0 + (bounceProgress / 0.2f) * 1.2; // 1.0 → 2.2
                    else
                    {
                        double fall = (bounceProgress - 0.2) / 0.8;
                        double wobble = 0.15 * Math.Sin(2.0 * Math.PI * 3.0 * fall); // spring wobble
                        sweepMult = 2.2 - fall * 1.5 + wobble; // 2.2 → 0.7 with wobble
                    }
                    double freq = currentFrequency * sweepMult;
                    // Square wave for the chiptune/8-bit character (attenuated — squares are loud)
                    double square = Math.Sin(2.0 * Math.PI * phase) >= 0 ? 1.0 : -1.0;
                    phase += freq / sampleRate;
                    if (phase >= 1.0) phase -= 1.0;
                    return (float)(square * 0.35);
                }
                case CollectibleSoundType.ObjectiveRes:
                {
                    // Two-note ascending bell (root, then a fifth above) with a bright
                    // 3rd harmonic — reads as "this is what the mission wants", clearly
                    // apart from the metallic clink of an ordinary mineral vein.
                    double freq = progress < 0.5f ? currentFrequency : currentFrequency * 1.5;
                    double s1 = Math.Sin(2.0 * Math.PI * phase);
                    double s2 = Math.Sin(2.0 * Math.PI * phase2) * 0.35;
                    phase += freq / sampleRate;
                    phase2 += (freq * 3.0) / sampleRate;
                    if (phase >= 1.0) phase -= 1.0;
                    if (phase2 >= 1.0) phase2 -= 1.0;
                    return (float)((s1 + s2) * 0.75);
                }
                case CollectibleSoundType.Lootbug:
                {
                    // Chirpy squeak: fast upward glide with vibrato — animal, not mineral
                    double sweepMult = 1.0 + progress * 0.6; // glides up over the beep
                    double vibrato = 1.0 + 0.08 * Math.Sin(2.0 * Math.PI * phase2);
                    double freq = currentFrequency * sweepMult * vibrato;
                    // Triangle wave: softer/rounder than a square, still "voiced"
                    double triPhase = (phase * 2.0) % 2.0;
                    double triangle = triPhase < 1.0 ? (triPhase * 2.0 - 1.0) : (1.0 - (triPhase - 1.0) * 2.0);
                    phase += freq / sampleRate;
                    phase2 += 35.0 / sampleRate; // 35 Hz vibrato
                    if (phase >= 1.0) phase -= 1.0;
                    if (phase2 >= 1.0) phase2 -= 1.0;
                    return (float)(triangle * 0.7);
                }
                default:
                    return 0f;
            }
        }

        /// <summary>Envelope for the two-note bell: each note is struck and rings out.</summary>
        private static float ComputeTwoNoteEnvelope(float progress)
        {
            float noteProgress = progress < 0.5f ? progress * 2f : (progress - 0.5f) * 2f;
            return noteProgress < 0.03f
                ? noteProgress / 0.03f
                : (float)Math.Exp(-(noteProgress - 0.03) * 3.0);
        }

        /// <summary>Envelope for the double "boing": two attack+decay humps per beep.</summary>
        private static float ComputeBoingEnvelope(float progress)
        {
            float bounceProgress = progress < 0.5f ? progress * 2f : (progress - 0.5f) * 2f;
            // Second bounce slightly quieter, like a real spring settling
            float bounceScale = progress < 0.5f ? 1f : 0.75f;
            float env = bounceProgress < 0.05f
                ? bounceProgress / 0.05f
                : (float)Math.Exp(-(bounceProgress - 0.05) * 3.5);
            return env * bounceScale;
        }
    }
}
