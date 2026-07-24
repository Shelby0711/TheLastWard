using LastWard.Audio;
using UnityEngine;

namespace LastWard.Core
{
    /// <summary>
    /// Makes the Service Corridor feel occupied without anything actually being there.
    ///
    /// The Entity itself is mostly absent by design (see <c>EntityState.Dormant</c>) — which risks
    /// the corridor feeling empty rather than tense. This fills that space with evidence: giant
    /// footfalls from somewhere ahead, a door slamming in a room you have not opened, crying that
    /// stops when you listen for it, and the tubes dropping to red.
    ///
    /// None of it is the Entity, and none of it can hurt the player. That is deliberate. If every
    /// sound means danger, players learn to read sound as a threat meter; if most sounds mean
    /// nothing, they can never stop listening.
    ///
    /// Runs per-client and is never synchronised — two players hearing different things is far more
    /// unsettling than a shared soundtrack, and "did you hear that?" / "no" is the best line the
    /// game can produce.
    /// </summary>
    public class HauntingDirector : MonoBehaviour
    {
        [Tooltip("Only haunts players inside this Z range — the Service Corridor and beyond.")]
        [SerializeField] private float activeFromZ = 20f;

        [Header("Timing")]
        [SerializeField] private float intervalMin = 26f;
        [SerializeField] private float intervalMax = 55f;

        [Header("Placement")]
        [Tooltip("How far away an event happens. Always out of sight, never on top of the player.")]
        [SerializeField] private float eventDistanceMin = 7f;
        [SerializeField] private float eventDistanceMax = 16f;

        [Header("Lights")]
        [Tooltip("Seconds the corridor tubes go red during an event.")]
        [SerializeField] private float redSeconds = 3.5f;
        [SerializeField] private Color redColor = new Color(1f, 0.12f, 0.08f);

        private float nextEvent;
        private bool entityHunting;
        private AudioListener listener;
        private FlickeringLight[] tubes;

        private void OnEnable() => GameEvents.OnEntityStateChanged += OnEntityState;
        private void OnDisable() => GameEvents.OnEntityStateChanged -= OnEntityState;

        private void OnEntityState(EntityState s) =>
            entityHunting = s == EntityState.Chase || s == EntityState.Stare;

        private void Start()
        {
            tubes = FindObjectsByType<FlickeringLight>(FindObjectsInactive.Include);
            Schedule();
        }

        private void Update()
        {
            if (listener == null) listener = FindAnyObjectByType<AudioListener>();
            if (listener == null) return;

            // Only where its presence is meant to be obvious. Earlier zones stay quiet so arriving
            // here is a change the player can feel.
            if (listener.transform.position.z < activeFromZ) return;

            // Never layer a phantom on top of the real thing. While it is actually chasing or
            // standing there watching you, the building shuts up: competing cues made both read as
            // noise, and it is what made the effects feel constant.
            if (entityHunting) { Schedule(); return; }

            if (Time.time < nextEvent) return;
            Schedule();
            FireEvent();
        }

        private void Schedule() => nextEvent = Time.time + Random.Range(intervalMin, intervalMax);

        private void FireEvent()
        {
            Vector3 origin = listener.transform.position;
            Vector2 flat = Random.insideUnitCircle.normalized *
                           Random.Range(eventDistanceMin, eventDistanceMax);
            Vector3 at = origin + new Vector3(flat.x, Random.Range(-0.5f, 1.5f), flat.y);

            switch (Random.Range(0, 5))
            {
                case 0:
                    // Something heavy walking, somewhere ahead.
                    Play(GameSfx.EntityFootsteps, at, 0.85f);
                    break;
                case 1:
                    // A door, in a room the player has not touched.
                    Play(GameSfx.DoorSlam, at, 0.9f);
                    break;
                case 2:
                    Play(Random.value < 0.5f ? GameSfx.Get("horror_cries_echoing")
                                             : GameSfx.Get("haunted-ghost-baby-crying"), at, 0.7f);
                    break;
                case 3:
                    // Girls muttering somewhere down the corridor. Nobody is there.
                    Play(GameSfx.GirlsMumbling, at, 0.75f);
                    break;
                default:
                    Play(GameSfx.Get("deep-evil-male-laugh"), at, 0.65f);
                    break;
            }

            // The lights answer whatever it was.
            if (Random.value < 0.6f) StartCoroutine(RedPulse());
        }

        private System.Collections.IEnumerator RedPulse()
        {
            if (tubes == null || tubes.Length == 0) yield break;

            var lights = new System.Collections.Generic.List<Light>();
            var originals = new System.Collections.Generic.List<Color>();
            foreach (var t in tubes)
            {
                if (t == null) continue;
                var l = t.GetComponent<Light>();
                if (l == null) continue;
                lights.Add(l);
                originals.Add(l.color);
                l.color = redColor;
            }

            yield return new WaitForSeconds(redSeconds);

            for (int i = 0; i < lights.Count; i++)
                if (lights[i] != null) lights[i].color = originals[i];
        }

        private static void Play(AudioClip clip, Vector3 at, float volume)
        {
            if (clip != null) AudioSource.PlayClipAtPoint(clip, at, volume);
        }
    }
}
