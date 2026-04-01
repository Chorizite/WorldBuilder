using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

using WorldBuilder.Shared.Models;
using WorldBuilder.Services;
using WorldBuilder.Views;
using System.Linq;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace WorldBuilder.Modules.Landscape;

public partial class LandscapeView : UserControl {
    private DispatcherTimer? _updateTimer;
    private TextBlock? _locationText;
    private Button? _copyButton;
    private RenderView? _renderView;
    private string? _lastLocationString;
    private GridSplitter? _rightSplitter;
    private GridSplitter? _sideBarSplitter;
    private WorldBuilderSettings? _settings;
    private Avalonia.Controls.Image? _minimapImage;
    private Avalonia.Controls.Shapes.Ellipse? _minimapIndicator;

    // Setting Grid.Width at DesignTime causes the content in the right column to not stretch to greater widths
    // Setting Grid.MinWidth sets the starting width properly, but doesn't actually enforce a MinWidth constraint when dragging GridSplitter
    // https://github.com/AvaloniaUI/Avalonia/issues/5868
    // Unfortunately, the solution seems to be just manually managing it in code-behind.
    // If we run into this problem with other GridSplitters, then recommended this code is centralized into a reusable component
    private static readonly int RIGHT_PANELS_STARTING_WIDTH = 300;
    private static readonly int RIGHT_PANELS_MIN_WIDTH = 300;
    private static readonly int RIGHT_PANELS_MAX_WIDTH_PCT = 50;
    
    private static readonly int PROPERTIES_PANEL_STARTING_HEIGHT_PCT = 50;
    private static readonly int PROPERTIES_PANEL_MIN_TOP_DIST = 75;
    private static readonly int PROPERTIES_PANEL_MAX_BOTTOM_DIST = 100;

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
        _sideBarSplitter = this.FindControl<GridSplitter>("SideBarSplitter");
        _settings = WorldBuilder.App.Services?.GetService<WorldBuilderSettings>();
        _minimapImage = this.FindControl<Avalonia.Controls.Image>("MinimapImage");
        _minimapIndicator = this.FindControl<Avalonia.Controls.Shapes.Ellipse>("MinimapPlayerIndicator");

        if (_minimapIndicator != null) {
            _minimapIndicator.Margin = new Avalonia.Thickness(100 - 4, 100 - 4, 0, 0);
        }

        var rootLayoutGrid = this.FindControl<Grid>("RootLayoutGrid");
        if (rootLayoutGrid != null && rootLayoutGrid.ColumnDefinitions.Count >= 4) {
            var rightPanelsColumn = rootLayoutGrid.ColumnDefinitions[3];
            
            // Load saved width from settings
            var savedWidth = _settings?.Landscape?.RightPanelWidth ?? RIGHT_PANELS_STARTING_WIDTH;
            rightPanelsColumn.Width = new GridLength(savedWidth);
        }
        ApplyPropertiesPanelSize();

        // Subscribe to right panel size changes
        var rightPanels = this.FindControl<Grid>("RightPanels");
        if (rightPanels != null) {
            rightPanels.SizeChanged += OnRightPanelSizeChanged;
        }
        var propertiesPanel = this.FindControl<UserControl>("PropertiesPanel");
        if (propertiesPanel != null) {
            propertiesPanel.SizeChanged += OnPropertiesPanelSizeChanged;
        }

        if (_rightSplitter != null) {
            _rightSplitter.DragCompleted += OnSplitterDragCompleted;
        }
        if (_sideBarSplitter != null) {
            _sideBarSplitter.DragCompleted += OnSideBarSplitterDragCompleted;
        }

