#if UNITY_EDITOR
using LastWard.Player;
using LastWard.Puzzles;
using LastWard.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityObject = UnityEngine.Object;

namespace LastWard.EditorTools
{
    /// <summary>
    /// One-shot dev tool: builds a minimal enclosed room with the full M1 player rig, UI,
    /// and one of each interactable (Door/Pickup/NoteInteractable) so the M1 scripts can be
    /// smoke-tested end-to-end. This is a functional sandbox, not the real Lobby level —
    /// real geometry/art is a later milestone. Safe to re-run; overwrites the saved scene.
    /// </summary>
    public static class BuildM1TestScene
    {
        private const string ScenePath = "Assets/_Project/Scenes/M1_TestScene.unity";
        private const string TestClueAssetPath = "Assets/_Project/Data/TestClue.asset";

        [MenuItem("The Last Ward/Build M1 Test Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Exit Play Mode before building the test scene.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("Build M1 Test Scene cancelled.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateRoom();
            CreatePlayerRig();
            CreateUI();
            CreateInteractables();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            Debug.Log("M1 test scene built at " + ScenePath +
                ". Press Play: WASD move, Shift sprint, Ctrl crouch, mouse look, F flashlight, " +
                "E interact, Escape closes a note. Walk up to the door/pickup/note to test.");
        }

        private static void CreateRoom()
        {
            const float roomSize = 12f;
            const float wallHeight = 3f;
            const float wallThickness = 0.3f;

            CreateBox("Floor", new Vector3(0f, -0.1f, 0f), new Vector3(roomSize, 0.2f, roomSize));
            CreateBox("Ceiling", new Vector3(0f, wallHeight, 0f), new Vector3(roomSize, 0.2f, roomSize));
            CreateBox("Wall_North", new Vector3(0f, wallHeight / 2f, roomSize / 2f), new Vector3(roomSize + wallThickness, wallHeight, wallThickness));
            CreateBox("Wall_South", new Vector3(0f, wallHeight / 2f, -roomSize / 2f), new Vector3(roomSize + wallThickness, wallHeight, wallThickness));
            CreateBox("Wall_East", new Vector3(roomSize / 2f, wallHeight / 2f, 0f), new Vector3(wallThickness, wallHeight, roomSize + wallThickness));
            CreateBox("Wall_West", new Vector3(-roomSize / 2f, wallHeight / 2f, 0f), new Vector3(wallThickness, wallHeight, roomSize + wallThickness));

            // An empty scene's default flat/skybox ambient is what was over-lighting the room.
            // Force ambient near-black + add fog so the space only becomes readable with the
            // flashlight on ("flashlight as primary dynamic light" pillar).
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.06f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.02f, 0.02f, 0.03f);
            RenderSettings.fogDensity = 0.035f;
            RenderSettings.skybox = null;

            var roomMat = MakeMaterial(new Color(0.35f, 0.35f, 0.37f), Color.black);
            foreach (var go in new[] { "Floor", "Ceiling", "Wall_North", "Wall_South", "Wall_East", "Wall_West" })
                SetMaterial(GameObject.Find(go), roomMat);

            // A very faint fill so you're not in total blackout before turning the flashlight on.
            var fillLight = new GameObject("FillLight").AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.range = 8f;
            fillLight.intensity = 0.15f;
            fillLight.transform.position = new Vector3(0f, wallHeight - 0.3f, 0f);
        }

        private static void CreatePlayerRig()
        {
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 1f, -4f);

            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            var inputReader = player.AddComponent<PlayerInputReader>();

            var cameraPivot = new GameObject("CameraPivot");
            cameraPivot.transform.SetParent(player.transform);
            cameraPivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            var cameraGO = new GameObject("PlayerCamera");
            cameraGO.transform.SetParent(cameraPivot.transform);
            cameraGO.transform.localPosition = Vector3.zero;
            var camera = cameraGO.AddComponent<Camera>();
            cameraGO.AddComponent<AudioListener>();

            var flashlightGO = new GameObject("Flashlight");
            flashlightGO.transform.SetParent(cameraGO.transform);
            flashlightGO.transform.localPosition = Vector3.zero;
            var flashlight = flashlightGO.AddComponent<Light>();
            flashlight.type = LightType.Spot;
            flashlight.range = 12f;
            flashlight.spotAngle = 45f;
            flashlight.intensity = 3f;
            flashlight.color = new Color(1f, 0.95f, 0.8f);

