using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;

namespace BellumCivile
{
    internal static class EstateInheritanceDisplay
    {
        private const int TooltipAssetLimit = 5;

        private enum AssetKind { Ordinary, CrownEstate, Sovereign }

        private sealed class Asset
        {
            internal string Name { get; }
            internal AssetKind Kind { get; }
            internal string Label => Kind == AssetKind.Sovereign ? SovereignTitle(Name)
                : Kind == AssetKind.CrownEstate ? CrownEstateAsset(Name) : Name;

            internal Asset(string name, AssetKind kind = AssetKind.Ordinary) { Name = name; Kind = kind; }
        }

        internal static string Caveat => new TextObject("{=BC_EstatePreview_Caveat}Expected inheritance reflects current circumstances, not a guarantee. Pending inheritance is recorded but not yet fully settled.").ToString();

        internal static List<StringPairItemVM> BuildHeroEntries(Hero hero)
        {
            var entries = new List<StringPairItemVM>();
            if (hero == null || Campaign.Current == null)
                return entries;

            List<CrossClanEstateRecord> pending = GetPendingEstates();
            var pendingSources = new HashSet<Clan>(pending.Select(record => record.Source));
            foreach (CrossClanEstateRecord record in pending)
                AddHeroShares(entries, hero, record, true);

            if (!hero.IsAlive || hero.Clan == null || hero.IsWanderer || hero.IsNotable)
                return entries;

            // One on-demand pass per refresh; never replan a house with an unsettled snapshot.
            foreach (Clan source in Clan.All)
            {
                if (source == null || source.IsEliminated || source.Leader?.IsAlive != true
                    || pendingSources.Contains(source))
                    continue;
                Hero predecessor = RegencyBehavior.Instance?.GetLegalClanHead(source) ?? source.Leader;
                if (!HereditaryRealmSuccession.IsBloodRelative(hero, predecessor))
                    continue;
                AddHeroShares(entries, hero, EstateInheritancePolicy.BuildProjection(source, predecessor), false);
            }

            foreach (Kingdom realm in Kingdom.All)
            {
                Hero sovereign = RegencyBehavior.Instance?.GetLegalClanHead(realm?.RulingClan) ?? realm?.RulingClan?.Leader;
                if (pendingSources.Contains(realm?.RulingClan) || !HereditaryRealmSuccession.IsBloodRelative(hero, sovereign)
                    || !TryGetCrownHeir(realm, out Hero heir, out FeudalTitleRecord title) || heir != hero)
                    continue;
                entries.Add(HeroEntry(
                    new Asset(title == null ? realm.Name.ToString() : FeudalTitleDisplayHelper.FormatTitleName(title, realm.RulingClan), AssetKind.Sovereign),
                    FromPerson(sovereign, realm.RulingClan,
                        "{=BC_EstatePreview_ExpectedFrom}Expected from {PERSON} of {HOUSE}")));
            }
            return entries;
        }

