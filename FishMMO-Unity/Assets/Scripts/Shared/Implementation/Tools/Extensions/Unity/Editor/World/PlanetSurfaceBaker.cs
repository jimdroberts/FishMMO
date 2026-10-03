#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Writes each world body's generated terrain into an image, for the globe in the designer and
	/// the disc in the sky.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A bake, not a source.</b> <see cref="PlanetSurface"/> is the truth; this only writes it
	/// down at a resolution a GPU can sample. Nothing reads a height back out of an image — the
	/// server samples the function for a scene's altitude and the scene generator samples it across
	/// a rectangle at whatever precision it wants — so the picture cannot drift from the world it
	/// shows, and deleting every file here costs nothing but a re-bake.
	/// </para>
	/// <para>
	/// <b>Everything it writes is build output, not source</b>, exactly as the world map bake is:
	/// a client build bakes, registers the images in a client addressable group, builds, and then
	/// removes them again. The folder and the group are both gitignored. Nineteen bodies at this
	/// resolution is about 30 MB of PNG that a seed reproduces exactly, and committing that would
	/// be storing what a function can produce.
	/// </para>
	/// <para>
	/// <b>Nothing is written onto the body asset.</b> The texture is found by address, not by a
	/// reference on <see cref="CelestialBody.SurfaceTexture"/> — a hard reference would pull every
	/// planet's art into a dedicated server build that never draws a frame, and would leave the
	/// committed body asset pointing at a file the bake deletes. <see cref="CelestialBody.SurfaceTexture"/>
	/// stays for hand-made surfaces, and a baked one takes precedence over it.
	/// </para>
	/// </remarks>
	public static class PlanetSurfaceBaker
	{
		/// <summary>Folder the baked surfaces are written to. Gitignored: this is build output.</summary>
		public const string BakedDirectory = "Assets/Prefabs/Shared/PlanetSurfaces";

		/// <summary>
		/// Addressable group the baked surfaces are placed in.
		/// </summary>
		/// <remarks>
		/// The name carries "Client" because the build tool excludes groups by that substring from
		/// server bundles. A dedicated server samples <see cref="PlanetSurface"/> directly for
		/// every height it needs and never draws a planet, so it has no use for the pictures.
		/// </remarks>
		public const string AddressableGroupName = "ClientPlanetSurfaces";

		/// <summary>Width of a baked surface. Height is half of it: an equirectangular map is 2:1.</summary>
		public const int Width = 2048;

		/// <summary>Where one body's surface is written.</summary>
		public static string BakedImagePath(string bodyName) => $"{BakedDirectory}/{WorldEditorAssets.Sanitize(bodyName)}.png";

		/// <summary>The address a baked surface is loaded by at runtime.</summary>
		public static string AddressOf(string bodyName) => $"PlanetSurfaces/{bodyName}";

		// ── Baking ────────────────────────────────────────────────────

		[DashboardTool(DashboardToolAttribute.Maintenance, "Bake planet surfaces", Section = "Content", Order = 5,
			Tooltip = "Generates a surface image for every planet and moon from its terrain seed. Build output: the folder is gitignored and a client build removes it again.")]
		public static void BakeFromDashboard()
		{
			int baked = BakeAll(out int skipped);
			Debug.Log($"[PlanetSurfaceBaker] Baked {baked} surface(s), skipped {skipped}. They are build output: '{BakedDirectory}' is gitignored.");
		}

		[DashboardTool(DashboardToolAttribute.Maintenance, "Remove baked planet surfaces", Section = "Content", Order = 6,
			Tooltip = "Deletes the baked surface images and their addressable group, leaving the project as it was found.")]
		public static void CleanFromDashboard()
		{
			CleanBakedSurfaces();
		}

		/// <summary>Bakes every world body that has ground. Returns how many were written.</summary>
		public static int BakeAll(out int skipped)
		{
			skipped = 0;
			int baked = 0;
			foreach (WorldBody body in WorldEditorAssets.FindAll<WorldBody>())
			{
				if (body == null)
				{
					skipped++;
					continue;
				}
				if (Bake(body) != null)
				{
					baked++;
				}
			}
			if (baked > 0)
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}
			return baked;
		}

		/// <summary>
		/// Whether a body has a surface to bake.
		/// </summary>
		/// <remarks>
		/// A gas giant has no ground — what looks like a surface is a cloud deck at whatever depth
		/// the pressure makes one — so a heightmap would be a picture of something that is not there.
		/// </remarks>
		public static bool HasGround(WorldBody body)
		{
			return body != null && body.Kind != WorldBodyKind.GasGiant;
		}

		/// <summary>The already-baked surface for a body, or null. Editor-side lookup, straight off disk.</summary>
		public static Texture2D Baked(WorldBody body)
		{
			return body == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(BakedImagePath(body.name));
		}

		/// <summary>Generates one body's surface image and registers it. Returns the imported texture.</summary>
		public static Texture2D Bake(WorldBody body)
		{
			if (body == null)
			{
				return null;
			}
			if (!HasGround(body))
			{
				/* A gas giant has no ground, but it is the most striking thing in any sky it is in.
				 * Skipping it left the atlas drawing a flat tinted ball — read, reasonably, as a
				 * planet that had come out plain blue. It has a surface to show; it is just made of
				 * cloud rather than rock. */
				return BakeGasGiant(body);
			}

			WorldEditorAssets.EnsureFolder(BakedDirectory);

			/* The world's own climate, so the picture is of THIS planet rather than a colour ramp.
			 * Every body used to come out the same brown-to-white gradient whatever its orbit,
			 * atmosphere or ocean — a frozen moon and a temperate world were indistinguishable, and
			 * nothing showed where the ice caps or the deserts would actually fall.
			 *
			 * Resolved once, in the shared field, so the picture, the ground a scene is cut from
			 * and the name that scene is given all come from the same arithmetic. */
			SolarSystemProfile system = SolarSystemProfile.Resolve(body);
			PlanetClimateField climate = PlanetClimateField.For(system, body);

			uint seed = climate.Seed;
			float cratering = climate.Cratering;
			PlanetSurface.PlanetProfile profile = climate.Profile;
			float relief = climate.ReliefMetres;
			// What the world makes for itself: lava on a tormented moon, a fractured shell on a
			// frozen one. Nothing to do with how far it sits from its star.
			float internalHeat = climate.InternalHeat;

			/* Land is painted with the biome the scene generator would paint there, so the globe,
			 * a scene's biome map and the ground underfoot all show the same place the same way.
			 * Asked of the same field the generator asks, from the same height and altitude this
			 * bake already has, so it costs the resolver and the moisture walk and nothing more. A biome with no art of
			 * its own keeps the physical shading below rather than a placeholder's flat colour. */
			BiomeGroundColours.ClearCache();
			BiomeTerrainLayers.ClearCache();
			var palette = new BiomePalette();
			bool hasOcean = body.Water > 0f;

			/* Where rock stands molten: the same decision, and the same level, the scene generator puts a
			 * lava surface at (SurfaceLiquids), tested against the same altitude it compares the terrain
			 * with — so a scene cut from the globe finds lava exactly where the globe shows it. A world
			 * above the melting point of rock was drawn as blue sea here whenever it had any water at all,
			 * while every scene cut from it was a magma ocean. */
			bool hasLava = SurfaceLiquids.For(system, body, out float lavaLevelMetres) == SurfaceLiquid.Lava;

			int height = Width / 2;
			var texture = new Texture2D(Width, height, TextureFormat.RGBA32, false);
			var pixels = new Color32[Width * height];

			/* One row of heights kept so the slope costs nothing.
			 *
			 * Relief shading is what makes a crater read as a bowl rather than a smudge, and it
			 * needs the height of the neighbours — but those are the samples just taken. Keeping
			 * the previous row and the previous pixel gives the gradient for free, where sampling
			 * the function again would have tripled the cost of the slowest bodies. */
			var previousRow = new float[Width];
			var previousRowAltitudes = new float[Width];
			bool hasPreviousRow = false;

			/* The row, taken in two passes. The first samples the ground and shades the slope; it
			 * also records each pixel's altitude and whether it is open sea, which is exactly what
			 * the moisture walk upwind asks of every other pixel on the same circle of latitude.
			 * The second resolves the biomes with that walk reading the row instead of the noise:
			 * eight lookups a pixel rather than eight surface samples. */
			var rowHeights = new float[Width];
			var rowAltitudes = new float[Width];
			var rowTemperatures = new float[Width];
			var rowSlopes = new float[Width];
			var rowSteepness = new float[Width];
			var moistureAltitudes = new float[Width];
			var moistureOceans = new bool[Width];
			var rowTerrain = new MoistureRowTerrain(moistureAltitudes, moistureOceans);
			// The same sea test PlanetMoistureTerrain makes: a dry world's low ground is lowland, not sea.
			bool hasSea = profile.OceanFloor != null;

			/* Land is finished in a third pass over the whole image, because blending a border needs
			 * the biomes on the rows below as well as above. These hold what that pass reads: which
			 * biome each land pixel is (0 for a pixel already finished — sea, frozen sea, lava), its
			 * own colour before relief, and the inputs to its variation. About 55 MB at this
			 * resolution, for the length of one body's bake. */
			int pixelCount = Width * height;
			var land = new LandPixels(pixelCount);

			// Metres per pixel, for the slope rock shows on: the body's real size, which its relief is scaled from.
			double radiusMetres = (body.SkyRadiusKm > 0.01f ? body.SkyRadiusKm : PlanetSurface.EarthRadiusKm) * 1000.0;
			float northMetres = (float)(System.Math.PI * radiusMetres / height);
			float equatorMetres = (float)(2.0 * System.Math.PI * radiusMetres / Width);
			float earthScale = Mathf.Max(1e-3f, relief / PlanetSurface.EarthReliefMetres);
			// Greener where wet, browner where dry, only where something grows.
			bool living = climate.Conditions.HasLiquidWater;

			for (int y = 0; y < height; y++)
			{
				/* Pixel centres. Sampling the exact pole would make the whole top row one point,
				 * and every longitude there would return the same colour — a visible bar of flat
				 * colour across the top of every planet. */
				double latitude = 90.0 - (y + 0.5) * 180.0 / height;
				int row = y * Width;
				float previousHeight = 0f;
				float previousAltitude = 0f;
				bool hasPreviousHeight = false;
				for (int x = 0; x < Width; x++)
				{
					double longitude = (x + 0.5) * 360.0 / Width - 180.0;
					float h = PlanetSurface.HeightAt(seed, latitude, longitude, cratering);
					/* Temperature here: the ground under the star at noon, cooled toward the pole by
					 * the sun's noon altitude, and cooled again by how far above sea level the ground
					 * stands. The same three terms the running game uses. */
					// The same helper the terrain generator uses, so the picture and the ground
					// agree about how high a continent is.
					float altitude = PlanetSurface.AltitudeFromHeight(h, profile, relief);
					Vector3 direction = PlanetSurface.Direction(latitude, longitude);
					float temperature = climate.TemperatureAt(latitude, direction, altitude);
					/* Slope from the neighbours already taken. Scaled by the cosine of the
					 * latitude because an equirectangular map's columns crowd together toward the
					 * poles: without it a gentle slope near the pole shades like a cliff. */
					float east = hasPreviousHeight ? h - previousHeight : 0f;
					float north = hasPreviousRow ? h - previousRow[x] : 0f;
					/* Floored well away from zero, and faded out entirely at the poles.
					 *
					 * Dividing by the cosine converts an east-west step into real surface distance,
					 * which is right everywhere except near the axis, where the cosine goes to zero
					 * and the division explodes — the top and bottom rows came out as a bright
					 * stripe of amplified noise. The poles are a handful of texels on a globe and
					 * nothing there is worth that, so the shading simply stops. */
					float cosLat = Mathf.Max(0.3f, Mathf.Cos((float)latitude * Mathf.Deg2Rad));
					float polarFade = 1f - Smooth(72f, 86f, Mathf.Abs((float)latitude));

					// The same neighbours in metres over metres, for where rock shows through.
					float eastGrade = hasPreviousHeight ? (altitude - previousAltitude) / (equatorMetres * cosLat) : 0f;
					float northGrade = hasPreviousRow ? (altitude - previousRowAltitudes[x]) / northMetres : 0f;

					previousHeight = h;
					previousAltitude = altitude;
					hasPreviousHeight = true;
					rowHeights[x] = h;
					rowAltitudes[x] = altitude;
					rowTemperatures[x] = temperature;
					rowSlopes[x] = ((east / cosLat) * 0.7f + north * 0.7f) * polarFade;
					rowSteepness[x] = Mathf.Sqrt(eastGrade * eastGrade + northGrade * northGrade) * polarFade;
					moistureOceans[x] = hasSea && h <= profile.SeaLevel;
					moistureAltitudes[x] = moistureOceans[x] ? 0f : altitude;
				}

				for (int x = 0; x < Width; x++)
				{
					// The surface field's own height and altitude: never a biome selection's moved one.
					float h = rowHeights[x];
					float altitude = rowAltitudes[x];
					double longitude = (x + 0.5) * 360.0 / Width - 180.0;
					Vector3 direction = PlanetSurface.Direction(latitude, longitude);

					switch (Classify(hasLava, lavaLevelMetres, hasOcean, profile.SeaLevel, h, altitude))
					{
						// Molten ground first: no biome lives under lava, and no sea is drawn over it.
						case GlobePixel.Lava:
							pixels[row + x] = Lava(direction, seed);
							continue;
						case GlobePixel.Sea:
							pixels[row + x] = Shade(h, profile, body, rowTemperatures[x], rowSlopes[x], internalHeat, direction, seed,
								SeaDepth01(-altitude / (relief / PlanetSurface.EarthReliefMetres)));
							continue;
					}

					BiomeTemplate biome = climate.BiomeAt(latitude, direction, h, altitude, rowTerrain, out PlanetSurfacePoint point);
					int i = row + x;
					BiomePalette.Entry entry = palette.Of(biome);
					land.Ids[i] = entry.Id;
					if (entry.HasArt)
					{
						land.Bases[i] = entry.Main;
						land.Cliffs[i] = entry.Cliff;
						land.Art[i] = true;
					}
					else
					{
						// A biome with no art of its own keeps the physical shading rather than a placeholder's flat colour.
						Color shaded = ShadeColour(h, profile, body, rowTemperatures[x], internalHeat, direction, seed, 0f);
						land.Bases[i] = shaded;
						land.Cliffs[i] = shaded;
					}
					land.Slopes[i] = rowSlopes[x];
					land.Steepness[i] = rowSteepness[x];
					land.EarthAltitudes[i] = altitude / earthScale;
					land.Humidities[i] = point.Climate.Humidity;
				}

				// This row is the next one's northern neighbour.
				(previousRow, rowHeights) = (rowHeights, previousRow);
				(previousRowAltitudes, rowAltitudes) = (rowAltitudes, previousRowAltitudes);
				hasPreviousRow = true;
			}

			/* Land, finished: each pixel's colour blended with the biomes around it, varied within its
			 * own biome, and only then lit. Sea, frozen sea and lava were finished above and are never
			 * read or written here. */
			for (int y = 0; y < height; y++)
			{
				double latitude = 90.0 - (y + 0.5) * 180.0 / height;
				// A pixel's width in equator pixels, so the blend reaches as far east-west as north-south.
				float stretch = Mathf.Clamp(1f / Mathf.Cos((float)latitude * Mathf.Deg2Rad), 1f, MaxBlendStretch);
				int row = y * Width;
				for (int x = 0; x < Width; x++)
				{
					int i = row + x;
					if (land.Ids[i] == 0)
					{
						continue;
					}
					double longitude = (x + 0.5) * 360.0 / Width - 180.0;
					Vector3 direction = PlanetSurface.Direction(latitude, longitude);
					BorderWarp(seed, direction, out float warpX, out float warpY);
					LandBlend blend = BlendAcrossBiomes(land.Ids, land.Bases, land.Cliffs, land.Art, Width, height, x, y,
						x + warpX * stretch, y + warpY, stretch);
					Color colour = Vary(blend, land.EarthAltitudes[i], land.Humidities[i], living, land.Steepness[i], Mottle(seed, direction));
					pixels[i] = Relief(colour, land.Slopes[i], body);
				}
			}

			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			return Write(texture, body);
		}

		/// <summary>Writes a finished image to disk, imports it and registers it. Destroys the texture.</summary>
		private static Texture2D Write(Texture2D texture, WorldBody body)
		{
			string path = BakedImagePath(body.name);
			File.WriteAllBytes(path, texture.EncodeToPNG());
			Object.DestroyImmediate(texture);
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

			var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
			if (imported != null)
			{
				MakeAddressable(path, body.name);
			}
			return imported;
		}

		/// <summary>
		/// Puts a baked surface in the client group under its body's address.
		/// </summary>
		/// <remarks>
		/// Addressable rather than a reference on the body, because <see cref="WorldBody"/> is
		/// shared content the scene server loads. A hard reference would pull every planet's art
		/// into a dedicated server build, and would leave the committed body asset pointing at a
		/// file this bake deletes again.
		/// </remarks>
		private static void MakeAddressable(string imagePath, string bodyName)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				Debug.LogWarning($"[PlanetSurfaceBaker] Addressables is not initialised, so '{imagePath}' will not load at runtime. Open Window > Asset Management > Addressables > Groups once.");
				return;
			}

			AddressableAssetGroup group = settings.FindGroup(AddressableGroupName);
			if (group == null)
			{
				group = settings.CreateGroup(AddressableGroupName, false, false, false, null,
					settings.DefaultGroup.Schemas.ConvertAll(schema => schema.GetType()).ToArray());
			}

			string guid = AssetDatabase.AssetPathToGUID(imagePath);
			AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
			if (entry != null)
			{
				entry.address = AddressOf(bodyName);
			}
		}

		/// <summary>
		/// Removes everything the bake produced: the images and their addressable group.
		/// </summary>
		/// <remarks>
		/// Called by the build tool after a client build, and available as a dashboard button for
		/// tidying up after a manual bake. The project is left exactly as it was found.
		/// </remarks>
		public static void CleanBakedSurfaces()
		{
			bool removedFolder = AssetDatabase.IsValidFolder(BakedDirectory) && AssetDatabase.DeleteAsset(BakedDirectory);
			if (!removedFolder && Directory.Exists(BakedDirectory))
			{
				// Not known to the asset database (a bake without a refresh): remove it directly.
				Directory.Delete(BakedDirectory, true);
				if (File.Exists(BakedDirectory + ".meta"))
				{
					File.Delete(BakedDirectory + ".meta");
				}
				removedFolder = true;
			}

			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			AddressableAssetGroup group = settings != null ? settings.FindGroup(AddressableGroupName) : null;
			if (group != null)
			{
				// Removes the group asset and its schema assets too.
				settings.RemoveGroup(group);
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log($"[PlanetSurfaceBaker] Removed {(removedFolder ? $"'{BakedDirectory}'" : "no bake folder")}{(group != null ? $" and the '{AddressableGroupName}' group" : "")}.");
		}

		// ── Colour ────────────────────────────────────────────────────

		/// <summary>
		/// Writes a gas giant's cloud deck: latitude bands, sheared and stirred, with storms.
		/// </summary>
		/// <remarks>
		/// Banded because a fast-spinning fluid planet is: rotation breaks the flow into zonal jets
		/// running parallel to the equator, which is why Jupiter and Saturn are striped and why the
		/// stripes never cross. The noise shears along those bands rather than across them, so the
		/// texture reads as flow rather than as marble.
		/// </remarks>
		private static Texture2D BakeGasGiant(WorldBody body)
		{
			WorldEditorAssets.EnsureFolder(BakedDirectory);
			uint seed = body.ResolvedTerrainSeed;

			int height = Width / 2;
			var texture = new Texture2D(Width, height, TextureFormat.RGBA32, false);
			var pixels = new Color32[Width * height];

			/* The body's own colour, but only as a wash over strongly separated bands.
			 *
			 * Tinting each band 50% toward the body's colour made all three nearly the same shade,
			 * and a gas giant whose belts and zones differ by a few percent reads as a plain ball.
			 * What makes Jupiter legible is the contrast BETWEEN bands, so the bands keep their
			 * own values and the tint is a light glaze on top. */
			Color warm = Color.Lerp(new Color(0.94f, 0.86f, 0.70f), body.Tint, 0.22f);
			Color cool = Color.Lerp(new Color(0.55f, 0.47f, 0.40f), body.Tint, 0.30f);
			Color deep = Color.Lerp(new Color(0.26f, 0.22f, 0.22f), body.Tint, 0.34f);

			for (int y = 0; y < height; y++)
			{
				double latitude = 90.0 - (y + 0.5) * 180.0 / height;
				float lat01 = (float)(latitude / 90.0);
				int row = y * Width;

				for (int x = 0; x < Width; x++)
				{
					double longitude = (x + 0.5) * 360.0 / Width - 180.0;
					Vector3 direction = PlanetSurface.Direction(latitude, longitude);

					/* The latitude coordinate is dragged about before the bands are read from it.
					 * That is what produces festoons — the curling tongues where one band intrudes
					 * into its neighbour — and it is why real bands wander instead of running
					 * ruler-straight. A clean sine in latitude cannot make them at any amplitude. */
					float warp = (PlanetSurface.FieldNoiseAt(seed ^ 0xBA4D1E5u,
						new Vector3(direction.x * 1.6f, direction.y * 3.4f, direction.z * 1.6f), 4) - 0.5f) * 0.30f;

					/* Bands from noise in latitude alone, not a sine: real belts and zones are of
					 * unequal width, and a sine makes every one identical. Sampled on a direction
					 * squashed to nothing in x and z so it varies only with latitude. */
					float bands = PlanetSurface.FieldNoiseAt(seed ^ 0x2B4D0D5u,
						new Vector3(0.013f, (lat01 + warp) * 11f, 0.021f), 4);

					/* Sheared along the flow. Stretched hard in longitude and squashed in latitude,
					 * because zonal jets smear every eddy out sideways — turbulence on a gas giant
					 * runs along the bands and never across them. */
					float shear = (PlanetSurface.FieldNoiseAt(seed ^ 0x53EA71u,
						new Vector3(direction.x * 0.8f, direction.y * 14f, direction.z * 0.8f), 5) - 0.5f) * 0.42f;

					float band = Mathf.Clamp01(bands + shear);
					Color colour = band < 0.42f
						? Color.Lerp(deep, cool, Smooth(0f, 0.42f, band))
						: Color.Lerp(cool, warm, Smooth(0.42f, 1f, band));

					// Storms: oval, because they are stretched by the same jets, and rare.
					/* Storms are wide and shallow: a few degrees of latitude but many of longitude,
					 * because the same jets that shear the bands stretch anything caught in them.
					 * At a high frequency in latitude they came out as specks. */
					float storm = PlanetSurface.FieldNoiseAt(seed ^ 0x5703EDu,
						new Vector3(direction.x * 1.9f, direction.y * 7f, direction.z * 1.9f), 3);
					if (storm > 0.66f)
					{
						Color eye = Color.Lerp(new Color(0.96f, 0.88f, 0.76f), new Color(0.82f, 0.40f, 0.30f), 0.45f);
						colour = Color.Lerp(colour, eye, Smooth(0.66f, 0.84f, storm) * 0.9f);
					}

					// Polar hoods are darker and less banded: the jets break down near the axis.
					float polar = Smooth(0.62f, 0.95f, Mathf.Abs(lat01));
					colour = Color.Lerp(colour, deep, polar * 0.55f);
					// Limb darkening, which every thick atmosphere shows toward its poles.
					colour *= Mathf.Lerp(1f, 0.8f, Mathf.Abs(lat01) * Mathf.Abs(lat01));

					pixels[row + x] = new Color32(
						(byte)(Mathf.Clamp01(colour.r) * 255f),
						(byte)(Mathf.Clamp01(colour.g) * 255f),
						(byte)(Mathf.Clamp01(colour.b) * 255f),
						255);
				}
			}

			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			return Write(texture, body);
		}
		/// <summary>
		/// A smoothstep between two edges, 0..1.
		/// </summary>
		/// <remarks>
		/// <b>Not <c>Mathf.SmoothStep</c>.</b> Unity's takes (from, to, t) and interpolates between
		/// the first two arguments by the third — so <c>Smooth(0f, -0.25f, temperature)</c>
		/// returns a number between 0 and -0.25, which as a <c>Color.Lerp</c> factor clamps to zero
		/// and never moves. That is exactly why polar land stayed brown while the sea beside it
		/// went to ice: three of the four bands in this shader were being asked the wrong question.
		/// </remarks>
		private static float Smooth(float edge0, float edge1, float x)
		{
			return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, x));
		}

		/// <summary>
		/// How dark to draw the sea over ground this deep, 0 at the shore to 1 on the abyssal plain.
		/// </summary>
		/// <param name="earthMetres">Depth in metres on Earth's scale, which the body's own floor is sized from.</param>
		/// <remarks>
		/// <para>
		/// <b>From the metres the ground has, not from the raw field.</b> This used to shade by how
		/// far down the raw field's range a point lay, through a smoothstep that stays flat at its
		/// start — so everything shallower than about two kilometres came out in nearly the same
		/// pale coastal blue. A scene cut from a kilometre of open water 141 km offshore looked like
		/// the shallows on the globe, and the terrain generated there was a kilometre under the
		/// surface: the picture and the ground were showing two different functions, the very bug
		/// the land curve was fixed for.
		/// </para>
		/// <para>
		/// On a log scale, because that is how a sea floor reads: the step from shelf to slope is
		/// a few hundred metres and the one from slope to abyss is kilometres, and both have to
		/// show. The shelf's edge lands near a quarter, a kilometre near 0.6, the abyssal plain at
		/// 0.9 and past.
		/// </para>
		/// </remarks>
		private static float SeaDepth01(float earthMetres)
		{
			const float Scale = 100f;
			const float Abyss = 6000f;
			return Mathf.Clamp01(Mathf.Log(1f + Mathf.Max(0f, earthMetres) / Scale) / Mathf.Log(1f + Abyss / Scale));
		}

		private static Color32 Shade(float h, PlanetSurface.PlanetProfile profile, WorldBody body, float temperature,
			float slope, float internalHeat, Vector3 direction, uint seed, float seaDepth01)
		{
			/* Relief shading, last, over whatever the ground turned out to be.
			 *
			 * Height alone cannot show a crater: its floor and the plain outside it are at similar
			 * heights, so both come out the same colour and the rim between them vanishes. What
			 * makes any planetary map legible is the SLOPE — a light from one side, brightening
			 * what faces it and darkening what turns away. Strongest on airless ground, which has
			 * no water, no vegetation and no weather to tell one place from another. */
			/* Large, because the gradient is per PIXEL. Two neighbouring texels are about a fifth
			 * of a degree apart on the globe, so even a crater wall is a height difference of
			 * roughly a thousandth — at any sane-looking multiplier the shading is a couple of
			 * percent and may as well not be there. The clamp is what keeps it from tearing. */
			/* Measured, not guessed, and twice wrong before this.
			 *
			 * The first pass used 26 and I called it invisible without checking that the bake had
			 * actually finished — it had not. The second used 520, which pins every pixel against
			 * one clamp or the other and turns a moon into an engraving. A crater field has hard
			 * edges, so the step between neighbouring texels is far larger than a smooth heightmap's
			 * and needs a far smaller multiplier, not a larger one. */
			return Relief(ShadeColour(h, profile, body, temperature, internalHeat, direction, seed, seaDepth01), slope, body);
		}

		/// <summary>
		/// The physical colour of the ground or sea at a point, before relief: what the globe shows where
		/// no biome has art, and every sea.
		/// </summary>
		/// <remarks>
		/// Split from <see cref="Shade"/> so a land pixel's own colour can be blended with its neighbours'
		/// before it is lit; the relief must come last, or the blend would smear the slope light too.
		/// </remarks>
		private static Color ShadeColour(float h, PlanetSurface.PlanetProfile profile, WorldBody body, float temperature,
			float internalHeat, Vector3 direction, uint seed, float seaDepth01)
		{
			bool hasOcean = body.Water > 0f;
			bool airless = body.Atmosphere == AtmosphereKind.None;
			Color colour;

			if (hasOcean && h <= profile.SeaLevel)
			{
				float depth = seaDepth01;
				// Frozen over where the sea itself would freeze, which is what makes a polar cap
				// read as a cap rather than as a white ring on the land.
				colour = temperature <= -0.05f
					? Color.Lerp(new Color(0.86f, 0.91f, 0.95f), new Color(0.62f, 0.74f, 0.84f), depth)
					: Color.Lerp(new Color(0.22f, 0.48f, 0.66f), new Color(0.02f, 0.07f, 0.20f), Smooth(0f, 1f, depth));
			}
			else
			{
				/* Measured from the sea on a wet world and from the floor on a dry one.
				 *
				 * A dry world's datum is its MEDIAN ground, so half its surface is below it —
				 * measured from there, that whole half clamps to zero and comes out one flat
				 * colour. On a cratered moon that is every crater floor on the planet, which is
				 * most of what there is to look at. */
				float above = hasOcean
					? Mathf.Clamp01((h - profile.SeaLevel) / Mathf.Max(1e-4f, profile.Highest - profile.SeaLevel))
					: Mathf.Clamp01((h - profile.Lowest) / Mathf.Max(1e-4f, profile.Highest - profile.Lowest));
				Color rock = airless
					// Wider range on airless ground: with no water, no vegetation and no weather,
					// shading is the only thing left to tell a crater floor from a rim.
					? Color.Lerp(new Color(0.22f, 0.21f, 0.20f), new Color(0.72f, 0.70f, 0.66f), above)
					: Color.Lerp(new Color(0.46f, 0.40f, 0.32f), new Color(0.52f, 0.49f, 0.45f), above);

				if (airless || body.Atmosphere == AtmosphereKind.Thin)
				{
					/* An airless world is NOT just regolith at every temperature, which is what
					 * this used to say. Europa is airless and made of ice; the Moon is airless and
					 * made of rock; Io is airless and made of sulphur and lava. What separates them
					 * is water and internal heat, not air. */
					// The world conditions' own ice test, point by point: whenever the world is an
					// ice world (BiomeWorldConditions.IsIceWorld) every point passes it, so the globe
					// is ice exactly where Europa's biomes are chosen and the lava lakes are not.
					bool icy = BiomeWorldConditions.IsIceAt(body.Water, temperature);

					if (icy)
					{
						// Water ice: bright, and bluer where it is thick and old.
						Color ice = Color.Lerp(new Color(0.78f, 0.84f, 0.90f), new Color(0.93f, 0.96f, 0.99f), above);
						colour = ice;

						if (internalHeat >= 0.3f)
						{
							/* Cryovolcanism. A shell worked from beneath cracks into long curved
							 * lineae with darker material welling up through them — Europa's
							 * defining feature, and invisible from temperature alone since the
							 * surface is frozen either way. */
							float lineae = PlanetSurface.FieldNoiseAt(seed ^ 0x1CE1AEu,
								new Vector3(direction.x * 7f, direction.y * 7f, direction.z * 7f), 4);
							float crack = 1f - Mathf.Abs(lineae - 0.5f) * 2f;
							colour = Color.Lerp(colour, new Color(0.62f, 0.52f, 0.45f),
								Smooth(0.86f, 1f, crack) * Mathf.Clamp01(internalHeat));
						}
					}
					else if (internalHeat >= ClimateModel.VolcanicThreshold)
					{
						/* Volcanic rock: fresh basalt is dark, and sulphur compounds stain it
						 * yellow and orange wherever the vents are. Io, not the Moon. */
						float vents = PlanetSurface.FieldNoiseAt(seed ^ 0x7A0A11u,
							new Vector3(direction.x * 5.5f, direction.y * 5.5f, direction.z * 5.5f), 4);
						Color basalt = Color.Lerp(new Color(0.17f, 0.15f, 0.15f), new Color(0.34f, 0.31f, 0.29f), above);
						Color sulphur = Color.Lerp(new Color(0.78f, 0.66f, 0.28f), new Color(0.86f, 0.52f, 0.22f), vents);
						colour = Color.Lerp(basalt, sulphur, Smooth(0.52f, 0.78f, vents) * Mathf.Clamp01(internalHeat));

						/* Molten ground glows in the lowest places, where the crust is thinnest. The
						 * threshold and the fade are SurfaceLiquids', which also stands the lava lakes
						 * at the middle of this fade: below it the bake draws the lakes themselves
						 * (Lava), so what is left of the fade is the hot ground around them. */
						if (internalHeat >= SurfaceLiquids.LavaLakeHeat)
						{
							colour = Color.Lerp(colour, new Color(0.95f, 0.35f, 0.10f),
								Smooth(2f * SurfaceLiquids.LavaLakeLevel, 0f, above) * 0.8f);
						}
					}
					else
					{
						// Dead rock: no air, no water, no heat left. Our own Moon.
						colour = rock;
					}
				}
				else if (temperature <= 0f)
				{
					// Snowline: bare rock gives way to permanent snow a little below freezing.
					colour = Color.Lerp(rock, new Color(0.95f, 0.96f, 0.98f), Smooth(0f, -0.25f, temperature));
				}
				else if (temperature >= 0.62f)
				{
					// Hot: the vegetation goes first, then the ground bakes pale.
					colour = Color.Lerp(new Color(0.72f, 0.62f, 0.42f), new Color(0.80f, 0.74f, 0.62f),
						Smooth(0.62f, 1f, temperature));
				}
				else
				{
					/* Temperate, and only as green as the world has water to be. A dry world's
					 * temperate band is steppe and rock, not meadow. */
					Color living = Color.Lerp(new Color(0.55f, 0.50f, 0.36f), new Color(0.26f, 0.46f, 0.20f), Mathf.Clamp01(body.Water));
					colour = Color.Lerp(living, rock, Smooth(0.35f, 0.9f, above));
				}
			}

			return colour;
		}

		/// <summary>
		/// Molten rock seen from orbit: a pixel is kilometres across, so the crust plates and the glowing
		/// seams between them average out, and what is left is how much of the melt is crusted over.
		/// </summary>
		/// <remarks>
		/// Rafts of cooled crust drift on a lava sea in fields far larger than a pixel, so a low-frequency
		/// field decides how crusted each place is: dim red-brown where the rafts have closed up, the
		/// incandescent orange of open melt where they have parted. Flat, so no relief shading — a liquid
		/// has no slope to light.
		/// </remarks>
		private static Color32 Lava(Vector3 direction, uint seed)
		{
			float rafts = PlanetSurface.FieldNoiseAt(seed ^ 0x1A7A5u,
				new Vector3(direction.x * 9f, direction.y * 9f, direction.z * 9f), 4);
			Color crusted = new Color(0.30f, 0.10f, 0.05f);
			Color open = new Color(0.95f, 0.40f, 0.12f);
			Color colour = Color.Lerp(crusted, open, Smooth(0.35f, 0.75f, rafts));
			return new Color32((byte)(colour.r * 255f), (byte)(colour.g * 255f), (byte)(colour.b * 255f), 255);
		}

		/// <summary>The relief shading every land colour gets, biome or physical, as a finished pixel.</summary>
		private static Color32 Relief(Color colour, float slope, WorldBody body)
		{
			float strength = body.Atmosphere == AtmosphereKind.None ? 14f : 9f;
			colour *= Mathf.Clamp(1f + slope * strength, 0.6f, 1.45f);

			return new Color32(
				(byte)(Mathf.Clamp01(colour.r) * 255f),
				(byte)(Mathf.Clamp01(colour.g) * 255f),
				(byte)(Mathf.Clamp01(colour.b) * 255f),
				255);
		}

		// ── Land: what a pixel is, its biome borders, its variation ────

		/// <summary>What a globe pixel is, decided from the surface field alone.</summary>
		public enum GlobePixel : byte
		{
			Land,
			Sea,
			Lava,
		}

		/// <summary>
		/// Lava, sea or land, from the surface field's own raw height and altitude.
		/// </summary>
		/// <param name="hasLava">Whether the body's rock stands molten anywhere (<see cref="SurfaceLiquids"/>).</param>
		/// <param name="lavaLevelMetres">The level the lava stands at, in the body's metres.</param>
		/// <param name="hasOcean">Whether the body has a sea.</param>
		/// <param name="seaLevel">The sea level on the raw surface field.</param>
		/// <param name="height">The raw surface field at the pixel.</param>
		/// <param name="altitudeMetres">The pixel's altitude, from <see cref="PlanetSurface.AltitudeFromHeight"/>.</param>
		/// <remarks>
		/// <b>Never from a biome selection.</b> The scene generator stands its sea and its lava at these
		/// physical levels, so the globe's shores have to be decided from the same real height and
		/// altitude or the globe and the scenes cut from it would disagree about where a coast is. The
		/// biome choice moves its own copy of the height about to blur landform borders
		/// (<see cref="PlanetClimateField.SelectionPoint"/>); nothing here ever sees that copy.
		/// </remarks>
		public static GlobePixel Classify(bool hasLava, float lavaLevelMetres, bool hasOcean, float seaLevel, float height, float altitudeMetres)
		{
			if (hasLava && altitudeMetres < lavaLevelMetres)
			{
				return GlobePixel.Lava;
			}
			if (hasOcean && height <= seaLevel)
			{
				return GlobePixel.Sea;
			}
			return GlobePixel.Land;
		}

		/// <summary>A land pixel's colours once its neighbours' biomes are blended in.</summary>
		public struct LandBlend
		{
			/// <summary>The ground: biome art's mean colour, or the physical shading where a biome has none.</summary>
			public Color Ground;

			/// <summary>The bare rock the ground gives way to on steep slopes.</summary>
			public Color Cliff;

			/// <summary>How much of the blend is biome art, 0…1: the share the variation applies to.</summary>
			public float Art;
		}

		/// <summary>Half-width of the border blend, in pixels at the equator.</summary>
		public const int BlendReachPixels = 2;

		/// <summary>
		/// The blend's Gaussian radius, in pixels at the equator: about 20 km on an Earth-sized body.
		/// </summary>
		/// <remarks>
		/// A border fades over three or four pixels — soft at the globe's own scale, where an ecotone
		/// tens of kilometres deep is exactly that wide — without smearing biomes that are only a few
		/// pixels across into their neighbours.
		/// </remarks>
		public const float BlendSigmaPixels = 1f;

		/// <summary>The most the blend widens east-west toward the poles, in pixels per equator pixel.</summary>
		/// <remarks>
		/// An equirectangular pixel narrows with the cosine of the latitude, so the blend steps further
		/// east-west to reach the same distance on the ground; capped where the cosine runs out, near 83°.
		/// </remarks>
		public const float MaxBlendStretch = 8f;

		/// <summary>How far the blend's centre is pushed about, in pixels at the equator.</summary>
		/// <remarks>
		/// A plain blur of a smooth border is a smooth soft border. Moving the point the neighbourhood is
		/// read around by a field of a few pixels' scale makes the soft edge ragged and lets the two
		/// biomes reach into each other — the interlocking a real ecotone has at the scale a pixel
		/// cannot resolve.
		/// </remarks>
		public const float BorderWarpPixels = 1.5f;

		/// <summary>
		/// Frequency of the border warp on the unit sphere: cells of about 8 and 4 pixels on a
		/// 2048-wide bake, finer than most of the selection noise's meanders (<see cref="PlanetClimateField.SelectionNoiseFrequency"/>).
		/// </summary>
		/// <remarks>
		/// Measured: at 60 the warp moved by 0.27 of its range per pixel, 0.4 px of centre shift per
		/// pixel stepped — steep enough in places for the read-around point to fold back on itself
		/// and scatter specks. At 40 it moves about two thirds of that, and the ragged edge stays an edge.
		/// </remarks>
		public const float BorderWarpFrequency = 40f;

		private static readonly float[] BlendWeights = BuildBlendWeights();

		private static float[] BuildBlendWeights()
		{
			int side = BlendReachPixels * 2 + 1;
			var weights = new float[side * side];
			int k = 0;
			for (int dy = -BlendReachPixels; dy <= BlendReachPixels; dy++)
			{
				for (int dx = -BlendReachPixels; dx <= BlendReachPixels; dx++, k++)
				{
					weights[k] = Mathf.Exp(-(dx * dx + dy * dy) / (2f * BlendSigmaPixels * BlendSigmaPixels));
				}
			}
			return weights;
		}

		/// <summary>
		/// A land pixel's ground and rock colours blended with the land around it, weighted by distance.
		/// </summary>
		/// <param name="ids">Each pixel's biome, 0 for a pixel that is not land (sea, frozen sea, lava).</param>
		/// <param name="bases">Each land pixel's own ground colour.</param>
		/// <param name="cliffs">Each land pixel's own rock colour.</param>
		/// <param name="art">Whether each land pixel's colour is biome art rather than the physical shading.</param>
		/// <param name="width">Image width; the image wraps east-west.</param>
		/// <param name="height">Image height; rows are clamped at the poles.</param>
		/// <param name="x">The pixel's column.</param>
		/// <param name="y">The pixel's row.</param>
		/// <param name="centreX">The column the neighbourhood is read around: <paramref name="x"/>, warped.</param>
		/// <param name="centreY">The row the neighbourhood is read around: <paramref name="y"/>, warped.</param>
		/// <param name="stretch">Pixels per equator pixel east-west (1 / cos latitude, capped).</param>
		/// <remarks>
		/// <para>
		/// <b>Only across biomes.</b> A neighbour of the pixel's own biome contributes the pixel's own
		/// colours, so nothing inside a biome is blurred — the physical shading of a biome without art
		/// keeps its detail — and a pixel whose whole neighbourhood is one biome comes back exactly
		/// as it went in.
		/// </para>
		/// <para>
		/// <b>Never across a coast.</b> Sea, frozen sea and lava neighbours are skipped, so a coastline
		/// stays exactly where the surface field puts it, as the scenes cut from the globe have it.
		/// </para>
		/// <para>Allocation-free: at most 25 lookups, and none when they all agree.</para>
		/// </remarks>
		public static LandBlend BlendAcrossBiomes(ushort[] ids, Color32[] bases, Color32[] cliffs, bool[] art, int width, int height,
			int x, int y, float centreX, float centreY, float stretch)
		{
			int self = y * width + x;
			ushort own = ids[self];
			Color ownGround = bases[self];
			Color ownCliff = cliffs[self];
			float ownArt = art[self] ? 1f : 0f;

			Color ground = Color.clear;
			Color cliff = Color.clear;
			float artShare = 0f;
			float total = 0f;
			bool mixed = false;
			int k = 0;
			for (int dy = -BlendReachPixels; dy <= BlendReachPixels; dy++)
			{
				int sy = Mathf.Clamp(Mathf.RoundToInt(centreY + dy), 0, height - 1);
				for (int dx = -BlendReachPixels; dx <= BlendReachPixels; dx++, k++)
				{
					int sx = Mathf.RoundToInt(centreX + dx * stretch) % width;
					if (sx < 0)
					{
						sx += width;
					}
					int sample = sy * width + sx;
					ushort id = ids[sample];
					if (id == 0)
					{
						continue;
					}
					float weight = BlendWeights[k];
					if (id == own)
					{
						ground += ownGround * weight;
						cliff += ownCliff * weight;
						artShare += ownArt * weight;
					}
					else
					{
						mixed = true;
						ground += (Color)bases[sample] * weight;
						cliff += (Color)cliffs[sample] * weight;
						artShare += art[sample] ? weight : 0f;
					}
					total += weight;
				}
			}

			if (!mixed || total <= 0f)
			{
				return new LandBlend { Ground = ownGround, Cliff = ownCliff, Art = ownArt };
			}
			return new LandBlend { Ground = ground / total, Cliff = cliff / total, Art = artShare / total };
		}

		/// <summary>The border warp at a point, each axis −1…1 × <see cref="BorderWarpPixels"/>.</summary>
		private static void BorderWarp(uint seed, Vector3 direction, out float x, out float y)
		{
			x = SignedField(seed ^ 0x6A2D4E1Bu, direction, BorderWarpFrequency, 2) * BorderWarpPixels;
			y = SignedField(seed ^ 0x19C3F57Du, direction, BorderWarpFrequency, 2) * BorderWarpPixels;
		}

		/// <summary>Frequency of the colour mottle on the unit sphere: about 210, 105 and 52 km cells on an Earth-sized body.</summary>
		public const float MottleFrequency = 30f;

		/// <summary>How far the mottle moves a land pixel's brightness, either way.</summary>
		/// <remarks>
		/// Four percent: enough that a biome hundreds of kilometres across is not one flat swatch — soil,
		/// stand age and old fire scars vary over exactly that range — and too little to read as a
		/// pattern of its own.
		/// </remarks>
		public const float MottleStrength = 0.04f;

		/// <summary>The low-frequency brightness variation at a point, −1…1.</summary>
		private static float Mottle(uint seed, Vector3 direction)
		{
			return SignedField(seed ^ 0x4F0771E5u, direction, MottleFrequency, 3);
		}

		/// <summary>A field noise as −1…1, stretched as the selection noise is (fBm does not fill its range).</summary>
		private static float SignedField(uint seed, Vector3 direction, float frequency, int octaves)
		{
			float n = PlanetSurface.FieldNoise(seed, direction, frequency, octaves);
			return Mathf.Clamp((n - 0.5f) * 2f * PlanetClimateField.SelectionNoiseStretch, -1f, 1f);
		}

		/// <summary>Earth metres at which ground reads fully "high": the alpine edge, where vegetation thins out.</summary>
		public const float HighGroundMetres = 4500f;

		/// <summary>Brightness of the lowest and the highest ground, against the biome's mean colour.</summary>
		/// <remarks>
		/// Low ground is wetter, more vegetated and darker; high ground drier, thinner-soiled and paler
		/// — so a highland biome shades up toward its summits instead of being one flat colour over
		/// kilometres of height. Measured Earth-like land: median 712 m, 90th percentile 2.1 km, so most
		/// ground sits near the low end and the paling shows on the ranges.
		/// </remarks>
		public const float LowGroundBrightness = 0.96f;
		public const float HighGroundBrightness = 1.08f;

		/// <summary>How much of its colour the highest ground loses toward grey.</summary>
		public const float HighGroundDesaturation = 0.2f;

		/// <summary>The multiplier a fully wet pixel's ground takes: a little greener.</summary>
		public static readonly Color WetTint = new Color(0.94f, 1.03f, 0.94f, 1f);

		/// <summary>The multiplier a fully dry pixel's ground takes: a little browner.</summary>
		public static readonly Color DryTint = new Color(1.05f, 1f, 0.9f, 1f);

		/// <summary>The slope, metres per metre between neighbouring pixels, at which rock starts to show.</summary>
		/// <remarks>
		/// Measured on a 2048-wide bake: Earth-like land has a median of 0.006, a 90th percentile of
		/// 0.018 and a 99th of 0.036; Arthis 0.006 / 0.015 / 0.028. A pixel is about 20 km, so these are
		/// the mean grades of whole massif flanks. Rock starts at 0.015 (the steepest seventh of an
		/// Earth-like world's land) and is full at 0.06, which on Earth-like ground only the steepest
		/// fronts approach. Small moons are far steeper at their finer pixels (crater walls: a 90th
		/// percentile of 0.05–0.1) and show their rock there, as the Moon's bright crater walls do.
		/// </remarks>
		public const float RockSteepnessStart = 0.015f;

		/// <summary>The slope at which a biome's rock shows at full share. See <see cref="RockSteepnessStart"/>.</summary>
		public const float RockSteepnessFull = 0.06f;

		/// <summary>The most of a pixel's colour that is rock: a 20 km pixel is never all cliff.</summary>
		public const float RockShare = 0.5f;

		/// <summary>
		/// A land pixel's colour varied within its biome: paler and greyer high, darker low, greener
		/// where wet and browner where dry, rock on steep ground, a faint mottle — all before relief.
		/// </summary>
		/// <param name="blend">The pixel's blended colours.</param>
		/// <param name="earthAltitudeMetres">Its altitude in Earth metres (the body's metres over its relief scale).</param>
		/// <param name="humidity">Its honest humidity, −1…1.</param>
		/// <param name="living">Whether the world has liquid water: green and brown are a statement about vegetation.</param>
		/// <param name="steepness">Its slope in metres per metre.</param>
		/// <param name="mottle">The low-frequency variation there, −1…1.</param>
		/// <remarks>
		/// <para>
		/// <b>Every term is a smooth function of the ground, never of the biome's own envelope</b>, so
		/// two biomes meeting at a border take the same variation on both sides and the border does not
		/// come back as a step in brightness.
		/// </para>
		/// <para>
		/// <b>Only on biome art.</b> The physical shading already varies with altitude and temperature,
		/// so the variation is scaled by the blend's art share and a biome without art is left alone.
		/// </para>
		/// </remarks>
		public static Color Vary(in LandBlend blend, float earthAltitudeMetres, float humidity, bool living, float steepness, float mottle)
		{
			Color colour = blend.Ground;
			if (blend.Art > 0f)
			{
				float high = Smooth(0f, HighGroundMetres, earthAltitudeMetres);
				float luma = colour.r * 0.299f + colour.g * 0.587f + colour.b * 0.114f;
				Color varied = Color.Lerp(colour, new Color(luma, luma, luma, 1f), HighGroundDesaturation * high);
				varied *= Mathf.Lerp(LowGroundBrightness, HighGroundBrightness, high);
				if (living)
				{
					float wet = Mathf.Clamp(humidity, -1f, 1f);
					varied *= wet >= 0f ? Color.Lerp(Color.white, WetTint, wet) : Color.Lerp(Color.white, DryTint, -wet);
				}
				varied = Color.Lerp(varied, blend.Cliff, Smooth(RockSteepnessStart, RockSteepnessFull, steepness) * RockShare);
				varied *= 1f + MottleStrength * mottle;
				colour = Color.Lerp(colour, varied, blend.Art);
			}
			colour.a = 1f;
			return colour;
		}

		/// <summary>Each biome's colours, read once per bake, and the small ID the border blend compares.</summary>
		private sealed class BiomePalette
		{
			/// <summary>One biome's colours. ID 0 is reserved for "not land".</summary>
			public readonly struct Entry
			{
				public readonly ushort Id;
				public readonly bool HasArt;
				public readonly Color32 Main;
				public readonly Color32 Cliff;

				public Entry(ushort id, bool hasArt, Color32 main, Color32 cliff)
				{
					Id = id;
					HasArt = hasArt;
					Main = main;
					Cliff = cliff;
				}
			}

			private readonly Dictionary<BiomeTemplate, Entry> entries = new Dictionary<BiomeTemplate, Entry>();
			private Entry none;
			private bool hasNone;
			private ushort next;

			/// <summary>The entry for a biome, reading its art the first time it is seen. Null is a biome of its own.</summary>
			public Entry Of(BiomeTemplate biome)
			{
				if (biome == null)
				{
					if (!hasNone)
					{
						none = new Entry(++next, false, default, default);
						hasNone = true;
					}
					return none;
				}
				if (!entries.TryGetValue(biome, out Entry entry))
				{
					bool hasArt = BiomeGroundColours.TryMainColour(biome, BiomeTerrainLayers.Resolve, out Color main);
					Color cliff = hasArt ? BiomeGroundColours.CliffColour(biome, BiomeTerrainLayers.Resolve) : default;
					entry = new Entry(++next, hasArt, main, cliff);
					entries[biome] = entry;
				}
				return entry;
			}
		}

		/// <summary>The whole image's land, as the finishing pass reads it.</summary>
		private sealed class LandPixels
		{
			public readonly ushort[] Ids;
			public readonly Color32[] Bases;
			public readonly Color32[] Cliffs;
			public readonly bool[] Art;
			public readonly float[] Slopes;
			public readonly float[] Steepness;
			public readonly float[] EarthAltitudes;
			public readonly float[] Humidities;

			public LandPixels(int count)
			{
				Ids = new ushort[count];
				Bases = new Color32[count];
				Cliffs = new Color32[count];
				Art = new bool[count];
				Slopes = new float[count];
				Steepness = new float[count];
				EarthAltitudes = new float[count];
				Humidities = new float[count];
			}
		}
	}
}
#endif
