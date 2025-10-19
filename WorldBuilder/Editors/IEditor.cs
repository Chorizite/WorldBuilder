using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors {
    public interface IEditor {
        DocumentManager DocumentManager { get; }
        CommandHistory History { get; }
        Task<BaseDocument?> LoadDocumentAsync(string documentId, Type documentType, bool forceReload = false);
        Task UnloadDocumentAsync(string documentId);
        IEnumerable<BaseDocument> GetActiveDocuments();
        BaseDocument? GetDocument(string documentId);
        event EventHandler<DocumentEventArgs> DocumentLoaded;
        event EventHandler<DocumentEventArgs> DocumentUnloaded;
    }
}
