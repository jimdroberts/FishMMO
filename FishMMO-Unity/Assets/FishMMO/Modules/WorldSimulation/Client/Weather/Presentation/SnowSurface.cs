using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Where the snow's drawn surface stands, on the CPU: the terrain under a point and how far the terrain shader lifts it
	/// under snow (FishSnowLift / FishSnowLiftMetres). What a camera is kept out of: the terrain collider is the bare ground,
	/// so a camera that only met the collider went down through the white surface and looked up at it from inside.
	/// </summary>
	/// <remarks>
	/// Installed as the player camera's <see cref="KCCCamera.SnowSurfaceAt"/> (the camera is core and knows nothing of the
	/// weather), and read by the world test bed's camera.
	/// </remarks>
	public static class SnowSurface
	{
		private static readonly int SnowDepthProperty = Shader.PropertyToID("_FishSnowDepth");
		private static readonly List<Terrain> terrains = new List<Terrain>();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Install()
		{
			KCCCamera.SnowSurfaceAt = TryAt;
		}

		/// <summary>
		/// The bare ground under <paramref name="point"/> and the snow's drawn surface over it. False where there is no
		/// terrain under the point, a hole in it, or no snow lifting it (this tier does not lift the ground, or there is no
		/// snow): nothing to keep out of.
		/// </summary>
		public static bool TryAt(Vector3 point, out float ground, out float surface)
		{
			ground = float.NegativeInfinity;
			surface = float.NegativeInfinity;
			if (!LiftPossible())
			{
				return false;
			}
			Terrain under = TerrainUnder(point);
			if (under == null)
			{
				return false;
			}
			TerrainData data = under.terrainData;
			Vector3 origin = under.GetPosition();
			Vector3 size = data.size;
			float u = (point.x - origin.x) / size.x;
			float v = (point.z - origin.z) / size.z;
			if (IsHole(data, u, v))
			{
				return false;
			}
			ground = origin.y + under.SampleHeight(point);
			float lift = LiftAt(under, new Vector3(point.x, ground, point.z));
			if (lift <= 0f)
			{
				return false;
			}
			surface = ground + lift;
			return true;
		}

		/// <summary>
		/// How high the drawn snow stands over the terrain at a point on it, metres, as the terrain shader lifts it: the tile's
		/// blanket where the cover is white, the deep snow past it (the weather's and the season's pack), none on ground too
		/// steep to hold it, and none unless this quality tier lifts the ground and there is snow in the scene. Read from the
		/// cover map's own copy of what it publishes. Footprints and the sky's shelter are left out: both only lower the snow,
		/// so a camera errs above the surface, never under it.
		/// </summary>
		public static float LiftAt(Terrain terrain, Vector3 ground)
		{
			Material material = terrain != null ? terrain.materialTemplate : null;
			float blanket = material != null && material.HasFloat(SnowDepthProperty) ? material.GetFloat(SnowDepthProperty) : 0f;
			if (blanket <= 0f || !LiftPossible())
			{
				return 0f;
			}
			Vector4 everywhere = Shader.GetGlobalVector(WeatherShaderGlobals.Cover);
			WeatherCoverMap map = WeatherPresentation.Instance != null ? WeatherPresentation.Instance.CoverMap : null;
			float cover = map != null ? map.Sample(ground, new WeatherCover { Snow = everywhere.x }).Snow : everywhere.x;
			float deep = map != null ? map.SampleDeepSnow(ground) : 0f;
			float lift = Mathf.Clamp01(cover) * blanket + deep;
			if (lift <= 0f)
			{
				return 0f;
			}
			// FishCoverFacing at a slope bias of 0.5: all of it on the level, none past 60°.
			Vector3 size = terrain.terrainData.size;
			Vector3 origin = terrain.GetPosition();
			float up = terrain.terrainData.GetInterpolatedNormal((ground.x - origin.x) / size.x, (ground.z - origin.z) / size.z).y;
			float facing = Mathf.Clamp01((up - 0.5f) / 0.5f);
			return lift * facing * facing;
		}

		/// <summary>The terrain shader's own gate (FishSnowLift): this tier lifts the ground, and there is snow in the scene.</summary>
		private static bool LiftPossible()
		{
			if (Shader.GetGlobalVector(WeatherShaderGlobals.Tier).x <= 0f)
			{
				return false;
			}
			return Shader.GetGlobalVector(WeatherShaderGlobals.Cover).x > 0f || Shader.GetGlobalVector(WeatherCoverMap.SnowLineId).z > 0.5f;
		}

		private static Terrain TerrainUnder(Vector3 point)
		{
			Terrain.GetActiveTerrains(terrains);
			for (int i = 0; i < terrains.Count; i++)
			{
				Terrain terrain = terrains[i];
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				Vector3 origin = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;
				if (point.x >= origin.x && point.x <= origin.x + size.x && point.z >= origin.z && point.z <= origin.z + size.z)
				{
					return terrain;
				}
			}
			return null;
		}

		/// <summary>Whether the hole map has a hole at a point given as 0..1 across the tile (the hole texel it falls in).</summary>
		private static bool IsHole(TerrainData data, float u, float v)
		{
			int resolution = data.holesResolution;
			if (resolution <= 0)
			{
				return false;
			}
			int x = Mathf.Clamp(Mathf.FloorToInt(u * resolution), 0, resolution - 1);
			int z = Mathf.Clamp(Mathf.FloorToInt(v * resolution), 0, resolution - 1);
			return data.IsHole(x, z);
		}
	}
}
