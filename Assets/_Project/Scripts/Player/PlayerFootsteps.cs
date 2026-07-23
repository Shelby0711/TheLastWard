using LastWard.Audio;
using UnityEngine;

namespace LastWard.Player
{
    /// <summary>
    /// Footsteps for every copy of the player (position-derived, not input-derived) so all clients
    /// hear each other — important for co-op and for the Entity's tension.
    ///
    /// The available clips are continuous LOOPS of someone running (the gravel one is nearly two
    /// minutes long), not single footfalls. They are therefore played as one looping source gated on
    /// movement, not fired per stride: firing a multi-second loop on every step stacked dozens of
    /// overlapping copies, which produced a permanent running sound and enough voices to distort the
    /// whole mix.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class PlayerFootsteps : MonoBehaviour
    {
        [Tooltip("Below this speed the player counts as standing still.")]
        [SerializeField] private float moveThreshold = 0.6f;
        [SerializeField] private float sprintSpeedThreshold = 4f;
        [SerializeField] private float walkVolume = 0.4f;
        [SerializeField] private float sprintVolume = 0.75f;
        [SerializeField] private float fadeSpeed = 8f;

        private AudioSource source;
        private AudioClip interior;
        private AudioClip gravel;
        private Vector3 lastPosition;
        private float smoothedSpeed;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.volume = 0f;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 18f;

            interior = GameSfx.FootstepsInterior;
            gravel = GameSfx.FootstepsGravel;
            source.clip = interior;
            lastPosition = transform.position;
        }

        private void Update()
        {
            Vector3 delta = transform.position - lastPosition;
            delta.y = 0f;
            lastPosition = transform.position;

            float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            // Smoothed: raw per-frame deltas are noisy over the network and would chatter the audio
            // on and off for remote players.
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, Time.deltaTime * 10f);

            bool moving = smoothedSpeed > moveThreshold;
            bool sprinting = smoothedSpeed > sprintSpeedThreshold;

            // Gravel outside, boards inside — z=0 is the building face, the same boundary the level
            // geometry uses. Swapped only while silent, so the clip never cuts mid-stride.
            var wanted = transform.position.z < 0f && gravel != null ? gravel : interior;
            if (source.clip != wanted && source.volume < 0.02f)
            {
                source.clip = wanted;
                if (moving && wanted != null) source.Play();
            }

            float target = moving ? (sprinting ? sprintVolume : walkVolume) : 0f;
            source.volume = Mathf.MoveTowards(source.volume, target, Time.deltaTime * fadeSpeed);
            source.pitch = sprinting ? 1.15f : 0.92f;

            if (moving && source.clip != null && !source.isPlaying) source.Play();
            else if (!moving && source.volume <= 0.001f && source.isPlaying) source.Stop();
        }
    }
}
