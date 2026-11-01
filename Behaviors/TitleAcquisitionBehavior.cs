using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public sealed class TitleAcquisitionBehavior : CampaignBehaviorBase
    {
        private List<TitleAcquiredMapNotification> _pending = new List<TitleAcquiredMapNotification>();
        private List<Kingdom> _newRealms = new List<Kingdom>();
        private readonly HashSet<Settlement> _nativeTransfers = new HashSet<Settlement>();
        private bool _ready;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => InitializeUi());
            CampaignEvents.OnAfterSessionLaunchedEvent.AddNonSerializedListener(this, _ => _ready = true);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, _ => Process());
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, SettlementChanged);
            CampaignEvents.KingdomCreatedEvent.AddNonSerializedListener(this, RealmCreated);
            CampaignEvents.OnPlayerCharacterChangedEvent.AddNonSerializedListener(this, PlayerChanged);
        }

        private static void InitializeUi()
        {
            try { Patches.TitleAcquisitionNotificationPatch.Initialize(); }
            catch (Exception ex) { BellumCivileLogger.Log("Title acquisition notification UI could not be registered: " + ex); }
        }

        public override void SyncData(IDataStore store)
        {
            store.SyncData("BC_TitleAcquisitionNotices", ref _pending);
            store.SyncData("BC_TitleAcquisitionNewRealms", ref _newRealms);
            _pending = _pending ?? new List<TitleAcquiredMapNotification>();
            _newRealms = _newRealms ?? new List<Kingdom>();
            if (store.IsLoading) { _ready = false; _nativeTransfers.Clear(); }
        }

        // Notification failures must never interrupt the political action that already succeeded.
        private static void Record(Action<TitleAcquisitionBehavior> action)
        {
            try
            {
                var behavior = Campaign.Current?.GetCampaignBehavior<TitleAcquisitionBehavior>();
                if (behavior?._ready == true) action(behavior);
            }
            catch (Exception ex) { BellumCivileLogger.Log("Title acquisition notice could not be queued: " + ex); }
        }

        internal static void Acquired(Clan clan, FeudalTitleRecord title, string oldLegal, string oldActual)
            => Record(b => b.QueueTitle(clan, title, oldLegal, oldActual));
        internal static void Granted(PersonalLordshipRecord grant)
            => Record(b => { if (grant?.Holder != null && grant.Holder == Hero.MainHero && grant.IsValid)
                b.Queue(new TitleAcquiredMapNotification(null, grant, grant.Holder, 0)); });
        internal static void Acceded(Kingdom realm, Hero predecessor, Hero successor)
            => Record(b => { if (successor != null && successor == Hero.MainHero && predecessor != successor)
                b.QueueTitle(successor.Clan, FeudalTitleBehavior.Instance?.GetKingdomPoliticalTitle(realm), null, null); });

        private void QueueTitle(Clan clan, FeudalTitleRecord title, string oldLegal, string oldActual)
        {
            if (!_ready || clan == null || clan != Clan.PlayerClan || clan.Leader != Hero.MainHero
                || title?.IsActive != true) return;
            int rights = (title.DeJureHolderClanId == clan.StringId && oldLegal != clan.StringId ? 1 : 0)
                | (title.DeFactoHolderClanId == clan.StringId && oldActual != clan.StringId ? 2 : 0);
            if (rights == 0) return;
            var notice = new TitleAcquiredMapNotification(title, null, Hero.MainHero, rights);
            notice.NativeCovered = title.TitleType == FeudalTitleType.Barony
                && _nativeTransfers.Any(s => s.StringId == title.CapitalSettlementId);
            Queue(notice);
        }

        private void Queue(TitleAcquiredMapNotification notice)
        {
            var prior = _pending.FirstOrDefault(n => notice.SameAcquisition(n));
            if (prior == null) _pending.Add(notice);
            else { prior.Rights |= notice.Rights; prior.NativeCovered |= notice.NativeCovered; }
        }

        private void SettlementChanged(Settlement settlement, bool openToClaim, Hero owner, Hero previous,
            Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!_ready || owner == null || owner != Hero.MainHero || settlement == null) return;
            _nativeTransfers.Add(settlement);
            foreach (var notice in _pending.Where(n => n != null && n.Lordship == null))
            {
                var title = FeudalTitleBehavior.Instance?.GetTitle(notice.TitleId);
                if (title?.TitleType == FeudalTitleType.Barony && title.CapitalSettlementId == settlement.StringId)
                    notice.NativeCovered = true;
            }
        }

        private void RealmCreated(Kingdom realm)
        {
            if (_ready && realm?.Leader != null && realm.Leader == Hero.MainHero
                && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm) && !_newRealms.Contains(realm)) _newRealms.Add(realm);
        }

        private void PlayerChanged(Hero previous, Hero current, MobileParty party, bool partyChanged)
        {
            if (!_ready || previous == null || current == null || previous == current || previous.Clan != current.Clan
                || current != Hero.MainHero || current.Clan?.Leader != current || FeudalTitleBehavior.Instance == null) return;
            foreach (var title in FeudalTitleBehavior.Instance.GetTitlesHeldByClan(current.Clan)
                .Concat(FeudalTitleBehavior.Instance.GetTitlesHeldByClan(current.Clan, false)).Distinct())
                QueueTitle(current.Clan, title, null, null);
        }

        private static bool InProgress(TitleAcquiredMapNotification notice)
        {
            var clan = notice.ReceivingClan;
            var realm = clan?.Kingdom;
            return CivilWarConflictBehavior.IsRealmTransferPending(realm)
                || CrownAccessionBehavior.Instance?.IsPending(realm) == true
                || CrownAccessionBehavior.Instance?.IsRealmUnionProtected(realm) == true
                || notice.TitleId != null && Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()
                    ?.IsCrownPromotionTitleProtected(notice.TitleId) == true;
        }

        private void Process()
        {
            if (!_ready || _pending.Count == 0 && _newRealms.Count == 0) { _nativeTransfers.Clear(); return; }
            try
            {
                foreach (var realm in _newRealms.ToList())
                {
                    if (realm == null || realm.IsEliminated || realm.Leader != Hero.MainHero
                        || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)) { _newRealms.Remove(realm); continue; }
                    var title = FeudalTitleBehavior.Instance?.GetKingdomPoliticalTitle(realm);
                    if (title == null) continue;
                    QueueTitle(Clan.PlayerClan, title, null, null);
                    _newRealms.Remove(realm);
                }
                var information = Campaign.Current?.CampaignInformationManager;
                if (information == null) return;
                foreach (var notice in _pending.ToList())
                {
                    if (notice == null) { _pending.Remove(notice); continue; }
                    try
                    {
                        if (InProgress(notice)) continue;
                        if (!notice.IsValid()) { _pending.Remove(notice); continue; }
                        var title = FeudalTitleBehavior.Instance?.GetTitle(notice.TitleId);
                        bool native = notice.NativeCovered || title?.TitleType == FeudalTitleType.Barony
                            && _nativeTransfers.Any(s => s.StringId == title.CapitalSettlementId);
                        if (!native)
                        {
                            TitleAcquiredMapNotification unread = null;
                            information.InformationDataExists<TitleAcquiredMapNotification>(n =>
                            {
                                if (!n.SameAcquisition(notice) || !n.IsValid()) return false;
                                unread = n;
                                return true;
                            });
                            if (unread != null) unread.Rights |= notice.Rights;
                            else information.NewMapNoticeAdded(notice);
                        }
                    }
                    catch (Exception ex) { BellumCivileLogger.Log("Title acquisition notice could not be displayed: " + ex); }
                    _pending.Remove(notice);
                }
            }
            catch (Exception ex) { BellumCivileLogger.Log("Title acquisition notice deferred: " + ex); }
            finally { _nativeTransfers.Clear(); }
        }
    }
}
