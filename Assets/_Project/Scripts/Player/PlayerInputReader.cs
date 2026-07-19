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

        public Vector2 Move => controls.Gameplay.Move.ReadValue<Vector2>();
        public Vector2 Look => controls.Gameplay.Look.ReadValue<Vector2>();
        public bool SprintHeld => controls.Gameplay.Sprint.IsPressed();
        public bool CrouchHeld => controls.Gameplay.Crouch.IsPressed();

        public event Action InteractPressed;
        public event Action FlashlightTogglePressed;
        public event Action InventorySlot1Pressed;
        public event Action InventorySlot2Pressed;
        public event Action PausePressed;

        private void Awake()
        {
            controls = new PlayerControls();
            Local = this;

            controls.Gameplay.Interact.performed += OnInteract;
            controls.Gameplay.ToggleFlashlight.performed += OnFlashlightToggle;
            controls.Gameplay.InventorySlot1.performed += OnInventorySlot1;
            controls.Gameplay.InventorySlot2.performed += OnInventorySlot2;
            controls.Gameplay.Pause.performed += OnPause;
        }

        private void OnEnable() => controls.Gameplay.Enable();
        private void OnDisable() => controls.Gameplay.Disable();

        private void OnDestroy()
        {
            controls.Gameplay.Interact.performed -= OnInteract;
            controls.Gameplay.ToggleFlashlight.performed -= OnFlashlightToggle;
            controls.Gameplay.InventorySlot1.performed -= OnInventorySlot1;
            controls.Gameplay.InventorySlot2.performed -= OnInventorySlot2;
            controls.Gameplay.Pause.performed -= OnPause;
            controls.Dispose();
            if (Local == this) Local = null;
        }

        private void OnInteract(InputAction.CallbackContext ctx) => InteractPressed?.Invoke();
        private void OnFlashlightToggle(InputAction.CallbackContext ctx) => FlashlightTogglePressed?.Invoke();
        private void OnInventorySlot1(InputAction.CallbackContext ctx) => InventorySlot1Pressed?.Invoke();
        private void OnInventorySlot2(InputAction.CallbackContext ctx) => InventorySlot2Pressed?.Invoke();
        private void OnPause(InputAction.CallbackContext ctx) => PausePressed?.Invoke();
    }
}
