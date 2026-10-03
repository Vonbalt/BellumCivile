using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Localization;
using C = BellumCivile.BellumCivileConstants;

namespace BellumCivile
{
    /// <summary>
    /// Applies contested legal-title income losses as visible clan-finance line items.
    /// </summary>
    public class BellumFeudalClanFinanceModel : ClanFinanceModel
    {
        private readonly ClanFinanceModel _baseModel;
        private FeudalServiceLedger _serviceLedger;
        private int _serviceLedgerDay = -1;
        private int _serviceLedgerTitleRevision = -1;
        private int _serviceLedgerServiceRevision = -1;

        public BellumFeudalClanFinanceModel(ClanFinanceModel baseModel)
        {
            _baseModel = baseModel ?? new DefaultClanFinanceModel();
        }

        public override int PartyGoldLowerThreshold => _baseModel.PartyGoldLowerThreshold;

        public override ExplainedNumber CalculateClanGoldChange(Clan clan, bool includeDescriptions = false, bool applyWithdrawals = false, bool includeDetails = false)
        {
            ExplainedNumber result = _baseModel.CalculateClanGoldChange(clan, includeDescriptions, applyWithdrawals, includeDetails);
            AddContestedLegalTitleIncomePenalty(clan, ref result, includeDetails);
            AddFeudalServiceOwed(clan, ref result, includeDetails);
            AddSeneschalAdministration(clan, ref result, includeDetails);
            AddSeneschalAssignmentExpense(clan, ref result);
            var council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            council?.AddCouncilSalaryIncome(clan, ref result, applyWithdrawals);
            council?.AddCouncilSalaryExpenses(clan, ref result, applyWithdrawals);
            return result;
        }

        public override ExplainedNumber CalculateClanIncome(Clan clan, bool includeDescriptions = false, bool applyWithdrawals = false, bool includeDetails = false)
        {
            ExplainedNumber result = _baseModel.CalculateClanIncome(clan, includeDescriptions, applyWithdrawals, includeDetails);
            AddContestedLegalTitleIncomePenalty(clan, ref result, includeDetails);
            AddFeudalServiceOwed(clan, ref result, includeDetails);
            AddSeneschalAdministration(clan, ref result, includeDetails);
            Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>()?.AddCouncilSalaryIncome(clan, ref result, applyWithdrawals);
            return result;
        }

        public override ExplainedNumber CalculateClanExpenses(Clan clan, bool includeDescriptions = false, bool applyWithdrawals = false, bool includeDetails = false)
        {
            ExplainedNumber result = _baseModel.CalculateClanExpenses(clan, includeDescriptions, applyWithdrawals, includeDetails);
            AddSeneschalAssignmentExpense(clan, ref result);
            Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>()?.AddCouncilSalaryExpenses(clan, ref result, applyWithdrawals);
            return result;
        }

        public override ExplainedNumber CalculateTownIncomeFromTariffs(Clan clan, Town town, bool applyWithdrawals)
        {
            return _baseModel.CalculateTownIncomeFromTariffs(clan, town, applyWithdrawals);
        }

        public override int CalculateTownIncomeFromProjects(Town town)
        {
            return _baseModel.CalculateTownIncomeFromProjects(town);
        }

        public override int CalculateNotableDailyGoldChange(Hero hero, bool applyWithdrawals)
        {
            return _baseModel.CalculateNotableDailyGoldChange(hero, applyWithdrawals);
        }

        public override int CalculateVillageIncome(Clan clan, Village village, bool applyWithdrawals)
        {
            // Keep the public village calculation neutral. The contested-title loss is
            // applied once as a visible clan-finance line item after base income is built.
            return _baseModel.CalculateVillageIncome(clan, village, applyWithdrawals);
        }

        public override int CalculateOwnerIncomeFromCaravan(MobileParty caravan)
        {
            return _baseModel.CalculateOwnerIncomeFromCaravan(caravan);
        }

        public override int CalculateOwnerIncomeFromWorkshop(Workshop workshop)
        {
            return _baseModel.CalculateOwnerIncomeFromWorkshop(workshop);
        }

