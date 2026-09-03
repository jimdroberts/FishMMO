using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for the dungeon group finder's queue: who is waiting for a group, and
	/// the one operation that turns waiters into a party with an instance.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The queue is shared by every scene server on a world server, and every one of them runs
	/// the same matching pump against it. Nothing here assumes a single matcher: the methods that
	/// change a row's state are single statements or single transactions whose <c>WHERE</c>
	/// clauses re-assert the state they expect, so two servers acting on the same rows at once
	/// produce one winner and one no-op rather than a character in two groups.
	/// </para>
	/// <para>
	/// All methods return <see cref="DatabaseResult"/> or <see cref="DatabaseResult{T}"/>. Write
	/// operations go through the <c>BaseService</c> execution wrappers for transient-failure
	/// retry and exception mapping.
	/// </para>
	/// </remarks>
	public interface IGroupFinderQueueService
	{
		/// <summary>
		/// Puts a character in the queue for one dungeon at one difficulty, or moves their
		/// existing entry to it.
		/// </summary>
		/// <remarks>
		/// A character already waiting for something else is re-pointed rather than refused —
		/// pressing Find Group at a second entrance means "this one instead", and their place in
		/// line restarts. A character whose row is already <see cref="GroupFinderQueueStatus.Matched"/>
		/// cannot be moved: the group they were placed in is real, and their scene server is about
		/// to transfer them into it.
		/// </remarks>
		/// <param name="worldServerId">World server the character belongs to.</param>
		/// <param name="characterId">Character queuing.</param>
		/// <param name="sceneName">Dungeon scene they want to run.</param>
		/// <param name="difficulty">Difficulty index into the dungeon's own list.</param>
		/// <param name="stalePulsedBeforeUtc">
		/// A matched row whose heartbeat is older than this is treated as abandoned and re-pointed
		/// like a waiting one. Without this a character whose scene server died between the match
		/// and the transfer could never queue again until the stale sweep happened to reach them.
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The row's ID, or <c>0</c> when the character is already matched and nothing changed.</returns>
		Task<DatabaseResult<long>> EnqueueAsync(long worldServerId, long characterId, string sceneName, int difficulty, DateTime stalePulsedBeforeUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Removes a character's queue row.
		/// </summary>
		/// <param name="characterId">Character to remove.</param>
		/// <param name="onlyIfWaiting">
		/// True to leave a <see cref="GroupFinderQueueStatus.Matched"/> row alone. A player who
		/// presses Leave in the instant after their group formed has already been placed in a
		/// party, and quietly deleting the row would leave them in that party with no transfer
		/// coming; the caller must refuse instead. False when the row's job is done — the transfer
		/// has been dispatched — or when the character is gone.
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>True when a row was removed.</returns>
		Task<DatabaseResult<bool>> DeleteAsync(long characterId, bool onlyIfWaiting, CancellationToken cancellationToken = default);

		/// <summary>
		/// Refreshes the heartbeat on the rows of characters connected to the calling scene server.
		/// </summary>
		/// <remarks>
		/// A row whose heartbeat stops is excluded from matching by <see cref="TryFormGroupAsync"/>
		/// and <see cref="CountWaitingAsync"/> and eventually removed by <see cref="DeleteStaleAsync"/>.
		/// That is the only defence against a scene server dying with waiters on it: nothing else
		/// would ever remove them, and a group formed around a character nobody can reach is a
		/// party of four with an empty slot that never fills.
		/// </remarks>
		/// <param name="characterIds">Characters to pulse; duplicates and non-positive ids are ignored.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Rows updated.</returns>
		Task<DatabaseResult<int>> PulseAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default);

		/// <summary>
		/// Reads the queue rows of the given characters.
		/// </summary>
		/// <remarks>
		/// The scene server's pump reads its own characters' rows once per interval to learn which
		/// of them have been matched — by itself or by any other server — and which have been
		/// removed underneath it.
		/// </remarks>
		/// <param name="characterIds">Characters to look up; duplicates and non-positive ids are ignored.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The rows that exist. Characters with no row are simply absent.</returns>
		Task<DatabaseResult<IReadOnlyList<GroupFinderQueueData>>> FetchByCharactersAsync(IReadOnlyList<long> characterIds, CancellationToken cancellationToken = default);

		/// <summary>
		/// Counts the live waiters for one dungeon at one difficulty.
		/// </summary>
		/// <param name="worldServerId">World server to count on.</param>
		/// <param name="sceneName">Dungeon scene.</param>
		/// <param name="difficulty">Difficulty index.</param>
		/// <param name="pulsedSinceUtc">Rows whose heartbeat is older than this are not counted.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>How many are waiting.</returns>
		Task<DatabaseResult<int>> CountWaitingAsync(long worldServerId, string sceneName, int difficulty, DateTime pulsedSinceUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Forms a group from the longest-waiting eligible players, creating their party and
		/// opening their instance, all in one transaction.
		/// </summary>
		/// <remarks>
		/// <para>
		/// One transaction, and nothing observable in between. The waiters are selected and
		/// row-locked, the party is created, every member's membership row is inserted, the
		/// instance row is inserted under the same one-instance-per-party guard the dungeon
		/// finder's own open path uses, and the queue rows are marked matched — and if any step
		/// cannot complete, none of it happened. There is no "claimed" state to recover, no
		/// half-formed party to dissolve, and no crash window in which a row names a party that
		/// does not exist.
		/// </para>
		/// <para>
		/// Eligibility is re-checked inside the transaction rather than trusted from queue time. A
		/// waiter who has since joined a party, or whose party holds an instance, is skipped — and
		/// because the membership insert is <c>ON CONFLICT DO NOTHING</c> and its row count is
		/// checked, a party join that lands between the select and the insert rolls the whole
		/// group back rather than moving that character out of the party they just accepted.
		/// </para>
		/// <para>
		/// Concurrent callers on different scene servers lock the same candidate rows and
		/// serialise on them; the second one re-evaluates its predicate after the first commits,
		/// finds those rows matched, and forms nothing. The pump retries on its next interval.
		/// </para>
		/// </remarks>
		/// <param name="worldServerId">World server to form the group on.</param>
		/// <param name="sceneName">Dungeon scene to open.</param>
		/// <param name="difficulty">Difficulty to open it at.</param>
		/// <param name="groupSize">Exactly how many players to take. Nothing forms with fewer.</param>
		/// <param name="pulsedSinceUtc">Waiters whose heartbeat is older than this are not eligible.</param>
		/// <param name="sceneType">Scene type to record on the instance row.</param>
		/// <param name="leaderRank">Party rank value to write for the leader.</param>
		/// <param name="memberRank">Party rank value to write for everybody else.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// The group, or a result whose <see cref="GroupFinderMatchData.Formed"/> is false when
		/// fewer than <paramref name="groupSize"/> eligible players were waiting. Failure results
		/// carry the database error.
		/// </returns>
		Task<DatabaseResult<GroupFinderMatchData>> TryFormGroupAsync(
			long worldServerId,
			string sceneName,
			int difficulty,
			int groupSize,
			DateTime pulsedSinceUtc,
			SceneType sceneType,
			byte leaderRank,
			byte memberRank,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Marks one waiting character as matched into an existing instance, for the late-join
		/// path that fills empty slots in runs already open.
		/// </summary>
		/// <remarks>
		/// The status is re-asserted in the <c>WHERE</c>: a row that another server has already
		/// matched is not touched, so a character cannot be handed to two runs at once. The caller
		/// adds the character to the instance's party after this succeeds, and calls
		/// <see cref="ReleaseClaimAsync"/> if that is refused.
		/// </remarks>
		/// <param name="characterId">Waiting character.</param>
		/// <param name="partyId">Party that owns the instance.</param>
		/// <param name="instanceId">Instance being joined.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>True when the row was waiting and is now matched.</returns>
		Task<DatabaseResult<bool>> TryClaimForInstanceAsync(long characterId, long partyId, long instanceId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Returns a character claimed by <see cref="TryClaimForInstanceAsync"/> to the waiting
		/// state, when the join it was claimed for could not be completed.
		/// </summary>
		/// <param name="characterId">Character to release.</param>
		/// <param name="instanceId">Instance the claim named; a row matched to anything else is left alone.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>True when a row was released.</returns>
		Task<DatabaseResult<bool>> ReleaseClaimAsync(long characterId, long instanceId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Removes rows whose heartbeat stopped before <paramref name="pulsedBeforeUtc"/>.
		/// </summary>
		/// <remarks>
		/// Bounded per call so a large backlog drains across several sweeps rather than in one
		/// long statement. Matched rows are reaped too: a matched character whose server died was
		/// placed in a party it will never be transferred into, and the party system's own absent-
		/// member handling takes it from there.
		/// </remarks>
		/// <param name="pulsedBeforeUtc">Rows whose heartbeat is older than this are eligible.</param>
		/// <param name="maxRows">Upper bound on rows removed in one call.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Rows deleted.</returns>
		Task<DatabaseResult<int>> DeleteStaleAsync(DateTime pulsedBeforeUtc, int maxRows = 256, CancellationToken cancellationToken = default);
	}
}
