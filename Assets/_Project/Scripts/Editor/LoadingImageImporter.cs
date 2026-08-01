#if UNITY_EDITOR
using UnityEditor;

namespace LastWard.EditorTools
{
    /// <summary>
    /// Import settings for the loading-screen backdrops.
    ///
    /// Unity's defaults are wrong for a full-screen still in two ways that both cost memory for
    /// nothing: mipmaps add a third again to a texture that is only ever drawn at roughly 1:1 (and
    /// make it noticeably softer while they are at it), and 2048 is twice the resolution these are
    /// authored at, so the importer would just be padding them back up.
    ///
    /// A postprocessor rather than a build-time pass, because these have to be right whenever they
    /// are imported — dropping a new screenshot into the folder should not require remembering to
    /// re-run a menu item.
    /// </summary>
    public class LoadingImageImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/_Project/Resources/Loading/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 1280;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            // No alpha in a JPEG backdrop, and asking for one costs a full DXT5 instead of DXT1.
            importer.alphaSource = TextureImporterAlphaSource.None;
        }
    }
}
#endif
