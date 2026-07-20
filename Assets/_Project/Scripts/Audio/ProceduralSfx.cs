using UnityEngine;

namespace LastWard.Audio
{
    /// <summary>
    /// Generates placeholder SFX in code so the prototype has audible feedback without shipping any
    /// audio files yet. These are rough synth stand-ins — the M8 audio pass swaps in real CC0 clips
    /// (Freesound) and the AudioMixer snapshot system. Clips are built once at runtime by whichever
    /// component needs them.
    /// </summary>
    public static class ProceduralSfx
    {
        private const int Rate = 44100;

        private static AudioClip Make(string name, float[] samples)
        {
            // Fade the first/last few ms to zero to avoid clicks on loop points.
            int fade = Mathf.Min(256, samples.Length / 8);
            for (int i = 0; i < fade; i++)
            {
                float g = i / (float)fade;
                samples[i] *= g;
                samples[samples.Length - 1 - i] *= g;
            }
            var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static AudioClip Footstep()
        {
            int n = (int)(Rate * 0.18f);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * 32f);
                float tone = Mathf.Sin(2f * Mathf.PI * 105f * t);
                float noise = Random.value * 2f - 1f;
                s[i] = (tone * 0.55f + noise * 0.45f) * env * 0.7f;
            }
            return Make("sfx_footstep", s);
        }

        public static AudioClip DoorCreak()
        {
            int n = (int)(Rate * 0.9f);
            var s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float freq = Mathf.Lerp(360f, 180f, t / 0.9f) + Mathf.Sin(t * 40f) * 25f;
                phase += 2f * Mathf.PI * freq / Rate;
                float env = Mathf.Sin(Mathf.PI * (t / 0.9f)); // attack+decay bump
                float noise = (Random.value * 2f - 1f) * 0.15f;
                s[i] = (Mathf.Sin(phase) * 0.35f + noise) * env * 0.6f;
            }
            return Make("sfx_door", s);
        }

        public static AudioClip EntityAmbient()
        {
            int n = (int)(Rate * 3f);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float lfo = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.15f * t);
                float drone = Mathf.Sin(2f * Mathf.PI * 55f * t) + 0.7f * Mathf.Sin(2f * Mathf.PI * 58f * t);
                float wisp = (Random.value * 2f - 1f) * 0.05f;
                s[i] = (drone * 0.18f + wisp) * lfo;
            }
            return Make("sfx_entity_ambient", s);
        }

        public static AudioClip Heartbeat()
        {
            int n = (int)(Rate * 1.1f);
            var s = new float[n];
            AddThump(s, 0.0f, 0.12f);
            AddThump(s, 0.17f, 0.10f);
            return Make("sfx_heartbeat", s);
        }

        private static void AddThump(float[] s, float startSec, float durSec)
        {
            int start = (int)(Rate * startSec);
            int len = (int)(Rate * durSec);
            for (int i = 0; i < len && start + i < s.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * 45f);
                s[start + i] += Mathf.Sin(2f * Mathf.PI * 48f * t) * env * 0.8f;
            }
        }

        public static AudioClip DeathSting()
        {
            int n = (int)(Rate * 1.3f);
            var s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float freq = Mathf.Lerp(300f, 42f, t / 1.3f);
                phase += 2f * Mathf.PI * freq / Rate;
                float env = Mathf.Exp(-t * 2.2f);
                float noise = (Random.value * 2f - 1f) * 0.2f * Mathf.Exp(-t * 6f);
                s[i] = (Mathf.Sin(phase) * 0.5f + noise) * env;
            }
            return Make("sfx_death", s);
        }

        public static AudioClip FarewellSting()
        {
            // A slow, sad four-note descending phrase — the placeholder "farewell song" stand-in
            // until a real composed cue lands in M8.
            float[] notes = { 440f, 392f, 349.23f, 293.66f };
            float noteDur = 1f;
            int n = (int)(Rate * noteDur * notes.Length);
            var s = new float[n];
            int perNote = (int)(Rate * noteDur);
            for (int noteIdx = 0; noteIdx < notes.Length; noteIdx++)
            {
                int start = noteIdx * perNote;
                float freq = notes[noteIdx];
                for (int i = 0; i < perNote && start + i < n; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Exp(-t * 2.2f);
                    s[start + i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.35f;
                }
            }
            return Make("sfx_farewell", s);
        }

        public static AudioClip PingChime()
        {
            int n = (int)(Rate * 0.45f);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * 6f);
                s[i] = (Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.3f + Mathf.Sin(2f * Mathf.PI * 1320f * t) * 0.15f) * env;
            }
            return Make("sfx_ping", s);
        }

        public static AudioClip Whisper()
        {
            int n = (int)(Rate * 2.5f);
            var s = new float[n];
            float smooth = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                // Low-passed noise (running average) modulated by a breathy envelope = whisper-ish.
                float noise = Random.value * 2f - 1f;
                smooth = Mathf.Lerp(smooth, noise, 0.15f);
                float breath = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 0.6f * t)), 2f);
                s[i] = smooth * breath * 0.4f;
            }
            return Make("sfx_whisper", s);
        }
    }
}
