using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;

namespace BellumCivile.Behaviors
{
    public sealed partial class HostagePactBehavior
    {
        internal float WarDeterrence(Clan clan, Kingdom target, bool council = false)
        {
            var realm = clan?.Kingdom;
            if (!BellumCivileOptions.EnableWarPeaceLogicRevamp || realm == null || target == null
                || clan.Leader == null || realm == target || realm.IsAtWarWith(target)) return 0;
            var pact = _pacts.FirstOrDefault(p => p?.Phase == HostagePactPhase.Active
                && ((p.FirstRealm == realm && p.SecondRealm == target) || (p.FirstRealm == target && p.SecondRealm == realm))
                && !p.IsDue(CampaignTime.Now.ToDays) && !p.FirstRealm.IsEliminated && !p.SecondRealm.IsEliminated
                && p.FirstRealm.RulingClan == p.FirstHouse && p.SecondRealm.RulingClan == p.SecondHouse
                && (p.FirstHostage == null || InTreatyCustody(p.FirstHostage))
                && (p.SecondHostage == null || InTreatyCustody(p.SecondHostage)));
            if (pact == null) return 0;
            var supplied = pact.FirstRealm == realm ? pact.FirstHostage : pact.SecondHostage;
            int tier = supplied?.Tier ?? 4;
            int honor = clan.Leader.GetTraitLevel(DefaultTraits.Honor), mercy = clan.Leader.GetTraitLevel(DefaultTraits.Mercy);
            bool ownHouse = supplied?.SupplyingHouse == clan;
            return (float)(council ? HostagePactRules.GetCouncilDeterrence(tier, honor, mercy, supplied != null, ownHouse)
                : HostagePactRules.GetWarDeterrence(tier, honor, mercy, supplied != null, ownHouse));
        }

        private static void ApplyPactMemories(HostagePactRecord pact)
        {
            if (pact.EndReason != HostagePactEndReason.War || pact.VoluntaryAggressor == null
                || pact.FirstHostage?.ActionCompleted == false || pact.SecondHostage?.ActionCompleted == false) return;
            if (!pact.BreachMemoryApplied)
            {
                pact.BreachMemoryApplied = true;
                // The aggressor may itself be holding the other realm's hostage.
                var heldByAggressor = pact.VoluntaryAggressor == pact.FirstRealm ? pact.SecondHostage : pact.FirstHostage;
                bool executed = heldByAggressor?.ExecutionSucceeded == true;
                RelationMemoryService.ApplyChange(pact.FirstHouse?.Leader, pact.SecondHouse?.Leader,
                    executed ? -30 : -20, true,
                    executed ? RelationMemorySources.BetrayedHostagePledge : RelationMemorySources.BrokeHostagePeace,
                    10, RelationMemoryScope.House);
            }
            ApplyClemency(pact, pact.FirstHostage, pact.FirstRealm);
            ApplyClemency(pact, pact.SecondHostage, pact.SecondRealm);
        }

        private static void ApplyClemency(HostagePactRecord pact, TreatyHostageRecord record, Kingdom supplier)
        {
            if (record == null || record.RelationsApplied) return;
            record.RelationsApplied = true;
            if (pact.VoluntaryAggressor != supplier || !record.ClemencyChosen
                || record.Outcome != HostageCustodyOutcome.Release || record.Hero?.IsAlive != true
                || record.Hero.IsPrisoner) return;
            RelationMemoryService.ApplyChange(record.SupplyingHouse?.Leader, record.ReceivingHouse?.Leader,
                10, true, RelationMemorySources.SparedTreatyHostage, 5, RelationMemoryScope.House);
        }
    }
}
