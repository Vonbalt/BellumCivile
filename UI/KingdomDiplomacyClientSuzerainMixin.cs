using System;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using BellumCivile.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BellumCivile.ViewModelMixin
{
    [ViewModelMixin("RefreshValues", true)]
    internal sealed class KingdomTruceClientSuzerainMixin : BaseViewModelMixin<KingdomTruceItemVM>
    {
        private BannerImageIdentifierVM _clientSuzerainVisual;
        private BannerImageIdentifierVM _clientSuzerainFarVisual;
        private bool _hasClientSuzerain;

        public KingdomTruceClientSuzerainMixin(KingdomTruceItemVM vm) : base(vm)
        {
            RefreshSuzerain();
        }

        [DataSourceProperty]
        public BannerImageIdentifierVM ClientSuzerainFarVisual
        {
            get => _clientSuzerainFarVisual;
            set
            {
                if (value != _clientSuzerainFarVisual)
                {
                    _clientSuzerainFarVisual = value;
                    ViewModel.OnPropertyChangedWithValue(value, nameof(ClientSuzerainFarVisual));
                }
            }
        }

        [DataSourceProperty]
        public BannerImageIdentifierVM ClientSuzerainVisual
        {
            get => _clientSuzerainVisual;
            set
            {
                if (value != _clientSuzerainVisual)
                {
                    _clientSuzerainVisual = value;
                    ViewModel.OnPropertyChangedWithValue(value, nameof(ClientSuzerainVisual));
                }
            }
        }

        [DataSourceProperty]
        public bool HasClientSuzerain
        {
            get => _hasClientSuzerain;
            set
            {
                if (value != _hasClientSuzerain)
                {
                    _hasClientSuzerain = value;
                    ViewModel.OnPropertyChangedWithValue(value, nameof(HasClientSuzerain));
                }
            }
        }

        public override void OnRefresh() => RefreshSuzerain();

        private void RefreshSuzerain()
        {
            Kingdom client = ViewModel.Faction2 as Kingdom;
            Kingdom suzerain = ClientKingdomBehavior.Instance?.GetSuzerain(client);
            BannerImageIdentifierVM visual = suzerain == null ? null : new BannerImageIdentifierVM(suzerain.Banner, true);
            bool agreementIconUsesNearSlot = ViewModel.HasTradeAgreement || ViewModel.HasAlliance;
            ClientSuzerainVisual = agreementIconUsesNearSlot ? null : visual;
            ClientSuzerainFarVisual = agreementIconUsesNearSlot ? visual : null;
            HasClientSuzerain = suzerain != null;
        }
    }

    [ViewModelMixin("UpdateDiplomacyProperties", true)]
    internal sealed class KingdomWarClientSuzerainMixin : BaseViewModelMixin<KingdomWarItemVM>
    {
        private static readonly TextObject WarProgressName = new TextObject("{=8qbkS5D2}War Progress");
        private static readonly TextObject WarScoreName = new TextObject("{=BC_Diplomacy_WarScore}War Score");

        private BannerImageIdentifierVM _clientSuzerainVisual;
        private bool _hasClientSuzerain;
        private KingdomWarComparableStatVM _vanillaWarProgressStat;

        public KingdomWarClientSuzerainMixin(KingdomWarItemVM vm) : base(vm)
        {
            RefreshSuzerain();
            RefreshWarScoreStat();
        }

        [DataSourceProperty]
        public BannerImageIdentifierVM ClientSuzerainVisual
        {
            get => _clientSuzerainVisual;
            set
            {
                if (value != _clientSuzerainVisual)
                {
                    _clientSuzerainVisual = value;
                    ViewModel.OnPropertyChangedWithValue(value, nameof(ClientSuzerainVisual));
                }
            }
        }

        [DataSourceProperty]
        public bool HasClientSuzerain
        {
            get => _hasClientSuzerain;
            set
            {
                if (value != _hasClientSuzerain)
                {
                    _hasClientSuzerain = value;
                    ViewModel.OnPropertyChangedWithValue(value, nameof(HasClientSuzerain));
                }
            }
        }

        public override void OnRefresh()
        {
            RefreshSuzerain();
            RefreshWarScoreStat();
        }

        private void RefreshSuzerain()
        {
            Kingdom client = ViewModel.Faction2 as Kingdom;
            Kingdom suzerain = ClientKingdomBehavior.Instance?.GetSuzerain(client);
            ClientSuzerainVisual = suzerain == null ? null : new BannerImageIdentifierVM(suzerain.Banner, true);
            HasClientSuzerain = suzerain != null;
        }

        private void RefreshWarScoreStat()
        {
            if (ViewModel.Stats == null)
                return;

            string vanillaName = WarProgressName.ToString();
            string bellumName = WarScoreName.ToString();
            int vanillaIndex = FindStatIndex(vanillaName);
            int bellumIndex = FindStatIndex(bellumName);

            if (!WarPeaceRevampBehavior.IsRevampEnabled())
            {
                if (bellumIndex >= 0 && _vanillaWarProgressStat != null)
                    ReplaceStat(bellumIndex, _vanillaWarProgressStat);
                return;
            }

            if (vanillaIndex >= 0)
            {
                _vanillaWarProgressStat = ViewModel.Stats[vanillaIndex];
                bellumIndex = vanillaIndex;
            }

            if (bellumIndex < 0)
                return;

            Kingdom faction1 = ViewModel.Faction1 as Kingdom;
            Kingdom faction2 = ViewModel.Faction2 as Kingdom;
            float faction1RelativeScore = Campaign.Current?
                .GetCampaignBehavior<WarScoreBehavior>()?
                .GetSelfRelativeWarScore(faction1, faction1, faction2) ?? 0f;

            int faction1Score = (int)Math.Round(Math.Max(0f, faction1RelativeScore));
            int faction2Score = (int)Math.Round(Math.Max(0f, -faction1RelativeScore));
            KingdomWarComparableStatVM warScoreStat = new KingdomWarComparableStatVM(
                faction1Score,
                faction2Score,
                WarScoreName,
                Color.FromUint(faction1?.Color ?? 0u).ToString(),
                Color.FromUint(faction2?.Color ?? 0u).ToString(),
                100,
                null,
                null);

            ReplaceStat(bellumIndex, warScoreStat);
        }

        private int FindStatIndex(string name)
        {
            for (int i = 0; i < ViewModel.Stats.Count; i++)
            {
                if (string.Equals(ViewModel.Stats[i]?.Name, name, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        private void ReplaceStat(int index, KingdomWarComparableStatVM stat)
        {
            ViewModel.Stats.RemoveAt(index);
            ViewModel.Stats.Insert(index, stat);
        }
    }
}
