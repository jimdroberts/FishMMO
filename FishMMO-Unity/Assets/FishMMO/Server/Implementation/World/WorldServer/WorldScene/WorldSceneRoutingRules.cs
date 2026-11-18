using System;
using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// The decisions the world server's scene routing makes, as pure functions.
	/// </summary>
	/// <remarks>
	/// <see cref="WorldSceneSystem"/> gathers everything a routing pass needs in a few batched
	/// reads, decides with these, then writes and sends in batches. Keeping the decisions here, free
	/// of the network and the database, is what lets tests pin them as tables.
	/// </remarks>
	public static class WorldSceneRoutingRules
	{
		/// <summary>
		/// One connection waiting on the open-world queue, as the selection sees it.
		/// </summary>
		public readonly struct QueueCandidate<T>
		{
			/// <summary>The waiting connection.</summary>
			public readonly T Item;

			/// <summary>
			/// When it began waiting, in seconds on the world server's monotonic clock. The same
			/// clock the queue positions are ranked by.
			/// </summary>
			public readonly double WaitingSince;

			/// <summary>Breaks ties between equal <see cref="WaitingSince"/>, so the order is total.</summary>
			public readonly int ClientId;

			/// <summary>
			/// True for a connection that has to be looked at on every pass whatever the capacity:
			/// one held for its combat-logout body, which is waiting on one specific instance
			/// rather than on a free slot anywhere.
			/// </summary>
			public readonly bool AlwaysRoute;

			public QueueCandidate(T item, double waitingSince, int clientId, bool alwaysRoute)
			{
				Item = item;
				WaitingSince = waitingSince;
				ClientId = clientId;
				AlwaysRoute = alwaysRoute;
			}
		}

		/// <summary>
		/// Picks which waiting connections one open-world routing pass takes off the queue.
		/// </summary>
		/// <param name="candidates">Everyone waiting on the scene. Reordered in place.</param>
		/// <param name="freeCapacity">Free slots across every routable instance of the scene.</param>
		/// <param name="selected">Receives the picked connections, oldest first.</param>
		/// <remarks>
		/// <para>
		/// The oldest <paramref name="freeCapacity"/> connections, plus every
		/// <see cref="QueueCandidate{T}.AlwaysRoute"/> one. A pass cannot place more than the free
		/// capacity, so taking the whole queue — which it used to — read a character row for every
		/// waiting player and put back everyone it could not place, one queued action each: 2,500
		/// rows and 2,490 re-queues every two seconds to route ten.
		/// </para>
		/// <para>
		/// Oldest first by the same order the queue positions are reported in (arrival, then client
		/// id), so the players told they are at the front are the ones placed first. A set has no
		/// order of its own, so the whole-queue pass handed the free slots to whoever happened to
		/// enumerate first.
		/// </para>
		/// </remarks>
		public static void SelectForRouting<T>(List<QueueCandidate<T>> candidates, int freeCapacity, List<T> selected)
		{
			if (candidates == null || selected == null || candidates.Count == 0)
			{
				return;
			}

			candidates.Sort(CompareByArrival);

			int taken = 0;
			for (int i = 0; i < candidates.Count; ++i)
			{
				QueueCandidate<T> candidate = candidates[i];
				if (candidate.AlwaysRoute)
				{
					selected.Add(candidate.Item);
				}
				else if (taken < freeCapacity)
				{
					selected.Add(candidate.Item);
					++taken;
				}
			}
		}

		private static int CompareByArrival<T>(QueueCandidate<T> a, QueueCandidate<T> b)
		{
			int byTime = a.WaitingSince.CompareTo(b.WaitingSince);
			return byTime != 0 ? byTime : a.ClientId.CompareTo(b.ClientId);
		}

		/// <summary>
		/// Whether a queued connection's wait has run out.
		/// </summary>
		/// <remarks>
		/// The wait is timed from the later of joining the queue and the scene's last placement.
		/// The queue is first in, first out, so while it is placing anyone the players behind are
		/// moving forward and keep their place; only a line that has placed nobody for a whole TTL
		/// sends its waiters back to choose again. Timed from joining alone, a queue longer than one
		/// TTL's worth of placements purged its tail while the line moved, and a retry went to the
		/// back, so those players cycled forever.
		/// </remarks>
		/// <param name="queuedAt">When the connection joined the queue, in monotonic seconds.</param>
		/// <param name="lastPlacementAt">When its scene last placed anyone, or null if it never has.</param>
		/// <param name="now">The current monotonic reading.</param>
		/// <param name="ttlSeconds">The TTL for the kind of wait this is.</param>
		/// <returns>True when the connection should be purged.</returns>
		public static bool QueueWaitExpired(double queuedAt, double? lastPlacementAt, double now, double ttlSeconds)
		{
			double since = lastPlacementAt.HasValue && lastPlacementAt.Value > queuedAt ? lastPlacementAt.Value : queuedAt;
			return now - since >= ttlSeconds;
		}

		/// <summary>
		/// Whether an open-world assignment has to rewrite the character's scene binding before the
		/// client is sent on.
		/// </summary>
		/// <remarks>
		/// The scene server matches an arriving character on the (world server, scene, handle) it
		/// reads back from the row, so any of the three being stale gets the client refused.
		/// </remarks>
		public static bool NeedsSceneBind(long characterWorldServerID, long characterSceneHandle, long worldServerID, long assignedHandle)
		{
			return characterSceneHandle != assignedHandle || characterWorldServerID != worldServerID;
		}

		/// <summary>
		/// Whether a character being sent to its instance has to be bound to this world server
		/// first.
		/// </summary>
		/// <remarks>
		/// Only the world server id can be stale here: the instance route does not change the
		/// character's scene. An unknown world server id (0) binds nothing.
		/// </remarks>
		public static bool NeedsWorldBind(long characterWorldServerID, long worldServerID)
		{
			return worldServerID > 0 && characterWorldServerID != worldServerID;
		}

		/// <summary>What a read of one row answered.</summary>
		public enum RowLookup
		{
			/// <summary>The row is there.</summary>
			Found,

			/// <summary>The read succeeded and the row is not there.</summary>
			Absent,

			/// <summary>The read failed, which says nothing either way.</summary>
			Failed,
		}

		/// <summary>Where a character bound to an instance goes this pass.</summary>
		public enum InstanceRoute
		{
			/// <summary>To the scene server hosting the instance.</summary>
			Connect,

			/// <summary>Back on the instance queue: the instance is still loading.</summary>
			WaitForLoad,

			/// <summary>Back on the instance queue: a read failed, so nothing is known.</summary>
			Retry,

			/// <summary>Out of the instance, which is gone or never coming, and to the open world.</summary>
			Release,

			/// <summary>
			/// As <see cref="Release"/>, and the instance's row is deleted: it names a scene server
			/// that has stopped or is no longer registered.
			/// </summary>
			ReleaseDeletingScene,
		}

		/// <summary>
		/// Decides where a character bound to an instance goes.
		/// </summary>
		/// <param name="scene">What the read of the instance's scene row answered.</param>
		/// <param name="status">The row's status, when it was found.</param>
		/// <param name="sceneAgeSeconds">The row's age by the database clock, when it was found.</param>
		/// <param name="sceneServer">What the read of the hosting scene server answered. Only consulted for a Ready row.</param>
		/// <param name="sceneServerLive">Whether that scene server is still pulsing, when it was found.</param>
		/// <param name="readyGraceSeconds">How long a row may stay short of Ready before the character is released.</param>
		/// <remarks>
		/// <para>
		/// Only an answer releases a character. A read that failed is not an answer, and treating it
		/// as one cleared the instance flag of a character whose instance was alive, or deleted the
		/// row of a live, populated instance so every other member was ejected on their next
		/// reconnect.
		/// </para>
		/// <para>
		/// A row that is still loading is bounded by its own age, not by the connection's wait:
		/// the queue TTL ends one visit, the client reconnects, and without a bound on the row it
		/// is queued behind the same dead instance forever.
		/// </para>
		/// </remarks>
		public static InstanceRoute DecideInstanceRoute(
			RowLookup scene,
			SceneStatus status,
			double sceneAgeSeconds,
			RowLookup sceneServer,
			bool sceneServerLive,
			double readyGraceSeconds)
		{
			switch (scene)
			{
				case RowLookup.Failed:
					return InstanceRoute.Retry;
				case RowLookup.Absent:
					return InstanceRoute.Release;
			}

			switch (status)
			{
				case SceneStatus.Ready:
					switch (sceneServer)
					{
						case RowLookup.Failed:
							return InstanceRoute.Retry;
						case RowLookup.Found when sceneServerLive:
							return InstanceRoute.Connect;
						default:
							return InstanceRoute.ReleaseDeletingScene;
					}

				case SceneStatus.Pending:
				case SceneStatus.Loading:
					return sceneAgeSeconds >= readyGraceSeconds
						? InstanceRoute.Release
						: InstanceRoute.WaitForLoad;

				default:
					// Failed, or a status this build does not know: nothing will ever make it ready.
					return InstanceRoute.Release;
			}
		}
	}
}
