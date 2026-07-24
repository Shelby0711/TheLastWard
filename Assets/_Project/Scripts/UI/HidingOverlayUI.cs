using LastWard.Core;
using TMPro;
using UnityEngine;

namespace LastWard.UI
{
    /// <summary>
    /// Sells "you are inside something" while hidden: heavy dark borders close in from every edge so
    /// the world is seen through a narrow gap, and a line at the bottom names the way out.
    ///
    /// The exit hint is not optional polish — while hidden the interaction raycast is suspended, so
    /// nothing else on screen would tell the player which key backs them out.
    /// </summary>
    public class HidingOverlayUI : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TextMeshProUGUI hint;
        [SerializeField] private string hintFormat = "{0}   [Q]";

        [Tooltip("How fast the panels close in and open back up.")]
        [SerializeField] private float fadeSpeed = 6f;

        private HidingSpot shown;

        private void Update()
        {
            if (group == null) return;

            var spot = HidingSpot.LocalOccupied;
            bool hiding = spot != null;

            if (hiding && hint != null)
            {
                // Rebuilt every frame rather than only on entry: whether something is takeable
                // changes as the player looks around, and advertising "[E] take" with nothing in
                // view was simply wrong.
                // Only the exit hint. The interaction prompt already names whatever is under the
                // crosshair ("[E] Read Referral Note"), and printing a second generic "[E] take"
                // beside it stacked two overlapping lines on screen.
                hint.text = string.Format(hintFormat, spot.ExitPrompt);
                shown = spot;
            }
            if (!hiding) shown = null;

            group.alpha = Mathf.MoveTowards(group.alpha, hiding ? 1f : 0f, Time.deltaTime * fadeSpeed);
        }
    }
}
