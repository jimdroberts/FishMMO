using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Keyless entity used to map scalar <c>bigint</c> results from raw SQL.
	/// </summary>
	[Keyless]
	public sealed class SqlLongValue
	{
		/// <summary>
		/// Gets or sets the scalar value.
		/// </summary>
		public long Value { get; set; }
	}
}