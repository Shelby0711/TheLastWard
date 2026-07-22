using UnityEngine;

namespace LastWard.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonMotor : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;

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

        private CharacterController controller;
        private float verticalVelocity;
        private float currentHeight;
        private Vector3 horizontalVelocity;

        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            currentHeight = standHeight;
        }

        private void Update()
        {
            IsCrouching = input.CrouchHeld;
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
            Vector2 moveInput = Vector2.ClampMagnitude(input.Move, 1f);
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
