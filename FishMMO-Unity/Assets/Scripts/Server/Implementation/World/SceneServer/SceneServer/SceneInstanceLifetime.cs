using System;
using System.Collections.Generic;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The arithmetic of a scene instance's lifetime cap, its closing warnings and its idle
	/// timeout.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every time here is seconds on this process's <see cref="FishMMO.Server.Core.MonotonicClock"/>.
	/// These are all durations, and a duration measured on the host's wall clock moves when that
	/// clock is stepped: a forward step of N minutes used to close, on one pulse, every instance
	/// within N minutes of its cap, with its players still inside.
	/// </para>
	/// <para>
	/// The one input that does not start in this process is the instance's age when its row was
	/// dequeued. The database measures that against the clock that stamped the row, and
	/// <see cref="CreatedAt"/> turns it into a point on the monotonic clock, so no host clock takes
	/// part in the comparison at all.
	/// </para>
	/// <para>
	/// Pure and static so the rules are pinned by tests without a scene server.
	/// </para>
	/// </remarks>
	public static class SceneInstanceLifetime
	{
		/// <summary>
		/// Where a scene row's creation falls on the monotonic clock.
		/// </summary>
		/// <param name="observedAt">Monotonic time at which the age was read.</param>
		/// <param name="ageSeconds">
		/// The row's age at that moment, by the database clock. Negative or not a number reads as
		/// zero: "created in the future" is not an age anything can be measured from.
		/// </param>
		public static double CreatedAt(double observedAt, double ageSeconds)
		{
			return observedAt - (ageSeconds > 0.0 ? ageSeconds : 0.0);
		}

		/// <summary>
		/// Seconds left before an instance reaches its lifetime cap. Zero or less once it has.
		/// </summary>
		public static double RemainingSeconds(double now, double createdAt, double lifetimeSeconds)
		{
			return lifetimeSeconds - (now - createdAt);
		}

		/// <summary>
		/// Whether an instance has reached its lifetime cap.
		/// </summary>
		public static bool IsExpired(double now, double createdAt, double lifetimeSeconds)
		{
			return RemainingSeconds(now, createdAt, lifetimeSeconds) <= 0.0;
		}

		/// <summary>
		/// Whether an empty scene has been empty for at least its idle timeout.
		/// </summary>
		public static bool IsIdleExpired(double now, double lastExitAt, double timeoutSeconds)
		{
			return now - lastExitAt >= timeoutSeconds;
		}

		/// <summary>
		/// Which closing warning, if any, is due now.
		/// </summary>
		/// <param name="remainingSeconds">Time left before the instance closes.</param>
		/// <param name="marks">
		/// Remaining-time marks, in seconds, at which occupants are warned. Any order.
		/// </param>
		/// <param name="announced">
		/// Marks already dealt with for this instance. Updated in place: the mark warned for, and
		/// every larger one, are added.
		/// </param>
		/// <returns>
		/// The time to announce in seconds, rounded up to a whole minute, or 0 when nothing is due.
		/// </returns>
		/// <remarks>
		/// <para>
		/// The tightest mark crossed wins, and every larger one is retired with it. Warning for the
		/// first unannounced mark instead told players a dungeon "closes in 10 minutes" with four
		/// left, whenever an instance crossed more than one mark between two checks: one whose
		/// difficulty allows less than ten minutes in total, one that spent most of its life queued
		/// and loading, or one whose checks were held up. The next check then said "5 minutes",
		/// when it had fewer.
		/// </para>
		/// <para>
		/// The time announced is what is actually left, rounded up to the minute, which in the
		/// ordinary case (a mark crossed on the check just after it) is the mark itself.
		/// </para>
		/// </remarks>
		public static int ResolveExpiryWarning(double remainingSeconds, IReadOnlyList<int> marks, ISet<int> announced)
		{
			if (marks == null || announced == null || !(remainingSeconds > 0.0))
			{
				return 0;
			}

			int tightest = int.MaxValue;
			for (int i = 0; i < marks.Count; ++i)
			{
				int mark = marks[i];
				if (remainingSeconds <= mark && mark < tightest)
				{
					tightest = mark;
				}
			}

			if (tightest == int.MaxValue || announced.Contains(tightest))
			{
				return 0;
			}

			for (int i = 0; i < marks.Count; ++i)
			{
				if (marks[i] >= tightest)
				{
					announced.Add(marks[i]);
				}
			}

			int minutes = (int)Math.Ceiling(remainingSeconds / 60.0);
			return Math.Max(1, minutes) * 60;
		}
	}
}
