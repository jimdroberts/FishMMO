using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace FishMMO.Shared
{
	/// <summary>
	/// Static class providing cryptographic helper methods for RSA (via BouncyCastle), key generation, and AES encryption/decryption.
	/// RSA operations use BouncyCastle for cross-platform OAEP-SHA256 support on all Unity targets.
	/// </summary>
	public static class CryptoHelper
	{
		/// <summary>
		/// Default RSA key size in bits.
		/// </summary>
		private const int RsaKeySize = 2048;

		/// <summary>
		/// Modulus length in bytes for a 2048-bit RSA key.
		/// </summary>
		private const int ModulusLength = RsaKeySize / 8; // 256

		/// <summary>
		/// Generates a new RSA key pair using BouncyCastle's secure random generator.
		/// </summary>
		/// <returns>The generated asymmetric key pair containing both public and private keys.</returns>
		public static AsymmetricCipherKeyPair GenerateRsaKeyPair()
		{
			var generator = new RsaKeyPairGenerator();
			generator.Init(new KeyGenerationParameters(new SecureRandom(), RsaKeySize));
			return generator.GenerateKeyPair();
		}

		/// <summary>
		/// Exports the public key from a BouncyCastle key pair as a structured byte array (modulus + exponent).
		/// Wire format: [256-byte modulus][3-byte exponent] for 2048-bit keys.
		/// </summary>
		/// <param name="keyPair">The RSA key pair to export the public key from.</param>
		/// <returns>Byte array containing the modulus and exponent of the public key.</returns>
		public static byte[] ExportPublicKey(AsymmetricCipherKeyPair keyPair)
		{
			var pubKey = (RsaKeyParameters)keyPair.Public;
			byte[] modulus = pubKey.Modulus.ToByteArrayUnsigned();
			byte[] exponent = pubKey.Exponent.ToByteArrayUnsigned();

			// Pad modulus to fixed length if leading zeros were stripped
			byte[] publicKeyBytes = new byte[ModulusLength + exponent.Length];
			int modulusOffset = ModulusLength - modulus.Length;
			Buffer.BlockCopy(modulus, 0, publicKeyBytes, modulusOffset, modulus.Length);
			Buffer.BlockCopy(exponent, 0, publicKeyBytes, ModulusLength, exponent.Length);

			return publicKeyBytes;
		}

		/// <summary>
		/// Reconstructs a BouncyCastle RSA public key from a structured byte array (modulus + exponent).
		/// </summary>
		/// <param name="publicKeyBytes">Byte array containing the modulus and exponent.</param>
		/// <returns>BouncyCastle RSA public key parameters.</returns>
		public static RsaKeyParameters ImportPublicKey(byte[] publicKeyBytes)
		{
			if (publicKeyBytes == null)
				throw new ArgumentNullException(nameof(publicKeyBytes));

			// Require at least a modulus and a minimum exponent (1 byte).
			if (publicKeyBytes.Length < ModulusLength + 1)
				throw new ArgumentException($"publicKeyBytes is too short: expected at least {ModulusLength + 1} bytes.", nameof(publicKeyBytes));

			byte[] modulus = new byte[ModulusLength];
			int exponentLength = publicKeyBytes.Length - ModulusLength;
			byte[] exponent = new byte[exponentLength];

			Buffer.BlockCopy(publicKeyBytes, 0, modulus, 0, ModulusLength);
			Buffer.BlockCopy(publicKeyBytes, ModulusLength, exponent, 0, exponentLength);

			return new RsaKeyParameters(
				false,
				new BigInteger(1, modulus),
				new BigInteger(1, exponent));
		}

		/// <summary>
		/// Encrypts data using RSA with OAEP-SHA256 padding via BouncyCastle.
		/// </summary>
		/// <param name="publicKey">The recipient's RSA public key.</param>
		/// <param name="data">The plaintext data to encrypt.</param>
		/// <returns>The encrypted ciphertext.</returns>
		public static byte[] EncryptRsaOaepSha256(RsaKeyParameters publicKey, byte[] data)
		{
			if (publicKey == null)
				throw new ArgumentNullException(nameof(publicKey));
			if (data == null)
				throw new ArgumentNullException(nameof(data));

			// OAEP-SHA256 max plaintext size = modulusLength - 2*hashLen - 2
			int hashLen = 32; // SHA-256 output size
			int maxPlain = ModulusLength - 2 * hashLen - 2;
			if (data.Length > maxPlain)
				throw new ArgumentException($"Data too large for RSA OAEP-SHA256 with {RsaKeySize}-bit key. Max {maxPlain} bytes.", nameof(data));

			var engine = new OaepEncoding(new RsaEngine(), new Org.BouncyCastle.Crypto.Digests.Sha256Digest());
			engine.Init(true, publicKey);
			return engine.ProcessBlock(data, 0, data.Length);
		}

		/// <summary>
		/// Decrypts data using RSA with OAEP-SHA256 padding via BouncyCastle.
		/// </summary>
		/// <param name="privateKey">The RSA private key for decryption.</param>
		/// <param name="ciphertext">The encrypted data to decrypt.</param>
		/// <returns>The decrypted plaintext.</returns>
		public static byte[] DecryptRsaOaepSha256(AsymmetricKeyParameter privateKey, byte[] ciphertext)
		{
			if (privateKey == null)
				throw new ArgumentNullException(nameof(privateKey));
			if (ciphertext == null)
				throw new ArgumentNullException(nameof(ciphertext));

			// Ciphertext for RSA should match modulus length
			if (ciphertext.Length != ModulusLength)
				throw new ArgumentException($"Invalid ciphertext length; expected {ModulusLength} bytes.", nameof(ciphertext));

			var engine = new OaepEncoding(new RsaEngine(), new Org.BouncyCastle.Crypto.Digests.Sha256Digest());
			engine.Init(false, privateKey);
			return engine.ProcessBlock(ciphertext, 0, ciphertext.Length);
		}

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
		/// Builds a 12-byte GCM nonce from a session prefix, message counter, and direction flag.
		/// Layout: [4-byte prefix][8-byte direction-encoded counter (big-endian)].
		/// The direction flag occupies the upper 32 bits of the counter field, ensuring
		/// server-to-client and client-to-server nonces never collide for the same counter value.
		/// </summary>
		/// <param name="sessionPrefix">4-byte random prefix unique to the session.</param>
		/// <param name="counter">Monotonically increasing per-message counter.</param>
		/// <param name="serverToClient">
		/// <c>true</c> for server→client messages; <c>false</c> for client→server messages.
		/// </param>
		/// <returns>A 12-byte nonce suitable for AES-GCM.</returns>
		public static byte[] BuildGcmNonce(byte[] sessionPrefix, uint counter, bool serverToClient)
		{
			if (sessionPrefix == null || sessionPrefix.Length != SessionPrefixLength)
				throw new ArgumentException($"Session prefix must be exactly {SessionPrefixLength} bytes.", nameof(sessionPrefix));

			byte[] nonce = new byte[GcmNonceLength];
			Buffer.BlockCopy(sessionPrefix, 0, nonce, 0, SessionPrefixLength);

			// Encode direction in upper 32 bits to prevent nonce collision across directions.
			// Client→server: direction = 0, Server→client: direction = 1.
			// Layout: [prefix(4)] [direction(1)] [0x00(3)] [counter big-endian(4)]
			nonce[SessionPrefixLength] = serverToClient ? (byte)1 : (byte)0;
			// nonce[5..7] remain zero (padding).
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
		/// Required length for a GCM nonce in bytes.
		/// </summary>
		public const int GcmNonceLength = 12;

		/// <summary>
		/// Encrypts input data using AES-GCM symmetric encryption with the provided key and nonce.
		/// The nonce is also passed as Additional Authenticated Data (AAD) to cryptographically
		/// bind the ciphertext to its nonce context, preventing ciphertext transplant attacks.
		/// </summary>
		/// <param name="symmetricKey">AES symmetric key.</param>
		/// <param name="iv">12-byte nonce for GCM (also used as AAD).</param>
		/// <param name="input">Input data to encrypt.</param>
		/// <returns>Encrypted data as a byte array (ciphertext + 128-bit tag).</returns>
		public static byte[] EncryptAES(byte[] symmetricKey, byte[] iv, byte[] input)
		{
			if (symmetricKey == null) throw new ArgumentNullException(nameof(symmetricKey));
			if (iv == null) throw new ArgumentNullException(nameof(iv));
			if (input == null) throw new ArgumentNullException(nameof(input));
			if (iv.Length != GcmNonceLength) throw new ArgumentException("IV must be 12 bytes for GCM.", nameof(iv));

			// Use BouncyCastle AES-GCM (tag appended to ciphertext)
			var cipher = new Org.BouncyCastle.Crypto.Modes.GcmBlockCipher(new AesEngine());
			int tagLenBits = 128;
			// Pass iv as AAD to bind ciphertext to its nonce context.
			var parameters = new AeadParameters(new KeyParameter(symmetricKey), tagLenBits, iv, iv);
			cipher.Init(true, parameters);

			byte[] output = new byte[cipher.GetOutputSize(input.Length)];
			int len = cipher.ProcessBytes(input, 0, input.Length, output, 0);
			try
			{
				len += cipher.DoFinal(output, len);
			}
			catch (InvalidCipherTextException ex)
			{
				// Should not happen during encryption, but handle defensively
				throw new CryptographicException("AES-GCM encryption failed.", ex);
			}

			if (len == output.Length)
				return output;
			// Trim if necessary
			var result = new byte[len];
			Buffer.BlockCopy(output, 0, result, 0, len);
			// zero temporary output buffer
			CryptographicOperations.ZeroMemory(output);
			return result;
		}

		/// <summary>
		/// Decrypts input data using AES-GCM symmetric decryption with the provided key and nonce.
		/// The nonce must match the one used for encryption (also verified as AAD).
		/// </summary>
		/// <param name="symmetricKey">AES symmetric key.</param>
		/// <param name="iv">12-byte nonce for GCM (also used as AAD).</param>
		/// <param name="input">Input data to decrypt (ciphertext + 128-bit tag).</param>
		/// <returns>Decrypted data as a byte array.</returns>
		public static byte[] DecryptAES(byte[] symmetricKey, byte[] iv, byte[] input)
		{
			if (symmetricKey == null) throw new ArgumentNullException(nameof(symmetricKey));
			if (iv == null) throw new ArgumentNullException(nameof(iv));
			if (input == null) throw new ArgumentNullException(nameof(input));
			if (iv.Length != GcmNonceLength) throw new ArgumentException("IV must be 12 bytes for GCM.", nameof(iv));

			var cipher = new Org.BouncyCastle.Crypto.Modes.GcmBlockCipher(new AesEngine());
			int tagLenBits = 128;
			// Pass iv as AAD — must match the AAD used during encryption.
			var parameters = new AeadParameters(new KeyParameter(symmetricKey), tagLenBits, iv, iv);
			cipher.Init(false, parameters);

			byte[] output = new byte[cipher.GetOutputSize(input.Length)];
			int len = 0;
			try
			{
				len = cipher.ProcessBytes(input, 0, input.Length, output, 0);
				len += cipher.DoFinal(output, len);
			}
			catch (InvalidCipherTextException ex)
			{
				// Authentication failed — caller must treat this as fatal (disconnect)
				throw new CryptographicException("AES-GCM authentication failed.", ex);
			}

			var result = new byte[len];
			Buffer.BlockCopy(output, 0, result, 0, len);
			// zero temporary output buffer
			CryptographicOperations.ZeroMemory(output);
			return result;
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
	}
}