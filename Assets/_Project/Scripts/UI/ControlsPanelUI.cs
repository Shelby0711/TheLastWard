using LastWard.Player;
using TMPro;
using UnityEngine;

namespace LastWard.UI
{
    /// <summary>
    /// F3 control reference. Exists because this game has no tutorial and several verbs that aren't
    /// guessable — that hiding, attacking and interacting are three different keys, that the torch
    /// makes you far easier to see, that a weapon is good for exactly one swing. A player who never
    /// discovers those isn't playing the game as designed.
    ///
    /// Deliberately a static list rather than reading live bindings: the generated wrapper doesn't
    /// expose a tidy display string per binding, and a wrong-but-simple list is worse than useless
    /// only if the bindings change — so if you rebind anything in PlayerControls.inputactions,
    /// update this text with it.
    /// </summary>
    public class ControlsPanelUI : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TextMeshProUGUI body;

        private const string Controls =
            "<b>CONTROLS</b>\n\n" +
            "<b>W A S D</b>      Move\n" +
            "<b>Mouse</b>        Look\n" +
            "<b>Shift</b>        Sprint  <i>— louder; it can hear you</i>\n" +
            "<b>C / Ctrl</b>     Crouch  <i>— to see under beds and into low cupboards</i>\n\n" +
            "<b>E</b>            Interact — pick up, read, open containers\n" +
            "<b>Q</b>            Hide / come out of a wardrobe, locker or bed\n" +
            "<b>Left Mouse</b>   Swing carried weapon  <i>— one hit, then it's gone</i>\n" +
            "<b>F</b>            Torch  <i>— makes you much easier to spot</i>\n" +
            "<b>1 / 2</b>        Inventory slots\n\n" +
            "<b>Q / E</b>        While dead: switch who you're watching\n" +
            "<b>Left Mouse</b>   While dead: ping for the living\n\n" +
            "<b>R</b>            Skip the wait after a run ends\n" +
            "<b>F3</b>           Close this";

        private bool visible;
        private PlayerInputReader subscribedTo;

        private void OnEnable()
        {
            if (body != null) body.text = Controls;
            Apply(false);
        }

        private void OnDisable() => Unsubscribe();

        private void Update()
        {
            // This UI exists before any player does, and the player is replaced on respawn, so the
            // subscription follows whichever reader is currently local rather than being wired once.
            var reader = PlayerInputReader.Local;
            if (reader == subscribedTo) return;

            Unsubscribe();
            if (reader == null) return;
            reader.JournalPressed += Toggle;
            subscribedTo = reader;
        }

        private void Unsubscribe()
        {
            if (subscribedTo == null) return;
            subscribedTo.JournalPressed -= Toggle;
            subscribedTo = null;
        }

        private void Toggle() => Apply(!visible);

        private void Apply(bool show)
        {
            visible = show;
            if (group == null) return;
            group.alpha = show ? 1f : 0f;
            group.blocksRaycasts = show;
        }
    }
}
