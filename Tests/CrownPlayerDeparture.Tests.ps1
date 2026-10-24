$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw -LiteralPath (Join-Path $root $path) }
function Check($ok, $message) { if (!$ok) { throw $message }; "PASS: $message" }
$crown = Read 'Behaviors/CrownAccessionBehavior.cs'
$departure = Read 'Behaviors/CrownPlayerDeparture.cs'
$settle = Read 'Behaviors/CrownAbdication.cs'
$recovery = Read 'Behaviors/CrownHouseholdRecovery.cs'
$mercenary = Read 'Behaviors/DynamicMercenaryBandBehavior.cs'
$at = $settle.IndexOf('internal bool SettleCrownCadet(')
$settle = $settle.Substring($at)
Check ($settle.IndexOf('RetainExistingCrownChildren(record)') -lt $settle.IndexOf('WaitForPlayerCrownFarewell(record)') -and
    $settle.IndexOf('the successor''s household is unavailable') -lt $settle.IndexOf('WaitForPlayerCrownFarewell(record)') -and
    $settle.IndexOf('WaitForPlayerCrownFarewell(record)') -lt $settle.IndexOf('partition?.PrepareAbdicationCadet(record)')) 'Farewell gating follows household preflight and child retention but precedes cadet creation and transfer.'
Check ($crown.Contains('CapturePlayerCrownDeparture(record, allowDialogue: false);') -and
    $departure.Contains('record.Completed || record.PlayerDepartureCaptured') -and
    $departure.Contains('!record.TitleTransferred || record.Cadet == null')) 'Pending legacy accessions can receive a letter without replaying completed historical accessions or announcing unfinished titles.'
Check ($departure.Contains('record.ChallengeConcession || record.RegentReplacement || record.Emergency') -and
    $departure.Contains('source != Clan.PlayerClan') -and $departure.Contains('heir == Hero.MainHero || heir == source.Leader')) 'Only real non-player departures from the player household are captured; unrelated cadets and ruler replacements are excluded.'
Check ($departure.Contains('record.PlayerDepartureDialogueAttempted = true;') -and
    $crown.Contains('r.PlayerDepartureCaptured && r.PlayerDepartureDialogueAttempted') -and
    $crown.Contains('record.PlayerDepartureWantsDialogue = false;')) 'Attempt receipts survive loading and turn interrupted dialogue into a nonblocking letter fallback.'
Check ($departure.Contains('CampaignTime.Now.ToDays - record.PlayerDepartureQueuedAt.ToDays >= 1d') -and
    $departure.Contains('if (_activeCrownDeparture == record) return true;')) 'An unavailable presentation stops gating after one campaign day, but an active conversation cannot lose its participant mid-dialogue.'
Check ($recovery.Contains('ResetPlayerCrownDeparture(record);')) 'Uncommitted household replanning clears stale farewell identities and addresses.'
Check (([regex]::Matches($mercenary, 'CrownAccessionBehavior.Instance\?\.IsPendingCrownHeir\(hero\) == true')).Count -eq 2 -and
    $departure.Contains('mercenaries?.ClearPendingPlayerDeparture(heir);')) 'A pending Crown departure wins over both new and already queued mercenary departures.'
foreach ($guard in @('!map.AtMenu', '!map.MapConversationActive', 'map.NextIncident == null', '!map.IsSimulationActive',
    '!InformationManager.IsAnyInquiryActive()', 'CurrentMenuContext == null', 'IsConversationInProgress != true',
    'IsConversationFlowActive != true', 'CampaignMission.Current == null', 'IsPrisoner != true',
    'PlayerEncounter.Current == null', 'party.MapEvent == null', 'party.SiegeEvent == null')) {
    Check ($departure.Contains($guard)) "Safe campaign presentation guard: $guard"
}
Check ($departure.Contains('"bc_crown_departure_acknowledge", "bc_crown_departure_acknowledge", "close_window"') -and
    $departure.Contains('BuildCrownDepartureLetter(record).ToString(), true, false,') -and
    $departure -notmatch 'GiveGoldAction|ChangeClanInfluenceAction|RelationMemoryService|ObedienceRoll|SetResolution\(') 'Dialogue and letters are acknowledgement-only, with no dissuasion, payment, or relationship effects.'
[xml]$catalog = Read 'ModuleData/Languages/EN/strings.xml'
foreach ($id in @('BC_CrownDeparture_Dialogue', 'BC_CrownDeparture_LetterBody', 'BC_CrownDeparture_LetterMinor',
    'BC_CrownDeparture_LetterHistorical', 'BC_CrownDeparture_Acknowledge', 'BC_CrownDeparture_MarriageWife',
    'BC_CrownDeparture_MarriageHusband', 'BC_CrownDeparture_FamilyUnchanged')) {
    Check (@($catalog.base.strings.string | Where-Object id -eq $id).Count -eq 1) "Unique localized Crown departure text: $id"
}
foreach ($entry in @($catalog.base.strings.string | Where-Object { $_.id -match '^BC_CrownDeparture_(Dialogue|LetterBody|LetterMinor|LetterHistorical)$' })) {
    Check ($entry.text.Contains('household') -and !$entry.text.Contains('ruling house') -and
        $departure.Contains("{=$($entry.id)}$($entry.text)".Replace("`n", '\n').Replace('"', '\"'))) "Household wording matches the localized fallback: $($entry.id)"
}
'Crown departure source contracts passed; live conversation scenes and native save/load still require campaign testing.'
