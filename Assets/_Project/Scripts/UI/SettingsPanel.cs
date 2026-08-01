using LastWard.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// The audio sliders, built into whichever screen asks for them.
    ///
    /// Deliberately not a singleton panel that both menus share: the title screen and the pause menu
    /// live on different canvases with different lifetimes, and passing one panel between them means
    /// re-parenting UI at runtime for no gain. Three sliders are cheap to build twice.
    /// </summary>
    public static class SettingsPanel
    {
        public static void Build(Transform parent, Vector2 topLeft)
        {
            AudioSettingsService.Load();
            Row(parent, "Master", topLeft + new Vector2(0f, 0f), AudioSettingsService.Master,
                AudioSettingsService.SetMaster);
            Row(parent, "Sound Effects", topLeft + new Vector2(0f, -62f), AudioSettingsService.Sfx,
                AudioSettingsService.SetSfx);
            Row(parent, "Music", topLeft + new Vector2(0f, -124f), AudioSettingsService.Music,
                AudioSettingsService.SetMusic);
        }

        private static void Row(Transform parent, string label, Vector2 pos, float value,
                                System.Action<float> onChange)
        {
            var row = MenuTheme.Rect("Set_" + label, parent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(520f, 44f), pos);
            row.pivot = new Vector2(0f, 1f);

            var name = MenuTheme.Text(row, "Label", label.ToUpperInvariant(), 20f, MenuTheme.Dim);
            var nrt = name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0f, 0.5f);
            nrt.pivot = new Vector2(0f, 0.5f);
            nrt.sizeDelta = new Vector2(220f, 26f);
            nrt.anchoredPosition = Vector2.zero;

            var readout = MenuTheme.Text(row, "Value", Mathf.RoundToInt(value * 100f) + "%",
                20f, MenuTheme.Accent, TextAlignmentOptions.Right);
            var vrt = readout.rectTransform;
            vrt.anchorMin = vrt.anchorMax = new Vector2(1f, 0.5f);
            vrt.pivot = new Vector2(1f, 0.5f);
            vrt.sizeDelta = new Vector2(90f, 26f);
            vrt.anchoredPosition = Vector2.zero;

            // Slider built by hand rather than from a prefab: a bare Slider with no art renders as an
            // invisible strip, and the project has no UI sprite set to point it at.
            var track = MenuTheme.Rect("Track", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(210f, 4f), new Vector2(230f, 0f));
            track.pivot = new Vector2(0f, 0.5f);
            var trackImg = track.gameObject.AddComponent<Image>();
            trackImg.color = new Color(0.28f, 0.27f, 0.26f, 0.8f);

            var fill = MenuTheme.Rect("Fill", track, new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.color = MenuTheme.Accent;

            var handle = MenuTheme.Rect("Handle", track, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, 20f), Vector2.zero);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.color = MenuTheme.Ink;

            var slider = track.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(v =>
            {
                onChange(v);
                readout.text = Mathf.RoundToInt(v * 100f) + "%";
            });
        }
    }
}
