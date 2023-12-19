using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct CreateAccountBroadcast : IBroadcast
	{
		public string username;
		public string salt;
		public string verifier;
	}

	public struct SrpVerifyBroadcast : IBroadcast
	{
		public string s;
		public string publicEphemeral;
	}

	public struct SrpProofBroadcast : IBroadcast
	{
		public string proof;
	}

	public struct SrpSuccess : IBroadcast
	{
	}

	public struct ClientAuthResultBroadcast : IBroadcast
	{
		public ClientAuthenticationResult result;
	}
}