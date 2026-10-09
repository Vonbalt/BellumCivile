using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

internal static class HostagePactDurationTests
{
    private static Clan _firstHouse, _secondHouse;
    private static Kingdom _firstRealm;
    private static List<TreatyTermRecord> _editedTerms;
    private static bool CaptureDraft(List<TreatyTermRecord> __0) { _editedTerms = __0; return false; }
    private static bool RulingHouse(Kingdom __instance, ref Clan __result)
    { __result = __instance == _firstRealm ? _firstHouse : _secondHouse; return false; }
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(HostagePactRecord).Assembly;
        var service = assembly.GetType("BellumCivile.TreatyHostageTerms", true);
        var rules = assembly.GetType("BellumCivile.HostagePactRules", true);
        object Rule(string name, params object[] args) => AccessTools.Method(rules, name).Invoke(null, args);
        int Cost(int tier, int days) => (int)Rule("GetNegotiatedCost", tier, days);
        check(AccessTools.Property(assembly.GetType("BellumCivile.BellumCivileSettings"), "HostagePactDurationDays") == null,
            "Negotiated duration replaces the MCM slider");
        foreach (int days in new[] { 30, 50, 100, 150, 200, 300, 500, 1000 })
        for (int tier = 1; tier <= 4; tier++)
            check(Cost(tier, days) == 36 - 6 * tier + (days - 50) / 10,
                $"Duration price: tier {tier}, {days} days");
        foreach (int days in new[] { 0, 1, 29, 31, 99, 1001 })
            check(!(bool)Rule("IsNegotiableDuration", days), "New draft rejects non-negotiable duration " + days);
        int[] lengths = { 30, 50, 100, 150, 200, 300, 500, 1000 };
        float[] penalties = { 0, 0, 10, 30, 50, 110, 230, 530 };
        for (int i = 0; i < lengths.Length; i++)
            check(Math.Abs((float)Rule("GetDurationPenalty", lengths[i]) - penalties[i]) < .001,
                "Progressive diplomatic restraint at " + lengths[i]);
        check((float)Rule("GetRecoveryUtility", 50, 0f) == 0
            && (float)Rule("GetRecoveryUtility", 100, 0f) == 15
            && (float)Rule("GetRecoveryUtility", 1000, 0f) == 15
            && (float)Rule("GetRecoveryUtility", 100, 5f) == 7.5f
            && (float)Rule("GetRecoveryUtility", 100, 10f) == 0,
            "Recovery bonus needs exhaustion and stops growing at 100 days");
        check((int)Rule("GetPreferredAiDuration", 50f, 500f) == 50
            && (int)Rule("GetPreferredAiDuration", 5f, 500f) == 100
            && (int)Rule("GetPreferredAiDuration", 0f, 199f) == 100
            && (int)Rule("GetPreferredAiDuration", 0f, 200f) == 150
            && (int)Rule("GetPreferredAiDuration", 0f, 400f) == 200,
            "Exceptional AI durations require complete exhaustion and a prolonged campaign");
        foreach (float enthusiasm in new[] { 0f, 5f, 10f, 50f, 100f })
        foreach (float warDays in new[] { 0f, 100f, 200f, 400f, 1000f })
        {
            var durations = (int[])Rule("GetAiDurations", enthusiasm, warDays);
            int preferred = (int)Rule("GetPreferredAiDuration", enthusiasm, warDays);
            check(durations.Length <= 5 && durations.All(d => d >= 30 && d <= 200)
                && durations.Contains(30) && durations.Contains(50)
                && durations.OrderByDescending(d => (float)Rule("GetAiDurationAdjustment", d, enthusiasm, warDays)).First() == preferred,
                $"Bounded AI duration shortlist and preference: will={enthusiasm}, war={warDays}");
        }

