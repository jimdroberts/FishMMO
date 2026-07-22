using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Represents a scalar integer result returned from raw SQL queries.
	/// </summary>
	/// <remarks>
	/// This is configured as a keyless entity type in <see cref="NpgsqlDbContext"/> and is intended
	/// for mapping single-row results such as counts and status codes.
	/// </remarks>
	[Keyless]
	public sealed class SqlIntValue
	{
		/// <summary>
		/// Gets or sets the scalar integer value.
		/// </summary>
		public int Value { get; set; }
	}
}