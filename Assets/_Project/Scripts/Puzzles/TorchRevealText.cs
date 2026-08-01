// Split out of its original combined file. Unity resolves a MonoBehaviour's MonoScript by FILENAME:
// a component class that does not match the file it lives in cannot be serialised into a scene, so
// AddComponent at build time produced a component that arrived broken and silently did nothing.
// Every MonoBehaviour therefore gets its own file.
using LastWard.Core;
using LastWard.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// Writing that only exists under a torch beam.
    ///
    /// This is the floor's cruellest idea and its most honest one: the Manager is drawn to light more
    /// than anything else, and the only way to read this is to stand still and shine a torch directly
    /// at it. The puzzle demands the exact behaviour that gets you killed. Standing here long enough
    /// to memorise a number is a genuine gamble, and it is meant to be — split the job with someone
    /// watching the hall, or make several short trips.
    ///
    /// Purely local: it is a property of looking, so there is nothing to replicate.
    /// </summary>
    public class TorchRevealText : MonoBehaviour
    {
        [SerializeField] private Renderer revealed;
        [SerializeField] private float range = 6f;
        [SerializeField, Range(5f, 60f)] private float halfAngle = 22f;
        [SerializeField] private float fadeSpeed = 4f;

        private float shown;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Start() { if (revealed != null) SetAlpha(0f); }

        private void Update()
        {
            if (revealed == null) return;
            bool lit = IsLitByLocalTorch();
            shown = Mathf.MoveTowards(shown, lit ? 1f : 0f, Time.deltaTime * fadeSpeed);
            SetAlpha(shown);
        }

        private bool IsLitByLocalTorch()
        {
            var state = LastWard.Net.PlayerNetworkState.LocalInstance;
            if (state == null || !state.FlashlightOn) return false;
            var pivot = state.CameraPivot;
            if (pivot == null) return false;

            Vector3 to = transform.position - pivot.position;
            if (to.magnitude > range) return false;
            return Vector3.Angle(pivot.forward, to) <= halfAngle;
        }

        private void SetAlpha(float a)
        {
            var mpb = new MaterialPropertyBlock();
            revealed.GetPropertyBlock(mpb);
            // Emissive-ish: it should look like something the beam is finding, not a lamp.
            mpb.SetColor(BaseColorId, new Color(0.55f, 0.05f, 0.04f) * a);
            revealed.SetPropertyBlock(mpb);
        }
    }
}
