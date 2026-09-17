using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Renders the Dashboard's Solar System and World Atlas pages to PNGs, with the example
	/// system and every world scene laid out on Home, so the designers can be looked at headlessly.
	/// </summary>
	/// <remarks>
	/// Headless: <c>-executeMethod FishMMO.RenderScratch.WorldDesignRender.Render</c> under xvfb,
	/// without <c>-quit</c>. The example system is created for real (as the page's button does);
	/// the scene placement and the extra sky objects are made in memory only and never saved.
	/// Output goes to <c>FISHMMO_WORLD_RENDER_DIR</c>, defaulting to <c>PanelRenders/</c>.
	/// </remarks>
	public static class WorldDesignRender
	{
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const int WIDTH = 1800;
		private const int HEIGHT = 1100;
		private const int SETTLE_FRAMES = 90;

		private static GameObject host;
		private static UIDocument document;
		private static PanelSettings settings;
		private static RenderTexture texture;
		private static int frames;
		private static string outputDirectory;
		private static readonly Queue<string> stages = new Queue<string>();
		private static string current;
		private static int failures;

		[DashboardTool(DashboardToolAttribute.UITests, "Render World Designers", Section = "Renders", Order = 20, Tooltip = "Creates the example solar system if missing and renders the Solar System and World Atlas pages to PanelRenders/.")]
		public static void Render()
		{
			outputDirectory = Environment.GetEnvironmentVariable("FISHMMO_WORLD_RENDER_DIR");
			if (string.IsNullOrEmpty(outputDirectory))
			{
				outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "PanelRenders"));
			}
			Directory.CreateDirectory(outputDirectory);
			failures = 0;
			try
			{
				Prepare();
			}
			catch (Exception ex)
			{
				Debug.LogError($"[WorldDesignRender] setup failed: {ex}");
				EditorAutomation.Finish(1);
				return;
			}
			stages.Clear();
			foreach (string stage in new[] { "solar-system-day", "solar-system-moon", "world-atlas", "world-atlas-close" })
			{
				stages.Enqueue(stage);
			}
			current = null;
			EditorApplication.update -= Pump;
			EditorApplication.update += Pump;
		}

		/// <summary>The example system, saved; then placement and sky extras, in memory only.</summary>
		private static void Prepare()
		{
			SolarSystemProfile system = WorldEditorAssets.CreateExampleSystem();
			WorldAtlas atlas = WorldEditorAssets.EnsureAtlas(system);
			WorldEditorAssets.SyncAtlasScenes(atlas);
			AssetDatabase.SaveAssets();

			// ── In memory from here on: nothing below is saved. ──
			var ember = ScriptableObject.CreateInstance<WorldBody>();
			ember.name = "Ember";
			ember.DisplayName = "Ember";
			ember.Parent = system.PrimaryStar;
			ember.Orbit = new OrbitSettings { Distance = 0.62f, Eccentricity = 0.05f, OffsetDegrees = 140f, PeriodMode = OrbitPeriodMode.Kepler, PeriodDays = 1f };
			ember.Retrograde = true;
			ember.Atmosphere = AtmosphereKind.Thick;
			ember.Water = 0.05f;
			ember.Tint = new Color(0.9f, 0.55f, 0.3f, 1f);
			ember.hideFlags = HideFlags.DontSave;
			var tarn = ScriptableObject.CreateInstance<WorldBody>();
			tarn.name = "Tarn";
			tarn.DisplayName = "Tarn";
			tarn.Parent = system.PrimaryStar;
			tarn.Orbit = new OrbitSettings { Distance = 4.2f, OffsetDegrees = 250f, PeriodMode = OrbitPeriodMode.Kepler, PeriodDays = 1f };
			tarn.HasRings = true;
			tarn.SkyRadiusKm = 58000f;
			tarn.Tint = new Color(0.85f, 0.75f, 0.55f, 1f);
			tarn.hideFlags = HideFlags.DontSave;
			var comet = ScriptableObject.CreateInstance<CometBody>();
			comet.name = "Comet K";
			comet.DisplayName = "Comet K";
			comet.Parent = system.PrimaryStar;
			comet.Orbit = new OrbitSettings { Distance = 9f, Eccentricity = 0.93f, InclinationDegrees = 12f, OffsetDegrees = 355f, PeriodMode = OrbitPeriodMode.Kepler };
			comet.hideFlags = HideFlags.DontSave;
			system.Bodies.Add(ember);
			system.Bodies.Add(tarn);
			system.Bodies.Add(comet);
			system.MeteorShowers.Add(new MeteorShower { Name = "Example shower", PeakDayOfYear = 1, HalfWidthDays = 5f, PeakPerHour = 80f });
			system.AsteroidBelts.Add(new AsteroidBelt { Name = "Example belt", InnerAU = 2.2f, OuterAU = 3.2f, Count = 1500, Seed = 3 });

		}

		/// <summary>Lays every scene out on Home, in memory only.</summary>
		private static void PlaceScenes(SolarSystemProfile system)
		{
			WorldBody home = system.HomeWorld;
			WorldBody moon = null;
			foreach (CelestialBody body in system.Bodies)
			{
				if (body is WorldBody w && w.Kind == WorldBodyKind.Moon)
				{
					moon = w;
				}
			}
			var placed = new List<AtlasFootprint>();
			double lon = -6.0;
			foreach (WorldAtlasScene entry in WorldEditorAssets.FindAll<WorldAtlasScene>())
			{
				entry.Body = home;
				entry.Placed = true;
				var wanted = new AtlasFootprint(18.0 + (placed.Count % 2) * 5.0, lon, (placed.Count * 90) % 180, entry.SizeKm);
				AtlasGeometry.FindFreeSpot(wanted, placed, home.AtlasRadiusKm, out double la, out double lo);
				entry.Latitude = la;
				entry.Longitude = lo;
				entry.HeadingDegrees = wanted.HeadingDegrees;
				placed.Add(AtlasFootprint.Of(entry));
				lon += 5.0;
			}
			Debug.Log($"[WorldDesignRender] placed {placed.Count} scene(s) in memory on {home.ResolvedName}; moon {(moon != null ? moon.ResolvedName : "none")}.");
		}

		private static void Pump()
		{
			try
			{
				if (current != null)
				{
					if (++frames < SETTLE_FRAMES)
					{
						document?.rootVisualElement?.MarkDirtyRepaint();
						return;
					}
					string path = Path.Combine(outputDirectory, "WorldDesign-" + current + ".png");
					Capture(path);
					Debug.Log($"[WorldDesignRender] wrote {path}");
					Teardown();
					current = null;
					return;
				}
				if (stages.Count == 0)
				{
					EditorApplication.update -= Pump;
					ReleaseTarget();
					EditorAutomation.Finish(failures == 0 ? 0 : 1);
					return;
				}
				current = stages.Dequeue();
				frames = 0;
				Mount(current);
			}
			catch (Exception ex)
			{
				failures++;
				Debug.LogError($"[WorldDesignRender] stage {current} failed: {ex}");
				Teardown();
				current = null;
			}
		}

		private static void Mount(string stage)
		{
			EnsureTarget();
			host = new GameObject("Render_WorldDesign_" + stage) { hideFlags = HideFlags.HideAndDontSave };
			document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			VisualElement root = document.rootVisualElement;
			root.style.flexGrow = 1f;
			root.style.backgroundColor = new Color(0.16f, 0.16f, 0.17f, 1f);

			SolarSystemProfile system = WorldEditorAssets.FindFirst<SolarSystemProfile>();
			switch (stage)
			{
				case "solar-system-day":
				case "solar-system-moon":
				{
					var page = new SolarSystemPage();
					page.style.width = WIDTH;
					page.style.height = HEIGHT;
					root.Add(page);
					WorldBody observer = system.HomeWorld;
					double hours = 30.0 * CelestialMath.HomeSolarDayHours(system) + 2.0;
					if (stage == "solar-system-moon")
					{
						foreach (CelestialBody body in system.Bodies)
						{
							if (body is WorldBody w && w.Kind == WorldBodyKind.Moon)
							{
								observer = w;
							}
						}
						hours = 30.0 * CelestialMath.HomeSolarDayHours(system) + 5.0;
					}
					page.ShowSky(observer, hours, 25f, 10f);
					break;
				}
				default:
				{
					PlaceScenes(system);
					var page = new WorldAtlasPage();
					int placedNow = 0;
					foreach (WorldAtlasScene entry in WorldEditorAssets.FindAll<WorldAtlasScene>())
					{
						placedNow += entry.Placed ? 1 : 0;
					}
					Debug.Log($"[WorldDesignRender] {placedNow} scene(s) placed after the page was built.");
					page.style.width = WIDTH;
					page.style.height = HEIGHT;
					root.Add(page);
					page.Globe.schedule.Execute(() =>
					{
						page.Globe.Frame();
						if (stage == "world-atlas-close")
						{
							page.Globe.Zoom *= 2.2f;
						}
					}).ExecuteLater(200);
					break;
				}
			}
		}

		private static void EnsureTarget()
		{
			if (texture != null)
			{
				return;
			}
			texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
			texture.Create();
			settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;
			settings.scaleMode = PanelScaleMode.ConstantPixelSize;
			settings.scale = 1f;
			settings.clearColor = true;
			settings.colorClearValue = new Color(0.1f, 0.1f, 0.1f, 1f);
		}

		private static void Capture(string outputPath)
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = texture;
			try
			{
				var image = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
				image.Apply();
				File.WriteAllBytes(outputPath, image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		private static void Teardown()
		{
			if (host != null)
			{
				UnityEngine.Object.DestroyImmediate(host);
			}
			host = null;
			document = null;
		}

		private static void ReleaseTarget()
		{
			if (settings != null)
			{
				UnityEngine.Object.DestroyImmediate(settings);
			}
			if (texture != null)
			{
				texture.Release();
				UnityEngine.Object.DestroyImmediate(texture);
			}
			settings = null;
			texture = null;
		}
	}
}