        var harmony = new Harmony("bellum.tests.hostage.duration");
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"),
                prefix: new HarmonyMethod(typeof(HostagePactDurationTests), nameof(RulingHouse)));
            var secondRealm = Empty<Kingdom>(); secondRealm.StringId = "second_realm";
            _firstRealm = Empty<Kingdom>(); _firstRealm.StringId = "first_realm";
            _firstHouse = Empty<Clan>(); _firstHouse.StringId = "first_house";
            _secondHouse = Empty<Clan>(); _secondHouse.StringId = "second_house";
            var hero = Empty<Hero>(); hero.StringId = "pledged_heir";
            var candidate = Activator.CreateInstance(assembly.GetType("BellumCivile.TreatyHostageCandidate"),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { hero, 1 }, null);
            TreatyTermRecord Draft(int? days = null, bool priced = true) => (TreatyTermRecord)AccessTools.Method(service, "Create")
                .Invoke(null, new object[] { _firstRealm, secondRealm, candidate, false, days, priced });
            int DraftDuration(params TreatyTermRecord[] terms) => (int)AccessTools.Method(service, "DurationForDraft")
                .Invoke(null, new object[] { terms });
            check(Draft().DurationDays == 50 && DraftDuration() == 50 && Draft().HostageDurationPriced,
                "New pacts start at 50 days with negotiated pricing");
            var original = Draft(133, false);
            check(original.WarScoreCost == 30 && DraftDuration(original) == 133,
                "Legacy pending MCM draft retains its arbitrary duration and tier-only price");
            var reciprocal = new TreatyTermRecord(TreatyTermType.HostagePeace, 12, durationDays: 133,
                fromKingdomId: secondRealm.StringId, toKingdomId: _firstRealm.StringId,
                heroId: "other", clanId: _secondHouse.StringId, hostageReceivingClanId: _firstHouse.StringId,
                wasVoluntaryOffering: true, hostageTier: 4);
            var wealth = new TreatyTermRecord(TreatyTermType.Reparations, 5);
            var changed = (List<TreatyTermRecord>)AccessTools.Method(service, "WithDuration")
                .Invoke(null, new object[] { new[] { original, reciprocal, wealth }, 100 });
            check(changed[0].DurationDays == 100 && changed[1].DurationDays == 100
                && changed[0].WarScoreCost == 35 && changed[1].WarScoreCost == 17
                && changed.Take(2).All(t => t.HostageDurationPriced) && ReferenceEquals(changed[2], wealth)
                && original.DurationDays == 133 && original.WarScoreCost == 30,
                "Explicit duration edit reprices both pledges without mutating prior draft or unrelated terms");
            var cloned = (List<TreatyTermRecord>)AccessTools.Method(assembly.GetType("BellumCivile.UI.Parley.PeaceParleyVM"), "CloneTerms")
                .Invoke(null, new object[] { changed });
            check(cloned.Take(2).All(t => t.HostageDurationPriced) && cloned[0].WarScoreCost == 35,
                "Parley reset/clone preserves negotiated pricing marker");
            object[] validArgs = { cloned.Take(2), _firstRealm.StringId, secondRealm.StringId, null };
            // Factory direction above is an offering only when the first realm wins.
            validArgs[1] = secondRealm.StringId; validArgs[2] = _firstRealm.StringId;
            check((bool)AccessTools.Method(service, "ValidShape").Invoke(null, validArgs), "Repriced reciprocal pact passes central validation");
            var forged = new TreatyTermRecord(TreatyTermType.HostagePeace, 30, durationDays: 100,
                fromKingdomId: original.FromKingdomId, toKingdomId: original.ToKingdomId, heroId: original.HeroId,
                clanId: original.ClanId, hostageTier: 1, hostageReceivingClanId: original.HostageReceivingClanId,
                hostageDurationPriced: true);
            validArgs[0] = new[] { forged };
            check(!(bool)AccessTools.Method(service, "ValidShape").Invoke(null, validArgs),
                "New 100-day clause cannot reuse the legacy 30 WS heir price");

            var vmType = assembly.GetType("BellumCivile.UI.Parley.PeaceParleyVM");
            var vm = FormatterServices.GetUninitializedObject(vmType);
            var proposal = new TreatyProposalRecord("ui", secondRealm.StringId, _firstRealm.StringId, _firstRealm.StringId, 0, 100, false);
            AccessTools.Field(vmType, "_proposal").SetValue(vm, proposal);
            harmony.Patch(AccessTools.Method(vmType, "ReplaceDraft"),
                prefix: new HarmonyMethod(typeof(HostagePactDurationTests), nameof(CaptureDraft)));
            void Click(bool increase)
            {
                _editedTerms = null;
                AccessTools.Method(vmType, increase ? "ExecuteIncreaseHostageDuration" : "ExecuteDecreaseHostageDuration").Invoke(vm, null);
            }
            proposal.ReplaceTerms(changed.Take(2));
            Click(true);
            check(_editedTerms.Count == 2 && _editedTerms.All(t => t.DurationDays == 110)
                && _editedTerms[0].WarScoreCost == 36 && _editedTerms[1].WarScoreCost == 18,
                "Actual plus command updates both pledged hostages and prices");
            proposal.ReplaceTerms(new[] { Draft(30) }); Click(false);
            check(_editedTerms == null, "Minus command stops at 30 days");
            proposal.ReplaceTerms(new[] { Draft(1000) }); Click(true);
            check(_editedTerms == null, "Plus command stops at 1000 days");
            proposal.ReplaceTerms(new[] { original }); Click(true);
            check(_editedTerms.Single().DurationDays == 140 && _editedTerms.Single().HostageDurationPriced,
                "Editing a legacy 133-day pledge moves to next ten-day step and new pricing");
            Click(false);
            check(_editedTerms.Single().DurationDays == 130, "Legacy duration decrease uses preceding ten-day step");
            proposal.ReplaceTerms(new[] { Draft(50) });
            AccessTools.Field(vmType, "_isDraftEditingDisabled").SetValue(vm, true); Click(true);
            check(_editedTerms == null, "Vassal cannot change pact duration through commands");
            AccessTools.Field(vmType, "_isDraftEditingDisabled").SetValue(vm, false);
            proposal.ReplaceTerms(Array.Empty<TreatyTermRecord>()); Click(true);
            check(_editedTerms == null && !(bool)AccessTools.Property(vmType, "HasHostagePact").GetValue(vm),
                "Empty draft has no duration control and cannot create a hostage by adjustment");

            HostagePactRecord Pact(int days, bool recorded = true, bool priced = false) => new HostagePactRecord {
                Id = "duration-test", FirstRealm = _firstRealm, SecondRealm = secondRealm,
                FirstHouse = _firstHouse, SecondHouse = _secondHouse, AgreedDurationDays = days, DurationRecorded = recorded,
                FirstHostage = new TreatyHostageRecord { Hero = hero, SupplyingHouse = _firstHouse,
                    ReceivingHouse = _secondHouse, Holding = Empty<Settlement>(), Tier = 1,
                    NegotiatedCost = priced ? Cost(1, days) : 30, DurationPriced = priced, CustodyEstablished = true }
            };
            bool Activate(HostagePactRecord pact, double day) => (bool)AccessTools.Method(typeof(HostagePactRecord), "TryActivate")
                .Invoke(pact, new object[] { day });
            bool Expire(HostagePactRecord pact, double day, bool daily) => (bool)AccessTools.Method(typeof(HostagePactRecord), "CanExpire")
                .Invoke(pact, new object[] { day, daily });
            foreach (int days in new[] { 30, 50, 100, 200, 1000 })
            {
                var pact = Pact(days, priced: true);
                check(Activate(pact, 10) && pact.EndDay == 10 + days,
                    "Negotiated pact activates with its priced duration: " + days);
                check(!Expire(pact, pact.EndDay - .001, true) && Expire(pact, pact.EndDay, false)
                    && !Activate(pact, 20) && pact.EndDay == 10 + days,
                    "Expiry and reactivation preserve signed boundary: " + days);
            }
            foreach (int days in new[] { 1, 99, 133, 1000 })
                check(Activate(Pact(days), 10), "Legacy tier-only pact still activates at saved duration " + days);
            var legacy = Pact(0, false);
            check(Activate(legacy, 10) && legacy.EndDay == 110, "Missing snapshot retains original 100 days, not new 50-day default");
            var zero = Pact(0);
            check(Activate(zero, 10) && !Expire(zero, 10, false) && Expire(zero, 10, true),
                "Legacy zero-day pact still resolves on a daily tick");
            var malformed = Pact(100, priced: true); malformed.FirstHostage.NegotiatedCost = 30;
            check(!Activate(malformed, 10), "Delivery cannot activate a mispriced negotiated pact");
            var recovered = Pact(200, priced: true);
            recovered.TreatySettlementStarted = recovered.TreatySettlementCompleted = true;
            recovered.SigningDayRecorded = true; recovered.TreatySigningDay = 10;
            check(Activate(recovered, recovered.TreatySigningDay) && recovered.EndDay == 210,
                "Interrupted negotiated delivery preserves original signing day and agreed term");

            var resultType = assembly.GetType("BellumCivile.TreatyAiDraftResult");
            var posture = Enum.ToObject(assembly.GetType("BellumCivile.TreatyNegotiationPosture"), 1);
            var result = Activator.CreateInstance(resultType, new object[] { new[] { Draft(200) }, posture, 50, "test", false });
            var alternatives = ((IEnumerable)AccessTools.Method(assembly.GetType("BellumCivile.TreatyAiDraftService"), "ShorterHostageDrafts")
                .Invoke(null, new[] { result })).Cast<object>().ToList();
            check(alternatives.Select(a => ((IEnumerable<TreatyTermRecord>)resultType.GetProperty("Terms").GetValue(a))
                .First().DurationDays).SequenceEqual(new[] { 150, 100, 50, 30 }),
                "Rejected long drafts have bounded shorter alternatives, never longer ones");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            _firstRealm = null; _firstHouse = _secondHouse = null;
            _editedTerms = null;
        }
    }
}
