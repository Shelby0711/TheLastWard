using System.Collections.Generic;
using LastWard.Core;
using TMPro;
using UnityEngine;

namespace LastWard.UI
{
    public class ObjectiveUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;

        private static readonly Dictionary<ObjectiveStage, string> Labels = new Dictionary<ObjectiveStage, string>
        {
            { ObjectiveStage.Exterior, "Find shelter." },
            { ObjectiveStage.Lobby, "Restore power." },
            { ObjectiveStage.OrphanWard, "Push further into the ward." },
            { ObjectiveStage.ServiceCorridor, "Find a way through." },
            { ObjectiveStage.ExitRoute, "Get out." },
            { ObjectiveStage.Ended, string.Empty },
        };

        private void OnEnable() => GameEvents.OnObjectiveStageChanged += OnStageChanged;
        private void OnDisable() => GameEvents.OnObjectiveStageChanged -= OnStageChanged;

        private void OnStageChanged(ObjectiveStage stage)
        {
            if (text != null) text.text = Labels.TryGetValue(stage, out var label) ? label : string.Empty;
        }
    }
}