        internal static List<TooltipProperty> BuildClanTooltip(Clan clan)
        {
            if (clan == null)
                return new List<TooltipProperty>();

            List<TooltipProperty> rows = SuccessionLawHelper.BuildSuccessionLineTooltipProperties(clan);
            rows[0] = Heading(new TextObject("{=BC_EstatePreview_ClanSuccession}Clan succession").ToString());
            rows.Add(Paragraph(new TextObject("{=BC_EstatePreview_ClanScope}The clan successor takes leadership of the house. Estate beneficiaries may be different people, including heirs in other houses.").ToString()));

            List<CrossClanEstateRecord> pending = GetPendingEstates().Where(record => record.Source == clan).ToList();
            rows.Add(Separator());
            rows.Add(Heading(pending.Count > 0
                ? new TextObject("{=BC_EstatePreview_PendingBeneficiaries}Pending estate beneficiaries").ToString()
                : new TextObject("{=BC_EstatePreview_ExpectedBeneficiaries}Expected estate beneficiaries").ToString()));
            int estateStart = rows.Count;
            if (pending.Count > 0)
            {
                foreach (CrossClanEstateRecord record in pending)
                    AddClanShares(rows, record, true);
            }
            else if (clan.Leader?.IsAlive == true)
            {
                AddClanShares(rows, EstateInheritancePolicy.BuildProjection(clan), false);
            }
            if (rows.Count == estateStart)
                rows.Add(Paragraph(pending.Count > 0
                    ? new TextObject("{=BC_EstatePreview_SettlementPending}Estate settlement outstanding").ToString()
                    : new TextObject("{=BC_EstatePreview_NoEstate}No estate beneficiaries are currently projected.").ToString()));

            if (pending.Count == 0 && clan.Kingdom?.RulingClan == clan
                && TryGetCrownHeir(clan.Kingdom, out Hero crownHeir, out FeudalTitleRecord crown))
            {
                rows.Add(Separator());
                rows.Add(Heading(new TextObject("{=BC_EstatePreview_CrownSuccession}Crown succession (separate)").ToString()));
                rows.Add(Paragraph(Beneficiary(crownHeir, crownHeir.Clan) + "\n"
                    + (crown == null ? clan.Kingdom.Name.ToString() : FeudalTitleDisplayHelper.FormatTitleName(crown, clan))));
            }

            rows.Add(Separator());
            rows.Add(Paragraph(pending.Any(record => record.PolicyVersion == 0)
                ? new TextObject("{=BC_EstatePreview_LegacyPending}Older recorded allocations are preserved. Pending shares may include foreign beneficiaries and are not recalculated under the current same-realm rule.").ToString()
                : new TextObject("{=BC_EstatePreview_SameRealm}Ordinary landed inheritance stays within the deceased's realm; foreign heirs do not receive ordinary land. Sovereign succession is resolved separately.").ToString()));
            rows.Add(Paragraph(Caveat));
            return rows;
        }

        private static List<CrossClanEstateRecord> GetPendingEstates() =>
            Campaign.Current?.GetCampaignBehavior<PartitionSuccessionBehavior>()?.GetPendingEstatePreviews()
                .Where(record => record != null && !record.Completed).ToList()
            ?? new List<CrossClanEstateRecord>();

        private static void AddHeroShares(List<StringPairItemVM> entries, Hero hero,
            CrossClanEstateRecord record, bool pending)
        {
            if (record?.Shares == null)
                return;
            foreach (CrossClanEstateShare share in record.Shares.Where(share => share != null
                && share.Heir == hero && !share.IsEscheat && (!pending || !share.Completed)))
            {
                string context = FromEstate(record, pending);
                List<Asset> assets = GetAssets(record, share, pending);
                if (assets.Count == 0 && pending)
                    assets.Add(new Asset(new TextObject("{=BC_EstatePreview_SettlementPending}Estate settlement outstanding").ToString()));
                foreach (Asset asset in assets)
                    entries.Add(HeroEntry(asset, context));
            }
        }

        private static StringPairItemVM HeroEntry(Asset asset, string context)
        {
            string hint = context;
            if (asset.Kind != AssetKind.Ordinary)
            {
                var text = new TextObject("{=BC_EstatePreview_HeroHint}{KIND} - {SOURCE}");
                text.SetTextVariable("KIND", new TextObject(asset.Kind == AssetKind.CrownEstate
                    ? "{=BC_EstatePreview_CrownEstateKind}Crown estate"
                    : "{=BC_EstatePreview_SovereignKind}Sovereign succession"));
                text.SetTextVariable("SOURCE", context);
                hint = text.ToString();
            }
            return new StringPairItemVM(asset.Name, string.Empty, new BasicTooltipViewModel(() => hint));
        }

