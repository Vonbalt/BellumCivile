using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
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
    public partial class ExiledClanRecoveryBehavior
    {
        internal const int AsylumVisitDays = 7;
        private Dictionary<string, AsylumVisitRecord> _asylumVisits = new Dictionary<string, AsylumVisitRecord>();
        private float _asylumTickDelay;

        private void SyncAsylumVisits(IDataStore store)
        {
            store.SyncData("BellumCivile_AsylumVisits", ref _asylumVisits);
            if (_asylumVisits == null) _asylumVisits = new Dictionary<string, AsylumVisitRecord>();
            if (store.IsLoading)
            {
                _asylumInquiryOpen = false;
                _asylumConversation = null;
                _asylumOfferedVillage = null;
                _asylumAdmissionSucceeded = _asylumOathSworn = _asylumGrantSucceeded = false;
            }
        }

        private static bool IsAsylumHost(Settlement settlement, Kingdom realm)
            => settlement != null && (settlement.IsTown || settlement.IsCastle) && settlement.Town != null
                && settlement.OwnerClan?.Kingdom == realm && !settlement.IsUnderSiege
                && settlement.Party?.MapEvent == null
                && settlement.LocationComplex?.GetLocationWithId("lordshall") != null;

        internal static Settlement FindAsylumSettlement(Kingdom realm)
        {
            if (!IsPlayerRuledValidRefuge(realm)) return null;
            var holdings = realm.Settlements.Where(s => s != null && (s.IsTown || s.IsCastle)).ToList();
            var personal = holdings.Where(s => s.OwnerClan == Clan.PlayerClan).ToList();
            return (personal.Count > 0 ? personal : holdings)
                .Where(s => IsAsylumHost(s, realm)).OrderByDescending(s => s.Town.Prosperity)
                .ThenBy(s => s.StringId, StringComparer.Ordinal).FirstOrDefault();
        }

        private static bool CanPrepareAsylumGuest(Hero guest)
        {
            if (guest?.IsAlive != true || guest.IsDisabled || guest.IsChild || guest.IsPrisoner
                || guest.IsTraveling || guest.GovernorOf != null || guest.PartyBelongedToAsPrisoner != null
                || guest.CurrentSettlement?.IsUnderSiege == true) return false;
            var party = guest.PartyBelongedTo;
            return party == null || (party != MobileParty.MainParty && party.IsActive && party.IsLordParty
                && party.LeaderHero == guest && party.ActualClan == guest.Clan && !party.IsDisbanding
                && party.MapEvent == null && party.MapEventSide == null && party.SiegeEvent == null && party.BesiegerCamp == null
                && party.Army == null && party.AttachedTo == null && !party.IsCurrentlyUsedByAQuest
                && !party.IsCurrentlyAtSea && !party.IsTransitionInProgress && party.Position.IsOnLand
                && party.Ai != null && !party.Ai.IsDisabled && !party.Ai.DoNotMakeNewDecisions);
        }

        private static bool CanMutateAsylumWorld()
            => Campaign.Current != null && CampaignMission.Current == null
                && Campaign.Current.ConversationManager?.IsConversationInProgress != true
                && Campaign.Current.ConversationManager?.IsConversationFlowActive != true
                && MobileParty.MainParty?.MapEvent == null && MobileParty.MainParty?.SiegeEvent == null;

        private static bool CanPresentAsylumNotice()
        {
            var map = Game.Current?.GameStateManager?.ActiveState as MapState;
            return CanMutateAsylumWorld() && map != null && !map.AtMenu && !map.MapConversationActive
                && !map.IsSimulationActive && map.NextIncident == null && Campaign.Current.CurrentMenuContext == null
                && !InformationManager.IsAnyInquiryActive() && PlayerEncounter.Current == null
                && Hero.MainHero?.IsPrisoner == false;
        }

        private static bool AsylumAuthorityValid(AsylumVisitRecord visit)
            => visit?.Ruler != null && visit.Ruler == Hero.MainHero && visit.Ruler.IsAlive && !visit.Ruler.IsDisabled
                && visit.Realm?.Leader == visit.Ruler && IsPlayerRuledValidRefuge(visit.Realm);

        private static bool AsylumGuestValid(AsylumVisitRecord visit)
            => visit?.Clan != null && !visit.Clan.IsEliminated && visit.Guest?.IsAlive == true
                && !visit.Guest.IsDisabled && !visit.Guest.IsChild && !visit.Guest.IsPrisoner
                && visit.Guest.PartyBelongedToAsPrisoner == null && visit.Clan.Leader == visit.Guest
                && visit.Guest.Clan == visit.Clan;

        private bool TryCreateAsylumVisit(Clan clan, Kingdom realm)
        {
            if (!IsRecoverableExileCandidate(clan) || !CanPrepareAsylumGuest(clan.Leader)
                || realm?.Leader != Hero.MainHero || clan.IsAtWarWith(realm)) return false;
            var settlement = FindAsylumSettlement(realm);
            if (settlement == null) return false;
            var visit = new AsylumVisitRecord { Clan = clan, Guest = clan.Leader, Realm = realm,
                Ruler = Hero.MainHero, Settlement = settlement, Party = clan.Leader.PartyBelongedTo,
                Deadline = CampaignTime.Now + CampaignTime.Days(AsylumVisitDays) };
            _asylumVisits[clan.StringId] = visit;
            try
            {
                PlaceAsylumGuest(visit);
                if (!AsylumGuestValid(visit) || !AsylumAuthorityValid(visit) || clan.Kingdom != null
                    || visit.Guest.CurrentSettlement != settlement)
                { visit.State = AsylumVisitRecord.Cancelled; return false; }
                BellumCivileLogger.Log($"Asylum audience arranged; clan={clan.StringId}; guest={visit.Guest.StringId}; settlement={settlement.StringId}; deadline={visit.Deadline.ToDays}.");
                return true;
            }
            catch (Exception ex)
            {
                visit.State = AsylumVisitRecord.Cancelled;
                BellumCivileLogger.Log($"Asylum placement deferred; clan={clan.StringId}; error={ex}");
                return false;
            }
        }

        private static void PlaceAsylumGuest(AsylumVisitRecord visit)
        {
            if (visit.Party == null)
            {
                TeleportHeroAction.ApplyImmediateTeleportToSettlement(visit.Guest, visit.Settlement);
                return;
            }
            // EnterSettlementAction updates the locator, settlement lists and position at the gate.
            // TeleportHeroAction must not be used here: it would remove the commander from the roster.
            if (visit.Party.CurrentSettlement != null && visit.Party.CurrentSettlement != visit.Settlement)
                LeaveSettlementAction.ApplyForParty(visit.Party);
            visit.Party.SetMoveModeHold();
            if (visit.Party.CurrentSettlement != visit.Settlement)
                EnterSettlementAction.ApplyForParty(visit.Party, visit.Settlement);
            visit.PartyHoldUntil = CampaignTime.Now + CampaignTime.Hours(AsylumVisitDays * 24);
            visit.PartyHeld = true;
            visit.Party.Ai.DisableForHours(AsylumVisitDays * 24);
        }

        internal bool IsAwaitingAsylum(Hero hero)
            => hero?.Clan != null && _asylumVisits.TryGetValue(hero.Clan.StringId, out var visit)
                && visit.State == AsylumVisitRecord.Waiting && visit.Guest == hero
                && visit.Deadline.IsFuture && AsylumAuthorityValid(visit) && AsylumGuestValid(visit)
                && visit.Clan.Kingdom == null && visit.Settlement == hero.CurrentSettlement;

        private void OnAsylumTick(float dt)
        {
            if (_pendingAsylumClanIds.Count == 0 && _asylumVisits.Count == 0) return;
            _asylumTickDelay -= dt;
            if (_asylumTickDelay > 0f) return;
            _asylumTickDelay = 1f;
            if (_asylumInquiryOpen && !InformationManager.IsAnyInquiryActive()) _asylumInquiryOpen = false;
            ProcessAsylumVisits();
            TryShowNextPendingAsylumRequest();
        }

        private void ProcessAsylumVisits()
        {
            if (!CanMutateAsylumWorld()) return;
            EnsureCollectionsInitialized();
            foreach (string id in _pendingAsylumClanIds.ToList())
            {
                if (_asylumVisits.ContainsKey(id)) continue;
                // Do not consume the audience window while another screen prevents its notice.
                if (!CanPresentAsylumNotice()) break;
                var clan = ResolveClanById(id);
                var realm = ResolveKingdomById(GetDictionaryValue(_pendingAsylumTargetKingdomIds, id));
                // Legacy requests receive a fresh seven-day visit after successful placement.
                if (!TryCreateAsylumVisit(clan, realm)) DenyPlayerAsylum(id, silent: true);
            }
            foreach (var pair in _asylumVisits.ToList())
            {
                var visit = pair.Value;
                if (visit == null) { _asylumVisits.Remove(pair.Key); continue; }
                try
                {
                    if (visit.State == AsylumVisitRecord.Accepted)
                    {
                        if (CompleteAsylumAdmission(visit))
                        {
                            FinishAsylumVisit(pair.Key, visit, leaveSettlement: false);
                            continue;
                        }
                        visit.State = AsylumVisitRecord.Cancelled;
                    }
                    if (visit.State == AsylumVisitRecord.Waiting)
                    {
                        if (!AsylumAuthorityValid(visit) || !AsylumGuestValid(visit) || visit.Clan.Kingdom != null
                            || visit.Clan.IsAtWarWith(visit.Realm)
                            || !IsAsylumHost(visit.Settlement, visit.Realm) || visit.Guest.CurrentSettlement != visit.Settlement
                            || (visit.Party != null && visit.Guest.PartyBelongedTo != visit.Party))
                            visit.State = AsylumVisitRecord.Cancelled;
                        else if (!visit.Deadline.IsFuture) visit.State = AsylumVisitRecord.Expired;
                        else continue;
                    }
                    bool expired = visit.State == AsylumVisitRecord.Expired;
                    if (visit.State == AsylumVisitRecord.Refused) ApplyAsylumRelation(visit, -10);
                    FinishAsylumVisit(pair.Key, visit, leaveSettlement: true);
                    if (_pendingAsylumClanIds.Contains(pair.Key)) DenyPlayerAsylum(pair.Key, silent: true);
                    if (expired)
                        BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_Asylum_VisitExpired}Having received no answer to their petition, the {CLAN} have departed to seek protection elsewhere.")
                            .SetTextVariable("CLAN", visit.Clan.Name), BellumNotificationColors.Warning);
                }
                catch (Exception ex) { BellumCivileLogger.Log($"Asylum audience recovery deferred; clan={pair.Key}; error={ex}"); }
            }
        }

        private static void ReleaseAsylumParty(AsylumVisitRecord visit)
        {
            if (!visit.PartyHeld || visit.Party?.Ai == null) return;
            // Do not clear a newer AI hold installed by the game or another mod.
            var hold = AccessTools.Property(typeof(MobilePartyAi), "_enableAgainAtHour");
            if (visit.Party.Ai.IsDisabled && hold?.GetValue(visit.Party.Ai) is CampaignTime until
                && until.ToHours == visit.PartyHoldUntil.ToHours)
            {
                visit.Party.Ai.EnableAi();
                visit.Party.Ai.RethinkAtNextHourlyTick = true;
            }
            visit.PartyHeld = false;
        }

        private void FinishAsylumVisit(string id, AsylumVisitRecord visit, bool leaveSettlement)
        {
            ReleaseAsylumParty(visit);
            if (leaveSettlement && visit.Guest?.IsAlive == true && !visit.Guest.IsPrisoner
                && visit.Guest.CurrentSettlement == visit.Settlement && visit.Settlement?.IsUnderSiege != true)
            {
                if (visit.Party != null && visit.Party.CurrentSettlement == visit.Settlement
                    && visit.Party.MapEvent == null && visit.Party.SiegeEvent == null && visit.Party.LeaderHero == visit.Guest
                    && visit.Guest.PartyBelongedTo == visit.Party && !visit.Party.IsCurrentlyUsedByAQuest)
                    LeaveSettlementAction.ApplyForParty(visit.Party);
                else if (visit.Guest.PartyBelongedTo == null)
                    LeaveSettlementAction.ApplyForCharacterOnly(visit.Guest);
            }
            _asylumVisits.Remove(id);
        }

        private static void ApplyAsylumRelation(AsylumVisitRecord visit, int change)
        {
            if (visit.RelationApplied) return;
            visit.RelationApplied = true;
            RelationMemoryService.ApplyChange(visit.Ruler, visit.Guest, change, true,
                change > 0 ? "asylum_granted" : "asylum_refused", change > 0 ? 5f : 2f,
                RelationMemoryScope.House, visit.Clan.Name.ToString());
        }

        private bool CompleteAsylumAdmission(AsylumVisitRecord visit)
        {
            if (visit?.State != AsylumVisitRecord.Accepted || !AsylumAuthorityValid(visit) || !AsylumGuestValid(visit)) return false;
            if (visit.Clan.Kingdom != visit.Realm)
            {
                if (!IsRecoverableExileCandidate(visit.Clan) || visit.Clan.IsAtWarWith(visit.Realm)
                    || !IsAsylumHost(visit.Settlement, visit.Realm) || visit.Guest.CurrentSettlement != visit.Settlement
                    || visit.Guest.PartyBelongedTo != visit.Party) return false;
                ChangeKingdomAction.ApplyByJoinToKingdom(visit.Clan, visit.Realm);
                if (visit.Clan.Kingdom != visit.Realm) return false;
            }
            RecordPlayerInvitation(visit.Clan, visit.Realm);
            RemoveTrackedExile(visit.Clan.StringId);
            ApplyAsylumRelation(visit, 10);
            if (!visit.GrantSettled)
            {
                var registry = LordshipBehavior.Instance;
                var existing = registry?.GetGrant(visit.Guest);
                if (visit.PromisedVillage != null)
                    visit.GrantAwarded = existing?.Village == visit.PromisedVillage && existing.GrantingClan == visit.Ruler.Clan
                        || registry?.TryGrantVassal(visit.Ruler.Clan, visit.PromisedVillage, visit.Guest, visit.Ruler, out _) == true;
                visit.GrantSettled = true;
                if (visit.PromisedVillage != null && !visit.GrantAwarded)
                {
                    BellumCivileLogger.Log($"Asylum admitted without promised estate; clan={visit.Clan.StringId}; village={visit.PromisedVillage.StringId}.");
                    BellumCivileNotifications.ShowPersonal(new TextObject("{=BC_Asylum_GrantChanged}The {CLAN} have sworn homage, but the promised estate is no longer available. You may endow their house with another vacant village.")
                        .SetTextVariable("CLAN", visit.Clan.Name), BellumNotificationColors.Warning);
                }
            }
            return true;
        }
    }
}
