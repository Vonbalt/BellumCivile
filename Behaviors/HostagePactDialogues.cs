using System.Linq;
using TaleWorlds.CampaignSystem;
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
        private void RegisterHostageDialogues(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("bc_hostage_talk", "hero_main_options", "bc_hostage_answer",
                "{=BC_Hostage_Talk}Let us speak of your stay here as a pledge of peace.", CanDiscussHostage, null, 120);
            starter.AddDialogLine("bc_hostage_answer", "bc_hostage_answer", "bc_hostage_options",
                "{=BC_Hostage_Answer}What would you have of me?", null, null);
            starter.AddPlayerLine("bc_hostage_reassure", "bc_hostage_options", "bc_hostage_reassured",
                "{=BC_Hostage_Reassure}Keep faith with our agreement, and you shall be treated honorably.", CanDiscussHostage, null);
            starter.AddDialogLine("bc_hostage_reassured_child", "bc_hostage_reassured", "bc_hostage_options",
                "{=BC_Hostage_ReassuredChild}I understand. I shall behave myself. I hope I may see my family soon.",
                () => Hero.OneToOneConversationHero?.IsChild == true, null, 110);
            starter.AddDialogLine("bc_hostage_reassured", "bc_hostage_reassured", "bc_hostage_options",
                "{=BC_Hostage_Reassured}I understand. I shall honor the agreement between our houses.", null, null);
            starter.AddPlayerLine("bc_hostage_release", "bc_hostage_options", "bc_hostage_release_confirm",
                "{=BC_Hostage_ReleaseOffer}You may return to your family. I release your house from this pledge.", CanDiscussHostage, null);
            starter.AddDialogLine("bc_hostage_release_confirm", "bc_hostage_release_confirm", "bc_hostage_release_decision",
                "{=BC_Hostage_ReleaseConfirm}Then our houses will no longer be bound by this pledge, and any kin of yours held in return shall come home too. Is that your wish?", null, null);
            starter.AddPlayerLine("bc_hostage_release_yes", "bc_hostage_release_decision", "bc_hostage_release_thanks",
                "{=BC_Hostage_ReleaseYes}It is. Let both houses return their hostages in peace.", CanDiscussHostage,
                () => QueueHostageJudgment(false));
            starter.AddDialogLine("bc_hostage_release_thanks", "bc_hostage_release_thanks", "close_window",
                "{=BC_Hostage_ReleaseThanks}Then I shall return with gratitude, and tell my family that you kept me honorably.", null, null);
            starter.AddPlayerLine("bc_hostage_release_no", "bc_hostage_release_decision", "bc_hostage_answer",
                "{=BC_Hostage_Reconsider}I must reconsider.", null, null);
            starter.AddPlayerLine("bc_hostage_execute", "bc_hostage_options", "bc_hostage_execute_confirm",
                "{=BC_Hostage_ExecuteThreat}Your life is forfeit. Prepare yourself.", CanExecuteDiscussedHostage, null);
            starter.AddDialogLine("bc_hostage_execute_confirm", "bc_hostage_execute_confirm", "bc_hostage_execute_decision",
                "{=BC_Hostage_ExecuteConfirm}I came under the protection of your word. My death would break this pledge and stain your honor. My family will remember, and any kin of yours in their keeping may answer for it. Will you truly do this?", null, null);
            starter.AddPlayerLine("bc_hostage_execute_yes", "bc_hostage_execute_decision", "close_window",
                "{=BC_Hostage_ExecuteYes}My decision is final. Carry out the execution.", CanExecuteDiscussedHostage,
                () => QueueHostageJudgment(true));
            starter.AddPlayerLine("bc_hostage_execute_no", "bc_hostage_execute_decision", "bc_hostage_answer",
                "{=BC_Hostage_Reconsider}I must reconsider.", null, null);
            starter.AddPlayerLine("bc_hostage_back", "bc_hostage_options", "hero_main_options",
                "{=BC_Hostage_Back}Let us speak of something else.", null, null);
            starter.AddPlayerLine("bc_hostage_leave", "bc_hostage_options", "close_window",
                "{=BC_Hostage_Leave}That is all for now.", null, null);
        }

        private bool CanDiscussHostage()
        {
            var pact = GetProtectedPact(Hero.OneToOneConversationHero);
            var record = HostagePactText.Hostage(pact, Hero.OneToOneConversationHero);
            return CanOrderHostageTermination(pact, record, Hero.MainHero);
        }

        private static bool CanOrderHostageTermination(HostagePactRecord pact, TreatyHostageRecord record, Hero ruler)
            => pact?.Phase == HostagePactPhase.Active && !pact.IsDue(CampaignTime.Now.ToDays)
                && ruler != null && ruler.IsAlive && !ruler.IsPrisoner && InTreatyCustody(record)
                && record.ReceivingHouse?.Leader == ruler && record.ReceivingHouse.Kingdom?.RulingClan == record.ReceivingHouse
                && pact.FirstRealm?.RulingClan == pact.FirstHouse && pact.SecondRealm?.RulingClan == pact.SecondHouse
                && !pact.FirstRealm.IsEliminated && !pact.SecondRealm.IsEliminated
                && !pact.FirstRealm.IsAtWarWith(pact.SecondRealm)
                && !CivilWarConflictBehavior.IsRealmTransferPending(pact.FirstRealm)
                && !CivilWarConflictBehavior.IsRealmTransferPending(pact.SecondRealm);

        private bool CanExecuteDiscussedHostage()
            => CanDiscussHostage() && ExecutionAllowed(HostagePactText.Hostage(
                GetProtectedPact(Hero.OneToOneConversationHero), Hero.OneToOneConversationHero));

        private void QueueHostageJudgment(bool execute)
        {
            var hero = Hero.OneToOneConversationHero;
            var pact = GetProtectedPact(hero);
            var record = HostagePactText.Hostage(pact, hero);
            if (!CanOrderHostageTermination(pact, record, Hero.MainHero) || (execute && !ExecutionAllowed(record))) return;
            if (!pact.TryBeginResolution(execute ? HostagePactEndReason.Betrayal : HostagePactEndReason.EarlyRelease,
                execute ? record.ReceivingHouse.Kingdom : null)) return;
            pact.TerminatingHostage = hero;
            pact.TerminatingRuler = Hero.MainHero;
            record.DispositionRuler = Hero.MainHero;
            if (execute) record.TryChoose(HostageCustodyOutcome.Execute);
            BellumCivileLogger.Log($"Hostage judgment queued; pact={pact.Id}; hostage={hero.StringId}; reason={pact.EndReason}.");
        }

        private static bool CanResolveHostageJudgment()
        {
            var map = Game.Current?.GameStateManager?.ActiveState as MapState;
            return map != null && !map.AtMenu && !map.MapConversationActive && map.NextIncident == null
                && PlayerEncounter.Current == null && Campaign.Current?.CurrentMenuContext == null
                && !InformationManager.IsAnyInquiryActive();
        }

        private void ProcessHostageJudgment(float dt)
        {
            if (_pacts.Any(p => p?.Phase == HostagePactPhase.Resolving
                && (p.EndReason == HostagePactEndReason.EarlyRelease || p.EndReason == HostagePactEndReason.Betrayal))
                && CanResolveHostageJudgment()) MaintainCustody();
        }
    }
}
