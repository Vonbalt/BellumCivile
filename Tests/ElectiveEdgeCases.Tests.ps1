$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$pump = Read 'Behaviors/ElectiveContestTesting.cs'
$record = Read 'ElectiveContestRecord.cs'
$dispatch = Read 'Behaviors/ElectiveContestDispatch.cs'
$deposition = Read 'Behaviors/ElectiveDeposition.cs'
$crown = Read 'Behaviors/CrownAccessionBehavior.cs'
Check ($pump.Contains('if (!record.WarDispatchStarted && !record.CrownTransferStarted)') -and $pump.Contains('record.RevalidateParticipants(')) 'Pre-dispatch participant/ruler validation also runs after a saved ruler answer.'
Check ($record.Contains('Closed || WarDispatchStarted || CrownTransferStarted || WarDispatchCompleted')) 'Participant refresh cannot rewrite a partially dispatched conflict.'
Check ($record.Contains('pledge.Preferences.Clear();') -and $record.Contains('pledge.AwaitingPlayer = pledge.PlayerChoice;')) 'Replacement clan heads do not inherit personal commitments; player successors choose their side.'
Check ($dispatch.Contains('A challenger lost the required backing before dispatch') -and $dispatch.Contains('candidate.Withdrawn = true;')) 'Lost backing withdraws before side effects rather than retrying forever.'
Check ($deposition.Contains('if (record.InterimPrepared && valid) return true;') -and $deposition.Contains('LegalHead(caretaker) != record.DeposedRuler')) 'Prepared caretakers are revalidated and cannot restore the deposed ruler.'
Check ($crown.Contains('ElectiveExcluded = ElectiveSuccessionBehavior.Instance?.PendingDeposition(realm)?.DeposedRuler')) 'Caretaker death preserves the deposed ruler exclusion in emergency succession.'
'Source contracts only; popup restoration and native save/load still require live campaign tests.'
