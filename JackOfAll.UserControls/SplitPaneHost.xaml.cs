using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JackOfAll.Lib.Messaging;
using JackOfAll.Lib.Tools;

namespace JackOfAll.UserControls;

public partial class SplitPaneHost : UserControl, IDisposable, ICloseGuard
{
    private readonly ToolRegistry _registry;
    private readonly IMessageBus _bus;

    private UserControl? _leafContent;
    private string _leafTitle = "";
    private string _leafToolId = "";

    private SplitPaneHost? _first;
    private SplitPaneHost? _second;
    private SplitPaneHost? _parentHost;
    private Orientation _splitOrientation;
    private bool _isSplit;

    public SplitPaneHost(ToolRegistry registry, IMessageBus bus)
    {
        InitializeComponent();
        _registry = registry;
        _bus = bus;
    }

    public void SetLeafContent(UserControl view, string title, string toolId)
    {
        _isSplit = false;
        _first = null;
        _second = null;
        _leafContent = view;
        _leafTitle = title;
        _leafToolId = toolId;
        RebuildAsLeaf();
    }

    public void Split(Orientation orientation)
    {
        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        var firstHost = new SplitPaneHost(_registry, _bus) { _parentHost = this };

        if (_isSplit)
        {
            firstHost._isSplit = true;
            firstHost._first = _first;
            firstHost._second = _second;
            firstHost._splitOrientation = _splitOrientation;
            if (_first is not null) _first._parentHost = firstHost;
            if (_second is not null) _second._parentHost = firstHost;
            firstHost.RebuildAsSplit();
        }
        else if (_leafContent is not null)
        {
            firstHost.SetLeafContent(_leafContent, _leafTitle, _leafToolId);
        }

        var secondHost = new SplitPaneHost(_registry, _bus) { _parentHost = this };
        secondHost.ShowToolPicker();

        _first = firstHost;
        _second = secondHost;
        _isSplit = true;
        _splitOrientation = orientation;
        _leafContent = null;

        RebuildAsSplit();
    }

    private void ShowToolPicker()
    {
        var picker = new NewTabView(_registry);
        picker.ToolSelected += OnPickerToolSelected;
        _leafContent = picker;
        _leafTitle = "Select Tool";
        _leafToolId = "newtab";
        RebuildAsLeaf();
    }

    private void OnPickerToolSelected(string toolId)
    {
        var factory = _registry.Get(toolId);
        if (factory is null) return;
        var view = factory.CreateView(_bus, msg => _bus.Publish(msg));
        SetLeafContent(view, factory.DisplayName, toolId);
    }

    private void OnChildClosed(SplitPaneHost closed)
    {
        if (!closed.ConfirmClose()) return;
        var remaining = closed == _first ? _second! : _first!;

        DisposePane(closed);

        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        if (remaining._isSplit)
        {
            remaining.RootGrid.Children.Clear();
            remaining.RootGrid.RowDefinitions.Clear();
            remaining.RootGrid.ColumnDefinitions.Clear();

            _isSplit = true;
            _first = remaining._first;
            _second = remaining._second;
            _splitOrientation = remaining._splitOrientation;
            if (_first is not null) _first._parentHost = this;
            if (_second is not null) _second._parentHost = this;
            RebuildAsSplit();
        }
        else
        {
            remaining.RootGrid.Children.Clear();

            _isSplit = false;
            _first = null;
            _second = null;
            _leafContent = remaining._leafContent;
            _leafTitle = remaining._leafTitle;
            _leafToolId = remaining._leafToolId;
            RebuildAsLeaf();
        }
    }

    private void RebuildAsLeaf()
    {
        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        if (_parentHost is not null)
        {
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = BuildHeaderBar();
            Grid.SetRow(header, 0);
            RootGrid.Children.Add(header);

            if (_leafContent is not null)
            {
                Grid.SetRow(_leafContent, 1);
                RootGrid.Children.Add(_leafContent);
            }
        }
        else
        {
            if (_leafContent is not null)
                RootGrid.Children.Add(_leafContent);
        }
    }

    private void RebuildAsSplit()
    {
        RootGrid.Children.Clear();
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();

        if (_splitOrientation == Orientation.Horizontal)
        {
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(_first!, 0);
            RootGrid.Children.Add(_first!);

            var splitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = FindBrush("SurfaceBorder"),
                Cursor = Cursors.SizeWE,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            Grid.SetColumn(splitter, 1);
            RootGrid.Children.Add(splitter);

            Grid.SetColumn(_second!, 2);
            RootGrid.Children.Add(_second!);
        }
        else
        {
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(_first!, 0);
            RootGrid.Children.Add(_first!);

            var splitter = new GridSplitter
            {
                Height = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Background = FindBrush("SurfaceBorder"),
                Cursor = Cursors.SizeNS,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            Grid.SetRow(splitter, 1);
            RootGrid.Children.Add(splitter);

            Grid.SetRow(_second!, 2);
            RootGrid.Children.Add(_second!);
        }
    }

    private Border BuildHeaderBar()
    {
        var dock = new DockPanel { LastChildFill = true };

        var closeBtn = MakeHeaderButton("✕", "Close Pane");
        closeBtn.Click += (_, _) => _parentHost?.OnChildClosed(this);
        DockPanel.SetDock(closeBtn, Dock.Right);
        dock.Children.Add(closeBtn);

        var splitDownBtn = MakeHeaderButton("↕", "Split Down");
        splitDownBtn.Click += (_, _) => Split(Orientation.Vertical);
        DockPanel.SetDock(splitDownBtn, Dock.Right);
        dock.Children.Add(splitDownBtn);

        var splitRightBtn = MakeHeaderButton("↔", "Split Right");
        splitRightBtn.Click += (_, _) => Split(Orientation.Horizontal);
        DockPanel.SetDock(splitRightBtn, Dock.Right);
        dock.Children.Add(splitRightBtn);

        var title = new TextBlock
        {
            Text = _leafTitle,
            FontSize = 11,
            Foreground = FindBrush("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        dock.Children.Add(title);

        return new Border
        {
            Background = FindBrush("TabBarBackground"),
            BorderBrush = FindBrush("SurfaceBorder"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 1, 4, 1),
            Child = dock
        };
    }

    private static Button MakeHeaderButton(string content, string tooltip)
    {
        var btn = new Button
        {
            Content = content,
            ToolTip = tooltip,
            FontSize = 11,
            Width = 22,
            Height = 18,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        return btn;
    }

    private static void DisposePane(SplitPaneHost pane)
    {
        if (pane._isSplit)
        {
            if (pane._first is not null) DisposePane(pane._first);
            if (pane._second is not null) DisposePane(pane._second);
        }
        else if (pane._leafContent is IDisposable d)
        {
            d.Dispose();
        }
    }

    public bool ConfirmClose()
    {
        if (_isSplit)
            return (_first?.ConfirmClose() ?? true) && (_second?.ConfirmClose() ?? true);
        return _leafContent is not ICloseGuard guard || guard.ConfirmClose();
    }

    public void Dispose()
    {
        if (_isSplit)
        {
            if (_first is not null) DisposePane(_first);
            if (_second is not null) DisposePane(_second);
        }
        else if (_leafContent is IDisposable d)
        {
            d.Dispose();
        }
    }

    private Brush FindBrush(string key)
        => TryFindResource(key) as Brush ?? Brushes.Gray;
}
