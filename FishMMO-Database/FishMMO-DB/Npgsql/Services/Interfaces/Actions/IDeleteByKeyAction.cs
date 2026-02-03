using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services.Interfaces.Actions
{
	/// <summary>
	/// Defines a delete operation keyed by a single value.
	/// </summary>
	/// <remarks>
	/// Intended for service interfaces that expose a non-versioned delete.
	/// Implementations may perform hard deletes or soft deletes depending on the entity semantics.
	/// </remarks>
	/// <typeparam name="TKey">The key type used to locate the entity.</typeparam>
	public interface IDeleteByKeyAction<TKey>
	{
		/// <summary>
		/// Deletes the entity identified by the given key.
		/// </summary>
		/// <param name="key">The key used to locate the entity.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		Task<DatabaseResult> DeleteAsync(TKey key, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Defines a version-gated delete operation keyed by a single value.
	/// </summary>
	/// <remarks>
	/// Intended for service interfaces that enforce optimistic concurrency via a logical, monotonic version.
	/// Implementations should reject stale deletes when <paramref name="incomingVersion"/> is not newer.
	/// </remarks>
	/// <typeparam name="TKey">The key type used to locate the entity.</typeparam>
	public interface IDeleteByKeyVersionedAction<TKey>
	{
		/// <summary>
		/// Deletes the entity identified by the given key if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <param name="key">The key used to locate the entity.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this delete operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		Task<DatabaseResult> DeleteAsync(TKey key, long incomingVersion, CancellationToken cancellationToken = default);
	}
}