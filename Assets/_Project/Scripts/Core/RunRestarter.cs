using LastWard.Net;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastWard.Core
{
    /// <summary>
    /// Ends a finished run instead of leaving everyone parked on a death screen forever. Once the
    /// run is over — someone escaped, or nobody is left alive — this counts down and then tears the
    /// session down and reloads the scene, dropping every machine back at the connect menu ready to
    /// host or join again.
    ///
    /// Each client restarts itself rather than the host pushing a networked scene load: the run is
    /// already over, so there is no state left worth synchronising, and a local reload can't be
    /// broken by the host having already disconnected.
    /// </summary>
    public class RunRestarter : MonoBehaviour
    {
        [Tooltip("Seconds to sit on the ending before the run resets, so the last beat can land.")]
        [SerializeField] private float restartDelay = 8f;

        private float countdown;
        private bool restarting;

        private void Update()
        {
            if (restarting) return;

            if (countdown <= 0f)
            {
                if (!IsRunOver()) return;
                countdown = restartDelay;
                return;
            }

            countdown -= Time.deltaTime;
            // Skip the wait — useful when you already know how it went.
            bool skip = UnityEngine.InputSystem.Keyboard.current != null &&
                        UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame;
            if (countdown > 0f && !skip) return;

            restarting = true;
            StartCoroutine(RestartRoutine());
        }

        /// <summary>
        /// Over when the objective says so (someone escaped, or the Entity took the last player), or
        /// when this client is dead with no living teammate left to spectate.
        /// </summary>
        private bool IsRunOver()
        {
            if (ObjectiveTracker.Instance != null && ObjectiveTracker.Instance.Stage == ObjectiveStage.Ended)
                return true;

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || nm.LocalClient == null) return false;
            var playerObject = nm.LocalClient.PlayerObject;
            if (playerObject == null) return false;
            if (!playerObject.TryGetComponent<PlayerNetworkState>(out var state)) return false;
            if (state.IsAlive) return false;

            return !LastWard.Spectator.SpectatorController.AnyOtherAlive(state);
        }

        private System.Collections.IEnumerator RestartRoutine()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening) nm.Shutdown();

            // Shutdown despawns objects over the following frame; tearing the NetworkManager down in
            // the same frame throws as that unwinds.
            yield return null;

            // Both survive scene loads (DontDestroyOnLoad), so without this they'd stack up a fresh
            // copy on every restart.
            if (nm != null) Destroy(nm.gameObject);
            if (NetworkSessionManager.Instance != null) Destroy(NetworkSessionManager.Instance.gameObject);
            yield return null;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
