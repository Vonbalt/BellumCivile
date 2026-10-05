# Bellum Civile 1.4.0 - Maintainer Guide

Bellum Civile extends Mount & Blade II: Bannerlord with a connected political simulation: court factions, feudal titles, dynastic succession, internal wars, and negotiated foreign affairs. Relationships, personality, legal rights, wealth, influence, and military strength feed into these systems through shared services.

This repository contains the source for the released **Bellum Civile 1.4.0**. This README provides a technical orientation and maintenance reference for contributors. Players can find the gameplay guide in **Encyclopedia > Concepts > Bellum Civile**.

| Item | Current state |
| --- | --- |
| Documentation revision | October 3, 2026 |
| Release version | Bellum Civile 1.4.0 |
| Target game version | Bannerlord 1.4.8 |
| Core runtime | .NET Framework 4.7.2 (`net472`) |
| Required mod dependencies | Harmony, UIExtenderEx, Mod Configuration Menu v5 |
| Optional integrations | Diplomacy, True Noble Opinion, Harvest and Production Overhaul (HAP), custom-banner integrations, Naval DLC / War Sails |
| Development branch | `main` |
| License | [CC BY-NC-SA 4.0](LICENSE.txt) |

### Reading This Guide

Start with the project map and invariants, then follow the section for the system being changed. Balance numbers describe compiled defaults unless a setting or modifier is named. A **day** means a campaign day; durations expressed in **years** use the campaign calendar rather than an assumed 84-day year. Relation-memory durations are quoted at the default 1x multiplier.

The mod centers on term-based court agendas, three-way policy stances, separate legal and actual title hierarchies, personal succession and realm inheritance, bilateral marriage evaluation, relation memories, and score-budgeted peace treaties with hostage pledges. The sections below describe their rules, shared state, and integration points.

## Contents

