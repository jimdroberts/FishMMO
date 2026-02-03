using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services.Interfaces.Actions
{
	/// <summary>
	/// Defines a fetch operation that returns a collection for a given key.
	/// </summary>
	/// <remarks>
	/// Intended for service interfaces that return a read-only collection scoped by a key.
	/// </remarks>
	/// <typeparam name="TKey">The key type used to scope the collection.</typeparam>
	/// <typeparam name="TItem">The item type contained in the returned collection.</typeparam>
	public interface IFetchCollectionByKeyAction<TKey, TItem>
	{
		/// <summary>
		/// Fetches a collection of items for the given key.
		/// </summary>
		/// <param name="key">The key used to scope the collection.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		Task<DatabaseResult<IReadOnlyList<TItem>>> FetchAsync(TKey key, CancellationToken cancellationToken = default);
	}
}