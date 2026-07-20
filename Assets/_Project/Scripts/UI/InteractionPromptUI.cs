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
            if (show) promptText.text = $"[E]  {prompt}";
        }
    }
}
