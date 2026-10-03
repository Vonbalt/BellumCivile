$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS: $message"
}
$foreign = Read 'Behaviors/CrownForeignAccession.cs'
$crown = Read 'Behaviors/CrownAccessionBehavior.cs'
$incoming = Read 'Behaviors/CrownIncomingHouse.cs'
Check ($foreign -match 'ApplyByJoinToKingdom\(house, record.Realm' -and $foreign -notmatch 'ApplyByLeave|DeclareWarAction|ApplyByJoinToKingdomByDefection') 'Foreign accession never invokes confiscation, rebellion or defection actions.'
Check ($foreign -match 'RequiresRealmUnion' -and $foreign -match 'realm union is not implemented yet') 'Existing foreign sovereigns wait for union support instead of losing their first realm.'
Check ($foreign -match 'ForeignMoveStarted = true' -and $foreign.IndexOf('ForeignMoveStarted = true') -lt $foreign.IndexOf('ChangeKingdomAction.ApplyByJoin')) 'Foreign movement is journaled before native membership callbacks.'
Check ($foreign -match 'ForeignInfluenceRestored' -and $foreign -match 'ForeignLegalRightsUnchanged') 'Foreign completion validates legal rights and restores recorded influence once.'
Check ($crown.IndexOf('PrepareForeignCrownClan(record, house)') -lt $crown.IndexOf('ChangeRulingClanAction.Apply(record.Realm, house)')) 'Incoming membership settles before the target Crown changes hands.'
Check ($crown -match 'record.IncomingHousePrepared \|\| record.ForeignMovingClan != null') 'An interrupted foreign transfer cannot silently become an emergency election.'
Check ($incoming -match 'IncomingSourceRealm = source.Kingdom' -and (Read 'Behaviors/PartitionSuccessionBehavior.cs') -match 'cadet.Kingdom = record.HouseholdRealm') 'A non-leading heir receives its cadet estate in the source realm first.'
Check ($incoming -match 't.TitleId != sourcePoliticalCrown' -and $incoming -match 't.TitleType < sourceCrown.TitleType') 'An inheritance advance cannot strip a living source monarch of sovereign Crown titles.'
Check ($foreign -notmatch 'SetParentTitle|SetDeJureHolder|Kingdom.All|ChangeOwnerOfSettlementAction') 'Movement does not rewrite legal hierarchy, confiscate holdings or move subordinate clans.'
Check ((Read 'Behaviors/DynasticHeirBehavior.cs') -match 'BC_DynasticHeiress_RightsRetained') 'Hereditary foreign marriage narration no longer announces automatic renunciation.'
$xml = [xml](Read 'ModuleData/Languages/EN/strings.xml')
Check ($xml.SelectSingleNode("//string[@id='BC_DynasticHeiress_RightsRetained']") -and $xml.SelectSingleNode("//string[@id='BC_AbdicationUnionPending']")) 'New narration and union gate explanation are localized.'
Write-Output 'Source contracts only. Live foreign departure, title callbacks and save/load still require campaign tests.'
