using LastWard.UI;
using UnityEngine;

namespace LastWard.Player
{
    public class FirstPersonLook : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float sensitivity = 1f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;

        private float yaw;
        private float pitch;

        private void Start()
        {
            yaw = transform.eulerAngles.y;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // LateUpdate so the view resolves after movement has run this frame.
        private void LateUpdate()
        {
            // Take the cursor lock back if something dropped it. The Editor releases it on Escape
            // and on any focus loss — including the click that returns focus — and nothing ever
            // reclaimed it, so mouse look silently stopped working until the pointer happened to be
            // over the Game view again. Skipped while a panel owns the cursor, or the note reader
            // and keypad could never be clicked.
            if (Application.isFocused && !CursorLockGate.AnyPanelOpen &&
                Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            // Look input arrives already frame-normalized from PlayerInputReader (mouse delta as-is,
            // gamepad stick scaled by deltaTime). Deliberately NOT smoothed: filtering a delta signal
            // adds input lag, keeps drifting after the mouse stops, and loses travel on uneven
            // frames — which reads as "sticky then jumpy".
            Vector2 look = input.Look * sensitivity;

            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, minPitch, maxPitch);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
