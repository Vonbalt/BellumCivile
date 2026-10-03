$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read($path) { Get-Content -Raw (Join-Path $root $path) }
function Check($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
$promotion = Read 'Behaviors/CivilWarRivalPromotion.cs'
$resolution = Read 'Behaviors/CivilWarResolutionBehavior.cs'
$score = Read 'Behaviors/WarScoreBehavior.cs'
$save = Read 'BellumCivileSaveDefiner.cs'
Check ($save.Contains('AddClassDefinition(typeof(CivilWarRivalPromotionRecord), 88)') -and $save.Contains('List<CivilWarRivalPromotionRecord>')) 'Promotion journal has a registered save type and container.'
Check ($promotion.IndexOf('_promotions.Add(transfer)') -lt $promotion.IndexOf('return ResumeRivalPromotion(transfer)')) 'Native counter snapshot is saved before promotion starts.'
Check ($resolution.IndexOf('PrepareRivalCrownVictory(faction)') -lt $resolution.IndexOf('CompleteCivilWarTracker(faction, rebelKingdom, "rebel victory")')) 'Rival progress is promoted before winning-side cleanup can close it.'
Check ($score.Contains('if (old.IsActive) MarkWarEnded(old)') -and $score.Contains('war.TryPromoteRivalry')) 'Obsolete Crown matchup is retired instead of merging histories.'
Check ($score.Contains('resolution.ResolveRebelVictory(faction, primary);') -and $score.Contains('return !war.IsActive;')) 'Deferred Crown victories keep their terminal tracker available for retry.'
Check ($promotion.Contains('NativeWarStart.SetValue') -and $promotion.Contains('RestoreCounts(transfer.NativeCounts, 5, stance, crown)')) 'Native dates and both sides of battle/siege/raid counters are preserved.'
Check ($promotion.IndexOf('EndSupersededCivilWarMomentum') -lt $promotion.IndexOf('RetargetCivilWarMomentum')) 'Old-Crown war-will pressures end before the rivalry pressures are retargeted.'
Check ($promotion.Contains('transfer.CrownPair.Score = transfer.RivalPair.Score') -and $promotion.Contains('transfer.RivalPair.Closed = true')) 'Journal retains the rivalry score under the surviving Crown pair.'
Check ($promotion.Contains('FactionManager.SetNeutral(survivor, winner)') -and !$promotion.Contains('DeclareWarAction') -and !$promotion.Contains('MakePeaceAction')) 'Representation transition avoids new-war and negotiated-peace callbacks.'
Check ((Read 'Behaviors/CivilWarConflictBehavior.cs').Contains('ResumePendingRivalPromotions') -and (Read 'Behaviors/CivilWarCrownTransfer.cs').Contains('_promotions.Any')) 'Incomplete promotion resumes hourly and protects competing settlement paths.'
'Source contracts and pure score tests do not replace native campaign/save-load testing.'
