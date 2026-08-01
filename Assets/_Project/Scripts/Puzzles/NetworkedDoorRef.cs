// Split out of its original combined file. Unity resolves a MonoBehaviour's MonoScript by FILENAME:
// a component class that does not match the file it lives in cannot be serialised into a scene, so
// AddComponent at build time produced a component that arrived broken and silently did nothing.
// Every MonoBehaviour therefore gets its own file.
using LastWard.Core;
using LastWard.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>Thin wrapper so a puzzle can unlock a door without knowing which kind it is.</summary>
    public class NetworkedDoorRef : MonoBehaviour
    {
        [SerializeField] private LastWard.Net.NetworkedDoor door;
        public void Unlock() => door?.ServerSetLocked(false);
    }
}
