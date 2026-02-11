# WorldBuilder.Shared

This project contains the shared logic, models, and services used by both the WorldBuilder client and server. It implements a document-based architecture with a command system for state synchronization and persistence.

## Core Systems

### 1. Command System (`BaseCommand`)
The command system is the primary way to modify state in the application. Every action that changes a document should be encapsulated in a command.

- **`BaseCommand`**: The abstract base class for all commands.
- **`BaseCommand<TResult>`**: Base class for commands that return a specific result.
- **Serialization**: Commands are serialized using [MemoryPack](https://github.com/Cysharp/MemoryPack). New commands must be added to the `[MemoryPackUnion]` list in `BaseCommand.cs`.
- **`ApplyAsync` / `ApplyResultAsync`**: This is where the command's logic is implemented. It receives an `IDocumentManager`, `IDatReaderWriter`, and a transaction.
- **`CreateInverse()`**: Every command should ideally implement this to provide undo functionality.

**Best Practice**: Keep commands small, atomic, and focused on a single change.

### 2. Document System (`BaseDocument`)
Documents are the units of persistence in the system. They represent complex data structures that can be versioned and saved as blobs.

- **`BaseDocument`**: The base class for all documents (e.g., `LandscapeDocument`).
- **Versioning**: Documents have a `Version` property used for optimistic concurrency and tracking changes.
- **Initialization**: 
    - `InitializeForUpdatingAsync`: Called when a document is about to be modified.
    - `InitializeForEditingAsync`: Called when a document is being prepared for UI editing.
- **Persistence**: Documents can save their state to DAT files via `SaveToDatsAsync`.

**Best Practice**: Ensure documents are registered in the `[MemoryPackUnion]` list in `BaseDocument.cs`.

### 4. Result Pattern (`Result<T>`)
Most operations in `WorldBuilder.Shared` return a `Result<T>` or `Result<Unit>` instead of throwing exceptions for expected failure cases.

- **Success**: Contains the requested value.
- **Failure**: Contains an `Error` object with a message and an optional error code.
- **Unit**: Used when an operation returns no value but can still fail (`Result<Unit>`).

**Best Practice**: Always check `IsSuccess` or `IsFailure` before accessing the `Value`. Use `Result` for any operation that can fail due to external factors (DB, network, validation).

## 3. Transaction System
The transaction system handles database operations and ensures consistency during state changes.

- **`ITransaction`**: Interface for database transactions used in command execution.
- **Resource Management**: Always dispose of transactions properly using `await using` pattern.
- **Commit Operations**: Call `tx.CommitAsync()` to finalize successful transactions.

## Architecture & Best Practices

### State Modification
- **Never** modify a document directly. Always use a command and apply it through the `IDocumentManager`.
- Commands ensure that changes are:
    1. Validated.
    2. Applied consistently.
    3. Recorded for synchronization with other clients/server.
    4. Reversible (via `CreateInverse`).

### Resource Management
- **Rentals**: Always use `await using` or a `try...finally` block to ensure `DocumentRental<T>` is disposed.
- **Transactions**: Always use `await using` for `ITransaction`. Ensure you call `tx.CommitAsync()` if the operation was successful.

### Concurrency
- The `DocumentManager` uses a semaphore to protect its internal cache.
- Commands should be designed to handle potential version conflicts if necessary, although the current implementation focuses on local-first application.

### Adding New Features
1. Define a new `BaseDocument` if you need a new type of persistent data.
2. Define `BaseCommand`s for any actions users can perform on that data.
3. Register the new types in the `MemoryPackUnion` attributes in `BaseDocument.cs` and `BaseCommand.cs`.
4. Implement the logic in the command's `ApplyAsync` method.

## Testing

The WorldBuilder.Shared project includes comprehensive unit tests located in the `WorldBuilder.Shared.Tests` project:
- **Running Tests**: Execute `dotnet test WorldBuilder.Shared.Tests` to run tests specific to this project
- **Test Structure**: Tests follow the same architectural patterns as the main codebase
- **Mocking**: Use the mock implementations in the `Mocks` folder for dependency injection during testing
