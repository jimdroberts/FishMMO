using System;
using System.Globalization;
using FishMMO.Shared;
using FishNet.Transporting.WebTransport;

namespace FishMMO.Client
{
	/// <summary>The rows of the network statistics overlay, each with one badge and one explanation.</summary>
	public enum NetworkStatsRow : byte
	{
		/// <summary>FishNet bundles handed to and received from the transport.</summary>
		App = 0,
		/// <summary>The UDP payload: QUIC headers, encryption, acknowledgements, retransmissions, handshake.</summary>
		Quic = 1,
		/// <summary>The UDP payload plus the IP and UDP headers: what crosses the line.</summary>
		Wire = 2,
		/// <summary>UDP datagrams per second.</summary>
		Datagrams = 3,
		/// <summary>Wire bytes per application byte.</summary>
		Overhead = 4,
		/// <summary>Round-trip time, loss, path MTU and congestion window of the current connection.</summary>
		Link = 5,
	}

	/// <summary>
	/// The text one row shows: a value per direction, a total per direction where the row has
	/// one, and the measure its badge names.
	/// </summary>
	/// <remarks>
	/// The <see cref="NetworkStatsRow.Link"/> row has no directions; its four slots carry, in
	/// order, the round-trip time, the loss, the path MTU and the congestion window.
	/// </remarks>
	public struct NetworkStatsFigures
	{
		/// <summary>How the row's figures were obtained.</summary>
		public TrafficMeasure Measure;
		/// <summary>True before the first sample, when the measure is not yet known either.</summary>
		public bool Pending;
		/// <summary>The received rate (Link: round-trip time).</summary>
		public string Down;
		/// <summary>The sent rate (Link: loss).</summary>
		public string Up;
		/// <summary>The received total since launch, or null when the row has none (Link: path MTU).</summary>
		public string DownTotal;
		/// <summary>The sent total since launch, or null when the row has none (Link: congestion window).</summary>
		public string UpTotal;
	}

	/// <summary>
	/// Every rule the network statistics overlay applies between the transport's numbers and the
	/// text on screen: units, rounding, what "unknown" looks like, which badge a row wears, and
	/// what its tooltip says about why.
	/// </summary>
	/// <remarks>
	/// <para><b>Units are decimal (SI) throughout.</b> 1 kB is 1,000 bytes and 1 Mbps is 1,000,000
	/// bits a second, so a byte rate and the bit rate beside it differ by exactly eight — the way an
	/// internet plan and a network monitor both count. Binary kibibytes would make "1 MB/s" and
	/// "8 Mbps" disagree by five per cent, and a player comparing the overlay against their plan is
	/// the reader this is for.</para>
	///
	/// <para><b>Bytes in the table, bits in the headline.</b> The table's per-layer figures are
	/// bytes, which is what the counters count and what adds up across layers; the headline is the
	/// on-the-wire rate in bits, because that is the unit a connection's speed is sold in. Every
	/// tooltip gives both.</para>
	///
	/// <para><b>Unknown is a dash, never a zero.</b> The transport reports what it cannot see as -1
	/// or NaN; every formatter here turns those into <see cref="Unknown"/>. A measured zero is still
	/// written as zero.</para>
	///
	/// <para>Culture-invariant, so a comma-decimal machine still reads "1.25 MB".</para>
	/// </remarks>
	public static class NetworkStatsPresentation
	{
		/// <summary>What an unknown value reads as.</summary>
		public const string Unknown = "—";

		/// <summary>
		/// How old the connection figures may be, relative to the snapshot, before they are shown
		/// as unknown. The client socket refreshes them once a second while the overlay is open;
		/// older than this means it has stopped (the connection closed between refreshes).
		/// </summary>
		public const double ConnectionStaleSeconds = 5.0;

		private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

