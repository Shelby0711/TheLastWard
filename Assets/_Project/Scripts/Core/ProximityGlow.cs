using Unity.Netcode;
using UnityEngine;

namespace LastWard.Core
{
    /// <summary>
    /// Makes a small item pulse faintly once a player is close enough to reach it.
    ///
    /// The game is deliberately very dark, and small props — a fuse, a key, a battery on a floor —
    /// were being lost in it. That is frustration rather than tension: the player knows to search,
    /// they simply cannot see. The glow only appears at close range, so it never gives away where
    /// anything is from across a room; it just confirms what your hand is already on.
    ///
    /// Runs on every client and is never networked — it is a purely local readability aid, and
    /// syncing it would cost bandwidth to tell everyone something they can each work out.
    /// </summary>
    public class ProximityGlow : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Tooltip("Distance at which the pulse becomes visible. Roughly arm's reach plus a little.")]
        [SerializeField] private float glowRadius = 3.5f;
        [SerializeField] private Color glowColor = new Color(1f, 0.85f, 0.45f);
        [SerializeField] private float pulseSpeed = 2.4f;
        [SerializeField] private float maxIntensity = 0.9f;
        [Tooltip("Seconds between distance checks. Rechecking every frame for every prop in the " +
            "level is wasted work when the player cannot cross the radius that fast.")]
        [SerializeField] private float checkInterval = 0.25f;

        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private float nextCheck;
        private float closeness;   // 0 = out of range, 1 = right on top of it

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            block = new MaterialPropertyBlock();

            // A MaterialPropertyBlock can set _EmissionColor, but URP will not RENDER emission
            // unless the material has the _EMISSION keyword enabled — which is off by default. That
            // is why the glow appeared to do nothing at all. Enabling it per material instance here
            // is what actually makes the property visible.
            foreach (var r in renderers)
            {
                if (r == null) continue;
                foreach (var mat in r.materials)   // instances, so this cannot leak to shared assets
                {
                    if (mat == null) continue;
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }
        }

        private void Update()
        {
            if (renderers == null || renderers.Length == 0) return;

            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + checkInterval;
                closeness = ComputeCloseness();
            }

            // Faded rather than switched, so an item does not blink on as you step over a line.
            float pulse = 0.55f + Mathf.Sin(Time.time * pulseSpeed) * 0.45f;
            Color emission = glowColor * (closeness * pulse * maxIntensity);

            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionColorId, emission);
                r.SetPropertyBlock(block);
            }
        }

        private float ComputeCloseness()
        {
            var nm = NetworkManager.Singleton;
            Transform nearest = null;
            float best = float.MaxValue;

            if (nm != null && nm.IsListening && nm.LocalClient?.PlayerObject != null)
            {
                nearest = nm.LocalClient.PlayerObject.transform;
                best = Vector3.Distance(transform.position, nearest.position);
            }
            else
            {
                // Offline sandbox: fall back to the listener, which rides the player camera.
                var listener = FindAnyObjectByType<AudioListener>();
                if (listener == null) return 0f;
                best = Vector3.Distance(transform.position, listener.transform.position);
            }

            if (best > glowRadius) return 0f;
            // Squared so it comes up gently at the edge and is brightest in hand.
            float t = 1f - Mathf.Clamp01(best / glowRadius);
            return t * t;
        }
    }
}
