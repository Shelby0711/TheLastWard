using LastWard.Audio;
using LastWard.Core;
using LastWard.Knowledge;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Entity
{
    /// <summary>
    /// The Entity's sound, per client. Five layers, mostly 3D so distance and direction do the work:
    ///
    /// <list type="bullet">
    /// <item><b>Drone</b> — always on, quiet: "something is in the building".</item>
    /// <item><b>Movement</b> — footsteps scaled to real travel speed; how close, and how fast.</item>
    /// <item><b>State voice</b> — swaps with behaviour, so a listening player can hear the difference
    /// between it circling and it coming for them.</item>
    /// <item><b>Heartbeat</b> — 2D, the player's own, rising during a chase.</item>
    /// <item><b>Whisper</b> — only for the marked player; the "knowledge is expensive" tell.</item>
    /// </list>
    ///
    /// This is the counterplay for everything the Entity does out of sight. It stalks, stares and
    /// withdraws — none of which mean anything if the player cannot perceive them.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EntityAudio : MonoBehaviour
    {
        [SerializeField] private float droneVolume = 0.32f;
        [SerializeField] private float movementVolume = 0.55f;
        [SerializeField] private float voiceVolume = 0.6f;
        [SerializeField] private float heartbeatVolume = 0.65f;
        [Tooltip("The Entity's own breathing. Short range on purpose - it should only reach you when " +
            "it is genuinely close, so hearing it at all means it is already too late to be casual.")]
        [SerializeField] private float breathVolume = 0.5f;

        private AudioSource drone;
        private AudioSource movement;
        private AudioSource voice;
        private AudioSource heartbeat;
        private AudioSource whisper;
        private AudioSource breath;

        private EntityState state = EntityState.Patrol;
        private Vector3 lastPosition;
        private float nextChaseCry;

        private void Awake()
        {
            drone = GetComponent<AudioSource>();
            Configure(drone, GameSfx.EntityDrone, droneVolume, spatial: true, range: 26f);
            Play(drone);

            movement = Add(GameSfx.EntityFootsteps, 0f, spatial: true, range: 18f);
            Play(movement);

            voice = Add(null, 0f, spatial: true, range: 24f);

            // 2D — the player's own pulse, not a sound in the room.
            heartbeat = Add(GameSfx.Heartbeat, 0f, spatial: false, range: 0f);
            Play(heartbeat);

            whisper = Add(GameSfx.Whisper, 0f, spatial: true, range: 20f);
            Play(whisper);

            breath = Add(GameSfx.EntityBreathing, 0f, spatial: true, range: 15f);
            Play(breath);

            lastPosition = transform.position;
        }

        private AudioSource Add(AudioClip clip, float volume, bool spatial, float range)
        {
            var source = gameObject.AddComponent<AudioSource>();
            Configure(source, clip, volume, spatial, range);
            return source;
        }

        private void Configure(AudioSource s, AudioClip clip, float volume, bool spatial, float range)
        {
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.volume = volume;
            s.spatialBlend = spatial ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 2f;
            s.maxDistance = range > 0f ? range : 30f;
        }

        private static void Play(AudioSource s)
        {
            if (s != null && s.clip != null) s.Play();
        }

        private void OnEnable() => GameEvents.OnEntityStateChanged += OnStateChanged;
        private void OnDisable() => GameEvents.OnEntityStateChanged -= OnStateChanged;

        private void OnStateChanged(EntityState next)
        {
            state = next;

            // The voice layer is what makes states legible from another room.
            AudioClip clip = next switch
            {
                EntityState.Stalk => GameSfx.EntityStalk,
                EntityState.Stare => GameSfx.EntityStare,
                EntityState.Chase => GameSfx.EntityLurk,
                _ => null,
            };

            if (voice == null) return;
            if (clip == null) { voice.Stop(); return; }
            if (voice.clip == clip && voice.isPlaying) return;
            voice.clip = clip;
            voice.Play();
        }

        private void Update()
        {
            bool chasing = state == EntityState.Chase;

            // Footsteps track real travel speed — measured from the transform because clients don't
            // run the NavMeshAgent, so agent.velocity is zero everywhere but the host.
            float speed = Time.deltaTime > 0f
                ? (transform.position - lastPosition).magnitude / Time.deltaTime
                : 0f;
            lastPosition = transform.position;

            if (movement != null)
            {
                // MoveTowards so a stationary Entity actually goes silent — Lerp only approaches
                // zero, leaving footsteps faintly audible forever.
                movement.volume = Mathf.MoveTowards(movement.volume, Mathf.Clamp01(speed / 3f) * movementVolume,
                    Time.deltaTime * 2f);
                // Pitch rises hard when it runs — a charge is audible before it is visible, and
                // during a chase the footfalls should be coming faster than you can think.
                float targetPitch = Mathf.Clamp(0.8f + speed * 0.18f, 0.8f, 2.1f);
                if (chasing) targetPitch = Mathf.Max(targetPitch, 1.7f);
                movement.pitch = Mathf.Lerp(movement.pitch, targetPitch, Time.deltaTime * 6f);
                if (chasing) movement.volume = Mathf.Max(movement.volume, movementVolume);
            }

            if (voice != null)
                voice.volume = Mathf.MoveTowards(voice.volume, voice.isPlaying ? voiceVolume : 0f, Time.deltaTime * 1.2f);
            if (heartbeat != null)
            {
                heartbeat.volume = Mathf.MoveTowards(heartbeat.volume, chasing ? heartbeatVolume : 0f, Time.deltaTime * 1.2f);
                if (heartbeat.volume <= 0.005f && heartbeat.isPlaying) heartbeat.Pause();
                else if (heartbeat.volume > 0.005f && !heartbeat.isPlaying) heartbeat.UnPause();
            }

            // Cries and laughter pile in during a chase — irregularly, so it never settles into a
            // rhythm the player can tune out.
            if (chasing)
            {
                if (Time.time >= nextChaseCry)
                {
                    nextChaseCry = Time.time + Random.Range(3.5f, 8f);
                    var cry = GameSfx.Random(GameSfx.ChaseCries);
                    if (cry != null)
                        AudioSource.PlayClipAtPoint(cry, transform.position, 0.8f);
                }
            }
            else
            {
                nextChaseCry = Time.time + 1f;
            }

            if (breath != null)
            {
                // Always breathing; harder once it is coming for you. Distance is handled by the
                // source's own rolloff, so this only shapes intensity.
                breath.volume = Mathf.MoveTowards(breath.volume,
                    chasing ? breathVolume * 1.6f : breathVolume, Time.deltaTime * 1.5f);
            }

            bool localIsMarked = KnowledgeService.Instance != null &&
                                 NetworkManager.Singleton != null &&
                                 KnowledgeService.Instance.MarkedClientId == NetworkManager.Singleton.LocalClientId;
            if (whisper != null)
            {
                whisper.volume = Mathf.MoveTowards(whisper.volume, localIsMarked ? 0.5f : 0f, Time.deltaTime * 0.8f);
                if (whisper.volume <= 0.005f && whisper.isPlaying) whisper.Pause();
                else if (whisper.volume > 0.005f && !whisper.isPlaying) whisper.UnPause();
            }
        }
    }
}
