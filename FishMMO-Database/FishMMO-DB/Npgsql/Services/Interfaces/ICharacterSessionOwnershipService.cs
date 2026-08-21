using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Proves — inside an open unit of work — that the calling server is still entitled to write a
	/// character's state, and holds that entitlement still for the remainder of the transaction.
	/// </summary>
	/// <remarks>
	/// <para>
	/// WHY THIS EXISTS. Item writes had no ownership check of any kind. Every one of them was
	/// "whoever gets here writes", which is a dupe waiting to happen in three separate ways:
	/// </para>
	/// <list type="number">
	///   <item><description>
	///     A player signs in on scene server A while still connected to B. Both processes hold a
	///     live in-memory copy of the same containers, and both persist. The later writer wins per
	///     slot, so the two inventories interleave and an item can end up recorded twice.
	///   </description></item>
	///   <item><description>
	///     A scene transfer lands mid-operation. The source server has already mutated memory and
	///     enqueued the write; the destination has already loaded the character from the database.
	///     The source's write then lands on top of the destination's freshly loaded state.
	///   </description></item>
	///   <item><description>
	///     A queued persist completes after the character despawned, by which time the character
	///     may have been claimed somewhere else.
	///   </description></item>
	/// </list>
	/// <para>
	/// The project already had the answer and item writes simply were not using it:
	/// <c>characters.session_state</c> / <c>session_owner_server_id</c> / <c>session_owner_token</c>,
	/// claimed by <c>ICharacterService.TryClaimAsync</c> and carried in memory as
	/// <c>CharacterSessionInfo</c>. This service turns that triple into a precondition for writing.
	/// </para>
	/// <para>
	/// HOW IT IS SAFE, not merely checked. The assertion locks the character row
	/// (<c>SELECT ... FOR NO KEY UPDATE</c>) before comparing. <c>TryClaimAsync</c> and
	/// <c>ReleaseAsync</c> are plain <c>UPDATE</c>s on that same row, and an <c>UPDATE</c> takes a
	/// conflicting row lock, so a competing claim BLOCKS until the item transaction commits or rolls
	/// back, and any later write from the displaced server fails the comparison. Without the lock
	/// this would be a check-then-act with a window between them — which is the bug, not the fix.
	/// (Measured against PostgreSQL 18.6: a concurrent claim waits for the holder's transaction.)
	/// </para>
	/// <para>
	/// Consequently this method is only meaningful inside a unit of work whose transaction outlives
	/// it. Called without one it refuses rather than returning a reassuring answer that expires the
	/// instant it is given.
	/// </para>
	/// </remarks>
	public interface ICharacterSessionOwnershipService
	{
		/// <summary>
		/// Asserts that the caller may still write this character's state, and holds the character
		/// row locked for the rest of the ambient transaction.
		/// </summary>
		/// <param name="lease">
		/// The ownership triple the caller believes it holds. May be default/invalid when the caller
		/// has already given the claim up — see <paramref name="allowUnclaimed"/>.
		/// </param>
		/// <param name="allowUnclaimed">
		/// <para>
		/// When <c>true</c>, a character whose session is <b>unowned</b> (state Offline with a zeroed
		/// owner) also passes, whether or not <paramref name="lease"/> is valid.
		/// </para>
		/// <para>
		/// This is not leniency, it is the correct predicate for a final flush. A character nobody
		/// has claimed has no authoritative holder for the write to conflict with, and the ONE thing
		/// a stale write must never do — overwrite a live session's state — is still refused,
		/// because a live session is by definition Online with a token that will not match.
		/// </para>
		/// <para>
		/// It is needed because <c>CharacterSystem.SaveAndDespawnCharacter</c> takes the token out of
		/// <c>SessionTokens</c> and enqueues the session release <em>before</em> it raises
		/// <c>OnDespawnCharacter</c>, which is the hook the logout item flush hangs off. Requiring a
		/// live claim there would silently discard everything the player did since the last periodic
		/// snapshot — a far worse failure than the one being guarded against.
		/// </para>
		/// <para>
		/// RESIDUAL, stated rather than hidden: a process stalled past its lease expiry (default two
		/// minutes) can have its character claimed, played and released elsewhere, and its zombie
		/// write would then find the row unowned and land. The lease duration is what bounds this,
		/// exactly as it bounds the position split-brain already documented on
		/// <c>ICharacterService.UpdatePositionAsync</c>.
		/// </para>
		/// </param>
		/// <param name="characterId">
		/// The character to assert. Must match <paramref name="lease"/> when the lease is valid; it
		/// is supplied separately so an unclaimed-only assertion can be made with no lease at all.
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// Success when the caller may write.
		/// <see cref="DatabaseErrorCodes.Forbidden"/> when the session belongs to someone else, or is
		/// unowned and <paramref name="allowUnclaimed"/> is <c>false</c>, or the character has been
		/// deleted — the write must be abandoned, not retried.
		/// <see cref="DatabaseErrorCodes.NotFound"/> when no such character row exists.
		/// <see cref="DatabaseErrorCodes.InvalidOperation"/> when called outside a unit of work.
		/// </returns>
		Task<DatabaseResult> AssertOwnershipAsync(long characterId, CharacterSessionLeaseData lease, bool allowUnclaimed = false, CancellationToken cancellationToken = default);
	}
}
