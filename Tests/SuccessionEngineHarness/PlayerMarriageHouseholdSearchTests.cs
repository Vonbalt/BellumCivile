using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

internal static partial class MarriageHouseholdTests
{
    private static void RunAlternatePlayerOffers(Action<bool, string> check, Campaign campaign,
        PlayerMarriageAgreementBehavior behavior, Hero other, Hero player, Action clear)
    {
        var helper = typeof(BellumMarriageModel).Assembly.GetType("BellumCivile.Behaviors.BellumMarriageStrategyHelper");
        var contextType = helper.GetNestedType("EvaluationContext", BindingFlags.NonPublic);
        var recordsField = AccessTools.Field(AgreementBehavior, "_agreements");
        var records = (List<PlayerMarriageAgreement>)recordsField.GetValue(behavior);
        var waiting = (Dictionary<Hero, Hero>)AccessTools.Field(typeof(MarriageOfferCampaignBehavior),
            "_acceptedMarriageOffersThatWaitingForAvailability").GetValue(_offers);
        var manager = (OfferManager)campaign.CampaignBehaviorManager;
        Clan npcHouse = other.Clan, playerHouse = player.Clan;
        Hero oldHeir = Heirs[npcHouse], oldLeader = Leaders[npcHouse];
        Hero oldPlayerHeir = Heirs[playerHouse];
        var oldCrowns = new Dictionary<Kingdom, Hero>(Crowns);
        var retryFixture = new Harmony("bellum.test.player_household_retry");
        string Command(string mode) => CheatCommands.ForceMarriageOffer(new List<string> { mode, player.StringId });
        object Context() => Activator.CreateInstance(contextType,
            new object[] { null, true, null, null, false });
        object Match(object context, Hero first, Hero second) => AccessTools.Method(helper, "EvaluateOutcome")
            .Invoke(null, new object[] { first, second, context, true });
        Clan Destination(object match)
        {
            if (match == null) return null;
            object outcome = AccessTools.Property(match.GetType(), "Outcome").GetValue(match);
            return (Clan)AccessTools.Property(outcome.GetType(), "Destination").GetValue(outcome);
        }
        try
        {
            // Reproduce the reported direction: a player-clan sister and an unprotected NPC husband.
            clear(); Women.Remove(other); Women.Add(player);
            _commandCandidates = new List<Hero> { other };
            _npcHouseholdScores = new Dictionary<Clan, float> { [npcHouse] = 110, [playerHouse] = 120 };
            check(Destination(Match(Context(), other, player)) == playerHouse
                && Destination(Match(Context(), player, other)) == playerHouse,
                "Automatic offer scoring evaluates the incoming husband in either proposal orientation");
            var cached = Context();
            Match(cached, other, player);
            int crownReads = _crownReads, clanReads = _clanReads, rosterReads = _rosterReads;
            for (int i = 0; i < 100; i++) Match(cached, other, player);
            check(_crownReads == crownReads && _clanReads == clanReads && _rosterReads == rosterReads,
                "Both household alternatives reuse heir and continuity snapshots across repeated scoring");
            _npcHouseholdScores[npcHouse] = 120;
            check(Destination(Match(Context(), other, player)) == npcHouse,
                "Equal acceptance preserves the ordinary household as the tie-breaker");
            _npcHouseholdScores[npcHouse] = 130;
            retryFixture.Patch(AccessTools.Method(helper, "MarriageProspectEligible"),
                prefix: new HarmonyMethod(typeof(MarriageHouseholdTests), nameof(Yes)));
            object retry = AccessTools.Method(helper, "ReevaluateProspect")
                .Invoke(null, new object[] { other, player, playerHouse });
            check(Destination(retry) == playerHouse,
                "Actual prospect reevaluation scores the saved household even when another household now scores higher");
            string result = Command("matrilineal");
            check(result.StartsWith("Success!") && records.Single().Destination == playerHouse,
                "Requested matrilineal offer is allowed even when a legal patrilineal match scores higher");
            var agreed = records.Single();
            check(_model.GetClanAfterMarriage(other, player) == playerHouse
                && AccessTools.Method(typeof(PlayerMarriageAgreement), "HouseholdText").Invoke(agreed, null).ToString().Contains(other.Name.ToString()),
                "Offer preview identifies the NPC husband joining the player household");
            clear();

            _npcHouseholdScores[playerHouse] = 94;
            retry = AccessTools.Method(helper, "ReevaluateProspect")
                .Invoke(null, new object[] { other, player, playerHouse });
            check(retry == null, "Refused saved household cancels the prospect instead of substituting an acceptable alternative");
            result = Command("matrilineal");
            check(result.StartsWith("Error:") && result.Contains("npc_score=94/95") && records.Count == 0,
                "Requested household must pass its own NPC acceptance score, not the other arrangement's score");
            check(Destination(Match(Context(), other, player)) == npcHouse,
                "Automatic search may still choose the acceptable ordinary arrangement");
            _npcHouseholdScores[playerHouse] = 200;
            Heirs[npcHouse] = other;
            result = Command("matrilineal");
            check(result.StartsWith("Error:") && result.Contains("household_reasons=") && records.Count == 0,
                "Even 200 acceptance cannot send the NPC's first clan heir away in an unsolicited offer");
            Heirs[npcHouse] = oldHeir;
            Crowns[_realms[0]] = other;
            check(Command("matrilineal").StartsWith("Error:"), "First Crown heir is also protected from outgoing offers");
            Crowns.Clear();
            Leaders[npcHouse] = other;
            check(Command("matrilineal").StartsWith("Error:"), "A current clan head cannot leave through a test offer");
            Leaders[npcHouse] = oldLeader;
            foreach (Hero hero in Houses.Where(p => p.Value == npcHouse && p.Key != other).Select(p => p.Key)) Dead.Add(hero);
            check(Command("matrilineal").StartsWith("Error:"), "Last viable NPC continuation cannot leave through a test offer");
            Dead.Clear();

            // The offer is legal for a player heiress too, with no outgoing-heir confirmation needed.
            Heirs[playerHouse] = player;
            check(Command("matrilineal").StartsWith("Success!") && records.Single().Destination == playerHouse,
                "Player-clan heiress can receive a willing husband without surrendering her household");
            agreed = records.Single();
            _npcHouseholdScores[npcHouse] = 250;
            _offerReady = false;
            _offers.OnMarriageOfferAcceptedOnPopUp();
            check(agreed.Accepted && waiting.TryGetValue(player, out Hero partner) && partner == other && _offerRewards == 0,
                "Incoming husband offer preserves terms when acceptance queues a temporarily unavailable wedding");
            AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "HourlyTick").Invoke(_offers, null);
            check(waiting.Count == 1 && Spouses.Count == 0, "Unavailable incoming husband waits without changing household");
            var store = new OfferStore(); behavior.SyncData(store); store.IsLoading = true;
            var loaded = new PlayerMarriageAgreementBehavior(); loaded.SyncData(store);
            manager.Items.Remove(behavior); manager.Items.Add(loaded);
            check(((List<PlayerMarriageAgreement>)recordsField.GetValue(loaded)).Single().Destination == playerHouse,
                "Save/load retains an explicitly selected incoming husband's destination");
            _offerReady = true;
            AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "HourlyTick").Invoke(_offers, null);
            check(Spouses.TryGetValue(player, out partner) && partner == other && other.Clan == playerHouse
                && _callbackHouse == playerHouse && _offerRewards == 1 && waiting.Count == 0,
                "Native hourly wedding moves the NPC husband into the agreed player house and rewards exactly once");
            AccessTools.Method(typeof(MarriageOfferCampaignBehavior), "HourlyTick").Invoke(_offers, null);
            check(_offerRewards == 1, "Completed incoming husband offer cannot reward twice");
            manager.Items.Remove(loaded); manager.Items.Add(behavior);
        }
        finally
        {
            retryFixture.UnpatchAll(retryFixture.Id);
            clear(); Spouses.Clear(); Houses[other] = npcHouse; Houses[player] = playerHouse;
            Women.Add(other); Women.Remove(player); Dead.Clear();
            Heirs[npcHouse] = oldHeir; Heirs[playerHouse] = oldPlayerHeir; Leaders[npcHouse] = oldLeader;
            Crowns.Clear(); foreach (var entry in oldCrowns) Crowns[entry.Key] = entry.Value;
            _npcHouseholdScores = null; _offerReady = true; _offerRewards = 0;
            manager.Items.RemoveAll(b => b is PlayerMarriageAgreementBehavior);
            manager.Items.Add(behavior);
        }
    }
}
