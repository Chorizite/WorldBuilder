namespace WorldBuilder.Shared.Messages {
    public class JoinProjectMessage {
        public string Username { get; }

        public JoinProjectMessage(string username) {
            Username = username;
        }
    }
}