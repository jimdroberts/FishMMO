using System;
using System.Collections.Concurrent;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for friend ingress protection.
	/// </summary>
	public class FriendSystemRuntimeData : RuntimeDataContainer, IFriendSystemRuntimeData
	{
		public ConcurrentDictionary<long, DateTime> NextAllowedIngressUtcByKey { get; private set; }
		public ConcurrentDictionary<long, byte> IngressInFlightByKey { get; private set; }
		public DateTime NextIngressSweepUtc { get; set; }

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			NextAllowedIngressUtcByKey = new ConcurrentDictionary<long, DateTime>();
			IngressInFlightByKey = new ConcurrentDictionary<long, byte>();
			NextIngressSweepUtc = DateTime.UtcNow;
			return ServerComponentInitializationStatus.Initialized;
		}

		public override void Clear()
		{
			NextAllowedIngressUtcByKey?.Clear();
			IngressInFlightByKey?.Clear();
			NextIngressSweepUtc = DateTime.UtcNow;
		}

		public override void Deinitialize()
		{
			Clear();
			NextAllowedIngressUtcByKey = null;
			IngressInFlightByKey = null;
		}
	}
}