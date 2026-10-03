using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

internal static class CourtSubjugationEngineTests
{
    private static readonly Assembly Mod = typeof(CourtAgendaRecord).Assembly;
    private static int _cost = 75;
    private static int _relation;
    private static bool _eligible = true;

    private static bool ClientCandidate(object[] __args, ref bool __result)
    {
        __args[2] = Activator.CreateInstance(Mod.GetType("BellumCivile.TreatyClientKingdomCandidate"),
            __args[1], __args[0], 2, _cost);
        __args[3] = "test clientage eligibility";
        __result = _eligible;
        return false;
    }
    private static bool NoForce(object[] __args, ref bool __result)
    { __args[2] = null; __args[3] = "no force vassalization fixture"; __result = false; return false; }
    private static bool NoTrait(ref TraitObject __result) { __result = null; return false; }
    private static bool Relation(ref int __result) { __result = _relation; return false; }

    internal static void Run(Action<bool, string> check)
    {
        Kingdom Realm(string id)
        {
            var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
            realm.StringId = id;
            return realm;
        }
        var winner = Realm("crown");
        var loser = Realm("target");
        var preferenceType = Mod.GetType("BellumCivile.CourtTreatyDraftPreference");
        object Preference(string from = "target", string to = "crown") => Activator.CreateInstance(preferenceType,
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { TreatyTermType.MakeClientKingdom, from, to, 40f, 80f, "test aligned Crown" }, null);
        var preference = Preference();
        var scorePreference = AccessTools.Method(preferenceType, "Score");
        float PreferenceScore(params TreatyTermRecord[] terms) => (float)scorePreference.Invoke(preference, new object[] { terms });
        var matching = new TreatyTermRecord(TreatyTermType.MakeClientKingdom, 75, fromKingdomId: "target", toKingdomId: "crown");
        check(PreferenceScore(matching) == 80 && PreferenceScore(matching, matching) == 80, "Production package preference applies once, not per repeated term");
        check(PreferenceScore(new TreatyTermRecord(TreatyTermType.MakeClientKingdom, 75, fromKingdomId: "crown", toKingdomId: "target")) == 0,
            "Reverse clientage does not fulfill Crown preference");
        check(PreferenceScore(new TreatyTermRecord(TreatyTermType.ForceVassalization, 75, fromKingdomId: "target", toKingdomId: "crown")) == 0,
            "Annexation is not interchangeable with clientage");
        check(PreferenceScore(new TreatyTermRecord(TreatyTermType.MakeClientKingdom, 75, fromKingdomId: "other", toKingdomId: "crown")) == 0
            && PreferenceScore() == 0, "Other targets and absent terms yield no preference");

        var war = new WarScoreRecord("crown|target", "crown", "target", 10, null);
        var draftService = Mod.GetType("BellumCivile.TreatyAiDraftService");
        var alternative = AccessTools.Method(draftService, "BuildObjectiveDraft");
        var ordinary = AccessTools.Method(draftService, "BuildDraft");
        var clientage = AccessTools.Method(draftService, "GetClientageSettlement");
        var posture = Enum.Parse(Mod.GetType("BellumCivile.TreatyNegotiationPosture"), "TotalVictory");
        var proposal = new TreatyProposalRecord(war.WarKey, "crown", "target", "crown", 20, 100, false);
        object Alternative(TreatyProposalRecord p, Kingdom drafter, int spend, object pref) =>
            alternative.Invoke(null, new object[] { war, p, winner, loser, drafter, spend, pref });
        var harmony = new Harmony("bellum.test.court_clientage");
        try
        {
            harmony.Patch(AccessTools.Method(Mod.GetType("BellumCivile.TreatyDraftService"), "TryGetClientKingdomCandidate"),
                prefix: new HarmonyMethod(typeof(CourtSubjugationEngineTests), nameof(ClientCandidate)));
            harmony.Patch(AccessTools.Method(Mod.GetType("BellumCivile.TreatyRealmTransitionService"), "TryGetForceVassalizationCandidate"),
                prefix: new HarmonyMethod(typeof(CourtSubjugationEngineTests), nameof(NoForce)));
            harmony.Patch(AccessTools.Method(draftService, "GetRulerRelation"),
                prefix: new HarmonyMethod(typeof(CourtSubjugationEngineTests), nameof(Relation)));
            foreach (var name in new[] { "Calculating", "Valor", "Mercy", "Honor" })
                harmony.Patch(AccessTools.PropertyGetter(typeof(DefaultTraits), name),
                    prefix: new HarmonyMethod(typeof(CourtSubjugationEngineTests), nameof(NoTrait)));
            check(Alternative(proposal, winner, 100, null) == null, "No alignment context means no extra draft");
            check(Alternative(proposal, loser, 100, preference) == null, "Losing drafter cannot use victor's alignment");
            check(Alternative(proposal, winner, 100, Preference("other")) == null, "Alternative generation is target-specific");
            check(clientage.Invoke(null, new object[] { proposal, winner, loser, winner, posture, 100, false, null, war }) == null,
                "Ordinary neutral ruler still fails the unchanged clientage threshold");
            var draft = Alternative(proposal, winner, 100, preference);
            check(draft != null, "Actual clientage drafting accepts neutral aligned ruler at 45 + 40 = 85");
            var terms = (IReadOnlyList<TreatyTermRecord>)AccessTools.Property(draft.GetType(), "Terms").GetValue(draft);
            check(terms.Count == 1 && terms[0].Type == TreatyTermType.MakeClientKingdom && terms[0].WarScoreCost == 75,
                "Objective alternative preserves candidate cost and correct term");
            check((bool)AccessTools.Property(draft.GetType(), "IsObjectiveAlternative").GetValue(draft), "Alternative retains identity for ordinary-draft ties");
            check(Alternative(proposal, winner, 74, preference) == null, "Objective does not bypass chosen spend limit");
            _cost = 101;
            check(Alternative(proposal, winner, 150, preference) == null, "Objective does not invent additional treaty budget");
            _cost = 75;
            _eligible = false;
            check(Alternative(proposal, winner, 100, preference) == null, "Failed clientage eligibility blocks the objective draft");
            _eligible = true;
            var limited = new TreatyProposalRecord(war.WarKey, "crown", "target", "crown", 20, 89, false);
            check(Alternative(limited, winner, 89, preference) == null, "Crown preference preserves TotalVictory posture gate");
            var forced = new TreatyProposalRecord(war.WarKey, "crown", "target", "crown", 20, 80, true);
            check(Alternative(forced, winner, 80, preference) != null, "Existing forced-settlement posture remains supported");
            _relation = -200;
            var originalDraft = ordinary.Invoke(null, new object[] { war, proposal, winner, loser, winner, 100, true });
            var originalTerms = (IReadOnlyList<TreatyTermRecord>)AccessTools.Property(originalDraft.GetType(), "Terms").GetValue(originalDraft);
            check(originalTerms.Count == 1 && originalTerms[0].Type == TreatyTermType.MakeClientKingdom
                && !(bool)AccessTools.Property(originalDraft.GetType(), "IsObjectiveAlternative").GetValue(originalDraft),
                "Ordinary structural drafting still independently selects eligible clientage");

            var packageScore = AccessTools.Method(typeof(ForeignTreatyBehavior), "ScoreAiDraftCandidate");
            TreatyCouncilEvaluation Council(int votes) => new TreatyCouncilEvaluation(votes, 0, 1, 0, 0, null);
            float Score(object candidate, bool otherAccepts) => (float)packageScore.Invoke(null,
                new object[] { candidate, 100, "crown", 100, Council(100), Council(100), true, otherAccepts, false, false, false });
            check(Score(draft, false) + PreferenceScore(terms.ToArray()) < Score(originalDraft, true),
                "Actual package formula does not sacrifice another side's acceptance for +80");
            check(Score(draft, true) == Score(originalDraft, true), "Identical ordinary and objective packages have identical base score");
        }
        finally
        {
            harmony.UnpatchAll("bellum.test.court_clientage");
            _cost = 75; _relation = 0; _eligible = true;
        }

        var plan = new CourtSubjugationRecord { Target = loser, Activated = true, ActivatedDay = 20 };
        var activeBonus = AccessTools.Method(typeof(CourtSubjugationRecord), "ActiveBonus");
        check((float)activeBonus.Invoke(plan, new object[] { loser, 30d, 84d }) == 15
            && (float)activeBonus.Invoke(plan, new object[] { loser, 85d, 84d }) == 0,
            "Production saved clientage plan respects its window");
        var fields = typeof(CourtSubjugationRecord).GetFields();
        check(fields.Length == 4 && fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
            "Every clientage plan field is saved");
        var loaded = new CourtSubjugationRecord();
        foreach (var field in fields) field.SetValue(loaded, field.GetValue(plan));
        check((float)activeBonus.Invoke(loaded, new object[] { loser, 30d, 84d }) == 15, "Clientage record field roundtrip preserves native target reference");
        var behavior = new CourtAgendaBehavior();
        var agenda = new CourtAgendaRecord { State = CourtAgendaState.PursuingObjective,
            ObjectiveData = new CourtObjectiveRecord { Kind = "court_seek_clientage" }, Subjugation = plan };
        ((List<CourtAgendaRecord>)AccessTools.Field(typeof(CourtAgendaBehavior), "_agendas").GetValue(behavior)).Add(agenda);
        AccessTools.Method(typeof(CourtAgendaBehavior), "SettleClosedExecutiveObjectives").Invoke(behavior, null);
        check(!agenda.ResultApplied && agenda.IsOngoingObjective, "Closed-ballot cleanup leaves active subjugation untouched");
    }
}
