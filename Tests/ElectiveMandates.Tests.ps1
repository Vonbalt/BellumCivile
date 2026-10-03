$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; Write-Output "PASS: $message" }
$election = Read 'Behaviors/ElectiveSuccessionBehavior.cs'
$crown = Read 'Behaviors/CrownAccessionBehavior.cs'
$court = Read 'Behaviors/CourtAgendaBehavior.cs'
$store = Read 'Behaviors/RealmLawBehavior.cs'
[xml]$config = Read 'ModuleData/succession_config.xml'
[xml]$panel = Read 'GUI/Prefabs/KingdomManagement/Succession/BellumSuccessionPolicyPanel.xml'
Check ($config.SelectSingleNode('//Kingdom[@id="empire"]').electiveTerm -eq '5' -and
    !$config.SelectSingleNode('//Culture[@id="empire"]').HasAttribute('electiveTerm')) 'Northern Empire five-year mandate is a kingdom-only override.'
Check ($court.Contains('CheckMandate(realm)') -and !$election.Contains('DailyTickEvent') -and !$election.Contains('HourlyTickEvent')) 'Mandate expiry uses the existing court scheduler.'
Check ($election.Contains('if (days <= 0)') -and $election.Contains('BeginMandateElection(realm)')) 'Expiry includes its recorded date.'
Check ($election.Contains('accession.MandateExpiry ? null : accession.Predecessor')) 'Incumbent can stand at expiry but not voluntary abdication.'
Check ($crown.Contains('!record.MandateExpiry && Campaign.Current.GetCampaignBehavior<PartitionSuccessionBehavior>()') -and
    $crown.Contains('!record.ElectiveElection && !PrepareAbdicationTransfer')) 'Expiry does not distribute a living inheritance.'
Check ($crown.Contains('!record.MandateStarted') -and $crown.Contains('record.MandateStarted = true')) 'Crown journal receipts prevent repeated mandate starts.'
Check ($election.Contains('days > 0 && days <= 7') -and $election.Contains('!record.ElectionWarningSent')) 'Advance warning is once per mandate and before expiry.'
Check ($election.Contains('Confirm support') -and $election.Contains('Change support') -and $election.Contains('Abstain')) 'Final inquiry supports confirmation, revision and abstention.'
Check ($crown.Contains('DefeatNoticePending') -and $election.Contains('defeat.DefeatNoticePending = false')) 'Player defeat notice has a saved acknowledgement receipt.'
Check ($store.Contains('law.GroupId == RealmLawRegistry.TermGroup && !ElectiveSuccessionBehavior.UsesElection(realm)')) 'Term reform is rejected in hereditary realms.'
Check (([regex]::Matches($store, 'ApplyMandateReform\(')).Count -eq 2) 'Player decrees and court ballots both recalculate the sitting mandate.'
Check ($election.Contains('record.ElectionDate.ToDays') -and $election.Contains('record.ElectionDate.ToString()')) 'Scheduler and UI use the same effective election date.'
Check ($crown.Contains('standing?.ReformElectionPending == true') -and $crown.Contains('standing.ElectionDate.ToDays > CampaignTime.Now.ToDays')) 'Accession dispatch cannot skip reform deliberation.'
Check ((Read 'Behaviors/SuccessionLawBehavior.cs').Contains('Get(kingdom)?.ReformElectionPending == true') -and
    (Read 'Behaviors/CourtMandateAgendas.cs').Contains('Get(realm)?.ReformElectionPending != true')) 'Further succession rewrites wait for the reform election.'
foreach ($binding in @('@ElectionHeading', '@ElectionDate')) {
    Check ($panel.SelectSingleNode("//*[@Text='$binding']").GetAttribute('Brush.TextHorizontalAlignment') -eq 'Center') "Centered $binding."
}
Write-Output 'Source/XML contracts only; live elections, inquiries and save roundtrips require campaign testing.'
