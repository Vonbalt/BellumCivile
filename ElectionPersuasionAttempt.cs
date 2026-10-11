using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    /// <summary>A single campaign-session attempt. Do not serialize or reuse after completion.</summary>
    public sealed class ElectionPersuasionAttempt
    {
        internal ElectionLobbyingBehavior Owner;
        internal ElectiveSuccessionRecord Ballot;
        internal int Mandate;
        internal bool Completed;
        public Kingdom Realm { get; }
        public Hero Speaker { get; }
        public Hero Candidate { get; }
        public CampaignTime Expires { get; }

        internal ElectionPersuasionAttempt(ElectionLobbyingBehavior owner, ElectiveSuccessionRecord ballot,
            Kingdom realm, Hero speaker, Hero candidate, CampaignTime until)
        {
            Owner = owner; Ballot = ballot; Mandate = ballot.MandateNumber;
            Realm = realm; Speaker = speaker; Candidate = candidate; Expires = until;
        }
    }
}
