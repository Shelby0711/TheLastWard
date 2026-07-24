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
        [Tooltip("How far the view drops as it takes hold. Looking UP at it is what puts its face " +
            "in frame and makes it loom; at standing eye height you only get its chest.")]
        [SerializeField] private float heldCameraDrop = 0.45f;
        [Tooltip("How far the view is then LIFTED as it hoists you off the floor by the throat. " +
            "Sized so you end up level with its face, not above its head looking down.")]
        [SerializeField] private float heldCameraLift = 0.7f;
        [Tooltip("Seconds into the hold before the lift starts. Matches Watcher_Catch, whose hand " +
            "begins rising at frame 104 of 168 at 24fps.")]
        [SerializeField] private float heldLiftDelay = 4.33f;
        [Tooltip("Matches the clip's lift, frames 104 to 156 at 24fps.")]
        [SerializeField] private float heldLiftDuration = 2.17f;
        [SerializeField] private float heldDropDuration = 0.7f;
        [Tooltip("Height of the Entity's FACE above its transform origin. Its transform sits about " +
            "1.15m up already (NavMeshAgent.baseOffset on a centre-pivot capsule), so this is a " +
            "small offset - not the full head height, which would aim above the roof of its skull.")]
        [SerializeField] private float heldAimHeight = 0.65f;
        private LastWard.Net.PlayerNetworkState heldState;
        private LastWard.Entity.EntityController caughtBy;
        private float heldBasePivotY;
        private bool heldWasActive;
        private float heldStartedAt;

        /// <summary>
        /// Starts the catch camera timing, driven by the same RPC that triggers the catch animation
        /// so the two share one clock. Deriving it from the replicated hold flag let the animation
        /// start a frame or more earlier, which read as the arm lifting before the view did.
        /// </summary>
        public void BeginCatch()
        {
            if (cameraPivot != null) heldBasePivotY = cameraPivot.localPosition.y;
            heldStartedAt = Time.time;
            heldWasActive = true;
        }

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
            // While the Entity has hold of you, look input is ignored and the view is turned onto
            // it. The catch is built entirely around eye contact; letting the player spin away
            // would throw out the one beat the sequence exists for.
            if (heldState == null) heldState = GetComponent<LastWard.Net.PlayerNetworkState>();
            if (heldState != null && heldState.IsHeld)
            {
                if (!heldWasActive)
                {
                    heldBasePivotY = cameraPivot.localPosition.y;
                    heldStartedAt = Time.time;
                    heldWasActive = true;
                }

                // Two beats, driven straight off the clip's timing: the view drops as the hand
                // closes on your throat, then rises as it hoists you clear of the floor.
                //
                // Assigned DIRECTLY rather than eased toward with a Lerp. A per-frame Lerp is a
                // smoothing filter with real lag, so the camera always trailed the animation by a
                // few tenths of a second and the arm visibly went up first. The SmoothStep below
                // already supplies the easing, so the position can be exact.
                float sinceHeld = Time.time - heldStartedAt;
                float drop = heldDropDuration <= 0f ? 1f
                    : Mathf.Clamp01(sinceHeld / heldDropDuration);
                float lift = heldLiftDuration <= 0f ? 1f
                    : Mathf.Clamp01((sinceHeld - heldLiftDelay) / heldLiftDuration);
                drop = Mathf.SmoothStep(0f, 1f, drop);
                lift = Mathf.SmoothStep(0f, 1f, lift);

                Vector3 lp = cameraPivot.localPosition;
                lp.y = heldBasePivotY - heldCameraDrop * drop + heldCameraLift * lift;
                cameraPivot.localPosition = lp;

                if (caughtBy == null) caughtBy = FindAnyObjectByType<LastWard.Entity.EntityController>();
                if (caughtBy != null)
                {
                    // Aimed at its FACE. Its transform is already raised off the floor, so this is
                    // a small offset on top of that - the old full-head value aimed over its skull,
                    // which is why being lifted showed the ceiling instead of the thing holding you.
                    Vector3 to = caughtBy.transform.position + Vector3.up * heldAimHeight - cameraPivot.position;
                    Vector3 flat = new Vector3(to.x, 0f, to.z);
                    if (flat.sqrMagnitude > 0.0001f)
                    {
                        yaw = Mathf.LerpAngle(yaw, Quaternion.LookRotation(flat).eulerAngles.y,
                            Time.deltaTime * 8f);
                        float wantPitch = -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                        pitch = Mathf.Lerp(pitch, Mathf.Clamp(wantPitch, minPitch, maxPitch),
                            Time.deltaTime * 8f);
                    }
                }
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                return;
            }
            if (heldWasActive)
            {
                // Released (or dead): hand the pivot height back to the motor.
                Vector3 lp = cameraPivot.localPosition;
                lp.y = heldBasePivotY;
                cameraPivot.localPosition = lp;
                heldWasActive = false;
            }

            Vector2 look = input.Look * sensitivity;

            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, minPitch, maxPitch);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
