using System;

namespace FishMMO.Server
{
	[Serializable]
	public struct SceneInstanceDetails
	{
		public string Name;
		public int Handle;
		public int ClientCount;
	}
}