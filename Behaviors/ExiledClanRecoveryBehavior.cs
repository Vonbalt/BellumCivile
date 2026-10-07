using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    internal enum ExileResolutionResult
    {
        Failed,
        MovedToRefuge,
        PendingPlayerAsylum
    }

    public enum ExileCause
    {
        Unknown = 0,
        RebelIndependence = 1,
        RebelAbdication = 2,
        RebelInstallRuler = 3,
        Treason = 5,
        LoyalistIndependence = 6,
        LoyalistAbdication = 7,
        LoyalistInstallRuler = 8,
        // Values 4 and 9 belonged to retired redistribution causes and remain reserved.
        Expulsion = 10,
        KingdomDestroyed = 11,
        VoluntaryDeparture = 12
    }

    /// <summary>
    /// Why did I do this file?
    /// To preserve landless noble clans who are exiled by Bellum Civile and keep trying to place
    /// them in a valid refuge kingdom instead of letting vanilla quietly discontinue them.
    /// </summary>
    public partial class ExiledClanRecoveryBehavior : CampaignBehaviorBase
    {
        private const int RefugeRetryDays = 7;

        private List<string> _trackedExiledClanIds = new List<string>();
        private Dictionary<string, string> _trackedOriginKingdomIds = new Dictionary<string, string>();
        private Dictionary<string, string> _trackedExcludedKingdomIds = new Dictionary<string, string>();
        private Dictionary<string, int> _trackedExileCauseIds = new Dictionary<string, int>();
        private Dictionary<string, int> _trackedExileCourtFactionIds = new Dictionary<string, int>();
        private Dictionary<string, CampaignTime> _nextRefugeRetryDates = new Dictionary<string, CampaignTime>();
        private List<string> _pendingAsylumClanIds = new List<string>();
        private Dictionary<string, string> _pendingAsylumOriginKingdomIds = new Dictionary<string, string>();
        private Dictionary<string, string> _pendingAsylumExcludedKingdomIds = new Dictionary<string, string>();
        private Dictionary<string, string> _pendingAsylumTargetKingdomIds = new Dictionary<string, string>();
        private Dictionary<string, CampaignTime> _pendingAsylumRequestDates = new Dictionary<string, CampaignTime>();
        private bool _legacyExileBackfillDone;
        private bool _asylumInquiryOpen;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.OnClanDestroyedEvent.AddNonSerializedListener(this, OnClanDestroyed);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_TrackedExiledClanIds", ref _trackedExiledClanIds);
            dataStore.SyncData("BellumCivile_TrackedExileOriginKingdomIds", ref _trackedOriginKingdomIds);
            dataStore.SyncData("BellumCivile_TrackedExileExcludedKingdomIds", ref _trackedExcludedKingdomIds);
            dataStore.SyncData("BellumCivile_TrackedExileCauseIds", ref _trackedExileCauseIds);
            dataStore.SyncData("BellumCivile_TrackedExileCourtFactionIds", ref _trackedExileCourtFactionIds);
            dataStore.SyncData("BellumCivile_TrackedExileRetryDates", ref _nextRefugeRetryDates);
            dataStore.SyncData("BellumCivile_PendingAsylumClanIds", ref _pendingAsylumClanIds);
            dataStore.SyncData("BellumCivile_PendingAsylumOriginKingdomIds", ref _pendingAsylumOriginKingdomIds);
            dataStore.SyncData("BellumCivile_PendingAsylumExcludedKingdomIds", ref _pendingAsylumExcludedKingdomIds);
            dataStore.SyncData("BellumCivile_PendingAsylumTargetKingdomIds", ref _pendingAsylumTargetKingdomIds);
            dataStore.SyncData("BellumCivile_PendingAsylumRequestDates", ref _pendingAsylumRequestDates);
            dataStore.SyncData("BellumCivile_LegacyExileBackfillDone", ref _legacyExileBackfillDone);
            SyncAdmissionData(dataStore);
            EnsureCollectionsInitialized();
        }

        internal static bool IsRecoverableExileCandidate(Clan clan)
        {
            return clan != null
                && !clan.IsEliminated
                && clan != Clan.PlayerClan
                && clan.IsNoble
                && !clan.IsMinorFaction
                && !clan.IsUnderMercenaryService
                && clan.Kingdom == null
                && IsLandlessForExile(clan)
                && HasSurvivingFamily(clan);
        }

        private static bool HasSurvivingFamily(Clan clan) => clan != null && !clan.IsEliminated
            && clan.Heroes.Any(hero => hero != null && hero.IsAlive && (!hero.IsDisabled || hero.IsChild));

        internal static bool IsLandlessForExile(Clan clan)
        {
            return clan != null
                && clan.Fiefs.Count == 0
                && !clan.Settlements.Any(s => s != null && (s.IsTown || s.IsCastle));
        }

        internal static bool CanClanBeRelocated(Clan clan)
        {
            return clan != null
                && !clan.IsEliminated
                && clan.Heroes.Any(IsAdultLivingClanMember);
        }

        private static bool IsAdultLivingClanMember(Hero hero)
        {
            if (hero == null || !hero.IsAlive || hero.IsDisabled || hero.IsChild)
                return false;

            int adultAge = Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18;
            return hero.Age >= adultAge;
        }

        public bool ResolveClanExile(Clan clan, Kingdom originKingdom, Kingdom excludedKingdom = null, ExileCause cause = ExileCause.Unknown)
        {
            ExileResolutionResult result = ResolveClanExileWithResult(clan, originKingdom, excludedKingdom, cause);
            return result != ExileResolutionResult.Failed;
        }

        internal ExileResolutionResult ResolveClanExileWithResult(Clan clan, Kingdom originKingdom, Kingdom excludedKingdom = null, ExileCause cause = ExileCause.Unknown)
        {
            EnsureCollectionsInitialized();
            if (!HasSurvivingFamily(clan) || !IsLandlessForExile(clan)) return ExileResolutionResult.Failed;

            (FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>())
                ?.ConfiscateNonBaronyTitlesForExile(clan, originKingdom?.RulingClan, originKingdom, $"exile recovery {cause}");

            RecordExileContext(clan, originKingdom, cause);
            RecordDeparture(clan, originKingdom, cause);

            ExileResolutionResult refugeResult = TryMoveClanToRefuge(clan, originKingdom, new[] { excludedKingdom }, showRecoveryMessage: false);
            if (refugeResult != ExileResolutionResult.Failed)
                return refugeResult;

            if (clan.Kingdom != null)
                ChangeKingdomAction.ApplyByLeaveKingdom(clan, false);

            if (IsRecoverableExileCandidate(clan))
                TrackExiledClan(clan, originKingdom, new[] { excludedKingdom }, CampaignTime.Now + CampaignTime.Days(RefugeRetryDays));
            else
                RemoveTrackedExile(clan.StringId);

            return ExileResolutionResult.Failed;
        }

        public bool TryPreserveClanFromDiscontinuation(Clan clan)
        {
            EnsureCollectionsInitialized();
            if (!IsRecoverableExileCandidate(clan)) return false;

            string clanId = clan.StringId;
            if (string.IsNullOrEmpty(clanId)) return false;

            if (!_trackedExiledClanIds.Contains(clanId))
                TrackExiledClan(clan, null, new Kingdom[0], CampaignTime.Now);

            if (ShouldAttemptRetry(clanId))
                TryRecoverTrackedExile(clan, showRecoveryMessage: true);

            return true;
        }

        private void EnsureCollectionsInitialized()
        {
            if (_trackedExiledClanIds == null) _trackedExiledClanIds = new List<string>();
            if (_trackedOriginKingdomIds == null) _trackedOriginKingdomIds = new Dictionary<string, string>();
            if (_trackedExcludedKingdomIds == null) _trackedExcludedKingdomIds = new Dictionary<string, string>();
            if (_trackedExileCauseIds == null) _trackedExileCauseIds = new Dictionary<string, int>();
            if (_trackedExileCourtFactionIds == null) _trackedExileCourtFactionIds = new Dictionary<string, int>();
            if (_nextRefugeRetryDates == null) _nextRefugeRetryDates = new Dictionary<string, CampaignTime>();
            if (_pendingAsylumClanIds == null) _pendingAsylumClanIds = new List<string>();
            if (_pendingAsylumOriginKingdomIds == null) _pendingAsylumOriginKingdomIds = new Dictionary<string, string>();
            if (_pendingAsylumExcludedKingdomIds == null) _pendingAsylumExcludedKingdomIds = new Dictionary<string, string>();
            if (_pendingAsylumTargetKingdomIds == null) _pendingAsylumTargetKingdomIds = new Dictionary<string, string>();
            if (_pendingAsylumRequestDates == null) _pendingAsylumRequestDates = new Dictionary<string, CampaignTime>();
            EnsureAdmissionCollections();
        }

        private void OnDailyTick()
        {
            EnsureCollectionsInitialized();
            if (!_legacyExileBackfillDone || !_admissionHistoryReconciled)
            {
                ReconcileAdmissionHistory();
                BackfillUntrackedExiles();
                _legacyExileBackfillDone = true;
            }
            RecoverDueExiles();
            TryShowNextPendingAsylumRequest();
        }

        private void OnWeeklyTick()
        {
            EnsureCollectionsInitialized();
            BackfillUntrackedExiles();
        }

        private void RecoverDueExiles()
        {
            foreach (string clanId in _trackedExiledClanIds.ToList())
            {
                if (!ShouldAttemptRetry(clanId)) continue;
                Clan clan = ResolveClanById(clanId);
                if (!IsRecoverableExileCandidate(clan))
                {
                    RemoveTrackedExile(clanId);
                    continue;
                }

                try
                {
                    TryRecoverTrackedExile(clan, showRecoveryMessage: true);
                }
                catch (Exception ex)
                {
                    _nextRefugeRetryDates[clanId] = CampaignTime.Now + CampaignTime.Days(RefugeRetryDays);
                    BellumCivileLogger.Log($"Exile recovery deferred; clan={clanId}; error={ex}");
                }
            }
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom, ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            if (clan == null || string.IsNullOrEmpty(clan.StringId)) return;
            EnsureCollectionsInitialized();
            if (newKingdom == null && !clan.IsEliminated)
            {
                if (!IsHistoryEligibleClan(clan) || IsScriptedDeparture(oldKingdom)) return;
                ExileCause cause;
                switch (detail)
                {
                    case ChangeKingdomAction.ChangeKingdomActionDetail.LeaveByKingdomDestruction:
                        cause = ExileCause.KingdomDestroyed;
                        break;
                    case ChangeKingdomAction.ChangeKingdomActionDetail.LeaveKingdom:
                    case ChangeKingdomAction.ChangeKingdomActionDetail.LeaveWithRebellion:
                        cause = oldKingdom?.IsEliminated == true ? ExileCause.KingdomDestroyed : ExileCause.VoluntaryDeparture;
                        break;
                    default:
                        return;
                }
                // Explicit exile records are written before the native leave callback.
                if (!_trackedExileCauseIds.TryGetValue(clan.StringId, out int recordedCause)
                    || (ExileCause)recordedCause == ExileCause.Unknown)
                {
                    RecordExileContext(clan, oldKingdom, cause);
                    RecordDeparture(clan, oldKingdom, cause,
                        rebelled: detail == ChangeKingdomAction.ChangeKingdomActionDetail.LeaveWithRebellion);
                }
                if (IsRecoverableExileCandidate(clan))
                    TrackExiledClan(clan, oldKingdom, (IEnumerable<Kingdom>)null, CampaignTime.Now + CampaignTime.Days(1));
                return;
            }

            // Native defection can move directly between realms without a leave event.
            // Ordinary JoinKingdom actions also serve scripted transfers, so do not infer betrayal from them.
            if (newKingdom != null && oldKingdom != newKingdom
                && detail == ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdomByDefection
                && IsHistoryEligibleClan(clan) && !IsScriptedDeparture(oldKingdom) && !IsScriptedDeparture(newKingdom)
                && !_trackedExileCauseIds.ContainsKey(clan.StringId))
                RecordDeparture(clan, oldKingdom,
                    oldKingdom.IsEliminated ? ExileCause.KingdomDestroyed : ExileCause.VoluntaryDeparture, rebelled: true);

            RemoveTrackedExile(clan.StringId);
        }

        private void OnClanDestroyed(Clan clan)
        {
            if (clan == null || string.IsNullOrEmpty(clan.StringId)) return;
            RemoveTrackedExile(clan.StringId);
            RemoveDepartureHistory(clan.StringId);
        }

        private bool TryRecoverTrackedExile(Clan clan, bool showRecoveryMessage)
        {
            if (clan == null || string.IsNullOrEmpty(clan.StringId)) return false;

            string clanId = clan.StringId;
            Kingdom originKingdom = ResolveKingdomById(GetDictionaryValue(_trackedOriginKingdomIds, clanId));
            Kingdom[] excludedKingdoms = ResolveKingdomsByDelimitedIds(GetDictionaryValue(_trackedExcludedKingdomIds, clanId));

            _nextRefugeRetryDates[clanId] = CampaignTime.Now + CampaignTime.Days(RefugeRetryDays);
            if (TryMoveClanToRefuge(clan, originKingdom, excludedKingdoms, showRecoveryMessage) != ExileResolutionResult.Failed)
                return true;

            return false;
        }

        private ExileResolutionResult TryMoveClanToRefuge(Clan clan, Kingdom originKingdom, Kingdom[] excludedKingdoms, bool showRecoveryMessage)
        {
            if (!CanClanBeRelocated(clan) || clan.IsMinorFaction || clan.IsUnderMercenaryService || clan == Clan.PlayerClan)
                return ExileResolutionResult.Failed;
            if (!IsLandlessForExile(clan))
                return ExileResolutionResult.Failed;
            if (clan.Leader == null || !clan.Leader.IsAlive || clan.Leader.IsDisabled || clan.Leader.IsChild
                || clan.WarPartyComponents.Any(p => p.MobileParty?.MapEvent != null || p.MobileParty?.SiegeEvent != null))
                return ExileResolutionResult.Failed;

            Kingdom refuge = RefugeSelectionHelper.FindBestRefuge(clan, originKingdom, excludedKingdoms);
            if (refuge == null) return ExileResolutionResult.Failed;

            if (refuge.RulingClan == Clan.PlayerClan)
                return QueuePlayerAsylumRequest(clan, originKingdom, excludedKingdoms, refuge);

            ChangeKingdomAction.ApplyByJoinToKingdom(clan, refuge);
            if (clan.Kingdom != refuge) return ExileResolutionResult.Failed;
            RemoveTrackedExile(clan.StringId);
            BellumCivileLogger.Log($"Exile admitted; clan={clan.StringId}; realm={refuge.StringId}; ruler={refuge.RulingClan?.StringId}.");

            if (showRecoveryMessage)
            {
                TextObject text = new TextObject("{=BC_ExileRecovered}After a long exile, the {CLAN_NAME} have found refuge in {KINGDOM_NAME}.");
                text.SetTextVariable("CLAN_NAME", clan.Name);
                text.SetTextVariable("KINGDOM_NAME", refuge.Name);
                BellumCivileNotifications.Show(text, BellumNotificationColors.Warning, primaryKingdom: refuge, primaryClan: clan);
            }

            return ExileResolutionResult.MovedToRefuge;
        }

        private void TrackExiledClan(Clan clan, Kingdom originKingdom, Kingdom excludedKingdom, CampaignTime nextRetryDate)
        {
            TrackExiledClan(clan, originKingdom, new[] { excludedKingdom }, nextRetryDate);
        }

        private void TrackExiledClan(Clan clan, Kingdom originKingdom, IEnumerable<Kingdom> excludedKingdoms, CampaignTime nextRetryDate)
        {
            if (clan == null || string.IsNullOrEmpty(clan.StringId)) return;

            string clanId = clan.StringId;
            if (!_trackedExiledClanIds.Contains(clanId))
                _trackedExiledClanIds.Add(clanId);
            if (!_exileStartDates.ContainsKey(clanId)) _exileStartDates[clanId] = CampaignTime.Now;

            if (originKingdom != null || !_trackedOriginKingdomIds.ContainsKey(clanId))
                _trackedOriginKingdomIds[clanId] = originKingdom?.StringId ?? string.Empty;

            if (excludedKingdoms != null || !_trackedExcludedKingdomIds.ContainsKey(clanId))
                _trackedExcludedKingdomIds[clanId] = SerializeKingdomIds(excludedKingdoms);

            _nextRefugeRetryDates[clanId] = nextRetryDate;
        }

        private void RemoveTrackedExile(string clanId)
        {
            if (string.IsNullOrEmpty(clanId)) return;

            _trackedExiledClanIds.Remove(clanId);
            _trackedOriginKingdomIds.Remove(clanId);
            _trackedExcludedKingdomIds.Remove(clanId);
            _trackedExileCauseIds.Remove(clanId);
            _trackedExileCourtFactionIds.Remove(clanId);
            _nextRefugeRetryDates.Remove(clanId);
            _exileStartDates.Remove(clanId);
            RemovePendingAsylum(clanId);
        }

        private bool ShouldAttemptRetry(string clanId)
        {
            if (_pendingAsylumClanIds.Contains(clanId))
                return false;

            return !_nextRefugeRetryDates.TryGetValue(clanId, out CampaignTime nextRetryDate)
                || !nextRetryDate.IsFuture;
        }

        private ExileResolutionResult QueuePlayerAsylumRequest(Clan clan, Kingdom originKingdom, IEnumerable<Kingdom> excludedKingdoms, Kingdom targetKingdom)
        {
            if (clan == null || targetKingdom == null || targetKingdom.RulingClan != Clan.PlayerClan)
                return ExileResolutionResult.Failed;

            string clanId = clan.StringId;
            if (string.IsNullOrEmpty(clanId))
                return ExileResolutionResult.Failed;

            if (clan.Kingdom != null)
                ChangeKingdomAction.ApplyByLeaveKingdom(clan, false);

            if (!_pendingAsylumClanIds.Contains(clanId))
                _pendingAsylumClanIds.Add(clanId);

            _pendingAsylumOriginKingdomIds[clanId] = originKingdom?.StringId ?? string.Empty;
            _pendingAsylumExcludedKingdomIds[clanId] = SerializeKingdomIds(excludedKingdoms);
            _pendingAsylumTargetKingdomIds[clanId] = targetKingdom.StringId;
            _pendingAsylumRequestDates[clanId] = CampaignTime.Now;

            TrackExiledClan(clan, originKingdom, excludedKingdoms, CampaignTime.Now + CampaignTime.Days(RefugeRetryDays));
            TryShowNextPendingAsylumRequest();
            return ExileResolutionResult.PendingPlayerAsylum;
        }

        private void TryShowNextPendingAsylumRequest()
        {
            if (_asylumInquiryOpen)
                return;

            foreach (string clanId in _pendingAsylumClanIds.ToList())
            {
                Clan clan = ResolveClanById(clanId);
                Kingdom targetKingdom = ResolveKingdomById(GetDictionaryValue(_pendingAsylumTargetKingdomIds, clanId));

                if (!IsRecoverableExileCandidate(clan))
                {
                    RemovePendingAsylum(clanId);
                    continue;
                }
                if (!CanClanBeRelocated(clan) || clan.Leader?.IsAlive != true || clan.Leader.IsDisabled || clan.Leader.IsChild)
                {
                    RemovePendingAsylum(clanId);
                    _nextRefugeRetryDates[clanId] = CampaignTime.Now + CampaignTime.Days(RefugeRetryDays);
                    continue;
                }

                if (!IsPlayerRuledValidRefuge(targetKingdom))
                {
                    DenyPlayerAsylum(clanId, silent: true);
                    continue;
                }

                ShowPlayerAsylumInquiry(clan, targetKingdom);
                return;
            }
        }

        private void ShowPlayerAsylumInquiry(Clan clan, Kingdom targetKingdom)
        {
            if (clan == null || targetKingdom == null || string.IsNullOrEmpty(clan.StringId))
                return;

            _asylumInquiryOpen = true;
            string clanId = clan.StringId;

            TextObject title = new TextObject("{=BC_Asylum_Title}Request for Asylum");
            TextObject desc = new TextObject("{=BC_Asylum_RequestDesc}An envoy from {CLAN_LEADER} of the {CLAN_NAME} has arrived, seeking a new home and protection for their landless family.{EXILE_CONTEXT}{COURT_CONTEXT}\n\nWill you accept their allegiance?");
            desc.SetTextVariable("CLAN_LEADER", clan.Leader?.Name ?? clan.Name);
            desc.SetTextVariable("CLAN_NAME", clan.Name);
            desc.SetTextVariable("EXILE_CONTEXT", BuildExileCauseContext(clanId, clan));
            desc.SetTextVariable("COURT_CONTEXT", BuildCourtFactionContext(clanId));

            InquiryData inquiry = new InquiryData(
                title.ToString(),
                desc.ToString(),
                true,
                true,
                new TextObject("{=BC_Asylum_Accept}Accept Exiles").ToString(),
                new TextObject("{=BC_Asylum_Deny}Deny Their Request").ToString(),
                () =>
                {
                    _asylumInquiryOpen = false;
                    AcceptPlayerAsylum(clanId);
                    TryShowNextPendingAsylumRequest();
                },
                () =>
                {
                    _asylumInquiryOpen = false;
                    DenyPlayerAsylum(clanId, silent: false);
                    TryShowNextPendingAsylumRequest();
                });

            InformationManager.ShowInquiry(inquiry, true);
        }

        private void AcceptPlayerAsylum(string clanId)
        {
            Clan clan = ResolveClanById(clanId);
            Kingdom targetKingdom = ResolveKingdomById(GetDictionaryValue(_pendingAsylumTargetKingdomIds, clanId));

            if (!IsRecoverableExileCandidate(clan) || !CanClanBeRelocated(clan)
                || clan.Leader?.IsAlive != true || clan.Leader.IsDisabled || clan.Leader.IsChild
                || !IsPlayerRuledValidRefuge(targetKingdom))
            {
                DenyPlayerAsylum(clanId, silent: true);
                return;
            }

            ChangeKingdomAction.ApplyByJoinToKingdom(clan, targetKingdom);
            if (clan.Kingdom != targetKingdom)
            {
                DenyPlayerAsylum(clanId, silent: true);
                return;
            }
            RecordPlayerInvitation(clan, targetKingdom);
            RemoveTrackedExile(clanId);

            TextObject text = new TextObject("{=BC_Asylum_AcceptedMsg}You have granted asylum to the {CLAN_NAME}. They have joined {KINGDOM_NAME} under your protection.");
            text.SetTextVariable("CLAN_NAME", clan.Name);
            text.SetTextVariable("KINGDOM_NAME", targetKingdom.Name);
            BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Success);
        }

        private void DenyPlayerAsylum(string clanId, bool silent)
        {
            Clan clan = ResolveClanById(clanId);
            Kingdom originKingdom = ResolveKingdomById(GetDictionaryValue(_pendingAsylumOriginKingdomIds, clanId));
            List<Kingdom> excludedKingdoms = ResolveKingdomsByDelimitedIds(GetDictionaryValue(_pendingAsylumExcludedKingdomIds, clanId)).ToList();
            Kingdom targetKingdom = ResolveKingdomById(GetDictionaryValue(_pendingAsylumTargetKingdomIds, clanId));
            if (targetKingdom != null && !excludedKingdoms.Contains(targetKingdom))
                excludedKingdoms.Add(targetKingdom);

            RemovePendingAsylum(clanId);

            if (!IsRecoverableExileCandidate(clan))
            {
                RemoveTrackedExile(clanId);
                return;
            }

            ExileResolutionResult result = TryMoveClanToRefuge(clan, originKingdom, excludedKingdoms.ToArray(), showRecoveryMessage: false);
            if (result == ExileResolutionResult.MovedToRefuge)
            {
                if (!silent && clan.Kingdom != null)
                {
                    TextObject text = new TextObject("{=BC_Asylum_DeniedFoundMsg}You denied asylum to the {CLAN_NAME}. They have found refuge in {KINGDOM_NAME} instead.");
                    text.SetTextVariable("CLAN_NAME", clan.Name);
                    text.SetTextVariable("KINGDOM_NAME", clan.Kingdom.Name);
                    BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Warning);
                }
                return;
            }

            TrackExiledClan(clan, originKingdom, excludedKingdoms, CampaignTime.Now + CampaignTime.Days(RefugeRetryDays));

            if (!silent)
            {
                TextObject text = new TextObject("{=BC_Asylum_DeniedExileMsg}You denied asylum to the {CLAN_NAME}. With no other realm willing to shelter them, they remain in exile.");
                text.SetTextVariable("CLAN_NAME", clan.Name);
                BellumCivileNotifications.ShowPersonal(text, BellumNotificationColors.Warning);
            }
        }

        private void RemovePendingAsylum(string clanId)
        {
            if (string.IsNullOrEmpty(clanId)) return;

            _pendingAsylumClanIds.Remove(clanId);
            _pendingAsylumOriginKingdomIds.Remove(clanId);
            _pendingAsylumExcludedKingdomIds.Remove(clanId);
            _pendingAsylumTargetKingdomIds.Remove(clanId);
            _pendingAsylumRequestDates.Remove(clanId);
        }

        private static bool IsPlayerRuledValidRefuge(Kingdom kingdom)
        {
            return RefugeSelectionHelper.IsValidRefuge(kingdom) && kingdom.RulingClan == Clan.PlayerClan;
        }

        private static string GetDictionaryValue(Dictionary<string, string> dictionary, string key)
        {
            if (dictionary == null || string.IsNullOrEmpty(key)) return string.Empty;
            return dictionary.TryGetValue(key, out string value) ? value : string.Empty;
        }

        private static string SerializeKingdomIds(IEnumerable<Kingdom> kingdoms)
        {
            if (kingdoms == null) return string.Empty;

            return string.Join("|", kingdoms
                .Where(k => k != null && !string.IsNullOrEmpty(k.StringId))
                .Select(k => k.StringId)
                .Distinct()
                .ToArray());
        }

        private static Kingdom[] ResolveKingdomsByDelimitedIds(string kingdomIds)
        {
            if (string.IsNullOrEmpty(kingdomIds)) return new Kingdom[0];

            return kingdomIds
                .Split('|')
                .Select(ResolveKingdomById)
                .Where(k => k != null)
                .Distinct()
                .ToArray();
        }

        private static Clan ResolveClanById(string clanId)
        {
            if (string.IsNullOrEmpty(clanId)) return null;
            return Clan.All.FirstOrDefault(c => c.StringId == clanId);
        }

        private static Kingdom ResolveKingdomById(string kingdomId)
        {
            if (string.IsNullOrEmpty(kingdomId)) return null;
            return Kingdom.All.FirstOrDefault(k => k.StringId == kingdomId);
        }

        private void RecordExileContext(Clan clan, Kingdom originKingdom, ExileCause cause)
        {
            if (clan == null || string.IsNullOrEmpty(clan.StringId))
                return;

            string clanId = clan.StringId;
            if (originKingdom != null || !_trackedOriginKingdomIds.ContainsKey(clanId))
                _trackedOriginKingdomIds[clanId] = originKingdom?.StringId ?? string.Empty;

            if (cause != ExileCause.Unknown || !_trackedExileCauseIds.ContainsKey(clanId))
                _trackedExileCauseIds[clanId] = (int)cause;

            FactionObject ideology = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>()?.GetIdeologicalFaction(clan);
            if (ideology != null && ideology.IsIdeology)
                _trackedExileCourtFactionIds[clanId] = (int)ideology.Type;
            else if (!_trackedExileCourtFactionIds.ContainsKey(clanId))
                _trackedExileCourtFactionIds[clanId] = -1;
        }

        private string BuildExileCauseContext(string clanId, Clan clan)
        {
            if (string.IsNullOrEmpty(clanId))
                return string.Empty;

            Kingdom originKingdom = ResolveKingdomById(GetDictionaryValue(_trackedOriginKingdomIds, clanId));
            if (originKingdom == null)
                return string.Empty;

            ExileCause cause = ExileCause.Unknown;
            if (_trackedExileCauseIds.TryGetValue(clanId, out int causeValue))
                cause = (ExileCause)causeValue;

            TextObject text;
            switch (cause)
            {
                case ExileCause.RebelIndependence:
                    text = new TextObject("{=BC_Asylum_Cause_RebelIndependence}\n\nReports say the {CLAN_NAME} were exiled after trying to secede from {OLD_KINGDOM}.");
                    break;
                case ExileCause.RebelAbdication:
                    text = new TextObject("{=BC_Asylum_Cause_RebelAbdication}\n\nReports say the {CLAN_NAME} were exiled after trying to overthrow the ruler of {OLD_KINGDOM}.");
                    break;
                case ExileCause.RebelInstallRuler:
                    text = new TextObject("{=BC_Asylum_Cause_RebelInstallRuler}\n\nReports say the {CLAN_NAME} were exiled after trying to claim the crown of {OLD_KINGDOM}.");
                    break;
                case ExileCause.Treason:
                    text = new TextObject("{=BC_Asylum_Cause_Treason}\n\nReports say the {CLAN_NAME} were exiled from {OLD_KINGDOM} after being condemned for treason.");
                    break;
                case ExileCause.LoyalistIndependence:
                    text = new TextObject("{=BC_Asylum_Cause_LoyalistIndependence}\n\nReports say the {CLAN_NAME} were exiled after resisting the secession of rebels from {OLD_KINGDOM}.");
                    break;
                case ExileCause.LoyalistAbdication:
                    text = new TextObject("{=BC_Asylum_Cause_LoyalistAbdication}\n\nReports say the {CLAN_NAME} were exiled after defending the deposed ruler of {OLD_KINGDOM}.");
                    break;
                case ExileCause.LoyalistInstallRuler:
                    text = new TextObject("{=BC_Asylum_Cause_LoyalistInstallRuler}\n\nReports say the {CLAN_NAME} were exiled after opposing the new claimant in {OLD_KINGDOM}.");
                    break;
                case ExileCause.KingdomDestroyed:
                    text = new TextObject("{=BC_Asylum_Cause_RealmDestroyed}\n\nTheir former realm, {OLD_KINGDOM}, has fallen, leaving their house without a liege.");
                    break;
                case ExileCause.VoluntaryDeparture:
                    text = new TextObject("{=BC_Asylum_Cause_Departure}\n\nTheir house has left the service of {OLD_KINGDOM} and is seeking a new allegiance.");
                    break;
                default:
                    text = new TextObject("{=BC_Asylum_Cause_Unknown}\n\nReports say the {CLAN_NAME} were exiled from {OLD_KINGDOM}.");
                    break;
            }

            text.SetTextVariable("CLAN_NAME", clan?.Name ?? new TextObject("?"));
            text.SetTextVariable("OLD_KINGDOM", originKingdom.Name);
            return text.ToString();
        }

        private string BuildCourtFactionContext(string clanId)
        {
            if (string.IsNullOrEmpty(clanId)
                || !_trackedExileCourtFactionIds.TryGetValue(clanId, out int factionValue)
                || factionValue < 0)
                return string.Empty;

            FactionType factionType = (FactionType)factionValue;
            Kingdom originKingdom = null;
            if (_trackedOriginKingdomIds.TryGetValue(clanId, out string originKingdomId))
            {
                originKingdom = Kingdom.All.FirstOrDefault(kingdom =>
                    kingdom != null && kingdom.StringId == originKingdomId);
            }

            TextObject factionName = GetCourtFactionName(factionType, originKingdom);
            if (factionName == null)
                return string.Empty;

            TextObject text = new TextObject("{=BC_Asylum_CourtFaction}\n\nThe same reports state they last sat in court as members of the {COURT_FACTION}.");
            text.SetTextVariable("COURT_FACTION", factionName);
            return text.ToString();
        }

        private static TextObject GetCourtFactionName(FactionType factionType, Kingdom kingdom)
        {
            return factionType == FactionType.Royalists
                || factionType == FactionType.Glory
                || factionType == FactionType.Nobility
                || factionType == FactionType.Liberty
                    ? CourtInstitutionDisplayHelper.GetCourtFactionName(factionType, kingdom)
                    : null;
        }
    }
}
