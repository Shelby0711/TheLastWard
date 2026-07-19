using LastWard.Core;
using UnityEngine;

namespace LastWard.Player
{
    public class Door : MonoBehaviour, IInteractable
    {
        [SerializeField] private Transform hinge;
        [SerializeField] private float openAngle = 100f;
        [SerializeField] private float openSpeed = 3f;
        [SerializeField] private bool isLocked;
        [SerializeField] private float noiseRadius = 8f;

        private bool isOpen;
        private Quaternion closedRotation;
        private Quaternion targetRotation;

        private void Awake()
        {
            if (hinge == null) hinge = transform;
            closedRotation = hinge.localRotation;
            targetRotation = closedRotation;
        }

        private void Update()
        {
            hinge.localRotation = Quaternion.Slerp(hinge.localRotation, targetRotation, Time.deltaTime * openSpeed);
        }

        public string GetPrompt() => isLocked ? "Locked" : (isOpen ? "Close door" : "Open door");
        public bool CanInteract(ulong playerId) => !isLocked;

        public void Interact(ulong playerId)
        {
            isOpen = !isOpen;
            targetRotation = isOpen ? closedRotation * Quaternion.Euler(0f, openAngle, 0f) : closedRotation;
            GameEvents.RaiseNoiseEmitted(transform.position, noiseRadius, NoiseSource.Door);
        }

        public void SetLocked(bool locked) => isLocked = locked;
    }
}
