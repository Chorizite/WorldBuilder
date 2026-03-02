using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

using WorldBuilder.Shared.Models;
using WorldBuilder.Services;
using WorldBuilder.Views;

namespace WorldBuilder.Modules.Landscape;

public partial class LandscapeView : UserControl {
    private DispatcherTimer? _updateTimer;
    private TextBlock? _locationText;
    private Button? _copyButton;
    private RenderView? _renderView;
    private string? _lastLocationString;
    private GridSplitter? _rightSplitter;
    private WorldBuilderSettings? _settings;

    // Setting Grid.Width at DesignTime causes the content in the right column to not stretch to greater widths
    // Setting Grid.MinWidth sets the starting width properly, but doesn't actually enforce a MinWidth constraint when dragging GridSplitter
    // https://github.com/AvaloniaUI/Avalonia/issues/5868
    // Unfortunately, the solution seems to be just manually managing it in code-behind.
    // If we run into this problem with other GridSplitters, then recommended this code is centralized into a reusable component
    private static readonly int RIGHT_PANELS_STARTING_WIDTH = 300;
    private static readonly int RIGHT_PANELS_MIN_WIDTH = 300;
    private static readonly int RIGHT_PANELS_MAX_WIDTH_PCT = 50;

    public LandscapeView() {
        InitializeComponent();

        // Wait for control to load to access the visual tree
        this.AttachedToVisualTree += OnAttachedToVisualTree;
        this.DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        _locationText = this.FindControl<TextBlock>("LocationText");
        _copyButton = this.FindControl<Button>("CopyLocationButton");
        if (_copyButton != null) {
            _copyButton.Click += OnCopyLocationClicked;
        }
        _renderView = this.FindControl<RenderView>("RenderView");
        _rightSplitter = this.FindControl<GridSplitter>("RightSplitter");
        _settings = WorldBuilder.App.Services?.GetService<WorldBuilderSettings>();

        var rootLayoutGrid = this.FindControl<Grid>("RootLayoutGrid");
        if (rootLayoutGrid != null && rootLayoutGrid.ColumnDefinitions.Count >= 4) {
            var rightPanelsColumn = rootLayoutGrid.ColumnDefinitions[3];
            
            // Load saved width from settings
            var savedWidth = _settings?.Landscape?.RightPanelWidth ?? RIGHT_PANELS_STARTING_WIDTH;
            rightPanelsColumn.Width = new GridLength(savedWidth);
        }
        
        // Subscribe to right panel size changes
        var rightPanels = this.FindControl<Grid>("RightPanels");
        if (rightPanels != null) {
            rightPanels.SizeChanged += OnRightPanelSizeChanged;
        }
        if (_rightSplitter != null) {
            _rightSplitter.DragCompleted += OnSplitterDragCompleted;
        }

        TryInitializeToolContext();
        StartUpdateTimer();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (_copyButton != null) {
            _copyButton.Click -= OnCopyLocationClicked;
        }

        var rightPanels = this.FindControl<Grid>("RightPanels");
        if (rightPanels != null) {
            rightPanels.SizeChanged -= OnRightPanelSizeChanged;
        }
        if (_rightSplitter != null) {
            _rightSplitter.DragCompleted -= OnSplitterDragCompleted;
        }
        StopUpdateTimer();
    }

    private async void OnCopyLocationClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
        if (!string.IsNullOrEmpty(_lastLocationString)) {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null) {
                await topLevel.Clipboard.SetTextAsync(_lastLocationString);
            }
        }
    }

    private void StartUpdateTimer() {
        if (_updateTimer == null) {
            _updateTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _updateTimer.Tick += OnUpdateTick;
        }
        _updateTimer.Start();
    }

    private void StopUpdateTimer() {
        _updateTimer?.Stop();
    }

    private void OnUpdateTick(object? sender, EventArgs e) {
        if (_locationText == null || _renderView?.Camera == null || _renderView.LandscapeDocument?.Region == null) return;

        var pos = _renderView.Camera.Position;
        var cellId = _renderView.GetEnvCellAt(pos);
        var loc = Position.FromGlobal(pos, _renderView.LandscapeDocument.Region, cellId != 0 ? cellId : null);

        loc.Rotation = _renderView.Camera.Rotation;
        _lastLocationString = loc.ToLandblockString();

        _locationText.Text = loc.ToMapString() + $" | {_lastLocationString}";
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        TryInitializeToolContext();
    }

    private void TryInitializeToolContext() {
        if (DataContext is LandscapeViewModel vm) {
            var renderView = this.FindControl<RenderView>("RenderView");
            if (renderView != null) {
                if (renderView.Camera != null) {
                    InitializeContext(vm, renderView);
                }
                else {
                    // Subscribe to ensure we init when GL is ready
                    renderView.SceneInitialized -= OnRenderViewInitialized;
                    renderView.SceneInitialized += OnRenderViewInitialized;
                }
            }
        }
    }

    private void OnRenderViewInitialized() {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            TryInitializeToolContext();
        });
    }

    private void InitializeContext(LandscapeViewModel vm, RenderView renderView) {
        if (renderView.Camera != null) {
            vm.InitializeToolContext(renderView.Camera, (x, y) => {
                renderView.InvalidateLandblock(x, y);
            });
        }
    }

    private void OnRightPanelSizeChanged(object? sender, SizeChangedEventArgs e) {
        // Enforce minimum and maximum width properly when dragging GridSplitter, since setting MinWidth/MaxWidth in XAML doesn't work due to Avalonia issue #5868
        if (_rightSplitter != null) {
            var rightColumn = (_rightSplitter.Parent as Grid)?.ColumnDefinitions[3];
            if (rightColumn != null) {
                // Enforce minimum width
                if (RIGHT_PANELS_MIN_WIDTH > 0 && e.NewSize.Width < RIGHT_PANELS_MIN_WIDTH) {
                    rightColumn.Width = new GridLength(RIGHT_PANELS_MIN_WIDTH);
                }
                // Enforce maximum width
                else if (RIGHT_PANELS_MAX_WIDTH_PCT > 0) {
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel != null) {
                        var maxAllowedWidth = topLevel.ClientSize.Width * RIGHT_PANELS_MAX_WIDTH_PCT / 100.0;
                        if (e.NewSize.Width > maxAllowedWidth) {
                            rightColumn.Width = new GridLength(maxAllowedWidth);
                        }
                    }
                }
            }
        }
    }

    private void OnSplitterDragCompleted(object? sender, VectorEventArgs e) {
        SaveRightPanelWidth();
    }
    
    private void SaveRightPanelWidth() {
        var rootLayoutGrid = this.FindControl<Grid>("RootLayoutGrid");
        if (rootLayoutGrid != null && rootLayoutGrid.ColumnDefinitions.Count >= 4 && _settings != null) {
            var rightPanelsColumn = rootLayoutGrid.ColumnDefinitions[3];
            var currentWidth = rightPanelsColumn.Width.Value;
            
            if (Math.Abs(currentWidth - _settings.Landscape.RightPanelWidth) > 0.1) {
                _settings.Landscape.RightPanelWidth = currentWidth;
                _settings.Save();
            }
        }
    }
}
