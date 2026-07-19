using UnityEngine;

namespace LastWard.Entity
{
    [CreateAssetMenu(fileName = "EntityTuning", menuName = "The Last Ward/Entity Tuning")]
    public class EntityTuning : ScriptableObject
    {
        [Header("Movement speed per state")]
        public float patrolSpeed = 2.2f;
        public float investigateSpeed = 2.0f;
        public float searchSpeed = 2.4f;
        public float chaseSpeed = 4.2f;
        public float returnSpeed = 2.2f;

        [Header("Senses")]
        public float visionRange = 12f;
        [Range(0, 180)] public float visionAngleDegrees = 55f;
        public float hearingRadiusMultiplier = 1f;

        [Header("State timing")]
        public float lostTargetMemorySeconds = 3f;
        public float knowledgeChaseMemoryBonusSeconds = 2.5f;
        public float searchDurationSeconds = 12f;
        public float investigateIdleSeconds = 6f;

        [Header("Aggression tiers (driven by team total knowledge)")]
        public float[] aggressionTierThresholds = { 30f, 70f, 120f };
        public float[] aggressionTierSpeedMultiplier = { 1f, 1.1f, 1.2f, 1.3f };
        public float[] aggressionTierSearchMultiplier = { 1f, 1.15f, 1.3f, 1.5f };

        [Header("Mimicry")]
        public int mimicryMaxPerRun = 2;
    }
}
