using System.Collections.Generic;
using LastWard.Core;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// The Morgue rearranging itself behind you.
    ///
    /// Both basement keys taken is the trigger. From that moment the floor you memorised on the way
    /// in is <b>wrong</b>: openings you walked through are bricked, walls you gave up on are gaps, and
    /// the stair you arrived by does not go anywhere it went before.
    ///
    /// Built as two sets of geometry that both exist from the start and simply swap active state, not
    /// as anything generated at runtime. That matters for three reasons: it replicates as one bool
    /// instead of a description of a floor plan, it cannot desync a host and a client into different
    /// buildings, and the level designer can walk both layouts in the editor.
    ///
    /// The cruelty is deliberate and specific. Spatial knowledge is the only knowledge on the asylum
    /// floor that does not mark you — so the Morgue takes that away too, and the return trip has to be
    /// solved rather than remembered.
    /// </summary>
    public class MorgueShifter : NetworkBehaviour
    {
        public static MorgueShifter Instance { get; private set; }

        [Tooltip("Present on the way IN, gone on the way OUT.")]
        [SerializeField] private List<GameObject> beforeOnly = new List<GameObject>();
        [Tooltip("Absent on the way IN, present on the way OUT.")]
        [SerializeField] private List<GameObject> afterOnly = new List<GameObject>();
        [SerializeField] private float shiftNoise = 26f;

        private readonly NetworkVariable<bool> shifted = new NetworkVariable<bool>();

        public bool HasShifted => shifted.Value;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            shifted.OnValueChanged += (_, v) => Apply(v);
            Apply(shifted.Value);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        private void Apply(bool after)
        {
            foreach (var g in beforeOnly) if (g != null) g.SetActive(!after);
            foreach (var g in afterOnly) if (g != null) g.SetActive(after);
        }

        /// <summary>Server-only. Called once both keys are held.</summary>
        public void ServerShift()
        {
            if (!IsServer || shifted.Value) return;
            shifted.Value = true;

            // It is not quiet. Everything on this floor hears the building move, and that is the
            // point: the change announces itself and then you have to walk through it.
            GameEvents.RaiseNoiseEmitted(transform.position, shiftNoise, NoiseSource.PuzzleInteraction);
            ShiftClientRpc();
            Debug.Log("[Morgue] The floor has shifted. The way back is not the way you came.");
        }

        [ClientRpc]
        private void ShiftClientRpc()
        {
            var clip = LastWard.Audio.GameSfx.Random(LastWard.Audio.GameSfx.WrongAttempt);
            if (clip != null) LastWard.Audio.GameSfx.Play2D(clip, 1f);
        }
    }
}
