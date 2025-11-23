using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace INTG.Mongo.GenericRepository
{
    /// <summary>
    /// Optional interface: implement on your documents if you want a common Id property.
    /// Use BsonId/BsonRepresentation attributes on concrete types if you want string <-> ObjectId mapping.
    /// </summary>
    public interface IEntity
    {
        // This property is optional for the repository to work; some helper methods assume this.
        object? Id { get; set; }
    }
}
