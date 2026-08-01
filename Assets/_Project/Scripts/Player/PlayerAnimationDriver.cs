using LastWard.Net;
using UnityEngine;

namespace LastWard.Player
{
    /// <summary>
    /// Drives a player character's Animator from replicated state.
    ///
    /// Deliberately reads <b>position delta</b> rather than the local motor: the motor only exists on
    /// the owner, and the bodies that matter are everyone else's. Measuring travel from the transform
    /// means a remote player animates correctly from nothing but the NetworkTransform, with no extra
    /// replication at all.
    ///
    /// This matters more than it used to. With the torch on a battery, a teammate is often the only
    /// thing you can see, and a T-posing silhouette sliding down a corridor is worse than no body —
    /// it reads as a bug at exactly the moment you want it to read as a person.
    /// </summary>
    public class PlayerAnimationDriver : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerNetworkState state;

        [Tooltip("Ground speed the walk clip is authored at — the point where Speed reaches 1.")]
        [SerializeField] private float walkSpeed = 2.2f;
        [Tooltip("Speed at which the blend reaches the run end of the tree.")]
        [SerializeField] private float runSpeed = 4.6f;
        [Tooltip("Smoothing on the blend. Raw frame deltas are noisy over the network.")]
        [SerializeField] private float smoothing = 9f;

        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int CrouchParam = Animator.StringToHash("Crouching");
        private static readonly int EmoteParam = Animator.StringToHash("Emote");

        private Vector3 lastPosition;
        private float blend;
        private bool hasSpeed, hasCrouch, hasEmote;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (state == null) state = GetComponentInParent<PlayerNetworkState>();
            lastPosition = transform.position;

            // Setting a parameter a controller does not have spams a warning every frame, and the
            // three body variants do not all necessarily have the same clips available.
            if (animator == null) return;
            foreach (var p in animator.parameters)
            {
                if (p.nameHash == SpeedParam) hasSpeed = true;
                else if (p.nameHash == CrouchParam) hasCrouch = true;
                else if (p.nameHash == EmoteParam) hasEmote = true;
            }
        }

        private void Update()
        {
            if (animator == null || Time.deltaTime <= 0f) return;

            Vector3 delta = transform.position - lastPosition;
            delta.y = 0f;                       // stairs and falling are not walking
            lastPosition = transform.position;

            float speed = delta.magnitude / Time.deltaTime;
            // 0 = idle, 1 = walk, 2 = run, matching the blend tree's thresholds.
            float target = speed <= 0.05f ? 0f
                : speed < walkSpeed ? Mathf.InverseLerp(0f, walkSpeed, speed)
                : 1f + Mathf.InverseLerp(walkSpeed, runSpeed, speed);

            blend = Mathf.Lerp(blend, Mathf.Clamp(target, 0f, 2f), Time.deltaTime * smoothing);
            if (hasSpeed) animator.SetFloat(SpeedParam, blend);
            if (hasCrouch && state != null) animator.SetBool(CrouchParam, state.IsCrouching);
        }

        /// <summary>Plays the emote currently set on the player. Called from EmoteController.</summary>
        public void PlayEmote(int emoteIndex)
        {
            if (animator == null || !hasEmote) return;
            animator.SetInteger("EmoteId", emoteIndex);
            animator.SetTrigger(EmoteParam);
        }
    }
}
