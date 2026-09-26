using System;
using FishNet.Transporting.WebTransport;
using FishNet.Transporting.WebTransport.Native;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using BrowserStats = FishNet.Transporting.WebTransport.TransportTrafficMath.BrowserStats;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The pure arithmetic behind the network statistics: rates from two snapshots, the IP-level
	/// estimate, the overhead ratio, the WebGL estimate model, and the mapping of native and browser
	/// figures. Every rule here decides whether a number shown to a player or stored for an operator
	/// is right, or honestly labelled when it cannot be.
	/// </summary>
	[TestFixture]
	public class TransportTrafficMathTests
	{
		private const double Epsilon = 1e-9;

		/// <summary>A native snapshot with every layer measured.</summary>
		private static TransportTrafficSnapshot Native(double at, long appSent, long appRecv, long quicSent, long quicRecv,
			long dgramsSent, long dgramsRecv, long sessions = 1)
		{
			return new TransportTrafficSnapshot
			{
				TimestampSeconds = at,
				Backend = TransportTrafficBackend.Native,
				AppSentUnreliableBytes = appSent,
				AppSentUnreliableMessages = appSent / 100,
				AppRecvUnreliableBytes = appRecv,
				AppRecvUnreliableMessages = appRecv / 100,
				QuicMeasure = TrafficMeasure.Measured,
				QuicSentBytes = quicSent,
				QuicRecvBytes = quicRecv,
				DatagramMeasure = TrafficMeasure.Measured,
				UdpSentDatagrams = dgramsSent,
				UdpRecvDatagrams = dgramsRecv,
				SessionsOpened = sessions,
			};
		}

		// ── Varint framing ────────────────────────────────────────────────

		[Test]
		public void VarintLength_FollowsTheQuicBoundaries()
		{
			/* RFC 9000 §16. The framing counter adds this per reliable message, so an off-by-one at
			 * a boundary would miscount every message of that size. */
			LogAssert.AreEqual(1, TransportTrafficMath.VarintLength(0));
			LogAssert.AreEqual(1, TransportTrafficMath.VarintLength(63));
			LogAssert.AreEqual(2, TransportTrafficMath.VarintLength(64));
			LogAssert.AreEqual(2, TransportTrafficMath.VarintLength(16383));
			LogAssert.AreEqual(4, TransportTrafficMath.VarintLength(16384));
			LogAssert.AreEqual(4, TransportTrafficMath.VarintLength(1073741823));
			LogAssert.AreEqual(8, TransportTrafficMath.VarintLength(1073741824));
		}

		// ── Wire layer ────────────────────────────────────────────────────

		[Test]
		public void WireBytes_AddTwentyEightBytesPerDatagram()
		{
			LogAssert.AreEqual(28, TransportTrafficMath.IpV4UdpHeaderBytes, "IPv4 20 + UDP 8");
			LogAssert.AreEqual(1000L + 10 * 28, TransportTrafficMath.WireBytes(1000, 10, TrafficMeasure.Measured, TrafficMeasure.Measured));
		}

		[Test]
		public void WireBytes_AreUnknownWhenEitherInputIs()
		{
			LogAssert.AreEqual(-1L, TransportTrafficMath.WireBytes(1000, 10, TrafficMeasure.Unavailable, TrafficMeasure.Measured));
			LogAssert.AreEqual(-1L, TransportTrafficMath.WireBytes(1000, 10, TrafficMeasure.Measured, TrafficMeasure.Unavailable));
			LogAssert.AreEqual(-1L, TransportTrafficMath.WireBytes(-1, 10, TrafficMeasure.Measured, TrafficMeasure.Measured));
			LogAssert.AreEqual(-1L, TransportTrafficMath.WireBytes(1000, -1, TrafficMeasure.Measured, TrafficMeasure.Measured));
		}

		[Test]
		public void WireMeasure_IsAlwaysAnEstimate_OrUnavailable()
		{
			/* No user-space counter sees an IP or UDP header, so even two measured inputs make an
			 * estimate: showing the wire figure as Measured would claim what the process cannot see. */
			TrafficMeasure[] all = { TrafficMeasure.Unavailable, TrafficMeasure.Measured, TrafficMeasure.BrowserReported, TrafficMeasure.Estimated };
			foreach (TrafficMeasure quic in all)
			{
				foreach (TrafficMeasure dgrams in all)
				{
					TrafficMeasure expected = quic == TrafficMeasure.Unavailable || dgrams == TrafficMeasure.Unavailable
						? TrafficMeasure.Unavailable
						: TrafficMeasure.Estimated;
					LogAssert.AreEqual(expected, TransportTrafficMath.WireMeasure(quic, dgrams), $"{quic} × {dgrams}");
				}
			}
		}

		[Test]
		public void OverheadRatio_IsWireOverApp_AndNaNWhenUndefined()
		{
			LogAssert.IsTrue(Math.Abs(TransportTrafficMath.OverheadRatio(1280, 1000) - 1.28) < Epsilon);
			LogAssert.IsTrue(double.IsNaN(TransportTrafficMath.OverheadRatio(1280, 0)), "no app bytes: no ratio, not infinity or 0");
			LogAssert.IsTrue(double.IsNaN(TransportTrafficMath.OverheadRatio(-1, 1000)), "unknown wire: no ratio");
		}

		// ── Rates ─────────────────────────────────────────────────────────

		[Test]
		public void Rates_DifferenceTwoSnapshots_PerLayerAndDirection()
		{
			TransportTrafficSnapshot a = Native(10.0, 1000, 5000, 1600, 6500, 20, 60, sessions: 1);
			TransportTrafficSnapshot b = Native(12.0, 3000, 9000, 4800, 11700, 60, 110, sessions: 2);

			LogAssert.IsTrue(TransportTrafficMath.TryComputeRates(in a, in b, out TransportTrafficRates r));
			LogAssert.IsTrue(Math.Abs(r.Seconds - 2.0) < Epsilon);
			LogAssert.AreEqual(2000L, r.AppSentBytes);
			LogAssert.AreEqual(4000L, r.AppRecvBytes);
			LogAssert.AreEqual(TrafficMeasure.Measured, r.QuicMeasure);
			LogAssert.AreEqual(3200L, r.QuicSentBytes);
			LogAssert.AreEqual(5200L, r.QuicRecvBytes);
			LogAssert.AreEqual(40L, r.UdpSentDatagrams);
			LogAssert.AreEqual(50L, r.UdpRecvDatagrams);
			LogAssert.AreEqual(3200L + 40 * 28, r.WireSentBytes);
			LogAssert.AreEqual(5200L + 50 * 28, r.WireRecvBytes);
			LogAssert.AreEqual(TrafficMeasure.Estimated, r.WireMeasure);
			LogAssert.AreEqual(1L, r.SessionsOpened);

			LogAssert.IsTrue(Math.Abs(r.AppSentBytesPerSecond - 1000.0) < Epsilon);
			LogAssert.IsTrue(Math.Abs(r.QuicRecvBytesPerSecond - 2600.0) < Epsilon);
			LogAssert.IsTrue(Math.Abs(r.WireSentBytesPerSecond - (3200 + 1120) / 2.0) < Epsilon);
			LogAssert.IsTrue(Math.Abs(r.DatagramsRecvPerSecond - 25.0) < Epsilon);
			LogAssert.IsTrue(Math.Abs(r.SentOverheadRatio - (3200.0 + 1120.0) / 2000.0) < Epsilon);
		}

		[Test]
		public void Rates_CarryRefusalsAndHandshakeFailures_OnlyWhenBothEndsHaveThem()
		{
			/* What a server sampler stores beside the bytes: connections refused before a session
			 * and handshakes that failed. Unknown (-1) rather than 0 when the process counters are
			 * missing at either end. */
			TransportTrafficSnapshot a = Native(10.0, 1000, 5000, 1600, 6500, 20, 60);
			TransportTrafficSnapshot b = Native(70.0, 2000, 6000, 1700, 6600, 21, 61);
			a.Process = new TransportProcessCounters { IsValid = true, RefusedByLimits = 5, RefusedForLoad = 1, RefusedNoAlpn = 2, HandshakeFailures = 3 };
			b.Process = new TransportProcessCounters { IsValid = true, RefusedByLimits = 9, RefusedForLoad = 1, RefusedNoAlpn = 4, HandshakeFailures = 10 };

			LogAssert.IsTrue(TransportTrafficMath.TryComputeRates(in a, in b, out TransportTrafficRates r));
			LogAssert.AreEqual(6L, r.ConnectionsRefused, "limits +4, load +0, ALPN +2");
			LogAssert.AreEqual(7L, r.HandshakeFailures);

			a.Process = TransportProcessCounters.Unknown;
			LogAssert.IsTrue(TransportTrafficMath.TryComputeRates(in a, in b, out r));
			LogAssert.AreEqual(-1L, r.ConnectionsRefused);
			LogAssert.AreEqual(-1L, r.HandshakeFailures);
		}

		[Test]
		public void Rates_RefuseAnEmptyOrBackwardInterval()
		{
			TransportTrafficSnapshot a = Native(10.0, 1000, 5000, 1600, 6500, 20, 60);
			LogAssert.IsFalse(TransportTrafficMath.TryComputeRates(in a, in a, out _), "zero interval");
			TransportTrafficSnapshot earlierClock = Native(9.0, 2000, 6000, 1700, 6600, 21, 61);
			LogAssert.IsFalse(TransportTrafficMath.TryComputeRates(in a, in earlierClock, out _), "negative interval");
		}

		[Test]
		public void Rates_RefuseSnapshotsThatCannotShareAProcessLifetime()
		{
			TransportTrafficSnapshot a = Native(10.0, 1000, 5000, 1600, 6500, 20, 60);
			TransportTrafficSnapshot shrunk = Native(11.0, 900, 5000, 1700, 6600, 21, 61);
			LogAssert.IsFalse(TransportTrafficMath.TryComputeRates(in a, in shrunk, out _),
				"an application total that went backwards is a different process (or domain), not negative traffic");

			TransportTrafficSnapshot browser = Native(11.0, 2000, 6000, 1700, 6600, 21, 61);
			browser.Backend = TransportTrafficBackend.Browser;
			LogAssert.IsFalse(TransportTrafficMath.TryComputeRates(in a, in browser, out _), "backends differ");

			LogAssert.IsFalse(TransportTrafficMath.TryComputeRates(default, default, out _), "default snapshots");
		}

		[Test]
		public void Rates_MarkALayerUnknown_WhenItsMeasureChangesMidInterval()
		{
			/* A WebGL client's first getStats() answer arrives mid-session: one end is the estimate,
			 * the other the browser's count. Their difference is meaningless — it must come out as
			 * unknown, never as a spike or a negative rate. The application layer is unaffected. */
			TransportTrafficSnapshot a = Native(10.0, 1000, 5000, 1600, 6500, 20, 60);
			a.Backend = TransportTrafficBackend.Browser;
			a.QuicMeasure = TrafficMeasure.Estimated;
			TransportTrafficSnapshot b = Native(11.0, 2000, 6000, 900, 7000, 30, 70);
			b.Backend = TransportTrafficBackend.Browser;
			b.QuicMeasure = TrafficMeasure.BrowserReported;

			LogAssert.IsTrue(TransportTrafficMath.TryComputeRates(in a, in b, out TransportTrafficRates r));
			LogAssert.AreEqual(TrafficMeasure.Unavailable, r.QuicMeasure);
			LogAssert.AreEqual(-1L, r.QuicSentBytes);
			LogAssert.IsTrue(double.IsNaN(r.QuicSentBytesPerSecond), "unknown shows as NaN, never 0");
			LogAssert.AreEqual(-1L, r.WireSentBytes);
			LogAssert.IsTrue(double.IsNaN(r.SentOverheadRatio));
			LogAssert.AreEqual(1000L, r.AppSentBytes, "the application layer is still measured");
			LogAssert.AreEqual(TrafficMeasure.Measured, r.DatagramMeasure, "an unchanged layer keeps its measure");
		}

		[Test]
		public void Rates_MarkALayerUnknown_WhenEitherEndLacksIt()
		{
			TransportTrafficSnapshot a = Native(10.0, 1000, 5000, -1, -1, -1, -1);
			a.QuicMeasure = TrafficMeasure.Unavailable;
			a.DatagramMeasure = TrafficMeasure.Unavailable;
			TransportTrafficSnapshot b = Native(11.0, 2000, 6000, 1600, 6500, 20, 60);

			LogAssert.IsTrue(TransportTrafficMath.TryComputeRates(in a, in b, out TransportTrafficRates r),
				"the application layer still has a rate before the native library first loads");
			LogAssert.AreEqual(TrafficMeasure.Unavailable, r.QuicMeasure);
			LogAssert.AreEqual(TrafficMeasure.Unavailable, r.DatagramMeasure);
			LogAssert.AreEqual(TrafficMeasure.Unavailable, r.WireMeasure);
			LogAssert.AreEqual(-1L, r.UdpSentDatagrams);
			LogAssert.IsTrue(double.IsNaN(r.DatagramsSentPerSecond));
		}

		[Test]
		public void Rates_MarkALayerUnknown_WhenItsTotalWentBackwards()
		{
			TransportTrafficSnapshot a = Native(10.0, 1000, 5000, 1600, 6500, 20, 60);
			TransportTrafficSnapshot b = Native(11.0, 2000, 6000, 1500, 6600, 21, 61);
			LogAssert.IsTrue(TransportTrafficMath.TryComputeRates(in a, in b, out TransportTrafficRates r));
			LogAssert.AreEqual(TrafficMeasure.Unavailable, r.QuicMeasure, "a counter reset must not read as negative traffic");
			LogAssert.AreEqual(-1L, r.QuicSentBytes);
			LogAssert.AreEqual(-1L, r.QuicRecvBytes);
		}

		// ── WebGL estimate model ──────────────────────────────────────────

		[Test]
		public void BrowserEstimate_IsZeroWithNoTraffic_AndTheHandshakeCostPerSession()
		{
			var none = new TransportTrafficSnapshot();
			TransportTrafficMath.EstimateBrowserQuic(in none, out long sb, out long rb, out long sd, out long rd);
			LogAssert.AreEqual(0L, sb);
			LogAssert.AreEqual(0L, rb);
			LogAssert.AreEqual(0L, sd);
			LogAssert.AreEqual(0L, rd);

			var threeHops = new TransportTrafficSnapshot { SessionsOpened = 3 };
			TransportTrafficMath.EstimateBrowserQuic(in threeHops, out sb, out rb, out sd, out rd);
			LogAssert.AreEqual(3L * (TransportTrafficMath.BrowserHandshakeSentBytes + TransportTrafficMath.BrowserSessionSetupSentBytes), sb);
			LogAssert.AreEqual(3L * (TransportTrafficMath.BrowserHandshakeRecvBytes + TransportTrafficMath.BrowserSessionSetupRecvBytes), rb);
			LogAssert.AreEqual(3L * TransportTrafficMath.BrowserHandshakeSentDatagrams, sd);
			LogAssert.AreEqual(3L * TransportTrafficMath.BrowserHandshakeRecvDatagrams, rd);
		}

		[Test]
		public void BrowserEstimate_FollowsTheModel_ForAWorkedExample()
		{
			/* Sent: 10 reliable messages of 100 B (2-byte prefix each) and 30 datagrams of 50 B.
			 * Received: 5 reliable messages of 1000 B and 60 datagrams of 200 B. No session set-up
			 * (SessionsOpened 0) so only the per-message and per-packet terms remain. */
			var s = new TransportTrafficSnapshot
			{
				AppSentReliableMessages = 10, AppSentReliableBytes = 1000, FramingSentBytes = 20,
				AppSentUnreliableMessages = 30, AppSentUnreliableBytes = 1500,
				AppRecvReliableMessages = 5, AppRecvReliableBytes = 5000, FramingRecvBytes = 10,
				AppRecvUnreliableMessages = 60, AppRecvUnreliableBytes = 12000,
			};
			TransportTrafficMath.EstimateBrowserQuic(in s, out long sentBytes, out long recvBytes, out long sentDgrams, out long recvDgrams);

			long sentData = 30 + Math.Max(10, (1020 + 1199) / 1200);   // 40
			long recvData = 60 + Math.Max(5, (5010 + 1199) / 1200);    // 65
			long sentAcks = (recvData + 1) / 2;                         // 33: this side acknowledges what it received
			long recvAcks = (sentData + 1) / 2;                         // 20
			long sentPackets = Math.Max(sentData, sentAcks);
			long recvPackets = Math.Max(recvData, recvAcks);
			long expectedSent = 2500 + 20 + 30 * TransportTrafficMath.QuarterStreamIdBytes
				+ 40 * TransportTrafficMath.QuicFrameHeaderBytes
				+ sentPackets * TransportTrafficMath.QuicPacketOverheadBytes
				+ sentAcks * TransportTrafficMath.QuicAckFrameBytes;
			long expectedRecv = 17000 + 10 + 60 * TransportTrafficMath.QuarterStreamIdBytes
				+ 65 * TransportTrafficMath.QuicFrameHeaderBytes
				+ recvPackets * TransportTrafficMath.QuicPacketOverheadBytes
				+ recvAcks * TransportTrafficMath.QuicAckFrameBytes;

			LogAssert.AreEqual(expectedSent, sentBytes);
			LogAssert.AreEqual(expectedRecv, recvBytes);
			LogAssert.AreEqual(sentPackets, sentDgrams);
			LogAssert.AreEqual(recvPackets, recvDgrams);
			LogAssert.AreEqual(4035L, sentBytes, "pin of today's constants: 30 B/packet, 3 B/frame, 5 B/ACK");
			LogAssert.AreEqual(19315L, recvBytes, "pin of today's constants");
		}

		[Test]
		public void BrowserEstimate_CountsAckOnlyPackets_ForAClientThatMostlyReceives()
		{
			/* A client that sends little and receives a lot still sends a packet for every second
			 * packet it receives. Leaving that out would make the upstream estimate near zero. */
			var s = new TransportTrafficSnapshot
			{
				AppRecvUnreliableMessages = 1000, AppRecvUnreliableBytes = 200000,
				AppSentUnreliableMessages = 10, AppSentUnreliableBytes = 500,
			};
			TransportTrafficMath.EstimateBrowserQuic(in s, out long sentBytes, out _, out long sentDgrams, out _);
			LogAssert.AreEqual(500L, sentDgrams, "one ACK-carrying packet per two received, not the 10 data packets");
			LogAssert.IsTrue(sentBytes >= 500L * TransportTrafficMath.QuicPacketOverheadBytes,
				"every ACK-only packet pays the per-packet overhead");
		}

		[Test]
		public void BrowserEstimate_NeverDecreases_AsCountersGrow()
		{
			/* The estimate is recomputed from cumulative counters at every capture. If growing any
			 * input could shrink an output, a rate would come out negative. */
			var baseline = new TransportTrafficSnapshot
			{
				AppSentReliableMessages = 50, AppSentReliableBytes = 9000, FramingSentBytes = 100,
				AppSentUnreliableMessages = 200, AppSentUnreliableBytes = 20000,
				AppRecvReliableMessages = 40, AppRecvReliableBytes = 30000, FramingRecvBytes = 80,
				AppRecvUnreliableMessages = 900, AppRecvUnreliableBytes = 150000,
				SessionsOpened = 2,
			};
			TransportTrafficMath.EstimateBrowserQuic(in baseline, out long sb0, out long rb0, out long sd0, out long rd0);

			for (int field = 0; field < 11; field++)
			{
				foreach (long step in new long[] { 1, 7, 1200, 100000 })
				{
					TransportTrafficSnapshot grown = baseline;
					switch (field)
					{
						case 0: grown.AppSentReliableMessages += step; break;
						case 1: grown.AppSentReliableBytes += step; break;
						case 2: grown.FramingSentBytes += step; break;
						case 3: grown.AppSentUnreliableMessages += step; break;
						case 4: grown.AppSentUnreliableBytes += step; break;
						case 5: grown.AppRecvReliableMessages += step; break;
						case 6: grown.AppRecvReliableBytes += step; break;
						case 7: grown.FramingRecvBytes += step; break;
						case 8: grown.AppRecvUnreliableMessages += step; break;
						case 9: grown.AppRecvUnreliableBytes += step; break;
						case 10: grown.SessionsOpened += step; break;
					}
					TransportTrafficMath.EstimateBrowserQuic(in grown, out long sb, out long rb, out long sd, out long rd);
					LogAssert.IsTrue(sb >= sb0 && rb >= rb0 && sd >= sd0 && rd >= rd0,
						$"growing input {field} by {step} must not shrink any estimate");
				}
			}
		}

		// ── Browser getStats() mapping ────────────────────────────────────

		private static double[] BrowserValues()
		{
			var v = new double[BrowserStats.Count];
			for (int i = 0; i < v.Length; i++) v[i] = double.NaN;
			return v;
		}

		private static TransportTrafficSnapshot BrowserApp()
		{
			return new TransportTrafficSnapshot
			{
				Backend = TransportTrafficBackend.Browser,
				TimestampSeconds = 5.0,
				AppSentUnreliableMessages = 100, AppSentUnreliableBytes = 5000,
				AppRecvUnreliableMessages = 100, AppRecvUnreliableBytes = 20000,
				SessionsOpened = 1,
			};
		}

		[Test]
		public void BrowserStats_WithBytesInBothDirections_AreBrowserReported()
		{
			double[] v = BrowserValues();
			v[BrowserStats.BytesSent] = 9000;
			v[BrowserStats.BytesSentOverhead] = 1000;
			v[BrowserStats.BytesReceived] = 30000;
			int mask = (1 << BrowserStats.BytesSent) | (1 << BrowserStats.BytesSentOverhead) | (1 << BrowserStats.BytesReceived);

			TransportTrafficSnapshot s = BrowserApp();
			TransportTrafficMath.ApplyBrowserStats(ref s, v, mask, 5.0);
			LogAssert.AreEqual(TrafficMeasure.BrowserReported, s.QuicMeasure);
			LogAssert.AreEqual(10000L, s.QuicSentBytes, "specification split: payload plus reported overhead");
			LogAssert.AreEqual(30000L, s.QuicRecvBytes);
			LogAssert.AreEqual(TrafficMeasure.Estimated, s.DatagramMeasure, "QUIC packets stand in for datagrams: an estimate");
			LogAssert.IsFalse(s.Process.IsValid, "no msquic in a browser");
		}

		[Test]
		public void BrowserStats_WithoutBothByteCounts_FallBackToTheEstimate()
		{
			/* Chrome's getStats() (behind a flag) has no byte counters at all; a browser that fills
			 * only one direction is no better. Neither may be shown as a measurement. */
			double[] v = BrowserValues();
			v[BrowserStats.BytesSent] = 9000;
			int mask = 1 << BrowserStats.BytesSent;

			TransportTrafficSnapshot s = BrowserApp();
			TransportTrafficMath.ApplyBrowserStats(ref s, v, mask, 5.0);
			TransportTrafficMath.EstimateBrowserQuic(in s, out long estSent, out long estRecv, out long estSentDg, out long estRecvDg);
			LogAssert.AreEqual(TrafficMeasure.Estimated, s.QuicMeasure);
			LogAssert.AreEqual(estSent, s.QuicSentBytes);
			LogAssert.AreEqual(estRecv, s.QuicRecvBytes);
			LogAssert.AreEqual(estSentDg, s.UdpSentDatagrams);
			LogAssert.AreEqual(estRecvDg, s.UdpRecvDatagrams);

			TransportTrafficSnapshot none = BrowserApp();
			TransportTrafficMath.ApplyBrowserStats(ref none, null, 0, 5.0);
			LogAssert.AreEqual(TrafficMeasure.Estimated, none.QuicMeasure, "no getStats() at all");
			LogAssert.IsFalse(none.Connection.IsValid);
			LogAssert.IsTrue(double.IsNaN(none.Connection.RttMs), "unknown RTT is NaN, not 0");
		}

		[Test]
		public void BrowserStats_PacketCountsReplaceTheDatagramModel_WhenPresent()
		{
			double[] v = BrowserValues();
			v[BrowserStats.PacketsSent] = 321;
			v[BrowserStats.PacketsReceived] = 654;
			int mask = (1 << BrowserStats.PacketsSent) | (1 << BrowserStats.PacketsReceived);

			TransportTrafficSnapshot s = BrowserApp();
			TransportTrafficMath.ApplyBrowserStats(ref s, v, mask, 5.0);
			LogAssert.AreEqual(321L, s.UdpSentDatagrams);
			LogAssert.AreEqual(654L, s.UdpRecvDatagrams);
			LogAssert.AreEqual(TrafficMeasure.Estimated, s.DatagramMeasure);
		}

		[Test]
		public void BrowserStats_RttComesFromTheLiveSession()
		{
			double[] v = BrowserValues();
			v[BrowserStats.SmoothedRttMs] = 42.5;
			v[BrowserStats.MinRttMs] = 30;
			int mask = (1 << BrowserStats.SmoothedRttMs) | (1 << BrowserStats.MinRttMs) | BrowserStats.LiveSampleBit;

			TransportTrafficSnapshot s = BrowserApp();
			TransportTrafficMath.ApplyBrowserStats(ref s, v, mask, 7.0);
			LogAssert.IsTrue(s.Connection.IsValid);
			LogAssert.AreEqual(TrafficMeasure.BrowserReported, s.Connection.Measure);
			LogAssert.IsTrue(Math.Abs(s.Connection.RttMs - 42.5) < Epsilon);
			LogAssert.IsTrue(Math.Abs(s.Connection.MinRttMs - 30) < Epsilon);
			LogAssert.IsTrue(double.IsNaN(s.Connection.RttVarianceMs), "a slot the browser left out stays unknown");
			LogAssert.AreEqual(-1, s.Connection.PathMtu, "browsers do not expose the path MTU");
			LogAssert.IsTrue(Math.Abs(s.Connection.SampledAtSeconds - 7.0) < Epsilon);
		}

		// ── Native mapping ────────────────────────────────────────────────

		[Test]
		public void NativeConnectionStats_ConvertUnitsAndDeriveLoss()
		{
			var raw = new WebTransportNative.WtConnectionStats
			{
				Flags = WebTransportNative.WtConnectionStats.HasCongestionWindow | WebTransportNative.WtConnectionStats.HasRttVariance,
				RttUs = 42500,
				MinRttUs = 30000,
				MaxRttUs = 90000,
				RttVarianceUs = 4000,
				PathMtu = 1500,
				CongestionWindow = 12000,
				SendPackets = 1000,
				SendSuspectedLostPackets = 25,
				SendSpuriousLostPackets = 5,
			};
			TransportConnectionStats c = TransportTrafficMath.FromNative(in raw, 3.0);
			LogAssert.IsTrue(c.IsValid);
			LogAssert.AreEqual(TrafficMeasure.Measured, c.Measure);
			LogAssert.IsTrue(Math.Abs(c.RttMs - 42.5) < Epsilon, "microseconds to milliseconds");
			LogAssert.IsTrue(Math.Abs(c.RttVarianceMs - 4.0) < Epsilon);
			LogAssert.AreEqual(1500, c.PathMtu);
			LogAssert.AreEqual(12000L, c.CongestionWindowBytes);
			LogAssert.AreEqual(20L, c.LostPackets, "lost = suspected − spurious");
			LogAssert.IsTrue(Math.Abs(c.LossRatio - 0.02) < Epsilon);
		}

		[Test]
		public void NativeConnectionStats_WithoutTheirFlags_AreUnknownNotZero()
		{
			var raw = new WebTransportNative.WtConnectionStats { RttUs = 1000, CongestionWindow = 0, RttVarianceUs = 0, PathMtu = 0 };
			TransportConnectionStats c = TransportTrafficMath.FromNative(in raw, 3.0);
			LogAssert.AreEqual(-1L, c.CongestionWindowBytes);
			LogAssert.IsTrue(double.IsNaN(c.RttVarianceMs));
			LogAssert.AreEqual(-1, c.PathMtu);
		}

		[Test]
		public void NativeProcessCounters_MapAndSaturate()
		{
			var raw = new WebTransportNative.WtGlobalCounters
			{
				AppSendBytes = 123,
				ConnAppReject = 7,
				ConnHandshakeFail = 3,
				PktsDropped = ulong.MaxValue,
			};
			TransportProcessCounters p = TransportTrafficMath.FromNative(in raw);
			LogAssert.IsTrue(p.IsValid);
			LogAssert.AreEqual(123L, p.QuicAppSentBytes);
			LogAssert.AreEqual(7L, p.RefusedByLimits);
			LogAssert.AreEqual(3L, p.HandshakeFailures);
			LogAssert.AreEqual(long.MaxValue, p.PacketsDropped, "an unsigned total saturates, never wraps negative");
		}

		// ── Capture ───────────────────────────────────────────────────────

		[Test]
		public void Capture_ProducesAConsistentLabelledSnapshot()
		{
			TransportTraffic.Capture(out TransportTrafficSnapshot a);
			TransportTraffic.Capture(out TransportTrafficSnapshot b);

			LogAssert.AreEqual(TransportTrafficBackend.Native, a.Backend, "the editor runs the native backend");
			LogAssert.IsTrue(a.TimestampSeconds > 0 && b.TimestampSeconds >= a.TimestampSeconds, "monotonic timestamps");
			LogAssert.IsTrue(a.AppSentBytes >= 0 && a.AppRecvBytes >= 0, "application totals are always measured");
			if (a.QuicMeasure == TrafficMeasure.Unavailable)
			{
				LogAssert.AreEqual(-1L, a.QuicSentBytes, "an unavailable layer reads -1, never 0");
				LogAssert.AreEqual(-1L, a.UdpSentDatagrams);
				LogAssert.AreEqual(-1L, a.WireSentBytes);
			}
			else
			{
				LogAssert.AreEqual(TrafficMeasure.Measured, a.QuicMeasure);
				LogAssert.IsTrue(a.QuicSentBytes >= 0 && a.Process.IsValid);
			}
			if (b.TimestampSeconds > a.TimestampSeconds)
				LogAssert.IsTrue(TransportTrafficMath.TryComputeRates(in a, in b, out _), "two captures of one process difference cleanly");
		}
	}
}
