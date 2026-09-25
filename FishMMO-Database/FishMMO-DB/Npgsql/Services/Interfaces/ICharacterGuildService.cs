using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing a character's guild membership state.
	/// </summary>
	/// <remarks>
	/// Guild membership updates should be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterGuildService :
		ICountByKeyAction<long>,
		IDeleteByKeyVersionedAction<long>,
		IFetchByKeyAction<long, CharacterGuildData?>,
		IFetchManyByKeyAction<long, CharacterGuildData>
	{
		/// <summary>
		/// Persists the provided guild membership data, enforcing capacity limits.
		/// </summary>
		/// <param name="guildData">The guild membership data to persist.</param>
		/// <param name="maxCapacity">The maximum number of members allowed in the guild.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> PersistAsync(CharacterGuildData guildData, int maxCapacity, CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates a character's guild rank if <paramref name="incomingVersion"/> is newer.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="guildId">The guild ID.</param>
		/// <param name="rank">The new rank.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this update operation.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> UpdateRankAsync(long characterId, long guildId, byte rank, long incomingVersion, CancellationToken cancellationToken = default);

		/// <summary>
		/// Writes one of a member's two guild notes.
		/// </summary>
		/// <param name="characterId">The member the note is about.</param>
		/// <param name="guildId">The guild the note belongs to.</param>
		/// <param name="note">The note text. Capped at 128 characters.</param>
		/// <param name="isOfficerNote">True for the officer-only note, false for the public one.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		/// <remarks>
		/// The guild is in the WHERE clause. A note request naming a character who has since left
		/// — or who was never in the editor's guild — must not write, and this is where that is
		/// guaranteed rather than merely checked earlier.
		/// </remarks>
		Task<DatabaseResult> UpdateNoteAsync(long characterId, long guildId, string note, bool isOfficerNote, CancellationToken cancellationToken = default);

		/// <summary>
		/// Writes where a member is — the scene name, or "Offline" — without touching anything else.
		/// </summary>
		/// <param name="characterId">The member.</param>
		/// <param name="guildId">The guild the member must still belong to.</param>
		/// <param name="location">The location label.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// True when the member's row was updated; false when the character no longer holds a
		/// membership in <paramref name="guildId"/> — kicked, left, or moved — which is not an error.
		/// </returns>
		/// <remarks>
		/// <para>
		/// An UPDATE, never an insert, and the guild is in the WHERE clause. The location used to
		/// be written through <see cref="PersistAsync"/>, a version-gated UPSERT, after reading the
		/// row for its version and rank. Between that read and the write a kick could delete the
		/// row, and the UPSERT then INSERTED it again: the kicked member was back in the guild,
		/// permanently. A write that can only change an existing row cannot do that.
		/// </para>
		/// <para>
		/// No version bump, like <see cref="UpdateNoteAsync"/>. The version arbitrates membership
		/// (rank, removal); a location label is not membership, and bumping it would make every
		/// rank change or kick that raced a login or logout fail as stale.
		/// </para>
		/// </remarks>
		Task<DatabaseResult<bool>> UpdateLocationAsync(long characterId, long guildId, string location, CancellationToken cancellationToken = default);
	}
}