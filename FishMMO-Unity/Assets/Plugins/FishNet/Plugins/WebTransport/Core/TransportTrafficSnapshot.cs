namespace FishNet.Transporting.WebTransport
{
	/// <summary>
	/// How a traffic figure was obtained. Every figure a UI or a report shows carries one, so a
	/// number the process cannot see is never presented as if it had been counted.
	/// </summary>
	public enum TrafficMeasure : byte
	{
		/// <summary>
		/// This process cannot see the layer at all. The value is -1 (or NaN); show "unknown",
		/// never 0.
		/// </summary>
		Unavailable = 0,

		/// <summary>Counted exactly by this process: the managed socket, or msquic's own counters.</summary>
		Measured = 1,

		/// <summary>
		/// Reported by the browser through <c>WebTransport.getStats()</c>. Measured by the browser,
		/// not by us, and the definition differs between browsers: Firefox reports QUIC wire bytes,
		/// the specification describes payload plus a separate overhead figure.
		/// </summary>
		BrowserReported = 2,

		/// <summary>
		/// Derived from measured values through a model (<see cref="TransportTrafficMath"/>). The IP
		/// level is always this: no user-space counter sees IP or UDP headers.
		/// </summary>
		Estimated = 3,
	}

	/// <summary>Which transport backend produced a snapshot.</summary>
	public enum TransportTrafficBackend : byte
	{
		/// <summary>No snapshot has been taken (a default struct).</summary>
		None = 0,

		/// <summary>The native msquic library: desktop clients, the editor, every server.</summary>
		Native = 1,

		/// <summary>The browser's WebTransport API: WebGL clients.</summary>
		Browser = 2,
	}

	/// <summary>
	/// Statistics of the client's own connection: round-trip time, loss, congestion window, path
	/// MTU. Only a client has one; on a server <see cref="IsValid"/> is false.
	/// </summary>
	/// <remarks>
	/// Unknown values are -1 (integers) or <see cref="double.NaN"/> (times), never 0. On native
	/// clients the figures come from msquic's QUIC_STATISTICS_V2 for the current connection and
	/// restart with each connection (a server hop starts a new one); on WebGL they come from
	/// <c>getStats()</c> where the browser offers it, which leaves most of them unknown.
	/// </remarks>
	public struct TransportConnectionStats
	{
		/// <summary>False when there is no connection or its statistics could not be read.</summary>
		public bool IsValid;

		/// <summary><see cref="TrafficMeasure.Measured"/> (msquic) or <see cref="TrafficMeasure.BrowserReported"/>.</summary>
		public TrafficMeasure Measure;

		/// <summary>Monotonic time the figures were read, on the snapshot's clock. Shows how stale they are.</summary>
		public double SampledAtSeconds;

		/// <summary>Smoothed round-trip time, milliseconds.</summary>
		public double RttMs;
		/// <summary>Lowest round-trip time seen on this connection, milliseconds.</summary>
		public double MinRttMs;
		/// <summary>Highest round-trip time seen on this connection, milliseconds. NaN on WebGL.</summary>
		public double MaxRttMs;
		/// <summary>Round-trip time variance, milliseconds.</summary>
		public double RttVarianceMs;

		/// <summary>Current path MTU: the UDP payload budget of one datagram, bytes. -1 when unknown.</summary>
		public int PathMtu;
		/// <summary>Congestion window, bytes. -1 when unknown.</summary>
		public long CongestionWindowBytes;
		/// <summary>Congestion events on this connection. -1 when unknown.</summary>
		public long CongestionEvents;

		/// <summary>QUIC packets sent on this connection, retransmissions included. -1 when unknown.</summary>
		public long SentPackets;
		/// <summary>QUIC packets received on this connection. -1 when unknown.</summary>
		public long RecvPackets;
		/// <summary>UDP payload bytes sent on this connection. -1 when unknown.</summary>
		public long SentBytes;
		/// <summary>UDP payload bytes received on this connection. -1 when unknown.</summary>
		public long RecvBytes;

		/// <summary>
		/// Sent packets declared lost and not later found to have arrived (suspected minus spurious).
		/// QUIC retransmits the frames of these packets in new ones, so this is also the closest
		/// available count of retransmissions. -1 when unknown.
		/// </summary>
		public long LostPackets;
		/// <summary>Sent packets declared lost that later turned out to have arrived. -1 when unknown.</summary>
		public long SpuriousLostPackets;
		/// <summary>Received packets dropped (duplicates included). -1 when unknown.</summary>
		public long RecvDroppedPackets;
		/// <summary>Received packets that arrived out of order. -1 when unknown.</summary>
		public long RecvReorderedPackets;

		/// <summary>
		/// Lost packets as a fraction of packets sent, or NaN when either is unknown or nothing has
		/// been sent.
		/// </summary>
		public double LossRatio => LostPackets < 0 || SentPackets <= 0 ? double.NaN : (double)LostPackets / SentPackets;

		/// <summary>An invalid record with every field set to its "unknown" value.</summary>
		public static TransportConnectionStats Unknown => new TransportConnectionStats
		{
			IsValid = false,
			Measure = TrafficMeasure.Unavailable,
			SampledAtSeconds = double.NaN,
			RttMs = double.NaN,
			MinRttMs = double.NaN,
			MaxRttMs = double.NaN,
			RttVarianceMs = double.NaN,
			PathMtu = -1,
			CongestionWindowBytes = -1,
			CongestionEvents = -1,
			SentPackets = -1,
			RecvPackets = -1,
			SentBytes = -1,
			RecvBytes = -1,
			LostPackets = -1,
			SpuriousLostPackets = -1,
			RecvDroppedPackets = -1,
			RecvReorderedPackets = -1,
		};
	}

	/// <summary>
	/// msquic's process-wide connection and packet counters (native backends only). Totals are
	/// cumulative for the process; the fields marked gauge are current values.
	/// </summary>
	/// <remarks>
	/// These cover every connection the process made or accepted, including those refused before
	/// or during the handshake, which the managed layer never hears about. On WebGL
	/// <see cref="IsValid"/> is false and every field is -1.
	/// </remarks>
	public struct TransportProcessCounters
	{
		/// <summary>False when the native counters are unavailable (WebGL, or the library never loaded).</summary>
		public bool IsValid;

		/// <summary>
		/// Bytes msquic accepted from and delivered to this library: the application payload plus
		/// the transport's own framing (length prefixes, HTTP/3 stream and datagram headers). The
		/// managed app layer excludes that framing, so the two differ by exactly it.
		/// </summary>
		public long QuicAppSentBytes;
		/// <summary>See <see cref="QuicAppSentBytes"/>.</summary>
		public long QuicAppRecvBytes;

		/// <summary>Connections ever allocated, both roles.</summary>
		public long ConnectionsCreated;
		/// <summary>Gauge: connections allocated now, handshaking ones included.</summary>
		public long ConnectionsActive;
		/// <summary>Gauge: connections past the QUIC handshake now.</summary>
		public long ConnectionsConnected;
		/// <summary>Connections that started and were freed before completing the handshake.</summary>
		public long HandshakeFailures;
		/// <summary>Connections refused by this transport's own listener limits (rate, per-IP, half-open, full).</summary>
		public long RefusedByLimits;
		/// <summary>Connections refused by msquic because its workers were overloaded.</summary>
		public long RefusedForLoad;
		/// <summary>Connection attempts with no matching ALPN.</summary>
		public long RefusedNoAlpn;
		/// <summary>Connections shut down with a QUIC protocol error.</summary>
		public long ProtocolErrors;
		/// <summary>Sent packets declared lost, spurious ones included.</summary>
		public long PacketsSuspectedLost;
		/// <summary>Received packets dropped for any reason.</summary>
		public long PacketsDropped;
		/// <summary>Received packets that failed to decrypt.</summary>
		public long DecryptionFailures;
		/// <summary>Stateless retry packets sent (address validation under load).</summary>
		public long StatelessRetriesSent;
		/// <summary>Stateless reset packets sent.</summary>
		public long StatelessResetsSent;

		/// <summary>An invalid record with every field -1.</summary>
		public static TransportProcessCounters Unknown => new TransportProcessCounters
		{
			IsValid = false,
			QuicAppSentBytes = -1,
			QuicAppRecvBytes = -1,
			ConnectionsCreated = -1,
			ConnectionsActive = -1,
			ConnectionsConnected = -1,
			HandshakeFailures = -1,
			RefusedByLimits = -1,
			RefusedForLoad = -1,
			RefusedNoAlpn = -1,
			ProtocolErrors = -1,
			PacketsSuspectedLost = -1,
			PacketsDropped = -1,
			DecryptionFailures = -1,
			StatelessRetriesSent = -1,
			StatelessResetsSent = -1,
		};
	}

	/// <summary>
	/// Everything this process has sent and received through the WebTransport transport, at one
	/// instant, per layer and per direction. Take one with <see cref="TransportTraffic.Capture"/>;
	/// turn two into rates with <see cref="TransportTrafficMath.TryComputeRates"/>.
	/// </summary>
	/// <remarks>
	/// <para>All totals are cumulative for the life of the process: a client's server hop never
	/// zeroes them, so a rate spanning a hop is still correct. They are process-wide, which on a
	/// dedicated server is exactly that server's bandwidth and on a client is the game
	/// connection.</para>
	/// <para>Layers, from the application outwards:</para>
	/// <list type="bullet">
	/// <item><b>App</b> — FishNet bundles handed to and received from the transport. Always
	/// <see cref="TrafficMeasure.Measured"/>.</item>
	/// <item><b>Framing</b> — the transport's length prefix on every reliable message, counted
	/// exactly as it is written or parsed.</item>
	/// <item><b>QUIC</b> — the UDP payload: QUIC headers, AEAD tags, padding, ACK-only packets,
	/// retransmissions and the handshake. Measured from msquic on native backends; on WebGL
	/// browser-reported where <c>getStats()</c> gives bytes, otherwise estimated.</item>
	/// <item><b>Wire</b> — QUIC plus 28 bytes of IPv4 and UDP header per datagram. Always
	/// <see cref="TrafficMeasure.Estimated"/> (no user-space counter sees those headers), and exact
	/// up to IP options when the datagram count is measured.</item>
	/// </list>
	/// <para>A value this process cannot see is -1 with its measure set to
	/// <see cref="TrafficMeasure.Unavailable"/>; never read -1 as zero.</para>
	/// </remarks>
	public struct TransportTrafficSnapshot
	{
		/// <summary>
		/// Seconds on the process's monotonic clock (<see cref="System.Diagnostics.Stopwatch"/>, the
		/// same basis as FishMMO's server <c>MonotonicClock.NowSeconds</c>). Only differences mean
		/// anything. Zero on a default struct.
		/// </summary>
		public double TimestampSeconds;

		/// <summary>The backend that produced this snapshot.</summary>
		public TransportTrafficBackend Backend;

		// ── Application layer (always Measured) ─────────────────────────────

		/// <summary>Reliable-channel bytes handed to the transport.</summary>
		public long AppSentReliableBytes;
		/// <summary>Reliable-channel messages handed to the transport.</summary>
		public long AppSentReliableMessages;
		/// <summary>Unreliable-channel bytes handed to the transport.</summary>
		public long AppSentUnreliableBytes;
		/// <summary>Unreliable-channel messages handed to the transport.</summary>
		public long AppSentUnreliableMessages;
		/// <summary>Reliable-channel bytes received from the transport.</summary>
		public long AppRecvReliableBytes;
		/// <summary>Reliable-channel messages received from the transport.</summary>
		public long AppRecvReliableMessages;
		/// <summary>Unreliable-channel bytes received from the transport.</summary>
		public long AppRecvUnreliableBytes;
		/// <summary>Unreliable-channel messages received from the transport.</summary>
		public long AppRecvUnreliableMessages;

		/// <summary>Length-prefix bytes written in front of sent reliable messages (exact).</summary>
		public long FramingSentBytes;
		/// <summary>Length-prefix bytes parsed in front of received reliable messages (exact).</summary>
		public long FramingRecvBytes;

		/// <summary>All application bytes sent, both channels.</summary>
		public long AppSentBytes => AppSentReliableBytes + AppSentUnreliableBytes;
		/// <summary>All application bytes received, both channels.</summary>
		public long AppRecvBytes => AppRecvReliableBytes + AppRecvUnreliableBytes;
		/// <summary>All application messages sent, both channels.</summary>
		public long AppSentMessages => AppSentReliableMessages + AppSentUnreliableMessages;
		/// <summary>All application messages received, both channels.</summary>
		public long AppRecvMessages => AppRecvReliableMessages + AppRecvUnreliableMessages;

		// ── QUIC / UDP payload layer ─────────────────────────────────────────

		/// <summary>How <see cref="QuicSentBytes"/> and <see cref="QuicRecvBytes"/> were obtained.</summary>
		public TrafficMeasure QuicMeasure;
		/// <summary>UDP payload bytes sent. -1 when <see cref="QuicMeasure"/> is Unavailable.</summary>
		public long QuicSentBytes;
		/// <summary>UDP payload bytes received. -1 when <see cref="QuicMeasure"/> is Unavailable.</summary>
		public long QuicRecvBytes;

		/// <summary>How <see cref="UdpSentDatagrams"/> and <see cref="UdpRecvDatagrams"/> were obtained.</summary>
		public TrafficMeasure DatagramMeasure;
		/// <summary>
		/// UDP datagrams sent. -1 when <see cref="DatagramMeasure"/> is Unavailable. On native
		/// backends msquic 2.5.9 over-counts a flush that goes out in several batches (handshakes,
		/// some bursts); steady traffic counts exactly. See the transport README.
		/// </summary>
		public long UdpSentDatagrams;
		/// <summary>UDP datagrams received. -1 when <see cref="DatagramMeasure"/> is Unavailable.</summary>
		public long UdpRecvDatagrams;

		// ── Sessions (managed, Measured) ─────────────────────────────────────

		/// <summary>
		/// WebTransport sessions established in this process: accepted connections on a server,
		/// successful connects (one per hop) on a client.
		/// </summary>
		public long SessionsOpened;
		/// <summary>Sessions open now: connected clients on a server, 0 or 1 on a client.</summary>
		public int SessionsActive;

		/// <summary>msquic's process-wide connection and packet counters (native only).</summary>
		public TransportProcessCounters Process;

		/// <summary>The client's connection statistics (clients only, when available).</summary>
		public TransportConnectionStats Connection;

		// ── Wire (IP) layer, derived ─────────────────────────────────────────

		/// <summary>The wire layer is always an estimate, or unavailable when the QUIC layer is.</summary>
		public TrafficMeasure WireMeasure => TransportTrafficMath.WireMeasure(QuicMeasure, DatagramMeasure);

		/// <summary>Estimated IP-level bytes sent (UDP payload + datagrams × 28), or -1.</summary>
		public long WireSentBytes => TransportTrafficMath.WireBytes(QuicSentBytes, UdpSentDatagrams, QuicMeasure, DatagramMeasure);

		/// <summary>Estimated IP-level bytes received (UDP payload + datagrams × 28), or -1.</summary>
		public long WireRecvBytes => TransportTrafficMath.WireBytes(QuicRecvBytes, UdpRecvDatagrams, QuicMeasure, DatagramMeasure);
	}

	/// <summary>
	/// The traffic between two snapshots: exact deltas for storage and per-second rates for
	/// display. Produced by <see cref="TransportTrafficMath.TryComputeRates"/>.
	/// </summary>
	/// <remarks>
	/// A delta of -1 means the layer could not be differenced over this interval — unavailable at
	/// either end, or measured one way at one end and another way at the other (a WebGL client
	/// whose first <c>getStats()</c> answer arrived mid-interval). Its rate is then NaN. Show
	/// "unknown", never 0.
	/// </remarks>
	public struct TransportTrafficRates
	{
		/// <summary>Length of the interval, seconds (positive when valid).</summary>
		public double Seconds;

		/// <summary>The QUIC layer's measure over this interval.</summary>
		public TrafficMeasure QuicMeasure;
		/// <summary>The datagram count's measure over this interval.</summary>
		public TrafficMeasure DatagramMeasure;

		/// <summary>Application bytes sent in the interval.</summary>
		public long AppSentBytes;
		/// <summary>Application bytes received in the interval.</summary>
		public long AppRecvBytes;
		/// <summary>Application messages sent in the interval.</summary>
		public long AppSentMessages;
		/// <summary>Application messages received in the interval.</summary>
		public long AppRecvMessages;

		/// <summary>UDP payload bytes sent in the interval, or -1.</summary>
		public long QuicSentBytes;
		/// <summary>UDP payload bytes received in the interval, or -1.</summary>
		public long QuicRecvBytes;
		/// <summary>UDP datagrams sent in the interval, or -1.</summary>
		public long UdpSentDatagrams;
		/// <summary>UDP datagrams received in the interval, or -1.</summary>
		public long UdpRecvDatagrams;

		/// <summary>Estimated IP-level bytes sent in the interval, or -1.</summary>
		public long WireSentBytes;
		/// <summary>Estimated IP-level bytes received in the interval, or -1.</summary>
		public long WireRecvBytes;

		/// <summary>Sessions opened in the interval (accepted clients on a server, connects on a client).</summary>
		public long SessionsOpened;

		/// <summary>
		/// Connections refused before a session in the interval — by this transport's listener
		/// limits, by msquic under load, or for no matching ALPN. -1 when the process counters are
		/// unavailable at either end (WebGL, or before the native library first loaded).
		/// </summary>
		public long ConnectionsRefused;

		/// <summary>
		/// Connections that started and failed their handshake in the interval, or -1 when unknown
		/// (see <see cref="ConnectionsRefused"/>).
		/// </summary>
		public long HandshakeFailures;

		/// <summary>The wire layer's measure over this interval.</summary>
		public TrafficMeasure WireMeasure => TransportTrafficMath.WireMeasure(QuicMeasure, DatagramMeasure);

		/// <summary><paramref name="delta"/> per second, or NaN when it is unknown or the interval is empty.</summary>
		public double PerSecond(long delta) => delta < 0 || !(Seconds > 0) ? double.NaN : delta / Seconds;

		/// <summary>Application bytes sent per second.</summary>
		public double AppSentBytesPerSecond => PerSecond(AppSentBytes);
		/// <summary>Application bytes received per second.</summary>
		public double AppRecvBytesPerSecond => PerSecond(AppRecvBytes);
		/// <summary>UDP payload bytes sent per second, or NaN.</summary>
		public double QuicSentBytesPerSecond => PerSecond(QuicSentBytes);
		/// <summary>UDP payload bytes received per second, or NaN.</summary>
		public double QuicRecvBytesPerSecond => PerSecond(QuicRecvBytes);
		/// <summary>Estimated IP-level bytes sent per second, or NaN.</summary>
		public double WireSentBytesPerSecond => PerSecond(WireSentBytes);
		/// <summary>Estimated IP-level bytes received per second, or NaN.</summary>
		public double WireRecvBytesPerSecond => PerSecond(WireRecvBytes);
		/// <summary>UDP datagrams sent per second, or NaN.</summary>
		public double DatagramsSentPerSecond => PerSecond(UdpSentDatagrams);
		/// <summary>UDP datagrams received per second, or NaN.</summary>
		public double DatagramsRecvPerSecond => PerSecond(UdpRecvDatagrams);

		/// <summary>
		/// Wire bytes per application byte sent (≥ 1): what the transport costs on top of the game's
		/// own data. NaN when the wire layer is unknown or nothing was sent.
		/// </summary>
		public double SentOverheadRatio => TransportTrafficMath.OverheadRatio(WireSentBytes, AppSentBytes);

		/// <summary>Wire bytes per application byte received. NaN when unknown or nothing was received.</summary>
		public double RecvOverheadRatio => TransportTrafficMath.OverheadRatio(WireRecvBytes, AppRecvBytes);
	}
}
