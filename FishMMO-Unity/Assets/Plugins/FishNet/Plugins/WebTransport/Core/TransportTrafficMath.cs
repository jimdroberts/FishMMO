using System;
using FishNet.Transporting.WebTransport.Native;

namespace FishNet.Transporting.WebTransport
{
	/// <summary>
	/// The pure arithmetic of the traffic statistics: rates from two snapshots, the IP-level
	/// estimate, the overhead ratio, the WebGL estimate model, and the mapping of the raw native
	/// and browser figures into a snapshot. No clock, no socket, no allocation, so each rule can be
	/// pinned by a test.
	/// </summary>
	public static class TransportTrafficMath
	{
		/// <summary>
		/// IPv4 header (20) plus UDP header (8), paid by every datagram and seen by no user-space
		/// counter. The native stack binds IPv4 only (see <c>WebTransport.SetServerBindAddress</c>);
		/// IPv6 would be 48. Ethernet framing is deliberately not added: egress is billed at the IP
		/// level.
		/// </summary>
		public const int IpV4UdpHeaderBytes = 20 + 8;

		// ── Wire layer ──────────────────────────────────────────────────────

		/// <summary>
		/// The wire layer's measure: <see cref="TrafficMeasure.Estimated"/> whenever the QUIC layer
		/// and the datagram count are both known, because the IP and UDP headers never are.
		/// </summary>
		public static TrafficMeasure WireMeasure(TrafficMeasure quic, TrafficMeasure datagrams)
		{
			return quic == TrafficMeasure.Unavailable || datagrams == TrafficMeasure.Unavailable
				? TrafficMeasure.Unavailable
				: TrafficMeasure.Estimated;
		}

		/// <summary>
		/// IP-level bytes: the UDP payload plus <see cref="IpV4UdpHeaderBytes"/> per datagram, or -1
		/// when either input is unknown. With measured inputs this is exact up to IP options, which
		/// this stack never sends.
		/// </summary>
		public static long WireBytes(long quicBytes, long datagrams, TrafficMeasure quic, TrafficMeasure datagramMeasure)
		{
			if (WireMeasure(quic, datagramMeasure) == TrafficMeasure.Unavailable || quicBytes < 0 || datagrams < 0)
				return -1;
			return quicBytes + datagrams * IpV4UdpHeaderBytes;
		}

		/// <summary>
		/// Wire bytes per application byte, or NaN when the wire figure is unknown or there were no
		/// application bytes to divide by. 1.25 means the transport added a quarter on top.
		/// </summary>
		public static double OverheadRatio(long wireBytes, long appBytes)
		{
			return wireBytes < 0 || appBytes <= 0 ? double.NaN : (double)wireBytes / appBytes;
		}

		/// <summary>
		/// Bytes of the QUIC variable-length integer (RFC 9000 §16) that length-prefixes a reliable
		/// message: 1 below 64, 2 below 16384, 4 below 2^30, else 8. The native library and the
		/// WebGL bridge both frame with it.
		/// </summary>
		public static int VarintLength(long value)
		{
			if (value < 64) return 1;
			if (value < 16384) return 2;
			if (value < 1073741824) return 4;
			return 8;
		}

		// ── Rates ───────────────────────────────────────────────────────────

