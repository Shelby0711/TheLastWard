namespace LastWard.Core
{
    /// <summary>
    /// Implemented by anything a player can raycast-interact with (doors, pickups, notes,
    /// breakers, keypads). The M2 networking pass wraps the actual call in a ServerRpc —
    /// implementations here should assume they only run once authority has already validated it.
    /// </summary>
    public interface IInteractable
    {
        string GetPrompt();
        bool CanInteract(ulong playerId);
        void Interact(ulong playerId);
    }
}
