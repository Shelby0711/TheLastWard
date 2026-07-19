using LastWard.Core;
using UnityEngine;

namespace LastWard.Aftermath
{
    [CreateAssetMenu(fileName = "AftermathTemplateDefinition", menuName = "The Last Ward/Aftermath Template")]
    public class AftermathTemplateDefinition : ScriptableObject
    {
        public string templateId;
        public AftermathType type;

        [Tooltip("Team aggression tier range this template is eligible in. -1 = unbounded.")]
        public int minAggressionTier = 0;
        public int maxAggressionTier = -1;

        public GameObject scenePrefab;

        [Tooltip("Relative weight when the server randomly picks a template for a death.")]
        public float selectionWeight = 1f;
    }
}
