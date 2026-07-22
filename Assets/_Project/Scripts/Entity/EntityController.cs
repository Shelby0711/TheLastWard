using System.Collections.Generic;
using LastWard.Aftermath;
using LastWard.Core;
using LastWard.Knowledge;
using LastWard.Net;
using LastWard.Spectator;
using LastWard.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace LastWard.Entity
{
    /// <summary>
    /// Server-authoritative Entity AI: a NavMesh agent driven by the six-state FSM from the plan
    /// (Patrol/Investigate/Search/Chase/LostTarget/Return). Only the server ticks the FSM and moves
    /// the agent; clients see it through a server NetworkTransform and react to the replicated state
    /// enum. Target selection is nearest-visible for now — knowledge weighting hooks in at M4, and
    /// death is the minimal M3 kill (victim's PlayerDeath), with the real spectator flow coming in M6.
    ///
    /// Tuning lives in serialized fields here for now (defaults mirror EntityTuning); a later pass
    /// centralizes them into the EntityTuning ScriptableObject.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EntityController : NetworkBehaviour
    {
        [Header("Patrol")]
        [SerializeField] private Transform[] waypoints;

        [Header("Speeds")]
        [SerializeField] private float patrolSpeed = 2.2f;
        [SerializeField] private float investigateSpeed = 2.0f;
        [SerializeField] private float searchSpeed = 2.4f;
        [SerializeField] private float chaseSpeed = 4.2f;
        [SerializeField] private float returnSpeed = 2.2f;

        [Header("Senses")]
        [SerializeField] private float visionRange = 12f;
        [SerializeField, Range(0, 180)] private float visionHalfAngle = 55f;
        [SerializeField] private float hearingRadius = 12f;
        [SerializeField] private float eyeHeight = 1.5f;

        [Header("Timing")]
        [SerializeField] private float lostTargetMemory = 3f;
        [SerializeField] private float searchDuration = 12f;
        [SerializeField] private float investigateIdle = 6f;
        [SerializeField] private float arriveDistance = 1.2f;

        [Header("Discovery")]
        [Tooltip("Seconds of clean line of sight, at mid range, to go from unseen to found.")]
        [SerializeField] private float timeToDiscover = 2.6f;
        [Tooltip("How fast discovery drains once it can no longer see you.")]
        [SerializeField] private float discoveryDecayPerSecond = 0.22f;
        [Tooltip("Extra drain while inside a hiding spot, on top of the normal decay.")]
        [SerializeField] private float hiddenDecayBonus = 0.5f;
        [Tooltip("A lit flashlight makes you far easier to pick out of the dark.")]
        [SerializeField] private float flashlightDetectionMultiplier = 2.4f;
        [Tooltip("Discovery at which it may stop and stare instead of continuing to lurk.")]
        [SerializeField, Range(0f, 1f)] private float stareThreshold = 0.55f;
        [SerializeField] private float stareChance = 0.5f;
        [SerializeField] private float stareDuration = 2.5f;
        [Tooltip("After staring, the odds it rushes rather than melting away.")]
        [SerializeField] private float rushAfterStareChance = 0.45f;

        [Header("Unpredictability")]
        [Tooltip("It won't start a chase for this long after the run begins, so the opening puzzle " +
            "isn't an immediate sprint. It still patrols and can be seen during this window.")]
        [SerializeField] private float openingGrace = 45f;
        [Tooltip("A chase runs for a random duration in this range before the Entity may break off " +
            "and vanish. Rolled fresh per chase so the player can never learn the number.")]
        [SerializeField] private float chaseCommitMin = 7f;
        [SerializeField] private float chaseCommitMax = 16f;
        [Tooltip("It won't break off if it's this close — being nearly caught should stay committed.")]
        [SerializeField] private float commitWithinDistance = 4.5f;
        [Tooltip("How long it stays away, ignoring players entirely, after breaking off.")]
        [SerializeField] private float withdrawDuration = 14f;
        [SerializeField] private float withdrawSpeed = 3.4f;
        [Tooltip("Odds that finishing a patrol leg turns into a lurk around the nearest player.")]
        [SerializeField, Range(0f, 1f)] private float stalkChance = 0.35f;
        [SerializeField] private float stalkDuration = 18f;
        [SerializeField] private float stalkRadiusMin = 7f;
        [SerializeField] private float stalkRadiusMax = 13f;
        [Tooltip("Every speed is jittered by this fraction on each state entry, so its pace never " +
            "reads as one constant number.")]
        [SerializeField, Range(0f, 0.6f)] private float speedJitter = 0.25f;
        [Tooltip("Speed multiplier for a rush coming out of a stare.")]
        [SerializeField] private float rushSpeedMultiplier = 1.3f;
        [Tooltip("Extra seconds it stays away after being struck with the pipe, on top of the " +
            "normal withdrawal. This is the whole value of the weapon, so it needs to be felt.")]
        [SerializeField] private float repelExtraSeconds = 12f;

        [Header("Kill")]
        [SerializeField] private float killRange = 1.6f;
        [SerializeField] private float postKillCooldown = 4f;

        private readonly NetworkVariable<EntityState> netState =
            new NetworkVariable<EntityState>(EntityState.Patrol);

        private NavMeshAgent agent;
        private EntityState state = EntityState.Patrol;
        private float stateTimer;
        private int waypointIndex;
        private Transform target;
        private Vector3 lastKnownPos;
        private Vector3 stimulusPos;
        private bool hasStimulus;

        private float runTime;          // since spawn, for the opening grace window
        private float chaseElapsed;     // continuous chase time; stateTimer resets while visible
        private float chaseCommit;      // rolled per chase — how long before it may break off
        private int previousWaypoint = -1;
        private bool rushing;           // this chase came out of a stare, so it comes in fast

        private readonly List<Transform> players = new List<Transform>();

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
        }

        public override void OnNetworkSpawn()
        {
            netState.OnValueChanged += OnNetStateChanged;
            if (!IsServer)
            {
                // Clients don't path — the server NetworkTransform drives position.
                agent.enabled = false;
                return;
            }
            GameEvents.OnNoiseEmitted += OnNoiseHeard;
            // Ensure the agent starts on the baked NavMesh even if spawned slightly above it.
            if (NavMesh.SamplePosition(transform.position, out var navHit, 4f, NavMesh.AllAreas))
                agent.Warp(navHit.position);
            EnterState(EntityState.Patrol);
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;
            if (IsServer) GameEvents.OnNoiseEmitted -= OnNoiseHeard;
        }

        private void OnNetStateChanged(EntityState previous, EntityState current)
        {
            // Fires on every peer (including server) so local audio/anim systems can react in M8.
            GameEvents.RaiseEntityStateChanged(current);
        }

        private void Update()
        {
            if (!IsServer || !agent.enabled || !agent.isOnNavMesh) return;

            stateTimer += Time.deltaTime;
            runTime += Time.deltaTime;
            RefreshPlayers();

            // Seeing someone no longer means hunting them. Line of sight feeds a per-player
            // discovery meter; only a full meter promotes to a chase. That's what turns "it spotted
            // me the instant I walked in" into something the player can actually play against.
            bool sees = TickDiscovery(out var seen, out float topDiscovery);
            if (sees)
            {
                target = seen;
                lastKnownPos = seen.position;
            }
            bool engages = seen != null && topDiscovery >= 1f && CanEngage;

            // The stop-and-watch beat: it has half-noticed someone and holds their gaze before
            // deciding whether to commit. Only from the unhurried states — never mid-chase.
            if (!engages && sees && CanEngage && topDiscovery >= stareThreshold &&
                (state == EntityState.Patrol || state == EntityState.Stalk) &&
                Random.value < stareChance * Time.deltaTime)
            {
                target = seen;
                EnterState(EntityState.Stare);
            }

            switch (state)
            {
                case EntityState.Patrol: TickPatrol(engages); break;
                case EntityState.Investigate: TickInvestigate(engages); break;
                case EntityState.Search: TickSearch(engages); break;
                case EntityState.Chase: TickChase(sees); break;
                case EntityState.LostTarget: TickLostTarget(engages); break;
                case EntityState.Return: TickReturn(engages); break;
                case EntityState.Withdraw: TickWithdraw(); break;
                case EntityState.Stalk: TickStalk(engages); break;
                case EntityState.Stare: TickStare(); break;
            }
        }

        /// <summary>
        /// Advances every living player's discovery meter and reports the most-discovered one it can
        /// currently see. Meters rise only with line of sight and drain otherwise, so breaking sight
        /// — a corner, a doorway, a wardrobe — is always the counterplay.
        /// </summary>
        private bool TickDiscovery(out Transform seen, out float topDiscovery)
        {
            seen = null;
            topDiscovery = 0f;
            float best = float.MinValue;
            var knowledge = KnowledgeService.Instance;

            foreach (var player in players)
            {
                if (player == null) continue;
                if (!player.TryGetComponent<PlayerNetworkState>(out var pns) || !pns.IsAlive) continue;

                float current = pns.Discovery;
                // Distance is computed up front rather than through an out-param: `CanEngage &&
                // IsVisible(...)` short-circuits during the opening grace, which would leave an
                // out-param unassigned by the time the scoring below reads it.
                float distance = Vector3.Distance(transform.position, player.position);
                bool visible = CanEngage && IsVisible(player, pns, distance);

                if (visible)
                {
                    // Closer reads faster, and a lit torch in a dark ward is a beacon.
                    float proximity = Mathf.Lerp(2f, 0.65f, Mathf.Clamp01(distance / visionRange));
                    float rate = proximity / Mathf.Max(0.1f, timeToDiscover);
                    if (pns.FlashlightOn) rate *= flashlightDetectionMultiplier;
                    current += rate * Time.deltaTime;
                }
                else
                {
                    float decay = discoveryDecayPerSecond + (pns.IsHidden ? hiddenDecayBonus : 0f);
                    current -= decay * Time.deltaTime;
                }

                current = Mathf.Clamp01(current);
                pns.ServerSetDiscovery(current);

                if (!visible) continue;
                // Same "knowledge is expensive" weighting as before — the best-informed visible
                // player wins ties, so learning the hospital's secrets makes you the hunted one.
                float weight = 1f;
                if (knowledge != null && player.TryGetComponent<NetworkObject>(out var netObj))
                    weight = knowledge.GetTargetWeight(netObj.OwnerClientId);
                float score = current * 10f + weight * 5f - distance * 0.1f;
                if (score <= best) continue;
                best = score;
                seen = player;
                topDiscovery = current;
            }

            return seen != null;
        }

        private bool IsVisible(Transform player, PlayerNetworkState pns, float distance)
        {
            if (pns.IsHidden) return false;
            if (distance > visionRange) return false;
            if (Vector3.Angle(transform.forward, player.position - transform.position) > visionHalfAngle) return false;

            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 head = player.position + Vector3.up * 1f;
            if (Physics.Linecast(eye, head, out var hit, ~0, QueryTriggerInteraction.Ignore)
                && hit.transform != player && !hit.transform.IsChildOf(player))
                return false;
            return true;
        }

        private bool CanEngage => runTime >= openingGrace;

        // --- states ---

        private void TickPatrol(bool sees)
        {
            if (sees) { EnterState(EntityState.Chase); return; }
            if (hasStimulus) { EnterState(EntityState.Investigate); return; }
            if (waypoints == null || waypoints.Length == 0) return;
            if (!HasArrived()) return;

            // Sometimes a patrol leg turns into circling whoever is closest instead of carrying on
            // to the next point — the difference between a guard on a route and something hunting.
            if (players.Count > 0 && CanEngage && Random.value < stalkChance)
            {
                EnterState(EntityState.Stalk);
                return;
            }
            waypointIndex = PickNextWaypoint();
            agent.SetDestination(waypoints[waypointIndex].position);
        }

        private void TickInvestigate(bool sees)
        {
            if (sees) { EnterState(EntityState.Chase); return; }
            if (HasArrived())
            {
                if (stateTimer >= investigateIdle) EnterState(EntityState.Search);
            }
        }

        private void TickSearch(bool sees)
        {
            if (sees) { EnterState(EntityState.Chase); return; }
            if (HasArrived())
                agent.SetDestination(RandomNavPointNear(lastKnownPos, 5f));
            if (stateTimer >= searchDuration) EnterState(EntityState.Return);
        }

        private void TickChase(bool sees)
        {
            if (target == null) { EnterState(EntityState.LostTarget); return; }
            agent.SetDestination(lastKnownPos);
            chaseElapsed += Time.deltaTime;

            float distance = Vector3.Distance(transform.position, target.position);
            if (distance <= killRange)
            {
                KillTarget();
                return;
            }

            // Break off mid-chase and vanish. Only once it's been chasing a while AND isn't right
            // on top of the player — being nearly caught should always stay committed, otherwise
            // escapes stop feeling earned. The commit time is re-rolled per chase, so outrunning it
            // once teaches the player nothing about the next time.
            if (chaseElapsed >= chaseCommit && distance > commitWithinDistance)
            {
                EnterState(EntityState.Withdraw);
                return;
            }

            if (!sees && stateTimer >= lostTargetMemory) EnterState(EntityState.LostTarget);
            else if (sees) stateTimer = 0f;
        }

        // Circles the area at a distance instead of beelining. Keeps it audible and occasionally
        // glimpsed without it actually closing, which is where most of the dread lives.
        private void TickStalk(bool engages)
        {
            if (engages) { EnterState(EntityState.Chase); return; }
            if (hasStimulus) { EnterState(EntityState.Investigate); return; }
            if (stateTimer >= stalkDuration) { EnterState(EntityState.Patrol); return; }
            if (!HasArrived()) return;

            // A ring around the nearest player rather than a point on top of them.
            var nearest = NearestPlayer();
            if (nearest == null) { EnterState(EntityState.Patrol); return; }
            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(stalkRadiusMin, stalkRadiusMax);
            Vector3 ring = nearest.position + new Vector3(offset.x, 0f, offset.y);
            agent.SetDestination(RandomNavPointNear(ring, 2.5f));
        }

        // Stops dead and watches. The pause is the point — then it either commits or is simply
        // gone the next time you look.
        private void TickStare()
        {
            if (target != null)
            {
                Vector3 flat = target.position - transform.position;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(flat), Time.deltaTime * 6f);
            }
            if (stateTimer < stareDuration) return;

            if (target != null && Random.value < rushAfterStareChance)
            {
                rushing = true;
                EnterState(EntityState.Chase);
            }
            else
            {
                EnterState(EntityState.Withdraw);
            }
        }

        private Transform NearestPlayer()
        {
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (var player in players)
            {
                if (player == null) continue;
                if (player.TryGetComponent<PlayerNetworkState>(out var pns) && !pns.IsAlive) continue;
                float d = Vector3.Distance(transform.position, player.position);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = player;
            }
            return best;
        }

        /// <summary>
        /// Driven off by a pipe to the head. Breaks the chase, clears the meter of whoever swung so
        /// they aren't instantly re-found, and sends it away for longer than a normal withdrawal —
        /// the swing has to buy enough time to actually matter.
        /// </summary>
        public void ServerRepel(Vector3 fromPosition)
        {
            if (!IsServer) return;

            foreach (var player in players)
            {
                if (player == null) continue;
                if (Vector3.Distance(player.position, fromPosition) > 2f) continue;
                if (player.TryGetComponent<PlayerNetworkState>(out var pns)) pns.ServerSetDiscovery(0f);
            }

            target = null;
            hasStimulus = false;
            EnterState(EntityState.Withdraw);
            // Negative timer extends the state past its normal duration.
            stateTimer = -repelExtraSeconds;
        }

        // Deliberately ignores whether it can see anyone — walking away from a visible player is
        // the entire point of the state.
        private void TickWithdraw()
        {
            if (stateTimer >= withdrawDuration) { EnterState(EntityState.Patrol); return; }
            if (HasArrived())
            {
                waypointIndex = PickNextWaypoint();
                agent.SetDestination(waypoints != null && waypoints.Length > 0
                    ? waypoints[waypointIndex].position
                    : RandomNavPointNear(transform.position, 12f));
            }
        }

        private void TickLostTarget(bool sees)
        {
            if (sees) { EnterState(EntityState.Chase); return; }
            if (HasArrived())
                EnterState(EntityState.Search);
        }

        private void TickReturn(bool sees)
        {
            if (sees) { EnterState(EntityState.Chase); return; }
            if (hasStimulus) { EnterState(EntityState.Investigate); return; }
            if (HasArrived())
                EnterState(EntityState.Patrol);
        }

        // --- transitions ---

        private void EnterState(EntityState next)
        {
            state = next;
            stateTimer = 0f;
            netState.Value = next;

            // Stare is the only state that plants it; everything else must clear the flag or the
            // agent stays frozen for the rest of the run.
            if (agent.isOnNavMesh) agent.isStopped = next == EntityState.Stare;

            switch (next)
            {
                case EntityState.Patrol:
                    agent.speed = Jitter(patrolSpeed);
                    if (waypoints != null && waypoints.Length > 0)
                        agent.SetDestination(waypoints[waypointIndex].position);
                    break;
                case EntityState.Investigate:
                    agent.speed = investigateSpeed;
                    agent.SetDestination(hasStimulus ? stimulusPos : lastKnownPos);
                    hasStimulus = false;
                    break;
                case EntityState.Search:
                    agent.speed = searchSpeed;
                    agent.SetDestination(RandomNavPointNear(lastKnownPos, 5f));
                    break;
                case EntityState.Chase:
                    // Team-total knowledge ramps the whole run's pressure (aggression tier).
                    int tier = KnowledgeService.Instance != null ? KnowledgeService.Instance.GetAggressionTier() : 0;
                    agent.speed = Jitter(chaseSpeed) * (1f + tier * 0.1f) * (rushing ? rushSpeedMultiplier : 1f);
                    rushing = false;
                    chaseElapsed = 0f;
                    // Rolled per chase, and the more the team knows the longer it stays committed.
                    chaseCommit = Random.Range(chaseCommitMin, chaseCommitMax) * (1f + tier * 0.15f);
                    break;
                case EntityState.Stalk:
                    // Slower than a patrol: it isn't going anywhere, it's circling.
                    agent.speed = Jitter(patrolSpeed * 0.8f);
                    break;
                case EntityState.Stare:
                    break;
                case EntityState.Withdraw:
                    agent.speed = Jitter(withdrawSpeed);
                    target = null;
                    hasStimulus = false;
                    waypointIndex = FarthestWaypointFromPlayers();
                    if (waypoints != null && waypoints.Length > 0)
                        agent.SetDestination(waypoints[waypointIndex].position);
                    break;
                case EntityState.LostTarget:
                    agent.speed = searchSpeed;
                    agent.SetDestination(lastKnownPos);
                    break;
                case EntityState.Return:
                    agent.speed = returnSpeed;
                    agent.SetDestination(NearestWaypoint());
                    break;
            }
        }

        // A single fixed speed per state is itself a tell — the player learns exactly how fast it
        // is and paces themselves against it. Jittering each entry keeps that read fuzzy.
        private float Jitter(float speed) => speed * Random.Range(1f - speedJitter, 1f + speedJitter);

        // True once the agent has closed on its destination — or has no path at all (remainingDistance
        // is Infinity with no path, which never satisfies a <= comparison, so without this an agent
        // whose path got cleared for any reason would idle forever instead of picking a new one).
        private bool HasArrived() =>
            !agent.pathPending && (!agent.hasPath || agent.remainingDistance <= arriveDistance);

        // --- senses ---

        private void RefreshPlayers()
        {
            players.Clear();
            if (NetworkManager.Singleton == null) return;
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
                if (client.PlayerObject != null) players.Add(client.PlayerObject.transform);
        }

        private void OnNoiseHeard(Vector3 position, float radius, NoiseSource source)
        {
            if (Vector3.Distance(transform.position, position) > radius + hearingRadius) return;
            if (state == EntityState.Chase) return; // already engaged
            stimulusPos = position;
            hasStimulus = true;
        }

        // --- kill ---

        private void KillTarget()
        {
            // Server sets the victim's alive flag; it syncs to all clients, the victim's PlayerDeath
            // handles the death->spectator transition, and everyone (incl. the Entity's own
            // targeting) then treats them as dead. No per-victim RPC needed anymore.
            if (target != null && target.TryGetComponent<PlayerNetworkState>(out var victim))
            {
                Vector3 deathPos = target.position;
                victim.ServerKill();
                // Clear their meter, or a spectator watching through them sees a full bar frozen
                // on screen for the rest of the run.
                victim.ServerSetDiscovery(0f);
                victim.ServerSetHidden(false);
                int tier = KnowledgeService.Instance != null ? KnowledgeService.Instance.GetAggressionTier() : 0;
                AftermathManager.Instance?.ServerTriggerAftermath(deathPos, tier);

                // Bible §7, third ending: if that was the last living player, no one reached the
                // exit — a shared ending, not just this player's personal death screen.
                if (!SpectatorController.AnyOtherAlive(victim))
                {
                    ObjectiveTracker.Instance?.ServerAdvanceTo(ObjectiveStage.Ended);
                    AnnounceNoEscapeClientRpc();
                }
            }

            target = null;
            hasStimulus = false;
            stateTimer = -postKillCooldown; // pause before re-engaging
            EnterState(EntityState.Return);
        }

        [ClientRpc]
        private void AnnounceNoEscapeClientRpc()
        {
            EndingUI.Instance?.Show("NO ONE LEAVES.");
        }

        // --- helpers ---

        private Vector3 NearestWaypoint()
        {
            if (waypoints == null || waypoints.Length == 0) return transform.position;
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < waypoints.Length; i++)
            {
                float d = Vector3.Distance(transform.position, waypoints[i].position);
                if (d < best) { best = d; nearest = i; }
            }
            waypointIndex = nearest;
            return waypoints[nearest].position;
        }

        /// <summary>
        /// Picks the next patrol point at random rather than walking the array in order — a fixed
        /// loop is learnable within a couple of minutes, which is what made it feel like it was
        /// "running in circles". Distant points are weighted higher so it actually crosses the map
        /// instead of shuffling between two neighbours, and it never picks the one it just left.
        /// </summary>
        private int PickNextWaypoint()
        {
            if (waypoints == null || waypoints.Length == 0) return 0;
            if (waypoints.Length <= 2) return (waypointIndex + 1) % waypoints.Length;

            float total = 0f;
            var weights = new float[waypoints.Length];
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (i == waypointIndex || i == previousWaypoint || waypoints[i] == null) continue;
                float distance = Vector3.Distance(transform.position, waypoints[i].position);
                weights[i] = 1f + distance;
                total += weights[i];
            }
            if (total <= 0f) return (waypointIndex + 1) % waypoints.Length;

            float roll = Random.value * total;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0f) continue;
                roll -= weights[i];
                if (roll > 0f) continue;
                previousWaypoint = waypointIndex;
                return i;
            }
            previousWaypoint = waypointIndex;
            return (waypointIndex + 1) % waypoints.Length;
        }

        /// <summary>Where to disappear to: the patrol point furthest from anyone still alive.</summary>
        private int FarthestWaypointFromPlayers()
        {
            if (waypoints == null || waypoints.Length == 0) return 0;
            int best = 0;
            float bestDistance = float.MinValue;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                float nearestPlayer = float.MaxValue;
                foreach (var player in players)
                {
                    if (player == null) continue;
                    nearestPlayer = Mathf.Min(nearestPlayer, Vector3.Distance(waypoints[i].position, player.position));
                }
                if (nearestPlayer == float.MaxValue) nearestPlayer = 0f;
                if (nearestPlayer <= bestDistance) continue;
                bestDistance = nearestPlayer;
                best = i;
            }
            return best;
        }

        private Vector3 RandomNavPointNear(Vector3 center, float radius)
        {
            Vector3 random = center + Random.insideUnitSphere * radius;
            return NavMesh.SamplePosition(random, out var hit, radius, NavMesh.AllAreas) ? hit.position : center;
        }
    }
}
