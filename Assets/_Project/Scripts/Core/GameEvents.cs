using System;
using UnityEngine;

namespace LastWard.Core
{
    /// <summary>
    /// Local broadcast bus. Fires on every machine after server-authoritative state has already
    /// been synced (e.g. via NetworkVariable/ClientRpc) — this is not the sync mechanism itself,
    /// just the fan-out point systems use to react without referencing each other directly.
    /// playerId matches NGO's client id convention (see PROJECT_CONTEXT.md).
    /// </summary>
    public static class GameEvents
    {
        public static event Action<ulong, float> OnKnowledgeChanged;
        public static event Action<int> OnAggressionTierChanged;
        public static event Action<EntityState> OnEntityStateChanged;
        public static event Action<Vector3, float, NoiseSource> OnNoiseEmitted;
        public static event Action<ulong> OnPlayerDied;
        public static event Action<string, ulong> OnPuzzleStepCompleted;
        public static event Action<ObjectiveStage> OnObjectiveStageChanged;
        public static event Action<AftermathType, Vector3> OnAftermathTriggered;
        public static event Action<ulong> OnSpectatorTargetChanged;
        public static event Action OnSpectatorPing;

        public static void RaiseKnowledgeChanged(ulong playerId, float newScore) => OnKnowledgeChanged?.Invoke(playerId, newScore);
        public static void RaiseAggressionTierChanged(int newTier) => OnAggressionTierChanged?.Invoke(newTier);
        public static void RaiseEntityStateChanged(EntityState newState) => OnEntityStateChanged?.Invoke(newState);
        public static void RaiseNoiseEmitted(Vector3 position, float radius, NoiseSource source) => OnNoiseEmitted?.Invoke(position, radius, source);
        public static void RaisePlayerDied(ulong playerId) => OnPlayerDied?.Invoke(playerId);
        public static void RaisePuzzleStepCompleted(string puzzleId, ulong playerId) => OnPuzzleStepCompleted?.Invoke(puzzleId, playerId);
        public static void RaiseObjectiveStageChanged(ObjectiveStage stage) => OnObjectiveStageChanged?.Invoke(stage);
        public static void RaiseAftermathTriggered(AftermathType type, Vector3 position) => OnAftermathTriggered?.Invoke(type, position);
        public static void RaiseSpectatorTargetChanged(ulong watchedPlayerId) => OnSpectatorTargetChanged?.Invoke(watchedPlayerId);
        public static void RaiseSpectatorPing() => OnSpectatorPing?.Invoke();
    }
}
