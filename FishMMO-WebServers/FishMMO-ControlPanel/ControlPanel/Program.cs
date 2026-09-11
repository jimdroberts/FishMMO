using System.Security.Claims;
using System.Threading.RateLimiting;
using FishMMO.Auth.Core;
using FishMMO.ControlPanel;
using FishMMO.ControlPanel.Auth;
using FishMMO.ControlPanel.Services;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Services;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.WebShared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Force the .NET host to recognise FISHMMO_ENVIRONMENT, as IpFetchServer,
// Patcher and WebGLServer all do, so one variable configures every web host.
string? fishEnv = Environment.GetEnvironmentVariable("FISHMMO_ENVIRONMENT");
if (!string.IsNullOrWhiteSpace(fishEnv))
{
	Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", fishEnv);
	Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", fishEnv);
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
	// Bundled defaults from FishMMO-Setup (copied to output directory).
	ContentRootPath = AppContext.BaseDirectory,
	// The single-page app is authored under the project's wwwroot/. The Web SDK's
	// static web asset manifest, which lands next to the assembly, maps it back
	// to the source tree for `dotnet run` and copies it for `dotnet publish`.
	Args = args,
});

// Working-directory overrides take precedence over bundled defaults.
string env = builder.Environment.EnvironmentName;
builder.Configuration.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"), optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile(Path.Combine(Directory.GetCurrentDirectory(), $"appsettings.{env}.json"), optional: true, reloadOnChange: true);

// Kestrel binds loopback only, matching IpFetchServer, Patcher and WebGLServer.
// TLS terminates at the NGINX edge; see FishMMO-Setup/nginx.conf.
string configuredPort = builder.Configuration["WebServer:HttpPort"] ?? "8100";
if (!int.TryParse(configuredPort, out int httpPort) || httpPort <= 0 || httpPort > 65535)
{
	throw new InvalidOperationException($"WebServer:HttpPort '{configuredPort}' is not a valid TCP port.");
}
builder.WebHost.ConfigureKestrel(options =>
{
	options.ListenLocalhost(httpPort);
	// Admin surface: nothing legitimate uploads more than a small JSON body.
	options.Limits.MaxRequestBodySize = 2 * 1024 * 1024;
});

ValidateNpgsqlSslMode(builder.Configuration, builder.Environment);

// ── Database ────────────────────────────────────────────────────────────────
// Explicit factory constructor: AddSingleton<NpgsqlDbContextFactory>() is ambiguous
// between .ctor(IConfiguration) and .ctor(NpgsqlDbConfiguration), which crashes the
// host at startup rather than failing at registration. IpFetchServer carries the
// same note for the same reason.
builder.Services.AddSingleton(new NpgsqlDbConfiguration(builder.Configuration));
builder.Services.AddSingleton(sp => new NpgsqlDbContextFactory(sp.GetRequiredService<NpgsqlDbConfiguration>()));
builder.Services.AddSingleton<INpgsqlDbContextFactory>(sp => sp.GetRequiredService<NpgsqlDbContextFactory>());

builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IAuthTokenService, AuthTokenService>();
builder.Services.AddScoped<IWebSessionService, WebSessionService>();
builder.Services.AddScoped<ITwoFactorRecoveryCodeService, TwoFactorRecoveryCodeService>();
builder.Services.AddScoped<IEmailQueueService, EmailQueueService>();
builder.Services.AddScoped<IDeploymentSecretService, DeploymentSecretService>();
builder.Services.AddScoped<IKickRequestService, KickRequestService>();
builder.Services.AddScoped<IAdminAuditService, AdminAuditService>();
builder.Services.AddScoped<ISupportTicketService, SupportTicketService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IServerBoardService, ServerBoardService>();
builder.Services.AddScoped<IQueueBoardService, QueueBoardService>();
builder.Services.AddScoped<ISocialBoardService, SocialBoardService>();
builder.Services.AddScoped<IMaintenanceService, MaintenanceService>();
builder.Services.AddScoped<IPasswordResetTokenService, PasswordResetTokenService>();
builder.Services.AddScoped<PasswordResetService>();
/* Advances maintenance windows without a viewer. A window's actuation is finished the moment
 * it starts — the deadline is on each server's own row — but the retry of any write the start
 * did not manage, and every status after it, happen only when something reads. */
builder.Services.AddHostedService<MaintenanceAdvanceService>();

