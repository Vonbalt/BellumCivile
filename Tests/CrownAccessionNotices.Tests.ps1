$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$shock = Read 'Behaviors/IdeologyEventShockBehavior.cs'
$title = Read 'Behaviors/FeudalTitleBehavior.cs'
$crown = Read 'Behaviors/CrownAccessionBehavior.cs'
$deposition = Read 'Behaviors/ElectiveDeposition.cs'
Check (!$shock.Contains('RulingClanChanged.AddNonSerializedListener')) 'Raw ruling-clan events no longer announce ascension or reset moods.'
Check ($shock.Contains('title?.DeJureHolderClanId') -and $shock.Contains('title.DeFactoHolderClanId')) 'Ascension verifies legal and actual Crown ownership.'
Check ($shock.Contains('accession?.AccessionReactionsApplied == true') -and !$shock.Contains('previous == sovereign.StringId')) 'Saved reign receipt prevents replay without blocking later restoration of the same hero.'
Check ($title.Contains('legalTransfer && oldDeJure != rulerClan.StringId') -and $title.Contains('CompleteCrownAccession(kingdom')) 'Confirmed direct legal transfers cover claimant victories, not temporary custody.'
Check ($crown.Contains('if (!record.RegentReplacement)') -and $crown.Contains('(record.Heir != record.Predecessor || record.DepositionElection)') -and $crown.Contains('!shocks.CompleteCrownAccession(record.Realm, record.Heir, record)')) 'Reign announcements exclude regents and ordinary reelections.'
Check ($crown.Contains('record.ElectiveElection || record.Emergency') -and $crown.Contains('!shocks.CompleteElectionReactions(record,')) 'Election aftermath is independent of new-reign announcement.'
Check ($shock.Contains('ballot.Votes.Where') -and $shock.Contains('v.Supported') -and $shock.Contains('sovereign.Name')) 'Standing ballots supply electoral reactions and legal sovereign supplies the name.'
Check ($deposition.Contains('notices.Publish(notice, "election_called")')) 'Election announcement uses the saved popup queue.'
Check ((Read 'Behaviors/CivilWarResolutionBehavior.cs').Contains('BC_Result_Caretaker')) 'Deposition outcome explains caretaker, tribunal and election sequence.'
[xml]$strings = Read 'ModuleData/Languages/EN/strings.xml'
foreach ($id in @('BC_Deposition_ElectionCalled', 'BC_Deposition_ElectionCalledBody', 'BC_Result_Caretaker')) {
    Check (@($strings.base.strings.string | Where-Object id -eq $id).Count -eq 1) "Unique localized text: $id"
}
'Structural checks only; campaign transitions still require live verification.'
