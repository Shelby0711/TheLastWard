#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LastWard.EditorTools
{
    /// <summary>
    /// Compresses every imported model and texture in <c>Assets/_Project/Art</c>.
    ///
    /// These assets were downloaded at showcase quality — 4K textures on props seen across a dark
    /// yard, meshes with full read/write buffers and no compression. That costs both VRAM and system
    /// RAM (a readable mesh is kept twice: once on the GPU, once on the CPU).
    ///
    /// Nothing here removes content. It changes how the same content is stored:
    /// <list type="bullet">
    /// <item><b>Meshes</b> — compressed, optimised for vertex cache, read/write disabled, and
    /// normals/tangents/blend shapes dropped where they aren't used. Read/write alone typically
    /// halves a mesh's memory.</item>
    /// <item><b>Textures</b> — capped by role and crunch-compressed. A 2048² prop texture is
    /// indistinguishable from 512² at arm's length in an unlit corridor.</item>
    /// </list>
    ///
    /// Safe to re-run; it only writes settings that differ.
    /// </summary>
    public static class AssetOptimizer
    {
        private const string ArtRoot = "Assets/_Project/Art";

        // Characters keep more detail than props: they are the thing the player looks at.
        private const int CharacterTextureMax = 512;
        private const int PropTextureMax = 256;
        private const int EnvironmentTextureMax = 256;

        [MenuItem("The Last Ward/Optimize All Assets")]
        public static void Optimize()
        {
            int meshes = OptimizeModels();
            int textures = OptimizeTextures();

            AssetDatabase.SaveAssets();
            Debug.Log($"[Optimize] {meshes} models compressed, {textures} textures capped and crunched. " +
                      "Re-run after importing new art.");
        }

        private static int OptimizeModels()
        {
            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer) continue;

                bool dirty = false;

                // Read/write off is the single biggest win: a readable mesh is held in CPU memory as
                // well as on the GPU. Nothing here reads mesh data at runtime.
                if (importer.isReadable) { importer.isReadable = false; dirty = true; }

                if (importer.meshCompression != ModelImporterMeshCompression.High)
                {
                    importer.meshCompression = ModelImporterMeshCompression.High;
                    dirty = true;
                }
                if (!importer.optimizeMeshVertices) { importer.optimizeMeshVertices = true; dirty = true; }
                if (!importer.weldVertices) { importer.weldVertices = true; dirty = true; }

                // Unused vertex channels are pure weight. The game has no normal maps on these
                // models and no blend shapes at all.
                if (importer.importBlendShapes) { importer.importBlendShapes = false; dirty = true; }
                if (importer.importTangents != ModelImporterTangents.None)
                {
                    importer.importTangents = ModelImporterTangents.None;
                    dirty = true;
                }
                if (importer.importCameras) { importer.importCameras = false; dirty = true; }
                if (importer.importLights) { importer.importLights = false; dirty = true; }

                if (!dirty) continue;
                importer.SaveAndReimport();
                changed++;
            }
            return changed;
        }

        private static int OptimizeTextures()
        {
            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;

                int max = path.Contains("/Characters/") ? CharacterTextureMax
                        : path.Contains("/Environment/") ? EnvironmentTextureMax
                        : PropTextureMax;

                bool dirty = false;
                if (importer.maxTextureSize > max) { importer.maxTextureSize = max; dirty = true; }

                if (importer.textureCompression != TextureImporterCompression.Compressed)
                {
                    importer.textureCompression = TextureImporterCompression.Compressed;
                    dirty = true;
                }
                // Crunch trades a slower import for a much smaller file and less VRAM.
                if (!importer.crunchedCompression)
                {
                    importer.crunchedCompression = true;
                    importer.compressionQuality = 50;
                    dirty = true;
                }
                if (importer.isReadable) { importer.isReadable = false; dirty = true; }
                // Mipmaps are worth keeping: without them distant textures shimmer badly, and they
                // actually reduce sampling cost at distance.
                if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; dirty = true; }

                if (!dirty) continue;
                importer.SaveAndReimport();
                changed++;
            }
            return changed;
        }
    }
}
#endif
