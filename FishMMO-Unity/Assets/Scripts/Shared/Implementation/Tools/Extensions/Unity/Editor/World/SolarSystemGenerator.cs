using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Makes, re-rolls, renames and chooses between the project's solar systems.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A project can hold several systems: the one the game is set in, and others to try things in.
	/// Each lives in a folder of its own under <see cref="WorldEditorAssets.SystemsFolder"/>, with its
	/// bodies beside it, so a system is one thing to move, copy or throw away. Which of them the game
	/// uses is the world atlas's to say (<see cref="SolarSystemProfile.Active"/>), and changing that
	/// is its own deliberate step here, never a side effect of making a new one.
	/// </para>
	/// <para>
	/// Anything removed goes to the trash and not out of existence, so a wrong click is a drag back
	/// out of the bin.
	/// </para>
	/// <para>
	/// A rolled system is plausible, not simulated. It keeps the things that make a sky read as a
	/// sky — rocky worlds close in and giants further out, a home world where its star's light is
	/// about what ours is, moons that mostly keep one face to their planet, rings on the big ones —
	/// and varies everything a designer would want varied: how many suns, how the worlds lean and
	/// which way, what air they have, what is in the sky at night.
	/// </para>
	/// </remarks>
	public static class SolarSystemGenerator
	{
		/// <summary>Asks, then makes another example system beside whatever exists. Null when declined.</summary>
		public static SolarSystemProfile NewExample()
		{
			string name = WorldEditorAssets.UniqueSystemName("Solar System");
			bool first = WorldEditorAssets.FindFirst<SolarSystemProfile>() == null;
			if (!first && !EditorUtility.DisplayDialog("New solar system",
				$"Create another solar system, \"{name}\"?\n\n"
				+ $"It gets a folder of its own at {WorldEditorAssets.SystemFolder(name)}, with the example Sun, Home and Moon 1 in it. "
				+ "The systems you already have are not touched, and the game keeps using the one that is active until you choose otherwise.",
				"Create", "Cancel"))
			{
				return null;
			}
			SolarSystemProfile system = WorldEditorAssets.CreateExampleSystem(name);
			FinishCreating(system, first);
			return system;
		}

		/// <summary>
		/// Asks, then replaces <paramref name="current"/> with a system rolled from a fresh seed. With
		/// nothing selected there is nothing to replace, and one is simply made.
		/// </summary>
		public static SolarSystemProfile ReplaceWithRandom(SolarSystemProfile current)
		{
			bool wasActive = current != null && SolarSystemProfile.Active == current;
			if (current != null)
			{
				int bodies = 0;
				foreach (CelestialBody body in current.Bodies)
				{
					if (body != null)
					{
						bodies++;
					}
				}
				if (!EditorUtility.DisplayDialog("Random solar system",
					$"Replace \"{current.name}\" and its {bodies} bod{(bodies == 1 ? "y" : "ies")} with a randomly generated system?\n\n"
					+ "Its assets are moved to the trash, from where they can be restored. Your other systems are not touched."
					+ (wasActive
						? "\n\nThis is the ACTIVE system, the one the game uses. The new one takes its place, and every scene that stood on one of its bodies moves to the new home world until you place it again."
						: string.Empty),
					"Replace", "Cancel"))
				{
					return null;
				}
				Remove(current);
			}
			bool first = WorldEditorAssets.FindFirst<SolarSystemProfile>() == null;
			// From the clock, and said in the log: the same seed rolls the same system, so one worth
			// keeping can be had again.
			uint seed = (uint)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
			SolarSystemProfile system = Roll(seed);
			FinishCreating(system, first || wasActive);

			/* Surfaces, but no scenes.
			 *
			 * A globe nobody can see is a globe nobody can choose a scene location on, so the
			 * bake belongs with the roll. Scenes do not: a scene is a named place people will live
			 * in, and rolling a dozen of them makes a dozen places nobody chose and somebody has to
			 * curate or delete. They are cut by hand on the atlas, from ground that can be seen.
			 * The bake is build output either way — gitignored, and a client build makes its own. */
			int baked = BakeSurfaces(system);

			Debug.Log($"[Solar system] Rolled \"{system.name}\" from seed {seed}: {system.Bodies.Count} bodies, " +
				$"home world \"{(system.HomeWorld != null ? system.HomeWorld.ResolvedName : "none")}\", {baked} surface(s) baked. " +
				"Cut scenes from it in World \u2192 World Atlas.");
			return system;
		}

		/// <summary>
		/// Bakes a surface for every body in a rolled system that has ground.
		/// </summary>
		/// <remarks>
		/// Failing here must not lose the system: the bodies are real assets and the roll is not
		/// repeatable once the clock has moved on, so a bake that throws is reported and the
		/// system kept.
		/// </remarks>
		private static int BakeSurfaces(SolarSystemProfile system)
		{
			if (system == null)
			{
				return 0;
			}
			int baked = 0;
			try
			{
				for (int i = 0; i < system.Bodies.Count; i++)
				{
					if (system.Bodies[i] is WorldBody world && PlanetSurfaceBaker.HasGround(world))
					{
						EditorUtility.DisplayProgressBar("Rolling a solar system",
							$"Baking {world.ResolvedName}...", (i + 1f) / Mathf.Max(1f, system.Bodies.Count));
						if (PlanetSurfaceBaker.Bake(world) != null)
						{
							baked++;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[Solar system] The surfaces could not all be baked, but the system is fine: {ex.Message}. " +
					"Run Core \u2192 Maintenance \u2192 Bake planet surfaces.");
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}
			return baked;
		}

		/// <summary>A system, its bodies, and its folder if it has one to itself: to the trash.</summary>
		private static void Remove(SolarSystemProfile system)
		{
			var paths = new List<string>();
			foreach (CelestialBody body in system.Bodies)
			{
				string bodyPath = body != null ? AssetDatabase.GetAssetPath(body) : null;
				if (!string.IsNullOrEmpty(bodyPath) && !paths.Contains(bodyPath))
				{
					paths.Add(bodyPath);
				}
				// A body's own cloud stack goes with it, when it has one of its own.
				string cloudPath = body is WorldBody world && world.Clouds != null ? AssetDatabase.GetAssetPath(world.Clouds) : null;
				if (!string.IsNullOrEmpty(cloudPath) && !paths.Contains(cloudPath))
				{
					paths.Add(cloudPath);
				}
			}
			string path = AssetDatabase.GetAssetPath(system);
			string folder = WorldEditorAssets.SystemFolder(system.name);
			bool ownFolder = !string.IsNullOrEmpty(path) && path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
			if (!string.IsNullOrEmpty(path))
			{
				paths.Add(path);
			}
			var failed = new List<string>();
			AssetDatabase.MoveAssetsToTrash(paths.ToArray(), failed);
			foreach (string stuck in failed)
			{
				Debug.LogWarning($"[Solar system] Could not move {stuck} to the trash; it is still in the project.");
			}
			// Its own folder goes too, once it is empty of anything but folders. Not before: a
			// designer may have put textures or notes in there, and those are not ours to bin.
			if (ownFolder && failed.Count == 0 && AssetDatabase.FindAssets(string.Empty, new[] { folder }).Length == AssetDatabase.GetSubFolders(folder).Length)
			{
				AssetDatabase.MoveAssetToTrash(folder);
			}
			AssetDatabase.Refresh();
		}

		private static void FinishCreating(SolarSystemProfile system, bool makeActive)
		{
			if (system == null)
			{
				return;
			}
			WorldEditorAssets.EnsureAtlas(system);
			if (makeActive)
			{
				MakeActive(system);
			}
			EditorUtility.SetDirty(system);
			AssetDatabase.SaveAssets();
		}

		/// <summary>
		/// Makes this the system the game is set in: the atlas names it, and the calendar counts its
		/// home world's days.
		/// </summary>
		public static void MakeActive(SolarSystemProfile system)
		{
			if (system == null)
			{
				return;
			}
			WorldAtlas atlas = WorldEditorAssets.EnsureAtlas(system);
			if (atlas != null && atlas.SolarSystem != system)
			{
				Undo.RecordObject(atlas, "Make solar system active");
				atlas.SolarSystem = system;
				EditorUtility.SetDirty(atlas);
			}
			if (system.Calendar != null && system.HomeWorld != null && system.Calendar.CalendarBody != system.HomeWorld)
			{
				Undo.RecordObject(system.Calendar, "Make solar system active");
				system.Calendar.CalendarBody = system.HomeWorld;
				EditorUtility.SetDirty(system.Calendar);
			}
			AssetDatabase.SaveAssets();
		}

		/// <summary>
		/// Renames a system: its asset, its folder, and the system's name at the front of each of its
		/// bodies' asset names. Returns why not, or null when it is done.
		/// </summary>
		/// <remarks>
		/// A system that was made before systems had folders — loose under the world root, its bodies
		/// in the shared Bodies folder under plain names — is moved into a folder of its own by this,
		/// and its bodies given the system's name, which is also what stops "Home" in one system being
		/// the same cache entry as "Home" in another.
		/// </remarks>
		public static string Rename(SolarSystemProfile system, string newName)
		{
			if (system == null)
			{
				return "No system is selected.";
			}
			newName = (newName ?? string.Empty).Trim();
			if (newName == system.name)
			{
				return null;
			}
			string problem = WorldEditorAssets.SystemNameProblem(newName, system);
			if (problem != null)
			{
				return problem;
			}
			string oldName = system.name;
			string oldFolder = WorldEditorAssets.SystemFolder(oldName);
			string newFolder = WorldEditorAssets.SystemFolder(newName);
			string path = AssetDatabase.GetAssetPath(system);
			bool ownFolder = path.StartsWith(oldFolder + "/", StringComparison.OrdinalIgnoreCase);

			// The folder first, so everything in it — a designer's own files too — comes along.
			if (ownFolder)
			{
				WorldEditorAssets.EnsureFolder(WorldEditorAssets.SystemsFolder);
				string moved = AssetDatabase.MoveAsset(oldFolder, newFolder);
				if (!string.IsNullOrEmpty(moved))
				{
					return $"Could not rename the folder: {moved}";
				}
			}
			else
			{
				WorldEditorAssets.EnsureFolder(WorldEditorAssets.SystemBodiesFolder(newName));
			}

			// The bodies: the system's name at the front of each, in the system's own Bodies folder.
			string bodiesFolder = WorldEditorAssets.SystemBodiesFolder(newName);
			WorldEditorAssets.EnsureFolder(bodiesFolder);
			foreach (CelestialBody body in system.Bodies)
			{
				if (body == null)
				{
					continue;
				}
				string bodyPath = AssetDatabase.GetAssetPath(body);
				string prefix = oldName + " - ";
				string plain = body.name.StartsWith(prefix, StringComparison.Ordinal) ? body.name.Substring(prefix.Length) : body.name;
				string target = AssetDatabase.GenerateUniqueAssetPath($"{bodiesFolder}/{WorldEditorAssets.Sanitize(WorldEditorAssets.BodyAssetName(newName, plain))}.asset");
				if (string.IsNullOrWhiteSpace(body.DisplayName))
				{
					// The plain name was the asset's; keep it as what people see.
					body.DisplayName = plain;
					EditorUtility.SetDirty(body);
				}
				string failure = AssetDatabase.MoveAsset(bodyPath, target);
				if (!string.IsNullOrEmpty(failure))
				{
					Debug.LogWarning($"[Solar system] Could not rename {bodyPath}: {failure}");
				}
				// And its cloud stack, which carries the same prefix and lives beside it.
				if (body is WorldBody world && world.Clouds != null)
				{
					string cloudPath = AssetDatabase.GetAssetPath(world.Clouds);
					string cloudPlain = world.Clouds.name.StartsWith(prefix, StringComparison.Ordinal) ? world.Clouds.name.Substring(prefix.Length) : world.Clouds.name;
					string cloudFolder = newFolder + "/Clouds";
					WorldEditorAssets.EnsureFolder(cloudFolder);
					string cloudTarget = AssetDatabase.GenerateUniqueAssetPath($"{cloudFolder}/{WorldEditorAssets.Sanitize(WorldEditorAssets.BodyAssetName(newName, cloudPlain))}.asset");
					string cloudFailure = AssetDatabase.MoveAsset(cloudPath, cloudTarget);
					if (!string.IsNullOrEmpty(cloudFailure))
					{
						Debug.LogWarning($"[Solar system] Could not rename {cloudPath}: {cloudFailure}");
					}
				}
			}

			// The system itself, last, into its folder under its new name.
			string current = AssetDatabase.GetAssetPath(system);
			string wanted = $"{newFolder}/{WorldEditorAssets.Sanitize(newName)}.asset";
			if (!string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
			{
				string failure = AssetDatabase.MoveAsset(current, wanted);
				if (!string.IsNullOrEmpty(failure))
				{
					return $"The bodies were renamed but the system asset was not: {failure}";
				}
			}
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			return null;
		}

		// ── Rolling a system ──────────────────────────────────────────

		/// <summary>A stream of numbers from a seed. The project's own hash, so a seed is a system for good.</summary>
		private sealed class Dice
		{
			private readonly uint seed;
			private uint drawn;

			public Dice(uint seed)
			{
				this.seed = seed;
			}

			private static uint Hash(uint a, uint b)
			{
				unchecked
				{
					uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u + (a << 6) + (a >> 2));
					h ^= h >> 15;
					h *= 0x2C1B3C6Du;
					h ^= h >> 12;
					h *= 0x297A2D39u;
					h ^= h >> 15;
					return h;
				}
			}

			public uint Bits() => Hash(seed, drawn++);
			public float Unit() => (Bits() & 0xFFFFFF) / (float)0x1000000;
			public float Range(float low, float high) => Mathf.Lerp(low, high, Unit());
			public int Range(int low, int highInclusive) => low + (int)(Bits() % (uint)(highInclusive - low + 1));
			public bool Chance(float probability) => Unit() < probability;
			public T Pick<T>(IReadOnlyList<T> from) => from[(int)(Bits() % (uint)from.Count)];
		}

		private static readonly string[] Openings = { "Ar", "Bel", "Cal", "Dra", "El", "Fen", "Gal", "Hel", "Ith", "Jor", "Kel", "Lor", "Mor", "Nar", "Or", "Pel", "Quor", "Rhe", "Sol", "Tal", "Ul", "Val", "Wen", "Xan", "Yr", "Zel" };
		private static readonly string[] Middles = { "a", "e", "i", "o", "u", "an", "en", "ir", "or", "ae", "ia", "eo" };
		private static readonly string[] Endings = { "dor", "lis", "mar", "nos", "ra", "reth", "ris", "ron", "tha", "this", "var", "ven", "wyn", "x", "on", "is", "us", "ara" };

		private static string Name(Dice dice, HashSet<string> taken)
		{
			for (int attempt = 0; attempt < 32; attempt++)
			{
				string name = dice.Pick(Openings) + (dice.Chance(0.6f) ? dice.Pick(Middles) : string.Empty) + dice.Pick(Endings);
				if (taken.Add(name))
				{
					return name;
				}
			}
			string fallback = "Body " + (taken.Count + 1);
			taken.Add(fallback);
			return fallback;
		}

		/// <summary>A star of a main-sequence class: hotter is bluer, brighter and bigger.</summary>
		private static StarBody RollStar(Dice dice, string folder, string assetName, string name, float brightest)
		{
			// Mostly suns like ours and cooler; a hot blue one now and then.
			float temperature = dice.Chance(0.12f) ? dice.Range(7500f, 14000f) : dice.Range(3200f, 6800f);
			float relative = temperature / StarBody.ReferenceTemperatureK;
			float luminosity = Mathf.Clamp(Mathf.Pow(relative, 5.2f), 0.02f, brightest);
			return WorldEditorAssets.Create<StarBody>(folder, assetName, s =>
			{
				s.DisplayName = name;
				s.TemperatureK = temperature;
				s.Luminosity = luminosity;
				// Radius from L = R^2 T^4: R goes as sqrt(L) over T squared.
				s.SkyRadiusKm = 696000f * Mathf.Clamp(Mathf.Sqrt(luminosity) / (relative * relative), 0.2f, 8f);
				s.Tint = StarBody.TemperatureColor(temperature);
			});
		}

		/// <summary>
		/// A world's cloud stack from its air, its gravity and its warmth: where the deck sits, how deep
		/// the sky is, what stands above. Null for a world with no air, which has no clouds to stack.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The stack is the planetary part of how weather is drawn, so it is rolled with the planet. The
		/// shape follows what makes a real sky: the condensation level rises with a dry, warm air and
		/// falls with a damp cold one; a thick atmosphere is a deep one, so every band sits higher and
		/// the columns reach further; a big world's gravity squashes the sky, a small one's lets it
		/// spread; a giant has no ground and no ground fog — its stack is bands of cloud all the way
		/// down, in its own colours. Every number is a multiple of the home world's, so the home world
		/// rolls the default stack near enough, and only OTHER worlds look different for it.
		/// </para>
		/// </remarks>
		private static CloudStackProfile RollClouds(Dice dice, string folder, string assetName, WorldBody world, bool giant, float distanceRelative)
		{
			if (world.Atmosphere == AtmosphereKind.None)
			{
				return null;
			}
			float air = AtmosphereModel.Density(world.Atmosphere);
			// The sky's height scales with the air's depth and inversely with the pull of the ground.
			float gravity = Mathf.Clamp(world.SkyRadiusKm / 6371f, 0.25f, 4f);
			float height = Mathf.Clamp(Mathf.Pow(air, 0.35f) / Mathf.Pow(gravity, 0.5f), 0.3f, 3.5f);
			// The condensation level: high over dry warm ground, low over damp cold ground.
			float dry = 1f - world.Water;
			float warm = Mathf.Clamp(1f / Mathf.Max(0.3f, distanceRelative), 0.5f, 2f);
			float baseHeight = Mathf.Clamp(800f * height * Mathf.Lerp(0.6f, 2.2f, dry * 0.6f + (warm - 1f) * 0.3f + 0.2f) * dice.Range(0.85f, 1.15f), 150f, 6000f);
			float tint = dice.Range(0f, 1f);
			Color giantShade = giant ? Color.Lerp(world.Tint, Color.white, 0.5f) : new Color(0.62f, 0.68f, 0.82f);

			var layers = new List<CloudLayer>();
			if (!giant)
			{
				layers.Add(new CloudLayer
				{
					Name = "Weather", Bottom = 0f, Top = baseHeight,
					NoiseScale = 2600f * height, DetailScale = 180f, DetailStrength = 0.6f,
					BaseSoftness = 0.5f, TopSoftness = 0.75f, Convection = 0.35f,
					Density = 0.55f * Mathf.Clamp(air, 0.3f, 2f), CoverageScale = 0f, CoverageBias = 0f, WindScale = 0.6f,
					ShadedTint = new Color(0.78f, 0.82f, 0.88f),
				});
			}
			// The columns: the deck and everything a tower can climb to. Thick air is unstable air,
			// and its towers go higher; thin air barely convects.
			float towerTop = baseHeight + 7000f * height * Mathf.Lerp(0.6f, 1.3f, Mathf.Clamp01((air - 0.15f) / 2.85f));
			float convection = giant ? dice.Range(0.6f, 1f) : Mathf.Clamp01(0.5f + 0.3f * (air - 1f) + (warm - 1f) * 0.4f + dice.Range(-0.15f, 0.15f));
			layers.Add(new CloudLayer
			{
				Name = giant ? "Ammonia deck" : "Cumulus", Bottom = giant ? baseHeight * 0.3f : baseHeight, Top = towerTop, Column = true, BaseFollowsCondensation = !giant,
				NoiseScale = 12000f * Mathf.Sqrt(height) * dice.Range(0.8f, 1.25f), DetailScale = 380f, DetailStrength = 0.45f,
				BaseSoftness = giant ? 0.2f : 0.06f, TopSoftness = 0.65f, Convection = convection,
				Density = giant ? dice.Range(0.9f, 1.4f) : Mathf.Clamp(air, 0.35f, 1.5f), CoverageScale = 1f,
				CoverageBias = giant ? dice.Range(0.25f, 0.5f) : 0f, WindScale = 1f,
				Stretch = giant ? dice.Range(3f, 9f) : 1f,
				CarriesRain = !giant, GrowsStorms = true,
				ShadedTint = giantShade,
			});
			// A middle sheet, most skies; a giant's is a second band of a different colour.
			if (giant || dice.Chance(0.8f))
			{
				float altoBottom = baseHeight + (towerTop - baseHeight) * dice.Range(0.35f, 0.5f);
				layers.Add(new CloudLayer
				{
					Name = giant ? "Upper haze" : "Alto", Bottom = altoBottom, Top = altoBottom + 1000f * height,
					NoiseScale = 9000f * dice.Range(0.8f, 1.3f), DetailScale = 700f, DetailStrength = 0.25f,
					BaseSoftness = 0.3f, TopSoftness = 0.4f, Convection = 0.2f,
					Density = giant ? 0.7f : 0.5f, CoverageScale = 1f, CoverageOnset = giant ? 0.2f : 0.55f, WindScale = 1.7f,
					Stretch = giant ? dice.Range(4f, 12f) : 1f,
					ShadedTint = giant ? Color.Lerp(world.Tint, new Color(1f, 0.95f, 0.85f), 0.6f) : new Color(0.66f, 0.7f, 0.8f),
				});
			}
			// High ice, where the air is deep enough to hold any: thin air has no cirrus.
			if (air >= 0.5f && (giant || dice.Chance(0.85f)))
			{
				float cirrusBottom = towerTop * dice.Range(0.94f, 1.02f);
				layers.Add(new CloudLayer
				{
					Name = "Cirrus", Bottom = cirrusBottom, Top = cirrusBottom + 1100f * height,
					NoiseScale = 14000f, DetailScale = 1200f, DetailStrength = 0.2f, Stretch = dice.Range(5f, 12f),
					BaseSoftness = 0.35f, TopSoftness = 0.45f,
					Density = 0.18f, CoverageScale = -0.35f, CoverageBias = 0.3f, WindScale = 3f,
					ShadedTint = new Color(0.8f, 0.84f, 0.92f),
				});
			}
			return WorldEditorAssets.Create<CloudStackProfile>(folder, assetName, c => c.Layers = layers);
		}

		/// <summary>
		/// Rolls a whole system from a seed and creates its assets. The same seed gives the same system.
		/// </summary>
		public static SolarSystemProfile Roll(uint seed)
		{
			var dice = new Dice(seed);
			var taken = new HashSet<string>();
			CalendarProfile calendar = WorldEditorAssets.FindFirst<CalendarProfile>() ?? WorldEditorAssets.Create<CalendarProfile>(WorldEditorAssets.Root, "World Calendar");
			var bodies = new List<CelestialBody>();

			// ── The suns ──
			// One, most often. A second goes round the first; a third goes round the pair, further out.
			string primaryName = Name(dice, taken);
			string systemName = WorldEditorAssets.UniqueSystemName(primaryName + " System");
			string folder = WorldEditorAssets.SystemBodiesFolder(systemName);
			string Asset(string body) => WorldEditorAssets.BodyAssetName(systemName, body);
			StarBody primary = RollStar(dice, folder, Asset(primaryName), primaryName, 40f);
			bodies.Add(primary);
			int suns = dice.Chance(0.68f) ? 1 : dice.Chance(0.82f) ? 2 : 3;
			for (int i = 1; i < suns; i++)
			{
				string name = primaryName + (i == 1 ? " B" : " C");
				taken.Add(name);
				// Never brighter than the primary: the primary is the one the home world's year is measured round.
				StarBody companion = RollStar(dice, folder, Asset(name), name, Mathf.Max(0.02f, primary.Luminosity * 0.8f));
				float separation = i == 1 ? dice.Range(0.08f, 0.35f) : dice.Range(18f, 40f);
				companion.Orbit = new OrbitSettings
				{
					Distance = separation,
					Eccentricity = dice.Range(0f, 0.25f),
					InclinationDegrees = dice.Range(-6f, 6f),
					OffsetDegrees = dice.Range(0f, 360f),
					PeriodMode = OrbitPeriodMode.Kepler,
					PeriodDays = 30f,
				};
				EditorUtility.SetDirty(companion);
				bodies.Add(companion);
			}

			// ── The worlds ──
			// Spaced like a real system: each orbit a fixed ratio outside the last. The habitable
			// distance is where the primary's light is what ours is at one unit: the square root of
			// its luminosity. Inside the first orbit a close pair of suns would be in the way, so
			// the worlds begin outside it and go round both.
			float habitable = Mathf.Sqrt(Mathf.Max(0.02f, primary.Luminosity));
			int planets = dice.Range(3, 8);
			float spacing = dice.Range(1.45f, 1.9f);
			int homeIndex = dice.Range(1, Mathf.Min(3, planets - 1));
			float first = habitable / Mathf.Pow(spacing, homeIndex);
			if (suns >= 2)
			{
				first = Mathf.Max(first, 1.2f);
			}
			// Past this the star's light is too thin to keep water liquid or air from freezing out.
			float frost = habitable * 2.7f;
			WorldBody home = null;
			var giants = new List<WorldBody>();
			float beltInner = 0f, beltOuter = 0f;

			for (int i = 0; i < planets; i++)
			{
				float distance = first * Mathf.Pow(spacing, i) * dice.Range(0.94f, 1.06f);
				bool isHome = i == homeIndex;
				bool giant = !isHome && distance > frost && dice.Chance(0.75f);
				// A gap between the rocky worlds and the giants is where a belt gathers.
				if (giant && giants.Count == 0 && i > 0 && beltOuter <= 0f)
				{
					beltInner = first * Mathf.Pow(spacing, i - 1) * 1.15f;
					beltOuter = distance * 0.85f;
				}
				string name = Name(dice, taken);
				float tilt = dice.Chance(0.1f) ? dice.Range(55f, 88f) : dice.Range(0f, 34f);
				WorldBody world = WorldEditorAssets.Create<WorldBody>(folder, Asset(name), b =>
				{
					b.DisplayName = name;
					// The roll already knew; it simply had nowhere to record it until GasGiant existed.
					b.Kind = giant ? WorldBodyKind.GasGiant
						: !isHome && dice.Chance(0.15f) ? WorldBodyKind.DwarfPlanet
						: WorldBodyKind.Planet;
					// To the primary whatever happens: a world belongs to a star, and with a close
					// pair the two are near enough together that going round one is going round both.
					b.Parent = primary;
					b.Orbit = new OrbitSettings
					{
						Distance = distance,
						Eccentricity = isHome ? dice.Range(0f, 0.04f) : dice.Range(0f, 0.18f),
						InclinationDegrees = isHome ? 0f : dice.Range(-4f, 4f),
						OffsetDegrees = dice.Range(0f, 360f),
						PeriodMode = OrbitPeriodMode.Kepler,
						PeriodDays = 365f,
					};
					// The home world keeps the six-hour day the game's clock is built on.
					b.RotationHours = isHome ? 6f : giant ? dice.Range(2.5f, 5f) : dice.Range(4f, 40f);
					b.Retrograde = !isHome && dice.Chance(0.08f);
					b.AxialTiltDegrees = isHome ? dice.Range(8f, 32f) : tilt;
					b.PoleLongitudeDegrees = dice.Range(0f, 360f);
					b.RotationOffsetDegrees = dice.Range(0f, 360f);
					/* Rolled from the system's own dice, so the whole system — orbits, sizes and
					 * every coastline on every world — comes back from the one seed in the log. */
					b.TerrainSeed = unchecked((uint)dice.Range(1, int.MaxValue));
					b.SkyRadiusKm = giant ? dice.Range(24000f, 72000f) : b.Kind == WorldBodyKind.DwarfPlanet ? dice.Range(600f, 1800f) : dice.Range(2400f, 9000f);
					if (isHome)
					{
						b.Atmosphere = AtmosphereKind.Standard;
						b.Water = dice.Range(0.45f, 0.85f);
						b.Tint = new Color(dice.Range(0.25f, 0.45f), dice.Range(0.45f, 0.65f), dice.Range(0.7f, 0.9f), 1f);
					}
					else if (giant)
					{
						b.Atmosphere = AtmosphereKind.Thick;
						b.Water = 0f;
						b.Tint = Color.HSVToRGB(dice.Chance(0.5f) ? dice.Range(0.06f, 0.14f) : dice.Range(0.5f, 0.62f), dice.Range(0.25f, 0.55f), dice.Range(0.7f, 0.95f));
						b.HasRings = dice.Chance(0.45f);
					}
					else
					{
						// Small worlds lose their air; those near the star have it boiled off, those far
						// out have it frozen to the ground.
						bool small = b.SkyRadiusKm < 3200f;
						bool temperate = distance > habitable * 0.6f && distance < frost;
						b.Atmosphere = small ? (dice.Chance(0.7f) ? AtmosphereKind.None : AtmosphereKind.Thin)
							: temperate ? dice.Pick(new[] { AtmosphereKind.Thin, AtmosphereKind.Standard, AtmosphereKind.Thick })
							: dice.Pick(new[] { AtmosphereKind.None, AtmosphereKind.Thin, AtmosphereKind.Thick });
						b.Water = b.Atmosphere == AtmosphereKind.None ? 0f : temperate ? dice.Range(0f, 0.6f) : dice.Range(0f, 0.1f);
						b.Tint = Color.HSVToRGB(dice.Range(0.02f, 0.12f), dice.Range(0.15f, 0.6f), dice.Range(0.45f, 0.85f));
						b.HasRings = dice.Chance(0.04f);
					}
					// Dust: a dry world is a dusty one, and its dust is the colour of its ground. A wet
					// world's haze is water, and white.
					if (b.Atmosphere != AtmosphereKind.None && !isHome)
					{
						bool dry = b.Water < 0.15f;
						b.Haze = giant ? dice.Range(1f, 4f) : dry ? dice.Range(2f, 7f) : dice.Range(0.6f, 1.8f);
						b.HazeColor = dry && !giant ? Color.Lerp(Color.white, b.Tint, 0.8f) : giant ? Color.Lerp(Color.white, b.Tint, 0.5f) : Color.white;
					}
					// A field wants a molten, turning core: big worlds that spin have one, small ones have
					// cooled, slow ones never wound one up. Giants have the strongest of all.
					float spin = Mathf.Clamp01(12f / Mathf.Max(1f, b.RotationHours));
					b.MagneticField = isHome ? dice.Range(0.8f, 1.2f)
						: giant ? dice.Range(1.2f, 2f)
						: b.SkyRadiusKm < 3200f ? (dice.Chance(0.75f) ? 0f : dice.Range(0.05f, 0.3f))
						: Mathf.Clamp(dice.Range(0.2f, 1.3f) * Mathf.Lerp(0.3f, 1f, spin), 0f, 2f);
					// The dipole leans off the spin axis by a little, as ours does; now and then by a lot.
					b.MagneticPoleTiltDegrees = dice.Chance(0.12f) ? dice.Range(20f, 45f) : dice.Range(2f, 15f);
					b.MagneticPoleLongitudeDegrees = dice.Range(0f, 360f);
					if (b.HasRings)
					{
						float inner = dice.Range(1.2f, 1.7f);
						b.Rings = new RingSettings
						{
							InnerRadius = inner,
							OuterRadius = inner + dice.Range(0.5f, 1.4f),
							Bands = dice.Range(1, 9),
							Opacity = dice.Range(0.35f, 0.8f),
							Tint = Color.HSVToRGB(dice.Range(0.06f, 0.14f), dice.Range(0.08f, 0.35f), dice.Range(0.7f, 0.95f)),
						};
					}
					b.MinimumRadiusKm = isHome ? 30f : 10f;
					b.CurrentRadiusKm = b.MinimumRadiusKm;
				});
				// Its clouds, from the world it turned out to be. Not the home world's: that keeps the
				// default stack, which is the one everything was tuned against.
				if (!isHome)
				{
					world.Clouds = RollClouds(dice, WorldEditorAssets.SystemFolder(systemName) + "/Clouds", Asset(name + " Clouds"), world, giant, distance / Mathf.Max(1e-4f, habitable));
					EditorUtility.SetDirty(world);
				}
				bodies.Add(world);
				if (isHome)
				{
					home = world;
				}
				if (giant)
				{
					giants.Add(world);
				}

				// ── Its moons ──
				int moons = isHome ? dice.Range(1, 2) : giant ? dice.Range(1, 4) : dice.Chance(0.35f) ? 1 : 0;
				float moonDistance = world.SkyRadiusKm * dice.Range(8f, 20f) / 1000f;
				for (int m = 0; m < moons; m++)
				{
					string moonName = Name(dice, taken);
					float thisDistance = moonDistance;
					WorldBody moon = WorldEditorAssets.Create<WorldBody>(folder, Asset(moonName), b =>
					{
						b.DisplayName = moonName;
						b.Kind = WorldBodyKind.Moon;
						b.Parent = world;
						b.Orbit = new OrbitSettings
						{
							// A moon's distance is in thousands of kilometres.
							Distance = thisDistance,
							Eccentricity = dice.Range(0f, 0.08f),
							InclinationDegrees = dice.Range(-8f, 8f),
							OffsetDegrees = dice.Range(0f, 360f),
							PeriodMode = OrbitPeriodMode.Authored,
							// Further out goes round slower, by the same law as the planets.
							PeriodDays = Mathf.Max(1.5f, 7.5f * Mathf.Pow(thisDistance / 384f, 1.5f) * dice.Range(0.8f, 1.25f)),
						};
						b.TidallyLocked = dice.Chance(0.85f);
						b.RotationHours = dice.Range(6f, 60f);
						b.AxialTiltDegrees = dice.Range(0f, 8f);
						b.PoleLongitudeDegrees = dice.Range(0f, 360f);
						b.SkyRadiusKm = Mathf.Min(world.SkyRadiusKm * 0.35f, dice.Range(400f, 2600f));
						// A moon big enough to hold air is the exception worth having.
						b.Atmosphere = b.SkyRadiusKm > 2200f && dice.Chance(0.35f) ? AtmosphereKind.Thin : AtmosphereKind.None;
						// Moons are small and mostly dead inside; the odd large one keeps a weak field.
						b.MagneticField = b.SkyRadiusKm > 2000f && dice.Chance(0.3f) ? dice.Range(0.05f, 0.4f) : 0f;
						b.Water = 0f;
						float grey = dice.Range(0.5f, 0.85f);
						b.Tint = new Color(grey, grey * dice.Range(0.92f, 1f), grey * dice.Range(0.85f, 1f), 1f);
						b.MinimumRadiusKm = 10f;
						b.CurrentRadiusKm = 10f;
					});
					if (moon.Atmosphere != AtmosphereKind.None)
					{
						moon.Clouds = RollClouds(dice, WorldEditorAssets.SystemFolder(systemName) + "/Clouds", Asset(moonName + " Clouds"), moon, false, distance / Mathf.Max(1e-4f, habitable));
						EditorUtility.SetDirty(moon);
					}
					bodies.Add(moon);
					moonDistance *= dice.Range(1.6f, 2.4f);
				}
			}

			// ── Comets ──
			int comets = dice.Range(0, 2);
			for (int i = 0; i < comets; i++)
			{
				string name = Name(dice, taken);
				CometBody comet = WorldEditorAssets.Create<CometBody>(folder, Asset(name), c =>
				{
					c.DisplayName = name;
					c.Parent = primary;
					c.Orbit = new OrbitSettings
					{
						Distance = habitable * dice.Range(6f, 16f),
						Eccentricity = dice.Range(0.82f, 0.96f),
						InclinationDegrees = dice.Range(-40f, 40f),
						OffsetDegrees = dice.Range(0f, 360f),
						PeriodMode = OrbitPeriodMode.Kepler,
						PeriodDays = 365f,
					};
					c.SkyRadiusKm = dice.Range(3f, 20f);
					c.TailLengthMillionKm = dice.Range(8f, 45f);
					c.Brightness = dice.Range(0.6f, 1.6f);
					c.Tint = new Color(0.9f, 0.95f, 1f, 1f);
				});
				bodies.Add(comet);
			}

			// ── The system ──
			SolarSystemProfile system = WorldEditorAssets.Create<SolarSystemProfile>(WorldEditorAssets.SystemFolder(systemName), systemName, p =>
			{
				p.Bodies.AddRange(bodies);
				p.HomeWorld = home;
				p.Calendar = calendar;
				p.StarSeed = dice.Bits();
				p.WeatherSeed = dice.Bits();
				p.SporadicMeteorsPerHour = dice.Range(2f, 12f);
				if (beltOuter > beltInner && beltInner > 0f && dice.Chance(0.8f))
				{
					p.AsteroidBelts.Add(new AsteroidBelt
					{
						Name = Name(dice, taken) + " Belt",
						InnerAU = beltInner,
						OuterAU = beltOuter,
						Count = dice.Range(800, 3000),
						ThicknessDegrees = dice.Range(3f, 10f),
						Seed = (int)(dice.Bits() & 0x7FFFFFFF),
					});
				}
				int showers = dice.Range(1, 3);
				for (int i = 0; i < showers; i++)
				{
					p.MeteorShowers.Add(new MeteorShower
					{
						Name = Name(dice, taken) + "ids",
						PeakDayOfYear = dice.Range(1, Mathf.Max(1, p.DaysPerYear)),
						HalfWidthDays = dice.Range(1.5f, 6f),
						PeakPerHour = dice.Range(25f, 140f),
						RadiantRightAscension = dice.Range(0f, 360f),
						RadiantDeclination = dice.Range(-60f, 75f),
					});
				}
			});
			return system;
		}
	}
}
