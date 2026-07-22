using UnityEngine;

namespace LastWard.Core
{
    /// <summary>
    /// A failing fluorescent tube. Runs locally on every machine — it's cosmetic, and syncing it
    /// would spend bandwidth to make everyone's dread identical, which is the opposite of useful.
    ///
    /// Two behaviours rather than one: a constant low buzz-flicker, plus occasional full dropouts
    /// where the tube dies for a beat. The dropouts are what actually unsettle — a steady strobe
    /// becomes wallpaper within a minute, while a light that sometimes goes out entirely keeps the
    /// player checking. Both intervals are randomised per light, so a corridor of them never falls
    /// into a rhythm.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class FlickeringLight : MonoBehaviour
    {
        [SerializeField] private float baseIntensity = 0.6f;
        [Tooltip("How much the steady buzz varies the brightness.")]
        [SerializeField] private float flickerAmount = 0.35f;
        [SerializeField] private float flickerSpeed = 14f;

        [Header("Dropouts")]
        [Tooltip("Seconds between full blackouts of this tube, randomised in this range.")]
        [SerializeField] private float dropoutIntervalMin = 6f;
        [SerializeField] private float dropoutIntervalMax = 22f;
        [SerializeField] private float dropoutDurationMin = 0.15f;
        [SerializeField] private float dropoutDurationMax = 1.4f;

        private Light target;
        private float noiseSeed;
        private float nextDropout;
        private float dropoutEndsAt;

        private void Awake()
        {
            target = GetComponent<Light>();
            // Per-instance offset, or every tube in the corridor pulses in unison.
            noiseSeed = Random.Range(0f, 100f);
            ScheduleNextDropout();
        }

        private void Update()
        {
            if (target == null) return;

            if (Time.time < dropoutEndsAt)
            {
                // Not quite zero — a dead-black tube reads as a missing asset rather than a dying one.
                target.intensity = baseIntensity * 0.04f;
                return;
            }

            if (Time.time >= nextDropout)
            {
                dropoutEndsAt = Time.time + Random.Range(dropoutDurationMin, dropoutDurationMax);
                ScheduleNextDropout();
                return;
            }

            float noise = Mathf.PerlinNoise(noiseSeed, Time.time * flickerSpeed);
            target.intensity = baseIntensity * (1f - flickerAmount + noise * flickerAmount * 2f);
        }

        private void ScheduleNextDropout() =>
            nextDropout = Time.time + Random.Range(dropoutIntervalMin, dropoutIntervalMax);
    }
}
