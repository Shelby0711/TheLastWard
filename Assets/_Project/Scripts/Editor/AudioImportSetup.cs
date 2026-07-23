#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LastWard.EditorTools
{
    /// <summary>
    /// Sets sane import settings on every clip in Resources/SFX.
    ///
    /// This matters more than it looks. Everything in a Resources folder is loaded when the game
    /// starts, and Unity's default for audio is <b>Decompress On Load</b> — so a 115-second MP3
    /// becomes roughly 20MB of raw PCM in RAM. Across 28 clips that is hundreds of megabytes
    /// allocated up front, on the main thread, which is what stalled session creation and dragged
    /// the whole machine down.
    ///
    /// The rule applied here:
    /// <list type="bullet">
    /// <item><b>Long clips (&gt;8s)</b> — ambience beds, breathing loops, the running loops — are
    /// <i>streamed</i> from disk. They play continuously, so streaming costs almost nothing and
    /// saves all of that memory.</item>
    /// <item><b>Short clips</b> stay <i>compressed in memory</i> and decode on play: small enough to
    /// hold, and they need to fire instantly.</item>
    /// </list>
    /// Also forces mono — every one of these is either 2D or positional, and neither benefits from
    /// a stereo source. That halves the data again.
    /// </summary>
    public static class AudioImportSetup
    {
        private const string Folder = "Assets/_Project/Resources/SFX";
        private const float StreamAboveSeconds = 8f;

        [MenuItem("The Last Ward/Fix Audio Import Settings")]
        public static void Apply()
        {
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { Folder });
            int streamed = 0, inMemory = 0;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not AudioImporter importer) continue;

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                bool longClip = clip != null && clip.length > StreamAboveSeconds;

                var settings = importer.defaultSampleSettings;
                settings.loadType = longClip ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.55f;
                // Per-platform setting now; the old AudioImporter.preloadAudioData is obsolete.
                settings.preloadAudioData = false;

                importer.defaultSampleSettings = settings;
                importer.forceToMono = true;
                importer.loadInBackground = true;

                importer.SaveAndReimport();
                if (longClip) streamed++; else inMemory++;
            }

            Debug.Log($"[Audio] Import settings applied: {streamed} streamed, {inMemory} compressed in memory. " +
                      "Long clips no longer decompress into RAM at startup.");
        }
    }
}
#endif
