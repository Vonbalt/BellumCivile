using System;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;

namespace BellumCivile.UI.VanillaTabs.Kingdoms.Hierarchy
{
    public sealed class HierarchyActionMenuWidget : Widget
    {
        private Widget _anchor;
        private Widget _overlay;
        private Widget _menu;
        private float _anchorX;
        private float _anchorY;
        private Widget _treeClip;
        private bool _backgroundPress;
        private float _pressX;
        private float _pressY;

        public HierarchyActionMenuWidget(UIContext context) : base(context) { }

        internal static HierarchyActionMenuWidget FindOwner(Widget widget)
        {
            for (Widget parent = widget.ParentWidget; parent != null; parent = parent.ParentWidget)
                if (parent is HierarchyActionMenuWidget owner)
                    return owner;
            return null;
        }

        internal void Toggle(Widget anchor)
        {
            if (_anchor == anchor) { Close(); return; }
            _overlay = FindChild("TitleActionOverlay", true);
            _menu = FindChild("TitleActionMenu", true);
            if (_overlay == null || _menu == null) return;
            _anchor = anchor;
            _anchorX = anchor.GlobalPosition.X;
            _anchorY = anchor.GlobalPosition.Y;
            PositionMenu();
            _overlay.IsVisible = true;
        }

        internal void Close()
        {
            if (_overlay != null) _overlay.IsVisible = false;
            _anchor = null;
        }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            UpdateBackgroundSelection();
            if (_anchor == null) return;
            // Selection changes, rebuilds, scrolling and panning invalidate the anchor.
            if (!_anchor.IsRecursivelyVisible() || !_anchor.ConnectedToRoot
                || !IsRecursivelyVisible()
                || Math.Abs(_anchor.GlobalPosition.X - _anchorX) > 1f
                || Math.Abs(_anchor.GlobalPosition.Y - _anchorY) > 1f
                || Input.IsKeyPressed(InputKey.Escape)
                || Input.DeltaMouseScroll != 0f)
            {
                Close();
                return;
            }
            PositionMenu();
        }

        private void UpdateBackgroundSelection()
        {
            if (!IsRecursivelyVisible())
            {
                _backgroundPress = false;
                return;
            }

            float x = EventManager.MousePosition.X;
            float y = EventManager.MousePosition.Y;
            if (Input.IsKeyPressed(InputKey.LeftMouseButton))
            {
                _treeClip = FindChild("HierarchyTreeClip", true);
                _backgroundPress = IsTreeBackground(EventManager.LatestMouseDownWidget, x, y);
                _pressX = x;
                _pressY = y;
            }
            if (!_backgroundPress) return;
            float tolerance = 5f * _scaleToUse;
            if (Math.Abs(x - _pressX) > tolerance || Math.Abs(y - _pressY) > tolerance)
                _backgroundPress = false;
            if (Input.IsKeyReleased(InputKey.LeftMouseButton))
            {
                if (_backgroundPress && IsTreeBackground(EventManager.LatestMouseUpWidget, x, y))
                {
                    Close();
                    EventFired("ClearSelection");
                }
                _backgroundPress = false;
            }
        }

        private bool IsTreeBackground(Widget hit, float x, float y)
        {
            if (_treeClip == null || hit == null
                || x < _treeClip.GlobalPosition.X || y < _treeClip.GlobalPosition.Y
                || x >= _treeClip.GlobalPosition.X + _treeClip.Size.X
                || y >= _treeClip.GlobalPosition.Y + _treeClip.Size.Y)
                return false;

            for (Widget current = hit; current != null; current = current.ParentWidget)
            {
                if (current.Id == "TitleActionMenu") return false;
                if (current is ButtonWidget && current.Id != "TitleActionBackdrop") return false;
                if (current == this) return true;
            }
            return false;
        }

        private void PositionMenu()
        {
            float width = _menu.SuggestedWidth * _scaleToUse;
            float height = _menu.SuggestedHeight * _scaleToUse;
            float x = _anchor.GlobalPosition.X - GlobalPosition.X + (_anchor.Size.X - width) / 2f;
            float y = _anchor.GlobalPosition.Y - GlobalPosition.Y + _anchor.Size.Y;
            if (y + height > Size.Y)
                y = _anchor.GlobalPosition.Y - GlobalPosition.Y - height;
            _menu.PositionXOffset = Math.Max(0f, Math.Min(x, Size.X - width)) * _inverseScaleToUse;
            _menu.PositionYOffset = Math.Max(0f, Math.Min(y, Size.Y - height)) * _inverseScaleToUse;
        }
    }

    public sealed class HierarchyOptionsButtonWidget : ButtonWidget
    {
        public HierarchyOptionsButtonWidget(UIContext context) : base(context)
        {
            ClickEventHandlers.Add(widget => HierarchyActionMenuWidget.FindOwner(this)?.Toggle(this));
        }
    }

    public sealed class HierarchyActionButtonWidget : ButtonWidget
    {
        public HierarchyActionButtonWidget(UIContext context) : base(context)
        {
            // Close before the bound command opens an inquiry or rebuilds the tree.
            ClickEventHandlers.Add(widget => HierarchyActionMenuWidget.FindOwner(this)?.Close());
        }
    }
}
