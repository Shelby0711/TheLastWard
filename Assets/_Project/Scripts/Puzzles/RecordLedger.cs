using System.Collections.Generic;
using LastWard.Core;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// The Inspector's ledger, and the four files ranged around it.
    ///
    /// This exists so the mark is attached to <b>a physical object in the world</b> rather than to
    /// the player's memory. The original design asked players to "forget", which cannot be done —
    /// a player who read a code still knows it, whatever the game says. Making it a record instead
    /// sidesteps that entirely: nobody is asked to forget anything, they are asked what they will do
    /// with a minute.
    ///
    /// Two verbs, deliberately asymmetric:
    /// <list type="bullet">
    /// <item><b>Burn</b> your own file. Cancels your timer. Costs a match, and fire on this floor is
    /// loud and bright — it draws the Manager as well.</item>
    /// <item><b>Swap</b> a nameplate. Moves the mark to someone else. Quiet, fast, calls nothing.</item>
    /// </list>
    /// Betrayal being the <i>easier</i> option is the point and must be protected. It should not be
    /// a clever exploit players discover; it should be the obvious cowardly thing sitting right
    /// there, which is what makes declining it mean something.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class RecordLedger : NetworkBehaviour
    {
        public static RecordLedger Instance { get; private set; }

        [SerializeField] private Vector3 bookPosition;
        [SerializeField] private string matchItemId = "matchbox";
        [SerializeField] private float burnNoise = 20f;

        // Four fixed NetworkVariables, not a NetworkList. NetworkList allocates native collections
        // that NGO ticks every frame, and a pair of them on a scene object was throwing
        // ArgumentNullException("dest") a thousand times a second. Four slots is a fixed count -
        // there was never a reason for a dynamic container.
        private readonly NetworkVariable<long> owner0 = new NetworkVariable<long>(-1);
        private readonly NetworkVariable<long> owner1 = new NetworkVariable<long>(-1);
        private readonly NetworkVariable<long> owner2 = new NetworkVariable<long>(-1);
        private readonly NetworkVariable<long> owner3 = new NetworkVariable<long>(-1);
        /// <summary>Bitmask, one bit per slot.</summary>
        private readonly NetworkVariable<int> burnedMask = new NetworkVariable<int>();

        private const int SlotCount = 4;

        private long GetOwner(int i) => i switch
        {
            0 => owner0.Value, 1 => owner1.Value, 2 => owner2.Value, 3 => owner3.Value, _ => -1
        };

        private void SetOwner(int i, long v)
        {
            switch (i)
            {
                case 0: owner0.Value = v; break;
                case 1: owner1.Value = v; break;
                case 2: owner2.Value = v; break;
                case 3: owner3.Value = v; break;
            }
        }

        public Vector3 BookPosition => bookPosition;
        public string MatchItemId => matchItemId;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;
            AssignServer();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Server-only. Fills empty plaques from whoever is connected.</summary>
        public void AssignServer()
        {
            if (!IsServer || NetworkManager.Singleton == null) return;
            var taken = new HashSet<long>();
            for (int i = 0; i < SlotCount; i++)
                if (GetOwner(i) >= 0) taken.Add(GetOwner(i));

            int next = 0;
            foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (taken.Contains((long)c.ClientId)) continue;
                while (next < SlotCount && GetOwner(next) >= 0) next++;
                if (next >= SlotCount) break;
                SetOwner(next, (long)c.ClientId);
                taken.Add((long)c.ClientId);
            }
        }

        public long OwnerOf(int slot) => slot >= 0 && slot < SlotCount ? GetOwner(slot) : -1;

        public bool IsBurned(int slot) =>
            slot >= 0 && slot < SlotCount && (burnedMask.Value & (1 << slot)) != 0;

        public int SlotOf(ulong client)
        {
            for (int i = 0; i < SlotCount; i++)
                if (GetOwner(i) == (long)client) return i;
            return -1;
        }

        /// <summary>True if this file's owner has a burned record and is therefore off the list.</summary>
        public bool IsOffTheList(ulong client)
        {
            int s = SlotOf(client);
            return s >= 0 && IsBurned(s);
        }

        // ---- the two verbs ----

        /// <summary>Server-only. Burns a file: its owner drops off the Inspector's list for good.</summary>
        public void ServerBurn(int slot, ulong by)
        {
            if (!IsServer || slot < 0 || slot >= SlotCount || IsBurned(slot)) return;
            burnedMask.Value |= 1 << slot;

            // Fire is the whole cost. It is loud, it is bright, and on a floor where the Manager
            // punishes being lit, doing this is a decision rather than a formality.
            GameEvents.RaiseNoiseEmitted(transform.position, burnNoise, NoiseSource.PuzzleInteraction);
            BurnFlashClientRpc(slot);

            long owner = GetOwner(slot);
            if (owner >= 0) LastWard.Entity.InspectorController.Instance?.ServerClearMark((ulong)owner);
        }

        /// <summary>
        /// Server-only. Swaps two nameplates — and with them, who the Inspector is reading about.
        ///
        /// Nothing is announced. The other player simply gets a timer, which they would have got
        /// eventually anyway by being next-highest, so they can suspect and accuse and be wrong. If
        /// the game ever tells anyone what happened, the whole social mechanic collapses into a
        /// notification.
        /// </summary>
        public void ServerSwap(int a, int b)
        {
            if (!IsServer || a == b) return;
            if (a < 0 || b < 0 || a >= SlotCount || b >= SlotCount) return;
            long tmp = GetOwner(a);
            SetOwner(a, GetOwner(b));
            SetOwner(b, tmp);
            // Quiet. A page turning is not a fire.
            GameEvents.RaiseNoiseEmitted(transform.position, 4f, NoiseSource.PuzzleInteraction);
            LastWard.Entity.InspectorController.Instance?.ServerReconsider();
        }

        [ClientRpc]
        private void BurnFlashClientRpc(int slot)
        {
            var go = new GameObject("RecordBurn");
            go.transform.position = bookPosition + Vector3.up * 0.3f;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 9f;
            l.intensity = 3.2f;
            l.color = new Color(1f, 0.55f, 0.18f);
            l.shadows = LightShadows.None;
            // Registered as world light for as long as it burns, so the Manager's existing "standing
            // in light" term does the rest without a special case.
            go.AddComponent<WorldLight>();
            var clip = LastWard.Audio.GameSfx.MatchStrike;
            if (clip != null) AudioSource.PlayClipAtPoint(clip, bookPosition, 1f);
            Destroy(go, 6f);
        }
    }
}
