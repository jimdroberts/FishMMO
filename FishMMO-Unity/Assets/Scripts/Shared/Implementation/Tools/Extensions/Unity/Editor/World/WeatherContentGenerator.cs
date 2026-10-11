#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Creates the default weather content — the substances that fall, blow and lie — and gives the
	/// biomes that have one the ground the air meets there: what the wind can lift off it and what
	/// it puts into the air by itself. Existing substances, and biomes whose ground is already
	/// authored, are never replaced.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>There used to be much more here.</b> A layer template for every kind of weather, a couple
	/// of dozen named presets, and a weather profile on every biome saying which presets spawned
	/// over it and how often. All of that went with the presets themselves: the weather is worked
	/// out from the air now, on the world's own physics, and a preset could only ever disagree with
	/// it. What a biome still has to say is what the physics cannot guess — a desert and a steppe
	/// can have exactly the same air, and only one of them has sand lying about to be lifted.
	/// </para>
	/// <para>
	/// <b>The ground table was derived, not invented.</b> Each biome's old profile named the
	/// substances its storms and background layers carried; sorted by what the substance is, sand
	/// and dust became the loose ground, and ash, sulphur, tholin and cryovolcanic ice became what
	/// the ground emits. Rain and snow of every condensate were dropped: the physics picks those
	/// from what the world's clouds are made of, and nobody assigns them.
	/// </para>
	/// </remarks>
	public static class WeatherContentGenerator
	{
		public const string Root = "Assets/Templates/Weather";
		public const string SubstancesFolder = Root + "/Substances";

		/// <summary>What one run did.</summary>
		public sealed class Report
		{
			public int Substances;
			public int Biomes;
			public readonly List<string> Skipped = new List<string>();
			public override string ToString() =>
				$"{Substances} substance(s) created and {Biomes} biome ground(s) filled; {Skipped.Count} left as authored.";
		}

		[DashboardTool(DashboardToolAttribute.Weather, "Create default weather content", Section = "Content", Order = 0,
			Tooltip = "Creates the weather substances under Assets/Templates/Weather/Substances, and gives every biome in the shipped ground table its loose ground and emissions if it has none. Authored content is left alone.")]
		public static void GenerateFromDashboard()
		{
			Report report = Generate();
			Debug.Log("[Weather content] " + report);
			foreach (string line in report.Skipped)
			{
				Debug.Log("[Weather content] kept: " + line);
			}
		}

		/// <summary>
		/// Creates the missing substances under <paramref name="root"/>, and with
		/// <paramref name="fillBiomes"/> gives every biome in the ground table its ground if it has
		/// none yet.
		/// </summary>
		public static Report Generate(string root = Root, bool fillBiomes = true)
		{
			var report = new Report();
			Dictionary<string, WeatherSubstance> substances = EnsureSubstances(root + "/Substances", report);

			if (fillBiomes)
			{
				foreach (BiomeTemplate biome in WorldEditorAssets.FindAll<BiomeTemplate>())
				{
					if (!Ground.TryGetValue(biome.name, out GroundSpec spec))
					{
						// Rock, soil, vegetation, ice or water: nothing to lift and nothing coming out.
						continue;
					}
					if (HasGround(biome))
					{
						report.Skipped.Add($"biome {biome.name}");
						continue;
					}
					Undo.RecordObject(biome, "Biome ground");
					ApplyGround(biome, spec, substances);
					EditorUtility.SetDirty(biome);
					report.Biomes++;
				}
			}
			AssetDatabase.SaveAssets();
			return report;
		}

		[DashboardTool(DashboardToolAttribute.Weather, "Update substance physics and biome ground to the shipped ones", Section = "Content", Order = 4,
			Tooltip = "Re-applies the shipped physical facts — what each substance condenses out of, whether it is frozen, how big and dense its grains are — to every substance, and the shipped loose ground and emissions to every biome in the table. Use it after the shipped content changes; it OVERWRITES hand edits to those fields. Looks, cover and harshness are left alone.",
			Confirm = "Overwrite the physical fields of every weather substance, and the loose ground and emissions of every biome in the shipped table? Hand edits to those fields will be lost.")]
		public static void RefreshFromDashboard()
		{
			var report = new Report();
			Dictionary<string, WeatherSubstance> substances = EnsureSubstances(SubstancesFolder, report);

			var physics = new List<string>();
			foreach (WeatherSubstance substance in WorldEditorAssets.FindAll<WeatherSubstance>())
			{
				SubstanceSpec spec = Array.Find(Substances, candidate => candidate.Name == substance.name);
				if (spec == null)
				{
					continue;
				}
				Undo.RecordObject(substance, "Substance physics");
				ApplyPhysics(substance, spec);
				EditorUtility.SetDirty(substance);
				physics.Add(substance.name);
			}

			var ground = new List<string>();
			foreach (BiomeTemplate biome in WorldEditorAssets.FindAll<BiomeTemplate>())
			{
				if (!Ground.TryGetValue(biome.name, out GroundSpec spec))
				{
					continue;
				}
				Undo.RecordObject(biome, "Biome ground");
				ApplyGround(biome, spec, substances);
				EditorUtility.SetDirty(biome);
				ground.Add(biome.name);
			}
			AssetDatabase.SaveAssets();
			Debug.Log($"[Weather content] {physics.Count} substance(s) and {ground.Count} biome ground(s) back on the shipped content; {report.Substances} missing substance(s) created." +
				(physics.Count > 0 ? $"\n  Substances: {string.Join(", ", physics)}" : string.Empty) +
				(ground.Count > 0 ? $"\n  Biomes: {string.Join(", ", ground)}" : string.Empty));
		}

		// ── Substances ──

		/// <summary>One shipped substance: its look and what it leaves, and what it physically is.</summary>
		private sealed class SubstanceSpec
		{
			public string Name;
			public string Description;
			public Color Tint;
			public Color FogColor;
			public float FallSpeedScale = 1f;
			public float StretchScale = 1f;
			public float Emission;
			public WeatherCoverKind Cover;
			public Color CoverTint = Color.white;
			public float MeltsAbove = 2f;
			public bool Breathable;
			public float Harshness;

			public bool Condenses;
			public Condensate Condensate;
			public bool Frozen;
			public float GrainMetres = 1e-4f;
			public float GrainDensity = 2500f;
			public bool Vapour;
		}

		private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

		/// <summary>A world's cloud stuff coming down, as a drop a millimetre across of the condensate's own density.</summary>
		private static SubstanceSpec Condensed(string name, Condensate condensate, bool frozen, float density)
			=> new SubstanceSpec { Name = name, Condenses = true, Condensate = condensate, Frozen = frozen, GrainMetres = 1e-3f, GrainDensity = density };

		/// <summary>Something that is not cloud: lifted off the ground, or put out by it.</summary>
		private static SubstanceSpec Grain(string name, float metres, float density)
			=> new SubstanceSpec { Name = name, GrainMetres = metres, GrainDensity = density };

		/// <summary>A vapour off hot ground, seen as the drops it condenses into where it meets colder air: cloud drops of ten microns.</summary>
		private static SubstanceSpec Vapour(string name)
			=> new SubstanceSpec { Name = name, Vapour = true, GrainMetres = 1e-5f, GrainDensity = 1000f };

		/* The grains are measured ones, not tuned ones. Sand is a fifth of a millimetre of quartz;
		 * lunar regolith dust is finer and heavier, basalt glass at about 70 microns; volcanic ash a
		 * tenth of a millimetre of porous glass; tholin is photochemical smog, a couple of microns of
		 * something like tar. The condensates fall as millimetre drops and flakes of their own
		 * liquid or ice — water 1000 and 917, ammonia 682 and 817, methane 423 and 500, nitrogen 807
		 * and 1027, sulphuric acid 1830 — which is what the physics needs to know about them. */
		private static readonly SubstanceSpec[] Substances =
		{
			With(Condensed("Water Rain", Condensate.Water, false, 1000f), "Ordinary rain. The default every Earth-like world already assumes.",
				C(0.78f, 0.83f, 0.92f), C(0.55f, 0.6f, 0.66f), 1f, 1f, 0f, WeatherCoverKind.Wet, Color.white, 2f, true, 0.1f),
			With(Condensed("Water Snow", Condensate.Water, true, 917f), "Ordinary snow.",
				Color.white, C(0.82f, 0.85f, 0.9f), 1f, 1f, 0f, WeatherCoverKind.Snow, Color.white, 0.1f, true, 0.2f),
			With(Condensed("Ammonia Hail", Condensate.Ammonia, true, 817f), "Ammonia ice, hard and startlingly cold.",
				C(0.85f, 0.9f, 0.82f), C(0.65f, 0.7f, 0.64f), 0.95f, 1f, 0f, WeatherCoverKind.Snow, C(0.8f, 0.86f, 0.8f), -0.55f, false, 0.9f),
			With(Condensed("Methane Drizzle", Condensate.Methane, false, 423f), "Liquid methane, falling slow and heavy in the cold.",
				C(0.62f, 0.56f, 0.38f), C(0.42f, 0.38f, 0.26f), 0.7f, 1.4f, 0f, WeatherCoverKind.Wet, C(0.5f, 0.45f, 0.3f), -0.6f, false, 0.8f),
			With(Condensed("Nitrogen Snow", Condensate.Nitrogen, true, 1027f), "Nitrogen frozen out of the air. A flake of it falls much as one of water snow does; on a thin-aired world both fall faster, and that is the world's doing.",
				C(0.88f, 0.93f, 1f), C(0.72f, 0.8f, 0.9f), 1.05f, 1.2f, 0f, WeatherCoverKind.Snow, C(0.9f, 0.95f, 1f), -0.7f, false, 0.85f),
			With(Condensed("Acid Rain", Condensate.SulphuricAcid, false, 1830f), "Sulphuric rain under a runaway greenhouse. It never reaches the ground on Venus; here it does.",
				C(0.85f, 0.82f, 0.45f), C(0.62f, 0.58f, 0.3f), 1.3f, 1f, 0.05f, WeatherCoverKind.Wet, C(0.8f, 0.78f, 0.5f), 2f, false, 0.9f),
			With(Grain("Silicate Sand", 2e-4f, 2650f), "Ordinary wind-blown sand.",
				C(0.9f, 0.78f, 0.58f), C(0.78f, 0.66f, 0.46f), 1f, 1f, 0f, WeatherCoverKind.Sand, C(0.85f, 0.74f, 0.54f), 2f, true, 0.4f),
			With(Grain("Regolith Dust", 7e-5f, 3100f), "Sharp, electrostatic dust that clings to everything and never settles properly.",
				C(0.62f, 0.6f, 0.56f), C(0.5f, 0.48f, 0.45f), 0.5f, 1f, 0f, WeatherCoverKind.Sand, C(0.55f, 0.53f, 0.5f), 2f, false, 0.65f),
			With(Grain("Irradiated Dust", 5e-5f, 2800f), "Dust lit from within by a giant's radiation belt. Faintly, unpleasantly green.",
				C(0.68f, 0.82f, 0.55f), C(0.45f, 0.55f, 0.4f), 0.6f, 1f, 0.35f, WeatherCoverKind.Sand, C(0.55f, 0.62f, 0.45f), 2f, false, 1f),
			With(Grain("Sulphur Dust", 1e-4f, 2070f), "Yellow crystalline dust off the vents.",
				C(0.9f, 0.84f, 0.35f), C(0.72f, 0.66f, 0.28f), 0.8f, 1f, 0.05f, WeatherCoverKind.Ash, C(0.85f, 0.8f, 0.35f), 2f, false, 0.75f),
			With(Grain("Tholin Haze", 2e-6f, 1400f), "Organic haze settling as a rust-coloured crust.",
				C(0.72f, 0.48f, 0.32f), C(0.55f, 0.36f, 0.24f), 0.6f, 1f, 0f, WeatherCoverKind.Ash, C(0.6f, 0.4f, 0.28f), 2f, false, 0.5f),
			With(Grain("Volcanic Ash", 1e-4f, 2400f), "Hot mineral ash. The default ashfall.",
				C(0.55f, 0.53f, 0.5f), C(0.42f, 0.4f, 0.38f), 1f, 1f, 0.05f, WeatherCoverKind.Ash, C(0.35f, 0.34f, 0.33f), 2f, false, 0.6f),
			With(Grain("Cryo Tephra", 2e-4f, 920f), "Ice thrown out by a cryovolcano and drifting back down. Ash in how it falls, snow in what it leaves.",
				C(0.8f, 0.88f, 0.94f), C(0.62f, 0.72f, 0.8f), 0.55f, 1f, 0.02f, WeatherCoverKind.Snow, C(0.86f, 0.92f, 0.96f), -0.5f, false, 0.7f),
			With(Vapour("Steam"), "Water vapour off hot springs, fumaroles and geysers: white where it mixes into colder air, gone again once the drier air has evaporated it. It never falls and never lies.",
				C(0.96f, 0.97f, 0.98f), C(0.86f, 0.88f, 0.9f), 1f, 1f, 0f, WeatherCoverKind.None, Color.white, 2f, true, 0.1f),
		};

		private static SubstanceSpec With(SubstanceSpec spec, string description, Color tint, Color fog, float fallSpeed, float stretch,
			float emission, WeatherCoverKind cover, Color coverTint, float meltsAbove, bool breathable, float harshness)
		{
			spec.Description = description;
			spec.Tint = tint;
			spec.FogColor = fog;
			spec.FallSpeedScale = fallSpeed;
			spec.StretchScale = stretch;
			spec.Emission = emission;
			spec.Cover = cover;
			spec.CoverTint = coverTint;
			spec.MeltsAbove = meltsAbove;
			spec.Breathable = breathable;
			spec.Harshness = harshness;
			return spec;
		}

		/// <summary>Every shipped substance, created where missing, by name.</summary>
		private static Dictionary<string, WeatherSubstance> EnsureSubstances(string folder, Report report)
		{
			var result = new Dictionary<string, WeatherSubstance>(StringComparer.Ordinal);
			// One that has been moved out of the folder is still the one to point biomes at.
			foreach (WeatherSubstance existing in WorldEditorAssets.FindAll<WeatherSubstance>())
			{
				result[existing.name] = existing;
				/* Registered here too, not only when created: every substance made before creation
				 * registered had no Addressables entry, so a running game never cached one and
				 * WeatherPhysics.PrecipitateOf found no condensate on any world. Only the missing ones,
				 * so the group file does not churn on every run. Guarded by
				 * WeatherSubstanceAddressablesTests. */
				string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(existing));
				var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
				if (settings != null && !string.IsNullOrEmpty(guid) && settings.FindAssetEntry(guid) == null)
				{
					WorldEditorAssets.RegisterAddressable(existing);
					report.Substances++;
				}
			}
			foreach (SubstanceSpec spec in Substances)
			{
				if (result.ContainsKey(spec.Name))
				{
					report.Skipped.Add($"substance {spec.Name}");
					continue;
				}
				result[spec.Name] = FindOrCreate<WeatherSubstance>(folder, spec.Name, report, s =>
				{
					s.DisplayName = spec.Name;
					s.Description = spec.Description;
					s.Tint = spec.Tint;
					s.FogColor = spec.FogColor;
					s.FallSpeedScale = spec.FallSpeedScale;
					s.StretchScale = spec.StretchScale;
					s.Emission = spec.Emission;
					s.Cover = spec.Cover;
					s.CoverTint = spec.CoverTint;
					s.MeltsAbove = spec.MeltsAbove;
					s.Breathable = spec.Breathable;
					s.Harshness = spec.Harshness;
					ApplyPhysics(s, spec);
					report.Substances++;
				});
			}
			return result;
		}

		/// <summary>What the substance physically is: the part of it the weather reads, not the part the eye does.</summary>
		private static void ApplyPhysics(WeatherSubstance substance, SubstanceSpec spec)
		{
			substance.Condenses = spec.Condenses;
			substance.Condensate = spec.Condensate;
			substance.Frozen = spec.Frozen;
			substance.GrainMetres = spec.GrainMetres;
			substance.GrainDensity = spec.GrainDensity;
			substance.Vapour = spec.Vapour;
		}

		private static T FindOrCreate<T>(string folder, string name, Report report, Action<T> setup) where T : ScriptableObject
		{
			string path = $"{folder}/{WorldEditorAssets.Sanitize(name)}.asset";
			T existing = AssetDatabase.LoadAssetAtPath<T>(path);
			if (existing != null)
			{
				report.Skipped.Add($"{typeof(T).Name} {name}");
				return existing;
			}
			WorldEditorAssets.EnsureFolder(folder);
			T asset = ScriptableObject.CreateInstance<T>();
			asset.name = WorldEditorAssets.Sanitize(name);
			setup(asset);
			AssetDatabase.CreateAsset(asset, path);
			WorldEditorAssets.RegisterAddressable(asset);
			return asset;
		}

		// ── Biome ground ──

		/// <summary>What one biome's ground gives the air: a substance's name for each, or null.</summary>
		private readonly struct GroundSpec
		{
			public readonly string Loose;
			public readonly string Emits;
			public readonly float EmissionRate;

			public GroundSpec(string loose, string emits, float emissionRate)
			{
				Loose = loose;
				Emits = emits;
				EmissionRate = emissionRate;
			}
		}

		private static GroundSpec LooseOnly(string substance) => new GroundSpec(substance, null, 0f);
		private static GroundSpec Emitting(string substance, float rate) => new GroundSpec(null, substance, rate);

		/* Keyed by biome asset name. A biome not listed is rock, soil, vegetation, ice or water, and
		 * gives the air nothing of its own: that is nearly all of them.
		 *
		 * Loose ground. The regolith and radiation plains were the dust-devil and regolith-storm
		 * biomes, and carried those dusts by name. The sand biomes carried the generic sand layer,
		 * which had no substance and fell as the default sand; only the four whose weather was
		 * sand storms for a third or more of the time are sand here. Savanna, scrubland, wasteland
		 * and oasis saw a sandstorm between an eighth and a fifth of the time, the steppe a haboob
		 * now and then, and the farmland and grasslands only ever the dirt a tornado picks up; none
		 * of them is loose ground, and on none of them will the wind lift sand however hard it
		 * blows. The beach is sand by name, but the old weather forbade sand on it outright.
		 *
		 * Emissions. The volcanic three spawned ashfall more than half the time under a standing
		 * haze of 0.15, and emit ash at that. Everything else emitting did it only through its
		 * storms, and gets a trickle of 0.08 that the eruptions the physics raises over it then
		 * multiply. The methane lake's tholin was the planet's haze settling on it one storm in
		 * five; kept, since that is what it did. */
		private static readonly Dictionary<string, GroundSpec> Ground = new Dictionary<string, GroundSpec>(StringComparer.Ordinal)
		{
			{ "Badlands", LooseOnly("Silicate Sand") },
			{ "Desert", LooseOnly("Silicate Sand") },
			{ "High Desert", LooseOnly("Silicate Sand") },
			{ "Salt Flat", LooseOnly("Silicate Sand") },
			{ "Dust Sea", LooseOnly("Regolith Dust") },
			{ "Impact Basin", LooseOnly("Regolith Dust") },
			{ "Regolith Plain", LooseOnly("Regolith Dust") },
			{ "Rille", LooseOnly("Regolith Dust") },
			{ "Radiation Plain", LooseOnly("Irradiated Dust") },

			{ "Crater", Emitting("Volcanic Ash", 0.15f) },
			{ "Volcanic", Emitting("Volcanic Ash", 0.15f) },
			{ "Volcanic Temple", Emitting("Volcanic Ash", 0.15f) },
			{ "Cryovolcanic Plain", Emitting("Cryo Tephra", 0.08f) },
			{ "Ice Geyser Field", Emitting("Cryo Tephra", 0.08f) },
			{ "Tidal Fracture", Emitting("Cryo Tephra", 0.08f) },
			{ "Molten Surface", Emitting("Sulphur Dust", 0.08f) },
			{ "Runaway Greenhouse Plain", Emitting("Sulphur Dust", 0.08f) },
			{ "Sulphur Flats", Emitting("Sulphur Dust", 0.08f) },
			{ "Sulphuric Cloud Deck", Emitting("Sulphur Dust", 0.08f) },
			{ "Methane Lake", Emitting("Tholin Haze", 0.08f) },
			{ "Tholin Plain", Emitting("Tholin Haze", 0.08f) },

			/* Steam. A geyser basin is a field of hot springs, fumaroles and geysers (GeothermalVents); its
			 * steam is a vapour, which the weather never treats as falling or erupting. At 0.5 seven sites in
			 * ten of the basin's 80 m cells, a quarter of them geysers: Yellowstone's Upper Geyser Basin, a
			 * few square kilometres, holds some 150 geysers among several hundred springs. */
			{ "Geyser Basin", Emitting("Steam", 0.5f) },
		};

		/// <summary>True when somebody has already said what this biome's ground gives the air.</summary>
		private static bool HasGround(BiomeTemplate biome)
		{
			return biome.LooseGround != null || biome.Emits != null || biome.EmissionRate > 0f;
		}

		private static void ApplyGround(BiomeTemplate biome, GroundSpec spec, Dictionary<string, WeatherSubstance> substances)
		{
			biome.LooseGround = Lookup(spec.Loose, substances, biome);
			biome.Emits = Lookup(spec.Emits, substances, biome);
			biome.EmissionRate = biome.Emits != null ? spec.EmissionRate : 0f;
		}

		private static WeatherSubstance Lookup(string name, Dictionary<string, WeatherSubstance> substances, BiomeTemplate biome)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}
			if (substances.TryGetValue(name, out WeatherSubstance substance) && substance != null)
			{
				return substance;
			}
			Debug.LogWarning($"[Weather content] {biome.name} wants the substance {name}, which does not exist; its ground is left without it.");
			return null;
		}
	}
}
#endif