            var motor = player.AddComponent<FirstPersonMotor>();
            SetObjectField(motor, "input", inputReader);

            var look = player.AddComponent<FirstPersonLook>();
            SetObjectField(look, "input", inputReader);
            SetObjectField(look, "cameraPivot", cameraPivot.transform);

            var flashlightController = player.AddComponent<FlashlightController>();
            SetObjectField(flashlightController, "input", inputReader);
            SetObjectField(flashlightController, "fallbackLight", flashlight);

            var interactor = player.AddComponent<PlayerInteractor>();
            SetObjectField(interactor, "input", inputReader);
            SetObjectField(interactor, "interactCamera", camera);

            var inventory = player.AddComponent<PlayerInventory>();
            SetObjectField(inventory, "input", inputReader);

            player.AddComponent<PlayerFootsteps>();
        }

        private static void CreateUI()
        {
            var eventSystemGO = new GameObject("EventSystem");
            eventSystemGO.AddComponent<EventSystem>();
            eventSystemGO.AddComponent<InputSystemUIInputModule>();

            var canvasGO = new GameObject("Canvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var promptRoot = new GameObject("InteractionPrompt", typeof(RectTransform));
            promptRoot.transform.SetParent(canvasGO.transform, false);
            var promptRect = (RectTransform)promptRoot.transform;
            promptRect.anchorMin = new Vector2(0.5f, 0.18f);
            promptRect.anchorMax = new Vector2(0.5f, 0.18f);
            promptRect.sizeDelta = new Vector2(500f, 34f);
            var promptText = CreateTMPText(promptRoot.transform, "PromptText", 18f, TextAlignmentOptions.Center);
            var promptTextRect = (RectTransform)promptText.transform;
            promptTextRect.anchorMin = Vector2.zero;
            promptTextRect.anchorMax = Vector2.one;
            promptTextRect.offsetMin = Vector2.zero;
            promptTextRect.offsetMax = Vector2.zero;
            promptRoot.SetActive(false);

            var promptUI = canvasGO.AddComponent<InteractionPromptUI>();
            SetObjectField(promptUI, "root", promptRoot);
            SetObjectField(promptUI, "promptText", promptText);

            var crosshairRoot = new GameObject("Crosshair", typeof(RectTransform));
            crosshairRoot.transform.SetParent(canvasGO.transform, false);
            var crosshairRect = (RectTransform)crosshairRoot.transform;
            crosshairRect.anchorMin = new Vector2(0.5f, 0.5f);
            crosshairRect.anchorMax = new Vector2(0.5f, 0.5f);
            crosshairRect.sizeDelta = new Vector2(7f, 7f);
            var crosshairImage = crosshairRoot.AddComponent<Image>();
            crosshairImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            crosshairImage.color = new Color(1f, 1f, 1f, 0.5f);
            crosshairImage.raycastTarget = false;
            var crosshairUI = canvasGO.AddComponent<CrosshairUI>();
            SetObjectField(crosshairUI, "dot", crosshairImage);

            var noteRoot = new GameObject("NoteReaderPanel", typeof(RectTransform));
            noteRoot.transform.SetParent(canvasGO.transform, false);
            var noteRect = (RectTransform)noteRoot.transform;
            noteRect.anchorMin = new Vector2(0.5f, 0.5f);
            noteRect.anchorMax = new Vector2(0.5f, 0.5f);
            noteRect.sizeDelta = new Vector2(650f, 420f);
            var noteImage = noteRoot.AddComponent<Image>();
            noteImage.color = new Color(0f, 0f, 0f, 0.85f);

            var titleText = CreateTMPText(noteRoot.transform, "Title", 30f, TextAlignmentOptions.TopLeft);
            var titleRect = (RectTransform)titleText.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(-40f, 50f);
            titleRect.anchoredPosition = new Vector2(0f, -20f);

            var bodyText = CreateTMPText(noteRoot.transform, "Body", 20f, TextAlignmentOptions.TopLeft);
            var bodyRect = (RectTransform)bodyText.transform;
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(25f, 25f);
            bodyRect.offsetMax = new Vector2(-25f, -80f);
            noteRoot.SetActive(false);

            var noteUI = canvasGO.AddComponent<NoteReaderUI>();
            SetObjectField(noteUI, "root", noteRoot);
            SetObjectField(noteUI, "titleText", titleText);
            SetObjectField(noteUI, "bodyText", bodyText);

            if (TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogWarning("No default TMP font asset found. If prompt/note text renders " +
                    "invisible, go to Window > TextMeshPro > Import TMP Essential Resources, then rebuild.");
            }
        }

        private static void CreateInteractables()
        {
            // All three sit in front of the player (who spawns at z=-4 looking +z) and get bright
            // emissive materials so they read clearly even in the dark room. Freestanding for this
            // functional test, not embedded in walls.

            // Door (right of the player) — dark red, faintly emissive so it's recognizable as a slab.
            var doorHinge = new GameObject("Door_Hinge");
            doorHinge.transform.position = new Vector3(3.5f, 0f, 2f);
            var doorPanel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorPanel.name = "Door_Panel";
            doorPanel.transform.SetParent(doorHinge.transform);
            doorPanel.transform.localScale = new Vector3(1f, 2.1f, 0.1f);
            doorPanel.transform.localPosition = new Vector3(0.5f, 1.05f, 0f);
            SetMaterial(doorPanel, MakeMaterial(new Color(0.45f, 0.12f, 0.1f), new Color(0.18f, 0.03f, 0.02f)));
            var door = doorHinge.AddComponent<Door>();
            SetObjectField(door, "hinge", doorHinge.transform);

            // Battery pickup (front-right) on a pedestal, glowing green.
            CreateBox("Pedestal_Battery", new Vector3(2f, 0.4f, 3f), new Vector3(0.5f, 0.8f, 0.5f));
            var pickup = CreateBox("Pickup_Battery", new Vector3(2f, 0.95f, 3f), new Vector3(0.18f, 0.35f, 0.18f));
            SetMaterial(pickup, MakeMaterial(new Color(0.1f, 0.5f, 0.15f), new Color(0.1f, 0.9f, 0.25f)));
            var pickupComp = pickup.AddComponent<Pickup>();
            SetStringField(pickupComp, "itemId", "battery");
            SetStringField(pickupComp, "displayName", "battery");

            var clue = AssetDatabase.LoadAssetAtPath<ClueDefinition>(TestClueAssetPath);
            if (clue == null)
            {
                clue = ScriptableObject.CreateInstance<ClueDefinition>();
                clue.clueId = "test_nurse_note";
                clue.displayTitle = "Nurse's Note";
                clue.bodyText = "\"Ward B fed at six. Ward B fed at six. Ward B fed at six.\"\n\n" +
                    "The same line, written maybe two hundred times, each one a little less steady than the last.";
                clue.knowledgeValue = 2f;
                AssetDatabase.CreateAsset(clue, TestClueAssetPath);
                AssetDatabase.SaveAssets();
            }

            // Note (front-left) on a stand, upright, glowing paper-white so it's obvious.
            CreateBox("Stand_Note", new Vector3(-2f, 0.5f, 3f), new Vector3(0.5f, 1f, 0.5f));
            var notePanel = CreateBox("Note_NursesNote", new Vector3(-2f, 1.15f, 3f), new Vector3(0.45f, 0.55f, 0.04f));
            SetMaterial(notePanel, MakeMaterial(new Color(0.8f, 0.78f, 0.65f), new Color(0.45f, 0.43f, 0.35f)));
            var noteComp = notePanel.AddComponent<NoteInteractable>();
            SetObjectField(noteComp, "clue", clue);
        }

        private static Material MakeMaterial(Color baseColor, Color emission)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", baseColor);
            if (emission.maxColorComponent > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", emission);
            }
            return mat;
        }

        private static void SetMaterial(GameObject go, Material mat)
        {
            if (go != null && go.TryGetComponent<Renderer>(out var renderer))
                renderer.sharedMaterial = mat;
        }

        private static TMP_Text CreateTMPText(Transform parent, string name, float fontSize, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = Color.white;
            tmp.text = string.Empty;
            return tmp;
        }

        private static GameObject CreateBox(string name, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            return go;
        }

        private static void SetObjectField(UnityObject target, string fieldName, UnityObject value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError($"Field '{fieldName}' not found on {target.GetType().Name}.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        private static void SetStringField(UnityObject target, string fieldName, string value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError($"Field '{fieldName}' not found on {target.GetType().Name}.");
                return;
            }
            prop.stringValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
#endif
