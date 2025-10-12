using System;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors {
    public class DocumentEventArgs : EventArgs {
        public string DocumentId { get; }
        public BaseDocument Document { get; }
        public DocumentEventArgs(string id, BaseDocument doc) {
            DocumentId = id;
            Document = doc;
        }
    }
}