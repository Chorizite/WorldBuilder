namespace WorldBuilder.Shared.Models {
    public class DBSnapshot {
        public Guid Id { get; set; }
        public string DocumentId { get; set; }
        public string Name { get; set; }
        public byte[] Data { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
