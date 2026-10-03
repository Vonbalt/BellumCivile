using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile.Behaviors
{
    public sealed class DynamicMercenaryBandBehavior : CampaignBehaviorBase
    {
        private const string CultureCounterPrefix = "culture::";
        private static readonly string[] CultureSummaryMetrics =
        {
            "source_clans_evaluated",
            "source_house_too_small",
            "no_eligible_candidate",
            "eligible_candidates",
            "eligible_candidates_at_global_cap",
            "roll_passed_candidates",
            "selected_departures",
            "player_departures_queued",
            "joined_existing_company",
            "company_join_failed",
            "founding_opportunities",
            "companies_founded",
            "company_limit_blocked",
            "no_cultural_city",
            "company_creation_failed",
            "companies_destroyed"
        };

        private List<DynamicMercenaryBandRecord> _bands = new List<DynamicMercenaryBandRecord>();
        private List<DynamicMercenaryDepartureIntent> _playerDepartureIntents = new List<DynamicMercenaryDepartureIntent>();
        private Dictionary<string, CampaignTime> _legacyPendingPlayerDepartures = new Dictionary<string, CampaignTime>();
        private Dictionary<string, int> _yearlyCounters = new Dictionary<string, int>();
        private int _summaryYearIndex = -1;
        private int _lastMaintenanceDay = -1;
        private string _activeDepartureConversationHeroId;
        private bool _isShowingDepartureLetter;
        private float _presentationRetrySeconds;
        private int _postLoadPresentationTicks;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyTickClan);
            CampaignEvents.OnClanDestroyedEvent.AddNonSerializedListener(this, OnClanDestroyed);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_DynamicMercenaryBands", ref _bands);
            dataStore.SyncData("BellumCivile_DynamicMercenaryPendingPlayerDepartures", ref _legacyPendingPlayerDepartures);
            dataStore.SyncData("BellumCivile_DynamicMercenaryPlayerDepartureIntents", ref _playerDepartureIntents);
            dataStore.SyncData("BellumCivile_DynamicMercenarySummaryYear", ref _summaryYearIndex);
            dataStore.SyncData("BellumCivile_DynamicMercenaryYearlyCounters", ref _yearlyCounters);
            EnsureCollectionsInitialized();
            MigrateLegacyPlayerDepartures();
        }

        public IReadOnlyList<Hero> GetPendingPlayerDepartureCandidates()
        {
            EnsureCollectionsInitialized();
            return _playerDepartureIntents
                .Where(intent => intent != null)
                .Select(intent => ResolveHero(intent.HeroId))
                .Where(hero => hero != null && hero.IsAlive && hero.Clan == Clan.PlayerClan)
                .ToList();
        }

        public bool HasPendingPlayerDeparture(Hero hero)
        {
            return hero != null
                && _playerDepartureIntents != null
                && _playerDepartureIntents.Any(intent => intent != null
                    && string.Equals(intent.HeroId, hero.StringId, StringComparison.Ordinal));
        }

        public void ClearPendingPlayerDeparture(Hero hero)
        {
            if (hero != null)
            {
                _playerDepartureIntents?.RemoveAll(intent => intent == null
                    || string.Equals(intent.HeroId, hero.StringId, StringComparison.Ordinal));
                if (string.Equals(_activeDepartureConversationHeroId, hero.StringId, StringComparison.Ordinal))
                    _activeDepartureConversationHeroId = null;
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            RegisterPlayerDepartureDialogue(starter);
            EnsureCollectionsInitialized();
            MigrateLegacyPlayerDepartures();
            ReconcileBands(recoverUntracked: true);
            PrunePendingPlayerDepartures();
            FlushSummaryIfYearChanged();
            _postLoadPresentationTicks = 2;
            BellumCivileLogger.Log(
                $"Dynamic mercenary company limit configured; limit={BellumCivileOptions.DynamicMercenaryCompanyLimit}.");
        }

        private void RegisterPlayerDepartureDialogue(CampaignGameStarter starter)
        {
            starter.AddDialogLine(
                "bc_dynamic_mercenary_departure_opening",
                "start",
                "bc_dynamic_mercenary_departure_choices",
                "{=BC_DynamicMercenaryDeparture_Opening}{KIN_ADDRESS}, I wanted to speak with you. I have made up my mind to seek my own fortune as a mercenary. I would not leave without speaking to you first. I hope you can understand.",
                IsActiveDepartureConversation,
                null,
                250);

            starter.AddPlayerLine(
                "bc_dynamic_mercenary_departure_support",
                "bc_dynamic_mercenary_departure_choices",
                "bc_dynamic_mercenary_departure_support_response",
                "{=BC_DynamicMercenaryDeparture_Support}You have my blessing, and the support of our house. Take these denars for the road.",
                null,
                ChooseSupportedDeparture,
                120,
                CanAffordDepartureSupport);

            starter.AddDialogLine(
                "bc_dynamic_mercenary_departure_support_response",
                "bc_dynamic_mercenary_departure_support_response",
                "close_window",
                "{=BC_DynamicMercenaryDeparture_SupportReply}Thank you. I will not forget this, and I will honor our name wherever fortune carries me.[if:convo_happy]",
                null,
                null,
                120);

            starter.AddPlayerLine(
                "bc_dynamic_mercenary_departure_bless",
                "bc_dynamic_mercenary_departure_choices",
                "bc_dynamic_mercenary_departure_bless_response",
                "{=BC_DynamicMercenaryDeparture_Bless}I see your mind is made up. Go with my blessing, then.",
                null,
                ChooseBlessedDeparture,
                110);

            starter.AddDialogLine(
                "bc_dynamic_mercenary_departure_bless_response",
                "bc_dynamic_mercenary_departure_bless_response",
                "close_window",
                "{=BC_DynamicMercenaryDeparture_BlessReply}Thank you. I will send word when I have found my place.[if:convo_happy]",
                null,
                null,
                120);

            starter.AddPlayerLine(
                "bc_dynamic_mercenary_departure_ask_stay",
                "bc_dynamic_mercenary_departure_choices",
                "bc_dynamic_mercenary_departure_ask_stay_response",
                "{=BC_DynamicMercenaryDeparture_AskStay}I understand why this calls to you, but I would rather you remained with our family. Will you reconsider?",
                null,
                ChooseAskedDeparture,
                105);

            starter.AddDialogLine(
                "bc_dynamic_mercenary_departure_ask_stay_accept",
                "bc_dynamic_mercenary_departure_ask_stay_response",
                "close_window",
                "{=BC_DynamicMercenaryDeparture_AskStayAccept}For your sake, I will remain. Perhaps there is still a place for me here.[if:convo_grave]",
                IsAskedStayResponse,
                null,
                130);

            starter.AddDialogLine(
                "bc_dynamic_mercenary_departure_ask_stay_refuse",
                "bc_dynamic_mercenary_departure_ask_stay_response",
                "close_window",
                "{=BC_DynamicMercenaryDeparture_AskStayRefuse}I respect your concern, but my mind is made up. I must follow this path.[if:convo_grave]",
                IsAskedLeaveResponse,
                null,
                120);

            starter.AddPlayerLine(
                "bc_dynamic_mercenary_departure_forbid",
                "bc_dynamic_mercenary_departure_choices",
                "bc_dynamic_mercenary_departure_forbid_response",
                "{=BC_DynamicMercenaryDeparture_Forbid}I rule this house, and you will not go. I forbid it.",
                null,
                ChooseForbiddenDeparture,
                100);

            starter.AddDialogLine(
                "bc_dynamic_mercenary_departure_forbid_stay",
                "bc_dynamic_mercenary_departure_forbid_response",
                "close_window",
                "{=BC_DynamicMercenaryDeparture_ForbidStay}As you command. But do not ask me to be glad of it.[if:convo_grave]",
                IsForbiddenStayResponse,
                null,
                130);

            starter.AddDialogLine(
                "bc_dynamic_mercenary_departure_forbid_leave",
                "bc_dynamic_mercenary_departure_forbid_response",
                "close_window",
                "{=BC_DynamicMercenaryDeparture_ForbidLeave}No. My life is my own, and I will not surrender it to your command. Farewell.[if:convo_angry]",
                IsForbiddenLeaveResponse,
                null,
                120);
        }

        private void OnTick(float deltaTime)
        {
            if (!BellumCivileOptions.EnableDynamicMercenaryBands || Campaign.Current == null)
                return;

            EnsureCollectionsInitialized();
            MigrateLegacyPlayerDepartures();
            if (_playerDepartureIntents.Count == 0)
            {
                _activeDepartureConversationHeroId = null;
                return;
            }

            if (_postLoadPresentationTicks > 0)
            {
                _postLoadPresentationTicks--;
                return;
            }

            if (_presentationRetrySeconds > 0f)
            {
                _presentationRetrySeconds = Math.Max(0f, _presentationRetrySeconds - deltaTime);
                return;
            }

            DynamicMercenaryDepartureIntent intent = GetNextPlayerDepartureIntent();
            if (intent == null)
            {
                _activeDepartureConversationHeroId = null;
                return;
            }

            Hero hero = ResolveHero(intent.HeroId);
            if (hero == null || hero.Clan != Clan.PlayerClan)
            {
                RemovePlayerDepartureIntent(intent);
                return;
            }

            bool conversationActive = Campaign.Current.ConversationManager?.IsConversationInProgress == true
                || Campaign.Current.ConversationManager?.IsConversationFlowActive == true;
            if (!string.IsNullOrWhiteSpace(_activeDepartureConversationHeroId))
            {
                if (conversationActive || CampaignMission.Current != null)
                    return;

                if (intent.Resolution != DynamicMercenaryDepartureResolution.Undecided)
                {
                    if (CanPresentPlayerDeparture())
                        ResolveDirectPlayerDeparture(intent, hero);
                    return;
                }

                _activeDepartureConversationHeroId = null;
                _presentationRetrySeconds = 1f;
                return;
            }

            if (intent.Resolution != DynamicMercenaryDepartureResolution.Undecided)
            {
                if (CanPresentPlayerDeparture())
                    ResolveDirectPlayerDeparture(intent, hero);
                return;
            }

            if (_isShowingDepartureLetter
                || !CanPresentPlayerDeparture()
                || !CanPlaceCandidateNow(hero, allowGovernorDeparture: true))
            {
                return;
            }

            if (hero.PartyBelongedTo == MobileParty.MainParty)
                OpenPlayerDepartureConversation(hero);
            else
                ResolveAwayPlayerDeparture(intent, hero);
        }

        private bool IsActiveDepartureConversation()
        {
            Hero hero = Hero.OneToOneConversationHero;
            if (hero == null
                || string.IsNullOrWhiteSpace(_activeDepartureConversationHeroId)
                || !string.Equals(hero.StringId, _activeDepartureConversationHeroId, StringComparison.Ordinal)
                || GetPlayerDepartureIntent(hero) == null)
            {
                return false;
            }

            MBTextManager.SetTextVariable("KIN_ADDRESS", GetPlayerKinshipAddress(hero));
            return true;
        }

        private bool CanAffordDepartureSupport(out TextObject explanation)
        {
            if ((Hero.MainHero?.Gold ?? 0) < C.DynamicMercenaryPlayerSupportGold)
            {
                explanation = new TextObject("{=BC_DynamicMercenaryDeparture_SupportUnavailable}You need {GOLD} denars to support this venture.");
                explanation.SetTextVariable("GOLD", C.DynamicMercenaryPlayerSupportGold.ToString("N0"));
                return false;
            }

            explanation = new TextObject("{=BC_DynamicMercenaryDeparture_SupportCost}This will give {GOLD} denars to your relative.");
            explanation.SetTextVariable("GOLD", C.DynamicMercenaryPlayerSupportGold.ToString("N0"));
            return true;
        }

        private void ChooseSupportedDeparture()
        {
            SetActiveDepartureResolution(DynamicMercenaryDepartureResolution.Supported);
            Record("player_departure_supported");
        }

        private void ChooseBlessedDeparture()
        {
            SetActiveDepartureResolution(DynamicMercenaryDepartureResolution.Blessed);
            Record("player_departure_blessed");
        }

        private void ChooseAskedDeparture()
        {
            DynamicMercenaryDepartureIntent intent = GetActiveDepartureIntent();
            Hero hero = ResolveHero(intent?.HeroId);
            if (intent == null || hero == null)
                return;

            float persuasionChance = CalculateRespectfulRequestChance(hero);
            bool stays = intent.ObedienceRoll < persuasionChance;
            intent.SetResolution(stays
                ? DynamicMercenaryDepartureResolution.AskedStay
                : DynamicMercenaryDepartureResolution.AskedLeave);
            Record(stays ? "player_departure_asked_stay" : "player_departure_asked_leave");
            BellumCivileLogger.Log(
                $"Player respectfully asked family mercenary candidate to remain; hero={hero.StringId}; stays={stays}; chance={persuasionChance:0.000}; roll={intent.ObedienceRoll:0.000}; relation={hero.GetRelation(Hero.MainHero)}; valor={hero.GetTraitLevel(DefaultTraits.Valor)}; calculating={hero.GetTraitLevel(DefaultTraits.Calculating)}.");
        }

        private void ChooseForbiddenDeparture()
        {
            DynamicMercenaryDepartureIntent intent = GetActiveDepartureIntent();
            Hero hero = ResolveHero(intent?.HeroId);
            if (intent == null || hero == null)
                return;

            float obedienceChance = CalculateObedienceChance(hero);
            bool stays = intent.ObedienceRoll < obedienceChance;
            intent.SetResolution(stays
                ? DynamicMercenaryDepartureResolution.ForbiddenStay
                : DynamicMercenaryDepartureResolution.ForbiddenLeave);
            Record(stays ? "player_departure_forbidden_stay" : "player_departure_forbidden_leave");
            BellumCivileLogger.Log(
                $"Player-family mercenary departure forbidden; hero={hero.StringId}; stays={stays}; chance={obedienceChance:0.000}; roll={intent.ObedienceRoll:0.000}; relation={hero.GetRelation(Hero.MainHero)}; valor={hero.GetTraitLevel(DefaultTraits.Valor)}; calculating={hero.GetTraitLevel(DefaultTraits.Calculating)}.");
        }

        private bool IsForbiddenStayResponse()
        {
            return GetActiveDepartureIntent()?.Resolution == DynamicMercenaryDepartureResolution.ForbiddenStay;
        }

        private bool IsForbiddenLeaveResponse()
        {
            return GetActiveDepartureIntent()?.Resolution == DynamicMercenaryDepartureResolution.ForbiddenLeave;
        }

        private bool IsAskedStayResponse()
        {
            return GetActiveDepartureIntent()?.Resolution == DynamicMercenaryDepartureResolution.AskedStay;
        }

        private bool IsAskedLeaveResponse()
        {
            return GetActiveDepartureIntent()?.Resolution == DynamicMercenaryDepartureResolution.AskedLeave;
        }

        private void SetActiveDepartureResolution(DynamicMercenaryDepartureResolution resolution)
        {
            GetActiveDepartureIntent()?.SetResolution(resolution);
        }

        private void OnDailyTick()
        {
            EnsureCollectionsInitialized();
            FlushSummaryIfYearChanged();

            int today = GetCurrentDay();
            if (_lastMaintenanceDay >= 0 && today - _lastMaintenanceDay < C.DynamicMercenaryMaintenanceIntervalDays)
                return;

            _lastMaintenanceDay = today;
            ReconcileBands(recoverUntracked: true);
            MaintainBandLeadershipAndLifecycle();
            PrunePendingPlayerDepartures();
        }

        private void OnDailyTickClan(Clan clan)
        {
            if (!BellumCivileOptions.EnableDynamicMercenaryBands || !CanEvaluateSourceClan(clan))
                return;

            int periodDays = Math.Max(1, GetDaysPerYear() * BellumCivileOptions.DynamicMercenaryEvaluationIntervalYears);
            int today = GetCurrentDay();
            int offset = PositiveHash(clan.StringId) % periodDays;
            if ((today + offset) % periodDays != 0)
                return;

            EvaluateClan(clan, (today + offset) / periodDays);
        }

        private void EvaluateClan(Clan clan, int evaluationCycle)
        {
            Record("clans_evaluated");
            RecordForCulture(clan?.Culture, "source_clans_evaluated");
            IReadOnlyCollection<Hero> protectedBeneficiaries =
                FeudalInheritancePlanner.GetProspectiveInheritanceBeneficiaries(
                    clan,
                    BellumCivileOptions.PartitionSuccessionMainHeirReservedFiefs,
                    BellumCivileOptions.EnablePartitionSuccession);

            HashSet<Hero> protectedSet = new HashSet<Hero>(protectedBeneficiaries);
            Record("inheritance_beneficiaries_reserved", protectedSet.Count);
            int adultLords = clan.AliveLords.Count(IsViableAdultLord);
            if (adultLords < C.DynamicMercenaryMinimumSourceAdultLords)
            {
                Record("source_house_too_small");
                RecordForCulture(clan?.Culture, "source_house_too_small");
                return;
            }

            if (clan == Clan.PlayerClan && GetPendingPlayerDepartureCandidates().Count > 0)
            {
                Record("player_candidate_already_pending");
                return;
            }

            var candidates = clan.Heroes
                .Where(hero => IsEligibleCandidate(hero, clan, protectedSet, adultLords))
                .Select(hero => new
                {
                    Hero = hero,
                    Score = GetAdventureScore(hero),
                    Chance = GetDepartureChance(hero),
                    Roll = StableUnitFloat($"dynamic_mercenary_departure|{hero.StringId}|{evaluationCycle}")
                })
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => StableUnitFloat($"dynamic_mercenary_order|{candidate.Hero.StringId}|{evaluationCycle}"))
                .ToList();

            if (candidates.Count == 0)
            {
                Record("no_eligible_candidate");
                RecordForCulture(clan?.Culture, "no_eligible_candidate");
                return;
            }

            Record("eligible_candidates", candidates.Count);
            bool globalCapFull = GetActiveBands().Count >= BellumCivileOptions.DynamicMercenaryCompanyLimit;
            foreach (var candidate in candidates)
            {
                CultureObject candidateCulture = GetMercenaryCulture(candidate.Hero);
                RecordForCulture(candidateCulture, "eligible_candidates");
                if (globalCapFull)
                    RecordForCulture(candidateCulture, "eligible_candidates_at_global_cap");
                if (candidate.Roll < candidate.Chance)
                    RecordForCulture(candidateCulture, "roll_passed_candidates");
            }

            var selected = candidates.FirstOrDefault(candidate => candidate.Roll < candidate.Chance);
            if (selected == null)
            {
                Record("departure_roll_declined");
                return;
            }

            RecordForCulture(GetMercenaryCulture(selected.Hero), "selected_departures");

            if (clan == Clan.PlayerClan)
            {
                _playerDepartureIntents.Add(new DynamicMercenaryDepartureIntent(
                    selected.Hero.StringId,
                    CampaignTime.Now,
                    StableUnitFloat($"dynamic_mercenary_obedience|{selected.Hero.StringId}|{evaluationCycle}")));
                Record("player_departures_queued");
                RecordForCulture(GetMercenaryCulture(selected.Hero), "player_departures_queued");
                BellumCivileLogger.Log(
                    $"Queued player-family mercenary departure for future dialogue; hero={selected.Hero.StringId}; valor={selected.Hero.GetTraitLevel(DefaultTraits.Valor)}; calculating={selected.Hero.GetTraitLevel(DefaultTraits.Calculating)}; chance={selected.Chance:0.000}; roll={selected.Roll:0.000}.");
                return;
            }

            TryPlaceCandidate(selected.Hero, showNotification: true, allowGovernorDeparture: false, out _);
        }

        private bool TryPlaceCandidate(
            Hero hero,
            bool showNotification,
            bool allowGovernorDeparture,
            out MercenaryPlacementResult result)
        {
            result = null;
            CultureObject culture = GetMercenaryCulture(hero);
            if (hero?.Clan == null || culture == null)
                return false;

            Clan sourceClan = hero.Clan;
            List<Clan> activeBands = GetActiveBands();
            int companyLimit = BellumCivileOptions.DynamicMercenaryCompanyLimit;
            Clan target = activeBands
                .Where(clan => SameCulture(clan.Culture, culture)
                    && DynamicMercenaryBandService.GetAdultOfficerCount(clan) < BellumCivileOptions.DynamicMercenaryMaximumOfficers)
                .OrderBy(DynamicMercenaryBandService.GetAdultOfficerCount)
                .ThenBy(clan => clan.StringId)
                .FirstOrDefault();

            if (target != null)
            {
                if (DynamicMercenaryBandService.TryJoinBand(
                    hero,
                    target,
                    target.InitialHomeSettlement,
                    out string joinFailure,
                    allowGovernorDeparture))
                {
                    Record("nobles_joined_existing_company");
                    RecordForCulture(culture, "joined_existing_company");
                    result = new MercenaryPlacementResult(target, sourceClan, founded: false);
                    if (showNotification)
                        ShowCompanyJoinedMessage(target, hero, sourceClan);
                    return true;
                }

                Record("company_join_failed");
                RecordForCulture(culture, "company_join_failed");
                BellumCivileLogger.Log(
                    $"Dynamic mercenary company join failed; hero={hero.StringId}; company={target.StringId}; reason={joinFailure}.");
                return false;
            }

            RecordForCulture(culture, "founding_opportunities");
            if (activeBands.Count >= companyLimit)
            {
                Record("company_limit_blocked");
                RecordForCulture(culture, "company_limit_blocked");
                return false;
            }

            Settlement home = SelectCulturalHome(hero, culture);
            if (home == null)
            {
                Record("no_cultural_city");
                RecordForCulture(culture, "no_cultural_city");
                return false;
            }

            if (!DynamicMercenaryBandService.TryCreateBand(
                hero,
                home,
                BellumCivileOptions.DynamicMercenaryStartingTier,
                out Clan company,
                out DynamicMercenaryBandRecord record,
                out string failure,
                allowGovernorDeparture))
            {
                Record("company_creation_failed");
                RecordForCulture(culture, "company_creation_failed");
                BellumCivileLogger.Log(
                    $"Dynamic mercenary company could not be formed; hero={hero.StringId}; source={sourceClan?.StringId ?? "none"}; home={home.StringId}; reason={failure}.");
                return false;
            }

            _bands.Add(record);
            Record("companies_founded");
            RecordForCulture(culture, "companies_founded");
            BellumCivileLogger.Log(
                $"Dynamic mercenary company founded; company={company.StringId}; founder={hero.StringId}; source={sourceClan.StringId}; culture={culture.StringId}; home={home.StringId}; active={activeBands.Count + 1}/{companyLimit}.");
            result = new MercenaryPlacementResult(company, sourceClan, founded: true);
            if (showNotification)
                ShowCompanyFoundedMessage(company, hero, sourceClan);
            return true;
        }

        private bool CanPlaceCandidateNow(Hero hero, bool allowGovernorDeparture)
        {
            CultureObject culture = hero?.Culture ?? hero?.Clan?.Culture;
            if (hero?.Clan == null
                || culture == null
                || !DynamicMercenaryBandService.CanTransferHeroNow(hero, allowGovernorDeparture))
            {
                return false;
            }

            if (GetActiveBands().Any(clan => SameCulture(clan.Culture, culture)
                && DynamicMercenaryBandService.GetAdultOfficerCount(clan)
                    < BellumCivileOptions.DynamicMercenaryMaximumOfficers))
            {
                return true;
            }

            return GetActiveBands().Count < BellumCivileOptions.DynamicMercenaryCompanyLimit
                && culture.BasicTroop != null
                && (culture.DefaultPartyTemplate != null || hero.Clan.DefaultPartyTemplate != null)
                && SelectCulturalHome(hero, culture) != null;
        }

        private static bool CanPresentPlayerDeparture()
        {
            MapState mapState = Game.Current?.GameStateManager?.ActiveState as MapState;
            MobileParty mainParty = MobileParty.MainParty;
            if (mapState == null
                || mapState.AtMenu
                || mapState.MapConversationActive
                || mapState.NextIncident != null
                || mapState.IsSimulationActive
                || InformationManager.IsAnyInquiryActive()
                || Campaign.Current?.CurrentMenuContext != null
                || Campaign.Current?.ConversationManager?.IsConversationInProgress == true
                || Campaign.Current?.ConversationManager?.IsConversationFlowActive == true
                || CampaignMission.Current != null
                || Hero.MainHero?.IsPrisoner == true
                || PlayerEncounter.Current != null
                || PartyBase.MainParty == null
                || mainParty == null
                || mainParty.MapEvent != null
                || mainParty.SiegeEvent != null)
            {
                return false;
            }

            return true;
        }

        private void OpenPlayerDepartureConversation(Hero hero)
        {
            if (hero?.CharacterObject == null || PartyBase.MainParty == null)
                return;

            _activeDepartureConversationHeroId = hero.StringId;
            try
            {
                Campaign.Current.CurrentConversationContext = ConversationContext.Default;
                if (CampaignMission.OpenConversationMission(
                    new ConversationCharacterData(CharacterObject.PlayerCharacter, PartyBase.MainParty),
                    new ConversationCharacterData(hero.CharacterObject, PartyBase.MainParty)) == null)
                {
                    _activeDepartureConversationHeroId = null;
                    _presentationRetrySeconds = 2f;
                    return;
                }

                Record("player_departure_dialogues_opened");
                BellumCivileLogger.Log($"Opened player-family mercenary departure dialogue; hero={hero.StringId}.");
            }
            catch (Exception ex)
            {
                _activeDepartureConversationHeroId = null;
                _presentationRetrySeconds = 2f;
                BellumCivileLogger.Log(
                    $"Could not open player-family mercenary departure dialogue; hero={hero.StringId}; error={ex.GetType().Name}:{ex.Message}.");
            }
        }

        private void ResolveDirectPlayerDeparture(DynamicMercenaryDepartureIntent intent, Hero hero)
        {
            if (intent == null || hero == null)
                return;

            if (intent.Resolution == DynamicMercenaryDepartureResolution.ForbiddenStay
                || intent.Resolution == DynamicMercenaryDepartureResolution.AskedStay)
            {
                bool wasRespectfulRequest = intent.Resolution == DynamicMercenaryDepartureResolution.AskedStay;
                RelationMemoryService.ApplyChange(
                    hero,
                    Hero.MainHero,
                    wasRespectfulRequest
                        ? C.DynamicMercenaryPlayerAskedStayRelation
                        : C.DynamicMercenaryPlayerForbidStayRelation,
                    false,
                    wasRespectfulRequest
                        ? RelationMemorySources.AskedMeToAbandonMercenaryVenture
                        : RelationMemorySources.ForbadeMyMercenaryDeparture,
                    wasRespectfulRequest
                        ? C.DynamicMercenaryPlayerAskedStayMemoryYears
                        : C.DynamicMercenaryPlayerForbidMemoryYears);
                RemovePlayerDepartureIntent(intent);
                Record("player_departures_prevented");
                return;
            }

            if (intent.Resolution == DynamicMercenaryDepartureResolution.Supported
                && (Hero.MainHero?.Gold ?? 0) < C.DynamicMercenaryPlayerSupportGold)
            {
                _presentationRetrySeconds = 2f;
                return;
            }

            if (!CanPlaceCandidateNow(hero, allowGovernorDeparture: true)
                || !TryPlaceCandidate(
                    hero,
                    showNotification: false,
                    allowGovernorDeparture: true,
                    out MercenaryPlacementResult placement))
            {
                _presentationRetrySeconds = 2f;
                return;
            }

            ApplyDirectDepartureConsequences(intent.Resolution, hero, placement.Company);
            RemovePlayerDepartureIntent(intent);
            Record("player_departures_completed_after_dialogue");
        }

        private void ApplyDirectDepartureConsequences(
            DynamicMercenaryDepartureResolution resolution,
            Hero relative,
            Clan company)
        {
            Hero player = Hero.MainHero;
            Hero captain = company?.Leader;
            switch (resolution)
            {
                case DynamicMercenaryDepartureResolution.Supported:
                    GiveGoldAction.ApplyBetweenCharacters(
                        player,
                        relative,
                        C.DynamicMercenaryPlayerSupportGold,
                        false);
                    RelationMemoryService.ApplyChange(
                        relative,
                        player,
                        C.DynamicMercenaryPlayerSupportRelativeRelation,
                        false,
                        RelationMemorySources.SupportedMyMercenaryVenture,
                        C.DynamicMercenaryPlayerSupportMemoryYears);
                    if (captain != null && captain != relative)
                    {
                        RelationMemoryService.ApplyChange(
                            captain,
                            player,
                            C.DynamicMercenaryPlayerSupportCaptainRelation,
                            false,
                            RelationMemorySources.PatronizedMyMercenaryCompany,
                            C.DynamicMercenaryPlayerSupportMemoryYears);
                    }
                    break;

                case DynamicMercenaryDepartureResolution.Blessed:
                    RelationMemoryService.ApplyChange(
                        relative,
                        player,
                        C.DynamicMercenaryPlayerBlessingRelation,
                        false,
                        RelationMemorySources.BlessedMyMercenaryDeparture,
                        C.DynamicMercenaryPlayerBlessingMemoryYears);
                    break;

                case DynamicMercenaryDepartureResolution.AskedLeave:
                    // A respectful request carries no relationship penalty when refused.
                    break;

                case DynamicMercenaryDepartureResolution.ForbiddenLeave:
                    RelationMemoryService.ApplyChange(
                        relative,
                        player,
                        C.DynamicMercenaryPlayerForbidLeaveRelation,
                        false,
                        RelationMemorySources.ForbadeMyMercenaryDeparture,
                        C.DynamicMercenaryPlayerForbidMemoryYears);
                    if (captain != null && captain != relative)
                    {
                        RelationMemoryService.ApplyChange(
                            captain,
                            player,
                            C.DynamicMercenaryPlayerForbidCaptainRelation,
                            false,
                            RelationMemorySources.OpposedMyCompanyRecruitment,
                            C.DynamicMercenaryPlayerForbidMemoryYears);
                    }
                    break;
            }
        }

        private void ResolveAwayPlayerDeparture(DynamicMercenaryDepartureIntent intent, Hero hero)
        {
            if (!TryPlaceCandidate(
                hero,
                showNotification: false,
                allowGovernorDeparture: true,
                out MercenaryPlacementResult placement))
            {
                _presentationRetrySeconds = 2f;
                return;
            }

            RemovePlayerDepartureIntent(intent);
            Record("player_departures_completed_by_letter");
            ShowAwayDepartureLetter(hero, placement);
        }

        private void ShowAwayDepartureLetter(Hero hero, MercenaryPlacementResult placement)
        {
            TextObject title = new TextObject("{=BC_DynamicMercenaryDeparture_LetterTitle}A Farewell from {HERO}");
            title.SetTextVariable("HERO", hero?.Name ?? new TextObject("?"));

            TextObject outcome = placement.Founded
                ? new TextObject("{=BC_DynamicMercenaryDeparture_LetterFounded}{HERO} has left the {SOURCE_CLAN} to found the {COMPANY}.")
                : new TextObject("{=BC_DynamicMercenaryDeparture_LetterJoined}{HERO} has left the {SOURCE_CLAN} to sign with the {COMPANY}.");
            outcome.SetTextVariable("HERO", hero?.Name ?? new TextObject("?"));
            outcome.SetTextVariable("SOURCE_CLAN", placement.SourceClan?.Name ?? new TextObject("?"));
            outcome.SetTextVariable("COMPANY", placement.Company?.Name ?? new TextObject("?"));

            TextObject body = new TextObject(
                "{=BC_DynamicMercenaryDeparture_LetterBody}A sealed letter from {HERO} reaches you.\n\n\"{KIN_ADDRESS}, I have made up my mind to seek my own fortune as a mercenary. Though I could not speak with you in person, I would not leave without sending word. I hope you can understand.\"\n\n{OUTCOME}");
            body.SetTextVariable("HERO", hero?.Name ?? new TextObject("?"));
            body.SetTextVariable("KIN_ADDRESS", GetPlayerKinshipAddress(hero));
            body.SetTextVariable("OUTCOME", outcome);

            _isShowingDepartureLetter = true;
            InformationManager.ShowInquiry(new InquiryData(
                title.ToString(),
                body.ToString(),
                true,
                false,
                new TextObject("{=BC_DynamicMercenaryDeparture_LetterAcknowledge}So be it.").ToString(),
                null,
                () => _isShowingDepartureLetter = false,
                null), true, false);
        }

        private static float CalculateObedienceChance(Hero hero)
        {
            float chance = C.DynamicMercenaryObedienceBaseChance
                + hero.GetRelation(Hero.MainHero) * C.DynamicMercenaryObedienceRelationScale
                + hero.GetTraitLevel(DefaultTraits.Calculating) * C.DynamicMercenaryObedienceTraitScale
                - hero.GetTraitLevel(DefaultTraits.Valor) * C.DynamicMercenaryObedienceTraitScale;
            return Math.Max(
                C.DynamicMercenaryObedienceMinimumChance,
                Math.Min(C.DynamicMercenaryObedienceMaximumChance, chance));
        }

        private static float CalculateRespectfulRequestChance(Hero hero)
        {
            float chance = C.DynamicMercenaryRequestBaseChance
                + hero.GetRelation(Hero.MainHero) * C.DynamicMercenaryRequestRelationScale
                + hero.GetTraitLevel(DefaultTraits.Calculating) * C.DynamicMercenaryRequestTraitScale
                - hero.GetTraitLevel(DefaultTraits.Valor) * C.DynamicMercenaryRequestTraitScale;
            return Math.Max(
                C.DynamicMercenaryRequestMinimumChance,
                Math.Min(C.DynamicMercenaryRequestMaximumChance, chance));
        }

        private static TextObject GetPlayerKinshipAddress(Hero relative)
        {
            Hero player = Hero.MainHero;
            int generation = GetDescendantGeneration(relative, player, 3);
            bool playerIsFemale = player?.IsFemale == true;
            switch (generation)
            {
                case 1:
                    return playerIsFemale
                        ? new TextObject("{=BC_DynamicMercenaryDeparture_AddressMother}Mother")
                        : new TextObject("{=BC_DynamicMercenaryDeparture_AddressFather}Father");
                case 2:
                    return playerIsFemale
                        ? new TextObject("{=BC_DynamicMercenaryDeparture_AddressGrandmother}Grandmother")
                        : new TextObject("{=BC_DynamicMercenaryDeparture_AddressGrandfather}Grandfather");
                case 3:
                    return playerIsFemale
                        ? new TextObject("{=BC_DynamicMercenaryDeparture_AddressGreatGrandmother}Great-grandmother")
                        : new TextObject("{=BC_DynamicMercenaryDeparture_AddressGreatGrandfather}Great-grandfather");
                default:
                    return playerIsFemale
                        ? new TextObject("{=BC_DynamicMercenaryDeparture_AddressKinswoman}Kinswoman")
                        : new TextObject("{=BC_DynamicMercenaryDeparture_AddressKinsman}Kinsman");
            }
        }

        private static int GetDescendantGeneration(Hero relative, Hero ancestor, int maximumGeneration)
        {
            if (relative == null || ancestor == null || maximumGeneration <= 0)
                return 0;

            HashSet<Hero> current = new HashSet<Hero> { relative };
            HashSet<Hero> visited = new HashSet<Hero> { relative };
            for (int generation = 1; generation <= maximumGeneration; generation++)
            {
                HashSet<Hero> parents = new HashSet<Hero>();
                foreach (Hero hero in current)
                {
                    if (hero?.Father != null && visited.Add(hero.Father))
                        parents.Add(hero.Father);
                    if (hero?.Mother != null && visited.Add(hero.Mother))
                        parents.Add(hero.Mother);
                }

                if (parents.Contains(ancestor))
                    return generation;
                if (parents.Count == 0)
                    break;
                current = parents;
            }

            return 0;
        }

        private void ReconcileBands(bool recoverUntracked)
        {
            EnsureCollectionsInitialized();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DynamicMercenaryBandRecord record in _bands.ToList())
            {
                if (record == null || string.IsNullOrWhiteSpace(record.ClanId) || !seen.Add(record.ClanId))
                {
                    _bands.Remove(record);
                    continue;
                }

                Clan clan = ResolveClan(record.ClanId);
                if (clan == null || clan.IsEliminated || !DynamicMercenaryBandService.IsDynamicBand(clan))
                {
                    _bands.Remove(record);
                    continue;
                }

                DynamicMercenaryBandService.RepairBand(clan, ResolveSettlement(record.HomeSettlementId));
                DynamicMercenaryDescriptionHelper.EnsureDescription(clan, record,
                    ResolveHero(record.FounderHeroId), ResolveSettlement(record.HomeSettlementId));
            }

            if (!recoverUntracked)
                return;

            foreach (Clan clan in Clan.All.Where(candidate => DynamicMercenaryBandService.IsDynamicBand(candidate) && !candidate.IsEliminated))
            {
                if (seen.Contains(clan.StringId))
                    continue;

                _bands.Add(new DynamicMercenaryBandRecord(
                    clan.StringId,
                    clan.Leader?.StringId ?? string.Empty,
                    string.Empty,
                    clan.Culture?.StringId ?? string.Empty,
                    clan.InitialHomeSettlement?.StringId ?? clan.HomeSettlement?.StringId ?? string.Empty,
                    CampaignTime.Now));
                DynamicMercenaryBandService.RepairBand(clan, clan.InitialHomeSettlement ?? clan.HomeSettlement);
                DynamicMercenaryDescriptionHelper.EnsureDescription(clan, _bands.Last(), null,
                    clan.InitialHomeSettlement ?? clan.HomeSettlement);
                seen.Add(clan.StringId);
                Record("orphaned_companies_recovered");
                BellumCivileLogger.Log($"Recovered untracked dynamic mercenary company; clan={clan.StringId}.");
            }
        }

        private void MaintainBandLeadershipAndLifecycle()
        {
            foreach (Clan clan in GetActiveBands().ToList())
            {
                if (clan.Leader != null && clan.Leader.IsAlive)
                    continue;

                Hero successor = clan.AliveLords
                    .Where(IsViableAdultLord)
                    .OrderByDescending(hero => hero.GetSkillValue(DefaultSkills.Leadership))
                    .ThenByDescending(hero => hero.GetTraitLevel(DefaultTraits.Valor))
                    .FirstOrDefault();
                if (successor != null)
                {
                    Hero oldLeader = clan.Leader;
                    if (oldLeader != null)
                        ChangeClanLeaderAction.ApplyWithSelectedNewLeader(clan, successor);
                    else
                        clan.SetLeader(successor);

                    BellumCivileLogger.Log(
                        $"Dynamic mercenary company selected a new captain; company={clan.StringId}; old={oldLeader?.StringId ?? "none"}; new={successor.StringId}.");
                    continue;
                }

                if (clan.Heroes.Any(hero => hero != null && hero.IsAlive))
                    continue;

                BellumCivileLogger.Log($"Dynamic mercenary company became extinct and will be retired; company={clan.StringId}.");
                DestroyClanAction.Apply(clan);
            }
        }

        private void PrunePendingPlayerDepartures()
        {
            EnsureCollectionsInitialized();
            HashSet<Hero> protectedSet = Clan.PlayerClan == null
                ? new HashSet<Hero>()
                : new HashSet<Hero>(FeudalInheritancePlanner.GetProspectiveInheritanceBeneficiaries(
                    Clan.PlayerClan,
                    BellumCivileOptions.PartitionSuccessionMainHeirReservedFiefs,
                    BellumCivileOptions.EnablePartitionSuccession));
            int adultLords = Clan.PlayerClan?.AliveLords?.Count(IsAliveAdultLord) ?? 0;

            foreach (DynamicMercenaryDepartureIntent intent in _playerDepartureIntents.ToList())
            {
                Hero hero = ResolveHero(intent?.HeroId);
                bool expiredBeforePresentation = intent != null
                    && intent.Resolution == DynamicMercenaryDepartureResolution.Undecided
                    && CampaignTime.Now.ToDays - intent.QueuedAt.ToDays
                        >= GetDaysPerYear() * C.DynamicMercenaryPlayerIntentExpiryYears;
                if (intent == null
                    || hero == null
                    || hero.Clan != Clan.PlayerClan
                    || !IsStillValidPendingCandidate(hero, protectedSet, adultLords)
                    || expiredBeforePresentation)
                {
                    RemovePlayerDepartureIntent(intent);
                    if (expiredBeforePresentation)
                        Record("player_departures_expired");
                }
            }
        }

        private void OnClanDestroyed(Clan clan)
        {
            if (!DynamicMercenaryBandService.IsDynamicBand(clan))
                return;

            EnsureCollectionsInitialized();
            int removed = _bands.RemoveAll(record => record == null
                || string.Equals(record.ClanId, clan.StringId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                Record("companies_destroyed");
                RecordForCulture(clan.Culture, "companies_destroyed");
            }
        }

        private static bool CanEvaluateSourceClan(Clan clan)
        {
            if (clan == null || clan.IsEliminated || clan.Leader == null || clan.Leader.IsDead || clan.IsBanditFaction || clan.IsRebelClan)
                return false;

            if (clan == Clan.PlayerClan)
                return true;

            return clan.IsNoble
                && !clan.IsMinorFaction
                && !clan.IsClanTypeMercenary
                && !clan.IsUnderMercenaryService;
        }

        private bool IsEligibleCandidate(Hero hero, Clan clan, HashSet<Hero> protectedSet, int adultLords)
        {
            if (hero == null
                || clan == null
                || hero.Clan != clan
                || hero == clan.Leader
                || hero == Hero.MainHero
                || hero.IsHumanPlayerCharacter
                || !hero.IsLord
                || hero.IsWanderer
                || !IsViableAdultLord(hero)
                || protectedSet.Contains(hero)
                || adultLords - 1 < C.DynamicMercenaryMinimumAdultsRemainingInSourceClan
                || (clan != Clan.PlayerClan && !DynamicMercenaryBandService.CanTransferHeroNow(hero)))
            {
                return false;
            }

            if (hero.Spouse != null && hero.Spouse.IsAlive)
                return false;
            if (hero.Children.Any(child => child != null && child.IsAlive && child.IsChild))
                return false;
            if (HasPendingPlayerDeparture(hero))
                return false;

            int valor = Math.Max(0, hero.GetTraitLevel(DefaultTraits.Valor));
            int impulsiveness = Math.Max(0, -hero.GetTraitLevel(DefaultTraits.Calculating));
            return valor > 0 || impulsiveness > 0;
        }

        private static bool IsStillValidPendingCandidate(Hero hero, HashSet<Hero> protectedSet, int adultLords)
        {
            if (hero == null
                || hero.Clan != Clan.PlayerClan
                || hero == Clan.PlayerClan?.Leader
                || hero == Hero.MainHero
                || hero.IsHumanPlayerCharacter
                || !hero.IsLord
                || hero.IsWanderer
                || !IsAliveAdultLord(hero)
                || protectedSet.Contains(hero)
                || adultLords - 1 < C.DynamicMercenaryMinimumAdultsRemainingInSourceClan)
            {
                return false;
            }

            return (hero.Spouse == null || !hero.Spouse.IsAlive)
                && !hero.Children.Any(child => child != null && child.IsAlive && child.IsChild);
        }

        private static bool IsViableAdultLord(Hero hero)
        {
            return hero != null
                && hero.IsAlive
                && hero.IsActive
                && hero.IsLord
                && !hero.IsChild
                && hero.Age >= SuccessionLawHelper.GetAgeOfMajority()
                && !hero.IsDisabled;
        }

        private static bool IsAliveAdultLord(Hero hero)
        {
            return hero != null
                && hero.IsAlive
                && hero.IsLord
                && !hero.IsChild
                && hero.Age >= SuccessionLawHelper.GetAgeOfMajority()
                && !hero.IsDisabled;
        }

        private static float GetAdventureScore(Hero hero)
        {
            int valor = Math.Max(0, hero?.GetTraitLevel(DefaultTraits.Valor) ?? 0);
            int impulsiveness = Math.Max(0, -(hero?.GetTraitLevel(DefaultTraits.Calculating) ?? 0));
            int leadership = hero?.GetSkillValue(DefaultSkills.Leadership) ?? 0;
            int tactics = hero?.GetSkillValue(DefaultSkills.Tactics) ?? 0;
            return valor * 40f + impulsiveness * 40f + leadership * 0.08f + tactics * 0.05f;
        }

        private static float GetDepartureChance(Hero hero)
        {
            int valor = Math.Max(0, hero?.GetTraitLevel(DefaultTraits.Valor) ?? 0);
            int impulsiveness = Math.Max(0, -(hero?.GetTraitLevel(DefaultTraits.Calculating) ?? 0));
            float chance = C.DynamicMercenaryBaseDepartureChance
                + valor * C.DynamicMercenaryTraitDepartureChancePerLevel
                + impulsiveness * C.DynamicMercenaryTraitDepartureChancePerLevel;
            return Math.Max(0f, Math.Min(C.DynamicMercenaryMaximumDepartureChance, chance));
        }

        private static Settlement SelectCulturalHome(Hero hero, CultureObject culture)
        {
            List<Settlement> culturalCities = Settlement.All
                .Where(settlement => settlement != null && settlement.IsTown && SameCulture(settlement.Culture, culture))
                .ToList();
            if (culturalCities.Count == 0)
                return null;

            Settlement preferred = hero?.Clan?.HomeSettlement;
            if (preferred != null && preferred.IsTown && culturalCities.Contains(preferred) && !preferred.IsUnderSiege)
                return preferred;

            List<Settlement> safeCities = culturalCities.Where(settlement => !settlement.IsUnderSiege).ToList();
            List<Settlement> pool = safeCities.Count > 0 ? safeCities : culturalCities;
            int index = PositiveHash($"dynamic_mercenary_home|{hero?.StringId}|{culture?.StringId}") % pool.Count;
            return pool[index];
        }

        private List<Clan> GetActiveBands()
        {
            return Clan.All
                .Where(clan => clan != null && !clan.IsEliminated && DynamicMercenaryBandService.IsDynamicBand(clan))
                .ToList();
        }

        private void FlushSummaryIfYearChanged()
        {
            int daysPerYear = GetDaysPerYear();
            int currentYear = GetCurrentDay() / daysPerYear;
            if (_summaryYearIndex < 0)
            {
                _summaryYearIndex = currentYear;
                return;
            }
            if (currentYear == _summaryYearIndex)
                return;

            if (_yearlyCounters.Values.Any(value => value > 0))
                BellumCivileDebug.TraceYearlyReport("dynamic mercenaries", FormatYearlySummary(_summaryYearIndex));

            _yearlyCounters.Clear();
            _summaryYearIndex = currentYear;
        }

        private string FormatYearlySummary(int yearIndex)
        {
            List<Clan> active = GetActiveBands();
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"yearly dynamic mercenary summary: campaign year {yearIndex + 1}");
            builder.AppendLine($"active_companies={active.Count}; company_limit={BellumCivileOptions.DynamicMercenaryCompanyLimit}; adult_officers={active.Sum(DynamicMercenaryBandService.GetAdultOfficerCount)}; war_parties={active.Sum(clan => clan.WarPartyComponents.Count)}");
            foreach (KeyValuePair<string, int> counter in _yearlyCounters
                .Where(pair => !pair.Key.StartsWith(CultureCounterPrefix, StringComparison.Ordinal))
                .OrderBy(pair => pair.Key))
            {
                builder.AppendLine($"{counter.Key}={counter.Value}");
            }

            AppendCultureSummary(builder, active);
            builder.Append($"pending_player_departures={_playerDepartureIntents.Count}");
            return builder.ToString();
        }

        private void AppendCultureSummary(StringBuilder builder, IReadOnlyCollection<Clan> activeBands)
        {
            HashSet<string> cultureIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in _yearlyCounters.Keys)
            {
                if (TryParseCultureCounterKey(key, out string cultureId, out _))
                    cultureIds.Add(cultureId);
            }

            foreach (Clan clan in Clan.All)
            {
                if (CanEvaluateSourceClan(clan) && !string.IsNullOrWhiteSpace(clan.Culture?.StringId))
                    cultureIds.Add(clan.Culture.StringId);
            }

            foreach (Clan band in activeBands)
            {
                if (!string.IsNullOrWhiteSpace(band?.Culture?.StringId))
                    cultureIds.Add(band.Culture.StringId);
            }

            foreach (string cultureId in cultureIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                List<Clan> culturalBands = activeBands
                    .Where(band => string.Equals(band?.Culture?.StringId, cultureId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                builder.Append($"culture={cultureId}");
                builder.Append($"; active_companies={culturalBands.Count}");
                builder.Append($"; adult_officers={culturalBands.Sum(DynamicMercenaryBandService.GetAdultOfficerCount)}");
                builder.Append($"; war_parties={culturalBands.Sum(clan => clan.WarPartyComponents.Count)}");
                foreach (string metric in CultureSummaryMetrics)
                    builder.Append($"; {metric}={GetCultureCounter(cultureId, metric)}");
                builder.AppendLine();
            }
        }

        private void Record(string key, int amount = 1)
        {
            if (string.IsNullOrWhiteSpace(key) || amount == 0)
                return;
            _yearlyCounters[key] = (_yearlyCounters.TryGetValue(key, out int current) ? current : 0) + amount;
        }

        private void RecordForCulture(CultureObject culture, string metric, int amount = 1)
        {
            string cultureId = culture?.StringId;
            if (string.IsNullOrWhiteSpace(cultureId) || string.IsNullOrWhiteSpace(metric))
                return;

            Record(BuildCultureCounterKey(cultureId, metric), amount);
        }

        private int GetCultureCounter(string cultureId, string metric)
        {
            string key = BuildCultureCounterKey(cultureId, metric);
            return _yearlyCounters.TryGetValue(key, out int value) ? value : 0;
        }

        private static string BuildCultureCounterKey(string cultureId, string metric)
        {
            return CultureCounterPrefix + cultureId + "::" + metric;
        }

        private static bool TryParseCultureCounterKey(string key, out string cultureId, out string metric)
        {
            cultureId = null;
            metric = null;
            if (string.IsNullOrWhiteSpace(key)
                || !key.StartsWith(CultureCounterPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string payload = key.Substring(CultureCounterPrefix.Length);
            int separator = payload.IndexOf("::", StringComparison.Ordinal);
            if (separator <= 0 || separator >= payload.Length - 2)
                return false;

            cultureId = payload.Substring(0, separator);
            metric = payload.Substring(separator + 2);
            return true;
        }

        private static CultureObject GetMercenaryCulture(Hero hero)
        {
            return hero?.Culture ?? hero?.Clan?.Culture;
        }

        private static void ShowCompanyFoundedMessage(Clan company, Hero founder, Clan sourceClan)
        {
            TextObject message = new TextObject("{=BC_DynamicMercenaryCompanyFounded}Seeking {?HERO.GENDER}her{?}his{\\?} own fortune, {HERO.NAME} has left the {SOURCE_CLAN} to found the {COMPANY}.");
            StringHelpers.SetCharacterProperties("HERO", founder.CharacterObject, message);
            message.SetTextVariable("SOURCE_CLAN", sourceClan?.Name ?? new TextObject("?"));
            message.SetTextVariable("COMPANY", company?.Name ?? new TextObject("?"));
            BellumCivileNotifications.Show(
                message,
                BellumNotificationColors.Inheritance,
                primaryKingdom: sourceClan?.Kingdom,
                primaryClan: sourceClan,
                secondaryClan: company);
        }

        private static void ShowCompanyJoinedMessage(Clan company, Hero hero, Clan sourceClan)
        {
            TextObject message = new TextObject("{=BC_DynamicMercenaryCompanyJoined}Seeking adventure and riches, {HERO.NAME} has left the {SOURCE_CLAN} to sign with the {COMPANY}.");
            StringHelpers.SetCharacterProperties("HERO", hero.CharacterObject, message);
            message.SetTextVariable("SOURCE_CLAN", sourceClan?.Name ?? new TextObject("?"));
            message.SetTextVariable("COMPANY", company?.Name ?? new TextObject("?"));
            BellumCivileNotifications.Show(
                message,
                BellumNotificationColors.Inheritance,
                primaryKingdom: sourceClan?.Kingdom,
                primaryClan: sourceClan,
                secondaryClan: company);
        }

        private void EnsureCollectionsInitialized()
        {
            if (_bands == null)
                _bands = new List<DynamicMercenaryBandRecord>();
            if (_playerDepartureIntents == null)
                _playerDepartureIntents = new List<DynamicMercenaryDepartureIntent>();
            if (_legacyPendingPlayerDepartures == null)
                _legacyPendingPlayerDepartures = new Dictionary<string, CampaignTime>();
            if (_yearlyCounters == null)
                _yearlyCounters = new Dictionary<string, int>();
        }

        private void MigrateLegacyPlayerDepartures()
        {
            EnsureCollectionsInitialized();
            if (_legacyPendingPlayerDepartures.Count == 0)
                return;

            foreach (KeyValuePair<string, CampaignTime> pair in _legacyPendingPlayerDepartures)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)
                    || _playerDepartureIntents.Any(intent => intent != null
                        && string.Equals(intent.HeroId, pair.Key, StringComparison.Ordinal)))
                {
                    continue;
                }

                _playerDepartureIntents.Add(new DynamicMercenaryDepartureIntent(
                    pair.Key,
                    pair.Value,
                    StableUnitFloat($"dynamic_mercenary_obedience|{pair.Key}|{pair.Value.ToDays:0.000}")));
                Record("legacy_player_departures_migrated");
            }

            _legacyPendingPlayerDepartures.Clear();
        }

        private DynamicMercenaryDepartureIntent GetNextPlayerDepartureIntent()
        {
            return _playerDepartureIntents
                .Where(intent => intent != null && !string.IsNullOrWhiteSpace(intent.HeroId))
                .OrderBy(intent => intent.QueuedAt.ToDays)
                .ThenBy(intent => intent.HeroId, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private DynamicMercenaryDepartureIntent GetPlayerDepartureIntent(Hero hero)
        {
            if (hero == null)
                return null;

            return _playerDepartureIntents.FirstOrDefault(intent => intent != null
                && string.Equals(intent.HeroId, hero.StringId, StringComparison.Ordinal));
        }

        private DynamicMercenaryDepartureIntent GetActiveDepartureIntent()
        {
            string heroId = _activeDepartureConversationHeroId
                ?? Hero.OneToOneConversationHero?.StringId;
            return string.IsNullOrWhiteSpace(heroId)
                ? null
                : _playerDepartureIntents.FirstOrDefault(intent => intent != null
                    && string.Equals(intent.HeroId, heroId, StringComparison.Ordinal));
        }

        private void RemovePlayerDepartureIntent(DynamicMercenaryDepartureIntent intent)
        {
            if (intent == null)
            {
                _playerDepartureIntents.RemoveAll(candidate => candidate == null);
                return;
            }

            _playerDepartureIntents.Remove(intent);
            if (string.Equals(_activeDepartureConversationHeroId, intent.HeroId, StringComparison.Ordinal))
                _activeDepartureConversationHeroId = null;
            _presentationRetrySeconds = 0f;
        }

        private static bool SameCulture(CultureObject first, CultureObject second)
        {
            return first != null
                && second != null
                && string.Equals(first.StringId, second.StringId, StringComparison.OrdinalIgnoreCase);
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }

        private static Hero ResolveHero(string heroId)
        {
            return string.IsNullOrWhiteSpace(heroId)
                ? null
                : Hero.FindFirst(hero => hero != null && hero.StringId == heroId);
        }

        private static Settlement ResolveSettlement(string settlementId)
        {
            return string.IsNullOrWhiteSpace(settlementId)
                ? null
                : Settlement.All.FirstOrDefault(settlement => settlement != null && settlement.StringId == settlementId);
        }

        private static int GetDaysPerYear()
        {
            return Math.Max(1, CampaignTime.DaysInYear > 0 ? CampaignTime.DaysInYear : C.MarriageStrategyDaysPerYear);
        }

        private static int GetCurrentDay()
        {
            return Campaign.Current == null ? 0 : (int)CampaignTime.Now.ToDays;
        }

        private static int PositiveHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char character in value ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 16777619u;
                }
                return (int)(hash & 0x7FFFFFFF);
            }
        }

        private static float StableUnitFloat(string value)
        {
            return PositiveHash(value) / (float)int.MaxValue;
        }

        private sealed class MercenaryPlacementResult
        {
            public Clan Company { get; }
            public Clan SourceClan { get; }
            public bool Founded { get; }

            public MercenaryPlacementResult(Clan company, Clan sourceClan, bool founded)
            {
                Company = company;
                SourceClan = sourceClan;
                Founded = founded;
            }
        }
    }
}
