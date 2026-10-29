using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Helpers;

public static class SettingsLayout
{
    public static readonly DependencyProperty AdaptiveProperty = DependencyProperty.RegisterAttached(
        "Adaptive", typeof(bool), typeof(SettingsLayout), new PropertyMetadata(false, Changed));
    public static bool GetAdaptive(DependencyObject item) => (bool)item.GetValue(AdaptiveProperty);
    public static void SetAdaptive(DependencyObject item, bool value) => item.SetValue(AdaptiveProperty, value);

    private static void Changed(DependencyObject item, DependencyPropertyChangedEventArgs e)
    {
        if (item is not Grid grid || !(bool)e.NewValue) return;
        RowLayout? layout = null;
        grid.Loaded += (_, _) => { layout ??= new RowLayout(grid); layout.Arrange(); };
        grid.SizeChanged += (_, _) => layout?.Arrange();
    }

    private sealed class RowLayout
    {
        private readonly Grid _grid;
        private readonly List<(FrameworkElement Child, int Row, int Column, int Span)> _children;
        private readonly int _labelColumn;
        private readonly int _actionColumn;
        private readonly List<FrameworkElement> _actions;
        private readonly GridLength _actionWidth;
        private readonly double _naturalWidth;
        private readonly List<(FrameworkElement Child, double Min, double Max)> _bounds;
        private readonly List<(StackPanel Panel, Orientation Orientation, double Width)> _panels;
        private bool _stacked;
        private RowDefinition? _extra;

        public RowLayout(Grid grid)
        {
            _grid = grid;
            _children = grid.Children.OfType<FrameworkElement>().Select(c => (c, Grid.GetRow(c), Grid.GetColumn(c), Grid.GetColumnSpan(c))).ToList();
            _actionColumn = grid.ColumnDefinitions.Count - 1;
            _labelColumn = Math.Max(0, _actionColumn - 1);
            _actions = _children.Where(c => c.Row == 0 && c.Column == _actionColumn && c.Span == 1 && HasAction(c.Child)).Select(c => c.Child).ToList();
            _actionWidth = _actionColumn >= 0 ? grid.ColumnDefinitions[_actionColumn].Width : GridLength.Auto;
            foreach (var action in _actions) action.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            _naturalWidth = _actions.Select(a => a.DesiredSize.Width).DefaultIfEmpty(0).Max();
            var elements = _actions.SelectMany(Descendants).ToList();
            _bounds = elements.Select(c => (c, c.MinWidth, c.MaxWidth)).ToList();
            _panels = elements.OfType<StackPanel>().Select(p => (p, p.Orientation, p.DesiredSize.Width)).ToList();
        }

        private static IEnumerable<FrameworkElement> Descendants(FrameworkElement element)
        {
            yield return element;
            var children = element switch
            {
                Panel panel => panel.Children.OfType<FrameworkElement>(),
                Border border when border.Child is FrameworkElement child => new[] { child },
                _ => Enumerable.Empty<FrameworkElement>()
            };
            foreach (var child in children)
                foreach (var descendant in Descendants(child)) yield return descendant;
        }

        private static bool HasAction(FrameworkElement element) => element switch
        {
            Button or ToggleSwitch or ComboBox or Slider or NumberBox or TextBox or PasswordBox or CheckBox => true,
            Panel panel => panel.Children.OfType<FrameworkElement>().Any(HasAction),
            Border border when border.Child is FrameworkElement child => HasAction(child),
            _ => false
        };

        public void Arrange()
        {
            if (_actionColumn < 1 || _actions.Count == 0 || _grid.ActualWidth <= 0) return;
            var available = _grid.ActualWidth - _grid.Padding.Left - _grid.Padding.Right;
            var stacked = available < _naturalWidth + Services.UiScale.Size(240);
            var actionSpace = Math.Max(0, available - _grid.ColumnDefinitions.Take(_labelColumn).Sum(c => c.ActualWidth) - _grid.ColumnSpacing * _labelColumn);
            foreach (var (child, min, max) in _bounds)
            {
                child.MinWidth = stacked ? Math.Min(min, actionSpace) : min;
                child.MaxWidth = stacked ? Math.Min(max, actionSpace) : max;
            }
            foreach (var (panel, orientation, width) in _panels)
                panel.Orientation = stacked && orientation == Orientation.Horizontal && width > actionSpace ? Orientation.Vertical : orientation;
            if (stacked == _stacked) return;
            _stacked = stacked;
            if (stacked)
            {
                if (_grid.RowDefinitions.Count == 0) _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                _extra = new RowDefinition { Height = GridLength.Auto };
                _grid.RowDefinitions.Insert(1, _extra);
            }
            else if (_extra is not null) { _grid.RowDefinitions.Remove(_extra); _extra = null; }
            _grid.ColumnDefinitions[_actionColumn].Width = stacked ? new GridLength(0) : _actionWidth;
            foreach (var (child, row, column, span) in _children)
            {
                var action = row == 0 && column == _actionColumn && span == 1;
                Grid.SetRow(child, stacked ? action ? 1 : row > 0 ? row + 1 : row : row);
                Grid.SetColumn(child, stacked && action ? _labelColumn : column);
                Grid.SetColumnSpan(child, stacked && row == 0 && column >= _labelColumn ? 2 : span);
                if (action) child.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            }
            _grid.RowSpacing = Services.UiScale.Size(12);
        }
    }
}
