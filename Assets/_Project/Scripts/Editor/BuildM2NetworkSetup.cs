#if UNITY_EDITOR
using LastWard.Puzzles;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace LastWard.EditorTools
{
    /// <summary>
    /// One-shot dev tool: a single-room systems sandbox (co-op, Entity, knowledge, all three
    /// puzzles) used to smoke-test mechanics in isolation, distinct from the real level being built
    /// zone-by-zone in BuildM5Level. Shared infrastructure (player prefab, NetworkManager, UI, doors,
    /// the fuse puzzle, the Entity) lives in EditorBuildKit; this file only has this room's specific
    /// layout (P2/P3 stay here for now — they haven't been relocated into the real level yet).
    /// Requires the project linked to UGS with Relay enabled before Host/Join will work.
    /// Safe to re-run; overwrites prefab and scene.
    /// </summary>
    public static class BuildM2NetworkSetup
    {
        private const string ScenePath = "Assets/_Project/Scenes/M2_TestScene.unity";

        [MenuItem("The Last Ward/Build M2 Network Setup")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Exit Play Mode before building the network setup.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("Build M2 Network Setup cancelled.");
                return;
            }

            EditorBuildKit.EnsureProjectSettings();
            var playerPrefab = EditorBuildKit.BuildPlayerPrefab();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateRoom();
            EditorBuildKit.CreateBootstrapCamera(new Vector3(0f, 1.6f, -5f));
            EditorBuildKit.CreateNetworkManager(playerPrefab);
            EditorBuildKit.CreateSessionManager();
            EditorBuildKit.CreateKnowledgeService();
            var aftermathTemplates = EditorBuildKit.CreateAftermathTemplates();
            EditorBuildKit.CreateAftermathManager(aftermathTemplates);
            EditorBuildKit.CreateAftermathAnchor(new Vector3(-3f, 0f, 8f));
            EditorBuildKit.CreateAftermathAnchor(new Vector3(3f, 0f, -3f));
            EditorBuildKit.CreateConnectionUI();

            EditorBuildKit.CreateFusePuzzle(
                "NetworkedDoor_P1", new Vector3(3.5f, 0f, 2f),
                new Vector3(5.5f, 1f, 2f),
                new Vector3(2f, 0.3f, -2f), new Vector3(-2f, 0.3f, -2f),
                new Vector3(-4f, 1.15f, -2f));
            CreateNetworkedInteractables();
            CreateRecordCodePuzzle();
            CreateIntercomPuzzle();

            var waypoints = CreatePatrolWaypoints();
            EditorBuildKit.BakeNavMesh();
            EditorBuildKit.CreateEntity(waypoints, new Vector3(0f, 1f, 5f));

            // Save once so in-scene objects get persistent file IDs, then assign unique
            // GlobalObjectIdHash values (script-created NetworkObjects otherwise all default to 0
            // and collide when NetworkManager registers scene objects), then save again.
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildKit.FixSceneNetworkObjectHashes();
            EditorSceneManager.SaveScene(scene, ScenePath);

            RegisterInBuildSettings();

            Debug.Log("M2 network setup built. Scene: " + ScenePath +
                ". NEXT: link the project to UGS (Project Settings > Services) and enable Relay, then " +
                "make a build + open the scene in the editor to test Host (one) / Join by code (other).");
        }

        private static void RegisterInBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            const string m1 = "Assets/_Project/Scenes/M1_TestScene.unity";
            const string m5 = "Assets/_Project/Scenes/M5_Level.unity";
            if (System.IO.File.Exists(m1)) scenes.Add(new EditorBuildSettingsScene(m1, true));
            if (System.IO.File.Exists(m5)) scenes.Add(new EditorBuildSettingsScene(m5, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void CreateRoom()
        {
            const float roomSize = 14f;
            const float wallHeight = 3f;
            const float t = 0.3f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.06f, 0.06f, 0.07f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.02f, 0.02f, 0.03f);
            RenderSettings.fogDensity = 0.03f;
            RenderSettings.skybox = null;

            var mat = EditorBuildKit.MakeMaterial(new Color(0.35f, 0.35f, 0.37f));
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Floor", new Vector3(0f, -0.1f, 0f), new Vector3(roomSize, 0.2f, roomSize)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Ceiling", new Vector3(0f, wallHeight, 0f), new Vector3(roomSize, 0.2f, roomSize)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Wall_N", new Vector3(0f, wallHeight / 2f, roomSize / 2f), new Vector3(roomSize + t, wallHeight, t)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Wall_S", new Vector3(0f, wallHeight / 2f, -roomSize / 2f), new Vector3(roomSize + t, wallHeight, t)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Wall_E", new Vector3(roomSize / 2f, wallHeight / 2f, 0f), new Vector3(t, wallHeight, roomSize + t)), mat);
            EditorBuildKit.SetMaterial(EditorBuildKit.CreateBox("Wall_W", new Vector3(-roomSize / 2f, wallHeight / 2f, 0f), new Vector3(t, wallHeight, roomSize + t)), mat);

            var fill = new GameObject("FillLight").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 12f;
            fill.intensity = 0.2f;
            fill.transform.position = new Vector3(0f, wallHeight - 0.3f, 0f);
        }

        private static void CreateNetworkedInteractables()
        {
            // Battery on a pedestal — networked so it vanishes for everyone when taken.
            EditorBuildKit.CreateBox("Pedestal_Battery", new Vector3(2f, 0.4f, 3f), new Vector3(0.5f, 0.8f, 0.5f));
            var battery = EditorBuildKit.CreateBox("Pickup_Battery", new Vector3(2f, 0.95f, 3f), new Vector3(0.18f, 0.35f, 0.18f));
            EditorBuildKit.SetMaterial(battery, EditorBuildKit.MakeEmissive(new Color(0.1f, 0.5f, 0.15f), new Color(0.1f, 0.9f, 0.25f)));
            battery.AddComponent<NetworkObject>();
            var pickup = battery.AddComponent<LastWard.Net.NetworkedPickup>();
            EditorBuildKit.SetString(pickup, "itemId", "battery");
            EditorBuildKit.SetString(pickup, "displayName", "battery");

            EditorBuildKit.CreateBox("Stand_Note", new Vector3(-2f, 0.5f, 3f), new Vector3(0.5f, 1f, 0.5f));
            EditorBuildKit.CreateNoteProp("Note_NursesNote", new Vector3(-2f, 1.15f, 3f),
                "Assets/_Project/Data/TestClue.asset", "test_nurse_note", "Nurse's Note",
                "\"Ward B fed at six. Ward B fed at six. Ward B fed at six.\"\n\n" +
                "The same line, written maybe two hundred times, each one a little less steady than the last.", 2f);
        }

        private static Transform[] CreatePatrolWaypoints()
        {
            var parent = new GameObject("PatrolWaypoints").transform;
            Vector3[] positions =
            {
                new Vector3(-5f, 0f, -5f), new Vector3(5f, 0f, -5f),
                new Vector3(5f, 0f, 5f), new Vector3(-5f, 0f, 5f)
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

        // P2 from the plan: keypad code from 3 valid patient files (a 4th is a deliberate
        // contradiction the criterion note lets you exclude). Files shuffle position via
        // ClueSpawnShuffler each rebuild.
        private static void CreateRecordCodePuzzle()
        {
            var door = EditorBuildKit.CreateNetworkedDoor("NetworkedDoor_P2", new Vector3(-3.5f, 0f, 6f));

            var keypadGO = EditorBuildKit.CreateBox("Keypad", new Vector3(-3.5f, 1f, 5.6f), new Vector3(0.3f, 0.3f, 0.1f));
            EditorBuildKit.SetMaterial(keypadGO, EditorBuildKit.MakeEmissive(new Color(0.1f, 0.1f, 0.15f), new Color(0.2f, 0.5f, 0.6f)));
            keypadGO.AddComponent<NetworkObject>();
            var puzzle = keypadGO.AddComponent<RecordCodePuzzle>();
            EditorBuildKit.SetRef(puzzle, "gatedDoor", door);
            var keypad = keypadGO.AddComponent<KeypadInteractable>();
            EditorBuildKit.SetRef(keypad, "puzzle", puzzle);

            EditorBuildKit.CreateNoteProp("Note_CriterionMemo", new Vector3(-6f, 1f, -4f),
                "Assets/_Project/Data/CriterionMemoClue.asset", "p2_criterion_memo", "Admission Policy Memo",
                "Fire took the east wing in March 1974. Anyone processed after that point wasn't real intake -- paperwork only, backdated to cover the gap.\n\nCount only the ones admitted before. List their rooms, oldest to newest.",
                2f);

            var file1 = EditorBuildKit.CreateNoteProp("Note_File1", new Vector3(6f, 1f, -4f),
                "Assets/_Project/Data/PatientFile1Clue.asset", "p2_file_room4", "Patient Intake -- Room 4",
                "Admitted January 12, 1974. Quiet. Doesn't speak much.", 2f);
            var file2 = EditorBuildKit.CreateNoteProp("Note_File2", new Vector3(6f, 1f, -0.5f),
                "Assets/_Project/Data/PatientFile2Clue.asset", "p2_file_room8", "Patient Intake -- Room 8",
                "Admitted February 3, 1974. Transferred from the county home.", 2f);
            var file3 = EditorBuildKit.CreateNoteProp("Note_File3", new Vector3(6f, 1f, 4f),
                "Assets/_Project/Data/PatientFile3Clue.asset", "p2_file_room2", "Patient Intake -- Room 2",
                "Admitted February 20, 1974. No family listed.", 2f);
            // The decoy: dated after the March 1974 fire per the criterion memo, so it should be
            // excluded from the code -- the actual "figure it out" moment of the puzzle.
            var file4 = EditorBuildKit.CreateNoteProp("Note_File4", new Vector3(-6f, 1f, 4f),
                "Assets/_Project/Data/PatientFile4Clue.asset", "p2_file_room9", "Patient Intake -- Room 9",
                "Admitted April 5, 1974. [Note: ward reopened under new administration.]", 2f);

            var shufflerGO = new GameObject("ClueSpawnShuffler_P2");
            shufflerGO.AddComponent<NetworkObject>();
            var shuffler = shufflerGO.AddComponent<ClueSpawnShuffler>();
            EditorBuildKit.SetRefArray(shuffler, "clues", new UnityObject[] { file1, file2, file3, file4 });
            EditorBuildKit.SetRefArray(shuffler, "candidatePoints", new UnityObject[]
            {
                EditorBuildKit.MakePoint(new Vector3(6f, 1f, -4f)), EditorBuildKit.MakePoint(new Vector3(6f, 1f, -0.5f)),
                EditorBuildKit.MakePoint(new Vector3(6f, 1f, 4f)), EditorBuildKit.MakePoint(new Vector3(-6f, 1f, 4f)),
                EditorBuildKit.MakePoint(new Vector3(-6f, 1f, 0.5f)), EditorBuildKit.MakePoint(new Vector3(3f, 1f, -6.2f)),
            });
        }

        // P3 from the plan: 3 stations activated in the order a "radio log" note gives, favoring
        // one player relaying the order to others running stations.
        private static void CreateIntercomPuzzle()
        {
            EditorBuildKit.CreateIntercomPuzzle(
                "NetworkedDoor_P3", new Vector3(3.5f, 0f, 6f),
                new Vector3(0f, 1f, 0f),
                new (string, Vector3)[]
                {
                    ("Ward A", new Vector3(-5.5f, 1f, -3f)),
                    ("Reception", new Vector3(5.5f, 1f, -3f)),
                    ("Ward C", new Vector3(0f, 1f, -6.2f)),
                },
                new Vector3(0f, 1f, 0.6f));
        }
    }
}
#endif
