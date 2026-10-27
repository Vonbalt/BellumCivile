using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile.Behaviors
{
    public partial class CivilWarResolutionBehavior
    {
        private sealed class AcceptedIndependenceRealm
        {
            internal IndependencePartitionGroup Group;
            internal IndependentKingdomProfile Profile;
            internal Kingdom Realm;
            internal KingdomVisualHelper.KingdomFoundingVisuals Visuals;
            internal string PreviousSourceRealmId;
        }

        internal bool TryAcceptIndependence(FactionObject faction, out List<Kingdom> independentKingdoms)
        {
            independentKingdoms = new List<Kingdom>();
            Kingdom parent = faction?.ParentKingdom;
            Clan sponsor = faction?.Leader;
            if (faction?.Type != FactionType.Independence || parent == null || parent.IsEliminated
                || faction.HasTrackedRebelKingdom || !KingdomCreationSafetyHelper.IsValidFounder(sponsor)
                || sponsor.Kingdom != parent || sponsor == parent.RulingClan)
                return false;

            // Plan every destination before the first callback can prune the political faction.
            List<Clan> participants = faction.Members.Where(c => NobleClanEligibilityHelper.IsLiveNobleClan(c)
                && c.Kingdom == parent && c != parent.RulingClan).Distinct().ToList();
            List<IndependencePartitionGroup> groups = BuildIndependencePartitionGroupsForClans(parent, participants, sponsor);
            if (groups.Count == 0 || groups.Any(g => !KingdomCreationSafetyHelper.IsValidFounder(g.Leader)))
                return false;

            var entries = groups.Select(g => new AcceptedIndependenceRealm
            {
                Group = g,
                Profile = IndependentKingdomProfileHelper.CreateForTitle(g.Leader, parent, g.SourceTitle),
                PreviousSourceRealmId = g.SourceTitle?.AssociatedKingdomId
            }).ToList();
            var preservedBanners = KingdomVisualHelper.CaptureClanBannersToPreserve(participants);
            try
            {
                foreach (var entry in entries)
                {
                    Clan founder = entry.Group.Leader;
                    string prefix = parent.StringId + "_indep_" + founder.StringId;
                    string id = prefix;
                    int suffix = 0;
                    while (Kingdom.All.Any(k => k.StringId == id)) id = prefix + "_" + (++suffix);
                    entry.Realm = KingdomCreationSafetyHelper.CreateKingdom(id, founder);
                    if (entry.Realm == null) return false;
                    entry.Visuals = KingdomVisualHelper.ResolveBreakawayKingdomVisuals(parent, founder, id);
                    Settlement capital = founder.Settlements.FirstOrDefault() ?? parent.Settlements.FirstOrDefault()
                        ?? Settlement.All.FirstOrDefault(s => s.IsTown || s.IsCastle);
                    var profile = entry.Profile;
                    var visuals = entry.Visuals;
                    entry.Realm.InitializeKingdom(profile.Name, profile.Name, profile.Culture ?? parent.Culture,
                        visuals.Banner, visuals.PrimaryColor, visuals.SecondaryColor, capital,
                        profile.EncyclopediaText, profile.EncyclopediaTitle, profile.RulerTitle);
                    KingdomVisualHelper.ApplyKingdomPalette(entry.Realm, visuals);
                    // Membership callbacks must see the founding title, not invent a kingdom-tier crown.
                    RegisterIndependentRealmSourceTitle(entry.Realm, profile, "accepted independence ultimatum");
                    RebelPolicyHelper.ApplyIndependencePolicyProfile(faction, entry.Realm);
                }

                bool complete = true;
                foreach (var entry in entries)
                {
                    Clan founder = entry.Group.Leader;
                    if (!MoveAcceptedIndependenceClan(founder, parent, entry))
                    {
                        complete = false;
                        continue;
                    }
                    entry.Realm.RulingClan = founder;
                    foreach (Clan clan in entry.Group.Clans.Where(c => c != founder))
                        complete &= MoveAcceptedIndependenceClan(clan, parent, entry);
                    EnsureValidRulingClan(entry.Realm, founder, forcePreferred: true);
                    if (!NobleClanEligibilityHelper.IsValidRulingClan(entry.Realm.RulingClan, entry.Realm))
                    {
                        complete = false;
                        continue;
                    }
                    ClearTemporaryKingdomRepair(entry.Realm);
                    if (entry.Visuals.RecolorMemberClans)
                        KingdomVisualHelper.ReapplyKingdomPaletteAfterClanChanges(entry.Realm, entry.Visuals);
                    Campaign.Current.GetCampaignBehavior<IdeologyBehavior>()?.RefreshKingdomIdeologies(entry.Realm);
                    independentKingdoms.Add(entry.Realm);
                }
                BellumCivileLogger.Log($"Accepted independence partition; parent={parent.StringId}; sponsor={sponsor.StringId}; complete={complete}; "
                    + $"groups={string.Join(";", entries.Select(e => $"{e.Group.Leader.StringId}:{e.Realm?.StringId ?? "none"}:[{string.Join(",", e.Group.Clans.Select(c => c.StringId))}]"))}.");
                return complete && independentKingdoms.Count == groups.Count;
            }
            finally
            {
                foreach (var entry in entries.Where(e => e.Realm != null && !e.Realm.IsEliminated))
                {
                    if (!entry.Visuals.RecolorMemberClans)
                        KingdomVisualHelper.RestoreClanBanners(preservedBanners.Where(p => entry.Group.Clans.Contains(p.Key))
                            .ToDictionary(p => p.Key, p => p.Value));
                    if (!entry.Realm.Clans.Any(c => c != null && !c.IsEliminated))
                    {
                        var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
                        titles?.UnregisterIndependentRealmShell(entry.Realm, "accepted independence transfer failed");
                        if (entry.Group.SourceTitle?.AssociatedKingdomId == entry.Realm.StringId)
                            entry.Group.SourceTitle.SetAssociatedKingdom(entry.PreviousSourceRealmId);
                        titles?.ReconcilePoliticalHierarchy("accepted independence transfer failed");
                        ClearTemporaryKingdomRepair(entry.Realm);
                        DestroyKingdomAction.Apply(entry.Realm);
                    }
                }
            }
        }

        private static bool MoveAcceptedIndependenceClan(Clan clan, Kingdom parent, AcceptedIndependenceRealm entry)
        {
            if (clan?.Kingdom != parent || !NobleClanEligibilityHelper.IsLiveNobleClan(clan))
                return false;
            CourtPoliticalPositionBehavior.MoveToSuccessorRealm(clan, parent, entry.Realm,
                () => KingdomVisualHelper.ApplyJoinToKingdomWithBannerPolicy(clan, entry.Realm,
                    preserveCustomBanner: !entry.Visuals.RecolorMemberClans, showNotification: false));
            bool moved = clan.Kingdom == entry.Realm;
            if (!moved) BellumCivileLogger.Log($"Accepted independence clan transfer did not complete; clan={clan.StringId}; "
                + $"expected={entry.Realm.StringId}; actual={clan.Kingdom?.StringId ?? "none"}.");
            return moved;
        }
    }
}
