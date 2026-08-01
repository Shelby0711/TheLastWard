using LastWard.Audio;
using LastWard.Core;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// The gate at the foot of the Morgue stair — the only thing on this floor that has to be solved.
    ///
    /// Answering is itself an act of knowledge, so <b>whoever works the gate is scored for it</b>.
    /// That is the squeeze the floor is built around: the exit demands engagement, and engagement is
    /// what marks you. There is no way to be both useful and safe.
    ///
    /// A wrong answer does not lock the run — it culls. The gate stays shut, the attempt is loud, and
    /// the Inspector is handed a reason to look at you. Survivors walk back and try again with fewer
    /// people, which is a far better failure state than a hard lock.
    ///
    /// The riddle text and its run-keyed answer are not written yet; the code is set at build time so
    /// the floor is traversable and the wiring is testable. See FLOOR2_ASYLUM.md §5 for the intent —
    /// fixed riddle, answer keyed to how the ward was arranged this run, so veterans get faster
    /// rather than exempt.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class RiddleGate : NetworkBehaviour, IInteractable
    {
        [SerializeField] private Transform leaf;
        [SerializeField] private float slideDistance = 2.8f;
        [SerializeField] private float slideSpeed = 1.1f;
        [SerializeField] private string answer = "1919";
        [Tooltip("Knowledge scored for working the gate at all — right or wrong. This is what makes " +
            "answering an act of knowledge rather than a free action.")]
        [SerializeField] private float knowledgeForAttempt = 4f;
        [SerializeField] private float knowledgeForSolving = 8f;

        private readonly NetworkVariable<bool> open = new NetworkVariable<bool>();

        private Vector3 closedLocal;
        private bool haveRest;

        public bool IsOpen => open.Value;

        private void Awake()
        {
            if (leaf != null && !haveRest) { closedLocal = leaf.localPosition; haveRest = true; }
        }

        public override void OnNetworkSpawn()
        {
            if (leaf != null && !haveRest) { closedLocal = leaf.localPosition; haveRest = true; }
            open.OnValueChanged += (_, v) => { if (v) PlayOpen(); };
            if (leaf != null && open.Value)
                leaf.localPosition = closedLocal + Vector3.forward * slideDistance;
        }

        private void Update()
        {
            if (leaf == null || !haveRest) return;
            Vector3 target = open.Value ? closedLocal + Vector3.forward * slideDistance : closedLocal;
            leaf.localPosition = Vector3.MoveTowards(leaf.localPosition, target,
                slideSpeed * Time.deltaTime);
        }

        public string GetPrompt() => open.Value ? null : "It asks you a question";
        public bool CanInteract(ulong playerId) => !open.Value;

        public void Interact(ulong playerId) =>
            LastWard.UI.KeypadUI.Instance?.Open(entered => SubmitServerRpc(entered));

        [ServerRpc(RequireOwnership = false)]
        private void SubmitServerRpc(string entered, ServerRpcParams p = default)
        {
            if (open.Value) return;
            ulong who = p.Receive.SenderClientId;

            // Scored whether or not it was right. Speaking to it is the act, not being correct.
            LastWard.Knowledge.KnowledgeService.Instance?.AddScore(who, knowledgeForAttempt);

            if (entered != LastWard.Puzzles.RunCodes.Riddle)
            {
                // Loud, and no closer to being through. The design's 15-second window and the run
                // back to the hiding places hang off this branch once they are built.
                GameEvents.RaiseNoiseEmitted(transform.position, 20f, NoiseSource.PuzzleInteraction);
                WrongClientRpc();
                return;
            }

            open.Value = true;
            GameEvents.RaiseNoiseEmitted(transform.position, 16f, NoiseSource.PuzzleInteraction);
            GameEvents.RaisePuzzleStepCompleted("sf_riddle_gate", who);
            LastWard.Knowledge.KnowledgeService.Instance?.AddScore(who, knowledgeForSolving);
        }

        [ClientRpc]
        private void WrongClientRpc()
        {
            var bang = GameSfx.Random(GameSfx.WrongAttempt);
            if (bang != null) AudioSource.PlayClipAtPoint(bang, transform.position, 1f);
        }

        private void PlayOpen()
        {
            var clip = GameSfx.GateOpen;
            if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, 1f);
        }
    }
}
