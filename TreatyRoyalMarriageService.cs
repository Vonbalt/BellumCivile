using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile
{
    internal sealed class TreatyRoyalMarriageCandidate
    {
        public Kingdom ConcedingRealm { get; }
        public Kingdom ReceivingRealm { get; }
        public Hero ConcedingSpouse { get; }
        public Hero ReceivingSpouse { get; }
        public Clan ReceivingClan => ReceivingRealm?.RulingClan;
        public int WarScoreCost { get; }

        public TreatyRoyalMarriageCandidate(Kingdom concedingRealm, Kingdom receivingRealm,
            Hero concedingSpouse, Hero receivingSpouse, int warScoreCost)
        {
            ConcedingRealm = concedingRealm;
            ReceivingRealm = receivingRealm;
            ConcedingSpouse = concedingSpouse;
            ReceivingSpouse = receivingSpouse;
            WarScoreCost = warScoreCost;
        }
    }

    internal static class TreatyRoyalMarriageService
    {
        public static IReadOnlyList<TreatyRoyalMarriageCandidate> GetCandidates(Kingdom concedingRealm, Kingdom receivingRealm)
        {
            if (concedingRealm == null || receivingRealm == null || concedingRealm == receivingRealm
                || concedingRealm.IsEliminated || receivingRealm.IsEliminated
                || concedingRealm.RulingClan == null || receivingRealm.RulingClan == null)
                return new List<TreatyRoyalMarriageCandidate>();

            HashSet<Hero> protectedHeirs = new HashSet<Hero>();
            DynasticHeirBehavior heirs = Campaign.Current?.GetCampaignBehavior<DynasticHeirBehavior>();
            Hero concedingHeir = heirs?.GetDynasticSuccessionCandidate(concedingRealm);
            Hero receivingHeir = heirs?.GetDynasticSuccessionCandidate(receivingRealm);
            if (concedingHeir != null) protectedHeirs.Add(concedingHeir);
            if (receivingHeir != null) protectedHeirs.Add(receivingHeir);

            List<Hero> conceding = concedingRealm.RulingClan.Heroes
                .Where(hero => IsEligibleRoyalRelative(hero, concedingRealm, protectedHeirs))
                .ToList();
            List<Hero> receiving = receivingRealm.RulingClan.Heroes
                .Where(hero => IsEligibleRoyalRelative(hero, receivingRealm, protectedHeirs))
                .ToList();

            List<TreatyRoyalMarriageCandidate> result = new List<TreatyRoyalMarriageCandidate>();
            foreach (Hero sourceSpouse in conceding)
            {
                foreach (Hero receivingSpouse in receiving)
                {
                    if (sourceSpouse.IsFemale == receivingSpouse.IsFemale
                        || !Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(sourceSpouse, receivingSpouse)
                        || !CanGuaranteeReceivingClan(sourceSpouse, receivingSpouse, receivingRealm.RulingClan))
                        continue;

                    result.Add(new TreatyRoyalMarriageCandidate(concedingRealm, receivingRealm,
                        sourceSpouse, receivingSpouse, BellumCivileConstants.TreatyRoyalMarriageCost));
                }
            }

            return result
                .OrderBy(candidate => Math.Abs(candidate.ConcedingSpouse.Age - candidate.ReceivingSpouse.Age))
                .ThenBy(candidate => candidate.ConcedingSpouse.Age)
                .ThenBy(candidate => candidate.ReceivingSpouse.Age)
                .ToList();
        }

        public static bool TryResolveCandidate(Kingdom concedingRealm, Kingdom receivingRealm,
            string concedingHeroId, string receivingHeroId, out TreatyRoyalMarriageCandidate candidate)
        {
            candidate = GetCandidates(concedingRealm, receivingRealm)
                .FirstOrDefault(entry => entry.ConcedingSpouse.StringId == concedingHeroId
                    && entry.ReceivingSpouse.StringId == receivingHeroId);
            return candidate != null;
        }

        public static bool TryApply(TreatyRoyalMarriageCandidate candidate, out string reason)
        {
            reason = "royal marriage candidate is no longer valid";
            if (candidate == null || !TryResolveCandidate(candidate.ConcedingRealm, candidate.ReceivingRealm,
                candidate.ConcedingSpouse?.StringId, candidate.ReceivingSpouse?.StringId, out TreatyRoyalMarriageCandidate current))
                return false;

            try
            {
                if (!CanGuaranteeReceivingClan(current.ConcedingSpouse, current.ReceivingSpouse, current.ReceivingClan))
                {
                    reason = "the active marriage model cannot guarantee the treaty's receiving clan";
                    return false;
                }
                TreatyMarriageClanContext.Run(current.ConcedingSpouse, current.ReceivingSpouse, current.ReceivingClan,
                    () => MarriageAction.Apply(current.ConcedingSpouse, current.ReceivingSpouse));
            }
            catch (Exception ex)
            {
                reason = "marriage action failed: " + ex.Message;
                return false;
            }

            if (current.ConcedingSpouse.Spouse != current.ReceivingSpouse
                || current.ReceivingSpouse.Spouse != current.ConcedingSpouse)
            {
                reason = "Bannerlord did not register the selected marriage";
                return false;
            }
            if (current.ConcedingSpouse.Clan != current.ReceivingClan
                || current.ReceivingSpouse.Clan != current.ReceivingClan)
            {
                reason = "marriage completed but the conceding spouse did not enter the demanding royal clan";
                return false;
            }

            reason = "royal marriage concluded";
            return true;
        }

        private static bool CanGuaranteeReceivingClan(Hero concedingSpouse, Hero receivingSpouse, Clan receivingClan)
        {
            Clan resolved = null;
            TreatyMarriageClanContext.Run(concedingSpouse, receivingSpouse, receivingClan,
                () => resolved = Campaign.Current?.Models?.MarriageModel?.GetClanAfterMarriage(concedingSpouse, receivingSpouse));
            return resolved == receivingClan;
        }

        private static bool IsEligibleRoyalRelative(Hero hero, Kingdom realm, HashSet<Hero> protectedHeirs)
        {
            return hero != null
                && hero.Clan == realm.RulingClan
                && hero != realm.RulingClan.Leader
                && !protectedHeirs.Contains(hero)
                && hero.IsLord
                && hero.IsAlive
                && hero.IsActive
                && !hero.IsDisabled
                && !hero.IsChild
                && hero.Age >= SuccessionLawHelper.GetAgeOfMajority()
                && hero.Spouse == null
                && !hero.IsPrisoner
                && hero.CanMarry();
        }
    }

    internal static class TreatyMarriageClanContext
    {
        private static Hero _first;
        private static Hero _second;
        private static Clan _targetClan;

        public static bool TryResolve(Hero first, Hero second, out Clan targetClan)
        {
            bool matches = _targetClan != null
                && ((first == _first && second == _second) || (first == _second && second == _first));
            targetClan = matches ? _targetClan : null;
            return matches;
        }

        public static void Run(Hero first, Hero second, Clan targetClan, Action action)
        {
            Hero oldFirst = _first;
            Hero oldSecond = _second;
            Clan oldTarget = _targetClan;
            _first = first;
            _second = second;
            _targetClan = targetClan;
            try
            {
                action?.Invoke();
            }
            finally
            {
                _first = oldFirst;
                _second = oldSecond;
                _targetClan = oldTarget;
            }
        }
    }
}
