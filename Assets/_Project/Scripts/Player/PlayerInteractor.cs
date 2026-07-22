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

            // Inside a hiding spot the ray is worthless — it starts inside the collider it would
            // have to hit — so targeting is suspended and the prompt comes from the overlay instead.
            if (HidingSpot.LocalOccupied != null)
            {
                InteractionPromptUI.Instance?.SetPrompt(null);
                CrosshairUI.Instance?.SetTargeted(false);
                return;
            }

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
            // Same key gets you out as got you in.
            var hidingIn = HidingSpot.LocalOccupied;
            if (hidingIn != null)
            {
                hidingIn.RequestExit();
                return;
            }

            // Swinging takes priority over interacting: if the Entity is on top of you and you're
            // holding a pipe, the button you'll be mashing is this one, and reading a note instead
            // would be a bad joke. Only consumes the press when a swing actually connects.
            if (PlayerMeleeDefense.Local != null && PlayerMeleeDefense.Local.TryStrike()) return;

            if (current != null && current.CanInteract(LocalPlayerId))
                current.Interact(LocalPlayerId);
        }
    }
}
