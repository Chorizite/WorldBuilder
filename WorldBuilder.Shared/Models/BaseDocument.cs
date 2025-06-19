using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using YDotNet.Document.Options;
using YDotNet.Document.Transactions;
using YDotNet.Document;

namespace WorldBuilder.Shared.Models {
    public abstract class BaseDocument : ObservableObject, IDisposable {
        private string documentId;
        private bool isInitialized;
        protected Project project;

        public string DocumentId {
            get => documentId;
            protected set => SetProperty(ref documentId, value);
        }

        public Doc Doc { get; private set; }
        public DocumentManager Manager { get; private set; }

        protected readonly HashSet<string> appliedUpdateIds = new HashSet<string>();
        protected readonly object docLock = new object();

        public bool IsInitialized {
            get => isInitialized;
            private set => SetProperty(ref isInitialized, value);
        }
        public bool NeedsUpdate { get; private set; }

        internal void Initialize(string id, DocumentManager manager) {
            if (IsInitialized)
                throw new InvalidOperationException("Document already initialized");

            DocumentId = id;
            Manager = manager;
            project = manager.Project;
            Doc = new Doc(new DocOptions { CollectionId = DocumentId });

            InitializeDocument();

            Doc.ObserveUpdatesV2(async e => {
                Console.WriteLine($"{Doc.CollectionId} update: {e.Update.Length} bytes");
                var updateId = Manager.StoreUpdate(DocumentId, e.Update);
                NeedsUpdate = true;
            });

            IsInitialized = true;
            OnPropertyChanged(nameof(IsInitialized));
        }

        internal void Update() {
            if (NeedsUpdate) {
                OnDocumentUpdated();
                NeedsUpdate = false;
            }
        }

        protected abstract void InitializeDocument();
        protected virtual void OnDocumentUpdated() { }

        public TransactionUpdateResult ApplyUpdate(string updateId, byte[] update) {
            TransactionUpdateResult res = TransactionUpdateResult.Other;
            lock (docLock) {
                if (appliedUpdateIds.Contains(updateId)) {
                    Console.WriteLine($"{Doc.CollectionId}: Skipping duplicate update {updateId}");
                    return TransactionUpdateResult.Ok;
                }

                ExecuteInTransaction(e => {
                    res = e.ApplyV2(update);
                    Console.WriteLine($"{Doc.CollectionId}: Applied update {updateId} ({update.Length} bytes, result: {res})");
                });
            }

            return res;
        }

        protected void ExecuteInTransaction(Action<Transaction> action) {
            lock (docLock) {
                using var writeTransaction = Doc.WriteTransaction();
                action(writeTransaction);
                writeTransaction.Commit();
            }
        }

        protected T ReadInTransaction<T>(Func<Transaction, T> func) {
            lock (docLock) {
                using var readTransaction = Doc.ReadTransaction();
                return func(readTransaction);
            }
        }

        public virtual void Dispose() {
            Doc?.Dispose();
        }
    }
}
