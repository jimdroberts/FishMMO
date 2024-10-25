using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct CreateAccountBroadcast : IBroadcast
	{
		public byte[] Username;
		public byte[] Salt;
		public byte[] Verifier;
	}

	public struct ClientHandshake : IBroadcast
	{
		public byte[] PublicKey;
	}

	public struct ServerHandshake : IBroadcast
	{
		public byte[] Key;
		public byte[] IV;
	}

	public struct SrpVerifyBroadcast : IBroadcast
	{
		public byte[] S;
		public byte[] PublicEphemeral;
	}

	public struct SrpProofBroadcast : IBroadcast
	{
		public byte[] Proof;
	}

	public struct SrpSuccess : IBroadcast
	{
	}

	public struct ClientAuthResultBroadcast : IBroadcast
	{
		public ClientAuthenticationResult Result;
	}
}