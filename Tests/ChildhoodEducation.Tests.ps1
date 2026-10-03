param([string]$GameFolder)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/GamePath.ps1"
$harmony = Join-Path (Resolve-BellumGameFolder $GameFolder) 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client\0Harmony.dll'
if (-not (Test-Path -LiteralPath $harmony -PathType Leaf)) {
    throw 'Harmony was not found. Set GameFolder or pass -GameFolder with the Bannerlord installation path.'
}
$sources = @('EducationAgeSchedule.cs', 'Patches/ChildhoodEducationAgePatch.cs') | ForEach-Object { Join-Path $root $_ }
$doubles = @'
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
namespace TaleWorlds.CampaignSystem
{
    public class Hero { public float Age; }
    public class Campaign
    {
        public static Campaign Current = new Campaign();
        public Notices CampaignInformationManager = new Notices();
    }
    public class Notices
    {
        public MapNotificationTypes.EducationMapNotification Pending;
        public bool InformationDataExists(Func<MapNotificationTypes.EducationMapNotification, bool> predicate)
            { return Pending != null && predicate(Pending); }
    }
}
namespace TaleWorlds.CampaignSystem.MapNotificationTypes
{
    public class EducationMapNotification { public Hero Child; public int Age; }
}
namespace TaleWorlds.CampaignSystem.CampaignBehaviors
{
    public class EducationCampaignBehavior
    {
        private enum Stage : short { A, B, C, D, E, F, Count }
        private Dictionary<Hero, short> _previousEducations = new Dictionary<Hero, short>();
        public void Complete(Hero hero, short stage) { _previousEducations[hero] = stage; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ChildStateToAge(Stage stage) { return stage < Stage.Count ? new[] { 2,5,8,11,14,16 }[(int)stage] : -1; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private Stage GetClosestStage(Hero child) { return Stage.F; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool HasNotificationForAge(Hero child, int age) { return false; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool IsValidEducationNotification(MapNotificationTypes.EducationMapNotification data) { return data.Child.Age < BellumCivile.BellumCivileOptions.Adult; }
        public int AgeFor(int stage) { return ChildStateToAge((Stage)stage); }
        public int Next(Hero hero) { return (int)GetClosestStage(hero); }
        public bool Pending(Hero hero, int age) { return HasNotificationForAge(hero, age); }
    }
}
namespace TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapNotificationTypes
{
    public class EducationNotificationItemVM
    {
        private readonly TaleWorlds.CampaignSystem.Hero _child;
        private readonly int _age;
        public bool Removed;
        public EducationNotificationItemVM(TaleWorlds.CampaignSystem.Hero child, int age) { _child = child; _age = age; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void OnEducationCompletedForChild(TaleWorlds.CampaignSystem.Hero child, int age) { if (child == _child && age >= _age) Removed = true; }
        public void Complete(TaleWorlds.CampaignSystem.Hero hero, int age) { OnEducationCompletedForChild(hero, age); }
    }
}
namespace BellumCivile
{
    public static class BellumCivileOptions
    {
        public static bool Enabled = true;
        public static bool EnableCustomAdulthoodAge { get { return Enabled; } }
        public static int Adult = 16;
        public static int[] Ages = new[] { 2,5,8,10,13,15 };
        public static int EducationMilestoneAge(int stage) { return stage < 0 || stage >= 6 ? -1 : EducationAgeSchedule.Normalize(Adult, Ages)[stage]; }
    }
    public static class EducationTests
    {
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        public static void Run()
        {
            var random = new Random(10);
            for (int adult = 16; adult <= 21; adult++)
                for (int trial = 0; trial < 1000; trial++)
                {
                    var input = new int[6];
                    for (int i=0; i<6; i++) input[i] = random.Next(-100, 100);
                    var ages = EducationAgeSchedule.Normalize(adult, input);
                    Check(ages[0] >= 1 && ages[5] < adult, "Age boundaries violated");
                    for (int i=1; i<6; i++) Check(ages[i] > ages[i-1], "Stages out of order");
                }
            new Harmony("bellum.education.tests").PatchAll(typeof(EducationTests).Assembly);
            var behavior = new TaleWorlds.CampaignSystem.CampaignBehaviors.EducationCampaignBehavior();
            var child = new TaleWorlds.CampaignSystem.Hero { Age = 14 };
            for (int i=0; i<6; i++) Check(behavior.AgeFor(i) == BellumCivileOptions.Ages[i], "Patched age mismatch");
            Check(behavior.AgeFor(6) == -1, "Sentinel changed");
            Check(behavior.Next(child) == 0, "Overdue stages skipped");
            behavior.Complete(child, 3);
            Check(behavior.Next(child) == 4, "Completed stage repeated");
            var notice = new TaleWorlds.CampaignSystem.MapNotificationTypes.EducationMapNotification { Child = child, Age = 14 };
            TaleWorlds.CampaignSystem.Campaign.Current.CampaignInformationManager.Pending = notice;
            Check(behavior.Pending(child, 13), "Old-age notice duplicated");
            Check(behavior.IsValidEducationNotification(notice), "Due notice rejected");
            child.Age = 12;
            Check(!behavior.IsValidEducationNotification(notice), "Future stage offered early");
            var vm = new TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapNotificationTypes.EducationNotificationItemVM(child, 14);
            vm.Complete(new TaleWorlds.CampaignSystem.Hero(), 13);
            Check(!vm.Removed, "Another child's notice removed");
            vm.Complete(child, 13);
            Check(vm.Removed, "Old-age notice not removed");
            BellumCivileOptions.Enabled = false;
            Check(behavior.AgeFor(3) == 11 && behavior.AgeFor(5) == 16, "Vanilla ages not restored");
            Check(behavior.Next(child) == 5 && !behavior.Pending(child, 13), "Vanilla scheduling not restored");
        }
    }
}
'@
$sdkVersion = (& dotnet --version).Trim()
$compiler = Join-Path $env:ProgramFiles "dotnet\sdk\$sdkVersion\Roslyn\bincore\csc.dll"
$framework = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('BellumEducationTests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temporary) | Out-Null
try {
    $inputFile = Join-Path $temporary 'Tests.cs'
    $assembly = Join-Path $temporary 'Tests.dll'
    [System.IO.File]::WriteAllText($inputFile, $doubles)
    & dotnet $compiler /nologo /target:library "/out:$assembly" "/reference:$harmony" "/reference:$(Join-Path $framework 'mscorlib.dll')" "/reference:$(Join-Path $framework 'System.dll')" "/reference:$(Join-Path $framework 'System.Core.dll')" $sources $inputFile
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    [System.Reflection.Assembly]::LoadFrom($harmony) | Out-Null
    [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($assembly)) | Out-Null
    [BellumCivile.EducationTests]::Run()
}
finally {
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.cs') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.dll') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporary
}
Write-Output 'Education schedule and Harmony integration tests passed (6,000 schedule cases).'
