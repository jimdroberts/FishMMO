using System;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// The place or thing the player chose on the world map to be guided to. Client-side only: nothing
	/// is sent, and the server neither knows nor cares.
	/// </summary>
	/// <remarks>
	/// <para>Chosen by clicking any marker on the world map (<see cref="UITKMap"/>); drawn by the minimap as a
	/// ring on the target while it is in view and an arrow on the frame's rim pointing at it while it is
	/// not (<see cref="UITKMapView.GuideTarget"/>). Both maps mark the chosen marker with
	/// <see cref="UITKMapView.MarkerGuidedClass"/>.</para>
	/// <para><b>A fixed target</b> — a note, a landmark, a waypoint, a place — is kept as a position, and is
	/// let go of once the player arrives.</para>
	/// <para><b>A live target</b> — a marker on an object, such as an NPC — is followed through
	/// <see cref="MapMarkerFilter.TryResolve"/>, the map's own visibility rules, so the guide is never told
	/// more about it than the map draws: a detection-tier target is followed at its coarse position, and
	/// one the player may no longer see is kept at the last place it was seen. The marker is let go of the
	/// moment it leaves the registry, because a pooled object returns as a different creature.</para>
	/// <para>Per scene: changing scene clears it.</para>
	/// </remarks>
	public static class MapGuide
	{
		/// <summary>How close, in metres across the ground, counts as having arrived at a fixed target.</summary>
		public const float ArrivalDistance = 6.0f;

		/// <summary>Raised when the target is chosen, replaced or cleared.</summary>
		public static event Action OnChanged;

		/// <summary>Whether there is a target.</summary>
		public static bool HasTarget { get; private set; }

		/// <summary>What the target is called, for the side bar.</summary>
		public static string Name { get; private set; }

		/// <summary>The scene the target is in.</summary>
		public static string SceneName { get; private set; }

		/// <summary>Whether the target is followed live, rather than being a fixed position.</summary>
		public static bool IsLive => source != null;

		/// <summary>Whether a live target has been lost from the map and is shown at the last place it was seen.</summary>
		public static bool IsLastKnown { get; private set; }

		/// <summary>The marker followed, for a live target.</summary>
		private static MapMarker source;

		/// <summary>The fixed position, or the last resolved position of a live target.</summary>
		private static Vector3 position;

		/// <summary>The type of the chosen marker, which with the position identifies a place.</summary>
		private static MapMarkerType type;

		/// <summary>The chosen note, or zero.</summary>
		private static long noteID;

		/// <summary>Whether the chosen marker is a waypoint.</summary>
		private static bool isWaypoint;

		/// <summary>The chosen waypoint's index. Meaningful only when <see cref="isWaypoint"/>.</summary>
		private static int waypointIndex;

		/// <summary>The frame the target was last resolved on, so the two maps resolve it once a frame between them.</summary>
		private static int resolvedFrame = -1;

		static MapGuide()
		{
			MapMarkerRegistry.OnMarkerUnregistered += OnMarkerUnregistered;
		}

		/// <summary>
		/// Whether a marker can be guided to: everything but the player's own.
		/// </summary>
		/// <param name="snapshot">The marker.</param>
		public static bool CanGuideTo(in MapMarkerSnapshot snapshot)
		{
			return snapshot.Type != MapMarkerType.Self;
		}

		/// <summary>
		/// Makes a marker the target.
		/// </summary>
		/// <param name="snapshot">The marker as the map drew it.</param>
		/// <param name="sceneName">The scene it is in.</param>
		/// <returns>True when it became the target.</returns>
		public static bool Set(in MapMarkerSnapshot snapshot, string sceneName)
		{
			if (!CanGuideTo(in snapshot))
			{
				return false;
			}

			HasTarget = true;
			SceneName = sceneName;
			Name = DescribeSnapshot(in snapshot);
			source = snapshot.Source;
			position = snapshot.TracksSource && snapshot.Source != null ? snapshot.Source.Position : snapshot.Position;
			type = snapshot.Type;
			noteID = snapshot.NoteID;
			isWaypoint = snapshot.IsWaypoint;
			waypointIndex = snapshot.WaypointIndex;
			IsLastKnown = false;
			resolvedFrame = -1;

			OnChanged?.Invoke();
			return true;
		}

		/// <summary>Drops the target.</summary>
		public static void Clear()
		{
			if (!HasTarget)
			{
				return;
			}

			HasTarget = false;
			Name = null;
			SceneName = null;
			source = null;
			noteID = 0;
			isWaypoint = false;
			IsLastKnown = false;

			OnChanged?.Invoke();
		}

		/// <summary>
		/// Whether a drawn marker is the target.
		/// </summary>
		/// <param name="snapshot">The marker.</param>
		/// <param name="sceneName">The scene the map drawing it shows.</param>
		public static bool IsTarget(in MapMarkerSnapshot snapshot, string sceneName)
		{
			if (!HasTarget || !string.Equals(sceneName, SceneName, StringComparison.Ordinal))
			{
				return false;
			}
			if (source != null)
			{
				return ReferenceEquals(snapshot.Source, source);
			}
			if (noteID != 0)
			{
				return snapshot.NoteID == noteID;
			}
			if (isWaypoint)
			{
				return snapshot.IsWaypoint && snapshot.WaypointIndex == waypointIndex;
			}

			// A place: no identity but what it is and where. The same landmark comes back at the same point.
			return snapshot.Source == null && snapshot.NoteID == 0 && !snapshot.IsWaypoint && snapshot.Type == type &&
				(snapshot.Position - position).sqrMagnitude < 0.25f;
		}

		/// <summary>
		/// Where to guide the player this frame.
		/// </summary>
		/// <param name="local">The local player character.</param>
		/// <param name="fog">The explored map of the scene the character is in.</param>
		/// <param name="target">The position to point at.</param>
		/// <returns>False when there is no target in the character's scene.</returns>
		/// <remarks>
		/// Also where arrival is noticed: a fixed target, or the last place a lost one was seen, is cleared
		/// once the character reaches it, and the call answers false.
		/// </remarks>
		public static bool TryGetTarget(IPlayerCharacter local, FogOfWarMap fog, out Vector3 target)
		{
			target = default;
			if (!HasTarget || local == null || local.Transform == null)
			{
				return false;
			}

			if (!string.Equals(local.CurrentSceneName(), SceneName, StringComparison.Ordinal))
			{
				return false;
			}

			if (source != null && resolvedFrame != Time.frameCount)
			{
				resolvedFrame = Time.frameCount;
				if (ClientMapSystem.Filter.TryResolve(source, local, fog, out Vector3 resolved, out _))
				{
					position = resolved;
					IsLastKnown = false;
				}
				else
				{
					IsLastKnown = true;
				}
			}

			if (source == null)
			{
				Vector3 offset = position - local.Transform.position;
				offset.y = 0.0f;
				if (offset.sqrMagnitude <= ArrivalDistance * ArrivalDistance)
				{
					Clear();
					return false;
				}
			}

			target = position;
			return true;
		}

		/// <summary>
		/// Clears a target in another scene than the character's; called when the character's scene changes.
		/// </summary>
		/// <param name="sceneName">The scene the character is now in.</param>
		public static void OnSceneChanged(string sceneName)
		{
			if (HasTarget && !string.Equals(sceneName, SceneName, StringComparison.Ordinal))
			{
				Clear();
			}
		}

		/// <summary>
		/// Clears a note target whose note has been removed.
		/// </summary>
		/// <param name="noteExists">Answers whether a note id still exists.</param>
		public static void OnNotesChanged(Func<long, bool> noteExists)
		{
			if (HasTarget && noteID != 0 && !noteExists(noteID))
			{
				Clear();
			}
		}

		/// <summary>The text the side bar names a target by.</summary>
		/// <param name="snapshot">The marker.</param>
		/// <returns>Its label, else the first line of its tooltip, else its type.</returns>
		public static string DescribeSnapshot(in MapMarkerSnapshot snapshot)
		{
			if (!string.IsNullOrEmpty(snapshot.Label))
			{
				return snapshot.Label;
			}
			if (!string.IsNullOrEmpty(snapshot.Tooltip))
			{
				int newline = snapshot.Tooltip.IndexOf('\n');
				return newline >= 0 ? snapshot.Tooltip.Substring(0, newline) : snapshot.Tooltip;
			}
			return DescribeType(snapshot.Type);
		}

		/// <summary>A marker type as words: "HousingPlot" becomes "Housing Plot".</summary>
		private static string DescribeType(MapMarkerType markerType)
		{
			string raw = markerType.ToString();
			System.Text.StringBuilder builder = new System.Text.StringBuilder(raw.Length + 4);
			for (int i = 0; i < raw.Length; ++i)
			{
				if (i > 0 && char.IsUpper(raw[i]) && !char.IsUpper(raw[i - 1]))
				{
					builder.Append(' ');
				}
				builder.Append(raw[i]);
			}
			return builder.ToString();
		}

		/// <summary>
		/// Lets go of a live target's marker when it leaves the registry: despawned, pooled, or out of the
		/// observer's range. The target stays at the last place it was seen.
		/// </summary>
		private static void OnMarkerUnregistered(MapMarker marker)
		{
			if (marker == null || !ReferenceEquals(marker, source))
			{
				return;
			}
			source = null;
			IsLastKnown = true;
			OnChanged?.Invoke();
		}
	}
}
