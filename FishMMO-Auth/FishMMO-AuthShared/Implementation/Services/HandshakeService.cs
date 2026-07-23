using System;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using FishMMO.Shared;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Transport-agnostic service for X25519 ECDH handshake operations
	/// and stateless cookie challenge management.
	/// Provides both server-side and client-side key agreement,
	/// IP normalisation, and HMAC-SHA256 cookie computation/verification.
	/// </summary>
	public static class HandshakeService
	{
		/// <summary>
		/// Domain separator prepended to cookie HMAC input to prevent cross-purpose
		/// key reuse if the same HMAC key were accidentally shared with another subsystem.
		/// </summary>
		private static readonly byte[] cookieDomainSeparator = Encoding.ASCII.GetBytes("fishmmo-cookie-v1:");

		/// <summary>
		/// Time bucket width in seconds for stateless handshake cookies.
		/// Cookies are valid for the current bucket and the immediately preceding one,
		/// giving a maximum validity window of 2x this value.
		/// </summary>
		public const int CookieTimeBucketSeconds = 30;

		/// <summary>
		/// Identifies the set of cryptographic primitives (curve, hash, KDF) used by this build.
		/// Bound into the handshake transcript hash to prevent cross-suite transcript reuse
		/// and enable future algorithm agility without a full protocol version bump.
		/// Increment when any primitive changes (e.g. X25519→X448, SHA-256→SHA-512).
		/// </summary>
		public const ushort CryptoSuiteId = 1;

		/// <summary>
		/// Domain label for server key confirmation HMAC.
		/// Labels are version-neutral — separation is provided by the protocol version
		/// and <see cref="CryptoSuiteId"/> already bound into the transcript hash.
		/// </summary>
		private static readonly byte[] serverFinishedLabel = Encoding.ASCII.GetBytes("fishmmo finished s");

		/// <summary>
		/// Domain label for client key confirmation HMAC.
		/// Labels are version-neutral — separation is provided by the protocol version
		/// and <see cref="CryptoSuiteId"/> already bound into the transcript hash.
		/// </summary>
		private static readonly byte[] clientFinishedLabel = Encoding.ASCII.GetBytes("fishmmo finished c");

		#region Cookie

		/// <summary>
		/// Returns the current UTC time bucket index used for cookie expiration.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint GetTimeBucket()
		{
			return (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / CookieTimeBucketSeconds);
		}

		/// <summary>
		/// Computes a stateless HMAC-SHA256 handshake cookie binding the client's IP,
		/// public key, and time bucket. The server stores no state — validity is
		/// re-derived on echo.
		/// </summary>
		/// <remarks>
		/// <para><b>Input structure:</b> [domain | timeBucket(4B) | ipLen(2B) | ipBytes | hasConnId(1B) | connId(4B if present) | publicKey].</para>
		/// <para><b>Replay:</b> Cookies are not single-use — a valid cookie may be replayed
		/// within its validity window (up to 2x <see cref="CookieTimeBucketSeconds"/>).
		/// Replay only permits re-attempting the ECDH handshake, which is still gated by
		/// per-IP and global rate limits. <b>IMPORTANT:</b> Those rate limits are NOT
		/// implemented by this service — the transport layer (e.g., FishNet broadcast
		/// handlers) MUST enforce per-IP connection throttling and a global handshake
		/// cap. Without them, the cookie challenge provides only minimal amplification
		/// protection against DDoS.</para>
		/// <para><b>Replay hardening (optional):</b> To further limit replay:
		/// (A) rotate <paramref name="hmacKey"/> periodically via <c>HKDF(masterKey, timeWindow)</c>
		/// so captured cookies become invalid after rotation;
		/// (B) include a per-boot server salt in the HMAC key derivation to invalidate
		/// cookies across server restarts;
		/// (C) require lightweight proof-of-work (hashcash) tied to the cookie.</para>
		/// </remarks>
		/// <param name="remoteIp">Canonical remote IP address string.</param>
		/// <param name="clientPublicKey">Client's X25519 public key (32 bytes).</param>
		/// <param name="timeBucket">Time bucket index from <see cref="GetTimeBucket"/>.</param>
		/// <param name="hmacKey">HMAC-SHA256 signing key.</param>
		/// <param name="connectionId">Optional connection-specific identifier (e.g., transport connection ID)
		/// to distinguish clients behind shared NAT. Pass -1 to omit.</param>
		/// <returns>32-byte HMAC cookie.</returns>
		public static byte[] ComputeHandshakeCookie(string remoteIp, byte[] clientPublicKey, uint timeBucket, byte[] hmacKey, int connectionId = -1)
		{
			if (clientPublicKey == null || clientPublicKey.Length != CryptoHelper.X25519PublicKeyLength)
				throw new ArgumentException($"Client public key must be exactly {CryptoHelper.X25519PublicKeyLength} bytes.", nameof(clientPublicKey));
			if (hmacKey == null || hmacKey.Length != CryptoHelper.HmacKeyLength)
				throw new ArgumentException($"HMAC key must be exactly {CryptoHelper.HmacKeyLength} bytes.", nameof(hmacKey));

			byte[] ipBytes = string.IsNullOrEmpty(remoteIp) ? Array.Empty<byte>() : Encoding.ASCII.GetBytes(remoteIp);
			bool hasConnId = connectionId >= 0;
			int dataLen = cookieDomainSeparator.Length + 4 + 2 + ipBytes.Length + 1 + (hasConnId ? 4 : 0) + clientPublicKey.Length;
			byte[] data = new byte[dataLen];
			int offset = 0;

			Buffer.BlockCopy(cookieDomainSeparator, 0, data, offset, cookieDomainSeparator.Length);
			offset += cookieDomainSeparator.Length;

			data[offset++] = (byte)(timeBucket >> 24);
			data[offset++] = (byte)(timeBucket >> 16);
			data[offset++] = (byte)(timeBucket >> 8);
			data[offset++] = (byte)timeBucket;

			data[offset++] = (byte)(ipBytes.Length >> 8);
			data[offset++] = (byte)ipBytes.Length;

			Buffer.BlockCopy(ipBytes, 0, data, offset, ipBytes.Length);
			offset += ipBytes.Length;

			data[offset++] = hasConnId ? (byte)1 : (byte)0;
			if (hasConnId)
			{
				data[offset++] = (byte)(connectionId >> 24);
				data[offset++] = (byte)(connectionId >> 16);
				data[offset++] = (byte)(connectionId >> 8);
				data[offset++] = (byte)connectionId;
			}

			Buffer.BlockCopy(clientPublicKey, 0, data, offset, clientPublicKey.Length);

			byte[] cookie;
			using (var hmac = new HMACSHA256(hmacKey))
			{
				cookie = hmac.ComputeHash(data);
			}
			CryptographicOperationsCompat.ZeroMemory(data);
			return cookie;
		}

		/// <summary>
		/// Verifies a handshake cookie against a specific time bucket in constant time.
		/// </summary>
		/// <param name="cookie">Cookie bytes received from the client.</param>
		/// <param name="remoteIp">Canonical remote IP address string.</param>
		/// <param name="clientPublicKey">Client's X25519 public key (32 bytes).</param>
		/// <param name="timeBucket">Time bucket index to verify against.</param>
		/// <param name="hmacKey">HMAC-SHA256 signing key.</param>
		/// <param name="connectionId">Optional connection-specific identifier to match <see cref="ComputeHandshakeCookie"/>.</param>
		/// <returns><c>true</c> if the cookie is valid for this time bucket.</returns>
		public static bool VerifyHandshakeCookie(byte[] cookie, string remoteIp, byte[] clientPublicKey, uint timeBucket, byte[] hmacKey, int connectionId = -1)
		{
			if (cookie == null || cookie.Length != CryptoHelper.HmacTagLength)
				return false;
			if (clientPublicKey == null || clientPublicKey.Length != CryptoHelper.X25519PublicKeyLength)
				return false;
			if (hmacKey == null || hmacKey.Length != CryptoHelper.HmacKeyLength)
				return false;

			byte[] expected = ComputeHandshakeCookie(remoteIp, clientPublicKey, timeBucket, hmacKey, connectionId);
			bool valid = CryptoHelper.FixedTimeEquals(cookie, expected);
			CryptographicOperationsCompat.ZeroMemory(expected);
			return valid;
		}

		/// <summary>
		/// Verifies a handshake cookie against the current and immediately preceding time bucket.
		/// Tolerates bucket-boundary crossings by checking both.
		/// </summary>
		/// <param name="cookie">Cookie bytes received from the client.</param>
		/// <param name="remoteIp">Canonical remote IP address string.</param>
		/// <param name="clientPublicKey">Client's X25519 public key (32 bytes).</param>
		/// <param name="hmacKey">HMAC-SHA256 signing key.</param>
		/// <param name="connectionId">Optional connection-specific identifier to match <see cref="ComputeHandshakeCookie"/>.</param>
		/// <returns><c>true</c> if the cookie is valid.</returns>
		public static bool VerifyHandshakeCookieWithRollover(byte[] cookie, string remoteIp, byte[] clientPublicKey, byte[] hmacKey, int connectionId = -1)
		{
			uint currentBucket = GetTimeBucket();
			return VerifyHandshakeCookie(cookie, remoteIp, clientPublicKey, currentBucket, hmacKey, connectionId) ||
				   VerifyHandshakeCookie(cookie, remoteIp, clientPublicKey, currentBucket - 1, hmacKey, connectionId);
		}

		#endregion

		#region IP Normalisation

		/// <summary>
		/// Normalises a raw IP address string to its canonical form via <see cref="IPAddress"/>.
		/// IPv4-mapped IPv6 addresses (e.g., <c>::ffff:192.168.1.1</c>) are collapsed to plain IPv4
		/// so that both representations share a single rate-limit and cookie identity.
		/// Returns an empty string for null/unparseable input.
		/// </summary>
		/// <remarks>
		/// <para><b>Trust model:</b> Callers MUST pass the actual socket remote endpoint address,
		/// NOT values from client-controlled headers such as <c>X-Forwarded-For</c> or
		/// <c>X-Real-IP</c>, unless those headers are authenticated by a trusted reverse proxy.
		/// Accepting spoofed headers would allow attackers to bypass per-IP rate limits and
		/// forge cookie bindings.</para>
		/// <para>When operating behind a load balancer or NAT gateway, consider binding cookies
		/// to an additional connection-specific identifier (e.g., connection ID or port)
		/// to distinguish clients that share the same external IP.</para>
		/// </remarks>
		/// <param name="rawIp">Raw IP address string from the transport layer (must be the actual socket endpoint).</param>
		/// <returns>Canonical IP string, or empty string if invalid.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static string NormalizeIp(string rawIp)
		{
			if (string.IsNullOrEmpty(rawIp))
				return string.Empty;
			if (!IPAddress.TryParse(rawIp, out IPAddress parsed))
				return string.Empty;
			if (parsed.IsIPv4MappedToIPv6)
				parsed = parsed.MapToIPv4();
			return parsed.ToString();
		}

		#endregion

		#region Key Agreement

		/// <summary>
		/// Result of a server-side X25519 key agreement operation.
		/// </summary>
		public struct ServerKeyAgreementResult
		{
			/// <summary>Whether the key agreement succeeded.</summary>
			public bool Success;

			/// <summary>Server's ephemeral public key to send to the client (32 bytes).</summary>
			public byte[] ServerPublicKey;

			/// <summary>Derived directional session keys (client→server and server→client).</summary>
			public CryptoHelper.SessionKeys SessionKeys;

			/// <summary>Negotiated protocol version.</summary>
			public ushort AgreedVersion;

			/// <summary>
			/// Server key confirmation MAC — send to client for verification.
			/// HMAC-SHA256(ServerToClientKey, "fishmmo finished s" || transcriptHash).
			/// </summary>
			public byte[] ServerKeyConfirmation;

			/// <summary>
			/// Expected client key confirmation MAC — verify against the client's response.
			/// HMAC-SHA256(ClientToServerKey, "fishmmo finished c" || transcriptHash).
			/// </summary>
			public byte[] ExpectedClientKeyConfirmation;
		}

		/// <summary>
		/// Performs server-side X25519 ECDH key agreement.
		/// Negotiates protocol version from the client's advertised range, computes a
		/// transcript hash with domain separation and version binding, and derives
		/// directional AES-256 session keys via HKDF.
		/// </summary>
		/// <remarks>
		/// <para>Both ephemeral keypairs are discarded after use for forward secrecy.</para>
		/// <para>The transcript hash binds both public keys and the full version negotiation
		/// to prevent cross-protocol replay and version downgrade attacks.</para>
		/// <para><b>Low-order key rejection:</b> Small-subgroup/low-order public keys are
		/// detected by <see cref="CryptoHelper.X25519EphemeralKeyPair.DeriveSharedSecret"/>,
		/// which throws <see cref="CryptographicException"/> when the raw ECDH output is
		/// all-zero (RFC 7748 §6.1). This method catches that exception and returns
		/// <c>Success=false</c>.</para>
		/// </remarks>
		/// <param name="clientPublicKey">Client's ephemeral X25519 public key (32 bytes).</param>
		/// <param name="clientMinVersion">Minimum protocol version the client supports.</param>
		/// <param name="clientMaxVersion">Maximum protocol version the client supports.</param>
		/// <returns>Result containing server public key, session keys, and agreed version; or <c>Success=false</c> on failure.</returns>
		public static ServerKeyAgreementResult ServerPerformKeyAgreement(
			byte[] clientPublicKey,
			ushort clientMinVersion,
			ushort clientMaxVersion)
		{
			if (clientPublicKey == null || clientPublicKey.Length != CryptoHelper.X25519PublicKeyLength)
				return new ServerKeyAgreementResult { Success = false };

			ushort agreedVersion;
			try
			{
				agreedVersion = CryptoHelper.NegotiateProtocolVersion(clientMinVersion, clientMaxVersion);
			}
			catch (CryptographicException)
			{
				return new ServerKeyAgreementResult { Success = false };
			}

			using var serverKeyPair = new CryptoHelper.X25519EphemeralKeyPair();

			byte[] transcriptHash = computeTranscriptHash(
				clientPublicKey, serverKeyPair.PublicKey,
				clientMinVersion, clientMaxVersion, agreedVersion);

			try
			{
				byte[] sharedSecret = serverKeyPair.DeriveSharedSecret(clientPublicKey, transcriptHash);
				try
				{
					var sessionKeys = CryptoHelper.DeriveSessionKeys(sharedSecret, transcriptHash);

					// Compute key confirmation MACs before zeroing transcript.
					byte[] serverConf = computeKeyConfirmation(sessionKeys.ServerToClientKey, serverFinishedLabel, transcriptHash);
					byte[] expectedClientConf = computeKeyConfirmation(sessionKeys.ClientToServerKey, clientFinishedLabel, transcriptHash);

					// Copy public key before the using block disposes the keypair.
					byte[] serverPubKeyCopy = new byte[serverKeyPair.PublicKey.Length];
					Buffer.BlockCopy(serverKeyPair.PublicKey, 0, serverPubKeyCopy, 0, serverKeyPair.PublicKey.Length);

					return new ServerKeyAgreementResult
					{
						Success = true,
						ServerPublicKey = serverPubKeyCopy,
						SessionKeys = sessionKeys,
						AgreedVersion = agreedVersion,
						ServerKeyConfirmation = serverConf,
						ExpectedClientKeyConfirmation = expectedClientConf,
					};
				}
				finally
				{
					// DeriveSessionKeys zeros masterSecret (=sharedSecret) internally,
					// but zero again defensively in case it threw before reaching its finally.
					CryptographicOperationsCompat.ZeroMemory(sharedSecret);
				}
			}
			catch (CryptographicException)
			{
				return new ServerKeyAgreementResult { Success = false };
			}
			finally
			{
				CryptographicOperationsCompat.ZeroMemory(transcriptHash);
			}
		}

		/// <summary>
		/// Result of a client-side X25519 key agreement operation.
		/// </summary>
		public struct ClientKeyAgreementResult
		{
			/// <summary>Whether the key agreement succeeded.</summary>
			public bool Success;

			/// <summary>Derived directional session keys (client→server and server→client).</summary>
			public CryptoHelper.SessionKeys SessionKeys;

			/// <summary>
			/// Client key confirmation MAC — send to server for verification.
			/// HMAC-SHA256(ClientToServerKey, "fishmmo finished c" || transcriptHash).
			/// </summary>
			public byte[] ClientKeyConfirmation;

			/// <summary>
			/// Expected server key confirmation MAC — verify against the server's response.
			/// HMAC-SHA256(ServerToClientKey, "fishmmo finished s" || transcriptHash).
			/// </summary>
			public byte[] ExpectedServerKeyConfirmation;
		}

		/// <summary>
		/// Performs client-side X25519 ECDH key agreement.
		/// Computes a transcript hash matching the server's computation, derives
		/// directional session keys. The client keypair's private key is consumed
		/// (zeroed) during this call.
		/// </summary>
		/// <param name="serverPublicKey">Server's ephemeral X25519 public key (32 bytes).</param>
		/// <param name="clientKeyPair">Client's ephemeral keypair. Private key is zeroed after use.</param>
		/// <param name="clientMinVersion">Minimum protocol version the client supports.</param>
		/// <param name="clientMaxVersion">Maximum protocol version the client supports.</param>
		/// <param name="serverAgreedVersion">Protocol version the server agreed to.</param>
		/// <returns>Result containing session keys; or <c>Success=false</c> on failure.</returns>
		public static ClientKeyAgreementResult ClientPerformKeyAgreement(
			byte[] serverPublicKey,
			CryptoHelper.X25519EphemeralKeyPair clientKeyPair,
			ushort clientMinVersion,
			ushort clientMaxVersion,
			ushort serverAgreedVersion)
		{
			if (serverPublicKey == null || serverPublicKey.Length != CryptoHelper.X25519PublicKeyLength)
				return new ClientKeyAgreementResult { Success = false };
			if (clientKeyPair == null)
				return new ClientKeyAgreementResult { Success = false };

			byte[] transcriptHash = computeTranscriptHash(
				clientKeyPair.PublicKey, serverPublicKey,
				clientMinVersion, clientMaxVersion, serverAgreedVersion);

			try
			{
				byte[] sharedSecret = clientKeyPair.DeriveSharedSecret(serverPublicKey, transcriptHash);
				try
				{
					var sessionKeys = CryptoHelper.DeriveSessionKeys(sharedSecret, transcriptHash);

					// Compute key confirmation MACs before zeroing transcript.
					byte[] clientConf = computeKeyConfirmation(sessionKeys.ClientToServerKey, clientFinishedLabel, transcriptHash);
					byte[] expectedServerConf = computeKeyConfirmation(sessionKeys.ServerToClientKey, serverFinishedLabel, transcriptHash);

					return new ClientKeyAgreementResult
					{
						Success = true,
						SessionKeys = sessionKeys,
						ClientKeyConfirmation = clientConf,
						ExpectedServerKeyConfirmation = expectedServerConf,
					};
				}
				finally
				{
					// DeriveSessionKeys zeros masterSecret (=sharedSecret) internally,
					// but zero again defensively in case it threw before reaching its finally.
					CryptographicOperationsCompat.ZeroMemory(sharedSecret);
				}
			}
			catch (CryptographicException)
			{
				return new ClientKeyAgreementResult { Success = false };
			}
			finally
			{
				CryptographicOperationsCompat.ZeroMemory(transcriptHash);
			}
		}

		/// <summary>
		/// Computes the handshake transcript hash:
		/// SHA256(domain || clientPub || serverPub || clientMin(2B) || clientMax(2B) || agreed(2B) || cryptoSuiteId(2B)).
		/// Used by both server and client to derive matching session keys.
		/// The crypto suite ID binds the choice of primitives into the transcript,
		/// enabling algorithm agility without a full protocol version bump.
		/// </summary>
		private static byte[] computeTranscriptHash(
			byte[] clientPublicKey,
			byte[] serverPublicKey,
			ushort clientMinVersion,
			ushort clientMaxVersion,
			ushort agreedVersion)
		{
			using (var sha = SHA256.Create())
			{
				sha.TransformBlock(CryptoHelper.HandshakeDomainSeparator, 0, CryptoHelper.HandshakeDomainSeparator.Length, null, 0);
				sha.TransformBlock(clientPublicKey, 0, clientPublicKey.Length, null, 0);
				sha.TransformBlock(serverPublicKey, 0, serverPublicKey.Length, null, 0);

				byte[] versionBytes = new byte[8];
				versionBytes[0] = (byte)(clientMinVersion >> 8);
				versionBytes[1] = (byte)clientMinVersion;
				versionBytes[2] = (byte)(clientMaxVersion >> 8);
				versionBytes[3] = (byte)clientMaxVersion;
				versionBytes[4] = (byte)(agreedVersion >> 8);
				versionBytes[5] = (byte)agreedVersion;
				versionBytes[6] = (byte)(CryptoSuiteId >> 8);
				versionBytes[7] = (byte)CryptoSuiteId;
				sha.TransformFinalBlock(versionBytes, 0, versionBytes.Length);

				return sha.Hash;
			}
		}

		/// <summary>
		/// Computes a key confirmation MAC: HMAC-SHA256(sessionKey, label || transcriptHash).
		/// Used by both sides to prove they derived the same session keys (similar to
		/// TLS Finished / Noise Protocol key confirmation).
		/// </summary>
		private static byte[] computeKeyConfirmation(byte[] sessionKey, byte[] label, byte[] transcriptHash)
		{
			byte[] data = new byte[label.Length + transcriptHash.Length];
			Buffer.BlockCopy(label, 0, data, 0, label.Length);
			Buffer.BlockCopy(transcriptHash, 0, data, label.Length, transcriptHash.Length);
			byte[] mac;
			using (var hmac = new HMACSHA256(sessionKey))
			{
				mac = hmac.ComputeHash(data);
			}
			CryptographicOperationsCompat.ZeroMemory(data);
			return mac;
		}

		/// <summary>
		/// Verifies a peer's key confirmation MAC in constant time.
		/// Call after receiving the peer's confirmation tag to authenticate
		/// that both sides derived identical session keys.
		/// </summary>
		/// <param name="received">The confirmation tag received from the peer.</param>
		/// <param name="expected">The locally computed expected confirmation tag
		/// (from <see cref="ServerKeyAgreementResult.ExpectedClientKeyConfirmation"/>
		/// or <see cref="ClientKeyAgreementResult.ExpectedServerKeyConfirmation"/>).</param>
		/// <returns><c>true</c> if the tags match.</returns>
		public static bool VerifyKeyConfirmation(byte[] received, byte[] expected)
		{
			if (received == null || received.Length != CryptoHelper.HmacTagLength)
				return false;
			if (expected == null || expected.Length != CryptoHelper.HmacTagLength)
				return false;
			return CryptoHelper.FixedTimeEquals(received, expected);
		}

		#endregion
	}
}