using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Collections.Generic;
using System.Collections;
using BellumCivile;
using BellumCivile.Behaviors;
using BellumCivile.Patches;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;

internal static class Program
{
    private static int _checks;
    private static string _game;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _checks++;
        Console.WriteLine("PASS: " + name);
    }

    private static int Main(string[] args)
    {
        _game = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (string.IsNullOrWhiteSpace(_game)) _game = Environment.GetEnvironmentVariable("GameFolder");
        if (string.IsNullOrWhiteSpace(_game))
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFiles))
                _game = Path.Combine(programFiles, "Steam", "steamapps", "common", "Mount & Blade II Bannerlord");
        }
        if (string.IsNullOrWhiteSpace(_game) || !Directory.Exists(Path.Combine(_game, "bin", "Win64_Shipping_Client")))
        {
            Console.Error.WriteLine("Bannerlord was not found. Pass its installation path as the first argument or set the GameFolder environment variable.");
            return 1;
        }
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
        {
            string name = new AssemblyName(request.Name).Name + ".dll";
            string path = Directory.EnumerateFiles(_game, name, SearchOption.AllDirectories)
                .FirstOrDefault(p => p.Contains("Win64_Shipping_Client"));
            return path == null ? null : Assembly.LoadFrom(path);
        };
        try
        {
            if (args.Contains("--campaign-ui-startup")) CampaignUiStartupProbe.Run(Check);
            else if (args.Contains("--influence-budget"))
            {
                NpcInfluenceBudgetTests.Run(Check);
                Console.WriteLine($"{_checks} influence budget checks passed.");
            }
            else if (args.Contains("--fief-allocation"))
            {
                FiefAllocationTests.Run(Check);
                Console.WriteLine($"{_checks} fief allocation checks passed.");
            }
            else if (args.Contains("--vote-lobbying"))
            {
                VoteLobbyingArmyTests.Run(Check);
                CourtMandateEngineTests.Run(Check);
                Console.WriteLine($"{_checks} vote lobbying checks passed.");
            }
            else if (args.Contains("--council-nominations"))
            {
                CouncilNomineeBallotTests.Run(Check);
                CouncilRecoveryTests.Run(Check);
                CouncilSchedulingCleanupTests.Run(Check);
                Console.WriteLine($"{_checks} council nomination checks passed.");
            }
            else if (args.Contains("--vote-pledges"))
            {
                VotePledgeTests.Run(Check);
                Console.WriteLine($"{_checks} vote pledge checks passed.");
            }
            else if (args.Contains("--realm-names"))
            {
                RealmNameTests.Run(Check);
                Console.WriteLine($"{_checks} realm naming checks passed.");
            }
            else if (args.Contains("--title-name-cache"))
            {
                TitleNameCacheTests.Run(Check);
                Console.WriteLine($"{_checks} title-name cache checks passed.");
            }
            else if (args.Contains("--clan-finance-diagnostic"))
            {
                ClanFinanceDiagnosticTests.Run(Check);
                Console.WriteLine($"{_checks} clan-finance diagnostic checks passed.");
            }
            else if (args.Contains("--long-run-recovery"))
            {
                LongRunRecoveryTests.Run(Check);
                EstateSovereignRoutingTests.Run(Check);
                MarriageCombinedScoreTests.Run(Check);
                Console.WriteLine($"{_checks} long-run recovery checks passed.");
            }
            else if (args.Contains("--crown-household-recovery"))
            {
                CrownHouseholdRecoveryTests.Run(Check);
                Console.WriteLine($"{_checks} Crown household recovery checks passed.");
            }
            else if (args.Contains("--gameplay-relation-memories"))
            {
                GameplayRelationMemoryTests.Run(Check);
                RelationCauseLabelTests.Run(Check);
                RelationExpansionTests.Run(Check);
                RelationIdentityCacheTests.Run(Check);
                TreatyRelationOutcomeTests.Run(Check);
                ExpulsionRelationTests.Run(Check);
                Console.WriteLine($"{_checks} gameplay relation memory checks passed.");
            }
            else if (args.Contains("--exile-recovery"))
            {
                ExileRecoveryTests.Run(Check);
                FeudRecruitmentTests.Run(Check);
                Console.WriteLine($"{_checks} exile recovery and recruitment checks passed.");
            }
            else if (args.Contains("--expulsion-relations"))
            {
                ExpulsionRelationTests.Run(Check);
                PersonalBereavementTests.Run(Check);
                CourtReactionCompletionTests.Run(Check);
                RelationCauseLabelTests.Run(Check);
                Console.WriteLine($"{_checks} expulsion and relation checks passed.");
            }
            else if (args.Contains("--client-liberation"))
            {
                ClientLiberationResolveTests.Run(Check);
                ClientLiberationEngineTests.Run(Check);
                CourtLiberationEngineTests.Run(Check);
                DiplomacyOverviewTests.Run(Check, _game);
                Console.WriteLine($"{_checks} client liberation checks passed.");
            }
            else if (args.Contains("--hostage-pacts"))
            {
                HostagePactRulesTests.Run(Check);
                HostagePactRecordTests.Run(Check);
                HostagePactDurationTests.Run(Check);
                HostageTreatyTermTests.Run(Check);
                HostageDeliveryTests.Run(Check);
                HostagePresentationTests.Run(Check);
                HostageCustodyGuardTests.Run(Check);
                HostageTerminationTests.Run(Check);
                HostagePoliticsTests.Run(Check);
                Console.WriteLine($"{_checks} hostage pact checks passed.");
            }
            else Run();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void TestCourtActivityReceipts()
    {
        foreach (var type in new[] { typeof(CourtActivityRecord), typeof(CourtActivityTarget) })
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Check(fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
                type.Name + " persists every plan or execution receipt field");
            var ids = fields.Select(f => Convert.ToInt32(f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute")
                .ConstructorArguments[0].Value)).ToList();
            Check(ids.Distinct().Count() == ids.Count, type.Name + " save field IDs remain unique");
        }
        Check(typeof(CourtActivityTarget).GetField("Army").FieldType == typeof(Army), "Activity saves original native Army identity");
        Check(typeof(CourtAgendaRecord).GetField("Activity").GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute"),
            "Selected activity is saved with its agenda");
        var target = new CourtActivityTarget();
        var begin = AccessTools.Method(typeof(CourtActivityTarget), "TryBegin");
        Check((bool)begin.Invoke(target, null) && target.Started && !target.Completed, "Activity recipient seals before application");
        Check(!(bool)begin.Invoke(target, null), "Interrupted native recipient cannot be applied again");
        target.Completed = true;
        Check(!(bool)begin.Invoke(target, null), "Completed native recipient cannot be paid again");
        var assembly = typeof(CourtActivityRecord).Assembly;
        var catalog = (IEnumerable)AccessTools.Field(assembly.GetType("BellumCivile.CourtActivityCatalog"), "All").GetValue(null);
        var definitions = catalog.Cast<object>().ToList();
        Check(definitions.Count == 30, "All 30 mood activities remain available in production catalog");
        Check(definitions.Select(d => (string)AccessTools.Field(d.GetType(), "Id").GetValue(d)).Distinct().Count() == 30,
            "Production activity IDs are unique");
        Check(definitions.Count(d => (bool)AccessTools.Field(d.GetType(), "Positive").GetValue(d)) == 16,
            "Production catalog retains 16 positive and 14 negative effects");
        var execute = AccessTools.Method(assembly.GetType("BellumCivile.CourtSessionEvents"), "Execute");
        execute.Invoke(null, new object[] { null, null, null });
        execute.Invoke(null, new object[] { new CourtActivityRecord(), null, null });
        Check(true, "Missing or obsolete activity plans safely do nothing");
    }

    private static bool ActiveAccommodation(ref bool __result) { __result = true; return false; }

    private static void TestCourtAppeasement()
    {
        var fields = typeof(CourtAppeasementRecord).GetFields(BindingFlags.Public | BindingFlags.Instance);
        Check(fields.Length == 11 && fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
            "Appeasement saves target, quote, duration and attempt/application receipts");
        var ids = fields.Select(f => Convert.ToInt32(f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute")
            .ConstructorArguments[0].Value)).ToList();
        Check(ids.Distinct().Count() == ids.Count, "Appeasement save field IDs are unique");
        Check(AccessTools.Field(typeof(FactionObject), "CrownAccommodation").GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute"),
            "Faction retains accommodation independently of agenda retention");
        var faction = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
        var mood = AccessTools.Field(typeof(FactionObject), "_mood");
        var accommodation = AccessTools.Field(typeof(FactionObject), "CrownAccommodation");
        var harmony = new Harmony("bellum.test.appeasement");
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(CourtAppeasementRecord), "Active"),
                prefix: new HarmonyMethod(typeof(Program), nameof(ActiveAccommodation)));
            foreach (float underlying in new[] { -100f, -80, -60, -40, 0, 90, 100 })
            {
                mood.SetValue(faction, underlying);
                accommodation.SetValue(faction, new CourtAppeasementRecord());
                Check(faction.Mood == Math.Min(100, underlying + 20), "Actual faction getter includes accommodation: " + underlying);
                faction.Mood = faction.Mood;
                Check((float)mood.GetValue(faction) == underlying, "Capped actual mood roundtrip preserves underlying mood: " + underlying);
                accommodation.SetValue(faction, null);
                Check(faction.Mood == underlying, "Removing actual modifier restores underlying mood: " + underlying);
            }
            mood.SetValue(faction, -60f);
            accommodation.SetValue(faction, new CourtAppeasementRecord());
            faction.Mood -= 5;
            Check(faction.Mood == -45 && (float)mood.GetValue(faction) == -65, "Actual mood shock changes grievances independently of allowance");
        }
        finally { harmony.UnpatchAll("bellum.test.appeasement"); }
    }

    private static void TestCourtPeaceInitiative()
    {
        var assembly = typeof(CourtPeaceRecord).Assembly;
        var helper = assembly.GetType("BellumCivile.PoliticalInfluenceVoteHelper");
        var method = helper.GetMethods().Single(m => m.Name == "GetCommitment" && m.GetParameters().Length == 3
            && m.GetParameters()[1].ParameterType == typeof(float));
        float bonus = (float)AccessTools.Field(assembly.GetType("BellumCivile.CourtPeaceRules"), "AcceptanceBonus").GetRawConstantValue();
        foreach (float influence in new[] { 0f, 24, 25, 75, 150, 1000 })
        for (int utility = -100; utility <= 100; utility++)
        {
            object[] before = { (float)utility, influence, null };
            object[] after = { utility + bonus, influence, null };
            int oldVotes = (int)method.Invoke(null, before);
            int newVotes = (int)method.Invoke(null, after);
            string oldStance = before[2].ToString(), newStance = after[2].ToString();
            int oldSigned = oldStance == "Yay" ? oldVotes : oldStance == "Nay" ? -oldVotes : 0;
            int newSigned = newStance == "Yay" ? newVotes : newStance == "Nay" ? -newVotes : 0;
            Check(newSigned >= oldSigned && newVotes <= Math.Min(150, influence), "Peace bonus respects vote direction and affordability");
            Check(!(oldStance == "Nay" && newStance == "Yay"), "Fifteen utility cannot directly turn an opposing vote into support");
            if (influence == 150 && new[] { -30, -15, 0, 20, 40 }.Contains(utility))
                Console.WriteLine($"PEACE SIM: utility {utility} -> {utility + bonus}; {oldStance} {oldVotes} -> {newStance} {newVotes}");
        }
        var member = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var original = new WarScoreRecord("a|b", "a", "b", 10, null);
        var later = new WarScoreRecord("a|b", "a", "b", 90, null);
        var plan = new CourtPeaceRecord { War = original, ActivatedDay = 20, Activated = true };
        plan.Members.Add(member);
        var getBonus = AccessTools.Method(typeof(CourtPeaceRecord), "BonusFor");
        Check((float)getBonus.Invoke(plan, new object[] { original, member, 30d, 84d }) == 15, "Actual saved peace record yields the configured bonus");
        Check((float)getBonus.Invoke(plan, new object[] { later, member, 30d, 84d }) == 0, "Actual war record identity prevents same-pair reuse");
        Check((float)getBonus.Invoke(plan, new object[] { original, member, 85d, 84d }) == 0, "Actual record expires without daily maintenance");
        var fields = typeof(CourtPeaceRecord).GetFields();
        Check(fields.Length == 5 && fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
            "Peace record saves war identity, target, membership and activation");
        var loaded = new CourtPeaceRecord();
        foreach (var field in fields) field.SetValue(loaded, field.GetValue(plan));
        Check((float)getBonus.Invoke(loaded, new object[] { original, member, 30d, 84d }) == 15, "Field roundtrip retains native reference-based bonus");
        var behavior = new CourtAgendaBehavior();
        var records = (List<CourtAgendaRecord>)AccessTools.Field(typeof(CourtAgendaBehavior), "_agendas").GetValue(behavior);
        var agenda = new CourtAgendaRecord { State = CourtAgendaState.PursuingObjective,
            ObjectiveData = new CourtObjectiveRecord { Kind = "court_seek_peace" }, Peace = plan };
        records.Add(agenda);
        AccessTools.Method(typeof(CourtAgendaBehavior), "SettleClosedExecutiveObjectives").Invoke(behavior, null);
        Check(!agenda.ResultApplied && agenda.IsOngoingObjective && !agenda.IsFiled,
            "Actual closed-motion cleanup does not settle an active peace objective");
    }

    private static void TestCourtCampaignInitiative()
    {
        var target = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var other = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var member = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var plan = new CourtCampaignRecord { Target = target, ActivatedDay = 20, Activated = true };
        plan.Members.Add(member);
        var bonus = AccessTools.Method(typeof(CourtCampaignRecord), "BonusFor");
        Check((float)bonus.Invoke(plan, new object[] { target, member, 30d, 84d }) == 15, "Actual campaign record gives target-specific +15 utility");
        Check((float)bonus.Invoke(plan, new object[] { other, member, 30d, 84d }) == 0, "Campaign record does not assist another target");
        Check((float)bonus.Invoke(plan, new object[] { target, member, 85d, 84d }) == 0, "Campaign record expires even before maintenance");
        var fields = typeof(CourtCampaignRecord).GetFields();
        Check(fields.Length == 4 && fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
            "Campaign target, member snapshot and activation are saveable");
        var loaded = new CourtCampaignRecord();
        foreach (var field in fields) field.SetValue(loaded, field.GetValue(plan));
        Check((float)bonus.Invoke(loaded, new object[] { target, member, 30d, 84d }) == 15, "Campaign fields roundtrip with native references");
        var rules = typeof(CourtCampaignRecord).Assembly.GetType("BellumCivile.CourtCampaignRules");
        var result = AccessTools.Method(rules, "DeclarationResult");
        foreach (DeclareWarAction.DeclareWarDetail detail in Enum.GetValues(typeof(DeclareWarAction.DeclareWarDetail)))
        {
            bool authorized = detail == DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision;
            Check((CourtObjectiveState)result.Invoke(null, new object[] { true, authorized, 40d, 10d, 84d })
                == (authorized ? CourtObjectiveState.Succeeded : CourtObjectiveState.Cancelled), "Campaign provenance: " + detail);
            Check((CourtObjectiveState)result.Invoke(null, new object[] { false, authorized, 40d, 10d, 84d }) == CourtObjectiveState.Cancelled,
                "Enemy-first entry never rewards or penalizes campaign: " + detail);
        }
        var nativeOutcome = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(DeclareWarDecision), "ApplyChosenOutcome"));
        Check(nativeOutcome.Any(i => i.operand is MethodInfo m && m == AccessTools.Method(typeof(DeclareWarAction), "ApplyByKingdomDecision")),
            "Native successful war ballot dispatches the authorized declaration path");
        var body = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(DeclareWarAction), "ApplyInternal")).ToList();
        int relation = body.FindIndex(i => i.operand is MethodInfo m && m.DeclaringType == typeof(FactionManager) && m.Name == "DeclareWar");
        int dispatch = body.FindIndex(i => i.operand is MethodInfo m && m.Name == "OnWarDeclared");
        Check(relation >= 0 && dispatch > relation, "Native event runs after the hostility relation changes");
        var wrapper = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(DeclareWarAction), "ApplyByKingdomDecision"))
            .Where(i => i.opcode != System.Reflection.Emit.OpCodes.Nop).ToList();
        Check(wrapper[0].opcode == System.Reflection.Emit.OpCodes.Ldarg_0 && wrapper[1].opcode == System.Reflection.Emit.OpCodes.Ldarg_1,
            "Native authorized wrapper preserves attacker/defender argument order");
        var behavior = new CourtAgendaBehavior();
        var records = (List<CourtAgendaRecord>)AccessTools.Field(typeof(CourtAgendaBehavior), "_agendas").GetValue(behavior);
        var agenda = new CourtAgendaRecord { State = CourtAgendaState.PursuingObjective,
            ObjectiveData = new CourtObjectiveRecord { Kind = "court_support_campaign" }, Campaign = plan };
        records.Add(agenda);
        AccessTools.Method(typeof(CourtAgendaBehavior), "SettleClosedExecutiveObjectives").Invoke(behavior, null);
        Check(!agenda.ResultApplied && agenda.IsOngoingObjective && !agenda.IsFiled, "Campaign survives actual closed-motion cleanup");
        var helper = typeof(CourtCampaignRecord).Assembly.GetType("BellumCivile.PoliticalInfluenceVoteHelper");
        var commitment = helper.GetMethods().Single(m => m.Name == "GetCommitment" && m.GetParameters().Length == 3
            && m.GetParameters()[1].ParameterType == typeof(float));
        foreach (float influence in new[] { 0f, 25, 75, 150 })
        foreach (float utility in new[] { -30f, -15, 0, 20, 40 })
        {
            object[] args = { utility + 15, influence, null };
            int votes = (int)commitment.Invoke(null, args);
            Check(votes <= influence, "Campaign-support vote remains affordable");
            if (influence == 150) Console.WriteLine($"CAMPAIGN SIM: utility {utility} -> {utility + 15}; {args[2]} {votes}");
        }
    }

    private static void TestCouncilAppointmentReceipts()
    {
        var agendas = new CourtAgendaBehavior();
        var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var other = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var records = (List<CourtAgendaRecord>)AccessTools.Field(typeof(CourtAgendaBehavior), "_agendas").GetValue(agendas);
        var hasReservation = AccessTools.Method(typeof(CourtAgendaBehavior), "HasCouncilReservation");
        var record = new CourtAgendaRecord { Realm = realm, ObjectiveData = new CourtObjectiveRecord { Kind = "council_appointment", TargetId = "Marshal", ActionId = "fill" } };
        records.Add(record);
        foreach (CourtAgendaState state in Enum.GetValues(typeof(CourtAgendaState)))
        {
            record.State = state;
            bool expected = state == CourtAgendaState.Announced || state == CourtAgendaState.AwaitingPlayerDecision
                || state == CourtAgendaState.Deliberating || state == CourtAgendaState.Voting;
            Check((bool)hasReservation.Invoke(agendas, new object[] { realm, null }) == expected,
                "Council reservation follows agenda lifecycle: " + state);
            Check(!(bool)hasReservation.Invoke(agendas, new object[] { other, null }), "Council reservation stays realm-scoped: " + state);
            Check(!(bool)hasReservation.Invoke(agendas, new object[] { realm, record }), "Selected agenda can exclude its own reservation: " + state);
        }
        foreach (var name in new[] { "PreferredCouncilCandidate", "CouncilMotionId" })
            Check(typeof(CourtAgendaRecord).GetField(name).GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute"),
                "Council agenda identity is saved: " + name);
        var type = typeof(PrivyCouncilAppointmentDecision);
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        foreach (var name in new[] { "_appliedCandidate", "_aftermathApplied", "_outcomeAttempted", "CourtAgendaId" })
            Check(AccessTools.Field(type, name).GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute"),
                "Council outcome receipt is saveable: " + name);
        var ids = fields.SelectMany(f => f.GetCustomAttributesData().Where(a => a.AttributeType.Name == "SaveableFieldAttribute")
            .Select(a => Convert.ToInt32(a.ConstructorArguments[0].Value))).ToList();
        Check(ids.Distinct().Count() == ids.Count, "Council decision save IDs remain unique");
        var decision = (PrivyCouncilAppointmentDecision)FormatterServices.GetUninitializedObject(type);
        Check(AccessTools.Property(type, "AppliedCandidate").GetValue(decision) == null,
            "Old unresolved decisions start without a fabricated appointment receipt");
        AccessTools.Field(type, "_outcomeAttempted").SetValue(decision, true);
        decision.ApplyChosenOutcome(null);
        Check(AccessTools.Property(type, "AppliedCandidate").GetValue(decision) == null,
            "A repeated failed outcome cannot create a success receipt");
    }

    private static void TestConflictOutcomes()
    {
        var bypass = AccessTools.Property(typeof(ElectiveContestRecord), "BypassTestPowerGate");
        var canProceed = AccessTools.Method(typeof(ElectiveContestRecord).Assembly.GetType("BellumCivile.SuccessionChallengeRules"), "CanProceed");
        foreach (bool manual in new[] { false, true })
        foreach (bool forced in new[] { false, true })
        {
            var record = new ElectiveContestRecord { ManualTest = manual, ForcedThreeWayTest = forced,
                AccessionCompleted = true, EligibleDay = 10 };
            bool enabled = (bool)bypass.GetValue(record);
            Check(enabled == (manual && forced), "Forced power bypass requires both explicit test flags");
            Check((bool)canProceed.Invoke(null, new object[] { 1d, 1000d, 1d, enabled, true }) == enabled,
                "Only explicit forced fixture bypasses insufficient power");
            Check(!(bool)canProceed.Invoke(null, new object[] { 1d, 1000d, 1d, enabled, false }),
                "Forced fixture still requires a stronghold");
        }
        var behavior = new ConflictOutcomeBehavior();
        var type = typeof(ConflictOutcomeBehavior);
        var publish = AccessTools.Method(type, "Publish");
        var rebuild = AccessTools.Method(type, "Rebuild");
        var notices = (List<ConflictOutcomeNotice>)AccessTools.Field(type, "_notices").GetValue(behavior);
        var queue = (Queue<ConflictOutcomeNotice>)AccessTools.Field(type, "_pending").GetValue(behavior);
        var kinds = new[] { "victory", "claimant", "loyalist", "abdication", "independence", "peace", "satisfied",
            "collapse", "rival", "feud_claimant", "feud_holder", "feud_default", "feud_peace", "ended", "election_called" };
        foreach (var kind in kinds)
        {
            var notice = new ConflictOutcomeNotice { Id = kind, PlayerInvolved = true };
            foreach (var key in new[] { "LEADER", "RULER", "REALM", "FACTION", "SUCCESSION", "NEW_REALM", "DEMAND_RESULT",
                "VICTOR", "CLAIMANT_HOUSE", "HOLDER_HOUSE", "TITLE", "NEW_HOLDER", "HOLDER", "CAUSE", "DEPOSED", "CARETAKER", "DATE" })
                notice.Names[key] = "Test " + key;
            notices.Add(notice);
            publish.Invoke(behavior, new object[] { notice, kind, "A surviving war continues." });
            Check(notice.Ready && !notice.Acknowledged, "Involved result awaits acknowledgement: " + kind);
            Check(!string.IsNullOrWhiteSpace(notice.Title) && !notice.Body.Contains("{") && !notice.Chat.Contains("{"),
                "Outcome renders all name placeholders: " + kind);
            string body = notice.Body;
            publish.Invoke(behavior, new object[] { notice, "ended", "Must not replace a committed result." });
            Check(notice.Body == body, "Duplicate publication preserves committed outcome: " + kind);
        }
        Check(queue.Count == kinds.Length, "One popup per committed settlement");
        notices[0].Acknowledged = true;
        AccessTools.Field(type, "_active").SetValue(behavior, notices[1]);
        rebuild.Invoke(behavior, null);
        Check(queue.Count == kinds.Length - 1 && queue.Peek() == notices[1], "Load rebuild restores unacknowledged results in order");
        Check(AccessTools.Field(type, "_active").GetValue(behavior) == null, "Load rebuild clears transient active inquiry");
        var unfinished = new ConflictOutcomeNotice { Id = "unfinished", PlayerInvolved = true };
        notices.Add(unfinished);
        var observer = new ConflictOutcomeNotice { Id = "observer" };
        notices.Add(observer);
        publish.Invoke(behavior, new object[] { observer, "victory", null });
        Check(observer.Acknowledged, "Uninvolved observers never receive a popup");
        rebuild.Invoke(behavior, null);
        Check(queue.Count == kinds.Length - 1 && !queue.Contains(unfinished), "Incomplete settlements and observers stay out of the queue");
        Check((bool)AccessTools.Property(type, "HasPending").GetValue(behavior), "Result queue blocks subsequent tribunal UI");
        foreach (var notice in notices.Where(n => n.Ready)) notice.Acknowledged = true;
        rebuild.Invoke(behavior, null);
        Check(!(bool)AccessTools.Property(type, "HasPending").GetValue(behavior), "Acknowledged results never replay after load rebuild");
    }

    private static void TestSingleClaimantCollapse()
    {
        var gate = AccessTools.Method(typeof(FiefDeliberationBehavior), "CanDeliberateFiefs");
        foreach (var id in new[] { "empire_w", "empire_w_restored_conflict_test", "bc_treaty_release_test",
            "empire_w_rebels_challenge_elective_test", "bc_feud_test" })
        {
            var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
            AccessTools.Property(typeof(Kingdom), "StringId").SetValue(realm, id);
            bool expected = !id.Contains("_rebels_") && !id.StartsWith("bc_feud_");
            Check((bool)gate.Invoke(null, new object[] { realm }) == expected, "Compiled fief deliberation eligibility: " + id);
        }
        Check(!(bool)gate.Invoke(null, new object[] { null }), "Missing realm cannot queue fief deliberation");
        var crown = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var faction = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
        var conflict = new CivilWarConflictRecord { CrownRealm = crown };
        conflict.Sides.Add(new CivilWarSideRecord { Id = "sole", Faction = faction });
        conflict.Pairs.Add(new CivilWarPairRecord { AttackerSideId = "sole",
            DefenderSideId = (string)AccessTools.Field(typeof(CivilWarConflictRecord), "CrownSide").GetRawConstantValue() });
        var journal = new CivilWarConflictBehavior();
        AccessTools.Field(typeof(CivilWarConflictBehavior), "_conflicts").SetValue(journal, new List<CivilWarConflictRecord> { conflict });
        var capture = AccessTools.Method(typeof(CivilWarConflictBehavior), "CaptureCrownPairs");
        var pairs = (List<CivilWarPairTransferRecord>)capture.Invoke(journal, new object[] { conflict, faction });
        Check(pairs != null && pairs.Count == 0, "Sole retiring claimant yields a valid empty native-pair snapshot");
        conflict.Closed = true;
        Check(capture.Invoke(journal, new object[] { conflict, faction }) == null, "Closed conflict does not yield a valid empty snapshot");
    }

    private static void TestRestoredRealmTitleStyles()
    {
        var behavior = new FeudalTitleBehavior();
        var type = typeof(FeudalTitleBehavior);
        Dictionary<string, string> Map(string field) => (Dictionary<string, string>)AccessTools.Field(type, field).GetValue(behavior);
        var restored = Map("_independentRealmSourceTitleByKingdomId");
        var history = Map("_historicalRealmSovereignTitleByKingdomId");
        var sources = Map("_realmTitleStyleSources");
        history["empire_w"] = "bc_title_kingdom_empire_w";
        history["empire_w_restored"] = "bc_title_kingdom_empire_w";
        restored["empire_w_restored"] = "bc_title_kingdom_empire_w";
        restored["empire_w_restored_again"] = "bc_title_kingdom_empire_w";
        restored["independent_county"] = "county_test";
        restored["empire_w_restored_unrelated"] = "other_title";
        AccessTools.Method(type, "RecoverRestoredRealmTitleStyles").Invoke(behavior, null);
        Check(sources["empire_w_restored"] == "empire_w", "Existing restored mantle recovers Western Empire style");
        Check(sources["empire_w_restored_again"] == "empire_w", "Repeated restoration recovers original style");
        Check(!sources.ContainsKey("independent_county") && !sources.ContainsKey("empire_w_restored_unrelated"),
            "Independent realms and similar-looking IDs do not borrow a title preset");
        sources["empire_w_restored_again"] = "empire_s";
        AccessTools.Method(type, "RecoverRestoredRealmTitleStyles").Invoke(behavior, null);
        Check(sources["empire_w_restored_again"] == "empire_s", "Recovery preserves an already saved style identity");
        var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        AccessTools.Property(typeof(Kingdom), "StringId").SetValue(realm, "empire_w_restored");
        Check((string)AccessTools.Method(type, "GetRealmTitleStyleSourceId").Invoke(behavior, new object[] { realm }) == "empire_w",
            "Runtime lookup uses saved origin rather than restored engine ID");
        var configType = type.Assembly.GetType("BellumCivile.FeudalTitleConfig");
        var configField = AccessTools.Field(configType, "_instance");
        var oldConfig = configField.GetValue(null);
        var instance = AccessTools.Property(type, "Instance");
        var oldBehavior = instance.GetValue(null);
        try
        {
            instance.SetValue(null, behavior);
            var config = Activator.CreateInstance(configType, true);
            configField.SetValue(null, config);
            AccessTools.Method(configType, "LoadFile").Invoke(config, new object[] {
                Path.GetFullPath("ModuleData/bellum_title_styles_immersive.xml"), true, true });
            var helper = type.Assembly.GetType("BellumCivile.FeudalTitleDisplayHelper");
            var resolve = AccessTools.Method(helper, "ResolveKingdomRankStyle");
            var rank = resolve.Invoke(null, new object[] { FeudalTitleType.Kingdom, realm });
            Check((string)AccessTools.Field(rank.GetType(), "MaleRank").GetValue(rank) == "Autokrator",
                "Compiled title resolver restores the actual Western Empire XML rank");
            AccessTools.Property(typeof(Kingdom), "StringId").SetValue(realm, "empire_s");
            sources["empire_s"] = "empire_w";
            rank = resolve.Invoke(null, new object[] { FeudalTitleType.Kingdom, realm });
            Check((string)AccessTools.Field(rank.GetType(), "MaleRank").GetValue(rank) != "Autokrator",
                "Direct kingdom preset outranks an inherited style");
        }
        finally { configField.SetValue(null, oldConfig); instance.SetValue(null, oldBehavior); }
    }

    private static void TestElectionLobbying()
    {
        var retain = AccessTools.Method(typeof(ElectiveSuccessionRecord).Assembly.GetType("BellumCivile.ElectiveSuccessionRules"), "RetainPromise");
        var nominee = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var eligible = new List<Hero> { nominee };
        var dayTicks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = dayTicks.GetValue(null);
        dayTicks.SetValue(null, 1000L);
        try
        {
        foreach (string source in new[] { "bribed", "persuaded" })
        {
            var promise = new ElectiveCommitment { Nominee = nominee, Source = source, Until = CampaignTime.Days(100) };
            bool Keep(bool frozen, double day, List<Hero> candidates) =>
                (bool)retain.Invoke(null, new object[] { promise, candidates, frozen, day });
            Check(Keep(false, 99, eligible), source + " promise survives reconsideration before expiry");
            Check(!Keep(false, 100, eligible), source + " promise expires exactly at the evaluation boundary");
            Check(Keep(true, 101, eligible), source + " frozen ballot rebuild retains its original commitment");
            Check(!Keep(true, 99, new List<Hero>()), source + " promise releases an ineligible nominee even when frozen");
            Check(promise.Until.ToDays == 100 && promise.Source == source, source + " retention never extends or rewrites the promise");
            promise.Source = "natural";
            Check(!Keep(true, 99, eligible), "Natural support is not converted into a frozen promise");
            promise.Source = "player";
            Check(!Keep(true, 99, eligible), "Player choice remains separate from purchased promises");
        }
        }
        finally { dayTicks.SetValue(null, oldTicks); }
        var rules = typeof(ElectionLobbyingBehavior).Assembly.GetType("BellumCivile.ElectionLobbyingRules");
        var price = AccessTools.Method(rules, "Price");
        var difficulty = AccessTools.Method(rules, "Difficulty");
        var resistance = AccessTools.Method(rules, "Resistance");
        var openness = AccessTools.Method(rules, "Openness");
        int Quote(double share, double gap, double relation, int honor = 0, int generosity = 0) =>
            (int)price.Invoke(null, new object[] { share, gap, relation, honor, generosity });
        int Difficulty(double gap, double relation, int fit, bool self, bool trust = false) =>
            (int)difficulty.Invoke(null, new object[] { gap, relation, fit, self, trust });
        Check(Quote(5, 0, 60) == 61500, "Lobbying friendly minor quote");
        Check(Quote(10, 25, 0) == 150000, "Lobbying neutral elector quote");
        Check(Quote(20, 60, 30, -1, -1) == 250800, "Lobbying greedy major quote");
        Check(Quote(20, 50, 90) == 219000, "Lobbying self-voter quote");
        Check(Quote(20, 50, 0) == Quote(100, 50, 0), "Lobbying share premium capped at twenty percent");
        Check((double)resistance.Invoke(null, new object[] { 20d, 40d, false }) == 0, "Preferred target has no negative resistance");
        Check((double)resistance.Invoke(null, new object[] { 20d, 40d, true }) == 50, "Own candidacy resistance floor");
        Check((double)resistance.Invoke(null, new object[] { 500d, 0d, false }) == 100, "Resistance cap");
        Check((double)openness.Invoke(null, new object[] { 60d, 25d, 1, 1, 1, 0 }) < 35, "Principled friend refuses barter");
        Check((double)openness.Invoke(null, new object[] { -40d, 25d, -1, 0, -1, 1 }) >= 35, "Hostile opportunist accepts barter");
        Check(Difficulty(25, 60, 1, false) == -1, "Credible matching appeal receives one fit step");
        Check(Difficulty(25, 60, 1, false, true) == 0, "Personal trust does not double-count fit");
        Check(Difficulty(50, 90, 1, true) == 1, "Friendly self-voter retains Hard floor");
        Check(Difficulty(10, 30, 0, false) == 0 && Difficulty(11, 30, 0, false) == 1,
            "First resistance difficulty boundary");
        Check(Difficulty(30, 30, 0, false) == 1 && Difficulty(31, 30, 0, false) == 2,
            "Second resistance difficulty boundary");
        Check(Difficulty(60, 30, 0, false) == 2 && Difficulty(61, 30, 0, false) == 3,
            "Third resistance difficulty boundary");
        foreach (double relation in new[] { -100d, 0, 30, 59, 60, 89, 90, 100 })
        foreach (double share in new[] { 0d, 5, 10, 20, 100 })
        {
            int previous = 0;
            foreach (double gap in new[] { 0d, 10, 11, 30, 31, 50, 60, 61, 100 })
            {
                int next = Quote(share, gap, relation);
                Check(next >= previous && next > 0, "Compiled lobbying price monotonic and positive");
                previous = next;
                Check(Difficulty(gap, relation, 2, true) >= 1, "Compiled self-vote difficulty never below Hard");
                Check(Difficulty(gap, relation, 2, false, true) == Difficulty(gap, relation, -2, false, true),
                    "Trust appeal ignores thematic fit in compiled rules");
            }
        }
        var barter = typeof(TaleWorlds.CampaignSystem.BarterSystem.BarterManager);
        var target = AccessTools.Method(barter, "ApplyAndFinalizePlayerBarter");
        Check(target != null && target.GetParameters().Select(p => p.Name).SequenceEqual(new[] { "offererHero", "otherHero", "barterData" }),
            "Native barter finalization matches Harmony argument names");
        Check(AccessTools.Method(barter, "CancelAndFinalizePlayerBarter") != null, "Native cancellation path exists");
        var patch = typeof(ElectionLobbyingBehavior).Assembly.GetType("BellumCivile.Patches.ElectionVoteBarterPatch");
        Check(AccessTools.Method(patch, "Prefix")?.ReturnType == typeof(bool), "Electoral barter can suppress stale payment");
        var fields = typeof(ElectionLobbyingBehavior).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        Check(fields.Any(f => f.Name == "_attempts" && f.FieldType == typeof(Dictionary<string, CampaignTime>)),
            "Persuasion cycle attempts use existing saveable container");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        TestCouncilAppointmentReceipts();
        TestCourtActivityReceipts();
        TestCourtAppeasement();
        TestCourtPeaceInitiative();
        TestCourtCampaignInitiative();
        CourtSubjugationEngineTests.Run(Check);
        CourtClaimEngineTests.Run(Check);
        CourtDynasticEngineTests.Run(Check);
        CourtDynasticPursuitTests.Run(Check);
        CourtProtectionEngineTests.Run(Check);
        CourtProtectionClientageTests.Run(Check);
        CourtTradeEngineTests.Run(Check);
        CourtTradePursuitTests.Run(Check);
        CourtTitleGrantEngineTests.Run(Check);
        CourtClientGrantTests.Run(Check);
        CrownActionCooldownTests.Run(Check);
        PeaceReconsiderationTests.Run(Check);
        PeaceFeasibilityTests.Run(Check);
        HostagePactRulesTests.Run(Check);
        HostagePactRecordTests.Run(Check);
        HostagePactDurationTests.Run(Check);
        HostageCustodyGuardTests.Run(Check);
        HostagePresentationTests.Run(Check);
        DiplomacyOverviewTests.Run(Check, _game);
        DiplomacyPactSettingsTests.Run(Check);
        HostageTerminationTests.Run(Check);
            HostagePoliticsTests.Run(Check);
            HostageTreatyTermTests.Run(Check);
            HostageDeliveryTests.Run(Check);
        PolicyCardStanceTests.Run(Check);
        CourtDynamicPolicyTests.Run(Check);
        TitleTenurePresentationTests.Run(Check);
        ReversibleWarScoreTests.Run(Check);
        FeudObjectiveTickingTests.Run(Check);
        PeaceReadinessTests.Run(Check);
        PeaceCouncilReadinessTests.Run(Check);
        PeaceTreatyQuorumTests.Run(Check);
        OverBudgetDraftTests.Run(Check);
        WhitePeaceFallbackTests.Run(Check);
        TreatyWinnerLeverageTests.Run(Check);
        TreatyDraftReadScopeTests.Run(Check);
        RealmUnionTransferPlanTests.Run(Check);
        RealmUnionJournalTests.Run(Check);
        RealmUnionCrownTests.Run(Check);
        RealmUnionObligationTests.Run(Check);
        RealmUnionTradeTests.Run(Check);
        RealmUnionAllianceTests.Run(Check);
        RealmUnionAgreementProgressTests.Run(Check);
        RealmUnionTributeTests.Run(Check);
        RealmUnionTributeProjectionTests.Run(Check);
        RealmUnionLegacyTributeProjectionTests.Run(Check);
        RealmUnionClientTests.Run(Check);
        RealmUnionRetirementTests.Run(Check);
        RealmUnionCompletionTests.Run(Check);
        RealmUnionSequenceTests.Run(Check);
        RealmUnionPublicationTests.Run(Check);
        RealmUnionAuditTests.Run(Check);
        EncyclopediaConceptTests.Run(Check);
        RealmUnionReconciliationTests.Run(Check);
        FeudalCrownPartitionPlanTests.Run(Check);
        CrownPartitionResidenceTests.Run(Check);
        CrownPartitionAllegianceTests.Run(Check);
        CrownPartitionPromotionTests.Run(Check);
        CrownPartitionCaptureTests.Run(Check);
        CrownPartitionFinalizationTests.Run(Check);
        PartitionClaimSnapshotTests.Run(Check);
        PartitionEstateManifestTests.Run(Check);
        PartitionEstateSettlementTests.Run(Check);
        CrownPartitionBatchTests.Run(Check);
        CrownPartitionReconciliationTests.Run(Check);
        CrownEstateDeliveryTests.Run(Check);
        CrownPartitionHouseTransferTests.Run(Check);
        CrownPartitionGovernmentTests.Run(Check);
        VillageRaidCreditTests.Run(Check);
        ClientOccupationTreatyTests.Run(Check);
        CourtRallyEngineTests.Run(Check);
        CourtMandateEngineTests.Run(Check);
        RoyalPeaceEngineTests.Run(Check);
        CourtRoyalPeaceEngineTests.Run(Check);
        ClientLiberationResolveTests.Run(Check);
        ClientLiberationEngineTests.Run(Check);
        EstateRecoveryTests.Run(Check);
        CourtLiberationEngineTests.Run(Check);
        CouncilSchedulingCleanupTests.Run(Check);
        CouncilRecoveryTests.Run(Check);
        CourtMeetingCleanupTests.Run(Check);
        CourtPoliticalReactionTests.Run(Check);
        CourtTribunalReactionTests.Run(Check);
        CourtReactionCompletionTests.Run(Check);
        CourtHistoryPresentationTests.Run(Check);
        DeJureDriftConflictTests.Run(Check);
        IndependentDriftTests.Run(Check);
        TitleReorganizationTests.Run(Check);
        FeudMercenaryTests.Run(Check);
        AllocationCustodyTests.Run(Check);
        InternalPeaceTests.Run(Check);
        FeudRecruitmentTests.Run(Check);
        CourtMembershipRelationTests.Run(Check);
        ClaimRenunciationTests.Run(Check);
        FeudWithdrawalTests.Run(Check);
        CouncilCaptivityTests.Run(Check);
        TreatyRelationOutcomeTests.Run(Check);
        FabricationSetbackTests.Run(Check);
        HeroDescriptionTests.Run(Check);
        LandlessLeaderStyleTests.Run(Check);
        MercenaryLeaderStyleTests.Run(Check);
        ClanMemberLocationHintTests.Run(Check);
        CourtLeadershipHandoverTests.Run(Check);
        CourtNominationMandateTests.Run(Check);
        CourtMotionNavigationTests.Run(Check);
        CouncilCompetencePresentationTests.Run(Check);
        CouncilPayrollTests.Run(Check);
        RelationIdentityCacheTests.Run(Check);
        RelationExpansionTests.Run(Check);
        RelationCauseLabelTests.Run(Check);
        ExpulsionRelationTests.Run(Check);
        PersonalKinshipTests.Run(Check);
        GameplayRelationMemoryTests.Run(Check);
        PersonalBereavementTests.Run(Check);
        SettlementCauseLabelTests.Run(Check);
        RelationPerformanceTests.Run(Check);
        FactionAgendaLayoutTests.Run(Check);
        PlayerCrownBusinessTests.Run(Check);
        var retiredForeignScheduler = new BellumCivile.Behaviors.ForeignPolicyBehavior();
        foreach (bool force in new[] { false, true })
        {
            Check(!retiredForeignScheduler.TryRunRulerForeignPolicyProposal(null, force, out string retiredReport)
                && retiredReport.Contains("retired") && retiredReport.Contains("normal war and peace"),
                "Retired foreign scheduler remains harmless with force=" + force);
            Check(retiredForeignScheduler.GetActiveWars().Count == 0,
                "Retired foreign command cannot manufacture an active war");
        }
        TestCoalitionSuccessionState();
        TestCourtSessionReports();
        TestCourtObjectiveReports();
        TestCourtAgendaDeadlines();
        TestConflictOutcomes();
        TestSingleClaimantCollapse();
        TestRestoredRealmTitleStyles();
        TestElectionLobbying();
        TestPlayerHeirPresentation();
        TestElectiveContestFoundation();
        TestElectiveContestPledges();
        TestCivilWarContinuation();
        TestCivilWarConflictJournal();
        TestCivilWarCrownTransfer();
        TestCivilWarRivalry();
        TestRivalCrownPromotion();
        TestControlledElectionGate();
        TestCoordinatedElectionStartup();
        Check(!Enum.IsDefined(typeof(FactionType), 3), "Retired redistribution faction value is not an active type");
        Check((int)FactionType.InstallRuler == 2 && (int)FactionType.Royalists == 4 && (int)FactionType.Liberty == 7,
            "Removing a retired faction does not renumber surviving types");
        Check(!Enum.IsDefined(typeof(ExileCause), 4) && !Enum.IsDefined(typeof(ExileCause), 9)
            && (int)ExileCause.Treason == 5 && (int)ExileCause.LoyalistInstallRuler == 8,
            "Retired exile causes leave remaining identities unchanged");
        Check((int)ExileCause.Expulsion == 10 && (int)ExileCause.KingdomDestroyed == 11
            && (int)ExileCause.VoluntaryDeparture == 12,
            "New exile causes use fresh values without reusing retired save identities");
        var parseFaction = AccessTools.Method(typeof(CheatCommands), "TryParseCourtFaction");
        foreach (string invalid in new[] { "FiefRedistribution", "3", "999", "-1" })
            Check(!(bool)parseFaction.Invoke(null, new object[] { invalid, default(FactionType) }),
                "Console rejects removed or undefined faction type: " + invalid);
        foreach (string valid in new[] { "InstallRuler", "Abdication", "Independence", "2", "Traditionalists" })
            Check((bool)parseFaction.Invoke(null, new object[] { valid, default(FactionType) }),
                "Supported faction command remains valid: " + valid);
        var cadetGuard = typeof(SuccessionChallengeRecord).Assembly.GetType("BellumCivile.Patches.DiplomacyCadetFiefCompatibilityPatch");
        var protectFief = AccessTools.Method(cadetGuard, "ProtectLastFief");
        foreach (int count in new[] { 0, 1, 2, 3 })
        {
            Check((bool)protectFief.Invoke(null, new object[] { "bc_challenge_test", count }) == (count <= 1),
                "Diplomacy cadet safeguard protects the last holding only; count=" + count);
            Check(!(bool)protectFief.Invoke(null, new object[] { "clan_vlandia_1", count }),
                "Diplomacy ordinary-house corruption routine remains unchanged; count=" + count);
        }
        Check(!(bool)protectFief.Invoke(null, new object[] { null, 1 }), "Missing clan identity does not invent cadet protection");
        var target = AccessTools.Method(typeof(KillCharacterAction), "ApplyInternal");
        var original = PatchProcessor.GetOriginalInstructions(target);
        var modified = HereditaryDeathAccessionPatch.Transpiler(original).ToList();
        var crown = AccessTools.PropertyGetter(typeof(Kingdom), nameof(Kingdom.RulingClan));
        var death = AccessTools.Method(typeof(KillCharacterAction), "MakeDead");
        var after = AccessTools.Method(typeof(HereditaryDeathAccessionPatch), "AfterDeath");
        Check(original.Count(x => x.Calls(crown)) == 1, "Exactly one native Crown branch anchor");
        Check(modified.Count(x => x.Calls(crown)) == 0, "Only the death-action Crown getter is substituted");
        Check(modified.Count(x => x.Calls(after)) == 1, "Exactly one accession callback");
        Check(modified.FindIndex(x => x.Calls(after)) == modified.FindIndex(x => x.Calls(death)) + 2,
            "Accession runs after MakeDead, before native clan destruction");
        Check(original.Sum(x => x.labels.Count) == modified.Sum(x => x.labels.Count), "Branch labels retained");
        Check(original.Sum(x => x.blocks.Count) == modified.Sum(x => x.blocks.Count), "Exception blocks retained");
        bool rejected = false;
        try { HereditaryDeathAccessionPatch.Transpiler(original.Where(x => !x.Calls(crown))).ToList(); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Changed engine anchor fails explicitly instead of partly patching death");
        var harmony = new Harmony("BellumCivile.Tests.HereditaryAccession");
        foreach (var patch in new[] { typeof(HereditaryDeathSnapshotPatch), typeof(HereditaryPlayerDeathSnapshotPatch),
            typeof(HereditaryDeathAccessionPatch), typeof(HereditaryInterregnumHousePatch), typeof(KingSelectionAIPatch),
            typeof(HereditaryAbdicationButtonPatch), typeof(HereditaryAbdicationAvailabilityPatch), typeof(ElectiveAbdicationButtonPatch) })
        {
            Check(harmony.CreateClassProcessor(patch).Patch().Count > 0, "Harmony compiles " + patch.Name);
        }
        harmony.UnpatchAll(harmony.Id);
        var fields = typeof(CrownAccessionRecord).GetFields();
        Check(fields.All(x => x.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
            "All accession journal fields carry save metadata");
        Check(fields.Select(x => x.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute")
            .ConstructorArguments[0].Value).Distinct().Count() == fields.Length, "Journal save IDs are unique");
        var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var otherRealm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var ruler = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var otherRuler = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var decision = (KingSelectionKingdomDecision)FormatterServices.GetUninitializedObject(typeof(KingSelectionKingdomDecision));
        var otherDecision = (KingSelectionKingdomDecision)FormatterServices.GetUninitializedObject(typeof(KingSelectionKingdomDecision));
        var record = new CrownAccessionRecord { Realm = realm, Predecessor = ruler };
        var behavior = new CrownAccessionBehavior();
        AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions").SetValue(behavior, new List<CrownAccessionRecord> { record });
        Check(behavior.OwnsDeath(realm, ruler), "Journal claims only the recorded ruler death");
        Check(!behavior.OwnsDeath(otherRealm, ruler) && !behavior.OwnsDeath(realm, otherRuler), "Unrelated deaths stay native");
        Check(behavior.IsPending(realm) && !behavior.IsPending(otherRealm), "Pending accession is realm-scoped");
        Check(behavior.GetEmergency(null) == null, "Unassigned decision cannot match an emergency");
        record.Emergency = true;
        record.EmergencyElection = decision;
        Check(behavior.GetEmergency(decision) == record && behavior.GetEmergency(otherDecision) == null,
            "Emergency authorization requires exact saved decision identity");
        record.Completed = true;
        record.OutcomeApplied = true;
        Check(!behavior.IsPending(realm), "Completed accession releases the law-change gate");
        Check(behavior.GetEmergency(decision)?.OutcomeApplied == true, "Completed outcome retains its replay guard");
        Check(behavior.OwnsDeath(realm, ruler), "Duplicate death retains native-election suppression");
        record.VoluntaryAbdication = true;
        Check(!behavior.OwnsDeath(realm, ruler), "Completed voluntary abdication does not consume a later death");
        TestAbdicationPreview(realm, ruler, otherRuler);
        TestPrimaryInheritanceShare(ruler, otherRuler);
        TestRegencyContinuity();
        TestCadetFoundingMembers();
        TestForcedAbdication();
        TestHereditaryRealmLine();
        TestIncomingCrownHouse();
        TestSeparateSpouseHouseholds();
        TestHouseholdFoundation();
        TestForeignAccession();
        TestCrownClaimCarrier();
        TestRoyalCadetNames();
        TestTitlePackageParity();
        TestMarriageCadetRetirement();
        TestBilateralMarriage();
        MarriageMatchmakingTests.Run(Check);
        MarriageProspectTests.Run(Check);
        MarriageHealthCacheTests.Run(Check);
        MarriageClaimForecastTests.Run(Check);
        MarriageCombinedScoreTests.Run(Check);
        CourtTradeSaveDefinitionTests.Run(Check);
        CadetRegencyFoundingTests.Run(Check);
        ClanFinanceDiagnosticTests.Run(Check);
        EstateSovereignRoutingTests.Run(Check);
        LongRunRecoveryTests.Run(Check);
        CrownHouseholdRecoveryTests.Run(Check);
        TestRealmLawGroups();
        TestRealmMeritProfiles();
        TestStandingElections();
        TestFinalistSelfSupport();
        TestHereditaryLoyalty();
        TestElectiveAcceptance();
        TestSuccessionChallenges();
        TestSuccessionPledgesAndTribunals();
        TestHeirLoyaltyCommand();
        TestHereditaryAllegiance();
        TestSuccessionPretenders();
        TestChallengeSchedule();
        TestChallengeWording();
        TestDispositionPresentation();
        TestElectoralBacking();
        foreach (var type in new[] { typeof(CrossClanEstateRecord), typeof(CrossClanEstateShare) })
        {
            var savedFields = type.GetFields();
            Check(savedFields.All(f => f.GetCustomAttributesData().Count(a => a.AttributeType.Name == "SaveableFieldAttribute") == 1),
                type.Name + " persists all estate and delivery receipts");
            Check(savedFields.Select(f => f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute")
                .ConstructorArguments[0].Value).Distinct().Count() == savedFields.Length,
                type.Name + " uses unique field IDs");
        }
        Console.WriteLine($"{_checks} engine-contract checks passed. No live campaign/save roundtrip was run.");
    }

    private static void TestFinalistSelfSupport()
    {
        var rules = typeof(CrownAccessionBehavior).Assembly.GetType("BellumCivile.ElectiveSuccessionRules");
        var tally = AccessTools.Method(rules, "Tally");
        var refresh = AccessTools.Method(rules, "RefreshWeights");
        var candidates = Enumerable.Range(0, 4).Select(i =>
        {
            var hero = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            AccessTools.Property(typeof(Hero), "StringId").SetValue(hero, "self_candidate_" + i);
            return hero;
        }).ToArray();
        var record = new ElectiveSuccessionRecord { Votes = candidates.Select((hero, i) => new ElectiveCommitment
        {
            Clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan)), Speaker = hero,
            Nominee = candidates[0], Source = "natural", Weight = new[] { 100, 20, 10, 40 }[i],
            Preferences = candidates.Select((candidate, rank) => new ElectivePreference
                { Candidate = candidate, Law = 50, Score = rank == 0 ? 90 : 50 - rank }).ToList()
        }).ToList() };
        tally.Invoke(null, new object[] { record });
        Check(record.Finalists.SequenceEqual(candidates.Take(3)), "Self-support does not alter nomination-based finalist selection");
        Check(record.Support(candidates[0]) == 140 && record.Support(candidates[1]) == 20
            && record.Support(candidates[2]) == 10, "Each natural NPC finalist contributes its own house's weight");
        Check(record.Votes.All(v => v.Nominee == candidates[0]) && record.Votes[3].Supported == candidates[0],
            "Natural nominations remain intact and nonfinalists retain their preference");
        tally.Invoke(null, new object[] { record });
        Check(record.Finalists.SequenceEqual(candidates.Take(3)) && record.TotalWeight == record.Finalists.Sum(record.Support),
            "Repeated self-support tally stays stable and conserves weight");
        record.Votes[1].Source = "bribed";
        record.Votes[2].Source = "player";
        tally.Invoke(null, new object[] { record });
        Check(record.Votes[1].Supported == candidates[0] && record.Votes[2].Supported == candidates[0],
            "Finalist self-support cannot override a bribe or player endorsement");
        record.Votes[1].Source = "persuaded";
        record.Votes[2].Nominee = null;
        tally.Invoke(null, new object[] { record });
        Check(record.Votes[1].Supported == candidates[0] && record.Votes[2].Supported == null,
            "Persuaded finalists honor their promise and player finalists may abstain");
        record.Votes[1].Source = record.Votes[2].Source = "natural";
        record.Votes[2].Nominee = candidates[0];
        var weights = record.Votes.ToDictionary(v => v.Clan, v => v.Weight);
        Func<Clan, double> power = clan => weights[clan];
        Check((bool)refresh.Invoke(null, new object[] { record, power }) && record.Votes[1].Supported == candidates[1],
            "Existing standing ballots repair self-support without waiting for changed power or a new term");
        Check(!(bool)refresh.Invoke(null, new object[] { record, power }), "Repaired unchanged self-support does not retally repeatedly");
        record.Votes[1].Supported = candidates[0];
        record.Frozen = true;
        var finalists = record.Finalists.ToArray();
        AccessTools.Method(typeof(ElectiveSuccessionBehavior), "RetallyFrozen").Invoke(null, new object[] { record });
        Check(record.Votes[1].Supported == candidates[1] && record.Finalists.SequenceEqual(finalists)
            && record.Votes.All(v => v.Weight == weights[v.Clan]), "Frozen tally applies self-support without changing finalists or captured weights");
        record.Frozen = false;
        record.Votes[3].Nominee = candidates[3]; record.Votes[3].Weight = 400;
        tally.Invoke(null, new object[] { record });
        Check(!record.Finalists.Contains(candidates[2]) && record.Votes[2].Supported == candidates[0],
            "A former finalist resumes ordinary preference when leaving the ballot");
    }

    private static void TestElectoralBacking()
    {
        var type = typeof(CrownAccessionBehavior).Assembly.GetType("BellumCivile.ElectiveAcceptanceRules");
        var backers = AccessTools.Method(type, "ExpectedBackingClans");
        var candidate = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var winner = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var own = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var royal = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var player = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var npc = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        AccessTools.Field(typeof(Hero), "_clan").SetValue(candidate, own);
        AccessTools.Field(typeof(Hero), "_clan").SetValue(winner, royal);
        var votes = new List<ElectiveCommitment>
        {
            new ElectiveCommitment { Clan = own, Supported = candidate },
            new ElectiveCommitment { Clan = player, Source = "player", Supported = candidate },
            new ElectiveCommitment { Clan = npc, Source = "natural", Supported = candidate },
            new ElectiveCommitment { Clan = royal, Supported = winner }
        };
        Func<Hero, HashSet<Clan>> expected = hero => (HashSet<Clan>)backers.Invoke(null, new object[] { hero, votes });
        var power = new Dictionary<Clan, double> { [own] = 300, [player] = 1000, [npc] = 400, [royal] = 2000 };
        Check(expected(candidate).Sum(c => power[c]) == 1700,
            "Public player endorsement contributes its full 1000 power alongside NPC endorsement");
        votes.Add(votes[0]); votes.Add(votes[1]);
        Check(expected(candidate).Count == 3 && expected(candidate).Sum(c => power[c]) == 1700,
            "Own self-vote and duplicate endorsements cannot duplicate expected clan power");
        votes[1].Supported = winner;
        Check(expected(candidate).Sum(c => power[c]) == 700 && expected(winner).Sum(c => power[c]) == 3000,
            "Switching endorsement moves player power to projected loyalists");
        votes[1].Supported = null;
        Check(!expected(candidate).Contains(player) && !expected(winner).Contains(player),
            "Player abstention contributes no assumed armed backing to either candidate");
        votes[0].Supported = winner;
        Check(expected(candidate).Contains(own), "A challenger retains its own house even without a self-endorsement");
        Check(votes[1].Source == "player" && votes[2].Source == "natural",
            "Expected backing leaves player and NPC voting records unchanged");
    }

    private static void TestDispositionPresentation()
    {
        var type = typeof(CheatCommands).Assembly.GetType("BellumCivile.UI.VanillaTabs.Kingdoms.Succession.SuccessionDisposition");
        var band = AccessTools.Method(type, "Band");
        var color = AccessTools.Method(type, "ColorHex");
        foreach (var pair in new[] { Tuple.Create(0.0, 0), Tuple.Create(24.999, 0), Tuple.Create(25.0, 1),
            Tuple.Create(49.999, 1), Tuple.Create(50.0, 2), Tuple.Create(74.999, 2), Tuple.Create(75.0, 3), Tuple.Create(100.0, 3) })
        {
            Check((int)band.Invoke(null, new object[] { pair.Item1 }) == pair.Item2, "Disposition uses strict 25/50/75 boundaries");
            Check((string)color.Invoke(null, new object[] { (double?)pair.Item1 }) ==
                new[] { "#FF0000FF", "#FF8C00FF", "#F1D8A4FF", "#82E06AFF" }[pair.Item2], "Disposition band uses the matching portrait color");
        }
        Check((string)color.Invoke(null, new object[] { null }) == "#F1D8A4FF", "Unknown disposition stays neutral, not steadfast or disloyal");
        var value = AccessTools.Method(type, "Value");
        Check((string)value.Invoke(null, new object[] { 50.0 }) == "Loyal (50%)", "Portrait and tooltip share the combined disposition value");
        Check(!((string)value.Invoke(null, new object[] { 24.999 })).Contains("25%"), "Rounding cannot display Disloyal at 25 percent");
    }

    private static void TestChallengeWording()
    {
        var demand = AccessTools.Method(typeof(SuccessionChallengeBehavior), "DemandDescription");
        foreach (bool ruler in new[] { false, true })
        {
            string prefix = ruler ? "BC_Challenge_Ruler" : "BC_Challenge_Call";
            foreach (bool grant in new[] { false, true })
                Check(((string)demand.Invoke(null, new object[] { SuccessionChallengeDemand.Crown, grant, ruler }))
                    .Contains(prefix + "Crown}"), "Crown demand wording never becomes an inheritance demand");
            Check(((string)demand.Invoke(null, new object[] { SuccessionChallengeDemand.InheritanceFirst, true, ruler }))
                .Contains(prefix + "Grant}"), "Excluded heir requests a landed grant");
            Check(((string)demand.Invoke(null, new object[] { SuccessionChallengeDemand.InheritanceFirst, false, ruler }))
                .Contains(prefix + "Inheritance}"), "Landed heir requests their rightful inheritance");
        }
        var power = AccessTools.Method(typeof(SuccessionChallengeBehavior), "PowerDescription");
        foreach (var pair in new[] { Tuple.Create(0.0, "Outmatched"), Tuple.Create(49.0, "Outmatched"),
            Tuple.Create(50.0, "Weaker"), Tuple.Create(89.0, "Weaker"), Tuple.Create(90.0, "Even"),
            Tuple.Create(100.0, "Even"), Tuple.Create(110.0, "Even"), Tuple.Create(111.0, "Stronger"),
            Tuple.Create(199.0, "Stronger"), Tuple.Create(200.0, "Dominant") })
            Check(((string)power.Invoke(null, new object[] { pair.Item1, 100.0 })).Contains("Power" + pair.Item2 + "}"),
                "Narrative power bands handle their boundaries");
        Check(((string)power.Invoke(null, new object[] { 1.0, 0.0 })).Contains("PowerDominant}"), "No loyalist power is handled without division");
        foreach (double value in new[] { 0.0, -1, double.NaN, double.PositiveInfinity })
            Check(((string)power.Invoke(null, new object[] { value, 0.0 })).Contains("PowerUncertain}"), "Unknown or empty power is not described as parity");
    }

    private static void TestChallengeSchedule()
    {
        var type = typeof(CheatCommands).Assembly.GetType("BellumCivile.SuccessionChallengeSchedule");
        var method = AccessTools.Method(type, "IsDue");
        Func<string, int, int, bool> due = (id, day, last) => (bool)method.Invoke(null, new object[] { id, day, last });
        foreach (string id in new[] { "vlandia", "sturgia", "empire", "battania", "player_realm" })
        {
            int last = -7;
            var days = new List<int>();
            for (int day = 0; day < 70; day++)
            {
                if (!due(id, day, last)) continue;
                days.Add(day); last = day;
                Check(!due(id, day, last), "Saved check receipt blocks same-day replay including reload");
                Check(!due(id, day - 1, last), "Clock rewind does not trigger another check");
            }
            Check(days.Count == 10 && days.Skip(1).Select((day, i) => day - days[i]).All(d => d == 7),
                "Each realm is checked exactly once per seven days");
            Check(due(id, last + 7, last), "Next scheduled check resumes normally after receipt");
        }
        Check(!due(null, 7, -7) && !due("", 7, -7) && !due("vlandia", -1, -7), "Invalid schedule inputs are rejected");
        Check(new[] { "vlandia", "sturgia", "empire", "battania", "player_realm" }
            .Select(id => Enumerable.Range(0, 7).Single(d => due(id, d, -7))).Distinct().Count() > 1,
            "Realm checks are staggered across days; slot collisions are permitted");
    }

    private static void TestSuccessionPretenders()
    {
        var type = typeof(CheatCommands).Assembly.GetType("BellumCivile.UI.VanillaTabs.Kingdoms.Succession.SuccessionPretender");
        var order = AccessTools.Method(type, "Order");
        Func<string, Hero> hero = id =>
        {
            var h = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            AccessTools.Property(typeof(Hero), "StringId").SetValue(h, id);
            return h;
        };
        Func<Hero, FeudalClaimStrength, double, object> entry = (h, strength, power) =>
        {
            object item = Activator.CreateInstance(type, true);
            AccessTools.Field(type, "Hero").SetValue(item, h);
            AccessTools.Field(type, "Strength").SetValue(item, strength);
            AccessTools.Field(type, "Power").SetValue(item, power);
            return item;
        };
        var king = hero("king"); var heir = hero("heir"); var weak = hero("weak");
        var strongA = hero("strong_a"); var strongB = hero("strong_b"); var strongC = hero("strong_c");
        var candidates = Array.CreateInstance(type, 8);
        var source = new[] {
            entry(king, FeudalClaimStrength.Strong, 99999), entry(heir, FeudalClaimStrength.Strong, 99999),
            entry(weak, FeudalClaimStrength.Weak, 9000), entry(strongC, FeudalClaimStrength.Strong, 100),
            entry(strongB, FeudalClaimStrength.Strong, 200), entry(strongA, FeudalClaimStrength.Strong, 200),
            entry(strongA, FeudalClaimStrength.Weak, 200), entry(null, FeudalClaimStrength.Strong, 99999) };
        for (int i = 0; i < source.Length; i++) candidates.SetValue(source[i], i);
        var result = ((IEnumerable)order.Invoke(null, new object[] { candidates, new[] { heir }, king })).Cast<object>().ToList();
        var actual = result.Select(p => (Hero)AccessTools.Field(type, "Hero").GetValue(p)).ToArray();
        Check(actual.SequenceEqual(new[] { strongA, strongB, strongC, weak }),
            "Pretenders exclude sovereign/lawful heirs, deduplicate, and sort claim before power before stable ID");
        Check((FeudalClaimStrength)AccessTools.Field(type, "Strength").GetValue(result[0]) == FeudalClaimStrength.Strong,
            "Multiple claims on one carrier use the strongest claim");
        var emptyHeirs = new List<Hero>();
        var noHeirResult = ((IEnumerable)order.Invoke(null, new object[] { candidates, emptyHeirs, king })).Cast<object>().ToList();
        Check(emptyHeirs.Count == 0 && noHeirResult.Count == 5, "Pretenders never mutate an empty lawful inheritance list");
        var empty = Array.CreateInstance(type, 0);
        Check(!((IEnumerable)order.Invoke(null, new object[] { empty, new[] { heir }, king })).Cast<object>().Any(),
            "No claims produces no fabricated pretenders");
    }

    private static void TestHereditaryAllegiance()
    {
        var method = AccessTools.Method(typeof(CheatCommands).Assembly.GetType("BellumCivile.HereditaryAllegiance"), "Chance");
        Func<float, int, int, bool, bool, bool, bool, float> chance = (i, h, r, hk, rk, hm, rm) =>
            (float)method.Invoke(null, new object[] { i, h, r, hk, rk, hm, rm });
        Check(Math.Abs(chance(20, 0, 0, false, false, false, false) - .2) < .000001, "Calm house can answer a royal falling-out without a personal gate");
        Check(Math.Abs(chance(50, 60, 20, false, false, false, false) - .6) < .000001, "Relative affinity shifts support by a quarter point per relation");
        Check(Math.Abs(chance(80, 10, 70, false, false, false, false) - .65) < .000001, "Strong ruler affinity moderates discontent");
        Check(Math.Abs(chance(-50, 0, 0, true, false, false, false) - .15) < .000001, "Kinship can draw a content lord into the dispute");
        Check(Math.Abs(chance(50, 0, 0, true, false, true, false) - .85) < .000001, "Exclusive kinship and marriage advantages total 35 points");
        foreach (float intent in new[] { -100f, -1, 0, 20, 50, 80, 100, 150 })
            foreach (int relation in new[] { -100, -25, 0, 25, 100 })
            {
                float baseline = chance(intent, relation, 0, false, false, false, false);
                Check(baseline == chance(intent, relation, 0, true, true, true, true), "Shared family ties cancel exactly");
                Check(baseline >= 0 && baseline <= 1, "Allegiance probability is bounded");
                Check(chance(intent, relation, 0, true, false, true, false) >= baseline
                    && chance(intent, relation, 0, false, true, false, true) <= baseline, "Ties favor only their own side");
            }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Check(chance(invalid, 0, 0, true, false, true, false) == 0, "Invalid intent fails closed");
        // Exact enumeration, not a Monte Carlo estimate: eight independent houses,
        // plus a royal household with twice one ordinary house's power. No feudal edges.
        foreach (int intent in new[] { 10, 25, 50, 75, 90 })
        {
            double[] probabilities = Enumerable.Range(0, 8).Select(i => (double)chance(intent, 0, 0, false, false, false, false)).ToArray();
            PrintAllegianceSimulation("Equal ties, intent " + intent, probabilities, Enumerable.Repeat(1.0, 8).ToArray());
        }
        var mixed = new[] { 10f, 20, 30, 40, 50, 60, 70, 80 };
        double[] ties = mixed.Select((intent, i) => (double)chance(intent, 0, 0, i < 2, i >= 6, i < 2, i >= 6)).ToArray();
        PrintAllegianceSimulation("Mixed intent; two tied to each royal", ties, Enumerable.Repeat(1.0, 8).ToArray());
        PrintAllegianceSimulation("Same odds; last two houses strongest", ties, new[] { .5, .5, .5, .5, 1.0, 1, 2, 2 });
    }

    private static void PrintAllegianceSimulation(string name, double[] chances, double[] powers)
    {
        double mass = 0, success = 0, expected = 0;
        for (int mask = 0; mask < 1 << chances.Length; mask++)
        {
            double probability = 1, backing = 0;
            for (int i = 0; i < chances.Length; i++)
            {
                bool joins = (mask & (1 << i)) != 0;
                probability *= joins ? chances[i] : 1 - chances[i];
                if (joins) backing += powers[i];
            }
            mass += probability;
            expected += probability * backing;
            if (backing >= 2 + powers.Sum() - backing) success += probability;
        }
        Check(Math.Abs(mass - 1) < .000001, "Exact allegiance distribution conserves probability");
        Console.WriteLine($"ALLEGIANCE SIM: {name}; expected backing={expected:0.###}/{powers.Sum() + 2:0.###}; threshold success={success * 100:0.###}%");
    }

    private static void TestHeirLoyaltyCommand()
    {
        var split = AccessTools.Method(typeof(CheatCommands), "TryParseHeirChallenge");
        foreach (string input in new[] { "Vlandia | Erdurand", "Southern Empire|Crown Princess Ira" })
            Check((bool)split.Invoke(null, new object[] { input.Split(' ').ToList(), null, null }),
                "Challenge command shared parser accepts multiword realm and heir");
        foreach (string input in new[] { "", "Vlandia |", "| Ira", "Vlandia | Ira | extra" })
            Check(!(bool)split.Invoke(null, new object[] { input.Split(' ').ToList(), null, null }),
                "Challenge command shared parser rejects missing or extra arguments");
        var parse = AccessTools.Method(typeof(CheatCommands), "TryParseHeirLoyalty");
        foreach (string input in new[] { "Vlandia | Erdurand | 0", "Southern Empire|Crown Princess Ira|24.5", "Vlandia | lord_123 | 100", "Vlandia | Erdurand | CLEAR" })
        {
            object[] values = { input.Split(' ').ToList(), null, null, null };
            Check((bool)parse.Invoke(null, values) && (string)values[1] == input.Split('|')[0].Trim()
                && (string)values[2] == input.Split('|')[1].Trim(), "Loyalty command parses multiword realms/heroes and adjacent separators");
            Check(input.EndsWith("CLEAR") ? values[3] == null : (double)values[3] >= 0 && (double)values[3] <= 100,
                "Loyalty target and clear are distinguished");
        }
        foreach (string input in new[] { "", "Vlandia | | 10", "Vlandia | Ira", "Vlandia | Ira | -1", "Vlandia | Ira | 101",
            "Vlandia | Ira | NaN", "Vlandia | Ira | Infinity", "Vlandia | Ira | 1e999", "Vlandia | Ira | 10 | extra", "Vlandia | Ira | 10,5" })
            Check(!(bool)parse.Invoke(null, new object[] { input.Split(' ').ToList(), null, null, null }), "Malformed/out-of-range loyalty command is rejected");
        foreach (double natural in new[] { -25.0, 0, 45, 80, 150 })
            foreach (double target in new[] { 0.0, 10, 24.5, 25, 100 })
            {
                var score = new HereditaryLoyaltyAssessment();
                AccessTools.Property(typeof(HereditaryLoyaltyAssessment), "Concession").SetValue(score, natural - 80);
                AccessTools.Property(typeof(HereditaryLoyaltyAssessment), "TestAdjustment").SetValue(score, target - score.Raw);
                Check(Math.Abs(score.Total - target) < 0.000001 && score.IsDisloyal == (target < 25),
                    "Testing adjustment reaches exact target even outside natural clamp and respects strict threshold");
                AccessTools.Property(typeof(HereditaryLoyaltyAssessment), "TestAdjustment").SetValue(score, 0.0);
                Check(score.Total == Math.Max(0, Math.Min(100, natural)), "Clearing adjustment restores natural loyalty");
            }
    }

    private static void TestSuccessionPledgesAndTribunals()
    {
        var assembly = typeof(SuccessionChallengeRecord).Assembly;
        var resolve = AccessTools.Method(assembly.GetType("BellumCivile.SuccessionPledgeRules"), "Resolve");
        Func<List<SuccessionPledgeRecord>, HashSet<Clan>> run = values => (HashSet<Clan>)resolve.Invoke(null, new object[] { values });
        Func<Clan> house = () => (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        Clan crown = house(), root = house(), first = house(), second = house(), player = house();
        var rows = new List<SuccessionPledgeRecord>
        {
            new SuccessionPledgeRecord { Clan = crown, Power = 500, Choice = SuccessionPledgeChoice.Loyal, PersonalChance = 1, Roll = 0 },
            new SuccessionPledgeRecord { Clan = root, Power = 200, Choice = SuccessionPledgeChoice.Rebel },
            new SuccessionPledgeRecord { Clan = first, Power = 100, PersonalChance = 0, Roll = 0.5f,
                LiegeChances = new Dictionary<Clan, float> { [root] = 0.6f, [second] = 1 } },
            new SuccessionPledgeRecord { Clan = second, Power = 100, PersonalChance = 0, Roll = 0.5f,
                LiegeChances = new Dictionary<Clan, float> { [first] = 0.6f } },
            new SuccessionPledgeRecord { Clan = player, Power = 1000, Choice = SuccessionPledgeChoice.AwaitingPlayer }
        };
        Check(run(rows) == null, "Actual pledge resolution waits for explicit player choice");
        rows[4].Choice = SuccessionPledgeChoice.Loyal;
        var rebels = run(rows);
        Check(rebels.SetEquals(new[] { root, first, second }), "A pledged liege unlocks successive vassal calls");
        Check(!rebels.Contains(crown) && !rebels.Contains(player), "Forced Crown and explicit player loyalty cannot be overridden");
        Check(rows.Where(r => rebels.Contains(r.Clan)).Sum(r => r.Power) == 400
            && rows.Where(r => !rebels.Contains(r.Clan)).Sum(r => r.Power) == 1500, "Clans contribute full power once to one exclusive side");
        rows[4].Choice = SuccessionPledgeChoice.Rebel;
        Check(rows.Where(r => run(rows).Contains(r.Clan)).Sum(r => r.Power) == 1400, "Player's 1000 power counts fully when actually pledged");
        Check(run(rows.AsEnumerable().Reverse().ToList()).SetEquals(run(rows)), "Pledges are independent of clan enumeration order");
        Check(run(rows).SetEquals(run(rows)) && rows[2].Roll == 0.5f && rows[2].Choice == SuccessionPledgeChoice.Automatic,
            "Reevaluation consumes frozen inputs without rerolling or modifying choices");
        rows[1].Choice = SuccessionPledgeChoice.Loyal;
        rows[4].Choice = SuccessionPledgeChoice.Loyal;
        Check(run(rows).Count == 0, "A vassal cycle cannot manufacture its own rebel root");
        rows[2].PersonalChance = 0.5f;
        Check(run(rows).Count == 0, "Exact probability boundary uses strict less-than");
        rows[2].PersonalChance = 0.51f;
        Check(run(rows).SetEquals(new[] { first, second }), "A successful personal call can seed later vassal backing");
        Check(run(rows.Concat(new[] { rows[2] }).ToList()) == null, "Duplicate houses are rejected instead of double-counted");
        rows[4].Choice = SuccessionPledgeChoice.Rebel;
        rows[2].PersonalChance = 0;
        rows[2].LiegeChances[player] = 0.7f;
        Check(run(rows).SetEquals(new[] { player, first, second }), "Player pledge activates the player's own vassal network");
        rows[4].Choice = SuccessionPledgeChoice.Loyal;
        Check(run(rows).Count == 0, "Player refusal cannot be undone by NPC recruitment");
        var passed = AccessTools.Method(assembly.GetType("BellumCivile.SuccessionPledgeRules"), "Passes");
        Check(!(bool)passed.Invoke(null, new object[] { 0f, 0f })
            && (bool)passed.Invoke(null, new object[] { 0.999f, 1f })
            && !(bool)passed.Invoke(null, new object[] { float.NaN, 1f }), "Pledge probabilities handle zero, full support and invalid rolls");
        var chance = AccessTools.Method(assembly.GetType("BellumCivile.RoyalHeirTribunalRules"), "ExecutionChance");
        foreach (float ordinary in new[] { 0f, 0.1f, 0.2f, 0.5f, 1f })
        {
            float normal = (float)chance.Invoke(null, new object[] { ordinary, false });
            float heir = (float)chance.Invoke(null, new object[] { ordinary, true });
            Check(normal == ordinary && heir == ordinary * 0.25f, "Royal-heir leniency preserves ordinary MCM/trait odds and quarters heir execution odds");
            foreach (double confiscation in new[] { 0.0, 0.2, 0.5, 1 })
            {
                double punish = (1 - heir) * confiscation, pardon = (1 - heir) * (1 - confiscation);
                Check(Math.Abs(heir + punish + pardon - 1) < 0.000001, "Tribunal execution, confiscation and pardon probabilities conserve the total");
                Check(punish >= (1 - normal) * confiscation && pardon >= (1 - normal) * (1 - confiscation),
                    "Reduced execution increases existing confiscation/pardon paths without new punishment weights");
            }
        }
        var fields = typeof(SuccessionPledgeRecord).GetFields();
        Check(fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
            "Pledge speaker, power, probabilities, roll and explicit choice are saved");
        Check(fields.Select(f => f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute")
            .ConstructorArguments[0].Value).Distinct().Count() == fields.Length, "Pledge save field IDs are unique");
    }

    private static void TestSuccessionChallenges()
    {
        var assembly = typeof(CrownAccessionRecord).Assembly;
        var rules = assembly.GetType("BellumCivile.SuccessionChallengeRules");
        Func<string, object[], object> call = (name, values) => AccessTools.Method(rules, name).Invoke(null, values);
        foreach (double loyalty in new[] { -1.0, 0, 10, 24, 25, 100, double.NaN, double.PositiveInfinity })
            foreach (bool available in new[] { false, true })
                foreach (bool blocked in new[] { false, true })
                {
                    bool expected = loyalty >= 0 && loyalty < 25 && available && !blocked;
                    Check((bool)call("CanInitiate", new object[] { loyalty, available, true, blocked }) == expected,
                        "Challenge initiation obeys strict loyalty threshold and availability/cooldowns");
                    Check(!(bool)call("CanInitiate", new object[] { loyalty, available, false, blocked }),
                        "Non-heirs cannot initiate hereditary challenges");
                }
        foreach (double loyalty in new[] { 0.0, 10, 20, 24, 25 })
        {
            int inheritance = 0;
            for (int i = 0; i < 100; i++)
            {
                double roll = (i + 0.5) / 100;
                if ((SuccessionChallengeDemand)call("ChooseDemand", new object[] { loyalty, true, roll }) == SuccessionChallengeDemand.InheritanceFirst) inheritance++;
                Check((SuccessionChallengeDemand)call("ChooseDemand", new object[] { loyalty, false, roll }) == SuccessionChallengeDemand.Crown,
                    "Established household leaders demand the Crown");
            }
            Check(inheritance == 50 + loyalty, $"Loyalty {loyalty} produces exactly {50 + loyalty}% inheritance-first rolls");
        }
        foreach (int owned in Enumerable.Range(0, 6))
            foreach (int granted in Enumerable.Range(0, 7))
                Check((bool)call("CanConcede", new object[] { owned, granted }) == (granted > 0 && granted < owned),
                    "Concessions never consume the royal household's last fief");
        Check((bool)call("HasBacking", new object[] { 80.0, 100.0, 0.8 })
            && !(bool)call("HasBacking", new object[] { 79.0, 100.0, 0.8 })
            && !(bool)call("HasBacking", new object[] { 0.0, 0.0, 0.8 })
            && (bool)call("HasBacking", new object[] { 1.0, 0.0, 0.8 }), "Power gate handles equality and zero forces");
        var acceptance = AccessTools.Method(assembly.GetType("BellumCivile.UltimatumAcceptanceRules"), "Calculate");
        foreach (double ratio in new[] { 0.0, 0.5, 1, 1.49, 1.5, 2, 2.5, 3, 4 })
            foreach (int calculating in Enumerable.Range(-2, 5))
                foreach (int valor in Enumerable.Range(-2, 5))
                    foreach (int mercy in Enumerable.Range(-2, 5))
                        foreach (bool war in new[] { false, true })
                        {
                            double expected = Math.Min(0.5, 0.1 + Math.Floor((ratio - 1) / 0.5) * 0.1);
                            expected += war ? 0.1 : 0;
                            expected += calculating * 0.05;
                            expected -= Math.Max(0, valor) * 0.05;
                            expected += Math.Max(0, mercy) * 0.05;
                            expected = ratio < 1 ? 0 : Math.Max(0, Math.Min(1, expected));
                            double actual = (double)acceptance.Invoke(null, new object[] { ratio * 100, 100.0, war, calculating, valor, mercy });
                            Check(Math.Abs(expected - actual) < 0.000001, "Shared ultimatum chance preserves existing power/trait weights");
                        }
        Check((double)acceptance.Invoke(null, new object[] { 1.0, 0.0, false, 0, 0, 0 }) == 0.5
            && (double)acceptance.Invoke(null, new object[] { 0.0, 0.0, false, 0, 0, 0 }) == 0,
            "Zero loyalist power is bounded without infinity or division by zero");
        var ruler = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var next = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var record = new SuccessionChallengeRecord { Sovereign = ruler, RewardRecorded = true, RewardUntil = 500,
            Estate = new CrownAccessionRecord() };
        var loyaltyAt = AccessTools.Method(typeof(SuccessionChallengeRecord), "LoyaltyAt");
        Check((double)loyaltyAt.Invoke(record, new object[] { ruler, 499.0 }) == 25
            && (double)loyaltyAt.Invoke(record, new object[] { next, 499.0 }) == 0
            && (double)loyaltyAt.Invoke(record, new object[] { ruler, 500.0 }) == 0, "Concession loyalty is ruler-bound with exact expiry");
        record.LoyaltyEnded = true;
        Check((double)loyaltyAt.Invoke(record, new object[] { ruler, 499.0 }) == 0, "Restoration cannot revive ended loyalty");
        Check(!record.HasAdvance, "Unpaid concession cannot consume inheritance rights");
        record.Estate.EndowmentSettled = true;
        Check(!record.HasAdvance, "Empty settlement cannot invent an inheritance advance");
        record.Estate.DeliveredFiefs.Add("barony");
        Check(!record.HasAdvance && record.HasHouseholdGrant,
            "Delivered household grant blocks repeat grants without consuming death inheritance, even after gratitude expiry");
        record.Estate = null;
        record.CrownEstate = new CrownAccessionRecord();
        record.CrownEstate.DeliveredFiefs.Add("royal_share");
        Check(!record.HasAdvance, "A partial Crown endowment is not a completed advance");
        record.CrownEstate.EndowmentSettled = true;
        Check(record.HasAdvance, "Crown surrender endowment consumes the same inheritance advance once delivered");
        record.CrownEstate.DeliveredFiefs.Clear();
        Check(!record.HasAdvance, "Landless Crown accession does not invent a landed advance");
        record.CrownEstate.DeliveredGold = 50;
        Check(record.HasAdvance, "Completed Crown gold endowment remains a recorded inheritance advance");
        record.CrownEstate = new CrownAccessionRecord();
        record.SeizureIntents.Add("occupied_barony");
        record.SeizedFiefs.Add("occupied_barony");
        Check(!record.HasAdvance, "Hostile possession alone is not a paid inheritance advance");
        record.WarEstate = new CrownAccessionRecord { EndowmentSettled = true };
        record.WarEstate.DeliveredFiefs.Add("occupied_barony");
        Check(!record.HasAdvance && record.HasHouseholdGrant,
            "Legalized seizure records a household grant, not a full inheritance advance");
        record.Challenger = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var grantBehavior = new SuccessionChallengeBehavior();
        ((List<SuccessionChallengeRecord>)AccessTools.Field(typeof(SuccessionChallengeBehavior), "_records")
            .GetValue(grantBehavior)).Add(record);
        var grantReceipt = AccessTools.Method(typeof(SuccessionChallengeBehavior), "HasHouseholdGrant");
        Check((bool)grantReceipt.Invoke(grantBehavior, new object[] { ruler, record.Challenger })
            && !(bool)grantReceipt.Invoke(grantBehavior, new object[] { next, record.Challenger }),
            "Permanent household receipt is bound to the donor and recipient, not gratitude duration");
        Check(!(bool)AccessTools.Method(typeof(SuccessionChallengeBehavior), "HasInheritanceAdvance")
            .Invoke(grantBehavior, new object[] { ruler, record.Challenger }),
            "Inheritance consumers do not treat a household receipt as a paid death estate");
        record.SubmissionRuler = ruler;
        record.WarOutcome = SuccessionChallengeOutcome.Defeat;
        record.OutcomeDay = 100;
        record.SubmissionUntil = 400;
        var submissionAt = AccessTools.Method(typeof(SuccessionChallengeRecord), "SubmissionAt");
        Check((double)submissionAt.Invoke(record, new object[] { ruler, 100.0 }) == 0,
            "Unfinished war resolution cannot grant submission");
        record.ResolutionReturned = true;
        Check((double)submissionAt.Invoke(record, new object[] { ruler, 100.0 }) == 25
            && (double)submissionAt.Invoke(record, new object[] { ruler, 250.0 }) == 12.5
            && (double)submissionAt.Invoke(record, new object[] { ruler, 400.0 }) == 0,
            "Submission fades from 25 to zero over the saved three-year interval");
        Check((double)submissionAt.Invoke(record, new object[] { next, 100.0 }) == 0,
            "Submission does not attach to a replacement ruler");
        record.WarOutcome = SuccessionChallengeOutcome.WhitePeace;
        Check((double)submissionAt.Invoke(record, new object[] { ruler, 100.0 }) == 0,
            "White peace grants no defeat submission bonus");
        var pause = AccessTools.Method(rules, "PersonalPauseYears");
        Check((int)pause.Invoke(null, new object[] { SuccessionChallengeOutcome.Defeat }) == 5
            && (int)pause.Invoke(null, new object[] { SuccessionChallengeOutcome.WhitePeace }) == 2,
            "Defeat uses five personal years; white peace uses two");
        var fade = AccessTools.Method(rules, "SubmissionBonus");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Check((double)fade.Invoke(null, new object[] { 100.0, 400.0, invalid }) == 0,
                "Invalid time cannot manufacture submission loyalty");
        Check((double)fade.Invoke(null, new object[] { 100.0, 100.0, 100.0 }) == 0,
            "Zero-duration submission avoids division by zero");
        var frozenFaction = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
        AccessTools.Field(typeof(FactionObject), "_type").SetValue(frozenFaction, FactionType.InstallRuler);
        var bind = AccessTools.Method(typeof(FactionObject), "BindSuccessionChallenge");
        bind.Invoke(frozenFaction, new object[] { "crisis_one" });
        bind.Invoke(frozenFaction, new object[] { "crisis_one" });
        Check(frozenFaction.IsChallengeStartupPending && !frozenFaction.IsCivilWarActive(),
            "Interrupted shell startup is not exposed as an active civil war");
        Check((bool)AccessTools.Field(typeof(FactionObject), "_ultimatumResolved").GetValue(frozenFaction)
            && (bool)AccessTools.Field(typeof(FactionObject), "_solidarityRecruitmentApplied").GetValue(frozenFaction),
            "Frozen startup seals ordinary ultimatum and recruitment paths");
        bool rejectedRebind = false;
        try { bind.Invoke(frozenFaction, new object[] { "crisis_two" }); }
        catch (System.Reflection.TargetInvocationException ex) { rejectedRebind = ex.InnerException is InvalidOperationException; }
        Check(rejectedRebind, "A saved faction cannot be adopted by a different crisis");
        var transferable = AccessTools.Method(rules, "TransferablePower");
        foreach (double party in new[] { -1.0, 0, 100, 1000, double.NaN, double.PositiveInfinity })
            foreach (double military in new[] { 0.0, 50, 500 })
                foreach (double loyal in new[] { 0.0, 20, 700 })
                {
                    double shifted = (double)transferable.Invoke(null, new object[] { party, military, loyal });
                    double expected = double.IsNaN(party) || double.IsInfinity(party) ? 0
                        : Math.Max(0, Math.Min(party, Math.Min(military, loyal)));
                    Check(shifted == expected, "Transfer credits only finite existing royal military power");
                    Check(Math.Abs((200 + shifted) + (loyal - shifted) - (200 + loyal)) < 0.00001,
                        "Moving an heir party conserves total allegiance power");
                }
        foreach (SuccessionChallengePhase phase in Enum.GetValues(typeof(SuccessionChallengePhase)))
        {
            record.Phase = phase;
            Check(record.IsOpen == (phase != SuccessionChallengePhase.Settled && phase != SuccessionChallengePhase.Withdrawn
                && phase != SuccessionChallengePhase.Cancelled), "Only terminal phases release the crisis lock");
        }
        var fields = typeof(SuccessionChallengeRecord).GetFields();
        Check(fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
            "Every challenge state, roll, pledge and reward receipt has save metadata");
        Check(fields.Select(f => f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute")
            .ConstructorArguments[0].Value).Distinct().Count() == fields.Length, "Challenge field save IDs are unique");
    }

    private static void TestElectiveAcceptance()
    {
        var type = typeof(CrownAccessionBehavior).Assembly.GetType("BellumCivile.ElectiveAcceptanceRules");
        var assess = AccessTools.Method(type, "Assess");
        Func<bool, int, int, int, int, double, double, double, double, double, double, object> score =
            (leading, honor, mercy, relation, claim, support, margin, own, crown, threshold, rival) =>
                assess.Invoke(null, new object[] { leading, honor, mercy, relation, claim, support, margin, own, crown, threshold, rival });
        Func<object, double> total = value => (double)AccessTools.Property(value.GetType(), "Total").GetValue(value);
        Check(Math.Abs(total(score(false, 0, 0, 0, 0, 49, 2, 480, 520, 0.8, 0)) - 48.923076923) < 0.00001,
            "Acceptance matches close-election simulation");
        Check(total(score(false, 0, 0, 0, 0, 20, 50, 200, 800, 0.8, 0)) == 100,
            "Weak landslide loser reaches full acceptance");
        Check(total(score(false, -1, 0, -40, 2, 40, 5, 600, 400, 0.8, 0)) == 10,
            "Hostile strong claimant matches 10 acceptance");
        Check(total(score(false, 2, 1, 0, 2, 40, 5, 600, 400, 0.8, 0)) == 55,
            "Honorable strong claimant matches 55 acceptance");
        Check(Math.Abs(total(score(false, 0, 0, 0, 0, 32, 8, 420, 200, 0.8, 380)) - 65.076923077) < 0.00001,
            "Conditional rival power receives exactly half weight");
        foreach (int relation in new[] { -150, -100, 0, 100, 150 })
            foreach (int claim in new[] { 0, 1, 2 })
            {
                Check(total(score(true, -2, -2, relation, claim, 100, 0, 0, 1000, 0.8, 0)) == 100,
                    $"Projected winner remains 100 regardless of relation {relation} and claim {claim}");
                var loser = score(false, -2, -2, relation, claim, 100, 0, 1000, 0, 0.8, 0);
                Check(total(loser) >= 0 && total(loser) <= 100, "Losing acceptance remains bounded");
            }
        var none = total(score(false, 0, 0, 0, 0, 20, 0, 800, 1000, 0.8, 0));
        Check(none == 80 && total(score(false, 0, 0, 0, 1, 20, 0, 800, 1000, 0.8, 0)) == 75
            && total(score(false, 0, 0, 0, 2, 20, 0, 800, 1000, 0.8, 0)) == 65,
            "Claims are exclusive and military modifier is zero at required power");
        Check(total(score(false, 0, 0, 0, 0, 20, 0, 0, 0, 0.8, 0)) == 100
            && total(score(false, 0, 0, 0, 0, 20, 0, 1, 0, 0.8, 0)) == 60,
            "Zero force and zero opposition are handled without division by zero");
        Check(Math.Abs(total(score(false, 0, 0, -10, 0, 49, 2, 480, 520, 0.8, 0)) - 46.423076923) < 0.00001,
            "Existing -10 relation memory contributes -2.5 without a duplicate penalty");
        Func<object, string, double> field = (value, name) => (double)AccessTools.Field(value.GetType(), name).GetValue(value);
        foreach (double support in new[] { -10.0, 0, 17, 20, 40.9, 100, 110 })
        {
            var value = score(false, 0, 0, 0, 0, support, 0, 800, 1000, 0.8, 0);
            Check(field(value, "Baseline") == 100 - Math.Max(0, Math.Min(100, support)),
                $"Baseline uses bounded support percentage {support}");
            Check(total(score(true, -2, -2, -100, 2, support, 0, 1000, 0, 0.8, 0)) == 100,
                "Winner override is independent of support-based baseline");
        }
        Check(Math.Abs(total(score(false, 0, 0, -44, 0, 40.9, 1.24, 1.103, 1, 1, 0)) - 46.66) < 0.00001,
            "Saratis rounded screenshot modifiers yield 46.66 acceptance");
        Check(total(score(false, 0, 1, 37, 0, 17, 25.18, 0.104, 1, 1, 0)) == 100,
            "Desporion screenshot remains capped at 100 with baseline 83");
        foreach (int honor in Enumerable.Range(-3, 7))
            foreach (int mercy in Enumerable.Range(-3, 7))
            {
                var value = score(false, honor, mercy, 0, 0, 20, 0, 800, 1000, 0.8, 0);
                double h = Math.Max(-2, Math.Min(2, honor)) * 10;
                double m = Math.Max(-2, Math.Min(2, mercy)) * 5;
                Check(field(value, "Honor") == h && field(value, "Mercy") == m
                    && field(value, "PersonalityCap") == Math.Max(-25, Math.Min(25, h + m)) - h - m,
                    $"Separate trait rows preserve combined cap for Honor {honor}, Mercy {mercy}");
                Check(total(value) == Math.Min(100, 80 + Math.Max(-25, Math.Min(25, h + m))),
                    "Trait breakdown agrees with final clamped total");
            }
        var helper = typeof(CrownAccessionBehavior).Assembly.GetType("BellumCivile.RebellionPowerHelper");
        var projection = AccessTools.Method(helper, "CalculateProjectedConflictPower").GetParameters();
        Check(projection[5].IsOptional && projection[5].DefaultValue == null && projection[6].IsOptional
            && projection[6].DefaultValue == null, "Normal rebellion calls retain null projected-election defaults");
    }

    private static void TestHereditaryLoyalty()
    {
        var rules = typeof(CrownAccessionBehavior).Assembly.GetType("BellumCivile.HereditaryLoyaltyRules");
        var assess = AccessTools.Method(rules, "Assess");
        Func<int, int, bool, bool, double, int, double, bool, bool, bool, double, int, HereditaryLoyaltyAssessment> score =
            (honor, mercy, heir, initial, years, shock, controversy, regency, dependent, share, wait, fiefs) =>
                (HereditaryLoyaltyAssessment)assess.Invoke(null, new object[] {
                    honor, mercy, heir, initial, years, shock, controversy, regency, dependent, share, wait, fiefs, 0 });
        foreach (int relation in new[] { -150, -100, -35, -23, 0, 11, 82, 100, 150 })
        {
            var value = (HereditaryLoyaltyAssessment)assess.Invoke(null, new object[] {
                0, -1, true, false, 0.0, 25, 0.0, false, false, false, 0.0, 0, relation });
            double modifier = Math.Max(-100, Math.Min(100, relation)) * 0.25;
            Check(value.Relations == modifier && value.Total == 45 + modifier,
                $"Baseline-80 Erdurand scenario uses bounded personal relation {relation}");
        }
        Check(score(0, 0, true, true, 0, 0, 0, false, false, false, 0, 0).Total == 90,
            "Initial Crown heir: baseline 80 plus position 10");
        Check(score(0, 0, false, true, 0, 0, 0, false, false, false, 0, 0).Total == 70,
            "Other lawful successors have a flat -10 position term");
        Check(score(0, 0, false, false, 0, 25, 0, false, false, false, 0, 0).Total == 30,
            "Adult accession: -15 reign and -25 shock");
        Check(score(0, 0, false, false, 0, 50, 0, false, false, false, 0, 0).Total == 5,
            "Minor accession: captured -50 shock");
        Check(score(0, 0, false, false, 1, 0, 0, false, false, false, 0, 0).Total == 56,
            "First anniversary: shock removed and reign increases once");
        Check(score(0, 0, false, false, 0, 25, 5, false, false, false, 0, 0).Total == 25
            && !score(0, 0, false, false, 0, 25, 5, false, false, false, 0, 0).IsDisloyal,
            "Exactly 25 is not disloyal");
        Check(score(0, 0, false, false, 0, 25, 5.1, false, false, false, 0, 0).IsDisloyal,
            "Strictly below 25 is disloyal");
        Check(score(0, 0, false, true, 0, 0, 12, true, false, false, 0, 0).Controversy == -24
            && score(0, 0, false, true, 0, 0, 12, false, false, false, 0, 0).Controversy == -12,
            "Only minority regency doubles government controversy");
        foreach (int h in Enumerable.Range(-3, 7))
            foreach (int m in Enumerable.Range(-3, 7))
            {
                var value = score(h, m, false, true, 0, 0, 0, false, false, false, 0, 0);
                var expected = Math.Max(-25, Math.Min(25, Math.Max(-2, Math.Min(2, h)) * 10
                    + Math.Max(-2, Math.Min(2, m)) * 5));
                Check(value.Honor + value.Mercy + value.PersonalityCap == expected,
                    $"Personality bounded correctly for Honor {h}, Mercy {m}");
            }
        Check(score(0, 0, false, false, 0.99, 0, 0, false, false, false, 0, 0).Reign == -15
            && score(0, 0, false, false, 15, 0, 0, false, false, false, 0, 0).Reign == 0
            && score(0, 0, false, false, 40, 0, 0, false, false, false, 0, 0).Reign == 15,
            "Reign uses completed years and caps at +15");
        Check(score(0, 0, false, true, 40, 0, 0, false, false, false, 0, 0).Reign == 15,
            "Initial reign starts at zero and shares the long-reign cap");
        Check(score(0, 0, false, true, 0, 0, 0, false, true, true, 3.9, 50).Inheritance == -3,
            "Waiting counts completed adult years");
        Check(score(0, 0, false, true, 0, 0, 0, false, true, true, 20, 5).Inheritance == -2.5,
            "Expected share pressure caps at half the royal demesne");
        Check(score(0, 0, false, true, 0, 0, 0, false, true, true, 30, 100).Inheritance == -10,
            "Waiting pressure has a hard ten-point cap");
        Check(score(0, 0, false, true, 0, 0, 0, false, true, false, 0, 1).Inheritance == -5
            && score(0, 0, false, true, 0, 0, 0, false, true, false, 30, 1).Inheritance == -15,
            "Excluded heirs receive only the five-plus-years penalty capped at fifteen");
        Check(score(0, 0, false, true, 0, 0, 0, false, false, false, 30, 100).Inheritance == 0,
            "Minors and independent households have no dependency pressure");
        Check(score(2, 2, true, true, 30, 0, 0, false, false, false, 0, 0).Total == 100
            && score(-2, -2, false, false, 0, 50, 100, true, true, false, 30, 100).Total == 0,
            "Final loyalty clamps to 0-100");
        var fields = typeof(HereditaryLoyaltyMemory).GetFields();
        Check(fields.Length == 8 && fields.All(f => f.GetCustomAttributesData()
            .Any(a => a.AttributeType.Name == "SaveableFieldAttribute")), "All loyalty event-memory fields are saveable");
        Check(fields.Select(f => f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute")
            .ConstructorArguments[0].Value).Distinct().Count() == fields.Length, "Loyalty memory save IDs are unique");
        Check(new HereditaryLoyaltyMemory().ShockSubjects.Count == 0, "No invented shock recipients in new memory");
        TestLoyaltyMemoryDates();
    }

    private static void TestLoyaltyMemoryDates()
    {
        var dayTicks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = dayTicks.GetValue(null);
        dayTicks.SetValue(null, 1000L);
        try
        {
        var sovereign = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var subject = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var newcomer = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var memory = new HereditaryLoyaltyMemory { Sovereign = sovereign, ReignStart = CampaignTime.Days(10),
            YearDays = 84, ShockUntil = CampaignTime.Days(94), ShockPoints = 50,
            ShockSubjects = new List<Hero> { sovereign, subject } };
        var shockFor = AccessTools.Method(typeof(HereditaryLoyaltyMemory), "ShockFor");
        var reignAt = AccessTools.Method(typeof(HereditaryLoyaltyMemory), "ReignYearsAt");
        Check((int)shockFor.Invoke(memory, new object[] { subject, 93.9 }) == 50
            && (int)shockFor.Invoke(memory, new object[] { subject, 94.0 }) == 0,
            "Captured shock persists in full until the exact expiry");
        Check((int)shockFor.Invoke(memory, new object[] { sovereign, 11.0 }) == 0
            && (int)shockFor.Invoke(memory, new object[] { newcomer, 11.0 }) == 0,
            "Sovereign and newly eligible relatives cannot acquire an old succession shock");
        Check((double)reignAt.Invoke(memory, new object[] { 93.9 }) == 0
            && (double)reignAt.Invoke(memory, new object[] { 94.0 }) == 1,
            "Reign anniversary uses the saved year length and start");
        }
        finally { dayTicks.SetValue(null, oldTicks); }
    }

    private static void TestStandingElections()
    {
        var rules = typeof(CrownAccessionBehavior).Assembly.GetType("BellumCivile.ElectiveSuccessionRules");
        var tally = AccessTools.Method(rules, "Tally");
        var preference = AccessTools.Method(rules, "Preference");
        Check((double)preference.Invoke(null, new object[] { 100d, 0d, false }) == 60, "Election gives law merit sixty percent");
        Check((double)preference.Invoke(null, new object[] { 0d, 100d, false }) == 40, "Election gives political preference forty percent");
        Check((double)preference.Invoke(null, new object[] { 100d, 100d, true }) == 75, "Minor clan heads retain candidacy with a 25 point penalty");
        Hero[] candidates = Enumerable.Range(0, 4).Select(i =>
        {
            var h = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            AccessTools.Property(typeof(Hero), "StringId").SetValue(h, "candidate_" + i);
            return h;
        }).ToArray();
        Func<int, double, string, ElectiveCommitment> vote = (nominee, weight, source) => new ElectiveCommitment
        {
            Nominee = nominee < 0 ? null : candidates[nominee], Weight = weight, Source = source,
            Preferences = candidates.Select((h, i) => new ElectivePreference { Candidate = h, Law = 50, Politics = 50,
                Score = i == nominee ? 90 : 50 - i }).ToList()
        };
        var record = new ElectiveSuccessionRecord { Votes = new List<ElectiveCommitment>
            { vote(0, 100, "natural"), vote(1, 20, "natural"), vote(2, 10, "natural"), vote(-1, 70, "player") } };
        tally.Invoke(null, new object[] { record });
        Check(record.Finalists.Count == 3 && !record.Finalists.Contains(candidates[3]), "Weighted nominations admit only three earned finalists");
        Check(record.Winner == candidates[0] && record.Support(candidates[0]) == 100, "Power-weighted ballot chooses the supported candidate");
        Check(record.TotalWeight == 200 && record.Votes.Where(v => v.Supported == null).Sum(v => v.Weight) == 70,
            "Abstention remains in denominator instead of inflating support to 100 percent");
        record.Votes[3].Nominee = candidates[3]; record.Votes[3].Source = "persuaded";
        tally.Invoke(null, new object[] { record });
        Check(record.Finalists.Contains(candidates[3]) && record.Votes[3].Supported == candidates[3], "A promised nomination can elevate a previously absent finalist");
        record.Votes[3].Weight = 1;
        tally.Invoke(null, new object[] { record });
        Check(record.Votes[3].Nominee == candidates[3] && record.Votes[3].Supported == null, "A promise to a nonfinalist is preserved, not redirected to a rival");
        foreach (var v in record.Votes) v.Weight = 0;
        tally.Invoke(null, new object[] { record });
        Check(record.Votes.All(v => v.Weight == 1), "All-zero electorate falls back to equal voting weights");
        record.Votes[0].Weight = -20;
        tally.Invoke(null, new object[] { record });
        Check(record.Votes[0].Weight == 0 && record.TotalWeight == 3, "An individual zero-power clan gains no artificial vote");
        var refreshWeights = AccessTools.Method(rules, "RefreshWeights");
        var npcClan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var playerClan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var live = new ElectiveSuccessionRecord { Votes = new List<ElectiveCommitment>
            { vote(0, 1000, "natural"), vote(1, 0, "player") } };
        live.Votes[0].Clan = npcClan; live.Votes[1].Clan = playerClan;
        tally.Invoke(null, new object[] { live });
        Check(live.Support(candidates[1]) == 0, "Reproduces a zero-weight player commitment before influence gain");
        var cachedPreferences = live.Votes[1].Preferences;
        int reads = 0;
        double playerPower = 1000;
        Func<Clan, double> power = clan => { reads++; return clan == playerClan ? playerPower : 1000; };
        Check((bool)refreshWeights.Invoke(null, new object[] { live, power }), "Influence gain refreshes standing weights without a new term");
        Check(live.Support(candidates[1]) == 1000 && live.TotalWeight == 2000, "Player's 1000 power contributes fifty percent of a 2000-power electorate");
        Check(ReferenceEquals(cachedPreferences, live.Votes[1].Preferences) && live.Votes[1].Nominee == candidates[1]
            && live.Votes[1].Source == "player", "Refreshing weight does not reconsider preferences or replace commitments");
        live.Votes[1].Nominee = candidates[0];
        tally.Invoke(null, new object[] { live });
        Check(live.Support(candidates[0]) == 2000 && live.Support(candidates[1]) == 0, "Switching player support moves its full refreshed voting weight");
        Check(!(bool)refreshWeights.Invoke(null, new object[] { live, power }), "Unchanged live weights do not trigger another tally");
        playerPower = 2000; reads = 0; live.Frozen = true;
        Check(!(bool)refreshWeights.Invoke(null, new object[] { live, power }) && reads == 0 && live.TotalWeight == 2000,
            "Frozen final election ignores later power changes without reading live clan power");
        live.Frozen = false; live.Completed = true;
        Check(!(bool)refreshWeights.Invoke(null, new object[] { live, power }) && reads == 0,
            "Completed election weights remain historical");
        live.Completed = false;
        Func<Clan, double> zeroPower = clan => 0;
        Check((bool)refreshWeights.Invoke(null, new object[] { live, zeroPower }) && live.TotalWeight == 2,
            "Live all-zero electorate still uses equal fallback weights");
        Check(!(bool)refreshWeights.Invoke(null, new object[] { live, zeroPower }), "All-zero fallback stabilizes instead of retallying on every read");
        record.Votes.Clear();
        tally.Invoke(null, new object[] { record });
        Check(record.Winner == null && record.Finalists.Count == 0, "Empty candidate electorate resolves to emergency signal, not a fabricated winner");
        foreach (int count in new[] { 1, 2, 3, 4 })
        {
            record.Votes = Enumerable.Range(0, count).Select(i => vote(i, 10, "natural")).ToList();
            foreach (var v in record.Votes) v.Preferences.RemoveAll(p => Array.IndexOf(candidates, p.Candidate) >= count);
            tally.Invoke(null, new object[] { record });
            Hero winner = record.Winner;
            var finalists = record.Finalists.ToArray();
            record.Votes.Reverse();
            foreach (var v in record.Votes) v.Preferences.Reverse();
            tally.Invoke(null, new object[] { record });
            Check(record.Winner == winner && record.Finalists.SequenceEqual(finalists), "Tie-breaking is independent of enumeration order for " + count + " candidates");
            Check(Math.Abs(record.Finalists.Sum(record.Support) + record.Votes.Where(v => v.Supported == null).Sum(v => v.Weight) - record.TotalWeight) < .00001,
                "Every clan contributes voting weight once for " + count + " candidates");
        }
        foreach (var type in new[] { typeof(ElectiveSuccessionRecord), typeof(ElectiveCommitment), typeof(ElectivePreference) })
        {
            var fields = type.GetFields();
            Check(fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")), type.Name + " persists all ballot fields");
            Check(fields.Select(f => f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute")
                .ConstructorArguments[0].Value).Distinct().Count() == fields.Length, type.Name + " has unique save field IDs");
        }
    }

    private static void TestRealmMeritProfiles()
    {
        var assembly = typeof(CrownAccessionBehavior).Assembly;
        var type = assembly.GetType("BellumCivile.RealmSuccessionMerit");
        var score = AccessTools.Method(type, "Score");
        Func<HouseSuccessionLaw, double, double, bool, bool, bool, double> evaluate = (law, kin, skill, party, army, governor) =>
            (double)score.Invoke(null, new object[] { law, kin, skill, skill, skill, skill, skill,
                skill, skill, skill, skill, skill / 300, party, army, governor });
        foreach (var law in new[] { HouseSuccessionLaw.Kinship, HouseSuccessionLaw.Tanistry,
            HouseSuccessionLaw.MilitaryAcclamation, HouseSuccessionLaw.ElectiveSeniority, HouseSuccessionLaw.ShuraCouncil })
        {
            Check(Math.Abs(evaluate(law, 1, 300, true, true, true) - 100) < .00001, law + " profile totals 100");
            Check(Math.Abs(evaluate(law, 2, 900, true, true, true) - 100) < .00001, law + " inputs cap independently");
            Check(evaluate(law, -1, -100, false, false, false) == 0, law + " negative inputs cannot reduce the base below zero");
        }
        Check(evaluate(HouseSuccessionLaw.Kinship, 1, 0, false, false, false) == 40, "Kinship kinship carries 40 points");
        Check(evaluate(HouseSuccessionLaw.Tanistry, 1, 0, false, false, false) == 45, "Tanistry kinship carries 45 points");
        Check(evaluate(HouseSuccessionLaw.Tanistry, 0, 300, false, false, false) == 55, "Unrelated Tanistry candidate still has merit");
        Check(evaluate(HouseSuccessionLaw.MilitaryAcclamation, 0, 0, false, true, false) == 0, "Army service requires party leadership");
        Check(evaluate(HouseSuccessionLaw.MilitaryAcclamation, 0, 0, true, false, false) == 3, "Current party command contributes 3 points");
        Check(evaluate(HouseSuccessionLaw.MilitaryAcclamation, 0, 0, true, true, false) == 10, "Current army command contributes the remaining 7 points");
        var experience = AccessTools.Method(type, "Experience");
        Func<double, double, double> exp = (age, majority) => (double)experience.Invoke(null, new object[] { age, majority });
        Check(exp(12, 18) == 0 && exp(18, 18) == 0, "No experience awarded before configured adulthood");
        Check(exp(42.5, 20) == .5 && exp(65, 20) == 1 && exp(90, 20) == 1, "Experience uses configured majority and caps at 65");
        Check((double)AccessTools.Field(type, "GenderPreferenceBonus").GetRawConstantValue() == 5, "Merit gender preference is bounded to 5 points");

        var kinType = assembly.GetType("BellumCivile.RealmSuccessionKinship");
        Func<object> context = () => Activator.CreateInstance(kinType, true);
        var get = AccessTools.Method(kinType, "Get");
        Func<Hero> make = () =>
        {
            var hero = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            AccessTools.Field(typeof(Hero), "_children").SetValue(hero, new TaleWorlds.Library.MBList<Hero>());
            return hero;
        };
        Hero grandparent = make(), parent = make(), ruler = make(), sibling = make(), child = make(), grandchild = make(), uncle = make(), cousin = make(), spouse = make();
        parent.Father = uncle.Father = grandparent;
        ruler.Father = sibling.Father = parent;
        child.Father = ruler;
        child.Mother = spouse;
        grandchild.Father = child;
        cousin.Father = uncle;
        object cache = context();
        Func<Hero, Hero, double> kin = (candidate, root) => (double)get.Invoke(cache, new object[] { candidate, root });
        Check(kin(child, ruler) == 1 && kin(parent, ruler) == .85 && kin(grandchild, ruler) == .85, "Directional immediate kinship tiers");
        Check(kin(sibling, ruler) == .8 && kin(uncle, ruler) == .6 && kin(cousin, ruler) == .45, "Collateral kinship tiers");
        Check(kin(spouse, ruler) == 0, "Shared children never manufacture blood kinship between spouses");
        Hero common = make(), first = common, second = common;
        for (int i = 0; i < 4; i++) { Hero a = make(), b = make(); a.Father = first; b.Father = second; first = a; second = b; }
        Check(Math.Abs(kin(first, second) - .20) < .00001, "Third cousins qualify at the 0.20 kinship boundary with four links per side");
        Hero beyond = make(); beyond.Father = first;
        Check(kin(beyond, second) == 0, "Five links on either side exceeds the Kinship boundary");
    }

    private static void TestRealmLawGroups()
    {
        TestMandates();
        TestDepositionSchedule();
        string xml;
        using (var reader = new StreamReader(typeof(RealmLawRegistry).Assembly.GetManifestResourceStream("BellumCivile.ModuleData.bellum_laws.xml")))
            xml = reader.ReadToEnd();
        var read = AccessTools.Method(typeof(RealmLawRegistry), "Read");
        Func<string, RealmLawRegistry> parse = input =>
        {
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(input)))
                return (RealmLawRegistry)read.Invoke(null, new object[] { stream });
        };
        var registry = parse(xml);
        Check(registry.InGroup("succession").Any(law => law.Id == "law_succession_kinship")
            && xml.Contains("value=\"Kinship\"") && xml.Contains("{=BC_SuccessionLaw_Kinship}Kinship")
            && !xml.Contains("bloodline_elective"), "Kinship uses its own registry, enum and localization identity");
        Check(registry.Groups.Count() == 3, "Three exclusive groups: gender, succession and elective terms");
        Check(registry.InGroup("elective_terms").Select(l => l.Value).SequenceEqual(new[] { "1", "5", "10", "0" }), "Mandates offer 1/5/10 years and lifetime");
        Check(registry.InGroup("gender").Count == 5 && registry.InGroup("succession").Count == 8,
            "XML defines all existing gender/succession effects");
        var replace = AccessTools.Method(typeof(RealmLawBehavior), "TryReplace");
        foreach (string group in registry.Groups)
        foreach (var before in registry.InGroup(group))
        foreach (var after in registry.InGroup(group))
        {
            var record = new RealmLawSelectionRecord { RealmId = "realm" };
            foreach (string g in registry.Groups) record.SelectedLaws[g] = registry.DefaultFor(g);
            record.SelectedLaws[group] = before.Id;
            string other = group == "gender" ? "succession" : "gender";
            string untouched = record.SelectedLaws[other];
            var args = new object[] { record, after, before.Id, null };
            bool changed = (bool)replace.Invoke(null, args);
            Check(changed == (before.Id != after.Id), "Replacement/no-op contract: " + before.Id + " -> " + after.Id);
            Check(record.SelectedLaws.Count == 3 && record.SelectedLaws[group] == after.Id
                && record.SelectedLaws[other] == untouched, "Exactly one selection; other group untouched");
            Check(!(bool)replace.Invoke(null, new object[] { record, before, "stale_id", null })
                && record.SelectedLaws[group] == after.Id, "Stale motions cannot replace the current law");
        }
        foreach (var law in registry.InGroup("succession"))
            Check(law.GetStance(FactionType.Nobility) == CourtPolicyStance.Neutral
                && law.CrownStance == CourtPolicyStance.Neutral, "Unspecified preferences remain neutral");
        foreach (string bad in new[] {
            xml.Replace("default=\"law_gender_male_preference\"", "default=\"missing\""),
            xml.Replace("value=\"Primogeniture\"", "value=\"UnknownLaw\""),
            xml.Replace("id=\"law_succession_ultimogeniture\"", "id=\"law_succession_primogeniture\""),
            xml.Replace("category=\"hereditary\"", "category=\"elective\""),
            xml.Replace("effect=\"gender\"", "effect=\"unsupported\""),
            xml.Replace("<BellumLaws>", "<!DOCTYPE BellumLaws [<!ENTITY attack SYSTEM 'file:///nonexistent'>]><BellumLaws>") })
        {
            bool rejected = false;
            try { parse(bad); } catch (TargetInvocationException) { rejected = true; }
            Check(rejected, "Invalid law XML rejected without publishing partial definitions");
        }
        var document = new System.Xml.XmlDocument();
        document.LoadXml(xml);
        var node = document.SelectSingleNode("/BellumLaws/Group/Law");
        var stance = document.CreateElement("Stance");
        stance.SetAttribute("faction", "Nobility"); stance.SetAttribute("value", "Support"); node.AppendChild(stance);
        var crown = document.CreateElement("Crown"); crown.SetAttribute("stance", "Oppose"); node.AppendChild(crown);
        var configured = parse(document.OuterXml).Find(node.Attributes["id"].Value);
        Check(configured.GetStance(FactionType.Nobility) == CourtPolicyStance.Support
            && configured.GetStance(FactionType.Liberty) == CourtPolicyStance.Neutral
            && configured.CrownStance == CourtPolicyStance.Oppose, "Explicit bloc/Crown stances are independent");
        stance.SetAttribute("value", "99");
        bool invalidStance = false;
        try { parse(document.OuterXml); } catch (TargetInvocationException) { invalidStance = true; }
        Check(invalidStance, "Undefined numerical stance rejected");
        var fields = typeof(RealmLawSelectionRecord).GetFields();
        Check(fields.Length == 2 && fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
            "Generic realm/group selections have save metadata, no duplicate enum fields");
    }

    private static void TestDepositionSchedule()
    {
        var resolution = new CivilWarResolutionBehavior();
        var judgmentRealm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        AccessTools.Property(typeof(Kingdom), "StringId").SetValue(judgmentRealm, "deposition_test");
        var pending = AccessTools.Method(typeof(CivilWarResolutionBehavior), "HasPendingPostWarJudgments");
        foreach (string field in new[] { "_pendingSuccessionTribunalRewardKingdomIds", "_tribunalRewardKingdomIds", "_postWarExecutionRewardKingdomIds" })
        {
            var queue = (Dictionary<string, string>)AccessTools.Field(typeof(CivilWarResolutionBehavior), field).GetValue(resolution);
            queue["test"] = "another_realm";
            Check(!(bool)pending.Invoke(resolution, new object[] { judgmentRealm }), "Unrelated judgments do not delay election: " + field);
            queue["test"] = judgmentRealm.StringId;
            Check((bool)pending.Invoke(resolution, new object[] { judgmentRealm }), "Saved pending consequences block election: " + field);
            queue.Clear();
            Check(!(bool)pending.Invoke(resolution, new object[] { judgmentRealm }), "Finished consequences release election: " + field);
        }
        var schedule = AccessTools.Method(typeof(ElectiveSuccessionRecord), "ScheduleDeposition");
        var ruler = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var rival = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var caretaker = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var record = new ElectiveSuccessionRecord { Realm = realm, MandateNumber = 4, Completed = true, PlayerConfirmed = true };
        var vote = new ElectiveCommitment { Nominee = rival, Source = "persuaded", Until = CampaignTime.Never };
        record.Votes.Add(vote);
        Check((bool)schedule.Invoke(record, new object[] { ruler, caretaker, CampaignTime.Zero }), "Deposition records a deliberation window");
        Check(record.DepositionPending && record.DepositionAwaitingJudgments && record.DeposedRuler == ruler && record.Excluded == ruler
            && record.InterimHouse == caretaker && !record.Completed && !record.PlayerConfirmed && !record.Frozen,
            "Caretaker is not elected; deposed monarch excluded while ballot remains open");
        Check(record.MandateNumber == 4 && record.Votes.Single() == vote,
            "Scheduling preserves negotiated votes and grants no caretaker mandate");
        record.InterimPrepared = record.DepositionAnnounced = true;
        Check((bool)schedule.Invoke(record, new object[] { ruler, caretaker, CampaignTime.Never })
            && record.DepositionElectionDate.Equals(CampaignTime.Zero) && record.InterimPrepared && record.DepositionAnnounced,
            "Duplicate deposition cannot reset the deadline or installation/announcement receipts");
        Check(!(bool)schedule.Invoke(record, new object[] { rival, caretaker, CampaignTime.Never })
            && record.DeposedRuler == ruler, "Another demand cannot overwrite a pending deposition");
        var behavior = new ElectiveSuccessionBehavior();
        AccessTools.Field(typeof(ElectiveSuccessionBehavior), "_elections").SetValue(behavior, new List<ElectiveSuccessionRecord> { record });
        Check(AccessTools.Method(typeof(ElectiveSuccessionBehavior), "PendingDeposition").Invoke(behavior, new object[] { realm }) == record,
            "Restored behavior finds the saved deposition without a transient inquiry");
        behavior.Complete(realm);
        Check(!record.DepositionPending && !record.DepositionAwaitingJudgments && record.Completed && record.Excluded == null,
            "Completed election releases caretaker restrictions and candidacy exclusion");
        foreach (var type in new[] { typeof(ElectiveSuccessionRecord), typeof(CrownAccessionRecord) })
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var ids = fields.Select(f => f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute")
                .ConstructorArguments[0].Value).ToList();
            Check(ids.Distinct().Count() == fields.Length, type.Name + " has unique saved field IDs including deposition state");
        }
    }

    private static void TestMandates()
    {
        var dayTicks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldDayTicks = dayTicks.GetValue(null);
        var yearTicks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerYear");
        var oldTicks = yearTicks.GetValue(null);
        var start = AccessTools.Method(typeof(ElectiveSuccessionRecord), "StartMandate");
        var hero = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var next = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        try
        {
            dayTicks.SetValue(null, 10L);
            yearTicks.SetValue(null, 1000L);
            var record = new ElectiveSuccessionRecord();
            start.Invoke(record, new object[] { hero, 5, CampaignTime.Zero });
            Check(record.MandateEnd.Equals(CampaignTime.Years(5)) && record.MandateNumber == 1
                && record.MandateInitialized && record.MandateSovereign == hero, "First five-year mandate is captured");
            var previousEnd = record.MandateEnd;
            yearTicks.SetValue(null, 2000L);
            Check(record.MandateEnd.Equals(previousEnd), "Changed calendar settings do not move a saved mandate end");
            record.ElectionWarningSent = true;
            start.Invoke(record, new object[] { hero, 1, previousEnd });
            Check(record.MandateEnd.Equals(previousEnd + CampaignTime.Years(1)) && record.MandateNumber == 2
                && !record.ElectionWarningSent, "Reelection starts a full mandate with a fresh warning receipt");
            start.Invoke(record, new object[] { next, 10, previousEnd });
            Check(record.MandateStart.Equals(previousEnd) && record.MandateEnd.Equals(previousEnd + CampaignTime.Years(10))
                && record.MandateSovereign == next, "Replacement sovereign receives ten full years, not an unserved remainder");
            start.Invoke(record, new object[] { next, 0, previousEnd });
            Check(record.MandateEnd.Equals(CampaignTime.Never), "Lifetime has no finite expiry");
            bool rejected = false;
            try { start.Invoke(record, new object[] { next, 2, previousEnd }); } catch (TargetInvocationException) { rejected = true; }
            Check(rejected && record.MandateYears == 0, "Invalid mandate length cannot mutate the snapshot");
            var reform = AccessTools.Method(typeof(ElectiveSuccessionRecord), "ReformMandate");
            bool Apply(int years, CampaignTime now) => (bool)reform.Invoke(record, new object[] { years, now, 7 });
            var approved = CampaignTime.Years(20);
            int number = record.MandateNumber;
            Check(Apply(10, approved) && record.MandateStart.Equals(approved)
                && record.MandateEnd.Equals(approved + CampaignTime.Years(10)), "Lifetime to fixed starts at adoption");
            Check(record.MandateNumber == number && record.MandateSovereign == next, "Reform is not a fresh accession");
            Check(!Apply(10, approved + CampaignTime.Years(1)) && record.MandateStart.Equals(approved), "Same law cannot restart mandate");
            Check(Apply(5, approved + CampaignTime.Years(1)) && record.MandateEnd.Equals(approved + CampaignTime.Years(5)), "Shorter fixed term retains original start");
            Check(Apply(10, approved + CampaignTime.Years(2)) && record.MandateEnd.Equals(approved + CampaignTime.Years(10)), "Longer fixed term retains original start");
            Check(Apply(0, approved + CampaignTime.Years(3)) && record.MandateEnd.Equals(CampaignTime.Never), "Lifetime reform removes expiry");
            Apply(10, approved);
            var overdue = approved + CampaignTime.Years(6);
            Check(Apply(5, overdue) && record.ReformElectionPending
                && record.MandateEnd.Equals(approved + CampaignTime.Years(5))
                && record.ElectionDate.Equals(overdue + CampaignTime.Days(7)), "Overdue reform preserves legal expiry and adds deliberation");
            var scheduled = record.ElectionDate;
            Check(!Apply(0, overdue) && record.ElectionDate.Equals(scheduled), "Pending reform election cannot be cancelled by another reform");
            var restored = new ElectiveSuccessionRecord();
            foreach (var field in typeof(ElectiveSuccessionRecord).GetFields(BindingFlags.Public | BindingFlags.Instance))
                field.SetValue(restored, field.GetValue(record));
            Check(restored.ReformElectionPending && restored.ElectionDate.Equals(scheduled), "Saved fields preserve reform election date");
            start.Invoke(record, new object[] { next, 5, scheduled });
            Check(!record.ReformElectionPending && record.ElectionDate.Equals(scheduled + CampaignTime.Years(5)), "Successor clears reform deliberation");
            record.Frozen = true;
            Check(!Apply(1, approved), "Frozen ballot cannot be rewritten");
            record.Frozen = false; record.Completed = true;
            Check(!Apply(1, approved), "Completed election cannot mutate old mandate");
            record.Completed = false; record.DepositionPending = true;
            Check(!Apply(1, approved), "Interim government has no reformable mandate");
            record.DepositionPending = false;
            record.MandateInitialized = false;
            Check(!Apply(1, approved), "Uninitialized mandate is not retroactively fabricated");
        }
        finally { yearTicks.SetValue(null, oldTicks); dayTicks.SetValue(null, oldDayTicks); }

        var config = new SuccessionConfig();
        var read = AccessTools.Method(typeof(SuccessionConfig), "ReadTerm");
        var cultures = (Dictionary<string, int>)AccessTools.Field(typeof(SuccessionConfig), "_cultureTerms").GetValue(config);
        var kingdoms = (Dictionary<string, int>)AccessTools.Field(typeof(SuccessionConfig), "_kingdomTerms").GetValue(config);
        var xml = new System.Xml.XmlDocument();
        xml.LoadXml("<Kingdom id='empire' electiveTerm='5'/>");
        read.Invoke(null, new object[] { xml.DocumentElement, kingdoms });
        Check(config.GetDefaultMandateYears("empire", "empire") == 5 && config.GetDefaultMandateYears("empire", "empire_w") == 0,
            "Northern Empire override does not change Western Empire's lifetime default");
        cultures["empire"] = 10;
        Check(config.GetDefaultMandateYears("empire", "empire") == 5 && config.GetDefaultMandateYears("empire", "empire_w") == 10,
            "Explicit kingdom term wins over culture; omitted kingdom inherits culture");
        var ruler = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var accession = new CrownAccessionRecord { Realm = realm, Predecessor = ruler, MandateExpiry = true };
        var behavior = new CrownAccessionBehavior();
        AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions").SetValue(behavior, new List<CrownAccessionRecord> { accession });
        Check(behavior.OwnsDeath(realm, ruler), "Pending expiry owns coincident incumbent death");
        accession.Completed = true;
        Check(!behavior.OwnsDeath(realm, ruler), "Completed reelection does not swallow the incumbent's later death");
    }

    private static void TestBilateralMarriage()
    {
        var type = typeof(CrownAccessionRecord).Assembly.GetType("BellumCivile.Behaviors.MarriageOutcome");
        var accept = AccessTools.Method(type, "BothAccept");
        var rank = AccessTools.Method(type, "Rank");
        foreach (float first in new[] { -200f, 0f, 94f, 95f, 180f, 1000f })
        foreach (float second in new[] { -200f, 0f, 94f, 95f, 180f, 1000f })
        {
            bool allowed = (bool)accept.Invoke(null, new object[] { first, second, false, false, 95f });
            Check(allowed == (first >= 95f && second >= 95f), "Both NPC houses independently accept " + first + "/" + second);
            Check(allowed == (bool)accept.Invoke(null, new object[] { second, first, false, false, 95f }),
                "Acceptance cannot change by swapping proposal direction");
            Check((float)rank.Invoke(null, new object[] { first, second, false, false }) == Math.Min(first, second),
                "Stronger house benefit cannot conceal weaker house refusal");
            Check((bool)accept.Invoke(null, new object[] { first, second, true, false, 95f }) == (second >= 95f),
                "Player decides their side while the NPC still requires acceptance");
        }
        var offset = AccessTools.Method(type, "AnnualOffset");
        foreach (int days in new[] { 1, 84, 365 })
        foreach (string id in new[] { "clan_empire_south_1", "clan_vlandia_1", "player", "" })
        {
            int day = (int)offset.Invoke(null, new object[] { id, days });
            Check(day >= 0 && day < days && day == (int)offset.Invoke(null, new object[] { id, days }),
                "Stable bounded house date: " + id + "/" + days);
        }
        var titles = typeof(FeudalTitleBehavior);
        var hook = PatchProcessor.GetOriginalInstructions(AccessTools.Method(titles, "OnBeforeHeroesMarried"));
        Check(hook.Count(i => i.Calls(AccessTools.Method(titles, "RegisterMarriageBirthrightsForDepartingHero"))) == 2,
            "Shared marriage title hook registers both departing participants");
        Check(AccessTools.DeclaredMethod(titles, "IsRoyalHeiressMarriage") == null,
            "No royal-heiress exclusion from ordinary personal birthright registration");
        Check(AccessTools.DeclaredMethod(typeof(DynasticHeirBehavior), "TryResolveDynasticMarriageClanOverride") == null,
            "Foreign-only royal household exception is retired");
        var strategy = typeof(StrategicMarriageBehavior);
        Check(AccessTools.DeclaredMethod(strategy, "OnDailyTickHero") == null
            && AccessTools.DeclaredMethod(strategy, "OnDailyTickClan") != null,
            "Marriage scheduling belongs to houses, not individual men");
    }

    private static void TestMarriageCadetRetirement()
    {
        var type = typeof(DynasticHeirBehavior);
        foreach (string method in new[] { "CreateCadetBranch", "ApplyCadetMarriageDowry",
            "ProcessPendingCadetMarriages", "OnHourlyTick", "OnBarterAccepted" })
            Check(AccessTools.DeclaredMethod(type, method) == null,
                "Retired marriage machinery absent from compiled behavior: " + method);
        var hook = PatchProcessor.GetOriginalInstructions(AccessTools.Method(type, "OnBeforeHeroesMarried"));
        var rights = AccessTools.Method(type, "TrackMarriageRights");
        Check(hook.Count(i => i.Calls(rights)) == 2, "Marriage hook checks rights for both spouses");
        var sync = PatchProcessor.GetOriginalInstructions(AccessTools.Method(type, "SyncData"));
        Check(sync.Any(i => i.Calls(AccessTools.Method(typeof(List<PendingCadetMarriageRecord>), "Clear"))),
            "Loaded marriage cadet queue is discarded without executing old endowments");
        Check(AccessTools.Method(type, "BuildRoyalHeiressCadetName") != null,
            "Accession retains the shared combined-house naming helper");
    }

    private static void TestTitlePackageParity()
    {
        var assembly = typeof(CrownAccessionRecord).Assembly;
        var planType = assembly.GetType("BellumCivile.FeudalInheritancePlan");
        var planner = assembly.GetType("BellumCivile.FeudalInheritancePlanner");
        Func<object, string, IList> list = (o, n) => (IList)AccessTools.Property(o.GetType(), n).GetValue(o);
        var source = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var foreign = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        AccessTools.Property(typeof(Clan), "StringId").SetValue(source, "source");
        var heirs = Enumerable.Range(0, 3).Select(_ => (Hero)FormatterServices.GetUninitializedObject(typeof(Hero))).ToArray();
        var towns = Enumerable.Range(0, 4).Select(i =>
        {
            var settlement = (Settlement)FormatterServices.GetUninitializedObject(typeof(Settlement));
            AccessTools.Property(typeof(Settlement), "StringId").SetValue(settlement, "f" + i);
            var party = (TaleWorlds.CampaignSystem.Party.PartyBase)FormatterServices.GetUninitializedObject(typeof(TaleWorlds.CampaignSystem.Party.PartyBase));
            AccessTools.Property(party.GetType(), "Settlement").SetValue(party, settlement);
            var town = (Town)FormatterServices.GetUninitializedObject(typeof(Town));
            AccessTools.Field(typeof(SettlementComponent), "_owner").SetValue(town, party);
            AccessTools.Field(typeof(Town), "_ownerClan").SetValue(town, source);
            AccessTools.Field(typeof(Town), "_prosperity").SetValue(town, new[] { 100f, 900f, 200f, 50f }[i]);
            return town;
        }).ToArray();
        var titleList = new List<FeudalTitleRecord>();
        Action<string, FeudalTitleType, string, string> add = (id, rank, parent, capital) =>
            titleList.Add(new FeudalTitleRecord(id, id, rank, "source", "source", parent, capital, "realm", 0, 0));
        add("crown_a", FeudalTitleType.Kingdom, "", "f0");
        add("duchy_a", FeudalTitleType.Duchy, "crown_a", "f0");
        add("county_a", FeudalTitleType.County, "duchy_a", "f0");
        add("b0", FeudalTitleType.Barony, "county_a", "f0");
        add("duchy_extra", FeudalTitleType.Duchy, "crown_a", "f3");
        add("county_extra", FeudalTitleType.County, "duchy_extra", "f3");
        add("b3", FeudalTitleType.Barony, "county_extra", "f3");
        add("crown_b", FeudalTitleType.Kingdom, "", "f1");
        add("duchy_b", FeudalTitleType.Duchy, "crown_b", "f1");
        add("county_b", FeudalTitleType.County, "duchy_b", "f1");
        add("b1", FeudalTitleType.Barony, "county_b", "f1");
        add("county_b2", FeudalTitleType.County, "duchy_b", "f2");
        add("b2", FeudalTitleType.Barony, "county_b2", "f2");
        var titles = new FeudalTitleBehavior();
        AccessTools.Field(typeof(FeudalTitleBehavior), "_titlesById").SetValue(titles, titleList.ToDictionary(t => t.TitleId));
        var children = (Dictionary<string, List<string>>)AccessTools.Field(typeof(FeudalTitleBehavior), "_childrenByParentTitleId").GetValue(titles);
        foreach (var group in titleList.Where(t => t.ParentTitleId != "").GroupBy(t => t.ParentTitleId))
            children[group.Key] = group.Select(t => t.TitleId).ToList();
        var baronies = (Dictionary<string, string>)AccessTools.Field(typeof(FeudalTitleBehavior), "_baronyTitleBySettlementId").GetValue(titles);
        foreach (var t in titleList.Where(t => t.TitleType == FeudalTitleType.Barony)) baronies[t.CapitalSettlementId] = t.TitleId;
        var snapshot = AccessTools.Method(typeof(PartitionSuccessionBehavior), "SnapshotEstateShare");
        Func<int, object> makePlan = count =>
        {
            object plan = Activator.CreateInstance(planType, true);
            AccessTools.Property(planType, "ParentClan").SetValue(plan, source);
            foreach (var t in towns.OrderByDescending(t => t.Prosperity)) list(plan, "EstateFiefs").Add(t);
            foreach (var t in titleList) list(plan, "EstateTitles").Add(t);
            foreach (var h in heirs.Take(count)) list(plan, "NaturalHeirs").Add(h);
            AccessTools.Method(planner, "BuildTitlePackages").Invoke(null, new[] { plan, titles, (object)1, "crown_a" });
            return plan;
        };
        var crownFirst = AccessTools.Method(planner, "TryBuildCrownFirstTitlePackages");
        Func<object, int, bool> buildCrownFirst = (plan, reserve) =>
        {
            var args = new object[] { plan, titles, titleList, reserve, "crown_a", null };
            bool ok = (bool)crownFirst.Invoke(null, args);
            Check(ok || !string.IsNullOrEmpty(args[5] as string), "Crown-first allocation explains refusal");
            return ok;
        };
        var crownPlan = makePlan(2);
        Check(buildCrownFirst(crownPlan, 99), "Crown-first estate allocation accepts full topology");
        var crownPackage = list(crownPlan, "SecondaryPackages")[0];
        Check(list(crownPackage, "Fiefs").Cast<Town>().SequenceEqual(new[] { towns[1], towns[2] }),
            "Even oversized personal reservation cannot consume secondary Crown land");
        Check(list(crownPlan, "ReservedPersonalFiefs").Cast<Town>().SequenceEqual(new[] { towns[0], towns[3] }),
            "Royal personal reservation draws only from remaining estate");
        var crownPrimaryShare = AccessTools.Method(planner, "GetPrimaryHeirShare")
            .Invoke(null, new[] { crownPlan, heirs[0], (object)true });
        Check(list(crownPrimaryShare, "Fiefs").Count == 2
            && list(crownPrimaryShare, "Titles").Cast<FeudalTitleRecord>().All(t => !t.TitleId.EndsWith("_b") && t.TitleId != "b1" && t.TitleId != "b2" && t.TitleId != "county_b2"),
            "Primary heir retains own Crown without another heir's Crown package");
        Check(buildCrownFirst(crownPlan, 99) && list(crownPlan, "SecondaryPackages").Count == 1,
            "Repeated Crown planning does not duplicate packages");
        var soleCrownPlan = makePlan(1);
        Check(buildCrownFirst(soleCrownPlan, 99), "Single heir can plan both Crowns");
        var soleCrownShare = AccessTools.Method(planner, "GetPrimaryHeirShare")
            .Invoke(null, new[] { soleCrownPlan, heirs[0], (object)true });
        Check(list(soleCrownShare, "Fiefs").Count == 4 && list(soleCrownShare, "Titles").Count == titleList.Count,
            "Single heir retains all Crown packages");
        var crownNoPartition = AccessTools.Method(planner, "GetPrimaryHeirShare")
            .Invoke(null, new[] { crownPlan, heirs[0], (object)false });
        Check(list(crownNoPartition, "Fiefs").Count == 4 && list(crownNoPartition, "Titles").Count == titleList.Count,
            "Disabled partition retains complete Crown estate");
        var invalidCrownPlan = makePlan(2);
        var threeCrownPlan = makePlan(3);
        Check(buildCrownFirst(threeCrownPlan, 1), "Crown-first plan supports additional lesser-estate heirs");
        Check(((FeudalTitleRecord)AccessTools.Property(list(threeCrownPlan, "SecondaryPackages")[0].GetType(), "RootTitle")
            .GetValue(list(threeCrownPlan, "SecondaryPackages")[0])).TitleId == "crown_b"
            && list(threeCrownPlan, "SecondaryPackages").Count == 2,
            "Secondary Crown precedes lesser landed package for later heirs");
        var oldPackage = list(invalidCrownPlan, "SecondaryPackages")[0];
        list(invalidCrownPlan, "EstateTitles").Remove(titleList.Single(t => t.TitleId == "crown_b"));
        Check(!buildCrownFirst(invalidCrownPlan, 1)
            && ReferenceEquals(oldPackage, list(invalidCrownPlan, "SecondaryPackages")[0]),
            "Invalid Crown snapshot leaves existing allocation untouched");
        var vassalOnly = makePlan(2);
        foreach (var id in new[] { "duchy_b", "county_b", "county_b2", "b1", "b2" })
        {
            var title = titleList.Single(t => t.TitleId == id);
            list(vassalOnly, "EstateTitles").Remove(title);
            title.SetDeJureHolder("vassal"); title.SetDeFactoHolder("vassal");
        }
        list(vassalOnly, "EstateFiefs").Remove(towns[1]);
        list(vassalOnly, "EstateFiefs").Remove(towns[2]);
        Check(buildCrownFirst(vassalOnly, 1), "Vassal-only Crown survives estate allocation");
        var vassalPackage = list(vassalOnly, "SecondaryPackages")[0];
        Check(list(vassalPackage, "Fiefs").Count == 0
            && list(vassalPackage, "Titles").Cast<FeudalTitleRecord>().Single().TitleId == "crown_b"
            && AccessTools.Property(vassalPackage.GetType(), "PrimaryFief").GetValue(vassalPackage) == null,
            "Vassal-only package neither confiscates land nor invents an owned residence");
        list(vassalOnly, "EstateFiefs").Clear();
        Check(buildCrownFirst(vassalOnly, 1), "Crown allocation works with no royal personal fiefs anywhere");
        foreach (var id in new[] { "duchy_b", "county_b", "county_b2", "b1", "b2" })
        {
            var title = titleList.Single(t => t.TitleId == id);
            title.SetDeJureHolder("source"); title.SetDeFactoHolder("source");
        }
        foreach (int count in new[] { 1, 2, 3 })
        {
            foreach (var h in heirs) AccessTools.Field(typeof(Hero), "_clan").SetValue(h, source);
            object local = makePlan(count);
            Check(list(local, "ReservedPersonalFiefs").Cast<Town>().Single() == towns[0],
                "Preferred sovereign capital is reserved ahead of richest fief; heirs=" + count);
            AccessTools.Field(typeof(Hero), "_clan").SetValue(heirs[0], foreign);
            object cross = makePlan(count);
            for (int i = 0; i < count; i++)
            {
                var expectedPackage = AccessTools.Method(planner, "GetLivingAccessionShare")
                    .Invoke(null, new[] { local, heirs[0], heirs[i], (object)true });
                var actual = (CrossClanEstateShare)snapshot.Invoke(null, new[] { cross, heirs[0], heirs[i], (object)true });
                var expectedFiefs = list(expectedPackage, "Fiefs").Cast<Town>().Select(t => t.Settlement.StringId).ToArray();
                var expectedTitles = list(expectedPackage, "Titles").Cast<FeudalTitleRecord>().Select(t => t.TitleId)
                    .Concat(titleList.Where(t => t.TitleType == FeudalTitleType.Barony && expectedFiefs.Contains(t.CapitalSettlementId))
                        .Select(t => t.TitleId)).Distinct().OrderBy(id => id);
                Check(actual.Fiefs.SequenceEqual(expectedFiefs) && actual.Titles.OrderBy(id => id).SequenceEqual(expectedTitles),
                    "Cross-clan journal matches original title package; heirs=" + count + "; share=" + i);
            }
            var primary = (CrossClanEstateShare)snapshot.Invoke(null, new[] { cross, heirs[0], heirs[0], (object)false });
            Check(primary.Fiefs.Count == 4 && primary.Titles.Count == titleList.Count,
                "Partition disabled preserves the whole full-rank estate; heirs=" + count);
            var secondary = (CrossClanEstateShare)snapshot.Invoke(null, new[] { cross, heirs[0], heirs[1], (object)false });
            Check(secondary == null, "Disabled partition does not invent secondary shares; heirs=" + count);
        }
        var multi = makePlan(3);
        var establish = AccessTools.Method(planner, "GetHouseholdEstablishmentShare");
        var sole = makePlan(1);
        var whole = AccessTools.Method(planner, "GetLivingAccessionShare")
            .Invoke(null, new[] { sole, heirs[0], heirs[0], (object)true });
        var partial = establish.Invoke(null, new[] { sole, titles });
        Check(list(sole, "SecondaryPackages").Contains(partial) && !list(partial, "Fiefs").Contains(towns[0]),
            "Living concession uses an existing secondary package and retains the ruler's capital");
        Check(list(whole, "Fiefs").Count == 4, "Negotiating land does not change the full Crown/death inheritance share");
        var seized = establish.Invoke(null, new[] { sole, titles });
        Check(list(seized, "Fiefs").Count == 1 && list(seized, "Fiefs").Contains(towns[3]),
            "Hostile seizure selects one secondary landed package, excluding primary and sovereign packages");
        Check(list(whole, "Fiefs").Count == 4, "Hostile selection leaves surrender inheritance intact");
        children["county_a"].Add("b3");
        var protectedShare = establish.Invoke(null, new[] { sole, titles });
        Check(list(protectedShare, "Fiefs").Count == 1 && list(protectedShare, "Fiefs").Contains(towns[3])
            && list(protectedShare, "Titles").Cast<FeudalTitleRecord>().All(t => t.TitleType == FeudalTitleType.Barony),
            "Without secondary package, one non-capital barony is split off without its county or duchy");
        var loose = Activator.CreateInstance(assembly.GetType("BellumCivile.FeudalInheritancePackage"), true);
        list(loose, "Fiefs").Add(towns[1]);
        list(loose, "Titles").Add(titleList.Single(t => t.TitleId == "b1"));
        list(sole, "SecondaryPackages").Add(loose);
        var separateBarony = establish.Invoke(null, new[] { sole, titles });
        Check(ReferenceEquals(separateBarony, loose) && !list(separateBarony, "Fiefs").Contains(towns[0])
            && !list(separateBarony, "Fiefs").Contains(towns[3]),
            "Two-baronies primary county stays intact while the standalone secondary barony can be seized");
        list(sole, "SecondaryPackages").Remove(loose);
        children["county_a"].Remove("b3");
        var siblingGrant = establish.Invoke(null, new[] { multi, titles });
        Check(list(siblingGrant, "Fiefs").Count == 1 && list(siblingGrant, "Fiefs").Contains(towns[3]),
            "Household grant can use a package allocated to a sibling; claimant share is not an input");
        var two = makePlan(1);
        list(two, "EstateFiefs").Clear();
        list(two, "EstateFiefs").Add(towns[0]); list(two, "EstateFiefs").Add(towns[3]);
        list(two, "SecondaryPackages").Clear();
        var twoShare = AccessTools.Method(planner, "GetLivingAccessionShare")
            .Invoke(null, new[] { two, heirs[0], heirs[0], (object)true });
        var one = establish.Invoke(null, new[] { two, titles });
        Check(list(one, "Fiefs").Count == 1 && list(one, "Fiefs").Contains(towns[3]),
            "Two-baronies sole heir receives one negotiated barony, not both");
        Check(list(one, "Titles").Cast<FeudalTitleRecord>().All(t => t.TitleType == FeudalTitleType.Barony
            && t.CapitalSettlementId == "f3"), "Trimmed barony grant does not take the retained royal title chain");
        list(two, "EstateFiefs").Remove(towns[3]);
        Check(list(establish.Invoke(null, new[] { two, titles }), "Fiefs").Count == 0,
            "Negotiation never gives away the ruler's last fief");
        var coequal = (CrossClanEstateShare)snapshot.Invoke(null, new[] { multi, heirs[0], heirs[1], (object)true });
        Check(coequal.RootTitleId == "crown_b" && coequal.Fiefs.Count == 2 && coequal.Titles.Contains("county_b2"),
            "Coequal Crown and its duchy/county estate remain one saved secondary package");
        var routing = AccessTools.Method(typeof(PartitionSuccessionBehavior), "IsUnsupportedSovereignPackage");
        var record = new CrossClanEstateRecord { RealmCrownTitleId = "crown_a" };
        Check((bool)routing.Invoke(null, new object[] { record, titleList.Where(t => coequal.Titles.Contains(t.TitleId)) }),
            "Unsupported additional Crown is explicitly gated after preserving its full package");
        Check(!(bool)routing.Invoke(null, new object[] { record, titleList.Where(t => t.TitleId == "crown_a") }),
            "Realm's own Crown is handed off to accession instead of generic land transfer");
        var needs = AccessTools.Method(typeof(PartitionSuccessionBehavior), "NeedsLandedSettlement");
        var pending = new CrossClanEstateShare { Heir = heirs[0] };
        Check((bool)needs.Invoke(null, new object[] { pending, heirs[0] }), "Crown waits for undelivered estate land");
        pending.LandedSettled = true;
        Check(!(bool)needs.Invoke(null, new object[] { pending, heirs[0] }) && !pending.Completed,
            "Landed receipt releases Crown handoff without falsely completing the Crown-title package");
    }

    private static void TestRoyalCadetNames()
    {
        var royal = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var married = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var build = AccessTools.Method(typeof(DynasticHeirBehavior), "BuildRoyalHeiressCadetName");
        Action<Clan, string> name = (clan, value) => AccessTools.Property(typeof(Clan), "Name")
            .SetValue(clan, new TaleWorlds.Localization.TextObject("{=!}" + value));
        Func<string> combined = () => build.Invoke(null, new object[] { royal, married }).ToString();
        name(royal, "Pethros");
        name(married, "Froringing");
        Check(combined() == "Pethros-Froringing", "Heiress cadet combines royal dynasty then marital house");
        var heir = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var spouse = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        AccessTools.Field(typeof(Hero), "_clan").SetValue(heir, married);
        AccessTools.Field(typeof(Hero), "_clan").SetValue(spouse, married);
        heir.Spouse = spouse;
        var namingPlan = new CrownAccessionRecord { PreviousHouse = royal, IncomingSourceHouse = married, Heir = heir };
        var partition = new PartitionSuccessionBehavior();
        var preview = AccessTools.Method(typeof(PartitionSuccessionBehavior), "PreviewAbdicationCadetName");
        Check((string)preview.Invoke(partition, new object[] { namingPlan }) == "Pethros-Froringing",
            "Male default hero uses the same combined Crown cadet name");

        var deceased = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        AccessTools.Field(typeof(Hero), "_heroState").SetValue(deceased, Hero.CharacterStates.Dead);
        var share = new CrossClanEstateShare { Heir = heir, Fiefs = new List<string> { "inherited_fief" } };
        var estate = new CrossClanEstateRecord { Deceased = deceased, Shares = new List<CrossClanEstateShare>
            { share, new CrossClanEstateShare { Heir = spouse, Fiefs = new List<string> { "siblings_fief" } } } };
        AccessTools.Field(typeof(PartitionSuccessionBehavior), "_crossClanEstates").SetValue(partition,
            new List<CrossClanEstateRecord> { estate });
        var fiefIds = AccessTools.Method(typeof(PartitionSuccessionBehavior), "GetCadetNamingFiefIds");
        Func<string[]> roots = () => ((IEnumerable<string>)fiefIds.Invoke(partition, new object[] { namingPlan })).ToArray();
        Check(roots().SequenceEqual(new[] { "inherited_fief" }), "Naming sees the heir's frozen death share but not a sibling's fief");
        Check(namingPlan.EndowmentFiefs.Count == 0 && share.DeliveredFiefs.Count == 0,
            "Naming neither copies death assets into living endowments nor marks them delivered");
        namingPlan.EndowmentFiefs.Add("advance_fief");
        Check(roots().SequenceEqual(new[] { "advance_fief", "inherited_fief" }),
            "Formation endowment keeps naming priority before separately pending inheritance");
        namingPlan.EndowmentFiefs.Clear();
        share.Completed = true;
        Check(roots().Length == 0, "Completed historical shares cannot name a future cadet");
        share.Completed = false;
        estate.Completed = true;
        Check(roots().Length == 0, "Completed estates do not supply a stale naming fief");
        estate.Completed = false;
        AccessTools.Field(typeof(Hero), "_heroState").SetValue(deceased, Hero.CharacterStates.Active);
        Check(roots().Length == 0, "A living parent's death snapshot does not supply inheritance naming");
        var culture = (CultureObject)FormatterServices.GetUninitializedObject(typeof(CultureObject));
        married.Culture = culture;
        foreach (var pair in new[] { new[] { "vlandia", "dey Meroc", "Meroc" },
            new[] { "battania", "fen Gruffendoc", "Gruffendoc" }, new[] { "aserai", "Banu Hulyan", "Hulyan" } })
        {
            AccessTools.Property(typeof(CultureObject), "StringId").SetValue(culture, pair[0]);
            name(married, pair[1]);
            Check(combined() == "Pethros-" + pair[2], "Combined name retains existing " + pair[0] + " prefix stripping");
        }
        name(married, "  Banu   Hulyan  ");
        Check(combined() == "Pethros-Hulyan", "Existing heiress formatter normalizes name whitespace");
        name(married, "");
        Check(combined() == "Pethros", "Missing marital name retains the dynasty rather than an empty suffix");
    }

    private static void TestCrownClaimCarrier()
    {
        var move = AccessTools.Method(typeof(FeudalClaimRecord), "MoveWithCarrier");
        var claim = new FeudalClaimRecord("stable_id", "player", "maternal_title",
            FeudalClaimStrength.Strong, "royal_heiress_player_marriage", "princess", "birth_house",
            20f, 90f, 1, "princess");
        Func<string, string, string, bool> transfer = (hero, from, to) =>
            (bool)move.Invoke(claim, new object[] { hero, from, to });
        Check(!transfer("spouse", "player", "cadet"), "Spouse cannot take the princess's personal claim");
        Check(!transfer("princess", "unrelated", "cadet"), "Only claims of the departing household move");
        Check(!transfer("princess", "player", null), "Claim cannot move to a missing household");
        Check(transfer("princess", "player", "cadet") && claim.ClaimantClanId == "cadet",
            "Princess carries her claim into her ruling cadet");
        Check(claim.ClaimId == "stable_id" && claim.CarrierHeroId == "princess"
            && claim.SourceHeroId == "princess" && claim.OriginClanId == "birth_house"
            && claim.Source == "royal_heiress_player_marriage" && claim.TargetTitleId == "maternal_title",
            "Moving a carrier preserves claim identity and provenance");
        Check(claim.Strength == FeudalClaimStrength.Strong && claim.GenerationDepth == 1
            && claim.CreatedDay == 20f && claim.ExpiresDay == 90f,
            "Household movement neither weakens claims nor renews their expiry");
        Check(!transfer("princess", "player", "cadet"), "Replaying Crown claim movement is a no-op");
        claim.SetActive(false);
        Check(!transfer("princess", "cadet", "another"), "Moving a carrier cannot revive an inactive claim");
    }

    private static Hero VisitPlayer;
    private static Dictionary<Hero, Settlement> VisitSettlements;
    private static Dictionary<Hero, TaleWorlds.CampaignSystem.Party.MobileParty> VisitParties;
    private static float VisitRoll;
    private static bool VisitMainHero(ref Hero __result) { __result = VisitPlayer; return false; }

    private static void TestRivalCrownPromotion()
    {
        var promote = AccessTools.Method(typeof(WarScoreRecord), "TryPromoteRivalry");
        foreach (bool reversed in new[] { false, true })
        {
            string attacker = reversed ? "winner" : "survivor", defender = reversed ? "survivor" : "winner";
            var fief = new WarScoreFiefSnapshotRecord("castle", "winner", "house", false, true);
            var war = new WarScoreRecord("survivor|winner", attacker, defender, 123, new[] { fief },
                WarScoreConflictType.CivilWar, "bc_rival_test");
            war.SetOccupationScore(4); war.AddBattleScore(5, 100); war.AddRaidScore(6, 100);
            war.AddPrisonerScore(7, 100); war.AddTickingScore(8, 100, 125); war.AddObjectiveScore(9, 100);
            war.SetLandlessPressureScore(10, 100); war.ScheduleNextWhitePeaceCheck(140);
            var entry = new WarScoreEventRecord(WarScoreEventType.WarEnded, 124, 5, 12, attacker, defender);
            war.AddEvent(entry);
            string eventId = entry.EventId;
            float before = war.Score, sign = reversed ? -1 : 1;
            Check((bool)promote.Invoke(war, new object[] { "winner", "survivor", "crown" }), "Either rivalry orientation can become a Crown war");
            Check(war.AttackerKingdomId == "survivor" && war.DefenderKingdomId == "crown"
                && war.ContextId == "survivor" && war.WarKey == "crown|survivor", "Promoted war routes to the remaining claimant");
            Check(war.Score == sign * before && war.OccupationScore == sign * 4 && war.BattleScore == sign * 5
                && war.RaidScore == sign * 6 && war.PrisonerScore == sign * 7 && war.TickingScore == sign * 8
                && war.ObjectiveScore == sign * 9 && war.LandlessPressureScore == sign * 10, "All score components retain their side-relative meaning");
            Check(war.StartedDay == 123 && war.LastTickDay == 125 && war.NextWhitePeaceCheckDay == 140
                && war.OriginalWarKey == "survivor|winner", "Promotion preserves war history and dates");
            Check(entry.Delta == sign * 5 && entry.ScoreAfter == sign * 12 && entry.EventId == eventId
                && entry.ActorKingdomId == attacker && entry.TargetKingdomId == defender && entry.Day == 124,
                "History changes score perspective but keeps original actors and event identity");
            Check(fief.OwnerKingdomId == "crown" && fief.OriginalOwnerKingdomId == "winner"
                && fief.OwnerClanId == "house", "Fief accounting follows the winning house into the Crown without losing provenance");
            Check((bool)promote.Invoke(war, new object[] { "winner", "survivor", "crown" })
                && war.Score == sign * before && entry.Delta == sign * 5, "Retry cannot reverse scores twice");
            Check((bool)AccessTools.Method(typeof(WarScoreRecord), "TryRetargetCivilWarDefender")
                .Invoke(war, new object[] { "crown", "restored" }), "Promoted rivalry transfers onward into a restored successor realm");
            Check(war.AttackerKingdomId == "survivor" && war.DefenderKingdomId == "restored"
                && war.Score == sign * before && war.StartedDay == 123 && war.OriginalWarKey == "survivor|winner",
                "Outside collapse preserves the rival war rather than the obsolete Crown history");
            Check(fief.OwnerKingdomId == "restored" && fief.OriginalOwnerKingdomId == "winner"
                && entry.EventId == eventId && entry.Delta == sign * 5, "Both transitions preserve fief provenance and event identity");
            Check((bool)AccessTools.Method(typeof(WarScoreRecord), "TryRetargetCivilWarDefender")
                .Invoke(war, new object[] { "crown", "restored" }) && war.Score == sign * before,
                "Restored Crown transfer retries do not replay the rivalry perspective change");
        }
        foreach (string state in new[] { "ended", "resolution", "parley", "whitepeace", "terminal", "foreign", "unrelated" })
        {
            var war = new WarScoreRecord("survivor|winner", "winner", "survivor", 1, null,
                state == "foreign" ? WarScoreConflictType.ForeignWar : WarScoreConflictType.CivilWar,
                state == "unrelated" ? "other" : "bc_rival_test");
            war.AddBattleScore(10, 100);
            if (state == "ended") war.MarkEnded(2);
            if (state == "resolution") war.BeginResolution();
            if (state == "parley") war.BeginParley(false, 2);
            if (state == "whitepeace") war.SetWhitePeaceOfferPending(true);
            if (state == "terminal") AccessTools.Field(typeof(WarScoreRecord), "_terminalResolutionQueued").SetValue(war, true);
            Check(!(bool)promote.Invoke(war, new object[] { "winner", "survivor", "crown" })
                && war.AttackerKingdomId == "winner" && war.Score == 10, "Invalid or unsettled rivalry is not mutated: " + state);
        }
        var crown = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var winnerRealm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var rivalRealm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var protection = new CivilWarRivalPromotionRecord { Conflict = new CivilWarConflictRecord { CrownRealm = crown },
            Winner = new CivilWarSideRecord { Realm = winnerRealm }, Survivor = new CivilWarSideRecord { Realm = rivalRealm } };
        var protects = AccessTools.Method(typeof(CivilWarRivalPromotionRecord), "Protects");
        foreach (bool completed in new[] { false, true })
        {
            protection.Completed = completed;
            foreach (var realm in new[] { crown, winnerRealm, rivalRealm })
                Check((bool)protects.Invoke(protection, new object[] { realm }) == !completed,
                    "All three participants are protected until promotion commits");
        }
        Check(typeof(CivilWarRivalPromotionRecord).GetFields().All(f =>
            f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")), "Promotion checkpoints are saved");
        var collapseOwner = new CivilWarCollapseRecord { Promotion = protection };
        protection.CollapseOwner = collapseOwner;
        Check(protection.CollapseOwner.Promotion == protection, "Collapse and promotion retain one shared recovery identity");
        Check(typeof(CivilWarCollapseRecord).GetField("Promotion").GetCustomAttributesData()
            .Any(a => a.AttributeType.Name == "SaveableFieldAttribute"), "Collapse saves its rivalry promotion reference");
        var winningFaction = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
        var survivingFaction = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
        var continuingScore = new WarScoreRecord("crown|survivor", "survivor", "crown", 1, null, WarScoreConflictType.CivilWar, "survivor");
        var graph = new CivilWarConflictRecord
        {
            Sides = new List<CivilWarSideRecord> { new CivilWarSideRecord { Id = "winner", Faction = winningFaction },
                new CivilWarSideRecord { Id = "survivor", Faction = survivingFaction } },
            Pairs = new List<CivilWarPairRecord> {
                new CivilWarPairRecord { AttackerSideId = "winner", DefenderSideId = "crown" },
                new CivilWarPairRecord { AttackerSideId = "survivor", DefenderSideId = "crown", Score = continuingScore },
                new CivilWarPairRecord { AttackerSideId = "winner", DefenderSideId = "survivor", Score = continuingScore, Closed = true } }
        };
        AccessTools.Method(typeof(CivilWarConflictRecord), "Complete").Invoke(graph, new object[] { winningFaction });
        Check(!graph.Closed && graph.Sides[0].Closed && !graph.Sides[1].Closed
            && graph.Pairs[0].Closed && !graph.Pairs[1].Closed && graph.Pairs[1].Score.IsActive,
            "Completing the crowned claimant preserves the promoted surviving matchup");
    }

    private static void TestCivilWarCrownTransfer()
    {
        var destroyInternal = AccessTools.Method(typeof(DestroyKingdomAction), "ApplyInternal", new[] { typeof(Kingdom), typeof(bool) });
        Check(destroyInternal != null && destroyInternal.ReturnType == typeof(void),
            "Loaded native destruction entry exposes the pre-deactivation interception target");
        Check(destroyInternal.GetParameters()[0].Name == "destroyedKingdom"
            && destroyInternal.GetParameters()[1].Name == "isKingdomLeaderDeath",
            "Destruction prefix argument names match the installed game assembly");
        var destructionPatch = typeof(CivilWarCollapseRecord).Assembly.GetType("BellumCivile.Patches.CivilWarParentDestructionPatch");
        var destructionPrefix = AccessTools.Method(destructionPatch, "Prefix");
        Check((bool)destructionPrefix.Invoke(null, new object[] { null, true }),
            "Leader-death destruction bypasses ordinary foreign-collapse reservation");
        Check((bool)destructionPrefix.Invoke(null, new object[] { null, false }),
            "No-campaign destruction does not invent or reserve a successor");
        var advance = AccessTools.Method(typeof(CivilWarCrownTransferRecord).Assembly.GetType("BellumCivile.CivilWarTransferRules"), "Advance");
        for (int stop = 0; stop < 4; stop++)
        {
            var pair = new CivilWarPairTransferRecord();
            var visits = new int[4];
            int failAt = stop;
            Func<int, bool> interrupted = stage => { visits[stage]++; return stage != failAt; };
            Check(!(bool)advance.Invoke(null, new object[] { pair, interrupted }) && pair.Stage == stop,
                "Failed transfer stage retains its retry checkpoint: " + stop);
            Func<int, bool> resume = stage => { visits[stage]++; return true; };
            Check((bool)advance.Invoke(null, new object[] { pair, resume }) && pair.Stage == 4,
                "Interrupted Crown transfer reaches completion on retry: " + stop);
            Check(Enumerable.Range(0, stop).All(i => visits[i] == 1) && visits[stop] == 2,
                "Recovery does not repeat stages completed before interruption");
            int calls = visits.Sum();
            advance.Invoke(null, new object[] { pair, resume });
            Check(visits.Sum() == calls, "Completed Crown transfer stage sequence cannot replay");
        }
        var throwingPair = new CivilWarPairTransferRecord();
        try
        {
            advance.Invoke(null, new object[] { throwingPair, new Func<int, bool>(stage =>
                { if (stage == 2) throw new InvalidOperationException("simulated callback failure"); return true; }) });
            Check(false, "Expected injected transfer failure");
        }
        catch (TargetInvocationException) { Check(throwingPair.Stage == 2, "Thrown callbacks retain the incomplete stage receipt"); }
        Check(!(bool)advance.Invoke(null, new object[] { new CivilWarPairTransferRecord { Stage = 99 }, new Func<int, bool>(_ => true) }),
            "Invalid transfer checkpoint cannot be mistaken for completion");

        var rebel = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var oldCrown = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var successor = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var unrelated = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var faction = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
        var parent = AccessTools.Property(typeof(FactionObject), "ParentKingdom");
        parent.SetValue(faction, oldCrown);
        var challenge = new SuccessionChallengeRecord { Realm = oldCrown, WarFaction = faction };
        var warRealm = AccessTools.Property(typeof(SuccessionChallengeRecord), "WarRealm");
        Check(warRealm.GetValue(challenge) == oldCrown, "Challenge initially opposes its original Crown realm");
        parent.SetValue(faction, successor);
        Check(warRealm.GetValue(challenge) == successor && challenge.Realm == oldCrown,
            "Challenge follows the current Crown opponent without rewriting its origin or estate history");
        challenge.WarFaction = null;
        Check(warRealm.GetValue(challenge) == oldCrown, "Prewar challenge retains its original realm");

        var transfer = new CivilWarCrownTransferRecord { Previous = oldCrown, Successor = successor };
        var step = new CivilWarPairTransferRecord { Side = new CivilWarSideRecord { Realm = rebel, Faction = faction } };
        transfer.Pairs.Add(step);
        var protects = AccessTools.Method(typeof(CivilWarCrownTransferRecord), "Protects");
        var protectsFaction = AccessTools.Method(typeof(CivilWarCrownTransferRecord), "ProtectsFaction");
        for (int stage = 0; stage <= 4; stage++)
        {
            step.Stage = stage;
            foreach (var participant in new[] { oldCrown, successor, rebel })
                Check((bool)protects.Invoke(transfer, new object[] { participant }),
                    "Uncommitted transfer protects every participating realm at stage " + stage);
            Check((bool)protectsFaction.Invoke(transfer, new object[] { faction }),
                "Faction remains protected after parent retarget at stage " + stage);
        }
        Check(!(bool)protects.Invoke(transfer, new object[] { unrelated })
            && !(bool)protects.Invoke(transfer, new object[] { null }), "Transfer protection does not freeze unrelated realms");
        transfer.Completed = true;
        Check(new[] { oldCrown, successor, rebel }.All(k => !(bool)protects.Invoke(transfer, new object[] { k }))
            && !(bool)protectsFaction.Invoke(transfer, new object[] { faction }), "Only full transfer commit releases participant protection");
        var collapse = new CivilWarCollapseRecord
        {
            Parent = oldCrown, Successor = successor, WinnerRealm = unrelated,
            Conflict = new CivilWarConflictRecord(), SuccessorId = "saved-successor"
        };
        var collapseScore = new WarScoreRecord("old|rebel", "rebel", "old", 1, null, WarScoreConflictType.CivilWar, "rebel");
        collapse.Conflict.Sides.Add(step.Side);
        collapse.Conflict.Pairs.Add(new CivilWarPairRecord { Score = collapseScore });
        var collapseProtects = AccessTools.Method(typeof(CivilWarCollapseRecord), "Protects");
        var collapseProtectsScore = AccessTools.Method(typeof(CivilWarCollapseRecord), "ProtectsScore");
        for (int stage = 0; stage <= 4; stage++)
        {
            collapse.Stage = stage;
            foreach (var realm in new[] { oldCrown, successor, unrelated, rebel })
                Check((bool)collapseProtects.Invoke(collapse, new object[] { realm }) == (stage < 3),
                    "Collapse reserves old, winning and rival realms through transfer commit: " + stage);
            Check((bool)collapseProtectsScore.Invoke(collapse, new object[] { collapseScore }) == (stage < 3),
                "Collapse keeps war settlement deferred until all opponent transfers commit: " + stage);
            Check(collapse.SuccessorId == "saved-successor", "Collapse checkpoints retain the chosen successor identity");
        }
        collapse.Stage = 0;
        collapse.Completed = true;
        Check(!(bool)collapseProtects.Invoke(collapse, new object[] { oldCrown }), "Completed collapse cannot reserve realms again");
        Check(typeof(CivilWarCollapseRecord).GetFields(BindingFlags.Public | BindingFlags.Instance).All(f =>
            f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
            "Collapse persists all successor, phase and reward receipts");
        var ownsChallenge = AccessTools.Method(typeof(CivilWarCollapseRecord), "OwnsChallenge");
        collapse.Completed = false;
        collapse.Challenge = challenge;
        var otherChallenge = new SuccessionChallengeRecord();
        for (int stage = 0; stage <= 4; stage++)
        {
            collapse.Stage = stage;
            Check((bool)ownsChallenge.Invoke(collapse, new object[] { challenge }),
                "Saved collapse retains exclusive hereditary recovery ownership through phase " + stage);
            Check(!(bool)ownsChallenge.Invoke(collapse, new object[] { otherChallenge }),
                "A different hereditary challenge keeps its own recovery owner");
        }
        Check(!(bool)ownsChallenge.Invoke(collapse, new object[] { null }), "Unrelated generic outcomes cannot acquire challenge ownership");
        collapse.Completed = true;
        Check(!(bool)ownsChallenge.Invoke(collapse, new object[] { challenge }), "Completed collapse releases hereditary recovery ownership");
        var oldStance = (StanceLink)FormatterServices.GetUninitializedObject(typeof(StanceLink));
        var newStance = (StanceLink)FormatterServices.GetUninitializedObject(typeof(StanceLink));
        AccessTools.Property(typeof(StanceLink), "Faction1").SetValue(oldStance, rebel);
        AccessTools.Property(typeof(StanceLink), "Faction2").SetValue(oldStance, oldCrown);
        AccessTools.Property(typeof(StanceLink), "Faction1").SetValue(newStance, successor);
        AccessTools.Property(typeof(StanceLink), "Faction2").SetValue(newStance, rebel);
        oldStance.TroopCasualties1 = 111; oldStance.ShipCasualties1 = 2; oldStance.SuccessfulSieges1 = 3;
        oldStance.SuccessfulTownSieges1 = 4; oldStance.SuccessfulRaids1 = 5;
        oldStance.TroopCasualties2 = 222; oldStance.ShipCasualties2 = 6; oldStance.SuccessfulSieges2 = 7;
        oldStance.SuccessfulTownSieges2 = 8; oldStance.SuccessfulRaids2 = 9;
        var counts = new List<int>();
        var capture = AccessTools.Method(typeof(CivilWarConflictBehavior), "CaptureCounts");
        var restore = AccessTools.Method(typeof(CivilWarConflictBehavior), "RestoreCounts");
        capture.Invoke(null, new object[] { counts, oldStance, rebel });
        capture.Invoke(null, new object[] { counts, oldStance, oldCrown });
        oldStance.TroopCasualties1 = 0;
        restore.Invoke(null, new object[] { counts, 0, newStance, rebel });
        restore.Invoke(null, new object[] { counts, 5, newStance, successor });
        Check(newStance.TroopCasualties2 == 111 && newStance.TroopCasualties1 == 222
            && newStance.ShipCasualties2 == 2 && newStance.ShipCasualties1 == 6,
            "Saved native casualty counts survive reversed stance orientation and old counter reset");
        Check(newStance.SuccessfulSieges2 == 3 && newStance.SuccessfulTownSieges2 == 4 && newStance.SuccessfulRaids2 == 5
            && newStance.SuccessfulSieges1 == 7 && newStance.SuccessfulTownSieges1 == 8 && newStance.SuccessfulRaids1 == 9,
            "Native siege and raid history follows the appropriate successor side");
        var pressure = new WarWillPressureRecord("memory", "house", "old", "old|rebel", -15, "battle", 10, 100);
        var rekey = AccessTools.Method(typeof(WarWillPressureRecord), "RetargetConflict");
        rekey.Invoke(pressure, new object[] { "old|rebel", "new|rebel", "old", "new" });
        rekey.Invoke(pressure, new object[] { "old|rebel", "new|rebel", "old", "new" });
        Check(pressure.TargetKingdomId == "new" && pressure.ConflictKey == "new|rebel" && pressure.Amount == -15
            && pressure.CreatedDay == 10 && pressure.ExpiresDay == 100 && pressure.RecordId == "memory",
            "War-will pressure is reattached without replaying, stacking or extending it");
        foreach (var type in new[] { typeof(CivilWarCrownTransferRecord), typeof(CivilWarPairTransferRecord) })
            Check(type.GetFields(BindingFlags.Public | BindingFlags.Instance).All(f =>
                f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
                type.Name + " persists all transfer checkpoints");
    }

    private static void TestCivilWarConflictJournal()
    {
        var observe = AccessTools.Method(typeof(CivilWarConflictRecord), "Observe");
        var complete = AccessTools.Method(typeof(CivilWarConflictRecord), "Complete");
        var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var first = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
        var second = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
        var graph = new CivilWarConflictRecord { Id = "conflict", SovereignTitleId = "crown" };
        var side = (CivilWarSideRecord)observe.Invoke(graph, new object[] { first, realm, null });
        string id = side.Id;
        observe.Invoke(graph, new object[] { first, realm, null });
        Check(graph.Sides.Count == 1 && graph.Pairs.Count == 1 && graph.Sides[0].Id == id,
            "Repeated civil-war observation retains one stable side and pair identity");
        observe.Invoke(graph, new object[] { second, realm, null });
        var untouched = graph.Pairs[1];
        complete.Invoke(graph, new object[] { first });
        Check(graph.Sides[0].Closed && !graph.Sides[1].Closed && !untouched.Closed && !graph.Closed,
            "Completing one side leaves the independent rival conflict open");
        Check(observe.Invoke(graph, new object[] { first, realm, null }) == null,
            "Late observation cannot resurrect a completed side");
        complete.Invoke(graph, new object[] { first });
        Check(!untouched.Closed, "Repeated completion cannot drain rival journal entries");
        complete.Invoke(graph, new object[] { second });
        Check(graph.Closed && graph.Pairs.All(p => p.Closed), "Conflict closes only after its final side completes");

        var retarget = AccessTools.Method(typeof(WarScoreRecord), "TryRetargetCivilWarDefender");
        var snapshots = new[] { new WarScoreFiefSnapshotRecord("castle", "old", "loyalist", false, true),
            new WarScoreFiefSnapshotRecord("town", "rebel", "claimant", true, false) };
        var war = new WarScoreRecord("old|rebel", "rebel", "old", 42, snapshots, WarScoreConflictType.CivilWar, "rebel");
        war.AddBattleScore(12, 50); war.AddRaidScore(3, 10); war.AddPrisonerScore(5, 20);
        war.SetOccupationScore(20); war.AddTickingScore(2, 10, 45); war.AddObjectiveScore(1, 10);
        var evt = new WarScoreEventRecord(default(WarScoreEventType), 44, 12, 12, "rebel", "old");
        war.AddEvent(evt);
        float score = war.Score;
        war.QueueTerminalResolution("already queued");
        Check((bool)retarget.Invoke(war, new object[] { "old", "new" }), "Civil war accepts a replacement Crown realm");
        Check(war.DefenderKingdomId == "new" && war.WarKey == "new|rebel" && war.OriginalWarKey == "old|rebel",
            "Current pair identity changes while the original identity survives");
        Check(war.Score == score && war.StartedDay == 42 && war.LastTickDay == 45 && war.BattleScore == 12
            && war.RaidScore == 3 && war.PrisonerScore == 5 && war.OccupationScore == 20
            && war.TickingScore == 2 && war.ObjectiveScore == 1,
            "Crown retargeting preserves every accumulated score component and time baseline");
        Check(war.TerminalResolutionQueued && war.TerminalResolutionReason == "already queued"
            && ReferenceEquals(war.Events[0], evt) && evt.TargetKingdomId == "old",
            "Retargeting preserves pending terminal outcome and historical battle identities");
        Check(snapshots[0].OwnerKingdomId == "new" && snapshots[0].OriginalOwnerKingdomId == "old"
            && snapshots[0].OwnerClanId == "loyalist" && snapshots[1].OwnerKingdomId == "rebel",
            "Occupation references follow the successor without changing historical clan ownership");
        Check((bool)retarget.Invoke(war, new object[] { "old", "new" }) && war.Score == score,
            "Retarget retry is idempotent");
        Check((bool)retarget.Invoke(war, new object[] { "new", "third" }) && snapshots[0].OriginalOwnerKingdomId == "old"
            && war.OriginalWarKey == "old|rebel", "Repeated succession preserves the first historical identities");
        Check(!(bool)retarget.Invoke(war, new object[] { "third", "rebel" }), "Cannot retarget a war onto its own attacker");
        Check(!(bool)retarget.Invoke(war, new object[] { "unrelated", "other" }), "Wrong predecessor cannot retarget a war");
        war.SetWhitePeaceOfferPending(true);
        Check(!(bool)retarget.Invoke(war, new object[] { "third", "fourth" }), "Outstanding player peace choice blocks retargeting");
        war.SetWhitePeaceOfferPending(false);
        war.MarkEnded(50);
        Check(!(bool)retarget.Invoke(war, new object[] { "third", "fourth" }), "Completed wars cannot be resurrected by retargeting");
        var foreign = new WarScoreRecord("a|b", "a", "b", 1, null);
        Check(!(bool)retarget.Invoke(foreign, new object[] { "b", "c" }), "Civil-war retargeting cannot change a foreign war");
        foreach (var type in new[] { typeof(CivilWarConflictRecord), typeof(CivilWarSideRecord), typeof(CivilWarPairRecord) })
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Check(fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
                type.Name + " persists all lifecycle fields");
        }
    }

    private static void TestCivilWarContinuation()
    {
        var defeated = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var loyal = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var history = new CivilWarConflictRecord();
        var recordDefeat = AccessTools.Method(typeof(CivilWarConflictRecord), "RecordCrownDefeat");
        var eligibleReward = AccessTools.Method(typeof(CivilWarConflictRecord), "CanReceiveLoyalistReward");
        bool Eligible(CivilWarConflictRecord conflict, Clan clan) => (bool)eligibleReward.Invoke(conflict, new object[] { clan });
        recordDefeat.Invoke(history, new object[] { new[] { defeated, defeated, null } });
        Check(history.CrownDefeatedHouses.Count == 1, "Defeat history is null-safe and deduplicated");
        Check(!Eligible(history, defeated) && Eligible(history, loyal), "Returned defeated rebels cannot receive the later loyalist reward");
        history.Closed = true;
        Check(!Eligible(history, defeated), "Closing the last civil-war side does not erase reward exclusions");
        var restored = new CivilWarConflictRecord { CrownDefeatedHouses = history.CrownDefeatedHouses.ToList() };
        Check(!Eligible(restored, defeated), "Restored saved defeat history retains the exclusion");
        Check(Eligible(new CivilWarConflictRecord(), defeated), "A separate civil war does not inherit the exclusion");
        var legacy = new CivilWarConflictRecord { CrownDefeatedHouses = null };
        Check(Eligible(legacy, loyal) && !Eligible(legacy, null), "Legacy saves tolerate absent history without inventing defeats");
        recordDefeat.Invoke(legacy, new object[] { new[] { defeated } });
        Check(!Eligible(legacy, defeated), "First post-update defeat initializes legacy history");
        var rules = typeof(FactionObject).Assembly.GetType("BellumCivile.CivilWarContinuationRules");
        var satisfied = AccessTools.Method(rules, "DemandSatisfied");
        var hostile = AccessTools.Method(rules, "MutuallyExclusive");
        var destination = AccessTools.Method(rules, "DefeatedClanDestination");
        foreach (FactionType type in Enum.GetValues(typeof(FactionType)))
            foreach (bool replaced in new[] { false, true })
                foreach (bool installed in new[] { false, true })
                {
                    bool expected = type == FactionType.Abdication ? replaced : type == FactionType.InstallRuler && installed;
                    Check((bool)satisfied.Invoke(null, new object[] { type, replaced, installed }) == expected,
                        "Demand-specific continuation: " + type + ", replaced=" + replaced + ", installed=" + installed);
                }
        foreach (FactionType first in new[] { FactionType.Abdication, FactionType.InstallRuler, FactionType.Independence })
            foreach (FactionType second in new[] { FactionType.Abdication, FactionType.InstallRuler, FactionType.Independence })
                foreach (bool sameCrown in new[] { false, true })
                    foreach (bool sameClaimant in new[] { false, true })
                        Check((bool)hostile.Invoke(null, new object[] { first, second, sameCrown, sameClaimant })
                            == (first == FactionType.InstallRuler && second == FactionType.InstallRuler && sameCrown && !sameClaimant),
                            "Only incompatible claims to the same Crown imply rebel rivalry");
        Check(destination.Invoke(null, new object[] { true }).ToString() == "VictoriousClaimant",
            "Defeated Crown loyalists follow the claimant who becomes sovereign");
        Check(destination.Invoke(null, new object[] { false }).ToString() == "CurrentCrown",
            "A claimant defeated by the Crown returns surviving clans to the Crown");
        Check(destination.Invoke(null, new object[] { true }).ToString() == "VictoriousClaimant",
            "A claimant defeating another claimant absorbs the surviving coalition");
    }

    private static void TestCivilWarRivalry()
    {
        Func<string, Kingdom> realm = id =>
        {
            var k = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
            k.StringId = id;
            return k;
        };
        var crown = realm("crown");
        Func<FactionObject> claimant = () =>
        {
            var f = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
            f.Leader = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
            AccessTools.Property(typeof(FactionObject), "Type").SetValue(f, FactionType.InstallRuler);
            AccessTools.Property(typeof(FactionObject), "ParentKingdom").SetValue(f, crown);
            return f;
        };
        var first = claimant(); var second = claimant();
        var a = new CivilWarSideRecord { Id = "a", Faction = first, Realm = realm("a") };
        var b = new CivilWarSideRecord { Id = "b", Faction = second, Realm = realm("b") };
        var conflict = new CivilWarConflictRecord { CrownRealm = crown };
        conflict.Sides.Add(a); conflict.Sides.Add(b);
        var crownA = new CivilWarPairRecord { Id = "a-crown", AttackerSideId = "a", DefenderSideId = "crown" };
        crownA.Score = new WarScoreRecord("a|crown", "a", "crown", 7, null, WarScoreConflictType.CivilWar, "a");
        crownA.Score.AddBattleScore(12, 50);
        var crownB = new CivilWarPairRecord { Id = "b-crown", AttackerSideId = "b", DefenderSideId = "crown" };
        conflict.Pairs.Add(crownA); conflict.Pairs.Add(crownB);
        var observe = AccessTools.Method(typeof(CivilWarConflictRecord), "ObserveRivalry");
        var pair = (CivilWarPairRecord)observe.Invoke(conflict, new object[] { second, first });
        Check(pair != null && pair.AttackerSideId == "a" && pair.DefenderSideId == "b", "Rivalry ordering is stable regardless of registration order");
        Check(ReferenceEquals(pair, observe.Invoke(conflict, new object[] { first, second })) && conflict.Pairs.Count == 3,
            "Repeated rivalry registration does not duplicate a third war or replace Crown pair records");
        var war = new WarScoreRecord("a|b", "a", "b", 1, null, WarScoreConflictType.CivilWar, pair.Id);
        pair.Score = war;
        var resolve = AccessTools.Method(typeof(CivilWarConflictRecord), "TryGetRivalSides");
        var args = new object[] { war, null, null };
        Check((bool)resolve.Invoke(conflict, args) && args[1] == a && args[2] == b, "Rival score resolves its exact saved attacker and defender");
        var other = new WarScoreRecord("a|b", "a", "b", 1, null, WarScoreConflictType.CivilWar, pair.Id);
        Check(!(bool)resolve.Invoke(conflict, new object[] { other, null, null }), "A different score object cannot hijack a rivalry using the same text ID");
        pair.Score = new WarScoreRecord("a|b", "a", "b", 1, null, WarScoreConflictType.CivilWar, "wrong-context");
        Check(!(bool)resolve.Invoke(conflict, new object[] { pair.Score, null, null }), "Mismatched rivalry context cannot select claimant sides");
        pair.Score = war;
        second.Leader = first.Leader;
        Check(observe.Invoke(conflict, new object[] { first, second }) == null, "Same-house claimants cannot create a rivalry");
        second.Leader = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        AccessTools.Property(typeof(FactionObject), "Type").SetValue(second, FactionType.Independence);
        Check(observe.Invoke(conflict, new object[] { first, second }) == null, "Independence does not imply claimant rivalry");
        AccessTools.Property(typeof(FactionObject), "Type").SetValue(second, FactionType.InstallRuler);
        AccessTools.Method(typeof(CivilWarConflictRecord), "Complete").Invoke(conflict, new object[] { second });
        Check(pair.Closed && crownB.Closed && !crownA.Closed && !conflict.Closed,
            "Settling the defeated claimant closes only its incident wars; victorious claimant still contests the Crown");
        Check(crownA.Score.IsActive && crownA.Score.BattleScore == 12 && crownA.Score.StartedDay == 7,
            "Rival defeat neither completes nor merges scores into the survivor Crown war");
        Check(observe.Invoke(conflict, new object[] { first, second }) == null, "Completed claimant rivalry is never resurrected");
        Check(!(bool)resolve.Invoke(conflict, new object[] { war, null, null }), "Closed rivalry cannot be dispatched again");
        var defeat = new CivilWarRivalDefeatRecord { Crown = crown, Winner = a, Loser = b, Challenge = new SuccessionChallengeRecord() };
        var protect = AccessTools.Method(typeof(CivilWarRivalDefeatRecord), "Protects");
        var owns = AccessTools.Method(typeof(CivilWarRivalDefeatRecord), "OwnsChallenge");
        for (int stage = 0; stage < 4; stage++)
        {
            defeat.Stage = stage;
            Check(new[] { crown, a.Realm, b.Realm }.All(k => (bool)protect.Invoke(defeat, new object[] { k }) == (stage < 2)),
                "Rival absorption protects affected realms until household movement completes: " + stage);
            Check((bool)owns.Invoke(defeat, new object[] { defeat.Challenge }), "Losing hereditary outcome has one recovery owner through settlement");
        }
        defeat.Completed = true;
        Check(!(bool)owns.Invoke(defeat, new object[] { defeat.Challenge }), "Completed rival settlement releases challenge recovery ownership");
        Check(typeof(CivilWarRivalDefeatRecord).GetFields(BindingFlags.Public | BindingFlags.Instance).All(f =>
            f.GetCustomAttributesData().Any(x => x.AttributeType.Name == "SaveableFieldAttribute")), "Rival settlement persists every recovery receipt");
    }

    private static void TestCoordinatedElectionStartup()
    {
        var pending = AccessTools.Method(typeof(ElectiveContestBehavior), "PendingStartup");
        foreach (bool started in new[] { false, true })
            foreach (bool complete in new[] { false, true })
                foreach (bool closed in new[] { false, true })
                    foreach (bool aborted in new[] { false, true })
                    {
                        var r = new ElectiveContestRecord { WarDispatchStarted = started, WarDispatchCompleted = complete,
                            Closed = closed, StartupAborted = aborted };
                        Check((bool)pending.Invoke(null, new object[] { r }) == (started && !complete && !closed && !aborted),
                            "Startup protects only the incomplete, non-aborted transaction");
                    }
        var behavior = (ElectiveContestBehavior)FormatterServices.GetUninitializedObject(typeof(ElectiveContestBehavior));
        var first = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
        var second = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
        var stranger = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
        var record = new ElectiveContestRecord { WarDispatchStarted = true,
            Candidates = new List<ElectiveContestCandidate> { new ElectiveContestCandidate { WarFaction = first, WarStarted = true },
                new ElectiveContestCandidate { WarFaction = second, WarStarted = true } } };
        AccessTools.Field(typeof(ElectiveContestBehavior), "_contests").SetValue(behavior, new List<ElectiveContestRecord> { record });
        var owns = AccessTools.Method(typeof(ElectiveContestBehavior), "OwnsRivalStartup");
        Func<ElectiveContestRecord, FactionObject, FactionObject, bool> owned = (r, a, b) =>
            (bool)owns.Invoke(behavior, new object[] { r, a, b });
        Check(owned(record, first, second) && owned(record, second, first), "Either pair order has the same registered startup owner");
        Check(!owned(record, first, first) && !owned(record, first, stranger) && !owned(new ElectiveContestRecord(), first, second),
            "Unrelated or duplicate factions cannot bypass transition protection");
        record.Candidates[1].WarStarted = false;
        Check(!owned(record, first, second), "Rivalry cannot start before both Crown wars complete startup");
        record.Candidates[1].WarStarted = true;
        record.StartupAborted = true;
        Check(!owned(record, first, second), "Aborted startup loses its transition authorization");
        record.StartupAborted = false;
        record.WarDispatchCompleted = true;
        Check(!owned(record, first, second), "Completed transaction cannot authorize a fresh rivalry");
        foreach (var property in new[] { "Rivalry", "StartupAborted" })
            Check(typeof(ElectiveContestRecord).GetField(property).GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute"),
                "Election startup receipt persists: " + property);
        Check(typeof(ElectiveContestCandidate).GetField("WarStarted").GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute"),
            "Per-shell completion receipt persists");
    }

    private static void TestControlledElectionGate()
    {
        var gate = AccessTools.Method(typeof(ElectiveContestRecord), "ShouldAdvance");
        foreach (bool manual in new[] { false, true })
          foreach (bool automatic in new[] { false, true })
            foreach (bool armed in new[] { false, true })
                foreach (bool closed in new[] { false, true })
                    foreach (bool dispatched in new[] { false, true })
                        foreach (double day in new[] { 9.0, 10.0, 11.0 })
                        {
                            var record = new ElectiveContestRecord { ManualTest = manual, AutomaticChallengeEnabled = automatic, AccessionCompleted = armed,
                                Closed = closed, WarDispatchCompleted = dispatched, EligibleDay = 10 };
                            Check((bool)gate.Invoke(record, new object[] { day }) == ((manual || automatic) && armed && !closed && !dispatched && day >= 10),
                                "Election scheduler respects activation, accession, day and completion gates");
                        }
        var method = AccessTools.Method(typeof(CrownAccessionBehavior), "BeginMandateElection");
        Check(method.GetParameters()[1].IsOptional && Equals(method.GetParameters()[1].DefaultValue, false),
            "Normal mandate elections do not opt into test contests by default");
        foreach (var field in new[] { typeof(CrownAccessionRecord).GetField("ElectiveContestTest"),
            typeof(ElectiveContestRecord).GetField("ManualTest"), typeof(ElectiveContestRecord).GetField("TestStatus"),
            typeof(ElectiveContestRecord).GetField("AutomaticChallengeEnabled") })
            Check(field.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute"),
                "Controlled election state survives save/load: " + field.Name);
    }

    private static void TestElectiveContestPledges()
    {
        var comparison = AccessTools.Method(typeof(ElectiveContestBehavior), "ComparisonText");
        Func<double, double, string> describe = (own, other) => (string)comparison.Invoke(null, new object[] { own, other });
        Check(describe(200, 100).Contains("PowerDominant"), "Player report identifies overwhelming advantage");
        Check(describe(120, 100).Contains("PowerStronger"), "Player report identifies advantage");
        Check(describe(100, 100).Contains("PowerEven"), "Player report identifies evenly matched forces");
        Check(describe(60, 100).Contains("PowerWeaker"), "Player report identifies disadvantage");
        Check(describe(10, 100).Contains("PowerOutmatched"), "Player report identifies overwhelming opposition");
        Check(describe(double.NaN, 100).Contains("PowerUnknown"), "Player report does not invent invalid force comparisons");
        Check(describe(10, 0).Contains("PowerDominant") && describe(0, 0).Contains("PowerEven"), "Empty opponent comparisons do not divide by zero");
        var resolver = AccessTools.Method(typeof(ElectiveContestRecord).Assembly.GetType("BellumCivile.ElectiveContestPledgeRules"), "Resolve");
        var heroes = Enumerable.Range(0, 3).Select(_ => (Hero)FormatterServices.GetUninitializedObject(typeof(Hero))).ToArray();
        var houses = Enumerable.Range(0, 6).Select(_ => (Clan)FormatterServices.GetUninitializedObject(typeof(Clan))).ToArray();
        Func<double, double, double, ElectiveContestRecord> make = (crown, a, b) =>
        {
            var r = new ElectiveContestRecord { ElectedWinner = heroes[0], ElectedHouse = houses[0], PledgesCaptured = true };
            for (int i = 0; i < 3; i++)
            {
                r.Candidates.Add(new ElectiveContestCandidate { Candidate = heroes[i], House = houses[i], Threshold = .8,
                    Decision = i == 0 ? ElectiveContestDecision.Accept : ElectiveContestDecision.Contest });
                r.Pledges.Add(new ElectiveContestPledge { House = houses[i], Power = new[] { crown, a, b }[i], HasStronghold = true });
            }
            return r;
        };
        Func<object, string, object> field = (r, name) => AccessTools.Field(r.GetType(), name).GetValue(r);
        Func<object, HashSet<Hero>> active = r => (HashSet<Hero>)field(r, "Active");
        Func<object, Dictionary<Clan, Hero>> sides = r => (Dictionary<Clan, Hero>)field(r, "Sides");
        var three = make(200, 420, 380);
        var revalidate = AccessTools.Method(typeof(ElectiveContestRecord), "RevalidateParticipants");
        var interrupted = make(200, 420, 380);
        for (int i = 0; i < 3; i++) interrupted.Pledges[i].Speaker = heroes[i];
        interrupted.RulerAnswered = interrupted.UltimataPrepared = interrupted.PledgesResolved = true;
        interrupted.SurrenderTo = heroes[1];
        interrupted.RulerRoll = .37;
        interrupted.Candidates[2].Roll = .18;
        Func<ElectiveContestCandidate, bool> available = c => c.Candidate != heroes[1];
        Func<Clan, Hero> speaker = h => h == houses[1] ? null : heroes[Array.IndexOf(houses, h)];
        Check((bool)revalidate.Invoke(interrupted, new object[] { available, speaker, null }),
            "Pre-dispatch loss of a challenger revalidates the saved contest");
        Check(interrupted.Candidates[1].Decision == ElectiveContestDecision.Unavailable
            && interrupted.Candidates[2].Decision == ElectiveContestDecision.Contest
            && !interrupted.RulerAnswered && interrupted.SurrenderTo == null,
            "Unavailable challenger is removed without cancelling the surviving contender or retaining stale surrender");
        Check(interrupted.RulerRoll == .37 && interrupted.Candidates[2].Roll == .18,
            "Participant revalidation retains saved challenge and surrender rolls");
        var resolvedInterruption = resolver.Invoke(null, new object[] { interrupted, null });
        Check(resolvedInterruption != null && active(resolvedInterruption).SetEquals(new[] { heroes[2] }),
            "Absent unavailable candidate house does not block remaining coalition allocation");
        Check(!(bool)revalidate.Invoke(interrupted, new object[] { available, speaker, null }),
            "Repeated revalidation is idempotent");
        interrupted.WarDispatchStarted = true;
        Func<ElectiveContestCandidate, bool> nobody = c => false;
        Check(!(bool)revalidate.Invoke(interrupted, new object[] { nobody, speaker, null })
            && interrupted.Candidates[2].Decision == ElectiveContestDecision.Contest,
            "Started wars remain owned by the saved dispatch recovery path");
        var changedHead = make(200, 420, 380);
        for (int i = 0; i < 3; i++) changedHead.Pledges[i].Speaker = heroes[i];
        changedHead.Pledges[1].Preferences.Add(new ElectiveContestPreference { Candidate = heroes[2], Roll = .12f });
        Func<Clan, Hero> replacement = h => h == houses[1] ? heroes[0] : heroes[Array.IndexOf(houses, h)];
        revalidate.Invoke(changedHead, new object[] { available, replacement, houses[1] });
        Check(changedHead.Pledges[1].Preferences.Count == 0 && changedHead.Pledges[1].AwaitingPlayer
            && changedHead.Pledges[1].Power == 420,
            "New player clan head chooses afresh; old personal pledge is cleared without losing clan power");
        var ultimatumRules = typeof(ElectiveContestRecord).Assembly.GetType("BellumCivile.ElectiveContestUltimatumRules");
        var warChallengers = AccessTools.Method(ultimatumRules, "WarChallengers");
        var dispatchOpposition = AccessTools.Method(ultimatumRules, "DispatchOpposition");
        Func<ElectiveContestRecord, List<ElectiveContestCandidate>> wars = r =>
            (List<ElectiveContestCandidate>)warChallengers.Invoke(null, new object[] { r });
        for (int recipient = 0; recipient < 3; recipient++)
            foreach (bool withdrawA in new[] { false, true })
                foreach (bool withdrawB in new[] { false, true })
                {
                    var plan = make(200, 420, 380);
                    plan.SurrenderTo = recipient == 0 ? null : heroes[recipient];
                    plan.Candidates[1].Withdrawn = withdrawA;
                    plan.Candidates[2].Withdrawn = withdrawB;
                    var expected = plan.Candidates.Where(c => c.Candidate != heroes[0]
                        && c.Candidate != plan.SurrenderTo && !c.Withdrawn).ToList();
                    Check(wars(plan).SequenceEqual(expected), "Only active unsatisfied claimants receive war shells");
                    plan.Candidates.Reverse();
                    Check(new HashSet<Hero>(wars(plan).Select(c => c.Candidate)).SetEquals(expected.Select(c => c.Candidate)),
                        "Surrender continuation is independent of ballot order");
                    Check(plan.Candidates.Where(c => c.Candidate != heroes[0]).All(c => c.Decision == ElectiveContestDecision.Contest),
                        "Selecting war sides does not rewrite willingness or reroll support");
                }
        var forces = make(200, 420, 380);
        for (int i = 0; i < 3; i++) forces.Pledges[i].AssignedSide = heroes[i];
        Func<Clan, double> power = house => forces.Pledges.First(p => p.House == house).Power;
        Check((double)dispatchOpposition.Invoke(null, new object[] { forces, heroes[1], power }) == 390,
            "First challenger dispatch uses Crown power plus half rival power");
        Check((double)dispatchOpposition.Invoke(null, new object[] { forces, heroes[2], power }) == 410,
            "Second challenger dispatch uses the same discounted opposition rule");
        forces.SurrenderTo = heroes[1];
        Check(wars(forces).Single().Candidate == heroes[2] && !forces.Closed,
            "Yielding to one claimant does not close the remaining claimant's dispute");
        Check((double)dispatchOpposition.Invoke(null, new object[] { forces, heroes[2], power }) == 410,
            "Surrender does not retroactively change the validation of issued demands");
        Check(forces.Pledges.Select(p => p.AssignedSide).SequenceEqual(heroes),
            "A Crown surrender preserves each house's previously assigned allegiance");
        var chooseSurrender = AccessTools.Method(ultimatumRules, "ChooseSurrender");
        var canAnswerUltimata = AccessTools.Method(ultimatumRules, "CanAnswer");
        var demands = make(100, 500, 300);
        for (int i = 0; i < 3; i++) demands.Candidates[i].PledgedPower = demands.Pledges[i].Power;
        Func<double, ElectiveContestCandidate> surrender = roll => (ElectiveContestCandidate)chooseSurrender.Invoke(null,
            new object[] { demands, roll, false, 0, 0, 0 });
        Check(surrender(.1)?.Candidate == heroes[1], "One Crown is surrendered to the strongest acceptable demand");
        Check(surrender(.9) == null, "A failed surrender roll refuses all challengers");
        demands.Candidates.Reverse();
        Check(surrender(.1)?.Candidate == heroes[1], "Surrender does not depend on candidate enumeration order");
        demands.Candidates.First(c => c.Candidate == heroes[1]).Withdrawn = true;
        Check(surrender(.1)?.Candidate == heroes[2], "Withdrawn challengers cannot receive the Crown");
        Check(!(bool)canAnswerUltimata.Invoke(null, new object[] { demands, heroes[2] }), "Unprepared ultimata cannot receive a ruler response");
        demands.UltimataPrepared = true;
        Check((bool)canAnswerUltimata.Invoke(null, new object[] { demands, heroes[2] }), "Prepared active claimant is a valid surrender choice");
        Check(!(bool)canAnswerUltimata.Invoke(null, new object[] { demands, heroes[1] }), "Ruler cannot yield to a withdrawn challenger");
        Check((bool)canAnswerUltimata.Invoke(null, new object[] { demands, null }), "Ruler can refuse all demands");
        demands.RulerAnswered = true;
        Check(!(bool)canAnswerUltimata.Invoke(null, new object[] { demands, null }), "A saved ruler response cannot be overwritten");
        var resolved = resolver.Invoke(null, new object[] { three, null });
        Check(active(resolved).Count == 2, "Both challengers survive the discounted rival-power gate");
        Check(((Dictionary<Hero, double>)field(resolved, "Power")).Values.Sum() == 1000,
            "Three-way allocation conserves total strength plus influence");
        Check(three.Pledges.All(p => p.AssignedSide == null) && three.Candidates.All(c => !c.Withdrawn),
            "Pure evaluation does not mutate saved inputs before commit");
        var cascade = make(300, 420, 280);
        var playerCascade = resolver.Invoke(null, new object[] { cascade, heroes[1] });
        Check(active(playerCascade).SetEquals(new[] { heroes[1] }) && sides(playerCascade)[houses[2]] == heroes[0],
            "Player survives NPC withdrawal even when increased Crown backing exceeds the power gate");
        var weakPlayer = make(900, 10, 90);
        Check(active(resolver.Invoke(null, new object[] { weakPlayer, heroes[1] })).Contains(heroes[1]),
            "Outmatched player chooses military risk");
        weakPlayer.Pledges[1].HasStronghold = false;
        Check(!active(resolver.Invoke(null, new object[] { weakPlayer, heroes[1] })).Contains(heroes[1]),
            "Player still needs a coalition stronghold");
        var proceed = AccessTools.Method(typeof(SuccessionChallengeRecord).Assembly.GetType("BellumCivile.SuccessionChallengeRules"), "CanProceed");
        Func<double, double, bool, bool, bool> canProceed = (backing, opposition, isPlayer, stronghold) =>
            (bool)proceed.Invoke(null, new object[] { backing, opposition, .8, isPlayer, stronghold });
        Check(canProceed(10, 1000, true, true) && !canProceed(10, 1000, false, true), "Only players bypass military thresholds");
        foreach (bool isPlayer in new[] { false, true })
        {
            Check(!canProceed(1000, 10, isPlayer, false), "All challengers require a stronghold");
            foreach (double invalid in new[] { 0.0, -1, double.NaN, double.PositiveInfinity })
                Check(!canProceed(invalid, 10, isPlayer, true), "All challengers need positive finite forces");
            Check(!canProceed(100, double.NaN, isPlayer, true), "Invalid opposition never permits dispatch");
        }
        var cascadeResult = resolver.Invoke(null, new object[] { cascade, null });
        Check(active(cascadeResult).Count == 0 && ((HashSet<Hero>)field(cascadeResult, "Withdrawn")).Count == 2,
            "Weaker candidate withdrawal can remove the remaining candidate's power advantage");
        cascade.Pledges[2].Preferences.Add(new ElectiveContestPreference { Candidate = heroes[1], PersonalChance = 1, Roll = 0 });
        var fallback = resolver.Invoke(null, new object[] { cascade, null });
        Check(active(fallback).SetEquals(new[] { heroes[1] }) && sides(fallback)[houses[2]] == heroes[1],
            "Withdrawn candidate may support the remaining rival through saved fallback");
        cascade.Pledges.Reverse(); cascade.Candidates.Reverse();
        var reversed = resolver.Invoke(null, new object[] { cascade, null });
        Check(sides(reversed).All(p => sides(fallback)[p.Key] == p.Value), "Candidate and clan iteration order cannot change allocation");
        var player = make(100, 500, 500);
        var playerPledge = new ElectiveContestPledge { House = houses[3], Power = 100, PlayerChoice = true, AwaitingPlayer = true };
        player.Pledges.Add(playerPledge);
        Check(resolver.Invoke(null, new object[] { player, null }) == null, "Unanswered player choice blocks the whole simultaneous allocation");
        playerPledge.AwaitingPlayer = false;
        playerPledge.Preferences.Add(new ElectiveContestPreference { Candidate = heroes[0],
            LiegeChances = new Dictionary<Clan, float> { [houses[1]] = 1 } });
        Check(sides(resolver.Invoke(null, new object[] { player, null }))[houses[3]] == heroes[0],
            "Explicit Crown support cannot be overridden by feudal calls");
        var chain = make(100, 500, 500);
        chain.Pledges.Add(new ElectiveContestPledge { House = houses[3], Power = 10,
            Preferences = new List<ElectiveContestPreference> { new ElectiveContestPreference { Candidate = heroes[1],
                Roll = .1f, LiegeChances = new Dictionary<Clan, float> { [houses[1]] = 1 } } } });
        chain.Pledges.Add(new ElectiveContestPledge { House = houses[4], Power = 10,
            Preferences = new List<ElectiveContestPreference> { new ElectiveContestPreference { Candidate = heroes[1],
                Roll = .1f, LiegeChances = new Dictionary<Clan, float> { [houses[3]] = 1 } } } });
        var chainResult = resolver.Invoke(null, new object[] { chain, null });
        Check(sides(chainResult)[houses[3]] == heroes[1] && sides(chainResult)[houses[4]] == heroes[1],
            "Feudal support propagates through actual assigned sponsors without double counting");
        chain.Pledges[3].Preferences[0].LiegeChances = new Dictionary<Clan, float> { [houses[4]] = 1 };
        var cycle = resolver.Invoke(null, new object[] { chain, null });
        Check(sides(cycle)[houses[3]] == heroes[0] && sides(cycle)[houses[4]] == heroes[0],
            "Unseeded feudal cycles cannot mobilize themselves");
        var bad = make(100, 500, 500);
        bad.Pledges.Add(bad.Pledges[1]);
        Check(resolver.Invoke(null, new object[] { bad, null }) == null, "Duplicate house records cannot duplicate armed power");
        bad = make(100, 500, 500); bad.Pledges[1].Power = double.NaN;
        Check(resolver.Invoke(null, new object[] { bad, null }) == null, "Invalid armed power fails validation");
        var noCastle = make(100, 500, 500); noCastle.Pledges[1].HasStronghold = false;
        Check(!active(resolver.Invoke(null, new object[] { noCastle, null })).Contains(heroes[1]),
            "A strong challenger still needs a coalition stronghold");
        var withdrawn = make(100, 500, 500); withdrawn.Candidates[1].Withdrawn = true;
        Check(!active(resolver.Invoke(null, new object[] { withdrawn, null })).Contains(heroes[1]), "Recorded withdrawal cannot resurrect a candidate");
        foreach (var type in new[] { typeof(ElectiveContestPledge), typeof(ElectiveContestPreference) })
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Check(fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
                type.Name + " persists all allegiance inputs");
        }
    }

    private static void TestElectiveContestFoundation()
    {
        foreach (string name in new[] { "AddDecision", "RemoveDecision" })
        {
            var methods = typeof(Kingdom).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.Name == name).ToList();
            Check(methods.Count == 1 && methods[0].GetParameters()[0].ParameterType == typeof(TaleWorlds.CampaignSystem.Election.KingdomDecision),
                "Rebel succession diagnostic matches native " + name + " first argument and unique overload");
        }
        Check(AccessTools.Method(typeof(TaleWorlds.CampaignSystem.Election.KingdomDecision), "ShouldBeCancelled")?.ReturnType == typeof(bool),
            "Rebel succession diagnostic observes native cancellation boolean");
        var decide = AccessTools.Method(typeof(ElectiveContestCandidate), "Decide");
        var answer = AccessTools.Method(typeof(ElectiveContestCandidate), "AnswerPlayer");
        foreach (double acceptance in new[] { 0.0, 25, 60, 100 })
            foreach (double roll in new[] { 0.0, .249, .399, .4, .75, .999 })
            {
                var candidate = new ElectiveContestCandidate { HasAssessment = true, Acceptance = acceptance };
                decide.Invoke(candidate, new object[] { true, false, roll });
                var expected = roll * 100 < 100 - acceptance ? ElectiveContestDecision.Contest : ElectiveContestDecision.Accept;
                Check(candidate.Decision == expected && candidate.Roll == roll, "Saved elective contest roll matches acceptance chance");
                decide.Invoke(candidate, new object[] { true, false, 1 - roll / 2 });
                Check(candidate.Decision == expected && candidate.Roll == roll, "Repeated evaluation cannot reroll an election candidate");
            }
        var unavailable = new ElectiveContestCandidate { HasAssessment = true, Acceptance = 0 };
        decide.Invoke(unavailable, new object[] { false, false, 0.0 });
        decide.Invoke(unavailable, new object[] { true, false, 0.0 });
        Check(unavailable.Decision == ElectiveContestDecision.Unavailable && unavailable.Roll == -1,
            "Unavailable candidates miss this election permanently without consuming a roll");
        var player = new ElectiveContestCandidate();
        decide.Invoke(player, new object[] { true, true, double.NaN });
        Check(player.Decision == ElectiveContestDecision.AwaitingPlayer && player.Roll == -1,
            "Player chooses without an acceptance assessment or random roll");
        answer.Invoke(player, new object[] { true });
        answer.Invoke(player, new object[] { false });
        Check(player.Decision == ElectiveContestDecision.Contest, "Player election-contest answer is recorded once");

        var winner = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var loser = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var ballot = new ElectiveSuccessionRecord { Winner = winner };
        ballot.Finalists.Add(winner); ballot.Finalists.Add(loser);
        ballot.Votes.Add(new ElectiveCommitment { Supported = winner, Weight = 60 });
        ballot.Votes.Add(new ElectiveCommitment { Supported = loser, Weight = 40 });
        var snapshotMethod = AccessTools.Method(typeof(ElectiveContestBehavior), "Snapshot");
        var scoreType = typeof(ElectiveContestBehavior).Assembly.GetType("BellumCivile.ElectiveAcceptanceAssessment");
        var parameter = System.Linq.Expressions.Expression.Parameter(typeof(Hero));
        var noScore = System.Linq.Expressions.Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(Hero), scoreType),
            System.Linq.Expressions.Expression.Constant(null, scoreType), parameter).Compile();
        var snapshot = (ElectiveContestRecord)snapshotMethod.Invoke(null, new object[] { "election_test", ballot, winner, 20.5, noScore });
        ballot.Votes[0].Weight = 1; ballot.Votes[1].Supported = winner; ballot.Finalists.Clear(); ballot.Winner = loser;
        Check(snapshot.ElectedWinner == winner && snapshot.Votes[0].Weight == 60 && snapshot.Votes[1].Supported == loser
            && snapshot.Candidates.Count == 2 && snapshot.Candidates[1].Support == 40,
            "Next-mandate ballot changes cannot mutate the saved disputed election");
        Check(snapshot.Candidates[0].Decision == ElectiveContestDecision.Accept,
            "The elected winner cannot initiate against their own victory");
        var arm = AccessTools.Method(typeof(ElectiveContestRecord), "Arm");
        arm.Invoke(snapshot, new object[] { 20.5 }); arm.Invoke(snapshot, new object[] { 99.0 });
        Check(snapshot.AccessionCompleted && snapshot.EligibleDay == 21, "Accession arms next calendar day once; reload cannot postpone it");
        var closed = new ElectiveContestRecord { Closed = true };
        arm.Invoke(closed, new object[] { 20.0 });
        Check(!closed.AccessionCompleted, "A superseded election cannot be armed");
        foreach (var type in new[] { typeof(ElectiveContestRecord), typeof(ElectiveContestCandidate), typeof(ElectiveContestVote) })
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Check(fields.All(f => f.GetCustomAttributesData().Any(a => a.AttributeType.Name == "SaveableFieldAttribute")),
                type.Name + " persists every journal field");
            Check(fields.Select(f => f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute")
                .ConstructorArguments[0].Value).Distinct().Count() == fields.Length, type.Name + " has unique saved field IDs");
        }
    }

    private static void TestPlayerHeirPresentation()
    {
        var harmony = new Harmony("BellumCivile.Tests.PlayerHeirPresentation");
        var getter = AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.MainHero));
        var previous = VisitPlayer;
        try
        {
            VisitPlayer = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            harmony.Patch(getter, prefix: new HarmonyMethod(typeof(Program), nameof(VisitMainHero)));
            var type = typeof(SuccessionChallengeRecord).Assembly.GetTypes().Single(t => t.Name == "SuccessionHeirVM");
            var vm = FormatterServices.GetUninitializedObject(type);
            AccessTools.Field(type, "_hero").SetValue(vm, VisitPlayer);
            Check((string)AccessTools.Property(type, "LoyaltyText").GetValue(vm) == "--",
                "Player heir loyalty text bypasses campaign assessment");
            Check((bool)AccessTools.Property(type, "IsPlayerHeir").GetValue(vm), "Player heir presentation recognizes the active player");
            AccessTools.Field(type, "_hero").SetValue(vm, FormatterServices.GetUninitializedObject(typeof(Hero)));
            Check(!(bool)AccessTools.Property(type, "IsPlayerHeir").GetValue(vm), "NPC heir presentation remains distinct");
        }
        finally { harmony.Unpatch(getter, HarmonyPatchType.Prefix, harmony.Id); VisitPlayer = previous; }
    }
    private static bool VisitRandom(ref float __result) { __result = VisitRoll; return false; }
    private static bool VisitLocation(Hero hero, out Settlement heroSettlement,
        out TaleWorlds.CampaignSystem.Party.MobileParty heroParty)
    {
        VisitSettlements.TryGetValue(hero, out heroSettlement);
        VisitParties.TryGetValue(hero, out heroParty);
        return false;
    }

    private static void TestSeparateSpouseHouseholds()
    {
        var native = typeof(TaleWorlds.CampaignSystem.CampaignBehaviors.PregnancyCampaignBehavior);
        var behavior = Activator.CreateInstance(native);
        var nearby = AccessTools.Method(native, "CheckAreNearby");
        Hero mother = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        Hero father = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        Hero unrelatedPlayer = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        Clan first = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        Clan second = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        Clan third = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        AccessTools.Field(typeof(Hero), "_clan").SetValue(mother, first);
        AccessTools.Field(typeof(Hero), "_clan").SetValue(father, second);
        AccessTools.Field(typeof(Hero), "_clan").SetValue(unrelatedPlayer, third);
        mother.Spouse = father;
        Check(mother.Spouse == father && father.Spouse == mother && mother.Clan != father.Clan,
            "Native reciprocal spouse link accepts different clans");
        VisitPlayer = father;
        VisitRoll = 0.9f;
        VisitSettlements = new Dictionary<Hero, Settlement>();
        VisitParties = new Dictionary<Hero, TaleWorlds.CampaignSystem.Party.MobileParty>();
        var harmony = new Harmony("BellumCivile.Tests.SeparateSpouses");
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.MainHero)),
                prefix: new HarmonyMethod(typeof(Program), nameof(VisitMainHero)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(TaleWorlds.Core.MBRandom), "RandomFloat"),
                prefix: new HarmonyMethod(typeof(Program), nameof(VisitRandom)));
            // Only location lookup and randomness are supplied; the proximity decision is native.
            harmony.Patch(AccessTools.Method(native, "GetLocation"),
                prefix: new HarmonyMethod(typeof(Program), nameof(VisitLocation)));
            Func<bool> eligible = () => (bool)nearby.Invoke(behavior, new object[] { mother, father });
            Settlement home = (Settlement)FormatterServices.GetUninitializedObject(typeof(Settlement));
            VisitSettlements[mother] = VisitSettlements[father] = home;
            Check(eligible(), "Different-clan spouses together in a settlement pass native pregnancy proximity");
            VisitSettlements.Clear();
            var party = (TaleWorlds.CampaignSystem.Party.MobileParty)FormatterServices.GetUninitializedObject(
                typeof(TaleWorlds.CampaignSystem.Party.MobileParty));
            VisitParties[mother] = VisitParties[father] = party;
            Check(eligible(), "Different-clan spouses together in a party pass native pregnancy proximity");
            VisitParties.Clear();
            Check(!eligible(), "Separated spouses fail proximity when the abstract visit roll fails");
            VisitRoll = 0.1f;
            Check(eligible(), "A wife outside her player husband's clan receives the native abstract visit chance");
            VisitPlayer = mother;
            Check(!eligible(), "A player mother does not receive the same distant abstract visit chance");
            VisitSettlements[mother] = VisitSettlements[father] = home;
            Check(eligible(), "A player mother with an external-clan spouse can pass proximity when together");
            VisitSettlements.Clear();
            VisitPlayer = unrelatedPlayer;
            Check(eligible(), "Two NPC spouses in separate clans receive abstract visits");
            VisitRoll = 0.2f;
            Check(!eligible(), "Native abstract visit comparison is strictly below twenty percent");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            VisitPlayer = null;
            VisitSettlements = null;
            VisitParties = null;
        }
    }

    private static void TestHouseholdFoundation()
    {
        var harmony = new Harmony("BellumCivile.Tests.HouseholdFoundation");
        try
        {
            foreach (var patch in new[] { typeof(SeparateSpouseBirthClanPatch), typeof(ConsistentSpouseVisitPatch) })
                Check(harmony.CreateClassProcessor(patch).Patch().Count > 0, "Harmony compiles " + patch.Name);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
        var birth = AccessTools.Method(typeof(HeroCreator), nameof(HeroCreator.DeliverOffSpring));
        var original = PatchProcessor.GetOriginalInstructions(birth).ToList();
        var modified = SeparateSpouseBirthClanPatch.Transpiler(original).ToList();
        var getter = AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.Clan));
        var replacement = AccessTools.Method(typeof(SeparateSpouseBirthClanPatch), nameof(SeparateSpouseBirthClanPatch.ResolveBirthClan));
        Check(original.Count(c => c.Calls(getter)) == 2 && modified.Count(c => c.Calls(replacement)) == 2,
            "Both player-parent and NPC birth clan branches use the household resolver");
        Check(modified.Count(c => c.Calls(getter)) == 0, "No birth branch retains the native father/player clan override");
        Check(original.Sum(c => c.labels.Count) == modified.Sum(c => c.labels.Count)
            && original.Sum(c => c.blocks.Count) == modified.Sum(c => c.blocks.Count), "Birth patch retains native branch and exception metadata");
        bool rejected = false;
        try { SeparateSpouseBirthClanPatch.Transpiler(original.Where(c => !c.Calls(getter))).ToList(); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Changed native birth anchors fail explicitly");
        Hero mother = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        Hero father = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        Clan maternal = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        Clan paternal = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        Clan newMaternal = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        AccessTools.Field(typeof(Hero), "_clan").SetValue(mother, maternal);
        AccessTools.Field(typeof(Hero), "_clan").SetValue(father, paternal);
        Check(SeparateSpouseBirthClanPatch.ResolveBirthClan(father, mother, father) == maternal,
            "Player-father and NPC-father birth destinations yield to the separate mother's clan");
        Check(SeparateSpouseBirthClanPatch.ResolveBirthClan(mother, mother, father) == maternal,
            "Player-mother birth destination follows the same maternal rule");
        AccessTools.Field(typeof(Hero), "_clan").SetValue(mother, newMaternal);
        Check(SeparateSpouseBirthClanPatch.ResolveBirthClan(father, mother, father) == newMaternal,
            "Birth uses the mother's current household rather than her household at conception");
        AccessTools.Field(typeof(Hero), "_clan").SetValue(father, newMaternal);
        Check(SeparateSpouseBirthClanPatch.ResolveBirthClan(father, mother, father) == newMaternal,
            "Shared-household births retain their existing clan");
        Check(SeparateSpouseBirthClanPatch.ResolveBirthClan(father, null, father) == newMaternal,
            "Incomplete parental data retains the native destination instead of inventing a clan");
        var visit = AccessTools.Method(typeof(ConsistentSpouseVisitPatch), "IsVisitEligible");
        Func<bool, bool, bool, float, bool> eligible = (c, w, t, r) => (bool)visit.Invoke(null, new object[] { c, w, t, r });
        Check(eligible(false, false, true, 1f), "Peaceful spouses together need no abstract visit roll");
        Check(eligible(false, false, false, 0.1f) && !eligible(false, false, false, 0.2f),
            "All households share the same strict twenty-percent abstract visit threshold");
        foreach (bool together in new[] { false, true })
        {
            Check(!eligible(true, false, together, 0f), "Captivity blocks spouse visits; together=" + together);
            Check(!eligible(false, true, together, 0f), "Hostility blocks spouse visits; together=" + together);
        }
    }

    private static ChangeKingdomAction.ChangeKingdomActionDetail CapturedJoinDetail;
    private static bool CapturedJoinRebellion;
    private static bool CapturedJoinNotification;
    private static bool CaptureNativeJoin(ChangeKingdomAction.ChangeKingdomActionDetail detail, bool byRebellion, bool showNotification)
    {
        CapturedJoinDetail = detail;
        CapturedJoinRebellion = byRebellion;
        CapturedJoinNotification = showNotification;
        return false;
    }

    private static void TestForeignAccession()
    {
        Kingdom origin = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        Kingdom destination = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        Clan clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        AccessTools.Property(typeof(Kingdom), "StringId").SetValue(origin, "foreign_test");
        AccessTools.Property(typeof(Kingdom), "StringId").SetValue(destination, "destination_test");
        AccessTools.Field(typeof(Clan), "_kingdom").SetValue(clan, origin);
        var union = AccessTools.Method(typeof(CrownAccessionBehavior), "RequiresRealmUnion");
        Func<bool> needsUnion = () => (bool)union.Invoke(null, new object[] { clan, destination });
        Check(!needsUnion(), "A foreign vassal's own clan accession does not merge its liege's realm");
        origin.RulingClan = clan;
        Check(needsUnion(), "A foreign ruling clan requires the realm-union executor instead of ordinary house movement");
        Check(!(bool)union.Invoke(null, new object[] { clan, origin }), "Same-realm accession is not a union");
        var candidate = AccessTools.Method(typeof(CrownAccessionBehavior).Assembly.GetType("BellumCivile.HereditaryRealmSuccession"), "CanConsiderClan");
        Check((bool)candidate.Invoke(null, new object[] { clan, destination }), "Permanent foreign households can supply hereditary blood candidates");
        AccessTools.Property(typeof(Kingdom), "StringId").SetValue(origin, "bc_feud_test");
        Check(!(bool)candidate.Invoke(null, new object[] { clan, destination }), "Unattached temporary feud realms cannot supply foreign candidates");
        AccessTools.Property(typeof(Kingdom), "StringId").SetValue(origin, "foreign_test");
        var record = new CrownAccessionRecord { Realm = destination, IncomingSourceRealm = origin };
        Check(record.HouseholdRealm == origin && record.Realm == destination, "Foreign cadet endowment settles in its source realm before movement");
        record.IncomingSourceRealm = null;
        Check(record.HouseholdRealm == destination, "Existing local cadet records retain their original realm");
        var title = new FeudalTitleRecord("foreign_barony", "Barony", FeudalTitleType.Barony,
            "lawful_owner", "possessor", "original_crown", "", "", 0, 0);
        record.ForeignLegalHolders[title.TitleId] = "lawful_owner";
        record.ForeignLegalParents[title.TitleId] = "original_crown";
        var legal = AccessTools.Method(typeof(CrownAccessionBehavior), "ForeignLegalRightsUnchanged");
        Func<FeudalTitleRecord[], bool> preserved = current => (bool)legal.Invoke(null, new object[] { record, current });
        Check(preserved(new[] { title }), "Foreign estate snapshot preserves another house's lawful ownership");
        title.SetDeFactoParentTitle("destination_crown");
        Check(preserved(new[] { title }), "Political reparenting may change without rewriting de jure hierarchy");
        title.SetDeJureHolder("possessor");
        Check(!preserved(new[] { title }), "Foreign movement cannot legalize possession at a third party's expense");
        Check(!preserved(new FeudalTitleRecord[0]), "Missing legal title blocks foreign settlement completion");
        var harmony = new Harmony("BellumCivile.Tests.ForeignJoin");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(ChangeKingdomAction), "ApplyInternal"),
                prefix: new HarmonyMethod(typeof(Program), nameof(CaptureNativeJoin)));
            ChangeKingdomAction.ApplyByJoinToKingdom(clan, destination, showNotification: false);
            Check(CapturedJoinDetail == ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdom
                && !CapturedJoinRebellion && !CapturedJoinNotification,
                "Native accession transport selects ordinary JoinKingdom, not leave/rebellion/defection");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
        var leaveBody = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Clan), "ClanLeaveKingdom"));
        Check(!leaveBody.Any(c => c.operand is MethodInfo m && (m.DeclaringType == typeof(ChangeOwnerOfSettlementAction)
            || m.DeclaringType == typeof(DeclareWarAction))), "Installed native join's clan-leave helper does not confiscate fiefs or declare war");
    }

    private static void TestPrimaryInheritanceShare(Hero primary, Hero secondary)
    {
        var assembly = typeof(CrownAccessionRecord).Assembly;
        Type planType = assembly.GetType("BellumCivile.FeudalInheritancePlan");
        Type packageType = assembly.GetType("BellumCivile.FeudalInheritancePackage");
        object plan = Activator.CreateInstance(planType, true);
        Func<object, string, IList> list = (owner, name) => (IList)owner.GetType().GetProperty(name).GetValue(owner);
        var fiefs = Enumerable.Range(0, 3).Select(_ => (Town)FormatterServices.GetUninitializedObject(typeof(Town))).ToArray();
        for (int i = 0; i < fiefs.Length; i++)
        {
            var settlement = (Settlement)FormatterServices.GetUninitializedObject(typeof(Settlement));
            AccessTools.Property(typeof(Settlement), "StringId").SetValue(settlement, "estate_fief_" + i);
            var party = (TaleWorlds.CampaignSystem.Party.PartyBase)FormatterServices.GetUninitializedObject(typeof(TaleWorlds.CampaignSystem.Party.PartyBase));
            AccessTools.Property(party.GetType(), "Settlement").SetValue(party, settlement);
            AccessTools.Field(typeof(SettlementComponent), "_owner").SetValue(fiefs[i], party);
            AccessTools.Property(typeof(Town), "StringId").SetValue(fiefs[i], "town_component_" + i);
        }
        var snapshot = AccessTools.Method(typeof(PartitionSuccessionBehavior), "SnapshotEstateFiefs");
        var recorded = (List<string>)snapshot.Invoke(null, new object[] { fiefs });
        Check(recorded.SequenceEqual(new[] { "estate_fief_0", "estate_fief_1", "estate_fief_2" }),
            "Death estate saves settlement IDs rather than distinct Town component IDs");
        var canonical = AccessTools.Method(typeof(PartitionSuccessionBehavior), "CanonicalizeEstateFiefIds");
        var aliases = new Dictionary<string, string> { ["town_comp_ES4"] = "town_ES4" };
        Func<string[], List<string>> normalize = ids => (List<string>)canonical.Invoke(null, new object[] { ids, aliases });
        Check(normalize(new[] { "town_comp_ES4", "town_ES4" }).SequenceEqual(new[] { "town_ES4" }),
            "Known pending component IDs normalize to one settlement receipt");
        Check(normalize(new[] { "unknown_component" }).Single() == "unknown_component",
            "Recovery does not fabricate settlement IDs for unknown objects");
        Check(normalize(normalize(new[] { "town_comp_ES4" }).ToArray()).Single() == "town_ES4",
            "Repeated estate ID repair is idempotent");
        var titles = Enumerable.Range(0, 3).Select(i => new FeudalTitleRecord("title_" + i, "Title", FeudalTitleType.Barony,
            "parent", "parent", "", "estate_fief_" + i, "", 0, 0)).ToArray();
        foreach (var fief in fiefs) list(plan, "EstateFiefs").Add(fief);
        foreach (var title in titles) list(plan, "EstateTitles").Add(title);
        list(plan, "NaturalHeirs").Add(primary);
        list(plan, "NaturalHeirs").Add(secondary);
        for (int i = 1; i < 3; i++)
        {
            object package = Activator.CreateInstance(packageType, true);
            list(package, "Fiefs").Add(fiefs[i]);
            list(package, "Titles").Add(titles[i]);
            list(plan, "SecondaryPackages").Add(package);
        }
        var livingShare = AccessTools.Method(assembly.GetType("BellumCivile.FeudalInheritancePlanner"), "GetLivingAccessionShare");
        Func<Hero, bool, object> living = (heir, enabled) => livingShare.Invoke(null, new[] { plan, primary, heir, (object)enabled });
        Check(list(living(primary, true), "Fiefs").Count == 2, "Living primary heir gets the primary share, not all secondary packages");
        Check(list(living(secondary, true), "Fiefs").Cast<Town>().Single() == fiefs[1], "Living secondary heir receives only their assigned package");
        Check(living(secondary, false) == null, "Disabled partition does not invent a secondary living inheritance");
        Check(living(null, true) == null, "Missing incoming heir has no estate entitlement");
        Hero outsider = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        Check(living(outsider, true) == null, "Crown status alone does not entitle an unrelated household member to land");
        var method = AccessTools.Method(assembly.GetType("BellumCivile.FeudalInheritancePlanner"), "GetPrimaryHeirShare");
        object share = method.Invoke(null, new[] { plan, primary, (object)true });
        Check(list(share, "Fiefs").Count == 2 && list(share, "Fiefs").Contains(fiefs[2]), "Unassigned secondary package stays in the primary share");
        Check(list(share, "Titles").Count == 2 && !list(share, "Titles").Contains(titles[1]), "Assigned secondary titles are not granted to the Crown heir");
        list(list(plan, "SecondaryPackages")[0], "Titles").Clear();
        share = method.Invoke(null, new[] { plan, primary, (object)true });
        Check(!list(share, "Titles").Contains(titles[1]),
            "A secondary fief's barony cannot also enter the primary heir's title package");
        share = method.Invoke(null, new[] { plan, primary, (object)false });
        Check(list(share, "Fiefs").Count == 3 && list(share, "Titles").Count == 3, "Disabled partition leaves the whole estate in the primary share");
        list(plan, "NaturalHeirs").Remove(secondary);
        share = method.Invoke(null, new[] { plan, primary, (object)true });
        Check(list(share, "Fiefs").Count == 3, "Only one heir receives the whole estate");
        list(plan, "EstateFiefs").Clear();
        list(plan, "EstateTitles").Clear();
        share = method.Invoke(null, new[] { plan, primary, (object)true });
        Check(list(share, "Fiefs").Count == 0 && list(share, "Titles").Count == 0, "Empty estate produces an empty endowment");
        Check(list(living(primary, true), "Fiefs").Count == 0, "An entitled heir can ascend without any available land");
    }

    private static void TestIncomingCrownHouse()
    {
        Hero ruler = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        Hero heir = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        Hero donor = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        Clan royal = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        Clan source = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
        var method = AccessTools.Method(typeof(CrownAccessionBehavior), "CanUseExistingCrownHouse");
        Func<Hero, Hero, Hero, bool> retain = (h, l, ward) => (bool)method.Invoke(null, new object[] { h, l, ward });
        Check(retain(heir, heir, heir), "Existing clan-leading heir retains their house, including player continuation");
        Check(retain(heir, donor, heir), "A lawful ward retains their existing regency household");
        Check(!retain(heir, donor, donor), "A non-leading Crown heir cannot displace a living household head");
        Check(!retain(null, null, null), "Missing heir never counts as an existing house head");
        var record = new CrownAccessionRecord
        {
            Predecessor = ruler, PreviousHouse = royal, Heir = heir, CreatesCadet = true,
            IncomingSourceHead = donor, IncomingSourceHouse = source, IncomingHousePrepared = true,
            HasLivingEndowment = true
        };
        Check(record.EndowmentHouse == source && record.EndowmentDonor == donor,
            "Incoming estate source is independent of the outgoing Crown");
        Check(!record.IsAbdication, "Death accession cadet is not mislabeled as an abdication");
        var behavior = new CrownAccessionBehavior();
        AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions").SetValue(behavior, new List<CrownAccessionRecord> { record });
        Check(!behavior.HasInheritanceAdvance(donor, heir), "Unsettled incoming estate does not consume inheritance rights");
        record.EndowmentSettled = true;
        Check(!behavior.HasInheritanceAdvance(donor, heir), "A zero-delivery incoming estate does not consume a future inheritance");
        Check(behavior.IsPendingCrownHeir(heir) && !behavior.IsPendingCrownHeir(donor), "Partition identifies the exact pending Crown heir");
        record.Emergency = true;
        Check(!behavior.IsPendingCrownHeir(heir), "Emergency ballots do not reserve a stale heir household");
        record.Emergency = false;
        record.Completed = true;
        Check(!behavior.IsPendingCrownHeir(heir), "Completed accession releases the partition household guard");
        record.Completed = false;
        record.DeliveredGold = 100;
        Check(behavior.HasInheritanceAdvance(donor, heir), "Settled living endowment prevents repayment at the donor's death");
        Check(!behavior.HasInheritanceAdvance(ruler, heir), "Incoming endowment cannot be charged to the deceased monarch");
        record.HasLivingEndowment = false;
        Check(!behavior.HasInheritanceAdvance(donor, heir), "Unendowed cadet does not create a fictitious inheritance advance");
        record.IncomingHousePrepared = false;
        record.IncomingSourceHead = null;
        record.IncomingSourceHouse = null;
        record.VoluntaryAbdication = true;
        Check(record.EndowmentHouse == royal && behavior.HasInheritanceAdvance(ruler, heir),
            "Existing same-house abdication receipts retain their original estate source");
        AccessTools.Property(typeof(Hero), nameof(Hero.Gold)).SetValue(ruler, 900);
        AccessTools.Property(typeof(Hero), nameof(Hero.Gold)).SetValue(donor, 30);
        AccessTools.Property(typeof(Hero), nameof(Hero.Gold)).SetValue(heir, 10);
        var payment = new CrownAccessionRecord
        {
            Predecessor = ruler, IncomingSourceHead = donor, GoldRecipient = heir, EndowmentGold = 100
        };
        var deliver = AccessTools.Method(typeof(CrownAccessionBehavior), "DeliverAbdicationGold");
        try { deliver.Invoke(behavior, new object[] { payment }); }
        catch (TargetInvocationException) when (payment.GoldCredited) { /* No live campaign event dispatcher. */ }
        Check(ruler.Gold == 900 && donor.Gold == 0 && heir.Gold == 40,
            "External accession charges only available source-house gold, not the outgoing monarch");
        deliver.Invoke(behavior, new object[] { payment });
        Check(heir.Gold == 40 && payment.DeliveredGold == 30, "External gold receipt retains the actual amount across replay");
        var empty = new CrownAccessionRecord();
        Check((bool)deliver.Invoke(behavior, new object[] { empty }) && empty.GoldCredited && empty.GoldDebited,
            "Unendowed accession completes without requiring a fabricated gold recipient");
    }

    private static void TestAbdicationPreview(Kingdom realm, Hero ruler, Hero heir)
    {
        Func<CrownAccessionRecord> make = () => new CrownAccessionRecord
        {
            Realm = realm, Predecessor = ruler, Heir = heir, CreatesCadet = true,
            VoluntaryAbdication = true, CadetName = "Cadet", EndowmentFiefs = new List<string> { "town" },
            EndowmentTitles = new List<string> { "title" }, Household = new List<string> { "heir", "spouse" }
        };
        var comparison = AccessTools.Method(typeof(CrownAccessionBehavior), "SameAbdicationPreview");
        Func<CrownAccessionRecord, CrownAccessionRecord, bool> same = (a, b) => (bool)comparison.Invoke(null, new object[] { a, b });
        var original = make();
        Check(same(original, make()), "Unchanged abdication preview can be confirmed");
        foreach (var change in new Action<CrownAccessionRecord>[]
        {
            r => r.Heir = ruler, r => r.RequiresRegency = true, r => r.HouseLaw = HouseSuccessionLaw.Ultimogeniture,
            r => r.GenderLaw = GenderSuccessionLaw.Equal, r => r.EndowmentFiefs.Clear(),
            r => r.EndowmentTitles.Add("another_title"), r => r.Household.Remove("spouse"),
            r => r.CadetName = "Other", r => r.CreatesCadet = false, r => r.Emergency = true
        })
        {
            var changed = make();
            change(changed);
            Check(!same(original, changed), "Changed abdication preview requires renewed confirmation");
        }
        Check(!same(original, null), "Missing abdication preview is rejected");
    }

    private static void TestCadetFoundingMembers()
    {
        var method = AccessTools.Method(typeof(CadetHouseholdBehavior), "EnsureSlots");
        var members = new Dictionary<string, Hero>();
        var initialized = new Dictionary<string, bool>();
        int created = 0, activated = 0;
        Func<int, Hero> create = slot =>
        {
            created++;
            return (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        };
        Action<Hero, int> activate = (hero, slot) => activated++;
        Action<string, Action<Hero, int>> ensure = (id, init) =>
            method.Invoke(null, new object[] { id, members, initialized, create, init });
        ensure("cadet_a", activate);
        Check(created == 2 && activated == 2 && members.Count == 2, "Cadet creation adds exactly two founding nobles");
        ensure("cadet_a", activate);
        Check(created == 2 && activated == 2, "Repeated cadet preparation does not create or reinitialize nobles");
        AccessTools.Field(typeof(Hero), "_heroState").SetValue(members["cadet_a:0"], Hero.CharacterStates.Dead);
        ensure("cadet_a", activate);
        Check(created == 2, "Founding member death does not trigger replenishment");
        bool interrupted = false;
        try { ensure("cadet_b", (hero, slot) => { throw new InvalidOperationException("activation failure"); }); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { interrupted = true; }
        Check(interrupted && members.ContainsKey("cadet_b:0") && !initialized.ContainsKey("cadet_b:0"),
            "Interrupted activation preserves hero identity before completion");
        Hero pending = members["cadet_b:0"];
        ensure("cadet_b", activate);
        Check(created == 4 && ReferenceEquals(pending, members["cadet_b:0"]) && initialized.Count == 4,
            "Retry resumes the existing noble and only creates the missing slot");
        members = new Dictionary<string, Hero>(members);
        initialized = new Dictionary<string, bool>(initialized);
        ensure("cadet_b", activate);
        Check(created == 4 && activated == 4, "Restored receipt containers suppress repeat creation");
    }

    private static void TestForcedAbdication()
    {
        var gold = AccessTools.Method(typeof(CrownAccessionBehavior), "CalculateAbdicationGold");
        Func<int, int, int> share = (cash, others) => (int)gold.Invoke(null, new object[] { cash, others });
        Check(share(1000, 0) == 500, "Living abdication reserves a continuing-house cash share");
        Check(share(1000, 2) == 250, "Cash advance accounts for other eligible inheritance branches");
        Check(share(0, 3) == 0 && share(-10, 0) == 0, "Empty treasury creates no cash");
        Check(share(1001, 1) == 333, "Fractional inheritance gold remains in the original house");
        var monarch = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var heir = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var otherHeir = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
        var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        var record = new CrownAccessionRecord
        {
            Realm = realm, Predecessor = monarch, Heir = heir, ForcedAbdication = true,
            CreatesCadet = true, EndowmentSettled = true
        };
        var behavior = new CrownAccessionBehavior();
        AccessTools.Field(typeof(CrownAccessionBehavior), "_accessions").SetValue(behavior,
            new List<CrownAccessionRecord> { record });
        Check(record.IsAbdication && !record.VoluntaryAbdication, "Forced cause is distinct from voluntary abdication");
        Check(behavior.OwnsDeath(realm, monarch), "Pending forced settlement retains the target death boundary");
        record.Completed = true;
        Check(!behavior.OwnsDeath(realm, monarch), "Completed forced abdication releases the later death callback");
        record.ForcedCauseId = "first_rebellion";
        Check(!behavior.HasCompletedForcedAccession(realm, "first_rebellion"), "Tribunal cannot start before legal Crown title completion");
        record.TitleTransferred = true;
        Check(behavior.HasCompletedForcedAccession(realm, "first_rebellion"), "Exact completed rebellion unlocks its tribunal");
        Check(!behavior.HasCompletedForcedAccession(realm, "second_rebellion")
            && !behavior.HasCompletedForcedAccession(null, "first_rebellion"), "Old settlements cannot authorize another rebellion or realm");
        Check(behavior.HasInheritanceAdvance(monarch, heir), "Delivered inheritance remains recorded after accession");
        Check(!behavior.HasInheritanceAdvance(monarch, otherHeir)
            && !behavior.HasInheritanceAdvance(otherHeir, heir), "Advance excludes neither siblings nor other inheritance sources");
        record.EndowmentSettled = false;
        Check(!behavior.HasInheritanceAdvance(monarch, heir), "An unfulfilled promise is not a completed advance");
        var same = AccessTools.Method(typeof(CrownAccessionBehavior), "SameAbdicationPreview");
        var first = new CrownAccessionRecord { EndowmentGold = 5 };
        var second = new CrownAccessionRecord { EndowmentGold = 6 };
        Check(!(bool)same.Invoke(null, new object[] { first, second }), "Changed cash share requires a renewed player preview");

        AccessTools.Property(typeof(Hero), nameof(Hero.Gold)).SetValue(monarch, 1000);
        AccessTools.Property(typeof(Hero), nameof(Hero.Gold)).SetValue(heir, 100);
        var payment = new CrownAccessionRecord { Predecessor = monarch, GoldRecipient = heir, EndowmentGold = 250 };
        var deliver = AccessTools.Method(typeof(CrownAccessionBehavior), "DeliverAbdicationGold");
        try { deliver.Invoke(behavior, new object[] { payment }); }
        catch (TargetInvocationException) when (payment.GoldCredited) { /* No live campaign event dispatcher in this harness. */ }
        Check(monarch.Gold == 750 && heir.Gold == 350 && payment.GoldDebited && payment.GoldCredited,
            "Native gold mutation completes both saved legs before trade notification");
        deliver.Invoke(behavior, new object[] { payment });
        Check(monarch.Gold == 750 && heir.Gold == 350, "Replaying a completed gold delivery pays nothing twice");
    }

    private static bool AdultAge(ref float __result) { __result = 30f; return false; }

    private static Dictionary<Hero, float> FamilyAges;
    private static bool FamilyAge(Hero __instance, ref float __result)
    {
        __result = FamilyAges.TryGetValue(__instance, out float age) ? age : 30f;
        return false;
    }

    private static bool ZeroSuccessionSkill(ref int __result) { __result = 0; return false; }
    private static bool EmptyDefaultSkill(ref TaleWorlds.Core.SkillObject __result) { __result = null; return false; }

    private static void TestHereditaryRealmLine()
    {
        FamilyAges = new Dictionary<Hero, float>();
        var harmony = new Harmony("BellumCivile.Tests.RealmFamily");
        harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.Age)),
            prefix: new HarmonyMethod(typeof(Program), nameof(FamilyAge)));
        harmony.Patch(AccessTools.Method(typeof(Hero), nameof(Hero.GetSkillValue)),
            prefix: new HarmonyMethod(typeof(Program), nameof(ZeroSuccessionSkill)));
        foreach (var property in typeof(TaleWorlds.Core.DefaultSkills).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(TaleWorlds.Core.SkillObject)))
            harmony.Patch(property.GetGetMethod(), prefix: new HarmonyMethod(typeof(Program), nameof(EmptyDefaultSkill)));
        try
        {
            Func<string, float, Hero> make = (id, age) =>
            {
                var hero = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
                AccessTools.Property(typeof(Hero), "StringId").SetValue(hero, id);
                AccessTools.Field(typeof(Hero), "_children").SetValue(hero, new TaleWorlds.Library.MBList<Hero>());
                AccessTools.Field(typeof(Hero), "_heroState").SetValue(hero, Hero.CharacterStates.Active);
                FamilyAges[hero] = age;
                return hero;
            };
            Hero father = make("deposed", 60), king = make("king", 30), elder = make("elder_brother", 25), younger = make("younger_brother", 20);
            Hero generated = make("founding_noble", 40), wife = make("wife", 25);
            king.Father = elder.Father = younger.Father = father;
            var oldClan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
            var cadet = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
            foreach (Hero hero in new[] { father, elder, younger }) AccessTools.Field(typeof(Hero), "_clan").SetValue(hero, oldClan);
            foreach (Hero hero in new[] { king, generated, wife }) AccessTools.Field(typeof(Hero), "_clan").SetValue(hero, cadet);
            var candidates = new List<Hero> { generated, wife, father, younger, elder };
            var type = typeof(CrownAccessionBehavior).Assembly.GetType("BellumCivile.HereditaryRealmSuccession");
            var order = AccessTools.Method(type, "OrderLine");
            Func<HouseSuccessionLaw, List<Hero>> line = law => (List<Hero>)order.Invoke(null,
                new object[] { candidates, king, new SuccessionLawSet(GenderSuccessionLaw.Equal, law), new[] { father } });
            foreach (var law in new[] { HouseSuccessionLaw.Primogeniture, HouseSuccessionLaw.Ultimogeniture,
                HouseSuccessionLaw.Kinship, HouseSuccessionLaw.Seniority })
            {
                var heirs = line(law);
                Check(heirs.Count == 2 && heirs.Contains(elder) && heirs.Contains(younger),
                    law + " retains brothers outside the ruling cadet and excludes unrelated nobles/deposed father");
                Check(heirs[0] == (law == HouseSuccessionLaw.Ultimogeniture ? younger : elder), law + " retains legal sibling ordering");
            }
            Hero child = make("royal_child", 1);
            child.Father = king;
            candidates.Add(child);
            Check(line(HouseSuccessionLaw.Primogeniture)[0] == child, "New sovereign's infant child precedes collateral brothers under primogeniture");
            Check(line(HouseSuccessionLaw.Seniority)[0] == elder, "Seniority favors the oldest eligible blood relative");
            Check(line(HouseSuccessionLaw.Kinship)[0] == child, "Kinship includes infants and ranks their own kinship without a regent substitute");
            AccessTools.Property(typeof(Hero), nameof(Hero.IsFemale)).SetValue(child, true);
            var kinshipPreference = (List<Hero>)order.Invoke(null, new object[] { candidates, king,
                new SuccessionLawSet(GenderSuccessionLaw.MalePreference, HouseSuccessionLaw.Kinship), new[] { father } });
            Check(kinshipPreference[0] == child, "Kinship gender preference does not automatically defeat greater merit");
            var kinshipMaleOnly = (List<Hero>)order.Invoke(null, new object[] { candidates, king,
                new SuccessionLawSet(GenderSuccessionLaw.MaleOnly, HouseSuccessionLaw.Kinship), new[] { father } });
            Check(!kinshipMaleOnly.Contains(child), "Kinship retains hard gender eligibility");
            var martial = AccessTools.Method(typeof(CrownAccessionBehavior).Assembly.GetType("BellumCivile.SuccessionLawHelper"), "CalculateMartialElectiveScore");
            Check((int)martial.Invoke(null, new object[] { king }) == 0, "Household military score no longer rewards null-party army command");
            Hero uncle = make("older_uncle", 70);
            Hero grandfather = make("grandfather", 90);
            father.Father = uncle.Father = grandfather;
            candidates.Add(uncle);
            Check(line(HouseSuccessionLaw.Seniority)[0] == uncle, "Seniority no longer places younger siblings before older collateral kin");
            AccessTools.Property(typeof(Hero), nameof(Hero.IsFemale)).SetValue(uncle, true);
            var malePreference = (List<Hero>)order.Invoke(null, new object[] { candidates, king,
                new SuccessionLawSet(GenderSuccessionLaw.MalePreference, HouseSuccessionLaw.Seniority), new[] { father } });
            Check(malePreference[0] == elder, "Approved Seniority gender preference precedes age");
            candidates.Remove(uncle);
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(elder, Hero.CharacterStates.Prisoner);
            Check(line(HouseSuccessionLaw.Seniority)[0] == elder, "Captivity does not erase a brother's dynastic eligibility");
            candidates.Clear();
            candidates.Add(generated);
            Check(line(HouseSuccessionLaw.Primogeniture).Count == 0, "Extinct eligible bloodline has no unrelated-household fallback");
            var householdOrder = AccessTools.Method(typeof(CrownAccessionBehavior).Assembly.GetType("BellumCivile.SuccessionLawHelper"), "OrderSuccessionCandidates");
            Check(((List<Hero>)householdOrder.Invoke(null, new object[] { candidates, king,
                new SuccessionLawSet(GenderSuccessionLaw.Equal, HouseSuccessionLaw.Primogeniture), true, true })).Single() == generated,
                "Household ordering still permits its existing unrelated-member fallback");
            Check(line(HouseSuccessionLaw.MilitaryAcclamation).Count == 0, "Hereditary realm selector does not replace elective elections");
            Check(line(HouseSuccessionLaw.ElectiveSeniority).Count == 0, "Elective Seniority no longer triggers hereditary direct transfer");
            Hero replacementChild = make("replacement_child", 5);
            replacementChild.Father = generated;
            candidates.Add(replacementChild);
            var replacementLine = (List<Hero>)order.Invoke(null, new object[] { candidates, generated,
                new SuccessionLawSet(GenderSuccessionLaw.Equal, HouseSuccessionLaw.Primogeniture), null });
            Check(replacementLine.Single() == replacementChild, "A newly installed unrelated sovereign starts their own family line");
            var estateOrder = AccessTools.Method(typeof(PartitionSuccessionBehavior), "OrderEstateHeirs");
            var estateHeirs = (List<Hero>)estateOrder.Invoke(null, new object[] { new[] { father, generated }, father,
                new SuccessionLawSet(GenderSuccessionLaw.Equal, HouseSuccessionLaw.Primogeniture) });
            Check(estateHeirs.Contains(king) && estateHeirs.Contains(elder) && estateHeirs.Contains(younger),
                "Parent estate includes descendants outside the household candidate list");
            Check(!estateHeirs.Contains(generated) && !estateHeirs.Contains(wife),
                "Cross-clan inheritance excludes unrelated household nobles and spouses");
            Check(estateHeirs[0] == king, "Changing a child's clan does not change birth-order estate priority");
        }
        finally { harmony.UnpatchAll(harmony.Id); FamilyAges.Clear(); }
    }

    private static void TestCourtAgendaDeadlines()
    {
        var type = typeof(CourtAgendaRecord).Assembly.GetType("BellumCivile.CourtAgendaPresentation");
        var deadline = AccessTools.Method(type, "Deadline");
        Func<CourtAgendaRecord, (string text, CampaignTime date)?> read = agenda =>
            ((ValueTuple<string, CampaignTime>?)deadline.Invoke(null, new object[] { agenda }));
        Check(!read(null).HasValue, "No agenda does not invent a deadline");
        var agendaRecord = new CourtAgendaRecord { State = CourtAgendaState.Announced,
            SessionDate = CampaignTime.Days(10), VoteDate = CampaignTime.Days(15),
            NominationDeadline = CampaignTime.Days(8), AllocationVoteDate = CampaignTime.Days(20), HasScheduleSnapshot = true };
        Check(read(agendaRecord).Value.date.Equals(agendaRecord.VoteDate) && read(agendaRecord).Value.text.Contains("Vote scheduled"),
            "Scheduled agenda shows its saved vote date rather than the session date");
        agendaRecord.State = CourtAgendaState.Crisis;
        Check(read(agendaRecord).Value.date.Equals(agendaRecord.SessionDate) && read(agendaRecord).Value.text.Contains("Challenge considered"),
            "Crisis date is the saved consideration date, not a promised war");
        agendaRecord.State = CourtAgendaState.AwaitingNomination;
        Check(read(agendaRecord).Value.date.Equals(agendaRecord.NominationDeadline), "Player nomination shows the nomination deadline");
        agendaRecord.State = CourtAgendaState.AwaitingPlayerDecision;
        Check(read(agendaRecord).Value.date.Equals(agendaRecord.SessionDate), "Player response preserves the saved court session");
        agendaRecord.State = CourtAgendaState.Deliberating;
        agendaRecord.AllocationStage = true;
        Check(read(agendaRecord).Value.date.Equals(agendaRecord.AllocationVoteDate), "Second-stage land vote shows its allocation date");
        agendaRecord.AllocationStage = false;
        Check(read(agendaRecord).Value.date.Equals(agendaRecord.VoteDate), "Ordinary deliberation shows its frozen vote date");
        agendaRecord.State = CourtAgendaState.Announced;
        agendaRecord.HasScheduleSnapshot = false;
        Check(read(agendaRecord).Value.date.Equals(agendaRecord.SessionDate) && read(agendaRecord).Value.text.Contains("Court session"),
            "Legacy unsnapshotted schedule does not invent a vote date");
        agendaRecord.HasScheduleSnapshot = true;
        var decree = (string)AccessTools.Field(typeof(CourtAgendaRecord).Assembly.GetType("BellumCivile.CourtExecutiveRules"), "Decree").GetRawConstantValue();
        agendaRecord.ObjectiveData = new CourtObjectiveRecord { Kind = decree };
        Check(read(agendaRecord).Value.date.Equals(agendaRecord.SessionDate) && read(agendaRecord).Value.text.Contains("Royal judgment"),
            "Royal decree uses the judgment date, not a ballot date");
        foreach (var state in new[] { CourtAgendaState.Voting, CourtAgendaState.Passed, CourtAgendaState.Defeated,
            CourtAgendaState.Cancelled, CourtAgendaState.FulfilledElsewhere, CourtAgendaState.Withdrawn,
            CourtAgendaState.TooWeak, CourtAgendaState.Ultimatum, CourtAgendaState.Blocked,
            CourtAgendaState.NominationExpired, CourtAgendaState.Decreed, CourtAgendaState.NotProposed })
        {
            agendaRecord.State = state;
            agendaRecord.CrisisInterventionPending = true;
            Check(!read(agendaRecord).HasValue, "Resolved/current-voting state has no misleading upcoming date: " + state);
        }
    }

    private static void TestCourtObjectiveReports()
    {
        var type = typeof(CourtAgendaRecord).Assembly.GetType("BellumCivile.CourtObjectiveReports");
        var build = AccessTools.Method(type, "WithMood");
        var color = AccessTools.Method(type, "Color");
        var narrative = new TaleWorlds.Localization.TextObject("{FACTION} welcomes {TARGET}'s submission.")
            .SetTextVariable("FACTION", "Glory").SetTextVariable("TARGET", "Battania");
        var factionName = new TaleWorlds.Localization.TextObject("Glory");
        var faction = (FactionObject)FormatterServices.GetUninitializedObject(typeof(FactionObject));
        var mood = AccessTools.Field(typeof(FactionObject), "_mood");
        foreach (var fixture in new[] {
            new { Before = 40f, Shock = 10f, Expected = "+10 mood" },
            new { Before = 95f, Shock = 10f, Expected = "+5 mood" },
            new { Before = 100f, Shock = 10f, Expected = "0 mood" },
            new { Before = -95f, Shock = -10f, Expected = "-5 mood" },
            new { Before = -100f, Shock = -10f, Expected = "0 mood" },
            new { Before = 20f, Shock = -10f, Expected = "-10 mood" } })
        {
            mood.SetValue(faction, fixture.Before);
            float before = faction.Mood;
            faction.Mood = Math.Max(-100, Math.Min(100, before + fixture.Shock));
            var state = fixture.Shock > 0 ? CourtObjectiveState.Succeeded : CourtObjectiveState.Expired;
            string rendered = build.Invoke(null, new object[] { narrative, factionName, faction.Mood - before, state }).ToString();
            Check(rendered == "Glory welcomes Battania's submission.\nGlory: " + fixture.Expected,
                "Objective report renders observed capped mood change: " + fixture.Expected);
        }
        string cancelled = build.Invoke(null, new object[] { new TaleWorlds.Localization.TextObject("Set aside without blame."),
            factionName, 0f, CourtObjectiveState.Cancelled }).ToString();
        Check(cancelled == "Set aside without blame.\nFaction mood unchanged.", "Neutral cancellation explicitly reports unchanged mood");
        Check(color.Invoke(null, new object[] { CourtObjectiveState.Succeeded }).Equals(TaleWorlds.Library.Colors.Green), "Successful objective reports are green");
        Check(color.Invoke(null, new object[] { CourtObjectiveState.Expired }).Equals(TaleWorlds.Library.Colors.Red), "Failed objective reports are red");
        Check(color.Invoke(null, new object[] { CourtObjectiveState.Cancelled }).Equals(TaleWorlds.Library.Colors.Magenta), "Cancelled objectives retain politics color");
        Check(color.Invoke(null, new object[] { null }).Equals(TaleWorlds.Library.Colors.Magenta), "Activation reports retain politics color");
    }

    private static void TestCourtSessionReports()
    {
        var type = typeof(CivilWarResolutionBehavior).Assembly.GetType("BellumCivile.CourtSessionEventReports");
        var change = AccessTools.Method(type, "Change");
        var unit = new TaleWorlds.Localization.TextObject("security");
        Func<float, float, string> format = (before, after) => (string)change.Invoke(null, new object[] { before, after, unit });
        Check(format(95, 100) == "+5 security", "Court report displays actual upper-capped gain");
        Check(format(3, 0) == "-3 security", "Court report displays actual lower-capped loss");
        Check(format(100, 100) == "0 security", "Court report does not invent a gain at the cap");
        Check(format(40, 60) == "+20 security", "Court report preserves full uncapped effects");
        Check(format(0.001f, 0) == "0 security", "Court report avoids negative zero from float noise");
        Check((string)AccessTools.Method(type, "ForTarget").Invoke(null, new object[] { "Odokh", "+5 security, 0 loyalty" })
            == "Odokh: +5 security, 0 loyalty", "Court effect detail names the affected settlement");
        var narrative = AccessTools.Method(type, "Narrative");
        foreach (var targets in new[] { "Odokh", "Odokh, Makeb" })
        {
            var report = (TaleWorlds.Localization.TextObject)narrative.Invoke(null, new object[] { "BC_CourtFestival" });
            report.SetTextVariable("FACTION", "Liberty");
            report.SetTextVariable("REALM", "Khuzait Khanate");
            report.SetTextVariable("TARGETS", targets);
            report.SetTextVariable("RULER", "Monchug");
            string rendered = report.ToString();
            Check(rendered.Contains("Liberty faction in Khuzait Khanate") && rendered.Contains("festivities in " + targets)
                && !rendered.Contains("{"), "Festival report renders its faction, realm and actual target list");
            var message = new TaleWorlds.Localization.TextObject("{REPORT}\n{EFFECT}")
                .SetTextVariable("REPORT", report).SetTextVariable("EFFECT", "Odokh: +5 security, 0 loyalty");
            Check(message.ToString() == rendered + "\nOdokh: +5 security, 0 loyalty", "Narrative and capped effects render on separate lines");
        }
    }

    private static void TestCoalitionSuccessionState()
    {
        var behavior = new CivilWarResolutionBehavior();
        var type = typeof(CivilWarResolutionBehavior);
        var deaths = (Dictionary<string, string>)AccessTools.Field(type, "_coalitionDeaths").GetValue(behavior);
        var nominees = (Dictionary<string, string>)AccessTools.Field(type, "_coalitionNominees").GetValue(behavior);
        var notices = (List<string>)AccessTools.Field(type, "_coalitionSuccessionNotices").GetValue(behavior);
        Check(deaths.Count == 0 && nominees.Count == 0 && notices.Count == 0, "Coalition succession starts with empty saved journals");
        Check(!(bool)AccessTools.Method(type, "IsCoalitionSuccession").Invoke(behavior, new object[] { null }),
            "A missing realm cannot authorize a coalition election");
        Check(((List<Clan>)AccessTools.Method(type, "CoalitionCandidates").Invoke(behavior, new object[] { null })).Count == 0,
            "Missing coalition returns no candidates");
        AccessTools.Method(type, "CaptureCoalitionDeath").Invoke(behavior, new object[] { null });
        Check(deaths.Count == 0, "Missing victim cannot create a pending succession");
        var realm = (Kingdom)FormatterServices.GetUninitializedObject(typeof(Kingdom));
        AccessTools.Property(typeof(Kingdom), "StringId").SetValue(realm, "coalition_fixture");
        deaths[realm.StringId] = "dead_claimant";
        deaths["other_coalition"] = "other_claimant";
        notices.Add(realm.StringId);
        notices.Add("other_coalition");
        for (int i = 0; i < 3; i++) nominees[realm.StringId + "|" + i] = "nominee_" + i;
        nominees["other_coalition|0"] = "other_nominee";
        KingSelectionAIPatch.ActiveElections[realm] = new SuccessionElectionProfile();
        AccessTools.Method(type, "ClearCoalitionSuccession").Invoke(behavior, new object[] { realm });
        Check(!deaths.ContainsKey(realm.StringId) && deaths.ContainsKey("other_coalition"), "Completing one coalition preserves the rival's pending succession");
        Check(nominees.Count == 1 && nominees.ContainsKey("other_coalition|0"), "Completed coalition clears only its own frozen nominees");
        Check(notices.SequenceEqual(new[] { "other_coalition" }), "Completed coalition clears only its own notice receipt");
        Check(!KingSelectionAIPatch.ActiveElections.ContainsKey(realm), "Completion invalidates the old election profile");
        AccessTools.Method(type, "ClearCoalitionSuccession").Invoke(behavior, new object[] { realm });
        Check(deaths.Count == 1 && nominees.Count == 1, "Repeated succession cleanup is idempotent");
    }

    private static void TestRegencyContinuity()
    {
        var harmony = new Harmony("BellumCivile.Tests.RegencyContinuity");
        harmony.Patch(AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.Age)),
            prefix: new HarmonyMethod(typeof(Program), nameof(AdultAge)));
        try
        {
            var clan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
            var otherClan = (Clan)FormatterServices.GetUninitializedObject(typeof(Clan));
            var regent = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            var ward = (Hero)FormatterServices.GetUninitializedObject(typeof(Hero));
            AccessTools.Field(typeof(Hero), "_clan").SetValue(regent, clan);
            var retain = AccessTools.Method(typeof(RegencyBehavior), "CanRetainRegent");
            foreach (Hero.CharacterStates state in Enum.GetValues(typeof(Hero.CharacterStates)))
            {
                AccessTools.Field(typeof(Hero), "_heroState").SetValue(regent, state);
                Check((bool)retain.Invoke(null, new object[] { regent, clan, ward }) == (state != Hero.CharacterStates.Dead),
                    "Recorded regent continuity in engine state " + state);
            }
            AccessTools.Field(typeof(Hero), "_heroState").SetValue(regent, Hero.CharacterStates.Active);
            Check(!(bool)retain.Invoke(null, new object[] { regent, otherClan, ward }), "A departed regent cannot retain the old house's office");
            Check(!(bool)retain.Invoke(null, new object[] { regent, clan, regent }), "Ward cannot be their own regent");
            Check(!(bool)retain.Invoke(null, new object[] { null, clan, ward }), "Unresolved regent does not satisfy retention");
            AccessTools.Property(typeof(Hero), "StringId").SetValue(regent, "former_generated_regent");
            var behavior = new RegencyBehavior();
            AccessTools.Field(typeof(RegencyBehavior), "_generatedRegentIds").SetValue(behavior,
                new Dictionary<string, bool> { [regent.StringId] = true });
            Check(behavior.IsGeneratedRegent(regent), "Former generated caretaker stays excluded without any active regency record");
            AccessTools.Field(typeof(Hero), "_clan").SetValue(regent, otherClan);
            Check(behavior.IsGeneratedRegent(regent), "Generated identity survives a move to another clan");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
