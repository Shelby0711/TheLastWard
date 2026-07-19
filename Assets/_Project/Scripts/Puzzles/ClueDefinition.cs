using UnityEngine;

namespace LastWard.Puzzles
{
    [CreateAssetMenu(fileName = "ClueDefinition", menuName = "The Last Ward/Clue Definition")]
    public class ClueDefinition : ScriptableObject
    {
        public string clueId;
        public string displayTitle;
        [TextArea(3, 10)] public string bodyText;
        public float knowledgeValue = 2f;

        [Tooltip("Which spawn point pool this clue can appear at (shuffled per run).")]
        public string spawnPointPoolId;

        [Tooltip("Puzzle this clue feeds, if any.")]
        public string puzzleId;

        [Tooltip("If true, this clue's info is deliberately wrong and another clue exposes it.")]
        public bool isContradiction;
    }
}
