using System;
using System.Collections.Generic;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;

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
                if (!HasPledgeStructure(p)
                    || _byHero.ContainsKey(p.First) || _byHero.ContainsKey(p.Second))
                {
                    // A malformed saved agreement must not silently discard funded escrow.
                    if ((p?.HeldDeposit ?? 0) == 0) _pledges.RemoveAt(i--);
                    continue;
                }
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

        private static bool HasPledgeStructure(BetrothalRecord p) => p?.First != null && p.Second != null && p.First != p.Second
            && p.FirstHouse != null && p.SecondHouse != null && p.FirstHouse != p.SecondHouse
            && (p.Destination == p.FirstHouse || p.Destination == p.SecondHouse);

        private static bool IdentityValid(BetrothalRecord p) => HasPledgeStructure(p) && HouseExists(p.FirstHouse) && HouseExists(p.SecondHouse)
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

        internal bool Create(Hero first, Hero second, Clan destination, bool offerToPlayer) =>
            CreateWithTerms(first, second, destination, offerToPlayer);

        internal static int BetrothalDowry(Hero first, Hero second, Clan destination, MarriageHouseholdPolicy policy)
        {
            if (first?.Clan == null || second?.Clan == null || destination != first.Clan && destination != second.Clan) return 0;
            bool firstReceives = destination == first.Clan;
            return MarriagePayments.Quote(firstReceives ? second : first, firstReceives ? first : second, policy);
        }

        private bool CreateWithTerms(Hero first, Hero second, Clan destination, bool offerToPlayer, int? dowry = null)
        {
            var policy = new MarriageHouseholdPolicy();
            if (!CanArrange || !Potential(first) || !Potential(second) || !CompatiblePair(first, second)
                || _byHero.ContainsKey(first) || _byHero.ContainsKey(second)
                || !CanSetDestination(first, second, destination, policy)) return false;
            var p = new BetrothalRecord { First = first, Second = second, FirstHouse = first.Clan, SecondHouse = second.Clan,
                Destination = destination, Dowry = dowry ?? BetrothalDowry(first, second, destination, policy),
                OfferExpiresDay = Day + 7, NextReviewDay = Day };
            if (offerToPlayer && (!p.IsPlayerPledge || HasIncomingOffer)) return false;
            if (!CanFundDeposit(p)) return false;
            if (!offerToPlayer && !FundPledge(p)) return false;
            _pledges.Add(p);
            Changed(p, offerToPlayer ? "offered" : "pledged");
            if (offerToPlayer)
                MarriagePayments.TraceQuote("betrothal offer quoted", p.First, p.Second, p.Destination, p.SendingHouse, p.Dowry, p.Deposit);
            if (p.Accepted) ShowBetrothalOutcome(p, "pledged", null);
            return true;
        }

        private bool AcceptOffer(BetrothalRecord p)
        {
            if (!_pledges.Contains(p) || p.Accepted || Day > p.OfferExpiresDay || !CanArrange || !p.IsPlayerPledge
                || !IdentityValid(p) || !CompatiblePair(p.First, p.Second)
                || !CanSetDestination(p.First, p.Second, p.Destination, new MarriageHouseholdPolicy())) return false;
            if (!FundPledge(p)) return false;
            Changed(p, "pledged");
            ShowBetrothalOutcome(p, "pledged", null);
            return true;
        }

        internal static bool CanFundDeposit(BetrothalRecord p) => p.Deposit == 0
            || MarriagePayments.CanPay(p.Destination, p.Deposit) && MarriagePayments.Wallet(p.SendingHouse) != null;

        private static bool FundPledge(BetrothalRecord p)
        {
            if (p.Accepted || !CanFundDeposit(p)) return false;
            p.SendingHouseHadUrgentNeed = p.SendingHouse == Clan.PlayerClan || HasUrgentDeparture(p);
            p.OriginalDepositor = MarriagePayments.Wallet(p.Destination);
            MarriagePayments.Move(p.OriginalDepositor, null, p.Deposit);
            p.HeldDeposit = p.Deposit;
            p.Accepted = true;
            if (p.HeldDeposit > 0 && BellumCivileDebug.ShowInGameMessages)
                BellumCivileDebug.Trace("marriage",
                    $"betrothal deposit held; first={p.First.StringId}; second={p.Second.StringId}; payer={p.Destination.StringId}; "
                    + $"payee={p.SendingHouse.StringId}; dowry={p.Dowry}; escrow={p.HeldDeposit}; wedding_balance={p.Balance}; "
                    + $"payer_gold_after={p.OriginalDepositor.Gold}.", requestInGameDisplay: true);
            return true;
        }

        private static bool HasUrgentDeparture(BetrothalRecord p) =>
            BellumMarriageStrategyHelper.PlayerMarriageHouseRisk(p.SendingHouse) >= .75f
            && new MarriageHouseholdPolicy().IsContinuation(p.Departing);

        private static Hero RefundWallet(BetrothalRecord p) =>
            MarriagePayments.Wallet(p.Destination) ?? (p.OriginalDepositor?.IsAlive == true ? p.OriginalDepositor : null);

        private static bool CanSettleEscrow(BetrothalRecord p, Clan breakingHouse)
        {
            if (p.HeldDeposit <= 0) return true;
            if (breakingHouse != null && breakingHouse == p.Destination)
                return MarriagePayments.HasRoom(MarriagePayments.Wallet(p.SendingHouse), p.HeldDeposit);
            int premium = breakingHouse == null ? 0 : MarriagePayments.RenunciationPremium(p.HeldDeposit);
            Hero refund = RefundWallet(p);
            return (refund == null && breakingHouse == null && (p.Destination == null || p.Destination.IsEliminated)
                    || MarriagePayments.HasRoom(refund, p.HeldDeposit + premium))
                && (breakingHouse == null || MarriagePayments.CanPay(breakingHouse, premium));
        }

        private static void SettleEscrow(BetrothalRecord p, Clan breakingHouse)
        {
            int held = p.HeldDeposit;
            p.HeldDeposit = 0;
            if (held <= 0) return;
            bool forfeited = breakingHouse != null && breakingHouse == p.Destination;
            Hero beneficiary = forfeited ? MarriagePayments.Wallet(p.SendingHouse) : RefundWallet(p);
            int premium = breakingHouse != null && !forfeited ? MarriagePayments.RenunciationPremium(held) : 0;
            MarriagePayments.Move(premium > 0 ? MarriagePayments.Wallet(breakingHouse) : null, beneficiary, premium);
            if (beneficiary != null) MarriagePayments.Move(null, beneficiary, held);
            if (BellumCivileDebug.ShowInGameMessages)
                BellumCivileDebug.Trace("marriage",
                    $"betrothal escrow settled; first={p.First?.StringId}; second={p.Second?.StringId}; breaker={breakingHouse?.StringId ?? "none"}; "
                    + $"payer={p.Destination?.StringId}; payee={p.SendingHouse?.StringId}; credited_hero={beneficiary?.StringId ?? "none"}; dowry={p.Dowry}; "
                    + $"refunded={(!forfeited && beneficiary != null ? held : 0)}; forfeited={(forfeited ? held : 0)}; "
                    + $"premium={premium}; unclaimed={(beneficiary == null ? held : 0)}.", requestInGameDisplay: true);
            ShowBetrothalSettlement(p, breakingHouse, held);
        }

        private void Changed(BetrothalRecord p, string result)
        {
            Reindex();
            if (p.Accepted) DynamicRelationBehavior.Instance?.InvalidateBaselineCache();
            BellumCivileLogger.Log($"Betrothal {result}; first={p.First?.StringId}; second={p.Second?.StringId}; house={p.Destination?.StringId}; dowry={p.Dowry}; held={p.HeldDeposit}.");
        }

        private bool End(BetrothalRecord p, string reason, Clan breakingHouse = null)
        {
            if (!_pledges.Contains(p) || p.Accepted && !CanSettleEscrow(p, breakingHouse)) return false;
            if (!_pledges.Remove(p)) return false;
            if (p.Accepted) SettleEscrow(p, breakingHouse);
            Changed(p, reason);
            if (p.Accepted && breakingHouse != null && p.Involves(breakingHouse))
            {
                Clan other = breakingHouse == p.FirstHouse ? p.SecondHouse : p.FirstHouse;
                Hero breaker = breakingHouse == Clan.PlayerClan ? Hero.MainHero : breakingHouse.Leader;
                Hero offended = other == Clan.PlayerClan ? Hero.MainHero : other.Leader;
                RelationMemoryService.ApplyChange(breaker, offended, BetrothalRules.BrokenPledgeRelation, p.IsPlayerPledge,
                    RelationMemorySources.BrokeBetrothal, BetrothalRules.BrokenPledgeYears);
            }
            if (p.Accepted && reason != "married")
            {
                if (breakingHouse == null) ShowPledgeLapsed(p);
                else ShowBetrothalOutcome(p, "renounced", breakingHouse);
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
                    End(p, "house_or_participant_changed");
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
                    // Hostility alone is not a deliberate breach. A dishonorable NPC may renege to
                    // retain a family member only if the need arose after it gave its word.
                    Clan sending = p.SendingHouse;
                    bool npcRenounces = sending != Clan.PlayerClan && !p.SendingHouseHadUrgentNeed
                        && sending.Leader?.GetTraitLevel(DefaultTraits.Honor) < 0
                        && CanSetDestination(p.First, p.Second, p.Destination, new MarriageHouseholdPolicy()) && HasUrgentDeparture(p);
                    if (!npcRenounces || !End(p, "npc_renounced", sending)) End(p, "lapsed_after_hostility");
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
            { End(p, "household_terms_no_longer_possible"); return; }
            if (!CanFundWedding(p) || !CanExecuteWedding(p)) { DelayWedding(p); return; }
            if (!p.IsPlayerPledge || p.PlayerConsentedToWedding) CompleteWedding(p);
            else p.FirstDelayedDay = -1;
        }

        private void DelayWedding(BetrothalRecord p)
        {
            if (p.FirstDelayedDay < 0) p.FirstDelayedDay = Day;
            if (BetrothalRules.DelayExpired(p.FirstDelayedDay, Day)) End(p, "wedding_remained_unavailable");
        }

        private static bool TemporaryBlocker(BetrothalRecord p) => AtWar(p.FirstHouse, p.SecondHouse)
            || p.FirstHouse != Clan.PlayerClan && p.FirstHouse.Kingdom == null || p.SecondHouse != Clan.PlayerClan && p.SecondHouse.Kingdom == null
            || RegencyBehavior.Instance?.IsActingRegent(p.First) == true || RegencyBehavior.Instance?.IsActingRegent(p.Second) == true
            || CrownAccessionBehavior.Instance?.IsHouseholdReservedForMarriage(p.First) == true
            || CrownAccessionBehavior.Instance?.IsHouseholdReservedForMarriage(p.Second) == true
            || !BellumMarriageStrategyHelper.MarriageParticipantReady(p.First) || !BellumMarriageStrategyHelper.MarriageParticipantReady(p.Second);

        private static bool CanExecuteWedding(BetrothalRecord p) => WithWeddingScope(p, marry: false);
        private static bool CanFundWedding(BetrothalRecord p) =>
            MarriagePayments.CanFundWedding(p.Destination, p.SendingHouse, p.Balance, p.HeldDeposit);

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
            var payment = MarriagePayments.Reserve(p.First, p.Second, p.Destination, p.SendingHouse, p.Balance, p.HeldDeposit);
            if (payment == null) { DelayWedding(p); return; }
            try { using (payment) WithWeddingScope(p, marry: true); }
            catch (Exception ex) { BellumCivileLogger.Log($"Betrothal wedding failed; first={p.First.StringId}; second={p.Second.StringId}; error={ex}"); }
            if (payment.Settled)
            {
                int released = p.HeldDeposit;
                p.HeldDeposit = 0;
                if (End(p, "married")) ShowBetrothalWeddingPayment(p, released);
            }
            else if (p.First.Spouse != null || p.Second.Spouse != null) End(p, "wedding_already_changed_state");
            else DelayWedding(p);
        }
    }
}
