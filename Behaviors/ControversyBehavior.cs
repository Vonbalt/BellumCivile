using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Routes military and diplomatic failures into the responsible Privy Council office.
    /// The legacy kingdom-wide storage remains only to migrate older saves safely.
    /// </summary>
    public class ControversyBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, int> _trackedControversyById = new Dictionary<string, int>();
        private Dictionary<string, int> _dailyDynamicCacheById = new Dictionary<string, int>();

        private static MethodInfo _getDailyTributeMethod;
        private static PropertyInfo _dailyTributeProp;
        private static Dictionary<string, PropertyInfo> _boolPropsCache = new Dictionary<string, PropertyInfo>();
        private static bool _stanceReflectionInitialized = false;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(this, OnRulingClanChanged);

            SubscribeToMobilePartyDestroyedSafely();
            CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
        }

        public override void SyncData(IDataStore dataStore)
        {
            Dictionary<Kingdom, int> legacyTrackedControversy = new Dictionary<Kingdom, int>();
            dataStore.SyncData("BellumCivile_Controversy", ref legacyTrackedControversy);
            dataStore.SyncData("BellumCivile_ControversyById", ref _trackedControversyById);

            if (_trackedControversyById == null) _trackedControversyById = new Dictionary<string, int>();

            if (legacyTrackedControversy != null)
            {
                foreach (KeyValuePair<Kingdom, int> entry in legacyTrackedControversy)
                {
                    if (entry.Key == null || string.IsNullOrEmpty(entry.Key.StringId)) continue;
                    _trackedControversyById[entry.Key.StringId] = entry.Value;
                }
            }
        }

        private void AddControversy(Kingdom kingdom, int amount, string reason = null)
        {
            AddControversy(kingdom, PrivyCouncilOffice.Marshal, amount, reason);
        }

        private void AddControversy(Kingdom kingdom, PrivyCouncilOffice office, int amount, string reason = null)
        {
            if (kingdom == null || string.IsNullOrEmpty(kingdom.StringId)) return;

            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (council != null)
            {
                council.AddControversy(kingdom, office, amount, reason ?? GetCouncilReason(office));
                return;
            }

            amount = ScaleControversy(amount);
            if (amount == 0) return;

            string kingdomId = kingdom.StringId;
            if (!_trackedControversyById.ContainsKey(kingdomId)) _trackedControversyById[kingdomId] = 0;

            int newValue = _trackedControversyById[kingdomId] + amount;
            if (newValue > 100) newValue = 100;
            else if (newValue < 0) newValue = 0;

            _trackedControversyById[kingdomId] = newValue;
        }

        private void PruneTrackedControversy()
        {
            CivilWarResolutionBehavior resolutionBehavior = Campaign.Current?.GetCampaignBehavior<CivilWarResolutionBehavior>();
            HashSet<string> liveKingdomIds = new HashSet<string>(
                Kingdom.All
                    .Where(k => k != null && !k.IsEliminated && !string.IsNullOrEmpty(k.StringId))
                    .Select(k => k.StringId));

            foreach (string kingdomId in _trackedControversyById.Keys.ToList())
            {
                if (liveKingdomIds.Contains(kingdomId)) continue;
                if (resolutionBehavior != null && resolutionBehavior.IsKingdomIdReferencedByBellumCivileState(kingdomId)) continue;
                _trackedControversyById.Remove(kingdomId);
            }
        }

        private void OnWeeklyTick()
        {
            PruneTrackedControversy();
        }

        private void OnDailyTick()
        {
            _dailyDynamicCacheById.Clear();

            if (Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>() != null)
                return;

            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated || string.IsNullOrEmpty(kingdom.StringId)) continue;

                string kingdomId = kingdom.StringId;
                if (_trackedControversyById.TryGetValue(kingdomId, out int trackedScore) && trackedScore > 0)
                {
                    _trackedControversyById[kingdomId] = trackedScore - 1;
                }

                _dailyDynamicCacheById[kingdomId] = CalculateDynamicState(kingdom);
            }
        }

        private void OnRulingClanChanged(Kingdom kingdom, Clan newRulingClan)
        {
            if (Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>() != null)
                return;

            if (kingdom == null || string.IsNullOrEmpty(kingdom.StringId)) return;
            if (_trackedControversyById.ContainsKey(kingdom.StringId)) _trackedControversyById[kingdom.StringId] = 0;
        }

        private void SubscribeToMobilePartyDestroyedSafely()
        {
            try
            {
                var prop = typeof(CampaignEvents).GetProperty("MobilePartyDestroyed", BindingFlags.Public | BindingFlags.Static) ??
                           typeof(CampaignEvents).GetProperty("OnMobilePartyDestroyedEvent", BindingFlags.Public | BindingFlags.Static) ??
                           typeof(CampaignEvents).GetProperty("MobilePartyDestroyedEvent", BindingFlags.Public | BindingFlags.Static);

                if (prop != null)
                {
                    var eventInstance = prop.GetValue(null);
                    if (eventInstance != null)
                    {
                        var addMethod = eventInstance.GetType().GetMethod("AddNonSerializedListener");
                        if (addMethod != null)
                        {
                            System.Action<MobileParty, PartyBase> handler = OnMobilePartyDestroyed;
                            addMethod.Invoke(eventInstance, new object[] { this, handler });
                            return;
                        }
                    }
                }
            }
            catch { }

            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEndedFallback);
        }

        private void OnMapEventEndedFallback(MapEvent mapEvent)
        {
            if (mapEvent.HasWinner && mapEvent.Winner != null)
            {
                MapEventSide loserSide = mapEvent.Winner == mapEvent.AttackerSide ? mapEvent.DefenderSide : mapEvent.AttackerSide;
                foreach (MapEventParty party in loserSide.Parties)
                {
                    if (party.Party != null && party.Party.MobileParty != null && party.Party.MobileParty.IsCaravan)
                    {
                        if (party.Party.MapFaction is Kingdom victimKingdom) AddControversy(victimKingdom, 1, "own caravan destroyed");
                        if (mapEvent.Winner.LeaderParty?.MapFaction is Kingdom attackerKingdom) AddControversy(attackerKingdom, -1, "enemy caravan destroyed");
                    }
                }
            }
        }

        private void OnMobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyerParty)
        {
            if (mobileParty != null && mobileParty.IsCaravan)
            {
                if (mobileParty.MapFaction is Kingdom victimKingdom) AddControversy(victimKingdom, 1, "own caravan destroyed");
                if (destroyerParty?.MapFaction is Kingdom attackerKingdom) AddControversy(attackerKingdom, -1, "enemy caravan destroyed");
            }
        }

        private void OnVillageLooted(Village village)
        {
            if (village.Settlement.MapFaction is Kingdom victimKingdom) AddControversy(victimKingdom, 2, "own village raided");
            if (village.Settlement.LastAttackerParty?.MapFaction is Kingdom attackerKingdom) AddControversy(attackerKingdom, -2, "enemy village raided");
        }

        private void OnHeroPrisonerTaken(PartyBase captor, Hero prisoner)
        {
            if (prisoner.Clan != null && !prisoner.Clan.IsMinorFaction)
            {
                if (prisoner.Clan.Kingdom != null) AddControversy(prisoner.Clan.Kingdom, 3);
                if (captor?.MapFaction is Kingdom captorKingdom) AddControversy(captorKingdom, -3);
            }
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if ((detail == KillCharacterAction.KillCharacterActionDetail.DiedInBattle || detail == KillCharacterAction.KillCharacterActionDetail.Executed) && victim.Clan != null && !victim.Clan.IsMinorFaction)
            {
                if (victim.Clan.Kingdom != null) AddControversy(victim.Clan.Kingdom, 5);
                if (killer?.Clan?.Kingdom != null) AddControversy(killer.Clan.Kingdom, -5);
            }
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (BellumTreatyTransferContext.IsTreatyTransfer) return;
            if (detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege)
            {
                if (oldOwner?.Clan?.Kingdom != null) AddControversy(oldOwner.Clan.Kingdom, 10);
                if (newOwner?.Clan?.Kingdom != null) AddControversy(newOwner.Clan.Kingdom, -10);
            }
        }

        private void OnMakePeace(IFaction side1Faction, IFaction side2Faction, MakePeaceAction.MakePeaceDetail detail)
        {
            if (side1Faction is Kingdom k1 && side2Faction is Kingdom k2)
            {
                StanceLink stance = k1.GetStanceWith(k2);
                if (stance != null)
                {
                    int k1TributePaid = GetSafeTribute(stance, k1);

                    if (k1TributePaid > 0)
                    {
                        AddControversy(k1, PrivyCouncilOffice.Chancellor, 25); 
                        AddControversy(k2, PrivyCouncilOffice.Chancellor, -25); 
                    }
                    else if (k1TributePaid < 0)
                    {
                        AddControversy(k1, PrivyCouncilOffice.Chancellor, -25); 
                        AddControversy(k2, PrivyCouncilOffice.Chancellor, 25); 
                    }
                }
            }
        }

        private int CalculateDynamicState(Kingdom kingdom)
        {
            int dynamicScore = 0;

            if (kingdom.RulingClan?.Leader != null && kingdom.RulingClan.Leader.IsPrisoner)
            {
                dynamicScore += ScaleControversy(40);
            }

            int activeWars = 0;

            foreach (Kingdom otherKingdom in Kingdom.All)
            {
                if (kingdom == otherKingdom || otherKingdom.IsEliminated) continue;

                StanceLink stance = kingdom.GetStanceWith(otherKingdom);
                if (stance == null) continue;

                if (stance.IsAtWar)
                {
                    activeWars++;
                }
                else
                {
                    int tribute = GetSafeTribute(stance, kingdom);
                    if (tribute > 0) dynamicScore += ScaleControversy(10);
                    else if (tribute < 0) dynamicScore += ScaleControversy(-10);

                    if (GetSafeBool(stance, "IsAllied")) dynamicScore += ScaleControversy(-10);
                    if (GetSafeBool(stance, "HasTradeAgreement") || GetSafeBool(stance, "IsTradeAgreementActive")) dynamicScore += ScaleControversy(-5);
                }
            }

            if (activeWars > 1)
            {
                dynamicScore += ScaleControversy(20);
            }

            return dynamicScore;
        }

        private static int ScaleControversy(int amount)
        {
            return amount;
        }

        private void InitializeStanceReflection()
        {
            if (_stanceReflectionInitialized) return;
            
            var stanceType = typeof(StanceLink);
            _getDailyTributeMethod = stanceType.GetMethod("GetDailyTributePaid", BindingFlags.Public | BindingFlags.Instance);
            _dailyTributeProp = stanceType.GetProperty("DailyTribute", BindingFlags.Public | BindingFlags.Instance);
            
            _boolPropsCache["IsAllied"] = stanceType.GetProperty("IsAllied", BindingFlags.Public | BindingFlags.Instance);
            _boolPropsCache["HasTradeAgreement"] = stanceType.GetProperty("HasTradeAgreement", BindingFlags.Public | BindingFlags.Instance);
            _boolPropsCache["IsTradeAgreementActive"] = stanceType.GetProperty("IsTradeAgreementActive", BindingFlags.Public | BindingFlags.Instance);
            
            _stanceReflectionInitialized = true;
        }

        private int GetSafeTribute(StanceLink stance, Kingdom kingdom)
        {
            InitializeStanceReflection();
            
            try
            {
                if (_getDailyTributeMethod != null)
                {
                    return (int)_getDailyTributeMethod.Invoke(stance, new object[] { kingdom });
                }

                if (_dailyTributeProp != null)
                {
                    int val = (int)_dailyTributeProp.GetValue(stance);
                    return (stance.Faction1 == kingdom) ? val : -val;
                }
            }
            catch { }

            return 0;
        }

        private bool GetSafeBool(object obj, string propertyName)
        {
            InitializeStanceReflection();
            
            try
            {
                if (_boolPropsCache.TryGetValue(propertyName, out PropertyInfo prop) && prop != null)
                {
                    return (bool)prop.GetValue(obj);
                }
            }
            catch { }
            return false;
        }

        public int GetTotalControversy(Kingdom kingdom)
        {
            if (kingdom == null) return 0;

            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (council != null)
                return (int)Math.Round(council.GetRulerControversy(kingdom), MidpointRounding.AwayFromZero);

            _trackedControversyById.TryGetValue(kingdom.StringId, out int trackedScore);
            _dailyDynamicCacheById.TryGetValue(kingdom.StringId, out int cachedDynamicScore);

            return trackedScore + cachedDynamicScore;
        }

        internal int ConsumeLegacyTrackedControversy(Kingdom kingdom)
        {
            if (kingdom == null || string.IsNullOrEmpty(kingdom.StringId))
                return 0;

            if (!_trackedControversyById.TryGetValue(kingdom.StringId, out int value))
                return 0;

            _trackedControversyById.Remove(kingdom.StringId);
            _dailyDynamicCacheById.Remove(kingdom.StringId);
            return Math.Max(0, value);
        }

        private static string GetCouncilReason(PrivyCouncilOffice office)
        {
            return office == PrivyCouncilOffice.Chancellor
                ? "unfavorable peace settlement"
                : "military setback";
        }
    }
}
