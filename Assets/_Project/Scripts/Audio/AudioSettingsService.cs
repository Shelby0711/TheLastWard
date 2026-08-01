using UnityEngine;

namespace LastWard.Audio
{
    /// <summary>
    /// Master/SFX/music volumes, remembered between sessions.
    ///
    /// There is no AudioMixer in the project and adding one would mean re-routing every AudioSource
    /// that already exists — including the ones created at runtime by PlayClipAtPoint, which cannot
    /// be routed at all. So music is a single tracked source and everything else rides the global
    /// AudioListener. That is a real limitation: <b>SFX volume is effectively master volume</b> for
    /// anything that is not the menu track. Worth replacing with a mixer before ship, but not worth
    /// blocking a menu on.
    /// </summary>
    public static class AudioSettingsService
    {
        private const string KeyMaster = "lw_vol_master";
        private const string KeySfx = "lw_vol_sfx";
        private const string KeyMusic = "lw_vol_music";

        private static AudioSource music;
        private static bool loaded;

        public static float Master { get; private set; } = 1f;
        public static float Sfx { get; private set; } = 1f;
        public static float Music { get; private set; } = 0.6f;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            Master = PlayerPrefs.GetFloat(KeyMaster, 1f);
            Sfx = PlayerPrefs.GetFloat(KeySfx, 1f);
            Music = PlayerPrefs.GetFloat(KeyMusic, 0.6f);
            Apply();
        }

        /// <summary>The menu track, so music can be balanced separately from everything else.</summary>
        public static void RegisterMusic(AudioSource src)
        {
            music = src;
            Load();
            Apply();
        }

        public static void SetMaster(float v) { Master = Mathf.Clamp01(v); Save(KeyMaster, Master); }
        public static void SetSfx(float v) { Sfx = Mathf.Clamp01(v); Save(KeySfx, Sfx); }
        public static void SetMusic(float v) { Music = Mathf.Clamp01(v); Save(KeyMusic, Music); }

        private static void Save(string key, float v)
        {
            PlayerPrefs.SetFloat(key, v);
            PlayerPrefs.Save();
            Apply();
        }

        private static void Apply()
        {
            // Listener volume carries master AND sfx, because sfx has nowhere else to live without a
            // mixer. Music then divides that back out so its own slider stays independent of both.
            AudioListener.volume = Master * Sfx;
            if (music != null)
            {
                float carrier = Mathf.Max(0.0001f, Master * Sfx);
                music.volume = Mathf.Clamp01(Master * Music / carrier) * 0.9f;
            }
        }
    }
}
