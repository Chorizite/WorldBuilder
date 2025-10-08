namespace WorldBuilder.Shared.Models {
    public class DocumentStats {
        public string DocumentId { get; set; }
        public int DocumentCount { get; set; }
        public int UpdateCount { get; set; }
        public int SnapshotCount { get; set; }
    }
}