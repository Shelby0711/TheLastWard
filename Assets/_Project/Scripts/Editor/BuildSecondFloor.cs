#if UNITY_EDITOR
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace LastWard.EditorTools
{
    /// <summary>
    /// The asylum — second floor. See FLOOR2_ASYLUM.md for why any of this is shaped the way it is.
    ///
    /// This floor is not a puzzle floor. Its whole job is a route you have to be able to run in the
    /// dark, so the geometry serves one number: <b>78 metres</b>, which at the player's 5.2 m/s sprint
    /// is a hair under the 14–15 second window a wrong riddle answer gives you. Walk any part of it
    /// and you do not arrive.
    ///
    /// It is a U rather than a straight run, deliberately. A corridor whose end you can see from the
    /// entrance is a corridor you never have to learn, and learning the building — as opposed to
    /// reading about it — is the only knowledge on this floor that does not mark you for death.
    /// </summary>
    public static partial class BuildM5Level
    {
        // ---- the frame -------------------------------------------------------------------------
        // The second-floor landing already exists in CreateSecondFloorStairs: y = FFy + 3.2, at
        // z ~121.6 inside a shaft whose walls sit at x = +/-1.9. Everything below hangs off that.
        // The asylum's own surfaces. Floors 0 and 1 are a hospital that failed; this is what was
        // underneath it, so nothing here is shared with them.
        private const string SFWall    = "SecondFloor/Textures/SF_Wall_Plaster.png";
        private const string SFWallAlt = "SecondFloor/Textures/SF_Wall_Stained.png";
        private const string SFWallRed = "SecondFloor/Textures/SF_Wall_Blood.png";
        private const string SFFloorT  = "SecondFloor/Textures/SF_Floor_Tile.png";
        private const string SFFloorR  = "SecondFloor/Textures/SF_Floor_Blood.png";
        private const string SFCeil    = "SecondFloor/Textures/SF_Ceiling.png";
        private const string SFRust    = "SecondFloor/Textures/SF_Rust_Heavy.png";
        private const string SFRustDk  = "SecondFloor/Textures/SF_Rust_Dark.png";

        private const float SFy = FFy + 3.2f;          // 6.4
        // 4.4, not the 3m used on the floors below. The Inspector is 3.6m and its arch lives in
        // the animation clips — so any moment it is NOT animating (bind pose, a missing clip, a
        // frame between states) it stands upright and puts its head through a 3m ceiling. Sizing the
        // floor so that upright still fits means the arch becomes character rather than a load-
        // bearing requirement, and one broken clip stops being a visible geometry bug.
        private const float SFh = 4.4f;
        private const float SFt = 0.3f;                // wall thickness
        private const float SFhalf = 1.5f;             // corridor half-width, matching floor 1

        // The U runs NORTH from the stairhead, into fresh ground beyond the stairwell.
        //
        // It used to run south, back over the first floor -- which looked economical and was in fact
        // the reason the staircase appeared to end in a ceiling: leg A's floor slab spanned z 72-120.4
        // at y 6.4, and the surviving flight climbs through z 117.1-120.4 to arrive at exactly that
        // height. The asylum was a lid welded over the top half of its own approach. A storey is not
        // obliged to sit on the one below it, and this one has no reason to.
        private const float LegAz0 = 120.4f, LegAz1 = 168.8f; // 48.4m, starting AT the stairhead
        private const float LegBx0 = -14f, LegBx1 = 0f;       // 14m across
        private const float LegBz = 168.8f;
        private const float LegCz0 = 154.8f, LegCz1 = 168.8f; // 14m back south
        private const float LegCx = -14f;
        // 48.4 + 14 + 14 = 76.4m of centreline.
        //
        // That figure is the single most fragile number on this floor and it is NOT free to change.
        // At the player's 5.2 m/s sprint it is 14.7s against a 15s escape window — about 300ms of
        // margin, and only if you sprint every step of it. The first draft was 78.4m, which works out
        // at 15.1s: unsurvivable with perfect play, which is not tension, it is a scripted death.
        // Move this and you must move the window with it.
        private const float SprintSpeed = 5.2f;              // must match FirstPersonMotor
        private const float EscapeWindow = 15f;

        private const float RiddleZ = 154.8f;                // door out, end of leg C
        private static readonly Vector3 RecordsCentre = new Vector3(8f, SFy, 145f);
        private const float RecordsRadius = 6.5f;
        private const float TallCeil = SFy + 12f;            // where it can stand upright

        public static void CreateSecondFloor()
        {
            BuildShell();
            BuildAtrium();
            BuildRecordsRoom();
            BuildRiddleDoor();
            BuildCandles();
            BuildHidingSpots();
            BuildDressing();
            BuildMatchboxAndLore();

            BuildInspector();
            BuildAsylumManager();

            // The run's ten matches, and the thing that hands the box to one player at the start.
            var supply = new GameObject("MatchSupply");
            supply.AddComponent<Unity.Netcode.NetworkObject>();
            supply.AddComponent<LastWard.Puzzles.MatchSupply>();
            // Print the route rather than trusting the comment. This is the number the whole floor
            // balances on, and a silent 2m drift in the layout turns a survivable sprint into a
            // guaranteed death that looks exactly like a bug.
            float route = (LegAz1 - LegAz0) + (LegBx1 - LegBx0) + (LegCz1 - LegCz0);
            float sprint = route / SprintSpeed;
            Debug.Log($"[Build] Second floor (asylum): route {route:0.0}m = {sprint:0.00}s at full " +
                      $"sprint, against a {EscapeWindow:0.0}s window ({EscapeWindow - sprint:0.00}s spare).");
            if (sprint > EscapeWindow)
                Debug.LogError($"[Build] ASYLUM ROUTE IS UNSURVIVABLE: {sprint:0.00}s needed, " +
                               $"{EscapeWindow:0.0}s given. Shorten a leg or widen the window.");
            Debug.Log("[Build] 10 candles / 10 matches, records room, riddle door, 2 hiding spots.");
        }

        // -----------------------------------------------------------------------------------------

        /// <summary>Floor, ceiling and walls for the three legs of the U.</summary>
        private static void BuildShell()
        {
            // Leg A - the spine. Its side walls are cut for cell mouths rather than run solid.
            CorridorFloorAndCeiling("SF_LegA", new Vector3(0f, SFy, (LegAz0 + LegAz1) / 2f),
                                    SFhalf * 2f, LegAz1 - LegAz0);
            BuildCellBlock();
            // Leg B - across the bottom of the U.
            Corridor("SF_LegB", new Vector3((LegBx0 + LegBx1) / 2f, SFy, LegBz),
                     LegBx1 - LegBx0, SFhalf * 2f, alongZ: false);
            // Leg C - back south. Its east wall is cut for the stair up to the Morgue, so it is
            // built in pieces rather than as one run.
            CorridorFloorAndCeiling("SF_LegC", new Vector3(LegCx, SFy, (LegCz0 + LegCz1) / 2f),
                                    SFhalf * 2f, LegCz1 - LegCz0);
            Tile(EditorBuildKit.CreateBox("SF_LegC_WallW",
                new Vector3(LegCx - (SFhalf + SFt / 2f), SFy + SFh / 2f, (LegCz0 + LegCz1) / 2f),
                new Vector3(SFt, SFh, LegCz1 - LegCz0)), SFWall, 2f);
            foreach (var seg in new[] { (LegCz0, StairZ - StairW / 2f), (StairZ + StairW / 2f, LegCz1) })
            {
                if (seg.Item2 - seg.Item1 <= 0.02f) continue;
                Tile(EditorBuildKit.CreateBox($"SF_LegC_WallE_{(int)seg.Item1}",
                    new Vector3(LegCx + SFhalf + SFt / 2f, SFy + SFh / 2f,
                                (seg.Item1 + seg.Item2) / 2f),
                    new Vector3(SFt, SFh, seg.Item2 - seg.Item1)), SFWall, 2f);
            }
            BuildMorgueStair();

            // Short branch east off leg A into the records room.
            Corridor("SF_RecordsLink", new Vector3(4.6f, SFy, RecordsCentre.z),
                     6.2f, SFhalf * 2f, alongZ: false);
        }

        /// <summary>Just the slabs, for runs whose walls are built separately (leg A's cells).</summary>
        private static void CorridorFloorAndCeiling(string name, Vector3 centre, float xSize, float zSize)
        {
            Tile(EditorBuildKit.CreateBox(name + "_Floor",
                new Vector3(centre.x, SFy - 0.1f, centre.z), new Vector3(xSize, 0.2f, zSize)),
                SFFloorT, 2f);
            Tile(EditorBuildKit.CreateBox(name + "_Ceil",
                new Vector3(centre.x, SFy + SFh, centre.z), new Vector3(xSize, 0.2f, zSize)),
                SFCeil, 3f);
        }

        /// <summary>One straight run: floor slab, ceiling slab and the two side walls.</summary>
        private static void Corridor(string name, Vector3 centre, float xSize, float zSize, bool alongZ)
        {
            Tile(EditorBuildKit.CreateBox(name + "_Floor",
                new Vector3(centre.x, SFy - 0.1f, centre.z), new Vector3(xSize, 0.2f, zSize)),
                SFFloorT, 2f);
            Tile(EditorBuildKit.CreateBox(name + "_Ceil",
                new Vector3(centre.x, SFy + SFh, centre.z), new Vector3(xSize, 0.2f, zSize)),
                SFCeil, 3f);

            // Walls run down the long sides only; the short ends stay open so the legs join up.
            float midY = SFy + SFh / 2f;
            if (alongZ)
            {
                foreach (float sx in new[] { -1f, 1f })
                    Tile(EditorBuildKit.CreateBox($"{name}_Wall{(sx < 0 ? "W" : "E")}",
                        new Vector3(centre.x + sx * (xSize / 2f + SFt / 2f), midY, centre.z),
                        new Vector3(SFt, SFh, zSize)), SFWall, 2f);
            }
            else
            {
                foreach (float sz in new[] { -1f, 1f })
                    Tile(EditorBuildKit.CreateBox($"{name}_Wall{(sz < 0 ? "S" : "N")}",
                        new Vector3(centre.x, midY, centre.z + sz * (zSize / 2f + SFt / 2f)),
                        new Vector3(xSize, SFh, SFt)), SFWall, 2f);
            }
        }

        // Cells: 2.6 wide, 3.2 deep, alternating down the spine so you never face two mouths at once.
        private static readonly float[] CellZ = { 127f, 133f, 139f, 145f, 151f, 157f, 163f };

        /// <summary>
        /// The cell block. This is what makes the asylum an asylum rather than a corridor: the route
        /// is lined with barred mouths, and every one of them is somewhere the Inspector could be
        /// standing when your candlelight reaches it.
        ///
        /// The gates open. I built them welded shut first, on the grounds that a 76m sprint cannot
        /// afford a dozen obstacles on the route — but the cells are the only cover on this floor,
        /// and cover you can see into and never enter reads as scenery. Opening one costs a loud
        /// grind of rusted iron, which is the only currency this floor trades in.
        /// </summary>
        private static void BuildCellBlock()
        {
            const float mouth = 2.6f, deep = 3.2f;
            float wx = SFhalf + SFt / 2f;

            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                var mine = new System.Collections.Generic.List<float>();
                for (int i = 0; i < CellZ.Length; i++)
                    if ((i % 2 == 0) == (side == 0)) mine.Add(CellZ[i]);

                // Wall in the gaps between mouths, so the corridor still reads as enclosed.
                var edges = new System.Collections.Generic.List<float>();
                edges.Add(LegAz0);
                foreach (float cz in mine) { edges.Add(cz - mouth / 2f); edges.Add(cz + mouth / 2f); }
                edges.Add(LegAz1);
                for (int e = 0; e + 1 < edges.Count; e += 2)
                {
                    float a = edges[e], b = edges[e + 1];
                    if (b - a <= 0.02f) continue;
                    Tile(EditorBuildKit.CreateBox($"SF_LegA_Wall{(sx < 0 ? "W" : "E")}_{e}",
                        new Vector3(sx * wx, SFy + SFh / 2f, (a + b) / 2f),
                        new Vector3(SFt, SFh, b - a)), SFWall, 2f);
                }

                foreach (float cz in mine) BuildCell(sx, cz, mouth, deep);
            }
        }

        /// <summary>One cell: a box off the corridor, with a rusted gate across its mouth.</summary>
        private static void BuildCell(float sx, float cz, float mouth, float deep)
        {
            string tag = (sx < 0 ? "W" : "E") + ((int)cz).ToString();
            float inner = SFhalf + SFt;
            float far = inner + deep;
            float midX = sx * (inner + deep / 2f);

            Tile(EditorBuildKit.CreateBox($"SF_Cell{tag}_Floor",
                new Vector3(midX, SFy - 0.1f, cz), new Vector3(deep, 0.2f, mouth)), SFFloorR, 1.5f);
            Tile(EditorBuildKit.CreateBox($"SF_Cell{tag}_Ceil",
                new Vector3(midX, SFy + SFh, cz), new Vector3(deep, 0.2f, mouth)), SFCeil, 2f);
            Tile(EditorBuildKit.CreateBox($"SF_Cell{tag}_Back",
                new Vector3(sx * (far + SFt / 2f), SFy + SFh / 2f, cz),
                new Vector3(SFt, SFh, mouth + SFt * 2f)), SFWallRed, 1.5f);
            foreach (float sz in new[] { -1f, 1f })
                Tile(EditorBuildKit.CreateBox($"SF_Cell{tag}_Side{(sz < 0 ? "S" : "N")}",
                    new Vector3(midX, SFy + SFh / 2f, cz + sz * (mouth / 2f + SFt / 2f)),
                    new Vector3(deep, SFh, SFt)), SFWallAlt, 1.5f);

            BuildJailGate($"SF_Gate{tag}", new Vector3(sx * inner, SFy, cz), mouth, sx);
        }

        /// <summary>
        /// A rusted barred gate. Bars every 16cm — dense enough to read as a cage from down the hall,
        /// open enough that a torch beam passes through and shows what is inside. The second property
        /// is the point: a solid door hides the cell, and a cell you cannot see into is not
        /// frightening, it is a wall.
        /// </summary>
        private static void BuildJailGate(string name, Vector3 centre, float width, float facing)
        {
            var rootGO = new GameObject(name);
            rootGO.transform.position = centre;
            rootGO.AddComponent<Unity.Netcode.NetworkObject>();
            var root = rootGO.transform;

            // Frame stays put; everything that moves hangs off the leaf.
            var leaf = new GameObject("Leaf").transform;
            leaf.SetParent(root, false);
            leaf.localPosition = Vector3.zero;

            foreach (float sz in new[] { -1f, 1f })
            {
                var jamb = EditorBuildKit.CreateBox($"{name}_Jamb{(sz < 0 ? "S" : "N")}",
                    centre + new Vector3(0f, SFh / 2f, sz * width / 2f),
                    new Vector3(0.14f, SFh, 0.13f));
                Tile(jamb, SFRustDk, 1f);
                jamb.transform.SetParent(root, true);
            }
            var head = EditorBuildKit.CreateBox($"{name}_Head",
                centre + new Vector3(0f, SFh - 0.08f, 0f), new Vector3(0.14f, 0.16f, width));
            Tile(head, SFRustDk, 1f);
            head.transform.SetParent(root, true);

            // Bars, very slightly off-true so it reads as hand-made ironwork rather than a grid.
            int bars = Mathf.Max(2, Mathf.RoundToInt(width / 0.16f));
            for (int i = 1; i < bars; i++)
            {
                float z = centre.z - width / 2f + width * (i / (float)bars);
                var bar = EditorBuildKit.CreateBox($"{name}_Bar{i:00}",
                    new Vector3(centre.x, SFy + SFh / 2f, z), new Vector3(0.06f, SFh - 0.16f, 0.055f));
                Tile(bar, SFRust, 0.7f);
                bar.transform.rotation = Quaternion.Euler(0f, 0f, (i % 3 == 0) ? 0.7f : -0.4f);
                bar.transform.SetParent(leaf, true);
            }

            foreach (float ry in new[] { 0.75f, 2.05f })
            {
                var rail = EditorBuildKit.CreateBox($"{name}_Rail{(int)(ry * 10)}",
                    centre + new Vector3(0f, ry, 0f), new Vector3(0.075f, 0.09f, width - 0.14f));
                Tile(rail, SFRust, 0.7f);
                rail.transform.SetParent(leaf, true);
            }

            // Lock plate on the corridor side: these were shut from the outside.
            var plate = EditorBuildKit.CreateBox($"{name}_Lock",
                centre + new Vector3(-facing * 0.09f, 1.15f, width * 0.32f),
                new Vector3(0.05f, 0.26f, 0.2f));
            Tile(plate, SFRustDk, 1f);
            plate.transform.SetParent(leaf, true);

            var gate = rootGO.AddComponent<LastWard.Puzzles.CellGate>();
            EditorBuildKit.SetRef(gate, "leaf", leaf);
            // Slides its own width, so it clears the mouth completely.
            EditorBuildKit.SetFloat(gate, "slideDistance", width);
        }

        /// <summary>
        /// The tall space at the corner of the U — and the only place on the route where the Inspector
        /// can straighten up without it meaning you are about to die.
        ///
        /// It exists purely so the player learns what that ceiling height means while they are safe.
        /// Without it, seeing the thing upright and being killed by it are the same event, and the
        /// reveal is wasted on someone who is already gone.
        /// </summary>
        private static void BuildAtrium()
        {
            var c = new Vector3(-4.5f, SFy, LegBz);
            Tile(EditorBuildKit.CreateBox("SF_Atrium_Floor",
                new Vector3(c.x, SFy - 0.1f, c.z), new Vector3(11f, 0.2f, 9f)), SFFloorT, 2f);
            Tile(EditorBuildKit.CreateBox("SF_Atrium_Ceil",
                new Vector3(c.x, TallCeil, c.z), new Vector3(11f, 0.2f, 9f)), SFCeil, 3f);
            foreach (float sz in new[] { -1f, 1f })
                Tile(EditorBuildKit.CreateBox($"SF_Atrium_Wall{(sz < 0 ? "S" : "N")}",
                    new Vector3(c.x, SFy + (TallCeil - SFy) / 2f, c.z + sz * 4.65f),
                    new Vector3(11f, TallCeil - SFy, SFt)), SFWallAlt, 2.5f);
            Tile(EditorBuildKit.CreateBox("SF_Atrium_Wall_W",
                new Vector3(c.x - 5.65f, SFy + (TallCeil - SFy) / 2f, c.z),
                new Vector3(SFt, TallCeil - SFy, 9f)), SFWallAlt, 2.5f);

            // Barely lit, and from high up: enough to read the volume, not enough to see into it.
            AddDimLight(new Vector3(c.x, TallCeil - 1.5f, c.z), 0.10f);
        }

        /// <summary>
        /// The records room: a round library, four storeys of shelving, with the Inspector's ledger
        /// open on a table in the middle of it.
        ///
        /// Round because there is no corner to break line of sight in while your file burns, and tall
        /// because this is the one room on the floor where the thing can stand at full height. Those
        /// two facts are the room — everything else is dressing for them.
        ///
        /// Sited off leg A rather than beside the riddle door on purpose: from the door it is about
        /// 30m and the hiding places are the full 76m. Two escapes at genuinely different prices is
        /// the decision the doom timer exists to force. Put them next to each other and burning your
        /// file is free.
        /// </summary>
        private static void BuildRecordsRoom()
        {
            var root = new GameObject("SF_RecordsRoom");
            var c = RecordsCentre;

            Tile(EditorBuildKit.CreateBox("SF_Records_Floor",
                new Vector3(c.x, SFy - 0.1f, c.z),
                new Vector3(RecordsRadius * 2f, 0.2f, RecordsRadius * 2f)), SFFloorR, 2f);
            Tile(EditorBuildKit.CreateBox("SF_Records_Ceil",
                new Vector3(c.x, TallCeil, c.z),
                new Vector3(RecordsRadius * 2f, 0.2f, RecordsRadius * 2f)), SFCeil, 3f);

            // Round wall as chords. Cheaper than a cylinder mesh and every surface stays a box
            // collider, which is what the rest of this level is made of.
            const int SEG = 24;
            float chord = 2f * Mathf.PI * RecordsRadius / SEG * 1.08f;
            for (int i = 0; i < SEG; i++)
            {
                float a = i * Mathf.PI * 2f / SEG;
                bool doorway = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 180f)) < 16f;
                if (doorway) continue;
                var w = EditorBuildKit.CreateBox($"SF_Records_Wall{i:00}",
                    new Vector3(c.x + Mathf.Cos(a) * RecordsRadius, SFy + (TallCeil - SFy) / 2f,
                                c.z + Mathf.Sin(a) * RecordsRadius),
                    new Vector3(SFt, TallCeil - SFy, chord));
                w.transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                Tile(w, SFWallRed, 2.5f);
                w.transform.SetParent(root.transform, true);

                // Two tiers of real shelving, stacked. One ring reads as a room with bookcases in
                // it; stacked, the wall becomes the library and the twelve-metre ceiling is doing
                // something rather than sitting there.
                for (int tier = 0; tier < 2; tier++)
                    BuildShelfUnit($"SF_Shelf{i:00}_{tier}", c, a, SFy + tier * 2.45f, root.transform,
                                   i * 7 + tier);
            }

            // The reading table, and the ledger on it.
            var table = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            table.name = "SF_Records_Table";
            table.transform.position = new Vector3(c.x, SFy + 0.38f, c.z);
            table.transform.localScale = new Vector3(2.6f, 0.38f, 2.6f);
            Tile(table, SFRustDk, 1.4f);
            table.transform.SetParent(root.transform, true);

            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "SF_Records_TableLeg";
            pedestal.transform.position = new Vector3(c.x, SFy + 0.19f, c.z);
            pedestal.transform.localScale = new Vector3(0.7f, 0.19f, 0.7f);
            Tile(pedestal, SFRustDk, 1f);
            pedestal.transform.SetParent(root.transform, true);

            BuildRecordBook(new Vector3(c.x, SFy + 0.77f, c.z), root.transform);
            ScatterBooks(c, root.transform);

            // Lit from high up and dimly. You should be able to tell the room is tall without being
            // able to see what is at the top of it.
            AddDimLight(new Vector3(c.x, TallCeil - 2.5f, c.z), 0.10f);
            AddDimLight(new Vector3(c.x, SFy + 1.6f, c.z), 0.12f);
        }

        /// <summary>One bookcase against the round wall, faced inward.</summary>
        private static void BuildShelfUnit(string name, Vector3 c, float angle, float baseY,
                                           Transform parent, int seed)
        {
            float r = RecordsRadius - 0.42f;
            var at = new Vector3(c.x + Mathf.Cos(angle) * r, baseY, c.z + Mathf.Sin(angle) * r);

            var model = ArtKit.LoadModel("SecondFloor/Props/dusty_old_bookshelf_free/scene.gltf");
            if (model == null)
            {
                // Fallback so a missing asset degrades to a plain carcass rather than a hole.
                var box = EditorBuildKit.CreateBox(name + "_Carcass",
                    at + new Vector3(0f, 1.1f, 0f), new Vector3(0.5f, 2.2f, 1.35f));
                box.transform.rotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
                Tile(box, SFRustDk, 1f);
                box.transform.SetParent(parent, true);
                return;
            }

            var inst = ArtKit.Spawn(model, parent, name);
            ArtKit.FitHeight(inst, 2.35f);
            ArtKit.AutoTexture(inst, "SecondFloor/Props/dusty_old_bookshelf_free/textures");
            // Backs on to the wall, opening inward. -angle turns its local forward toward the centre.
            inst.transform.rotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + 90f, 0f);
            ArtKit.GroundAt(inst, at);
            // Upper tiers sit on the one below rather than on the floor.
            inst.transform.position += new Vector3(0f, baseY - SFy, 0f);
        }

        /// <summary>Loose volumes: on the table, and dropped where people left in a hurry.</summary>
        private static void ScatterBooks(Vector3 c, Transform parent)
        {
            var model = ArtKit.LoadModel("SecondFloor/Props/book/scene.gltf");
            if (model == null) return;

            var rng = new System.Random(4471);
            var spots = new[]
            {
                new Vector3(c.x + 0.55f, SFy + 0.77f, c.z + 0.35f),      // on the table
                new Vector3(c.x - 0.62f, SFy + 0.77f, c.z - 0.28f),
                new Vector3(c.x + 1.9f,  SFy,         c.z - 2.6f),       // on the floor
                new Vector3(c.x - 3.1f,  SFy,         c.z + 1.4f),
                new Vector3(c.x + 0.4f,  SFy,         c.z + 4.3f),
                new Vector3(c.x - 4.4f,  SFy,         c.z - 3.2f),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                var inst = ArtKit.Spawn(model, parent, $"SF_Book{i}");
                ArtKit.FitHeight(inst, 0.07f);
                ArtKit.AutoTexture(inst, "SecondFloor/Props/book/textures");
                inst.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                ArtKit.GroundAt(inst, spots[i]);
                foreach (var col in inst.GetComponentsInChildren<Collider>(true))
                    UnityObject.DestroyImmediate(col);
            }
        }

        /// <summary>
        /// The ledger itself: open, on the table, and the object every choice on this floor runs
        /// through. Burning a page or swapping a name both happen here.
        /// </summary>
        private static void BuildRecordBook(Vector3 at, Transform parent)
        {
            var rootGO = new GameObject("SF_RecordBook");
            rootGO.transform.position = at;
            rootGO.AddComponent<Unity.Netcode.NetworkObject>();

            var leather = EditorBuildKit.MakeMaterial(new Color(0.13f, 0.06f, 0.05f));
            var paper = EditorBuildKit.MakeMaterial(new Color(0.62f, 0.58f, 0.47f));

            var spine = EditorBuildKit.CreateBox("Book_Spine", at, new Vector3(0.1f, 0.07f, 0.42f));
            EditorBuildKit.SetMaterial(spine, leather);
            spine.transform.SetParent(rootGO.transform, true);

            // Two leaves fanned open, so it reads as a book being consulted rather than shut.
            foreach (float sx in new[] { -1f, 1f })
            {
                var cover = EditorBuildKit.CreateBox($"Book_Cover{(sx < 0 ? "L" : "R")}",
                    at + new Vector3(sx * 0.29f, 0.005f, 0f), new Vector3(0.52f, 0.035f, 0.42f));
                EditorBuildKit.SetMaterial(cover, leather);
                cover.transform.rotation = Quaternion.Euler(0f, 0f, sx * 5f);
                cover.transform.SetParent(rootGO.transform, true);

                var page = EditorBuildKit.CreateBox($"Book_Page{(sx < 0 ? "L" : "R")}",
                    at + new Vector3(sx * 0.28f, 0.032f, 0f), new Vector3(0.48f, 0.02f, 0.38f));
                EditorBuildKit.SetMaterial(page, paper);
                page.transform.rotation = Quaternion.Euler(0f, 0f, sx * 5f);
                page.transform.SetParent(rootGO.transform, true);
                UnityObject.DestroyImmediate(page.GetComponent<Collider>());
            }

            var ledger = rootGO.AddComponent<LastWard.Puzzles.RecordLedger>();
            EditorBuildKit.SetVector3(ledger, "bookPosition", at);

            // One plaque per player slot, ranged round the table. These are what you burn and what
            // you swap; the book is the thing that tells you which is which.
            for (int i = 0; i < 4; i++)
            {
                float a = (i / 4f) * Mathf.PI * 2f + 0.78f;
                var spot = new Vector3(at.x + Mathf.Cos(a) * 1.55f, SFy + 0.85f,
                                       at.z + Mathf.Sin(a) * 1.55f);
                var plaqueGO = EditorBuildKit.CreateBox($"SF_File{i}",
                    spot, new Vector3(0.34f, 0.26f, 0.05f));
                plaqueGO.transform.rotation = Quaternion.Euler(-22f, -a * Mathf.Rad2Deg + 90f, 0f);
                Tile(plaqueGO, SFRustDk, 0.5f);
                plaqueGO.AddComponent<Unity.Netcode.NetworkObject>();
                var file = plaqueGO.AddComponent<LastWard.Puzzles.RecordFile>();
                EditorBuildKit.SetRef(file, "ledger", ledger);
                EditorBuildKit.SetInt(file, "slot", i);
                plaqueGO.transform.SetParent(rootGO.transform, true);
            }
        }

        // Where the stair up to the Morgue breaks out of leg C's east wall.
        private const float StairZ = 161.5f, StairW = 2.8f;
        private const float TFy = SFy + 3.4f;                // 9.8 - third floor, the Morgue

        /// <summary>
        /// The end of the hallway: a pair of tall window doors, boarded from outside.
        ///
        /// A corridor that simply stops is a corridor the player reads as unfinished. A window is a
        /// dead end you can see through — it tells you there is nothing further this way without
        /// having to say so, and on a floor where the only correct move is to turn around and take
        /// the stairs, that distinction is worth the geometry.
        /// </summary>
        private static void BuildRiddleDoor()
        {
            float z = LegCz0 - SFt / 2f;
            var frameMat = EditorBuildKit.MakeMaterial(new Color(0.10f, 0.09f, 0.08f));
            var glassMat = EditorBuildKit.MakeEmissive(new Color(0.05f, 0.06f, 0.07f),
                                                       new Color(0.05f, 0.07f, 0.09f));

            // Wall around the opening.
            foreach (float sx in new[] { -1f, 1f })
                Tile(EditorBuildKit.CreateBox($"SF_EndWall{(sx < 0 ? "W" : "E")}",
                    new Vector3(LegCx + sx * 1.18f, SFy + SFh / 2f, z),
                    new Vector3(0.64f, SFh, SFt)), SFWallAlt, 1.5f);
            Tile(EditorBuildKit.CreateBox("SF_EndWall_Head",
                new Vector3(LegCx, SFy + SFh - 0.35f, z), new Vector3(2.36f, 0.7f, SFt)), SFWallAlt, 1.5f);

            // Two leaves, each with its own glazing bars.
            foreach (float sx in new[] { -1f, 1f })
            {
                var leaf = EditorBuildKit.CreateBox($"SF_Window{(sx < 0 ? "L" : "R")}_Frame",
                    new Vector3(LegCx + sx * 0.59f, SFy + 1.22f, z), new Vector3(1.18f, 2.15f, 0.09f));
                EditorBuildKit.SetMaterial(leaf, frameMat);

                var glass = EditorBuildKit.CreateBox($"SF_Window{(sx < 0 ? "L" : "R")}_Glass",
                    new Vector3(LegCx + sx * 0.59f, SFy + 1.22f, z + 0.02f),
                    new Vector3(1.02f, 1.98f, 0.03f));
                EditorBuildKit.SetMaterial(glass, glassMat);
                UnityObject.DestroyImmediate(glass.GetComponent<Collider>());

                for (int i = 1; i <= 3; i++)
                {
                    var bar = EditorBuildKit.CreateBox($"SF_Window{(sx < 0 ? "L" : "R")}_Bar{i}",
                        new Vector3(LegCx + sx * 0.59f, SFy + 0.28f + i * 0.55f, z + 0.04f),
                        new Vector3(1.06f, 0.05f, 0.04f));
                    EditorBuildKit.SetMaterial(bar, frameMat);
                    UnityObject.DestroyImmediate(bar.GetComponent<Collider>());
                }
                var mull = EditorBuildKit.CreateBox($"SF_Window{(sx < 0 ? "L" : "R")}_Mull",
                    new Vector3(LegCx + sx * 0.59f, SFy + 1.22f, z + 0.04f),
                    new Vector3(0.05f, 2.0f, 0.04f));
                EditorBuildKit.SetMaterial(mull, frameMat);
                UnityObject.DestroyImmediate(mull.GetComponent<Collider>());
            }

            // Boards nailed across from the far side. Whatever is out there, it was shut in or out.
            for (int i = 0; i < 3; i++)
            {
                var plank = EditorBuildKit.CreateBox($"SF_Window_Board{i}",
                    new Vector3(LegCx + (i - 1) * 0.15f, SFy + 0.75f + i * 0.62f, z - 0.09f),
                    new Vector3(2.5f, 0.24f, 0.05f));
                Tile(plank, SFRustDk, 0.8f);
                plank.transform.rotation = Quaternion.Euler(0f, 0f, i == 1 ? 4f : -3f);
                UnityObject.DestroyImmediate(plank.GetComponent<Collider>());
            }

            AddDimLight(new Vector3(LegCx, SFy + 2.2f, z + 1.6f), 0.07f);

            EditorBuildKit.CreateNoteProp("SF_Note_Riddle",
                new Vector3(LegCx + 0.9f, SFy, LegCz0 + 1.6f),
                "Assets/_Project/Data/sf_riddle.asset", "sf_riddle", "Nailed Beside The Window",
                "There is nothing out there and there never was. The way on is the stair, and the " +
                "stair goes down into the cold rooms. It asks the same question every night and it " +
                "already knows what you will say - the answer is not a word, it is whatever this " +
                "place happens to be tonight. Whoever speaks is the one it hears.", 4f);
        }

        /// <summary>
        /// The stair to the Morgue: off the SIDE of the hallway, climbing east out of leg C.
        ///
        /// Every other stair in this building sits square at the end of a corridor, which makes them
        /// easy to find and easy to run at. This one is in a wall you walk past, so the way up is
        /// something you have to have noticed — and on a floor whose only free knowledge is spatial,
        /// that is the right thing to charge for.
        /// </summary>
        private static void BuildMorgueStair()
        {
            const int steps = 20;
            const float rise = 0.17f, run = 0.32f;
            float x0 = LegCx + SFhalf + SFt;               // inner face of leg C's east wall
            float x1 = x0 + run * steps;                   // 6.4m across
            float ceil = TFy + 3f;

            var root = new GameObject("SF_MorgueStair").transform;

            Tile(EditorBuildKit.CreateBox("SF_MStair_Floor",
                new Vector3((x0 + x1) / 2f + 1f, SFy - 0.1f, StairZ),
                new Vector3(x1 - x0 + 2f, 0.2f, StairW)), SFFloorT, 1.5f);
            Tile(EditorBuildKit.CreateBox("SF_MStair_Ceil",
                new Vector3((x0 + x1) / 2f + 1f, ceil, StairZ),
                new Vector3(x1 - x0 + 2f, 0.2f, StairW)), SFCeil, 2f);
            foreach (float sz in new[] { -1f, 1f })
                Tile(EditorBuildKit.CreateBox($"SF_MStair_Wall{(sz < 0 ? "S" : "N")}",
                    new Vector3((x0 + x1) / 2f + 1f, SFy + (ceil - SFy) / 2f,
                                StairZ + sz * (StairW / 2f + SFt / 2f)),
                    new Vector3(x1 - x0 + 2f, ceil - SFy, SFt)), SFWallAlt, 2f);
            Tile(EditorBuildKit.CreateBox("SF_MStair_WallE",
                new Vector3(x1 + 2f + SFt / 2f, SFy + (ceil - SFy) / 2f, StairZ),
                new Vector3(SFt, ceil - SFy, StairW + SFt * 2f)), SFWallAlt, 2f);

            // Treads, climbing along +X. BuildStaircase only runs along Z, and rotating its output
            // afterwards would fight the box colliders, so this is its own short loop.
            for (int i = 0; i < steps; i++)
            {
                float y = SFy + rise * (i + 1);
                var step = EditorBuildKit.CreateBox($"SF_MStair_Step{i:00}",
                    new Vector3(x0 + run * (i + 0.5f), y - (rise + 0.05f) / 2f, StairZ),
                    new Vector3(run, rise + 0.05f, StairW - 0.2f));
                Tile(step, SFFloorT, 1f);
                step.transform.SetParent(root, true);
                // Solid under each tread so the flight is not a set of floating slabs.
                var fill = EditorBuildKit.CreateBox($"SF_MStair_Fill{i:00}",
                    new Vector3(x0 + run * (i + 0.5f), (SFy + y - 0.06f) / 2f, StairZ),
                    new Vector3(run - 0.02f, y - 0.06f - SFy, StairW - 0.24f));
                Tile(fill, SFWallAlt, 1f);
                fill.transform.SetParent(root, true);
            }

            // Top landing: the Morgue's doorway, unbuilt beyond this.
            Tile(EditorBuildKit.CreateBox("SF_MStair_Landing",
                new Vector3(x1 + 1f, TFy - 0.1f, StairZ), new Vector3(2f, 0.2f, StairW)), SFFloorT, 1.5f);
            Tile(EditorBuildKit.CreateBox("SF_MStair_MorgueDoor",
                new Vector3(x1 + 1.95f, TFy + 1.3f, StairZ), new Vector3(0.2f, 2.6f, 2.2f)), SFRust, 1f);

            // The gate. It stands in leg C's wall opening, so the stair is visible through the
            // bars long before it can be used -- you know where the way out is and cannot take it.
            BuildRiddleGate(new Vector3(LegCx + SFhalf + SFt / 2f, SFy, StairZ), StairW - 0.2f);

            AddDimLight(new Vector3(x0 + 1.5f, SFy + 2.4f, StairZ), 0.10f);
            AddDimLight(new Vector3(x1 + 0.6f, TFy + 1.8f, StairZ), 0.09f);

            EditorBuildKit.CreateNoteProp("SF_Note_Morgue",
                new Vector3(LegCx + 1.0f, SFy, StairZ - 1.9f),
                "Assets/_Project/Data/sf_morgue.asset", "sf_morgue", "Chalked On The Stairhead",
                "Down there they kept the ones it was finished with. Nobody signed for them and " +
                "nobody came for them. If you are reading this you have already been counted - the " +
                "only question left is how much you know.", 5f);
        }

        /// <summary>
        /// Ten candle positions, roughly every 8m around the route — exactly one matchbox.
        ///
        /// That agreement is not decorative. The player can light the entire path once, or ration it,
        /// and cannot do both. Change the route length and the match count has to move with it.
        /// </summary>
        private static void BuildCandles()
        {
            var spots = new[]
            {
                new Vector3(1.28f, SFy, 123f),  new Vector3(-1.28f, SFy, 131f),
                new Vector3(1.28f, SFy, 139f),  new Vector3(-1.28f, SFy, 147f),
                new Vector3(1.28f, SFy, 155f),  new Vector3(-1.28f, SFy, 163f),
                new Vector3(-2.2f, SFy, LegBz - 1.28f),
                new Vector3(-8f,  SFy, LegBz - 1.28f),
                new Vector3(LegCx + 1.28f, SFy, 162f), new Vector3(LegCx - 1.28f, SFy, 156f),
            };

            var iron = EditorBuildKit.MakeMaterial(new Color(0.09f, 0.09f, 0.10f));
            // The same model the lobby table uses. Boxes stacked into a candle shape read as a
            // switch on a wall, which is exactly what these looked like.
            var candleModel = ArtKit.LoadModel("Props/CandleFlame/CandleFlame.fbx");

            for (int i = 0; i < spots.Length; i++)
            {
                var root = new GameObject($"SF_Candle{i:00}");
                root.transform.position = spots[i];
                root.AddComponent<Unity.Netcode.NetworkObject>();

                // A cast dish STANDING ON THE FLOOR. The bracket used to float 0.6m up against a
                // wall it never touched, holding a candle that touched nothing either. Floor-standing
                // also puts the pool exactly where this floor needs it: candles are how you read
                // where a room starts and where the floor ends, and a light at knee height throws
                // that across the ground instead of across the wall.
                var sconce = EditorBuildKit.CreateBox($"SF_Candle{i:00}_Sconce",
                    spots[i] + new Vector3(0f, 0.03f, 0f), new Vector3(0.22f, 0.06f, 0.22f));
                EditorBuildKit.SetMaterial(sconce, iron);
                sconce.transform.SetParent(root.transform, true);

                float wickY = 0.98f;
                if (candleModel != null)
                {
                    var inst = ArtKit.Spawn(candleModel, root.transform, $"SF_Candle{i:00}_Body");
                    ArtKit.FitHeight(inst, 0.34f);
                    ArtKit.AutoTexture(inst, "Props/CandleFlame", alphaClip: false, pointFilter: false);
                    ArtKit.GroundAt(inst, spots[i] + new Vector3(0f, 0.06f, 0f));
                    if (ArtKit.TryGetBounds(inst, out var cb)) wickY = cb.max.y - spots[i].y;
                    foreach (var col in inst.GetComponentsInChildren<Collider>(true))
                        UnityObject.DestroyImmediate(col);
                }
                else
                {
                    // Fallback: a turned cylinder, not a stack of cubes.
                    var stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    stick.name = $"SF_Candle{i:00}_Wax";
                    stick.transform.position = spots[i] + new Vector3(0f, 0.23f, 0f);
                    stick.transform.localScale = new Vector3(0.07f, 0.17f, 0.07f);
                    EditorBuildKit.SetMaterial(stick,
                        EditorBuildKit.MakeMaterial(new Color(0.80f, 0.76f, 0.66f)));
                    UnityObject.DestroyImmediate(stick.GetComponent<Collider>());
                    stick.transform.SetParent(root.transform, true);
                }

                // An oval flame: a sphere squashed on X and Z and drawn out on Y, so it tapers the
                // way a flame does instead of sitting there as a glowing cube.
                var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                flame.name = $"SF_Candle{i:00}_Flame";
                flame.transform.position = spots[i] + new Vector3(0f, wickY + 0.035f, 0f);
                flame.transform.localScale = new Vector3(0.035f, 0.085f, 0.035f);
                EditorBuildKit.SetMaterial(flame, EditorBuildKit.MakeEmissive(
                    new Color(1f, 0.58f, 0.16f), new Color(1f, 0.46f, 0.10f)));
                UnityObject.DestroyImmediate(flame.GetComponent<Collider>());
                flame.transform.SetParent(root.transform, true);

                // Nothing on the candle casts a shadow. The dish sitting between the flame and the
                // floor was throwing a hard wedge every time a torch swept past it.
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                var lightGO = new GameObject("Glow");
                lightGO.transform.SetParent(root.transform, false);
                lightGO.transform.position = spots[i] + new Vector3(0f, wickY + 0.02f, 0f);
                var lt = lightGO.AddComponent<Light>();
                lt.type = LightType.Point;
                // Warm orange, not the near-white it was. A candle at 1900K is far redder than a
                // torch bulb, and the difference is what tells you at a glance which light you are
                // standing in -- which matters, because only one of them is safe to carry.
                lt.range = 4.2f;
                lt.intensity = 1.2f;
                lt.color = new Color(1f, 0.53f, 0.20f);
                lt.shadows = LightShadows.None;
                lightGO.AddComponent<LastWard.Core.FlickeringLight>();

                // Range pulled in from 4.2. Ten candles plus the corridor tubes plus a torch overrun
                // URP's per-object additional-light budget, and when that happens objects flick
                // between light sets as the camera turns -- which is the shearing wedge that crawls
                // across the floor. Fewer overlapping pools is the fix that does not cost quality.
                lt.range = 3.4f;

                var poolGO = new GameObject("LightPool");
                poolGO.transform.SetParent(root.transform, false);
                poolGO.transform.position = spots[i] + new Vector3(0f, 0.35f, 0f);
                var pool = poolGO.AddComponent<LastWard.Core.WorldLight>();
                EditorBuildKit.SetFloat(pool, "radius", 3.0f);

                var holder = root.AddComponent<LastWard.Puzzles.CandleHolder>();
                EditorBuildKit.SetRef(holder, "flame", flame.GetComponent<Renderer>());
                EditorBuildKit.SetRef(holder, "glow", lt);
                EditorBuildKit.SetRef(holder, "pool", pool);

                // Aim volume, sized to what is now a knee-high object. At 0.85 with a 0.9 box it
                // was reaching for a candle that is no longer up there.
                var reach = EditorBuildKit.CreateBox($"SF_Candle{i:00}_Reach",
                    spots[i] + new Vector3(0f, 0.32f, 0f), new Vector3(0.5f, 0.64f, 0.5f));
                reach.GetComponent<Renderer>().enabled = false;
                reach.transform.SetParent(root.transform, true);

                lightGO.SetActive(true);
                lt.enabled = false;
                flame.GetComponent<Renderer>().enabled = false;
                pool.enabled = false;
            }
        }

        /// <summary>
        /// Two hiding places, both at the entrance. There are no others on this floor.
        ///
        /// That is what makes a wrong answer at the riddle door cost 78 metres: the only safety is
        /// back at the top of the stairs, and getting there means running the whole U in the dark.
        /// </summary>
        private static void BuildHidingSpots()
        {
            // Kept SOUTH of the first cell mouth (which starts at 125.7) and tight to the wall.
            // At z 123.5/126.5 and x +/-2.0 they were standing inside the cell openings, which is the
            // wardrobe that appeared to be growing through a gate.
            EditorBuildKit.CreateWardrobeHidingSpot("SF_Hide_A",
                new Vector3(1.1f, SFy, 122.0f), "Get inside", -90f);
            EditorBuildKit.CreateWardrobeHidingSpot("SF_Hide_B",
                new Vector3(-1.1f, SFy, 124.2f), "Get inside", 90f);
        }

        /// <summary>
        /// What is left in the cells, and the thing in the atrium.
        ///
        /// Deliberately sparse and weighted toward the far end of the route. The first cells you pass
        /// are empty, which makes the ones further in mean something — a floor where every cell is
        /// full of bones is a floor where bones are wallpaper. The guillotine goes in the atrium
        /// because that is the one room tall enough to hold it and the one room you cross twice.
        /// </summary>
        private static void BuildDressing()
        {
            var parent = new GameObject("SF_Dressing").transform;
            float inner = SFhalf + SFt;

            // Cobwebs, tucked into the back corners INSIDE the cells. Hung in the corridor at
            // ceiling height they read as slabs of rubble floating in mid-air -- an unlit, untextured
            // web mesh at 1.1m across is just a polygon, and it needs a corner to be read as a web.
            for (int i = 0; i < CellZ.Length; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                // texRelPath is a FILE, not a folder -- passing the folder made it look for a
                // texture literally called "textures" and warn once per web. AutoTexture below is
                // what actually dresses it.
                var web = PlaceProp(parent, "SecondFloor/Props/cobwebs/scene.gltf", null, null,
                    $"SF_Web{i}", 0.7f,
                    new Vector3(sx * (inner + 2.6f), SFy + SFh - 0.75f, CellZ[i] + 0.9f),
                    sx < 0 ? 45f : -45f);
                if (web != null)
                {
                    ArtKit.AutoTexture(web, "SecondFloor/Props/cobwebs/textures", alphaClip: true);
                    foreach (var c in web.GetComponentsInChildren<Collider>(true))
                        UnityObject.DestroyImmediate(c);
                    foreach (var r in web.GetComponentsInChildren<Renderer>(true))
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            // Bones and skulls, only in the deeper cells.
            var boneCells = new[] { 3, 4, 5, 6 };
            for (int k = 0; k < boneCells.Length; k++)
            {
                int i = boneCells[k];
                float sx = (i % 2 == 0) ? -1f : 1f;
                string model = (k % 2 == 0) ? "bone_pile" : "pile_of_skulls";
                var pile = PlaceProp(parent, $"SecondFloor/Props/{model}/scene.gltf",
                    null, null, $"SF_Bones{i}", (k % 2 == 0) ? 0.55f : 0.7f,
                    new Vector3(sx * (inner + 1.9f), SFy, CellZ[i] + (k % 2 == 0 ? 0.5f : -0.5f)),
                    47f * k);
                if (pile != null) ArtKit.AutoTexture(pile, $"SecondFloor/Props/{model}/textures");
            }

            // Drag marks on the floor: a body was pulled north up this corridor. They point the way
            // the route goes, which is quietly useful when you are sprinting it in the dark.
            for (int i = 0; i < 5; i++)
            {
                var blood = PlaceProp(parent, "SecondFloor/Props/blood_spatter/scene.gltf",
                    null, null, $"SF_Drag{i}", 0.02f,
                    new Vector3((i % 2 == 0) ? 0.5f : -0.4f, SFy + 0.01f, 129f + i * 8f), 8f * i);
                if (blood != null)
                {
                    ArtKit.AutoTexture(blood, "SecondFloor/Props/blood_spatter/textures", alphaClip: true);
                    foreach (var c in blood.GetComponentsInChildren<Collider>(true))
                        UnityObject.DestroyImmediate(c);
                }
            }

            // The atrium's centrepiece.
            var guill = PlaceProp(parent, "SecondFloor/Props/guillotine/scene.gltf",
                null, null, "SF_Guillotine", 3.6f,
                new Vector3(-4.5f, SFy, LegBz + 1.6f), 195f);
            if (guill != null)
            {
                ArtKit.AutoTexture(guill, "SecondFloor/Props/guillotine/textures");
                // Lit from directly above so it throws a long shadow down the corridor you enter by.
                AddDimLight(new Vector3(-4.5f, SFy + 4.5f, LegBz + 1.6f), 0.22f);
            }

            var pileBig = PlaceProp(parent, "SecondFloor/Props/pile_of_skulls/scene.gltf",
                null, null, "SF_SkullsAtrium", 1.0f,
                new Vector3(-7.4f, SFy, LegBz + 2.4f), 30f);
            if (pileBig != null)
                ArtKit.AutoTexture(pileBig, "SecondFloor/Props/pile_of_skulls/textures");
        }

        /// <summary>
        /// The barred gate across the Morgue stair, and the question on it.
        ///
        /// Built like the cell gates so it reads as the same ironwork, but it does not open to force
        /// — it opens to an answer, and working it scores you knowledge whether or not you are right.
        /// </summary>
        private static void BuildRiddleGate(Vector3 centre, float width)
        {
            var rootGO = new GameObject("SF_RiddleGate");
            rootGO.transform.position = centre;
            rootGO.AddComponent<Unity.Netcode.NetworkObject>();
            var leaf = new GameObject("Leaf").transform;
            leaf.SetParent(rootGO.transform, false);

            foreach (float sz in new[] { -1f, 1f })
            {
                var jamb = EditorBuildKit.CreateBox($"SF_RiddleGate_Jamb{(sz < 0 ? "S" : "N")}",
                    centre + new Vector3(0f, SFh / 2f, sz * width / 2f),
                    new Vector3(0.16f, SFh, 0.15f));
                Tile(jamb, SFRustDk, 1f);
                jamb.transform.SetParent(rootGO.transform, true);
            }
            var head = EditorBuildKit.CreateBox("SF_RiddleGate_Head",
                centre + new Vector3(0f, SFh - 0.09f, 0f), new Vector3(0.16f, 0.18f, width));
            Tile(head, SFRustDk, 1f);
            head.transform.SetParent(rootGO.transform, true);

            int bars = Mathf.Max(2, Mathf.RoundToInt(width / 0.15f));
            for (int i = 1; i < bars; i++)
            {
                float z = centre.z - width / 2f + width * (i / (float)bars);
                var bar = EditorBuildKit.CreateBox($"SF_RiddleGate_Bar{i:00}",
                    new Vector3(centre.x, SFy + SFh / 2f, z), new Vector3(0.07f, SFh - 0.18f, 0.06f));
                Tile(bar, SFRust, 0.7f);
                bar.transform.SetParent(leaf, true);
            }
            foreach (float ry in new[] { 0.7f, 2.1f })
            {
                var rail = EditorBuildKit.CreateBox($"SF_RiddleGate_Rail{(int)(ry * 10)}",
                    centre + new Vector3(0f, ry, 0f), new Vector3(0.085f, 0.1f, width - 0.16f));
                Tile(rail, SFRust, 0.7f);
                rail.transform.SetParent(leaf, true);
            }

            // The plate the question is cut into.
            var plate = EditorBuildKit.CreateBox("SF_RiddleGate_Plate",
                centre + new Vector3(-0.12f, 1.35f, width * 0.34f), new Vector3(0.06f, 0.4f, 0.32f));
            Tile(plate, SFRustDk, 0.5f);
            plate.transform.SetParent(rootGO.transform, true);

            var gate = rootGO.AddComponent<LastWard.Puzzles.RiddleGate>();
            EditorBuildKit.SetRef(gate, "leaf", leaf);
            EditorBuildKit.SetFloat(gate, "slideDistance", width);
            // Placeholder answer. The real one is keyed to the run - see FLOOR2_ASYLUM.md section 5.
            EditorBuildKit.SetString(gate, "answer", "1919");
            Debug.Log("[Build] Riddle gate at the Morgue stair. Placeholder answer 1919 until the " +
                      "run-keyed riddle is written.");
        }

        /// <summary>
        /// The asylum's own Manager.
        ///
        /// There was only ever one, spawned at z=85 on the first floor with roam bounds of 61–111 and
        /// a 34m perception radius. The asylum starts at z=120. It could not see anyone up here, could
        /// not walk up here, and never did — which is why torches, candles and sprinting all went
        /// unanswered on this floor. The design has always said the Manager is on both floors; it just
        /// was not.
        ///
        /// A second instance rather than widening the first one's bounds: one Manager patrolling 60m
        /// of first floor AND 48m of asylum would be absent from both, and the floors are meant to
        /// feel separately occupied.
        /// </summary>
        private static void BuildAsylumManager()
        {
            Vector3 spawn = new Vector3(0f, SFy, 150f);          // mid-spine, north of the cells
            var root = new GameObject("Manager_Asylum");
            root.transform.position = spawn;
            root.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            root.AddComponent<Unity.Netcode.NetworkObject>();
            root.AddComponent<Unity.Netcode.Components.NetworkTransform>();
            var mc = root.AddComponent<LastWard.Entity.ManagerController>();

            // Its floor, not the one below. floorMinY separates them without a zone volume.
            EditorBuildKit.SetFloat(mc, "firstFloorMinY", SFy - 0.9f);
            EditorBuildKit.SetFloat(mc, "roamMinZ", LegAz0 + 3f);
            EditorBuildKit.SetFloat(mc, "roamMaxZ", LegAz1 - 3f);
            // Wider than the first floor's 34m: leg A is 48m of straight corridor, and an entity that
            // cannot perceive the length of its own hallway spends most of the floor doing nothing.
            EditorBuildKit.SetFloat(mc, "perceptionRange", 52f);
            // Its own aggression ramp. The default is 72->110, which are FIRST FLOOR z values -- at
            // z=120+ the ramp reads as fully complete, so this Manager ran at the maximum 3.2x
            // multiplier on every sensing term from the instant you walked in. That is why arriving
            // on the asylum killed you with nothing visible: the meter was filling three times faster
            // than the worst stretch of the floor below.
            EditorBuildKit.SetFloat(mc, "endgameFromZ", LegAz0 + 10f);
            EditorBuildKit.SetFloat(mc, "endgameToZ", LegAz1);
            EditorBuildKit.SetFloat(mc, "endgameMultiplier", 2.2f);

            var model = ArtKit.LoadModel("Characters/Manager/Rig_Pugalo.fbx");
            if (model == null)
            {
                Debug.LogWarning("[Build] Asylum Manager placed without a body.");
                return;
            }
            var visual = ArtKit.Spawn(model, root.transform, "Visual");
            ArtKit.NeutralizeParentScale(visual);
            ArtKit.FitHeight(visual, 2.05f);
            ArtKit.GroundAt(visual, spawn);
            ArtKit.MakeSilhouetteWithEyes(visual, new Color(0.9f, 0.86f, 0.55f), 0.06f);

            const string AN = "Characters/Manager/Animations/";
            ArtKit.SetupManagerAnimator(visual, "Characters/Manager/Rig_Pugalo.fbx",
                new (string, string, bool)[]
                {
                    ("Idle",      AN + "Idle_Pose.fbx",           true),
                    ("Walk",      AN + "Roaming_Around.fbx",      true),
                    ("Crawl",     AN + "Crawling.fbx",            true),
                    ("CrawlBack", AN + "Crawling_Backwards.fbx",  true),
                    ("StrafeL",   AN + "Left_Strafe_Walking.fbx", true),
                    ("StrafeR",   AN + "Right_Strafe_Walking.fbx",true),
                    ("Kill1",     AN + "killing01.fbx",           false),
                    ("Kill2",     AN + "killing02.fbx",           false),
                    ("Kill3",     AN + "killing03.fbx",           false),
                    ("Impact",    AN + "Impact animation.fbx",    false),
                }, "AC_Manager");

            Debug.Log("[Build] Asylum Manager: roams z " + (LegAz0 + 3f) + "-" + (LegAz1 - 3f) +
                      " at y>=" + (SFy - 0.9f) + ", perception 52m.");
        }

        /// <summary>
        /// The Inspector itself.
        ///
        /// No patrol route and no meaningful spawn point — it is not anywhere until it is in front of
        /// you, so all it needs is somewhere offstage to wait. Renderers stay disabled until the
        /// moment it manifests.
        ///
        /// Exported out of Inspector_work.blend as a bind-pose rig plus one FBX per clip. Blender
        /// 5.x's <c>bake_anim_use_all_actions</c> silently exports only the ACTIVE action, so a
        /// single-file export produced a rig with one animation and no arch — the per-clip layout is
        /// not a style preference, it is the only thing that works.
        /// </summary>
        private static void BuildInspector()
        {
            var root = new GameObject("SF_Inspector");
            root.transform.position = new Vector3(LegCx, SFy, LegCz1 + 6f);   // offstage, north of leg C
            root.AddComponent<Unity.Netcode.NetworkObject>();

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            // The Mixamo rig, not the original. Mixamo auto-rigged the bare mesh and its clips are
            // authored against ITS skeleton, so the two cannot coexist -- this replaces the old rig
            // wholesale. Every shape change survives regardless, because the buffing, hips, head and
            // hump were baked into the mesh vertices rather than into bones.
            const string rig = "Characters/InspectorMx/rig.fbx";
            const string anim = "Characters/InspectorMx/Animations/";
            var model = ArtKit.LoadModel(rig);

            if (model != null)
            {
                var inst = ArtKit.Spawn(model, body, "Inspector_Body");
                // 3.6m: taller than every ceiling on the route except the atrium and the records
                // room, which is what forces the stoop and what makes standing upright mean
                // something. The arch is baked into the clips, not applied here.
                ArtKit.FitHeight(inst, 3.6f);
                ArtKit.AutoTexture(inst, "Characters/InspectorMx/textures",
                                   alphaClip: false, pointFilter: false);
                if (ArtKit.TryGetBounds(inst, out var b))
                    inst.transform.position += root.transform.position -
                        new Vector3(b.center.x, b.min.y, b.center.z);

                ArtKit.SetupManagerAnimator(inst, rig, new[]
                {
                    ("Idle", anim + "Idle.fbx", true),
                    ("Run",  anim + "Run.fbx",  true),
                    // One clip for the whole execution: it reaches, lifts, and drives you down. The
                    // victim's camera is animated against this same duration in InspectorController.
                    ("Kill", anim + "Kill.fbx", false),
                }, "AC_Inspector");

                foreach (var col in inst.GetComponentsInChildren<Collider>(true))
                    UnityObject.DestroyImmediate(col);
                Debug.Log("[Build] Inspector: real rig attached (3.6m, 6 clips wired).");
            }
            else
            {
                // Placeholder so the mechanics stay testable if the export is missing.
                var torso = EditorBuildKit.CreateBox("SF_Inspector_Torso",
                    root.transform.position + new Vector3(0f, 2.2f, 0f), new Vector3(1.5f, 1.7f, 0.9f));
                EditorBuildKit.SetMaterial(torso, EditorBuildKit.MakeMaterial(new Color(0.16f, 0.13f, 0.13f)));
                UnityObject.DestroyImmediate(torso.GetComponent<Collider>());
                torso.transform.SetParent(body, true);
                Debug.LogWarning("[Build] Inspector rig not found — using placeholder box.");
            }

            var ctrl = root.AddComponent<LastWard.Entity.InspectorController>();
            EditorBuildKit.SetRef(ctrl, "body", body);
            // Only counts players on the asylum floor. SFy is 6.4 and the first floor tops out well
            // below that, so this separates them without needing a zone volume.
            EditorBuildKit.SetFloat(ctrl, "floorMinY", SFy - 0.9f);
            EditorBuildKit.SetFloat(ctrl, "floorMaxY", TFy + 6f);
            var animator = body.GetComponentInChildren<Animator>();
            if (animator != null) EditorBuildKit.SetRef(ctrl, "animator", animator);

            Debug.Log("[Build] Inspector wired: marks the highest knowledge score on the asylum floor, " +
                      "40-120s clock, ledger can cancel or transfer it.");
        }

        /// <summary>The lore that teaches the Inspector's habit, without teaching the answer.</summary>
        private static void BuildMatchboxAndLore()
        {
            EditorBuildKit.CreateNoteProp("SF_Note_Candles",
                new Vector3(1.2f, SFy, 122f),
                "Assets/_Project/Data/sf_candles.asset", "sf_candles", "By The Stairhead",
                "Light them as you go and it will not care - it is not the flame it wants, it is you " +
                "standing in it. Strike, set it down, and step out of the ring. And do not light them " +
                "all at once. You will want the far end of the hall lit far more than you want this " +
                "end lit, and there are only ten in the box.", 4f);

            EditorBuildKit.CreateNoteProp("SF_Note_Inspector",
                new Vector3(-1.2f, SFy, 143f),
                "Assets/_Project/Data/sf_inspector.asset", "sf_inspector", "Pinned Above A Bed",
                "It does not hunt the loud ones or the bright ones. It hunts whoever has been paying " +
                "attention. I have watched it walk past three of us to reach the one who had been " +
                "reading. Whatever you do, do not be the one who knows the most - and if you are, do " +
                "not be near anyone when it comes.", 5f);
        }
    }
}
#endif
