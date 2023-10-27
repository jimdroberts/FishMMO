using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct CreateAccountBroadcast : IBroadcast
	{
		public string username;
		public string salt;
		public string verifier;
	}

	public struct SRPVerifyBroadcast : IBroadcast
	{
		public string s;
		public string publicEphemeral;
	}

	public struct SRPProofBroadcast : IBroadcast
	{
		public string proof;
	}

	public struct SRPSuccess : IBroadcast
	{
	}

	public struct ClientAuthResultBroadcast : IBroadcast
	{
		public ClientAuthenticationResult result;
	}
}