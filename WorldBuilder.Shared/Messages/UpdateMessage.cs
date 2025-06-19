namespace WorldBuilder.Shared.Messages {
    public class UpdateMessage {
        public string DocumentName { get; set; }
        public byte[] Update { get; set; }

        public UpdateMessage(string documentName, byte[] update) {
            DocumentName = documentName;
            Update = update;
        }
    }
}