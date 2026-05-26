using System;
using System.Security.Cryptography;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// AEAD envelope (AES-256-GCM) for wrapping symmetric key material at rest.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Sealed blob layout:
	/// <code>
	///   [0..3]    magic         (4 bytes, identifies a wrapped blob)
	///   [4]       version       (1 byte, currently <c>0x01</c>)
	///   [5..16]   nonce         (12 bytes, GCM nonce)
	///   [17..N]   ciphertext    (= plaintext length bytes)
	///   [N..N+15] tag           (16 bytes, GCM auth tag)
	/// </code>
	/// The magic prefix lets readers distinguish a wrapped blob from a legacy raw
	/// 32-byte HMAC key on the same column without an additional schema flag. The
	/// false-positive probability for a uniformly-random 32-byte key beginning with
	/// the 4-byte magic is 2<sup>-32</sup> ≈ negligible; in practice all newly written
	/// keys are produced via <see cref="Wrap"/> so the discriminator is unambiguous.
	/// </para>
	/// <para>
	/// The KEK is supplied per call (e.g. loaded once at startup from a deployment-level
	/// secret distinct from the database). A leaked DB dump without the KEK does not
	/// disclose any signing-key material.
	/// </para>
	/// </remarks>
	public static class KeyEnvelope
	{
		/// <summary>
		/// Magic prefix identifying a sealed blob produced by <see cref="Wrap"/>.
		/// </summary>
		public static readonly byte[] Magic = new byte[] { 0xF1, 0x57, 0xE9, 0x01 };

		/// <summary>
		/// Current sealed-blob version. Bump on incompatible layout changes.
		/// </summary>
		public const byte Version1 = 0x01;

		/// <summary>
		/// AES-GCM nonce length in bytes.
		/// </summary>
		public const int NonceSize = 12;

		/// <summary>
		/// AES-GCM authentication tag length in bytes.
		/// </summary>
		public const int TagSize = 16;

		/// <summary>
		/// Header length (magic + version + nonce) preceding ciphertext.
		/// </summary>
		public const int HeaderSize = 4 + 1 + NonceSize;

		/// <summary>
		/// Minimum length of any sealed blob produced by <see cref="Wrap"/>.
		/// </summary>
		public const int MinWrappedLength = HeaderSize + TagSize;

		/// <summary>
		/// Returns <c>true</c> when <paramref name="blob"/> begins with the wrapped-blob
		/// magic prefix and is structurally large enough to be a valid envelope. Does not
		/// validate the GCM tag — callers must still call <see cref="Unwrap"/> for that.
		/// </summary>
		public static bool LooksWrapped(byte[] blob)
		{
			if (blob == null || blob.Length < MinWrappedLength)
			{
				return false;
			}
			return blob[0] == Magic[0]
				&& blob[1] == Magic[1]
				&& blob[2] == Magic[2]
				&& blob[3] == Magic[3];
		}

		/// <summary>
		/// Encrypts <paramref name="plaintext"/> under <paramref name="kek"/> with AES-256-GCM,
		/// authenticating <paramref name="aad"/>. Returns the sealed blob.
		/// </summary>
		/// <param name="kek">32-byte key-encryption key (AES-256).</param>
		/// <param name="plaintext">Key material to wrap. Caller retains ownership and is
		/// responsible for zeroing it after the call returns.</param>
		/// <param name="aad">Additional authenticated data bound to the ciphertext (e.g. the
		/// owning row's stable identifier). May be empty but should be non-null and stable
		/// across <see cref="Wrap"/>/<see cref="Unwrap"/> cycles.</param>
		/// <returns>The sealed blob.</returns>
		public static byte[] Wrap(byte[] kek, byte[] plaintext, byte[] aad)
		{
			if (kek == null)
			{
				throw new ArgumentNullException(nameof(kek));
			}
			if (kek.Length != 32)
			{
				throw new ArgumentException("KEK must be exactly 32 bytes (AES-256).", nameof(kek));
			}
			if (plaintext == null)
			{
				throw new ArgumentNullException(nameof(plaintext));
			}
			if (aad == null)
			{
				aad = Array.Empty<byte>();
			}

			byte[] nonce = new byte[NonceSize];
			using (var rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(nonce);
			}

			byte[] ciphertext = new byte[plaintext.Length];
			byte[] tag = new byte[TagSize];

			using (var aes = new AesGcm(kek))
			{
				aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
			}

			byte[] blob = new byte[HeaderSize + ciphertext.Length + TagSize];
			Buffer.BlockCopy(Magic, 0, blob, 0, Magic.Length);
			blob[4] = Version1;
			Buffer.BlockCopy(nonce, 0, blob, 5, NonceSize);
			Buffer.BlockCopy(ciphertext, 0, blob, HeaderSize, ciphertext.Length);
			Buffer.BlockCopy(tag, 0, blob, HeaderSize + ciphertext.Length, TagSize);

			CryptographicOperations.ZeroMemory(nonce);
			CryptographicOperations.ZeroMemory(ciphertext);
			CryptographicOperations.ZeroMemory(tag);
			return blob;
		}

		/// <summary>
		/// Decrypts a sealed blob produced by <see cref="Wrap"/> under <paramref name="kek"/>,
		/// validating <paramref name="aad"/>. Returns the plaintext on success or <c>null</c>
		/// on any structural, version, or authentication failure (the caller MUST treat
		/// <c>null</c> as a hard failure and fail closed; do not retry with a different KEK).
		/// </summary>
		public static byte[]? Unwrap(byte[] kek, byte[] wrappedBlob, byte[] aad)
		{
			if (kek == null)
			{
				throw new ArgumentNullException(nameof(kek));
			}
			if (kek.Length != 32)
			{
				throw new ArgumentException("KEK must be exactly 32 bytes (AES-256).", nameof(kek));
			}
			if (!LooksWrapped(wrappedBlob))
			{
				return null;
			}
			if (wrappedBlob[4] != Version1)
			{
				return null;
			}
			if (aad == null)
			{
				aad = Array.Empty<byte>();
			}

			int ctLen = wrappedBlob.Length - HeaderSize - TagSize;
			if (ctLen < 0)
			{
				return null;
			}

			byte[] nonce = new byte[NonceSize];
			byte[] ciphertext = new byte[ctLen];
			byte[] tag = new byte[TagSize];
			byte[] plaintext = new byte[ctLen];
			Buffer.BlockCopy(wrappedBlob, 5, nonce, 0, NonceSize);
			Buffer.BlockCopy(wrappedBlob, HeaderSize, ciphertext, 0, ctLen);
			Buffer.BlockCopy(wrappedBlob, HeaderSize + ctLen, tag, 0, TagSize);

			try
			{
				using var aes = new AesGcm(kek);
				aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
				return plaintext;
			}
			catch (CryptographicException)
			{
				CryptographicOperations.ZeroMemory(plaintext);
				return null;
			}
			finally
			{
				CryptographicOperations.ZeroMemory(nonce);
				CryptographicOperations.ZeroMemory(ciphertext);
				CryptographicOperations.ZeroMemory(tag);
			}
		}
	}
}