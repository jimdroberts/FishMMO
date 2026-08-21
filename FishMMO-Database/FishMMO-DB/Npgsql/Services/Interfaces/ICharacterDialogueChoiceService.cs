using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for per-character dialogue choice bitmasks.
	/// </summary>
	/// <remarks>
	/// Implements the shared fetch-collection contract, but <b>not</b>
	/// <c>IPersistManyAction</c>/<c>IDeleteByKeyVersionedAction</c>. Those describe a row written
	/// under an optimistic <c>Version</c> where the newest writer wins; this row is a monotonically
	/// growing bitmask that is merged, never replaced, so there is no stale write to reject and no
	/// version for the contract to gate on. See <see cref="CharacterDialogueChoiceData"/> for why
	/// the state is stored as a mask.
	/// </remarks>
	public interface ICharacterDialogueChoiceService :
		IFetchCollectionByKeyAction<long, CharacterDialogueChoiceData>
	{
		/// <summary>
		/// Merges choice bits into the stored masks, creating rows that do not exist yet.
		/// </summary>
		/// <param name="choices">The masks to merge. Bits already stored are retained.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		/// <remarks>
		/// Bits are OR-ed into whatever is already there, so this is idempotent: a retry after an
		/// ambiguous failure, or two scene servers writing during a transfer, converge on the union
		/// rather than one silently discarding the other's bits. It follows that a bit can never be
		/// cleared through this call — that is the point, since a cleared bit is a one-time
		/// dialogue reward the character can claim a second time.
		/// </remarks>
		Task<DatabaseResult> MergeAsync(IEnumerable<CharacterDialogueChoiceData> choices, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes every stored mask for one dialogue template, across all characters.
		/// </summary>
		/// <param name="templateId">The dialogue template to reset.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>The number of rows removed.</returns>
		/// <remarks>
		/// Bit positions are assigned positionally by <c>DialogueTemplate.GetChoiceBitIndex</c>, so
		/// inserting or removing a choice anywhere but the end re-points every later bit at a
		/// different choice. Nothing at runtime can detect that the asset was re-cut — the mask is
		/// just a number — so this is the deliberate operator action that goes with such an edit.
		/// Administrative only; nothing on the gameplay path calls it.
		/// </remarks>
		Task<DatabaseResult<int>> ResetTemplateAsync(int templateId, CancellationToken cancellationToken = default);
	}
}
