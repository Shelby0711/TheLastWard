#if UNITY_EDITOR
using LastWard.Entity;
using LastWard.Knowledge;
using LastWard.Net;
using LastWard.Player;
using LastWard.Puzzles;
using LastWard.Spectator;
using LastWard.UI;
using TMPro;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityObject = UnityEngine.Object;

namespace LastWard.EditorTools
{
    /// <summary>
    /// Shared scene-building infrastructure used by every "Build M#..." Editor tool: the networked
    /// player prefab, NetworkManager/session/knowledge singletons, the connection + gameplay UI
    /// bundle, generic props (doors, notes, the fuse puzzle, the Entity), NavMesh baking, and all the
    /// low-level GameObject/UI/SerializedObject helpers. Scene-specific layout (room shapes, which
    /// puzzles go where, waypoints) stays in each individual builder script.
    /// </summary>
    public static class EditorBuildKit
    {
        public const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/NetworkPlayer.prefab";

        // --- player / networking ---

        public static GameObject BuildPlayerPrefab()
        {
            var root = new GameObject("NetworkPlayer");

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            root.AddComponent<NetworkObject>();
            root.AddComponent<ClientNetworkTransform>();

            var input = root.AddComponent<PlayerInputReader>();

            var pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(root.transform);
            pivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            var cameraGO = new GameObject("PlayerCamera");
            cameraGO.transform.SetParent(pivot.transform);
            cameraGO.transform.localPosition = Vector3.zero;
            var camera = cameraGO.AddComponent<Camera>();
            var audioListener = cameraGO.AddComponent<AudioListener>();

            var flashlightGO = new GameObject("Flashlight");
            flashlightGO.transform.SetParent(cameraGO.transform);
            flashlightGO.transform.localPosition = Vector3.zero;
            var flashlight = flashlightGO.AddComponent<Light>();
            flashlight.type = LightType.Spot;
            flashlight.range = 12f;
            flashlight.spotAngle = 45f;
            flashlight.intensity = 3f;
            flashlight.color = new Color(1f, 0.95f, 0.8f);
            flashlight.enabled = false;

            var motor = root.AddComponent<FirstPersonMotor>();
            SetRef(motor, "input", input);

            var look = root.AddComponent<FirstPersonLook>();
            SetRef(look, "input", input);
            SetRef(look, "cameraPivot", pivot.transform);

            // Networked view/flashlight/alive state — enabled on ALL copies (remote clients apply the
            // flashlight and the spectator reads the watched view through it).
            var netState = root.AddComponent<PlayerNetworkState>();
            SetRef(netState, "cameraPivot", pivot.transform);
            SetRef(netState, "flashlight", flashlight);

            var flashlightController = root.AddComponent<FlashlightController>();
            SetRef(flashlightController, "input", input);
            SetRef(flashlightController, "state", netState);

            var interactor = root.AddComponent<PlayerInteractor>();
            SetRef(interactor, "input", input);
            SetRef(interactor, "interactCamera", camera);

            var inventory = root.AddComponent<PlayerInventory>();
            SetRef(inventory, "input", input);

            var spectator = root.AddComponent<SpectatorController>();
            SetRef(spectator, "input", input);
            SetRef(spectator, "state", netState);
            SetRef(spectator, "spectatorCamera", camera);

            var death = root.AddComponent<PlayerDeath>();
            // On death, freeze movement/look/interaction. Camera + input stay (spectator needs them).
            SetRefArray(death, "disableOnDeath", new Behaviour[] { motor, look, interactor });
            SetRef(death, "state", netState);
            SetRef(death, "spectator", spectator);

            // Footsteps run on all copies (position-derived) so everyone hears everyone — not owner-only.
            root.AddComponent<PlayerFootsteps>();

            var networkPlayer = root.AddComponent<NetworkPlayer>();

            // Owner-only: disabled in the prefab, re-enabled for the owner by NetworkPlayer on spawn.
            // PlayerNetworkState + PlayerDeath stay enabled on all copies (state must sync/apply
            // everywhere; PlayerDeath self-guards to the owner). SpectatorController is owner-only.
            Behaviour[] ownerOnly = { camera, audioListener, input, motor, look, flashlightController, interactor, inventory, spectator };
            foreach (var b in ownerOnly) b.enabled = false;
            SetRefArray(networkPlayer, "ownerOnlyBehaviours", ownerOnly);

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PlayerPrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            UnityObject.DestroyImmediate(root);
            return prefab;
        }

