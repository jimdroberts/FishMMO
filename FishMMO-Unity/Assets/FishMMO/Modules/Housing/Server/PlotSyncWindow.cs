using System;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Where each poll of <c>plot_updates</c> starts reading, and when a finished poll may move
	/// that point on.
	/// </summary>
	/// <remarks>
	/// <para>Every time here is on the <b>database</b> clock. Writers stamp their marks with it, and
	/// each poll reports the database time it was taken at; a window measured on the poller's own
	/// clock lost, for good, every change stamped inside however far that clock ran ahead.</para>
	///
	/// <para>A window still starts a little before the last poll's time. A mark is stamped when
	/// its statement runs and becomes visible when its transaction commits, so one stamped just
	/// before a poll can commit just after it. The margin re-reads those few; applying a change
	/// twice is harmless, missing it is not.</para>
	///
	/// <para>A world with no watermark reads every mark there is. That is the first poll after
	/// startup, and the first after a scene of the world is resolved: the resolve read its plots at
	/// some moment the running window knows nothing about, so the only window guaranteed to cover
	/// the gap is all of it. It is bounded — one mark per watched plot — and happens once per scene
	/// load. The epoch is what makes that restart stick: a poll that was already out when the scene
	/// resolved would otherwise finish and put the old window straight back.</para>
	/// </remarks>
	public static class PlotSyncWindow
	{
		/// <summary>
		/// Where the next poll of a world starts.
		/// </summary>
		/// <param name="hasWatermark">Whether the world has completed a poll since its window last restarted.</param>
		/// <param name="watermarkUtc">The database time of that poll.</param>
		/// <param name="commitMargin">How far behind it to start, for marks committed after their stamp.</param>
		/// <returns><see cref="DateTime.MinValue"/> — every mark — when there is no watermark.</returns>
		public static DateTime Since(bool hasWatermark, DateTime watermarkUtc, TimeSpan commitMargin)
		{
			if (!hasWatermark)
			{
				return DateTime.MinValue;
			}

			TimeSpan margin = commitMargin < TimeSpan.Zero ? TimeSpan.Zero : commitMargin;
			if (watermarkUtc - DateTime.MinValue <= margin)
			{
				return DateTime.MinValue;
			}

			return watermarkUtc - margin;
		}

		/// <summary>
		/// Whether a poll that read and applied everything it found may move its world's watermark.
		/// </summary>
		/// <param name="pollEpoch">The world's epoch when the poll was sent.</param>
		/// <param name="currentEpoch">The world's epoch now.</param>
		/// <param name="hasWatermark">Whether the world has a watermark now.</param>
		/// <param name="watermarkUtc">That watermark.</param>
		/// <param name="pollAsOfUtc">The database time the finished poll was taken at.</param>
		/// <remarks>
		/// Only ever forward, so a slow poll finishing after a later one cannot rewind the window;
		/// and never across a restart, so a poll sent before a scene resolved cannot close the window
		/// the resolve opened.
		/// </remarks>
		public static bool ShouldAdvance(int pollEpoch, int currentEpoch, bool hasWatermark, DateTime watermarkUtc, DateTime pollAsOfUtc)
		{
			if (pollEpoch != currentEpoch)
			{
				return false;
			}

			return !hasWatermark || watermarkUtc < pollAsOfUtc;
		}
	}
}
