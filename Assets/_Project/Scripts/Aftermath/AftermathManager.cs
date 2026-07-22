using System.Collections.Generic;
using LastWard.Core;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Aftermath
{
    /// <summary>
    /// Server-authoritative body/message staging (bible §11, PROTOTYPE_PLAN.md §9). On a kill, the
    /// server picks one eligible template (weighted, filtered by the team's current aggression tier),
    /// spawns it at the nearest hand-placed AftermathAnchor, and raises GameEvents so local
    /// audio/UI can react.
    ///
    /// Plain MonoBehaviour, not NetworkBehaviour: it has no synced state of its own, it only spawns
    /// OTHER NetworkObjects. That matters for prefab registration — with NetworkConfig.ForceSamePrefabs
    /// enabled (the default), every dynamic prefab must be registered with NetworkManager BEFORE
    /// networking starts, or AddNetworkPrefab throws. A NetworkBehaviour's OnNetworkSpawn fires
    /// AFTER hosting/joining has already begun, which is too late. Start() on a plain MonoBehaviour
    /// runs at scene load — well before the player can click Host/Join — and is guaranteed to run
    /// after NetworkManager's own Awake (which sets NetworkManager.Singleton).
    /// </summary>
    public class AftermathManager : MonoBehaviour
    {
        public static AftermathManager Instance { get; private set; }

        [SerializeField] private AftermathTemplateDefinition[] templates;

        private void Awake() => Instance = this;

        private void Start()
        {
            if (NetworkManager.Singleton == null || templates == null) return;
            var prefabs = NetworkManager.Singleton.NetworkConfig?.Prefabs;
            if (prefabs == null) return;
            foreach (var t in templates)
            {
                if (t == null || t.scenePrefab == null) continue;
                // The Editor auto-populates Assets/DefaultNetworkPrefabs.asset with every prefab
                // that has a NetworkObject — which already covers these three. Registering again
                // logs "duplicate GlobalObjectIdHash source entry" errors, so only add what's
                // genuinely missing (still needed for builds/configs where that asset isn't used).
                if (!prefabs.Contains(t.scenePrefab))
                    NetworkManager.Singleton.AddNetworkPrefab(t.scenePrefab);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Server-only (self-checked). Call right after a kill with the victim's death
        /// position and the team's current aggression tier.</summary>
        public void ServerTriggerAftermath(Vector3 deathPosition, int aggressionTier)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (templates == null || templates.Length == 0) return;

            var eligible = new List<AftermathTemplateDefinition>();
            float totalWeight = 0f;
            foreach (var t in templates)
            {
                if (t == null || t.scenePrefab == null) continue;
                bool tierOk = aggressionTier >= t.minAggressionTier &&
                              (t.maxAggressionTier < 0 || aggressionTier <= t.maxAggressionTier);
                if (!tierOk) continue;
                eligible.Add(t);
                totalWeight += Mathf.Max(0f, t.selectionWeight);
            }
            if (eligible.Count == 0 || totalWeight <= 0f) return;

            float roll = Random.Range(0f, totalWeight);
            AftermathTemplateDefinition chosen = eligible[eligible.Count - 1];
            foreach (var t in eligible)
            {
                roll -= Mathf.Max(0f, t.selectionWeight);
                if (roll <= 0f) { chosen = t; break; }
            }

            Vector3 spawnPos = FindNearestAnchor(deathPosition);
            var instance = Instantiate(chosen.scenePrefab, spawnPos, Quaternion.identity);
            instance.GetComponent<NetworkObject>().Spawn(true);

            GameEvents.RaiseAftermathTriggered(chosen.type, spawnPos);
        }

        private static Vector3 FindNearestAnchor(Vector3 position)
        {
            var anchors = FindObjectsByType<AftermathAnchor>();
            if (anchors.Length == 0) return position;

            Transform best = anchors[0].transform;
            float bestDist = Vector3.Distance(position, best.position);
            for (int i = 1; i < anchors.Length; i++)
            {
                float d = Vector3.Distance(position, anchors[i].transform.position);
                if (d < bestDist) { bestDist = d; best = anchors[i].transform; }
            }
            return best.position;
        }
    }
}
