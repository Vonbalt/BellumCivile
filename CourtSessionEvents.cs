using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BellumCivile
{
    // Discovery is read-only. Execution uses only the saved recipients, never a fresh lottery.
    internal static class CourtSessionEvents
    {
        private static List<Town> Holdings(FactionObject faction, Kingdom realm) =>
            realm.Fiefs.Where(t => faction.Members.Contains(t.OwnerClan)).ToList();

        internal static List<CourtActivityTarget> FindTargets(CourtActivityDefinition definition, FactionObject faction, Kingdom realm)
        {
            var result = new List<CourtActivityTarget>();
            if (definition == null || faction == null || realm?.RulingClan?.Leader == null) return result;
            var holdings = Holdings(faction, realm);
            switch (definition.Effect)
            {
                case CourtActivityEffect.Towns:
                case CourtActivityEffect.Castles:
                case CourtActivityEffect.Holdings:
                    result.AddRange(holdings.Select(t => new CourtActivityTarget { Settlement = t.Settlement, Name = t.Name.ToString() }));
                    break;
                case CourtActivityEffect.Villages:
                    result.AddRange(holdings.SelectMany(t => t.Villages).Distinct()
                        .Select(v => new CourtActivityTarget { Settlement = v.Settlement, Name = v.Name.ToString() }));
                    break;
                case CourtActivityEffect.Peers:
                    result.AddRange(faction.Members.Where(c => c != realm.RulingClan && c.Kingdom == realm && !c.IsEliminated && c.Leader?.IsDead == false)
                        .Select(c => c.Leader).Distinct().Select(h => new CourtActivityTarget { Hero = h, Name = h.Name.ToString() }));
                    break;
                case CourtActivityEffect.Notables:
                    result.AddRange(holdings.SelectMany(t => t.Settlement.Notables).Where(h => !h.IsDead).Distinct()
                        .Select(h => new CourtActivityTarget { Hero = h, Name = h.Name.ToString() }));
                    break;
                case CourtActivityEffect.Armies:
                    result.AddRange(realm.Armies.Where(a => a.LeaderParty?.LeaderHero?.Clan != null && faction.Members.Contains(a.LeaderParty.LeaderHero.Clan))
                        .Select(a => new CourtActivityTarget { Army = a, Name = a.LeaderParty.LeaderHero.Name.ToString() }));
                    break;
                case CourtActivityEffect.Cavalry:
                case CourtActivityEffect.Veterans:
                    result.AddRange(TroopSelectionHelper.GetCultureEliteTroops(realm.Culture, definition.Effect == CourtActivityEffect.Cavalry)
                        .Select(t => new CourtActivityTarget { Hero = realm.RulingClan.Leader, Troop = t, Name = realm.RulingClan.Leader.Name.ToString() }));
                    break;
                default:
                    result.Add(new CourtActivityTarget { Hero = realm.RulingClan.Leader, Name = realm.RulingClan.Leader.Name.ToString() });
                    break;
            }
            return result.Where(t => IsValidTarget(definition, t, faction, realm)).ToList();
        }

        private static bool CanChange(float value, int delta, float ceiling = float.PositiveInfinity) =>
            delta > 0 ? value < ceiling : delta < 0 && value > 0;

        internal static bool IsValidTarget(CourtActivityDefinition d, CourtActivityTarget target, FactionObject faction, Kingdom realm)
        {
            if (d == null || target == null || faction == null || realm?.RulingClan?.Leader == null) return false;
            var ruler = realm.RulingClan;
            var town = target.Settlement?.Town;
            switch (d.Effect)
            {
                case CourtActivityEffect.Towns:
                case CourtActivityEffect.Castles:
                case CourtActivityEffect.Holdings:
                    return town != null && town.OwnerClan?.Kingdom == realm && faction.Members.Contains(town.OwnerClan)
                        && (d.Effect != CourtActivityEffect.Towns || town.IsTown)
                        && (d.Effect != CourtActivityEffect.Castles || town.IsCastle)
                        && (CanChange(town.Prosperity, d.Amount) || CanChange(town.Security, d.Security, 100) || CanChange(town.Loyalty, d.Loyalty, 100));
                case CourtActivityEffect.Villages:
                    return target.Settlement?.Village is Village village && Holdings(faction, realm).SelectMany(t => t.Villages).Contains(village)
                        && CanChange(village.Hearth, d.Amount);
                case CourtActivityEffect.Peers:
                    return target.Hero?.IsDead == false && target.Hero.Clan != ruler && target.Hero.Clan?.Kingdom == realm
                        && target.Hero.Clan.Leader == target.Hero && !target.Hero.Clan.IsEliminated && faction.Members.Contains(target.Hero.Clan)
                        && RelationCanChange(target.Hero, ruler.Leader, d.Amount);
                case CourtActivityEffect.Notables:
                    return target.Hero?.IsDead == false && Holdings(faction, realm).Any(t => t.Settlement.Notables.Contains(target.Hero))
                        && RelationCanChange(target.Hero, ruler.Leader, d.Amount);
                case CourtActivityEffect.Armies:
                    return target.Army != null && realm.Armies.Contains(target.Army) && target.Army.LeaderParty?.LeaderHero?.Clan != null
                        && faction.Members.Contains(target.Army.LeaderParty.LeaderHero.Clan) && CanChange(target.Army.Cohesion, d.Amount, 100);
                case CourtActivityEffect.Cavalry:
                case CourtActivityEffect.Veterans:
                    return target.Hero == ruler.Leader && !target.Hero.IsDead && !target.Hero.IsPrisoner && target.Hero.PartyBelongedTo != null
                        && target.Troop != null && !target.Troop.IsHero && target.Troop.Culture == realm.Culture
                        && target.Troop.Tier >= 5 && target.Troop.Tier <= 6 && (d.Effect != CourtActivityEffect.Cavalry || target.Troop.IsMounted);
                case CourtActivityEffect.Influence: return target.Hero == ruler.Leader && (d.Amount > 0 || ruler.Influence > 0);
                case CourtActivityEffect.Gold: return target.Hero == ruler.Leader && (d.Amount > 0 || target.Hero.Gold > 0);
                default: return false;
            }
        }

        private static bool RelationCanChange(Hero hero, Hero ruler, int amount) => hero != ruler
            && (amount > 0 ? hero.GetRelation(ruler) < 100 : hero.GetRelation(ruler) > -100);

        internal static CourtActivityRecord Prepare(CourtActivityDefinition definition, FactionObject faction, Kingdom realm)
        {
            var targets = FindTargets(definition, faction, realm);
            if (targets.Count == 0) return null;
            int count = definition.Effect == CourtActivityEffect.Cavalry || definition.Effect == CourtActivityEffect.Veterans ? 1 : Math.Min(3, targets.Count);
            var chosen = new List<CourtActivityTarget>();
            while (chosen.Count < count)
            {
                int index = MBRandom.RandomInt(targets.Count);
                chosen.Add(targets[index]);
                targets.RemoveAt(index);
            }
            return new CourtActivityRecord { EventId = definition.Id, Positive = definition.Positive, Ruler = realm.RulingClan.Leader,
                RulingClan = realm.RulingClan, Targets = chosen };
        }

        internal static bool ValidPlan(CourtActivityRecord plan, FactionObject faction, Kingdom realm)
        {
            var definition = CourtActivityCatalog.Find(plan?.EventId);
            return plan != null && definition != null && plan.Targets != null && realm != null && !realm.IsEliminated
                && faction?.ParentKingdom == realm && faction.IsIdeology && faction.Type == definition.Faction
                && plan.Positive == definition.Positive && realm.RulingClan == plan.RulingClan
                && plan.Ruler != null && !plan.Ruler.IsDead && realm.RulingClan?.Leader == plan.Ruler
                && CourtActivityRules.MatchesMood(faction.Mood, plan.Positive);
        }

        internal static void Execute(CourtActivityRecord plan, FactionObject faction, Kingdom realm)
        {
            if (!ValidPlan(plan, faction, realm) || plan.Finished) return;
            var definition = CourtActivityCatalog.Find(plan.EventId);
            foreach (var target in plan.Targets)
            {
                if (!ValidPlan(plan, faction, realm)) break;
                if (!IsValidTarget(definition, target, faction, realm) || !target.TryBegin()) continue;
                try
                {
                    Apply(definition, target, plan);
                    target.Completed = true;
                }
                catch (Exception ex)
                {
                    BellumCivileLogger.Log($"Court activity recipient interrupted; event={plan.EventId}; target={target.Name}; no replay: {ex}");
                }
            }
            plan.Finished = true;
        }

        private static void Apply(CourtActivityDefinition d, CourtActivityTarget target, CourtActivityRecord plan)
        {
            var changes = new List<string>();
            void Change(float before, float after, string unit)
            {
                target.Changed |= Math.Abs(after - before) >= .005f;
                changes.Add(CourtSessionEventReports.Change(before, after, new TextObject(unit)));
            }
            var ruler = plan.Ruler;
            var clan = plan.RulingClan;
            switch (d.Effect)
            {
                case CourtActivityEffect.Towns:
                case CourtActivityEffect.Castles:
                case CourtActivityEffect.Holdings:
                    var town = target.Settlement.Town;
                    float beforeProsperity = town.Prosperity, beforeSecurity = town.Security, beforeLoyalty = town.Loyalty;
                    town.Prosperity = Math.Max(0, town.Prosperity + d.Amount);
                    town.Security = Math.Max(0, Math.Min(100, town.Security + d.Security));
                    town.Loyalty = Math.Max(0, Math.Min(100, town.Loyalty + d.Loyalty));
                    if (d.Amount != 0) Change(beforeProsperity, town.Prosperity, "{=BC_CourtProsperityUnit}prosperity");
                    if (d.Security != 0) Change(beforeSecurity, town.Security, "{=BC_CourtSecurityUnit}security");
                    if (d.Loyalty != 0) Change(beforeLoyalty, town.Loyalty, "{=BC_CourtLoyaltyUnit}loyalty");
                    target.Name = town.Name.ToString();
                    break;
                case CourtActivityEffect.Villages:
                    var village = target.Settlement.Village;
                    float beforeHearths = village.Hearth;
                    village.Hearth = Math.Max(0, village.Hearth + d.Amount);
                    Change(beforeHearths, village.Hearth, "{=BC_CourtHearths}hearths");
                    target.Name = village.Name.ToString();
                    break;
                case CourtActivityEffect.Armies:
                    var army = target.Army;
                    float beforeCohesion = army.Cohesion;
                    army.Cohesion = Math.Max(0, Math.Min(100, army.Cohesion + d.Amount));
                    Change(beforeCohesion, army.Cohesion, "{=BC_CourtCohesion}army cohesion");
                    target.Name = army.LeaderParty.LeaderHero.Name.ToString();
                    break;
                case CourtActivityEffect.Peers:
                case CourtActivityEffect.Notables:
                    float beforeRelation = target.Hero.GetRelation(ruler);
                    RelationMemoryService.ApplyChange(target.Hero, ruler, d.Amount, false,
                        d.Positive ? RelationMemorySources.CourtCooperation : RelationMemorySources.CourtObstruction,
                        5f, target.Hero.Clan == null ? RelationMemoryScope.Personal : RelationMemoryScope.House,
                        d.AgendaText.ToString());
                    Change(beforeRelation, target.Hero.GetRelation(ruler), "{=BC_CourtRelationUnit}relation with the ruler");
                    target.Name = target.Hero.Name.ToString();
                    break;
                case CourtActivityEffect.Influence:
                    float beforeInfluence = clan.Influence, beforeRenown = clan.Renown;
                    if (d.Amount < 0) NpcInfluenceBudgetService.ApplyClampedLoss(clan, -d.Amount, "court_activity_" + d.Id);
                    else ChangeClanInfluenceAction.Apply(clan, d.Amount);
                    if (d.Renown > 0) clan.Renown += d.Renown;
                    Change(beforeInfluence, clan.Influence, "{=BC_CourtInfluence}influence");
                    if (d.Renown > 0) Change(beforeRenown, clan.Renown, "{=BC_CourtRenown}renown");
                    target.Name = clan.Name.ToString();
                    break;
                case CourtActivityEffect.Gold:
                    int beforeGold = ruler.Gold;
                    if (d.Amount > 0) GiveGoldAction.ApplyBetweenCharacters(null, ruler, d.Amount, true);
                    else GiveGoldAction.ApplyBetweenCharacters(ruler, null, Math.Min(ruler.Gold, -d.Amount), true);
                    Change(beforeGold, ruler.Gold, "{=BC_CourtDenars}denars");
                    target.Name = ruler.Name.ToString();
                    break;
                case CourtActivityEffect.Cavalry:
                case CourtActivityEffect.Veterans:
                    var roster = ruler.PartyBelongedTo.MemberRoster;
                    int beforeTroops = roster.GetTroopCount(target.Troop);
                    roster.AddToCounts(target.Troop, d.Amount);
                    Change(beforeTroops, roster.GetTroopCount(target.Troop), "{=!}" + target.Troop.Name);
                    target.Name = new TextObject("{=BC_CourtEvent_Retinue}{RULER}'s retinue").SetTextVariable("RULER", ruler.Name).ToString();
                    break;
            }
            target.Report = CourtSessionEventReports.ForTarget(target.Name, string.Join(", ", changes));
        }

        internal static void Report(CourtActivityRecord plan, FactionObject faction, Kingdom realm)
        {
            var applied = plan.Targets.Where(t => t.Completed && t.Changed).ToList();
            bool interrupted = plan.Targets.Any(t => t.Started && !t.Completed);
            if (applied.Count == 0 && !interrupted) return;
            var narrative = applied.Count > 0 ? CourtSessionEventReports.Narrative(plan.EventId)
                : new TextObject("{=BC_CourtActivityInterrupted}The undertaking of the {FACTION} in {REALM} was interrupted. Its effects could not be fully confirmed.");
            narrative.SetTextVariable("FACTION", faction.GetDisplayName());
            narrative.SetTextVariable("REALM", realm.Name);
            narrative.SetTextVariable("RULER", plan.Ruler.Name);
            narrative.SetTextVariable("TARGETS", string.Join(", ", applied.Select(t => t.Name)));
            string effects = string.Join("; ", applied.Select(t => t.Report));
            if (interrupted && applied.Count > 0)
                effects += "\n" + new TextObject("{=BC_CourtActivityPartial}Part of this undertaking was interrupted; unconfirmed effects will not be repeated.");
            var text = new TextObject("{=BC_CourtSessionReport}{REPORT}\n{EFFECT}")
                .SetTextVariable("REPORT", narrative).SetTextVariable("EFFECT", effects);
            BellumCivileNotifications.Show(text, plan.Positive ? BellumNotificationColors.Success : BellumNotificationColors.Danger, primaryKingdom: realm);
        }
    }
}
