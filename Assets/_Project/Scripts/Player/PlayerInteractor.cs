using LastWard.Core;
using LastWard.UI;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Player
{
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private Camera interactCamera;
        [SerializeField] private float interactRange = 3f;
        [SerializeField] private LayerMask interactableMask = ~0;

        private IInteractable current;

        // Real NGO client id once a session is running, 0 in the offline sandbox. Knowledge
        // scoring (M4) attributes actions by this id, so it has to be the true owner id.
        private static ulong LocalPlayerId =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
                ? NetworkManager.Singleton.LocalClientId
                : 0UL;

        private void OnEnable() => input.InteractPressed += TryInteract;
        private void OnDisable()
        {
            input.InteractPressed -= TryInteract;
            InteractionPromptUI.Instance?.SetPrompt(null);
            CrosshairUI.Instance?.SetTargeted(false);
        }

        private void Update()
        {
            current = null;

            if (Physics.Raycast(interactCamera.transform.position, interactCamera.transform.forward, out var hit, interactRange, interactableMask, QueryTriggerInteraction.Ignore))
            {
                // GetComponentInParent, not TryGetComponent: colliders usually sit on a child
                // mesh (a door's panel) while the IInteractable lives on the parent root.
                current = hit.collider.GetComponentInParent<IInteractable>();
            }

            InteractionPromptUI.Instance?.SetPrompt(current?.GetPrompt());
            CrosshairUI.Instance?.SetTargeted(current != null);
        }

        private void TryInteract()
        {
            if (current != null && current.CanInteract(LocalPlayerId))
                current.Interact(LocalPlayerId);
        }
    }
}
