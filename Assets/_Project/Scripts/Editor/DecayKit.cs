#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace LastWard.EditorTools
{
    /// <summary>
    /// One rule runs this whole building: nothing in it has been maintained in decades. Anything
    /// that reads as new is a bug, and the hand-built props were the worst offenders — flat grey
    /// panels with a cube for a handle.
    ///
    /// This is the shared vocabulary for that: aged materials, cobwebs, floor grime, litter decals
    /// and the PSX waste props, all pulled from one place so a locker on the ground floor and a
    /// cabinet on the second are dirty in the same way.
    ///
    /// <b>Performance.</b> Everything here is deliberately cheap and it has to stay that way, because
    /// decay is applied by the hundred:
    ///   - Six 256px textures total, six materials total. Every web in the game is one material, so
    ///     the whole lot batches; the moment this starts making per-instance materials it stops.
    ///   - Decals are single quads (2 tris). The waste models are 12-92 tris each.
    ///   - Everything is flagged static, so Unity batches it and it costs nothing to cull.
    ///   - Only the props big enough to walk into get colliders. Floor litter is thin enough for the
    ///     CharacterController's step offset to ignore, so it never trips anyone.
    /// </summary>
    public static class DecayKit
    {
        private const string Aged = "Assets/_Project/Art/Props/Aged/";
        private const string Debris = "Assets/_Project/Art/Props/Debris/";
        private const string Waste = "Assets/_Project/Art/Props/Waste/";
        private const string MatFolder = "Assets/_Project/Materials/Aged";

        // ------------------------------------------------------------------ materials

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(MatFolder)) return;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Materials"))
                AssetDatabase.CreateFolder("Assets/_Project", "Materials");
            AssetDatabase.CreateFolder("Assets/_Project/Materials", "Aged");
        }

        private enum Mode { Opaque, Cutout, Blend }

        private static Material Load(string texPath, string matName, Mode mode, float tiling = 1f)
        {
            if (Cache.TryGetValue(matName, out var cached) && cached != null) return cached;
            EnsureFolder();

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                Debug.LogWarning($"[Decay] Texture missing: {texPath}");
                return null;
            }
            if (mode != Mode.Opaque) EnsureAlpha(texPath);

            string path = $"{MatFolder}/{matName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.mainTextureScale = new Vector2(tiling, tiling);

            if (mode == Mode.Cutout)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.3f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = 2450;
            }
            else if (mode == Mode.Blend)
            {
                // Soft-edged floor filth. Blended rather than clipped because a cut-out grime stain
                // has a hard rim, which is the one thing dirt never has. No depth write, so a
                // dozen overlapping stains cost nothing to sort.
                mat.SetFloat("_Surface", 1f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.renderQueue = 3000;
            }
            // Webs and decals are single quads seen from both sides.
            if (mode != Mode.Opaque) mat.SetFloat("_Cull", 0f);

            EditorUtility.SetDirty(mat);
            Cache[matName] = mat;
            return mat;
        }

        private static void EnsureAlpha(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter imp) return;
            if (imp.alphaIsTransparency && imp.alphaSource == TextureImporterAlphaSource.FromInput) return;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = true;
            imp.SaveAndReimport();
        }

        /// <summary>Rotted, water-damaged planking. Tiles horizontally.</summary>
        public static Material Wood(float tiling = 1f) =>
            Load(Aged + "Wood_Rotten.png", tiling > 1.01f ? $"M_Aged_Wood_x{tiling:0.#}" : "M_Aged_Wood",
                 Mode.Opaque, tiling);

        /// <summary>Institutional green enamel, flaked back to the rust underneath.</summary>
        public static Material Enamel(float tiling = 1f) =>
            Load(Aged + "Metal_Chipped.png", tiling > 1.01f ? $"M_Aged_Enamel_x{tiling:0.#}" : "M_Aged_Enamel",
                 Mode.Opaque, tiling);

        /// <summary>Bare corroded iron. Handles, hinges, legs, frames.</summary>
        public static Material Rust(float tiling = 1f) =>
            Load(Aged + "Metal_Rust.png", tiling > 1.01f ? $"M_Aged_Rust_x{tiling:0.#}" : "M_Aged_Rust",
                 Mode.Opaque, tiling);

        /// <summary>Mattress ticking that has been slept on by people who did not leave.</summary>
        public static Material Ticking() => Load(Aged + "Fabric_Stained.png", "M_Aged_Ticking", Mode.Opaque);

        public static Material WebMat() => Load(Aged + "Cobweb.png", "M_Aged_Cobweb", Mode.Cutout);

        public static Material GrimeMat() => Load(Aged + "Grime.png", "M_Aged_Grime", Mode.Blend);

        /// <summary>
        /// One item of floor litter, taken from a cell of the debris atlas.
        ///
        /// The Trash-and-Debris pack is UV atlases for models, not decals. Six of its sheets carry
        /// no alpha channel at all and five more are a flat translucent wash, so laying them down
        /// whole put a photograph of a supermarket can display on the ward floor and a pane of
        /// green glass beside it. The atlas is built from those sheets by cutting the individual
        /// objects out, keying the flat backgrounds, and grinding the studio polish off them.
        ///
        /// One texture, sixteen materials that differ only in UV offset. Sixteen draw calls for
        /// every piece of litter in the game, and static batching collapses each of those to one.
        /// </summary>
        public const int LitterCells = 16;

        public static Material LitterMat(int cell)
        {
            cell = ((cell % LitterCells) + LitterCells) % LitterCells;
            var mat = Load(Debris + "Debris_Atlas.png", $"M_Debris_{cell:00}", Mode.Cutout);
            if (mat == null) return null;
            const int grid = 4;
            const float step = 1f / grid;
            mat.mainTextureScale = new Vector2(step, step);
            // Row 0 of the atlas image is the TOP row; UV v runs from the bottom.
            mat.mainTextureOffset = new Vector2((cell % grid) * step,
                                                1f - step - (cell / grid) * step);
            mat.SetTextureScale("_BaseMap", mat.mainTextureScale);
            mat.SetTextureOffset("_BaseMap", mat.mainTextureOffset);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ primitives

        /// <summary>
        /// A shaped, rotated, textured piece of a hand-built prop. Colliders are opt-out because
        /// interaction rays run with QueryTriggerInteraction.Ignore — a handle you can't hit is a
        /// handle that isn't there — but trim and detail geometry should pass them up.
        /// </summary>
        public static GameObject Part(Transform parent, string name, PrimitiveType type,
            Vector3 localPos, Vector3 localEuler, Vector3 size, Material mat,
            bool collide = true, bool immobile = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = size;
            if (mat != null && go.TryGetComponent<Renderer>(out var r)) r.sharedMaterial = mat;
            if (!collide)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) UnityObject.DestroyImmediate(c);
            }
            if (immobile) MarkStatic(go);
            return go;
        }

        /// <summary>
        /// Batching-static, but nothing else. Static batching bakes the transform into a shared
        /// mesh, so anything that swings — a door leaf, a container's door — must pass
        /// <c>immobile: false</c> or it will visibly stay shut while its collider opens.
        /// </summary>
        public static void MarkStatic(GameObject go) =>
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);

        private static GameObject Quad(Transform parent, string name, Vector3 localPos,
            Vector3 euler, Vector2 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            if (mat != null && go.TryGetComponent<Renderer>(out var r)) r.sharedMaterial = mat;
            // Decoration never has physics. A collider here would block interaction rays and, on
            // the floor, catch the player's capsule on a piece of paper.
            var col = go.GetComponent<Collider>();
            if (col != null) UnityObject.DestroyImmediate(col);
            MarkStatic(go);
            return go;
        }

        // ------------------------------------------------------------------ dressing

        /// <summary>
        /// A web strung across an inside corner. <paramref name="yaw"/> faces it out of the corner;
        /// the quad is hung on the diagonal so it spans both walls rather than sitting on one.
        /// </summary>
        public static GameObject WebCorner(Transform parent, Vector3 localPos, float yaw, float size)
        {
            var mat = WebMat();
            if (mat == null) return null;
            var go = Quad(parent, "Cobweb", localPos, new Vector3(0f, yaw, 0f), new Vector2(size, size), mat);
            return go;
        }

        /// <summary>
        /// Webs in the top corners of a box-shaped prop, plus one hanging off a front edge. Seeded so
        /// two lockers in the same room are not webbed identically.
        /// </summary>
        public static void WebBox(Transform root, Vector3 halfExtents, float top, int seed, int count = 3)
        {
            var rng = new System.Random(seed);
            (float x, float z, float yaw)[] corners =
            {
                (-halfExtents.x, -halfExtents.z, 45f),
                ( halfExtents.x, -halfExtents.z, -45f),
                (-halfExtents.x,  halfExtents.z, 135f),
                ( halfExtents.x,  halfExtents.z, -135f),
            };
            // Shuffle so which corners get webbed varies, then take the first `count`.
            for (int i = corners.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (corners[i], corners[j]) = (corners[j], corners[i]);
            }
            for (int i = 0; i < Mathf.Min(count, corners.Length); i++)
            {
                var (x, z, yaw) = corners[i];
                float s = Mathf.Lerp(0.22f, 0.44f, (float)rng.NextDouble());
                // Pulled in and down by half the quad so the web's anchored corner lands in the
                // prop's corner rather than the quad's centre landing there.
                WebCorner(root, new Vector3(x - Mathf.Sign(x) * s * 0.42f, top - s * 0.45f,
                                            z - Mathf.Sign(z) * s * 0.42f), yaw, s);
            }
        }

        /// <summary>A dirt stain on the floor. <paramref name="worldPos"/> is on the floor plane.</summary>
        public static GameObject Grime(Transform parent, Vector3 worldPos, float size, float yaw)
        {
            var mat = GrimeMat();
            if (mat == null) return null;
            var go = Quad(parent, "Grime", Vector3.zero, new Vector3(90f, yaw, 0f),
                new Vector2(size, size), mat);
            // 1.2cm of clearance: enough to beat depth precision at the far plane, small enough
            // that it never reads as floating.
            go.transform.position = worldPos + Vector3.up * 0.012f;
            return go;
        }

        /// <summary>
        /// Scattered rubbish as flat decals — paper, glass, flattened cans. Far cheaper than
        /// modelling any of it, and at floor level nobody can tell.
        /// </summary>
        public static void Litter(Transform parent, Vector3 centre, float radius, int count, int seed)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                var mat = LitterMat(rng.Next(LitterCells));
                if (mat == null) continue;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = radius * Mathf.Sqrt((float)rng.NextDouble());
                var pos = centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                // A crushed can is 15cm across and a sheet of newspaper is 45. Anything bigger
                // stops being an object on the floor and starts being a poster of one — the
                // previous 0.5-1.5m range is what made these read as photographs.
                float s = Mathf.Lerp(0.2f, 0.46f, (float)rng.NextDouble());
                var go = Quad(parent, "Litter", Vector3.zero,
                    new Vector3(90f, (float)rng.NextDouble() * 360f, 0f), new Vector2(s, s), mat);
                // Staggered heights so overlapping items have a stable draw order instead of
                // z-fighting against each other.
                go.transform.position = pos + Vector3.up * (0.010f + i * 0.0012f);
            }
        }

        // ------------------------------------------------------------------ waste props

        private static readonly Dictionary<string, GameObject> Models = new Dictionary<string, GameObject>();

        private static GameObject WasteModel(string file)
        {
            if (Models.TryGetValue(file, out var m) && m != null) return m;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Waste + file + ".glb");
            if (go == null) Debug.LogWarning($"[Decay] Waste model missing: {Waste}{file}.glb");
            Models[file] = go;
            return go;
        }

        /// <summary>
        /// One PSX waste prop, grounded and optionally solid. These import at real-world scale, so
        /// nothing needs fitting — a barrel is a metre tall because it is a barrel.
        /// </summary>
        public static GameObject WasteProp(Transform parent, string file, Vector3 worldPos,
            float yaw, bool solid, float tilt = 0f)
        {
            var model = WasteModel(file);
            if (model == null) return null;

            var go = (GameObject)UnityObject.Instantiate(model, parent);
            go.name = "Waste_" + file;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) UnityObject.DestroyImmediate(c);
            go.transform.rotation = Quaternion.Euler(tilt, yaw, 0f);
            ArtKit.GroundAt(go, worldPos);
            if (solid) ArtKit.MakeSolid(go);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) MarkStatic(t.gameObject);
            return go;
        }

        // Which of the six models can be walked into, and how often each turns up. Cans and bottles
        // dominate because that is what actually accumulates; a dumpster is an event.
        private static readonly (string file, bool solid, int weight)[] WasteTable =
        {
            ("crumpled-can",  false, 30),
            ("glass-bottle",  false, 22),
            ("cardboard-box", true,  16),
            ("trash-bag",     true,  16),
            ("barrel",        true,  10),
        };

        // The small half of the table — what can sit against a wall in a corridor without becoming
        // an obstacle. No barrels and no bags: those go in corners only.
        private static readonly (string file, bool solid, int weight)[] WallTable =
        {
            ("crumpled-can",  false, 40),
            ("glass-bottle",  false, 34),
            ("cardboard-box", true,  26),
        };

        /// <summary>
        /// One modelled object tucked against a wall, pushed back into it so it does not float in
        /// the lane. <paramref name="outward"/> points from the room toward the wall.
        /// </summary>
        public static void WasteAgainstWall(Transform parent, Vector3 spot, Vector3 outward, int seed)
        {
            var rng = new System.Random(seed);
            int total = 0;
            foreach (var w in WallTable) total += w.weight;
            int roll = rng.Next(total);
            var pick = WallTable[0];
            foreach (var w in WallTable)
            {
                if (roll < w.weight) { pick = w; break; }
                roll -= w.weight;
            }

            var pos = spot + outward.normalized * 0.18f;
            float tilt = pick.solid ? rng.Next(-5, 6) : rng.Next(-95, 96);
            WasteProp(parent, pick.file, pos, (float)rng.NextDouble() * 360f, pick.solid, tilt);
        }

        /// <summary>
        /// Rubbish piled where rubbish piles: against walls, in corners, never in the middle of a
        /// room the player has to be chased through. Callers pass the spots; this decides what
        /// lands on each one.
        /// </summary>
        public static void ScatterWaste(Transform parent, Vector3 centre, float radius, int count, int seed)
        {
            var rng = new System.Random(seed);
            int total = 0;
            foreach (var w in WasteTable) total += w.weight;

            for (int i = 0; i < count; i++)
            {
                int roll = rng.Next(total);
                var pick = WasteTable[0];
                foreach (var w in WasteTable)
                {
                    if (roll < w.weight) { pick = w; break; }
                    roll -= w.weight;
                }

                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = radius * Mathf.Sqrt((float)rng.NextDouble());
                var pos = centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                // Cans and bottles have been kicked around; they lie over rather than stand up.
                float tilt = pick.solid ? rng.Next(-4, 5) : rng.Next(-95, 96);
                WasteProp(parent, pick.file, pos, (float)rng.NextDouble() * 360f, pick.solid, tilt);
            }
        }

        // ------------------------------------------------------------------ weathering

        private static readonly Dictionary<Material, Material> Weathered = new Dictionary<Material, Material>();

        /// <summary>
        /// Ages an imported prop by swapping its materials for darkened, desaturated, brown-shifted
        /// variants. The beds, drip stands and doors all ship clean from their packs, and a ward
        /// full of factory-fresh hospital furniture is the single loudest thing telling the player
        /// this building is a set.
        ///
        /// The variants are cached per source material, so the whole game adds one extra material
        /// per prop texture and every instance of that prop still batches together. Tinting the
        /// source material in place would be cheaper still, but it would also age pickups and notes
        /// that share a texture with something else.
        /// </summary>
        /// <param name="amount">0 = untouched, 1 = filthy. 0.5-0.7 is the usual range.</param>
        public static void Weather(GameObject prop, float amount = 0.6f)
        {
            if (prop == null || amount <= 0.001f) return;
            EnsureFolder();

            foreach (var r in prop.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;
                    if (!Weathered.TryGetValue(src, out var aged) || aged == null)
                    {
                        aged = MakeWeathered(src, amount);
                        Weathered[src] = aged;
                    }
                    if (aged != null && aged != src) { mats[i] = aged; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        private static Material MakeWeathered(Material src, float amount)
        {
            string name = $"M_Worn_{src.name}";
            string path = $"{MatFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(src);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = src.shader;
                mat.CopyPropertiesFromMaterial(src);
            }
            mat.name = name;

            // Grime does three things to a colour: takes the light out of it, takes the saturation
            // out of it, and pushes what is left toward the brown of dust and rust. Doing only the
            // first gives you a prop that looks like it is in shadow, not one that is filthy.
            Color c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
            float grey = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            var dust = new Color(0.44f, 0.38f, 0.30f);          // what settles on everything
            var tinted = new Color(
                Mathf.Lerp(c.r, grey * dust.r * 2.1f, amount * 0.7f),
                Mathf.Lerp(c.g, grey * dust.g * 2.1f, amount * 0.7f),
                Mathf.Lerp(c.b, grey * dust.b * 2.1f, amount * 0.7f),
                c.a);
            tinted.r *= 1f - amount * 0.36f;
            tinted.g *= 1f - amount * 0.36f;
            tinted.b *= 1f - amount * 0.36f;
            mat.SetColor("_BaseColor", tinted);

            // Nothing in this building shines. Any specular left over reads as a wipe-clean surface.
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", Mathf.Min(mat.GetFloat("_Metallic"), 0.05f));

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Clears the per-build caches. Rebuilds reuse the material assets on disk.</summary>
        public static void Reset()
        {
            Cache.Clear();
            Models.Clear();
            Weathered.Clear();
        }
    }
}
#endif
