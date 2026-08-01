using System.Collections.Generic;
using UnityEngine;

namespace LastWard.Core
{
    /// <summary>
    /// A light in the world that the entities can reason about — a lit candle, in practice.
    ///
    /// The Manager already had a notion of <c>lit</c>, but it meant one specific thing: "the player is
    /// pointing a torch at me". There was no concept of a player simply <i>standing in light they did
    /// not carry</i>, because until the asylum every light source in the game was either a fixed
    /// prop nobody could change or a torch attached to a player.
    ///
    /// Candles need exactly that distinction. They are not a beacon you carry, so they must not add
    /// the torch's carry penalty — but standing in the pool of one has to be as damning as sweeping a
    /// torch across the room, or "safe to be near, fatal to stand in" is just words.
    ///
    /// A flat list rather than a spatial structure: there are ten candles on the floor and four
    /// players. Anything cleverer would be slower.
    /// </summary>
    public class WorldLight : MonoBehaviour
    {
        private static readonly List<WorldLight> Active = new List<WorldLight>();

        [Tooltip("How far the pool reaches. Should match what the player can SEE lit, or the rule " +
            "stops being readable — being burned by light you cannot see is indistinguishable from " +
            "a bug.")]
        [SerializeField] private float radius = 3.0f;

        public float Radius => radius;

        // Registration follows enabled state, so a candle burning out or being switched off by the
        // server drops out of the entities' senses on the same frame its glow disappears.
        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        /// <summary>True if <paramref name="position"/> stands inside any active pool of light.</summary>
        public static bool IsLit(Vector3 position)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                var l = Active[i];
                if (l == null) continue;
                if ((l.transform.position - position).sqrMagnitude <= l.radius * l.radius)
                    return true;
            }
            return false;
        }

        /// <summary>0..1 by how deep into the brightest pool the position sits. Centre is worst.</summary>
        public static float LitAmount(Vector3 position)
        {
            float worst = 0f;
            for (int i = 0; i < Active.Count; i++)
            {
                var l = Active[i];
                if (l == null || l.radius <= 0.001f) continue;
                float d = (l.transform.position - position).magnitude;
                if (d >= l.radius) continue;
                worst = Mathf.Max(worst, 1f - d / l.radius);
            }
            return worst;
        }
    }
}
