using System.Threading;

namespace WorldBuilder.Shared.Lib {
    /// <summary>
    /// Provides a way to store and retrieve the current transaction for the current execution context.
    /// </summary>
    public static class TransactionContext {
        private static readonly AsyncLocal<ITransaction?> _currentTransaction = new();

        /// <summary>
        /// Gets or sets the current transaction.
        /// </summary>
        public static ITransaction? Current {
            get => _currentTransaction.Value;
            set => _currentTransaction.Value = value;
        }
    }
}