		private static readonly string[] ByteUnits = { "B", "kB", "MB", "GB", "TB" };
		private static readonly string[] ByteRateUnits = { "B/s", "kB/s", "MB/s", "GB/s" };
		private static readonly string[] BitRateUnits = { "bps", "kbps", "Mbps", "Gbps" };
		private static readonly string[] CountUnits = { "", "k", "M" };

		// ── Formatting ──────────────────────────────────────────────────────

		/// <summary>A byte rate, e.g. "43.1 kB/s", or <see cref="Unknown"/>.</summary>
		public static string ByteRate(double bytesPerSecond)
		{
			return IsKnown(bytesPerSecond) ? Scaled(bytesPerSecond, ByteRateUnits) : Unknown;
		}

		/// <summary>A byte rate written in bits, e.g. "345 kbps", or <see cref="Unknown"/>.</summary>
		public static string BitRate(double bytesPerSecond)
		{
			return IsKnown(bytesPerSecond) ? Scaled(bytesPerSecond * 8.0, BitRateUnits) : Unknown;
		}

		/// <summary>A byte count, e.g. "12.4 MB", or <see cref="Unknown"/> when negative.</summary>
		public static string Bytes(long bytes)
		{
			return bytes >= 0 ? Scaled(bytes, ByteUnits) : Unknown;
		}

		/// <summary>A count per second, e.g. "62/s" or "2.5/s", or <see cref="Unknown"/>.</summary>
		public static string PerSecond(double perSecond)
		{
			if (!IsKnown(perSecond))
			{
				return Unknown;
			}
			if (perSecond < 9.95)
			{
				return perSecond.ToString("0.0", Invariant) + "/s";
			}
			if (perSecond < 999.5)
			{
				return perSecond.ToString("0", Invariant) + "/s";
			}
			return Scaled(perSecond, CountUnits).Replace(" ", string.Empty) + "/s";
		}

		/// <summary>A count, e.g. "48,210", or <see cref="Unknown"/> when negative.</summary>
		public static string Count(long count)
		{
			return count >= 0 ? count.ToString("#,0", Invariant) : Unknown;
		}

		/// <summary>A wire-per-app ratio, e.g. "×1.13", or <see cref="Unknown"/>.</summary>
		public static string Ratio(double ratio)
		{
			if (!IsKnown(ratio))
			{
				return Unknown;
			}
			return "×" + ratio.ToString(ratio < 9.995 ? "0.00" : "0.0", Invariant);
		}

		/// <summary>Milliseconds, e.g. "42 ms" or "4.2 ms", or <see cref="Unknown"/>.</summary>
		public static string Milliseconds(double ms)
		{
			if (!IsKnown(ms))
			{
				return Unknown;
			}
			return ms.ToString(ms < 9.95 ? "0.0" : "0", Invariant) + " ms";
		}

		/// <summary>A fraction as a percentage, e.g. "0.20%", "1.5%", "12%", or <see cref="Unknown"/>.</summary>
		public static string Percent(double fraction)
		{
			if (!IsKnown(fraction))
			{
				return Unknown;
			}
			double percent = fraction * 100.0;
			string format = percent < 0.995 ? "0.00" : percent < 9.95 ? "0.0" : "0";
			return percent.ToString(format, Invariant) + "%";
		}

		/// <summary>A path MTU in bytes, e.g. "1452 B", or <see cref="Unknown"/> when not positive.</summary>
		public static string Mtu(int bytes)
		{
			return bytes > 0 ? bytes.ToString(Invariant) + " B" : Unknown;
		}

		/// <summary>True for a finite, non-negative value.</summary>
		public static bool IsKnown(double value)
		{
			return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0;
		}

