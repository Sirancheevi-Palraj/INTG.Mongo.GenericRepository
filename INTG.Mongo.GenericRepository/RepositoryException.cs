using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace INTG.Mongo.GenericRepository
{
    /// <summary>
    /// Custom exception type thrown for configuration or irrecoverable repository errors.
    /// </summary>
    public class RepositoryException : Exception
    {
        public RepositoryException(string message) : base(message) { }
        public RepositoryException(string message, Exception inner) : base(message, inner) { }
    }
}
