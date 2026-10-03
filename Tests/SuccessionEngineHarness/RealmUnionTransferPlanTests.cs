using System;
using System.Reflection;
using BellumCivile;

internal static class RealmUnionTransferPlanTests
{
    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(FeudalTitleRecord).Assembly;
        var facts = assembly.GetType("BellumCivile.RealmUnionClanSnapshot");
        var planner = assembly.GetType("BellumCivile.RealmUnionTransferPlan");
        var create = planner.GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic);
        var retire = planner.GetMethod("CanRetireSource", BindingFlags.Instance | BindingFlags.NonPublic);
        object Clan(string id, string realm, bool mercenary = false, float influence = 100, int debt = 25)
            => Activator.CreateInstance(facts, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { id, realm, mercenary, influence, debt }, null);
        Array Clans(params object[] entries)
        {
            var array = Array.CreateInstance(facts, entries.Length);
            for (int i = 0; i < entries.Length; i++) array.SetValue(entries[i], i);
            return array;
        }
        object plan = null;
        bool Create(Array entries, string source = "sturgia", string destination = "vlandia")
        {
            var args = new object[] { source, destination, entries, null, null };
            bool result = (bool)create.Invoke(null, args);
            plan = args[3];
            check(result || !string.IsNullOrEmpty((string)args[4]), "Invalid union snapshot explains refusal");
            return result;
        }
        var original = Clans(Clan("noble", "sturgia"), Clan("company", "sturgia", true));
        check(!Create(original, destination: "sturgia"), "Union rejects identical realm identities");
        check(!Create(Clans()), "Union rejects empty initial snapshot");
        check(!Create(null), "Union rejects absent initial snapshot");
        check(!Create(Clans(Clan("a", "sturgia"), Clan("a", "sturgia"))), "Union rejects duplicate houses");
        check(!Create(Clans(Clan("a", "other"))), "Union rejects wrong-realm snapshot");
        check(!Create(Clans(Clan("company", "sturgia", true))), "Mercenary-only realm is not a Crown transfer");
        check(!Create(Clans(Clan("a", "sturgia", influence: float.NaN))), "Invalid balances block planning");
        check(Create(original), "Nobles and contract companies produce a union plan");
        original.SetValue(Clan("changed", "sturgia"), 0);
        var healthy = Clans(Clan("noble", "vlandia"), Clan("company", null));
        bool Retire(Array current, string[] restored = null, string[] contracts = null,
            bool crown = true, bool obligations = true)
        {
            var args = new object[] { current, restored ?? new[] { "noble" }, contracts ?? new[] { "company" }, crown, obligations, null };
            bool result = (bool)retire.Invoke(plan, args);
            check(result || !string.IsNullOrEmpty((string)args[5]), "Blocked retirement explains its pending condition");
            return result;
        }
        check(Retire(healthy), "Verified move and ended contract permit retirement assessment");
        check(Retire(healthy), "Repeated retirement assessment is pure and stable");
        check(!Retire(healthy, restored: new string[0]), "Membership alone cannot replace restoration receipt");
        check(!Retire(healthy, contracts: new string[0]), "Mercenary independence alone cannot replace termination receipt");
        check(!Retire(healthy, crown: false), "Crown verification is mandatory");
        check(!Retire(healthy, obligations: false), "Obligation settlement is mandatory");
        check(!Retire(null), "Missing live snapshot never implies an empty realm");
        check(!Retire(Clans(Clan("company", null))), "Missing expected noble blocks retirement");
        check(!Retire(Clans(Clan("noble", "vlandia"))), "Missing mercenary company is not successful termination");
        check(!Retire(Clans(Clan("noble", "vlandia", influence: 0), Clan("company", null))), "Native influence reset must be restored");
        check(!Retire(Clans(Clan("noble", "vlandia", debt: 0), Clan("company", null))), "Native debt reset cannot forgive obligations silently");
        check(!Retire(Clans(Clan("noble", "third"), Clan("company", null))), "Unexpected noble allegiance blocks retirement");
        check(!Retire(Clans(Clan("noble", "vlandia"), Clan("company", "vlandia", true))), "Mercenaries are not silently hired by the survivor");
        check(!Retire(Clans(Clan("noble", "vlandia"), Clan("company", null), Clan("late_arrival", "sturgia"))),
            "An unplanned live clan in the source blocks destructive retirement");
        check(!Retire(Clans(Clan("noble", "sturgia"), Clan("company", null))), "Incomplete movement blocks retirement");
        check(!Retire(Clans(Clan("noble", "vlandia"), Clan("noble", "vlandia"), Clan("company", null))),
            "Ambiguous current membership cannot authorize retirement");
    }
}
