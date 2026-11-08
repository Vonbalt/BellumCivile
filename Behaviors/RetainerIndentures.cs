using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class LordshipBehavior
    {
        private const int IndentureRenewalDays = 7;
        private List<RetainerIndentureRecord> _indentures = new List<RetainerIndentureRecord>();
        private readonly Dictionary<Hero, RetainerIndentureRecord> _indenturesByHero = new Dictionary<Hero, RetainerIndentureRecord>();
        private bool _indentureInquiryOpen;
        private bool _indentureRenewalPending;

        internal RetainerIndentureRecord GetIndenture(Hero hero) => hero != null
            && _indenturesByHero.TryGetValue(hero, out var contract) && contract.ActiveAt(CampaignTime.Now.ToDays) ? contract : null;

        internal bool HasServiceSupport(Hero hero) => GetGrant(hero) != null || GetIndenture(hero) != null;
        internal static int IndentureFee(int years) => years == 1 ? 5000 : years == 2 ? 8000 : years == 3 ? 13000 : 0;
        internal static int IndentureDaysLeft(RetainerIndentureRecord contract) =>
            (int)Math.Min(int.MaxValue, Math.Ceiling(Math.Max(0, contract.EndDay - CampaignTime.Now.ToDays)));

        private void ReindexIndentures()
        {
            _indenturesByHero.Clear();
            foreach (var contract in _indentures)
                if (contract?.Holder != null && !_indenturesByHero.ContainsKey(contract.Holder))
                    _indenturesByHero.Add(contract.Holder, contract);
        }

        private bool CanDiscussIndenture(Hero hero) => Ready && Clan.PlayerClan?.Leader == Hero.MainHero
            && hero?.Clan == Clan.PlayerClan && HouseholdMembership.IsRetainer(hero) && !hero.IsChild
            && !hero.IsDisabled && !hero.IsPrisoner && !hero.IsNotable && !hero.IsSpecial && !hero.IsTemplate && hero.IsAlive;

        internal bool CanSignIndenture(Hero hero, int years, out TextObject reason)
        {
            reason = null;
            int fee = IndentureFee(years);
            var existing = GetIndenture(hero);
            if (!CanDiscussIndenture(hero) || fee == 0 || CampaignTime.DaysInYear <= 0)
                reason = Text("BC_Indenture_Unavailable", "This indenture can no longer be agreed. We must speak of it another time.");
            else if (existing != null && existing.EndDay - CampaignTime.Now.ToDays > IndentureRenewalDays)
                reason = Text("BC_Indenture_TooEarly", "This indenture may be renewed when seven days or fewer remain.");
            else if (Hero.MainHero.Gold < fee)
                reason = Text("BC_Indenture_NotEnoughGold", "You need {GOLD} denars to agree to these terms.").SetTextVariable("GOLD", fee);
            else if (hero.Gold > int.MaxValue - fee)
                reason = Text("BC_Indenture_Unavailable", "This indenture can no longer be agreed. We must speak of it another time.");
            return reason == null;
        }

        internal bool TrySignIndenture(Hero hero, int years, out TextObject reason, double expectedEnd = -1)
        {
            if (!CanSignIndenture(hero, years, out reason)) return false;
            var existing = GetIndenture(hero);
            if (expectedEnd >= 0 && expectedEnd != (existing?.EndDay ?? 0))
            {
                reason = Text("BC_Indenture_Unavailable", "This indenture can no longer be agreed. We must speak of it another time.");
                return false;
            }
            double end = (existing?.EndDay ?? CampaignTime.Now.ToDays) + years * (double)CampaignTime.DaysInYear;
            GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, hero, IndentureFee(years));
            if (existing == null)
            {
                if (_indenturesByHero.TryGetValue(hero, out var old)) _indentures.Remove(old);
                existing = new RetainerIndentureRecord { Holder = hero, Employer = hero.Clan };
                _indentures.Add(existing);
                _indenturesByHero[hero] = existing;
            }
            existing.EndDay = end;
            existing.RenewalNotified = false;
            _appointments.RemoveAll(a => a?.Hero == hero && a.OwningClan == hero.Clan);
            Changed();
            BellumCivileLogger.Log($"Agreed retainer indenture; hero={hero.StringId}; clan={hero.Clan.StringId}; years={years}; fee={IndentureFee(years)}; endDay={end}.");
            return true;
        }

        private void MaintainIndentures()
        {
            _indentureRenewalPending = false;
            var seen = new HashSet<Hero>();
            foreach (var contract in _indentures.ToList())
            {
                if (contract?.Holder != null && contract.ActiveAt(CampaignTime.Now.ToDays) && seen.Add(contract.Holder))
                {
                    _indenturesByHero[contract.Holder] = contract;
                    if (!contract.RenewalNotified && contract.EndDay - CampaignTime.Now.ToDays <= IndentureRenewalDays)
                        _indentureRenewalPending = true;
                    continue;
                }
                _indentures.Remove(contract);
                if (contract?.Holder == null) continue;
                if (_indenturesByHero.TryGetValue(contract.Holder, out var indexed) && indexed == contract)
                    _indenturesByHero.Remove(contract.Holder);
                if (seen.Contains(contract.Holder)) continue;
                Changed();
                // Expiry removes authority, not the retainer. Existing appointment retirement supplies the safe handover.
                if (contract.HasEmployer && contract.Employer == Clan.PlayerClan)
                {
                    CheckAppointment(contract.Holder);
                    InformationManager.DisplayMessage(new InformationMessage(Text("BC_Indenture_Expired", "{HERO}'s indenture has expired.")
                        .SetTextVariable("HERO", contract.Holder.Name).ToString()));
                }
            }
        }

        private bool ShowIndentureRenewalInquiry()
        {
            if (_indentureInquiryOpen) return true;
            if (!_indentureRenewalPending) return false;
            var contract = _indentures.FirstOrDefault(c => c?.RenewalNotified == false && GetIndenture(c.Holder) == c
                && CanDiscussIndenture(c.Holder) && c.EndDay - CampaignTime.Now.ToDays <= IndentureRenewalDays);
            if (contract == null) { _indentureRenewalPending = false; return false; }
            contract.RenewalNotified = true;
            _indentureInquiryOpen = true;
            InformationManager.ShowInquiry(new InquiryData(Text("BC_Indenture_RenewalTitle", "Indenture Nearing Its End").ToString(),
                Text("BC_Indenture_RenewalBody", "Your indenture with {HERO} ends in {DAYS} {?DAYS > 1}days{?}day{\\?}. Will you retain their services for another term?")
                    .SetTextVariable("HERO", contract.Holder.Name).SetTextVariable("DAYS", IndentureDaysLeft(contract)).ToString(),
                true, true, Text("BC_Indenture_Renew", "Renew Indenture").ToString(), Text("BC_Indenture_LetExpire", "Let It Expire").ToString(),
                () => { _indentureInquiryOpen = false; ShowIndentureRenewalTerms(contract); },
                () => _indentureInquiryOpen = false), true);
            return true;
        }

        private bool CanShowIndentureRenewal() => Ready && (_indentureRenewalPending || _indentureInquiryOpen)
            && !_replacementInquiryOpen && !InformationManager.IsAnyInquiryActive()
            && Hero.MainHero != null && !Hero.MainHero.IsPrisoner && CampaignMission.Current == null
            && Game.Current?.GameStateManager?.ActiveState is MapState && MobileParty.MainParty?.MapEvent == null
            && (PlayerEncounter.Current == null || MobileParty.MainParty?.CurrentSettlement != null)
            && Campaign.Current.ConversationManager?.IsConversationInProgress != true;

        private void ShowIndentureRenewalTerms(RetainerIndentureRecord contract)
        {
            if (!Ready || GetIndenture(contract.Holder) != contract) return;
            double expectedEnd = contract.EndDay;
            var choices = Enumerable.Range(1, 3).Select(years => new InquiryElement(years,
                Text("BC_Indenture_TermCost", "{TERM} - {GOLD} denars").SetTextVariable("TERM", IndentureTerm(years))
                    .SetTextVariable("GOLD", IndentureFee(years)).ToString(), null,
                CanSignIndenture(contract.Holder, years, out var reason), reason?.ToString() ?? string.Empty)).ToList();
            _indentureInquiryOpen = true;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                Text("BC_Indenture_Renew", "Renew Indenture").ToString(),
                Text("BC_Indenture_RenewalTerms", "{HERO} offers to serve for another term, with the fee payable in advance.")
                    .SetTextVariable("HERO", contract.Holder.Name).ToString(), choices, true, 1, 1,
                Text("BC_Indenture_Agree", "Agree").ToString(), Text("BC_UI_Cancel", "Cancel").ToString(), selected =>
                {
                    _indentureInquiryOpen = false;
                    if (!(selected?.FirstOrDefault()?.Identifier is int years)) return;
                    if (!TrySignIndenture(contract.Holder, years, out var reason, expectedEnd))
                        InformationManager.DisplayMessage(new InformationMessage(reason.ToString(), Colors.Red));
                    else InformationManager.DisplayMessage(new InformationMessage(Text("BC_Indenture_Renewed", "Your indenture with {HERO} has been renewed.")
                        .SetTextVariable("HERO", contract.Holder.Name).ToString()));
                }, _ => _indentureInquiryOpen = false), true);
        }

        private static TextObject IndentureTerm(int years) => years == 1 ? Text("BC_Indenture_TermOne", "One year")
            : years == 2 ? Text("BC_Indenture_TermTwo", "Two years") : Text("BC_Indenture_TermThree", "Three years");
    }
}
