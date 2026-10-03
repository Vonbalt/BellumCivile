using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BellumCivile.Patches
{
    // Temporary, observational only: remove after the leaderless-clan source is identified.
    [HarmonyPatch(typeof(ClanVariablesCampaignBehavior), "DailyTickClan")]
    public static class ClanFinanceDiagnosticPatch
    {
        private const string Tag = "[BellumCivile clan finance diagnostic]";

        [HarmonyPrefix]
        public static void Prefix(Clan clan)
        {
            if (clan != null && (clan.IsBanditFaction || clan.Leader != null)) return;
            try
            {
                Write("identity", () => $"clan={clan?.StringId ?? "null"}; leader=null; native_tick_unchanged=true");
                if (clan == null) return;
                Write("state", () => $"day={CampaignTime.Now.ToDays:F3}; eliminated={clan.IsEliminated}; "
                    + $"minor={clan.IsMinorFaction}; rebel={clan.IsRebelClan}; bandit={clan.IsBanditFaction}; "
                    + $"mercenary={clan.IsUnderMercenaryService}; realm={clan.Kingdom?.StringId}; "
                    + $"ruling_house={clan.Kingdom?.RulingClan?.StringId}; realm_eliminated={clan.Kingdom?.IsEliminated}; "
                    + $"home={clan.HomeSettlement?.StringId}; initial_home={clan.InitialHomeSettlement?.StringId}");
                Write("members", () => Join(clan.Heroes.Select(h => h == null ? "null" :
                    $"hero={h.StringId},house={h.Clan?.StringId},alive={h.IsAlive},disabled={h.IsDisabled},"
                    + $"child={h.IsChild},prisoner={h.IsPrisoner},party={h.PartyBelongedTo?.StringId}")));
                Write("holdings", () => Join(clan.Settlements.Select(s => s.StringId)));
                Write("parties", () => Join(clan.WarPartyComponents.Select(p =>
                    $"party={p?.MobileParty?.StringId},head={p?.MobileParty?.LeaderHero?.StringId},"
                    + $"actual_house={p?.MobileParty?.ActualClan?.StringId}")));
                Write("regencies", () => DescribeRegencies(clan,
                    Campaign.Current?.GetCampaignBehavior<RegencyBehavior>()));
                Write("accessions", () => DescribeAccessions(clan,
                    Campaign.Current?.GetCampaignBehavior<CrownAccessionBehavior>()));
                Write("estates", () => DescribeEstates(clan,
                    Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()));
                Write("partitions", () => DescribePartitions(clan,
                    Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()));
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"{Tag} diagnostic_failed={ex.GetType().Name}: {ex.Message}");
            }
            finally { BellumCivileLogger.Flush(); }
        }

        private static void Write(string section, Func<string> describe)
        {
            try { BellumCivileLogger.Log($"{Tag} {section}: {describe()}"); }
            catch (Exception ex)
            {
                BellumCivileLogger.Log($"{Tag} {section}: unavailable={ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string Join(IEnumerable<string> rows)
        {
            var list = rows.Take(21).ToList();
            return list.Count == 0 ? "none" : string.Join(" | ", list.Take(20))
                + (list.Count > 20 ? " | truncated_after_20" : "");
        }

        // Read journals directly: some apparent query helpers also queue destruction or repairs.
        private static IEnumerable<T> Records<T>(object behavior, string field)
        {
            if (behavior == null) return Enumerable.Empty<T>();
            var member = AccessTools.Field(behavior.GetType(), field);
            if (member == null) throw new MissingFieldException(behavior.GetType().Name, field);
            return (IEnumerable<T>)member.GetValue(behavior) ?? Enumerable.Empty<T>();
        }

        private static bool Related(CrownAccessionRecord r, Clan clan) => r != null
            && (r.Cadet == clan || r.CadetId == clan.StringId || r.PreviousHouse == clan
                || r.IncomingSourceHouse == clan || r.Heir?.Clan == clan || r.ForeignMovingClan == clan);

        private static string Accession(CrownAccessionRecord r) => r == null ? "none" :
            $"realm={r.Realm?.StringId},predecessor={r.Predecessor?.StringId},heir={r.Heir?.StringId},"
            + $"source={r.EndowmentHouse?.StringId},cadet={r.Cadet?.StringId},planned_cadet={r.CadetId},"
            + $"initialized={r.CadetInitialized},announced={r.CadetAnnounced},completed={r.Completed},"
            + $"regency_required={r.RequiresRegency},union={r.Union != null},failure={r.AbdicationFailure}";

        private static string DescribeAccessions(Clan clan, CrownAccessionBehavior behavior) =>
            Join(Records<CrownAccessionRecord>(behavior, "_accessions").Where(r => Related(r, clan)).Select(Accession));

        private static string DescribeRegencies(Clan clan, RegencyBehavior behavior) =>
            Join(Records<RegencyRecord>(behavior, "_regencies").Where(r => r != null && r.ClanId == clan.StringId)
                .Select(r => $"ward={r.WardHeroId},regent={r.RegentHeroId},predecessor={r.PredecessorHeroId},generated={r.RegentWasGenerated}"));

        private static string DescribeEstates(Clan clan, PartitionSuccessionBehavior behavior) =>
            Join(Records<CrossClanEstateRecord>(behavior, "_crossClanEstates").Where(r => r != null)
                .SelectMany(r => (r.Shares ?? new List<CrossClanEstateShare>())
                    .Where(s => s != null && (r.Source == clan || s.Recipient == clan || Related(s.CadetPlan, clan)))
                    .Select(s => $"deceased={r.Deceased?.StringId},source={r.Source?.StringId},estate_completed={r.Completed},"
                        + $"heir={s.Heir?.StringId},recipient={s.Recipient?.StringId},share_completed={s.Completed},"
                        + $"landed_settled={s.LandedSettled},status={s.Status},failure={r.Failure},cadet_plan=[{Accession(s.CadetPlan)}]")));

        private static string DescribePartitions(Clan clan, PartitionSuccessionBehavior behavior) =>
            Join(Records<PendingPartitionSuccessionRecord>(behavior, "_pendingPartitions")
                .Where(r => r != null && (r.ParentClanId == clan.StringId
                    || r.CrownPromotions?.Any(p => p != null && (p.Founder == clan || p.FounderId == clan.StringId)) == true
                    || r.EstateShares?.Any(s => s != null && (s.Recipient == clan || Related(s.CadetPlan, clan))) == true))
                .Select(r => $"source={r.ParentClanId},deceased={r.DeadLeaderId},heirs={r.HeirIds},"
                    + $"ready_day={r.ReadyDate.ToDays:F3},routing_failure={r.CrownRoutingFailure}"));
    }
}
