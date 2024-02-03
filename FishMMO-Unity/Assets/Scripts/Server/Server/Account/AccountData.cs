using FishMMO.Shared;

namespace FishMMO.Server
{
	public class AccountData
	{
		public AccessLevel AccessLevel { get; private set; }
		public ServerSrpData SrpData { get; private set; }

		public AccountData(AccessLevel accessLevel, ServerSrpData srpData)
		{
			AccessLevel = accessLevel;
			SrpData = srpData;
		}
	}
}