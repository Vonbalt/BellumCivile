using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    public enum HostageStatus { Reserved, PeacePledge, AwaitingDisposition, OrdinaryPrisoner }

    public sealed class HostageStatusSnapshot
    {
        public string PactId { get; }
        public Hero Hero { get; }
        public Clan SupplyingHouse { get; }
        public Clan ReceivingHouse { get; }
        public Settlement Holding { get; }
        public double EndDay { get; }
        public HostageStatus Status { get; }
        public bool AgreementActive { get; }
        public bool InTreatyCustody { get; }
        public bool Protected { get; }

        internal HostageStatusSnapshot(HostagePactRecord pact, TreatyHostageRecord slot,
            HostageStatus status, bool active, bool custody)
        {
            PactId = pact.Id; Hero = slot.Hero; SupplyingHouse = slot.SupplyingHouse;
            ReceivingHouse = slot.ReceivingHouse; Holding = slot.Holding; EndDay = pact.EndDay;
            Status = status; AgreementActive = active; InTreatyCustody = custody;
            Protected = custody && pact.Protects(slot.Hero);
        }
    }

    public sealed class CourtAgendaSnapshot
    {
        public string Id { get; }
        public Kingdom Realm { get; }
        public FactionType? Faction { get; }
        public Clan Sponsor { get; }
        public string Kind { get; }
        public string TargetId { get; }
        public string ActionId { get; }
        public bool Abolish { get; }
        public CourtAgendaState State { get; }
        public double SessionDay { get; }
        public double ResultVisibleUntilDay { get; }
        public bool ResultApplied { get; }

        internal CourtAgendaSnapshot(CourtAgendaRecord record)
        {
            Id = record.IntegrationId; Realm = record.Realm; Faction = record.Faction?.Type;
            Sponsor = record.Sponsor; Kind = record.ObjectiveData?.Kind ?? "policy";
            TargetId = record.ObjectiveData?.TargetId ?? record.PolicyId;
            ActionId = record.ObjectiveData?.ActionId; Abolish = record.Abolish;
            State = record.State; SessionDay = record.SessionDate.ToDays;
            ResultVisibleUntilDay = record.ResultVisibleUntil.ToDays; ResultApplied = record.ResultApplied;
        }
    }
}
