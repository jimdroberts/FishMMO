using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Extension methods for NpgsqlDbContext to provide dynamic table name resolution.
	/// </summary>
	public static class DbContextExtensions
	{
		private readonly struct TableInfo
		{
			public TableInfo(string tableName, string? schema)
			{
				TableName = tableName;
				Schema = schema;
			}

			public string TableName { get; }
			public string? Schema { get; }
		}

		private static readonly ConcurrentDictionary<Type, TableInfo> TableInfoCache = new ConcurrentDictionary<Type, TableInfo>();

		/// <summary>
		/// Gets the fully qualified table name (schema.table_name) for the specified entity type.
		/// </summary>
		/// <typeparam name="TEntity">The entity type.</typeparam>
		/// <param name="context">The database context.</param>
		/// <returns>The fully qualified table name in the format "schema.table_name".</returns>
		/// <exception cref="System.ArgumentNullException">Thrown when context is null.</exception>
		/// <exception cref="System.InvalidOperationException">Thrown when entity type is not found in the model.</exception>
		/// <remarks>
		/// <para><b>Identifier Safety:</b> The schema and table name are resolved from EF Core metadata (not user input).
		/// However, SQL identifiers cannot be passed as parameters. When using interpolated SQL APIs (e.g., ExecuteSqlInterpolatedAsync
		/// or FromSqlInterpolated), EF Core parameterizes the interpolated holes, which would turn the table name into a SQL parameter
		/// and break the statement.</para>
		/// <para><b>Required Usage:</b> Embed the returned identifier into SQL text and use ExecuteSqlRaw/FromSqlRaw with parameter placeholders
		/// (<c>{0}</c>, <c>{1}</c>, ...) for values. Never build SQL by concatenating untrusted strings.</para>
		/// </remarks>
		public static string GetTableName<TEntity>(this NpgsqlDbContext context) where TEntity : class
		{
			if (context == null)
			{
				throw new ArgumentNullException(nameof(context));
			}

			var tableInfo = TableInfoCache.GetOrAdd(typeof(TEntity), _ =>
			{
				var entityType = context.Model.FindEntityType(typeof(TEntity));
				if (entityType == null)
				{
					throw new InvalidOperationException($"Entity type {typeof(TEntity).Name} not found in model.");
				}

				var tableName = entityType.GetTableName();
				if (string.IsNullOrWhiteSpace(tableName))
				{
					throw new InvalidOperationException(
						$"Entity type {typeof(TEntity).Name} does not have a table name. " +
						"This entity may be an owned type, keyless entity, or view.");
				}

				var schema = entityType.GetSchema();

				// Validate identifiers once at cache population time.
				// If schema is null, it will be resolved from context.Schema on each call.
				if (!IsValidUnquotedIdentifier(tableName) || (schema != null && !IsValidUnquotedIdentifier(schema)))
				{
					throw new InvalidOperationException(
						$"Entity type {typeof(TEntity).Name} resolves to an identifier that cannot be safely represented as an unquoted PostgreSQL identifier. " +
						$"Schema='{schema}', Table='{tableName}'. Ensure schema/table names contain only lowercase letters, digits, and underscores and start with a letter or underscore.");
				}

				return new TableInfo(tableName, schema);
			});

			var resolvedSchema = tableInfo.Schema ?? context.Schema;
			if (!IsValidUnquotedIdentifier(resolvedSchema))
			{
				throw new InvalidOperationException(
					$"Entity type {typeof(TEntity).Name} resolves to an identifier that cannot be safely represented as an unquoted PostgreSQL identifier. " +
					$"Schema='{resolvedSchema}', Table='{tableInfo.TableName}'. Ensure schema/table names contain only lowercase letters, digits, and underscores and start with a letter or underscore.");
			}

			return $"{resolvedSchema}.{tableInfo.TableName}";
		}

		private static bool IsValidUnquotedIdentifier(string identifier)
		{
			if (string.IsNullOrWhiteSpace(identifier))
			{
				return false;
			}

			return Regex.IsMatch(identifier, "^[a-z_][a-z0-9_]*$");
		}
	}
}