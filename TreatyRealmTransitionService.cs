using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    internal sealed class TreatyVassalReleaseCandidate
    {
        public Clan LeaderClan { get; }
        public FeudalTitleRecord RootTitle { get; }
        public IReadOnlyList<Clan> ClusterClans { get; }
        public int FiefCount { get; }
        public int WarScoreCost { get; }

        public TreatyVassalReleaseCandidate(Clan leaderClan, FeudalTitleRecord rootTitle, IEnumerable<Clan> clusterClans, int fiefCount, int warScoreCost)
        {
            LeaderClan = leaderClan;
            RootTitle = rootTitle;
            ClusterClans = (clusterClans ?? Enumerable.Empty<Clan>()).Distinct().ToList();
            FiefCount = fiefCount;
            WarScoreCost = warScoreCost;
        }
    }

    internal sealed class TreatyForceVassalizationCandidate
    {
        public Kingdom VictorRealm { get; }
        public Kingdom DefeatedRealm { get; }
        public FeudalTitleRecord VictorSovereignTitle { get; }
        public FeudalTitleRecord DefeatedSovereignTitle { get; }
        public bool HasClaimBasis { get; }
        public bool HasDeJureBasis { get; }
        public int FiefCount { get; }
        public int WarScoreCost { get; }

        public TreatyForceVassalizationCandidate(Kingdom victorRealm, Kingdom defeatedRealm, FeudalTitleRecord victorTitle,
            FeudalTitleRecord defeatedTitle, bool hasClaimBasis, bool hasDeJureBasis, int fiefCount, int warScoreCost)
        {
            VictorRealm = victorRealm;
            DefeatedRealm = defeatedRealm;
            VictorSovereignTitle = victorTitle;
            DefeatedSovereignTitle = defeatedTitle;
            HasClaimBasis = hasClaimBasis;
            HasDeJureBasis = hasDeJureBasis;
            FiefCount = fiefCount;
            WarScoreCost = warScoreCost;
        }
    }

    internal static class TreatyRealmTransitionService
    {
        public static IReadOnlyList<TreatyVassalReleaseCandidate> GetReleaseCandidates(Kingdom sourceRealm)
        {
            FeudalTitleBehavior titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord sourceSovereign = titles?.GetRealmSovereignTitle(sourceRealm, FeudalHierarchyMode.DeFacto)
                ?? titles?.GetKingdomPoliticalTitle(sourceRealm);
            if (titles == null || sourceRealm == null || sourceRealm.IsEliminated || sourceSovereign == null)
                return new List<TreatyVassalReleaseCandidate>();

            List<TreatyVassalReleaseCandidate> result = new List<TreatyVassalReleaseCandidate>();
            foreach (Clan clan in sourceRealm.Clans.Where(IsEligibleSettledClan))
            {
                if (clan == sourceRealm.RulingClan)
                    continue;

                List<FeudalTitleRecord> held = titles.GetTitlesHeldByClan(clan, deJure: false)
                    .Where(title => title != null && title.IsActive)
                    .OrderByDescending(title => title.TitleType)
                    .ToList();
                FeudalTitleRecord root = held.FirstOrDefault(title =>
                    string.Equals(title.DeFactoParentTitleId, sourceSovereign.TitleId, StringComparison.Ordinal));
                if (root == null || IsDeJureWithinRealm(titles, root, sourceRealm))
                    continue;

                HashSet<string> clusterTitleIds = new HashSet<string>(titles.GetTitleAndDescendants(root, FeudalHierarchyMode.DeFacto)
                    .Where(title => title != null && title.IsActive)
                    .Select(title => title.TitleId));
                if (held.Any(title => !clusterTitleIds.Contains(title.TitleId)))
                    continue;

                List<FeudalTitleRecord> clusterTitles = clusterTitleIds.Select(titles.GetTitle).Where(title => title != null).ToList();
                List<Clan> clusterClans = clusterTitles
                    .Select(title => ResolveClan(title.DeFactoHolderClanId))
                    .Where(candidate => candidate != null && candidate.Kingdom == sourceRealm && !candidate.IsEliminated && !candidate.IsUnderMercenaryService)
                    .Distinct()
                    .ToList();
                if (!clusterClans.Contains(clan) || clusterClans.Contains(sourceRealm.RulingClan))
                    continue;

                bool mixedSubordinate = clusterClans.Any(clusterClan => titles.GetTitlesHeldByClan(clusterClan, deJure: false)
                    .Any(title => title != null && title.IsActive && !clusterTitleIds.Contains(title.TitleId)));
                if (mixedSubordinate)
                    continue;

                List<Settlement> clusterFiefs = clusterTitles
                    .Where(title => title.TitleType == FeudalTitleType.Barony && !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
                    .Select(title => ResolveSettlement(title.CapitalSettlementId))
                    .Where(settlement => settlement != null && (settlement.IsTown || settlement.IsCastle))
                    .Distinct()
                    .ToList();
                int fiefCount = clusterFiefs.Count;
                int cost = TreatyTermCostModel.GetStructuralFiefWarScoreCost(clusterFiefs);
                result.Add(new TreatyVassalReleaseCandidate(clan, root, clusterClans, fiefCount, cost));
            }

            return result.OrderBy(candidate => candidate.WarScoreCost)
                .ThenBy(candidate => candidate.LeaderClan.Name?.ToString() ?? candidate.LeaderClan.StringId)
                .ToList();
        }

        public static bool TryGetForceVassalizationCandidate(Kingdom victorRealm, Kingdom defeatedRealm,
            out TreatyForceVassalizationCandidate candidate, out string reason,
            IEnumerable<TreatyTermRecord> projectedTerms = null)
        {
            candidate = null;
            reason = "force vassalization is not legally available";
            FeudalTitleBehavior titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null || victorRealm == null || defeatedRealm == null || victorRealm == defeatedRealm
                || victorRealm.IsEliminated || defeatedRealm.IsEliminated
                || victorRealm.RulingClan?.Leader == null || defeatedRealm.RulingClan?.Leader == null)
                return false;

            FeudalTitleRecord victorTitle = titles.GetRealmSovereignTitle(victorRealm, FeudalHierarchyMode.DeFacto)
                ?? titles.GetKingdomPoliticalTitle(victorRealm);
            FeudalTitleRecord defeatedTitle = titles.GetRealmSovereignTitle(defeatedRealm, FeudalHierarchyMode.DeFacto)
                ?? titles.GetKingdomPoliticalTitle(defeatedRealm);
            if (victorTitle == null || defeatedTitle == null || victorTitle.TitleType <= defeatedTitle.TitleType)
            {
                reason = "the victor's sovereign title does not outrank the defeated ruler";
                return false;
            }

            Clan victorRuler = victorRealm.RulingClan;
            bool hasClaim = titles.HasActiveClaim(victorRuler, defeatedTitle, FeudalClaimStrength.Strong)
                || titles.HasActiveClaim(victorRuler, defeatedTitle, FeudalClaimStrength.Weak);
            bool hasDeJureBasis = IsDeJureDescendantOf(titles, defeatedTitle, victorTitle);
            if (!hasClaim && !hasDeJureBasis)
            {
                reason = "the victor has neither a claim nor de jure suzerainty over the defeated title";
                return false;
            }

            HashSet<string> defeatedCluster = new HashSet<string>(titles.GetTitleAndDescendants(defeatedTitle, FeudalHierarchyMode.DeFacto)
                .Where(title => title != null && title.IsActive)
                .Select(title => title.TitleId));
            foreach (Clan clan in defeatedRealm.Clans.Where(clan => clan != null && !clan.IsEliminated && !clan.IsUnderMercenaryService))
            {
                if (titles.GetTitlesHeldByClan(clan, deJure: false)
                    .Any(title => title != null && title.IsActive && !defeatedCluster.Contains(title.TitleId)))
                {
                    reason = "the defeated realm contains a title outside its sovereign cluster";
                    return false;
                }
            }

            int fiefCount = Settlement.All.Count(settlement => settlement != null
                && (settlement.IsTown || settlement.IsCastle)
                && settlement.OwnerClan?.Kingdom == defeatedRealm);
            int cost = TreatyTermCostModel.GetProjectedRealmStructuralCost(defeatedRealm, projectedTerms);
            candidate = new TreatyForceVassalizationCandidate(victorRealm, defeatedRealm, victorTitle, defeatedTitle,
                hasClaim, hasDeJureBasis, fiefCount, cost);
            reason = "force vassalization is legally available";
            return true;
        }

        public static bool TryReleaseVassal(Kingdom sourceRealm, Clan leaderClan, FeudalTitleRecord rootTitle,
            out Kingdom independentRealm, out string reason)
        {
            independentRealm = null;
            reason = "release candidate is no longer valid";
            TreatyVassalReleaseCandidate candidate = GetReleaseCandidates(sourceRealm)
                .FirstOrDefault(entry => entry.LeaderClan == leaderClan && entry.RootTitle?.TitleId == rootTitle?.TitleId);
            if (candidate == null)
                return false;

            FeudalTitleBehavior titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            string baseId = $"bc_treaty_release_{Sanitize(candidate.RootTitle.TitleId)}_{Sanitize(candidate.LeaderClan.StringId)}";
            string kingdomId = MakeUniqueKingdomId(baseId);
            independentRealm = KingdomCreationSafetyHelper.CreateKingdom(kingdomId, candidate.LeaderClan);
            if (independentRealm == null)
            {
                reason = "independent realm shell could not be created";
                return false;
            }

            TextObject name = new TextObject("{=!}" + FeudalTitleDisplayHelper.FormatTitleName(candidate.RootTitle, candidate.LeaderClan));
            TextObject rulerTitle = new TextObject("{=!}" + FeudalTitleDisplayHelper.ResolveDisplayTitle(candidate.RootTitle,
                candidate.LeaderClan.Leader?.IsFemale == true, candidate.LeaderClan));
            TextObject description = new TextObject("{=BC_Treaty_ReleasedRealmDescription}Released from {FORMER_REALM} by treaty, {NEW_REALM} entered the world as an independent realm.")
                .SetTextVariable("FORMER_REALM", sourceRealm.Name)
                .SetTextVariable("NEW_REALM", name);
            Settlement capital = ResolveSettlement(candidate.RootTitle.CapitalSettlementId)
                ?? candidate.LeaderClan.Settlements.FirstOrDefault()
                ?? sourceRealm.Settlements.FirstOrDefault()
                ?? Settlement.All.FirstOrDefault(settlement => settlement.IsTown || settlement.IsCastle);
            var visuals = KingdomVisualHelper.ResolveIndependentSuccessorKingdomVisuals(sourceRealm, candidate.LeaderClan, kingdomId);
            independentRealm.InitializeKingdom(name, name, candidate.LeaderClan.Culture ?? sourceRealm.Culture,
                visuals.Banner, visuals.PrimaryColor, visuals.SecondaryColor, capital, description, name, rulerTitle);
            KingdomVisualHelper.ApplyKingdomPalette(independentRealm, visuals);

            MoveClansPreservingState(sourceRealm, independentRealm, candidate.ClusterClans, candidate.LeaderClan);
            candidate.RootTitle.SetDeFactoParentTitle(string.Empty);
            foreach (FeudalTitleRecord title in titles.GetTitleAndDescendants(candidate.RootTitle, FeudalHierarchyMode.DeFacto))
            {
                Clan holder = ResolveClan(title.DeFactoHolderClanId);
                if (holder?.Kingdom == independentRealm)
                {
                    title.SetAssociatedKingdom(independentRealm.StringId);
                    title.MarkSynced(CurrentDay);
                }
            }
            titles.RegisterIndependentRealmShell(independentRealm, candidate.RootTitle, "foreign treaty vassal release");
            titles.ReconcilePoliticalHierarchy("foreign treaty vassal release");
            RebelPolicyHelper.CopyPolicies(sourceRealm, independentRealm);
            Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(independentRealm);
            reason = "vassal title cluster released as an independent realm";
            return true;
        }

        public static bool TryForceVassalize(Kingdom victorRealm, Kingdom defeatedRealm, out string reason)
        {
            if (!TryGetForceVassalizationCandidate(victorRealm, defeatedRealm, out TreatyForceVassalizationCandidate candidate, out reason))
                return false;

            FeudalTitleBehavior titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            List<Clan> clans = defeatedRealm.Clans
                .Where(clan => clan != null && !clan.IsEliminated && !clan.IsUnderMercenaryService)
                .ToList();
            foreach (Clan mercenary in defeatedRealm.Clans.Where(clan => clan != null && clan.IsUnderMercenaryService).ToList())
                ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(mercenary, showNotification: false);

            MoveClansPreservingState(defeatedRealm, victorRealm,
                clans.OrderBy(clan => clan == defeatedRealm.RulingClan ? 1 : 0).ToList(), null);
            candidate.DefeatedSovereignTitle.SetDeFactoParentTitle(candidate.VictorSovereignTitle.TitleId);
            foreach (FeudalTitleRecord title in titles.GetTitleAndDescendants(candidate.DefeatedSovereignTitle, FeudalHierarchyMode.DeFacto))
            {
                Clan holder = ResolveClan(title.DeFactoHolderClanId);
                if (holder?.Kingdom == victorRealm)
                {
                    title.SetAssociatedKingdom(victorRealm.StringId);
                    title.MarkSynced(CurrentDay);
                }
            }
            titles.UnregisterIndependentRealmShell(defeatedRealm, "foreign treaty force vassalization");
            titles.ReconcilePoliticalHierarchy("foreign treaty force vassalization");
            Campaign.Current.GetCampaignBehavior<FactionManagerBehavior>()?.ClearTemporaryKingdomRepair(defeatedRealm);
            if (!defeatedRealm.IsEliminated && !defeatedRealm.Clans.Any(clan => clan != null && !clan.IsEliminated))
                DestroyKingdomAction.Apply(defeatedRealm);
            Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(victorRealm);
            reason = "defeated realm absorbed as a vassal title cluster";
            return true;
        }

        private static void MoveClansPreservingState(Kingdom source, Kingdom destination, IEnumerable<Clan> clans, Clan preferredRuler)
        {
            List<Clan> moving = (clans ?? Enumerable.Empty<Clan>()).Where(clan => clan != null && clan.Kingdom == source).Distinct().ToList();
            Dictionary<Clan, float> influence = moving.ToDictionary(clan => clan, clan => Math.Max(0f, clan.Influence));
            Dictionary<Clan, TaleWorlds.Core.Banner> banners = KingdomVisualHelper.CaptureClanBannersToPreserve(moving);
            Action move = () =>
            {
                foreach (Clan clan in moving)
                {
                    if (preferredRuler != null)
                        CourtPoliticalPositionBehavior.MoveToSuccessorRealm(clan, source, destination,
                            () => KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, destination, showNotification: false));
                    else
                        KingdomVisualHelper.ApplyJoinToKingdomPreservingCustomBanner(clan, destination, showNotification: false);
                }
                if (preferredRuler != null && preferredRuler.Kingdom == destination)
                    destination.RulingClan = preferredRuler;
            };
            FactionManagerBehavior manager = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (manager != null)
                manager.RunWithRulerRepairSuppressed(source, move);
            else
                move();
            KingdomVisualHelper.RestoreClanBanners(banners);
            foreach (KeyValuePair<Clan, float> snapshot in influence)
                snapshot.Key.Influence = Math.Max(snapshot.Key.Influence, snapshot.Value);
        }

        private static bool IsDeJureWithinRealm(FeudalTitleBehavior titles, FeudalTitleRecord title, Kingdom realm)
        {
            FeudalTitleRecord realmTitle = titles.GetRealmSovereignTitle(realm, FeudalHierarchyMode.DeJure)
                ?? titles.GetKingdomPoliticalTitle(realm);
            return realmTitle != null && IsDeJureDescendantOf(titles, title, realmTitle);
        }

        private static bool IsDeJureDescendantOf(FeudalTitleBehavior titles, FeudalTitleRecord title, FeudalTitleRecord ancestor)
        {
            if (titles == null || title == null || ancestor == null)
                return false;
            HashSet<string> visited = new HashSet<string>();
            FeudalTitleRecord current = title;
            while (current != null && visited.Add(current.TitleId))
            {
                if (current.TitleId == ancestor.TitleId)
                    return true;
                current = titles.GetParentTitle(current, FeudalHierarchyMode.DeJure);
            }
            return false;
        }

        private static bool IsEligibleSettledClan(Clan clan)
        {
            return clan != null && !clan.IsEliminated && !clan.IsUnderMercenaryService
                && (!clan.IsMinorFaction || clan == Clan.PlayerClan) && clan.Leader != null;
        }

        private static Clan ResolveClan(string id) => Clan.All.FirstOrDefault(clan => clan?.StringId == id);
        private static Settlement ResolveSettlement(string id) => Settlement.All.FirstOrDefault(settlement => settlement?.StringId == id);
        private static float CurrentDay => (float)CampaignTime.Now.ToDays;
        private static string Sanitize(string value) => new string((value ?? "realm").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        private static string MakeUniqueKingdomId(string baseId)
        {
            string id = baseId;
            int suffix = 0;
            while (Kingdom.All.Any(kingdom => kingdom?.StringId == id))
                id = baseId + "_" + (++suffix);
            return id;
        }
    }
}
