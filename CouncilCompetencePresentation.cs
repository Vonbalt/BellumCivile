using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class CouncilCompetencePresentation
    {
        internal static TextObject Requirements(PrivyCouncilOffice office)
        {
            var factors = PrivyCouncilBehavior.GetCompetenceFactors(office);
            return new TextObject("{=BC_Council_CompetenceRequirements}Competence in this office relies equally on {FIRST} and {SECOND} (40% each), supported by {ATTRIBUTE} (20%).")
                .SetTextVariable("FIRST", factors.First.Name).SetTextVariable("SECOND", factors.Second.Name)
                .SetTextVariable("ATTRIBUTE", factors.Attribute.Name);
        }

        internal static string Tier(float competence)
        {
            switch (PrivyCouncilBehavior.GetCompetenceTierKey(competence))
            {
                case "Inapt": return new TextObject("{=BC_Council_CompetenceInapt}Inapt").ToString();
                case "Mediocre": return new TextObject("{=BC_Council_CompetenceMediocre}Mediocre").ToString();
                case "Average": return new TextObject("{=BC_Council_CompetenceAverage}Average").ToString();
                case "Skillful": return new TextObject("{=BC_Council_CompetenceSkillful}Skillful").ToString();
                default: return new TextObject("{=BC_Council_CompetenceMasterful}Masterful").ToString();
            }
        }

        internal static TextObject Candidate(Hero hero, PrivyCouncilOffice office)
        {
            if (hero == null) return new TextObject("{=BC_Council_CompetenceUnavailable}Competence: unavailable");
            float score = PrivyCouncilBehavior.CalculateCompetence(hero, office);
            return new TextObject("{=BC_Council_CandidateCompetence}Competence: {TIER} ({SCORE})")
                .SetTextVariable("TIER", Tier(score)).SetTextVariable("SCORE", score.ToString("0"));
        }
    }
}
