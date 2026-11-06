$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw -LiteralPath (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$dialogue = Read 'Behaviors/ExileAsylumDialogues.cs'
$visits = Read 'Behaviors/ExileAsylumVisits.cs'
$recovery = Read 'Behaviors/ExiledClanRecoveryBehavior.cs'
$record = Read 'AsylumVisitRecord.cs'
$definer = Read 'BellumCivileSaveDefiner.cs'
[xml]$catalog = Read 'ModuleData/Languages/EN/strings.xml'
$ids = @($catalog.base.strings.string | ForEach-Object { $_.id })
Check (@($ids | Group-Object | Where-Object Count -gt 1).Count -eq 0) 'English localization IDs remain unique.'
foreach ($source in @($dialogue, $visits, $recovery)) {
    foreach ($match in [regex]::Matches($source, '\{=(BC_Asylum_[A-Za-z0-9_]+)\}')) {
        Check ($ids -contains $match.Groups[1].Value) "Localized audience text exists: $($match.Groups[1].Value)"
    }
}
$saveIds = @([regex]::Matches($record, 'SaveableField\((\d+)\)') | ForEach-Object { $_.Groups[1].Value })
Check ($saveIds.Count -eq 15 -and @($saveIds | Select-Object -Unique).Count -eq 15) 'Visit state and decision receipts have distinct save fields.'
Check ($definer.Contains('AddClassDefinition(typeof(AsylumVisitRecord), 142)') -and
    $definer.Contains('ConstructContainerDefinition(typeof(Dictionary<string, AsylumVisitRecord>))')) 'Visit records and their dictionary are registered for native saves.'
Check ($recovery.Contains('RegisterAsylumDialogues') -and $recovery.Contains('ProcessAsylumVisits') -and
    $recovery.Contains('OnAsylumTick')) 'Audience dialogue, timed expiry and safe deferred cleanup are registered.'
Check (!$recovery.Contains('AcceptPlayerAsylum(') -and $recovery.Contains('BC_Asylum_Understood')) 'The old remote-acceptance popup is replaced by an acknowledgement notice.'
Check ($visits.Contains('internal const int AsylumVisitDays = 7') -and
    $visits.Contains('if (!CanPresentAsylumNotice()) break;')) 'The seven-day window starts only when arrival can be announced.'
Check ($visits.Contains('until.ToHours == visit.PartyHoldUntil.ToHours') -and
    $visits.Contains('DisableForHours(AsylumVisitDays * 24)')) 'Party waiting is time-limited and cleanup only releases its own timer.'
Check ($dialogue.Contains('CompleteAsylumOath);') -and $dialogue.Contains('AsylumEstateStillAvailable()') -and
    $dialogue.Contains('ListAsylumEstates')) 'Admission occurs at the final oath, with a selected and revalidated estate.'
Check ($dialogue.Contains('bc_asylum_postpone') -and $dialogue.Contains('bc_asylum_oath_changed') -and
    $dialogue.Contains('bc_asylum_estate_stale') -and $dialogue.Contains('token + "_stale"')) 'Dialogue includes postponement and stale-state exits.'
Check ($dialogue.Contains('starter.AddPlayerLine(token + "_stale"') -and
    $dialogue.Contains('starter.AddDialogLine(token + "_stale"')) 'Stale petitions have exit lines on both player and NPC turns.'
Check ($dialogue.Contains('I say I was unjustly accused.') -and
    $dialogue.Contains('With the fall of {REALM}')) 'Explanations distinguish alleged unjust treason from realm destruction.'
Check ($recovery.Contains('A messenger brings you a letter') -and $recovery.Contains('BuildAsylumLetterContext(clanId)') -and
    !$recovery.Contains('BuildExileCauseContext') -and !$recovery.Contains('BuildCourtFactionContext')) 'The letter leaves detailed exile explanations to the audience.'
Check ($dialogue.Contains('"bc_asylum_accept_estate", "bc_asylum_conclusion"') -and
    $dialogue.Contains('CanConcludeAsylum, CompleteAsylumEstate') -and $dialogue.Contains('AcknowledgeAsylumOath')) 'The oath establishes membership before the concluding estate offer and award.'
foreach ($number in 1..4) {
    Check ($dialogue.Contains("{BC_ASYLUM_OATH_$number}")) "The shared words of oath line $number are bound for both speakers."
}
$testing = Read 'Behaviors/ExileAsylumTesting.cs'
Check ((Read 'CheatCommands.cs').Contains('CommandLineArgumentFunction("test_asylum", "civilwars")') -and
    $testing.Contains('QueuePlayerAsylumRequest(clan, origin, new[] { origin }, realm)')) 'The test console command forces the destination through the production asylum queue.'
Check ($testing.Contains('RecordExileContext(clan, origin, ExileCause.Expulsion)') -and
    $testing.Contains('RecordDeparture(clan, origin, ExileCause.Expulsion)') -and
    $testing.Contains('ConfiscateNonBaronyTitlesForExile') -and !$testing.Contains('ApplyByJoinToKingdom')) 'Testing preserves real exile handling without prematurely recruiting the clan.'
Check ($testing.Contains('No eligible foreign landless clan') -and $testing.Contains('deadline will not be reset') -and
    $testing.Contains('reload your test save')) 'The command explains eligibility, blocks repeated visits and warns about partial campaign changes.'
'Source contracts passed; run --asylum-visits, --exile-recovery and --campaign-ui-startup, then test the keep conversation in-game.'
