using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class HostagePactBehavior
    {
        private bool _hostageInquiryOpen;

        internal static bool IsVoluntaryDeclaration(DeclareWarAction.DeclareWarDetail detail)
            => detail == DeclareWarAction.DeclareWarDetail.CausedByKingdomDecision
                || detail == DeclareWarAction.DeclareWarDetail.CausedByPlayerHostility;

        private void OnPactWarDeclared(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail)
        {
            foreach (var pact in _pacts.Where(p => p?.Phase == HostagePactPhase.Active
                && ((p.FirstRealm == first && p.SecondRealm == second) || (p.FirstRealm == second && p.SecondRealm == first))).ToList())
            {
                if (pact.FirstRealm.RulingClan != pact.FirstHouse || pact.SecondRealm.RulingClan != pact.SecondHouse)
                    pact.TryBeginResolution(HostagePactEndReason.HouseReplaced);
                else if (pact.IsDue(CampaignTime.Now.ToDays)) pact.TryBeginResolution(HostagePactEndReason.Expired);
                else pact.TryBeginResolution(HostagePactEndReason.War, IsVoluntaryDeclaration(detail) ? first as Kingdom : null);
                BellumCivileLogger.Log($"Hostage pact war observed; pact={pact.Id}; detail={detail}; aggressor={pact.VoluntaryAggressor?.StringId ?? "unknown"}.");
            }
            // Execute on the hourly pass, after every war listener has registered the conflict.
        }

        private static bool InTreatyCustody(TreatyHostageRecord record)
            => record?.Hero?.IsAlive == true && record.Hero.IsPrisoner && SameCaptivity(record)
                && record.Holding != null && record.Hero.PartyBelongedToAsPrisoner == record.Holding.Party;

        private static bool ExecutionAllowed(TreatyHostageRecord record)
            => InTreatyCustody(record) && record.Hero != Hero.MainHero
                && record.Hero.DeathMark == KillCharacterAction.KillCharacterActionDetail.None
                && record.Hero.CanDie(KillCharacterAction.KillCharacterActionDetail.Executed);

        private void ResolveWarHostage(HostagePactRecord pact, TreatyHostageRecord record)
        {
            if (record == null || record.ActionCompleted) return;
            if (record.ActionStarted && record.Outcome == HostageCustodyOutcome.Execute && record.Hero?.IsDead == true)
            {
                record.ExecutionSucceeded = true;
                FinishWarDisposition(pact, record);
                return;
            }
            if (!InTreatyCustody(record))
            {
                record.TryChoose(HostageCustodyOutcome.Retain);
                record.ActionCompleted = true;
                return;
            }
            if ((pact.EndReason != HostagePactEndReason.Betrayal && !pact.FirstRealm.IsAtWarWith(pact.SecondRealm))
                || pact.FirstRealm.RulingClan != pact.FirstHouse || pact.SecondRealm.RulingClan != pact.SecondHouse)
            {
                // A delayed inquiry is not authority to execute after peace or a change of dynasty.
                record.ClemencyChosen = false;
                record.Outcome = HostageCustodyOutcome.Release;
                return;
            }
            if (record.Outcome == HostageCustodyOutcome.Pending)
            {
                var ruler = record.ReceivingHouse?.Leader;
                record.DispositionRuler = ruler;
                if (ruler == null || !ruler.IsAlive)
                    record.TryChoose(HostageCustodyOutcome.Retain);
                else if (ruler == Hero.MainHero)
                {
                    OfferHostageDisposition(pact, record);
                    return;
                }
                else if (pact.VoluntaryAggressor == null) record.TryChoose(HostageCustodyOutcome.Retain);
                else
                {
                    var supplier = record.SupplyingHouse?.Leader;
                    bool supplierBreached = pact.VoluntaryAggressor == (pact.FirstHostage == record ? pact.FirstRealm : pact.SecondRealm);
                    var choice = HostagePactRules.ChooseDisposition(record.Tier, ruler.GetTraitLevel(DefaultTraits.Honor),
                        ruler.GetTraitLevel(DefaultTraits.Mercy), ruler.GetTraitLevel(DefaultTraits.Calculating),
                        supplier == null ? 0 : ruler.GetRelation(supplier), supplierBreached, ExecutionAllowed(record), MBRandom.RandomFloat);
                    record.TryChoose(choice == HostageDisposition.Release ? HostageCustodyOutcome.Release
                        : choice == HostageDisposition.Execute ? HostageCustodyOutcome.Execute : HostageCustodyOutcome.Retain);
                    record.ClemencyChosen = record.Outcome == HostageCustodyOutcome.Release;
                }
            }
            if (record.Outcome == HostageCustodyOutcome.Release) return;
            if (record.Outcome == HostageCustodyOutcome.Execute && !record.ActionStarted)
            {
                // Commit the attempt before native death callbacks; never repeat an execution on recovery.
                record.ActionStarted = true;
                if (ExecutionAllowed(record) && record.DispositionRuler?.IsAlive == true)
                    KillCharacterAction.ApplyByExecution(record.Hero, record.DispositionRuler, true, false);
                record.ExecutionSucceeded = record.Hero.IsDead;
            }
            // A native veto leaves an ordinary prisoner, not an immortal treaty hostage.
            if (!record.ExecutionSucceeded && record.Hero.DeathMark != KillCharacterAction.KillCharacterActionDetail.None) return;
            if (!record.ExecutionSucceeded) record.Outcome = HostageCustodyOutcome.Retain;
            FinishWarDisposition(pact, record);
        }

        private static void FinishWarDisposition(HostagePactRecord pact, TreatyHostageRecord record)
        {
            record.ActionStarted = true;
            record.ActionCompleted = true;
            Campaign.Current?.GetCampaignBehavior<WarScoreBehavior>()?.RefreshHostageCustodyScore();
            BellumCivileLogger.Log($"Hostage war disposition; pact={pact.Id}; hero={record.Hero?.StringId}; outcome={record.Outcome}; executed={record.ExecutionSucceeded}; ruler={record.DispositionRuler?.StringId}.");
            if (!record.Reported)
            {
                record.Reported = true;
                var text = new TextObject(pact.EndReason == HostagePactEndReason.Betrayal
                    ? (record.ExecutionSucceeded
                        ? "{=BC_Hostage_BetrayalExecuted}The pledge with {REALM} lies broken. {HERO}, once entrusted to the other house's protection, has been executed in captivity."
                        : "{=BC_Hostage_BetrayalRetained}Following the betrayal that ended the pledge with {REALM}, {HERO} has been spared but remains a prisoner. Treaty protection no longer bars ransom, escape, or rescue.")
                    : record.ExecutionSucceeded
                    ? "{=BC_Hostage_WarExecuted}The peace with {REALM} lies broken. {HERO}, once entrusted as a pledge between the houses, has been executed in captivity."
                    : "{=BC_Hostage_WarRetained}War has ended the pledge of peace with {REALM}. {HERO} has been spared, but remains a prisoner; ransom or rescue may now secure their freedom.");
                HostagePactText.Fill(text, pact, record.Hero);
                InformationManager.DisplayMessage(new InformationMessage(text.ToString()));
            }
        }

        private void OfferHostageDisposition(HostagePactRecord pact, TreatyHostageRecord record)
        {
            var map = Game.Current?.GameStateManager?.ActiveState as MapState;
            if (_hostageInquiryOpen || InformationManager.IsAnyInquiryActive() || map == null || map.AtMenu
                || map.MapConversationActive || map.NextIncident != null || PlayerEncounter.Current != null
                || Hero.MainHero?.IsPrisoner == true || Campaign.Current?.CurrentMenuContext != null) return;
            var elements = new List<InquiryElement>
            {
                new InquiryElement(HostageCustodyOutcome.Release, new TextObject("{=BC_Hostage_ReleaseChoice}Spare and release the hostage").ToString(), null),
                new InquiryElement(HostageCustodyOutcome.Retain, new TextObject("{=BC_Hostage_RetainChoice}Keep the hostage as a prisoner").ToString(), null),
                new InquiryElement(HostageCustodyOutcome.Execute, new TextObject("{=BC_Hostage_ExecuteChoice}Execute the hostage").ToString(), null,
                    pact.VoluntaryAggressor != null && ExecutionAllowed(record), new TextObject("{=BC_Hostage_ExecuteHint}Execution requires a deliberate breach of the pact and remains subject to protections against death.").ToString())
            };
            var body = new TextObject("{=BC_Hostage_DispositionBody}War has broken the peace with {REALM}. {HERO} remains in your custody, but the pledge that bound the two houses is at an end. What fate do you decree?");
            HostagePactText.Fill(body, pact, record.Hero);
            _hostageInquiryOpen = true;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=BC_Hostage_DispositionTitle}A Broken Pledge").ToString(), body.ToString(), elements,
                true, 1, 1, new TextObject("{=BC_Hostage_Decide}Confirm judgment").ToString(),
                new TextObject("{=BC_Hostage_Later}Decide later").ToString(), selected =>
                {
                    _hostageInquiryOpen = false;
                    if (record.ReceivingHouse?.Leader != Hero.MainHero || !InTreatyCustody(record)
                        || pact.Phase != HostagePactPhase.Resolving || selected?.Count != 1) return;
                    var choice = (HostageCustodyOutcome)selected[0].Identifier;
                    if (choice == HostageCustodyOutcome.Execute && (pact.VoluntaryAggressor == null || !ExecutionAllowed(record))) return;
                    if (record.TryChoose(choice)) record.ClemencyChosen = choice == HostageCustodyOutcome.Release;
                    MaintainCustody();
                }, _ => _hostageInquiryOpen = false), true);
        }
    }
}
