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
        [Tooltip("Seconds an open door waits before easing shut on its own. Stops doors being " +
            "propped open as permanent escape routes, and means the Entity finds them CLOSED — which " +
            "is what lets it slam them rather than glide through an open frame. 0 disables.")]
        [SerializeField] private float autoCloseSeconds = 9f;

        // Declared BEFORE isOpen on purpose: NGO deserializes NetworkVariables in declaration order,
        // and isOpen's OnValueChanged reads slammed. Set alongside isOpen when the Entity kicks a
        // door in, so clients snap it hard and play the slam instead of the creak; reset on close.
        private readonly NetworkVariable<bool> slammed = new NetworkVariable<bool>();

        private readonly NetworkVariable<bool> isOpen = new NetworkVariable<bool>();

        /// <summary>Shut doors are what the Entity slams open before a kill.</summary>
        public bool IsOpen => isOpen.Value;
        public bool IsLocked => locked.Value;
        private readonly NetworkVariable<bool> locked = new NetworkVariable<bool>();
        private Quaternion closedRotation;
        private Quaternion targetRotation;
        private AudioSource audioSource;
        private AudioClip creak;
        private bool stateInitialized;
        private float openedAt;   // server clock, for auto-close

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

            // Auto-close is server-authoritative; the swing above then replicates to everyone.
            if (IsServer && autoCloseSeconds > 0f && isOpen.Value && !locked.Value
                && Time.time - openedAt >= autoCloseSeconds && !DoorwayOccupied())
            {
                slammed.Value = false;
                isOpen.Value = false;
            }
        }

        // Don't swing shut through a player standing in the frame — wait until they move clear.
        private bool DoorwayOccupied()
        {
            if (NetworkManager.Singleton == null) return false;
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var po = client.PlayerObject;
                if (po == null) continue;
                Vector3 d = po.transform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude < 1.6f * 1.6f) return true;
            }
            return false;
        }

        private void OnOpenChanged(bool previous, bool current) => ApplyState(current);

        private void ApplyState(bool open)
        {
            targetRotation = open ? closedRotation * Quaternion.Euler(0f, openAngle, 0f) : closedRotation;
            // Skip the sound on the initial spawn sync; only play on actual toggles. Opening and
            // closing are different sounds — a door heard closing somewhere you are not is one of
            // the cheapest sources of dread in the game.
            if (stateInitialized)
            {
                if (open && slammed.Value)
                {
                    // The Entity does not open doors, it goes through them. Snap the panel fully open
                    // this frame — no gentle creak-swing — and hit it with the slam clip.
                    hinge.localRotation = targetRotation;
                    audioSource.PlayOneShot(GameSfx.DoorSlam);
                }
                else audioSource.PlayOneShot(open ? GameSfx.DoorOpen : GameSfx.DoorClose);
            }
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

        /// <summary>
        /// Server-only. The Entity kicks the door in — snaps open hard, plays the slam, and throws a
        /// bigger noise than a normal open. Does nothing to a door that is already open or is still
        /// puzzle-locked (a locked door is a barrier the players earned; it blocks the Entity too).
        /// </summary>
        public void ServerSlamOpen()
        {
            if (!IsServer || isOpen.Value || locked.Value) return;
            slammed.Value = true;
            isOpen.Value = true;
            openedAt = Time.time;
            GameEvents.RaiseNoiseEmitted(transform.position, noiseRadius * 1.6f, NoiseSource.Door);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestToggleServerRpc()
        {
            if (locked.Value) return;
            bool nowOpen = !isOpen.Value;
            // A player opening a door is never a slam; clear the flag so it creaks normally.
            if (slammed.Value) slammed.Value = false;
            isOpen.Value = nowOpen;
            if (nowOpen) openedAt = Time.time;
            GameEvents.RaiseNoiseEmitted(transform.position, noiseRadius, NoiseSource.Door);
        }
    }
}
