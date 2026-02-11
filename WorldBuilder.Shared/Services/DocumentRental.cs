using System;
using System.Threading;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Services;

/// <summary>
/// Represents a rental of a document from the document manager.
/// When disposed, the document is returned to the manager.
/// </summary>
/// <typeparam name="T">The type of document.</typeparam>
public class DocumentRental<T> : IDisposable where T : BaseDocument {
    private readonly T _document;
    private readonly Action _onReturn;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentRental{T}"/> class.
    /// </summary>
    /// <param name="document">The document instance.</param>
    /// <param name="onReturn">The action to perform when the rental is returned.</param>
    public DocumentRental(T document, Action onReturn) {
        _document = document;
        _onReturn = onReturn;
    }

    /// <summary>
    /// Gets the rented document.
    /// </summary>
    public T Document => _document;

    /// <summary>
    /// Implicit conversion from a rental to the document it contains.
    /// </summary>
    /// <param name="rental">The rental.</param>
    public static implicit operator T(DocumentRental<T> rental) => rental._document;

    /// <inheritdoc/>
    public void Dispose() {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0) {
            _onReturn();
        }
    }
}