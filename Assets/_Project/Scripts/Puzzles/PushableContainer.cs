using System.Collections.Generic;
using LastWard.Core;
using LastWard.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// The linen crate you shove up the corridor to get over the collapsed stairs.
    ///
    /// Pushing is slow and continuously loud on a floor patrolled by something that hunts sound, so
    /// the trip becomes a run of decisions about when to stop and let the meter fall. Alone it costs
    /// half your speed; a second pair of hands makes it a quarter each, which is the first point on
    /// this floor where co-op is mechanically faster rather than merely safer.
    ///
    /// <b>One</b> interactable, on the root, doing all three jobs. It used to be two — a push handle
    /// and a separate climb trigger — and the climb box sat directly in front of the crate, so aiming
    /// at the face you actually push hit the climb prompt instead and you had to target the side to
    /// grab it. Two interactables on one object will always fight over the same look direction.
    ///
    /// While attached the player does not walk. Their input drives the crate and the crate drags them
    /// along with it. Letting both move independently meant they drifted apart at the first frame of
    /// latency and the crate slid out from under whoever was pushing it.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PushableContainer : NetworkBehaviour, IInteractable
    {
        [Header("Rail")]
        [SerializeField] private float railMinZ = 98f;
        [Tooltip("Where it comes to rest against the blockage.")]
        [SerializeField] private float railMaxZ = 114.7f;
        [SerializeField] private float railX;

        [Header("Feel")]
        [Tooltip("Fraction of speed a LONE pusher loses. Divided by the number of pushers.")]
        [SerializeField] private float loneLoad = 0.5f;
        [SerializeField] private float walkSpeed = 3.2f;
        [SerializeField] private float grabRange = 3.4f;

        [Header("Gate")]
        [Tooltip("A door across the rail. The crate is moved by writing railZ, which bypasses physics " +
            "entirely - so without this it slid straight through a shut door as if it were not there.")]
        [SerializeField] private LastWard.Net.NetworkedDoor gateDoor;
        [Tooltip("Z of that door. The crate stops short of it until the door is open.")]
        [SerializeField] private float gateZ = 111.8f;
        [SerializeField] private float gateClearance = 1.0f;

        [Header("Climb")]
        [Tooltip("Where climbing over puts you — ON THE STAIRS, not on the lid. A 1.6x1.2 lid is too " +
            "small to reliably land a capsule on, and the destination was always the flight anyway.")]
        [SerializeField] private Vector3 standPoint;

        [Header("Noise")]
        [SerializeField] private float noiseInterval = 0.85f;
        [SerializeField] private float noiseRadius = 15f;

        private readonly NetworkVariable<float> railZ = new NetworkVariable<float>();
        private readonly NetworkVariable<int> pusherCount = new NetworkVariable<int>();

        /// <summary>True once it is up against the blockage and can be climbed.</summary>
        public bool InPlace => railZ.Value >= railMaxZ - 0.35f;

        private readonly HashSet<ulong> pushers = new HashSet<ulong>();
        private readonly Dictionary<ulong, float> intent = new Dictionary<ulong, float>();
        private float noiseTimer;
        private static bool localAttached;
        private float lastSentIntent = -99f;
        private float lastRailZ;

        public override void OnNetworkSpawn()
        {
            if (IsServer && Mathf.Abs(railZ.Value) < 0.001f) railZ.Value = transform.position.z;
            lastRailZ = railZ.Value;
            railZ.OnValueChanged += (_, z) => Apply(z);
            Apply(railZ.Value);
        }

        private void Apply(float z)
        {
            var p = transform.position;
            transform.position = new Vector3(railX, p.y, z);
        }

        // ---- one prompt, three states ----

        public string GetPrompt()
        {
            if (localAttached) return "Let go";
            if (InPlace) return "Climb over";
            return "Grab the crate";
        }

        public bool CanInteract(ulong playerId) => true;

        public void Interact(ulong playerId)
        {
            // Climbing wins once it is in place, but only if you are not already holding it — so
            // "let go" is always reachable and you can never be locked to a crate you cannot release.
            if (!localAttached && InPlace)
            {
                ClimbServerRpc();
                return;
            }
            SetAttached(!localAttached);
        }

        private void SetAttached(bool on)
        {
            localAttached = on;
            var motor = LocalMotor();
            if (motor != null)
            {
                motor.PushAttached = on;
                motor.PushLoad = 0f;
            }
            lastSentIntent = -99f;
            AttachServerRpc(on);
            if (!on) IntentServerRpc(0f);
        }

        private static FirstPersonMotor LocalMotor()
        {
            var inv = PlayerInventory.Local;
            return inv != null ? inv.GetComponent<FirstPersonMotor>() : null;
        }

        // ---- local: feed intent, ride the crate ----

        private void Update()
        {
            float z = railZ.Value;
            float delta = z - lastRailZ;
            lastRailZ = z;

            if (!localAttached) return;
            var motor = LocalMotor();
            var reader = PlayerInputReader.Local;
            if (motor == null || reader == null) return;

            if ((motor.transform.position - transform.position).sqrMagnitude > grabRange * grabRange)
            {
                SetAttached(false);
                return;
            }

            // Forward AND back. Only pushing one way meant a crate nudged past its mark could never
            // be recovered, which on a rail with one useful end is a soft-locked run.
            float want = Mathf.Abs(reader.Move.y) > 0.15f ? Mathf.Sign(reader.Move.y) : 0f;
            if (!Mathf.Approximately(want, lastSentIntent))
            {
                lastSentIntent = want;
                IntentServerRpc(want);
            }

            motor.PushLoad = want != 0f ? loneLoad / Mathf.Max(1, pusherCount.Value) : 0f;

            // Dragged along by it rather than walking beside it.
            if (Mathf.Abs(delta) > 0.0001f)
            {
                var cc = motor.GetComponent<CharacterController>();
                if (cc != null) cc.Move(new Vector3(0f, 0f, delta));
                else motor.transform.position += new Vector3(0f, 0f, delta);
            }
        }

        private void OnDisable()
        {
            if (!localAttached) return;
            localAttached = false;
            var motor = LocalMotor();
            if (motor != null) { motor.PushAttached = false; motor.PushLoad = 0f; }
        }

        // ---- server ----

        [ServerRpc(RequireOwnership = false)]
        private void AttachServerRpc(bool on, ServerRpcParams p = default)
        {
            ulong who = p.Receive.SenderClientId;
            if (on) pushers.Add(who);
            else { pushers.Remove(who); intent.Remove(who); }
            pusherCount.Value = pushers.Count;
        }

        [ServerRpc(RequireOwnership = false)]
        private void IntentServerRpc(float dir, ServerRpcParams p = default)
        {
            ulong who = p.Receive.SenderClientId;
            if (!pushers.Contains(who)) return;
            intent[who] = Mathf.Clamp(dir, -1f, 1f);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ClimbServerRpc(ServerRpcParams p = default)
        {
            if (!InPlace) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.ConnectedClients.TryGetValue(p.Receive.SenderClientId, out var c)) return;
            if (c.PlayerObject == null) return;
            Place(c.PlayerObject.transform);
            ClimbedClientRpc(c.PlayerObject.OwnerClientId);
        }

        private void Place(Transform t)
        {
            var cc = t.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            t.position = standPoint;
            if (cc != null) cc.enabled = true;
        }

        [ClientRpc]
        private void ClimbedClientRpc(ulong who)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.LocalClientId != who) return;
            // The owning client authors its own position, so the server move alone gets snapped back.
            var po = nm.LocalClient.PlayerObject;
            if (po != null) Place(po.transform);
        }

        private void FixedUpdate()
        {
            if (!IsServer) return;

            // Everyone pulling the same way moves it; a disagreement cancels out, which is fair.
            float dir = 0f;
            foreach (var kv in intent) dir += kv.Value;
            dir = Mathf.Clamp(dir, -1f, 1f);

            if (Mathf.Abs(dir) < 0.01f)
            {
                noiseTimer = 0f;
                return;
            }

            float load = loneLoad / Mathf.Max(1, pusherCount.Value);
            float speed = walkSpeed * (1f - load);
            float ceiling = railMaxZ;
            if (gateDoor != null && !gateDoor.IsOpen) ceiling = Mathf.Min(ceiling, gateZ - gateClearance);
            float next = Mathf.Clamp(railZ.Value + dir * speed * Time.fixedDeltaTime, railMinZ, ceiling);
            if (Mathf.Approximately(next, railZ.Value)) return;
            railZ.Value = next;

            noiseTimer -= Time.fixedDeltaTime;
            if (noiseTimer <= 0f)
            {
                noiseTimer = noiseInterval;
                GameEvents.RaiseNoiseEmitted(transform.position, noiseRadius, NoiseSource.PuzzleInteraction);
            }
        }
    }
}
