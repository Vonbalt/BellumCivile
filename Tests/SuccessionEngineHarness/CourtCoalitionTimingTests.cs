using System;
using System.Diagnostics;
using System.Reflection;
using BellumCivile;
using HarmonyLib;

internal static class CourtCoalitionTimingTests
{
    internal static void Run(Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(FactionObject).Assembly.GetType("BellumCivile.CourtCoalitionTiming", true);
        var phase = type.GetNestedType("Phase", BindingFlags.NonPublic);
        object Call(object target, string method, params object[] args) => AccessTools.Method(type, method).Invoke(target, args);
        long Ms(int amount) => Stopwatch.Frequency * amount / 1000L;
        void Record(object target, string name, int milliseconds) => Call(target, "Record", Enum.Parse(phase, name), Ms(milliseconds));
        void Result(object target, UltimatumResolution value) => type.GetProperty("Result", flags).SetValue(target, value);
        object Launch() => Activator.CreateInstance(type, flags, null, new object[] { 84.5d, "khuzait", FactionType.Nobility, "house_a", false }, null);
        string Describe(object target, int milliseconds) => (string)Call(target, "Describe", Ms(milliseconds));

        var launch = Launch();
        Record(launch, "Assembly", 4);
        Record(launch, "CoalitionForecast", 6);
        type.GetProperty("BlockedReason", flags).SetValue(launch, "power");
        check(Describe(launch, 9) == null, "Cheap failed coalition attempts do not flood the campaign log");
        string rejected = Describe(launch, 10);
        check(rejected.Contains("source=launch; result=Failed; blocked=power") && rejected.Contains("assembly_ms=4; coalition_forecast_ms=6")
            && !rejected.Contains("war_creation_ms") && !rejected.Contains("ultimatum_ms"),
            "A slow power rejection reports the measured stages without pretending an ultimatum or war took place");

        launch = Launch();
        type.GetProperty("AssembledHouses", flags).SetValue(launch, 4);
        Record(launch, "Assembly", 4);
        Record(launch, "CoalitionForecast", 6);
        Record(launch, "Registration", 2);
        Record(launch, "Ultimatum", 68);
        Record(launch, "RulerForecast", 8);
        Record(launch, "Rally", 10);
        Record(launch, "WarCreation", 47);
        Result(launch, UltimatumResolution.RebellionStarted);
        string automatic = Describe(launch, 81);
        check(automatic.Contains("realm=khuzait; bloc=Nobility; leader=house_a; source=launch; result=RebellionStarted; assembled_houses=4")
            && automatic.Contains("ultimatum_ms=68; ruler_forecast_ms=8; rally_ms=10; war_creation_ms=47") && !automatic.Contains("concession_ms"),
            "AI coalition reports both forecasts and distinguishes recruitment from native war creation within the ultimatum");

        launch = Launch();
        type.GetProperty("AssembledHouses", flags).SetValue(launch, 5);
        Record(launch, "Ultimatum", 2);
        Result(launch, UltimatumResolution.PendingPlayerChoice);
        check(Describe(launch, 3).Contains("result=PendingPlayerChoice"), "Issued ultimatums are logged even below the failed-attempt threshold");
        var response = Call(launch, "ForPlayerResponse", 85.25d);
        Result(response, UltimatumResolution.DemandsAccepted);
        Record(response, "Concession", 12);
        string answered = Describe(response, 13);
        check(answered.Contains("day=85.25; realm=khuzait; bloc=Nobility; leader=house_a; source=player_response; result=DemandsAccepted")
            && answered.Contains("assembled_houses=5; total_ms=13; concession_ms=12") && !answered.Contains("ultimatum_ms"),
            "The response retains the same coalition identity but measures only the player's eventual resolution");
    }
}
