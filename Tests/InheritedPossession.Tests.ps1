$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Behaviors/FeudalTitleBehavior.cs') -Raw
function Extract($start, $end) {
    $first = $source.IndexOf($start, [StringComparison]::Ordinal)
    $last = $source.IndexOf($end, $first, [StringComparison]::Ordinal)
    if ($first -lt 0 -or $last -le $first) { throw "Missing source boundary: $start" }
    $source.Substring($first, $last - $first)
}
$methods = (Extract '        public bool TransferInheritedPossession(' '        public int MoveCrownHeirClaims(') +
    (Extract '        private void OnSettlementOwnerChanged(' '        private bool TryApplySubinfeudationOwnershipChange(') +
    (Extract '        private FeudalTitleRecord EnsureBaronyTitle(' '        private void UpdateBaronyParentToCurrentKingdom(') +
    (Extract '        private bool ShouldLegalTitleTransferToHolder(' '        private void RegisterDisplacedLegalOwnerClaimIfNeeded(') +
    (Extract '        private string GetDriftOriginKingdomId(' '        public void RegisterPartitionHouseClaims(') +
    (Extract '        public void LegalizePartitionInheritance(' '        internal bool CompleteCourtTitleGrant(')
$harness = @'
using System;
using System.Collections.Generic;
using System.Linq;
using BellumCivile;
namespace TaleWorlds.SaveSystem { public class SaveableFieldAttribute : Attribute { public SaveableFieldAttribute(int id) {} } }
public class Kingdom { public string StringId; }
public class Clan { public string StringId; public Hero Leader; public Kingdom Kingdom; public bool IsEliminated; public List<Town> Fiefs = new List<Town>(); }
public class Hero { public Clan Clan; public bool IsAlive = true; public bool IsDead { get { return !IsAlive; } } }
public class Party { public object MapEvent; }
public class Settlement { public string StringId = "castle"; public Clan OwnerClan; public object SiegeEvent;
    public Party Party; public bool IsTown = true, IsCastle; public object MapFaction; }
public class Town { public Settlement Settlement; }
public enum FeudalClaimStrength { Strong }
public class FeudalClaimRecord { public string ClaimantClanId, TargetTitleId, Source; public Hero Carrier, SourceHero;
    public Clan Origin; public int Depth; public FeudalClaimStrength Strength; }
public class Campaign {
    public static Campaign Current = new Campaign();
    public PartitionSuccessionBehavior Partition = new PartitionSuccessionBehavior();
    public FeudalDeJureDriftBehavior Drift = new FeudalDeJureDriftBehavior();
    public T GetCampaignBehavior<T>() where T : class { return (Partition as T) ?? (Drift as T); }
}
public class PartitionSuccessionBehavior { public bool Protected; public bool IsCrownPromotionTitleProtected(string id) { return Protected; } }
public class CrownAccessionBehavior { public static CrownAccessionBehavior Instance = new CrownAccessionBehavior();
    public bool Protected; public bool IsRealmUnionTitleProtected(string id) { return Protected; } }
public class FeudalDeJureDriftBehavior { public Action<FeudalTitleRecord> Refresh;
    public void RefreshTitleFamily(FeudalTitleRecord title) { if (Refresh != null) Refresh(title); } }
