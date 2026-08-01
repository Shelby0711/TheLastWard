using Unity.Netcode.Components;
using UnityEngine;

namespace LastWard.Entity
{
    /// <summary>
    /// Safe wrapper around <see cref="NetworkTransform.Teleport"/>.
    ///
    /// Teleport throws outright — "Teleporting on non-authoritative side is not allowed!" — whenever
    /// <see cref="NetworkTransform.CanCommitToTransform"/> is false. That flag is only set inside
    /// NetworkTransform's OWN OnNetworkSpawn, and NGO dispatches spawn callbacks by iterating
    /// <c>NetworkObject.ChildNetworkBehaviours.Values</c> — a Dictionary. Dictionary order is not
    /// component order and is not guaranteed, so a controller that teleports from its own
    /// OnNetworkSpawn is racing the transform beside it: usually the transform wins and nothing
    /// happens, occasionally it loses and the spawn throws. Ordering the AddComponent calls in the
    /// builder does not help, which is why this looked fixed and was not.
    ///
    /// Skipping the call at spawn costs nothing. Every caller sets <c>transform.position</c> (or
    /// warps the agent) before asking, and NetworkTransform captures the transform as its initial
    /// replicated state when it does initialise — so the position still arrives, just via the
    /// normal spawn path instead of a teleport delta.
    ///
    /// After spawn, though, a refused teleport is a real problem: it means the transform is
    /// owner-authoritative on something the server is trying to move, and the Manager would appear
    /// to vanish for everyone but the host. That case warns rather than failing silently.
    /// </summary>
    public static class NetTransformSafety
    {
        public static void SafeTeleport(this NetworkTransform netTransform, Behaviour owner,
            Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (netTransform == null) return;

            if (netTransform.CanCommitToTransform)
            {
                netTransform.Teleport(position, rotation, scale);
                return;
            }

            if (netTransform.IsSpawned)
                Debug.LogWarning($"[Net] {owner?.name} tried to teleport a transform it has no " +
                                 "authority over. The move applied locally but will not replicate — " +
                                 "check whether this object should be server-authoritative.", owner);
        }
    }
}