/* Outbound mail. Account creation still happens on the LoginServer too, and both it and this
 * panel ENQUEUE — but only this process drains the queue and sends. Doing it here keeps
 * blocking network I/O off a game server's tick, and the LoginServer's own drain is off by
 * default (Smtp:DrainQueue) so there is exactly one sender. */
builder.Services.AddSingleton<ISmtpSender, SmtpSender>();
builder.Services.AddHostedService<EmailQueueDrainService>();
builder.Services.AddScoped<IDaemonService, DaemonService>();
builder.Services.AddScoped<IWorldServerService, WorldServerService>();
builder.Services.AddScoped<ISceneServerService, SceneServerService>();

// ── Panel services ──────────────────────────────────────────────────────────
// The auto-verify bypass mirrors AccountVerificationPolicy: honoured only outside
// Production, so a development configuration that reaches a production host cannot
// re-enable it.
var registrationOptions = new PanelRegistrationOptions
{
	AutoVerifyAccounts = !builder.Environment.IsProduction() &&
						 builder.Configuration.GetValue("Panel:AutoVerifyAccounts", true),
	RegistrationEnabled = builder.Configuration.GetValue("Panel:RegistrationEnabled", true),
	/* Handing the reset code straight back to the caller is what makes recovery testable on a
	 * machine with no mail sender. It is gated the same way auto-verification is, and for the
	 * same reason: the environment check is the hard gate, so a development configuration that
	 * reaches a production host cannot switch it back on. PasswordResetService checks the
	 * environment itself too, so this flag can only ever narrow. */
	ExposeResetCodes = !builder.Environment.IsProduction() &&
					   builder.Configuration.GetValue("Panel:ExposeResetCodes", true),
};
builder.Services.AddSingleton(registrationOptions);

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<TotpKeyProvider>();
builder.Services.AddScoped<AuditWriter>();
builder.Services.AddScoped<AuditScope>();
builder.Services.AddScoped<AuditActionFilter>();
builder.Services.AddScoped<AccountRegistrationService>();
builder.Services.AddScoped<TwoFactorService>();
builder.Services.AddScoped<SelfServiceService>();
builder.Services.AddScoped<ICharacterService, CharacterService>();
builder.Services.AddScoped<PanelSessionManager>();

// A singleton, because one SRP exchange spans two requests and cannot capture a
// scoped database service; it opens its own scope per call instead.
builder.Services.AddSingleton(sp => new SrpLoginService(
	new ScopedAccountService(sp),
	sp.GetRequiredService<PanelRegistrationOptions>(),
	sp.GetRequiredService<ILogger<SrpLoginService>>()));

// ── Authentication and authorization ────────────────────────────────────────
builder.Services.AddAuthentication(PanelAuthenticationHandler.SchemeName)
	.AddScheme<AuthenticationSchemeOptions, PanelAuthenticationHandler>(
		PanelAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
	/* A route with no policy attribute is DENIED, not permitted. This one line closes
	 * the hole the old CMS scaffold shipped with: it called UseAuthorization while
	 * registering no authentication and no policies, and carried no [Authorize]
	 * attributes, so every administrative route was reachable anonymously. */
	options.FallbackPolicy = new AuthorizationPolicyBuilder()
		.RequireAuthenticatedUser()
		.AddRequirements(new AccessLevelRequirement((byte)AccessLevel.Admin, requireTwoFactor: true, requireStepUp: false))
		.Build();

	// A session that has proven a password but not a second factor. It reaches the
	// two-factor endpoints and nothing else.
	options.AddPolicy(PanelPolicies.TwoFactorPending, policy => policy.RequireAuthenticatedUser());

	options.AddPolicy(PanelPolicies.Self, policy => policy
		.AddRequirements(new AccessLevelRequirement((byte)AccessLevel.Player, requireTwoFactor: true, requireStepUp: false)));

	options.AddPolicy(PanelPolicies.Support, policy => policy
		.AddRequirements(new AccessLevelRequirement((byte)AccessLevel.GameMaster, requireTwoFactor: true, requireStepUp: false)));

	options.AddPolicy(PanelPolicies.SupportStepUp, policy => policy
		.AddRequirements(new AccessLevelRequirement((byte)AccessLevel.GameMaster, requireTwoFactor: true, requireStepUp: true)));

	options.AddPolicy(PanelPolicies.Operator, policy => policy
		.AddRequirements(new AccessLevelRequirement((byte)AccessLevel.Admin, requireTwoFactor: true, requireStepUp: false)));

	options.AddPolicy(PanelPolicies.OperatorStepUp, policy => policy
		.AddRequirements(new AccessLevelRequirement((byte)AccessLevel.Admin, requireTwoFactor: true, requireStepUp: true)));
});
builder.Services.AddSingleton<IAuthorizationHandler, AccessLevelHandler>();

