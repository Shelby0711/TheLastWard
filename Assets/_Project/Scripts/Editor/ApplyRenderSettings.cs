#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LastWard.EditorTools
{
    /// <summary>
    /// Pushes the URP asset toward the plan's retro look and buys back framerate at the same time:
    /// sub-native render scale, no MSAA, short shadow distance. Re-runnable; tweak the constants
    /// and run again. Separate menu item (not part of the scene builders) so it never silently
    /// changes render settings out from under you.
    /// </summary>
    public static class ApplyRenderSettings
    {
        private const float RenderScale = 0.8f;
        private const float ShadowDistance = 25f;

        [MenuItem("The Last Ward/Apply Performance + Retro Render Settings")]
        public static void Apply()
        {
            var guids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
            if (guids.Length == 0)
            {
                Debug.LogWarning("No UniversalRenderPipelineAsset found — assign one in Project Settings > Graphics first.");
                return;
            }

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null) continue;

                var so = new SerializedObject(asset);
                TrySetFloat(so, "m_RenderScale", RenderScale);
                TrySetInt(so, "m_MSAA", 1);
                TrySetFloat(so, "m_ShadowDistance", ShadowDistance);
                TrySetBool(so, "m_SoftShadowsSupported", false);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
                Debug.Log($"Applied retro/perf settings to {path}");
            }

            QualitySettings.vSyncCount = 1;
            QualitySettings.antiAliasing = 0;
            AssetDatabase.SaveAssets();
            Debug.Log($"Render scale {RenderScale}, MSAA off, shadow distance {ShadowDistance}, VSync on. " +
                "Lower RenderScale in ApplyRenderSettings.cs if you want more speed or a crunchier look.");
        }

        private static void TrySetFloat(SerializedObject so, string name, float value)
        {
            var prop = so.FindProperty(name);
            if (prop != null) prop.floatValue = value;
        }

        private static void TrySetInt(SerializedObject so, string name, int value)
        {
            var prop = so.FindProperty(name);
            if (prop != null) prop.intValue = value;
        }

        private static void TrySetBool(SerializedObject so, string name, bool value)
        {
            var prop = so.FindProperty(name);
            if (prop != null) prop.boolValue = value;
        }
    }
}
#endif
