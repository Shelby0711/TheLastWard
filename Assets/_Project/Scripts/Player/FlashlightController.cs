using LastWard.Net;
using UnityEngine;

namespace LastWard.Player
{
    /// <summary>
    /// Owner-only flashlight toggle. In networked scenes it drives PlayerNetworkState (so every
    /// client — including dead spectators — sees the light). The offline M1 sandbox has no
    /// PlayerNetworkState, so it falls back to toggling a directly-referenced Light.
    /// </summary>
    public class FlashlightController : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerNetworkState state;
        [SerializeField] private Light fallbackLight;

        private void OnEnable() => input.FlashlightTogglePressed += Toggle;
        private void OnDisable() => input.FlashlightTogglePressed -= Toggle;

        private void Toggle()
        {
            if (state != null) state.ToggleFlashlight();
            else if (fallbackLight != null) fallbackLight.enabled = !fallbackLight.enabled;
        }
    }
}
