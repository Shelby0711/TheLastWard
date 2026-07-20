using System.Collections.Generic;
using LastWard.Core;
using LastWard.Knowledge;
using LastWard.Net;
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
            RefreshPlayers();
            bool sees = TryGetVisibleTarget(out var seen);
            if (sees)
            {
                target = seen;
                lastKnownPos = seen.position;
            }

            switch (state)
            {
                case EntityState.Patrol: TickPatrol(sees); break;
                case EntityState.Investigate: TickInvestigate(sees); break;
                case EntityState.Search: TickSearch(sees); break;
                case EntityState.Chase: TickChase(sees); break;
                case EntityState.LostTarget: TickLostTarget(sees); break;
                case EntityState.Return: TickReturn(sees); break;
            }
        }

        // --- states ---

        private void TickPatrol(bool sees)
        {
            if (sees) { EnterState(EntityState.Chase); return; }
            if (hasStimulus) { EnterState(EntityState.Investigate); return; }
            if (waypoints == null || waypoints.Length == 0) return;
            if (HasArrived())
            {
                waypointIndex = (waypointIndex + 1) % waypoints.Length;
                agent.SetDestination(waypoints[waypointIndex].position);
            }
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

            if (Vector3.Distance(transform.position, target.position) <= killRange)
            {
                KillTarget();
                return;
            }
            if (!sees && stateTimer >= lostTargetMemory) EnterState(EntityState.LostTarget);
            else if (sees) stateTimer = 0f;
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

            switch (next)
            {
                case EntityState.Patrol:
                    agent.speed = patrolSpeed;
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
                    agent.speed = chaseSpeed * (1f + tier * 0.1f);
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

        private bool TryGetVisibleTarget(out Transform best)
        {
            best = null;
            float bestScore = float.MinValue;
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            var knowledge = KnowledgeService.Instance;

            foreach (var player in players)
            {
                if (player == null) continue;
                // Dead players stay connected (their body remains for M7's aftermath) — don't hunt them.
                if (player.TryGetComponent<PlayerNetworkState>(out var pns) && !pns.IsAlive) continue;
                Vector3 toPlayer = player.position - transform.position;
                float dist = toPlayer.magnitude;
                if (dist > visionRange) continue;
                if (Vector3.Angle(transform.forward, toPlayer) > visionHalfAngle) continue;

                // Line of sight: if something other than the player blocks the ray, it's hidden.
                Vector3 head = player.position + Vector3.up * 1f;
                if (Physics.Linecast(eye, head, out var hit, ~0, QueryTriggerInteraction.Ignore)
                    && hit.transform != player && !hit.transform.IsChildOf(player))
                    continue;

                // "Knowledge is expensive": prefer the most-knowledgeable visible player; distance
                // only breaks ties. This is what makes the informed player the hunted one.
                float weight = 1f;
                if (knowledge != null && player.TryGetComponent<NetworkObject>(out var netObj))
                    weight = knowledge.GetTargetWeight(netObj.OwnerClientId);
                float score = weight * 10f - dist;
                if (score > bestScore) { bestScore = score; best = player; }
            }
            return best != null;
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
                victim.ServerKill();

            target = null;
            hasStimulus = false;
            stateTimer = -postKillCooldown; // pause before re-engaging
            EnterState(EntityState.Return);
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

        private Vector3 RandomNavPointNear(Vector3 center, float radius)
        {
            Vector3 random = center + Random.insideUnitSphere * radius;
            return NavMesh.SamplePosition(random, out var hit, radius, NavMesh.AllAreas) ? hit.position : center;
        }
    }
}
