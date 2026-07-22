namespace LastWard.UI
{
    /// <summary>
    /// Tracks whether any full-screen UI currently wants the mouse cursor. Exists so
    /// <see cref="LastWard.Player.FirstPersonLook"/> can re-lock the cursor without fighting the
    /// note reader or the keypad for it.
    ///
    /// The cursor was locked exactly once, in Start. But the Editor releases that lock whenever the
    /// Game view loses focus or Escape is pressed, and nothing ever took it back — after which mouse
    /// look only works while the pointer happens to be over the Game view, which reads as the camera
    /// randomly refusing to turn.
    /// </summary>
    public static class CursorLockGate
    {
        private static int openPanels;

        public static bool AnyPanelOpen => openPanels > 0;

        public static void PanelOpened() => openPanels++;

        public static void PanelClosed()
        {
            openPanels--;
            if (openPanels < 0) openPanels = 0;
        }
    }
}
