using LastWard.Audio;
using LastWard.Net;
using UnityEngine;

namespace LastWard.Player
{
    /// <summary>
    /// The player's own breathing. Three layers that cross-fade, never cut:
    ///
    /// <list type="bullet">
    /// <item><b>Idle</b> — normal breathing, slightly louder when standing still, because there is
    /// nothing else to hear and the silence should not be total.</item>
    /// <item><b>Walking</b> — the same normal breathing, quieter; walking is not exertion.</item>
    /// <item><b>Heavy</b> — only after <i>sustained</i> running, or after being frightened. Not from
    /// a couple of steps.</item>
    /// </list>
    ///
    /// The important behaviour is the recovery: exertion accumulates while sprinting and bleeds off
    /// afterwards, so heavy breathing eases back through normal breathing to quiet instead of
    /// stopping dead. Panting that snaps off the instant you release shift is the single most
    /// obvious way this kind of system reads as fake.
    ///
    /// Owner-only and 2D — hearing a teammate's panic would leak information the Entity is meant to
    /// own, and would turn a private tell into a shared resource.
    /// </summary>
    [RequireComponent(typeof(PlayerNetworkState))]
    public class PlayerBreathing : MonoBehaviour
    {
        [SerializeField] private FirstPersonMotor motor;
        [SerializeField] private PlayerNetworkState state;

        [Header("Exertion")]
        [Tooltip("Seconds of continuous sprinting before breathing turns heavy.")]
        [SerializeField] private float secondsToWinded = 3.5f;
        [Tooltip("How long it takes to get your breath back. Deliberately slower than losing it.")]
        [SerializeField] private float recoverySeconds = 9f;

        [Header("Fear")]
        [Tooltip("Discovery above this counts as having seen something, and breathing goes heavy.")]
        [SerializeField, Range(0f, 1f)] private float fearThreshold = 0.45f;

        [Header("Mix")]
        [SerializeField] private float idleVolume = 0.32f;
        [SerializeField] private float walkVolume = 0.18f;
        [SerializeField] private float heavyVolume = 0.7f;
        [Tooltip("Your own pulse, riding on top of the heavy breathing. It rises with the same heavy " +
            "level, so it pounds whenever you are winded or frightened - which the chase keeps pinned " +
            "high, so it covers the whole chase sequence too.")]
        [SerializeField] private float heartbeatVolume = 0.6f;
        [Tooltip("Cross-fade rate. Low so layers blend rather than switch.")]
        [SerializeField] private float blendSpeed = 0.7f;

        private AudioSource idle;    // normal breathing — standing still
        private AudioSource walk;    // normal breathing — moving, quieter
        private AudioSource heavy;   // winded or frightened
        private AudioSource heartbeat;   // your pulse, rising with the heavy layer

        /// <summary>0 = rested, 1 = fully winded. Rises while sprinting, bleeds off afterwards.</summary>
        private float exertion;

        private void Start()
        {
            idle = CreateLayer(GameSfx.BreathingIdle);
            walk = CreateLayer(GameSfx.BreathingWalk);
            heavy = CreateLayer(GameSfx.BreathingHeavy);
            heartbeat = CreateLayer(GameSfx.Heartbeat);
        }

        private AudioSource CreateLayer(AudioClip clip)
        {
            if (clip == null) return null;
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = 0f;
            source.spatialBlend = 0f;   // your own breathing is not positional
            source.playOnAwake = false;
            return source;
        }

        private void Update()
        {
            if (state != null && !state.IsLocalPlayer) return;

            // The dead do not breathe. Without this every layer kept looping through the death
            // screen and on into spectating, which undercuts the one moment the game most needs to
            // land. Faded rather than cut so it tails off as the screen goes.
            if (state != null && !state.IsAlive)
            {
                Blend(heavy, 0f);
                Blend(walk, 0f);
                Blend(idle, 0f);
                Blend(heartbeat, 0f);
                return;
            }

            // Holding your breath means exactly that: every layer goes, including the heartbeat,
            // which is the point — the silence is what you traded the timer for.
            if (state != null && state.IsHoldingBreath)
            {
                Blend(heavy, 0f);
                Blend(walk, 0f);
                Blend(idle, 0f);
                Blend(heartbeat, 0f);
                // Exertion still bleeds off while you hold, so waiting it out does recover you.
                exertion = Mathf.MoveTowards(exertion, 0f, Time.deltaTime / Mathf.Max(0.1f, recoverySeconds));
                return;
            }

            bool sprinting = motor != null && motor.IsSprinting && motor.IsMoving;
            bool moving = motor != null && motor.IsMoving && !sprinting;

            // Fear is its own source of breathlessness — seeing the Entity should wind you as surely
            // as running does, and it decays through the same recovery so the two blend.
            float fear = state != null ? state.Discovery : 0f;
            bool frightened = fear >= fearThreshold;

            if (sprinting || frightened)
            {
                float rate = 1f / Mathf.Max(0.1f, secondsToWinded);
                // Fear pushes exertion toward a floor rather than pinning it at full, so a scare
                // raises your breathing without sounding like a sprint.
                float ceiling = sprinting ? 1f : Mathf.Lerp(0.45f, 0.9f, Mathf.InverseLerp(fearThreshold, 1f, fear));
                exertion = Mathf.MoveTowards(exertion, ceiling, rate * Time.deltaTime);
            }
            else
            {
                exertion = Mathf.MoveTowards(exertion, 0f, Time.deltaTime / Mathf.Max(0.1f, recoverySeconds));
            }

            // Heavy takes over as exertion builds; the normal layers give way to it rather than
            // being silenced, so there is always breath present.
            float heavyLevel = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, exertion));
            float remainder = 1f - heavyLevel;
            float walkLevel = moving || sprinting ? remainder : 0f;
            float idleLevel = moving || sprinting ? 0f : remainder;

            Blend(heavy, heavyLevel * heavyVolume);
            Blend(walk, walkLevel * walkVolume);
            Blend(idle, idleLevel * idleVolume);
            // Pulse tracks the heavy layer, so it is loudest exactly when you are winded or hunted.
            Blend(heartbeat, heavyLevel * heartbeatVolume);
        }

        private void Blend(AudioSource source, float target)
        {
            if (source == null) return;

            // MoveTowards, not Lerp: Lerp only approaches its target asymptotically, so an "off"
            // layer never actually reaches zero and stays faintly audible for the whole run.
            source.volume = Mathf.MoveTowards(source.volume, target, Time.deltaTime * blendSpeed);

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
    }
}
