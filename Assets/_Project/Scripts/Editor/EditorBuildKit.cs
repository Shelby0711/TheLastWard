#if UNITY_EDITOR
using LastWard.Aftermath;
using LastWard.Core;
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

        /// <summary>
        /// Project-level settings a networked host can't run correctly without. Run In Background is
        /// the big one: without it Unity pauses when the window loses focus, which stalls the host's
        /// UGS session heartbeats and drops the session — surfacing later as a mid-game "freeze"
        /// plus a WebSocket-closed error.
        /// </summary>
        public static void EnsureProjectSettings()
        {
            if (PlayerSettings.runInBackground) return;
            PlayerSettings.runInBackground = true;
            Debug.Log("Enabled Player Settings > Run In Background (required for a stable networked host).");
        }

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
            // Default clear flags would render the skybox, which shows up as a bright blue daylight
            // gradient behind the level once there's real geometry with an open horizon. Clearing to
            // the fog colour instead keeps the "can't see past the fog" isolation the level relies on.
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            // Held items live ~0.4m from the eye, which the default 0.3 near plane clips into.
            camera.nearClipPlane = 0.03f;
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

            // Held torch: a real mesh in the corner of the view, shown only while the beam is on, so
            // the flashlight is something the player carries rather than a light source hovering in
            // front of their face. Parented to the camera, which is disabled on remote copies, so
            // only its owner ever sees it.
            var flashlightModel = BuildFlashlightViewModel(cameraGO.transform);

            var motor = root.AddComponent<FirstPersonMotor>();
            SetRef(motor, "input", input);
            SetRef(motor, "cameraPivot", pivot.transform);

            var look = root.AddComponent<FirstPersonLook>();
            SetRef(look, "input", input);
            SetRef(look, "cameraPivot", pivot.transform);

            // Networked view/flashlight/alive state — enabled on ALL copies (remote clients apply the
            // flashlight and the spectator reads the watched view through it).
            var netState = root.AddComponent<PlayerNetworkState>();
            SetRef(netState, "cameraPivot", pivot.transform);
            SetRef(netState, "flashlight", flashlight);
            if (flashlightModel != null) SetRef(netState, "flashlightModel", flashlightModel);

            var flashlightController = root.AddComponent<FlashlightController>();
            SetRef(flashlightController, "input", input);
            SetRef(flashlightController, "state", netState);

            var interactor = root.AddComponent<PlayerInteractor>();
            SetRef(interactor, "input", input);
            SetRef(interactor, "interactCamera", camera);

            var melee = root.AddComponent<PlayerMeleeDefense>();
            SetRef(melee, "swingCamera", camera);

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

        /// <summary>
        /// The first-person torch model, held low-right like something actually gripped. Falls back
        /// to a simple stand-in if the art isn't imported, so the prefab is never left without one.
        /// Starts inactive — PlayerNetworkState switches it on with the beam.
        /// </summary>
        private static GameObject BuildFlashlightViewModel(Transform cameraTransform)
        {
            var holder = new GameObject("FlashlightViewModel");
            holder.transform.SetParent(cameraTransform, false);
            holder.transform.localPosition = new Vector3(0.24f, -0.2f, 0.45f);
            holder.transform.localRotation = Quaternion.Euler(6f, -8f, 4f);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Art/Props/HorrorKit/PSXHorrorKit_Default.fbx");
            bool built = false;

            if (model != null)
            {
                var instance = (GameObject)UnityObject.Instantiate(model, holder.transform);
                instance.name = "Model";
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                    UnityObject.DestroyImmediate(collider);

                // The kit is one FBX holding four props, so everything that isn't the torch goes.
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name.StartsWith("Flashlight", System.StringComparison.OrdinalIgnoreCase)) continue;
                    UnityObject.DestroyImmediate(renderer.gameObject);
                }

                if (instance.GetComponentInChildren<Renderer>(true) != null)
                {
                    NormalizeViewModel(instance, 0.22f);
                    built = true;
                }
                else
                {
                    UnityObject.DestroyImmediate(instance);
                }
            }

            if (!built)
            {
                var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stand.name = "Model";
                stand.transform.SetParent(holder.transform, false);
                stand.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                stand.transform.localScale = new Vector3(0.035f, 0.11f, 0.035f);
                UnityObject.DestroyImmediate(stand.GetComponent<Collider>());
                SetMaterial(stand, MakeMaterial(new Color(0.18f, 0.18f, 0.2f)));
            }

            Debug.Log($"[Player] Flashlight view model built from {(built ? "the horror kit mesh" : "a primitive fallback")}.");
            holder.SetActive(false);
            return holder;
        }

        // Scales an imported prop to a sensible held size, turns it to point away from the viewer,
        // and re-centres it on its holder. Packs disagree wildly about units, pivots AND which axis
        // a prop "points" down, so all three have to be derived rather than assumed.
        private static void NormalizeViewModel(GameObject instance, float targetLength)
        {
            if (!TryMeasure(instance, out var bounds)) return;

            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest > 0.0001f) instance.transform.localScale *= targetLength / longest;

            // A torch modelled lying along X or standing up Y reads as held sideways — which is
            // exactly how it looked. Turn whichever axis is longest to face forward (+Z).
            if (!TryMeasure(instance, out bounds)) return;
            var size = bounds.size;
            if (size.x >= size.y && size.x >= size.z) instance.transform.Rotate(0f, 90f, 0f, Space.World);
            else if (size.y >= size.x && size.y >= size.z) instance.transform.Rotate(90f, 0f, 0f, Space.World);

            // Recompute once more — the rotation moved it — then pull it onto the holder's origin.
            if (!TryMeasure(instance, out bounds)) return;
            instance.transform.position += instance.transform.parent.position - bounds.center;
        }

        private static bool TryMeasure(GameObject instance, out Bounds bounds)
        {
            bounds = default;
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        /// <summary>
        /// A wardrobe you climb inside. Built hollow out of panels rather than as a solid block —
        /// the player is genuinely standing in the cavity, and the front is slatted so they watch
        /// the room through the gaps. The panels are ordinary solid colliders, which is also what
        /// the interaction ray hits: it runs with QueryTriggerInteraction.Ignore, so a trigger
        /// volume here would never be targetable.
        /// </summary>
        public static GameObject CreateWardrobeHidingSpot(string name, Vector3 position, string enterPrompt, float yaw)
        {
            const float width = 1.15f;
            const float height = 2.05f;
            const float depth = 0.72f;
            const float panel = 0.06f;

            var root = new GameObject(name);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var wood = MakeMaterial(new Color(0.13f, 0.1f, 0.08f));
            var trim = MakeMaterial(new Color(0.09f, 0.07f, 0.055f));

            AddPanel(root.transform, "Back", new Vector3(0f, height / 2f, -depth / 2f + panel / 2f), new Vector3(width, height, panel), wood);
            AddPanel(root.transform, "Side_L", new Vector3(-width / 2f + panel / 2f, height / 2f, 0f), new Vector3(panel, height, depth), wood);
            AddPanel(root.transform, "Side_R", new Vector3(width / 2f - panel / 2f, height / 2f, 0f), new Vector3(panel, height, depth), wood);
            AddPanel(root.transform, "Top", new Vector3(0f, height - panel / 2f, 0f), new Vector3(width, panel, depth), wood);
            AddPanel(root.transform, "Bottom", new Vector3(0f, panel / 2f, 0f), new Vector3(width, panel, depth), wood);

            // Louvred front. The gaps between slats are the whole point — they're what you see out
            // through, and what light falls through onto you.
            const float slatHeight = 0.1f;
            const float slatGap = 0.055f;
            float frontZ = depth / 2f - panel / 2f;
            for (float y = 0.16f; y < height - 0.16f; y += slatHeight + slatGap)
                AddPanel(root.transform, "Slat", new Vector3(0f, y, frontZ), new Vector3(width - 0.1f, slatHeight, panel * 0.7f), trim);

            // Frame around the doors so the front doesn't read as floating slats.
            AddPanel(root.transform, "Frame_L", new Vector3(-width / 2f + 0.04f, height / 2f, frontZ), new Vector3(0.08f, height, panel), wood);
            AddPanel(root.transform, "Frame_R", new Vector3(width / 2f - 0.04f, height / 2f, frontZ), new Vector3(0.08f, height, panel), wood);

            var handle = AddPanel(root.transform, "Handle", new Vector3(0.1f, 1.05f, frontZ + 0.04f), new Vector3(0.04f, 0.22f, 0.04f), trim);
            handle.transform.localRotation = Quaternion.Euler(0f, 0f, 6f);

            // Standing on the wardrobe floor puts the camera (1.6m up the player rig) inside the
            // cavity. Pushed back toward the rear panel rather than centred: dead centre leaves the
            // slats only ~33cm away, right on the camera's near-clip plane, where they swallow the
            // whole view. From the back wall you look through the gaps instead of into the door.
            return FinishHidingSpot(root, new Vector3(0f, 0.02f, -0.13f), enterPrompt);
        }

        /// <summary>
        /// A bed to crawl under. The player is dropped well below floor level so the camera ends up
        /// at about 35cm — down in the gap, sightlines pinched by the frame above.
        /// </summary>
        public static GameObject CreateUnderBedHidingSpot(string name, Vector3 position, string enterPrompt, float yaw)
        {
            const float width = 1.15f;
            const float length = 2.05f;
            const float clearance = 0.44f;   // underside of the frame

            var root = new GameObject(name);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var metal = MakeMaterial(new Color(0.15f, 0.15f, 0.16f));
            var sheet = MakeMaterial(new Color(0.3f, 0.29f, 0.26f));

            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    AddPanel(root.transform, "Leg",
                        new Vector3(sx * (width / 2f - 0.07f), clearance / 2f, sz * (length / 2f - 0.07f)),
                        new Vector3(0.07f, clearance, 0.07f), metal);
                }
            }

            AddPanel(root.transform, "Frame", new Vector3(0f, clearance + 0.05f, 0f), new Vector3(width, 0.1f, length), metal);
            AddPanel(root.transform, "Mattress", new Vector3(0f, clearance + 0.21f, 0f), new Vector3(width - 0.06f, 0.22f, length - 0.06f), sheet);
            AddPanel(root.transform, "Headboard", new Vector3(0f, clearance + 0.45f, -length / 2f + 0.04f), new Vector3(width, 0.7f, 0.08f), metal);

            // The player rig's camera sits 1.6m above its root, so the root has to go well under the
            // floor to get the viewpoint down into the gap. Safe because the CharacterController is
            // switched off for the whole time the player is hidden.
            return FinishHidingSpot(root, new Vector3(0f, -1.25f, 0f), enterPrompt);
        }

        private static GameObject AddPanel(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material)
        {
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = name;
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = localPosition;
            panel.transform.localScale = size;
            SetMaterial(panel, material);
            return panel;
        }

        private static GameObject FinishHidingSpot(GameObject root, Vector3 hideLocalPosition, string enterPrompt)
        {
            var point = new GameObject("HidePoint");
            point.transform.SetParent(root.transform, false);
            point.transform.localPosition = hideLocalPosition;

            root.AddComponent<NetworkObject>();
            var spot = root.AddComponent<HidingSpot>();
            SetRef(spot, "hidePoint", point.transform);
            SetString(spot, "enterPrompt", enterPrompt);
            return root;
        }

        /// <summary>
        /// A cupboard/locker/crate with a hinged door. Returns the transform to park contents on —
        /// items placed there sit inside the shell, unreachable until the door swings open, because
        /// the interaction ray hits the door panel first.
        /// </summary>
        /// <param name="requiredItemId">Empty to open freely, or "key" / "crowbar".</param>
        public static Transform CreateStorageContainer(string name, Vector3 position, float yaw,
            Vector3 size, string requiredItemId, bool consumesItem, string openPrompt)
        {
            const float panel = 0.06f;
            float w = size.x, h = size.y, d = size.z;

            var root = new GameObject(name);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var body = MakeMaterial(new Color(0.12f, 0.12f, 0.13f));
            var doorMat = MakeMaterial(string.IsNullOrEmpty(requiredItemId)
                ? new Color(0.16f, 0.15f, 0.14f)
                : new Color(0.2f, 0.13f, 0.08f));   // locked ones read warmer, so they stand out

            AddPanel(root.transform, "Back", new Vector3(0f, h / 2f, -d / 2f + panel / 2f), new Vector3(w, h, panel), body);
            AddPanel(root.transform, "Side_L", new Vector3(-w / 2f + panel / 2f, h / 2f, 0f), new Vector3(panel, h, d), body);
            AddPanel(root.transform, "Side_R", new Vector3(w / 2f - panel / 2f, h / 2f, 0f), new Vector3(panel, h, d), body);
            AddPanel(root.transform, "Top", new Vector3(0f, h - panel / 2f, 0f), new Vector3(w, panel, d), body);
            AddPanel(root.transform, "Bottom", new Vector3(0f, panel / 2f, 0f), new Vector3(w, panel, d), body);

            // Hinged on its left edge so it swings out of the way rather than sliding through itself.
            var hinge = new GameObject("Door");
            hinge.transform.SetParent(root.transform, false);
            hinge.transform.localPosition = new Vector3(-w / 2f + panel, h / 2f, d / 2f - panel / 2f);
            var doorPanel = AddPanel(hinge.transform, "Panel", new Vector3(w / 2f - panel, 0f, 0f), new Vector3(w - panel, h - panel * 2f, panel), doorMat);
            SetMaterial(doorPanel, doorMat);

            if (!string.IsNullOrEmpty(requiredItemId))
            {
                var latch = AddPanel(hinge.transform, "Latch", new Vector3(w - panel * 2.2f, 0f, 0.04f),
                    new Vector3(0.09f, 0.16f, 0.05f), MakeEmissive(new Color(0.25f, 0.2f, 0.06f), new Color(0.4f, 0.3f, 0.06f)));
                SetMaterial(latch, MakeEmissive(new Color(0.25f, 0.2f, 0.06f), new Color(0.45f, 0.33f, 0.07f)));
            }

            var contents = new GameObject("Contents");
            contents.transform.SetParent(root.transform, false);
            contents.transform.localPosition = new Vector3(0f, Mathf.Min(0.3f, h * 0.35f), 0f);

            root.AddComponent<NetworkObject>();
            var container = root.AddComponent<StorageContainer>();
            SetRef(container, "door", hinge.transform);
            SetString(container, "requiredItemId", requiredItemId ?? string.Empty);
            SetBool(container, "consumesItem", consumesItem);
            SetString(container, "openPrompt", openPrompt);
            return contents.transform;
        }

        /// <summary>
        /// Small carried tool as a networked pickup. <paramref name="meshNamePrefix"/> pulls the real
        /// mesh out of the horror kit FBX when the pack actually contains that item; otherwise it
        /// falls back to a shaped block. The kit ships a key, but no crowbar.
        /// </summary>
        /// <param name="standaloneModel">Art-relative path to a model that IS the item on its own
        /// (e.g. "Props/Crowbar/scene.gltf"), textured from <paramref name="standaloneTextures"/>.
        /// Takes precedence over carving one out of the horror kit.</param>
        public static GameObject CreateToolPickup(string itemId, string displayName, Vector3 position,
            Color color, string meshNamePrefix = null, string texFile = null,
            string standaloneModel = null, string standaloneTextures = null)
        {
            var tool = new GameObject($"Pickup_{itemId}");
            tool.transform.position = position;

            bool built = false;

            if (!string.IsNullOrEmpty(standaloneModel))
            {
                var model = ArtKit.LoadModel(standaloneModel);
                if (model != null)
                {
                    var instance = ArtKit.Spawn(model, tool.transform, "Model");
                    if (!string.IsNullOrEmpty(standaloneTextures))
                        ArtKit.AutoTexture(instance, standaloneTextures);
                    NormalizeViewModel(instance, 0.8f);   // a crowbar is roughly this long
                    built = true;
                }
            }

            if (!built && !string.IsNullOrEmpty(meshNamePrefix))
            {
                var kit = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/_Project/Art/Props/HorrorKit/PSXHorrorKit_Default.fbx");
                if (kit != null)
                {
                    var instance = (GameObject)UnityObject.Instantiate(kit, tool.transform);
                    instance.name = "Model";
                    foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                        UnityObject.DestroyImmediate(collider);
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer.name.StartsWith(meshNamePrefix, System.StringComparison.OrdinalIgnoreCase)) continue;
                        UnityObject.DestroyImmediate(renderer.gameObject);
                    }
                    if (instance.GetComponentInChildren<Renderer>(true) != null)
                    {
                        NormalizeViewModel(instance, 0.22f);
                        if (!string.IsNullOrEmpty(texFile))
                        {
                            var mat = ArtKit.MakeTexturedMaterial("Props/HorrorKit/textures/" + texFile, $"M_{itemId}");
                            if (mat != null) ArtKit.ApplyMaterial(instance, mat);
                        }
                        built = true;
                    }
                    else UnityObject.DestroyImmediate(instance);
                }
            }

            if (!built)
            {
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "Model";
                block.transform.SetParent(tool.transform, false);
                block.transform.localScale = itemId == "crowbar"
                    ? new Vector3(0.05f, 0.05f, 0.8f)
                    : new Vector3(0.05f, 0.12f, 0.02f);
                UnityObject.DestroyImmediate(block.GetComponent<Collider>());
                SetMaterial(block, MakeEmissive(color * 0.5f, color));
            }

            // Dropped items rest on whatever is under them. Callers pass the surface height, and
            // the mesh is settled onto it rather than hovering with its centre on that point —
            // which is why the key was floating in mid-air.
            var settle = tool.GetComponentInChildren<Renderer>();
            if (settle != null)
            {
                var b = settle.bounds;
                foreach (var r in tool.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(r.bounds);
                tool.transform.position += new Vector3(0f, position.y - b.min.y, 0f);
            }

            // The pickup needs its own collider for the interaction ray, since the mesh's own
            // colliders are stripped above. Generous, and lifted to sit around the item rather than
            // half-sunk through the floor — a small item flat on the ground is otherwise very hard
            // to put a crosshair on.
            var trigger = tool.AddComponent<BoxCollider>();
            trigger.size = new Vector3(0.4f, 0.35f, 0.9f);
            trigger.center = new Vector3(0f, 0.14f, 0f);

            tool.AddComponent<NetworkObject>();
            var pickup = tool.AddComponent<NetworkedPickup>();
            SetString(pickup, "itemId", itemId);
            SetString(pickup, "displayName", displayName);
            return tool;
        }

        /// <summary>F3 controls reference — hidden until asked for.</summary>
        public static void CreateControlsPanelUI(Transform canvas)
        {
            var container = CreateRect("ControlsPanel", canvas, new Vector2(0.5f, 0.5f), new Vector2(640f, 520f), Vector2.zero);
            var group = container.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            var backing = CreateRect("Backing", container, new Vector2(0.5f, 0.5f), new Vector2(640f, 520f), Vector2.zero);
            var backingImage = backing.gameObject.AddComponent<Image>();
            backingImage.color = new Color(0.02f, 0.02f, 0.03f, 0.93f);

            var textRect = CreateRect("Body", container, new Vector2(0.5f, 0.5f), new Vector2(580f, 470f), Vector2.zero);
            var body = CreateText(textRect, "Text", 18f, TextAlignmentOptions.TopLeft);
            StretchToParent((RectTransform)body.transform);
            body.color = new Color(0.86f, 0.85f, 0.8f);

            var panel = container.gameObject.AddComponent<ControlsPanelUI>();
            SetRef(panel, "group", group);
            SetRef(panel, "body", body);
        }

        /// <summary>
        /// Just the exit hint while hidden — no screen overlay.
        ///
        /// An earlier version drew dark borders closing in from each edge, which stacked a second,
        /// fake letterbox on top of the real occlusion from the wardrobe slats and bed frame. Two
        /// competing frames read as a bug, not as enclosure. The geometry already tells the player
        /// they're inside something; the UI's only job is naming the way out, which matters because
        /// interaction targeting is suspended while hidden.
        /// </summary>
        public static void CreateHidingOverlayUI(Transform canvas)
        {
            var container = CreateRect("HidingOverlay", canvas, new Vector2(0.5f, 0f), new Vector2(520f, 34f), new Vector2(0f, 118f));
            var group = container.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var hint = CreateText(container, "Text", 20f, TextAlignmentOptions.Center);
            StretchToParent((RectTransform)hint.transform);
            hint.color = new Color(0.78f, 0.76f, 0.7f);

            var overlay = container.gameObject.AddComponent<HidingOverlayUI>();
            SetRef(overlay, "group", group);
            SetRef(overlay, "hint", hint);
        }

        /// <summary>Discovery meter — a thin bar low on the screen, hidden until something sees you.</summary>
        public static void CreateDiscoveryMeterUI(Transform canvas)
        {
            var container = CreateRect("DiscoveryMeter", canvas, new Vector2(0.5f, 0f), new Vector2(260f, 10f), new Vector2(0f, 90f));
            var group = container.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var backing = CreateRect("Backing", container, new Vector2(0.5f, 0.5f), new Vector2(260f, 10f), Vector2.zero);
            var backingImage = backing.gameObject.AddComponent<Image>();
            backingImage.color = new Color(0.05f, 0.05f, 0.06f, 0.75f);

            var fillRect = CreateRect("Fill", container, new Vector2(0.5f, 0.5f), new Vector2(256f, 6f), Vector2.zero);
            var fillImage = fillRect.gameObject.AddComponent<Image>();
            fillImage.color = new Color(0.85f, 0.78f, 0.35f);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillAmount = 0f;

            var meter = container.gameObject.AddComponent<DiscoveryMeterUI>();
            SetRef(meter, "group", group);
            SetRef(meter, "fill", fillImage);
        }

        /// <summary>
        /// Sibling index + name at every level. The index disambiguates the duplicate names this
        /// level has (two "Pickup_Fuse", several "Slat"), and is deterministic because the build
        /// script always creates objects in the same order.
        /// </summary>
        private static string StableHierarchyPath(Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (var cursor = t; cursor != null; cursor = cursor.parent)
                parts.Add($"{cursor.GetSiblingIndex():D4}:{cursor.name}");
            parts.Reverse();
            return string.Join("/", parts);
        }

        public static void CreateBootstrapCamera(Vector3 position)
        {
            var camGO = new GameObject("MenuCamera");
            camGO.transform.position = position;
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.01f, 0.02f);
            // Without this, Unity logs "There are no audio listeners in the scene" every frame
            // while sitting in the connect menu — the only other listener lives on the player
            // camera, which doesn't exist until you host/join. BootstrapCamera disables it again
            // the moment the local player spawns, so the two listeners never overlap.
            camGO.AddComponent<AudioListener>();
            camGO.AddComponent<BootstrapCamera>();
        }

        public static void FixSceneNetworkObjectHashes()
        {
            // Every in-scene NetworkObject needs a unique, non-zero GlobalObjectIdHash, and this
            // scene is built entirely from code — which is exactly the case NGO does NOT cover.
            //
            // NetworkObject.OnValidate regenerates the hash from GlobalObjectId, but only when the
            // Editor validates the component: on import, or an inspector edit. A NetworkObject added
            // with AddComponent during a build is never validated before the scene is saved, so it
            // keeps the default 0. Several of those collide, and the host dies on start with
            // "already contains the same GlobalObjectIdHash value 0". So this assignment is load-
            // bearing, not redundant — it was briefly removed on the assumption OnValidate covered
            // it, and hosting broke immediately.
            //
            // Sorted by hierarchy path before numbering, because FindObjectsByType returns an
            // ARBITRARY order. Unsorted, two machines that each rebuild assign different ids to the
            // same object, and the client then fails with "NetworkPrefab hash was not found!
            // In-Scene placed NetworkObject soft synchronization failure". Sorting makes a rebuild
            // reproducible — though the safe workflow is still one person building and committing
            // the scene for everyone else to pull.
            var netObjs = UnityObject.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include);
            System.Array.Sort(netObjs, (a, b) =>
                string.CompareOrdinal(StableHierarchyPath(a.transform), StableHierarchyPath(b.transform)));

            uint hash = 0x1001;
            foreach (var netObj in netObjs)
            {
                var so = new SerializedObject(netObj);
                var prop = so.FindProperty("GlobalObjectIdHash");
                if (prop == null)
                {
                    Debug.LogWarning("NetworkObject has no serialized GlobalObjectIdHash field — NGO API may have changed.");
                    continue;
                }
                prop.uintValue = hash++;
                so.ApplyModifiedProperties();
            }
            Debug.Log($"[Build] Assigned unique scene hashes to {netObjs.Length} NetworkObjects.");
        }

        public static void CreateNetworkManager(GameObject playerPrefab)
        {
            var nmGO = new GameObject("NetworkManager");
            var nm = nmGO.AddComponent<NetworkManager>();
            var transport = nmGO.AddComponent<UnityTransport>();

            // The default queue (128 packets) overflows the moment a client joins: this scene holds
            // a lot of in-scene NetworkObjects — every door, pickup, container, puzzle and aftermath
            // anchor — and they all synchronize in one burst, which floods the send/receive queues.
            //
            // Written through SerializedObject, NOT by assigning the property. A plain assignment
            // changes the in-memory backing field without telling Unity the object is dirty, so
            // SaveScene writes the default straight back over it — which is why the error kept
            // reporting the stock 128 after it was "fixed".
            SetInt(transport, "m_MaxPacketQueueSize", 512);

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
            => CreateFusePuzzle(doorName, doorPosition, breakerBoxPosition,
                fusePickup1Position, fusePickup2Position, orderNotePosition, out _);

        /// <summary>
        /// Same puzzle, but hands back the two fuse pickups so a caller can hook them up to a
        /// <see cref="ClueSpawnShuffler"/> and randomise where they spawn.
        /// </summary>
        public static NetworkedDoor CreateFusePuzzle(
            string doorName, Vector3 doorPosition,
            Vector3 breakerBoxPosition,
            Vector3 fusePickup1Position, Vector3 fusePickup2Position,
            Vector3 orderNotePosition,
            out GameObject[] fusePickups)
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

            fusePickups = new[]
            {
                CreateFusePickup(fusePickup1Position),
                CreateFusePickup(fusePickup2Position),
            };

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

        public static GameObject CreateFusePickup(Vector3 position)
        {
            var fuse = CreateBox("Pickup_Fuse", position, new Vector3(0.12f, 0.12f, 0.3f));
            SetMaterial(fuse, MakeEmissive(new Color(0.5f, 0.3f, 0.05f), new Color(0.9f, 0.55f, 0.1f)));
            fuse.AddComponent<NetworkObject>();
            var pickup = fuse.AddComponent<NetworkedPickup>();
            SetString(pickup, "itemId", "fuse");
            SetString(pickup, "displayName", "fuse");
            return fuse;
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

        // --- M7 aftermath/body-message system ---

        /// <summary>Builds (or reuses, if already created) the 3 template prefabs + their
        /// AftermathTemplateDefinition assets. Idempotent — safe to call from every scene builder;
        /// each scene gets its own AftermathManager instance but they all share these same assets.</summary>
        public static AftermathTemplateDefinition[] CreateAftermathTemplates()
        {
            var warningPrefab = GetOrBuildAftermathPrefab("Assets/_Project/Prefabs/Aftermath/Aftermath_Warning.prefab",
                () => BuildAftermathBody("Aftermath_Warning", new Color(0.12f, 0.05f, 0.05f), "NOT THIS WAY", new Color(0.9f, 0.2f, 0.2f)));
            var falseCluePrefab = GetOrBuildAftermathPrefab("Assets/_Project/Prefabs/Aftermath/Aftermath_FalseClue.prefab",
                () => BuildAftermathBody("Aftermath_FalseClue", new Color(0.08f, 0.08f, 0.14f), "(scattered belongings)", new Color(0.5f, 0.55f, 0.7f)));
            var mockeryPrefab = GetOrBuildAftermathPrefab("Assets/_Project/Prefabs/Aftermath/Aftermath_Mockery.prefab",
                () => BuildAftermathBody("Aftermath_Mockery", new Color(0.1f, 0.05f, 0.12f), "THEY READ TOO MUCH", new Color(0.75f, 0.4f, 0.85f)));

            // FalseClue only past tier 2 (needs trust built first, per the plan); Mockery is rarer
            // (lower weight) than a plain Warning.
            var warning = GetOrCreateAftermathDef("Assets/_Project/Data/AftermathWarning.asset", "aftermath_warning", AftermathType.Warning, 0, -1, 1f, warningPrefab);
            var falseClue = GetOrCreateAftermathDef("Assets/_Project/Data/AftermathFalseClue.asset", "aftermath_false_clue", AftermathType.FalseClue, 2, -1, 1f, falseCluePrefab);
            var mockery = GetOrCreateAftermathDef("Assets/_Project/Data/AftermathMockery.asset", "aftermath_mockery", AftermathType.Mockery, 0, -1, 0.6f, mockeryPrefab);

            return new[] { warning, falseClue, mockery };
        }

        public static void CreateAftermathManager(AftermathTemplateDefinition[] templates)
        {
            // Plain MonoBehaviour, not a NetworkObject — see AftermathManager's doc comment for why
            // (prefab registration must happen before networking starts, in Start(), not on network spawn).
            var go = new GameObject("AftermathManager");
            var manager = go.AddComponent<AftermathManager>();
            SetRefArray(manager, "templates", templates);
        }

        public static void CreateAftermathAnchor(Vector3 position)
        {
            var go = new GameObject("AftermathAnchor");
            go.transform.position = position;
            go.AddComponent<AftermathAnchor>();
        }

        private static GameObject GetOrBuildAftermathPrefab(string path, System.Func<GameObject> build)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var instance = build();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            UnityObject.DestroyImmediate(instance);
            return prefab;
        }

        // Greybox-only: a lying "shrouded form" box + a floating flavor-text label. Real posed
        // art/animation is M8; this proves the mechanic (right template, right place, all clients
        // see it) rather than the fiction's visual fidelity.
        private static GameObject BuildAftermathBody(string name, Color bodyColor, string flavorText, Color textColor)
        {
            var root = new GameObject(name);
            root.AddComponent<NetworkObject>();

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform);
            body.transform.localPosition = new Vector3(0f, 0.18f, 0f);
            body.transform.localScale = new Vector3(0.6f, 0.35f, 1.8f);
            SetMaterial(body, MakeMaterial(bodyColor));

            var textGO = new GameObject("FlavorText", typeof(RectTransform));
            textGO.transform.SetParent(root.transform);
            textGO.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            var tmp = textGO.AddComponent<TextMeshPro>();
            tmp.text = flavorText;
            tmp.fontSize = 3f;
            tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
            ((RectTransform)tmp.transform).sizeDelta = new Vector2(4f, 1f);

            return root;
        }

        private static AftermathTemplateDefinition GetOrCreateAftermathDef(string path, string id, AftermathType type, int minTier, int maxTier, float weight, GameObject prefab)
        {
            var def = AssetDatabase.LoadAssetAtPath<AftermathTemplateDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<AftermathTemplateDefinition>();
                def.templateId = id;
                def.type = type;
                def.minAggressionTier = minTier;
                def.maxAggressionTier = maxTier;
                def.selectionWeight = weight;
                def.scenePrefab = prefab;
                AssetDatabase.CreateAsset(def, path);
                AssetDatabase.SaveAssets();
            }
            return def;
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
            // The note is a flat sheet lying face-up, not a slab standing on its edge in mid-air.
            // Callers pass the surface it rests on — a table top, a shelf, the floor — and the sheet
            // is laid a couple of millimetres above it.
            var root = new GameObject(name);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, (name.GetHashCode() % 90) - 45f, 0f);

            var letters = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Props/Letters/scene.gltf");
            bool built = false;
            if (letters != null)
            {
                var instance = (GameObject)UnityObject.Instantiate(letters, root.transform);
                instance.name = "Model";
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                    UnityObject.DestroyImmediate(collider);
                // The pack holds several sheets; keep one so each note is a single page.
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                for (int i = 1; i < renderers.Length; i++)
                    if (renderers[i] != null) UnityObject.DestroyImmediate(renderers[i].gameObject);

                if (instance.GetComponentInChildren<Renderer>(true) != null)
                {
                    ArtKit.AutoTexture(instance, "Props/Letters/textures");
                    // NOT NormalizeViewModel — that turns a model's longest axis to face forward,
                    // which stands a sheet of paper on its edge like a wedge. A note just needs
                    // scaling and laying flat where it was put.
                    ArtKit.FitLongest(instance, 0.3f);
                    ArtKit.GroundAt(instance, position);
                    built = true;
                }
                else UnityObject.DestroyImmediate(instance);
            }

            if (!built)
            {
                var sheet = GameObject.CreatePrimitive(PrimitiveType.Cube);
                sheet.name = "Model";
                sheet.transform.SetParent(root.transform, false);
                sheet.transform.localScale = new Vector3(0.28f, 0.006f, 0.36f);   // lying flat
                sheet.transform.localPosition = new Vector3(0f, 0.003f, 0f);
                UnityObject.DestroyImmediate(sheet.GetComponent<Collider>());
                SetMaterial(sheet, MakeEmissive(new Color(0.8f, 0.78f, 0.65f), new Color(0.3f, 0.29f, 0.24f)));
            }

            // Interaction target — generous enough to click a thin sheet on a table.
            var trigger = root.AddComponent<BoxCollider>();
            trigger.size = new Vector3(0.36f, 0.14f, 0.42f);
            trigger.center = new Vector3(0f, 0.07f, 0f);

            var note = root.AddComponent<NoteInteractable>();
            SetRef(note, "clue", clue);
            return root.transform;
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
            CreateExitWarningUI(canvasGO);
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

        public static void CreateExitWarningUI(GameObject canvasGO)
        {
            var root = CreateRect("ExitWarning", canvasGO.transform, new Vector2(0.5f, 0.25f), new Vector2(700f, 50f), Vector2.zero);
            var label = CreateText(root, "Label", 24f, TextAlignmentOptions.Center);
            StretchToParent((RectTransform)label.transform);
            label.color = new Color(1f, 0.85f, 0.5f);
            root.gameObject.SetActive(false);

            var warning = canvasGO.AddComponent<ExitWarningUI>();
            SetRef(warning, "root", root.gameObject);
            SetRef(warning, "label", label);
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
