using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FishMMO.Auth.Core;
using SecureRemotePassword;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Transport-agnostic SRP-6a authentication service.
	/// Provides server-side and client-side SRP field decryption/encryption,
	/// fake SRP salt derivation for anti-enumeration, and SRP session helpers.
	/// </summary>
	/// <remarks>
	/// <para>Methods that operate on encrypted fields use <see cref="ConnectionEncryptionData"/>
	/// for AES-GCM transport encryption. Methods without encryption data parameters
	/// operate on plaintext (suitable for TLS-protected transports like HTTPS).</para>
	/// </remarks>
	public static class SrpService
	{
		#region Fake SRP

		/// <summary>
		/// Pre-computed fake SRP salt/verifier for non-existent accounts.
		/// Prevents timing-based account enumeration. The salt is AES-GCM encrypted
		/// before transmission (if applicable), so reuse is unobservable on the wire.
		/// </summary>
		/// <remarks>
		/// <b>NOTE:</b> <c>fakeSrpTuple</c> uses compile-time constants (<c>"fake_user"</c>, <c>"fake_password"</c>).
		/// An attacker who decompiles the binary knows the exact fake verifier. This is mitigated by
		/// per-username fake salts (<see cref="DerivePerUsernameFakeSalt"/>) which are the primary defense.
		/// </remarks>
		private static readonly Lazy<(string Salt, string Verifier)> fakeSrpTuple =
			new Lazy<(string, string)>(() =>
			{
				var client = new SrpClient(SrpParameters.Create2048<SHA512>());
				string salt = client.GenerateSalt();
				string priv = client.DerivePrivateKey(salt, "fake_user", "fake_password");
				string verifier = client.DeriveVerifier(priv);
				return (salt, verifier);
			});

		/// <summary>
		/// Returns the pre-computed static fake SRP salt and verifier.
		/// Force-access this at startup to prevent first-use timing side-channels.
		/// </summary>
		/// <returns>Tuple of (Salt, Verifier) strings.</returns>
		public static (string Salt, string Verifier) GetStaticFakeData()
		{
			return fakeSrpTuple.Value;
		}

		/// <summary>
		/// Derives a deterministic per-username fake SRP salt via HMAC-SHA512
		/// so that each non-existent username receives a unique but repeatable salt.
		/// Prevents attackers from detecting salt reuse across different fake accounts.
		/// </summary>
		/// <remarks>
		/// <para>Output is a 128-character lowercase hex string derived from HMAC-SHA512,
		/// matching the length of real SRP salts produced by the SRP library with SHA-512 parameters.
		/// This prevents ciphertext-size oracles from leaking account existence.</para>
		/// </remarks>
		/// <param name="username">The username to derive a fake salt for.</param>
		/// <param name="fakeSaltKey">HMAC-SHA512 key. Must not be null or zeroed.</param>
		/// <returns>Hex-encoded fake salt string, or the static fake salt if the key is unavailable.</returns>
		public static string DerivePerUsernameFakeSalt(string username, byte[] fakeSaltKey)
		{
			// We require an exact-length key for two reasons: (1) HMAC-SHA512 hashes any key
			// longer than its block size (128 bytes) down via SHA-512 first, so accepting an
			// over-long key silently changes the derivation domain and would let two keys
			// produce the same fake-salt mapping; (2) a too-short key is a misconfiguration —
			// the audit-time invariant is that the operator provisioned a freshly-generated
			// 64-byte key. Reject anything else loudly so misconfiguration cannot silently
			// degrade per-username fake-salt uniqueness.
			if (fakeSaltKey == null || fakeSaltKey.Length != CryptoHelper.HmacSha512KeyLength)
				throw new ArgumentException(
					$"fakeSaltKey must be exactly {CryptoHelper.HmacSha512KeyLength} bytes. " +
					"A missing, short, or oversize key indicates server misconfiguration.",
					nameof(fakeSaltKey));
			using (var hmac = new HMACSHA512(fakeSaltKey))
			{
				byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(username));
				return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
			}
		}

		#endregion

		#region Server-Side Encrypted SRP (AES-GCM transport)

		/// <summary>
		/// Decrypts the SRP verify fields (username/email and public ephemeral) from
		/// AES-GCM encrypted payloads using the two-sequence encoding:
		/// seq-1 = username, seq = public ephemeral.
		/// </summary>
		/// <remarks>
		/// <para>Throws <see cref="CryptographicException"/> on decryption/authentication failure.
		/// The caller should treat any CryptographicException as connection-fatal.</para>
		/// <para><b>Sequence atomicity:</b> This method consumes two consecutive sequence numbers
		/// (seq−1, seq). If the first consume succeeds but the second fails (e.g., due to a
		/// concurrent call or counter exhaustion), seq−1 is burned and the receive counter is
		/// left in an inconsistent state. Callers MUST tear down the connection on any <c>false</c>
		/// return — a partially-consumed sequence cannot be recovered.</para>
		/// </remarks>
		/// <param name="encryptedUsername">AES-GCM encrypted username/email bytes.</param>
		/// <param name="encryptedPublicEphemeral">AES-GCM encrypted SRP public ephemeral bytes.</param>
		/// <param name="encryptionData">Connection encryption state (keys, nonces, version).</param>
		/// <param name="seq">The broadcast sequence number (ephemeral's sequence; username = seq-1).</param>
		/// <param name="username">Decrypted username/email string.</param>
		/// <param name="publicEphemeral">Decrypted SRP public ephemeral string.</param>
		/// <returns><c>true</c> if decryption and sequence validation succeeded.</returns>
		public static bool TryDecryptVerifyFields(
			byte[] encryptedUsername,
			byte[] encryptedPublicEphemeral,
			ConnectionEncryptionData encryptionData,
			uint seq,
			out string? username,
			out string? publicEphemeral)
		{
			username = null;
			publicEphemeral = null;

			if (!CryptoHelper.ValidateSequenceRange(seq, 2))
				return false;

			// Consume both sequences ATOMICALLY before decrypting.
			// Previously we consumed seq-1, decrypted, then consumed seq — a partial
			// failure (e.g. second consume races a concurrent reader, or the second
			// decrypt throws) would leave the counter at seq-1 with no way to
			// reconcile. The caller still tears the connection down on a false
			// return, but the atomic consume guarantees the counter is either fully
			// advanced or untouched — no half-state.
			uint seqUsername = seq - 1;
			if (!encryptionData.TryConsumeReceiveSequenceRange(seqUsername, 2))
				return false;

			byte[]? decryptedRawUsername = null;
			byte[]? decryptedRawPublicEphemeral = null;
			try
			{
				byte[] nonce1 = encryptionData.BuildReceiveNonce(seqUsername);
				byte[] aad1 = new byte[CryptoHelper.AadLength];
				CryptoHelper.WriteAad(aad1, (byte)CryptoHelper.AuthMessageType.SrpVerify, encryptionData.AgreedVersion, seqUsername);
				decryptedRawUsername = CryptoHelper.DecryptAES(encryptionData.ClientToServerKey!, nonce1, encryptedUsername, aad1);

				try
				{
					username = CryptoHelper.StrictUtf8.GetString(decryptedRawUsername);
				}
				catch (DecoderFallbackException)
				{
					throw new CryptographicException("Malformed UTF-8 in decrypted username.");
				}

				byte[] nonce2 = encryptionData.BuildReceiveNonce(seq);
				byte[] aad2 = new byte[CryptoHelper.AadLength];
				CryptoHelper.WriteAad(aad2, (byte)CryptoHelper.AuthMessageType.SrpVerify, encryptionData.AgreedVersion, seq);
				decryptedRawPublicEphemeral = CryptoHelper.DecryptAES(encryptionData.ClientToServerKey!, nonce2, encryptedPublicEphemeral, aad2);

				try
				{
					publicEphemeral = CryptoHelper.StrictUtf8.GetString(decryptedRawPublicEphemeral);
				}
				catch (DecoderFallbackException)
				{
					throw new CryptographicException("Malformed UTF-8 in decrypted public ephemeral.");
				}

				return true;
			}
			finally
			{
				if (decryptedRawUsername != null) CryptographicOperations.ZeroMemory(decryptedRawUsername);
				if (decryptedRawPublicEphemeral != null) CryptographicOperations.ZeroMemory(decryptedRawPublicEphemeral);
			}
		}

		/// <summary>
		/// Encrypts the SRP verify response fields (salt + server ephemeral)
		/// for transmission using AES-GCM with SrpVerifyResponse AAD type.
		/// </summary>
		/// <param name="srpSalt">SRP salt string to encrypt.</param>
		/// <param name="srpPublicServerEphemeral">SRP server public ephemeral string to encrypt.</param>
		/// <param name="encryptionData">Connection encryption state.</param>
		/// <param name="encryptedSalt">Encrypted salt output.</param>
		/// <param name="encryptedPublicServerEphemeral">Encrypted server ephemeral output.</param>
		public static void EncryptVerifyResponse(
			string srpSalt,
			string srpPublicServerEphemeral,
			ConnectionEncryptionData encryptionData,
			out byte[] encryptedSalt,
			out byte[] encryptedPublicServerEphemeral)
		{
			uint sendSeq1 = encryptionData.NextSendSequence();
			byte[] sendNonce1 = encryptionData.BuildSendNonce(sendSeq1);
			byte[] aadSend1 = new byte[CryptoHelper.AadLength];
			CryptoHelper.WriteAad(aadSend1, (byte)CryptoHelper.AuthMessageType.SrpVerifyResponse, encryptionData.AgreedVersion, sendSeq1);
			byte[] srpSaltBytes = Encoding.UTF8.GetBytes(srpSalt);
			encryptedSalt = CryptoHelper.EncryptAES(encryptionData.ServerToClientKey!, sendNonce1, srpSaltBytes, aadSend1);
			CryptographicOperations.ZeroMemory(srpSaltBytes);

			uint sendSeq2 = encryptionData.NextSendSequence();
			byte[] sendNonce2 = encryptionData.BuildSendNonce(sendSeq2);
			byte[] aadSend2 = new byte[CryptoHelper.AadLength];
			CryptoHelper.WriteAad(aadSend2, (byte)CryptoHelper.AuthMessageType.SrpVerifyResponse, encryptionData.AgreedVersion, sendSeq2);
			byte[] srpEphemeralBytes = Encoding.UTF8.GetBytes(srpPublicServerEphemeral);
			encryptedPublicServerEphemeral = CryptoHelper.EncryptAES(encryptionData.ServerToClientKey!, sendNonce2, srpEphemeralBytes, aadSend2);
			CryptographicOperations.ZeroMemory(srpEphemeralBytes);
		}

		/// <summary>
		/// Decrypts the SRP proof from an AES-GCM encrypted payload.
		/// </summary>
		/// <remarks>
		/// <para>Throws <see cref="CryptographicException"/> on decryption/authentication failure.</para>
		/// </remarks>
		/// <param name="encryptedProof">AES-GCM encrypted proof bytes.</param>
		/// <param name="encryptionData">Connection encryption state.</param>
		/// <param name="seq">Broadcast sequence number.</param>
		/// <returns>Decrypted proof string.</returns>
		public static string DecryptProof(
			byte[] encryptedProof,
			ConnectionEncryptionData encryptionData,
			uint seq)
		{
			if (!encryptionData.TryConsumeReceiveSequence(seq))
				throw new CryptographicException("SRP proof sequence out-of-order or duplicate.");

			byte[] nonce = encryptionData.BuildReceiveNonce(seq);
			byte[] aad = new byte[CryptoHelper.AadLength];
			CryptoHelper.WriteAad(aad, (byte)CryptoHelper.AuthMessageType.SrpProof, encryptionData.AgreedVersion, seq);
			byte[] decryptedClientProof = CryptoHelper.DecryptAES(encryptionData.ClientToServerKey!, nonce, encryptedProof, aad);
			string clientProof;
			try
			{
				clientProof = CryptoHelper.StrictUtf8.GetString(decryptedClientProof);
			}
			catch (DecoderFallbackException)
			{
				CryptographicOperations.ZeroMemory(decryptedClientProof);
				throw new CryptographicException("Malformed UTF-8 in decrypted client proof.");
			}
			CryptographicOperations.ZeroMemory(decryptedClientProof);
			return clientProof;
		}

		/// <summary>
		/// Encrypts the server proof for transmission using AES-GCM with SrpSuccess AAD type.
		/// </summary>
		/// <param name="serverProof">SRP server proof string.</param>
		/// <param name="encryptionData">Connection encryption state.</param>
		/// <returns>Encrypted server proof bytes.</returns>
		public static byte[] EncryptServerProof(
			string serverProof,
			ConnectionEncryptionData encryptionData)
		{
			uint sendSeq = encryptionData.NextSendSequence();
			byte[] sendNonce = encryptionData.BuildSendNonce(sendSeq);
			byte[] aadSend = new byte[CryptoHelper.AadLength];
			CryptoHelper.WriteAad(aadSend, (byte)CryptoHelper.AuthMessageType.SrpSuccess, encryptionData.AgreedVersion, sendSeq);
			return CryptoHelper.EncryptAES(encryptionData.ServerToClientKey!, sendNonce, Encoding.UTF8.GetBytes(serverProof), aadSend);
		}

		#endregion

		#region Client-Side Encrypted SRP (AES-GCM transport)

		/// <summary>
		/// Encrypts a username for SRP verify transmission from client to server.
		/// </summary>
		/// <param name="username">Plaintext username string.</param>
		/// <param name="clientToServerKey">AES-256 key for client→server direction.</param>
		/// <param name="sendNonceCtx">Client's send nonce context.</param>
		/// <param name="agreedVersion">Negotiated protocol version.</param>
		/// <param name="isRegistration">If true, uses CreateAccount AAD; otherwise SrpVerify AAD.</param>
		/// <param name="encryptedUsername">Encrypted username output.</param>
		/// <param name="seq">Sequence number used (for broadcast).</param>
		public static void ClientEncryptUsername(
			string username,
			byte[] clientToServerKey,
			CryptoHelper.GcmNonceContext sendNonceCtx,
			ushort agreedVersion,
			bool isRegistration,
			out byte[] encryptedUsername,
			out uint seq)
		{
			byte[] usernameBytes = Encoding.UTF8.GetBytes(username);
			try
			{
				var (nonce, seqVal) = sendNonceCtx.NextNonce();
				seq = seqVal;
				var aadType = isRegistration
					? CryptoHelper.AuthMessageType.CreateAccount
					: CryptoHelper.AuthMessageType.SrpVerify;
				byte[] aad = CryptoHelper.BuildAad((byte)aadType, agreedVersion, seq);
				encryptedUsername = CryptoHelper.EncryptAES(clientToServerKey, nonce, usernameBytes, aad);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(usernameBytes);
			}
		}

		/// <summary>
		/// Encrypts the client's SRP public ephemeral for transmission to the server.
		/// </summary>
		/// <param name="publicEphemeral">Client's SRP public ephemeral string.</param>
		/// <param name="clientToServerKey">AES-256 key for client→server direction.</param>
		/// <param name="sendNonceCtx">Client's send nonce context.</param>
		/// <param name="agreedVersion">Negotiated protocol version.</param>
		/// <param name="encryptedEphemeral">Encrypted ephemeral output.</param>
		/// <param name="seq">Sequence number used (for broadcast).</param>
		public static void ClientEncryptEphemeral(
			string publicEphemeral,
			byte[] clientToServerKey,
			CryptoHelper.GcmNonceContext sendNonceCtx,
			ushort agreedVersion,
			out byte[] encryptedEphemeral,
			out uint seq)
		{
			byte[] ephemeralBytes = Encoding.UTF8.GetBytes(publicEphemeral);
			try
			{
				var (nonce, seqVal) = sendNonceCtx.NextNonce();
				seq = seqVal;
				byte[] aad = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpVerify, agreedVersion, seq);
				encryptedEphemeral = CryptoHelper.EncryptAES(clientToServerKey, nonce, ephemeralBytes, aad);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(ephemeralBytes);
			}
		}

		/// <summary>
		/// Decrypts the SRP verify response from the server (salt + server ephemeral).
		/// </summary>
		/// <remarks>
		/// <para>Throws <see cref="CryptographicException"/> on decryption/authentication failure.</para>
		/// </remarks>
		/// <param name="encryptedSalt">AES-GCM encrypted salt from server.</param>
		/// <param name="encryptedPublicEphemeral">AES-GCM encrypted server ephemeral from server.</param>
		/// <param name="serverToClientKey">AES-256 key for server→client direction.</param>
		/// <param name="receiveNonceCtx">Client's receive nonce context.</param>
		/// <param name="agreedVersion">Negotiated protocol version.</param>
		/// <param name="salt">Decrypted salt string.</param>
		/// <param name="publicServerEphemeral">Decrypted server public ephemeral string.</param>
		public static void ClientDecryptVerifyResponse(
			byte[] encryptedSalt,
			byte[] encryptedPublicEphemeral,
			byte[] serverToClientKey,
			CryptoHelper.GcmNonceContext receiveNonceCtx,
			ushort agreedVersion,
			out string salt,
			out string publicServerEphemeral)
		{
			byte[]? decryptedSalt = null;
			byte[]? decryptedRawPublicEphemeral = null;
			try
			{
				var (nonce1, rseq1) = receiveNonceCtx.NextNonce();
				byte[] aad1 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpVerifyResponse, agreedVersion, rseq1);
				decryptedSalt = CryptoHelper.DecryptAES(serverToClientKey, nonce1, encryptedSalt, aad1);

				var (nonce2, rseq2) = receiveNonceCtx.NextNonce();
				byte[] aad2 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpVerifyResponse, agreedVersion, rseq2);
				decryptedRawPublicEphemeral = CryptoHelper.DecryptAES(serverToClientKey, nonce2, encryptedPublicEphemeral, aad2);

				salt = CryptoHelper.StrictUtf8.GetString(decryptedSalt);
				publicServerEphemeral = CryptoHelper.StrictUtf8.GetString(decryptedRawPublicEphemeral);
			}
			catch (DecoderFallbackException)
			{
				throw new CryptographicException("Malformed UTF-8 in SRP verify response.");
			}
			finally
			{
				if (decryptedSalt != null) CryptographicOperations.ZeroMemory(decryptedSalt);
				if (decryptedRawPublicEphemeral != null) CryptographicOperations.ZeroMemory(decryptedRawPublicEphemeral);
			}
		}

		/// <summary>
		/// Encrypts the client's SRP proof for transmission to the server.
		/// </summary>
		/// <param name="proof">Client SRP proof string.</param>
		/// <param name="clientToServerKey">AES-256 key for client→server direction.</param>
		/// <param name="sendNonceCtx">Client's send nonce context.</param>
		/// <param name="agreedVersion">Negotiated protocol version.</param>
		/// <param name="encryptedProof">Encrypted proof output.</param>
		/// <param name="seq">Sequence number used (for broadcast).</param>
		public static void ClientEncryptProof(
			string proof,
			byte[] clientToServerKey,
			CryptoHelper.GcmNonceContext sendNonceCtx,
			ushort agreedVersion,
			out byte[] encryptedProof,
			out uint seq)
		{
			byte[] proofBytes = Encoding.UTF8.GetBytes(proof);
			try
			{
				var (nonce, seqVal) = sendNonceCtx.NextNonce();
				seq = seqVal;
				byte[] aad = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpProof, agreedVersion, seq);
				encryptedProof = CryptoHelper.EncryptAES(clientToServerKey, nonce, proofBytes, aad);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(proofBytes);
			}
		}

		/// <summary>
		/// Decrypts the server's SRP success proof.
		/// </summary>
		/// <remarks>
		/// <para>Throws <see cref="CryptographicException"/> on decryption/authentication failure.</para>
		/// </remarks>
		/// <param name="encryptedProof">AES-GCM encrypted server proof bytes.</param>
		/// <param name="serverToClientKey">AES-256 key for server→client direction.</param>
		/// <param name="receiveNonceCtx">Client's receive nonce context.</param>
		/// <param name="agreedVersion">Negotiated protocol version.</param>
		/// <returns>Decrypted server proof string.</returns>
		public static string ClientDecryptServerProof(
			byte[] encryptedProof,
			byte[] serverToClientKey,
			CryptoHelper.GcmNonceContext receiveNonceCtx,
			ushort agreedVersion)
		{
			byte[]? decryptedProof = null;
			try
			{
				var (nonce, rseq) = receiveNonceCtx.NextNonce();
				byte[] aadProof = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpSuccess, agreedVersion, rseq);
				decryptedProof = CryptoHelper.DecryptAES(serverToClientKey, nonce, encryptedProof, aadProof);

				return CryptoHelper.StrictUtf8.GetString(decryptedProof);
			}
			catch (DecoderFallbackException)
			{
				throw new CryptographicException("Malformed UTF-8 in SRP success proof.");
			}
			finally
			{
				if (decryptedProof != null) CryptographicOperations.ZeroMemory(decryptedProof);
			}
		}

		/// <summary>
		/// Decrypts an auth token from a SRP success message.
		/// </summary>
		/// <param name="encryptedToken">AES-GCM encrypted token bytes.</param>
		/// <param name="serverToClientKey">AES-256 key for server→client direction.</param>
		/// <param name="receiveNonceCtx">Client's receive nonce context.</param>
		/// <param name="agreedVersion">Negotiated protocol version.</param>
		/// <returns>Raw token bytes.</returns>
		public static byte[] ClientDecryptAuthToken(
			byte[] encryptedToken,
			byte[] serverToClientKey,
			CryptoHelper.GcmNonceContext receiveNonceCtx,
			ushort agreedVersion)
		{
			var (tokenNonce, tokenRseq) = receiveNonceCtx.NextNonce();
			byte[] tokenAad = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpSuccess, agreedVersion, tokenRseq);
			return CryptoHelper.DecryptAES(serverToClientKey, tokenNonce, encryptedToken, tokenAad);
		}

		#endregion

		#region Registration Encryption

		/// <summary>
		/// Encrypts registration fields (email, age, salt, verifier) for transmission.
		/// Uses the CreateAccount AAD type for all fields.
		/// </summary>
		/// <param name="email">Email address string.</param>
		/// <param name="age">Age value.</param>
		/// <param name="salt">SRP salt string.</param>
		/// <param name="verifier">SRP verifier string.</param>
		/// <param name="clientToServerKey">AES-256 key for client→server direction.</param>
		/// <param name="sendNonceCtx">Client's send nonce context.</param>
		/// <param name="agreedVersion">Negotiated protocol version.</param>
		/// <param name="encryptedEmail">Encrypted email output.</param>
		/// <param name="encryptedAge">Encrypted age output.</param>
		/// <param name="encryptedSalt">Encrypted salt output.</param>
		/// <param name="encryptedVerifier">Encrypted verifier output.</param>
		/// <param name="verifierSeq">Sequence number of the verifier (used as broadcast Seq).</param>
		public static void ClientEncryptRegistrationFields(
			string email,
			int age,
			string salt,
			string verifier,
			byte[] clientToServerKey,
			CryptoHelper.GcmNonceContext sendNonceCtx,
			ushort agreedVersion,
			out byte[] encryptedEmail,
			out byte[] encryptedAge,
			out byte[] encryptedSalt,
			out byte[] encryptedVerifier,
			out uint verifierSeq)
		{
			byte[] emailBytes = Encoding.UTF8.GetBytes(email ?? "");
			byte[] ageBytes = Encoding.UTF8.GetBytes(age.ToString(CultureInfo.InvariantCulture));
			byte[] saltBytes = Encoding.UTF8.GetBytes(salt);
			byte[] verifierBytes = Encoding.UTF8.GetBytes(verifier);

			try
			{
				var (nonceE, seqE) = sendNonceCtx.NextNonce();
				byte[] aadE = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.CreateAccount, agreedVersion, seqE);
				encryptedEmail = CryptoHelper.EncryptAES(clientToServerKey, nonceE, emailBytes, aadE);

				var (nonceA, seqA) = sendNonceCtx.NextNonce();
				byte[] aadA = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.CreateAccount, agreedVersion, seqA);
				encryptedAge = CryptoHelper.EncryptAES(clientToServerKey, nonceA, ageBytes, aadA);

				var (nonce1, seq1) = sendNonceCtx.NextNonce();
				byte[] aad1 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.CreateAccount, agreedVersion, seq1);
				encryptedSalt = CryptoHelper.EncryptAES(clientToServerKey, nonce1, saltBytes, aad1);

				var (nonce2, seq2) = sendNonceCtx.NextNonce();
				verifierSeq = seq2;
				byte[] aad2 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.CreateAccount, agreedVersion, verifierSeq);
				encryptedVerifier = CryptoHelper.EncryptAES(clientToServerKey, nonce2, verifierBytes, aad2);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(emailBytes);
				CryptographicOperations.ZeroMemory(ageBytes);
				CryptographicOperations.ZeroMemory(saltBytes);
				CryptographicOperations.ZeroMemory(verifierBytes);
			}
		}

		#endregion

		#region TOTP Encryption

		/// <summary>
		/// Encrypts a TOTP code for transmission to the server.
		/// </summary>
		/// <param name="code">TOTP code string.</param>
		/// <param name="clientToServerKey">AES-256 key for client→server direction.</param>
		/// <param name="sendNonceCtx">Client's send nonce context.</param>
		/// <param name="agreedVersion">Negotiated protocol version.</param>
		/// <param name="encryptedCode">Encrypted code output.</param>
		/// <param name="seq">Sequence number used (for broadcast).</param>
		public static void ClientEncryptTotpCode(
			string code,
			byte[] clientToServerKey,
			CryptoHelper.GcmNonceContext sendNonceCtx,
			ushort agreedVersion,
			out byte[] encryptedCode,
			out uint seq)
		{
			byte[] codeBytes = Encoding.UTF8.GetBytes(code);
			try
			{
				var (nonce, seqVal) = sendNonceCtx.NextNonce();
				seq = seqVal;
				byte[] aad = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.TwoFactorVerify, agreedVersion, seq);
				encryptedCode = CryptoHelper.EncryptAES(clientToServerKey, nonce, codeBytes, aad);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(codeBytes);
			}
		}

		/// <summary>
		/// Decrypts a TOTP code from an AES-GCM encrypted payload sent by the client.
		/// </summary>
		/// <param name="encryptedCode">AES-GCM encrypted TOTP code bytes.</param>
		/// <param name="encryptionData">Connection encryption state.</param>
		/// <param name="seq">Broadcast sequence number.</param>
		/// <returns>Decrypted TOTP code string.</returns>
		public static string ServerDecryptTotpCode(
			byte[] encryptedCode,
			ConnectionEncryptionData encryptionData,
			uint seq)
		{
			if (!encryptionData.TryConsumeReceiveSequence(seq))
				throw new CryptographicException("TOTP code sequence out-of-order or duplicate.");

			byte[] nonce = encryptionData.BuildReceiveNonce(seq);
			byte[] aad = new byte[CryptoHelper.AadLength];
			CryptoHelper.WriteAad(aad, (byte)CryptoHelper.AuthMessageType.TwoFactorVerify, encryptionData.AgreedVersion, seq);
			byte[] decryptedCode = CryptoHelper.DecryptAES(encryptionData.ClientToServerKey!, nonce, encryptedCode, aad);
			string totpCode;
			try
			{
				totpCode = CryptoHelper.StrictUtf8.GetString(decryptedCode);
			}
			catch (DecoderFallbackException)
			{
				CryptographicOperations.ZeroMemory(decryptedCode);
				throw new CryptographicException("Malformed UTF-8 in TOTP code.");
			}
			CryptographicOperations.ZeroMemory(decryptedCode);
			return totpCode;
		}

		/// <summary>
		/// Decrypts two-factor setup data (otpauth URI and recovery codes) from the server.
		/// </summary>
		/// <param name="encryptedOtpauthUri">AES-GCM encrypted otpauth URI.</param>
		/// <param name="encryptedRecoveryCodes">AES-GCM encrypted newline-delimited recovery codes.</param>
		/// <param name="serverToClientKey">AES-256 key for server→client direction.</param>
		/// <param name="receiveNonceCtx">Client's receive nonce context.</param>
		/// <param name="agreedVersion">Negotiated protocol version.</param>
		/// <param name="otpauthUri">Decrypted otpauth URI.</param>
		/// <param name="recoveryCodes">Decrypted recovery codes array.</param>
		public static void ClientDecryptTwoFactorSetup(
			byte[] encryptedOtpauthUri,
			byte[] encryptedRecoveryCodes,
			byte[] serverToClientKey,
			CryptoHelper.GcmNonceContext receiveNonceCtx,
			ushort agreedVersion,
			out string otpauthUri,
			out string[] recoveryCodes)
		{
			byte[]? decryptedUri = null;
			byte[]? decryptedCodes = null;
			try
			{
				var (nonce1, rseq1) = receiveNonceCtx.NextNonce();
				byte[] aad1 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.TwoFactorSetup, agreedVersion, rseq1);
				decryptedUri = CryptoHelper.DecryptAES(serverToClientKey, nonce1, encryptedOtpauthUri, aad1);

				var (nonce2, rseq2) = receiveNonceCtx.NextNonce();
				byte[] aad2 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.TwoFactorSetup, agreedVersion, rseq2);
				decryptedCodes = CryptoHelper.DecryptAES(serverToClientKey, nonce2, encryptedRecoveryCodes, aad2);

				otpauthUri = CryptoHelper.StrictUtf8.GetString(decryptedUri);
				string codesStr = CryptoHelper.StrictUtf8.GetString(decryptedCodes);
				recoveryCodes = codesStr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
			}
			catch (DecoderFallbackException)
			{
				throw new CryptographicException("Malformed UTF-8 in 2FA setup data.");
			}
			finally
			{
				if (decryptedUri != null) CryptographicOperations.ZeroMemory(decryptedUri);
				if (decryptedCodes != null) CryptographicOperations.ZeroMemory(decryptedCodes);
			}
		}

		#endregion

		#region Account Verification Encryption

		/// <summary>
		/// Encrypts account verification fields (username + code) for transmission.
		/// </summary>
		/// <param name="username">Username to verify.</param>
		/// <param name="verifyCode">Verification code.</param>
		/// <param name="clientToServerKey">AES-256 key for client→server direction.</param>
		/// <param name="sendNonceCtx">Client's send nonce context.</param>
		/// <param name="agreedVersion">Negotiated protocol version.</param>
		/// <param name="encryptedUsername">Encrypted username output.</param>
		/// <param name="encryptedCode">Encrypted verification code output.</param>
		/// <param name="codeSeq">Sequence number of the code (used as broadcast Seq).</param>
		public static void ClientEncryptAccountVerify(
			string username,
			string verifyCode,
			byte[] clientToServerKey,
			CryptoHelper.GcmNonceContext sendNonceCtx,
			ushort agreedVersion,
			out byte[] encryptedUsername,
			out byte[] encryptedCode,
			out uint codeSeq)
		{
			byte[] usernameBytes = Encoding.UTF8.GetBytes(username);
			byte[] codeBytes = Encoding.UTF8.GetBytes(verifyCode);
			try
			{
				var (nonceU, seqU) = sendNonceCtx.NextNonce();
				byte[] aadU = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.AccountVerify, agreedVersion, seqU);
				encryptedUsername = CryptoHelper.EncryptAES(clientToServerKey, nonceU, usernameBytes, aadU);

				var (nonceC, seqC) = sendNonceCtx.NextNonce();
				codeSeq = seqC;
				byte[] aadC = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.AccountVerify, agreedVersion, codeSeq);
				encryptedCode = CryptoHelper.EncryptAES(clientToServerKey, nonceC, codeBytes, aadC);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(usernameBytes);
				CryptographicOperations.ZeroMemory(codeBytes);
			}
		}

		#endregion
	}
}