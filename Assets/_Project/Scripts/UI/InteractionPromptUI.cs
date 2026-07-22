using TMPro;
using UnityEngine;

namespace LastWard.UI
{
    public class InteractionPromptUI : MonoBehaviour
    {
        public static InteractionPromptUI Instance { get; private set; }

        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text promptText;

        private void Awake()
        {
            Instance = this;
            SetPrompt(null);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetPrompt(string prompt)
        {
            bool show = !string.IsNullOrEmpty(prompt);
            root.SetActive(show);
            // Interactables that use a different key say so themselves ("[Q] Hide"). Prefixing
            // [E] unconditionally produced "[E]  [Q] Hide under the bed".
            if (show) promptText.text = prompt.StartsWith("[") ? prompt : $"[E]  {prompt}";
        }
    }
}
