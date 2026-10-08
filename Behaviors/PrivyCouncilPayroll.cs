using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BellumCivile.Behaviors
{
    public sealed partial class PrivyCouncilBehavior
    {
        private List<CouncilSalaryCredit> _salaryCredits = new List<CouncilSalaryCredit>();
        private Dictionary<string, float> _payrollDay = new Dictionary<string, float>();

        private List<CouncilSalaryCredit> BuildCouncilPayroll(Kingdom realm, int budget)
        {
            var result = new List<CouncilSalaryCredit>();
            if (!IsEligiblePermanentRealm(realm) || realm.RulingClan?.Leader == null) return result;
            int remaining = Math.Max(0, budget);
            foreach (var seat in GetOfficeRecords(realm).OrderBy(s => s.Office))
            {
                var holder = GetOfficeHolder(realm, seat.Office);
                if (holder?.Leader == null || holder.IsEliminated || holder.Kingdom != realm || holder == realm.RulingClan
                    || seat.Controversy >= 100) continue;
                int amount = Math.Min(remaining, GetDailySalary(realm, seat.Office));
                if (amount <= 0) continue;
                result.Add(new CouncilSalaryCredit { Realm = realm, Recipient = holder, Office = seat.Office, Amount = amount });
                remaining -= amount;
            }
            return result;
        }

        internal int ExpectedCouncilSalary(Kingdom realm, PrivyCouncilOffice office) =>
            BuildCouncilPayroll(realm, Math.Max(0, realm?.RulingClan?.Gold ?? 0))
                .Where(c => c.Office == office).Sum(c => c.Amount);

        internal static TextObject SalaryFinanceLabel(CouncilSalaryCredit credit, bool expense) =>
            new TextObject(expense ? "{=BC_CouncilPayrollExpense}Council salary: {OFFICE} ({HOUSE})"
                : "{=BC_CouncilPayrollIncome}Council salary: {OFFICE} ({REALM})")
                .SetTextVariable("OFFICE", GetLocalizedOfficeName(credit.Office, credit.Realm))
                .SetTextVariable("HOUSE", credit.Recipient?.Name ?? TextObject.GetEmpty())
                .SetTextVariable("REALM", credit.Realm?.Name ?? TextObject.GetEmpty());

        internal void AddCouncilSalaryIncome(Clan clan, ref ExplainedNumber result, bool applyWithdrawals)
        {
            if (clan?.Leader == null || clan.IsEliminated) return;
            var credits = _salaryCredits.Where(c => c.Recipient == clan).ToList();
            // Preview the next payroll when no funded wages await collection. Reads never fund wages.
            if (!applyWithdrawals && credits.Count == 0)
                credits = BuildCouncilPayroll(clan.Kingdom, Math.Max(0, clan.Kingdom?.RulingClan?.Gold ?? 0))
                    .Where(c => c.Recipient == clan).ToList();
            foreach (var credit in credits)
                result.Add(credit.Amount, SalaryFinanceLabel(credit, false));
            if (applyWithdrawals) _salaryCredits.RemoveAll(c => c.Recipient == clan);
        }

        internal void AddCouncilSalaryExpenses(Clan clan, ref ExplainedNumber result, bool applyWithdrawals)
        {
            var realm = clan?.Kingdom;
            if (clan?.Leader == null || clan.IsEliminated || realm?.RulingClan != clan) return;
            float day = (float)Math.Floor(CampaignTime.Now.ToDays);
            if (applyWithdrawals && _payrollDay.TryGetValue(realm.StringId, out float paidDay) && paidDay == day) return;
            int budget = Math.Max(0, clan.Gold);
            // Reserve only cash remaining after the caller's other expenses; never manufacture salary debt.
            if (applyWithdrawals) budget = Math.Min(budget, Math.Max(0, (int)(clan.Gold + result.ResultNumber)));
            var payroll = BuildCouncilPayroll(realm, budget);
            if (applyWithdrawals) _payrollDay[realm.StringId] = day;
            foreach (var credit in payroll)
            {
                result.Add(-credit.Amount, SalaryFinanceLabel(credit, true));
                if (!applyWithdrawals) continue;
                var existing = _salaryCredits.FirstOrDefault(c => c.Realm == realm && c.Recipient == credit.Recipient && c.Office == credit.Office);
                if (existing == null) _salaryCredits.Add(credit);
                else existing.Amount += credit.Amount;
            }
        }
    }
}