		/// <summary>
		/// Three significant figures in the largest unit that keeps the number under a thousand;
		/// the base unit is written as a whole number.
		/// </summary>
		/// <remarks>
		/// The unit is chosen before rounding would push the number to 1000: 999.6 B reads "1.00
		/// kB", not "1000 B".
		/// </remarks>
		private static string Scaled(double value, string[] units)
		{
			int unit = 0;
			while (unit < units.Length - 1 && value >= 999.5)
			{
				value /= 1000.0;
				++unit;
			}

			string number;
			if (unit == 0)
			{
				number = value.ToString("0", Invariant);
			}
			else if (value < 9.995)
			{
				number = value.ToString("0.00", Invariant);
			}
			else if (value < 99.95)
			{
				number = value.ToString("0.0", Invariant);
			}
			else
			{
				number = value.ToString("0", Invariant);
			}
			return number + " " + units[unit];
		}

		// ── Badges ──────────────────────────────────────────────────────────

		/// <summary>USS class of a badge for <paramref name="measure"/>, or the pending class.</summary>
		public static string BadgeClass(TrafficMeasure measure, bool pending = false)
		{
			if (pending)
			{
				return "netstats-badge--pending";
			}
			switch (measure)
			{
				case TrafficMeasure.Measured: return "netstats-badge--measured";
				case TrafficMeasure.Estimated: return "netstats-badge--estimated";
				case TrafficMeasure.BrowserReported: return "netstats-badge--browser";
				default: return "netstats-badge--unavailable";
			}
		}

		/// <summary>Every badge class, so a refresh can clear the ones that no longer apply.</summary>
		public static readonly string[] BadgeClasses =
		{
			"netstats-badge--measured",
			"netstats-badge--estimated",
			"netstats-badge--browser",
			"netstats-badge--unavailable",
			"netstats-badge--pending",
		};

		/// <summary>The word a badge shows for <paramref name="measure"/>.</summary>
		public static string BadgeText(TrafficMeasure measure, bool pending = false)
		{
			if (pending)
			{
				return "…";
			}
			switch (measure)
			{
				case TrafficMeasure.Measured: return "MEASURED";
				case TrafficMeasure.Estimated: return "ESTIMATE";
				case TrafficMeasure.BrowserReported: return "BROWSER";
				default: return "N/A";
			}
		}

		/// <summary>The measure spelled out, for a tooltip's subtitle.</summary>
		public static string MeasureName(TrafficMeasure measure)
		{
			switch (measure)
			{
				case TrafficMeasure.Measured: return "Measured";
				case TrafficMeasure.Estimated: return "Estimated";
				case TrafficMeasure.BrowserReported: return "Reported by the browser";
				default: return "Unavailable";
			}
		}

		/// <summary>The row's name as the overlay's first column shows it.</summary>
		public static string RowName(NetworkStatsRow row)
		{
			switch (row)
			{
				case NetworkStatsRow.App: return "App";
				case NetworkStatsRow.Quic: return "QUIC/UDP";
				case NetworkStatsRow.Wire: return "Wire (IP)";
				case NetworkStatsRow.Datagrams: return "Datagrams";
				case NetworkStatsRow.Overhead: return "Overhead";
				default: return "Link";
			}
		}

		/// <summary>The row's name as a tooltip's title spells it out.</summary>
		public static string RowTitle(NetworkStatsRow row)
		{
			switch (row)
			{
				case NetworkStatsRow.App: return "Game data (application layer)";
				case NetworkStatsRow.Quic: return "QUIC / UDP payload";
				case NetworkStatsRow.Wire: return "On the wire (IP level)";
				case NetworkStatsRow.Datagrams: return "UDP datagrams";
				case NetworkStatsRow.Overhead: return "Transport overhead";
				default: return "Connection";
			}
		}

		// ── Figures ─────────────────────────────────────────────────────────

