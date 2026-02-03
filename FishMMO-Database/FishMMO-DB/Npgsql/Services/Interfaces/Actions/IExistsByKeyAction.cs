using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services.Interfaces.Actions
{
	/// <summary>
	/// Defines an existence check keyed by a single value.
	/// </summary>
	/// <remarks>
	/// Intended for service interfaces that expose a fast existence check (often via indexed lookups).
	/// </remarks>
	/// <typeparam name="TKey">The key type used to locate the entity.</typeparam>
	public interface IExistsByKeyAction<TKey>
	{
		/// <summary>
		/// Checks whether an entity exists for the given key.
		/// </summary>
		/// <param name="key">The key used to locate the entity.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		Task<DatabaseResult<bool>> ExistsAsync(TKey key, CancellationToken cancellationToken = default);
	}
}