using LastWard.Audio;
using LastWard.Core;
using LastWard.Knowledge;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Entity
{
    /// <summary>
    /// Per-client Entity audio (runs on every copy, not just the server): a constant low drone that
    /// attenuates by distance ("heard before seen"), a heartbeat layer that fades in during Chase,
    /// and a whisper layer that only the marked/highest-knowledge player hears as the Entity nears —
    /// the "knowledge is expensive" tell that also makes targeting legible.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EntityAudio : MonoBehaviour
    {
        private AudioSource ambient;
        private AudioSource heartbeat;
        private AudioSource whisper;
        private bool chasing;

        private void Awake()
        {
            ambient = GetComponent<AudioSource>();
            Configure(ambient, ProceduralSfx.EntityAmbient(), 0.5f);
            ambient.Play();

            heartbeat = gameObject.AddComponent<AudioSource>();
            Configure(heartbeat, ProceduralSfx.Heartbeat(), 0f);
            heartbeat.Play();

            whisper = gameObject.AddComponent<AudioSource>();
            Configure(whisper, ProceduralSfx.Whisper(), 0f);
            whisper.maxDistance = 22f;
            whisper.Play();
        }

        private void Configure(AudioSource s, AudioClip clip, float volume)
        {
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.volume = volume;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 2f;
            s.maxDistance = 30f;
        }

        private void OnEnable() => GameEvents.OnEntityStateChanged += OnStateChanged;
        private void OnDisable() => GameEvents.OnEntityStateChanged -= OnStateChanged;

        private void OnStateChanged(EntityState state) => chasing = state == EntityState.Chase;

        private void Update()
        {
            heartbeat.volume = Mathf.Lerp(heartbeat.volume, chasing ? 0.7f : 0f, Time.deltaTime * 3f);

            // Whisper only for the local player if they're the marked one; 3D attenuation on this
            // source handles the "as it gets closer" part.
            bool localIsMarked = KnowledgeService.Instance != null &&
                                 NetworkManager.Singleton != null &&
                                 KnowledgeService.Instance.MarkedClientId == NetworkManager.Singleton.LocalClientId;
            whisper.volume = Mathf.Lerp(whisper.volume, localIsMarked ? 0.8f : 0f, Time.deltaTime * 2f);
        }
    }
}
