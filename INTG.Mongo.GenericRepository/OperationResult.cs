using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace INTG.Mongo.GenericRepository
{
    /// <summary>
    /// Uniform result wrapper for repository operations.
    /// Contains success flag, data (when applicable), and error information.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class OperationResult<T>
    {
        public bool Success { get; private set; }
        public T? Data { get; private set; }
        public string? ErrorMessage { get; private set; }
        public Exception? Exception { get; private set; }

        private OperationResult() { }

        public static OperationResult<T> Ok(T? data = default) => new OperationResult<T> { Success = true, Data = data };
        public static OperationResult<T> Fail(string message, Exception? ex = null) => new OperationResult<T> { Success = false, ErrorMessage = message, Exception = ex };
    }
}
