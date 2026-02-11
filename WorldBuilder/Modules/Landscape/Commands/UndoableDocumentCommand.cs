using Avalonia.Threading;
using System;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using worldBuilderShared = WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.Landscape.Commands {
    /// <summary>
    /// Wraps a <see cref="BaseCommand"/> (system command) into an <see cref="ICommand"/> (UI command)
    /// for integration with <see cref="CommandHistory"/>.
    /// </summary>
    public class UndoableDocumentCommand : ICommand {
        private readonly BaseCommand _baseCommand;
        private readonly worldBuilderShared.IDocumentManager _documentManager;
        private readonly Func<Task> _refreshCallback;
        private readonly string _name;

        public string Name => _name;

        public UndoableDocumentCommand(string name, BaseCommand baseCommand, worldBuilderShared.IDocumentManager documentManager, Func<Task> refreshCallback) {
            _name = name;
            _baseCommand = baseCommand;
            _documentManager = documentManager;
            _refreshCallback = refreshCallback;
        }

        public void Execute() {
            Dispatcher.UIThread.InvokeAsync(async () => {
                await ApplyCommand(_baseCommand);
            });
        }

        public void Undo() {
            var inverse = _baseCommand.CreateInverse();
            Dispatcher.UIThread.InvokeAsync(async () => {
                await ApplyCommand(inverse);
            });
        }

        private async Task ApplyCommand(BaseCommand cmd) {
            try {
                await using var tx = await _documentManager.CreateTransactionAsync(default);
                cmd.Id = Guid.NewGuid().ToString();

                var result = await _documentManager.ApplyLocalEventAsync(cmd, tx, default);

                if (result.IsSuccess) {
                    await tx.CommitAsync(default);
                    await _refreshCallback();
                }
                else {
                    Console.WriteLine($"[UndoableDocumentCommand] Failed to apply {cmd.GetType().Name}: {result.Error}");
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"[UndoableDocumentCommand] Exception applying {cmd.GetType().Name}: {ex}");
            }
        }
    }
}