        // Setup tab control handler
        if (rightPanels != null) {
            var actualTabControl = rightPanels.Children.OfType<TabControl>().FirstOrDefault();
            if (actualTabControl != null) {
                actualTabControl.SelectionChanged += OnTabSelectionChanged;
                
                // Restore active tab from settings
                var activeTab = _settings?.Project?.ActiveTab ?? "Layers";
                var items = actualTabControl.Items;
                if (items != null) {
                    foreach (TabItem? item in items) {
                        if (item?.Header?.ToString() == activeTab) {
                            actualTabControl.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
        }

        TryInitializeToolContext();
        StartUpdateTimer();
    }

    public void ApplyPropertiesPanelSize() {
        var rightPanels = this.FindControl<Grid>("RightPanels");
        if (rightPanels != null && rightPanels.RowDefinitions.Count >= 3) {
            var propertiesPanelRow = rightPanels.RowDefinitions[2];
            var topRow = rightPanels.RowDefinitions[0]; // TabControl

            // Load saved height from settings
            var savedHeightPercent = _settings?.Landscape?.PropertiesPanelHeight ?? PROPERTIES_PANEL_STARTING_HEIGHT_PCT / 100.0;

            // Set both rows to star sizing
            topRow.Height = new GridLength(1.0 - savedHeightPercent, GridUnitType.Star);    // Remaining space for TabControl
            propertiesPanelRow.Height = new GridLength(savedHeightPercent, GridUnitType.Star);
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (_copyButton != null) {
            _copyButton.Click -= OnCopyLocationClicked;
        }

        var rightPanels = this.FindControl<Grid>("RightPanels");
        if (rightPanels != null) {
            rightPanels.SizeChanged -= OnRightPanelSizeChanged;
            
            // Cleanup tab selection event
            var actualTabControl = rightPanels.Children.OfType<TabControl>().FirstOrDefault();
            if (actualTabControl != null) {
                actualTabControl.SelectionChanged -= OnTabSelectionChanged;
            }
        }

        var propertiesPanel = this.FindControl<UserControl>("PropertiesPanel");
        if (propertiesPanel != null) {
            propertiesPanel.SizeChanged -= OnPropertiesPanelSizeChanged;
        }
        
        if (_rightSplitter != null) {
            _rightSplitter.DragCompleted -= OnSplitterDragCompleted;
        }
        if (_sideBarSplitter != null) {
            _sideBarSplitter.DragCompleted -= OnSideBarSplitterDragCompleted;
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

    private void OnPropertiesPanelSizeChanged(object? sender, SizeChangedEventArgs e) {
        // Enforce minimum and maximum height properly when dragging GridSplitter, since setting MinHeight/MaxHeight in XAML doesn't work due to Avalonia issue #5868
        if (_sideBarSplitter != null) {
            var rightPanelsGrid = _sideBarSplitter.Parent as Grid;
            if (rightPanelsGrid != null && rightPanelsGrid.RowDefinitions.Count >= 3) {
                var totalHeight = rightPanelsGrid.Bounds.Height;

                if (totalHeight > 0) {
                    var topRow = rightPanelsGrid.RowDefinitions[0]; // TabControl
                    var bottomRow = rightPanelsGrid.RowDefinitions[2]; // PropertiesPanel
                    var currentHeight = e.NewSize.Height;
                    var splitterHeight = 4.0; // Fixed height of the splitter row
                    var availableHeightForStars = totalHeight - splitterHeight;

                    // Apply constraints using star format to prevent bouncing
                    if (currentHeight < PROPERTIES_PANEL_MAX_BOTTOM_DIST) {
                        var constrainedStarValue = Math.Clamp(PROPERTIES_PANEL_MAX_BOTTOM_DIST / availableHeightForStars, 0, 1);
                        topRow.Height = new GridLength(1.0 - constrainedStarValue, GridUnitType.Star);
                        bottomRow.Height = new GridLength(constrainedStarValue, GridUnitType.Star);
                    }
                    else if (currentHeight > totalHeight - PROPERTIES_PANEL_MIN_TOP_DIST) {
                        var maxHeight = totalHeight - PROPERTIES_PANEL_MIN_TOP_DIST;
                        var constrainedStarValue = Math.Clamp(maxHeight / availableHeightForStars, 0, 1);
                        topRow.Height = new GridLength(1.0 - constrainedStarValue, GridUnitType.Star);
                        bottomRow.Height = new GridLength(constrainedStarValue, GridUnitType.Star);
                    }
                }
            }
        }
    }

    private void OnSplitterDragCompleted(object? sender, VectorEventArgs e) {
        SaveRightPanelWidth();
    }
    
    private void OnSideBarSplitterDragCompleted(object? sender, VectorEventArgs e) {
        var rightPanelsGrid = this.FindControl<Grid>("RightPanels");
        if (rightPanelsGrid != null && rightPanelsGrid.RowDefinitions.Count >= 3) {
            var topRow = rightPanelsGrid.RowDefinitions[0]; // TabControl
            var bottomRow = rightPanelsGrid.RowDefinitions[2]; // PropertiesPanel
            var totalHeight = rightPanelsGrid.Bounds.Height;
            var splitterHeight = 4.0; // Fixed height of the splitter row
            
            if (totalHeight > 0 && bottomRow.Height.Value > 1) {
                var currentHeight = bottomRow.Height.Value;
                var availableHeightForStars = totalHeight - splitterHeight;
                
                // Calculate star values for both rows
                var bottomStarValue = currentHeight / availableHeightForStars;
                var topStarValue = 1.0 - bottomStarValue; // Remaining space for TabControl
                
                // Set both rows to star sizing
                topRow.Height = new GridLength(topStarValue, GridUnitType.Star);
                bottomRow.Height = new GridLength(bottomStarValue, GridUnitType.Star);
            }
        }
        SavePropertiesPanelHeight();
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

    private void SavePropertiesPanelHeight() {
        var rightPanelsGrid = this.FindControl<Grid>("RightPanels");
        if (rightPanelsGrid != null && rightPanelsGrid.RowDefinitions.Count >= 3 && _settings != null) {
            var bottomRow = rightPanelsGrid.RowDefinitions[2];
            // Read the star value directly (already in percentage format)
            if (bottomRow.Height.IsStar) {
                _settings.Landscape.PropertiesPanelHeight = bottomRow.Height.Value;
                _settings.Save();
            }
        }
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e) {
        if (sender is TabControl tabControl && tabControl.SelectedItem is TabItem selectedTab) {
            var tabHeader = selectedTab.Header?.ToString();
            if (!string.IsNullOrEmpty(tabHeader) && DataContext is LandscapeViewModel vm) {
                vm.OnActiveTabChanged(tabHeader);
            }
        }
    }

    private void OnViewportDragOver(object? sender, DragEventArgs e) {
#pragma warning disable CS0618
        if (e.Data.Contains("WorldBuilder.SetupId") && DataContext is LandscapeViewModel vm) {
            var objTool = vm.Tools.OfType<WorldBuilder.Shared.Modules.Landscape.Tools.ObjectManipulationTool>().FirstOrDefault();
            if (objTool != null) {
                if (vm.ActiveTool != objTool) {
                    vm.ActiveTool = objTool;
                }

                var setupId = (uint)e.Data.Get("WorldBuilder.SetupId")!;
#pragma warning restore CS0618
                if (!objTool.IsPlacementMode || objTool.PlacementSetupId != setupId) {
                    objTool.EnterPlacementMode(setupId);
                }

                var pos = e.GetPosition(_renderView!);
                var inputEvent = CreateViewportInputEvent(pos);
                objTool.OnPointerMoved(inputEvent);

                e.DragEffects = DragDropEffects.Copy | DragDropEffects.Move;
                e.Handled = true;
            }
        }
    }

    private async void OnViewportDrop(object? sender, DragEventArgs e) {
#pragma warning disable CS0618
        if (e.Data.Contains("WorldBuilder.SetupId") && DataContext is LandscapeViewModel vm) {
            var objTool = vm.Tools.OfType<WorldBuilder.Shared.Modules.Landscape.Tools.ObjectManipulationTool>().FirstOrDefault();
            if (objTool != null && objTool.IsPlacementMode) {
                var pos = e.GetPosition(_renderView!);
#pragma warning restore CS0618
                var inputEvent = CreateViewportInputEvent(pos);
                await objTool.CommitPlacementAsync(inputEvent);
                _renderView?.Focus();
                e.Handled = true;
            }
        }
    }

    private ViewportInputEvent CreateViewportInputEvent(Point pos) {
        if (DataContext is LandscapeViewModel vm && _renderView != null) {
            var topLevel = TopLevel.GetTopLevel(this);
            var scaling = topLevel?.RenderScaling ?? 1.0;
            var scaledPos = new Vector2((float)(pos.X * scaling), (float)(pos.Y * scaling));
            var viewportSize = new Vector2((float)(_renderView.Bounds.Width * scaling), (float)(_renderView.Bounds.Height * scaling));

            return new ViewportInputEvent {
                Position = scaledPos,
                ViewportSize = viewportSize,
                IsLeftDown = true,
                RayOrigin = vm.Camera?.Position ?? Vector3.Zero,
            };
        }
        return new ViewportInputEvent();
    }

    private void OnMinimapPointerPressed(object? sender, PointerPressedEventArgs e) {
        if (DataContext is LandscapeViewModel vm && _renderView?.Camera != null) {
            // Get X, Y coordinates of the click within the image (0-200)
            var point = e.GetPosition(_minimapImage);
            
            // Minimap range is 20 landblocks (same as TerrainRenderManager)
            float range = 20 * 192f;
            
            // Calculate offset from center. X: 0 is left (-range/2), 200 is right (+range/2)
            float offsetX = (float)(((point.X / 200.0) - 0.5) * range);
            
            // Y: UI coordinates go down, world coordinates go up, so invert
            float offsetY = (float)((0.5 - (point.Y / 200.0)) * range);
    
            // Current position
            var currentPos = _renderView.Camera.Position;
            
            // New position
            var newPos = new Vector3(currentPos.X + offsetX, currentPos.Y + offsetY, currentPos.Z);
    
            // Find cellId at the new location for teleportation
            uint cellId = _renderView.GetEnvCellAt(newPos);
            
            // Teleport
            vm.GameScene?.Teleport(newPos, cellId);
            
            e.Handled = true;
        }
    }
}
