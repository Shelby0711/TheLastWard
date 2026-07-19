namespace LastWard.Core
{
    public enum EntityState
    {
        Patrol,
        Investigate,
        Search,
        Chase,
        LostTarget,
        Return
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
