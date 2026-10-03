$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$button = Get-Content -Raw (Join-Path $root 'Patches/HereditaryAbdicationButtonPatch.cs')
$flow = Get-Content -Raw (Join-Path $root 'Behaviors/CrownAbdication.cs')
$journal = Get-Content -Raw (Join-Path $root 'Behaviors/CrownAccessionBehavior.cs')
$strings = [xml](Get-Content -Raw (Join-Path $root 'ModuleData/Languages/EN/strings.xml'))
function Check($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS: $message"
}
Check ($button -match 'if \(!CrownAccessionBehavior.UsesHereditaryPlayerAbdication\(realm\)\) return true') 'The hereditary hook leaves elective routing and vassal leave actions to their own entry points.'
Check ($button -match '!__instance.IsKingdomActionEnabled' -and $button -match 'if \(!__result') 'Native availability and other-mod permission gates are not overridden.'
Check ($button -notmatch '\.AbdicateTheThrone\(' -and $button -notmatch '\.ForceDecideDecision\(') 'Hereditary confirmation does not invoke native abdication or force an unrelated vote.'
Check ($flow -match 'GetLegalClanHead\(Clan.PlayerClan\) != Hero.MainHero') 'A player regent cannot abdicate the ward crown.'
Check ($flow -match 'SameAbdicationPreview\(preview, current\)' -and $flow -match '_accessions.Add\(preview\);\s+Resolve\(preview\)') 'Confirmation revalidates first and journals before mutation.'
Check ($flow -match 'record.EndowmentFiefs.Except\(record.DeliveredFiefs\)' -and $flow -match 'record.EndowmentTitles.Except\(record.DeliveredTitles\)') 'Inheritance delivery uses frozen targets and receipts.'
Check ($flow -notmatch 'ChangePlayerCharacterAction|ApplyHeirSelectionAction') 'Abdication never replaces the controlled character.'
Check ($journal -match '!record.IsAbdication \|\| clan.Leader != record.Predecessor') 'Both emergency abdication paths exclude the outgoing ruler.'
Check ($flow -notmatch '_nextTerms|SessionDate\s*=|DeliberationDate\s*=') 'Abdication does not reschedule court terms or sessions.'
$ids = [regex]::Matches($flow + $journal, '\{=(BC_(?:Abdication\w+|CrownVoluntaryAccession))\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
Check (-not ($ids | Where-Object { -not $strings.SelectSingleNode("//string[@id='$_']") })) 'All new abdication UI/narrative strings are registered.'
Write-Output 'Source/XML contracts only. Live transfer, long-estate confirmation rendering and save/load require campaign testing.'
