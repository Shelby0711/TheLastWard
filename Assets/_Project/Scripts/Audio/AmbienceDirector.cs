using LastWard.Core;
using UnityEngine;

namespace LastWard.Audio
{
    /// <summary>
    /// The building's voice. Two layers: a bed (ambience + wind) and occasional one-shot stingers —
    /// a distant cry, a laugh, something scuttling — placed at random points around the listener.
    ///
    /// The bed is an <b>exterior</b> sound: wind and open-air atmosphere belong to the yard, not to
    /// a sealed ward. It fades out as the player moves into the building, which also makes crossing
    /// the threshold audible — stepping inside should feel like a door closing behind you.
    ///
    /// The stingers are the point of the rest. A looping bed becomes inaudible within a minute; an
    /// irregular sound from an unpredictable direction keeps the player reading the space. They are
    /// never synchronised between clients — everyone hearing the same cry at the same instant would
    /// let players confirm it wasn't real.
    /// </summary>
    public class AmbienceDirector : MonoBehaviour
    {
        [Header("Exterior bed")]
        [SerializeField] private float bedVolume = 0.35f;
        [SerializeField] private float windVolume = 0.22f;
        [Tooltip("Z at which the bed is at full volume (out by the car).")]
        [SerializeField] private float fullVolumeZ = -14f;
        [Tooltip("Z at which it has faded out completely (inside the doors).")]
        [SerializeField] private float silentZ = 2f;
        [SerializeField] private float fadeSpeed = 1.5f;

        [Header("Stingers")]
        [Tooltip("Seconds between distant sounds, before progress scaling.")]
        [SerializeField] private float intervalMin = 22f;
        [SerializeField] private float intervalMax = 55f;
        [SerializeField] private float stingerVolume = 0.55f;
        [SerializeField] private float stingerDistanceMin = 8f;
        [SerializeField] private float stingerDistanceMax = 20f;

        private AudioSource bed;
        private AudioSource wind;
        private AudioListener listener;
        private float nextStinger;

        private void Start()
        {
            bed = CreateLoop(GameSfx.Ambience, bedVolume);
            wind = CreateLoop(GameSfx.Wind, windVolume);
            ScheduleNext();
        }

        private AudioSource CreateLoop(AudioClip clip, float volume)
        {
            if (clip == null) return null;
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = 0f;
            source.spatialBlend = 0f;   // 2D: the bed is everywhere outside, not from a point
            source.playOnAwake = false;
            source.Play();
            return source;
        }

        private void Update()
        {
            // Cached: FindAnyObjectByType every frame is needless work, and the listener only
            // changes when a player spawns or dies.
            if (listener == null) listener = FindAnyObjectByType<AudioListener>();

            ApplyExteriorFade();

            if (Time.time < nextStinger) return;
            ScheduleNext();
            PlayStinger();
        }

        private void ApplyExteriorFade()
        {
            // 1 out by the car, 0 once inside. The Lobby floor starts at z=0, so the bed is gone
            // shortly after the player crosses the threshold.
            float z = listener != null ? listener.transform.position.z : fullVolumeZ;
            float outside = 1f - Mathf.Clamp01(Mathf.InverseLerp(fullVolumeZ, silentZ, z));

            Fade(bed, bedVolume * outside);
            Fade(wind, windVolume * outside);
        }

        private void Fade(AudioSource source, float target)
        {
            if (source == null) return;

            // MoveTowards, not Lerp — Lerp only approaches its target, so the bed would stay
            // faintly audible deep inside the building forever.
            source.volume = Mathf.MoveTowards(source.volume, target, Time.deltaTime * fadeSpeed);

            if (source.volume <= 0.004f)
            {
                source.volume = 0f;
                if (source.isPlaying) source.Pause();
            }
            else if (!source.isPlaying)
            {
                source.UnPause();
                if (!source.isPlaying) source.Play();
            }
        }

        private void ScheduleNext()
        {
            // Tighten as the objective advances — the building gets louder the deeper you go.
            float progress = 0f;
            if (ObjectiveTracker.Instance != null)
                progress = Mathf.Clamp01((int)ObjectiveTracker.Instance.Stage / 4f);

            float scale = Mathf.Lerp(1f, 0.45f, progress);
            nextStinger = Time.time + Random.Range(intervalMin, intervalMax) * scale;
        }

        private void PlayStinger()
        {
            var clip = GameSfx.Random(GameSfx.DistantStingers);
            if (clip == null) return;

            Vector3 origin = listener != null ? listener.transform.position : transform.position;

            // Around the player, roughly on their level — a cry from directly overhead reads as a
            // bug rather than a building.
            Vector2 flat = Random.insideUnitCircle.normalized * Random.Range(stingerDistanceMin, stingerDistanceMax);
            Vector3 at = origin + new Vector3(flat.x, Random.Range(-1f, 2f), flat.y);

            AudioSource.PlayClipAtPoint(clip, at, stingerVolume);
        }
    }
}
