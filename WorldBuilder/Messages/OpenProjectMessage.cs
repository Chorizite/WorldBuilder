using CommunityToolkit.Mvvm.Messaging.Messages;

namespace WorldBuilder.Messages;

/// <summary>
/// A message indicating that a project should be opened.
/// </summary>
public class OpenProjectMessage : ValueChangedMessage<string> {
    /// <summary>
    /// Initializes a new instance of the OpenProjectMessage class with the specified project file path.
    /// </summary>
    /// <param name="value">The path to the project file to open</param>
    public OpenProjectMessage(string value) : base(value) {

    }
}