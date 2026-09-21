#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Example sky objects for the solar system: a big textured companion world, a comet on a long
	/// eccentric orbit, an asteroid belt and a meteor shower.
	/// </summary>
	/// <remarks>
	/// The example system is deliberately small — a sun, a home world and a moon — which leaves the
	/// textured-body, comet, asteroid and meteor paths with nothing to draw. These give a designer
	/// (and the Sky Sim test bed) one of each to look at. Everything is created once and kept;
	/// running the tool again only fills in what is missing.
	/// </remarks>
	public static class SkyObjectExamples
	{
		public const string TexturesFolder = WorldEditorAssets.BodiesFolder + "/Textures";
		public const string CompanionTexturePath = TexturesFolder + "/Companion Surface.png";
		private const int TextureWidth = 1024;
		private const int TextureHeight = 512;
		private const int Seed = 238;

		/// <summary>The Solar System page's "Add example sky objects" button.</summary>
		public static void AddFromDashboard()
		{
			SolarSystemProfile system = WorldEditorAssets.FindFirst<SolarSystemProfile>();
			if (system == null)
			{
				Debug.LogWarning("[Sky objects] No solar system yet. Press New on the Solar System page first.");
				return;
			}
			Add(system);
			Debug.Log($"[Sky objects] {system.name} now has a companion world, a comet, {system.AsteroidBelts.Count} belt(s) and {system.MeteorShowers.Count} shower(s).");
		}

		/// <summary>Adds whatever the system is missing. Returns the companion world.</summary>
		public static WorldBody Add(SolarSystemProfile system)
		{
			if (system == null || system.HomeWorld == null)
			{
				return null;
			}
			WorldBody home = system.HomeWorld;
			Texture2D surface = BakeCompanionTexture();

			WorldBody companion = Find<WorldBody>(system, "Companion");
			if (companion == null)
			{
				companion = WorldEditorAssets.Create<WorldBody>(WorldEditorAssets.BodiesFolder, "Companion", b =>
				{
					b.DisplayName = "Companion";
					b.Kind = WorldBodyKind.Moon;
					b.Parent = home;
					b.TidallyLocked = true;
					b.AxialTiltDegrees = 3f;
					// Close and large: about 11° across, well past the textured threshold.
					b.Orbit = new OrbitSettings { Distance = 62f, InclinationDegrees = 2.5f, PeriodMode = OrbitPeriodMode.Authored, PeriodDays = 3.1f };
					b.SkyRadiusKm = 6000f;
					b.Atmosphere = AtmosphereKind.Thick;
					b.Water = 0.1f;
					b.HasRings = true;
					b.Tint = new Color(0.85f, 0.72f, 0.55f, 1f);
					b.MinimumRadiusKm = 20f;
					b.CurrentRadiusKm = 20f;
				});
				system.Bodies.Add(companion);
			}
			if (companion.SurfaceTexture == null)
			{
				companion.SurfaceTexture = surface;
				EditorUtility.SetDirty(companion);
			}

			CometBody comet = Find<CometBody>(system, "Wanderer");
			if (comet == null)
			{
				comet = WorldEditorAssets.Create<CometBody>(WorldEditorAssets.BodiesFolder, "Wanderer", c =>
				{
					c.DisplayName = "Wanderer";
					c.Parent = system.PrimaryStar;
					// Perihelion inside the home orbit, aphelion far out: a bright tail every few years.
					c.Orbit = new OrbitSettings { Distance = 3.2f, Eccentricity = 0.78f, InclinationDegrees = 17f, PeriodMode = OrbitPeriodMode.Kepler };
					c.SkyRadiusKm = 8f;
					c.TailLengthMillionKm = 60f;
					c.Brightness = 1.4f;
					c.Tint = new Color(0.85f, 0.9f, 0.8f, 1f);
					c.IonTailColor = new Color(0.55f, 0.75f, 1f, 1f);
				});
				system.Bodies.Add(comet);
			}

			if (system.AsteroidBelts.Count == 0)
			{
				system.AsteroidBelts.Add(new AsteroidBelt
				{
					Name = "Inner Belt",
					InnerAU = 2.2f,
					OuterAU = 3.3f,
					Count = 2000,
					ThicknessDegrees = 6f,
					Seed = Seed,
					Color = new Color(0.8f, 0.75f, 0.7f, 1f),
				});
			}

			if (system.MeteorShowers.Count == 0)
			{
				system.MeteorShowers.Add(new MeteorShower
				{
					Name = "Emberfall",
					PeakDayOfYear = 200,
					HalfWidthDays = 3f,
					PeakPerHour = 90f,
					RadiantRightAscension = 48f,
					RadiantDeclination = 58f,
					Color = new Color(1f, 0.85f, 0.65f, 1f),
				});
			}

			EditorUtility.SetDirty(system);
			WorldEditorAssets.RegisterAddressable(system);
			AssetDatabase.SaveAssets();
			return companion;
		}

		private static T Find<T>(SolarSystemProfile system, string name) where T : CelestialBody
		{
			foreach (CelestialBody body in system.Bodies)
			{
				if (body is T match && (match.name == name || match.ResolvedName == name))
				{
					return match;
				}
			}
			return null;
		}

		/// <summary>
		/// Bakes the companion's surface: an equirectangular map of banded cloud, so the body reads
		/// as a turning world rather than a flat disc. Deterministic, and only baked once.
		/// </summary>
		public static Texture2D BakeCompanionTexture()
		{
			Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(CompanionTexturePath);
			if (existing != null)
			{
				return existing;
			}
			WorldEditorAssets.EnsureFolder(TexturesFolder);
			var texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGB24, false);
			var random = new global::System.Random(Seed);
			// A few noise octaves, stretched around the equator into bands.
			float[] offsets = new float[6];
			for (int i = 0; i < offsets.Length; i++)
			{
				offsets[i] = (float)random.NextDouble() * 100f;
			}
			var warm = new Color(0.92f, 0.78f, 0.55f);
			var pale = new Color(0.98f, 0.94f, 0.86f);
			var dark = new Color(0.55f, 0.4f, 0.28f);
			var pixels = new Color[TextureWidth * TextureHeight];
			for (int y = 0; y < TextureHeight; y++)
			{
				float v = y / (float)(TextureHeight - 1);
				// Bands: latitude bins with a little wobble, more of them toward the equator.
				float bands = Mathf.Sin(v * Mathf.PI * 14f) * 0.5f + 0.5f;
				for (int x = 0; x < TextureWidth; x++)
				{
					float u = x / (float)(TextureWidth - 1);
					float swirl = 0f;
					float amplitude = 0.5f, frequency = 3f;
					for (int o = 0; o < offsets.Length; o++)
					{
						// Stretched along longitude: the flow runs around the body.
						swirl += Mathf.PerlinNoise(u * frequency + offsets[o], v * frequency * 4f + offsets[o]) * amplitude;
						amplitude *= 0.5f;
						frequency *= 2f;
					}
					float t = Mathf.Clamp01(bands * 0.65f + swirl * 0.5f);
					Color colour = t < 0.45f ? Color.Lerp(dark, warm, t / 0.45f) : Color.Lerp(warm, pale, (t - 0.45f) / 0.55f);
					// A darker polar cap at each end.
					float pole = Mathf.InverseLerp(0.82f, 1f, Mathf.Abs(v - 0.5f) * 2f);
					colour = Color.Lerp(colour, dark * 0.8f, pole * 0.7f);
					pixels[y * TextureWidth + x] = colour;
				}
			}
			texture.SetPixels(pixels);
			texture.Apply();
			File.WriteAllBytes(CompanionTexturePath, texture.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(texture);
			AssetDatabase.ImportAsset(CompanionTexturePath, ImportAssetOptions.ForceUpdate);
			var importer = AssetImporter.GetAtPath(CompanionTexturePath) as TextureImporter;
			if (importer != null)
			{
				importer.textureType = TextureImporterType.Default;
				importer.wrapModeU = TextureWrapMode.Repeat;
				importer.wrapModeV = TextureWrapMode.Clamp;
				importer.mipmapEnabled = true;
				importer.isReadable = false;
				importer.SaveAndReimport();
			}
			Texture2D asset = AssetDatabase.LoadAssetAtPath<Texture2D>(CompanionTexturePath);
			WorldEditorAssets.RegisterAddressable(asset);
			return asset;
		}
	}
}
#endif
