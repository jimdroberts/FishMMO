namespace FishMMO.Client
{
	public enum ServerConnectionType : byte
	{
		None,
		Login,
		ConnectingToWorld,
		World,
		Scene,
	}
}