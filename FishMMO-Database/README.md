# FishMMO-Database

FishMMO-Database is a .NET-based database layer for the FishMMO project, providing entity models and database context for managing game data such as characters, abilities, accounts, servers, and more. It is designed to work with PostgreSQL (via Npgsql) and Redis, supporting scalable and efficient data storage for MMO game servers.

## Features
- Entity Framework Core models for all major MMO data types (characters, abilities, accounts, servers, etc.)
- PostgreSQL support via Npgsql
- Redis support for caching or fast-access data
- Database migration support via a dedicated migrator project
- Modular structure for easy extension and maintenance

## Project Structure
- `FishMMO-DB/` - Main database project containing entity models, context factories, and configuration
- `FishMMO-DB-Migrator/` - Migration support project, this is generally left empty

## Configuration Options

### AppSettings
Configuration is typically managed via an `AppSettings.cs` or `appsettings.json` file. Key options include:

- **PostgreSQL Connection String**: Set the connection string for the PostgreSQL database.
- **Redis Connection String**: Set the connection string for the Redis instance (if used).
- **Database Schema**: The default schema is `fish_mmo_postgresql` for PostgreSQL tables.

Example (appsettings.json):
```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Database=fish_mmo;Username=postgres;Password=yourpassword",
    "Redis": "localhost:6379"
  },
  "Database": {
    "Schema": "fish_mmo_postgresql"
  }
}
```

## Usage
1. Build the solution using Visual Studio or `dotnet build`.
2. Configure your connection strings and schema as needed.
3. Use the migrator project to apply database migrations:
   ```powershell
   dotnet run --project FishMMO-DB-Migrator
   ```
4. Integrate the `FishMMO-DB` library into your MMO server projects to access and manage game data.

## Entity Example
A sample entity, `CharacterAbilityEntity`, represents a character's abilities:
```csharp
[Table("character_abilities", Schema = "fish_mmo_postgresql")]
public class CharacterAbilityEntity
{
    public long ID { get; set; }
    public long CharacterID { get; set; }
    public CharacterEntity Character { get; set; }
    public int TemplateID { get; set; }
    public List<int> AbilityEvents { get; set; }
    public float Cooldown { get; set; }
}
```

## Requirements
- .NET 8.0 or later
- PostgreSQL database
- (Optional) Redis server