		/// <summary>
		/// The traffic between two snapshots of the same process. Fails (returns false) when the
		/// interval is empty or negative, the backends differ, or an application counter went
		/// backwards — two snapshots that cannot belong to one process lifetime.
		/// </summary>
		/// <remarks>
		/// A layer whose measure differs between the two ends, or that is unavailable at either,
		/// gets delta -1 and measure <see cref="TrafficMeasure.Unavailable"/> for this interval
		/// rather than a difference of two incomparable numbers. For display smoothing, difference
		/// against an older snapshot (say five seconds back) instead of averaging one-second rates:
		/// the result is the same average without the rounding of each step.
		/// </remarks>
		public static bool TryComputeRates(in TransportTrafficSnapshot earlier, in TransportTrafficSnapshot later, out TransportTrafficRates rates)
		{
			rates = default;
			double seconds = later.TimestampSeconds - earlier.TimestampSeconds;
			if (!(seconds > 0) || later.Backend == TransportTrafficBackend.None || later.Backend != earlier.Backend)
				return false;

			long appSent = later.AppSentBytes - earlier.AppSentBytes;
			long appRecv = later.AppRecvBytes - earlier.AppRecvBytes;
			long appSentMessages = later.AppSentMessages - earlier.AppSentMessages;
			long appRecvMessages = later.AppRecvMessages - earlier.AppRecvMessages;
			if (appSent < 0 || appRecv < 0 || appSentMessages < 0 || appRecvMessages < 0)
				return false;

			rates.Seconds = seconds;
			rates.AppSentBytes = appSent;
			rates.AppRecvBytes = appRecv;
			rates.AppSentMessages = appSentMessages;
			rates.AppRecvMessages = appRecvMessages;
			rates.SessionsOpened = Math.Max(0, later.SessionsOpened - earlier.SessionsOpened);

			rates.QuicMeasure = LayerDelta(earlier.QuicMeasure, later.QuicMeasure,
				earlier.QuicSentBytes, later.QuicSentBytes, earlier.QuicRecvBytes, later.QuicRecvBytes,
				out rates.QuicSentBytes, out rates.QuicRecvBytes);

			rates.DatagramMeasure = LayerDelta(earlier.DatagramMeasure, later.DatagramMeasure,
				earlier.UdpSentDatagrams, later.UdpSentDatagrams, earlier.UdpRecvDatagrams, later.UdpRecvDatagrams,
				out rates.UdpSentDatagrams, out rates.UdpRecvDatagrams);

			rates.WireSentBytes = WireBytes(rates.QuicSentBytes, rates.UdpSentDatagrams, rates.QuicMeasure, rates.DatagramMeasure);
			rates.WireRecvBytes = WireBytes(rates.QuicRecvBytes, rates.UdpRecvDatagrams, rates.QuicMeasure, rates.DatagramMeasure);

			rates.ConnectionsRefused = -1;
			rates.HandshakeFailures = -1;
			if (earlier.Process.IsValid && later.Process.IsValid)
			{
				long refused = Refused(in later.Process) - Refused(in earlier.Process);
				long failed = later.Process.HandshakeFailures - earlier.Process.HandshakeFailures;
				rates.ConnectionsRefused = refused >= 0 ? refused : -1;
				rates.HandshakeFailures = failed >= 0 ? failed : -1;
			}
			return true;
		}

		/// <summary>Every refusal before a session: listener limits, msquic load, no ALPN.</summary>
		private static long Refused(in TransportProcessCounters p)
		{
			return p.RefusedByLimits + p.RefusedForLoad + p.RefusedNoAlpn;
		}

		/// <summary>One layer's two deltas, or -1/-1 and Unavailable when it cannot be differenced.</summary>
		private static TrafficMeasure LayerDelta(TrafficMeasure earlierMeasure, TrafficMeasure laterMeasure,
			long earlierSent, long laterSent, long earlierRecv, long laterRecv, out long sent, out long recv)
		{
			sent = -1;
			recv = -1;
			if (laterMeasure == TrafficMeasure.Unavailable || laterMeasure != earlierMeasure)
				return TrafficMeasure.Unavailable;
			if (earlierSent < 0 || laterSent < 0 || earlierRecv < 0 || laterRecv < 0)
				return TrafficMeasure.Unavailable;
			long s = laterSent - earlierSent;
			long r = laterRecv - earlierRecv;
			if (s < 0 || r < 0)
				return TrafficMeasure.Unavailable;
			sent = s;
			recv = r;
			return laterMeasure;
		}

		// ── WebGL estimate model ────────────────────────────────────────────
		/* A browser that does not expose byte counters (Chrome ships getStats() only behind a flag,
		 * and without bytes when it does) leaves the QUIC layer invisible. It is modelled from what
		 * the socket does see — every message, its channel, its size, its exact length prefix — plus
		 * the fixed costs below. Each constant is an estimate with its reason, taken from the RFCs
		 * and the native library's own framing; none has been checked against a packet capture of a
		 * browser, which is why the result is labelled Estimated wherever it is shown. The model is
		 * a monotone function of cumulative counters, so an estimated total never goes backwards and
		 * an estimated rate is never negative. */

		/// <summary>
		/// Fixed bytes of one 1-RTT QUIC packet around its frames: 1 flags byte, a 9-byte destination
		/// connection id (msquic issues 9), a 4-byte packet number, and the 16-byte AEAD tag.
		/// </summary>
		public const int QuicPacketOverheadBytes = 1 + 9 + 4 + 16;

		/// <summary>STREAM or DATAGRAM frame type and length (plus a stream offset, amortised), per message.</summary>
		public const int QuicFrameHeaderBytes = 3;

		/// <summary>An ACK frame acknowledging one contiguous range.</summary>
		public const int QuicAckFrameBytes = 5;

		/// <summary>
		/// Ack-eliciting packets per ACK: RFC 9000 §13.2.2 has a receiver acknowledge at least every
		/// second one.
		/// </summary>
		public const int AckElicitingPacketsPerAck = 2;

