using System.Collections;
using System.Collections.Generic;
using LastWard.Audio;
using LastWard.Core;
using LastWard.Knowledge;
using LastWard.Puzzles;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Entity
{
    /// <summary>
    /// The Inspector. It does not hunt the loud or the bright — it hunts whoever has been paying
    /// attention.
    ///
    /// No chase and no stalking: the Manager provides all the stalking texture this floor needs, and
    /// the Inspector provides consequence. What it does instead is <b>mark</b> the player with the
    /// highest knowledge score and start a clock only that player can see. When the clock runs out it
    /// arrives, takes them, and goes.
    ///
    /// The clock is the whole design. It converts a death into a <i>transfer</i>: the dying player's
    /// last act is to dump codes and locations into voice chat, so the group ends up stronger for
    /// having lost their best-informed member. The random 40–120s band matters more than the length,
    /// because not knowing whether you have thirty seconds or two minutes forces you to triage what
    /// you say — you lead with the code, not the story.
    ///
    /// Sharing knowledge does NOT move the mark. Telling someone the code never raised their score,
    /// because they never read the note. It is a sacrifice, not a hot potato — and because the mark
    /// descends to the next-highest when its holder dies, standing still on this floor is queuing.
    /// That queue is the floor's engine; it needs no fear meter of its own.
    /// </summary>
    public class InspectorController : NetworkBehaviour
    {
        public static InspectorController Instance { get; private set; }

        [Header("Where it operates")]
        [Tooltip("Only players above this Y are on its floor.")]
        [SerializeField] private float floorMinY = 5.5f;
        [SerializeField] private float floorMaxY = 20f;

        [Header("The clock")]
        // 15-30s, not 40-120. Two minutes is long enough to say everything you know twice and then
        // stand around waiting, which drains the dread out of the whole mechanic. Under thirty
        // seconds you have to triage - you lead with the code, not the story, which was always the
        // point of the clock.
        [SerializeField] private float minSeconds = 15f;
        [SerializeField] private float maxSeconds = 30f;
        [Tooltip("Grace measured from when a player ARRIVES on this floor, not from scene load.")]
        [SerializeField] private float settleSeconds = 35f;
        [SerializeField] private float retargetInterval = 2f;

        [Header("The kill")]
        [SerializeField] private Transform body;
        [SerializeField] private Animator animator;
        [SerializeField] private float reachDistance = 3.2f;
        [SerializeField] private float killSeconds = 4.2f;
        [Tooltip("Eye height at the top of the lift — roughly the Inspector's own head.")]
        [SerializeField] private float liftHeight = 3.1f;
        [Tooltip("Eye height at the bottom of the slam. Face down on the tiles.")]
        [SerializeField] private float slamHeight = 0.18f;

        // Declared before anything read by another's OnValueChanged - NGO deserialises in order.
        private readonly NetworkVariable<bool> manifest = new NetworkVariable<bool>();

        private ulong marked = ulong.MaxValue;
        private bool hasMark;
        private float deadline;
        private float nextScan;
        // Per player, stamped when they first appear on this floor. Measuring grace from scene load
        // meant it had expired long before anyone finished the climb, so the first thing the asylum
        // did was mark whoever walked in.
        private readonly Dictionary<ulong, float> arrivedAt = new Dictionary<ulong, float>();
        private bool killing;

        [Header("Diagnostics")]
        [Tooltip("Logs every player's knowledge score when nobody can be marked.")]
        [SerializeField] private bool debugScores = true;
        private float nextScoreLog;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            manifest.OnValueChanged += (_, v) => ApplyVisible(v);
            ApplyVisible(manifest.Value);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        private void ApplyVisible(bool on)
        {
            if (body == null) return;
            foreach (var r in body.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            // Kick it onto a real pose the instant it becomes visible. Whatever else is wrong, it
            // should never be SEEN in bind pose — that is the one failure the player reads as
            // "the entity is broken" rather than "the entity is strange".
            if (on && animator != null && animator.runtimeAnimatorController != null)
            {
                animator.Play("Idle", 0, 0f);
                animator.Update(0f);
            }
        }

        private void Update()
        {
            if (!IsServer || killing) return;

            if (hasMark && Time.time >= deadline)
            {
                StartCoroutine(Execute(marked));
                return;
            }

            if (Time.time < nextScan) return;
            nextScan = Time.time + retargetInterval;
            if (!hasMark) TryMark();
        }

        // ---- targeting ----

        private void TryMark()
        {
            var ks = KnowledgeService.Instance;
            if (ks == null || NetworkManager.Singleton == null) return;

            ulong best = ulong.MaxValue;
            float bestScore = -1f;
            foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
            {
                var po = c.PlayerObject;
                if (po == null) continue;
                var pns = po.GetComponent<LastWard.Net.PlayerNetworkState>();
                float y = po.transform.position.y;
                if (y < floorMinY || y > floorMaxY)
                {
                    arrivedAt.Remove(c.ClientId);                       // left the floor: clock resets
                    continue;
                }
                if (!arrivedAt.TryGetValue(c.ClientId, out float since))
                {
                    arrivedAt[c.ClientId] = Time.time;
                    // Wipe the fear meter they carried up the stairs. It is per-player and persists
                    // across floors, so anyone who fought their way off the first floor arrived here
                    // already most of the way to dead, and the asylum killed them for the previous
                    // floor's noise. Each floor starts its own reckoning.
                    if (pns != null) pns.ServerSetDiscovery(0f);
                    continue;                                          // grace starts now
                }
                if (Time.time - since < settleSeconds * LastWard.Core.PartyScale.Grace) continue;
                if (pns != null && !pns.IsAlive) continue;
                // A burned record is not a reprieve from being hunted, it is removal from the list
                // it hunts FROM. Nothing else on this floor can take you off it.
                if (RecordLedger.Instance != null && RecordLedger.Instance.IsOffTheList(c.ClientId)) continue;

                float score = ks.GetScore(c.ClientId);
                if (score > bestScore) { bestScore = score; best = c.ClientId; }
            }

            // The score is invisible to players by design, which also makes it invisible to US when
            // something is wrong with it. This prints the whole table on every scan that finds
            // nobody, so "the knowledge system isn't working" becomes a readable line rather than a
            // guess about a number no UI shows.
            if (best == ulong.MaxValue || bestScore <= 0f)
            {
                if (debugScores && Time.time > nextScoreLog)
                {
                    nextScoreLog = Time.time + 5f;
                    var sb = new System.Text.StringBuilder("[Inspector] nobody markable. scores:");
                    foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
                    {
                        var po2 = c.PlayerObject;
                        float yy = po2 != null ? po2.transform.position.y : -999f;
                        sb.Append($" [{c.ClientId}] score={ks.GetScore(c.ClientId):0.0} y={yy:0.0}");
                    }
                    Debug.Log(sb.ToString());
                }
                return;
            }
            ServerSetMark(best);
        }

        private void ServerSetMark(ulong who)
        {
            marked = who;
            hasMark = true;
            // Solo runs get a longer clock. One player carries every note, every puzzle and every
            // code, so their score is what four people would have split - and being marked for that
            // the moment you arrive is being punished for playing the game at all.
            float span = Random.Range(minSeconds, maxSeconds) * LastWard.Core.PartyScale.Grace;
            deadline = Time.time + span;
            // Only the marked player is told. Nobody else gets any signal at all, which is why
            // saying "I have a timer" out loud is a decision rather than a formality.
            DoomClientRpc(span, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { who } }
            });
            Debug.Log($"[Inspector] Marked client {who} — {span:0}s " +
                      $"({LastWard.Core.PartyScale.Living} alive, danger x{LastWard.Core.PartyScale.Danger:0.00}).");
        }

        [ClientRpc]
        private void DoomClientRpc(float seconds, ClientRpcParams p = default) =>
            LastWard.UI.DoomTimerUI.Instance?.Begin(seconds);

        [ClientRpc]
        private void ClearDoomClientRpc(ClientRpcParams p = default) =>
            LastWard.UI.DoomTimerUI.Instance?.Clear();

        /// <summary>Server-only. Their record burned: drop the mark and look again.</summary>
        public void ServerClearMark(ulong who)
        {
            if (!IsServer || !hasMark || marked != who) return;
            hasMark = false;
            ClearDoomClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { who } }
            });
            nextScan = Time.time + 1.5f;
        }

        /// <summary>
        /// Server-only. The ledger changed under it. The clock does NOT restart — a swap hands the
        /// remaining seconds to whoever the plate now names, so betrayal buys you time rather than
        /// a fresh start, and the person you handed it to may have very little left.
        /// </summary>
        public void ServerReconsider()
        {
            if (!IsServer || !hasMark) return;
            var ledger = RecordLedger.Instance;
            if (ledger == null) return;

            int slot = ledger.SlotOf(marked);
            if (slot >= 0) return;                      // still names the same person

            float remaining = Mathf.Max(3f, deadline - Time.time);
            ClearDoomClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { marked } }
            });

            // Whoever the Inspector's own file now points at inherits the clock.
            ulong inheritor = ulong.MaxValue;
            var ks = KnowledgeService.Instance;
            float bestScore = -1f;
            foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (ledger.IsOffTheList(c.ClientId)) continue;
                float sc = ks != null ? ks.GetScore(c.ClientId) : 0f;
                if (sc > bestScore) { bestScore = sc; inheritor = c.ClientId; }
            }
            if (inheritor == ulong.MaxValue) { hasMark = false; return; }

            marked = inheritor;
            deadline = Time.time + remaining;
            DoomClientRpc(remaining, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { inheritor } }
            });
            Debug.Log($"[Inspector] Ledger altered — clock passed to {inheritor}, {remaining:0}s left.");
        }

        // ---- the execution ----

        private IEnumerator Execute(ulong who)
        {
            killing = true;
            hasMark = false;

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.ConnectedClients.TryGetValue(who, out var client) ||
                client.PlayerObject == null)
            {
                killing = false;
                yield break;
            }

            var target = client.PlayerObject;
            var pns = target.GetComponent<LastWard.Net.PlayerNetworkState>();

            Vector3 at = ChooseArrival(target.transform);
            transform.position = at;
            transform.rotation = Quaternion.LookRotation(
                new Vector3(target.transform.position.x - at.x, 0f,
                            target.transform.position.z - at.z), Vector3.up);
            manifest.Value = true;
            // Belt and braces: manifest's OnValueChanged does this on every client, but the server's
            // own callback can be missed if the value was already true from an interrupted kill.
            ApplyVisible(true);
            PlayClientRpc("Arrive");

            if (pns != null) pns.ServerSetHeld(true);
            ExecuteClientRpc(transform.position);
            GameEvents.RaiseNoiseEmitted(transform.position, 24f, NoiseSource.PuzzleInteraction);

            // One clip covers the whole thing now: it reaches, lifts, and drives you down.
            PlayClientRpc("Kill");
            LiftClientRpc(who, killSeconds);
            yield return new WaitForSeconds(killSeconds);

            // Same path the Watcher uses: the server flips the victim's alive flag, that syncs, and
            // their own PlayerDeath drives the spectator transition. No per-victim RPC needed.
            if (pns != null)
            {
                pns.ServerKill();
                pns.ServerSetHeld(false);      // released as they die, never left frozen
                pns.ServerSetDiscovery(0f);    // or a spectator watching through them sees a full bar
                pns.ServerSetHidden(false);
            }

            manifest.Value = false;
            killing = false;
            // The queue moves down. Standing still on this floor is standing in line.
            nextScan = Time.time + 6f;
        }

        /// <summary>
        /// Somewhere in the room it can actually stand.
        ///
        /// It used to appear at reachDistance straight along the player's forward vector, which is
        /// fine in open corridor and absurd against a wall — back to the plaster and it came through
        /// the surface you were staring at. This sweeps outward from directly-ahead, taking the first
        /// bearing with clear floor and no wall between it and you, so it still prefers to be in front
        /// of you and settles for beside or behind rather than inside the masonry.
        /// </summary>
        private Vector3 ChooseArrival(Transform target)
        {
            Vector3 eye = target.position + Vector3.up * 1.5f;
            Vector3 best = Vector3.zero;
            bool found = false;

            // Front first, so head-on stays the common case, then progressively to the sides.
            foreach (float deg in new[] { 0f, 30f, -30f, 60f, -60f, 95f, -95f, 130f, -130f, 180f })
            {
                Vector3 dir = Quaternion.Euler(0f, deg, 0f) * target.forward;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f) continue;
                Vector3 spot = target.position + dir.normalized * reachDistance;

                // A wall between us means it would have to come through it.
                if (Physics.Linecast(eye, spot + Vector3.up * 1.4f, out _, ~0,
                        QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out var hit, 5f, ~0,
                        QueryTriggerInteraction.Ignore)) continue;
                // 0.32 radius, not 0.5. Half a metre of clearance is more than a 3m corridor with
                // cell mouths can offer at most bearings, so every candidate failed and the search
                // fell through every single time.
                if (Physics.CheckCapsule(hit.point + Vector3.up * 0.5f, hit.point + Vector3.up * 1.9f,
                        0.32f, ~0, QueryTriggerInteraction.Ignore)) continue;
                best = hit.point;
                found = true;
                break;
            }

            if (found) return best;

            // NOTHING clear. The old fallback returned the player's own position, which put the
            // Inspector inside their head: you died staring at the inside of its mesh and saw
            // nothing at all. Standing it in front and letting it clip a wall is far better — being
            // killed by something you can SEE is the entire point of the encounter.
            Vector3 fwd = target.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            Vector3 ahead = target.position + fwd.normalized * (reachDistance * 0.8f);
            if (Physics.Raycast(ahead + Vector3.up * 2f, Vector3.down, out var floor, 5f, ~0,
                    QueryTriggerInteraction.Ignore))
                ahead.y = floor.point.y;
            else
                ahead.y = target.position.y;
            Debug.LogWarning("[Inspector] No clear arrival bearing — standing it in front regardless.");
            return ahead;
        }

        /// <summary>
        /// Drives the kill by hand rather than trusting the animator.
        ///
        /// The clips are still triggered, but nothing depends on them landing. A rig that fails to
        /// bind produces a statue, and a statue that kills you is worse than no entity at all — this
        /// guarantees it leans in, reaches, and drops, whatever the animation is doing.
        /// </summary>
        private IEnumerator ScriptedKill(Transform victim)
        {
            if (body == null) yield break;
            Quaternion rest = body.localRotation;
            Vector3 restPos = body.localPosition;
            float t = 0f;

            // Lean in over the first second.
            while (t < 1f)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t);
                body.localRotation = rest * Quaternion.Euler(k * 22f, 0f, 0f);
                body.localPosition = restPos + Vector3.forward * (k * 0.5f);
                yield return null;
            }
            // Snap down onto them.
            t = 0f;
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                float k = t / 0.35f;
                body.localRotation = rest * Quaternion.Euler(22f + k * 30f, 0f, 0f);
                body.localPosition = restPos + Vector3.forward * (0.5f + k * 0.55f)
                                             + Vector3.down * (k * 0.35f);
                yield return null;
            }
            body.localRotation = rest;
            body.localPosition = restPos;
        }

        /// <summary>
        /// The skull cracks flare and the room floods.
        ///
        /// This is the hook that makes "the Manager takes the witness" work without a special case:
        /// a player standing here is now LIT, and the Manager already weights being lit at 0.55 —
        /// its heaviest term. They are killed by the rule they have lived under since the ground
        /// floor, suddenly maxed out by something they did not cause.
        /// </summary>
        // Play(), not SetTrigger(). A trigger needs a transition to consume it, and if the state
        // machine is mid-transition or the parameter name drifts, the trigger is silently swallowed
        // and the entity stands in its bind pose. Play() addresses the state directly and cannot
        // fail quietly.
        [ClientRpc]
        private void LiftClientRpc(ulong victim, float seconds)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.LocalClientId != victim) return;
            var po = nm.LocalClient.PlayerObject;
            if (po == null) return;
            var look = po.GetComponentInChildren<LastWard.Player.FirstPersonLook>();
            if (look != null) StartCoroutine(LiftAndSlam(look, seconds));
        }

        /// <summary>
        /// The victim's own camera, hoisted to the Inspector's head and then driven into the floor.
        ///
        /// Timed against the clip rather than run as a separate flourish: it rises over the first
        /// two thirds, hangs there for a beat while you are held at its eye level, then drops far
        /// faster than it rose. The asymmetry is the whole effect — a slam that takes as long as the
        /// lift reads as being set down.
        /// </summary>
        private IEnumerator LiftAndSlam(LastWard.Player.FirstPersonLook look, float seconds)
        {
            float rise = seconds * 0.55f;
            float hang = seconds * 0.28f;
            float fall = Mathf.Max(0.12f, seconds - rise - hang);
            float from = 1.6f;                 // standing eye height
            float to = liftHeight;

            for (float e = 0f; e < rise; e += Time.deltaTime)
            {
                look.ScriptedEyeHeight = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, e / rise));
                yield return null;
            }
            look.ScriptedEyeHeight = to;
            yield return new WaitForSeconds(hang);

            for (float e = 0f; e < fall; e += Time.deltaTime)
            {
                // Accelerating, not eased: it is dropped, not lowered.
                float k = e / fall;
                look.ScriptedEyeHeight = Mathf.Lerp(to, slamHeight, k * k);
                yield return null;
            }
            look.ScriptedEyeHeight = slamHeight;
            GameSfx.Play2D(GameSfx.Random(GameSfx.WrongAttempt), 1f);   // the impact
            yield return new WaitForSeconds(0.6f);
            look.ScriptedEyeHeight = -1f;                                // hand the camera back
        }

        [ClientRpc]
        private void PlayClientRpc(string state)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            animator.Play(state, 0, 0f);
            animator.Update(0f);        // force it onto the pose this frame, not next
        }

        [ClientRpc]
        private void ExecuteClientRpc(Vector3 at)
        {
            var go = new GameObject("InspectorFlare");
            go.transform.position = at + Vector3.up * 1.9f;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 12f;
            l.intensity = 4.5f;
            l.color = new Color(1f, 0.24f, 0.12f);
            l.shadows = LightShadows.None;
            var pool = go.AddComponent<WorldLight>();
            var f = typeof(WorldLight).GetField("radius",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null) f.SetValue(pool, 7.5f);

            var clip = GameSfx.Scream;
            if (clip != null) AudioSource.PlayClipAtPoint(clip, at, 1f);
            Destroy(go, killSeconds + 0.6f);
        }
    }
}
