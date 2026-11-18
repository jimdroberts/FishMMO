using System;
using System.Globalization;
using System.Threading;
using FishMMO.Client;
using FishMMO.Shared;
using FishNet.Transporting.WebTransport;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The rules between the transport's traffic counters and the network statistics overlay's
	/// text: units, rounding, what "unknown" looks like, which badge a row wears, what its tooltip
	/// admits, and how the sampler smooths and remembers.
	/// </summary>
	/// <remarks>
	/// Every case is a fabricated snapshot with hand-computed expectations, so nothing here needs
	/// a connection, a native library or a panel. The mounted panel is covered by
	/// <see cref="NetworkStatsPanelTests"/>.
	/// </remarks>
	[TestFixture]
	public class NetworkStatsPresentationTests
	{
		private const string Dash = NetworkStatsPresentation.Unknown;

		// ── Snapshot builders ────────────────────────────────────────────────────────────────

		/// <summary>A desktop snapshot with every layer measured and a fresh connection reading.</summary>
		internal static TransportTrafficSnapshot Native(double time, long appRecv, long appSent,
			long quicRecv, long quicSent, long datagramsRecv, long datagramsSent)
		{
			TransportTrafficSnapshot s = default;
			s.TimestampSeconds = time;
			s.Backend = TransportTrafficBackend.Native;
			s.AppRecvUnreliableBytes = appRecv;
			s.AppRecvUnreliableMessages = appRecv / 100;
			s.AppSentUnreliableBytes = appSent;
			s.AppSentUnreliableMessages = appSent / 100;
			s.QuicMeasure = TrafficMeasure.Measured;
			s.QuicRecvBytes = quicRecv;
			s.QuicSentBytes = quicSent;
			s.DatagramMeasure = TrafficMeasure.Measured;
			s.UdpRecvDatagrams = datagramsRecv;
			s.UdpSentDatagrams = datagramsSent;
			s.SessionsOpened = 1;
			s.SessionsActive = 1;
			s.Process = TransportProcessCounters.Unknown;
			s.Connection = new TransportConnectionStats
			{
				IsValid = true,
				Measure = TrafficMeasure.Measured,
				SampledAtSeconds = time - 0.5,
				RttMs = 38.4,
				MinRttMs = 31.2,
				MaxRttMs = 112.0,
				RttVarianceMs = 4.1,
				PathMtu = 1472,
				CongestionWindowBytes = 61440,
				CongestionEvents = 0,
				SentPackets = 1000,
				RecvPackets = 1500,
				SentBytes = quicSent,
				RecvBytes = quicRecv,
				LostPackets = 2,
				SpuriousLostPackets = 0,
				RecvDroppedPackets = 0,
				RecvReorderedPackets = 0,
			};
			return s;
		}

		/// <summary>A desktop snapshot before the native library has started: QUIC unknown.</summary>
		internal static TransportTrafficSnapshot NativeNotStarted(double time, long appRecv = 0, long appSent = 0)
		{
			TransportTrafficSnapshot s = default;
			s.TimestampSeconds = time;
			s.Backend = TransportTrafficBackend.Native;
			s.AppRecvUnreliableBytes = appRecv;
			s.AppSentUnreliableBytes = appSent;
			s.QuicMeasure = TrafficMeasure.Unavailable;
			s.QuicRecvBytes = -1;
			s.QuicSentBytes = -1;
			s.DatagramMeasure = TrafficMeasure.Unavailable;
			s.UdpRecvDatagrams = -1;
			s.UdpSentDatagrams = -1;
			s.Process = TransportProcessCounters.Unknown;
			s.Connection = TransportConnectionStats.Unknown;
			return s;
		}

		/// <summary>A browser snapshot mapped by the transport's own rule: Chrome (no figures) or Firefox (bytes and RTT).</summary>
		internal static TransportTrafficSnapshot Browser(double time, long appRecv, long appSent, bool firefox)
		{
			TransportTrafficSnapshot s = default;
			s.TimestampSeconds = time;
			s.Backend = TransportTrafficBackend.Browser;
			s.AppRecvUnreliableBytes = appRecv;
			s.AppRecvUnreliableMessages = appRecv / 100;
			s.AppSentUnreliableBytes = appSent;
			s.AppSentUnreliableMessages = appSent / 100;
			s.SessionsOpened = 1;
			s.SessionsActive = 1;

			double[] values = null;
			int mask = 0;
			if (firefox)
			{
				values = new double[TransportTrafficMath.BrowserStats.Count];
				values[TransportTrafficMath.BrowserStats.BytesSent] = appSent * 1.2;
				values[TransportTrafficMath.BrowserStats.BytesReceived] = appRecv * 1.1;
				values[TransportTrafficMath.BrowserStats.SmoothedRttMs] = 44.0;
				mask = (1 << TransportTrafficMath.BrowserStats.BytesSent)
					| (1 << TransportTrafficMath.BrowserStats.BytesReceived)
					| (1 << TransportTrafficMath.BrowserStats.SmoothedRttMs)
					| TransportTrafficMath.BrowserStats.LiveSampleBit;
			}
			TransportTrafficMath.ApplyBrowserStats(ref s, values, mask, time);
			return s;
		}

		private static NetworkStatsReadout ReadoutOf(params TransportTrafficSnapshot[] snapshots)
		{
			NetworkStatsSampler sampler = new NetworkStatsSampler();
			foreach (TransportTrafficSnapshot s in snapshots)
			{
				sampler.Add(in s);
			}
			return sampler.Readout();
		}

		/// <summary>Two desktop snapshots a second apart, with round per-second deltas.</summary>
		private static NetworkStatsReadout SteadyNative()
		{
			return ReadoutOf(
				Native(100.0, 1_000_000, 200_000, 1_100_000, 250_000, 50_000, 30_000),
				Native(101.0, 1_040_000, 208_000, 1_144_000, 260_000, 50_060, 30_040));
		}

		// ── Units and rounding ───────────────────────────────────────────────────────────────

		[Test]
		public void ByteRates_UseDecimalUnits_WithThreeSignificantFigures()
		{
			LogAssert.AreEqual("0 B/s", NetworkStatsPresentation.ByteRate(0), "a measured zero is still a zero");
			LogAssert.AreEqual("999 B/s", NetworkStatsPresentation.ByteRate(999.4));
			LogAssert.AreEqual("1.00 kB/s", NetworkStatsPresentation.ByteRate(999.6), "the unit steps up before rounding would print 1000");
			LogAssert.AreEqual("43.1 kB/s", NetworkStatsPresentation.ByteRate(43_100));
			LogAssert.AreEqual("123 kB/s", NetworkStatsPresentation.ByteRate(123_456));
			LogAssert.AreEqual("1.25 MB/s", NetworkStatsPresentation.ByteRate(1_250_000));
			LogAssert.AreEqual("1.02 kB", NetworkStatsPresentation.Bytes(1024), "a kilobyte is a thousand bytes here, not 1024");
			LogAssert.AreEqual("12.4 MB", NetworkStatsPresentation.Bytes(12_400_000));
			LogAssert.AreEqual("0 B", NetworkStatsPresentation.Bytes(0));
		}

		[Test]
		public void BitRates_AreExactlyEightTimesTheByteRate()
		{
			/* Decimal on both sides is what makes this exact: 1 MB/s is 8 Mbps, the way an
			 * internet plan counts. Binary kilobytes would put the two five per cent apart. */
			LogAssert.AreEqual("8.00 Mbps", NetworkStatsPresentation.BitRate(1_000_000));
			LogAssert.AreEqual("345 kbps", NetworkStatsPresentation.BitRate(43_100));
			LogAssert.AreEqual("1.00 Mbps", NetworkStatsPresentation.BitRate(125_000));
			LogAssert.AreEqual("800 bps", NetworkStatsPresentation.BitRate(100));
		}

		[Test]
		public void UnknownValues_ReadAsADash_NeverAsZero()
		{
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.ByteRate(double.NaN), "NaN rate");
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.ByteRate(-1), "negative rate");
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.ByteRate(double.PositiveInfinity), "infinite rate");
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.BitRate(double.NaN), "NaN bit rate");
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.Bytes(-1), "the transport's -1 total");
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.PerSecond(double.NaN), "datagrams");
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.Ratio(double.NaN), "overhead");
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.Milliseconds(double.NaN), "round trip");
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.Percent(double.NaN), "loss");
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.Mtu(-1), "path MTU -1");
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.Mtu(0), "path MTU 0");
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.Count(-1), "count");
		}

		[Test]
		public void TheOtherFigures_ReadTheWayAPersonWouldSayThem()
		{
			LogAssert.AreEqual("2.5/s", NetworkStatsPresentation.PerSecond(2.5));
			LogAssert.AreEqual("62/s", NetworkStatsPresentation.PerSecond(62.4));
			LogAssert.AreEqual("1.50k/s", NetworkStatsPresentation.PerSecond(1500));
			LogAssert.AreEqual("×1.13", NetworkStatsPresentation.Ratio(1.1349));
			LogAssert.AreEqual("×12.3", NetworkStatsPresentation.Ratio(12.34));
			LogAssert.AreEqual("38 ms", NetworkStatsPresentation.Milliseconds(38.4));
			LogAssert.AreEqual("4.3 ms", NetworkStatsPresentation.Milliseconds(4.26));
			LogAssert.AreEqual("0.22%", NetworkStatsPresentation.Percent(0.0022));
			LogAssert.AreEqual("1.5%", NetworkStatsPresentation.Percent(0.015));
			LogAssert.AreEqual("12%", NetworkStatsPresentation.Percent(0.12));
			LogAssert.AreEqual("1472 B", NetworkStatsPresentation.Mtu(1472));
			LogAssert.AreEqual("48,210", NetworkStatsPresentation.Count(48210));
		}

		[Test]
		public void Formatting_IgnoresTheMachinesLocale()
		{
			CultureInfo previous = Thread.CurrentThread.CurrentCulture;
			try
			{
				Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
				LogAssert.AreEqual("43.1 kB/s", NetworkStatsPresentation.ByteRate(43_100), "a comma-decimal machine still reads a point");
				LogAssert.AreEqual("×1.13", NetworkStatsPresentation.Ratio(1.1349));
			}
			finally
			{
				Thread.CurrentThread.CurrentCulture = previous;
			}
		}

		// ── Badges and figures ───────────────────────────────────────────────────────────────

		[Test]
		public void EveryMeasure_HasItsOwnWordAndClass()
		{
			var words = new System.Collections.Generic.HashSet<string>();
			var classes = new System.Collections.Generic.HashSet<string>();
			foreach (TrafficMeasure measure in Enum.GetValues(typeof(TrafficMeasure)))
			{
				words.Add(NetworkStatsPresentation.BadgeText(measure));
				classes.Add(NetworkStatsPresentation.BadgeClass(measure));
				LogAssert.IsTrue(Array.IndexOf(NetworkStatsPresentation.BadgeClasses, NetworkStatsPresentation.BadgeClass(measure)) >= 0,
					$"{measure}'s class must be one the refresh clears");
			}
			int count = Enum.GetValues(typeof(TrafficMeasure)).Length;
			LogAssert.AreEqual(count, words.Count, "no two measures may read the same");
			LogAssert.AreEqual(count, classes.Count, "no two measures may look the same");
			LogAssert.AreEqual("ESTIMATE", NetworkStatsPresentation.BadgeText(TrafficMeasure.Estimated));
			LogAssert.AreEqual("MEASURED", NetworkStatsPresentation.BadgeText(TrafficMeasure.Measured));
		}

		[Test]
		public void BeforeTheFirstSample_EveryRowIsPending_AndShowsNoNumber()
		{
			NetworkStatsReadout empty = NetworkStatsReadout.Empty;
			foreach (NetworkStatsRow row in Enum.GetValues(typeof(NetworkStatsRow)))
			{
				NetworkStatsFigures f = NetworkStatsPresentation.Describe(row, in empty);
				LogAssert.IsTrue(f.Pending, $"{row} is pending");
				LogAssert.AreEqual(Dash, f.Down, $"{row} down");
				LogAssert.AreEqual(Dash, f.Up, $"{row} up");
			}
		}

		[Test]
		public void OneSample_GivesTotals_ButNoRates()
		{
			NetworkStatsReadout one = ReadoutOf(Native(100.0, 1_000_000, 200_000, 1_100_000, 250_000, 50_000, 30_000));
			NetworkStatsFigures app = NetworkStatsPresentation.Describe(NetworkStatsRow.App, in one);
			LogAssert.IsFalse(one.HasRates, "one snapshot is no interval");
			LogAssert.AreEqual(Dash, app.Down, "no rate yet");
			LogAssert.AreEqual("1.00 MB", app.DownTotal, "but the total since launch is known");
			LogAssert.AreEqual("200 kB", app.UpTotal);
		}

		[Test]
		public void ASteadyDesktopClient_MeasuresTheTransport_AndEstimatesOnlyTheWire()
		{
			NetworkStatsReadout r = SteadyNative();
			LogAssert.IsTrue(r.HasRates, "two snapshots a second apart are an interval");

			NetworkStatsFigures app = NetworkStatsPresentation.Describe(NetworkStatsRow.App, in r);
			LogAssert.AreEqual(TrafficMeasure.Measured, app.Measure, "app");
			LogAssert.AreEqual("40.0 kB/s", app.Down);
			LogAssert.AreEqual("8.00 kB/s", app.Up);

			NetworkStatsFigures quic = NetworkStatsPresentation.Describe(NetworkStatsRow.Quic, in r);
			LogAssert.AreEqual(TrafficMeasure.Measured, quic.Measure, "msquic counts the UDP payload");
			LogAssert.AreEqual("44.0 kB/s", quic.Down);
			LogAssert.AreEqual("10.0 kB/s", quic.Up);

			/* 44,000 + 60 datagrams × 28 = 45,680; 10,000 + 40 × 28 = 11,120. */
			NetworkStatsFigures wire = NetworkStatsPresentation.Describe(NetworkStatsRow.Wire, in r);
			LogAssert.AreEqual(TrafficMeasure.Estimated, wire.Measure, "no process can count IP headers");
			LogAssert.AreEqual("45.7 kB/s", wire.Down);
			LogAssert.AreEqual("11.1 kB/s", wire.Up);

			NetworkStatsFigures datagrams = NetworkStatsPresentation.Describe(NetworkStatsRow.Datagrams, in r);
			LogAssert.AreEqual(TrafficMeasure.Measured, datagrams.Measure, "datagrams");
			LogAssert.AreEqual("60/s", datagrams.Down);
			LogAssert.AreEqual("40/s", datagrams.Up);
			LogAssert.IsNull(datagrams.DownTotal, "the datagram row has no total line");

			NetworkStatsFigures overhead = NetworkStatsPresentation.Describe(NetworkStatsRow.Overhead, in r);
			LogAssert.AreEqual(TrafficMeasure.Estimated, overhead.Measure, "divided from an estimate");
			LogAssert.AreEqual("×1.14", overhead.Down, "45,680 / 40,000");
			LogAssert.AreEqual("×1.39", overhead.Up, "11,120 / 8,000");

			NetworkStatsFigures link = NetworkStatsPresentation.Describe(NetworkStatsRow.Link, in r);
			LogAssert.AreEqual(TrafficMeasure.Measured, link.Measure, "link");
			LogAssert.AreEqual("38 ms", link.Down, "round trip");
			LogAssert.AreEqual("0.20%", link.Up, "2 lost of 1000 sent");
			LogAssert.AreEqual("1472 B", link.DownTotal, "path MTU");
			LogAssert.AreEqual("61.4 kB", link.UpTotal, "congestion window");

			NetworkStatsPresentation.Headline(in r, out string down, out string up);
			LogAssert.AreEqual("365 kbps", down, "the headline is the wire rate in bits");
			LogAssert.AreEqual("89.0 kbps", up);
			LogAssert.AreEqual("NATIVE QUIC", NetworkStatsPresentation.BackendCaption(in r));
		}

		[Test]
		public void ADesktopClientThatHasNotConnected_ShowsDashesBelowTheApp_NeverZero()
		{
			NetworkStatsReadout r = ReadoutOf(NativeNotStarted(10.0), NativeNotStarted(11.0));

			NetworkStatsFigures app = NetworkStatsPresentation.Describe(NetworkStatsRow.App, in r);
			LogAssert.AreEqual("0 B/s", app.Down, "the app layer is counted, and it really is zero");

			foreach (NetworkStatsRow row in new[] { NetworkStatsRow.Quic, NetworkStatsRow.Wire, NetworkStatsRow.Datagrams, NetworkStatsRow.Overhead, NetworkStatsRow.Link })
			{
				NetworkStatsFigures f = NetworkStatsPresentation.Describe(row, in r);
				LogAssert.AreEqual(TrafficMeasure.Unavailable, f.Measure, $"{row} is unavailable");
				LogAssert.AreEqual(Dash, f.Down, $"{row} down reads unknown");
				LogAssert.AreEqual(Dash, f.Up, $"{row} up reads unknown");
				LogAssert.IsFalse(f.Pending, $"{row} is known to be unknown, not waiting");
			}
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.Describe(NetworkStatsRow.Quic, in r).DownTotal, "no total either");

			NetworkStatsPresentation.Headline(in r, out string down, out string _);
			LogAssert.AreEqual(Dash, down, "the headline too");
		}

		[Test]
		public void AChromeClient_EstimatesTheTransport_AndKnowsNothingOfTheLink()
		{
			NetworkStatsReadout r = ReadoutOf(Browser(10.0, 100_000, 20_000, false), Browser(11.0, 120_000, 24_000, false));

			LogAssert.AreEqual(TrafficMeasure.Estimated, NetworkStatsPresentation.RowMeasure(NetworkStatsRow.Quic, in r), "modelled, not counted");
			LogAssert.AreEqual(TrafficMeasure.Estimated, NetworkStatsPresentation.RowMeasure(NetworkStatsRow.Datagrams, in r), "datagrams");
			LogAssert.AreEqual(TrafficMeasure.Estimated, NetworkStatsPresentation.RowMeasure(NetworkStatsRow.Wire, in r), "wire");
			LogAssert.AreEqual(TrafficMeasure.Unavailable, NetworkStatsPresentation.RowMeasure(NetworkStatsRow.Link, in r), "Chrome reports no RTT");
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.Describe(NetworkStatsRow.Link, in r).Down, "so the RTT is a dash");
			LogAssert.AreNotEqual(Dash, NetworkStatsPresentation.Describe(NetworkStatsRow.Quic, in r).Down, "an estimate is still a number");
			LogAssert.AreEqual("BROWSER", NetworkStatsPresentation.BackendCaption(in r));
		}

		[Test]
		public void AFirefoxClient_ReportsTheTransport_AndOnlyTheRoundTripOfTheLink()
		{
			NetworkStatsReadout r = ReadoutOf(Browser(10.0, 100_000, 20_000, true), Browser(11.0, 120_000, 24_000, true));

			LogAssert.AreEqual(TrafficMeasure.BrowserReported, NetworkStatsPresentation.RowMeasure(NetworkStatsRow.Quic, in r), "bytes from getStats()");
			LogAssert.AreEqual(TrafficMeasure.BrowserReported, NetworkStatsPresentation.RowMeasure(NetworkStatsRow.Link, in r), "RTT from getStats()");
			NetworkStatsFigures link = NetworkStatsPresentation.Describe(NetworkStatsRow.Link, in r);
			LogAssert.AreEqual("44 ms", link.Down, "the round trip the browser gave");
			LogAssert.AreEqual(Dash, link.Up, "loss it did not");
			LogAssert.AreEqual(Dash, link.DownTotal, "nor the path MTU");
			LogAssert.AreEqual(Dash, link.UpTotal, "nor the congestion window");
		}

		[Test]
		public void AStaleConnectionReading_IsNotPresentedAsCurrent()
		{
			TransportTrafficSnapshot a = Native(100.0, 0, 0, 0, 0, 0, 0);
			TransportTrafficSnapshot b = Native(101.0, 0, 0, 0, 0, 0, 0);
			b.Connection.SampledAtSeconds = 101.0 - NetworkStatsPresentation.ConnectionStaleSeconds - 1.0;
			NetworkStatsReadout r = ReadoutOf(a, b);

			LogAssert.AreEqual(TrafficMeasure.Unavailable, NetworkStatsPresentation.RowMeasure(NetworkStatsRow.Link, in r), "stale");
			LogAssert.AreEqual(Dash, NetworkStatsPresentation.Describe(NetworkStatsRow.Link, in r).Down, "a six-second-old RTT is not the RTT");
		}

		// ── Tooltips ─────────────────────────────────────────────────────────────────────────

		private static TooltipContent Tooltip(NetworkStatsRow row, in NetworkStatsReadout readout)
		{
			TooltipContent content = new TooltipContent();
			NetworkStatsPresentation.BuildTooltip(content, row, in readout);
			return content;
		}

		private static string Text(TooltipContent content) => content.ToRichText();

		[Test]
		public void EveryRow_ExplainsItself_InEveryState()
		{
			NetworkStatsReadout[] states =
			{
				NetworkStatsReadout.Empty,
				SteadyNative(),
				ReadoutOf(NativeNotStarted(10.0), NativeNotStarted(11.0)),
				ReadoutOf(Browser(10.0, 100_000, 20_000, false), Browser(11.0, 120_000, 24_000, false)),
				ReadoutOf(Browser(10.0, 100_000, 20_000, true), Browser(11.0, 120_000, 24_000, true)),
			};
			foreach (NetworkStatsReadout state in states)
			{
				foreach (NetworkStatsRow row in Enum.GetValues(typeof(NetworkStatsRow)))
				{
					TooltipContent content = Tooltip(row, in state);
					LogAssert.AreEqual(NetworkStatsPresentation.RowTitle(row), content.Title, $"{row} is titled");
					LogAssert.IsTrue(content.Rows.Count >= 3, $"{row} says more than its own name");
				}
			}
		}

		[Test]
		public void TheWireTooltip_SaysWhyItIsAnEstimate_AndThatUploadIsAnUpperBound()
		{
			NetworkStatsReadout r = SteadyNative();
			string text = Text(Tooltip(NetworkStatsRow.Wire, in r));
			LogAssert.IsTrue(text.Contains("28 bytes per datagram"), "the model is stated");
			LogAssert.IsTrue(text.Contains("no program can count them"), "and why it has to be a model");
			LogAssert.IsTrue(text.Contains(NetworkStatsPresentation.SentDatagramCaveat), "msquic's sent-datagram over-count is admitted");
			LogAssert.IsTrue(text.Contains("45.7 kB/s") && text.Contains("365 kbps"), "the rate is given in bytes and in bits");
		}

		[Test]
		public void TheOverCountCaveat_AppearsOnlyWhereTheCountIsMsquics()
		{
			NetworkStatsReadout chrome = ReadoutOf(Browser(10.0, 100_000, 20_000, false), Browser(11.0, 120_000, 24_000, false));
			string wire = Text(Tooltip(NetworkStatsRow.Wire, in chrome));
			LogAssert.IsFalse(wire.Contains(NetworkStatsPresentation.SentDatagramCaveat), "a browser's count is not msquic's");
			LogAssert.IsTrue(wire.Contains("estimate of an estimate"), "and an estimated count is called what it is");
		}

		[Test]
		public void TheQuicTooltip_NamesItsSource_ForEachMeasure()
		{
			NetworkStatsReadout native = SteadyNative();
			NetworkStatsReadout chrome = ReadoutOf(Browser(10.0, 100_000, 20_000, false), Browser(11.0, 120_000, 24_000, false));
			NetworkStatsReadout firefox = ReadoutOf(Browser(10.0, 100_000, 20_000, true), Browser(11.0, 120_000, 24_000, true));
			NetworkStatsReadout notStarted = ReadoutOf(NativeNotStarted(10.0), NativeNotStarted(11.0));

			LogAssert.IsTrue(Text(Tooltip(NetworkStatsRow.Quic, in native)).Contains("counted by msquic"), "measured names msquic");
			LogAssert.IsTrue(Text(Tooltip(NetworkStatsRow.Quic, in chrome)).Contains("modelled"), "estimated says it is modelled");
			LogAssert.IsTrue(Text(Tooltip(NetworkStatsRow.Quic, in firefox)).Contains("getStats()"), "browser-reported names the API");
			LogAssert.IsTrue(Text(Tooltip(NetworkStatsRow.Quic, in notStarted)).Contains("unknown, not zero"), "unavailable says it is not zero");
		}

		// ── Graph ────────────────────────────────────────────────────────────────────────────

		[Test]
		public void TheGraphScale_IsARoundNumber_AboveThePeak_WithAFloor()
		{
			LogAssert.AreEqual(50_000.0, NetworkStatsPresentation.GraphScale(43_700), "5 x 10^4");
			LogAssert.AreEqual(200_000.0, NetworkStatsPresentation.GraphScale(120_000), "2 x 10^5");
			LogAssert.AreEqual(1_000.0, NetworkStatsPresentation.GraphScale(1_000), "an exact step is its own scale");
			LogAssert.AreEqual(2_000.0, NetworkStatsPresentation.GraphScale(1_001), "just above steps up");
			LogAssert.AreEqual(1_000.0, NetworkStatsPresentation.GraphScale(300), "idle acknowledgements do not fill the graph");
			LogAssert.AreEqual(1_000.0, NetworkStatsPresentation.GraphScale(double.NaN), "an unknown peak uses the floor");
		}

		[Test]
		public void AnUnknownGraphPoint_IsAGap_NotTheBottomOfTheGraph()
		{
			LogAssert.IsTrue(float.IsNaN(NetworkStatsPresentation.GraphY(double.NaN, 1000, 26f)), "NaN is a gap");
			LogAssert.IsTrue(float.IsNaN(NetworkStatsPresentation.GraphY(-1, 1000, 26f)), "-1 is a gap");
			LogAssert.AreEqual(26f, NetworkStatsPresentation.GraphY(0, 1000, 26f), "a measured zero sits on the floor");
			LogAssert.AreEqual(0f, NetworkStatsPresentation.GraphY(1000, 1000, 26f), "the scale is the top");
			LogAssert.AreEqual(13f, NetworkStatsPresentation.GraphY(500, 1000, 26f), "halfway");
			LogAssert.AreEqual(0f, NetworkStatsPresentation.GraphY(5000, 1000, 26f), "above the scale is clamped to the top");
		}

		// ── Sampler ──────────────────────────────────────────────────────────────────────────

		[Test]
		public void TheSmoothedRate_SpansTheOldestSnapshotInsideFiveSeconds()
		{
			/* One kilobyte a second, then a 5 kB spike in the sixth second. The one-second rate at
			 * the end is 1 kB/s; the five-second window from t=2 to t=7 carries the spike:
			 * (12,000 - 2,000) / 5 = 2,000. */
			NetworkStatsSampler sampler = new NetworkStatsSampler();
			long[] app = { 0, 1000, 2000, 3000, 4000, 5000, 11000, 12000 };
			for (int t = 0; t < app.Length; ++t)
			{
				TransportTrafficSnapshot s = Native(t, app[t], 0, app[t], 0, t, t);
				sampler.Add(in s);
			}

			LogAssert.AreEqual(NetworkStatsSampler.SnapshotCapacity, sampler.Count, "the window holds six snapshots");
			LogAssert.IsTrue(sampler.TryGetSmoothedRates(out TransportTrafficRates rates), "rates exist");
			LogAssert.IsTrue(Math.Abs(rates.Seconds - 5.0) < 1e-9, $"a five-second window, got {rates.Seconds}");
			LogAssert.IsTrue(Math.Abs(rates.AppRecvBytesPerSecond - 2000.0) < 1e-9, $"expected 2000 B/s, got {rates.AppRecvBytesPerSecond}");
		}

		[Test]
		public void AFrameHitchLongerThanTheWindow_StillGivesARate()
		{
			NetworkStatsSampler sampler = new NetworkStatsSampler();
			TransportTrafficSnapshot a = Native(100.0, 0, 0, 0, 0, 0, 0);
			TransportTrafficSnapshot b = Native(108.0, 8000, 0, 8000, 0, 8, 8);
			sampler.Add(in a);
			sampler.Add(in b);
			LogAssert.IsTrue(sampler.TryGetSmoothedRates(out TransportTrafficRates rates), "the previous snapshot is used");
			LogAssert.IsTrue(Math.Abs(rates.AppRecvBytesPerSecond - 1000.0) < 1e-9, "over the eight seconds it spans");
		}

		[Test]
		public void AChangeOfDefinition_RestartsTheWindow_SoTheLayerIsUnknownForOneSampleOnly()
		{
			NetworkStatsSampler sampler = new NetworkStatsSampler();
			TransportTrafficSnapshot s1 = NativeNotStarted(1.0), s2 = NativeNotStarted(2.0), s3 = NativeNotStarted(3.0);
			sampler.Add(in s1);
			sampler.Add(in s2);
			sampler.Add(in s3);
			LogAssert.AreEqual(3, sampler.Count, "three comparable snapshots");

			// The native library starts: the QUIC layer turns from unknown to measured.
			TransportTrafficSnapshot s4 = Native(4.0, 0, 0, 5000, 2000, 5, 3);
			sampler.Add(in s4);
			LogAssert.AreEqual(1, sampler.Count, "the window restarts at the first measured snapshot");
			LogAssert.IsFalse(sampler.Readout().HasRates, "one snapshot is no interval");

			TransportTrafficSnapshot s5 = Native(5.0, 0, 0, 6000, 2500, 6, 4);
			sampler.Add(in s5);
			NetworkStatsReadout r = sampler.Readout();
			LogAssert.IsTrue(r.HasRates, "a second later the layer has a rate");
			LogAssert.AreEqual(TrafficMeasure.Measured, r.Rates.QuicMeasure, "and it is measured, not unknown for the whole window");
		}

		[Test]
		public void SnapshotsThatCannotBelongToOneProcess_RestartTheWindow_AndLeaveAGap()
		{
			NetworkStatsSampler sampler = new NetworkStatsSampler();
			TransportTrafficSnapshot a = Native(1.0, 5000, 0, 5000, 0, 5, 5);
			TransportTrafficSnapshot b = Native(2.0, 6000, 0, 6000, 0, 6, 6);
			TransportTrafficSnapshot c = Native(3.0, 100, 0, 100, 0, 1, 1); // a counter went backwards
			sampler.Add(in a);
			sampler.Add(in b);
			sampler.Add(in c);

			LogAssert.AreEqual(1, sampler.Count, "restarted");
			LogAssert.AreEqual(2, sampler.HistoryCount, "one point per second after the first");
			sampler.GetHistory(1, out double down, out double _);
			LogAssert.IsTrue(double.IsNaN(down), "the impossible second is a gap in the graph, not a dip");
			sampler.GetHistory(0, out double first, out double _);
			LogAssert.IsFalse(double.IsNaN(first), "the good second before it is kept");
		}

		[Test]
		public void TheGraph_RemembersAMinute_OldestFirst()
		{
			NetworkStatsSampler sampler = new NetworkStatsSampler();
			for (int t = 0; t <= 75; ++t)
			{
				// Wire down per second = t (QUIC bytes grow by t each second, no datagrams).
				long quic = t * (t + 1) / 2;
				TransportTrafficSnapshot s = Native(t, 0, 0, quic, 0, 0, 0);
				sampler.Add(in s);
			}

			LogAssert.AreEqual(NetworkStatsSampler.HistoryCapacity, sampler.HistoryCount, "a minute of points");
			sampler.GetHistory(0, out double oldest, out double _);
			sampler.GetHistory(NetworkStatsSampler.HistoryCapacity - 1, out double newest, out double _);
			LogAssert.AreEqual(16.0, oldest, "points 1..15 have rolled off");
			LogAssert.AreEqual(75.0, newest, "the newest second is last");
			LogAssert.AreEqual(75.0, sampler.HistoryPeak(), "the peak is the largest point held");
		}

		[Test]
		public void TheGraphPeak_IgnoresUnknownSeconds()
		{
			NetworkStatsSampler sampler = new NetworkStatsSampler();
			TransportTrafficSnapshot a = NativeNotStarted(1.0), b = NativeNotStarted(2.0), c = NativeNotStarted(3.0);
			sampler.Add(in a);
			sampler.Add(in b);
			sampler.Add(in c);
			LogAssert.AreEqual(2, sampler.HistoryCount, "two seconds remembered");
			LogAssert.IsTrue(double.IsNaN(sampler.HistoryPeak()), "no known point means no peak, not a peak of zero");
		}

		[Test]
		public void AnEmptySnapshot_IsRefused_AndClearForgetsEverything()
		{
			NetworkStatsSampler sampler = new NetworkStatsSampler();
			TransportTrafficSnapshot none = default;
			LogAssert.IsFalse(sampler.Add(in none), "a default struct carries no backend");
			LogAssert.AreEqual(0, sampler.Count, "and is not kept");

			TransportTrafficSnapshot a = Native(1.0, 0, 0, 0, 0, 0, 0), b = Native(2.0, 10, 0, 10, 0, 1, 1);
			sampler.Add(in a);
			sampler.Add(in b);
			sampler.Clear();
			LogAssert.AreEqual(0, sampler.Count, "window cleared");
			LogAssert.AreEqual(0, sampler.HistoryCount, "graph cleared");
			LogAssert.IsFalse(sampler.Readout().HasSnapshot, "nothing to show");
		}
	}
}
