using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace LastWard.Net
{
    /// <summary>
    /// Listen-server co-op via the unified Multiplayer Services SDK (Sessions API). Host creates a
    /// Relay-backed session and shares session.Code; others join by that code. The SDK configures
    /// the transport and starts Netcode automatically, so there are no manual Relay/Lobby calls and
    /// no dedicated server. Solo play = Host alone. Requires the Unity project to be linked to a
    /// UGS project (Edit > Project Settings > Services) with Relay enabled.
    /// </summary>
    public class NetworkSessionManager : MonoBehaviour
    {
        public static NetworkSessionManager Instance { get; private set; }

        private const int MaxPlayers = 4;

        [Tooltip("Where players spawn in this scene. NGO defaults to the player prefab's own saved " +
            "transform (world origin, since the shared prefab has no scene-specific position baked " +
            "in) unless overridden here via ConnectionApprovalCallback — every scene using this " +
            "prefab needs its own sensible spawn point set on this field.")]
        [SerializeField] private Vector3 spawnPosition = Vector3.zero;

        public ISession Session { get; private set; }
        public string JoinCode => Session?.Code;
        public event Action<string> StatusChanged;

        /// <summary>Raised locally when this client loses the session (relay/websocket drop, host
        /// gone, transport failure) so the UI can surface it instead of silently freezing.</summary>
        public event Action Disconnected;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // Start (not Awake/OnEnable) guarantees NetworkManager's own Awake — which sets
        // NetworkManager.Singleton — has already run.
        private void Start()
        {
            if (NetworkManager.Singleton == null) return;
            NetworkManager.Singleton.ConnectionApprovalCallback += OnConnectionApproval;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (NetworkManager.Singleton == null) return;
            NetworkManager.Singleton.ConnectionApprovalCallback -= OnConnectionApproval;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
        }

        // Without this, losing the session (relay/websocket drop, idle timeout, host gone) despawns
        // the player object and the game just appears to freeze with no explanation.
        private void OnClientDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            bool weDropped = clientId == nm.LocalClientId || !nm.IsListening;
            if (!weDropped) return;
            ReportLostConnection("Connection lost — session ended.");
        }

        private void OnTransportFailure() => ReportLostConnection("Network transport failed.");

        private void ReportLostConnection(string reason)
        {
            Session = null;
            SetStatus(reason);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Disconnected?.Invoke();
        }

        // Requires NetworkConfig.ConnectionApproval enabled (EditorBuildKit sets this) — otherwise
        // NGO never calls this and every player silently spawns at the prefab's saved position instead.
        private void OnConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = true;
            response.CreatePlayerObject = true;
            response.Position = spawnPosition;
            response.Rotation = Quaternion.identity;
        }

        public async void Host()
        {
            try
            {
                await EnsureSignedIn();
                SetStatus("Creating session...");
                var options = new SessionOptions { MaxPlayers = MaxPlayers }.WithRelayNetwork();
                Session = await MultiplayerService.Instance.CreateSessionAsync(options);
                SetStatus($"Hosting. Share code: {Session.Code}");
            }
            catch (Exception e)
            {
                SetStatus("Host failed: " + e.Message);
                Debug.LogException(e);
            }
        }

        public async void Join(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                SetStatus("Enter a join code first.");
                return;
            }
            try
            {
                await EnsureSignedIn();
                SetStatus("Joining...");
                Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim());
                SetStatus("Joined session.");
            }
            catch (Exception e)
            {
                SetStatus("Join failed: " + e.Message);
                Debug.LogException(e);
            }
        }

        public async void Leave()
        {
            if (Session != null)
            {
                try { await Session.LeaveAsync(); } catch { /* best effort */ }
                Session = null;
            }
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            SetStatus("Disconnected.");
        }

        private async Task EnsureSignedIn()
        {
            SetStatus("Connecting to services...");
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        private void SetStatus(string status)
        {
            Debug.Log("[Session] " + status);
            StatusChanged?.Invoke(status);
        }
    }
}
