using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CourtAgendaBehavior
    {
        private Dictionary<string, string> _crownOwners = new Dictionary<string, string>();
        // -1 is impartial and selectable; legacy -2 choices are also selectable. Bloc IDs lock the term.
        private Dictionary<string, int> _crownFavor = new Dictionary<string, int>();
        private Dictionary<string, CampaignTime> _crownUntil = new Dictionary<string, CampaignTime>();
        private Dictionary<string, string> _crownReigningHeroes = new Dictionary<string, string>();
        private Dictionary<string, CampaignTime> _crownReignStarts = new Dictionary<string, CampaignTime>();
        private readonly HashSet<string> _crownReconciled = new HashSet<string>();

        private void SyncCrownData(IDataStore store)
        {
            store.SyncData("BC_CrownOwners", ref _crownOwners);
            store.SyncData("BC_CrownFavor", ref _crownFavor);
            store.SyncData("BC_CrownFavorUntil", ref _crownUntil);
            store.SyncData("BC_CrownReigningHeroes", ref _crownReigningHeroes);
            store.SyncData("BC_CrownReignStarts", ref _crownReignStarts);
            _crownOwners = _crownOwners ?? new Dictionary<string, string>();
            _crownFavor = _crownFavor ?? new Dictionary<string, int>();
            _crownUntil = _crownUntil ?? new Dictionary<string, CampaignTime>();
            _crownReigningHeroes = _crownReigningHeroes ?? new Dictionary<string, string>();
            _crownReignStarts = _crownReignStarts ?? new Dictionary<string, CampaignTime>();
        }

        private CampaignTime UpdateCrownReign(Kingdom realm, bool newRealm = false) =>
            CrownReignClock.Update(_crownReigningHeroes, _crownReignStarts, realm.StringId,
                (RegencyBehavior.Instance?.GetLegalClanHead(realm.RulingClan) ?? realm.RulingClan.Leader).StringId,
                CampaignTime.Now, Campaign.Current.Models.CampaignTimeModel.CampaignStartTime, newRealm);

        public string DescribeCrownReign(Kingdom realm)
        {
            if (!ValidRealm(realm)) return "";
            if (ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm) != null)
                return new TextObject("{=BC_Deposition_Caretaker}Governing until the election").ToString();
            int months = CrownReignClock.Months(CampaignTime.Now.ToDays - UpdateCrownReign(realm).ToDays, CampaignTime.DaysInYear);
            if (months == 0) return new TextObject("{=BC_CrownReignNew}Reigning for less than a month").ToString();
            int years = months / 12;
            int remainder = months % 12;
            var yearText = new TextObject(years == 1 ? "{=BC_CrownReignYear}1 year" : "{=BC_CrownReignYears}{COUNT} years");
            yearText.SetTextVariable("COUNT", years);
            var monthText = new TextObject(remainder == 1 ? "{=BC_CrownReignMonth}1 month" : "{=BC_CrownReignMonths}{COUNT} months");
            monthText.SetTextVariable("COUNT", remainder);
            var text = new TextObject(years > 0 && remainder > 0 ? "{=BC_CrownReignYearsMonths}Reigning for {YEARS} and {MONTHS}"
                : "{=BC_CrownReignDuration}Reigning for {DURATION}");
            text.SetTextVariable("YEARS", yearText);
            text.SetTextVariable("MONTHS", monthText);
            text.SetTextVariable("DURATION", years > 0 ? yearText : monthText);
            return text.ToString();
        }

        private void ReconcileCrowns()
        {
            foreach (Kingdom realm in Kingdom.All)
                if (ValidRealm(realm)) ReconcileCrown(realm);
        }

        internal void ReconcileCrown(Kingdom realm)
        {
            if (!ValidRealm(realm)) return;
            UpdateCrownReign(realm);
            string id = realm.StringId, ruler = realm.RulingClan.StringId;
            bool changed = _crownOwners.TryGetValue(id, out string old) && old != ruler;
            if (!changed && _crownReconciled.Contains(id)) return;
            var manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager == null) return;
            _crownOwners[id] = ruler;
            _crownReconciled.Add(id);
            if (changed)
            {
                _crownFavor.Remove(id);
                _crownUntil.Remove(id);
            }
            foreach (var faction in Kingdom.All.SelectMany(manager.GetFactionsInKingdom).Where(f => f.IsIdeology).ToList())
            {
                if (!faction.Members.Contains(realm.RulingClan) && faction.Leader != realm.RulingClan) continue;
                Kingdom courtRealm = faction.ParentKingdom;
                bool leaderRemoved = faction.Leader == realm.RulingClan;
                faction.RemoveMember(realm.RulingClan);
                if (leaderRemoved)
                    faction.Leader = faction.Members.Where(c => CourtMembershipEligibility.CanBelong(c, courtRealm) && c.Leader?.IsAlive == true)
                        .OrderByDescending(RebellionPowerHelper.CalculateClanPower).ThenByDescending(c => c.Influence)
                        .ThenByDescending(c => c.Tier).ThenBy(c => c.StringId).FirstOrDefault();
                if (faction.Members.Count == 0 || faction.Leader == null) manager.RemoveFaction(faction);
                foreach (var agenda in _agendas.Where(a => a.Faction == faction && a.IsUnopened).ToList())
                    if (agenda.Sponsor == realm.RulingClan || MemberCount(faction) < 2) Cancel(agenda, "accession_invalidated_unfiled_faction_motion");
            }
            foreach (var agenda in _agendas.Where(a => a.Realm == realm && a.IsUnopened && a.Faction == null
                && (changed || a.Sponsor != realm.RulingClan)).ToList()) Cancel(agenda, "crown_owner_changed_before_filing");
            manager.InvalidateFactionLookupCache();
            BellumCivileLogger.Log($"Crown reconciled; realm={id}; former={old}; ruler={ruler}; changed={changed}.");
        }

        public FactionType? GetFavoredBloc(Kingdom realm)
        {
            if (!ValidRealm(realm) || !_crownOwners.TryGetValue(realm.StringId, out string owner)
                || owner != realm.RulingClan.StringId || !_crownUntil.TryGetValue(realm.StringId, out var until)
                || until.ToDays <= CampaignTime.Now.ToDays || !_crownFavor.TryGetValue(realm.StringId, out int value)
                || value < 0 || !CourtFactionRoster.Types.Contains((FactionType)value)) return null;
            return (FactionType)value;
        }

        public int GetFavorMood(FactionObject faction)
        {
            var favored = GetFavoredBloc(faction?.ParentKingdom);
            return !favored.HasValue ? 0 : favored.Value == faction.Type ? 10 : -5;
        }

        public string DescribeCrownFavor(Kingdom realm)
        {
            var favored = GetFavoredBloc(realm);
            var text = new TextObject("{=BC_CrownCurrentFavor}Current favoring: {CHOICE}");
            text.SetTextVariable("CHOICE", favored.HasValue ? CourtInstitutionDisplayHelper.GetCourtFactionName(favored.Value, realm)
                : new TextObject("{=BC_CrownImpartial}Impartial"));
            return text.ToString();
        }

        private void OpenCrownTerm(Kingdom realm, FactionManagerBehavior manager)
        {
            _crownUntil[realm.StringId] = _nextTerms[realm.StringId];
            _crownFavor[realm.StringId] = -1;
            var factions = manager.GetFactionsInKingdom(realm).Where(f => f.IsIdeology && MemberCount(f) > 0).ToList();
            if (realm.RulingClan == Clan.PlayerClan)
            {
                return;
            }
            Hero hero = realm.RulingClan.Leader;
            var personality = CourtAffiliationMath.Personality(hero.GetTraitLevel(DefaultTraits.Honor), hero.GetTraitLevel(DefaultTraits.Generosity),
                hero.GetTraitLevel(DefaultTraits.Mercy), hero.GetTraitLevel(DefaultTraits.Valor), hero.GetTraitLevel(DefaultTraits.Calculating));
            var types = CourtFactionRoster.Types.ToArray();
            int[] houses = types.Select(t => factions.Where(f => f.Type == t).SelectMany(f => f.Members)
                .Where(c => CourtMembershipEligibility.CanBelong(c, realm)).Distinct().Count()).ToArray();
            double[] moods = types.Select(t => (double)(factions.FirstOrDefault(f => f.Type == t)?.Mood ?? 0)).ToArray();
            int eligible = realm.Clans.Count(c => CourtMembershipEligibility.CanBelong(c, realm));
            var scores = types.Select((t, i) => new { Type = t, Score = CrownFavoritismMath.Score(personality[t], i, houses, moods, eligible) })
                .OrderByDescending(s => s.Score).ToList();
            if (scores[0].Score >= 5 && scores[0].Score - scores[1].Score > 1e-9
                && NpcInfluenceBudgetService.TrySpend(realm.RulingClan, CrownFavorSelectionRules.Cost,
                    NpcInfluenceExpenseKind.Discretionary, "crown_favor"))
                _crownFavor[realm.StringId] = (int)scores[0].Type;
            AnnounceCrownFavor(realm);
        }

        public CampaignTime? GetCrownFavorExpiry(Kingdom realm) =>
            realm != null && _crownUntil.TryGetValue(realm.StringId, out var date) ? (CampaignTime?)date : null;

        public bool CanSelectCrownFavor(Kingdom realm)
            => CrownFavorChoiceOpen(realm) && NpcInfluenceBudgetService.CanAfford(realm.RulingClan,
                CrownFavorSelectionRules.Cost, NpcInfluenceExpenseKind.Discretionary);

        private bool CrownFavorChoiceOpen(Kingdom realm)
        {
            if (!ValidRealm(realm)) return false;
            return CrownFavorSelectionRules.CanSelect(realm.RulingClan == Clan.PlayerClan,
                _crownOwners.TryGetValue(realm.StringId, out var owner) && owner == realm.RulingClan.StringId,
                _crownFavor.TryGetValue(realm.StringId, out int choice) ? (int?)choice : null,
                GetCrownFavorExpiry(realm)?.ToDays ?? 0, CampaignTime.Now.ToDays);
        }

        public TextObject GetCrownFavorSelectionHint(Kingdom realm)
        {
            if (!ValidRealm(realm) || realm.RulingClan != Clan.PlayerClan)
                return new TextObject("{=BC_CrownFavorRulerOnly}Only the ruler can sponsor a court faction.");
            if (CrownFavorChoiceOpen(realm))
                return new TextObject(CanSelectCrownFavor(realm)
                    ? "{=BC_CrownFavorSelect}Spend {COST} influence to sponsor this faction for the rest of the term. This choice cannot be changed until the next term."
                    : "{=BC_CrownFavorUnaffordable}You need {COST} influence to sponsor a court faction.")
                    .SetTextVariable("COST", CrownFavorSelectionRules.Cost);
            var expiry = GetCrownFavorExpiry(realm);
            if (expiry.HasValue && expiry.Value.ToDays > CampaignTime.Now.ToDays)
            {
                var hint = new TextObject("{=BC_CrownFavorLocked}Choice locked for {DAYS} more days, until the next court term.");
                hint.SetTextVariable("DAYS", (int)Math.Ceiling(expiry.Value.ToDays - CampaignTime.Now.ToDays));
                return hint;
            }
            return new TextObject("{=BC_CrownFavorNextTerm}Sponsorship becomes available when the next court term begins.");
        }

        public bool TrySelectCrownFavor(Kingdom realm, FactionObject faction, Clan expectedRuler,
            Hero expectedPlayer, CampaignTime? expectedExpiry)
        {
            ReconcileCrown(realm);
            if (!CanSelectCrownFavor(realm) || realm.RulingClan != expectedRuler || Hero.MainHero != expectedPlayer
                || !expectedExpiry.HasValue || GetCrownFavorExpiry(realm) != expectedExpiry
                || faction == null || !faction.IsIdeology || faction.ParentKingdom != realm || MemberCount(faction) == 0)
                return false;
            var manager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager?.GetFactionsInKingdom(realm).Contains(faction) != true) return false;
            if (!NpcInfluenceBudgetService.TrySpend(realm.RulingClan, CrownFavorSelectionRules.Cost,
                NpcInfluenceExpenseKind.Discretionary, "crown_favor")) return false;
            _crownFavor[realm.StringId] = (int)faction.Type;
            AnnounceCrownFavor(realm);
            return true;
        }

        private void AnnounceCrownFavor(Kingdom realm)
        {
            var message = new TextObject("{=BC_CrownFavorAnnouncement}Court of {REALM}: {FAVOR}");
            message.SetTextVariable("REALM", realm.Name);
            message.SetTextVariable("FAVOR", DescribeCrownFavor(realm));
            BellumCivileNotifications.Show(message, BellumNotificationColors.Politics, primaryKingdom: realm);
            BellumCivileLogger.Log($"Crown favor selected; realm={realm.StringId}; ruler={realm.RulingClan.StringId}; favor={_crownFavor[realm.StringId]}; expires={_crownUntil[realm.StringId].ToDays}.");
        }
    }
}