// ── Transport hardening, matching the sibling web hosts ─────────────────────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
	options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
	// Single hop: NGINX is the only trusted proxy. Further values are attacker-controlled
	// and would shift the client address, breaking per-IP rate limiting.
	options.ForwardLimit = 1;
	MiddlewareExtensions.ConfigureTrustedProxies(options, builder.Configuration, builder.Environment);
});

builder.Services.AddRateLimiter(options =>
{
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

	options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
		RateLimitPartition.GetTokenBucketLimiter(httpContext.GetClientIpKey(), _ => new TokenBucketRateLimiterOptions
		{
			TokenLimit = 60,
			TokensPerPeriod = 20,
			ReplenishmentPeriod = TimeSpan.FromSeconds(1),
			QueueLimit = 0,
			AutoReplenishment = true,
		}));

	/* The auth surface partitions on IP and on the submitted subject, so credential
	 * stuffing can be spread neither across usernames from one address nor across
	 * addresses for one account. NGINX layers a tighter zone in front of this. */
	options.AddPolicy("Auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
		$"{httpContext.GetClientIpKey()}|{httpContext.Request.Headers["X-Panel-Subject"]}",
		_ => new FixedWindowRateLimiterOptions
		{
			PermitLimit = 20,
			Window = TimeSpan.FromMinutes(1),
			QueueLimit = 0,
		}));

	/* Registration is rare and expensive, so it gets its own much tighter bucket.
	 * This is the panel's counterpart to the account creation system's per-IP
	 * cooldown and its global hourly cap. */
	options.AddPolicy("Register", httpContext => RateLimitPartition.GetFixedWindowLimiter(
		httpContext.GetClientIpKey(),
		_ => new FixedWindowRateLimiterOptions
		{
			PermitLimit = 5,
			Window = TimeSpan.FromMinutes(10),
			QueueLimit = 0,
		}));
});

builder.Services.AddControllers(options =>
{
	/* The project has nullable reference types on, which makes MVC treat every non-nullable
	 * string on a request model as required and answer a missing one with its own model-state
	 * 400 before the action runs. Every controller here already validates its own input and
	 * returns a message written for the operator reading it, so the framework's version is
	 * both redundant and worse: it turned an optional "scene name" on a partial character
	 * edit into "The BindScene field is required". */
	options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;

	/* Every privileged write is recorded by this filter, whether or not its author added
	 * anything. See AuditActionFilter: the in-game commands are audited at the single access
	 * gate, and HTTP endpoints now have the same property, so coverage is structural rather
	 * than a matter of remembering. */
	options.Filters.Add<AuditActionFilter>();
})
	.AddJsonOptions(o =>
	{
		o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;

		/* Every timestamp goes out with an explicit UTC designator. Without this the columns,
		 * which are `timestamp without time zone` holding UTC, serialize with no designator and
		 * the browser parses them as LOCAL time — silently shifting every date in the panel by
		 * the operator's offset. See UtcDateTimeConverter. */
		o.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
		o.JsonSerializerOptions.Converters.Add(new NullableUtcDateTimeConverter());
	});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

/* Refuses to start, outside production, if a privileged write endpoint does not declare what
 * it records. The filter above already guarantees such an endpoint IS recorded; this is about
 * it being recorded under a stable name, and about the omission surfacing at the first run
 * rather than as a gap noticed months later. */
AuditCoverage.Verify(app.Services, app.Environment, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AuditCoverage"));

/* The TOTP master KEK, loaded once before the host serves anything. It is the same
 * deployment secret the LoginServer loads under the same row key, which is what makes
 * an authenticator enrolled in the game verify here and the other way round. */
using (var scope = app.Services.CreateScope())
{
	var secrets = scope.ServiceProvider.GetRequiredService<IDeploymentSecretService>();
	var totpKeys = app.Services.GetRequiredService<TotpKeyProvider>();
	if (!await totpKeys.LoadAsync(secrets))
	{
		if (app.Environment.IsProduction())
		{
			throw new InvalidOperationException(
				$"The TOTP master KEK is required in production. {totpKeys.LoadError}");
		}
		app.Logger.LogWarning(
			"TOTP master KEK unavailable — two-factor cannot be verified. {Error}", totpKeys.LoadError);
	}
}

if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

// A non-cleared exception handler runs before everything else so an unhandled
// exception surfaces as an opaque 500 rather than a stack trace.
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
	ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
	var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
	if (feature?.Error != null)
	{
		app.Logger.LogError(feature.Error, "Unhandled exception in the Control Panel request pipeline.");
	}
	ctx.Response.ContentType = "application/json";
	await ctx.Response.WriteAsync("{\"error\":\"Internal Server Error\"}");
}));

