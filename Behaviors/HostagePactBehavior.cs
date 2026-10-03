using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public sealed partial class HostagePactBehavior : CampaignBehaviorBase
    {
        private List<HostagePactRecord> _pacts = new List<HostagePactRecord>();
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, RegisterHostageDialogues);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, ProcessHostageJudgment);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, MaintainCustody);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnHoldingOwnerChanged);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnPactWarDeclared);
        }
        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_HostagePacts", ref _pacts);
            if (_pacts == null) _pacts = new List<HostagePactRecord>();
        }
        internal bool IsProtectedHostage(Hero hero) => hero != null && _pacts.Any(p => p?.Protects(hero) == true);
        internal bool HasPendingPartitionCustody(Kingdom realm) => realm != null && _pacts.Any(p => p != null
            && p.Phase != HostagePactPhase.Ended && (p.FirstRealm == realm || p.SecondRealm == realm));
        internal HostagePactRecord GetProtectedPact(Hero hero)
            => hero == null ? null : _pacts.FirstOrDefault(p => p?.Protects(hero) == true);
        internal IEnumerable<HostagePactRecord> GetHistory(Hero hero)
            => _pacts.Where(p => hero != null && p != null && p.Phase != HostagePactPhase.Preparing && p.EndDay > 0
                && (p.FirstHostage?.Hero == hero || p.SecondHostage?.Hero == hero));
        internal IReadOnlyList<HostagePactRecord> GetActivePacts(Kingdom realm)
        {
            if (realm == null || !BellumCivileOptions.EnableWarPeaceLogicRevamp)
                return new HostagePactRecord[0];
            double day = CampaignTime.Now.ToDays;
            return _pacts.Where(p => IsCurrentAgreement(p, realm, day)
                && !p.FirstRealm.IsEliminated && !p.SecondRealm.IsEliminated
                && p.FirstRealm.RulingClan == p.FirstHouse && p.SecondRealm.RulingClan == p.SecondHouse
                && !p.FirstRealm.IsAtWarWith(p.SecondRealm)
                && !Dead(p.FirstHostage) && !Dead(p.SecondHostage)).ToList();
        }

        internal static bool IsCurrentAgreement(HostagePactRecord pact, Kingdom realm, double day)
            => realm != null && pact?.Phase == HostagePactPhase.Active
                && pact.FirstRealm != null && pact.SecondRealm != null
                && (pact.FirstRealm == realm || pact.SecondRealm == realm)
                && pact.EndDay > day && !double.IsInfinity(pact.EndDay)
                && !double.IsNaN(day) && !double.IsInfinity(day);
        internal bool IsHostageOriginCaptive(Hero hero) => hero?.IsPrisoner == true && _pacts.Any(p => p != null
            && (IsContinuingCustody(p.FirstHostage, hero) || IsContinuingCustody(p.SecondHostage, hero)));
        private static bool IsContinuingCustody(TreatyHostageRecord record, Hero hero)
            => record?.Hero == hero && record.CustodyEstablished
                && SameCaptivity(record)
                && (!record.ActionCompleted || record.Outcome == HostageCustodyOutcome.Retain);
        internal bool IsReserved(Hero hero) => hero != null && _pacts.Any(p => p != null
            && p.Phase != HostagePactPhase.Ended && (p.FirstHostage?.Hero == hero || p.SecondHostage?.Hero == hero));
    }
}
