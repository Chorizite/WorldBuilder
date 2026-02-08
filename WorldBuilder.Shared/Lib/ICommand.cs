using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Lib {
    /// <summary>
    /// Represents a command that returns a result.
    /// </summary>
    /// <typeparam name="TResult">The type of the result returned by the command.</typeparam>
    public interface ICommand<TResult> { }
}