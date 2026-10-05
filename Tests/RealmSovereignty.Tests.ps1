$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sources = @('RealmSovereignSelection.cs', 'RealmNameConfig.cs') | ForEach-Object { Join-Path $root $_ }
$doubles = @'
using System;
using System.Collections.Generic;
using System.Linq;
namespace TaleWorlds.Localization
{
    public class TextObject
    {
        private string value;
        public TextObject(string text) { value = text; }
        public override string ToString() { return value.StartsWith("{=") ? value.Substring(value.IndexOf('}') + 1) : value; }
    }
}
namespace TaleWorlds.ModuleManager
{
    public class ModuleInfo { public string FolderPath; }
    public static class ModuleHelper
    {
        public static string Root;
        public static IEnumerable<ModuleInfo> GetActiveModules() { return new[] { new ModuleInfo { FolderPath = Root } }; }
    }
}
namespace BellumCivile
{
    public static class BellumCivileLogger { public static void Log(string message) { throw new Exception(message); } }
    public enum FeudalTitleType { Barony, County, Duchy, Kingdom, Empire }
    public class FeudalTitleRecord
    {
        public string TitleId;
        public string Name;
        public string ParentTitleId;
        public string AssociatedKingdomId;
        public string DeFactoHolderClanId = "player";
        public string DeJureHolderClanId = "player";
        public bool IsActive = true;
        public FeudalTitleType TitleType;
    }
    public static class RealmTests
    {
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static void Run(string root)
        {
            var barony = new FeudalTitleRecord { TitleId = "barony" };
            var county = new FeudalTitleRecord { TitleId = "county", TitleType = FeudalTitleType.County };
            var titles = new List<FeudalTitleRecord> { barony };
            Check(RealmSovereignSelection.Select(titles, "player", false, null) == barony, "Independent barony not recognized");
            titles.Add(county);
            Check(RealmSovereignSelection.Select(titles, "player", false, "barony") == county, "Acquisition did not promote realm");
            Check(RealmSovereignSelection.Select(titles, "player", true, "barony") == county, "Full ownership did not provide lawful receiver");
            county.DeJureHolderClanId = "foreign";
            Check(RealmSovereignSelection.Select(titles, "player", false, "barony") == county, "Disputed political rank lost");
            Check(RealmSovereignSelection.Select(titles, "player", true, "county") == barony, "Foreign legal rights overridden");
            county.DeJureHolderClanId = "player";
            county.DeFactoHolderClanId = "foreign";
            Check(RealmSovereignSelection.Select(titles, "player", false, "county") == barony, "Lost title did not demote realm");
            county.DeFactoHolderClanId = "player";
            county.IsActive = false;
            Check(RealmSovereignSelection.Select(titles, "player", true, "county") == barony, "Inactive title selected");
            county.IsActive = true;
            var second = new FeudalTitleRecord { TitleId = "aaa", TitleType = FeudalTitleType.County };
            titles.Add(second);
            Check(RealmSovereignSelection.Select(titles, "player", false, "county") == county, "Equal-rank crown changed");
            Check(RealmSovereignSelection.Select(titles, "player", false, null) == second, "Tie fallback is unstable");
            var empire = new FeudalTitleRecord { TitleId = "empire", TitleType = FeudalTitleType.Empire };
            titles.Add(empire);
            Check(RealmSovereignSelection.Select(titles, "player", false, "county") == empire, "Empire promotion failed");
            Check(RealmSovereignSelection.Select(titles, "new_ruler", false, "empire") == null, "Former ruler's holdings leaked into new rank");
            Check(RealmSovereignSelection.Select(titles, null, false, null) == null, "Missing ruler accepted");
            TaleWorlds.ModuleManager.ModuleHelper.Root = root;
            Check(RealmNameConfig.ResolveRoot("empire_w", "Western Empire") == "Western Calradia", "Imperial root incorrect");
            Check(RealmNameConfig.ResolveRoot("vlandia", "Vlandia") == "Vlandia", "Vlandian root incorrect");
            Check(RealmNameConfig.ResolveRoot("nord", "Nord") == "Nord", "Naval DLC realm root missing");
            Check(RealmNameConfig.ResolveRoot("nord", "My Northern Realm") == null, "Renamed DLC realm overridden");
            Check(RealmNameConfig.ResolveRoot("vlandia", "My Realm") == null, "Player rename overridden");
            Check(RealmNameConfig.ResolveRoot("unknown", "Western Empire") == null, "Unknown realm inferred from words");
            RealmNameConfig.Reset();
            Check(RealmNameConfig.ResolveRoot("empire_s", "Southern Empire") == "Southern Calradia", "Reload failed");
        }
    }
}
'@
$driftSource = Get-Content (Join-Path $root 'Behaviors/FeudalDeJureDriftBehavior.cs') -Raw
$findStart = $driftSource.IndexOf('        private FeudalTitleRecord FindTargetParent(')
$findEnd = $driftSource.IndexOf('        private bool TryResolveUnifiedHolderKingdom(', $findStart)
$resolveStart = $driftSource.IndexOf('        private string ResolveRecordedLegalKingdomId(')
$resolveEnd = $driftSource.IndexOf('        private bool ShouldReverseDrift(', $resolveStart)
if ($findStart -lt 0 -or $resolveStart -lt 0 -or $findEnd -le $findStart -or $resolveEnd -le $resolveStart) { throw 'Drift production method boundaries not found.' }
$driftDoubles = @'
namespace BellumCivile
{
    public enum FeudalHierarchyMode { DeJure, DeFacto }
    public class Kingdom { public string StringId = "independent"; public Clan RulingClan; }
    public class Clan { public string StringId = "player"; public Kingdom Kingdom; }
    public class FeudalTitleBehavior
    {
        public List<FeudalTitleRecord> Titles = new List<FeudalTitleRecord>();
        public FeudalTitleRecord GetTitle(string id) { return Titles.FirstOrDefault(t => t.TitleId == id); }
        public IEnumerable<FeudalTitleRecord> GetAllTitles() { return Titles; }
        public IEnumerable<FeudalTitleRecord> GetTitleAndDescendants(FeudalTitleRecord title)
        {
            yield return title;
            foreach (var child in Titles.Where(t => t.ParentTitleId == title.TitleId))
                foreach (var descendant in GetTitleAndDescendants(child)) yield return descendant;
        }
        public FeudalTitleRecord GetRealmSovereignTitle(Kingdom realm, FeudalHierarchyMode mode)
            { return RealmSovereignSelection.Select(Titles, realm.RulingClan.StringId, mode == FeudalHierarchyMode.DeJure, "barony"); }
        public FeudalTitleRecord GetKingdomPoliticalTitle(Kingdom realm) { return GetTitle("barony"); }
        public bool AreTitlesAdjacentForTitleLogic(FeudalTitleRecord a, FeudalTitleRecord b) { return true; }
    }
    public class DriftReceiverTests
    {
        private Clan ruler;
        private Clan ResolveClan(string id) { return id == "player" ? ruler : null; }
        private Kingdom ResolveTitleKingdom(FeudalTitleRecord title) { return null; }
        private bool IsPermanentKingdom(Kingdom realm) { return realm != null; }
        private bool IsTitleHeldInsideKingdom(FeudalTitleRecord title, Kingdom realm)
            { return title.DeFactoHolderClanId == "player" && title.DeJureHolderClanId == "player"; }
        public void Run()
        {
            var realm = new Kingdom();
            ruler = new Clan { Kingdom = realm };
            realm.RulingClan = ruler;
            var titles = new FeudalTitleBehavior();
            var county = new FeudalTitleRecord { TitleId = "county", TitleType = FeudalTitleType.County, ParentTitleId = "duchy", AssociatedKingdomId = "vlandia" };
            var barony = new FeudalTitleRecord { TitleId = "barony", ParentTitleId = "county", AssociatedKingdomId = "vlandia" };
            var secondBarony = new FeudalTitleRecord { TitleId = "barony2", ParentTitleId = "county", AssociatedKingdomId = "vlandia" };
            titles.Titles.Add(county); titles.Titles.Add(barony); titles.Titles.Add(secondBarony);
            if (FindTargetParent(titles, barony, county, realm) != county || FindTargetParent(titles, secondBarony, county, realm) != county)
                throw new Exception("Acquired independent county rejected as receiver");
            if (ResolveRecordedLegalKingdomId(titles, county) != realm.StringId) throw new Exception("Current lawful crown not recognized");
            if (ResolveRecordedLegalKingdomId(titles, barony) != "vlandia" || county.AssociatedKingdomId != "vlandia")
                throw new Exception("Recognizing crown instantly annexed territory");
            county.DeJureHolderClanId = "foreign";
            if (FindTargetParent(titles, secondBarony, county, realm) != null) throw new Exception("Disputed county allowed as lawful receiver");
        }
'@
$doubles += $driftDoubles + $driftSource.Substring($findStart, $findEnd - $findStart) + $driftSource.Substring($resolveStart, $resolveEnd - $resolveStart) + '}}'
$sdkVersion = (& dotnet --version).Trim()
$compiler = Join-Path $env:ProgramFiles "dotnet\sdk\$sdkVersion\Roslyn\bincore\csc.dll"
$framework = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('BellumRealmTests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temporary) | Out-Null
try {
    $inputFile = Join-Path $temporary 'Tests.cs'
    $assembly = Join-Path $temporary 'Tests.dll'
    [System.IO.File]::WriteAllText($inputFile, $doubles)
    & dotnet $compiler /nologo /target:library "/out:$assembly" "/reference:$(Join-Path $framework 'mscorlib.dll')" "/reference:$(Join-Path $framework 'System.dll')" "/reference:$(Join-Path $framework 'System.Core.dll')" "/reference:$(Join-Path $framework 'System.Xml.dll')" $sources $inputFile
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($assembly)) | Out-Null
    [BellumCivile.RealmTests]::Run($root)
    (New-Object BellumCivile.DriftReceiverTests).Run()
}
finally {
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.cs') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.dll') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporary
}
Write-Output 'Realm selection, name-root, and acquired-county drift regression tests passed.'
