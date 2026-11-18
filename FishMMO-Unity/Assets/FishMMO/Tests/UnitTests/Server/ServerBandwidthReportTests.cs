using System;
using System.Collections.Generic;
using System.Linq;
using FishMMO.Database.Data;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// How the Bandwidth page is assembled from what its queries return: "no data" stays null,
	/// a tier adds only what its servers reported, and a peak is the busiest point of the summed
	/// series. Pure, over <see cref="ServerBandwidthReportBuilder"/>.
	/// </summary>
	[TestFixture]
	public class ServerBandwidthReportTests
	{
		private static readonly DateTime Now = new DateTime(2026, 9, 26, 12, 30, 20, DateTimeKind.Utc);

		private static ServerBandwidthTotals Totals(long udpSent, long datagrams = 10) => new ServerBandwidthTotals
		{
			Samples = 60,
			IntervalMs = 3_600_000,
			UdpSentBytes = udpSent,
			UdpSentDatagrams = datagrams,
			LastBucketUtc = Now.AddMinutes(-1),
		};

		private static ServerBandwidthWindowRow Window(ServerBandwidthWindow window, int kind, string name, long udpSent, long instances = 1) =>
			new ServerBandwidthWindowRow { Window = window, Kind = kind, Name = name, Totals = Totals(udpSent), Instances = instances };

		private static ServerBandwidthRates Rates(int minutesAgo, double udpSentBps) => new ServerBandwidthRates
		{
			BucketUtc = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo),
			UdpSentBps = udpSentBps,
			DatagramsSentPs = 2,
		};

		private static ServerBandwidthPointRow Point(int kind, string name, int minutesAgo, double udpSentBps, long? sessions = null)
		{
			var rates = Rates(minutesAgo, udpSentBps);
			rates.SessionsActive = sessions;
			return new ServerBandwidthPointRow { Kind = kind, Name = name, Rates = rates };
		}

		private static ServerBandwidthReport Sample(int scopeKind = 0, string scopeName = null)
		{
			var windows = new List<ServerBandwidthWindowRow>
			{
				Window(ServerBandwidthWindow.Hour, ServerBandwidthKind.World, "w1", 100),
				Window(ServerBandwidthWindow.Day, ServerBandwidthKind.World, "w1", 1000, instances: 2),
				Window(ServerBandwidthWindow.Month, ServerBandwidthKind.World, "w1", 10_000),
				Window(ServerBandwidthWindow.Month, ServerBandwidthKind.Scene, "gone", 7),
				Window(ServerBandwidthWindow.Day, ServerBandwidthKind.Scene, "s1", 50),
				Window(ServerBandwidthWindow.Month, ServerBandwidthKind.Scene, "s1", 500),
			};
			var currents = new List<ServerBandwidthPointRow>
			{
				Point(ServerBandwidthKind.World, "w1", 0, 1000, sessions: 4),
				Point(ServerBandwidthKind.Scene, "s1", 0, 300, sessions: 2),
			};
			var peaks = new List<ServerBandwidthPointRow> { Point(ServerBandwidthKind.World, "w1", 5, 4000) };
			var kindSeries = new List<ServerBandwidthKindPointRow>
			{
				new ServerBandwidthKindPointRow { Kind = ServerBandwidthKind.World, Rates = Rates(2, 3000) },
				new ServerBandwidthKindPointRow { Kind = ServerBandwidthKind.Scene, Rates = Rates(2, 2500) },
				new ServerBandwidthKindPointRow { Kind = ServerBandwidthKind.World, Rates = Rates(1, 4000) },
				new ServerBandwidthKindPointRow { Kind = ServerBandwidthKind.Scene, Rates = Rates(1, 100) },
			};
			var serverSeries = new List<ServerBandwidthRates> { Rates(1, 9), Rates(3, 8) };
			return ServerBandwidthReportBuilder.Build(Now, ServerBandwidthRange.Hour, windows, currents, peaks, kindSeries,
				scopeKind, scopeName, scopeName != null ? serverSeries : null);
		}

		[Test]
		public void AServerWithoutRows_HasNoFigure_NotAZero()
		{
			var report = Sample();
			var gone = report.Servers.Single(s => s.Name == "gone");
			LogAssert.IsNull(gone.Current, "no current rate");
			LogAssert.IsNull(gone.LastHour, "no last hour");
			LogAssert.IsNull(gone.LastDay, "no last day");
			LogAssert.IsNull(gone.Peak, "no peak");
			LogAssert.AreEqual(7L, gone.Last30Days.UdpSentBytes, "but its 30 days are counted");
		}

		[Test]
		public void ServersAreListedByTierThenName_WithTheirRestartsAndLastSighting()
		{
			var report = Sample();
			LogAssert.AreEqual("w1,gone,s1", string.Join(",", report.Servers.Select(s => s.Name)), "world before scene, names in order");
			var w1 = report.Servers.First();
			LogAssert.AreEqual(2L, w1.Instances24h, "two processes in 24 hours");
			LogAssert.AreEqual(Now.AddMinutes(-1), w1.LastSeenUtc.Value, "last seen is its newest row, whichever window it came from");
		}

		[Test]
		public void ATier_AddsOnlyWhatItsServersReported()
		{
			var report = Sample();
			var scene = report.Tiers.Single(t => t.Kind == ServerBandwidthKind.Scene);
			LogAssert.AreEqual(2, scene.Servers, "two scene servers in 30 days");
			LogAssert.AreEqual(1, scene.ServersReporting, "one reporting now");
			LogAssert.AreEqual(300.0, scene.Current.UdpSentBps, "the silent one adds nothing, not a zero");
			LogAssert.IsNull(scene.LastHour, "no scene server had an hour: the tier has none either");
			LogAssert.AreEqual(50L, scene.LastDay.UdpSentBytes, "the day is the one server that had one");
			LogAssert.AreEqual(507L, scene.Last30Days.UdpSentBytes, "30 days add both");
			LogAssert.AreEqual(2500.0, scene.Peak.UdpSentBps, "the tier's busiest point of its own series");

			LogAssert.AreEqual(1300.0, report.Total.Current.UdpSentBps, "the grand total adds the reporting servers");
			LogAssert.AreEqual(6L, report.Total.Current.SessionsActive.Value, "and their sessions");
			LogAssert.AreEqual(10_507L, report.Total.Last30Days.UdpSentBytes, "30 days across every server");
			LogAssert.AreEqual(10_507L + (3 * 10 * 28), report.Total.Last30Days.WireSentBytesEstimated, "the wire estimate derives from the sums");
		}

		[Test]
		public void TheGrandPeak_IsTheBusiestPointOfTheSummedSeries()
		{
			/* Not the largest tier peak: world peaks at 4000 a minute after scene's 2500, and the
			 * busiest moment for the shard is 3000 + 2500 = 5500, not 4000 + 100 = 4100. */
			var report = Sample();
			LogAssert.AreEqual(5500.0, report.Total.Peak.UdpSentBps, "the sum at each point, then the maximum");
			LogAssert.AreEqual(Rates(2, 0).BucketUtc, report.Total.Peak.BucketUtc, "at that point's time");
		}

		[Test]
		public void TheChart_FollowsTheScope()
		{
			var all = Sample();
			LogAssert.AreEqual(2, all.Series.Count, "every server: one point per bucket");
			LogAssert.IsTrue(all.Series[0].BucketUtc < all.Series[1].BucketUtc, "oldest first");
			LogAssert.AreEqual(5500.0, all.Series[0].UdpSentBps, "summed across tiers");

			var scene = Sample(ServerBandwidthKind.Scene);
			LogAssert.AreEqual(100.0, scene.Series.Last().UdpSentBps, "one tier: only its points");

			var one = Sample(ServerBandwidthKind.World, "w1");
			LogAssert.AreEqual("8,9", string.Join(",", one.Series.Select(p => p.UdpSentBps)), "one server: its own points, ordered");
		}

		[Test]
		public void NothingRecorded_IsAnEmptyReport_NotZeros()
		{
			var report = ServerBandwidthReportBuilder.Build(Now, ServerBandwidthRange.Month,
				Array.Empty<ServerBandwidthWindowRow>(), Array.Empty<ServerBandwidthPointRow>(), Array.Empty<ServerBandwidthPointRow>(),
				Array.Empty<ServerBandwidthKindPointRow>(), 0, null, null);
			LogAssert.AreEqual(0, report.Servers.Count, "no servers");
			LogAssert.AreEqual(0, report.Tiers.Count, "no tiers");
			LogAssert.IsNull(report.Total.Current, "no current total");
			LogAssert.IsNull(report.Total.Last30Days, "no 30-day total");
			LogAssert.IsNull(report.Total.Peak, "no peak");
			LogAssert.AreEqual(3600, report.ResolutionSeconds, "the month is charted by the hour");
			LogAssert.AreEqual(ServerBandwidthMath.MonthWindowStart(Now), report.RangeStartUtc, "from whole hours 30 days back");
		}
	}
}
