using System.Collections.Generic;
using System.Linq;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

namespace BellumCivile.Behaviors
{
    public sealed class RetainedTreatyPrisonerBehavior : CampaignBehaviorBase
    {
        private List<string> _retainedHeroIds = new List<string>();
        private int _yearlyNewlyRetained;
        private int _yearlyPeaceReleasesBlocked;
        private int _yearlyTreatyReleased;
        private int _yearlyEscaped;
        private int _yearlyRansomed;
        private int _yearlyOtherReleased;
        private int _yearlyStaleRemoved;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnHeroPrisonerReleased);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_RetainedTreatyPrisoners", ref _retainedHeroIds);
            dataStore.SyncData("BellumCivile_RetainedPrisonersYearlyNew", ref _yearlyNewlyRetained);
            dataStore.SyncData("BellumCivile_RetainedPrisonersYearlyPeaceBlocked", ref _yearlyPeaceReleasesBlocked);
            dataStore.SyncData("BellumCivile_RetainedPrisonersYearlyTreatyReleased", ref _yearlyTreatyReleased);
            dataStore.SyncData("BellumCivile_RetainedPrisonersYearlyEscaped", ref _yearlyEscaped);
            dataStore.SyncData("BellumCivile_RetainedPrisonersYearlyRansomed", ref _yearlyRansomed);
            dataStore.SyncData("BellumCivile_RetainedPrisonersYearlyOtherReleased", ref _yearlyOtherReleased);
            dataStore.SyncData("BellumCivile_RetainedPrisonersYearlyStaleRemoved", ref _yearlyStaleRemoved);
            EnsureInitialized();
        }

        public RetainedPrisonerYearlyTelemetry ConsumeYearlyTelemetry()
        {
            EnsureInitialized();
            RetainedPrisonerYearlyTelemetry result = new RetainedPrisonerYearlyTelemetry(
                _yearlyNewlyRetained,
                _yearlyPeaceReleasesBlocked,
                _yearlyTreatyReleased,
                _yearlyEscaped,
                _yearlyRansomed,
                _yearlyOtherReleased,
                _yearlyStaleRemoved,
                _retainedHeroIds.Count);
            _yearlyNewlyRetained = 0;
            _yearlyPeaceReleasesBlocked = 0;
            _yearlyTreatyReleased = 0;
            _yearlyEscaped = 0;
            _yearlyRansomed = 0;
            _yearlyOtherReleased = 0;
            _yearlyStaleRemoved = 0;
            return result;
        }

        public bool IsRetained(Hero hero)
        {
            EnsureInitialized();
            return hero != null && _retainedHeroIds.Contains(hero.StringId);
        }

        public void RetainForeignPrisoners(Kingdom first, Kingdom second)
        {
            if (first == null || second == null)
                return;

            EnsureInitialized();
            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                if (!IsEligibleForeignPrisoner(hero, first, second))
                    continue;
                if (!_retainedHeroIds.Contains(hero.StringId))
                {
                    _retainedHeroIds.Add(hero.StringId);
                    _yearlyNewlyRetained++;
                    BellumCivileLogger.Log($"Treaty prisoner retained; hero={hero.StringId}; captive_realm={hero.Clan?.Kingdom?.StringId}; captor_realm={GetCaptorKingdom(hero)?.StringId}.");
                }
            }
        }

        public void RecordBlockedPeaceRelease(Hero hero)
        {
            if (IsRetained(hero))
                _yearlyPeaceReleasesBlocked++;
        }

        public bool ReleaseByTreaty(Hero hero)
        {
            if (HostageCustodyGuard.IsProtected(hero)) return false;
            if (hero == null || !hero.IsPrisoner)
                return false;

            EnsureInitialized();
            Kingdom captorKingdom = GetCaptorKingdom(hero);
            Hero gratefulLeader = hero.Clan?.Leader;
            Hero releasingRuler = captorKingdom?.RulingClan?.Leader;
            if (gratefulLeader != null
                && releasingRuler != null
                && gratefulLeader != releasingRuler)
            {
                RelationMemoryService.ApplyChange(
                    gratefulLeader,
                    releasingRuler,
                    BellumCivileConstants.TreatyPrisonerReleaseRelationGain,
                    gratefulLeader == Hero.MainHero || releasingRuler == Hero.MainHero,
                    RelationMemorySources.ReleasedMyKinsman,
                    8f,
                    RelationMemoryScope.House,
                    hero.Name?.ToString());
            }

            _retainedHeroIds.Remove(hero.StringId);
            _yearlyTreatyReleased++;
            EndCaptivityAction.ApplyByReleasedByChoice(hero);
            BellumCivileLogger.Log($"Treaty prisoner released by negotiated term; hero={hero.StringId}.");
            return true;
        }

        public bool TryHandleRetainedEscape(Hero hero)
        {
            if (HostageCustodyGuard.IsProtected(hero)) return true;
            if (!IsRetained(hero))
                return false;

            PartyBase captor = hero?.PartyBelongedToAsPrisoner;
            if (hero == null || !hero.IsAlive || !hero.IsPrisoner || captor == null || hero == Hero.MainHero)
            {
                _retainedHeroIds.Remove(hero?.StringId ?? string.Empty);
                return true;
            }

            bool travellingWithParty = captor.IsMobile && captor.MobileParty.CurrentSettlement == null;
            float baseChance = travellingWithParty
                ? BellumCivileOptions.RetainedPrisonerMobileEscapeChance
                : BellumCivileOptions.RetainedPrisonerDungeonEscapeChance;
            if (IsHeldByPlayer(captor))
                baseChance *= 0.5f;

            ExplainedNumber chance = new ExplainedNumber(baseChance);
            ApplyVanillaPerkModifiers(hero, captor, ref chance);
            float result = System.Math.Max(0f, System.Math.Min(1f, chance.ResultNumber));
            if (MBRandom.RandomFloat < result)
            {
                _retainedHeroIds.Remove(hero.StringId);
                _yearlyEscaped++;
                EndCaptivityAction.ApplyByEscape(hero);
            }
            return true;
        }

        public static Kingdom GetCaptorKingdom(Hero hero)
        {
            IFaction faction = hero?.PartyBelongedToAsPrisoner?.MapFaction;
            if (faction is Kingdom kingdom)
                return kingdom;
            return (faction as Clan)?.Kingdom;
        }

        public static Settlement GetHoldingSettlement(Hero hero)
        {
            PartyBase captor = hero?.PartyBelongedToAsPrisoner;
            return captor?.IsSettlement == true ? captor.Settlement : null;
        }

        public static bool IsHeldInSettlementDungeon(Hero hero, Settlement settlement)
        {
            PartyBase captor = hero?.PartyBelongedToAsPrisoner;
            return settlement != null
                && captor?.IsSettlement == true
                && captor.Settlement == settlement;
        }

        private void OnDailyTick()
        {
            EnsureInitialized();
            foreach (string heroId in _retainedHeroIds.ToList())
            {
                Hero hero = ResolveHero(heroId);
                if (hero == null || !hero.IsAlive || !hero.IsPrisoner || hero.PartyBelongedToAsPrisoner == null)
                {
                    _retainedHeroIds.Remove(heroId);
                    _yearlyStaleRemoved++;
                    continue;
                }

                if (!BellumCivileOptions.EnableWarPeaceLogicRevamp)
                {
                    Kingdom captor = GetCaptorKingdom(hero);
                    Kingdom captiveRealm = hero.Clan?.Kingdom;
                    _retainedHeroIds.Remove(heroId);
                    if (captor != null && captiveRealm != null && !captor.IsAtWarWith(captiveRealm))
                        EndCaptivityAction.ApplyByPeace(hero);
                }
            }
        }

        private void OnHeroPrisonerReleased(Hero prisoner, PartyBase party, IFaction capturerFaction, EndCaptivityDetail detail, bool showNotification)
        {
            if (prisoner == null || _retainedHeroIds?.Remove(prisoner.StringId) != true)
                return;

            if (detail == EndCaptivityDetail.Ransom)
            {
                _yearlyRansomed++;
                BellumCivileLogger.Log($"Retained treaty prisoner ransomed; hero={prisoner.StringId}; captor={capturerFaction?.StringId ?? "unknown"}.");
            }
            else
            {
                _yearlyOtherReleased++;
            }
        }

        private static bool IsEligibleForeignPrisoner(Hero hero, Kingdom first, Kingdom second)
        {
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive || !hero.IsPrisoner || hero.Clan?.Kingdom == null)
                return false;
            Kingdom captiveRealm = hero.Clan.Kingdom;
            Kingdom captorRealm = GetCaptorKingdom(hero);
            return (captiveRealm == first && captorRealm == second)
                || (captiveRealm == second && captorRealm == first);
        }

        private static bool IsHeldByPlayer(PartyBase captor)
        {
            if (captor == PartyBase.MainParty)
                return true;
            if (captor.IsSettlement)
                return captor.Settlement.OwnerClan == Clan.PlayerClan;
            if (captor.IsMobile && captor.MobileParty.CurrentSettlement != null)
                return captor.MobileParty.CurrentSettlement.OwnerClan == Clan.PlayerClan;
            return false;
        }

        private static void ApplyVanillaPerkModifiers(Hero hero, PartyBase captor, ref ExplainedNumber chance)
        {
            if (captor.IsSettlement && captor.Settlement.Town?.Governor != null)
            {
                Town town = captor.Settlement.Town;
                Hero governor = town.Governor;
                if (governor.GetPerkValue(DefaultPerks.Roguery.SweetTalker))
                    chance.AddFactor(DefaultPerks.Roguery.SweetTalker.SecondaryBonus, DefaultPerks.Roguery.SweetTalker.Description);
                if (governor.GetPerkValue(DefaultPerks.Engineering.DungeonArchitect))
                    chance.AddFactor(DefaultPerks.Engineering.DungeonArchitect.SecondaryBonus, DefaultPerks.Engineering.DungeonArchitect.Description);
                if (governor.GetPerkValue(DefaultPerks.Riding.MountedPatrols))
                    chance.AddFactor(DefaultPerks.Riding.MountedPatrols.SecondaryBonus, DefaultPerks.Riding.MountedPatrols.Description);
            }

            if (!captor.IsMobile)
                return;
            MobileParty party = captor.MobileParty;
            if (hero.GetPerkValue(DefaultPerks.Roguery.FleetFooted))
                chance.AddFactor(DefaultPerks.Roguery.FleetFooted.SecondaryBonus);
            if (party.HasPerk(DefaultPerks.Riding.MountedPatrols))
                PerkHelper.AddPerkBonusForParty(DefaultPerks.Riding.MountedPatrols, party, true, ref chance);
            if (party.HasPerk(DefaultPerks.Roguery.RansomBroker))
                PerkHelper.AddPerkBonusForParty(DefaultPerks.Roguery.RansomBroker, party, false, ref chance);
            if (!party.IsCurrentlyAtSea)
                PerkHelper.AddPerkBonusForParty(DefaultPerks.Scouting.KeenSight, party, false, ref chance);
        }

        private static Hero ResolveHero(string heroId)
        {
            return Hero.AllAliveHeroes.FirstOrDefault(hero => hero?.StringId == heroId);
        }

        private void EnsureInitialized()
        {
            if (_retainedHeroIds == null)
                _retainedHeroIds = new List<string>();
        }
    }

    public sealed class RetainedPrisonerYearlyTelemetry
    {
        public int NewlyRetained { get; }
        public int PeaceReleasesBlocked { get; }
        public int TreatyReleased { get; }
        public int Escaped { get; }
        public int Ransomed { get; }
        public int OtherReleased { get; }
        public int StaleRemoved { get; }
        public int CurrentlyRetained { get; }

        public RetainedPrisonerYearlyTelemetry(
            int newlyRetained,
            int peaceReleasesBlocked,
            int treatyReleased,
            int escaped,
            int ransomed,
            int otherReleased,
            int staleRemoved,
            int currentlyRetained)
        {
            NewlyRetained = newlyRetained;
            PeaceReleasesBlocked = peaceReleasesBlocked;
            TreatyReleased = treatyReleased;
            Escaped = escaped;
            Ransomed = ransomed;
            OtherReleased = otherReleased;
            StaleRemoved = staleRemoved;
            CurrentlyRetained = currentlyRetained;
        }
    }
}
