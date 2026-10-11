using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Turns the map's non-entity content — authored landmarks and the player's own notes — into
	/// the same snapshots the marker filter produces for live objects.
	/// </summary>
	/// <remarks>
	/// Kept apart from <see cref="MapMarkerFilter"/> because the questions are different. The
	/// filter's job is to decide what a player is <i>allowed</i> to know about other entities, and
	/// everything in it is about relationships, ranges and staleness. Nothing here is a secret from
	/// anybody: a landmark is scene data and a note is something the player wrote. Mixing them
	/// would put content with no visibility rules through a class whose entire purpose is
	/// visibility rules.
	/// </remarks>
	public static class MapContent
	{
		/// <summary>Icon size, in UI points, used for authored landmarks.</summary>
		private const float LandmarkSize = 18.0f;

		/// <summary>Icon size, in UI points, used for player notes.</summary>
		private const float NoteSize = 14.0f;

		/// <summary>Draw priority given to landmarks, above ordinary world markers.</summary>
		private const int LandmarkPriority = 5;

		/// <summary>Draw priority given to notes, above landmarks.</summary>
		private const int NotePriority = 10;

		/// <summary>Icon size, in UI points, used for discovered waypoints. The largest thing on the map: it is a button.</summary>
		private const float WaypointSize = 20.0f;

		/// <summary>Draw priority given to waypoints, between landmarks and notes.</summary>
		private const int WaypointPriority = 8;

		/// <summary>
		/// The colours a note's pin may be drawn in.
		/// </summary>
		/// <remarks>
		/// A fixed palette rather than a colour picker. The pins are drawn at fourteen points over
		/// terrain of every possible hue, and a player who picks their own colour will eventually
		/// pick one that is invisible against the ground they put it on. These are chosen to stay
		/// legible against both the dark water and the bright sand a map contains.
		/// </remarks>
		public static readonly Color[] NoteColors =
		{
			new Color(0.98f, 0.85f, 0.35f),
			new Color(0.42f, 0.79f, 0.98f),
			new Color(0.45f, 0.86f, 0.55f),
			new Color(0.98f, 0.48f, 0.45f),
			new Color(0.78f, 0.55f, 0.96f),
			new Color(0.98f, 0.98f, 0.98f),
		};

		/// <summary>
		/// The colour for a note's palette index, wrapping rather than failing.
		/// </summary>
		/// <param name="index">The palette index, from a note that may have been edited by hand.</param>
		/// <returns>A colour from <see cref="NoteColors"/>.</returns>
		public static Color NoteColor(int index)
		{
			if (NoteColors.Length < 1)
			{
				return Color.white;
			}

			/* Wrapped, not clamped, and guarded against a negative. The index comes out of a text
			 * file the player can edit; clamping would silently pile every out-of-range note onto
			 * the last colour, and the modulo of a negative is negative in C#. */
			int wrapped = index % NoteColors.Length;
			if (wrapped < 0)
			{
				wrapped += NoteColors.Length;
			}
			return NoteColors[wrapped];
		}

		/// <summary>
		/// Adds the player's notes to a snapshot list.
		/// </summary>
		/// <param name="results">The list to append to.</param>
		/// <param name="notes">The notes for the current scene.</param>
		/// <param name="forWorldMap">True for the world map, false for the minimap.</param>
		public static void AppendNotes(List<MapMarkerSnapshot> results, IReadOnlyList<MapNote> notes, bool forWorldMap)
		{
			if (results == null || notes == null)
			{
				return;
			}

			for (int i = 0; i < notes.Count; ++i)
			{
				MapNote note = notes[i];
				if (note == null)
				{
					continue;
				}

				if (!forWorldMap && !note.ShowOnMinimap)
				{
					continue;
				}

				results.Add(new MapMarkerSnapshot()
				{
					Position = note.Position,
					Type = MapMarkerType.Note,
					Relationship = MapRelationship.NonPlayer,
					Tint = NoteColor(note.ColorIndex),
					/* The title is drawn on the world map, where there is room for it, and left to
					 * the tooltip on the minimap, where a two-hundred-pixel square would fill with
					 * text the moment a player pinned three things near each other. */
					Label = forWorldMap ? note.Title : null,
					Tooltip = BuildNoteTooltip(note),
					Size = NoteSize,
					Priority = NotePriority,
					NoteID = note.ID,
				});
			}
		}

		/// <summary>
		/// Adds a scene's authored landmarks to a snapshot list, from its map definition only.
		/// </summary>
		/// <param name="results">The list to append to.</param>
		/// <param name="definition">The scene's map definition. May be null.</param>
		/// <param name="fog">The explored map, for the discovery rule. May be null.</param>
		/// <param name="forWorldMap">True for the world map, false for the minimap.</param>
		/// <remarks>Kept for callers that have no scene details; see the overload that takes them.</remarks>
		public static void AppendPointsOfInterest(List<MapMarkerSnapshot> results, WorldMapDefinition definition,
			FogOfWarMap fog, bool forWorldMap)
		{
			AppendPointsOfInterest(results, null, definition, fog, forWorldMap);
		}

		/// <summary>
		/// Adds a scene's points of interest to a snapshot list: the scene details' own list first, then
		/// the map definition's, each landmark once.
		/// </summary>
		/// <param name="results">The list to append to.</param>
		/// <param name="details">The scene's details, holding its harvested points of interest. May be null.</param>
		/// <param name="definition">The scene's map definition, for landmarks baked into it. May be null.</param>
		/// <param name="fog">The explored map, for the discovery rule. May be null.</param>
		/// <param name="forWorldMap">True for the world map, false for the minimap.</param>
		/// <param name="labelTier">
		/// The highest detail tier whose names are drawn; deeper tiers draw their icon without a name.
		/// See <see cref="LabelTierForZoom"/>.
		/// </param>
		/// <remarks>
		/// <para>
		/// The details come first because they are where points of interest live now
		/// (<see cref="WorldSceneDetails.PointsOfInterest"/>): harvested by every cache rebuild whether or
		/// not a map is baked. The definition's list is what a baked scene carried before, and while a bake
		/// is present it holds the same hand-placed landmarks again, so a definition entry with the same
		/// name at the same place (to the metre) as one already added is skipped.
		/// </para>
		/// <para>
		/// Two gates, and a point must pass both. The Cartography tier (<see cref="Cartography.VisibleContentTier"/>,
		/// 0 to <see cref="Cartography.MaximumDetailTier"/>) decides whether the point is known at all; every
		/// point-of-interest tier (0 to 3, <c>PointOfInterestKinds</c>) is inside that range, and with no
		/// Cartography provider every tier is shown. The discovery rule hides a point until the fog has
		/// revealed its chunk; every generated point requires discovery (Jim, 2026-10-10).
		/// </para>
		/// </remarks>
		public static void AppendPointsOfInterest(List<MapMarkerSnapshot> results, WorldSceneDetails details,
			WorldMapDefinition definition, FogOfWarMap fog, bool forWorldMap, int labelTier = int.MaxValue)
		{
			if (results == null)
			{
				return;
			}

			seen.Clear();
			int visibleTier = Cartography.VisibleContentTier;
			if (details != null)
			{
				AppendPointsOfInterest(results, details.PointsOfInterest, fog, forWorldMap, visibleTier, labelTier);
			}
			if (definition != null)
			{
				AppendPointsOfInterest(results, definition.PointsOfInterest, fog, forWorldMap, visibleTier, labelTier);
			}
			seen.Clear();
		}

		/// <summary>
		/// The deepest detail tier whose names the world map draws at a zoom.
		/// </summary>
		/// <param name="zoom">The view's half-extent, in world metres.</param>
		/// <param name="mapHalfExtent">Half the longer side of the scene's map, in world metres.</param>
		/// <returns>0 to 3: 1 when the whole scene is in view, 3 once zoomed in to a quarter of it or closer.</returns>
		/// <remarks>
		/// A generated scene holds dozens to a hundred and more points of interest, and every name at once
		/// is a wall of text. The icons stay, so nothing found is lost; names arrive as the view closes in:
		/// settlements and great sites (tiers 0 and 1) at any zoom, ordinary sites (tier 2) from half the
		/// scene, small features (tier 3) from a quarter.
		/// </remarks>
		public static int LabelTierForZoom(float zoom, float mapHalfExtent)
		{
			if (mapHalfExtent <= 0.0f)
			{
				return 3;
			}

			float fraction = zoom / mapHalfExtent;
			return fraction > 0.5f ? 1 : fraction > 0.25f ? 2 : 3;
		}

		/// <summary>The (name, metre x, metre z) of every landmark added by the current call, for de-duplication.</summary>
		/// <remarks>Static and reused: the maps refresh several times a second on the main thread only.</remarks>
		private static readonly HashSet<(string, int, int)> seen = new HashSet<(string, int, int)>();

		private static void AppendPointsOfInterest(List<MapMarkerSnapshot> results, List<MapPointOfInterestDetails> points,
			FogOfWarMap fog, bool forWorldMap, int visibleTier, int labelTier)
		{
			if (points == null)
			{
				return;
			}

			for (int i = 0; i < points.Count; ++i)
			{
				MapPointOfInterestDetails landmark = points[i];
				if (landmark == null)
				{
					continue;
				}

				if (!seen.Add((landmark.Name ?? string.Empty, Mathf.RoundToInt(landmark.Position.x), Mathf.RoundToInt(landmark.Position.z))))
				{
					continue;
				}

				if (!forWorldMap && !landmark.ShowOnMinimap)
				{
					continue;
				}

				if (landmark.DetailTier > visibleTier)
				{
					continue;
				}

				if (landmark.RequiresDiscovery && fog != null && !fog.IsDiscovered(landmark.Position))
				{
					continue;
				}

				results.Add(new MapMarkerSnapshot()
				{
					Position = landmark.Position,
					Type = landmark.Type,
					Relationship = MapRelationship.NonPlayer,
					Icon = landmark.Icon,
					Tint = Color.white,
					Label = forWorldMap && landmark.DetailTier <= labelTier ? landmark.Name : null,
					Tooltip = string.IsNullOrEmpty(landmark.Description)
						? landmark.Name
						: landmark.Name + "\n" + landmark.Description,
					Size = LandmarkSize,
					Priority = LandmarkPriority,
				});
			}
		}

		/// <summary>
		/// Adds the waypoints the character has discovered in a scene to a snapshot list.
		/// </summary>
		/// <param name="results">The list to append to.</param>
		/// <param name="details">The scene's details, holding its authored waypoints. May be null.</param>
		/// <param name="sceneName">The scene's name, the key the unlock record is kept under.</param>
		/// <param name="waypoints">The character's unlock record. May be null.</param>
		/// <param name="forWorldMap">True for the world map, false for the minimap.</param>
		/// <remarks>
		/// Only unlocked waypoints are appended, and fog plays no part: a waypoint the character
		/// has not discovered is absent from the map even when the ground it stands on has been
		/// explored. Walking past one is not finding it; using it is. That is what keeps a
		/// waypoint a thing to look for rather than a dot that appears on approach.
		/// </remarks>
		public static void AppendWaypoints(List<MapMarkerSnapshot> results, WorldSceneDetails details, string sceneName,
			IWaypointController waypoints, bool forWorldMap)
		{
			if (results == null || details == null || details.Waypoints == null || waypoints == null ||
				string.IsNullOrEmpty(sceneName))
			{
				return;
			}

			foreach (KeyValuePair<int, SceneWaypointDetails> entry in details.Waypoints)
			{
				SceneWaypointDetails waypoint = entry.Value;
				if (waypoint == null || !waypoints.IsUnlocked(sceneName, entry.Key))
				{
					continue;
				}

				results.Add(new MapMarkerSnapshot()
				{
					Position = waypoint.Position,
					Type = MapMarkerType.Waypoint,
					Relationship = MapRelationship.NonPlayer,
					Icon = waypoint.Icon,
					Tint = Color.white,
					Label = forWorldMap ? waypoint.Name : null,
					Tooltip = BuildWaypointTooltip(waypoint, forWorldMap),
					Size = WaypointSize,
					Priority = WaypointPriority,
					IsWaypoint = true,
					WaypointIndex = entry.Key,
				});
			}
		}

		/// <summary>
		/// Builds the hover text for a waypoint.
		/// </summary>
		private static string BuildWaypointTooltip(SceneWaypointDetails waypoint, bool forWorldMap)
		{
			string text = string.IsNullOrEmpty(waypoint.Description)
				? waypoint.Name
				: waypoint.Name + "\n" + waypoint.Description;
			return forWorldMap ? text + "\nClick to select for fast travel" : text;
		}

		/// <summary>
		/// Builds the hover text for a note.
		/// </summary>
		/// <param name="note">The note.</param>
		/// <returns>The tooltip, or null when the note has no text at all.</returns>
		private static string BuildNoteTooltip(MapNote note)
		{
			bool hasTitle = !string.IsNullOrEmpty(note.Title);
			bool hasText = !string.IsNullOrEmpty(note.Text);

			if (hasTitle && hasText)
			{
				return note.Title + "\n" + note.Text;
			}
			if (hasTitle)
			{
				return note.Title;
			}
			return hasText ? note.Text : null;
		}

		/// <summary>
		/// The name to show for where a position is.
		/// </summary>
		/// <param name="definition">The scene's map definition. May be null.</param>
		/// <param name="fog">The explored map, for the discovery rule. May be null.</param>
		/// <param name="fallback">The scene's own name, used when no region matches.</param>
		/// <param name="position">The position to name.</param>
		/// <returns>The most specific region name that applies, or the fallback.</returns>
		/// <remarks>
		/// A region the player has not explored yet reads as the scene's name rather than as its
		/// own. That is the point of marking a region as requiring discovery: the player can see
		/// they are somewhere in the province without being told they have reached the Sunken
		/// Chapel before they have found it.
		/// </remarks>
		public static string ResolveLocationName(WorldMapDefinition definition, FogOfWarMap fog,
			string fallback, Vector3 position)
		{
			MapRegionLabelDetails region = definition != null ? definition.FindRegion(position) : null;
			if (region == null)
			{
				return fallback;
			}

			if (region.RequiresDiscovery && fog != null && !fog.IsDiscovered(region.Position))
			{
				return fallback;
			}

			return region.Name;
		}
	}
}