        private static void AddClanShares(List<TooltipProperty> rows, CrossClanEstateRecord record, bool pending)
        {
            if (record?.Shares == null)
                return;
            var beneficiaries = record.Shares.Where(share => share != null && (!pending || !share.Completed))
                .GroupBy(share => new
                {
                    share.Heir,
                    share.IsEscheat,
                    Recipient = (pending ? share.Recipient : null)
                        ?? (share.IsEscheat ? record.Realm?.RulingClan : share.Heir?.Clan)
                });
            foreach (var group in beneficiaries)
            {
                List<string> assets = group.SelectMany(share => GetAssets(record, share, pending)).Select(asset => asset.Label).ToList();
                if (assets.Count == 0 && !pending)
                    continue;
                string beneficiary;
                if (group.Key.IsEscheat)
                {
                    var text = new TextObject("{=BC_EstatePreview_Escheat}Escheat to {HOUSE}");
                    text.SetTextVariable("HOUSE", HouseName(group.Key.Recipient));
                    beneficiary = text.ToString();
                }
                else
                {
                    beneficiary = Beneficiary(group.Key.Heir, group.Key.Recipient);
                    if (group.Any(share => share.Primary))
                    {
                        var text = new TextObject("{=BC_EstatePreview_Primary}{BENEFICIARY} (primary estate beneficiary)");
                        text.SetTextVariable("BENEFICIARY", beneficiary);
                        beneficiary = text.ToString();
                    }
                }
                rows.Add(Paragraph(beneficiary + "\n" + FromEstate(record, pending) + "\n"
                    + (assets.Count > 0 ? SummarizeAssets(assets)
                        : new TextObject("{=BC_EstatePreview_SettlementPending}Estate settlement outstanding").ToString())));
            }
        }

        private static string SummarizeAssets(List<string> assets)
        {
            string summary = string.Join("\n", assets.Take(TooltipAssetLimit));
            if (assets.Count <= TooltipAssetLimit)
                return summary;
            var text = new TextObject("{=BC_EstatePreview_MoreAssets}Plus {COUNT} more");
            text.SetTextVariable("COUNT", assets.Count - TooltipAssetLimit);
            return summary + "\n" + text;
        }

        private static List<Asset> GetAssets(CrossClanEstateRecord record, CrossClanEstateShare share, bool pending)
        {
            var assets = new List<Asset>();
            var shownBaronySettlements = new HashSet<string>();
            bool crownEstate = !share.DomesticOnly && record.PolicyVersion > 0
                || share.SovereignTransfers?.Count > 0;
            FeudalTitleBehavior titles = FeudalTitleBehavior.Instance;
            string politicalCrownId = titles?.GetKingdomPoliticalTitle(record.Realm)?.TitleId;
            IEnumerable<string> titleIds = Outstanding(share.Titles, share.DeliveredTitles, share.SupersededTitles, pending);
            if (pending)
                titleIds = titleIds.Union(Outstanding(share.SovereignTransfers,
                    share.DeliveredSovereignTransfers, share.SupersededTitles, true));
            foreach (var entry in titleIds.Select(id => new { Id = id, Title = titles?.GetTitle(id) })
                .OrderByDescending(entry => entry.Title?.TitleType ?? FeudalTitleType.Barony)
                .ThenBy(entry => entry.Title?.Name ?? entry.Id))
            {
                string id = entry.Id;
                // A political crown follows realm succession, not the ordinary estate's primary heir.
                if (id == record.RealmCrownTitleId || id == politicalCrownId)
                    continue;
                FeudalTitleRecord title = entry.Title;
                string name;
                if (title == null)
                {
                    var text = new TextObject("{=BC_EstatePreview_UnresolvedTitle}Unresolved title ({ID})");
                    text.SetTextVariable("ID", id);
                    name = text.ToString();
                }
                else
                {
                    name = FeudalTitleDisplayHelper.FormatTitleName(title, record.Source);
                    if (title.TitleType == FeudalTitleType.Barony && !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
                        shownBaronySettlements.Add(title.CapitalSettlementId);
                }
                bool sovereign = crownEstate && id == share.RootTitleId
                    || share.SovereignTransfers?.Contains(id) == true;
                assets.Add(new Asset(name, sovereign ? AssetKind.Sovereign : crownEstate ? AssetKind.CrownEstate : AssetKind.Ordinary));
            }
            foreach (string id in Outstanding(share.Fiefs, share.DeliveredFiefs, share.SupersededFiefs, pending))
            {
                // Only titles still displayed suppress their fiefs; a delivered title cannot hide pending possession.
                if (shownBaronySettlements.Contains(id))
                    continue;
                var text = new TextObject("{=BC_EstatePreview_Fief}Fief: {FIEF}");
                text.SetTextVariable("FIEF", Settlement.Find(id)?.Name?.ToString() ?? id);
                assets.Add(new Asset(text.ToString(), crownEstate ? AssetKind.CrownEstate : AssetKind.Ordinary));
            }
            return assets;
        }

        private static IEnumerable<string> Outstanding(IEnumerable<string> assigned, IEnumerable<string> delivered,
            IEnumerable<string> superseded, bool pending)
        {
            IEnumerable<string> ids = (assigned ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id));
            return pending
                ? ids.Except(delivered ?? Enumerable.Empty<string>()).Except(superseded ?? Enumerable.Empty<string>())
                : ids.Distinct();
        }

