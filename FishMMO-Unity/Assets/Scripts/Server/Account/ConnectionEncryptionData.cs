namespace FishMMO.Server
{
	public class ConnectionEncryptionData
	{
		public byte[] PublicKey;
		public byte[] SymmetricKey;
		public byte[] IV;

		public ConnectionEncryptionData(byte[] publicKey, byte[] symmetricKey, byte[] iv)
		{
			PublicKey = publicKey;
			SymmetricKey = symmetricKey;
			IV = iv;
		}
	}
}