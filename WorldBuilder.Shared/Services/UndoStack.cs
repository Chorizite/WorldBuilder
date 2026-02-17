using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Services;

public class UndoStack : IUndoStack {
    private readonly IDocumentManager _docManager;
    private readonly Stack<List<BaseCommand>> _undoStack = new();
    private readonly Stack<List<BaseCommand>> _redoStack = new();
    private readonly object _lock = new();

    public int HistoryLimit { get; set; } = 50;

    public UndoStack(IDocumentManager docManager) {
        _docManager = docManager;
    }

    public void Push(IReadOnlyList<BaseCommand> events) {
        if (events == null || events.Count == 0)
            throw new ArgumentException("Events list cannot be null or empty", nameof(events));

        lock (_lock) {
            _undoStack.Push([.. events]);
            _redoStack.Clear();

            while (_undoStack.Count > HistoryLimit) {
                // Remove the oldest item from the bottom of the stack
                var list = _undoStack.ToList();
                list.RemoveAt(0);
                _undoStack.Clear();
                foreach (var item in list.AsEnumerable().Reverse()) {
                    _undoStack.Push(item);
                }
            }
        }
    }

    public async Task<IReadOnlyList<BaseCommand>?> UndoAsync(CancellationToken ct) {
        List<BaseCommand> events;

        lock (_lock) {
            if (_undoStack.Count == 0)
                return null;

            events = _undoStack.Pop();
        }

        var inverses = events.Select(e => e.CreateInverse()).Reverse().ToList();

        var tx = await _docManager.CreateTransactionAsync(ct);
        try {
            foreach (var inverse in inverses) {
                inverse.Id = Guid.NewGuid().ToString();
                await _docManager.ApplyLocalEventAsync(inverse, tx, ct);
            }
            await tx.CommitAsync(ct);
        }
        catch {
            await tx.RollbackAsync(ct);
            throw;
        }

        lock (_lock) {
            _redoStack.Push(events);
        }

        return inverses;
    }

    public async Task<IReadOnlyList<BaseCommand>?> RedoAsync(CancellationToken ct) {
        List<BaseCommand> events;

        lock (_lock) {
            if (_redoStack.Count == 0)
                return null;

            events = _redoStack.Pop();
        }

        var tx = await _docManager.CreateTransactionAsync(ct);
        try {
            foreach (var evt in events) {
                evt.Id = Guid.NewGuid().ToString();
                await _docManager.ApplyLocalEventAsync(evt, tx, ct);
            }
            await tx.CommitAsync(ct);
        }
        catch {
            await tx.RollbackAsync(ct);
            throw;
        }

        lock (_lock) {
            _undoStack.Push(events);
        }

        return events;
    }
}