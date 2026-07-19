using UnityEngine;

namespace LastWard.Player
{
    public class FlashlightController : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private Light flashlight;
        [SerializeField] private bool startEnabled;

        private void Awake()
        {
            if (flashlight != null) flashlight.enabled = startEnabled;
        }

        private void OnEnable() => input.FlashlightTogglePressed += ToggleFlashlight;
        private void OnDisable() => input.FlashlightTogglePressed -= ToggleFlashlight;

        private void ToggleFlashlight()
        {
            if (flashlight != null) flashlight.enabled = !flashlight.enabled;
        }
    }
}
