using System.Collections.Concurrent;
using System.Security.Cryptography;
using SecureRemotePassword;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.Database.Npgsql.Services.Interfaces;

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
	/// </remarks>
	public sealed class SrpLoginService
	{
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

		public SrpLoginService(IAccountService accounts, PanelRegistrationOptions options, ILogger<SrpLoginService> log)
		{
			this.accounts = accounts;
			this.options = options;
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
			DateTime CreatedUtc);

		/// <summary>Result of the challenge step.</summary>
		public sealed record ChallengeResult(string Handle, string Salt, string ServerPublicEphemeral);

		/// <summary>Result of the proof step.</summary>
		public sealed record ProofResult(
			bool Ok,
			string Error,
			string ServerProof,
			string Username,
			byte AccessLevel,
			bool TotpEnabled);

		/// <summary>
		/// Message one: look the account up and answer with its salt and a server ephemeral.
		/// </summary>
		/// <remarks>
		/// An account that does not exist, or that is banned, gets a plausible fake salt and a
		/// fake verifier. The exchange proceeds normally and fails at the proof, so the two cases
		/// are indistinguishable in both timing shape and response shape. This is the same
		/// property <c>SrpService.DerivePerUsernameFakeSalt</c> gives the game path.
		/// </remarks>
		public async Task<ChallengeResult> ChallengeAsync(string username, CancellationToken cancellationToken = default)
		{
			Sweep();

			string salt;
			string verifier;
			byte accessLevel = (byte)AccessLevel.Player;
			bool totpEnabled = false;
			bool isReal = false;

			var lookup = await accounts.FetchForLoginAsync(username, false, cancellationToken);
			if (lookup.IsSuccess)
			{
				var data = lookup.Data;

				// The same verification gate the LoginServer applies: an unverified account may
				// sign in until its verification email has actually gone out, and the development
				// auto-verify policy bypasses the gate outright.
				bool verified = data.Verified ||
								data.VerificationEmailSentAt == null ||
								options.AutoVerifyAccounts;

				if (verified && data.AccessLevel != (byte)AccessLevel.Banned)
				{
					salt = data.Salt;
					verifier = data.Verifier;
					accessLevel = data.AccessLevel;
					totpEnabled = data.TotpEnabled;
					isReal = true;
				}
				else
				{
					(salt, verifier) = BuildFake(username);
				}
			}
			else
			{
				// Covers both a missing account and a banned one: FetchForLoginAsync deliberately
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
			var exchange = new Exchange(username, salt, verifier, ephemeral.Secret, accessLevel, totpEnabled, isReal, DateTime.UtcNow);

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
		/// Message two: verify the client's proof and answer with the server's.
		/// </summary>
		/// <remarks>
		/// The exchange is removed before it is evaluated, so a handle is single-use and a wrong
		/// proof cannot be retried against the same server ephemeral.
		/// </remarks>
		public ProofResult Proof(string handle, string clientPublicEphemeral, string clientProof)
		{
			const string genericFailure = "That username and password do not match an account.";

			if (string.IsNullOrWhiteSpace(handle) || !exchanges.TryRemove(handle, out Exchange exchange))
			{
				return new ProofResult(false, genericFailure, null, null, 0, false);
			}

			if (DateTime.UtcNow - exchange.CreatedUtc > ExchangeLifetime)
			{
				return new ProofResult(false, genericFailure, null, null, 0, false);
			}

			if (string.IsNullOrWhiteSpace(clientPublicEphemeral) || string.IsNullOrWhiteSpace(clientProof))
			{
				return new ProofResult(false, genericFailure, null, null, 0, false);
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
				string serverProof = session.Proof;

				if (!exchange.IsReal)
				{
					// Should be unreachable: nobody knows a password for a fake verifier. Belt
					// and braces, so a future change to the fake path cannot become a bypass.
					log.LogWarning("An SRP proof verified against a FAKE verifier. This should be impossible.");
					return new ProofResult(false, genericFailure, null, null, 0, false);
				}

				return new ProofResult(true, null, serverProof, exchange.Username, exchange.AccessLevel, exchange.TotpEnabled);
			}
			catch (Exception ex)
			{
				// A malformed ephemeral throws out of the SRP library rather than returning false.
				log.LogDebug(ex, "SRP proof evaluation threw.");
				return new ProofResult(false, genericFailure, null, null, 0, false);
			}
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
