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
            "<b>F</b>            Torch  <i>— 5 bars; leaving it on burns through them faster</i>\n" +
            "<b>V</b>            Hold breath  <i>— silent, but only for as long as you have air</i>\n" +
            "<b>1 / 2</b>        Inventory slots\n" +
            "<b>G</b>            Drop selected item  <i>— the bag is small; choose what you carry</i>\n\n" +
            "<b>Q / E</b>        While dead: switch who you're watching\n" +
            "<b>Left Mouse</b>   While dead: ping for the living\n\n" +
            "<b>R</b>            Skip the wait after a run ends\n" +
            "<b>Tab</b>          Switch to the Inventory tab  <i>— drop anything from there</i>\n" +
            "<b>F3</b>           Close this";

        private bool visible;
        private PlayerInputReader subscribedTo;
        // Which tab is showing. F3 opens on Controls; Tab flips between the two. This is the shell
        // the pause menu will grow out of.
        private bool onInventoryTab;

        private const string TabHeader =
            "<b><color=#FFFFFF>| CONTROLS |</color></b>  <color=#6E6E6E>Inventory</color>" +
            "        <i><color=#6E6E6E>Tab to switch</color></i>\n\n";
        private const string TabHeaderInv =
            "<color=#6E6E6E>| Controls |</color>  <b><color=#FFFFFF>INVENTORY</color></b>" +
            "        <i><color=#6E6E6E>Tab to switch</color></i>\n\n";

        private void OnEnable()
        {
            RefreshBody();
            Apply(false);
        }

        private void OnDisable() => Unsubscribe();

        private void RefreshBody()
        {
            if (body != null)
                body.text = onInventoryTab ? TabHeaderInv : TabHeader + Controls;
            InventoryPanelUI.Instance?.SetShown(visible && onInventoryTab);
        }

        private void Update()
        {
            if (visible && UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.tabKey.wasPressedThisFrame)
            {
                onInventoryTab = !onInventoryTab;
                RefreshBody();
            }

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
            // Only a real open/close touches the cursor. Apply(false) also runs from OnEnable, before
            // anyone has hosted — and grabbing the cursor there locked and hid it on the main menu, so
            // Host and Join could not be clicked at all. It would also have left CursorLockGate with a
            // close it never opened.
            bool changed = visible != show;
            visible = show;
            if (group == null) return;
            group.alpha = show ? 1f : 0f;
            group.blocksRaycasts = show;

            // Always reopens on Controls, so F3 means the same thing every time.
            if (!show) onInventoryTab = false;
            RefreshBody();

            if (!changed) return;

            // The inventory tab has clickable Drop buttons, so the cursor has to be released while
            // the panel is up — otherwise it is locked to the centre and nothing can be clicked.
            if (show)
            {
                CursorLockGate.PanelOpened();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                CursorLockGate.PanelClosed();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
}
