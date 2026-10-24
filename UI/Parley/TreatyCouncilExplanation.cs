using System;
using System.Linq;
using TaleWorlds.Localization;

namespace BellumCivile.UI.Parley
{
    internal static class TreatyCouncilExplanation
    {
        internal static string Resolve(TreatyCouncilMemberEvaluation evaluation, bool isRuler,
            bool realmAccepts, string rulerCouncilReason)
        {
            if (isRuler && !string.IsNullOrWhiteSpace(rulerCouncilReason))
                return rulerCouncilReason;
            if (evaluation == null)
                return new TextObject("{=BC_Parley_NoStrongMotive}No decisive motive").ToString();

            TreatyCouncilVoteStance stance = isRuler
                ? realmAccepts ? TreatyCouncilVoteStance.Yay : TreatyCouncilVoteStance.Nay
                : evaluation.Stance;
            if (stance == TreatyCouncilVoteStance.Abstain)
            {
                switch (evaluation.AbstentionReason)
                {
                    case TreatyCouncilAbstentionReason.NeutralAssessment:
                        return new TextObject("{=BC_Parley_AbstainNeutral}Neither side makes a convincing case; I will abstain.").ToString();
                    case TreatyCouncilAbstentionReason.InsufficientInfluence:
                        return new TextObject("{=BC_Parley_AbstainInsufficientInfluence}I lack the influence to cast my vote.").ToString();
                    case TreatyCouncilAbstentionReason.ProtectedInfluence:
                        return new TextObject("{=BC_Parley_AbstainProtectedInfluence}I must reserve my influence for other obligations.").ToString();
                    case TreatyCouncilAbstentionReason.PlayerAbstained:
                        return new TextObject("{=BC_Parley_Reason_PlayerAbstains}I withhold my voice from this treaty.").ToString();
                    case TreatyCouncilAbstentionReason.PlayerUndecided:
                        return new TextObject("{=BC_Parley_PlayerPositionPending}I have not yet declared my position.").ToString();
                    default:
                        return new TextObject("{=BC_Parley_AbstainUnspecified}I will abstain from this vote.").ToString();
                }
            }

            TreatyCouncilReason primary = evaluation.Reasons
                .Where(reason => reason != null && !string.IsNullOrWhiteSpace(reason.Label)
                    && !float.IsNaN(reason.Amount) && !float.IsInfinity(reason.Amount)
                    && (stance == TreatyCouncilVoteStance.Yay ? reason.Amount > 0f : reason.Amount < 0f))
                .OrderByDescending(reason => Math.Abs(reason.Amount))
                .FirstOrDefault();
            return primary?.Label ?? new TextObject(stance == TreatyCouncilVoteStance.Yay
                ? "{=BC_Parley_Reason_PlayerBacksTreaty}I stand behind these terms."
                : "{=BC_Parley_Reason_PlayerOpposesTreaty}I cannot support these terms.").ToString();
        }
    }
}
