$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw -LiteralPath (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$settings = Read 'BellumCivileSettings.cs'
$rules = Read 'HostagePactRules.cs'
$terms = Read 'TreatyHostageTerms.cs'
$ai = Read 'TreatyAiDraftService.cs'
Check ((Read 'PeaceReadiness.cs').Contains('-Math.Max(0f, period - days) * resolve') -and
    (Read 'BellumCivileConstants.cs').Contains('WarPeaceDurationReluctanceDays = 50;')) 'One configurable value controls reluctance strength and duration, defaulting to fifty.'
Check ($settings.Contains('HostagePactMinimumDurationDays') -and
    (Read 'BellumCivileOptions.cs').Contains('Settings?.HostagePactMinimumDurationDays')) 'New hostage negotiations use a live, bounded MCM minimum/default.'
Check ((Read 'TreatyTermRecord.cs').Contains('[SaveableField(19)] private int _hostageDurationBaselineDays;') -and
    (Read 'HostagePactRecord.cs').Contains('[SaveableField(21)] public int DurationBaselineDays;')) 'Baseline snapshots append save fields without changing existing IDs.'
Check ($rules.Contains('LegacyPricingBaselineDays = 50') -and
    $terms.Contains('pact?.HostageDurationBaselineDays ?? BellumCivileOptions.HostagePactMinimumDurationDays')) 'Legacy pricing stays fixed and existing negotiations do not reread the MCM baseline.'
Check ((Read 'UI/Parley/PeaceParleyHostages.cs').Contains('TreatyHostageTerms.MinimumForDraft(_proposal.Terms)') -and
    (Read 'UI/Parley/PeaceParleyVM.cs').Contains('term.HostageDurationBaselineDays')) 'Player controls and cloning preserve the chosen baseline.'
Check ($ai.Contains('existingPact?.HostageDurationBaselineDays') -and
    $ai.Contains('GetAiDurations(enthusiasm, warDays, baseline)') -and
    $ai.Contains('BaselineForDraft(draft.Terms)')) 'NPC initial drafts, counteroffers and shorter alternatives respect their saved baseline.'
Check ((Read 'Behaviors/HostagePactTreatyDelivery.cs').Contains('to.RulingClan, term.HostageDurationBaselineDays') -and
    (Read 'HostagePactRecord.cs').Contains('record.DurationPriced, record.DurationBaselineDays')) 'Custody staging and interrupted activation validate the recorded baseline.'
Check ((Read 'TreatyRatificationService.cs').Contains('GetDurationPenalty(negotiatedPact.DurationDays, negotiatedPact.HostageDurationBaselineDays)') -and
    $rules.Contains('days - PricingBaseline(baselineDays) - 150')) 'Council duration utility is relative to the saved baseline, not live settings.'
[xml]$catalog = Read 'ModuleData/Languages/EN/strings.xml'
foreach ($id in @('BC_MCM_WarDurationReluctance_Hint', 'BC_MCM_HostageMinimumDuration_Hint')) {
    $entry = @($catalog.base.strings.string | Where-Object id -eq $id)
    Check ($entry.Count -eq 1 -and $settings.Contains(('{=' + $id + '}' + $entry[0].text)) -and
        $entry[0].text.Contains('Increasing') -and $entry[0].text.Contains('decreasing') -and
        $entry[0].text.Contains('Bellum default: 50')) "MCM hint explains defaults and both directions: $id"
}
'Source contracts passed; run the hostage, treaty-balance, readiness and internal-peace compiled suites and verify the menu in-game.'
