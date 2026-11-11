using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BellumCivile;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

internal static class VoluntaryPactTransactionTests
{
    private static int _events;
    private static bool _throwEvent;
    private static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool Publish() { _events++; if (_throwEvent) throw new InvalidOperationException("test listener"); return false; }
    private static bool NoLog() => false;

    internal static void Run(Action<bool, string> check)
    {
        Type recordType = typeof(VoluntaryPactProposalRecord);
        Type paymentType = recordType.Assembly.GetType("BellumCivile.VoluntaryPactPayment", true);
        object Call(VoluntaryPactProposalRecord p, string name, params object[] args) => AccessTools.Method(recordType, name).Invoke(p, args);
        bool Pay(VoluntaryPactProposalRecord p, bool active = true) => (bool)AccessTools.Method(paymentType, "TryPay").Invoke(null, new object[] { p, active });
        VoluntaryPactProposalRecord New(int amount = 50000) => new VoluntaryPactProposalRecord {
            Id = "test-proposal", Source = Blank<Kingdom>(), Target = Blank<Kingdom>(), SourceHouse = Blank<Clan>(),
            TargetHouse = Blank<Clan>(), Sponsor = Blank<Clan>(), SourceHostage = Blank<Hero>(),
            DurationDays = 100, BaselineDays = 50, Compensation = amount, CreatedDay = 1,
            Payer = Blank<Hero>(), Payee = Blank<Hero>() };
        VoluntaryPactProposalRecord Delivery(int amount = 50000)
        {
            var p = New(amount);
            Call(p, "TrySubmit"); Call(p, "TryFinishSourceVote", true);
            Call(p, "TryChooseTargetHostage", Blank<Hero>()); Call(p, "TryFinishTargetVote", true);
            p.PactId = "pact";
            return p;
        }
        var ids = recordType.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => f.GetCustomAttributesData().Single(a => a.AttributeType.Name == "SaveableFieldAttribute").ConstructorArguments[0].Value).ToArray();
        check(ids.Length == ids.Distinct().Count(), "Voluntary proposal save fields have unique IDs");
        var p1 = New();
        check(!(bool)Call(p1, "TryFinishTargetVote", true) && (bool)Call(p1, "TrySubmit")
            && !(bool)Call(p1, "TrySubmit"), "A proposal must be submitted once before either vote");
        check((bool)Call(p1, "TryFinishSourceVote", true) && !(bool)Call(p1, "TryChooseTargetHostage", p1.SourceHostage)
            && (bool)Call(p1, "TryChooseTargetHostage", Blank<Hero>()), "A source vote precedes choosing a different target hostage");
        check((bool)Call(p1, "TryFinishTargetVote", false) && p1.Phase == VoluntaryPactProposalPhase.Cancelled
            && !(bool)Call(p1, "TryFinishTargetVote", true), "A rejected target vote cannot be replayed");
        var bad = New(); bad.DurationDays = 30;
        check(!(bool)Call(bad, "TrySubmit"), "A draft below its saved minimum cannot be submitted");

        var harmony = new Harmony("bellum.tests.voluntary_pact_payment");
        harmony.Patch(AccessTools.Method(paymentType, "PublishTrade"), prefix: new HarmonyMethod(typeof(VoluntaryPactTransactionTests), nameof(Publish)));
        harmony.Patch(AccessTools.Method(typeof(BellumCivileLogger), "Log"), prefix: new HarmonyMethod(typeof(VoluntaryPactTransactionTests), nameof(NoLog)));
        try
        {
            _events = 0; _throwEvent = false;
            var noPact = Delivery(); noPact.Payer.Gold = 100000;
            check(!Pay(noPact, false) && noPact.Payer.Gold == 100000 && !noPact.GoldDebited,
                "Compensation cannot move before pact activation");
            var poor = Delivery(); poor.Payer.Gold = 10000;
            check(!Pay(poor) && poor.Payer.Gold == 10000 && !poor.GoldDebited && !poor.GoldCredited,
                "Insufficient funds never produce a partial debit");
            var paid = Delivery(); paid.Payer.Gold = 200000; paid.Payee.Gold = 10000;
            check(Pay(paid) && paid.Payer.Gold == 150000 && paid.Payee.Gold == 60000
                && paid.GoldDebited && paid.GoldCredited && _events == 1, "Approved payment moves the exact amount once");
            check(Pay(paid) && paid.Payer.Gold == 150000 && paid.Payee.Gold == 60000 && _events == 1,
                "Retrying completed payment cannot repeat money or event");
            check(!(bool)Call(paid, "TryCancel") && (bool)Call(paid, "TryComplete"),
                "An established paid pact cannot be cancelled as an uncommitted proposal");
            var resumed = Delivery(); resumed.Payer.Gold = 150000; resumed.Payee.Gold = 10000; resumed.GoldDebited = true;
            check(Pay(resumed) && resumed.Payer.Gold == 150000 && resumed.Payee.Gold == 60000,
                "Saved debit receipt resumes only the missing credit");
            var expiredAfterDebit = Delivery(); expiredAfterDebit.GoldDebited = true; expiredAfterDebit.Payee.Gold = 10;
            check(Pay(expiredAfterDebit, false) && expiredAfterDebit.Payee.Gold == 50010,
                "A committed debit can finish its credit even if the pact ends during recovery");
            var overflow = Delivery(); overflow.GoldDebited = true; overflow.Payee.Gold = int.MaxValue;
            check(!Pay(overflow) && !(bool)Call(overflow, "TryCancel") && !overflow.GoldCredited,
                "Interrupted debit remains recoverable when the recipient cannot accept the credit");
            overflow.Payee.Gold = 1;
            check(Pay(overflow) && overflow.Payee.Gold == 50001, "Deferred credit resumes without a second debit");
            var corrupt = Delivery(); corrupt.GoldCredited = true;
            check(!Pay(corrupt) && !corrupt.GoldDebited, "Contradictory credit-only receipts fail closed");
            var free = Delivery(0);
            check(Pay(free) && free.GoldDebited && free.GoldCredited && (bool)Call(free, "TryComplete"),
                "A free mutual agreement completes without a gold transfer");
            var listenerFailure = Delivery(); listenerFailure.Payer.Gold = 100000; _throwEvent = true;
            int before = _events;
            check(Pay(listenerFailure) && Pay(listenerFailure) && _events == before + 1
                && listenerFailure.Payer.Gold == 50000 && listenerFailure.Payee.Gold == 50000,
                "A failing trade-event listener cannot duplicate a committed payment");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
