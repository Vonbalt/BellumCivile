$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'KingdomCreationSafetyHelper.cs') -Raw

# Compile the production helper against minimal campaign doubles, without loading the game.
$doubles = @'
namespace TaleWorlds.CampaignSystem
{
    public class Hero { public bool IsDead; }
    public class Clan { public bool IsEliminated; public Hero Leader; public string StringId; }
    public class Kingdom
    {
        public bool IsEliminated;
        public string StringId;
        public Clan RulingClan;
        public static int Created;
        public static Kingdom CreateKingdom(string id) { Created++; return new Kingdom { StringId = id }; }
    }
}
namespace BellumCivile
{
    public static class BellumCivileLogger { public static void Log(string message) { } }
    public static class KingdomCreationSafetyTests
    {
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new System.Exception(message);
        }
        public static void Run()
        {
            var founder = new TaleWorlds.CampaignSystem.Clan {
                StringId = "founder", Leader = new TaleWorlds.CampaignSystem.Hero() };
            var kingdom = KingdomCreationSafetyHelper.CreateKingdom("rebels", founder);
            Check(kingdom.RulingClan == founder, "Founder must be assigned before initialization/transfer.");
            var invalid = new[] { null, new TaleWorlds.CampaignSystem.Clan(),
                new TaleWorlds.CampaignSystem.Clan { IsEliminated = true, Leader = founder.Leader },
                new TaleWorlds.CampaignSystem.Clan { Leader = new TaleWorlds.CampaignSystem.Hero { IsDead = true } } };
            foreach (var clan in invalid)
            {
                Check(KingdomCreationSafetyHelper.CreateKingdom("invalid", clan) == null, "Invalid founder accepted.");
                Check(!KingdomCreationSafetyHelper.PrepareRuler(kingdom, clan, "test"), "Invalid recovery accepted.");
                Check(kingdom.RulingClan == founder, "Failed recovery changed the ruler.");
            }
            Check(TaleWorlds.CampaignSystem.Kingdom.Created == 1, "Invalid creation left a shell behind.");
            var partial = new TaleWorlds.CampaignSystem.Kingdom();
            Check(KingdomCreationSafetyHelper.PrepareRuler(partial, founder, "recovery"), "Partial recovery failed.");
            Check(partial.RulingClan == founder, "Partial recovery did not seed intended ruler.");
            partial.IsEliminated = true;
            Check(!KingdomCreationSafetyHelper.PrepareRuler(partial, founder, "eliminated"), "Eliminated realm accepted.");
            Check(!KingdomCreationSafetyHelper.PrepareRuler(null, founder, "missing"), "Null realm accepted.");
        }
    }
}
'@
$sdkVersion = (& dotnet --version).Trim()
$compiler = Join-Path $env:ProgramFiles "dotnet\sdk\$sdkVersion\Roslyn\bincore\csc.dll"
$framework = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('BellumKingdomTests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temporary) | Out-Null
try {
    $inputFile = Join-Path $temporary 'Tests.cs'
    $assembly = Join-Path $temporary 'Tests.dll'
    [System.IO.File]::WriteAllText($inputFile, $source + [Environment]::NewLine + $doubles)
    & dotnet $compiler /nologo /target:library "/out:$assembly" "/reference:$(Join-Path $framework 'mscorlib.dll')" $inputFile
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($assembly)) | Out-Null
}
finally {
    # Only remove artifacts in this test run's explicitly created directory.
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.cs') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.dll') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporary
}
[BellumCivile.KingdomCreationSafetyTests]::Run()

$directCalls = Get-ChildItem $root -Recurse -Filter '*.cs' |
    Where-Object { $_.Name -ne 'KingdomCreationSafetyHelper.cs' } |
    Select-String -Pattern '(?<!\w)Kingdom\.CreateKingdom\('
if ($directCalls) { throw "Kingdom creation bypasses safety helper: $directCalls" }
Write-Output 'Kingdom creation safety tests passed.'
