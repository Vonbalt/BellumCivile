using System;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BellumCivile
{
    internal static class RealmUnionSnapshotService
    {
        internal static bool SupportsAccession(CrownAccessionRecord accession) => accession != null
            && !accession.Completed && accession.Union == null && !accession.IsAbdication && !accession.RegentReplacement
            && !accession.ElectiveElection && !accession.Emergency && !accession.MandateExpiry
            && !accession.TitleTransferred && !accession.ForeignMoveStarted && accession.ForeignMovingClan == null;

        // Publish the journal only after the entire capture succeeds; never overwrite it on retry.
        internal static bool TryCapture(CrownAccessionRecord accession, out RealmUnionRecord journal, out string reason)
        {
            journal = null;
            reason = null;
            if (!SupportsAccession(accession))
                return Fail("accession is absent, already journaled or incompatible with initial union capture", out reason);
            Kingdom source = accession.Realm;
            Hero heir = accession.Heir;
            Clan house = heir?.Clan;
            Kingdom destination = house?.Kingdom;
            if (!Permanent(source) || !Permanent(destination) || source == destination
                || heir?.IsAlive != true || heir.IsDisabled || house.IsEliminated
                || house != destination.RulingClan || house.IsUnderMercenaryService
                || house.IsBanditFaction || NobleClanEligibilityHelper.IsNonPlayerMinorClan(house)
                || (RegencyBehavior.Instance?.GetLegalClanHead(house) ?? house.Leader) != heir
                || source.RulingClan != accession.PreviousHouse)
                return Fail("lawful incoming sovereign or permanent realm identity is unavailable", out reason);
            if (SuccessionRealmRules.Classify(accession.HouseLaw) != RealmSuccessionSystem.Hereditary
                || SuccessionRealmRules.Classify(SuccessionLawHelper.GetLawsForKingdom(source).SuccessionLaw) != RealmSuccessionSystem.Hereditary
                || SuccessionRealmRules.Classify(SuccessionLawHelper.GetLawsForKingdom(destination).SuccessionLaw) != RealmSuccessionSystem.Hereditary)
                return Fail("initial unions require hereditary government in both realms", out reason);
            var clients = Campaign.Current?.GetCampaignBehavior<ClientKingdomBehavior>();
            if (clients == null || clients.GetSuzerain(source) != null || clients.GetSuzerain(destination) != null)
                return Fail("clientage service is unavailable or a participant is subordinate", out reason);
            var titles = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            var inherited = titles?.GetKingdomPoliticalTitle(source);
            var primary = titles?.GetKingdomPoliticalTitle(destination);
            if (inherited?.IsActive != true || primary?.IsActive != true || inherited.TitleId == primary.TitleId
                || inherited.TitleType != primary.TitleType
                || inherited.DeJureHolderClanId != accession.PreviousHouse?.StringId
                || inherited.DeFactoHolderClanId != accession.PreviousHouse?.StringId
                || primary.DeJureHolderClanId != house.StringId || primary.DeFactoHolderClanId != house.StringId)
                return Fail("distinct fully held coequal political Crowns are required for this first scope", out reason);

            var members = source.Clans.Where(c => c != null && !c.IsEliminated).ToList();
            if (members.Count != source.Clans.Count || Clan.All.Any(c => c.Kingdom == source && !members.Contains(c)))
                return Fail("source membership contains stale or inconsistent clan entries", out reason);
            if (source.Armies.Any() || members.Any(c => c.Heroes.Any(h => h.PartyBelongedTo?.MapEvent != null
                    || h.PartyBelongedTo?.SiegeEvent != null || h.PartyBelongedTo?.Army != null)
                || c.Settlements.Any(s => s.SiegeEvent != null || s.Party?.MapEvent != null)))
                return Fail("source houses must finish battles, sieges and army duty before union capture", out reason);
            var factions = Campaign.Current?.GetCampaignBehavior<FactionManagerBehavior>();
            if (factions == null || factions.GetFactionsInKingdom(source).Any(f => !f.IsIdeology
                || CivilWarConflictBehavior.IsFactionTransferPending(f)))
                return Fail("source political conflicts must be settled before union capture", out reason);
            if (members.Any(c => c.IsBanditFaction || !c.IsUnderMercenaryService
                && NobleClanEligibilityHelper.IsNonPlayerMinorClan(c)))
                return Fail("source contains an unsupported non-noble participant", out reason);
            if (!RealmUnionTransferPlan.TryCreate(source.StringId, destination.StringId,
                members.Select(c => new RealmUnionClanSnapshot(c.StringId, c.Kingdom?.StringId,
                    c.IsUnderMercenaryService, c.Influence, c.DebtToKingdom)), out _, out reason)) return false;

            var affected = members.SelectMany(c => titles.GetTitlesHeldByClan(c, true)
                    .Concat(titles.GetTitlesHeldByClan(c, false)))
                .Concat(titles.GetTitleAndDescendants(inherited, FeudalHierarchyMode.DeJure))
                .Concat(titles.GetTitleAndDescendants(inherited, FeudalHierarchyMode.DeFacto))
                .Concat(new[] { inherited, primary }).Where(t => t != null && t.IsActive)
                .GroupBy(t => t.TitleId).Select(g => g.First()).OrderBy(t => t.TitleId, StringComparer.Ordinal);
            var captured = new RealmUnionRecord
            {
                Source = source, Destination = destination, Heir = heir,
                PreviousHouse = accession.PreviousHouse, SurvivingHouse = house,
                InheritedCrownId = inherited.TitleId, PrimaryCrownId = primary.TitleId,
                CapturedAt = CampaignTime.Now,
                Clans = members.OrderBy(c => c.StringId, StringComparer.Ordinal).Select(CaptureClan).ToList(),
                Titles = affected.Select(RealmUnionTitleRecord.Capture).ToList(),
                PendingReason = "snapshot captured; diplomatic preflight pending"
            };
            journal = captured;
            return true;
        }

        internal static RealmUnionClanRecord CaptureClan(Clan clan) => new RealmUnionClanRecord
        {
            Clan = clan, EndMercenaryContract = clan.IsUnderMercenaryService,
            Influence = clan.Influence, Debt = clan.DebtToKingdom,
            Banner = clan.Banner == null ? null : new Banner(clan.Banner),
            OriginalBanner = clan.ClanOriginalBanner == null ? null : new Banner(clan.ClanOriginalBanner),
            Color = clan.Color, Color2 = clan.Color2,
            Holdings = clan.Settlements.Select(s => s.StringId).OrderBy(id => id, StringComparer.Ordinal).ToList()
        };

        private static bool Permanent(Kingdom realm) => realm != null && !realm.IsEliminated
            && !BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm);

        private static bool Fail(string message, out string reason) { reason = message; return false; }
    }
}
