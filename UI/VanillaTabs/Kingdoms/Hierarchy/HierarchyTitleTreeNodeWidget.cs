using TaleWorlds.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets.Encyclopedia;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy
{
    // UIExtenderEx derives generated prefab classes from this widget.
    public class HierarchyTitleTreeNodeWidget : EncyclopediaUnitTreeNodeItemBrushWidget
    {
        public HierarchyTitleTreeNodeWidget(UIContext context) : base(context) { }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            SynchronizeLines();
            RefreshLinePositions();
        }

        internal void SynchronizeLines()
        {
            if (ChildContainer == null || LineContainer == null
                || ChildContainer.ChildCount == LineContainer.ChildCount) return;

            // Native trees create connectors on addition but never remove them on collapse.
            LineContainer.RemoveAllChildren();
            for (int i = 0; i < ChildContainer.ChildCount; i++)
                OnListItemAdded(ChildContainer, ChildContainer.GetChild(i));
        }

        private void RefreshLinePositions()
        {
            if (ChildContainer == null || LineContainer == null || ChildContainer.ChildCount <= 1
                || ChildContainer.ChildCount != LineContainer.ChildCount) return;

            // Expanded descendants also move ancestor branches whose own child count is unchanged.
            float center = GlobalPosition.X + Size.X * 0.5f;
            for (int i = 0; i < ChildContainer.ChildCount; i++)
            {
                var child = ChildContainer.GetChild(i);
                var line = LineContainer.GetChild(i);
                float childCenter = child.GlobalPosition.X + child.Size.X * 0.5f;
                bool left = center > childCenter;
                line.SetState(left ? "Left" : "Right");
                float width = MathF.Abs(center - childCenter);
                line.ScaledSuggestedWidth = width;
                line.ScaledPositionXOffset = (width * 0.5f + 5f * _scaleToUse) * (left ? -1f : 1f);
            }
        }
    }
}
