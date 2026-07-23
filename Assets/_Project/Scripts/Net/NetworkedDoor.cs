using LastWard.Audio;
using LastWard.Core;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Net
{
    /// <summary>
    /// The canonical networked IInteractable pattern (M5 puzzle interactables follow this): client
    /// requests via ServerRpc, server owns the state in a NetworkVariable, all clients react to the
    /// replicated value. The offline LastWard.Player.Door stays as-is for the solo M1 sandbox; from
    /// M2 on, interactables that must sync use this shape.
    /// </summary>
    public class NetworkedDoor : NetworkBehaviour, IInteractable
    {
        [SerializeField] private Transform hinge;
        [SerializeField] private float openAngle = 100f;
        [SerializeField] private float openSpeed = 3f;
        [Tooltip("Initial locked state on spawn. Puzzles unlock at runtime via ServerSetLocked, which drives the replicated 'locked' NetworkVariable, not this field directly.")]
        [SerializeField] private bool startLocked;
        [SerializeField] private float noiseRadius = 8f;

        private readonly NetworkVariable<bool> isOpen = new NetworkVariable<bool>();

        /// <summary>Shut doors are what the Entity slams open before a kill.</summary>
        public bool IsOpen => isOpen.Value;
        private readonly NetworkVariable<bool> locked = new NetworkVariable<bool>();
        private Quaternion closedRotation;
        private Quaternion targetRotation;
        private AudioSource audioSource;
        private AudioClip creak;
        private bool stateInitialized;

        private void Awake()
        {
            if (hinge == null) hinge = transform;
            closedRotation = hinge.localRotation;
            targetRotation = closedRotation;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 1.5f;
            audioSource.maxDistance = 20f;
            // Real door audio; open and close are different sounds.
            creak = GameSfx.DoorOpen;
        }

        public override void OnNetworkSpawn()
        {
            isOpen.OnValueChanged += OnOpenChanged;
            ApplyState(isOpen.Value);
            if (IsServer) locked.Value = startLocked;
        }

        public override void OnNetworkDespawn()
        {
            isOpen.OnValueChanged -= OnOpenChanged;
        }

        private void Update()
        {
            hinge.localRotation = Quaternion.Slerp(hinge.localRotation, targetRotation, Time.deltaTime * openSpeed);
        }

        private void OnOpenChanged(bool previous, bool current) => ApplyState(current);

        private void ApplyState(bool open)
        {
            targetRotation = open ? closedRotation * Quaternion.Euler(0f, openAngle, 0f) : closedRotation;
            // Skip the sound on the initial spawn sync; only play on actual toggles. Opening and
            // closing are different sounds — a door heard closing somewhere you are not is one of
            // the cheapest sources of dread in the game.
            if (stateInitialized) audioSource.PlayOneShot(open ? GameSfx.DoorOpen : GameSfx.DoorClose);
            stateInitialized = true;
        }

        public string GetPrompt() => locked.Value ? "Locked" : (isOpen.Value ? "Close door" : "Open door");
        public bool CanInteract(ulong playerId) => !locked.Value;
        public void Interact(ulong playerId) => RequestToggleServerRpc();

        /// <summary>Server-only. How puzzles (e.g. FusePowerPuzzle) unlock a gated door at runtime.</summary>
        public void ServerSetLocked(bool value)
        {
            if (!IsServer) return;
            locked.Value = value;
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestToggleServerRpc()
        {
            if (locked.Value) return;
            isOpen.Value = !isOpen.Value;
            GameEvents.RaiseNoiseEmitted(transform.position, noiseRadius, NoiseSource.Door);
        }
    }
}
