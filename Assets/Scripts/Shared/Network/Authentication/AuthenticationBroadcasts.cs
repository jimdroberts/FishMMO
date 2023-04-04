using FishNet.Broadcast;

public struct BeginClientAuthBroadcast : IBroadcast
{
	public string username;
	public string password;
}

public struct ClientAuthResultBroadcast : IBroadcast
{
	public ClientAuthenticationResult result;
}