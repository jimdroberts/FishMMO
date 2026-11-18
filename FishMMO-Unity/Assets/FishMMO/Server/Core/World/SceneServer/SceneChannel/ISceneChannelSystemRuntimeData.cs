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
		/// Maps client connection ID to when it last attempted a channel switch, in seconds on
		/// <see cref="MonotonicClock"/>.
		/// </summary>
		/// <remarks>
		/// A duration's start, so not a wall-clock instant: a host clock stepped back would hold
		/// every entry inside its cooldown until the clock caught up.
		/// </remarks>
		Dictionary<int, double> ChannelSwitchCooldownByClientId { get; }

		/// <summary>
		/// Time remaining (in seconds) until the next cooldown dictionary cleanup sweep.
		/// </summary>
		float NextCooldownCleanup { get; set; }
	}
}