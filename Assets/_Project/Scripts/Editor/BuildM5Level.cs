#if UNITY_EDITOR
using LastWard.Core;
using LastWard.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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

            // Gates the actual passage into the Ward — solving it is what the objective HUD's
            // "Restore power." -> "Push further into the ward." transition represents.
            EditorBuildKit.CreateFusePuzzle(
                "Door_ToWard", new Vector3(0f, 0f, 10f),
                new Vector3(1.5f, 1f, 8f),
                new Vector3(-3f, 0.3f, 3f), new Vector3(3f, 0.3f, 3f),
                new Vector3(-3f, 1.15f, 8f));

            CreateRecordCodePuzzleInWard();
            CreateIntercomPuzzleInExit();
            CreateFinalExitTrigger();

            var waypoints = CreatePatrolWaypoints();
            EditorBuildKit.BakeNavMesh();
            EditorBuildKit.CreateEntity(waypoints, new Vector3(0f, 1f, 5f));

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
            EditorBuildKit.SetRefArray(shuffler, "candidatePoints", new UnityEngine.Object[]
            {
                EditorBuildKit.MakePoint(new Vector3(4f, 1f, 11f)), EditorBuildKit.MakePoint(new Vector3(-4f, 1f, 15f)),
                EditorBuildKit.MakePoint(new Vector3(4f, 1f, 15f)), EditorBuildKit.MakePoint(new Vector3(-4f, 1f, 19f)),
                EditorBuildKit.MakePoint(new Vector3(2f, 1f, 13f)), EditorBuildKit.MakePoint(new Vector3(-2f, 1f, 17f)),
            });
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

        private static void CreateObjectiveTracker()
        {
            var go = new GameObject("ObjectiveTracker");
            go.AddComponent<Unity.Netcode.NetworkObject>();
            go.AddComponent<ObjectiveTracker>();
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
    }
}
#endif
