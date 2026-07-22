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
            CreateZoneThreshold();

            EditorBuildKit.CreateBootstrapCamera(new Vector3(0f, 1.6f, -11f));
            EditorBuildKit.CreateNetworkManager(playerPrefab);
            var sessionManager = EditorBuildKit.CreateSessionManager();
            // NGO otherwise spawns every player at the prefab's saved world origin, which in this
            // layout is right on the Lobby doorway threshold, not "in the Exterior near the car".
            EditorBuildKit.SetVector3(sessionManager, "spawnPosition", new Vector3(0f, 1f, -13f));
            EditorBuildKit.CreateKnowledgeService();
            CreateObjectiveTracker();
            var aftermathTemplates = EditorBuildKit.CreateAftermathTemplates();
            EditorBuildKit.CreateAftermathManager(aftermathTemplates);
            CreateAftermathAnchors();
            EditorBuildKit.CreateConnectionUI();
            CreateObjectiveUI();
            CreateDiscoveryUI();
            CreateHidingSpots();

            // Gates the actual passage into the Ward — solving it is what the objective HUD's
            // "Restore power." -> "Push further into the ward." transition represents.
            EditorBuildKit.CreateFusePuzzle(
                "Door_ToWard", new Vector3(0f, 0f, 10f),
                new Vector3(1.5f, 1f, 8f),
                new Vector3(-3f, 0.3f, 3f), new Vector3(3f, 0.3f, 3f),
                new Vector3(-3f, 1.15f, 8f),
                out var fusePickups);
            ShuffleFuseSpawns(fusePickups);

            CreateRecordCodePuzzleInWard();
            CreateIntercomPuzzleInExit();
            CreateFinalExitTrigger();
            CreateExitChoiceZone();

            var waypoints = CreatePatrolWaypoints();
            EditorBuildKit.BakeNavMesh();
            // Spawns deep in the Service Corridor rather than in the Lobby the players walk into.
            // Starting it on top of the first puzzle is what made every run open the same way.
            EditorBuildKit.CreateEntity(waypoints, new Vector3(0f, 1f, 40f));

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
            RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.06f);
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

            EditorBuildKit.CreateNoteProp("Note_Tutorial", new Vector3(1.5f, 1f, -15f),
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

            points.Add(EditorBuildKit.CreateStorageContainer("Cupboard_Exterior",
                new Vector3(-7.5f, 0f, -6.5f), 25f, new Vector3(0.95f, 1.15f, 0.55f),
                requiredItemId: "", consumesItem: false, openPrompt: "Open the cabinet"));

            // Under the Lobby bed — no door, just out of sight unless you crouch and look.
            EditorBuildKit.CreateUnderBedHidingSpot("Hide_Bed_Lobby",
                new Vector3(2.5f, 0f, 2.8f), "Hide under the bed", 90f);
            points.Add(EditorBuildKit.MakePoint(new Vector3(2.5f, 0.12f, 2.8f)));

            // Both tools live in the Exterior, deliberately. The fuse shuffle can drop both fuses
            // into locked containers, so any tool placed past the fuse door would be locked behind
            // the very puzzle it's needed to solve — an unwinnable run. Everything required to open
            // the Lobby's stashes is reachable before the player ever enters the building.
            // The kit has a real key mesh; it has no crowbar, so that one stays a shaped block.
            EditorBuildKit.CreateToolPickup("key", "rusted key", new Vector3(1.4f, 0.9f, -14.2f),
                new Color(0.85f, 0.72f, 0.25f), meshNamePrefix: "Key", texFile: "LockAndKey_Tex_1024.png");
            EditorBuildKit.CreateToolPickup("crowbar", "crowbar", new Vector3(-8.4f, 0.35f, -21f),
                new Color(0.75f, 0.3f, 0.2f));

            // Two pipes for the whole level, deep enough in that you've already met the Entity
            // before you find one. One swing each — see PlayerMeleeDefense.
            CreatePipePickup(new Vector3(-3.7f, 0.15f, 22.8f));
            CreatePipePickup(new Vector3(3.3f, 0.15f, 42.2f));

            return points.ToArray();
        }

        // Lobby spans x:[-5,5], z:[0,10]. South wall (z=0) has a 2m doorway gap to the Exterior;
        // north wall (z=10) has a matching gap filled by Door_ToWard (the fuse puzzle's gate).
        private static void CreateLobbyZone()
        {
            const float wallHeight = 3f;
            const float t = 0.3f;

            var mat = EditorBuildKit.MakeMaterial(new Color(0.35f, 0.35f, 0.37f));
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Lobby_Floor", new Vector3(0f, -0.1f, 5f), new Vector3(10f, 0.2f, 10f)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Lobby_Ceiling", new Vector3(0f, wallHeight, 5f), new Vector3(10f, 0.2f, 10f)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Lobby_Wall_E", new Vector3(5f, wallHeight / 2f, 5f), new Vector3(t, wallHeight, 10f + t)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Lobby_Wall_W", new Vector3(-5f, wallHeight / 2f, 5f), new Vector3(t, wallHeight, 10f + t)), mat);
            // South and north walls in two segments each, leaving x:[-1,1] open as doorways.
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Lobby_Wall_S_Left", new Vector3(-3f, wallHeight / 2f, 0f), new Vector3(4f, wallHeight, t)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Lobby_Wall_S_Right", new Vector3(3f, wallHeight / 2f, 0f), new Vector3(4f, wallHeight, t)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Lobby_Wall_N_Left", new Vector3(-3f, wallHeight / 2f, 10f), new Vector3(4f, wallHeight, t)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Lobby_Wall_N_Right", new Vector3(3f, wallHeight / 2f, 10f), new Vector3(4f, wallHeight, t)), mat);

            var fill = new GameObject("Lobby_FillLight").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 10f;
            fill.intensity = 0.2f;
            fill.transform.position = new Vector3(0f, wallHeight - 0.3f, 5f);
        }

        // Ward spans x:[-5,5], z:[10,20] — same footprint as Lobby, connected through Door_ToWard.
        // North wall (z=20) has a 3m gap into the Corridor (no door — the Ward is meant to feel
        // exposed once you're through it, unlike the puzzle-gated approach into and out of it).
        private static void CreateWardZone()
        {
            const float wallHeight = 3f;
            const float t = 0.3f;

            var mat = EditorBuildKit.MakeMaterial(new Color(0.3f, 0.3f, 0.32f));
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Ward_Floor", new Vector3(0f, -0.1f, 15f), new Vector3(10f, 0.2f, 10f)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Ward_Ceiling", new Vector3(0f, wallHeight, 15f), new Vector3(10f, 0.2f, 10f)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Ward_Wall_E", new Vector3(5f, wallHeight / 2f, 15f), new Vector3(t, wallHeight, 10f + t)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Ward_Wall_W", new Vector3(-5f, wallHeight / 2f, 15f), new Vector3(t, wallHeight, 10f + t)), mat);
            // North wall in two segments, leaving x:[-1.5,1.5] open into the Corridor.
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Ward_Wall_N_Left", new Vector3(-3.25f, wallHeight / 2f, 20f), new Vector3(3.5f, wallHeight, t)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Ward_Wall_N_Right", new Vector3(3.25f, wallHeight / 2f, 20f), new Vector3(3.5f, wallHeight, t)), mat);

            var fill = new GameObject("Ward_FillLight").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 10f;
            fill.intensity = 0.15f; // dimmer than Lobby — "more aggressive Entity behavior" per the plan
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

            var mat = EditorBuildKit.MakeMaterial(new Color(0.22f, 0.22f, 0.24f));
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Corridor_Floor", new Vector3(0f, -0.1f, 32f), new Vector3(8f, 0.2f, 24f)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Corridor_Ceiling", new Vector3(0f, wallHeight, 32f), new Vector3(8f, 0.2f, 24f)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Corridor_Wall_E", new Vector3(4f, wallHeight / 2f, 32f), new Vector3(t, wallHeight, 24f + t)), mat);
            // West wall in two segments, leaving z:[29,32] open so the alcove (west of x=-4) is reachable.
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Corridor_Wall_W_S", new Vector3(-4f, wallHeight / 2f, 24.5f), new Vector3(t, wallHeight, 9f)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Corridor_Wall_W_N", new Vector3(-4f, wallHeight / 2f, 38f), new Vector3(t, wallHeight, 12f)), mat);

            // The loop-creating pillar: leaves ~3.25m paths on either side.
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Corridor_Pillar", new Vector3(0f, wallHeight / 2f, 31f), new Vector3(1.5f, wallHeight, 14f)), mat);

            // Dead-end alcove off the west path.
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Alcove_Floor", new Vector3(-5.25f, -0.1f, 30.5f), new Vector3(2.5f, 0.2f, 3f)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Alcove_Ceiling", new Vector3(-5.25f, wallHeight, 30.5f), new Vector3(2.5f, 0.2f, 3f)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Alcove_Wall_Far", new Vector3(-6.5f, wallHeight / 2f, 30.5f), new Vector3(t, wallHeight, 3f + t)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Alcove_Wall_S", new Vector3(-5.25f, wallHeight / 2f, 29f), new Vector3(2.5f, wallHeight, t)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Alcove_Wall_N", new Vector3(-5.25f, wallHeight / 2f, 32f), new Vector3(2.5f, wallHeight, t)), mat);

            AddDimLight(new Vector3(0f, wallHeight - 0.3f, 24f), 0.1f);
            AddDimLight(new Vector3(0f, wallHeight - 0.3f, 40f), 0.1f);
        }

        // Exit Route spans x:[-4,4], z:[44,54] (matching the Corridor's width, so they meet with no
        // gap). Door_FinalExit (z=54) is gated by the intercom puzzle; beyond it, a short "outside"
        // sliver holds the one-slot exit trigger.
        private static void CreateExitRouteZone()
        {
            const float wallHeight = 3f;
            const float t = 0.3f;

            var mat = EditorBuildKit.MakeMaterial(new Color(0.35f, 0.33f, 0.3f));
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Exit_Floor", new Vector3(0f, -0.1f, 49f), new Vector3(8f, 0.2f, 10f)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Exit_Ceiling", new Vector3(0f, wallHeight, 49f), new Vector3(8f, 0.2f, 10f)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Exit_Wall_E", new Vector3(4f, wallHeight / 2f, 49f), new Vector3(t, wallHeight, 10f + t)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Exit_Wall_W", new Vector3(-4f, wallHeight / 2f, 49f), new Vector3(t, wallHeight, 10f + t)), mat);
            // North wall in two segments, leaving x:[-1.5,1.5] for Door_FinalExit.
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Exit_Wall_N_Left", new Vector3(-2.75f, wallHeight / 2f, 54f), new Vector3(2.5f, wallHeight, t)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Exit_Wall_N_Right", new Vector3(2.75f, wallHeight / 2f, 54f), new Vector3(2.5f, wallHeight, t)), mat);

            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Outside_Floor", new Vector3(0f, -0.1f, 56f), new Vector3(8f, 0.2f, 4f)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Outside_Boundary_Far", new Vector3(0f, wallHeight / 2f, 58f), new Vector3(8f + t, wallHeight, t)), mat);

            AddDimLight(new Vector3(0f, wallHeight - 0.3f, 49f), 0.2f);
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
            EditorBuildKit.CreateIntercomPuzzle(
                "Door_FinalExit", new Vector3(0f, 0f, 54f),
                new Vector3(0f, 1f, 49f),
                new (string, Vector3)[]
                {
                    ("Ward A", new Vector3(-3f, 1f, 46f)),
                    ("Reception", new Vector3(3f, 1f, 46f)),
                    ("Ward C", new Vector3(0f, 1f, 52f)),
                },
                new Vector3(0f, 1f, 45f));
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
            var door = EditorBuildKit.CreateNetworkedDoor("Door_RecordsNook", new Vector3(4f, 0f, 18f));

            var keypadGO = EditorBuildKit.CreateBox("Keypad", new Vector3(4f, 1f, 17.6f), new Vector3(0.3f, 0.3f, 0.1f));
            EditorBuildKit.SetMaterial(keypadGO, EditorBuildKit.MakeEmissive(new Color(0.1f, 0.1f, 0.15f), new Color(0.2f, 0.5f, 0.6f)));
            keypadGO.AddComponent<Unity.Netcode.NetworkObject>();
            var puzzle = keypadGO.AddComponent<LastWard.Puzzles.RecordCodePuzzle>();
            EditorBuildKit.SetRef(puzzle, "gatedDoor", door);
            var keypad = keypadGO.AddComponent<LastWard.Puzzles.KeypadInteractable>();
            EditorBuildKit.SetRef(keypad, "puzzle", puzzle);

            EditorBuildKit.CreateNoteProp("Note_CriterionMemo", new Vector3(-4f, 1f, 11f),
                "Assets/_Project/Data/CriterionMemoClue.asset", "p2_criterion_memo", "Admission Policy Memo",
                "Fire took the east wing in March 1974. Anyone processed after that point wasn't real intake -- paperwork only, backdated to cover the gap.\n\nCount only the ones admitted before. List their rooms, oldest to newest.",
                2f);

            var file1 = EditorBuildKit.CreateNoteProp("Note_File1", new Vector3(4f, 1f, 11f),
                "Assets/_Project/Data/PatientFile1Clue.asset", "p2_file_room4", "Patient Intake -- Room 4",
                "Admitted January 12, 1974. Quiet. Doesn't speak much.", 2f);
            var file2 = EditorBuildKit.CreateNoteProp("Note_File2", new Vector3(-4f, 1f, 15f),
                "Assets/_Project/Data/PatientFile2Clue.asset", "p2_file_room8", "Patient Intake -- Room 8",
                "Admitted February 3, 1974. Transferred from the county home.", 2f);
            var file3 = EditorBuildKit.CreateNoteProp("Note_File3", new Vector3(4f, 1f, 15f),
                "Assets/_Project/Data/PatientFile3Clue.asset", "p2_file_room2", "Patient Intake -- Room 2",
                "Admitted February 20, 1974. No family listed.", 2f);
            // The decoy: dated after the March 1974 fire per the criterion memo, so it should be
            // excluded from the code -- the actual "figure it out" moment of the puzzle.
            var file4 = EditorBuildKit.CreateNoteProp("Note_File4", new Vector3(-4f, 1f, 19f),
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
        private const string EntityModelPath = "Characters/Entity/scene.gltf";

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
            ArtKit.CapTextureSize("Environment/Farm/textures", 1024);

            AddMoonlight(root);
            ScatterTreeline(root, scratch);
            ScatterDeadTrees(root);
            DressCorridorShell(root);
            PlaceFarmStructures(root, scratch);
            SwapCarVisual(root);
            DressInteriors(root, scratch);
            SwapEntityVisual();

            UnityObject.DestroyImmediate(scratch.gameObject);
            ArtKit.FlushAssets();
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
            light.intensity = 0.3f;
            light.shadows = LightShadows.Soft;
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

        // The Service Corridor's greybox gets a real skin: its wall/ceiling/floor renderers go dark
        // and scaled corridor sections are laid end to end along the same run.
        //
        // Their COLLIDERS are deliberately left in place and the corridor sections carry none, so
        // navigation and the already-baked NavMesh are untouched. That keeps this safe, but it also
        // means the visible walls only approximately match what you bump into — this is a first
        // pass, and properly aligning geometry to collision is its own job.
        private static void DressCorridorShell(Transform root)
        {
            string[] models =
            {
                "Environment/Corridor1/scene.gltf",
                "Environment/Corridor2/scene.gltf",
                "Environment/Corridor3/scene.gltf",
            };

            var parent = new GameObject("CorridorShell").transform;
            parent.SetParent(root, false);

            // Hide the greybox surfaces, keep the pillar and alcove — they're gameplay geometry
            // (the chase loop and the ambush dead end), not decoration.
            foreach (var name in new[] { "Corridor_Floor", "Corridor_Ceiling", "Corridor_Wall_E", "Corridor_Wall_W_S", "Corridor_Wall_W_N" })
            {
                var box = GameObject.Find(name);
                if (box != null && box.TryGetComponent<Renderer>(out var renderer)) renderer.enabled = false;
            }

            const float corridorHeight = 3.2f;   // matches the greybox interior
            float z = 20f;
            int index = 0;
            while (z < 44f && index < 12)
            {
                var model = ArtKit.LoadModel(models[index % models.Length]);
                if (model == null) return;

                var section = ArtKit.Spawn(model, parent, $"CorridorSection_{index}");
                string texFolder = models[index % models.Length].Replace("/scene.gltf", "/textures");
                ArtKit.AutoTexture(section, texFolder, alphaClip: false, pointFilter: true);
                ArtKit.FitHeight(section, corridorHeight);

                if (!ArtKit.TryGetBounds(section, out var bounds)) return;
                // Run the section along its own longest horizontal axis.
                float length = Mathf.Max(bounds.size.x, bounds.size.z);
                if (bounds.size.x > bounds.size.z) section.transform.Rotate(0f, 90f, 0f, Space.World);

                ArtKit.GroundAt(section, new Vector3(0f, 0f, z + length * 0.5f));
                Debug.Log($"[ArtPass] Corridor section {index} fitted to height {corridorHeight}m, run length {length:0.0}m at z={z:0.0}");

                z += Mathf.Max(2f, length);
                index++;
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
            ArtKit.CapTextureSize("Characters/Entity/textures", 1024);
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
            float floorY = entity.transform.position.y;
            if (entity.TryGetComponent<Renderer>(out var capsule))
            {
                floorY = capsule.bounds.min.y;
                capsule.enabled = false;
            }

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
            ArtKit.EnsureLoopingAnimator(visual, EntityModelPath, "AC_Entity");

            // Ties playback rate to real travel speed, so the feet stop sliding.
            var driver = entity.GetComponent<LastWard.Entity.EntityAnimationDriver>();
            if (driver == null) driver = entity.AddComponent<LastWard.Entity.EntityAnimationDriver>();
            EditorBuildKit.SetRef(driver, "animator", visual.GetComponentInChildren<Animator>());
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
                PlaceOnTop(PlaceHorrorKitItem(parent, scratch, "Radio", 0.3f, "Radio_Tex_1024.png",
                    "M_Radio", "Radio"), table);
                var candle = PlaceSingleModel(parent, "Props/Candle/Candle.fbx", "Candle", 0.16f,
                    "Props/Candle/Candle.png", "M_Candle");
                PlaceOnTop(candle, table);
                if (candle != null) candle.transform.position += new Vector3(0.32f, 0f, 0.18f);
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
                "LockAndKey_Tex_1024.png", "M_LockAndKey", "Lock_", "Key");
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
            ArtKit.ApplyMaterial(go, ArtKit.MakeTexturedMaterial(texRelPath, materialName));
            ArtKit.FitHeight(go, targetHeight);
            return go;
        }

        // The horror kit ships all four items in one FBX, so each is cut out by name.
        private static GameObject PlaceHorrorKitItem(Transform parent, Transform scratch, string name,
            float targetHeight, string texFile, string materialName, params string[] namePrefixes)
        {
            var model = ArtKit.LoadModel("Props/HorrorKit/PSXHorrorKit_Default.fbx");
            if (model == null) return null;
            var pack = ArtKit.Spawn(model, scratch, $"HorrorKitPack_{name}");
            var item = ArtKit.ExtractProp(pack, name, parent, namePrefixes);
            if (item == null) return null;
            ArtKit.ApplyMaterial(item, ArtKit.MakeTexturedMaterial(
                "Props/HorrorKit/textures/" + texFile, materialName));
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
