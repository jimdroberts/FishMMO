using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces.Actions
{
	/// <summary>
	/// Defines an ownership-gated persist operation for a collection of per-character rows.
	/// </summary>
	/// <remarks>
	/// The write a scene server makes for a character it holds. A row lands only while its
	/// character's session claim is still held under the triple the writer quotes, and the check is
	/// held under a lock for the rest of the write's transaction, so a release cannot slip in between
	/// — see <see cref="CharacterWriteGate"/>. The ungated <see cref="IPersistManyAction{TItem}"/>
	/// remains for writers that hold no claim by design (character creation) and for writes already
	/// inside a unit of work that asserted ownership itself.
	/// </remarks>
	/// <typeparam name="TItem">The row type being persisted.</typeparam>
	public interface IPersistManyOwnedAction<TItem>
	{
		/// <summary>
		/// Persists the rows whose characters the writer still owns.
		/// </summary>
		/// <param name="items">Rows to persist.</param>
		/// <param name="claims">The claim held for each character the rows name, as captured with them.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// What the write did. Rows of a live character whose claim is not held are counted as
		/// <see cref="BulkWriteResult.Unowned"/> (and so as filtered). The write fails with
		/// <see cref="DatabaseErrorCodes.Forbidden"/> when it could touch none of its characters for
		/// that reason, and with <see cref="DatabaseErrorCodes.NotFound"/> when none exists.
		/// </returns>
		Task<DatabaseResult<BulkWriteResult>> PersistOwnedAsync(IEnumerable<TItem> items, IReadOnlyCollection<CharacterSessionLeaseData> claims, CancellationToken cancellationToken = default);
	}
}
