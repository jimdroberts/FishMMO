using System;
using System.Collections.Generic;
using System.Linq;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Which tier a bandwidth row belongs to, as stored in <c>server_kind</c>.
	/// </summary>
	/// <remarks>
	/// The numbers are <c>FishMMO.Server.Core.ServerType</c>'s (Login 1, World 2, Scene 3). This
	/// assembly cannot reference that one, so the pairing is pinned by a test
	/// (<c>ServerBandwidthLedgerTests</c>) rather than by a shared type, as the entity
	/// configuration README describes for every enum-valued column.
	/// </remarks>
	public static class ServerBandwidthKind
	{
		/// <summary>A login server.</summary>
		public const int Login = 1;

		/// <summary>A world server.</summary>
		public const int World = 2;

		/// <summary>A scene server.</summary>
		public const int Scene = 3;

		/// <summary>Every tier, in the order the panel lists them.</summary>
		public static readonly int[] All = { Login, World, Scene };

		/// <summary>Whether <paramref name="kind"/> is one of the three tiers.</summary>
		public static bool IsValid(int kind) => kind == Login || kind == World || kind == Scene;

		/// <summary>The tier's name as the panel's routes spell it: <c>login</c>, <c>world</c> or <c>scene</c>.</summary>
		public static string Name(int kind) => kind switch
		{
			Login => "login",
			World => "world",
			Scene => "scene",
			_ => "unknown",
		};

		/// <summary>Parses <c>login</c>, <c>world</c> or <c>scene</c>, ignoring case and surrounding space.</summary>
		public static bool TryParse(string? text, out int kind)
		{
			switch ((text ?? string.Empty).Trim().ToLowerInvariant())
			{
				case "login":
					kind = Login;
					return true;
				case "world":
					kind = World;
					return true;
				case "scene":
					kind = Scene;
					return true;
				default:
					kind = 0;
					return false;
			}
		}
	}

	/// <summary>The span the bandwidth page charts, and the resolution it charts it at.</summary>
	public enum ServerBandwidthRange
	{
		/// <summary>The last hour, one point a minute.</summary>
		Hour = 1,

		/// <summary>The last 24 hours, one point per five minutes.</summary>
		Day = 2,

		/// <summary>The last 30 days (whole hours), one point an hour.</summary>
		Month = 3,
	}

	/// <summary>
	/// The rules the bandwidth tables are read and written by, as pure functions.
	/// </summary>
	public static class ServerBandwidthMath
	{
		/// <summary>
		/// IPv4 (20) plus UDP (8) header bytes per datagram, the difference between msquic's UDP
		/// payload count and the bytes on the wire.
		/// </summary>
		/// <remarks>
		/// The same constant as <c>TransportTrafficMath.IpV4UdpHeaderBytes</c> in the WebTransport
		/// plugin, which this assembly cannot reference; a test pins them equal. The native stack
		/// binds IPv4 only. Ethernet framing is deliberately not added: egress is billed at the IP
		/// level.
		/// </remarks>
		public const int IpV4UdpHeaderBytes = 20 + 8;

		/// <summary>How long minute rows are kept before the rollup job folds and deletes them.</summary>
		public const int MinuteRetentionDays = 14;

		/// <summary>How long hour rows are kept: 13 months, so a year-on-year comparison always exists.</summary>
		public const int HourRetentionMonths = 13;

		/// <summary>
		/// How many trailing hours every rollup pass recomputes, the current one included.
		/// </summary>
		/// <remarks>
		/// A server holds a minute it could not write and retries it for up to
		/// <see cref="MaxPendingMinutes"/>, so a minute row can land that long after its minute.
		/// The window has to reach further back than that, or the hour it belongs to would have
		/// been rolled for the last time before the row arrived.
		/// </remarks>
		public const int RollupWindowHours = 6;

		/// <summary>
		/// The most minutes a server process holds for retry while the database refuses its
		/// writes; older ones are dropped, and the drop is logged.
		/// </summary>
		public const int MaxPendingMinutes = 60;

		/// <summary>
		/// How far behind the current hour the 30-day figures stop reading hour rows and start
		/// reading minutes, so they never depend on the rollup having run in the last few minutes.
		/// </summary>
		public const int StitchHours = 3;

		/// <summary>
		/// A server's latest minute counts as its current rate only while it is at most this old
		/// by the database clock. A server samples every 60 seconds, so a live one is never more
		/// than two minutes behind; five leaves room for a deferred sample.
		/// </summary>
		public const int CurrentFreshSeconds = 300;

		/// <summary>The bytes on the wire: UDP payload plus <see cref="IpV4UdpHeaderBytes"/> per datagram. An estimate.</summary>
		public static long EstimateWireBytes(long udpPayloadBytes, long datagrams)
		{
			return udpPayloadBytes + (datagrams * IpV4UdpHeaderBytes);
		}

		/// <summary>The wire rate estimated from a payload rate and a datagram rate.</summary>
		public static double EstimateWireRate(double udpPayloadPerSecond, double datagramsPerSecond)
		{
			return udpPayloadPerSecond + (datagramsPerSecond * IpV4UdpHeaderBytes);
		}

		/// <summary>
		/// <paramref name="value"/> per second over <paramref name="intervalMs"/>, or NaN when the
		/// interval is empty. Never <c>value / 60</c>: a minute row's interval is what it says.
		/// </summary>
		public static double PerSecond(long value, long intervalMs)
		{
			return intervalMs > 0 ? value * 1000.0 / intervalMs : double.NaN;
		}

		/// <summary>Truncates a UTC time to its minute.</summary>
		public static DateTime FloorToMinute(DateTime utc)
		{
			return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMinute), DateTimeKind.Utc);
		}

		/// <summary>Truncates a UTC time to its hour.</summary>
		public static DateTime FloorToHour(DateTime utc)
		{
			return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerHour), DateTimeKind.Utc);
		}

		/// <summary>The width of one chart point for a range, in seconds.</summary>
		public static int ResolutionSeconds(ServerBandwidthRange range) => range switch
		{
			ServerBandwidthRange.Hour => 60,
			ServerBandwidthRange.Day => 300,
			_ => 3600,
		};

		/// <summary>
		/// Where the 30-day figures switch from hour rows to minute rows: the start of the hour
		/// <see cref="StitchHours"/> before the current one.
		/// </summary>
		public static DateTime StitchPoint(DateTime nowUtc)
		{
			return FloorToHour(nowUtc).AddHours(-StitchHours);
		}

		/// <summary>The start of the 30-day window: whole hours, so it lines up with the hour rows.</summary>
		public static DateTime MonthWindowStart(DateTime nowUtc)
		{
			return FloorToHour(nowUtc.AddDays(-30));
		}

		/// <summary>Where a range's chart starts, by the database clock.</summary>
		public static DateTime RangeStart(ServerBandwidthRange range, DateTime nowUtc) => range switch
		{
			ServerBandwidthRange.Hour => nowUtc.AddHours(-1),
			ServerBandwidthRange.Day => nowUtc.AddHours(-24),
			_ => MonthWindowStart(nowUtc),
		};

		/// <summary>
		/// The oldest hour whose minutes the retention job may fold and delete: every minute before
		/// this instant is older than <see cref="MinuteRetentionDays"/>. Whole hours only, so no
		/// hour is ever left with some of its minutes gone.
		/// </summary>
		public static DateTime MinuteRetentionCutoff(DateTime nowUtc)
		{
			return FloorToHour(nowUtc.AddDays(-MinuteRetentionDays));
		}
	}

	/// <summary>
	/// One minute a server process writes about itself: the difference in its transport's counters
	/// over an interval that ended in <see cref="BucketStartUtc"/>. Measured values only.
	/// </summary>
	public struct ServerBandwidthSample
	{
		/// <summary>The minute, by the database clock. See <c>ServerBandwidthMinuteEntity.BucketStart</c>.</summary>
		public DateTime BucketStartUtc;

		/// <summary>The counted interval, milliseconds. Positive.</summary>
		public long IntervalMs;

		/// <summary>Application bytes sent.</summary>
		public long AppSentBytes;

		/// <summary>Application bytes received.</summary>
		public long AppRecvBytes;

		/// <summary>UDP payload bytes sent.</summary>
		public long UdpSentBytes;

		/// <summary>UDP payload bytes received.</summary>
		public long UdpRecvBytes;

		/// <summary>UDP datagrams sent.</summary>
		public long UdpSentDatagrams;

		/// <summary>UDP datagrams received.</summary>
		public long UdpRecvDatagrams;

		/// <summary>Sessions open when sampled (a gauge).</summary>
		public long SessionsActive;

		/// <summary>Sessions accepted in the interval.</summary>
		public long SessionsOpened;

		/// <summary>Connections refused before a session in the interval.</summary>
		public long ConnectionsRefused;

		/// <summary>Handshakes that failed in the interval.</summary>
		public long HandshakeFailures;

		/// <summary>
		/// Why this sample cannot be written, or null when it can. Every counter must be
		/// non-negative and the interval positive: a negative would mean the process could not see
		/// the value, and storing it would put "unknown" in a column that sums.
		/// </summary>
		public string? Invalid()
		{
			if (BucketStartUtc.Ticks % TimeSpan.TicksPerMinute != 0) return "the bucket is not a whole minute";
			if (IntervalMs <= 0) return "the interval is not positive";
			if (AppSentBytes < 0 || AppRecvBytes < 0 || UdpSentBytes < 0 || UdpRecvBytes < 0 ||
				UdpSentDatagrams < 0 || UdpRecvDatagrams < 0 || SessionsActive < 0 || SessionsOpened < 0 ||
				ConnectionsRefused < 0 || HandshakeFailures < 0)
			{
				return "a counter is negative";
			}
			return null;
		}
	}

	/// <summary>
	/// Traffic summed over a window: the measured totals, and the wire estimate derived from them.
	/// </summary>
	public sealed class ServerBandwidthTotals
	{
		/// <summary>Minute rows summed (for hour rows, the minutes they were rolled from).</summary>
		public long Samples { get; set; }

		/// <summary>The counted intervals summed, milliseconds.</summary>
		public long IntervalMs { get; set; }

		/// <summary>Application bytes sent. Measured.</summary>
		public long AppSentBytes { get; set; }

		/// <summary>Application bytes received. Measured.</summary>
		public long AppRecvBytes { get; set; }

		/// <summary>UDP payload bytes sent. Measured.</summary>
		public long UdpSentBytes { get; set; }

		/// <summary>UDP payload bytes received. Measured.</summary>
		public long UdpRecvBytes { get; set; }

		/// <summary>UDP datagrams sent. Measured; an upper bound under bursts.</summary>
		public long UdpSentDatagrams { get; set; }

		/// <summary>UDP datagrams received. Measured.</summary>
		public long UdpRecvDatagrams { get; set; }

		/// <summary>Sessions accepted.</summary>
		public long SessionsOpened { get; set; }

		/// <summary>Connections refused before a session.</summary>
		public long ConnectionsRefused { get; set; }

		/// <summary>Failed handshakes.</summary>
		public long HandshakeFailures { get; set; }

		/// <summary>The highest session gauge seen.</summary>
		public long SessionsActiveMax { get; set; }

		/// <summary>The newest bucket that contributed, or null.</summary>
		public DateTime? LastBucketUtc { get; set; }

		/// <summary>Bytes sent on the wire: payload plus headers. ESTIMATED, and an upper bound (see the datagram count).</summary>
		public long WireSentBytesEstimated => ServerBandwidthMath.EstimateWireBytes(UdpSentBytes, UdpSentDatagrams);

		/// <summary>Bytes received on the wire: payload plus headers. ESTIMATED.</summary>
		public long WireRecvBytesEstimated => ServerBandwidthMath.EstimateWireBytes(UdpRecvBytes, UdpRecvDatagrams);

		/// <summary>Adds <paramref name="other"/> into this one.</summary>
		public void Add(ServerBandwidthTotals other)
		{
			if (other == null)
			{
				return;
			}
			Samples += other.Samples;
			IntervalMs += other.IntervalMs;
			AppSentBytes += other.AppSentBytes;
			AppRecvBytes += other.AppRecvBytes;
			UdpSentBytes += other.UdpSentBytes;
			UdpRecvBytes += other.UdpRecvBytes;
			UdpSentDatagrams += other.UdpSentDatagrams;
			UdpRecvDatagrams += other.UdpRecvDatagrams;
			SessionsOpened += other.SessionsOpened;
			ConnectionsRefused += other.ConnectionsRefused;
			HandshakeFailures += other.HandshakeFailures;
			SessionsActiveMax = Math.Max(SessionsActiveMax, other.SessionsActiveMax);
			if (other.LastBucketUtc.HasValue && (!LastBucketUtc.HasValue || other.LastBucketUtc.Value > LastBucketUtc.Value))
			{
				LastBucketUtc = other.LastBucketUtc;
			}
		}
	}

	/// <summary>
	/// Rates at one point: a server's latest minute, one chart bucket, or a peak. Bytes and
	/// datagrams per second; the wire rates are estimates derived from the measured ones.
	/// </summary>
	public sealed class ServerBandwidthRates
	{
		/// <summary>The bucket these rates describe (its start, UTC).</summary>
		public DateTime BucketUtc { get; set; }

		/// <summary>Application bytes sent per second. Measured.</summary>
		public double AppSentBps { get; set; }

		/// <summary>Application bytes received per second. Measured.</summary>
		public double AppRecvBps { get; set; }

		/// <summary>UDP payload bytes sent per second. Measured.</summary>
		public double UdpSentBps { get; set; }

		/// <summary>UDP payload bytes received per second. Measured.</summary>
		public double UdpRecvBps { get; set; }

		/// <summary>UDP datagrams sent per second. Measured; an upper bound under bursts.</summary>
		public double DatagramsSentPs { get; set; }

		/// <summary>UDP datagrams received per second. Measured.</summary>
		public double DatagramsRecvPs { get; set; }

		/// <summary>Sessions open, where the point is a single sample; otherwise null.</summary>
		public long? SessionsActive { get; set; }

		/// <summary>Bytes on the wire sent per second. ESTIMATED; an upper bound under bursts.</summary>
		public double WireSentBpsEstimated => ServerBandwidthMath.EstimateWireRate(UdpSentBps, DatagramsSentPs);

		/// <summary>Bytes on the wire received per second. ESTIMATED.</summary>
		public double WireRecvBpsEstimated => ServerBandwidthMath.EstimateWireRate(UdpRecvBps, DatagramsRecvPs);

		/// <summary>Rates from one row of counters over its own interval.</summary>
		public static ServerBandwidthRates FromCounters(DateTime bucketUtc, long intervalMs,
			long appSent, long appRecv, long udpSent, long udpRecv, long datagramsSent, long datagramsRecv)
		{
			return new ServerBandwidthRates
			{
				BucketUtc = bucketUtc,
				AppSentBps = ServerBandwidthMath.PerSecond(appSent, intervalMs),
				AppRecvBps = ServerBandwidthMath.PerSecond(appRecv, intervalMs),
				UdpSentBps = ServerBandwidthMath.PerSecond(udpSent, intervalMs),
				UdpRecvBps = ServerBandwidthMath.PerSecond(udpRecv, intervalMs),
				DatagramsSentPs = ServerBandwidthMath.PerSecond(datagramsSent, intervalMs),
				DatagramsRecvPs = ServerBandwidthMath.PerSecond(datagramsRecv, intervalMs),
			};
		}

		/// <summary>A copy, so a sum can be built without changing the rows it came from.</summary>
		public ServerBandwidthRates Clone()
		{
			return (ServerBandwidthRates)MemberwiseClone();
		}

		/// <summary>Adds another server's rates at the same point into this one.</summary>
		public void Add(ServerBandwidthRates other)
		{
			if (other == null)
			{
				return;
			}
			AppSentBps += other.AppSentBps;
			AppRecvBps += other.AppRecvBps;
			UdpSentBps += other.UdpSentBps;
			UdpRecvBps += other.UdpRecvBps;
			DatagramsSentPs += other.DatagramsSentPs;
			DatagramsRecvPs += other.DatagramsRecvPs;
			if (other.SessionsActive.HasValue)
			{
				SessionsActive = (SessionsActive ?? 0) + other.SessionsActive.Value;
			}
		}
	}

	/// <summary>One server on the bandwidth page.</summary>
	/// <remarks>
	/// Every figure is null when the server has no rows for it — "no data", never zero. A server
	/// that ran and moved nothing has a row with zeros; a server that did not run has no row, and
	/// the page must not describe the two the same way.
	/// </remarks>
	public sealed class ServerBandwidthServerSummary
	{
		/// <summary>The tier (<see cref="ServerBandwidthKind"/>).</summary>
		public int Kind { get; set; }

		/// <summary>The server's configured name.</summary>
		public string Name { get; set; } = string.Empty;

		/// <summary>Its latest minute, if that minute is recent enough to be "now"; otherwise null.</summary>
		public ServerBandwidthRates? Current { get; set; }

		/// <summary>The last hour, or null.</summary>
		public ServerBandwidthTotals? LastHour { get; set; }

		/// <summary>The last 24 hours, or null.</summary>
		public ServerBandwidthTotals? LastDay { get; set; }

		/// <summary>The last 30 days (whole hours), or null.</summary>
		public ServerBandwidthTotals? Last30Days { get; set; }

		/// <summary>The busiest point of the charted range by UDP payload sent, or null.</summary>
		public ServerBandwidthRates? Peak { get; set; }

		/// <summary>The newest minute or hour it wrote within 30 days, or null.</summary>
		public DateTime? LastSeenUtc { get; set; }

		/// <summary>Process instances that wrote in the last 24 hours. More than one is a restart.</summary>
		public long Instances24h { get; set; }
	}

	/// <summary>A tier's servers added together, or every server (<see cref="Kind"/> 0).</summary>
	public sealed class ServerBandwidthTierSummary
	{
		/// <summary>The tier, or 0 for the grand total.</summary>
		public int Kind { get; set; }

		/// <summary>Servers with any row in 30 days.</summary>
		public int Servers { get; set; }

		/// <summary>Servers with a current minute.</summary>
		public int ServersReporting { get; set; }

		/// <summary>Current rates of the servers that have one, summed; null when none has.</summary>
		public ServerBandwidthRates? Current { get; set; }

		/// <summary>The last hour, or null.</summary>
		public ServerBandwidthTotals? LastHour { get; set; }

		/// <summary>The last 24 hours, or null.</summary>
		public ServerBandwidthTotals? LastDay { get; set; }

		/// <summary>The last 30 days, or null.</summary>
		public ServerBandwidthTotals? Last30Days { get; set; }

		/// <summary>The busiest point of the charted range, the servers summed per point, or null.</summary>
		public ServerBandwidthRates? Peak { get; set; }
	}

	/// <summary>Everything the bandwidth page shows, read as one snapshot.</summary>
	public sealed class ServerBandwidthReport
	{
		/// <summary>The database clock the report was read at.</summary>
		public DateTime GeneratedUtc { get; set; }

		/// <summary>The charted range.</summary>
		public ServerBandwidthRange Range { get; set; }

		/// <summary>Seconds per chart point.</summary>
		public int ResolutionSeconds { get; set; }

		/// <summary>Where the chart starts.</summary>
		public DateTime RangeStartUtc { get; set; }

		/// <summary>Every server with any row in 30 days, by tier then name.</summary>
		public List<ServerBandwidthServerSummary> Servers { get; set; } = new List<ServerBandwidthServerSummary>();

		/// <summary>One row per tier that has a server.</summary>
		public List<ServerBandwidthTierSummary> Tiers { get; set; } = new List<ServerBandwidthTierSummary>();

		/// <summary>Every server added together.</summary>
		public ServerBandwidthTierSummary Total { get; set; } = new ServerBandwidthTierSummary();

		/// <summary>The chart's tier, or 0 for every tier.</summary>
		public int ScopeKind { get; set; }

		/// <summary>The chart's server, or null for a whole tier or everything.</summary>
		public string? ScopeName { get; set; }

		/// <summary>The chart: one point per bucket that has data, oldest first. A missing bucket is a gap, not a zero.</summary>
		public List<ServerBandwidthRates> Series { get; set; } = new List<ServerBandwidthRates>();

		/// <summary>The newest hour the rollup has written, or null when it has written none.</summary>
		public DateTime? LatestRolledHourUtc { get; set; }

		/// <summary>
		/// True when minutes older than the stitch point exist but the rollup has not caught up
		/// to them, so the 30-day figures are short by those hours.
		/// </summary>
		public bool RollupBehind { get; set; }
	}

	/// <summary>Which window a <see cref="ServerBandwidthWindowRow"/> sums.</summary>
	public enum ServerBandwidthWindow
	{
		/// <summary>The last hour.</summary>
		Hour = 1,

		/// <summary>The last 24 hours.</summary>
		Day = 2,

		/// <summary>The last 30 days.</summary>
		Month = 3,
	}

	/// <summary>One server's totals over one fixed window, as the report query returns them.</summary>
	public sealed class ServerBandwidthWindowRow
	{
		/// <summary>The window.</summary>
		public ServerBandwidthWindow Window { get; set; }

		/// <summary>The tier.</summary>
		public int Kind { get; set; }

		/// <summary>The server.</summary>
		public string Name { get; set; } = string.Empty;

		/// <summary>The totals.</summary>
		public ServerBandwidthTotals Totals { get; set; } = new ServerBandwidthTotals();

		/// <summary>Distinct process instances in the window; minute windows only, 0 otherwise.</summary>
		public long Instances { get; set; }
	}

	/// <summary>One server's rates at one point (its latest minute, or its peak), as the report query returns them.</summary>
	public sealed class ServerBandwidthPointRow
	{
		/// <summary>The tier.</summary>
		public int Kind { get; set; }

		/// <summary>The server.</summary>
		public string Name { get; set; } = string.Empty;

		/// <summary>The rates.</summary>
		public ServerBandwidthRates Rates { get; set; } = new ServerBandwidthRates();
	}

	/// <summary>One tier's servers summed at one chart point, as the report query returns them.</summary>
	public sealed class ServerBandwidthKindPointRow
	{
		/// <summary>The tier.</summary>
		public int Kind { get; set; }

		/// <summary>The servers' rates, each over its own interval, summed.</summary>
		public ServerBandwidthRates Rates { get; set; } = new ServerBandwidthRates();
	}

	/// <summary>
	/// Assembles the bandwidth page from what the report queries return. Pure, so the rules —
	/// "no data" is null, a tier sums only servers that reported, a peak is the busiest point of
	/// the summed series — are testable without a database.
	/// </summary>
	public static class ServerBandwidthReportBuilder
	{
		/// <summary>Builds the report. The inputs are not modified.</summary>
		/// <param name="nowUtc">The database clock the rows were read at.</param>
		/// <param name="range">The charted range.</param>
		/// <param name="windows">Every server's fixed-window totals.</param>
		/// <param name="currents">Each server's latest minute, only where it is fresh.</param>
		/// <param name="peaks">Each server's busiest point of the range.</param>
		/// <param name="kindSeries">Per tier and chart point, the servers' rates summed.</param>
		/// <param name="scopeKind">The chart's tier, or 0.</param>
		/// <param name="scopeName">The chart's server, or null.</param>
		/// <param name="serverSeries">The scoped server's own points, when <paramref name="scopeName"/> is set.</param>
		public static ServerBandwidthReport Build(
			DateTime nowUtc,
			ServerBandwidthRange range,
			IEnumerable<ServerBandwidthWindowRow> windows,
			IEnumerable<ServerBandwidthPointRow> currents,
			IEnumerable<ServerBandwidthPointRow> peaks,
			IEnumerable<ServerBandwidthKindPointRow> kindSeries,
			int scopeKind,
			string? scopeName,
			IEnumerable<ServerBandwidthRates>? serverSeries)
		{
			var report = new ServerBandwidthReport
			{
				GeneratedUtc = nowUtc,
				Range = range,
				ResolutionSeconds = ServerBandwidthMath.ResolutionSeconds(range),
				RangeStartUtc = ServerBandwidthMath.RangeStart(range, nowUtc),
				ScopeKind = scopeKind,
				ScopeName = scopeName,
			};

			var servers = new Dictionary<(int, string), ServerBandwidthServerSummary>();
			ServerBandwidthServerSummary For(int kind, string name)
			{
				if (!servers.TryGetValue((kind, name), out var summary))
				{
					summary = new ServerBandwidthServerSummary { Kind = kind, Name = name };
					servers.Add((kind, name), summary);
				}
				return summary;
			}

			foreach (var row in windows ?? Enumerable.Empty<ServerBandwidthWindowRow>())
			{
				var summary = For(row.Kind, row.Name);
				switch (row.Window)
				{
					case ServerBandwidthWindow.Hour:
						summary.LastHour = row.Totals;
						break;
					case ServerBandwidthWindow.Day:
						summary.LastDay = row.Totals;
						summary.Instances24h = row.Instances;
						break;
					case ServerBandwidthWindow.Month:
						summary.Last30Days = row.Totals;
						break;
				}
				summary.LastSeenUtc = Later(summary.LastSeenUtc, row.Totals.LastBucketUtc);
			}
			foreach (var row in currents ?? Enumerable.Empty<ServerBandwidthPointRow>())
			{
				var summary = For(row.Kind, row.Name);
				summary.Current = row.Rates;
				summary.LastSeenUtc = Later(summary.LastSeenUtc, row.Rates.BucketUtc);
			}
			foreach (var row in peaks ?? Enumerable.Empty<ServerBandwidthPointRow>())
			{
				For(row.Kind, row.Name).Peak = row.Rates;
			}

			report.Servers = servers.Values
				.OrderBy(s => s.Kind)
				.ThenBy(s => s.Name, StringComparer.Ordinal)
				.ToList();

			var series = (kindSeries ?? Enumerable.Empty<ServerBandwidthKindPointRow>()).ToList();
			foreach (int kind in ServerBandwidthKind.All)
			{
				var members = report.Servers.Where(s => s.Kind == kind).ToList();
				if (members.Count == 0)
				{
					continue;
				}
				var tier = Sum(kind, members);
				tier.Peak = Busiest(series.Where(p => p.Kind == kind).Select(p => p.Rates));
				report.Tiers.Add(tier);
			}

			var everything = SumSeries(series);
			report.Total = Sum(0, report.Servers);
			report.Total.Peak = Busiest(everything);

			if (!string.IsNullOrEmpty(scopeName))
			{
				report.Series = (serverSeries ?? Enumerable.Empty<ServerBandwidthRates>())
					.OrderBy(p => p.BucketUtc)
					.ToList();
			}
			else if (ServerBandwidthKind.IsValid(scopeKind))
			{
				report.Series = series.Where(p => p.Kind == scopeKind)
					.Select(p => p.Rates)
					.OrderBy(p => p.BucketUtc)
					.ToList();
			}
			else
			{
				report.Series = everything;
			}
			return report;
		}

		/// <summary>
		/// Adds servers together. A window is null when no member has it, and the current rate is
		/// the sum over the members that reported one: a silent server contributes nothing rather
		/// than a zero that would read as "idle".
		/// </summary>
		public static ServerBandwidthTierSummary Sum(int kind, IReadOnlyCollection<ServerBandwidthServerSummary> members)
		{
			var tier = new ServerBandwidthTierSummary { Kind = kind, Servers = members.Count };
			foreach (var member in members)
			{
				tier.LastHour = AddInto(tier.LastHour, member.LastHour);
				tier.LastDay = AddInto(tier.LastDay, member.LastDay);
				tier.Last30Days = AddInto(tier.Last30Days, member.Last30Days);
				if (member.Current != null)
				{
					tier.ServersReporting++;
					if (tier.Current == null)
					{
						tier.Current = member.Current.Clone();
					}
					else
					{
						tier.Current.Add(member.Current);
						if (member.Current.BucketUtc > tier.Current.BucketUtc)
						{
							tier.Current.BucketUtc = member.Current.BucketUtc;
						}
					}
				}
			}
			return tier;
		}

		/// <summary>The per-tier points summed per bucket, oldest first.</summary>
		public static List<ServerBandwidthRates> SumSeries(IEnumerable<ServerBandwidthKindPointRow> kindSeries)
		{
			var byBucket = new SortedDictionary<DateTime, ServerBandwidthRates>();
			foreach (var point in kindSeries ?? Enumerable.Empty<ServerBandwidthKindPointRow>())
			{
				if (byBucket.TryGetValue(point.Rates.BucketUtc, out var sum))
				{
					sum.Add(point.Rates);
				}
				else
				{
					byBucket.Add(point.Rates.BucketUtc, point.Rates.Clone());
				}
			}
			return byBucket.Values.ToList();
		}

		/// <summary>
		/// The point with the most UDP payload sent per second — the measured egress figure — or
		/// null when there are no points. Ties go to the earlier point.
		/// </summary>
		public static ServerBandwidthRates? Busiest(IEnumerable<ServerBandwidthRates> points)
		{
			ServerBandwidthRates? best = null;
			foreach (var point in (points ?? Enumerable.Empty<ServerBandwidthRates>()).OrderBy(p => p.BucketUtc))
			{
				if (best == null || point.UdpSentBps > best.UdpSentBps)
				{
					best = point;
				}
			}
			return best;
		}

		private static ServerBandwidthTotals? AddInto(ServerBandwidthTotals? sum, ServerBandwidthTotals? add)
		{
			if (add == null)
			{
				return sum;
			}
			sum ??= new ServerBandwidthTotals();
			sum.Add(add);
			return sum;
		}

		private static DateTime? Later(DateTime? a, DateTime? b)
		{
			if (!a.HasValue) return b;
			if (!b.HasValue) return a;
			return a.Value >= b.Value ? a : b;
		}
	}

	/// <summary>What one rollup pass did.</summary>
	public sealed class ServerBandwidthRollupResult
	{
		/// <summary>True when another panel held the rollup lock, so this pass did nothing.</summary>
		public bool Skipped { get; set; }

		/// <summary>Hour rows written (inserted or rewritten).</summary>
		public int HoursWritten { get; set; }
	}

	/// <summary>What one retention pass did.</summary>
	public sealed class ServerBandwidthPruneResult
	{
		/// <summary>True when another panel held the rollup lock at some point, so the pass stopped early.</summary>
		public bool Skipped { get; set; }

		/// <summary>Whole hours of minute rows folded into their hour row and deleted.</summary>
		public int MinuteHoursPruned { get; set; }

		/// <summary>Minute rows deleted.</summary>
		public long MinuteRowsDeleted { get; set; }

		/// <summary>Hour rows deleted for being older than the hour retention.</summary>
		public long HourRowsDeleted { get; set; }

		/// <summary>True when the pass stopped at its batch limit with more to delete.</summary>
		public bool MoreRemaining { get; set; }
	}
}
