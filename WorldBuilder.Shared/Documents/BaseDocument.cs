using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Runtime.Serialization;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Documents {
    public class UpdateEventArgs : EventArgs {
        /// <summary>
        /// The document that generated this event
        /// </summary>
        public BaseDocument Document { get; set; }

        /// <summary>
        /// The event that generated this update
        /// </summary>
        public BaseDocumentEvent Event { get; set; }

        public UpdateEventArgs(BaseDocument document, BaseDocumentEvent @event) {
            Document = document;
            Event = @event;
        }
    }

    /// <summary>
    /// Base class for all editable worldbuilder documents. These are not tied directly to a single
    /// DBObj entry in the dat files, some will span multiple DBObj entries.
    /// </summary>
    public abstract partial class BaseDocument : ObservableObject, IDisposable {
        protected readonly ILogger _logger;
        protected readonly object _stateLock = new();

        public virtual string Id { get; set; } = string.Empty;

        /// <summary>
        /// The type of the document
        /// </summary>
        public abstract string Type { get; }

        /// <summary>
        /// Event raised when the document is updated locally
        /// </summary>
        public event EventHandler<UpdateEventArgs>? Update;

        protected string? _cacheDirectory;

        public BaseDocument(ILogger logger) {
            _logger = logger;
            Update += (s, e) => {
                OnPropertyChanged();
            };
        }

        public void SetCacheDirectory(string cacheDirectory) => _cacheDirectory = cacheDirectory;

        /// <summary>
        /// Raise the update event
        /// </summary>
        /// <param name="event"></param>
        protected void OnUpdate(BaseDocumentEvent @event) => Update?.Invoke(this, new UpdateEventArgs(this, @event));

        /// <summary>
        /// Initialize a newly loaded document, this should load any needed data from the dat files
        /// </summary>
        /// <param name="datreader"></param>
        /// <returns></returns>
        public virtual Task<bool> InitAsync(IDatReaderWriter datreader) {
            lock (_stateLock) {
                return InitInternal(datreader);
            }
        }

        /// <summary>
        /// Initialize a newly loaded document, this should load any needed data from the dat files
        /// </summary>
        /// <param name="datreader"></param>
        /// <returns></returns>
        protected abstract Task<bool> InitInternal(IDatReaderWriter datreader);

        /// <summary>
        /// Serialize the document to a byte array
        /// </summary>
        /// <returns></returns>
        public virtual byte[] SaveToProjection() {
            lock (_stateLock) {
                return SaveToProjectionInternal();
            }
        }

        /// <summary>
        /// Serialize the document to a byte array
        /// </summary>
        /// <returns></returns>
        protected abstract byte[] SaveToProjectionInternal();

        /// <summary>
        /// Load the document from a byte array
        /// </summary>
        /// <param name="projection"></param>
        /// <returns></returns>
        public virtual bool LoadFromProjection(byte[] projection) {
            lock (_stateLock) {
                return LoadFromProjectionInternal(projection);
            }
        }

        /// <summary>
        /// Load the document from a byte array
        /// </summary>
        /// <param name="projection"></param>
        /// <returns></returns>
        protected abstract bool LoadFromProjectionInternal(byte[] projection);

        /// <summary>
        /// Save the document to the dat files
        /// </summary>
        /// <param name="datwriter"></param>
        /// <returns></returns>
        public virtual Task<bool> SaveToDats(IDatReaderWriter datwriter, int iteration = 0) {
            lock (_stateLock) {
                return SaveToDatsInternal(datwriter, iteration);
            }
        }

        /// <summary>
        /// Save the document to the dat files
        /// </summary>
        /// <param name="datwriter"></param>
        /// <returns></returns>
        protected abstract Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int iteration = 0);

        public virtual void Dispose() {

        }
    }
}