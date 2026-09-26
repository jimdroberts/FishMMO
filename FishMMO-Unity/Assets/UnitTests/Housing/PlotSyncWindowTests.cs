using System;
using FishMMO.Server.Implementation.World.SceneServer;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for the cross-channel plot sync's window: where each poll starts, and when a finished
	/// poll may move it on.
	/// </summary>
	/// <remarks>
	/// Plot marks are stamped by the database clock. The window used to be measured on the polling
	/// server's clock, minus ten seconds, so a server whose clock ran more than ten seconds ahead
	/// stepped past marks it had not read yet and lost them for good — another channel's sale, a
	/// finished house, a revoked key. Every time here is the database's, and these pin the two
	/// rules that make that sound.
	/// </remarks>
	[TestFixture]
	public class PlotSyncWindowTests
	{
		private static readonly DateTime DbNow = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
		private static readonly TimeSpan Margin = TimeSpan.FromSeconds(10);

		[Test]
		public void AWorldWithNoWatermark_ReadsEveryMark()
		{
			Assert.AreEqual(DateTime.MinValue, PlotSyncWindow.Since(false, default, Margin),
				"The first poll, and the first after a scene resolves, cannot know what it missed; it must read it all.");
		}

		[Test]
		public void AWindow_StartsAMarginBeforeTheLastPollsDatabaseTime()
		{
			Assert.AreEqual(DbNow - Margin, PlotSyncWindow.Since(true, DbNow, Margin),
				"A mark stamped just before the last poll and committed just after it must still be found.");
		}

		[Test]
		public void AWatermarkNearTheStartOfTime_DoesNotUnderflow()
		{
			Assert.AreEqual(DateTime.MinValue, PlotSyncWindow.Since(true, DateTime.MinValue.AddSeconds(3), Margin));
		}

		[Test]
		public void ANegativeMargin_IsTreatedAsNone()
		{
			Assert.AreEqual(DbNow, PlotSyncWindow.Since(true, DbNow, TimeSpan.FromSeconds(-5)),
				"A window must never start after the last poll's time, or marks between the two are skipped.");
		}

		[Test]
		public void AFinishedPoll_MovesTheWindowForward()
		{
			Assert.IsTrue(PlotSyncWindow.ShouldAdvance(3, 3, true, DbNow, DbNow.AddSeconds(10)));
		}

		[Test]
		public void AFinishedPoll_SetsTheFirstWatermark()
		{
			Assert.IsTrue(PlotSyncWindow.ShouldAdvance(0, 0, false, default, DbNow));
		}

		[Test]
		public void ASlowPollFinishingLate_CannotRewindTheWindow()
		{
			Assert.IsFalse(PlotSyncWindow.ShouldAdvance(3, 3, true, DbNow, DbNow.AddSeconds(-10)),
				"A later poll already moved the window past this one; going back would re-read, and moving it back is never right.");
		}

		/// <summary>
		/// A scene resolved while this poll was out restarted the window; letting the poll finish
		/// and set the old watermark back would close the gap the restart exists to cover.
		/// </summary>
		[Test]
		public void APollSentBeforeASceneResolved_CannotCloseTheRestartedWindow()
		{
			Assert.IsFalse(PlotSyncWindow.ShouldAdvance(pollEpoch: 3, currentEpoch: 4, hasWatermark: false, watermarkUtc: default, pollAsOfUtc: DbNow),
				"The resolved scene's plots were read at a moment this poll's window knows nothing about.");
		}
	}
}
