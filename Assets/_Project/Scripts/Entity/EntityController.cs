using System.Collections.Generic;
using LastWard.Aftermath;
using LastWard.Audio;
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
        [SerializeField] private float visionRange = 16f;
        [Tooltip("Half-angle of the vision cone. Wide on purpose — a narrow cone made it walk past " +
            "players standing in the open, because it is only ever looking where it is going.")]
        [SerializeField, Range(0, 180)] private float visionHalfAngle = 85f;
        [SerializeField] private float hearingRadius = 14f;
        [Tooltip("Inside this radius it senses a player REGARDLESS of facing or line of sight. " +
            "This is what stops it strolling past someone pressed into a corner beside it — it is " +
            "not a person with eyes, it is something that knows you are there.")]
        [SerializeField] private float proximitySenseRadius = 6f;
        [Tooltip("Meter gained per second from proximity alone, at the very edge of that radius. " +
            "Scales up as it closes. Kept low — this is background pressure, not the main source.")]
        [SerializeField] private float proximityFillRate = 0.035f;
        [Tooltip("How far it can sense a player who is hiding. Much shorter — hiding works, but " +
            "hiding in a spot it walks right past does not.")]
        [SerializeField] private float hiddenSenseRadius = 2.2f;
        [Tooltip("Everything sensory is multiplied by this while the player crouches. Crouching is " +
            "the stealth verb; it has to actually buy something.")]
        [SerializeField, Range(0.1f, 1f)] private float crouchSenseMultiplier = 0.45f;
        [Tooltip("Proximity radius when a wall stands between them. Deliberately tiny — adjacent " +
            "through a door, not across a room.")]
        [SerializeField] private float throughWallSenseRadius = 1.6f;
        [Tooltip("Unhidden and this close in its FACING vision cone starts the chase at once — no " +
            "meter wait, no opening grace.")]
        [SerializeField] private float immediateThreatRadius = 4f;
        [Tooltip("Unhidden and this close with no wall between starts the chase REGARDLESS of which " +
            "way it faces. This is what stops it standing beside or behind a player noticing nothing.")]
        [SerializeField] private float pointBlankRadius = 3.2f;
        [SerializeField] private float eyeHeight = 1.5f;

        [Header("Torch betrayal")]
        [Tooltip("A player shining their flashlight AT the Entity gives themselves away — even from " +
            "a hiding spot. The beam has to actually land on it: within this range...")]
        [SerializeField] private float torchBetrayRange = 18f;
        [Tooltip("...and within this half-angle of where the player is looking. Wide enough that " +
            "'pointing at it' counts, tight enough that sweeping the room past it does not stick.")]
        [SerializeField] private float torchBetrayHalfAngle = 30f;

        [Header("Presence")]
        [Tooltip("A living player within this range, looking roughly at it, counts as SEEING it. " +
            "Repositioning and vanishing are only ever allowed when nobody does - that single rule " +
            "is what makes it read as 'it was suddenly there' instead of something you watched " +
            "walk in from down the corridor.")]
        [SerializeField] private float observeRange = 40f;
        [SerializeField, Range(10f, 90f)] private float observeHalfAngle = 65f;
        [Tooltip("How far off it stages an appearance - far enough to read as a silhouette you are " +
            "not certain about, never close enough to feel reachable.")]
        [SerializeField] private float sightingDistanceMin = 10f;
        [SerializeField] private float sightingDistanceMax = 20f;
        [Tooltip("Longest it will hold a staged appearance if the player simply never looks away.")]
        [SerializeField] private float appearanceHoldMax = 14f;
        [Tooltip("While absent it keeps at least this far from every player. If someone wanders " +
            "closer it relocates (unseen) rather than letting itself be walked up to.")]
        [SerializeField] private float absentMinDistance = 22f;

        [Header("Timing")]
        [SerializeField] private float lostTargetMemory = 3f;
        [SerializeField] private float searchDuration = 12f;
        [SerializeField] private float investigateIdle = 6f;
        [SerializeField] private float arriveDistance = 1.2f;

        [Header("Discovery")]
        [Tooltip("Seconds of clean line of sight, at mid range, to go from unseen to found. Long " +
            "on purpose: being SEEN is a slow burn, while the noise you make is the fast, obvious " +
            "cost. Short values here made the meter snap to full before the player could react.")]
        [SerializeField] private float timeToDiscover = 18f;
        [Tooltip("How fast discovery drains once it can no longer see you.")]
        [SerializeField] private float discoveryDecayPerSecond = 0.3f;
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
        [Tooltip("While it stands and watches, closing inside this distance triggers an immediate " +
            "full-speed rush — trying to slip past something that is staring at you is punished.")]
        [SerializeField] private float stareTripDistance = 6f;
        [Tooltip("Meter gained per second while the torch is on. Tuned so a few seconds of light " +
            "adds roughly 30% — the difference between 'made a noise' and 'made a noise while lit'.")]
        [SerializeField] private float torchDrawPerSecond = 0.05f;

        [Header("Unpredictability")]
        [Tooltip("It won't start a chase for this long after the run begins, so the opening puzzle " +
            "isn't an immediate sprint. It still patrols and can be seen during this window.")]
        [SerializeField] private float openingGrace = 20f;
        [Tooltip("A chase runs for a random duration in this range before the Entity may break off " +
            "and vanish. Rolled fresh per chase so the player can never learn the number.")]
        [SerializeField] private float chaseCommitMin = 7f;
        [SerializeField] private float chaseCommitMax = 16f;
        [Tooltip("It won't break off if it's this close — being nearly caught should stay committed.")]
        [SerializeField] private float commitWithinDistance = 4.5f;
        [Tooltip("How long it stays away, ignoring players entirely, after breaking off.")]
        [SerializeField] private float withdrawDuration = 14f;
        [SerializeField] private float withdrawSpeed = 3.4f;
        [Tooltip("Odds that finishing a patrol leg turns into a lurk around the nearest player. " +
            "High by design — a fixed patrol route is what made it feel like a guard on rails " +
            "rather than something hunting.")]
        [SerializeField, Range(0f, 1f)] private float stalkChance = 0.6f;
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

        [Header("Presence")]
        [Tooltip("Typical seconds spent absent early in the run. It should be a rumour in the Lobby.")]
        [SerializeField] private float dormantSecondsEarly = 30f;
        [Tooltip("Typical seconds absent late in the run — by the Corridor it barely leaves.")]
        [SerializeField] private float dormantSecondsLate = 12f;
        [Tooltip("How long a brief appearance lasts before it withdraws again.")]
        [SerializeField] private float appearanceSeconds = 22f;
        [Tooltip("How far away it appears. Far enough to be a silhouette, not an ambush.")]
        [SerializeField] private float appearanceDistance = 14f;

        [Tooltip("How far its head turns off its path while patrolling, in degrees each way.")]
        [SerializeField] private float gazeSweepAngle = 55f;
        [SerializeField] private float gazeSweepSpeed = 0.7f;

        [Header("Kill")]
        [SerializeField] private float killRange = 1.6f;
        [SerializeField] private float postKillCooldown = 4f;
        [Tooltip("While actively chasing, the target's meter is held at least this high so the chase " +
            "reads as a chase — pounding heart, heavy breathing — without being a death sentence.")]
        [SerializeField, Range(0f, 1f)] private float chaseFearFloor = 0.72f;
        [Tooltip("When it gives up a chase, the target's meter drops to this so they get to breathe " +
            "and the entity doesn't instantly re-commit off a still-full meter.")]
        [SerializeField, Range(0f, 1f)] private float chaseBreakoffDiscovery = 0.45f;
        [Tooltip("How long the catch is held before the screen goes. It does not celebrate and it " +
            "does not rush - the horror is the intimacy and the certainty, so this is deliberately " +
            "long. Must match the authored Watcher_Catch clip.")]
        [SerializeField] private float catchSeconds = 7f;
        [Tooltip("How close it settles while holding the victim.")]
        [SerializeField] private float catchHoldDistance = 0.6f;
        [SerializeField] private float catchApproachSpeed = 1.4f;
        [Tooltip("Volume of the randomised death stinger. Deliberately at the ceiling - this is the " +
            "loudest thing in the game and it should be.")]
        [SerializeField, Range(0f, 1f)] private float jumpscareVolume = 1f;
        [Tooltip("While hunting, any shut (unlocked) door this close gets kicked open with a slam " +
            "instead of the Entity easing through it. Locked puzzle doors still hold.")]
        [SerializeField] private float doorSlamRange = 3f;

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
        private float gazeSweepOffset;  // per-instance phase so the sweep is not a metronome
        private float nextAppearance;
        private float appearanceEndsAt;
        private LastWard.Net.NetworkedDoor[] doors;
        private float nextDoorScan;
        private Unity.Netcode.Components.NetworkTransform netTransform;
        private bool parkedWhileAbsent;   // has it already teleported out of the way this absence?
        private Renderer[] visualRenderers;
        private bool jumpscareRunning;

        private readonly List<Transform> players = new List<Transform>();

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            gazeSweepOffset = Random.Range(0f, Mathf.PI * 2f);
        }

        /// <summary>
        /// Repositions the Entity as a REAL teleport, on every peer.
        ///
        /// <c>agent.Warp</c> alone only moves the server's transform. The replicated NetworkTransform
        /// then smoothly INTERPOLATES to the new spot, so every client watches the Entity glide
        /// across the entire level — which is exactly what "it glides around the map" was. Cutting
        /// the NetworkTransform too is what makes it simply *be* somewhere else, which is the whole
        /// point of an Entity that appears out of nowhere.
        /// </summary>
        private void ServerTeleport(Vector3 position, Quaternion? rotation = null)
        {
            Quaternion rot = rotation ?? transform.rotation;
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Warp(position);
            else transform.position = position;
            transform.rotation = rot;
            if (netTransform != null) netTransform.Teleport(position, rot, transform.localScale);
        }

        public override void OnNetworkSpawn()
        {
            netState.OnValueChanged += OnNetStateChanged;
            netTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();
            ApplyPresenceVisibility(netState.Value);   // clients join mid-absence too
            if (!IsServer)
            {
                // Clients don't path — the server NetworkTransform drives position.
                agent.enabled = false;
                return;
            }
            GameEvents.OnNoiseEmitted += OnNoiseHeard;
            // Ensure the agent starts on the baked NavMesh even if spawned slightly above it.
            if (NavMesh.SamplePosition(transform.position, out var navHit, 4f, NavMesh.AllAreas))
                ServerTeleport(navHit.position);
            // Starts absent, not patrolling. It arrives when the building decides it should.
            EnterState(EntityState.Dormant);
            ScheduleNextAppearance();
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;
            if (IsServer) GameEvents.OnNoiseEmitted -= OnNoiseHeard;
        }

        /// <summary>
        /// Absence is LITERAL: while dormant the Entity is not rendered at all. Parking it far away
        /// and hoping nobody wandered over was not enough - players kept finding it standing
        /// motionless in the Exterior, which turns the thing the whole game is built around into a
        /// prop you can walk up and inspect. It is not in the building; now it looks that way.
        /// Driven off the replicated state so every client agrees.
        /// </summary>
        private void ApplyPresenceVisibility(EntityState current)
        {
            if (visualRenderers == null)
            {
                var visual = transform.Find("Visual");
                visualRenderers = visual != null
                    ? visual.GetComponentsInChildren<Renderer>(true)
                    : new Renderer[0];
            }
            bool absent = current == EntityState.Dormant;
            foreach (var r in visualRenderers)
                if (r != null) r.enabled = !absent;
        }

        private void OnNetStateChanged(EntityState previous, EntityState current)
        {
            ApplyPresenceVisibility(current);
            // Fires on every peer (including server) so local audio/anim systems can react in M8.
            GameEvents.RaiseEntityStateChanged(current);
        }

        private void Update()
        {
            if (!IsServer || agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            // The jumpscare disables the agent and drives the transform itself. Ticking the FSM
            // through that window called SetDestination on a disabled agent, which is the error
            // thrown at the moment of death.
            if (jumpscareRunning) return;

            stateTimer += Time.deltaTime;
            runTime += Time.deltaTime;

            // A brief appearance expires back into absence — unless it is chasing, which always
            // plays out.
            if (!IsDormant && Time.time > appearanceEndsAt &&
                state != EntityState.Chase && state != EntityState.Stare && !jumpscareRunning)
            {
                EnterState(EntityState.Dormant);
                ScheduleNextAppearance();
            }
            RefreshPlayers();

            // Seeing someone no longer means hunting them. Line of sight feeds a per-player
            // discovery meter; only a full meter promotes to a chase. That's what turns "it spotted
            // me the instant I walked in" into something the player can actually play against.
            bool sees = TickDiscovery(out var seen, out float topDiscovery);

            // TickDiscovery can START the jumpscare (the immediate-threat kill), and the jumpscare
            // disables the agent. Without bailing out here the rest of this frame's FSM still ran
            // and called SetDestination on that now-disabled agent — the error thrown at the moment
            // of death. The guard at the top of Update only covers frames that BEGIN mid-jumpscare.
            if (jumpscareRunning) return;

            if (sees)
            {
                target = seen;
                lastKnownPos = seen.position;
            }

            // A full meter means it has FOUND you — it commits even if you've since stepped out of
            // its vision cone. Previously engagement also required current line of sight, so it
            // would walk straight past a player whose meter was already full, which read as the
            // Entity simply not caring.
            var found = FindFullyDiscoveredPlayer();
            if (found != null && CanEngage)
            {
                target = found;
                lastKnownPos = found.position;

                // A full meter means it KNOWS WHERE YOU ARE and commits — it does not mean instant
                // death. It has to close the distance and catch you, which is what turns the meter
                // into a chase you might survive rather than a countdown you cannot.
                if (!jumpscareRunning && state != EntityState.Chase)
                {
                    rushing = true;
                    appearanceEndsAt = float.MaxValue;
                    EnterState(EntityState.Chase);
                }
            }
            bool engages = CanEngage && (found != null || (seen != null && topDiscovery >= 1f));

            // Half-noticing someone makes it COME AND LOOK. Previously a partly-filled meter changed
            // nothing about where it walked, so it would sense a player and carry on to its next
            // waypoint — which is exactly what read as being ignored. Now suspicion redirects it.
            if (seen != null && topDiscovery > 0.3f && CanEngage &&
                (state == EntityState.Patrol || state == EntityState.Stalk || state == EntityState.Return))
            {
                target = seen;
                lastKnownPos = seen.position;
                if (state != EntityState.Investigate)
                {
                    stimulusPos = seen.position;
                    hasStimulus = true;
                    EnterState(EntityState.Investigate);
                }
            }

            // The stop-and-watch beat: it has half-noticed someone and holds their gaze before
            // deciding whether to commit. Only from the unhurried states — never mid-chase.
            if (!engages && sees && CanEngage && topDiscovery >= stareThreshold &&
                (state == EntityState.Patrol || state == EntityState.Stalk) &&
                Random.value < Mathf.Lerp(stareChance, stareChance * 3f, Progress) * Time.deltaTime)
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
                case EntityState.Dormant: TickDormant(); break;
            }

            TrySlamDoorsAhead();
        }

        /// <summary>
        /// The Entity does not open doors — it goes through them. While it is actively hunting, any
        /// shut, unlocked door it reaches is kicked open with a slam, so it bursts into the room
        /// rather than gliding through the panel. Locked puzzle doors are left alone: those are
        /// barriers the players earned, and they hold against the Entity too.
        ///
        /// Approach works because a shut door's NavMeshObstacle carves the doorway, so the agent
        /// pathing to a target beyond it stops at the threshold — right inside slam range.
        /// </summary>
        private void TrySlamDoorsAhead()
        {
            // Only mid-hunt. A door bursting open is a beat that belongs to being chased or sought,
            // not to idle patrolling past a room.
            if (state != EntityState.Chase && state != EntityState.Investigate &&
                state != EntityState.Search && state != EntityState.Stalk && state != EntityState.LostTarget)
                return;

            if (doors == null || Time.time >= nextDoorScan)
            {
                doors = FindObjectsByType<LastWard.Net.NetworkedDoor>(FindObjectsInactive.Include);
                nextDoorScan = Time.time + 3f;
            }

            foreach (var door in doors)
            {
                if (door == null || door.IsOpen || door.IsLocked) continue;
                Vector3 flat = door.transform.position - transform.position;
                flat.y = 0f;
                if (flat.sqrMagnitude <= doorSlamRange * doorSlamRange)
                    door.ServerSlamOpen();
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
                // Detection runs from the first second. Gating it on CanEngage meant the meter
                // stayed pinned at zero for the whole opening window, so the Entity genuinely
                // could not notice anyone — only the CHASE should wait, not the noticing.
                // Dormant means genuinely absent: no senses at all. Without this the Entity is
                // still quietly accumulating discovery on people it is nowhere near.
                // Worked out BEFORE visibility, because visibility has to honour it: a hidden
                // player lighting it up is no longer hidden as far as its senses are concerned.
                bool beamOnEntity = TorchBeamHitsMe(pns, distance);
                bool effectivelyHidden = pns.IsHidden && !beamOnEntity;

                bool trueVisible = IsVisible(player, pns, distance, effectivelyHidden);
                // Dormancy stops it HUNTING, not seeing. Gating vision on it entirely is why it
                // stood in a doorway ignoring a player two metres away in the open.
                bool visible = !IsDormant && trueVisible;

                // Proximity sense: close enough and it knows, whatever it happens to be facing.
                // Without this the Entity is a security camera on legs — it walked past players
                // standing in the open simply because they were not inside its cone.
                // Shining your torch AT it betrays you — even from a hiding spot. A hidden player is
                // otherwise invisible to every sense; the beam is the one thing that gives them away,
                // which makes the flashlight a liability you choose to carry, not free vision.
                if (beamOnEntity)
                {
                    // It reacts: turns and comes to look, waking if it was absent. This is the fix
                    // for "I can flashlight straight onto it from under the bed and it does nothing".
                    lastKnownPos = player.position;
                    stimulusPos = player.position;
                    hasStimulus = true;
                    if (state == EntityState.Dormant || state == EntityState.Patrol ||
                        state == EntityState.Stalk || state == EntityState.Return)
                        EnterState(EntityState.Investigate);
                }

                float senseRadius = effectivelyHidden ? hiddenSenseRadius : proximitySenseRadius;
                // Crouching shrinks how close it can sense you and slows how fast it reads you.
                // Previously crouching did nothing for stealth at all, so the meter climbed while
                // the player was doing the one thing that should have been keeping them safe.
                bool crouching = pns.IsCrouching;
                if (crouching) senseRadius *= crouchSenseMultiplier;

                // Proximity sense now respects WALLS. It had none, so the Entity standing in the
                // Ward sensed players through the wall the breaker box is mounted on and the fuse
                // puzzle could not be solved at all. Through a wall it keeps only a very short
                // radius — it still knows if you are pressed against the other side of a door, but
                // it cannot feel you across a room.
                bool blocked = Physics.Linecast(
                    transform.position + Vector3.up * eyeHeight,
                    player.position + Vector3.up * 1f,
                    out var wallHit, ~0, QueryTriggerInteraction.Ignore)
                    && wallHit.transform != player && !wallHit.transform.IsChildOf(player);
                if (blocked) senseRadius = Mathf.Min(senseRadius, throughWallSenseRadius);
                bool sensed = !IsDormant && distance <= senseRadius;

                // POINT-BLANK. If it is right on top of an exposed player with no wall between, it
                // knows — full stop. It does NOT need to be facing them. This is the fix for the one
                // thing that has always read as broken: the Entity gliding up beside or behind a
                // standing player and noticing nothing until it wandered off and came back. Being
                // within arm's reach of it, out of a hiding spot, is now instant.
                bool pointBlank = !effectivelyHidden && pns.IsAlive && !blocked && distance <= pointBlankRadius;

                // Caught in the open, close, in clear line of sight: it comes for you AT ONCE —
                // but it CHASES, it does not teleport a death screen onto you. Direct contact is
                // the start of the encounter, not the end of it. The meter is for the slow
                // pressure of working and being watched; being walked in on is its own thing.
                if (pns.IsAlive && !effectivelyHidden && !jumpscareRunning
                    && (pointBlank || (trueVisible && distance <= immediateThreatRadius)))
                {
                    target = player;
                    lastKnownPos = player.position;
                    if (state != EntityState.Chase)
                    {
                        rushing = true;                 // comes in at full speed
                        appearanceEndsAt = float.MaxValue;
                        EnterState(EntityState.Chase);
                    }
                    seen = player;
                    topDiscovery = Mathf.Max(topDiscovery, pns.Discovery);
                    return true;
                }

                if (visible)
                {
                    // Closer reads faster, and a lit torch in a dark ward is a beacon. Deeper into
                    // the run it notices you roughly twice as fast.
                    float proximity = Mathf.Lerp(2f, 0.65f, Mathf.Clamp01(distance / visionRange));
                    float rate = proximity / Mathf.Max(0.1f, timeToDiscover);
                    if (pns.FlashlightOn) rate *= flashlightDetectionMultiplier;
                    if (crouching) rate *= crouchSenseMultiplier;
                    rate *= 1f + Progress;
                    current += rate * Time.deltaTime;
                }
                else if (sensed)
                {
                    // Rises faster the closer it is: at the edge it is a slow prickle, at arm's
                    // length it is nearly instant.
                    float closeness = 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, senseRadius));
                    float proximityRate = proximityFillRate * (0.4f + closeness * 1.6f) * (1f + Progress);
                    if (crouching) proximityRate *= crouchSenseMultiplier;
                    current += proximityRate * Time.deltaTime;
                }
                else
                {
                    float decay = discoveryDecayPerSecond + (pns.IsHidden ? hiddenDecayBonus : 0f);
                    current -= decay * Time.deltaTime;

                    // A torch left burning keeps winding the meter even with no line of sight, so
                    // light is a resource to spend rather than something to leave on permanently.
                    if (pns.FlashlightOn && !pns.IsHidden)
                        current += torchDrawPerSecond * (1f + Progress) * Time.deltaTime;
                }

                current = Mathf.Clamp01(current);
                pns.ServerSetDiscovery(current);

                if (!visible && !sensed) continue;
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

        /// <summary>
        /// The catch. Fired only from <see cref="TickChase"/> once the Entity has closed to
        /// <c>killRange</c>, so it is already on top of the victim — the end of a chase, never a
        /// teleported death screen.
        ///
        /// It does not celebrate and it does not rush. It takes HOLD of the victim (movement and
        /// look input suspended, their view turned onto it), plays the authored Watcher_Catch beat —
        /// hand to the face, eye contact, drawn slowly closer — and only then takes them. The horror
        /// is the intimacy and the certainty of it, so the whole thing is deliberately unhurried and
        /// ends before anything explicit; the screen goes first.
        /// </summary>
        private System.Collections.IEnumerator JumpscareRoutine(Transform victim)
        {
            jumpscareRunning = true;

            if (victim == null)
            {
                jumpscareRunning = false;
                yield break;
            }

            netState.Value = EntityState.Chase;
            GameEvents.RaiseNoiseEmitted(transform.position, 20f, NoiseSource.PuzzleInteraction);

            // A shut door between them is kicked open first — the panel actually bursts aside.
            var blockingDoor = ClosedDoorTowards(victim.position);
            bool slamFirst = blockingDoor != null;
            if (blockingDoor != null) blockingDoor.ServerSlamOpen();

            var victimObject = victim.GetComponent<NetworkObject>();
            victim.TryGetComponent<PlayerNetworkState>(out var pns);

            // Take hold. This is the difference between a catch and a scare you can jog away from:
            // for the length of the sequence the victim cannot move, and their view is turned onto
            // it so the eye contact the animation is built around actually happens.
            if (pns != null) pns.ServerSetHeld(true);

            PlayJumpscareClientRpc(victimObject != null ? victimObject.OwnerClientId : 0UL, slamFirst);
            PlayCatchClientRpc();
            if (slamFirst) yield return new WaitForSeconds(0.45f);

            // The agent is DISABLED, not merely stopped. A stopped-but-enabled NavMeshAgent still
            // writes the transform from its own internal position every frame, so it silently undid
            // every step of the movement below.
            bool hadAgent = agent != null && agent.enabled;
            if (hadAgent) agent.enabled = false;

            // Close the last step and then simply hold there, facing them, for the length of the
            // clip. No lunge — it has already won; there is nothing left to hurry.
            float elapsed = 0f;
            while (elapsed < catchSeconds)
            {
                if (victim == null) break;
                elapsed += Time.deltaTime;

                Vector3 toVictim = victim.position - transform.position;
                toVictim.y = 0f;
                float gap = toVictim.magnitude;
                if (gap > catchHoldDistance)
                {
                    float step = Mathf.Min(catchApproachSpeed * Time.deltaTime, gap - catchHoldDistance);
                    transform.position += toVictim.normalized * step;
                }
                if (toVictim.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(toVictim), Time.deltaTime * 6f);
                yield return null;
            }

            if (victim != null && pns != null && pns.IsAlive)
            {
                target = victim;
                KillTarget();
            }
            else if (pns != null)
            {
                // Died some other way mid-sequence; never leave a live player frozen.
                pns.ServerSetHeld(false);
            }

            // Put it back on the NavMesh where it finished, or it is left unable to path.
            if (hadAgent && agent != null)
            {
                agent.enabled = true;
                if (NavMesh.SamplePosition(transform.position, out var back, 5f, NavMesh.AllAreas))
                    ServerTeleport(back.position);
            }
            if (agent != null && agent.isOnNavMesh) agent.isStopped = false;
            jumpscareRunning = false;
        }

        /// <summary>
        /// True when a shut door stands between the Entity and a point — used to decide whether the
        /// kill opens with a slam. Checked against door colliders specifically rather than any
        /// geometry, so a wall does not read as a door.
        /// </summary>
        private NetworkedDoor ClosedDoorTowards(Vector3 point)
        {
            Vector3 from = transform.position + Vector3.up * eyeHeight;
            Vector3 to = point + Vector3.up * 1f;
            foreach (var hit in Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to),
                         ~0, QueryTriggerInteraction.Ignore))
            {
                // An open door's panel has swung aside, so it should not count even if the ray
                // happens to clip it edge-on.
                var door = hit.collider.GetComponentInParent<NetworkedDoor>();
                if (door != null && !door.IsOpen) return door;
            }
            return null;
        }

        /// <summary>Plays the one-shot catch animation on every peer.</summary>
        [ClientRpc]
        private void PlayCatchClientRpc()
        {
            var driver = GetComponent<EntityAnimationDriver>();
            if (driver != null) driver.PlayCatch();
        }

        [ClientRpc]
        private void PlayJumpscareClientRpc(ulong victimClientId, bool slamFirst)
        {
            // Only the victim gets the scare. Everyone else hears the Entity through the normal
            // spatial audio, which is far more unsettling than sharing the jump.
            if (NetworkManager.Singleton == null) return;
            if (NetworkManager.Singleton.LocalClientId != victimClientId) return;
            JumpscareUI.Instance?.Play(slamFirst);
            // Straight at the ears, at full volume, and a different one each death.
            GameSfx.Play2D(GameSfx.Random(GameSfx.Jumpscares), jumpscareVolume);

            // Start the held-camera timing HERE, off the same message that fires the catch
            // animation. It used to key off the replicated `held` flag, but a NetworkVariable delta
            // and a ClientRpc do not necessarily land on the same frame - the animation generally
            // arrived first, which is the delay between the arm lifting and the view following.
            var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (localPlayer != null)
            {
                var look = localPlayer.GetComponentInChildren<LastWard.Player.FirstPersonLook>();
                if (look != null) look.BeginCatch();
            }
        }

        /// <summary>Any living, unhidden player whose meter has filled — seen or not.</summary>
        private Transform FindFullyDiscoveredPlayer()
        {
            foreach (var player in players)
            {
                if (player == null) continue;
                if (!player.TryGetComponent<PlayerNetworkState>(out var pns)) continue;
                if (!pns.IsAlive || pns.IsHidden) continue;
                if (pns.Discovery >= 0.999f) return player;
            }
            return null;
        }

        /// <summary>
        /// True when a player's flashlight beam is actually landing on the Entity — on, in range,
        /// pointed within the beam cone, and with a clear-ish line to it. A blocker within ~1.6 m of
        /// the player is treated as the hiding furniture they are shining out from and does not count;
        /// a wall further off does. This is what makes shining your torch at it, from anywhere, a tell.
        /// </summary>
        private bool TorchBeamHitsMe(PlayerNetworkState pns, float distance)
        {
            if (!pns.FlashlightOn || distance > torchBetrayRange) return false;
            var pivot = pns.CameraPivot;
            if (pivot == null) return false;

            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 toEntity = eye - pivot.position;
            if (Vector3.Angle(pivot.forward, toEntity) > torchBetrayHalfAngle) return false;

            // The Entity carries NO collider (the capsule's is destroyed at build time and the
            // art pass strips them off the model), so a raycast can never report hitting it. The
            // real test is therefore "is anything solid strictly BETWEEN the torch and it" - the
            // old check asked whether the ray hit the Entity, which was never true, so a wall
            // behind it failed the test and the betrayal never fired once.
            float reach = toEntity.magnitude;
            if (Physics.Linecast(pivot.position, eye, out var h, ~0, QueryTriggerInteraction.Ignore))
            {
                bool hitEntity = h.transform == transform || h.transform.IsChildOf(transform);
                // A blocker right on top of the player is the furniture they are hiding inside and
                // shining out past; anything further along that still stops short of the Entity is
                // a real wall.
                if (!hitEntity && h.distance > 1.2f && h.distance < reach - 0.35f) return false;
            }
            return true;
        }

        private bool IsVisible(Transform player, PlayerNetworkState pns, float distance, bool hidden)
        {
            // Takes the caller's notion of hidden rather than reading pns.IsHidden directly: a
            // player betraying themselves with a torch counts as exposed, and hard-failing here was
            // why lighting it up from a wardrobe moved the meter not at all.
            if (hidden) return false;
            if (distance > visionRange) return false;

            // Compared FLAT. The old check used the full 3D angle, so a crouching player standing
            // right in front of the Entity sat at a steep downward angle that blew past the 55°
            // cone — crouching in the open made you invisible, and anyone close was often missed
            // entirely. Height is handled by the line-of-sight test below, not the cone.
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            Vector3 facing = transform.forward;
            facing.y = 0f;
            if (toPlayer.sqrMagnitude > 0.001f && facing.sqrMagnitude > 0.001f &&
                Vector3.Angle(facing, toPlayer) > visionHalfAngle)
                return false;

            // Aimed at the chest and re-aimed lower if that's blocked: crouching drops the player's
            // real height to ~1m, so a fixed 1m offset can sit inside the floor or a prop and read
            // as "hidden" while they're in plain view.
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            if (HasLineOfSight(eye, player, 1.1f)) return true;
            if (HasLineOfSight(eye, player, 0.55f)) return true;
            return HasLineOfSight(eye, player, 0.2f);
        }

        private bool HasLineOfSight(Vector3 eye, Transform player, float heightOffset)
        {
            Vector3 point = player.position + Vector3.up * heightOffset;
            if (!Physics.Linecast(eye, point, out var hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.transform == player || hit.transform.IsChildOf(player);
        }

        /// <summary>
        /// 0 at the car, 1 at the exit door. Everything that makes the Entity dangerous scales off
        /// this, so pressure climbs as the party gets deeper and solves more — rather than the whole
        /// run being played at one difficulty. Team knowledge stacks on top, so the group that reads
        /// everything is hunted harder than the group that guesses.
        /// </summary>
        private float Progress
        {
            get
            {
                float stage = 0f;
                if (ObjectiveTracker.Instance != null)
                {
                    switch (ObjectiveTracker.Instance.Stage)
                    {
                        case ObjectiveStage.Exterior: stage = 0f; break;
                        case ObjectiveStage.Lobby: stage = 0.25f; break;
                        case ObjectiveStage.OrphanWard: stage = 0.5f; break;
                        case ObjectiveStage.ServiceCorridor: stage = 0.75f; break;
                        default: stage = 1f; break;
                    }
                }
                int tier = KnowledgeService.Instance != null ? KnowledgeService.Instance.GetAggressionTier() : 0;
                return Mathf.Clamp01(stage + tier * 0.08f);
            }
        }

        // The opening lull shrinks to nothing by the Ward — it exists to keep the first puzzle calm,
        // not to protect the whole run.
        private bool CanEngage => runTime >= openingGrace * (1f - Progress);

        // --- states ---

        // --- presence ---

        /// <summary>
        /// True while any living player could actually see it right now: in range, roughly in front
        /// of them, with a clear line. Deliberately generous (wide angle, long range) because the
        /// cost of a false negative - teleporting while someone is watching - is the whole illusion.
        /// Hidden players still count; they can see out of a wardrobe perfectly well.
        /// </summary>
        private bool IsObservedByAnyPlayer()
        {
            Vector3 mid = transform.position + Vector3.up * 1f;
            foreach (var player in players)
            {
                if (player == null) continue;
                if (!player.TryGetComponent<PlayerNetworkState>(out var pns) || !pns.IsAlive) continue;

                var pivot = pns.CameraPivot;
                Vector3 eye = pivot != null ? pivot.position : player.position + Vector3.up * 1.6f;
                Vector3 fwd = pivot != null ? pivot.forward : player.forward;

                Vector3 to = mid - eye;
                if (to.magnitude > observeRange) continue;
                if (Vector3.Angle(fwd, to) > observeHalfAngle) continue;
                if (Physics.Linecast(eye, mid, out var hit, ~0, QueryTriggerInteraction.Ignore)
                    && hit.transform != transform && !hit.transform.IsChildOf(transform)) continue;
                return true;
            }
            return false;
        }

        /// <summary>Does this player have a clear line to a point? Used to stalk from cover.</summary>
        private bool PlayerHasLineTo(Transform player, Vector3 point)
        {
            if (!player.TryGetComponent<PlayerNetworkState>(out var pns)) return true;
            var pivot = pns.CameraPivot;
            Vector3 eye = pivot != null ? pivot.position : player.position + Vector3.up * 1.6f;
            Vector3 mid = point + Vector3.up * 1f;
            return !(Physics.Linecast(eye, mid, out var hit, ~0, QueryTriggerInteraction.Ignore)
                     && hit.transform != player && !hit.transform.IsChildOf(player));
        }

        /// <summary>Goes absent - but only at a moment nobody could witness it leaving.</summary>
        private bool TryVanish()
        {
            if (IsObservedByAnyPlayer()) return false;
            EnterState(EntityState.Dormant);
            return true;
        }

        /// <summary>
        /// Picks somewhere to be SEEN from: on the navmesh, at silhouette distance, with a clear
        /// line back to the player so the appearance is not wasted on a wall, and as close to their
        /// forward view as possible so it actually registers.
        /// </summary>
        private bool TryFindSightingSpot(Transform player, out Vector3 spot)
        {
            spot = Vector3.zero;
            if (!player.TryGetComponent<PlayerNetworkState>(out var pns)) return false;
            var pivot = pns.CameraPivot;
            Vector3 eye = pivot != null ? pivot.position : player.position + Vector3.up * 1.6f;
            Vector3 fwd = pivot != null ? pivot.forward : player.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();

            float best = float.MinValue;
            const int Samples = 24;
            for (int i = 0; i < Samples; i++)
            {
                float ang = (i / (float)Samples) * Mathf.PI * 2f;
                float d = Random.Range(sightingDistanceMin, sightingDistanceMax);
                Vector3 c = player.position + new Vector3(Mathf.Cos(ang) * d, 0f, Mathf.Sin(ang) * d);
                if (!NavMesh.SamplePosition(c, out var hit, 3f, NavMesh.AllAreas)) continue;

                Vector3 mid = hit.position + Vector3.up * 1f;
                if (Physics.Linecast(eye, mid, out var blocker, ~0, QueryTriggerInteraction.Ignore)
                    && blocker.transform != player && !blocker.transform.IsChildOf(player)) continue;

                Vector3 to = mid - eye;
                to.y = 0f;
                if (to.sqrMagnitude < 0.01f) continue;
                float facing = Vector3.Dot(fwd, to.normalized);   // 1 = dead ahead
                if (facing > best) { best = facing; spot = hit.position; }
            }
            return best > -0.1f;   // anything not squarely behind them is worth appearing at
        }

        /// <summary>
        /// Stages an appearance: it is simply THERE, at distance, already facing the player - never
        /// witnessed walking into view. If it cannot move unseen right now it waits rather than
        /// forcing it, because being caught arriving is what turns dread back into a man in a suit.
        /// </summary>
        private void TryBeginAppearance()
        {
            if (!CanEngage) { ScheduleNextAppearance(); return; }
            var player = NearestPlayer();
            if (player == null) { ScheduleNextAppearance(); return; }
            if (IsObservedByAnyPlayer()) return;                 // hold; retry next frame

            if (!TryFindSightingSpot(player, out Vector3 spot))
            {
                nextAppearance = Time.time + 3f;                 // nowhere good; try again shortly
                return;
            }

            target = player;
            lastKnownPos = player.position;
            Vector3 face = player.position - spot;
            face.y = 0f;
            ServerTeleport(spot, face.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(face) : transform.rotation);

            // From the Service Corridor on, this is its territory: half its appearances stop being
            // a look-and-leave and become a hunt. It arrives in view, then moves into cover and
            // shadows you — glimpsed once, then only heard, which is far worse than either alone.
            bool inItsTerritory = ObjectiveTracker.Instance != null &&
                                  (int)ObjectiveTracker.Instance.Stage >= (int)ObjectiveStage.ServiceCorridor;
            if (inItsTerritory && Random.value < 0.5f)
            {
                appearanceEndsAt = Time.time + appearanceHoldMax * 2f;
                EnterState(EntityState.Stalk);
                return;
            }

            appearanceEndsAt = Time.time + appearanceHoldMax;
            EnterState(EntityState.Stare);
        }

        private void TickPatrol(bool sees)
        {
            // Sweeps its gaze while walking. The agent otherwise faces exactly where it is going,
            // which made "stand still off its route" a perfect, permanent hiding strategy.
            SweepGaze();

            if (sees) { EnterState(EntityState.Chase); return; }
            if (hasStimulus) { EnterState(EntityState.Investigate); return; }
            if (waypoints == null || waypoints.Length == 0) return;
            if (!HasArrived()) return;

            // Sometimes a patrol leg turns into circling whoever is closest instead of carrying on
            // to the next point — the difference between a guard on a route and something hunting.
            // Circling instead of patrolling becomes the norm later on — by the Exit it does it
            // most of the time, which is what turns "roaming" into "hunting".
            if (players.Count > 0 && CanEngage && Random.value < Mathf.Lerp(stalkChance, 0.85f, Progress))
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

            // Being hunted IS terror. Hold the target's meter high for as long as the chase lasts so
            // the heart pounds and the breathing goes ragged the instant it commits — even a chase
            // that started from being walked in on, where the meter had no time to fill on its own.
            // Kept just under full so it never trips the "fully discovered" re-commit while it is
            // already the one being chased.
            if (target.TryGetComponent<PlayerNetworkState>(out var chased) && chased.IsAlive
                && chased.Discovery < chaseFearFloor)
                chased.ServerSetDiscovery(chaseFearFloor);

            float distance = Vector3.Distance(transform.position, target.position);
            // The jumpscare is the moment it catches you at the end of a chase — the only way to
            // die. Everything else is pressure.
            if (distance <= killRange && !jumpscareRunning)
            {
                StartCoroutine(JumpscareRoutine(target));
                return;
            }

            // Break off mid-chase and vanish. Only once it's been chasing a while AND isn't right
            // on top of the player — being nearly caught should always stay committed, otherwise
            // escapes stop feeling earned. The commit time is re-rolled per chase, so outrunning it
            // once teaches the player nothing about the next time.
            if (chaseElapsed >= chaseCommit * (1f + Progress * 1.5f) && distance > commitWithinDistance)
            {
                // Give up the meter as it gives up the chase. A chase left the target's meter pinned
                // near full; without this drop it would be instantly re-flagged as fully discovered
                // and dragged straight back into another chase, so escaping would be impossible.
                if (chased != null && chased.IsAlive) chased.ServerSetDiscovery(chaseBreakoffDiscovery);
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
            SweepGaze();

            if (engages) { EnterState(EntityState.Chase); return; }
            if (hasStimulus) { EnterState(EntityState.Investigate); return; }
            if (stateTimer >= stalkDuration) { EnterState(EntityState.Dormant); return; }
            if (!HasArrived()) return;

            // A ring around the nearest player rather than a point on top of them.
            var nearest = NearestPlayer();
            if (nearest == null) { EnterState(EntityState.Dormant); return; }

            // Shadow them from BEHIND COVER. The old ring put it in open view half the time, and
            // stalking you can watch is just a man walking in circles - audible and unseen is the
            // whole point. Take the first candidate the player has no line to.
            Vector3 chosen = Vector3.zero;
            bool found = false;
            for (int i = 0; i < 10; i++)
            {
                Vector2 o = Random.insideUnitCircle.normalized * Random.Range(stalkRadiusMin, stalkRadiusMax);
                Vector3 ring = nearest.position + new Vector3(o.x, 0f, o.y);
                if (!NavMesh.SamplePosition(ring, out var hit, 3f, NavMesh.AllAreas)) continue;
                chosen = hit.position;
                found = true;
                if (!PlayerHasLineTo(nearest, hit.position)) break;   // occluded: ideal
            }
            if (found) agent.SetDestination(chosen);
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
            // While it stands there it is a tripwire: break the distance it is guarding and it
            // comes at once, at full speed, instead of waiting out its timer.
            if (target != null)
            {
                float gap = Vector3.Distance(transform.position, target.position);
                if (gap < stareTripDistance)
                {
                    rushing = true;
                    EnterState(EntityState.Chase);
                    return;
                }
            }

            if (stateTimer < stareDuration * Random.Range(1f, 2.5f)) return;

            if (target != null && Random.value < Mathf.Lerp(rushAfterStareChance, 0.9f, Progress))
            {
                rushing = true;
                EnterState(EntityState.Chase);
                return;
            }

            // Otherwise it is simply gone the next time you look. It will NOT walk away: it holds
            // position until the instant nobody can see it, then vanishes. Letting players watch it
            // leave is how they learned where it went and stopped being afraid of it.
            if (TryVanish()) return;
            // Held the whole window and they never looked away. It does not shuffle off under
            // observation - refusing to break eye contact is how you get it to come.
            if (Time.time >= appearanceEndsAt)
            {
                rushing = true;
                EnterState(EntityState.Chase);
            }
        }

        public bool IsDormant => state == EntityState.Dormant;

        /// <summary>
        /// Waits, out of the way, until it is due to appear. How long it waits shrinks sharply with
        /// progress: near the exit it is barely gone at all, while in the Lobby it is mostly a
        /// rumour.
        /// </summary>
        private void TickDormant()
        {
            // Seeing someone ends dormancy on the spot. "Absent" is about where it spends its time,
            // not about being blind — an Entity that looks straight at a player and keeps walking is
            // not frightening, it is broken.
            foreach (var player in players)
            {
                if (player == null) continue;
                if (!player.TryGetComponent<PlayerNetworkState>(out var pns)) continue;
                if (!pns.IsAlive || pns.IsHidden) continue;

                float d = Vector3.Distance(transform.position, player.position);
                if (d > visionRange || !IsVisible(player, pns, d, pns.IsHidden)) continue;

                target = player;
                lastKnownPos = player.position;
                appearanceEndsAt = Time.time + appearanceSeconds;
                EnterState(EntityState.Chase);
                return;
            }

            // ABSENT MEANS ABSENT. It does not walk anywhere. Strolling a waypoint route in open
            // view is precisely what made it read as a guard you could watch, learn and route
            // around - it stands still, and relocates only by teleporting while nobody is looking.
            if (agent.isOnNavMesh && !agent.isStopped) agent.isStopped = true;

            // Somebody is wandering into where it is parked - move, before they arrive.
            var nearestToPark = NearestPlayer();
            if (nearestToPark != null &&
                Vector3.Distance(transform.position, nearestToPark.position) < absentMinDistance)
                parkedWhileAbsent = false;

            if (!parkedWhileAbsent && waypoints != null && waypoints.Length > 0 && !IsObservedByAnyPlayer())
            {
                int far = FarthestWaypointFromPlayers();
                if (far >= 0 && far < waypoints.Length && waypoints[far] != null)
                {
                    waypointIndex = far;
                    ServerTeleport(waypoints[far].position);
                    parkedWhileAbsent = true;
                }
            }

            if (Time.time < nextAppearance) return;
            TryBeginAppearance();
        }

        // The pre-presence appearance flow, superseded by TryBeginAppearance. Left unreferenced so
        // the old behaviour stays easy to diff against; nothing calls it.
        private void LegacyAppearance()
        {

            // Its territory is the Service Corridor. Anywhere earlier it only makes brief
            // appearances; from the corridor on it stays and hunts properly.
            bool inItsTerritory = ObjectiveTracker.Instance != null &&
                                  (int)ObjectiveTracker.Instance.Stage >= (int)ObjectiveStage.ServiceCorridor;

            EnterState(inItsTerritory ? EntityState.Stalk : EntityState.Investigate);
            appearanceEndsAt = Time.time + (inItsTerritory ? float.MaxValue : appearanceSeconds);

            var nearest = NearestPlayer();
            if (nearest != null)
            {
                // Arrives at a DISTANCE — seen across a room, not stepped out of a cupboard beside
                // you. Being glimpsed far off is the whole point of an appearance.
                stimulusPos = nearest.position;
                hasStimulus = true;
                Vector2 ring = Random.insideUnitCircle.normalized * appearanceDistance;
                Vector3 spot = nearest.position + new Vector3(ring.x, 0f, ring.y);
                if (NavMesh.SamplePosition(spot, out var hit, 8f, NavMesh.AllAreas))
                    ServerTeleport(hit.position);
            }
        }

        private void ScheduleNextAppearance()
        {
            float wait = Mathf.Lerp(dormantSecondsEarly, dormantSecondsLate, Progress);
            nextAppearance = Time.time + Random.Range(wait * 0.6f, wait * 1.4f);
        }

        /// <summary>
        /// Rotates the agent's facing off its direction of travel in a slow sine sweep, so its cone
        /// covers the sides of a corridor as it moves. Applied to the transform rather than the
        /// agent's own rotation, and only while it is not chasing — during a chase it should look
        /// exactly where it is running.
        /// </summary>
        private void SweepGaze()
        {
            if (agent == null || !agent.isOnNavMesh) return;

            Vector3 travel = agent.velocity;
            travel.y = 0f;
            if (travel.sqrMagnitude < 0.05f) return;

            float sweep = Mathf.Sin(Time.time * gazeSweepSpeed + gazeSweepOffset) * gazeSweepAngle;
            Quaternion look = Quaternion.LookRotation(travel.normalized) * Quaternion.Euler(0f, sweep, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 3f);
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
            // Vanishes for a much shorter time late on — near the exit it is barely gone before
            // it is back.
            if (stateTimer >= withdrawDuration * (1f - Progress * 0.6f)) { EnterState(EntityState.Dormant); return; }
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
                EnterState(EntityState.Dormant);
        }

        // --- transitions ---

        private void EnterState(EntityState next)
        {
            state = next;
            stateTimer = 0f;
            netState.Value = next;

            // Every transition re-arms absence. Entering Dormant MUST roll a fresh wait: the stale
            // timer left over from the previous appearance is already in the past, so it re-appeared
            // the instant it went absent - a large part of why it felt permanently present.
            parkedWhileAbsent = false;
            if (next == EntityState.Dormant) ScheduleNextAppearance();

            // Nothing below may touch a disabled or unplaced agent.
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

            // Stare and Dormant both plant it; everything else must clear the flag or the agent
            // stays frozen for the rest of the run. Dormant is stationary because absence means it
            // is not walking a route anywhere at all.
            if (agent.isOnNavMesh)
                agent.isStopped = next == EntityState.Stare || next == EntityState.Dormant;

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
                case EntityState.Dormant:
                    agent.speed = Jitter(patrolSpeed * 0.9f);
                    appearanceEndsAt = float.MaxValue;   // dormancy has no expiry of its own
                    if (waypoints != null && waypoints.Length > 0)
                    {
                        waypointIndex = FarthestWaypointFromPlayers();
                        agent.SetDestination(waypoints[waypointIndex].position);
                    }
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

            // Noise raises the meter of whoever made it, even through walls and with no line of
            // sight. Sprinting, slamming doors and working puzzles are choices with a cost now,
            // rather than free actions the Entity only reacts to by walking somewhere.
            // Noise is the loud, legible cost — a single container is a large, obvious jump, which
            // is what makes searching a decision rather than a formality. Roughly: one container
            // ~40%, plus light ~70%, plus one more noise ~100%.
            float bump = source switch
            {
                NoiseSource.Sprint => 0.22f,
                NoiseSource.Door => 0.22f,
                NoiseSource.PuzzleInteraction => 0.3f,
                NoiseSource.Footstep => 0f,     // ordinary walking must cost nothing
                _ => 0.05f,
            };
            bump *= 1f + Progress * 0.5f;

            foreach (var player in players)
            {
                if (player == null) continue;
                if (Vector3.Distance(player.position, position) > 2.5f) continue;
                if (!player.TryGetComponent<PlayerNetworkState>(out var pns)) continue;
                if (!pns.IsAlive) continue;
                // Hiding muffles you, it doesn't silence you.
                pns.ServerSetDiscovery(pns.Discovery + (pns.IsHidden ? bump * 0.25f : bump));
            }

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
                victim.ServerSetHeld(false);   // released as they die, never left frozen
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
