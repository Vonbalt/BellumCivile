$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw -LiteralPath (Join-Path $root $path) }
function Check($ok, $message) { if (!$ok) { throw $message }; "PASS: $message" }
$regency = Read 'Behaviors/RegencyBehavior.cs'
$ready = Read 'Behaviors/RegencyAdulthood.cs'
$equipment = Read 'RegencyAdulthoodEquipment.cs'
$patch = Read 'Patches/RegencyAdulthoodEventPatch.cs'
$birthday = $regency.Substring($regency.IndexOf('private void OnHeroComesOfAge'),
    $regency.IndexOf('private void OnClanDestroyed') - $regency.IndexOf('private void OnHeroComesOfAge'))
Check ($birthday.Contains('_adultWardHandovers.Add(hero)') -and !$birthday.Contains('EndRegency(') -and !$birthday.Contains('ChangeState(')) 'Birthday listener defers leadership until native adulthood listeners finish.'
foreach ($method in @('CompletePlayerRegencyHandover', 'EndRegency')) {
    $start = $regency.IndexOf("private void $method(")
    $body = $regency.Substring($start, $regency.IndexOf('private ', $start + 15) - $start)
    Check ($body.IndexOf('EnsureWardAdulthood(') -ge 0 -and $body.IndexOf('EnsureWardAdulthood(') -lt $body.IndexOf('ChangeState(')) "$method checks adulthood before activation."
}
Check ($regency.Contains('if (!EnsureWardAdulthood(clan, activeRegency, ward)) return false;')) 'Player death succession preparation uses the same adulthood gate.'
Check ($ready.IndexOf('pending.Remove(ward)') -lt $ready.IndexOf('OnHeroComesOfAge(ward)') -and
    $ready.Contains('_heroesYoungerThanHeroComesOfAge') -and !$ready.Contains('ChangeState(')) 'Catch-up consumes native pending birthdays before dispatch without changing hero state.'
Check ($ready.Contains('_adulthoodEventsInProgress.Contains(ward)') -and $ready.Contains('record.AdulthoodFailure') -and
    $ready.Contains('record.IsAdulthoodPrepared(ward)')) 'In-flight, failed and completed native initialization cannot be blindly replayed.'
Check ($patch.Contains('[HarmonyFinalizer]') -and $patch.Contains('private static void Finalizer') -and !$patch.Contains('return false') -and
    !$patch.Contains('ref Exception')) 'Native event receipt preserves original dispatch and exceptions.'
Check ($equipment.Contains('hero.Clan == Clan.PlayerClan') -and $equipment.Contains('HasWeaponOrMount') -and
    $equipment.Contains('SameSlots') -and $equipment.Contains('IsChildEquipmentTemplate') -and $equipment.Contains('IsTeenagerEquipmentTemplate')) 'Legacy recovery requires a matching childhood loadout and excludes player inventory and existing weapons/mounts.'
Check (!$equipment.Contains('SetSkillValue') -and !$equipment.Contains('InitializeHeroDeveloper') -and
    $equipment.Contains('GetEquipmentForHeroComeOfAge')) 'Equipment-only recovery uses the native selection model without rerolling skills.'
Check ($regency.Contains('RecoverLegacyWardEquipment();') -and $regency.Contains('ProcessAdultWardHandovers();')) 'Existing campaign lifecycle wires the post-event queue and session equipment recovery.'
'Source contracts passed; run --regency-adulthood against both game versions.'