- [Project Map](#project-map)
- [Critical Invariants](#critical-invariants)
- [Court Politics and Council](#court-politics-and-council)
- [Rebellions and Claim Feuds](#rebellions-and-claim-feuds)
- [Titles, Claims, and Service](#titles-claims-and-service)
- [Succession, Marriage, and Households](#succession-marriage-and-households)
- [Personal Unions and Realm Partition](#personal-unions-and-realm-partition)
- [War and Peace](#war-and-peace)
- [Relations and Personal Politics](#relations-and-personal-politics)
- [Interfaces and Localization](#interfaces-and-localization)
- [Configuration and Extension Points](#configuration-and-extension-points)
- [Persistence and Recovery](#persistence-and-recovery)
- [Optional Integrations](#optional-integrations)
- [Build and Packaging](#build-and-packaging)
- [Verification and Diagnostics](#verification-and-diagnostics)
- [Maintenance](#maintenance)
- [License](#license)

## Project Map

### Runtime Structure

[SubModule.cs](SubModule.cs) initializes Harmony and UIExtenderEx, loads configuration, registers campaign behaviors and models, and schedules deferred UI work. Campaign behaviors own durable state and lifecycle events. Models and calculation services supply shared rules. Harmony patches and UI extensions connect those rules to native actions and screens.

Shared records and services mostly live at the project root. Campaign owners are under `Behaviors/`, native interception under `Patches/`, ViewModels/extensions under `UI/`, and assets under `GUI/` and `ModuleData/`. Several large behaviors are split into partial-class files; inspect those files before assuming the main behavior contains the complete lifecycle.

| Area | Primary entry points and responsibilities |
| --- | --- |
| Court membership and pressure | [FactionManagerBehavior](Behaviors/FactionManagerBehavior.cs), [IdeologyBehavior](Behaviors/IdeologyBehavior.cs), `RebellionPowerHelper.cs` |
| Political scheduling | [CourtAgendaBehavior](Behaviors/CourtAgendaBehavior.cs) and its partial files; objectives, mandates, crises, and execution receipts |
| Council | [PrivyCouncilBehavior](Behaviors/PrivyCouncilBehavior.cs), `PrivyCouncilAssignmentRegistry.cs`, `CouncilIncidentRegistry.cs`, appointment and controversy behaviors |
| Internal conflicts | `CivilWarConflictBehavior`, `CivilWarResolutionBehavior`, `InternalPeaceSettlementBehavior`, `ClaimFeudBehavior`, `ClaimFeudWarBehavior`, `ConflictCallResponseBehavior` under `Behaviors/` |
| Titles and ownership | [FeudalTitleBehavior](Behaviors/FeudalTitleBehavior.cs), `FeudalTitlePlayerActionService.cs`, `FeudalTitleUsurpationAssessmentService.cs`, `FeudalTitleConfig.cs` |
| Title presentation | `FeudalTitleDisplayHelper.cs`, `RealmSovereignSelection.cs`, `RealmNameConfig.cs`, `DynamicKingdomTitleNameHelper.cs`, `Patches/FeudalTitleHeroNamePatch.cs` |
| Crown succession | [CrownAccessionBehavior](Behaviors/CrownAccessionBehavior.cs), elective succession, hereditary loyalty, succession challenge, regency, and dynastic-heir behaviors |
| Estates and realms | [PartitionSuccessionBehavior](Behaviors/PartitionSuccessionBehavior.cs), `FeudalInheritancePlanner.cs`, `Behaviors/CrownPartition*.cs`, `Behaviors/CrownRealmUnion*.cs`, shared `RealmUnion*.cs` services |
| Marriage | [StrategicMarriageBehavior](Behaviors/StrategicMarriageBehavior.cs), `Behaviors/BellumMarriageEvaluation.cs`, household/health/political helpers, `SpecificMarriageProposalBehavior` |
| War readiness and targeting | [WarPeaceRevampBehavior](Behaviors/WarPeaceRevampBehavior.cs), [WarTargetScoringService](WarPeace/WarTargetScoringService.cs) |
| Conflict score | [WarScoreBehavior](Behaviors/WarScoreBehavior.cs), [PeaceReadiness](PeaceReadiness.cs) |
| Treaties | [ForeignTreatyBehavior](Behaviors/ForeignTreatyBehavior.cs), `TreatyAiDraftService.cs`, `TreatyDraftService.cs`, `TreatyRatificationService.cs`, `TreatyWarScoreAccounting.cs` |
| Hostages and clients | `HostagePactBehavior`, `RetainedTreatyPrisonerBehavior`, `HouseholdRansomBehavior`, `ClientKingdomBehavior` under `Behaviors/` |
| Relations | [DynamicRelationBehavior](Behaviors/DynamicRelationBehavior.cs), `DynamicRelationBaselineHelper.cs`, `RelationMemoryService.cs`, `RelationMemoryRecord.cs` |
| Shared spending | [NpcInfluenceBudgetService](NpcInfluenceBudgetService.cs); reserves, payments, refunds, and telemetry |
| Settings and saves | `BellumCivileSettings.cs`, `BellumCivileOptions.cs`, `BellumCivileConstants.cs`, [BellumCivileSaveDefiner](BellumCivileSaveDefiner.cs) |

### Models and Decisions

`BellumFeudalClanFinanceModel` supplies title/service accounting and visible council salaries. `BellumClanPoliticsModel` adds council influence. Council settlement models wrap patrol, militia, construction, and food calculations. `DynamicArmyManagementModel` applies military authority and army modifiers. `DynamicSuccessionModel`, `BellumMarriageModel`, and `BellumAgeModel` connect succession, matching, and adulthood to native systems.

Fief, policy, expulsion, and council deliberation behaviors prepare their respective votes; `DelayedVoteReliability` handles stale decisions and pending records. Bellum-created decisions must enter through `IdeologyBehavior.AddDecisionAsModAction` where required, so native-decision interception does not block Bellum's own proposals.

Wrapped models capture the model already registered at startup and delegate unrelated calculations to it. This is why load order matters for economy and diplomacy integrations. Army management, succession, and marriage have their own registered models; do not assume every model uses the same wrapping strategy.

### Shared Terminology

| Term | Meaning in this project |
| --- | --- |
| House / clan | A noble family and its engine `Clan`; household membership can change without erasing a person's dynastic rights |
| Realm / kingdom shell | The engine `Kingdom` that owns political membership, wars, policies, and decisions |
| Crown | A sovereign political title; its legal tree can outlive or differ from its kingdom shell |
| De jure / de facto | Legal ownership and hierarchy / actual possession and hierarchy |
| Agenda / motion | Scheduled political business / the specific action or objective selected for it |
| Filing mandate | A faction leader's time-limited authority to submit a policy or council proposal |
| Elective mandate | The sitting ruler's term of office, governed by realm law |
| Receipt / journal | Saved evidence of a completed action / the record coordinating a multi-stage transition |

## Critical Invariants

- **Legal ownership and possession are separate.** De jure titles establish rights and legal hierarchy. De facto control determines possession, service, military authority, and displayed rank. Capture alone does not confer legal ownership.
- **A campaign kingdom and a legal Crown are different objects.** Temporary war kingdoms conduct conflicts without becoming sovereign legal roots. A legitimate Crown may survive destruction or absorption of its kingdom shell.
- **Court affiliation does not create a military alliance.** Calls to arms require a qualifying title-vassal, marriage, or friendship tie; ideology can modify acceptance.
- **The court agenda schedules NPC political business.** Do not restore independent random meeting or vacancy schedulers. Player rulers retain paid, on-demand actions under ordinary legal rules.
- **The ruler cannot occupy a council seat.** Appointment, dismissal, and override powers do not make the ruler a candidate.
- **Peace willingness, treaty acceptability, and War Score are separate.** Exhaustion encourages settlement; it cannot authorize unavailable terms or over-budget demands.
- **The revamp toggle is a behavior boundary.** With it off, native war and peace must remain functional. With it on, foreign peace uses the Parley lifecycle, including redirects from stale native decisions.
- **Saved decisions are not rerolled on load.** Preserve heirs, agenda dates, selected targets, ballots, and legal history. Repair impossible references conservatively.
- **Multi-stage transfers require evidence of completion.** A started native action is not proof that it returned successfully. Reconcile journals before retrying irreversible work.
- **Presentation refreshes do not rewrite rights.** XML/style changes may refresh configured names, but preserve legal owners, historical origins, and explicit player renames.
- **Identifiers are stable; names are presentation.** Never use localized text as a save key, title identity, or integration lookup.
- **Optional integrations remain optional.** Core systems must load without their assemblies. Cache reflection and report unsupported paths without corrupting campaign state.

## Court Politics and Council

### Crown and Court Blocs

Permanent realms have three member blocs: **Nobility**, **Glory**, and **Liberty**. The **Crown** represents the ruling house separately.

| Bloc | Main interests |
| --- | --- |
| Nobility | Landed rights, claims, hierarchy, noble representation, and dynastic diplomacy |
| Glory | Military standing, campaigns, conquest, subjugation, and victory |
| Liberty | Prosperity, trade, peace, limits on royal power, and shorter elective mandates |

Affiliation combines personality, political position, and bounded social ties. Membership and leadership are reviewed through the court term lifecycle. The ruler cannot be an ordinary member. Legacy faction identities remain reserved for save migration.

Joining or leaving creates no direct relation award or penalty. A live condition supplies +10 for shared affiliation or -10 for different blocs within the same realm. Unaffiliated and cross-realm pairs receive neither.

The ruler may favor a bloc once per term for **100 influence**, or remain impartial for free. NPC choices respect influence reserves. Favor can extend applicable faction-objective support to the ruler without making the Crown a faction member.

### Terms, Motions, and Mandates

Ordinary agendas open once per configured term, default **one campaign year**. Membership and leadership settle before motion selection. Eligible blocs normally require two houses. Registered candidate sources propose weighted, valid business; a bloc without viable business waits for the next term.

Player faction leaders receive **Approve / Block / Substitute** choices. Taking leadership at term opening also triggers review of the pending motion. Substitution exposes eligible alternatives and has an influence cost. Policy and council motions can grant a filing mandate: the leader uses the normal Policies or Privy Council buttons during that window. Missing an unused mandate angers the faction; unrelated decisions do not consume it.

The agenda saves dates, targets, deadlines, outcomes, and execution receipts. Success, failure, invalidation, and player neglect are distinct results. An objective can encourage war, marriage, trade, or peace without guaranteeing acceptance by the relevant parties.

| Motion family | Main owners and purpose |
| --- | --- |
| Laws and council | Eligible blocs propose policies or appointments; Nobility seeks longer elective terms and Liberty shorter ones |
| Campaigns and claims | Glory pursues war, subjugation, and rallying; Nobility pursues claimant campaigns and title grants |
| Foreign relations | Nobility pursues dynastic marriage/alliance objectives; Liberty pursues peace and trade |
| Crown government | Land/title grants, treason/revocation business, faction accommodation, and council replacement |
| Crown foreign affairs | Seeking protection, preparing client liberation, and granting a barony to a client |
| Crises and activities | Political challenges, council vacancies, feud peace enforcement, and mood-driven session activities |

The **player ruler does not receive the NPC annual Crown business invitation**. Existing policy, council, title, and expulsion interfaces remain available. Special prerogatives live in **Crown Actions**. Non-rulers see a disabled control with an explanation. Current agenda reflects active proceedings without imposing another scheduling gate on the player.

| Crown Action | Cost and reuse rules |
| --- | --- |
| Appease a court faction | 100 influence plus 25 per member house; +20 mood accommodation; one full-term cooldown across appeasement targets; cannot stack or refresh |
| Prepare for liberation | 100 influence; +20 Liberty Desire preparation; one full-term cooldown; ordinary readiness and declaration rules still apply |
| Seek foreign protection | 100 influence even if refused; one full-term cooldown after sending the appeal |
| Grant land to a client | 100 influence plus the transferred holding; excludes the last fief, sieges, disputes, wartime occupations, and pending distribution |
| Enforce Peace | Existing feud-specific influence calculation, scaling with the participants' hierarchy tiers; foreign-war and eligibility rules still apply |

Cooldowns are saved per realm and action, not reset by switching a target or ruler. This is separate from the active benefit: ruler replacement ends an appeasement accommodation, while changed rulership or clientage ends liberation preparation. See `Behaviors/CourtPlayerCrownActions.cs` and `Behaviors/CourtCrownActionCooldowns.cs` for presentation and receipts.

### Mood and Policy Stances

Mood ranges from -100 to +100. Material baselines reflect landholding, law, centralization, welfare, war, and council representation. Event shocks are separate; their temporary history entries use the rule that magnitude `N` remains listed for `N` days. This display duration is not a personal relation-memory duration or a replacement for an activity's saved lifetime. Mood-driven activities compete in the agenda pool rather than running as an independent scheduler.

[ModuleData/bellum_policy_agendas.xml](ModuleData/bellum_policy_agendas.xml) distinguishes **Support**, **Neutral**, and **Oppose**. Unlisted non-Crown policies are neutral. Crown-designated centralizing policies use current bloc mood:

| Mood | Effective stance |
| --- | --- |
| 20 or below | Oppose |
| Above 20, below 60 | Neutral |
| 60 or above | Support |

[CourtPolicyStanceRules.cs](CourtPolicyStanceRules.cs) supplies the shared rule. Eligibility, voting, mandates, and card colors use the same stance: blue for support, untinted for neutral, red for opposition. Material policy interests remain separate from mood-baseline calculations to prevent a feedback loop. Reactions use pre-outcome stances, so a result's own mood shock cannot redefine betrayal retroactively.

### Political Power and Challenges

Projected conflict power combines military strength and influence with eligible direct vassals, same-realm marriage allies, and close friends. Wealth is not added. Supporters are counted once, opposing commitments are excluded, and mood affects expected commitment. The Crown belongs to the loyalist side.

At the ordinary rebellious mood threshold of **-60**, a bloc can challenge the ruler when projected support is credible. Other hostile blocs may form a grand coalition. Emergency -100 attempts have bounded retry rules. These conflicts seek abdication through the appropriate succession handler; faction mood cannot manufacture a throne claim.

### Privy Council

The Council appears inside `Court of {SOVEREIGN_TITLE_NAME}` in the Factions tab. Its short territorial name comes from the title hierarchy rather than `Kingdom.Name`.

| Office | Permanent role |
| --- | --- |
| Marshal | Delegated authority to summon the realm host when restricted feudal calls are enabled |
| Chancellor | Competence-based motion discounts for the councillor's faction and mitigation of unresolved feud pressure |
| Seneschal | Improves feudal service income reaching the Crown |
| Spymaster | Acts as the realm's intrigue proxy and modifies offensive/defensive intrigue |
| First Advisor | Additional council support; unlocks above 30 towns and castles |
| Second Advisor | Additional council support; unlocks with an empire-tier sovereign title |

Sustainable core offices are limited by eligible council clans, up to four. Unsustainable vacancies are excused. Every occupied seat grants its clan daily influence and a competence-scaled salary. Salary enters the clan-finance model and its named breakdown, not an invisible separate daily payment. `PrivyCouncilOfficeRecord` saves `HolderClanId`; runtime calculations resolve the councillor through that clan's current leader.

Tooltips show competence, contributing skills, controversy, support, and assignment effects. Candidate competence appears in appointment voting. A nomination ballot produces three candidates for the kingdom decision. The player's realm receives deliberation and persuasion/bribery dialogue; NPC-only appointments can resolve synchronously at their scheduled session.

**Appoint** costs 100 influence before applicable Chancellor discounts. A faction leader needs an active mandate; a player ruler can file directly. **Dismiss** is ruler-only, costs 100 influence, applies grievances/mood effects, and leaves a vacancy. Overrides have their own influence and relation costs.

NPC rulers dismiss councillors in any captivity, with a lesser grievance than scandal dismissal. Eligible vacancies enter a serialized Crown crisis queue; subsequent appointments wait for the current proceeding. Player rulers manage captured councillors manually. A new appointment resets office controversy and assignment state.

### Competence, Controversy, and Support

`PrivyCouncilBehavior.CalculateCompetence` combines two skills and one attribute:

| Office | First skill | Second skill | Attribute |
| --- | --- | --- | --- |
| Marshal | Tactics | Leadership | Endurance |
| Chancellor | Charm | Steward | Social |
| Seneschal | Steward | Trade | Intelligence |
| Spymaster | Roguery | Scouting | Cunning |
| Advisors | Charm | Leadership | Social |

Each skill contributes up to 40 points after clamping to 0-300; the attribute contributes up to 20 after clamping to 0-10. Raw competence therefore ranges from 0 to 100. Advisor bonuses can change effective core-office competence. Assignment benefit and drawback multipliers use that effective value; 50 is their neutral baseline. Do not substitute a candidate's raw competence for the incumbent's effective runtime value when displaying assignment effects.

Core-office controversy attributes failures to the responsible office: military reverses to the Marshal, diplomatic/court failures to the Chancellor, economic distress to the Seneschal, and hostile intrigue to the Spymaster. Successes can reduce it. Repeated military events are capped. Ruler controversy derives from core-office controversy and sustainable vacancies and feeds rebellious intent.

`PrivyCouncilOfficeRecord.Controversy` is clamped to **0-100**, not an unbounded accumulator. All offices share that scale, but their events and recovery paths differ. Support separately measures political security through competence, relations, representation, appointment history, monopolies, and current conditions. Low support is not the same as high controversy.

The UI uses the following bands, with each lower bound inclusive:

| Value | Competence | Controversy | Support |
| --- | --- | --- | --- |
| 0 to below 20 | Inapt | Unblemished | Isolated |
| 20 to below 40 | Mediocre | Questioned | Contested |
| 40 to below 60 | Average | Contentious | Accepted |
| 60 to below 80 | Skillful | Scandalous | Respected |
| 80 to 100 | Masterful | Ruinous | Entrenched |

Compatibility integrations should read these as separate metrics, not label a controversial office as a contested vote. Presentation is in `CouncilCompetencePresentation.cs` and `UI/VanillaTabs/Kingdoms/Factions/PrivyCouncilVM.cs`.

### Assignments and Incidents

Assignments normally have a **30-day change cooldown** and stop functioning during captivity. `None` gives no administrative benefit and costs one relation per week with the sidelined councillor. The following are baseline effects; competence scales applicable costs/drawbacks and incidents can modify results. Tooltips show effective values.

| Office | Assignment | Baseline effect and tradeoff |
| --- | --- | --- |
| Marshal | Organize Patrols | About 10% larger patrols, 15% faster formation; protection failures cause 25% more controversy |
| Marshal | Train the Militia | +10% positive militia growth and veteran chance; -10% village production |
| Marshal | Oversee Logistics | -15% army cohesion loss and army-party food consumption; weekly controversy |
| Chancellor | Appease the Nobility | About +1 relation weekly with an estranged loyal vassal, up to +25; weekly controversy |
| Chancellor | Improve Foreign Relations | About +1 relation weekly with an estranged neighboring ruler at peace, up to +25; weekly controversy |
| Chancellor | Fabricate Grievances | Ruling-house fabrication 25% faster with +5 percentage points discovery risk; weekly controversy |
| Seneschal | Audit Vassals | +10% feudal service income to the Crown; weekly controversy |
| Seneschal | Subsidize Infrastructure | +10% construction; 50 denars per stronghold daily, only when the full cost is affordable |
| Seneschal | Stockpile Provisions | Up to +2 food per stronghold daily; 30 denars per stronghold, all-or-nothing funding; HAP uses market requisition instead |
| Spymaster | Counter-Espionage | +15 defensive intrigue, +5 percentage points hostile-claim discovery, -5 offensive intrigue |
| Spymaster | Uncover Dissent | 15% slower covert rebel growth, +10 percentage points hostile-claim discovery; weekly controversy |
| Spymaster | Sow Rumors | +15 offensive intrigue, -5 defensive intrigue; weekly controversy |
| Advisor | Counsel the Crown | +5 effective core-office competence; two advisors cap at +8 |
| Advisor | Mediate the Council | +5 support and about -1 weekly controversy at the worst core office; combined caps +8 and -1.5 |
| Advisor | Represent Court Interests | +5 faction mood baseline and -5 rebellious intent; same-faction representation does not stack |

`CouncilIncidentBehavior` runs incidents only for a player ruler. Reviews occur every 14-35 days, with a 10% roll per eligible assignment and at most one incident opened. Individual incidents have an 84-day cooldown. Full effects last 30 days and limited trials 15. Each assignment has three definitions in `CouncilIncidentRegistry.cs`.

### Deliberation and Voting

The default **five-day** deliberation applies in the player's realm to fief grants, policies, expulsion/treason, and council appointments. Clan leaders explain their intentions through dialogue, with eligible persuasion/bribery. NPC-only proceedings use their immediate-resolution paths. Crown succession has a separate standing election/accession lifecycle.

Fief candidates are weighted by claims, need, title rank, proximity, clan tier, relations, and support. Anti-monopoly pressure limits repeated grants to overlanded houses. Treason confiscation returns fiefs to ordinary distribution. The direct high-treason decree retains its separate dialogue path.

When changing votes, check both deliberation and the final decision. `DelayedVoteReliability.cs` validates pending records, duplicate decisions, retries, and orphaned unassigned fiefs.

## Rebellions and Claim Feuds

### Rebel Movements and Solidarity

Rebellious Intent combines grievances, mood, ruler controversy, demesne hoarding, culture, claims, rank, land need, relations, and personality. The default threshold is **100**. Armed movement types are **Independence**, **Abdication**, and **Install Ruler**. Claims and feuds replace the former redistribution rebellion.

Political membership uses cause-specific legitimacy and relationship rules. Power is checked at escalation and when joining an active war. A succession crisis can accelerate an existing claimant movement.

Direct title-vassals, same-realm marriage allies, and close friends can receive calls to arms. Acceptance weighs duty, relations, personality, cause, ideology, and strength. Side-choice and messenger popups preserve player control. Mercenaries on the Crown's payroll are excluded from private-feud recruitment. Defection persuasion is blocked for active feud/civil-war participants where it would conflict with reintegration.

### Claim Feud Lifecycle

1. A claimant builds political pressure and gathers supporters.
2. The ruler can hear the dispute while it is still political.
3. Parties accept or defy judgment.
4. Defiance can create temporary kingdoms for a contained private war.
5. Victory, concession, white peace, enforcement, or invalidation ends the conflict and reintegrates the houses.

The player initiates their own feuds through the Factions tab; AI ambition checks do not act on the player's inherited claims. NPC claims against the player remain valid. Petition closes once private war begins.

A supporting player can **Leave** before or during war. Wartime withdrawal returns the house and its possessions to the parent realm and creates a ten-year **Oathbreaker** memory: -20 with the former side's leader and -10 with its other participants. Leaders use their dedicated settlement rules.

### Settlement and Enforcement

Civil wars and feuds share scoring/readiness infrastructure with foreign wars but retain their own terminal handlers. White peace ends the dispute without judgment. Install Ruler, Abdication, and independence must reach the appropriate political resolution before generic cleanup.

Royal **Enforce Peace** applies to private claim wars, not open rebellions or grand coalitions. The player's Crown Action retains its foreign-war, eligibility, and influence requirements. An NPC ruler considers a crisis override when a substantial foreign attack threatens a realm distracted by feuds; advance notice precedes the decree. Enforcement returns the houses without awarding the disputed claim.

Close war stances before deleting feud records or temporary kingdoms. A legitimate realm destroyed by a third party during civil war uses a separate successor-recovery path.

### Post-War Tribunals

Eligible victors receive a tribunal; player judgment is deferred until the correct ruler has authority. Abdication waits for succession. Successful independence does not grant jurisdiction over the former liege: departing houses instead receive a -25 **War of independence** house memory lasting 15 years at the default multiplier.

Outcomes include pardon (+25 for 10 years), confiscation of the most prosperous fief (-15 for 20 years), eligible landless exile (-20 for 25 years), and execution. Execution invokes death consequences and does not automatically confiscate a fief. NPCs consider execution first (10% base, Mercy-adjusted), then landed confiscation (20% base, Generosity-adjusted), otherwise pardon. Base chances are configurable.

Faction reactions are additional to personal memories. Affected-member reactions use +10 for pardon, -10 for confiscation/exile, and -30 for execution, summed and capped at +/-30 per faction per tribunal; Liberty retains its separate clemency reaction. Check tribunal and shock handlers together when altering penalties.

## Titles, Claims, and Service

### Legal Hierarchy and Possession

Tiers are **Barony, County, Duchy, Kingdom, Empire**. Baronies bind to towns and castles; upper titles organize legal territory and service. Records retain identity, legal/actual parents, legal/actual holders, root status, settlement binding, claims, and custom names.

Displays use **(de facto only)** for possession without legal ownership and **(de jure only)** for legal ownership without possession. Full ownership has no suffix. The highest de facto title supplies displayed rank. A legal Crown can remain as a vacant historical root after its campaign realm disappears.

### Claims and Fabrication

Claims arise from fabrication, marriage, inheritance, dynastic succession, dispossession, and other legal transfers. They retain a carrier: strong claims can pass to the next generation as weak claims; weak claims expire with their carrier. Duplicate clan/title claims are normalized rather than stacked.

Marriage grants the receiving house strong claims to the spouse's parental-house titles through the shared claim service. Current marriage birthrights and possible future inheritances are separate matchmaking considerations.

Fabrication defaults to **three campaign years**, configurable from one to ten. Targets include lieges, neighbors, held territory, same-realm opportunities, and immediate child titles below a held title. A child captured into another realm can remain eligible.

| Target tier | Initial gold | Initial influence |
| --- | ---: | ---: |
| Barony | 50,000 | 50 |
| County | 75,000 | 75 |
| Duchy | 100,000 | 100 |
| Kingdom | 125,000 | 125 |
| Empire | 150,000 | 150 |

Checks occur at **33%, 66%, and completion**, using the same risk formula with current skills and council modifiers. Early failure presents a saved scribe inquiry: pay 25% of the original gold investment, repeat the affected stage without rerolling that failed checkpoint, or abandon. Final failure exposes the forgery; success grants a weak claim. NPCs choose using target value and reserves.

Abandonment and exposure impose one- and two-year same-target cooldowns. Technical invalidation retains refund rules. Tooltips distinguish stage risk from final success, not the chance of passing all checks without a setback.

**Renounce** removes a player's weak/strong claim to a title they do not control. It does not transfer land. Live claimant relation penalties disappear when their qualifying claim is gone.

### Usurpation, Revocation, and Transfers

Usurpation normally requires a claim, payment, and **more than half** of effective subordinate control units. `FeudalTitleUsurpationAssessmentService` finds the nearest active descendant tier, so a kingdom without duchies can still be evaluated through counties or baronies. A zero-child title has no artificial denominator. A legal holder may recover a vacant upper title without a redundant claim, subject to control and cost checks.

Claimed revocation requires immediate de facto liege authority, a different same-realm holder, a claim to the target, 100 influence, and no conflicting feud commitment. Defiance can start a revocation war. Legal ownership of a higher title alone does not create political authority over a foreign ruler.

Transfer handlers capture existing rights before synchronization. **Temporary Crown custody pending distribution must not create free claims or legal ownership**, including confiscated land. Dispossession preserves appropriate claims; voluntary grants and barter use their relinquishment rules. Barter can convey the seller's legal rights, not a third party's.

### Formation, Reorganization, and Drift

Formation normally needs at least two contiguous same-tier children. Detaching children from an existing legal parent requires full legal and actual control. Changes are validated before rebinding; eligible empty non-sovereign parents are then cleaned up.

Overlapping active feuds block holder-driven reorganization/dissolution. Ordinary claims, fabrication, and drift are not blanket vetoes. Disregarded claimants receive a ten-year **Disregarded our ancestral claims** house memory: -15 for weak rights or -30 for strong rights, once per affected house using its strongest claim. Secret fabrication is not exposed by confirmation. Automatic orphan cleanup remains more conservative.

`Rename` requires legal and actual control. `Dissolve` applies to eligible non-sovereign upper titles and reparents surviving children. Sovereign reorganization must preserve a viable original Crown and cannot take independent vassal structures; it can create a coequal Crown for later partition.

Drift defaults to **25 campaign years**. Temporary civil-war/feud membership does not reset permanent-realm progress. Each title has its own clock; a newly eligible parent does not restart children. Surviving titles keep history through reorganization; a newly formed upper title does not inherit a dissolved title's elapsed time.

Drift requires a suitable, fully controlled higher legal receiver. A title cannot drift into itself or an equal/lower rank. This preserves separate coequal Crown trees in a union. Blocked reasons appear in hierarchy tooltips.

### Service, Grants, and Naming

Immediate de facto links define liege/vassal service. Service Owed redirects subordinate income and affects summons costs/eligibility. `BellumFeudalClanFinanceModel` accounts for transfers without double-taxing village income. With restricted summons enabled, the ruler and Marshal can call the full host; other nobles use their hierarchy and permitted exceptions.

Player vassals can grant eligible baronies to companions through subinfeudation dialogue. The new house joins under the existing title structure. Possession-only grants preserve third-party rights; full legal grants carry the larger formation reward. Ruler companion grants use native dialogue with Bellum ownership handling. AI grants weigh land need and strategic/economic value.

Styles support holder, female holder, consort, landless house-head, mercenary-leader, and optional wanderer honorifics. `spouseRank`/`femaleSpouseRank`, `landlessLeaderRank`/`femaleLandlessLeaderRank`, and `mercenaryLeaderRank`/`femaleMercenaryLeaderRank` fall back to existing forms when omitted. Hero, party, army, and encyclopedia descriptions share styling services.

Realm rank follows the highest de facto title independently of the naming toggle. Sovereign-title naming uses the territorial title; otherwise known realms combine configured geography with rank. Explicit player renames survive presentation updates. Configuration precedence is documented below.

## Succession, Marriage, and Households

### Laws and Crown Accession

Realm laws have one saved selection per group: **Gender**, **Succession**, and **Elective Term Duration**.

| Group | Choices |
| --- | --- |
| Gender | Male Preference, Female Preference, Equal, Male Only, Female Only |
| Hereditary Crown succession | Primogeniture, Ultimogeniture, Seniority, Kinship |
| Elective Crown succession | Elective Seniority, Military Acclamation, Tanistry, Shura Council |
| Elective term | 1, 5, or 10 years; lifetime |

Hereditary accession follows the lawful personal line using saved transition records. A temporary transfer problem does not replace a valid heir. Heirless cases retain an emergency election. Realm-law profiles, personal Crown rights, and household estate sorting have explicit bindings; they are not interchangeable calculations.

An heir and their recorded incoming household are unavailable for new marriages while Crown settlement is pending. Ordinary future Crown heirs remain eligible. Hourly recovery can refresh an unexecuted household plan if the heir has already changed clan, retaining the original heir, Crown, cause, and laws. Once a cadet, payment, property transfer, or foreign-clan move has been committed, its saved receipts are preserved rather than rebuilding the package. `Behaviors/CrownHouseholdRecovery.cs` owns this guard and recovery path.

Elective realms maintain standing nominations and support during the ruler's life. Law merit contributes 60% and political preference 40%. Military strength plus influence determines standing voting power, not an expenditure. Weighted nominations produce up to three finalists; finalist-house self-support applies after nomination, subject to promises and player choices. Death, abdication, and term expiry freeze the final ballot. Acceptance and challenges are separate from candidate selection.

Term reform affects the sitting mandate. Fixed-to-fixed keeps its start date; lifetime-to-fixed starts at adoption; fixed-to-lifetime removes expiry. An already elapsed mandate schedules an election after notice/deliberation rather than immediately replacing the ruler. Loading an old save alone does not retrospectively change its mandate.

House inheritance uses the current realm's laws, cultural defaults for independent clans, and generic Male Preference Primogeniture as the final fallback. The clan encyclopedia shows the first heir and legal line. The player can disable legal-heir enforcement for playable succession without changing the world's legal rules.

### Regency and Adulthood

An underage lawful heir becomes a ward. An eligible adult relative acts as engine-facing clan leader; a generated cultural noble covers houses without a suitable adult. The ward takes leadership at adulthood. Regents receive appropriate styling and remain ordinary members after handover.

With legal-heir enforcement enabled, player regency allows control of a regent until a deferred, safe handover to the ward. Accession and partition remain regency-aware, so the acting adult does not become the hereditary beneficiary.

Custom adulthood defaults to **16**, configurable from 16 to 21. Education milestones default to **2, 5, 8, 10, 13, 15** and normalize into strictly increasing ages below adulthood. Completed stages are preserved. Disabling the option restores native ages; changes require restart. Marriage minimum ages are clamped to adulthood.

Strict gender-only laws exclude the opposite gender from title succession. If the lawful line is extinct, an engine-valid leader can preserve the house while title escheat follows its own rules. A living underage heir is not extinction.

### Strategic Marriage

Matching runs a staggered annual house search. **Both houses evaluate the actual household outcome**: continuity, fertility, members retained/lost, status, relations, useful alliances, political interests, and transferable claims. Healthy houses can prioritize strategic ties; endangered houses relax status demands and prioritize continuity. Sending a daughter or other member away is not inherently a veto.

A reproductive marriage sending a blood relative to another house adds up to 30 acceptance points for the outgoing kinship tie. This benefit scales down to zero at maximum household risk; it does not waive departure penalties or the other house's consent. It is shared by ordinary and royal matchmaking.

Ordinary household rules and special Crown/treaty outcomes determine who moves. A favorable match does not promise an automatic realm alliance or an unearned inheritance package. Player-house proposals still require the player's offer decision. Specific-marriage dialogue supports selecting both candidates and paginated large households.

A mutually acceptable ordinary match blocked by army duty or temporary unavailability can become a **30-day saved prospect**. Its first retry is scheduled for the following day, then every three days. Bellum reserves the pair against its other matches and rechecks legality, consent, and destination before execution. Invalid/expired prospects cancel. Court dynastic accords use their named couple and term deadline instead.

Matching shares short-lived house-health, kinship, claim, and political calculations. A pending prospect retries its pair without repeating the world search.

### Cadet Households and Mercenary Companies

Marriage preserves personal Crown rights without immediately creating a cadet house. Later accession can require a separate ruling house; the saved plan handles household and estate delivery. Existing children do not automatically move with an incoming Crown heir. Separate-household newborns follow their mother's current clan. Cadet banners preserve parent geometry and the intended palette.

Dynamic mercenary evaluation defaults to once every **five years per house**. Adventurous adults not due to inherit may join a cultural company or found one. Defaults are **10 companies**, three adult officers, and starting tier two. Zero prevents new departures/formations; lowering the cap does not destroy existing companies. Companies use local troops and configurable cultural names.

Player relatives can depart too. In-party dialogue offers financial support (25,000 denars), blessing, persuasion, or refusal with possible defiance. Away relatives send a letter. Company descriptions preserve known founding history rather than inventing missing ancestry.

## Personal Unions and Realm Partition

### Inheriting a Second Realm

A union can occur when an **already-ruling lawful heir** inherits another Crown through a supported hereditary death accession. Both realms must be permanent, independent, hereditary, and possess distinct, fully held coequal political Crowns. Elective elections, abdications, emergency successions, and arbitrary diplomatic mergers are outside this route.

The heir's existing realm survives. The inherited realm's noble houses join peacefully, mercenary contracts end, and the source kingdom is retired only after delivery is verified. The survivor keeps its government, policies, court, and primary sovereign identity. The inherited Crown retains its legal vassal tree. Equal-rank drift remains prohibited.

An heir who does not yet rule a foreign Crown follows accession/household handling rather than automatically absorbing their current host realm. Eligibility to inherit two Crowns is not itself a union: the accession must actually occur.

### Absorption Sequence and Recovery

[RealmUnionSnapshotService.cs](RealmUnionSnapshotService.cs), [RealmUnionSequence.cs](RealmUnionSequence.cs), and the `CrownRealmUnion` partial files own this path:

1. Validate accession, sovereignty, source membership, title ownership, military availability, and political conflicts.
2. Capture Crowns, title parents/holders, clans, holdings, influence, debt, banners, and diplomatic obligations.
3. Preflight and transfer compatible agreements through their specific adapters.
4. Transfer and verify the inherited Crown.
5. Move noble houses, terminate mercenary contracts, and restore recorded clan state where native moves alter it.
6. Verify source emptiness and obligations before retiring the kingdom shell.
7. Reconcile the outer accession and publish the localized union announcement once.

Compatible alliances, trade agreements, tribute, and client commitments can pass to the survivor. Conflicts defer the union; they are not silently discarded. Foreign war alignment, client subordination, active decisions, armies, sieges, and internal conflicts can block preparation or completion. Any non-ended hostage pact involving a participating realm also defers preparation; the current route does not reassign active pledges to a new sovereign.

`RealmUnionRecord` distinguishes **started**, **returned**, **verified**, **announced**, and **completed** work. An interrupted native transfer must be reconciled against current state. Do not clear its receipt and replay it as if nothing happened. Announcements are chat messages, not an extra mandatory decision popup.

### Later Inheritance Splits

Partition can separate fully held coequal Crown packages among eligible heirs. Secondary Crowns take allocation priority in the journaled Crown batch. A **fully held common higher legal ancestor** binds those Crowns together; an unrelated higher title does not. The legal vassal tree stays with its assigned Crown.

Successor realms inherit applicable laws/policies and recorded ongoing foreign enemies. **Alliances, trade agreements, tribute, and client states stay with the surviving primary realm.** Hostage pledges are not copied: a non-ended pact defers this preparation until its custody lifecycle is resolved. This deliberately differs from absorption, where compatible source commitments can transfer to the survivor.

The Crown batch captures estate shares, residence/allegiance plans, households, titles, holdings, treasury delivery, and government/court work. Completion verifies the batch before finalizing and announcing separation. Holding two Crowns does not guarantee a split: heirs, bindings, residence, legal control, and delivery must all qualify.

### Ordinary Estate Partition

The older landed-estate route remains distinct. It can promote a coequal inherited title below kingdom rank, such as a sovereign duchy, when spare inheritable fiefs remain after the primary heir's reserve and the secondary heir receives a qualifying title and holding. Its guard checks whether the parent holds any higher title fully, rather than using the Crown batch's common-ancestor test.

Do not assume that the journaled Crown batch's recovery guarantees cover this older route. Relevant code is in `FeudalInheritancePlanner.cs`, `PartitionSuccessionBehavior`, the `CrownPartition` partial files, and estate delivery/settlement helpers. Changes to union or partition handling should be checked in native campaigns and across save/load, alongside isolated contract tests.

## War and Peace

### War Will and Enemy Selection

The **War and Peace Revamp** is enabled by default. War Will expresses a clan's enthusiasm; targeting chooses whom it wants to fight. War Score measures leverage; treaty utility evaluates a particular settlement.

War Will starts around **45**, recovers in peace, and responds to ideology, losses, captures, events, and simultaneous fronts. All ideologies can eventually reach 100. Ordinary war proposals require 75; an additional foreign front raises the effective requirement to 90 and adds combined-danger checks. The target sanity floor is 10.

Motives include claims, cultural territory, frontier/maritime access, relations, treaties, marriage, prosperity, vulnerability, and all existing fronts. Military opportunity/danger can reach +/-35; a formal alliance contributes -50. The strongest aggregated motive supplies declaration/vote argument text. Temporary feud realms are not normal foreign targets.

StoryMode's protected conspiracy wars are excluded from passive fatigue, extra-front opening penalties, and positive-shock damping. Combat losses and strategic danger still count. Quest-required total victory remains protected from ordinary settlement.

### War Score Components

`WarScoreBehavior` maintains a signed **-100 to +100** ledger from the attacker's perspective. Civil wars, feuds, and foreign wars share scoring infrastructure and have different terminal handlers.

| Component | Baseline |
| --- | --- |
| Occupied town / castle | 15 / 10 |
| Retaken core fief | +5 objective value |
| Village raid | 1; component capped at 15; each village credited once per side per conflict |
| Battle | Severity-scaled, with major-battle weighting; component capped at 40 |
| Captive noble / heir / ruler | 2 / 8 / 15; component capped at 25 |
| Ordinary ticking leverage | 0.1 per day to the advantaged side, capped at 25 |
| Feud objective | Replaces ordinary ticking: 1 per completed uninterrupted day of control, capped at 50 |
| Landless-war pressure | Separate pressure for realms starting without fiefs; prevents immunity through an empty occupation denominator |

Prisoner leverage reflects **current qualifying custody**. Release, escape, rescue, or death removes it. Feud control loss clears the former holder's objective accumulation; the next controller starts at zero. Repeated capture/raid cycles cannot repeatedly award the same objective shock. Collapse and conflict objectives have additional terminal handling.

### Peace Readiness

There is **no fixed minimum war duration**. The configurable **War Duration Reluctance Period** defaults to 100 days. [PeaceReadiness.cs](PeaceReadiness.cs) centralizes willingness; [BellumCivileConstants.cs](BellumCivileConstants.cs) supplies thresholds.

With elapsed days `d`, enthusiasm `e` clamped to 0-100, absolute score `s`, reluctance period `P >= 1`, and `clamp01(x)` limiting a value to 0-1:

```text
r = clamp01(e / 10)
Exhaustion           = (20 - e) * 0.5 + 35 * (1 - r)
Early-war reluctance = -max(0, P - d) * (100 / P) * r
Inconclusive war     = -30 * clamp01((50 - s) / 50) * r * clamp01((90 - d) / 60)
Prolonged exhaustion = +30 when d >= P and e < 10, otherwise 0
Readiness            = sum of these four components
```

The early penalty fades with age and is softened below 10 enthusiasm. Prolonged exhaustion is a **one-off utility contribution**, not an accumulating daily bonus. Individual evaluation adds terms and politics; readiness alone is not a yes vote.

Automatic mutual white peace requires absolute score **<=10**, both strength-weighted effective enthusiasm values **<=10**, and readiness **>=11** on both sides. Ordinary internal concession requires winner-relative score **>=50**, loser enthusiasm **<10**, and readiness **>=11**. After the reluctance period, score **>10** and zero loser enthusiasm can permit concession, avoiding an indefinite 11-49 stalemate. Decisive 100-score outcomes, enforcement, invalidation, and scripted protections retain their own paths.

### Peace Parley and Ratification

`ForeignTreatyBehavior` owns opening, AI drafting, council assessment, ratification, execution, and cleanup. The Parley displays both councils, score, terms, commitments, ruler choices, and predicted outcome.

- Rulers edit **Fiefs, Prisoners, Politics, and Wealth** demands/offerings and choose Accept or Reject.
- Vassals vote Yay, Nay, or Abstain, with 25/75/150 influence commitments; they cannot edit terms.
- Mercenaries do not participate in sovereign votes; AI resolves those proposals.
- Overrides cost influence and have relation consequences. Rejection reactions follow clans' actual position on the treaty, not an automatic reward for refusal.
- There is no minimum-participation quorum. With no votes, an NPC ruler uses their own assessment and a player ruler decides explicitly. No opposition means no override payment, not exemption from legal/budget checks.
- Internal wars expose restricted white-peace/concession outcomes, not general foreign territorial bargaining.

Rejected voluntary foreign treaties have a **seven-day minimum automatic retry window**, with early reconsideration for significant military changes. Expiry does not force a proposal. Explicit player negotiation and forced settlement remain separate. Repeated penalty application is guarded.

### Budgets and Demands

Players can assemble excessive drafts. The opposing council receives **Enemy making unreasonable demands: -1000**, with excess shown in the UI. **Final ratification/execution rejects over-budget settlements**, including capitulation and ruler overrides. Repair may remove invalid terms but must not silently trim valid excessive player demands.

Offering credit equals half the offered value, capped at half the side's base War Score and at an absolute 50 additional points. Excess offerings can improve recipient utility without increasing budget. The side-relative calculation is shared by players and NPCs.

| Term | Baseline cost |
| --- | --- |
| Occupied castle / town | 20 / 30 War Score |
| Unoccupied fief | Occupied cost multiplied by 1.5 |
| Fief prosperity | +1 per 500, capped at 12,000 prosperity; claims can discount cost |
| Noble prisoner | 5 base, with leader/heir/ruler premiums |
| Concede Defeat / Discredit / Humiliate | 10 / 20 / 30; mutually exclusive prestige tiers |
| End Trade Agreement / End Alliance | 10 / 25 |
| Royal Marriage | 30 |
| Reparations | 5,000 denars per War Score by default |
| Tribute | 250 denars daily per War Score for 100 days by default |

Prestige tiers transfer renown/influence: Concede +/-15 and +/-30; Discredit +/-30 and +/-60; Humiliate +/-50 and +/-100. The latter tiers add ruler-relation fallout. Other terms cover vassal release, vassalization, active rebel demands, client release/clientage, and hostage pledges. Size-scaled costs prevent cheap absorption of large realms.

NPC winners target earned leverage and compare compromises through both councils. Affordable reparations/tribute use remaining granular budget. Winners penalize unused actionable leverage beyond `max(5, ceil(10% of budget))` at 1.5 utility per point. Reserves, collective wealth, and the full tribute burden bound affordability. Exhaustion does not validate an impossible financial demand.

### Territory and Ordinary Prisoners

The Fiefs picker supports **Recognize {CLIENT}'s possession of {FIEF}** for eligible client occupations. Linked dungeon prisoners contribute to a fief term's cost. Removing a linked prisoner removes that fief term; prisoners can also be selected independently.

Already distributed occupations are not sent through a second allocation vote. Unoccupied transfers use temporary-Crown custody and normal distribution. Garrison transitions must remove hostile ownership safely.

Unexchanged foreign nobles can remain captive after peace under retained-prisoner handling. Dungeon/mobile escape chances default to 1%/3%. The household broker can arrange relative/companion ransom, and clan tooltips explain available routes. Treaty summaries group land, payments, politics, and prisoners; native peace announcements remain.

### Hostage-Backed Pacts

Treaties can demand or offer a blood relative of the supplying ruler for **100 days by default**. Children are eligible. Succession standing supplies four value tiers; voluntary pledges use shared half-value offering credit.

The **Hostage Pact Duration (Days)** MCM slider ranges from 0 to 1,000 and applies when a new pledge is drafted. Reciprocal pledges share that duration, which is saved before handover and retained through delivery recovery. Existing pacts keep their saved expiry; older records retain their original 100-day agreement. Setting 0 disables new pacts for players and NPCs without invalidating existing or pending agreements. A zero-day pact, if signed, returns its hostages on the next daily tick.

Hostages remain in their own house but enter protected custody in an eligible captor stronghold. Ordinary escape, ransom, and manual release are blocked. Encyclopedia/clan descriptions identify treaty custody; the broker explains why ordinary ransom is unavailable.

Expiry or replacement of either recorded ruling house ends the arrangement through its return lifecycle. War removes protection. NPC captors choose release, ordinary captivity, or execution using personality, aggression context, and chance; player captors receive a choice. Retained captives resume escape, ransom, rescue, and prisoner-score rules. Pacts discourage attacks rather than absolutely prohibiting them.

Dungeon dialogue permits honorable early return or dishonorable execution with confirmation, relation memories, and honor-trait effects. Reciprocal hostages use the other ruler's response, not guaranteed release. Protected hostages must not reach ordinary release dialogue that awards relations despite failing to release them.

### Client Kingdoms

Clients maintain a forced alliance/trade agreement, follow their suzerain into wars/peace, and cannot independently pursue conflicting diplomacy or ordinary wars. Liberation requires the clientage's one-year cooldown to expire, realm and proposing-clan Liberty Desire of at least 60, and at least 100% strength readiness. Readiness uses clan military strength plus influence: client clans contribute 50%, 75%, or 100% at desire below 40, 40-59, or 60+, against the suzerain plus half the power of its other clients and allies, counted once each. The required client-to-bloc ratio starts at 80% and follows the existing ruler-personality adjustments. Suzerain collapse releases clients.

Liberty Desire combines submission type, lawful suzerainty through the title hierarchy, culture, opinion, marriage ties, court faction, personality, and active Crown preparations. The realm average is weighted by clan military strength plus influence. For liberation proposals and council votes, each clan gains 0.75 resolve per personal desire point above 60, capped at +30. Effective willingness is War Will plus resolve, capped at 100; proposals require 75. Resolve is calculated on demand and does not alter stored War Will or peace evaluations. NPC preparation viability uses the same formula with the projected +20 desire bonus. Influence costs, reserves, strategic target requirements, and the declaration vote still apply. `BuildDebugReport` lists actual War Will, resolve, and effective willingness for each clan.

The map widget and Kingdom Diplomacy client entries share `ClientLibertyTooltip`. It shows the current blocker, weighted desire causes (including individual 0-100 limits), client and opposing-bloc power, the required power ratio, and the leading prospective proposer's desire, War Will, resolve, willingness, and influence budget. Hovering refreshes the assessment in either view. Proposal previews use `PeekWarWill`, read-only influence assessments, and targeted strategy scoring without publishing AI preferences or recording spending attempts. Strategy/permission checks are deferred until the realm and a candidate clear the cheaper gates; council approval remains a separate decision.

The Crown can grant an eligible personally held barony to a client, excluding pending distribution. This is direct for player rulers and rare/situational for NPC agendas. The client ruler receives +25 relations for ten years at the default memory multiplier, capped at +50 from active grants. Only the donor's own legal rights accompany the land; higher titles and historical boundaries remain intact.

A weak threatened realm can seek protection; acceptance includes joining its defensive war. Liberation preparation encourages the existing path rather than bypassing declaration rules.

Client peace propagation and occupation recognition exist. **Full shared-war contribution accounting and independent-allied negotiation/consent are not implemented as one combined war system.**

## Relations and Personal Politics

### Layers and Scope

Relations are calculated for eligible individual hero pairs, including regular nobles, same-house relatives, and notables. Eligibility requires living, enabled, non-child characters and excludes a character's relation with themselves. Additional clan checks filter unsupported minor factions and some pairs of independent non-player houses. `DynamicRelationBehavior.ShouldAffectPair` and `IsEligibleHero` are authoritative; individual support does not mean every possible character pair is eligible.

House-scoped events remain attached to the houses and survive changes of leader. Same-clan and notable pairs use personal event scope rather than applying a house grievance against itself. True Noble Opinion integration preserves individual identity.

Noble relations combine a natural foundation, live political conditions, and memories. Foundation includes personality/culture and family bonds. Conditions include claims, title relationships, court alignment, wars, and captivity. Notable pairs use event memories without the noble foundation/political baseline. The implementation does not generate a global table of every hero pair.

Only one applicable family bond contributes: parent/child **+20**, siblings or spouses **+15**, close extended kin **+5**. Marriage's live bond is separate from the temporary native wedding reward. Charm affects applicable event gains through normal paths rather than a permanent baseline.

Contested claims give one non-stacking pair penalty: **-10 weak** or **-20 strong**. De jure title-desire conditions can accumulate by title. Prisoner possession ends on release; donations use a separate memory capped at +20.

### Lifetime and Migration

Ordinary timed memories retain full value until expiry. Named contexts cover quests, rescues, battles, losses, appointments, promises, and other recognized events. Unrecognized native/third-party changes use a generic timed fallback.

The duration multiplier is **0.25x-5x**, default 1x. New memories scale centrally. Changes rescale remaining duration, not elapsed history; permanent conditions and expired records are unaffected. The saved last-applied multiplier prevents double scaling. Durations use campaign years (`CampaignTime.DaysInYear`).

Two migration concepts must remain distinct:

- **Prior personal history** is temporary, with base duration `abs(value) / 3` years, bounded to 0.25-10 years before scaling. Migration preserves the immediate total and prevents expired history from being recreated.
- Legacy **Prior history** from the drifting model retains migration-only fading so loading does not abruptly change visible relation.

`RelationMemoryService` owns contexts/durations. `DynamicRelationBehavior` owns records/integration; `DynamicRelationBaselineHelper` owns natural/political calculations. Foundation and political caches are bounded and separate from durable memories. Expiry uses indexed pairs and lazy checks.

### Intrigue and Influence

Player rulers can send companions on clandestine missions. NPC subterfuge uses staggered checks. Operations affect court mood, rebels, money, troops, and exposure. The Spymaster is the intrigue proxy when available; otherwise the ruler is. Captured/disabled agents and ended conflicts require cleanup.

`NpcInfluenceBudgetService` centralizes voluntary AI spending. Base reserves are **200 for clans** and **500 for rulers**, plus 200 during foreign war for rulers/army leaders. Discretionary actions retain the larger of the role reserve or action cost; council commitments retain the role reserve; Crown emergencies use a smaller floor. Involuntary losses and player spending do not use this policy.

## Interfaces and Localization

### Kingdom and Map Screens

| Surface | Implementation and content |
| --- | --- |
| Factions | `UI/VanillaTabs/Kingdoms/Factions/`; court, Council, rebel/feud groups, Crown Actions, and mandates |
| Hierarchy | `UI/VanillaTabs/Kingdoms/Hierarchy/`; legal/actual trees, claims, service, and selected-title action dropdown |
| Succession Laws | `UI/VanillaTabs/Kingdoms/Succession/`; inline Policies panel, laws, heirs, and elective standings |
| Peace Parley | `UI/Parley/`, `GUI/Prefabs/Parley/BellumPeaceParley.xml` |
| Diplomacy Overview | Wars, native alliances, Bellum hostage pacts, and clients in a bounded scroll area alongside Stats |
| Map widgets | `UI/Map/`, `GUI/Prefabs/Map/`; War Score and Liberty Desire, independently configurable |

Overview hovers reuse score, alliance expiry, custody/duration, and Liberty Desire services. Direct client/suzerain relationships appear only under Client States instead of duplicating forced alliances. War widgets follow the player's actual conflict shell and include only relevant feuds.

Extensions add titles, offices, claims, relations, succession, policy tints, and prisoner guidance around native nodes. Preserve dimensions and hitboxes. Passive location/tooltip overlays must not block member selection. Refresh existing ViewModels where possible instead of recreating widgets every tick.

### Encyclopedia Concepts

[ModuleData/bellum_concepts.xml](ModuleData/bellum_concepts.xml) contains **31 articles**, including the overview. The manifest registers native `Concepts` XML for `Campaign` and `CampaignStoryMode`. [Patches/EncyclopediaConceptFilterPatch.cs](Patches/EncyclopediaConceptFilterPatch.cs) adds **Bellum Civile** to the existing Types filter rather than a separate intersecting group.

The overview links to all specialist articles and each links back. Titles, bodies, and filter have **63 English localization entries**. Native pages supply scrolling, bookmarks, and navigation. Articles explain gameplay in the native conceptual style; implementation details belong here.

### Localization Contract

Player-facing text uses stable `TextObject` IDs with matching English in [ModuleData/Languages/EN/strings.xml](ModuleData/Languages/EN/strings.xml). Preserve named variables and concept-link tokens in translations. Configurable title/name presets have separate presentation data and patch support.

When changing text, update both fallback and source entry. For concepts, also update shipped article XML and verify links. Runtime articles are self-contained; the private manuscript under `docs/` is not loaded by the game.

Use `BellumCivileNotifications` for optional political chat messages. Its Disabled, Kingdom Only, and Global settings filter ordinary events; explicitly personal messages still appear, and major events also appear under Kingdom Only. A required inquiry or pending decision is a separate interaction and must not depend on optional chat visibility.

## Configuration and Extension Points

### MCM and Defaults

[BellumCivileSettings.cs](BellumCivileSettings.cs) declares MCM. Runtime callers use [BellumCivileOptions.cs](BellumCivileOptions.cs) for safe access/defaults. [BellumCivileConstants.cs](BellumCivileConstants.cs) contains compiled balance values. Do not expose a toggle solely to preserve an obsolete implementation detail.

| Group | Important defaults |
| --- | --- |
| Presentation | Anglicized titles; sovereign-title realm names off; both widgets on |
| War/peace | Revamp on; reluctance 100 days (1-500); renewed-war peace interval 20 days (0-500), with client-liberation exemption |
| Hostage pacts | Duration 100 days (0-1,000), fixed per drafted agreement; 0 disables new pacts without shortening existing ones |
| Treaty wealth/captivity | 5,000 gold or 250 daily tribute per point; dungeon/mobile escape 1%/3% |
| Court | One-year terms (0.25-4); five-day deliberation; rebellion threshold 100; automatic treason indictments on |
| Titles/armies | Three-year fabrication; minimum two children for formation; restricted summons on; friend exception +60 relation |
| Succession | Partition on; primary heir reserves one fief; player legal-heir enforcement on |
| Childhood | Custom adulthood on at 16; education 2/5/8/10/13/15 |
| Marriage | Strategic matching and Bellum-only NPC marriages on; ages respect adulthood |
| Mercenaries | Limit 10, five-year evaluation, three officers, starting tier two |
| Relations | Memory system on; duration 1x (0.25x-5x) |
| Intrigue/notices | NPC subterfuge on; notifications Kingdom Only; debug messages off |

Some saved/internal names retain older terminology, such as `EnableDynamicRelationDrift`. Read current code before interpreting them as the retired weekly-drift system.

### Data and Load Order

| File under `ModuleData/` | Purpose |
| --- | --- |
| `bellum_feudal_titles.xml` | Base hierarchy, records, and default styles |
| `bellum_title_styles_*.xml` | Selectable presentation presets |
| `bellum_feudal_titles_patch.xml` | Title/style overrides from loaded modules |
| `bellum_policy_agendas.xml` / `bellum_policy_agendas_patch.xml` | Crown interests and bloc stances |
| `bellum_laws.xml` | Grouped law definitions |
| `succession_config.xml` | Realm/cultural succession defaults |
| `bellum_realm_names.xml` / `bellum_realm_names_patch.xml` | Geographic roots and native-name guards, independent of rank |
| `dynamic_mercenary_names.xml` / `dynamic_mercenary_names_patch.xml` | Cultural/generic name pools and overrides |
| `bellum_concepts.xml` | Native encyclopedia definitions |
| `Languages/EN/strings.xml` | English localization |

Title configuration loads base hierarchy, selected preset, then active-module title patches. Shipped presets include Anglicized, Immersive, and BannerKings-compatible terminology; the latter is a style preset, not a promise of full BannerKings gameplay compatibility. Additional matching files are discovered for MCM. Restart after changing selection.

Styles scope to kingdom, ruler culture, or holder culture, in that priority order. Temporary realms inherit the parent's style. Realm-wide court/council terminology uses kingdom, ruler culture, then realm culture. `CourtFactions` and `CouncilOffices` blocks customize institutional names, including finance/UI text. Realm-name patches can guard against overwriting unrelated map-mod identities.

Extensions use settlement, title, kingdom, culture, and clan IDs. Use structured XML parsing; display-name matching is not a stable API.

## Persistence and Recovery

[BellumCivileSaveDefiner.cs](BellumCivileSaveDefiner.cs) reserves base ID **8456123**. Behaviors serialize through `SyncData`; records and nested containers need explicit registration.

Saved families include court affiliations/agendas, council state, titles/claims/drift, conflict membership/cleanup, war score/custody, treaties/payments/pacts/clients, delayed decisions, laws/elections/accessions, heirs/regencies, estate/union journals, marriage prospects, mercenary history, and relation memories/migrations.

- Keep type, enum, and field identities stable; never reuse retired IDs.
- Register new records and required nested containers. Compilation does not prove serialization.
- Make migration idempotent and tolerant of partially initialized state. Rebuild transient indexes from saved authority.
- Preserve healthy heirs and legal ownership. Cache expiry is not permission to reroll succession or reseed rights.
- Record intent before irreversible native actions and completion after verification. Recover partial results rather than blindly retrying.
- Distinguish invalidation from defeat, neglect, or a broken promise.
- Test fresh and supported upgraded saves. Back up campaigns before migration tests; backward loading into older versions is not guaranteed.

Union and Crown-partition journals are especially sensitive: native clan moves, kingdom creation/destruction, diplomacy, and estate delivery can emit further campaign events. Read the complete sequence and reconciliation helpers before adding a mutation.

## Optional Integrations

### Diplomacy

Diplomacy is a soft integration and should load above Bellum. Bellum redirects its Factions button and supersedes the overlapping Overview while retaining compatible statistics/actions.

With the revamp on, disable Diplomacy settings independently controlling overlapping exhaustion, proposal scheduling, or civil-war systems as identified by `DiplomacyCompatibilityBehavior`. In particular:

- **Non-Aggression Pact Duration in Days: 0**
- **Non-Aggression Pact Tendency: -100**
- Diplomacy's minimum war duration: **0** when using Bellum's duration utility

The warning waits until campaign UI is ready and returns on load while conflicts remain. Bellum reports incompatible settings; it does not change another mod's configuration. Reflection-based compatibility is build-sensitive and requires testing against supported external versions.

### Relations, Economy, and Banners

True Noble Opinion integration retains hero-pair identity, including regular members/family. Verify displayed values, events, and save/load with and without it.

HAP uses external requisition for Stockpile Provisions instead of direct food creation: up to 2% of eligible market food, subject to HAP capacity/supply. Bellum pays administration; HAP owns the food tooltip. Construction retains Bellum's modifier. See `CouncilAssignmentHapIntegration.cs`, `CouncilAssignmentRuntimePatches.cs`, and `BellumCouncilAssignmentModels.cs`.

`KingdomVisualHelper` preserves recognized custom banners and limits recoloring to appropriate paths. Cadet/rebel banners copy geometry before changing palette. Test banner integrations with generated houses and realm transitions.

### Naval DLC / War Sails

The `BellumCivile.NavalDLCPatch` project references Naval UI assemblies. Core startup registers its bundled DLL only when DLC is available. Core must remain buildable/loadable without Naval DLC.

The companion also retains a separate module manifest declaring `v1.0.33`. Review that metadata if distributing a separate module. The core manifest does not update it; do not package both activation layouts without verifying registration behavior.

## Build and Packaging

### Prerequisites and Commands

Build on Windows with a .NET SDK capable of SDK-style projects, .NET Framework 4.7.2 targeting support, target game assemblies, and installed Harmony/UIExtenderEx dependencies. NuGet supplies compile-time MCM, currently `Bannerlord.MCM` **5.11.4**. The lightweight Court harness separately targets **.NET 9**.

`Directory.Build.props` supplies a shared `GameFolder` default based on Windows' Program Files (x86) location and the standard Steam library layout. It is independent of where the repository is cloned. An explicit `-p:GameFolder` takes priority over the `GameFolder` environment variable, which in turn overrides the default.

From the repository root, select the installation below. For a custom Steam library or another storefront, replace `$game` with that installation's absolute path. Setting the environment variable also configures the PowerShell tests and engine harness for this session:

```powershell
$game = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Steam\steamapps\common\Mount & Blade II Bannerlord'
$env:GameFolder = $game
dotnet restore BellumCivile.csproj -p:GameFolder="$game"
dotnet build BellumCivile.csproj -c Release --no-restore -p:GameFolder="$game"
```

For the companion, with Naval assemblies installed:

```powershell
dotnet build BellumCivile.NavalDLCPatch/BellumCivile.NavalDLCPatch.csproj -c Release -p:GameFolder="$game"
```

Its build copies the DLL/PDB into the main output. The solution builds both projects; use the core project directly on machines without Naval references.

### Runtime Package

Compilation is not deployment. The core project lists content but does not automatically assemble a full module. Prepare `Modules/BellumCivile/` with:

```text
BellumCivile/
  SubModule.xml                  <- _Module/SubModule.xml
  LICENSE.txt                    <- repository license
  bin/Win64_Shipping_Client/
    BellumCivile.dll              <- bin/Release/net472/
    BellumCivile.NavalDLCPatch.dll <- bundled integration, when shipped
  GUI/                           <- project GUI assets
  ModuleData/                    <- configuration, concepts, and Languages
```

Ship manifest, XML, localization, and prefabs from the DLL's revision. A rebuilt DLL alone does not install Concepts articles. Do not redistribute game/dependency assemblies merely because they appear in test output.

`main` is the development branch. Generated `bin/` and `obj/` outputs, local IDE/tool settings, and private research under `docs/` are excluded from source control. Keep credentials, saves, and logs out of commits as well. Build runtime packages from the tracked source and assets; a source checkout or Git push does not install or publish a packaged release.

## Verification and Diagnostics

### Regression Checks

The repository includes focused checks and harnesses for maintaining the released systems. Choose the relevant layers when changing code:

| Layer | What it establishes | Boundary |
| --- | --- | --- |
| Core/companion build | Compilation against installed references | No campaign transitions or serialization |
| `Tests/*.Tests.ps1` | Focused rules, wiring, and compiled helper checks | Many use doubles or source assertions |
| `Tests/CourtRedesignHarness` | Pure-rule checks and simulations | .NET 9, not the running game |
| `Tests/SuccessionEngineHarness` | Contracts against installed game types and Bellum services | Not a native save roundtrip |
| In-game testing | Menus, events, persistence, integrations, long-run balance | Observer runs do not cover every player branch |

Examples from the repository root, using the `$game` path above:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/RealmSovereignty.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/GamePath.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/ChildhoodEducation.Tests.ps1 -GameFolder "$game"
dotnet run --project Tests/CourtRedesignHarness/CourtRedesignHarness.csproj
dotnet build Tests/SuccessionEngineHarness/SuccessionEngineHarness.csproj -p:GameFolder="$game"
dotnet run --project Tests/SuccessionEngineHarness/SuccessionEngineHarness.csproj --no-build -- "$game"
dotnet run --project Tests/SuccessionEngineHarness/SuccessionEngineHarness.csproj --no-build -- "$game" --title-name-cache
```

Keep name-cache checks isolated because Harmony/JIT fixtures can affect other checks. Record results with the revision/environment rather than maintaining a stale passed-check count here.

The engine harness accepts an installation path as its first positional argument, ahead of the `GameFolder` environment variable and the standard Steam default. `ChildhoodEducation.Tests.ps1` accepts `-GameFolder` with the same precedence. Court prototypes resolve Sandbox data through `GameFolder`; their existing `-ModuleData` parameter overrides that location. Prototypes supporting `-SyntheticOnly` can run without game data.

`Tests/EncyclopediaConcepts.Tests.ps1` also compares text with the manuscript through `Tests/ConceptArticleSource.ps1`. It needs `docs/encyclopedia-articles-draft.md` and **cannot run unchanged in a clean clone without that private file**. The engine harness separately checks shipped Concepts XML/filter. Runtime has no manuscript dependency.

### Logs and Console Tools

Logs live under the user's Documents folder:

```text
Mount and Blade II Bannerlord/Configs/ModLogs/
  BellumCivileYYYYMMDD.log
  BellumCivileYYYYMMDD_1.log ...
  bellumcivile_yearlyreports.txt
```

`BellumCivileLogger` rotates ordinary logs at about 5 MiB and prunes old dated logs after five days. Capture all numbered segments before expiry. Correlate yearly war/succession summaries with detailed transitions and campaign identity. Accelerated runs can skip telemetry opportunities; missing samples alone do not prove a mechanic failed.

Treaty openings are counted by creation date; applied, rejected, and cancelled outcomes by their saved resolution date. Pending parleys are a current snapshot, and average war score spent includes applied treaties only. Older resolved proposals without a resolution timestamp are not assigned an invented year. Crown-heir regency creation and replacement contribute to the same succession counters as ordinary regencies.

[CheatCommands.cs](CheatCommands.cs) contains diagnostics. Useful entries include `civilwars.make_me_king`, `civilwars.pending_votes`, `civilwars.repair_pending_votes`, `civilwars.relation_breakdown`, `civilwars.relation_stats [reset]`, and `civilwars.title_name_stats [reset]`, plus title, marriage, succession, treaty, client, and tribunal tools. Prefer commands exercising production services and reporting failed preconditions over direct record mutation.

`Patches/KingdomManagementTabDiagnosticsPatch.cs` emits `KTAB-*` only with debug notifications enabled and a settled UI failing to bind. It reports relevant mixin/provider/command and optional Diplomacy state while remaining silent when healthy.

### Performance Boundaries

Existing optimizations focus on repeated reads without changing political rules:

- Hero-name caches retain titled/untitled results with household/rulership context and a separate display revision.
- A reverse hero-to-realm Crown-heir index avoids scanning every realm for non-heirs.
- `TreatyDraftReadScope` reuses heir/prisoner/fief reads within one synchronous search; execution validates fresh state.
- Marriage searches share local health/kinship/claim data; prospects retry only their pair.
- Relation caches are bounded; expiry uses indexed checks and stable compaction.

Do not retain search caches through settlement or serialize derived caches as authority. Invalidate on relevant events and keep campaign work staggered/event-driven. Avoid world scans in application/mission ticks.

Measure performance changes with matched saves, modules, speed, and duration. Distinguish inclusive profiler samples from measured savings. Normal and accelerated runs answer different questions.

## Maintenance

### Change Discipline

- Update authoritative services, then callers, reasoning, persistence, and focused tests.
- Read all partial files before adding a scheduler or recovery path.
- Scale tests to risk: ownership, relations, diplomacy, and succession need cross-system checks.
- Use structured parsers, cache optional reflection, and prefer exact Harmony signatures where stable.
- Keep visible text localized and technical diagnostics actionable.
- Update this guide when rules/ownership change and the changelog for release-facing changes.
- Preserve unrelated work and legitimate campaign history during repairs.

### Extending a System

| Change | Places to review together |
| --- | --- |
| New court motion | Objective source and selector registration, saved target/state, player selection, execution, success/failure/invalidation, payment/refund, and localized descriptions |
| New council assignment | Assignment registry, runtime effects, model hooks, funding and competence scaling, incidents, and tooltips |
| New treaty term | Term identity and persistence, eligibility, score cost and accounting, AI drafting, council utility, player editing, final execution, and rollback/recovery rules |
| New relation memory | Stable source ID, personal/house scope, event context, duration and stacking rules, localization, and migration where needed |
| New title or naming option | XML loader and precedence, ownership versus presentation, caches, tooltips, existing-save refresh, and player renames |
| New transition stage | Saved intent/result fields, container registration, preconditions, native side effects, verification, recovery, and exactly-once outcome reporting |

A court objective must be registered with `CourtAgendaBehavior.CreateObjectiveSelector` and use the shared agenda contracts. Adding only a menu option or a weighted candidate does not implement its lifecycle. Likewise, every treaty draft needs final validation against current state even if its picker previously declared the term valid.

For bug reports, begin with the owning behavior's saved record and the corresponding action log. Establish the last verified stage and its current preconditions before changing state. A deferred journal may be correctly waiting for a siege, decision, hostage lifecycle, or conflicting commitment; it is not automatically a failed scheduler. Reproduce and distinguish that wait from a lost callback before adding recovery code.

## License

Bellum Civile by **Vonbalt** is licensed under the [Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International License (CC BY-NC-SA 4.0)](https://creativecommons.org/licenses/by-nc-sa/4.0/). Unless otherwise noted, this applies to the project's original source code, documentation, and assets in this repository.

You may share and adapt this material under the license's terms:

- **Attribution:** credit Bellum Civile and Vonbalt, retain existing contributor and copyright notices, include the license text or a link to it, and indicate changes when sharing modified material.
- **NonCommercial:** use and redistribution under this license must be for noncommercial purposes.
- **ShareAlike:** shared adaptations must use the same license or another license permitted by its ShareAlike terms.

The full terms are in [LICENSE.txt](LICENSE.txt); the summary above does not replace them. Bellum's release packages should include this file alongside the mod.

Third-party code, libraries, game assets, and other separately licensed material remain subject to their respective licenses and attribution requirements. This license does not grant rights to TaleWorlds' game files or to other mods' material beyond the permissions their owners provide.
