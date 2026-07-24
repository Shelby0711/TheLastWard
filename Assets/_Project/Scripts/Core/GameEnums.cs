namespace LastWard.Core
{
    public enum EntityState
    {
        Patrol,
        Investigate,
        Search,
        Chase,
        LostTarget,
        Return,
        /// <summary>
        /// Deliberately breaks off a live chase and clears out — the "it was right behind me, then
        /// it was just gone" beat. Distinct from LostTarget/Return, which only happen when it has
        /// already lost you. Appended last so existing serialized values keep their numbers.
        /// </summary>
        Withdraw,
        /// <summary>Circling at a distance — close enough to be heard, not closing in.</summary>
        Stalk,
        /// <summary>Stopped dead, facing a player, watching. Ends in either a rush or a vanish.</summary>
        Stare,
        /// <summary>
        /// Not in the building. Parked far away, senses off, hunting nobody. The default state
        /// early on — an Entity that is always somewhere is an Entity you can map, and once you can
        /// map it you stop being afraid of it.
        /// </summary>
        Dormant
    }

    public enum NoiseSource
    {
        Footstep,
        Sprint,
        Door,
        PuzzleInteraction,
        Voice,
        Other
    }

    public enum AftermathType
    {
        Warning,
        FalseClue,
        Mockery
    }

    public enum ObjectiveStage
    {
        Exterior,
        Lobby,
        OrphanWard,
        ServiceCorridor,
        ExitRoute,
        Ended
    }
}
