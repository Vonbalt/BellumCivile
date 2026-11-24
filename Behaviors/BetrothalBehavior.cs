using System;
using System.Collections.Generic;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BellumCivile.Behaviors
{
    internal sealed partial class BetrothalBehavior : CampaignBehaviorBase
    {
        private List<BetrothalRecord> _pledges = new List<BetrothalRecord>();
        private readonly Dictionary<Hero, BetrothalRecord> _byHero = new Dictionary<Hero, BetrothalRecord>();
        private readonly HashSet<(Clan, Clan)> _pledgedHouses = new HashSet<(Clan, Clan)>();
        private BetrothalRecord _incomingOffer;
        private volatile int _campaignThread = Thread.CurrentThread.ManagedThreadId;
        [ThreadStatic] private static BetrothalRecord _wedding;

        internal static BetrothalBehavior Instance => Campaign.Current?.CampaignBehaviorManager?.GetBehavior<BetrothalBehavior>();
        private bool IsCampaignThread => Thread.CurrentThread.ManagedThreadId == _campaignThread;
        private static int Day => (int)CampaignTime.Now.ToDays;
        internal static int Adulthood => Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? BellumCivileOptions.AdulthoodAge;
        internal static bool CanArrange => BellumCivileOptions.EnableBellumStrategicMarriageLogic && !CampaignOptions.IsLifeDeathCycleDisabled;

        public override void RegisterEvents()
        {
            _campaignThread = Thread.CurrentThread.ManagedThreadId;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, ReviewPledges);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, PresentPendingPlayerInquiry);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_Betrothals", ref _pledges);
            _pledges = _pledges ?? new List<BetrothalRecord>();
            if (dataStore.IsLoading) Reindex();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _campaignThread = Thread.CurrentThread.ManagedThreadId;
            Reindex();
            RegisterDialogue(starter);
            Patches.BetrothalTooltipPatch.Initialize();
        }

        private void Reindex()
        {
            _byHero.Clear();
            _pledgedHouses.Clear();
            _incomingOffer = null;
            for (int i = 0; i < _pledges.Count; i++)
            {
                var p = _pledges[i];
                if (p?.First == null || p.Second == null || p.First == p.Second || p.FirstHouse == null
                    || p.SecondHouse == null || p.FirstHouse == p.SecondHouse || p.Destination != p.FirstHouse && p.Destination != p.SecondHouse
                    || _byHero.ContainsKey(p.First) || _byHero.ContainsKey(p.Second))
                { _pledges.RemoveAt(i--); continue; }
                _byHero.Add(p.First, p);
                _byHero.Add(p.Second, p);
                if (!p.Accepted) { if (p.IsPlayerPledge) _incomingOffer = p; continue; }
                _pledgedHouses.Add((p.FirstHouse, p.SecondHouse));
                _pledgedHouses.Add((p.SecondHouse, p.FirstHouse));
            }
        }

        internal BetrothalRecord Find(Hero hero) => IsCampaignThread && hero != null && _byHero.TryGetValue(hero, out var p)
            && IdentityValid(p) ? p : null;

        internal static bool IsReserved(Hero hero) => Instance?.Find(hero) != null;
        internal static bool BlocksWedding(Hero first, Hero second) => (_wedding == null
            || !_wedding.Includes(first) || !_wedding.Includes(second) || first == second)
            && (IsReserved(first) || IsReserved(second));
        internal static bool BlocksParticipant(Hero hero) => _wedding?.Includes(hero) != true && IsReserved(hero);
        internal bool HasAcceptedPledge(Clan first, Clan second) => IsCampaignThread && _pledgedHouses.Contains((first, second));
        internal bool HasIncomingOffer => IsIncomingOfferCurrent(_incomingOffer);

        internal bool IsIncomingOfferCurrent(BetrothalRecord pledge) => IsCampaignThread && pledge != null
            && pledge == _incomingOffer && !pledge.Accepted && pledge.IsPlayerPledge && Day <= pledge.OfferExpiresDay;

        private static bool PersonBelongs(Hero hero, Clan house) => hero?.IsAlive == true && hero.Clan == house
            && hero.Spouse == null && HouseholdMembership.IsFamily(hero) && hero.IsLord
            && RegencyBehavior.Instance?.IsGeneratedRegent(hero) != true;

        private static bool HouseExists(Clan clan) => clan != null && !clan.IsEliminated && !clan.IsBanditFaction
            && (clan == Clan.PlayerClan || !clan.IsMinorFaction && !clan.IsClanTypeMercenary);

        private static bool IdentityValid(BetrothalRecord p) => HouseExists(p.FirstHouse) && HouseExists(p.SecondHouse)
            && PersonBelongs(p.First, p.FirstHouse) && PersonBelongs(p.Second, p.SecondHouse);

        internal static bool Potential(Hero hero) => hero?.Clan != null && HouseExists(hero.Clan) && PersonBelongs(hero, hero.Clan)
            && !hero.IsDisabled && !hero.IsPrisoner && !IsReserved(hero) && !StrategicMarriageBehavior.HasMarriageOfferFor(hero)
            && Campaign.Current?.GetCampaignBehavior<StrategicMarriageBehavior>()?.HasPendingProspect(hero) != true
            && CourtAgendaBehavior.Current?.HasDynasticReservation(hero, hero) != true
            && RegencyBehavior.Instance?.IsActingRegent(hero) != true
            && CrownAccessionBehavior.Instance?.IsHouseholdReservedForMarriage(hero) != true;

        internal static bool CompatiblePair(Hero first, Hero second) => first != null && second != null && first != second
            && first.Clan != null && second.Clan != null && first.Clan != second.Clan && first.IsFemale != second.IsFemale
            && (first.IsChild || second.IsChild) && !AtWar(first.Clan, second.Clan) && !KinWithin(first, second, 3)
            && (Romance.GetCourtedHeroInOtherClan(first, second) is Hero firstChoice ? firstChoice == second : true)
            && (Romance.GetCourtedHeroInOtherClan(second, first) is Hero secondChoice ? secondChoice == first : true);

        // Mirror the native three-generation kinship gate without bypassing its adulthood check.
        private static bool KinWithin(Hero first, Hero second, int depth)
        {
            bool Contains(Hero ancestor, Hero descendant, int left) => descendant != null && (ancestor == descendant
                || left > 0 && (Contains(ancestor, descendant.Father, left - 1) || Contains(ancestor, descendant.Mother, left - 1)));
            bool Compare(Hero ancestor, int left) => ancestor != null && (Contains(ancestor, second, depth)
                || left > 0 && (Compare(ancestor.Father, left - 1) || Compare(ancestor.Mother, left - 1)));
            return Compare(first, depth);
        }

        private static bool AtWar(Clan first, Clan second) => first.IsAtWarWith(second)
            || first.Kingdom != null && second.Kingdom != null && first.Kingdom.IsAtWarWith(second.Kingdom);

        internal static bool CanSetDestination(Hero first, Hero second, Clan destination, MarriageHouseholdPolicy policy)
        {
            if (destination == null || destination != first.Clan && destination != second.Clan) return false;
            Hero leaving = destination == first.Clan ? second : first;
            return leaving != Hero.MainHero && !policy.MustRemain(leaving) && !policy.IsLastContinuation(leaving);
        }

        internal bool Create(Hero first, Hero second, Clan destination, bool offerToPlayer)
        {
            if (!CanArrange || !Potential(first) || !Potential(second) || !CompatiblePair(first, second)
                || _byHero.ContainsKey(first) || _byHero.ContainsKey(second)
                || !CanSetDestination(first, second, destination, new MarriageHouseholdPolicy())) return false;
            var p = new BetrothalRecord { First = first, Second = second, FirstHouse = first.Clan, SecondHouse = second.Clan,
                Destination = destination, Accepted = !offerToPlayer, OfferExpiresDay = Day + 7, NextReviewDay = Day };
            if (offerToPlayer && (!p.IsPlayerPledge || HasIncomingOffer)) return false;
            _pledges.Add(p);
            Changed(p, offerToPlayer ? "offered" : "pledged");
            return true;
        }

        private bool AcceptOffer(BetrothalRecord p)
        {
            if (!_pledges.Contains(p) || p.Accepted || Day > p.OfferExpiresDay || !CanArrange || !p.IsPlayerPledge
                || !IdentityValid(p) || !CompatiblePair(p.First, p.Second)
                || !CanSetDestination(p.First, p.Second, p.Destination, new MarriageHouseholdPolicy())) return false;
            p.Accepted = true;
            Changed(p, "pledged");
            return true;
        }

        private void Changed(BetrothalRecord p, string result)
        {
            Reindex();
            if (p.Accepted) DynamicRelationBehavior.Instance?.InvalidateBaselineCache();
            BellumCivileLogger.Log($"Betrothal {result}; first={p.First?.StringId}; second={p.Second?.StringId}; house={p.Destination?.StringId}.");
        }

        private bool End(BetrothalRecord p, string reason, Clan breakingHouse = null)
        {
            if (!_pledges.Remove(p)) return false;
            Changed(p, reason);
            if (p.Accepted && breakingHouse != null && p.Involves(breakingHouse))
            {
                Clan other = breakingHouse == p.FirstHouse ? p.SecondHouse : p.FirstHouse;
                Hero breaker = breakingHouse == Clan.PlayerClan ? Hero.MainHero : breakingHouse.Leader;
                Hero offended = other == Clan.PlayerClan ? Hero.MainHero : other.Leader;
                RelationMemoryService.ApplyChange(breaker, offended, BetrothalRules.BrokenPledgeRelation, p.IsPlayerPledge,
                    RelationMemorySources.BrokeBetrothal, BetrothalRules.BrokenPledgeYears);
            }
            return true;
        }

        private void ReviewPledges()
        {
            if (_pledges.Count == 0) return;
            foreach (var p in _pledges.ToArray())
            {
                if (!IdentityValid(p))
                {
                    if (End(p, "house_or_participant_changed") && p.Accepted) ShowPledgeLapsed(p);
                    continue;
                }
                if (!p.Accepted)
                {
                    if (Day > p.OfferExpiresDay || !CanArrange || !CompatiblePair(p.First, p.Second)) End(p, "offer_lapsed");
                    continue;
                }

                // A brief diplomatic shock is not enough to undo an accepted pledge.
                float relation = p.FirstHouse.Leader != null && p.SecondHouse.Leader != null
                    ? p.FirstHouse.Leader.GetRelation(p.SecondHouse.Leader) : 0;
                p.HostilityStartedDay = BetrothalRules.TrackHostility(p.HostilityStartedDay, Day, relation);
                if (BetrothalRules.MayEndForHostility(p.HostilityStartedDay, Day))
                {
                    var breakingHouse = p.IsPlayerPledge ? p.OtherPlayerHouse : p.FirstHouse;
                    End(p, "renounced_after_hostility", breakingHouse);
                    ShowPledgeLapsed(p);
                    continue;
                }
                ReviewWedding(p);
            }
        }

        private static bool BothOfAge(BetrothalRecord p) => !p.First.IsChild && !p.Second.IsChild;

        private void ReviewWedding(BetrothalRecord p)
        {
            if (!BothOfAge(p) || Day < p.NextReviewDay) return;
            p.NextReviewDay = Day + BetrothalRules.WeddingRetryDays;
            if (TemporaryBlocker(p)) { DelayWedding(p); return; }
            if (!CanSetDestination(p.First, p.Second, p.Destination, new MarriageHouseholdPolicy()))
            { if (End(p, "household_terms_no_longer_possible")) ShowPledgeLapsed(p); return; }
            if (!CanExecuteWedding(p)) { DelayWedding(p); return; }
            if (!p.IsPlayerPledge || p.PlayerConsentedToWedding) CompleteWedding(p);
            else p.FirstDelayedDay = -1;
        }

        private void DelayWedding(BetrothalRecord p)
        {
            if (p.FirstDelayedDay < 0) p.FirstDelayedDay = Day;
            if (BetrothalRules.DelayExpired(p.FirstDelayedDay, Day) && End(p, "wedding_remained_unavailable")) ShowPledgeLapsed(p);
        }

        private static bool TemporaryBlocker(BetrothalRecord p) => AtWar(p.FirstHouse, p.SecondHouse)
            || p.FirstHouse != Clan.PlayerClan && p.FirstHouse.Kingdom == null || p.SecondHouse != Clan.PlayerClan && p.SecondHouse.Kingdom == null
            || RegencyBehavior.Instance?.IsActingRegent(p.First) == true || RegencyBehavior.Instance?.IsActingRegent(p.Second) == true
            || CrownAccessionBehavior.Instance?.IsHouseholdReservedForMarriage(p.First) == true
            || CrownAccessionBehavior.Instance?.IsHouseholdReservedForMarriage(p.Second) == true
            || !BellumMarriageStrategyHelper.MarriageParticipantReady(p.First) || !BellumMarriageStrategyHelper.MarriageParticipantReady(p.Second);

        private static bool CanExecuteWedding(BetrothalRecord p) => WithWeddingScope(p, marry: false);

        private static bool WithWeddingScope(BetrothalRecord p, bool marry)
        {
            if (_wedding != null || !IdentityValid(p) || !BothOfAge(p)) return false;
            _wedding = p;
            try
            {
                var model = Campaign.Current?.Models?.MarriageModel;
                using (new NpcMarriageClanContext(p.First, p.Second, p.Destination))
                {
                    if (model == null || !model.IsCoupleSuitableForMarriage(p.First, p.Second)
                        || model.GetClanAfterMarriage(p.First, p.Second) != p.Destination) return false;
                    if (marry) MarriageAction.Apply(p.First, p.Second);
                    return !marry || p.First.Spouse == p.Second && p.Second.Spouse == p.First;
                }
            }
            finally { _wedding = null; }
        }

        private void CompleteWedding(BetrothalRecord p)
        {
            if (!_pledges.Contains(p) || !p.Accepted || p.IsPlayerPledge && !p.PlayerConsentedToWedding) return;
            bool married = false;
            try { married = WithWeddingScope(p, marry: true); }
            catch (Exception ex) { BellumCivileLogger.Log($"Betrothal wedding failed; first={p.First.StringId}; second={p.Second.StringId}; error={ex}"); }
            if (married || p.First.Spouse != null || p.Second.Spouse != null)
                End(p, married ? "married" : "wedding_already_changed_state");
            else DelayWedding(p);
        }
    }
}
