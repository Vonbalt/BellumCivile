using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Keeps NPC noble houses alive when their lawful successor is still underage. The adult
    /// regent remains the engine-facing clan leader while the saved ward remains the legal head.
    /// </summary>
    public sealed class RegencyBehavior : CampaignBehaviorBase
    {
        private enum PlayerSuccessionMode
        {
            BeginRegency,
            ReplaceRegent,
            WardSucceeds
        }

        private sealed class PendingPlayerSuccession
        {
            public string DyingHeroId;
            public string WardHeroId;
            public List<string> CandidateHeroIds = new List<string>();
            public string GeneratedCandidateHeroId;
            public PlayerSuccessionMode Mode;
            public bool UsedHouseContinuityFallback;
            public bool SelectionScreenOpened;
            public bool IntroductionShown;
        }

        private List<RegencyRecord> _regencies = new List<RegencyRecord>();
        private Dictionary<string, bool> _generatedRegentIds = new Dictionary<string, bool>();
        private readonly HashSet<string> _regentDeathsInProgress = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _generatedRegentDeathsInProgress = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _wardDeathsInProgress = new HashSet<string>(StringComparer.Ordinal);
        private PendingPlayerSuccession _pendingPlayerSuccession;
        private string _approvedPlayerSuccessorId;
        private bool _isShowingPlayerRegencyIntroduction;
        private bool _isShowingPlayerRegencyHandover;
        private bool _playerRegencyHandoverPending;

        public static RegencyBehavior Instance { get; private set; }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.BeforeHeroKilledEvent.AddNonSerializedListener(this, OnBeforeHeroKilled);
            CampaignEvents.HeroComesOfAgeEvent.AddNonSerializedListener(this, OnHeroComesOfAge);
            CampaignEvents.OnClanDestroyedEvent.AddNonSerializedListener(this, OnClanDestroyed);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            Instance = this;
            dataStore.SyncData("BellumCivile_Regencies", ref _regencies);
            dataStore.SyncData("BellumCivile_GeneratedRegentIds", ref _generatedRegentIds);
            EnsureCollectionsInitialized();
            _regencies.RemoveAll(record => record == null || string.IsNullOrWhiteSpace(record.ClanId));
            foreach (var record in _regencies.Where(record => record.RegentWasGenerated))
                _generatedRegentIds[record.RegentHeroId] = true;
        }

        public bool TryGetRegency(Clan clan, out RegencyRecord record)
        {
            EnsureCollectionsInitialized();
            record = clan == null || string.IsNullOrWhiteSpace(clan.StringId)
                ? null
                : _regencies.FirstOrDefault(item => item != null && item.ClanId == clan.StringId);
            return record != null;
        }

        public Hero GetWard(Clan clan)
        {
            return TryGetRegency(clan, out RegencyRecord record)
                ? ResolveHero(record.WardHeroId)
                : null;
        }

        public Hero GetRegent(Clan clan)
        {
            return TryGetRegency(clan, out RegencyRecord record)
                ? ResolveHero(record.RegentHeroId)
                : null;
        }

        public Hero GetLegalClanHead(Clan clan)
        {
            Hero ward = GetWard(clan);
            return ward != null && ward.IsAlive ? ward : clan?.Leader;
        }

        public bool EnsureCrownHeirRegency(Clan clan, Hero ward, Hero predecessor)
        {
            if (GetWard(clan) == ward && GetRegent(clan)?.IsAlive == true
                && clan.Leader == GetRegent(clan)) return true;
            if (!CanUseRegency(clan) || !IsUnderageWard(ward, clan)) return false;
            var line = SuccessionLawHelper.GetLegalSuccessionLine(clan, predecessor, SuccessionLawHelper.GetLawsForClan(clan));
            Hero regent = SelectRegent(clan, ward, predecessor, line, out bool generated);
            if (regent == null) return false;
            var record = new RegencyRecord(clan.StringId, ward.StringId, regent.StringId,
                predecessor.StringId, CampaignTime.Now, generated);
            var previous = _regencies.Where(x => x.ClanId == clan.StringId).ToList();
            _regencies.RemoveAll(x => x.ClanId == clan.StringId);
            _regencies.Add(record);
            if (!TryInstallClanLeader(clan, regent, "Crown heir regency"))
            {
                _regencies.Remove(record);
                _regencies.AddRange(previous);
                DiscardUnusedGeneratedRegent(regent, generated);
                return false;
            }
            var summary = Campaign.Current?.GetCampaignBehavior<SuccessionYearlySummaryBehavior>();
            if (previous.Count == 0) summary?.RecordRegencyStarted(generated);
            else summary?.RecordRegentReplaced(generated);
            BellumCivileLogger.Log($"Crown heir regency {(previous.Count == 0 ? "opened" : "replaced")}; clan={clan.StringId}; ward={ward.StringId}; regent={regent.StringId}; generated={generated}.");
            ShowRegencyStarted(clan, ward, regent);
            return true;
        }

        public bool IsActingRegent(Hero hero)
        {
            return hero?.Clan != null
                && TryGetRegency(hero.Clan, out RegencyRecord record)
                && record.RegentHeroId == hero.StringId;
        }

        public bool IsGeneratedRegent(Hero hero)
        {
            return hero != null && (_generatedRegentIds.ContainsKey(hero.StringId)
                || (hero.Clan != null && TryGetRegency(hero.Clan, out RegencyRecord record)
                && record.RegentWasGenerated
                && record.RegentHeroId == hero.StringId));
        }

        public bool RepairFormerGeneratedRegent(Hero hero)
        {
            if (hero?.Clan == null || !TryGetRegency(hero.Clan, out var record)
                || hero.StringId == record.WardHeroId) return false;
            _generatedRegentIds[hero.StringId] = true;
            Campaign.Current?.GetCampaignBehavior<DynasticHeirBehavior>()?.RefreshSuccessionAfterLawChange(hero.Clan.Kingdom);
            Patches.FeudalTitleHeroNamePatch.InvalidateCache();
            BellumCivileLogger.Log($"Explicit caretaker identity repair; hero={hero.StringId}; clan={hero.Clan.StringId}; ward={record.WardHeroId}.");
            return true;
        }

        public bool IsRegentDeathInProgress(Hero hero)
        {
            return hero != null && _regentDeathsInProgress.Contains(hero.StringId ?? string.Empty);
        }

        public bool IsGeneratedRegentDeathInProgress(Hero hero)
        {
            return hero != null && _generatedRegentDeathsInProgress.Contains(hero.StringId ?? string.Empty);
        }

        public bool IsWardDeathInProgress(Hero hero)
        {
            return hero != null && _wardDeathsInProgress.Contains(hero.StringId ?? string.Empty);
        }

        public bool TryGetWardForRegent(Hero regent, out Hero ward)
        {
            ward = null;
            if (!IsActingRegent(regent))
                return false;

            ward = GetWard(regent.Clan);
            return ward != null && ward.IsAlive;
        }

        internal bool TryPreparePlayerSuccession(Hero dyingPlayer)
        {
            if (dyingPlayer == null
                || dyingPlayer != Hero.MainHero
                || dyingPlayer.Clan != Clan.PlayerClan
                || Clan.PlayerClan == null)
            {
                return false;
            }

            bool hasActiveRegency = TryGetRegency(Clan.PlayerClan, out RegencyRecord activeRegency);
            if (!BellumCivileOptions.EnforcePlayerSuccessionLaw && !hasActiveRegency)
                return false;

            if (_pendingPlayerSuccession?.DyingHeroId == dyingPlayer.StringId)
                return true;

            EnsureCollectionsInitialized();
            Clan clan = Clan.PlayerClan;
            Hero ward;
            List<Hero> legalLine;
            bool usedContinuityFallback = false;
            PlayerSuccessionMode mode;

            if (hasActiveRegency)
            {
                ward = ResolveHero(activeRegency.WardHeroId);
                if (ward == null || !ward.IsAlive || ward.Clan != clan)
                    return false;

                legalLine = SuccessionLawHelper.GetLegalSuccessionLine(
                    clan,
                    ward,
                    SuccessionLawHelper.GetLawsForClan(clan));
                mode = ward.Age >= SuccessionLawHelper.GetAgeOfMajority()
                    ? PlayerSuccessionMode.WardSucceeds
                    : PlayerSuccessionMode.ReplaceRegent;
            }
            else
            {
                SuccessionLawSet laws = SuccessionLawHelper.GetLawsForClan(clan);
                legalLine = SuccessionLawHelper.GetLegalSuccessionLine(clan, dyingPlayer, laws);
                ward = legalLine.FirstOrDefault();
                if (ward == null)
                {
                    legalLine = GetHouseContinuityLine(clan, dyingPlayer);
                    ward = legalLine.FirstOrDefault();
                    usedContinuityFallback = ward != null;
                }

                if (!IsUnderageWard(ward, clan))
                    return false;

                mode = PlayerSuccessionMode.BeginRegency;
            }

            List<Hero> candidates;
            Hero generatedRegent = null;
            if (mode == PlayerSuccessionMode.WardSucceeds)
            {
                _playerRegencyHandoverPending = false;
                if (ward.IsNotSpawned || ward.IsDisabled)
                    ward.ChangeState(Hero.CharacterStates.Active);
                candidates = new List<Hero> { ward };
            }
            else
            {
                candidates = BuildPlayerRegentCandidates(clan, ward, dyingPlayer, legalLine);
                if (candidates.Count == 0)
                {
                    generatedRegent = CreateGeneratedRegent(clan);
                    if (generatedRegent != null)
                        candidates.Add(generatedRegent);
                }
            }

            if (candidates.Count == 0)
            {
                BellumCivileLogger.Log(
                    $"Could not prepare player regency because no regent candidate could be supplied; clan={clan.StringId}; ward={ward.StringId}; predecessor={dyingPlayer.StringId}.");
                return false;
            }

            _pendingPlayerSuccession = new PendingPlayerSuccession
            {
                DyingHeroId = dyingPlayer.StringId,
                WardHeroId = ward.StringId,
                CandidateHeroIds = candidates.Select(hero => hero.StringId).ToList(),
                GeneratedCandidateHeroId = generatedRegent?.StringId ?? string.Empty,
                Mode = mode,
                UsedHouseContinuityFallback = usedContinuityFallback
            };
            _approvedPlayerSuccessorId = null;
            _playerRegencyHandoverPending = false;

            BellumCivileLogger.Log(
                $"Prepared player succession; mode={mode}; clan={clan.StringId}; predecessor={dyingPlayer.StringId}; ward={ward.StringId}; candidates={string.Join(",", _pendingPlayerSuccession.CandidateHeroIds)}; generated={generatedRegent?.StringId ?? "false"}; continuity_fallback={usedContinuityFallback}.");
            return true;
        }

        internal bool TryInjectPlayerSuccessionCandidates(
            Clan clan,
            out Dictionary<Hero, int> candidates)
        {
            candidates = null;
            PendingPlayerSuccession pending = _pendingPlayerSuccession;
            if (pending == null
                || clan == null
                || clan != Clan.PlayerClan
                || pending.DyingHeroId != Hero.MainHero?.StringId)
            {
                return false;
            }

            Hero ward = ResolveHero(pending.WardHeroId);
            Hero dyingHero = ResolveHero(pending.DyingHeroId);
            List<Hero> resolved = pending.CandidateHeroIds
                .Select(ResolveHero)
                .Where(hero => pending.Mode == PlayerSuccessionMode.WardSucceeds
                    ? hero == ward && hero.IsAlive && hero.Clan == clan
                    : IsSuitablePlayerRegentCandidate(hero, clan, ward, dyingHero))
                .Distinct()
                .ToList();
            if (resolved.Count == 0)
                return false;

            candidates = new Dictionary<Hero, int>();
            for (int index = 0; index < resolved.Count; index++)
                candidates[resolved[index]] = 1000000 - index;

            return true;
        }

        internal bool HasPendingPlayerSuccessionSelection => _pendingPlayerSuccession != null;

        internal bool IsPlayerRegentSelectionActive =>
            _pendingPlayerSuccession != null
            && _pendingPlayerSuccession.Mode != PlayerSuccessionMode.WardSucceeds;

        internal Hero GetPendingPlayerSuccessionWard()
        {
            return ResolveHero(_pendingPlayerSuccession?.WardHeroId);
        }

        internal bool CanSelectPendingPlayerSuccessor(Hero hero)
        {
            return hero != null
                && _pendingPlayerSuccession?.CandidateHeroIds?.Contains(hero.StringId) == true;
        }

        internal void OnPlayerSuccessionSelectionScreenOpened()
        {
            if (_pendingPlayerSuccession == null)
                return;

            _pendingPlayerSuccession.SelectionScreenOpened = true;
        }

        internal TextObject GetPlayerSuccessionSelectionTitle()
        {
            Hero ward = GetPendingPlayerSuccessionWard();
            TextObject text = IsPlayerRegentSelectionActive
                ? new TextObject("{=BC_PlayerRegency_SelectionTitle}Appoint a Regent for {WARD}")
                : new TextObject("{=BC_PlayerRegency_WardSuccessionTitle}Recognize {WARD} as Head of the House");
            text.SetTextVariable("WARD", ward?.Name ?? new TextObject("?"));
            return text;
        }

        internal TextObject GetPlayerSuccessionSelectionButtonText()
        {
            return IsPlayerRegentSelectionActive
                ? new TextObject("{=BC_PlayerRegency_SelectionButton}Appoint Regent")
                : new TextObject("{=BC_PlayerRegency_WardSuccessionButton}Continue as Heir");
        }

        internal TextObject BuildPlayerSuccessionSelectionConfirmation(Hero selectedHero)
        {
            Hero ward = GetPendingPlayerSuccessionWard();
            TextObject text = IsPlayerRegentSelectionActive
                ? new TextObject("{=BC_PlayerRegency_SelectionConfirm}Appoint {REGENT} as regent for {WARD}? You will continue playing as {REGENT} until {WARD} comes of age.")
                : new TextObject("{=BC_PlayerRegency_WardSuccessionConfirm}{WARD} has already come of age and will now succeed as the rightful head of the house. You will continue playing as {WARD}.");
            text.SetTextVariable("REGENT", selectedHero?.Name ?? new TextObject("?"));
            text.SetTextVariable("WARD", ward?.Name ?? new TextObject("?"));
            return text;
        }

        internal bool TryApprovePreparedPlayerSuccessor(Hero selectedHero)
        {
            if (!IsPreparedPlayerSuccessorValid(selectedHero))
                return false;

            _approvedPlayerSuccessorId = selectedHero.StringId;
            return true;
        }

        internal bool TryCommitApprovedPlayerSuccession(Hero selectedHero)
        {
            if (selectedHero == null
                || _approvedPlayerSuccessorId != selectedHero.StringId
                || !IsPreparedPlayerSuccessorValid(selectedHero))
            {
                return false;
            }

            bool committed = TryCommitPreparedPlayerSuccession(selectedHero);
            if (committed)
                _approvedPlayerSuccessorId = null;
            return committed;
        }

        internal void CancelPreparedPlayerSuccessorApproval(Hero selectedHero)
        {
            if (selectedHero != null && _approvedPlayerSuccessorId == selectedHero.StringId)
                _approvedPlayerSuccessorId = null;
        }

        internal bool TryCommitPreparedPlayerSuccession(Hero selectedHero)
        {
            PendingPlayerSuccession pending = _pendingPlayerSuccession;
            Clan clan = Clan.PlayerClan;
            Hero ward = ResolveHero(pending?.WardHeroId);
            Hero dyingHero = ResolveHero(pending?.DyingHeroId);
            if (pending == null
                || clan == null
                || ward == null
                || dyingHero == null
                || !IsPreparedPlayerSuccessorValid(selectedHero))
            {
                return false;
            }

            bool selectedWasGenerated = pending.GeneratedCandidateHeroId == selectedHero.StringId || IsGeneratedRegent(selectedHero);
            if (pending.Mode == PlayerSuccessionMode.BeginRegency)
            {
                _playerRegencyHandoverPending = false;
                RegencyRecord record = new RegencyRecord(
                    clan.StringId,
                    ward.StringId,
                    selectedHero.StringId,
                    dyingHero.StringId,
                    CampaignTime.Now,
                    selectedWasGenerated);
                _regencies.RemoveAll(item => item == null || item.ClanId == clan.StringId);
                _regencies.Add(record);
                ShowRegencyStarted(clan, ward, selectedHero);
                Campaign.Current?.GetCampaignBehavior<SuccessionYearlySummaryBehavior>()?
                    .RecordRegencyStarted(selectedWasGenerated);
            }
            else if (pending.Mode == PlayerSuccessionMode.ReplaceRegent)
            {
                _playerRegencyHandoverPending = false;
                if (!TryGetRegency(clan, out RegencyRecord record))
                    return false;

                Hero formerRegent = ResolveHero(record.RegentHeroId) ?? dyingHero;
                _regentDeathsInProgress.Add(dyingHero.StringId);
                if (record.RegentWasGenerated)
                    _generatedRegentDeathsInProgress.Add(dyingHero.StringId);
                record.ReplaceRegent(selectedHero, selectedWasGenerated);
                ShowRegentReplaced(clan, ward, formerRegent, selectedHero, formerRegentDied: true);
                Campaign.Current?.GetCampaignBehavior<SuccessionYearlySummaryBehavior>()?
                    .RecordRegentReplaced(selectedWasGenerated);
            }
            else
            {
                _playerRegencyHandoverPending = false;
                if (!TryGetRegency(clan, out RegencyRecord record))
                    return false;

                _regentDeathsInProgress.Add(dyingHero.StringId);
                if (record.RegentWasGenerated)
                {
                    _generatedRegentDeathsInProgress.Add(dyingHero.StringId);
                    FeudalTitleBehavior.Instance?.ReassignClaimsForRegencyTransition(
                        dyingHero,
                        ward,
                        "generated player regent succeeded after death");
                }
                _regencies.Remove(record);
                ShowRegencyEnded(clan, ward, dyingHero);
                Campaign.Current?.GetCampaignBehavior<SuccessionYearlySummaryBehavior>()?
                    .RecordRegencyEnded();
            }

            _approvedPlayerSuccessorId = selectedHero.StringId;
            _pendingPlayerSuccession = null;
            Patches.FeudalTitleHeroNamePatch.InvalidateCache();
            Campaign.Current?.GetCampaignBehavior<DynasticHeirBehavior>()?
                .RefreshSuccessionAfterLawChange(clan.Kingdom);
            BellumCivileLogger.Log(
                $"Committed player succession; mode={pending.Mode}; clan={clan.StringId}; predecessor={dyingHero.StringId}; ward={ward.StringId}; selected={selectedHero.StringId}; generated={selectedWasGenerated}.");
            return true;
        }

        private bool IsPreparedPlayerSuccessorValid(Hero selectedHero)
        {
            PendingPlayerSuccession pending = _pendingPlayerSuccession;
            Clan clan = Clan.PlayerClan;
            Hero ward = ResolveHero(pending?.WardHeroId);
            Hero dyingHero = ResolveHero(pending?.DyingHeroId);
            if (pending == null
                || clan == null
                || ward == null
                || dyingHero == null
                || !CanSelectPendingPlayerSuccessor(selectedHero))
            {
                return false;
            }

            return pending.Mode == PlayerSuccessionMode.WardSucceeds
                ? selectedHero == ward
                    && ward.IsAlive
                    && ward.Clan == clan
                    && ward.Age >= SuccessionLawHelper.GetAgeOfMajority()
                : IsSuitablePlayerRegentCandidate(selectedHero, clan, ward, dyingHero);
        }

        internal bool ConsumeApprovedPlayerSuccessor(Hero selectedHero)
        {
            if (selectedHero == null || _approvedPlayerSuccessorId != selectedHero.StringId)
                return false;

            _approvedPlayerSuccessorId = null;
            return true;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Patches.FeudalTitleHeroNamePatch.InvalidateCache();
            ValidateRegencies(showNotifications: false);
        }

        private void OnBeforeHeroKilled(
            Hero victim,
            Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail,
            bool showNotification)
        {
            Clan clan = victim?.Clan;
            if (victim == null || clan == null)
                return;

            if (clan == Clan.PlayerClan)
            {
                if (victim != Hero.MainHero
                    && TryGetRegency(clan, out RegencyRecord playerRegency)
                    && playerRegency.WardHeroId == victim.StringId)
                {
                    _wardDeathsInProgress.Add(victim.StringId);
                    ReplaceDyingWard(clan, playerRegency, victim);
                }
                return;
            }

            if (TryGetRegency(clan, out RegencyRecord existing))
            {
                if (existing.RegentHeroId == victim.StringId)
                {
                    _regentDeathsInProgress.Add(victim.StringId);
                    if (existing.RegentWasGenerated)
                        _generatedRegentDeathsInProgress.Add(victim.StringId);
                    ReplaceRegent(clan, existing, victim, formerRegentDied: true);
                }
                else if (existing.WardHeroId == victim.StringId)
                {
                    _wardDeathsInProgress.Add(victim.StringId);
                    ReplaceDyingWard(clan, existing, victim);
                }
                return;
            }

            if (clan.Leader != victim || !CanUseRegency(clan))
                return;

            SuccessionLawSet laws = SuccessionLawHelper.GetLawsForClan(clan);
            List<Hero> legalLine = SuccessionLawHelper.GetLegalSuccessionLine(clan, victim, laws);
            Hero ward = legalLine.FirstOrDefault();
            if (ward == null)
                ward = GetHouseContinuityLine(clan, victim).FirstOrDefault();

            if (!IsUnderageWard(ward, clan))
                return;

            Hero regent = SelectRegent(clan, ward, victim, legalLine, out bool generated);
            if (regent == null)
            {
                BellumCivileLogger.Log(
                    $"Could not preserve underage succession because no regent could be appointed; clan={clan.StringId}; ward={ward.StringId}; predecessor={victim.StringId}.");
                return;
            }

            RegencyRecord record = new RegencyRecord(
                clan.StringId,
                ward.StringId,
                regent.StringId,
                victim.StringId,
                CampaignTime.Now,
                generated);
            _regencies.Add(record);
            if (!TryInstallClanLeader(clan, regent, "opening regency"))
            {
                _regencies.Remove(record);
                DiscardUnusedGeneratedRegent(regent, generated);
                return;
            }

            Patches.FeudalTitleHeroNamePatch.InvalidateCache();
            ShowRegencyStarted(clan, ward, regent);
            Campaign.Current?.GetCampaignBehavior<SuccessionYearlySummaryBehavior>()?
                .RecordRegencyStarted(generated);
            BellumCivileLogger.Log(
                $"Regency opened; clan={clan.StringId}; predecessor={victim.StringId}; ward={ward.StringId}; regent={regent.StringId}; generated={generated}; adulthood={SuccessionLawHelper.GetAgeOfMajority()}.");
        }

        private void OnHeroComesOfAge(Hero hero)
        {
            Clan clan = hero?.Clan;
            if (clan == null
                || !TryGetRegency(clan, out RegencyRecord record)
                || record.WardHeroId != hero.StringId
                || hero.Age < SuccessionLawHelper.GetAgeOfMajority())
            {
                return;
            }

            if (clan == Clan.PlayerClan)
            {
                _playerRegencyHandoverPending = true;
                BellumCivileLogger.Log(
                    $"Player regency ward came of age; clan={clan.StringId}; ward={hero.StringId}; awaiting safe handover.");
                return;
            }

            EndRegency(clan, record, hero, showNotification: true);
        }

        private void OnClanDestroyed(Clan clan)
        {
            if (clan == null)
                return;

            _regencies.RemoveAll(record => record == null || record.ClanId == clan.StringId);
            if (clan == Clan.PlayerClan)
                _playerRegencyHandoverPending = false;
        }

        private void OnHourlyTick()
        {
            _regentDeathsInProgress.Clear();
            _generatedRegentDeathsInProgress.Clear();
            _wardDeathsInProgress.Clear();
        }

        private void OnDailyTick()
        {
            ValidateRegencies(showNotifications: true);
        }

        private void OnTick(float dt)
        {
            if (_pendingPlayerSuccession != null)
                TryShowPlayerRegencyIntroduction();
            if (_playerRegencyHandoverPending)
                TryShowPlayerRegencyHandover();
        }

        private List<Hero> BuildPlayerRegentCandidates(
            Clan clan,
            Hero ward,
            Hero excludedHero,
            IEnumerable<Hero> legalLine)
        {
            IEnumerable<Hero> orderedCandidates = (legalLine ?? Enumerable.Empty<Hero>())
                .Concat(new[] { ward?.Mother, ward?.Father })
                .Concat(GetHouseContinuityLine(clan, ward))
                .Concat(clan?.Heroes?
                    .Where(hero => IsSuitablePlayerRegentCandidate(hero, clan, ward, excludedHero))
                    .OrderByDescending(hero => hero.GetSkillValue(DefaultSkills.Steward))
                    .ThenByDescending(hero => hero.GetSkillValue(DefaultSkills.Leadership))
                    .ThenByDescending(hero => hero.GetSkillValue(DefaultSkills.Charm))
                    .ThenByDescending(hero => hero.Age)
                    ?? Enumerable.Empty<Hero>());

            return orderedCandidates
                .Where(hero => IsSuitablePlayerRegentCandidate(hero, clan, ward, excludedHero))
                .Distinct()
                .ToList();
        }

        private void TryShowPlayerRegencyIntroduction()
        {
            PendingPlayerSuccession pending = _pendingPlayerSuccession;
            if (pending == null
                || pending.IntroductionShown
                || !pending.SelectionScreenOpened
                || pending.Mode == PlayerSuccessionMode.WardSucceeds
                || _isShowingPlayerRegencyIntroduction
                || InformationManager.IsAnyInquiryActive()
                || !(Game.Current?.GameStateManager?.ActiveState is MapState))
            {
                return;
            }

            Hero ward = ResolveHero(pending.WardHeroId);
            Hero predecessor = ResolveHero(pending.DyingHeroId);
            if (ward == null || predecessor == null)
                return;

            TextObject title = pending.Mode == PlayerSuccessionMode.ReplaceRegent
                ? new TextObject("{=BC_PlayerRegency_ReplacementIntroTitle}A New Regent Is Required")
                : new TextObject("{=BC_PlayerRegency_IntroTitle}The Heir Is Underage");
            TextObject description;
            if (pending.Mode == PlayerSuccessionMode.ReplaceRegent)
            {
                description = new TextObject("{=BC_PlayerRegency_ReplacementIntro}{WARD} remains the rightful head of your house but is still underage. With {REGENT} no longer able to serve, you must appoint a new regent and will continue playing as that regent until {WARD} comes of age.");
                description.SetTextVariable("REGENT", predecessor.Name);
            }
            else if (pending.UsedHouseContinuityFallback)
            {
                description = new TextObject("{=BC_PlayerRegency_ContinuityIntro}Your house's inheritance law leaves no member eligible to inherit its lands. {WARD} is its nearest surviving continuation but is still underage, so the estate will follow the law's reversion rules while a regent preserves the house. You must appoint that regent and will continue playing as them until {WARD} comes of age.");
            }
            else
            {
                SuccessionLawSet laws = SuccessionLawHelper.GetLawsForClan(Clan.PlayerClan);
                description = new TextObject("{=BC_PlayerRegency_Intro}Under your house's {GENDER_LAW} and {SUCCESSION_LAW} laws, {WARD} is the rightful heir. As {WARD} is still underage, you must appoint an adult regent and will continue playing as that regent until {WARD} comes of age.");
                description.SetTextVariable("GENDER_LAW", SuccessionLawHelper.GetGenderLawName(laws.GenderLaw));
                description.SetTextVariable("SUCCESSION_LAW", SuccessionLawHelper.GetSuccessionLawName(laws.SuccessionLaw));
            }

            description.SetTextVariable("WARD", ward.Name);
            Hero generatedRegent = ResolveHero(pending.GeneratedCandidateHeroId);
            if (generatedRegent != null)
            {
                TextObject withGeneratedRegent = new TextObject("{=BC_PlayerRegency_GeneratedIntro}{DESCRIPTION}\n\nWith no suitable adult member of the house available, {REGENT}, a distant relative, has come forward to assume the regency.");
                withGeneratedRegent.SetTextVariable("DESCRIPTION", description);
                withGeneratedRegent.SetTextVariable("REGENT", generatedRegent.Name);
                description = withGeneratedRegent;
            }

            pending.IntroductionShown = true;
            _isShowingPlayerRegencyIntroduction = true;
            InformationManager.ShowInquiry(
                new InquiryData(
                    title.ToString(),
                    description.ToString(),
                    true,
                    false,
                    new TextObject("{=BC_PlayerRegency_ChooseRegent}Choose a Regent").ToString(),
                    string.Empty,
                    () => _isShowingPlayerRegencyIntroduction = false,
                    null),
                true);
        }

        private void TryShowPlayerRegencyHandover()
        {
            if (_pendingPlayerSuccession != null
                || _isShowingPlayerRegencyIntroduction
                || _isShowingPlayerRegencyHandover
                || InformationManager.IsAnyInquiryActive())
            {
                return;
            }

            if (!TryGetRegency(Clan.PlayerClan, out RegencyRecord record))
            {
                _playerRegencyHandoverPending = false;
                return;
            }

            Hero ward = ResolveHero(record.WardHeroId);
            Hero regent = ResolveHero(record.RegentHeroId);
            if (ward == null || ward.Age < SuccessionLawHelper.GetAgeOfMajority())
            {
                _playerRegencyHandoverPending = false;
                return;
            }
            if (regent == null || !CanCompletePlayerRegencyHandover(ward, regent))
            {
                return;
            }

            TextObject description = new TextObject("{=BC_PlayerRegency_Handover}{WARD} has come of age and will now take {?WARD.GENDER}her{?}his{\\?} rightful place as head of {CLAN}. {REGENT}'s regency is at an end.\n\n(You will continue playing as {WARD}.)");
            StringHelpers.SetCharacterProperties("WARD", ward.CharacterObject, description);
            description.SetTextVariable("REGENT", regent.Name);
            description.SetTextVariable("CLAN", Clan.PlayerClan.Name);

            _isShowingPlayerRegencyHandover = true;
            InformationManager.ShowInquiry(
                new InquiryData(
                    new TextObject("{=BC_PlayerRegency_HandoverTitle}The Regency Ends").ToString(),
                    description.ToString(),
                    true,
                    false,
                    new TextObject("{=BC_PlayerRegency_HandoverContinue}Continue as Heir").ToString(),
                    string.Empty,
                    () =>
                    {
                        _isShowingPlayerRegencyHandover = false;
                        CompletePlayerRegencyHandover(record, ward, regent);
                    },
                    null),
                true);
        }

        private static bool CanCompletePlayerRegencyHandover(Hero ward, Hero regent)
        {
            MapState mapState = Game.Current?.GameStateManager?.ActiveState as MapState;
            MobileParty mainParty = MobileParty.MainParty;
            return mapState != null
                && !mapState.AtMenu
                && !mapState.MapConversationActive
                && mapState.NextIncident == null
                && !mapState.IsSimulationActive
                && Campaign.Current?.CurrentMenuContext == null
                && Campaign.Current?.ConversationManager?.IsConversationInProgress != true
                && Campaign.Current?.ConversationManager?.IsConversationFlowActive != true
                && CampaignMission.Current == null
                && PlayerEncounter.Current == null
                && PartyBase.MainParty != null
                && mainParty != null
                && mainParty.MapEvent == null
                && mainParty.SiegeEvent == null
                && mainParty.Army == null
                && ward != null
                && ward.IsAlive
                && ward.Clan == Clan.PlayerClan
                && !ward.IsDisabled
                && !ward.IsPrisoner
                && !ward.IsTraveling
                && (ward.PartyBelongedTo == null
                    || (ward.PartyBelongedTo.MapEvent == null
                        && ward.PartyBelongedTo.SiegeEvent == null
                        && ward.PartyBelongedTo.Army == null))
                && regent == Hero.MainHero
                && regent.IsAlive
                && Clan.PlayerClan?.Leader == regent;
        }

        private void CompletePlayerRegencyHandover(
            RegencyRecord record,
            Hero ward,
            Hero formerRegent)
        {
            Clan clan = Clan.PlayerClan;
            if (record == null
                || clan == null
                || !TryGetRegency(clan, out RegencyRecord current)
                || current != record
                || ward == null
                || ward.StringId != record.WardHeroId
                || ward.Age < SuccessionLawHelper.GetAgeOfMajority()
                || !CanCompletePlayerRegencyHandover(ward, formerRegent))
            {
                return;
            }

            try
            {
                if (ward.IsNotSpawned || ward.IsDisabled)
                    ward.ChangeState(Hero.CharacterStates.Active);
                if (ward.PartyBelongedTo == null && MobileParty.MainParty != null)
                    AddHeroToPartyAction.Apply(ward, MobileParty.MainParty, showNotification: false);

                if (!TryInstallClanLeader(clan, ward, "ending player regency"))
                    return;

                ChangePlayerCharacterAction.Apply(ward);
                if (Hero.MainHero != ward || clan.Leader != ward)
                    throw new InvalidOperationException("The player character or clan leader did not change to the lawful heir.");

                if (record.RegentWasGenerated)
                {
                    FeudalTitleBehavior.Instance?.ReassignClaimsForRegencyTransition(
                        formerRegent,
                        ward,
                        "generated player regency ended");
                }

                _regencies.Remove(record);
                _playerRegencyHandoverPending = false;
                Patches.FeudalTitleHeroNamePatch.InvalidateCache();
                ShowRegencyEnded(clan, ward, formerRegent);
                Campaign.Current?.GetCampaignBehavior<SuccessionYearlySummaryBehavior>()?
                    .RecordRegencyEnded();
                Campaign.Current?.GetCampaignBehavior<DynasticHeirBehavior>()?
                    .RefreshSuccessionAfterLawChange(clan.Kingdom);
                BellumCivileLogger.Log(
                    $"Player regency ended; clan={clan.StringId}; ward={ward.StringId}; former_regent={formerRegent.StringId}; generated_regent_retained={record.RegentWasGenerated}.");
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Player regency handover failed; clan={clan.StringId}; ward={ward.StringId}; former_regent={formerRegent?.StringId ?? "none"}; error={ex.GetType().Name}:{ex.Message}.");
            }
        }

        private void ReplaceRegent(Clan clan, RegencyRecord record, Hero dyingRegent, bool formerRegentDied)
        {
            bool preserveCaretakerClaims = record.RegentWasGenerated;
            Hero ward = ResolveHero(record.WardHeroId);
            if (!IsUnderageWard(ward, clan))
            {
                if (ward != null && ward.IsAlive && ward.Age >= SuccessionLawHelper.GetAgeOfMajority())
                    EndRegency(clan, record, ward, showNotification: true);
                return;
            }

            SuccessionLawSet laws = SuccessionLawHelper.GetLawsForClan(clan);
            List<Hero> legalLine = SuccessionLawHelper.GetLegalSuccessionLine(clan, ward, laws);
            Hero replacement = SelectRegent(clan, ward, dyingRegent, legalLine, out bool generated);
            if (replacement == null || !TryInstallClanLeader(clan, replacement, "regent replacement"))
            {
                BellumCivileLogger.Log(
                    $"Could not replace a dying regent; clan={clan.StringId}; ward={ward.StringId}; regent={dyingRegent?.StringId ?? "none"}.");
                return;
            }

            record.ReplaceRegent(replacement, generated);
            if (preserveCaretakerClaims)
            {
                FeudalTitleBehavior.Instance?.ReassignClaimsForRegencyTransition(
                    dyingRegent,
                    replacement,
                    "generated regent replaced");
            }
            Patches.FeudalTitleHeroNamePatch.InvalidateCache();
            ShowRegentReplaced(clan, ward, dyingRegent, replacement, formerRegentDied);
            Campaign.Current?.GetCampaignBehavior<SuccessionYearlySummaryBehavior>()?
                .RecordRegentReplaced(generated);
            BellumCivileLogger.Log(
                $"Regent replaced; clan={clan.StringId}; ward={ward.StringId}; old_regent={dyingRegent?.StringId ?? "none"}; new_regent={replacement.StringId}; generated={generated}; reason={(formerRegentDied ? "death" : "left_house")}; old_alive={dyingRegent?.IsAlive}; old_active={dyingRegent?.IsActive}; old_disabled={dyingRegent?.IsDisabled}; old_prisoner={dyingRegent?.IsPrisoner}; old_traveling={dyingRegent?.IsTraveling}.");
        }

        private void ReplaceDyingWard(Clan clan, RegencyRecord record, Hero dyingWard)
        {
            List<Hero> continuityLine = BuildContinuityLine(clan, dyingWard);
            Hero successor = continuityLine.FirstOrDefault(hero => hero != dyingWard);
            if (successor == null)
            {
                _regencies.Remove(record);
                if (clan == Clan.PlayerClan)
                    _playerRegencyHandoverPending = false;
                Patches.FeudalTitleHeroNamePatch.InvalidateCache();
                Campaign.Current?.GetCampaignBehavior<SuccessionYearlySummaryBehavior>()?
                    .RecordRegencyEnded();
                BellumCivileLogger.Log(
                    $"Regency ended without another surviving house heir; clan={clan.StringId}; ward={dyingWard?.StringId ?? "none"}; regent={record.RegentHeroId}; regent_retained=true.");
                return;
            }

            if (successor.Age >= SuccessionLawHelper.GetAgeOfMajority())
            {
                if (clan == Clan.PlayerClan)
                {
                    record.ReplaceWard(successor);
                    _playerRegencyHandoverPending = true;
                    Patches.FeudalTitleHeroNamePatch.InvalidateCache();
                    Campaign.Current?.GetCampaignBehavior<DynasticHeirBehavior>()?
                        .RefreshSuccessionAfterLawChange(clan.Kingdom);
                    BellumCivileLogger.Log(
                        $"Player regency received an adult replacement ward; clan={clan.StringId}; old_ward={dyingWard?.StringId ?? "none"}; new_ward={successor.StringId}; awaiting safe handover.");
                    return;
                }

                EndRegency(clan, record, successor, showNotification: false);
                ShowWardSucceededAfterDeath(clan, dyingWard, successor);
                return;
            }

            record.ReplaceWard(successor);
            if (clan == Clan.PlayerClan)
                _playerRegencyHandoverPending = false;
            Patches.FeudalTitleHeroNamePatch.InvalidateCache();
            Campaign.Current?.GetCampaignBehavior<DynasticHeirBehavior>()?
                .RefreshSuccessionAfterLawChange(clan.Kingdom);
            Hero regent = ResolveHero(record.RegentHeroId);
            bool regentCanRemain = clan == Clan.PlayerClan
                ? CanRemainPlayerRegent(regent, clan, successor)
                : CanRetainRegent(regent, clan, successor);
            if (!regentCanRemain)
            {
                if (clan == Clan.PlayerClan)
                {
                    BellumCivileLogger.Log(
                        $"Player regency retained for reconciliation after its ward changed; clan={clan.StringId}; ward={successor.StringId}; recorded_regent={regent?.StringId ?? "none"}.");
                    return;
                }

                Hero replacement = SelectRegent(
                    clan,
                    successor,
                    dyingWard,
                    SuccessionLawHelper.GetLegalSuccessionLine(
                        clan,
                        successor,
                        SuccessionLawHelper.GetLawsForClan(clan)),
                    out bool generated);
                if (replacement != null && TryInstallClanLeader(clan, replacement, "ward replacement"))
                {
                    Hero formerRegent = regent;
                    bool retireFormer = record.RegentWasGenerated;
                    record.ReplaceRegent(replacement, generated);
                    if (retireFormer)
                    {
                        FeudalTitleBehavior.Instance?.ReassignClaimsForRegencyTransition(
                            formerRegent,
                            replacement,
                            "generated regent replaced after ward succession");
                    }
                    Patches.FeudalTitleHeroNamePatch.InvalidateCache();
                    ShowRegentReplaced(clan, successor, formerRegent, replacement, formerRegentDied: false);
                    Campaign.Current?.GetCampaignBehavior<SuccessionYearlySummaryBehavior>()?
                        .RecordRegentReplaced(generated);
                }
            }

            ShowWardReplaced(clan, dyingWard, successor);
            BellumCivileLogger.Log(
                $"Regency ward replaced; clan={clan.StringId}; old_ward={dyingWard.StringId}; new_ward={successor.StringId}; regent={record.RegentHeroId}.");
        }

        private void EndRegency(
            Clan clan,
            RegencyRecord record,
            Hero rightfulLeader,
            bool showNotification)
        {
            if (clan == null || record == null || rightfulLeader == null || !rightfulLeader.IsAlive)
                return;

            Hero formerRegent = ResolveHero(record.RegentHeroId);
            if (rightfulLeader.IsNotSpawned || rightfulLeader.IsDisabled)
                rightfulLeader.ChangeState(Hero.CharacterStates.Active);

            if (!TryInstallClanLeader(clan, rightfulLeader, "ending regency"))
                return;

            if (record.RegentWasGenerated)
            {
                FeudalTitleBehavior.Instance?.ReassignClaimsForRegencyTransition(
                    formerRegent,
                    rightfulLeader,
                    "generated regency ended");
            }
            _regencies.Remove(record);
            Patches.FeudalTitleHeroNamePatch.InvalidateCache();
            if (showNotification)
                ShowRegencyEnded(clan, rightfulLeader, formerRegent);
            Campaign.Current?.GetCampaignBehavior<SuccessionYearlySummaryBehavior>()?
                .RecordRegencyEnded();
            Campaign.Current?.GetCampaignBehavior<DynasticHeirBehavior>()?
                .RefreshSuccessionAfterLawChange(clan.Kingdom);

            BellumCivileLogger.Log(
                $"Regency ended; clan={clan.StringId}; rightful_leader={rightfulLeader.StringId}; former_regent={formerRegent?.StringId ?? "none"}; adulthood={SuccessionLawHelper.GetAgeOfMajority()}.");
        }

        private void ValidateRegencies(bool showNotifications)
        {
            EnsureCollectionsInitialized();
            foreach (RegencyRecord record in _regencies.ToList())
            {
                Clan clan = ResolveClan(record?.ClanId);
                Hero ward = ResolveHero(record?.WardHeroId);
                Hero regent = ResolveHero(record?.RegentHeroId);
                if (record == null
                    || clan == null
                    || clan.IsEliminated
                    || (clan != Clan.PlayerClan && !CanUseRegency(clan)))
                {
                    _regencies.Remove(record);
                    continue;
                }

                if (ward == null)
                {
                    _regencies.Remove(record);
                    Patches.FeudalTitleHeroNamePatch.InvalidateCache();
                    BellumCivileLogger.Log($"Removed malformed regency with no resolvable ward; clan={clan.StringId}.");
                    continue;
                }

                if (!ward.IsAlive || ward.Clan != clan)
                {
                    ReplaceDyingWard(clan, record, ward);
                    continue;
                }

                if (clan == Clan.PlayerClan)
                {
                    ValidatePlayerRegency(record, ward, regent);
                    continue;
                }

                if (ward.Age >= SuccessionLawHelper.GetAgeOfMajority())
                {
                    EndRegency(clan, record, ward, showNotifications);
                    continue;
                }

                // Temporary absence or an external clan-leader repair does not end an
                // appointment. Keep the saved ward/regent and restore leadership when safe.
                if (CanRetainRegent(regent, clan, ward))
                {
                    if (clan.Leader != regent && IsSuitableRegent(regent, clan, ward, null))
                    {
                        Hero displaced = clan.Leader;
                        if (TryInstallClanLeader(clan, regent, "restoring recorded regent"))
                            BellumCivileLogger.Log($"Restored recorded regent; clan={clan.StringId}; ward={ward.StringId}; regent={regent.StringId}; external_leader={displaced?.StringId}.");
                    }
                    continue;
                }
                // An unresolved reference is not proof of death. Only confirmed death or
                // departure from the house authorizes a maintenance replacement.
                if (regent != null && (regent.IsDead || regent.Clan != clan))
                    ReplaceRegent(clan, record, regent, formerRegentDied: regent.IsDead);
            }
        }

        private void ValidatePlayerRegency(RegencyRecord record, Hero ward, Hero recordedRegent)
        {
            Clan clan = Clan.PlayerClan;
            Hero player = Hero.MainHero;
            if (record == null || clan == null || player == null)
                return;

            if (player == ward
                && ward.IsAlive
                && ward.Age >= SuccessionLawHelper.GetAgeOfMajority()
                && clan.Leader == ward)
            {
                if (record.RegentWasGenerated)
                {
                    FeudalTitleBehavior.Instance?.ReassignClaimsForRegencyTransition(
                        recordedRegent,
                        ward,
                        "recovered completed player regency");
                }
                _regencies.Remove(record);
                _playerRegencyHandoverPending = false;
                Patches.FeudalTitleHeroNamePatch.InvalidateCache();
                ShowRegencyEnded(clan, ward, recordedRegent);
                Campaign.Current?.GetCampaignBehavior<SuccessionYearlySummaryBehavior>()?
                    .RecordRegencyEnded();
                Campaign.Current?.GetCampaignBehavior<DynasticHeirBehavior>()?
                    .RefreshSuccessionAfterLawChange(clan.Kingdom);
                BellumCivileLogger.Log(
                    $"Recovered completed player regency record; clan={clan.StringId}; ward={ward.StringId}.");
                return;
            }

            _playerRegencyHandoverPending = ward.Age >= SuccessionLawHelper.GetAgeOfMajority();

            bool playerCanServe = CanRemainPlayerRegent(player, clan, ward);
            if (!playerCanServe)
            {
                BellumCivileLogger.Log(
                    $"Player regency could not be reconciled; clan={clan.StringId}; player={player.StringId}; ward={ward.StringId}; recorded_regent={recordedRegent?.StringId ?? "none"}.");
                return;
            }

            if (clan.Leader != player && !TryInstallClanLeader(clan, player, "reconciling player regency"))
                return;

            if (recordedRegent != player)
            {
                bool oldRegentWasGenerated = record.RegentWasGenerated;
                record.ReplaceRegent(player, wasGenerated: IsGeneratedRegent(player));
                if (oldRegentWasGenerated)
                {
                    FeudalTitleBehavior.Instance?.ReassignClaimsForRegencyTransition(
                        recordedRegent,
                        player,
                        "player regency reconciled");
                }
                Patches.FeudalTitleHeroNamePatch.InvalidateCache();
                BellumCivileLogger.Log(
                    $"Reconciled player regency with the active player character; clan={clan.StringId}; ward={ward.StringId}; old_regent={recordedRegent?.StringId ?? "none"}; new_regent={player.StringId}.");
            }
        }

        private Hero SelectRegent(
            Clan clan,
            Hero ward,
            Hero excludedHero,
            IEnumerable<Hero> legalLine,
            out bool generated)
        {
            generated = false;
            Hero regent = legalLine?.FirstOrDefault(hero => IsSuitableRegent(hero, clan, ward, excludedHero));
            if (regent != null)
            {
                generated = IsGeneratedRegent(regent);
                return regent;
            }

            regent = new[] { ward?.Mother, ward?.Father }
                .FirstOrDefault(hero => IsSuitableRegent(hero, clan, ward, excludedHero));
            if (regent != null)
            {
                generated = IsGeneratedRegent(regent);
                return regent;
            }

            regent = GetHouseContinuityLine(clan, ward)
                .FirstOrDefault(hero => IsSuitableRegent(hero, clan, ward, excludedHero));
            if (regent != null)
            {
                generated = IsGeneratedRegent(regent);
                return regent;
            }

            regent = clan.Heroes
                .Where(hero => IsSuitableRegent(hero, clan, ward, excludedHero))
                .OrderByDescending(hero => hero.GetSkillValue(DefaultSkills.Steward))
                .ThenByDescending(hero => hero.GetSkillValue(DefaultSkills.Leadership))
                .ThenByDescending(hero => hero.Age)
                .FirstOrDefault();
            if (regent != null)
            {
                generated = IsGeneratedRegent(regent);
                return regent;
            }

            regent = CreateGeneratedRegent(clan);
            generated = regent != null;
            return regent;
        }

        private static Hero CreateGeneratedRegent(Clan clan)
        {
            MBReadOnlyList<CharacterObject> templates = clan?.Culture?.LordTemplates;
            if (clan == null || templates == null || templates.Count == 0)
                return null;

            Settlement home = clan.HomeSettlement
                ?? clan.InitialHomeSettlement
                ?? clan.Settlements?.FirstOrDefault()
                ?? Settlement.All.FirstOrDefault(settlement => settlement != null
                    && (settlement.IsTown || settlement.IsCastle)
                    && settlement.Culture == clan.Culture)
                ?? Settlement.All.FirstOrDefault(settlement => settlement != null && (settlement.IsTown || settlement.IsCastle));
            if (home == null)
                return null;

            try
            {
                CharacterObject template = templates[MBRandom.RandomInt(templates.Count)];
                int minimumAge = Math.Max(SuccessionLawHelper.GetAgeOfMajority() + 8, 26);
                Hero regent = HeroCreator.CreateSpecialHero(
                    template,
                    home,
                    clan,
                    clan,
                    MBRandom.RandomInt(minimumAge, 51));
                regent.SetNewOccupation(Occupation.Lord);
                regent.IsMinorFactionHero = false;
                regent.ChangeState(Hero.CharacterStates.Active);
                regent.HeroDeveloper.SetInitialSkillLevel(DefaultSkills.Steward, MBRandom.RandomInt(100, 176));
                regent.HeroDeveloper.SetInitialSkillLevel(DefaultSkills.Leadership, MBRandom.RandomInt(100, 176));
                regent.HeroDeveloper.SetInitialSkillLevel(DefaultSkills.Charm, MBRandom.RandomInt(75, 151));
                if (regent.CurrentSettlement == null && regent.PartyBelongedTo == null)
                    EnterSettlementAction.ApplyForCharacterOnly(regent, home);
                if (Instance != null) Instance._generatedRegentIds[regent.StringId] = true;
                return regent;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Generated regent creation failed; clan={clan.StringId}; error={ex.GetType().Name}:{ex.Message}.");
                return null;
            }
        }

        private static bool TryInstallClanLeader(Clan clan, Hero newLeader, string context)
        {
            if (clan == null || newLeader == null || !newLeader.IsAlive || newLeader.Clan != clan)
                return false;
            if (clan.Leader == newLeader)
                return true;

            try
            {
                // Founding a house has no predecessor treasury or death relations to inherit.
                if (clan.Leader == null) clan.SetLeader(newLeader);
                else ChangeClanLeaderAction.ApplyWithSelectedNewLeader(clan, newLeader);
                return clan.Leader == newLeader;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Clan leader change failed during {context}; clan={clan.StringId}; leader={newLeader.StringId}; error={ex.GetType().Name}:{ex.Message}.");
                return false;
            }
        }

        private static void DiscardUnusedGeneratedRegent(Hero regent, bool wasGenerated)
        {
            if (!wasGenerated || regent == null || !regent.IsAlive || regent.IsDisabled || regent.IsClanLeader)
                return;

            try
            {
                DisableHeroAction.Apply(regent);
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log(
                    $"Could not retire generated regent; hero={regent.StringId}; error={ex.GetType().Name}:{ex.Message}.");
            }
        }

        private static List<Hero> BuildContinuityLine(Clan clan, Hero successionRoot)
        {
            SuccessionLawSet laws = SuccessionLawHelper.GetLawsForClan(clan);
            List<Hero> lawful = SuccessionLawHelper.GetLegalSuccessionLine(clan, successionRoot, laws);
            return lawful.Count > 0 ? lawful : GetHouseContinuityLine(clan, successionRoot);
        }

        private static List<Hero> GetHouseContinuityLine(Clan clan, Hero successionRoot)
        {
            return SuccessionLawHelper.GetLegalSuccessionLine(
                clan,
                successionRoot,
                new SuccessionLawSet(GenderSuccessionLaw.Equal, HouseSuccessionLaw.Primogeniture));
        }

        private static bool IsUnderageWard(Hero hero, Clan clan)
        {
            return hero != null
                && clan != null
                && hero.Clan == clan
                && hero.IsAlive
                && hero.Age < SuccessionLawHelper.GetAgeOfMajority();
        }

        private static bool CanRetainRegent(Hero hero, Clan clan, Hero ward) =>
            hero != null && clan != null && hero != ward && hero.Clan == clan
            && hero.IsAlive && hero.Age >= SuccessionLawHelper.GetAgeOfMajority();

        private static bool IsSuitableRegent(Hero hero, Clan clan, Hero ward, Hero excludedHero)
        {
            return hero != null
                && clan != null
                && hero != ward
                && hero != excludedHero
                && hero != Hero.MainHero
                && hero.Clan == clan
                && hero.IsAlive
                && hero.DeathMark == KillCharacterAction.KillCharacterActionDetail.None
                && hero.IsLord
                && hero.IsActive
                && !hero.IsDisabled
                && !hero.IsPrisoner
                && !hero.IsTraveling
                && (hero.PartyBelongedTo == null
                    || (hero.PartyBelongedTo.MapEvent == null && hero.PartyBelongedTo.SiegeEvent == null))
                && hero.Age >= SuccessionLawHelper.GetAgeOfMajority();
        }

        private static bool CanRemainPlayerRegent(Hero hero, Clan clan, Hero ward)
        {
            return hero != null
                && clan == Clan.PlayerClan
                && hero == Hero.MainHero
                && hero != ward
                && hero.Clan == clan
                && hero.IsAlive
                && !hero.IsDisabled
                && hero.Age >= SuccessionLawHelper.GetAgeOfMajority();
        }

        private static bool IsSuitablePlayerRegentCandidate(
            Hero hero,
            Clan clan,
            Hero ward,
            Hero excludedHero)
        {
            return hero != null
                && clan == Clan.PlayerClan
                && hero != ward
                && hero != excludedHero
                && hero.Clan == clan
                && hero.IsAlive
                && hero.DeathMark == KillCharacterAction.KillCharacterActionDetail.None
                && hero.IsLord
                && !hero.IsNotSpawned
                && !hero.IsDisabled
                && !hero.IsWanderer
                && !hero.IsNotable
                && !hero.IsPrisoner
                && !hero.IsTraveling
                && hero.Age >= SuccessionLawHelper.GetAgeOfMajority();
        }

        private static bool CanUseRegency(Clan clan)
        {
            return clan != null
                && clan != Clan.PlayerClan
                && !clan.IsEliminated
                && clan.IsNoble
                && !clan.IsBanditFaction
                && !clan.IsMinorFaction
                && !clan.IsClanTypeMercenary
                && !clan.IsUnderMercenaryService
                && !clan.IsRebelClan;
        }

        private void EnsureCollectionsInitialized()
        {
            if (_regencies == null)
                _regencies = new List<RegencyRecord>();
            if (_generatedRegentIds == null)
                _generatedRegentIds = new Dictionary<string, bool>();
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

        private static void ShowRegencyStarted(Clan clan, Hero ward, Hero regent)
        {
            TextObject text = new TextObject("{=BC_Regency_Started}With {WARD} still underage, {REGENT} has assumed the regency of {CLAN} until the lawful heir comes of age.");
            text.SetTextVariable("WARD", ward.Name);
            text.SetTextVariable("REGENT", regent.Name);
            text.SetTextVariable("CLAN", clan.Name);
            ShowRegencyNotification(text, clan);
        }

        private static void ShowRegentReplaced(
            Clan clan,
            Hero ward,
            Hero formerRegent,
            Hero newRegent,
            bool formerRegentDied)
        {
            TextObject text = formerRegentDied
                ? new TextObject("{=BC_Regency_RegentReplacedDeath}Following {OLD_REGENT}'s death, {NEW_REGENT} has assumed the regency of {CLAN} for {WARD}.")
                : new TextObject("{=BC_Regency_RegentReplaced}Following {OLD_REGENT}'s departure, {NEW_REGENT} has assumed the regency of {CLAN} for {WARD}.");
            text.SetTextVariable("OLD_REGENT", formerRegent?.Name ?? new TextObject("{=BC_Regency_FormerRegent}the former regent"));
            text.SetTextVariable("NEW_REGENT", newRegent.Name);
            text.SetTextVariable("WARD", ward.Name);
            text.SetTextVariable("CLAN", clan.Name);
            ShowRegencyNotification(text, clan);
        }

        private static void ShowRegencyEnded(Clan clan, Hero ward, Hero formerRegent)
        {
            TextObject text = new TextObject("{=BC_Regency_Ended}Having come of age, {WARD} has ended the regency and taken {?WARD.GENDER}her{?}his{\\?} rightful place as head of {CLAN}.");
            StringHelpers.SetCharacterProperties("WARD", ward.CharacterObject, text);
            text.SetTextVariable("CLAN", clan.Name);
            ShowRegencyNotification(text, clan);
        }

        private static void ShowWardReplaced(Clan clan, Hero formerWard, Hero newWard)
        {
            TextObject text = new TextObject("{=BC_Regency_WardReplaced}Following {OLD_WARD}'s death, {NEW_WARD} has become the underage head of {CLAN} and remains under regency.");
            text.SetTextVariable("OLD_WARD", formerWard?.Name ?? new TextObject("{=BC_Regency_FormerWard}the former heir"));
            text.SetTextVariable("NEW_WARD", newWard.Name);
            text.SetTextVariable("CLAN", clan.Name);
            ShowRegencyNotification(text, clan);
        }

        private static void ShowWardSucceededAfterDeath(Clan clan, Hero formerWard, Hero successor)
        {
            TextObject text = new TextObject("{=BC_Regency_AdultSuccessor}Following {OLD_WARD}'s death, {SUCCESSOR} has ended the regency and taken leadership of {CLAN}.");
            text.SetTextVariable("OLD_WARD", formerWard?.Name ?? new TextObject("{=BC_Regency_FormerWard}the former heir"));
            text.SetTextVariable("SUCCESSOR", successor.Name);
            text.SetTextVariable("CLAN", clan.Name);
            ShowRegencyNotification(text, clan);
        }

        private static void ShowRegencyNotification(TextObject text, Clan clan)
        {
            BellumCivileNotifications.Show(
                text,
                BellumNotificationColors.Inheritance,
                primaryKingdom: clan?.Kingdom,
                primaryClan: clan,
                isMajorEvent: clan != null && clan == clan.Kingdom?.RulingClan);
        }
    }
}
