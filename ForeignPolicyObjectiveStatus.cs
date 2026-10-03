using System.Collections.Generic;

namespace BellumCivile
{
    public sealed class ForeignPolicyObjectiveStatus
    {
        public int TotalObjectives { get; set; }
        public int ControlledObjectives { get; set; }
        public bool HasObjectives => TotalObjectives > 0;
        public bool AllObjectivesAchieved => HasObjectives && ControlledObjectives >= TotalObjectives;
        public float CompletionRatio => TotalObjectives > 0
            ? ControlledObjectives / (float)TotalObjectives
            : 0f;
        public List<string> ControlledTitleIds { get; } = new List<string>();
        public List<string> OutstandingTitleIds { get; } = new List<string>();
    }
}
