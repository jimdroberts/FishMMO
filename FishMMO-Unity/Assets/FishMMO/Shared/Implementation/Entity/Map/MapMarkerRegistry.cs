using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// The set of <see cref="MapMarker"/> components currently in the world, for the map panels
	/// to draw from.
	/// </summary>
	/// <remarks>
	/// <para>A registry rather than a per-frame scene search. The map refreshes several times a
	/// second and would otherwise call <c>FindObjectsByType</c> at that rate over every object in
	/// a streamed-in zone, which is the single most expensive way to answer a question the objects
	/// themselves already know the answer to.</para>
	///
	/// <para>Membership is not visibility. Everything with a marker component is in here,
	/// including objects the local player must not see on the map; the filtering is the client's
	/// job and happens on the way out. That split is deliberate — a registry that filtered on
	/// entry would have to be rebuilt whenever a party changed or a player moved, and the one
	/// thing worse than a map that shows too much is a map whose contents depend on when a
	/// GameObject happened to be enabled.</para>
	/// </remarks>
	public static class MapMarkerRegistry
	{
		/// <summary>Every registered marker.</summary>
		private static readonly HashSet<MapMarker> markers = new HashSet<MapMarker>();

		/// <summary>
		/// Raised when a marker joins the registry.
		/// </summary>
		/// <remarks>
		/// The map panels poll the collection rather than maintaining their own copy from these,
		/// so nothing subscribes today. They exist for a panel that wants to react to an arrival
		/// without polling — a "new landmark discovered" notification, for instance.
		/// </remarks>
		public static event Action<MapMarker> OnMarkerRegistered;

		/// <summary>Raised when a marker leaves the registry.</summary>
		public static event Action<MapMarker> OnMarkerUnregistered;

		/// <summary>Every registered marker. Do not mutate.</summary>
		public static IReadOnlyCollection<MapMarker> Markers => markers;

		/// <summary>How many markers are registered.</summary>
		public static int Count => markers.Count;

		/// <summary>
		/// Adds a marker to the registry.
		/// </summary>
		/// <param name="marker">The marker to add. Null is ignored.</param>
		public static void Register(MapMarker marker)
		{
			if (marker == null)
			{
				return;
			}

			if (markers.Add(marker))
			{
				OnMarkerRegistered?.Invoke(marker);
			}
		}

		/// <summary>
		/// Removes a marker from the registry.
		/// </summary>
		/// <param name="marker">The marker to remove. Null is ignored.</param>
		public static void Unregister(MapMarker marker)
		{
			if (marker == null)
			{
				return;
			}

			if (markers.Remove(marker))
			{
				OnMarkerUnregistered?.Invoke(marker);
			}
		}

		/// <summary>
		/// Empties the registry.
		/// </summary>
		/// <remarks>
		/// Called on quit to login. Every marker unregisters itself when its object is disabled,
		/// so in the ordinary case this finds nothing — but a scene teardown that destroys objects
		/// without disabling them first leaves entries behind, and a static collection holding
		/// destroyed <c>MonoBehaviour</c>s keeps their whole GameObject hierarchy alive for the
		/// rest of the session.
		/// </remarks>
		public static void Clear()
		{
			markers.Clear();
		}

		/// <summary>
		/// Drops any entries whose component has been destroyed.
		/// </summary>
		/// <returns>How many entries were removed.</returns>
		/// <remarks>
		/// A safety net for the disable-less teardown described on <see cref="Clear"/>, run
		/// occasionally by the map panels rather than every refresh: the comparison against null on
		/// a <c>MonoBehaviour</c> is an engine call, and doing it for every marker on every frame
		/// is a measurable cost to catch a case that is rare by design.
		/// </remarks>
		public static int Prune()
		{
			return markers.RemoveWhere(marker => marker == null);
		}
	}
}
