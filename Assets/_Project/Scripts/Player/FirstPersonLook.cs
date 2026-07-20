using UnityEngine;

namespace LastWard.Player
{
    public class FirstPersonLook : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float sensitivity = 1f;
        [Tooltip("Higher = snappier. Low values feel floaty/laggy.")]
        [SerializeField] private float smoothing = 22f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;

        private float yaw;
        private float pitch;
        private Vector2 smoothedLook;

        private void Start()
        {
            yaw = transform.eulerAngles.y;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // LateUpdate so the camera resolves after movement has already run this frame —
        // updating both in Update leaves the view a frame behind the body and reads as jitter.
        private void LateUpdate()
        {
            Vector2 look = input.Look * sensitivity;

            // Framerate-independent exponential smoothing: takes the edge off raw mouse deltas
            // without adding the input lag a plain Lerp-per-frame would.
            float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            smoothedLook = Vector2.Lerp(smoothedLook, look, t);

            yaw += smoothedLook.x;
            pitch = Mathf.Clamp(pitch - smoothedLook.y, minPitch, maxPitch);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
