#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What a cave grows and how thickly (2026-10-20, cave realism: "more clusters of detail objects (mushrooms are great already
	/// but more and even larger ones)", "Largest mushrooms should get up to 15m tall", "More glow is good inside the caves",
	/// "More vines and stuff inside caves"). Every 0..1 or multiplier is read by the dressing's passes; 0 turns a pass off.
	/// </summary>
	public struct CaveFungusPlan
	{
		/// <summary>Whether luminous fungi grow here at all (not in ice or lava).</summary>
		public bool Fungi;
		/// <summary>The chance a room, cavern or hall carries a glowcap grove (giant and middle-sized glowcaps, tufts, puffballs).</summary>
		public float Groves;
		/// <summary>The chance a great hall or cavern carries a forest of mushroom trees where its roof allows.</summary>
		public float Forests;
		/// <summary>The tallest mushroom tree, metres (at most <see cref="CaveFungi.TowerMax"/>; under <see cref="CaveFungi.TowerMin"/> none).</summary>
		public float TowerMax;
		/// <summary>How thickly glow clumps gather in rooms and along the ways (1 a limestone cave's).</summary>
		public float Clumps;
		/// <summary>The chance a room has a fairy ring of glowing tufts.</summary>
		public float Rings;
		/// <summary>The share of a room's fungus clumps that are puffballs or morels.</summary>
		public float Puffballs;
		/// <summary>How many clusters of breakdown (blocks fallen from the roof, a mine's spoil) a chamber gathers, about.</summary>
		public float Rubble;
		/// <summary>How thickly glowroot curtains and glow-vines hang in the deep dark (fantasy).</summary>
		public float GlowRoots;
		/// <summary>How readily tree roots come down through a thin roof near a mouth.</summary>
		public float RootsNearMouth;
		/// <summary>How thickly creeper and ivy mat the damp walls in a mouth's (or an opening's) daylight.</summary>
		public float DaylightVines;
		/// <summary>How thickly a mine's floors are littered with its workings' clutter (spoil, offcuts, crates, barrels, buckets).</summary>
		public float Clutter;
	}

	/// <summary>What the dressing reads of a cave's biome (its template), so the plan's choices stay pure and testable.</summary>
	public struct CaveBiomeTraits
	{
		/// <summary>Whether a biome was read at all (false: every choice is the form's default).</summary>
		public bool Known;
		/// <summary>The biome's vines: whether any grow, how thickly at caves, how many trees carry one (a wooded hill over the cave).</summary>
		public bool Vines;
		public float VineCaves, Trees;
		/// <summary>Whether ferns or bushes grow at its cave mouths (anything green above for roots to come down from).</summary>
		public bool Greenery;
		/// <summary>Whether its caves grow crystals that glow (their light competes with the fungi's).</summary>
		public bool GlowingCrystals;
	}

	/// <summary>
	/// The ONE lookup of the dressing's per-biome and per-form choices (2026-10-20): what a cave of a form in a biome grows, how
	/// big and how thickly. Pure (the template is read into <see cref="CaveBiomeTraits"/> first), so the biome suite can move the
	/// choices into template fields later; plus the rules every towering fungus keeps (its height range and its room under the
	/// roof).
	/// </summary>
	public static class CaveFungi
	{
		/// <summary>A mushroom tree stands 5–15 m (2026-10-20: "Largest mushrooms should get up to 15m tall").</summary>
		public const float TowerMin = 5f, TowerMax = 15f;
		/// <summary>The glowcaps of the middle size stand 3–5 m.</summary>
		public const float MidMin = 3f, MidMax = 5f;

		/// <summary>The plan for a cave of a form in a biome (null: the form's defaults).</summary>
		public static CaveFungusPlan For(BiomeTemplate biome, CaveForm form) => For(TraitsOf(biome, biome != null ? BiomeTemplateArt.Vines(biome) : null), form);

		/// <summary>What the plan reads of a biome, its vines given (read once by the caller: the lookup warns for an unauthored template).</summary>
		public static CaveBiomeTraits TraitsOf(BiomeTemplate biome, VineBiomes.Habitat vines) => TraitsOf(biome, vines, null);

		/// <summary>
		/// What the plan reads of a cave's biome, its vines given, with the crystals its inside grows (2026-10-25, I2n: the vines, the
		/// greenery at the mouth and the trees over it are the HOST's, the glowing crystals the interior's; null: the biome's own).
		/// </summary>
		public static CaveBiomeTraits TraitsOf(BiomeTemplate biome, VineBiomes.Habitat vines, IReadOnlyList<BiomeCaveCrystal> crystals)
		{
			if (ReferenceEquals(biome, null))
			{
				return default;
			}
			bool vined = vines != null && vines.Species != null && vines.Species.Length > 0;
			int green = (biome.CaveMouthFerns != null ? biome.CaveMouthFerns.Count : 0) + (biome.CaveMouthBushes != null ? biome.CaveMouthBushes.Count : 0);
			return new CaveBiomeTraits
			{
				Known = true,
				Vines = vined,
				VineCaves = vined ? Mathf.Clamp01(vines.Caves) : 0f,
				Trees = vined ? Mathf.Clamp01(vines.Trees) : 0f,
				Greenery = green > 0,
				GlowingCrystals = BiomeCaveCrystals.Total(crystals ?? BiomeTemplateArt.CaveCrystals(biome), glowingOnly: true) > 0f,
			};
		}

		/// <summary>
		/// The plan by form, then by what the biome is. A limestone cave grows the most: groves in its rooms, forests of mushroom
		/// trees in its great halls, glowroots in its deep dark. A grotto less and smaller (its chamber is low); a sea cave little
		/// (salt spray); a mine little but its clutter and a few fungi on its rotting timber; ice and lava none (nothing lives
		/// there), only their rubble, and a lava tube's thin roof lets roots through. Then the biome: vines inside only where its
		/// vines grow (as thickly as at its cave mouths), roots where something green grows above, fewer groves where its own
		/// glowing crystals light the halls.
		/// </summary>
		public static CaveFungusPlan For(in CaveBiomeTraits biome, CaveForm form)
		{
			CaveFungusPlan p;
			switch (form)
			{
				case CaveForm.Grotto:
					p = new CaveFungusPlan { Fungi = true, Groves = 0.35f, Forests = 0.25f, TowerMax = 9f, Clumps = 0.7f, Rings = 0.2f, Puffballs = 0.25f, Rubble = 0.6f,
						GlowRoots = 0.6f, RootsNearMouth = 0.8f, DaylightVines = 1.2f };
					break;
				case CaveForm.SeaCave:
					p = new CaveFungusPlan { Fungi = true, Groves = 0.1f, Clumps = 0.3f, Puffballs = 0.1f, Rubble = 1.2f, GlowRoots = 0.2f, RootsNearMouth = 0.2f, DaylightVines = 0.5f };
					break;
				case CaveForm.IceCave:
					p = new CaveFungusPlan { Clumps = 0.6f, Rubble = 0.8f };
					break;
				case CaveForm.LavaTube:
					p = new CaveFungusPlan { Clumps = 0.6f, Rubble = 1.3f, RootsNearMouth = 0.6f, DaylightVines = 0.4f };
					break;
				case CaveForm.Mine:
					p = new CaveFungusPlan { Fungi = true, Groves = 0.1f, Clumps = 0.25f, Puffballs = 0.5f, Rubble = 0.5f, GlowRoots = 0.1f, RootsNearMouth = 0.5f,
						DaylightVines = 0.3f, Clutter = 1f };
					break;
				default:
					p = new CaveFungusPlan { Fungi = true, Groves = 0.5f, Forests = 0.7f, TowerMax = TowerMax, Clumps = 1f, Rings = 0.3f, Puffballs = 0.3f, Rubble = 1f,
						GlowRoots = 1f, RootsNearMouth = 1f, DaylightVines = 1f };
					break;
			}
			p.TowerMax = Mathf.Min(p.TowerMax, TowerMax);
			if (biome.Known)
			{
				p.DaylightVines = biome.Vines ? p.DaylightVines * (0.4f + 0.9f * biome.VineCaves) : 0f;
				p.RootsNearMouth *= biome.Greenery || biome.Vines ? 0.5f + 2f * biome.Trees + 0.3f * biome.VineCaves : 0.2f;
				if (biome.GlowingCrystals)
				{
					p.Groves *= 0.7f;
					p.Forests *= 0.75f;
				}
			}
			return p;
		}

		/// <summary>How the system's character (its layout) weighs the fungi: a hall cave's great halls carry the forests, a crystal gallery's light is its crystals'.</summary>
		public static float CharacterWeight(CaveCharacter character)
		{
			switch (character)
			{
				case CaveCharacter.Halls: return 1.35f;
				case CaveCharacter.Warren: return 1.1f;
				case CaveCharacter.Rift:
				case CaveCharacter.Abyss: return 0.85f;
				case CaveCharacter.Crystal: return 0.5f;
				default: return 1f;
			}
		}

		/// <summary>The scale that stands a piece of art <paramref name="artHeight"/> tall at <paramref name="want"/> metres, kept within a mushroom tree's 5–15 m.</summary>
		public static float TowerScale(float artHeight, float want) => Mathf.Clamp(want, TowerMin, TowerMax) / Mathf.Max(0.01f, artHeight);

		/// <summary>The scale that stands a middle glowcap at <paramref name="want"/> metres, within 3–5 m.</summary>
		public static float MidScale(float artHeight, float want) => Mathf.Clamp(want, MidMin, MidMax) / Mathf.Max(0.01f, artHeight);

		/// <summary>The clear air kept between a fungus's crown and the roof over it, metres: 1.5, or an eighth of its height for the tallest.</summary>
		public static float Clearance(float height) => Mathf.Max(1.5f, 0.12f * height);

		/// <summary>Whether a fungus <paramref name="height"/> tall stands on a floor under a roof with its clearance kept.</summary>
		public static bool Fits(float floor, float roof, float height) => roof - floor >= height + Clearance(height) - 1e-4f;

		/// <summary>The tallest fungus that keeps its clearance under <paramref name="room"/> metres of headroom, at most <paramref name="max"/>.</summary>
		public static float TallestUnder(float room, float max)
		{
			float h = room - 1.5f;
			if (0.12f * h > 1.5f)
			{
				h = room / 1.12f;
			}
			return Mathf.Min(max, Mathf.Max(0f, h));
		}
	}

	/// <summary>
	/// Fungi, glow and rubble in plenty (2026-10-20, cave realism: "Caves of all types and mines look barren even with all the
	/// detail objects … more clusters of detail objects"): forests of mushroom trees to 15 m in the great halls whose roofs
	/// allow them, glowcap groves in the rooms of every cave that grows fungi, fairy rings, denser clumps of glow over the rooms'
	/// floors and along the ways, tall bracket tiers on the walls, and clusters of breakdown fallen from the roofs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where it gathers.</b> Every pass clusters on the habitat field (<see cref="CaveHabitat"/>: glow, wet, organic), the very
	/// field the rock's layers and the run-time cover read, blended with its own noise so it is never bare where the field is
	/// still flat: <see cref="Rich"/>.
	/// </para>
	/// <para>
	/// <b>Nothing moves.</b> Each pass draws from its own salted stream after every older pass has run, so what a cave carried
	/// before stands where it stood. Ways, lanes, camps, gates, climbs, chasms and the great pieces already laid are kept
	/// clear; lights only with the brightest, nine metres apart as all the glow's are.
	/// </para>
	/// </remarks>
	public sealed partial class PointOfInterestCaveDressing
	{
		private sealed partial class Dresser
		{
			/// <summary>Where the great pieces stand (giant crystals, groves, mushroom trees), system frame: what new ones keep apart from.</summary>
			private readonly List<Vector3> bigs = new List<Vector3>();
			/// <summary>Where each camp's fire is, system frame.</summary>
			private readonly List<Vector3> campSites = new List<Vector3>();

			private CaveFungusPlan plan;
			private bool planMade;
			private VineBiomes.Habitat vines;
			private CaveHabitat habitat;

			/// <summary>The cave's plan (<see cref="CaveFungi.For(in CaveBiomeTraits, CaveForm)"/>), its biome's vines read once.</summary>
			private CaveFungusPlan Plan
			{
				get
				{
					if (!planMade)
					{
						planMade = true;
						// The mouth's vines and greenery and the trees over the cave are its host land's; the glowing crystals its inside's.
						vines = host != null ? BiomeTemplateArt.Vines(host) : null;
						plan = CaveFungi.For(CaveFungi.TraitsOf(host, vines, caves.Linked ? CaveInteriorArt.Crystals(in caves) : null), system.Form);
					}
					return plan;
				}
			}

			/// <summary>The habitat at a point of the rock (system frame) with its outward normal.</summary>
			private CaveHabitatSample HabitatAt(Vector3 local, Vector3 normal) => (habitat ??= CaveHabitat.For(system)).Sample(local, normal);

			/// <summary>
			/// How rich a spot of floor is for what glows, 0..1: a smooth noise of its own (a patch every ten metres or so), lifted
			/// where the habitat gathers glow, damp and organic matter. With the habitat flat it is the noise alone.
			/// </summary>
			private float Rich(Vector3 at, float noise, float scale = 0.11f)
			{
				float n = Mathf.PerlinNoise(noise + at.x * scale, noise * 0.71f + at.z * scale);
				CaveHabitatSample h = HabitatAt(at, Vector3.up);
				float g = Mathf.Max(h.Glow, 0.6f * h.Wet + 0.4f * h.Organic);
				return Mathf.Clamp01(0.55f * n + 0.45f * Mathf.Max(n, g));
			}

			/// <summary>Within <paramref name="apart"/> of a great piece or a camp.</summary>
			private bool Crowded(Vector3 at, float apart, float campApart = 6f)
			{
				foreach (Vector3 b in bigs)
				{
					if ((b - at).sqrMagnitude < apart * apart)
					{
						return true;
					}
				}
				foreach (Vector3 c in campSites)
				{
					if ((c - at).sqrMagnitude < campApart * campApart)
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>The fungi's passes (natural caves; a mine's few are its clutter's), each on its own stream.</summary>
			private void Fungi()
			{
				CaveFungusPlan p = Plan;
				if (p.Fungi && Colours().Fungi)
				{
					Forests(new DeterministicRNG(unchecked(system.Seed * 0x6E1B + 0x3F75)), p);
					Groves(new DeterministicRNG(unchecked(system.Seed * 0x2A97 + 0x5C31)), p);
					Rings(new DeterministicRNG(unchecked(system.Seed * 0x7B53 + 0x0D8F)), p);
				}
				TerracesAndPits(new DeterministicRNG(unchecked(system.Seed * 0x0F4B + 0x7D21)), p);
				RoomClumps(new DeterministicRNG(unchecked(system.Seed * 0x1C6D + 0x4E07)), p);
				WayClumps(new DeterministicRNG(unchecked(system.Seed * 0x5F29 + 0x2297)), p);
				Rubble(new DeterministicRNG(unchecked(system.Seed * 0x33B5 + 0x6A4D)), p);
			}

			// ── Mushroom trees ────────────────────────────────────

			/// <summary>
			/// Forests of mushroom trees in the great halls and caverns (and the largest rooms) whose roofs stand high enough: in each
			/// chosen chamber one to five stands, each the tallest its roof allows (to 15 m) with lesser trees round it, middle
			/// glowcaps and giant ones between them, puffballs and tufts under them.
			/// </summary>
			private void Forests(DeterministicRNG random, in CaveFungusPlan p)
			{
				if (p.Forests <= 0f || p.TowerMax < CaveFungi.TowerMin)
				{
					return;
				}
				float chance = Mathf.Clamp01(p.Forests * CaveFungi.CharacterWeight(system.Character));
				foreach (CaveChamber c in system.Chambers)
				{
					float reach = Mathf.Max(c.Radii.x, c.Radii.z);
					bool roll = random.NextFloat() < chance;
					int stands = Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * c.Radii.x * c.Radii.z / 700f), 1, 5);
					bool great = c.Kind == CaveChamberKind.Hall || c.Kind == CaveChamberKind.Cavern || (c.Kind == CaveChamberKind.Room && Mathf.Min(c.Radii.x, c.Radii.z) >= 9f);
					if (!roll || !great || reach < 10f || c.Radii.y < 7f)
					{
						continue;
					}
					int hue = random.NextFloat() < 0.3f ? Colours().FungusAccent : Colours().Fungus;
					for (int s = 0; s < stands; s++)
					{
						Vector3 near = InChamber(c, random.Range(0f, 2f * Mathf.PI), random.Range(0.2f, 0.72f));
						Stand(random, near, hue, p.TowerMax);
					}
				}
			}

			/// <summary>One stand of mushroom trees about a point of a floor, if its roof allows a tree at all.</summary>
			private void Stand(DeterministicRNG random, Vector3 near, int hue, float max)
			{
				int more = random.Range(1, 4), mids = random.Range(2, 5), under = random.Range(4, 10), puffs = random.Range(1, 4);
				// Half the stands as tall as the roof lets them be (to 15 m), the rest a little less, so no two halls stand alike.
				float a0 = random.Range(0f, 360f), want = random.NextFloat() < 0.5f ? max : random.Range(0.6f, 1f) * max;
				if (!Ground(near + Vector3.up * 1.5f, 6f, out float floor))
				{
					return;
				}
				var at = new Vector3(near.x, floor, near.z);
				if (!Clear(at, 3f) || Crowded(at, 12f) || !Level(at, 2f, 0.8f))
				{
					return;
				}
				if (!Tower(random, at, Mathf.Max(CaveFungi.TowerMin, want), hue, light: true, out float tallest))
				{
					return;
				}
				bigs.Add(at);
				var trees = new List<(Vector3 at, float h)> { (at, tallest) };
				// The lesser trees round it, each as tall as a share of the first and its own roof allow.
				for (int k = 0; k < more; k++)
				{
					float a = a0 + k * 360f / more + random.Range(-25f, 25f), d = random.Range(0.45f, 0.8f) * tallest, share = random.Range(0.45f, 0.85f);
					Vector3 q = at + Dir(a) * d;
					if (!Ground(new Vector3(q.x, at.y + 1.5f, q.z), 3f, out float f) || Mathf.Abs(f - at.y) > 2f)
					{
						continue;
					}
					q.y = f;
					if (!Clear(q, 2.5f) || TooClose(trees, q, 0.35f * tallest) || !Level(q, 1.5f, 0.7f))
					{
						continue;
					}
					if (Tower(random, q, Mathf.Max(CaveFungi.TowerMin, tallest * share), hue, light: false, out float h))
					{
						trees.Add((q, h));
					}
				}
				// Middle glowcaps and giant ones between them.
				for (int k = 0; k < mids; k++)
				{
					float a = random.Range(0f, 360f), d = random.Range(0.25f, 0.9f) * tallest;
					Vector3 q = at + Dir(a) * d;
					if (!Ground(new Vector3(q.x, at.y + 1.5f, q.z), 3f, out float f) || Mathf.Abs(f - at.y) > 2.5f)
					{
						continue;
					}
					q.y = f;
					if (!Clear(q, 1.2f) || TooClose(trees, q, 2.2f))
					{
						continue;
					}
					if (!Mid(random, q, random.Range(CaveFungi.MidMin, CaveFungi.MidMax), hue, light: false, out float mh))
					{
						FloorGlow(random, CaveGlowKind.GiantFungus, hue, q, random.Range(0.8f, 1.25f), 6f, light: false);
						continue;
					}
					trees.Add((q, mh));
				}
				// Puffballs and tufts under them.
				for (int k = 0; k < puffs + under; k++)
				{
					float a = random.Range(0f, 360f), d = random.Range(1.2f, 0.75f * tallest);
					Vector3 q = at + Dir(a) * d;
					if (!Ground(new Vector3(q.x, at.y + 1.2f, q.z), 2.5f, out float f) || Mathf.Abs(f - at.y) > 2f)
					{
						continue;
					}
					q.y = f;
					if (!Clear(q, 0.3f) || TooClose(trees, q, 1.2f))
					{
						continue;
					}
					if (k < puffs)
					{
						FloorGlow(random, CaveGlowKind.Puffballs, hue, q, random.Range(0.8f, 1.3f), 5f, light: false);
					}
					else
					{
						FloorGlow(random, CaveGlowKind.Fungus, random.NextFloat() < 0.3f ? Colours().FungusAccent : hue, q, random.Range(0.9f, 1.5f), 5f, light: false);
					}
				}
			}

			private static bool TooClose(List<(Vector3 at, float h)> trees, Vector3 q, float apart)
			{
				foreach ((Vector3 at, float _) in trees)
				{
					if ((at - q).sqrMagnitude < apart * apart)
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>Whether the floor round a spot lies within <paramref name="rise"/> of it out to <paramref name="radius"/>: a big foot stands on it.</summary>
			private bool Level(Vector3 at, float radius, float rise)
			{
				for (int k = 0; k < 4; k++)
				{
					Vector3 q = at + Dir(45f + k * 90f) * radius;
					if (!Ground(new Vector3(q.x, at.y + rise + 0.5f, q.z), 2f * rise + 0.5f, out float f) || Mathf.Abs(f - at.y) > rise)
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>
			/// A mushroom tree (a piece of <see cref="CaveGlowKind.TowerFungus"/>) at a spot of floor, as tall as wanted and the roof
			/// allows (5–15 m, <see cref="CaveFungi"/>): the roof over its crown and over its cap's rim clear, its cap's rim in the
			/// open (not in a wall). Lowered a step at a time where it does not fit; false where not even 5 m does.
			/// </summary>
			private bool Tower(DeterministicRNG random, Vector3 at, float want, int hue, bool light, out float height)
				=> Fungus(random, CaveGlowKind.TowerFungus, at, want, CaveFungi.TowerMin, CaveFungi.TowerMax, hue, light, out height);

			/// <summary>A middle glowcap (3–5 m), as <see cref="Tower"/>.</summary>
			private bool Mid(DeterministicRNG random, Vector3 at, float want, int hue, bool light, out float height)
				=> Fungus(random, CaveGlowKind.MidFungus, at, want, CaveFungi.MidMin, CaveFungi.MidMax, hue, light, out height);

			private bool Fungus(DeterministicRNG random, CaveGlowKind kind, Vector3 at, float want, float min, float max, int hue, bool light, out float height)
			{
				int variant = random.Range(0, CaveArt.VariantCount(kind));
				float yaw = random.Range(0f, 360f), tx = random.Range(-2f, 2f), tz = random.Range(-2f, 2f);
				height = 0f;
				bool have = CaveArt.TryGlow(kind, hue, variant, out CaveGlowArt art);
				if (!Roof(at + Vector3.up * 1.2f, 60f, out float roof))
				{
					return false;
				}
				float h = Mathf.Min(Mathf.Clamp(want, min, max), CaveFungi.TallestUnder(roof - at.y, max));
				for (int tries = 0; tries < 4 && h >= min - 1e-3f; tries++, h *= 0.85f)
				{
					float scale = Mathf.Clamp(h, min, max) / Mathf.Max(0.01f, art.Height);
					float stood = art.Height * scale;
					// The cap's rim: the piece's spread, but no further than a single cap's (a stand's spread is its several feet).
					float rim = Mathf.Min(art.Radius * scale, 0.46f * stood);
					if (!CapClear(at, stood, rim))
					{
						continue;
					}
					if (!have)
					{
						Missing.Add("cave glow art");
						return false;
					}
					if (!Place(art.Prefab, at - Vector3.up * 0.05f, Quaternion.Euler(tx, yaw, tz), Vector3.one * scale))
					{
						return false;
					}
					Glowing++;
					height = stood;
					if (light)
					{
						Light(at + Vector3.up * (0.6f * stood), art, LightSpacing);
					}
					return true;
				}
				return false;
			}

			/// <summary>Whether a fungus <paramref name="height"/> tall with its cap's rim <paramref name="rim"/> out stands clear: crown and rim under the roof with room, the rim in the open.</summary>
			private bool CapClear(Vector3 at, float height, float rim)
			{
				if (!Roof(at + Vector3.up * 1.2f, 60f, out float roof) || !CaveFungi.Fits(at.y, roof, height))
				{
					return false;
				}
				for (int k = 0; k < 6; k++)
				{
					Vector3 q = at + Dir(30f + k * 60f) * rim;
					q.y = at.y + 0.7f * height;
					if (system.Void(q) >= 0f || !Roof(q, 60f, out float over) || over - at.y < 0.82f * height + 0.6f)
					{
						return false;
					}
				}
				return true;
			}

			// ── Groves ────────────────────────────────────────────

			/// <summary>
			/// Glowcap groves in the rooms, caverns and halls of every cave that grows fungi (2026-10-20; before only a hall cave's
			/// halls had them): a middle glowcap or two where the roof allows, giant glowcaps round it, tufts and puffballs under.
			/// </summary>
			private void Groves(DeterministicRNG random, in CaveFungusPlan p)
			{
				float chance = Mathf.Clamp01(p.Groves * CaveFungi.CharacterWeight(system.Character));
				foreach (CaveChamber c in system.Chambers)
				{
					bool roll = random.NextFloat() < chance;
					int groves = Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * c.Radii.x * c.Radii.z / 500f), 1, 3);
					if (!roll || c.Kind == CaveChamberKind.Alcove || c.Kind == CaveChamberKind.Stope || Mathf.Max(c.Radii.x, c.Radii.z) < 6f)
					{
						continue;
					}
					for (int g = 0; g < groves; g++)
					{
						Vector3 near = InChamber(c, random.Range(0f, 2f * Mathf.PI), random.Range(0.35f, 0.82f));
						Grove(random, near);
					}
				}
			}

			private void Grove(DeterministicRNG random, Vector3 near)
			{
				int hue = random.NextFloat() < 0.3f ? Colours().FungusAccent : Colours().Fungus;
				int mids = random.Range(0, 3), giants = random.Range(2, 6), tufts = random.Range(3, 9), puffs = random.Range(0, 3);
				if (!Ground(near + Vector3.up * 1.2f, 4f, out float floor))
				{
					return;
				}
				var middle = new Vector3(near.x, floor, near.z);
				if (!Clear(middle, 1.6f) || Crowded(middle, 7f))
				{
					return;
				}
				bigs.Add(middle);
				bool lit = false;
				for (int k = 0; k < mids + giants; k++)
				{
					float a = random.Range(0f, 360f), r = k == 0 ? 0f : random.Range(1.4f, 4.2f);
					float want = random.Range(CaveFungi.MidMin, CaveFungi.MidMax), scale = random.Range(0.75f, 1.25f);
					Vector3 q = middle + Dir(a) * r;
					if (!Ground(new Vector3(q.x, middle.y + 1.2f, q.z), 2.5f, out float f) || Mathf.Abs(f - middle.y) > 1.2f)
					{
						continue;
					}
					q.y = f;
					if (!Clear(q, 1f))
					{
						continue;
					}
					if (k < mids && Mid(random, q, want, hue, light: !lit, out _))
					{
						lit = true;
						continue;
					}
					lit |= FloorGlow(random, CaveGlowKind.GiantFungus, hue, q, scale, 6f, light: !lit, lightChance: 1f);
				}
				for (int k = 0; k < tufts + puffs; k++)
				{
					float a = random.Range(0f, 360f), r = random.Range(0.8f, 5f);
					Vector3 q = middle + Dir(a) * r;
					if (Ground(new Vector3(q.x, middle.y + 1f, q.z), 2.5f, out float f) && Clear(new Vector3(q.x, f, q.z), 0.2f))
					{
						q.y = f;
						if (k < puffs)
						{
							FloorGlow(random, CaveGlowKind.Puffballs, hue, q, random.Range(0.7f, 1.2f), 5f, light: false);
						}
						else
						{
							FloorGlow(random, CaveGlowKind.Fungus, hue, q, random.Range(0.8f, 1.5f), 5f, light: false);
						}
					}
				}
			}

			// ── Fairy rings ───────────────────────────────────────

			/// <summary>
			/// A ring of glowing tufts (a fairy ring: the mycelium growing out from where it began, fruiting at its edge) on a level
			/// stretch of a room's floor, now and then a puffball where it started.
			/// </summary>
			private void Rings(DeterministicRNG random, in CaveFungusPlan p)
			{
				if (p.Rings <= 0f)
				{
					return;
				}
				foreach (CaveChamber c in system.Chambers)
				{
					bool roll = random.NextFloat() < p.Rings;
					float ring = random.Range(1.2f, 2.6f), a0 = random.Range(0f, 360f);
					int tufts = Mathf.RoundToInt(ring * random.Range(5f, 7f));
					bool puff = random.NextFloat() < 0.35f;
					int hue = random.NextFloat() < 0.4f ? Colours().FungusAccent : Colours().Fungus;
					if (!roll || c.Kind == CaveChamberKind.Alcove || c.Kind == CaveChamberKind.Stope || Mathf.Min(c.Radii.x, c.Radii.z) < 5f)
					{
						continue;
					}
					Vector3 near = InChamber(c, random.Range(0f, 2f * Mathf.PI), random.Range(0.3f, 0.7f));
					if (!Ground(near + Vector3.up * 1.2f, 3f, out float floor))
					{
						continue;
					}
					var middle = new Vector3(near.x, floor, near.z);
					if (!Clear(middle, ring + 0.3f) || Crowded(middle, ring + 3f) || !Level(middle, ring, 0.35f))
					{
						continue;
					}
					bool lit = false;
					for (int k = 0; k < tufts; k++)
					{
						float a = a0 + k * 360f / tufts + random.Range(-6f, 6f), r = ring * random.Range(0.92f, 1.08f);
						Vector3 q = middle + Dir(a) * r;
						if (Ground(new Vector3(q.x, middle.y + 0.8f, q.z), 1.6f, out float f) && Mathf.Abs(f - middle.y) < 0.4f)
						{
							lit |= FloorGlow(random, CaveGlowKind.Fungus, hue, new Vector3(q.x, f, q.z), random.Range(0.7f, 1.1f), 4f, light: !lit, lightChance: 0.5f);
						}
					}
					if (puff)
					{
						FloorGlow(random, CaveGlowKind.Puffballs, hue, middle, random.Range(0.6f, 0.9f), 3f, light: false);
					}
				}
			}

			// ── Clumps ────────────────────────────────────────────

			/// <summary>
			/// Clumps of glow over the rooms' floors (two to five pieces, where the floor is rich), and tall bracket tiers on their
			/// walls: thicker the richer the cave's plan and the denser its character.
			/// </summary>
			private void RoomClumps(DeterministicRNG random, in CaveFungusPlan p)
			{
				if (p.Clumps <= 0f)
				{
					return;
				}
				float noise = random.Range(0f, 1000f);
				bool fungi = p.Fungi && Colours().Fungi;
				foreach (CaveChamber c in system.Chambers)
				{
					float area = Mathf.PI * c.Radii.x * c.Radii.z;
					int count = Mathf.Clamp(Mathf.RoundToInt(area / 45f * p.Clumps * Density), 1, 40);
					for (int i = 0; i < count; i++)
					{
						float a = random.Range(0f, 2f * Mathf.PI), t = Mathf.Sqrt(random.Range(0.15f, 0.9f)), roll = random.NextFloat();
						int pieces = random.Range(2, 6);
						Vector3 near = InChamber(c, a, t);
						if (!Ground(near + Vector3.up * 1f, 3f, out float floor))
						{
							continue;
						}
						var at = new Vector3(near.x, floor, near.z);
						if (roll >= Rich(at, noise) * 1.3f - 0.2f || !Clear(at, 0.3f) || Crowded(at, 2f, 4f))
						{
							continue;
						}
						RichClump(random, at, pieces, fungi ? p.Puffballs : 0f);
					}
					// Bracket tiers on the walls of the rooms that grow fungi.
					if (!fungi || c.Kind == CaveChamberKind.Stope)
					{
						continue;
					}
					int tiers = Mathf.Clamp(Mathf.RoundToInt(area / 120f * p.Clumps * CaveFungi.CharacterWeight(system.Character)), 0, 6);
					float reach = Mathf.Max(c.Radii.x, c.Radii.z) + 3f;
					for (int k = 0; k < tiers; k++)
					{
						Vector3 dir = Dir(random.Range(0f, 360f));
						float h = random.Range(0.3f, 1.2f);
						Vector3 start = InChamber(c, Mathf.Atan2(Vector3.Dot(dir, Across(c)), Vector3.Dot(dir, Along(c))), 0.55f);
						start.y += h;
						if (system.Void(start) >= 0f)
						{
							continue;
						}
						WallPiece(random, CaveGlowKind.ShelfTiers, random.NextFloat() < 0.25f ? Colours().FungusAccent : Colours().Fungus, -1, start, dir, reach,
							random.Range(0.8f, 1.2f), 6f, light: true);
					}
				}
			}

			/// <summary>
			/// Two to five pieces of glow round a rich spot of floor: fungus tufts, puffballs (a share <paramref name="puffballs"/>)
			/// or crystals by the cave's crystal share, now and then a giant glowcap where the roof allows; the first may carry a light.
			/// </summary>
			private void RichClump(DeterministicRNG random, Vector3 at, int count, float puffballs)
			{
				CaveGlowPalette p = Colours();
				for (int k = 0; k < count; k++)
				{
					float a = random.Range(0f, 360f), r = k == 0 ? 0f : random.Range(0.5f, 1.6f), pick = random.NextFloat(), size = random.NextFloat();
					bool accent = random.NextFloat() < 0.25f;
					Vector3 q = at + Dir(a) * r;
					if (k > 0)
					{
						if (!Ground(new Vector3(q.x, at.y + 0.8f, q.z), 1.6f, out float f) || Mathf.Abs(f - at.y) > 0.6f)
						{
							continue;
						}
						q.y = f;
						if (!Clear(q, 0.1f))
						{
							continue;
						}
					}
					bool light = k == 0;
					if (!p.Fungi || pick < CrystalShare)
					{
						FloorGlow(random, CaveGlowKind.Crystal, accent ? p.CrystalAccent : p.Crystal, q, Mathf.Lerp(0.45f, 1.1f, size), 10f, light);
					}
					else if (pick < CrystalShare + (1f - CrystalShare) * puffballs)
					{
						FloorGlow(random, CaveGlowKind.Puffballs, accent ? p.FungusAccent : p.Fungus, q, Mathf.Lerp(0.6f, 1.2f, size), 5f, light);
					}
					else if (k == 0 && size > 0.8f)
					{
						FloorGlow(random, CaveGlowKind.GiantFungus, accent ? p.FungusAccent : p.Fungus, q, Mathf.Lerp(0.6f, 1f, size), 6f, light);
					}
					else
					{
						FloorGlow(random, CaveGlowKind.Fungus, accent ? p.FungusAccent : p.Fungus, q, Mathf.Lerp(0.8f, 1.6f, size), 5f, light);
					}
				}
			}

			/// <summary>
			/// Clumps at the walls' feet along every passage (2026-10-20; before only a crystal gallery's): every few metres, kept
			/// where the floor is rich, out of the mouths' daylight, a tuft or a crystal, a puffball now and then.
			/// </summary>
			private void WayClumps(DeterministicRNG random, in CaveFungusPlan p)
			{
				if (p.Clumps <= 0f || system.Form == CaveForm.Mine)
				{
					return;
				}
				float noise = random.Range(0f, 1000f), density = Mathf.Max(0.2f, p.Clumps * Density);
				foreach (CavePassage passage in system.Passages)
				{
					List<CaveNode> nodes = passage.Nodes;
					if (nodes.Count < 2)
					{
						continue;
					}
					float end = nodes[nodes.Count - 1].Along;
					for (float along = nodes[0].Along + random.Range(2f, 6f); along < end; along += random.Range(2.5f, 6f) / density)
					{
						float hand = random.NextFloat() < 0.5f ? -1f : 1f, roll = random.NextFloat();
						int pieces = random.Range(1, 4);
						CentreAt(nodes, along, out Vector3 c, out float nominal);
						CentreAt(nodes, along + 0.5f, out Vector3 ahead, out _);
						if (NearMouth(c))
						{
							continue;
						}
						Vector3 side = Vector3.Cross(Vector3.up, Flat(ahead - c)) * hand;
						float radius = RadiusAt(nodes, along);
						if (!WallFoot(new Vector3(c.x, nominal + 0.5f, c.z), side, 2f * radius + 1f, out Vector3 foot)
							|| !Ground(foot - side * 0.4f + Vector3.up * 0.4f, 2f, out float floor))
						{
							continue;
						}
						var at = new Vector3(foot.x - side.x * 0.4f, floor, foot.z - side.z * 0.4f);
						if (roll >= Rich(at, noise, 0.08f) * 1.2f - 0.15f || !Clear(at, 0.15f))
						{
							continue;
						}
						if (radius < 2.5f)
						{
							Clump(random, at, pieces, small: true);
						}
						else
						{
							RichClump(random, at, pieces, p.Fungi ? p.Puffballs : 0f);
						}
					}
				}
			}

			/// <summary>
			/// A wall piece of glow art of a kind on a wall a ray meets (variant drawn where <paramref name="variant"/> is −1), flat on
			/// the rock, turned no more than <paramref name="maxRoll"/> about its outward axis (bracket tiers stay level); false where
			/// none is met or the art is missing.
			/// </summary>
			private bool WallPiece(DeterministicRNG random, CaveGlowKind kind, int hue, int variant, Vector3 from, Vector3 dir, float reach, float scale, float maxRoll, bool light)
			{
				int v = variant >= 0 ? variant : random.Range(0, CaveArt.VariantCount(kind));
				float roll = random.Range(-maxRoll, maxRoll), chance = random.NextFloat();
				// A wall near upright only: the piece stands level, so on a leaning one its top would stand off the rock or sink in.
				if (!Wall(from, dir, reach, out Vector3 surface, out Vector3 into) || Mathf.Abs(into.y) > 0.35f)
				{
					return false;
				}
				if (!CaveArt.TryGlow(kind, hue, v, out CaveGlowArt art))
				{
					Missing.Add("cave glow art");
					return false;
				}
				// Level: turned to the rock about the vertical only, so tiers and stems stand as they grow, not as the wall leans.
				Vector3 flat = Flat(into);
				if (!Place(art.Prefab, surface + into * 0.02f, Quaternion.LookRotation(flat, Vector3.up) * Quaternion.Euler(0f, 0f, roll), Vector3.one * scale))
				{
					return false;
				}
				Glowing++;
				if (light && chance < 0.5f)
				{
					Light(surface - into * 0.5f, art, LightSpacing);
				}
				return true;
			}

			// ── Terraces and pits ─────────────────────────────────

			/// <summary>
			/// The layout's terraces and pits (2026-10-20, its variety pass): glow along each terrace's lip (a hand in from its
			/// edge, off its walk) and clumps at its foot in the room, a bracket tier or a glow-vine up a tall one's riser; round
			/// each pit's rim the breakdown it shed and clumps of glow, and on a dry sinkhole's floor the rock it settled with.
			/// In a mine only the spoil (its clutter's pass lays the rest).
			/// </summary>
			private void TerracesAndPits(DeterministicRNG random, in CaveFungusPlan p)
			{
				bool fungi = p.Fungi && Colours().Fungi && system.Form != CaveForm.Mine;
				float density = Mathf.Max(0.3f, p.Clumps);
				foreach (CaveTerrace t in system.Terraces)
				{
					if (t.Solid < 0 || t.Solid >= system.Solids.Count || system.Form == CaveForm.Mine)
					{
						continue;
					}
					CaveSolid slab = system.Solids[t.Solid];
					Vector3 o = Flat(t.Out);
					Vector3 a = slab.A + o * slab.Width, b = slab.B + o * slab.Width;
					float length = new Vector2(b.x - a.x, b.z - a.z).magnitude;
					float noise = random.Range(0f, 1000f);
					bool lit = false;
					for (float s = random.Range(0.4f, 1.8f); s < length; s += random.Range(1.4f, 3.2f) / density)
					{
						float roll = random.NextFloat(), footRoll = random.NextFloat(), out_ = random.Range(0.6f, 1.4f);
						int pieces = random.Range(1, 4);
						Vector3 lip = Vector3.Lerp(a, b, s / Mathf.Max(1e-3f, length));
						if (roll < Rich(lip, noise) * 1.4f - 0.1f)
						{
							Vector3 q = lip - o * 0.25f + Vector3.up * 0.8f;
							if (system.Void(q) < 0f && Ground(q, 1.6f, out float f) && Mathf.Abs(f - lip.y) < 0.5f && !OnWay(new Vector3(q.x, f, q.z), 0f))
							{
								var at = new Vector3(q.x, f, q.z);
								lit |= fungi
									? FloorGlow(random, CaveGlowKind.Fungus, Colours().Fungus, at, random.Range(0.7f, 1.1f), 4f, light: !lit, lightChance: 0.5f)
									: FloorGlow(random, CaveGlowKind.Crystal, Colours().Crystal, at, random.Range(0.4f, 0.6f), 6f, light: !lit, lightChance: 0.5f);
							}
						}
						if (footRoll < 0.35f * density)
						{
							Vector3 q = lip + o * out_;
							q.y = lip.y + 0.3f;
							if (system.Void(q) < 0f && Ground(q, t.Height + 2.5f, out float f) && f < lip.y - 0.3f)
							{
								var at = new Vector3(q.x, f, q.z);
								if (Clear(at, 0.2f))
								{
									if (fungi)
									{
										RichClump(random, at, pieces, p.Puffballs);
									}
									else
									{
										Clump(random, at, pieces, small: true);
									}
								}
							}
						}
					}
					// Up a tall terrace's riser, out of its walk's way.
					if (fungi && t.Height >= 1.4f && random.NextFloat() < 0.6f)
					{
						Vector3 mid = (a + b) * 0.5f + o * 1.2f;
						mid.y = a.y + 0.5f;
						if (system.Void(mid) < 0f && Ground(mid, t.Height + 2.5f, out float f))
						{
							bool tall = t.Height >= 2.5f;
							WallPiece(random, tall ? CaveGlowKind.ShelfTiers : CaveGlowKind.GlowVine, Colours().Fungus, tall ? -1 : 0,
								new Vector3(mid.x, f + 0.3f, mid.z), -o, 3f, random.Range(0.7f, 1f), 6f, light: true);
						}
					}
				}
				foreach (CavePit pit in system.Pits)
				{
					if (pit.Kind == CavePitKind.Gour)
					{
						continue;
					}
					int around = Mathf.Clamp(Mathf.RoundToInt(pit.Radius * 0.8f), 1, 5);
					float a0 = random.Range(0f, 360f);
					for (int k = 0; k < around; k++)
					{
						float a = a0 + k * 360f / around + random.Range(-20f, 20f), d = pit.Radius + random.Range(0.8f, 2.2f);
						bool heap = random.NextFloat() < (fungi ? 0.4f : 0.7f);
						int pieces = random.Range(2, 5);
						Vector3 q = pit.Rim + Dir(a) * d + Vector3.up * 0.8f;
						if (system.Void(q) >= 0f || !Ground(q, 2f, out float f) || Mathf.Abs(f - pit.Rim.y) > 0.8f)
						{
							continue;
						}
						var at = new Vector3(q.x, f, q.z);
						if (!Clear(at, 0.2f))
						{
							continue;
						}
						if (heap && set.Breakdown != null)
						{
							RubbleHeap(random, at, pieces, 0.6f);
						}
						else if (fungi)
						{
							RichClump(random, at, pieces, p.Puffballs);
						}
						else if (system.Form != CaveForm.Mine)
						{
							Clump(random, at, pieces, small: true);
						}
					}
					// A dry sinkhole's floor: the rock it settled with.
					if (pit.Kind == CavePitKind.Sinkhole && float.IsNaN(pit.Water) && set.Breakdown != null)
					{
						var bottom = new Vector3(pit.Rim.x, pit.Bottom + 1f, pit.Rim.z);
						if (system.Void(bottom) < 0f && Ground(bottom, 2.5f, out float f))
						{
							RubbleHeap(random, new Vector3(bottom.x, f, bottom.z) + Dir(random.Range(0f, 360f)) * (0.3f * pit.Radius), random.Range(2, 5), 0.7f);
						}
					}
				}
			}

			// ── Rubble ────────────────────────────────────────────

			/// <summary>
			/// Clusters of breakdown (2026-10-20): a block or slab fallen from the roof and the lesser pieces it broke into, heaped
			/// toward a chamber's wall where the roof shed them, and now and then at a passage's wall; of the cave's own rock (a
			/// mine's spoil is its clutter's).
			/// </summary>
			private void Rubble(DeterministicRNG random, in CaveFungusPlan p)
			{
				if (p.Rubble <= 0f || set.Breakdown == null || system.Form == CaveForm.Mine)
				{
					return;
				}
				foreach (CaveChamber c in system.Chambers)
				{
					float area = Mathf.PI * c.Radii.x * c.Radii.z;
					int clusters = Mathf.Clamp(Mathf.RoundToInt(p.Rubble * (0.4f + area / 500f) * random.Range(0.6f, 1.4f)), 0, 5);
					for (int k = 0; k < clusters; k++)
					{
						Vector3 dir = Dir(random.Range(0f, 360f));
						float inset = random.Range(1.5f, 3.5f);
						if (!WallFoot(new Vector3(c.Centre.x, c.Floor + 0.5f, c.Centre.z), dir, Mathf.Max(c.Radii.x, c.Radii.z) + 2f, out Vector3 foot, c))
						{
							continue;
						}
						RubbleHeap(random, foot - dir * inset, random.Range(3, 8), 1f);
					}
				}
				foreach (CavePassage passage in system.Passages)
				{
					List<CaveNode> nodes = passage.Nodes;
					if (nodes.Count < 2)
					{
						continue;
					}
					float end = nodes[nodes.Count - 1].Along;
					for (float along = nodes[0].Along + random.Range(10f, 30f); along < end; along += random.Range(25f, 60f) / Mathf.Max(0.3f, p.Rubble))
					{
						float hand = random.NextFloat() < 0.5f ? -1f : 1f;
						int pieces = random.Range(2, 5);
						CentreAt(nodes, along, out Vector3 c, out float nominal);
						CentreAt(nodes, along + 0.5f, out Vector3 ahead, out _);
						Vector3 side = Vector3.Cross(Vector3.up, Flat(ahead - c)) * hand;
						if (NearMouth(c) || !WallFoot(new Vector3(c.x, nominal + 0.5f, c.z), side, 2f * RadiusAt(nodes, along) + 1f, out Vector3 foot))
						{
							continue;
						}
						RubbleHeap(random, foot - side * 0.7f, pieces, 0.6f);
					}
				}
			}

			/// <summary>One heap about a point: its greatest piece first, the lesser scattered round it, each set on the floor it lands on, none on a way.</summary>
			private void RubbleHeap(DeterministicRNG random, Vector3 near, int pieces, float size)
			{
				for (int i = 0; i < pieces; i++)
				{
					float a = random.Range(0f, 360f), r = i == 0 ? 0f : random.Range(0.6f, 2.4f) * size;
					float s = (i == 0 ? random.Range(0.45f, 0.9f) : random.Range(0.14f, 0.4f)) * size;
					Quaternion turn = Quaternion.Euler(random.Range(-14f, 14f), random.Range(0f, 360f), random.Range(-14f, 14f));
					var stretch = new Vector3(random.Range(0.8f, 1.25f), 1f, random.Range(0.8f, 1.25f));
					Vector3 q = near + Dir(a) * r;
					if (!FloorAndRoof(new Vector3(q.x, near.y + 0.5f, q.z), out float floor, out float roof) || roof - floor < 1.5f)
					{
						continue;
					}
					var at = new Vector3(q.x, floor, q.z);
					if (InLane(at, 0.8f + s) || OnWay(at, 0.5f) || Submerged(at) || Crowded(at, 2.5f, 4f))
					{
						continue;
					}
					Place(Formation(random, set.Breakdown), at - Vector3.up * (0.08f * s), turn, Vector3.Scale(stretch, Vector3.one * s));
				}
			}

			/// <summary>A formation's prefab drawn from a stream of the caller's (<see cref="Formation(string, string)"/> draws from the dressing's own).</summary>
			private GameObject Formation(DeterministicRNG random, string shape)
			{
				int variant = random.Range(0, RockTypes.VariantCount);
				(string artRock, string artShape) = FormationArt(set.Rock, shape, system.Form);
				string name = RockArtNames.FormationPrefab(artRock, artShape, variant);
				GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(name));
				if (prefab == null)
				{
					Missing.Add($"{artRock} {artShape}");
				}
				return prefab;
			}
		}
	}
}
#endif
