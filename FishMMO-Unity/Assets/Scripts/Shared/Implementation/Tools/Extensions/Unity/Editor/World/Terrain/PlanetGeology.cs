#if UNITY_EDITOR
using System;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What a rock is made of, as far as wearing it away is concerned.</summary>
	public enum GeologyFamily
	{
		/// <summary>Cooled from melt: granite, basalt, tuff.</summary>
		Igneous,
		/// <summary>Laid down in beds: sandstone, shale, limestone.</summary>
		Sedimentary,
		/// <summary>Recrystallised: slate, schist, gneiss, marble, quartzite.</summary>
		Metamorphic,
		/// <summary>An ice world's shell: water ice, as hard as rock at those temperatures.</summary>
		Ice,
	}

	/// <summary>How a rock's resistance is arranged in space, which is what erosion turns into landforms.</summary>
	public enum StrataStyle
	{
		/// <summary>The same everywhere: granite domes, quartzite ridges.</summary>
		Massive,
		/// <summary>Beds of different resistance stacked on a plane: benches, ledges, mesas when it lies flat.</summary>
		Bedded,
		/// <summary>Lava flows stacked one on another: hard interiors, softer rubbly tops.</summary>
		Flows,
		/// <summary>Steep bands from folding and recrystallisation: ribs and grooves across the ground.</summary>
		Foliated,
	}

	/// <summary>One kind of rock: how hard it is, how it dissolves, and how its resistance is laid out.</summary>
	public readonly struct Lithology
	{
		/// <summary>Stable name. For every family but ice it is also the art's <see cref="RockType.Name"/>.</summary>
		public readonly string Name;
		public readonly GeologyFamily Family;
		/// <summary>Resistance to wear, 0 soft clay … 1 the hardest quartzite, before the beds vary it.</summary>
		public readonly float Hardness;
		/// <summary>How readily water dissolves it, 0 … 1: what makes karst.</summary>
		public readonly float Solubility;
		public readonly StrataStyle Style;
		/// <summary>The thinnest and thickest bed, in scene metres.</summary>
		public readonly float BedMinMetres;
		public readonly float BedMaxMetres;
		/// <summary>How far a bed's hardness swings from <see cref="Hardness"/>, either way.</summary>
		public readonly float BedContrast;
		/// <summary>Needs carbonate, which only forms under liquid water.</summary>
		public readonly bool NeedsLiquidWater;

		public Lithology(string name, GeologyFamily family, float hardness, float solubility, StrataStyle style,
			float bedMin, float bedMax, float contrast, bool needsLiquidWater = false)
		{
			Name = name;
			Family = family;
			Hardness = hardness;
			Solubility = solubility;
			Style = style;
			BedMinMetres = bedMin;
			BedMaxMetres = bedMax;
			BedContrast = contrast;
			NeedsLiquidWater = needsLiquidWater;
		}

		/// <summary>The art's rock type of the same name, for cliffs and boulders to match; null for ice.</summary>
		public string RockTypeName => Family == GeologyFamily.Ice ? null : Name;
	}

	/// <summary>
	/// The rock under one point of a planet, all the way down: its province, its kind, and where its
	/// beds lie, so hardness at any altitude is cheap to ask.
	/// </summary>
	/// <remarks>
	/// Erosion asks the hardness of the ground it is lowering again and again as it lowers it. The
	/// province, its kind and the plane its beds lie on do not change as the ground does, so they are
	/// worked out once per point (<see cref="PlanetGeology.ColumnAt"/>), and each question after that
	/// is a little arithmetic and one hash.
	/// </remarks>
	public readonly struct GeologyColumn
	{
		/// <summary>Which province of the planet's rock this point is in: the same number everywhere in it.</summary>
		public readonly int Province;
		/// <summary>Index into <see cref="PlanetGeology.Lithologies"/>.</summary>
		public readonly int LithologyIndex;
		/// <summary>How steeply the province's beds or bands dip, in degrees.</summary>
		public readonly float DipDegrees;
		/// <summary>Metres to the nearest edge of the province: where its rock meets another's.</summary>
		public readonly float BoundaryMetres;

		/// <summary>Metres added to an altitude to place it in the province's stack of beds.</summary>
		private readonly float offset;
		private readonly float bedMetres;
		private readonly int bedSeed;

		public GeologyColumn(int province, int lithology, float dipDegrees, float boundaryMetres, float offset, float bedMetres, int bedSeed)
		{
			Province = province;
			LithologyIndex = lithology;
			DipDegrees = dipDegrees;
			BoundaryMetres = boundaryMetres;
			this.offset = offset;
			this.bedMetres = bedMetres;
			this.bedSeed = bedSeed;
		}

		public Lithology Lithology => PlanetGeology.Lithologies[LithologyIndex];

		/// <summary>
		/// True when the beds lie flat enough to wear into benches and tables: bedded rock or stacked
		/// flows dipping no more than <see cref="PlanetGeology.FlatLyingDegrees"/>.
		/// </summary>
		public bool FlatLying
		{
			get
			{
				StrataStyle style = Lithology.Style;
				return (style == StrataStyle.Bedded || style == StrataStyle.Flows) && DipDegrees <= PlanetGeology.FlatLyingDegrees;
			}
		}

		/// <summary>
		/// Metres added to an altitude at this point to place it in the province's stack of beds.
		/// </summary>
		/// <remarks>
		/// Within one province it is the beds' plane plus a wander hundreds of metres across, so it
		/// interpolates between neighbouring points of the same province: a fine grid can ask a few
		/// columns and share their offsets out (see <see cref="SceneGeologyGrid"/>).
		/// </remarks>
		public float Offset => offset;

		/// <summary>The province's mean bed thickness in metres; 0 for massive rock.</summary>
		public float BedMetres => Lithology.Style == StrataStyle.Massive ? 0f : bedMetres;

		/// <summary>Which bed a point at this altitude is in; 0 throughout massive rock.</summary>
		/// <param name="altitudeMetres">Scene metres above the body's sea level.</param>
		public int BedAt(float altitudeMetres) => BedAtStrata(altitudeMetres + offset);

		/// <summary>Resistance to wear at this altitude, 0 … 1: the rock's, swung harder or softer by the bed.</summary>
		/// <param name="altitudeMetres">Scene metres above the body's sea level.</param>
		public float HardnessAt(float altitudeMetres) => HardnessAtStrata(altitudeMetres + offset);

		/// <summary><see cref="BedAt"/> for a position in the stack already offset (altitude + <see cref="Offset"/>).</summary>
		public int BedAtStrata(float strataMetres)
		{
			if (Lithology.Style == StrataStyle.Massive || bedMetres <= 0f)
			{
				return 0;
			}
			return Mathf.FloorToInt(BedCoordinate(strataMetres));
		}

		/// <summary>Position in the stack in beds: whole numbers at the contacts.</summary>
		private float BedCoordinate(float strataMetres)
		{
			float t = strataMetres / bedMetres;
			if (FlatLying)
			{
				// Flat-lying beds keep an even thickness, so a plateau's benches land on their caps.
				return t;
			}
			/* Beds are not all one thickness. The bed coordinate is warped by a smooth 1D noise whose
			 * slope never reaches 1 (amplitude 0.8 times at most 0.75 per bed), so the warp stays
			 * monotone: beds thicken and thin, but never fold back over one another. */
			return t + ThicknessWarp * (Noise1D(t * 0.5f, bedSeed) - 0.5f);
		}

		/// <summary><see cref="HardnessAt"/> for a position in the stack already offset (altitude + <see cref="Offset"/>).</summary>
		public float HardnessAtStrata(float strataMetres)
		{
			Lithology rock = Lithology;
			if (rock.Style == StrataStyle.Massive || bedMetres <= 0f)
			{
				return rock.Hardness;
			}
			float t = BedCoordinate(strataMetres);
			int bed = Mathf.FloorToInt(t);
			float within = t - bed;
			float here = BedHardness(rock, bed);
			/* Contacts are not knife-edges. Over the outer BedContact of each bed the hardness eases
			 * to the mean of the two beds at the contact, so the water that cuts through a soft bed
			 * meets the hard one below gradually rather than stopping dead on a flat floor — which,
			 * with a step here, cut every gully into a flat-bottomed slot along every bed. */
			if (within < BedContact)
			{
				float w = 0.5f + 0.5f * Smooth01(within / BedContact);
				return Mathf.Lerp(BedHardness(rock, bed - 1), here, w);
			}
			if (within > 1f - BedContact)
			{
				float w = 0.5f + 0.5f * Smooth01((1f - within) / BedContact);
				return Mathf.Lerp(BedHardness(rock, bed + 1), here, w);
			}
			return here;
		}

		/// <summary>In flat-lying sediment, beds per cycle: every this many, a hard bed caps the soft ones below it (<see cref="PlateauTerrace.BedsPerCycle"/>).</summary>
		public const int CapEvery = PlateauTerrace.BedsPerCycle;

		/// <summary>Share of a bed at each side over which its hardness eases into its neighbour's.</summary>
		public const float BedContact = 0.2f;

		/// <summary>The hardness of one whole bed: a hard member or a soft one — the alternation that leaves ledges standing over slopes — and by how much is its own.</summary>
		private float BedHardness(in Lithology rock, int bed)
		{
			uint h = ProceduralNoise.Hash(bed, 0, 0, bedSeed);
			if (FlatLying && rock.Style == StrataStyle.Bedded)
			{
				/* Flat-lying sediment comes in cycles: soft beds capped by a hard one, again and again
				 * (cyclothems; the Grand Canyon's walls are a stack of them). The cap is what holds up a
				 * mesa's top and a bench's edge, so the plateau pass lays its treads on the caps
				 * (PlateauTerrace). The cap is the top bed of every CapEvery. */
				bool cap = ((bed % CapEvery) + CapEvery) % CapEvery == CapEvery - 1;
				float strength = 0.75f + 0.25f * ProceduralNoise.ToUnit(h);
				return Mathf.Clamp(rock.Hardness + rock.BedContrast * (cap ? 1f : -1f) * strength, 0.02f, 1f);
			}
			float side = ProceduralNoise.ToUnit(h) < 0.5f ? -1f : 1f;
			float magnitude = 0.5f + 0.5f * ProceduralNoise.ToUnit(ProceduralNoise.Mix(h));
			return Mathf.Clamp(rock.Hardness + rock.BedContrast * side * magnitude, 0.02f, 1f);
		}

		private static float Smooth01(float t) => t * t * (3f - 2f * t);

		/// <summary>How far the bed coordinate is warped, in beds.</summary>
		private const float ThicknessWarp = 0.8f;

		private static float Noise1D(float x, int seed)
		{
			int i = Mathf.FloorToInt(x);
			float f = x - i;
			float a = ProceduralNoise.ToUnit(ProceduralNoise.Hash(i, 1, 0, seed));
			float b = ProceduralNoise.ToUnit(ProceduralNoise.Hash(i + 1, 1, 0, seed));
			return Mathf.Lerp(a, b, f * f * (3f - 2f * f));
		}
	}

	/// <summary>
	/// A planet's rock: which kind lies where, and how its beds are laid, as a pure function of the
	/// direction from the planet's centre.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Provinces, not noise.</b> Rock comes in regions: a granite batholith, a basin of flat
	/// sandstone, a belt of folded slate. The planet is divided into provinces about
	/// <see cref="ProvinceMetres"/> across — cells of a 3D Voronoi diagram over the atlas sphere,
	/// so they have no seams and no poles — and each province is one kind of rock, its beds on one
	/// plane. Where provinces meet the rock changes abruptly, as it does at a real contact or fault,
	/// and erosion will find that line.
	/// </para>
	/// <para>
	/// <b>On the atlas globe, like the scenes.</b> Positions are the atlas radius's metres, which are
	/// the scenes' horizontal metres, so two scenes cut side by side read the same province across
	/// their shared edge and the same bed at the same altitude.
	/// </para>
	/// <para>
	/// <b>What the world allows.</b> Which kinds of rock are common follows the body
	/// (<see cref="BiomeWorldConditions"/>): bedded sediment needs a fluid to lay it down, so an
	/// airless rock is mostly basalt and anorthosite-like massive rock; carbonates (limestone, chalk,
	/// marble) need liquid water; a volcanic world is mostly lava; an ice world's crust is ice.
	/// The weights are starting values, like the terrain process profiles, and are tuned against the
	/// shapes erosion makes.
	/// </para>
	/// <para>
	/// Safe to share between threads once built: every question is arithmetic on its arguments.
	/// </para>
	/// </remarks>
	public sealed class PlanetGeology
	{
		/// <summary>Roughly how far across a province is, in atlas metres.</summary>
		/// <remarks>
		/// A few kilometres more than a large scene, so most scenes stand on one or two kinds of rock
		/// and a contact crossing a scene is an event, while a 30 km atlas globe still holds about a
		/// hundred provinces.
		/// </remarks>
		public const float ProvinceMetres = 12000f;

		/// <summary>The steepest dip at which beds still count as lying flat, in degrees.</summary>
		/// <remarks>The Colorado Plateau's strata dip a degree or two; past a few degrees beds wear into hogbacks and cuestas instead of tables.</remarks>
		public const float FlatLyingDegrees = 3f;

		/// <summary>How far a bed's surface wanders from its plane, as a share of the bed thickness.</summary>
		public const float WarpBeds = 0.6f;

		/// <summary>The horizontal scale of that wander, in metres.</summary>
		public const float WarpMetres = 600f;

		/// <summary>Every kind of rock a province can be.</summary>
		public static readonly Lithology[] Lithologies =
		{
			new Lithology("Granite", GeologyFamily.Igneous, 0.9f, 0f, StrataStyle.Massive, 0f, 0f, 0f),
			new Lithology("Basalt", GeologyFamily.Igneous, 0.8f, 0f, StrataStyle.Flows, 6f, 15f, 0.3f),
			new Lithology("Andesite", GeologyFamily.Igneous, 0.8f, 0f, StrataStyle.Flows, 8f, 20f, 0.25f),
			new Lithology("Tuff", GeologyFamily.Igneous, 0.35f, 0f, StrataStyle.Bedded, 3f, 10f, 0.4f),
			new Lithology("Sandstone", GeologyFamily.Sedimentary, 0.6f, 0.05f, StrataStyle.Bedded, 5f, 20f, 0.5f),
			new Lithology("Shale", GeologyFamily.Sedimentary, 0.25f, 0f, StrataStyle.Bedded, 3f, 10f, 0.35f),
			new Lithology("Limestone", GeologyFamily.Sedimentary, 0.65f, 0.9f, StrataStyle.Bedded, 4f, 15f, 0.35f, true),
			new Lithology("Chalk", GeologyFamily.Sedimentary, 0.35f, 0.7f, StrataStyle.Bedded, 6f, 20f, 0.15f, true),
			new Lithology("Conglomerate", GeologyFamily.Sedimentary, 0.55f, 0.05f, StrataStyle.Bedded, 5f, 15f, 0.4f),
			new Lithology("Slate", GeologyFamily.Metamorphic, 0.75f, 0f, StrataStyle.Foliated, 2f, 6f, 0.2f),
			new Lithology("Schist", GeologyFamily.Metamorphic, 0.6f, 0f, StrataStyle.Foliated, 2f, 8f, 0.3f),
			new Lithology("Gneiss", GeologyFamily.Metamorphic, 0.85f, 0f, StrataStyle.Foliated, 4f, 12f, 0.2f),
			new Lithology("Marble", GeologyFamily.Metamorphic, 0.6f, 0.8f, StrataStyle.Massive, 0f, 0f, 0f, true),
			new Lithology("Quartzite", GeologyFamily.Metamorphic, 0.95f, 0f, StrataStyle.Massive, 0f, 0f, 0f),
			new Lithology("Ice", GeologyFamily.Ice, 0.7f, 0f, StrataStyle.Bedded, 5f, 20f, 0.15f),
		};

		/// <summary>How common each lithology is within its family, before the world's own filter.</summary>
		private static readonly float[] WithinFamily =
		{
			0.4f, 0.4f, 0.1f, 0.1f,             // granite, basalt, andesite, tuff
			/* Shale is the commonest sedimentary rock at the surface, then sandstone and limestone. Sandstone 0.35 and
			 * conglomerate 0.15 made them the likeliest provinces on an Earth-like world (19% and 8%), so a quarter of
			 * scenes stood on sandstone-coloured rock (Jim, 2026-10-08). Now 12% and 6%. */
			0.22f, 0.35f, 0.25f, 0.07f, 0.11f,  // sandstone, shale, limestone, chalk, conglomerate
			0.15f, 0.3f, 0.35f, 0.1f, 0.1f,     // slate, schist, gneiss, marble, quartzite
			1f,                                 // ice
		};

		private readonly int seed;
		private readonly double radiusMetres;
		/// <summary>Each lithology's share of the planet's provinces, summing to one.</summary>
		private readonly float[] shares;

		private PlanetGeology(int seed, double radiusMetres, float[] shares)
		{
			this.seed = seed;
			this.radiusMetres = radiusMetres;
			this.shares = shares;
		}

		/// <summary>The geology of a body, laid over its atlas globe.</summary>
		/// <param name="body">The body; its terrain seed decides where the provinces are.</param>
		/// <param name="conditions">What the world is: its air, water, heat and ice decide which rocks are common.</param>
		/// <param name="atlasRadiusKm">The radius scenes are cut at.</param>
		/// <remarks>Main thread: the seed can come from the asset's name.</remarks>
		public static PlanetGeology For(WorldBody body, in BiomeWorldConditions conditions, double atlasRadiusKm)
		{
			uint terrainSeed = body != null ? body.ResolvedTerrainSeed : 1u;
			return new PlanetGeology(unchecked((int)ProceduralNoise.Mix(terrainSeed ^ 0x6E010A1u)),
				Math.Max(1.0, atlasRadiusKm) * 1000.0, Shares(conditions));
		}

		/// <summary>The geology of the body a scene is cut from, at the radius it is cut at.</summary>
		public static PlanetGeology For(SceneGenerationRequest request, SolarSystemProfile system)
		{
			return For(request.Body, BiomeWorldConditions.For(system, request.Body), request.ResolvedRadiusKm);
		}

		/// <summary>Each lithology's share of a world's provinces.</summary>
		public static float[] Shares(in BiomeWorldConditions conditions)
		{
			bool liquid = conditions.HasLiquidWater;
			bool fluid = liquid || conditions.HasMethaneCycle;
			bool airless = conditions.Atmosphere == AtmosphereKind.None;
			bool thin = conditions.Atmosphere == AtmosphereKind.Thin;

			/* Bedded sediment needs something to carry and lay it down; without water or air there
			 * is only impact rubble on lava. Lava dominates a world that erupts and has no fluid to
			 * bury it, and every airless rock is mostly its old crust. */
			float igneous = 0.2f + (conditions.IsVolcanic && !fluid ? 0.5f : 0f) + (airless ? 0.4f : thin ? 0.2f : 0f);
			float sedimentary = fluid ? 0.55f : thin ? 0.15f : airless ? 0.02f : 0.1f;
			float metamorphic = 0.25f;
			float ice = conditions.IsIceWorld ? 4f : 0f;

			var shares = new float[Lithologies.Length];
			float total = 0f;
			for (int i = 0; i < Lithologies.Length; i++)
			{
				Lithology rock = Lithologies[i];
				if (rock.NeedsLiquidWater && !liquid)
				{
					continue;
				}
				float family = rock.Family switch
				{
					GeologyFamily.Igneous => igneous,
					GeologyFamily.Sedimentary => sedimentary,
					GeologyFamily.Metamorphic => metamorphic,
					_ => ice,
				};
				shares[i] = family * WithinFamily[i];
				total += shares[i];
			}
			for (int i = 0; i < shares.Length; i++)
			{
				shares[i] = total > 0f ? shares[i] / total : 0f;
			}
			return shares;
		}

		/// <summary>The rock under a direction from the planet's centre.</summary>
		/// <param name="direction">Unit (or near-unit) direction, as <see cref="SceneAltitude"/> asks the planet.</param>
		public GeologyColumn ColumnAt(Vector3 direction)
		{
			Vector3 n = direction.normalized;
			double px = n.x * radiusMetres, py = n.y * radiusMetres, pz = n.z * radiusMetres;

			// The nearest province centre, from the 27 cells around the point.
			double s = ProvinceMetres;
			int cx = (int)Math.Floor(px / s), cy = (int)Math.Floor(py / s), cz = (int)Math.Floor(pz / s);
			Span<double> points = stackalloc double[27 * 4];
			int nearest = 0;
			int bx = 0, by = 0, bz = 0;
			int cell = 0;
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dy = -1; dy <= 1; dy++)
				{
					for (int dx = -1; dx <= 1; dx++, cell++)
					{
						int x = cx + dx, y = cy + dy, z = cz + dz;
						FeaturePoint(x, y, z, out double qx, out double qy, out double qz);
						double ex = qx - px, ey = qy - py, ez = qz - pz;
						points[cell * 4] = qx;
						points[cell * 4 + 1] = qy;
						points[cell * 4 + 2] = qz;
						points[cell * 4 + 3] = ex * ex + ey * ey + ez * ez;
						if (points[cell * 4 + 3] < points[nearest * 4 + 3] || cell == 0)
						{
							nearest = cell;
							bx = x; by = y; bz = z;
						}
					}
				}
			}
			double fx = points[nearest * 4], fy = points[nearest * 4 + 1], fz = points[nearest * 4 + 2];
			double best = points[nearest * 4 + 3];

			/* The province's edge: the nearest of the planes bisecting it from its neighbours. Not
			 * necessarily the plane shared with the second-nearest centre — a farther centre can sit
			 * at an angle that brings its face closer — so every neighbour is asked. */
			double edge = double.MaxValue;
			for (int k = 0; k < 27; k++)
			{
				if (k == nearest)
				{
					continue;
				}
				double ox = points[k * 4] - fx, oy = points[k * 4 + 1] - fy, oz = points[k * 4 + 2] - fz;
				double between = Math.Sqrt(ox * ox + oy * oy + oz * oz);
				if (between > 1e-6)
				{
					edge = Math.Min(edge, (points[k * 4 + 3] - best) / (2.0 * between));
				}
			}
			float boundary = (float)edge;

			uint h = ProceduralNoise.Hash(bx, by, bz, seed);
			int province = unchecked((int)h);
			int lithology = Pick(ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x51u)));
			Lithology rock = Lithologies[lithology];

			float dip = Dip(rock.Style, ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0xD1Bu)));
			float bed = Mathf.Lerp(rock.BedMinMetres, rock.BedMaxMetres, ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0xBEDu)));

			/* Where in the stack this point stands: the plane the beds lie on, tilted by the dip
			 * along the province's dip direction, plus a gentle wander so a bed's outcrop is not a
			 * ruler-straight contour, plus the province's own phase so beds do not line up across a
			 * contact. In double until the end: the positions are planet-sized, the differences are
			 * metres. */
			Vector3 centre = new Vector3((float)fx, (float)fy, (float)fz).normalized;
			Vector3 dipDirection = TangentDirection(centre, ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0xA21u)) * Mathf.PI * 2f);
			double along = (px - fx) * dipDirection.x + (py - fy) * dipDirection.y + (pz - fz) * dipDirection.z;
			double plane = Math.Tan(dip * Mathf.Deg2Rad) * along;
			float wander = WarpBeds * bed * 2f * (ValueNoise((float)(px / WarpMetres), (float)(py / WarpMetres), (float)(pz / WarpMetres), seed ^ province) - 0.5f);
			float phase = ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x9A5u)) * bed * 10f;

			return new GeologyColumn(province, lithology, dip, boundary, (float)plane + wander + phase, bed, unchecked((int)ProceduralNoise.Mix(h ^ 0x5EEDu)));
		}

		/// <summary>The lithology a province's draw lands on, by the world's shares.</summary>
		private int Pick(float u)
		{
			float sum = 0f;
			int last = 0;
			for (int i = 0; i < shares.Length; i++)
			{
				if (shares[i] <= 0f)
				{
					continue;
				}
				sum += shares[i];
				last = i;
				if (u < sum)
				{
					return i;
				}
			}
			return last;
		}

		/// <summary>
		/// How steeply a province's beds dip. Sediment and lava mostly lie flat — most of the world's
		/// sedimentary cover dips a few degrees at most — with a minority folded; foliation stands
		/// steep.
		/// </summary>
		private static float Dip(StrataStyle style, float u)
		{
			switch (style)
			{
				case StrataStyle.Bedded:
					return u < 0.6f ? u / 0.6f * FlatLyingDegrees : Mathf.Lerp(FlatLyingDegrees, 40f, (u - 0.6f) / 0.4f);
				case StrataStyle.Flows:
					return u * 5f;
				case StrataStyle.Foliated:
					return Mathf.Lerp(50f, 85f, u);
				default:
					return 0f;
			}
		}

		/// <summary>The feature point of a Voronoi cell: somewhere inside it, the same every time.</summary>
		private void FeaturePoint(int x, int y, int z, out double qx, out double qy, out double qz)
		{
			uint h = ProceduralNoise.Hash(x, y, z, seed);
			double s = ProvinceMetres;
			qx = (x + ProceduralNoise.ToUnit(h)) * s;
			qy = (y + ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x1u))) * s;
			qz = (z + ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x2u))) * s;
		}

		/// <summary>A unit direction in the plane tangent to the sphere at <paramref name="normal"/>, at an angle.</summary>
		private static Vector3 TangentDirection(Vector3 normal, float angle)
		{
			Vector3 reference = Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right;
			Vector3 e1 = Vector3.Cross(normal, reference).normalized;
			Vector3 e2 = Vector3.Cross(normal, e1);
			return e1 * Mathf.Cos(angle) + e2 * Mathf.Sin(angle);
		}

		/// <summary>Smooth 3D value noise in [0, 1].</summary>
		private static float ValueNoise(float x, float y, float z, int seed)
		{
			int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y), iz = Mathf.FloorToInt(z);
			float fx = Smooth(x - ix), fy = Smooth(y - iy), fz = Smooth(z - iz);
			float Corner(int ox, int oy, int oz) => ProceduralNoise.ToUnit(ProceduralNoise.Hash(ix + ox, iy + oy, iz + oz, seed));
			float x00 = Mathf.Lerp(Corner(0, 0, 0), Corner(1, 0, 0), fx);
			float x10 = Mathf.Lerp(Corner(0, 1, 0), Corner(1, 1, 0), fx);
			float x01 = Mathf.Lerp(Corner(0, 0, 1), Corner(1, 0, 1), fx);
			float x11 = Mathf.Lerp(Corner(0, 1, 1), Corner(1, 1, 1), fx);
			return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
		}

		private static float Smooth(float t) => t * t * (3f - 2f * t);
	}
}
#endif
