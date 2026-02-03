using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services.Interfaces.Actions
{
	/// <summary>
	/// Defines a count operation keyed by a single value.
	/// </summary>
	/// <remarks>
	/// Intended for service interfaces that expose a simple count scoped by a key.
	/// The key semantics and what is counted are defined by the specialized service.
	/// </remarks>
	/// <typeparam name="TKey">The key type used to scope the count (e.g., account, characterId, guildId).</typeparam>
	public interface ICountByKeyAction<TKey>
	{
		/// <summary>
		/// Counts items for the given key.
		/// </summary>
		/// <param name="key">The key used to scope the count.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		Task<DatabaseResult<int>> CountAsync(TKey key, CancellationToken cancellationToken = default);
	}
}