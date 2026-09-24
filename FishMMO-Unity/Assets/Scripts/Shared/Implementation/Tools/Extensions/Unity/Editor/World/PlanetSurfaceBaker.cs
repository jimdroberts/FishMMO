#if UNITY_EDITOR
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
			SolarSystemProfile system = WorldEditorAssets.FindFirst<SolarSystemProfile>();
			PlanetClimateField climate = PlanetClimateField.For(system, body);

			uint seed = climate.Seed;
			float cratering = climate.Cratering;
			PlanetSurface.PlanetProfile profile = climate.Profile;
			float relief = climate.ReliefMetres;
			// What the world makes for itself: lava on a tormented moon, a fractured shell on a
			// frozen one. Nothing to do with how far it sits from its star.
			float internalHeat = climate.InternalHeat;

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
			bool hasPreviousRow = false;

			for (int y = 0; y < height; y++)
			{
				/* Pixel centres. Sampling the exact pole would make the whole top row one point,
				 * and every longitude there would return the same colour — a visible bar of flat
				 * colour across the top of every planet. */
				double latitude = 90.0 - (y + 0.5) * 180.0 / height;
				int row = y * Width;
				float previousHeight = 0f;
				bool hasPreviousHeight = false;
				for (int x = 0; x < Width; x++)
				{
					double longitude = (x + 0.5) * 360.0 / Width - 180.0;
					float h = PlanetSurface.HeightAt(seed, latitude, longitude, cratering);
					/* Temperature here: the globe's mean, cooled toward the pole by the sun's noon
					 * altitude, and cooled again by how far above sea level the ground stands. The
					 * same three terms the running game uses. */
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
					float slope = ((east / cosLat) * 0.7f + north * 0.7f) * polarFade;

					previousHeight = h;
					hasPreviousHeight = true;
					previousRow[x] = h;

					pixels[row + x] = Shade(h, profile, body, temperature, slope, internalHeat, direction, seed);
				}
				hasPreviousRow = true;
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

		private static Color32 Shade(float h, PlanetSurface.PlanetProfile profile, WorldBody body, float temperature,
			float slope, float internalHeat, Vector3 direction, uint seed)
		{
			bool hasOcean = body.Water > 0f;
			bool airless = body.Atmosphere == AtmosphereKind.None;
			Color colour;

			if (hasOcean && h <= profile.SeaLevel)
			{
				float depth = Mathf.Clamp01((profile.SeaLevel - h) / Mathf.Max(1e-4f, profile.SeaLevel - profile.Lowest));
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
					bool icy = body.Water > 0.05f && temperature <= -0.35f;

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

						// Molten ground glows in the lowest places, where the crust is thinnest.
						if (internalHeat > 0.8f)
						{
							colour = Color.Lerp(colour, new Color(0.95f, 0.35f, 0.10f),
								Smooth(0.14f, 0f, above) * 0.8f);
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
			float strength = body.Atmosphere == AtmosphereKind.None ? 14f : 9f;
			colour *= Mathf.Clamp(1f + slope * strength, 0.6f, 1.45f);

			return new Color32(
				(byte)(Mathf.Clamp01(colour.r) * 255f),
				(byte)(Mathf.Clamp01(colour.g) * 255f),
				(byte)(Mathf.Clamp01(colour.b) * 255f),
				255);
		}
	}
}
#endif
