using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BellumCivile.Behaviors
{
    public class CrownAuthorityBehavior : CampaignBehaviorBase
    {
        private Dictionary<string, int> _authorityByKingdomId = new Dictionary<string, int>();

        public override void RegisterEvents()
        {
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BellumCivile_CrownAuthorityByKingdomId", ref _authorityByKingdomId);
            EnsureCollectionsInitialized();
        }

        public CrownAuthorityLevel GetAuthority(Kingdom kingdom)
        {
            if (kingdom == null || string.IsNullOrWhiteSpace(kingdom.StringId))
                return CrownAuthorityLevel.Normal;

            EnsureCollectionsInitialized();
            if (!_authorityByKingdomId.TryGetValue(kingdom.StringId, out int value))
                return CrownAuthorityLevel.Normal;

            return Clamp(value);
        }

        public void SetAuthority(Kingdom kingdom, CrownAuthorityLevel authority, string reason)
        {
            if (kingdom == null || string.IsNullOrWhiteSpace(kingdom.StringId))
                return;

            EnsureCollectionsInitialized();
            _authorityByKingdomId[kingdom.StringId] = (int)authority;
            BellumCivileLogger.Log($"Crown authority set; kingdom={kingdom.StringId}; authority={authority}; reason={reason ?? "unknown"}.");
        }

        public CrownAuthorityLevel AdjustAuthority(Kingdom kingdom, int delta, string reason)
        {
            CrownAuthorityLevel current = GetAuthority(kingdom);
            CrownAuthorityLevel adjusted = Clamp((int)current + delta);
            SetAuthority(kingdom, adjusted, reason);
            return adjusted;
        }

        public bool CanSuppressFeud(Kingdom kingdom, FeudalClaimStrength claimStrength)
        {
            CrownAuthorityLevel authority = GetAuthority(kingdom);
            if (authority <= CrownAuthorityLevel.Devastated)
                return false;

            if (claimStrength == FeudalClaimStrength.Weak)
                return authority >= CrownAuthorityLevel.Weak;

            return authority >= CrownAuthorityLevel.Normal;
        }

        public string BuildDebugReport()
        {
            EnsureCollectionsInitialized();
            return string.Join("\n", Kingdom.All
                .Where(kingdom => kingdom != null && !kingdom.IsEliminated && !kingdom.IsMinorFaction && !kingdom.IsBanditFaction)
                .OrderBy(kingdom => kingdom.Name?.ToString() ?? kingdom.StringId)
                .Select(kingdom => $"{kingdom.Name}: {GetAuthority(kingdom)}"));
        }

        private static CrownAuthorityLevel Clamp(int value)
        {
            int min = (int)CrownAuthorityLevel.Devastated;
            int max = (int)CrownAuthorityLevel.Absolute;
            return (CrownAuthorityLevel)Math.Max(min, Math.Min(max, value));
        }

        private void EnsureCollectionsInitialized()
        {
            if (_authorityByKingdomId == null)
                _authorityByKingdomId = new Dictionary<string, int>();
        }
    }
}
