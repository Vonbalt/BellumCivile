$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$service = Read 'Behaviors/ConflictOutcomeBehavior.cs'
$resolution = Read 'Behaviors/CivilWarResolutionBehavior.cs'
$collapse = Read 'Behaviors/CivilWarCollapseContinuation.cs'
$rival = Read 'Behaviors/CivilWarRivalSettlement.cs'
$feud = Read 'Behaviors/ClaimFeudWarBehavior.cs'
Check ($service.Contains('store.SyncData("BC_ConflictOutcomeNotices"') -and $service.Contains('if (store.IsLoading) Rebuild()')) 'Pending notices and acknowledgement receipts are saved and rebuilt.'
Check ($service.Contains('notice == null || notice.Ready') -and $service.Contains('notice.Ready && notice.PlayerInvolved && !notice.Acknowledged')) 'Publication and load both suppress duplicates.'
Check ($service.Contains('ActiveState is MapState') -and $service.Contains('InformationManager.IsAnyInquiryActive()') -and $service.Contains('Hero.OneToOneConversationHero != null')) 'Popup delivery waits for safe map UI.'
Check ($resolution.Contains('ConflictOutcomeBehavior.Current?.HasPending == true') -and $resolution.Contains('_collapses.Any(r => !r.Completed)')) 'Tribunals wait for settlement completion and result acknowledgement.'
Check ($collapse.IndexOf('BeginCivil(record.Winner') -lt $collapse.IndexOf('TransferClansToKingdom(record.WinnerRealm') -and $collapse.IndexOf('Publish(resultNotice') -gt $collapse.IndexOf('InheritExternalWars(record.Successor')) 'Collapse captures involvement before movement and reports after restoration.'
Check ($rival.IndexOf('BeginCivil(loser.Faction') -lt $rival.IndexOf('ReleaseMercenariesFromKingdom') -and $rival.IndexOf('Publish(resultNotice') -gt $rival.IndexOf('DrainAndDestroyRebelKingdom')) 'Rival absorption records both coalitions and reports after cleanup.'
Check ($feud.Contains('originalParent?.RulingClan == Clan.PlayerClan') -and $feud.Contains('DecodeIds(war.ClaimantClanIds).Contains(Clan.PlayerClan.StringId)')) 'Feud popup includes participating clans and the parent ruler, not every parent vassal.'
Check ($feud.IndexOf('Publish(resultNotice') -gt $feud.IndexOf('ReturnWarClans(war, parent)') -and !$feud.Contains('ShowClaimFeudWarResolved(')) 'Feud result replaces pre-transfer duplicate chat.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$files = @('ConflictOutcomeText.cs', 'Behaviors/ConflictOutcomeBehavior.cs', 'Behaviors/CivilWarResolutionBehavior.cs', 'Behaviors/ClaimFeudWarBehavior.cs', 'Behaviors/WarScoreBehavior.cs')
foreach ($file in $files) {
    foreach ($match in [regex]::Matches((Read $file), '\{=(BC_Result_[A-Za-z0-9_]+)\}([^"\r\n]*)')) {
        $node = $xml.SelectNodes("//string[@id='$($match.Groups[1].Value)']")
        Check ($node.Count -eq 1 -and $node[0].text -eq $match.Groups[2].Value) "Localized result matches fallback: $($match.Groups[1].Value)"
    }
}
Check ((Read 'BellumCivileSaveDefiner.cs').Contains('AddClassDefinition(typeof(ConflictOutcomeNotice), 89)')) 'Notice save class has its own registered type ID.'
'Native inquiry rendering and campaign save round trips still require in-game testing.'
