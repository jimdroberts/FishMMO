using System.Diagnostics;
using System.Reflection;
using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Monitoring.Diagnostics;
using FishMMO.Database.Npgsql.Monitoring.Health;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// The panel's own health, and the shared infrastructure it sits on.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What this page is for, and what it deliberately is not.</b> Three other screens already
	/// answer "is the shard up": the server board owns game server processes, their pulse and
	/// their population; daemon hosts owns supervised OS processes and the daemons watching them;
	/// the queues page owns email queue depth. Repeating any of those here would produce a second
	/// number for the same fact, and two screens that disagree by one poll interval is worse than
	/// one screen. What is left over — and what nothing else in the panel can tell you — is the
	/// state of <em>this process</em> and of the database every other page reads through. That is
	/// all this endpoint returns.
	/// </para>
	/// <para>
	/// <b>The pool figures belong to this process alone.</b> <c>ConnectionPoolMetrics</c> is a
	/// field on the singleton factory this host built at startup, fed by an EF connection
	/// interceptor. It counts connections the Control Panel opened. Each game server runs its own
	/// pool against the same PostgreSQL and is invisible here, and so is PostgreSQL's own
	/// <c>max_connections</c> ceiling. Saying "the pool" without saying whose would invite an
	/// operator to read a healthy panel as a healthy shard.
	/// </para>
	/// <para>
	/// <b>No secret, connection string or password can leave this endpoint.</b> That is enforced
	/// by what is fetched, not by care at the point of serialization. This controller never takes
	/// a dependency on <c>NpgsqlDbConfiguration</c>, which is the only type here that holds a
	/// connection string. Two diagnostic strings that <em>could</em> carry credential material —
	/// the driver's own connect-failure message from <c>TryConnectAsync</c>, and the exception
	/// text behind an unavailable schema check — are deliberately not projected at all: they are
	/// arbitrary exception text from a database driver, and no amount of reading them today
	/// guarantees what a future Npgsql release puts in one. Both are written to the application
	/// log instead, and the payload says so, so an operator knows the detail exists and where it
	/// is rather than believing there was none. Every other string sent from here is either a
	/// literal authored in this repository or a name from a closed set (a migration identifier,
	/// a <c>{Service}.{Method}</c> operation name, an audit action id).
	/// </para>
	/// <para>
	/// <b>Reads here are not audited.</b> Glancing at a health page is not an operator action,
	/// and a page polled every ten seconds by every open tab would write more audit rows than
	/// every real action combined — burying the log this same endpoint reports on. It is gated at
	/// Operator and logged to the application log, exactly as the audit log's own reader is.
	/// </para>
	/// <para>
	/// <b>This endpoint measures by acting.</b> The round trip is a real connection, so the page
	/// contributes to the very counters below it: each poll adds one to connections opened and
	/// closed. That is worth the honesty of a latency figure that was actually observed rather
	/// than one cached from somebody else's query.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/platform/health")]
	public sealed class PlatformHealthController : ControllerBase
	{
		/// <summary>How often the page should re-read, in seconds. Reported so one value governs both ends.</summary>
		private const int PollSeconds = 10;

		/// <summary>Pool utilisation at which the shared health assessment starts warning.</summary>
		private const double PoolWarnPercent = 70.0;

		/// <summary>Pool utilisation the shared health assessment calls critical.</summary>
		private const double PoolCriticalPercent = 85.0;

		/// <summary>
		/// How long the audit log may go without a row before the page says so.
		/// </summary>
		/// <remarks>
		/// Not an error — a quiet shard genuinely produces no operator actions overnight. It is
		/// the one symptom of a broken recording path, which otherwise has none: the writes fail
		/// silently by design, because refusing an action because its audit row would not save is
		/// a worse failure than the missing row.
		/// </remarks>
		private const int AuditQuietHours = 24;

		/// <summary>How many operations the slowest-operations list carries.</summary>
		/// <remarks>
		/// Five. It exists to answer "what is making the panel slow", which is a question about
		/// the worst offender; a full listing of every tracked operation would be a profiler, and
		/// this is a status page.
		/// </remarks>
		private const int SlowestOperations = 5;

		/// <summary>
		/// When this process started, resolved once.
		/// </summary>
		/// <remarks>
		/// The OS process start time rather than a timestamp captured on first use, so uptime
		/// counts from the host starting and not from the first operator who opened this page.
		/// </remarks>
		private static readonly DateTime? ProcessStartUtc = ResolveProcessStartUtc();

		/// <summary>The build this host is running, resolved once.</summary>
		private static readonly string BuildVersion = ResolveBuildVersion();

		private readonly NpgsqlDbContextFactory factory;
		private readonly IAdminAuditService audit;
		private readonly TotpKeyProvider totpKeys;
		private readonly PanelRegistrationOptions registration;
		private readonly IWebHostEnvironment environment;
		private readonly ILogger<PlatformHealthController> log;

		public PlatformHealthController(
			NpgsqlDbContextFactory factory,
			IAdminAuditService audit,
			TotpKeyProvider totpKeys,
			PanelRegistrationOptions registration,
			IWebHostEnvironment environment,
			ILogger<PlatformHealthController> log)
		{
			this.factory = factory;
			this.audit = audit;
			this.totpKeys = totpKeys;
			this.registration = registration;
			this.environment = environment;
			this.log = log;
		}

		/// <summary>Everything the page shows, in one read.</summary>
		/// <remarks>
		/// Deliberately one endpoint rather than four. Each section answers a different question,
		/// but they are read together to decide one thing — whether the panel is healthy — and a
		/// page assembled from four independently timed reads shows four different instants.
		/// </remarks>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> Get()
		{
			CancellationToken cancellationToken = HttpContext.RequestAborted;
			DateTime now = DateTime.UtcNow;

			log.LogInformation("Platform health read by '{Actor}'.", User.Identity?.Name);

			object database = await ReadDatabaseAsync(cancellationToken);
			object auditLog = await ReadAuditAsync(now, cancellationToken);

			return Ok(new
			{
				generatedUtc = now,
				pollSeconds = PollSeconds,
				database,
				auditLog,
				panel = ReadPanel(now),

				/* Named, with the page that owns each, rather than silently absent. An operator
				 * who cannot find population here has to be told it is one click away and not
				 * that nobody measured it — the same reason the dashboard names what it cannot
				 * show instead of leaving a gap. */
				ownedElsewhere = new[]
				{
					new
					{
						what = "Game server processes, their pulse, lock state and population",
						where = "the server board",
						route = (string?)"servers/board",
					},
					new
					{
						what = "Supervised OS processes, the daemons watching them, and restarts",
						where = "daemon hosts",
						route = (string?)"daemon/hosts",
					},
					new
					{
						what = "Email queue depth and failed sends",
						where = "the Queues page, under Platform",
						// No route: that page is not wired yet, and a link to a page that is not
						// built teaches an operator to ignore a dead end.
						route = (string?)null,
					},
					new
					{
						what = "The audit rows themselves — who did what, to what, and why",
						where = "the audit log",
						route = (string?)"admin/audit",
					},
					new
					{
						what = "Which deployment secrets exist and how old they are",
						where = "Secrets",
						route = (string?)"platform/secrets",
					},
				},

				/* One genuinely useful figure this endpoint cannot produce, named rather than
				 * quietly dropped or — far worse — estimated from something adjacent. */
				notWired = new[]
				{
					new
					{
						what = "Live panel sessions, and how many are waiting on a second factor",
						why = "IWebSessionService can list the sessions for one named account and " +
							"count nothing else. A deployment-wide figure needs a counting method " +
							"on that service; there is no way to derive it from what exists, and a " +
							"number assembled from anything else here would be a guess presented " +
							"as a measurement.",
					},
				},
			});
		}

		/* ── Database ────────────────────────────────────────────────── */

		/// <summary>Reachability, the pool, the schema, and what the panel's own queries cost.</summary>
		private async Task<object> ReadDatabaseAsync(CancellationToken cancellationToken)
		{
			// A real connection, timed. See the remarks on this controller for why the reason is
			// logged rather than returned.
			var stopwatch = Stopwatch.StartNew();
			var (connected, failureReason) = await factory.TryConnectAsync(cancellationToken);
			stopwatch.Stop();

			if (!connected)
			{
				log.LogError(
					"Platform health: the database is unreachable from the Control Panel. Reason: {Reason}",
					failureReason ?? "not reported");
			}

			/* Ordered deliberately: the schema check opens its own connection, so it runs only
			 * when the first one succeeded. Asking for pending migrations across a dead
			 * connection produces "could not read migration history", which reads as a second,
			 * separate fault and sends somebody looking for one. */
			SchemaValidationResult? schema = connected
				? await factory.ValidateSchemaAsync(cancellationToken)
				: null;

			if (schema != null && schema.UnavailableReason != null)
			{
				log.LogWarning(
					"Platform health: the schema check could not run. Reason: {Reason}",
					schema.UnavailableReason);
			}

			PoolHealthResult pool = factory.GetConnectionPoolHealth(PoolWarnPercent, PoolCriticalPercent);

			return new
			{
				reachable = connected,
				roundTripMs = connected ? (double?)Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1) : null,

				/* A fixed sentence and a pointer, never the driver's text. The reason is real and
				 * it matters — a rejected password and an unreachable host are hours apart in
				 * remedy — which is exactly why it is written somewhere an operator can read it
				 * rather than dropped. */
				unreachableHint = connected
					? null
					: "The Control Panel could not open a connection. The driver's reason is in this host's " +
						"application log, at Error, timestamped with this read — it is not sent to the browser " +
						"because a connection failure message is arbitrary text from the database driver and " +
						"this panel will not put driver text one serialization mistake away from a credential.",

				pool = new
				{
					maxSize = pool.MaxPoolSize,
					active = pool.ActiveConnections,
					utilizationPercent = pool.UtilizationPercent < 0
						? (double?)null
						: Math.Round(pool.UtilizationPercent, 1),
					peakActive = pool.PeakActiveConnections,
					opened = factory.PoolMetrics.TotalConnectionsCreated,
					closed = factory.PoolMetrics.TotalConnectionsDisposed,
					connectionErrors = pool.ConnectionErrors,
					exhaustionEvents = pool.PoolExhaustionCount,

					/* Contexts this factory has handed out and not seen disposed. It moves with
					 * requests in flight rather than with connections, so a figure well above
					 * `active` on an idle panel is the shape a context leak makes — and a leak is
					 * what eventually exhausts the pool. */
					openContexts = factory.ActiveContextCount,

					warnAtPercent = PoolWarnPercent,
					criticalAtPercent = PoolCriticalPercent,
					status = pool.Status.ToString(),
					// Authored in ConnectionPoolMetrics.GetPoolHealth; numbers interpolated into
					// a literal. No caller-supplied or driver-supplied text reaches these.
					message = pool.Message,
					recommendedAction = pool.RecommendedAction,
					requiresAction = pool.RequiresAction,
					scope = "This Control Panel process only. Every game server holds its own pool against the " +
						"same database, and PostgreSQL's own connection ceiling is above all of them; neither is " +
						"visible from here.",
				},

				schema = schema == null
					? new
					{
						checkRan = false,
						upToDate = false,
						unavailable = true,
						pendingMigrations = Array.Empty<string>(),
						note = "Not attempted: the database did not answer, so a migration check would only " +
							"report the same outage a second time under a different name.",
					}
					: schema.UnavailableReason != null
						? new
						{
							checkRan = false,
							upToDate = false,
							unavailable = true,
							pendingMigrations = Array.Empty<string>(),
							note = "The migration history could not be read. The reason is in this host's " +
								"application log, at Warning.",
						}
						: new
						{
							checkRan = true,
							upToDate = schema.IsUpToDate,
							unavailable = false,
							pendingMigrations = schema.PendingMigrations,
							/* Narrower than "the schema is correct", and the page has to say so:
							 * an entity changed with no migration generated leaves nothing pending
							 * and a schema that is quietly wrong. See SchemaValidationResult. */
							note = "Checks for migrations that exist and have not been applied. It cannot see an " +
								"entity that was changed without a migration being generated for it, which leaves " +
								"nothing pending and a schema that is wrong anyway.",
						},

				queries = ReadQueryMetrics(),
			};
		}

		/// <summary>What the panel's own database calls have cost since this host started.</summary>
		/// <remarks>
		/// Every service call in the panel runs through <c>BaseService</c>, which times it and
		/// records it under a <c>{Service}.{Method}</c> name. These are this process's numbers and
		/// they reset when it restarts, which is said in the payload: "no slow queries" after a
		/// restart two minutes ago is not the same claim as after a fortnight of uptime.
		/// </remarks>
		private object ReadQueryMetrics()
		{
			QueryPerformanceTracker tracker = factory.PerformanceTracker;
			if (tracker == null || !tracker.IsEnabled || tracker.Level == TrackingLevel.None)
			{
				return new
				{
					trackingEnabled = false,
					trackingLevel = tracker == null ? "None" : tracker.Level.ToString(),
					slowQueryThresholdMs = 0d,
					operationsTracked = 0,
					totalExecutions = 0L,
					failedExecutions = 0L,
					slowExecutions = 0L,
					slowest = Array.Empty<object>(),
				};
			}

			var all = tracker.GetAllMetrics();
			double threshold = tracker.Configuration?.SlowQueryThresholdMs ?? 0d;

			return new
			{
				trackingEnabled = true,
				trackingLevel = tracker.Level.ToString(),
				slowQueryThresholdMs = threshold,
				operationsTracked = all.Count,
				totalExecutions = all.Sum(m => m.TotalExecutions),
				failedExecutions = all.Sum(m => m.FailedExecutions),
				slowExecutions = all.Sum(m => m.SlowQueryCount),

				/* By average, not by worst case. One cold start after a deployment gives almost
				 * every operation a five-second maximum, so ordering by max would rank the list by
				 * which query happened to run first. */
				slowest = all
					.Where(m => m.TotalExecutions > 0)
					.OrderByDescending(m => m.AverageMs)
					.Take(SlowestOperations)
					.Select(m => new
					{
						// "CharacterService.SearchAdminAsync" — a type and member name from this
						// repository, never a SQL statement and never a parameter value.
						operation = m.OperationName,
						executions = m.TotalExecutions,
						failed = m.FailedExecutions,
						slow = m.SlowQueryCount,
						averageMs = Math.Round(m.AverageMs, 1),
						p95Ms = Math.Round(m.P95Ms, 1),
						maxMs = Math.Round(m.MaxMs, 1),
					}),
			};
		}

		/* ── Audit log ───────────────────────────────────────────────── */

		/// <summary>
		/// Whether the audit log is still receiving rows.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the one thing here that nothing else in the panel or the game can report. Audit
		/// writes are deliberately non-blocking: an action is not refused because its record failed
		/// to save, because the alternative is a shard that stops working when one table does. The
		/// cost of that choice is that a broken recording path is <em>silent</em> — every screen
		/// keeps working, every action keeps succeeding, and the evidence simply stops
		/// accumulating. The age of the newest row is the only symptom.
		/// </para>
		/// <para>
		/// Counts and one timestamp, never rows: the audit page owns the rows, and an actor name
		/// or a reason repeated here would be a second, worse audit view. Even the newest entry is
		/// reduced to when it happened and which action it was.
		/// </para>
		/// </remarks>
		private async Task<object> ReadAuditAsync(DateTime now, CancellationToken cancellationToken)
		{
			// Page of one: the count is the answer and the single row carries the timestamp.
			var newest = await audit.SearchAsync(
				new AdminAuditQuery { Page = 1, PageSize = 1 },
				cancellationToken);

			if (!newest.IsSuccess)
			{
				return new
				{
					readable = false,
					totalEntries = 0,
					entriesRecently = 0,
					windowHours = AuditQuietHours,
					lastEntryUtc = (DateTime?)null,
					lastEntryAction = (string?)null,
					lastEntryAgeSeconds = (double?)null,
					quiet = false,
					note = "The audit log could not be read. That is not the same as an empty log, and it " +
						"says nothing about whether rows are still being written.",
				};
			}

			var recent = await audit.SearchAsync(
				new AdminAuditQuery { Page = 1, PageSize = 1, FromUtc = now.AddHours(-AuditQuietHours) },
				cancellationToken);

			AdminAuditData? last = newest.Data.Items.Count > 0 ? newest.Data.Items[0] : null;
			double? ageSeconds = last == null
				? null
				: Math.Round((now - last.OccurredUtc).TotalSeconds, 1);

			return new
			{
				readable = true,
				totalEntries = newest.Data.TotalCount,
				entriesRecently = recent.IsSuccess ? recent.Data.TotalCount : (int?)null,
				windowHours = AuditQuietHours,
				lastEntryUtc = last?.OccurredUtc,
				// A dotted identifier from a closed set of constants, such as "character.rename".
				lastEntryAction = last?.Action,
				lastEntryAgeSeconds = ageSeconds,

				/* An empty log is quiet too. A deployment where nobody has ever used a privileged
				 * action has an audit path that may be perfectly healthy and may be completely
				 * broken, and the two are indistinguishable from here — so the flag is raised and
				 * the page is left to say which reading applies. */
				quiet = last == null || ageSeconds > AuditQuietHours * 3600.0,
				note = "Every privileged action in the panel and in-game writes one row here. Writes never " +
					"block the action that caused them, so a broken recording path has no symptom except " +
					"this timestamp going stale.",
			};
		}

		/* ── This process ────────────────────────────────────────────── */

		/// <summary>Facts about the running host that no other page reports.</summary>
		/// <remarks>
		/// Not duplicated by daemon hosts: that page says whether a supervisor is watching a
		/// process and whether it is up, which is a different question from which build is
		/// running, which environment it thinks it is in, and whether it managed to load the key
		/// two-factor depends on.
		/// </remarks>
		private object ReadPanel(DateTime now)
		{
			return new
			{
				version = BuildVersion,
				environment = environment.EnvironmentName,
				startedUtc = ProcessStartUtc,
				uptimeSeconds = ProcessStartUtc.HasValue
					? (double?)Math.Round((now - ProcessStartUtc.Value).TotalSeconds, 0)
					: null,

				/* Both of these change who can get an account, and neither is visible anywhere
				 * else in the panel. Auto-verify in particular is a bypass that is supposed to be
				 * impossible in production — showing it beside the environment name is what makes
				 * "Production" and "accounts skip verification" contradict each other on screen
				 * rather than in a configuration file nobody opens. */
				registrationEnabled = registration.RegistrationEnabled,
				autoVerifyAccounts = registration.AutoVerifyAccounts,

				/* Presence of the row is the Secrets page's job. This is the different, later
				 * question: whether THIS process decoded it into a usable key. A row that is
				 * present but malformed passes that page and fails here, and the symptom without
				 * this line is every authenticator code being rejected for no stated reason. */
				twoFactorKeyLoaded = totpKeys.IsAvailable,
				// Authored in TotpMasterKek.TryDecode: names the row and the fix, carries no key
				// material and no value — only, at most, a decoded byte length.
				twoFactorKeyError = totpKeys.IsAvailable ? null : totpKeys.LoadError,
			};
		}

		/* ── Resolution helpers ──────────────────────────────────────── */

		/// <summary>The OS process start time, or null when the platform will not give it.</summary>
		private static DateTime? ResolveProcessStartUtc()
		{
			try
			{
				using var process = Process.GetCurrentProcess();
				return process.StartTime.ToUniversalTime();
			}
			catch
			{
				// Restricted hosts can refuse this. Uptime is a nicety; nothing else depends on it.
				return null;
			}
		}

		/// <summary>The informational version of the running assembly, falling back to its file version.</summary>
		private static string ResolveBuildVersion()
		{
			Assembly assembly = typeof(PlatformHealthController).Assembly;
			string? informational = assembly
				.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
			if (!string.IsNullOrWhiteSpace(informational))
			{
				return informational;
			}
			return assembly.GetName().Version?.ToString() ?? "unknown";
		}
	}
}
