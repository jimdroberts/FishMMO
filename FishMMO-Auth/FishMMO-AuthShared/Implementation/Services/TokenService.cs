using System;
using System.Security.Cryptography;
using System.Text;
using FishMMO.Auth.Core;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Transport-agnostic service for authentication token operations.
	/// Provides token generation, encryption, decryption, and verification
	/// for both server-side issuance and client/server token authentication.
	/// </summary>
	public static class TokenService
	{
		#region Token Generation

		/// <summary>
		/// Builds a raw HMAC-signed auth token for the specified account.
		/// This is a thin wrapper around <see cref="CryptoHelper.BuildAuthToken"/>
		/// that validates the signing key before use.
		/// </summary>
		/// <param name="username">Account name to embed in the token.</param>
		/// <param name="loginServerId">ID of the issuing login server.</param>
		/// <param name="signingKeyId">Database ID of the signing key used for this token.</param>
		/// <param name="expiresUtc">Token expiration timestamp (UTC).</param>
		/// <param name="signingKey">HMAC-SHA256 signing key. Must be at least <see cref="CryptoHelper.HmacKeyLength"/> bytes.</param>
		/// <param name="accessLevel">Account access level to embed.</param>
		/// <returns>Raw signed token bytes, or <c>null</c> if the signing key is invalid.</returns>
		public static byte[]? BuildToken(
			string username,
			long loginServerId,
			long signingKeyId,
			DateTime expiresUtc,
			byte[] signingKey,
			AccessLevel accessLevel)
		{
			if (signingKey == null || signingKey.Length < CryptoHelper.HmacKeyLength)
				return null;

			return CryptoHelper.BuildAuthToken(username, loginServerId, signingKeyId, expiresUtc, signingKey, accessLevel);
		}

		/// <summary>
		/// Computes the SHA-256 hex hash of a raw token for revocation tracking.
		/// </summary>
		/// <param name="rawToken">Raw token bytes to hash.</param>
		/// <returns>Lowercase hex-encoded SHA-256 hash string.</returns>
		public static string HashToken(byte[] rawToken)
		{
			return CryptoHelper.HashTokenHex(rawToken);
		}

		#endregion

		#region Server-Side Encrypted Token Generation (AES-GCM)

		/// <summary>
		/// Builds, signs, and encrypts an auth token for transmission to the client
		/// over an AES-GCM encrypted channel. Used by login servers after successful SRP authentication.
		/// </summary>
		/// <param name="encryptionData">Connection encryption state (keys, nonces, version).</param>
		/// <param name="username">Account name to embed in the token.</param>
		/// <param name="loginServerId">ID of the issuing login server.</param>
		/// <param name="signingKeyId">Database ID of the signing key used for this token.</param>
		/// <param name="tokenExpirationMinutes">Token validity duration in minutes.</param>
		/// <param name="signingKey">HMAC-SHA256 signing key.</param>
		/// <param name="accessLevel">Account access level to embed.</param>
		/// <param name="rawTokenForHashing">Raw token bytes (caller must hash for DB storage, then zero).</param>
		/// <returns>Encrypted token bytes for transmission, or <c>null</c> if generation failed.</returns>
		public static byte[]? GenerateAndEncryptToken(
			ConnectionEncryptionData encryptionData,
			string username,
			long loginServerId,
			long signingKeyId,
			int tokenExpirationMinutes,
			byte[] signingKey,
			AccessLevel accessLevel,
			out byte[]? rawTokenForHashing)
		{
			rawTokenForHashing = null;

			byte[]? rawToken = BuildToken(username, loginServerId, signingKeyId, DateTime.UtcNow.AddMinutes(tokenExpirationMinutes), signingKey, accessLevel);
			if (rawToken == null)
				return null;

			try
			{
				rawTokenForHashing = rawToken;
				return EncryptTokenForSend(rawToken, encryptionData);
			}
			catch
			{
				CryptographicOperations.ZeroMemory(rawToken);
				rawTokenForHashing = null;
				return null;
			}
		}

		/// <summary>
		/// Encrypts a raw token for transmission using AES-GCM with SrpSuccess AAD type.
		/// </summary>
		/// <param name="rawToken">Raw token bytes.</param>
		/// <param name="encryptionData">Connection encryption state.</param>
		/// <returns>AES-GCM encrypted token bytes.</returns>
		public static byte[] EncryptTokenForSend(byte[] rawToken, ConnectionEncryptionData encryptionData)
		{
			uint tokenSeq = encryptionData.NextSendSequence();
			byte[] tokenNonce = encryptionData.BuildSendNonce(tokenSeq);
			byte[] tokenAad = new byte[CryptoHelper.AadLength];
			CryptoHelper.WriteAad(tokenAad, (byte)CryptoHelper.AuthMessageType.SrpSuccess, encryptionData.AgreedVersion, tokenSeq);
			return CryptoHelper.EncryptAES(encryptionData.ServerToClientKey!, tokenNonce, rawToken, tokenAad);
		}

		#endregion

		#region Server-Side Token Decryption and Verification

		/// <summary>
		/// Result of a token decryption and verification operation.
		/// </summary>
		public struct TokenVerifyResult
		{
			/// <summary>Whether decryption and HMAC verification both succeeded.</summary>
			public bool IsValid;

			/// <summary>Account name extracted from the verified token.</summary>
			public string? AccountName;

			/// <summary>Login server ID extracted from the verified token.</summary>
			public long LoginServerId;

			/// <summary>Signing-key database ID extracted from the verified token.</summary>
			public long SigningKeyId;

			/// <summary>Access level extracted from the verified token.</summary>
			public AccessLevel AccessLevel;

			/// <summary>Token expiration time (UTC) extracted from the verified token.</summary>
			public DateTime ExpiresUtc;

			/// <summary>SHA-256 hex hash of the raw token for revocation lookups.</summary>
			public string? TokenHash;

			/// <summary>
			/// If <c>false</c>, the signing key was not found but the HMAC verification
			/// ran anyway with a dummy key to equalize timing. Callers should reject.
			/// </summary>
			public bool SigningKeyFound;
		}

		/// <summary>
		/// Decrypts a token from an AES-GCM encrypted payload and performs partial parsing
		/// to extract the login server ID for signing key lookup. Does NOT verify the HMAC.
		/// Call <see cref="VerifyToken"/> after obtaining the signing key.
		/// </summary>
		/// <remarks>
		/// <para><b>Pre-decryption size gate:</b> No explicit ciphertext length check is performed
		/// here because <see cref="CryptoHelper.DecryptAES"/> enforces a hard cap of
		/// <see cref="CryptoHelper.MaxAesCiphertextSize"/> (64 KiB) and a minimum of
		/// <see cref="CryptoHelper.AesGcmTagLengthBytes"/> (16 B). Since GCM decryption is
		/// authenticated, oversized or malformed payloads are rejected before any plaintext
		/// is exposed, making an additional pre-check redundant.</para>
		/// </remarks>
		/// <param name="encryptedToken">AES-GCM encrypted token bytes from client.</param>
		/// <param name="encryptionData">Connection encryption state.</param>
		/// <param name="seq">Broadcast sequence number.</param>
		/// <param name="rawToken">Decrypted raw token bytes (caller must zero after use).</param>
		/// <param name="loginServerId">Extracted login server ID for signing key ownership checks.</param>
		/// <param name="signingKeyId">Extracted signing-key ID for signing key lookup.</param>
		/// <returns><c>true</c> if decryption and partial parse succeeded.</returns>
		public static bool TryDecryptAndPartialParse(
			byte[] encryptedToken,
			ConnectionEncryptionData encryptionData,
			uint seq,
			out byte[]? rawToken,
			out long loginServerId,
			out long signingKeyId)
		{
			rawToken = null;
			loginServerId = 0;
			signingKeyId = 0;

			if (seq == 0 || !encryptionData.TryConsumeReceiveSequence(seq))
				return false;

			try
			{
				byte[] nonce = encryptionData.BuildReceiveNonce(seq);
				byte[] aad = new byte[CryptoHelper.AadLength];
				CryptoHelper.WriteAad(aad, (byte)CryptoHelper.AuthMessageType.TokenAuth, encryptionData.AgreedVersion, seq);
				rawToken = CryptoHelper.DecryptAES(encryptionData.ClientToServerKey!, nonce, encryptedToken, aad);
			}
			catch (CryptographicException)
			{
				return false;
			}

			// Validate minimum token structure length
			if (rawToken.Length < CryptoHelper.MinSignedTokenLength)
			{
				CryptographicOperations.ZeroMemory(rawToken);
				rawToken = null;
				return false;
			}

			// Token format: [1B version][1B tokenType][2B nameLen BE][name][1B accessLevel][8B serverId][8B signingKeyId]...
			int nameLen = (rawToken[2] << 8) | rawToken[3];
			if (nameLen <= 0 || 4 + nameLen + 1 + 8 + 8 + 8 + CryptoHelper.HmacTagLength > rawToken.Length)
			{
				CryptographicOperations.ZeroMemory(rawToken);
				rawToken = null;
				return false;
			}

			int serverIdOffset = 4 + nameLen + 1;
			for (int i = 0; i < 8; i++)
				loginServerId = (loginServerId << 8) | rawToken[serverIdOffset + i];

			int signingKeyIdOffset = serverIdOffset + 8;
			for (int i = 0; i < 8; i++)
				signingKeyId = (signingKeyId << 8) | rawToken[signingKeyIdOffset + i];

			if (signingKeyId <= 0)
			{
				CryptographicOperations.ZeroMemory(rawToken);
				rawToken = null;
				return false;
			}

			return true;
		}

		/// <summary>
		/// Verifies the HMAC on a raw token and parses all fields.
		/// Equalizes timing regardless of whether the signing key is real or dummy.
		/// </summary>
		/// <param name="rawToken">Raw decrypted token bytes.</param>
		/// <param name="hmacKey">
		/// HMAC signing key. If the key was not found, pass a random dummy key
		/// of <see cref="CryptoHelper.HmacKeyLength"/> bytes to equalize timing.
		/// </param>
		/// <param name="signingKeyFound">Whether the signing key was actually found in the database.</param>
		/// <param name="preParseLoginServerId">
		/// Login server ID from partial parse. Cross-checked against the HMAC-verified value.
		/// </param>
		/// <param name="preParseSigningKeyId">Signing-key ID from partial parse. Cross-checked against the HMAC-verified value.</param>
		/// <returns>Verification result with parsed token fields.</returns>
		public static TokenVerifyResult VerifyToken(
			byte[] rawToken,
			byte[] hmacKey,
			bool signingKeyFound,
			long preParseLoginServerId,
			long preParseSigningKeyId)
		{
			var result = new TokenVerifyResult
			{
				SigningKeyFound = signingKeyFound,
			};

			bool hmacValid = CryptoHelper.TryParseAndVerifyAuthToken(
				rawToken,
				hmacKey,
				out result.AccountName,
				out result.LoginServerId,
				out result.SigningKeyId,
				out result.AccessLevel,
				out result.ExpiresUtc);

			if (!signingKeyFound || !hmacValid)
			{
				result.IsValid = false;
				return result;
			}

			// Cross-check: parsed IDs inside HMAC envelope must match the
			// pre-HMAC partial parse to detect token tampering.
			if (result.LoginServerId != preParseLoginServerId || result.SigningKeyId != preParseSigningKeyId)
			{
				result.IsValid = false;
				return result;
			}

			// Check expiration
			if (DateTime.UtcNow >= result.ExpiresUtc)
			{
				result.IsValid = false;
				return result;
			}

			result.TokenHash = CryptoHelper.HashTokenHex(rawToken);
			result.IsValid = true;
			return result;
		}

		#endregion

		#region Client-Side Token Encryption

		/// <summary>
		/// Encrypts a stored auth token for transmission to a World/Scene server
		/// using AES-GCM with TokenAuth AAD type.
		/// </summary>
		/// <param name="rawToken">Raw auth token bytes to encrypt.</param>
		/// <param name="clientToServerKey">AES-256 key for client→server direction.</param>
		/// <param name="sendNonceCtx">Client's send nonce context.</param>
		/// <param name="agreedVersion">Negotiated protocol version.</param>
		/// <param name="encryptedToken">Encrypted token output.</param>
		/// <param name="seq">Sequence number used (for broadcast).</param>
		public static void ClientEncryptToken(
			byte[] rawToken,
			byte[] clientToServerKey,
			CryptoHelper.GcmNonceContext sendNonceCtx,
			ushort agreedVersion,
			out byte[] encryptedToken,
			out uint seq)
		{
			var (nonce, seqVal) = sendNonceCtx.NextNonce();
			seq = seqVal;
			byte[] aad = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.TokenAuth, agreedVersion, seq);
			encryptedToken = CryptoHelper.EncryptAES(clientToServerKey, nonce, rawToken, aad);
		}

		#endregion
	}
}