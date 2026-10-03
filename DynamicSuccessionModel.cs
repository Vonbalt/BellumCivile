using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;

namespace BellumCivile
{
    public class DynamicSuccessionModel : DefaultHeirSelectionCalculationModel
    {
        private Hero _cachedDeadHero;
        private Clan _cachedCandidateClan;
        private GenderSuccessionLaw _cachedGenderLaw;
        private HouseSuccessionLaw _cachedSuccessionLaw;
        private Dictionary<Hero, int> _cachedScores;

        public override int CalculateHeirSelectionPoint(
            Hero candidateHeir,
            Hero deadHero,
            ref Hero maxSkillHero)
        {
            try
            {
                if (candidateHeir?.Clan == null || deadHero?.Clan == null)
                    return SafeVanillaHeirScore(candidateHeir, deadHero, ref maxSkillHero);

                SuccessionLawSet laws = SuccessionLawHelper.GetLawsForClan(deadHero.Clan);
                EnsureScoreCache(candidateHeir.Clan, deadHero, laws);
                return _cachedScores.TryGetValue(candidateHeir, out int score)
                    ? score
                    : int.MinValue / 4;
            }
            catch
            {
                return SafeVanillaHeirScore(candidateHeir, deadHero, ref maxSkillHero);
            }
        }

        private void EnsureScoreCache(Clan candidateClan, Hero deadHero, SuccessionLawSet laws)
        {
            if (_cachedScores != null
                && _cachedDeadHero == deadHero
                && _cachedCandidateClan == candidateClan
                && _cachedGenderLaw == laws.GenderLaw
                && _cachedSuccessionLaw == laws.SuccessionLaw)
            {
                return;
            }

            List<Hero> ordered = SuccessionLawHelper.GetOrderedSuccessionLine(
                candidateClan,
                deadHero,
                laws);
            _cachedScores = new Dictionary<Hero, int>();
            for (int index = 0; index < ordered.Count; index++)
                _cachedScores[ordered[index]] = 1000000 - index;

            _cachedDeadHero = deadHero;
            _cachedCandidateClan = candidateClan;
            _cachedGenderLaw = laws.GenderLaw;
            _cachedSuccessionLaw = laws.SuccessionLaw;
        }

        private int SafeVanillaHeirScore(Hero candidateHeir, Hero deadHero, ref Hero maxSkillHero)
        {
            try
            {
                return base.CalculateHeirSelectionPoint(candidateHeir, deadHero, ref maxSkillHero);
            }
            catch
            {
                if (candidateHeir == null || !candidateHeir.IsAlive || candidateHeir.IsDisabled
                    || candidateHeir.Age < SuccessionLawHelper.GetAgeOfMajority())
                    return 0;

                int score = candidateHeir.GetSkillValue(DefaultSkills.Leadership)
                    + candidateHeir.GetSkillValue(DefaultSkills.Steward)
                    + candidateHeir.GetSkillValue(DefaultSkills.Tactics)
                    + (int)candidateHeir.Age;
                if (candidateHeir.Father == deadHero || candidateHeir.Mother == deadHero)
                    score += 100000;
                return score;
            }
        }
    }
}
