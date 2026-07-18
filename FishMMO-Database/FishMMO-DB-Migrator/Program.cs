using FishMMO.Database.Npgsql;
using Microsoft.EntityFrameworkCore;

string connectionString = Environment.GetEnvironmentVariable("FISHMMO_CONNECTION_STRING")
    ?? "Host=127.0.0.1;Port=5432;Database=fish_mmo_postgresql;Username=user;Password=pass";

var optionsBuilder = new DbContextOptionsBuilder<NpgsqlDbContext>()
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConvention();
await using var context = new NpgsqlDbContext(optionsBuilder.Options, schema: "public");

Console.WriteLine("Applying pending migrations...");
await context.Database.MigrateAsync();
Console.WriteLine("Migrations applied successfully.");