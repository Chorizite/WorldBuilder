using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Services;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

using Avalonia.Input;
using WorldBuilder.ViewModels;
using WorldBuilder.Lib;

namespace WorldBuilder.Views;

public partial class MainWindow : Window {
    private DebugWindow? _debugWindow;
    private RenderView? _mainRenderView;

    public MainWindow() {
        InitializeComponent();

        var settings = App.Services?.GetService<WorldBuilderSettings>();
        if (settings?.Project != null) {
            Width = settings.Project.WindowWidth;
            Height = settings.Project.WindowHeight;
        }

        Closing += (s, e) => {
            if (settings?.Project != null) {
                settings.Project.WindowWidth = Width;
                settings.Project.WindowHeight = Height;
                settings.Project.Save();
            }
        };
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        if (DataContext is MainViewModel vm) {
            var activeTab = vm.ToolTabs.FirstOrDefault(t => t.IsSelected);
            if (activeTab?.ViewModel is IHotkeyHandler handler) {
                if (handler.HandleHotkey(e)) {
                    e.Handled = true;
                    return;
                }
            }
        }
        base.OnKeyDown(e);
    }

    /// <summary>
    /// Opens the debug window with a shared context
    /// </summary>
    public void OpenDebugWindow() {
        // Close existing debug window if open
        if (_debugWindow != null) {
            // Check if window is closed by checking if it's still in the open windows collection
            var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            bool isOpen = false;
            if (desktop != null) {
                foreach (var win in desktop.Windows) {
                    if (win == _debugWindow) {
                        isOpen = true;
                        break;
                    }
                }
            }
            if (!isOpen) {
                _debugWindow = null;
            }
            else {
                // Bring existing window to front
                _debugWindow.Activate();
                return;
            }
        }

        // Find the main render view from the content
        _mainRenderView = FindMainRenderView(this);
        if (_mainRenderView == null) {
            // Try to find it from the main view
            if (this.Content is UserControl mainUserControl) {
                _mainRenderView = FindRenderView(mainUserControl);
            }
        }

        // Create and show the debug window
        _debugWindow = new DebugWindow();
        _debugWindow.Show();

        // Pass the landscape data to the debug window
        if (_mainRenderView != null) {
            _debugWindow.SetLandscape(_mainRenderView.LandscapeDocument, _mainRenderView.Dats);
        }
    }

    private RenderView? FindMainRenderView(Control control) {
        if (control is RenderView renderView) {
            return renderView;
        }

        // Search through children
        if (control is Panel panel) {
            foreach (Control child in panel.Children) {
                var result = FindMainRenderView(child);
                if (result != null) return result;
            }
        }
        else if (control is UserControl uc) {
            return FindRenderView(uc);
        }

        return null;
    }

    private RenderView? FindRenderView(UserControl control) {
        // Find RenderView in the control hierarchy
        if (control is MainView mainView) {
            // Look for LandscapeView which contains the RenderView
            foreach (var child in mainView.GetVisualDescendants()) {
                if (child is Modules.Landscape.LandscapeView landscapeView) {
                    // Find the RenderView inside the LandscapeView
                    var renderView = FindRenderViewInControl(landscapeView);
                    if (renderView != null) return renderView;
                }
                else if (child is RenderView rv) {
                    return rv;
                }
            }
        }

        // General search in visual descendants
        foreach (var child in control.GetVisualDescendants()) {
            if (child is RenderView renderView) {
                return renderView;
            }
            else if (child is Control controlChild) {
                var result = FindMainRenderView(controlChild);
                if (result != null) return result;
            }
        }

        return null;
    }

    private RenderView? FindRenderViewInControl(Control control) {
        if (control is RenderView renderView) {
            return renderView;
        }

        foreach (var child in control.GetVisualDescendants()) {
            if (child is RenderView rv) {
                return rv;
            }
            else if (child is Control childControl) {
                var result = FindRenderViewInControl(childControl);
                if (result != null) return result;
            }
        }

        // Also check the control itself
        if (control is RenderView controlRV) {
            return controlRV;
        }

        return null;
    }
}