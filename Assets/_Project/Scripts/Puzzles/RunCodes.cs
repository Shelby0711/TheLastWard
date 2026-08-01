using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// Every code in the building, derived from one number chosen when the run starts.
    ///
    /// The problem this solves: a player who has cleared the game once knows 3047 and 482 forever, and
    /// two of the three floors collapse into typing. Randomising the locks alone does not work — the
    /// answers are taught by notes, so a random code with fixed notes is simply unsolvable. <b>The
    /// codes and the documents that teach them have to come from the same source.</b>
    ///
    /// So the notes are written as templates with tokens, and this fills them in. One seed governs the
    /// lock, the paperwork, and the wrong answers the paperwork rules out — they cannot drift apart,
    /// because there is only one of them.
    ///
    /// What deliberately does NOT change is the *shape* of each puzzle: the gate is always four bed
    /// numbers read in register order, the dispensary is always licensed/present/signed-out. A veteran
    /// still knows where to look and what kind of answer they need. They just cannot skip the reading.
    /// That is the right relationship between mastery and a puzzle — faster, not exempt.
    /// </summary>
    public class RunCodes : NetworkBehaviour
    {
        public static RunCodes Instance { get; private set; }

        // Replicated so every client fills its notes identically. A client deriving its own from a
        // local Random would read different paperwork from the host and the group would argue about
        // a number neither of them typed wrong.
        private readonly NetworkVariable<int> seed = new NetworkVariable<int>();

        [Tooltip("Fixed seed for testing. 0 uses a random one per run.")]
        [SerializeField] private int debugSeed;

        // --- the ward gate: four distinct bed numbers, in the order the register lists them ---
        public int[] Beds { get; private set; } = { 3, 0, 4, 7 };
        public string GateCode => $"{Beds[0]}{Beds[1]}{Beds[2]}{Beds[3]}";

        // --- the dispensary: licensed, actually present, ever signed out ---
        public int Licensed { get; private set; } = 4;
        public int Present { get; private set; } = 8;
        public int SignedOut { get; private set; } = 2;
        public string DialCode => $"{Licensed}{Present}{SignedOut}";

        // --- the asylum's riddle gate ---
        public int Ward { get; private set; } = 19;
        public int Year { get; private set; } = 19;
        public string RiddleAnswer => $"{Ward:00}{Year:00}";

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
                seed.Value = debugSeed != 0 ? debugSeed : Random.Range(1, int.MaxValue);
            seed.OnValueChanged += (_, v) => Derive(v);
            Derive(seed.Value);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        private void Derive(int s)
        {
            if (s == 0) return;
            var rng = new System.Random(s);

            // Four distinct digits. Distinct because the register lists four separate beds, and a
            // repeated bed number would read as a typo in the fiction rather than as a code.
            var pool = new System.Collections.Generic.List<int>();
            for (int i = 0; i <= 9; i++) pool.Add(i);
            var beds = new int[4];
            for (int i = 0; i < 4; i++)
            {
                int k = rng.Next(pool.Count);
                beds[i] = pool[k];
                pool.RemoveAt(k);
            }
            Beds = beds;

            // The census has to stay internally true or the horror stops working: more people present
            // than the ward was licensed for, and fewer signed out than were present.
            Licensed = rng.Next(3, 7);                       // 3-6
            Present = Licensed + rng.Next(2, 5);             // always over capacity
            if (Present > 9) Present = 9;
            SignedOut = rng.Next(1, Mathf.Min(4, Present));  // always fewer than were there

            Ward = rng.Next(1, 8);
            Year = rng.Next(10, 100);

            Debug.Log($"[RunCodes] seed={s}  gate={GateCode}  dials={DialCode}  riddle={RiddleAnswer}");
        }

        /// <summary>
        /// Fills a note's tokens. Unknown tokens are left alone rather than blanked, so a typo in a
        /// template shows up as visible {NONSENSE} in the note instead of a silently missing clue.
        /// </summary>
        public string Fill(string body)
        {
            if (string.IsNullOrEmpty(body) || body.IndexOf('{') < 0) return body;
            var sb = new StringBuilder(body);
            sb.Replace("{BED0}", Beds[0].ToString());
            sb.Replace("{BED1}", Beds[1].ToString());
            sb.Replace("{BED2}", Beds[2].ToString());
            sb.Replace("{BED3}", Beds[3].ToString());
            // The numerical sort — the wrong answer the maintenance card exists to rule out.
            var sorted = (int[])Beds.Clone();
            System.Array.Sort(sorted);
            sb.Replace("{BEDS_SORTED}", $"{sorted[0]}{sorted[1]}{sorted[2]}{sorted[3]}");
            sb.Replace("{LICENSED}", Licensed.ToString());
            sb.Replace("{PRESENT}", Present.ToString());
            sb.Replace("{SIGNED}", SignedOut.ToString());
            sb.Replace("{UNRECORDED}", (Present - SignedOut).ToString());
            sb.Replace("{WARD}", Ward.ToString());
            sb.Replace("{YEAR}", Year.ToString("00"));
            return sb.ToString();
        }

        /// <summary>Safe accessor for puzzles that may run before this spawns.</summary>
        public static string Gate => Instance != null ? Instance.GateCode : "3047";
        public static string Dials => Instance != null ? Instance.DialCode : "482";
        public static string Riddle => Instance != null ? Instance.RiddleAnswer : "1919";
    }
}
