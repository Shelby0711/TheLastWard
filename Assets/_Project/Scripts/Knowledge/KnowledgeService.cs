using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Knowledge
{
    /// <summary>
    /// Server-authoritative "knowledge is expensive" tracker. Per-player scores are hidden (never
    /// shown as raw numbers to players — only the debug HUD, and only the marked player gets the
    /// whisper tell). The Entity reads this to decide who to prefer, and team-total drives the
    /// aggression tier. Scores live only on the server; the single replicated value is which client
    /// is currently "marked" (highest knowledge), so clients can drive the whisper locally.
    /// </summary>
    public class KnowledgeService : NetworkBehaviour
    {
        public static KnowledgeService Instance { get; private set; }

        [SerializeField] private float decayPerSecond = 0.15f;
        [SerializeField] private float targetWeightK = 1.5f;
        [SerializeField] private float[] aggressionThresholds = { 30f, 70f, 120f };

        private readonly Dictionary<ulong, float> scores = new Dictionary<ulong, float>();
        private readonly NetworkVariable<ulong> markedClient =
            new NetworkVariable<ulong>(ulong.MaxValue);

        public ulong MarkedClientId => markedClient.Value;

        private void Awake() => Instance = this;

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        private void Update()
        {
            if (!IsServer) return;

            float dt = Time.deltaTime;
            float highest = 0f;
            ulong marked = ulong.MaxValue;

            var keys = new List<ulong>(scores.Keys);
            foreach (var id in keys)
            {
                float v = Mathf.Max(0f, scores[id] - decayPerSecond * dt);
                scores[id] = v;
                if (v > highest) { highest = v; marked = id; }
            }

            if (markedClient.Value != marked) markedClient.Value = marked;
        }

        /// <summary>Called on any client (e.g. NoteInteractable) to credit the local player.</summary>
        public void ReportLocalKnowledge(float amount)
        {
            if (amount <= 0f) return;
            ReportKnowledgeServerRpc(amount);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReportKnowledgeServerRpc(float amount, ServerRpcParams p = default)
        {
            AddScore(p.Receive.SenderClientId, amount);
        }

        public void AddScore(ulong clientId, float amount)
        {
            if (!IsServer) return;
            scores.TryGetValue(clientId, out var current);
            scores[clientId] = current + amount;
        }

        // --- server-side queries used by the Entity ---

        public float GetScore(ulong clientId)
        {
            scores.TryGetValue(clientId, out var v);
            return v;
        }

        public float GetTargetWeight(ulong clientId) => 1f + GetScore(clientId) * targetWeightK;

        public int GetAggressionTier()
        {
            float total = 0f;
            foreach (var v in scores.Values) total += v;
            int tier = 0;
            for (int i = 0; i < aggressionThresholds.Length; i++)
                if (total >= aggressionThresholds[i]) tier = i + 1;
            return tier;
        }

        public string GetDebugText()
        {
            if (!IsServer)
                return markedClient.Value == ulong.MaxValue ? "marked: none" : $"marked: client {markedClient.Value}";

            var sb = new StringBuilder();
            sb.AppendLine($"Aggression tier: {GetAggressionTier()}");
            foreach (var kv in scores)
            {
                string mark = kv.Key == markedClient.Value ? "  <-- MARKED" : "";
                sb.AppendLine($"client {kv.Key}: {kv.Value:0.0}{mark}");
            }
            return sb.ToString();
        }
    }
}
