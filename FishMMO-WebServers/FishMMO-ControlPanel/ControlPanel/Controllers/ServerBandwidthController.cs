using FishMMO.ControlPanel.Auth;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// What every server process sent and received: the Bandwidth page's one read.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Every figure says how it was obtained.</b> The application bytes and the UDP payload are
	/// MEASURED by each server's own transport (the managed WebTransport socket and msquic). The
	/// bytes on the wire are ESTIMATED — payload plus 28 bytes of IPv4 and UDP header per datagram
	/// — because no user-space counter sees an IP header; the estimate is computed as this reads,
	/// never stored, and is an upper bound because msquic over-counts the datagrams of a burst it
	/// sends in several batches. The field names carry <c>Estimated</c> so no client can present
	/// one as the other.
	/// </para>
	/// <para>
	/// <b>No data is null, never zero.</b> A server with no rows in a window has no figure for it;
	/// the page prints "no data". A server that ran and moved nothing has zeros, which is a
	/// different fact.
	/// </para>
	/// <para>
	/// At Operator, beside the server board it belongs with. A read, so the audit filter records
	/// it as <c>view.serverbandwidth.get</c> with its query string, except for the page's own
	/// timer, which marks itself as an automatic refresh.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/servers/bandwidth")]
	public sealed class ServerBandwidthController : ControllerBase
	{
		/// <summary>How often the page should re-read, in seconds: the servers write once a minute.</summary>
		private const int PollSeconds = 60;

		private readonly IServerBandwidthReportService reports;
		private readonly ILogger<ServerBandwidthController> log;

		public ServerBandwidthController(IServerBandwidthReportService reports, ILogger<ServerBandwidthController> log)
		{
			this.reports = reports;
			this.log = log;
		}

		/// <summary>The per-server and per-tier figures, and one chart.</summary>
		/// <param name="range"><c>hour</c> (the default), <c>day</c> or <c>month</c> (30 days): the chart's span and the peak's.</param>
		/// <param name="kind">Chart one tier — <c>login</c>, <c>world</c> or <c>scene</c> — or every server when empty.</param>
		/// <param name="name">Chart one server of <paramref name="kind"/>.</param>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> Get([FromQuery] string? range, [FromQuery] string? kind, [FromQuery] string? name)
		{
			if (!TryParseRange(range, out ServerBandwidthRange parsedRange))
			{
				return BadRequest(new { error = "The range must be hour, day or month." });
			}

			int scopeKind = 0;
			if (!string.IsNullOrWhiteSpace(kind) && !ServerBandwidthKind.TryParse(kind, out scopeKind))
			{
				return BadRequest(new { error = "The tier must be login, world or scene." });
			}

			string? scopeName = string.IsNullOrWhiteSpace(name) ? null : name;
			if (scopeName != null && scopeKind == 0)
			{
				return BadRequest(new { error = "A server is charted by its tier and its name." });
			}

			var result = await reports.FetchReportAsync(parsedRange, scopeKind, scopeName, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return DatabaseReplies.Failure(this, result, log, "Bandwidth could not be read.");
			}

			var r = result.Data;
			return Ok(new
			{
				generatedUtc = r.GeneratedUtc,
				range = RangeName(r.Range),
				resolutionSeconds = r.ResolutionSeconds,
				rangeStartUtc = r.RangeStartUtc,
				pollSeconds = PollSeconds,
				currentFreshSeconds = ServerBandwidthMath.CurrentFreshSeconds,
				ipUdpHeaderBytes = ServerBandwidthMath.IpV4UdpHeaderBytes,
				minuteRetentionDays = ServerBandwidthMath.MinuteRetentionDays,
				hourRetentionMonths = ServerBandwidthMath.HourRetentionMonths,
				scope = new
				{
					kind = r.ScopeKind == 0 ? null : ServerBandwidthKind.Name(r.ScopeKind),
					name = r.ScopeName,
				},
				latestRolledHourUtc = r.LatestRolledHourUtc,
				rollupBehind = r.RollupBehind,
				servers = r.Servers.Select(s => new
				{
					kind = ServerBandwidthKind.Name(s.Kind),
					name = s.Name,
					lastSeenUtc = s.LastSeenUtc,
					instances24h = s.Instances24h,
					current = Rates(s.Current),
					lastHour = Totals(s.LastHour),
					lastDay = Totals(s.LastDay),
					last30Days = Totals(s.Last30Days),
					peak = Rates(s.Peak),
				}),
				tiers = r.Tiers.Select(Tier),
				total = Tier(r.Total),
				series = r.Series.Select(Rates),
			});
		}

		private static object Tier(ServerBandwidthTierSummary t) => new
		{
			kind = t.Kind == 0 ? null : ServerBandwidthKind.Name(t.Kind),
			servers = t.Servers,
			serversReporting = t.ServersReporting,
			current = Rates(t.Current),
			lastHour = Totals(t.LastHour),
			lastDay = Totals(t.LastDay),
			last30Days = Totals(t.Last30Days),
			peak = Rates(t.Peak),
		};

		/// <summary>A window's totals, or null for "no data".</summary>
		private static object? Totals(ServerBandwidthTotals? t) => t == null ? null : new
		{
			samples = t.Samples,
			measuredSeconds = t.IntervalMs / 1000.0,
			appSentBytes = t.AppSentBytes,
			appRecvBytes = t.AppRecvBytes,
			udpSentBytes = t.UdpSentBytes,
			udpRecvBytes = t.UdpRecvBytes,
			udpSentDatagrams = t.UdpSentDatagrams,
			udpRecvDatagrams = t.UdpRecvDatagrams,
			wireSentBytesEstimated = t.WireSentBytesEstimated,
			wireRecvBytesEstimated = t.WireRecvBytesEstimated,
			sessionsOpened = t.SessionsOpened,
			connectionsRefused = t.ConnectionsRefused,
			handshakeFailures = t.HandshakeFailures,
			sessionsActiveMax = t.SessionsActiveMax,
			lastBucketUtc = t.LastBucketUtc,
		};

		/// <summary>Rates at one point, or null for "no data".</summary>
		/// <remarks>
		/// A rate is never NaN here (every stored interval is positive), but System.Text.Json
		/// throws on one, so a non-finite value would take the whole page down rather than one
		/// cell; <see cref="Finite"/> makes it a null cell instead.
		/// </remarks>
		private static object? Rates(ServerBandwidthRates? p) => p == null ? null : new
		{
			bucketUtc = p.BucketUtc,
			appSentBps = Finite(p.AppSentBps),
			appRecvBps = Finite(p.AppRecvBps),
			udpSentBps = Finite(p.UdpSentBps),
			udpRecvBps = Finite(p.UdpRecvBps),
			wireSentBpsEstimated = Finite(p.WireSentBpsEstimated),
			wireRecvBpsEstimated = Finite(p.WireRecvBpsEstimated),
			datagramsSentPs = Finite(p.DatagramsSentPs),
			datagramsRecvPs = Finite(p.DatagramsRecvPs),
			sessionsActive = p.SessionsActive,
		};

		private static double? Finite(double value) => double.IsFinite(value) ? value : null;

		private static bool TryParseRange(string? text, out ServerBandwidthRange range)
		{
			switch ((text ?? string.Empty).Trim().ToLowerInvariant())
			{
				case "":
				case "hour":
					range = ServerBandwidthRange.Hour;
					return true;
				case "day":
					range = ServerBandwidthRange.Day;
					return true;
				case "month":
					range = ServerBandwidthRange.Month;
					return true;
				default:
					range = ServerBandwidthRange.Hour;
					return false;
			}
		}

		private static string RangeName(ServerBandwidthRange range) => range switch
		{
			ServerBandwidthRange.Day => "day",
			ServerBandwidthRange.Month => "month",
			_ => "hour",
		};
	}
}
