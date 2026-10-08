/* =====================================================================================
* QUICK REFERENCE CHEAT COMMANDS (Copy & Paste in-game console)
* =====================================================================================
* * --- PLAYER FACTION SHORTCUTS ---
* civilwars.populate_player_faction
* civilwars.force_ultimatum
* civilwars.win
* civilwars.lose
* civilwars.make_me_king
* * --- AI / WORLD MANIPULATION ---
* civilwars.create_faction [Kingdom Name] [FactionType]
* civilwars.start [Kingdom Name] [FactionType]
* civilwars.end [Original Kingdom Name] [loyalist/rebel]
* civilwars.set_ideology_mood [Kingdom Name] [FactionType] [Value]
* civilwars.force_faction_meeting [FactionType]
* civilwars.trigger_council_incident
* civilwars.marry [Hero One] | [Hero Two]
* civilwars.force_marriage_offer [matrilineal/patrilineal]
* civilwars.fund_highwaymen [Kingdom Name]
* civilwars.trigger_coalition [Kingdom Name] [IdeologyType]
* * --- TREASON & LOYALTY TESTING ---
* civilwars.set_ruler_relation_player [Value]
* civilwars.set_ruler_relation [Clan Name] [Value]
* civilwars.force_treason [vote/decree] [Kingdom Name] | [Clan or Leader Name]
* civilwars.pending_votes [optional Kingdom Name]
* civilwars.repair_pending_votes [optional Kingdom Name]
* civilwars.fief_vote_eval [Settlement Name/Id]
* civilwars.relation_breakdown [Hero One] | [Hero Two]
* civilwars.relation_stats [reset]
* civilwars.set_heir_loyalty [Kingdom Name/Id] | [Heir Name/Id] | [0..100 or clear]
* civilwars.titles_summary
* civilwars.title_info [Settlement/Kingdom/Title Id or Name]
* civilwars.grant_me [Settlement/Kingdom/Title Id or Name] [de facto/full]
* civilwars.title_children [Settlement/Kingdom/Title Id]
* civilwars.title_claims [optional Clan/Leader/Settlement/Kingdom/Title Id]
* civilwars.claim_feuds
* civilwars.claim_feud_wars
* civilwars.claim_feud_eval [Clan or Leader Name]
* civilwars.political_options_eval [Clan or Leader Name]
* civilwars.start_claim_feud [Clan or Leader Name]
* civilwars.advance_claim_feud [Clan or Leader Name] [pressure]
* civilwars.crown_authority
* civilwars.set_crown_authority [Kingdom Name] [Devastated/Weak/Normal/Strong/Absolute]
* civilwars.inheritance_plan [Clan or Leader Name] [optional reserved personal holdings]
* civilwars.usurpation_info [Settlement/Kingdom/Title Id] | [Clan or Leader Name]
* civilwars.fabrications
* civilwars.fabricate_claim [Clan or Leader Name] | [Settlement/Kingdom/Title Id] [free]
* civilwars.fabrication_ai_eval [Clan or Leader Name]
* civilwars.advance_fabrication [Clan or Leader Name] [progress 0..1]
* civilwars.foreign_policy [Kingdom Name] [optional | Court Faction Type]
* civilwars.foreign_vote [war/peace] [Kingdom Name] | [Target Kingdom Name]
* civilwars.war_will [Clan or Leader Name]
* civilwars.realm_war_will [Kingdom Name]
 * civilwars.war_score [Kingdom Name] | [Target Kingdom Name]
 * civilwars.foreign_treaties
 * civilwars.client_realms
 * civilwars.client_info [Client Kingdom Name]
 * civilwars.make_client [Client Kingdom Name] | [Suzerain Kingdom Name] [voluntary]
 * civilwars.end_client [Client Kingdom Name]
* civilwars.ruler_foreign_policy [Kingdom Name] [force]
* civilwars.force_foreign_policy [Kingdom Name] | [Court Faction Type]
* civilwars.formable_titles [county/duchy/kingdom/empire] [optional Clan Name]
* civilwars.form_title [county/duchy/kingdom/empire] [optional Clan Name] [free]
* * Valid Faction Types: 
* Independence, Abdication, InstallRuler, Traditionalists, Militarists, Aristocrats, Populists
* * Example usage: civilwars.set_ruler_relation dey Meroc -100
* * Example usage: civilwars.force_treason decree Vlandia | dey Meroc
* ===================================================================================== */

