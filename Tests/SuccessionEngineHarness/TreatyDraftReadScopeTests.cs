using System;
using System.Reflection;
using System.Threading.Tasks;
using BellumCivile;

internal static class TreatyDraftReadScopeTests
{
    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(TreatyTermRecord).Assembly;
        var type = assembly.GetType("BellumCivile.TreatyDraftReadScope");
        var read = type.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic);
        IDisposable Begin() => (IDisposable)Activator.CreateInstance(type, true);
        T Read<T>(object a, object b, object c, Func<T> factory)
            => (T)read.MakeGenericMethod(typeof(T)).Invoke(null, new object[] { a, b, c, factory });

        var first = new object();
        var second = new object();
        int reads = 0;
        Func<int> fetch = () => ++reads;
        check(Read(first, second, null, fetch) == 1 && Read(first, second, null, fetch) == 2,
            "Outside AI search every read stays fresh");
        using (Begin())
        {
            int value = Read(first, second, null, fetch);
            for (int i = 0; i < 1000; i++) Read(first, second, null, fetch);
            check(reads == 3, "Thousand candidate reads resolve stable data once");
            check(Read(second, first, null, fetch) == 4, "Opposite treaty direction has distinct data");
            check(Read(first, second, new object(), fetch) == 5, "Different war identity has distinct data");
            check(Read(first, second, null, () => "typed") == "typed", "Different candidate types cannot collide");
            int nullReads = 0;
            Func<object> missing = () => { nullReads++; return null; };
            Read(first, null, null, missing);
            Read(first, null, null, missing);
            check(nullReads == 1, "Absent heir results can also be reused");
            using (Begin())
                check(Read(first, second, null, fetch) == 6, "Nested draft starts an independent snapshot");
            check(Read(first, second, null, fetch) == value, "Nested draft disposal restores outer snapshot");
            int otherThread = Task.Run(() =>
            {
                int count = 0;
                Read(first, second, null, () => ++count);
                Read(first, second, null, () => ++count);
                return count;
            }).GetAwaiter().GetResult();
            check(otherThread == 2, "Draft scope does not leak to another thread");
        }
        check(Read(first, second, null, fetch) == 7, "Settlement reads refresh after search ends");
        try
        {
            using (Begin())
            {
                Read(first, second, null, fetch);
                throw new InvalidOperationException();
            }
        }
        catch (InvalidOperationException) { }
        check(Read(first, second, null, fetch) == 9, "Exceptional exit clears drafting snapshot");

        var service = assembly.GetType("BellumCivile.TreatyDraftService");
        var prisoners = service.GetMethod("GetAvailablePrisonerReleases");
        var fiefs = service.GetMethod("GetAvailableFiefTransfers");
        object prisonerList, fiefList;
        using (Begin())
        {
            prisonerList = prisoners.Invoke(null, new object[] { null, null });
            fiefList = fiefs.Invoke(null, new object[] { null, null, null });
            check(ReferenceEquals(prisonerList, prisoners.Invoke(null, new object[] { null, null })),
                "Production prisoner availability uses the drafting snapshot");
            check(ReferenceEquals(fiefList, fiefs.Invoke(null, new object[] { null, null, null })),
                "Production fief availability uses the drafting snapshot");
        }
        check(!ReferenceEquals(prisonerList, prisoners.Invoke(null, new object[] { null, null })),
            "Production prisoner availability is rebuilt outside search");
        check(!ReferenceEquals(fiefList, fiefs.Invoke(null, new object[] { null, null, null })),
            "Production fief availability is rebuilt outside search");
    }
}
