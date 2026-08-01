using LastWard.Core;
using LastWard.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// The way down. Two keys, and it does not open for one.
    ///
    /// Both must be held <b>at the same time</b>, which in co-op means one player carries both or two
    /// stand here together — and the item cap makes the first option a real cost rather than a
    /// formality. Solo it means walking the Morgue twice.
    ///
    /// Turning the second key is what shifts the floor. That ordering is deliberate: the player is
    /// standing at the exit, holding everything they came for, at the exact moment the building takes
    /// the route back. Nothing is lost — the way down is right here — but they now know the trip out
    /// is not the trip in, and the Morgue makes them prove it.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class BasementDoor : NetworkBehaviour, IInteractable
    {
        [SerializeField] private MorgueShifter shifter;
        [SerializeField] private string keyA = "basement_key_a";
        [SerializeField] private string keyB = "basement_key_b";
        [SerializeField] private float slideDistance = 2.5f;
        [SerializeField] private float slideSpeed = 0.9f;

        private readonly NetworkVariable<bool> open = new NetworkVariable<bool>();

        private Vector3 shut;
        private bool haveRest;

        public bool IsOpen => open.Value;

        private void Awake()
        {
            if (!haveRest) { shut = transform.localPosition; haveRest = true; }
        }

        public override void OnNetworkSpawn()
        {
            if (!haveRest) { shut = transform.localPosition; haveRest = true; }
            if (open.Value) transform.localPosition = shut + Vector3.down * slideDistance;
        }

        private void Update()
        {
            if (!haveRest) return;
            Vector3 target = open.Value ? shut + Vector3.down * slideDistance : shut;
            transform.localPosition = Vector3.MoveTowards(transform.localPosition, target,
                slideSpeed * Time.deltaTime);
        }

        private static bool Holding(string id)
        {
            var inv = PlayerInventory.Local;
            return inv != null && inv.HasItem(id);
        }

        public string GetPrompt()
        {
            if (open.Value) return null;
            bool a = Holding(keyA), b = Holding(keyB);
            if (a && b) return "Unlock the way down";
            if (a || b) return "One key turns. The other lock does not move";
            return "Two locks, and no keys";
        }

        public bool CanInteract(ulong playerId) => !open.Value && Holding(keyA) && Holding(keyB);

        public void Interact(ulong playerId) => OpenServerRpc();

        [ServerRpc(RequireOwnership = false)]
        private void OpenServerRpc(ServerRpcParams p = default)
        {
            if (open.Value) return;
            open.Value = true;
            GameEvents.RaiseNoiseEmitted(transform.position, 18f, NoiseSource.PuzzleInteraction);
            GameEvents.RaisePuzzleStepCompleted("tf_basement_door", p.Receive.SenderClientId);

            // And the floor behind them stops being the floor they walked in through.
            if (shifter != null) shifter.ServerShift();
            else MorgueShifter.Instance?.ServerShift();
        }
    }
}
