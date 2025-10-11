using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Documents {
    /// <summary>
    /// Interface for document storage
    /// </summary>
    public interface IDocumentStorageService : IDisposable {
        /// <summary>
        /// Get document
        /// </summary>
        /// <param name="documentId"></param>
        /// <returns></returns>
        Task<DBDocument?> GetDocumentAsync(string documentId);

        /// <summary>
        /// Create document
        /// </summary>
        /// <param name="documentId"></param>
        /// <param name="type"></param>
        /// <param name="initialData"></param>
        /// <returns></returns>
        Task<DBDocument> CreateDocumentAsync(string documentId, string type, byte[] initialData);

        /// <summary>
        /// Update document
        /// </summary>
        /// <param name="documentId"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        Task<DBDocument> UpdateDocumentAsync(string documentId, byte[] update);

        /// <summary>
        /// Delete document
        /// </summary>
        /// <param name="documentId"></param>
        /// <returns></returns>
        Task<bool> DeleteDocumentAsync(string documentId);

        /// <summary>
        /// Create update
        /// </summary>
        /// <param name="documentId"></param>
        /// <param name="type"></param>
        /// <param name="update"></param>
        /// <returns></returns>
        Task<DBDocumentUpdate> CreateUpdateAsync(string documentId, string type, byte[] update);

        Task<DBSnapshot> CreateSnapshotAsync(DBSnapshot snapshot);
        Task<DBSnapshot?> GetSnapshotAsync(Guid snapshotId);
        Task<List<DBSnapshot>> GetSnapshotsAsync(string documentId);
        Task<bool> DeleteSnapshotAsync(Guid snapshotId);
        Task UpdateSnapshotNameAsync(Guid snapshotId, string newName);
    }
}
