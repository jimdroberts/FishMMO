using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using FishMMO.Auth.Core;
using SecureRemotePassword;

namespace FishMMO.UnitTests.Harness
{
	/// <summary>
	/// In-memory backing store that the test server core consults when answering
	/// <c>FetchAccountForLoginAsync</c>, <c>CheckIsOnlineAsync</c>, etc.
	/// Also provides simplified token issuance and validation helpers for token auth tests.
	/// </summary>
	internal sealed class InMemoryAccountStore
	{
		private enum TokenState { Valid, Expired, Revoked }

		private sealed class TokenRecord
		{
			public string Username = "";
			public TokenState State;
		}

		private sealed class Record
		{
			public string Username = "";
			public string Email = "";
			public string Salt = "";
			public string Verifier = "";
			public AccessLevel AccessLevel = AccessLevel.Player;
			public bool IsVerified;
			public bool TotpEnabled;
			public string? TotpSecret;
			public bool IsOnline;
			public bool HasPendingKick;
			public string? LastTokenHash;
			public int LastTokenExpirationMinutes;
		}

		private readonly ConcurrentDictionary<string, Record> byUsername =
			new ConcurrentDictionary<string, Record>(System.StringComparer.OrdinalIgnoreCase);

		private readonly Dictionary<string, TokenRecord> tokenStore = new Dictionary<string, TokenRecord>(StringComparer.Ordinal);
		private bool simulateDbError;

		/// <summary>Pre-seed an SRP-eligible account.</summary>
		/// <param name="username">Account username.</param>
		/// <param name="password">Account password.</param>
		/// <param name="isVerified">Whether the account email is verified.</param>
		/// <param name="totpEnabled">Whether TOTP 2FA is enabled.</param>
		/// <param name="totpSecret">TOTP secret key if TOTP is enabled.</param>
		/// <param name="email">Optional email address; defaults to username@example.test.</param>
		/// <param name="isBanned">Whether the account is banned.</param>
		public void SeedAccount(string username, string password, bool isVerified = true, bool totpEnabled = false, string? totpSecret = null, string? email = null, bool isBanned = false)
		{
			SrpClient srp = new SrpClient(SrpParameters.Create2048<SHA512>());
			string salt = srp.GenerateSalt();
			string privateKey = srp.DerivePrivateKey(salt, username, password);
			string verifier = srp.DeriveVerifier(privateKey);
			byUsername[username] = new Record
			{
				Username = username,
				Email = email ?? (username + "@example.test"),
				Salt = salt,
				Verifier = verifier,
				IsVerified = isVerified,
				TotpEnabled = totpEnabled,
				TotpSecret = totpSecret,
				AccessLevel = isBanned ? AccessLevel.Banned : AccessLevel.Player,
			};
		}

		/// <summary>Lookup row exposed to the test server core, which converts it into the nested
		/// <c>SrpAuthenticatorCore&lt;TConnection&gt;.SrpAccountLookupResult</c> struct.</summary>
		public readonly struct Lookup
		{
			public readonly bool IsVerified;
			public readonly string Salt;
			public readonly string Verifier;
			public readonly AccessLevel AccessLevel;
			public readonly bool TotpEnabled;
			public Lookup(bool isVerified, string salt, string verifier, AccessLevel accessLevel, bool totpEnabled)
			{
				IsVerified = isVerified; Salt = salt; Verifier = verifier; AccessLevel = accessLevel; TotpEnabled = totpEnabled;
			}
		}

		public bool TryGet(string username, out Lookup result)
		{
			if (byUsername.TryGetValue(username, out Record? rec))
			{
				result = new Lookup(rec.IsVerified, rec.Salt, rec.Verifier, rec.AccessLevel, rec.TotpEnabled);
				return true;
			}
			result = default;
			return false;
		}

