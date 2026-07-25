#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace LastWard.EditorTools
{
    /// <summary>
    /// Helpers for the M8 art pass. The rule here is <b>measure, don't guess</b>: downloaded packs
    /// arrive at wildly different unit scales (some authored in cm, some in m) with pivots at the
    /// mesh centre, a corner, or the origin of whatever showcase scene they were exported from.
    /// Hardcoding a scale/offset per model is how you end up with an Entity taller than the roof
    /// and a car rising out of the floor — so every placement helper below derives its numbers from
    /// the model's real renderer bounds instead.
    ///
    /// The other trap these helpers exist to avoid: several packs ship as a <i>showcase scene</i>
    /// (one .gltf holding 50+ unrelated meshes laid out side by side), not as a single prop.
    /// Instantiating one of those is not "placing a tree", it's placing the entire pack. Use
    /// <see cref="SplitIntoProps"/> or <see cref="ExtractProp"/> to pull individual items out first.
    /// </summary>
    public static class ArtKit
    {
        public const string ArtRoot = "Assets/_Project/Art/";
        private const string MaterialFolder = "Assets/_Project/Materials/Art";

        // --- loading ---

        public static GameObject LoadModel(string relPath)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ArtRoot + relPath);
            if (go == null) Debug.LogWarning($"[ArtPass] Model not found: {ArtRoot}{relPath} — skipping.");
            return go;
        }

        /// <summary>Plain (non-prefab-linked) instance so children can be freely re-parented.</summary>
        public static GameObject Spawn(GameObject model, Transform parent, string name)
        {
            if (model == null) return null;
            var inst = (GameObject)UnityObject.Instantiate(model, parent);
            inst.name = name;
            StripColliders(inst);
            return inst;
        }

        /// <summary>
        /// Decoration must never contribute physics or NavMesh geometry — the greybox boxes stay
        /// authoritative for both, so anything imported gets its colliders removed on the way in.
        /// </summary>
        public static void StripColliders(GameObject go)
        {
            if (go == null) return;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) UnityObject.DestroyImmediate(c);
        }

        /// <summary>
        /// Gives a placed prop physical presence: one fitted BoxCollider on its root, plus a carving
        /// NavMeshObstacle once it is big enough to matter.
        ///
        /// Imported colliders are still stripped on the way in (see <see cref="StripColliders"/>) -
        /// mesh colliders off downloaded art are unpredictable and expensive. A single measured box
        /// is cheap, behaves, and is derived from the model's real bounds rather than guessed.
        ///
        /// The NavMeshObstacle is the half that stops the Entity standing inside the furniture: the
        /// NavMesh is baked before the art pass runs, so the bake knows nothing about any of this.
        /// Carving lets a prop cut the walkable surface at runtime instead, with no re-bake.
        /// </summary>
        public static void MakeSolid(GameObject go, float minObstacleSize = 0.45f)
        {
            if (go == null) return;
            if (!TryGetBounds(go, out var b) || b.size.sqrMagnitude < 0.0001f) return;

            // Bounds are world-space; the collider is local, so convert through the transform.
            var box = go.GetComponent<BoxCollider>();
            if (box == null) box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            Vector3 ls = go.transform.lossyScale;
            box.size = new Vector3(
                b.size.x / Mathf.Max(0.0001f, Mathf.Abs(ls.x)),
                b.size.y / Mathf.Max(0.0001f, Mathf.Abs(ls.y)),
                b.size.z / Mathf.Max(0.0001f, Mathf.Abs(ls.z)));

            // Only things you could actually walk into are worth carving the NavMesh for. Doing it
            // for every bucket and picture frame would be a lot of carving for no behaviour change.
            float footprint = Mathf.Max(b.size.x, b.size.z);
            if (footprint < minObstacleSize || b.size.y < 0.3f) return;

            var obstacle = go.GetComponent<UnityEngine.AI.NavMeshObstacle>();
            if (obstacle == null) obstacle = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            obstacle.center = box.center;
            obstacle.size = box.size;
            obstacle.carving = true;
        }

        // --- measuring / fitting ---

        public static bool TryGetBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            if (go == null) return false;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var rb = r.bounds;
                // A SkinnedMeshRenderer instantiated in the Editor can report empty bounds until
                // it has been driven once, which would make every Fit* below divide by ~0 and
                // produce an absurd scale. Fall back to the mesh's own bounds in that case.
                if (rb.size.sqrMagnitude < 0.0000001f && r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                {
                    var local = skinned.sharedMesh.bounds;
                    rb = new Bounds(r.transform.TransformPoint(local.center), Vector3.Scale(local.size, r.transform.lossyScale));
                }
                if (!any) { bounds = rb; any = true; }
                else bounds.Encapsulate(rb);
            }
            return any;
        }

        /// <summary>Uniformly rescale so the model stands exactly <paramref name="targetHeight"/> tall.</summary>
        public static void FitHeight(GameObject go, float targetHeight)
        {
            if (!TryGetBounds(go, out var b) || b.size.y <= 0.0001f) return;
            go.transform.localScale *= targetHeight / b.size.y;
        }

        /// <summary>Uniformly rescale so the model's longest axis matches <paramref name="targetSize"/>.</summary>
        public static void FitLongest(GameObject go, float targetSize)
        {
            if (!TryGetBounds(go, out var b)) return;
            float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (longest <= 0.0001f) return;
            go.transform.localScale *= targetSize / longest;
        }

        /// <summary>
        /// Places the model so its <i>bottom centre</i> sits exactly on <paramref name="worldPos"/> —
        /// i.e. standing on the floor at that spot, regardless of where its pivot happens to be.
        /// Call after any Fit* so the scale is already final.
        /// </summary>
        public static void GroundAt(GameObject go, Vector3 worldPos)
        {
            if (!TryGetBounds(go, out var b)) { if (go != null) go.transform.position = worldPos; return; }
            var bottomCentre = new Vector3(b.center.x, b.min.y, b.center.z);
            go.transform.position += worldPos - bottomCentre;
        }

        /// <summary>
        /// Cancels a non-uniform parent scale so a child mesh isn't stretched by it. The Entity
        /// capsule, for example, is scaled (0.9, 1.15, 0.9) — parenting a character model under it
        /// unchanged would squash it horizontally and stretch it vertically.
        /// </summary>
        public static void NeutralizeParentScale(GameObject go)
        {
            if (go == null || go.transform.parent == null) return;
            var p = go.transform.parent.lossyScale;
            if (Mathf.Abs(p.x) < 0.0001f || Mathf.Abs(p.y) < 0.0001f || Mathf.Abs(p.z) < 0.0001f) return;
            go.transform.localScale = new Vector3(1f / p.x, 1f / p.y, 1f / p.z);
        }

        /// <summary>
        /// Turns a character model into a near-black silhouette with two glowing eyes — the single
        /// biggest quality lever for a free-asset creature. An unlit dark body hides every scrap of
        /// texture detail and reads instantly at distance; two bright unlit spheres are the only
        /// thing the dark gives back. It makes the Manager unmistakably NOT the Receptionist without
        /// depending on the model being good.
        ///
        /// Eyes are parented to the root (scale 1), positioned from the model's bounds toward its
        /// FORWARD face. If they come out on the back of its head, the spawn is facing the wrong way
        /// — flip the root's yaw, do not fight it here.
        /// </summary>
        public static void MakeSilhouetteWithEyes(GameObject visual, Color eyeColor, float eyeDiameter = 0.07f)
        {
            if (visual == null) return;

            var body = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            body.SetColor("_BaseColor", new Color(0.015f, 0.015f, 0.02f));  // not pure #000, which reads as a hole
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
                if (r != null) r.sharedMaterial = body;

            if (!TryGetBounds(visual, out var b)) return;

            var eyeMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            eyeMat.SetColor("_BaseColor", eyeColor);   // unlit => always full colour = glows in the dark

            // Anchor to the HEAD BONE where the rig has one. Deriving eye placement from the whole
            // model's bounding box put them at the width of the SHOULDERS and well in front of the
            // body — two dots hanging in mid-air beside its head. A head bone also means they track
            // the skull through every animation instead of staying pinned to the root.
            Transform head = null;
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (!n.Contains("head") || n.Contains("headtop") || n.Contains("_end")) continue;
                head = t;
                break;
            }

            // Parented to the ROOT, not the head bone. The bone is driven by the Animator, which the
            // stutter driver hand-steps at 8fps while the body travels at full rate — so eyes hung on
            // the bone visibly lagged behind and appeared to hang in the air as it moved. The root is
            // what actually carries the creature through the world, so they stay welded to it.
            Transform anchor = visual.transform.parent != null ? visual.transform.parent : visual.transform;
            Transform facingRef = visual.transform.parent != null ? visual.transform.parent : visual.transform;

            // Sized off the model, not the skeleton: bone scales in imported rigs are unreliable.
            float headY = head != null ? head.position.y + b.size.y * 0.04f : b.max.y - b.size.y * 0.10f;
            Vector3 centreOfFace = head != null
                ? new Vector3(head.position.x, headY, head.position.z)
                : new Vector3(b.center.x, headY, b.center.z);

            float spacing = Mathf.Max(0.035f, b.size.x * 0.055f);   // eyes, not shoulders
            float forward = Mathf.Max(0.06f, b.size.z * 0.16f);     // just proud of the face
            float scaleInAnchor = eyeDiameter / Mathf.Max(0.0001f, anchor.lossyScale.x);

            foreach (float sgn in new[] { -1f, 1f })
            {
                var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eye.name = "Eye";
                UnityObject.DestroyImmediate(eye.GetComponent<Collider>());
                eye.transform.position = centreOfFace
                    + facingRef.forward * forward
                    + facingRef.right * (sgn * spacing);
                eye.transform.SetParent(anchor, true);   // keep world pose, then ride the head
                eye.transform.localScale = Vector3.one * scaleInAnchor;
                eye.GetComponent<Renderer>().sharedMaterial = eyeMat;
            }
        }

        // --- pulling individual props out of multi-prop packs ---

        /// <summary>
        /// Collects every descendant whose name starts with any of <paramref name="namePrefixes"/>
        /// into one standalone GameObject (world positions preserved). Use when the pack's object
        /// names are known, so a specific item can be picked out by name.
        /// </summary>
        public static GameObject ExtractProp(GameObject pack, string propName, Transform parent, params string[] namePrefixes)
        {
            if (pack == null) return null;
            var matches = new List<Transform>();
            foreach (var t in pack.GetComponentsInChildren<Transform>(true))
            {
                if (t == pack.transform) continue;
                foreach (var prefix in namePrefixes)
                {
                    if (t.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(t);
                        break;
                    }
                }
            }
            if (matches.Count == 0)
            {
                Debug.LogWarning($"[ArtPass] No objects named {string.Join("/", namePrefixes)} found in '{pack.name}' — skipping '{propName}'.");
                return null;
            }

            var root = new GameObject(propName);
            root.transform.SetParent(parent, false);
            // Skip any match that's already a descendant of another match, or re-parenting would
            // pull it back out of the group it just travelled with.
            foreach (var t in matches)
            {
                if (t == null) continue;
                bool nestedUnderAnotherMatch = false;
                foreach (var other in matches)
                    if (other != t && other != null && t.IsChildOf(other)) { nestedUnderAnotherMatch = true; break; }
                if (!nestedUnderAnotherMatch) t.SetParent(root.transform, true);
            }
            return root;
        }

        /// <summary>
        /// Splits a showcase-scene pack into standalone props by grouping meshes that overlap in
        /// plan view (their XZ footprints touch, within <paramref name="mergeRadius"/>). Works
        /// without knowing any object names — a tree's trunk and its foliage planes sit on top of
        /// each other so they group, while the next tree along is a separate island.
        /// Returned tallest-first, so callers can take the big ones as trees.
        /// </summary>
        public static List<GameObject> SplitIntoProps(GameObject pack, Transform parent, float mergeRadius = 0.35f)
        {
            var result = new List<GameObject>();
            if (pack == null) return result;

            var renderers = pack.GetComponentsInChildren<Renderer>(true);
            int n = renderers.Length;
            if (n == 0) return result;

            var group = new int[n];
            for (int i = 0; i < n; i++) group[i] = i;
            int Find(int a) { while (group[a] != a) { group[a] = group[group[a]]; a = group[a]; } return a; }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    var bi = renderers[i].bounds;
                    var bj = renderers[j].bounds;
                    bool overlapX = Mathf.Abs(bi.center.x - bj.center.x) <= bi.extents.x + bj.extents.x + mergeRadius;
                    bool overlapZ = Mathf.Abs(bi.center.z - bj.center.z) <= bi.extents.z + bj.extents.z + mergeRadius;
                    if (!overlapX || !overlapZ) continue;
                    int ri = Find(i), rj = Find(j);
                    if (ri != rj) group[rj] = ri;
                }
            }

            var buckets = new Dictionary<int, List<Transform>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!buckets.TryGetValue(root, out var list)) buckets[root] = list = new List<Transform>();
                list.Add(renderers[i].transform);
            }

            int index = 0;
            foreach (var bucket in buckets.Values)
            {
                var go = new GameObject($"{pack.name}_Prop_{index++}");
                go.transform.SetParent(parent, false);
                foreach (var t in bucket)
                    if (t != null) t.SetParent(go.transform, true);
                result.Add(go);
            }

            result.Sort((a, b) =>
            {
                TryGetBounds(a, out var ba);
                TryGetBounds(b, out var bb);
                return bb.size.y.CompareTo(ba.size.y);
            });
            return result;
        }

        // --- materials ---

        /// <summary>
        /// Builds (and caches on disk) a flat URP material for a PSX-style texture. FBX imports
        /// don't wire their textures up automatically, so without this the props render untextured
        /// white. Smoothness is zeroed — these textures are already fully shaded by hand, and any
        /// specular highlight on top just reads as "wrong colour".
        /// </summary>
        public static Material MakeTexturedMaterial(string texRelPath, string materialName, bool alphaClip)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtRoot + texRelPath);
            if (tex == null)
            {
                Debug.LogWarning($"[ArtPass] Texture not found: {ArtRoot}{texRelPath}");
                return null;
            }
            ApplyPsxImportSettings(ArtRoot + texRelPath);
            if (alphaClip) EnsureAlphaIsTransparency(ArtRoot + texRelPath);
            return MakeMaterialFromTexture(tex, materialName, alphaClip);
        }

        public static Material MakeTexturedMaterial(string texRelPath, string materialName)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtRoot + texRelPath);
            if (tex == null)
            {
                Debug.LogWarning($"[ArtPass] Texture not found: {ArtRoot}{texRelPath}");
                return null;
            }
            ApplyPsxImportSettings(ArtRoot + texRelPath);

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Project/Materials"))
                    AssetDatabase.CreateFolder("Assets/_Project", "Materials");
                AssetDatabase.CreateFolder("Assets/_Project/Materials", "Art");
            }

            string path = $"{MaterialFolder}/{materialName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.mainTexture = tex;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Point filtering keeps the low-res PSX textures crisp instead of muddy.</summary>
        public static void ApplyPsxImportSettings(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer) return;
            if (importer.filterMode == FilterMode.Point) return;
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
        }

        public static void ApplyMaterial(GameObject go, Material mat)
        {
            if (go == null || mat == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
        }

        /// <summary>Applies a material only to descendants whose name starts with one of the prefixes.</summary>
        public static void ApplyMaterialToNamed(GameObject go, Material mat, params string[] namePrefixes)
        {
            if (go == null || mat == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var prefix in namePrefixes)
                {
                    if (!r.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) continue;
                    var mats = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                    break;
                }
            }
        }

        // --- automatic texturing for multi-material packs ---

        /// <summary>
        /// Textures a pack by matching each renderer against the texture files that shipped with it.
        /// These exports encode the material name into the object name — a mesh under a node called
        /// "Ps1Tree1Texture_24" belongs with "Ps1Tree1Texture_baseColor.png", and
        /// "L_Door_Front_Black_Car_Mat_0" with "Black_Car_Mat_baseColor.png" — so the two are paired
        /// by longest matching name, ignoring separators and the usual suffixes.
        ///
        /// A renderer with no confident match is left completely alone, keeping whatever the
        /// importer gave it. That matters for photogrammetry packs like the Riga building, whose
        /// nodes ("Object_2") carry no material name at all and whose imported materials are
        /// already correct.
        /// </summary>
        /// <param name="alphaClip">True for foliage: cut-out leaves render as solid slabs otherwise.</param>
        /// <param name="pointFilter">True for PSX pixel art. False for photogrammetry, where point
        /// sampling a photo texture just makes it look noisy.</param>
        public static void AutoTexture(GameObject root, string textureFolderRelPath, bool alphaClip = false, bool pointFilter = true)
        {
            if (root == null) return;
            string folder = (ArtRoot + textureFolderRelPath).TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning($"[ArtPass] Texture folder not found: {folder}");
                return;
            }

            var candidates = new List<(string key, Texture2D tex, string name)>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                // Only base colour drives the look; normal/metallic/roughness/AO maps would
                // otherwise win the name match and paint the mesh with a greyscale mask.
                string lower = file.ToLowerInvariant();
                if (lower.Contains("normal") || lower.Contains("metallic") || lower.Contains("roughness") ||
                    lower.Contains("emissive") || lower.Contains("specular") || lower.EndsWith("_ao"))
                    continue;

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;
                if (pointFilter) ApplyPsxImportSettings(path);
                if (alphaClip) EnsureAlphaIsTransparency(path);
                candidates.Add((NormalizeName(StripTextureSuffixes(file)), tex, file));
            }
            if (candidates.Count == 0) return;

            var cache = new Dictionary<Texture2D, Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                // The material name is the strongest signal available — importers name generated
                // materials after the source material ("Black_Car_Mat", "dzemdiibas_2_Material_u1_v1"),
                // which is exactly the stem of the matching texture file. Object names are only a
                // fallback for packs that don't carry it.
                var chain = new System.Text.StringBuilder(NameChain(r.transform, 3));
                foreach (var m in r.sharedMaterials)
                    if (m != null) chain.Append(m.name).Append('|');
                string haystack = NormalizeName(chain.ToString());
                Texture2D best = null;
                int bestLength = 0;
                foreach (var (key, tex, _) in candidates)
                {
                    if (key.Length < 3 || key.Length <= bestLength) continue;
                    if (haystack.Contains(key)) { best = tex; bestLength = key.Length; }
                }
                if (best == null)
                {
                    // No confident name match. A renderer whose material already has a texture is
                    // left completely alone. But one with no texture at all renders plain white,
                    // which against a night scene reads as the glaring white blobs — so those get a
                    // neutral dark stand-in instead.
                    bool alreadyTextured = false;
                    foreach (var m in r.sharedMaterials)
                        if (m != null && m.mainTexture != null) { alreadyTextured = true; break; }
                    if (alreadyTextured) continue;

                    var fallback = GetUntexturedFallback();
                    if (fallback == null) continue;
                    var fallbackSlots = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                    for (int i = 0; i < fallbackSlots.Length; i++) fallbackSlots[i] = fallback;
                    r.sharedMaterials = fallbackSlots;
                    continue;
                }

                if (!cache.TryGetValue(best, out var mat))
                {
                    cache[best] = mat = MakeMaterialFromTexture(best, $"M_{best.name}", alphaClip);
                    if (mat == null) continue;
                }
                var mats = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
        }

        private static string NameChain(Transform t, int depth)
        {
            var sb = new System.Text.StringBuilder();
            var cursor = t;
            for (int i = 0; i < depth && cursor != null; i++, cursor = cursor.parent)
                sb.Append(cursor.name).Append('|');
            return sb.ToString();
        }

        private static string NormalizeName(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        private static string StripTextureSuffixes(string file)
        {
            string[] suffixes = { "_baseColor", "_BaseColor", "_basecolor", "_diffuse", "_Diffuse", "_albedo", "_Albedo" };
            foreach (var s in suffixes)
                if (file.EndsWith(s, System.StringComparison.OrdinalIgnoreCase))
                    return file.Substring(0, file.Length - s.Length);
            return System.Text.RegularExpressions.Regex.Replace(file, @"_Tex(_\d+)?$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static void EnsureAlphaIsTransparency(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer) return;
            if (importer.alphaIsTransparency) return;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Textures a greybox box so the texture repeats at a real-world size instead of being
        /// stretched once across the whole surface. A Unity cube's UVs run 0–1 per face, so an 8m
        /// wall and a 1m crate would otherwise show the same texture at wildly different scales —
        /// which is what makes procedural rooms look like flat-shaded programmer art.
        ///
        /// Tiling is derived from the box's own scale, and materials are cached per
        /// texture+tiling combination so a corridor of same-sized walls shares one material rather
        /// than creating dozens.
        /// </summary>
        /// <param name="metresPerTile">World size one texture repeat covers. Smaller = busier.</param>
        public static void ApplyTiledMaterial(GameObject box, string texRelPath, float metresPerTile = 2f, Color? tint = null)
        {
            if (box == null) return;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtRoot + texRelPath);
            if (tex == null)
            {
                Debug.LogWarning($"[ArtPass] Texture not found: {ArtRoot}{texRelPath}");
                return;
            }
            ApplyPsxImportSettings(ArtRoot + texRelPath);
            EnsureRepeatWrap(ArtRoot + texRelPath);

            // The two largest dimensions are the face the player actually looks at.
            Vector3 size = box.transform.localScale;
            float a = Mathf.Max(size.x, size.z);
            float b = size.y > 0.5f ? size.y : Mathf.Min(size.x, size.z);
            var tiling = new Vector2(
                Mathf.Max(1f, Mathf.Round(a / metresPerTile)),
                Mathf.Max(1f, Mathf.Round(b / metresPerTile)));

            var color = tint ?? Color.white;
            string matName = $"M_{System.IO.Path.GetFileNameWithoutExtension(texRelPath)}_{tiling.x}x{tiling.y}" +
                             (tint.HasValue ? $"_{ColorUtility.ToHtmlStringRGB(color)}" : "");

            EnsureMaterialFolder();
            string path = $"{MaterialFolder}/{matName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) return;
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.mainTextureScale = tiling;
            EditorUtility.SetDirty(mat);

            SetMaterialOn(box, mat);
        }

        private static void SetMaterialOn(GameObject go, Material mat)
        {
            if (go.TryGetComponent<Renderer>(out var renderer)) renderer.sharedMaterial = mat;
        }

        // Tiling only works if the texture repeats rather than clamping at its edges.
        private static void EnsureRepeatWrap(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer) return;
            if (importer.wrapMode == TextureWrapMode.Repeat) return;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Rotates a model so its LONGEST axis points up. Exporters disagree about which axis is
        /// "up" — the retro tree pack is authored Z-up, so its trees measure 1 × 1 × 2.2 and
        /// FitHeight was scaling the trunk's *width* to the target, producing giant slabs floating
        /// off the ground. Standing it up first makes every later measurement mean what it says.
        /// </summary>
        public static void StandUpright(GameObject go)
        {
            if (!TryGetBounds(go, out var b)) return;
            var s = b.size;
            if (s.y >= s.x && s.y >= s.z) return;                       // already tallest on Y
            if (s.z > s.x) go.transform.Rotate(-90f, 0f, 0f, Space.World);  // Z is up -> Y
            else go.transform.Rotate(0f, 0f, 90f, Space.World);             // X is up -> Y
        }

        /// <summary>
        /// Rotates a flat object so it lies face-up. The letter sheets measure 1.9 × 2.0 × 0.003 —
        /// thinnest on Z, i.e. a vertical plane — so notes stood on their edge like headstones
        /// until this ran.
        /// </summary>
        public static void LayFlat(GameObject go)
        {
            if (!TryGetBounds(go, out var b)) return;
            var s = b.size;
            if (s.y <= s.x && s.y <= s.z) return;                       // already thinnest on Y
            if (s.z < s.x) go.transform.Rotate(90f, 0f, 0f, Space.World);   // Z normal -> Y
            else go.transform.Rotate(0f, 0f, 90f, Space.World);             // X normal -> Y
        }

        /// <summary>First descendant whose name starts with <paramref name="namePrefix"/>, or null.</summary>
        public static Transform FindDescendant(GameObject root, string namePrefix)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(namePrefix, System.StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        /// <summary>
        /// Turns a model so a named feature on it faces <paramref name="worldFacing"/>.
        ///
        /// Far more reliable than inferring "front" from the bounding box. A wall-mounted fuse box's
        /// largest axis is its open door swinging sideways, not its depth — so a bounds guess turned
        /// it to face the wall and stick out horizontally. Aiming an actual feature (a fuse slot)
        /// uses the model's own semantics instead.
        /// </summary>
        public static void FaceFeatureTowards(GameObject root, string featurePrefix, Vector3 worldFacing)
        {
            var feature = FindDescendant(root, featurePrefix);
            if (feature == null || !TryGetBounds(root, out var bounds)) return;

            Vector3 outward = feature.position - bounds.center;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f) return;

            Vector3 want = worldFacing;
            want.y = 0f;
            if (want.sqrMagnitude < 0.0001f) return;

            root.transform.Rotate(0f, Vector3.SignedAngle(outward, want, Vector3.up), 0f, Space.World);
        }

        public static Material MakeMaterialFromTexture(Texture2D tex, string materialName, bool alphaClip = false)
        {
            if (tex == null) return null;
            EnsureMaterialFolder();

            string path = $"{MaterialFolder}/{materialName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.mainTexture = tex;
            if (alphaClip)
            {
                // Foliage is drawn as crossed quads with cut-out leaves; without this the
                // transparent parts stay opaque and every tree reads as a solid dark slab.
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Photogrammetry packs ship dozens of 4K maps — the Riga building alone is ~150MB, ~86MB of
        /// it textures. Uncompressed at full size that stalls scene load and play-mode entry long
        /// enough to look like the game has hung, because Unity decompresses and uploads it all on
        /// the main thread. These are seen from across a fogged yard at night, so capping and
        /// compressing them costs nothing visible.
        /// </summary>
        public static void CapTextureSize(string textureFolderRelPath, int maxSize)
        {
            string folder = (ArtRoot + textureFolderRelPath).TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(folder)) return;

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                bool oversized = importer.maxTextureSize > maxSize;
                bool uncompressed = importer.textureCompression == TextureImporterCompression.Uncompressed;
                if (!oversized && !uncompressed) continue;
                importer.maxTextureSize = maxSize;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// Materials created during a build are only in memory until the database is flushed — left
        /// unsaved they come back blank (plain white, no texture) after the next domain reload,
        /// which is one way props end up rendering as white silhouettes.
        /// </summary>
        public static void FlushAssets() => AssetDatabase.SaveAssets();

        /// <summary>Neutral dark stand-in for geometry we can't confidently texture.</summary>
        private static Material GetUntexturedFallback()
        {
            EnsureMaterialFolder();
            const string path = MaterialFolder + "/M_UntexturedFallback.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0.16f, 0.16f, 0.17f));
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void EnsureMaterialFolder()
        {
            if (AssetDatabase.IsValidFolder(MaterialFolder)) return;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Materials"))
                AssetDatabase.CreateFolder("Assets/_Project", "Materials");
            AssetDatabase.CreateFolder("Assets/_Project/Materials", "Art");
        }

        // --- animation ---

        /// <summary>
        /// Drives an imported model with the animation baked into its own FBX, so it doesn't just
        /// stand in the bind pose (the dead-giveaway T-shape). Root motion is disabled — the
        /// NavMeshAgent owns movement, and a clip fighting it would slide the model off course.
        /// </summary>
        /// <summary>
        /// Gets a model's import settings into a state where its baked animation can actually play,
        /// and <b>must run before the model is instantiated</b> — the instance copies its Animator
        /// and Avatar at spawn time, so fixing the rig afterwards leaves the live copy broken.
        ///
        /// Two traps here, both of which produced a permanent T-pose:
        /// 1. A Generic rig with <c>avatarSetup = NoAvatar</c> generates no Avatar, and generic
        ///    animation cannot be applied without one — the mesh just sits in its bind pose.
        /// 2. Writing <c>clipAnimations</c> from <c>defaultClipAnimations</c> before the rig is set
        ///    up bakes in whatever placeholder range was there — in practice a single frame — which
        ///    then IS the animation. So the rig is fixed and reimported first, and only then are the
        ///    (now real) defaults read back, with a sanity check on their length.
        /// </summary>
        public static void PrepareAnimatedModel(string modelRelPath)
        {
            string modelPath = ArtRoot + modelRelPath;
            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter importer) return;

            bool rigChanged = false;
            if (importer.animationType == ModelImporterAnimationType.None)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                rigChanged = true;
            }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                rigChanged = true;
            }
            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                rigChanged = true;
            }
            // Drop any explicit clip list a previous run may have baked; regenerating from the
            // source is the only way to recover a clip that was truncated to one frame.
            if (importer.clipAnimations != null && importer.clipAnimations.Length > 0)
            {
                importer.clipAnimations = new ModelImporterClipAnimation[0];
                rigChanged = true;
            }
            if (rigChanged) importer.SaveAndReimport();

            // Now that the rig is real, the defaults are trustworthy.
            var clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;

            bool loopChanged = false;
            foreach (var clip in clips)
            {
                // Guard against re-baking a degenerate range — a clip barely a frame long is the
                // symptom of the bug above, not something to mark as looping.
                if (clip.lastFrame - clip.firstFrame < 2f) continue;
                if (clip.loopTime) continue;
                clip.loopTime = true;
                loopChanged = true;
            }
            if (!loopChanged) return;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        public static void EnsureLoopingAnimator(GameObject target, string modelRelPath, string controllerName)
        {
            if (target == null) return;
            string modelPath = ArtRoot + modelRelPath;

            AnimationClip clip = FindAnimationClip(modelPath);
            if (clip == null)
            {
                Debug.LogWarning($"[ArtPass] No AnimationClip inside {modelRelPath} — model will stay in its bind pose.");
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Animations"))
                AssetDatabase.CreateFolder("Assets/_Project", "Animations");

            string controllerPath = $"Assets/_Project/Animations/{controllerName}.controller";
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPathWithClip(controllerPath, clip);
            }
            else
            {
                // Re-point the existing controller at the current clip every time. Reimporting the
                // FBX (which PrepareAnimatedModel does) can change the clip's internal fileID, and a
                // cached controller then holds a dangling motion reference — a state with no motion,
                // which plays nothing and shows the bind pose. Creating the controller only once was
                // the reason the T-pose survived the rig fix.
                RebindControllerClip(controller, clip);
            }

            // glTF imports (gltFast) can bring animation in as a Legacy clip, which an
            // AnimatorController cannot use at all — assigning it silently animates nothing. Those
            // need the matching legacy component instead.
            if (clip.legacy)
            {
                var legacy = target.GetComponent<Animation>();
                if (legacy == null) legacy = target.AddComponent<Animation>();
                legacy.AddClip(clip, clip.name);
                legacy.clip = clip;
                legacy.playAutomatically = true;
                legacy.wrapMode = WrapMode.Loop;
                Debug.Log($"[ArtPass] Animator on '{target.name}': LEGACY clip='{clip.name}' " +
                          $"length={clip.length:0.00}s frameRate={clip.frameRate}");
                return;
            }

            // Search the CHILDREN too. gltFast puts the Animator — and its generated Avatar — on
            // whichever node owns the skin, which is often a child of the instantiated root. Only
            // checking the root meant adding a SECOND, avatar-less Animator that shadowed the real
            // one, which is what "avatar=NONE valid=False" was reporting.
            var animator = target.GetComponent<Animator>() ?? target.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = target.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            // The NavMeshAgent owns movement; a clip driving the root as well would slide the model
            // off its own path.
            animator.applyRootMotion = false;
            // AlwaysAnimate, NOT CullUpdateTransforms: a skinned mesh that has been rescaled keeps
            // stale bounds, so Unity can decide it's offscreen and stop writing bone transforms,
            // freezing it in the bind pose. Recomputing the bounds covers the same risk.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var skinned in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skinned.updateWhenOffscreen = true;

            // Assigned explicitly rather than trusting the instantiated copy to have inherited it:
            // a generic rig with no Avatar plays nothing at all, and silently shows the bind pose.
            if (animator.avatar == null || !animator.avatar.isValid)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
                {
                    if (asset is not Avatar avatar || !avatar.isValid) continue;
                    animator.avatar = avatar;
                    break;
                }
            }
            if (animator.avatar == null)
                Debug.LogWarning($"[ArtPass] No valid Avatar on {modelRelPath} — the model will stay in its bind pose (T-shape).");

            // Reported explicitly because every previous diagnosis of the T-pose was a guess. If it
            // is still wrong after this, these three numbers say which link in the chain is broken.
            Debug.Log($"[ArtPass] Animator on '{target.name}': clip='{clip.name}' length={clip.length:0.00}s " +
                      $"frameRate={clip.frameRate} loop={clip.isLooping} | avatar={(animator.avatar != null ? animator.avatar.name : "NONE")} " +
                      $"valid={(animator.avatar != null && animator.avatar.isValid)} | controller='{controller.name}'");
        }

        /// <summary>
        /// Builds the Watcher's full locomotion + catch state machine from the four separately
        /// exported skeleton clips, and sets up the Animator on <paramref name="target"/> (avatar,
        /// culling). Structure:
        /// <list type="bullet">
        /// <item>A 1-D blend tree "Locomotion" (Idle=0 → Walk=1 → Run=2), driven by the float
        /// parameter of the same name — a distinct RUN clip rather than a sped-up walk, which is
        /// what made the old chase read as a shuffle.</item>
        /// <item>A one-shot "Catch" state entered from Any State on the "Catch" trigger, for the
        /// jumpscare's intimate finish. No exit transition: the encounter ends there.</item>
        /// </list>
        /// The avatar still comes from the mesh model (<paramref name="meshModelRel"/>); the clips
        /// carry only skeleton curves and retarget onto it by matching bone paths.
        /// </summary>
        public static void SetupEntityAnimator(GameObject target, string meshModelRel, string controllerName,
            string idleName, string walkName, string runName, string catchName)
        {
            if (target == null) return;
            string modelPath = ArtRoot + meshModelRel;

            // Clips are pulled BY NAME out of the same model file that provides the mesh and
            // skeleton. Previously they lived in separate skeleton-only .glb files and were expected
            // to retarget onto this model's avatar — they silently did not bind, so the Animator
            // played nothing at all and the Entity stood frozen in its bind pose. Same file means
            // the curves address their own skeleton and binding cannot fail.
            var idle = FindAnimationClipNamed(modelPath, idleName);
            var walk = FindAnimationClipNamed(modelPath, walkName);
            var run  = FindAnimationClipNamed(modelPath, runName);
            var catchClip = FindAnimationClipNamed(modelPath, catchName);
            if (idle == null || walk == null || run == null)
            {
                var found = new List<string>();
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(modelPath))
                    if (a is AnimationClip c && !c.name.StartsWith("__preview")) found.Add(c.name);
                Debug.LogWarning($"[ArtPass] Missing an idle/walk/run clip inside {meshModelRel}. " +
                    $"Clips actually present: [{string.Join(", ", found)}]. Falling back to a single clip.");
                EnsureLoopingAnimator(target, meshModelRel, controllerName);
                return;
            }
            Debug.Log($"[ArtPass] Watcher clips bound: idle='{idle.name}' walk='{walk.name}' run='{run.name}' " +
                      $"catch='{(catchClip != null ? catchClip.name : "NONE")}'");

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Animations"))
                AssetDatabase.CreateFolder("Assets/_Project", "Animations");
            string controllerPath = $"Assets/_Project/Animations/{controllerName}.controller";

            // Rebuilt from scratch every run: incrementally editing a cached controller is how stale
            // states and dangling motion refs accumulated before. The scene reference is reassigned
            // below, so a fresh asset GUID is fine.
            if (AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(controllerPath) != null)
                AssetDatabase.DeleteAsset(controllerPath);
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            if (controller.layers.Length == 0) controller.AddLayer("Base");
            var sm = controller.layers[0].stateMachine;

            controller.AddParameter("Locomotion", AnimatorControllerParameterType.Float);
            controller.AddParameter("Catch", AnimatorControllerParameterType.Trigger);

            var tree = new UnityEditor.Animations.BlendTree
            {
                name = "Locomotion",
                blendType = UnityEditor.Animations.BlendTreeType.Simple1D,
                blendParameter = "Locomotion",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1f);
            tree.AddChild(run, 2f);

            var locoState = sm.AddState("Locomotion");
            locoState.motion = tree;
            sm.defaultState = locoState;

            if (catchClip != null)
            {
                var catchState = sm.AddState("Catch");
                catchState.motion = catchClip;
                var toCatch = sm.AddAnyStateTransition(catchState);
                toCatch.hasExitTime = false;
                toCatch.duration = 0.12f;
                toCatch.canTransitionToSelf = false;
                toCatch.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Catch");
            }

            EditorUtility.SetDirty(controller);
            SetupAnimatorComponent(target, ArtRoot + meshModelRel, controller);
        }

        /// <summary>
        /// Shared Animator-component wiring: assigns the controller, pulls a valid Avatar off the
        /// mesh model, and forces always-animate + offscreen updates so a rescaled skinned mesh with
        /// stale bounds can't freeze in its bind pose. Factored out of EnsureLoopingAnimator so the
        /// multi-clip entity path reuses exactly the same hard-won setup.
        /// </summary>
        private static void SetupAnimatorComponent(GameObject target, string modelPath,
            UnityEditor.Animations.AnimatorController controller)
        {
            var animator = target.GetComponent<Animator>() ?? target.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = target.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var skinned in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skinned.updateWhenOffscreen = true;

            if (animator.avatar == null || !animator.avatar.isValid)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
                {
                    if (asset is not Avatar avatar || !avatar.isValid) continue;
                    animator.avatar = avatar;
                    break;
                }
            }
            if (animator.avatar == null)
                Debug.Log($"[ArtPass] No Avatar on {modelPath}. Not a problem here: the clips ship " +
                    "inside this same model, so they bind to it by transform path. An Avatar is only " +
                    "needed to RETARGET clips authored against a different rig.");

            Debug.Log($"[ArtPass] Entity animator: controller='{controller.name}' " +
                      $"avatar={(animator.avatar != null ? animator.avatar.name : "NONE")} " +
                      $"valid={(animator.avatar != null && animator.avatar.isValid)}");
        }

        /// <summary>
        /// Imports a rig FBX as Generic and returns its generated Avatar. The Manager's animation
        /// clips live in SEPARATE FBX files, so — exactly like the Watcher's retarget problem — they
        /// only bind if they share this rig's skeleton — see ConfigureClip for how that is done.
        /// </summary>
        public static Avatar ConfigureGenericRig(string rigRelPath)
        {
            var imp = AssetImporter.GetAtPath(ArtRoot + rigRelPath) as ModelImporter;
            if (imp == null) { Debug.LogWarning($"[ArtPass] No ModelImporter for {rigRelPath}."); return null; }
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(ArtRoot + rigRelPath))
                if (a is Avatar av) return av;
            return null;
        }

        /// <summary>
        /// Imports an animation FBX as Generic and sets its clip's loop flag. Generic clips bind to
        /// the target rig by transform PATH, so as long as the animation FBX and the rig share the
        /// same skeleton hierarchy (they do — same source), the clip retargets with no explicit
        /// avatar copy. (ModelImporterAvatarSetup.CopyFromOtherAvatar is not present in this Unity
        /// version, and path-based generic binding does not need it.)
        /// </summary>
        public static void ConfigureClip(string clipRelPath, bool loop)
        {
            var imp = AssetImporter.GetAtPath(ArtRoot + clipRelPath) as ModelImporter;
            if (imp == null) return;
            imp.animationType = ModelImporterAnimationType.Generic;
            var clips = imp.defaultClipAnimations;
            if (clips.Length > 0) { clips[0].loopTime = loop; imp.clipAnimations = clips; }
            imp.SaveAndReimport();
        }

        /// <summary>
        /// The Manager's animator: a simple Idle (looping) with a one-shot Retreat driven by a
        /// "Retreat" trigger — enough for increment 2 (perch, then crawl back into the dark and
        /// vanish when a player reaches its floor). Returns the wired Animator.
        /// </summary>
        /// <summary>
        /// The Manager's animator: one state per authored clip, every one reachable from Any State via
        /// a trigger of the same name. A flat trigger-driven machine (rather than a blend tree) suits
        /// this entity because it does not accelerate between gaits - it is doing one deliberate thing
        /// at a time, and switching should be abrupt.
        ///
        /// Clips live in separate FBX files and bind to the rig by transform path (Generic), so they
        /// must share its skeleton - they do, being the same source.
        /// </summary>
        public static Animator SetupManagerAnimator(GameObject visual, string rigRelPath,
            (string trigger, string clipRel, bool loop)[] clips, string controllerName)
        {
            var avatar = ConfigureGenericRig(rigRelPath);
            foreach (var c in clips) ConfigureClip(c.clipRel, c.loop);

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Animations"))
                AssetDatabase.CreateFolder("Assets/_Project", "Animations");
            string ctrlPath = $"Assets/_Project/Animations/{controllerName}.controller";
            if (AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ctrlPath) != null)
                AssetDatabase.DeleteAsset(ctrlPath);
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            if (controller.layers.Length == 0) controller.AddLayer("Base");
            var sm = controller.layers[0].stateMachine;

            var bound = new List<string>();
            UnityEditor.Animations.AnimatorState first = null;
            foreach (var c in clips)
            {
                var clip = FindAnimationClip(ArtRoot + c.clipRel);
                if (clip == null) continue;
                controller.AddParameter(c.trigger, AnimatorControllerParameterType.Trigger);
                var st = sm.AddState(c.trigger);
                st.motion = clip;
                if (first == null) { first = st; sm.defaultState = st; }
                var tr = sm.AddAnyStateTransition(st);
                tr.hasExitTime = false;
                tr.duration = 0.1f;
                tr.canTransitionToSelf = false;
                tr.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, c.trigger);
                bound.Add($"{c.trigger}='{clip.name}'");
            }
            EditorUtility.SetDirty(controller);

            var animator = visual.GetComponent<Animator>() ?? visual.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (avatar != null) animator.avatar = avatar;
            foreach (var sk in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                sk.updateWhenOffscreen = true;

            Debug.Log($"[ArtPass] Manager animator: {bound.Count} clip(s) bound [{string.Join(", ", bound)}] " +
                      $"avatar={(avatar != null ? avatar.name : "NONE")}");
            return animator;
        }

        /// <summary>First AnimationClip inside a model whose name contains <paramref name="nameContains"/>.</summary>
        private static AnimationClip FindAnimationClipNamed(string modelPath, string nameContains)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
                if (asset is AnimationClip c && !c.name.StartsWith("__preview") &&
                    c.name.IndexOf(nameContains, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return c;
            return null;
        }

        /// <summary>Points an existing controller's first state at <paramref name="clip"/>.</summary>
        private static void RebindControllerClip(UnityEditor.Animations.AnimatorController controller, AnimationClip clip)
        {
            if (controller.layers == null || controller.layers.Length == 0) return;
            var stateMachine = controller.layers[0].stateMachine;
            if (stateMachine == null) return;

            var state = stateMachine.defaultState;
            if (state == null)
            {
                state = stateMachine.AddState(clip.name);
                stateMachine.defaultState = state;
            }
            if (state.motion == clip) return;
            state.motion = clip;
            EditorUtility.SetDirty(controller);
        }

        private static AnimationClip FindAnimationClip(string modelPath)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
                if (asset is AnimationClip candidate && !candidate.name.StartsWith("__preview"))
                    return candidate;
            return null;
        }

    }
}
#endif
