$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
$vote = Get-Content -Raw (Join-Path $root 'Patches/FiefVoteResolutionPatch.cs')
$shock = Get-Content -Raw (Join-Path $root 'Behaviors/IdeologyEventShockBehavior.cs')
$war = Get-Content -Raw (Join-Path $root 'Behaviors/CivilWarResolutionBehavior.cs')
Check ($vote.Contains('ActiveAwards.Add(new AwardScope') -and $vote.Contains('ActiveAwards.FindLastIndex')) 'Ballot context supports nested awards.'
Check ($vote.Contains('public static Exception Finalizer') -and $vote.Contains('return __exception;')) 'Ballot scope is released without swallowing native exceptions.'
Check ($shock.Contains('if (!Patches.FiefVoteResolutionPatch.DeferAward(settlement))')) 'Native award callbacks defer to ballot aftermath.'
Check ($vote.Contains('?.Transferred == true')) 'No-op and excluded treaty transfers do not invent an award shock.'
Check ($vote.Contains('?.RecordFiefAward(kingdom, winner, winningSupporters)')) 'Ballot supplies its actual supporters to mood recording.'
Check ($shock.Contains('supporters?.Contains(faction.Leader) == true')) 'Pragmatic faction support exempts the mood penalty as well as relations.'
Check ($shock.Contains('RecordFiefAward(newKingdom, newOwner.Clan)')) 'Non-ballot gifts retain ordinary award recording.'
Check ($vote.Contains('Resolved.TryGetValue(__instance, out _)')) 'Repeated resolution skips political aftermath.'
Check ($vote.IndexOf('if (winner == null') -lt $vote.IndexOf('Resolved.Add(__instance')) 'Failed ownership validation does not consume the receipt.'
$tribunal = Get-Content -Raw (Join-Path $root 'Behaviors/CivilWarTribunalReactions.cs')
Check ($tribunal.Contains('factions?.FirstOrDefault(f => f.Members.Contains(clan))')) 'Temporary rebel rulers retain their actual original-court membership snapshot.'
Check (!$war.Contains('ApplyTribunalShocks(') -and !$tribunal.Contains('FactionType.Royalists')) 'Retired aggregate tribunal reactions are removed.'
Check ($shock.Contains('RecordRoyalExecution(killer.Clan.Kingdom, victim)') -and $shock.Contains('ApplyMoodShock(sentencingRealm, FactionType.Nobility, -30f)')) 'Confirmed native execution retains one Nobility reaction.'
Check ($tribunal.Contains('bloc == FactionType.Liberty && record.Clemency ? 30 : 0')) 'Liberty clemency is separate from capped member totals.'
Check ($shock.Contains('OwnsExecutionReaction(victim) == true')) 'Tribunal-owned executions bypass generic mood only.'
Check ($war.Contains('CompleteTribunalExecution(executionId, condemnedLeader.IsDead, exiledAfterSentence)')) 'Execution aftermath uses actual death/exile, not queue acceptance.'
Check ($war.Contains('CompleteTribunalExecution(executionId, executed: false)')) 'Cancelled executions settle their pending reaction without an execution penalty.'
Check ($war.Contains('BC_TribunalReactions') -and $tribunal.Contains('record.Closed = true')) 'Deferred reaction groups are saved and explicitly closed.'
Check ($war.Contains('civilWarStartFiefSnapshot, groupId)')) 'Caretaker handoff preserves original reaction group identity.'
[xml]$xml = Get-Content -Raw (Join-Path $root 'ModuleData/Languages/EN/strings.xml')
foreach ($id in @('BC_Ag_TribunalApproval', 'BC_Ag_TribunalReprisal')) {
    Check (@($xml.base.strings.string | Where-Object id -eq $id).Count -eq 1) "Unique tribunal history label: $id"
}
