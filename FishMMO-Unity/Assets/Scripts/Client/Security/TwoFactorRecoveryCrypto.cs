using System;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace FishMMO.Client.Security
{
	/// <summary>
	/// The outcome of an attempt to open a recovery-code envelope.
	/// </summary>
	/// <remarks>
	/// These are deliberately distinct values rather than a bool. "There is nothing stored",
	/// "the password is wrong", "somebody edited the file" and "this is an old plaintext file"
	/// all demand different handling, and collapsing them into a failure flag is how a caller
	/// ends up deleting a perfectly good envelope because the player fat-fingered a password.
	/// </remarks>
	public enum TwoFactorRecoveryReadResult
	{
		/// <summary>The envelope opened and the plaintext is available.</summary>
		Success = 0,

		/// <summary>No data was supplied (null/empty blob).</summary>
		Empty,

		/// <summary>
		/// The blob is not an envelope at all — it is the old, unencrypted payload this class
		/// replaces. The caller can read it directly and should migrate it.
		/// </summary>
		LegacyPlaintext,

		/// <summary>
		/// The blob claims to be an envelope but its header is unusable: truncated, a future
		/// format version, an unknown KDF, or lengths that do not add up.
		/// </summary>
		Malformed,

		/// <summary>
		/// The header parsed, but the AEAD tag did not verify. Either the password is wrong or
		/// the file was tampered with; AES-GCM cannot tell those two apart and neither can we.
		/// **The file must not be deleted on this result.**
		/// </summary>
		WrongPasswordOrTampered,
	}

	/// <summary>
	/// Password-based authenticated encryption for the local 2FA recovery-code payload.
	///
	/// <para><b>Why the account password and not a machine-bound key.</b> The obvious alternative
	/// is a key sealed to the machine (DPAPI, keychain, a file next to the payload). That protects
	/// the codes from another local process, but it dies with the installation — and a reinstall,
	/// a new machine or a wiped profile is precisely the situation in which a player reaches for
	/// recovery codes. A machine-bound key would therefore trade a confidentiality problem for an
	/// availability one, and locking a player out of their own account is the worse failure of the
	/// two. The account password is in hand at exactly the two moments that matter — when the codes
	/// are written at 2FA setup, and when the player legitimately asks to read them back — and it
	/// is the one secret that survives a reinstall.</para>
	///
	/// <para><b>Primitives.</b> Argon2id and AES-256-GCM, both from the BouncyCastle build this
	/// project already ships (<c>Assets/Dependencies/BouncyCastle.Cryptography.dll</c>) and already
	/// depends on for all of its other crypto. No new dependency. BouncyCastle rather than
	/// <c>System.Security.Cryptography.AesGcm</c> for the same reason
	/// <c>CryptoHelper.EncryptAES</c> uses it: it behaves identically on every Unity player target,
	/// where the BCL AEAD types are not uniformly available.</para>
	///
	/// <para><b>Why Argon2id rather than the PBKDF2 the server uses for recovery-code hashing.</b>
	/// <c>CryptoHelper.TwoFactor.HashRecoveryCode</c> is PBKDF2-HMAC-SHA256 at 600k iterations
	/// because it runs server-side, once per verification, against a high-entropy random code. This
	/// runs client-side, twice per account lifetime, against a human-chosen password, and the
	/// attacker is someone who already has the file. Memory-hardness is the property that matters
	/// there, and the parameters are recorded in the envelope so they can be raised later without
	/// stranding files written by an older client.</para>
	/// </summary>
	public static class TwoFactorRecoveryCrypto
	{
		/// <summary>
		/// File magic. Eight bytes so a truncated or unrelated file is rejected on the first read
		/// rather than being mistaken for a short envelope.
		/// </summary>
		private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FM2FAREC");

		/// <summary>Envelope format version. Bumped only for a breaking layout change.</summary>
		private const byte FormatVersion = 1;

		/// <summary>KDF identifier for Argon2id.</summary>
		private const byte KdfArgon2id = 1;

		/// <summary>Argon2id passes over the memory block.</summary>
		private const int Argon2Iterations = 3;

		/// <summary>
		/// Argon2id memory cost in KiB. 64 MiB costs roughly a tenth of a second on a desktop CPU
		/// and is the whole point of the exercise — it is what makes a GPU or ASIC attack on the
		/// player's password expensive rather than trivial.
		/// </summary>
		private const int Argon2MemoryKiB = 64 * 1024;

		/// <summary>
		/// Argon2id lanes. One, deliberately: this runs on the Unity main thread during account
		/// creation, and extra lanes buy an attacker as much as they buy us while costing the
		/// player a longer stall.
		/// </summary>
		private const int Argon2Parallelism = 1;

		/// <summary>Length of the per-file KDF salt.</summary>
		private const int SaltLength = 16;

		/// <summary>AES-GCM nonce length, matching <c>CryptoHelper.GcmNonceLength</c>.</summary>
		private const int NonceLength = 12;

		/// <summary>AES-GCM tag length in bytes, matching <c>CryptoHelper.AesGcmTagLengthBytes</c>.</summary>
		private const int TagLength = 16;

		/// <summary>Derived key length — AES-256.</summary>
		private const int KeyLength = 32;

		/// <summary>
		/// Header size: magic + version + kdf id + iterations + memory + parallelism + salt + nonce
		/// + ciphertext length.
		/// </summary>
		private const int HeaderLength = 8 + 1 + 1 + 4 + 4 + 1 + SaltLength + NonceLength + 4;

		/// <summary>
		/// Refuses an absurd payload before allocating for it. The real payload is an otpauth URI
		/// plus eight short codes — a couple of hundred bytes.
		/// </summary>
		private const int MaxPayloadBytes = 64 * 1024;

		/// <summary>
		/// True if <paramref name="blob"/> begins with the envelope magic.
		/// </summary>
		/// <param name="blob">Candidate file contents.</param>
		/// <returns><c>true</c> if this looks like an envelope; <c>false</c> for anything else,
		/// including the legacy plaintext payload.</returns>
		public static bool LooksLikeEnvelope(byte[] blob)
		{
			if (blob == null || blob.Length < Magic.Length)
			{
				return false;
			}
			for (int i = 0; i < Magic.Length; ++i)
			{
				if (blob[i] != Magic[i])
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Encrypts <paramref name="plaintext"/> under a key derived from <paramref name="password"/>.
		/// </summary>
		/// <param name="password">The account password. Never stored, never logged.</param>
		/// <param name="plaintext">The recovery payload to protect.</param>
		/// <returns>A self-describing envelope: salt, nonce, KDF parameters, ciphertext and tag.</returns>
		/// <exception cref="ArgumentException">The password or plaintext is empty.</exception>
		/// <remarks>
		/// The whole header — including the KDF parameters — is fed to GCM as additional
		/// authenticated data. Without that, an attacker holding the file could rewrite the
		/// iteration and memory costs down to 1 and hand the weakened file back to the client,
		/// which would then derive the key cheaply on their behalf. Binding the parameters means
		/// any such edit fails the tag check instead.
		/// </remarks>
		public static byte[] Encrypt(string password, string plaintext)
		{
			if (string.IsNullOrEmpty(password))
			{
				throw new ArgumentException("A password is required to encrypt the recovery payload.", nameof(password));
			}
			if (string.IsNullOrEmpty(plaintext))
			{
				throw new ArgumentException("There is no recovery payload to encrypt.", nameof(plaintext));
			}

			byte[] salt = new byte[SaltLength];
			byte[] nonce = new byte[NonceLength];
			using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(salt);
				rng.GetBytes(nonce);
			}

			byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
			byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
			byte[] key = null;
			try
			{
				if (plaintextBytes.Length > MaxPayloadBytes)
				{
					throw new ArgumentException("The recovery payload is implausibly large.", nameof(plaintext));
				}

				key = DeriveKey(passwordBytes, salt, Argon2Iterations, Argon2MemoryKiB, Argon2Parallelism);

				// The ciphertext length is not known until the tag is appended, so the header is
				// built first with a placeholder and patched once the length is known. The AAD is
				// taken from the finished header, so the length is authenticated too.
				byte[] header = new byte[HeaderLength];
				int ciphertextLength = plaintextBytes.Length + TagLength;
				WriteHeader(header, salt, nonce, Argon2Iterations, Argon2MemoryKiB, Argon2Parallelism, ciphertextLength);

				byte[] ciphertext = GcmProcess(true, key, nonce, header, plaintextBytes, 0, plaintextBytes.Length);
				if (ciphertext.Length != ciphertextLength)
				{
					// Cannot happen with GCM, but the header would be a lie if it did.
					throw new CryptographicException("Unexpected AES-GCM output length.");
				}

				byte[] envelope = new byte[HeaderLength + ciphertext.Length];
				Buffer.BlockCopy(header, 0, envelope, 0, HeaderLength);
				Buffer.BlockCopy(ciphertext, 0, envelope, HeaderLength, ciphertext.Length);
				return envelope;
			}
			finally
			{
				Zero(key);
				Zero(plaintextBytes);
				Zero(passwordBytes);
			}
		}

		/// <summary>
		/// Attempts to open an envelope produced by <see cref="Encrypt"/>.
		/// </summary>
		/// <param name="password">The account password.</param>
		/// <param name="blob">The stored file contents.</param>
		/// <param name="plaintext">Receives the recovered payload on success; <c>null</c> otherwise.</param>
		/// <returns>Why the read succeeded or failed. See <see cref="TwoFactorRecoveryReadResult"/>.</returns>
		/// <remarks>
		/// This never throws on bad input and never deletes anything. A failure here is a fact
		/// about this attempt, not a verdict on the file.
		/// </remarks>
		public static TwoFactorRecoveryReadResult TryDecrypt(string password, byte[] blob, out string plaintext)
		{
			plaintext = null;

			if (blob == null || blob.Length == 0)
			{
				return TwoFactorRecoveryReadResult.Empty;
			}
			if (!LooksLikeEnvelope(blob))
			{
				return TwoFactorRecoveryReadResult.LegacyPlaintext;
			}
			if (blob.Length < HeaderLength + TagLength)
			{
				return TwoFactorRecoveryReadResult.Malformed;
			}
			if (string.IsNullOrEmpty(password))
			{
				// Not Malformed — the file may be perfectly good. The caller simply did not
				// supply the one thing that could open it.
				return TwoFactorRecoveryReadResult.WrongPasswordOrTampered;
			}

			int offset = Magic.Length;
			byte version = blob[offset++];
			byte kdfId = blob[offset++];
			if (version != FormatVersion || kdfId != KdfArgon2id)
			{
				return TwoFactorRecoveryReadResult.Malformed;
			}

			int iterations = ReadInt32(blob, ref offset);
			int memoryKiB = ReadInt32(blob, ref offset);
			int parallelism = blob[offset++];

			/* Sanity-bound the cost parameters before honouring them. They are authenticated, so a
			 * genuine envelope cannot carry hostile values — but the check happens *before* the
			 * tag is verified (the key has to be derived to verify it), so a forged header could
			 * otherwise ask this client to allocate an arbitrary amount of memory and spin for an
			 * arbitrary time. That is a denial of service on a file anyone can drop in the folder. */
			if (iterations < 1 || iterations > 16 ||
				memoryKiB < 8 * 1024 || memoryKiB > 512 * 1024 ||
				parallelism < 1 || parallelism > 8)
			{
				return TwoFactorRecoveryReadResult.Malformed;
			}

			byte[] salt = new byte[SaltLength];
			Buffer.BlockCopy(blob, offset, salt, 0, SaltLength);
			offset += SaltLength;

			byte[] nonce = new byte[NonceLength];
			Buffer.BlockCopy(blob, offset, nonce, 0, NonceLength);
			offset += NonceLength;

			int ciphertextLength = ReadInt32(blob, ref offset);
			if (ciphertextLength < TagLength ||
				ciphertextLength > MaxPayloadBytes + TagLength ||
				offset + ciphertextLength > blob.Length)
			{
				return TwoFactorRecoveryReadResult.Malformed;
			}

			byte[] header = new byte[HeaderLength];
			Buffer.BlockCopy(blob, 0, header, 0, HeaderLength);

			byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
			byte[] key = null;
			byte[] recovered = null;
			try
			{
				key = DeriveKey(passwordBytes, salt, iterations, memoryKiB, parallelism);
				recovered = GcmProcess(false, key, nonce, header, blob, offset, ciphertextLength);
				plaintext = Encoding.UTF8.GetString(recovered);
				return TwoFactorRecoveryReadResult.Success;
			}
			catch (InvalidCipherTextException)
			{
				return TwoFactorRecoveryReadResult.WrongPasswordOrTampered;
			}
			catch (CryptographicException)
			{
				return TwoFactorRecoveryReadResult.WrongPasswordOrTampered;
			}
			catch (ArgumentException)
			{
				return TwoFactorRecoveryReadResult.Malformed;
			}
			finally
			{
				Zero(key);
				Zero(recovered);
				Zero(passwordBytes);
			}
		}

		/// <summary>
		/// Runs Argon2id over the password.
		/// </summary>
		private static byte[] DeriveKey(byte[] passwordBytes, byte[] salt, int iterations, int memoryKiB, int parallelism)
		{
			Argon2Parameters parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
				.WithVersion(Argon2Parameters.Version13)
				.WithIterations(iterations)
				.WithMemoryAsKB(memoryKiB)
				.WithParallelism(parallelism)
				.WithSalt(salt)
				.Build();

			Argon2BytesGenerator generator = new Argon2BytesGenerator();
			generator.Init(parameters);

			byte[] key = new byte[KeyLength];
			generator.GenerateBytes(passwordBytes, key);
			return key;
		}

		/// <summary>
		/// One AES-256-GCM pass in either direction.
		/// </summary>
		private static byte[] GcmProcess(bool forEncryption, byte[] key, byte[] nonce, byte[] aad, byte[] input, int inputOffset, int inputLength)
		{
			GcmBlockCipher cipher = new GcmBlockCipher(new AesEngine());
			cipher.Init(forEncryption, new AeadParameters(new KeyParameter(key), TagLength * 8, nonce, aad));

			byte[] output = new byte[cipher.GetOutputSize(inputLength)];
			try
			{
				int written = cipher.ProcessBytes(input, inputOffset, inputLength, output, 0);
				written += cipher.DoFinal(output, written);

				byte[] result = new byte[written];
				Buffer.BlockCopy(output, 0, result, 0, written);
				return result;
			}
			finally
			{
				// The intermediate buffer holds plaintext on the decrypt path.
				Zero(output);
			}
		}

		/// <summary>
		/// Lays down the fixed-size header. Big-endian throughout so the format does not depend on
		/// the endianness of whichever machine wrote the file.
		/// </summary>
		private static void WriteHeader(byte[] header, byte[] salt, byte[] nonce, int iterations, int memoryKiB, int parallelism, int ciphertextLength)
		{
			int offset = 0;
			Buffer.BlockCopy(Magic, 0, header, offset, Magic.Length);
			offset += Magic.Length;
			header[offset++] = FormatVersion;
			header[offset++] = KdfArgon2id;
			WriteInt32(header, ref offset, iterations);
			WriteInt32(header, ref offset, memoryKiB);
			header[offset++] = (byte)parallelism;
			Buffer.BlockCopy(salt, 0, header, offset, salt.Length);
			offset += salt.Length;
			Buffer.BlockCopy(nonce, 0, header, offset, nonce.Length);
			offset += nonce.Length;
			WriteInt32(header, ref offset, ciphertextLength);
		}

		/// <summary>Writes a big-endian 32-bit value and advances the offset.</summary>
		private static void WriteInt32(byte[] buffer, ref int offset, int value)
		{
			buffer[offset++] = (byte)((value >> 24) & 0xFF);
			buffer[offset++] = (byte)((value >> 16) & 0xFF);
			buffer[offset++] = (byte)((value >> 8) & 0xFF);
			buffer[offset++] = (byte)(value & 0xFF);
		}

		/// <summary>Reads a big-endian 32-bit value and advances the offset.</summary>
		private static int ReadInt32(byte[] buffer, ref int offset)
		{
			int value = (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];
			offset += 4;
			return value;
		}

		/// <summary>
		/// Overwrites a buffer that held key material or plaintext.
		/// </summary>
		/// <remarks>
		/// Not a defence against a debugger — it is a defence against the buffer sitting in a
		/// reusable heap block, or in a core dump, long after the value stopped being needed.
		/// The .NET strings on either end of this class cannot be scrubbed at all, which is why
		/// every byte[] on the path is.
		/// </remarks>
		internal static void Zero(byte[] buffer)
		{
			if (buffer == null)
			{
				return;
			}
			Array.Clear(buffer, 0, buffer.Length);
		}
	}
}
