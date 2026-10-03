using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class HostagePactBehavior
    {
        private bool ResolveHostageBetrayal(HostagePactRecord pact)
        {
            var victim = HostagePactText.Hostage(pact, pact.TerminatingHostage);
            if (victim == null) { CancelHostageJudgment(pact, null); return false; }
            if (!victim.ActionStarted)
            {
                if (pact.TerminatingRuler != Hero.MainHero || !ExecutionAllowed(victim)
                    || victim.ReceivingHouse?.Leader != pact.TerminatingRuler
                    || pact.TerminatingRuler.IsPrisoner || !pact.TerminatingRuler.IsAlive
                    || pact.FirstRealm?.RulingClan != pact.FirstHouse || pact.SecondRealm?.RulingClan != pact.SecondHouse
                    || pact.FirstRealm.IsEliminated || pact.SecondRealm.IsEliminated
                    || pact.FirstRealm.IsAtWarWith(pact.SecondRealm)
                    || CampaignTime.Now.ToDays >= pact.EndDay
                    || CivilWarConflictBehavior.IsRealmTransferPending(pact.FirstRealm)
                    || CivilWarConflictBehavior.IsRealmTransferPending(pact.SecondRealm))
                { CancelHostageJudgment(pact, victim); return false; }
                // Native execution already penalizes player Honor for non-dishonorable victims.
                pact.ExecutionNeedsHonorPenalty = victim.Hero.GetTraitLevel(DefaultTraits.Honor) < 0;
                victim.ActionStarted = true;
                KillCharacterAction.ApplyByExecution(victim.Hero, pact.TerminatingRuler, true, false);
            }
            if (victim.Hero?.IsDead != true)
            {
                if (victim.Hero?.DeathMark != KillCharacterAction.KillCharacterActionDetail.None) return false;
                CancelHostageJudgment(pact, victim);
                return false;
            }
            victim.ExecutionSucceeded = true;
            if (!victim.ActionCompleted) FinishWarDisposition(pact, victim);
            ApplyBetrayalConsequences(pact, victim);
            // Relations and native execution reactions have changed before this draw.
            ResolveWarHostage(pact, pact.FirstHostage == victim ? pact.SecondHostage : pact.FirstHostage);
            return true;
        }

        private static void ApplyBetrayalConsequences(HostagePactRecord pact, TreatyHostageRecord victim)
        {
            if (!victim.ExecutionSucceeded) return;
            if (!pact.BreachMemoryApplied)
            {
                pact.BreachMemoryApplied = true;
                RelationMemoryService.ApplyChange(pact.TerminatingRuler, victim.SupplyingHouse?.Leader, -30, true,
                    RelationMemorySources.BetrayedHostagePledge, 10, RelationMemoryScope.House);
            }
            if (!pact.TerminationTraitApplied)
            {
                pact.TerminationTraitApplied = true;
                if (pact.ExecutionNeedsHonorPenalty && pact.TerminatingRuler == Hero.MainHero)
                    TraitLevelingHelper.OnLordExecuted();
            }
        }

        private static void CancelHostageJudgment(HostagePactRecord pact, TreatyHostageRecord victim)
        {
            // A veto is not an execution or a breach. Normal maintenance will handle
            // any intervening death, war, expiry, or change of ruling house.
            pact.Phase = HostagePactPhase.Active;
            pact.EndReason = HostagePactEndReason.None;
            pact.VoluntaryAggressor = null;
            pact.TerminatingHostage = null;
            pact.TerminatingRuler = null;
            if (victim != null)
            {
                victim.ActionStarted = false;
                victim.Outcome = HostageCustodyOutcome.Pending;
                victim.DispositionRuler = null;
            }
            InformationManager.DisplayMessage(new InformationMessage(new TextObject(
                "{=BC_Hostage_JudgmentCancelled}The hostage judgment could not be carried out. No execution or breach of the pledge has been recorded.").ToString()));
            BellumCivileLogger.Log($"Hostage judgment cancelled; pact={pact.Id}.");
        }

        private static void ApplyEarlyReleaseRewards(HostagePactRecord pact)
        {
            if (pact.EndReason != HostagePactEndReason.EarlyRelease) return;
            var released = HostagePactText.Hostage(pact, pact.TerminatingHostage);
            if (released?.ReleaseSucceeded != true || released.Hero?.IsAlive != true) return;
            if (!pact.ReleaseGratitudeApplied)
            {
                pact.ReleaseGratitudeApplied = true;
                RelationMemoryService.ApplyChange(pact.TerminatingRuler, released.Hero, 5, true,
                    RelationMemorySources.ReleasedTreatyHostageEarly, 5);
                if (released.SupplyingHouse?.Leader != released.Hero)
                    RelationMemoryService.ApplyChange(pact.TerminatingRuler, released.SupplyingHouse?.Leader, 5, true,
                        RelationMemorySources.ReleasedTreatyHostageEarly, 5);
            }
            if (!pact.TerminationTraitApplied)
            {
                pact.TerminationTraitApplied = true;
                if (pact.TerminatingRuler == Hero.MainHero)
                    TraitLevelingHelper.OnIncidentResolved(DefaultTraits.Honor, 20);
            }
        }
    }
}
