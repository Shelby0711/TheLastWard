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
        // Two faces of the generator housing, so the state is legible whether you come up off the
        // stairs or walk past it. One buried 7cm dot was not an indicator on a floor this dark.
        [SerializeField] private Renderer[] generatorLamps;
        [SerializeField] private Light generatorGlow;
        [SerializeField] private Renderer keypadScreen;
        [SerializeField] private Light keypadGlow;
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

        private static readonly Color Red = new Color(0.95f, 0.1f, 0.08f);
        private static readonly Color Green = new Color(0.1f, 0.9f, 0.3f);
        private static readonly Color Dead = new Color(0.05f, 0.05f, 0.05f);

        /// <summary>
        /// Red until the generator runs, and red again on the pad until the code is accepted.
        ///
        /// The pad used to sit on a dim green while locked and a bright green once open — two greens,
        /// which is no signal at all: you could not tell by looking whether you had solved it. Locked
        /// is now unambiguously red, so green means exactly one thing on this floor.
        /// </summary>
        private void Refresh()
        {
            Color gen = powered.Value ? Green : Red;
            if (generatorLamps != null)
                foreach (var r in generatorLamps) Tint(r, gen);
            SetGlow(generatorGlow, gen, powered.Value ? 1.3f : 1.1f);

            Color pad = !powered.Value ? Dead : unlocked.Value ? Green : Red;
            Tint(keypadScreen, pad);
            SetGlow(keypadGlow, pad, powered.Value ? 0.9f : 0.15f);

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

        // Emissive material on its own reads as flat dark paint under a 0.14 ambient. The lamp only
        // actually looks lit because something is casting from it.
        private static void SetGlow(Light l, Color c, float intensity)
        {
            if (l == null) return;
            l.color = c;
            l.intensity = intensity;
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
            // The run's code, not the one baked into the prefab.
            if (entered != RunCodes.Gate)
            {
                // A rejected code used to do nothing visible or audible at all — the pad stayed the
                // same green and the panel just closed, so there was no way to tell a wrong entry
                // from an unregistered keypress. It now buzzes and flashes, and the buzz is loud
                // enough to be worth being sure before you try: the same bargain the corridor locks
                // downstairs make, and the same clip.
                WrongCodeClientRpc();
                GameEvents.RaiseNoiseEmitted(transform.position, 18f, NoiseSource.PuzzleInteraction);
                return;
            }

            unlocked.Value = true;
            ulong who = p.Receive.SenderClientId;
            GameEvents.RaisePuzzleStepCompleted("ff_gate_code", who);
            LastWard.Knowledge.KnowledgeService.Instance?.AddScore(who, knowledgeOnComplete);
        }

        [ClientRpc]
        private void GateSlideClientRpc()
        {
            // Positional, and long: the gate takes seconds to travel and the sound should still be
            // running when it stops. Everyone on the floor hears where it happened.
            var clip = LastWard.Audio.GameSfx.HallwayGate;
            if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, 1f);
        }

        [ClientRpc]
        private void WrongCodeClientRpc()
        {
            var bang = LastWard.Audio.GameSfx.Random(LastWard.Audio.GameSfx.WrongAttempt);
            if (bang != null) AudioSource.PlayClipAtPoint(bang, transform.position, 1f);
            StopAllCoroutines();
            StartCoroutine(FlashWrong());
        }

        private System.Collections.IEnumerator FlashWrong()
        {
            for (int i = 0; i < 3; i++)
            {
                Tint(keypadScreen, Dead);
                SetGlow(keypadGlow, Red, 0.1f);
                yield return new WaitForSeconds(0.09f);
                Tint(keypadScreen, Red);
                SetGlow(keypadGlow, Red, 2.2f);
                yield return new WaitForSeconds(0.12f);
            }
            Refresh();
        }

        // ---- step 3: the crowbar ----

        [ServerRpc(RequireOwnership = false)]
        public void ForceOpenServerRpc()
        {
            if (!unlocked.Value || opened.Value) return;
            opened.Value = true;
            // Hauling a rusted gate aside is the loudest thing on this floor.
            GameEvents.RaiseNoiseEmitted(transform.position, 22f, NoiseSource.PuzzleInteraction);
            GateSlideClientRpc();
        }
    }

}
