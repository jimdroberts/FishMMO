using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data contract for the SceneChannelSystem.
	/// Stores all mutable state: ingress guard, per-connection cooldowns, and cleanup timers.
	/// </summary>
	public interface ISceneChannelSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Per-connection, per-operation ingress guard for DoS protection on channel broadcasts.
		/// </summary>
		IngressGuard IngressGuard { get; }

		/// <summary>
		/// Per-connection channel switch cooldown tracker.
		/// Maps client connection ID to the UTC time of their last channel switch.
		/// </summary>
		Dictionary<int, DateTime> ChannelSwitchCooldownByClientId { get; }

		/// <summary>
		/// Time remaining (in seconds) until the next cooldown dictionary cleanup sweep.
		/// </summary>
		float NextCooldownCleanup { get; set; }
	}
}