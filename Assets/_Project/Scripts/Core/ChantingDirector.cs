using LastWard.Audio;
using UnityEngine;

namespace LastWard.Core
{
    /// <summary>
    /// The chanting that starts once you begin taking the corridor apart.
    ///
    /// It arms the first time a player does something acquisitive in the Entity's territory — opens a
    /// container, works a lock, reads a note (all of which raise a
    /// <see cref="NoiseSource.PuzzleInteraction"/> noise). From then on it plays at irregular
    /// intervals, close by, for the rest of the run.
    ///
    /// That timing is the point: the sound is a direct consequence of the players' own searching, so
    /// it reads as "we have been noticed" rather than as ambience. Nothing about it is a threat on its
    /// own — it never damages, never tracks — but it is the building's answer to being pulled apart,
    /// and it arrives at the exact moment the players commit to doing so.
    ///
    /// Per-client and unsynchronised, like <see cref="HauntingDirector"/>: two players hearing it at
    /// different moments is far worse than a shared cue.
    /// </summary>
    public class ChantingDirector : MonoBehaviour
    {
        [Tooltip("Only arms past this Z — the Service Corridor and beyond. Searching the Lobby is " +
            "meant to stay quiet; this belongs to its territory.")]
        [SerializeField] private float activeFromZ = 20f;

        [Header("Timing")]
        [SerializeField] private float firstDelayMin = 6f;
        [SerializeField] private float firstDelayMax = 14f;
        [SerializeField] private float intervalMin = 22f;
        [SerializeField] private float intervalMax = 48f;

        [Header("Placement")]
        [Tooltip("How far off it is chanted from. Close enough to be in the room with you, never " +
            "close enough to have a source you could walk up to.")]
        [SerializeField] private float distanceMin = 5f;
        [SerializeField] private float distanceMax = 12f;
        [SerializeField] private float volume = 0.75f;

        private bool armed;
        private float nextChant;
        private AudioListener listener;

        private void OnEnable() => GameEvents.OnNoiseEmitted += OnNoise;
        private void OnDisable() => GameEvents.OnNoiseEmitted -= OnNoise;

        private void OnNoise(Vector3 position, float radius, NoiseSource source)
        {
            if (armed || source != NoiseSource.PuzzleInteraction) return;
            if (position.z < activeFromZ) return;
            armed = true;
            nextChant = Time.time + Random.Range(firstDelayMin, firstDelayMax);
        }

        private void Update()
        {
            if (!armed) return;
            if (listener == null) listener = FindAnyObjectByType<AudioListener>();
            if (listener == null || Time.time < nextChant) return;

            nextChant = Time.time + Random.Range(intervalMin, intervalMax);

            Vector2 flat = Random.insideUnitCircle.normalized * Random.Range(distanceMin, distanceMax);
            Vector3 at = listener.transform.position + new Vector3(flat.x, Random.Range(-0.5f, 1.5f), flat.y);
            var clip = GameSfx.DemonChanting;
            if (clip != null) AudioSource.PlayClipAtPoint(clip, at, volume);
        }
    }
}
