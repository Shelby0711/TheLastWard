using UnityEngine;

namespace LastWard.Audio
{
    [CreateAssetMenu(fileName = "AudioEventDefinition", menuName = "The Last Ward/Audio Event")]
    public class AudioEventDefinition : ScriptableObject
    {
        public string eventId;
        public AudioClip[] clipPool;

        [Tooltip("Mixer snapshot to blend to when this event fires, if any (Explore/Stalked/Chase/DeadView).")]
        public string mixerSnapshotName;

        [Range(0f, 1f)] public float volume = 1f;
        [Range(0f, 1f)] public float spatialBlend = 1f;

        [Tooltip("For randomized ambience stingers; ignored for one-shot events.")]
        public float minIntervalSeconds;
        public float maxIntervalSeconds;
    }
}
