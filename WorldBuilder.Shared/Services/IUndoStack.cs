using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Services;

public interface IUndoStack {
    void Push(IReadOnlyList<BaseCommand> events);
    Task<IReadOnlyList<BaseCommand>?> UndoAsync(CancellationToken ct);
    Task<IReadOnlyList<BaseCommand>?> RedoAsync(CancellationToken ct);
}