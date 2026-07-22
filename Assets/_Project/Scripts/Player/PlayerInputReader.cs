using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastWard.Player
{
    /// <summary>
    /// Owns the generated PlayerControls instance so no other script touches the Input System
    /// directly. M1 is solo-only, so a static Local reference is enough; M2's networking pass
    /// doesn't change this — input stays local/per-machine even in co-op, it just gets read by
    /// whichever player object belongs to this client.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        public static PlayerInputReader Local { get; private set; }

        private PlayerControls controls;

        private const float GamepadLookDegreesPerSecond = 90f;

        public Vector2 Move => controls.Gameplay.Move.ReadValue<Vector2>();

        /// <summary>
        /// Frame-normalized look input. A mouse delta is already a per-frame displacement, but a
        /// gamepad stick is a held rate — without the deltaTime scaling below, stick look speed
        /// would change with framerate. Consumers add this straight to their angles.
        /// </summary>
        public Vector2 Look
        {
            get
            {
                Vector2 value = controls.Gameplay.Look.ReadValue<Vector2>();
                if (controls.Gameplay.Look.activeControl?.device is Gamepad)
                    value *= Time.deltaTime * GamepadLookDegreesPerSecond;
                return value;
            }
        }
        public bool SprintHeld => controls.Gameplay.Sprint.IsPressed();
        public bool CrouchHeld => controls.Gameplay.Crouch.IsPressed();

        public event Action InteractPressed;
        /// <summary>Enter/leave a hiding spot. Separate from Interact so being hidden never
        /// blocks picking things up — and so the key that got you in is the key that gets you out.</summary>
        public event Action HidePressed;
        /// <summary>Swing a carried weapon. Separate from Interact so a swing can't be mistaken
        /// for reading a note with the Entity on top of you.</summary>
        public event Action AttackPressed;
        public event Action JournalPressed;
        public event Action FlashlightTogglePressed;
        public event Action InventorySlot1Pressed;
        public event Action InventorySlot2Pressed;
        public event Action PausePressed;

        public event Action SpectatorNextPressed;
        public event Action SpectatorPreviousPressed;
        public event Action SpectatorPingPressed;

        private void Awake()
        {
            controls = new PlayerControls();

            controls.Gameplay.Interact.performed += OnInteract;
            controls.Gameplay.ToggleFlashlight.performed += OnFlashlightToggle;
            controls.Gameplay.InventorySlot1.performed += OnInventorySlot1;
            controls.Gameplay.InventorySlot2.performed += OnInventorySlot2;
            controls.Gameplay.Pause.performed += OnPause;
            controls.Gameplay.Hide.performed += OnHide;
            controls.Gameplay.Attack.performed += OnAttack;
            controls.Gameplay.Journal.performed += OnJournal;

            controls.Spectator.SwitchNext.performed += OnSpectatorNext;
            controls.Spectator.SwitchPrevious.performed += OnSpectatorPrevious;
            controls.Spectator.Ping.performed += OnSpectatorPing;
        }

        /// <summary>Swap gameplay input for spectator input on death (and back, if ever revived).</summary>
        public void SetSpectatorMode(bool spectating)
        {
            if (spectating) { controls.Gameplay.Disable(); controls.Spectator.Enable(); }
            else { controls.Spectator.Disable(); controls.Gameplay.Enable(); }
        }

        // Local is set here (not Awake) so that in co-op only the owning client's player —
        // the one NetworkPlayer leaves enabled — ever claims the static reference. Remote
        // player objects have this component disabled, so their OnEnable never runs.
        private void OnEnable()
        {
            Local = this;
            controls.Gameplay.Enable();
        }

        private void OnDisable()
        {
            controls.Gameplay.Disable();
            controls.Spectator.Disable();
            if (Local == this) Local = null;
        }

        private void OnDestroy()
        {
            controls.Gameplay.Interact.performed -= OnInteract;
            controls.Gameplay.ToggleFlashlight.performed -= OnFlashlightToggle;
            controls.Gameplay.InventorySlot1.performed -= OnInventorySlot1;
            controls.Gameplay.InventorySlot2.performed -= OnInventorySlot2;
            controls.Gameplay.Pause.performed -= OnPause;
            controls.Gameplay.Hide.performed -= OnHide;
            controls.Gameplay.Attack.performed -= OnAttack;
            controls.Gameplay.Journal.performed -= OnJournal;
            controls.Spectator.SwitchNext.performed -= OnSpectatorNext;
            controls.Spectator.SwitchPrevious.performed -= OnSpectatorPrevious;
            controls.Spectator.Ping.performed -= OnSpectatorPing;
            controls.Dispose();
        }

        private void OnInteract(InputAction.CallbackContext ctx) => InteractPressed?.Invoke();
        private void OnFlashlightToggle(InputAction.CallbackContext ctx) => FlashlightTogglePressed?.Invoke();
        private void OnInventorySlot1(InputAction.CallbackContext ctx) => InventorySlot1Pressed?.Invoke();
        private void OnInventorySlot2(InputAction.CallbackContext ctx) => InventorySlot2Pressed?.Invoke();
        private void OnPause(InputAction.CallbackContext ctx) => PausePressed?.Invoke();
        private void OnHide(InputAction.CallbackContext ctx) => HidePressed?.Invoke();
        private void OnAttack(InputAction.CallbackContext ctx) => AttackPressed?.Invoke();
        private void OnJournal(InputAction.CallbackContext ctx) => JournalPressed?.Invoke();
        private void OnSpectatorNext(InputAction.CallbackContext ctx) => SpectatorNextPressed?.Invoke();
        private void OnSpectatorPrevious(InputAction.CallbackContext ctx) => SpectatorPreviousPressed?.Invoke();
        private void OnSpectatorPing(InputAction.CallbackContext ctx) => SpectatorPingPressed?.Invoke();
    }
}