        public static void CreateBootstrapCamera(Vector3 position)
        {
            var camGO = new GameObject("MenuCamera");
            camGO.transform.position = position;
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.01f, 0.02f);
            camGO.AddComponent<BootstrapCamera>();
        }

        public static void FixSceneNetworkObjectHashes()
        {
            uint hash = 0x1001;
            foreach (var netObj in UnityObject.FindObjectsByType<NetworkObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
            {
                var so = new SerializedObject(netObj);
                var prop = so.FindProperty("GlobalObjectIdHash");
                if (prop != null)
                {
                    prop.uintValue = hash++;
                    so.ApplyModifiedProperties();
                }
                else
                {
                    Debug.LogWarning("NetworkObject has no serialized GlobalObjectIdHash field — NGO API may have changed.");
                }
            }
        }

        public static void CreateNetworkManager(GameObject playerPrefab)
        {
            var nmGO = new GameObject("NetworkManager");
            var nm = nmGO.AddComponent<NetworkManager>();
            var transport = nmGO.AddComponent<UnityTransport>();

            var so = new SerializedObject(nm);
            var playerProp = so.FindProperty("NetworkConfig.PlayerPrefab");
            if (playerProp != null) playerProp.objectReferenceValue = playerPrefab;
            var transportProp = so.FindProperty("NetworkConfig.NetworkTransport");
            if (transportProp != null) transportProp.objectReferenceValue = transport;
            // Required for NetworkSessionManager's ConnectionApprovalCallback (spawn position
            // override) to actually be invoked — without this, NGO auto-approves everyone silently
            // and every scene's spawnPosition field is ignored.
            var approvalProp = so.FindProperty("NetworkConfig.ConnectionApproval");
            if (approvalProp != null) approvalProp.boolValue = true;
            so.ApplyModifiedProperties();
        }

        public static NetworkSessionManager CreateSessionManager()
        {
            return new GameObject("NetworkSessionManager").AddComponent<NetworkSessionManager>();
        }

        public static void CreateKnowledgeService()
        {
            var go = new GameObject("KnowledgeService");
            go.AddComponent<NetworkObject>();
            go.AddComponent<KnowledgeService>();
        }

        // --- doors / entity / navmesh ---

        public static NetworkedDoor CreateNetworkedDoor(string name, Vector3 position)
        {
            var hinge = new GameObject(name);
            hinge.transform.position = position;
            hinge.AddComponent<NetworkObject>();
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(hinge.transform);
            panel.transform.localScale = new Vector3(1f, 2.1f, 0.1f);
            panel.transform.localPosition = new Vector3(0.5f, 1.05f, 0f);
            SetMaterial(panel, MakeMaterial(new Color(0.45f, 0.12f, 0.1f)));
            var door = hinge.AddComponent<NetworkedDoor>();
            SetRef(door, "hinge", hinge.transform);
            // Gated by a puzzle, not free to open — that's each puzzle's whole point.
            SetBool(door, "startLocked", true);
            return door;
        }

        public static void BakeNavMesh()
        {
            var navGO = new GameObject("Navigation");
            var surface = navGO.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        public static void CreateEntity(Transform[] waypoints, Vector3 spawnPosition)
        {
            var entity = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            entity.name = "Entity";
            entity.transform.position = spawnPosition;
            entity.transform.localScale = new Vector3(0.9f, 1.15f, 0.9f);
            // The capsule's own collider would block the Entity's own vision linecasts and the
            // NavMeshAgent — drop it; the agent handles movement/avoidance.
            UnityObject.DestroyImmediate(entity.GetComponent<Collider>());
            SetMaterial(entity, MakeEmissive(new Color(0.02f, 0.02f, 0.02f), new Color(0.15f, 0.02f, 0.02f)));

            entity.AddComponent<NetworkObject>();
            var agent = entity.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 2f;
            agent.speed = 2.2f;
            agent.angularSpeed = 240f;
            agent.acceleration = 12f;

            // Server-authoritative transform (plain NetworkTransform), unlike the owner-authoritative
            // ClientNetworkTransform the players use.
            entity.AddComponent<NetworkTransform>();

            var controller = entity.AddComponent<EntityController>();
            SetRefArray(controller, "waypoints", waypoints);

            entity.AddComponent<EntityAudio>();
        }

        // --- P1 fuse/power puzzle (reusable across scenes; P2/P3 stay scene-specific for now) ---

        public static NetworkedDoor CreateFusePuzzle(
            string doorName, Vector3 doorPosition,
            Vector3 breakerBoxPosition,
            Vector3 fusePickup1Position, Vector3 fusePickup2Position,
            Vector3 orderNotePosition)
        {
            var door = CreateNetworkedDoor(doorName, doorPosition);

            var puzzleGO = new GameObject("FusePowerPuzzle");
            puzzleGO.transform.position = breakerBoxPosition;
            puzzleGO.AddComponent<NetworkObject>();
            var puzzle = puzzleGO.AddComponent<FusePowerPuzzle>();
            SetRef(puzzle, "gatedDoor", door);

            CreateBox("BreakerBox", breakerBoxPosition + new Vector3(0f, 0f, -0.15f), new Vector3(1.2f, 1f, 0.15f));
            string[] labels = { "Breaker 1", "Breaker 2", "Breaker 3" };
            float[] xOffsets = { -0.4f, 0f, 0.4f };
            for (int i = 0; i < 3; i++)
            {
                var sw = CreateBox($"Switch_{i}", breakerBoxPosition + new Vector3(xOffsets[i], 0.1f, -0.25f), new Vector3(0.12f, 0.25f, 0.06f));
                SetMaterial(sw, MakeEmissive(new Color(0.3f, 0.25f, 0.05f), new Color(0.6f, 0.5f, 0.1f)));
                var breaker = sw.AddComponent<BreakerSwitch>();
                SetRef(breaker, "puzzle", puzzle);
                SetInt(breaker, "breakerIndex", i);
                SetString(breaker, "label", labels[i]);
            }

            for (int i = 0; i < 2; i++)
            {
                var socket = CreateBox($"FuseSocket_{i}", breakerBoxPosition + new Vector3(i == 0 ? -0.25f : 0.25f, -0.25f, -0.25f), new Vector3(0.15f, 0.15f, 0.1f));
                SetMaterial(socket, MakeMaterial(new Color(0.15f, 0.15f, 0.15f)));
                var fs = socket.AddComponent<FuseSocket>();
                SetRef(fs, "puzzle", puzzle);
                SetInt(fs, "slotIndex", i);
                SetString(fs, "requiredItemId", "fuse");
            }

            CreateFusePickup(fusePickup1Position);
            CreateFusePickup(fusePickup2Position);

            // One shared clue asset — every scene's fuse puzzle reuses the same order note.
            const string cluePath = "Assets/_Project/Data/BreakerOrderClue.asset";
            var clue = AssetDatabase.LoadAssetAtPath<ClueDefinition>(cluePath);
            if (clue == null)
            {
                clue = ScriptableObject.CreateInstance<ClueDefinition>();
                clue.clueId = "p1_breaker_order";
                clue.displayTitle = "Maintenance Scrap";
                clue.bodyText = "Grease-pencil, barely legible:\n\n\"Breaker order -- 2, 3, 1. Do NOT skip. Ward loses power otherwise.\"";
                clue.knowledgeValue = 2f;
                AssetDatabase.CreateAsset(clue, cluePath);
                AssetDatabase.SaveAssets();
            }
            var notePanel = CreateBox("Note_BreakerOrder", orderNotePosition, new Vector3(0.45f, 0.55f, 0.04f));
            SetMaterial(notePanel, MakeEmissive(new Color(0.8f, 0.78f, 0.65f), new Color(0.45f, 0.43f, 0.35f)));
            var orderNote = notePanel.AddComponent<NoteInteractable>();
            SetRef(orderNote, "clue", clue);

            return door;
        }

        public static void CreateFusePickup(Vector3 position)
        {
            var fuse = CreateBox("Pickup_Fuse", position, new Vector3(0.12f, 0.12f, 0.3f));
            SetMaterial(fuse, MakeEmissive(new Color(0.5f, 0.3f, 0.05f), new Color(0.9f, 0.55f, 0.1f)));
            fuse.AddComponent<NetworkObject>();
            var pickup = fuse.AddComponent<NetworkedPickup>();
            SetString(pickup, "itemId", "fuse");
            SetString(pickup, "displayName", "fuse");
        }

        // --- P3 intercom sequence puzzle (reusable across scenes, same reasoning as CreateFusePuzzle) ---

        public static NetworkedDoor CreateIntercomPuzzle(
            string doorName, Vector3 doorPosition,
            Vector3 puzzleMarkerPosition,
            (string label, Vector3 pos)[] stations,
            Vector3 orderNotePosition)
        {
            var door = CreateNetworkedDoor(doorName, doorPosition);

            var puzzleGO = new GameObject("IntercomPuzzle");
            puzzleGO.transform.position = puzzleMarkerPosition;
            puzzleGO.AddComponent<NetworkObject>();
            var puzzle = puzzleGO.AddComponent<IntercomPuzzle>();
            SetRef(puzzle, "gatedDoor", door);

            for (int i = 0; i < stations.Length; i++)
            {
                var station = CreateBox($"Intercom_{stations[i].label}", stations[i].pos, new Vector3(0.25f, 0.35f, 0.15f));
                SetMaterial(station, MakeEmissive(new Color(0.15f, 0.15f, 0.05f), new Color(0.5f, 0.45f, 0.1f)));
                var comp = station.AddComponent<IntercomStation>();
                SetRef(comp, "puzzle", puzzle);
                SetInt(comp, "stationIndex", i);
                SetString(comp, "label", stations[i].label);
            }

            // One shared clue asset — text matches IntercomPuzzle's default correctOrder {2,0,1}:
            // whichever station is at stations[2], then stations[0], then stations[1].
            CreateNoteProp("Note_IntercomLog", orderNotePosition,
                "Assets/_Project/Data/IntercomLogClue.asset", "p3_intercom_order", "Reception Radio Log",
                "Looped for hours, same three lines:\n\n\"...Ward C. Then Ward A. Then Reception...\"\n\"...Ward C. Then Ward A. Then Reception...\"",
                2f);

            return door;
        }

        // --- notes ---

        public static Transform CreateNoteProp(string name, Vector3 position, string cluePath, string clueId, string title, string body, float knowledgeValue)
        {
            var clue = AssetDatabase.LoadAssetAtPath<ClueDefinition>(cluePath);
            if (clue == null)
            {
                clue = ScriptableObject.CreateInstance<ClueDefinition>();
                clue.clueId = clueId;
                clue.displayTitle = title;
                clue.bodyText = body;
                clue.knowledgeValue = knowledgeValue;
                AssetDatabase.CreateAsset(clue, cluePath);
                AssetDatabase.SaveAssets();
            }
            var panel = CreateBox(name, position, new Vector3(0.45f, 0.55f, 0.04f));
            SetMaterial(panel, MakeEmissive(new Color(0.8f, 0.78f, 0.65f), new Color(0.45f, 0.43f, 0.35f)));
            var note = panel.AddComponent<NoteInteractable>();
            SetRef(note, "clue", clue);
            return panel.transform;
        }

        public static Transform MakePoint(Vector3 position)
        {
            var go = new GameObject("ClueSpawnPoint");
            go.transform.position = position;
            return go.transform;
        }

        // --- connection + gameplay UI bundle ---

        public static void CreateConnectionUI()
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();

            var canvasGO = new GameObject("Canvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var panel = CreateRect("ConnectionPanel", canvasGO.transform, new Vector2(0.5f, 0.5f), new Vector2(420f, 320f), Vector2.zero);
            panel.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);

            var hostButton = CreateButton("HostButton", panel, "HOST", new Vector2(0f, 100f));
            var codeInput = CreateInputField("CodeInput", panel, "join code", new Vector2(0f, 20f));
            var joinButton = CreateButton("JoinButton", panel, "JOIN", new Vector2(0f, -60f));
            var status = CreateText(panel, "Status", 20f, TextAlignmentOptions.Center);
            var statusRect = (RectTransform)status.transform;
            statusRect.anchorMin = new Vector2(0.5f, 0f);
            statusRect.anchorMax = new Vector2(0.5f, 0f);
            statusRect.sizeDelta = new Vector2(400f, 60f);
            statusRect.anchoredPosition = new Vector2(0f, -120f);

            var ui = canvasGO.AddComponent<ConnectionUI>();
            SetRef(ui, "panel", panel.gameObject);
            SetRef(ui, "hostButton", hostButton);
            SetRef(ui, "joinButton", joinButton);
            SetRef(ui, "codeInput", codeInput);
            SetRef(ui, "statusText", status);

            CreateGameplayUI(canvasGO);
        }

        public static void CreateGameplayUI(GameObject canvasGO)
        {
            var promptRoot = CreateRect("InteractionPrompt", canvasGO.transform, new Vector2(0.5f, 0.18f), new Vector2(500f, 34f), Vector2.zero);
            var promptText = CreateText(promptRoot, "PromptText", 18f, TextAlignmentOptions.Center);
            StretchToParent((RectTransform)promptText.transform);
            promptRoot.gameObject.SetActive(false);

            var promptUI = canvasGO.AddComponent<InteractionPromptUI>();
            SetRef(promptUI, "root", promptRoot.gameObject);
            SetRef(promptUI, "promptText", promptText);

            CreateCrosshair(canvasGO);
            CreateDeathScreen(canvasGO);
            CreateKnowledgeDebug(canvasGO);
            CreateKeypadUI(canvasGO);
            CreateEndingUI(canvasGO);
            CreateSpectatorUI(canvasGO);
            CreateSpectatorPingUI(canvasGO);

            var noteRoot = CreateRect("NoteReaderPanel", canvasGO.transform, new Vector2(0.5f, 0.5f), new Vector2(650f, 420f), Vector2.zero);
            noteRoot.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

            var titleText = CreateText(noteRoot, "Title", 30f, TextAlignmentOptions.TopLeft);
            var titleRect = (RectTransform)titleText.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(-40f, 50f);
            titleRect.anchoredPosition = new Vector2(0f, -20f);

            var bodyText = CreateText(noteRoot, "Body", 20f, TextAlignmentOptions.TopLeft);
            var bodyRect = (RectTransform)bodyText.transform;
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(25f, 25f);
            bodyRect.offsetMax = new Vector2(-25f, -80f);
            noteRoot.gameObject.SetActive(false);

            var noteUI = canvasGO.AddComponent<NoteReaderUI>();
            SetRef(noteUI, "root", noteRoot.gameObject);
            SetRef(noteUI, "titleText", titleText);
            SetRef(noteUI, "bodyText", bodyText);
        }

        public static void CreateCrosshair(GameObject canvasGO)
        {
            var dotRect = CreateRect("Crosshair", canvasGO.transform, new Vector2(0.5f, 0.5f), new Vector2(7f, 7f), Vector2.zero);
            var image = dotRect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            image.color = new Color(1f, 1f, 1f, 0.5f);
            image.raycastTarget = false;

            var crosshair = canvasGO.AddComponent<CrosshairUI>();
            SetRef(crosshair, "dot", image);
        }

        public static void CreateDeathScreen(GameObject canvasGO)
        {
            var root = CreateRect("DeathScreen", canvasGO.transform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            StretchToParent(root);
            root.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.95f);
            var label = CreateText(root, "Label", 64f, TextAlignmentOptions.Center);
            StretchToParent((RectTransform)label.transform);
            label.color = new Color(0.6f, 0.05f, 0.05f);
            root.gameObject.SetActive(false);

            var death = canvasGO.AddComponent<DeathScreenUI>();
            SetRef(death, "root", root.gameObject);
            SetRef(death, "label", label);
        }

        public static void CreateKnowledgeDebug(GameObject canvasGO)
        {
            var root = CreateRect("KnowledgeDebug", canvasGO.transform, new Vector2(0f, 1f), new Vector2(320f, 200f), new Vector2(170f, -110f));
            root.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            var text = CreateText(root, "Text", 16f, TextAlignmentOptions.TopLeft);
            StretchToParent((RectTransform)text.transform);
            ((RectTransform)text.transform).offsetMin = new Vector2(8f, 8f);
            ((RectTransform)text.transform).offsetMax = new Vector2(-8f, -8f);
            root.gameObject.SetActive(false);

            var debug = canvasGO.AddComponent<KnowledgeDebugUI>();
            SetRef(debug, "root", root.gameObject);
            SetRef(debug, "text", text);
        }

        public static void CreateSpectatorUI(GameObject canvasGO)
        {
            var root = CreateRect("SpectatorOverlay", canvasGO.transform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            StretchToParent(root);
            // Faint blue-grey tint = "watching through the hospital's memory". Non-blocking.
            var tint = root.gameObject.AddComponent<Image>();
            tint.color = new Color(0.15f, 0.18f, 0.25f, 0.25f);
            tint.raycastTarget = false;

            var label = CreateText(root, "WatchedLabel", 20f, TextAlignmentOptions.Bottom);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = new Vector2(0.5f, 0f);
            labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.sizeDelta = new Vector2(700f, 40f);
            labelRect.anchoredPosition = new Vector2(0f, 40f);
            root.gameObject.SetActive(false);

            var spectatorUI = canvasGO.AddComponent<SpectatorUI>();
            SetRef(spectatorUI, "root", root.gameObject);
            SetRef(spectatorUI, "label", label);
        }

        public static void CreateSpectatorPingUI(GameObject canvasGO)
        {
            var marker = CreateRect("PingMarker", canvasGO.transform, new Vector2(0.5f, 0.5f), new Vector2(64f, 64f), Vector2.zero);
            var image = marker.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            image.color = new Color(1f, 0.9f, 0.4f, 0.85f);
            image.raycastTarget = false;
            marker.gameObject.SetActive(false);

            var pingUI = canvasGO.AddComponent<SpectatorPingUI>();
            SetRef(pingUI, "marker", marker.gameObject);
        }

        public static void CreateEndingUI(GameObject canvasGO)
        {
            var root = CreateRect("EndingScreen", canvasGO.transform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            StretchToParent(root);
            root.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.95f);
            var label = CreateText(root, "Label", 48f, TextAlignmentOptions.Center);
            StretchToParent((RectTransform)label.transform);
            label.color = new Color(0.85f, 0.85f, 0.8f);
            root.gameObject.SetActive(false);

            var ending = canvasGO.AddComponent<EndingUI>();
            SetRef(ending, "root", root.gameObject);
            SetRef(ending, "label", label);
        }

        public static void CreateKeypadUI(GameObject canvasGO)
        {
            var panel = CreateRect("KeypadPanel", canvasGO.transform, new Vector2(0.5f, 0.5f), new Vector2(320f, 220f), Vector2.zero);
            panel.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

            var label = CreateText(panel, "Label", 22f, TextAlignmentOptions.Center);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = new Vector2(0.5f, 1f);
            labelRect.anchorMax = new Vector2(0.5f, 1f);
            labelRect.sizeDelta = new Vector2(280f, 40f);
            labelRect.anchoredPosition = new Vector2(0f, -30f);
            label.text = "ENTER CODE";

            var codeInput = CreateInputField("CodeInput", panel, "code", new Vector2(0f, 10f));
            var submitButton = CreateButton("SubmitButton", panel, "SUBMIT", new Vector2(0f, -60f));
            panel.gameObject.SetActive(false);

            var keypadUI = canvasGO.AddComponent<KeypadUI>();
            SetRef(keypadUI, "root", panel.gameObject);
            SetRef(keypadUI, "codeInput", codeInput);
            SetRef(keypadUI, "submitButton", submitButton);
        }

        // --- low-level helpers ---

        public static Button CreateButton(string name, Transform parent, string label, Vector2 pos)
        {
            var rect = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(220f, 60f), pos);
            rect.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            var text = CreateText(rect, "Label", 24f, TextAlignmentOptions.Center);
            var tr = (RectTransform)text.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;
            text.text = label;
            return button;
        }

        public static TMP_InputField CreateInputField(string name, Transform parent, string placeholder, Vector2 pos)
        {
            var rect = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(220f, 50f), pos);
            rect.gameObject.AddComponent<Image>().color = new Color(0.9f, 0.9f, 0.9f, 1f);
            var input = rect.gameObject.AddComponent<TMP_InputField>();

            var area = CreateRect("TextArea", rect, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.sizeDelta = new Vector2(-16f, -8f); area.anchoredPosition = Vector2.zero;
            area.gameObject.AddComponent<RectMask2D>();

            var placeholderText = CreateText(area, "Placeholder", 22f, TextAlignmentOptions.Left);
            placeholderText.color = new Color(0.4f, 0.4f, 0.4f);
            placeholderText.text = placeholder;
            StretchToParent((RectTransform)placeholderText.transform);

            var inputText = CreateText(area, "Text", 22f, TextAlignmentOptions.Left);
            inputText.color = Color.black;
            StretchToParent((RectTransform)inputText.transform);

            input.textViewport = area;
            input.textComponent = inputText;
            input.placeholder = placeholderText;
            input.characterLimit = 12;
            return input;
        }

        public static void StretchToParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        public static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 anchoredPos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size; rt.anchoredPosition = anchoredPos;
            return rt;
        }

