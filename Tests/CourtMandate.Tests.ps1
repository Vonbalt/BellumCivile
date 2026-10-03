$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw -LiteralPath (Join-Path $root $name) }
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
$source = Read 'CourtMandateObjectiveSource.cs'
$flow = Read 'Behaviors/CourtMandateAgendas.cs'
$dialogue = Read 'Behaviors/CourtMandateDialogues.cs'
$decision = Read 'MandateReformDecision.cs'
$laws = Read 'Behaviors/RealmLawBehavior.cs'
$writer = $laws.Substring($laws.IndexOf('internal bool TryApplyCourtMandate'), $laws.IndexOf('internal static bool TryReplace') - $laws.IndexOf('internal bool TryApplyCourtMandate'))
Check ($source.Contains('FactionType.Nobility') -and $source.Contains('FactionType.Liberty') -and $source.Contains('CourtMandateRules.Next')) 'Adjacent reforms owned by Nobility and Liberty.'
Check ($writer.Contains('ValidateMandateDecision') -and $writer.Contains('TryReplace') -and !$writer.Contains('CompletePlayerLawChange') -and !$writer.Contains('StartMandate')) 'Court authorization does not use proclamation aftermath or restart mandates.'
Check ($flow.Contains('CrownAccessionBehavior.Instance?.IsPending') -and $flow.Contains('PendingDeposition') -and $flow.Contains('KingSelectionKingdomDecision')) 'Succession blocks unsafe reform enactment.'
Check ($flow.Contains('a.VoteDate.ToDays + 14') -and $flow.Contains('++a.ActionAttempts > 7')) 'Suspension and retries are bounded.'
Check ($decision.Contains('if (Attempted) return;') -and $decision.Contains('choice.Law != (choice.Reform ? NewLaw : OldLaw)')) 'Outcome identity and replay are guarded.'
Check ($flow.Contains('Mandate.Decision == d') -and $flow.Contains('a.Sponsor == d.ProposerClan')) 'Only the saved authorized ballot can commit.'
Check ($flow.Contains('p.Speaker == clan.Leader') -and $flow.Contains('ClearMandatePledges')) 'Promises belong to current named clan leaders, not successors.'
Check ($flow.Contains('CourtAgendaState.Deliberating && a.VoteDate.IsFuture') -and $flow.Contains('a?.IsFiled != true')) 'New negotiations close while existing promises survive handoff.'
Check ($flow.Contains('mandate_fulfilled_elsewhere') -and $flow.Contains('_mandateSettledUntil')) 'Direct enactment and per-term reservations are tracked.'
Check ($dialogue.Contains('GetRelation(_mandateSpeaker) >= 30') -and $dialogue.Contains('PersuasionDifficulty.Medium')) 'Native persuasion retains trust gate and Medium difficulty.'
$barter = Read 'Patches/MandateVoteBarterPatch.cs'
Check ($barter.Contains('IsOfferAcceptable') -and $barter.Contains('TrySecure') -and $barter.Contains('CancelAndFinalizePlayerBarter')) 'Payment finalization validates and secures the promise first.'
Check ((Read 'MandateVoteBribeBarterable.cs').Contains('ApplyBribeCostMultiplier')) 'Bribe price respects MCM.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
$ids = @([regex]::Matches(($source+$flow+$dialogue+$decision+(Read 'MandateVoteBribeBarterable.cs')+(Read 'Behaviors/CourtPlayerMotionPicker.cs')), '\{=(BC_Mandate[^}]+)\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
foreach ($id in $ids) {
    Check (@($xml.base.strings.string | Where-Object { $_.id -eq $id }).Count -eq 1) "Exactly one English localization: $id"
}
'Mandate source/localization contracts passed.'
