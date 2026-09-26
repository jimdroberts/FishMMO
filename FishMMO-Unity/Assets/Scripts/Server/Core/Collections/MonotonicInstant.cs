using System;

namespace FishMMO.Server.Core.Collections
{
	/// <summary>
	/// Carries a <see cref="MonotonicClock"/> reading in a <see cref="DateTime"/>, so a tracker whose
	/// store is <see cref="DateTime"/> can be driven by the monotonic clock without a second
	/// implementation.
	/// </summary>
	/// <remarks>
	/// The result is not a calendar instant: it is the reading's seconds as ticks from
	/// <see cref="DateTime.MinValue"/>, and it means nothing outside the tracker that stored it. The
	/// trackers only compare the instants they are given with one another and add durations to them,
	/// which is all a monotonic reading supports. One tracker, one clock: an instance driven through
	/// both its <see cref="DateTime"/> and its monotonic overloads compares unrelated numbers.
	/// Shared by <see cref="ExpiringKeyTracker{TKey}"/> and <see cref="LastSeenCacheTracker{TKey, TValue}"/>.
	/// </remarks>
	internal static class MonotonicInstant
	{
		/// <summary>The <see cref="DateTime"/> that stands for a monotonic reading of <paramref name="seconds"/>.</summary>
		/// <param name="seconds">A <see cref="MonotonicClock.NowSeconds"/> reading.</param>
		/// <returns>The reading as a tracker instant.</returns>
		internal static DateTime From(double seconds)
		{
			double ticks = seconds * TimeSpan.TicksPerSecond;
			if (!(ticks > 0.0))
			{
				return DateTime.MinValue;
			}
			// Leaves room for a window or TTL added on top by the tracker.
			double ceiling = DateTime.MaxValue.Ticks / 2.0;
			return new DateTime(ticks >= ceiling ? (long)ceiling : (long)ticks);
		}
	}
}
