$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$panel = Get-Content -Raw (Join-Path $root 'GUI/Prefabs/KingdomManagement/Factions/BellumFactionsPanel.xml')
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$vm = Get-Content -Raw (Join-Path $root 'UI/CrownPanelVM.cs')
Check (!$vm.Contains('CrownPowerProjection.Calculate') -and !$vm.Contains('PowerHint')) 'Crown no longer computes unused power projections or hints.'
Check ($panel.SelectNodes('//*[@Text="@BalanceText" or @Text="@RulingPowerText" or @DataSource="{PowerHint}"]').Count -eq 0) 'Crown power section and its hover target are removed.'
$plain = $panel.SelectNodes('//ButtonWidget[@IsVisible="@ShowPlainPortrait"]')
$rebel = $panel.SelectNodes('//ButtonWidget[@IsVisible="@ShowRebelliousIntent"]')
Check ($plain.Count -eq 2 -and $rebel.Count -eq 2) 'Leader and member portraits each have separate court/rebel button bindings.'
foreach ($button in $plain) {
    Check (!$button.HasAttribute('Command.HoverBegin') -and !$button.HasAttribute('Command.HoverEnd') -and !$button.HasAttribute('HoveredCursorState')) 'Court portraits have no residual tooltip or special hover cursor.'
    Check ($button.GetAttribute('Command.Click') -eq 'ExecuteOpenClan') 'Court portraits retain encyclopedia navigation.'
}
foreach ($button in $rebel) {
    Check ($button.GetAttribute('Command.HoverBegin') -eq 'ExecuteBeginScoreHint' -and $button.GetAttribute('Command.HoverEnd') -eq 'ExecuteEndScoreHint') 'Rebel portraits retain rebellious intent hover handlers.'
}
Check ($panel.SelectNodes('//*[@DataSource="{..\FactionPowerHint}"]').Count -gt 0) 'Individual faction power tooltip is unchanged.'
'XML/source contracts only; native hover rendering needs an in-game check.'