		/// <summary>
		/// Payload one packet carries before a large reliable message spills into the next: QUIC's
		/// guaranteed 1200-byte datagram, conservatively ignoring a larger discovered path MTU.
		/// </summary>
		public const int QuicPacketPayloadBudget = 1200;

		/// <summary>The HTTP/3 datagram's Quarter Stream ID (RFC 9297 §2.1), 1 byte for the first session.</summary>
		public const int QuarterStreamIdBytes = 1;

		/// <summary>
		/// Client handshake flight per session: the Initial padded to about 1250 bytes (RFC 9000
		/// §14.1 requires at least 1200) and the Handshake packet carrying Finished.
		/// </summary>
		public const int BrowserHandshakeSentBytes = 1350;
		/// <summary>Datagrams in <see cref="BrowserHandshakeSentBytes"/>.</summary>
		public const int BrowserHandshakeSentDatagrams = 2;

		/// <summary>
		/// Server handshake flight per session: ServerHello, encrypted extensions and a typical
		/// publicly trusted certificate chain (2–3 KB), kept under the 3× anti-amplification limit of
		/// RFC 9000 §8.1.
		/// </summary>
		public const int BrowserHandshakeRecvBytes = 3000;
		/// <summary>Datagrams in <see cref="BrowserHandshakeRecvBytes"/>.</summary>
		public const int BrowserHandshakeRecvDatagrams = 3;

		/// <summary>
		/// WebTransport session set-up the browser sends once per session: HTTP/3 control and QPACK
		/// stream openers with SETTINGS, the extended CONNECT request, and the stream header.
		/// </summary>
		public const int BrowserSessionSetupSentBytes = 150;

		/// <summary>The server's HTTP/3 SETTINGS and its CONNECT response, once per session.</summary>
		public const int BrowserSessionSetupRecvBytes = 35;

		/// <summary>
		/// Estimated QUIC-layer totals for a browser session history, from the application counters
		/// of <paramref name="app"/> (its App*, Framing* and SessionsOpened fields).
		/// </summary>
		/// <param name="app">A snapshot whose application counters are filled.</param>
		/// <param name="sentBytes">Estimated UDP payload bytes sent.</param>
		/// <param name="recvBytes">Estimated UDP payload bytes received.</param>
		/// <param name="sentDatagrams">Estimated datagrams sent.</param>
		/// <param name="recvDatagrams">Estimated datagrams received.</param>
		public static void EstimateBrowserQuic(in TransportTrafficSnapshot app,
			out long sentBytes, out long recvBytes, out long sentDatagrams, out long recvDatagrams)
		{
			long sessions = Math.Max(0, app.SessionsOpened);

			long sentData = DataPackets(app.AppSentReliableMessages, app.AppSentReliableBytes + app.FramingSentBytes, app.AppSentUnreliableMessages);
			long recvData = DataPackets(app.AppRecvReliableMessages, app.AppRecvReliableBytes + app.FramingRecvBytes, app.AppRecvUnreliableMessages);

			/* Each side acknowledges the other's data. An ACK rides on a data packet when there is
			 * one to ride on; the rest go in ACK-only packets. Over a whole history that is
			 * max(data packets, ACKs needed) packets per side. */
			long sentAcks = CeilDiv(recvData, AckElicitingPacketsPerAck);
			long recvAcks = CeilDiv(sentData, AckElicitingPacketsPerAck);
			long sentPackets = Math.Max(sentData, sentAcks);
			long recvPackets = Math.Max(recvData, recvAcks);

			sentBytes = app.AppSentBytes + app.FramingSentBytes
				+ app.AppSentUnreliableMessages * QuarterStreamIdBytes
				+ app.AppSentMessages * QuicFrameHeaderBytes
				+ sentPackets * QuicPacketOverheadBytes
				+ sentAcks * QuicAckFrameBytes
				+ sessions * (BrowserHandshakeSentBytes + BrowserSessionSetupSentBytes);

			recvBytes = app.AppRecvBytes + app.FramingRecvBytes
				+ app.AppRecvUnreliableMessages * QuarterStreamIdBytes
				+ app.AppRecvMessages * QuicFrameHeaderBytes
				+ recvPackets * QuicPacketOverheadBytes
				+ recvAcks * QuicAckFrameBytes
				+ sessions * (BrowserHandshakeRecvBytes + BrowserSessionSetupRecvBytes);

			sentDatagrams = sentPackets + sessions * BrowserHandshakeSentDatagrams;
			recvDatagrams = recvPackets + sessions * BrowserHandshakeRecvDatagrams;
		}

