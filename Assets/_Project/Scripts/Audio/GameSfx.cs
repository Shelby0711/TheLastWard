using System.Collections.Generic;
using UnityEngine;

namespace LastWard.Audio
{
    /// <summary>
    /// Every real sound in the game, resolved by name from <c>Resources/SFX</c>.
    ///
    /// Loaded through Resources rather than serialized references because the scene is built by
    /// script: wiring ~28 clips onto components through SerializedObject would be a large amount of
    /// fragile editor plumbing, and a renamed file would fail silently. Here a missing clip warns
    /// once, names itself, and the game carries on without audio rather than throwing.
    ///
    /// Clips are cached, so the repeated lookups in Update-driven code cost nothing after the first.
    /// </summary>
    public static class GameSfx
    {
        private const string Folder = "SFX/";
        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();
        private static readonly HashSet<string> Warned = new HashSet<string>();

        // --- ambience -------------------------------------------------------------------------
        public static AudioClip Ambience => Get("horror_ambiance");
        public static AudioClip Wind => Get("horror-rumble-winds");
        public static AudioClip Menu => Get("menu");

        /// <summary>Occasional one-shots layered over the ambience bed — see AmbienceDirector.</summary>
        public static AudioClip[] DistantStingers => GetMany(
            "horror_cries_echoing", "creepy-woman-cry", "haunted-ghost-baby-crying",
            "evil-woman-laugh", "someone-laughing", "gothic-butterfly-insectnoises1",
            "bug-scuttling-insectnoises2");

        // --- the Entity -----------------------------------------------------------------------
        public static AudioClip EntityDrone => Get("lurking-horror-monster");
        public static AudioClip EntityLurk => Get("horror-sound-lurking-monster02");
        public static AudioClip EntityStalk => Get("giant_insect_lurking_insectnoises3");
        public static AudioClip EntityFootsteps => Get("giant-walking");
        public static AudioClip EntityStare => Get("deep-evil-male-laugh");
        public static AudioClip Heartbeat => Get("heartbeat-sound");
        public static AudioClip Whisper => Get("creepy-woman-cry");
        /// <summary>The Entity's own breathing - close, wet and constant.</summary>
        public static AudioClip EntityBreathing => Get("entity-breathing");
        /// <summary>Corridor-only: girls muttering somewhere you are not.</summary>
        public static AudioClip GirlsMumbling => Get("girls-mumbling-in-the-corridor");

        // --- The Manager (first floor) ---
        /// <summary>Its constant whisper, heard long before it is seen.</summary>
        /// <summary>Starts once the players begin taking the corridor apart. See ChantingDirector.</summary>
        public static AudioClip DemonChanting => Get("demon_chanting");
        /// <summary>Dragging yourself through the duct.</summary>
        public static AudioClip VentCrawl => Get("vent-crawling");
        /// <summary>Dropping out of the duct onto the surviving flight.</summary>
        public static AudioClip Landing => Get("landing");
        /// <summary>Forcing a lock the wrong way. Loud, and the building answers it.</summary>
        public static AudioClip[] WrongAttempt => GetMany("loud-object-bang", "door-bang-echo");
        public static AudioClip ManagerWhisper => Get("Entity_Whispering");
        /// <summary>Its movement across surfaces - dry, wrong, not footsteps.</summary>
        public static AudioClip ManagerMovement => Get("Entity_movement_SFX");
        /// <summary>The scramble when it repositions in a hurry.</summary>
        public static AudioClip ManagerScramble => Get("Entity_Running_Around");

        /// <summary>Death stingers. Primary_Jumpscare is the loudest/signature hit; the numbered
        /// ones give variety so repeated deaths never land the same way.</summary>
        public static AudioClip[] Jumpscares =>
            GetMany("Primary_Jumpscare", "jumpscare01", "jumpscare02", "jumpscare03");

        /// <summary>Fired repeatedly during a chase — the building screaming along with it.</summary>
        public static AudioClip[] ChaseCries => GetMany(
            "creepy-woman-cry", "evil-woman-laugh", "deep-evil-male-laugh",
            "horror_cries_echoing", "someone-laughing");

        // --- player ---------------------------------------------------------------------------
        // Idle and walking share the same calm loop at different volumes — one person's breathing
        // does not change character just because they started walking.
        public static AudioClip BreathingIdle => Get("normal-breathing");
        public static AudioClip BreathingWalk => Get("normal-male-breathing");
        /// <summary>Winded or frightened. Only after sustained running, or after a scare.</summary>
        public static AudioClip BreathingHeavy => Get("scared-heavy-breathing");
        public static AudioClip BreathingRunFast => Get("breathing-faster-running");
        public static AudioClip BreathingWalkFast => Get("breathing-faster-walking");
        public static AudioClip FootstepsInterior => Get("running-interior");
        public static AudioClip FootstepsGravel => Get("running-on-gravel");

        // --- the asylum (second floor) ---
        /// <summary>A cell gate hauled open on rusted runners.</summary>
        public static AudioClip GateOpen => Get("gate_opening");
        /// <summary>The strike. Plays for the three seconds you are knelt over it, win or lose.</summary>
        public static AudioClip MatchStrike => Get("matchstick_lighting");
        /// <summary>The first floor's barred hallway gate sliding aside.</summary>
        public static AudioClip HallwayGate => Get("hallway_jail_door_Opening");

        // --- events ---------------------------------------------------------------------------
        public static AudioClip DoorOpen => Get("opening-door");
        public static AudioClip DoorClose => Get("door_closing_normal");
        public static AudioClip DoorSlam => Get("door-slamming-shut");
        public static AudioClip SwitchFlip => Get("switch-flip");
        public static AudioClip ObjectFalling => Get("loud-object-falling-noise");
        public static AudioClip Scream => Get("loud-scream");
        public static AudioClip TakenAway => Get("cry-of-pain-taken");

        public static AudioClip Get(string name)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;

            var clip = Resources.Load<AudioClip>(Folder + name);
            if (clip == null && Warned.Add(name))
                Debug.LogWarning($"[SFX] Clip '{name}' not found under Resources/{Folder}. " +
                                 "That sound will be silent — check the filename matches.");
            Cache[name] = clip;
            return clip;
        }

        private static AudioClip[] GetMany(params string[] names)
        {
            var list = new List<AudioClip>(names.Length);
            foreach (var n in names)
            {
                var c = Get(n);
                if (c != null) list.Add(c);
            }
            return list.ToArray();
        }

        /// <summary>
        /// Fires a clip straight at the listener - 2D, unpositioned, full volume regardless of where
        /// anything is standing. For the death stinger, which must hit like a wall rather than fade
        /// in from somewhere across the room.
        /// </summary>
        public static void Play2D(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;
            var go = new GameObject("SFX_2D_" + clip.name);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            src.spatialBlend = 0f;
            src.Play();
            UnityEngine.Object.Destroy(go, clip.length + 0.2f);
        }

        /// <summary>Random clip from a set, or null if the set is empty.</summary>
        public static AudioClip Random(AudioClip[] clips) =>
            clips == null || clips.Length == 0 ? null : clips[UnityEngine.Random.Range(0, clips.Length)];
    }
}
