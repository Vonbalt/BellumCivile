$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw -LiteralPath (Join-Path $root $path) }
function Check($ok, $message) { if (!$ok) { throw $message }; "PASS: $message" }
function Section($source, $start, $end) {
    $a = $source.IndexOf($start, [StringComparison]::Ordinal)
    if ($a -lt 0) { throw "Missing section: $start" }
    $b = $source.IndexOf($end, $a + $start.Length, [StringComparison]::Ordinal)
    if ($b -lt 0) { throw "Missing section end: $end" }
    $source.Substring($a, $b - $a)
}

$behavior = Read 'Behaviors/ForeignTreatyBehavior.cs'
$record = Read 'TreatyProposalRecord.cs'
$guardName = 'TryValidateCouncilControlledWhitePeace('
$guard = Section $behavior ('private bool ' + $guardName) 'private void CancelInvalidProposal('
$cancel = Section $behavior 'private void CancelInvalidProposal(' 'private bool TryCancelStorylineProtectedProposal('
$prepare = Section $behavior 'private bool TryPreparePendingProposalDraft(' ('private bool ' + $guardName)
$sign = Section $behavior 'public bool TrySignPlayerParley(' 'private TreatyProposalRecord FindPendingPlayerProposal('
$resolve = Section $behavior 'private void ResolvePendingParley(' 'private void RejectParley('
$apply = Section $behavior 'private bool ApplyTreaty(' 'private bool ApplyTreatyCore('
$open = Section $behavior 'public bool TryOpenPlayerParley(WarScoreRecord war, bool forced, bool preferWhitePeace, string reason, Kingdom drafter' 'private void EnsureProposalHasDraft('
$replace = Section $behavior 'public bool TryReplacePlayerDraft(' 'private bool TryQueueParley('
$queue = Section $behavior 'private bool TryQueueParley(' 'private bool TryShowForcedPlayerParleyInquiry('
$classification = Section $record 'internal bool IsWhitePeaceOrEmpty' 'public int UsedWarScore'

Check ($classification.Contains('_terms == null') -and $classification.Contains('_terms.All(term => term == null)') -and
    $classification.Contains('term?.Type == TreatyTermType.WhitePeace')) 'Null, empty, null-only and explicit white drafts share the fallback classification.'
Check ($guard.Contains('if (!proposal.IsWhitePeaceOrEmpty)') -and
    $guard.IndexOf('if (!proposal.IsWhitePeaceOrEmpty)') -lt $guard.IndexOf('IsMutualWhitePeaceEligible')) 'Nonwhite terms bypass the new gate and retain normal treaty utility.'
Check ($guard.Contains('if (proposal.IsForced)') -and
    $guard.IndexOf('if (proposal.IsForced)') -lt $guard.IndexOf('IsMutualWhitePeaceEligible')) 'Forced terminal/no-available-terms settlement is explicitly exempt.'
Check ($guard.Contains('Clan.PlayerClan != null') -and $guard.Contains('playerKingdom?.RulingClan == Clan.PlayerClan') -and
    $guard.Contains('playerKingdom.StringId == proposal.WinnerKingdomId') -and
    $guard.Contains('playerKingdom.StringId == proposal.LoserKingdomId')) 'Only a participating player ruler receives the manual-signing exemption; vassals and unrelated rulers do not.'
Check (!$guard.Contains('proposal.DrafterKingdomId') -and !$guard.Contains('ProposerClan') -and !$guard.Contains('requireAiOnly')) 'NPC parent proposals and player-entry routing cannot misclassify final authority.'
Check ($guard.Contains('scores?.IsMutualWhitePeaceEligible(war, out _, out _) == true')) 'Shared live eligibility is required; unavailable scoring fails closed.'
Check ($guard.Contains('CancelInvalidProposal(war, proposal, report);') -and !$guard.Contains('RejectParley(')) 'Ineligible white fallback is cancelled, not politically rejected.'
Check ($cancel.Contains('TreatyProposalState.Cancelled') -and $cancel.Contains('war?.EndParley();') -and
    $cancel.Contains('_councilSnapshots.Remove(proposal.ProposalId);')) 'Cancellation releases parley and council snapshot state.'
Check (($guard + $cancel) -notmatch 'SpendCouncil|SpendRuler|ApplyFinalCouncilConsequences|RecordPeaceRejection|ApplyTreatyRelationChange') 'Eligibility cancellation neither spends influence nor applies refusal consequences or cooldown.'

