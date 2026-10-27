$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vm = Get-Content -Raw -LiteralPath (Join-Path $root 'UI/Parley/PeaceParleyVM.cs')
$behavior = Get-Content -Raw -LiteralPath (Join-Path $root 'Behaviors/ForeignTreatyBehavior.cs')
[xml]$prefab = Get-Content -Raw -LiteralPath (Join-Path $root 'GUI/Prefabs/Parley/BellumPeaceParley.xml')
[xml]$strings = Get-Content -Raw -LiteralPath (Join-Path $root 'ModuleData/Languages/EN/strings.xml')
function Check($ok, $message) { if (!$ok) { throw $message }; "PASS: $message" }

Check ($vm.Contains('IsDraftEditingDisabled = !ForeignTreatyBehavior.CanPlayerEditDraft(') -and
    $behavior.Contains('if (!CanPlayerEditDraft(proposal, WarPeaceRevampBehavior.GetPlayerPoliticalKingdom(), Clan.PlayerClan, out report))')) 'UI and backend share draft-editing authority.'
$editor = $prefab.SelectSingleNode('//Widget[@Id="DraftEditor"]')
Check ($editor.SuggestedHeight -eq '@DraftEditorHeight' -and
    $editor.SelectNodes('Children/*[@SuggestedHeight="@DraftEditorHeight"]').Count -eq 4) 'All four editors retain their original shared height.'
$overlay = $editor.SelectSingleNode('Children/Widget[@Id="CapitulationDraftHint"]')
Check ($overlay.IsVisible -eq '@IsCapitulationDraftLocked' -and
    $overlay.SelectSingleNode('Children/HintWidget').DataSource -eq '{DraftTabDisabledHint}') 'Editor hover explains capitulation without covering ordinary ruler or vassal controls.'
Check ($editor.SelectNodes('.//*[@Parameter.DoneButtonAction="ExecuteSignTreaty"]').Count -eq 0 -and
    $prefab.SelectSingleNode('//*[@Parameter.DoneButtonAction="ExecuteSignTreaty"]').'Parameter.DoneButtonDisabled' -eq '@IsSignDisabled') 'Read-only overlay does not cover signing.'
foreach ($id in @('BC_Parley_CapitulatedDraftHint', 'BC_Parley_CapitulatedBudgetHint', 'BC_Parley_CapitulatedSignHint', 'BC_Parley_CapitulatedDraftRevised')) {
    $entry = $strings.SelectNodes("//string[@id='$id']")
    Check ($entry.Count -eq 1 -and $vm.Contains("{=$id}")) "Localized capitulation message is present once: $id"
}
Check ($vm.Contains('if (report == ForeignTreatyBehavior.CapitulationDraftRevisedReason)') -and
    $vm.Contains('public bool IsCapitulationDraftLocked')) 'Revised settlements refresh the visible terms and lock tooltip state.'
'Parley capitulation source and XML contracts passed; no campaign UI rendered.'
