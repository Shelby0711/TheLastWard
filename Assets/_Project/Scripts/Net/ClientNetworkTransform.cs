using Unity.Netcode.Components;
using UnityEngine;

namespace LastWard.Net
{
    /// <summary>
    /// Owner-authoritative transform. Per PROJECT_CONTEXT.md's sync-surface table, player movement
    /// is owner-authoritative (cheating doesn't matter in co-op, and this avoids server-side
    /// prediction work). Server-authoritative objects (the Entity, in M3) use a plain NetworkTransform.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
