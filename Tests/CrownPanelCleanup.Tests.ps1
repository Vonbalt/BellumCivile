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
foreach ($binding in @('{ClaimantLeader}', '{HolderLeader}', '{ClaimantMembers}', '{HolderMembers}')) {
    $buttons = $panel.SelectNodes('//*[@DataSource="' + $binding + '"]//ButtonWidget')
    Check ($buttons.Count -eq 1) "Claim-feud portrait binding exists: $binding"
    $button = $buttons[0]
    Check (!$button.HasAttribute('Command.HoverBegin') -and !$button.HasAttribute('Command.HoverEnd') -and !$button.HasAttribute('HoveredCursorState') -and
        $button.SelectNodes('.//HintWidget').Count -eq 0) "Claim-feud portraits have no residual tooltip or special hover cursor: $binding"
    Check ($button.GetAttribute('Command.Click') -eq 'ExecuteOpenClan') "Claim-feud portraits retain encyclopedia navigation: $binding"
}
Check ($panel.SelectNodes('//*[@*[name()="Command.HoverBegin"]="ExecuteBeginScoreHint"]').Count -eq 2) 'Only rebel leader and member portraits request rebellious intent tooltips.'
$feud = Get-Content -Raw (Join-Path $root 'UI/ClaimFeudItemVM.cs')
Check (([regex]::Matches($feud, 'new FactionMemberVM\([^,\r\n]+, false\)')).Count -eq 4) 'Both private-feud leaders and both supporter lists disable rebellious intent data.'
Check ($panel.SelectNodes('//*[@DataSource="{..\FactionPowerHint}"]').Count -gt 0) 'Individual faction power tooltip is unchanged.'
'XML/source contracts only; native hover rendering needs an in-game check.'
