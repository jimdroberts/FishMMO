using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

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
			Debug.Log($"[Solar system] Rolled \"{system.name}\" from seed {seed}: {system.Bodies.Count} bodies, home world \"{(system.HomeWorld != null ? system.HomeWorld.ResolvedName : "none")}\".");
			return system;
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
					b.Kind = !giant && !isHome && dice.Chance(0.15f) ? WorldBodyKind.DwarfPlanet : WorldBodyKind.Planet;
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
