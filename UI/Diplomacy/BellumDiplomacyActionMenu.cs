using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BellumCivile.Behaviors;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Diplomacy
{
    internal static class BellumDiplomacyActionMenu
    {
        private static readonly FieldInfo ExplanationField = AccessTools.Field(typeof(KingdomDiplomacyProposalActionItemVM), "_explanationText");
        private static readonly FieldInfo ActionField = AccessTools.Field(typeof(KingdomDiplomacyProposalActionItemVM), "_action");
        private static readonly MethodInfo RefreshSelection = AccessTools.Method(typeof(KingdomDiplomacyVM), "OnSetCurrentDiplomacyItem");

        private sealed class Selection
        {
            internal string Id;
            internal string Fallback;
            internal KingdomDecision Decision;
            internal KingdomDiplomacyProposalActionItemVM Action;
            internal int InfluenceCost;
            internal bool DiplomacyAlliance;
        }

        internal static void Open(KingdomDiplomacyVM viewModel)
        {
            var selectedRealm = viewModel?.CurrentSelectedDiplomacyItem;
            if (!BellumCivileOptions.EnableWarPeaceLogicRevamp || selectedRealm == null || viewModel.Actions == null) return;
            var choices = viewModel.Actions.Where(action => action != null).Select(BuildChoice).ToList();
            var alliance = DiplomacyExternalActionAdapter.GetAllianceAction(selectedRealm, false);
            if (alliance != null)
            {
                string hint = alliance.Explanation ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(alliance.Reason))
                    hint = string.IsNullOrWhiteSpace(hint) ? alliance.Reason : hint + "\n" + alliance.Reason;
                choices.Add(new InquiryElement(new Selection { DiplomacyAlliance = true, Fallback = alliance.Name },
                    alliance.Name, null, alliance.Enabled, hint));
            }
            if (choices.Count == 0) { ShowUnavailable(null); return; }
            string propose = GameTexts.FindText("str_policy_propose")?.ToString();
            if (string.IsNullOrWhiteSpace(propose)) propose = new TextObject("{=BC_Diplomacy_ActionPropose}Propose").ToString();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BC_Diplomacy_ActionTitle}Diplomatic Action").ToString(),
                new TextObject("{=BC_Diplomacy_ActionPrompt}Select an action concerning {REALM}.")
                    .SetTextVariable("REALM", selectedRealm.Faction2?.Name ?? TextObject.GetEmpty()).ToString(),
                choices, true, 1, 1, propose,
                new TextObject("{=BC_Diplomacy_ActionCancel}Cancel").ToString(), selected =>
                {
                    if (selected?.Count != 1 || !(selected[0].Identifier is Selection choice)) return;
                    ExecuteFresh(viewModel, selectedRealm, choice);
                }, null), true);
        }

        private static InquiryElement BuildChoice(KingdomDiplomacyProposalActionItemVM action)
        {
            var selection = Describe(action);
            string label = ActionLabel(selection);
            if (action.InfluenceCost > 0)
                label = new TextObject("{=BC_Diplomacy_ActionWithInfluence}{ACTION} ({COST} {ICON})")
                    .SetTextVariable("ACTION", label).SetTextVariable("COST", action.InfluenceCost)
                    .SetTextVariable("ICON", new TextObject("{=!}<img src=\"General\\Icons\\Influence@2x\" extend=\"7\">"))
                    .ToString();
            string hint = action.Explanation ?? string.Empty;
            string reason = action.Hint?.HintText?.ToString();
            if (!string.IsNullOrWhiteSpace(reason) && reason != hint)
                hint = string.IsNullOrWhiteSpace(hint) ? reason : hint + "\n" + reason;
            bool identifiable = !IsResolution(selection.Id) || selection.Decision != null;
            if (!identifiable)
            {
                string warning = new TextObject("{=BC_Diplomacy_ActionIdentityUnavailable}This decision cannot be identified safely here; use its kingdom notification instead.").ToString();
                hint = string.IsNullOrWhiteSpace(hint) ? warning : hint + "\n" + warning;
            }
            return new InquiryElement(selection, label, null, action.IsEnabled && identifiable, hint);
        }

        private static void ExecuteFresh(KingdomDiplomacyVM viewModel, KingdomDiplomacyItemVM selectedRealm, Selection choice)
        {
            if (!BellumCivileOptions.EnableWarPeaceLogicRevamp || viewModel?.CurrentSelectedDiplomacyItem != selectedRealm
                || RefreshSelection == null) { ShowUnavailable(null); return; }
            if (choice.DiplomacyAlliance)
            {
                var alliance = DiplomacyExternalActionAdapter.GetAllianceAction(selectedRealm, true);
                if (alliance == null || !alliance.Enabled || alliance.Name != choice.Fallback)
                {
                    ShowUnavailable(null);
                    return;
                }
                DiplomacyExternalActionAdapter.Execute(alliance);
                return;
            }
            KingdomDiplomacyProposalActionItemVM fresh;
            try
            {
                RefreshSelection.Invoke(viewModel, new object[] { selectedRealm });
                var matches = viewModel.Actions.Where(action => action != null)
                    .Select(Describe).Where(candidate => Matches(choice, candidate)).ToList();
                if (matches.Count != 1 || !matches[0].Action.IsEnabled
                    || matches[0].Action.InfluenceCost != choice.InfluenceCost)
                {
                    ShowUnavailable(matches.Count == 1 && !matches[0].Action.IsEnabled ? matches[0].Action.Hint?.HintText : null);
                    return;
                }
                fresh = matches[0].Action;
            }
            catch (Exception ex)
            {
                BellumCivileLogger.Log("Diplomacy action could not be refreshed: " + ex);
                ShowUnavailable(null);
                return;
            }
            fresh.ExecuteAction();
        }

        private static Selection Describe(KingdomDiplomacyProposalActionItemVM action)
        {
            var text = ExplanationField?.GetValue(action) as TextObject;
            string id = text?.GetID();
            return new Selection { Id = id ?? string.Empty, Fallback = text?.Value ?? action.Explanation ?? string.Empty,
                Decision = id == "XgioIHv6" || id == "BC_DefensivePact_ResolveAction" ? FindDecision(action) : null,
                Action = action, InfluenceCost = action.InfluenceCost };
        }

        private static bool Matches(Selection first, Selection second)
            => first != null && second != null && CanonicalId(first.Id) == CanonicalId(second.Id)
                && (!string.IsNullOrEmpty(first.Id) || first.Fallback == second.Fallback)
                && (!IsResolution(first.Id) || first.Decision != null && second.Decision != null)
                && (first.Decision == null ? second.Decision == null : ReferenceEquals(first.Decision, second.Decision));

        private static bool IsResolution(string id) => id == "XgioIHv6" || id == "BC_DefensivePact_ResolveAction";

        private static string CanonicalId(string id)
        {
            switch (id)
            {
                case "ioKwRFcj": case "RdsbyNE2": case "NaAA5kjG": case "BC_PeaceCouncil_ProposalExplanation": return "native-peace";
                case "BC_VoluntaryPact_ActionDescription": case "BC_VoluntaryPact_ActionSupport": return "voluntary-pact";
                case "BC_DefensivePact_FormAction": case "BC_DefensivePact_FormActionSupport": case "BC_DefensivePact_FormSupportUnavailable": return "defensive-form";
                case "BC_DefensivePact_JoinAction": case "BC_DefensivePact_JoinActionSupport": case "BC_DefensivePact_JoinSupportUnavailable": return "defensive-join";
                case "BC_DefensivePact_LeaveAction": case "BC_DefensivePact_LeaveActionSupport": case "BC_DefensivePact_LeaveSupportUnavailable": return "defensive-leave";
                default: return id ?? string.Empty;
            }
        }

        private static KingdomDecision FindDecision(KingdomDiplomacyProposalActionItemVM action)
        {
            try
            {
                var callback = ActionField?.GetValue(action) as Action;
                if (callback?.Target == null) return null;
                var decisions = PatchProcessor.GetOriginalInstructions(callback.Method)
                    .Select(instruction => instruction.operand).OfType<FieldInfo>()
                    .Where(field => typeof(KingdomDecision).IsAssignableFrom(field.FieldType)
                        && field.DeclaringType?.IsInstanceOfType(callback.Target) == true)
                    .Select(field => field.GetValue(callback.Target) as KingdomDecision)
                    .Where(decision => decision != null).Distinct().ToList();
                return decisions.Count == 1 ? decisions[0] : null;
            }
            catch { return null; }
        }

        private static string ActionLabel(Selection selection)
        {
            string key;
            switch (selection.Id)
            {
                case "PYaUEZzn": key = "{=BC_Diplomacy_DeclareWar}Declare War"; break;
                case "ioKwRFcj": case "RdsbyNE2": case "NaAA5kjG": case "BC_PeaceCouncil_ProposalExplanation":
                    key = "{=BC_Diplomacy_NegotiatePeace}Negotiate Peace"; break;
                case "MTtrk28a": key = "{=BC_Diplomacy_FormAlliance}Form Alliance"; break;
                case "gdUE1h39": key = "{=BC_Diplomacy_TradeAgreement}Sign Trade Agreement"; break;
                case "BC_VoluntaryPact_ActionDescription": case "BC_VoluntaryPact_ActionSupport":
                    key = "{=BC_Diplomacy_NonAggression}Propose Non-Aggression Pact"; break;
                case "BC_DefensivePact_FormAction": case "BC_DefensivePact_FormActionSupport": case "BC_DefensivePact_FormSupportUnavailable":
                    key = "{=BC_Diplomacy_FormDefensive}Form Defensive Pact"; break;
                case "BC_DefensivePact_JoinAction": case "BC_DefensivePact_JoinActionSupport": case "BC_DefensivePact_JoinSupportUnavailable":
                    key = "{=BC_Diplomacy_JoinDefensive}Join Defensive Pact"; break;
                case "BC_DefensivePact_LeaveAction": case "BC_DefensivePact_LeaveActionSupport": case "BC_DefensivePact_LeaveSupportUnavailable":
                    key = "{=BC_Diplomacy_LeaveDefensive}Leave Defensive Pact"; break;
                case "BC_DefensivePact_ResolveAction": key = "{=BC_Diplomacy_ResolveDefensive}Resolve Defensive Pact Decision"; break;
                case "XgioIHv6":
                    key = selection.Decision is DeclareWarDecision ? "{=BC_Diplomacy_ResolveWar}Resolve War Decision"
                        : selection.Decision is MakePeaceKingdomDecision ? "{=BC_Diplomacy_ResolvePeace}Resolve Peace Decision"
                        : selection.Decision is StartAllianceDecision ? "{=BC_Diplomacy_ResolveAlliance}Resolve Alliance Decision"
                        : selection.Decision is TradeAgreementDecision ? "{=BC_Diplomacy_ResolveTrade}Resolve Trade Decision"
                        : "{=BC_Diplomacy_ResolveDecision}Resolve Decision";
                    break;
                default: return (selection.Action.Explanation ?? selection.Action.Name ?? string.Empty).Replace('\n', ' ');
            }
            return new TextObject(key).ToString();
        }

        private static void ShowUnavailable(TextObject reason)
        {
            InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BC_Diplomacy_ActionUnavailableTitle}Action Unavailable").ToString(),
                (reason ?? new TextObject("{=BC_Diplomacy_ActionUnavailable}The diplomatic situation has changed. Please review the available actions again.")).ToString(),
                true, false, new TextObject("{=BC_Diplomacy_ActionDone}Done").ToString(), "", null, null));
        }
    }
}
