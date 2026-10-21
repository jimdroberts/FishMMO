using System;
using System.Collections.Generic;
using System.Globalization;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The cave biome a surface biome's caves of one form are inside (<see cref="BiomeTemplate.CaveInteriors"/>, 2026-10-18,
	/// decisions 37 and 51, phase I2n "caves have a biome"): the biome whose template dresses the cave's inside (its formations,
	/// crystals, glow, lights, floor grounds and animals), while the mouth, the shell's rock and every mine stay the host's.
	/// </summary>
	/// <remarks>
	/// The link holds only where the cave's shell rock is one the interior forms in (<see cref="BiomeTemplate.CaveInteriorRocks"/>):
	/// a limestone cave in a karst country is a Cave inside, a granite talus cave there stays the host's.
	/// </remarks>
	[Serializable]
	public struct BiomeCaveInterior
	{
		[Tooltip("The cave's form.")]
		public BiomeCaveForm Form;
		[Tooltip("The cave biome whose template dresses the inside of this biome's caves of the form (Cave, Ice Cave…), by its asset's name. Empty: the caves are dressed from this biome, as before.")]
		public string Biome;

		public BiomeCaveInterior(BiomeCaveForm form, string biome)
		{
			Form = form;
			Biome = biome;
		}

		/// <summary>The entry as the authoring marks and reports it: "Form=Biome".</summary>
		public override string ToString() => $"{Form}={Biome}";
	}

	/// <summary>Where in a cave a formation stands (the cave dressing's places for its shapes).</summary>
	/// <remarks>Append only; the ordinal is stored in template assets.</remarks>
	public enum BiomeCaveFormationRole : byte
	{
		/// <summary>Stood on the floor: stalagmites, columns' feet.</summary>
		Floor = 0,
		/// <summary>Hung from the roof: stalactites, straws, icicles.</summary>
		Hanging = 1,
		/// <summary>Down a wall from its foot: flowstone, frost flowers.</summary>
		Wall = 2,
		/// <summary>In a great cavern's wet hollows: gour pools.</summary>
		Pool = 3,
		/// <summary>Fallen from the roof, or a working's spoil.</summary>
		Breakdown = 4,
		/// <summary>The cave's own crystal formation (where its crystals are its own stone, not a deposit: an ice cave's ice crystals).</summary>
		Crystal = 5,
	}

	/// <summary>
	/// One formation a biome's caves of a form take in a place (<see cref="BiomeTemplate.CaveFormations"/>, 2026-10-18, I2n): a
	/// rock shape name (Stalagmite, Flowstone, Gours, Block, Dribble, FrostFlowers…) drawn in the cave's own rock, with how often
	/// among the place's others and the worlds it forms on.
	/// </summary>
	[Serializable]
	public struct BiomeCaveFormation
	{
		[Tooltip("The cave's form.")]
		public BiomeCaveForm Form;
		[Tooltip("Where it stands: on the floor, hung from the roof, down a wall, in a cavern's pools, fallen from the roof, or the cave's own crystal.")]
		public BiomeCaveFormationRole Role;
		[Tooltip("The formation's shape, drawn in the cave's own rock (Stalagmite, Flowstone, Gours, Block, Slabs, Dribble, FrostFlowers, Lobe, Crystal, Scree…).")]
		public string Shape;
		[Tooltip("How often the place's formations are this shape among its others (0 never).")]
		[Min(0f)] public float Weight;
		[Tooltip("The worlds it forms on (drip formations need liquid water: none on an airless world). Open = every world.")]
		public BiomeWorldGate WorldGate;

		public BiomeCaveFormation(BiomeCaveForm form, BiomeCaveFormationRole role, string shape, float weight = 1f, BiomeWorldGate gate = default)
		{
			Form = form;
			Role = role;
			Shape = shape;
			Weight = weight;
			WorldGate = gate;
		}

		/// <summary>The entry's identity for the authoring's marks: "Form/Role/Shape".</summary>
		public string Id => $"{Form}/{Role}/{Shape}";

		/// <summary>The entry as the authoring marks and reports it: "Form/Role/Shape×weight", the gate's signature after a '|' when set.</summary>
		public override string ToString()
		{
			string gate = WorldGate.Signature();
			return Id + "×" + Weight.ToString("R", CultureInfo.InvariantCulture) + (gate.Length > 0 ? "|" + gate : string.Empty);
		}
	}

	/// <summary>
	/// What lights a biome's caves of a form by itself (<see cref="BiomeTemplate.CaveGlows"/>, 2026-10-18, I2n): the glowing
	/// crystal's lead hue, and how much luminous fungus and how many glow-worm colonies its caves hold.
	/// </summary>
	[Serializable]
	public struct BiomeCaveGlow
	{
		[Tooltip("The cave's form.")]
		public BiomeCaveForm Form;
		[Tooltip("The glow crystals' hue by its name in the cave glow art (IceBlue, Amber, Violet, Green…), lead and accent alike. Empty: a lead and an accent drawn by chance for each cave.")]
		public string Crystal;
		[Tooltip("How much of the floors' and walls' glow is luminous fungus against crystal (1 the cave dressing's own share, 0 none).")]
		[Min(0f)] public float Fungus;
		[Tooltip("How often its chambers have glow-worm colonies hung from their roofs (1 the cave dressing's own chance, 0 none).")]
		[Min(0f)] public float Worms;

		public BiomeCaveGlow(BiomeCaveForm form, string crystal, float fungus, float worms)
		{
			Form = form;
			Crystal = crystal ?? string.Empty;
			Fungus = fungus;
			Worms = worms;
		}

		/// <summary>The entry as the authoring marks and reports it: "Form=crystal|fungus|worms".</summary>
		public override string ToString() => $"{Form}={(string.IsNullOrEmpty(Crystal) ? "chance" : Crystal)}|{Fungus.ToString("R", CultureInfo.InvariantCulture)}|{Worms.ToString("R", CultureInfo.InvariantCulture)}";
	}

	/// <summary>
	/// The zones of an ice cave's cold trap (decision 51, 2026-10-18): the COLD zone near a mouth and in the cold sink below its
	/// sill holds the ice, a THAW ring beyond it the ice's leavings, and past that the cave is bare dripping ROCK. As a ground
	/// layer's band (<see cref="TerrainTextureLayer.caveZones"/>): the zones the layer floors.
	/// </summary>
	[Flags]
	public enum BiomeCaveZone : byte
	{
		/// <summary>No zones (a layer: the cold and thaw zones of an ice cave, every floor of any other cave).</summary>
		None = 0,
		Cold = 1 << 0,
		Thaw = 1 << 1,
		Rock = 1 << 2,
	}

	/// <summary>The cave-interior rules that do not depend on a template asset (pure).</summary>
	public static class BiomeCaveInteriors
	{
		// ── The link ──────────────────────────────────────────────────

		/// <summary>
		/// The rocks a limestone-type cave forms in (decisions 37 and 46): the soluble rocks water dissolves into caves. What the
		/// Cave biome's interior rocks are authored from.
		/// </summary>
		public static readonly string[] SolubleRocks = { "Limestone", "Chalk", "Marble", "Gypsum", "Travertine", "Dolomite" };

		/// <summary>
		/// The shells an ice cave's cold trap holds ice in (decision 51): glacier ice itself, and the soluble rocks of the rock ice
		/// caves (Kungur's gypsum, Scărișoara's limestone).
		/// </summary>
		public static readonly string[] IceCaveRocks = { "Ice", "Limestone", "Dolomite", "Marble", "Gypsum" };

		/// <summary>True when a rock (a rock type name, or Ice) is one of <paramref name="rocks"/> (ignoring case); never for none.</summary>
		public static bool Forms(IReadOnlyList<string> rocks, string shell)
		{
			if (rocks == null || string.IsNullOrEmpty(shell))
			{
				return false;
			}
			for (int i = 0; i < rocks.Count; i++)
			{
				if (string.Equals(rocks[i], shell, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The biome a list links a form's caves to (the first entry naming the form), or false (none, or an empty name).</summary>
		public static bool TryLink(IReadOnlyList<BiomeCaveInterior> links, BiomeCaveForm form, out string biome)
		{
			biome = null;
			if (links != null)
			{
				for (int i = 0; i < links.Count; i++)
				{
					if (links[i].Form == form)
					{
						biome = links[i].Biome;
						return !string.IsNullOrEmpty(biome);
					}
				}
			}
			return false;
		}

		/// <summary>The forms a mine never links (a mine is its host's: dug, timbered and lit by the people of the land over it).</summary>
		public static bool Links(BiomeCaveForm form) => form != BiomeCaveForm.Mine;

		// ── Formations ────────────────────────────────────────────────

		/// <summary>
		/// The formations a form's caves took until 2026-10-18 (the cave dressing's SetFor, one shape a place): what every biome's
		/// template is authored with. A fresh list each call, in place order.
		/// </summary>
		public static List<BiomeCaveFormation> DefaultFormations(BiomeCaveForm form)
		{
			var list = new List<BiomeCaveFormation>();
			void Add(BiomeCaveFormationRole role, string shape) => list.Add(new BiomeCaveFormation(form, role, shape));
			switch (form)
			{
				case BiomeCaveForm.IceCave:
					Add(BiomeCaveFormationRole.Floor, "Stalagmite");
					Add(BiomeCaveFormationRole.Hanging, "Stalagmite");
					Add(BiomeCaveFormationRole.Wall, "FrostFlowers");
					Add(BiomeCaveFormationRole.Breakdown, "Lobe");
					Add(BiomeCaveFormationRole.Crystal, "Crystal");
					break;
				case BiomeCaveForm.LavaTube:
					Add(BiomeCaveFormationRole.Floor, "Dribble");
					Add(BiomeCaveFormationRole.Hanging, "Dribble");
					Add(BiomeCaveFormationRole.Breakdown, "Slabs");
					break;
				case BiomeCaveForm.SeaCave:
					Add(BiomeCaveFormationRole.Floor, "Stalagmite");
					Add(BiomeCaveFormationRole.Hanging, "Stalagmite");
					Add(BiomeCaveFormationRole.Breakdown, "Block");
					break;
				case BiomeCaveForm.Mine:
					Add(BiomeCaveFormationRole.Breakdown, "Scree");
					break;
				default:
					Add(BiomeCaveFormationRole.Floor, "Stalagmite");
					Add(BiomeCaveFormationRole.Hanging, "Stalagmite");
					Add(BiomeCaveFormationRole.Wall, "Flowstone");
					Add(BiomeCaveFormationRole.Pool, "Gours");
					Add(BiomeCaveFormationRole.Breakdown, "Block");
					Add(BiomeCaveFormationRole.Crystal, "Crystal");
					break;
			}
			return list;
		}

		/// <summary>Every form's default formations (<see cref="DefaultFormations(BiomeCaveForm)"/>), in the forms' order.</summary>
		public static List<BiomeCaveFormation> DefaultFormations()
		{
			var list = new List<BiomeCaveFormation>();
			foreach (BiomeCaveForm form in (BiomeCaveForm[])Enum.GetValues(typeof(BiomeCaveForm)))
			{
				list.AddRange(DefaultFormations(form));
			}
			return list;
		}

		/// <summary>True when a list names any formation for a form (it then decides the form's caves, a place it leaves out unformed).</summary>
		public static bool NamesForm(IReadOnlyList<BiomeCaveFormation> list, BiomeCaveForm form)
		{
			if (list != null)
			{
				for (int i = 0; i < list.Count; i++)
				{
					if (list[i].Form == form)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// The shapes a list forms in one place of a form's caves on a world, with their weights (entries of no weight, no shape or
		/// whose world gate shuts the world left out), in list order.
		/// </summary>
		public static List<(string Shape, float Weight)> Shapes(IReadOnlyList<BiomeCaveFormation> list, BiomeCaveForm form, BiomeCaveFormationRole role,
			in BiomeWorldConditions world)
		{
			var shapes = new List<(string, float)>();
			if (list != null)
			{
				for (int i = 0; i < list.Count; i++)
				{
					BiomeCaveFormation f = list[i];
					if (f.Form == form && f.Role == role && f.Weight > 0f && !string.IsNullOrEmpty(f.Shape) && (f.WorldGate.IsOpen || f.WorldGate.Admits(world)))
					{
						shapes.Add((f.Shape, f.Weight));
					}
				}
			}
			return shapes;
		}

		/// <summary>The shape a roll in [0, 1) picks by weight from <see cref="Shapes"/>' list, or null when it is empty.</summary>
		public static string Pick(IReadOnlyList<(string Shape, float Weight)> shapes, float roll)
		{
			if (shapes == null || shapes.Count == 0)
			{
				return null;
			}
			float total = 0f;
			foreach ((string _, float w) in shapes)
			{
				total += w;
			}
			float at = Mathf.Clamp01(roll) * total, run = 0f;
			foreach ((string shape, float w) in shapes)
			{
				run += w;
				if (at < run)
				{
					return shape;
				}
			}
			return shapes[shapes.Count - 1].Shape;
		}

		// ── Glow ──────────────────────────────────────────────────────

		/// <summary>
		/// What lit a form's caves by itself until 2026-10-18 (the cave dressing's palette by form): an ice cave's ice-blue crystal and
		/// a lava tube's amber with neither fungus nor glow-worms, a mine's fungus without glow-worms, every other cave's crystal hues
		/// by chance with both.
		/// </summary>
		public static BiomeCaveGlow DefaultGlow(BiomeCaveForm form)
		{
			switch (form)
			{
				case BiomeCaveForm.IceCave: return new BiomeCaveGlow(form, "IceBlue", 0f, 0f);
				case BiomeCaveForm.LavaTube: return new BiomeCaveGlow(form, "Amber", 0f, 0f);
				case BiomeCaveForm.Mine: return new BiomeCaveGlow(form, string.Empty, 1f, 0f);
				default: return new BiomeCaveGlow(form, string.Empty, 1f, 1f);
			}
		}

		/// <summary>Every form's default glow (<see cref="DefaultGlow"/>), in the forms' order.</summary>
		public static List<BiomeCaveGlow> DefaultGlows()
		{
			var list = new List<BiomeCaveGlow>();
			foreach (BiomeCaveForm form in (BiomeCaveForm[])Enum.GetValues(typeof(BiomeCaveForm)))
			{
				list.Add(DefaultGlow(form));
			}
			return list;
		}

		/// <summary>The glow a list gives a form (its first entry for the form), or false.</summary>
		public static bool TryGlow(IReadOnlyList<BiomeCaveGlow> list, BiomeCaveForm form, out BiomeCaveGlow glow)
		{
			glow = default;
			if (list != null)
			{
				for (int i = 0; i < list.Count; i++)
				{
					if (list[i].Form == form)
					{
						glow = list[i];
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// The crystal share of a cave's floor glow once its fungus is weighed (<paramref name="fungus"/> 1: the share as it was,
		/// 0: all crystal): crystal against fungus × weight.
		/// </summary>
		public static float CrystalShare(float share, float fungus)
		{
			if (fungus <= 0f)
			{
				return 1f;
			}
			float rest = (1f - share) * fungus;
			return share + rest > 0f ? share / (share + rest) : 1f;
		}

		// ── Crystals ──────────────────────────────────────────────────

		/// <summary>The share of its weight a host's glowing crystal grows with in a linked interior (decision 51: an accent).</summary>
		public const float HostAccent = 0.5f;

		/// <summary>
		/// A linked interior's crystals with the host's glowing ones as accents (decision 51, 2026-10-18: Glacier's Nāgamani in its
		/// ice caves): the interior's list, then each glowing kind of the host's it does not grow at <see cref="HostAccent"/> of the
		/// host's weight. The interior's own list (the same object) when the host adds nothing.
		/// </summary>
		public static IReadOnlyList<BiomeCaveCrystal> WithHostAccents(IReadOnlyList<BiomeCaveCrystal> interior, IReadOnlyList<BiomeCaveCrystal> host)
		{
			List<BiomeCaveCrystal> merged = null;
			if (host != null)
			{
				foreach (BiomeCaveCrystal c in host)
				{
					if (c.Weight <= 0f || !BiomeCaveCrystals.Glows(c.Kind) || Grows(interior, c.Kind) || merged != null && Grows(merged, c.Kind))
					{
						continue;
					}
					merged ??= interior != null ? new List<BiomeCaveCrystal>(interior) : new List<BiomeCaveCrystal>();
					merged.Add(new BiomeCaveCrystal(c.Kind, c.Weight * HostAccent));
				}
			}
			return merged ?? interior;
		}

		private static bool Grows(IReadOnlyList<BiomeCaveCrystal> list, BiomeCaveCrystalKind kind)
		{
			if (list != null)
			{
				foreach (BiomeCaveCrystal c in list)
				{
					if (c.Kind == kind)
					{
						return true;
					}
				}
			}
			return false;
		}

		// ── Lights ────────────────────────────────────────────────────

		/// <summary>
		/// A linked interior's cave light under the world's air (decision 51): as named where people can breathe, else the cold Glow
		/// kind in its place (<see cref="BiomeGlowLights.CaveLights"/>' mapping: a crystal post on a route or in a stope, a glowjar
		/// elsewhere), at the same spacing. A Glow kind stays.
		/// </summary>
		public static BiomeCaveLight ForAir(in BiomeCaveLight light, bool breathable)
		{
			if (breathable || BiomeGlowLights.Glows(light.Light))
			{
				return light;
			}
			BiomeWayLightKind glow = light.Place == BiomeCaveLightPlace.Route || light.Place == BiomeCaveLightPlace.MineStope
				? BiomeWayLightKind.CrystalPost
				: BiomeWayLightKind.Glowjar;
			return new BiomeCaveLight(light.Place, glow, light.Spacing);
		}

		// ── Site variants ─────────────────────────────────────────────

		/// <summary>The share of its weight an interior's own variant keeps where the host names variants of the kind (decision 51).</summary>
		public const float InteriorVariantShare = 0.4f;

		/// <summary>
		/// A cave site's template weight with its host's and its interior's variant lists merged (decision 51, 2026-10-18): the host's
		/// entry where it names the variant; else the interior's (<paramref name="interiorNamed"/>: the interior named it, so its
		/// pieces take the interior's palette), at <see cref="InteriorVariantShare"/> of it where the host names variants of its own for
		/// the kind; else <paramref name="elsewhere"/>.
		/// </summary>
		public static float MergedVariantWeight(IReadOnlyList<BiomeSiteVariant> host, IReadOnlyList<BiomeSiteVariant> interior, POIType kind, string variant,
			float elsewhere, out bool interiorNamed)
		{
			interiorNamed = false;
			if (BiomeSiteVariant.TryWeight(host, kind, variant, out float own))
			{
				return own;
			}
			if (BiomeSiteVariant.TryWeight(interior, kind, variant, out float inside))
			{
				interiorNamed = true;
				return NamesKind(host, kind) ? inside * InteriorVariantShare : inside;
			}
			return elsewhere;
		}

		/// <summary>True when a list weighs any variant of a kind.</summary>
		public static bool NamesKind(IReadOnlyList<BiomeSiteVariant> list, POIType kind)
		{
			if (list != null)
			{
				for (int i = 0; i < list.Count; i++)
				{
					if (list[i].Kind == kind && !string.IsNullOrEmpty(list[i].Variant))
					{
						return true;
					}
				}
			}
			return false;
		}

		// ── The cold trap ─────────────────────────────────────────────

		/// <summary>How far from a mouth the ice lies, metres (decision 51).</summary>
		public const float ColdReach = 90f;
		/// <summary>How far below a mouth's sill the cold sink lies, metres: winter air pools there.</summary>
		public const float SinkDepth = 8f;
		/// <summary>The thaw ring's width past the cold zone, metres.</summary>
		public const float ThawRing = 20f;

		/// <summary>
		/// The zone of an ice cave's cold trap at a point: <paramref name="distance"/> from the nearest mouth's sill, metres, and how
		/// far below it the point lies (<paramref name="belowSill"/>, metres, negative above it). COLD within <see cref="ColdReach"/>,
		/// and anywhere in the sink (<see cref="SinkDepth"/> or more below) however far in (2026-10-21, "cold traps freeze deep":
		/// decision 51 held the sink's ice to 160 m); THAW in the <see cref="ThawRing"/> past the cold zone; ROCK beyond. A cave in an
		/// ice shell is all cold (<paramref name="iceShell"/>).
		/// </summary>
		public static BiomeCaveZone ZoneOf(float distance, float belowSill, bool iceShell)
		{
			if (iceShell)
			{
				return BiomeCaveZone.Cold;
			}
			if (distance <= ColdReach || belowSill >= SinkDepth)
			{
				return BiomeCaveZone.Cold;
			}
			if (distance <= ColdReach + ThawRing)
			{
				return BiomeCaveZone.Thaw;
			}
			return BiomeCaveZone.Rock;
		}

		/// <summary>
		/// Whether a ground layer floors a zone: any zone without a cold trap (<paramref name="zone"/> None); an unbanded layer the
		/// cold zone and the thaw ring (the rock past them is bare rock); a banded one the zones it names.
		/// </summary>
		public static bool Admits(BiomeCaveZone band, BiomeCaveZone zone)
		{
			if (zone == BiomeCaveZone.None)
			{
				return true;
			}
			return band == BiomeCaveZone.None ? zone != BiomeCaveZone.Rock : (band & zone) != 0;
		}
	}
}
