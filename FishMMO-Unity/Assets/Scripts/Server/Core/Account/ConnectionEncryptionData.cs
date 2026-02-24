using System.Security.Cryptography;
using FishMMO.Shared;

namespace FishMMO.Server.Core.Account
{
	/// <summary>
	/// Holds per-connection encryption state including symmetric key, session prefix, and
	/// monotonic counters for GCM nonce derivation. Each direction (server→client,
	/// client→server) uses a separate counter to guarantee nonce uniqueness.
	/// <para>Thread-safety: auth is serialized per connection (in-flight gates), so counter
	/// increments do not require atomic operations.</para>
	/// </summary>
	public class ConnectionEncryptionData
	{
		/// <summary>
		/// The client's RSA public key received during the handshake.
		/// </summary>
		public byte[] PublicKey;

		/// <summary>
		/// The AES-256 symmetric key shared with the client.
		/// </summary>
		public byte[] SymmetricKey;

		/// <summary>
		/// 4-byte random prefix unique to this session, combined with a counter to form
		/// a 12-byte GCM nonce via <see cref="CryptoHelper.BuildGcmNonce"/>.
		/// </summary>
		public byte[] SessionPrefix;

		/// <summary>
		/// Monotonic counter for server→client (encrypt) nonces.
		/// </summary>
		public uint SendCounter;

		/// <summary>
		/// Monotonic counter for client→server (decrypt) nonces.
		/// </summary>
		public uint ReceiveCounter;

		/// <summary>
		/// Initializes a new instance with the given key material and zero counters.
		/// </summary>
		/// <param name="publicKey">The client's RSA public key.</param>
		/// <param name="symmetricKey">The AES-256 symmetric key.</param>
		/// <param name="sessionPrefix">A 4-byte random session prefix.</param>
		public ConnectionEncryptionData(byte[] publicKey, byte[] symmetricKey, byte[] sessionPrefix)
		{
			PublicKey = publicKey;
			SymmetricKey = symmetricKey;
			SessionPrefix = sessionPrefix;
			SendCounter = 0;
			ReceiveCounter = 0;
		}

		/// <summary>
		/// Builds the next server→client GCM nonce and advances <see cref="SendCounter"/>.
		/// </summary>
		/// <returns>A 12-byte nonce for AES-GCM encryption.</returns>
		/// <exception cref="System.Security.Cryptography.CryptographicException">Thrown when the counter would overflow, which would cause nonce reuse.</exception>
		public byte[] NextSendNonce()
		{
			if (SendCounter == uint.MaxValue)
				throw new CryptographicException("AES-GCM send counter exhausted. Session must be renegotiated.");
			return CryptoHelper.BuildGcmNonce(SessionPrefix, SendCounter++, serverToClient: true);
		}

		/// <summary>
		/// Builds the next client→server GCM nonce and advances <see cref="ReceiveCounter"/>.
		/// </summary>
		/// <returns>A 12-byte nonce for AES-GCM decryption.</returns>
		/// <exception cref="System.Security.Cryptography.CryptographicException">Thrown when the counter would overflow, which would cause nonce reuse.</exception>
		public byte[] NextReceiveNonce()
		{
			if (ReceiveCounter == uint.MaxValue)
				throw new CryptographicException("AES-GCM receive counter exhausted. Session must be renegotiated.");
			return CryptoHelper.BuildGcmNonce(SessionPrefix, ReceiveCounter++, serverToClient: false);
		}

		/// <summary>
		/// Zeroes all sensitive key material and resets counters.
		/// Call before removing the entry from the <c>AccountManager</c>.
		/// </summary>
		public void Clear()
		{
			if (SymmetricKey != null)
			{
				CryptographicOperations.ZeroMemory(SymmetricKey);
				SymmetricKey = null;
			}
			if (SessionPrefix != null)
			{
				CryptographicOperations.ZeroMemory(SessionPrefix);
				SessionPrefix = null;
			}
			PublicKey = null;
			SendCounter = 0;
			ReceiveCounter = 0;
		}
	}
}