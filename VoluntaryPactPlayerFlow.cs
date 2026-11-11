using System;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class VoluntaryPactPlayerFlow
    {
        internal static void Open(VoluntaryPactProposalBehavior owner, Kingdom source, Kingdom target, Action<KingdomDecision> resolve)
        {
            TextObject reason = null;
            if (owner == null || !owner.CanPropose(source, target, Clan.PlayerClan, out reason))
            { ShowFailure(reason); return; }
            var candidates = TreatyHostageEligibility.GetCandidates(source)
                .Where(c => c.Hero != null && c.Hero != Hero.MainHero).ToList();
            if (source.RulingClan != Clan.PlayerClan)
            {
                var candidate = candidates.OrderByDescending(c => c.Tier)
                    .ThenBy(c => c.Hero.StringId, StringComparer.Ordinal).FirstOrDefault();
                if (candidate == null) { ShowFailure(null); return; }
                int duration = BellumCivileOptions.HostagePactMinimumDurationDays;
                int payment = 0;
                var trial = new VoluntaryPactProposalRecord { Id = "player-vassal-preview", Source = source, Target = target,
                    SourceHouse = source.RulingClan, TargetHouse = target.RulingClan, Sponsor = Clan.PlayerClan,
                    SourceHostage = candidate.Hero, DurationDays = duration, BaselineDays = duration,
                    CreatedDay = CampaignTime.Now.ToDays, Phase = VoluntaryPactProposalPhase.SourceVote };
                try { VoluntaryPactAiBehavior.TryChoosePayment(trial, out payment, out _); }
                catch (Exception ex) { BellumCivileLogger.Log($"Voluntary pact vassal preview failed; source={source.StringId}; error={ex}"); }
                Confirm(owner, source, target, candidate.Hero, duration, payment, resolve);
                return;
            }
            var options = candidates.Select(c => new InquiryElement(c.Hero, c.Hero.Name.ToString(), null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BC_VoluntaryPact_OfferHostageTitle}Offer a Hostage").ToString(),
                new TextObject("{=BC_VoluntaryPact_OfferHostageBody}Choose a relative of your ruling house to offer as a pledge of peace.").ToString(),
                options, true, 1, 1, new TextObject("{=BC_VoluntaryPact_Next}Next").ToString(),
                new TextObject("{=BC_VoluntaryPact_Cancel}Cancel").ToString(), selected =>
                {
                    if (selected?.Count == 1 && selected[0].Identifier is Hero hostage)
                        AskDuration(owner, source, target, hostage, resolve);
                }, null), true);
        }

        private static void AskDuration(VoluntaryPactProposalBehavior owner, Kingdom source, Kingdom target,
            Hero hostage, Action<KingdomDecision> resolve)
        {
            int baseline = BellumCivileOptions.HostagePactMinimumDurationDays;
            var instructions = new TextObject("{=BC_VoluntaryPact_DurationHelp}Enter a duration from {MINIMUM} to 1000 days, in steps of 10.")
                .SetTextVariable("MINIMUM", baseline).ToString();
            InformationManager.ShowTextInquiry(new TextInquiryData(
                new TextObject("{=BC_VoluntaryPact_DurationTitle}Pledge Duration").ToString(), instructions, true, true,
                new TextObject("{=BC_VoluntaryPact_Next}Next").ToString(), new TextObject("{=BC_VoluntaryPact_Cancel}Cancel").ToString(),
                input => { if (int.TryParse(input, out int days) && HostagePactRules.IsNegotiableDuration(days, baseline))
                    AskPayment(owner, source, target, hostage, days, resolve); }, null, false,
                input => Tuple.Create(int.TryParse(input, out int days) && HostagePactRules.IsNegotiableDuration(days, baseline), instructions),
                "", baseline.ToString()));
        }

        private static void AskPayment(VoluntaryPactProposalBehavior owner, Kingdom source, Kingdom target,
            Hero hostage, int days, Action<KingdomDecision> resolve)
        {
            string instructions = new TextObject("{=BC_VoluntaryPact_PaymentHelp}Enter the denars your house will offer. Enter 0 to offer no payment.").ToString();
            bool Valid(string input) => int.TryParse(input, out int amount) && amount >= 0
                && source?.Leader != null && target?.Leader != null && amount <= source.Leader.Gold
                && target.Leader.Gold <= int.MaxValue - amount;
            InformationManager.ShowTextInquiry(new TextInquiryData(
                new TextObject("{=BC_VoluntaryPact_PaymentTitle}Offer Compensation").ToString(), instructions, true, true,
                new TextObject("{=BC_VoluntaryPact_Next}Next").ToString(), new TextObject("{=BC_VoluntaryPact_Cancel}Cancel").ToString(),
                input => { if (Valid(input) && int.TryParse(input, out int amount)) Confirm(owner, source, target, hostage, days, amount, resolve); },
                null, false, input => Tuple.Create(Valid(input), instructions), "", "0"));
        }

        private static void Confirm(VoluntaryPactProposalBehavior owner, Kingdom source, Kingdom target,
            Hero hostage, int days, int payment, Action<KingdomDecision> resolve)
        {
            var body = new TextObject("{=BC_VoluntaryPact_ConfirmBody}Offer {HOSTAGE} to {TARGET} for a pledge of peace lasting {DAYS} days, with {GOLD} denars in compensation. Filing this proposal costs 100 influence; the other ruler will choose a hostage in return.")
                .SetTextVariable("HOSTAGE", hostage.Name).SetTextVariable("TARGET", target.Name)
                .SetTextVariable("DAYS", days).SetTextVariable("GOLD", payment);
            InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_VoluntaryPact_ConfirmTitle}Propose a Pledge").ToString(),
                body.ToString(), true, true, new TextObject("{=BC_VoluntaryPact_Submit}Propose").ToString(),
                new TextObject("{=BC_VoluntaryPact_Cancel}Cancel").ToString(), () =>
                {
                    if (!owner.CanPropose(source, target, Clan.PlayerClan, out var failure)) { ShowFailure(failure); return; }
                    if (!owner.TryCreateAndFile(source, target, Clan.PlayerClan, hostage, days, payment, out string id))
                    { ShowFailure(null); return; }
                    var decision = owner.Find(id)?.SourceDecision;
                    if (decision != null && decision.Kingdom?.UnresolvedDecisions?.Contains(decision) == true) resolve?.Invoke(decision);
                }, null));
        }

        private static void ShowFailure(TextObject reason)
        {
            InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_VoluntaryPact_UnavailableTitle}Pledge Unavailable").ToString(),
                (reason ?? new TextObject("{=BC_VoluntaryPact_Unavailable}A pledge cannot be proposed under the current circumstances.")).ToString(),
                true, false, new TextObject("{=BC_VoluntaryPact_Ok}Done").ToString(), null, null, null));
        }
    }
}
