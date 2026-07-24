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
