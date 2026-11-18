using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>The kinds of authored place a character can be rescued to.</summary>
	public enum RescuePointKind : byte
	{
		/// <summary>A respawn position, keyed by its authored name.</summary>
		Respawn = 0,
		/// <summary>An initial spawn position, keyed by its spawner name.</summary>
		Spawn = 1,
		/// <summary>A waypoint, by its authored index or its player-facing name.</summary>
		Waypoint = 2,
	}

	/// <summary>One place a rescue can send a character to.</summary>
	public readonly struct RescuePoint
	{
		/// <summary>What kind of place it is.</summary>
		public readonly RescuePointKind Kind;

		/// <summary>The name an operator types to pick it.</summary>
		public readonly string Name;

		/// <summary>Where the character is placed.</summary>
		public readonly Vector3 Position;

		/// <summary>The facing to place them with, when the place has one.</summary>
		public readonly Quaternion Rotation;

		/// <summary>
		/// False for a waypoint, which is harvested as a marker and carries no facing. The caller
		/// keeps the character's own rotation rather than snapping them to face north.
		/// </summary>
		public readonly bool HasRotation;

		public RescuePoint(RescuePointKind kind, string name, Vector3 position, Quaternion rotation, bool hasRotation)
		{
			Kind = kind;
			Name = name;
			Position = position;
			Rotation = rotation;
			HasRotation = hasRotation;
		}
	}

	/// <summary>
	/// Reads the places a character can be rescued to out of a scene's cached details.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Pure: no scene, no network, no character. Both the game master rescue and the player's own
	/// unstuck resolve through here, so "nearest respawn point" means one thing in both.
	/// </para>
	/// <para>
	/// <b>The scene's own details only.</b> Every point comes from the details of the scene the
	/// caller passes, which is the scene the character is physically in. A point from another scene
	/// written into this one is valid coordinates in a place the character is not loaded into, and
	/// reads to the player as falling through the world — the thing a rescue exists to fix.
	/// </para>
	/// </remarks>
	public static class RescuePoints
	{
		/// <summary>The words an operator types for each kind, in <see cref="RescuePointKind"/> order.</summary>
		public static readonly string[] KindWords = { "respawn", "spawn", "waypoint" };

		/// <summary>Parses a kind word, case-insensitively.</summary>
		public static bool TryParseKind(string word, out RescuePointKind kind)
		{
			kind = RescuePointKind.Respawn;
			if (string.IsNullOrWhiteSpace(word))
			{
				return false;
			}

			string trimmed = word.Trim();
			for (int i = 0; i < KindWords.Length; ++i)
			{
				if (string.Equals(trimmed, KindWords[i], StringComparison.OrdinalIgnoreCase))
				{
					kind = (RescuePointKind)i;
					return true;
				}
			}
			return false;
		}

		/// <summary>Every point of a kind in the scene, in authored order. Never null.</summary>
		public static List<RescuePoint> List(WorldSceneDetails details, RescuePointKind kind)
		{
			List<RescuePoint> points = new List<RescuePoint>();
			if (details == null)
			{
				return points;
			}

			switch (kind)
			{
				case RescuePointKind.Respawn:
					if (details.RespawnPositions != null)
					{
						foreach (KeyValuePair<string, CharacterRespawnPositionDetails> pair in details.RespawnPositions)
						{
							if (pair.Value != null)
							{
								points.Add(new RescuePoint(kind, pair.Key, pair.Value.Position, pair.Value.Rotation, true));
							}
						}
					}
					break;

				case RescuePointKind.Spawn:
					if (details.InitialSpawnPositions != null)
					{
						foreach (KeyValuePair<string, CharacterInitialSpawnPositionDetails> pair in details.InitialSpawnPositions)
						{
							if (pair.Value != null)
							{
								string name = string.IsNullOrWhiteSpace(pair.Value.SpawnerName) ? pair.Key : pair.Value.SpawnerName;
								points.Add(new RescuePoint(kind, name, pair.Value.Position, pair.Value.Rotation, true));
							}
						}
					}
					break;

				case RescuePointKind.Waypoint:
					if (details.Waypoints != null)
					{
						foreach (KeyValuePair<int, SceneWaypointDetails> pair in details.Waypoints)
						{
							if (pair.Value != null)
							{
								string name = string.IsNullOrWhiteSpace(pair.Value.Name)
									? pair.Value.Index.ToString(CultureInfo.InvariantCulture)
									: pair.Value.Name;
								points.Add(new RescuePoint(kind, name, pair.Value.Position, Quaternion.identity, false));
							}
						}
					}
					break;
			}
			return points;
		}

		/// <summary>
		/// Finds a point of a kind: the named one, or the nearest to <paramref name="from"/> when no
		/// name is given.
		/// </summary>
		/// <remarks>
		/// A name matches exactly, ignoring case. A waypoint also matches by its authored index, so
		/// an operator reading the index off the map can type it. There is deliberately no prefix or
		/// "closest spelling" match: a rescue that silently picks a different place than the one
		/// typed puts a player somewhere the operator did not intend and cannot see.
		/// </remarks>
		/// <param name="details">The details of the scene the character is in.</param>
		/// <param name="kind">Which kind of point.</param>
		/// <param name="name">The point's name, or null/blank for the nearest.</param>
		/// <param name="from">The character's position, for the nearest.</param>
		/// <param name="point">The point found.</param>
		public static bool TryFind(WorldSceneDetails details, RescuePointKind kind, string name, Vector3 from, out RescuePoint point)
		{
			point = default;
			List<RescuePoint> points = List(details, kind);
			if (points.Count < 1)
			{
				return false;
			}

			if (string.IsNullOrWhiteSpace(name))
			{
				float nearest = float.MaxValue;
				bool found = false;
				for (int i = 0; i < points.Count; ++i)
				{
					float distance = (points[i].Position - from).sqrMagnitude;
					if (distance < nearest)
					{
						nearest = distance;
						point = points[i];
						found = true;
					}
				}
				return found;
			}

			string wanted = name.Trim();
			for (int i = 0; i < points.Count; ++i)
			{
				if (string.Equals(points[i].Name, wanted, StringComparison.OrdinalIgnoreCase))
				{
					point = points[i];
					return true;
				}
			}

			if (kind == RescuePointKind.Waypoint &&
				int.TryParse(wanted, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) &&
				details.Waypoints != null &&
				details.Waypoints.TryGetValue(index, out SceneWaypointDetails waypoint) &&
				waypoint != null)
			{
				string waypointName = string.IsNullOrWhiteSpace(waypoint.Name)
					? waypoint.Index.ToString(CultureInfo.InvariantCulture)
					: waypoint.Name;
				point = new RescuePoint(kind, waypointName, waypoint.Position, Quaternion.identity, false);
				return true;
			}

			return false;
		}
	}
}
