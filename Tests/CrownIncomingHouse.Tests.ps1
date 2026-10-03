$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS: $message"
}
$crown = Read 'Behaviors/CrownAccessionBehavior.cs'
$incoming = Read 'Behaviors/CrownIncomingHouse.cs'
$estate = Read 'Behaviors/CrownAbdication.cs'
$cadet = Read 'Behaviors/PartitionSuccessionBehavior.cs'
$gold = Read 'Behaviors/CrownForcedAbdication.cs'
Check ($crown -notmatch 'heir = RegencyBehavior.Instance\?\.GetLegalClanHead\(Clan.PlayerClan\)') 'Playable household heir cannot replace the recorded lawful Crown heir.'
Check ($crown -notmatch 'ChangeClanLeaderAction.ApplyWithSelectedNewLeader') 'Crown accession cannot evict a living household head.'
Check ($crown -match 'PrepareIncomingCrownHouse\(record\)') 'Death and abdication share incoming-house preparation.'
Check ($incoming -match 'if \(record.IncomingHousePrepared\) return SettleCrownCadet\(record\)') 'Partial cadet delivery resumes before checking current leadership.'
Check ($incoming -match 'partition.HasPendingInheritance\(source, record.Predecessor\)') 'Death-house partition finishes before a new Crown split is attempted.'
Check ($incoming -match 'h != Hero.MainHero' -and $incoming -match 'h != source.Leader && h != legalHead') 'Cadet membership excludes the player and both acting and lawful source-house heads.'
Check ($incoming -match '!HasInheritanceAdvance\(source.Leader, heir\)' -and $incoming -match 'GetLivingAccessionShare') 'Only an unpaid lawful living-house share is considered.'
Check ($estate -match 'Clan source = record.EndowmentHouse' -and $cadet -match 'Clan parent = record.EndowmentHouse' -and $gold -match 'Hero donor = record.EndowmentDonor') 'Estate checks, cadet formation and cash use the source household, not the outgoing Crown.'
Check ($incoming.IndexOf('record.IncomingHousePrepared = true') -gt $incoming.IndexOf('record.CadetName =')) 'The frozen incoming plan is fully populated before it is marked prepared.'
Check ($incoming -notmatch 'AddNonSerializedListener|ChangePlayerCharacterAction|ApplyHeirSelectionAction') 'No additional scheduler or player-character replacement was introduced.'
Write-Output 'Source contracts only; live accession, estate transfer and save/load still require campaign testing.'
