using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class CrownAccessionBehavior
    {
        public static bool UsesHereditaryPlayerAbdication(Kingdom realm) =>
            realm != null && realm.RulingClan == Clan.PlayerClan
            && SuccessionLawBehavior.Instance?.ResolvePermanentRealm(realm) == realm
            && SuccessionRealmRules.Classify(SuccessionLawHelper.GetLawsForKingdom(realm).SuccessionLaw)
                == RealmSuccessionSystem.Hereditary;

        public bool TryPreviewAbdication(Kingdom realm, out CrownAccessionRecord preview, out TextObject reason)
        {
            preview = null;
            reason = new TextObject("{=BC_AbdicationUnavailable}The Crown cannot be relinquished at this time.");
            if (!UsesHereditaryPlayerAbdication(realm) || realm.IsEliminated || Hero.MainHero?.IsAlive != true
                || Clan.PlayerClan.Leader != Hero.MainHero || IsPending(realm)
                || realm.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().Any()) return false;
            if (RegencyBehavior.Instance?.GetLegalClanHead(Clan.PlayerClan) != Hero.MainHero)
            {
                reason = new TextObject("{=BC_AbdicationRegentCannot}A regent cannot abdicate the lawful sovereign's Crown.");
                return false;
            }
            var laws = SuccessionLawHelper.GetLawsForKingdom(realm);
            Hero heir = HereditaryRealmSuccession.GetLine(realm, Hero.MainHero, laws).FirstOrDefault();
            preview = new CrownAccessionRecord
            {
                Realm = realm, PreviousHouse = Clan.PlayerClan, Predecessor = Hero.MainHero,
                Heir = heir, VoluntaryAbdication = true, CreatesCadet = heir?.Clan == Clan.PlayerClan,
                HeirHouse = heir?.Clan, RequiresRegency = heir != null && heir.Age < SuccessionLawHelper.GetAgeOfMajority(),
                HouseLaw = laws.SuccessionLaw, GenderLaw = laws.GenderLaw, Emergency = heir == null
            };
            if (heir == null)
            {
                if (EmergencyCandidates(preview).Count > 0) return true;
                reason = new TextObject("{=BC_AbdicationNoSuccessor}There is neither a lawful heir nor an eligible noble house to receive the Crown.");
                return false;
            }
            if (heir == Hero.MainHero || !heir.IsAlive || heir.IsDisabled || !HereditaryRealmSuccession.CanConsiderClan(heir.Clan, realm)
                || heir.Clan.IsEliminated) return false;
            if (RequiresRealmUnion(heir.Clan, realm)
                && CanUseExistingCrownHouse(heir, heir.Clan.Leader, RegencyBehavior.Instance?.GetLegalClanHead(heir.Clan)))
            {
                reason = new TextObject("{=BC_AbdicationUnionPending}The lawful heir already rules another realm. Uniting these Crowns through abdication is not supported; personal unions currently arise through hereditary succession after a ruler's death.");
                return false;
            }
            if (!preview.CreatesCadet) return true;
            preview.Household = new[] { heir, heir.Spouse }
                .Where(hero => hero != null && hero.IsAlive && hero.Clan == Clan.PlayerClan && hero != Hero.MainHero)
                .Distinct().Select(hero => hero.StringId).ToList();
            if (preview.Household.Select(ResolveAbdicationHero).Any(hero => hero == null || hero.IsPrisoner
                || hero.IsTraveling || hero.IsDisabled || hero.PartyBelongedTo?.MapEvent != null
                || hero.PartyBelongedTo?.SiegeEvent != null))
            {
                reason = new TextObject("{=BC_AbdicationHouseholdBusy}The successor's household must be free of captivity, travel and battle before forming its new house.");
                return false;
            }
            FillAbdicationEndowment(preview);
            if (preview.EndowmentFiefs.Select(Settlement.Find).Any(fief => fief?.SiegeEvent != null
                || fief?.Party?.MapEvent != null))
            {
                reason = new TextObject("{=BC_AbdicationEstateBusy}A holding in the inheritance is under siege or in battle.");
                return false;
            }
            var partition = Campaign.Current.GetCampaignBehavior<PartitionSuccessionBehavior>();
            if (partition == null || RegencyBehavior.Instance == null
                || (preview.PreviousHouse.HomeSettlement ?? Settlement.All.FirstOrDefault(s => s.IsTown || s.IsCastle)) == null)
                return false;
            preview.CadetName = partition.PreviewAbdicationCadetName(preview);
            reason = TextObject.GetEmpty();
            return true;
        }

        private static void FillAbdicationEndowment(CrownAccessionRecord record)
        {
            var plan = FeudalInheritancePlanner.BuildPlan(record.PreviousHouse, record.Predecessor,
                BellumCivileOptions.PartitionSuccessionMainHeirReservedFiefs);
            var share = FeudalInheritancePlanner.GetPrimaryHeirShare(plan, record.Heir, BellumCivileOptions.EnablePartitionSuccession);
            int otherBeneficiaries = BellumCivileOptions.EnablePartitionSuccession
                ? Math.Min(plan.SecondaryPackages.Count, plan.NaturalHeirs.Count(h => h != record.Heir)) : 0;
            record.EndowmentGold = CalculateAbdicationGold(record.Predecessor.Gold, otherBeneficiaries);
            record.EndowmentFiefs = share.Fiefs
                .Select(fief => fief.Settlement.StringId).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var titleBehavior = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            string crown = titleBehavior?.GetKingdomPoliticalTitle(record.Realm)?.TitleId;
            record.EndowmentTitles = share.Titles.Where(title => title.TitleId != crown)
                .Select(title => title.TitleId).OrderBy(id => id, StringComparer.Ordinal).ToList();
        }

        public TextObject DescribeAbdication(CrownAccessionRecord preview)
        {
            if (preview.Emergency)
                return new TextObject("{=BC_AbdicationEmergencyPreview}You have no lawful heir. The nobles will elect a new ruling house. You will not be a candidate, and you will retain your character, clan, estates and treasury. Continue?");
            var text = new TextObject(preview.CreatesCadet
                ? "{=BC_AbdicationCadetPreview}The Crown of {REALM} will pass to {HEIR}. {HEIR} will found the ruling house of {HOUSE}. An eligible spouse may accompany them; existing children will remain in their current clan.\n\nInheritance granted now:\n{FIEFS}\nLegal titles: {TITLES}\nGold: {GOLD}\n\n{REGENCY}\nYou will continue as {RULER}, leading your original clan. The rest of your treasury remains yours. This inheritance is granted now, not again upon your death. Continue?"
                : "{=BC_AbdicationExternalPreview}The Crown of {REALM} will pass to {HEIR} of {HOUSE}.\n\n{REGENCY}\nYou will retain your character, original clan, estates and treasury. Continue?");
            text.SetTextVariable("GOLD", preview.EndowmentGold);
            text.SetTextVariable("REALM", preview.Realm.Name);
            text.SetTextVariable("HEIR", preview.Heir.Name);
            text.SetTextVariable("HOUSE", preview.CreatesCadet ? preview.CadetName : preview.Heir.Clan.Name.ToString());
            text.SetTextVariable("RULER", preview.Predecessor.Name);
            text.SetTextVariable("FIEFS", preview.EndowmentFiefs.Count == 0
                ? new TextObject("{=BC_AbdicationUnlanded}No holdings. The new ruler will be unlanded.").ToString()
                : string.Join(", ", preview.EndowmentFiefs.Select(id => Settlement.Find(id)?.Name.ToString() ?? id)));
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            text.SetTextVariable("TITLES", preview.EndowmentTitles.Count == 0 ? new TextObject("{=BC_None}None").ToString()
                : string.Join(", ", preview.EndowmentTitles.Select(id => titles?.GetTitle(id)?.Name ?? id)));
            var regency = new TextObject(preview.Heir.Age < SuccessionLawHelper.GetAgeOfMajority()
                ? "{=BC_AbdicationMinorPreview}You are appointing a regency for {HEIR}, the lawful sovereign. An adult caretaker will govern their house until {HEIR} comes of age."
                : "{=BC_AbdicationAdultPreview}The successor will rule in their own right.");
            regency.SetTextVariable("HEIR", preview.Heir.Name);
            text.SetTextVariable("REGENCY", regency);
            return text;
        }

        public bool ConfirmAbdication(CrownAccessionRecord preview, out TextObject reason)
        {
            reason = new TextObject("{=BC_AbdicationPreviewChanged}The succession or inheritance has changed. Review Abdicate Leadership again before confirming.");
            if (preview == null || preview.Predecessor != Hero.MainHero || preview.PreviousHouse != Clan.PlayerClan
                || !Campaign.Current.Models.KingdomCreationModel.IsPlayerKingdomAbdicationPossible(out _)
                || !TryPreviewAbdication(preview.Realm, out var current, out _)
                || !SameAbdicationPreview(preview, current)) return false;
            preview.Started = CampaignTime.Now;
            preview.CadetId = "bc_partition_abdication_" + Guid.NewGuid().ToString("N");
            _accessions.Add(preview);
            Resolve(preview);
            reason = preview.Completed ? TextObject.GetEmpty()
                : new TextObject("{=BC_AbdicationPending}The succession has been recorded. Its remaining settlement is pending.");
            return true;
        }

        internal static bool SameAbdicationPreview(CrownAccessionRecord first, CrownAccessionRecord second) =>
            first != null && second != null && first.Realm == second.Realm && first.Predecessor == second.Predecessor
            && first.VoluntaryAbdication == second.VoluntaryAbdication
            && first.PreviousHouse == second.PreviousHouse && first.Heir == second.Heir && first.HouseLaw == second.HouseLaw
            && first.GenderLaw == second.GenderLaw && first.CreatesCadet == second.CreatesCadet && first.Emergency == second.Emergency
            && first.HeirHouse == second.HeirHouse && first.RequiresRegency == second.RequiresRegency
            && first.EndowmentGold == second.EndowmentGold
            && first.CadetName == second.CadetName && first.EndowmentFiefs.SequenceEqual(second.EndowmentFiefs)
            && first.EndowmentTitles.SequenceEqual(second.EndowmentTitles) && first.Household.SequenceEqual(second.Household);

        private bool PrepareAbdicationTransfer(CrownAccessionRecord record)
        {
            if (record.Heir?.IsAlive != true || record.Heir.IsDisabled
                || (record.Predecessor?.IsAlive != true && !record.ForcedAbdication))
                return DeferAbdication(record, "a participant is no longer available");
            Clan receivingHouse = record.CreatesCadet ? record.Cadet : record.HeirHouse;
            if (record.Realm.RulingClan != record.PreviousHouse && record.Realm.RulingClan != receivingHouse
                && !(record.ForcedAbdication && record.Realm.RulingClan == record.SettlementRulingHouse))
                return DeferAbdication(record, "a different house has taken the Crown during settlement");
            if (record.Heir.Clan != record.HeirHouse && record.Heir.Clan != receivingHouse)
                return DeferAbdication(record, "the successor has changed house during settlement");
            if (record.IncomingHousePrepared) return true;
            if (!record.CreatesCadet) return true;
            if (record.EndowmentSettled) return true;
            if ((record.PreviousHouse.Leader != record.Predecessor && record.Predecessor.IsAlive)
                || (!record.ForcedAbdication && (record.PreviousHouse != Clan.PlayerClan
                    || record.Realm.RulingClan != record.PreviousHouse || Hero.MainHero != record.Predecessor)))
                return DeferAbdication(record, "the original house or ruler changed during settlement");
            return SettleCrownCadet(record);
        }

        internal bool SettleCrownCadet(CrownAccessionRecord record)
        {
            if (record.EndowmentSettled) return true;
            Clan source = record.EndowmentHouse;
            if (source == null || source.IsEliminated || source.Kingdom != record.HouseholdRealm)
                return DeferAbdication(record, "the source household is unavailable");
            if (record.IncomingHousePrepared && source.Leader != record.IncomingSourceHead)
                return DeferAbdication(record, "the source household changed leadership during settlement");
            if (record.HasLivingEndowment && record.EndowmentDonor?.IsAlive != true)
                return DeferAbdication(record, "the inheritance donor died during settlement");
            var titles = Campaign.Current.GetCampaignBehavior<FeudalTitleBehavior>();
            if (titles == null) return DeferAbdication(record, "title service unavailable");
            foreach (string id in record.EndowmentFiefs.Except(record.DeliveredFiefs))
            {
                Settlement holding = Settlement.Find(id);
                if (holding?.SiegeEvent != null || holding?.Party?.MapEvent != null)
                    return DeferAbdication(record, "an undelivered holding is in battle: " + id);
                Clan owner = holding?.OwnerClan;
                if (owner == source && FiefDeliberationBehavior.IsAwaitingAllocation(holding))
                    return DeferAbdication(record, "an undelivered holding awaits allocation: " + id);
                if (owner == null || (owner != source && owner != record.Cadet))
                    return DeferAbdication(record, "an undelivered holding changed hands: " + id);
            }
            foreach (string id in record.EndowmentTitles.Except(record.DeliveredTitles))
            {
                var title = titles.GetTitle(id);
                if (FeudalInheritancePlanner.IsAllocationCustodyTitle(title, titles, source))
                    return DeferAbdication(record, "an undelivered title awaits allocation: " + id);
                if (title == null || !title.IsActive || (title.DeJureHolderClanId != source.StringId
                    && title.DeJureHolderClanId != record.Cadet?.StringId))
                    return DeferAbdication(record, "an undelivered title changed hands: " + id);
            }
            foreach (string id in record.Household)
            {
                Hero member = ResolveAbdicationHero(id);
                if (member == null || !member.IsAlive || member == Hero.MainHero
                    || (member.Clan != source && member.Clan != record.Cadet)
                    || member.IsDisabled || member.IsPrisoner || member.IsTraveling || member.PartyBelongedTo?.MapEvent != null
                    || member.PartyBelongedTo?.SiegeEvent != null)
                    return DeferAbdication(record, "the successor's household is unavailable");
            }
            var partition = Campaign.Current.GetCampaignBehavior<PartitionSuccessionBehavior>();
            if (partition?.PrepareAbdicationCadet(record) != true)
                return DeferAbdication(record, record.AbdicationFailure ?? "cadet household preparation is incomplete");
            titles.MoveCrownHeirClaims(record.Heir, source, record.Cadet);
            foreach (string id in record.EndowmentFiefs.Except(record.DeliveredFiefs).ToList())
            {
                Settlement fief = Settlement.Find(id);
                if (fief.OwnerClan != record.Cadet) ChangeOwnerOfSettlementAction.ApplyByDefault(record.Cadet.Leader, fief);
                if (fief.OwnerClan != record.Cadet) return DeferAbdication(record, "holding transfer failed: " + id);
                // Barony legalization occurs in the title loop, including de-jure-only titles.
                record.DeliveredFiefs.Add(id);
            }
            foreach (string id in record.EndowmentTitles.Except(record.DeliveredTitles).ToList())
            {
                var title = titles.GetTitle(id);
                if (title.DeJureHolderClanId != record.Cadet.StringId)
                    titles.LegalizeTitleInheritance(record.Cadet, title,
                        record.ChallengeConcession ? "succession_challenge_concession" : "crown_accession_inheritance_in_life",
                        preserveDeFacto: title.DeFactoHolderClanId != source.StringId
                            && title.DeFactoHolderClanId != record.Cadet.StringId);
                if (title.DeJureHolderClanId != record.Cadet.StringId) return DeferAbdication(record, "title transfer failed: " + id);
                record.DeliveredTitles.Add(id);
            }
            if (!DeliverAbdicationGold(record)) return DeferAbdication(record, "gold delivery is pending");
            record.EndowmentSettled = true;
            record.AbdicationFailure = null;
            return true;
        }

        private static bool DeferAbdication(CrownAccessionRecord record, string reason)
        {
            if (record.AbdicationFailure != reason)
                BellumCivileLogger.Log($"Abdication pending; realm={record.Realm?.StringId}; heir={record.Heir?.StringId}; reason={reason}.");
            record.AbdicationFailure = reason;
            return false;
        }

        internal static Hero ResolveAbdicationHero(string id) => Hero.FindFirst(hero => hero.StringId == id);
    }
}
