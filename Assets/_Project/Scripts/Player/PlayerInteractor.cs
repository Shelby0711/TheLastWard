using LastWard.Core;
using LastWard.UI;
using UnityEngine;

namespace LastWard.Player
{
    public class PlayerInteractor : MonoBehaviour
    {
        // Placeholder until M2 wires real NGO client ids — 0 matches NGO's default host id,
        // and every IInteractable already takes ulong per PROJECT_CONTEXT.md's convention so
        // nothing here needs to change when networking lands.
        private const ulong LocalPlayerId = 0;

        [SerializeField] private PlayerInputReader input;
        [SerializeField] private Camera interactCamera;
        [SerializeField] private float interactRange = 2.5f;
        [SerializeField] private LayerMask interactableMask = ~0;

        private IInteractable current;

        private void OnEnable() => input.InteractPressed += TryInteract;
        private void OnDisable() => input.InteractPressed -= TryInteract;

        private void Update()
        {
            current = null;

            if (Physics.Raycast(interactCamera.transform.position, interactCamera.transform.forward, out var hit, interactRange, interactableMask))
            {
                hit.collider.TryGetComponent(out current);
            }

            InteractionPromptUI.Instance?.SetPrompt(current?.GetPrompt());
        }

        private void TryInteract()
        {
            if (current != null && current.CanInteract(LocalPlayerId))
                current.Interact(LocalPlayerId);
        }
    }
}
