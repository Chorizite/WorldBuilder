using CommunityToolkit.Mvvm.Messaging.Messages;
using static WorldBuilder.ViewModels.SplashPageViewModel;

namespace WorldBuilder.ViewModels;


public class SplashPageChangedMessage : ValueChangedMessage<SplashPage> {
    public SplashPageChangedMessage(SplashPage page) : base(page) {
    }
}