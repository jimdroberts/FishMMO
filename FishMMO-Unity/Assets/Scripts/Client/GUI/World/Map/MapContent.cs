using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

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
		/// Adds a scene's authored landmarks to a snapshot list.
		/// </summary>
		/// <param name="results">The list to append to.</param>
		/// <param name="definition">The scene's map definition. May be null.</param>
		/// <param name="fog">The explored map, for the discovery rule. May be null.</param>
		/// <param name="forWorldMap">True for the world map, false for the minimap.</param>
		public static void AppendPointsOfInterest(List<MapMarkerSnapshot> results, WorldMapDefinition definition,
			FogOfWarMap fog, bool forWorldMap)
		{
			if (results == null || definition == null || definition.PointsOfInterest == null)
			{
				return;
			}

			int visibleTier = Cartography.VisibleContentTier;

			for (int i = 0; i < definition.PointsOfInterest.Count; ++i)
			{
				MapPointOfInterestDetails landmark = definition.PointsOfInterest[i];
				if (landmark == null)
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
					Label = forWorldMap ? landmark.Name : null,
					Tooltip = string.IsNullOrEmpty(landmark.Description)
						? landmark.Name
						: landmark.Name + "\n" + landmark.Description,
					Size = LandmarkSize,
					Priority = LandmarkPriority,
				});
			}
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
