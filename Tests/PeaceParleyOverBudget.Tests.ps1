$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vm = Get-Content -Raw -LiteralPath (Join-Path $root 'UI/Parley/PeaceParleyVM.cs')
[xml]$prefab = Get-Content -Raw -LiteralPath (Join-Path $root 'GUI/Prefabs/Parley/BellumPeaceParley.xml')
function Check($ok, $message) { if (!$ok) { throw $message }; "PASS: $message" }

$header = $prefab.SelectSingleNode('//TextWidget[@Text="@BudgetText"]')
Check ($header.HeightSizePolicy -eq 'Fixed' -and $header.SuggestedHeight -eq '30' -and
    $header.'Brush.FontColor' -eq '@BudgetColor') 'Budget header retains its fixed height and binds its warning color.'
Check ($vm.Contains('Drafted Terms ({USED} / {BUDGET} WS; +{EXCESS})') -and
    $vm.Contains('.SetTextVariable("EXCESS", proposal?.BudgetOverrun ?? 0)')) 'Compact header shows the shared accounting overrun.'
Check ($vm.Contains('BudgetColor = isOverBudget ? BuildOutcomeColor(false)')) 'Over-budget header uses the rejection red.'
Check ($vm.Contains('IsSignDisabled = !hasValidPlayerResponse || (isOverBudget && IsPlayerRuler && !IsDraftEditingDisabled);') -and
    $vm.Contains('{=BC_Parley_VassalInvalidBudgetHint}')) 'Vassals retain an escape from invalid drafts and capitulated rulers can request NPC repair; editable ruler signing remains budget-blocked.'
$budgetMessages = [regex]::Matches($vm, '\{=BC_Parley_FailureWarScoreExceeded\}[^"\r\n]+')
Check ($budgetMessages.Count -eq 2 -and $budgetMessages[0].Value -eq $budgetMessages[1].Value) 'Disabled-sign hint and backend failure report use identical localized text.'
Check ($vm -match 'public void ExecuteSignTreaty\(\)\s*\{\s*if \(_proposal\?\.IsOverBudget == true && IsPlayerRuler && !IsDraftEditingDisabled\)\s*\{\s*Refresh\(_proposal\);\s*return;') 'Direct editable ruler signing stays blocked; vassals and capitulated rulers reach backend recovery.'
Check ($vm.Contains('bool playerForced = proposal.IsForced && !isOverBudget;') -and
    $vm.Contains('bool opponentForced = proposal.IsForced && !isOverBudget;') -and
    $vm.Contains('bool treatyWillPass = !isOverBudget && playerAccepts && opponentAccepts;')) 'Forced drafts cannot predict acceptance while over budget.'
$summary = Get-Content -Raw -LiteralPath (Join-Path $root 'UI/Parley/TreatyCouncilSummaryVM.cs')
Check ($summary.IndexOf('if (council.IsBudgetBlocked)') -lt $summary.IndexOf('if (forced)')) 'Budget-blocked council status takes precedence over capitulation.'
$rulerReason = $summary.Substring($summary.IndexOf('private static string BuildRulerReason('))
Check ($rulerReason -match 'if \(council.IsBudgetBlocked\)\s+return new TextObject\("\{=BC_Parley_BudgetBlocksDecision\}' -and
    $rulerReason.IndexOf('if (council.IsBudgetBlocked)') -lt $rulerReason.IndexOf('if (forced)') -and
    $rulerReason.IndexOf('if (council.IsBudgetBlocked)') -lt $rulerReason.IndexOf('if (usesOverride)') -and
    $summary.Contains('bool actualOverride = council != null && !council.IsBudgetBlocked && !forced')) 'Budget rejection gives its actual reason without inventing an override or commitment.'
Check ($vm.Contains('Math.Max(currentCost, GetMaximumWealthCost(existing, fromKingdom, toKingdom))') -and
    $vm.Contains('nextCost = Math.Max(0, currentCost + (increase ? step : -step));')) 'Ctrl+increase never reduces oversized wealth and ordinary increments remain uncapped.'
Check ($vm.Contains('return Math.Max(0, _proposal.WarScoreBudget - usedWithoutThisTerm);') -and
    $vm.Contains('return Math.Max(existing?.WarScoreCost ?? 0, (int)BellumCivileConstants.WarScoreForcePeaceThreshold);')) 'Within-budget maximum shortcut and existing offering shortcut remain intact.'
$behavior = Get-Content -Raw -LiteralPath (Join-Path $root 'Behaviors/ForeignTreatyBehavior.cs')
function MethodBody($signature) {
    $start = $behavior.IndexOf($signature)
    if ($start -lt 0) { throw "Missing method: $signature" }
    $open = $behavior.IndexOf('{', $start)
    $depth = 1
    for ($i = $open + 1; $i -lt $behavior.Length; $i++) {
        if ($behavior[$i] -eq '{') { $depth++ }
        if ($behavior[$i] -eq '}') { $depth-- }
        if ($depth -eq 0) { return $behavior.Substring($open, $i - $open + 1) }
    }
    throw "Unterminated method: $signature"
}
$sign = MethodBody 'bool TrySignPlayerParley('
Check ($sign.IndexOf('HasSettlementBudget(proposal, out report)') -ge 0 -and
    $sign.IndexOf('HasSettlementBudget(proposal, out report)') -lt $sign.IndexOf('SpendCouncil')) 'Player signing checks budget before council influence is spent.'
Check ($sign.Contains('bool submittedOverBudget = proposal.IsOverBudget;') -and
    $sign -match 'if \(!HasSettlementBudget\(proposal, out report\) \|\| \(submittedOverBudget && !playerIsRuler\)\)\s*\{\s*if \(playerIsRuler\) return false;\s*CancelInvalidProposal\(war, proposal, report\);\s*return true;') 'Budget failure preserves a ruler draft but closes a vassal parley without political refusal, even if prices change during submission.'
Check (([regex]::Matches($sign, 'rebuildNpcDraft: false')).Count -eq 2 -and
    $sign.Contains('report = CapitulationDraftRevisedReason;')) 'Signing preserves normal drafts and requires review of revised capitulation terms.'
$automatic = MethodBody 'void ResolvePendingParley('
Check ($automatic.IndexOf('HasSettlementBudget(proposal, out string budgetReport)') -ge 0 -and
    $automatic.IndexOf('HasSettlementBudget(proposal, out string budgetReport)') -lt $automatic.IndexOf('SpendCouncil')) 'Automatic settlement checks budget before council spending.'
Check ($automatic -match 'if \(!HasSettlementBudget\(proposal, out string budgetReport\)\)\s*\{\s*CancelInvalidProposal\(war, proposal, budgetReport\);\s*return;') 'Invalid automatic budget cancels without treating it as political refusal.'
foreach ($method in @('bool ApplyTreaty(', 'bool ApplyTreatyCore(')) {
    $body = MethodBody $method
    Check ($body -match '^\{\s*if \(!HasSettlementBudget\(proposal, out _\)\)\s+return false;') "Budget is checked before any settlement side effects: $method"
}
'Parley over-budget UI and settlement source contracts passed; no build or campaign runtime tests executed.'
