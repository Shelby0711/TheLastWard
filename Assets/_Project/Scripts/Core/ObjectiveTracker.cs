using Unity.Netcode;

namespace LastWard.Core
{
    /// <summary>
    /// Server-authoritative shared party objective (which zone the group is working toward), per
    /// ObjectiveStage in GameEnums.cs. One-way: stages only ever advance, never regress, since this
    /// represents shared party progress rather than any one player's state. Zone-entry triggers and
    /// puzzle completions call ServerAdvanceTo.
    /// </summary>
    public class ObjectiveTracker : NetworkBehaviour
    {
        public static ObjectiveTracker Instance { get; private set; }

        private readonly NetworkVariable<ObjectiveStage> stage =
            new NetworkVariable<ObjectiveStage>(ObjectiveStage.Exterior);

        public ObjectiveStage Stage => stage.Value;

        private void Awake() => Instance = this;

        public override void OnNetworkSpawn()
        {
            stage.OnValueChanged += OnStageChanged;
            GameEvents.RaiseObjectiveStageChanged(stage.Value);
        }

        public override void OnNetworkDespawn()
        {
            stage.OnValueChanged -= OnStageChanged;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        private void OnStageChanged(ObjectiveStage previous, ObjectiveStage current) =>
            GameEvents.RaiseObjectiveStageChanged(current);

        /// <summary>Server-only. Advances the shared stage if `next` is further along than current.</summary>
        public void ServerAdvanceTo(ObjectiveStage next)
        {
            if (!IsServer) return;
            if (next > stage.Value) stage.Value = next;
        }
    }
}
