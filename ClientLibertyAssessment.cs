using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace BellumCivile
{
    public sealed class ClientLibertyReason
    {
        public string Label { get; }
        public float Amount { get; }

        public ClientLibertyReason(string label, float amount)
        {
            Label = label ?? string.Empty;
            Amount = amount;
        }
    }

    public sealed class ClientClanLibertyAssessment
    {
        public Clan Clan { get; }
        public float LibertyDesire { get; }
        public IReadOnlyList<ClientLibertyReason> Reasons { get; }

        public ClientClanLibertyAssessment(Clan clan, float libertyDesire, IReadOnlyList<ClientLibertyReason> reasons)
        {
            Clan = clan;
            LibertyDesire = libertyDesire;
            Reasons = reasons ?? new List<ClientLibertyReason>();
        }
    }

    public sealed class ClientLibertyAssessment
    {
        public Kingdom ClientKingdom { get; set; }
        public Kingdom SuzerainKingdom { get; set; }
        public bool IsLawfulSuzerainty { get; set; }
        public bool WasVoluntary { get; set; }
        public float RealmLibertyDesire { get; set; }
        public float EffectiveClientPower { get; set; }
        public float SuzerainBlocPower { get; set; }
        public float RequiredPowerRatio { get; set; }
        public float LiberationReadiness { get; set; }
        public float CooldownRemainingDays { get; set; }
        public bool CanAttemptLiberation { get; set; }
        public string BlockReason { get; set; }
        public IReadOnlyList<ClientClanLibertyAssessment> Clans { get; set; } = new List<ClientClanLibertyAssessment>();
    }
}
