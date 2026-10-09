using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BellumCivile
{
    internal static class KingdomVisualHelper
    {
        private static PropertyInfo _kingdomPrimaryBannerColorProperty;
        private static PropertyInfo _kingdomSecondaryBannerColorProperty;
        private static PropertyInfo _kingdomColorProperty;
        private static PropertyInfo _kingdomColor2Property;
        private static PropertyInfo _bannerManagerColorPaletteProperty;
        private static MethodInfo _clanUpdateBannerColorsAccordingToKingdomMethod;
        [ThreadStatic] private static Clan _generatedCadetAssignment;
        private static readonly int[] MetalColorIds = new[]
        {
            3, 5, 7, 11, 13, 15, 17, 19, 20, 21, 22, 26, 27, 28, 31, 32, 33, 34, 35, 36, 38, 39, 41, 42, 43, 44, 45,
            47, 53, 58, 60, 64, 84, 108, 109, 110, 111, 114, 121, 127, 131, 135, 139, 143, 155, 166, 171, 172, 173,
            174, 183, 185, 191, 192, 196, 212, 216, 224, 225, 228, 229, 230, 239, 242, 245, 246, 248, 249, 250
        };
        private static readonly HashSet<int> MetalColorIdSet = new HashSet<int>(MetalColorIds);
        private static readonly int[] PrimaryBackgroundColorIds = new[]
        {
            0, 1, 2, 4, 6, 8, 10, 12, 14, 48, 51, 65, 67, 71, 72, 77, 83, 89, 96, 97, 98, 100, 116, 122, 126, 130,
            134, 138, 142, 146, 148, 149, 158, 159, 160, 161, 162, 163, 164, 176, 178, 186, 189, 190, 193, 194, 197,
            200, 202, 205, 209, 210, 213, 214, 218, 220, 221, 222, 233, 236, 237, 243, 244, 247, 3, 5, 7, 11, 13, 15,
            35, 84, 172, 250
        };
        private static readonly int[] SecondaryColorIds = new[]
        {
            16, 18, 23, 24, 25, 26, 29, 30, 36, 38, 39, 46, 49, 50, 52, 54, 55, 56, 57, 59, 60, 61, 62, 63, 64, 66,
            68, 69, 70, 73, 74, 75, 76, 78, 79, 80, 81, 82, 85, 86, 87, 90, 91, 92, 93, 94, 95, 99, 101, 102, 103,
            104, 105, 106, 107, 112, 113, 115, 118, 119, 120, 123, 135, 151, 152, 153, 154, 156, 157, 165, 175, 179,
            180, 181, 182, 184, 185, 191, 192, 196, 201, 204, 208, 217, 219, 223, 224, 225, 226, 227, 231, 232, 235,
            238, 240, 241, 248, 249
        };
        private static readonly int[] SigilColorIds = new[]
        {
            3, 5, 7, 11, 13, 15, 17, 19, 20, 21, 22, 27, 28, 31, 32, 33, 34, 35, 41, 42, 43, 44, 45, 47, 84, 108,
            109, 110, 111, 121, 127, 131, 139, 143, 155, 166, 171, 172, 173, 174, 183, 212, 216, 228, 230, 239, 242,
            245, 246, 250, 1, 2, 4, 6, 8, 12, 14, 71, 72, 83, 116, 142, 161, 162, 205, 209, 233
        };

        private readonly struct TailoredBannerPalette
        {
            public TailoredBannerPalette(int backgroundMeshId, int backgroundPrimaryColorId, int backgroundSecondaryColorId, int sigilPrimaryColorId, int sigilSecondaryColorId)
            {
                BackgroundMeshId = backgroundMeshId;
                BackgroundPrimaryColorId = backgroundPrimaryColorId;
                BackgroundSecondaryColorId = backgroundSecondaryColorId;
                SigilPrimaryColorId = sigilPrimaryColorId;
                SigilSecondaryColorId = sigilSecondaryColorId;
            }

            public int BackgroundMeshId { get; }
            public int BackgroundPrimaryColorId { get; }
            public int BackgroundSecondaryColorId { get; }
            public int SigilPrimaryColorId { get; }
            public int SigilSecondaryColorId { get; }
            public uint BackgroundPrimaryColor => BannerManager.GetColor(BackgroundPrimaryColorId);
            public uint SigilPrimaryColor => BannerManager.GetColor(SigilPrimaryColorId);
        }

        internal readonly struct KingdomFoundingVisuals
        {
            public KingdomFoundingVisuals(Banner banner, uint primaryColor, uint secondaryColor, bool recolorMemberClans)
            {
                Banner = banner;
                PrimaryColor = primaryColor;
                SecondaryColor = secondaryColor;
                RecolorMemberClans = recolorMemberClans;
            }

            public Banner Banner { get; }
            public uint PrimaryColor { get; }
            public uint SecondaryColor { get; }
            public bool RecolorMemberClans { get; }
        }

        internal readonly struct CadetBranchVisuals
        {
            public CadetBranchVisuals(Banner banner, uint primaryColor, uint secondaryColor, bool copiedParentArtwork)
            {
                Banner = banner;
                PrimaryColor = primaryColor;
                SecondaryColor = secondaryColor;
                CopiedParentArtwork = copiedParentArtwork;
            }

            public Banner Banner { get; }
            public uint PrimaryColor { get; }
            public uint SecondaryColor { get; }
            public bool CopiedParentArtwork { get; }
        }

        public static KingdomFoundingVisuals ResolveBreakawayKingdomVisuals(Kingdom sourceKingdom, Clan foundingClan, string seedKey)
        {
            Banner foundingSourceBanner = foundingClan?.ClanOriginalBanner ?? foundingClan?.Banner;
            Banner foundingBanner = CloneBanner(foundingSourceBanner)
                ?? CloneBanner(sourceKingdom?.Banner)
                ?? Banner.CreateRandomClanBanner(GetStableHash(seedKey));

            uint sourceBannerPrimary = GetKingdomPrimaryBannerColor(sourceKingdom) ?? sourceKingdom?.Color ?? GetPaletteFallbackPrimary();
            uint sourceBannerSecondary = GetKingdomSecondaryBannerColor(sourceKingdom) ?? sourceKingdom?.Color2 ?? GetPaletteFallbackSecondary();

            uint foundingPrimary = foundingClan?.ClanOriginalBanner?.GetPrimaryColor()
                ?? foundingClan?.Banner?.GetPrimaryColor()
                ?? foundingClan?.Color
                ?? sourceBannerPrimary;
            uint foundingSecondary = foundingClan?.ClanOriginalBanner?.GetFirstIconColor()
                ?? foundingClan?.Banner?.GetFirstIconColor()
                ?? foundingClan?.Color2
                ?? sourceBannerSecondary;

            if (sourceKingdom == null)
                return new KingdomFoundingVisuals(foundingBanner, foundingPrimary, foundingSecondary, recolorMemberClans: false);

            if (ShouldPreserveClanBanner(foundingClan)
                || ShouldPreserveBreakawayBanner(sourceKingdom, foundingSourceBanner, foundingPrimary, foundingSecondary))
                return new KingdomFoundingVisuals(foundingBanner, foundingPrimary, foundingSecondary, recolorMemberClans: false);

            TailoredBannerPalette palette = BuildTailoredBannerPalette(seedKey, sourceBannerPrimary, sourceBannerSecondary);
            ApplyTailoredPaletteToBanner(foundingBanner, palette, randomizeBackground: true);
            return new KingdomFoundingVisuals(foundingBanner, palette.BackgroundPrimaryColor, palette.SigilPrimaryColor, recolorMemberClans: true);
        }

        public static KingdomFoundingVisuals ResolveInheritedKingdomVisuals(Kingdom sourceKingdom, Clan fallbackClan, string seedKey)
        {
            Banner inheritedBanner = CloneBanner(sourceKingdom?.Banner)
                ?? CloneBanner(fallbackClan?.Banner)
                ?? CloneBanner(fallbackClan?.ClanOriginalBanner)
                ?? Banner.CreateRandomClanBanner(GetStableHash(seedKey));

            uint inheritedPrimary = GetKingdomPrimaryBannerColor(sourceKingdom)
                ?? sourceKingdom?.Color
                ?? fallbackClan?.Color
                ?? fallbackClan?.Banner?.GetPrimaryColor()
                ?? fallbackClan?.ClanOriginalBanner?.GetPrimaryColor()
                ?? GetPaletteFallbackPrimary();
            uint inheritedSecondary = GetKingdomSecondaryBannerColor(sourceKingdom)
                ?? sourceKingdom?.Color2
                ?? fallbackClan?.Color2
                ?? fallbackClan?.Banner?.GetFirstIconColor()
                ?? fallbackClan?.ClanOriginalBanner?.GetFirstIconColor()
                ?? GetPaletteFallbackSecondary();

            return new KingdomFoundingVisuals(inheritedBanner, inheritedPrimary, inheritedSecondary, recolorMemberClans: false);
        }

        public static KingdomFoundingVisuals ResolveIndependentSuccessorKingdomVisuals(Kingdom parentKingdom, Clan foundingClan, string seedKey)
        {
            Banner sourceBanner = foundingClan?.ClanOriginalBanner ?? foundingClan?.Banner;
            Banner successorBanner = CloneBanner(sourceBanner)
                ?? CloneBanner(parentKingdom?.Banner)
                ?? Banner.CreateRandomClanBanner(GetStableHash(seedKey));

            uint foundingPrimary = sourceBanner?.GetPrimaryColor()
                ?? foundingClan?.Color
                ?? GetKingdomPrimaryBannerColor(parentKingdom)
                ?? parentKingdom?.Color
                ?? GetPaletteFallbackPrimary();
            uint foundingSecondary = sourceBanner?.GetFirstIconColor()
                ?? foundingClan?.Color2
                ?? GetKingdomSecondaryBannerColor(parentKingdom)
                ?? parentKingdom?.Color2
                ?? GetPaletteFallbackSecondary();

            if (ShouldPreserveClanBanner(foundingClan))
                return new KingdomFoundingVisuals(successorBanner, foundingPrimary, foundingSecondary, recolorMemberClans: false);

            uint sourcePrimary = GetKingdomPrimaryBannerColor(parentKingdom)
                ?? parentKingdom?.Color
                ?? foundingClan?.Color
                ?? sourceBanner?.GetPrimaryColor()
                ?? GetPaletteFallbackPrimary();
            uint sourceSecondary = GetKingdomSecondaryBannerColor(parentKingdom)
                ?? parentKingdom?.Color2
                ?? foundingClan?.Color2
                ?? sourceBanner?.GetFirstIconColor()
                ?? GetPaletteFallbackSecondary();

            TailoredBannerPalette palette = BuildTailoredBannerPalette(seedKey, sourcePrimary, sourceSecondary);
            ApplyTailoredPaletteToBanner(successorBanner, palette, randomizeBackground: false);
            return new KingdomFoundingVisuals(successorBanner, palette.BackgroundPrimaryColor, palette.SigilPrimaryColor, recolorMemberClans: false);
        }

        public static CadetBranchVisuals ResolveCadetBranchVisuals(Clan parentClan, string seedKey)
        {
            Banner parentBanner = parentClan?.ClanOriginalBanner ?? parentClan?.Banner;
            if (ShouldPreserveClanBanner(parentClan))
            {
                Banner inheritedBanner = CloneBanner(parentBanner);
                return new CadetBranchVisuals(inheritedBanner, inheritedBanner.GetPrimaryColor(),
                    inheritedBanner.GetFirstIconColor(), copiedParentArtwork: true);
            }

            Kingdom kingdom = parentClan?.Kingdom;
            uint sourcePrimary = GetKingdomPrimaryBannerColor(kingdom)
                ?? kingdom?.Color
                ?? parentClan?.Color
                ?? parentBanner?.GetPrimaryColor()
                ?? GetPaletteFallbackPrimary();
            uint sourceSecondary = GetKingdomSecondaryBannerColor(kingdom)
                ?? kingdom?.Color2
                ?? parentClan?.Color2
                ?? parentBanner?.GetFirstIconColor()
                ?? GetPaletteFallbackSecondary();

            TailoredBannerPalette palette = BuildTailoredBannerPalette(seedKey, sourcePrimary, sourceSecondary);
            Banner cadetBanner = CreateTailoredRandomBanner(seedKey, palette);
            return new CadetBranchVisuals(cadetBanner, palette.BackgroundPrimaryColor, palette.SigilPrimaryColor, copiedParentArtwork: false);
        }

        internal static Banner CreateRandomClanBannerWithoutStrokes(string seedKey)
        {
            Banner banner = Banner.CreateRandomClanBanner(GetStableHash(seedKey));
            DisableIconStrokes(banner);
            return banner;
        }

        public static void ApplyKingdomPalette(Kingdom kingdom, KingdomFoundingVisuals visuals)
        {
            if (kingdom == null)
                return;

            SetKingdomBannerColors(kingdom, visuals.PrimaryColor, visuals.SecondaryColor);
            if (visuals.RecolorMemberClans)
                InvokeKingdomClanBannerSync(kingdom);
            MarkKingdomVisualsDirty(kingdom);
        }

        public static void ReapplyKingdomPaletteAfterClanChanges(Kingdom kingdom, KingdomFoundingVisuals visuals)
        {
            ApplyKingdomPalette(kingdom, visuals);
        }

        public static void ApplyKingdomPalette(Kingdom kingdom, Banner banner, uint primaryColor, uint secondaryColor)
        {
            ApplyKingdomPalette(kingdom, banner, primaryColor, secondaryColor, recolorBannerArtwork: true);
        }

        private static void ApplyKingdomPalette(Kingdom kingdom, Banner banner, uint primaryColor, uint secondaryColor, bool recolorBannerArtwork)
        {
            if (kingdom == null)
                return;

            if (recolorBannerArtwork)
            {
                ApplyBannerPalette(kingdom.Banner, primaryColor, secondaryColor);
                if (banner != null && !ReferenceEquals(banner, kingdom.Banner))
                    ApplyBannerPalette(banner, primaryColor, secondaryColor);
            }

            SetKingdomBannerColors(kingdom, primaryColor, secondaryColor);
            if (recolorBannerArtwork)
                InvokeKingdomClanBannerSync(kingdom);
            MarkKingdomVisualsDirty(kingdom);
        }

        public static void ApplyJoinToKingdomPreservingCustomBanner(Clan clan, Kingdom targetKingdom, bool showNotification = true)
        {
            ApplyJoinToKingdomWithBannerPolicy(clan, targetKingdom, preserveCustomBanner: true, showNotification: showNotification);
        }

        public static void ApplyJoinToKingdomWithBannerPolicy(Clan clan, Kingdom targetKingdom, bool preserveCustomBanner, bool showNotification = true)
        {
            // A vanilla founder's palette must not decide the fate of another house's custom artwork.
            if (!preserveCustomBanner && !ShouldPreserveClanBanner(clan))
            {
                ChangeKingdomAction.ApplyByJoinToKingdom(clan, targetKingdom, showNotification: showNotification);
                InvokeClanBannerSync(clan);
                MarkClanVisualsDirty(clan);
                return;
            }

            Banner preservedBanner = CaptureClanBannerToPreserve(clan);
            ChangeKingdomAction.ApplyByJoinToKingdom(clan, targetKingdom, showNotification: showNotification);
            RestoreClanBanner(clan, preservedBanner);
        }

        public static Dictionary<Clan, Banner> CaptureClanBannersToPreserve(IEnumerable<Clan> clans)
        {
            Dictionary<Clan, Banner> preservedBanners = new Dictionary<Clan, Banner>();
            if (clans == null)
                return preservedBanners;

            foreach (Clan clan in clans.Where(c => c != null).Distinct())
            {
                Banner preservedBanner = CaptureClanBannerToPreserve(clan);
                if (preservedBanner != null)
                    preservedBanners[clan] = preservedBanner;
            }

            return preservedBanners;
        }

        public static void RestoreClanBanners(Dictionary<Clan, Banner> preservedBanners)
        {
            if (preservedBanners == null)
                return;

            foreach (KeyValuePair<Clan, Banner> preserved in preservedBanners)
                RestoreClanBanner(preserved.Key, preserved.Value);
        }

        private static bool PaletteMatchesSource(Kingdom sourceKingdom, uint primaryColor, uint secondaryColor)
        {
            if (sourceKingdom == null)
                return false;

            uint sourcePrimary = GetKingdomPrimaryBannerColor(sourceKingdom) ?? sourceKingdom.Color;
            uint sourceSecondary = GetKingdomSecondaryBannerColor(sourceKingdom) ?? sourceKingdom.Color2;

            return (sourcePrimary == primaryColor && sourceSecondary == secondaryColor)
                || (sourcePrimary == secondaryColor && sourceSecondary == primaryColor);
        }

        private static bool ShouldPreserveBreakawayBanner(Kingdom sourceKingdom, Banner foundingBanner, uint primaryColor, uint secondaryColor)
        {
            if (foundingBanner == null)
                return false;

            if (!PaletteMatchesSource(sourceKingdom, primaryColor, secondaryColor))
                return true;

            return HasForeignPaletteArtwork(sourceKingdom, foundingBanner);
        }

        private static Banner CaptureClanBannerToPreserve(Clan clan)
        {
            if (!ShouldPreserveClanBanner(clan))
                return null;

            return CloneBanner(clan.ClanOriginalBanner ?? clan.Banner);
        }

        private static void RestoreClanBanner(Clan clan, Banner preservedBanner)
        {
            if (clan == null || preservedBanner == null)
                return;
            if (Patches.PocBannerCompatibility.TryGetPolicy(clan, out _, out bool controlsColors) && controlsColors)
                return;

            clan.Banner = CloneBanner(preservedBanner);
            MarkClanVisualsDirty(clan);
        }

        internal static bool ShouldPreserveClanBanner(Clan clan)
        {
            if (clan != null && clan == _generatedCadetAssignment) return false;
            Banner clanBanner = clan?.ClanOriginalBanner ?? clan?.Banner;
            if (clanBanner?.BannerDataList == null || clanBanner.BannerDataList.Count == 0)
                return false;

            if (clanBanner.BannerDataList.Count > 2
                || clanBanner.BannerDataList.Any(layer => layer.ColorId != layer.ColorId2))
                return true;
            if (Patches.PocBannerCompatibility.TryGetPolicy(clan, out bool customBanner, out _) && customBanner)
                return true;

            Kingdom kingdom = clan.Kingdom;
            if (kingdom == null)
                return false;

            if (!PaletteMatchesSource(kingdom, clanBanner.GetPrimaryColor(), clanBanner.GetFirstIconColor()))
                return true;

            return HasForeignPaletteArtwork(kingdom, clanBanner);
        }

        internal static void AssignCadetKingdom(Clan cadet, Kingdom kingdom, CadetBranchVisuals visuals)
        {
            // Keep an independent snapshot: the kingdom setter may mutate the assigned Banner in place.
            Banner inherited = visuals.CopiedParentArtwork ? CloneBanner(visuals.Banner) : null;
            Clan previous = _generatedCadetAssignment;
            try
            {
                if (!visuals.CopiedParentArtwork) _generatedCadetAssignment = cadet;
                cadet.Kingdom = kingdom;
            }
            finally { _generatedCadetAssignment = previous; }
            if (inherited == null
                || (Patches.PocBannerCompatibility.TryGetPolicy(cadet, out _, out bool controlsColors) && controlsColors))
                return;
            cadet.Banner = inherited;
            cadet.UpdateBannerColor(visuals.PrimaryColor, visuals.SecondaryColor);
            cadet.Color = visuals.PrimaryColor;
            cadet.Color2 = visuals.SecondaryColor;
        }

        private static bool HasForeignPaletteArtwork(Kingdom kingdom, Banner banner)
        {
            if (kingdom == null || banner?.BannerDataList == null)
                return false;

            uint primary = GetKingdomPrimaryBannerColor(kingdom) ?? kingdom.Color;
            uint secondary = GetKingdomSecondaryBannerColor(kingdom) ?? kingdom.Color2;

            return banner.BannerDataList
                .SelectMany(data => new[] { data.ColorId, data.ColorId2 })
                .Select(GetBannerPaletteColor)
                .Where(color => color.HasValue)
                .Select(color => color.Value)
                .Distinct()
                .Any(color => color != primary && color != secondary);
        }

        private static TailoredBannerPalette BuildTailoredBannerPalette(string seedKey, uint sourcePrimary, uint sourceSecondary)
        {
            MBFastRandom random = new MBFastRandom((uint)GetStableHash(seedKey));
            int backgroundPrimary = PickColorId(random, GetAvailableColorIds(PrimaryBackgroundColorIds), sourcePrimary, sourceSecondary, requireMetal: null, forbiddenColorId: -1, minimumDistance: 0);
            bool backgroundIsMetal = IsMetalColor(backgroundPrimary);
            int backgroundSecondary = PickColorId(random, GetAvailableColorIds(SecondaryColorIds), sourcePrimary, sourceSecondary, backgroundIsMetal, backgroundPrimary, 70);
            int sigilPrimary = PickColorId(random, GetAvailableColorIds(SigilColorIds), sourcePrimary, sourceSecondary, !backgroundIsMetal, -1, 0);
            int sigilSecondary = PickColorId(random, GetAvailableColorIds(SigilColorIds), sourcePrimary, sourceSecondary, !backgroundIsMetal, sigilPrimary, 70);

            if (backgroundSecondary < 0)
                backgroundSecondary = backgroundPrimary;
            if (sigilPrimary < 0)
                sigilPrimary = BannerManager.GetColorId(backgroundIsMetal ? GetPaletteFallbackPrimary() : GetPaletteFallbackSecondary());
            if (sigilSecondary < 0)
                sigilSecondary = sigilPrimary;

            return new TailoredBannerPalette(PickBackgroundMeshId(random), backgroundPrimary, backgroundSecondary, sigilPrimary, sigilSecondary);
        }

        private static Banner CreateTailoredRandomBanner(string seedKey, TailoredBannerPalette palette)
        {
            MBFastRandom random = new MBFastRandom((uint)GetStableHash(seedKey));
            Banner banner = new Banner();
            banner.AddIconData(new BannerData(
                palette.BackgroundMeshId,
                palette.BackgroundPrimaryColorId,
                palette.BackgroundSecondaryColorId,
                new Vec2(1528f, 1528f),
                new Vec2(764f, 764f),
                drawStroke: false,
                mirror: false,
                0f));

            banner.AddIconData(new BannerData(
                PickIconMeshId(random),
                palette.SigilPrimaryColorId,
                palette.SigilSecondaryColorId,
                new Vec2(512f, 512f),
                new Vec2(764f, 764f),
                drawStroke: false,
                mirror: false,
                0f));

            return banner;
        }

        private static void ApplyTailoredPaletteToBanner(Banner banner, TailoredBannerPalette palette, bool randomizeBackground)
        {
            if (banner?.BannerDataList == null || banner.BannerDataList.Count == 0)
                return;

            BannerData background = banner.GetBannerDataAtIndex(Banner.BackgroundDataIndex);
            if (background != null)
            {
                if (randomizeBackground)
                    background.MeshId = palette.BackgroundMeshId;
                background.ColorId = palette.BackgroundPrimaryColorId;
                background.ColorId2 = palette.BackgroundSecondaryColorId;
                background.DrawStroke = false;
            }

            for (int i = Banner.BannerIconDataIndex; i < banner.BannerDataList.Count; i++)
            {
                BannerData icon = banner.GetBannerDataAtIndex(i);
                if (icon == null)
                    continue;

                icon.ColorId = palette.SigilPrimaryColorId;
                icon.ColorId2 = palette.SigilSecondaryColorId;
                icon.DrawStroke = false;
            }
        }

        private static int PickBackgroundMeshId(MBFastRandom random)
        {
            int meshId = BannerManager.Instance?.GetRandomBackgroundId(random) ?? -1;
            return meshId >= 0 ? meshId : 11;
        }

        private static int PickIconMeshId(MBFastRandom random)
        {
            int meshId = BannerManager.Instance?.GetRandomBannerIconId(random) ?? -1;
            return meshId >= 0 ? meshId : 500;
        }

        private static int PickColorId(MBFastRandom random, List<int> candidateIds, uint sourcePrimary, uint sourceSecondary, bool? requireMetal, int forbiddenColorId, int minimumDistance)
        {
            if (candidateIds == null || candidateIds.Count == 0)
                return -1;

            int startIndex = random.Next(candidateIds.Count);
            for (int offset = 0; offset < candidateIds.Count; offset++)
            {
                int id = candidateIds[(startIndex + offset) % candidateIds.Count];
                if (id == forbiddenColorId)
                    continue;

                if (requireMetal.HasValue && IsMetalColor(id) != requireMetal.Value)
                    continue;

                uint color = BannerManager.GetColor(id);
                if (color == sourcePrimary || color == sourceSecondary)
                    continue;

                if (forbiddenColorId >= 0 && minimumDistance > 0 && ColorDistance(color, BannerManager.GetColor(forbiddenColorId)) < minimumDistance)
                    continue;

                return id;
            }

            for (int offset = 0; offset < candidateIds.Count; offset++)
            {
                int id = candidateIds[(startIndex + offset) % candidateIds.Count];
                if (id != forbiddenColorId && (!requireMetal.HasValue || IsMetalColor(id) == requireMetal.Value))
                    return id;
            }

            return -1;
        }

        private static List<int> GetAvailableColorIds(IEnumerable<int> preferredIds)
        {
            HashSet<int> availableIds = GetAvailablePaletteIds();
            return preferredIds
                .Where(id => availableIds.Contains(id))
                .Distinct()
                .ToList();
        }

        private static HashSet<int> GetAvailablePaletteIds()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            if (_bannerManagerColorPaletteProperty == null)
                _bannerManagerColorPaletteProperty = typeof(BannerManager).GetProperty("ColorPalette", flags);

            MBReadOnlyDictionary<int, BannerColor> palette = _bannerManagerColorPaletteProperty?.GetValue(null) as MBReadOnlyDictionary<int, BannerColor>;
            return palette != null ? new HashSet<int>(palette.Keys) : new HashSet<int>();
        }

        private static bool IsMetalColor(int colorId)
        {
            return MetalColorIdSet.Contains(colorId);
        }

        private static (uint Primary, uint Secondary) PickDistinctPaletteColors(uint sourcePrimary, uint sourceSecondary, string seedKey)
        {
            List<uint> paletteColors = GetBannerPaletteColors();
            if (paletteColors.Count == 0)
                return (sourcePrimary, sourceSecondary);

            int startIndex = Math.Abs(GetStableHash(seedKey)) % paletteColors.Count;

            for (int offset = 0; offset < paletteColors.Count; offset++)
            {
                uint primary = paletteColors[(startIndex + offset) % paletteColors.Count];
                if (primary == sourcePrimary || primary == sourceSecondary)
                    continue;

                for (int secondaryOffset = 1; secondaryOffset < paletteColors.Count; secondaryOffset++)
                {
                    uint secondary = paletteColors[(startIndex + offset + secondaryOffset) % paletteColors.Count];
                    if (secondary == primary || secondary == sourcePrimary || secondary == sourceSecondary)
                        continue;

                    if (ColorDistance(primary, secondary) < 140)
                        continue;

                    if (ColorDistance(primary, sourcePrimary) < 120 && ColorDistance(secondary, sourceSecondary) < 120)
                        continue;

                    if (ColorDistance(primary, sourceSecondary) < 120 && ColorDistance(secondary, sourcePrimary) < 120)
                        continue;

                    return (primary, secondary);
                }
            }

            uint fallbackPrimary = paletteColors.FirstOrDefault(color => color != sourcePrimary && color != sourceSecondary);
            uint fallbackSecondary = paletteColors.FirstOrDefault(color => color != fallbackPrimary && color != sourcePrimary && color != sourceSecondary);

            if (fallbackPrimary != 0 && fallbackSecondary != 0)
                return (fallbackPrimary, fallbackSecondary);

            return (GetPaletteFallbackPrimary(), GetPaletteFallbackSecondary());
        }

        private static List<uint> GetBannerPaletteColors()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            if (_bannerManagerColorPaletteProperty == null)
                _bannerManagerColorPaletteProperty = typeof(BannerManager).GetProperty("ColorPalette", flags);

            MBReadOnlyDictionary<int, BannerColor> palette = _bannerManagerColorPaletteProperty?.GetValue(null) as MBReadOnlyDictionary<int, BannerColor>;
            if (palette == null)
                return new List<uint>();

            return palette
                .OrderBy(entry => entry.Key)
                .Select(entry => entry.Value.Color)
                .Distinct()
                .ToList();
        }

        private static uint? GetBannerPaletteColor(int colorId)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            if (_bannerManagerColorPaletteProperty == null)
                _bannerManagerColorPaletteProperty = typeof(BannerManager).GetProperty("ColorPalette", flags);

            MBReadOnlyDictionary<int, BannerColor> palette = _bannerManagerColorPaletteProperty?.GetValue(null) as MBReadOnlyDictionary<int, BannerColor>;
            if (palette == null)
                return null;

            return palette.TryGetValue(colorId, out BannerColor bannerColor) ? bannerColor.Color : (uint?)null;
        }

        private static uint GetPaletteFallbackPrimary()
        {
            List<uint> paletteColors = GetBannerPaletteColors();
            return paletteColors.FirstOrDefault() != 0 ? paletteColors.First() : uint.MaxValue;
        }

        private static uint GetPaletteFallbackSecondary()
        {
            List<uint> paletteColors = GetBannerPaletteColors();
            uint secondary = paletteColors.Skip(1).FirstOrDefault();
            return secondary != 0 ? secondary : GetPaletteFallbackPrimary();
        }

        private static int ColorDistance(uint firstColor, uint secondColor)
        {
            int firstR = (int)((firstColor >> 16) & 0xFF);
            int firstG = (int)((firstColor >> 8) & 0xFF);
            int firstB = (int)(firstColor & 0xFF);
            int secondR = (int)((secondColor >> 16) & 0xFF);
            int secondG = (int)((secondColor >> 8) & 0xFF);
            int secondB = (int)(secondColor & 0xFF);

            return Math.Abs(firstR - secondR) + Math.Abs(firstG - secondG) + Math.Abs(firstB - secondB);
        }

        private static Banner CloneBanner(Banner sourceBanner)
        {
            return sourceBanner != null ? new Banner(sourceBanner) : null;
        }

        private static void DisableIconStrokes(Banner banner)
        {
            if (banner?.BannerDataList == null)
                return;

            for (int i = Banner.BannerIconDataIndex; i < banner.BannerDataList.Count; i++)
            {
                BannerData data = banner.GetBannerDataAtIndex(i);
                if (data != null)
                    data.DrawStroke = false;
            }
        }

        private static void ApplyBannerPalette(Banner banner, uint primaryColor, uint secondaryColor)
        {
            if (banner == null)
                return;

            try
            {
                banner.ChangePrimaryColor(primaryColor);
                banner.ChangeIconColors(secondaryColor);
            }
            catch
            {
                // Ignore banner mutation failures and rely on kingdom palette fields.
            }
        }

        private static void SetKingdomBannerColors(Kingdom kingdom, uint primaryColor, uint secondaryColor)
        {
            EnsureKingdomPropertyAccessors();

            try
            {
                _kingdomPrimaryBannerColorProperty?.SetValue(kingdom, primaryColor);
                _kingdomSecondaryBannerColorProperty?.SetValue(kingdom, secondaryColor);
                _kingdomColorProperty?.SetValue(kingdom, primaryColor);
                _kingdomColor2Property?.SetValue(kingdom, secondaryColor);
            }
            catch
            {
                // Ignore reflection failures and rely on initialized kingdom state.
            }
        }

        private static uint? GetKingdomPrimaryBannerColor(Kingdom kingdom)
        {
            if (kingdom == null)
                return null;

            EnsureKingdomPropertyAccessors();

            try
            {
                return _kingdomPrimaryBannerColorProperty?.GetValue(kingdom) as uint?;
            }
            catch
            {
                return null;
            }
        }

        private static uint? GetKingdomSecondaryBannerColor(Kingdom kingdom)
        {
            if (kingdom == null)
                return null;

            EnsureKingdomPropertyAccessors();

            try
            {
                return _kingdomSecondaryBannerColorProperty?.GetValue(kingdom) as uint?;
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureKingdomPropertyAccessors()
        {
            if (_kingdomPrimaryBannerColorProperty != null)
                return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _kingdomPrimaryBannerColorProperty = typeof(Kingdom).GetProperty("PrimaryBannerColor", flags);
            _kingdomSecondaryBannerColorProperty = typeof(Kingdom).GetProperty("SecondaryBannerColor", flags);
            _kingdomColorProperty = typeof(Kingdom).GetProperty("Color", flags);
            _kingdomColor2Property = typeof(Kingdom).GetProperty("Color2", flags);
        }

        private static void MarkKingdomVisualsDirty(Kingdom kingdom)
        {
            if (kingdom == null)
                return;

            foreach (MobileParty mobileParty in MobileParty.All)
            {
                if (mobileParty?.Party?.Owner?.Clan?.Kingdom == kingdom)
                    mobileParty.Party.SetVisualAsDirty();
            }

            foreach (Settlement settlement in kingdom.Settlements)
            {
                settlement?.Party?.SetVisualAsDirty();

                if (settlement?.IsVillage == true && settlement.Village?.VillagerPartyComponent?.MobileParty?.Party != null)
                    settlement.Village.VillagerPartyComponent.MobileParty.Party.SetVisualAsDirty();
                else if ((settlement?.IsCastle == true || settlement?.IsTown == true) && settlement.Town?.GarrisonParty?.Party != null)
                    settlement.Town.GarrisonParty.Party.SetVisualAsDirty();
            }
        }

        internal static void MarkClanVisualsDirty(Clan clan)
        {
            if (clan == null)
                return;

            foreach (MobileParty mobileParty in MobileParty.All)
            {
                if (mobileParty?.Party?.Owner?.Clan == clan)
                    mobileParty.Party.SetVisualAsDirty();
            }

            foreach (Town fief in clan.Fiefs)
            {
                fief?.Settlement?.Party?.SetVisualAsDirty();
                if (fief?.Settlement?.IsVillage == true && fief.Settlement.Village?.VillagerPartyComponent?.MobileParty?.Party != null)
                    fief.Settlement.Village.VillagerPartyComponent.MobileParty.Party.SetVisualAsDirty();
                else if ((fief?.Settlement?.IsCastle == true || fief?.Settlement?.IsTown == true) && fief.Settlement.Town?.GarrisonParty?.Party != null)
                    fief.Settlement.Town.GarrisonParty.Party.SetVisualAsDirty();
            }
        }

        private static void InvokeKingdomClanBannerSync(Kingdom kingdom)
        {
            if (kingdom?.Clans == null)
                return;

            foreach (Clan clan in kingdom.Clans)
                InvokeClanBannerSync(clan);
        }

        private static void InvokeClanBannerSync(Clan clan)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

            if (_clanUpdateBannerColorsAccordingToKingdomMethod == null)
                _clanUpdateBannerColorsAccordingToKingdomMethod = typeof(Clan).GetMethod("UpdateBannerColorsAccordingToKingdom", flags);

            try
            {
                _clanUpdateBannerColorsAccordingToKingdomMethod?.Invoke(clan, null);
            }
            catch
            {
                // Ignore reflection failures and keep the directly assigned palette.
            }
        }

        private static int GetStableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                string text = value ?? string.Empty;
                for (int i = 0; i < text.Length; i++)
                    hash = (hash * 31) + text[i];
                return hash;
            }
        }
    }
}
