using System;

namespace Server
{
	[Serializable]
	public struct SceneInstanceDetails
	{
		public string name;
		public int handle;
		public int clientCount;
	}
}