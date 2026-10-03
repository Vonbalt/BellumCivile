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
        LoyalistInstallRuler = 8
    }

    /// <summary>
    /// Why did I do this file?
    /// To preserve landless noble clans who are exiled by Bellum Civile and keep trying to place
    /// them in a valid refuge kingdom instead of letting vanilla quietly discontinue them.
    /// </summary>
    public class ExiledClanRecoveryBehavior : CampaignBehaviorBase
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
                && CanClanBeRelocated(clan);
        }

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
            if (!CanClanBeRelocated(clan) || !IsLandlessForExile(clan)) return ExileResolutionResult.Failed;

            (FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>())
                ?.ConfiscateNonBaronyTitlesForExile(clan, originKingdom?.RulingClan, originKingdom, $"exile recovery {cause}");

            RecordExileContext(clan, originKingdom, cause);

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
        }

        private void OnDailyTick()
        {
            EnsureCollectionsInitialized();
            TryShowNextPendingAsylumRequest();

            if (_legacyExileBackfillDone) return;

            _legacyExileBackfillDone = true;

            foreach (Clan clan in Clan.All.ToList())
            {
                if (!IsRecoverableExileCandidate(clan)) continue;

                if (TryMoveClanToRefuge(clan, null, null, showRecoveryMessage: false) != ExileResolutionResult.Failed) continue;

                TrackExiledClan(clan, null, new Kingdom[0], CampaignTime.Now + CampaignTime.Days(RefugeRetryDays));
            }
        }

        private void OnWeeklyTick()
        {
            EnsureCollectionsInitialized();

            foreach (string clanId in _trackedExiledClanIds.ToList())
            {
                Clan clan = ResolveClanById(clanId);
                if (!IsRecoverableExileCandidate(clan))
                {
                    RemoveTrackedExile(clanId);
                    continue;
                }

                if (!ShouldAttemptRetry(clanId)) continue;

                TryRecoverTrackedExile(clan, showRecoveryMessage: true);
            }

            TryShowNextPendingAsylumRequest();
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom, ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            if (clan == null || string.IsNullOrEmpty(clan.StringId)) return;
            if (newKingdom == null && !clan.IsEliminated) return;

            RemoveTrackedExile(clan.StringId);
        }

        private void OnClanDestroyed(Clan clan)
        {
            if (clan == null || string.IsNullOrEmpty(clan.StringId)) return;
            RemoveTrackedExile(clan.StringId);
        }

        private bool TryRecoverTrackedExile(Clan clan, bool showRecoveryMessage)
        {
            if (clan == null || string.IsNullOrEmpty(clan.StringId)) return false;

            string clanId = clan.StringId;
            Kingdom originKingdom = ResolveKingdomById(GetDictionaryValue(_trackedOriginKingdomIds, clanId));
            Kingdom[] excludedKingdoms = ResolveKingdomsByDelimitedIds(GetDictionaryValue(_trackedExcludedKingdomIds, clanId));

            if (TryMoveClanToRefuge(clan, originKingdom, excludedKingdoms, showRecoveryMessage) != ExileResolutionResult.Failed)
                return true;

            _nextRefugeRetryDates[clanId] = CampaignTime.Now + CampaignTime.Days(RefugeRetryDays);
            return false;
        }

        private ExileResolutionResult TryMoveClanToRefuge(Clan clan, Kingdom originKingdom, Kingdom[] excludedKingdoms, bool showRecoveryMessage)
        {
            if (!CanClanBeRelocated(clan) || clan.IsMinorFaction || clan.IsUnderMercenaryService || clan == Clan.PlayerClan)
                return ExileResolutionResult.Failed;
            if (!IsLandlessForExile(clan))
                return ExileResolutionResult.Failed;

            Kingdom refuge = RefugeSelectionHelper.FindBestRefuge(clan, originKingdom, excludedKingdoms);
            if (refuge == null) return ExileResolutionResult.Failed;

            if (refuge.RulingClan == Clan.PlayerClan)
                return QueuePlayerAsylumRequest(clan, originKingdom, excludedKingdoms, refuge);

            ChangeKingdomAction.ApplyByJoinToKingdom(clan, refuge);
            RemoveTrackedExile(clan.StringId);

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
            TextObject desc = new TextObject("{=BC_Asylum_Desc}An envoy from {CLAN_LEADER} of the {CLAN_NAME} has arrived, presenting a formal request of asylum for their family as they flee persecution in their homeland.{EXILE_CONTEXT}{COURT_CONTEXT}\n\nWill you take them under your protection?");
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

            if (!IsRecoverableExileCandidate(clan) || !IsPlayerRuledValidRefuge(targetKingdom))
            {
                DenyPlayerAsylum(clanId, silent: true);
                return;
            }

            ChangeKingdomAction.ApplyByJoinToKingdom(clan, targetKingdom);
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
            return kingdom != null
                && !kingdom.IsEliminated
                && kingdom.RulingClan == Clan.PlayerClan
                && kingdom.Settlements.Any(s => s.IsTown || s.IsCastle);
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
