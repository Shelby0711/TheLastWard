using UnityEngine;

namespace LastWard.Knowledge
{
    [CreateAssetMenu(fileName = "KnowledgeConfig", menuName = "The Last Ward/Knowledge Config")]
    public class KnowledgeConfig : ScriptableObject
    {
        [Header("Score sources")]
        public float noteReadValue = 2f;
        public float puzzleStepValue = 5f;
        public float newAreaValue = 8f;
        public float firstIntoZoneValue = 3f;

        [Header("Decay")]
        public float decayRatePerSecond = 0.05f;
        public float decayFloor = 5f;

        [Header("Entity targeting")]
        public float targetWeightK = 1.5f;
        public float whisperTellRange = 20f;
    }
}
