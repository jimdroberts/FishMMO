# Entity Configuration — Namespace Note

All entity configuration classes in this directory use the namespace
`FishMMO.Database.Npgsql.Entities` even though they reside under
`Npgsql/EntityConfigurations/` in the project tree.

This divergence is **intentional**: EF Core discovers `IEntityTypeConfiguration<T>`
implementations by type scanning via `AddEntityConfigurationsFromAssembly`, which
relies on the declaring assembly -- not the namespace or folder path. Keeping the
same namespace as the entity models avoids `using` aliases and keeps configuration
files consistent with the entities they configure.

If a future reader finds this surprising, please keep this note in mind rather
than reorganising files to match the folder structure.
