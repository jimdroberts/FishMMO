using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services.Interfaces.Actions
{
	/// <summary>
	/// Defines a persist operation for a collection.
	/// </summary>
	/// <remarks>
	/// Intended for service interfaces that expose batch persistence semantics.
	/// Implementations should be careful to preserve idempotency under execution-strategy retries.
	/// </remarks>
	/// <typeparam name="TItem">The item type being persisted.</typeparam>
	public interface IPersistManyAction<TItem>
	{
		/// <summary>
		/// Persists the provided items.
		/// </summary>
		/// <param name="items">Items to persist.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		Task<DatabaseResult> PersistAsync(IEnumerable<TItem> items, CancellationToken cancellationToken = default);
	}
}