using BellumCivile.Behaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    /// <summary>
    /// Why did I do this file?
    /// To provide a suite of in-game console commands for debugging and testing the mod's core political and rebellion systems without needing to play through entire campaigns.
    /// </summary>
    public static class CheatCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("repair_regency_caretaker", "civilwars")]
        public static string RepairRegencyCaretaker(List<string> args)
        {
            if (Campaign.Current == null || args == null || args.Count != 1)
                return "Usage: civilwars.repair_regency_caretaker [confirmed generated caretaker Hero ID]";
            Hero hero = Hero.AllAliveHeroes.FirstOrDefault(candidate => candidate.StringId == args[0]);
            return RegencyBehavior.Instance?.RepairFormerGeneratedRegent(hero) == true
                ? "Caretaker identity restored. Reopen the succession panel. The ward and current regent are unchanged."
                : "No living hero with that ID in a house with an active regency, or the ID belongs to its ward.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("trigger_council_incident", "civilwars")]
        public static string TriggerCouncilIncident(List<string> args)
        {
            CouncilIncidentBehavior behavior = Campaign.Current?
                .GetCampaignBehavior<CouncilIncidentBehavior>();
            if (behavior == null)
                return "Error: Could not find CouncilIncidentBehavior.";

            string eventId = args == null || args.Count == 0
                ? string.Empty
                : string.Join(" ", args).Trim();
            return behavior.TryQueuePlayerCouncilIncident(eventId, out string result)
                ? "Success! " + result
                : "Error: " + result;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("create_faction", "civilwars")]
        public static string CreateTestFaction(List<string> args)
        {
            FactionObject testFaction = CreateFactionFromArgs(args, out string error);
            if (error != null) return error;

            return $"Success! Created '{testFaction.Name}' in {testFaction.ParentKingdom.Name}. Leader: {testFaction.Leader.Name}. Members: {testFaction.Members.Count}. Press your hotkey to check the UI!";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("populate_player_faction", "civilwars")]
        public static string PopulatePlayerFaction(List<string> args)
        {
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return "Error: Could not find FactionManagerBehavior.";

            FactionObject playerFaction = factionManager.GetRebelFaction(Clan.PlayerClan);

            if (playerFaction == null)
                return "Error: You are not currently leading an armed rebellion.";

            if (playerFaction.Leader != Clan.PlayerClan)
                return "Error: You must be the leader of the rebellion to use this command.";

            Kingdom targetKingdom = Clan.PlayerClan.Kingdom;
            if (targetKingdom == null) return "Error: You are not part of a kingdom.";

            List<Clan> eligibleClans = targetKingdom.Clans.Where(c =>
                c != targetKingdom.RulingClan &&
                !c.IsUnderMercenaryService &&
                !c.IsMinorFaction &&
                c != Clan.PlayerClan &&
                !playerFaction.Members.Contains(c)
            ).ToList();

            if (eligibleClans.Count == 0) return "Error: There are no eligible clans left in the kingdom to join your rebellion.";

            int numToAdd = MBRandom.RandomInt(1, eligibleClans.Count + 1);
            List<Clan> clansToAdd = eligibleClans.OrderBy(c => MBRandom.RandomInt()).Take(numToAdd).ToList();

            foreach (Clan clan in clansToAdd)
            {
                playerFaction.AddMember(clan);
            }

            return $"Success! Added {clansToAdd.Count} new clan(s) to your rebellion. Total members: {playerFaction.Members.Count}. Check the UI!";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("force_ultimatum", "civilwars")]
        public static string ForceUltimatum(List<string> args)
        {
            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return "Error: Could not find FactionManagerBehavior.";

            FactionObject playerFaction = factionManager.GetRebelFaction(Clan.PlayerClan);

            if (playerFaction == null)
                return "Error: You are not currently in an armed rebellion.";

            playerFaction.TriggerUltimatum();

            return $"Success! Bypassed requirements and forced ultimatum for '{playerFaction.Name}'.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("win", "civilwars")]
        public static string WinCivilWar(List<string> args)
        {
            Kingdom playerKingdom = Clan.PlayerClan.Kingdom;
            if (playerKingdom == null || !playerKingdom.StringId.EndsWith("_rebels"))
                return "Error: You are not currently fighting in an active rebel kingdom.";

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject activeFaction = factionManager?.GetFactionByRebelKingdom(playerKingdom);
            if (activeFaction == null) return "Error: Could not locate the FactionObject for this rebellion.";

            var resolutionBehavior = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
            if (resolutionBehavior == null) return "Error: Could not find CivilWarResolutionBehavior.";

            resolutionBehavior.ResolveRebelVictory(activeFaction, playerKingdom);

            return $"Success! The rebellion was victorious and changes have been applied.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("lose", "civilwars")]
        public static string LoseCivilWar(List<string> args)
        {
            Kingdom playerKingdom = Clan.PlayerClan.Kingdom;
            if (playerKingdom == null || !playerKingdom.StringId.EndsWith("_rebels"))
                return "Error: You are not currently fighting in an active rebel kingdom.";

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            FactionObject activeFaction = factionManager?.GetFactionByRebelKingdom(playerKingdom);
            if (activeFaction == null) return "Error: Could not locate the FactionObject for this rebellion.";

            var resolutionBehavior = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
            if (resolutionBehavior == null) return "Error: Could not find CivilWarResolutionBehavior.";

            resolutionBehavior.ResolveLiegeVictory(activeFaction, playerKingdom);

            return $"Success! The rebellion was crushed by the liege. Check for executions and confiscations!";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("make_me_king", "civilwars")]
        public static string MakeMeKing(List<string> args)
        {
            Kingdom kingdom = Clan.PlayerClan.Kingdom;
            if (kingdom == null)
                return "Error: You must be a vassal of a kingdom to usurp it.";

            FeudalTitleBehavior titleBehavior = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            if (kingdom.RulingClan == Clan.PlayerClan)
            {
                bool repaired = titleBehavior?.TrySetKingdomTitleRuler(
                    kingdom,
                    Clan.PlayerClan,
                    legalTransfer: false,
                    reason: "civilwars.make_me_king repair") == true;

                return repaired
                    ? $"Success! You already ruled {kingdom.Name}; its sovereign title is now synchronized to your clan de facto."
                    : "Error: You already rule this kingdom, but its sovereign title could not be synchronized.";
            }

            Clan oldRuler = kingdom.RulingClan;

            Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>()
                ?.ClearUnauthorizedPlayerSuccessionForKingdom(kingdom, "civilwars.make_me_king");

            RulerTransitionDebugHelper.Report(
                "civilwars.make_me_king",
                kingdom,
                oldRuler,
                Clan.PlayerClan,
                RulerTransitionDebugHelper.BuildValidityDetails(kingdom, Clan.PlayerClan),
                requestInGameDisplay: true);

            kingdom.RulingClan = Clan.PlayerClan;

            bool titleSynced = titleBehavior?.TrySetKingdomTitleRuler(
                    kingdom,
                    Clan.PlayerClan,
                    legalTransfer: false,
                    reason: "civilwars.make_me_king") == true;

            string titleResult = titleSynced
                ? " The realm's sovereign title is now held by your clan de facto."
                : " Warning: the realm changed ruler, but its sovereign title could not be synchronized.";

            return $"Success! {oldRuler.Name} has been deposed. You are now the ruler of {kingdom.Name}.{titleResult} Go talk to your companion to test the Spymaster system!";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("debug_messages", "civilwars")]
        public static string ToggleDebugMessages(List<string> args)
        {
            if (args == null || args.Count == 0)
            {
                string state = BellumCivileDebug.ShowInGameMessages ? "ON" : "OFF";
                return $"Bellum Civile in-game debug messages are currently {state}. Usage: civilwars.debug_messages on|off|status";
            }

            string command = args[0].ToLowerInvariant();
            if (command == "on" || command == "1" || command == "true")
            {
                BellumCivileDebug.SetInGameMessages(true);
                return "Bellum Civile in-game debug messages are now ON.";
            }

            if (command == "off" || command == "0" || command == "false")
            {
                BellumCivileDebug.SetInGameMessages(false);
                return "Bellum Civile in-game debug messages are now OFF.";
            }

            if (command == "status")
            {
                string state = BellumCivileDebug.ShowInGameMessages ? "ON" : "OFF";
                return $"Bellum Civile in-game debug messages are currently {state}.";
            }

            return "Usage: civilwars.debug_messages on|off|status";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("force_election", "civilwars")]
        public static string ForceElection(List<string> args) =>
            RunElectionTestCommand(args, (behavior, realm) => behavior.ForceTestElection(realm));

        [CommandLineFunctionality.CommandLineArgumentFunction("force_three_way_war", "civilwars")]
        public static string ForceThreeWayWar(List<string> args) =>
            RunElectionTestCommand(args, (behavior, realm) => behavior.ForceThreeWayTest(realm));

        [CommandLineFunctionality.CommandLineArgumentFunction("election_test_status", "civilwars")]
        public static string ElectionTestStatus(List<string> args) =>
            RunElectionTestCommand(args, (behavior, realm) => behavior.TestReport(realm));

        [CommandLineFunctionality.CommandLineArgumentFunction("stop_election_test", "civilwars")]
        public static string StopElectionTest(List<string> args) =>
            RunElectionTestCommand(args, (behavior, realm) => behavior.StopTest(realm));

        private static string RunElectionTestCommand(List<string> args, Func<ElectiveContestBehavior, Kingdom, string> command)
        {
            const string usage = "Usage: civilwars.force_election [Kingdom Name/Id]\n"
                + "civilwars.force_three_way_war [Kingdom Name/Id] (test save only; assigns all noble houses to three sides)\n"
                + "civilwars.election_test_status [Kingdom Name/Id]\n"
                + "civilwars.stop_election_test [Kingdom Name/Id]\n"
                + "Omit the kingdom to use your current realm. Example: civilwars.force_election Northern Empire";
            if (Campaign.Current == null) return "Error: Load a campaign first.";
            string query = args == null ? "" : string.Join(" ", args).Trim();
            if (query == "help" || query == "?") return usage;
            Kingdom realm = Clan.PlayerClan?.Kingdom;
            if (query.Length > 0 && !TryResolveKingdom(query, out realm, out string error)) return error;
            if (realm == null) return usage;
            var behavior = ElectiveContestBehavior.Instance;
            return behavior == null ? "Error: Elective contest services are unavailable." : command(behavior, realm);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("set_heir_loyalty", "civilwars")]
        public static string SetHeirLoyalty(List<string> args)
        {
            const string usage = "Usage: civilwars.set_heir_loyalty [Kingdom Name/Id] | [Heir Name/Id] | [0..100 or clear]\n"
                + "Example: civilwars.set_heir_loyalty Vlandia | Erdurand | 10\nUse clear instead of the number to restore normal loyalty.";
            if (Campaign.Current == null) return "Error: Load a campaign first.";
            if (!TryParseHeirLoyalty(args, out string realmQuery, out string heirQuery, out double? target)) return usage;
            if (!TryResolveKingdom(realmQuery, out Kingdom realm, out string error)) return error;
            var behavior = HereditaryLoyaltyBehavior.Instance;
            if (behavior == null || !CrownAccessionBehavior.IsHereditaryRealm(realm))
                return "Error: This command requires a hereditary realm with a loyalty assessment.";
            behavior.Maintain(realm);
            if (!TryResolveLawfulHeir(realm, heirQuery, out Hero heir, out error)) return error;
            double? before = behavior.Get(realm, heir)?.Total;
            if (!behavior.SetTestLoyalty(realm, heir, target)) return "Error: The heir's loyalty is currently unavailable; no override was applied.";
            double? after = behavior.Get(realm, heir)?.Total;
            return $"{heir.Name} [{heir.StringId}] in {realm.Name}: {before:0.##}% -> {after:0.##}%. "
                + (target.HasValue ? "Session-only override; cleared on loading a save or a change of sovereign. " : "Normal loyalty restored. ")
                + "Reopen the succession panel to refresh. Natural checks run every seven campaign days per realm; use civilwars.start_heir_challenge for an immediate test.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("start_heir_challenge", "civilwars")]
        public static string StartHeirChallenge(List<string> args)
        {
            if (Campaign.Current == null) return "Error: Load a campaign first.";
            if (!TryParseHeirChallenge(args, out string realmQuery, out string heirQuery))
                return "Usage: civilwars.start_heir_challenge [Kingdom Name/Id] | [Heir Name/Id]";
            if (!TryResolveKingdom(realmQuery, out Kingdom realm, out string error)) return error;
            if (!TryResolveLawfulHeir(realm, heirQuery, out Hero heir, out error)) return error;
            return SuccessionChallengeBehavior.Instance?.StartTestChallenge(realm, heir) ?? "Challenge behavior unavailable.";
        }

        private static bool TryResolveLawfulHeir(Kingdom realm, string heirQuery, out Hero heir, out string error)
        {
            heir = null; error = null;
            var behavior = HereditaryLoyaltyBehavior.Instance;
            if (behavior == null || !CrownAccessionBehavior.IsHereditaryRealm(realm))
            { error = "This command requires a hereditary realm."; return false; }
            behavior.Maintain(realm);
            var line = behavior.GetLine(realm).ToList();
            heir = line.FirstOrDefault(h => string.Equals(h.StringId, heirQuery, StringComparison.OrdinalIgnoreCase));
            if (heir == null)
            {
                var matches = line.Where(h => string.Equals(h.Name.ToString(), heirQuery, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0) matches = line.Where(h => h.Name.ToString().IndexOf(heirQuery, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (matches.Count != 1)
                {
                    error = (matches.Count == 0 ? "No matching hereditary heir. Eligible heirs: " : "Ambiguous heir name; use an ID: ")
                        + string.Join(", ", (matches.Count == 0 ? line : matches).Select(h => $"{h.Name} [{h.StringId}]"));
                    return false;
                }
                heir = matches[0];
            }
            return true;
        }

        internal static bool TryParseHeirChallenge(List<string> args, out string realm, out string heir)
        {
            realm = heir = null;
            if (args == null) return false;
            var parts = string.Join(" ", args).Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace)) return false;
            realm = parts[0]; heir = parts[1];
            return true;
        }

        internal static bool TryParseHeirLoyalty(List<string> args, out string realm, out string heir, out double? target)
        {
            realm = heir = null; target = null;
            if (args == null) return false;
            var parts = string.Join(" ", args).Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace)) return false;
            realm = parts[0]; heir = parts[1];
            if (string.Equals(parts[2], "clear", StringComparison.OrdinalIgnoreCase)) return true;
            if (!double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
                || !SuccessionChallengeRules.Finite(value) || value < 0 || value > 100) return false;
            target = value;
            return true;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("relation_breakdown", "civilwars")]
        public static string RelationBreakdown(List<string> args)
        {
            if (!TrySplitHeroArguments(args, out string firstHeroQuery, out string secondHeroQuery, out string usageError))
                return "Usage: civilwars.relation_breakdown [Hero One] | [Hero Two]\nExample: civilwars.relation_breakdown Ira | Rhagaea";

            if (!TryResolveHero(firstHeroQuery, out Hero firstHero, out string firstHeroError))
                return firstHeroError;

            if (!TryResolveHero(secondHeroQuery, out Hero secondHero, out string secondHeroError))
                return secondHeroError;

            DynamicRelationBehavior behavior = Campaign.Current?.GetCampaignBehavior<DynamicRelationBehavior>();
            if (behavior == null)
                return "Error: Could not find DynamicRelationBehavior.";

            return behavior.BuildDebugBreakdown(firstHero, secondHero);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("relation_stats", "civilwars")]
        public static string RelationStats(List<string> args)
        {
            if (args != null && (args.Count > 1 || (args.Count == 1 && !string.Equals(args[0], "reset", StringComparison.OrdinalIgnoreCase))))
                return "Usage: civilwars.relation_stats [reset]";
            var behavior = Campaign.Current?.GetCampaignBehavior<DynamicRelationBehavior>();
            return behavior == null ? "Error: Could not find DynamicRelationBehavior."
                : behavior.BuildPerformanceDiagnostics(args?.Count == 1);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("title_name_stats", "civilwars")]
        public static string TitleNameStats(List<string> args)
        {
            if (args != null && (args.Count > 1 || (args.Count == 1 && !string.Equals(args[0], "reset", StringComparison.OrdinalIgnoreCase))))
                return "Usage: civilwars.title_name_stats [reset]";
            return Patches.FeudalTitleHeroNamePatch.BuildPerformanceDiagnostics(args?.Count == 1);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("fief_vote_eval", "civilwars")]
        public static string FiefVoteEvaluation(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.fief_vote_eval [Settlement Name/Id]";

            string query = string.Join(" ", args).Trim();
            List<Settlement> matches = Settlement.All
                .Where(settlement => settlement != null
                    && (settlement.StringId.Equals(query, StringComparison.OrdinalIgnoreCase)
                        || settlement.Name?.ToString().Equals(query, StringComparison.OrdinalIgnoreCase) == true
                        || settlement.StringId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                        || settlement.Name?.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
            Settlement target = matches.FirstOrDefault(settlement =>
                settlement.StringId.Equals(query, StringComparison.OrdinalIgnoreCase)
                || settlement.Name?.ToString().Equals(query, StringComparison.OrdinalIgnoreCase) == true);

            if (target == null && matches.Count == 1)
                target = matches[0];
            if (target == null && matches.Count > 1)
                return $"Error: settlement query '{query}' is ambiguous. Matches: {string.Join(", ", matches.Take(10).Select(settlement => $"{settlement.Name} ({settlement.StringId})"))}.";
            if (target == null)
                return $"Error: could not find a settlement matching '{query}'.";

            Kingdom kingdom = target.OwnerClan?.Kingdom ?? target.MapFaction as Kingdom;
            if (kingdom == null)
                return $"Error: {target.Name} is not currently held by a kingdom.";

            FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            List<Clan> voters = Clan.All
                .Where(clan => FiefNominationHelper.IsValidVoter(clan, kingdom))
                .ToList();
            List<Clan> validCandidates = FiefNominationHelper.GetValidCandidates(kingdom, null).ToList();
            List<Clan> eligibleCandidates = FiefNominationHelper.GetEligibleCandidates(kingdom, target, null).ToList();
            HashSet<string> eligibleIds = new HashSet<string>(eligibleCandidates.Select(clan => clan.StringId));
            Clan capturer = target.Town?.LastCapturedBy;
            Dictionary<string, int> nominations = eligibleCandidates.ToDictionary(clan => clan.StringId, _ => 0);
            Dictionary<string, float> totals = validCandidates.ToDictionary(clan => clan.StringId, _ => 0f);
            Dictionary<string, List<string>> reasons = validCandidates.ToDictionary(clan => clan.StringId, _ => new List<string>());

            foreach (Clan voter in voters)
            {
                FiefNominationResult best = null;
                foreach (Clan candidate in validCandidates)
                {
                    FiefNominationResult result = FiefNominationHelper.ScoreCandidate(
                        voter,
                        candidate,
                        kingdom,
                        target,
                        capturer,
                        factionManager);
                    if (result == null)
                        continue;

                    totals[candidate.StringId] += result.Score;
                    reasons[candidate.StringId].AddRange(result.Reasons);
                    if (eligibleIds.Contains(candidate.StringId) && (best == null || result.Score > best.Score))
                        best = result;
                }

                if (best?.Candidate != null)
                    nominations[best.Candidate.StringId]++;
            }

            List<string> lines = validCandidates
                .Select(candidate =>
                {
                    int desired = FactionObject.CalculateDesiredFiefs(candidate);
                    int ceiling = desired + C.FiefCandidateOrdinaryExcessAllowance;
                    bool deJure = FiefNominationHelper.IsDirectDeJureHolder(candidate, target);
                    float average = voters.Count > 0 ? totals[candidate.StringId] / voters.Count : 0f;
                    int nominationCount = nominations.TryGetValue(candidate.StringId, out int count) ? count : 0;
                    string topReasons = string.Join(",", reasons[candidate.StringId]
                        .GroupBy(reason => reason)
                        .OrderByDescending(group => group.Count())
                        .ThenBy(group => group.Key)
                        .Take(3)
                        .Select(group => group.Key));
                    return new
                    {
                        Candidate = candidate,
                        NominationCount = nominationCount,
                        Average = average,
                        Line = $"- {candidate.Name} ({candidate.StringId}): nominations={nominationCount}; avg_score={average:0.0}; fiefs={candidate.Fiefs.Count}; desired={desired}; ordinary_ceiling={ceiling}; eligible={eligibleIds.Contains(candidate.StringId)}; direct_de_jure={deJure}; strength_bonus={MathF.Min(C.FiefCandidateStrengthBonusCap, candidate.CurrentTotalStrength * C.FiefCandidateStrengthScale):0.0}; influence_bonus={MathF.Min(C.FiefCandidateInfluenceBonusCap, candidate.Influence * C.FiefCandidateInfluenceScale):0.0}; common_reasons={topReasons}"
                    };
                })
                .OrderByDescending(entry => entry.NominationCount)
                .ThenByDescending(entry => entry.Average)
                .Select(entry => entry.Line)
                .ToList();

            return $"Fief vote evaluation for {target.Name} ({target.StringId}) in {kingdom.Name}: voters={voters.Count}; valid_candidates={validCandidates.Count}; eligible_candidates={eligibleCandidates.Count}; candidate_rule=desired fiefs + {C.FiefCandidateOrdinaryExcessAllowance} (direct de jure holder exempt; fallback to all if none qualify).\n"
                + string.Join("\n", lines);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("titles_summary", "civilwars")]
        public static string TitlesSummary(List<string> args)
        {
            FeudalTitleBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (behavior == null)
                return "Error: Could not find FeudalTitleBehavior.";

            return behavior.BuildSummary();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("title_info", "civilwars")]
        public static string TitleInfo(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.title_info [Settlement/Kingdom/Title Id or Name]";

            FeudalTitleBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (behavior == null)
                return "Error: Could not find FeudalTitleBehavior.";

            string query = string.Join(" ", args).Trim();
            FeudalTitleRecord title = ResolveFeudalTitleForDebug(behavior, query);

            if (title == null)
                return $"Error: Could not find a feudal title matching '{query}'.";

            return $"title={title.TitleId}; name={title.Name}; type={title.TitleType}; de_jure={title.DeJureHolderClanId}; de_facto={title.DeFactoHolderClanId}; de_jure_parent={title.ParentTitleId}; de_facto_parent={title.DeFactoParentTitleId}; capital={title.CapitalSettlementId}; kingdom={title.AssociatedKingdomId}; active={title.IsActive}";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("grant_me", "civilwars")]
        public static string GrantTitleToPlayer(List<string> args)
        {
            const string usage = "Usage: civilwars.grant_me [Settlement/Kingdom/Title Id or Name] [de facto/full]\nExample: civilwars.grant_me Barony of Talivel de facto";
            if (!TryParseTitleGrantArguments(args, out string titleQuery, out bool transferDeJure))
                return usage;

            Clan playerClan = Clan.PlayerClan;
            if (playerClan == null || playerClan.IsEliminated)
                return "Error: the player clan is unavailable.";

            FeudalTitleBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (behavior == null)
                return "Error: Could not find FeudalTitleBehavior.";

            if (!TryResolveFeudalTitleForGrant(behavior, titleQuery, out FeudalTitleRecord title, out string resolutionError))
                return resolutionError;

            string oldDeJure = title.DeJureHolderClanId;
            string oldDeFacto = title.DeFactoHolderClanId;
            if (!behavior.TryGrantTitleForTesting(
                playerClan,
                title,
                transferDeJure,
                out Settlement transferredSettlement,
                out string failureReason))
            {
                return $"Error: could not grant {FeudalTitleDisplayHelper.FormatTitleName(title)}: {failureReason}.";
            }

            string mode = transferDeJure ? "full (de facto and de jure)" : "de facto only";
            string settlementResult = transferredSettlement != null
                ? $" The linked settlement {transferredSettlement.Name} was also transferred to your clan."
                : string.Empty;
            return $"Success! Granted {FeudalTitleDisplayHelper.FormatTitleName(title)} ({title.TitleId}) to {playerClan.Name} {mode}. De facto: {oldDeFacto}->{title.DeFactoHolderClanId}; de jure: {oldDeJure}->{title.DeJureHolderClanId}.{settlementResult}";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("title_drift", "civilwars")]
        public static string TitleDrift(List<string> args)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalDeJureDriftBehavior driftBehavior = Campaign.Current?.GetCampaignBehavior<FeudalDeJureDriftBehavior>();
            if (titleBehavior == null || driftBehavior == null)
                return "Error: title or de jure drift behavior is unavailable.";

            if (args == null || args.Count == 0)
            {
                List<FeudalDeJureDriftRecord> records = driftBehavior.GetActiveDrifts().ToList();
                return records.Count == 0
                    ? "No active de jure drift candidates."
                    : "Active de jure drift candidates:\n" + string.Join("\n", records.Select(record =>
                        $"- title={record.TitleId}; target_parent={record.TargetParentTitleId}; target_kingdom={record.TargetKingdomId}; state={record.State}; progress={record.Progress:P1}"));
            }

            string query = string.Join(" ", args).Trim();
            FeudalTitleRecord title = ResolveFeudalTitleForDebug(titleBehavior, query);
            return title == null
                ? $"Error: Could not find a feudal title matching '{query}'."
                : driftBehavior.BuildDebugReport(title, refreshRecord: true);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("advance_title_drift", "civilwars")]
        public static string AdvanceTitleDrift(List<string> args)
        {
            if (args == null || args.Count < 2 || !int.TryParse(args.Last(), out int years) || years <= 0)
                return "Usage: civilwars.advance_title_drift [Settlement/Kingdom/Title Id or Name] [Campaign Years]";

            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalDeJureDriftBehavior driftBehavior = Campaign.Current?.GetCampaignBehavior<FeudalDeJureDriftBehavior>();
            if (titleBehavior == null || driftBehavior == null)
                return "Error: title or de jure drift behavior is unavailable.";

            string query = string.Join(" ", args.Take(args.Count - 1)).Trim();
            FeudalTitleRecord title = ResolveFeudalTitleForDebug(titleBehavior, query);
            return title == null
                ? $"Error: Could not find a feudal title matching '{query}'."
                : driftBehavior.DebugAdvanceDrift(title, years);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("title_children", "civilwars")]
        public static string TitleChildren(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.title_children [Settlement/Kingdom/Title Id] [dejure/defacto]";

            FeudalTitleBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (behavior == null)
                return "Error: Could not find FeudalTitleBehavior.";

            FeudalHierarchyMode mode = FeudalHierarchyMode.DeJure;
            string modeArgument = args.LastOrDefault();
            if (string.Equals(modeArgument, "defacto", StringComparison.OrdinalIgnoreCase))
            {
                mode = FeudalHierarchyMode.DeFacto;
                args = args.Take(args.Count - 1).ToList();
            }
            else if (string.Equals(modeArgument, "dejure", StringComparison.OrdinalIgnoreCase))
            {
                args = args.Take(args.Count - 1).ToList();
            }

            string query = string.Join(" ", args).Trim();
            FeudalTitleRecord title = ResolveFeudalTitleForDebug(behavior, query);
            if (title == null)
                return $"Error: Could not find a feudal title matching '{query}'.";

            List<FeudalTitleRecord> children = behavior.GetChildTitles(title, mode).ToList();
            if (children.Count == 0)
                return $"{title.Name} ({title.TitleId}) has no {mode.ToString().ToLowerInvariant()} child titles.";

            return $"{title.Name} ({title.TitleId}) mode={mode}; children={children.Count}\n"
                + string.Join("\n", children.Select(child =>
                    $"- {child.Name}: title={child.TitleId}; type={child.TitleType}; de_jure={child.DeJureHolderClanId}; de_facto={child.DeFactoHolderClanId}; de_jure_parent={child.ParentTitleId}; de_facto_parent={child.DeFactoParentTitleId}; capital={child.CapitalSettlementId}; children={behavior.GetChildTitles(child, mode).Count}"));
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("title_claims", "civilwars")]
        public static string TitleClaims(List<string> args)
        {
            FeudalTitleBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (behavior == null)
                return "Error: Could not find FeudalTitleBehavior.";

            string query = args == null || args.Count == 0 ? string.Empty : string.Join(" ", args).Trim();
            List<FeudalClaimRecord> claims = behavior.GetAllClaims()
                .Where(claim => claim != null && claim.IsActive)
                .ToList();

            if (string.IsNullOrWhiteSpace(query))
            {
                return $"active explicit claims={claims.Count}\n"
                    + string.Join("\n", claims.Take(30).Select(FormatFeudalClaimForDebug));
            }

            if (TryResolveClanAny(query, out Clan clan, out _))
            {
                List<string> lines = claims
                    .Where(claim => claim.ClaimantClanId == clan.StringId)
                    .Take(30)
                    .Select(FormatFeudalClaimForDebug)
                    .ToList();

                List<FeudalTitleRecord> implied = behavior.GetTitlesHeldByClan(clan, deJure: true)
                    .Where(title => title != null && title.DeFactoHolderClanId != clan.StringId)
                    .ToList();
                lines.AddRange(implied.Select(title =>
                    $"- implied strong: clan={clan.StringId}; title={title.TitleId}; de_facto={title.DeFactoHolderClanId}; source=de_jure_holder"));

                return lines.Count == 0
                    ? $"{clan.Name} has no active explicit or contested de jure claims."
                    : $"{clan.Name} claims:\n" + string.Join("\n", lines);
            }

            FeudalTitleRecord titleRecord = ResolveFeudalTitleForDebug(behavior, query);
            if (titleRecord != null)
            {
                List<string> lines = claims
                    .Where(claim => claim.TargetTitleId == titleRecord.TitleId)
                    .Take(30)
                    .Select(FormatFeudalClaimForDebug)
                    .ToList();

                if (!string.IsNullOrWhiteSpace(titleRecord.DeJureHolderClanId)
                    && titleRecord.DeJureHolderClanId != titleRecord.DeFactoHolderClanId)
                {
                    lines.Insert(0, $"- implied strong: clan={titleRecord.DeJureHolderClanId}; title={titleRecord.TitleId}; de_facto={titleRecord.DeFactoHolderClanId}; source=de_jure_holder");
                }

                return lines.Count == 0
                    ? $"{titleRecord.Name} ({titleRecord.TitleId}) has no active explicit claims."
                    : $"{titleRecord.Name} ({titleRecord.TitleId}) claims:\n" + string.Join("\n", lines);
            }

            return $"Error: could not resolve '{query}' as a clan, leader, settlement, kingdom, or title.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("claim_feuds", "civilwars")]
        public static string ClaimFeuds(List<string> args)
        {
            ClaimFeudBehavior behavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (behavior == null)
                return "Error: Could not find ClaimFeudBehavior.";

            return behavior.BuildDebugReport();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("claim_feud_wars", "civilwars")]
        public static string ClaimFeudWars(List<string> args)
        {
            ClaimFeudWarBehavior behavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>();
            if (behavior == null)
                return "Error: Could not find ClaimFeudWarBehavior.";

            return behavior.BuildDebugReport();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("claim_feud_eval", "civilwars")]
        public static string ClaimFeudEval(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.claim_feud_eval [Clan or Leader Name]";

            ClaimFeudBehavior behavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (behavior == null)
                return "Error: Could not find ClaimFeudBehavior.";

            if (!TryResolveClanAny(string.Join(" ", args), out Clan clan, out string clanError))
                return clanError;

            behavior.TryForceEvaluateClan(clan, out string report);
            return report;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("political_options_eval", "civilwars")]
        public static string PoliticalOptionsEval(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.political_options_eval [Clan or Leader Name]";

            if (!TryResolveClanAny(string.Join(" ", args), out Clan clan, out string error))
                return error;

            FeudalPoliticalOptionsBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalPoliticalOptionsBehavior>();
            if (behavior == null)
                return "Error: Could not find FeudalPoliticalOptionsBehavior.";

            behavior.TryForceEvaluateClan(clan, out string report);
            return report;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("start_claim_feud", "civilwars")]
        public static string StartClaimFeud(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.start_claim_feud [Clan or Leader Name]";

            ClaimFeudBehavior behavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (behavior == null)
                return "Error: Could not find ClaimFeudBehavior.";

            if (!TryResolveClanAny(string.Join(" ", args), out Clan clan, out string clanError))
                return clanError;

            behavior.TryForceStartFeud(clan, out string report);
            return report;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("advance_claim_feud", "civilwars")]
        public static string AdvanceClaimFeud(List<string> args)
        {
            if (args == null || args.Count < 2)
                return "Usage: civilwars.advance_claim_feud [Clan or Leader Name] [pressure]";

            if (!float.TryParse(args.Last(), out float amount))
                return $"Error: invalid pressure amount '{args.Last()}'.";

            ClaimFeudBehavior behavior = Campaign.Current?.GetCampaignBehavior<ClaimFeudBehavior>();
            if (behavior == null)
                return "Error: Could not find ClaimFeudBehavior.";

            string clanQuery = string.Join(" ", args.Take(args.Count - 1)).Trim();
            if (!TryResolveClanAny(clanQuery, out Clan clan, out string clanError))
                return clanError;

            behavior.TryAdvanceFeudPressure(clan, amount, out string report);
            return report;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("crown_authority", "civilwars")]
        public static string CrownAuthority(List<string> args)
        {
            CrownAuthorityBehavior behavior = Campaign.Current?.GetCampaignBehavior<CrownAuthorityBehavior>();
            return behavior == null
                ? "Error: Could not find CrownAuthorityBehavior."
                : behavior.BuildDebugReport();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("set_crown_authority", "civilwars")]
        public static string SetCrownAuthority(List<string> args)
        {
            if (args == null || args.Count < 2)
                return "Usage: civilwars.set_crown_authority [Kingdom Name] [Devastated/Weak/Normal/Strong/Absolute]";

            string authorityText = args.Last();
            if (!Enum.TryParse(authorityText, ignoreCase: true, out CrownAuthorityLevel authority))
                return $"Error: invalid crown authority '{authorityText}'.";

            string kingdomQuery = string.Join(" ", args.Take(args.Count - 1)).Trim();
            if (!TryResolveKingdom(kingdomQuery, out Kingdom kingdom, out string kingdomError))
                return kingdomError;

            CrownAuthorityBehavior behavior = Campaign.Current?.GetCampaignBehavior<CrownAuthorityBehavior>();
            if (behavior == null)
                return "Error: Could not find CrownAuthorityBehavior.";

            behavior.SetAuthority(kingdom, authority, "debug command");
            return $"Success! {kingdom.Name} crown authority is now {authority}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("usurpation_info", "civilwars")]
        public static string UsurpationInfo(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.usurpation_info [Settlement/Kingdom/Title Id] | [Clan or Leader Name]";

            FeudalTitleBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (behavior == null)
                return "Error: Could not find FeudalTitleBehavior.";

            string raw = string.Join(" ", args).Trim();
            string[] parts = raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return "Usage: civilwars.usurpation_info [Settlement/Kingdom/Title Id] | [Clan or Leader Name]";

            FeudalTitleRecord title = ResolveFeudalTitleForDebug(behavior, parts[0].Trim());
            if (title == null)
                return $"Error: could not resolve title '{parts[0].Trim()}'.";

            if (!TryResolveClanAny(parts[1].Trim(), out Clan clan, out string clanError))
                return clanError;

            FeudalTitleUsurpationAssessment assessment = FeudalTitleUsurpationAssessmentService.Evaluate(
                behavior,
                clan,
                title,
                checkResources: true);
            string unitIds = assessment.ControlUnits.Count == 0
                ? "none"
                : string.Join(",", assessment.ControlUnits.Select(unit => unit.TitleId));

            return $"title={title.TitleId}; type={title.TitleType}; claimant={clan.StringId}; de_jure={title.DeJureHolderClanId}; de_facto={title.DeFactoHolderClanId}; control_units=actual_direct_de_jure_children; units={unitIds}; control={assessment.ControlledTitles}/{assessment.TotalTitles} ({assessment.ControlShare:P0}); required={assessment.RequiredTitles}; has_claim={assessment.HasClaim}; gold_cost={assessment.GoldCost}; influence_cost={assessment.InfluenceCost:0}; relation_hit=-30; eligible={assessment.CanUsurp}; reason={assessment.Reason}";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("fabrications", "civilwars")]
        public static string Fabrications(List<string> args)
        {
            FeudalClaimFabricationBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalClaimFabricationBehavior>();
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (behavior == null)
                return "Error: Could not find FeudalClaimFabricationBehavior.";

            List<string> lines = behavior.GetActiveFabrications()
                .Select(record =>
                {
                    Clan clan = Clan.All.FirstOrDefault(c => c != null && c.StringId == record.FabricatorClanId);
                    FeudalTitleRecord title = titleBehavior?.GetTitle(record.TargetTitleId);
                    return $"- clan={clan?.Name?.ToString() ?? record.FabricatorClanId}; hero={record.FabricatorHeroId}; title={title?.Name ?? record.TargetTitleId}; track={record.Track}; progress={record.Progress:P1}; daily={record.DailyProgressDelta:0.000000}; paid={record.PaidGoldCost}g/{record.PaidInfluenceCost:0}inf";
                })
                .ToList();

            return lines.Count == 0
                ? "No active claim fabrications."
                : "Active claim fabrications:\n" + string.Join("\n", lines);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("foreign_policy", "civilwars")]
        public static string ForeignPolicy(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.foreign_policy [Kingdom Name] [optional | Traditionalists/Militarists/Aristocrats/Populists]";

            int separatorIndex = args.FindIndex(arg => arg == "|");
            string kingdomQuery = separatorIndex >= 0
                ? string.Join(" ", args.Take(separatorIndex))
                : string.Join(" ", args);

            if (!TryResolveKingdom(kingdomQuery, out Kingdom kingdom, out string error))
                return error;

            FactionObject viewpoint = null;
            if (separatorIndex >= 0)
            {
                string factionQuery = string.Join(" ", args.Skip(separatorIndex + 1)).Trim();
                if (!TryParseCourtFaction(factionQuery, out FactionType factionType)
                    || (factionType != FactionType.Royalists
                        && factionType != FactionType.Glory
                        && factionType != FactionType.Nobility
                        && factionType != FactionType.Liberty))
                {
                    return $"Error: '{factionQuery}' is not a court faction type.";
                }

                FactionManagerBehavior factionManager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
                viewpoint = factionManager?.GetFactionsInKingdom(kingdom)
                    .FirstOrDefault(faction => faction != null && faction.IsIdeology && faction.Type == factionType);
                if (viewpoint == null)
                    return $"Error: {kingdom.Name} has no active {factionType} court faction.";
            }

            return new ForeignPolicyEvaluationService().BuildDebugReport(kingdom, viewpoint);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("foreign_vote", "civilwars")]
        public static string ForeignVote(List<string> args)
        {
            if (args == null || args.Count < 4)
                return "Usage: civilwars.foreign_vote [war/peace] [Kingdom Name] | [Target Kingdom Name]";

            string action = args[0]?.Trim().ToLowerInvariant();
            bool isWar = action == "war";
            bool isPeace = action == "peace";
            if (!isWar && !isPeace)
                return $"Error: '{args[0]}' must be either war or peace.";

            int separatorIndex = args.FindIndex(1, arg => arg == "|");
            if (separatorIndex <= 1 || separatorIndex >= args.Count - 1)
                return "Usage: civilwars.foreign_vote [war/peace] [Kingdom Name] | [Target Kingdom Name]";

            string sourceQuery = string.Join(" ", args.Skip(1).Take(separatorIndex - 1));
            string targetQuery = string.Join(" ", args.Skip(separatorIndex + 1));
            if (!TryResolveKingdom(sourceQuery, out Kingdom sourceKingdom, out string sourceError))
                return sourceError;
            if (!TryResolveKingdom(targetQuery, out Kingdom targetKingdom, out string targetError))
                return targetError;
            if (sourceKingdom == targetKingdom)
                return "Error: source and target kingdoms must be different.";
            if (isWar && sourceKingdom.IsAtWarWith(targetKingdom))
                return $"Error: {sourceKingdom.Name} is already at war with {targetKingdom.Name}.";
            if (isPeace && !sourceKingdom.IsAtWarWith(targetKingdom))
                return $"Error: {sourceKingdom.Name} is not at war with {targetKingdom.Name}.";

            Clan proposer = sourceKingdom.RulingClan;
            ForeignPolicyTributeAssessment terms = isPeace
                ? ForeignPolicyTributeHelper.AssessCurrentTerms(sourceKingdom, targetKingdom)
                : new ForeignPolicyTributeAssessment();
            List<ForeignPolicyVoteBreakdown> evaluations = new List<ForeignPolicyVoteBreakdown>();
            foreach (Clan voter in sourceKingdom.Clans.Where(clan => clan?.Leader != null
                         && !clan.IsEliminated
                         && !clan.IsUnderMercenaryService
                         && (!clan.IsMinorFaction || clan == Clan.PlayerClan)))
            {
                bool evaluated = isWar
                    ? ForeignPolicyVoteEvaluator.TryEvaluateWar(sourceKingdom, targetKingdom, proposer, voter, out ForeignPolicyVoteBreakdown breakdown)
                    : ForeignPolicyVoteEvaluator.TryEvaluatePeace(
                        sourceKingdom,
                        targetKingdom,
                        proposer,
                        voter,
                        terms.DailyTribute,
                        terms.DurationDays,
                        out breakdown);
                if (evaluated)
                    evaluations.Add(breakdown);
            }

            if (evaluations.Count == 0)
                return "No eligible clan votes could be evaluated.";

            string termsText = isPeace
                ? $"; tribute={terms.DailyTribute}/day for {terms.DurationDays} days ({terms.Classification})"
                : string.Empty;
            string lines = string.Join("\n", evaluations
                .OrderByDescending(evaluation => evaluation.TotalScore)
                .Select(evaluation => "- " + evaluation.Format()));
            return $"{action.ToUpperInvariant()} vote: {sourceKingdom.Name} -> {targetKingdom.Name}{termsText}\n"
                + "Positive totals support the motion; negative totals oppose it.\n"
                + lines;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("war_will", "civilwars")]
        public static string WarWill(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.war_will [Clan or Leader Name]";

            if (!TryResolveClanAny(string.Join(" ", args), out Clan clan, out string clanError))
                return clanError;

            WarPeaceRevampBehavior behavior = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            return behavior?.BuildClanDebugReport(clan) ?? "Error: WarPeaceRevampBehavior not found.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("realm_war_will", "civilwars")]
        public static string RealmWarWill(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.realm_war_will [Kingdom Name]";

            if (!TryResolveKingdom(string.Join(" ", args), out Kingdom kingdom, out string kingdomError))
                return kingdomError;

            WarPeaceRevampBehavior behavior = Campaign.Current?.GetCampaignBehavior<WarPeaceRevampBehavior>();
            return behavior?.BuildKingdomDebugReport(kingdom) ?? "Error: WarPeaceRevampBehavior not found.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("war_score", "civilwars")]
        public static string WarScore(List<string> args)
        {
            int separatorIndex = args?.FindIndex(arg => arg == "|") ?? -1;
            if (separatorIndex <= 0 || separatorIndex >= args.Count - 1)
                return "Usage: civilwars.war_score [Kingdom Name] | [Target Kingdom Name]";

            string firstQuery = string.Join(" ", args.Take(separatorIndex));
            string secondQuery = string.Join(" ", args.Skip(separatorIndex + 1));
            if (!TryResolveKingdom(firstQuery, out Kingdom first, out string firstError))
                return firstError;
            if (!TryResolveKingdom(secondQuery, out Kingdom second, out string secondError))
                return secondError;

            WarScoreBehavior behavior = Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>();
            return behavior?.BuildDebugReport(first, second) ?? "Error: WarScoreBehavior not found.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("foreign_treaties", "civilwars")]
        public static string ForeignTreaties(List<string> args)
        {
            ForeignTreatyBehavior behavior = Campaign.Current?.GetCampaignBehavior<ForeignTreatyBehavior>();
            return behavior?.BuildDebugReport() ?? "Error: ForeignTreatyBehavior not found.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("client_realms", "civilwars")]
        public static string ClientRealms(List<string> args)
        {
            ClientKingdomBehavior behavior = ClientKingdomBehavior.Instance;
            if (behavior == null)
                return "Error: ClientKingdomBehavior not found.";

            List<ClientKingdomRecord> records = behavior.GetClientRecords().ToList();
            if (records.Count == 0)
                return "No client kingdoms are currently registered.";

            return string.Join("\n", records.Select(record =>
            {
                Kingdom client = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == record.ClientKingdomId);
                Kingdom suzerain = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == record.SuzerainKingdomId);
                return $"- {client?.Name?.ToString() ?? record.ClientKingdomId} -> {suzerain?.Name?.ToString() ?? record.SuzerainKingdomId}; voluntary={record.WasVoluntary}; started_day={record.StartedDay:0.0}";
            }));
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("protection_preview", "civilwars")]
        public static string ProtectionPreview(List<string> args)
        {
            int separator = args?.FindIndex(a => a == "|") ?? -1;
            if (separator <= 0 || separator >= args.Count - 1 || args.Count(a => a == "|") != 1)
                return "Usage: civilwars.protection_preview [Applicant Realm] | [Threat Realm]";
            if (!TryResolveKingdom(string.Join(" ", args.Take(separator)), out Kingdom client, out string error)) return error;
            if (!TryResolveKingdom(string.Join(" ", args.Skip(separator + 1)), out Kingdom threat, out error)) return error;
            if (Campaign.Current == null) return "Error: no active campaign.";
            return CourtProtectionAssessmentService.DebugReport(client, threat);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("client_info", "civilwars")]
        public static string ClientInfo(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.client_info [Client Kingdom Name]";
            if (!TryResolveKingdom(string.Join(" ", args), out Kingdom client, out string error))
                return error;

            return ClientKingdomBehavior.Instance?.BuildDebugReport(client)
                ?? "Error: ClientKingdomBehavior not found.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("make_client", "civilwars")]
        public static string MakeClient(List<string> args)
        {
            int separatorIndex = args?.FindIndex(arg => arg == "|") ?? -1;
            if (separatorIndex <= 0 || separatorIndex >= args.Count - 1)
                return "Usage: civilwars.make_client [Client Kingdom Name] | [Suzerain Kingdom Name] [voluntary]";

            string clientQuery = string.Join(" ", args.Take(separatorIndex));
            List<string> suzerainArgs = args.Skip(separatorIndex + 1).ToList();
            bool voluntary = suzerainArgs.Any(arg => string.Equals(arg, "voluntary", StringComparison.OrdinalIgnoreCase));
            suzerainArgs.RemoveAll(arg => string.Equals(arg, "voluntary", StringComparison.OrdinalIgnoreCase));
            if (!TryResolveKingdom(clientQuery, out Kingdom client, out string clientError))
                return clientError;
            if (!TryResolveKingdom(string.Join(" ", suzerainArgs), out Kingdom suzerain, out string suzerainError))
                return suzerainError;

            ClientKingdomBehavior behavior = ClientKingdomBehavior.Instance;
            string report = "ClientKingdomBehavior not found";
            return behavior != null && behavior.TryEstablishClientKingdom(client, suzerain, voluntary, out report)
                ? "Success: " + report + "."
                : "Failed: " + report + ".";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("end_client", "civilwars")]
        public static string EndClient(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.end_client [Client Kingdom Name]";
            if (!TryResolveKingdom(string.Join(" ", args), out Kingdom client, out string error))
                return error;

            return ClientKingdomBehavior.Instance?.EndClientStatus(client, "ended by debug command") == true
                ? $"Client status ended for {client.Name}."
                : $"{client.Name} is not a registered client kingdom.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("ruler_foreign_policy", "civilwars")]
        public static string RulerForeignPolicy(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.ruler_foreign_policy [Kingdom Name] [force]";

            bool force = args.Any(arg => string.Equals(arg, "force", StringComparison.OrdinalIgnoreCase));
            string kingdomQuery = string.Join(" ", args
                .Where(arg => !string.Equals(arg, "force", StringComparison.OrdinalIgnoreCase)));
            if (!TryResolveKingdom(kingdomQuery, out Kingdom kingdom, out string error))
                return error;

            ForeignPolicyBehavior behavior = Campaign.Current?.GetCampaignBehavior<ForeignPolicyBehavior>();
            if (behavior == null)
                return "Error: Could not find ForeignPolicyBehavior.";

            behavior.TryRunRulerForeignPolicyProposal(kingdom, force, out string report);
            return report;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("force_foreign_policy", "civilwars")]
        public static string ForceForeignPolicy(List<string> args)
        {
            int separatorIndex = args?.FindIndex(arg => arg == "|") ?? -1;
            if (separatorIndex <= 0 || separatorIndex >= args.Count - 1)
                return "Usage: civilwars.force_foreign_policy [Kingdom Name] | [Traditionalists/Militarists/Aristocrats/Populists]";

            if (!TryResolveKingdom(string.Join(" ", args.Take(separatorIndex)), out Kingdom kingdom, out string error))
                return error;

            string factionQuery = string.Join(" ", args.Skip(separatorIndex + 1)).Trim();
            if (!TryParseCourtFaction(factionQuery, out FactionType factionType)
                || (factionType != FactionType.Royalists
                    && factionType != FactionType.Glory
                    && factionType != FactionType.Nobility
                    && factionType != FactionType.Liberty))
            {
                return $"Error: '{factionQuery}' is not a court faction type.";
            }

            IdeologyBehavior behavior = Campaign.Current?.GetCampaignBehavior<IdeologyBehavior>();
            return behavior?.ForceForeignPolicyMotion(kingdom, factionType)
                ?? "Error: Could not find IdeologyBehavior.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("fabricate_claim", "civilwars")]
        public static string FabricateClaim(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.fabricate_claim [Clan or Leader Name] | [Settlement/Kingdom/Title Id] [free]";

            FeudalClaimFabricationBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalClaimFabricationBehavior>();
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (behavior == null)
                return "Error: Could not find FeudalClaimFabricationBehavior.";
            if (titleBehavior == null)
                return "Error: Could not find FeudalTitleBehavior.";

            bool free = args.Any(arg => string.Equals(arg, "free", StringComparison.OrdinalIgnoreCase));
            string raw = string.Join(" ", args.Where(arg => !string.Equals(arg, "free", StringComparison.OrdinalIgnoreCase))).Trim();
            string[] parts = raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return "Usage: civilwars.fabricate_claim [Clan or Leader Name] | [Settlement/Kingdom/Title Id] [free]";

            if (!TryResolveClanAny(parts[0].Trim(), out Clan clan, out string clanError))
                return clanError;

            FeudalTitleRecord title = ResolveFeudalTitleForDebug(titleBehavior, parts[1].Trim());
            if (title == null)
                return $"Error: could not resolve title '{parts[1].Trim()}'.";

            if (!behavior.TryStartFabrication(clan, title, chargeCost: !free, out FeudalClaimFabricationRecord record, out string reason))
                return $"Error: could not start fabrication. Reason: {reason}.";

            return $"Started fabrication: clan={clan.Name}; title={title.Name}; track={record.Track}; cost={(free ? "free" : $"{record.PaidGoldCost}g/{record.PaidInfluenceCost:0}inf")}; daily_delta={record.DailyProgressDelta:0.000000}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("fabrication_ai_eval", "civilwars")]
        public static string FabricationAiEval(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.fabrication_ai_eval [Clan or Leader Name]";

            FeudalClaimFabricationBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalClaimFabricationBehavior>();
            if (behavior == null)
                return "Error: Could not find FeudalClaimFabricationBehavior.";

            if (!TryResolveClanAny(string.Join(" ", args), out Clan clan, out string clanError))
                return clanError;

            behavior.DebugEvaluateFabricationTarget(clan, out string result);
            return result;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("advance_fabrication", "civilwars")]
        public static string AdvanceFabrication(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.advance_fabrication [Clan or Leader Name] [progress 0..1]";

            FeudalClaimFabricationBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalClaimFabricationBehavior>();
            if (behavior == null)
                return "Error: Could not find FeudalClaimFabricationBehavior.";

            float progress = 1f;
            List<string> queryArgs = args.ToList();
            if (queryArgs.Count > 1 && float.TryParse(queryArgs.Last(), out float parsed))
            {
                progress = parsed;
                queryArgs.RemoveAt(queryArgs.Count - 1);
            }

            if (!TryResolveClanAny(string.Join(" ", queryArgs), out Clan clan, out string clanError))
                return clanError;

            behavior.DebugAdvanceFabrication(clan, progress, out string result);
            return result;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("inheritance_plan", "civilwars")]
        public static string InheritancePlan(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.inheritance_plan [Clan or Leader Name] [optional reserved personal holdings]";

            int reservedPersonalHoldings = BellumCivileOptions.PartitionSuccessionMainHeirReservedFiefs;
            List<string> queryArgs = args.ToList();
            if (queryArgs.Count > 1 && int.TryParse(queryArgs.Last(), out int parsedReserved))
            {
                reservedPersonalHoldings = Math.Max(1, parsedReserved);
                queryArgs.RemoveAt(queryArgs.Count - 1);
            }

            string query = string.Join(" ", queryArgs).Trim();
            if (!TryResolveClanAny(query, out Clan clan, out string clanError))
                return clanError;

            Hero deadLeader = clan.Leader;
            if (deadLeader == null)
                return $"Error: {clan.Name} has no current leader to plan inheritance from.";

            FeudalInheritancePlan plan = FeudalInheritancePlanner.BuildPlan(clan, deadLeader, reservedPersonalHoldings);
            return FormatInheritancePlan(plan, reservedPersonalHoldings);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("formable_titles", "civilwars")]
        public static string FormableTitles(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.formable_titles [county/duchy/kingdom/empire] [optional Clan Name]";

            if (!TryParseFeudalTitleType(args[0], out FeudalTitleType targetType, out string typeError))
                return typeError;

            Clan clan = Clan.PlayerClan;
            if (args.Count > 1)
            {
                string clanQuery = string.Join(" ", args.Skip(1)).Trim();
                if (!TryResolveClanAny(clanQuery, out clan, out string clanError))
                    return clanError;
            }

            FeudalTitleBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (behavior == null)
                return "Error: Could not find FeudalTitleBehavior.";

            List<FeudalTitleFormationCandidate> candidates = behavior.GetFormableTitles(clan, targetType).ToList();
            if (candidates.Count == 0)
                return $"{clan.Name} has no formable {targetType.ToString().ToLowerInvariant()} titles.";

            return string.Join("\n", candidates
                .OrderByDescending(c => c.ChildTitleIds.Count)
                .ThenByDescending(c => c.ProsperityScore)
                .Take(8)
                .Select(c => $"{c.Name}: children={c.ChildTitleIds.Count}; legal_seed={c.DeJureHeldChildren}/{c.RequiredDeJureChildren}; cost={c.GoldCost}; influence={c.InfluenceReward:0}; capital={c.CapitalSettlementId}; child_titles={string.Join(",", c.ChildTitleIds)}"));
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("form_title", "civilwars")]
        public static string FormTitle(List<string> args)
        {
            if (args == null || args.Count == 0)
                return "Usage: civilwars.form_title [county/duchy/kingdom/empire] [optional Clan Name] [free]";

            if (!TryParseFeudalTitleType(args[0], out FeudalTitleType targetType, out string typeError))
                return typeError;

            bool free = args.Any(arg => string.Equals(arg, "free", StringComparison.OrdinalIgnoreCase));
            List<string> clanArgs = args
                .Skip(1)
                .Where(arg => !string.Equals(arg, "free", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Clan clan = Clan.PlayerClan;
            if (clanArgs.Count > 0)
            {
                string clanQuery = string.Join(" ", clanArgs).Trim();
                if (!TryResolveClanAny(clanQuery, out clan, out string clanError))
                    return clanError;
            }

            FeudalTitleBehavior behavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (behavior == null)
                return "Error: Could not find FeudalTitleBehavior.";

            bool chargeCost = !free;
            if (!behavior.TryFormTitle(clan, targetType, chargeCost, out FeudalTitleRecord title, out string reason))
                return $"Error: could not form {targetType.ToString().ToLowerInvariant()} for {clan.Name}: {reason}.";

            return $"Success! {clan.Name} formed {title.Name} ({title.TitleId}). de_jure={title.DeJureHolderClanId}; children now report this title as their parent. Cost charged: {(chargeCost ? "yes" : "no")}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("start", "civilwars")]
        public static string StartRebellion(List<string> args)
        {
            FactionObject testFaction = CreateFactionFromArgs(args, out string error);
            if (error != null) return error;
            
            testFaction.TriggerUltimatum();

            return $"Success! Forced {testFaction.Members.Count} clans in {testFaction.ParentKingdom.Name} to start a {testFaction.Type} rebellion.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("end", "civilwars")]
        public static string EndRebellion(List<string> args)
        {
            if (args.Count < 2)
                return "Usage: civilwars.end [Original Kingdom Name] [loyalist/rebel]";

            string winnerSide = args.Last().ToLower();
            string kingdomName = string.Join(" ", args.Take(args.Count - 1));

            Kingdom originalKingdom = Kingdom.All.FirstOrDefault(k => k.Name.ToString().Equals(kingdomName, StringComparison.OrdinalIgnoreCase) || k.StringId.Equals(kingdomName, StringComparison.OrdinalIgnoreCase));
            if (originalKingdom == null) return $"Error: Could not find original kingdom '{kingdomName}'.";

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return "Error: Could not find FactionManagerBehavior.";

            FactionObject activeFaction = FindActiveRebellionForOriginalKingdom(originalKingdom, factionManager, out Kingdom rebelKingdom, out string lookupDiagnostics);
            if (activeFaction == null || rebelKingdom == null)
                return $"Error: Could not locate a tracked active rebellion for '{originalKingdom.Name}'. {lookupDiagnostics}";

            var resolutionBehavior = Campaign.Current.GetCampaignBehavior<CivilWarResolutionBehavior>();
            if (resolutionBehavior == null) return "Error: Could not find CivilWarResolutionBehavior.";

            if (winnerSide == "loyalist")
            {
                resolutionBehavior.ResolveLiegeVictory(activeFaction, rebelKingdom);
                return $"Success! {originalKingdom.Name} crushed the rebellion. Check the map for executions and confiscations!";
            }
            else if (winnerSide == "rebel")
            {
                resolutionBehavior.ResolveRebelVictory(activeFaction, rebelKingdom);
                return $"Success! The rebels defeated {originalKingdom.Name}. Changes have been applied!";
            }

            return "Error: Winner must be 'loyalist' or 'rebel'.";
        }

        private static FactionObject FindActiveRebellionForOriginalKingdom(Kingdom originalKingdom, FactionManagerBehavior factionManager, out Kingdom rebelKingdom, out string diagnostics)
        {
            rebelKingdom = null;
            diagnostics = string.Empty;

            if (originalKingdom == null || factionManager == null)
            {
                diagnostics = "Missing original kingdom or faction manager.";
                return null;
            }

            List<FactionObject> trackedFactions = factionManager.GetFactionsInKingdom(originalKingdom)
                .Where(f => f != null && !f.IsIdeology)
                .ToList();

            List<Tuple<FactionObject, Kingdom>> activeTracked = trackedFactions
                .Select(f => Tuple.Create(f, f.GetRebelKingdom()))
                .Where(pair => pair.Item2 != null
                    && !pair.Item2.IsEliminated
                    && pair.Item2 != originalKingdom
                    && pair.Item2.IsAtWarWith(originalKingdom))
                .ToList();

            if (activeTracked.Count == 1)
            {
                rebelKingdom = activeTracked[0].Item2;
                return activeTracked[0].Item1;
            }

            if (activeTracked.Count > 1)
            {
                Tuple<FactionObject, Kingdom> strongest = activeTracked
                    .OrderByDescending(pair => pair.Item1.CalculateFactionPower())
                    .First();

                rebelKingdom = strongest.Item2;
                diagnostics = $"Multiple tracked rebellions found; selected strongest: {strongest.Item1.Name} ({strongest.Item2.StringId}).";
                return strongest.Item1;
            }

            List<Kingdom> prefixMatches = Kingdom.All
                .Where(k => k != null
                    && !k.IsEliminated
                    && k != originalKingdom
                    && k.StringId.StartsWith(originalKingdom.StringId + "_rebels", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (Kingdom candidate in prefixMatches.Where(k => k.IsAtWarWith(originalKingdom)))
            {
                FactionObject faction = factionManager.GetFactionByRebelKingdom(candidate);
                if (faction != null && faction.ParentKingdom == originalKingdom)
                {
                    rebelKingdom = candidate;
                    diagnostics = $"Resolved through rebel-kingdom fallback: {candidate.StringId}.";
                    return faction;
                }
            }

            diagnostics = BuildRebellionLookupDiagnostics(originalKingdom, trackedFactions, prefixMatches);
            return null;
        }

        private static string BuildRebellionLookupDiagnostics(Kingdom originalKingdom, List<FactionObject> trackedFactions, List<Kingdom> prefixMatches)
        {
            string trackedText = trackedFactions.Count == 0
                ? "no tracked rebel factions"
                : string.Join(", ", trackedFactions.Select(f =>
                {
                    Kingdom rk = f.GetRebelKingdom();
                    return $"{f.Name}[type={f.Type}, rebel={rk?.StringId ?? "null"}, active={f.IsCivilWarActive()}]";
                }));

            string prefixText = prefixMatches.Count == 0
                ? "no _rebels kingdoms"
                : string.Join(", ", prefixMatches.Select(k => $"{k.StringId}[war={k.IsAtWarWith(originalKingdom)}, ruler={k.RulingClan?.StringId ?? "null"}]"));

            return $"Tracked: {trackedText}. Prefix matches: {prefixText}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("set_ruler_relation_player", "civilwars")]
        public static string SetRulerRelationPlayer(List<string> args)
        {
            if (args.Count != 1 || !int.TryParse(args[0], out int value))
                return "Usage: civilwars.set_ruler_relation_player [Value]";

            if (Clan.PlayerClan.Kingdom == null || Clan.PlayerClan.Kingdom.RulingClan == Clan.PlayerClan)
                return "Error: You are not a vassal of a kingdom.";

            Hero ruler = Clan.PlayerClan.Kingdom.RulingClan.Leader;
            int currentRelation = Clan.PlayerClan.Leader.GetRelation(ruler);
            int difference = value - currentRelation;

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Clan.PlayerClan.Leader, ruler, difference, true);

            return $"Success! Set relationship between {Clan.PlayerClan.Name} and {ruler.Name} to {value}. Wait for the next daily tick to trigger potential treason events!";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("set_ruler_relation", "civilwars")]
        public static string SetRulerRelationAI(List<string> args)
        {
            if (args.Count < 2)
                return "Usage: civilwars.set_ruler_relation [Clan Name] [Value]";

            if (!int.TryParse(args.Last(), out int value))
                return "Error: The last argument must be a numerical relationship value.";

            string clanName = string.Join(" ", args.Take(args.Count - 1));

            Clan targetClan = Clan.All.FirstOrDefault(c => c.Name.ToString().Equals(clanName, StringComparison.OrdinalIgnoreCase));
            if (targetClan == null) return $"Error: Could not find a clan named '{clanName}'.";

            if (targetClan.Kingdom == null || targetClan.Kingdom.RulingClan == targetClan)
                return $"Error: {targetClan.Name} is not a vassal of any kingdom.";

            Hero ruler = targetClan.Kingdom.RulingClan.Leader;
            int currentRelation = targetClan.Leader.GetRelation(ruler);
            int difference = value - currentRelation;

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(targetClan.Leader, ruler, difference, true);

            return $"Success! Set relationship between {targetClan.Name} and {ruler.Name} to {value}. Wait for the next daily tick to trigger potential treason events!";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("force_treason", "civilwars")]
        public static string ForceTreason(List<string> args)
        {
            if (args == null || args.Count < 4)
                return "Usage: civilwars.force_treason [vote/decree] [Kingdom Name] | [Clan or Leader Name]\nExample: civilwars.force_treason decree Vlandia | dey Meroc";

            string mode = args[0].ToLowerInvariant();
            bool royalDecree;
            if (mode == "vote" || mode == "indict" || mode == "indictment")
                royalDecree = false;
            else if (mode == "decree" || mode == "purge" || mode == "expel")
                royalDecree = true;
            else
                return "Error: first argument must be 'vote' or 'decree'.";

            int separatorIndex = args.FindIndex(arg => arg == "|");
            if (separatorIndex <= 1 || separatorIndex >= args.Count - 1)
                return "Error: separate the kingdom and target with '|'. Example: civilwars.force_treason vote Western Empire | Garios";

            string kingdomQuery = string.Join(" ", args.Skip(1).Take(separatorIndex - 1)).Trim();
            string targetQuery = string.Join(" ", args.Skip(separatorIndex + 1)).Trim();
            if (string.IsNullOrWhiteSpace(kingdomQuery) || string.IsNullOrWhiteSpace(targetQuery))
                return "Error: both kingdom and target clan/leader must be provided.";

            if (!TryResolveKingdom(kingdomQuery, out Kingdom kingdom, out string kingdomError))
                return kingdomError;

            if (!TryResolveClanInKingdom(kingdom, targetQuery, out Clan targetClan, out string clanError))
                return clanError;

            var ideologyBehavior = Campaign.Current.GetCampaignBehavior<IdeologyBehavior>();
            if (ideologyBehavior == null)
                return "Error: Could not find IdeologyBehavior.";

            string error = ideologyBehavior.ForceTreasonJudgmentForTesting(kingdom, targetClan, royalDecree);
            if (error != null)
                return $"Error: {error}.";

            string action = royalDecree ? "royal decree" : "treason indictment vote";
            return $"Success! Forced {action} in {kingdom.Name} against {targetClan.Leader.Name} of {targetClan.Name}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("set_ideology_mood", "civilwars")]
        public static string SetIdeologyMood(List<string> args)
        {
            if (args.Count < 3)
                return "Usage: civilwars.set_ideology_mood [Kingdom Name] [FactionType] [Value]\nTypes: Traditionalists, Militarists, Aristocrats, Populists";

            if (!float.TryParse(args.Last(), out float value))
                return "Error: The last argument must be a numerical value.";

            string factionTypeStr = args[args.Count - 2];
            if (!TryParseCourtFaction(factionTypeStr, out FactionType type))
                return $"Error: '{factionTypeStr}' is not a valid FactionType.";

            string kingdomName = string.Join(" ", args.Take(args.Count - 2));
            Kingdom targetKingdom = Kingdom.All.FirstOrDefault(k => k.Name.ToString().Equals(kingdomName, StringComparison.OrdinalIgnoreCase) || k.StringId.Equals(kingdomName, StringComparison.OrdinalIgnoreCase));
            
            if (targetKingdom == null) return $"Error: Could not find kingdom '{kingdomName}'.";

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            if (factionManager == null) return "Error: Could not find FactionManagerBehavior.";

            FactionObject targetFaction = factionManager.GetFactionsInKingdom(targetKingdom).FirstOrDefault(f => f.IsIdeology && f.Type == type);
            
            if (targetFaction == null) return $"Error: {targetKingdom.Name} does not currently have an active {type} faction.";

            targetFaction.Mood = TaleWorlds.Library.MathF.Clamp(value, -100f, 100f);

            return $"Success! Set the mood of the {type} in {targetKingdom.Name} to {targetFaction.Mood}.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("trigger_coalition", "civilwars")]
        public static string TriggerCoalition(List<string> args)
        {
            if (args.Count < 2)
                return "Usage: civilwars.trigger_coalition [Kingdom Name] [IdeologyType]\nTypes: Traditionalists, Militarists, Aristocrats, Populists";

            string ideologyTypeStr = args.Last();
            if (!TryParseCourtFaction(ideologyTypeStr, out FactionType type) ||
                !(type == FactionType.Royalists || type == FactionType.Glory ||
                  type == FactionType.Nobility || type == FactionType.Liberty))
                return $"Error: '{ideologyTypeStr}' is not a valid ideology type. Use: Traditionalists, Militarists, Aristocrats, Populists";

            string kingdomName = string.Join(" ", args.Take(args.Count - 1));
            Kingdom targetKingdom = Kingdom.All.FirstOrDefault(k =>
                k.Name.ToString().Equals(kingdomName, StringComparison.OrdinalIgnoreCase) ||
                k.StringId.Equals(kingdomName, StringComparison.OrdinalIgnoreCase));

            if (targetKingdom == null) return $"Error: Could not find kingdom '{kingdomName}'.";

            var ideologyBehavior = Campaign.Current.GetCampaignBehavior<IdeologyBehavior>();
            if (ideologyBehavior == null) return "Error: Could not find IdeologyBehavior.";

            string error = ideologyBehavior.ForceGrandCoalition(targetKingdom, type);
            if (error != null) return $"Error: {error}.";

            string actionName = "Grand Coalition";
            return $"Success! Triggered {type} {actionName} in {targetKingdom.Name}. The rebellion has been issued its ultimatum.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("force_faction_meeting", "civilwars")]
        public static string ForceFactionMeeting(List<string> args)
        {
            if (args.Count < 1)
                return "Usage: civilwars.force_faction_meeting [FactionType]\nTypes: Traditionalists, Militarists, Aristocrats, Populists";

            string factionTypeStr = args[0];
            if (!TryParseCourtFaction(factionTypeStr, out FactionType type) ||
                !(type == FactionType.Royalists || type == FactionType.Glory ||
                  type == FactionType.Nobility || type == FactionType.Liberty))
                return $"Error: '{factionTypeStr}' is not a valid ideology type. Use: Traditionalists, Militarists, Aristocrats, Populists";

            Kingdom kingdom = Clan.PlayerClan.Kingdom;
            if (kingdom == null)
                return "Error: You are not part of a kingdom.";

            var ideologyBehavior = Campaign.Current.GetCampaignBehavior<IdeologyBehavior>();
            if (ideologyBehavior == null) return "Error: Could not find IdeologyBehavior.";

            string error = ideologyBehavior.ForceFactionMeeting(kingdom, type);
            if (error != null) return $"Error: {error}";

            return $"Success! Forced a {type} faction meeting in {kingdom.Name}. Check the message log for meeting output.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("fund_highwaymen", "civilwars")]
        public static string FundHighwaymen(List<string> args)
        {
            if (args.Count < 1)
                return "Usage: civilwars.fund_highwaymen [Kingdom Name]";

            string kingdomName = string.Join(" ", args);
            Kingdom targetKingdom = Kingdom.All.FirstOrDefault(k =>
                k.Name.ToString().Equals(kingdomName, StringComparison.OrdinalIgnoreCase) ||
                k.StringId.Equals(kingdomName, StringComparison.OrdinalIgnoreCase));

            if (targetKingdom == null) return $"Error: Could not find kingdom '{kingdomName}'.";

            var proxyWar = Campaign.Current.GetCampaignBehavior<ProxyWarBehavior>();
            if (proxyWar == null) return "Error: Could not find ProxyWarBehavior.";

            var (spawned, failReason) = proxyWar.ForceFundHighwaymen(targetKingdom);

            if (failReason != null) return $"Error: {failReason}.";

            return $"Success! Spawned {spawned}/3 highwayman band(s) near {targetKingdom.Name}'s capital. No cost or discovery roll applied.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("marry", "civilwars")]
        public static string MarryHeroes(List<string> args)
        {
            if (!TrySplitHeroArguments(args, out string firstHeroQuery, out string secondHeroQuery, out string usageError))
                return usageError;

            if (!TryResolveHero(firstHeroQuery, out Hero firstHero, out string firstHeroError))
                return firstHeroError;

            if (!TryResolveHero(secondHeroQuery, out Hero secondHero, out string secondHeroError))
                return secondHeroError;

            if (firstHero == secondHero)
                return "Error: You must choose two different heroes.";

            string validationError = ValidateMarriageTargets(firstHero, secondHero);
            if (validationError != null)
                return validationError;

            if (!TryApplyMarriage(firstHero, secondHero, out string marriageError))
                return marriageError;

            string firstClan = firstHero.Clan?.Name?.ToString() ?? "no clan";
            string secondClan = secondHero.Clan?.Name?.ToString() ?? "no clan";
            return $"Success! Married {firstHero.Name} and {secondHero.Name}. Current clans: {firstHero.Name} -> {firstClan}, {secondHero.Name} -> {secondClan}. Wait until the next hourly tick for queued cadet-branch and dynastic follow-up logic to resolve.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("force_marriage_offer", "civilwars")]
        public static string ForceMarriageOffer(List<string> args)
        {
            bool? matrilineal = null;
            if (args?.Count > 0)
            {
                if (args.Count != 1)
                    return "Usage: civilwars.force_marriage_offer [matrilineal/patrilineal]. Omit the mode to allow either arrangement. Only one offer can be active at a time.";
                if (string.Equals(args[0], "matrilineal", StringComparison.OrdinalIgnoreCase)) matrilineal = true;
                else if (string.Equals(args[0], "patrilineal", StringComparison.OrdinalIgnoreCase)) matrilineal = false;
                else return "Usage: civilwars.force_marriage_offer [matrilineal/patrilineal]. Omit the mode to allow either arrangement.";
            }
            if (Campaign.Current == null || Clan.PlayerClan == null)
                return "Error: Load a campaign with a player clan first.";
            if (!BellumCivileOptions.EnableBellumStrategicMarriageLogic)
                return "Error: Enable Bellum's strategic marriage logic before testing household offers.";

            MarriageOfferCampaignBehavior offerBehavior = Campaign.Current?.CampaignBehaviorManager?.GetBehavior<MarriageOfferCampaignBehavior>();
            var agreements = PlayerMarriageAgreementBehavior.Instance;
            if (offerBehavior == null || agreements == null)
                return "Error: Marriage offer services are not available in this campaign.";
            if (PlayerMarriageAgreementBehavior.ActivePlayer != null || PlayerMarriageAgreementBehavior.ActiveOther != null)
                return "Error: A marriage offer is already active. Accept or decline it before requesting another.";
            if (Clan.PlayerClan.Kingdom == null || Clan.PlayerClan.Kingdom.IsEliminated)
                return "Error: The player clan must belong to an active kingdom to qualify for strategic marriage offers.";

            FactionManagerBehavior factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            List<BellumMarriageMatch> matches = BellumMarriageStrategyHelper.FindPlayerClanTestOffers(
                factionManager, matrilineal, out string diagnostics);

            if (matches.Count == 0)
                return $"Error: No eligible {(matrilineal.HasValue ? matrilineal.Value ? "matrilineal" : "patrilineal" : "marriage")} offer found. "
                    + $"Age, availability, reservations, NPC acceptance and household protections remain in effect. Diagnostics: {diagnostics}.";
            // Give each eligible offering house an equal chance, then choose a pair within that house.
            var houses = matches.GroupBy(m => m.Suitor.Clan).Select(g => g.ToList()).ToList();
            List<BellumMarriageMatch> pool = houses[MBRandom.RandomInt(houses.Count)];
            BellumMarriageMatch selected = pool[MBRandom.RandomInt(pool.Count)];

            Hero playerClanHero = selected.Suitor.Clan == Clan.PlayerClan ? selected.Suitor : selected.Candidate;
            Hero otherClanHero = selected.Suitor.Clan == Clan.PlayerClan ? selected.Candidate : selected.Suitor;
            if (playerClanHero?.Clan != Clan.PlayerClan || otherClanHero?.Clan == Clan.PlayerClan)
                return "Error: Selected match did not resolve to one player-clan hero and one outside hero.";

            if (selected.Outcome?.StillMatches() != true
                || StrategicMarriageBehavior.HasMarriageOfferFor(playerClanHero) || StrategicMarriageBehavior.HasMarriageOfferFor(otherClanHero)
                || !Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(playerClanHero, otherClanHero))
                return $"Error: Selected match no longer meets the agreed household or eligibility requirements: {playerClanHero.Name} + {otherClanHero.Name}.";

            offerBehavior.CreateMarriageOffer(playerClanHero, otherClanHero);
            var agreement = agreements.Active;
            if (agreement == null || !agreement.Matches(playerClanHero, otherClanHero))
                return "Error: The marriage offer was not registered. No wedding has been performed.";
            return $"Success! {agreement.FormText()} offer sent: {playerClanHero.Name} ({playerClanHero.StringId}) of {playerClanHero.Clan.Name} "
                + $"and {otherClanHero.Name} ({otherClanHero.StringId}) of {otherClanHero.Clan.Name}. "
                + $"{agreement.HouseholdText()} NPC acceptance={selected.Score:0}. "
                + "Close the console and open the marriage-offer notification. Accepting performs a real marriage; use a test save.";
        }

        private static FactionObject CreateFactionFromArgs(List<string> args, out string errorMessage)
        {
            errorMessage = null;
            if (args.Count < 2)
            {
                errorMessage = "Usage: civilwars.create_faction [Kingdom Name] [FactionType]\nTypes: Independence, Abdication, InstallRuler, Traditionalists, Militarists, Aristocrats, Populists";
                return null;
            }

            string factionTypeStr = args.Last();
            string kingdomName = string.Join(" ", args.Take(args.Count - 1));

            Kingdom targetKingdom = Kingdom.All.FirstOrDefault(k => k.Name.ToString().Equals(kingdomName, StringComparison.OrdinalIgnoreCase) || k.StringId.Equals(kingdomName, StringComparison.OrdinalIgnoreCase));
            if (targetKingdom == null) { errorMessage = $"Error: Could not find kingdom '{kingdomName}'."; return null; }

            if (!TryParseCourtFaction(factionTypeStr, out FactionType type))
            {
                errorMessage = $"Error: '{factionTypeStr}' is not a valid FactionType.";
                return null;
            }

            List<Clan> eligibleClans = targetKingdom.Clans.Where(c => c != targetKingdom.RulingClan && !c.IsUnderMercenaryService && !c.IsMinorFaction && c != Clan.PlayerClan).ToList();
            if (eligibleClans.Count == 0) { errorMessage = $"Error: {targetKingdom.Name} has no eligible vassal clans."; return null; }

            eligibleClans = eligibleClans.OrderBy(c => MBRandom.RandomInt()).ToList();
            Clan leader = eligibleClans.First();
            eligibleClans.Remove(leader);

            string factionName = $"{leader.Name} Revolt";
            switch (type)
            {
                case FactionType.Independence: factionName = $"{leader.Name} Secessionists"; break;
                case FactionType.Abdication: factionName = $"{leader.Name} Coalition"; break;
                case FactionType.InstallRuler: factionName = $"{leader.Name} Claimants"; break;
            }

            FactionObject newFaction = new FactionObject(factionName, targetKingdom, leader, type);
            int membersToAdd = Math.Min(2, eligibleClans.Count);
            for (int i = 0; i < membersToAdd; i++) newFaction.AddMember(eligibleClans[i]);

            var factionManager = Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>();
            factionManager?.RegisterNewFaction(newFaction);
            return newFaction;
        }

        private static bool TrySplitHeroArguments(List<string> args, out string firstHeroQuery, out string secondHeroQuery, out string error)
        {
            firstHeroQuery = null;
            secondHeroQuery = null;
            error = null;

            if (args == null || args.Count < 3)
            {
                error = "Usage: civilwars.marry [Hero One] | [Hero Two]\nExample: civilwars.marry Ira | Pharon";
                return false;
            }

            int separatorIndex = args.FindIndex(arg => arg == "|");
            if (separatorIndex <= 0 || separatorIndex >= args.Count - 1)
            {
                error = "Error: Separate the two hero names with '|'. Example: civilwars.marry Ira | Pharon";
                return false;
            }

            firstHeroQuery = string.Join(" ", args.Take(separatorIndex)).Trim();
            secondHeroQuery = string.Join(" ", args.Skip(separatorIndex + 1)).Trim();

            if (string.IsNullOrWhiteSpace(firstHeroQuery) || string.IsNullOrWhiteSpace(secondHeroQuery))
            {
                error = "Error: Both hero names must be provided. Example: civilwars.marry Ira | Pharon";
                return false;
            }

            return true;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("pending_votes", "civilwars")]
        public static string PendingVotes(List<string> args)
        {
            if (!TryResolveVoteCommandKingdom(args, out Kingdom kingdom, out string error))
                return error;

            List<string> diagnostics = CollectVoteDiagnostics(kingdom);
            if (diagnostics.Count == 0)
                return $"No delayed or live fief, policy, or expulsion votes are registered for {kingdom.Name} ({kingdom.StringId}).";

            return $"Vote diagnostics for {kingdom.Name} ({kingdom.StringId}):\n"
                + string.Join("\n", diagnostics);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("repair_pending_votes", "civilwars")]
        public static string RepairPendingVotes(List<string> args)
        {
            if (!TryResolveVoteCommandKingdom(args, out Kingdom kingdom, out string error))
                return error;

            FiefDeliberationBehavior fief = Campaign.Current?.GetCampaignBehavior<FiefDeliberationBehavior>();
            PolicyDeliberationBehavior policy = Campaign.Current?.GetCampaignBehavior<PolicyDeliberationBehavior>();
            ExpulsionDeliberationBehavior expulsion = Campaign.Current?.GetCampaignBehavior<ExpulsionDeliberationBehavior>();

            int changes = 0;
            changes += fief?.RunReliabilityRepair(kingdom) ?? 0;
            changes += policy?.RunReliabilityRepair(kingdom) ?? 0;
            changes += expulsion?.RunReliabilityRepair(kingdom) ?? 0;

            List<string> diagnostics = CollectVoteDiagnostics(kingdom);
            string remaining = diagnostics.Count == 0
                ? "No delayed or live votes remain."
                : "Current state:\n" + string.Join("\n", diagnostics);
            return $"Reconciled delayed votes for {kingdom.Name}; repaired or removed {changes} stale/orphaned entries. Expired pending entries receive their final recovery attempt on the next daily tick.\n{remaining}";
        }

        private static bool TryResolveVoteCommandKingdom(List<string> args, out Kingdom kingdom, out string error)
        {
            kingdom = null;
            error = null;

            string query = args == null ? string.Empty : string.Join(" ", args).Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                kingdom = Clan.PlayerClan?.Kingdom;
                if (kingdom == null)
                {
                    error = "Error: provide a kingdom name or join a kingdom first.";
                    return false;
                }

                return true;
            }

            return TryResolveKingdom(query, out kingdom, out error);
        }

        private static List<string> CollectVoteDiagnostics(Kingdom kingdom)
        {
            List<string> diagnostics = new List<string>();
            FiefDeliberationBehavior fief = Campaign.Current?.GetCampaignBehavior<FiefDeliberationBehavior>();
            PolicyDeliberationBehavior policy = Campaign.Current?.GetCampaignBehavior<PolicyDeliberationBehavior>();
            ExpulsionDeliberationBehavior expulsion = Campaign.Current?.GetCampaignBehavior<ExpulsionDeliberationBehavior>();

            if (fief != null)
                diagnostics.AddRange(fief.GetReliabilityDiagnostics(kingdom));
            if (policy != null)
                diagnostics.AddRange(policy.GetReliabilityDiagnostics(kingdom));
            if (expulsion != null)
                diagnostics.AddRange(expulsion.GetReliabilityDiagnostics(kingdom));

            return diagnostics;
        }

        private static bool TryResolveKingdom(string query, out Kingdom kingdom, out string error)
        {
            kingdom = null;
            error = null;

            if (string.IsNullOrWhiteSpace(query))
            {
                error = "Error: kingdom query cannot be empty.";
                return false;
            }

            string trimmedQuery = query.Trim();
            kingdom = Kingdom.All.FirstOrDefault(k =>
                k != null &&
                (k.StringId.Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                 || k.Name.ToString().Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase)));

            if (kingdom != null)
                return true;

            List<Kingdom> partialMatches = Kingdom.All
                .Where(k => k != null
                    && !k.IsEliminated
                    && (k.Name.ToString().IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0
                        || k.StringId.IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0))
                .Take(6)
                .ToList();

            if (partialMatches.Count == 1)
            {
                kingdom = partialMatches[0];
                return true;
            }

            if (partialMatches.Count > 1)
            {
                string suggestions = string.Join(", ", partialMatches.Select(k => $"{k.Name} ({k.StringId})"));
                error = $"Error: kingdom query '{trimmedQuery}' is ambiguous. Matches: {suggestions}.";
                return false;
            }

            error = $"Error: could not find kingdom '{trimmedQuery}'.";
            return false;
        }

        private static bool TryResolveClanInKingdom(Kingdom kingdom, string query, out Clan clan, out string error)
        {
            clan = null;
            error = null;

            if (kingdom == null)
            {
                error = "Error: kingdom is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                error = "Error: target query cannot be empty.";
                return false;
            }

            string trimmedQuery = query.Trim();
            List<Clan> clans = kingdom.Clans
                .Where(c => c != null)
                .ToList();

            clan = clans.FirstOrDefault(c =>
                c.StringId.Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                || c.Name.ToString().Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                || (c.Leader != null && (c.Leader.StringId.Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                    || c.Leader.Name.ToString().Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase))));

            if (clan != null)
                return true;

            List<Clan> partialMatches = clans
                .Where(c =>
                    c.StringId.IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0
                    || c.Name.ToString().IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0
                    || (c.Leader != null && (c.Leader.StringId.IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0
                        || c.Leader.Name.ToString().IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0)))
                .Take(8)
                .ToList();

            if (partialMatches.Count == 1)
            {
                clan = partialMatches[0];
                return true;
            }

            if (partialMatches.Count > 1)
            {
                string suggestions = string.Join(", ", partialMatches.Select(c =>
                    $"{c.Leader?.Name?.ToString() ?? "no leader"} of {c.Name} ({c.StringId})"));
                error = $"Error: target query '{trimmedQuery}' is ambiguous in {kingdom.Name}. Matches: {suggestions}.";
                return false;
            }

            error = $"Error: could not find clan or living leader '{trimmedQuery}' in {kingdom.Name}.";
            return false;
        }

        private static FeudalTitleRecord ResolveFeudalTitleForDebug(FeudalTitleBehavior behavior, string query)
        {
            if (behavior == null || string.IsNullOrWhiteSpace(query))
                return null;

            string trimmedQuery = query.Trim();
            FeudalTitleRecord title = behavior.GetTitle(trimmedQuery);
            if (title != null)
                return title;

            if (TryResolveFeudalTitleForGrant(behavior, trimmedQuery, out title, out _))
                return title;

            List<FeudalTitleRecord> inactiveMatches = behavior.GetAllTitles()
                .Where(candidate => candidate != null
                    && !candidate.IsActive
                    && GetTitleGrantSearchValues(candidate)
                        .Any(value => string.Equals(value, trimmedQuery, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(candidate => candidate.TitleId)
                .Select(group => group.First())
                .ToList();
            if (inactiveMatches.Count == 1)
                return inactiveMatches[0];

            Settlement settlement = Settlement.All.FirstOrDefault(s =>
                s != null
                && (s.StringId.Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                    || s.Name?.ToString().Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase) == true));

            if (settlement != null && behavior.TryGetBarony(settlement, out title))
                return title;

            Kingdom kingdom = Kingdom.All.FirstOrDefault(k =>
                k != null
                && (k.StringId.Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                    || k.Name?.ToString().Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase) == true));

            return kingdom != null
                ? behavior.GetTitle(FeudalTitleBehavior.BuildKingdomTitleId(kingdom))
                : null;
        }

        private static bool TryParseTitleGrantArguments(List<string> args, out string titleQuery, out bool transferDeJure)
        {
            titleQuery = null;
            transferDeJure = false;
            if (args == null || args.Count < 2)
                return false;

            int titleArgumentCount = args.Count;
            string last = args[args.Count - 1]?.Trim() ?? string.Empty;
            if (string.Equals(last, "full", StringComparison.OrdinalIgnoreCase))
            {
                transferDeJure = true;
                titleArgumentCount--;
            }
            else if (string.Equals(last, "defacto", StringComparison.OrdinalIgnoreCase)
                || string.Equals(last, "de_facto", StringComparison.OrdinalIgnoreCase)
                || string.Equals(last, "de-facto", StringComparison.OrdinalIgnoreCase))
            {
                titleArgumentCount--;
            }
            else if (args.Count >= 3
                && string.Equals(args[args.Count - 2], "de", StringComparison.OrdinalIgnoreCase)
                && string.Equals(last, "facto", StringComparison.OrdinalIgnoreCase))
            {
                titleArgumentCount -= 2;
            }
            else
            {
                return false;
            }

            titleQuery = string.Join(" ", args.Take(titleArgumentCount)).Trim();
            return !string.IsNullOrWhiteSpace(titleQuery);
        }

        private static bool TryResolveFeudalTitleForGrant(
            FeudalTitleBehavior behavior,
            string query,
            out FeudalTitleRecord title,
            out string error)
        {
            title = null;
            error = null;
            if (behavior == null || string.IsNullOrWhiteSpace(query))
            {
                error = "Error: the title query cannot be empty.";
                return false;
            }

            string trimmedQuery = query.Trim();
            List<FeudalTitleRecord> activeTitles = behavior.GetAllTitles()
                .Where(candidate => candidate != null && candidate.IsActive)
                .ToList();

            List<FeudalTitleRecord> exactMatches = activeTitles
                .Where(candidate => GetTitleGrantSearchValues(candidate)
                    .Any(value => string.Equals(value, trimmedQuery, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(candidate => candidate.TitleId)
                .Select(group => group.First())
                .ToList();

            if (exactMatches.Count == 1)
            {
                title = exactMatches[0];
                return true;
            }

            if (exactMatches.Count > 1)
            {
                error = BuildAmbiguousTitleGrantError(trimmedQuery, exactMatches);
                return false;
            }

            List<FeudalTitleRecord> partialMatches = activeTitles
                .Where(candidate => GetTitleGrantSearchValues(candidate)
                    .Any(value => !string.IsNullOrWhiteSpace(value)
                        && value.IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0))
                .GroupBy(candidate => candidate.TitleId)
                .Select(group => group.First())
                .Take(10)
                .ToList();

            if (partialMatches.Count == 1)
            {
                title = partialMatches[0];
                return true;
            }

            if (partialMatches.Count > 1)
            {
                error = BuildAmbiguousTitleGrantError(trimmedQuery, partialMatches);
                return false;
            }

            error = $"Error: could not find an active feudal title matching '{trimmedQuery}'. Use civilwars.titles_summary or civilwars.title_info to inspect the hierarchy.";
            return false;
        }

        private static IEnumerable<string> GetTitleGrantSearchValues(FeudalTitleRecord title)
        {
            if (title == null)
                yield break;

            yield return title.TitleId;
            yield return title.Name;
            yield return FeudalTitleDisplayHelper.FormatTitleName(title);
            yield return FeudalTitleDisplayHelper.FormatTitleName(title, null);

            Clan deJureHolder = Clan.All.FirstOrDefault(candidate => candidate != null
                && string.Equals(candidate.StringId, title.DeJureHolderClanId, StringComparison.Ordinal));
            if (deJureHolder != null)
                yield return FeudalTitleDisplayHelper.FormatTitleName(title, deJureHolder);

            if (string.IsNullOrWhiteSpace(title.CapitalSettlementId))
                yield break;

            yield return title.CapitalSettlementId;
            Settlement settlement = Settlement.All.FirstOrDefault(candidate =>
                candidate != null
                && string.Equals(candidate.StringId, title.CapitalSettlementId, StringComparison.OrdinalIgnoreCase));
            if (settlement?.Name != null)
                yield return settlement.Name.ToString();
        }

        private static string BuildAmbiguousTitleGrantError(string query, IEnumerable<FeudalTitleRecord> matches)
        {
            string suggestions = string.Join(", ", matches.Select(match =>
                $"{FeudalTitleDisplayHelper.FormatTitleName(match)} ({match.TitleId})"));
            return $"Error: title query '{query}' is ambiguous. Matches: {suggestions}. Use the title ID to select one exactly.";
        }

        private static string FormatFeudalClaimForDebug(FeudalClaimRecord claim)
        {
            if (claim == null)
                return "- null claim";

            return $"- {claim.Strength.ToString().ToLowerInvariant()}: clan={claim.ClaimantClanId}; title={claim.TargetTitleId}; carrier={claim.CarrierHeroId}; depth={claim.GenerationDepth}; origin={claim.OriginClanId}; source={claim.Source}";
        }

        private static string GetUsurpationControlLabel(FeudalTitleType titleType)
        {
            switch (titleType)
            {
                case FeudalTitleType.Barony:
                    return "barony";
                case FeudalTitleType.County:
                    return "baronies";
                case FeudalTitleType.Duchy:
                    return "counties";
                case FeudalTitleType.Kingdom:
                    return "duchies";
                case FeudalTitleType.Empire:
                    return "kingdoms";
                default:
                    return "none";
            }
        }

        private static string FormatInheritancePlan(FeudalInheritancePlan plan, int reservedPersonalHoldings)
        {
            if (plan == null)
                return "Error: could not build inheritance plan.";

            string clanName = plan.ParentClan?.Name?.ToString() ?? plan.ParentClan?.StringId ?? "none";
            string leaderName = plan.DeadLeader?.Name?.ToString() ?? plan.DeadLeader?.StringId ?? "none";
            string legalHeir = FormatHeroForDebug(plan.LegalClanHeir);
            string successionLaw = SuccessionLawHelper.GetGenderLawName(plan.GenderLaw)
                + " / "
                + SuccessionLawHelper.GetSuccessionLawName(plan.SuccessionLaw);
            string successionScope = SuccessionLawHelper.GetSuccessionRuleScopeName(plan.SuccessionScope).ToString();

            List<string> lines = new List<string>
            {
                $"inheritance_plan clan={clanName} ({plan.ParentClan?.StringId ?? "none"}); leader={leaderName} ({plan.DeadLeader?.StringId ?? "none"}); law={successionLaw}; scope={successionScope}; reserved_personal_holdings={reservedPersonalHoldings}",
                $"legal_clan_heir={legalHeir}",
                $"natural_blood_heirs={plan.NaturalHeirs.Count}: {FormatHeroListForDebug(plan.NaturalHeirs)}",
                $"estate_fiefs={plan.EstateFiefs.Count}: {FormatTownListForDebug(plan.EstateFiefs)}",
                $"estate_titles={plan.EstateTitles.Count}: {FormatTitleListForDebug(plan.EstateTitles)}",
                $"primary_title_chain={plan.PrimaryTitleChain.Count}: {FormatTitleListForDebug(plan.PrimaryTitleChain)}",
                $"reserved_personal_fiefs={plan.ReservedPersonalFiefs.Count}: {FormatTownListForDebug(plan.ReservedPersonalFiefs)}",
                $"secondary_packages={plan.SecondaryPackages.Count}"
            };

            if (!string.IsNullOrWhiteSpace(plan.FailureReason))
                lines.Add($"warning={plan.FailureReason}");

            int index = 1;
            foreach (FeudalInheritancePackage package in plan.SecondaryPackages)
            {
                lines.Add($"package_{index}: root={FormatTitleForDebug(package.RootTitle)}; primary_fief={FormatTownForDebug(package.PrimaryFief)}; fiefs={FormatTownListForDebug(package.Fiefs)}; titles={FormatTitleListForDebug(package.Titles)}; debug={package.DebugName ?? "none"}");
                index++;
            }

            return string.Join("\n", lines);
        }

        private static string FormatHeroForDebug(Hero hero)
        {
            return hero == null
                ? "none"
                : $"{hero.Name} ({hero.StringId})";
        }

        private static string FormatHeroListForDebug(IEnumerable<Hero> heroes)
        {
            List<string> items = heroes?
                .Where(hero => hero != null)
                .Select(FormatHeroForDebug)
                .ToList() ?? new List<string>();

            return items.Count > 0 ? string.Join(", ", items) : "none";
        }

        private static string FormatTownForDebug(Town town)
        {
            return town?.Settlement == null
                ? "none"
                : $"{town.Name} ({town.StringId})";
        }

        private static string FormatTownListForDebug(IEnumerable<Town> towns)
        {
            List<string> items = towns?
                .Where(town => town?.Settlement != null)
                .Select(FormatTownForDebug)
                .ToList() ?? new List<string>();

            return items.Count > 0 ? string.Join(", ", items) : "none";
        }

        private static string FormatTitleForDebug(FeudalTitleRecord title)
        {
            return title == null
                ? "none"
                : $"{title.Name} ({title.TitleId}; {title.TitleType}; de_jure={title.DeJureHolderClanId}; de_facto={title.DeFactoHolderClanId})";
        }

        private static string FormatTitleListForDebug(IEnumerable<FeudalTitleRecord> titles)
        {
            List<string> items = titles?
                .Where(title => title != null)
                .Select(FormatTitleForDebug)
                .ToList() ?? new List<string>();

            return items.Count > 0 ? string.Join(", ", items) : "none";
        }

        private static bool TryResolveClanAny(string query, out Clan clan, out string error)
        {
            clan = null;
            error = null;

            if (string.IsNullOrWhiteSpace(query))
            {
                error = "Error: clan query cannot be empty.";
                return false;
            }

            string trimmedQuery = query.Trim();
            List<Clan> clans = Clan.All
                .Where(c => c != null)
                .ToList();

            clan = clans.FirstOrDefault(c =>
                c.StringId.Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                || c.Name.ToString().Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                || (c.Leader != null && (c.Leader.StringId.Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                    || c.Leader.Name.ToString().Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase))));

            if (clan != null)
                return true;

            List<Clan> partialMatches = clans
                .Where(c =>
                    c.StringId.IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0
                    || c.Name.ToString().IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0
                    || (c.Leader != null && (c.Leader.StringId.IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0
                        || c.Leader.Name.ToString().IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0)))
                .Take(8)
                .ToList();

            if (partialMatches.Count == 1)
            {
                clan = partialMatches[0];
                return true;
            }

            if (partialMatches.Count > 1)
            {
                string suggestions = string.Join(", ", partialMatches.Select(c =>
                    $"{c.Leader?.Name?.ToString() ?? "no leader"} of {c.Name} ({c.StringId})"));
                error = $"Error: clan query '{trimmedQuery}' is ambiguous. Matches: {suggestions}.";
                return false;
            }

            error = $"Error: could not find clan or living leader '{trimmedQuery}'.";
            return false;
        }

        private static bool TryParseFeudalTitleType(string value, out FeudalTitleType titleType, out string error)
        {
            titleType = FeudalTitleType.County;
            error = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Error: title type is missing. Valid types: county, duchy, kingdom, empire.";
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "county":
                case "count":
                    titleType = FeudalTitleType.County;
                    return true;
                case "duchy":
                case "duke":
                    titleType = FeudalTitleType.Duchy;
                    return true;
                case "kingdom":
                case "king":
                    titleType = FeudalTitleType.Kingdom;
                    return true;
                case "empire":
                case "emperor":
                    titleType = FeudalTitleType.Empire;
                    return true;
                default:
                    error = $"Error: '{value}' is not a formable feudal title type. Valid types: county, duchy, kingdom, empire.";
                    return false;
            }
        }

        private static bool TryResolveHero(string query, out Hero hero, out string error)
        {
            hero = null;
            error = null;

            if (string.IsNullOrWhiteSpace(query))
            {
                error = "Error: Hero query cannot be empty.";
                return false;
            }

            string trimmedQuery = query.Trim();

            List<Hero> livingHeroes = Hero.AllAliveHeroes
                .Where(h => h != null)
                .ToList();

            hero = livingHeroes.FirstOrDefault(h =>
                h.StringId.Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                || h.Name.ToString().Equals(trimmedQuery, StringComparison.OrdinalIgnoreCase));

            if (hero != null)
                return true;

            List<Hero> partialMatches = livingHeroes
                .Where(h =>
                    h.Name.ToString().IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0
                    || h.StringId.IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(6)
                .ToList();

            if (partialMatches.Count == 1)
            {
                hero = partialMatches[0];
                return true;
            }

            if (partialMatches.Count > 1)
            {
                string suggestions = string.Join(", ", partialMatches.Select(h =>
                    $"{h.Name} ({h.Clan?.Name?.ToString() ?? "no clan"})"));
                error = $"Error: Hero query '{trimmedQuery}' is ambiguous. Matches: {suggestions}.";
                return false;
            }

            error = $"Error: Could not find a living hero matching '{trimmedQuery}'.";
            return false;
        }

        private static string ValidateMarriageTargets(Hero firstHero, Hero secondHero)
        {
            if (firstHero == null || secondHero == null)
                return "Marriage did not proceed: both marriage targets must be valid heroes.";

            if (!firstHero.IsAlive || !secondHero.IsAlive)
                return "Marriage did not proceed: both marriage targets must be alive.";

            List<string> reasons = new List<string>();
            AddMarriageHeroStateReasons(firstHero, reasons);
            AddMarriageHeroStateReasons(secondHero, reasons);

            if (reasons.Count > 0)
                return $"Marriage did not proceed: {string.Join("; ", reasons)}.";

            if (firstHero.IsFemale == secondHero.IsFemale)
                return "Marriage did not proceed: the current Bannerlord marriage model only permits opposite-sex couples.";

            if (firstHero.Clan?.Leader == firstHero && secondHero.Clan?.Leader == secondHero)
                return "Marriage did not proceed: Bannerlord does not permit two reigning clan leaders to marry each other.";

            var marriageModel = Campaign.Current?.Models?.MarriageModel;
            if (marriageModel == null)
                return "Marriage did not proceed: the campaign marriage model is unavailable.";

            if (!marriageModel.IsClanSuitableForMarriage(firstHero.Clan))
                return $"Marriage did not proceed: {firstHero.Clan.Name} is not eligible for marriage under the current campaign rules.";

            if (!marriageModel.IsClanSuitableForMarriage(secondHero.Clan))
                return $"Marriage did not proceed: {secondHero.Clan.Name} is not eligible for marriage under the current campaign rules.";

            bool firstCanMarry = firstHero.CanMarry();
            bool secondCanMarry = secondHero.CanMarry();
            if (!firstCanMarry || !secondCanMarry)
            {
                string blockedNames = string.Join(" and ", new[]
                {
                    firstCanMarry ? null : firstHero.Name.ToString(),
                    secondCanMarry ? null : secondHero.Name.ToString()
                }.Where(name => !string.IsNullOrEmpty(name)));
                return $"Marriage did not proceed: {blockedNames} cannot currently marry under the active campaign rules, possibly because of an engagement or another mod restriction.";
            }

            if (!marriageModel.IsCoupleSuitableForMarriage(firstHero, secondHero))
                return $"Marriage did not proceed: the active campaign marriage model considers {firstHero.Name} and {secondHero.Name} an unsuitable couple, possibly because they are close relatives or committed to another courtship.";

            return null;
        }

        private static void AddMarriageHeroStateReasons(Hero hero, List<string> reasons)
        {
            string name = hero.Name?.ToString() ?? hero.StringId ?? "A selected hero";

            if (hero.IsDisabled)
                reasons.Add($"{name} is disabled");
            if (!hero.IsActive)
                reasons.Add($"{name} is not currently active in the campaign");
            if (hero.IsPrisoner)
                reasons.Add($"{name} is currently a prisoner");
            if (hero.IsChild || hero.Age < SuccessionLawHelper.GetAgeOfMajority())
                reasons.Add($"{name} is not an adult");
            if (hero.Spouse != null)
                reasons.Add($"{name} is already married to {hero.Spouse.Name}");
            if (hero.Clan == null)
                reasons.Add($"{name} does not belong to a clan");
            if (!hero.IsLord)
                reasons.Add($"{name} is not an eligible noble");
            if (hero.IsMinorFactionHero)
                reasons.Add($"{name} belongs to a minor faction");
            if (hero.IsNotable)
                reasons.Add($"{name} is a notable rather than a noble");
            if (hero.PartyBelongedTo?.MapEvent != null)
                reasons.Add($"{name} is currently involved in a battle");
            if (hero.PartyBelongedTo?.Army != null)
                reasons.Add($"{name} is currently serving in an army");
        }

        private static bool TryApplyMarriage(Hero firstHero, Hero secondHero, out string error)
        {
            error = null;

            MethodInfo marriageMethod = typeof(MarriageAction)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "Apply")
                .FirstOrDefault(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length >= 2
                        && parameters[0].ParameterType == typeof(Hero)
                        && parameters[1].ParameterType == typeof(Hero);
                });

            if (marriageMethod == null)
            {
                error = "Error: Could not locate a compatible TaleWorlds marriage action.";
                return false;
            }

            ParameterInfo[] methodParameters = marriageMethod.GetParameters();
            object[] invokeArgs = new object[methodParameters.Length];

            invokeArgs[0] = firstHero;
            invokeArgs[1] = secondHero;

            for (int i = 2; i < methodParameters.Length; i++)
            {
                ParameterInfo parameter = methodParameters[i];
                if (parameter.HasDefaultValue)
                    invokeArgs[i] = parameter.DefaultValue;
                else if (parameter.ParameterType == typeof(bool))
                    invokeArgs[i] = true;
                else if (parameter.ParameterType.IsValueType)
                    invokeArgs[i] = Activator.CreateInstance(parameter.ParameterType);
                else
                    invokeArgs[i] = null;
            }

            try
            {
                marriageMethod.Invoke(null, invokeArgs);

                if (firstHero.Spouse != secondHero || secondHero.Spouse != firstHero)
                {
                    string currentState = $"{firstHero.Name}.Spouse={firstHero.Spouse?.Name?.ToString() ?? "none"}; "
                        + $"{secondHero.Name}.Spouse={secondHero.Spouse?.Name?.ToString() ?? "none"}";
                    error = $"Marriage did not proceed: MarriageAction returned without linking {firstHero.Name} and {secondHero.Name}. "
                        + $"The action may have been rejected or intercepted by another mod. Current state: {currentState}.";
                    return false;
                }

                return true;
            }
            catch (TargetInvocationException ex)
            {
                error = $"Error: Marriage action failed: {ex.InnerException?.Message ?? ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Error: Marriage action failed: {ex.Message}";
                return false;
            }
        }
        private static bool TryParseCourtFaction(string value, out FactionType type)
        {
            // Keep the old enum/XML key usable for existing integrations and commands.
            if (string.Equals(value, "Traditionalists", StringComparison.OrdinalIgnoreCase))
            {
                type = FactionType.Royalists;
                return true;
            }
            return Enum.TryParse(value, true, out type) && Enum.IsDefined(typeof(FactionType), type);
        }
    }
}