		/// <summary>The measure a row's badge names, read from the newest snapshot.</summary>
		/// <remarks>
		/// The snapshot, not the interval: the totals come from it, and it is what the next
		/// interval will be measured as. The two differ only for the one sample after a layer
		/// changes definition, when the rates read unknown anyway.
		/// </remarks>
		public static TrafficMeasure RowMeasure(NetworkStatsRow row, in NetworkStatsReadout readout)
		{
			if (!readout.HasSnapshot)
			{
				return TrafficMeasure.Unavailable;
			}
			ref readonly TransportTrafficSnapshot s = ref readout.Latest;
			switch (row)
			{
				case NetworkStatsRow.App: return TrafficMeasure.Measured;
				case NetworkStatsRow.Quic: return s.QuicMeasure;
				case NetworkStatsRow.Wire: return s.WireMeasure;
				case NetworkStatsRow.Datagrams: return s.DatagramMeasure;
				case NetworkStatsRow.Overhead: return s.WireMeasure;
				default: return ConnectionIsCurrent(in s) ? s.Connection.Measure : TrafficMeasure.Unavailable;
			}
		}

		/// <summary>
		/// True when the snapshot carries connection figures that are valid and recent.
		/// </summary>
		/// <remarks>
		/// A reading older than <see cref="ConnectionStaleSeconds"/> is not presented as the
		/// current round-trip time. The socket withdraws its figures when it disconnects, but a
		/// read that failed quietly would otherwise leave the last good number on screen forever.
		/// </remarks>
		public static bool ConnectionIsCurrent(in TransportTrafficSnapshot snapshot)
		{
			if (!snapshot.Connection.IsValid || snapshot.Connection.Measure == TrafficMeasure.Unavailable)
			{
				return false;
			}
			double age = snapshot.TimestampSeconds - snapshot.Connection.SampledAtSeconds;
			return !double.IsNaN(age) && age <= ConnectionStaleSeconds;
		}

		/// <summary>The text a row shows for <paramref name="readout"/>.</summary>
		public static NetworkStatsFigures Describe(NetworkStatsRow row, in NetworkStatsReadout readout)
		{
			NetworkStatsFigures f = new NetworkStatsFigures
			{
				Measure = RowMeasure(row, in readout),
				Pending = !readout.HasSnapshot,
				Down = Unknown,
				Up = Unknown,
				DownTotal = Unknown,
				UpTotal = Unknown,
			};

			bool rates = readout.HasRates;
			ref readonly TransportTrafficSnapshot s = ref readout.Latest;
			ref readonly TransportTrafficRates r = ref readout.Rates;

			switch (row)
			{
				case NetworkStatsRow.App:
					if (rates)
					{
						f.Down = ByteRate(r.AppRecvBytesPerSecond);
						f.Up = ByteRate(r.AppSentBytesPerSecond);
					}
					if (readout.HasSnapshot)
					{
						f.DownTotal = Bytes(s.AppRecvBytes);
						f.UpTotal = Bytes(s.AppSentBytes);
					}
					break;

				case NetworkStatsRow.Quic:
					if (rates)
					{
						f.Down = ByteRate(r.QuicRecvBytesPerSecond);
						f.Up = ByteRate(r.QuicSentBytesPerSecond);
					}
					if (readout.HasSnapshot && s.QuicMeasure != TrafficMeasure.Unavailable)
					{
						f.DownTotal = Bytes(s.QuicRecvBytes);
						f.UpTotal = Bytes(s.QuicSentBytes);
					}
					break;

				case NetworkStatsRow.Wire:
					if (rates)
					{
						f.Down = ByteRate(r.WireRecvBytesPerSecond);
						f.Up = ByteRate(r.WireSentBytesPerSecond);
					}
					if (readout.HasSnapshot)
					{
						f.DownTotal = Bytes(s.WireRecvBytes);
						f.UpTotal = Bytes(s.WireSentBytes);
					}
					break;

				case NetworkStatsRow.Datagrams:
					if (rates)
					{
						f.Down = PerSecond(r.DatagramsRecvPerSecond);
						f.Up = PerSecond(r.DatagramsSentPerSecond);
					}
					f.DownTotal = null;
					f.UpTotal = null;
					break;

				case NetworkStatsRow.Overhead:
					if (rates)
					{
						f.Down = Ratio(r.RecvOverheadRatio);
						f.Up = Ratio(r.SentOverheadRatio);
					}
					f.DownTotal = null;
					f.UpTotal = null;
					break;

				default:
					// Four unknowns unless the reading is valid and recent.
					if (readout.HasSnapshot && ConnectionIsCurrent(in s))
					{
						f.Down = Milliseconds(s.Connection.RttMs);
						f.Up = Percent(s.Connection.LossRatio);
						f.DownTotal = Mtu(s.Connection.PathMtu);
						f.UpTotal = Bytes(s.Connection.CongestionWindowBytes);
					}
					break;
			}
			return f;
		}

