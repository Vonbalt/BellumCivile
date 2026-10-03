$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sources = @('FeudalTitleUsurpationAssessmentService.cs', 'DestroyedRealmTitleSafety.cs') | ForEach-Object { Join-Path $root $_ }
$doubles = @'
using System;
using System.Collections.Generic;
namespace TaleWorlds.CampaignSystem
{
    public class Hero { public bool IsDead; public int Gold = 300000; }
    public class Kingdom { public Clan RulingClan; }
    public class Clan
    {
        public static List<Clan> All = new List<Clan>();
        public string StringId = "player";
        public Hero Leader = new Hero();
        public float Influence = 300;
        public bool IsEliminated;
        public Kingdom Kingdom;
    }
}
namespace BellumCivile
{
    public enum FeudalTitleType { Barony, County, Duchy, Kingdom, Empire }
    public enum FeudalHierarchyMode { DeJure, DeFacto }
    public class FeudalTitleRecord
    {
        public bool IsActive = true;
        public string TitleId;
        public string DeJureHolderClanId = "player";
        public string DeFactoHolderClanId = "";
        public FeudalTitleType TitleType = FeudalTitleType.Empire;
    }
    public static class RecoveryTests
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static void Run()
        {
            var clan = new TaleWorlds.CampaignSystem.Clan();
            var behavior = new Behaviors.FeudalTitleBehavior();
            var title = new FeudalTitleRecord { TitleId = "empire_calradia" };
            for (int i=0; i<3; i++) behavior.Children.Add(new FeudalTitleRecord { TitleId = "child" + i, DeFactoHolderClanId = i<2 ? "player" : "other" });
            var result = FeudalTitleUsurpationAssessmentService.Evaluate(behavior, clan, title, true);
            Check(result.CanUsurp && result.IsLawfulAssumption && !result.HasClaim, "Lawful owner cannot recover vacant empire");
            behavior.Children[1].DeFactoHolderClanId = "other";
            Check(!FeudalTitleUsurpationAssessmentService.Evaluate(behavior, clan, title, true).CanUsurp, "Insufficient control accepted");
            behavior.Children[1].DeFactoHolderClanId = "player";
            clan.Influence = 299;
            Check(!FeudalTitleUsurpationAssessmentService.Evaluate(behavior, clan, title, true).CanUsurp, "Influence cost bypassed");
            clan.Influence = 300; clan.Leader.Gold = 299999;
            Check(!FeudalTitleUsurpationAssessmentService.Evaluate(behavior, clan, title, true).CanUsurp, "Gold cost bypassed");
            clan.Leader.Gold = 300000;
            title.DeFactoHolderClanId = "player";
            Check(!FeudalTitleUsurpationAssessmentService.Evaluate(behavior, clan, title, true).CanUsurp, "Fully held title recovered twice");
            title.DeFactoHolderClanId = "other";
            Check(!FeudalTitleUsurpationAssessmentService.Evaluate(behavior, clan, title, true).CanUsurp, "Occupied title treated as vacant");
            title.DeFactoHolderClanId = ""; title.TitleType = FeudalTitleType.Barony;
            Check(!FeudalTitleUsurpationAssessmentService.Evaluate(behavior, clan, title, true).CanUsurp, "Physical land granted without conquest");
            title.TitleType = FeudalTitleType.Empire; title.DeJureHolderClanId = "other";
            Check(!FeudalTitleUsurpationAssessmentService.Evaluate(behavior, clan, title, true).CanUsurp, "Non-owner bypassed claim requirement");
            behavior.HasClaim = true;
            Check(FeudalTitleUsurpationAssessmentService.Evaluate(behavior, clan, title, true).CanUsurp, "Normal usurpation regressed");
            var realm = new TaleWorlds.CampaignSystem.Kingdom { RulingClan = clan };
            clan.Kingdom = realm;
            Check(DestroyedRealmTitleSafety.CanVacate(clan, realm), "Stale crown cannot be cleaned");
            clan.Kingdom = new TaleWorlds.CampaignSystem.Kingdom();
            Check(!DestroyedRealmTitleSafety.CanVacate(clan, realm), "Surviving successor's crown cleared");
            clan.Kingdom = null;
            Check(!DestroyedRealmTitleSafety.CanVacate(clan, realm), "Independent surviving house's title cleared");
            clan.IsEliminated = true;
            Check(DestroyedRealmTitleSafety.CanVacate(clan, realm), "Eliminated old ruler cannot be cleaned");
            Check(!DestroyedRealmTitleSafety.CanVacate(new TaleWorlds.CampaignSystem.Clan(), realm), "Unrelated holder's title cleared");
            Check(!DestroyedRealmTitleSafety.CanVacate(null, realm), "Unknown ownership cleared");
        }
    }
}
namespace BellumCivile.Behaviors
{
    public class FeudalTitleBehavior
    {
        public bool HasClaim;
        public List<FeudalTitleRecord> Children = new List<FeudalTitleRecord>();
        public bool IsCurrentRealmSovereignTitle(TaleWorlds.CampaignSystem.Clan clan, FeudalTitleRecord title) { return false; }
        public int GetTitleFormationGoldCost(FeudalTitleType type) { return 300000; }
        public float GetTitleFormationInfluenceCost(FeudalTitleType type) { return 300; }
        public bool HasActiveClaim(TaleWorlds.CampaignSystem.Clan clan, FeudalTitleRecord title) { return HasClaim; }
        public IEnumerable<FeudalTitleRecord> GetChildTitles(FeudalTitleRecord title, FeudalHierarchyMode mode) { return Children; }
        public bool IsTitleWithinDeFactoAuthority(FeudalTitleRecord title, TaleWorlds.CampaignSystem.Clan clan) { return title.DeFactoHolderClanId == clan.StringId; }
        public bool IsClanWithinDeFactoAuthority(TaleWorlds.CampaignSystem.Clan a, TaleWorlds.CampaignSystem.Clan b) { return false; }
    }
}
'@
$sdkVersion = (& dotnet --version).Trim()
$compiler = Join-Path $env:ProgramFiles "dotnet\sdk\$sdkVersion\Roslyn\bincore\csc.dll"
$framework = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('BellumRecoveryTests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temporary) | Out-Null
try {
    $inputFile = Join-Path $temporary 'Tests.cs'
    $assembly = Join-Path $temporary 'Tests.dll'
    [System.IO.File]::WriteAllText($inputFile, $doubles)
    & dotnet $compiler /nologo /target:library "/out:$assembly" "/reference:$(Join-Path $framework 'mscorlib.dll')" "/reference:$(Join-Path $framework 'System.dll')" "/reference:$(Join-Path $framework 'System.Core.dll')" $sources $inputFile
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($assembly)) | Out-Null
    [BellumCivile.RecoveryTests]::Run()
}
finally {
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.cs') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.dll') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporary
}
Write-Output 'Vacant title recovery and destroyed-realm ownership safety tests passed (15 cases).'
