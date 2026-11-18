using UnityEngine;
using UnityEngine.Experimental.Rendering;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The tower lattice and the formation noise, each drawn once over one period into a tile that wraps, so the cloud
	/// field reads them with one fetch instead of working them out at every sample.
	/// </summary>
	/// <remarks>
	/// Both are fixed patterns in their own drifting frames (WeatherDriver.Tower, the mesoscale noise), repeating
	/// every <see cref="WeatherDriver.TowerPeriodTiles"/> and <see cref="WeatherDriver.MesoscalePeriodTiles"/> cells
	/// and smooth over kilometres; only the world's seed changes them. Worked out per sample they were most of the
	/// cloud field's cost — the tower's nine-cell search 5.8 ms of fair weather's march at 2560x1440, the noise's two
	/// octaves 3.6 (ScenePerfProbe, 2026-10-07). Drawn by the clouds' CloudFieldTile pass from the same functions, so
	/// the tiles are the same patterns; the weather's contrast on the noise is still applied per sample.
	/// </remarks>
	public sealed class CloudFieldTiles
	{
		/// <summary>Off: the field works both out per sample, as it did (the probe's A/B).</summary>
		public static bool Enabled = true;
		/// <summary>Texels a cell: the tower tile is 140 m a texel, the noise's 375 m, against features kilometres wide.</summary>
		public const int TexelsPerCell = 64;

		private static readonly int TowerTileId = Shader.PropertyToID("_FishCloudTowerTile");
		private static readonly int MesoTileId = Shader.PropertyToID("_FishCloudMesoTile");
		private static readonly int TilesId = Shader.PropertyToID("_FishCloudFieldTiles");
		private static readonly int BuildId = Shader.PropertyToID("_FishCloudTileBuild");

		private RenderTexture tower;
		private RenderTexture meso;
		private uint drawnSeed;
		private bool drawn;

		/// <summary>
		/// Draws the tiles when there are none yet or the world's seed has changed; otherwise only keeps them bound.
		/// </summary>
		/// <param name="seeded">Whether SkySystem has handed the lattice this world's seeds and periods: drawn before,
		/// the tiles would be another world's pattern. (Not read back with Shader.GetGlobalInt: Unity keeps an int
		/// global as a float, and a seed comes back rounded.)</param>
		public void Update(Material cloudMaterial, bool enabled, bool seeded)
		{
			int pass = cloudMaterial != null ? cloudMaterial.FindPass("CloudFieldTile") : -1;
			if (!Enabled || !enabled || pass < 0)
			{
				Clear();
				return;
			}
			uint seed = WeatherDriver.WorldSeed;
			if (drawn && seed == drawnSeed && tower != null && tower.IsCreated() && meso != null && meso.IsCreated())
			{
				return;
			}
			if (!seeded)
			{
				Clear();
				return;
			}
			tower = tower != null ? tower : Create(WeatherDriver.TowerPeriodTiles * TexelsPerCell, "Cloud Tower Tile");
			meso = meso != null ? meso : Create(WeatherDriver.MesoscalePeriodTiles * TexelsPerCell, "Cloud Formation Tile");
			cloudMaterial.SetVector(BuildId, new Vector4(0f, tower.width, 0f, 0f));
			Graphics.Blit(null, tower, cloudMaterial, pass);
			cloudMaterial.SetVector(BuildId, new Vector4(1f, meso.width, 0f, 0f));
			Graphics.Blit(null, meso, cloudMaterial, pass);
			Shader.SetGlobalTexture(TowerTileId, tower);
			Shader.SetGlobalTexture(MesoTileId, meso);
			Shader.SetGlobalVector(TilesId, new Vector4(1f, 0f, 0f, 0f));
			drawnSeed = seed;
			drawn = true;
		}

		/// <summary>No tiles: the field works the patterns out per sample.</summary>
		public void Clear()
		{
			if (drawn)
			{
				Shader.SetGlobalVector(TilesId, Vector4.zero);
				drawn = false;
			}
		}

		public void Dispose()
		{
			Clear();
			Release(ref tower);
			Release(ref meso);
		}

		private static RenderTexture Create(int size, string name)
		{
			// A float a texel: the noise runs a little past 0..1 and is multiplied by the weather's contrast after.
			GraphicsFormat format = SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat, GraphicsFormatUsage.Render)
				? GraphicsFormat.R32_SFloat
				: GraphicsFormat.R16_SFloat;
			var texture = new RenderTexture(size, size, 0, format)
			{
				name = name,
				wrapMode = TextureWrapMode.Repeat,
				filterMode = FilterMode.Bilinear,
				useMipMap = false,
				hideFlags = HideFlags.DontSave,
			};
			texture.Create();
			return texture;
		}

		private static void Release(ref RenderTexture texture)
		{
			if (texture == null)
			{
				return;
			}
			texture.Release();
			if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture);
			texture = null;
		}
	}
}