namespace Patches { public static class FiefVoteResolutionPatch {
    public static bool IsVoluntaryRelinquishment(Settlement settlement, string formerHolder, Clan recipient) { return false; }
} }
public static class BellumCivileLogger { public static void Log(string message) {} }
public static class ChangeOwnerOfSettlementAction {
    public enum ChangeOwnerOfSettlementDetail { BySiege, ByKingDecision, ByGift, ByBarter }
    public static Action<Settlement, Hero, Hero> Callback;
    public static bool Fail, ThrowAfter;
    public static int Calls;
    public static void ApplyByDefault(Hero recipient, Settlement holding) {
        Calls++;
        if (Fail) return;
        var old = holding.OwnerClan.Leader;
        holding.OwnerClan = recipient.Clan;
        Callback(holding, recipient, old);
        if (ThrowAfter) throw new InvalidOperationException("interrupted native callback");
    }
}
public class InheritedPossessionTests {
    private string _deFactoOnlySettlementTransferTitleId;
    private Settlement _clientLandGrantInProgress = null;
    private readonly Dictionary<string, FeudalTitleRecord> _titlesById = new Dictionary<string, FeudalTitleRecord>();
    private readonly List<FeudalClaimRecord> _claims = new List<FeudalClaimRecord>();
    private readonly List<string> events = new List<string>();
    private bool hasClaim, journaled;
    private int displaced, revision;
    private float CurrentDay { get { return 12; } }
    private void EnsureCollectionsInitialized() {}
    private bool TryGetBarony(Settlement s, out FeudalTitleRecord title) { return _titlesById.TryGetValue(BuildBaronyTitleId(s), out title); }
    private FeudalTitleRecord GetTitle(string id) { FeudalTitleRecord title; return id != null && _titlesById.TryGetValue(id, out title) ? title : null; }
    private static string BuildBaronyTitleId(Settlement s) { return s.StringId; }
    private static bool IsLandedSettlement(Settlement s) { return s != null && (s.IsTown || s.IsCastle); }
    private string ResolveKingdomLegalParentTitleId(Kingdom k) { return k == null ? "" : k.StringId + "_crown"; }
    private string BuildConfiguredFallbackTitleRoot(FeudalTitleType type, Settlement s, Settlement capital, string id) { return id; }
    private string ResolveEffectiveConfiguredTitleName(string id, string name) { return name; }
    private bool IsIndependentRealmShell(Kingdom k) { return false; }
    private void EnsureKingdomTitle(Kingdom k, string reason, out bool changed) { changed = false; }
    private bool IsValidPermanentKingdom(Kingdom k) { return k != null; }
    private bool HasActiveClaim(Clan c, FeudalTitleRecord t) { return hasClaim; }
    private bool TryApplyJournaledCrownEstateOwnership(Settlement s, Clan recipient) {
        if (!journaled) return false;
        GetTitle(s.StringId).SetDeFactoHolder(recipient.StringId);
        return true;
    }
    private bool TryApplyMixedEstateOwnership(Settlement s, Clan recipient) { return false; }
    private bool ConsumeAllocationCustody(string id, string oldId, string newId, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail d) { return false; }
    private Clan ResolveClan(string id) { return null; }
    private bool TryApplySubinfeudationOwnershipChange(Settlement s, FeudalTitleRecord t, string legal, string actual, Clan recipient, Clan old, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail d) { return false; }
    private bool ShouldBarterTransferLegalTitle(string id, Clan seller, Clan buyer) { return false; }
    private bool IsAllocationCustodian(string title, string clan) { return false; }
    private void RemoveRedundantClaimsForHolder(Clan c, FeudalTitleRecord t) { _claims.RemoveAll(x => x.ClaimantClanId == c.StringId && x.TargetTitleId == t.TitleId); }
    private void UpdateBaronyParentToCurrentKingdom(FeudalTitleRecord t, Clan c) {
        t.SetParentTitle(ResolveKingdomLegalParentTitleId(c.Kingdom)); t.SetAssociatedKingdom(c.Kingdom.StringId);
    }
    private void RegisterDisplacedLegalOwnerClaimIfNeeded(FeudalTitleRecord t, string old, Clan c, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail d) { displaced++; }
    private void DeactivateClaimsForTitle(Clan c, FeudalTitleRecord t) {}
    private bool ReconcileDeFactoParent(FeudalTitleRecord t, Kingdom k) { t.SetDeFactoParentTitle(ResolveKingdomLegalParentTitleId(k)); return true; }
    private void RebuildRuntimeIndexes() { revision++; events.Add("indexes"); }
    private void QueueServiceReview(FeudalTitleRecord t, string reason) { events.Add("service:" + t.DeJureHolderClanId + ":" + t.ParentTitleId + ":" + t.AssociatedKingdomId); }
    private void QueueServiceReviewForTitleAndChildren(FeudalTitleRecord t, string reason) { QueueServiceReview(t, reason); }
    private FeudalClaimRecord RegisterClaim(Clan claimantClan, FeudalTitleRecord targetTitle, FeudalClaimStrength strength,
        string source, Hero sourceHero = null, Clan originClan = null, float expiresDay = -1,
        Hero carrierHero = null, int generationDepth = 0) {
        var claim = new FeudalClaimRecord { ClaimantClanId = claimantClan.StringId, TargetTitleId = targetTitle.TitleId,
            Strength = strength, Source = source, SourceHero = sourceHero, Origin = originClan, Carrier = carrierHero, Depth = generationDepth };
        _claims.Add(claim); return claim;
    }
    private void Connect() {
        Campaign.Current = new Campaign(); CrownAccessionBehavior.Instance = new CrownAccessionBehavior();
        ChangeOwnerOfSettlementAction.Fail = ChangeOwnerOfSettlementAction.ThrowAfter = false;
        ChangeOwnerOfSettlementAction.Calls = 0;
        ChangeOwnerOfSettlementAction.Callback = (s, n, o) => OnSettlementOwnerChanged(s, false, n, o, null,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByGift);
        Campaign.Current.Drift.Refresh = t => events.Add("drift:" + t.DeJureHolderClanId + ":" + t.DeFactoHolderClanId);
    }
    private static Clan House(string id) { var c = new Clan { StringId = id, Kingdom = new Kingdom { StringId = id + "_realm" } }; c.Leader = new Hero { Clan = c }; return c; }
    private FeudalTitleRecord Title(string id, string legal, string actual, string parent, string origin) {
        var t = new FeudalTitleRecord(id, id, FeudalTitleType.Barony, legal, actual, parent, id, origin, 0, 0);
        _titlesById.Add(id, t); return t;
    }
    private static int checks;
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
    public static void Run() {
        var source = House("source"); var recipient = House("heir"); var occupier = House("occupier");
        foreach (string legal in new[] { source.StringId, "third_party", "" })
        foreach (string parent in new[] { "old_parent", "" })
        foreach (string origin in new[] { "old_realm", "" }) {
            var b = new InheritedPossessionTests(); b.Connect(); b.hasClaim = true;
            b.Title("old_parent", source.StringId, source.StringId, "", "inherited_origin");
            var title = b.Title("castle", legal, source.StringId, parent, origin);
            var holding = new Settlement { OwnerClan = source };
            string effective = origin.Length > 0 ? origin : parent.Length > 0 ? "inherited_origin" : "";
            Check(b.TransferInheritedPossession(source, recipient, holding), "Inherited possession delivered");
            Check(title.DeJureHolderClanId == legal && title.ParentTitleId == parent && title.AssociatedKingdomId == effective,
                "Possession preserves legal holder/parent/effective origin even with recipient claim or empty fields");
            Check(title.DeFactoHolderClanId == recipient.StringId && title.DeFactoParentTitleId == "heir_realm_crown",
                "Physical holder and political hierarchy follow recipient");
            Check(b.displaced == 0 && b._deFactoOnlySettlementTransferTitleId == null, "No artificial displaced-owner claim or leaked context");
            Check(b.events.Last() == "service:" + legal + ":" + parent + ":" + effective, "Rights restored before service notification");
        }
        {
            var b = new InheritedPossessionTests(); b.Connect();
            var title = b.Title("castle", source.StringId, source.StringId, "old_parent", "old_origin");
            var nested = b.Title("nested", source.StringId, source.StringId, "", "");
            var holding = new Settlement { OwnerClan = source };
            var innerHolding = new Settlement { StringId = "nested", OwnerClan = source };
            var callback = ChangeOwnerOfSettlementAction.Callback;
            b._deFactoOnlySettlementTransferTitleId = "outer_context";
            ChangeOwnerOfSettlementAction.Callback = (s, n, o) => {
                if (s == holding) {
                    Check(b._deFactoOnlySettlementTransferTitleId == title.TitleId, "Outer transfer context installed");
                    Check(b.TransferInheritedPossession(source, recipient, innerHolding), "Nested transfer completes");
                    Check(b._deFactoOnlySettlementTransferTitleId == title.TitleId, "Nested transfer restores outer context");
                }
                callback(s, n, o);
            };
            Check(b.TransferInheritedPossession(source, recipient, holding), "Outer transfer completes");
            Check(b._deFactoOnlySettlementTransferTitleId == "outer_context", "Preexisting context restored");
            holding.OwnerClan = source; title.SetDeFactoHolder(source.StringId);
            ChangeOwnerOfSettlementAction.Callback = callback; ChangeOwnerOfSettlementAction.ThrowAfter = true;
            bool threw = false;
            try { b.TransferInheritedPossession(source, recipient, holding); } catch (InvalidOperationException) { threw = true; }
            Check(threw && b._deFactoOnlySettlementTransferTitleId == "outer_context", "Exception preserves context cleanup");
            Check(title.DeJureHolderClanId == source.StringId && title.ParentTitleId == "old_parent", "Interrupted callback cannot pre-legalize inheritance");
            Check(!b.TransferInheritedPossession(source, recipient, holding), "Already-transferred possession is not replayed");
            holding.OwnerClan = source; ChangeOwnerOfSettlementAction.ThrowAfter = false; ChangeOwnerOfSettlementAction.Fail = true;
            Check(!b.TransferInheritedPossession(source, recipient, holding) && b._deFactoOnlySettlementTransferTitleId == "outer_context", "Native failure reported without leaking context");
        }
        {
            var b = new InheritedPossessionTests(); b.Connect();
            var title = b.Title("castle", source.StringId, source.StringId, "", "");
            Check(b.TransferChallengePossession(source, recipient, new Settlement { OwnerClan = source }), "Challenge seizure remains possession-only");
            Check(title.DeJureHolderClanId == source.StringId && title.ParentTitleId == "" && title.AssociatedKingdomId == "", "Challenge seizure preserves empty legal topology");
            b.Connect(); title.SetDeFactoHolder(source.StringId);
            ChangeOwnerOfSettlementAction.ApplyByDefault(recipient.Leader, new Settlement { OwnerClan = source });
            Check(title.DeJureHolderClanId == recipient.StringId && b.displaced == 1, "Ordinary/concession grants retain native legal-transfer behavior");
        }
        {
            var b = new InheritedPossessionTests(); b.Connect(); b.journaled = true;
            var title = b.Title("castle", source.StringId, source.StringId, "", "");
            Check(b.TransferInheritedPossession(source, recipient, new Settlement { OwnerClan = source }), "Crown journal callback retains precedence");
            Check(title.ParentTitleId == "" && title.AssociatedKingdomId == "" && title.DeJureHolderClanId == source.StringId,
                "Journal callback bypasses generic synchronization");
        }
        {
            var b = new InheritedPossessionTests(); b.Connect();
            var title = b.Title("castle", source.StringId, source.StringId, "old_parent", "old_origin");
            var holding = new Settlement { OwnerClan = source };
            var callback = ChangeOwnerOfSettlementAction.Callback;
            bool eligible = true;
            int refreshes = 0;
            Campaign.Current.Drift.Refresh = t => { refreshes++; eligible = t.DeJureHolderClanId == recipient.StringId; };
            ChangeOwnerOfSettlementAction.Callback = (s, n, o) => {
                callback(s, n, o);
                Campaign.Current.Drift.RefreshTitleFamily(title);
            };
            Check(b.TransferInheritedPossession(source, recipient, holding) && !eligible && refreshes == 1,
                "Physical callback can observe pre-legalization drift ineligibility");
            b.LegalizeTitleInheritance(recipient, title, "inheritance after possession");
            Check(eligible && refreshes == 2, "Post-legalization refresh corrects initial callback suppression immediately");
        }
        {
            var b = new InheritedPossessionTests(); b.Connect();
            var title = b.Title("castle", source.StringId, occupier.StringId, "old_parent", "old_origin");
            var holding = new Settlement { OwnerClan = occupier };
            b._claims.Add(new FeudalClaimRecord { ClaimantClanId = recipient.StringId, TargetTitleId = title.TitleId });
            b.LegalizeTitleInheritance(recipient, title, "legal-only inheritance", preserveDeFacto: true);
            Check(title.DeJureHolderClanId == recipient.StringId && title.DeFactoHolderClanId == occupier.StringId
                && holding.OwnerClan == occupier && ChangeOwnerOfSettlementAction.Calls == 0, "Legal-only inheritance leaves third-party occupation intact");
            Check(title.ParentTitleId == "old_parent" && title.AssociatedKingdomId == "old_origin" && b._claims.Count == 0,
                "Legalization preserves topology and removes redundant recipient claims");
            Check(b.revision == 1 && b.events.SequenceEqual(new[] { "indexes", "service:heir:old_parent:old_origin", "drift:heir:occupier" }),
                "Legalized indexes precede service notifications and final drift refresh");
            var dead = new Hero { IsAlive = false, Clan = source };
            var claim = b.RegisterEstateReversionClaim(source, recipient, title, dead);
            Check(claim != null && claim.Source == "domestic_estate_escheat" && claim.Strength == FeudalClaimStrength.Strong
                && claim.Depth == 0 && claim.SourceHero == dead && claim.Origin == source && claim.Carrier == source.Leader,
                "Escheat reversion claim retains source provenance and a living source-house carrier");
            Check(title.ParentTitleId == "old_parent" && title.AssociatedKingdomId == "old_origin"
                && title.DeFactoHolderClanId == occupier.StringId, "Escheat claim registration cannot naturalize or seize the inherited estate");
            Check(b.RegisterEstateReversionClaim(recipient, recipient, title, dead) == null, "No escheat self-claim");
            title.SetDeJureHolder(source.StringId);
            Check(b.RegisterEstateReversionClaim(source, recipient, title, dead) == null, "No reversion claim before legal delivery");
            title.SetDeJureHolder(recipient.StringId); source.Leader.IsAlive = false;
            Check(b.RegisterEstateReversionClaim(source, recipient, title, dead) == null, "No dead-carrier claim");
            source.Leader.IsAlive = true;
            b.events.Clear(); Campaign.Current.Partition.Protected = true;
            b.LegalizeTitleInheritance(recipient, title, "journaled Crown partition", preserveDeFacto: true);
            Check(b.events.Count == 2 && b.events[0] == "indexes", "Crown journal legalization updates indexes/reviews without synchronous drift completion");
            b.events.Clear(); Campaign.Current.Partition.Protected = false; CrownAccessionBehavior.Instance.Protected = true;
            b.FinishInheritedTitleTransfer(title, "realm union");
            Check(b.events.Count == 2, "Realm-union topology remains protected");
            CrownAccessionBehavior.Instance.Protected = false; b.events.Clear();
            b.LegalizeTitleInheritance(recipient, title, "controlled inheritance", preserveDeFacto: false);
            Check(title.DeFactoHolderClanId == recipient.StringId && b.events.Last() == "drift:heir:heir", "Controlled inheritance refreshes final legal and actual ownership");
        }
        Console.WriteLine("PASS: " + checks + " inherited-possession, legalization, ordering, journal and escheat checks.");
    }
'@
$sdkVersion = (& dotnet --version).Trim()
$compiler = Join-Path $env:ProgramFiles "dotnet\sdk\$sdkVersion\Roslyn\bincore\csc.dll"
$framework = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('BellumInheritedPossession-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temporary) | Out-Null
try {
    $inputFile = Join-Path $temporary 'Tests.cs'
    $assembly = Join-Path $temporary 'Tests.dll'
    [System.IO.File]::WriteAllText($inputFile, $harness + $methods + '}')
    & dotnet $compiler /nologo /target:library "/out:$assembly" "/reference:$(Join-Path $framework 'mscorlib.dll')" "/reference:$(Join-Path $framework 'System.dll')" "/reference:$(Join-Path $framework 'System.Core.dll')" $inputFile (Join-Path $root 'FeudalTitleRecord.cs') (Join-Path $root 'FeudalTitleType.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($assembly)) | Out-Null
    [InheritedPossessionTests]::Run()
}
finally {
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.cs') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $temporary 'Tests.dll') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporary
}
$crown = Get-Content (Join-Path $root 'Behaviors/CrownAbdication.cs') -Raw
if ($crown -notmatch '(?s)if \(record.ChallengeConcession\)\s+ChangeOwnerOfSettlementAction.ApplyByDefault\(record.Cadet.Leader, fief\);\s+else if \(!titles.TransferInheritedPossession\(source, record.Cadet, fief\)\)') {
    throw 'Crown inheritance must use guarded possession while concessions retain grant semantics.'
}
if ($crown.IndexOf('titles.TransferInheritedPossession(') -gt $crown.IndexOf('titles.LegalizeTitleInheritance(')) {
    throw 'Crown legal delivery must follow physical delivery.'
}
Write-Output 'PASS: Crown cadet dispatch and physical-before-legal delivery wiring. Native campaign events still require live validation.'
