using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;

internal static class GameplayRelationMemoryTests
{
    private sealed class Change
    {
        internal Hero First, Second;
        internal int Amount;
        internal string Source, Context;
        internal float Days;
        internal RelationMemoryScope Scope;
        internal bool PersonalPair;
    }

    private static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    private static readonly List<Clan> Members = new List<Clan>();
    private static readonly List<Change> Changes = new List<Change>();
    private static readonly Type Service = typeof(RelationMemoryService);
    private static readonly MethodInfo DefaultChange = AccessTools.Method(Service, "ApplyChangeWithDefaultDuration");
    private static readonly MethodInfo Resolve = AccessTools.Method(Service, "ResolveCapturedDescriptor");
    private static int? _actualDelta;
    private static bool _throw;
    private static Clan _crown;
    private static int _relation;

    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Leader(Clan __instance, ref Hero __result)
    { Leaders.TryGetValue(__instance, out __result); return false; }
    private static bool Clans(ref MBReadOnlyList<Clan> __result)
    { __result = new MBReadOnlyList<Clan>(Members); return false; }
    private static bool Yes(ref bool __result) { __result = true; return false; }
    private static bool No(ref bool __result) { __result = false; return false; }
    private static bool MainHero(ref Hero __result) { __result = null; return false; }
    private static bool RulingClan(ref Clan __result) { __result = _crown; return false; }
    private static bool Relation(ref int __result) { __result = _relation; return false; }
    private static bool TitleName(ref string __result) { __result = "County of Test"; return false; }

    private static object Read(object value, string property) => AccessTools.Property(value.GetType(), property).GetValue(value);
    private static object Current => AccessTools.Property(Service, "CurrentDescriptor").GetValue(null);
    private static float DefaultDays(int amount) => (float)Read(
        AccessTools.Method(Service, "BuildFallbackDescriptor").Invoke(null, new object[] { amount }), "DurationDays");

    private static bool Capture(Hero __0, Hero __1, int __2)
    {
        if (Current == null) throw new Exception("Gameplay relation change lacks an explicit memory context.");
        if (_throw) throw new InvalidOperationException("simulated native failure");
        object descriptor = Resolve.Invoke(null, new object[] { _actualDelta ?? __2 });
        Changes.Add(new Change {
            First = __0, Second = __1, Amount = __2,
            Source = (string)Read(descriptor, "SourceId"), Context = (string)Read(descriptor, "ContextText"),
            Days = (float)Read(descriptor, "DurationDays"), Scope = (RelationMemoryScope)Read(descriptor, "Scope"),
            PersonalPair = (bool)AccessTools.Method(Service, "UsesOriginalPersonalPair").Invoke(null, new object[] { __0, __1 })
        });
        return false;
    }

