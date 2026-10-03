using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace BellumCivile.Behaviors
{
    // Assessment only. Court maintenance owns observation; reads never create saved memories.
    public sealed class HereditaryLoyaltyBehavior : CampaignBehaviorBase
    {
        private List<HereditaryLoyaltyMemory> _memories = new List<HereditaryLoyaltyMemory>();
        private readonly Dictionary<Kingdom, RealmCache> _cache = new Dictionary<Kingdom, RealmCache>();
        private readonly Dictionary<Kingdom, Dictionary<Hero, double>> _testLoyalty = new Dictionary<Kingdom, Dictionary<Hero, double>>();
        private object[] _population;
        private int _populationDay = -1;
        private int _revision;
        public static HereditaryLoyaltyBehavior Instance => Campaign.Current?.GetCampaignBehavior<HereditaryLoyaltyBehavior>();

        private sealed class RealmCache
        {
            public int Revision = -1;
            public object[] LineInputs;
            public List<Hero> Line = new List<Hero>();
            public object[] EstateInputs;
            public object[] EstateSettings;
            public FeudalInheritancePlan Estate;
            public Hero Primary;
            public int EstateDay = -1;
            public int EstateRevision = -1;
            public readonly Dictionary<Hero, Tuple<object[], HereditaryLoyaltyAssessment>> Assessments =
                new Dictionary<Hero, Tuple<object[], HereditaryLoyaltyAssessment>>();
        }

        private static Hero Sovereign(Kingdom realm) => RegencyBehavior.Instance?.GetLegalClanHead(realm?.RulingClan) ?? realm?.Leader;
        private static bool Applies(Kingdom realm) => realm != null && !realm.IsEliminated
            && SuccessionLawBehavior.Instance?.ResolvePermanentRealm(realm) == realm
            && CrownAccessionBehavior.IsHereditaryRealm(realm);

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ =>
            {
                foreach (Kingdom realm in Kingdom.All.Where(Applies).ToList())
                    if (CrownAccessionBehavior.Instance?.IsPending(realm) != true) Observe(realm, true);
            });
            CampaignEvents.KingdomCreatedEvent.AddNonSerializedListener(this, realm => Observe(realm, false));
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, (hero, killer, detail, show) => Invalidate());
            CampaignEvents.HeroComesOfAgeEvent.AddNonSerializedListener(this, hero => Invalidate());
            CampaignEvents.OnGivenBirthEvent.AddNonSerializedListener(this, (mother, children, count) => Invalidate());
            CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, (hero, oldClan) => Invalidate());
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, (clan, oldRealm, newRealm, detail, show) => Invalidate());
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, (settlement, open, owner, old, capturer, detail) => Invalidate());
        }

        public override void SyncData(IDataStore store)
        {
            store.SyncData("BC_HereditaryLoyalty", ref _memories);
            _memories = _memories ?? new List<HereditaryLoyaltyMemory>();
            if (store.IsLoading) _testLoyalty.Clear();
            _cache.Clear(); _population = null; _populationDay = -1;
        }

        public void Invalidate() { _revision++; _populationDay = -1; }

        internal bool SetTestLoyalty(Kingdom realm, Hero heir, double? target)
        {
            if (!Applies(realm) || heir == null || (target.HasValue &&
                (!SuccessionChallengeRules.Finite(target.Value) || target.Value < 0 || target.Value > 100))) return false;
            if (target.HasValue)
            {
                if (Get(realm, heir) == null) return false;
                if (!_testLoyalty.TryGetValue(realm, out var values))
                    _testLoyalty[realm] = values = new Dictionary<Hero, double>();
                values[heir] = target.Value;
            }
            else if (_testLoyalty.TryGetValue(realm, out var values)) values.Remove(heir);
            _cache.Remove(realm);
            return true;
        }

        public void Maintain(Kingdom realm)
        {
            PollPopulation();
            if (!Applies(realm)) { _cache.Remove(realm); return; }
            if (CrownAccessionBehavior.Instance?.IsPending(realm) != true) Observe(realm, false);
            // Scores remain lazy; their cheap inputs are compared on the next read.
        }

        public void Observe(Kingdom realm, bool initial)
        {
            if (!Applies(realm)) return;
            Hero sovereign = Sovereign(realm);
            if (sovereign?.IsAlive != true) return;
            var memory = _memories.FirstOrDefault(m => m.Realm == realm);
            if (memory?.Sovereign == sovereign) return;
            _testLoyalty.Remove(realm);
            bool first = memory == null;
            Invalidate();
            var subjects = first && initial ? new List<Hero>() : GetLine(realm).ToList();
            if (first) { memory = new HereditaryLoyaltyMemory { Realm = realm }; _memories.Add(memory); }
            memory.Sovereign = sovereign;
            memory.InitialRuler = first && initial;
            memory.ReignStart = memory.InitialRuler ? Campaign.Current.Models.CampaignTimeModel.CampaignStartTime : CampaignTime.Now;
            memory.YearDays = CampaignTime.Years(1).ToDays;
            memory.ShockPoints = memory.InitialRuler ? 0 : sovereign.Age < SuccessionLawHelper.GetAgeOfMajority() ? 50 : 25;
            memory.ShockUntil = memory.InitialRuler ? CampaignTime.Zero : CampaignTime.Now + CampaignTime.Years(1);
            memory.ShockSubjects = subjects;
        }

        // One shallow population comparison per day also catches changes without native events.
        // Genealogy traversal and ranking are deferred until a stale realm line is requested.
        private void PollPopulation()
        {
            int day = (int)CampaignTime.Now.ToDays;
            if (_populationDay == day) return;
            var values = new List<object>();
            foreach (Clan clan in Clan.All)
            {
                values.Add(clan); values.Add(clan.Leader); values.Add(clan.Kingdom);
                values.Add(clan.IsEliminated); values.Add(clan.IsUnderMercenaryService);
                values.Add(clan.IsMinorFaction); values.Add(clan.IsBanditFaction);
                foreach (Hero hero in clan.Heroes)
                {
                    values.Add(hero); values.Add(hero.Father); values.Add(hero.Mother); values.Add(hero.Spouse);
                    values.Add(hero.IsAlive); values.Add(hero.IsDisabled); values.Add(Math.Floor(hero.Age));
                    values.Add(hero.IsFemale); values.Add(hero.IsWanderer); values.Add(hero.IsNotable);
                    values.Add(hero.GetSkillValue(DefaultSkills.Leadership)); values.Add(hero.GetSkillValue(DefaultSkills.Charm));
                    values.Add(hero.GetSkillValue(DefaultSkills.Steward)); values.Add(hero.GetSkillValue(DefaultSkills.Tactics));
                }
            }
            var next = values.ToArray();
            if (_population == null || !_population.SequenceEqual(next)) { _population = next; _revision++; }
            _populationDay = day;
        }

        private RealmCache Cache(Kingdom realm)
        {
            if (!_cache.TryGetValue(realm, out var result)) _cache.Add(realm, result = new RealmCache());
            return result;
        }

        public IReadOnlyList<Hero> GetLine(Kingdom realm)
        {
            if (!Applies(realm)) return Array.Empty<Hero>();
            PollPopulation();
            var cache = Cache(realm);
            var laws = SuccessionLawHelper.GetLawsForKingdom(realm);
            var input = new object[] { Sovereign(realm), laws.GenderLaw, laws.SuccessionLaw,
                SuccessionLawHelper.GetAgeOfMajority() };
            if (cache.Revision != _revision || cache.LineInputs == null || !cache.LineInputs.SequenceEqual(input))
            {
                cache.Line = HereditaryRealmSuccession.GetLine(realm);
                cache.Revision = _revision;
                cache.LineInputs = input;
                foreach (var hero in cache.Assessments.Keys.Where(h => !cache.Line.Contains(h)).ToList())
                    cache.Assessments.Remove(hero);
            }
            return cache.Line.AsReadOnly();
        }

        private void RefreshEstate(Kingdom realm, RealmCache cache)
        {
            int day = (int)CampaignTime.Now.ToDays;
            Clan clan = realm.RulingClan;
            Hero sovereign = Sovereign(realm);
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            var laws = SuccessionLawHelper.GetLawsForClan(clan);
            var settings = new object[] { clan, sovereign, clan.Leader, laws.GenderLaw, laws.SuccessionLaw,
                BellumCivileOptions.EnablePartitionSuccession, BellumCivileOptions.PartitionSuccessionMainHeirReservedFiefs,
                SuccessionLawHelper.GetAgeOfMajority() };
            if (cache.EstateDay == day && cache.EstateRevision == _revision
                && cache.EstateSettings != null && cache.EstateSettings.SequenceEqual(settings)) return;
            var values = new List<object>(settings);
            foreach (var hero in clan.Heroes)
            {
                values.Add(hero); values.Add(hero.Age >= SuccessionLawHelper.GetAgeOfMajority());
                values.Add(hero.IsAlive); values.Add(hero.IsDisabled); values.Add(hero.Father); values.Add(hero.Mother);
                values.Add(hero.Spouse); values.Add(hero.IsFemale); values.Add(Math.Floor(hero.Age));
                values.Add(hero.GetSkillValue(DefaultSkills.Leadership)); values.Add(hero.GetSkillValue(DefaultSkills.Charm));
                values.Add(hero.GetSkillValue(DefaultSkills.Steward)); values.Add(hero.GetSkillValue(DefaultSkills.Tactics));
                values.Add(CrownAccessionBehavior.Instance?.HasInheritanceAdvance(sovereign, hero) == true);
            }
            foreach (var fief in clan.Fiefs) { values.Add(fief); values.Add(fief.OwnerClan); values.Add(fief.Prosperity); }
            if (titles != null)
                foreach (var title in titles.GetTitlesHeldByClan(clan, deJure: true))
                {
                    values.Add(title.TitleId); values.Add(title.IsActive); values.Add(title.ParentTitleId);
                    values.Add(title.DeFactoParentTitleId); values.Add(title.CapitalSettlementId); values.Add(title.TitleType);
                }
            var input = values.ToArray();
            if (cache.EstateInputs == null || !cache.EstateInputs.SequenceEqual(input))
            {
                var estate = FeudalInheritancePlanner.BuildPlan(clan, sovereign, BellumCivileOptions.PartitionSuccessionMainHeirReservedFiefs);
                var primary = SuccessionLawHelper.GetLegalSuccessionLine(clan, sovereign, laws).FirstOrDefault();
                cache.Estate = estate;
                cache.Primary = primary;
            }
            cache.EstateInputs = input;
            cache.EstateSettings = settings;
            cache.EstateDay = day; cache.EstateRevision = _revision;
        }

        public HereditaryLoyaltyAssessment Get(Kingdom realm, Hero hero)
        {
            if (!Applies(realm) || hero?.IsAlive != true) return null;
            var memory = _memories.FirstOrDefault(m => m.Realm == realm);
            if (memory == null || memory.Sovereign != Sovereign(realm)) return null;
            var line = GetLine(realm);
            if (!line.Contains(hero)) return null;
            var cache = Cache(realm);
            double majority = SuccessionLawHelper.GetAgeOfMajority();
            bool dependent = hero.Age >= majority && hero.Clan == realm.RulingClan && hero != hero.Clan.Leader
                && hero != Sovereign(realm) && CrownAccessionBehavior.Instance?.HasInheritanceAdvance(memory.Sovereign, hero) != true;
            bool share = false;
            if (dependent)
            {
                RefreshEstate(realm, cache);
                if (cache.Estate.IsValid)
                    share = FeudalInheritancePlanner.GetLivingAccessionShare(cache.Estate, cache.Primary, hero,
                        BellumCivileOptions.EnablePartitionSuccession)?.Fiefs.Count > 0;
                else if (realm.RulingClan.Fiefs.Count > 0) return null;
            }
            double years = memory.ReignYearsAt(CampaignTime.Now.ToDays);
            int shock = memory.ShockFor(hero, CampaignTime.Now.ToDays);
            bool minorRegency = memory.Sovereign.Age < majority && RegencyBehavior.Instance?.GetRegent(realm.RulingClan)?.IsAlive == true;
            double controversy = Campaign.Current.GetCampaignBehavior<ControversyBehavior>()?.GetTotalControversy(realm) ?? 0;
            int honor = hero.GetTraitLevel(DefaultTraits.Honor), mercy = hero.GetTraitLevel(DefaultTraits.Mercy);
            // The patched hero-pair read matches relation_breakdown's visible value.
            int relation = CharacterRelationManager.GetHeroRelation(hero, memory.Sovereign);
            double dependency = Math.Max(0, Math.Floor(hero.Age - majority));
            double concession = SuccessionChallengeBehavior.Instance?.LoyaltyBonus(realm, hero, memory.Sovereign) ?? 0;
            double submission = SuccessionChallengeBehavior.Instance?.SubmissionBonus(realm, hero, memory.Sovereign) ?? 0;
            double? testTarget = _testLoyalty.TryGetValue(realm, out var targets) && targets.TryGetValue(hero, out double target)
                ? (double?)target : null;
            var input = new object[] { honor, mercy, line[0] == hero, memory.InitialRuler, years, shock,
                controversy, minorRegency, dependent, share, dependency, realm.RulingClan.Fiefs.Count, relation, concession, testTarget, submission };
            if (cache.Assessments.TryGetValue(hero, out var previous) && previous.Item1.SequenceEqual(input)) return previous.Item2;
            var result = HereditaryLoyaltyRules.Assess(honor, mercy, line[0] == hero, memory.InitialRuler,
                years, shock, controversy, minorRegency, dependent, share, dependency, realm.RulingClan.Fiefs.Count, relation);
            result.Concession = concession;
            result.Submission = submission;
            if (testTarget.HasValue) result.TestAdjustment = testTarget.Value - result.Raw;
            cache.Assessments[hero] = Tuple.Create(input, result);
            return result;
        }
    }
}
