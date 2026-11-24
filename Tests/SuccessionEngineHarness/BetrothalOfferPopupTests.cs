using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MarriageOfferPopup;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Library;

internal static partial class MarriageHouseholdTests
{
    private static bool FormatPledgeDate(ref string __result) { __result = "Winter 2, 1085"; return false; }

    private static void ResolveBetrothalThroughPopup(Action<bool, string> check, object pledge, Hero first, Hero second, bool accept)
    {
        const string adapterId = "com.bellumcivile.betrothal_offer_popup";
        var type = typeof(BetrothalOfferNotice).Assembly.GetType("BellumCivile.Patches.BetrothalOfferPopupPatch");
        var harmony = new Harmony("bellum.test.betrothal_offer_popup");
        var openingField = AccessTools.Field(type, "_opening");
        var installFlag = AccessTools.Field(type, "<IsAvailable>k__BackingField");
        var dayField = AccessTools.Field(pledge.GetType(), "OfferExpiresDay");
        var savedExpiry = dayField.GetValue(pledge);
        var oldInformation = Campaign.Current.CampaignInformationManager;
        int closes = 0;
        try
        {
            harmony.Patch(AccessTools.Method(typeof(CampaignTime), nameof(CampaignTime.ToString), Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(FormatPledgeDate)));
            harmony.Patch(AccessTools.Method(typeof(MBInformationManager), "AddNotice", new[] { typeof(InformationData) }),
                prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(Skip)));
            AccessTools.Method(type, "Initialize").Invoke(null, null);
            check((bool)installFlag.GetValue(null), "Betrothal popup hooks bind the installed native notification and offer methods");

            var manager = new CampaignInformationManager();
            Campaign.Current.CampaignInformationManager = manager;
            var behavior = typeof(BetrothalOfferNotice).Assembly.GetType("BellumCivile.Behaviors.BetrothalBehavior");
            var publish = AccessTools.Method(behavior, "PublishOfferNotice");
            publish.Invoke(null, new[] { pledge });
            publish.Invoke(null, new[] { pledge });
            var notices = (List<InformationData>)AccessTools.Field(typeof(CampaignInformationManager), "_mapNotices").GetValue(manager);
            var notice = (BetrothalOfferNotice)notices.Single();
            check((bool)AccessTools.Field(pledge.GetType(), "OfferNoticePublished").GetValue(pledge)
                && AccessTools.Field(typeof(BetrothalOfferNotice), "Pledge").GetValue(notice) == pledge,
                "Publishing twice keeps one saved notice linked to the original offer");
            var noticeVm = new BellumCivile.UI.BetrothalOfferNotificationItemVM(notice);
            check(notice.IsValid() && noticeVm.NotificationIdentifier == "marriage", "Only the separate incoming betrothal uses the native marriage icon");
            dayField.SetValue(pledge, _pledgeDay - 1);
            check(!notice.IsValid(), "An expired offer does not remain inspectable");
            dayField.SetValue(pledge, savedExpiry);

            MarriageOfferPopupHeroVM Candidate(Hero hero)
            {
                var candidate = Blank<MarriageOfferPopupHeroVM>();
                AccessTools.Field(typeof(MarriageOfferPopupHeroVM), "<Hero>k__BackingField").SetValue(candidate, hero);
                return candidate;
            }

            var popup = Blank<MarriageOfferPopupVM>();
            popup.OffereeClanMember = Candidate(second);
            popup.OffererClanMember = Candidate(first);
            popup.ConsequencesList = new MBBindingList<BindingListStringItem>();
            AccessTools.Field(typeof(MarriageOfferPopupVM), "_marriageBehavior").SetValue(popup, _offers);
            AccessTools.Field(typeof(MarriageOfferPopupVM), "_onClose").SetValue(popup, (Action)(() => closes++));
            var opening = Activator.CreateInstance(openingField.FieldType, nonPublic: true);
            AccessTools.Field(openingField.FieldType, "Offer").SetValue(opening, pledge);
            openingField.SetValue(null, opening);
            popup.RefreshValues();
            openingField.SetValue(null, null);
            popup.RefreshValues();
            check((bool)AccessTools.Field(opening.GetType(), "Bound").GetValue(opening)
                && popup.TitleText == notice.TitleText.ToString() && popup.TitleText.StartsWith("Betrothal offer from ")
                && popup.ButtonOkLabel == "Give my word" && popup.ButtonCancelLabel == "Decline",
                "The betrothal presentation survives a native refresh with no active vanilla marriage offer");
            var terms = popup.ConsequencesList.Select(item => item.Item).ToList();
            check(terms.Count == 5 && terms[0].Contains("Matrilineal") && terms[1].Contains("At the wedding,")
                && terms[2].Contains("Future children") && terms[3].Contains("Winter 2, 1085") && terms[4].Contains("No marriage or claim transfer"),
                "The native consequences panel previews household, descendants, date and deferred effects");
            check((bool)AccessTools.Method(type, "Accept").Invoke(null, new object[] { Blank<MarriageOfferPopupVM>(), null }) && closes == 0,
                "An unmarked marriage popup always keeps its original accept path");

            var waiting = AccessTools.Field(_offers.GetType(), "_acceptedMarriageOffersThatWaitingForAvailability").GetValue(_offers) as System.Collections.IDictionary;
            if (accept) popup.ExecuteAcceptOffer();
            else popup.ExecuteDeclineOffer();
            check(closes == 1 && !notice.IsValid() && Spouses.Count == 0 && waiting?.Count == 0,
                $"The native screen closes on {(accept ? "accept" : "decline")} without enqueuing or executing a vanilla wedding");
        }
        finally
        {
            Campaign.Current.CampaignInformationManager = oldInformation;
            dayField.SetValue(pledge, savedExpiry);
            openingField.SetValue(null, null);
            harmony.UnpatchAll(harmony.Id);
            new Harmony(adapterId).UnpatchAll(adapterId);
            installFlag.SetValue(null, false);
        }
    }
}
