using System.Collections.Generic;

namespace BellumCivile.Behaviors
{
    public class ClaimFeudActionPreview
    {
        public string TitleId { get; set; }
        public string TitleName { get; set; }
        public string HolderClanId { get; set; }
        public string HolderName { get; set; }
        public FeudalClaimStrength Strength { get; set; }
        public float Score { get; set; }
        public bool IsEnabled { get; set; }
        public string DisabledReason { get; set; }
        public List<string> Reasons { get; } = new List<string>();
    }
}
