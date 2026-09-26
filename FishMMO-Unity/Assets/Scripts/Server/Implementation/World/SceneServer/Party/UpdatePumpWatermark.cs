using System;
using System.Threading;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The watermark rule shared by the party and guild update pumps.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Both pumps poll an update table — one row per party or guild, stamped by the DATABASE clock
	/// whenever it changes — for everything at or after a mark this server keeps. What the mark may
	/// advance to decides whether an update can be lost, and the two pumps used to answer that
	/// differently: the party pump held its mark back for an unread update only within a bounded
	/// horizon and remembered what it had processed, while the guild pump held it back at the
	/// oldest unread update for ever. One guild whose read kept failing pinned the guild mark, and
	/// every guild updated after it was re-read and re-broadcast every second, indefinitely.
	/// </para>
	/// <para>
	/// The rule, as a truth table over one update the pass has not already processed:
	/// </para>
	/// <list type="bullet">
	/// <item>its snapshot was read — <see cref="Outcome.Processed"/>: delivered, recorded as
	/// processed, and no constraint on the mark;</item>
	/// <item>not read, stamped at or after the retry horizon — <see cref="Outcome.Retry"/>: the mark
	/// is held at its timestamp so the next pass fetches it again;</item>
	/// <item>not read, stamped before the retry horizon — <see cref="Outcome.GiveUp"/>: logged once
	/// and released, so it can no longer hold the mark.</item>
	/// </list>
	/// <para>
	/// The mark starts each pass at the moment the fetch was sent, less the skew allowance, because
	/// the rows are stamped by the database's clock and the mark by this server's. Updates inside
	/// that window are fetched again on later passes, and the processed-update record is what makes
	/// that free: they are skipped rather than re-read.
	/// </para>
	/// <para>
	/// Pure and static so the rule can be pinned by a test without a server or a database.
	/// </para>
	/// </remarks>
	internal static class UpdatePumpWatermark
	{
		/// <summary>What a pass does with one update it has not already processed.</summary>
		internal enum Outcome : byte
		{
			/// <summary>The snapshot was read; deliver it and record it as processed.</summary>
			Processed = 0,
			/// <summary>The snapshot could not be read; hold the mark so the next pass retries it.</summary>
			Retry = 1,
			/// <summary>Unreadable for longer than the retry horizon; release it.</summary>
			GiveUp = 2,
		}

		/// <summary>
		/// The shortest retry horizon, in seconds, whatever the skew allowance.
		/// </summary>
		internal const float MinimumRetryHorizonSeconds = 60.0f;

		/// <summary>
		/// How long an unread update stays worth re-reading.
		/// </summary>
		/// <param name="skewAllowanceSeconds">The pump's clock skew allowance.</param>
		/// <returns>Ten times the allowance, and never less than <see cref="MinimumRetryHorizonSeconds"/>.</returns>
		internal static TimeSpan RetryHorizon(float skewAllowanceSeconds)
		{
			return TimeSpan.FromSeconds(Math.Max(MinimumRetryHorizonSeconds, skewAllowanceSeconds * 10.0f));
		}

		/// <summary>
		/// How long a processed-update record is kept: twice the retry horizon.
		/// </summary>
		/// <param name="skewAllowanceSeconds">The pump's clock skew allowance.</param>
		/// <returns>The age, by this server's clock, past which a record may be swept.</returns>
		/// <remarks>
		/// <para>
		/// Every update behind a held mark is fetched again on each pass and skipped only because its
		/// record says it was handled, so a record swept while its update can still be fetched turns
		/// that skip into a re-read and re-broadcast. An update stays fetchable for as long as the
		/// mark can sit at or before it: the retry horizon, plus the skew the mark trails by, plus a
		/// pass. And the record is stamped by the database's clock but aged by this server's, which
		/// can make it look older by up to the skew again.
		/// </para>
		/// <para>
		/// So the lifetime must exceed horizon + 2·skew + one pass. The horizon is at least ten
		/// times the skew and at least a minute, so twice the horizon always does, with room — and a
		/// record costs a dictionary entry per party or guild that changed in the last two minutes.
		/// Sweeping at exactly the horizon, as the party pump used to, left that margin negative.
		/// </para>
		/// </remarks>
		internal static TimeSpan ProcessedRecordLifetime(float skewAllowanceSeconds)
		{
			return RetryHorizon(skewAllowanceSeconds) + RetryHorizon(skewAllowanceSeconds);
		}

		/// <summary>
		/// Where a pass's mark starts: the moment the fetch was sent, less the skew allowance.
		/// </summary>
		/// <param name="nowUtc">This server's clock, read BEFORE the fetch is sent.</param>
		/// <param name="skewAllowanceSeconds">The pump's clock skew allowance.</param>
		/// <returns>The initial mark, as UTC.</returns>
		/// <remarks>
		/// Read before the query, never after it: an update written while the round trip was in
		/// flight is stamped after this and is therefore fetched by the next pass. A mark taken after
		/// the round trip skipped exactly those updates.
		/// </remarks>
		internal static DateTime FetchStarted(DateTime nowUtc, float skewAllowanceSeconds)
		{
			return DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc).AddSeconds(-Math.Max(0.0f, skewAllowanceSeconds));
		}

		/// <summary>
		/// Classifies one update the pass has not already processed.
		/// </summary>
		/// <param name="read">Whether its snapshot was read in this pass.</param>
		/// <param name="lastUpdateUtc">The update row's timestamp.</param>
		/// <param name="retryHorizonUtc">The oldest timestamp still worth re-reading.</param>
		/// <returns>What to do with it.</returns>
		/// <summary>
		/// The database's clock as a pump last read it, carried forward on the monotonic clock.
		/// </summary>
		/// <remarks>
		/// The processed-update records are the rows' database stamps, so they are aged against this
		/// rather than the host clock. One word, written from the pump's continuation and read by the
		/// sweep, so it is exchanged atomically.
		/// </remarks>
		internal sealed class DatabaseClock
		{
			private const long Unset = long.MinValue;
			private long offsetTicks = Unset;

			/// <summary>Records the database's clock as a fetch read it.</summary>
			internal void Observe(DateTime databaseUtc)
			{
				if (databaseUtc == DateTime.MinValue)
				{
					return;
				}
				Interlocked.Exchange(ref offsetTicks, databaseUtc.Ticks - MonotonicClock.NowTicks);
			}

			/// <summary>The database's clock now, or false until a fetch has read it.</summary>
			internal bool TryNow(out DateTime databaseUtc)
			{
				long offset = Interlocked.Read(ref offsetTicks);
				if (offset == Unset)
				{
					databaseUtc = default;
					return false;
				}
				databaseUtc = new DateTime(MonotonicClock.NowTicks + offset, DateTimeKind.Utc);
				return true;
			}
		}

		internal static Outcome Classify(bool read, DateTime lastUpdateUtc, DateTime retryHorizonUtc)
		{
			if (read)
			{
				return Outcome.Processed;
			}

			return lastUpdateUtc >= retryHorizonUtc ? Outcome.Retry : Outcome.GiveUp;
		}

		/// <summary>
		/// Applies one update's outcome to the mark the pass will advance to.
		/// </summary>
		/// <param name="watermarkUtc">The mark so far.</param>
		/// <param name="outcome">The update's outcome.</param>
		/// <param name="lastUpdateUtc">The update row's timestamp.</param>
		/// <returns>The mark after this update: held at the update for a retry, otherwise unchanged.</returns>
		internal static DateTime Hold(DateTime watermarkUtc, Outcome outcome, DateTime lastUpdateUtc)
		{
			if (outcome == Outcome.Retry && lastUpdateUtc < watermarkUtc)
			{
				return DateTime.SpecifyKind(lastUpdateUtc, DateTimeKind.Utc);
			}

			return watermarkUtc;
		}
	}
}
