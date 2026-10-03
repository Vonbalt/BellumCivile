$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$presentation = Read 'CourtAgendaPresentation.cs'
$behavior = Read 'Behaviors/CourtAgendaBehavior.cs'
Check ($behavior.Contains('CourtAgendaPresentation.Deadline(agenda)') -and $behavior.Contains('BC_CurrentAgendaCompactDated')) 'Compact agendas include the shared saved deadline.'
Check ($presentation.Contains('agenda.AllocationStage ? agenda.AllocationVoteDate : agenda.VoteDate')) 'Allocation-stage dates are not confused with the initial vote.'
Check (!$presentation.Contains('CampaignTime.Now') -and !$presentation.Contains('BellumCivileOptions')) 'Presentation never recalculates deadlines from current time or settings.'
[xml]$prefab = Read 'GUI/Prefabs/KingdomManagement/Factions/BellumFactionsPanel.xml'
foreach ($binding in @('@Agenda', '@RivalsText')) {
    $widget = $prefab.SelectSingleNode("//TextWidget[@Text='$binding']")
    Check ($widget.GetAttribute('HeightSizePolicy') -eq 'CoverChildren' -and $widget.GetAttribute('Brush.TextHorizontalAlignment') -eq 'Center') "Centered, content-height agenda: $binding"
}
[xml]$strings = Read 'ModuleData/Languages/EN/strings.xml'
foreach ($id in @('BC_CurrentAgendaCompactDated', 'BC_AgendaDeadlineNomination', 'BC_AgendaDeadlineCrisis', 'BC_AgendaDeadlineSession', 'BC_AgendaDeadlineJudgment', 'BC_AgendaDeadlineVote')) {
    Check (@($strings.base.strings.string | Where-Object id -eq $id).Count -eq 1) "Unique date localization: $id"
}
'Source/XML contracts only; campaign date formatting and visual layout require in-game checks.'
