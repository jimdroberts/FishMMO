#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Flooded paddy fields (2026-10-18, phase I2i, decision 27): a crop field whose surface is <see cref="BiomeFieldSurface.Paddy"/>
	/// is dug level, banked round with a low earth bund and flooded a hand's depth deep, its crop (rice) standing in the water.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The ground.</b> The water stands <see cref="BiomeFieldSurfaces.PaddyFreeboard"/> under the lowest ground round the field
	/// out to <see cref="BiomeFieldSurfaces.PaddySlackMetres"/> (<see cref="BiomeFieldSurfaces.PaddyLevel"/>), so wherever the
	/// water's sheet reaches past its floor it is under ground; the floor is dug (or filled) level under it, the bund raised along
	/// the field's edge (<see cref="BiomeFieldSurfaces.PaddyGround"/>). A field that would need more than
	/// <see cref="BiomeFieldSurfaces.PaddyMaxCut"/> dug (or <see cref="BiomeFieldSurfaces.PaddyMaxFill"/> filled) is sown flat
	/// instead. The carve is recorded and put back before anything reads the ground again (PointOfInterestFieldCarve, as ridge and
	/// furrow's), so every repaint and Regenerate POIs lays the same paddy from the same ground.
	/// </para>
	/// <para>
	/// <b>The water</b> is a lake of the scene's hydrology (as a grotto's pool is, CaveShaper.WritePool), its mask the
	/// <see cref="SceneWater.LakeMaskCell"/> cells whose middles lie on the floor, written in the place stage after the scene's water
	/// is saved; drawn, waded and reflected like any lake. Its ids are <see cref="LakeIdBase"/> with the site and the field. Every
	/// paddy's lake is taken out of the hydrology before the sites are placed again (<see cref="ClearWater"/>), and the cut's own
	/// water never holds them (SceneWater.FromAsset leaves them out), so a moved or dropped field leaves no water behind and the
	/// water's settling never measures a paddy.
	/// </para>
	/// </remarks>
	public static class PointOfInterestPaddies
	{
		/// <summary>Lake ids paddies take: this with the site's low 20 bits and the field's index (moats and grottos take higher bases).</summary>
		public const int LakeIdBase = 1 << 28;

		/// <summary>How far apart the ground under and round a field is read for its level, metres.</summary>
		public const float SampleMetres = 2f;

		/// <summary>The lake id of a site's field.</summary>
		public static int LakeId(int siteId, int field) => LakeIdBase | ((siteId & 0xFFFFF) << 4) | (field & 0xF);

		/// <summary>True for a paddy's lake id.</summary>
		public static bool IsPaddy(int id) => id >= LakeIdBase && id < CaveShaper.PoolIdBase;

		// ── Pure ──────────────────────────────────────────────────────

		/// <summary>
		/// How far in from the edge of a field (centre, yaw degrees, size along its own x and z) a world point lies, metres (pure):
		/// positive inside (to the nearer side), negative outside (to the rectangle).
		/// </summary>
		public static float Inside(Vector2 centre, float yaw, Vector2 size, float x, float z)
		{
			float r = yaw * Mathf.Deg2Rad;
			var axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			var p = new Vector2(x, z) - centre;
			float u = Mathf.Abs(Vector2.Dot(p, axisX)) - 0.5f * size.x, v = Mathf.Abs(Vector2.Dot(p, axisZ)) - 0.5f * size.y;
			if (u <= 0f && v <= 0f)
			{
				return -Mathf.Max(u, v);
			}
			return -new Vector2(Mathf.Max(0f, u), Mathf.Max(0f, v)).magnitude;
		}

		/// <summary>The points a field's own ground is read at for its level (pure): a lattice <see cref="SampleMetres"/> apart across it, edges included.</summary>
		public static List<Vector2> Interior(Vector2 centre, float yaw, Vector2 size)
		{
			var points = new List<Vector2>();
			float r = yaw * Mathf.Deg2Rad;
			var axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			int nx = Mathf.Max(1, Mathf.CeilToInt(size.x / SampleMetres)), nz = Mathf.Max(1, Mathf.CeilToInt(size.y / SampleMetres));
			for (int i = 0; i <= nx; i++)
			{
				for (int j = 0; j <= nz; j++)
				{
					points.Add(centre + axisX * ((i / (float)nx - 0.5f) * size.x) + axisZ * ((j / (float)nz - 0.5f) * size.y));
				}
			}
			return points;
		}

		/// <summary>
		/// The points the ground round a field is read at for its level (pure): its edge (the bund's line) and rings round it every
		/// <see cref="SampleMetres"/> out to <see cref="BiomeFieldSurfaces.PaddySlackMetres"/>, each ring walked
		/// <see cref="SampleMetres"/> apart round its rectangle.
		/// </summary>
		public static List<Vector2> Ring(Vector2 centre, float yaw, Vector2 size)
		{
			var points = new List<Vector2>();
			float r = yaw * Mathf.Deg2Rad;
			var axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			int rings = Mathf.CeilToInt(BiomeFieldSurfaces.PaddySlackMetres / SampleMetres);
			for (int k = 0; k <= rings; k++)
			{
				float d = k * SampleMetres;
				float hx = 0.5f * size.x + d, hz = 0.5f * size.y + d;
				int nx = Mathf.Max(1, Mathf.CeilToInt(2f * hx / SampleMetres)), nz = Mathf.Max(1, Mathf.CeilToInt(2f * hz / SampleMetres));
				for (int i = 0; i <= nx; i++)
				{
					float u = -hx + 2f * hx * i / nx;
					points.Add(centre + axisX * u + axisZ * hz);
					points.Add(centre + axisX * u - axisZ * hz);
				}
				for (int j = 1; j < nz; j++)
				{
					float v = -hz + 2f * hz * j / nz;
					points.Add(centre + axisX * hx + axisZ * v);
					points.Add(centre - axisX * hx + axisZ * v);
				}
			}
			return points;
		}

		/// <summary>A paddy's water level on the ground as it lies (pure; <see cref="BiomeFieldSurfaces.PaddyLevel"/>); false when the field should be sown flat.</summary>
		public static bool Level(Vector2 centre, float yaw, Vector2 size, Func<float, float, float> ground, out float water)
		{
			water = float.NaN;
			if (ground == null)
			{
				return false;
			}
			List<Vector2> inner = Interior(centre, yaw, size), ring = Ring(centre, yaw, size);
			return BiomeFieldSurfaces.PaddyLevel(inner.ConvertAll(p => ground(p.x, p.y)), ring.ConvertAll(p => ground(p.x, p.y)), out water);
		}

		/// <summary>How far a paddy moves the ground at (x, z) where it stands at <paramref name="ground"/>, metres, signed (pure).</summary>
		public static float Offset(Vector2 centre, float yaw, Vector2 size, float water, float x, float z, float ground)
			=> BiomeFieldSurfaces.PaddyGround(Inside(centre, yaw, size, x, z), ground, water) - ground;

		/// <summary>The world rectangle a paddy's carve can touch: the field's bounds and its bund's outer face, with a sample's room.</summary>
		public static Rect CarveArea(Vector2 centre, float yaw, Vector2 size)
		{
			float reach = 0.5f * size.magnitude + BiomeFieldSurfaces.PaddyOuterSlope + 1f;
			return Rect.MinMaxRect(centre.x - reach, centre.y - reach, centre.x + reach, centre.y + reach);
		}

		/// <summary>
		/// A paddy's water as a lake of the scene's hydrology (pure): every <see cref="SceneWater.LakeMaskCell"/> cell, on a lattice
		/// fixed to the scene's metres, whose middle lies on the field's floor (<see cref="BiomeFieldSurfaces.PaddyFloorFrom"/> in
		/// from its edge); null when none does (a field too narrow for a cell).
		/// </summary>
		public static SceneHydrology.Lake Lake(int id, Vector2 centre, float yaw, Vector2 size, float water)
		{
			float cell = SceneWater.LakeMaskCell;
			float reach = 0.5f * size.magnitude + cell;
			float x0 = Mathf.Floor((centre.x - reach) / cell) * cell, z0 = Mathf.Floor((centre.y - reach) / cell) * cell;
			int width = Mathf.CeilToInt((centre.x + reach - x0) / cell), height = Mathf.CeilToInt((centre.y + reach - z0) / cell);
			var bits = new byte[(width * height + 7) / 8];
			int covered = 0;
			Vector2 seed = default;
			float best = float.MaxValue;
			for (int z = 0; z < height; z++)
			{
				for (int x = 0; x < width; x++)
				{
					float east = x0 + (x + 0.5f) * cell, north = z0 + (z + 0.5f) * cell;
					if (Inside(centre, yaw, size, east, north) < BiomeFieldSurfaces.PaddyFloorFrom)
					{
						continue;
					}
					int bit = z * width + x;
					bits[bit >> 3] |= (byte)(1 << (bit & 7));
					covered++;
					float d = (new Vector2(east, north) - centre).sqrMagnitude;
					if (d < best)
					{
						best = d;
						seed = new Vector2(east, north);
					}
				}
			}
			if (covered == 0)
			{
				return null;
			}
			return new SceneHydrology.Lake
			{
				Id = id,
				PlanetLake = -1,
				Level = water,
				SpillLevel = water,
				Seeds = new[] { seed },
				Bounds = new Rect(x0, z0, width * cell, height * cell),
				Mask = bits,
				MaskOrigin = new Vector2(x0, z0),
				MaskCell = cell,
				MaskWidth = width,
				MaskHeight = height,
			};
		}

		// ── The scene ─────────────────────────────────────────────────

		/// <summary>The scene's hydrology asset beside its terrain, or null (a harness, a LOCAL copy, a scene with no water saved).</summary>
		private static SceneHydrology Hydrology(string terrainFolder, string sceneName)
		{
			if (string.IsNullOrEmpty(terrainFolder) || string.IsNullOrEmpty(sceneName))
			{
				return null;
			}
			return AssetDatabase.LoadAssetAtPath<SceneHydrology>(SceneGenerator.HydrologyPath(terrainFolder, sceneName));
		}

		/// <summary>Takes every paddy's lake out of the scene's hydrology (before the sites are placed again); returns how many went.</summary>
		public static int ClearWater(string terrainFolder, string sceneName)
		{
			SceneHydrology hydrology = Hydrology(terrainFolder, sceneName);
			if (hydrology == null || hydrology.Lakes == null)
			{
				return 0;
			}
			int removed = hydrology.Lakes.RemoveAll(l => l != null && IsPaddy(l.Id));
			if (removed > 0)
			{
				EditorUtility.SetDirty(hydrology);
				AssetDatabase.SaveAssetIfDirty(hydrology);
			}
			return removed;
		}

		/// <summary>Writes a paddy's lake into the scene's hydrology, replacing one of the same id; false when there is no hydrology to hold it.</summary>
		public static bool WriteWater(PointOfInterestSiteContext context, SceneHydrology.Lake lake)
		{
			SceneHydrology hydrology = lake != null && context?.Request != null ? Hydrology(context.TerrainFolder, context.Request.SceneName) : null;
			if (hydrology == null)
			{
				return false;
			}
			hydrology.Lakes.RemoveAll(l => l != null && l.Id == lake.Id);
			hydrology.Lakes.Add(lake);
			EditorUtility.SetDirty(hydrology);
			AssetDatabase.SaveAssetIfDirty(hydrology);
			return true;
		}
	}
}
#endif
