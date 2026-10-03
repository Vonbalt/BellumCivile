$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) {
    if (!$condition) { throw $message }
    Write-Output "PASS: $message"
}
$incoming = Read 'Behaviors/CrownIncomingHouse.cs'
$partition = Read 'Behaviors/PartitionSuccessionBehavior.cs'
Check ($incoming -match 'PreviousHouse = record.PreviousHouse, Heir = heir') 'Incoming naming draft retains the royal house separately from the marital household.'
Check ($partition -match 'record.Heir\?\.Spouse\?\.Clan == household' -and $partition -match 'record.PreviousHouse != household') 'Either sex uses combined naming when leaving a distinct marital household.'
Check ($partition -match 'DynasticHeirBehavior.BuildRoyalHeiressCadetName\(record.PreviousHouse, household\)') 'Accession reuses the existing royal-marriage formatter, dynasty first.'
Check ($partition -match 'return BuildCadetClanName\(household, GetCadetNamingFiefIds\(record\)') 'Ordinary cadets use their assigned inheritance fief before the existing household fallback.'
Check ($incoming.IndexOf('record.CadetName = draft.CadetName') -lt $incoming.IndexOf('record.IncomingHousePrepared = true')) 'The selected name is frozen before the plan is published.'
Write-Output 'Source contracts only; in-game naming and saved-plan reload require campaign verification.'
