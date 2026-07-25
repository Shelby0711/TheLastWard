using LastWard.Core;
using UnityEngine;

namespace LastWard.Entity
{
    /// <summary>
    /// Drives the Watcher's Animator (a Locomotion blend tree: Idle=0 → Walk=1 → Run=2, plus a
    /// one-shot Catch). It sets the "Locomotion" float from the Entity's real travel speed and
    /// current state — standing still blends to Idle, moving to the patrol Walk, and a
    /// <see cref="EntityState.Chase"/> forces the Run end of the tree — and it playback-matches the
    /// moving clips to travel speed so the feet don't slide, while leaving the Idle and Catch clips
    /// at their authored rate.
    ///
    /// Speed is measured from the transform rather than the NavMeshAgent because clients don't run
    /// the agent at all — they only receive positions through the server's NetworkTransform, so
    /// agent.velocity is zero everywhere except the host.
    /// </summary>
    public class EntityAnimationDriver : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [Tooltip("Ground speed the WALK clip looks correct at, and the speed at which Locomotion " +
            "reaches 1 (full walk). Lower it if the feet slide forward, raise it if they scrabble.")]
        [SerializeField] private float clipAuthoredSpeed = 1.35f;
        [Tooltip("Playback floor, so a moving Entity never freezes between strides.")]
        [SerializeField] private float minPlaybackSpeed = 0.5f;
        [Tooltip("Deliberately low. Fully speed-matching the chase would spin the legs into a frantic " +
            "sprint, which is the opposite of the brief — the body is meant to outrun the legs so it " +
            "reads as gliding rather than running. Capping playback IS the effect.")]
        [SerializeField] private float maxPlaybackSpeed = 1.3f;
        [Tooltip("Smoothing on measured speed and the locomotion blend — raw frame deltas are noisy " +
            "over the network, and a hard snap between idle/walk/run reads as a pop.")]
        [SerializeField] private float smoothing = 8f;
        [Tooltip("Seconds the one-shot Catch clip runs; playback is held at 1x for its duration so " +
            "the intimate finish plays at its authored pace regardless of movement.")]
        [SerializeField] private float catchSeconds = 7f;

        [Header("Stutter")]
        [Tooltip("Renders the Entity at this framerate during its tense, visible moments while the " +
            "body still moves through space at full rate. The mismatch - a form sliding smoothly " +
            "but twitching between held poses - is deeply wrong to watch and costs nothing. The FF " +
            "clips are authored at 24fps so this divides cleanly.")]
        [SerializeField] private float stutterFps = 8f;
        [Tooltip("Auto-stutter during Chase/Stare. OFF by default: the ground-floor Receptionist is " +
            "meant to read as smooth and composed. The twitch belongs to the Manager, which turns it " +
            "on permanently via SetForceStutter — so the two entities move in visibly different ways.")]
        [SerializeField] private bool stutterInTenseStates = false;

        private static readonly int LocomotionParam = Animator.StringToHash("Locomotion");
        private static readonly int CatchParam = Animator.StringToHash("Catch");

        private Vector3 lastPosition;
        private float smoothedSpeed;
        private float locomotion;
        private bool isChasing;
        private EntityState state = EntityState.Patrol;
        private float catchUntil = -1f;
        private float stutterAccum;
        private bool forceStutter;   // for entities that should ALWAYS twitch (the Manager)
        private bool hasLocomotion, hasCatch;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            lastPosition = transform.position;
            // The Manager runs a simpler controller with neither parameter. Setting a parameter that
            // does not exist spams warnings every frame, so check once and guard.
            if (animator != null)
                foreach (var p in animator.parameters)
                {
                    if (p.nameHash == LocomotionParam) hasLocomotion = true;
                    else if (p.nameHash == CatchParam) hasCatch = true;
                }
        }

        private void OnEnable() => GameEvents.OnEntityStateChanged += OnStateChanged;

        private void OnDisable()
        {
            GameEvents.OnEntityStateChanged -= OnStateChanged;
            // Never leave the Animator manually-disabled behind us, or it freezes on despawn.
            if (animator != null) animator.enabled = true;
        }

        private void OnStateChanged(EntityState next)
        {
            state = next;
            isChasing = next == EntityState.Chase;
        }

        /// <summary>Forces the stutter on regardless of state, for an entity built around it.</summary>
        public void SetForceStutter(bool value) => forceStutter = value;

        /// <summary>Fires the one-shot catch animation. Called when the jumpscare reaches contact.</summary>
        public void PlayCatch()
        {
            if (animator == null || !hasCatch) return;
            animator.SetTrigger(CatchParam);
            catchUntil = Time.time + catchSeconds;
        }

        private void Update()
        {
            if (animator == null || Time.deltaTime <= 0f) return;

            Vector3 delta = transform.position - lastPosition;
            delta.y = 0f;   // falling or stepping shouldn't register as travel
            lastPosition = transform.position;

            float speed = delta.magnitude / Time.deltaTime;
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, Time.deltaTime * smoothing);

            // Chase pins the Run end of the tree; otherwise the walk fills in with speed, easing to
            // Idle when it stops. Lerped so idle/walk/run cross-fade instead of snapping.
            float targetLoco = isChasing ? 2f : Mathf.Clamp01(smoothedSpeed / Mathf.Max(0.01f, clipAuthoredSpeed));
            locomotion = Mathf.Lerp(locomotion, targetLoco, Time.deltaTime * smoothing);
            if (hasLocomotion) animator.SetFloat(LocomotionParam, locomotion);

            // Playback rate: authored pace during the catch, otherwise matched to travel while
            // moving and eased back to 1x at rest so the idle breathing is not dragged to a crawl.
            float playback;
            if (Time.time < catchUntil)
            {
                playback = 1f;
            }
            else
            {
                float moveMatch = Mathf.Clamp(smoothedSpeed / Mathf.Max(0.01f, clipAuthoredSpeed),
                    minPlaybackSpeed, maxPlaybackSpeed);
                playback = Mathf.Lerp(1f, moveMatch, Mathf.Clamp01(locomotion));
            }

            // Never stutter through the catch - the intimate beat plays clean.
            bool stutter = Time.time >= catchUntil &&
                (forceStutter || (stutterInTenseStates &&
                    (state == EntityState.Chase || state == EntityState.Stare)));

            if (stutter)
            {
                // Take the Animator off the automatic clock and hand-step it in fixed chunks, so it
                // renders at stutterFps while everything else - position, the world - runs at full
                // rate. Playback is applied through the chunk SIZE, so animator.speed stays 1 and
                // nothing scales it twice.
                animator.speed = 1f;
                if (animator.enabled) { animator.enabled = false; stutterAccum = 0f; }

                stutterAccum += Time.deltaTime;
                float renderStep = 1f / Mathf.Max(1f, stutterFps);
                int guard = 0;
                while (stutterAccum >= renderStep && guard++ < 4)
                {
                    animator.Update(renderStep * playback);
                    stutterAccum -= renderStep;
                }
            }
            else
            {
                if (!animator.enabled) animator.enabled = true;   // back to smooth 60fps
                animator.speed = playback;
            }
        }
    }
}
