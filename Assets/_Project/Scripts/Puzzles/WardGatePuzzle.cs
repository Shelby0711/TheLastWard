using LastWard.Core;
using LastWard.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// The security gate across the first-floor hallway, and the three things it takes to get past it.
    ///
    /// <list type="number">
    /// <item><b>Power.</b> The gate's lock is electric and the floor is dead. A heavy cell goes into
    /// the generator by the stairs, and the lever is thrown — the indicator turns green and the keypad
    /// wakes up.</item>
    /// <item><b>The code.</b> Only obtainable from the paperwork; nothing on the gate hints at it.</item>
    /// <item><b>Force.</b> The lock releasing is not the gate opening. It is decades rusted, so it
    /// still needs a crowbar to be hauled aside.</item>
    /// </list>
    ///
    /// The three steps are deliberately in different places and need different carried items, which is
    /// where it bites: with four slots and a one-tool cap you cannot hold the battery, the crowbar and
    /// a weapon at once. Alone that is several trips past the Manager; together it is a division of
    /// labour, which is the co-op beat this floor is built around.
    /// </summary>
    public class WardGatePuzzle : NetworkBehaviour
    {
        [SerializeField] private Transform gateLeaf;
        [SerializeField] private Renderer generatorLamp;
        [SerializeField] private Renderer keypadScreen;
        [SerializeField] private Transform lever;
        [SerializeField] private string code = "1974";
        [SerializeField] private float knowledgeOnComplete = 6f;
        [SerializeField] private float slideDistance = 2.6f;
        [SerializeField] private float slideSpeed = 1.1f;

        private readonly NetworkVariable<bool> powered = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> unlocked = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> opened = new NetworkVariable<bool>();

        public bool IsPowered => powered.Value;
        public bool IsUnlocked => unlocked.Value;
        public bool IsOpen => opened.Value;
        public string Code => code;

        private Vector3 gateClosedPos;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            if (gateLeaf != null) gateClosedPos = gateLeaf.localPosition;
        }

        public override void OnNetworkSpawn()
        {
            powered.OnValueChanged += (_, __) => Refresh();
            unlocked.OnValueChanged += (_, __) => Refresh();
            Refresh();
        }

        private void Update()
        {
            if (gateLeaf == null) return;
            Vector3 target = opened.Value
                ? gateClosedPos + Vector3.right * slideDistance
                : gateClosedPos;
            gateLeaf.localPosition = Vector3.MoveTowards(gateLeaf.localPosition, target,
                slideSpeed * Time.deltaTime);
        }

        /// <summary>Red until the generator runs, then green — the one legible sign the floor is live.</summary>
        private void Refresh()
        {
            Tint(generatorLamp, powered.Value ? new Color(0.1f, 0.9f, 0.2f) : new Color(0.9f, 0.1f, 0.1f));
            Tint(keypadScreen, !powered.Value ? new Color(0.05f, 0.05f, 0.05f)
                : unlocked.Value ? new Color(0.1f, 0.9f, 0.3f) : new Color(0.15f, 0.55f, 0.2f));
            if (lever != null)
                lever.localRotation = Quaternion.Euler(powered.Value ? -55f : 55f, 0f, 0f);
        }

        private void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c * 0.4f);
            mpb.SetColor(EmissionColorId, c);
            r.SetPropertyBlock(mpb);
        }

        // ---- step 1: the generator ----

        [ServerRpc(RequireOwnership = false)]
        public void PowerOnServerRpc()
        {
            if (powered.Value) return;
            powered.Value = true;
            GameEvents.RaiseNoiseEmitted(transform.position, 16f, NoiseSource.PuzzleInteraction);
        }

        // ---- step 2: the code ----

        [ServerRpc(RequireOwnership = false)]
        public void SubmitCodeServerRpc(string entered, ServerRpcParams p = default)
        {
            if (!powered.Value || unlocked.Value) return;
            GameEvents.RaiseNoiseEmitted(transform.position, 10f, NoiseSource.PuzzleInteraction);
            if (entered != code) return;

            unlocked.Value = true;
            ulong who = p.Receive.SenderClientId;
            GameEvents.RaisePuzzleStepCompleted("ff_gate_code", who);
            LastWard.Knowledge.KnowledgeService.Instance?.AddScore(who, knowledgeOnComplete);
        }

        // ---- step 3: the crowbar ----

        [ServerRpc(RequireOwnership = false)]
        public void ForceOpenServerRpc()
        {
            if (!unlocked.Value || opened.Value) return;
            opened.Value = true;
            // Hauling a rusted gate aside is the loudest thing on this floor.
            GameEvents.RaiseNoiseEmitted(transform.position, 22f, NoiseSource.PuzzleInteraction);
        }
    }

    /// <summary>The generator by the stairs: takes the heavy cell, then the lever.</summary>
    public class GeneratorInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private WardGatePuzzle puzzle;

        public string GetPrompt()
        {
            if (puzzle == null) return null;
            if (puzzle.IsPowered) return "Generator — running";
            return PlayerInventory.Local != null && PlayerInventory.Local.HasItem("cell")
                ? "Fit the cell and throw the lever"
                : "Generator — dead (needs a heavy cell)";
        }

        public bool CanInteract(ulong playerId) =>
            puzzle != null && !puzzle.IsPowered &&
            PlayerInventory.Local != null && PlayerInventory.Local.HasItem("cell");

        public void Interact(ulong playerId)
        {
            PlayerInventory.Local.RemoveItem("cell");
            puzzle.PowerOnServerRpc();
        }
    }

    /// <summary>The pad beside the gate. Dark until the generator runs.</summary>
    public class GateKeypadInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private WardGatePuzzle puzzle;

        public string GetPrompt()
        {
            if (puzzle == null) return null;
            if (!puzzle.IsPowered) return "Keypad — no power";
            return puzzle.IsUnlocked ? "Keypad — accepted" : "Enter code";
        }

        public bool CanInteract(ulong playerId) =>
            puzzle != null && puzzle.IsPowered && !puzzle.IsUnlocked;

        public void Interact(ulong playerId) =>
            LastWard.UI.KeypadUI.Instance?.Open(entered => puzzle.SubmitCodeServerRpc(entered));
    }

    /// <summary>The gate itself: needs the crowbar once the lock has released.</summary>
    public class GateBarInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private WardGatePuzzle puzzle;

        public string GetPrompt()
        {
            if (puzzle == null || puzzle.IsOpen) return null;
            if (!puzzle.IsPowered) return "Gate — the lock is dead";
            if (!puzzle.IsUnlocked) return "Gate — still locked";
            return PlayerInventory.Local != null && PlayerInventory.Local.HasItem("crowbar")
                ? "Lever the gate open"
                : "Gate — rusted solid (needs a crowbar)";
        }

        public bool CanInteract(ulong playerId) =>
            puzzle != null && puzzle.IsUnlocked && !puzzle.IsOpen &&
            PlayerInventory.Local != null && PlayerInventory.Local.HasItem("crowbar");

        public void Interact(ulong playerId)
        {
            PlayerInventory.Local?.RegisterUse("crowbar");
            puzzle.ForceOpenServerRpc();
        }
    }
}
