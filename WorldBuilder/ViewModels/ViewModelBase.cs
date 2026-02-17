using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Diagnostics.CodeAnalysis;

namespace WorldBuilder.ViewModels;

/// <summary>
/// Base class for all view models in the WorldBuilder application.
/// </summary>
public abstract class ViewModelBase : ObservableValidator {
    [UnconditionalSuppressMessage("Trimming", "IL2026:ObservableValidator uses reflection", Justification = "ObservableValidator is used for validation and is part of CommunityToolkit.Mvvm")]
    protected ViewModelBase() : base() { }

    /// <summary>
    /// Gets the top-level window or view for the current application context.
    /// </summary>
    protected TopLevel TopLevel {
        get {

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return TopLevel.GetTopLevel(desktop.MainWindow)!;
            else if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
                return TopLevel.GetTopLevel(singleView.MainView)!;

            throw new InvalidOperationException("Application lifetime is not supported! Could not get TopLevel.");
        }
    }
}