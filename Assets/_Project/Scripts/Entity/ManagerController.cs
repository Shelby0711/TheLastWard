using System.Collections;
using System.Collections.Generic;
using LastWard.Net;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Entity
{
    /// <summary>States for the Manager. Deliberately no Chase — it does not run.</summary>
    public enum ManagerState { Perch, Retreat, Gone }

    /// <summary>
    /// The Manager — the first-floor stalker, and the game's second entity. It is built to feel like
    /// nothing the Receptionist was: it <b>sees</b> rather than hears, it <b>never chases</b>, and it
    /// owns the surfaces and the dark corners.
    ///
    /// Increment 2 (MANAGER_ENTITY_PLAN.md): it perches at the end of the hall as a silhouette with
    /// glowing eyes, and the instant a player reaches its floor it does NOT wait to be walked up to —
    /// it crawls backward into the dark and is gone. That "I saw it, and then it wasn't there" is the
    /// whole beat. Roam/peek/manifest-kill land in later increments; for now it appears once, retreats
    /// and vanishes.
    /// </summary>
    public class ManagerController : NetworkBehaviour
    {
        [Tooltip("Below this height a player is downstairs (the Receptionist's floor) and the Manager " +
            "ignores them. It only reacts to players who have actually climbed to its floor.")]
        [SerializeField] private float firstFloorMinY = 2.5f;
        [Tooltip("How close a player on its floor has to be before it reacts. Wide, so it retreats as " +
            "you crest the stairs rather than waiting for you to walk up to it.")]
        [SerializeField] private float noticeRange = 35f;
        [Tooltip("Seconds of crawling-backward before it winks out.")]
        [SerializeField] private float retreatSeconds = 2.2f;
        [Tooltip("How far it slinks back over the retreat.")]
        [SerializeField] private float retreatDistance = 4f;

        private readonly NetworkVariable<ManagerState> netState =
            new NetworkVariable<ManagerState>(ManagerState.Perch);
        private readonly NetworkVariable<bool> gone = new NetworkVariable<bool>(false);

        private static readonly int RetreatParam = Animator.StringToHash("Retreat");

        private readonly List<Transform> players = new List<Transform>();
        private Renderer[] renderers;
        private Animator animator;
        private bool reacting;

        public ManagerState State => netState.Value;

        public override void OnNetworkSpawn()
        {
            animator = GetComponentInChildren<Animator>();

            // The permanent twitch is the Manager's alone — the Receptionist stays smooth.
            var driver = GetComponentInChildren<EntityAnimationDriver>();
            if (driver != null) driver.SetForceStutter(true);

            gone.OnValueChanged += OnGoneChanged;
            ApplyGone(gone.Value);   // late-joiners see it already vanished
        }

        public override void OnNetworkDespawn() => gone.OnValueChanged -= OnGoneChanged;

        private void OnGoneChanged(bool _, bool now) => ApplyGone(now);

        /// <summary>Hides/shows the body AND the eyes (which hang off the root, not under Visual).</summary>
        private void ApplyGone(bool isGone)
        {
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
                if (r != null) r.enabled = !isGone;
        }

        private void Update()
        {
            if (!IsServer || reacting || gone.Value) return;

            RefreshPlayers();
            foreach (var p in players)
            {
                if (p == null) continue;
                if (!p.TryGetComponent<PlayerNetworkState>(out var pns) || !pns.IsAlive) continue;
                if (p.position.y < firstFloorMinY) continue;                                  // still downstairs
                if (Vector3.Distance(p.position, transform.position) > noticeRange) continue; // not near enough

                reacting = true;
                netState.Value = ManagerState.Retreat;
                StartCoroutine(RetreatAndVanish(p));
                return;
            }
        }

        private IEnumerator RetreatAndVanish(Transform player)
        {
            RetreatClientRpc();   // crawl-backwards on every peer

            Vector3 start = transform.position;
            Vector3 away = transform.position - player.position;
            away.y = 0f;
            Vector3 dir = away.sqrMagnitude > 0.01f ? away.normalized : transform.forward;
            Vector3 target = start + dir * retreatDistance;

            float t = 0f;
            while (t < retreatSeconds)
            {
                t += Time.deltaTime;
                // Plain lerp (not a teleport) so the NetworkTransform interpolates it smoothly and you
                // watch it slink away, rather than it just blinking backward.
                transform.position = Vector3.Lerp(start, target, t / retreatSeconds);
                yield return null;
            }

            netState.Value = ManagerState.Gone;
            gone.Value = true;   // and now it simply is not there
        }

        [ClientRpc]
        private void RetreatClientRpc()
        {
            if (animator != null) animator.SetTrigger(RetreatParam);
        }

        private void RefreshPlayers()
        {
            players.Clear();
            if (NetworkManager.Singleton == null) return;
            foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
                if (c.PlayerObject != null) players.Add(c.PlayerObject.transform);
        }
    }
}
