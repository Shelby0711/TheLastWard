#if UNITY_EDITOR
using System.Collections.Generic;
using LastWard.Core;
using LastWard.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace LastWard.EditorTools
{
    /// <summary>
    /// The real level (distinct from BuildM2NetworkSetup's single-room systems sandbox), now
    /// complete end to end per PROTOTYPE_PLAN.md §13/§14: Exterior -> Lobby -> Orphan Ward ->
    /// Service Corridor -> Exit Route. Safe to re-run; overwrites prefab and scene.
    /// </summary>
    public static class BuildM5Level
    {
        private const string ScenePath = "Assets/_Project/Scenes/M5_Level.unity";

        [MenuItem("The Last Ward/Build M5 Level (Full)")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Exit Play Mode before building the level.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("Build M5 Level cancelled.");
                return;
            }

            EditorBuildKit.EnsureProjectSettings();
            var playerPrefab = EditorBuildKit.BuildPlayerPrefab();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            SetGlobalAtmosphere();
            CreateExteriorZone();
            CreateHospitalFacade();
            CreateLobbyZone();
            CreateWardZone();
            CreateCorridorZone();
            CreateExitRouteZone();
            CreateFloorStairs();
            CreateBlindLore();
            CreateFirstFloor();
            CreateZoneThreshold();

            EditorBuildKit.CreateBootstrapCamera(new Vector3(0f, 1.6f, -11f));
            EditorBuildKit.CreateNetworkManager(playerPrefab);
            var sessionManager = EditorBuildKit.CreateSessionManager();
            // NGO otherwise spawns every player at the prefab's saved world origin, which in this
            // layout is right on the Lobby doorway threshold, not "in the Exterior near the car".
            EditorBuildKit.SetVector3(sessionManager, "spawnPosition", new Vector3(0f, 1f, -13f));
            EditorBuildKit.CreateKnowledgeService();
            CreateObjectiveTracker();
            // The building's voice: a continuous bed plus irregular distant one-shots.
            new GameObject("AmbienceDirector").AddComponent<LastWard.Audio.AmbienceDirector>();
            var aftermathTemplates = EditorBuildKit.CreateAftermathTemplates();
            EditorBuildKit.CreateAftermathManager(aftermathTemplates);
            CreateAftermathAnchors();
            EditorBuildKit.CreateConnectionUI();
            CreateObjectiveUI();
            CreateDiscoveryUI();
            CreateLoreNotes();
            // Corridor presence: steps, slams, crying, lights going red — none of it the Entity.
            new GameObject("HauntingDirector").AddComponent<HauntingDirector>();
            // Arms the moment the players start working the corridor's locks and containers.
            new GameObject("ChantingDirector").AddComponent<ChantingDirector>();
            CreateHidingSpots();

            // Gates the actual passage into the Ward — solving it is what the objective HUD's
            // "Restore power." -> "Push further into the ward." transition represents.
            EditorBuildKit.CreateFusePuzzle(
                // Hinge at the LEFT EDGE of the 2m gap, panel spanning the full opening.
                "Door_ToWard", new Vector3(-1f, 0f, 10f),
                // Breaker box mounted ON the north wall (surface z=9.85) rather than floating in
                // mid-room, and clear of the doorway at x[-1,1].
                new Vector3(2.4f, 1.4f, 9.8f),
                new Vector3(-3f, 0.3f, 3f), new Vector3(3f, 0.3f, 3f),
                new Vector3(-3.2f, 0f, 7.6f),   // on the floor by the breaker box
                out var fusePickups);
            ShuffleFuseSpawns(fusePickups);

            CreateRecordCodePuzzleInWard();
            // The intercom still gates Door_FinalExit — but that door now opens onto the STAIRS, not
            // the outside. The old "You Escaped" trigger and the one-slot choice zone used to sit
            // just past it, so reaching the stairs ended the run. The real ending now lives up/down
            // the floors, so neither is created here any more. (Methods kept for when the ending is
            // rebuilt around the basement / upper floors.)
            CreateIntercomPuzzleInExit();

            var waypoints = CreatePatrolWaypoints();
            EditorBuildKit.BakeNavMesh();
            // Spawns deep in the Service Corridor rather than in the Lobby the players walk into.
            // Starting it on top of the first puzzle is what made every run open the same way.
            EditorBuildKit.CreateEntity(waypoints, new Vector3(0f, 1f, 40f));
            CreateManager();

            ApplyArtPass();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildKit.FixSceneNetworkObjectHashes();
            EditorSceneManager.SaveScene(scene, ScenePath);

            RegisterInBuildSettings();

            Debug.Log("M5 level built (full: Exterior/Lobby/Ward/Corridor/Exit). Scene: " + ScenePath +
                ". Spawn in the Exterior near the car -> Lobby (fuse puzzle, Door_ToWard) -> Ward " +
                "(patient files, code 482, Door_RecordsNook is a side nook not the main path) -> " +
                "Service Corridor (loop around the center pillar + a dead-end alcove to the west) -> " +
                "Exit Route (intercom stations, order from the radio log, Door_FinalExit). Crossing " +
                "past the final door seals it for everyone else — only the first player through gets " +
                "'YOU ESCAPED.', everyone else sees 'THE HOSPITAL KEEPS YOU.' Entity's patrol route " +
                "covers Lobby/Ward/Corridor (not the Exit), but it can still chase you into the Exit. " +
                "Requires UGS/Relay linked, same as M2.");
        }

        private static void RegisterInBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            const string m1 = "Assets/_Project/Scenes/M1_TestScene.unity";
            const string m2 = "Assets/_Project/Scenes/M2_TestScene.unity";
            if (System.IO.File.Exists(m1)) scenes.Add(new EditorBuildSettingsScene(m1, true));
            if (System.IO.File.Exists(m2)) scenes.Add(new EditorBuildSettingsScene(m2, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void SetGlobalAtmosphere()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            // Near black. The flashlight is meant to be the reason you can see anything indoors;
            // any real ambient term makes rooms legible without it and kills the whole tension.
            RenderSettings.ambientLight = new Color(0.012f, 0.012f, 0.016f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.02f, 0.02f, 0.03f);
            RenderSettings.fogDensity = 0.04f;
            RenderSettings.skybox = null;
        }

        // Exterior spans x:[-15,15], z:[-35,0] — ground reaches exactly to z=0 where the Lobby
        // floor begins (no gap between them), open/no ceiling; fog stands in for "can't see far"
        // isolation rather than literal boundary walls doing that work.
        private static void CreateExteriorZone()
        {
            var groundMat = EditorBuildKit.MakeMaterial(new Color(0.12f, 0.13f, 0.1f));
            EditorBuildKit.SetMaterial(
                EditorBuildKit.CreateBox("Exterior_Ground", new Vector3(0f, -0.1f, -17.5f), new Vector3(30f, 0.2f, 35f)), groundMat);

            var wallMat = EditorBuildKit.MakeMaterial(new Color(0.08f, 0.08f, 0.08f));
            float wh = 4f;
            // Far boundary, well south of the car — stops players wandering off into the fog.
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Exterior_Boundary_Far", new Vector3(0f, wh / 2f, -35f), new Vector3(30f, wh, 0.3f)), wallMat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Exterior_Boundary_E", new Vector3(15f, wh / 2f, -17.5f), new Vector3(0.3f, wh, 35f)), wallMat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Exterior_Boundary_W", new Vector3(-15f, wh / 2f, -17.5f), new Vector3(0.3f, wh, 35f)), wallMat);

            // Broken car: two greybox blocks read as chassis + cabin. No wheels — "broken down" fits.
            var carMat = EditorBuildKit.MakeMaterial(new Color(0.25f, 0.05f, 0.05f));
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Car_Chassis", new Vector3(0f, 0.5f, -15f), new Vector3(1.6f, 0.7f, 3.2f)), carMat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Car_Cabin", new Vector3(0f, 1.05f, -15.5f), new Vector3(1.4f, 0.6f, 1.6f)), carMat);

            var carLight = new GameObject("CarInteriorLight").AddComponent<Light>();
            carLight.type = LightType.Point;
            carLight.range = 6f;
            carLight.intensity = 0.25f;
            carLight.color = new Color(1f, 0.85f, 0.6f);
            carLight.transform.position = new Vector3(0f, 1f, -15f);

            EditorBuildKit.CreateNoteProp("Note_Tutorial", new Vector3(1.45f, 0.62f, -14.6f),
                "Assets/_Project/Data/TutorialNoteClue.asset", "tutorial_reading", "A Torn Page",
                "Someone wrote this and never came back for it. Reading it feels like taking something that isn't yours.\n\n" +
                "(Every note you read here matters more than it looks like it should.)", 1f);
        }

        // The hospital seen from the yard, built procedurally instead of using the scanned Riga
        // building — that asset was ~150MB (a 64MB mesh plus 4K textures) for something only ever
        // viewed across a fogged yard at night, and it stalled play-mode entry badly enough to look
        // like a hang.
        //
        // Built here rather than in the art pass because it's real level geometry: the slabs are
        // solid and get included in the NavMesh bake. That also closes a pre-existing hole — the
        // Lobby's south wall only spans x:[-5,5], so before this a player could walk north past z=0
        // out at x=-10 and drop off the end of the world, since the Exterior ground stops at z=0.
        // The doorway gap at x:[-1,1] lines up exactly with the Lobby's.
        private static void CreateHospitalFacade()
        {
            const float height = 18f;
            const float thickness = 0.7f;
            const float faceZ = -0.35f;      // spans z:[-0.7, 0], meeting the Lobby's south wall
            const float halfDoor = 1f;       // matches the Lobby doorway gap
            const float edge = 16f;

            var parent = new GameObject("Hospital_Facade").transform;
            var concrete = EditorBuildKit.MakeMaterial(new Color(0.17f, 0.17f, 0.16f));
            var stain = EditorBuildKit.MakeMaterial(new Color(0.1f, 0.1f, 0.09f));
            var glass = EditorBuildKit.MakeMaterial(new Color(0.015f, 0.016f, 0.02f));
            // A handful of windows keep a sick, faint glow — enough to suggest the building isn't
            // as empty as it looks, without lighting anything.
            var litGlass = EditorBuildKit.MakeEmissive(new Color(0.05f, 0.05f, 0.04f), new Color(0.12f, 0.11f, 0.06f));

            float wingWidth = (edge - halfDoor) / 2f;
            foreach (float side in new[] { -1f, 1f })
            {
                var slab = EditorBuildKit.CreateBox("Facade_Wall",
                    new Vector3(side * (halfDoor + wingWidth), height / 2f, faceZ),
                    new Vector3(wingWidth * 2f, height, thickness));
                EditorBuildKit.SetMaterial(slab, concrete);
                slab.transform.SetParent(parent);
            }

            // Lintel over the entrance — leaves the doorway itself clear.
            var lintel = EditorBuildKit.CreateBox("Facade_Lintel",
                new Vector3(0f, (height + 3.2f) / 2f, faceZ),
                new Vector3(halfDoor * 2f, height - 3.2f, thickness));
            EditorBuildKit.SetMaterial(lintel, concrete);
            lintel.transform.SetParent(parent);

            var rng = new System.Random(80511);
            var details = new GameObject("Facade_Details").transform;
            details.SetParent(parent);

            // Window grid. Sits just proud of the wall face to avoid z-fighting.
            const float windowZ = faceZ - thickness / 2f - 0.03f;
            for (float y = 3.6f; y <= height - 2f; y += 3f)
            {
                for (float x = -edge + 1.4f; x <= edge - 1.4f; x += 2.45f)
                {
                    if (Mathf.Abs(x) < halfDoor + 0.9f) continue;   // keep the entrance clear
                    if (rng.NextDouble() < 0.12) continue;          // gaps read as boarded up
                    var window = EditorBuildKit.CreateBox("Window",
                        new Vector3(x, y, windowZ), new Vector3(1.25f, 1.7f, 0.06f));
                    EditorBuildKit.SetMaterial(window, rng.NextDouble() < 0.06 ? litGlass : glass);
                    window.transform.SetParent(details);
                }
            }

            // Cracks: thin dark slivers raked across the facade at shallow angles.
            for (int i = 0; i < 14; i++)
            {
                float x = -edge + (float)rng.NextDouble() * edge * 2f;
                float y = 1.5f + (float)rng.NextDouble() * (height - 3f);
                var crack = EditorBuildKit.CreateBox("Crack",
                    new Vector3(x, y, windowZ), new Vector3(0.11f, 1.8f + (float)rng.NextDouble() * 3.5f, 0.05f));
                crack.transform.Rotate(0f, 0f, -35f + (float)rng.NextDouble() * 70f);
                EditorBuildKit.SetMaterial(crack, stain);
                crack.transform.SetParent(details);
            }

            // Blown-out patches where the wall has come away.
            for (int i = 0; i < 6; i++)
            {
                float x = -edge + 2f + (float)rng.NextDouble() * (edge * 2f - 4f);
                if (Mathf.Abs(x) < halfDoor + 1.5f) continue;
                float y = 2.5f + (float)rng.NextDouble() * (height - 5f);
                var hole = EditorBuildKit.CreateBox("Break",
                    new Vector3(x, y, windowZ),
                    new Vector3(1.2f + (float)rng.NextDouble() * 2.2f, 0.9f + (float)rng.NextDouble() * 1.8f, 0.05f));
                EditorBuildKit.SetMaterial(hole, glass);
                hole.transform.SetParent(details);
            }

            // Ragged roofline — uneven parapet stubs instead of a clean edge, so the silhouette
            // against the fog reads as a ruin rather than a box.
            for (float x = -edge; x < edge; x += 2.2f)
            {
                if (rng.NextDouble() < 0.25) continue;
                float stub = 0.4f + (float)rng.NextDouble() * 2.4f;
                var cap = EditorBuildKit.CreateBox("Parapet",
                    new Vector3(x + 1.1f, height + stub / 2f, faceZ),
                    new Vector3(2.2f, stub, thickness));
                EditorBuildKit.SetMaterial(cap, concrete);
                cap.transform.SetParent(details);
            }

            // Only the wall slabs should be solid; the surface detail would otherwise add hundreds
            // of pointless colliders for the NavMesh bake to chew through.
            foreach (var collider in details.GetComponentsInChildren<Collider>())
                UnityObject.DestroyImmediate(collider);
        }

        /// <summary>
        /// Randomises where the key and crowbar appear each run, among points along the trail
        /// between the car and the doors. Fixed positions meant a second playthrough skipped the
        /// search entirely; keeping every point in the Exterior keeps them reachable before the
        /// fuse door, which is what stops a run becoming unwinnable.
        /// </summary>
        private static void ShuffleToolSpawns(GameObject key, GameObject crowbar)
        {
            if (key == null || crowbar == null) return;

            var go = new GameObject("ClueSpawnShuffler_Tools");
            go.AddComponent<Unity.Netcode.NetworkObject>();
            var shuffler = go.AddComponent<LastWard.Puzzles.ClueSpawnShuffler>();
            EditorBuildKit.SetRefArray(shuffler, "clues",
                new UnityEngine.Object[] { key.transform, crowbar.transform });

            // Offset sideways off the trail centre so items sit at its edge — findable, but not
            // dropped in the player's path.
            var points = new System.Collections.Generic.List<UnityEngine.Object>();
            (float z, float side)[] spots =
            {
                (-27f, 1f), (-24f, -1f), (-20f, 1f), (-17f, -1f),
                (-12f, 1f), (-9f, -1f), (-5f, 1f), (-3f, -1f),
            };
            foreach (var (z, side) in spots)
                points.Add(EditorBuildKit.MakePoint(
                    new Vector3(TrailCentreX(z) + side * (TrailHalfWidth - 0.5f), 0.2f, z)));
            EditorBuildKit.SetRefArray(shuffler, "candidatePoints", points.ToArray());
        }

        // Fuses are stashed inside containers rather than left lying on the floor, and which
        // container holds them changes between runs — so the opening has to be searched, not
        // remembered. The candidate points are the containers' interiors, so a "shuffled" fuse is
        // still always somewhere that has to be opened, crowbarred or crawled under to reach.
        private static void ShuffleFuseSpawns(GameObject[] fusePickups)
        {
            if (fusePickups == null || fusePickups.Length == 0) return;

            var stashes = CreateSearchableStashes();

            var shufflerGO = new GameObject("ClueSpawnShuffler_Fuses");
            shufflerGO.AddComponent<Unity.Netcode.NetworkObject>();
            var shuffler = shufflerGO.AddComponent<LastWard.Puzzles.ClueSpawnShuffler>();

            var clues = new UnityEngine.Object[fusePickups.Length];
            for (int i = 0; i < fusePickups.Length; i++) clues[i] = fusePickups[i].transform;
            EditorBuildKit.SetRefArray(shuffler, "clues", clues);
            EditorBuildKit.SetRefArray(shuffler, "candidatePoints", stashes);

            // One fuse is pinned to an always-open spot rather than shuffled. Both fuses are needed
            // to leave the Lobby, and the shuffle can otherwise put both behind the key and the
            // crowbar at once — if a player then misses either tool, stage one is unwinnable and
            // the run is dead with no way to tell why. The other fuse still moves every run, so
            // searching still matters.
            if (fusePickups.Length > 0 && fusePickups[0] != null)
            {
                var pinned = EditorBuildKit.MakePoint(new Vector3(-1.9f, 0.25f, 2.2f));
                pinned.name = "FuseGuaranteedPoint";
                var remaining = new System.Collections.Generic.List<UnityEngine.Object>();
                for (int i = 1; i < fusePickups.Length; i++) remaining.Add(fusePickups[i].transform);

                if (remaining.Count > 0)
                {
                    var second = new GameObject("ClueSpawnShuffler_FuseB");
                    second.AddComponent<Unity.Netcode.NetworkObject>();
                    var secondShuffler = second.AddComponent<LastWard.Puzzles.ClueSpawnShuffler>();
                    EditorBuildKit.SetRefArray(secondShuffler, "clues", remaining.ToArray());
                    EditorBuildKit.SetRefArray(secondShuffler, "candidatePoints", stashes);
                    // The first shuffler now only owns the pinned fuse.
                    EditorBuildKit.SetRefArray(shuffler, "clues", new UnityEngine.Object[] { fusePickups[0].transform });
                    EditorBuildKit.SetRefArray(shuffler, "candidatePoints", new UnityEngine.Object[] { pinned });
                }
            }
        }

        /// <summary>
        /// The searchable furniture of the Lobby: an unlocked cupboard, a locker that needs the key
        /// from the yard, a crate that needs the crowbar, and the gap under a bed. Returns the
        /// interior points, which double as the fuse spawn pool.
        /// </summary>
        private static UnityEngine.Object[] CreateSearchableStashes()
        {
            var points = new System.Collections.Generic.List<UnityEngine.Object>();

            // Lobby furniture is laid out as one wall-hugging run per side, spaced ~2.5m apart, so
            // containers and the art pass's furniture can't land on each other. Everything in the
            // Lobby shares this budget — see DressLobby, which fills the gaps between these.
            //   West wall (x -4.5): 1.4 closet · 4.6 wardrobe · 7.4 cupboard
            //   East wall (x  4.5): 1.8 sofa   · 4.6 table    · 7.2 locker · 9.3 wardrobe

            // Opens freely — the one that teaches the player containers are worth checking.
            points.Add(EditorBuildKit.CreateStorageContainer("Cupboard_Lobby",
                new Vector3(-4.5f, 0f, 7.4f), 90f, new Vector3(0.95f, 1.25f, 0.55f),
                requiredItemId: "", consumesItem: false, openPrompt: "Open the cupboard"));

            // Needs the key from the Exterior.
            points.Add(EditorBuildKit.CreateStorageContainer("Locker_Lobby",
                new Vector3(4.5f, 0f, 7.2f), -90f, new Vector3(0.85f, 1.75f, 0.55f),
                requiredItemId: "key", consumesItem: true, openPrompt: "Unlock the locker"));

            // Nailed shut — needs the crowbar. Sits off the north wall, clear of the order note at
            // (-3, ·, 8) and the Ward doorway.
            points.Add(EditorBuildKit.CreateStorageContainer("Crate_Lobby",
                new Vector3(-3.6f, 0f, 9.4f), 180f, new Vector3(0.9f, 0.7f, 0.7f),
                requiredItemId: "crowbar", consumesItem: false, openPrompt: "Lever the crate open"));

            // Searchable furniture in the Exterior, but deliberately NOT added to the fuse pool: a
            // fuse landing out here reads as "a fuse never spawned in the Lobby", since the player is
            // looking indoors. Both fuses must be findable inside. (It stays a red-herring cabinet.)
            EditorBuildKit.CreateStorageContainer("Cupboard_Exterior",
                new Vector3(-7.5f, 0f, -6.5f), 25f, new Vector3(0.95f, 1.15f, 0.55f),
                requiredItemId: "", consumesItem: false, openPrompt: "Open the cabinet");

            // Under the Lobby bed — no door, just out of sight unless you crouch and look.
            EditorBuildKit.CreateUnderBedHidingSpot("Hide_Bed_Lobby",
                new Vector3(2.5f, 0f, 2.8f), "Hide under the bed", 90f);
            points.Add(EditorBuildKit.MakePoint(new Vector3(2.5f, 0.12f, 2.8f)));

            // Both tools live in the Exterior, deliberately. The fuse shuffle can drop both fuses
            // into locked containers, so any tool placed past the fuse door would be locked behind
            // the very puzzle it's needed to solve — an unwinnable run. Everything required to open
            // the Lobby's stashes is reachable before the player ever enters the building.
            // The kit has a real key mesh; it has no crowbar, so that one stays a shaped block.
            // Both tools sit near the building, just off the trail, and move between runs. They
            // must stay reachable BEFORE the Lobby: the fuse shuffle can put both fuses behind the
            // key and the crowbar at once, so a tool locked past the fuse door is unwinnable.
            var key = EditorBuildKit.CreateToolPickup("key", "rusted key", Vector3.zero,
                new Color(0.85f, 0.72f, 0.25f), meshNamePrefix: "Key", texFile: "KeyBake-V2.0_1024.png");
            var crowbar = EditorBuildKit.CreateToolPickup("crowbar", "crowbar", Vector3.zero,
                new Color(0.75f, 0.3f, 0.2f),
                standaloneModel: "Props/Crowbar/scene.gltf", standaloneTextures: "Props/Crowbar/textures");
            ShuffleToolSpawns(key, crowbar);

            // Weapons for the whole level: two pipes and a knife, all deep enough in that you've met
            // the Entity before you find one, and all worth exactly one swing — see
            // PlayerMeleeDefense. Kept scarce on purpose; the moment they're plentiful the Entity
            // becomes something you manage rather than something you avoid.
            CreatePipePickup(new Vector3(-3.7f, 0.15f, 22.8f));
            CreatePipePickup(new Vector3(3.3f, 0.15f, 42.2f));
            EditorBuildKit.CreateToolPickup("knife", "kitchen knife", new Vector3(2.2f, 0.12f, 14.4f),
                new Color(0.8f, 0.8f, 0.85f), meshNamePrefix: "Knife_", texFile: "Knife-V2.0_1024.png");

            return points.ToArray();
        }

        // Lobby spans x:[-5,5], z:[0,10]. South wall (z=0) has a 2m doorway gap to the Exterior;
        // north wall (z=10) has a matching gap filled by Door_ToWard (the fuse puzzle's gate).
        private static void CreateLobbyZone()
        {
            const float wallHeight = 3f;
            const float t = 0.3f;
            Tile(EditorBuildKit.CreateBox("Lobby_Floor", new Vector3(0f, -0.1f, 5f), new Vector3(10f, 0.2f, 10f)), "Textures/Floor_Tile.png", 2f);
            Tile(EditorBuildKit.CreateBox("Lobby_Ceiling", new Vector3(0f, wallHeight, 5f), new Vector3(10f, 0.2f, 10f)), "Textures/Floor_Stone.png", 3f);
            Tile(EditorBuildKit.CreateBox("Lobby_Wall_E", new Vector3(5f, wallHeight / 2f, 5f), new Vector3(t, wallHeight, 10f + t)), "Textures/Wall_Tile.png", 2f);
            Tile(EditorBuildKit.CreateBox("Lobby_Wall_W", new Vector3(-5f, wallHeight / 2f, 5f), new Vector3(t, wallHeight, 10f + t)), "Textures/Wall_Tile.png", 2f);
            // South and north walls in two segments each, leaving x:[-1,1] open as doorways.
            Tile(EditorBuildKit.CreateBox("Lobby_Wall_S_Left", new Vector3(-3f, wallHeight / 2f, 0f), new Vector3(4f, wallHeight, t)), "Textures/Wall_Tile.png", 2f);
            Tile(EditorBuildKit.CreateBox("Lobby_Wall_S_Right", new Vector3(3f, wallHeight / 2f, 0f), new Vector3(4f, wallHeight, t)), "Textures/Wall_Tile.png", 2f);
            Tile(EditorBuildKit.CreateBox("Lobby_Wall_N_Left", new Vector3(-3f, wallHeight / 2f, 10f), new Vector3(4f, wallHeight, t)), "Textures/Wall_Tile.png", 2f);
            Tile(EditorBuildKit.CreateBox("Lobby_Wall_N_Right", new Vector3(3f, wallHeight / 2f, 10f), new Vector3(4f, wallHeight, t)), "Textures/Wall_Tile.png", 2f);

            var fill = new GameObject("Lobby_FillLight").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 10f;
            fill.intensity = 0.045f;
            fill.transform.position = new Vector3(0f, wallHeight - 0.3f, 5f);
        }

        // Ward spans x:[-5,5], z:[10,20] — same footprint as Lobby, connected through Door_ToWard.
        // North wall (z=20) has a 3m gap into the Corridor (no door — the Ward is meant to feel
        // exposed once you're through it, unlike the puzzle-gated approach into and out of it).
        private static void CreateWardZone()
        {
            const float wallHeight = 3f;
            const float t = 0.3f;
            Tile(EditorBuildKit.CreateBox("Ward_Floor", new Vector3(0f, -0.1f, 15f), new Vector3(10f, 0.2f, 10f)), "Textures/Floor_Stone.png", 2f);
            Tile(EditorBuildKit.CreateBox("Ward_Ceiling", new Vector3(0f, wallHeight, 15f), new Vector3(10f, 0.2f, 10f)), "Textures/Floor_Stone.png", 3f);
            // East wall in two segments, leaving a 2m doorway at z:[17,19] for the records nook.
            // It used to be solid, with the nook door floating a metre away inside the room filling
            // nothing and opening onto a wall.
            Tile(EditorBuildKit.CreateBox("Ward_Wall_E_S", new Vector3(5f, wallHeight / 2f, 13.5f), new Vector3(t, wallHeight, 7f + t)), "Textures/Wall_Stone.png", 2f);
            Tile(EditorBuildKit.CreateBox("Ward_Wall_E_N", new Vector3(5f, wallHeight / 2f, 19.5f), new Vector3(t, wallHeight, 1f)), "Textures/Wall_Stone.png", 2f);
            Tile(EditorBuildKit.CreateBox("Ward_Wall_W", new Vector3(-5f, wallHeight / 2f, 15f), new Vector3(t, wallHeight, 10f + t)), "Textures/Wall_Stone.png", 2f);
            // North wall in two segments, leaving a standard 2m doorway at x:[-1,1] into the
            // Corridor — filled by Door_ToCorridor. It used to be a 3m hole with nothing in it,
            // which is why the way onward read as "wide open" while the only door in the Ward sat
            // off on a side wall.
            // Centres are +-3 with width 4, so the segments span x:[-5,-1] and x:[1,5] and the gap is
            // exactly the door's 2m. Centring them at +-3.5 (as they briefly were) spans [-5.5,-1.5]
            // and [1.5,5.5] — a 3m hole with a 2m door in it, leaving 0.5m open either side.
            Tile(EditorBuildKit.CreateBox("Ward_Wall_N_Left", new Vector3(-3f, wallHeight / 2f, 20f), new Vector3(4f, wallHeight, t)), "Textures/Wall_Stone.png", 2f);
            Tile(EditorBuildKit.CreateBox("Ward_Wall_N_Right", new Vector3(3f, wallHeight / 2f, 20f), new Vector3(4f, wallHeight, t)), "Textures/Wall_Stone.png", 2f);

            var fill = new GameObject("Ward_FillLight").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 10f;
            fill.intensity = 0.03f; // dimmer than Lobby — "more aggressive Entity behavior" per the plan
            fill.transform.position = new Vector3(0f, wallHeight - 0.3f, 15f);
        }

        // Corridor spans x:[-4,4], z:[20,44] — a single bounding box containing a center pillar
        // (z:24-38) that splits it into two parallel paths, forming a real loop for chase
        // counterplay, plus a dead-end alcove branching west at z:29-32 ("ambush potential" per the
        // plan). Deliberately dimmer/sparser lighting than the rooms either side of it.
        private static void CreateCorridorZone()
        {
            const float wallHeight = 3f;
            const float t = 0.3f;

            // Textured rather than flat-shaded. Walls/floor/ceiling repeat their texture at a real
            // world size (see ArtKit.ApplyTiledMaterial), which is most of the difference between
            // "greybox" and "a place".
            const string wallTex = "Textures/H_Wall_Corridor.png";
            const string wallAlt = "Textures/H_Metal.png";
            const string floorTex = "Textures/H_Floor_Corridor.png";
            const string ceilTex = "Textures/H_Stone.png";

            Tile(EditorBuildKit.CreateBox("Corridor_Floor", new Vector3(0f, -0.1f, 32f), new Vector3(8f, 0.2f, 24f)), floorTex, 2f);
            Tile(EditorBuildKit.CreateBox("Corridor_Ceiling", new Vector3(0f, wallHeight, 32f), new Vector3(8f, 0.2f, 24f)), ceilTex, 3f);
            // East wall in three segments, leaving 2.2m doorways at z=24 and z=34 for the side
            // rooms. These gaps MUST match BuildSideRoom's doorway centres or the rooms seal shut.
            Tile(EditorBuildKit.CreateBox("Corridor_Wall_E_A", new Vector3(4f, wallHeight / 2f, 21.45f), new Vector3(t, wallHeight, 2.9f)), wallTex, 2f);
            Tile(EditorBuildKit.CreateBox("Corridor_Wall_E_B", new Vector3(4f, wallHeight / 2f, 29f), new Vector3(t, wallHeight, 7.8f)), wallTex, 2f);
            Tile(EditorBuildKit.CreateBox("Corridor_Wall_E_C", new Vector3(4f, wallHeight / 2f, 39.6f), new Vector3(t, wallHeight, 9f)), wallTex, 2f);
            // West wall in three segments now: the alcove gap at z:[29,32] plus two doorways into
            // the side rooms.
            Tile(EditorBuildKit.CreateBox("Corridor_Wall_W_S", new Vector3(-4f, wallHeight / 2f, 24.5f), new Vector3(t, wallHeight, 9f)), wallTex, 2f);
            // North-west wall in two segments, leaving a doorway at z=41 into the records room.
            Tile(EditorBuildKit.CreateBox("Corridor_Wall_W_N_A", new Vector3(-4f, wallHeight / 2f, 35.95f), new Vector3(t, wallHeight, 7.9f)), wallTex, 2f);
            Tile(EditorBuildKit.CreateBox("Corridor_Wall_W_N_B", new Vector3(-4f, wallHeight / 2f, 43.1f), new Vector3(t, wallHeight, 2.1f)), wallTex, 2f);

            // The loop-creating pillar: leaves ~3.25m paths on either side.
            Tile(EditorBuildKit.CreateBox("Corridor_Pillar", new Vector3(0f, wallHeight / 2f, 31f), new Vector3(1.5f, wallHeight, 14f)), wallAlt, 2f);

            // Dead-end alcove off the west path.
            Tile(EditorBuildKit.CreateBox("Alcove_Floor", new Vector3(-5.25f, -0.1f, 30.5f), new Vector3(2.5f, 0.2f, 3f)), floorTex, 2f);
            Tile(EditorBuildKit.CreateBox("Alcove_Ceiling", new Vector3(-5.25f, wallHeight, 30.5f), new Vector3(2.5f, 0.2f, 3f)), ceilTex, 3f);
            Tile(EditorBuildKit.CreateBox("Alcove_Wall_Far", new Vector3(-6.5f, wallHeight / 2f, 30.5f), new Vector3(t, wallHeight, 3f + t)), wallAlt, 2f);
            Tile(EditorBuildKit.CreateBox("Alcove_Wall_S", new Vector3(-5.25f, wallHeight / 2f, 29f), new Vector3(2.5f, wallHeight, t)), wallAlt, 2f);
            Tile(EditorBuildKit.CreateBox("Alcove_Wall_N", new Vector3(-5.25f, wallHeight / 2f, 32f), new Vector3(2.5f, wallHeight, t)), wallAlt, 2f);

            // Side rooms branching EAST off the corridor. Each is a distinct texture set so the run
            // doesn't read as one repeated box, and each is a genuine dead end — somewhere to be
            // caught, and somewhere worth searching.
            // Centred far enough out that the room's door wall clears the corridor wall at x=4.
            // At x=7.5 with a 7m width the two were exactly coplanar and z-fought.
            BuildSideRoom("Room_Storage", new Vector3(9.5f, 0f, 24f), new Vector2(7f, 6f),
                "Textures/H_Brick.png", "Textures/H_Floor_Room.png", new Color(1f, 0.8f, 0.55f));
            BuildSideRoom("Room_Washroom", new Vector3(9.5f, 0f, 34f), new Vector2(7f, 6f),
                "Textures/Wall_Tile.png", "Textures/Floor_Tile.png", new Color(0.7f, 0.95f, 1f));
            BuildSideRoom("Room_Records", new Vector3(-9.5f, 0f, 41f), new Vector2(6f, 6f),
                "Textures/H_Wall_Room.png", "Textures/H_Stone.png", new Color(1f, 0.55f, 0.3f));

            // Failing tubes down the length of the corridor. Alternating sodium-yellow and a sick
            // red so the run reads as two different failures rather than one repeated prop — and
            // the red ones sit at the far end, where the player is heading.
            float[] tubeZ = { 22.5f, 27f, 31.5f, 36f, 40.5f, 43f };
            for (int i = 0; i < tubeZ.Length; i++)
            {
                bool red = i >= tubeZ.Length - 2;
                // The red ones are cranked and given a far longer reach: the end of the corridor
                // should visibly bleed red across the walls and floor, not just show a red bulb.
                // Kept saturated rather than washing toward white.
                AddFlickeringTube(new Vector3(0f, wallHeight - 0.25f, tubeZ[i]),
                    red ? new Color(1f, 0.10f, 0.06f) : new Color(1f, 0.86f, 0.55f),
                    red ? 2.2f : 0.62f,
                    red ? 17f : 9f);
            }
            AddFlickeringTube(new Vector3(-5.25f, wallHeight - 0.25f, 30.5f),
                new Color(1f, 0.12f, 0.07f), 1.8f, 15f);
        }

        /// <summary>
        /// A flight of steps. Built from boxes rather than an imported mesh for the same reason the
        /// rest of the level is: the tread height has to line up exactly with what the CharacterController
        /// can step over, and an art asset gives you whatever the artist happened to model.
        ///
        /// <paramref name="rise"/> is per-step and may be negative to descend. Steps march along +Z
        /// from <paramref name="start"/>, which is the FRONT EDGE of the first tread at floor level.
        /// </summary>
        private static void BuildStaircase(string name, Vector3 start, int steps, float rise,
            float run, float width, string stepTex, string wallTex, float zDir = 1f, bool sideWalls = true)
        {
            var root = new GameObject(name).transform;
            const float h = 3f;
            const float t = 0.3f;

            for (int i = 0; i < steps; i++)
            {
                float y = start.y + rise * (i + 1);
                float z = start.z + zDir * run * (i + 0.5f);
                // Each tread is a solid block down to the previous step, so there is no gap to fall
                // through and the collider is one clean box per step.
                float slabH = Mathf.Abs(rise) + 0.05f;
                var step = EditorBuildKit.CreateBox($"{name}_Step{i:00}",
                    new Vector3(start.x, y - slabH / 2f, z),
                    new Vector3(width, slabH, run));
                Tile(step, stepTex, 1.5f);
                step.transform.SetParent(root, true);
            }

            // Side walls so the flight reads as a stairwell and nobody walks off the edge.
            if (!sideWalls) return;
            float midZ = start.z + zDir * run * steps / 2f;
            float midY = start.y + rise * steps / 2f;
            float span = Mathf.Abs(rise) * steps + h;
            foreach (float sx in new[] { -1f, 1f })
            {
                var w = EditorBuildKit.CreateBox($"{name}_Wall{(sx < 0 ? "W" : "E")}",
                    new Vector3(start.x + sx * (width / 2f + t / 2f), midY, midZ),
                    new Vector3(t, span, run * steps + t));
                Tile(w, wallTex, 2f);
                w.transform.SetParent(root, true);
            }
        }

        /// <summary>
        /// Increment 1 of the Manager (MANAGER_ENTITY_PLAN.md): places it on the first floor as a
        /// standing silhouette with glowing eyes. It is server-authoritative (plain NetworkTransform)
        /// and holds a ManagerController that does nothing yet but exist — perch/roam/manifest land in
        /// later increments. Positioned at the far end of the first-floor corridor, facing back down
        /// it, so the first thing you see coming up the stairs is two eyes at the end of the hall.
        /// </summary>
        private static void CreateManager()
        {
            Vector3 spawn = new Vector3(0f, FFy, 85f);   // north end of the FF corridor
            var root = new GameObject("Manager");
            root.transform.position = spawn;
            root.transform.rotation = Quaternion.Euler(0f, 180f, 0f);   // face south, toward arriving players
            root.AddComponent<Unity.Netcode.NetworkObject>();
            root.AddComponent<Unity.Netcode.Components.NetworkTransform>();
            root.AddComponent<LastWard.Entity.ManagerController>();

            var model = ArtKit.LoadModel("Characters/Manager/Rig_Pugalo.fbx");
            if (model == null)
            {
                Debug.LogWarning("[Build] Rig_Pugalo.fbx not found — Manager placed without a body.");
                return;
            }

            var visual = ArtKit.Spawn(model, root.transform, "Visual");
            ArtKit.NeutralizeParentScale(visual);
            ArtKit.FitHeight(visual, 2.05f);                            // a touch taller than a person
            ArtKit.GroundAt(visual, spawn);
            // Cold, jaundiced eyes — distinct from the Receptionist's red glow.
            ArtKit.MakeSilhouetteWithEyes(visual, new Color(0.9f, 0.86f, 0.55f), 0.06f);

            // Idle + one-shot Retreat, retargeted from the separate animation FBXs onto the rig.
            ArtKit.SetupManagerAnimator(visual, "Characters/Manager/Rig_Pugalo.fbx",
                "Characters/Manager/Animations/Animation of breathing.fbx",
                "Characters/Manager/Animations/Crawling_Backwards.fbx", "AC_Manager");
            // The permanent stutter is the Manager's alone. ManagerController flips it on at spawn.
            visual.AddComponent<LastWard.Entity.EntityAnimationDriver>();

            Debug.Log("[Build] Manager placed on the first floor (silhouette + eyes, animated).");
        }

        /// <summary>
        /// Puts a real padlock on each of the three exit-door locks. The stations are built during
        /// the level pass as plain boxes (they carry the puzzle logic); this replaces that box with
        /// the horror kit's Lock mesh, so what the player sees is three padlocks in a row barring the
        /// way out rather than three glowing yellow blocks floating in the room.
        /// </summary>
        private static void DressCorridorLocks(Transform scratch)
        {
            var parent = new GameObject("CorridorLocks_Dressing").transform;
            foreach (var st in UnityObject.FindObjectsByType<LastWard.Puzzles.IntercomStation>(FindObjectsInactive.Include))
            {
                // Hide the placeholder box but keep the object: it owns the collider the player
                // interacts with, and the puzzle's colour feedback drives whatever renderer it finds.
                if (st.TryGetComponent<Renderer>(out var box)) box.enabled = false;

                var padlock = PlaceHorrorKitItem(parent, scratch, $"Lock_{st.name}", 0.26f,
                    "LockBake-V2.0_1024.png", "M_DoorLock", "Lock_");
                if (padlock == null) continue;
                padlock.transform.SetParent(st.transform, false);
                padlock.transform.localPosition = Vector3.zero;
                padlock.transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>
        /// The three keys and the notes that teach their order. Keys go in the three corridor rooms,
        /// so the puzzle is a reason to search every one of them while the Entity is at its most
        /// sensitive — and the notes are worth knowledge, which is itself what draws it to you.
        /// </summary>
        private static void WireCorridorLocks()
        {
            // Each lock wants its own key. Indices match the station order above: 0 top, 1 middle,
            // 2 bottom. The puzzle's correctOrder is {2,0,1} — bottom, top, middle — which is
            // exactly the habit the notes describe.
            string[] keyIds = { "lock_key_top", "lock_key_middle", "lock_key_bottom" };
            var stations = UnityObject.FindObjectsByType<LastWard.Puzzles.IntercomStation>(FindObjectsInactive.Include);
            foreach (var st in stations)
            {
                var so = new SerializedObject(st);
                int idx = so.FindProperty("stationIndex").intValue;
                if (idx >= 0 && idx < keyIds.Length)
                    EditorBuildKit.SetString(st, "requiredItemId", keyIds[idx]);
            }

            // The keys themselves, one per corridor room — Storage, Washroom, Records.
            EditorBuildKit.CreateToolPickup(keyIds[0], "small brass key", new Vector3(9.5f, 0.9f, 22.6f),
                new Color(0.85f, 0.72f, 0.25f), meshNamePrefix: "Key", texFile: "KeyBake-V2.0_1024.png");
            EditorBuildKit.CreateToolPickup(keyIds[1], "bent steel key", new Vector3(10.6f, 0.9f, 35.2f),
                new Color(0.75f, 0.75f, 0.78f), meshNamePrefix: "Key", texFile: "KeyBake-V2.0_1024.png");
            EditorBuildKit.CreateToolPickup(keyIds[2], "long iron key", new Vector3(-10.4f, 0.9f, 42.2f),
                new Color(0.55f, 0.45f, 0.35f), meshNamePrefix: "Key", texFile: "KeyBake-V2.0_1024.png");

            // The order, told as habit rather than instruction. No note states it outright; between
            // them they leave only one possible sequence: bottom, top, middle.
            EditorBuildKit.CreateNoteProp("Note_NurseRota", new Vector3(8.4f, 0f, 25.4f),
                "Assets/_Project/Data/lock_rota.asset", "lock_rota", "Night Rota - Back Page",
                "Everyone hated locking up but her. She had a way of doing it - started at the " +
                "bottom because she said the iron one stuck if you left it till last, then straight " +
                "up to the top, then finished on the middle. Twice a night, for eleven years. You " +
                "could set your watch by the sound of it.", 3f);

            EditorBuildKit.CreateNoteProp("Note_NurseComplaint", new Vector3(11f, 0f, 33f),
                "Assets/_Project/Data/lock_complaint.asset", "lock_complaint", "Maintenance Request",
                "The middle lock is worn through and she will not stop leaving it till the end. I " +
                "have told her the order does not matter. She says it does. She says the building " +
                "knows the difference and she is not going to be the one who teaches it otherwise.", 3f);

            EditorBuildKit.CreateNoteProp("Note_NurseLast", new Vector3(-9f, 0f, 43.4f),
                "Assets/_Project/Data/lock_last.asset", "lock_last", "Torn From A Diary",
                "I did them out of order. I was tired and I did the middle one first and I heard it " +
                "stop what it was doing. Three floors away and it stopped. It knows the sound of " +
                "that door being done right and it knows the sound of it being done wrong.", 4f);
        }

        // ================= FIRST FLOOR =================
        // Sits at y=3.2, the height the zig-zag climbs to. A spine corridor with five rooms off it,
        // each one a different job the building used to do. Textures come from the FF set so the
        // floor reads as somewhere else entirely, not more of the ground floor.
        private const float FFy = 3.2f;
        private const string FFWallA = "FirstFloor/Textures/Wall_01_256x256.png";
        private const string FFWallB = "FirstFloor/Textures/Wall_02_256x256.png";
        private const string FFFloorA = "FirstFloor/Textures/Floor_Tiles_01_256x256.png";
        private const string FFFloorB = "FirstFloor/Textures/Floor_Tiles_02_256x256.png";

        /// <summary>
        /// One first-floor room: floor, ceiling, three solid walls and a doorway wall facing the
        /// corridor, plus the short passage connecting the two. Same construction as the ground
        /// floor's side rooms, lifted to FFy and given its own texture pair.
        /// </summary>
        private static void BuildFFRoom(string name, Vector3 centre, Vector2 size,
            string wallTex, string floorTex, Color lightColor, float corridorWallX)
        {
            const float h = 3f;
            const float t = 0.3f;
            float hw = size.x / 2f, hd = size.y / 2f;
            bool opensWest = centre.x > 0f;
            float doorX = opensWest ? centre.x - hw : centre.x + hw;
            const float doorHalf = 1.1f;

            Tile(EditorBuildKit.CreateBox($"{name}_Floor", new Vector3(centre.x, FFy - 0.1f, centre.z), new Vector3(size.x, 0.2f, size.y)), floorTex, 2f);
            Tile(EditorBuildKit.CreateBox($"{name}_Ceiling", new Vector3(centre.x, FFy + h, centre.z), new Vector3(size.x, 0.2f, size.y)), floorTex, 3f);

            float farX = opensWest ? centre.x + hw : centre.x - hw;
            Tile(EditorBuildKit.CreateBox($"{name}_Wall_Far", new Vector3(farX, FFy + h / 2f, centre.z), new Vector3(t, h, size.y + t)), wallTex, 2f);
            Tile(EditorBuildKit.CreateBox($"{name}_Wall_S", new Vector3(centre.x, FFy + h / 2f, centre.z - hd), new Vector3(size.x, h, t)), wallTex, 2f);
            Tile(EditorBuildKit.CreateBox($"{name}_Wall_N", new Vector3(centre.x, FFy + h / 2f, centre.z + hd), new Vector3(size.x, h, t)), wallTex, 2f);

            float seg = (size.y - doorHalf * 2f) / 2f;
            float segOff = doorHalf + seg / 2f;
            Tile(EditorBuildKit.CreateBox($"{name}_Wall_Door_A", new Vector3(doorX, FFy + h / 2f, centre.z - segOff), new Vector3(t, h, seg)), wallTex, 2f);
            Tile(EditorBuildKit.CreateBox($"{name}_Wall_Door_B", new Vector3(doorX, FFy + h / 2f, centre.z + segOff), new Vector3(t, h, seg)), wallTex, 2f);

            float edge = opensWest ? corridorWallX : -corridorWallX;
            float passLen = Mathf.Abs(doorX - edge);
            if (passLen > 0.2f)
            {
                float px = (doorX + edge) / 2f;
                Tile(EditorBuildKit.CreateBox($"{name}_Pass_Floor", new Vector3(px, FFy - 0.1f, centre.z), new Vector3(passLen, 0.2f, doorHalf * 2f)), floorTex, 2f);
                Tile(EditorBuildKit.CreateBox($"{name}_Pass_Ceil", new Vector3(px, FFy + h, centre.z), new Vector3(passLen, 0.2f, doorHalf * 2f)), floorTex, 3f);
                Tile(EditorBuildKit.CreateBox($"{name}_Pass_S", new Vector3(px, FFy + h / 2f, centre.z - doorHalf), new Vector3(passLen, h, t)), wallTex, 2f);
                Tile(EditorBuildKit.CreateBox($"{name}_Pass_N", new Vector3(px, FFy + h / 2f, centre.z + doorHalf), new Vector3(passLen, h, t)), wallTex, 2f);
            }

            AddFlickeringTube(new Vector3(centre.x, FFy + h - 0.25f, centre.z), lightColor, 0.13f);
        }

        /// <summary>
        /// The first floor. Five rooms off a spine corridor, each one a job the building used to do,
        /// and between them the story of the man who ran it. The Receptionist downstairs was staff;
        /// the Manager decided who stayed. This floor is where you learn the difference.
        /// </summary>
        private static void CreateFirstFloor()
        {
            const float h = 3f;
            const float t = 0.3f;
            const float zS = 60f, zN = 87f;   // south end is where the staircase tops out
            float mid = (zS + zN) / 2f;

            // Spine corridor, x[-1.5,1.5], reached from the top of the zig-zag.
            Tile(EditorBuildKit.CreateBox("FF_Corridor_Floor", new Vector3(0f, FFy - 0.1f, mid), new Vector3(3f, 0.2f, zN - zS)), FFFloorA, 2f);
            Tile(EditorBuildKit.CreateBox("FF_Corridor_Ceiling", new Vector3(0f, FFy + h, mid), new Vector3(3f, 0.2f, zN - zS)), FFFloorA, 3f);
            // North end opens to the second-floor stairs, so the wall is left in two side pieces
            // with a doorway between them at x[-1,1].
            Tile(EditorBuildKit.CreateBox("FF_Corridor_N_L", new Vector3(-1.75f, FFy + h / 2f, zN), new Vector3(1.5f, h, t)), FFWallA, 2f);
            Tile(EditorBuildKit.CreateBox("FF_Corridor_N_R", new Vector3(1.75f, FFy + h / 2f, zN), new Vector3(1.5f, h, t)), FFWallA, 2f);

            // East and west walls, segmented to leave 2.2m doorways for each room.
            // East doorways at z=62 and z=72 and z=82; west at z=65 and z=75.
            float[] eastDoors = { 62f, 72f, 82f };
            float[] westDoors = { 65f, 75f };
            BuildFFCorridorWall("FF_Corridor_E", 1.5f, zS, zN, eastDoors, h, t, FFWallA);
            BuildFFCorridorWall("FF_Corridor_W", -1.5f, zS, zN, westDoors, h, t, FFWallB);

            // --- the five rooms ---
            BuildFFRoom("FF_Office",   new Vector3(5.5f, 0f, 62f), new Vector2(6f, 6f), FFWallB, FFFloorB, new Color(0.95f, 0.80f, 0.55f), 1.5f);
            BuildFFRoom("FF_WardTwo",  new Vector3(-5.5f, 0f, 65f), new Vector2(6f, 7f), FFWallA, FFFloorA, new Color(0.75f, 0.85f, 0.95f), 1.5f);
            BuildFFRoom("FF_Records",  new Vector3(5.5f, 0f, 72f), new Vector2(6f, 6f), FFWallA, FFFloorB, new Color(0.90f, 0.75f, 0.50f), 1.5f);
            BuildFFRoom("FF_Theatre",  new Vector3(-5.5f, 0f, 75f), new Vector2(6f, 7f), FFWallB, FFFloorA, new Color(0.95f, 0.95f, 0.90f), 1.5f);
            BuildFFRoom("FF_Staff",    new Vector3(5.5f, 0f, 82f), new Vector2(6f, 6f), FFWallB, FFFloorB, new Color(0.85f, 0.70f, 0.60f), 1.5f);

            DressFirstFloor();
            CreateManagerLore();
            CreateSecondFloorStairs();
        }

        /// <summary>
        /// Furniture for the five first-floor rooms. Every piece is drawn from the FF prop set and
        /// placed against a wall, so the middle of each room stays clear to be chased through. The
        /// choice of props IS the storytelling here: what a room contains is what it was for.
        /// </summary>
        private static void DressFirstFloor()
        {
            var parent = new GameObject("FF_Dressing").transform;
            const string PR = "FirstFloor/Props/";

            void Place(string model, string name, float height, Vector3 pos, float yaw)
            {
                var go = PlaceProp(parent, PR + model, null, null, name, height, pos, yaw);
                if (go != null) ArtKit.AutoTexture(go, "FirstFloor/Textures", alphaClip: false, pointFilter: false);
            }

            // 1. The Manager's office - his desk, his chair, his view of the corridor.
            Place("Furniture/DoctorsDesk/DoctorsDesk.fbx", "FF_Desk", 0.78f, new Vector3(7.2f, FFy, 63.4f), -90f);
            Place("Furniture/StorageRack/StorageRack.fbx", "FF_OfficeRack", 1.9f, new Vector3(5.5f, FFy, 64.6f), 180f);
            Place("Misc/Picture_01/Picture_01.fbx", "FF_OfficePicture", 0.6f, new Vector3(4.4f, FFy + 1.7f, 62f), 90f);

            // 2. Ward Two - the beds people were moved to when they "improved".
            Place("Furniture/Bed_01/Bed_01.fbx", "FF_Bed1", 0.6f, new Vector3(-7.3f, FFy, 63.2f), 90f);
            Place("Furniture/Bed_02/Bed_02.fbx", "FF_Bed2", 0.6f, new Vector3(-7.3f, FFy, 66.4f), 90f);
            Place("Furniture/Nightstand/Nightstand.fbx", "FF_Night1", 0.55f, new Vector3(-7.3f, FFy, 64.8f), 90f);
            Place("Misc/IV_Stand/IV_Stand.fbx", "FF_IV1", 1.6f, new Vector3(-4.6f, FFy, 63.6f), 0f);
            Place("Misc/IV_Stand/IV_Stand.fbx", "FF_IV2", 1.6f, new Vector3(-4.6f, FFy, 66.8f), 25f);

            // 3. Records - shelves of people, and the trolley someone left mid-job.
            Place("Furniture/StorageRack/StorageRack.fbx", "FF_Rack1", 1.9f, new Vector3(7.3f, FFy, 70.6f), -90f);
            Place("Furniture/StorageRack/StorageRack.fbx", "FF_Rack2", 1.9f, new Vector3(7.3f, FFy, 73.4f), -90f);
            Place("Misc/Bucket/Bucket.fbx", "FF_Bucket1", 0.35f, new Vector3(4.6f, FFy, 71.8f), 0f);

            // 4. Theatre - the screen, the chair, and a bucket nobody emptied.
            Place("Furniture/Screen/Screen.fbx", "FF_Screen", 1.8f, new Vector3(-4.6f, FFy, 76.6f), 110f);
            Place("Misc/WheelChair/WheelChair.fbx", "FF_Chair", 1.1f, new Vector3(-6.2f, FFy, 74.2f), 40f);
            Place("Misc/Bucket/Bucket.fbx", "FF_Bucket2", 0.35f, new Vector3(-7.4f, FFy, 76.8f), 0f);
            Place("Misc/IV_Stand/IV_Stand.fbx", "FF_IV3", 1.6f, new Vector3(-7.4f, FFy, 72.6f), 0f);

            // 5. Staff room - a bed nobody should have been sleeping in, and the vent.
            Place("Furniture/Bed_01/Bed_01_D.fbx", "FF_StaffBed", 0.6f, new Vector3(7.3f, FFy, 83.4f), -90f);
            Place("Furniture/Nightstand/Nightstand.fbx", "FF_Night2", 0.55f, new Vector3(7.3f, FFy, 81.4f), -90f);
            Place("Misc/AirVent/AirVent.fbx", "FF_Vent", 0.55f, new Vector3(4.4f, FFy + 2.2f, 82f), 90f);
            Place("Misc/Picture_01/Picture_01.fbx", "FF_StaffPicture", 0.6f, new Vector3(5.5f, FFy + 1.7f, 84.8f), 180f);
        }

        /// <summary>
        /// The Manager. The Receptionist downstairs was staff - he greeted people. The Manager
        /// decided which of them left again. These notes never describe him physically; they only
        /// ever record what he signed, which is the point.
        /// </summary>
        private static void CreateManagerLore()
        {
            EditorBuildKit.CreateNoteProp("FF_Note_Ledger", new Vector3(6.6f, FFy, 63.4f),
                "Assets/_Project/Data/ff_ledger.asset", "ff_ledger", "Occupancy Ledger - Ward Two",
                "Beds are counted at seven and again at seven. The count has not gone down in " +
                "eleven weeks and it has not gone up either. The Manager signs it every night and " +
                "every night it is the same number, and every night it is different people.", 3f);

            EditorBuildKit.CreateNoteProp("FF_Note_Transfer", new Vector3(-6.4f, FFy, 64.8f),
                "Assets/_Project/Data/ff_transfer.asset", "ff_transfer", "Transfer Slip",
                "Improved. Moved upstairs for observation. Signed - Mgr.\n\nThe same six words, in " +
                "the same hand, on every slip in this drawer. None of them say where upstairs is. " +
                "There is no floor above this one.", 3f);

            EditorBuildKit.CreateNoteProp("FF_Note_Theatre", new Vector3(-5.2f, FFy, 76.2f),
                "Assets/_Project/Data/ff_theatre.asset", "ff_theatre", "Procedure Log",
                "He does not scrub in and he does not wear gloves and nobody has ever asked him to. " +
                "He stands at the end of the table where the patient can see him, and he waits, and " +
                "when it is finished he writes IMPROVED and goes back upstairs.", 4f);

            EditorBuildKit.CreateNoteProp("FF_Note_Staff", new Vector3(6.6f, FFy, 81.4f),
                "Assets/_Project/Data/ff_staff.asset", "ff_staff", "Rota - Torn",
                "Whoever is on nights: the corridor lights on this floor are on the same circuit as " +
                "the ward. If yours start going, his are already out. Do not wait to find out how " +
                "far away he is. You will not hear him - he is not the one who limps.", 4f);

            EditorBuildKit.CreateNoteProp("FF_Note_Warning", new Vector3(0.9f, FFy, 86f),
                "Assets/_Project/Data/ff_warning.asset", "ff_warning", "Written On The Wall",
                "THE ONE DOWNSTAIRS CANNOT SEE. THIS ONE DOES NOTHING BUT. " +
                "DO NOT LET IT GET BETWEEN YOU AND THE STAIRS.", 4f);
        }

        /// <summary>
        /// A straight flight at the NORTH end of the first-floor hallway, climbing from the corridor
        /// (y=FFy=3.2) up to the second floor (y=6.4). Same proven pattern as the ground-to-first
        /// flight: 16 x 0.2m steps, topping out flush with a landing. The second floor itself is not
        /// built yet — for now the stairs deliver you to a small landing (a stub) so the way up is
        /// visibly there. Continues straight north out of the hallway rather than turning, which is
        /// the simplest geometry that cannot break.
        /// </summary>
        private static void CreateSecondFloorStairs()
        {
            const float t = 0.3f;
            const float h = 3f;
            const float y0 = FFy;                    // 3.2, first-floor level
            const float ceilY = FFy + 6f;            // 9.2, two storeys of shaft
            const int steps = 16;
            const float rise = 0.2f, run = 0.3f;
            const float botZ = 87.8f;                // just past the corridor's north wall (zN=87)
            const float topZ = botZ + run * steps;   // 92.6 — second-floor landing

            BuildStaircase("Stairs_Up2", new Vector3(0f, y0, botZ), steps, rise, run, 2.4f,
                FFFloorB, FFWallB, zDir: 1f, sideWalls: false);

            // Second-floor landing (stub): where the flight tops out, y = y0 + 3.2 = 6.4.
            Tile(EditorBuildKit.CreateBox("SecondFloor_Landing", new Vector3(0f, y0 + 3.2f - 0.1f, topZ + 1.2f),
                new Vector3(4f, 0.2f, 2.4f)), FFFloorB, 2f);

            // Shaft: safety floor at first-floor level, ceiling, side + north walls. Double-height so
            // the climb has headroom.
            float shaftMidZ = (botZ + topZ) / 2f;
            float shaftLen = topZ - botZ + 3.2f;     // covers landing too
            Tile(EditorBuildKit.CreateBox("Stairs2_Floor", new Vector3(0f, y0 - 0.1f, shaftMidZ + 0.8f), new Vector3(3.8f, 0.2f, shaftLen)), FFFloorB, 2f);
            Tile(EditorBuildKit.CreateBox("Stairs2_Ceil", new Vector3(0f, ceilY, shaftMidZ + 0.8f), new Vector3(4.0f, 0.2f, shaftLen)), FFFloorB, 3f);
            Tile(EditorBuildKit.CreateBox("Stairs2_Wall_W", new Vector3(-1.9f, y0 + (ceilY - y0) / 2f, shaftMidZ + 0.8f), new Vector3(t, ceilY - y0, shaftLen)), FFWallA, 2f);
            Tile(EditorBuildKit.CreateBox("Stairs2_Wall_E", new Vector3(1.9f, y0 + (ceilY - y0) / 2f, shaftMidZ + 0.8f), new Vector3(t, ceilY - y0, shaftLen)), FFWallA, 2f);
            Tile(EditorBuildKit.CreateBox("Stairs2_Wall_N", new Vector3(0f, y0 + (ceilY - y0) / 2f, topZ + 2.4f), new Vector3(4.0f, ceilY - y0, t)), FFWallA, 2f);

            AddDimLight(new Vector3(0f, ceilY - 0.6f, botZ + 2f), 0.14f);
            AddDimLight(new Vector3(0f, y0 + 2.6f, topZ + 1f), 0.12f);
        }

        /// <summary>A corridor wall broken by doorways at the given Z centres.</summary>
        private static void BuildFFCorridorWall(string name, float x, float zS, float zN,
            float[] doorZ, float h, float t, string tex)
        {
            const float doorHalf = 1.1f;
            var edges = new List<float> { zS };
            foreach (float dz in doorZ) { edges.Add(dz - doorHalf); edges.Add(dz + doorHalf); }
            edges.Add(zN);
            for (int i = 0; i < edges.Count; i += 2)
            {
                float a = edges[i], b = edges[i + 1];
                if (b - a < 0.05f) continue;
                Tile(EditorBuildKit.CreateBox($"{name}_{i}", new Vector3(x, FFy + h / 2f, (a + b) / 2f),
                    new Vector3(t, h, b - a)), tex, 2f);
            }
        }
        /// <summary>
        /// The notes that explain the ground floor. He was never actually a receptionist: he was a
        /// blind patient with nowhere to be sent, and the staff invented the front-desk job so he
        /// would feel useful. He took it completely seriously, and he has never stopped doing it.
        ///
        /// That is where the horror comes from, and why the corridor behaves as it does. It is not
        /// malice - he is still signing people in and out, and you are trying to leave his building
        /// without being counted. The blindness is the joke on top: you can stand in the dark with
        /// your torch off and he will still find you, because he was never looking in the first place.
        /// </summary>
        private static void CreateBlindLore()
        {
            EditorBuildKit.CreateNoteProp("Note_Reception", new Vector3(-3.4f, 0f, 4.2f),
                "Assets/_Project/Data/lore_reception.asset", "lore_reception", "Staff Notice - Front Desk",
                "Whoever keeps switching the desk lamp on at night: stop. He has asked twice now and " +
                "he is embarrassed to ask again. He does not need it and he does not like being " +
                "reminded of that. Let him work the way he works.", 2f);

            EditorBuildKit.CreateNoteProp("Note_Referral2", new Vector3(3.6f, 0f, 8.6f),
                "Assets/_Project/Data/lore_referral2.asset", "lore_referral2", "Ward Meeting - Minutes",
                "Bilateral, complete, permanent. He is not going home; there is nowhere to send him. " +
                "Agreed: give him the front desk -- signing the book, counting people in and out. It " +
                "costs us nothing and it will do him good to be needed.   And nobody tell him the " +
                "night book is copied out again by the day staff. Let him have it.", 4f);

            EditorBuildKit.CreateNoteProp("Note_Scrawl", new Vector3(4.1f, 0f, 16.4f),
                "Assets/_Project/Data/lore_scrawl.asset", "lore_scrawl", "Scratched Into The Paint",
                "THE TORCH DOES NOTHING. HE NEVER COULD SEE IT. HE HEARS YOU.   " +
                "AND HE IS STILL ON THE DESK. HE IS STILL COUNTING. WE GAVE HIM THAT JOB TO BE KIND " +
                "AND HE HAS NEVER PUT IT DOWN -- NOBODY GETS OUT OF HIS BUILDING UNSIGNED.", 4f);
        }

        /// <summary>
        /// The stair junction, built as its own landing BEYOND the Exit Route (z 54..60) rather than
        /// inside it. That is deliberate: a descending flight needs a hole in the floor it drops
        /// through, and the Exit Route floor is a single slab -- cutting it would mean rebuilding
        /// that whole zone. This landing owns its own floor and ceiling, so both openings are simply
        /// gaps left between segments, exactly how every doorway in this level is made.
        ///
        /// One flight climbs to the second floor, one drops to the basement. Only the stairs exist
        /// so far; both destinations (and the corridor Entity) come next session.
        /// </summary>
        /// <summary>
        /// A single straight flight climbing the Exit Route (y=0) up to the first-floor corridor
        /// (y=FFy=3.2) — 16 steps of 0.2m over 4.8m of run, topping out exactly at z=60, which is the
        /// corridor's south end. The earlier dog-leg attempt never connected: its flights and landings
        /// did not line up in height, so a few steps just met a wall. A straight run is trivially
        /// verifiable — every tread is 0.2m above the last, and the top tread is flush with the
        /// corridor floor — so it simply works.
        ///
        /// The flight sits in a double-height shaft entered from the Exit Route through Door_FinalExit
        /// (still gated by the intercom puzzle). The basement is deferred: it gets its own stairs when
        /// the basement itself is built.
        /// </summary>
        private static void CreateFloorStairs()
        {
            const float t = 0.3f;
            const float ceilY = FFy + 3f;               // 6.2 — the first floor's ceiling height
            const int steps = 16;                       // 16 x 0.2 = 3.2m == FFy, one storey
            const float rise = 0.2f, run = 0.3f;
            const float botZ = 55.2f;                   // front edge of the first tread
            const float topZ = botZ + run * steps;      // 60.0 — where it meets the corridor

            // The climb itself. Its own side walls are off — the shaft below encloses it full-height.
            BuildStaircase("Stairs_Up", new Vector3(0f, 0f, botZ), steps, rise, run, 2.4f,
                FFFloorA, FFWallA, zDir: 1f, sideWalls: false);

            // Shaft: a safety/entry floor at y=0 (door threshold to the first step and under the run),
            // a full-height ceiling, and side + upper-south walls. Double-height so the whole climb
            // has headroom and reads as a proper stairwell.
            // Derived from the flight itself (door threshold at z=54 up to where it meets the
            // corridor) rather than hardcoded, so the shaft can never drift out of step with the
            // steps it is meant to enclose.
            float shaftMid = (54f + topZ) / 2f;
            float shaftLen = topZ - 54f;
            Tile(EditorBuildKit.CreateBox("Stairs_ShaftFloor", new Vector3(0f, -0.1f, shaftMid), new Vector3(3.8f, 0.2f, shaftLen)), FFFloorA, 2f);
            Tile(EditorBuildKit.CreateBox("Stairs_ShaftCeil", new Vector3(0f, ceilY, shaftMid), new Vector3(4.0f, 0.2f, shaftLen)), FFFloorA, 3f);
            Tile(EditorBuildKit.CreateBox("Stairs_ShaftWall_W", new Vector3(-1.9f, ceilY / 2f, shaftMid), new Vector3(t, ceilY, shaftLen)), FFWallB, 2f);
            Tile(EditorBuildKit.CreateBox("Stairs_ShaftWall_E", new Vector3(1.9f, ceilY / 2f, shaftMid), new Vector3(t, ceilY, shaftLen)), FFWallB, 2f);
            // The Exit Route wall/door only reaches y=3; fill the shaft's south face above it.
            Tile(EditorBuildKit.CreateBox("Stairs_ShaftWall_S", new Vector3(0f, (3f + ceilY) / 2f, 54f), new Vector3(4.0f, ceilY - 3f, t)), FFWallB, 2f);

            // Signage, atmospheric — kept scratched-through so it never promises a route that is not
            // built yet.
            EditorBuildKit.CreateNoteProp("Note_StairSign", new Vector3(1.5f, 0f, 54.7f),
                "Assets/_Project/Data/StairSignClue.asset", "stair_sign", "Stairwell Sign",
                "UP -- WARD TWO, THEATRE, STAFF.   Below that, another line has been scratched " +
                "through the paint, over and over, until there was nothing left to read.",
                2f);

            AddDimLight(new Vector3(0f, ceilY - 0.6f, 56f), 0.13f);
            AddDimLight(new Vector3(0f, ceilY - 0.6f, 59f), 0.16f);
        }

        // Exit Route spans x:[-4,4], z:[44,54] (matching the Corridor's width, so they meet with no
        // gap). Door_FinalExit (z=54) is gated by the intercom puzzle; beyond it, a short "outside"
        // sliver holds the one-slot exit trigger.
        private static void CreateExitRouteZone()
        {
            const float wallHeight = 3f;
            const float t = 0.3f;
            Tile(EditorBuildKit.CreateBox("Exit_Floor", new Vector3(0f, -0.1f, 49f), new Vector3(8f, 0.2f, 10f)), "Textures/Floor_Wood.png", 2f);
            Tile(EditorBuildKit.CreateBox("Exit_Ceiling", new Vector3(0f, wallHeight, 49f), new Vector3(8f, 0.2f, 10f)), "Textures/Floor_Stone.png", 3f);
            Tile(EditorBuildKit.CreateBox("Exit_Wall_E", new Vector3(4f, wallHeight / 2f, 49f), new Vector3(t, wallHeight, 10f + t)), "Textures/Wall_Brick.png", 2f);
            Tile(EditorBuildKit.CreateBox("Exit_Wall_W", new Vector3(-4f, wallHeight / 2f, 49f), new Vector3(t, wallHeight, 10f + t)), "Textures/Wall_Brick.png", 2f);
            // North wall in two segments, leaving x:[-1.5,1.5] for Door_FinalExit.
            Tile(EditorBuildKit.CreateBox("Exit_Wall_N_Left", new Vector3(-2.75f, wallHeight / 2f, 54f), new Vector3(2.5f, wallHeight, t)), "Textures/Wall_Brick.png", 2f);
            Tile(EditorBuildKit.CreateBox("Exit_Wall_N_Right", new Vector3(2.75f, wallHeight / 2f, 54f), new Vector3(2.5f, wallHeight, t)), "Textures/Wall_Brick.png", 2f);

            // (Removed: the old "Outside" patch beyond Door_FinalExit — a floor and a full-width
            // wall at z=58 — dated from when that door led outdoors. The door now opens onto the
            // staircase, and that wall was sitting squarely across the fifth step.)

            AddDimLight(new Vector3(0f, wallHeight - 0.3f, 49f), 0.2f);
        }

        /// <summary>Textures a greybox box so its texture repeats at a real-world size.</summary>
        private static void Tile(GameObject box, string texRelPath, float metresPerTile, Color? tint = null)
            => ArtKit.ApplyTiledMaterial(box, texRelPath, metresPerTile, tint);

        /// <summary>
        /// A dead-end room off the Service Corridor. Built procedurally so the doorway lines up
        /// exactly with the corridor wall it cuts through — the thing that made imported corridor
        /// assets unusable was that no such guarantee was possible.
        ///
        /// Each room takes its own wall/floor textures and its own light colour, so three rooms
        /// built from the same code still read as three different places.
        /// </summary>
        /// <param name="hostWallX">X of the wall this room connects back to. Defaults to the
        /// Service Corridor's wall; the Ward's is further out, and a hardcoded value made the
        /// connecting passage stop short of the doorway.</param>
        private static void BuildSideRoom(string name, Vector3 centre, Vector2 size,
            string wallTex, string floorTex, Color lightColor, float hostWallX = 4f)
        {
            const float h = 3f;
            const float t = 0.3f;
            float hw = size.x / 2f, hd = size.y / 2f;

            // Which side the corridor is on: rooms east of the corridor open west, and vice versa.
            bool opensWest = centre.x > 0f;
            float doorX = opensWest ? centre.x - hw : centre.x + hw;
            const float doorHalf = 1.1f;

            Tile(EditorBuildKit.CreateBox($"{name}_Floor", new Vector3(centre.x, -0.1f, centre.z), new Vector3(size.x, 0.2f, size.y)), floorTex, 2f);
            Tile(EditorBuildKit.CreateBox($"{name}_Ceiling", new Vector3(centre.x, h, centre.z), new Vector3(size.x, 0.2f, size.y)), floorTex, 3f);

            // Far wall and the two side walls are solid.
            float farX = opensWest ? centre.x + hw : centre.x - hw;
            Tile(EditorBuildKit.CreateBox($"{name}_Wall_Far", new Vector3(farX, h / 2f, centre.z), new Vector3(t, h, size.y + t)), wallTex, 2f);
            Tile(EditorBuildKit.CreateBox($"{name}_Wall_S", new Vector3(centre.x, h / 2f, centre.z - hd), new Vector3(size.x, h, t)), wallTex, 2f);
            Tile(EditorBuildKit.CreateBox($"{name}_Wall_N", new Vector3(centre.x, h / 2f, centre.z + hd), new Vector3(size.x, h, t)), wallTex, 2f);

            // Door wall in two segments, leaving a gap centred on the room — and a matching gap is
            // cut in the corridor wall by the connector below, so the two actually meet.
            float segment = (size.y - doorHalf * 2f) / 2f;
            float segOffset = doorHalf + segment / 2f;
            Tile(EditorBuildKit.CreateBox($"{name}_Wall_Door_A", new Vector3(doorX, h / 2f, centre.z - segOffset), new Vector3(t, h, segment)), wallTex, 2f);
            Tile(EditorBuildKit.CreateBox($"{name}_Wall_Door_B", new Vector3(doorX, h / 2f, centre.z + segOffset), new Vector3(t, h, segment)), wallTex, 2f);

            // Short connecting passage from the corridor wall to the room's doorway.
            float corridorEdge = opensWest ? hostWallX : -hostWallX;
            float passLength = Mathf.Abs(doorX - corridorEdge);
            if (passLength > 0.2f)
            {
                float passX = (doorX + corridorEdge) / 2f;
                Tile(EditorBuildKit.CreateBox($"{name}_Pass_Floor", new Vector3(passX, -0.1f, centre.z), new Vector3(passLength, 0.2f, doorHalf * 2f)), floorTex, 2f);
                Tile(EditorBuildKit.CreateBox($"{name}_Pass_Ceiling", new Vector3(passX, h, centre.z), new Vector3(passLength, 0.2f, doorHalf * 2f)), floorTex, 3f);
                Tile(EditorBuildKit.CreateBox($"{name}_Pass_S", new Vector3(passX, h / 2f, centre.z - doorHalf), new Vector3(passLength, h, t)), wallTex, 2f);
                Tile(EditorBuildKit.CreateBox($"{name}_Pass_N", new Vector3(passX, h / 2f, centre.z + doorHalf), new Vector3(passLength, h, t)), wallTex, 2f);
            }

            // Barely a glow. Side rooms are rooms - you search them by torchlight; the corridor
            // is the only place lit well enough to walk through without one.
            AddFlickeringTube(new Vector3(centre.x, h - 0.25f, centre.z), lightColor, 0.14f);
            DressSideRoom(name, centre, size);
        }

        /// <summary>
        /// Furniture for a side room. Each room draws different pieces from the same pack so three
        /// procedurally identical boxes read as a storage room, a washroom and a records office.
        /// Everything hugs a wall, leaving the centre clear to be chased through.
        /// </summary>
        /// <summary>
        /// Fills a corridor side room with real hospital furniture, so it reads as a room somebody
        /// worked in rather than an empty box with one prop in it.
        ///
        /// Placement is deliberately hand-authored per room and then <b>verified</b>: every piece is
        /// registered in a local occupancy list and any candidate whose footprint overlaps something
        /// already placed is skipped rather than shoved in on top of it. That is what stops the
        /// "gurney inside the closet" result — with a dozen props in a 6x6 room, eyeballing the
        /// coordinates is not enough, and a prop that fails placement is better than one that clips.
        ///
        /// Everything hugs a wall: the middle of each room stays clear, because these rooms are dead
        /// ends and the middle is where you get caught.
        /// </summary>
        private static void FurnishCorridorRoom(string name, Vector3 centre, Vector2 size, Transform parent)
        {
            const string PR = "FirstFloor/Props/";
            var taken = new List<Bounds>();

            // Keep the doorway and the room's centre line clear from the outset.
            bool opensWest = centre.x > 0f;
            float doorX = opensWest ? centre.x - size.x / 2f : centre.x + size.x / 2f;
            taken.Add(new Bounds(new Vector3(doorX, 1f, centre.z), new Vector3(2.4f, 3f, 2.8f)));
            taken.Add(new Bounds(new Vector3(centre.x, 1f, centre.z), new Vector3(2.2f, 3f, 2.2f)));

            bool TryPlace(string model, string label, float height, Vector3 pos, float yaw)
            {
                var go = PlaceProp(parent, PR + model, null, null, $"{name}_{label}", height, pos, yaw);
                if (go == null) return false;
                if (!ArtKit.TryGetBounds(go, out var b)) return true;

                // Compared slightly shrunk, so pieces may sit shoulder to shoulder without being
                // rejected for merely touching.
                var probe = new Bounds(b.center, b.size * 0.85f);
                foreach (var t in taken)
                {
                    if (!t.Intersects(probe)) continue;
                    Debug.Log($"[ArtPass] {name}: skipped '{label}' — it overlapped something already placed.");
                    UnityObject.DestroyImmediate(go);
                    return false;
                }
                taken.Add(b);
                ArtKit.AutoTexture(go, "FirstFloor/Textures", alphaClip: false, pointFilter: false);
                return true;
            }

            float hw = size.x / 2f, hd = size.y / 2f;
            // Wall-hugging anchors, pulled in far enough that a prop's depth still clears the wall.
            float farX = opensWest ? centre.x + hw - 0.7f : centre.x - hw + 0.7f;
            float nearX = opensWest ? centre.x - hw + 0.9f : centre.x + hw - 0.9f;
            float sZ = centre.z - hd + 0.9f, nZ = centre.z + hd - 0.9f;
            float faceIn = opensWest ? -90f : 90f;

            switch (name)
            {
                case "Room_Storage":
                    TryPlace("Furniture/StorageRack/StorageRack.fbx", "Rack1", 1.9f, new Vector3(farX, 0f, sZ), faceIn);
                    TryPlace("Furniture/StorageRack/StorageRack.fbx", "Rack2", 1.9f, new Vector3(farX, 0f, nZ), faceIn);
                    TryPlace("Furniture/Bed_01/Bed_01_D.fbx", "Cot", 0.6f, new Vector3(nearX, 0f, nZ), faceIn);
                    TryPlace("Misc/Bucket/Bucket.fbx", "Bucket", 0.35f, new Vector3(centre.x, 0f, sZ), 0f);
                    TryPlace("Misc/WheelChair/WheelChair.fbx", "Chair", 1.1f, new Vector3(nearX, 0f, sZ), 35f);
                    break;

                case "Room_Washroom":
                    TryPlace("Furniture/Nightstand/Nightstand.fbx", "Stand", 0.55f, new Vector3(farX, 0f, sZ), faceIn);
                    TryPlace("Misc/Bucket/Bucket.fbx", "Bucket1", 0.35f, new Vector3(farX, 0f, nZ), 0f);
                    TryPlace("Misc/Bucket/Bucket.fbx", "Bucket2", 0.35f, new Vector3(centre.x + 0.5f, 0f, nZ), 0f);
                    TryPlace("Misc/IV_Stand/IV_Stand.fbx", "IV", 1.6f, new Vector3(nearX, 0f, nZ), 0f);
                    TryPlace("Furniture/Screen/Screen.fbx", "Screen", 1.8f, new Vector3(nearX, 0f, sZ), faceIn + 20f);
                    break;

                default:   // Room_Records, Room_RecordsNook and anything added later
                    TryPlace("Furniture/StorageRack/StorageRack.fbx", "Rack1", 1.9f, new Vector3(farX, 0f, sZ), faceIn);
                    TryPlace("Furniture/DoctorsDesk/DoctorsDesk.fbx", "Desk", 0.78f, new Vector3(farX, 0f, nZ), faceIn);
                    TryPlace("Furniture/Nightstand/Nightstand.fbx", "Stand", 0.55f, new Vector3(nearX, 0f, nZ), faceIn);
                    TryPlace("Misc/WheelChair/WheelChair.fbx", "Chair", 1.1f, new Vector3(nearX, 0f, sZ), -25f);
                    TryPlace("Misc/Bucket/Bucket.fbx", "Bucket", 0.35f, new Vector3(centre.x, 0f, nZ), 0f);
                    break;
            }
        }

        private static void DressSideRoom(string name, Vector3 centre, Vector2 size)
        {
            var parent = new GameObject($"{name}_Dressing").transform;
            var scratch = new GameObject($"~{name}_Scratch").transform;

            FurnishCorridorRoom(name, centre, size, parent);

            var furnitureModel = ArtKit.LoadModel("Props/Furniture/furniture_without_scene.fbx");
            if (furnitureModel != null)
            {
                var pack = ArtKit.Spawn(furnitureModel, scratch, "FurniturePack");
                const string tex = "Props/Furniture/textures/";
                float hw = size.x / 2f - 0.7f, hd = size.y / 2f - 0.7f;

                switch (name)
                {
                    case "Room_Storage":
                        PlaceFromPack(pack, parent, "Storage_Closet", tex + "closet.png", "M_Closet", 2f,
                            centre + new Vector3(-hw, 0f, hd), 135f, "Closet");
                        PlaceFromPack(pack, parent, "Storage_Table", tex + "table.png", "M_Table", 0.75f,
                            centre + new Vector3(hw * 0.4f, 0f, -hd), 15f, "Table");
                        PlaceFromPack(pack, parent, "Storage_Chair", tex + "chair.png", "M_Chair", 0.95f,
                            centre + new Vector3(hw * 0.4f, 0f, -hd + 0.9f), 190f, "Chair_1");
                        break;

                    case "Room_Washroom":
                        PlaceFromPack(pack, parent, "Wash_Stand", tex + "bedside_table2.png",
                            "M_BedsideTable_Wood", 0.6f, centre + new Vector3(hw, 0f, hd), -120f, "Bedside_table");
                        PlaceFromPack(pack, parent, "Wash_Pot", tex + "pot.png", "M_Pot", 0.45f,
                            centre + new Vector3(-hw, 0f, -hd), 0f, "Pot");
                        break;

                    case "Room_Records":
                        PlaceFromPack(pack, parent, "Rec_Closet", tex + "closet.png", "M_Closet", 2f,
                            centre + new Vector3(hw, 0f, -hd), -140f, "Closet");
                        PlaceFromPack(pack, parent, "Rec_Coffee", tex + "coffee_table.png", "M_CoffeeTable", 0.5f,
                            centre + new Vector3(-hw * 0.5f, 0f, hd * 0.5f), 40f, "Coffe_table");
                        PlaceFromPack(pack, parent, "Rec_Sofa", tex + "sofa_plating.png", "M_Sofa", 0.85f,
                            centre + new Vector3(-hw, 0f, -hd), 55f, "Sofa");
                        break;
                }
            }

            // The mirror goes in the washroom, where a mirror belongs — and where a player checking
            // it is facing away from the door.
            if (name == "Room_Washroom")
            {
                // A washroom mirror hangs ON the wall. PlaceProp grounds props on the floor, which
                // stood a 1.7m mirror up out of the tiles like a wardrobe — so it's placed, then
                // lifted to eye level and pushed flush against the inside face of the north wall
                // (wall plane at centre.z + size.y/2, 0.3 thick, so its inner surface is 0.15 in).
                const float wallT = 0.3f;
                var mirror = PlaceProp(parent, "Props/Mirror/scene.gltf", null, null,
                    "Mirror", 0.95f, centre + new Vector3(0f, 0f, size.y / 2f - 0.4f), 180f);
                if (mirror != null)
                {
                    mirror.transform.position = new Vector3(
                        centre.x,
                        1.15f,                                              // bottom edge at chest height
                        centre.z + size.y / 2f - wallT / 2f - 0.06f);       // just proud of the wall face
                    ArtKit.AutoTexture(mirror, "Props/Mirror/textures", alphaClip: false, pointFilter: false);
                }
            }

            // Batteries for the torch, one per room, on the floor against a wall.
            EditorBuildKit.CreateToolPickup("battery", "battery",
                centre + new Vector3(size.x / 2f - 0.5f, 0f, -size.y / 2f + 0.6f),
                new Color(0.4f, 0.8f, 0.4f),
                standaloneModel: "Props/Battery/scene.gltf", standaloneTextures: "Props/Battery/textures");

            UnityObject.DestroyImmediate(scratch.gameObject);
        }

        /// <summary>
        /// A naked flickering light with no fixture — for flames. Dim and short-range on purpose:
        /// a candle should pick out the table it stands on, not floodlight the room.
        /// </summary>
        private static Light AddFlameLight(Vector3 position, Color color, float intensity, float range)
        {
            var go = new GameObject("Flame");
            go.transform.position = position;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;

            var flicker = go.AddComponent<FlickeringLight>();
            EditorBuildKit.SetFloat(flicker, "baseIntensity", intensity);
            // A flame guts and recovers constantly; it never blacks out the way a dying tube does.
            EditorBuildKit.SetFloat(flicker, "flickerAmount", 0.45f);
            EditorBuildKit.SetFloat(flicker, "flickerSpeed", 9f);
            EditorBuildKit.SetFloat(flicker, "dropoutIntervalMin", 600f);
            EditorBuildKit.SetFloat(flicker, "dropoutIntervalMax", 900f);
            return light;
        }

        /// <summary>A ceiling tube that buzzes, dips and occasionally dies outright.</summary>
        private static Light AddFlickeringTube(Vector3 position, Color color, float intensity, float range = 9f)
        {
            var go = new GameObject("CeilingTube");
            go.transform.position = position;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;   // several of these with shadows is not worth the cost

            var flicker = go.AddComponent<FlickeringLight>();
            EditorBuildKit.SetFloat(flicker, "baseIntensity", intensity);

            // A dim housing so there's something visibly producing the light.
            var housing = EditorBuildKit.CreateBox("TubeHousing", position + new Vector3(0f, 0.14f, 0f),
                new Vector3(0.9f, 0.08f, 0.16f));
            EditorBuildKit.SetMaterial(housing, EditorBuildKit.MakeEmissive(
                new Color(0.06f, 0.06f, 0.06f), color * 1.6f));
            UnityObject.DestroyImmediate(housing.GetComponent<Collider>());
            housing.transform.SetParent(go.transform);
            return light;
        }

        private static void AddDimLight(Vector3 position, float intensity)
        {
            var light = new GameObject("DimLight").AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 8f;
            light.intensity = intensity;
            light.transform.position = position;
        }

        // P3 from the plan, relocated here from the M2 sandbox: 3 stations activated in the order
        // the radio log gives, gating the actual final exit door.
        private static void CreateIntercomPuzzleInExit()
        {
            // THREE LOCKS, ON THE DOOR ITSELF. The old version was three glowing boxes scattered
            // around the room, pressed in a sequence for no reason anyone could name. Now the way out
            // is visibly barred: three locks in a row at head height, each wanting its own key, and
            // the keys are somewhere in the corridor's rooms.
            //
            // The order is the nurse's. She worked this door twice a night for years and always went
            // top, bottom, middle - never the obvious way down the row. The notes are how you learn
            // it; getting it wrong resets the sequence and makes a noise the Entity hears.
            var lockPuzzle = EditorBuildKit.CreateIntercomPuzzle(
                "Door_FinalExit", new Vector3(-1.5f, 0f, 54f),
                new Vector3(0f, 1f, 52.5f),
                new (string, Vector3)[]
                {
                    ("the top lock",    new Vector3(-1.15f, 1.95f, 53.78f)),
                    ("the middle lock", new Vector3(-1.15f, 1.45f, 53.78f)),
                    ("the bottom lock", new Vector3(-1.15f, 0.95f, 53.78f)),
                },
                new Vector3(0f, 1f, 51f));

            WireCorridorLocks();
        }

        private static void CreateFinalExitTrigger()
        {
            var triggerGO = new GameObject("Trigger_FinalExit");
            triggerGO.transform.position = new Vector3(0f, 1.5f, 55.5f);
            triggerGO.AddComponent<Unity.Netcode.NetworkObject>();
            var box = triggerGO.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(3f, 3f, 1f);
            var trigger = triggerGO.AddComponent<OneSlotExitTrigger>();

            var door = GameObject.Find("Door_FinalExit");
            if (door != null && door.TryGetComponent<LastWard.Net.NetworkedDoor>(out var networkedDoor))
                EditorBuildKit.SetRef(trigger, "exitDoor", networkedDoor);
            else
                Debug.LogWarning("Door_FinalExit not found when wiring the one-slot exit trigger.");
        }

        // Bible §7 "choice beat": spans the Exit Route's entrance from the Corridor, so whoever
        // arrives with teammates still alive gets the "only one of you can leave" warning before
        // reaching the door itself.
        private static void CreateExitChoiceZone()
        {
            var zoneGO = new GameObject("Zone_ExitChoice");
            zoneGO.transform.position = new Vector3(0f, 1.5f, 45f);
            var box = zoneGO.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(7f, 3f, 3f);
            zoneGO.AddComponent<ExitChoiceZone>();
        }

        // P2 from the plan, relocated here from the M2 sandbox: keypad code from 3 valid patient
        // files (a 4th is a deliberate contradiction the criterion note lets you exclude). Gates a
        // self-contained records nook rather than the main path, since Service Corridor isn't built yet.
        private static void CreateRecordCodePuzzleInWard()
        {
            // The records nook is now an ORDINARY side room — it holds the clues, so locking it
            // behind the code those clues give you was circular. Hinged on the south edge of the
            // 2m gap in the east wall at x=5, swinging across Z since it sits in a side wall.
            var nookDoor = EditorBuildKit.CreateNetworkedDoor("Door_RecordsNook", new Vector3(5f, 0f, 17f),
                width: 2f, height: 3f, startLocked: false);
            nookDoor.transform.rotation = Quaternion.Euler(0f, -90f, 0f);

            // The nook itself. Without a room behind it the door opened onto solid wall.
            BuildSideRoom("Room_RecordsNook", new Vector3(9f, 0f, 18f), new Vector2(5f, 5f),
                "Textures/H_Wall_Room.png", "Textures/Floor_Stone.png", new Color(0.9f, 0.75f, 0.5f),
                hostWallX: 5f);

            // The code now gates the WAY ONWARD, not a side cupboard: the Ward's north doorway into
            // the Service Corridor. A puzzle the player can walk around isn't a gate, and it left
            // the main path standing open while the only locked door sat on a side wall.
            var door = EditorBuildKit.CreateNetworkedDoor("Door_ToCorridor", new Vector3(-1f, 0f, 20f),
                width: 2f, height: 3f);

            // Mounted on the Ward's north wall beside that doorway, facing back into the Ward.
            var keypadGO = EditorBuildKit.CreateKeypad("Keypad", new Vector3(1.7f, 1.35f, 19.8f), 180f);
            keypadGO.AddComponent<Unity.Netcode.NetworkObject>();
            var puzzle = keypadGO.AddComponent<LastWard.Puzzles.RecordCodePuzzle>();
            EditorBuildKit.SetRef(puzzle, "gatedDoor", door);
            var keypad = keypadGO.AddComponent<LastWard.Puzzles.KeypadInteractable>();
            EditorBuildKit.SetRef(keypad, "puzzle", puzzle);

            EditorBuildKit.CreateNoteProp("Note_CriterionMemo", new Vector3(-4.2f, 0f, 11f),
                "Assets/_Project/Data/CriterionMemoClue.asset", "p2_criterion_memo", "Admission Policy Memo",
                "Fire took the east wing in March 1974. Anyone processed after that point wasn't real intake -- paperwork only, backdated to cover the gap.\n\nCount only the ones admitted before. List their rooms, oldest to newest.",
                2f);

            var file1 = EditorBuildKit.CreateNoteProp("Note_File1", new Vector3(4.2f, 0f, 11f),
                "Assets/_Project/Data/PatientFile1Clue.asset", "p2_file_room4", "Patient Intake -- Room 4",
                "Admitted January 12, 1974. Quiet. Doesn't speak much.", 2f);
            var file2 = EditorBuildKit.CreateNoteProp("Note_File2", new Vector3(-4.2f, 0f, 15f),
                "Assets/_Project/Data/PatientFile2Clue.asset", "p2_file_room8", "Patient Intake -- Room 8",
                "Admitted February 3, 1974. Transferred from the county home.", 2f);
            var file3 = EditorBuildKit.CreateNoteProp("Note_File3", new Vector3(4.2f, 0f, 15f),
                "Assets/_Project/Data/PatientFile3Clue.asset", "p2_file_room2", "Patient Intake -- Room 2",
                "Admitted February 20, 1974. No family listed.", 2f);
            // The decoy: dated after the March 1974 fire per the criterion memo, so it should be
            // excluded from the code -- the actual "figure it out" moment of the puzzle.
            var file4 = EditorBuildKit.CreateNoteProp("Note_File4", new Vector3(-4.2f, 0f, 19f),
                "Assets/_Project/Data/PatientFile4Clue.asset", "p2_file_room9", "Patient Intake -- Room 9",
                "Admitted April 5, 1974. [Note: ward reopened under new administration.]", 2f);

            var shufflerGO = new GameObject("ClueSpawnShuffler_P2");
            shufflerGO.AddComponent<Unity.Netcode.NetworkObject>();
            var shuffler = shufflerGO.AddComponent<LastWard.Puzzles.ClueSpawnShuffler>();
            EditorBuildKit.SetRefArray(shuffler, "clues", new UnityEngine.Object[] { file1, file2, file3, file4 });
            // Patient files are stashed in ward furniture rather than floating at head height.
            // Deliberately more open spots than locked ones: all four files are needed for the code,
            // so gating too many of them behind the single crowbar turns a search into a chore.
            var wardStashes = new System.Collections.Generic.List<UnityEngine.Object>
            {
                EditorBuildKit.CreateStorageContainer("Cabinet_WardW", new Vector3(-4.5f, 0f, 12.4f), 90f,
                    new Vector3(0.9f, 1.2f, 0.5f), "", false, "Open the file cabinet"),
                EditorBuildKit.CreateStorageContainer("Cabinet_WardE", new Vector3(4.5f, 0f, 15.4f), -90f,
                    new Vector3(0.9f, 1.2f, 0.5f), "", false, "Open the file cabinet"),
                EditorBuildKit.CreateStorageContainer("Cabinet_WardN", new Vector3(-2.4f, 0f, 19.4f), 180f,
                    new Vector3(0.9f, 1.1f, 0.5f), "", false, "Open the file cabinet"),
                EditorBuildKit.CreateStorageContainer("Crate_Ward", new Vector3(2.6f, 0f, 19.4f), 180f,
                    new Vector3(0.85f, 0.65f, 0.65f), "crowbar", false, "Lever the crate open"),
                // Under the two ward beds — visible only if you think to look low.
                EditorBuildKit.MakePoint(new Vector3(-3.5f, 0.12f, 16.8f)),
                EditorBuildKit.MakePoint(new Vector3(3.5f, 0.12f, 12.2f)),
            };
            EditorBuildKit.SetRefArray(shuffler, "candidatePoints", wardStashes.ToArray());
        }

        private static void CreateZoneThreshold()
        {
            var triggerGO = new GameObject("Trigger_ExteriorToLobby");
            triggerGO.transform.position = new Vector3(0f, 1.5f, -0.5f);
            var box = triggerGO.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2f, 3f, 1.5f);
            var trigger = triggerGO.AddComponent<ObjectiveZoneTrigger>();
            EditorBuildKit.SetInt(trigger, "stageOnEnter", (int)ObjectiveStage.Lobby);
        }


        private static void CreateDiscoveryUI()
        {
            var canvasGO = GameObject.Find("Canvas");
            if (canvasGO == null)
            {
                Debug.LogWarning("Canvas not found — CreateDiscoveryUI must run after CreateConnectionUI.");
                return;
            }
            EditorBuildKit.CreateDiscoveryMeterUI(canvasGO.transform);
            EditorBuildKit.CreateHidingOverlayUI(canvasGO.transform);
            EditorBuildKit.CreateControlsPanelUI(canvasGO.transform);
            EditorBuildKit.CreateJumpscareUI(canvasGO.transform);
        }

        /// <summary>
        /// Notes stashed in containers and under beds — the game's only voice. Three rules:
        ///
        /// 1. <b>Never name it.</b> Every writer describes symptoms, never a creature. Nobody who
        ///    wrote these understood what they were dealing with, and several are plainly wrong.
        /// 2. <b>Never agree on what this building was.</b> One writes about patients, another about
        ///    guests, another about residents and a duty roster. The player should not be able to
        ///    settle whether this was a hospital, a care home or a hotel until very late.
        /// 3. <b>Contradict each other.</b> Two notes disagreeing is worth more than either being
        ///    informative, because it makes the player decide who to believe.
        ///
        /// Placed in the same container and under-bed spots the items use, so reading the lore and
        /// finding the tools are the same activity.
        /// </summary>
        private static void CreateLoreNotes()
        {
            var spots = new List<Vector3>
            {
                new Vector3(-4.4f, 0.55f, 7.4f),    // Lobby cupboard
                new Vector3(2.5f, 0.12f, 2.8f),     // under the Lobby bed
                new Vector3(-4.5f, 0.55f, 12.4f),   // Ward cabinet
                new Vector3(4.5f, 0.55f, 15.4f),    // Ward cabinet
                new Vector3(-3.5f, 0.12f, 16.8f),   // under a Ward bed
                new Vector3(9.5f, 0.5f, 24f),       // Storage room
                new Vector3(9.5f, 0.5f, 34f),       // Washroom
                new Vector3(-9.5f, 0.5f, 41f),      // Records room
            };

            (string id, string title, string body)[] notes =
            {
                ("lore_intake", "Intake Sheet (partial)",
                 "Room 6 asked again about the night staff. Told her the same as before: there is no night staff, we are a small facility and the doors lock at eight.\n\nShe said she wasn't asking about staff."),

                ("lore_maintenance", "Maintenance Log",
                 "Third callout this month for the corridor lights. Wiring tests clean every time.\n\nThey only fail on the east run. Never the wards. I have stopped writing down what colour."),

                ("lore_guest", "Guest Comment Card",
                 "Lovely stay otherwise, but the walls are terribly thin - we could hear the family in the next room walking about at all hours.\n\n(On the reverse, a different hand: 'There is no next room.')"),

                ("lore_roster", "Duty Roster - week of the 14th",
                 "Names scratched out down the whole column. One left, circled twice:\n\n'Whoever is on nights - do NOT do the round alone. Not because of the residents.'"),

                ("lore_letter", "Unsent Letter",
                 "I keep meaning to tell you what it is like here but every time I sit down it sounds mad written out.\n\nIt is not a person. I want to be clear about that, because the others keep saying a person. A person would have got tired by now."),

                ("lore_referral", "Referral Note",
                 "Transferred from the county home. Records incomplete - no admission date, no next of kin.\n\nQuery with administration: we have no record of this facility ever being a county home."),

                ("lore_incident", "Incident Report (incomplete)",
                 "Two residents reported the same figure at the end of the corridor. Their descriptions do not match each other in any particular - height, dress, even the number of them.\n\nBoth were adamant it had been standing there a long time before they noticed."),

                ("lore_last", "Torn Page",
                 "- and it is not the one in the wards. That one wants to be seen.\n\nWhatever is below does not, and I think that is the difference that matters."),
            };

            // Shuffled so the same note is not in the same drawer every run.
            for (int i = spots.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (spots[i], spots[j]) = (spots[j], spots[i]);
            }

            for (int i = 0; i < notes.Length && i < spots.Count; i++)
            {
                var entry = notes[i];
                EditorBuildKit.CreateNoteProp($"Note_{entry.id}", spots[i],
                    $"Assets/_Project/Data/{entry.id}.asset", entry.id, entry.title, entry.body, 1.5f);
            }
        }

        // Somewhere to break line of sight in each zone the Entity patrols, so the discovery meter
        // always has an answer nearby. Positions hug walls and stay clear of every note, pickup,
        // puzzle interactable and patrol waypoint.
        private static void CreateHidingSpots()
        {
            // Lobby — wardrobes against each side wall, fronts turned to face into the room so the
            // slats look out over the floor you're hiding from.
            EditorBuildKit.CreateWardrobeHidingSpot("Hide_Wardrobe_LobbyW",
                new Vector3(-4.5f, 0f, 4.4f), "Hide in the wardrobe", 90f);
            EditorBuildKit.CreateWardrobeHidingSpot("Hide_Wardrobe_LobbyE",
                new Vector3(4.5f, 0f, 8.6f), "Hide in the wardrobe", -90f);

            // Ward — beds to crawl under, clear of the patient-file spawn points.
            EditorBuildKit.CreateUnderBedHidingSpot("Hide_Bed_WardW",
                new Vector3(-3.5f, 0f, 16.8f), "Hide under the bed", 0f);
            EditorBuildKit.CreateUnderBedHidingSpot("Hide_Bed_WardE",
                new Vector3(3.5f, 0f, 12.2f), "Hide under the bed", 0f);

            // Corridor — a locker in the dead-end alcove. Deliberately as much trap as refuge:
            // it's the one place with no second way out.
            EditorBuildKit.CreateWardrobeHidingSpot("Hide_Locker_Alcove",
                new Vector3(-6.1f, 0f, 29.6f), "Hide in the locker", 90f);
        }

        private static void CreateObjectiveTracker()
        {
            var go = new GameObject("ObjectiveTracker");
            go.AddComponent<Unity.Netcode.NetworkObject>();
            go.AddComponent<ObjectiveTracker>();

            // Resets the run once it's over, rather than leaving everyone on a death screen.
            new GameObject("RunRestarter").AddComponent<RunRestarter>();
        }

        private static void CreateObjectiveUI()
        {
            var canvasGO = GameObject.Find("Canvas");
            if (canvasGO == null)
            {
                Debug.LogWarning("Canvas not found — CreateObjectiveUI must run after CreateConnectionUI.");
                return;
            }

            var rect = EditorBuildKit.CreateRect("ObjectiveText", canvasGO.transform, new Vector2(0.5f, 1f), new Vector2(600f, 40f), new Vector2(0f, -20f));
            var text = EditorBuildKit.CreateText(rect, "Text", 20f, TextAlignmentOptions.Center);
            EditorBuildKit.StretchToParent((RectTransform)text.transform);

            var objectiveUI = canvasGO.AddComponent<ObjectiveUI>();
            EditorBuildKit.SetRef(objectiveUI, "text", text);
        }

        // Spans Lobby + Ward + Corridor — deliberately NOT the Exit Route, so the finale plays out
        // as puzzle + one-slot-door tension rather than also dodging the Entity right at the end.
        // 2 per zone rather than the plan's "~4 per zone" — enough spatial variety to prove the
        // nearest-anchor logic without placing 20 markers by hand; trivial to add more later.
        private static void CreateAftermathAnchors()
        {
            Vector3[] positions =
            {
                new Vector3(2f, 0f, -18f), new Vector3(-2f, 0f, -12f), // Exterior
                new Vector3(2f, 0f, 4f), new Vector3(-2f, 0f, 6f),     // Lobby
                new Vector3(2f, 0f, 14f), new Vector3(-2f, 0f, 16f),   // Ward
                new Vector3(2.5f, 0f, 27f), new Vector3(-2.5f, 0f, 35f), // Corridor
                new Vector3(2f, 0f, 48f), new Vector3(-2f, 0f, 50f),   // Exit Route
            };
            foreach (var p in positions) EditorBuildKit.CreateAftermathAnchor(p);
        }

        private static Transform[] CreatePatrolWaypoints()
        {
            var parent = new GameObject("PatrolWaypoints").transform;
            Vector3[] positions =
            {
                new Vector3(-3f, 0f, 2f), new Vector3(3f, 0f, 2f),
                new Vector3(3f, 0f, 8f), new Vector3(-3f, 0f, 8f),
                new Vector3(-3f, 0f, 12f), new Vector3(3f, 0f, 12f),
                new Vector3(3f, 0f, 18f), new Vector3(-3f, 0f, 18f),
                new Vector3(0f, 0f, 22f),
                new Vector3(-2.5f, 0f, 31f), new Vector3(2.5f, 0f, 31f),
                new Vector3(0f, 0f, 40f),
            };
            var points = new Transform[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                var wp = new GameObject($"WP_{i}").transform;
                wp.SetParent(parent);
                wp.position = positions[i];
                points[i] = wp;
            }
            return points;
        }

        // --- M8 art pass ---------------------------------------------------------------------
        // Real meshes layered over the greybox, strictly as decoration: nothing here adds, moves or
        // removes a collider, trigger, door, NavMesh surface or puzzle position. Where a real mesh
        // stands in for a greybox (the Entity, the car) the greybox keeps its collider and only its
        // Renderer is switched off, so physics and navigation still resolve against the boxes the
        // level was designed around.
        //
        // All sizes below are real-world metres, applied by measuring each model's actual bounds
        // (see ArtKit) rather than by hand-picked scale numbers. The packs disagree wildly about
        // units — guessing is what put the Entity through the roof and the car under the floor on
        // the first attempt.
        private const string ArtRootName = "ArtPass";
        private const string ScratchName = "~ArtScratch";
        // Mesh + skeleton + all four Watcher actions in ONE file. The clips used to live in separate
        // skeleton-only .glb files and never bound to this model's avatar, which is why the Entity
        // stood frozen in its bind pose with no idle, walk or run playing at all.
        private const string EntityModelPath = "Characters/Entity/Watcher_Entity.glb";

        [MenuItem("The Last Ward/Reapply M8 Art Pass")]
        public static void ReapplyArtPass()
        {
            ApplyArtPass();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("M8 art pass re-applied.");
        }

        private static void ApplyArtPass()
        {
            // One root for everything, so re-running is a clean rebuild rather than a pile-up.
            var previous = GameObject.Find(ArtRootName);
            if (previous != null) UnityObject.DestroyImmediate(previous);
            var staleScratch = GameObject.Find(ScratchName);
            if (staleScratch != null) UnityObject.DestroyImmediate(staleScratch);

            var root = new GameObject(ArtRootName).transform;
            // Holding pen for pack instances we cut apart; destroyed once we're done with them.
            var scratch = new GameObject(ScratchName).transform;

            // Done before anything is instantiated: the farm pack ships 4K textures that stall
            // play-mode entry if left at full resolution.
            ArtKit.CapTextureSize("Environment/Farm/textures", 256);
            // Dressing textures that arrived oversized. The grass albedo alone is 3.6MB.
            ArtKit.CapTextureSize("Environment/Grass", 256);
            ArtKit.CapTextureSize("Environment/TreePack", 256);
            ArtKit.CapTextureSize("Characters/Entity/textures", 512);

            AddMoonlight(root);
            ScatterTreeline(root, scratch);
            ScatterForestTrail(root);
            ScatterGrass(root);
            PlaceFarmStructures(root, scratch);
            SwapCarVisual(root);
            DressInteriors(root, scratch);
            SwapEntityVisual();
            DressCorridorLocks(scratch);

            // Runs LAST, once every prop's final world position is known.
            ClearCirculation();

            UnityObject.DestroyImmediate(scratch.gameObject);
            ArtKit.FlushAssets();
        }

        /// <summary>
        /// Guarantees that nothing you can walk into ever stands in the way.
        ///
        /// Every doorway, every circulation lane and every stair run is a keep-clear volume. Any
        /// dressed prop whose collider intrudes into one is DELETED outright - not merely stripped
        /// of collision, because a wardrobe standing in the middle of a doorway is wrong to look at
        /// as well as wrong to walk into.
        ///
        /// This has to be the final step of the art pass: it is the only point at which every prop
        /// has been scaled, rotated and grounded, so it is the only point where "does this actually
        /// block the corridor" is a question that can be answered rather than guessed at. It matters
        /// for the Entity as much as the players - props now carve the NavMesh, so one badly placed
        /// chair could wall it out of a room entirely.
        /// </summary>
        private static void ClearCirculation()
        {
            var keepClear = new List<Bounds>();

            // Every door: the leaf swing plus standing room either side of the threshold.
            foreach (var door in UnityObject.FindObjectsByType<LastWard.Net.NetworkedDoor>(FindObjectsInactive.Include))
                keepClear.Add(new Bounds(door.transform.position + Vector3.up * 1.5f, new Vector3(4.5f, 3f, 4.5f)));

            // The routes everything actually travels along.
            keepClear.Add(new Bounds(new Vector3(0f, 1.5f, 5f),   new Vector3(2.8f, 3f, 10f)));  // Lobby spine
            keepClear.Add(new Bounds(new Vector3(0f, 1.5f, 15f),  new Vector3(2.8f, 3f, 10f)));  // Ward spine
            keepClear.Add(new Bounds(new Vector3(0f, 1.5f, 32f),  new Vector3(3.2f, 3f, 24f)));  // Service Corridor
            keepClear.Add(new Bounds(new Vector3(0f, 1.5f, 49f),  new Vector3(3.2f, 3f, 10f)));  // Exit Route
            keepClear.Add(new Bounds(new Vector3(0f, 2.5f, 57.5f), new Vector3(4f, 7f, 7f)));    // stair shaft + climb
            keepClear.Add(new Bounds(new Vector3(0f, FFy + 1.5f, 72f), new Vector3(3.2f, 3f, 30f))); // FF corridor

            // Anything the player has to USE also gets its own bubble: notes, pickups, keypads,
            // breakers, and every container (whose interior is a spawn point the shuffler fills at
            // runtime). Decoration must never be placed where an interactable is, or you get the
            // note half-buried in a closet and the fuse hidden inside a gurney. Interactables are
            // authored deliberately; furniture is filler, so furniture is what yields.
            foreach (var mb in UnityObject.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (mb is not LastWard.Core.IInteractable) continue;
                var col = mb.GetComponentInChildren<Collider>();
                Vector3 c = col != null ? col.bounds.center : mb.transform.position;
                Vector3 sz = col != null ? col.bounds.size : Vector3.one * 0.5f;
                // Generous margin: enough clearance to actually see and reach the thing.
                keepClear.Add(new Bounds(c, sz + new Vector3(1.1f, 0.6f, 1.1f)));
            }

            // Only dressing is eligible: greybox walls, doors and pickups are not "props".
            var roots = new List<Transform>();
            var art = GameObject.Find(ArtRootName);
            if (art != null) roots.Add(art.transform);
            foreach (var t in UnityObject.FindObjectsByType<Transform>(FindObjectsInactive.Include))
                if (t != null && t.name.EndsWith("_Dressing")) roots.Add(t);

            var doomed = new List<GameObject>();
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var col in root.GetComponentsInChildren<BoxCollider>(true))
                {
                    if (col == null) continue;
                    foreach (var zone in keepClear)
                    {
                        if (!zone.Intersects(col.bounds)) continue;
                        if (!doomed.Contains(col.gameObject)) doomed.Add(col.gameObject);
                        break;
                    }
                }
            }

            foreach (var go in doomed)
            {
                if (go == null) continue;
                Debug.Log($"[ArtPass] Cleared '{go.name}' - it was standing in a walkway.");
                UnityObject.DestroyImmediate(go);
            }
            Debug.Log($"[ArtPass] Circulation check: {doomed.Count} prop(s) removed from doorways, " +
                      "lanes and stairs. Every route is walkable for players and Entities alike.");
        }

        // Textured geometry needs some directional light to read at all — the greybox got away with
        // flat 0.05 ambient because it was untextured blocks, but real meshes just go black. Kept
        // dim and cold so it reads as moonlight, not daylight.
        //
        // Shadows are ON deliberately: a shadowless directional light passes straight through the
        // interior walls and ceilings and would wash out every room the darkness depends on.
        private static void AddMoonlight(Transform root)
        {
            var go = new GameObject("Moonlight");
            go.transform.SetParent(root, false);
            go.transform.rotation = Quaternion.Euler(35f, 145f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.55f, 0.62f, 0.9f);
            light.intensity = 0.11f;
            // Hard shadows, not soft. Soft shadows on a directional light mean a filtered lookup
            // over the whole scene every frame, and this one covers the entire exterior including
            // every tree. Hard shadows keep the interiors dark (which is what the shadow is FOR)
            // at a fraction of the cost.
            light.shadows = LightShadows.Hard;
        }

        // "Duplicate the trees to make it denser and eerier" — the forest pack is a showcase scene
        // of ~50 loose meshes, so it gets split up and only the tall items (the trees) are cloned.
        //
        // They're placed OUTSIDE the yard's boundary walls on purpose: decoration carries no
        // colliders, so a tree standing in the play area would be walk-through, and adding colliders
        // instead would mean re-baking the NavMesh the Entity already navigates. Out here they read
        // as a treeline looming over the 4m walls with no gameplay surface at all.
        private static void ScatterTreeline(Transform root, Transform scratch)
        {
            var model = ArtKit.LoadModel("Environment/Forest/scene.gltf");
            if (model == null) return;

            var pack = ArtKit.Spawn(model, scratch, "ForestPack");
            // Textured before splitting so every clone inherits it. Alpha clipping matters here:
            // PSX foliage is cut-out quads, and without it each tree renders as a solid dark slab.
            ArtKit.AutoTexture(pack, "Environment/Forest/textures", alphaClip: true);
            var props = ArtKit.SplitIntoProps(pack, scratch);

            var trees = new List<GameObject>();
            foreach (var p in props)
            {
                if (trees.Count >= 4) break;
                if (ArtKit.TryGetBounds(p, out var pb) && pb.size.y > 1f) trees.Add(p);
            }
            if (trees.Count == 0)
            {
                Debug.LogWarning("[ArtPass] No tree-sized props in the forest pack — treeline skipped.");
                return;
            }

            var parent = new GameObject("Treeline").transform;
            parent.SetParent(root, false);

            var rng = new System.Random(20260721);
            var spots = new List<Vector3>();
            for (float z = 2f; z >= -34f; z -= 4f)
            {
                spots.Add(new Vector3(-16.5f - (float)rng.NextDouble() * 8f, 0f, z));
                spots.Add(new Vector3(16.5f + (float)rng.NextDouble() * 8f, 0f, z));
            }
            for (float x = -24f; x <= 24f; x += 4f)
                spots.Add(new Vector3(x, 0f, -36.5f - (float)rng.NextDouble() * 8f));

            foreach (var spot in spots)
            {
                var template = trees[rng.Next(trees.Count)];
                var tree = (GameObject)UnityObject.Instantiate(template, parent);
                tree.name = "Tree";
                ArtKit.FitHeight(tree, 6f + (float)rng.NextDouble() * 3f);
                tree.transform.Rotate(0f, (float)rng.NextDouble() * 360f, 0f, Space.World);
                ArtKit.GroundAt(tree, spot);
            }
        }

        // Dead trees stand INSIDE the yard, unlike the treeline. The spawn area was a flat empty
        // square, and the treeline sits outside the walls where it can't break that up. These are
        // sparse and pushed off the walking line between the car (z -15) and the door (z 0), so
        // they add depth without turning the approach into an obstacle course. They keep no
        // colliders, same rule as the rest of the decoration.
        /// <summary>
        /// Fills the yard with forest, carved by a winding trail from the car up to the doors.
        ///
        /// The trail is the point. An open square reads as an empty stage; a path through dense
        /// trees reads as somewhere you were brought to, and it removes the "which way?" question without
        /// a marker. Trees are kept clear of the trail by distance test rather than by hand-placing,
        /// so the corridor of open ground is guaranteed rather than eyeballed.
        /// </summary>
        private static void ScatterForestTrail(Transform root)
        {
            string[] models =
            {
                "Environment/TreePack/tree_rt_1.glb", "Environment/TreePack/tree_rt_2.glb",
                "Environment/TreePack/tree_rt_3.glb", "Environment/TreePack/tree_rt_4.glb",
                "Environment/TreePack/dead_tree_rt_1.glb", "Environment/TreePack/dead_tree_rt_2.glb",
                "Environment/TreePack/small_tree_rt_1.glb",
            };
            var loaded = new List<GameObject>();
            foreach (var m in models)
            {
                var g = ArtKit.LoadModel(m);
                if (g != null) loaded.Add(g);
            }
            if (loaded.Count == 0) return;

            var parent = new GameObject("Forest").transform;
            parent.SetParent(root, false);

            // Capped for the same reason as the grass — these are full tree meshes.
            const int maxTrees = 22;
            var rng = new System.Random(90210);
            int placed = 0;

            // Yard spans x[-15,15], z[-35,0]. Trees fill it except along the trail and around the
            // car and door approach.
            for (float z = -34f; z <= -1f && placed < maxTrees; z += 5.5f)
            {
                for (float x = -14f; x <= 14f && placed < maxTrees; x += 5.5f)
                {
                    float jx = x + (float)(rng.NextDouble() - 0.5) * 1.8f;
                    float jz = z + (float)(rng.NextDouble() - 0.5) * 1.8f;
                    if (Mathf.Abs(jx - TrailCentreX(jz)) < TrailHalfWidth) continue;   // keep the path clear
                    if (Mathf.Abs(jx) < 3f && jz > -18f) continue;                     // car + door approach
                    if ((float)rng.NextDouble() > 0.62) continue;                      // thin it out

                    int pick = rng.Next(loaded.Count);
                    var tree = ArtKit.Spawn(loaded[pick], parent, "Tree");
                    // Assigned explicitly. AutoTexture matches meshes to textures by NAME, and this
                    // pack's atlases are called "DeadTrees"/"LiveTrees" while its meshes are called
                    // "tree_rt_1" — nothing matches, so every tree came out as an untextured slab,
                    // which is why the forest read as a row of buildings.
                    bool dead = models[pick].Contains("dead_tree");
                    ArtKit.ApplyMaterial(tree, ArtKit.MakeTexturedMaterial(
                        dead ? "Environment/TreePack/DeadTrees.png" : "Environment/TreePack/LiveTrees.png",
                        dead ? "M_TreeDead" : "M_TreeLive", alphaClip: true));
                    // The pack is authored Z-up; without standing it first, FitHeight scales the
                    // trunk's width instead of its height.
                    ArtKit.StandUpright(tree);
                    ArtKit.FitHeight(tree, 4.5f + (float)rng.NextDouble() * 4f);
                    tree.transform.Rotate(0f, (float)rng.NextDouble() * 360f, 0f, Space.World);
                    // Bedded slightly into the ground — a trunk sitting exactly on the surface
                    // reads as floating on uneven terrain.
                    ArtKit.GroundAt(tree, new Vector3(jx, -0.25f, jz));
                    placed++;
                }
            }
            Debug.Log($"[Build] Forest: {placed} trees placed, trail kept clear.");
        }

        /// <summary>
        /// Grass clumps across the yard, thickest away from the trail. Breaks up the flat ground
        /// plane, which reads as a bare stage however many trees are standing on it.
        ///
        /// Kept off the trail itself — a worn path through overgrowth is what tells the player where
        /// to walk without a marker.
        /// </summary>
        private static void ScatterGrass(Transform root)
        {
            var model = ArtKit.LoadModel("Environment/Grass/Grass.fbx");
            if (model == null) return;

            var parent = new GameObject("Grass").transform;
            parent.SetParent(root, false);

            // The grass model is a 4MB HD mesh. It is dressing, so it gets a hard budget: a coarse
            // grid and a cap. Hundreds of instances of a high-poly model is what made the exterior
            // unplayable, and no amount of grass is worth the frame rate.
            const int maxClumps = 24;
            var material = ArtKit.MakeTexturedMaterial("Environment/Grass/Grass.png", "M_Grass", alphaClip: true);
            var rng = new System.Random(5150);
            int placed = 0;

            for (float z = -34f; z <= -1f && placed < maxClumps; z += 5f)
            {
                for (float x = -14f; x <= 14f && placed < maxClumps; x += 5f)
                {
                    float jx = x + (float)(rng.NextDouble() - 0.5) * 2.4f;
                    float jz = z + (float)(rng.NextDouble() - 0.5) * 2.4f;

                    // Thins out toward the trail and stops at its edge, so the path stays legible.
                    float fromTrail = Mathf.Abs(jx - TrailCentreX(jz));
                    if (fromTrail < TrailHalfWidth) continue;
                    if ((float)rng.NextDouble() > Mathf.Clamp01(fromTrail / 6f) * 0.6f) continue;

                    var clump = ArtKit.Spawn(model, parent, "Grass");
                    ArtKit.ApplyMaterial(clump, material);
                    ArtKit.StandUpright(clump);
                    ArtKit.FitHeight(clump, 0.4f + (float)rng.NextDouble() * 0.5f);
                    clump.transform.Rotate(0f, (float)rng.NextDouble() * 360f, 0f, Space.World);
                    ArtKit.GroundAt(clump, new Vector3(jx, -0.05f, jz));
                    placed++;
                }
            }
            Debug.Log($"[Build] Grass: {placed} clumps placed.");
        }

        // A shallow S-curve from the car (z -15) to the doors (z 0). Sampled by both the tree
        // scatter and the tool spawn points so they can never contradict each other.
        private const float TrailHalfWidth = 2.6f;
        private static float TrailCentreX(float z) => Mathf.Sin((z + 35f) * 0.16f) * 5.5f;

        private static void ScatterDeadTrees(Transform root)
        {
            var model = ArtKit.LoadModel("Environment/DeadTree/scene.gltf");
            if (model == null) return;

            var parent = new GameObject("DeadTrees").transform;
            parent.SetParent(root, false);

            var rng = new System.Random(4417);
            (float x, float z, float height)[] spots =
            {
                (-9.5f, -6f, 7f), (-11f, -17f, 8.5f), (-7.5f, -27f, 6.5f),
                (9.5f, -9f, 7.5f), (11.5f, -21f, 8f), (7f, -31f, 6f),
                (-4.5f, -32f, 7f), (4f, -24f, 6.5f), (12f, -3f, 7f), (-12.5f, -2f, 6.5f),
            };

            foreach (var (x, z, height) in spots)
            {
                var tree = ArtKit.Spawn(model, parent, "DeadTree");
                ArtKit.AutoTexture(tree, "Environment/DeadTree/textures", alphaClip: true);
                ArtKit.FitHeight(tree, height);
                tree.transform.Rotate(0f, (float)rng.NextDouble() * 360f, 0f, Space.World);
                ArtKit.GroundAt(tree, new Vector3(x, 0f, z));
            }
        }

        // A couple of the farm pack's larger structures sit beyond the side walls as silhouettes —
        // same reasoning as the treeline for why they're outside the play area.
        private static void PlaceFarmStructures(Transform root, Transform scratch)
        {
            var model = ArtKit.LoadModel("Environment/Farm/scene.gltf");
            if (model == null) return;

            var pack = ArtKit.Spawn(model, scratch, "FarmPack");
            ArtKit.AutoTexture(pack, "Environment/Farm/textures", alphaClip: true);
            var props = ArtKit.SplitIntoProps(pack, scratch, 1f);
            if (props.Count == 0) return;

            var parent = new GameObject("FarmStructures").transform;
            parent.SetParent(root, false);

            Vector3[] spots = { new Vector3(-23f, 0f, -9f), new Vector3(22f, 0f, -27f) };
            float[] yaws = { 70f, -55f };
            for (int i = 0; i < spots.Length && i < props.Count; i++)
            {
                var structure = props[i];
                structure.transform.SetParent(parent, true);
                structure.name = $"FarmStructure_{i}";
                ArtKit.FitLongest(structure, 11f);
                structure.transform.Rotate(0f, yaws[i], 0f, Space.World);
                ArtKit.GroundAt(structure, spots[i]);
            }
        }

        // Car_Chassis/Car_Cabin stay as the physical blockers; only their Renderers go dark.
        private static void SwapCarVisual(Transform root)
        {
            var chassis = GameObject.Find("Car_Chassis");
            if (chassis == null)
            {
                Debug.LogWarning("[ArtPass] 'Car_Chassis' not found — car swap skipped.");
                return;
            }
            if (chassis.TryGetComponent<Renderer>(out var chassisRenderer)) chassisRenderer.enabled = false;
            var cabin = GameObject.Find("Car_Cabin");
            if (cabin != null && cabin.TryGetComponent<Renderer>(out var cabinRenderer)) cabinRenderer.enabled = false;

            var model = ArtKit.LoadModel("Props/Vehicles/RetroCar/scene.gltf");
            if (model == null) return;

            var car = ArtKit.Spawn(model, root, "Car_Visual");
            ArtKit.AutoTexture(car, "Props/Vehicles/RetroCar/textures");
            ArtKit.FitLongest(car, 4.3f);
            // The greybox it replaces runs lengthways along Z. If the model came in with its long
            // axis on X, turn it a quarter so it sits along the approach rather than across it.
            if (ArtKit.TryGetBounds(car, out var cb) && cb.size.x > cb.size.z)
                car.transform.Rotate(0f, 90f, 0f, Space.World);
            ArtKit.GroundAt(car, new Vector3(0f, 0f, -15f));
        }

        private static void SwapEntityVisual()
        {
            var entity = GameObject.Find("Entity");
            if (entity == null)
            {
                Debug.LogWarning("[ArtPass] 'Entity' not found — the art pass must run after the Entity is created.");
                return;
            }

            var stale = entity.transform.Find("Visual");
            if (stale != null) UnityObject.DestroyImmediate(stale.gameObject);

            // The old Character_Monster.fbx was replaced: of the three candidate packs, only this
            // one ("deadman", Sketchfab) ships both a skin and a baked Mixamo clip — the others are
            // rigged but carry no animation at all, so they could only ever T-pose. Its 4K maps are
            // capped first; it's seen in the dark, at distance.
            ArtKit.CapTextureSize("Characters/Entity/textures", 512);
            // No-ops for glTF (gltFast uses its own importer, not ModelImporter) but harmless, and
            // keeps the path correct if the entity is ever swapped back to an FBX.
            ArtKit.PrepareAnimatedModel(EntityModelPath);

            var model = ArtKit.LoadModel(EntityModelPath);
            if (model == null) return;

            // The capsule keeps its transform, NavMeshAgent, NetworkTransform and EntityController
            // exactly as they were — only its Renderer goes dark, with the model riding as a child.
            // Its pivot sits at its CENTRE rather than its feet, so the floor is the bottom of its
            // bounds, measured here before the renderer is switched off. Grounding the model on the
            // pivot instead is what left it hovering a metre off the floor.
            // The capsule is NOT hidden yet. It used to be switched off here, before the model was
            // even spawned — so any failure after this point (missing clip, bad avatar, collapsed
            // skinning) left the Entity with nothing rendering at all. An invisible Entity that can
            // still chase and kill is the single worst failure this scene can ship, so the fallback
            // stays visible until a real, non-degenerate skinned mesh is confirmed below.
            float floorY = entity.transform.position.y;
            entity.TryGetComponent<Renderer>(out var capsule);
            if (capsule != null) floorY = capsule.bounds.min.y;

            var visual = ArtKit.Spawn(model, entity.transform, "Visual");
            // The capsule is scaled (0.9, 1.15, 0.9); inherited unchanged that would squash the
            // model horizontally and stretch it vertically.
            ArtKit.NeutralizeParentScale(visual);
            ArtKit.FitHeight(visual, 1.95f);
            ArtKit.GroundAt(visual, new Vector3(entity.transform.position.x, floorY, entity.transform.position.z));
            // Base colour only — AutoTexture skips normal/metallic/specular maps, which would
            // otherwise win the name match and paint the model with a greyscale mask.
            ArtKit.AutoTexture(visual, "Characters/Entity/textures", alphaClip: false, pointFilter: false);
            // Without an Animator (or Animation, for a legacy clip) driving it, the model just
            // stands in its bind pose — the T-shape.
            // Full state machine from the four hand-authored Watcher clips (idle / patrol / run /
            // catch), retargeted onto the mesh model's avatar. Replaces the old single sped-up walk.
            ArtKit.SetupEntityAnimator(visual, EntityModelPath, "AC_Entity",
                "Idle", "Patrol", "Run", "Catch");

            // Ties playback rate to real travel speed, so the feet stop sliding.
            var driver = entity.GetComponent<LastWard.Entity.EntityAnimationDriver>();
            if (driver == null) driver = entity.AddComponent<LastWard.Entity.EntityAnimationDriver>();
            EditorBuildKit.SetRef(driver, "animator", visual.GetComponentInChildren<Animator>());

            // Only NOW is the placeholder capsule retired, and only if the swapped-in model is
            // actually drawable: at least one enabled renderer with real volume. A collapsed or
            // zero-scale skinned mesh counts as a failure and keeps the capsule, so the Entity is
            // always something you can see coming.
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(entity.transform.position, Vector3.zero);
            bool drawable = false;
            foreach (var r in renderers)
            {
                if (!r.enabled) continue;
                if (!drawable) { bounds = r.bounds; drawable = true; }
                else bounds.Encapsulate(r.bounds);
            }
            drawable &= bounds.size.y > 0.3f && bounds.size.x > 0.05f;

            if (drawable)
            {
                if (capsule != null) capsule.enabled = false;
                Debug.Log($"[ArtPass] Entity visual OK — {renderers.Length} renderer(s), bounds size {bounds.size}. Capsule hidden.");
            }
            else
            {
                Debug.LogError("[ArtPass] Entity model did NOT produce a drawable mesh " +
                    $"({renderers.Length} renderer(s), bounds size {bounds.size}) — keeping the placeholder " +
                    "capsule visible so the Entity is never invisible. Check the Watcher clips/avatar.");
            }
        }

        // --- interior set dressing ---
        // Props hug the walls and deliberately avoid every interactive position: notes, fuse
        // pickups, the breaker box, the keypad, intercom stations, doorways, patrol waypoints and
        // aftermath anchors. The Ward gets the lightest touch of all — it's dense with patient-file
        // spawn points that ClueSpawnShuffler moves around at runtime.
        private static void DressInteriors(Transform root, Transform scratch)
        {
            var parent = new GameObject("InteriorProps").transform;
            parent.SetParent(root, false);

            DressLobby(parent, scratch);
            DressWard(parent, scratch);
            DressCorridorAndExit(parent, scratch);
        }

        private static void DressLobby(Transform parent, Transform scratch)
        {
            var furnitureModel = ArtKit.LoadModel("Props/Furniture/furniture_without_scene.fbx");
            if (furnitureModel != null)
            {
                var pack = ArtKit.Spawn(furnitureModel, scratch, "FurniturePack");
                // Slots into the wall runs reserved in CreateSearchableStashes — the containers own
                // z 7.2/7.4/9.3/9.4 and the wardrobes own 4.6, so furniture takes what's left.
                const string tex = "Props/Furniture/textures/";
                PlaceFromPack(pack, parent, "Closet", tex + "closet.png", "M_Closet", 2f,
                    new Vector3(-4.5f, 0f, 1.4f), 90f, "Closet");
                PlaceFromPack(pack, parent, "Sofa", tex + "sofa_plating.png", "M_Sofa", 0.85f,
                    new Vector3(4.5f, 0f, 1.8f), -90f, "Sofa");
                var table = PlaceFromPack(pack, parent, "Table", tex + "table.png", "M_Table", 0.75f,
                    new Vector3(4.4f, 0f, 4.6f), 0f, "Table");
                PlaceFromPack(pack, parent, "Chair_A", tex + "chair.png", "M_Chair", 0.95f,
                    new Vector3(2.9f, 0f, 5.6f), 200f, "Chair_1");

                // A radio on the table, a candle beside it — both measured off the table's real top
                // face rather than a guessed height.
                PlaceOnTop(PlaceHorrorKitItem(parent, scratch, "Radio", 0.3f, "RadioBake-V2.0_1024.png",
                    "M_Radio", "Radio"), table);
                // The "Candle & Flame" model has no animation in it despite the name (zero curves),
                // so the flame is a small flickering light rather than a moving mesh — which reads
                // better anyway, since it lights the table.
                var candle = PlaceSingleModel(parent, "Props/CandleFlame/CandleFlame.fbx", "Candle", 0.22f,
                    "Props/CandleFlame/Candle.png", "M_CandleFlame");
                PlaceOnTop(candle, table);
                if (candle != null)
                {
                    candle.transform.position += new Vector3(0.32f, 0f, 0.18f);
                    // A bare light, NOT AddFlickeringTube — that helper builds a ceiling fixture
                    // housing, which is the floating slab that appeared over the candle. A candle
                    // flame also has to be dim and short-range or it lights the whole room.
                    AddFlameLight(candle.transform.position + new Vector3(0f, 0.19f, 0f),
                        new Color(1f, 0.62f, 0.28f), 0.11f, 2.2f);
                }
            }

            // The PS1 closet, in the gap between the wardrobe (z 4.6) and the cupboard (z 7.4) on
            // the west run. Decoration — the openable containers are the ones with StorageContainer.
            var closet = PlaceProp(parent, "Props/Closet/scene.gltf", null, null,
                "Closet_PS1", 2f, new Vector3(-2.2f, 0f, 6.1f), 25f);
            if (closet != null) ArtKit.AutoTexture(closet, "Props/Closet/textures");

            // No loose flashlight on the floor here — the torch is carried, and a second one lying
            // around just reads as a prop the player can't pick up.
        }

        // Deliberately sparse. The Ward's furniture is the file cabinets and beds built by
        // CreateRecordCodePuzzleInWard (x ±4.5 at z 12.4/15.4/19.4, beds at ±3.5) — adding bedside
        // tables on the same walls is what had props growing through each other.
        private static void DressWard(Transform parent, Transform scratch)
        {
            // One hospital bedside table, in the gap the cabinets leave on the east wall.
            var stand = PlaceSingleModel(parent, "Props/BedsideTable/SM_BedsideTable.fbx", "WardTable_E", 0.62f,
                "Props/BedsideTable/textures/T_BedsideTableHospital_BaseColor.png", "M_BedsideTable_Hospital");
            if (stand != null)
            {
                stand.transform.Rotate(0f, -90f, 0f, Space.World);
                ArtKit.GroundAt(stand, new Vector3(4.6f, 0f, 18.2f));
                PlaceOnTop(CreatePillBottle(parent, "Pills_E"), stand);
            }

            // Lock and key on the floor — flavour, not the record-nook puzzle. Between the west
            // cabinet (z 12.4) and the west bed (z 16.8).
            var lockProp = PlaceHorrorKitItem(parent, scratch, "LockAndKey", 0.2f,
                "KeyBake-V2.0_1024.png", "M_LockAndKey", "Lock_", "Key");
            if (lockProp != null) ArtKit.GroundAt(lockProp, new Vector3(-4.7f, 0f, 14.6f));

            // Gurneys are what makes the Ward read as a hospital rather than a room with cabinets.
            // Placed down the centre line, clear of the wall runs the cabinets and beds occupy.
            PlaceProp(parent, "Props/Gurney/Gurney.fbx", "Props/Gurney/Gurney.png", "M_Gurney",
                "Gurney_A", 1.05f, new Vector3(-1.5f, 0f, 13.6f), 8f);
            PlaceProp(parent, "Props/Gurney/Gurney.fbx", "Props/Gurney/Gurney.png", "M_Gurney",
                "Gurney_B", 1.05f, new Vector3(1.7f, 0f, 17.2f), 168f);
        }

        /// <summary>Textured, size-normalised, grounded prop in one call.</summary>
        private static GameObject PlaceProp(Transform parent, string modelRelPath, string texRelPath,
            string materialName, string name, float targetHeight, Vector3 spot, float yaw)
        {
            var prop = PlaceSingleModel(parent, modelRelPath, name, targetHeight, texRelPath, materialName);
            if (prop == null) return null;
            prop.transform.Rotate(0f, yaw, 0f, Space.World);
            ArtKit.GroundAt(prop, spot);
            // Solid AFTER it is rotated and grounded, so the box is measured from where it actually
            // ends up rather than from the import pose.
            ArtKit.MakeSolid(prop);
            return prop;
        }

        private static void DressCorridorAndExit(Transform parent, Transform scratch)
        {
            var model = ArtKit.LoadModel("Props/CratesAndBarrels/scene.gltf");
            if (model == null) return;

            var pack = ArtKit.Spawn(model, scratch, "CratesPack");
            ArtKit.AutoTexture(pack, "Props/CratesAndBarrels/textures");
            var props = ArtKit.SplitIntoProps(pack, scratch, 0.2f);
            if (props.Count == 0) return;

            // Against the outer walls, clear of the centre pillar, both patrol lanes and the
            // aftermath anchors at (±2.5, 27/35).
            (Vector3 spot, float yaw)[] placements =
            {
                (new Vector3(-3.6f, 0f, 22.6f), 15f),
                (new Vector3(3.6f, 0f, 25.4f), -30f),
                (new Vector3(3.6f, 0f, 33.2f), 60f),
                (new Vector3(-3.6f, 0f, 41f), -10f),
                (new Vector3(-5.3f, 0f, 30.2f), 25f),   // dead-end alcove
                (new Vector3(-5.9f, 0f, 31.5f), -40f),  // dead-end alcove
                (new Vector3(-3.6f, 0f, 49.5f), 20f),   // Exit Route
                (new Vector3(3.6f, 0f, 51.2f), -25f),   // Exit Route
            };

            for (int i = 0; i < placements.Length; i++)
            {
                var template = props[i % props.Count];
                var crate = (GameObject)UnityObject.Instantiate(template, parent);
                crate.name = $"Crate_{i}";
                ArtKit.FitHeight(crate, 0.9f);
                crate.transform.Rotate(0f, placements[i].yaw, 0f, Space.World);
                ArtKit.GroundAt(crate, placements[i].spot);
            }
        }

        // --- placement helpers ---

        private static GameObject PlaceFromPack(GameObject pack, Transform parent, string name,
            string texRelPath, string materialName, float targetHeight, Vector3 spot, float yaw,
            params string[] namePrefixes)
        {
            var prop = ArtKit.ExtractProp(pack, name, parent, namePrefixes);
            if (prop == null) return null;
            ArtKit.ApplyMaterial(prop, ArtKit.MakeTexturedMaterial(texRelPath, materialName));
            ArtKit.FitHeight(prop, targetHeight);
            prop.transform.Rotate(0f, yaw, 0f, Space.World);
            ArtKit.GroundAt(prop, spot);
            // Pack-sourced furniture (closets, beds, chairs) needs to be as solid as anything else.
            ArtKit.MakeSolid(prop);
            return prop;
        }

        private static void CreatePipePickup(Vector3 position)
        {
            var model = ArtKit.LoadModel("Props/Pipe/Pipe.fbx");
            var pipe = new GameObject("Pickup_pipe");
            pipe.transform.position = position;

            if (model != null)
            {
                var instance = ArtKit.Spawn(model, pipe.transform, "Model");
                ArtKit.ApplyMaterial(instance, ArtKit.MakeTexturedMaterial("Props/Pipe/Pipe.png", "M_Pipe"));
                ArtKit.FitLongest(instance, 0.85f);
                // Lying on the floor rather than standing on end.
                instance.transform.localRotation = Quaternion.Euler(0f, 24f, 90f);
                ArtKit.GroundAt(instance, position);
            }

            var trigger = pipe.AddComponent<BoxCollider>();
            trigger.size = new Vector3(0.35f, 0.35f, 0.95f);

            pipe.AddComponent<LastWard.Core.ProximityGlow>();
            pipe.AddComponent<Unity.Netcode.NetworkObject>();
            var pickup = pipe.AddComponent<LastWard.Net.NetworkedPickup>();
            EditorBuildKit.SetString(pickup, "itemId", "pipe");
            EditorBuildKit.SetString(pickup, "displayName", "metal pipe");
        }

        // The pack's pill bottle model is a 134MB high-poly sculpt — absurd for an 11cm prop sitting
        // on a bedside table in the dark, and a genuine contributor to the load stall. The mesh was
        // dropped; a primitive wearing the pack's own texture is indistinguishable at this size.
        private static GameObject CreatePillBottle(Transform parent, string name)
        {
            var bottle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bottle.name = name;
            bottle.transform.SetParent(parent);
            // Unity's cylinder primitive is 2 units tall, so half-height gives the real size.
            bottle.transform.localScale = new Vector3(0.05f, 0.055f, 0.05f);
            UnityObject.DestroyImmediate(bottle.GetComponent<Collider>());
            ArtKit.ApplyMaterial(bottle, ArtKit.MakeTexturedMaterial(
                "Props/PillBottle/PillBottle.png", "M_PillBottle"));
            return bottle;
        }

        private static GameObject PlaceSingleModel(Transform parent, string modelRelPath, string name,
            float targetHeight, string texRelPath, string materialName)
        {
            var model = ArtKit.LoadModel(modelRelPath);
            if (model == null) return null;
            var go = ArtKit.Spawn(model, parent, name);
            // Null texture means "keep the importer's own materials" — the mirror and closet are
            // textured by AutoTexture afterwards, and asking for an empty path only produced a
            // spurious "Texture not found" warning.
            if (!string.IsNullOrEmpty(texRelPath))
                ArtKit.ApplyMaterial(go, ArtKit.MakeTexturedMaterial(texRelPath, materialName));
            ArtKit.FitHeight(go, targetHeight);
            return go;
        }

        // The horror kit ships all four items in one FBX, so each is cut out by name.
        private static GameObject PlaceHorrorKitItem(Transform parent, Transform scratch, string name,
            float targetHeight, string texFile, string materialName, params string[] namePrefixes)
        {
            var model = ArtKit.LoadModel("Props/HorrorKitV2/HorrorKitV2.fbx");
            if (model == null) return null;
            var pack = ArtKit.Spawn(model, scratch, $"HorrorKitPack_{name}");
            var item = ArtKit.ExtractProp(pack, name, parent, namePrefixes);
            if (item == null) return null;
            ArtKit.ApplyMaterial(item, ArtKit.MakeTexturedMaterial(
                "Props/HorrorKitV2/textures/" + texFile, materialName));
            ArtKit.FitHeight(item, targetHeight);
            return item;
        }

        /// <summary>Stands an item on the measured top face of another prop.</summary>
        private static void PlaceOnTop(GameObject item, GameObject surface)
        {
            if (item == null || surface == null) return;
            if (!ArtKit.TryGetBounds(surface, out var sb)) return;
            ArtKit.GroundAt(item, new Vector3(sb.center.x, sb.max.y, sb.center.z));
        }
    }
}
#endif
