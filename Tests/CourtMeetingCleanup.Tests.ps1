$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw -LiteralPath (Join-Path $root $path) }
function Check($condition, $label) { if (!$condition) { throw $label }; "PASS: $label" }
$ideology = Read 'Behaviors/IdeologyBehavior.cs'
$policy = Read 'Behaviors/PolicyDeliberationBehavior.cs'
$foreign = Read 'Behaviors/ForeignPolicyBehavior.cs'
foreach ($old in @('HoldFactionMeeting', 'ScheduleNextFactionMeeting', 'CourtAgendaMotion', '_factionMeetingDates', '_playerCriticalAgendaRetryDates', '_foreignPolicyMotionCooldowns', '_courtRebellionSuppressionCooldowns', 'ExecuteMilitaristEvent', 'ExecutePopulistEvent', 'ExecuteRoyalistEvent', 'ExecuteAristocratEvent', 'AttemptPolicyChange')) {
    Check (!$ideology.Contains($old)) "Retired meeting component absent: $old"
}
foreach ($old in @('_playerPolicyMandateUntil', '_playerPolicyMandateFaction', 'GrantPlayerPolicyMandate', 'CanPlayerUsePolicyMandateForCouncilAppointment')) {
    Check (!$policy.Contains($old)) "Retired nomination state absent: $old"
}
Check (!$foreign.Contains('_playerForeignPolicyMandate') -and !$foreign.Contains('GrantPlayerForeignPolicyMandate')) 'Short foreign proposal permissions retired.'
Check ($policy.Contains('CourtAgendaBehavior.Current?.IsNominationOpen(kingdom) == true') -and $policy.Contains('termAgenda?.Faction == null')) 'Permission queries use current nomination state and guard Crown alignment.'
Check ($policy.Contains('ApplyPlayerPolicyMandateDeviation') -and $policy.Contains('GetBribedVote')) 'Faction deviation consequences and vote lobbying retained.'
$agenda = Read 'Behaviors/CourtAgendaBehavior.cs'
Check ($agenda.Contains('!agenda.OrdinaryCrisisRestrained') -and $agenda.Contains('_emergencyAfter')) 'Current restraint and emergency retry ownership remain in the calendar.'
Check ($ideology.Contains('BeginCourtTerm') -and $ideology.Contains('PacifyIdeologyMembers') -and $ideology.Contains('_rebelFactionRejoinCooldowns')) 'Membership, live loyalty pacification and rebel rejoin protection retained.'
Check ((Read 'Behaviors/CourtActivityAgendas.cs').Contains('CourtSessionEvents.Execute(plan, agenda.Faction, agenda.Realm)')) 'Only saved agenda activities execute the current mood-event plans.'
$vm = Read 'UI/VanillaTabs/Kingdoms/Factions/PrivyCouncilVM.cs'
Check (!$vm.Contains('PolicyMandateForCouncilAppointment') -and $vm.Contains('BC_CourtAgenda_Unavailable')) 'Council panel no longer advertises legacy proposal permissions.'
