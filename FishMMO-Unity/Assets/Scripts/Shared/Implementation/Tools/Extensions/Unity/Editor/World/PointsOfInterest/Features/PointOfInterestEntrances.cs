#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Every way into a site marked on the map, run once a site is built (2026-10-20: "Every dungeon entrance or marker should
	/// be clearly marked as a dungeon entrance. Some are simply generated as background meshes?"; "Caves now have multi
	/// entrances/exits and should be properly marked for both as Points of Interest").
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A dungeon door is always a dungeon entrance.</b> A crypt or a ringed barrow lays an open door over a stair into the
	/// dark (CryptEntrance) and its template had no <see cref="DungeonEntranceFeature"/>, so it stood as scenery: no
	/// interaction, no mark. Any site whose props laid a door with a doorway (StructurePieces.TryDoorwayOf: the dungeon doors
	/// and the crypt's) and built no entrance gets one here, which also marks the site a dungeon on the map. Templates authored
	/// since carry the feature themselves (PointOfInterestTemplateAuthoring.FeaturesFor); this keeps the older ones honest.
	/// </para>
	/// <para>
	/// <b>Each of a cave's mouths is a way in.</b> The site's own entry marks the mouth at its anchor; every other mouth of its
	/// own (the far side of the hill, a dive under a lake, a ledge behind a fall) gets an entry of its own
	/// (<see cref="ScenePointOfInterest.Entrances"/>), saying how it is reached and which caves its passages join. A joined
	/// cave is a site of its own and marks its own mouth.
	/// </para>
	/// <para>Not a registered site builder: a builder's draws are salted with its registration index, and a new one would move
	/// every builder registered after it. Called by the stage after them with its own salt (<see cref="Salt"/>).</para>
	/// </remarks>
	public static class PointOfInterestEntrances
	{
		/// <summary>The salt of the site context this is given (the stage's builders take 1000 and up).</summary>
		public const int Salt = 2000;

		/// <summary>Marks the site's ways in (see remarks).</summary>
		public static void Mark(PointOfInterestSiteContext context)
		{
			if (context == null || context.Record == null || context.Root == null)
			{
				return;
			}
			ScenePointOfInterest point = context.Root.GetComponent<ScenePointOfInterest>();
			if (point == null)
			{
				return;
			}
			EnsureDungeonAtDoor(context);
			MarkCaveMouths(point, context.Points, context.Record);
		}

		/// <summary>Builds a dungeon entrance in the door the site's props laid, where no feature built one.</summary>
		public static void EnsureDungeonAtDoor(PointOfInterestSiteContext context)
		{
			if (context.Root.GetComponentInChildren<DungeonEntrance>(true) != null
				|| !DungeonEntranceFeature.TryLaidDoor(context.Laid, out PointOfInterestSiteContext.PlacedProp door, out _))
			{
				return;
			}
			new DungeonEntranceFeature().Build(context);
			PointOfInterestFeatureKit.Note(context, $"its {door.Prefab.name} is a dungeon's door, so it was made an entrance (re-author the template to carry the feature).");
		}

		/// <summary>
		/// Adds an entry for each of the site's own mouths but the one its anchor marks, and says on the site's own how many
		/// ways in it has. Nothing for a site that is no carved cave or has a single mouth.
		/// </summary>
		public static void MarkCaveMouths(ScenePointOfInterest point, ScenePointsOfInterest points, PointOfInterestRecord record)
		{
			if (!CaveShaper.TryForm(record.Kind, out _))
			{
				return;
			}
			List<CaveMouth> system = SystemMouths(points, record.Id);
			var own = new List<CaveMouth>();
			foreach (CaveMouth mouth in system)
			{
				if (mouth.SiteId == record.Id && IsWayIn(mouth))
				{
					own.Add(mouth);
				}
			}
			string joins = Joins(points, system, record.Id);
			if (own.Count <= 1 && joins == null)
			{
				return;
			}

			// The mouth the site's own entry stands at: the nearest to its anchor.
			int anchor = -1;
			float best = float.MaxValue;
			for (int i = 0; i < own.Count; i++)
			{
				Vector3 d = own[i].Floor - record.Position;
				float distance = d.x * d.x + d.z * d.z;
				if (distance < best)
				{
					best = distance;
					anchor = i;
				}
			}

			string name = point.ResolvedName;
			string kind = PointOfInterestKinds.Info(record.Kind).DisplayName.ToLowerInvariant();
			point.Entrances ??= new List<ScenePointOfInterestEntrance>();
			for (int i = 0; i < own.Count; i++)
			{
				if (i == anchor)
				{
					continue;
				}
				point.Entrances.Add(new ScenePointOfInterestEntrance
				{
					Position = own[i].Floor,
					Type = PointOfInterestKinds.MarkerFor(record.Kind),
					Name = name,
					Description = Line(WayIn(own[i].Kind, kind), own.Count, joins),
				});
			}
			string ways = Line(null, own.Count, joins);
			point.Description = string.IsNullOrEmpty(point.Description) ? ways : point.Description + "\n" + ways;
			EditorUtility.SetDirty(point);
		}

		/// <summary>A mouth a person comes in or goes out by: over the ground, by diving, along a ledge (not an opening in a roof).</summary>
		public static bool IsWayIn(in CaveMouth mouth)
			=> mouth.Kind == CaveMouthKind.Ground || mouth.Kind == CaveMouthKind.Submerged || mouth.Kind == CaveMouthKind.Shelf;

		/// <summary>How a mouth is come to, for its entry's tooltip.</summary>
		public static string WayIn(CaveMouthKind mouth, string kind)
		{
			switch (mouth)
			{
				case CaveMouthKind.Submerged: return $"An underwater way into the {kind}, reached by diving.";
				case CaveMouthKind.Shelf: return $"A way into the {kind} behind a waterfall, along a ledge over its pool.";
				default: return $"Another way into the {kind}.";
			}
		}

		/// <summary>The tooltip line: how the mouth is come to, how many ways in and out, the caves its passages join.</summary>
		public static string Line(string first, int ways, string joins)
		{
			var line = new StringBuilder(first ?? string.Empty);
			if (ways > 1)
			{
				line.Append(line.Length > 0 ? " " : string.Empty).Append($"One of {ways} ways in and out.");
			}
			if (joins != null)
			{
				line.Append(line.Length > 0 ? " " : string.Empty).Append($"Its passages join {joins}.");
			}
			return line.ToString();
		}

		/// <summary>Every mouth of the system the site's cave is part of (its host's when it was joined to one), world space.</summary>
		public static List<CaveMouth> SystemMouths(ScenePointsOfInterest points, int siteId)
		{
			var mouths = new List<CaveMouth>();
			PointOfInterestShape shape = CaveShaper.ShapeFor(points, siteId);
			if (shape != null && CaveShaper.IsLink(shape.Values, out int host))
			{
				shape = CaveShaper.ShapeFor(points, host);
				if (shape != null && CaveShaper.IsLink(shape.Values, out _))
				{
					shape = null;
				}
			}
			if (shape == null)
			{
				return mouths;
			}
			foreach (CaveMouth m in CaveSystem.MouthsFromValues(shape.Values))
			{
				CaveMouth world = m;
				world.Floor += shape.Position;
				world.Landing += shape.Position;
				mouths.Add(world);
			}
			return mouths;
		}

		/// <summary>The names of the other sites whose mouths open into the same system, in the system's order; null for none.</summary>
		private static string Joins(ScenePointsOfInterest points, List<CaveMouth> system, int siteId)
		{
			var names = new List<string>();
			var seen = new HashSet<int> { siteId };
			foreach (CaveMouth mouth in system)
			{
				if (!seen.Add(mouth.SiteId) || points?.Points == null)
				{
					continue;
				}
				PointOfInterestRecord other = points.Points.Find(r => r != null && r.Id == mouth.SiteId);
				if (other != null && !string.IsNullOrWhiteSpace(other.Name))
				{
					names.Add(other.Name);
				}
			}
			if (names.Count == 0)
			{
				return null;
			}
			return names.Count == 1 ? names[0] : string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1];
		}
	}
}
#endif
