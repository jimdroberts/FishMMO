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
		/// <remarks>
		/// A successful result does not mean every supplied row was written. Batched writes are
		/// version-gated and the service filters what it cannot act on, so the outcome carries
		/// counts rather than a bare boolean — see <see cref="BulkWriteResult"/>, which explains
		/// which discrepancies a caller should care about and which are routine.
		/// </remarks>
		/// <param name="items">Items to persist.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>What the write actually did, or a failure.</returns>
		Task<DatabaseResult<BulkWriteResult>> PersistAsync(IEnumerable<TItem> items, CancellationToken cancellationToken = default);
	}
}