using CommunityToolkit.Mvvm.Messaging.Messages;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Messages;

public class ShowProjectErrorDetailsMessage : ValueChangedMessage<RecentProject> {
    public ShowProjectErrorDetailsMessage(RecentProject project) : base(project) {
    }
}