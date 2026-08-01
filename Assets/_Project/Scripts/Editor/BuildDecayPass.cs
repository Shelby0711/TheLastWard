#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace LastWard.EditorTools
{
    /// <summary>
    /// The pass that makes the building look forty years abandoned instead of freshly greyboxed.
    ///
    /// It runs last and reads the scene rather than a coordinate table. Every floor slab in the game
    /// is a named box, so the pass finds them, measures them, and dresses their edges — which means
    /// it covers all four floors including any room added later, and it cannot drift out of sync
    /// with the geometry the way a hand-written list of positions does.
    ///
    /// <b>Where things go, and why.</b> Rubbish accumulates against walls and in corners, never in
    /// the middle of a room, for two reasons that happen to agree: it is where it collects in real
    /// buildings, and the middle of every room in this game is a lane somebody is being chased down.
    /// Anything solid enough to trip over is confined to corners, which are never doorways in this
    /// layout. Edges get flat decals, which nothing can catch on at all.
    /// </summary>
    public static partial class BuildM5Level
    {
        // Big enough that the room reads as neglected, small enough that no corridor turns into an
        // obstacle course. Tuned against the 3m-wide first-floor corridor, which is the tightest
        // space in the game.
        private const int CornerPropsMax = 3;
        private const float EdgeInset = 0.55f;

        private static void ApplyDecayPass()
        {
            DecayKit.Reset();

            // Idempotent: the whole pass lives under one root, so re-running it on an already-built
            // scene replaces the dressing instead of stacking a second copy on top of the first.
            var existing = GameObject.Find("Decay");
            if (existing != null) UnityObject.DestroyImmediate(existing);
            var parent = new GameObject("Decay").transform;

            var slabs = new List<(string name, Bounds b)>();
            foreach (var r in UnityObject.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string n = r.gameObject.name;
                // "_Floor" as a suffix covers three of the four floors; the Morgue names its cells
                // "TF_Floor_c r" instead. Excluding "Fall" keeps the bloodfalls out of it — they
                // are wall pieces that happen to be named after what runs down them.
                bool isFloor = n.EndsWith("_Floor") || (n.StartsWith("TF_Floor_") && !n.Contains("Fall"));
                if (!isFloor) continue;

                var b = r.bounds;
                if (b.size.x < 2f || b.size.z < 2f) continue;   // stair treads and thresholds
                slabs.Add((n, b));
            }

            if (slabs.Count == 0)
            {
                Debug.LogWarning("[Decay] No floor slabs found — nothing dressed.");
                return;
            }

            // Corner tests below query colliders, and the level was built by moving transforms
            // around without a physics step in between.
            Physics.SyncTransforms();

            int props = 0, decals = 0, webs = 0, skipped = 0;
            foreach (var (name, b) in slabs)
            {
                int seed = name.GetHashCode();
                var rng = new System.Random(seed);
                float y = b.max.y;
                float hx = b.size.x / 2f - EdgeInset;
                float hz = b.size.z / 2f - EdgeInset;
                if (hx <= 0.2f || hz <= 0.2f) continue;

                var centre = new Vector3(b.center.x, y, b.center.z);
                float area = b.size.x * b.size.z;

                // --- corners: the solid stuff -------------------------------------------------
                (float sx, float sz)[] corners = { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) };
                for (int i = corners.Length - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (corners[i], corners[j]) = (corners[j], corners[i]);
                }

                int cornerCount = Mathf.Clamp(Mathf.RoundToInt(area / 34f), 1, CornerPropsMax);
                for (int i = 0; i < cornerCount; i++)
                {
                    var (sx, sz) = corners[i % corners.Length];
                    var spot = new Vector3(centre.x + sx * (hx - 0.25f), y, centre.z + sz * (hz - 0.25f));
                    // A "corner" is only a corner if two walls actually meet there. Slabs butt onto
                    // open thresholds, stairheads and the exterior, and a barrel standing in the
                    // middle of a doorway is worse than no barrel at all.
                    if (!IsEnclosedCorner(spot, sx, sz)) { skipped++; continue; }
                    DecayKit.ScatterWaste(parent, spot, 0.7f, 1 + rng.Next(2), seed + i * 31);
                    DecayKit.Grime(parent, spot, Mathf.Lerp(1.1f, 2.2f, (float)rng.NextDouble()),
                        (float)rng.NextDouble() * 360f);
                    props++;
                }

                // --- edges: litter, and real rubbish wherever there is a wall to pile it against
                int litter = Mathf.Clamp(Mathf.RoundToInt(area / 9f), 4, 18);
                for (int i = 0; i < litter; i++)
                {
                    bool alongX = rng.Next(2) == 0;
                    float t = (float)rng.NextDouble() * 2f - 1f;
                    float side = rng.Next(2) == 0 ? -1f : 1f;
                    var spot = alongX
                        ? new Vector3(centre.x + t * hx, y, centre.z + side * hz)
                        : new Vector3(centre.x + side * hx, y, centre.z + t * hz);
                    DecayKit.Litter(parent, spot, 0.45f, 1, seed + 977 + i * 17);
                    decals++;

                    // Roughly one spot in four gets a modelled object rather than a decal. It has
                    // to be against a real wall, or a bottle ends up standing in a doorway; and it
                    // is always one of the small ones, since a barrel here would block the lane.
                    if (rng.Next(4) != 0) continue;
                    var outward = alongX ? new Vector3(0f, 0f, side) : new Vector3(side, 0f, 0f);
                    if (!Physics.Raycast(spot + Vector3.up * 0.6f, outward, 0.95f, ~0,
                                         QueryTriggerInteraction.Ignore)) { skipped++; continue; }
                    DecayKit.WasteAgainstWall(parent, spot, outward, seed + 313 + i * 7);
                    props++;
                }

                // A couple of stains out in the open. Not rubbish — water has been coming through
                // this ceiling for decades, and the floor under the leak shows it.
                int stains = Mathf.Clamp(Mathf.RoundToInt(area / 45f), 1, 4);
                for (int i = 0; i < stains; i++)
                {
                    var spot = new Vector3(
                        centre.x + ((float)rng.NextDouble() * 2f - 1f) * hx * 0.8f, y,
                        centre.z + ((float)rng.NextDouble() * 2f - 1f) * hz * 0.8f);
                    DecayKit.Grime(parent, spot, Mathf.Lerp(1.4f, 2.8f, (float)rng.NextDouble()),
                        (float)rng.NextDouble() * 360f);
                    decals++;
                }

                // --- webs in the upper corners ------------------------------------------------
                // 2.3m clears the player's head and sits below every ceiling in the building
                // (3.0m on the ground floor is the lowest), so this never needs the ceiling's
                // actual height and never pokes through it.
                float webY = y + 2.3f;
                for (int i = 0; i < 4; i++)
                {
                    if (rng.NextDouble() > 0.62) continue;
                    var (sx, sz) = corners[i];
                    // Same test as the corner props, and it matters more here: a web is strung
                    // BETWEEN two walls, so hanging one where a wall is missing leaves it floating
                    // in a doorway with nothing holding either end.
                    var probe = new Vector3(centre.x + sx * (hx - 0.25f), y, centre.z + sz * (hz - 0.25f));
                    if (!IsEnclosedCorner(probe, sx, sz)) { skipped++; continue; }
                    float s = Mathf.Lerp(0.5f, 1.0f, (float)rng.NextDouble());
                    // Facing out of the corner along the diagonal, pulled in so the web's anchor
                    // corner — not the quad's centre — lands where the two walls meet.
                    float yaw = Mathf.Atan2(-sx, -sz) * Mathf.Rad2Deg;
                    var pos = new Vector3(
                        centre.x + sx * (hx + EdgeInset - s * 0.36f), webY - s * 0.4f,
                        centre.z + sz * (hz + EdgeInset - s * 0.36f));
                    var web = DecayKit.WebCorner(parent, Vector3.zero, yaw, s);
                    if (web == null) continue;
                    web.transform.position = pos;
                    webs++;
                }
            }

            Debug.Log("[Decay] Floors dressed: " + string.Join(", ", slabs.ConvertAll(f => f.name)));
            Debug.Log($"[Decay] Dressed {slabs.Count} floors: {props} waste piles, {decals} decals, " +
                      $"{webs} webs, {skipped} open corners skipped. All batching-static, " +
                      "6 shared materials, ~1 MB of texture.");
        }

        /// <summary>
        /// True when both walls forming this corner of the slab are really there.
        ///
        /// Probes outward along each axis from a point just inside the corner. Slab edges are not
        /// reliably walls — they are also doorways, open thresholds onto corridors, stairheads, and
        /// on the exterior, nothing at all — and the difference is invisible from the bounds alone.
        /// The probe deliberately starts inside the room and reaches only just past where a wall
        /// face would be, so furniture standing against the wall cannot pass for the wall.
        /// </summary>
        private static bool IsEnclosedCorner(Vector3 spot, float sx, float sz)
        {
            const float reach = 0.95f;
            var from = spot + Vector3.up * 1.1f;   // chest height: under lintels, over skirting
            return Physics.Raycast(from, new Vector3(sx, 0f, 0f), reach, ~0, QueryTriggerInteraction.Ignore)
                && Physics.Raycast(from, new Vector3(0f, 0f, sz), reach, ~0, QueryTriggerInteraction.Ignore);
        }
    }
}
#endif