        public override float RevenueSmoothenFraction()
        {
            return _baseModel.RevenueSmoothenFraction();
        }

        private void AddContestedLegalTitleIncomePenalty(Clan clan, ref ExplainedNumber result, bool includeDetails)
        {
            if (clan == null)
                return;

            int totalPenalty = 0;

            foreach (Town town in clan.Fiefs)
                AddTownPenalty(town, ref result, includeDetails, ref totalPenalty);

            foreach (Village village in clan.Villages)
                AddVillagePenalty(clan, village, ref result, includeDetails, ref totalPenalty);

            if (!includeDetails && totalPenalty != 0)
                result.Add(totalPenalty, new TextObject("{=BC_TitlePenalty_ContestedLegalTitlesIncome}Contested legal titles"));
        }

        private void AddFeudalServiceOwed(Clan clan, ref ExplainedNumber result, bool includeDetails)
        {
            if (clan == null || string.IsNullOrWhiteSpace(clan.StringId))
                return;

            FeudalServiceLedger ledger = GetFeudalServiceLedger();
            if (ledger == null || !ledger.TryGetEntry(clan.StringId, out FeudalServiceLedgerEntry entry))
                return;

            if (includeDetails)
            {
                foreach (FeudalServiceLedgerLine payment in entry.Payments)
                {
                    TextObject text = new TextObject("{=BC_ServiceOwed_Payment}{TITLE_NAME}: service owed to {LIEGE_NAME}");
                    text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + payment.TitleName));
                    text.SetTextVariable("LIEGE_NAME", new TextObject("{=!}" + payment.OtherClanName));
                    result.Add(-payment.Amount, text);
                }

