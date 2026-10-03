$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($name) { Get-Content -Raw (Join-Path $root $name) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$war = Read 'Behaviors/SuccessionChallengeWar.cs'
$faction = Read 'FactionObject.cs'
$titles = Read 'Behaviors/FeudalTitleBehavior.cs'
$resolution = Read 'Behaviors/CivilWarResolutionBehavior.cs'
$recovery = Read 'Behaviors/CivilWarSuccessionSettlement.cs'
$behavior = Read 'Behaviors/SuccessionChallengeBehavior.cs'
Check ($war.Contains('record.MilitaryEstate') -and $war.Contains('hostile: true') -and $war -notmatch 'record.Estate\b|DeliverAbdicationGold|RefreshSuccessionConcession') 'Refusal uses a separate protected secondary package, without fallback gifts, treasury seizure or gratitude.'
Check ($war.IndexOf('record.WarDispatchStarted = true') -lt $war.IndexOf('PrepareAbdicationCadet(estate)') -and
    $war.IndexOf('PrepareAbdicationCadet(estate)') -lt $war.IndexOf('StartFrozenSuccessionRebellion')) 'Journal precedes cadet preparation, which precedes rebel shell startup.'
Check ($war.IndexOf('record.SeizureIntents.Add(id)') -lt $war.IndexOf('titles.TransferChallengePossession(estate.EndowmentHouse') -and
    $war.Contains('record.SeizedFiefs.Add(id)')) 'Hostile transfer persists intent before callbacks and verifies delivery afterward.'
$possession = $titles.Substring($titles.IndexOf('internal bool TransferChallengePossession'))
$possession = $possession.Substring(0, $possession.IndexOf('public int MoveCrownHeirClaims'))
Check ($possession.Contains('_deFactoOnlySettlementTransferTitleId = title.TitleId') -and
    $possession.Contains('title.DeJureHolderClanId == legalOwner') -and $possession.Contains('title.ParentTitleId == legalParent') -and
    $possession.Contains('finally')) 'Seizure and restitution protect legal owner/parent and restore nested transfer context.'
Check ($war.Contains('holding?.OwnerClan == estate.Cadet') -and $war.Contains('LegalSnapshotMatches(record, title)') -and
    $war.Contains('record.ReconciledFiefs.Add(id)')) 'Restitution only touches tracked possession; later third-party ownership and grants are not overwritten.'
Check ($war.Contains('CalculateLiveClanPower') -and $war.Contains('DispatchPledgesValid(record)') -and
    $war.Contains('The coalition no longer has a stronghold') -and $war -notmatch 'MBRandom|RallyAiSupporters') 'Final dispatch rechecks current forces and holdings without rerolling allegiance.'
Check ($faction.Contains('resumeChallenge ? GetTrackedRebelKingdomIncludingEliminated()') -and
    $faction.Contains('if (resumeChallenge) SetRebelKingdom(rebelKingdom)') -and
    $faction.Contains('if (!resumeChallenge || !_challengeShellInitialized)') -and
    $faction.Contains('if (IsChallengeStartupPending) return false')) 'Frozen startup reuses its deterministic shell and hides incomplete war state.'
Check ($resolution.Contains('PrepareWarOutcome(faction, SuccessionChallengeOutcome.Defeat') -and
    $resolution.Contains('PrepareWarOutcome(faction, SuccessionChallengeOutcome.WhitePeace') -and
    $resolution.Contains('PrepareWarOutcome(strongestFaction, SuccessionChallengeOutcome.Victory')) 'Normal defeat, white peace and restored-realm victory capture outcomes before cleanup.'
Check ($recovery.Contains('QueuePlayerTribunal(realm, realm, record.OutcomeLosers') -and
    $recovery.Contains('record.TribunalQueued = true') -and $recovery -notmatch 'ApplyPostWarConsequences\(') 'Both AI and player challenge tribunals reuse the saved queue without replaying immediate judgments.'
Check ($resolution.IndexOf('savedRewards.Add(clan)') -lt $resolution.IndexOf('CampaignEventDispatcher.Instance.OnClanInfluenceChanged(clan, reward)') -and
    $resolution.Contains('HasPendingWarOutcome(collapsedParent)')) 'Rewards record before event callbacks; pending collapse outcomes cannot create another successor realm.'
Check ($resolution.Contains('challenge.OutcomeRealm = restoredKingdom') -and
    $resolution.Contains('challenge.RestoredRealmInitialized = true') -and
    $resolution.Contains('challenge.RestoredRealmReady = true') -and $recovery.Contains('!record.RestoredRealmReady')) 'Restored successor creation has a deterministic identity and resumable initialization receipts.'
Check ($war.Contains('LegalizeChallengeVictory') -and $war.Contains('estate.EndowmentSettled = true') -and
    $war.Contains('record.WarShell.Clans.Any(c => !c.IsEliminated)')) 'Victory advances and cooldown completion require actual title/cleanup postconditions.'
Check ($behavior.Contains('SubmissionRuler = null') -and (Read 'HereditaryLoyalty.cs').Contains('+ Submission +') -and
    (Read 'UI/VanillaTabs/Kingdoms/Succession/KingdomSuccessionTabVM.cs').Contains('BC_Loyalty_Submission')) 'Submission has a separate tooltip contribution and expires on ruler replacement.'
$allChallenge = $behavior + $war + (Read 'Behaviors/SuccessionChallengeDispatch.cs') + (Read 'Behaviors/SuccessionChallengePledges.cs')
Check ([regex]::Matches($allChallenge, '\bTryBegin\(').Count -eq 1) 'Settlement and pledge backends do not create extra appeals.'
$testing = Read 'Behaviors/SuccessionChallengeTesting.cs'
Check ([regex]::Matches($testing, '\bTryBegin\(').Count -eq 1 -and $testing.Contains('internal string StartTestChallenge')) 'Explicit test entry continues to use the shared initiation backend.'
Check (!$testing.Contains('r.ManualTest &&') -and $testing.Contains('r.Phase == SuccessionChallengePhase.AwaitingResponse') -and
    $testing.Contains('if (record.ResponseRoll < 0) record.ResponseRoll = MBRandom.RandomFloat;') -and
    $testing.Contains('RecordNpcResponse(record, record.ResponseRoll)')) 'Natural and manual challenges advance responses and retry the same saved roll.'
Check ($testing.Contains('RecordPlayerResponse(record, phase)') -and $testing.Contains('CanDeliver(record)') -and
    $testing.Contains('CrownEstateAvailable(record.CrownEstate)') -and $testing -notmatch 'SetTestLoyalty|RallyAiSupporters') 'Player choices revalidate through the normal backend without forced loyalty or recruitment.'
Check ((Read 'SuccessionChallengeRecord.cs').Contains('[SaveableField(57)] public bool ManualTest;')) 'Controlled test marker survives save/load.'
'Source contracts only. In-game shell creation, partial transfers, tribunals and save/load still need campaign tests.'