		/// <summary>
		/// Data-carrying packets: one per datagram message, and for the reliable stream one per
		/// message or one per <see cref="QuicPacketPayloadBudget"/> bytes, whichever is more.
		/// </summary>
		private static long DataPackets(long reliableMessages, long reliableStreamBytes, long unreliableMessages)
		{
			return Math.Max(0, unreliableMessages)
				+ Math.Max(Math.Max(0, reliableMessages), CeilDiv(Math.Max(0, reliableStreamBytes), QuicPacketPayloadBudget));
		}

		private static long CeilDiv(long value, long divisor)
		{
			return value <= 0 ? 0 : (value + divisor - 1) / divisor;
		}

		// ── Browser getStats() mapping ──────────────────────────────────────

		/// <summary>
		/// Slots of the array <c>WTGetStats</c> in WebTransport.jslib fills, and bit i of its return
		/// mask says slot i was present. Cumulative slots are summed over every session this page
		/// has opened; the RTT slots are the live session's.
		/// </summary>
		public static class BrowserStats
		{
			public const int BytesSent = 0;
			public const int BytesSentOverhead = 1;
			public const int BytesReceived = 2;
			public const int PacketsSent = 3;
			public const int PacketsReceived = 4;
			public const int PacketsLost = 5;
			public const int SmoothedRttMs = 6;
			public const int MinRttMs = 7;
			public const int RttVariationMs = 8;
			/// <summary>Number of slots.</summary>
			public const int Count = 9;

			/// <summary>Mask bit: the browser's WebTransport object has getStats().</summary>
			public const int ApiPresentBit = 1 << 16;
			/// <summary>Mask bit: a live session has answered at least once.</summary>
			public const int LiveSampleBit = 1 << 17;

			/// <summary>True when <paramref name="mask"/> says <paramref name="slot"/> was filled.</summary>
			public static bool Has(int mask, int slot) => (mask & (1 << slot)) != 0;
		}

		/// <summary>
		/// Fills a WebGL snapshot's QUIC layer, datagram count and connection statistics from the
		/// browser's figures where it gave them, and from <see cref="EstimateBrowserQuic"/> where it
		/// did not. The application counters must already be filled.
		/// </summary>
		/// <remarks>
		/// Bytes count as <see cref="TrafficMeasure.BrowserReported"/> only when both directions were
		/// reported. Sent bytes add <c>bytesSentOverhead</c> when present, which is the
		/// specification's split (payload, then framing and retransmission overhead); Firefox reports
		/// wire bytes in <c>bytesSent</c> and omits the overhead field, which this also reads right.
		/// The datagram count is always <see cref="TrafficMeasure.Estimated"/> on WebGL: the browser
		/// reports QUIC packets, which equal datagrams except where the handshake coalesces them.
		/// </remarks>
		public static void ApplyBrowserStats(ref TransportTrafficSnapshot snapshot, double[] values, int mask, double nowSeconds)
		{
			EstimateBrowserQuic(in snapshot, out long estSentBytes, out long estRecvBytes, out long estSentDatagrams, out long estRecvDatagrams);

			bool haveBytes = values != null && values.Length >= BrowserStats.Count &&
				BrowserStats.Has(mask, BrowserStats.BytesSent) && BrowserStats.Has(mask, BrowserStats.BytesReceived);
			if (haveBytes)
			{
				double sent = values[BrowserStats.BytesSent];
				if (BrowserStats.Has(mask, BrowserStats.BytesSentOverhead))
					sent += values[BrowserStats.BytesSentOverhead];
				snapshot.QuicMeasure = TrafficMeasure.BrowserReported;
				snapshot.QuicSentBytes = ToCount(sent);
				snapshot.QuicRecvBytes = ToCount(values[BrowserStats.BytesReceived]);
			}
			else
			{
				snapshot.QuicMeasure = TrafficMeasure.Estimated;
				snapshot.QuicSentBytes = estSentBytes;
				snapshot.QuicRecvBytes = estRecvBytes;
			}

			snapshot.DatagramMeasure = TrafficMeasure.Estimated;
			bool havePackets = values != null && values.Length >= BrowserStats.Count &&
				BrowserStats.Has(mask, BrowserStats.PacketsSent) && BrowserStats.Has(mask, BrowserStats.PacketsReceived);
			snapshot.UdpSentDatagrams = havePackets ? ToCount(values[BrowserStats.PacketsSent]) : estSentDatagrams;
			snapshot.UdpRecvDatagrams = havePackets ? ToCount(values[BrowserStats.PacketsReceived]) : estRecvDatagrams;

			TransportConnectionStats c = TransportConnectionStats.Unknown;
			if (values != null && values.Length >= BrowserStats.Count && (mask & BrowserStats.LiveSampleBit) != 0)
			{
				c.Measure = TrafficMeasure.BrowserReported;
				c.SampledAtSeconds = nowSeconds;
				if (BrowserStats.Has(mask, BrowserStats.SmoothedRttMs)) c.RttMs = values[BrowserStats.SmoothedRttMs];
				if (BrowserStats.Has(mask, BrowserStats.MinRttMs)) c.MinRttMs = values[BrowserStats.MinRttMs];
				if (BrowserStats.Has(mask, BrowserStats.RttVariationMs)) c.RttVarianceMs = values[BrowserStats.RttVariationMs];
				c.IsValid = !double.IsNaN(c.RttMs);
			}
			snapshot.Connection = c;
			snapshot.Process = TransportProcessCounters.Unknown;
		}

