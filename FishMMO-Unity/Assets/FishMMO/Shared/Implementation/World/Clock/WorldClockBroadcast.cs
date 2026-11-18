using FishNet.Broadcast;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server → client: the world clock's anchor, and the one it is easing away from, so a client
	/// that arrives mid-correction computes the same time as everyone else.
	/// </summary>
	public struct WorldClockBroadcast : IBroadcast
	{
		public WorldClockAnchor Anchor;
		public WorldClockAnchor Previous;
		public bool HasPrevious;
		public uint SlewTicks;
	}
}