foreach ($entry in @(@('Player signing', $sign), @('Automatic resolution', $resolve))) {
    $name = $entry[0]; $body = $entry[1]
    $checkAt = $body.IndexOf($guardName)
    Check ($checkAt -gt $body.LastIndexOf('TryPreparePendingProposalDraft(')) "$name checks the final normalized/repaired draft, including successful empty repairs."
    Check ($checkAt -ge 0 -and $checkAt -lt $body.IndexOf('SpendCouncilCommitments(') -and
        $checkAt -lt $body.IndexOf('SpendRulerOverride(')) "$name cancels before influence spending."
    Check ($body.Contains('ApplyTreaty(') -and $body.Contains('RejectParley(')) "$name retains normal council approval and rejection."
}
Check ($apply.IndexOf($guardName) -gt $apply.IndexOf('TryCancelStorylineProtectedProposal(') -and
    $apply.IndexOf($guardName) -lt $apply.IndexOf('ApplyTreatyCore(') -and
    $apply.IndexOf($guardName) -lt $apply.IndexOf('DeliverTreaty(')) 'Settlement backstop preserves story protection and precedes custody and settlement side effects.'
Check (!$prepare.Contains($guardName) -and !$open.Contains($guardName) -and !$replace.Contains($guardName)) 'Opening, reopening and editing remain free of the automatic settlement gate.'
Check ($queue -match 'if \(automaticRequest && !TryValidateCouncilControlledWhitePeace\(war, proposal, out report\)\)\s*\{[^}]*RecordPeaceDeferral[^}]*return false;' -and
    $queue.IndexOf($guardName) -lt $queue.IndexOf('war.BeginParley(')) 'Automatic fallback failures set backoff and return before opening a parley.'
Check ($queue.IndexOf('automaticRequest && !CanSubmitAutomaticDraft(') -lt $queue.IndexOf('war.BeginParley(') -and
    $queue.IndexOf('war.BeginParley(') -lt $queue.IndexOf('_proposals.Add(proposal);')) 'Automatic feasibility precedes parley state and registration.'
Check ($queue.IndexOf($guardName) -gt $queue.IndexOf('DraftAiTerms(war, proposal') -and
    $queue.IndexOf($guardName) -lt $queue.IndexOf('_proposals.Add(proposal);') -and
    $queue.IndexOf($guardName) -lt $queue.IndexOf('TryShowForcedPlayerParleyInquiry(')) 'AI fallback eligibility is checked after drafting but before registration or UI handoff.'
Check ($behavior.Contains('requireAiOnly: false, automaticRequest: false, drafter, out report') -and
    $behavior.Contains('requireAiOnly: !playerInvolved, automaticRequest: true, drafter, out report')) 'Manual/native-player queueing bypasses the early gate; AI requests include player-vassal wars.'
Check ($queue.IndexOf('if (existing != null)') -lt $queue.IndexOf($guardName) -and
    !$record.Contains('automaticRequest')) 'Existing proposals are reused and the early-request flag is not saved as provenance.'
Check ($replace.Contains('TreatyDraftService.TryValidateAndNormalize(') -and
    $prepare.Contains('repairStateDrift: true')) 'Existing draft validation and state-drift repair remain in place.'
Check ($open.Contains('TreatyProposalRecord proposal = GetPendingProposalForPlayer(war);') -and
    $open.Contains('if (proposal == null)')) 'Reopening reuses the pending proposal instead of assigning new provenance.'
Check (([regex]::Matches($behavior, [regex]::Escape($guardName))).Count -eq 5) 'The shared guard has one early queue check and three final-resolution checks.'
Check ($guard -match 'if \(proposal\.IsForced\)\s+return true;') 'Every shared-guard invocation immediately preserves forced settlement.'
Check (([regex]::Matches($behavior, 'IsMutualWhitePeaceEligible\(')).Count -eq 1 -and
    !$behavior.Contains('CanConsiderWhitePeace(')) 'No duplicate white eligibility check can override the forced or manual exemption.'
Check ($guard.Contains('Player rulers intentionally bypass this automatic gate; normal councils still decide.')) 'The intentional manual player-ruler bypass is documented at the exemption.'
Check ($sign -match 'if \(!TryValidateCouncilControlledWhitePeace\(war, proposal, out report\)\)\s+return proposal.State == TreatyProposalState.Cancelled;' -and
    $resolve -match 'if \(!TryValidateCouncilControlledWhitePeace\(war, proposal, out _\)\)\s+return;') 'Both pre-spending failures return immediately without reaching any spending or council penalties.'
'Automatic white-peace fallback source contracts passed; no build or campaign runtime tests executed.'
