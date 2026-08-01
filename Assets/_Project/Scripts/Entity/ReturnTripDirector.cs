using System.Collections.Generic;
using LastWard.Core;
using LastWard.Puzzles;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Entity
{
    /// <summary>
    /// The rule the Morgue teaches by killing you: on the way out, <b>do not stop and do not look
    /// back</b>.
    ///
    /// Dormant until the floor shifts. After that, every player on this floor carries a hidden
    /// attention value that climbs while they stand still, and climbs far faster while they are
    /// facing back the way they came. Reaching the top does not kill them — it makes a noise loud
    /// enough that whatever is on this floor comes to find out what made it.
    ///
    /// Why attention rather than instant death: the floors below all teach that being noticed is the
    /// failure, and death is what noticing leads to. A new rule that skipped straight to a corpse
    /// would read as a scripted trap rather than as the building behaving the way it has behaved for
    /// three floors. It also lets the mechanic be survivable — you can feel it building, and moving
    /// forward is always the answer.
    ///
    /// The two states are deliberately asymmetric. Standing still is a mistake anyone makes; looking
    /// back is a choice, and it is the one the corridor punishes hardest.
    /// </summary>
    public class ReturnTripDirector : NetworkBehaviour
    {
        [Header("Where")]
        [SerializeField] private float floorMinY = 9f;
        [SerializeField] private float floorMaxY = 14f;
        [Tooltip("The direction OUT. Facing away from this is 'looking back'.")]
        [SerializeField] private Vector3 wayOut = new Vector3(0f, 0f, -1f);

        [Header("Attention")]
        [Tooltip("Seconds of stillness before it starts counting at all.")]
        [SerializeField] private float stillGrace = 2.5f;
        [SerializeField] private float stillRate = 0.10f;
        [Tooltip("Per second while facing back down the corridor. The choice costs more than the habit.")]
        [SerializeField] private float lookBackRate = 0.34f;
        [SerializeField] private float moveDrain = 0.22f;
        [Tooltip("Noise made when a player's attention fills. Loud enough to be answered.")]
        [SerializeField] private float summonNoise = 30f;
        [SerializeField] private float cooldown = 12f;

        private class Watch
        {
            public Vector3 last;
            public float still;
            public float attention;
            public float mutedUntil;
        }

        private readonly Dictionary<ulong, Watch> watching = new Dictionary<ulong, Watch>();

        private void Update()
        {
            if (!IsServer) return;
            var shifter = MorgueShifter.Instance;
            if (shifter == null || !shifter.HasShifted) return;      // only on the way out
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            float dt = Time.deltaTime;
            Vector3 outward = wayOut.normalized;

            foreach (var c in nm.ConnectedClientsList)
            {
                var po = c.PlayerObject;
                if (po == null) continue;
                var pns = po.GetComponent<LastWard.Net.PlayerNetworkState>();
                if (pns != null && !pns.IsAlive) continue;

                float y = po.transform.position.y;
                if (y < floorMinY || y > floorMaxY) { watching.Remove(c.ClientId); continue; }

                if (!watching.TryGetValue(c.ClientId, out var w))
                {
                    w = new Watch { last = po.transform.position };
                    watching[c.ClientId] = w;
                }

                float moved = Vector3.Distance(w.last, po.transform.position);
                w.last = po.transform.position;
                bool moving = moved / Mathf.Max(dt, 0.0001f) > 0.5f;

                // Facing back: the camera's forward pointing against the way out.
                var pivot = pns != null ? pns.CameraPivot : null;
                bool lookingBack = false;
                if (pivot != null)
                {
                    Vector3 f = pivot.forward; f.y = 0f;
                    if (f.sqrMagnitude > 0.01f)
                        lookingBack = Vector3.Dot(f.normalized, outward) < -0.35f;
                }

                w.still = moving ? 0f : w.still + dt;

                float delta = 0f;
                if (lookingBack) delta += lookBackRate;
                if (!moving && w.still > stillGrace) delta += stillRate;
                // Moving forward and facing forward is the only state that buys anything back. There
                // is no safe place to wait on this floor, only a safe direction.
                if (moving && !lookingBack) delta -= moveDrain;

                w.attention = Mathf.Clamp01(w.attention + delta * dt);

                if (w.attention >= 1f && Time.time >= w.mutedUntil)
                {
                    w.mutedUntil = Time.time + cooldown;
                    w.attention = 0.45f;                 // not reset: it stays uncomfortably high
                    GameEvents.RaiseNoiseEmitted(po.transform.position, summonNoise,
                        NoiseSource.PuzzleInteraction);
                    // The meter the floors below use, so the summons reads as the same language
                    // rather than a new punishment nobody has been taught.
                    if (pns != null) pns.ServerSetDiscovery(Mathf.Min(1f, pns.Discovery + 0.5f));
                    WarnClientRpc(new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams { TargetClientIds = new[] { c.ClientId } }
                    });
                }
            }
        }

        [ClientRpc]
        private void WarnClientRpc(ClientRpcParams p = default)
        {
            var clip = LastWard.Audio.GameSfx.Whisper;
            if (clip != null) LastWard.Audio.GameSfx.Play2D(clip, 0.9f);
        }
    }
}
