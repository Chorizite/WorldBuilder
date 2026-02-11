namespace WorldBuilder.Shared.Lib {
    // Base for queries (reads)
    /// <summary>
    /// Represents a query (read operation) that returns a result.
    /// </summary>
    /// <typeparam name="TResult">The type of the result returned by the query.</typeparam>
    public interface IQuery<TResult> { }
}