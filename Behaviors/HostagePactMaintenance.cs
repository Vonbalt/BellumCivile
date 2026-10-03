using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class HostagePactBehavior
    {
        private bool _maintaining;

        private void MaintainCustody() => MaintainCustody(false);
        private void MaintainDailyCustody() => MaintainCustody(true);

        private void MaintainCustody(bool dailyTick)
        {
            if (_maintaining) return;
            _maintaining = true;
            try
            {
                foreach (var pact in _pacts.Where(p => p != null && p.Phase != HostagePactPhase.Ended).ToList())
                {
                    try { Maintain(pact, dailyTick); }
                    catch (Exception ex) { BellumCivileLogger.Log($"Hostage custody maintenance deferred; pact={pact.Id}; {ex}"); }
                }
            }
            finally { _maintaining = false; }
        }

        private void Maintain(HostagePactRecord pact, bool dailyTick)
        {
            // An interrupted handover is rolled back, never resumed by taking a now-unavailable hero.
            if (pact.Phase == HostagePactPhase.Preparing)
            {
                // A treaty may already have transferred wealth or land. Its delivery receipt
                // must reconcile that boundary; ordinary failed-handover rollback is unsafe.
                if (pact.TreatySettlementStarted) return;
                pact.TryAbortPreparation();
            }
            if (pact.Phase == HostagePactPhase.Active)
            {
                bool lost = pact.FirstRealm == null || pact.SecondRealm == null
                    || pact.FirstRealm.IsEliminated || pact.SecondRealm.IsEliminated;
                var reason = HostagePactLifecycleRules.Evaluate(
                    CivilWarConflictBehavior.IsRealmTransferPending(pact.FirstRealm)
                        || CivilWarConflictBehavior.IsRealmTransferPending(pact.SecondRealm), lost,
                    pact.FirstRealm?.RulingClan != pact.FirstHouse || pact.SecondRealm?.RulingClan != pact.SecondHouse,
                    Dead(pact.FirstHostage) || Dead(pact.SecondHostage),
                    !lost && pact.FirstRealm.IsAtWarWith(pact.SecondRealm),
                    BellumCivileOptions.EnableWarPeaceLogicRevamp, pact.CanExpire(CampaignTime.Now.ToDays, dailyTick));
                if (reason != HostagePactEndReason.None) pact.TryBeginResolution(reason);
                else
                {
                    // Do not inspect transient ownership while a civil-war transfer is in flight.
                    if (CivilWarConflictBehavior.IsRealmTransferPending(pact.FirstRealm)
                        || CivilWarConflictBehavior.IsRealmTransferPending(pact.SecondRealm)) return;
                    MaintainHolding(pact, pact.FirstHostage);
                    if (pact.Phase == HostagePactPhase.Active) MaintainHolding(pact, pact.SecondHostage);
                }
            }
            if (pact.Phase != HostagePactPhase.Resolving) return;
            if ((pact.EndReason == HostagePactEndReason.EarlyRelease || pact.EndReason == HostagePactEndReason.Betrayal)
                && !CanResolveHostageJudgment()) return;
            if (pact.EndReason == HostagePactEndReason.Betrayal && !ResolveHostageBetrayal(pact)) return;
            if (pact.EndReason == HostagePactEndReason.War)
            {
                ResolveWarHostage(pact, pact.FirstHostage);
                ResolveWarHostage(pact, pact.SecondHostage);
            }
            Release(pact, pact.FirstHostage);
            Release(pact, pact.SecondHostage);
            ApplyEarlyReleaseRewards(pact);
            ApplyPactMemories(pact);
            if (pact.TryComplete() && pact.EndDay <= 0) _pacts.Remove(pact);
        }

        private static bool Dead(TreatyHostageRecord record) => record != null
            && (record.Hero == null || record.Hero.IsDead);

        internal static Settlement SelectHolding(Clan house, Func<Settlement, bool> eligible = null)
        {
            if (house == null || house.IsEliminated) return null;
            var titles = CourtTitleGrantObjectiveSource.Titles;
            var realm = house.Kingdom;
            var crown = realm == null ? null : titles?.GetRealmSovereignTitle(realm, FeudalHierarchyMode.DeFacto)
                ?? titles?.GetKingdomPoliticalTitle(realm);
            return house.Fiefs.Where(t => t.Settlement.OwnerClan == house && !t.Settlement.IsUnderSiege)
                .Where(t => eligible == null || eligible(t.Settlement))
                .OrderByDescending(t => t.Settlement.StringId == crown?.CapitalSettlementId)
                .ThenByDescending(t => t.Prosperity).ThenBy(t => t.Settlement.StringId, StringComparer.Ordinal)
                .Select(t => t.Settlement).FirstOrDefault();
        }

        private static void MaintainHolding(HostagePactRecord pact, TreatyHostageRecord record)
        {
            if (record == null) return;
            var hero = record.Hero;
            if (!record.CustodyEstablished || hero == null || !hero.IsPrisoner
                || record.Holding == null || hero.PartyBelongedToAsPrisoner != record.Holding.Party
                || !SameCaptivity(record))
            {
                pact.TryBeginResolution(HostagePactEndReason.InvalidCustody);
                return;
            }
            if (record.Holding.OwnerClan == record.ReceivingHouse) return;
            var next = SelectHolding(record.ReceivingHouse);
            if (next == null)
            {
                pact.TryBeginResolution(HostagePactEndReason.InvalidCustody);
                return;
            }
            using (HostageCustodyGuard.Authorize(hero))
                TransferPrisonerAction.Apply(hero.CharacterObject, hero.PartyBelongedToAsPrisoner, next.Party);
            if (hero.PartyBelongedToAsPrisoner == next.Party) record.Holding = next;
        }

        private void OnHoldingOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner,
            Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege)
                foreach (var pact in _pacts.Where(p => p?.Phase == HostagePactPhase.Active).ToList())
                    if (pact.FirstHostage?.Holding == settlement || pact.SecondHostage?.Holding == settlement)
                        pact.TryBeginResolution(HostagePactEndReason.Rescued);
            MaintainCustody();
        }

        private static void Release(HostagePactRecord pact, TreatyHostageRecord record)
        {
            if (record == null || record.ActionCompleted || record.Outcome != HostageCustodyOutcome.Release) return;
            var hero = record.Hero;
            record.ActionStarted = true;
            bool returned = false;
            if (hero != null && !hero.IsDead && record.CustodyEstablished)
            {
                // Never release an unrelated later captivity if another system already moved the hero.
                if (hero.IsPrisoner && SameCaptivity(record) && record.Holding != null && hero.PartyBelongedToAsPrisoner == record.Holding.Party)
                {
                    using (HostageCustodyGuard.Authorize(hero)) EndCaptivityAction.ApplyByReleasedByChoice(hero);
                    record.ReleaseSucceeded = !hero.IsPrisoner && hero.IsAlive;
                }
                if (hero.IsPrisoner && SameCaptivity(record) && hero.PartyBelongedToAsPrisoner == record.Holding?.Party) return;
                if (!hero.IsPrisoner)
                {
                    var home = SelectHolding(hero.Clan);
                    if (home != null && hero != Hero.MainHero && hero.PartyBelongedTo == null)
                    {
                        TeleportHeroAction.ApplyImmediateTeleportToSettlement(hero, home);
                        if (hero.IsChild && record.PreviousHeroState == (int)Hero.CharacterStates.NotSpawned
                            && hero.Clan?.Leader != hero) hero.ChangeState(Hero.CharacterStates.NotSpawned);
                    }
                    returned = true;
                }
            }
            record.ActionCompleted = true;
            if (record.Reported) return;
            record.Reported = true;
            if (returned && pact.EndDay > 0)
            {
                var text = new TextObject(pact.EndReason == HostagePactEndReason.Betrayal
                    ? "{=BC_Hostage_BetrayalReleased}Despite the betrayal that ended the pledge with {REALM}, {HERO} has been spared and returned to their family."
                    : pact.EndReason == HostagePactEndReason.EarlyRelease
                    ? "{=BC_Hostage_EarlyReturned}The pledge with {REALM} has been ended honorably before its appointed day. {HERO} has been returned to their family."
                    : pact.EndReason == HostagePactEndReason.War
                    ? "{=BC_Hostage_WarReleased}Though war has broken the peace with {REALM}, {HERO} has been spared and released from custody, free to return to their family."
                    : pact.EndReason == HostagePactEndReason.Expired
                    ? "{=BC_Hostage_ReturnExpired}The pledged peace with {REALM} has run its course. {HERO} has been released from treaty custody, free to return to their family."
                    : "{=BC_Hostage_ReturnEnded}The pledge of peace with {REALM} no longer binds the two houses. {HERO} has been released from treaty custody, free to return to their family.");
                HostagePactText.Fill(text, pact, hero);
                InformationManager.DisplayMessage(new InformationMessage(text.ToString()));
            }
            BellumCivileLogger.Log($"Hostage peaceful resolution; pact={pact.Id}; hero={hero?.StringId}; reason={pact.EndReason}; released={returned}.");
        }

        private static bool SameCaptivity(TreatyHostageRecord record)
            => record.Hero != null && Math.Abs(record.Hero.CaptivityStartTime.ToDays - record.CaptivityStartDay) < 0.000001;
    }
}
