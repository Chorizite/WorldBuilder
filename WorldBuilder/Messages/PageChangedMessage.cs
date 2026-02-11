using CommunityToolkit.Mvvm.Messaging.Messages;
using static WorldBuilder.ViewModels.SplashPageViewModel;

namespace WorldBuilder.Messages;

/// <summary>
/// A message indicating that the splash page should be changed.
/// </summary>
public class SplashPageChangedMessage : ValueChangedMessage<SplashPage> {
    /// <summary>
    /// Initializes a new instance of the SplashPageChangedMessage class with the specified page.
    /// </summary>
    /// <param name="page">The target splash page</param>
    public SplashPageChangedMessage(SplashPage page) : base(page) {
    }
}