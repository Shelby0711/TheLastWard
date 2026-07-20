using LastWard.Audio;
using UnityEngine;

namespace LastWard.Player
{
    /// <summary>
    /// Plays footsteps from horizontal movement, on every copy of the player (position-derived, not
    /// input-derived) so all clients hear each other — important for co-op/Entity tension. Faster
    /// movement shortens the stride and raises pitch/volume, so sprinting reads as sprinting.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class PlayerFootsteps : MonoBehaviour
    {
        [SerializeField] private float walkStride = 2.2f;
        [SerializeField] private float sprintSpeedThreshold = 4f;

        private AudioSource source;
        private AudioClip footstep;
        private Vector3 lastPosition;
        private float accumulated;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 18f;
            footstep = ProceduralSfx.Footstep();
            lastPosition = transform.position;
        }

        private void Update()
        {
            Vector3 delta = transform.position - lastPosition;
            delta.y = 0f;
            lastPosition = transform.position;

            float dist = delta.magnitude;
            float speed = dist / Mathf.Max(Time.deltaTime, 0.0001f);
            if (speed < 0.5f) { accumulated = 0f; return; }

            accumulated += dist;
            bool sprinting = speed > sprintSpeedThreshold;
            float stride = sprinting ? walkStride * 0.72f : walkStride;
            if (accumulated >= stride)
            {
                accumulated = 0f;
                source.pitch = Random.Range(0.9f, 1.1f) * (sprinting ? 1.15f : 1f);
                source.PlayOneShot(footstep, sprinting ? 0.9f : 0.55f);
            }
        }
    }
}
