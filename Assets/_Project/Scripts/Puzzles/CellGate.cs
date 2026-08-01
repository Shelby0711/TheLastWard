using LastWard.Audio;
using LastWard.Core;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// A cell gate on the asylum floor. Slides aside on rusted runners and stays open.
    ///
    /// I originally built these welded shut, reasoning that a floor whose tension is a 76m sprint
    /// could not afford a dozen openable obstacles on the route. That was the wrong call: the cells
    /// are the only cover on this floor, and cover you can see into but never enter is worse than no
    /// cover at all — it reads as scenery and the player stops looking at it.
    ///
    /// Open, they become somewhere to break line of sight, somewhere to be caught in, and somewhere
    /// the Inspector might already be. Opening one costs a loud grind of rusted metal, which on this
    /// floor is the only currency that matters.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class CellGate : NetworkBehaviour, IInteractable
    {
        [SerializeField] private Transform leaf;
        [Tooltip("How far it slides, along its own local Z. Set to the gate's width at build time.")]
        [SerializeField] private float slideDistance = 2.4f;
        [SerializeField] private float slideSpeed = 1.6f;
        [SerializeField] private float noiseRadius = 17f;

        private readonly NetworkVariable<bool> open = new NetworkVariable<bool>();

        private Vector3 closedLocal;
        private bool haveRest;

        public override void OnNetworkSpawn()
        {
            if (leaf != null && !haveRest) { closedLocal = leaf.localPosition; haveRest = true; }
            open.OnValueChanged += (_, v) => { if (v) PlayOpen(); };
            // Snap on spawn: a late joiner should find the gate where everyone else sees it, not
            // watch it slide open from closed.
            if (leaf != null && open.Value) leaf.localPosition = closedLocal + Vector3.forward * slideDistance;
        }

        private void Awake()
        {
            if (leaf != null && !haveRest) { closedLocal = leaf.localPosition; haveRest = true; }
        }

        private void Update()
        {
            if (leaf == null || !haveRest) return;
            Vector3 target = open.Value ? closedLocal + Vector3.forward * slideDistance : closedLocal;
            leaf.localPosition = Vector3.MoveTowards(leaf.localPosition, target, slideSpeed * Time.deltaTime);
        }

        public string GetPrompt() => open.Value ? null : "Force the gate";
        public bool CanInteract(ulong playerId) => !open.Value;
        public void Interact(ulong playerId) => OpenServerRpc();

        [ServerRpc(RequireOwnership = false)]
        private void OpenServerRpc()
        {
            if (open.Value) return;
            open.Value = true;
            // Rusted iron on rusted iron. Louder than a door, and it does not stop when you let go.
            GameEvents.RaiseNoiseEmitted(transform.position, noiseRadius, NoiseSource.PuzzleInteraction);
        }

        private void PlayOpen()
        {
            var clip = GameSfx.GateOpen;
            if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, 0.9f);
        }
    }
}
