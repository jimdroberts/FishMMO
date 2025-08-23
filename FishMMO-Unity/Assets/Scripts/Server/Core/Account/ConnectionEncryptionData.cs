namespace FishMMO.Server.Core.Account
{
	/// <summary>
	/// Holds encryption data for a network connection, including public key, symmetric key, and IV.
	/// </summary>
	public class ConnectionEncryptionData
	{
		/// <summary>
		/// The public key used for asymmetric encryption.
		/// </summary>
		public byte[] PublicKey;

		/// <summary>
		/// The symmetric key used for encrypting data.
		/// </summary>
		public byte[] SymmetricKey;

		/// <summary>
		/// The initialization vector (IV) for symmetric encryption.
		/// </summary>
		public byte[] IV;

		/// <summary>
		/// Initializes a new instance of the <see cref="ConnectionEncryptionData"/> class.
		/// </summary>
		/// <param name="publicKey">The public key for asymmetric encryption.</param>
		/// <param name="symmetricKey">The symmetric key for encryption.</param>
		/// <param name="iv">The initialization vector for encryption.</param>
		public ConnectionEncryptionData(byte[] publicKey, byte[] symmetricKey, byte[] iv)
		{
			PublicKey = publicKey;
			SymmetricKey = symmetricKey;
			IV = iv;
		}
	}
}