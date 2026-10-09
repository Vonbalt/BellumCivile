using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public partial class PartitionSuccessionBehavior
    {
        private List<CrossClanEstateRecord> _crossClanEstates = new List<CrossClanEstateRecord>();

        internal static List<string> SnapshotEstateFiefs(IEnumerable<Town> fiefs) =>
            fiefs.Select(f => f.Settlement.StringId).ToList();

        internal static List<string> CanonicalizeEstateFiefIds(IEnumerable<string> ids,
            IReadOnlyDictionary<string, string> componentSettlements) => ids
            .Select(id => componentSettlements.TryGetValue(id, out string settlementId) ? settlementId : id)
            .Distinct(StringComparer.Ordinal).ToList();

        private static void RepairEstateFiefIds(CrossClanEstateRecord record)
        {
            if (!record.Shares.SelectMany(s => s.Fiefs.Concat(s.DeliveredFiefs)
                .Concat(s.CadetPlan?.EndowmentFiefs ?? Enumerable.Empty<string>()))
                .Any(id => Settlement.Find(id) == null)) return;
            // Older snapshots used Town.StringId, which is a different object registry
            // from Settlement.StringId. Resolve real objects, never infer IDs by spelling.
            var aliases = Settlement.All.Where(s => s.Town != null && !string.IsNullOrWhiteSpace(s.Town.StringId)
                    && Settlement.Find(s.Town.StringId) == null)
                .ToDictionary(s => s.Town.StringId, s => s.StringId, StringComparer.Ordinal);
            foreach (var share in record.Shares)
            {
                share.Fiefs = CanonicalizeEstateFiefIds(share.Fiefs, aliases);
                share.DeliveredFiefs = CanonicalizeEstateFiefIds(share.DeliveredFiefs, aliases);
                if (share.CadetPlan != null)
                    share.CadetPlan.EndowmentFiefs = CanonicalizeEstateFiefIds(share.CadetPlan.EndowmentFiefs, aliases);
            }
        }

        internal static List<Hero> OrderEstateHeirs(IEnumerable<Hero> household, Hero parent, SuccessionLawSet laws)
        {
            var descendants = new HashSet<Hero>();
            var pending = new Stack<Hero>();
            if (parent != null) pending.Push(parent);
            while (pending.Count > 0)
            {
                Hero current = pending.Pop();
                if (!descendants.Add(current)) continue;
                foreach (Hero child in current.Children) pending.Push(child);
            }
            return SuccessionLawHelper.OrderSuccessionCandidates(household.Concat(descendants)
                .Where(h => h != null && h != parent && h.Clan != null && !h.Clan.IsEliminated
                    && !h.IsWanderer && !h.IsNotable
                    && (descendants.Contains(h) || SuccessionLawHelper.IsBloodRelative(h, parent))),
                parent, laws, includeUnderage: true);
        }

        public void CaptureCrossClanEstate(Hero victim)
        {
            Clan source = victim?.Clan;
            if (source == null || source.Leader != victim || !CanPartitionClan(source)
                || RegencyBehavior.Instance?.GetLegalClanHead(source) is Hero ward && ward != victim
                || _crossClanEstates.Any(r => r.Deceased == victim)) return;
            var heirs = OrderEstateHeirs(source.Heroes, victim, SuccessionLawHelper.GetLawsForClan(source));
            if (!heirs.Any(h => h.Clan != source)) return;
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null) return;
            // Keep the same full hierarchy and primary-title anchor as local partition.
            // Sovereign execution is separated after planning, never by filtering inputs.
            var estateTitles = titles.GetTitlesHeldByClan(source, deJure: true)
                .Where(t => t.IsActive).ToList();
            var primarySovereign = source == source.Kingdom?.RulingClan
                ? titles.GetRealmSovereignTitle(source.Kingdom, FeudalHierarchyMode.DeFacto)
                    ?? titles.GetKingdomPoliticalTitle(source.Kingdom) : null;
            var plan = FeudalInheritancePlanner.BuildPlan(source, victim, GetPartitionableFiefs(source),
                heirs, estateTitles, GetMainHeirReservedFiefs(), primarySovereign?.TitleId ?? string.Empty);
            var record = new CrossClanEstateRecord
            {
                Source = source, Deceased = victim, Realm = source.Kingdom,
                PreferredPrimaryTitleId = primarySovereign?.TitleId,
                RealmCrownTitleId = source == source.Kingdom?.RulingClan
                    ? titles.GetKingdomPoliticalTitle(source.Kingdom)?.TitleId : null,
                ReadyDate = CampaignTime.Now + CampaignTime.Hours(1f)
            };
            for (int i = 0; i < heirs.Count; i++)
            {
                var share = SnapshotEstateShare(plan, heirs[0], heirs[i],
                    BellumCivileOptions.EnablePartitionSuccession);
                if (share != null) record.Shares.Add(share);
            }
            if (!record.Shares.Any(s => s.Heir.Clan != source && (s.Fiefs.Count > 0 || s.Titles.Count > 0))) return;
            _crossClanEstates.Add(record);
            BellumCivileLogger.Log($"Cross-clan death estate captured; deceased={victim.StringId}; source={source.StringId}; shares={record.Shares.Count}; heirs={string.Join(",", record.Shares.Select(s => s.Heir.StringId))}.");
        }

        internal static CrossClanEstateShare SnapshotEstateShare(FeudalInheritancePlan plan,
            Hero primary, Hero heir, bool partitionEnabled)
        {
            var package = FeudalInheritancePlanner.GetLivingAccessionShare(plan, primary, heir, partitionEnabled);
            if (package == null) return null;
            return new CrossClanEstateShare
            {
                Heir = heir, Primary = heir == primary,
                RootTitleId = package.RootTitle?.TitleId
                    ?? (heir == primary ? plan.PrimarySovereignTitle?.TitleId : null),
                PrimaryFiefId = package.PrimaryFief?.Settlement?.StringId,
                Fiefs = SnapshotEstateFiefs(package.Fiefs),
                // Local partition legalizes baronies with their fiefs, separately from
                // the upper-title list. The journal needs explicit receipts for both.
                Titles = package.Titles.Concat(plan.EstateTitles.Where(t => t.TitleType == FeudalTitleType.Barony
                    && package.Fiefs.Any(f => f.Settlement.StringId == t.CapitalSettlementId)))
                    .Select(t => t.TitleId).Distinct().ToList()
            };
        }

        internal static bool NeedsLandedSettlement(CrossClanEstateShare share, Hero heir) =>
            share.Heir == heir && !share.Completed && !share.LandedSettled;

        internal static bool IsUnsupportedSovereignPackage(CrossClanEstateRecord record,
            IEnumerable<FeudalTitleRecord> titles) => titles.Any(t => t != null
                && t.IsActive && t.TitleType >= FeudalTitleType.Kingdom && t.TitleId != record.RealmCrownTitleId);

        private static bool RequiresSovereignEstateExecutor(CrossClanEstateRecord record,
            FeudalTitleBehavior titles, FeudalTitleRecord title)
        {
            if (!IsUnsupportedSovereignPackage(record, new[] { title })) return false;
            // A legal-only title is property, not authority to take over its current government.
            if (title.DeFactoHolderClanId != record.Source.StringId) return false;
            if (titles.IsLandlessHistoricalCrown(title)) return false;
            var visited = new HashSet<string>();
            var parent = titles.GetParentTitle(title, FeudalHierarchyMode.DeFacto);
            while (parent != null && visited.Add(parent.TitleId))
            {
                if (parent.IsActive && parent.TitleType > title.TitleType) return false;
                parent = titles.GetParentTitle(parent, FeudalHierarchyMode.DeFacto);
            }
            // Parent links are not sufficient evidence that a fully-held coequal Crown is ordinary property.
            return true;
        }

        internal static void ReconcileEstateTitleReceipt(CrossClanEstateRecord record,
            CrossClanEstateShare share, FeudalTitleRecord title, Clan recipient)
        {
            if (!title.IsActive || title.DeJureHolderClanId != record.Source.StringId
                && title.DeJureHolderClanId != recipient.StringId)
                SupersedeEstateTitle(share, title.TitleId);
            else if (title.DeJureHolderClanId == recipient.StringId && recipient != record.Source
                && !share.DeliveredTitles.Contains(title.TitleId))
                share.DeliveredTitles.Add(title.TitleId);
        }

        internal IEnumerable<string> GetCadetNamingFiefIds(CrownAccessionRecord record)
        {
            // Naming may inspect a promised estate without moving its delivery into
            // the Crown's living-endowment plan or acquiring another heir's package.
            return record.EndowmentFiefs.Concat(_crossClanEstates
                .Where(r => !r.Completed && r.Deceased.IsDead)
                .SelectMany(r => r.Shares)
                .Where(s => record.Heir != null && s.Heir == record.Heir && !s.Completed)
                .SelectMany(s => s.Fiefs)).Distinct(StringComparer.Ordinal);
        }

        internal bool HasCrossClanEstate(Hero deceased) => deceased != null
            && _crossClanEstates.Any(r => r.Deceased == deceased);

        internal bool HasCrossClanShare(Hero heir) => heir != null && _crossClanEstates.Any(r =>
            r.Deceased.IsDead && r.Shares.Any(s => s.Heir == heir && !s.Completed));

        internal bool SettleCrownDeathEstate(Hero heir)
        {
            foreach (var record in _crossClanEstates.Where(r => !r.Completed && r.Deceased.IsDead
                && r.Shares.Any(s => s.Heir == heir && !s.Completed)).ToList())
                TrySettleCrossClanEstate(record, heir);
            return !_crossClanEstates.Any(r => !r.Completed && r.Deceased.IsDead
                && r.Shares.Any(s => NeedsLandedSettlement(s, heir)));
        }

        internal bool KeepCrossClanEstateHouse(Clan house) => house != null
            && _crossClanEstates.Any(r => r.Source == house && !r.Completed && r.Deceased.IsDead);

        private void ProcessCrossClanEstates()
        {
            foreach (var record in _crossClanEstates.Where(r => !r.Completed && r.Deceased.IsDead).ToList())
                TrySettleCrossClanEstate(record);
            foreach (var record in _crossClanEstates.Where(r => r.Completed && !r.Source.IsEliminated
                && r.Source.Leader?.IsAlive != true && r.Source.Kingdom?.RulingClan != r.Source).ToList())
                DestroyClanAction.ApplyByClanLeaderDeath(record.Source);
        }

        private void TrySettleCrossClanEstate(CrossClanEstateRecord record, Hero readyCrownHeir = null)
        {
            if (record.ReadyDate.IsFuture) return;
            try
            {
                RepairEstateFiefIds(record);
                var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
                if (titles == null) return;
                if (!record.GoldPrepared)
                {
                    record.GoldDonor = record.Source.Leader?.IsAlive == true ? record.Source.Leader : null;
                    record.GoldPerShare = record.GoldDonor == null ? 0
                        : Math.Max(0, record.GoldDonor.Gold) / record.Shares.Count;
                    record.GoldPrepared = true;
                }
                foreach (var share in record.Shares.Where(s => !s.Completed))
                {
                    try { SettleCrossClanShare(record, share, titles, readyCrownHeir); }
                    catch (Exception ex) { ReportEstateShare(record, share, "deferred: " + ex.Message); }
                }
                if (record.Shares.All(s => s.Completed))
                    titles.RegisterPartitionHouseClaims(record.Source, record.Deceased,
                        record.Shares.Where(s => !s.BeneficiaryReconciled && s.Recipient != null
                            && s.Heir?.IsAlive == true && s.Heir.Clan == s.Recipient)
                            .Select(s => Tuple.Create(s.Recipient, s.Heir)).ToList(),
                        record.Shares.SelectMany(s => s.DeliveredFiefs).Distinct().Select(Settlement.Find)
                            .Where(s => s?.Town != null).Select(s => s.Town).ToList(),
                        record.Shares.SelectMany(s => s.DeliveredTitles).Distinct().Select(titles.GetTitle)
                            .Where(t => t != null).ToList());
                record.Completed = record.Shares.All(s => s.Completed);
                if (record.Completed) record.Failure = null;
                if (record.Completed && !record.Source.IsEliminated && record.Source.Leader?.IsAlive != true
                    && record.Source.Kingdom?.RulingClan != record.Source)
                    DestroyClanAction.ApplyByClanLeaderDeath(record.Source);
            }
            catch (Exception ex)
            {
                if (record.Failure != ex.Message)
                    BellumCivileLogger.Log($"Cross-clan death estate deferred; deceased={record.Deceased.StringId}; reason={ex}");
                record.Failure = ex.Message;
            }
        }

        private void SettleCrossClanShare(CrossClanEstateRecord record, CrossClanEstateShare share,
            FeudalTitleBehavior titles, Hero readyCrownHeir)
        {
            PreservePendingCadetHead(share.CadetPlan?.Cadet, share.CadetPlan?.Heir);
            share.SupersededFiefs = share.SupersededFiefs ?? new List<string>();
            share.SupersededTitles = share.SupersededTitles ?? new List<string>();
            share.SovereignTransfers = share.SovereignTransfers ?? new List<string>();
            share.DeliveredSovereignTransfers = share.DeliveredSovereignTransfers ?? new List<string>();
            // Accession owns the political Crown, independently of the blood heir's property.
            if (share.Titles.Contains(record.RealmCrownTitleId)
                && Kingdom.All.Any(realm => !realm.IsEliminated
                    && titles.GetKingdomPoliticalTitle(realm)?.TitleId == record.RealmCrownTitleId))
                SupersedeEstateTitle(share, record.RealmCrownTitleId);
            if (EstateDeliveryComplete(share))
            {
                share.LandedSettled = true;
                share.Completed = true;
                ReportEstateShare(record, share, "completed from existing delivery receipts");
                return;
            }
            Hero heir = share.Heir;
            if (heir == null)
            { ReportEstateShare(record, share, "awaiting unresolved beneficiary reference"); return; }
            if (CrownAccessionBehavior.Instance?.IsPendingCrownHeir(heir) == true && readyCrownHeir != heir)
            { ReportEstateShare(record, share, "awaiting Crown household preparation"); return; }
            if (heir?.IsAlive != true || heir.Clan == null || heir.Clan.IsEliminated)
            {
                Hero successor = OrderEstateHeirs(
                    heir.Clan?.Heroes ?? Enumerable.Empty<Hero>(), heir,
                    SuccessionLawHelper.GetLawsForClan(heir.Clan ?? record.Source)).FirstOrDefault();
                if (successor == null)
                {
                    // With no surviving blood successor, untransferred property remains with the estate house.
                    successor = record.Source.Leader;
                    if (successor?.IsAlive != true || successor == heir)
                    { ReportEstateShare(record, share, "awaiting estate house succession; no surviving beneficiary"); return; }
                }
                share.Heir = heir = successor;
                share.BeneficiaryReconciled = true;
                if (share.Recipient == null && share.CadetPlan?.Cadet != null)
                    share.Recipient = share.CadetPlan.Cadet;
                share.CadetPlan = null;
                ReportEstateShare(record, share, "outstanding inheritance followed lawful successor " + heir.StringId);
            }
            foreach (string id in share.Titles.Except(share.DeliveredTitles).Except(share.SupersededTitles).ToList())
            {
                var title = titles.GetTitle(id);
                if (title == null) throw new InvalidOperationException("an estate title could not be resolved: " + id);
                ReconcileEstateTitleReceipt(record, share, title, heir.Clan);
            }
            foreach (var sovereign in share.Titles.Except(share.DeliveredTitles).Except(share.SupersededTitles)
                .Select(titles.GetTitle).Where(t => RequiresSovereignEstateExecutor(record, titles, t)))
            {
                if (heir.Clan != record.Source && heir.Clan.Kingdom != record.Realm
                    && CrownAccessionBehavior.CanUseExistingCrownHouse(heir, heir.Clan.Leader,
                        RegencyBehavior.Instance?.GetLegalClanHead(heir.Clan))
                    && titles.CanReceiveForeignSovereignInheritance(heir.Clan, sovereign))
                {
                    if (!share.SovereignTransfers.Contains(sovereign.TitleId)) share.SovereignTransfers.Add(sovereign.TitleId);
                }
                else
                    throw new InvalidOperationException($"additional sovereign-title executor required; title={sovereign.TitleId}; rank={sovereign.TitleType}; de_jure={sovereign.DeJureHolderClanId}; de_facto={sovereign.DeFactoHolderClanId}; package retained pending");
            }
            if (share.Recipient == null)
            {
                if (share.CadetPlan != null || (heir.Clan == record.Source && !share.Primary && heir != record.Source.Leader
                    && heir != Hero.MainHero && heir != RegencyBehavior.Instance?.GetLegalClanHead(record.Source)))
                {
                    if (share.CadetPlan == null)
                    {
                        var draft = new CrownAccessionRecord
                        {
                            Realm = record.Realm,
                            PreviousHouse = record.Source,
                            Predecessor = record.Deceased,
                            Heir = heir,
                            CadetId = "bc_partition_estate_" + Guid.NewGuid().ToString("N"),
                            EndowmentFiefs = share.Fiefs.ToList(),
                            Household = new List<string> { heir.StringId }
                        };
                        draft.Household = new[] { heir, heir.Spouse }.Concat(heir.Children)
                            .Where(h => h != null && h.IsAlive && h.Clan == record.Source
                                && h != record.Source.Leader && h != Hero.MainHero)
                            .Distinct().Select(h => h.StringId).ToList();
                        draft.CadetName = PreviewAbdicationCadetName(draft);
                        share.CadetPlan = draft;
                    }
                    if (heir.IsPrisoner || heir.PartyBelongedTo?.MapEvent != null || heir.PartyBelongedTo?.SiegeEvent != null)
                    { ReportEstateShare(record, share, "awaiting heir freedom or battle completion"); return; }
                    ReconcileEstateCadetHousehold(record, share);
                    if (!PrepareAbdicationCadet(share.CadetPlan))
                    { ReportEstateShare(record, share, "awaiting cadet preparation: " + share.CadetPlan.AbdicationFailure); return; }
                    titles.MoveCrownHeirClaims(heir, record.Source, heir.Clan);
                }
                share.Recipient = heir.Clan;
            }
            Clan recipient = share.Recipient;
            if (recipient != heir.Clan || recipient.IsEliminated)
            {
                share.BeneficiaryReconciled = true;
                share.Recipient = recipient = heir.Clan;
                ReportEstateShare(record, share, "outstanding inheritance followed recipient " + recipient.StringId);
            }
            if (recipient.Leader?.IsAlive != true)
            { ReportEstateShare(record, share, "awaiting living recipient leader"); return; }
            foreach (string id in share.Fiefs.Except(share.DeliveredFiefs).Except(share.SupersededFiefs).ToList())
            {
                Settlement fief = Settlement.Find(id);
                if (fief == null)
                    throw new InvalidOperationException("an estate settlement could not be resolved: " + id);
                if (fief.OwnerClan != record.Source && fief.OwnerClan != recipient)
                {
                    // Legal inheritance is handled separately; occupation is not undone.
                    share.SupersededFiefs.Add(id);
                    ReportEstateShare(record, share, "possession superseded by ownership change: " + id);
                    continue;
                }
                if (fief.SiegeEvent != null || fief.Party?.MapEvent != null)
                { ReportEstateShare(record, share, "awaiting settlement battle: " + id); return; }
                if (fief.OwnerClan != recipient && !titles.TransferInheritedPossession(record.Source, recipient, fief))
                { ReportEstateShare(record, share, "awaiting possession transfer: " + id); return; }
                if (fief.OwnerClan != recipient)
                { ReportEstateShare(record, share, "awaiting verified possession: " + id); return; }
                share.DeliveredFiefs.Add(id);
            }
            foreach (string id in share.Titles.Except(share.DeliveredTitles).Except(share.SupersededTitles).ToList())
            {
                var title = titles.GetTitle(id);
                if (title == null) throw new InvalidOperationException("an estate title could not be resolved: " + id);
                if (!title.IsActive || title.DeJureHolderClanId != record.Source.StringId
                    && title.DeJureHolderClanId != recipient.StringId)
                {
                    SupersedeEstateTitle(share, id);
                    ReportEstateShare(record, share, "legal title superseded: " + id);
                    continue;
                }
                if (title.DeJureHolderClanId != recipient.StringId)
                    titles.LegalizeTitleInheritance(recipient, title, "cross_clan_death_inheritance",
                        preserveDeFacto: title.DeFactoHolderClanId != record.Source.StringId
                            && title.DeFactoHolderClanId != recipient.StringId);
                if (title.DeJureHolderClanId != recipient.StringId)
                { ReportEstateShare(record, share, "awaiting verified legal inheritance: " + id); return; }
                share.DeliveredTitles.Add(id);
            }
            foreach (string id in share.SovereignTransfers.Except(share.DeliveredSovereignTransfers).ToList())
            {
                if (share.SupersededTitles.Contains(id))
                { share.DeliveredSovereignTransfers.Add(id); continue; }
                if (!titles.TryIntegrateForeignSovereignInheritance(record.Realm, recipient, titles.GetTitle(id), out string failure))
                { ReportEstateShare(record, share, "awaiting sovereign integration: " + id + "; " + failure); return; }
                share.DeliveredSovereignTransfers.Add(id);
            }
            if (share.GoldPayment == null)
                share.GoldPayment = new CrownAccessionRecord
                {
                    IncomingSourceHead = record.GoldDonor,
                    GoldRecipient = recipient.Leader,
                    EndowmentGold = record.GoldDonor == recipient.Leader ? 0 : record.GoldPerShare
                };
            if (!share.GoldPayment.GoldDebited && share.GoldPayment.EndowmentGold > 0
                && share.GoldPayment.EndowmentDonor?.IsAlive != true)
            {
                if (record.Source.Leader?.IsAlive != true)
                { ReportEstateShare(record, share, "awaiting estate treasury successor"); return; }
                share.GoldPayment.IncomingSourceHead = record.Source.Leader;
                ReportEstateShare(record, share, "unpaid gold followed estate treasury successor");
            }
            if (!share.GoldPayment.GoldCredited && share.GoldPayment.GoldRecipient != recipient.Leader)
                share.GoldPayment.GoldRecipient = recipient.Leader;
            if (!share.GoldPayment.GoldDebited && share.GoldPayment.EndowmentDonor == share.GoldPayment.GoldRecipient)
                share.GoldPayment.EndowmentGold = 0;
            if (!CrownAccessionBehavior.DeliverAbdicationGold(share.GoldPayment))
            { ReportEstateShare(record, share, "awaiting verified gold delivery"); return; }
            share.LandedSettled = true;
            share.Completed = true;
            share.Status = "completed";
            BellumCivileLogger.Log($"Cross-clan death estate delivered; deceased={record.Deceased.StringId}; heir={heir.StringId}; recipient={recipient.StringId}; fiefs={share.DeliveredFiefs.Count}; titles={share.DeliveredTitles.Count}; superseded_fiefs={share.SupersededFiefs.Count}; superseded_titles={share.SupersededTitles.Count}.");
            if (recipient != record.Source)
            {
                var message = new TextObject("{=BC_CrossClanEstateInherited}Following the death of {PARENT}, {HEIR} has received {POSSESSIVE} lawful share of the estates of {HOUSE} into the keeping of {RECIPIENT}.");
                message.SetTextVariable("PARENT", record.Deceased.Name);
                message.SetTextVariable("HEIR", heir.Name);
                message.SetTextVariable("POSSESSIVE", heir.IsFemale ? new TextObject("{=BC_Pronoun_Her}her") : new TextObject("{=BC_Pronoun_His}his"));
                message.SetTextVariable("HOUSE", record.Source.Name);
                message.SetTextVariable("RECIPIENT", recipient.Name);
                BellumCivileNotifications.Show(message, BellumNotificationColors.Inheritance,
                    primaryKingdom: record.Realm, primaryClan: recipient, secondaryClan: record.Source);
            }
        }

        private static void ReconcileEstateCadetHousehold(CrossClanEstateRecord estate, CrossClanEstateShare share)
        {
            var plan = share.CadetPlan;
            if (plan?.Household == null) return;
            var otherHeirs = new HashSet<Hero>(estate.Shares.Where(s => s != null && s != share).Select(s => s.Heir));
            Hero legalHead = RegencyBehavior.Instance?.GetLegalClanHead(estate.Source);
            plan.Household.RemoveAll(id =>
            {
                if (id == plan.Heir?.StringId) return false;
                Hero member = CrownAccessionBehavior.ResolveAbdicationHero(id);
                // Unresolved references still defer preparation. Already-transferred living
                // members stay put; only unexecuted dependant moves are reconciled.
                if (member == null || (member.IsAlive && plan.Cadet != null && member.Clan == plan.Cadet)) return false;
                bool remove = member.IsDead || otherHeirs.Contains(member) || member == estate.Source.Leader
                    || member == legalHead || member == Hero.MainHero
                    || (member.Clan != null && member.Clan != estate.Source)
                    || CrownAccessionBehavior.Instance?.IsPendingCrownHeir(member) == true;
                if (remove)
                    BellumCivileLogger.Log($"Estate cadet dependant retained outside household; cadet={plan.CadetId}; heir={plan.Heir?.StringId}; member={id}; current_house={member.Clan?.StringId}; separate_beneficiary={otherHeirs.Contains(member)}.");
                return remove;
            });
        }

        internal static void SupersedeEstateTitle(CrossClanEstateShare share, string id)
        {
            if (id != null && share.Titles.Contains(id) && !share.DeliveredTitles.Contains(id)
                && !share.SupersededTitles.Contains(id)) share.SupersededTitles.Add(id);
        }

        internal static bool EstateDeliveryComplete(CrossClanEstateShare share) =>
            !share.Fiefs.Except(share.DeliveredFiefs).Except(share.SupersededFiefs).Any()
            && !share.Titles.Except(share.DeliveredTitles).Except(share.SupersededTitles).Any()
            && !(share.SovereignTransfers ?? new List<string>()).Except(share.DeliveredSovereignTransfers ?? new List<string>()).Any()
            && share.GoldPayment?.GoldCredited == true;

        private static void ReportEstateShare(CrossClanEstateRecord record, CrossClanEstateShare share, string status)
        {
            if (share.Status == status) return;
            share.Status = status;
            BellumCivileLogger.Log($"Cross-clan estate share status; deceased={record.Deceased.StringId}; heir={share.Heir?.StringId}; recipient={share.Recipient?.StringId}; status={status}.");
        }
    }
}
