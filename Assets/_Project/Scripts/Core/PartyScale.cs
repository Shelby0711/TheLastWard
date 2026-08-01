using Unity.Netcode;
using UnityEngine;

namespace LastWard.Core
{
    /// <summary>
    /// How hard the building leans on you, given how many of you are left.
    ///
    /// The asylum's whole design assumes labour is divided: one player reads, another carries the
    /// matches, a third watches the corridor. A solo player does all of it, so their knowledge score
    /// is the sum of what four people would have split — and since the Inspector hunts whoever knows
    /// the most, solo play marks you the instant you arrive and kills you for having played the game.
    ///
    /// This is scaled on LIVING players, not lobby size, which matters more than it looks: a
    /// four-player run reduced to one survivor has exactly the solo problem, and the last one alive
    /// is by then carrying everything the dead knew. Easing off as the party thins is the same rule
    /// as easing off for solo, not a special case bolted next to it.
    ///
    /// It deliberately does NOT touch the knowledge score itself. The score is the honest record of
    /// what you know and other systems read it; what changes is how hard the entities press on it.
    /// </summary>
    public static class PartyScale
    {
        /// <summary>Living players right now. Never below 1, so callers can divide by it.</summary>
        public static int Living
        {
            get
            {
                var nm = NetworkManager.Singleton;
                if (nm == null) return 1;
                int n = 0;
                foreach (var c in nm.ConnectedClientsList)
                {
                    var po = c.PlayerObject;
                    if (po == null) continue;
                    var pns = po.GetComponent<LastWard.Net.PlayerNetworkState>();
                    if (pns == null || pns.IsAlive) n++;
                }
                return Mathf.Max(1, n);
            }
        }

        /// <summary>
        /// Multiplier applied to how fast entities sense and how quickly they act.
        /// Solo 0.70, two 0.85, three or more 1.0 — a 30% reprieve alone, tapering to none.
        /// </summary>
        public static float Danger
        {
            get
            {
                switch (Living)
                {
                    case 1: return 0.70f;
                    case 2: return 0.85f;
                    default: return 1f;
                }
            }
        }

        /// <summary>
        /// Multiplier for durations that should get LONGER as danger drops — a doom clock, a grace
        /// period. Simply the inverse, so one number governs both directions and they can never
        /// drift apart.
        /// </summary>
        public static float Grace => 1f / Danger;
    }
}
