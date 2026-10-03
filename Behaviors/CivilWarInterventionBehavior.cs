using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BellumCivile.Behaviors
{
    /// <summary>
    /// Why did I do this file?
    /// To apply a temporary diplomatic shield to newly formed rebel kingdoms, preventing neighboring foreign empires from instantly declaring war on them before they can resolve their internal civil war.
    /// </summary>
    public class CivilWarInterventionBehavior : CampaignBehaviorBase
    {
        private Dictionary<Kingdom, CampaignTime> _protectedRebelKingdoms = new Dictionary<Kingdom, CampaignTime>();
        private List<string> _permanentlyProtectedKingdomIds = new List<string>();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, k => RemoveLock(k));
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("ProtectedRebelKingdoms", ref _protectedRebelKingdoms);
            dataStore.SyncData("BellumCivile_PermanentlyProtectedKingdomIds", ref _permanentlyProtectedKingdomIds);
            if (_protectedRebelKingdoms == null) _protectedRebelKingdoms = new Dictionary<Kingdom, CampaignTime>();
            if (_permanentlyProtectedKingdomIds == null) _permanentlyProtectedKingdomIds = new List<string>();
        }

        public void ApplyLock(Kingdom rebelKingdom, int days = 30) { if (rebelKingdom != null) _protectedRebelKingdoms[rebelKingdom] = CampaignTime.Now + CampaignTime.Days(days); }
        public void ApplyPermanentLock(Kingdom kingdom)
        {
            if (kingdom == null || string.IsNullOrWhiteSpace(kingdom.StringId)) return;
            if (!_permanentlyProtectedKingdomIds.Contains(kingdom.StringId))
                _permanentlyProtectedKingdomIds.Add(kingdom.StringId);
        }
        public void RemoveLock(Kingdom rebelKingdom)
        {
            if (rebelKingdom == null) return;
            _protectedRebelKingdoms.Remove(rebelKingdom);
            _permanentlyProtectedKingdomIds.Remove(rebelKingdom.StringId);
        }
        public bool IsProtected(Kingdom kingdom) => kingdom != null && (_protectedRebelKingdoms.ContainsKey(kingdom) || _permanentlyProtectedKingdomIds.Contains(kingdom.StringId));

        private void OnDailyTick()
        {
            List<Kingdom> expiredLocks = new List<Kingdom>();
            foreach (var kvp in _protectedRebelKingdoms) if (kvp.Value.IsPast) expiredLocks.Add(kvp.Key);
            foreach (Kingdom k in expiredLocks) RemoveLock(k);
        }
    }
}
