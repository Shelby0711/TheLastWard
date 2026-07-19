using LastWard.Core;
using UnityEngine;

namespace LastWard.Puzzles
{
    [CreateAssetMenu(fileName = "PuzzleStepDefinition", menuName = "The Last Ward/Puzzle Step Definition")]
    public class PuzzleStepDefinition : ScriptableObject
    {
        public string puzzleId;
        public string displayName;
        public string[] requiredClueIds;

        [Tooltip("Door/area id this puzzle unlocks on completion.")]
        public string gatesAreaId;

        public float noiseRadius = 10f;
        public NoiseSource noiseSource = NoiseSource.PuzzleInteraction;
        public float knowledgeValueOnComplete = 5f;
    }
}
