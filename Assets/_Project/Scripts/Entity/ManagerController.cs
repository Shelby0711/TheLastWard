using System.Collections;
using System.Collections.Generic;
using LastWard.Audio;
using LastWard.Core;
using LastWard.Net;
using LastWard.UI;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Entity
{
    /// <summary>What the Manager is doing. There is no Chase — it never runs at you.</summary>
    public enum ManagerState { Roam, Perch, Slip, Manifest, Stunned }

    /// <summary>
    /// The Manager — the first floor's stalker, and the inversion of the ground floor.
    ///
    /// The Receptionist is blind: it hears you and your torch is meaningless. The Manager <b>sees</b>.
    /// Light is what damns you here — a beam held on it fills the meter faster than anything else in
    /// the game — and noise barely registers. Everything you learned about being quiet and lighting
    /// your way is now backwards.
    ///
    /// It is <b>always moving</b>. It walks the corridor at a distance, climbs into the black ceiling
    /// corners to watch, and strafes sideways into doorways when you look at it. It never approaches
    /// and never chases: when your meter fills it simply <i>is</i> in front of you, lifts you, and
    /// that is the end.
    /// </summary>
    public class ManagerController : NetworkBehaviour
    {
        [Header("Territory")]
        [SerializeField] private float firstFloorMinY = 2.5f;
        [SerializeField] private float perceptionRange = 34f;
        [Tooltip("Corridor extent it patrols, and the floor height it walks on.")]
        [SerializeField] private float roamMinZ = 61f;
        [SerializeField] private float roamMaxZ = 111f;
        [SerializeField] private float floorY = 3.2f;

        [Header("Light — its sense")]
        [Tooltip("Meter per second simply for having the torch ON anywhere on its floor. It does not " +
            "need to see you - light on its floor is the thing it is drawn to, and this is what makes " +
            "the torch a resource you spend rather than something you leave running.")]
        [SerializeField] private float torchOnFill = 0.16f;
        [Tooltip("Additional meter per second with the beam held ON it. The fastest death here.")]
        [SerializeField] private float litFill = 0.55f;
        [SerializeField, Range(5f, 90f)] private float beamHalfAngle = 26f;
        [Tooltip("Meter per second while merely looked at. Being seen still costs, just far less.")]
        [SerializeField] private float gazeFill = 0.10f;
        [Tooltip("Noise multiplier. Lower than the Receptionist's — it is watching, not listening — " +
            "but no longer negligible: moving carelessly still gives you away, just more slowly than " +
            "a torch does.")]
        [SerializeField] private float noiseSensitivity = 0.45f;
        [Tooltip("A step or two is free; past this many seconds of continuous movement you are heard.")]
        [SerializeField] private float sustainedMoveGrace = 2f;
        [Tooltip("Meter per second while walking continuously past that grace.")]
        [SerializeField] private float walkFill = 0.12f;
        [Tooltip("Meter per second while running. Crossing its floor at a sprint is a decision.")]
        [SerializeField] private float runFill = 0.30f;
        [Tooltip("Speed above which you count as running rather than walking, in m/s.")]
        [SerializeField] private float runSpeed = 3.4f;
        [Tooltip("Meter bled off per second while you are still, silent and unlit. Being careful has " +
            "to actively buy safety back, or there is no way to recover from a bad moment.")]
        [SerializeField] private float calmDrain = 0.07f;
        [Tooltip("Extra drain while holding your breath. Stillness plus silence is the strongest " +
            "thing you can do against it.")]
        [SerializeField] private float breathHoldDrain = 0.22f;
        [Tooltip("Drain while properly hidden in a spot with the torch off.")]
        [SerializeField] private float hiddenDrain = 0.20f;

        [Header("Endgame")]
        [Tooltip("Where the floor starts getting worse. Everything it senses is multiplied on a " +
            "ramp from here to endgameToZ rather than flipping at a line, so pushing north feels like " +
            "steadily running out of room instead of crossing a tripwire.")]
        [SerializeField] private float endgameFromZ = 72f;
        [Tooltip("The far end of the wing, where it is at full strength.")]
        [SerializeField] private float endgameToZ = 110f;
        [Tooltip("Sense multiplier at the far end. The night book is meant to be frightening to reach.")]
        [SerializeField] private float endgameMultiplier = 3.2f;
        [Tooltip("At full aggression it prowls and climbs this much more often.")]
        [SerializeField] private float endgameTempo = 0.45f;
        [Tooltip("Logs the meter while your torch is on it, so a stalled meter can be told apart from " +
            "stalled senses at a glance.")]
        [SerializeField] private bool debugMeter;

        [Header("Movement")]
        [SerializeField] private float roamSpeed = 1.2f;
        [SerializeField] private float slipSpeed = 2.4f;
        [Tooltip("It will not come closer than this while roaming. Seen down the hall, never approached.")]
        [SerializeField] private float keepAway = 10f;
        [Tooltip("Looked at from inside this range and it slips sideways out of sight.")]
        [SerializeField] private float slipTriggerRange = 22f;
        [SerializeField] private float ceilingY = 5.6f;

        [Header("Kill")]
        [SerializeField] private float manifestDistance = 1.1f;
        [SerializeField] private float liftSeconds = 4.5f;
        [Tooltip("Stare it down this long on a full meter and it takes you regardless.")]
        [SerializeField] private float patience = 1.2f;
        [Tooltip("Seconds of immunity after entering its perception range. Arriving on a floor " +
            "already at a full meter must be survivable — long enough to kill the torch and stop " +
            "moving, short enough that it is not a free crossing.")]
        [SerializeField] private float arrivalGrace = 3f;
        [Tooltip("Full meter and already this close? No staging — it is in reach, it simply takes you.")]
        [SerializeField] private float reachDistance = 4.5f;
        [SerializeField] private float stunSeconds = 5f;

        private readonly NetworkVariable<ManagerState> netState =
            new NetworkVariable<ManagerState>(ManagerState.Roam);
        private readonly NetworkVariable<bool> hidden = new NetworkVariable<bool>(false);

        private static readonly int TIdle = Animator.StringToHash("Idle");
        private float busySince;
        [Tooltip("Seconds after which a stuck busy flag is force-cleared. Longer than the longest " +
            "routine (perch tops out around 20s) and short enough that a wedged Manager recovers " +
            "inside one encounter.")]
        [SerializeField] private float busyTimeout = 30f;

        private static readonly int TWalk = Animator.StringToHash("Walk");
        private static readonly int TCrawl = Animator.StringToHash("Crawl");
        private static readonly int TCrawlBack = Animator.StringToHash("CrawlBack");
        private static readonly int TStrafeL = Animator.StringToHash("StrafeL");
        private static readonly int TStrafeR = Animator.StringToHash("StrafeR");
        // Three of them, rolled per kill, so repeated deaths never play out identically.
        private static readonly int[] TKills =
        {
            Animator.StringToHash("Kill1"),
            Animator.StringToHash("Kill2"),
            Animator.StringToHash("Kill3"),
        };
        private static readonly int TImpact = Animator.StringToHash("Impact");

        private readonly List<Transform> players = new List<Transform>();
        private Renderer[] renderers;
        private Animator animator;
        private Unity.Netcode.Components.NetworkTransform netTransform;
        private AudioSource whisper, movement, scramble;

        private bool busy;
        // Separate from `busy` on purpose. Sensing runs every frame now (a stalker that only watches
        // while idle is not a stalker), which means the kill trigger can be reached while a kill is
        // ALREADY running — and StopAllCoroutines would then abort the first Manifest partway, leaving
        // the victim held, alive and frozen forever. This flag makes the kill strictly non-reentrant
        // while still letting a full meter interrupt perching or slipping.
        private bool killing;
        private float stunnedUntil, fullSince = -1f;
        // How long each player has been within this Manager's reach, and how long their meter has
        // been full while it could actually sense them. Both are per-player: `fullSince` alone was
        // one shared field, so a meter that filled on the first floor left it set, and returning to
        // the floor later satisfied "full for longer than `patience`" on the very first frame back.
        private readonly Dictionary<Transform, float> inRangeSince = new Dictionary<Transform, float>();
        private readonly Dictionary<Transform, float> fullSincePlayer = new Dictionary<Transform, float>();
        private Vector3 roamTarget;
        private readonly Dictionary<Transform, float> moveTime = new Dictionary<Transform, float>();
        private readonly Dictionary<Transform, Vector3> lastPos = new Dictionary<Transform, Vector3>();
        private float nextRoamPick, nextPerch, lastAnim;

        public ManagerState State => netState.Value;

        public override void OnNetworkSpawn()
        {
            animator = GetComponentInChildren<Animator>();
            netTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();

            // The permanent twitch is the Manager's alone, so the two entities read as different
            // kinds of thing before either does anything.
            var driver = GetComponentInChildren<EntityAnimationDriver>();
            if (driver != null) driver.SetForceStutter(true);

            // Three spatial layers at different reaches, so distance alone tells you what it is
            // doing: the whisper carries furthest and is always there, the movement loop only reaches
            // you when it is genuinely near, and the scramble is the panic of it repositioning fast.
            whisper = MakeLoop(GameSfx.ManagerWhisper, 0.55f, 26f);
            movement = MakeLoop(GameSfx.ManagerMovement, 0f, 14f);
            scramble = MakeLoop(GameSfx.ManagerScramble, 0f, 22f);

            hidden.OnValueChanged += OnHiddenChanged;
            ApplyHidden(hidden.Value);
            if (IsServer)
            {
                GameEvents.OnNoiseEmitted += OnNoise;
                roamTarget = PickRoamPoint();
                nextPerch = Time.time + Random.Range(8f, 16f);
            }
        }

        public override void OnNetworkDespawn()
        {
            hidden.OnValueChanged -= OnHiddenChanged;
            if (IsServer) GameEvents.OnNoiseEmitted -= OnNoise;
        }

        private AudioSource MakeLoop(AudioClip clip, float volume, float range)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = volume;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2f;
            src.maxDistance = range;
            src.playOnAwake = false;
            if (clip != null) src.Play();
            return src;
        }

        private void OnHiddenChanged(bool _, bool now) => ApplyHidden(now);

        private void ApplyHidden(bool isHidden)
        {
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
                if (r != null) r.enabled = !isHidden;
            // Whispering follows the body. A voice with nothing behind it is a bug, not a scare.
            if (whisper != null) whisper.volume = isHidden ? 0.18f : 0.55f;
        }

        public void ServerStun(float seconds)
        {
            if (!IsServer) return;
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + Mathf.Max(seconds, stunSeconds));
            // A thrown weapon mid-lift releases the victim rather than freezing them in its grip.
            if (killing)
            {
                StopAllCoroutines();
                foreach (var p in players)
                    if (p != null && p.TryGetComponent<PlayerNetworkState>(out var vic) && vic.IsHeld)
                        vic.ServerSetHeld(false);
                killing = false;
                busy = false;
            }
            netState.Value = ManagerState.Stunned;
            Play(TImpact);
        }

        private void OnNoise(Vector3 position, float radius, NoiseSource source)
        {
            if (!IsServer || busy || position.y < firstFloorMinY) return;
            foreach (var p in players)
            {
                if (p == null || Vector3.Distance(p.position, position) > 2.5f) continue;
                if (!p.TryGetComponent<PlayerNetworkState>(out var pns) || !pns.IsAlive) continue;
                if (pns.IsHoldingBreath) continue;      // whatever that was, it was not you
                float mult = Aggression(p.position.z) * LastWard.Core.PartyScale.Danger;
                pns.ServerSetDiscovery(pns.Discovery + 0.2f * noiseSensitivity * mult);
            }
        }

        private void Update()
        {
            // Walking and scrambling are different sounds on purpose. A slow drag somewhere in the
            // dark and a sudden scurry across the ceiling should not be the same cue, because they
            // mean different things about how much time you have.
            var st = netState.Value;
            if (movement != null)
                movement.volume = Mathf.MoveTowards(movement.volume,
                    st == ManagerState.Roam ? 0.5f : 0f, Time.deltaTime * 2f);
            if (scramble != null)
                scramble.volume = Mathf.MoveTowards(scramble.volume,
                    st == ManagerState.Slip || st == ManagerState.Perch ? 0.8f : 0f, Time.deltaTime * 3.5f);

            if (!IsServer) return;
            RefreshPlayers();

            // Sensing runs ALWAYS. It used to sit behind the `busy` guard, so while it was perching
            // or slipping — which is most of the time — nobody's meter moved at all and it could
            // never reach the threshold that kills. Being busy should stop it ACTING, not stop it
            // watching; a thing that only sees you while idle is not a stalker.
            // WATCHDOG. busy is set before a coroutine and cleared at its end -- but StopAllCoroutines
            // in the kill path destroys perch/slip mid-flight, so the clearing line never runs and the
            // flag stays true forever. From then on `acting` is false and the Manager never perches,
            // never slips, never roams: it just stands in the corridor. This is the fourth separate
            // instance of that same bug, so it is now caught by class rather than by case.
            if (busy && Time.time - busySince > busyTimeout)
            {
                Debug.LogWarning($"[Manager] busy stuck for {Time.time - busySince:0.0}s - forcing clear. " +
                                 "A coroutine was interrupted before it could release it.");
                busy = false;
                killing = false;
                netState.Value = ManagerState.Roam;
            }
            bool acting = !busy && Time.time >= stunnedUntil;

            Transform watcher = null;
            float watcherDist = float.MaxValue;
            bool anyUpstairs = false;

            foreach (var p in players)
            {
                if (p == null) continue;
                if (!p.TryGetComponent<PlayerNetworkState>(out var pns) || !pns.IsAlive) continue;
                if (p.position.y < firstFloorMinY) continue;
                anyUpstairs = true;

                float dist = Vector3.Distance(p.position, transform.position);
                if (dist > perceptionRange)
                {
                    // Out of range is not "paused" — it is not being hunted. Forget the player
                    // entirely so re-entering starts a fresh acquisition; PlayerNetworkState's
                    // unclaimed decay brings their meter down in the meantime.
                    inRangeSince.Remove(p);
                    fullSincePlayer.Remove(p);
                    lastPos.Remove(p);
                    continue;
                }
                if (!inRangeSince.ContainsKey(p)) inRangeSince[p] = Time.time;

                bool lit = false, gazed = false;
                var pivot = pns.CameraPivot;
                // Only counts as being seen if it is actually VISIBLE. Testing line of sight alone
                // meant that once it hid, the player still "saw" it, so it re-hid forever and never
                // moved again — the bug that left it frozen at the end of the hall.
                if (!hidden.Value && pivot != null && HasLineTo(pivot.position))
                {
                    Vector3 to = (transform.position + Vector3.up * 1.4f) - pivot.position;
                    float angle = Vector3.Angle(pivot.forward, to);
                    gazed = angle <= beamHalfAngle * 1.6f;
                    lit = pns.FlashlightOn && angle <= beamHalfAngle;
                }

                // Nearer the second-floor stairs it is guarding the way up, and everything it
                // senses counts for more — the Manager's version of the corridor hardening.
                // Party scaling multiplies the ramp rather than being applied separately, so a
                // solo player gets the same 30% reprieve everywhere on the floor instead of only
                // where the endgame ramp happens to be shallow.
                float aggression = Aggression(p.position.z) * LastWard.Core.PartyScale.Danger;

                // The meter tracks WHAT YOU DO, not what it happens to be able to see. Gating all of
                // this behind line of sight was the mistake: it hides constantly, so the meter tracked
                // its own state instead of your behaviour and collapsed the moment it slipped away.
                // Light and noise on its floor are what draw it, whether or not it is looking.
                float dt = Mathf.Max(0.0001f, Time.deltaTime);
                Vector3 prev = lastPos.TryGetValue(p, out var lp) ? lp : p.position;
                float moved = Vector3.Distance(prev, p.position);
                lastPos[p] = p.position;

                float speed = moved / dt;
                bool moving = speed > 0.4f;
                bool running = speed > runSpeed;
                bool silent = pns.IsHoldingBreath;
                bool concealed = pns.IsHidden;

                float held = moveTime.TryGetValue(p, out var mt) ? mt : 0f;
                held = moving ? held + dt : held - dt * 2.5f;
                held = Mathf.Clamp(held, 0f, sustainedMoveGrace * 3f);
                moveTime[p] = held;

                float delta = 0f;

                // --- LIGHT: its sense. Carrying a lit torch across its floor costs you even when it
                // cannot see you; pointing it straight at the thing costs far more.
                if (pns.FlashlightOn)
                {
                    delta += torchOnFill;
                    if (lit) delta += litFill;
                }
                if (gazed && !lit) delta += gazeFill;

                // Candlelight. It is not a beacon you carry, so it never charges torchOnFill — but
                // standing in the pool is being lit, and that has to cost the same as sweeping a torch
                // or "safe to be near, fatal to stand in" is only a sentence in a design document.
                // Scaled by how deep into the pool you are, so the edge of the light is a real place
                // to stand rather than a cliff.
                float inCandle = LastWard.Core.WorldLight.LitAmount(p.position);
                if (inCandle > 0f && !concealed) delta += litFill * inCandle;

                // --- SOUND: weaker than light, but no longer negligible. Running is a real decision.
                if (!silent && !concealed && moving && held > sustainedMoveGrace)
                    delta += (running ? runFill : walkFill) * (pns.IsCrouching ? 0.45f : 1f);

                // --- RELIEF: torch off and still. Hiding and holding your breath stack on top, which
                // is what makes the breath timer worth spending rather than a novelty.
                if (!pns.FlashlightOn && !moving)
                    delta -= concealed ? hiddenDrain : calmDrain;
                if (silent) delta -= breathHoldDrain;

                if (!Mathf.Approximately(delta, 0f))
                    pns.ServerSetDiscovery(pns.Discovery + delta * aggression * Time.deltaTime);

                // Twice now the first-floor meter has been silently cancelled out by something else
                // writing it (the Receptionist decaying upstairs players, then the SanctuaryZone
                // covering the whole floor). Both times it looked identical from the outside: the
                // Manager stalking perfectly and never killing. This makes that failure visible
                // instead of invisible - if the meter is not moving while lit, it is being fought.
                if (debugMeter)
                    Debug.Log($"[Manager] torch={(pns.FlashlightOn ? 1 : 0)} lit={(lit ? 1 : 0)} " +
                        $"spd={speed:0.0} hold={(silent ? 1 : 0)} delta={delta * aggression:+0.000;-0.000}/s " +
                        $"meter={pns.Discovery:0.00}");

                if (pns.Discovery >= 0.999f)
                {
                    if (!fullSincePlayer.ContainsKey(p)) fullSincePlayer[p] = Time.time;
                    fullSince = fullSincePlayer[p];
                    bool inReach = dist <= reachDistance;

                    // Walking into its range already at full must never be an instant kill. That is
                    // the shape of every "I died to nothing the moment I reached the floor" report:
                    // the meter was pinned from somewhere else, and the first frame in range was
                    // also the last. The grace is short enough that it is not a free pass — turn
                    // the torch off and stand still and the meter is falling before it expires.
                    if (Time.time - inRangeSince[p] < arrivalGrace) continue;
                    // Three ways in. Requiring "unseen" alone was a deadlock: looking is what fills
                    // the meter, so staring at it made the kill impossible.
                    if (!killing && (!gazed || inReach || Time.time - fullSince >= patience))
                    {
                        // The kill outranks whatever it was doing. Perching and slipping are stopped
                        // so a full meter is never left waiting on a ceiling routine to finish - but
                        // never a kill already in progress, hence the `killing` guard above.
                        StopAllCoroutines();
                        killing = true;
                        busy = true; busySince = Time.time;
                        StartCoroutine(Manifest(p, pns, inReach || gazed));
                        return;
                    }
                }
                else fullSincePlayer.Remove(p);

                if (gazed && dist < watcherDist) { watcher = p; watcherDist = dist; }
            }

            if (!anyUpstairs)
            {
                fullSince = -1f;
                inRangeSince.Clear();
                fullSincePlayer.Clear();
                return;
            }
            if (!acting) return;                 // busy or stunned: it watched, but it does not move

            // Caught looking: slip sideways out of the corridor rather than standing there.
            if (watcher != null && watcherDist < slipTriggerRange && netState.Value != ManagerState.Slip)
            {
                busy = true; busySince = Time.time;
                StartCoroutine(SlipAside(watcher));
                return;
            }

            // Otherwise it is ALWAYS doing something. Roaming is the default, not a fallback.
            if (Time.time >= nextPerch && !hidden.Value)
            {
                busy = true; busySince = Time.time;
                StartCoroutine(PerchInCorner());
                return;
            }
            TickRoam();
        }

        /// <summary>Walks the corridor, keeping its distance. Plain lerp: the run is straight.</summary>
        private void TickRoam()
        {
            if (hidden.Value) hidden.Value = false;
            if (netState.Value != ManagerState.Roam) netState.Value = ManagerState.Roam;

            if (Time.time >= nextRoamPick || Vector3.Distance(transform.position, roamTarget) < 0.7f)
            {
                roamTarget = PickRoamPoint();
                nextRoamPick = Time.time + Random.Range(7f, 15f);
            }

            Vector3 dir = roamTarget - transform.position;
            dir.y = 0f;
            // Backs off rather than closing — lurking, not approaching.
            foreach (var p in players)
                if (p != null && p.position.y >= firstFloorMinY &&
                    Vector3.Distance(p.position, transform.position) < keepAway)
                { dir = transform.position - p.position; dir.y = 0f; break; }
            if (dir.sqrMagnitude < 0.01f) return;

            // Prowls faster the deeper they have gone.
            Vector3 step = dir.normalized * roamSpeed * (1f + endgameTempo * Pressure()) * Time.deltaTime;
            Vector3 next = transform.position + step;
            next.z = Mathf.Clamp(next.z, roamMinZ, roamMaxZ);
            next.x = Mathf.Clamp(next.x, -1.1f, 1.1f);
            next.y = floorY;
            transform.position = next;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir.normalized), Time.deltaTime * 3f);
            Play(TWalk, 1.4f);
        }

        /// <summary>
        /// 1 at the stairs, rising smoothly to endgameMultiplier at the top of the wing. The deeper
        /// they push, the less forgiving every torch flick and every footstep becomes.
        /// </summary>
        private float Aggression(float z) =>
            Mathf.Lerp(1f, endgameMultiplier,
                Mathf.Clamp01(Mathf.InverseLerp(endgameFromZ, endgameToZ, z)));

        /// <summary>How deep the furthest living player upstairs has pushed, as 0..1.</summary>
        private float Pressure()
        {
            float peak = 1f;
            foreach (var t in players)
                if (t != null && t.position.y >= firstFloorMinY)
                    peak = Mathf.Max(peak, Aggression(t.position.z));
            return Mathf.Clamp01((peak - 1f) / Mathf.Max(0.01f, endgameMultiplier - 1f));
        }

        private Vector3 PickRoamPoint() =>
            new Vector3(Random.Range(-1.1f, 1.1f), floorY, Random.Range(roamMinZ, roamMaxZ));

        /// <summary>Strafes sideways into a doorway and is gone — the "it went into that room" beat.</summary>
        private IEnumerator SlipAside(Transform watcher)
        {
            netState.Value = ManagerState.Slip;
            bool left = Random.value < 0.5f;
            Play(left ? TStrafeL : TStrafeR);

            Vector3 start = transform.position;
            Vector3 side = (left ? -transform.right : transform.right);
            Vector3 target = start + side * 3.2f;

            float t = 0f;
            while (t < 3.2f / slipSpeed)
            {
                t += Time.deltaTime;
                transform.position = Vector3.Lerp(start, target, t * slipSpeed / 3.2f);
                yield return null;
            }

            hidden.Value = true;                       // into the room, out of sight
            yield return new WaitForSeconds(Random.Range(4f, 9f));

            // Reappears somewhere else entirely, which is the point of vanishing at all.
            Vector3 spot = PickRoamPoint();
            transform.position = spot;
            netTransform.SafeTeleport(this, spot, transform.rotation, transform.localScale);
            hidden.Value = false;
            // Deliberately NOT rescheduling the perch here. Slipping happens every time a player
            // looks at it, and each slip used to push the ceiling perch another 14-26s away — so on
            // a floor where you are constantly looking around, it never climbed at all.
            busy = false;
        }

        /// <summary>Climbs into a black ceiling corner and watches, motionless, until it drops back.</summary>
        private IEnumerator PerchInCorner()
        {
            netState.Value = ManagerState.Perch;
            Play(TCrawl);

            Vector3 start = transform.position;
            Vector3 corner = new Vector3(Random.value < 0.5f ? -1.15f : 1.15f, ceilingY,
                Mathf.Clamp(start.z + Random.Range(-6f, 6f), roamMinZ, roamMaxZ));

            float t = 0f;
            while (t < 1.6f)
            {
                t += Time.deltaTime;
                transform.position = Vector3.Lerp(start, corner, t / 1.6f);
                yield return null;
            }

            Play(TIdle);
            yield return new WaitForSeconds(Random.Range(6f, 12f));

            Play(TCrawlBack);
            Vector3 down = new Vector3(corner.x * 0.6f, floorY, corner.z);
            t = 0f;
            while (t < 1.4f)
            {
                t += Time.deltaTime;
                transform.position = Vector3.Lerp(corner, down, t / 1.4f);
                yield return null;
            }

            // Deep in the wing it is back on the ceiling far sooner, so there are fewer stretches of
            // hallway where you can be confident it is not above you.
            nextPerch = Time.time + Random.Range(12f, 22f) * Mathf.Lerp(1f, 0.45f, Pressure());
            busy = false;
        }

        private IEnumerator Manifest(Transform victim, PlayerNetworkState pns, bool alreadyThere)
        {
            netState.Value = ManagerState.Manifest;
            fullSince = -1f;
            hidden.Value = false;

            Vector3 fwd = victim.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            if (!alreadyThere)
            {
                Vector3 spot = victim.position + fwd.normalized * manifestDistance;
                spot.y = victim.position.y;
                transform.position = spot;
                netTransform.SafeTeleport(this, spot, transform.rotation, transform.localScale);
            }
            Vector3 face = victim.position - transform.position; face.y = 0f;
            if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(face.normalized);

            pns.ServerSetHeld(true);
            var netObj = victim.GetComponent<NetworkObject>();
            CatchClientRpc(netObj != null ? netObj.OwnerClientId : 0UL);
            Play(TKills[Random.Range(0, TKills.Length)], 0f);

            yield return new WaitForSeconds(liftSeconds);

            if (pns.IsAlive)
            {
                pns.ServerSetHeld(false);
                pns.ServerSetDiscovery(0f);
                pns.ServerKill();
            }

            Vector3 away = PickRoamPoint();
            transform.position = away;
            netTransform.SafeTeleport(this, away, transform.rotation, transform.localScale);
            netState.Value = ManagerState.Roam;
            killing = false;
            busy = false;
        }

        private void Play(int trigger, float minGap = 0.35f)
        {
            // Nothing interrupts a kill. Idle/Walk/Crawl kept arriving mid-death-scene and each one
            // re-entered the state machine through its AnyState transition, which is why the killing
            // animation restarted several times over one death.
            if (killing && System.Array.IndexOf(TKills, trigger) < 0) return;
            if (Time.time - lastAnim < minGap) return;
            lastAnim = Time.time;
            PlayClientRpc(trigger);
        }

        [ClientRpc]
        private void PlayClientRpc(int triggerHash)
        {
            if (animator != null) animator.SetTrigger(triggerHash);
        }

        [ClientRpc]
        private void CatchClientRpc(ulong victimClientId)
        {
            if (NetworkManager.Singleton == null) return;
            if (NetworkManager.Singleton.LocalClientId != victimClientId) return;
            JumpscareUI.Instance?.Play(false);
            GameSfx.Play2D(GameSfx.Random(GameSfx.Jumpscares), 1f);

            var po = NetworkManager.Singleton.LocalClient.PlayerObject;
            var look = po != null ? po.GetComponentInChildren<LastWard.Player.FirstPersonLook>() : null;
            if (look != null) look.BeginCatch(transform);
        }

        private bool HasLineTo(Vector3 eye)
        {
            Vector3 mid = transform.position + Vector3.up * 1.4f;
            return !(Physics.Linecast(eye, mid, out var hit, ~0, QueryTriggerInteraction.Ignore)
                     && !hit.transform.IsChildOf(transform));
        }

        private void RefreshPlayers()
        {
            players.Clear();
            if (NetworkManager.Singleton == null) return;
            foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
                if (c.PlayerObject != null) players.Add(c.PlayerObject.transform);
        }
    }
}
