using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed class SuccessionLawBehavior : CampaignBehaviorBase
    {
        public const int LawChangeInfluenceCost = 1000;
        public const int LawChangeVassalRelationPenalty = -25;
        private const float GenderLineEscheatMaxPendingDays = 30f;
        private List<PendingGenderLineEscheatRecord> _pendingGenderLineEscheats =
            new List<PendingGenderLineEscheatRecord>();
        private Dictionary<string, string> _knownClanLeaderIds =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public static SuccessionLawBehavior Instance { get; private set; }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            Instance = this;
            dataStore.SyncData("BellumCivile_PendingGenderLineEscheats", ref _pendingGenderLineEscheats);
            dataStore.SyncData("BellumCivile_KnownSuccessionClanLeaders", ref _knownClanLeaderIds);
            EnsureCollectionsInitialized();
            PrunePendingGenderLineEscheats();
        }

        public SuccessionLawSet GetLawsForClan(
            Clan clan,
            out SuccessionConfig.SuccessionRuleScope scope)
        {
            if (clan?.Kingdom != null)
                return GetLawsForKingdom(clan.Kingdom, out scope);

            string cultureId = clan?.Culture?.StringId;
            return SuccessionConfig.Instance.GetDefaultLaws(cultureId, null, out scope);
        }

        public SuccessionLawSet GetLawsForKingdom(
            Kingdom kingdom,
            out SuccessionConfig.SuccessionRuleScope scope)
        {
            Kingdom permanentRealm = ResolvePermanentRealm(kingdom);
            if (permanentRealm == null || string.IsNullOrWhiteSpace(permanentRealm.StringId))
            {
                string cultureId = kingdom?.Culture?.StringId;
                return SuccessionConfig.Instance.GetDefaultLaws(cultureId, null, out scope);
            }

            scope = SuccessionConfig.SuccessionRuleScope.Kingdom;
            return Campaign.Current.GetCampaignBehavior<RealmLawBehavior>().GetSuccessionLaws(permanentRealm);
        }

        public bool TryApplyPlayerGenderLaw(Kingdom kingdom, GenderSuccessionLaw law, out TextObject failureReason)
        {
            if (!Enum.IsDefined(typeof(GenderSuccessionLaw), law))
            {
                failureReason = new TextObject("{=BC_LawGroup_Unknown}This law is not registered.");
                return false;
            }
            return ApplyPlayerDefinition(kingdom, RealmLawRegistry.Instance.ForGender(law), out failureReason);
        }

        public bool TryApplyPlayerSuccessionLaw(Kingdom kingdom, HouseSuccessionLaw law, out TextObject failureReason)
        {
            if (!Enum.IsDefined(typeof(HouseSuccessionLaw), law))
            {
                failureReason = new TextObject("{=BC_LawGroup_Unknown}This law is not registered.");
                return false;
            }
            return ApplyPlayerDefinition(kingdom, RealmLawRegistry.Instance.ForSuccession(law), out failureReason);
        }

        private bool ApplyPlayerDefinition(Kingdom realm, RealmLawDefinition law, out TextObject failure)
        {
            if (!CanPlayerChangeLaws(realm, out failure)) return false;
            var groups = Campaign.Current.GetCampaignBehavior<RealmLawBehavior>();
            return groups.TryApplyPlayerLaw(realm, law.Id, groups.GetActiveLawId(realm, law.GroupId), out failure);
        }

        public bool CanPlayerChangeLaws(Kingdom kingdom, out TextObject failureReason)
        {
            Kingdom permanentRealm = ResolvePermanentRealm(kingdom);
            if (kingdom == null || permanentRealm == null || permanentRealm != kingdom)
            {
                failureReason = new TextObject("{=BC_SuccessionLaw_TemporaryRealm}Succession law cannot be altered while this is a temporary wartime realm.");
                return false;
            }

            if (Clan.PlayerClan == null || kingdom.RulingClan != Clan.PlayerClan)
            {
                failureReason = new TextObject("{=BC_SuccessionLaw_RulerOnly}Only the ruler may alter the realm's laws of succession.");
                return false;
            }

            if (CrownAccessionBehavior.Instance?.IsPending(kingdom) == true
                || ElectiveSuccessionBehavior.Instance?.Get(kingdom)?.ReformElectionPending == true
                || ElectiveSuccessionBehavior.Instance?.PendingDeposition(kingdom) != null
                || kingdom.UnresolvedDecisions?.OfType<KingSelectionKingdomDecision>().Any() == true)
            {
                failureReason = new TextObject("{=BC_SuccessionLaw_ElectionPending}The succession cannot be rewritten while the crown itself is being decided.");
                return false;
            }

            if (Clan.PlayerClan.Influence < LawChangeInfluenceCost)
            {
                TextObject text = new TextObject("{=BC_SuccessionLaw_InsufficientInfluence}You need {COST} influence to alter a law of succession.");
                text.SetTextVariable("COST", LawChangeInfluenceCost);
                failureReason = text;
                return false;
            }

            failureReason = new TextObject(string.Empty);
            return true;
        }

        public Kingdom ResolvePermanentRealm(Kingdom kingdom)
        {
            if (kingdom == null)
                return null;

            FactionObject rebellion = Campaign.Current?
                .GetCampaignBehavior<FactionManagerBehavior>()?
                .GetFactionByRebelKingdom(kingdom);
            if (rebellion?.ParentKingdom != null)
                return rebellion.ParentKingdom;

            Kingdom feudParent = Campaign.Current?
                .GetCampaignBehavior<ClaimFeudWarBehavior>()?
                .GetParentKingdomForTemporaryRealm(kingdom);
            return feudParent ?? kingdom;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureCollectionsInitialized();
            List<Kingdom> permanentKingdoms = Kingdom.All
                .Where(k => k != null
                    && !string.IsNullOrWhiteSpace(k.StringId)
                    && ResolvePermanentRealm(k) == k)
                .ToList();

            foreach (Kingdom kingdom in permanentKingdoms.Where(k => !IsGeneratedPermanentRealm(k.StringId)))
                GetLawsForKingdom(kingdom, out _);

            foreach (Kingdom kingdom in permanentKingdoms.Where(k => IsGeneratedPermanentRealm(k.StringId)))
            {
                GetLawsForKingdom(kingdom, out _);
            }

            RefreshKnownClanLeaders();
            ProcessPendingGenderLineEscheats();
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            EnsureCollectionsInitialized();
            if (Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()?.HasCrossClanEstate(victim) == true)
                return;
            RegencyBehavior regency = RegencyBehavior.Instance;
            if (regency?.IsRegentDeathInProgress(victim) == true)
                return;

            Clan clan = ResolveVictimClan(victim);
            if (!CanApplyGenderLineEscheat(clan))
                return;

            SuccessionLawSet laws = GetLawsForClan(clan, out _);
            bool victimWasLeader = WasKnownClanLeader(clan, victim)
                || regency?.IsWardDeathInProgress(victim) == true;
            bool genderLineExtinct = IsStrictGenderLaw(laws.GenderLaw)
                && (victimWasLeader || SuccessionLawHelper.IsEligibleUnderGenderLaw(victim, laws.GenderLaw))
                && !HasLivingPermittedGenderMember(clan, victim, laws.GenderLaw);
            bool dynasticLineExtinct = victimWasLeader
                && SuccessionLawHelper.IsDynasticBloodlineLaw(laws.SuccessionLaw)
                && !HasLivingDynasticSuccessor(clan, victim, laws.GenderLaw);
            if (!genderLineExtinct && !dynasticLineExtinct)
                return;

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            bool hasTitles = titleBehavior != null
                && (titleBehavior.GetTitlesHeldByClan(clan, deJure: true).Count > 0
                    || titleBehavior.GetTitlesHeldByClan(clan, deJure: false).Count > 0);
            bool hasSettlements = clan.Settlements?.Any(settlement => settlement?.OwnerClan == clan) == true;
            if (!hasTitles && !hasSettlements)
                return;

            _pendingGenderLineEscheats.RemoveAll(record => record != null && record.ClanId == clan.StringId);
            _pendingGenderLineEscheats.Add(new PendingGenderLineEscheatRecord(
                victim.StringId,
                clan.StringId,
                clan.Kingdom.StringId,
                laws.GenderLaw,
                CampaignTime.Now + CampaignTime.Hours(1f),
                clan == clan.Kingdom.RulingClan,
                dynasticLineExtinct && !genderLineExtinct));

            BellumCivileLogger.Log(
                $"Queued succession-line estate escheat; cause={(genderLineExtinct ? "gender" : "dynastic")}; clan={clan.StringId}; trigger_hero={victim.StringId}; former_leader={victimWasLeader}; kingdom={clan.Kingdom.StringId}; gender_law={laws.GenderLaw}; succession_law={laws.SuccessionLaw}; ruling_clan={clan == clan.Kingdom.RulingClan}; titles={hasTitles}; settlements={hasSettlements}.");
        }

        private void OnHourlyTick()
        {
            EnsureCollectionsInitialized();
            ProcessPendingGenderLineEscheats();
        }

        private void OnDailyTick()
        {
            EnsureCollectionsInitialized();
            ProcessPendingGenderLineEscheats();
            RefreshKnownClanLeaders();
        }

        private void ProcessPendingGenderLineEscheats()
        {
            foreach (PendingGenderLineEscheatRecord record in _pendingGenderLineEscheats.ToList())
            {
                if (record == null)
                {
                    _pendingGenderLineEscheats.Remove(record);
                    continue;
                }

                if (record.ReadyDate.IsFuture)
                    continue;

                if ((record.ReadyDate + CampaignTime.Days(GenderLineEscheatMaxPendingDays)).IsPast)
                {
                    BellumCivileLogger.Log(
                        $"Expired unresolved succession-line estate escheat; cause={(record.DynasticLineExtinction ? "dynastic" : "gender")}; clan={record.ClanId}; kingdom={record.KingdomId}; trigger_hero={record.TriggerHeroId}; ruling_clan={record.WasRulingClan}.");
                    _pendingGenderLineEscheats.Remove(record);
                    continue;
                }

                Clan clan = ResolveClan(record.ClanId);
                Kingdom kingdom = ResolveKingdom(record.KingdomId);
                if (!CanApplyGenderLineEscheat(clan)
                    || kingdom == null
                    || kingdom.IsEliminated
                    || clan.Kingdom != kingdom)
                {
                    _pendingGenderLineEscheats.Remove(record);
                    continue;
                }

                SuccessionLawSet currentLaws = GetLawsForClan(clan, out _);
                Hero triggerHero = ResolveHero(record.TriggerHeroId);
                if (Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()?.HasCrossClanEstate(triggerHero) == true)
                {
                    _pendingGenderLineEscheats.Remove(record);
                    continue;
                }
                bool lineContinues = record.DynasticLineExtinction
                    ? !SuccessionLawHelper.IsDynasticBloodlineLaw(currentLaws.SuccessionLaw)
                        || triggerHero == null
                        || HasLivingDynasticSuccessor(clan, triggerHero, currentLaws.GenderLaw)
                    : !IsStrictGenderLaw(currentLaws.GenderLaw)
                        || HasLivingPermittedGenderMember(clan, null, currentLaws.GenderLaw);
                if (lineContinues)
                {
                    BellumCivileLogger.Log(
                        $"Cancelled succession-line estate escheat after the line continued or its law changed; cause={(record.DynasticLineExtinction ? "dynastic" : "gender")}; clan={clan.StringId}; gender_law={currentLaws.GenderLaw}; succession_law={currentLaws.SuccessionLaw}.");
                    _pendingGenderLineEscheats.Remove(record);
                    continue;
                }

                Clan crownClan = kingdom.RulingClan;
                if (clan.Leader == null
                    || clan.Leader.IsDead
                    || crownClan?.Leader == null
                    || crownClan.Leader.IsDead
                    || crownClan == clan)
                {
                    continue;
                }

                FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
                int settlements = 0;
                int titles = 0;
                string failureReason = titleBehavior == null ? "title behavior unavailable" : null;
                if (titleBehavior == null
                    || !titleBehavior.TryEscheatEstateForExtinctSuccessionLine(
                        clan,
                        crownClan,
                        kingdom,
                        triggerHero,
                        record.DynasticLineExtinction,
                        out settlements,
                        out titles,
                        out failureReason))
                {
                    BellumCivileLogger.Log(
                        $"Deferred succession-line estate escheat; cause={(record.DynasticLineExtinction ? "dynastic" : "gender")}; clan={clan.StringId}; crown={crownClan.StringId}; reason={failureReason ?? "title behavior unavailable"}.");
                    continue;
                }

                ShowSuccessionLineEscheatNotification(record, clan, crownClan, kingdom, settlements, titles);
                _pendingGenderLineEscheats.Remove(record);
            }
        }

        private static void ShowSuccessionLineEscheatNotification(
            PendingGenderLineEscheatRecord record,
            Clan clan,
            Clan crownClan,
            Kingdom kingdom,
            int settlements,
            int titles)
        {
            if (settlements <= 0 && titles <= 0)
                return;
            if (Clan.PlayerClan != clan
                && Clan.PlayerClan != crownClan
                && Clan.PlayerClan?.Kingdom != kingdom)
            {
                return;
            }

            Hero deceasedHero = ResolveHero(record.TriggerHeroId);
            TextObject message = record.DynasticLineExtinction
                ? new TextObject("{=BC_DynasticLineEscheat_Notification}With no lawful blood relative remaining after {DECEASED_HERO}'s death, the lands and titles of {CLAN_NAME} have reverted to {CROWN_CLAN}. The surviving house retains strong claims to its former estate.")
                : new TextObject("{=BC_GenderLineEscheat_Notification}With no lawful {GENDER} successor remaining after {DECEASED_HERO}'s death, the lands and titles of {CLAN_NAME} have reverted to {CROWN_CLAN}. The surviving house retains strong claims to its former estate.");
            if (!record.DynasticLineExtinction)
            {
                message.SetTextVariable("GENDER", record.GenderLaw == GenderSuccessionLaw.MaleOnly
                    ? new TextObject("{=BC_GenderLine_Male}male")
                    : new TextObject("{=BC_GenderLine_Female}female"));
            }
            message.SetTextVariable("DECEASED_HERO", deceasedHero?.Name ?? clan.Name);
            message.SetTextVariable("CLAN_NAME", clan.Name);
            message.SetTextVariable("CROWN_CLAN", crownClan.Name);
            BellumCivileNotifications.ShowPersonal(message, BellumNotificationColors.Warning);
        }

        private static bool IsStrictGenderLaw(GenderSuccessionLaw law)
        {
            return law == GenderSuccessionLaw.MaleOnly
                || law == GenderSuccessionLaw.FemaleOnly;
        }

        private static bool HasLivingPermittedGenderMember(
            Clan clan,
            Hero excludedHero,
            GenderSuccessionLaw law)
        {
            return clan?.Heroes?.Any(hero => hero != null
                && hero != excludedHero
                && hero.Clan == clan
                && hero.IsAlive
                && !hero.IsDisabled
                && !hero.IsWanderer
                && !hero.IsNotable
                && !(RegencyBehavior.Instance?.IsGeneratedRegent(hero) ?? false)
                && SuccessionLawHelper.IsEligibleUnderGenderLaw(hero, law)) == true;
        }

        private static bool HasLivingDynasticSuccessor(
            Clan clan,
            Hero successionRoot,
            GenderSuccessionLaw genderLaw)
        {
            return clan?.Heroes?.Any(hero => hero != null
                && hero != successionRoot
                && hero.Clan == clan
                && hero.IsAlive
                && !hero.IsDisabled
                && !hero.IsWanderer
                && !hero.IsNotable
                && !(RegencyBehavior.Instance?.IsGeneratedRegent(hero) ?? false)
                && SuccessionLawHelper.IsEligibleUnderGenderLaw(hero, genderLaw)
                && SuccessionLawHelper.IsBloodRelative(hero, successionRoot)) == true;
        }

        private static bool CanApplyGenderLineEscheat(Clan clan)
        {
            bool isPlayerClan = clan == Clan.PlayerClan;
            return clan != null
                && !clan.IsEliminated
                && (isPlayerClan || !clan.IsMinorFaction)
                && (isPlayerClan || !clan.IsClanTypeMercenary)
                && (isPlayerClan || !clan.IsUnderMercenaryService)
                && !clan.IsBanditFaction
                && clan.Kingdom != null
                && !clan.Kingdom.IsEliminated;
        }

        private Clan ResolveVictimClan(Hero victim)
        {
            if (victim?.Clan != null)
                return victim.Clan;

            string victimId = victim?.StringId;
            if (string.IsNullOrWhiteSpace(victimId))
                return null;

            return Clan.All.FirstOrDefault(clan => clan != null
                && _knownClanLeaderIds.TryGetValue(clan.StringId, out string leaderId)
                && leaderId == victimId);
        }

        private bool WasKnownClanLeader(Clan clan, Hero victim)
        {
            if (clan == null || victim == null)
                return false;
            if (clan.Leader == victim)
                return true;

            return _knownClanLeaderIds.TryGetValue(clan.StringId, out string leaderId)
                && leaderId == victim.StringId;
        }

        private void RefreshKnownClanLeaders()
        {
            _knownClanLeaderIds.Clear();
            foreach (Clan clan in Clan.All)
            {
                if (clan?.Leader == null || clan.Leader.IsDead)
                    continue;

                _knownClanLeaderIds[clan.StringId] = clan.Leader.StringId;
            }
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All.FirstOrDefault(clan => clan?.StringId == clanId);
        }

        private static Kingdom ResolveKingdom(string kingdomId)
        {
            return string.IsNullOrWhiteSpace(kingdomId)
                ? null
                : Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == kingdomId);
        }

        private static Hero ResolveHero(string heroId)
        {
            return string.IsNullOrWhiteSpace(heroId)
                ? null
                : Hero.FindFirst(hero => hero?.StringId == heroId);
        }

        private static bool IsGeneratedPermanentRealm(string kingdomId)
        {
            return !string.IsNullOrWhiteSpace(kingdomId)
                && (kingdomId.IndexOf("_restored", StringComparison.Ordinal) >= 0
                    || kingdomId.IndexOf("_indep_", StringComparison.Ordinal) >= 0);
        }

        internal void NotifyLawChanged(Kingdom kingdom)
        {
            Campaign.Current?
                .GetCampaignBehavior<DynasticHeirBehavior>()?
                .RefreshSuccessionAfterLawChange(kingdom);
        }

        internal void CompletePlayerLawChange(Kingdom kingdom, TextObject lawName)
        {
            Clan rulerClan = kingdom?.RulingClan;
            Hero ruler = rulerClan?.Leader;
            if (rulerClan == null)
                return;

            // Influence and relation actions can synchronously refresh the kingdom screen.
            // Recalculate first so every replacement view model observes the new legal heir.
            NotifyLawChanged(kingdom);
            ChangeClanInfluenceAction.Apply(rulerClan, -LawChangeInfluenceCost);
            foreach (Clan clan in kingdom.Clans.Where(clan => clan != null
                && clan != rulerClan
                && !clan.IsUnderMercenaryService
                && clan.Leader != null))
            {
                RelationMemoryService.ApplyChange(
                    clan.Leader,
                    ruler,
                    LawChangeVassalRelationPenalty,
                    clan == Clan.PlayerClan,
                    RelationMemorySources.ChangedSuccessionLaws,
                    10f,
                    RelationMemoryScope.House,
                    lawName?.ToString());
            }

            TextObject message = new TextObject("{=BC_SuccessionLaw_Changed}{RULER_NAME} has proclaimed {LAW_NAME} as inheritance law throughout {KINGDOM_NAME}. The nobility resents this intrusion upon the established succession.");
            message.SetTextVariable("RULER_NAME", ruler?.Name ?? rulerClan.Name);
            message.SetTextVariable("LAW_NAME", lawName ?? new TextObject(string.Empty));
            message.SetTextVariable("KINGDOM_NAME", kingdom.Name);
            BellumCivileNotifications.ShowPersonal(message, BellumNotificationColors.Warning);
        }

        private void EnsureCollectionsInitialized()
        {
            if (_pendingGenderLineEscheats == null)
                _pendingGenderLineEscheats = new List<PendingGenderLineEscheatRecord>();
            if (_knownClanLeaderIds == null)
                _knownClanLeaderIds = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private void PrunePendingGenderLineEscheats()
        {
            _pendingGenderLineEscheats = _pendingGenderLineEscheats
                .Where(record => record != null
                    && !string.IsNullOrWhiteSpace(record.TriggerHeroId)
                    && !string.IsNullOrWhiteSpace(record.ClanId)
                    && !string.IsNullOrWhiteSpace(record.KingdomId)
                    && (record.DynasticLineExtinction || IsStrictGenderLaw(record.GenderLaw)))
                .GroupBy(record => record.ClanId)
                .Select(group => group.OrderByDescending(record => record.ReadyDate.ToDays).First())
                .ToList();
        }
    }
}
