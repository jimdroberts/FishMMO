using System.Collections.Concurrent;
using System.Security.Cryptography;
using SecureRemotePassword;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;
using AccessLevel = FishMMO.Auth.Core.AccessLevel;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// The server half of the panel's SRP-6a login, using the same
	/// <see cref="ServerSrpData"/> and the same 2048-bit SHA-512 parameters the LoginServer uses.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The panel never sees a password. It answers a challenge with the account's salt and a
	/// public ephemeral, then verifies the client's proof; only the proof crosses the wire.
	/// </para>
	/// <para>
	/// An in-flight exchange has to survive between the two requests, so it is held in memory
	/// keyed by an opaque handle the browser echoes back. Memory rather than the database because
	/// it is worthless after ten seconds and worthless to anyone but the one browser that started
	/// it; the trade is that a panel restart mid-login makes the browser start again.
	/// </para>
	/// <para>
	/// <b>Lockout.</b> The password step is counted in the database, in the same columns the
	/// LoginServer counts into, under <see cref="AuthLockoutOptions"/>. A locked account is refused
	/// before its proof is evaluated, and the refusal is <see cref="GenericFailure"/> — word for word
	/// the answer to a wrong password — so the lock never becomes a way to learn a name exists or a
	/// guess was right. The lookup and the count run for unknown and banned names too, so the two
	/// paths also cost the same round trips.
	/// </para>
	/// </remarks>
	public sealed class SrpLoginService
	{
		/// <summary>
		/// The one answer to every failed password step: wrong password, unknown account, banned
		/// account, expired exchange, and a sign-in that is locked.
		/// </summary>
		public const string GenericFailure =
			"That username and password do not match an account, or sign-in is temporarily locked after repeated failures. Try again later.";

		/// <summary>How long a half-finished exchange is kept before it is swept.</summary>
		private static readonly TimeSpan ExchangeLifetime = TimeSpan.FromMinutes(2);

		/// <summary>
		/// Cap on concurrent in-flight exchanges. The challenge endpoint is unauthenticated, so
		/// without a cap it is a memory-growth primitive for anyone who can reach it.
		/// </summary>
		private const int MaxExchanges = 10_000;

		private readonly ConcurrentDictionary<string, Exchange> exchanges = new();
		private readonly IAccountService accounts;
		private readonly PanelRegistrationOptions options;
		private readonly VerificationOptions verification;
		private readonly AuthLockoutOptions lockout;
		private readonly IServiceScopeFactory scopes;
		private readonly ILogger<SrpLoginService> log;

		/// <summary>
		/// Key used to derive a stable fake salt for accounts that do not exist, so probing the
		/// challenge endpoint cannot enumerate usernames.
		/// </summary>
		/// <remarks>
		/// <see cref="SrpService.DerivePerUsernameFakeSalt"/> requires exactly
		/// <see cref="CryptoHelper.HmacSha512KeyLength"/> bytes and throws otherwise, deliberately:
		/// a short key would silently weaken the per-username uniqueness the fake salt depends on.
		/// It is per-process rather than a deployment secret because it only has to be stable for
		/// as long as an attacker could compare two probes; a restart changing it reveals nothing,
		/// since a real account's salt does not change and a fake one is unknowable either way.
		/// </remarks>
		private readonly byte[] fakeSaltKey = CryptoHelper.GenerateKey(CryptoHelper.HmacSha512KeyLength);

		public SrpLoginService(
			IAccountService accounts,
			PanelRegistrationOptions options,
			VerificationOptions verification,
			AuthLockoutOptions lockout,
			IServiceScopeFactory scopes,
			ILogger<SrpLoginService> log)
		{
			this.accounts = accounts;
			this.options = options;
			this.verification = verification;
			this.lockout = lockout;
			this.scopes = scopes;
			this.log = log;
		}

		private sealed record Exchange(
			string Username,
			string Salt,
			string Verifier,
			string ServerSecretEphemeral,
			byte AccessLevel,
			bool TotpEnabled,
			bool IsReal,
			bool VerificationRequired,
			AccountVerificationChannels Outstanding,
			bool DiscordVerifyCodeIssued,
			DateTime CreatedUtc);

		/// <summary>Result of the challenge step.</summary>
		public sealed record ChallengeResult(string Handle, string Salt, string ServerPublicEphemeral);

		/// <summary>Result of the proof step.</summary>
		/// <param name="VerificationRequired">
		/// The password was proven, but the account still owes a verification code. Only ever set
		/// after a correct proof, so it tells nobody anything they could not already sign in with.
		/// </param>
		public sealed record ProofResult(
			bool Ok,
			string Error,
			string ServerProof,
			string Username,
			byte AccessLevel,
			bool TotpEnabled,
			bool VerificationRequired = false,
			AccountVerificationChannels Outstanding = AccountVerificationChannels.None);

		private static ProofResult Failed() => new(false, GenericFailure, null, null, 0, false);

		/// <summary>
		/// Message one: look the account up and answer with its salt and a server ephemeral.
		/// </summary>
		/// <remarks>
		/// <para>
		/// An account that does not exist, or that is banned, gets a plausible fake salt and a
		/// fake verifier. The exchange proceeds normally and fails at the proof, so the two cases
		/// are indistinguishable in both timing shape and response shape. This is the same
		/// property <c>SrpService.DerivePerUsernameFakeSalt</c> gives the game path.
		/// </para>
		/// <para>
		/// An unverified account gets its REAL salt and verifier, and is marked. A wrong password still
		/// fails exactly as it would for anyone; a right one is told which verification codes it may
		/// enter instead of being handed a session. Before this, the unverified account was faked, and a
		/// player who had simply not typed their code was told their password was wrong.
		/// </para>
		/// <para>
		/// There is no grace period. An account used to be let in until its first code had been
		/// delivered, which made "the mail relay is down" and "verification is required" the same
		/// thing for as long as the relay stayed down. Now an account owes a code from the moment it
		/// exists, and a player who never receives one reaches staff through the ticket three wrong
		/// codes open.
		/// </para>
		/// </remarks>
		public async Task<ChallengeResult> ChallengeAsync(string username, CancellationToken cancellationToken = default)
		{
			Sweep();

			string salt;
			string verifier;
			byte accessLevel = (byte)AccessLevel.Player;
			bool totpEnabled = false;
			bool isReal = false;
			bool verificationRequired = false;
			bool discordVerifyCodeIssued = false;
			AccountVerificationChannels outstanding = AccountVerificationChannels.None;

			var lookup = await accounts.FetchForLoginAsync(username, false, cancellationToken);
			if (lookup.IsSuccess && lookup.Data.AccessLevel != (byte)AccessLevel.Banned)
			{
				var data = lookup.Data;
				salt = data.Salt;
				verifier = data.Verifier;
				accessLevel = data.AccessLevel;
				totpEnabled = data.TotpEnabled;
				isReal = true;

				/* The same verification gate the LoginServer applies, from the same shared rule. An
				 * account that owes no code is in: a verified one, one on a server that verifies
				 * nothing, and one no enabled channel can reach (AccountVerificationRules.IsWaived) —
				 * Outstanding answers None for all three. The development auto-verify policy bypasses
				 * the gate outright. */
				AccountVerificationChannels owed = AccountVerificationRules.Outstanding(data, verification.Enabled);
				bool verified = data.Verified ||
								options.AutoVerifyAccounts ||
								owed == AccountVerificationChannels.None;
				if (!verified)
				{
					verificationRequired = true;
					outstanding = owed;
					discordVerifyCodeIssued = data.DiscordVerifyCodeIssued;
				}
			}
			else
			{
				// Covers a missing account and a banned one alike: FetchForLoginAsync deliberately
				// does not distinguish them.
				(salt, verifier) = BuildFake(username);
			}

			/* SrpServer directly, rather than ServerSrpData, because that type takes the client's
			 * ephemeral in its constructor: it models one FishNet connection whose messages arrive
			 * in order on a live socket. An HTTP pair receives the client ephemeral in the SECOND
			 * request, so the server secret has to outlive the first. This is the same library,
			 * the same SrpParameters.Create2048<SHA512>(), and the same two calls ServerSrpData
			 * makes internally — GenerateEphemeral then DeriveSession. */
			var server = new SrpServer(SrpParameters.Create2048<SHA512>());
			SrpEphemeral ephemeral = server.GenerateEphemeral(verifier);

			string handle = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
			var exchange = new Exchange(username, salt, verifier, ephemeral.Secret, accessLevel, totpEnabled,
				isReal, verificationRequired, outstanding, discordVerifyCodeIssued, DateTime.UtcNow);

			if (exchanges.Count >= MaxExchanges)
			{
				// Fail closed rather than grow without bound. The caller sees a normal failure.
				log.LogWarning("SRP exchange table is at its cap ({Cap}); refusing a new challenge.", MaxExchanges);
				return null;
			}
			exchanges[handle] = exchange;

			return new ChallengeResult(handle, salt, ephemeral.Public);
		}

		/// <summary>
		/// Message two for SIGN-IN: the lockout, the proof, the failure count, and the verification gate.
		/// </summary>
		/// <remarks>
		/// <list type="number">
		/// <item><description>The exchange is removed before anything else, so a handle is single-use.</description></item>
		/// <item><description>A locked password step is refused WITHOUT evaluating the proof, with <see cref="GenericFailure"/>.</description></item>
		/// <item><description>A failed proof is counted; the failure that starts a lock queues one security notice.</description></item>
		/// <item><description>A good proof clears the count.</description></item>
		/// <item><description>A proven, unverified account is issued its Discord code if it is owed one and has none, and is told which codes it may enter.</description></item>
		/// </list>
		/// </remarks>
		public async Task<ProofResult> SignInProofAsync(string handle, string clientPublicEphemeral, string clientProof, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(handle) || !exchanges.TryRemove(handle, out Exchange exchange))
			{
				return Failed();
			}
			if (DateTime.UtcNow - exchange.CreatedUtc > ExchangeLifetime)
			{
				return Failed();
			}

			// Unknown and banned names are looked up too, so a real account costs no extra round trip.
			DateTime now = DateTime.UtcNow;
			var state = await accounts.FetchAuthLockoutAsync(exchange.Username, cancellationToken);
			if (state.IsSuccess && state.Data.IsLocked(AuthFailureKind.Password, now))
			{
				log.LogInformation("Refused a panel sign-in for '{User}': the password step is locked until {Until:o}.",
					exchange.Username, state.Data.LoginLockedUntilUtc);
				return Failed();
			}

			ProofResult evaluated = Evaluate(exchange, clientPublicEphemeral, clientProof);
			if (!evaluated.Ok)
			{
				var recorded = await accounts.RecordAuthFailureAsync(
					exchange.Username, AuthFailureKind.Password,
					lockout.PasswordThreshold,
					TimeSpan.FromMinutes(lockout.PasswordWindowMinutes),
					TimeSpan.FromMinutes(lockout.PasswordLockMinutes),
					cancellationToken);

				if (!recorded.IsSuccess)
				{
					log.LogWarning("Could not count a failed panel sign-in for '{User}': [{Code}] {Message}",
						exchange.Username, recorded.ErrorCode, recorded.ErrorMessage);
				}
				else if (recorded.Data.HasValue && recorded.Data.Value > now)
				{
					// Unlocked a moment ago, locked now: this failure started the lock.
					log.LogWarning("Password sign-in for '{User}' locked until {Until:o} after repeated failures.",
						exchange.Username, recorded.Data.Value);
					await NotifyLockedAsync(exchange.Username, recorded.Data.Value, cancellationToken);
				}
				return Failed();
			}

			var cleared = await accounts.ClearAuthFailuresAsync(exchange.Username, AuthFailureKind.Password, cancellationToken);
			if (!cleared.IsSuccess)
			{
				log.LogWarning("Could not clear the password failure count for '{User}': [{Code}] {Message}",
					exchange.Username, cleared.ErrorCode, cleared.ErrorMessage);
			}

			if (exchange.VerificationRequired)
			{
				/* The password is proven, so this is the account holder. An account that chose Discord
				 * but has no Discord code — one registered in-game before the bot was switched on, or one
				 * whose registration could not issue it — gets it now, and the bot sends the one DM.
				 * Only here, after a CORRECT proof: a wrong password must never make the bot message
				 * anybody, or the challenge endpoint would be a way to spam a stranger's Discord. The
				 * database refuses to issue a second code, so repeated sign-ins send nothing more. */
				if ((exchange.Outstanding & AccountVerificationChannels.Discord) != 0 && !exchange.DiscordVerifyCodeIssued)
				{
					int discordCode = RandomNumberGenerator.GetInt32(100000, 1000000);
					var issued = await accounts.PersistDiscordVerifyCodeAsync(exchange.Username, discordCode, cancellationToken);
					if (!issued.IsSuccess)
					{
						log.LogWarning("PersistDiscordVerifyCodeAsync failed for '{User}' at sign-in: [{Code}] {Message}",
							exchange.Username, issued.ErrorCode, issued.ErrorMessage);
					}
				}

				return evaluated with
				{
					Ok = false,
					Error = VerificationRequiredMessage(exchange.Outstanding),
					VerificationRequired = true,
					Outstanding = exchange.Outstanding,
				};
			}

			return evaluated;
		}

		/// <summary>
		/// Message two, with no lockout and no verification gate: a proof of the CURRENT password by a
		/// session that is already signed in (the password change).
		/// </summary>
		/// <remarks>
		/// The exchange is removed before it is evaluated, so a handle is single-use and a wrong
		/// proof cannot be retried against the same server ephemeral.
		/// </remarks>
		public ProofResult Proof(string handle, string clientPublicEphemeral, string clientProof)
		{
			if (string.IsNullOrWhiteSpace(handle) || !exchanges.TryRemove(handle, out Exchange exchange))
			{
				return Failed();
			}
			if (DateTime.UtcNow - exchange.CreatedUtc > ExchangeLifetime)
			{
				return Failed();
			}
			return Evaluate(exchange, clientPublicEphemeral, clientProof);
		}

		/// <summary>The one message naming the codes an unverified account may enter.</summary>
		/// <remarks>
		/// Names every outstanding channel, because any one of them verifies the account: a player told
		/// only about the email may not think to look in Discord, where the code already is.
		/// </remarks>
		public static string VerificationRequiredMessage(AccountVerificationChannels outstanding)
		{
			var places = new List<string>(3);
			if ((outstanding & AccountVerificationChannels.Email) != 0) places.Add("your email address");
			if ((outstanding & AccountVerificationChannels.Sms) != 0) places.Add("your phone");
			if ((outstanding & AccountVerificationChannels.Discord) != 0) places.Add("you as a Discord direct message");
			if (places.Count == 0)
			{
				places.Add("your email address");
			}

			string where = places.Count == 1
				? places[0]
				: string.Join(", ", places.Take(places.Count - 1)) + " or " + places[^1];
			string any = places.Count == 1 ? "the code" : "any one of the codes";
			return $"This account is not verified yet. Enter {any} we sent to {where} to verify it, then sign in.";
		}

		private ProofResult Evaluate(Exchange exchange, string clientPublicEphemeral, string clientProof)
		{
			if (string.IsNullOrWhiteSpace(clientPublicEphemeral) || string.IsNullOrWhiteSpace(clientProof))
			{
				return Failed();
			}

			try
			{
				// Throws SecurityException when the proof does not verify, which is the library's
				// only signal; ServerSrpData wraps the same call and converts it to a bool.
				var server = new SrpServer(SrpParameters.Create2048<SHA512>());
				SrpSession session = server.DeriveSession(
					exchange.ServerSecretEphemeral,
					clientPublicEphemeral,
					exchange.Salt,
					exchange.Username,
					exchange.Verifier,
					clientProof);

				if (!exchange.IsReal)
				{
					// Should be unreachable: nobody knows a password for a fake verifier. Belt
					// and braces, so a future change to the fake path cannot become a bypass.
					log.LogWarning("An SRP proof verified against a FAKE verifier. This should be impossible.");
					return Failed();
				}

				return new ProofResult(true, null, session.Proof, exchange.Username, exchange.AccessLevel, exchange.TotpEnabled);
			}
			catch (Exception ex)
			{
				// A malformed ephemeral throws out of the SRP library rather than returning false.
				log.LogDebug(ex, "SRP proof evaluation threw.");
				return Failed();
			}
		}

		private async Task NotifyLockedAsync(string username, DateTime lockedUntilUtc, CancellationToken cancellationToken)
		{
			using var scope = scopes.CreateScope();
			var notices = scope.ServiceProvider.GetRequiredService<SecurityNoticeService>();
			await notices.SignInLockedAsync(username, AuthFailureKind.Password, lockedUntilUtc, cancellationToken);
		}

		private (string Salt, string Verifier) BuildFake(string username)
		{
			string salt = SrpService.DerivePerUsernameFakeSalt(username ?? string.Empty, fakeSaltKey);
			// A verifier nobody holds the password for. Derived, not random, so repeated probes
			// of the same name see a stable answer exactly as a real account would.
			var client = new SrpClient(SrpParameters.Create2048<SHA512>());
			string privateKey = client.DerivePrivateKey(salt, username ?? string.Empty, Convert.ToBase64String(fakeSaltKey));
			return (salt, client.DeriveVerifier(privateKey));
		}

		private void Sweep()
		{
			if (exchanges.IsEmpty)
			{
				return;
			}
			DateTime cutoff = DateTime.UtcNow - ExchangeLifetime;
			foreach (var pair in exchanges)
			{
				if (pair.Value.CreatedUtc < cutoff)
				{
					exchanges.TryRemove(pair.Key, out _);
				}
			}
		}
	}
}
