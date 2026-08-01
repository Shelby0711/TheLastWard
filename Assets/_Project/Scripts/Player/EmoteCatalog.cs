namespace LastWard.Player
{
    /// <summary>
    /// The emote list, in one place.
    ///
    /// Adding one should be three edits and nothing else: drop the clip into each character's
    /// Animations folder, add a row here, and add its state to the animator in
    /// <c>ArtKit.SetupPlayerAnimator</c>. Nothing in <see cref="EmoteController"/> or
    /// <see cref="PlayerAnimationDriver"/> needs to change — the index is the only contract.
    ///
    /// Keep the file names identical across every character model. A clip that exists for one body and
    /// not another means an emote that silently does nothing depending on which variant a player was
    /// assigned, which is exactly the kind of bug nobody reports because it looks like lag.
    /// </summary>
    public static class EmoteCatalog
    {
        public readonly struct Emote
        {
            public readonly string Key;       // the clip file name, shared by all characters
            public readonly string Display;   // for the controls page
            public readonly float Seconds;    // roughly how long it runs, for the cooldown

            public Emote(string key, string display, float seconds)
            {
                Key = key; Display = display; Seconds = seconds;
            }
        }

        /// <summary>
        /// Placeholder entries. These reference clips that do not exist yet — the animator builder
        /// skips any it cannot find, so the pipeline is live and simply plays nothing until the files
        /// are dropped in. That is deliberate: the plumbing is testable before the art arrives.
        /// </summary>
        public static readonly Emote[] All =
        {
            new Emote("Emote_Wave",  "Wave",         2.0f),
            new Emote("Emote_Point", "Point",        1.6f),
            new Emote("Emote_Panic", "Panic",        2.4f),
        };

        public static int Count => All.Length;
        public static Emote Get(int i) => All[UnityEngine.Mathf.Clamp(i, 0, All.Length - 1)];
    }
}
