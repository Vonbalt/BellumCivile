using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BellumCivile
{
    internal sealed class FeudalInheritancePlan
    {
        public Clan ParentClan { get; set; }
        public Hero DeadLeader { get; set; }
        public Hero LegalClanHeir { get; set; }
        public int SuccessionType { get; set; }
        public GenderSuccessionLaw GenderLaw { get; set; }
        public HouseSuccessionLaw SuccessionLaw { get; set; }
        public SuccessionConfig.SuccessionRuleScope SuccessionScope { get; set; }
        public List<Hero> NaturalHeirs { get; } = new List<Hero>();
        public List<Town> EstateFiefs { get; } = new List<Town>();
        public List<FeudalTitleRecord> EstateTitles { get; } = new List<FeudalTitleRecord>();
        public FeudalTitleRecord PrimarySovereignTitle { get; set; }
        public List<FeudalTitleRecord> PrimaryTitleChain { get; } = new List<FeudalTitleRecord>();
        public List<Town> ReservedPersonalFiefs { get; } = new List<Town>();
        public List<FeudalInheritancePackage> SecondaryPackages { get; } = new List<FeudalInheritancePackage>();
        public string FailureReason { get; set; }

        public bool IsValid => ParentClan != null && DeadLeader != null && string.IsNullOrWhiteSpace(FailureReason);
    }

    internal sealed class FeudalInheritancePackage
    {
        public FeudalTitleRecord RootTitle { get; set; }
        public Town PrimaryFief { get; set; }
        public List<Town> Fiefs { get; } = new List<Town>();
        public List<FeudalTitleRecord> Titles { get; } = new List<FeudalTitleRecord>();
        public string DebugName { get; set; }
    }

    internal static class FeudalInheritancePlanner
    {
        internal static FeudalInheritancePackage GetLivingAccessionShare(
            FeudalInheritancePlan plan, Hero primary, Hero heir, bool partitionEnabled)
        {
            if (heir == null || primary == null) return null;
            if (heir == primary) return GetPrimaryHeirShare(plan, heir, partitionEnabled);
            if (!partitionEnabled) return null;
            int index = plan.NaturalHeirs.Where(h => h != primary).ToList().IndexOf(heir);
            return index >= 0 && index < plan.SecondaryPackages.Count ? plan.SecondaryPackages[index] : null;
        }

        internal static FeudalInheritancePackage GetPrimaryHeirShare(FeudalInheritancePlan plan, Hero heir, bool partitionEnabled)
        {
            // Only packages assignable to other heirs leave the primary heir's share.
            int assignments = partitionEnabled
                ? Math.Min(plan.SecondaryPackages.Count, plan.NaturalHeirs.Count(candidate => candidate != heir)) : 0;
            var assigned = plan.SecondaryPackages.Take(assignments).ToList();
            var otherFiefs = new HashSet<Town>(assigned.SelectMany(package => package.Fiefs));
            var otherTitles = new HashSet<string>(assigned.SelectMany(package => package.Titles).Select(title => title.TitleId));
            var otherSettlements = new HashSet<string>(otherFiefs.Where(f => f?.Settlement != null)
                .Select(f => f.Settlement.StringId));
            otherTitles.UnionWith(plan.EstateTitles.Where(t => t.TitleType == FeudalTitleType.Barony
                && otherSettlements.Contains(t.CapitalSettlementId)).Select(t => t.TitleId));
            var result = new FeudalInheritancePackage();
            result.Fiefs.AddRange(plan.EstateFiefs.Where(fief => !otherFiefs.Contains(fief)));
            result.Titles.AddRange(plan.EstateTitles.Where(title => !otherTitles.Contains(title.TitleId)));
            result.PrimaryFief = result.Fiefs.FirstOrDefault();
            return result;
        }

        internal static FeudalInheritancePackage GetHouseholdEstablishmentShare(
            FeudalInheritancePlan plan, FeudalTitleBehavior titles)
        {
            var empty = new FeudalInheritancePackage();
            if (titles == null || plan.EstateFiefs.Count <= 1) return empty;
            var protectedFiefs = new HashSet<Town>();
            // Protect the whole primary landed title, not merely its capital barony.
            var primary = plan.PrimaryTitleChain.Where(t => t.TitleType < FeudalTitleType.Kingdom)
                .OrderByDescending(t => t.TitleType).FirstOrDefault();
            Town retained = plan.EstateFiefs.FirstOrDefault(f => f.Settlement.StringId == primary?.CapitalSettlementId)
                ?? plan.ReservedPersonalFiefs.FirstOrDefault() ?? plan.EstateFiefs.First();
            protectedFiefs.Add(retained);
            if (primary != null)
                protectedFiefs.UnionWith(GetOwnedFiefsUnderTitle(titles, primary, plan.ParentClan, plan.EstateFiefs));
            var package = plan.SecondaryPackages.FirstOrDefault(p => p.Fiefs.Count > 0
                && p.Fiefs.Count < plan.EstateFiefs.Count
                && p.Fiefs.All(f => !protectedFiefs.Contains(f))
                && p.Titles.All(t => t.TitleType < FeudalTitleType.Kingdom && !plan.PrimaryTitleChain.Contains(t)));
            if (package != null) return package;
            // With no separate package, establish the household with one non-capital barony.
            Town fief = plan.EstateFiefs.Where(f => f != retained
                && plan.EstateTitles.Any(t => t.TitleType == FeudalTitleType.Barony
                    && t.CapitalSettlementId == f.Settlement.StringId)).OrderBy(f => f.Prosperity)
                .ThenBy(f => f.Settlement.StringId, StringComparer.Ordinal).FirstOrDefault();
            if (fief == null) return empty;
            var portion = new FeudalInheritancePackage { PrimaryFief = fief };
            portion.Fiefs.Add(fief);
            portion.Titles.AddRange(plan.EstateTitles.Where(t => t.TitleType == FeudalTitleType.Barony
                && t.CapitalSettlementId == fief.Settlement.StringId));
            return portion;
        }

        public static FeudalInheritancePlan BuildPlan(Clan parentClan, Hero deadLeader, int reservedPersonalHoldings)
        {
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalTitleRecord primarySovereign = parentClan != null && parentClan == parentClan.Kingdom?.RulingClan
                ? titleBehavior?.GetRealmSovereignTitle(parentClan.Kingdom, FeudalHierarchyMode.DeFacto)
                    ?? titleBehavior?.GetKingdomPoliticalTitle(parentClan.Kingdom)
                : null;
            return BuildPlan(
                parentClan,
                deadLeader,
                GetPartitionableFiefs(parentClan),
                GetOrderedNaturalHeirs(parentClan, deadLeader),
                null,
                reservedPersonalHoldings,
                primarySovereign?.TitleId ?? string.Empty);
        }

        public static FeudalInheritancePlan BuildPlan(
            Clan parentClan,
            Hero deadLeader,
            IReadOnlyCollection<Town> estateFiefs,
            IReadOnlyCollection<Hero> naturalHeirs,
            IReadOnlyCollection<FeudalTitleRecord> estateTitles,
            int reservedPersonalHoldings,
            string preferredPrimaryTitleId = "")
        {
            SuccessionLawSet laws = SuccessionLawHelper.GetLawsForClan(parentClan, out SuccessionConfig.SuccessionRuleScope scope);
            FeudalInheritancePlan plan = new FeudalInheritancePlan
            {
                ParentClan = parentClan,
                DeadLeader = deadLeader,
                SuccessionType = SuccessionConfig.ToLegacyType(laws),
                GenderLaw = laws.GenderLaw,
                SuccessionLaw = laws.SuccessionLaw,
                SuccessionScope = scope
            };

            if (parentClan == null)
            {
                plan.FailureReason = "missing parent clan";
                return plan;
            }

            if (deadLeader == null)
            {
                plan.FailureReason = "missing dead/current leader";
                return plan;
            }

            reservedPersonalHoldings = Math.Max(1, reservedPersonalHoldings);
            FeudalTitleBehavior titleBehavior = Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();

            plan.LegalClanHeir = SuccessionLawHelper.GetOrderedSuccessionLine(
                parentClan,
                deadLeader,
                laws).FirstOrDefault();
            plan.NaturalHeirs.AddRange((naturalHeirs ?? GetOrderedNaturalHeirs(parentClan, deadLeader))
                .Where(hero => hero != null
                    && CrownAccessionBehavior.Instance?.HasInheritanceAdvance(deadLeader, hero) != true
                    && SuccessionLawHelper.IsEligibleUnderSuccessionLaws(hero, deadLeader, laws))
                .Distinct()
                .ToList());
            plan.EstateFiefs.AddRange((estateFiefs ?? GetPartitionableFiefs(parentClan))
                .Where(fief => IsHeritableFief(fief, parentClan))
                .OrderByDescending(fief => fief.Prosperity)
                .ToList());

            if (estateTitles != null)
            {
                plan.EstateTitles.AddRange(estateTitles
                    .Where(title => title != null && title.IsActive)
                    .GroupBy(title => title.TitleId)
                    .Select(group => group.First())
                    .OrderByDescending(title => title.TitleType)
                    .ThenBy(title => title.Name)
                    .ToList());
            }
            else if (titleBehavior != null)
            {
                plan.EstateTitles.AddRange(titleBehavior.GetTitlesHeldByClan(parentClan, deJure: true)
                    .Where(title => title != null && title.IsActive)
                    .OrderByDescending(title => title.TitleType)
                    .ThenBy(title => title.Name)
                    .ToList());
            }

            if (titleBehavior != null)
                plan.EstateTitles.RemoveAll(title => IsAllocationCustodyTitle(title, titleBehavior, parentClan));

            if (plan.EstateFiefs.Count == 0)
            {
                plan.FailureReason = "clan holds no partitionable fiefs";
                return plan;
            }

            BuildTitlePackages(plan, titleBehavior, reservedPersonalHoldings, preferredPrimaryTitleId);
            return plan;
        }

        internal static void BuildTitlePackages(FeudalInheritancePlan plan, FeudalTitleBehavior titleBehavior,
            int reservedPersonalHoldings, string preferredPrimaryTitleId)
        {
            BuildPrimaryTitleChain(plan, titleBehavior, preferredPrimaryTitleId);
            BuildReservedPersonalFiefs(plan, titleBehavior, reservedPersonalHoldings);
            BuildSecondaryPackages(plan, titleBehavior);
        }

        // Opt-in until the partition executor can establish a house without a personal fief.
        // Build into a temporary plan so invalid snapshots leave the caller untouched.
        internal static bool TryBuildCrownFirstTitlePackages(FeudalInheritancePlan plan,
            FeudalTitleBehavior titleBehavior, IReadOnlyCollection<FeudalTitleRecord> titleSnapshot,
            int reservedPersonalHoldings, string primaryId, out string reason)
        {
            reason = null;
            if (plan?.ParentClan == null || titleBehavior == null)
            { reason = "missing estate owner or title service"; return false; }
            string house = plan.ParentClan.StringId;
            if (!FeudalCrownPartitionPlan.TryCreate(house, primaryId, titleSnapshot, out var crowns, out reason))
                return false;
            var byId = titleSnapshot.ToDictionary(t => t.TitleId, StringComparer.Ordinal);
            if (titleSnapshot.Where(t => t.IsActive && t.TitleType == FeudalTitleType.Barony
                    && !string.IsNullOrEmpty(t.CapitalSettlementId))
                .GroupBy(t => t.CapitalSettlementId).Any(g => g.Count() > 1))
            { reason = "multiple active baronies describe the same physical land"; return false; }
            var estateIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var title in plan.EstateTitles)
            {
                if (title == null || !estateIds.Add(title.TitleId) || !byId.TryGetValue(title.TitleId, out var captured)
                    || !captured.IsActive || captured.DeJureHolderClanId != house
                    || !ReferenceEquals(title, captured))
                { reason = "estate titles do not match the supplied legal snapshot"; return false; }
            }
            if (crowns.Any(c => !estateIds.Contains(c.CrownId)))
            { reason = "a selected Crown is absent from the personal title estate"; return false; }
            if (plan.EstateFiefs.Any(f => f?.Settlement == null || f.OwnerClan != plan.ParentClan)
                || plan.EstateFiefs.Select(f => f.Settlement.StringId).Distinct().Count() != plan.EstateFiefs.Count)
            { reason = "personal fief snapshot contains missing, duplicate or foreign holdings"; return false; }

            var crownPackages = new List<FeudalInheritancePackage>();
            var allocatedTitles = new HashSet<string>(StringComparer.Ordinal);
            var allocatedFiefs = new HashSet<Town>();
            foreach (var crown in crowns.Skip(1))
            {
                var ids = new HashSet<string>(crown.TitleIds, StringComparer.Ordinal);
                var settlementIds = new HashSet<string>(ids.Select(id => byId[id])
                    .Where(t => t.TitleType == FeudalTitleType.Barony && !string.IsNullOrEmpty(t.CapitalSettlementId))
                    .Select(t => t.CapitalSettlementId), StringComparer.Ordinal);
                var package = new FeudalInheritancePackage
                {
                    RootTitle = byId[crown.CrownId], DebugName = "crown:" + crown.CrownId
                };
                package.Titles.AddRange(plan.EstateTitles.Where(t => ids.Contains(t.TitleId))
                    .OrderByDescending(t => t.TitleType).ThenBy(t => t.TitleId, StringComparer.Ordinal));
                package.Fiefs.AddRange(plan.EstateFiefs.Where(f => settlementIds.Contains(f.Settlement.StringId))
                    .OrderByDescending(f => f.Prosperity).ThenBy(f => f.Settlement.StringId, StringComparer.Ordinal));
                if (package.Fiefs.Any(f => !allocatedFiefs.Add(f)))
                { reason = "secondary Crown packages overlap in physical land"; return false; }
                allocatedTitles.UnionWith(package.Titles.Select(t => t.TitleId));
                package.PrimaryFief = ResolvePrimaryFiefForPackage(package.RootTitle, package.Fiefs);
                crownPackages.Add(package);
            }
            var remainder = new FeudalInheritancePlan { ParentClan = plan.ParentClan };
            remainder.EstateTitles.AddRange(plan.EstateTitles.Where(t => !allocatedTitles.Contains(t.TitleId)));
            remainder.EstateFiefs.AddRange(plan.EstateFiefs.Where(f => !allocatedFiefs.Contains(f)));
            BuildPrimaryTitleChain(remainder, titleBehavior, primaryId);
            remainder.PrimarySovereignTitle = byId[primaryId];
            // Bound coequal Crowns stay with the primary heir, while their lesser estates
            // may still follow the ordinary landed-partition rules.
            foreach (var title in remainder.EstateTitles.Where(t => t.TitleType == byId[primaryId].TitleType
                && t.DeFactoHolderClanId == house))
                if (!remainder.PrimaryTitleChain.Contains(title)) remainder.PrimaryTitleChain.Add(title);
            BuildReservedPersonalFiefs(remainder, titleBehavior, Math.Max(1, reservedPersonalHoldings));
            BuildSecondaryPackages(remainder, titleBehavior);

            plan.PrimarySovereignTitle = remainder.PrimarySovereignTitle;
            plan.PrimaryTitleChain.Clear();
            plan.PrimaryTitleChain.AddRange(remainder.PrimaryTitleChain);
            plan.ReservedPersonalFiefs.Clear();
            plan.ReservedPersonalFiefs.AddRange(remainder.ReservedPersonalFiefs);
            plan.SecondaryPackages.Clear();
            plan.SecondaryPackages.AddRange(crownPackages);
            plan.SecondaryPackages.AddRange(remainder.SecondaryPackages);
            return true;
        }

        public static IReadOnlyCollection<Hero> GetProspectiveInheritanceBeneficiaries(
            Clan parentClan,
            int reservedPersonalHoldings,
            bool includePartitionBeneficiaries)
        {
            HashSet<Hero> beneficiaries = new HashSet<Hero>();
            Hero currentLeader = parentClan?.Leader;
            if (parentClan == null || currentLeader == null || currentLeader.IsDead)
                return beneficiaries;

            Hero legalHeir = SuccessionLawHelper.GetOrderedSuccessionLine(parentClan)
                .FirstOrDefault(hero => hero != null
                    && hero != currentLeader
                    && hero.Clan == parentClan
                    && hero.IsAlive
                    && !hero.IsDisabled);
            if (legalHeir != null)
                beneficiaries.Add(legalHeir);

            if (!includePartitionBeneficiaries)
                return beneficiaries;

            FeudalInheritancePlan plan = BuildPlan(parentClan, currentLeader, reservedPersonalHoldings);
            if (!plan.IsValid || plan.SecondaryPackages.Count == 0)
                return beneficiaries;

            int secondaryAssignments = plan.SecondaryPackages.Count;
            foreach (Hero secondaryHeir in plan.NaturalHeirs
                .Where(hero => hero != null && hero != legalHeir && hero != currentLeader)
                .Take(secondaryAssignments))
            {
                beneficiaries.Add(secondaryHeir);
            }

            return beneficiaries;
        }

        private static List<Hero> GetOrderedNaturalHeirs(Clan clan, Hero deadLeader)
        {
            if (clan == null || deadLeader == null)
                return new List<Hero>();

            SuccessionLawSet laws = SuccessionLawHelper.GetLawsForClan(clan);
            List<Hero> candidates = clan.Heroes
                .Where(hero => IsValidNaturalHeirAtDeath(hero, clan, deadLeader)
                    && SuccessionLawHelper.IsEligibleUnderSuccessionLaws(hero, deadLeader, laws))
                .ToList();
            return SuccessionLawHelper.OrderSuccessionCandidates(candidates, deadLeader, laws);
        }

        private static bool IsValidNaturalHeirAtDeath(Hero hero, Clan clan, Hero deadLeader)
        {
            return hero != null
                && clan != null
                && deadLeader != null
                && hero != deadLeader
                && hero.Clan == clan
                && hero.IsAlive
                && hero.IsLord
                && !hero.IsChild
                && hero.Age >= SuccessionLawHelper.GetAgeOfMajority()
                && !hero.IsDisabled
                && SuccessionLawHelper.IsBloodRelative(hero, deadLeader);
        }

        internal static bool IsHeritableFief(Town fief, Clan clan)
        {
            return clan != null && fief?.Settlement != null && fief.OwnerClan == clan
                && !FiefDeliberationBehavior.IsAwaitingAllocation(fief.Settlement);
        }

        internal static bool IsAllocationCustodyTitle(FeudalTitleRecord title,
            FeudalTitleBehavior titles, Clan clan)
        {
            // Sovereign titles and established mixed estates remain inheritable.
            if (title == null || titles == null || clan == null || title.TitleType >= FeudalTitleType.Kingdom)
                return false;
            var holdings = titles.GetTitleAndDescendants(title)
                .Where(child => child.IsActive && child.TitleType == FeudalTitleType.Barony)
                .Select(child => Settlement.Find(child.CapitalSettlementId)?.Town)
                .Where(fief => fief != null && fief.OwnerClan == clan).ToList();
            return holdings.Count > 0 && holdings.All(fief => !IsHeritableFief(fief, clan));
        }

        private static List<Town> GetPartitionableFiefs(Clan clan)
        {
            return clan?.Fiefs?
                .Where(fief => IsHeritableFief(fief, clan))
                .OrderByDescending(fief => fief.Prosperity)
                .ToList() ?? new List<Town>();
        }

        private static void BuildPrimaryTitleChain(
            FeudalInheritancePlan plan,
            FeudalTitleBehavior titleBehavior,
            string preferredPrimaryTitleId)
        {
            if (plan == null || titleBehavior == null || plan.EstateTitles.Count == 0 || plan.EstateFiefs.Count == 0)
                return;

            FeudalTitleRecord preferredTitle = string.IsNullOrWhiteSpace(preferredPrimaryTitleId)
                ? null
                : plan.EstateTitles.FirstOrDefault(title => string.Equals(title.TitleId, preferredPrimaryTitleId, StringComparison.Ordinal));
            plan.PrimarySovereignTitle = preferredTitle;

            Town primaryFief = preferredTitle == null
                ? plan.EstateFiefs.FirstOrDefault()
                : ResolvePrimaryFiefForPackage(
                    preferredTitle,
                    GetOwnedFiefsUnderTitle(titleBehavior, preferredTitle, plan.ParentClan, plan.EstateFiefs));
            primaryFief = primaryFief ?? plan.EstateFiefs.FirstOrDefault();
            string primarySettlementId = primaryFief?.Settlement?.StringId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(primarySettlementId))
                return;

            plan.PrimaryTitleChain.AddRange(plan.EstateTitles
                .Where(title => TitleContainsSettlement(titleBehavior, title, primarySettlementId))
                .OrderByDescending(title => title.TitleType)
                .ThenBy(title => title.Name)
                .ToList());

            if (preferredTitle != null && plan.PrimaryTitleChain.All(title => title.TitleId != preferredTitle.TitleId))
                plan.PrimaryTitleChain.Insert(0, preferredTitle);
        }

        private static void BuildReservedPersonalFiefs(
            FeudalInheritancePlan plan,
            FeudalTitleBehavior titleBehavior,
            int reservedPersonalHoldings)
        {
            if (plan == null)
                return;

            List<Town> ordered = new List<Town>();
            if (plan.PrimarySovereignTitle != null && titleBehavior != null)
            {
                ordered.AddRange(GetOwnedFiefsUnderTitle(
                        titleBehavior,
                        plan.PrimarySovereignTitle,
                        plan.ParentClan,
                        plan.EstateFiefs)
                    .OrderBy(fief => fief?.Settlement?.StringId == plan.PrimarySovereignTitle.CapitalSettlementId ? 0 : 1)
                    .ThenByDescending(fief => fief?.Prosperity ?? 0f));
            }

            ordered.AddRange(plan.EstateFiefs.Where(fief => !ordered.Contains(fief)));
            plan.ReservedPersonalFiefs.AddRange(ordered.Take(reservedPersonalHoldings));
        }

        private static void BuildSecondaryPackages(FeudalInheritancePlan plan, FeudalTitleBehavior titleBehavior)
        {
            if (plan == null)
                return;

            HashSet<string> assignedSettlementIds = new HashSet<string>(plan.ReservedPersonalFiefs
                .Where(fief => fief?.Settlement != null)
                .Select(fief => fief.Settlement.StringId));

            HashSet<string> estateTitleIds = new HashSet<string>(plan.EstateTitles
                .Where(title => title != null && title.IsActive)
                .Select(title => title.TitleId));

            HashSet<string> primaryChainTitleIds = new HashSet<string>(plan.PrimaryTitleChain
                .Where(title => title != null && title.IsActive)
                .Select(title => title.TitleId));

            if (titleBehavior != null && plan.EstateTitles.Count > 0)
            {
                List<FeudalTitleRecord> selectedPackageRoots = new List<FeudalTitleRecord>();
                foreach (FeudalTitleRecord title in plan.EstateTitles
                    .Where(title => title != null
                        && title.IsActive
                        && title.TitleType > FeudalTitleType.Barony
                        && !primaryChainTitleIds.Contains(title.TitleId))
                    .OrderByDescending(title => title.TitleType)
                    .ThenByDescending(title => SumOwnedFiefProsperity(titleBehavior, title, plan.ParentClan, plan.EstateFiefs)))
                {
                    if (selectedPackageRoots.Any(root => TitleContainsTitle(titleBehavior, root, title)))
                        continue;

                    List<Town> packageFiefs = GetOwnedFiefsUnderTitle(titleBehavior, title, plan.ParentClan, plan.EstateFiefs)
                        .Where(fief => fief?.Settlement != null && !assignedSettlementIds.Contains(fief.Settlement.StringId))
                        .OrderByDescending(fief => fief.Prosperity)
                        .ToList();

                    if (packageFiefs.Count == 0)
                        continue;

                    FeudalInheritancePackage package = new FeudalInheritancePackage
                    {
                        RootTitle = title,
                        PrimaryFief = ResolvePrimaryFiefForPackage(title, packageFiefs),
                        DebugName = $"{title.TitleType}:{title.TitleId}"
                    };

                    package.Fiefs.AddRange(packageFiefs);
                    package.Titles.AddRange(titleBehavior.GetTitleAndDescendants(title)
                        .Where(candidate => candidate != null
                            && candidate.IsActive
                            && candidate.TitleType > FeudalTitleType.Barony
                            && estateTitleIds.Contains(candidate.TitleId)
                            && !primaryChainTitleIds.Contains(candidate.TitleId))
                        .OrderByDescending(candidate => candidate.TitleType)
                        .ThenBy(candidate => candidate.Name)
                        .ToList());

                    foreach (Town packageFief in packageFiefs)
                        assignedSettlementIds.Add(packageFief.Settlement.StringId);

                    selectedPackageRoots.Add(title);
                    plan.SecondaryPackages.Add(package);
                }
            }

            foreach (Town looseFief in plan.EstateFiefs
                .Where(fief => fief?.Settlement != null && !assignedSettlementIds.Contains(fief.Settlement.StringId)))
            {
                assignedSettlementIds.Add(looseFief.Settlement.StringId);
                FeudalTitleRecord baronyTitle = null;
                if (titleBehavior != null
                    && titleBehavior.TryGetBarony(looseFief.Settlement, out FeudalTitleRecord resolvedBarony)
                    && resolvedBarony != null
                    && estateTitleIds.Contains(resolvedBarony.TitleId)
                    && !primaryChainTitleIds.Contains(resolvedBarony.TitleId))
                {
                    baronyTitle = resolvedBarony;
                }

                FeudalInheritancePackage package = new FeudalInheritancePackage
                {
                    RootTitle = baronyTitle,
                    PrimaryFief = looseFief,
                    DebugName = $"barony:{looseFief.StringId}"
                };
                package.Fiefs.Add(looseFief);
                if (baronyTitle != null)
                    package.Titles.Add(baronyTitle);
                plan.SecondaryPackages.Add(package);
            }
        }

        private static bool TitleContainsSettlement(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title, string settlementId)
        {
            if (titleBehavior == null || title == null || string.IsNullOrWhiteSpace(settlementId))
                return false;

            return titleBehavior.GetTitleAndDescendants(title)
                .Any(child => child != null && child.CapitalSettlementId == settlementId);
        }

        private static bool TitleContainsTitle(FeudalTitleBehavior titleBehavior, FeudalTitleRecord root, FeudalTitleRecord possibleChild)
        {
            if (titleBehavior == null || root == null || possibleChild == null)
                return false;

            return titleBehavior.GetTitleAndDescendants(root)
                .Any(title => title != null && title.TitleId == possibleChild.TitleId);
        }

        private static List<Town> GetOwnedFiefsUnderTitle(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title, Clan ownerClan, List<Town> estateFiefs)
        {
            if (titleBehavior == null || title == null || ownerClan == null || estateFiefs == null)
                return new List<Town>();

            HashSet<string> settlementIds = new HashSet<string>(titleBehavior.GetDescendantBaronyTitles(title)
                .Where(barony => barony != null && barony.IsActive && !string.IsNullOrWhiteSpace(barony.CapitalSettlementId))
                .Select(barony => barony.CapitalSettlementId));

            if (title.TitleType == FeudalTitleType.Barony && !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
                settlementIds.Add(title.CapitalSettlementId);

            return estateFiefs
                .Where(fief => fief?.Settlement != null
                    && fief.OwnerClan == ownerClan
                    && settlementIds.Contains(fief.Settlement.StringId))
                .ToList();
        }

        private static float SumOwnedFiefProsperity(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title, Clan ownerClan, List<Town> estateFiefs)
        {
            return GetOwnedFiefsUnderTitle(titleBehavior, title, ownerClan, estateFiefs)
                .Sum(fief => fief?.Prosperity ?? 0f);
        }

        private static Town ResolvePrimaryFiefForPackage(FeudalTitleRecord title, List<Town> packageFiefs)
        {
            if (title != null && !string.IsNullOrWhiteSpace(title.CapitalSettlementId))
            {
                Town capital = packageFiefs.FirstOrDefault(fief => fief?.Settlement?.StringId == title.CapitalSettlementId);
                if (capital != null)
                    return capital;
            }

            return packageFiefs.OrderByDescending(fief => fief?.Prosperity ?? 0f).FirstOrDefault();
        }

    }
}
