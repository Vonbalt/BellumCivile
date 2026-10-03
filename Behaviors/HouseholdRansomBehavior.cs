using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Lets the player commission a ransom broker to recover imprisoned members of
    /// the player clan instead of waiting for the captor's random ransom offer.
    /// </summary>
    public sealed class HouseholdRansomBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddPlayerLine(
                "bc_household_ransom_start",
                "ransom_broker_talk",
                "bc_household_ransom_response",
                "{=BC_HouseholdRansom_Request}I wish to arrange the ransom of a member of my household.",
                IsSpeakingToRansomBroker,
                null,
                120);

            starter.AddDialogLine(
                "bc_household_ransom_available",
                "bc_household_ransom_response",
                "close_window",
                "{=BC_HouseholdRansom_BrokerReviews}Tell me whom you seek to free, my {?PLAYER.GENDER}lady{?}lord{\\?}, and I shall see what can be arranged.",
                HasEligibleHouseholdPrisoner,
                OpenHouseholdRansomInquiry,
                120);

            starter.AddDialogLine(
                "bc_household_ransom_none",
                "bc_household_ransom_response",
                "ransom_broker_talk",
                "{=BC_HouseholdRansom_None}None of your household are presently held for ransom, my {?PLAYER.GENDER}lady{?}lord{\\?}.",
                null,
                null,
                100);
        }

        private static bool IsSpeakingToRansomBroker()
        {
            return CharacterObject.OneToOneConversationCharacter?.Occupation == Occupation.RansomBroker
                && Clan.PlayerClan != null;
        }

        private static bool HasEligibleHouseholdPrisoner()
        {
            return GetEligibleHouseholdPrisoners().Count > 0;
        }

        private static void OpenHouseholdRansomInquiry()
        {
            List<HouseholdRansomCandidate> candidates = BuildRansomCandidates();
            if (candidates.Count == 0)
            {
                BellumCivileNotifications.ShowPersonal(
                    new TextObject("{=BC_HouseholdRansom_NoLongerAvailable}No member of your household is presently available for ransom."),
                    BellumNotificationColors.Warning);
                return;
            }

            int availableGold = Hero.MainHero?.Gold ?? 0;
            List<InquiryElement> elements = candidates
                .Select(candidate => new InquiryElement(
                    candidate,
                    BuildCandidateLabel(candidate),
                    new CharacterImageIdentifier(CharacterCode.CreateFrom(candidate.Prisoner.CharacterObject)),
                    candidate.IsTreatyHostage || availableGold >= candidate.RansomPrice,
                    BuildCandidateHint(candidate, availableGold)))
                .ToList();

            MultiSelectionInquiryData inquiry = new MultiSelectionInquiryData(
                new TextObject("{=BC_HouseholdRansom_Title}Ransom a Household Member").ToString(),
                new TextObject("{=BC_HouseholdRansom_Description}Select the member of your household whose release the broker should arrange.").ToString(),
                elements,
                true,
                1,
                1,
                new TextObject("{=BC_HouseholdRansom_Confirm}Confirm").ToString(),
                new TextObject("{=BC_HouseholdRansom_Cancel}Cancel").ToString(),
                OnHouseholdRansomConfirmed,
                null);

            MBInformationManager.ShowMultiSelectionInquiry(inquiry, true);
        }

        private static void OnHouseholdRansomConfirmed(List<InquiryElement> selected)
        {
            HouseholdRansomCandidate quote = selected?.FirstOrDefault()?.Identifier as HouseholdRansomCandidate;
            if (quote == null)
                return;

            Hero prisoner = quote.Prisoner;
            if (prisoner?.IsAlive == true && prisoner.IsPrisoner && prisoner.Clan == Clan.PlayerClan)
            {
                var pact = HostagePactText.Find(prisoner);
                if (pact != null)
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        new TextObject("{=BC_Hostage_BrokerTitle}Ransom Broker").ToString(),
                        HostagePactText.BrokerRefusal(pact, prisoner).ToString(), true, false,
                        new TextObject("{=BC_Hostage_BrokerUnderstood}I understand.").ToString(), null,
                        OpenHouseholdRansomInquiry, null), true);
                    return;
                }
            }
            // A protected entry is not a zero-price quote if the pact ended while the
            // selection was open. Rebuild it using ordinary ransom valuation instead.
            if (quote.IsTreatyHostage)
            {
                OpenHouseholdRansomInquiry();
                return;
            }
            PartyBase currentCaptorParty = prisoner?.PartyBelongedToAsPrisoner;
            Clan currentCaptorClan = ResolveCaptorClan(prisoner);
            if (prisoner == null
                || !prisoner.IsAlive
                || !prisoner.IsPrisoner
                || HostageCustodyGuard.IsProtected(prisoner)
                || prisoner.Clan != Clan.PlayerClan
                || currentCaptorParty == null
                || currentCaptorClan == null
                || currentCaptorClan.Leader == null
                || currentCaptorClan != quote.CaptorClan)
            {
                BellumCivileNotifications.ShowPersonal(
                    new TextObject("{=BC_HouseholdRansom_ChangedCustody}The captive's circumstances have changed, and the broker can no longer honor that ransom offer."),
                    BellumNotificationColors.Warning);
                return;
            }

            if ((Hero.MainHero?.Gold ?? 0) < quote.RansomPrice)
            {
                TextObject insufficient = new TextObject("{=BC_HouseholdRansom_InsufficientGold}You do not have the {COST} denars required to pay this ransom.");
                insufficient.SetTextVariable("COST", quote.RansomPrice.ToString("N0"));
                BellumCivileNotifications.ShowPersonal(insufficient, BellumNotificationColors.Warning);
                return;
            }

            GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, currentCaptorClan.Leader, quote.RansomPrice);
            EndCaptivityAction.ApplyByRansom(prisoner, Hero.MainHero);

            TextObject released = new TextObject("{=BC_HouseholdRansom_Released}The ransom has been paid. {HERO_NAME} is free.");
            released.SetTextVariable("HERO_NAME", prisoner.Name);
            BellumCivileNotifications.ShowPersonal(released, BellumNotificationColors.Success);
            BellumCivileLogger.Log(
                $"Player household ransom completed; prisoner={prisoner.StringId}; captor={currentCaptorClan.StringId}; price={quote.RansomPrice}.");
        }

        private static List<HouseholdRansomCandidate> BuildRansomCandidates()
        {
            Clan playerClan = Clan.PlayerClan;
            if (playerClan == null || Hero.MainHero == null)
                return new List<HouseholdRansomCandidate>();

            List<HouseholdRansomCandidate> candidates = new List<HouseholdRansomCandidate>();
            foreach (Hero prisoner in GetEligibleHouseholdPrisoners()
                .OrderBy(hero => hero.IsPlayerCompanion ? 1 : 0)
                .ThenBy(hero => hero.Name?.ToString()))
            {
                PartyBase captorParty = prisoner.PartyBelongedToAsPrisoner;
                Clan captorClan = ResolveCaptorClan(prisoner);
                bool hostage = HostageCustodyGuard.IsProtected(prisoner);
                int price = hostage ? 0 : CalculateRansomPrice(prisoner, captorClan, captorParty);
                if (!hostage && price <= 0)
                    continue;

                candidates.Add(new HouseholdRansomCandidate(
                    prisoner,
                    captorClan,
                    price,
                    ResolveHoldingName(captorParty), hostage));
            }

            return candidates;
        }

        private static List<Hero> GetEligibleHouseholdPrisoners()
        {
            Clan playerClan = Clan.PlayerClan;
            if (playerClan == null || Hero.MainHero == null)
                return new List<Hero>();

            return playerClan.Heroes
                .Where(hero => hero != null
                    && hero != Hero.MainHero
                    && hero.IsAlive
                    && hero.IsPrisoner
                    && hero.PartyBelongedToAsPrisoner != null)
                .Where(hero =>
                {
                    Clan captorClan = ResolveCaptorClan(hero);
                    return captorClan != null
                        && captorClan != playerClan
                        && captorClan.Leader != null;
                })
                .ToList();
        }

        private static int CalculateRansomPrice(Hero prisoner, Clan captorClan, PartyBase captorParty)
        {
            SetPrisonerFreeBarterable ransom = new SetPrisonerFreeBarterable(
                prisoner,
                captorClan.Leader,
                captorParty,
                Hero.MainHero);
            return Math.Max(1, (int)(ransom.GetUnitValueForFaction(prisoner.Clan) * 1.1f));
        }

        private static string BuildCandidateLabel(HouseholdRansomCandidate candidate)
        {
            if (candidate.IsTreatyHostage)
                return new TextObject("{=BC_Hostage_BrokerCandidate}{HERO_NAME} - Treaty hostage")
                    .SetTextVariable("HERO_NAME", candidate.Prisoner.Name).ToString();
            TextObject text = new TextObject("{=BC_HouseholdRansom_Candidate}{HERO_NAME} - {COST} denars");
            text.SetTextVariable("HERO_NAME", candidate.Prisoner.Name);
            text.SetTextVariable("COST", candidate.RansomPrice.ToString("N0"));
            return text.ToString();
        }

        private static string BuildCandidateHint(HouseholdRansomCandidate candidate, int availableGold)
        {
            var pact = HostagePactText.Find(candidate.Prisoner);
            if (pact != null) return HostagePactText.Status(pact, candidate.Prisoner).ToString();
            TextObject text = new TextObject("{=BC_HouseholdRansom_CandidateHint}Captor: {CAPTOR_CLAN}{newline}Held by: {HOLDING}{newline}Ransom demanded: {COST} denars.{AFFORDABILITY}");
            text.SetTextVariable("CAPTOR_CLAN", candidate.CaptorClan.Name);
            text.SetTextVariable("HOLDING", new TextObject("{=!}" + candidate.HoldingName));
            text.SetTextVariable("COST", candidate.RansomPrice.ToString("N0"));
            text.SetTextVariable("newline", "\n");
            text.SetTextVariable("AFFORDABILITY", availableGold >= candidate.RansomPrice
                ? string.Empty
                : new TextObject("{=BC_HouseholdRansom_CannotAfford}{newline}You cannot afford this ransom.").ToString());
            return text.ToString();
        }

        private static string ResolveHoldingName(PartyBase party)
        {
            if (party?.IsSettlement == true)
                return party.Settlement?.Name?.ToString() ?? string.Empty;
            if (party?.IsMobile == true)
                return party.MobileParty?.Name?.ToString() ?? string.Empty;
            return party?.Name?.ToString() ?? string.Empty;
        }

        private static Clan ResolveCaptorClan(Hero prisoner)
        {
            PartyBase party = prisoner?.PartyBelongedToAsPrisoner;
            if (party == null)
                return null;
            if (party.IsSettlement)
                return party.Settlement?.OwnerClan;
            if (!party.IsMobile)
                return null;

            MobileParty mobileParty = party.MobileParty;
            if ((mobileParty.IsMilitia || mobileParty.IsGarrison || mobileParty.IsCaravan || mobileParty.IsVillager)
                && party.Owner != null)
            {
                return party.Owner.IsNotable
                    ? party.Owner.CurrentSettlement?.OwnerClan
                    : party.Owner.Clan;
            }

            if (mobileParty.IsPatrolParty)
                return mobileParty.HomeSettlement?.OwnerClan;

            return mobileParty.ActualClan ?? party.Owner?.Clan ?? mobileParty.HomeSettlement?.OwnerClan;
        }

        private sealed class HouseholdRansomCandidate
        {
            public Hero Prisoner { get; }
            public Clan CaptorClan { get; }
            public int RansomPrice { get; }
            public string HoldingName { get; }
            public bool IsTreatyHostage { get; }

            public HouseholdRansomCandidate(
                Hero prisoner,
                Clan captorClan,
                int ransomPrice,
                string holdingName, bool isTreatyHostage = false)
            {
                Prisoner = prisoner;
                CaptorClan = captorClan;
                RansomPrice = ransomPrice;
                HoldingName = holdingName ?? string.Empty;
                IsTreatyHostage = isTreatyHostage;
            }
        }
    }
}
