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
        [SerializeField] private float roamMaxZ = 86f;
        [SerializeField] private float floorY = 3.2f;

        [Header("Light — its sense")]
        [Tooltip("Meter per second with a torch beam held on it. The fastest death on this floor.")]
        [SerializeField] private float litFill = 0.30f;
        [SerializeField, Range(5f, 90f)] private float beamHalfAngle = 26f;
        [Tooltip("Meter per second while merely looked at. Being seen still costs, just far less.")]
        [SerializeField] private float gazeFill = 0.05f;
        [Tooltip("Noise multiplier. Low on purpose: it is watching, not listening.")]
        [SerializeField] private float noiseSensitivity = 0.25f;

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
        [SerializeField] private float patience = 2.5f;
        [Tooltip("Full meter and already this close? No staging — it is in reach, it simply takes you.")]
        [SerializeField] private float reachDistance = 3.5f;
        [SerializeField] private float stunSeconds = 5f;

        private readonly NetworkVariable<ManagerState> netState =
            new NetworkVariable<ManagerState>(ManagerState.Roam);
        private readonly NetworkVariable<bool> hidden = new NetworkVariable<bool>(false);

        private static readonly int TIdle = Animator.StringToHash("Idle");
        private static readonly int TWalk = Animator.StringToHash("Walk");
        private static readonly int TCrawl = Animator.StringToHash("Crawl");
        private static readonly int TCrawlBack = Animator.StringToHash("CrawlBack");
        private static readonly int TStrafeL = Animator.StringToHash("StrafeL");
        private static readonly int TStrafeR = Animator.StringToHash("StrafeR");
        private static readonly int TLift = Animator.StringToHash("Lift");
        private static readonly int TImpact = Animator.StringToHash("Impact");

        private readonly List<Transform> players = new List<Transform>();
        private Renderer[] renderers;
        private Animator animator;
        private Unity.Netcode.Components.NetworkTransform netTransform;
        private AudioSource whisper, movement;

        private bool busy;
        private float stunnedUntil, fullSince = -1f;
        private Vector3 roamTarget;
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

            whisper = MakeLoop(GameSfx.ManagerWhisper, 0.55f, 26f);
            movement = MakeLoop(GameSfx.ManagerMovement, 0f, 16f);

            hidden.OnValueChanged += OnHiddenChanged;
            ApplyHidden(hidden.Value);
            if (IsServer)
            {
                GameEvents.OnNoiseEmitted += OnNoise;
                roamTarget = PickRoamPoint();
                nextPerch = Time.time + Random.Range(14f, 26f);
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
            netState.Value = ManagerState.Stunned;
            Play(TImpact);
        }

        private void OnNoise(Vector3 position, float radius, NoiseSource source)
        {
            if (!IsServer || busy || position.y < firstFloorMinY) return;
            foreach (var p in players)
            {
                if (p == null || Vector3.Distance(p.position, position) > 2.5f) continue;
                if (p.TryGetComponent<PlayerNetworkState>(out var pns) && pns.IsAlive)
                    pns.ServerSetDiscovery(pns.Discovery + 0.2f * noiseSensitivity);
            }
        }

        private void Update()
        {
            if (movement != null)
                movement.volume = Mathf.MoveTowards(movement.volume,
                    netState.Value == ManagerState.Roam || netState.Value == ManagerState.Slip ? 0.5f : 0f,
                    Time.deltaTime * 2f);

            if (!IsServer || busy || Time.time < stunnedUntil) return;
            RefreshPlayers();

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
                if (dist > perceptionRange) continue;

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

                if (lit) pns.ServerSetDiscovery(pns.Discovery + litFill * Time.deltaTime);
                else if (gazed) pns.ServerSetDiscovery(pns.Discovery + gazeFill * Time.deltaTime);

                if (pns.Discovery >= 0.999f)
                {
                    if (fullSince < 0f) fullSince = Time.time;
                    bool inReach = dist <= reachDistance;
                    // Three ways in. Requiring "unseen" alone was a deadlock: looking is what fills
                    // the meter, so staring at it made the kill impossible.
                    if (!gazed || inReach || Time.time - fullSince >= patience)
                    {
                        busy = true;
                        StartCoroutine(Manifest(p, pns, inReach || gazed));
                        return;
                    }
                }

                if (gazed && dist < watcherDist) { watcher = p; watcherDist = dist; }
            }

            if (!anyUpstairs) { fullSince = -1f; return; }

            // Caught looking: slip sideways out of the corridor rather than standing there.
            if (watcher != null && watcherDist < slipTriggerRange && netState.Value != ManagerState.Slip)
            {
                busy = true;
                StartCoroutine(SlipAside(watcher));
                return;
            }

            // Otherwise it is ALWAYS doing something. Roaming is the default, not a fallback.
            if (Time.time >= nextPerch && !hidden.Value)
            {
                busy = true;
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

            Vector3 step = dir.normalized * roamSpeed * Time.deltaTime;
            Vector3 next = transform.position + step;
            next.z = Mathf.Clamp(next.z, roamMinZ, roamMaxZ);
            next.x = Mathf.Clamp(next.x, -1.1f, 1.1f);
            next.y = floorY;
            transform.position = next;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir.normalized), Time.deltaTime * 3f);
            Play(TWalk, 1.4f);
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
            if (netTransform != null) netTransform.Teleport(spot, transform.rotation, transform.localScale);
            hidden.Value = false;
            nextPerch = Time.time + Random.Range(14f, 26f);
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

            nextPerch = Time.time + Random.Range(18f, 34f);
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
                if (netTransform != null) netTransform.Teleport(spot, transform.rotation, transform.localScale);
            }
            Vector3 face = victim.position - transform.position; face.y = 0f;
            if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(face.normalized);

            pns.ServerSetHeld(true);
            var netObj = victim.GetComponent<NetworkObject>();
            CatchClientRpc(netObj != null ? netObj.OwnerClientId : 0UL);
            Play(TLift);

            yield return new WaitForSeconds(liftSeconds);

            if (pns.IsAlive)
            {
                pns.ServerSetHeld(false);
                pns.ServerSetDiscovery(0f);
                pns.ServerKill();
            }

            Vector3 away = PickRoamPoint();
            transform.position = away;
            if (netTransform != null) netTransform.Teleport(away, transform.rotation, transform.localScale);
            netState.Value = ManagerState.Roam;
            busy = false;
        }

        private void Play(int trigger, float minGap = 0.35f)
        {
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
