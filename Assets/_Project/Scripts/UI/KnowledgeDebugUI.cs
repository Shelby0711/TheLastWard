using LastWard.Knowledge;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastWard.UI
{
    /// <summary>
    /// Dev-only readout of the otherwise-hidden knowledge scores so M4 can be verified. Toggle with
    /// F3. On the host it shows every player's score + who's marked + the aggression tier; on a
    /// remote client it can only show the replicated marked-player id. Not shipped UI.
    /// </summary>
    public class KnowledgeDebugUI : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text text;

        private bool visible;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            {
                visible = !visible;
                if (root != null) root.SetActive(visible);
            }

            if (visible && text != null)
                text.text = KnowledgeService.Instance != null ? KnowledgeService.Instance.GetDebugText() : "(no KnowledgeService)";
        }
    }
}
