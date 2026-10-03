$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$behavior = Read 'Behaviors/SuccessionChallengeBehavior.cs'
$crown = Read 'Behaviors/CrownForcedAbdication.cs'
$memory = Read 'Behaviors/DynamicRelationBehavior.cs'
$faction = Read 'FactionObject.cs'
$save = Read 'BellumCivileSaveDefiner.cs'
Check ($behavior.Contains('hero.IsActive') -and $behavior.Contains('!hero.IsPrisoner') -and
    $behavior.Contains('!hero.IsTraveling') -and $behavior.Contains('GetAgeOfMajority()')) 'Temporary invalid states block initiation without removing claims.'
Check ($behavior.Contains('Phase != SuccessionChallengePhase.Gathering') -and
    $behavior.Contains('Phase != SuccessionChallengePhase.AwaitingResponse')) 'Phase gates prevent replaying backing or response rolls.'
Check ($behavior -notmatch 'MBRandom|new FactionObject|DeclareWarAction|CreateKingdom\(') 'Settlement backend does not autonomously generate appeals or wars.'
Check ($behavior.Contains('UltimatumAcceptanceRules.Calculate(') -and $faction.Contains('UltimatumAcceptanceRules.Calculate(')) 'Concessions and ordinary factions share acceptance weights.'
Check ($behavior.Contains('GetHouseholdEstablishmentShare(plan, titles)') -and
    $behavior.Contains('CanConcede(source.Fiefs.Count, share.Fiefs.Count)')) 'Household grants use a shared package selector and protect the last fief.'
Check ($behavior.Contains('source.Fiefs.Count > remaining.Count') -and $behavior.Contains('s.OwnerClan == source || s.OwnerClan == record.Estate.Cadet')) 'Settlement retries revalidate last-fief protection and actual ownership.'
Check ($behavior.Contains('SettleCrownCadet(record.Estate)') -and $behavior.Contains('ChallengeConcession = true') -and
    $behavior -notmatch 'BeginForcedAbdication|ConfirmAbdication') 'Peaceful settlement reuses cadet delivery without recording a false Crown accession.'
Check ($crown.Contains('SuccessionChallengeBehavior.Instance?.HasInheritanceAdvance(predecessor, heir)') -and
    (Read 'SuccessionChallengeRecord.cs').Contains('public bool HasAdvance => DeliveredAdvance(CrownEstate);') -and
    $behavior.Contains('dependent && !HasHouseholdGrant(ruler, challenger)')) 'Only Crown surrender consumes inheritance; household receipts prevent repeated land demands.'
Check ($memory.Contains('m.SourceId == RelationMemorySources.SatisfiedSuccessionDemand') -and
    $memory.Contains('RefreshSuccessionConcession') -and $memory.Contains('10 * Math.Max(1, CampaignTime.DaysInYear)')) 'Personal gratitude refreshes its own source through the MCM-scaled memory service.'
Check ($behavior.Contains('record.LoyaltyEnded = true') -and !$behavior.Contains('RemoveAll') -and
    $behavior.Contains('Day + CampaignTime.Years(10).ToDays')) 'Sovereign replacement expires loyalty without deleting personal memory or estate receipts.'
Check ($save.Contains('typeof(SuccessionChallengeRecord), 69') -and $save.Contains('typeof(List<SuccessionChallengeRecord>)') -and
    (Read 'SubModule.cs').Contains('new SuccessionChallengeBehavior()')) 'Challenge journal and owning behavior are registered.'
$ids = [regex]::Matches($save, 'Add(?:Class|Enum)Definition\(typeof\([^\r\n]+?\), (\d+)\)') | ForEach-Object { $_.Groups[1].Value }
Check (($ids | Select-Object -Unique).Count -eq $ids.Count) 'Save class and enum IDs do not collide.'
$responses = Read 'Behaviors/SuccessionChallengeTesting.cs'
Check ($behavior.Contains('bool establishment = hostile || !lawfulOnly;') -and
    $behavior.Contains('GetHouseholdEstablishmentShare(plan, titles)')) 'Peaceful and hostile household grants use the same selection, separate from Crown inheritance.'
Check ($responses.Contains('DemandEstatePreview(record)') -and !$responses.Contains('EstatePreview(record.Estate ?? record.CrownEstate)') -and
    $responses.Contains('BC_Challenge_InheritanceHeading') -and $responses.Contains('BC_Challenge_CrownEstateHeading')) 'Inheritance popup never silently substitutes the Crown-surrender package.'
Check ($behavior.Contains('record.Estate == null') -and $responses.Contains('RefreshMissingConcession(record)') -and
    $behavior.Contains('grantedTitles.Add(barony)')) 'Missing pending offers can recover and granted holdings carry their barony titles.'
'Source contracts only. Allegiance UI, war dispatch and campaign/save roundtrips remain separate verification gates.'
