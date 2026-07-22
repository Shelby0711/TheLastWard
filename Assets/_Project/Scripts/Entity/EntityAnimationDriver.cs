using UnityEngine;

namespace LastWard.Entity
{
    /// <summary>
    /// Matches the walk clip's playback rate to how fast the Entity is actually travelling. Without
    /// this the clip runs at a constant rate no matter the speed, so the feet slide across the floor
    /// — the "gliding" look. Since the Entity's speed varies per state (and is jittered on every
    /// state entry), a single fixed playback rate can never line up.
    ///
    /// Speed is measured from the transform rather than the NavMeshAgent because clients don't run
    /// the agent at all — they only receive positions through the server's NetworkTransform, so
    /// agent.velocity is zero everywhere except the host.
    /// </summary>
    public class EntityAnimationDriver : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [Tooltip("Ground speed the clip looks correct at. Lower it if the feet still slide forward, " +
            "raise it if they scrabble.")]
        [SerializeField] private float clipAuthoredSpeed = 1.35f;
        [Tooltip("Playback floor, so a stationary Entity still breathes instead of freezing solid.")]
        [SerializeField] private float minPlaybackSpeed = 0.25f;
        [SerializeField] private float maxPlaybackSpeed = 2.5f;
        [Tooltip("Smoothing on the measured speed — raw frame deltas are noisy over the network.")]
        [SerializeField] private float smoothing = 8f;

        private Vector3 lastPosition;
        private float smoothedSpeed;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            lastPosition = transform.position;
        }

        private void Update()
        {
            if (animator == null || Time.deltaTime <= 0f) return;

            Vector3 delta = transform.position - lastPosition;
            delta.y = 0f;   // falling or stepping shouldn't speed up the walk
            lastPosition = transform.position;

            float speed = delta.magnitude / Time.deltaTime;
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, Time.deltaTime * smoothing);

            animator.speed = Mathf.Clamp(
                smoothedSpeed / Mathf.Max(0.01f, clipAuthoredSpeed),
                minPlaybackSpeed, maxPlaybackSpeed);
        }
    }
}
