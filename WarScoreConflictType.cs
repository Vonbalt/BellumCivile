namespace BellumCivile
{
    /// <summary>
    /// Defines which resolution layer consumes a terminal War Score result.
    /// The score itself is always stored from the primary side's perspective.
    /// </summary>
    public enum WarScoreConflictType
    {
        ForeignWar = 0,
        CivilWar = 1,
        ClaimFeud = 2
    }
}
