using System;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql
{
	/// <summary>
	/// Extension methods for NpgsqlDbContext to provide dynamic table name resolution.
	/// </summary>
	public static class DbContextExtensions
	{
		/// <summary>
		/// Gets the fully qualified table name (schema.table_name) for the specified entity type.
		/// </summary>
		/// <typeparam name="TEntity">The entity type.</typeparam>
		/// <param name="context">The database context.</param>
		/// <returns>The fully qualified table name in the format "schema.table_name".</returns>
		/// <exception cref="System.ArgumentNullException">Thrown when context is null.</exception>
		/// <exception cref="System.InvalidOperationException">Thrown when entity type is not found in the model.</exception>
		/// <remarks>
		/// <para><b>SQL Injection Safety:</b> This method is safe for use in ExecuteSqlInterpolated/FromSqlInterpolated 
		/// operations. Table names are retrieved from EF Core's internal metadata model, NOT from user input. 
		/// The values are controlled by the application's entity configuration and cannot be manipulated externally.</para>
		/// <para><b>Usage with Interpolated SQL:</b> When used with ExecuteSqlInterpolatedAsync or FromSqlInterpolated, 
		/// EF Core automatically parameterizes all interpolated values EXCEPT table/column names from GetTableName(). 
		/// This is safe because table names come from trusted metadata, not user input.</para>
		/// <para>Example safe usage: <c>$"SELECT * FROM {tableName} WHERE id = {userId}"</c> - userId is parameterized, tableName is metadata-derived.</para>
		/// </remarks>
		public static string GetTableName<TEntity>(this NpgsqlDbContext context) where TEntity : class
		{
			var entityType = context.Model.FindEntityType(typeof(TEntity));
			if (entityType == null)
			{
				throw new System.InvalidOperationException($"Entity type {typeof(TEntity).Name} not found in model.");
			}

			var schema = entityType.GetSchema() ?? context.Schema;
			var tableName = entityType.GetTableName();
			if (string.IsNullOrWhiteSpace(tableName))
			{
				throw new InvalidOperationException(
					$"Entity type {typeof(TEntity).Name} does not have a table name. " +
					"This entity may be an owned type, keyless entity, or view.");
			}
			return $"\"{schema}\".\"{tableName}\"";
		}
	}
}