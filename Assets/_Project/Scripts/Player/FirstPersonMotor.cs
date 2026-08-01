using UnityEngine;

namespace LastWard.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonMotor : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [Tooltip("Replicates the crouch state so the Entity's server-side senses can read it.")]
        [SerializeField] private LastWard.Net.PlayerNetworkState netState;

        [Header("Speed")]
        [SerializeField] private float walkSpeed = 3.2f;
        [SerializeField] private float sprintSpeed = 5.2f;
        [SerializeField] private float crouchSpeed = 1.8f;

        [Header("Crouch")]
        [SerializeField] private float standHeight = 1.8f;
        [SerializeField] private float crouchHeight = 1.0f;
        [SerializeField] private float crouchTransitionSpeed = 8f;
        [Tooltip("Camera rig, dropped along with the collider so crouching actually changes what " +
            "you can see — looking under a bed or into a low cupboard is the point of it.")]
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float standEyeHeight = 1.6f;
        [SerializeField] private float crouchEyeHeight = 0.85f;

        [SerializeField] private float gravity = -18f;
        [Tooltip("How fast horizontal speed ramps in/out. Lower = floatier.")]
        [SerializeField] private float acceleration = 28f;

        [Header("Climbing")]
        [Tooltip("Speed along the look direction while inside a ClimbVolume. Deliberately slower " +
            "than a walk: a ladder is a commitment, and being partway up one with something below " +
            "you should be the worst place to be.")]
        [SerializeField] private float climbSpeed = 2.2f;

        private CharacterController controller;
        private float verticalVelocity;
        private float currentHeight;
        private Vector3 horizontalVelocity;

        // Tracked as a list, not a count: the climb steering needs the volume's actual extents to
        // pen the climber to the ladder. Overlapping volumes are normal where a ladder top meets a
        // hatch, so a single reference would clear on the first exit while still inside the second.
        private readonly System.Collections.Generic.List<ClimbVolume> climbing =
            new System.Collections.Generic.List<ClimbVolume>();
        private int climbVolumes => climbing.Count;
        private int lowVolumes;

        /// <summary>
        /// Held down by a scripted action rather than by the player — striking a match, for one.
        /// Separate from the CrouchVolume count because it is set and cleared by a single coroutine
        /// that must not have its state clobbered by walking in and out of a low space mid-action.
        /// </summary>
        public bool ScriptedCrouch { get; set; }

        /// <summary>
        /// Fraction of speed surrendered to whatever you are dragging, 0..1. Half alone on the crate,
        /// a quarter each with two of you — the load is shared, so a second pair of hands is a real
        /// reason to wait for one rather than a courtesy.
        /// </summary>
        public float PushLoad { get; set; }

        /// <summary>
        /// Locked to something you are shoving. Your input drives that object and it drags you, so
        /// self-walking is switched off — letting both move independently meant the player and the
        /// crate drifted apart on the first frame of latency and it slid out from under them.
        /// Gravity still applies; only horizontal steering is surrendered.
        /// </summary>
        public bool PushAttached { get; set; }

        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsClimbing => climbVolumes > 0;
        /// <summary>Actually travelling, not merely holding a key against a wall.</summary>
        public bool IsMoving => horizontalVelocity.sqrMagnitude > 0.04f;

        private void OnTriggerEnter(Collider other)
        {
            var climb = other.GetComponent<ClimbVolume>();
            if (climb != null && !climbing.Contains(climb)) climbing.Add(climb);
            if (other.GetComponent<CrouchVolume>() != null) lowVolumes++;
        }

        private void OnTriggerExit(Collider other)
        {
            var climb = other.GetComponent<ClimbVolume>();
            if (climb != null) climbing.Remove(climb);
            if (other.GetComponent<CrouchVolume>() != null) lowVolumes = Mathf.Max(0, lowVolumes - 1);
        }

        /// <summary>
        /// Holds the climber inside the ladder's own footprint on its penned axes.
        ///
        /// Climb steering turns gravity off, which quietly turns a ladder into a flying machine: you
        /// could strafe off the side of it at height and walk onto anything nearby. On the stair
        /// shaft that meant stepping across onto the rubble pile and reaching the good treads without
        /// ever opening the duct — the whole puzzle skipped. The free axis is the one you dismount
        /// along, so the ladder still lets you off where it is supposed to.
        /// </summary>
        private void PenToLadder(ClimbVolume volume)
        {
            var b = volume.Bounds;
            Vector3 p = transform.position;
            // Half the radius, not the whole one. At full radius a 0.9m-wide volume left a 0.2m
            // window, so the clamp was hauling the body to the centre every frame while the
            // CharacterController pushed it back off the wall — the two fought at frame rate and the
            // camera shook itself apart at the top of the ladder.
            float inset = controller.radius * 0.5f;
            float step = Mathf.Max(0.02f, climbSpeed * Time.deltaTime * 2f);
            if (volume.PenX) p.x = Ease(p.x, PenAxis(p.x, b.min.x, b.max.x, inset), step);
            if (volume.PenZ) p.z = Ease(p.z, PenAxis(p.z, b.min.z, b.max.z, inset), step);
            transform.position = p;
        }

        private static float PenAxis(float value, float min, float max, float inset)
        {
            if (max - min <= inset * 2f) return (min + max) * 0.5f;
            return Mathf.Clamp(value, min + inset, max - inset);
        }

        // Eased rather than assigned. Writing transform.position skips collision resolution, so a
        // hard correction can wedge the capsule into geometry or oscillate against it; moving toward
        // the target at a bounded rate absorbs that instead of amplifying it.
        private static float Ease(float from, float to, float maxStep) =>
            Mathf.Approximately(from, to) ? from : Mathf.MoveTowards(from, to, maxStep);

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            currentHeight = standHeight;
        }

        private void Update()
        {
            // Caught. No walking out of the encounter.
            if (netState != null && netState.IsHeld) return;

            // A low space holds the crouch down for you — see CrouchVolume for why this isn't optional.
            IsCrouching = input.CrouchHeld || lowVolumes > 0 || ScriptedCrouch;
            if (netState != null) netState.SetCrouching(IsCrouching);
            IsSprinting = input.SprintHeld && !IsCrouching;

            float targetHeight = IsCrouching ? crouchHeight : standHeight;
            currentHeight = Mathf.Lerp(currentHeight, targetHeight, Time.deltaTime * crouchTransitionSpeed);
            controller.height = currentHeight;
            controller.center = new Vector3(0f, currentHeight * 0.5f, 0f);

            if (cameraPivot != null)
            {
                float targetEye = IsCrouching ? crouchEyeHeight : standEyeHeight;
                var local = cameraPivot.localPosition;
                local.y = Mathf.Lerp(local.y, targetEye, Time.deltaTime * crouchTransitionSpeed);
                cameraPivot.localPosition = local;
            }

            float speed = IsCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);
            // Shoving something heavy. Applied after the gait choice so sprinting with a crate is
            // still faster than walking with one, just not by much.
            speed *= Mathf.Clamp01(1f - PushLoad);
            Vector2 moveInput = Vector2.ClampMagnitude(input.Move, 1f);

            // --- on a ladder ---
            // Steering follows where you are LOOKING, not where the body faces: look up and hold W to
            // go up, look down and hold W to come down, and — the reason it has to work this way —
            // look level at the duct mouth at the top and hold W to crawl straight into it. Binding
            // up to W outright would leave no key to get off the ladder with, and the whole point of
            // this ladder is that it ends at an opening rather than at a floor.
            if (climbVolumes > 0 && cameraPivot != null)
            {
                horizontalVelocity = Vector3.zero;
                verticalVelocity = 0f;
                Vector3 climb = cameraPivot.forward * moveInput.y + cameraPivot.right * moveInput.x;
                controller.Move(Vector3.ClampMagnitude(climb, 1f) * climbSpeed * Time.deltaTime);
                PenToLadder(climbing[0]);
                return;
            }

            // Holding the crate: the input has already been spent moving it, so none of it moves you.
            if (PushAttached) moveInput = Vector2.zero;

            Vector3 targetVelocity = (transform.right * moveInput.x + transform.forward * moveInput.y) * speed;

            // Ramp toward the target instead of snapping — instant start/stop is what reads as
            // "stiff" in first person.
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, acceleration * Time.deltaTime);

            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -1f;
            verticalVelocity += gravity * Time.deltaTime;

            Vector3 move = horizontalVelocity;
            move.y = verticalVelocity;
            controller.Move(move * Time.deltaTime);
        }
    }
}