                foreach (FeudalServiceLedgerLine receipt in entry.Receipts)
                {
                    TextObject text = new TextObject("{=BC_ServiceOwed_Receipt}{TITLE_NAME}: service owed by {VASSAL_NAME}");
                    text.SetTextVariable("TITLE_NAME", new TextObject("{=!}" + receipt.TitleName));
                    text.SetTextVariable("VASSAL_NAME", new TextObject("{=!}" + receipt.OtherClanName));
                    result.Add(receipt.Amount, text);
                }
            }
            else
            {
                if (entry.TotalPayments > 0)
                    result.Add(-entry.TotalPayments, new TextObject("{=BC_ServiceOwed_Payments}Service owed"));
                if (entry.TotalReceipts > 0)
                    result.Add(entry.TotalReceipts, new TextObject("{=BC_ServiceOwed_Receipts}Service received"));
            }
        }

        private void AddSeneschalAdministration(Clan clan, ref ExplainedNumber result, bool includeDetails)
        {
            Kingdom kingdom = clan?.Kingdom;
            if (kingdom?.RulingClan != clan)
                return;

            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            float bonusRate = council?.GetSeneschalTaxBonusRate(kingdom) ?? 0f;
            if (bonusRate <= 0f)
                return;

            int totalBonus = 0;
            foreach (Town town in clan.Fiefs
                .Where(fief => fief != null)
                .OrderByDescending(fief => fief.Prosperity)
                .Take(3))
            {
                int taxableIncome = ApplyContestedTaxFactor(
                    (int)Math.Round(Campaign.Current?.Models?.SettlementTaxModel?.CalculateTownTax(town, false).ResultNumber ?? 0f),
                    town);
                foreach (Village village in clan.Villages.Where(village => village?.Bound == town.Settlement))
                {
                    taxableIncome += ApplyContestedTaxFactor(
                        _baseModel.CalculateVillageIncome(clan, village, false),
                        village);
                }

                int bonus = Math.Max(0, (int)Math.Round(taxableIncome * bonusRate));
                if (bonus <= 0)
                    continue;

                totalBonus += bonus;
                if (includeDetails)
                {
                    TextObject detail = new TextObject("{=BC_Council_SeneschalTaxDetail}{FIEF_NAME}: {OFFICE} administration income");
                    detail.SetTextVariable("FIEF_NAME", town.Name);
                    detail.SetTextVariable("OFFICE", PrivyCouncilBehavior.GetLocalizedOfficeName(
                        PrivyCouncilOffice.Seneschal,
                        kingdom));
                    result.Add(bonus, detail);
                }
            }

            if (!includeDetails && totalBonus > 0)
            {
                TextObject detail = new TextObject("{=BC_Council_SeneschalTax}{OFFICE} administration income");
                detail.SetTextVariable("OFFICE", PrivyCouncilBehavior.GetLocalizedOfficeName(
                    PrivyCouncilOffice.Seneschal,
                    kingdom));
                result.Add(totalBonus, detail);
            }
        }

        private static void AddSeneschalAssignmentExpense(Clan clan, ref ExplainedNumber result)
        {
            Kingdom kingdom = clan?.Kingdom;
            if (kingdom?.RulingClan != clan)
                return;

            PrivyCouncilBehavior council = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>();
            if (council == null)
                return;

            int infrastructureCost = council.GetFundedAssignmentDailyCost(
                kingdom,
                "seneschal_subsidize_infrastructure");
            if (infrastructureCost > 0 && clan.Gold >= infrastructureCost)
            {
                result.Add(
                    -infrastructureCost,
                    new TextObject("{=BC_Council_InfrastructureExpense}Infrastructure subsidies"));
                return;
            }

            int provisionsCost = council.GetFundedAssignmentDailyCost(
                kingdom,
                "seneschal_stockpile_provisions");
            if (provisionsCost > 0 && clan.Gold >= provisionsCost)
            {
                result.Add(
                    -provisionsCost,
                    new TextObject("{=BC_Council_ProvisionsExpense}Provision stockpiles"));
            }
        }

        private FeudalServiceLedger GetFeudalServiceLedger()
        {
            int day = (int)CampaignTime.Now.ToDays;
            FeudalTitleBehavior titleBehavior = FeudalTitleBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalTitleBehavior>();
            FeudalServiceBehavior serviceBehavior = FeudalServiceBehavior.Instance ?? Campaign.Current?.GetCampaignBehavior<FeudalServiceBehavior>();
            int titleRevision = titleBehavior?.RuntimeRevision ?? 0;
            int serviceRevision = serviceBehavior?.RuntimeRevision ?? 0;

            if (_serviceLedger != null
                && _serviceLedgerDay == day
                && _serviceLedgerTitleRevision == titleRevision
                && _serviceLedgerServiceRevision == serviceRevision)
            {
                return _serviceLedger;
            }

            _serviceLedgerDay = day;
            _serviceLedgerTitleRevision = titleRevision;
            _serviceLedgerServiceRevision = serviceRevision;
            _serviceLedger = BuildFeudalServiceLedger(titleBehavior, serviceBehavior);
            return _serviceLedger;
        }

        private FeudalServiceLedger BuildFeudalServiceLedger(FeudalTitleBehavior titleBehavior, FeudalServiceBehavior serviceBehavior)
        {
            FeudalServiceLedger ledger = new FeudalServiceLedger();
            if (titleBehavior == null || serviceBehavior == null)
                return ledger;

            Dictionary<string, int> taxableIncomeByTitle = new Dictionary<string, int>();
            List<FeudalTitleRecord> titles = titleBehavior.GetAllTitles()
                .Where(title => title != null && title.IsActive)
                .OrderBy(title => title.TitleType)
                .ToList();

            foreach (FeudalTitleRecord title in titles.Where(title => title.TitleType == FeudalTitleType.Barony))
            {
                int income = CalculateEffectiveBaronyIncome(titleBehavior, title);
                if (income > 0)
                    taxableIncomeByTitle[title.TitleId] = income;
            }

            foreach (FeudalTitleRecord title in titles)
            {
                taxableIncomeByTitle.TryGetValue(title.TitleId, out int titleIncome);
                if (titleIncome <= 0)
                    continue;

                FeudalTitleRecord parent = titleBehavior.GetParentTitle(title, FeudalHierarchyMode.DeFacto);
                if (parent == null || !parent.IsActive)
                    continue;

                Clan childHolder = ResolveClan(title.DeFactoHolderClanId);
                Clan parentHolder = ResolveClan(parent.DeFactoHolderClanId);
                if (childHolder == null || parentHolder == null || childHolder.Kingdom == null || childHolder.Kingdom != parentHolder.Kingdom)
                    continue;

                if (childHolder == parentHolder)
                {
                    AddTitleIncome(taxableIncomeByTitle, parent.TitleId, titleIncome);
                    continue;
                }

                float taxShare = serviceBehavior.GetTaxShare(title, parent);
                int tax = Math.Max(0, (int)Math.Round(titleIncome * taxShare));
                if (parentHolder == parentHolder.Kingdom?.RulingClan)
                {
                    float auditBonus = Campaign.Current?.GetCampaignBehavior<PrivyCouncilBehavior>()?
                        .GetAuditVassalsServiceBonusRate(parentHolder.Kingdom) ?? 0f;
                    tax = Math.Min(titleIncome, Math.Max(0, (int)Math.Round(tax * (1f + auditBonus))));
                }
                if (tax <= 0)
                    continue;

                string titleName = FeudalTitleDisplayHelper.FormatTitleName(title, childHolder);
                ledger.AddPayment(childHolder, parentHolder, titleName, tax);
                AddTitleIncome(taxableIncomeByTitle, parent.TitleId, tax);
            }

            return ledger;
        }

        private int CalculateEffectiveBaronyIncome(FeudalTitleBehavior titleBehavior, FeudalTitleRecord title)
        {
            Settlement settlement = ResolveSettlement(title?.CapitalSettlementId);
            Town town = settlement?.Town;
            if (town == null)
                return 0;

            Clan holder = ResolveClan(title.DeFactoHolderClanId) ?? town.OwnerClan;
            if (holder == null)
                return 0;

            int income = 0;
            int townTax = (int)Math.Round(Campaign.Current?.Models?.SettlementTaxModel?.CalculateTownTax(town, false).ResultNumber ?? 0f);
            income += ApplyContestedTaxFactor(townTax, town);

            foreach (Village village in holder.Villages.Where(village => village?.Bound == settlement))
            {
                int villageIncome = _baseModel.CalculateVillageIncome(holder, village, false);
                income += ApplyContestedTaxFactor(villageIncome, village);
            }

            return Math.Max(0, income);
        }

        private static int ApplyContestedTaxFactor(int income, Town town)
        {
            if (income <= 0)
                return 0;

            return FeudalTitlePenaltyHelper.IsContestedLegalTitle(town, out FeudalTitleRecord _)
                ? Math.Max(0, (int)Math.Round(income * (1f + C.FeudalTitleContestedTaxFactor)))
                : income;
        }

        private static int ApplyContestedTaxFactor(int income, Village village)
        {
            if (income <= 0)
                return 0;

            return FeudalTitlePenaltyHelper.IsBoundToContestedLegalTitle(village, out FeudalTitleRecord _)
                ? Math.Max(0, (int)Math.Round(income * (1f + C.FeudalTitleContestedTaxFactor)))
                : income;
        }

        private static void AddTitleIncome(Dictionary<string, int> incomeByTitle, string titleId, int amount)
        {
            if (string.IsNullOrWhiteSpace(titleId) || amount <= 0)
                return;

            incomeByTitle.TryGetValue(titleId, out int current);
            incomeByTitle[titleId] = current + amount;
        }

        private static Settlement ResolveSettlement(string settlementId)
        {
            return string.IsNullOrWhiteSpace(settlementId)
                ? null
                : Settlement.All.FirstOrDefault(settlement => settlement != null && settlement.StringId == settlementId);
        }

        private static Clan ResolveClan(string clanId)
        {
            return string.IsNullOrWhiteSpace(clanId)
                ? null
                : Clan.All.FirstOrDefault(clan => clan != null && clan.StringId == clanId);
        }

        private static void AddTownPenalty(Town town, ref ExplainedNumber result, bool includeDetails, ref int totalPenalty)
        {
            if (town == null || !FeudalTitlePenaltyHelper.IsContestedLegalTitle(town, out FeudalTitleRecord _))
                return;

            int baseIncome = (int)(Campaign.Current?.Models?.SettlementTaxModel?.CalculateTownTax(town, false).ResultNumber ?? 0f);
            AddIncomePenalty(baseIncome, town.Name, ref result, includeDetails, ref totalPenalty);
        }

        private void AddVillagePenalty(Clan clan, Village village, ref ExplainedNumber result, bool includeDetails, ref int totalPenalty)
        {
            if (village == null || !FeudalTitlePenaltyHelper.IsBoundToContestedLegalTitle(village, out FeudalTitleRecord _))
                return;

            int baseIncome = _baseModel.CalculateVillageIncome(clan, village, false);
            AddIncomePenalty(baseIncome, village.Settlement?.Name, ref result, includeDetails, ref totalPenalty);
        }

        private static void AddIncomePenalty(int baseIncome, TextObject fiefName, ref ExplainedNumber result, bool includeDetails, ref int totalPenalty)
        {
            if (baseIncome <= 0)
                return;

            int penalty = (int)Math.Round(baseIncome * C.FeudalTitleContestedTaxFactor);
            if (penalty == 0)
                return;

            if (includeDetails)
            {
                TextObject text = new TextObject("{=BC_TitlePenalty_ContestedLegalTitleIncome}{FIEF_NAME}: contested legal title");
                text.SetTextVariable("FIEF_NAME", fiefName ?? new TextObject(string.Empty));
                result.Add(penalty, text);
            }
            else
            {
                totalPenalty += penalty;
            }
        }

        private sealed class FeudalServiceLedger
        {
            private readonly Dictionary<string, FeudalServiceLedgerEntry> _entriesByClanId = new Dictionary<string, FeudalServiceLedgerEntry>();

            public bool TryGetEntry(string clanId, out FeudalServiceLedgerEntry entry)
            {
                entry = null;
                return !string.IsNullOrWhiteSpace(clanId) && _entriesByClanId.TryGetValue(clanId, out entry);
            }

            public void AddPayment(Clan payer, Clan receiver, string titleName, int amount)
            {
                if (payer == null || receiver == null || amount <= 0)
                    return;

                GetOrCreate(payer.StringId).AddPayment(receiver.Name?.ToString() ?? receiver.StringId, titleName, amount);
                GetOrCreate(receiver.StringId).AddReceipt(payer.Name?.ToString() ?? payer.StringId, titleName, amount);
            }

            private FeudalServiceLedgerEntry GetOrCreate(string clanId)
            {
                if (!_entriesByClanId.TryGetValue(clanId, out FeudalServiceLedgerEntry entry))
                {
                    entry = new FeudalServiceLedgerEntry();
                    _entriesByClanId[clanId] = entry;
                }

                return entry;
            }
        }

        private sealed class FeudalServiceLedgerEntry
        {
            public List<FeudalServiceLedgerLine> Payments { get; } = new List<FeudalServiceLedgerLine>();
            public List<FeudalServiceLedgerLine> Receipts { get; } = new List<FeudalServiceLedgerLine>();
            public int TotalPayments => Payments.Sum(line => line.Amount);
            public int TotalReceipts => Receipts.Sum(line => line.Amount);

            public void AddPayment(string otherClanName, string titleName, int amount)
            {
                Payments.Add(new FeudalServiceLedgerLine(otherClanName, titleName, amount));
            }

            public void AddReceipt(string otherClanName, string titleName, int amount)
            {
                Receipts.Add(new FeudalServiceLedgerLine(otherClanName, titleName, amount));
            }
        }

        private sealed class FeudalServiceLedgerLine
        {
            public string OtherClanName { get; }
            public string TitleName { get; }
            public int Amount { get; }

            public FeudalServiceLedgerLine(string otherClanName, string titleName, int amount)
            {
                OtherClanName = otherClanName ?? string.Empty;
                TitleName = titleName ?? string.Empty;
                Amount = amount;
            }
        }
    }
}
