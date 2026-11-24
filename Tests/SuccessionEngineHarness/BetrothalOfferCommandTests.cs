using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

internal static partial class MarriageHouseholdTests
{
    private static float _betrothalCommandScore;
    private static bool BetrothalCommandScore(ref float __result) { __result = _betrothalCommandScore; return false; }
    private static bool BetrothalCommandClans(ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Rulers.Values.ToList()); return false; }

    private static void CheckBetrothalOfferCommand(Action<bool, string> check, CampaignBehaviorBase behavior, Hero first, Hero second)
    {
        var type = behavior.GetType();
        var helper = type.Assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
        var context = helper.GetNestedType("EvaluationContext", BindingFlags.NonPublic);
        var adapter = type.Assembly.GetType("BellumCivile.Patches.BetrothalOfferPopupPatch");
        var installFlag = AccessTools.Field(adapter, "<IsAvailable>k__BackingField");
        var oldInstalled = installFlag.GetValue(null);
        var playerOffer = AccessTools.Field(_offers.GetType(), "_currentOfferedPlayerClanHero");
        var campaign = Campaign.Current;
        var oldInformation = campaign.CampaignInformationManager;
        var trackerField = AccessTools.Field(typeof(Campaign), "<MapTimeTracker>k__BackingField");
        var oldTracker = trackerField.GetValue(campaign);
        var harmony = new Harmony("bellum.tests.force_betrothal_offer");
        void Patch(MethodBase target, string prefix) => harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), prefix));
        string Command(params string[] args) => CheatCommands.ForceBetrothalOffer(args.ToList());
        try
        {
            Patch(AccessTools.Method(type, "BetrothalScore"), nameof(BetrothalCommandScore));
            Patch(AccessTools.PropertyGetter(typeof(Clan), "All"), nameof(BetrothalCommandClans));
            Patch(AccessTools.Method(context, "PoliticalValue"), nameof(NoPolitics));
            Patch(AccessTools.Method(typeof(MBRandom), "RandomInt", new[] { typeof(int) }), nameof(Zero));
            Patch(AccessTools.Method(typeof(MBInformationManager), "AddNotice", new[] { typeof(InformationData) }), nameof(Skip));
            installFlag.SetValue(null, true);
            trackerField.SetValue(campaign, Activator.CreateInstance(trackerField.FieldType,
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { CampaignTime.Days(_pledgeDay) }, null));
            var manager = new CampaignInformationManager();
            campaign.CampaignInformationManager = manager;
            var pending = AccessTools.Field(type, "_incomingOffer");

            check(Command("bogus").StartsWith("Usage:") && Command("any", "missing_hero").Contains("No player-clan member found"),
                "Betrothal console command validates the optional mode and member selector");
            playerOffer.SetValue(_offers, second);
            check(Command().Contains("already active") && pending.GetValue(behavior) == null,
                "The command does not compete with an open native marriage offer");
            playerOffer.SetValue(_offers, null);

            _betrothalCommandScore = 94;
            check(Command("matrilineal", second.StringId).Contains("best NPC score: 94; required: 95") && pending.GetValue(behavior) == null,
                "Forced betrothal uses the normal bilateral acceptance threshold and reports the best score when refused");
            _betrothalCommandScore = 120;
            check(Command("patrilineal", second.StringId).Contains("No eligible") && pending.GetValue(behavior) == null,
                "Forced betrothal cannot take a protected heiress out of her house");
            string result = Command("MaTrIlInEaL", second.StringId.ToUpperInvariant());
            object pledge = pending.GetValue(behavior);
            check(result.StartsWith("Success!") && result.Contains("open the betrothal notification") && pledge != null,
                "Console use publishes an offer immediately for the requested family member by case-insensitive ID");
            check(AccessTools.Field(pledge.GetType(), "First").GetValue(pledge) == first
                && AccessTools.Field(pledge.GetType(), "Second").GetValue(pledge) == second
                && AccessTools.Field(pledge.GetType(), "Destination").GetValue(pledge) == first.Clan
                && !(bool)AccessTools.Field(pledge.GetType(), "Accepted").GetValue(pledge) && Spouses.Count == 0,
                "The command retains both households and creates an unanswered offer, not a pledge or wedding");
            var notices = (List<InformationData>)AccessTools.Field(typeof(CampaignInformationManager), "_mapNotices").GetValue(manager);
            check(notices.Single() is BetrothalOfferNotice notice && notice.IsValid()
                && Command().Contains("already active") && CheatCommands.ForceMarriageOffer(null).Contains("betrothal offer is already active")
                && notices.Count == 1, "The real betrothal map notice is registered once and neither command can overwrite it");
            AccessTools.Method(type, "ResolveIncomingOffer").Invoke(behavior, new[] { pledge, (object)false });
        }
        finally
        {
            campaign.CampaignInformationManager = oldInformation;
            trackerField.SetValue(campaign, oldTracker);
            installFlag.SetValue(null, oldInstalled);
            playerOffer.SetValue(_offers, null);
            harmony.UnpatchAll(harmony.Id);
        }
    }
}
