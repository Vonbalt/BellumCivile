$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS: $message"
}
foreach ($file in @('CrownIncomingHouse', 'CrownAbdication', 'CrownForcedAbdication')) {
    $source = Read "Behaviors/$file.cs"
    Check ($source -notmatch 'Concat\([^)]*Children\)') "$file does not automatically relocate existing children."
}
$patch = Read 'Patches/SeparateSpouseHouseholdPatch.cs'
Check ((Read 'Behaviors/PartitionSuccessionBehavior.cs') -match 'IsPendingCrownHeir\(heir\) != true') 'Preparatory partition cadets also retain children for a pending Crown heir.'
Check ($patch -match 'HeroCreator.DeliverOffSpring' -and $patch -notmatch 'OnHeroCreated|OnGivenBirth') 'Newborn clan is selected before native initialization, not repaired after birth.'
Check ($patch -notmatch 'Hero.MainHero|IsFemale') 'Production visit and birth household rules do not privilege player sex.'
Check ($patch -match 'hero.IsPrisoner \|\| spouse.IsPrisoner' -and $patch -match 'IsAtWarWith') 'Spouse visits inspect both captivity and faction hostility.'
Check ($patch -match 'party.AttachedTo' -and $patch -match 'party\?\.CurrentSettlement') 'Native attached-party and settlement location conventions remain.'
$alliance = Read 'Behaviors/MarriageAllianceBehavior.cs'
Check ($alliance -match 'OnHeroChangedClanEvent.AddNonSerializedListener' -and $alliance -match 'if \(hero\?\.Spouse != null\) MarkAllianceCacheDirty') 'Moving a married hero invalidates the cached marriage alliances.'
$line = Read 'HereditaryRealmSuccession.cs'
Check ($line -match 'CanConsiderClan' -and (Read 'Behaviors/CrownForeignAccession.cs') -match 'RequiresRealmUnion') 'Foreign heir eligibility uses the accession path while realm unions remain gated.'
Write-Output 'Source contracts only. Campaign childbirth, clan callbacks, menus and save/load still need live tests.'
