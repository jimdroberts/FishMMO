using FishMMO.Database.Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

Console.WriteLine("FishMMO DB Migrator starting...");

// NOTE: Connection string is read directly from environment variable and bypasses NpgsqlDbConfiguration validation.
// Migrator is a standalone tool with minimal dependencies — configuration validation is deferred to runtime.
// See FishMMO.Database.Npgsql.NpgsqlDbConfiguration for the canonical connection-string validation logic
// used by the host application (server processes). The Migrator does not reuse that class to avoid
// pulling in the full database-layer assembly's configuration pipeline.
string? connectionString = Environment.GetEnvironmentVariable("FISHMMO_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__NpgsqlConnection");
}
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("FATAL: Neither FISHMMO_CONNECTION_STRING nor ConnectionStrings__NpgsqlConnection "
        + "environment variable is set. The Migrator requires a database connection string to apply migrations. "
        + "Example: FISHMMO_CONNECTION_STRING=\"Host=127.0.0.1;Port=5432;Database=fish_mmo;Username=fishmmo;Password=...\"");
    return 1;
}

// Determine the database schema to use for migrations.
// Priority: FISHMMO_DB_SCHEMA env var > appsettings.Database.json (Npgsql:Schema) > "public"
string schema = Environment.GetEnvironmentVariable("FISHMMO_DB_SCHEMA") ?? "";
if (string.IsNullOrWhiteSpace(schema))
{
    try
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Database.json", optional: true)
            .Build();
        schema = config["Npgsql:Schema"] ?? "";
    }
    catch
    {
        // Ignore config read errors; fall through to default.
    }
}
if (string.IsNullOrWhiteSpace(schema))
{
    schema = "public";
}

Console.WriteLine($"Using database schema: \"{schema}\"");

const int maxRetries = 3;
const int retryDelayMs = 2000;

for (int attempt = 1; attempt <= maxRetries; attempt++)
{
    try
    {
        Console.WriteLine($"Attempt {attempt}/{maxRetries}: Connecting to database and applying migrations...");

        var optionsBuilder = new DbContextOptionsBuilder<NpgsqlDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();
        await using var context = new NpgsqlDbContext(optionsBuilder.Options, schema: schema);

        Console.WriteLine("Applying pending migrations...");
        await context.Database.MigrateAsync();
        Console.WriteLine("Migrations applied successfully.");

        return 0;
    }
    catch (NpgsqlException ex) when (ex.InnerException is System.Net.Sockets.SocketException
                                         or System.IO.IOException)
    {
        // Connection refused / database not yet available — retry
        Console.Error.WriteLine($"Database connection failed (attempt {attempt}/{maxRetries}): {ex.Message}");

        if (attempt < maxRetries)
        {
            Console.WriteLine($"Retrying in {retryDelayMs / 1000} seconds...");
            await Task.Delay(retryDelayMs);
        }
        else
        {
            Console.Error.WriteLine("Max retries exceeded. Database is still unavailable.");
            return 1;
        }
    }
    catch (NpgsqlException ex)
    {
        Console.Error.WriteLine($"Database error: {ex.Message}");
        return 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Unexpected error: {ex}");
        return 1;
    }
}

return 1;