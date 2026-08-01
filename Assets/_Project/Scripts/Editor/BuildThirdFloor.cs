#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace LastWard.EditorTools
{
    /// <summary>
    /// The Morgue — third floor. A death maze, and the last thing above the basement.
    ///
    /// Built as a 4x4 grid of identical cold rooms rather than as authored spaces, and that is the
    /// whole design. The floors below are legible: a corridor with rooms off it, a U you can hold in
    /// your head. Here every cell is the same size with the same drawers and the same tiles, so you
    /// navigate by the <i>contents</i> of rooms — a bone heap, a spatter, a table — and the moment the
    /// floor rearranges those landmarks stop meaning what they meant.
    ///
    /// Two things make it a maze rather than a grid: openings are asymmetric (you can reach a cell one
    /// way and not come back the same way), and roughly a third of the boundaries are <b>shiftable</b>
    /// — they carry both a wall and an opening, and MorgueShifter swaps which is real once both keys
    /// are taken.
    /// </summary>
    public static partial class BuildM5Level
    {
        // ---- surfaces -------------------------------------------------------------------------
        private const string TFFloor = "ThirdFloor/Textures/TF_Metal_Floor.png";
        private const string TFFloorDark = "ThirdFloor/Textures/TF_Floor_Dark.png";
        private const string TFWall = "ThirdFloor/Textures/TF_Metal_Wall.png";
        private const string TFWallBlood = "ThirdFloor/Textures/TF_Blood_Wall.png";
        private const string TFStone = "ThirdFloor/Textures/TF_Stone_Wall.png";
        private const string TFDrawer = "ThirdFloor/Textures/TF_Drawer.png";
        private const string TFGrate = "ThirdFloor/Textures/TF_Grate.png";
        private const string TFBloodfall = "ThirdFloor/Textures/TF_Bloodfall.png";

        // ---- the grid -------------------------------------------------------------------------
        private const float Cell = 7f;              // cell centre spacing
        private const float Room = 6.2f;            // floor plate per cell
        private const float TFh = 3.2f;             // ceiling height
        private const float TFt = 0.3f;
        private const float TFx0 = -4f;             // cell (0,0) centre — beside the Morgue stairhead
        private const float TFz0 = 158f;
        private const int Cols = 4, Rows = 4;

        private static readonly List<GameObject> shiftBefore = new List<GameObject>();
        private static readonly List<GameObject> shiftAfter = new List<GameObject>();

        private static Vector3 CellAt(int c, int r) =>
            new Vector3(TFx0 + c * Cell, TFy, TFz0 + r * Cell);

        /// <summary>
        /// Openings on the way IN. "c,r>c,r" means the two cells connect.
        ///
        /// Deliberately not a spanning tree — there are loops, and there are cells reachable from one
        /// side only. A perfect maze is solvable by keeping a hand on one wall; this is not a maze to
        /// be solved, it is a floor to be survived.
        /// </summary>
        private static readonly string[] OpenIn =
        {
            "0,0>1,0", "1,0>2,0", "2,0>3,0",
            "0,0>0,1", "1,0>1,1", "3,0>3,1",
            "0,1>1,1", "2,1>3,1",
            "1,1>1,2", "2,1>2,2",
            "1,2>2,2", "2,2>3,2",
            "1,2>1,3", "3,2>3,3",
            "2,3>3,3",
        };

        /// <summary>Openings on the way OUT. Overlaps deliberately, but not much.</summary>
        private static readonly string[] OpenOut =
        {
            "0,0>0,1", "0,1>0,2", "0,2>0,3",
            "0,3>1,3", "1,3>2,3",
            "2,3>2,2", "2,2>2,1", "2,1>2,0",
            "2,0>1,0",
            "1,1>2,1", "3,2>3,3", "3,1>3,2",
        };

        public static void CreateThirdFloor()
        {
            shiftBefore.Clear();
            shiftAfter.Clear();

            var inSet = new HashSet<string>(OpenIn);
            var outSet = new HashSet<string>(OpenOut);

            BuildMorgueShell(inSet, outSet);
            BuildMorgueRooms();
            BuildMorgueKeys();
            BuildBasementStair();
            BuildMorgueLore();
            BuildMorgueDirectors();

            Debug.Log($"[Build] Morgue: {Cols}x{Rows} cells, {inSet.Count} openings in, " +
                      $"{outSet.Count} out, {shiftBefore.Count + shiftAfter.Count} shifting walls.");
        }

        // -----------------------------------------------------------------------------------------

        private static void BuildMorgueShell(HashSet<string> inSet, HashSet<string> outSet)
        {
            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                {
                    var at = CellAt(c, r);
                    // Alternating floor texture. Two tiles that are nearly the same is worse than one
                    // — you cannot be sure whether you have been here or somewhere like it.
                    Tile(EditorBuildKit.CreateBox($"TF_Floor_{c}{r}",
                        at + new Vector3(0f, -0.1f, 0f), new Vector3(Room, 0.2f, Room)),
                        (c + r) % 3 == 0 ? TFFloorDark : TFFloor, 2f);
                    Tile(EditorBuildKit.CreateBox($"TF_Ceil_{c}{r}",
                        at + new Vector3(0f, TFh, 0f), new Vector3(Room, 0.2f, Room)), TFGrate, 2f);
                    AddDimLight(at + new Vector3(0f, TFh - 0.5f, 0f), 0.07f);
                }

            // Boundaries. Every edge between two cells, plus the outer perimeter.
            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                {
                    if (c + 1 < Cols) Boundary(c, r, c + 1, r, true, inSet, outSet);
                    if (r + 1 < Rows) Boundary(c, r, c, r + 1, false, inSet, outSet);
                }
            for (int c = 0; c < Cols; c++)
            {
                Perimeter(CellAt(c, 0) + new Vector3(0f, 0f, -Cell / 2f), true);
                Perimeter(CellAt(c, Rows - 1) + new Vector3(0f, 0f, Cell / 2f), true);
            }
            for (int r = 0; r < Rows; r++)
            {
                Perimeter(CellAt(0, r) + new Vector3(-Cell / 2f, 0f, 0f), false);
                Perimeter(CellAt(Cols - 1, r) + new Vector3(Cell / 2f, 0f, 0f), false);
            }
        }

        private static void Perimeter(Vector3 at, bool alongX)
        {
            var size = alongX ? new Vector3(Cell, TFh, TFt) : new Vector3(TFt, TFh, Cell);
            Tile(EditorBuildKit.CreateBox("TF_Edge", at + new Vector3(0f, TFh / 2f, 0f), size), TFStone, 2f);
        }

        /// <summary>
        /// One boundary between two cells. Solid, an opening, or a shifting one that is both.
        /// </summary>
        private static void Boundary(int c0, int r0, int c1, int r1, bool alongX,
                                     HashSet<string> inSet, HashSet<string> outSet)
        {
            string key = $"{c0},{r0}>{c1},{r1}";
            bool openIn = inSet.Contains(key);
            bool openOut = outSet.Contains(key);
            Vector3 at = (CellAt(c0, r0) + CellAt(c1, r1)) * 0.5f;
            string tag = $"TF_W_{c0}{r0}_{c1}{r1}";

            if (openIn && openOut) { DoorFrame(at, alongX, tag); return; }   // always passable
            if (!openIn && !openOut) { WallFull(at, alongX, tag, null); return; }

            // Shifting. Both pieces are built; MorgueShifter decides which is real.
            if (openIn)
            {
                DoorFrame(at, alongX, tag);
                var w = WallFull(at, alongX, tag + "_shut", TFWallBlood);
                w.SetActive(false);
                shiftAfter.Add(w);          // appears on the way out: this route closes
            }
            else
            {
                var w = WallFull(at, alongX, tag + "_open", TFStone);
                shiftBefore.Add(w);         // vanishes on the way out: a new route appears
            }
        }

        private static GameObject WallFull(Vector3 at, bool alongX, string name, string tex)
        {
            var size = alongX ? new Vector3(TFt, TFh, Cell) : new Vector3(Cell, TFh, TFt);
            var go = EditorBuildKit.CreateBox(name, at + new Vector3(0f, TFh / 2f, 0f), size);
            Tile(go, tex ?? TFWall, 2f);
            return go;
        }

        /// <summary>A gap with a lintel and two jambs, so an opening still reads as built.</summary>
        private static void DoorFrame(Vector3 at, bool alongX, string name)
        {
            float half = Cell / 2f, gap = 1.5f;
            Vector3 along = alongX ? Vector3.forward : Vector3.right;
            for (int s = -1; s <= 1; s += 2)
            {
                float len = half - gap;
                Vector3 c = at + along * (s * (gap + len / 2f));
                var size = alongX ? new Vector3(TFt, TFh, len) : new Vector3(len, TFh, TFt);
                Tile(EditorBuildKit.CreateBox($"{name}_J{s}", c + new Vector3(0f, TFh / 2f, 0f), size),
                    TFWall, 2f);
            }
            var head = alongX ? new Vector3(TFt, 0.7f, gap * 2f) : new Vector3(gap * 2f, 0.7f, TFt);
            Tile(EditorBuildKit.CreateBox($"{name}_H", at + new Vector3(0f, TFh - 0.35f, 0f), head),
                TFWall, 1.5f);
        }

        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// What is in the rooms. This is the only way to tell them apart, which is why the shift is
        /// cruel — the landmarks stay put while the routes between them stop being true.
        /// </summary>
        private static void BuildMorgueRooms()
        {
            var parent = new GameObject("TF_Dressing").transform;

            // Drawer banks. Three walls of them, all identical, in cells that are not adjacent — so
            // finding one tells you nothing about where you are.
            foreach (var (c, r) in new[] { (1, 0), (1, 1), (3, 1), (2, 3) })
                DrawerBank(CellAt(c, r), parent);

            // Bone and rubble, thickening toward the far corner.
            var piles = new[] { (0, 1, "bone_pile"), (1, 2, "pile_of_skulls"), (3, 2, "bone_pile"),
                                (3, 3, "pile_of_skulls"), (2, 2, "bone_pile") };
            for (int i = 0; i < piles.Length; i++)
            {
                var (c, r, model) = piles[i];
                var p = PlaceProp(parent, $"SecondFloor/Props/{model}/scene.gltf", null, null,
                    $"TF_Bones{i}", 0.6f + i * 0.12f,
                    CellAt(c, r) + new Vector3(i % 2 == 0 ? 1.9f : -1.7f, 0f, i % 3 == 0 ? 1.5f : -1.4f),
                    61f * i);
                if (p != null) ArtKit.AutoTexture(p, $"SecondFloor/Props/{model}/textures");
            }

            // Drag marks. All of them point the same way — toward the basement stair — because
            // everything on this floor was taken there eventually.
            for (int i = 0; i < 7; i++)
            {
                int c = i % Cols, r = (i / Cols) % Rows;
                var b = PlaceProp(parent, "SecondFloor/Props/blood_spatter/scene.gltf", null, null,
                    $"TF_Drag{i}", 0.02f,
                    CellAt(c, r) + new Vector3(0.4f * ((i % 3) - 1), 0.01f, 0.5f * ((i % 4) - 2)), 12f * i);
                if (b != null)
                {
                    ArtKit.AutoTexture(b, "SecondFloor/Props/blood_spatter/textures", alphaClip: true);
                    foreach (var col in b.GetComponentsInChildren<Collider>(true))
                        UnityObject.DestroyImmediate(col);
                }
            }

            // Blood running down the walls of the two cells the keys are in. The floor tells you
            // where the worst things are before it tells you why.
            foreach (var (c, r) in new[] { (1, 2), (3, 2) })
            {
                var fall = EditorBuildKit.CreateBox($"TF_Fall_{c}{r}",
                    CellAt(c, r) + new Vector3(0f, TFh / 2f, Room / 2f - 0.2f),
                    new Vector3(Room - 0.6f, TFh - 0.4f, 0.06f));
                Tile(fall, TFBloodfall, 2f);
                UnityObject.DestroyImmediate(fall.GetComponent<Collider>());
            }

            // The autopsy table, in the middle cell. One in the whole building.
            var slab = EditorBuildKit.CreateBox("TF_Slab",
                CellAt(2, 1) + new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.1f, 2.1f));
            Tile(slab, TFDrawer, 1f);
            foreach (float sx in new[] { -0.35f, 0.35f })
                foreach (float sz in new[] { -0.85f, 0.85f })
                    Tile(EditorBuildKit.CreateBox("TF_SlabLeg",
                        CellAt(2, 1) + new Vector3(sx, 0.22f, sz), new Vector3(0.07f, 0.45f, 0.07f)),
                        TFDrawer, 0.6f);
            AddDimLight(CellAt(2, 1) + new Vector3(0f, 2.3f, 0f), 0.16f);
        }

        private static void DrawerBank(Vector3 at, Transform parent)
        {
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 3; row++)
                {
                    var d = EditorBuildKit.CreateBox("TF_Drawer",
                        at + new Vector3(-Room / 2f + 0.22f, 0.55f + row * 0.62f, -1.6f + col * 1.05f),
                        new Vector3(0.34f, 0.55f, 0.95f));
                    Tile(d, TFDrawer, 0.7f);
                    d.transform.SetParent(parent, true);
                    // One in six hangs open. Never the same one, but never random either — a fixed
                    // pattern reads as neglect, whereas true randomness reads as noise.
                    if ((col + row) % 6 == 2)
                        d.transform.position += new Vector3(0.45f, 0f, 0f);
                }
        }

        // -----------------------------------------------------------------------------------------

        private static void BuildMorgueKeys()
        {
            // Two keys, in the two cells furthest apart by the INWARD route. Both are needed and
            // neither opens anything alone, so a solo player walks the whole floor twice and a pair
            // has to split up on the worst floor in the building.
            EditorBuildKit.CreateToolPickup("basement_key_a", "basement key",
                CellAt(1, 2) + new Vector3(1.4f, 0.35f, -1.2f), new Color(0.75f, 0.68f, 0.35f),
                standaloneModel: null, standaloneTextures: null, targetSize: 0.16f);
            EditorBuildKit.CreateToolPickup("basement_key_b", "basement key",
                CellAt(3, 2) + new Vector3(-1.5f, 0.35f, 1.3f), new Color(0.62f, 0.66f, 0.70f),
                standaloneModel: null, standaloneTextures: null, targetSize: 0.16f);
        }

        private static void BuildBasementStair()
        {
            var at = CellAt(1, 3);
            var root = new GameObject("TF_BasementStair");
            root.transform.position = at;
            root.AddComponent<Unity.Netcode.NetworkObject>();

            // The shaft down. Only the head of it is built — the basement itself is not this floor's
            // problem, and a stair that visibly continues into black is worth more than a landing.
            // No floor plate over the shaft mouth - the steps are what you stand on going down.
            for (int i = 0; i < 6; i++)
                Tile(EditorBuildKit.CreateBox($"TF_Down{i}",
                    at + new Vector3(0f, -0.2f - i * 0.2f, 1.0f + i * 0.3f),
                    new Vector3(2.4f, 0.2f, 0.3f)), TFStone, 1f);

            var door = EditorBuildKit.CreateBox("TF_BasementDoor",
                at + new Vector3(0f, 1.4f, 0.4f), new Vector3(2.4f, 2.8f, 0.2f));
            Tile(door, TFDrawer, 1f);
            var gate = door.AddComponent<LastWard.Puzzles.BasementDoor>();
            EditorBuildKit.SetRef(gate, "shifter", UnityObject.FindFirstObjectByType<LastWard.Puzzles.MorgueShifter>());
            door.AddComponent<Unity.Netcode.NetworkObject>();

            AddDimLight(at + new Vector3(0f, 2.4f, 0.6f), 0.12f);
        }

        /// <summary>
        /// The Morgue's paperwork, and the first place the building explains itself.
        ///
        /// Three rules held from the ground floor up: never name it, never agree on what this place
        /// was, contradict each other. They are relaxed here — not abandoned. This is the floor where
        /// the arithmetic becomes legible, because the basement is where it gets paid, and a player
        /// who arrives down there without understanding the price has been cheated rather than
        /// frightened.
        ///
        /// What the notes must land, without any of them stating it outright:
        ///   1. Discharge is a TRANSFER, not a release. The register must balance.
        ///   2. Therefore one leaving means one staying. Arriving as a group is arriving with the fee
        ///      already owed.
        ///   3. A single person owes nothing to anyone but the book — and the book will only sign for
        ///      someone who can account for the whole of it.
        /// </summary>
        private static void BuildMorgueLore()
        {
            EditorBuildKit.CreateNoteProp("TF_Note_Intake",
                CellAt(0, 0) + new Vector3(1.6f, 0f, -1.4f),
                "Assets/_Project/Data/tf_intake.asset", "tf_intake", "Cold Room - Intake Sheet",
                "Nothing is stored here. Storage implies collection. Everything that comes through " +
                "these doors is in transit and the drawers are a formality for the ones who arrive " +
                "before their paperwork does. Below is where they are processed and below is where " +
                "they stay. I have signed for nine hundred and forty and I have never once seen the " +
                "count go down.", 6f);

            EditorBuildKit.CreateNoteProp("TF_Note_Discharge",
                CellAt(2, 1) + new Vector3(-1.8f, 0f, 1.2f),
                "Assets/_Project/Data/tf_discharge.asset", "tf_discharge", "Standing Order - Discharge",
                "A discharge is not a release. It is a TRANSFER, and a transfer requires both halves " +
                "or the register does not balance and he will not sign. One out, one in. That is the " +
                "whole of the arrangement and it has never once been waived. If you have come here in " +
                "company then the second half is already standing next to you and you have simply not " +
                "said it out loud yet. Decide upstairs. Do not make him choose - he chooses by weight " +
                "of paperwork, and he is not sentimental about which of you knows the most.", 10f);

            EditorBuildKit.CreateNoteProp("TF_Note_Fee",
                CellAt(3, 1) + new Vector3(1.5f, 0f, -1.5f),
                "Assets/_Project/Data/tf_fee.asset", "tf_fee", "Scratched Into A Drawer Front",
                "THE FEE IS NOT MONEY. Four of us came in. Three signatures on the sheet and the " +
                "fourth line left open, and we spent two days pretending we did not know what the " +
                "open line was for. It is for whoever is still standing in the room when the rest of " +
                "you have gone down. He does not take the weakest. He takes the one the book knows " +
                "best. God forgive me, I read everything I could find because I thought knowing more " +
                "would get me out.", 10f);

            EditorBuildKit.CreateNoteProp("TF_Note_Alone",
                CellAt(1, 2) + new Vector3(-1.4f, 0f, -1.6f),
                "Assets/_Project/Data/tf_alone.asset", "tf_alone", "Folded Into A Key Envelope",
                "If you are on your own then there is no fee, and that is not mercy - it is that " +
                "there is nobody left to charge. What he wants from a single applicant is an ACCOUNT. " +
                "The whole of it, start to end, every name and every number, and he will know the " +
                "moment you are guessing. The ones who go down there half-informed do not come back " +
                "up and they do not stop either. You have heard them. That is what the walking is.", 10f);

            EditorBuildKit.CreateNoteProp("TF_Note_Below",
                CellAt(2, 3) + new Vector3(1.7f, 0f, 1.3f),
                "Assets/_Project/Data/tf_below.asset", "tf_below", "Last Page Of The Duty Log",
                "All three of them are down there and they do not take turns. The blind one at the " +
                "desk who hears you, the tall one in the hall who sees your light, the one that reads " +
                "what you have learned - and in one room together, so there is no way to be quiet and " +
                "dark and ignorant at the same time. You need a light to find the door. You need to " +
                "move to reach it. You need to KNOW where it is. Every one of those is fatal to " +
                "something down there. That is not cruelty, it is just what happens when three rules " +
                "meet in one room. Whatever you do, do not go down there owing anything.", 12f);

            EditorBuildKit.CreateNoteProp("TF_Note_Return",
                CellAt(1, 3) + new Vector3(-1.6f, 0f, -1.3f),
                "Assets/_Project/Data/tf_return.asset", "tf_return", "Nailed Beside The Stairhead",
                "The floor is not the same on the way out. I do not mean you forget it - I mean the " +
                "doors are in different walls and the stair you came up is a cupboard now. There is no " +
                "trick to it and there is nothing to work out. Walk. Keep walking. Do not stop in a " +
                "room to get your bearings and DO NOT TURN ROUND to see how far you have come, " +
                "because the corridor behind you is the part that is listening, and it comes when it " +
                "is looked at. I have watched a man stand still for eleven seconds. Only the light " +
                "came back.", 12f);
        }

        private static void BuildMorgueDirectors()
        {
            var shifterGO = new GameObject("MorgueShifter");
            shifterGO.transform.position = CellAt(1, 1);
            shifterGO.AddComponent<Unity.Netcode.NetworkObject>();
            var shifter = shifterGO.AddComponent<LastWard.Puzzles.MorgueShifter>();
            EditorBuildKit.SetRefArray(shifter, "beforeOnly", shiftBefore.ToArray());
            EditorBuildKit.SetRefArray(shifter, "afterOnly", shiftAfter.ToArray());

            var dirGO = new GameObject("ReturnTripDirector");
            dirGO.transform.position = CellAt(1, 1);
            dirGO.AddComponent<Unity.Netcode.NetworkObject>();
            var dir = dirGO.AddComponent<LastWard.Entity.ReturnTripDirector>();
            EditorBuildKit.SetFloat(dir, "floorMinY", TFy - 0.9f);
            EditorBuildKit.SetFloat(dir, "floorMaxY", TFy + 6f);
            // Out is back toward the stair you arrived by, at the low-Z corner.
            EditorBuildKit.SetVector3(dir, "wayOut", new Vector3(0f, 0f, -1f));

            // Wire the door to the shifter now that it exists.
            var door = UnityObject.FindFirstObjectByType<LastWard.Puzzles.BasementDoor>();
            if (door != null) EditorBuildKit.SetRef(door, "shifter", shifter);
        }
    }
}
#endif
