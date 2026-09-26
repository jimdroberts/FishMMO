using System;
using FishMMO.Database.Data;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Combines a pending scene load request with its enqueue time,
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
		/// When this request was taken on, in seconds on <see cref="MonotonicClock"/>. Bounds how
		/// long the load may take.
		/// </summary>
		public readonly double EnqueuedAt;

		/// <summary>
		/// When the scene row was created, in seconds on <see cref="MonotonicClock"/>. Carried to
		/// the loaded instance as <see cref="ISceneInstanceDetails.CreatedAt"/>.
		/// </summary>
		public readonly double RowCreatedAt;

		/// <summary>
		/// Initializes a new pending scene info.
		/// </summary>
		/// <param name="sceneData">The database scene data for this pending load request.</param>
		/// <param name="enqueuedAt">Monotonic time at which this request was taken on.</param>
		/// <param name="rowCreatedAt">Monotonic time at which the scene row was created.</param>
		public PendingSceneInfo(SceneData sceneData, double enqueuedAt, double rowCreatedAt)
		{
			SceneData = sceneData;
			EnqueuedAt = enqueuedAt;
			RowCreatedAt = rowCreatedAt;
		}
	}
}