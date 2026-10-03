$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$dispatch = Read 'Behaviors/SuccessionChallengeDispatch.cs'
$behavior = Read 'Behaviors/SuccessionChallengeBehavior.cs'
$partition = Read 'Behaviors/PartitionSuccessionBehavior.cs'
$record = Read 'SuccessionChallengeRecord.cs'
Check ($dispatch.Contains('record.Challenger.Clan') -and $dispatch.Contains('receivingHouse?.Leader != record.Challenger') -and
    $dispatch -notmatch 'BeginForcedAbdication|RecognizeLawfulCrownAccession') 'Crown surrender installs the actual challenger using contested accession semantics.'
Check ($dispatch.Contains('CrownEstateAvailable(record.CrownEstate)') -and
    $dispatch.IndexOf('record.CrownTransferStarted = true') -lt $dispatch.IndexOf('SettleCrownCadet(record.CrownEstate)')) 'Saved Crown stage precedes household mutations; estate preflight runs first.'
Check ($dispatch.Contains('crown.DeJureHolderClanId != receivingHouse.StringId') -and
    $dispatch.Contains('crown.DeFactoHolderClanId != receivingHouse.StringId') -and
    $dispatch.IndexOf('record.CrownTitleTransferred = true') -lt $dispatch.IndexOf('record.Phase = SuccessionChallengePhase.Settled')) 'Actual Crown title postconditions gate completion.'
Check ($behavior.Contains('PreviewConcession(record, lawfulOnly: true)') -and
    $behavior.Contains('bool fallback = !lawfulOnly') -and $behavior.Contains('if (!lawfulOnly && !SuccessionChallengeRules.CanConcede')) 'Lawful Crown share is separate from gift fallback and negotiated last-fief protection.'
Check ($record.Contains('HasAdvance => DeliveredAdvance(CrownEstate)') -and
    $record.Contains('HasHouseholdGrant => DeliveredAdvance(Estate) || DeliveredAdvance(WarEstate)')) 'Household grants retain a distinct receipt without consuming death inheritance.'
Check ($partition.Contains('party != MobileParty.MainParty && party.LeaderHero == heir') -and
    $partition.Contains('party.ActualClan == parent') -and $partition.Contains('record.HeirPartyCaptured = true')) 'Only an eligible own lord party is captured once, never the player or another leader party.'
$retained = $partition.Substring($partition.IndexOf('if (party == retainedParty)'))
$retained = $retained.Substring(0, $retained.IndexOf('else'))
Check ($retained.Contains('party.ActualClan = cadetClan') -and $retained -notmatch 'RemoveTroop|MakeHeroFugitive|StartDisband|AddTroop') 'Retained party switches engine clan bookkeeping without replacing its roster.'
Check ($dispatch.Contains('PledgeParticipantsUnchanged(record)') -and $dispatch.Contains('record.LoyalistPower = loyal - transferred') -and
    $dispatch -notmatch 'MBRandom|RallyAiSupporters|ShowCivilWarSolidarityChoice') 'Fresh response forces preserve saved allegiance and debit credited heir power from loyalists.'
Check ($dispatch.Contains('HasCoalitionStronghold(record)') -and $dispatch.Contains('CampaignTime.Years(2).ToDays')) 'A coalition stronghold is required; insufficient backing uses the two-year pause.'
Check (($behavior + $dispatch) -notmatch 'new FactionObject|DeclareWarAction|CreateKingdom\(' -and
    $dispatch -notmatch 'RewardRecorded = true|RefreshSuccessionConcession') 'Crown surrender stays separate from war creation and grants no land-demand gratitude.'
[xml]$strings = Read 'ModuleData/Languages/EN/strings.xml'
Check (@($strings.base.strings.string | Where-Object id -eq 'BC_Challenge_CrownSurrender').Count -eq 1) 'Crown surrender notification has one localized definition.'
'Source contracts only; party transfer, Crown callbacks and save/load require campaign verification.'
