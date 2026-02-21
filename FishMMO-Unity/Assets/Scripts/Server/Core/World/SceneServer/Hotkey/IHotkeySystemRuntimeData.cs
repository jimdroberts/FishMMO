using System;
using System.Collections.Concurrent;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for hotkey ingress guards.
	/// </summary>
	public interface IHotkeySystemRuntimeData : IRuntimeDataContainer
	{
		ConcurrentDictionary<long, DateTime> NextAllowedIngressUtcByKey { get; }
		ConcurrentDictionary<long, byte> IngressInFlightByKey { get; }
		DateTime NextIngressSweepUtc { get; set; }
	}
}
