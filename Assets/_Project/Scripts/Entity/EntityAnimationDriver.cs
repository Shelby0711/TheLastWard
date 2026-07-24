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

        private static readonly int LocomotionParam = Animator.StringToHash("Locomotion");
        private static readonly int CatchParam = Animator.StringToHash("Catch");

        private Vector3 lastPosition;
        private float smoothedSpeed;
        private float locomotion;
        private bool isChasing;
        private float catchUntil = -1f;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            lastPosition = transform.position;
        }

        private void OnEnable() => GameEvents.OnEntityStateChanged += OnStateChanged;
        private void OnDisable() => GameEvents.OnEntityStateChanged -= OnStateChanged;

        private void OnStateChanged(EntityState next) => isChasing = next == EntityState.Chase;

        /// <summary>Fires the one-shot catch animation. Called when the jumpscare reaches contact.</summary>
        public void PlayCatch()
        {
            if (animator == null) return;
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
            animator.SetFloat(LocomotionParam, locomotion);

            // Hold 1x during the catch (authored pace); otherwise match travel while moving and fall
            // back to 1x when idle so the idle breathing isn't dragged to a crawl.
            if (Time.time < catchUntil)
            {
                animator.speed = 1f;
                return;
            }
            float moveMatch = Mathf.Clamp(smoothedSpeed / Mathf.Max(0.01f, clipAuthoredSpeed),
                minPlaybackSpeed, maxPlaybackSpeed);
            animator.speed = Mathf.Lerp(1f, moveMatch, Mathf.Clamp01(locomotion));
        }
    }
}
