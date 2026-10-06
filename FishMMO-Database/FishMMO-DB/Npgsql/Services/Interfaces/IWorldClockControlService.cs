using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// The one world clock: the single <c>world_clock_control</c> row every scene server adopts on
	/// its next pulse, and the admin writes that set, shift, pace, hold and resume it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// World time is world milliseconds since the calendar epoch, and the row stores an anchor
	/// rather than a value: world ms now = base_world_ms + rate × max(0, db_now_ms −
	/// base_reference_ms), floored, computed IN SQL against <c>clock_timestamp()</c>. No caller's
	/// clock enters the arithmetic, so every scene server and the panel agree to the millisecond.
	/// </para>
	/// <para>
	/// <b>Every write is ONE statement.</b> It locks the row, computes world-now from it at the
	/// database instant, re-anchors there, bumps <c>revision</c>, stamps <c>updated_by</c> and
	/// <c>updated_at</c>, and returns the new reading — exact to the millisecond and free of any
	/// read-modify-write race between two admins. Each write also carries an idempotency key
	/// (taken once, outside the retry loop), so a retried write whose first attempt committed but
	/// lost its reply is not applied twice; that matters for <see cref="ShiftAsync"/>.
	/// </para>
	/// <para>
	/// <b>The row is seeded by scene servers</b> (<see cref="EnsureSeededAsync"/>), never by an admin
	/// write. Every write fails with <see cref="DatabaseErrorCodes.NotFound"/> and a sentence saying
	/// so when no scene server has run against this database yet.
	/// </para>
	/// <para>
	/// A write is adopted by the scene servers on their next pulse. An acknowledgement says what was
	/// WRITTEN, never that it has taken effect.
	/// </para>
	/// </remarks>
	public interface IWorldClockControlService
	{
		/// <summary>
		/// Reads the database clock and the row in one round trip. Succeeds with
		/// <see cref="WorldClockReading.Exists"/> false when there is no row yet.
		/// </summary>
		Task<DatabaseResult<WorldClockReading>> ReadAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Creates the row if there is none (<c>INSERT … ON CONFLICT (id) DO NOTHING</c>): world time
		/// = the database's now − the epoch (floored at zero), real-time pace, revision 1. An existing
		/// row is left exactly as it is, epoch included.
		/// </summary>
		/// <param name="epochUnixSeconds">The calendar epoch the scene servers use, Unix seconds.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// The row as it now stands (<see cref="WorldClockReading.Exists"/> is true on success). Compare
		/// its <see cref="WorldClockReading.EpochUnixSeconds"/> with your own: a row seeded earlier keeps
		/// the epoch it was seeded with.
		/// </returns>
		Task<DatabaseResult<WorldClockReading>> EnsureSeededAsync(long epochUnixSeconds, CancellationToken cancellationToken = default);

		/// <summary>Sets world time to <paramref name="worldMs"/> at this database instant, keeping the pace.</summary>
		/// <param name="worldMs">World milliseconds since the epoch; at least zero, and a calendar date (year ≤ 9999).</param>
		/// <param name="actor">The ACCOUNT making the change (recorded as updated_by).</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The new reading, with <see cref="WorldClockReading.ReplacedWorldMs"/> and <see cref="WorldClockReading.ReplacedRate"/> set.</returns>
		Task<DatabaseResult<WorldClockReading>> SetAsync(long worldMs, string actor, CancellationToken cancellationToken = default);

		/// <summary>Moves world time by <paramref name="deltaMs"/> from world-now, keeping the pace.</summary>
		/// <param name="deltaMs">Signed milliseconds. Refused when the result would fall before the epoch or past year 9999.</param>
		/// <param name="actor">The ACCOUNT making the change.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The new reading.</returns>
		Task<DatabaseResult<WorldClockReading>> ShiftAsync(long deltaMs, string actor, CancellationToken cancellationToken = default);

		/// <summary>
		/// Changes the pace from this instant without moving world time. Above zero it also becomes
		/// the resume pace; zero is a hold (the running pace is kept as the resume pace).
		/// </summary>
		/// <param name="rate">World seconds per real second, 0 to <see cref="WorldClockLimits.MaxRate"/>. Negative, NaN and infinity are refused.</param>
		/// <param name="actor">The ACCOUNT making the change.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The new reading.</returns>
		Task<DatabaseResult<WorldClockReading>> SetRateAsync(double rate, string actor, CancellationToken cancellationToken = default);

		/// <summary>
		/// Holds world time where it is now (pace 0). The running pace becomes the resume pace;
		/// holding an already held clock keeps the resume pace it had.
		/// </summary>
		Task<DatabaseResult<WorldClockReading>> HoldAsync(string actor, CancellationToken cancellationToken = default);

		/// <summary>
		/// Resumes at the resume pace from where world time stands (while held, that is the held
		/// time). On a clock that is already running it re-applies the resume pace.
		/// </summary>
		Task<DatabaseResult<WorldClockReading>> ResumeAsync(string actor, CancellationToken cancellationToken = default);
	}
}
