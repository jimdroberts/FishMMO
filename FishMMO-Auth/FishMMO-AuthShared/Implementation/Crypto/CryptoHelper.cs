using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using OtpNet;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Security;
using FishMMO.Auth.Core;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Static class providing cryptographic helper methods for X25519 ECDH key agreement,
	/// AES-256-GCM authenticated encryption, HKDF-SHA256 key derivation, HMAC-SHA256 signing,
	/// and authentication token management. BouncyCastle is used for cross-platform support
	/// on all Unity targets.
	/// </summary>
	public static class CryptoHelper
	{
		/// <summary>
		/// Maximum allowed size in bytes for any single encrypted SRP payload field.
		/// Prevents oversized payloads from consuming AES decryption CPU on workers.
		/// Enforcement is at the protocol layer (server broadcast handlers) rather than
		/// inside crypto helpers, because the limit is transport-specific.
		/// </summary>
		public const int MaxSrpPayloadBytes = 1024;

		/// <summary>
		/// Maximum allowed token lifetime in minutes for <see cref="BuildAuthToken"/>.
		/// Caps the blast radius of a token compromise by preventing tokens with
		/// excessively long validity windows.
		/// </summary>
		public const int MaxTokenLifetimeMinutes = 60;

		/// <summary>
		/// Length in bytes of an X25519 public key.
		/// </summary>
		public const int X25519PublicKeyLength = 32;

		/// <summary>
		/// Domain separation prefix hashed into the handshake transcript.
		/// Prevents cross-protocol transcript reuse and future downgrade attacks.
		/// </summary>
		public static readonly byte[] HandshakeDomainSeparator = Encoding.ASCII.GetBytes("fishmmo-handshake-v1");

		/// <summary>
		/// Builds AAD from protocol metadata to be bound into AES-GCM authentication.
		/// Layout: [1-byte messageType][2-byte version big-endian][4-byte sequence big-endian].
		/// </summary>
		public static byte[] BuildAad(byte messageType, ushort version, uint sequence)
		{
			var aad = new byte[AadLength];
			WriteAad(aad, messageType, version, sequence);
			return aad;
		}

		/// <summary>
		/// Length in bytes of an AAD buffer produced by <see cref="BuildAad"/>.
		/// </summary>
		public const int AadLength = 7;

		/// <summary>
		/// Writes AAD directly into a caller-supplied buffer, avoiding a heap allocation.
		/// Use on hot paths (e.g., per-field encryption in a multi-field message) where
		/// thousands of 7-byte arrays per second would pressure the GC.
		/// </summary>
		/// <param name="destination">Must be at least <see cref="AadLength"/> bytes.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void WriteAad(byte[] destination, byte messageType, ushort version, uint sequence)
		{
			destination[0] = messageType;
			destination[1] = (byte)(version >> 8);
			destination[2] = (byte)version;
			destination[3] = (byte)(sequence >> 24);
			destination[4] = (byte)(sequence >> 16);
			destination[5] = (byte)(sequence >> 8);
			destination[6] = (byte)sequence;
		}

		/// <summary>
		/// Validates that a base sequence number is large enough to derive <paramref name="fieldCount"/>
		/// sub-sequences via <c>seq - (fieldCount - 1)</c> through <c>seq</c> without underflow.
		/// Use before any <c>seq - N</c> arithmetic in protocol handlers to prevent uint wrap-around.
		/// </summary>
		/// <param name="seq">The base (highest) sequence number from the message.</param>
		/// <param name="fieldCount">Total number of fields encoded (e.g., 5 for CreateAccount, 2 for SrpVerify).</param>
		/// <returns><c>true</c> if <c>seq >= fieldCount - 1</c>; <c>false</c> if subtraction would underflow.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool ValidateSequenceRange(uint seq, uint fieldCount)
		{
			return fieldCount <= 1 || seq >= (fieldCount - 1);
		}

		/// <summary>
		/// Simple HKDF-SHA256 extract-and-expand (RFC 5869).
		/// </summary>
		/// <remarks>
		/// <para><b>Salt behavior:</b> When <paramref name="salt"/> is <c>null</c>, the code passes
		/// <c>new byte[32]</c> (32 zero bytes) to BouncyCastle's <c>HkdfParameters</c>, satisfying
		/// RFC 5869 §2.2 which mandates a <c>HashLen</c>-length zero salt when none is provided.
		/// The info parameter defaults to <c>Array.Empty&lt;byte&gt;()</c> when <c>null</c> (RFC 5869
		/// §2.2 allows empty info).</para>
		/// </remarks>
		private static byte[] HkdfSha256(byte[] salt, byte[] ikm, byte[] info, int outputLength)
		{
			if (ikm == null) throw new ArgumentNullException(nameof(ikm));
			// RFC 5869 §2.2: salt defaults to HashLen zeros, info defaults to empty.
			var hkdf = new HkdfBytesGenerator(new Sha256Digest());
			var parameters = new HkdfParameters(ikm, salt ?? new byte[32], info ?? Array.Empty<byte>());
			hkdf.Init(parameters);
			byte[] okm = new byte[outputLength];
			hkdf.GenerateBytes(okm, 0, okm.Length);
			return okm;
		}

		/// <summary>
		/// Container for derived session keys and prefixes.
		/// </summary>
		/// <remarks>
		/// <para><b>Mutability:</b> The byte[] fields are mutable references. Callers must
		/// not share or alias them, and <b>must</b> zeroize via
		/// <see cref="CryptographicOperations.ZeroMemory"/> when no longer needed.</para>
		/// <para><b>IDisposable usage:</b> Implement <c>using var keys = DeriveSessionKeys(…);</c>
		/// to ensure <see cref="Dispose"/> zeroes all key material when the scope exits.
		/// Because this is a <c>readonly struct</c>, the compiler calls <see cref="Dispose"/>
		/// directly on the variable without boxing.</para>
		/// </remarks>
		public readonly struct SessionKeys : IDisposable
		{
			public readonly byte[] ClientToServerKey;
			public readonly byte[] ServerToClientKey;
			public readonly byte[] ClientPrefix; // 4 bytes
			public readonly byte[] ServerPrefix; // 4 bytes

			public SessionKeys(byte[] clientToServerKey, byte[] serverToClientKey, byte[] clientPrefix, byte[] serverPrefix)
			{
				ClientToServerKey = clientToServerKey;
				ServerToClientKey = serverToClientKey;
				ClientPrefix = clientPrefix;
				ServerPrefix = serverPrefix;
			}

			/// <summary>
			/// Zeroes all key material referenced by this instance.
			/// </summary>
			/// <remarks>
			/// <para><b>Ownership:</b> SessionKeys is a transient container whose fields are
			/// typically copied into long-lived connection state after derivation. Calling
			/// ZeroAll() only zeroes the arrays referenced by THIS struct — it does not
			/// affect copies held by <c>ConnectionEncryptionData</c> or other consumers.
			/// Callers must manage key lifetime at the connection level.</para>
			/// </remarks>
			public void ZeroAll()
			{
				if (ClientToServerKey != null) CryptographicOperations.ZeroMemory(ClientToServerKey);
				if (ServerToClientKey != null) CryptographicOperations.ZeroMemory(ServerToClientKey);
				if (ClientPrefix != null) CryptographicOperations.ZeroMemory(ClientPrefix);
				if (ServerPrefix != null) CryptographicOperations.ZeroMemory(ServerPrefix);
			}

			/// <summary>
			/// Zeroes all key material. Equivalent to <see cref="ZeroAll"/>.
			/// Enables <c>using var keys = …;</c> for deterministic cleanup.
			/// </summary>
			public void Dispose() => ZeroAll();
		}

		/// <summary>
		/// Derives directional AES keys and per-direction session prefixes from a single master secret.
		/// The <paramref name="handshakeTranscriptHash"/> MUST be bound into the derivation to prevent handshake tampering.
		/// </summary>
		/// <remarks>
		/// <para><b>Rekey strategy:</b> Callers should trigger a rekey (regenerate master secret and
		/// re-derive session keys) under any of the following conditions:</para>
		/// <list type="bullet">
		/// <item>The GCM nonce counter approaches <see cref="MaxGcmNonceCounter"/>.</item>
		/// <item>A configurable time interval elapses (recommended: every 10–30 minutes).</item>
		/// <item>A server transfer or reconnection occurs.</item>
		/// </list>
		/// <para>Rekeying provides forward secrecy and bounds the blast radius of any single key compromise.</para>
		/// <para><b>Reconnect safety:</b> Session prefixes are derived from the master secret.
		/// A fresh master secret MUST be negotiated on every new connection or reconnect
		/// to guarantee prefix uniqueness. Reusing a master secret across connections would
		/// produce identical prefixes and catastrophically break GCM nonce uniqueness.</para>
		/// <para><b>Key material lifetime:</b> This method zeroes <paramref name="masterSecret"/>
		/// in its <c>finally</c> block before returning — even if HKDF throws mid-derivation.
		/// This is intentional: from a security perspective, a partially-derived state must not
		/// leave the master secret accessible. Callers must not reference <paramref name="masterSecret"/>
		/// after this call. On HKDF failure, callers should tear down the connection and
		/// re-handshake to negotiate a fresh master secret.</para>
		/// </remarks>
		public static SessionKeys DeriveSessionKeys(byte[] masterSecret, byte[] handshakeTranscriptHash, int aesKeyLength = 32)
		{
			if (masterSecret == null) throw new ArgumentNullException(nameof(masterSecret));
			if (handshakeTranscriptHash == null) throw new ArgumentNullException(nameof(handshakeTranscriptHash));
			if (handshakeTranscriptHash.Length < 32)
				throw new ArgumentException("Transcript hash must be at least 32 bytes (SHA-256 output).", nameof(handshakeTranscriptHash));

			// Use the transcript hash as salt to HKDF so the handshake is bound into keys.
			byte[] salt = handshakeTranscriptHash;
			try
			{
				return new SessionKeys(
					HkdfSha256(salt, masterSecret, Encoding.ASCII.GetBytes("fishmmo v1 client-to-server"), aesKeyLength),
					HkdfSha256(salt, masterSecret, Encoding.ASCII.GetBytes("fishmmo v1 server-to-client"), aesKeyLength),
					HkdfSha256(salt, masterSecret, Encoding.ASCII.GetBytes("fishmmo v1 client-prefix"), SessionPrefixLength),
					HkdfSha256(salt, masterSecret, Encoding.ASCII.GetBytes("fishmmo v1 server-prefix"), SessionPrefixLength)
				);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(masterSecret);
			}
		}

		/// <summary>
		/// Disposable container for an ephemeral X25519 keypair that enforces private-key
		/// zeroization via API ownership. The private key is never exposed to callers.
		/// </summary>
		/// <remarks>
		/// <para><b>Usage pattern:</b></para>
		/// <code>
		/// using var kp = new CryptoHelper.X25519EphemeralKeyPair();
		/// byte[] shared = kp.DeriveSharedSecret(peerPub, transcript);
		/// // kp.Dispose() is automatic — private key is zeroed even on exception.
		/// </code>
		/// <para><b>Single-use:</b> <see cref="DeriveSharedSecret"/> zeros the private key after
		/// the first call. Subsequent calls throw <see cref="InvalidOperationException"/>.
		/// <see cref="Dispose"/> is idempotent and safe to call multiple times.</para>
		/// </remarks>
		public sealed class X25519EphemeralKeyPair : IDisposable
		{
			private byte[]? privateKey;
			private bool consumed;
			private bool disposed;

			/// <summary>
			/// The X25519 public key (32 bytes). Not secret — safe to transmit.
			/// </summary>
			public byte[] PublicKey { get; }

			/// <summary>
			/// Generates a fresh ephemeral X25519 keypair using BouncyCastle's SecureRandom.
			/// </summary>
			public X25519EphemeralKeyPair()
			{
				privateKey = new byte[32];
				PublicKey = new byte[32];
				var priv = new X25519PrivateKeyParameters(new SecureRandom());
				priv.Encode(privateKey, 0);
				var pub = priv.GeneratePublicKey();
				pub.Encode(PublicKey, 0);
			}

			/// <summary>
			/// Derives a shared secret via X25519 ECDH + HKDF-SHA256.
			/// Automatically zeros the private key after derivation (single use).
			/// </summary>
			/// <param name="peerPublicKey">The peer's X25519 public key (32 bytes).</param>
			/// <param name="handshakeTranscriptHash">Transcript hash bound into HKDF salt.</param>
			/// <returns>A 32-byte HKDF-derived shared secret.</returns>
			public byte[] DeriveSharedSecret(byte[] peerPublicKey, byte[] handshakeTranscriptHash)
			{
				if (disposed) throw new ObjectDisposedException(nameof(X25519EphemeralKeyPair));
				if (consumed) throw new InvalidOperationException("Private key has already been consumed. Ephemeral keypairs are single-use.");

				try
				{
					return DeriveX25519SharedSecret(privateKey!, peerPublicKey, handshakeTranscriptHash);
				}
				finally
				{
					consumed = true;
					ZeroPrivateKey();
				}
			}

			/// <summary>
			/// Zeros the private key and marks the instance as disposed.
			/// </summary>
			public void Dispose()
			{
				if (!disposed)
				{
					ZeroPrivateKey();
					disposed = true;
				}
			}

			private void ZeroPrivateKey()
			{
				if (privateKey != null)
				{
					CryptographicOperations.ZeroMemory(privateKey);
					privateKey = null;
				}
			}
		}

		/// <summary>
		/// Internal: Generate X25519 keypair (private/public).
		/// Prefer <see cref="X25519EphemeralKeyPair"/> which enforces private key zeroization.
		/// </summary>
		private static void GenerateX25519Keypair(out byte[] privateKey, out byte[] publicKey)
		{
			privateKey = new byte[32];
			publicKey = new byte[32];
			// Use BouncyCastle's SecureRandom for proper key generation with internal clamping.
			var priv = new Org.BouncyCastle.Crypto.Parameters.X25519PrivateKeyParameters(new SecureRandom());
			priv.Encode(privateKey, 0);
			var pub = priv.GeneratePublicKey();
			pub.Encode(publicKey, 0);
		}

		/// <summary>
		/// Internal: Derives a key from an X25519 key agreement using HKDF-SHA256.
		/// The raw ECDH output is never returned directly — it is fed through HKDF
		/// to produce a uniformly random 32-byte key.
		/// </summary>
		/// <remarks>
		/// <para>This is the low-level primitive used by <see cref="X25519EphemeralKeyPair.DeriveSharedSecret"/>.
		/// External callers should use the ephemeral keypair wrapper which enforces private key zeroization.</para>
		/// </remarks>
		private static byte[] DeriveX25519SharedSecret(byte[] ourPrivateKey, byte[] peerPublicKey, byte[] handshakeTranscriptHash)
		{
			if (ourPrivateKey == null) throw new ArgumentNullException(nameof(ourPrivateKey));
			if (peerPublicKey == null) throw new ArgumentNullException(nameof(peerPublicKey));
			if (handshakeTranscriptHash == null) throw new ArgumentNullException(nameof(handshakeTranscriptHash));
			// Reject known small-order points BEFORE running ECDH.
			// The post-ECDH all-zero check below catches the same class of input,
			// but pre-checking gives a clearer error path and protects against any
			// future BouncyCastle behaviour change.
			if (!IsValidX25519PublicKey(peerPublicKey))
				throw new CryptographicException("X25519 peer public key is a known small-order point.");
			var priv = new Org.BouncyCastle.Crypto.Parameters.X25519PrivateKeyParameters(ourPrivateKey, 0);
			var pub = new Org.BouncyCastle.Crypto.Parameters.X25519PublicKeyParameters(peerPublicKey, 0);
			byte[] rawShared = new byte[32];
			priv.GenerateSecret(pub, rawShared, 0);

			// RFC 7748 §6.1: reject all-zero output produced by small-order public keys.
			// These are the ~12 low-order points on Curve25519 that collapse the shared
			// secret to zero regardless of the private key. BouncyCastle does not check
			// for this internally.
			bool allZero = true;
			for (int i = 0; i < rawShared.Length; i++)
			{
				if (rawShared[i] != 0) { allZero = false; break; }
			}
			if (allZero)
			{
				CryptographicOperations.ZeroMemory(rawShared);
				throw new CryptographicException("X25519 produced all-zero shared secret (peer sent a small-order public key).");
			}

			// Never use raw ECDH output directly — derive a uniform key via HKDF.
			byte[] derived = HkdfSha256(handshakeTranscriptHash, rawShared, Encoding.ASCII.GetBytes("fishmmo v1 x25519"), 32);
			CryptographicOperations.ZeroMemory(rawShared);
			return derived;
		}

		/// <summary>
		/// Validates a peer-supplied X25519 public key against the
		/// well-known small-order point blacklist (RFC 7748 §6.1). Returns <c>false</c>
		/// for any input that would collapse the ECDH shared secret to a predictable
		/// value regardless of the local private key.
		/// </summary>
		/// <remarks>
		/// <para>The blacklist below covers the seven distinct small-order x-coordinates
		/// plus their high-bit-set variants (X25519 implementations mask off the top
		/// bit, so both forms must be rejected explicitly).</para>
		/// <para>Comparison is byte-wise but constant-time per entry to avoid leaking
		/// which (if any) blacklist entry matched. This is informational — a malicious
		/// peer already knows their own public key — but reduces the risk of side-channel
		/// regressions in the surrounding handshake code.</para>
		/// </remarks>
		/// <param name="publicKey">32-byte X25519 public key (peer-supplied).</param>
		/// <returns><c>true</c> if the key is not on the small-order blacklist.</returns>
		public static bool IsValidX25519PublicKey(byte[] publicKey)
		{
			if (publicKey == null || publicKey.Length != 32) return false;
			int matchAccumulator = 0;
			for (int i = 0; i < smallOrderX25519Points.Length; i++)
			{
				byte[] bad = smallOrderX25519Points[i];
				int diff = 0;
				for (int j = 0; j < 32; j++)
					diff |= publicKey[j] ^ bad[j];
				// diff == 0  =>  match; OR (1 - clamp(diff)) into accumulator.
				matchAccumulator |= ((diff - 1) >> 31) & 1;
			}
			return matchAccumulator == 0;
		}

		// RFC 7748 §6.1 small-order point blacklist. Both low-bit-cleared and
		// high-bit-set forms are listed because the high bit is masked by every
		// conforming X25519 implementation, making both byte-patterns yield the
		// same curve point.
		private static readonly byte[][] smallOrderX25519Points = new byte[][]
		{
			// 0 (point at infinity)
			new byte[32],
			// 1
			new byte[] { 0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
						 0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00 },
			// 325606250916557431795983626356110631294008115727848805560023387167927233504
			new byte[] { 0xe0,0xeb,0x7a,0x7c,0x3b,0x41,0xb8,0xae,0x16,0x56,0xe3,0xfa,0xf1,0x9f,0xc4,0x6a,
						 0xda,0x09,0x8d,0xeb,0x9c,0x32,0xb1,0xfd,0x86,0x62,0x05,0x16,0x5f,0x49,0xb8,0x00 },
			// 39382357235489614581723060781553021112529911719440698176882885853963445705823
			new byte[] { 0x5f,0x9c,0x95,0xbc,0xa3,0x50,0x8c,0x24,0xb1,0xd0,0xb1,0x55,0x9c,0x83,0xef,0x5b,
						 0x04,0x44,0x5c,0xc4,0x58,0x1c,0x8e,0x86,0xd8,0x22,0x4e,0xdd,0xd0,0x9f,0x11,0x57 },
			// p - 1
			new byte[] { 0xec,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,
						 0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0x7f },
			// p
			new byte[] { 0xed,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,
						 0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0x7f },
			// p + 1
			new byte[] { 0xee,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,
						 0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0x7f },
			// High-bit-set variants (top bit ignored by X25519, so these alias the above).
			new byte[] { 0xe0,0xeb,0x7a,0x7c,0x3b,0x41,0xb8,0xae,0x16,0x56,0xe3,0xfa,0xf1,0x9f,0xc4,0x6a,
						 0xda,0x09,0x8d,0xeb,0x9c,0x32,0xb1,0xfd,0x86,0x62,0x05,0x16,0x5f,0x49,0xb8,0x80 },
			new byte[] { 0x5f,0x9c,0x95,0xbc,0xa3,0x50,0x8c,0x24,0xb1,0xd0,0xb1,0x55,0x9c,0x83,0xef,0x5b,
						 0x04,0x44,0x5c,0xc4,0x58,0x1c,0x8e,0x86,0xd8,0x22,0x4e,0xdd,0xd0,0x9f,0x11,0xd7 },
			new byte[] { 0xec,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,
						 0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff },
			new byte[] { 0xed,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,
						 0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff },
			new byte[] { 0xee,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,
						 0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff,0xff },
		};

		/// <summary>
		/// Generates a cryptographically secure random key of the specified length in bytes.
		/// </summary>
		/// <param name="length">Length of the key in bytes.</param>
		/// <returns>Randomly generated key as a byte array.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte[] GenerateKey(int length)
		{
			if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
			byte[] key = new byte[length];
			RandomNumberGenerator.Fill(key);
			return key;
		}

		/// <summary>
		/// Convenience wrapper around <see cref="CryptographicOperations.ZeroMemory"/> for code clarity.
		/// Zeroes all bytes in the given key material.
		/// Call on disconnect, logout, or rekey to destroy sensitive key material.
		/// Null arrays are safely ignored.
		/// </summary>
		/// <param name="key">The key material to zeroize, or null.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Destroy(byte[] key)
		{
			if (key != null)
				CryptographicOperations.ZeroMemory(key);
		}

		/// <summary>
		/// Builds a 12-byte GCM nonce from a session prefix, explicit message sequence number,
		/// and direction flag. Callers MUST provide an explicit, monotonic sequence number
		/// (for example obtained via <c>Interlocked.Increment</c>) to avoid implicit ordering.
		/// Layout: [4-byte prefix][1-byte direction][7-byte counter big-endian].
		/// The direction byte prevents collision between client→server and server→client nonces
		/// for the same sequence number.
		/// </summary>
		/// <remarks>
		/// <para><b>Prefix uniqueness:</b> Each session’s prefix is derived from a unique
		/// master secret via <see cref="DeriveSessionKeys"/>, giving statistical uniqueness
		/// within a 4-byte (2³²) space. Combined with the direction byte and monotonic counter,
		/// the full 12-byte nonce is unique per (session, direction, message) tuple.
		/// Callers must ensure that each session uses a fresh master secret so that
		/// prefixes do not repeat across sessions.</para>
		/// <para>The 7-byte counter supports up to 2^56 − 1 messages per direction — centuries at 1 M packets/sec.
		/// Callers should rekey the session well before exhaustion (see <see cref="DeriveSessionKeys"/> remarks).</para>
		/// </remarks>
		/// <param name="sessionPrefix">4-byte random prefix unique to the session.</param>
		/// <param name="counter">Explicit message sequence number (ulong). Must not exceed <see cref="MaxGcmNonceCounter"/>.</param>
		/// <param name="serverToClient">
		/// <c>true</c> for server→client messages; <c>false</c> for client→server messages.
		/// </param>
		/// <returns>A 12-byte nonce suitable for AES-GCM.</returns>
		/// <exception cref="CryptographicException">Thrown if <paramref name="counter"/> exceeds <see cref="MaxGcmNonceCounter"/>.</exception>
		public static byte[] BuildGcmNonce(byte[] sessionPrefix, ulong counter, bool serverToClient)
		{
			if (sessionPrefix == null || sessionPrefix.Length != SessionPrefixLength)
				throw new ArgumentException($"Session prefix must be exactly {SessionPrefixLength} bytes.", nameof(sessionPrefix));
			if (counter > MaxGcmNonceCounter)
				throw new CryptographicException("GCM nonce counter exhausted; session must be rekeyed.");

			byte[] nonce = new byte[GcmNonceLength];
			Buffer.BlockCopy(sessionPrefix, 0, nonce, 0, SessionPrefixLength);

			// Layout: [prefix(4)] [direction(1)] [counter big-endian(7)]
			nonce[SessionPrefixLength] = serverToClient ? (byte)1 : (byte)0;
			nonce[5] = (byte)(counter >> 48);
			nonce[6] = (byte)(counter >> 40);
			nonce[7] = (byte)(counter >> 32);
			nonce[8] = (byte)(counter >> 24);
			nonce[9] = (byte)(counter >> 16);
			nonce[10] = (byte)(counter >> 8);
			nonce[11] = (byte)counter;

			return nonce;
		}

		/// <summary>
		/// Required length for a GCM session prefix in bytes.
		/// </summary>
		public const int SessionPrefixLength = 4;

		/// <summary>
		/// Protocol version used for AAD binding. Increment when protocol-level changes occur.
		/// </summary>
		public const ushort ProtocolVersion = 1;

		/// <summary>
		/// Minimum protocol version this build supports.
		/// </summary>
		public const ushort MinSupportedProtocolVersion = 1;

		/// <summary>
		/// Maximum protocol version this build supports.
		/// </summary>
		public const ushort MaxSupportedProtocolVersion = 1;

		/// <summary>
		/// Negotiates the highest common protocol version between local and peer version ranges.
		/// Called by the server after receiving the client's version range in <c>ClientHandshake</c>.
		/// The agreed version is bound into HKDF labels and AAD for all subsequent messages.
		/// </summary>
		/// <param name="peerMin">Peer's minimum supported version.</param>
		/// <param name="peerMax">Peer's maximum supported version.</param>
		/// <returns>The highest mutually supported version.</returns>
		/// <exception cref="CryptographicException">Thrown if no common version exists.</exception>
		public static ushort NegotiateProtocolVersion(ushort peerMin, ushort peerMax)
		{
			if (peerMin > peerMax)
				throw new CryptographicException($"Peer version range is invalid: [{peerMin}..{peerMax}].");
			ushort agreedMax = Math.Min(MaxSupportedProtocolVersion, peerMax);
			ushort agreedMin = Math.Max(MinSupportedProtocolVersion, peerMin);
			if (agreedMin > agreedMax)
				throw new CryptographicException(
					$"No common protocol version: local [{MinSupportedProtocolVersion}..{MaxSupportedProtocolVersion}], peer [{peerMin}..{peerMax}].");
			return agreedMax;
		}

		/// <summary>
		/// Message type identifiers for AAD binding. Values are fixed and must be used by both client and server.
		/// </summary>
		// DO NOT RENUMBER — changing values breaks authentication between all client/server versions.
		// Deprecated values must remain reserved and must not be reused.
		public enum AuthMessageType : byte
		{
			ClientHandshake = 0x01,
			ServerHandshake = 0x02,
			SrpVerify = 0x03, // client->server
			SrpVerifyResponse = 0x04, // server->client
			SrpProof = 0x05, // client->server
			SrpSuccess = 0x06, // server->client
			ClientAuthResult = 0x07,
			CreateAccount = 0x08,
			TokenAuth = 0x09,
			AccountVerify = 0x0A, // client->server
			TwoFactorSetup = 0x0B, // server->client (2FA setup data after registration)
			TwoFactorVerify = 0x0C, // client->server (TOTP code during login)
			TokenTransfer = 0x0D, // server->client (encrypted token payload)
		}

		/// <summary>
		/// Required length for a GCM nonce in bytes.
		/// </summary>
		public const int GcmNonceLength = 12;

		/// <summary>
		/// Maximum GCM nonce counter value (7 bytes = 2^56 − 1).
		/// This is the theoretical nonce-space limit for the 7-byte counter field in the
		/// 12-byte GCM nonce layout. In practice, <see cref="GcmNonceContext.NextNonce"/>
		/// caps the counter at <see cref="uint.MaxValue"/> (2^32 − 1) because the sequence
		/// numbers exchanged in wire messages are 32-bit. <see cref="GcmNonceContext.ShouldRekey"/>
		/// uses uint.MaxValue as the practical maximum accordingly.
		/// </summary>
		public const ulong MaxGcmNonceCounter = 0x00FFFFFFFFFFFFFF;

		/// <summary>
		/// Disposable container that owns a GCM session prefix and provides thread-safe
		/// nonce generation. Zeroes the prefix on dispose to prevent accidental reuse
		/// across sessions.
		/// </summary>
		/// <remarks>
		/// <para><b>Session isolation:</b> Each <see cref="GcmNonceContext"/> copies the prefix
		/// at construction time. Disposing the context zeroes the copy, making it impossible
		/// to generate new nonces with the old prefix. This structurally prevents the
		/// catastrophic nonce-reuse scenario where a stale prefix from a previous session
		/// is accidentally used with a new counter.</para>
		/// <para><b>Thread safety:</b> Counter increments use <see cref="Interlocked.Increment(ref long)"/>.
		/// Multiple threads may call <see cref="NextNonce"/> concurrently.</para>
		/// </remarks>
		/// <summary>
		/// Traffic direction for <see cref="GcmNonceContext"/>.
		/// Selects the direction byte embedded in each GCM nonce to guarantee that
		/// client→server and server→client nonces are always distinct, even when
		/// derived from the same counter value and session prefix.
		/// </summary>
		public enum NonceSide : byte
		{
			/// <summary>Client→server direction (nonce direction byte = 0x00).</summary>
			ClientToServer = 0,
			/// <summary>Server→client direction (nonce direction byte = 0x01).</summary>
			ServerToClient = 1,
		}

		public sealed class GcmNonceContext : IDisposable
		{
			private byte[]? prefix;
			private readonly bool serverToClient;
			private long counter;
			private bool disposed;

			/// <summary>
			/// Creates a nonce context that copies the session prefix.
			/// </summary>
			/// <param name="sessionPrefix">4-byte session prefix (copied, not aliased).</param>
			/// <param name="side">Traffic direction — determines the direction byte in generated nonces.</param>
			public GcmNonceContext(byte[] sessionPrefix, NonceSide side)
				: this(sessionPrefix, side == NonceSide.ServerToClient)
			{
			}

			/// <summary>
			/// Creates a nonce context that copies the session prefix.
			/// Prefer the <see cref="NonceSide"/> overload for clarity.
			/// </summary>
			/// <param name="sessionPrefix">4-byte session prefix (copied, not aliased).</param>
			/// <param name="serverToClient"><c>true</c> for server→client direction; <c>false</c> for client→server.</param>
			public GcmNonceContext(byte[] sessionPrefix, bool serverToClient)
			{
				if (sessionPrefix == null || sessionPrefix.Length != SessionPrefixLength)
					throw new ArgumentException($"Session prefix must be exactly {SessionPrefixLength} bytes.", nameof(sessionPrefix));
				prefix = new byte[SessionPrefixLength];
				Buffer.BlockCopy(sessionPrefix, 0, prefix, 0, SessionPrefixLength);
				this.serverToClient = serverToClient;
			}

			/// <summary>
			/// Atomically increments the internal counter and builds a 12-byte GCM nonce.
			/// Use for the send direction where the caller controls sequencing.
			/// </summary>
			/// <returns>Tuple of (12-byte nonce, sequence number used).</returns>
			/// <exception cref="CryptographicException">Thrown when the counter exceeds <see cref="MaxGcmNonceCounter"/>.</exception>
			public (byte[] Nonce, uint Sequence) NextNonce()
			{
				if (disposed) throw new ObjectDisposedException(nameof(GcmNonceContext));
				long seq = Interlocked.Increment(ref counter);
				if (seq > uint.MaxValue)
					throw new CryptographicException("GCM nonce counter exhausted; session must be rekeyed.");
				return (BuildGcmNonce(prefix!, (ulong)seq, serverToClient), (uint)seq);
			}

			/// <summary>
			/// Builds a nonce for a specific externally-validated sequence number.
			/// Use for the receive direction where the sequence comes from the peer's message.
			/// </summary>
			/// <param name="sequence">The validated sequence number.</param>
			/// <returns>A 12-byte GCM nonce.</returns>
			public byte[] BuildNonceForSequence(uint sequence)
			{
				if (disposed) throw new ObjectDisposedException(nameof(GcmNonceContext));
				return BuildGcmNonce(prefix!, sequence, serverToClient);
			}

			/// <summary>
			/// Atomically increments the internal counter and returns the sequence number
			/// without building a nonce. Use when the caller needs the sequence for AAD
			/// construction and will build the nonce separately via <see cref="BuildNonceForSequence"/>.
			/// Avoids the heap allocation of a 12-byte nonce array that would be immediately discarded.
			/// </summary>
			/// <returns>The next sequence number.</returns>
			/// <exception cref="CryptographicException">Thrown when the counter exceeds <see cref="uint.MaxValue"/>.</exception>
			public uint NextSequenceOnly()
			{
				if (disposed) throw new ObjectDisposedException(nameof(GcmNonceContext));
				long seq = Interlocked.Increment(ref counter);
				if (seq > uint.MaxValue)
					throw new CryptographicException("GCM nonce counter exhausted; session must be rekeyed.");
				return (uint)seq;
			}

			/// <summary>
			/// Maximum CAS retry iterations before giving up. Bounds the spin loop to
			/// prevent pathological spinning under extreme contention (e.g., many threads
			/// racing on the same context). 16 iterations is generous — under normal
			/// conditions the CAS succeeds on the first attempt.
			/// </summary>
			private const int MaxCasRetries = 16;

			/// <summary>
			/// Atomically validates and consumes an expected receive sequence number.
			/// Returns <c>true</c> and advances the counter if <paramref name="seq"/> is exactly
			/// one greater than the current counter. Returns <c>false</c> for duplicates or gaps.
			/// </summary>
			/// <remarks>
			/// <para><b>Threading model:</b> This method is designed for a <b>single-producer</b>
			/// receive path — typically one thread processing inbound messages per connection.
			/// The CAS loop exists as a safety net, not as a concurrency strategy. If multiple
			/// threads call this concurrently on the same context, the strict in-order requirement
			/// (seq == current + 1) means at most one can succeed per round — the others will
			/// return <c>false</c> or retry. Under genuine multi-producer contention, legitimate
			/// messages may be spuriously rejected after <see cref="MaxCasRetries"/> iterations.</para>
			/// <para>If concurrent receive processing is needed, serialise calls to this method
			/// behind a lock or channel, or use a single-threaded receive loop.</para>
			/// </remarks>
			/// <param name="seq">The expected incoming sequence number.</param>
			/// <returns><c>true</c> if consumed; <c>false</c> otherwise.</returns>
			public bool TryConsumeSequence(uint seq)
			{
				if (disposed) throw new ObjectDisposedException(nameof(GcmNonceContext));
				// Single-attempt CAS. The documented threading model
				// is single-producer-per-direction. Looping on CAS contention masked
				// real protocol-misuse bugs by sometimes accepting messages from a
				// racing reader. We now surface the misuse as a hard "return false".
				long current = Interlocked.Read(ref counter);
				// Guard: if the counter has reached uint.MaxValue, no further sequences
				// can be consumed. Without this, (current + 1) as long would be
				// 4294967296 and the uint cast would wrap to 0, accepting a replayed
				// seq-0 on an exhausted counter.
				if (current >= uint.MaxValue) return false;
				if (seq != (uint)(current + 1)) return false;
				return Interlocked.CompareExchange(ref counter, seq, current) == current;
			}

			/// <summary>
			/// Atomically validates and consumes a contiguous range
			/// of receive sequence numbers <c>[baseSeq, baseSeq + count - 1]</c> in a
			/// single CAS. Used by multi-field protocol decoders (SRP verify,
			/// account-creation payload) that must not leave the counter advanced
			/// partway through a logical message if one field fails to decrypt.
			/// </summary>
			/// <param name="baseSeq">First sequence number in the range.</param>
			/// <param name="count">Number of consecutive sequences to consume; must be &gt; 0.</param>
			/// <returns><c>true</c> on success; <c>false</c> on duplicate, gap, exhaustion or invalid count.</returns>
			public bool TryConsumeSequenceRange(uint baseSeq, uint count)
			{
				if (disposed) throw new ObjectDisposedException(nameof(GcmNonceContext));
				if (count == 0) return false;
				long current = Interlocked.Read(ref counter);
				if (current >= uint.MaxValue) return false;
				if (baseSeq != (uint)(current + 1)) return false;
				long last = current + count;
				if (last > uint.MaxValue) return false;
				return Interlocked.CompareExchange(ref counter, last, current) == current;
			}

			/// <summary>
			/// Whether the counter has exceeded 90% of the practical maximum (<see cref="uint.MaxValue"/>),
			/// indicating an imminent rekey. The practical limit is <c>uint.MaxValue</c> because
			/// <see cref="NextNonce"/> caps the counter at that value.
			/// </summary>
			public bool ShouldRekey => (ulong)Interlocked.Read(ref counter) > uint.MaxValue * 9UL / 10;

			/// <summary>
			/// Creates an independent clone for async worker processing.
			/// The clone has its own prefix copy and counter snapshot —
			/// mutations on the clone do not affect the original and vice versa.
			/// Both instances must be independently disposed.
			/// </summary>
			public GcmNonceContext Clone()
			{
				if (disposed) throw new ObjectDisposedException(nameof(GcmNonceContext));
				var clone = new GcmNonceContext(prefix!, serverToClient);
				// Snapshot the current counter so the clone continues from this point.
				// Interlocked.Read ensures we see the latest value from all threads.
				Interlocked.Exchange(ref clone.counter, Interlocked.Read(ref counter));
				return clone;
			}

			/// <summary>
			/// Zeros the session prefix, preventing further nonce generation.
			/// </summary>
			public void Dispose()
			{
				if (!disposed)
				{
					if (prefix != null)
					{
						CryptographicOperations.ZeroMemory(prefix);
						prefix = null;
					}
					disposed = true;
				}
			}
		}

		/// <summary>
		/// Maximum allowed AES ciphertext size in bytes to prevent oversized allocations.
		/// This limit covers the complete GCM output (ciphertext + 16-byte authentication tag).
		/// </summary>
		public const int MaxAesCiphertextSize = 64 * 1024; // 64 KiB

		/// <summary>
		/// Length in bytes of AES-GCM authentication tag.
		/// </summary>
		public const int AesGcmTagLengthBytes = 16;

		/// <summary>
		/// AES-GCM encrypt with Additional Authenticated Data (AAD).
		/// </summary>
		/// <param name="symmetricKey">AES-256 encryption key.</param>
		/// <param name="iv">12-byte GCM nonce (see <see cref="BuildGcmNonce"/>).</param>
		/// <param name="input">Plaintext to encrypt. Callers should zero this array after the call
		/// if it contains sensitive data (e.g., SRP proofs, token payloads).</param>
		/// <param name="aad">Additional authenticated data bound into the GCM tag.</param>
		/// <returns>Ciphertext including the GCM authentication tag. The return value is
		/// not secret and does not require zeroization by callers.</returns>
		/// <remarks>
		/// The intermediate output buffer is heap-allocated and zeroed in a <c>finally</c> block.
		/// ArrayPool would reduce GC pressure, but System.Buffers has an ambient assembly
		/// conflict in this project (duplicate with netstandard2.1). Revisit if that is resolved.
		/// </remarks>
		public static byte[] EncryptAES(byte[] symmetricKey, byte[] iv, byte[] input, byte[] aad)
		{
			if (symmetricKey == null) throw new ArgumentNullException(nameof(symmetricKey));
			if (iv == null) throw new ArgumentNullException(nameof(iv));
			if (input == null) throw new ArgumentNullException(nameof(input));
			if (aad == null) throw new ArgumentNullException(nameof(aad));
			if (iv.Length != GcmNonceLength)
				throw new ArgumentException($"IV must be exactly {GcmNonceLength} bytes for AES-GCM.", nameof(iv));
			// Binds protocol metadata as AAD to authenticate message type/version/seq.
			var cipher = new Org.BouncyCastle.Crypto.Modes.GcmBlockCipher(new AesEngine());
			int tagLenBits = AesGcmTagLengthBytes * 8;
			var parameters = new AeadParameters(new KeyParameter(symmetricKey), tagLenBits, iv, aad);
			cipher.Init(true, parameters);

			int expectedOutputSize = cipher.GetOutputSize(input.Length);
			if (expectedOutputSize > MaxAesCiphertextSize)
				throw new CryptographicException($"Requested AES output too large: {expectedOutputSize} bytes (max {MaxAesCiphertextSize}).");

			byte[] output = new byte[expectedOutputSize];
			int len = 0;
			try
			{
				len = cipher.ProcessBytes(input, 0, input.Length, output, 0);
				len += cipher.DoFinal(output, len);
				var result = new byte[len];
				Buffer.BlockCopy(output, 0, result, 0, len);
				return result;
			}
			catch (InvalidCipherTextException ex)
			{
				throw new CryptographicException("AES-GCM encryption failed.", ex);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(output);
			}
		}

		/// <summary>
		/// AES-GCM decrypt with Additional Authenticated Data (AAD).
		/// </summary>
		/// <returns>Decrypted plaintext. Callers <b>must</b> zeroize the returned array via
		/// <see cref="CryptographicOperations.ZeroMemory"/> when no longer needed, as it
		/// contains the original secret data.</returns>
		/// <remarks>
		/// Callers MUST treat a thrown <see cref="CryptographicException"/> as fatal to the
		/// connection — do not continue the session after a GCM authentication tag mismatch.
		/// </remarks>
		public static byte[] DecryptAES(byte[] symmetricKey, byte[] iv, byte[] input, byte[] aad)
		{
			if (symmetricKey == null) throw new ArgumentNullException(nameof(symmetricKey));
			if (iv == null) throw new ArgumentNullException(nameof(iv));
			if (input == null) throw new ArgumentNullException(nameof(input));
			if (aad == null) throw new ArgumentNullException(nameof(aad));
			if (iv.Length != GcmNonceLength)
				throw new ArgumentException($"IV must be exactly {GcmNonceLength} bytes for AES-GCM.", nameof(iv));
			var cipher = new Org.BouncyCastle.Crypto.Modes.GcmBlockCipher(new AesEngine());
			int tagLenBits = AesGcmTagLengthBytes * 8;
			var parameters = new AeadParameters(new KeyParameter(symmetricKey), tagLenBits, iv, aad);
			cipher.Init(false, parameters);

			if (input.Length > MaxAesCiphertextSize)
				throw new CryptographicException($"AES ciphertext too large: {input.Length} bytes (max {MaxAesCiphertextSize}).");
			if (input.Length < AesGcmTagLengthBytes)
				throw new CryptographicException($"AES ciphertext too small: {input.Length} bytes (min {AesGcmTagLengthBytes} for GCM tag).");

			int outputSize = cipher.GetOutputSize(input.Length);
			byte[] output = new byte[outputSize];
			int len = 0;
			try
			{
				len = cipher.ProcessBytes(input, 0, input.Length, output, 0);
				len += cipher.DoFinal(output, len);
				var result = new byte[len];
				Buffer.BlockCopy(output, 0, result, 0, len);
				return result;
			}
			catch (InvalidCipherTextException ex)
			{
				throw new CryptographicException("AES-GCM authentication failed.", ex);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(output);
			}
		}

		/// <summary>
		/// Compares two byte spans in constant time to prevent timing side-channel attacks.
		/// Delegates to <see cref="CryptographicOperations.FixedTimeEquals"/> which is
		/// guaranteed not to short-circuit on the first differing byte.
		/// </summary>
		/// <param name="left">First byte array.</param>
		/// <param name="right">Second byte array.</param>
		/// <returns><c>true</c> if both arrays are non-null, equal length, and contain the same bytes; otherwise <c>false</c>.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool FixedTimeEquals(byte[] left, byte[] right)
		{
			if (left == null || right == null)
				return false;
			if (left.Length != right.Length)
				return false;
			return CryptographicOperations.FixedTimeEquals(left, right);
		}

		/// <summary>
		/// HMAC-SHA256 key length in bytes.
		/// </summary>
		public const int HmacKeyLength = 32;

		/// <summary>
		/// HMAC-SHA512 optimal key length in bytes.
		/// Used for keying HMAC-SHA512 operations (e.g., fake SRP salt derivation).
		/// </summary>
		public const int HmacSha512KeyLength = 64;

		/// <summary>
		/// HMAC-SHA256 output tag length in bytes.
		/// Semantically distinct from <see cref="HmacKeyLength"/> even though both are 32 for SHA-256.
		/// </summary>
		public const int HmacTagLength = 32;

		/// <summary>
		/// Computes an HMAC-SHA256 over the given data using the specified key.
		/// </summary>
		/// <param name="key">HMAC key (must be exactly <see cref="HmacKeyLength"/> bytes).</param>
		/// <param name="data">Data to authenticate.</param>
		/// <returns>32-byte HMAC-SHA256 tag.</returns>
		public static byte[] SignHmacSha256(byte[] key, byte[] data)
		{
			if (key == null) throw new ArgumentNullException(nameof(key));
			if (key.Length != HmacKeyLength) throw new ArgumentException($"HMAC key must be exactly {HmacKeyLength} bytes.", nameof(key));
			if (data == null) throw new ArgumentNullException(nameof(data));
			using (var hmac = new HMACSHA256(key))
			{
				return hmac.ComputeHash(data);
			}
		}

		/// <summary>
		/// Verifies an HMAC-SHA256 signature in constant time.
		/// </summary>
		/// <param name="key">HMAC key (must be 32 bytes).</param>
		/// <param name="data">Data that was authenticated.</param>
		/// <param name="signature">Expected 32-byte HMAC-SHA256 tag.</param>
		/// <returns><c>true</c> if the signature is valid; otherwise <c>false</c>.</returns>
		public static bool VerifyHmacSha256(byte[] key, byte[] data, byte[] signature)
		{
			if (key == null) throw new ArgumentNullException(nameof(key));
			if (data == null) throw new ArgumentNullException(nameof(data));
			if (signature == null || signature.Length != HmacTagLength) return false;
			byte[] computed = SignHmacSha256(key, data);
			bool result = FixedTimeEquals(computed, signature);
			CryptographicOperations.ZeroMemory(computed);
			return result;
		}

		/// <summary>
		/// Computes the SHA-256 hash of a byte array and returns it as a lowercase hex string.
		/// Used to derive a token fingerprint for database revocation checks.
		/// </summary>
		/// <remarks>
		/// <para><b>GC note:</b> The returned string persists on the managed heap until collected.
		/// This is acceptable because the hash is not secret — it is a one-way fingerprint
		/// stored in the database for revocation lookups. The raw token (input) should be
		/// zeroed by callers after this call.</para>
		/// </remarks>
		/// <param name="token">The signed token to hash.</param>
		/// <returns>Lowercase hex SHA-256 hash string.</returns>
		public static string HashTokenHex(byte[] token)
		{
			if (token == null) throw new ArgumentNullException(nameof(token));
			using (var sha = SHA256.Create())
			{
				byte[] hash = sha.ComputeHash(token);
				var sb = new StringBuilder(hash.Length * 2);
				for (int i = 0; i < hash.Length; i++)
					sb.Append(hash[i].ToString("x2"));
				CryptographicOperations.ZeroMemory(hash);
				return sb.ToString();
			}
		}

		/// <summary>
		/// Token format version embedded in auth tokens.
		/// Decoupled from <see cref="ProtocolVersion"/> so that protocol negotiation
		/// changes do not silently invalidate outstanding tokens.
		/// Increment only when the token wire format changes.
		/// </summary>
		public const byte TokenFormatVersion = 3;

		/// <summary>
		/// Token type discriminator for authentication tokens.
		/// Prevents cross-purpose token reuse across different subsystems.
		/// </summary>
		public const byte TokenTypeAuth = 0x01;

		/// <summary>
		/// Builds a signed authentication token for use in World/Scene server token-based authentication.
		/// Layout: [1-byte version][1-byte tokenType][2-byte accountName length BE][accountName UTF-8][1-byte accessLevel][8-byte loginServerId BE][8-byte signingKeyId BE][8-byte expiresUtcTicks BE][16-byte nonce][32-byte HMAC-SHA256].
		/// The HMAC covers the entire payload (everything except the trailing 32-byte HMAC).
		/// Token format version and token type are included in the HMAC, preventing token
		/// reuse across format versions or different token subsystems.
		/// </summary>
		/// <remarks>
		/// <para><b>Replay mitigation:</b> Tokens are bearer tokens with a random nonce but no
		/// server-side single-use enforcement. Replay is bounded by the expiration window.
		/// Nonce uniqueness is not tracked because the same token may be legitimately presented
		/// to multiple World/Scene servers within its lifetime (e.g., server transfers).</para>
		/// <para>For additional protection the issuing LoginServer stores
		/// <see cref="HashTokenHex"/> on issuance, and World/Scene servers check revocation
		/// via the database before accepting a token. Explicit logout or key rotation
		/// revokes all outstanding tokens for an account.</para>
		/// </remarks>
		/// <param name="accountName">Account name to embed in the token.</param>
		/// <param name="loginServerId">Database ID of the issuing LoginServer.</param>
		/// <param name="signingKeyId">Database ID of the HMAC signing key used to sign this token.</param>
		/// <param name="expiresUtc">UTC expiration time for the token.</param>
		/// <param name="accessLevel">Account access level to embed in the token.</param>
		/// <param name="hmacKey">32-byte HMAC signing key.</param>
		/// <returns>Signed token as a byte array (payload + HMAC).</returns>
		public static byte[] BuildAuthToken(string accountName, long loginServerId, long signingKeyId, DateTime expiresUtc, byte[] hmacKey, AccessLevel accessLevel)
		{
			if (string.IsNullOrEmpty(accountName)) throw new ArgumentException("accountName is required.", nameof(accountName));
			if (loginServerId < 0) throw new ArgumentOutOfRangeException(nameof(loginServerId), "loginServerId must be non-negative.");
			if (signingKeyId <= 0) throw new ArgumentOutOfRangeException(nameof(signingKeyId), "signingKeyId must be positive.");
			if (expiresUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("expiresUtc must be UTC.", nameof(expiresUtc));
			if (hmacKey == null || hmacKey.Length != HmacKeyLength) throw new ArgumentException($"hmacKey must be {HmacKeyLength} bytes.", nameof(hmacKey));

			DateTime utcNow = DateTime.UtcNow;
			TimeSpan lifetime = expiresUtc - utcNow;
			if (lifetime <= TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(expiresUtc), "Token has already expired.");
			if (lifetime > TimeSpan.FromMinutes(MaxTokenLifetimeMinutes))
				throw new ArgumentOutOfRangeException(nameof(expiresUtc), $"Token lifetime ({lifetime.TotalMinutes:F0} min) exceeds maximum ({MaxTokenLifetimeMinutes} min).");

			byte[] nameBytes = Encoding.UTF8.GetBytes(accountName);
			if (nameBytes.Length > ushort.MaxValue)
			{
				CryptographicOperations.ZeroMemory(nameBytes);
				throw new ArgumentException("Account name too long.", nameof(accountName));
			}

			// Layout: [1B version][1B tokenType][2B nameLen][name][1B accessLevel][8B serverId][8B signingKeyId][8B ticks][16B nonce]
			int payloadLength = 1 + 1 + 2 + nameBytes.Length + 1 + 8 + 8 + 8 + 16;
			byte[] payload = new byte[payloadLength];
			int offset = 0;

			// Token format version — decoupled from protocol negotiation version
			payload[offset++] = TokenFormatVersion;

			// Token type discriminator — prevents cross-purpose token reuse
			payload[offset++] = TokenTypeAuth;

			// Account name length (2 bytes big-endian)
			payload[offset++] = (byte)(nameBytes.Length >> 8);
			payload[offset++] = (byte)nameBytes.Length;

			// Account name
			Buffer.BlockCopy(nameBytes, 0, payload, offset, nameBytes.Length);
			offset += nameBytes.Length;

			// Access level (1 byte)
			payload[offset++] = (byte)accessLevel;

			// LoginServerId (8 bytes big-endian)
			long sid = loginServerId;
			for (int i = 7; i >= 0; i--)
			{
				payload[offset + i] = (byte)(sid & 0xFF);
				sid >>= 8;
			}
			offset += 8;

			// Signing key ID (8 bytes big-endian)
			long kid = signingKeyId;
			for (int i = 7; i >= 0; i--)
			{
				payload[offset + i] = (byte)(kid & 0xFF);
				kid >>= 8;
			}
			offset += 8;

			// ExpiresUtc ticks (8 bytes big-endian)
			long ticks = expiresUtc.Ticks;
			for (int i = 7; i >= 0; i--)
			{
				payload[offset + i] = (byte)(ticks & 0xFF);
				ticks >>= 8;
			}
			offset += 8;

			// Random nonce (16 bytes)
			byte[] nonce = GenerateKey(16);
			Buffer.BlockCopy(nonce, 0, payload, offset, 16);
			CryptographicOperations.ZeroMemory(nonce);
			offset += 16;

			// HMAC-SHA256 over payload
			byte[] signature = SignHmacSha256(hmacKey, payload);

			// Combine payload + signature
			byte[] token = new byte[payloadLength + HmacTagLength];
			Buffer.BlockCopy(payload, 0, token, 0, payloadLength);
			Buffer.BlockCopy(signature, 0, token, payloadLength, HmacTagLength);

			CryptographicOperations.ZeroMemory(nameBytes);
			CryptographicOperations.ZeroMemory(payload);
			CryptographicOperations.ZeroMemory(signature);

			return token;
		}

		/// <summary>
		/// Strict UTF-8 decoder that rejects malformed byte sequences instead of silently
		/// replacing them with U+FFFD. Used for security-sensitive token parsing and SRP decryption.
		/// </summary>
		public static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

		/// <summary>
		/// Minimum valid signed token length: 1 (version) + 1 (tokenType) + 2 (nameLen) + 1 (name) + 1 (accessLevel) + 8 (serverId) + 8 (signingKeyId) + 8 (ticks) + 16 (nonce) + 32 (HMAC).
		/// </summary>
		public const int MinSignedTokenLength = 1 + 1 + 2 + 1 + 1 + 8 + 8 + 8 + 16 + HmacTagLength;

		/// <summary>
		/// Parses and verifies an authentication token's HMAC signature.
		/// Does NOT check expiration or revocation — callers must validate those separately.
		/// </summary>
		/// <param name="signedToken">Signed token bytes (payload + HMAC).</param>
		/// <param name="hmacKey">32-byte HMAC verification key.</param>
		/// <param name="accountName">Parsed account name (null on failure).</param>
		/// <param name="loginServerId">Parsed LoginServer database ID (0 on failure).</param>
		/// <param name="signingKeyId">Parsed signing-key database ID (0 on failure).</param>
		/// <param name="accessLevel">Parsed access level (AccessLevel.Player on failure).</param>
		/// <param name="expiresUtc">Parsed UTC expiration (DateTime.MinValue on failure).</param>
		/// <returns><c>true</c> if the HMAC is valid and the token is well-formed; otherwise <c>false</c>.</returns>
		public static bool TryParseAndVerifyAuthToken(byte[] signedToken, byte[] hmacKey, out string? accountName, out long loginServerId, out long signingKeyId, out AccessLevel accessLevel, out DateTime expiresUtc)
		{
			accountName = null;
			loginServerId = 0;
			signingKeyId = 0;
			accessLevel = AccessLevel.Player;
			expiresUtc = DateTime.MinValue;

			if (signedToken == null || signedToken.Length < MinSignedTokenLength)
				return false;
			if (hmacKey == null || hmacKey.Length != HmacKeyLength)
				return false;

			int payloadLength = signedToken.Length - HmacTagLength;

			// Extract the fixed-size HMAC signature (32 bytes).
			byte[] signature = new byte[HmacTagLength];
			Buffer.BlockCopy(signedToken, payloadLength, signature, 0, HmacTagLength);

			// Verify HMAC directly from signedToken without allocating a payload copy.
			// ComputeHash(byte[], int, int) avoids the allocation entirely.
			bool hmacValid;
			using (var hmac = new System.Security.Cryptography.HMACSHA256(hmacKey))
			{
				byte[] computed = hmac.ComputeHash(signedToken, 0, payloadLength);
				hmacValid = FixedTimeEquals(computed, signature);
				CryptographicOperations.ZeroMemory(computed);
			}
			CryptographicOperations.ZeroMemory(signature);
			// NOTE: signature is zeroed above before the HMAC validity check below.
			// This ensures the copy is destroyed even on the early return path when
			// !hmacValid. The signedToken buffer itself is a caller-owned reference
			// and is not zeroed here.

			if (!hmacValid)
				return false;

			// Parse fields directly from signedToken to avoid a redundant payload copy.
			int offset = 0;

			// Token format version (1 byte) — reject tokens from incompatible format versions
			byte version = signedToken[offset++];
			if (version != TokenFormatVersion)
			{
				return false;
			}

			// Token type discriminator (1 byte) — reject non-auth tokens
			byte tokenType = signedToken[offset++];
			if (tokenType != TokenTypeAuth)
			{
				return false;
			}

			// Account name length (2 bytes big-endian)
			int nameLength = (signedToken[offset] << 8) | signedToken[offset + 1];
			offset += 2;
			if (nameLength <= 0 || offset + nameLength + 1 + 8 + 8 + 8 + 16 > payloadLength)
			{
				return false;
			}

			// Strict UTF-8: reject malformed byte sequences in account names.
			try
			{
				accountName = StrictUtf8.GetString(signedToken, offset, nameLength);
			}
			catch (DecoderFallbackException)
			{
				return false;
			}
			offset += nameLength;

			// Access level (1 byte)
			accessLevel = (AccessLevel)signedToken[offset++];

			// LoginServerId (8 bytes big-endian)
			loginServerId = 0;
			for (int i = 0; i < 8; i++)
				loginServerId = (loginServerId << 8) | signedToken[offset++];

			// Signing key ID (8 bytes big-endian)
			signingKeyId = 0;
			for (int i = 0; i < 8; i++)
				signingKeyId = (signingKeyId << 8) | signedToken[offset++];

			if (signingKeyId <= 0)
				return false;

			// ExpiresUtc ticks (8 bytes big-endian)
			long ticks = 0;
			for (int i = 0; i < 8; i++)
				ticks = (ticks << 8) | signedToken[offset++];

			if (ticks < 0 || ticks > DateTime.MaxValue.Ticks)
				return false;

			expiresUtc = new DateTime(ticks, DateTimeKind.Utc);
			return true;
		}

		/// <summary>
		/// TOTP two-factor authentication helpers.
		/// Uses OtpNet for TOTP generation/verification, AES-256-GCM for encrypting
		/// TOTP secrets at rest, and PBKDF2-SHA256 for hashing recovery codes.
		/// </summary>
		public static class TwoFactor
		{
			/// <summary>
			/// TOTP secret length in bytes (160 bits per RFC 4226 §4).
			/// </summary>
			public const int TotpSecretLength = 20;

			/// <summary>
			/// Default number of recovery codes generated per account.
			/// </summary>
			public const int DefaultRecoveryCodeCount = 8;

			/// <summary>
			/// PBKDF2 iteration count for recovery code hashing. Raised from
			/// 100_000 to 600_000 to align with OWASP 2023 guidance for PBKDF2-HMAC-SHA256. The new
			/// hash is written with a versioned envelope ("v2:&lt;iters&gt;:salt:hash") so the cost
			/// can be tuned without breaking existing stored hashes; <see cref="VerifyRecoveryCode"/>
			/// transparently accepts the legacy "salt:hash" format at the original 100_000 iterations.
			/// </summary>
			private const int Pbkdf2Iterations = 600_000;

			/// <summary>Legacy PBKDF2 iteration count for verifying hashes written before H2.</summary>
			private const int Pbkdf2IterationsLegacy = 100_000;

			/// <summary>Versioned envelope prefix written by <see cref="HashRecoveryCode"/>.</summary>
			private const string RecoveryCodeEnvelopeV2Prefix = "v2:";

			/// <summary>
			/// PBKDF2 salt length in bytes.
			/// </summary>
			private const int Pbkdf2SaltLength = 16;

			/// <summary>
			/// PBKDF2 derived hash length in bytes.
			/// </summary>
			private const int Pbkdf2HashLength = 32;

			/// <summary>
			/// TOTP step size in seconds (standard 30s per RFC 6238).
			/// </summary>
			private const int TotpStepSeconds = 30;

			/// <summary>
			/// TOTP code digit count.
			/// </summary>
			private const int TotpDigits = 6;

			/// <summary>
			/// AAD prefix for v2 TOTP at-rest envelopes. The full AAD is built per-call as
			/// <c>"fishmmo-totp-secret-v2|" + lower(username) + "|kv=" + kekVersion</c> so that
			/// (a) an envelope encrypted for one user cannot be decrypted for another, and
			/// (b) a downgrade across KEK versions is rejected by GCM tag verification.
			/// </summary>
			private const string TotpSecretAadPrefixV2 = "fishmmo-totp-secret-v2|";

			/// <summary>
			/// On-disk version byte for the TOTP at-rest envelope. Bumping this is a breaking
			/// change for existing rows in the <c>account.totp_secret</c> column.
			/// </summary>
			private const byte TotpEnvelopeVersion = 2;

			/// <summary>
			/// Length in bytes of the random salt used to derive the per-user TOTP KEK
			/// via HKDF-SHA256. 16 bytes is well above the 128-bit collision-resistance threshold.
			/// </summary>
			private const int TotpKekSaltLength = 16;

			/// <summary>
			/// Generates a cryptographically random TOTP secret.
			/// </summary>
			/// <returns>20-byte secret suitable for TOTP.</returns>
			public static byte[] GenerateTotpSecret()
			{
				return GenerateKey(TotpSecretLength);
			}

			/// <summary>
			/// Encrypts a TOTP secret for at-rest storage using AES-256-GCM under a per-user
			/// data-encryption key derived from the server master KEK via HKDF-SHA256.
			/// </summary>
			/// <remarks>
			/// Envelope layout (binary, then base64-encoded):
			/// <code>
			/// [1B  version  = TotpEnvelopeVersion]
			/// [4B  kekVersion (big-endian uint32)]
			/// [16B salt    (random, HKDF salt)]
			/// [12B nonce   (random, GCM IV)]
			/// [N B ciphertext + 16B GCM tag]
			/// </code>
			/// Per-user data key = <c>HKDF-SHA256(IKM=masterKek, salt=salt, info="totp|"+lower(username)+"|kv="+kekVersion, L=32)</c>.
			/// AAD = UTF-8 of <c>"fishmmo-totp-secret-v2|"+lower(username)+"|kv="+kekVersion</c>.
			/// Both the salt and the username binding make every envelope unique per account
			/// and prevent ciphertext transplant across users or KEK versions.
			/// </remarks>
			/// <param name="masterKek">32-byte persistent server master KEK (NOT the data key).</param>
			/// <param name="username">Account name the secret belongs to. Normalised to lowercase invariant for binding.</param>
			/// <param name="plaintextSecret">Plaintext TOTP secret (typically 20 bytes).</param>
			/// <param name="kekVersion">Master-KEK version identifier (>= 1). Allows offline re-wrap on key rotation.</param>
			/// <returns>Base64-encoded envelope suitable for direct storage.</returns>
			public static string EncryptTotpSecret(byte[] masterKek, string username, byte[] plaintextSecret, int kekVersion = 1)
			{
				if (masterKek == null || masterKek.Length != 32)
					throw new ArgumentException("Master KEK must be exactly 32 bytes.", nameof(masterKek));
				if (string.IsNullOrEmpty(username))
					throw new ArgumentException("Username is required.", nameof(username));
				if (plaintextSecret == null || plaintextSecret.Length == 0)
					throw new ArgumentNullException(nameof(plaintextSecret));
				if (kekVersion <= 0)
					throw new ArgumentOutOfRangeException(nameof(kekVersion));

				byte[] salt = GenerateKey(TotpKekSaltLength);
				byte[] nonce = GenerateKey(GcmNonceLength);
				byte[]? kek = null;
				byte[]? ciphertext = null;
				byte[]? envelope = null;
				byte[]? aad = null;
				try
				{
					kek = DeriveTotpKek(masterKek, username, kekVersion, salt);
					aad = BuildTotpAad(username, kekVersion);
					ciphertext = EncryptAES(kek, nonce, plaintextSecret, aad);

					int headerLen = 1 + 4 + TotpKekSaltLength + GcmNonceLength;
					envelope = new byte[headerLen + ciphertext.Length];
					envelope[0] = TotpEnvelopeVersion;
					envelope[1] = (byte)((kekVersion >> 24) & 0xFF);
					envelope[2] = (byte)((kekVersion >> 16) & 0xFF);
					envelope[3] = (byte)((kekVersion >> 8) & 0xFF);
					envelope[4] = (byte)(kekVersion & 0xFF);
					int off = 5;
					Buffer.BlockCopy(salt, 0, envelope, off, TotpKekSaltLength); off += TotpKekSaltLength;
					Buffer.BlockCopy(nonce, 0, envelope, off, GcmNonceLength); off += GcmNonceLength;
					Buffer.BlockCopy(ciphertext, 0, envelope, off, ciphertext.Length);

					return Convert.ToBase64String(envelope);
				}
				finally
				{
					CryptographicOperations.ZeroMemory(salt);
					CryptographicOperations.ZeroMemory(nonce);
					if (kek != null) CryptographicOperations.ZeroMemory(kek);
					if (ciphertext != null) CryptographicOperations.ZeroMemory(ciphertext);
					if (envelope != null) CryptographicOperations.ZeroMemory(envelope);
					if (aad != null) CryptographicOperations.ZeroMemory(aad);
				}
			}

			/// <summary>
			/// Decrypts a v2 TOTP at-rest envelope produced by <see cref="EncryptTotpSecret"/>.
			/// Older single-key envelopes are NOT supported; rotation must be performed by
			/// out-of-band re-wrap tooling that decrypts under the old scheme and re-encrypts
			/// via this method.
			/// </summary>
			/// <param name="masterKek">32-byte persistent server master KEK.</param>
			/// <param name="username">Account name the envelope belongs to. Must match the value
			/// supplied at encrypt-time (case-insensitive); a mismatch causes the GCM tag to fail.</param>
			/// <param name="storedValue">Base64-encoded envelope from <see cref="EncryptTotpSecret"/>.</param>
			/// <returns>Plaintext TOTP secret. Caller must zeroize when done.</returns>
			public static byte[] DecryptTotpSecret(byte[] masterKek, string username, string storedValue)
			{
				if (masterKek == null || masterKek.Length != 32)
					throw new ArgumentException("Master KEK must be exactly 32 bytes.", nameof(masterKek));
				if (string.IsNullOrEmpty(username))
					throw new ArgumentException("Username is required.", nameof(username));
				if (string.IsNullOrEmpty(storedValue))
					throw new ArgumentNullException(nameof(storedValue));

				byte[] envelope = Convert.FromBase64String(storedValue);
				const int headerLen = 1 + 4 + TotpKekSaltLength + GcmNonceLength;
				if (envelope.Length <= headerLen)
					throw new CryptographicException("Stored TOTP envelope is too short.");
				if (envelope[0] != TotpEnvelopeVersion)
					throw new CryptographicException("Unsupported TOTP envelope version.");

				int kekVersion = (envelope[1] << 24) | (envelope[2] << 16) | (envelope[3] << 8) | envelope[4];
				if (kekVersion <= 0)
					throw new CryptographicException("Invalid TOTP KEK version.");

				byte[] salt = new byte[TotpKekSaltLength];
				Buffer.BlockCopy(envelope, 5, salt, 0, TotpKekSaltLength);
				byte[] nonce = new byte[GcmNonceLength];
				Buffer.BlockCopy(envelope, 5 + TotpKekSaltLength, nonce, 0, GcmNonceLength);
				byte[] ciphertext = new byte[envelope.Length - headerLen];
				Buffer.BlockCopy(envelope, headerLen, ciphertext, 0, ciphertext.Length);

				byte[]? kek = null;
				byte[]? aad = null;
				try
				{
					kek = DeriveTotpKek(masterKek, username, kekVersion, salt);
					aad = BuildTotpAad(username, kekVersion);
					return DecryptAES(kek, nonce, ciphertext, aad);
				}
				finally
				{
					CryptographicOperations.ZeroMemory(salt);
					CryptographicOperations.ZeroMemory(nonce);
					CryptographicOperations.ZeroMemory(ciphertext);
					CryptographicOperations.ZeroMemory(envelope);
					if (kek != null) CryptographicOperations.ZeroMemory(kek);
					if (aad != null) CryptographicOperations.ZeroMemory(aad);
				}
			}

			/// <summary>
			/// Derives the per-user, per-version 32-byte data-encryption key for a TOTP envelope
			/// via HKDF-SHA256. The KEK output is suitable for direct use as an AES-256-GCM key.
			/// </summary>
			private static byte[] DeriveTotpKek(byte[] masterKek, string username, int kekVersion, byte[] salt)
			{
				string info = "totp|" + username.ToLowerInvariant() + "|kv=" + kekVersion.ToString(CultureInfo.InvariantCulture);
				byte[] infoBytes = Encoding.UTF8.GetBytes(info);
				try
				{
					return HkdfSha256(salt, masterKek, infoBytes, 32);
				}
				finally
				{
					CryptographicOperations.ZeroMemory(infoBytes);
				}
			}

			/// <summary>
			/// Builds the AES-GCM associated-data (AAD) for a TOTP envelope. The AAD binds the
			/// ciphertext to a specific account and KEK version, defeating cross-row transplant.
			/// </summary>
			private static byte[] BuildTotpAad(string username, int kekVersion)
			{
				string aad = TotpSecretAadPrefixV2 + username.ToLowerInvariant() + "|kv=" + kekVersion.ToString(CultureInfo.InvariantCulture);
				return Encoding.UTF8.GetBytes(aad);
			}

			/// <summary>
			/// Builds an otpauth:// URI for use with authenticator apps (Google Authenticator, Authy, etc.).
			/// </summary>
			/// <param name="secret">Plaintext TOTP secret.</param>
			/// <param name="accountName">Account name to display in the authenticator app.</param>
			/// <param name="issuer">Issuer name (application name).</param>
			/// <returns>otpauth://totp/ URI string.</returns>
			public static string BuildOtpauthUri(byte[] secret, string accountName, string issuer = "FishMMO")
			{
				if (secret == null || secret.Length == 0)
					throw new ArgumentNullException(nameof(secret));
				if (string.IsNullOrEmpty(accountName))
					throw new ArgumentException("Account name is required.", nameof(accountName));

				string base32Secret = Base32Encoding.ToString(secret);
				return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountName)}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={TotpDigits}&period={TotpStepSeconds}";
			}

			/// <summary>
			/// Generates a set of single-use recovery codes in XXXX-XXXX-XXXX-XXXX format.
			/// Each code carries 64 bits of entropy (8 random bytes, 16 hex chars).
			/// </summary>
			/// <param name="count">Number of codes to generate.</param>
			/// <returns>Array of plaintext recovery codes.</returns>
			public static string[] GenerateRecoveryCodes(int count = DefaultRecoveryCodeCount)
			{
				if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

				string[] codes = new string[count];
				for (int i = 0; i < count; i++)
				{
					byte[] bytes = GenerateKey(8);
					string hex = BitConverter.ToString(bytes).Replace("-", "").ToUpperInvariant();
					codes[i] = hex.Substring(0, 4) + "-" + hex.Substring(4, 4) + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4);
					CryptographicOperations.ZeroMemory(bytes);
				}
				return codes;
			}

			/// <summary>
			/// Hashes a recovery code with PBKDF2-SHA256 for secure at-rest storage.
			/// </summary>
			/// <param name="accountId">Account identifier (e.g. username). Bound
			/// into the PBKDF2 input so a stolen recovery-code hash cannot be reused across accounts
			/// even if the cleartext code happens to collide.</param>
			/// <param name="plaintextCode">Plaintext recovery code.</param>
			/// <returns>String in format "v2:&lt;iterations&gt;:base64(salt):base64(hash)" for database storage.</returns>
			public static string HashRecoveryCode(string accountId, string plaintextCode)
			{
				if (string.IsNullOrEmpty(accountId))
					throw new ArgumentNullException(nameof(accountId));
				if (string.IsNullOrEmpty(plaintextCode))
					throw new ArgumentNullException(nameof(plaintextCode));

				string normalizedCode = plaintextCode.Trim().ToUpperInvariant();
				string normalizedAccount = accountId.Trim().ToLowerInvariant();

				byte[] salt = GenerateKey(Pbkdf2SaltLength);
				byte[] codeBytes = Encoding.UTF8.GetBytes(normalizedAccount + "|" + normalizedCode);

				using (var pbkdf2 = new Rfc2898DeriveBytes(codeBytes, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256))
				{
					byte[] hash = pbkdf2.GetBytes(Pbkdf2HashLength);
					string result = RecoveryCodeEnvelopeV2Prefix + Pbkdf2Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture)
						+ ":" + Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
					CryptographicOperations.ZeroMemory(salt);
					CryptographicOperations.ZeroMemory(hash);
					CryptographicOperations.ZeroMemory(codeBytes);
					return result;
				}
			}

			/// <summary>
			/// Verifies a submitted recovery code against a stored PBKDF2-SHA256 hash. Accepts both
			/// the v2 envelope ("v2:&lt;iters&gt;:salt:hash" — account-bound) and the legacy
			/// ("salt:hash" — not account-bound, 100_000 iterations) format.
			/// </summary>
			/// <remarks>
			/// <para><b>WARNING: Legacy format cross-account reuse.</b> The legacy "salt:hash" format
			/// hashes only the plaintext recovery code with a per-code salt, without binding to the
			/// account identifier. This means the same recovery code (or two codes that happen to
			/// produce the same PBKDF2 output with different salts) could, in a pathological
			/// collision scenario, be verified against an unintended account's stored hash.
			/// The v2 envelope mitigates this by incorporating the account ID into the PBKDF2 input
			/// (via <c>accountId + "|" + normalizedCode</c>), making each hash account-specific.</para>
			/// <para>All newly-generated recovery codes use the v2 envelope. The legacy parser is
			/// retained only for backward-compatible verification of hashes written before the v2
			/// migration. Consider rotating any remaining legacy hashes to v2 during a maintenance
			/// window to eliminate this class of risk entirely.</para>
			/// </remarks>
			/// <param name="accountId">Account identifier. Used only when verifying v2 hashes.</param>
			/// <param name="submitted">Plaintext recovery code submitted by the user.</param>
			/// <param name="storedHash">Stored hash in v2 or legacy envelope format.</param>
			/// <returns><c>true</c> if the code matches; <c>false</c> otherwise.</returns>
			public static bool VerifyRecoveryCode(string accountId, string submitted, string storedHash)
			{
				if (string.IsNullOrEmpty(submitted) || string.IsNullOrEmpty(storedHash))
					return false;

				bool isV2 = storedHash.StartsWith(RecoveryCodeEnvelopeV2Prefix, StringComparison.Ordinal);
				string[] parts;
				int iterations;
				string saltBase64;
				string hashBase64;

				if (isV2)
				{
					parts = storedHash.Substring(RecoveryCodeEnvelopeV2Prefix.Length).Split(':');
					if (parts.Length != 3) return false;
					if (!int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out iterations) || iterations < Pbkdf2IterationsLegacy)
						return false;
					saltBase64 = parts[1];
					hashBase64 = parts[2];
				}
				else
				{
					parts = storedHash.Split(':');
					if (parts.Length != 2) return false;
					iterations = Pbkdf2IterationsLegacy;
					saltBase64 = parts[0];
					hashBase64 = parts[1];
				}

				byte[] salt;
				byte[] expectedHash;
				try
				{
					salt = Convert.FromBase64String(saltBase64);
					expectedHash = Convert.FromBase64String(hashBase64);
				}
				catch (FormatException)
				{
					return false;
				}

				if (salt.Length != Pbkdf2SaltLength || expectedHash.Length != Pbkdf2HashLength)
				{
					CryptographicOperations.ZeroMemory(salt);
					CryptographicOperations.ZeroMemory(expectedHash);
					return false;
				}

				string normalizedCode = submitted.Trim().ToUpperInvariant();
				byte[] codeBytes;
				if (isV2)
				{
					if (string.IsNullOrEmpty(accountId))
					{
						CryptographicOperations.ZeroMemory(salt);
						CryptographicOperations.ZeroMemory(expectedHash);
						return false;
					}
					string normalizedAccount = accountId.Trim().ToLowerInvariant();
					codeBytes = Encoding.UTF8.GetBytes(normalizedAccount + "|" + normalizedCode);
				}
				else
				{
					codeBytes = Encoding.UTF8.GetBytes(normalizedCode);
				}

				using (var pbkdf2 = new Rfc2898DeriveBytes(codeBytes, salt, iterations, HashAlgorithmName.SHA256))
				{
					byte[] computedHash = pbkdf2.GetBytes(Pbkdf2HashLength);
					bool result = FixedTimeEquals(computedHash, expectedHash);
					CryptographicOperations.ZeroMemory(salt);
					CryptographicOperations.ZeroMemory(expectedHash);
					CryptographicOperations.ZeroMemory(computedHash);
					CryptographicOperations.ZeroMemory(codeBytes);
					return result;
				}
			}

			/// <summary>
			/// Verifies a TOTP code with a ±1 step verification window and anti-replay protection.
			/// </summary>
			/// <param name="plaintextSecret">Decrypted TOTP secret.</param>
			/// <param name="submittedCode">6-digit TOTP code from the user.</param>
			/// <param name="lastWindow">Last successfully used TOTP time window (for anti-replay). Pass 0 for first use.</param>
			/// <returns>Tuple: (valid, windowUsed). If valid, windowUsed should be stored for anti-replay.</returns>
			/// <remarks>
			/// <para><b>Heap retention:</b> The OtpNet <c>Totp</c> constructor copies
			/// <paramref name="plaintextSecret"/> into an internal managed array that cannot
			/// be zeroed without reflection. The <c>Totp</c> object becomes GC-eligible once
			/// this method returns, and the caller is expected to zero <paramref name="plaintextSecret"/>
			/// in its <c>finally</c> block (after any DB persistence calls complete). Pinning and
			/// manual zeroing of the OtpNet internal field is not worthwhile given the narrow time
			/// window and the already-encrypted-at-rest storage.</para>
			/// <para><b>TODO:</b> If a future OtpNet release exposes <c>IDisposable</c> or a zeroing
			/// API on <c>Totp</c>, adopt it here to eliminate the residual heap copy.</para>
			/// <para><b>TryZeroizeTotpInternals is best-effort:</b>
			/// <see cref="TryZeroizeTotpInternals"/> uses reflection to find and zero
			/// any <c>byte[]</c> fields inside the OtpNet <c>Totp</c> object graph, including the
			/// embedded <c>InMemoryKey</c>. This is best-effort: it depends on the internal
			/// field layout of OtpNet, which may change between versions without notice.
			/// After this method returns, the original TOTP secret remains in the managed heap
			/// (inside OtpNet-internal arrays) until the next garbage collection. Callers must
			/// zero <paramref name="plaintextSecret"/> independently in a <c>finally</c> block.
			/// Do not rely solely on <see cref="TryZeroizeTotpInternals"/> for security guarantees.</para>
			/// </remarks>
			public static (bool Valid, long WindowUsed) VerifyTotpCode(byte[] plaintextSecret, string submittedCode, long lastWindow)
			{
				if (plaintextSecret == null || plaintextSecret.Length == 0)
					return (false, 0);
				if (string.IsNullOrEmpty(submittedCode) || submittedCode.Length != TotpDigits)
					return (false, 0);

				var totp = new Totp(plaintextSecret, step: TotpStepSeconds, mode: OtpHashMode.Sha1, totpSize: TotpDigits);

				long timeWindowUsed;
				bool valid;
				try
				{
					// previous: 1 allows one expired window for clock drift / slow typing.
					// future: 0 rejects codes from upcoming windows to prevent pre-computed
					// code submission (the server clock is authoritative).
					valid = totp.VerifyTotp(submittedCode, out timeWindowUsed, new VerificationWindow(previous: 1, future: 0));
				}
				finally
				{
					// OtpNet's Totp copies the secret into a private
					// InMemoryKey/byte[] field that has no public zeroize API. Reach into the
					// instance via reflection and overwrite any byte[] field we find so the
					// secret is not left dangling on the managed heap until the next GC sweep.
					TryZeroizeTotpInternals(totp);
				}

				if (!valid)
					return (false, 0);

				// Anti-replay: reject if this window was already used
				if (timeWindowUsed <= lastWindow)
					return (false, 0);

				return (true, timeWindowUsed);
			}

			/// <summary>
			/// Best-effort reflection-based zeroize of any <c>byte[]</c> fields reachable from an
			/// OtpNet <c>Totp</c> instance (and the embedded <c>Key</c>/<c>InMemoryKey</c> object).
			/// Failures are swallowed — we cannot guarantee the field layout across OtpNet versions
			/// and this is defence-in-depth on top of caller-side zeroize of <c>plaintextSecret</c>.
			/// </summary>
			private static void TryZeroizeTotpInternals(object totp)
			{
				if (totp == null) return;
				try
				{
					const System.Reflection.BindingFlags flags =
						System.Reflection.BindingFlags.Instance |
						System.Reflection.BindingFlags.Public |
						System.Reflection.BindingFlags.NonPublic;

					var visited = new System.Collections.Generic.HashSet<object>(ReferenceEqualityComparer.Instance);
					var queue = new System.Collections.Generic.Queue<object>();
					queue.Enqueue(totp);

					while (queue.Count > 0)
					{
						object current = queue.Dequeue();
						if (current == null || !visited.Add(current)) continue;

						Type t = current.GetType();
						// Walk the inheritance chain so private fields on base classes are also reached.
						while (t != null && t != typeof(object))
						{
							foreach (var f in t.GetFields(flags))
							{
								object value;
								try { value = f.GetValue(current); } catch { continue; }
								if (value == null) continue;

								if (value is byte[] buf && buf.Length > 0)
								{
									CryptographicOperations.ZeroMemory(buf);
								}
								else if (!f.FieldType.IsValueType && f.FieldType != typeof(string))
								{
									queue.Enqueue(value);
								}
							}
							t = t.BaseType;
						}
					}
				}
				catch (Exception ex)
				{
					// Defensive: never let a reflection failure escape into the caller.
					// Log at debug level since OtpNet internals may change across versions.
					System.Diagnostics.Debug.WriteLine($"[CryptoHelper.TryZeroizeTotpInternals] Reflection-based zeroize failed: {ex.Message}");
				}
			}

			private sealed class ReferenceEqualityComparer : System.Collections.Generic.IEqualityComparer<object>
			{
				public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
				public new bool Equals(object x, object y) => ReferenceEquals(x, y);
				public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
			}
		}
	}
}