        private static string CrownEstateAsset(string name)
        {
            var text = new TextObject("{=BC_EstatePreview_CrownEstateAsset}{ASSET} (Crown estate)");
            text.SetTextVariable("ASSET", name);
            return text.ToString();
        }

        private static string SovereignTitle(string name)
        {
            var text = new TextObject("{=BC_EstatePreview_SovereignTitle}{TITLE} (sovereign succession)");
            text.SetTextVariable("TITLE", name);
            return text.ToString();
        }

        private static bool TryGetCrownHeir(Kingdom realm, out Hero heir, out FeudalTitleRecord title)
        {
            heir = null;
            title = null;
            if (realm == null || realm.IsEliminated || realm.RulingClan?.Leader?.IsAlive != true
                || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)
                || SuccessionRealmRules.Classify(SuccessionLawHelper.GetLawsForKingdom(realm).SuccessionLaw)
                    != RealmSuccessionSystem.Hereditary)
                return false;
            heir = HereditaryRealmSuccession.GetLine(realm).FirstOrDefault();
            if (heir == null)
                return false;
            title = FeudalTitleBehavior.Instance?.GetRealmSovereignTitle(realm, FeudalHierarchyMode.DeFacto)
                ?? FeudalTitleBehavior.Instance?.GetKingdomPoliticalTitle(realm);
            return true;
        }

        private static string FromEstate(CrossClanEstateRecord record, bool pending) =>
            FromPerson(record.Deceased, record.Source, pending
                ? "{=BC_EstatePreview_PendingFrom}Pending from {PERSON} of {HOUSE}"
                : "{=BC_EstatePreview_ExpectedFrom}Expected from {PERSON} of {HOUSE}");

        private static string FromPerson(Hero person, Clan house, string template)
        {
            var text = new TextObject(template);
            text.SetTextVariable("PERSON", PersonName(person));
            text.SetTextVariable("HOUSE", HouseName(house));
            return text.ToString();
        }

        private static string Beneficiary(Hero heir, Clan recipient)
        {
            var text = new TextObject("{=BC_EstatePreview_Beneficiary}{HEIR} of {HOUSE}");
            text.SetTextVariable("HEIR", PersonName(heir));
            text.SetTextVariable("HOUSE", HouseName(recipient));
            return text.ToString();
        }

        private static TextObject PersonName(Hero hero) => hero?.Name
            ?? new TextObject("{=BC_EstatePreview_UnknownPerson}Unknown person");

        private static TextObject HouseName(Clan clan) => clan?.Name
            ?? new TextObject("{=BC_EstatePreview_UnknownHouse}Unresolved house");

        private static TooltipProperty Heading(string text) =>
            new TooltipProperty(text, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.Title);

        private static TooltipProperty Paragraph(string text) =>
            new TooltipProperty(string.Empty, text, 0, false, TooltipProperty.TooltipPropertyFlags.MultiLine);

        private static TooltipProperty Separator() =>
            new TooltipProperty(string.Empty, string.Empty, 0, false, TooltipProperty.TooltipPropertyFlags.DefaultSeperator);
    }
}
