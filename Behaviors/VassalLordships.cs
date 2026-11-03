using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class LordshipBehavior
    {
        private List<LordshipIncomeCredit> _incomeCredits = new List<LordshipIncomeCredit>();
        private PersonalLordshipRecord _oathGrant;

        internal sealed class VassalOath
        {
            internal Hero Recipient, Ruler;
            internal Kingdom Realm;
            internal bool Completed;
        }

        private static Kingdom LordshipRealm(Clan clan, ClaimFeudWarBehavior feuds = null)
        {
            var realm = clan?.Kingdom;
            if (realm == null || realm.IsEliminated) return null;
            // Private-feud shells do not sever fealty. Crown rebellions remain separate realms.
            return (feuds ?? Campaign.Current?.GetCampaignBehavior<ClaimFeudWarBehavior>())?.GetParentKingdomForTemporaryRealm(realm) ?? realm;
        }

        internal static bool ShareLordshipRealm(Clan first, Clan second)
        {
            var realm = LordshipRealm(first);
            return realm != null && realm == LordshipRealm(second);
        }

        private void ValidateVassalRealms()
        {
            foreach (var grant in _grants.Where(g => g?.IsVassalGrant == true && !g.IsValid).ToList())
            {
                if (grant.HasValidEstate && (CivilWarConflictBehavior.IsRealmTransferPending(grant.GrantingClan.Kingdom)
                    || CivilWarConflictBehavior.IsRealmTransferPending(grant.BeneficiaryClan.Kingdom)
                    || CrownAccessionBehavior.Instance?.IsRealmUnionProtected(grant.GrantingClan.Kingdom) == true
                    || CrownAccessionBehavior.Instance?.IsRealmUnionProtected(grant.BeneficiaryClan.Kingdom) == true))
                { _realmCheckPending = true; continue; }
                Remove(grant);
            }
        }

        internal VassalOath CaptureVassalOath()
        {
            _oathGrant = null;
            var hero = Hero.MainHero;
            var ruler = Hero.OneToOneConversationHero;
            var realm = ruler?.Clan?.Kingdom;
            if (!Ready || hero?.Clan == null || hero.Clan != Clan.PlayerClan || hero.Clan.Leader != hero || ruler != realm?.Leader
                || realm == null || realm.IsEliminated || BellumKingdomVisibilityHelper.IsTemporaryBellumKingdom(realm)
                || (hero.Clan.Kingdom == realm && !hero.Clan.IsUnderMercenaryService)) return null;
            return new VassalOath { Recipient = hero, Ruler = ruler, Realm = realm };
        }

        internal void CompleteVassalOath(VassalOath oath, ClanFinanceModel model = null)
        {
            if (!Ready || oath == null || oath.Completed) return;
            oath.Completed = true;
            var clan = oath.Ruler?.Clan;
            if (oath.Recipient?.Clan == null || oath.Recipient != Hero.MainHero || oath.Recipient.Clan != Clan.PlayerClan
                || oath.Recipient.Clan.Kingdom != oath.Realm || oath.Recipient.Clan.IsUnderMercenaryService
                || oath.Realm?.Leader != oath.Ruler || oath.Realm.RulingClan != clan) return;
            model = model ?? Campaign.Current?.Models?.ClanFinanceModel;
            if (model == null) return;
            // Draw once from a small low-income pool after the oath succeeds.
            var candidates = Villages(clan).Where(v => CanGrantVassal(clan, v, oath.Recipient))
                .Select(v => new { Village = v, Income = EffectiveVillageIncome(clan, v, model) })
                .Where(v => v.Income > 0).OrderBy(v => v.Income).ThenBy(v => v.Village.StringId, StringComparer.Ordinal)
                .Take(3).Select(v => v.Village).ToList();
            var village = candidates.Count == 0 ? null : candidates[candidates.Count == 1 ? 0 : MBRandom.RandomInt(candidates.Count)];
            if (village != null && TryGrantVassal(clan, village, oath.Recipient, oath.Ruler, out var grant)) _oathGrant = grant;
        }

        internal bool CanGrantVassal(Clan source, Settlement village, Hero recipient)
            => CanGrantVassalEstate(source, village, recipient) && ShareLordshipRealm(source, recipient.Clan);

        private bool CanGrantVassalEstate(Clan source, Settlement village, Hero recipient)
            => Ready && recipient?.Clan != null && HouseholdMembership.Supports(source) && source != recipient.Clan
                && (recipient.Clan == Clan.PlayerClan || source == Clan.PlayerClan
                    && recipient.Clan.IsNoble && !recipient.Clan.IsMinorFaction && !recipient.Clan.IsEliminated
                    && ExiledClanRecoveryBehavior.IsLandlessForExile(recipient.Clan))
                && recipient == recipient.Clan.Leader
                && recipient.IsAlive && !recipient.IsChild && !recipient.IsDisabled && !recipient.IsPrisoner
                && !recipient.Clan.IsUnderMercenaryService && source.Kingdom?.RulingClan == source
                && village?.Village?.Bound?.OwnerClan == source
                && FeudalTitleBehavior.Instance?.IsHeldForAllocation(village.Village.Bound, source) != true
                && GetGrant(village) == null && GetGrant(recipient) == null;

        internal bool CanOfferAsylumGrant(Clan source, Settlement village, Hero recipient)
            => source == Clan.PlayerClan && recipient?.Clan?.Kingdom == null
                && CanGrantVassalEstate(source, village, recipient);

        internal IEnumerable<Hero> LandlessVassalRecipients(Clan source)
            => source != null && source.Kingdom?.RulingClan == source && source == Clan.PlayerClan
                ? source.Kingdom.Clans.Where(c => c != null && c != source && !c.IsEliminated
                    && c.IsNoble && !c.IsMinorFaction && !c.IsUnderMercenaryService
                    && ExiledClanRecoveryBehavior.IsLandlessForExile(c)).Select(c => c.Leader)
                    .Where(h => h?.IsAlive == true && !h.IsDisabled && !h.IsChild)
                : Enumerable.Empty<Hero>();

        internal bool CanGrantFromPlayerHouse(Settlement village, Hero recipient, out TextObject reason)
        {
            if (recipient?.Clan == Clan.PlayerClan) return CanGrant(Clan.PlayerClan, village, recipient, out reason);
            reason = CanGrantVassal(Clan.PlayerClan, village, recipient) ? null
                : Text("BC_Lordship_InvalidVassal", "Choose the adult head of a landless vassal house, without an existing personal estate. This village must be vacant and controlled by your house, not awaiting allocation.");
            return reason == null;
        }

        internal bool TryGrantFromPlayerHouse(Settlement village, Hero recipient, out TextObject reason)
        {
            if (recipient?.Clan == Clan.PlayerClan) return TryGrant(Clan.PlayerClan, village, recipient, out reason);
            if (!CanGrantFromPlayerHouse(village, recipient, out reason)) return false;
            return TryGrantVassal(Clan.PlayerClan, village, recipient, Clan.PlayerClan.Leader, out _);
        }

        internal bool TryGrantVassal(Clan source, Settlement village, Hero recipient, Hero ruler, out PersonalLordshipRecord grant)
        {
            grant = null;
            if (ruler != source?.Leader || !CanGrantVassal(source, village, recipient)) return false;
            grant = new PersonalLordshipRecord { Village = village, Parent = village.Village.Bound, GrantingClan = source,
                Holder = recipient, GrantedDay = Now, RewardApplied = true, IsVassalGrant = true,
                BeneficiaryClan = recipient.Clan, Grantor = ruler };
            _grants.Add(grant); _byVillage[village] = grant; _byHero[recipient] = grant;
            Changed();
            RelationMemoryService.ApplyChange(ruler, recipient, 10, true, "lordship_granted", 5f,
                RelationMemoryScope.Personal, village.Name.ToString());
            TitleAcquisitionBehavior.Granted(grant);
            return true;
        }

        internal bool ReturnVassalLordship(Hero recipient, Hero acceptingLord)
        {
            var grant = GetGrant(recipient);
            if (grant?.IsVassalGrant != true || !grant.IsValid || acceptingLord == null
                || grant.GrantingClan.Leader != acceptingLord) return false;
            Remove(grant);
            RelationMemoryService.ApplyChange(acceptingLord, recipient, -10, true, "lordship_returned", 2f,
                RelationMemoryScope.Personal, grant.Village.Name.ToString());
            return true;
        }

        private static int EffectiveVillageIncome(Clan clan, Settlement village, ClanFinanceModel model)
            => LordshipIncome.Net(model.CalculateVillageIncome(clan, village.Village, false),
                FeudalTitlePenaltyHelper.IsBoundToContestedLegalTitle(village.Village, out _));

        internal void FundVassalIncome(PersonalLordshipRecord grant, int amount)
        {
            if (amount <= 0 || grant?.IsVassalGrant != true || !grant.IsValid) return;
            var credit = _incomeCredits.FirstOrDefault(c => c != null && c.Recipient == grant.BeneficiaryClan
                && c.Source == grant.GrantingClan && c.Village == grant.Village);
            if (credit == null)
                _incomeCredits.Add(new LordshipIncomeCredit { Recipient = grant.BeneficiaryClan, Source = grant.GrantingClan,
                    Village = grant.Village, Amount = amount });
            else credit.Amount += amount;
        }

        internal void AddVassalIncome(Clan clan, ClanFinanceModel model, ref ExplainedNumber result, bool apply)
        {
            if (clan?.Leader == null || clan.IsEliminated) return;
            var credits = _incomeCredits.Where(c => c?.Recipient == clan).ToList();
            if (!apply && credits.Count == 0)
                credits = _grants.Where(g => g?.IsVassalGrant == true && g.IsValid && g.BeneficiaryClan == clan)
                    .Select(g => new LordshipIncomeCredit { Village = g.Village, Amount = EffectiveVillageIncome(g.GrantingClan, g.Village, model) }).ToList();
            foreach (var credit in credits)
                if (credit.Amount > 0 && credit.Village != null) result.Add(credit.Amount, Name(credit.Village));
            // Only funded receipts reach the native clan-bank payout. Previewing never funds a grant.
            if (apply) _incomeCredits.RemoveAll(c => c?.Recipient == clan);
        }

        internal bool IsPlayerLordship(Settlement village)
        {
            var grant = GetGrant(village);
            return Ready && grant != null && Hero.MainHero != null && grant.Holder == Hero.MainHero && grant.IsValid;
        }

        internal bool RecognizesPlayerLord(Hero notable) => notable?.IsNotable == true && IsPlayerLordship(notable.HomeSettlement);
    }
}
