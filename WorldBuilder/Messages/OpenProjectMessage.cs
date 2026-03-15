using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Messages;

/// <summary>
/// A message indicating that a project should be opened.
/// </summary>
public class OpenProjectMessage : ValueChangedMessage<string> {
    /// <summary>
    /// Gets the source view model that sent the message.
    /// </summary>
    public SplashPageViewModelBase? SourceViewModel { get; }

    /// <summary>
    /// Initializes a new instance of the OpenProjectMessage class with the specified project file path.
    /// </summary>
    /// <param name="value">The path to the project file to open</param>
    /// <param name="sourceViewModel">The source view model that sent the message</param>
    public OpenProjectMessage(string value, SplashPageViewModelBase? sourceViewModel = null) : base(value) {
        SourceViewModel = sourceViewModel;
    }
}