		public bool IsOnline(string username) =>
			byUsername.TryGetValue(username, out Record? r) && r.IsOnline;
	/// <summary>Sets the online status of the named account.</summary>
	public void SetOnline(string username, bool value)
	{
		if (byUsername.TryGetValue(username, out Record? r)) r.IsOnline = value;
	}
		public bool HasPendingKick(string username) =>
			byUsername.TryGetValue(username, out Record? r) && r.HasPendingKick;

		public void SetPendingKick(string username, bool value)
		{
			if (byUsername.TryGetValue(username, out Record? r)) r.HasPendingKick = value;
		}

		public void SetVerified(string username, bool value)
		{
			if (byUsername.TryGetValue(username, out Record? r)) r.IsVerified = value;
		}

		public string? GetTotpSecret(string username) =>
			byUsername.TryGetValue(username, out Record? r) ? r.TotpSecret : null;

		public void PersistTokenHash(string username, string tokenHash, int expirationMinutes)
		{
			if (byUsername.TryGetValue(username, out Record? r))
			{
				r.LastTokenHash = tokenHash;
				r.LastTokenExpirationMinutes = expirationMinutes;
			}
		}

		public string? GetLastTokenHash(string username) =>
			byUsername.TryGetValue(username, out Record? r) ? r.LastTokenHash : null;

		public bool ContainsAccount(string username) => byUsername.ContainsKey(username);

		#region Token helpers for unit tests

		/// <summary>Issues a valid token for the named account and returns its identifier.</summary>
		public string IssueValidToken(string username)
		{
			string id = GenerateTokenId();
			tokenStore[id] = new TokenRecord { Username = username, State = TokenState.Valid };
			return id;
		}

		/// <summary>Issues an already-expired token for the named account and returns its identifier.</summary>
		public string IssueExpiredToken(string username)
		{
			string id = GenerateTokenId();
			tokenStore[id] = new TokenRecord { Username = username, State = TokenState.Expired };
			return id;
		}

		/// <summary>Issues a revoked token for the named account and returns its identifier.</summary>
		public string IssueRevokedToken(string username)
		{
			string id = GenerateTokenId();
			tokenStore[id] = new TokenRecord { Username = username, State = TokenState.Revoked };
			return id;
		}

		/// <summary>Creates a new valid token for the same account, leaving the original unchanged.</summary>
		/// <returns>The identifier of the newly created token, or <c>null</c> if the original token was not found.</returns>
		public string? RenewToken(string token)
		{
			if (!tokenStore.TryGetValue(token, out TokenRecord? rec))
				return null;
			string newId = GenerateTokenId();
			tokenStore[newId] = new TokenRecord { Username = rec.Username, State = TokenState.Valid };
			return newId;
		}

		/// <summary>Marks the given token as revoked.</summary>
		public void RevokeToken(string token)
		{
			if (tokenStore.TryGetValue(token, out TokenRecord? rec))
				rec.State = TokenState.Revoked;
		}

		/// <summary>Causes the next <see cref="ValidateToken"/> call to return <see cref="ClientAuthenticationResult.ServerBusy"/>.</summary>
		public void SimulateDbError()
		{
			simulateDbError = true;
		}

		/// <summary>
		/// Validates a token identifier and returns the appropriate <see cref="ClientAuthenticationResult"/>.
		/// Used by the test harness token auth path in place of full crypto decryption.
		/// </summary>
		public ClientAuthenticationResult ValidateToken(string token)
		{
			if (simulateDbError)
			{
				simulateDbError = false;
				return ClientAuthenticationResult.ServerBusy;
			}

			if (!tokenStore.TryGetValue(token, out TokenRecord? rec))
				return ClientAuthenticationResult.TokenInvalid;

			return rec.State switch
			{
				TokenState.Expired => ClientAuthenticationResult.TokenExpired,
				TokenState.Revoked => ClientAuthenticationResult.TokenRevoked,
				_ => ClientAuthenticationResult.LoginSuccess,
			};
		}

		private static string GenerateTokenId()
		{
			byte[] bytes = new byte[16];
			using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
				rng.GetBytes(bytes);
			return Convert.ToBase64String(bytes);
		}

		#endregion
	}
}