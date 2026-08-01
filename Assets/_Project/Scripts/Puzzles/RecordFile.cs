// Split out of its original combined file. Unity resolves a MonoBehaviour's MonoScript by FILENAME:
// a component class that does not match the file it lives in cannot be serialised into a scene, so
// AddComponent at build time produced a component that arrived broken and silently did nothing.
// Every MonoBehaviour therefore gets its own file.
using LastWard.Core;
using LastWard.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// One nameplate on the ledger table. Burn it if it is yours; swap it if it is not.
    ///
    /// The prompt never says who is marked, only whose file it is. Knowing that the Inspector is
    /// reading about you comes from the timer on your own screen and nowhere else — so a player
    /// standing at someone else's plaque with a match in hand is making a decision with incomplete
    /// information, which is the only honest way to price a betrayal.
    /// </summary>
    public class RecordFile : NetworkBehaviour, IInteractable
    {
        [SerializeField] private RecordLedger ledger;
        [SerializeField] private int slot;

        private static int selected = -1;      // client-local: the plate you picked up first

        public string GetPrompt()
        {
            if (ledger == null) return null;
            if (ledger.IsBurned(slot)) return "Burned";

            long owner = ledger.OwnerOf(slot);
            if (owner < 0) return "An empty file";

            bool mine = NetworkManager.Singleton != null &&
                        (ulong)owner == NetworkManager.Singleton.LocalClientId;

            if (selected >= 0 && selected != slot) return "Swap the nameplates";
            if (mine)
            {
                var inv = PlayerInventory.Local;
                bool haveMatch = inv != null && inv.HasItem(ledger.MatchItemId);
                return haveMatch ? "Burn your file" : "Your file — nothing to light it with";
            }
            return "Lift the nameplate";
        }

        public bool CanInteract(ulong playerId)
        {
            if (ledger == null || ledger.IsBurned(slot) || ledger.OwnerOf(slot) < 0) return false;
            if (selected >= 0 && selected != slot) return true;
            if (!Mine()) return true;
            var inv = PlayerInventory.Local;
            return inv != null && inv.HasItem(ledger.MatchItemId);
        }

        private bool Mine() =>
            ledger != null && NetworkManager.Singleton != null &&
            ledger.OwnerOf(slot) == (long)NetworkManager.Singleton.LocalClientId;

        public void Interact(ulong playerId)
        {
            if (ledger == null) return;

            // Second plate of a pair: complete the swap.
            if (selected >= 0 && selected != slot)
            {
                int first = selected;
                selected = -1;
                SwapServerRpc(first, slot);
                return;
            }

            if (Mine())
            {
                BurnServerRpc(slot);
                return;
            }

            // Someone else's: lift it, and wait for the plate you mean to exchange it with.
            selected = slot;
        }

        [ServerRpc(RequireOwnership = false)]
        private void BurnServerRpc(int s, ServerRpcParams p = default)
        {
            if (ledger == null) return;
            // Burning is the only verb that costs a match. Swapping is free, which is exactly the
            // asymmetry the design is built on.
            if (MatchSupply.Instance != null && !MatchSupply.Instance.ServerSpend()) return;
            ledger.ServerBurn(s, p.Receive.SenderClientId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SwapServerRpc(int a, int b) => ledger?.ServerSwap(a, b);
    }
}
