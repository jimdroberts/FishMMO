using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct CreateAccountBroadcast : IBroadcast
	{
		public byte[] username;
		public byte[] salt;
		public byte[] verifier;
	}

	public struct ClientHandshake : IBroadcast
	{
		public byte[] publicKey;
	}

	public struct ServerHandshake : IBroadcast
	{
		public byte[] key;
		public byte[] iv;
	}

	public struct SrpVerifyBroadcast : IBroadcast
	{
		public byte[] s;
		public byte[] publicEphemeral;
	}

	public struct SrpProofBroadcast : IBroadcast
	{
		public byte[] proof;
	}

	public struct SrpSuccess : IBroadcast
	{
	}

	public struct ClientAuthResultBroadcast : IBroadcast
	{
		public ClientAuthenticationResult result;
	}
}