# INTG.Mongo.Repository

A **secure, generic, enterprise-grade MongoDB repository library** built for **banking and financial applications** under the INTG platform. Designed to eliminate duplicate MongoDB CRUD logic across multiple applications while ensuring **standardization, error handling, security, and ease of integration**.

---

## 🚀 Overview

`INTG.Mongo.Repository` provides a **generic repository** over MongoDB that:

* Works for **any entity/document** type
* Provides **Insert / Update / Delete / Get / GetAll / Count** operations
* Accepts **connection string, database name, collection name** dynamically
* Auto-creates collections if missing
* Ensures **secure error handling**, **logging**, and **input validation**
* Returns a **uniform response** using the `OperationResult<T>` wrapper
* Designed for **highly regulated banking systems**

This package helps standardize MongoDB access across multiple INTG-based services.

---

## 🎯 Key Features

### ✔ Generic Repository — Works with Any Entity

No need to write repository code for each model.

### ✔ Secure Error Handling

All actions return `OperationResult<T>`, containing:

* Success flag
* Data
* Error message
* Exception (if any)

### ✔ Collection Auto-Creation

If a collection doesn’t exist, the repository **creates it automatically**.

### ✔ Logging Supported

Works with Microsoft `ILogger` for:

* Insert errors
* Get errors
* Update failures
* Mongo exceptions

### ✔ Zero Configuration Boilerplate

Instantiate the repository and start performing CRUD operations instantly.

---

## 📦 Installation

Coming soon to **NuGet**.

For now, install via local `.nupkg`:

```sh
dotnet add package INTG.Mongo.Repository
```

---

## 🔧 How to Use

### 1. Register Repository

```csharp
var repo = new MongoRepository<MyDocument>(
    connectionString: config["Mongo:ConnectionString"],
    databaseName: "banking-db",
    collectionName: "accounts",
    logger: logger
);
```

---

## 📥 Insert Document

```csharp
var result = await repo.InsertAsync(new Account { Name = "John", Balance = 5000 });

if (!result.Success)
    logger.LogError(result.ErrorMessage);
```

---

## 📤 Get a Single Document

```csharp
var result = await repo.GetAsync(x => x.AccountNumber == "12345");
```

---

## 📃 Get All

```csharp
var result = await repo.GetAllAsync();
```

---

## 🔄 Update

```csharp
var update = Builders<Account>.Update.Set(a => a.Balance, 8000);
var result = await repo.UpdateAsync(a => a.Id == id, update);
```

---

## 🗑 Delete

```csharp
var result = await repo.DeleteAsync(a => a.Id == id);
```

---

## 📊 Count

```csharp
var count = await repo.CountAsync();
```

---

## 🛡 Security Guidelines

To ensure compliance for banking systems:

* **Do not hardcode MongoDB credentials**
* Use **Azure Key Vault / AWS Secrets Manager / GCP Secret Manager**
* Use **TLS/SSL** for all MongoDB connections
* Restrict database permissions
* Implement network firewall rules
* Enable audit logs on MongoDB

---

## 🧱 Project Structure

```
INTG.Mongo.Repository
│
├── MongoRepository.cs          # Generic Mongo CRUD repository
├── OperationResult.cs          # Standard response wrapper
├── RepositoryException.cs      # Custom exception type
└── IEntity.cs                  # Optional common ID interface
```

---

## 🧪 Testing Strategy (recommended)

You may use:

* **xUnit** for unit tests
* **Testcontainers.MongoDb** for integration tests
* Mock `ILogger` for validation

---

## 🏗 Build & Pack

```sh
dotnet pack -c Release
```

Creates:

```
bin/Release/INTG.Mongo.Repository.x.x.x.nupkg
```

---

## 🚀 Publish to NuGet

```sh
dotnet nuget push ./bin/Release/INTG.Mongo.Repository.x.x.x.nupkg \
    -k <API_KEY> \
    -s https://api.nuget.org/v3/index.json
```



---

## 📄 License

INTG Internal Use / Proprietary

---

## ✨ Maintainers

**INTG Platform Engineering Team**
