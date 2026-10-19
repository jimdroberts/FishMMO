#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The kinds of field added on 2026-10-18 (phase I2j-b, decisions 23, 29, 31, 38) beyond what PointOfInterestFields lays itself:
	/// orchards' trees, corn hills' tilled earth, a garden's raised beds (cross paths and wattle edging), a marcita's flooded crown
	/// rills, a baulatura's piantata along its ditches, and the old terraces round a hamlet.
	/// </summary>
	/// <remarks>
	/// Every choice here is a hash of the site's seed with the field (<see cref="PointOfInterestPlanner.Hash"/>), never a draw from the
	/// site's sequence, so the fields, fences and every later site lie as they did; a biome that names none of these lays nothing here.
	/// Carves go through PointOfInterestFieldCarve (recorded, restored before the ground is read again); water is written as lakes in the
	/// paddies' id range (PointOfInterestFieldTerraces.LakeId), cleared by the place stage with the paddies'.
	/// </remarks>
	public static class PointOfInterestFieldKinds
	{
		/// <summary>A raised bed's wattle edging, metres high.</summary>
		public const float EdgingHeight = 0.3f;
		/// <summary>A marcita's crown rill: its half width and its depth under its banks, metres.</summary>
		public const float RillHalfWidth = 0.4f, RillDepth = 0.3f;
		/// <summary>The half width a rill's water is masked to, metres: its one-metre cells reach over the trench, their sheet under its banks.</summary>
		public const float RillWaterHalf = 1f;
		/// <summary>How far out from a baulatura's ditch its piantata stands, metres.</summary>
		public const float PiantataBankMetres = 0.6f;
		/// <summary>The attempts a flight of old terraces gets to find its slope.</summary>
		public const int FlightAttempts = 16;

		// ── Pure ──────────────────────────────────────────────────────

		/// <summary>
		/// True when <paramref name="wet"/> holds anywhere within <paramref name="reach"/> metres of a point (pure): searched outward on
		/// rings eight metres apart, each walked about eight metres a step, stopping at the first. True at once for no reach.
		/// </summary>
		public static bool NearWater(Func<float, float, bool> wet, Vector2 p, float reach)
		{
			if (wet == null || !(reach > 0f))
			{
				return true;
			}
			const float step = 8f;
			for (float r = 0f; r <= reach + 1e-3f; r += step)
			{
				int n = r <= 0f ? 1 : Mathf.Max(6, Mathf.CeilToInt(2f * Mathf.PI * r / step));
				for (int k = 0; k < n; k++)
				{
					float a = 2f * Mathf.PI * k / n;
					if (wet(p.x + r * Mathf.Sin(a), p.y + r * Mathf.Cos(a)))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>A field's own axes: x along its Size.x, z along its Size.y.</summary>
		public static void Axes(float yaw, out Vector2 axisX, out Vector2 axisZ)
		{
			float r = yaw * Mathf.Deg2Rad;
			axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
		}

		/// <summary>
		/// An orchard's rows on flat ground (pure): lines along the field's long side, <paramref name="spacing"/> apart across it
		/// (<see cref="BiomeOrchards.Rows"/>), each ending half a spacing inside the headland.
		/// </summary>
		public static List<List<Vector2>> OrchardRows(Vector2 centre, float yaw, Vector2 size, float spacing, float headland)
		{
			var lines = new List<List<Vector2>>();
			Axes(yaw, out Vector2 axisX, out Vector2 axisZ);
			bool alongX = size.x >= size.y;
			Vector2 along = alongX ? axisX : axisZ, across = alongX ? axisZ : axisX;
			float length = Mathf.Max(size.x, size.y) - 2f * headland - spacing, width = Mathf.Min(size.x, size.y) - 2f * headland;
			if (length < 0f || width <= 0f)
			{
				return lines;
			}
			foreach (float offset in BiomeOrchards.Rows(width, spacing))
			{
				Vector2 middle = centre + across * offset;
				lines.Add(new List<Vector2> { middle - along * (0.5f * length), middle + along * (0.5f * length) });
			}
			return lines;
		}

		/// <summary>
		/// A garden's raised beds (pure): each bed's two long sides as lines (the wattle edging's), beds across the sown band as its
		/// ridges are (<see cref="BiomeFieldSurfaces.Furrows"/>), broken along by the cross paths (<see cref="BiomeFieldSurfaces.CrossPaths"/>);
		/// a bed under two metres long left out.
		/// </summary>
		public static List<List<Vector2>> BedEdges(Vector2 centre, float yaw, Vector2 size, float headland)
		{
			var lines = new List<List<Vector2>>();
			const BiomeFieldSurface beds = BiomeFieldSurface.RaisedBeds;
			Axes(yaw, out Vector2 axisX, out Vector2 axisZ);
			bool alongX = size.x >= size.y;
			Vector2 along = alongX ? axisX : axisZ, across = alongX ? axisZ : axisX;
			float band = Mathf.Max(0f, Mathf.Min(size.x, size.y) - 2f * headland);
			float length = Mathf.Max(size.x, size.y) - 2f * headland;
			int count = BiomeFieldSurfaces.RidgeCount(beds, band);
			if (count <= 0 || length <= 0f)
			{
				return lines;
			}
			float period = band / count;
			BiomeFieldSurfaces.Rows rows = BiomeFieldSurfaces.Nominal(beds);
			float half = 0.5f * period * rows.Ridge / rows.Period;
			var stretches = new List<(float From, float To)>();
			float start = 0f;
			foreach (float cross in BiomeFieldSurfaces.CrossPaths(beds, length))
			{
				stretches.Add((start, cross - 0.5f * BiomeFieldSurfaces.BedCrossPath));
				start = cross + 0.5f * BiomeFieldSurfaces.BedCrossPath;
			}
			stretches.Add((start, length));
			for (int k = 0; k < count; k++)
			{
				float middle = (k + 0.5f) * period - 0.5f * band;
				foreach ((float from, float to) in stretches)
				{
					if (to - from < 2f)
					{
						continue;
					}
					foreach (float side in new[] { middle - half, middle + half })
					{
						Vector2 a = centre + across * side + along * (from - 0.5f * length), b = centre + across * side + along * (to - 0.5f * length);
						lines.Add(new List<Vector2> { a, b });
					}
				}
			}
			return lines;
		}

		/// <summary>
		/// A ridged field's lines along its length at offsets across its sown band (pure): each from one end of the full-height ridges to
		/// the other (<see cref="BiomeFieldSurfaces.RidgeEndMetres"/> in from the headland), at <paramref name="offsets"/> metres from the band's edge.
		/// </summary>
		public static List<List<Vector2>> AlongLines(Vector2 centre, float yaw, Vector2 size, float headland, IEnumerable<float> offsets)
		{
			var lines = new List<List<Vector2>>();
			Axes(yaw, out Vector2 axisX, out Vector2 axisZ);
			bool alongX = size.x >= size.y;
			Vector2 along = alongX ? axisX : axisZ, across = alongX ? axisZ : axisX;
			float band = Mathf.Max(0f, Mathf.Min(size.x, size.y) - 2f * headland);
			float reach = 0.5f * Mathf.Max(size.x, size.y) - headland - BiomeFieldSurfaces.RidgeEndMetres;
			if (reach <= 1f)
			{
				return lines;
			}
			foreach (float offset in offsets)
			{
				Vector2 middle = centre + across * (offset - 0.5f * band);
				lines.Add(new List<Vector2> { middle - along * reach, middle + along * reach });
			}
			return lines;
		}

		/// <summary>The crowns of a ridged field's ridges, metres across its sown band from its edge (pure): each ridge's middle.</summary>
		public static List<float> Crowns(BiomeFieldSurface surface, float band)
		{
			var crowns = new List<float>();
			int count = BiomeFieldSurfaces.RidgeCount(surface, band);
			for (int k = 0; k < count; k++)
			{
				crowns.Add((k + 0.5f) * band / count);
			}
			return crowns;
		}

		/// <summary>The yaw that sets a field's long side (its x) along the contour where the ground falls along <paramref name="gradient"/> (pure), degrees.</summary>
		public static float ContourYaw(Vector2 gradient)
		{
			if (gradient.sqrMagnitude < 1e-12f)
			{
				return 0f;
			}
			Vector2 g = gradient.normalized;
			// axisZ = (sin r, cos r) along the fall; axisX, the long side, then lies along the contour.
			return Mathf.Atan2(g.x, g.y) * Mathf.Rad2Deg;
		}

		// ── The scene ─────────────────────────────────────────────────

		/// <summary>
		/// What a field's surface lays beyond its crop and furrows (2026-10-18, I2j-b): corn hills' tilled earth, a garden's cross paths and
		/// wattle edging, a marcita's flooded crown rills, a baulatura's piantata. Returns the props laid; <paramref name="carved"/> gains the
		/// samples a rill's carve moved.
		/// </summary>
		public static int LaySurface(PointOfInterestSiteContext context, BiomeTemplate biome, Vector2 centre, float yaw, Vector2 size, BiomeFieldSurface surface,
			int siteSeed, int field, float headland, ref int carved)
		{
			var own = (centre, yaw, size);
			int seed = unchecked((int)PointOfInterestPlanner.Hash(siteSeed, 0x46444B, field, 0));
			float band = Mathf.Max(0f, Mathf.Min(size.x, size.y) - 2f * headland);
			switch (surface)
			{
				case BiomeFieldSurface.CornHills:
					// The hills stand in tilled earth: the field's ground bare to its fence, as a ploughed plot's.
					AddYard(context, centre, yaw, size, 0.5f, 0.9f);
					return 0;
				case BiomeFieldSurface.RaisedBeds:
				{
					Axes(yaw, out Vector2 axisX, out Vector2 axisZ);
					bool alongX = size.x >= size.y;
					Vector2 along = alongX ? axisX : axisZ, across = alongX ? axisZ : axisX;
					float length = Mathf.Max(size.x, size.y) - 2f * headland;
					foreach (float cross in BiomeFieldSurfaces.CrossPaths(surface, length))
					{
						Vector2 middle = centre + along * (cross - 0.5f * length);
						AddStrip(context, middle - across * (0.5f * band), middle + across * (0.5f * band), 0.5f * BiomeFieldSurfaces.BedCrossPath, 0.85f);
					}
					BiomeLinearStyle edging = EdgingStyle(biome);
					int laid = 0, line = 0;
					foreach (List<Vector2> edge in BedEdges(centre, yaw, size, headland))
					{
						laid += PointOfInterestLinearFeatures.LayAlong(context, biome, edging, edge, unchecked(seed + 7907 * ++line), own);
					}
					return laid;
				}
				case BiomeFieldSurface.Marcita:
					carved += LayRills(context, centre, yaw, size, headland, field, band);
					return 0;
				case BiomeFieldSurface.Baulatura:
				{
					if (!PiantataStyle(biome, out BiomeLinearStyle row))
					{
						return 0;
					}
					List<float> ditches = BiomeFieldSurfaces.Furrows(surface, band, out float width);
					var banks = new List<float>();
					foreach (float d in ditches)
					{
						banks.Add(d + 0.5f * width + PiantataBankMetres);
					}
					int laid = 0, line = 0;
					foreach (List<Vector2> bank in AlongLines(centre, yaw, size, headland, banks))
					{
						laid += PointOfInterestLinearFeatures.LayAlong(context, biome, row, bank, unchecked(seed + 7907 * ++line), own);
					}
					return laid;
				}
				default:
					return 0;
			}
		}

		/// <summary>An orchard's trees (2026-10-18, I2j-b): in rows along the field (or along the middles of its benches when terraced), its tree at its spacing. Returns the trees laid.</summary>
		public static int LayOrchard(PointOfInterestSiteContext context, BiomeTemplate biome, BiomeOrchard orchard, Vector2 centre, float yaw, Vector2 size,
			PointOfInterestFieldTerraces.Plan terraces, int siteSeed, int field, float headland)
		{
			if (orchard.Tree == null)
			{
				return 0;
			}
			float spacing = BiomeOrchards.SpacingOf(orchard);
			var style = new BiomeLinearStyle(BiomeLinearForm.Filare, BiomeLinearPlace.FieldEdges)
			{
				Species = new List<GameObject> { orchard.Tree },
				Spacing = spacing,
			};
			var own = (centre, yaw, size);
			int seed = unchecked((int)PointOfInterestPlanner.Hash(siteSeed, 0x4F5243, field, 0));
			List<List<Vector2>> rows;
			if (terraces != null && terraces.Terraced)
			{
				rows = new List<List<Vector2>>();
				foreach ((int _, List<Vector2> line) in PointOfInterestFieldTerraces.TreadLines(terraces, headland + 0.5f * spacing))
				{
					rows.Add(line);
				}
			}
			else
			{
				rows = OrchardRows(centre, yaw, size, spacing, headland);
			}
			int laid = 0;
			for (int k = 0; k < rows.Count; k++)
			{
				laid += PointOfInterestLinearFeatures.LayAlong(context, biome, style, rows[k], unchecked(seed + 7901 * (k + 1)), own);
			}
			return laid;
		}

		/// <summary>
		/// The old terraces round a site (2026-10-18, I2j-b): up to its biome's flights, each on a slope about it its biome's band allows,
		/// long side along the contour, clear of every field, way, water and keep-out, carved as a terraced field's, nothing sown, its
		/// walls tumbled; each claims its ground (under a claim of its own, so no channel heads for it). Returns the flights laid.
		/// </summary>
		public static int LayAbandonedTerraces(PointOfInterestSiteContext context, PathExclusion ways, ref int carved, ref int pieces)
		{
			PointOfInterestRecord record = context?.Record;
			if (record == null || context.GroundAt == null)
			{
				return 0;
			}
			var site = new Vector2(record.Position.x, record.Position.z);
			BiomeTemplate biome = context.BiomeAt != null ? context.BiomeAt(site.x, site.y) : null;
			BiomeAbandonedTerraces old = BiomeTemplateArt.AbandonedTerraces(biome);
			if (!old.Any)
			{
				return 0;
			}
			int claimant = ~record.Id;
			int flights = 0;
			var laid = new List<(Vector2 Centre, float Yaw, Vector2 Size)>();
			for (int flight = 0; flight < old.Flights; flight++)
			{
				for (int attempt = 0; attempt < FlightAttempts; attempt++)
				{
					float Unit(int k) => PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(record.SiteSeed, 0x414254 + k, flight, attempt));
					float angle = Unit(0) * 360f;
					float edge = PointOfInterestOutline.ReachToward(record.Outline, record.Radius, angle);
					float distance = edge * 1.1f + 20f + Unit(1) * Mathf.Max(60f, record.Radius * 2f);
					float a = angle * Mathf.Deg2Rad;
					Vector2 centre = site + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * distance;
					var size = new Vector2(Mathf.Lerp(30f, 70f, Unit(2)), Mathf.Lerp(20f, 40f, Unit(3)));
					var gradient = new Vector2(context.GroundAt(centre.x + 2f, centre.y) - context.GroundAt(centre.x - 2f, centre.y),
						context.GroundAt(centre.x, centre.y + 2f) - context.GroundAt(centre.x, centre.y - 2f));
					if (float.IsNaN(gradient.x) || float.IsNaN(gradient.y))
					{
						continue;
					}
					float yaw = ContourYaw(gradient);
					if (context.FieldClaims != null && !context.FieldClaims.Clear(claimant, centre, yaw, size) || !FlightFits(context, centre, yaw, size, old, ways)
						|| laid.Exists(o => PointOfInterestLayouts.Overlaps(centre, yaw, size + Vector2.one * PointOfInterestFieldClaims.Gap, o.Centre, o.Yaw, o.Size + Vector2.one * PointOfInterestFieldClaims.Gap)))
					{
						continue;
					}
					PointOfInterestFieldTerraces.Plan plan = PointOfInterestFieldTerraces.Make(centre, yaw, size, old.Riser, false, context.GroundAt);
					if (!plan.Terraced)
					{
						continue;
					}
					carved += PointOfInterestFieldTerraces.Carve(context, plan);
					pieces += PointOfInterestFieldTerraces.FaceSteps(context, biome, plan, unchecked((int)PointOfInterestPlanner.Hash(record.SiteSeed, 0x414255, flight, 0)), old.Breaks);
					context.FieldClaims?.Add(claimant, centre, yaw, size);
					laid.Add((centre, yaw, size));
					flights++;
					break;
				}
			}
			return flights;
		}

		/// <summary>True when a flight's ground is dry, open, clear of the ways and keep-outs, and its mean slope in its biome's band.</summary>
		private static bool FlightFits(PointOfInterestSiteContext context, Vector2 centre, float yaw, Vector2 size, BiomeAbandonedTerraces old, PathExclusion ways)
		{
			float slopes = 0f;
			int count = 0;
			foreach (Vector2 p in PointOfInterestPaddies.Interior(centre, yaw, size * 0.999f))
			{
				float ground = context.GroundAt(p.x, p.y);
				if (float.IsNaN(ground) || float.IsInfinity(ground) || context.SkyAt != null && context.SkyAt(p.x, p.y))
				{
					return false;
				}
				if (context.Water != null && context.Water.KindAt(p.x, p.y) != WaterKind.None || context.IsWet(p.x, p.y, 0.4f))
				{
					return false;
				}
				if (context.Points != null && context.Points.IsKeptOut(p.x, p.y) || ways != null && ways.Trees(p.x, p.y))
				{
					return false;
				}
				float slope = context.SlopeAt != null ? context.SlopeAt(p.x, p.y) : 0f;
				if (slope > old.SlopeMax + 10f)
				{
					return false;
				}
				slopes += slope;
				count++;
			}
			float mean = count > 0 ? slopes / count : 0f;
			return count > 0 && mean >= old.SlopeMin && mean <= old.SlopeMax;
		}

		/// <summary>
		/// A marcita's crown rills (2026-10-18, I2j-b): one along each bed's crown, its standing pounds (PointOfInterestLinearFeatures.Pounds),
		/// the rill carved as a channel's trench (recorded for the restore) and each pound's water written as a lake. Returns the samples carved.
		/// </summary>
		private static int LayRills(PointOfInterestSiteContext context, Vector2 centre, float yaw, Vector2 size, float headland, int field, float band)
		{
			List<List<Vector2>> rills = AlongLines(centre, yaw, size, headland, Crowns(BiomeFieldSurface.Marcita, band));
			int carved = 0;
			for (int r = 0; r < rills.Count && r < 16; r++)
			{
				List<PointOfInterestLinearFeatures.ChannelStation> stations = PointOfInterestLinearFeatures.Pounds(rills[r], context.GroundAt, false, 0f, 0f);
				if (stations.Count < 2)
				{
					continue;
				}
				const float gridCell = 6f;
				float reach = RillHalfWidth + PointOfInterestLinearFeatures.BankMetres + PointOfInterestLinearFeatures.BankFallMetres;
				Dictionary<long, List<int>> grid = PointOfInterestLinearFeatures.CutGrid(stations, gridCell, reach);
				Vector2 a = rills[r][0], b = rills[r][rills[r].Count - 1];
				Rect area = Rect.MinMaxRect(Mathf.Min(a.x, b.x) - reach - 1f, Mathf.Min(a.y, b.y) - reach - 1f, Mathf.Max(a.x, b.x) + reach + 1f, Mathf.Max(a.y, b.y) + reach + 1f);
				carved += PointOfInterestFieldCarve.CarveTo(context, area, (x, z, g) =>
				{
					float d = PointOfInterestLinearFeatures.NearestCut(stations, grid, gridCell, new Vector2(x, z), out int k);
					if (k < 0 || d > reach)
					{
						return 0f;
					}
					return PointOfInterestLinearFeatures.ChannelGround(d, g, stations[k].Level, RillHalfWidth, RillDepth) - g;
				});
				int pounds = Mathf.Min(16, stations[stations.Count - 1].Pound + 1);
				for (int p = 0; p < pounds; p++)
				{
					SceneHydrology.Lake lake = PointOfInterestLinearFeatures.PoundLake(
						PointOfInterestFieldTerraces.LakeId(PointOfInterestFieldTerraces.RillLakeKind, context.Record.Id, field, (r << 4) | p), stations, p, RillWaterHalf);
					if (lake != null && !PointOfInterestPaddies.WriteWater(context, lake))
					{
						PointOfInterestFeatureKit.Note(context, "a marcita's rills have no hydrology to hold their water (a harness, or a LOCAL copy)");
						return carved;
					}
				}
				AddStrip(context, a, b, 0.6f * RillHalfWidth, 0.85f);
			}
			return carved;
		}

		/// <summary>The edging a biome's raised beds are laid in: its own wattle fence style where it names one (its material), else a plain wattle hurdle, knee high.</summary>
		private static BiomeLinearStyle EdgingStyle(BiomeTemplate biome)
		{
			var edging = new BiomeLinearStyle(BiomeLinearForm.Wattle, BiomeLinearPlace.FieldEdges);
			IReadOnlyList<BiomeLinearStyle> styles = biome != null ? BiomeTemplateArt.LinearFeatures(biome) : null;
			if (styles != null)
			{
				foreach (BiomeLinearStyle style in styles)
				{
					if (style.Form == BiomeLinearForm.Wattle && style.OwnMaterial)
					{
						edging.OwnMaterial = true;
						edging.Material = style.Material;
						edging.Tint = style.Tint;
						break;
					}
				}
			}
			edging.Height = EdgingHeight;
			return edging;
		}

		/// <summary>A biome's piantata (its first tree-row style trained as a piantata, wherever it is laid), or none.</summary>
		private static bool PiantataStyle(BiomeTemplate biome, out BiomeLinearStyle row)
		{
			row = default;
			IReadOnlyList<BiomeLinearStyle> styles = biome != null ? BiomeTemplateArt.LinearFeatures(biome) : null;
			if (styles == null)
			{
				return false;
			}
			foreach (BiomeLinearStyle style in styles)
			{
				if (style.Form == BiomeLinearForm.Piantata && style.Species != null && style.Species.Count > 0)
				{
					row = style;
					row.Where = BiomeLinearPlace.FieldEdges;
					return true;
				}
			}
			return false;
		}

		/// <summary>A field's whole ground as bare yard earth (a ploughed plot's), <paramref name="inset"/> metres inside its edge.</summary>
		private static void AddYard(PointOfInterestSiteContext context, Vector2 centre, float yaw, Vector2 size, float inset, float wear)
		{
			Axes(yaw, out Vector2 axisX, out Vector2 axisZ);
			bool alongX = size.x >= size.y;
			Vector2 axis = alongX ? axisX : axisZ;
			float shortest = Mathf.Min(size.x, size.y), longest = Mathf.Max(size.x, size.y);
			AddStrip(context, centre - axis * (0.5f * longest - inset), centre + axis * (0.5f * longest - inset), 0.5f * shortest - inset, wear);
		}

		/// <summary>One squared strip of bare earth (a yard way the path overlay draws), between two points, <paramref name="half"/> metres either side.</summary>
		private static void AddStrip(PointOfInterestSiteContext context, Vector2 a, Vector2 b, float half, float wear)
		{
			if (context.Points == null || context.GroundAt == null || !(half > 0f))
			{
				return;
			}
			int id = 0;
			foreach (ScenePath existing in context.Points.Paths)
			{
				id = Math.Max(id, existing != null ? existing.Id : 0);
			}
			var flags = (byte)(ScenePathPointFlags.InSite | ScenePathPointFlags.Squared);
			context.Points.Paths.Add(new ScenePath
			{
				Id = id + 1,
				Class = ScenePathClass.Yard,
				Points = new[] { new Vector3(a.x, context.GroundAt(a.x, a.y), a.y), new Vector3(b.x, context.GroundAt(b.x, b.y), b.y) },
				HalfWidth = new[] { half, half },
				Wear = new[] { wear, wear },
				Surface = new[] { (byte)ScenePathSurface.Earth, (byte)ScenePathSurface.Earth },
				Flags = new[] { flags, flags },
			});
		}
	}
}
#endif
