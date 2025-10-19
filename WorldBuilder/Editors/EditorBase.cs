using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors {
    public abstract class EditorBase : IEditor, IDisposable {
        protected readonly ConcurrentDictionary<string, BaseDocument> ActiveDocuments = new();
        public DocumentManager DocumentManager { get; }
        public CommandHistory History { get; }
        public event EventHandler<DocumentEventArgs> DocumentLoaded;
        public event EventHandler<DocumentEventArgs> DocumentUnloaded;

        protected EditorBase(DocumentManager docManager, WorldBuilderSettings settings, ILogger logger) {
            DocumentManager = docManager;
            History = new CommandHistory(settings.App, this, logger);
        }


        public async Task<T?> LoadDocumentAsync<T>(string documentId, bool forceReload = false) where T : BaseDocument {
            var result = await LoadDocumentAsync(documentId, typeof(T), forceReload);
            if (result == null) return default;
            return result as T;
        }

        public virtual async Task<BaseDocument?> LoadDocumentAsync(string documentId, Type documentType, bool forceReload = false) {
            Console.WriteLine($"Loading document {documentId}");
            if (!forceReload && ActiveDocuments.TryGetValue(documentId, out var doc)) {
                return doc;
            }

            var loadedDoc = await DocumentManager.GetOrCreateDocumentAsync(documentId, documentType);
            if (loadedDoc != null) {
                ActiveDocuments[documentId] = loadedDoc;
                DocumentLoaded?.Invoke(this, new DocumentEventArgs(documentId, loadedDoc));
            }
            return loadedDoc;
        }

        public virtual async Task UnloadDocumentAsync(string documentId) {
            if (ActiveDocuments.TryRemove(documentId, out var doc)) {
                await DocumentManager.CloseDocumentAsync(documentId);
                DocumentUnloaded?.Invoke(this, new DocumentEventArgs(documentId, doc));
            }
        }

        public IEnumerable<BaseDocument> GetActiveDocuments() => ActiveDocuments.Values;

        public BaseDocument? GetDocument(string documentId) => ActiveDocuments.TryGetValue(documentId, out var doc) ? doc : null;

        public virtual void Dispose() {
            foreach (var docId in ActiveDocuments.Keys.ToArray()) {
                UnloadDocumentAsync(docId).GetAwaiter().GetResult();
            }
            ActiveDocuments.Clear();
        }
    }
}