		/// <summary>The on-the-wire rate each way in bits, for the headline.</summary>
		public static void Headline(in NetworkStatsReadout readout, out string down, out string up)
		{
			if (!readout.HasRates)
			{
				down = Unknown;
				up = Unknown;
				return;
			}
			down = BitRate(readout.Rates.WireRecvBytesPerSecond);
			up = BitRate(readout.Rates.WireSentBytesPerSecond);
		}

		/// <summary>The backend as a short caption for the overlay's header.</summary>
		public static string BackendCaption(in NetworkStatsReadout readout)
		{
			if (!readout.HasSnapshot)
			{
				return string.Empty;
			}
			switch (readout.Latest.Backend)
			{
				case TransportTrafficBackend.Native: return "NATIVE QUIC";
				case TransportTrafficBackend.Browser: return "BROWSER";
				default: return string.Empty;
			}
		}

		// ── Graph ───────────────────────────────────────────────────────────

		/// <summary>
		/// The graph's vertical scale: the peak rounded up to 1, 2 or 5 times a power of ten, and
		/// never below <paramref name="floor"/> bytes a second.
		/// </summary>
		/// <remarks>
		/// The floor keeps an idle connection's few hundred bytes of acknowledgements from being
		/// drawn as a full-height mountain. The rounding keeps the scale label a number a person
		/// would say ("50 kB/s", not "43.7 kB/s"), and keeps it from twitching every second.
		/// </remarks>
		public static double GraphScale(double peak, double floor = 1000.0)
		{
			double value = IsKnown(peak) ? Math.Max(peak, floor) : floor;
			double magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(value)));
			double normalised = value / magnitude;
			double step = normalised <= 1.0 ? 1.0 : normalised <= 2.0 ? 2.0 : normalised <= 5.0 ? 5.0 : 10.0;
			return step * magnitude;
		}

		/// <summary>
		/// The vertical position of a graph point, 0 at the top, <paramref name="height"/> at the
		/// bottom, or NaN for an unknown point (a break in the line, never a zero).
		/// </summary>
		public static float GraphY(double value, double scale, float height)
		{
			if (!IsKnown(value) || !(scale > 0.0))
			{
				return float.NaN;
			}
			double fraction = Math.Min(1.0, value / scale);
			return (float)(height - fraction * height);
		}

		/// <summary>
		/// Writes the explanation of the traffic graph: what a point is, what the scale is, and
		/// what a gap means.
		/// </summary>
		/// <param name="content">The content being assembled.</param>
		/// <param name="peak">The highest known point in the graph, bytes a second, or NaN.</param>
		/// <param name="wire">The wire layer's measure in the newest snapshot.</param>
		public static void BuildGraphTooltip(TooltipContent content, double peak, TrafficMeasure wire)
		{
			double scale = GraphScale(peak);
			content.AddTitle("Traffic, last minute");
			content.AddSubtitle(MeasureName(wire), tone: ToneFor(wire, false));
			content.AddBody("The on-the-wire rate each second, down and up, coloured as in the headline. Both lines share one scale.");
			content.AddBody("A gap is a second that could not be measured, not a second without traffic.", tone: TooltipTone.Muted);
			content.AddStat("Top of the graph", ByteRate(scale) + "  ·  " + BitRate(scale), TooltipPriority.Stats);
			content.AddStat("Peak", IsKnown(peak) ? ByteRate(peak) + "  ·  " + BitRate(peak) : Unknown, TooltipPriority.Stats + 1);
			content.Sort();
		}

		// ── Tooltips ────────────────────────────────────────────────────────

		/// <summary>
		/// Writes the explanation of one row into <paramref name="content"/>: what it counts, how it
		/// was obtained, and why that is the best this process can do.
		/// </summary>
		public static void BuildTooltip(TooltipContent content, NetworkStatsRow row, in NetworkStatsReadout readout)
		{
			NetworkStatsFigures f = Describe(row, in readout);
			TrafficMeasure measure = f.Measure;
			ref readonly TransportTrafficSnapshot s = ref readout.Latest;
			ref readonly TransportTrafficRates r = ref readout.Rates;
			bool browser = readout.HasSnapshot && s.Backend == TransportTrafficBackend.Browser;

			content.AddTitle(RowTitle(row));
			content.AddSubtitle(f.Pending ? "Waiting for the first sample" : MeasureName(measure),
				tone: ToneFor(measure, f.Pending));

			if (f.Pending)
			{
				content.AddBody("The overlay samples the transport once a second while it is open; figures appear after the first sample, and rates after the second.");
				content.Sort();
				return;
			}

			switch (row)
			{
				case NetworkStatsRow.App:
					content.AddBody("FishNet's messages exactly as they were handed to the transport and received from it, counted by the game's own socket.");
					content.AddBody("Excludes the transport's length prefix on reliable messages and everything below it.", tone: TooltipTone.Muted);
					AddRateStats(content, in readout, readout.HasRates ? r.AppRecvBytesPerSecond : double.NaN, readout.HasRates ? r.AppSentBytesPerSecond : double.NaN);
					AddTotalStats(content, s.AppRecvBytes, s.AppSentBytes);
					content.AddStat("Messages down / up", Count(s.AppRecvMessages) + " / " + Count(s.AppSentMessages), TooltipPriority.Stats + 2);
					break;

				case NetworkStatsRow.Quic:
					content.AddBody(QuicExplanation(measure, browser));
					if (measure != TrafficMeasure.Unavailable)
					{
						AddRateStats(content, in readout, readout.HasRates ? r.QuicRecvBytesPerSecond : double.NaN, readout.HasRates ? r.QuicSentBytesPerSecond : double.NaN);
						AddTotalStats(content, s.QuicRecvBytes, s.QuicSentBytes);
					}
					break;

				case NetworkStatsRow.Wire:
					content.AddBody(WireExplanation(measure, s.DatagramMeasure, browser));
					if (measure != TrafficMeasure.Unavailable)
					{
						AddRateStats(content, in readout, readout.HasRates ? r.WireRecvBytesPerSecond : double.NaN, readout.HasRates ? r.WireSentBytesPerSecond : double.NaN);
						AddTotalStats(content, s.WireRecvBytes, s.WireSentBytes);
					}
					if (measure != TrafficMeasure.Unavailable && s.DatagramMeasure == TrafficMeasure.Measured)
					{
						content.AddHint(SentDatagramCaveat);
					}
					break;

				case NetworkStatsRow.Datagrams:
					content.AddBody(DatagramExplanation(measure, browser));
					if (measure != TrafficMeasure.Unavailable)
					{
						content.AddStat("Now down / up", f.Down + " / " + f.Up, TooltipPriority.Stats);
						content.AddStat("Total down / up", Count(s.UdpRecvDatagrams) + " / " + Count(s.UdpSentDatagrams), TooltipPriority.Stats + 1);
					}
					if (measure == TrafficMeasure.Measured)
					{
						content.AddHint(SentDatagramCaveat);
					}
					break;

				case NetworkStatsRow.Overhead:
					content.AddBody("Bytes on the wire for every byte of game data. ×1.25 means QUIC, encryption, acknowledgements and the IP and UDP headers add a quarter on top of what the game itself sent.");
					if (measure != TrafficMeasure.Unavailable)
					{
						content.AddBody("Estimated because the wire figure it is divided from is.", tone: TooltipTone.Muted);
						content.AddStat(Capitalised(Window(in readout)) + " down / up", f.Down + " / " + f.Up, TooltipPriority.Stats);
						content.AddStat("Since launch down / up",
							Ratio(TransportTrafficMath.OverheadRatio(s.WireRecvBytes, s.AppRecvBytes)) + " / " +
							Ratio(TransportTrafficMath.OverheadRatio(s.WireSentBytes, s.AppSentBytes)),
							TooltipPriority.Stats + 1);
						content.AddHint("Small messages cost the most: every packet pays about 30 bytes of QUIC framing and 28 of IP and UDP header whatever it carries.");
					}
					else
					{
						content.AddBody("Unknown while the wire figure is.", tone: TooltipTone.Muted);
					}
					break;

				default:
					content.AddBody(LinkExplanation(measure, browser));
					if (measure != TrafficMeasure.Unavailable)
					{
						TransportConnectionStats c = s.Connection;
						content.AddStat("Round trip", Milliseconds(c.RttMs), TooltipPriority.Stats);
						content.AddStat("Lowest / highest", Milliseconds(c.MinRttMs) + " / " + Milliseconds(c.MaxRttMs), TooltipPriority.Stats + 1);
						content.AddStat("Packets lost", LossText(in c), TooltipPriority.Stats + 2);
						content.AddStat("Path MTU", Mtu(c.PathMtu), TooltipPriority.Stats + 3);
						content.AddStat("Congestion window", Bytes(c.CongestionWindowBytes), TooltipPriority.Stats + 4);
						content.AddHint("These restart with each server hop: they describe the current connection only.");
					}
					break;
			}

			content.Sort();
		}

		/// <summary>
		/// The one msquic quirk that bounds a figure: its count of datagrams SENT is too high
		/// during bursts, so the sent datagrams and the sent wire bytes derived from them are upper
		/// bounds. Received counts are exact.
		/// </summary>
		public const string SentDatagramCaveat =
			"Upload is an upper bound: msquic 2.5.9 counts some sent datagrams twice when a burst (such as a handshake) goes out in several batches, and each extra count adds 28 bytes to the header estimate. Received counts are exact, and the QUIC byte counts are unaffected.";

		private static string QuicExplanation(TrafficMeasure measure, bool browser)
		{
			switch (measure)
			{
				case TrafficMeasure.Measured:
					return "Every UDP payload byte this process sent and received, counted by msquic: QUIC headers, encryption tags, padding, acknowledgements, retransmissions and the handshake. Excludes the IP and UDP headers, which no program can see.";
				case TrafficMeasure.BrowserReported:
					return "Reported by the browser's WebTransport getStats(). Browsers disagree on the definition: Firefox counts QUIC wire bytes, the specification splits payload from overhead. The game cannot see beneath the browser.";
				case TrafficMeasure.Estimated:
					return "Your browser does not report transport bytes (Chrome offers getStats() only behind a flag, and without byte counts), so this is modelled from the game's own traffic: the exact message framing, about 30 bytes per QUIC packet, acknowledgements, and the handshake. It has not been checked against a packet capture.";
				default:
					return browser
						? "The browser reports nothing at this layer."
						: "The transport library has not started yet (no connection has been made in this session), so nothing below the game's own messages can be read. It is unknown, not zero.";
			}
		}

		private static string WireExplanation(TrafficMeasure measure, TrafficMeasure datagrams, bool browser)
		{
			if (measure == TrafficMeasure.Unavailable)
			{
				return browser
					? "Unknown while the QUIC layer is."
					: "Unknown until the transport library starts: it is the QUIC figure plus the IP and UDP headers.";
			}
			string exactness = datagrams == TrafficMeasure.Measured
				? " With a measured datagram count this is exact up to IP options, which this client never sends."
				: " The datagram count is itself estimated here, so this is an estimate of an estimate.";
			return "The QUIC figure plus 28 bytes per datagram (IPv4 20 + UDP 8). The operating system adds those headers after the game hands the packet over, so no program can count them; they are computed, not measured." +
				exactness + " A VPN or tunnel adds more that nothing here sees. Ethernet framing is not included: bandwidth is billed at the IP level.";
		}

		private static string DatagramExplanation(TrafficMeasure measure, bool browser)
		{
			switch (measure)
			{
				case TrafficMeasure.Measured:
					return "UDP datagrams this process sent and received, counted by msquic. Every one of them also carries 28 bytes of IP and UDP header.";
				case TrafficMeasure.BrowserReported:
				case TrafficMeasure.Estimated:
					return browser
						? "QUIC packets as the browser reports them (about one per datagram), or where it reports none, modelled from the game's message counts."
						: "Modelled from the game's message counts.";
				default:
					return "Unknown until the transport library starts.";
			}
		}

		private static string LinkExplanation(TrafficMeasure measure, bool browser)
		{
			switch (measure)
			{
				case TrafficMeasure.Measured:
					return "msquic's own statistics for this client's connection: smoothed round-trip time, packets declared lost (each one's contents were sent again), the path MTU it discovered, and the congestion window.";
				case TrafficMeasure.BrowserReported:
					return "The round-trip time the browser reports through getStats(). Browsers report nothing else about the connection, so the other figures stay unknown.";
				default:
					return browser
						? "Your browser does not report connection statistics (Chrome offers getStats() only behind a flag)."
						: "No connection statistics yet: not connected, or the first reading since the overlay opened has not arrived. They are read once a second while the overlay is open.";
			}
		}

		private static string LossText(in TransportConnectionStats c)
		{
			if (c.LostPackets < 0 || c.SentPackets < 0)
			{
				return Unknown;
			}
			return Count(c.LostPackets) + " of " + Count(c.SentPackets) + " (" + Percent(c.LossRatio) + ")";
		}

		private static void AddRateStats(TooltipContent content, in NetworkStatsReadout readout, double downBytesPerSecond, double upBytesPerSecond)
		{
			string window = Window(in readout);
			content.AddStat("Down, " + window, ByteRate(downBytesPerSecond) + "  ·  " + BitRate(downBytesPerSecond), TooltipPriority.Stats);
			content.AddStat("Up, " + window, ByteRate(upBytesPerSecond) + "  ·  " + BitRate(upBytesPerSecond), TooltipPriority.Stats);
		}

		/// <summary>
		/// The interval the rates actually span, e.g. "last 5 s": shorter for the first seconds
		/// after the overlay opens, longer after a stall.
		/// </summary>
		private static string Window(in NetworkStatsReadout readout)
		{
			if (!readout.HasRates || !IsKnown(readout.Rates.Seconds))
			{
				return "last " + NetworkStatsSampler.SmoothingWindowSeconds.ToString("0", Invariant) + " s";
			}
			return "last " + Math.Max(1.0, Math.Round(readout.Rates.Seconds)).ToString("0", Invariant) + " s";
		}

		private static string Capitalised(string text)
		{
			return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
		}

		private static void AddTotalStats(TooltipContent content, long downBytes, long upBytes)
		{
			content.AddStat("Since launch down / up", Bytes(downBytes) + " / " + Bytes(upBytes), TooltipPriority.Stats + 1);
		}

		private static TooltipTone ToneFor(TrafficMeasure measure, bool pending)
		{
			if (pending)
			{
				return TooltipTone.Muted;
			}
			switch (measure)
			{
				case TrafficMeasure.Measured: return TooltipTone.Good;
				case TrafficMeasure.Estimated: return TooltipTone.Accent;
				case TrafficMeasure.BrowserReported: return TooltipTone.Neutral;
				default: return TooltipTone.Muted;
			}
		}
	}
}