        public static TMP_Text CreateText(Transform parent, string name, float fontSize, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize; tmp.alignment = alignment; tmp.color = Color.white; tmp.text = string.Empty;
            return tmp;
        }

        public static GameObject CreateBox(string name, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.position = position; go.transform.localScale = scale;
            return go;
        }

        public static Material MakeMaterial(Color baseColor)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", baseColor);
            return mat;
        }

        public static Material MakeEmissive(Color baseColor, Color emission)
        {
            var mat = MakeMaterial(baseColor);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", emission);
            return mat;
        }

        public static void SetMaterial(GameObject go, Material mat)
        {
            if (go != null && go.TryGetComponent<Renderer>(out var renderer)) renderer.sharedMaterial = mat;
        }

        public static void SetString(UnityObject target, string fieldName, string value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogError($"Field '{fieldName}' not found on {target.GetType().Name}."); return; }
            prop.stringValue = value;
            so.ApplyModifiedProperties();
        }

        public static void SetInt(UnityObject target, string fieldName, int value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogError($"Field '{fieldName}' not found on {target.GetType().Name}."); return; }
            prop.intValue = value;
            so.ApplyModifiedProperties();
        }

        public static void SetBool(UnityObject target, string fieldName, bool value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogError($"Field '{fieldName}' not found on {target.GetType().Name}."); return; }
            prop.boolValue = value;
            so.ApplyModifiedProperties();
        }

        public static void SetVector3(UnityObject target, string fieldName, Vector3 value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogError($"Field '{fieldName}' not found on {target.GetType().Name}."); return; }
            prop.vector3Value = value;
            so.ApplyModifiedProperties();
        }

        public static void SetRef(UnityObject target, string fieldName, UnityObject value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogError($"Field '{fieldName}' not found on {target.GetType().Name}."); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        public static void SetRefArray(UnityObject target, string fieldName, UnityObject[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null) { Debug.LogError($"Field '{fieldName}' not found on {target.GetType().Name}."); return; }
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedProperties();
        }
    }
}
#endif
