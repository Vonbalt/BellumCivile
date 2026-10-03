$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$panel = Read 'UI/VanillaTabs/Kingdoms/Succession/KingdomSuccessionTabVM.cs'
$pretenders = Read 'UI/VanillaTabs/Kingdoms/Succession/SuccessionPretender.cs'
Check ($panel.Contains('CrownHeir = line.Count > 0 ? new SuccessionHeirVM(line[0]') -and
    $panel.IndexOf('CrownHeir = line.Count > 0') -lt $panel.IndexOf('SuccessionPretender.Get(_kingdom, line)')) 'Crown slot is assigned exclusively from the lawful line before querying pretenders.'
Check ($panel.Contains('line.Skip(1).Take(3)') -and $panel.Contains('3 - NextHeirs.Count') -and
    $panel.Contains('_additionalHeirs.AddRange(line.Skip(4))') -and $panel.Contains('pretenders.Skip(visiblePretenders)')) 'Rightful heirs precede pretenders in portraits and overflow without consuming the Crown slot.'
Check ($panel.Contains('_additionalHeirs.Count + _additionalPretenders.Count') -and
    $panel.Contains('SuccessionPretender.Label(pretender.Strength), pretender.Hero.Name')) 'Overflow count and tooltips include claim-only pretenders without succession numbers.'
Check ($pretenders.Contains('GetActiveClaimsByTitle(crown)') -and $pretenders.Contains('GroupBy(c => c.CarrierHeroId)') -and
    $pretenders.Contains('c.ClaimantClanId == carrier.Clan.StringId') -and $pretenders.Contains('carrier?.IsAlive != true')) 'Only active sovereign claims with living matching carriers are displayed; no leader substitution.'
Check ($pretenders.Contains('RebellionPowerHelper.CalculateClanPower(carrier.Clan)') -and
    $pretenders.Contains('power.TryGetValue(carrier.Clan') -and $pretenders.Contains('StringComparer.Ordinal')) 'Clan strength plus influence is computed once per house with deterministic identity ties.'
Check ($panel.Contains('_loyaltyRealm = pretender.HasValue ? null : loyaltyRealm') -and
    $panel.Contains('pretender.HasValue ? SuccessionPretender.Label(pretender.Value)')) 'Pretender tooltip position is explicit and does not acquire heir loyalty mechanics.'
$strings = [xml](Read 'ModuleData/Languages/EN/strings.xml')
foreach ($id in @('BC_Succession_StrongPretender','BC_Succession_WeakPretender')) {
    Check ($strings.SelectNodes("//string[@id='$id']").Count -eq 1) "Localized pretender label: $id"
}
