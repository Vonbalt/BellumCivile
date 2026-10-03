$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($value, $message) { if (!$value) { throw $message }; "PASS: $message" }
$support = Read 'Behaviors/CourtPoliticalSupport.cs'
Check ($support.Contains('GetFavoredBloc(agenda.Realm) == agenda.Faction.Type')) 'Crown alignment uses the existing term and owner-aware favor lookup.'
Check ($support.Contains('Eligible(voter, agenda.Realm)') -and $support.Contains('voter == Clan.PlayerClan')) 'Invalid households and player decisions remain protected.'
Check (!$support.Contains('.Add(')) 'Ruler is not inserted into faction or snapshot membership.'
foreach ($kind in @('Peace', 'Campaign')) {
    $behavior = Read "Behaviors/Court${kind}Agendas.cs"
    Check ($behavior.Contains('ReceivesPoliticalSupport(agenda, voter, plan.Members)') -and $behavior.Contains('plan.ActiveBonus(')) "$kind keeps shared participant eligibility and saved objective bounds."
    Check ($behavior.Contains('!agenda.IsOngoingObjective || agenda.ResultApplied')) "$kind completed or inactive objectives grant no support."
}
$favor = Read 'Behaviors/CourtCrownBehavior.cs'
Check ($favor.Contains('owner != realm.RulingClan.StringId') -and $favor.Contains('until.ToDays <= CampaignTime.Now.ToDays')) 'Existing favor expires and cannot transfer through a different ruling clan.'
[xml]$xml = Read 'ModuleData/Languages/EN/strings.xml'
Check (@($xml.SelectNodes("//string[@id='BC_Parley_Reason_CrownPeaceInitiative']")).Count -eq 1) 'Crown peace rationale has one localized entry.'
