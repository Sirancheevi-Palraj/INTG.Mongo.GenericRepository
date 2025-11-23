/*
README - Generic MongoDB Repository NuGet (for .NET 8)

What this file contains:
- A secure, resilient, generic MongoDB repository implementation suitable for packaging as a NuGet.
- Types:
  - IEntity (optional interface you can implement on DTOs/documents)
  - OperationResult<T> (uniform return wrapper)
  - RepositoryException (custom exception)
  - MongoRepository<T> (the generic repository)

Design goals satisfied:
- Works for any document/entity type T (no requirement for a specific Id type, though helper methods for ObjectId-based ids are provided).
- Caller passes connection string, database name and collection name; repository ensures collection exists and obtains an IMongoCollection<T>.
- Common CRUD methods: InsertAsync, InsertManyAsync, GetAsync (by filter), GetByIdAsync, GetAllAsync, UpdateAsync (by filter or id), ReplaceAsync, DeleteAsync.
- Secure coding: input validation, cancellation token support, using ILogger for observability; note: keep connection strings out of source control and prefer secret stores.
- Error handling: wraps errors into OperationResult<T> and throws RepositoryException for fatal misconfiguration. Specific MongoExceptions are caught and logged.
- Async-first API and cancellation tokens everywhere.

Packaging & usage notes (brief):
- Project file should target net8.0 and reference MongoDB.Driver package (>= 2.20.0 as of late 2025). Example csproj snippet below.
- To pack: dotnet pack -c Release
- To publish: dotnet nuget push bin/Release/<your.nupkg> -k <API_KEY> -s https://api.nuget.org/v3/index.json

Security notes:
- Never hardcode the connection string. Use Azure KeyVault / AWS Secrets Manager / user secrets / environment variables.
- Use least privilege credentials for the MongoDB user.
- Enable TLS and IP allowlists on the MongoDB deployment.

-- End of README --
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace INTG.Mongo.GenericRepository
{
   

    /// <summary>
    /// Generic MongoDB repository designed to be reusable across multiple apps and document types.
    /// </summary>
    /// <typeparam name="T">Document type. No constraints required; implement IEntity optionally for convenience.</typeparam>
    public class MongoRepository<T> where T : class
    {
        private readonly IMongoCollection<T> _collection;
        private readonly IMongoDatabase _database;
        private readonly ILogger? _logger;

        /// <summary>
        /// Creates a new MongoRepository.
        /// </summary>
        /// <param name="connectionString">MongoDB connection string. Use secrets; do not hardcode.</param>
        /// <param name="databaseName">Database name to use.</param>
        /// <param name="collectionName">Collection name to use. If missing, will use the type name.</param>
        /// <param name="logger">Optional logger for diagnostics.</param>
        public MongoRepository(string connectionString, string databaseName, string? collectionName = null, ILogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("connectionString is required", nameof(connectionString));
            if (string.IsNullOrWhiteSpace(databaseName)) throw new ArgumentException("databaseName is required", nameof(databaseName));

            _logger = logger;

            try
            {
                var mongoClientSettings = MongoClientSettings.FromConnectionString(connectionString);
                // Harden settings where appropriate; example: set retry writes
                mongoClientSettings.RetryWrites = true;
                // TLS is enabled by default when provided in the connection string for cloud providers.

                var client = new MongoClient(mongoClientSettings);
                _database = client.GetDatabase(databaseName);

                var colName = string.IsNullOrWhiteSpace(collectionName) ? typeof(T).Name : collectionName!;

                EnsureCollectionExistsAsync(colName, CancellationToken.None).GetAwaiter().GetResult();

                _collection = _database.GetCollection<T>(colName);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize MongoRepository of {Type}", typeof(T).FullName);
                throw new RepositoryException("Failed to initialize MongoRepository. See inner exception for details.", ex);
            }
        }

        /// <summary>
        /// Ensure the collection exists; create if not. Safe to call multiple times.
        /// </summary>
        private async Task EnsureCollectionExistsAsync(string collectionName, CancellationToken cancellationToken)
        {
            try
            {
                using var cursor = await _database.ListCollectionNamesAsync(new ListCollectionNamesOptions { Filter = new BsonDocument("name", collectionName) }, cancellationToken);
                var exists = await cursor.AnyAsync(cancellationToken);
                if (!exists)
                {
                    _logger?.LogInformation("Collection {Collection} does not exist; creating", collectionName);
                    await _database.CreateCollectionAsync(collectionName, cancellationToken: cancellationToken);
                }
            }
            catch (MongoException mex)
            {
                _logger?.LogError(mex, "Mongo error when ensuring collection exists: {Collection}", collectionName);
                throw;
            }
        }

        #region Insert

        public async Task<OperationResult<T>> InsertAsync(T document, CancellationToken cancellationToken = default)
        {
            if (document is null) return OperationResult<T>.Fail("Document is null");
            try
            {
                await _collection.InsertOneAsync(document, new InsertOneOptions(), cancellationToken);
                return OperationResult<T>.Ok(document);
            }
            catch (MongoException mex)
            {
                _logger?.LogError(mex, "InsertAsync failed for type {Type}", typeof(T).Name);
                return OperationResult<T>.Fail("Insert failed", mex);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error in InsertAsync");
                return OperationResult<T>.Fail("Unexpected error during insert", ex);
            }
        }

        public async Task<OperationResult<IEnumerable<T>>> InsertManyAsync(IEnumerable<T> documents, CancellationToken cancellationToken = default)
        {
            if (documents is null) return OperationResult<IEnumerable<T>>.Fail("Documents collection is null");
            var docsList = documents.ToList();
            if (!docsList.Any()) return OperationResult<IEnumerable<T>>.Fail("No documents provided");

            try
            {
                await _collection.InsertManyAsync(docsList, new InsertManyOptions { IsOrdered = false }, cancellationToken);
                return OperationResult<IEnumerable<T>>.Ok(docsList);
            }
            catch (MongoBulkWriteException bex)
            {
                _logger?.LogError(bex, "Partial failure in InsertManyAsync");
                return OperationResult<IEnumerable<T>>.Fail("Bulk insert partially failed", bex);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error in InsertManyAsync");
                return OperationResult<IEnumerable<T>>.Fail("Unexpected error during bulk insert", ex);
            }
        }

        #endregion

        #region Read

        public async Task<OperationResult<T?>> GetAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            if (predicate is null) return OperationResult<T?>.Fail("Predicate is null");
            try
            {
                var item = await _collection.Find(predicate).FirstOrDefaultAsync(cancellationToken);
                return OperationResult<T?>.Ok(item);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetAsync error");
                return OperationResult<T?>.Fail("Error during Get", ex);
            }
        }

        public async Task<OperationResult<IEnumerable<T>>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var items = await _collection.Find(Builders<T>.Filter.Empty).ToListAsync(cancellationToken);
                return OperationResult<IEnumerable<T>>.Ok(items);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetAllAsync error");
                return OperationResult<IEnumerable<T>>.Fail("Error during GetAll", ex);
            }
        }

        /// <summary>
        /// Helper to get by string id. Works if the document uses ObjectId or string with property name "Id" or annotated with [BsonId].
        /// If your document uses a different id strategy, use GetAsync with a predicate.
        /// </summary>
        public async Task<OperationResult<T?>> GetByIdAsync(string id, string? idFieldName = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id)) return OperationResult<T?>.Fail("id is required");

            try
            {
                // Try to parse as ObjectId; otherwise match as string
                FilterDefinition<T> filter;
                if (ObjectId.TryParse(id, out var objectId))
                {
                    // common field name is _id
                    var fid = idFieldName ?? "_id";
                    filter = Builders<T>.Filter.Eq(fid, objectId);
                }
                else
                {
                    var fid = idFieldName ?? "_id";
                    filter = Builders<T>.Filter.Eq(fid, id);
                }

                var item = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
                return OperationResult<T?>.Ok(item);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetByIdAsync error for id {Id}", id);
                return OperationResult<T?>.Fail("Error during GetById", ex);
            }
        }

        #endregion

        #region Update / Replace

        /// <summary>
        /// Replace the matching document (by filter) with the provided document.
        /// </summary>
        public async Task<OperationResult<ReplaceOneResult>> ReplaceAsync(Expression<Func<T, bool>> predicate, T replacement, CancellationToken cancellationToken = default)
        {
            if (predicate is null) return OperationResult<ReplaceOneResult>.Fail("Predicate is null");
            if (replacement is null) return OperationResult<ReplaceOneResult>.Fail("Replacement document is null");

            try
            {
                var result = await _collection.ReplaceOneAsync(predicate, replacement, new ReplaceOptions { IsUpsert = false }, cancellationToken);
                return OperationResult<ReplaceOneResult>.Ok(result);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ReplaceAsync error");
                return OperationResult<ReplaceOneResult>.Fail("Error during Replace", ex);
            }
        }

        /// <summary>
        /// Update fields using an UpdateDefinition; useful for partial updates.
        /// </summary>
        public async Task<OperationResult<UpdateResult>> UpdateAsync(Expression<Func<T, bool>> predicate, UpdateDefinition<T> updateDefinition, bool upsert = false, CancellationToken cancellationToken = default)
        {
            if (predicate is null) return OperationResult<UpdateResult>.Fail("Predicate is null");
            if (updateDefinition is null) return OperationResult<UpdateResult>.Fail("UpdateDefinition is null");

            try
            {
                var options = new UpdateOptions { IsUpsert = upsert };
                var result = await _collection.UpdateOneAsync(predicate, updateDefinition, options, cancellationToken);
                return OperationResult<UpdateResult>.Ok(result);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "UpdateAsync error");
                return OperationResult<UpdateResult>.Fail("Error during Update", ex);
            }
        }

        /// <summary>
        /// Update by string id helper.
        /// </summary>
        public async Task<OperationResult<UpdateResult>> UpdateByIdAsync(string id, UpdateDefinition<T> updateDefinition, string? idFieldName = null, bool upsert = false, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id)) return OperationResult<UpdateResult>.Fail("id is required");
            if (updateDefinition is null) return OperationResult<UpdateResult>.Fail("UpdateDefinition is null");

            try
            {
                FilterDefinition<T> filter;
                if (ObjectId.TryParse(id, out var objectId))
                {
                    filter = Builders<T>.Filter.Eq(idFieldName ?? "_id", objectId);
                }
                else
                {
                    filter = Builders<T>.Filter.Eq(idFieldName ?? "_id", id);
                }

                var options = new UpdateOptions { IsUpsert = upsert };
                var result = await _collection.UpdateOneAsync(filter, updateDefinition, options, cancellationToken);
                return OperationResult<UpdateResult>.Ok(result);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "UpdateByIdAsync error for id {Id}", id);
                return OperationResult<UpdateResult>.Fail("Error during UpdateById", ex);
            }
        }

        #endregion

        #region Delete

        public async Task<OperationResult<DeleteResult>> DeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            if (predicate is null) return OperationResult<DeleteResult>.Fail("Predicate is null");
            try
            {
                var result = await _collection.DeleteOneAsync(predicate, cancellationToken);
                return OperationResult<DeleteResult>.Ok(result);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "DeleteAsync error");
                return OperationResult<DeleteResult>.Fail("Error during Delete", ex);
            }
        }

        public async Task<OperationResult<DeleteResult>> DeleteByIdAsync(string id, string? idFieldName = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id)) return OperationResult<DeleteResult>.Fail("id is required");

            try
            {
                FilterDefinition<T> filter;
                if (ObjectId.TryParse(id, out var objectId))
                {
                    filter = Builders<T>.Filter.Eq(idFieldName ?? "_id", objectId);
                }
                else
                {
                    filter = Builders<T>.Filter.Eq(idFieldName ?? "_id", id);
                }

                var result = await _collection.DeleteOneAsync(filter, cancellationToken);
                return OperationResult<DeleteResult>.Ok(result);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "DeleteByIdAsync error for id {Id}", id);
                return OperationResult<DeleteResult>.Fail("Error during DeleteById", ex);
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Count matching documents.
        /// </summary>
        public async Task<OperationResult<long>> CountAsync(FilterDefinition<T>? filter = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var f = filter ?? Builders<T>.Filter.Empty;
                var count = await _collection.CountDocumentsAsync(f, cancellationToken: cancellationToken);
                return OperationResult<long>.Ok(count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "CountAsync error");
                return OperationResult<long>.Fail("Error during Count", ex);
            }
        }

        #endregion
    }
}

