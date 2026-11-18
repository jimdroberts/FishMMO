using System;
using System.Collections.Generic;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Places in the open-world queue held for accounts whose wait ended without their say, so
	/// that coming back within a grace window resumes the place instead of starting at the back.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The open-world queue is first in, first out by when each connection began waiting
	/// (<see cref="WorldSceneRoutingRules.SelectForRouting{T}"/>). That order belonged to the
	/// connection, so anything that ended the connection ended the place: a dropped link that
	/// reconnected four seconds later, or a player the server sent back because the line had
	/// stalled, rejoined behind everyone who arrived after them. On a full world a flaky link
	/// meant never reaching the front.
	/// </para>
	/// <para>
	/// A place is remembered per account, for one scene, with the time the account began waiting.
	/// Rejoining that scene's queue within <see cref="GraceSeconds"/> resumes it; rejoining any
	/// other scene, or after the window, starts fresh. A resumed place is spent: the same wait
	/// can be remembered again if it ends again, but it cannot be claimed twice. A player who
	/// chose to leave the queue is <see cref="Forget"/>-ed and loses it.
	/// </para>
	/// <para>
	/// Every time is a <see cref="FishMMO.Server.Core.MonotonicClock"/> reading. Main thread only;
	/// no Unity or network dependency, so the rules are pinned by tests.
	/// </para>
	/// </remarks>
	public sealed class WorldQueuePlaceMemory
	{
		/// <summary>One remembered place.</summary>
		public readonly struct Place
		{
			/// <summary>The open-world scene whose queue the place is in.</summary>
			public readonly string SceneName;

			/// <summary>When the account began waiting: the value the queue is ordered by.</summary>
			public readonly double WaitingSince;

			/// <summary>When the wait ended and the place was remembered. The grace window runs from here.</summary>
			public readonly double RememberedAt;

			public Place(string sceneName, double waitingSince, double rememberedAt)
			{
				SceneName = sceneName;
				WaitingSince = waitingSince;
				RememberedAt = rememberedAt;
			}
		}

		/// <summary>
		/// Account names compare as the account manager and the routing debounce compare them,
		/// ignoring case.
		/// </summary>
		private readonly Dictionary<string, Place> placesByAccount =
			new Dictionary<string, Place>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Reused by <see cref="Sweep"/>.</summary>
		private readonly List<string> expiredScratch = new List<string>();

		/// <summary>
		/// How long a place is held after the wait ended. Zero or less holds nothing.
		/// </summary>
		public double GraceSeconds { get; set; } = 60.0;

		/// <summary>How many places are held.</summary>
		public int Count => placesByAccount.Count;

		/// <summary>
		/// Remembers where an account stood when its wait ended without its say.
		/// </summary>
		/// <param name="accountName">The account.</param>
		/// <param name="sceneName">The open-world scene it was waiting for.</param>
		/// <param name="waitingSince">When it began waiting.</param>
		/// <param name="now">The current monotonic reading.</param>
		/// <remarks>
		/// A place already held for the same scene keeps the earlier of the two starts: an account
		/// that had two connections in the line (a reconnect that arrived before the old one was
		/// noticed gone) is owed the older place, whichever connection ends last.
		/// </remarks>
		public void Remember(string accountName, string sceneName, double waitingSince, double now)
		{
			if (GraceSeconds <= 0.0 || string.IsNullOrEmpty(accountName) || string.IsNullOrEmpty(sceneName))
			{
				return;
			}

			if (placesByAccount.TryGetValue(accountName, out Place held) &&
				IsHeld(held, sceneName, now, GraceSeconds) &&
				held.WaitingSince < waitingSince)
			{
				waitingSince = held.WaitingSince;
			}

			placesByAccount[accountName] = new Place(sceneName, waitingSince, now);
		}

		/// <summary>
		/// Takes back the place an account left in a scene's queue, if it is still held.
		/// </summary>
		/// <param name="accountName">The account joining the queue.</param>
		/// <param name="sceneName">The open-world scene whose queue it is joining.</param>
		/// <param name="now">The current monotonic reading.</param>
		/// <param name="waitingSince">When the account originally began waiting, when resumed.</param>
		/// <returns>True when the place was resumed.</returns>
		/// <remarks>
		/// Whatever is held for the account is dropped either way. Joining a different scene's
		/// queue ends the old wait, and an expired place was never going to be claimed.
		/// </remarks>
		public bool TryResume(string accountName, string sceneName, double now, out double waitingSince)
		{
			waitingSince = 0.0;
			if (string.IsNullOrEmpty(accountName) || !placesByAccount.TryGetValue(accountName, out Place held))
			{
				return false;
			}

			placesByAccount.Remove(accountName);
			if (!IsHeld(held, sceneName, now, GraceSeconds))
			{
				return false;
			}

			waitingSince = held.WaitingSince;
			return true;
		}

		/// <summary>
		/// Gives up whatever place an account holds. For a player who chose to leave, and for one
		/// who has been placed.
		/// </summary>
		public void Forget(string accountName)
		{
			if (!string.IsNullOrEmpty(accountName))
			{
				placesByAccount.Remove(accountName);
			}
		}

		/// <summary>Drops every place whose grace window has closed.</summary>
		/// <param name="now">The current monotonic reading.</param>
		/// <returns>How many were dropped.</returns>
		public int Sweep(double now)
		{
			if (placesByAccount.Count == 0)
			{
				return 0;
			}

			expiredScratch.Clear();
			foreach (var kvp in placesByAccount)
			{
				if (!IsWithinGrace(kvp.Value, now, GraceSeconds))
				{
					expiredScratch.Add(kvp.Key);
				}
			}

			for (int i = 0; i < expiredScratch.Count; ++i)
			{
				placesByAccount.Remove(expiredScratch[i]);
			}

			int removed = expiredScratch.Count;
			expiredScratch.Clear();
			return removed;
		}

		/// <summary>Forgets every place.</summary>
		public void Clear()
		{
			placesByAccount.Clear();
			expiredScratch.Clear();
		}

		/// <summary>
		/// Whether a remembered place can be resumed by a join of <paramref name="sceneName"/>'s
		/// queue at <paramref name="now"/>: the same queue, within the grace window.
		/// </summary>
		/// <remarks>
		/// The scene name is compared ordinally, as the waiting queues key their scenes.
		/// </remarks>
		public static bool IsHeld(Place place, string sceneName, double now, double graceSeconds)
		{
			return string.Equals(place.SceneName, sceneName, StringComparison.Ordinal) &&
				IsWithinGrace(place, now, graceSeconds);
		}

		private static bool IsWithinGrace(Place place, double now, double graceSeconds)
		{
			double held = now - place.RememberedAt;
			return graceSeconds > 0.0 && held >= 0.0 && held < graceSeconds;
		}
	}
}
