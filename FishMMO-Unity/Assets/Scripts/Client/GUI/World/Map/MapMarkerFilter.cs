using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Decides which markers the local player may see, and at what fidelity.
	/// </summary>
	/// <remarks>
	/// <para>This is the map system's answer to "the minimap is a radar hack that ships with the
	/// game". The observer system upstream already decides which entities exist on this client at
	/// all; this decides which of those the map is willing to draw, and — for the ones it draws
	/// grudgingly — how stale and how coarse the position it draws them at is.</para>
	///
	/// <para><b>Three tiers.</b> The player's own character, their party and their guild are drawn
	/// exactly and continuously; the group already shares positions through the party frames, so
	/// the map is not the leak. World fixtures are drawn exactly and continuously because their
	/// positions are public and unchanging. Everyone else — hostile and neutral players — is drawn
	/// only inside <see cref="DetectionRadius"/>, refreshed at
	/// <see cref="ThrottleInterval"/>, snapped to a <see cref="PositionQuantum"/> grid, and never
	/// labelled.</para>
	///
	/// <para><b>Why that is worth doing at all,</b> given that a modified client can skip it: the
	/// throttled position is the only position this code ever <i>produces</i>. A client that
	/// deletes the filter still has to read the entity's transform to beat it — at which point it
	/// is a memory-reading cheat with or without a minimap, and the map has not made it easier.
	/// What the filter does buy is that the detection radius is genuinely smaller than the
	/// observer range that put the entity here, so the honest client's map is strictly less
	/// informative than the network stream, and every value it draws has been through a rule.</para>
	/// </remarks>
	public sealed class MapMarkerFilter
	{
		/// <summary>
		/// How close a hostile or neutral player must be before their marker appears, in metres.
		/// </summary>
		/// <remarks>
		/// Deliberately below <c>ObserverStreamingPolicy.MinimumRange</c>, which is the floor the
		/// observer system will shrink a character's streaming radius to under load. Keeping this
		/// under that floor is what makes the guarantee hold in the worst case as well as the
		/// usual one: there is no server condition under which the map would be showing more than
		/// the stream already contains.
		/// </remarks>
		public float DetectionRadius { get; set; } = 20.0f;

		/// <summary>How often a throttled marker's position is re-sampled, in seconds.</summary>
		public float ThrottleInterval { get; set; } = 1.0f;

		/// <summary>Grid, in metres, that a throttled marker's position is snapped to.</summary>
		/// <remarks>
		/// Four metres — one fog cell. Enough to say "something hostile is over there", not enough
		/// to aim with. Combined with the one-second refresh, a throttled marker tells a player
		/// roughly where an enemy was, which is what a map is for, rather than exactly where they
		/// are, which is what an aim assist is for.
		/// </remarks>
		public float PositionQuantum { get; set; } = 4.0f;

		/// <summary>The last position published for each throttled marker.</summary>
		private readonly Dictionary<MapMarker, ThrottledMarker> throttled = new Dictionary<MapMarker, ThrottledMarker>();

		/// <summary>Markers whose objects have gone, collected during a sweep and removed after it.</summary>
		private readonly List<MapMarker> expired = new List<MapMarker>();

		/// <summary>Time, on the unscaled clock, of the next expiry sweep.</summary>
		private double nextSweepTime;

		/// <summary>How often stale throttle entries are swept out, in seconds.</summary>
		private const double SweepInterval = 10.0;

		/// <summary>What was last published about a throttled marker.</summary>
		private struct ThrottledMarker
		{
			/// <summary>The coarsened position last published.</summary>
			public Vector3 Position;

			/// <summary>Time, on the unscaled clock, at which it may next be re-sampled.</summary>
			public double NextSampleTime;

			/// <summary>Whether the marker was inside the detection radius when last sampled.</summary>
			public bool WasDetected;
		}

		/// <summary>
		/// Collects everything the local player may see.
		/// </summary>
		/// <param name="results">List to fill. Cleared first.</param>
		/// <param name="local">The local player character. May be null.</param>
		/// <param name="forWorldMap">True for the world map, false for the minimap.</param>
		/// <param name="fog">The explored map, used by the discovery rule. May be null.</param>
		public void Collect(List<MapMarkerSnapshot> results, IPlayerCharacter local, bool forWorldMap, FogOfWarMap fog)
		{
			results.Clear();

			double now = Time.unscaledTimeAsDouble;
			Vector3 localPosition = local != null && local.Transform != null ? local.Transform.position : Vector3.zero;
			float detectionSquared = DetectionRadius * DetectionRadius;

			foreach (MapMarker marker in MapMarkerRegistry.Markers)
			{
				if (marker == null)
				{
					continue;
				}

				if (forWorldMap ? !marker.ShowOnWorldMap : !marker.ShowOnMinimap)
				{
					continue;
				}

				MapRelationship relationship = MapRelationshipTracker.Resolve(local, marker.Character);

				if (!TryResolvePosition(marker, relationship, localPosition, detectionSquared, now, fog,
					out Vector3 position, out bool exact))
				{
					continue;
				}

				MapMarkerSnapshot snapshot = new MapMarkerSnapshot()
				{
					Source = marker,
					Position = position,
					FacingDegrees = marker.FacingDegrees,
					HasFacing = exact && IsCharacterRelationship(relationship),
					Type = ResolveType(marker, relationship),
					Relationship = relationship,
					Icon = marker.Icon,
					Tint = marker.Tint,
					/* A label is only ever attached to something drawn exactly. A name beside a
					 * coarsened, one-second-old position would hand back precisely the identity
					 * the throttling exists to withhold, and would do it in the most useful
					 * possible form. */
					Label = exact ? marker.Label : null,
					Tooltip = exact ? marker.Label : null,
					Size = marker.IconSize,
					/* Group members are always pinned to the edge when they leave the view,
					 * whatever the prefab said. "Where has my party gone" is the question a
					 * minimap is asked most often, and the character prefab that party members
					 * share cannot know at authoring time that one of its instances will be in
					 * somebody's group. Nothing is leaked: a clamped marker is a direction, and
					 * the group's positions are already on the party frames. */
					ClampToEdge = marker.ClampToEdge ||
						relationship == MapRelationship.Party ||
						relationship == MapRelationship.Guild,
					Priority = marker.Priority,
				};

				results.Add(snapshot);
			}

			if (now >= nextSweepTime)
			{
				nextSweepTime = now + SweepInterval;
				SweepThrottled();
			}
		}

		/// <summary>
		/// Applies a marker's visibility rule and produces the position to draw it at.
		/// </summary>
		/// <param name="marker">The marker being considered.</param>
		/// <param name="relationship">How the local player stands towards it.</param>
		/// <param name="localPosition">Where the local player is.</param>
		/// <param name="detectionSquared">Squared detection radius, to avoid a square root.</param>
		/// <param name="now">The current unscaled time.</param>
		/// <param name="fog">The explored map, for the discovery rule. May be null.</param>
		/// <param name="position">The position to draw the marker at.</param>
		/// <param name="exact">Whether that position is the object's true, current one.</param>
		/// <returns>True when the marker may be drawn.</returns>
		private bool TryResolvePosition(MapMarker marker, MapRelationship relationship, Vector3 localPosition,
			float detectionSquared, double now, FogOfWarMap fog, out Vector3 position, out bool exact)
		{
			position = marker.Position;
			exact = true;

			/* Party and guild override the authored rule, they do not merely satisfy it. A player
			 * character prefab is authored with the strict Detection rule so that a prefab nobody
			 * revisits stays safe; promoting group members here is what stops that strictness
			 * making the party unfindable on their own map. */
			if (relationship == MapRelationship.Self ||
				relationship == MapRelationship.Party ||
				relationship == MapRelationship.Guild)
			{
				return true;
			}

			switch (marker.Visibility)
			{
				case MapMarkerVisibility.Always:
					return true;

				case MapMarkerVisibility.SelfOnly:
					// Self was handled above, so reaching here means this is somebody else's.
					return false;

				case MapMarkerVisibility.PartyOrGuild:
					return false;

				case MapMarkerVisibility.Discovered:
					return fog == null || fog.IsDiscovered(marker.Position);

				case MapMarkerVisibility.Detection:
					return TryResolveThrottled(marker, localPosition, detectionSquared, now, out position, out exact);
			}

			return false;
		}

		/// <summary>
		/// Produces the stale, coarsened position for a marker under the detection rule.
		/// </summary>
		/// <param name="marker">The marker being considered.</param>
		/// <param name="localPosition">Where the local player is.</param>
		/// <param name="detectionSquared">Squared detection radius.</param>
		/// <param name="now">The current unscaled time.</param>
		/// <param name="position">The position to draw at.</param>
		/// <param name="exact">Always false; the position is deliberately not exact.</param>
		/// <returns>True when the marker is currently detectable.</returns>
		/// <remarks>
		/// The range test uses the true position — it has to, since the question is whether the
		/// object is near — but the true position is never handed back. What escapes this method is
		/// a value snapped to a grid and up to <see cref="ThrottleInterval"/> old.
		/// </remarks>
		private bool TryResolveThrottled(MapMarker marker, Vector3 localPosition, float detectionSquared,
			double now, out Vector3 position, out bool exact)
		{
			exact = false;

			Vector3 truePosition = marker.Position;
			float dx = truePosition.x - localPosition.x;
			float dz = truePosition.z - localPosition.z;
			bool detected = ((dx * dx) + (dz * dz)) <= detectionSquared;

			if (!throttled.TryGetValue(marker, out ThrottledMarker entry))
			{
				entry = new ThrottledMarker()
				{
					Position = Quantize(truePosition),
					NextSampleTime = now + ThrottleInterval,
					WasDetected = detected,
				};
				throttled[marker] = entry;
				position = entry.Position;
				return detected;
			}

			if (now >= entry.NextSampleTime)
			{
				entry.Position = Quantize(truePosition);
				entry.NextSampleTime = now + ThrottleInterval;
				entry.WasDetected = detected;
				throttled[marker] = entry;
			}
			else if (entry.WasDetected != detected)
			{
				/* Appearing and disappearing is not throttled, only the position is. Holding the
				 * last known spot for a second after somebody left the radius would leave a ghost
				 * marker on the map, and delaying the first appearance by up to a second would
				 * make the detection radius feel like it varied. Both read as bugs; neither leaks
				 * anything, because the position itself is still the stale one. */
				entry.WasDetected = detected;
				throttled[marker] = entry;
			}

			position = entry.Position;
			return detected;
		}

		/// <summary>
		/// Snaps a position to the coarsening grid.
		/// </summary>
		/// <param name="position">The true position.</param>
		/// <returns>The position rounded to <see cref="PositionQuantum"/> on X and Z.</returns>
		private Vector3 Quantize(Vector3 position)
		{
			if (PositionQuantum <= 0.0f)
			{
				return position;
			}

			return new Vector3(
				Mathf.Round(position.x / PositionQuantum) * PositionQuantum,
				position.y,
				Mathf.Round(position.z / PositionQuantum) * PositionQuantum);
		}

		/// <summary>
		/// Chooses the marker type to draw, letting the live relationship override the authored one.
		/// </summary>
		/// <param name="marker">The marker.</param>
		/// <param name="relationship">How the local player stands towards it.</param>
		/// <returns>The type the UI should style the marker as.</returns>
		/// <remarks>
		/// A character prefab cannot know at authoring time whether it will be an ally or an enemy
		/// of whoever is looking at it — that is a runtime fact about two characters, not a
		/// property of one. Everything that is not a character keeps the type it was authored with.
		/// </remarks>
		private static MapMarkerType ResolveType(MapMarker marker, MapRelationship relationship)
		{
			switch (relationship)
			{
				case MapRelationship.Self: return MapMarkerType.Self;
				case MapRelationship.Party: return MapMarkerType.PartyMember;
				case MapRelationship.Guild: return MapMarkerType.GuildMember;
				case MapRelationship.FriendlyPlayer: return MapMarkerType.FriendlyPlayer;
				case MapRelationship.NeutralPlayer: return MapMarkerType.NeutralPlayer;
				case MapRelationship.HostilePlayer: return MapMarkerType.HostilePlayer;
				default: return marker.Type;
			}
		}

		/// <summary>
		/// Whether a relationship describes a player character, which is drawn with a heading.
		/// </summary>
		/// <param name="relationship">The relationship to test.</param>
		/// <returns>True for anything that is a character.</returns>
		private static bool IsCharacterRelationship(MapRelationship relationship)
		{
			return relationship != MapRelationship.NonPlayer;
		}

		/// <summary>
		/// Drops throttle entries whose markers have been destroyed.
		/// </summary>
		/// <remarks>
		/// The dictionary is keyed by a <c>MonoBehaviour</c>, so an entry for a destroyed marker
		/// keeps its GameObject hierarchy alive. Swept on a timer rather than every refresh
		/// because the comparison against null is an engine call per entry and the leak accrues at
		/// the rate creatures die, not at the rate the map redraws.
		/// </remarks>
		private void SweepThrottled()
		{
			expired.Clear();

			foreach (KeyValuePair<MapMarker, ThrottledMarker> pair in throttled)
			{
				if (pair.Key == null)
				{
					expired.Add(pair.Key);
				}
			}

			for (int i = 0; i < expired.Count; ++i)
			{
				throttled.Remove(expired[i]);
			}

			expired.Clear();
		}

		/// <summary>
		/// Forgets every throttled position.
		/// </summary>
		/// <remarks>
		/// Called on character change and on quit to login. Carrying entries across would let a
		/// new character's map open showing where the previous character's neighbours were.
		/// </remarks>
		public void Reset()
		{
			throttled.Clear();
			expired.Clear();
			nextSweepTime = 0.0;
		}
	}
}
