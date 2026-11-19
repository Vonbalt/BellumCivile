using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class InternalWarEnding
    {
        internal const string Exhaustion = "losing side exhausted under decisive war score pressure";
        internal const string MutualExhaustion = "both sides exhausted without decisive leverage";
        internal const string CouncilTerms = "negotiated internal settlement";

        internal static TextObject Explain(string reason, Kingdom winner = null, Kingdom loser = null,
            WarScoreRecord score = null, float? losingWill = null)
        {
            TextObject text;
            bool showScore = false;
            switch (reason)
            {
                case MutualExhaustion:
                case "player accepted mutual white peace":
                    return new TextObject("{=BC_Result_InternalMutualExhaustion}Both sides were exhausted, and neither could press its advantage. They agreed to lay down their arms.");
                case CouncilTerms:
                    return new TextObject("{=BC_Result_InternalCouncilTerms}The houses ended the fighting under agreed terms of reconciliation.");
                case "claimant surrendered the active feud":
                    return new TextObject("{=BC_Result_InternalFeudSurrender}The claimant chose to lay down arms and yield the dispute.");
                case "temporary feud kingdom destroyed":
                case "rebel kingdom eliminated before civil-war resolution":
                    text = new TextObject("{=BC_Result_InternalSideGone}{LOSER} could no longer muster a host. The contest could not continue.");
                    break;
                case Exhaustion:
                    text = new TextObject("{=BC_Result_Exhaustion}{LOSER} could no longer sustain the fighting and was forced to concede.");
                    showScore = true;
                    break;
                case "defender fully occupied":
                case "attacker fully occupied":
                    text = new TextObject("{=BC_Result_InternalOccupation}{WINNER} occupied the enemy strongholds and forced {LOSER} to yield.");
                    break;
                case "landless realm pressure reached total surrender":
                    text = new TextObject("{=BC_Result_InternalLandlessSurrender}Without a stronghold to sustain the war, {LOSER} was forced to yield.");
                    break;
                default:
                    if (score == null || winner == null || loser == null
                        || System.Math.Abs(score.Score) < BellumCivileConstants.WarScoreForcePeaceThreshold) return null;
                    text = new TextObject("{=BC_Result_InternalDecisive}{WINNER} gained a decisive advantage. {LOSER} could no longer resist its terms.");
                    showScore = true;
                    break;
            }
            if (winner == null || loser == null) return null;
            text.SetTextVariable("WINNER", winner.Name).SetTextVariable("LOSER", loser.Name);
            if (score == null || !showScore) return text;
            return new TextObject(losingWill.HasValue
                    ? "{=BC_Result_InternalEndingWithWill}{ENDING} (War Score for {WINNER}: {SCORE}; {LOSER} War Enthusiasm: {WILL}.)"
                    : "{=BC_Result_InternalEndingWithScore}{ENDING} (War Score for {WINNER}: {SCORE}.)")
                .SetTextVariable("ENDING", text).SetTextVariable("WINNER", winner.Name).SetTextVariable("LOSER", loser.Name)
                .SetTextVariable("SCORE", score.GetSelfRelativeScore(winner.StringId).ToString("+0.#;-0.#;0"))
                .SetTextVariable("WILL", losingWill.GetValueOrDefault().ToString("0.#"));
        }

        internal static TextObject VoluntarySurrender(Kingdom surrendering, Kingdom opponent) =>
            new TextObject("{=BC_Result_InternalVoluntarySurrender}{LOSER} laid down its arms and yielded to {WINNER}.")
                .SetTextVariable("LOSER", surrendering?.Name).SetTextVariable("WINNER", opponent?.Name);
    }
}