    internal static void Run(Action<bool, string> check)
    {
        CheckRawCallCoverage(check);
        var harmony = new Harmony("bellum.test.gameplay_relation_memories");
        void Patch(MethodBase method, string prefix) => harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(GameplayRelationMemoryTests), prefix));
        Clan House(string id, bool mercenary = false)
        {
            var clan = Blank<Clan>(); clan.StringId = id;
            var hero = Blank<Hero>(); hero.StringId = id + "_leader";
            Leaders[clan] = hero; Members.Add(clan);
            if (mercenary) clan.StartMercenaryService();
            return clan;
        }
        try
        {
            Patch(AccessTools.PropertyGetter(typeof(Clan), "Leader"), nameof(Leader));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "Clans"), nameof(Clans));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "IsDead"), nameof(No));
            Patch(AccessTools.PropertyGetter(typeof(Hero), "MainHero"), nameof(MainHero));
            Patch(AccessTools.PropertyGetter(typeof(Kingdom), "RulingClan"), nameof(RulingClan));
            Patch(AccessTools.Method(typeof(Hero), "GetRelation", new[] { typeof(Hero) }), nameof(Relation));
            Patch(AccessTools.PropertyGetter(Service.Assembly.GetType("BellumCivile.BellumCivileOptions"), "EnableDynamicRelationDrift"), nameof(Yes));
            Patch(AccessTools.Method(typeof(ChangeRelationAction), "ApplyRelationChangeBetweenHeroes"), nameof(Capture));
            Patch(AccessTools.Method(Service.Assembly.GetType("BellumCivile.FeudalTitleDisplayHelper"), "FormatTitleName", new[] { typeof(FeudalTitleRecord), typeof(Clan) }), nameof(TitleName));
            Clan crown = House("crown"), claimant = House("claimant"), holder = House("holder");
            _crown = crown;
            Hero first = Leaders[claimant], second = Leaders[holder], ruler = Leaders[crown];
            void Apply(int amount, RelationMemoryScope scope) => DefaultChange.Invoke(null,
                new object[] { first, second, amount, false, RelationMemorySources.FeudEscalation, scope, "County of Test" });

            foreach (int amount in new[] { -100, -75, -30, -20, -10, -5, -1, 1, 5, 10, 15, 30, 100 })
            foreach (RelationMemoryScope scope in new[] { RelationMemoryScope.Personal, RelationMemoryScope.House })
            {
                Changes.Clear(); Apply(amount, scope);
                Change change = Changes.Single();
                check(change.Amount == amount && change.Days == DefaultDays(amount), "Named legacy change retains amount and decay: " + amount + "/" + scope);
                check(change.Source == RelationMemorySources.FeudEscalation && change.Context == "County of Test"
                    && change.Scope == scope && change.PersonalPair == (scope == RelationMemoryScope.Personal),
                    "Named legacy change preserves explicit source, context and identity scope");
            }
            _actualDelta = 1; Changes.Clear(); Apply(30, RelationMemoryScope.Personal);
            check(Changes.Single().Days == DefaultDays(1), "Clamped gains use actual applied delta for default duration");
            _actualDelta = -1; Changes.Clear(); Apply(-75, RelationMemoryScope.Personal);
            check(Changes.Single().Days == DefaultDays(-1), "Clamped penalties do not become long-lived grievances");
            _actualDelta = null;
            Changes.Clear(); Apply(0, RelationMemoryScope.Personal);
            DefaultChange.Invoke(null, new object[] { null, second, 5, false, RelationMemorySources.FeudEscalation, RelationMemoryScope.Personal, null });
            check(Changes.Count == 0, "Zero and missing-hero changes remain harmless");

            using (RelationMemoryService.Begin(RelationMemorySources.ExpelledMyFriend, 10))
            {
                object outer = Current;
                Apply(1, RelationMemoryScope.Personal);
                check(ReferenceEquals(Current, outer), "Default-duration change restores enclosing explicit context");
                _throw = true;
                try { Apply(1, RelationMemoryScope.Personal); check(false, "Expected native failure"); }
                catch (TargetInvocationException ex) { check(ex.InnerException is InvalidOperationException, "Native exception is propagated"); }
                finally { _throw = false; }
                check(ReferenceEquals(Current, outer)
                    && !(bool)AccessTools.Method(Service, "UsesOriginalPersonalPair").Invoke(null, new object[] { first, second }),
                    "Failure restores memory context and personal identity override");
            }
            check(Current == null, "No memory context leaks between actions");
            CheckTitleAndHouseEffects(check, crown, claimant, holder);
            CheckFeudJudgments(check, ruler, claimant, holder);
            CheckTreatyEffects(check, first, second);
            CheckCouncilEffects(check, ruler, holder);

            var legacy = new RelationMemoryRecord(RelationMemoryScope.Personal, "a", "b", RelationMemorySources.RecentGrievance, "", -20, 2f, 45f);
            Apply(-20, RelationMemoryScope.Personal);
            check(legacy.SourceId == RelationMemorySources.RecentGrievance && legacy.ExpiryDay == 45f && legacy.Value == -20,
                "New named events do not relabel or reschedule existing generic records");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            Leaders.Clear(); Members.Clear(); Changes.Clear(); _actualDelta = null; _throw = false; _crown = null; _relation = 0;
        }
    }

    private static void CheckTitleAndHouseEffects(Action<bool, string> check, Clan crown, Clan claimant, Clan holder)
    {
        MethodInfo usurp = AccessTools.Method(typeof(FeudalTitleUsurpationBehavior), "ApplyUsurpationRelations");
        Changes.Clear();
        usurp.Invoke(null, new object[] { claimant, holder, Blank<FeudalTitleRecord>() });
        Change usurpation = Changes.Single();
        check(usurpation.Amount == -30 && usurpation.Source == RelationMemorySources.TitleUsurpation
            && usurpation.Scope == RelationMemoryScope.House && usurpation.Days == 20 * Math.Max(1, CampaignTime.DaysInYear)
            && usurpation.Context == "County of Test", "Shared title usurpation is a named twenty-year house grievance");
        check(typeof(FeudalTitlePlayerActionService).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Any(m => m.GetMethodBody() != null && PatchProcessor.GetOriginalInstructions(m).Any(i => i.Calls(usurp))),
            "Player usurpation uses the same consequence as autonomous usurpation");

        foreach (FeudalClaimStrength strength in new[] { FeudalClaimStrength.Strong, FeudalClaimStrength.Weak })
        {
            Changes.Clear();
            AccessTools.Method(typeof(FeudalTitlePlayerActionService), "ApplyRevocationRelationPenalty").Invoke(null,
                new object[] { crown, holder, strength });
            Change revocation = Changes.Single();
            int expected = (int)AccessTools.Field(Service.Assembly.GetType("BellumCivile.BellumCivileConstants"),
                strength == FeudalClaimStrength.Strong ? "FeudalTitleRevocationStrongRelationPenalty" : "FeudalTitleRevocationWeakRelationPenalty").GetRawConstantValue();
            check(revocation.Amount == expected && revocation.Source == RelationMemorySources.RevokedMyTitle
                && revocation.Scope == RelationMemoryScope.House && revocation.Days == DefaultDays(expected),
                "Title revocation preserves the claim-dependent penalty and duration: " + strength);
        }
        Changes.Clear();
        AccessTools.Method(typeof(PartitionSuccessionBehavior), "ApplyCadetParentRelationBonus").Invoke(null, new object[] { claimant, holder });
        check(Changes.Single().Source == RelationMemorySources.CadetHouseFounded && Changes[0].Amount == 30
            && Changes[0].Scope == RelationMemoryScope.House && Changes[0].Days == DefaultDays(30),
            "Cadet founding goodwill belongs to the houses without changing amount or duration");

        var mercenary = Blank<Clan>(); mercenary.StringId = "mercenary";
        Leaders[mercenary] = Blank<Hero>(); Members.Add(mercenary); mercenary.StartMercenaryService();
        foreach (int penalty in new[] { -20, -40 })
        {
            Changes.Clear();
            AccessTools.Method(typeof(FeudalTitleBehavior), "ApplySovereignDepartureRelations").Invoke(null,
                new object[] { claimant, Blank<Kingdom>(), penalty });
            check(Changes.Count == 2 && Changes.All(c => c.Second != Leaders[mercenary]
                && c.Source == RelationMemorySources.SovereignDeparture && c.Amount == penalty && c.Days == DefaultDays(penalty)),
                "Sovereign departure names reactions and excludes serving mercenaries: " + penalty);
        }
    }

    private static void CheckFeudJudgments(Action<bool, string> check, Hero ruler, Clan claimant, Clan holder)
    {
        var judge = AccessTools.Method(typeof(ClaimFeudBehavior), "ApplyRulingRelations");
        foreach (ClaimFeudJudgment judgment in new[] { ClaimFeudJudgment.UpholdClaimant, ClaimFeudJudgment.UpholdHolder,
            ClaimFeudJudgment.Suppress, ClaimFeudJudgment.Abstain })
        {
            Changes.Clear();
            judge.Invoke(null, new object[] { ruler, claimant, holder, judgment, ClaimFeudResponse.Accept, ClaimFeudResponse.Accept });
            string[] expected = judgment == ClaimFeudJudgment.Suppress ? new[] { RelationMemorySources.SuppressedMyClaim }
                : judgment == ClaimFeudJudgment.Abstain ? new[] { RelationMemorySources.WithheldFeudJudgment, RelationMemorySources.WithheldFeudJudgment }
                : new[] { RelationMemorySources.UpheldMyTitleRights, RelationMemorySources.RejectedMyTitleRights };
            check(Changes.Select(c => c.Source).SequenceEqual(expected) && Changes.All(c => c.Second == ruler
                && c.Scope == RelationMemoryScope.Personal && c.Days == DefaultDays(c.Amount)),
                "Feud ruling has specific causes with unchanged decay: " + judgment);
        }
        Changes.Clear();
        judge.Invoke(null, new object[] { ruler, claimant, holder, ClaimFeudJudgment.None, ClaimFeudResponse.Defy, ClaimFeudResponse.Defy });
        check(Changes.Count == 2 && Changes.All(c => c.Source == RelationMemorySources.DefiedFeudJudgment
            && c.Amount == (int)AccessTools.Field(Service.Assembly.GetType("BellumCivile.BellumCivileConstants"), "ClaimFeudRulerUpholdRelationPenalty").GetRawConstantValue()),
            "Both defiant parties receive their own judgment-defiance memory");
    }

    private static void CheckTreatyEffects(Action<bool, string> check, Hero first, Hero second)
    {
        var treaty = AccessTools.Method(typeof(ForeignTreatyBehavior), "ApplyTreatyRelationChange",
            new[] { typeof(Hero), typeof(Hero), typeof(int), typeof(string) });
        foreach (var effect in new[] {
            Tuple.Create(15, RelationMemorySources.TreatyRoyalMarriage), Tuple.Create(-30, RelationMemorySources.ForcedVassalization),
            Tuple.Create(5, RelationMemorySources.VoluntaryClientage), Tuple.Create(-30, RelationMemorySources.ForcedClientage),
            Tuple.Create(-10, RelationMemorySources.ForcedClientage), Tuple.Create(-20, RelationMemorySources.TreatySeparation) })
        {
            Changes.Clear(); treaty.Invoke(null, new object[] { first, second, effect.Item1, effect.Item2 });
            check(Changes.Single().Source == effect.Item2 && Changes[0].Amount == effect.Item1 && Changes[0].Days == DefaultDays(effect.Item1),
                "Treaty effect carries its own cause without rebalancing: " + effect.Item2 + "/" + effect.Item1);
        }
        Changes.Clear(); treaty.Invoke(null, new object[] { first, first, -30, RelationMemorySources.ForcedClientage });
        check(Changes.Count == 0, "Treaty helper still rejects self-relations");
    }

    private static void CheckCouncilEffects(Action<bool, string> check, Hero ruler, Clan holder)
    {
        var realm = Blank<Kingdom>(); realm.StringId = "test_realm";
        Type contextType = Service.Assembly.GetType("BellumCivile.CouncilIncidentContext");
        Type qualityType = Service.Assembly.GetType("BellumCivile.CouncilIncidentQuality");
        object context = Activator.CreateInstance(contextType, new object[] {
            null, null, realm, PrivyCouncilOffice.Marshal, "marshal_oversee_logistics", holder, 50f, Enum.ToObject(qualityType, 1) });
        foreach (int amount in new[] { 5, -10 })
        {
            Changes.Clear(); AccessTools.Method(contextType, "ChangeHolderRelation").Invoke(context, new object[] { amount });
            check(Changes.Single().Source == (amount > 0 ? RelationMemorySources.SupportedCouncilAdvice : RelationMemorySources.RejectedCouncilAdvice)
                && Changes[0].First == ruler && Changes[0].Second == Leaders[holder] && Changes[0].Days == DefaultDays(amount),
                "Council incident response records support or rejection with existing lifetime");
        }

        var council = new PrivyCouncilBehavior();
        var gain = AccessTools.Method(typeof(PrivyCouncilBehavior), "ApplyAssignmentRelationGain");
        foreach (string assignment in new[] { "chancellor_appease_nobles", "chancellor_improve_foreign_relations" })
        {
            Changes.Clear(); _relation = 24;
            gain.Invoke(council, new object[] { realm, assignment, ruler, Leaders[holder], 5f });
            check(Changes.Single().Amount == 1 && Changes[0].Days == DefaultDays(1)
                && Changes[0].Source == (assignment == "chancellor_appease_nobles" ? RelationMemorySources.ChancellorAppeasement : RelationMemorySources.ChancellorDiplomacy),
                "Chancellor assignment keeps its relation cap and short decay: " + assignment);
            Changes.Clear(); _relation = 25;
            gain.Invoke(council, new object[] { realm, assignment, ruler, Leaders[holder], 5f });
            check(Changes.Count == 0, "Chancellor adds no memory at the existing relation ceiling");
        }
        _relation = 0;
    }

    private static void CheckRawCallCoverage(Action<bool, string> check)
    {
        // Allow methods, not whole files: adding another raw gameplay write must fail this audit.
        var allowed = new HashSet<string> {
            "BellumCivile.RelationMemoryService.ApplyChangeWithDescriptor",
            "BellumCivile.Behaviors.DynamicRelationBehavior.SetRawRelation",
            "BellumCivile.CheatCommands.SetRulerRelationPlayer", "BellumCivile.CheatCommands.SetRulerRelationAI",
            "BellumCivile.Patches.MarriageRelationMemoryPatch.ApplyMarriageRelation",
            "BellumCivile.Patches.SettlementDailyRelationMemoryPatch.ApplySettlementRelation",
            "BellumCivile.Patches.NativeRelationCallLabels.Apply", "BellumCivile.Patches.SiegeAftermathRelationMemoryPatch.Apply"
        };
        var found = new List<string>();
        // Harmony ships Cecil; reading metadata also covers open generic methods and compiler-generated callbacks.
        using (var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(Service.Assembly.Location))
        {
            var pending = new Queue<Mono.Cecil.TypeDefinition>(assembly.MainModule.Types);
            while (pending.Count > 0)
            {
                var type = pending.Dequeue();
                foreach (var nested in type.NestedTypes) pending.Enqueue(nested);
                foreach (var method in type.Methods.Where(m => m.HasBody))
                foreach (var instruction in method.Body.Instructions)
                {
                    if (!(instruction.Operand is Mono.Cecil.MethodReference target)
                        || (instruction.OpCode.Code != Mono.Cecil.Cil.Code.Call && instruction.OpCode.Code != Mono.Cecil.Cil.Code.Callvirt)) continue;
                    if ((target.DeclaringType.FullName == typeof(ChangeRelationAction).FullName && target.Name.StartsWith("Apply", StringComparison.Ordinal))
                        || (target.DeclaringType.FullName == typeof(CharacterRelationManager).FullName && target.Name == "SetHeroRelation"))
                        found.Add(type.FullName + "." + method.Name);
                }
            }
        }
        string[] unexpected = found.Where(name => !allowed.Contains(name)).ToArray();
        check(unexpected.Length == 0, "No unlabelled direct gameplay relation writes: " + string.Join(", ", unexpected));
        check(found.Count == allowed.Count && allowed.SetEquals(found), "Raw-write exceptions are limited to the eight intentional engine/debug/native-wrapper calls");
    }
}
