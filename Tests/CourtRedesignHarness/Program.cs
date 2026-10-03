using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BellumCivile;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string label) { _checks++; if (!value) throw new Exception(label); }
    private static void Read(IdeologyPolicyAgendaConfig config, string xml, bool patch)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        typeof(IdeologyPolicyAgendaConfig).GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(config, new object[] { stream, patch });
    }
    private static void Main(string[] args)
    {
        if (args.Contains("--favoritism")) { CrownFavoritismPrototype.Run(); return; }
        if (args.Contains("--council-selection")) { CouncilSelectionSimulation.Run(); return; }
        if (args.Contains("--marriage-strategy")) { MarriageStrategySimulation.Run(); return; }
        CourtObjectiveFrameworkTests.Run(Check);
        MarriageHealthTests.Run(Check);
        MarriagePoliticalTests.Run(Check);
        var hereditary = new[] { HouseSuccessionLaw.Primogeniture, HouseSuccessionLaw.Ultimogeniture,
            HouseSuccessionLaw.Kinship, HouseSuccessionLaw.Seniority };
        var houseLaws = Enum.GetValues<HouseSuccessionLaw>();
        Check(houseLaws.Length == 8, "Eight existing household laws retained");
        Check(houseLaws.Count(law => SuccessionRealmRules.Classify(law) == RealmSuccessionSystem.Elective) == 4, "Four elective laws");
        foreach (var law in houseLaws)
        {
            Check(SuccessionRealmRules.Classify(law) == (hereditary.Contains(law)
                ? RealmSuccessionSystem.Hereditary : RealmSuccessionSystem.Elective), "Explicit Crown classification");
            foreach (var gender in Enum.GetValues<GenderSuccessionLaw>())
            {
                var resolved = new SuccessionLawSet(gender, law);
                Check(resolved.GenderLaw == gender && resolved.SuccessionLaw == law,
                    "Classification preserves typed household law view");
            }
        }
        Check(SuccessionRealmRules.Classify((HouseSuccessionLaw)(-1)) == RealmSuccessionSystem.Unknown, "Unknown negative law is not hereditary");
        Check(SuccessionRealmRules.Classify((HouseSuccessionLaw)99) == RealmSuccessionSystem.Unknown, "Unknown future law requires explicit classification");
        int[,] weights = { {8,2,4}, {-4,2,8}, {2,-4,4}, {0,8,0}, {4,-4,2} };
        var types = new[] { FactionType.Nobility, FactionType.Glory, FactionType.Liberty };
        for (int code = 0; code < 3125; code++)
        {
            int n = code;
            int[] trait = new int[5];
            for (int axis = 0; axis < 5; axis++) { trait[axis] = n % 5 - 2; n /= 5; }
            var result = CourtAffiliationMath.Personality(trait[0], trait[1], trait[2], trait[3], trait[4]);
            for (int bloc = 0; bloc < 3; bloc++)
                Check(result[types[bloc]] == Enumerable.Range(0, 5).Sum(a => trait[a] * weights[a, bloc]), "Personality matrix");
        }
        foreach (var anchor in new[] { (0,0f), (60,0f), (80,.5f), (100,1f), (150,2f), (200,3f), (250,4f), (300,4f) })
            Check(CourtAffiliationMath.Competence(anchor.Item1) == anchor.Item2, "Training competence anchor");
        var hero = new Hero();
        hero.Skills[DefaultSkills.Tactics] = 250;
        hero.Skills[DefaultSkills.Bow] = 250;
        Check(CourtAffiliationMath.Training(hero)[FactionType.Glory] == 4, "Exceptional martial profile");
        foreach (var skill in typeof(DefaultSkills).GetFields()) hero.Skills[(SkillObject)skill.GetValue(null)] = 250;
        Check(CourtAffiliationMath.Training(hero).Values.All(v => v == 0), "Equal training removes common floor");
        Check(CourtAffiliationMath.Training(null).Values.All(v => v == 0), "Missing hero");
        Check(CourtAgendaRules.Qualifies(3,7,2,true,true,1), "Exactly 30% contested");
        Check(!CourtAgendaRules.Qualifies(2,5,2,true,true,1), "Below 30%");
        Check(CourtAgendaRules.Qualifies(1,0,1,true,true,1), "Solo projected majority");
        Check(!CourtAgendaRules.Qualifies(1,1,1,true,true,1), "Tie needs another house");
        Check(!CourtAgendaRules.Qualifies(0,0,2,true,true,1), "Zero votes");
        Check(!CourtAgendaRules.Qualifies(3,0,2,false,true,1), "Filing reserve");
        Check(!CourtAgendaRules.Qualifies(3,0,2,true,false,1), "Sponsor must commit");
        Check(!CourtAgendaRules.Qualifies(3,0,2,true,true,0), "Sponsor needs positive preference");
        foreach (int year in new[] {24,84})
            foreach (double fraction in new[] {.25,.5,1,2,4})
            {
                double interval = year * fraction;
                Check(CourtAgendaRules.NextBoundary(interval, interval) == 2 * interval, "No partial new realm term");
                Check(CourtAgendaRules.NextBoundary(interval - .1, interval) == interval, "Calendar boundary");
                int nominationDays = CourtAgendaRules.NominationDays(interval);
                Check(nominationDays == Math.Max(1, (int)Math.Floor(interval / 8)), "Nomination window scales with term");
                double session = 100 + nominationDays + 1;
                Check(CourtAgendaRules.NominationCloses(100, session, nominationDays) == session - 1, "Earliest session leaves full nomination window");
                Check(CourtAgendaRules.NominationCloses(session - 2, session, nominationDays) <= session - 1, "Late substitution cannot overlap session");
                Check(CourtAgendaRules.NominationCloses(session, session, nominationDays) < session, "No substitution after session");
            }
        Check(CourtAgendaRules.NominationDays(84) == 10, "Vanilla annual nomination window");
        Check(CourtAgendaRules.NominationDays(24) == 3, "Fast annual nomination window");
        Check(CourtAgendaRules.NominationCloses(100, 160, 10) == 110, "Late session does not extend nomination window");
        foreach (CourtAgendaState state in Enum.GetValues(typeof(CourtAgendaState)))
        {
            var record = new CourtAgendaRecord { State = state };
            bool unopened = state == CourtAgendaState.Announced || state == CourtAgendaState.Crisis
                || state == CourtAgendaState.AwaitingNomination || state == CourtAgendaState.AwaitingPlayerDecision;
            Check(record.IsUnopened == unopened, "Only actionable agenda states remain unopened");
            Check(record.IsFiled == (state == CourtAgendaState.Deliberating || state == CourtAgendaState.Voting), "Nomination does not count as filing");
        }
        var savedIds = typeof(CourtAgendaRecord).GetFields().Select(f => f.GetCustomAttribute<TaleWorlds.SaveSystem.SaveableFieldAttribute>()?.Id).ToList();
        Check(savedIds.Count == 50 && savedIds.All(id => id.HasValue) && savedIds.Distinct().Count() == 50, "Agenda saved fields have unique IDs");
        var liberationIds = typeof(CourtLiberationRecord).GetFields().Select(f => f.GetCustomAttribute<TaleWorlds.SaveSystem.SaveableFieldAttribute>()?.Id).ToList();
        Check(liberationIds.Count == 8 && liberationIds.All(id => id.HasValue) && liberationIds.Distinct().Count() == 8, "Liberation identity, activation and war receipts have unique saved fields");
        CourtAppeasementTests.Run(Check);
        CourtPeaceTests.Run(Check);
        CourtCampaignTests.Run(Check);
        CourtSubjugationTests.Run(Check);
        CourtClaimTests.Run(Check);
        CourtDynasticTests.Run(Check);
        CourtProtectionTests.Run(Check);
        CourtProtectionExecutionTests.Run(Check);
        CourtTradeTests.Run(Check);
        CourtTitleGrantTests.Run(Check);
        CourtRallyTests.Run(Check);
        CourtMandateTests.Run(Check);
        var technicalFailures = new[] { "policy_missing", "queue_rejected_after_payment", "is_allowed_exception", "add_decision_exception",
            "add_decision_failed", "abolish_flag_missing", "leader_flag_missing", "initial_outcomes_exception", "support_option_exception" };
        var politicalFailures = new[] { "sponsor_ineligible", "realm_invalid", "faction_invalid", "policy_already_in_target_state",
            "blocked_vote_timeout", "stale_vote_timeout", "not_allowed", "unknown_cancel", "filed_motion_missing_from_queue_and_vote" };
        foreach (var state in new[] { CourtAgendaState.Deliberating, CourtAgendaState.Voting })
        foreach (int paid in new[] { 0, 25, 100 })
        foreach (string reason in technicalFailures.Concat(politicalFailures))
        {
            var motion = new CourtAgendaRecord { State = state, PaidInfluence = paid };
            int expected = technicalFailures.Contains(reason) ? paid : 0;
            Check(motion.SettleCancellation(reason) == expected, "Only technical failure refunds actual filing cost in either filed stage");
            Check(motion.State == CourtAgendaState.Cancelled && motion.PaymentSettled && motion.CancellationReason == reason,
                "Cancellation saves reason and settles payment");
            Check(motion.SettleCancellation("add_decision_exception") == 0, "Repeated or reclassified cancellation cannot double-refund");
            var loaded = new CourtAgendaRecord();
            foreach (var field in typeof(CourtAgendaRecord).GetFields()) field.SetValue(loaded, field.GetValue(motion));
            Check(loaded.SettleCancellation(reason) == 0 && loaded.CancellationReason == reason, "Settled cancellation remains settled after field roundtrip");
        }
        foreach (var state in new[] { CourtAgendaState.Passed, CourtAgendaState.Defeated })
        {
            var motion = new CourtAgendaRecord { State = state, PaidInfluence = 100, PaymentSettled = true, ResultApplied = true };
            Check(motion.SettleCancellation("policy_missing") == 0 && motion.State == state, "Concluded votes cannot be refunded or relabeled");
        }
        Check(!PolicyRefundRules.IsTechnical(new[] { "is_allowed_exception", "proposer_invalid" }), "Political reason defeats mixed technical classification");
        Check(!PolicyRefundRules.IsTechnical(Array.Empty<string>()), "No reason is not evidence of a technical fault");
        Check(PolicyRefundRules.IsTechnical(new[] { "leader_flag_missing", "proposer_missing" }), "Missing saved queue data is technical");
        foreach (int interval in new[] { 6, 24, 84, 336 })
        foreach (int deliberation in new[] { 1, 7, 30 })
        {
            var agenda = new CourtAgendaRecord { SessionDate = CampaignTime.Days(100), State = CourtAgendaState.Announced };
            agenda.FreezeSchedule(interval, deliberation);
            Check(agenda.VoteDate.ToDays == 100 + deliberation && agenda.TermDays == interval, "Announcement freezes session timing and cooldown duration");
            agenda.FreezeSchedule(interval * 2, deliberation * 2);
            Check(agenda.VoteDate.ToDays == 100 + deliberation && agenda.TermDays == interval, "MCM changes cannot rewrite existing snapshot");
            var loaded = new CourtAgendaRecord();
            foreach (var field in typeof(CourtAgendaRecord).GetFields()) field.SetValue(loaded, field.GetValue(agenda));
            loaded.FreezeSchedule(999, 99);
            Check(loaded.HasScheduleSnapshot && loaded.TermDays == interval && loaded.VoteDate.ToDays == 100 + deliberation,
                "Saveable-field roundtrip retains schedule; not an engine serialization test");
            var renewed = new CourtAgendaRecord { SessionDate = CampaignTime.Days(200) };
            renewed.FreezeSchedule(interval * 2, deliberation * 2);
            Check(renewed.TermDays == interval * 2 && renewed.VoteDate.ToDays == 200 + deliberation * 2, "Renewal adopts new settings");
            var legacy = new CourtAgendaRecord { SessionDate = CampaignTime.Days(100) };
            legacy.FreezeSchedule(interval, deliberation, CampaignTime.Days(177));
            Check(legacy.VoteDate.ToDays == 177, "Legacy migration preserves an already queued voting date");
        }
        for (int houses = 3; houses <= 30; houses++)
            Check(CourtAgendaRules.LandConcentration(houses, houses, (houses + 2) / 3) == -1, "Equal estates across court sizes");
        Check(CourtAgendaRules.LandConcentration(3,10,6) == 1, "Exact concentrated boundary");
        Check(CourtAgendaRules.LandConcentration(3,10,4) == -1, "Exact broad boundary");
        Check(CourtAgendaRules.LandConcentration(3,10,5) == 0, "Neutral land distribution");
        Check(CourtAgendaRules.LandConcentration(2,10,10) == 0, "Small court neutral");
        Check(CourtAgendaRules.LandConcentration(4,0,0) == 0, "No estates neutral");
        var config = new IdeologyPolicyAgendaConfig();
        var realm = new Kingdom();
        var ruler = new Clan { Kingdom = realm };
        var vassal = new Clan { Kingdom = realm };
        realm.RulingClan = ruler;
        Check(!CourtMembershipEligibility.CanBelong(ruler, realm), "Ruler cannot join court bloc");
        Check(CourtMembershipEligibility.CanBelong(vassal, realm), "Vassal can join court bloc");
        realm.RulingClan = vassal;
        Check(!CourtMembershipEligibility.CanBelong(vassal, realm), "Incoming ruler loses eligibility");
        Check(CourtMembershipEligibility.CanBelong(ruler, realm), "Former ruler regains eligibility");
        Check(!CourtMembershipEligibility.CanBelong(ruler, new Kingdom()), "Foreign clan not eligible");
        Check(!CourtMembershipEligibility.IsRuler(null), "Missing clan not Crown");
        realm.Temporary = true;
        Check(!CourtMembershipEligibility.IsRuler(vassal), "Temporary rebel shell does not create a Crown institution");
        realm.Temporary = false;
        Check(!CourtMembershipEligibility.CanBelong(new Clan { Kingdom = realm, IsMinorFaction = true }, realm), "Minor house excluded");
        var playerClan = new Clan { Kingdom = realm, IsMinorFaction = true };
        Clan.PlayerClan = playerClan;
        Check(CourtMembershipEligibility.CanBelong(playerClan, realm), "Player vassal with native minor-faction flag can join court blocs");
        playerClan.IsUnderMercenaryService = true;
        Check(!CourtMembershipEligibility.CanBelong(playerClan, realm), "Player mercenary remains excluded");
        playerClan.IsUnderMercenaryService = false;
        realm.RulingClan = playerClan;
        Check(!CourtMembershipEligibility.CanBelong(playerClan, realm), "Player ruler remains excluded");
        realm.RulingClan = ruler;
        Check(CourtMembershipEligibility.CanBelong(playerClan, realm), "Former player ruler can rejoin as vassal");
        Check(!CourtMembershipEligibility.CanBelong(playerClan, new Kingdom()), "Player exception does not allow foreign court membership");
        playerClan.IsEliminated = true;
        Check(!CourtMembershipEligibility.CanBelong(playerClan, realm), "Eliminated player clan remains excluded");
        Clan.PlayerClan = null;
        Read(config, "<BellumPolicyAgendas><Crown><Policy id='crown'/></Crown><Faction type='Glory'><Policy id='custom'/><Policy id='opposed' stance='Oppose'/></Faction></BellumPolicyAgendas>", false);
        Check(config.GetStance(FactionType.Glory,"custom") == CourtPolicyStance.Support, "Default Support");
        Check(config.GetStance(FactionType.Glory,"opposed") == CourtPolicyStance.Oppose, "Explicit Oppose");
        Check(config.GetStance(FactionType.Nobility,"custom") == CourtPolicyStance.Neutral, "Unlisted neutral");
        Check(config.IsCrownPolicy("crown"), "Crown roster");
        Read(config, "<BellumPolicyAgendaPatch><Crown><Remove id='crown'/></Crown><Faction type='Glory'><Add id='custom' stance='Neutral'/><Remove id='opposed'/></Faction></BellumPolicyAgendaPatch>", true);
        Check(!config.IsCrownPolicy("crown"), "Crown removal");
        Check(config.GetSupportedPolicies(FactionType.Glory).Count == 0, "Patch neutrality and removal");
        Check(CourtFactionRoster.Types.Count == 3 && !CourtFactionRoster.Types.Contains(FactionType.Royalists), "Three active identities");
        var displayConfig = new IdeologyPolicyAgendaConfig();
        Read(displayConfig, "<BellumPolicyAgendas><Crown><Policy id='core'/></Crown><Faction type='Glory'><Policy id='core' stance='Oppose'/><Policy id='supported'/><Policy id='opposed' stance='Oppose'/><Policy id='inactive'/></Faction><Faction type='Liberty'><Policy id='opposed'/></Faction></BellumPolicyAgendas>", false);
        var enacted = new[] { "core", "supported", "opposed", "neutral", "supported" };
        var blocDisplay = EnactedPolicyStances.Build(displayConfig, enacted, FactionType.Glory, false);
        Check(blocDisplay.Count == 3 && blocDisplay["core"] == CourtPolicyStance.Oppose, "Bloc views retain their own opposition to Crown laws");
        Check(blocDisplay["supported"] == CourtPolicyStance.Support && blocDisplay["opposed"] == CourtPolicyStance.Oppose, "Support and oppose use explicit stances");
        Check(!blocDisplay.ContainsKey("inactive") && !blocDisplay.ContainsKey("neutral"), "Inactive and neutral policies are omitted");
        var crownDisplay = EnactedPolicyStances.Build(displayConfig, enacted, FactionType.Glory, true);
        Check(crownDisplay.Count == 3 && crownDisplay["core"] == CourtPolicyStance.Support, "Core Crown support overrides favored bloc opposition without duplicate entries");
        Check(crownDisplay["supported"] == CourtPolicyStance.Support && crownDisplay["opposed"] == CourtPolicyStance.Oppose, "Crown displays favored bloc stances outside core policies");
        var impartial = EnactedPolicyStances.Build(displayConfig, enacted, null, true);
        Check(impartial.Count == 1 && impartial["core"] == CourtPolicyStance.Support, "Impartial Crown only supports enacted core laws");
        var renewedDisplay = EnactedPolicyStances.Build(displayConfig, enacted, FactionType.Liberty, true);
        Check(renewedDisplay["opposed"] == CourtPolicyStance.Support && !renewedDisplay.ContainsKey("supported"), "Changed favor changes display without stale stances");
        Check(EnactedPolicyStances.Build(displayConfig, Array.Empty<string>(), FactionType.Glory, true).Count == 0, "Empty enacted laws produce empty lists");
        foreach (CourtAgendaState state in Enum.GetValues(typeof(CourtAgendaState)))
        {
            string expected = state == CourtAgendaState.Passed || state == CourtAgendaState.Completed ? "#82E06AFF" : state == CourtAgendaState.Defeated ? "#FF6B6BFF" : "#F1D8A4FF";
            Check(CourtAgendaPresentation.Color(state) == expected, "Passed votes and completed activities receive success color; rejected votes receive failure color");
            Check(!CourtAgendaPresentation.Status(state).Contains('\n'), "Compact agenda status never inserts a line break");
        }
        Check(CourtAgendaPresentation.Color(null) == "#F1D8A4FF", "Missing agenda remains neutral");
        Check(CourtAgendaPresentation.Status(CourtAgendaState.Passed).EndsWith("passed"), "Passed suffix");
        Check(CourtAgendaPresentation.Status(CourtAgendaState.Defeated).EndsWith("rejected"), "Rejected suffix");
        var reignRulers = new System.Collections.Generic.Dictionary<string, string>();
        foreach (double powerValue in new[] { 0.0, 1.0, 500.0, 10000.0 })
        foreach (double chance in new[] { 0.0, .2, .5, 1.0 })
        foreach (double loyalty in new[] { 0.0, .25, .5, .75, 1.0 })
        {
            var share = new CrownPowerShare(powerValue, chance, loyalty);
            Check(Math.Abs(share.Total - share.Loyalists - share.Rebels - share.Uncommitted) < 1e-7, "Crown partitions clan power exactly once");
            Check(share.Rebels == powerValue * chance, "Expected rebel power uses joining probability");
            Check(share.Loyalists == powerValue * (1 - chance) * loyalty, "Loyalist power uses remaining probability and commitment");
        }
        var reluctant = new CrownPowerShare(1000, .4, .5);
        Check(reluctant.Loyalists == 300 && reluctant.Rebels == 400 && reluctant.Uncommitted == 300, "Partial support is not automatically rebel strength");
        var sovereign = new CrownPowerShare(500, 1, 0, ruler: true);
        Check(sovereign.Loyalists == 500 && sovereign.Rebels == 0, "Ruler always wholly loyalist");
        var undecided = new CrownPowerShare(500, .8, 1, undecidedPlayer: true);
        Check(undecided.Uncommitted == 500, "Undecided player contributes to neither side");
        var declared = new CrownPowerShare(500, 0, 1);
        Check(declared.Loyalists == 500, "Explicit player loyalty counts in full");
        var rebelMember = new CrownPowerShare(500, 1, 1);
        Check(rebelMember.Rebels == 500 && rebelMember.Loyalists == 0, "Recorded rebel contributes only to rebel side");
        var combinedPower = sovereign.Add(reluctant).Add(undecided);
        Check(combinedPower.Total == 2000 && combinedPower.Loyalists == 800 && combinedPower.LoyalistPercent == 40,
            "Realm denominator includes uncommitted houses and rebels");
        Check(new CrownPowerShare(0, 0, 1).LoyalistPercent == 0, "Zero-power realm has zero percentage");
        Check(new CrownPowerShare(-100, 0, 1).Total == 0 && new CrownPowerShare(double.NaN, 0, 1).Total == 0,
            "Invalid clan power cannot corrupt Crown bar");
        foreach (int? choice in new int?[] { null, -3, -2, -1, 5, 6, 7 })
        foreach (bool isPlayerRuler in new[] { false, true })
        foreach (bool matchingOwner in new[] { false, true })
        foreach (double now in new[] { 90.0, 99.9, 100.0, 101.0 })
        {
            bool expected = isPlayerRuler && matchingOwner && (choice == -1 || choice == -2) && now < 100;
            Check(CrownFavorSelectionRules.CanSelect(isPlayerRuler, matchingOwner, choice, 100, now) == expected,
                "Only current player ruler may spend unselected favor before saved expiry");
        }
        int termChoice = -1;
        Check(CrownFavorSelectionRules.CanSelect(true, true, termChoice, 184, 183), "Player may remain impartial and choose late in term");
        termChoice = (int)FactionType.Nobility;
        Check(!CrownFavorSelectionRules.CanSelect(true, true, termChoice, 184, 183), "Banner selection locks all faction choices");
        termChoice = -1;
        Check(CrownFavorSelectionRules.CanSelect(true, true, termChoice, 268, 185), "New term resets to impartial and unlocks choice");
        var reignStarts = new System.Collections.Generic.Dictionary<string, CampaignTime>();
        var campaignStart = CampaignTime.Days(100);
        Check(CrownReignClock.Update(reignRulers, reignStarts, "realm", "first", CampaignTime.Days(130), campaignStart).ToDays == 100,
            "Initial or legacy ruler starts at campaign start");
        Check(CrownReignClock.Update(reignRulers, reignStarts, "realm", "first", CampaignTime.Days(150), campaignStart).ToDays == 100,
            "Repeated reconciliation does not reset reign");
        Check(CrownReignClock.Update(reignRulers, reignStarts, "realm", "heir", CampaignTime.Days(155), campaignStart).ToDays == 155,
            "New hero resets reign even within the same ruling clan");
        var loadedRulers = new System.Collections.Generic.Dictionary<string, string>(reignRulers);
        var loadedStarts = new System.Collections.Generic.Dictionary<string, CampaignTime>(reignStarts);
        Check(CrownReignClock.Update(loadedRulers, loadedStarts, "realm", "heir", CampaignTime.Days(200), campaignStart).ToDays == 155,
            "Stored ruler and date survive dictionary roundtrip");
        Check(CrownReignClock.Update(loadedRulers, loadedStarts, "realm", "first", CampaignTime.Days(210), campaignStart).ToDays == 210,
            "Restored ruler begins a new reign, not accumulated reign time");
        Check(CrownReignClock.Update(reignRulers, reignStarts, "new", "founder", CampaignTime.Days(230), campaignStart, true).ToDays == 230,
            "New kingdom starts reign at creation");
        reignStarts["unassigned"] = CampaignTime.Days(240);
        Check(CrownReignClock.Update(reignRulers, reignStarts, "unassigned", "founder", CampaignTime.Days(245), campaignStart).ToDays == 240,
            "Kingdom creation date survives delayed ruler assignment");
        foreach (int yearLength in new[] { 24, 84, 365 })
        {
            Check(CrownReignClock.Months(0, yearLength) == 0, "New reign less than a month");
            Check(CrownReignClock.Months(yearLength / 12.0, yearLength) == 1, "Month follows campaign calendar");
            Check(CrownReignClock.Months(yearLength, yearLength) == 12, "Full year boundary");
            Check(CrownReignClock.Months(yearLength * 2.25, yearLength) == 27, "Years and remaining months");
            Check(CrownReignClock.Months(-1, yearLength) == 0, "No negative reign duration");
        }
        Console.WriteLine($"PASS: {_checks} production-math and XML-parser checks. Game lifecycle and save/load are not simulated by these stubs.");
    }
}
