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

        private void OnEnable()
        {
            input.InteractPressed += TryInteract;
            input.HidePressed += ToggleHide;
            input.AttackPressed += TryAttack;
        }

        private void OnDisable()
        {
            input.InteractPressed -= TryInteract;
            input.HidePressed -= ToggleHide;
            input.AttackPressed -= TryAttack;
            InteractionPromptUI.Instance?.SetPrompt(null);
            CrosshairUI.Instance?.SetTargeted(false);
        }

        private void Update()
        {
            current = null;

            // Targeting stays live while hidden. It used to be suspended, which meant you couldn't
            // pick anything up from inside a wardrobe or from under a bed — exactly where things are
            // stashed. Leaving it on is safe now that hiding has its own key and no longer competes
            // with Interact for the same press.
            if (Physics.Raycast(interactCamera.transform.position, interactCamera.transform.forward, out var hit, interactRange, interactableMask, QueryTriggerInteraction.Ignore))
            {
                // GetComponentInParent, not TryGetComponent: colliders usually sit on a child
                // mesh (a door's panel) while the IInteractable lives on the parent root.
                current = hit.collider.GetComponentInParent<IInteractable>();
            }

            InteractionPromptUI.Instance?.SetPrompt(current?.GetPrompt());
            CrosshairUI.Instance?.SetTargeted(current != null);
        }

        // E — pick things up, read notes, open containers. Never hiding, never attacking.
        private void TryInteract()
        {
            // A hiding spot is entered with Hide, not Interact, so looking at one while holding E
            // doesn't swallow a pickup that happens to be behind it.
            if (current is HidingSpot) return;
            if (current != null && current.CanInteract(LocalPlayerId))
                current.Interact(LocalPlayerId);
        }

        // Q — climb in, or climb out. The same key both ways.
        private void ToggleHide()
        {
            var hidingIn = HidingSpot.LocalOccupied;
            if (hidingIn != null)
            {
                hidingIn.RequestExit();
                return;
            }
            if (current is HidingSpot spot && spot.CanInteract(LocalPlayerId))
                spot.Interact(LocalPlayerId);
        }

        // Left mouse — swing whatever's carried.
        private void TryAttack()
        {
            if (HidingSpot.LocalOccupied != null) return;   // no swinging from inside a wardrobe
            PlayerMeleeDefense.Local?.TryStrike();
        }
    }
}