app.UseForwardedHeaders();
app.UseFishMMOSecurityHeaders(app.Environment);

// No UseHttpsRedirection: Kestrel is loopback-only behind NGINX, and redirecting
// would break `dotnet run` for anyone opening the panel locally.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseRouting();
app.UseAuthentication();

/* BEFORE UseAuthorization, and that ordering is the whole point. Authorization
 * short-circuits on failure and never calls its next, so a middleware registered after it
 * is not entered at all when it refuses — which is how the 403-to-428 rewrite silently did
 * nothing, and every lapsed step-up became a dead end instead of a prompt. Wrapping it also
 * lets the refusal be recorded, which an action filter cannot do because the action never
 * runs. */
app.UseMiddleware<PrivilegedRequestMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/healthz", async (HttpContext ctx) =>
{
	bool db = false;
	try
	{
		var factory = ctx.RequestServices.GetService<NpgsqlDbContextFactory>();
		if (factory != null)
		{
			using var c = factory.CreateDbContext();
			db = await c.Database.CanConnectAsync(ctx.RequestAborted);
		}
	}
	catch { db = false; }

	ctx.Response.ContentType = "application/json";
	ctx.Response.StatusCode = db ? 200 : 503;

	// Only loopback gets detail, matching the other hosts: a public caller must not be
	// able to fingerprint an outage or correlate one across services.
	bool isLoopback = ctx.Connection.RemoteIpAddress != null &&
					  System.Net.IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress);
	await ctx.Response.WriteAsync(isLoopback
		? "{\"status\":\"" + (db ? "ok" : "degraded") + "\",\"db\":" + (db ? "true" : "false") + ",\"time\":\"" + DateTime.UtcNow.ToString("o") + "\"}"
		: "{\"status\":\"" + (db ? "ok" : "degraded") + "\"}");
}).AllowAnonymous();

/* An unmatched route under /api/ is a 404 in JSON, NOT the app shell.
 *
 * Without this the single-page fallback caught them: a GET to a route that does not
 * exist returned index.html with a 200, so any client parsing JSON broke on HTML, and a
 * DELETE returned 401 from the fallback's own auth rather than saying the route is not
 * there. A more specific fallback pattern outranks the catch-all, so this wins. */
app.MapFallback("/api/{**rest}", async (HttpContext ctx) =>
{
	ctx.Response.StatusCode = StatusCodes.Status404NotFound;
	ctx.Response.ContentType = "application/json";
	await ctx.Response.WriteAsync("{\"error\":\"No such endpoint.\"}");
}).AllowAnonymous();

// Client-side routing: anything that is not an API route or a real file falls
// through to the single-page app shell.
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

/// <summary>
/// Refuses to start in Production unless the Npgsql connection string sets
/// <c>Ssl Mode=Require</c>. The default <c>Prefer</c> silently falls back to plaintext,
/// exposing credentials and result rows to anything on-path. Identical to the guard
/// IpFetchServer applies.
/// </summary>
static void ValidateNpgsqlSslMode(IConfiguration configuration, IHostEnvironment environment)
{
	if (!environment.IsProduction()) return;
	if (configuration.GetValue("ConnectionStrings:AllowInsecureNpgsql", false)) return;

	string? connectionString = configuration.GetConnectionString("NpgsqlConnection");
	if (string.IsNullOrWhiteSpace(connectionString)) return;

	var parsed = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
	if (parsed.SslMode != Npgsql.SslMode.Require)
	{
		throw new InvalidOperationException(
			"In Production the Npgsql connection must use 'Ssl Mode=Require'. Set " +
			"ConnectionStrings:AllowInsecureNpgsql=true to deliberately allow an unencrypted " +
			"connection, for example over a loopback Unix socket.");
	}
}

/// <summary>Program entry point marker, so integration tests can reference this assembly.</summary>
public partial class Program { }
