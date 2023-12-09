using FishMMO.Shared;

namespace FishMMO.Server
{
	public class AccountData
	{
		public AccessLevel AccessLevel { get; private set; }
		public ServerSRPData SrpData { get; private set; }

		public AccountData(AccessLevel accessLevel, ServerSRPData srpData)
		{
			AccessLevel = accessLevel;
			SrpData = srpData;
		}
	}
}