		private static long ToCount(double value)
		{
			if (double.IsNaN(value) || value < 0) return -1;
			return value >= long.MaxValue ? long.MaxValue : (long)value;
		}

		// ── Native mapping ──────────────────────────────────────────────────

		/// <summary>msquic's process counters as a managed record (all Measured).</summary>
		public static TransportProcessCounters FromNative(in WebTransportNative.WtGlobalCounters g)
		{
			return new TransportProcessCounters
			{
				IsValid = true,
				QuicAppSentBytes = Clamp(g.AppSendBytes),
				QuicAppRecvBytes = Clamp(g.AppRecvBytes),
				ConnectionsCreated = Clamp(g.ConnCreated),
				ConnectionsActive = Clamp(g.ConnActive),
				ConnectionsConnected = Clamp(g.ConnConnected),
				HandshakeFailures = Clamp(g.ConnHandshakeFail),
				RefusedByLimits = Clamp(g.ConnAppReject),
				RefusedForLoad = Clamp(g.ConnLoadReject),
				RefusedNoAlpn = Clamp(g.ConnNoAlpn),
				ProtocolErrors = Clamp(g.ConnProtocolErrors),
				PacketsSuspectedLost = Clamp(g.PktsSuspectedLost),
				PacketsDropped = Clamp(g.PktsDropped),
				DecryptionFailures = Clamp(g.PktsDecryptionFail),
				StatelessRetriesSent = Clamp(g.StatelessRetrySent),
				StatelessResetsSent = Clamp(g.StatelessResetSent),
			};
		}

		/// <summary>One connection's msquic statistics as a managed record.</summary>
		public static TransportConnectionStats FromNative(in WebTransportNative.WtConnectionStats s, double nowSeconds)
		{
			const double MicrosecondsPerMs = 1000.0;
			bool hasCwnd = (s.Flags & WebTransportNative.WtConnectionStats.HasCongestionWindow) != 0;
			bool hasVariance = (s.Flags & WebTransportNative.WtConnectionStats.HasRttVariance) != 0;
			long suspected = Clamp(s.SendSuspectedLostPackets);
			long spurious = Clamp(s.SendSpuriousLostPackets);
			return new TransportConnectionStats
			{
				IsValid = true,
				Measure = TrafficMeasure.Measured,
				SampledAtSeconds = nowSeconds,
				RttMs = s.RttUs / MicrosecondsPerMs,
				MinRttMs = s.MinRttUs / MicrosecondsPerMs,
				MaxRttMs = s.MaxRttUs / MicrosecondsPerMs,
				RttVarianceMs = hasVariance ? s.RttVarianceUs / MicrosecondsPerMs : double.NaN,
				PathMtu = s.PathMtu > 0 ? (int)Math.Min(s.PathMtu, int.MaxValue) : -1,
				CongestionWindowBytes = hasCwnd ? s.CongestionWindow : -1,
				CongestionEvents = s.CongestionEvents,
				SentPackets = Clamp(s.SendPackets),
				RecvPackets = Clamp(s.RecvPackets),
				SentBytes = Clamp(s.SendBytes),
				RecvBytes = Clamp(s.RecvBytes),
				LostPackets = Math.Max(0, suspected - spurious),
				SpuriousLostPackets = spurious,
				RecvDroppedPackets = Clamp(s.RecvDroppedPackets),
				RecvReorderedPackets = Clamp(s.RecvReorderedPackets),
			};
		}

		/// <summary>A native unsigned counter as a signed total; saturates rather than wrapping negative.</summary>
		private static long Clamp(ulong value)
		{
			return value > long.MaxValue ? long.MaxValue : (long)value;
		}
	}
}
