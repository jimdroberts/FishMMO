using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// The burn mosaic's globals (2026-10-25, phase I2t, decisions 75/82/84; <see cref="BurnMosaic"/>, FishBurn.hlsl): which biome
	/// burns how (one regime per flora row, live from the templates), the scene's thermal year at the shared clock, its prevailing
	/// wind and seed, the night for the blue fire, and the nearest brimstone vents. Client only, presentation only.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Rows are built with the flora map (FloraPaletteMap.Build: the same rows and grid the grass's looks and the glow modes read),
	/// a Brimstone row only on a breathable world (<see cref="BiomeBurnRegimes.Drawn"/>). <c>_FishBurnInfo.x</c> is 0 when no biome
	/// on the scene burns, and every shader then draws exactly as before.
	/// </para>
	/// <para>
	/// Per frame (<see cref="Frame"/>, from GlowSky after the sky): the clock from the world seconds the sky is drawn at, so every
	/// player in the scene reads the same year, seconds and live years; the flicker on the motion clock wrapped at whole cycles.
	/// The grass compute is handed the same values by name (<see cref="BindCompute"/>: a compute never sees the globals).
	/// </para>
	/// </remarks>
	public static class BurnMosaicTable
	{
		/// <summary>The rows the GPU array holds: every flora row.</summary>
		public const int Rows = FloraPaletteMap.MaxBiomes + 1;

		/// <summary>The vents handed to the GPU (FISH_BURN_VENTS), the nearest within <see cref="VentReach"/>.</summary>
		public const int Vents = 16;

		/// <summary>How far round the camera brimstone vents are looked for, m, and how far it may move before they are looked for again.</summary>
		public const float VentReach = 400f, VentRefind = 40f;

		private static readonly int InfoId = Shader.PropertyToID("_FishBurnInfo");
		private static readonly int RowsId = Shader.PropertyToID("_FishBurnRows");
		private static readonly int ClockId = Shader.PropertyToID("_FishBurnClock");
		private static readonly int WindId = Shader.PropertyToID("_FishBurnWind");
		private static readonly int MotionId = Shader.PropertyToID("_FishBurnMotion");
		private static readonly int VentsId = Shader.PropertyToID("_FishBurnVents");

		private static readonly Vector4[] rows = new Vector4[Rows / 4];
		private static readonly Vector4[] vents = new Vector4[Vents];
		private static readonly BiomeBurnRegime[] regimes = new BiomeBurnRegime[Rows];
		private static readonly List<GeothermalSite> found = new List<GeothermalSite>();
		private static Vector4 info, clock, wind, motion;
		private static bool anyVeld, anyBrimstone, published, climateKnown;
		private static BiomeBurnRegime veldRegime;
		private static WorldSceneSettings settings;
		private static uint seed;
		private static double yearSeconds, coldest01;
		private static Vector2 ventCentre = new Vector2(float.MaxValue, float.MaxValue);
		private static int ventCount;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			System.Array.Clear(rows, 0, rows.Length);
			System.Array.Clear(regimes, 0, regimes.Length);
			info = clock = wind = motion = Vector4.zero;
			anyVeld = anyBrimstone = published = climateKnown = false;
			settings = null;
			ventCount = 0;
			ventCentre = new Vector2(float.MaxValue, float.MaxValue);
		}

		/// <summary>Whether a biome on the scene burns (the table is on).</summary>
		public static bool On => anyVeld || anyBrimstone;

		/// <summary>The regime drawn for a flora row (None past the rows or before a build).</summary>
		public static BiomeBurnRegime RegimeOfRow(int row) => row >= 0 && row < Rows ? regimes[row] : BiomeBurnRegime.None;

		/// <summary>
		/// The rows from the flora map's row templates (null for none), on the scene's world: each its template's regime as the scene
		/// draws it (<see cref="BiomeBurnRegimes.Drawn"/>), and the scene's year, seed and season.
		/// </summary>
		public static void Build(IReadOnlyList<BiomeTemplate> rowBiomes, WorldSceneSettings scene)
		{
			System.Array.Clear(rows, 0, rows.Length);
			System.Array.Clear(regimes, 0, regimes.Length);
			anyVeld = anyBrimstone = false;
			veldRegime = BiomeBurnRegime.None;
			settings = scene;
			ventCount = 0;
			ventCentre = new Vector2(float.MaxValue, float.MaxValue);
			bool breathable = scene == null || BiomeAir.IsBreathable(scene.WorldConditions);
			int count = Mathf.Min(rowBiomes != null ? rowBiomes.Count : 0, Rows);
			for (int r = 0; r < count; r++)
			{
				BiomeTemplate biome = rowBiomes[r];
				BiomeBurnRegime regime = BiomeBurnRegimes.Drawn(biome != null ? biome.BurnRegime : BiomeBurnRegime.None, breathable);
				regimes[r] = regime;
				Vector4 row = rows[r >> 2];
				row[r & 3] = (float)regime;
				rows[r >> 2] = row;
				if (BiomeBurnRegimes.IsVeld(regime))
				{
					anyVeld = true;
					// The live-years test is per regime; Annual's window holds Occasional's, so the wider one is asked.
					veldRegime = veldRegime == BiomeBurnRegime.Annual ? veldRegime : regime;
				}
				anyBrimstone |= regime == BiomeBurnRegime.Brimstone;
			}
			seed = BurnMosaic.SeedOf(scene != null ? scene.gameObject.scene.name : null);
			climateKnown = false;
			if (scene != null && On)
			{
				SolarSystemProfile system = SolarSystemProfile.Active;
				WorldBody body = SceneTime.BodyOf(scene);
				PlanetClimateTable table = system != null && body != null ? PlanetClimateTable.For(system, body) : null;
				if (table != null && table.YearHours > 0.0)
				{
					ScenePlacementClimate placement = scene.PlacementClimate;
					float continentality = placement != null ? placement.CentreContinentality : 0.5f;
					yearSeconds = table.YearHours * 3600.0;
					coldest01 = table.ColdestPhase(scene.ClimateLatitude, continentality) / (double)PlanetClimateTable.Phases;
					climateKnown = true;
				}
			}
			info = On ? new Vector4(1f, 0f, 0f, 0f) : Vector4.zero;
		}

		/// <summary>Hands the rows to the shaders (off unless built with a burning biome and <paramref name="on"/>).</summary>
		public static void Publish(bool on)
		{
			published = on && On;
			Shader.SetGlobalVectorArray(RowsId, rows);
			Shader.SetGlobalVectorArray(VentsId, vents);
			Shader.SetGlobalVector(InfoId, published ? info : Vector4.zero);
		}

		/// <summary>Takes the table down.</summary>
		public static void Release()
		{
			anyVeld = anyBrimstone = published = false;
			settings = null;
			info = Vector4.zero;
			ventCount = 0;
			Shader.SetGlobalVector(InfoId, Vector4.zero);
		}

		/// <summary>
		/// The frame's clock, wind and night (GlowSky.Publish, after the sky is placed): <paramref name="worldSeconds"/> the world clock
		/// the sky is drawn at, <paramref name="sky"/> its prevailing way and sun height.
		/// </summary>
		public static void Frame(in SceneGlowSky sky, double worldSeconds)
		{
			if (!published || settings == null)
			{
				return;
			}
			float wx = sky.WayX, wz = sky.WayZ;
			BurnMosaic.Wind(ref wx, ref wz);
			float night = anyBrimstone ? BurnMosaic.NightOf(sky.SunHeight) : 0f;
			wind = new Vector4(wx, wz, seed, night);
			motion = new Vector4(WorldMotion.Wrapped(BurnMosaic.FlickerWrapSeconds), 0f, 0f, 0f);
			int live = 0;
			if (anyVeld && climateKnown)
			{
				BurnMosaic.BurnClock c = BurnMosaic.ClockAt(worldSeconds, yearSeconds, coldest01);
				live = BurnMosaic.LiveYears(in c, veldRegime);
				clock = new Vector4(c.Year & 0xFFFF, c.Seconds, c.YearSeconds, live);
			}
			if (anyBrimstone && night > 0f)
			{
				FindVents();
			}
			info = new Vector4(1f, live != 0 ? 1f : 0f, anyBrimstone && night > 0f ? 1f : 0f, anyBrimstone ? ventCount : 0);
			Shader.SetGlobalVector(InfoId, info);
			Shader.SetGlobalVector(ClockId, clock);
			Shader.SetGlobalVector(WindId, wind);
			Shader.SetGlobalVector(MotionId, motion);
			Shader.SetGlobalVectorArray(VentsId, vents);
		}

		/// <summary>Hands the frame's values to a compute kernel by name (the blade grass's generate kernel).</summary>
		public static void BindCompute(CommandBuffer cmd, ComputeShader compute)
		{
			cmd.SetComputeVectorParam(compute, InfoId, published ? info : Vector4.zero);
			cmd.SetComputeVectorArrayParam(compute, RowsId, rows);
			cmd.SetComputeVectorParam(compute, ClockId, clock);
			cmd.SetComputeVectorParam(compute, WindId, wind);
		}

		/// <summary>
		/// The fumaroles and sulphur pools nearest the camera, whose lips burn blue: looked for again only when the camera has moved
		/// <see cref="VentRefind"/> (the sites are the scene's, the same for everyone; only which are handed over follows the viewer).
		/// </summary>
		private static void FindVents()
		{
			Camera camera = Camera.main;
			if (camera == null)
			{
				return;
			}
			Vector3 eye = camera.transform.position;
			var centre = new Vector2(eye.x, eye.z);
			if ((centre - ventCentre).sqrMagnitude < VentRefind * VentRefind)
			{
				return;
			}
			ventCentre = centre;
			GeothermalVents.Near(settings, centre, VentReach, found);
			found.Sort((a, b) => (a.Position - centre).sqrMagnitude.CompareTo((b.Position - centre).sqrMagnitude));
			ventCount = 0;
			System.Array.Clear(vents, 0, vents.Length);
			foreach (GeothermalSite site in found)
			{
				if (ventCount >= Vents)
				{
					break;
				}
				if (site.Kind != GeothermalKind.Fumarole && site.Kind != GeothermalKind.SulphurPool)
				{
					continue;
				}
				// A fumarole's lip 2–4 m, a sulphur pond's rim 3–12 m (its basin).
				float radius = site.Kind == GeothermalKind.SulphurPool ? 3f + 9f * site.Size : 2f + 2f * site.Size;
				vents[ventCount++] = new Vector4(site.Position.x, site.Position.y, radius, 1f);
			}
		}
	}
}
