using CommunityToolkit.Mvvm.Messaging.Messages;

namespace WorldBuilder.ViewModels;

public class OpenProjectMessage : ValueChangedMessage<string> {
    public OpenProjectMessage(string value) : base(value) {
    
    }
}