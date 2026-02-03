using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services.Interfaces.Actions
{
	/// <summary>
	/// Defines a fetch operation that returns a single result for a given key.
	/// </summary>
	/// <remarks>
	/// Intended for service interfaces that expose a simple point-read by key.
	/// The key semantics and any filtering (e.g. soft-deleted rows) are defined by the specialized service.
	/// </remarks>
	/// <typeparam name="TKey">The key type used to locate the entity.</typeparam>
	/// <typeparam name="TResult">The result type returned on success.</typeparam>
	public interface IFetchByKeyAction<TKey, TResult>
	{
		/// <summary>
		/// Fetches an entity for the given key.
		/// </summary>
		/// <param name="key">The key used to locate the entity.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		Task<DatabaseResult<TResult>> FetchAsync(TKey key, CancellationToken cancellationToken = default);
	}
}