using System;
using FishMMO.Database.Data;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Combines a pending scene load request with its enqueue timestamp,
	/// eliminating the need for separate synchronized dictionaries.
	/// Previously PendingScenes (SceneData) and PendingSceneEnqueueUtcBySceneId (DateTime)
	/// were tracked in two separate maps, creating a dual-map sync risk.
	/// </summary>
	public readonly struct PendingSceneInfo
	{
		/// <summary>
		/// The database scene data for this pending load request.
		/// </summary>
		public readonly SceneData SceneData;

		/// <summary>
		/// UTC timestamp when this request was enqueued, used for TTL expiration.
		/// </summary>
		public readonly DateTime EnqueuedUtc;

		public PendingSceneInfo(SceneData sceneData, DateTime enqueuedUtc)
		{
			SceneData = sceneData;
			EnqueuedUtc = enqueuedUtc;
		}
	}
}
