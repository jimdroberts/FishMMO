using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services.Interfaces.Actions
{
	/// <summary>
	/// Defines a fetch operation that explicitly returns many items for a given key.
	/// </summary>
	/// <remarks>
	/// Intended for service interfaces that expose a one-to-many read scoped by a key.
	/// </remarks>
	/// <typeparam name="TKey">The key type used to scope the result set.</typeparam>
	/// <typeparam name="TItem">The item type contained in the returned collection.</typeparam>
	public interface IFetchManyByKeyAction<TKey, TItem>
	{
		/// <summary>
		/// Fetches many items for the given key.
		/// </summary>
		/// <param name="key">The key used to scope the result set.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		Task<DatabaseResult<IReadOnlyList<TItem>>> FetchManyAsync(TKey key, CancellationToken cancellationToken = default);
	}
}