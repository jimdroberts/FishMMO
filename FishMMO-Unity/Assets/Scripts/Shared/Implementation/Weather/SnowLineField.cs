using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// How much warmer or colder each place of a scene cut from the globe is at sea level than the middle of its weather's
	/// area, K, on a coarse grid over the scene and the ground out to its horizon: what moves the snow line from place to
	/// place by climate alone (<see cref="SnowLineShape.LiftAt"/>), height being the shaders' own part.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Static.</b> Latitude, how far inland and the regional wobble do not change with the weather or the season, so the
	/// grid is worked out once per scene, from the same field the cut chose the biomes from
	/// (<see cref="WorldSceneSettings.SampleClimateAt(Vector3, float, out float, float, float)"/> at y = 0), and measured
	/// from the same middle the snow line itself is read at (<see cref="GroundCover.MiddleKelvin"/>'s point). The cover map's
	/// own bands did this for the ground under the map, so the line stepped where the map ended.
	/// </para>
	/// <para>A scene not cut from the globe has none: its climate is one place's, and its line one height.</para>
	/// </remarks>
	public sealed class SnowLineField
	{
		/// <summary>Grid points per side.</summary>
		public const int Resolution = 64;

		/// <summary>How far the grid reaches, in scene widths: the scene and a scene's width of backdrop beyond each edge.</summary>
		public const float ReachScenes = 3f;

		/// <summary>World x/z of the grid's south-west corner, and its size, metres.</summary>
		public readonly Rect Area;

		/// <summary>K at each grid point, row-major from the south-west.</summary>
		public readonly float[] Kelvin;

		/// <summary>The coldest point's offset, K: how far below the middle's the line comes anywhere.</summary>
		public readonly float Coldest;

		private SnowLineField(Rect area, float[] kelvin)
		{
			Area = area;
			Kelvin = kelvin;
			float coldest = 0f;
			for (int i = 0; i < kelvin.Length; i++)
			{
				coldest = Mathf.Min(coldest, kelvin[i]);
			}
			Coldest = coldest;
		}

		/// <summary>
		/// The field of a scene, measured from <paramref name="middle"/> (world x/z), or null for a scene not cut from the
		/// globe. A few thousand climate samples: once per scene.
		/// </summary>
		public static SnowLineField For(WorldSceneSettings settings, Vector2 middle)
		{
			ScenePlacementClimate placement = settings != null ? settings.PlacementClimate : null;
			if (placement == null || !placement.Generated || settings.AtlasEntry == null)
			{
				return null;
			}
			Vector2 sceneMetres = settings.AtlasEntry.SizeKm * 1000f;
			Vector2 size = new Vector2(Mathf.Max(1f, sceneMetres.x), Mathf.Max(1f, sceneMetres.y)) * ReachScenes;
			var area = new Rect(-size * 0.5f, size);
			float reference = SeaLevelKelvin(settings, middle.x, middle.y);
			if (reference <= 0f)
			{
				return null;
			}
			var kelvin = new float[Resolution * Resolution];
			for (int j = 0; j < Resolution; j++)
			{
				float z = area.yMin + (j + 0.5f) / Resolution * area.height;
				for (int i = 0; i < Resolution; i++)
				{
					float x = area.xMin + (i + 0.5f) / Resolution * area.width;
					float here = SeaLevelKelvin(settings, x, z);
					kelvin[j * Resolution + i] = here > 0f ? here - reference : 0f;
				}
			}
			return new SnowLineField(area, kelvin);
		}

		/// <summary>The offset at a world x/z, K: bilinear between grid points (as the GPU filters it), the edge held past the grid.</summary>
		public float KelvinAt(float x, float z)
		{
			float gx = Mathf.Clamp((x - Area.xMin) / Area.width * Resolution - 0.5f, 0f, Resolution - 1);
			float gz = Mathf.Clamp((z - Area.yMin) / Area.height * Resolution - 0.5f, 0f, Resolution - 1);
			int x0 = Mathf.Min((int)gx, Resolution - 2), z0 = Mathf.Min((int)gz, Resolution - 2);
			float fx = gx - x0, fz = gz - z0;
			int i = z0 * Resolution + x0;
			return Mathf.Lerp(Mathf.Lerp(Kelvin[i], Kelvin[i + 1], fx), Mathf.Lerp(Kelvin[i + Resolution], Kelvin[i + Resolution + 1], fx), fz);
		}

		/// <summary>The year's mean at sea level at a place, K, with nothing added: 0 where the climate has no kelvin to give.</summary>
		private static float SeaLevelKelvin(WorldSceneSettings settings, float x, float z)
		{
			return settings.SampleClimateAt(new Vector3(x, 0f, z), 0.5f, out _, 0f, 0f).Kelvin;
		}
	}
}
