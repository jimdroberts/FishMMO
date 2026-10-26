using System;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	public static partial class BiomeLandforms
	{
		private const float TwoPi = MathF.PI * 2f;

		/// <summary>A rampart crater's distal reach, radii (its Crown, 1.2 … 2.5; 1.8 when unset).</summary>
		private static float RampartReach(in BiomeLandform f) => f.Crown > 0f ? Mathf.Clamp(f.Crown, 1.2f, 2.5f) : 1.8f;

		/// <summary>The angle between two headings, radians, 0 … π.</summary>
		private static float AngleBetween(float a, float b)
		{
			float d = (a - b) % TwoPi;
			if (d < 0f)
			{
				d += TwoPi;
			}
			return d > MathF.PI ? TwoPi - d : d;
		}

		/// <summary>
		/// The shapes added for the drafts written after the first landform phase (2026-10-25, phase I2g-b, decisions 55 and 61–65):
		/// the sheer tower and the stepped pyramid (Mound), the multi-ring basin and the summit caldera (Crater), the embanked tank
		/// (Depression), the pit chain and the spiral troughs (Channels), the cockpit cone field and the labyrinth (Waves), and the
		/// small tuners the drafts asked of the old ones: a flat-floored hollow, a table's axial cleft, a moat scooped by the wind.
		/// </summary>
		/// <remarks>
		/// Every one keeps the contract of the rest: a pure function of the site and the point, zero outside the site's bounds and
		/// meeting the untouched ground there without a step.
		/// </remarks>
		public sealed partial class Shape
		{
			// ── Mound: the tower and the pyramid ─────────────────────

			/// <summary>A tower's shoulder: the share of its radius inside which it is cap, outside which sheer flank.</summary>
			private float TowerShoulder
			{
				get
				{
					float flank = Site.Form.Flank > 0f ? Site.Form.Flank : 78f;
					float run = Mathf.Abs(Site.Height) / (MathF.Tan(Mathf.Clamp(flank, 5f, 88f) * Mathf.Deg2Rad) * radius);
					return 1f - Mathf.Clamp(run, 0.03f, 0.7f);
				}
			}

			/// <summary>A tower's distance measure: its plan's, drawn in a little at each flute so the flanks are grooved top to foot.</summary>
			private float TowerQ(float u, float v)
			{
				BiomeLandform f = Site.Form;
				float qe = MathF.Sqrt(u / halfLength * (u / halfLength) + v / radius * (v / radius));
				if (f.Count <= 0 || f.Roughness <= 0f)
				{
					return qe;
				}
				float theta = MathF.Atan2(v, u);
				float flute = MathF.Pow(0.5f + 0.5f * MathF.Cos(f.Count * theta + TwoPi * Unit(Site.Seed) + 0.8f * Noise(qe * 3f, theta * 0.5f, Site.Seed ^ 0x7Fu)), 6f);
				return qe / (1f - 0.1f * f.Roughness * flute);
			}

			/// <summary>A tower's height share at q: a rounded cap (its Top, 0 flat … 1 a dome dropping a third) and then the sheer flank.</summary>
			private float TowerProfile(float q)
			{
				if (q >= 1f)
				{
					return 0f;
				}
				float q0 = TowerShoulder;
				float drop = 0.35f * Mathf.Clamp01(Site.Form.Top);
				if (q < q0)
				{
					float t = q / q0;
					return 1f - drop * t * t;
				}
				return (1f - drop) * (1f - S01((q - q0) / (1f - q0)));
			}

			/// <summary>A superellipse's distance measure: 1 on a rounded square (power 6 a pyramid's, 8 a tank's).</summary>
			private static float SquareQ(float a, float b, float power)
			{
				return MathF.Pow(MathF.Pow(MathF.Abs(a), power) + MathF.Pow(MathF.Abs(b), power), 1f / power);
			}

			/// <summary>A pyramid's tiers: flat treads and steep risers, the top tread its platform, nothing at its foot.</summary>
			private float PyramidSteps(float qq)
			{
				float lin = 1f - Mathf.Clamp01(qq);
				int steps = Mathf.Max(1, Site.Form.Count);
				float level = lin * steps;
				float k = MathF.Floor(level);
				return Mathf.Min(1f, (k + S01((level - k - 0.7f) / 0.3f)) / steps);
			}

			/// <summary>
			/// How much of the local point (u, v) is a pyramid's axial stair (or its moat's causeway), 0 … 1: the faces its Rim names
			/// (1 the +u face, the east one when cardinal; 2 both u faces; 4 every face), its HalfWidth either side of the axis.
			/// </summary>
			private float StairAt(float u, float v)
			{
				BiomeLandform f = Site.Form;
				int faces = Mathf.RoundToInt(f.Rim);
				if (f.Form != BiomeLandformForm.Pyramid || faces <= 0)
				{
					return 0f;
				}
				float half = Mathf.Max(0.5f, Site.HalfWidth);
				float alongU = 1f - S01((MathF.Abs(v) - half) / (0.5f * half + 0.5f));
				if (faces == 1)
				{
					return alongU * S01(u / Mathf.Max(0.5f, 0.1f * halfLength));
				}
				if (faces < 4)
				{
					return alongU;
				}
				float alongV = 1f - S01((MathF.Abs(u) - half) / (0.5f * half + 0.5f));
				return Mathf.Max(alongU, alongV);
			}

			/// <summary>
			/// The share of a mound's moat at a point: all of it, less on the lee for a wind-scooped one (its MoatBias: a nunatak's
			/// scoop lies upwind), and none on a pyramid's causeways.
			/// </summary>
			private float MoatShare(float x, float z)
			{
				BiomeLandform f = Site.Form;
				if (f.MoatBias <= 0f && f.Form != BiomeLandformForm.Pyramid)
				{
					return 1f;
				}
				Local(x, z, out float u, out float v);
				float share = 1f;
				if (f.MoatBias > 0f)
				{
					float r = MathF.Sqrt(u * u + v * v);
					float windward = r > 1e-3f ? 0.5f * (1f - u / r) : 0.5f;
					share = 1f - f.MoatBias + f.MoatBias * Mathf.Min(1f, 1.6f * windward);
				}
				return share * (1f - StairAt(u, v));
			}

			// ── Depression: the flat hollow and the tank ─────────────

			/// <summary>A hollow's cut at q: a dish, or (with a Top) a flat floor and steep sides (Swiss-cheese pits, dolines).</summary>
			private float HollowProfile(float q)
			{
				float top = Mathf.Clamp(Site.Form.Top, 0f, 0.9f);
				if (top <= 0f)
				{
					return Bump(q);
				}
				return q >= 1f ? 0f : 1f - S01((q - top) / (1f - top));
			}

			private float TankIsland(float u, float v)
			{
				BiomeLandform f = Site.Form;
				if (f.Crown <= 0f)
				{
					return 0f;
				}
				float qi = MathF.Sqrt(u * u + v * v) / Mathf.Max(0.5f, f.Crown * radius);
				return 1f - S01((qi - 0.6f) / 0.4f);
			}

			private float Tank(float u, float v, float current)
			{
				BiomeLandform f = Site.Form;
				float q = SquareQ(u / halfLength, v / radius, 8f);
				float floor = !float.IsNaN(Site.Level) ? Site.Level : Site.Ground - Site.Height;
				float w = 1f - S01((q - 0.9f) / 0.1f);
				float target = current - w * Mathf.Max(0f, current - floor);
				if (f.Rim > 0f)
				{
					target += f.Rim * Bump((q - 1.08f) / 0.12f);
				}
				float island = TankIsland(u, v);
				if (island > 0f)
				{
					target += (Site.Ground + f.CrownDepth - target) * island;
				}
				return target;
			}

			private float TankMask(BiomeLandformPart part, float u, float v)
			{
				float q = SquareQ(u / halfLength, v / radius, 8f);
				float island = TankIsland(u, v);
				switch (part)
				{
					case BiomeLandformPart.Floor: return (1f - S01((q - 0.85f) / 0.05f)) * (1f - island);
					case BiomeLandformPart.Rim: return Site.Form.Rim > 0f ? Bump((q - 1.08f) / 0.12f) : 0f;
					case BiomeLandformPart.Top: return island;
					case BiomeLandformPart.Flank: return S01((q - 0.85f) / 0.05f) * (1f - S01((q - 1f) / 0.05f));
					default: return 1f - S01((q - 1.2f) / 0.05f);
				}
			}

			// ── Channels: the pit chain and the spiral ───────────────

			/// <summary>
			/// A point's reach across a channel stretch, 1 at its bank: across alone for most; a pit chain's to the nearest pit along
			/// it (pits a half width round, three half widths apart, rock bridges between); a spiral trough's leaning to its steep side.
			/// </summary>
			private float ChannelReach(in LineHit hit, float half)
			{
				BiomeLandform f = Site.Form;
				if (f.Form == BiomeLandformForm.PitChain)
				{
					float pitch = 3f * half;
					float da = MathF.Abs(Frac(hit.Along / pitch) - 0.5f) * pitch;
					return MathF.Sqrt(da * da + hit.Distance * hit.Distance) / half;
				}
				if (f.Form == BiomeLandformForm.Spiral)
				{
					float lean = Mathf.Clamp(f.Lean, -0.8f, 0.8f);
					return hit.Distance / (half * (hit.Side > 0f ? 1f + lean : 1f - lean));
				}
				if (f.Form == BiomeLandformForm.Rille && f.Crown > 0f)
				{
					// A beaded rille's necks (decision 76): the width drawn in to the neck's share every three widths.
					float neck = Mathf.Clamp(f.Crown, 0.2f, 1f);
					float bead = 0.5f + 0.5f * MathF.Cos(TwoPi * hit.Along / (6f * half));
					return hit.Distance / (half * (neck + (1f - neck) * bead));
				}
				return hit.Distance / half;
			}

			/// <summary>
			/// A rille's vent hollow at its line's head (decision 76): a bowl 1.1 widths across, <see cref="BiomeLandform.CrownDepth"/>
			/// times the trench's depth there; the cut in metres, <paramref name="share"/> its mask. None when CrownDepth is 0.
			/// </summary>
			private float RilleHead(float x, float z, out float share)
			{
				share = 0f;
				if (Site.Form.CrownDepth <= 0f || Site.Segments == null || Site.Segments.Count == 0)
				{
					return 0f;
				}
				BiomeLandformSegment first = Site.Segments[0];
				float r = 2.2f * Mathf.Max(0.05f, first.Half);
				float d = Vector2.Distance(new Vector2(x, z), first.A) / r;
				if (d >= 1f)
				{
					return 0f;
				}
				share = 1f - S01((d - 0.3f) / 0.7f);
				return Mathf.Clamp(Site.Form.CrownDepth, 0f, 3f) * first.Depth * share;
			}

			// ── Waves: cockpits and labyrinth ────────────────────────

			/// <summary>A cockpit field's cones at a point (the closed hollows between them flat), metres.</summary>
			private float CockpitHeight(float x, float z, float lambda)
			{
				Local(x, z, out float u, out float v);
				float row = lambda * 0.866f;
				float tanFlank = MathF.Tan(Mathf.Clamp(Site.Form.Flank > 0f ? Site.Form.Flank : 45f, 10f, 80f) * Mathf.Deg2Rad);
				float rc = 0.62f * lambda;
				int cj = Mathf.FloorToInt(v / row);
				float best = 0f;
				for (int j = cj - 1; j <= cj + 1; j++)
				{
					float offset = (j & 1) * 0.5f * lambda;
					int ci = Mathf.FloorToInt((u - offset) / lambda);
					for (int i = ci - 1; i <= ci + 1; i++)
					{
						uint h = Hash(Site.Seed, (uint)i, (uint)j, 0xC0Cu);
						float cu = (i + 0.5f) * lambda + offset + 0.4f * lambda * (Unit(Hash(h, 1u)) - 0.5f);
						float cv = (j + 0.5f) * row + 0.35f * lambda * (Unit(Hash(h, 2u)) - 0.5f);
						float d = MathF.Sqrt((u - cu) * (u - cu) + (v - cv) * (v - cv));
						if (d >= rc)
						{
							continue;
						}
						float hc = Mathf.Min(Site.Height * (0.7f + 0.6f * Unit(Hash(h, 3u))), rc * tanFlank);
						best = Mathf.Max(best, hc * MathF.Pow(1f - d / rc, 1.15f));
					}
				}
				return best;
			}

			/// <summary>A labyrinth's valley at a point, 0 on a mesa's top … 1 on a valley's flat floor.</summary>
			private float LabyrinthValley(float x, float z, float lambda)
			{
				float warp = 1.7f * lambda;
				float wx = x + 0.35f * lambda * (Noise(x / warp, z / warp, Site.Seed ^ 0x1Bu) - 0.5f);
				float wz = z + 0.35f * lambda * (Noise(x / warp + 31.7f, z / warp - 12.3f, Site.Seed ^ 0x1Cu) - 0.5f);
				Polygon(wx, wz, lambda, out float edge);
				return 1f - S01((edge - 0.1f * lambda) / (0.06f * lambda));
			}

			/// <summary>A convection-cell field's distances at a point: to its cell's edge (F2 − F1)/2 and to a triple junction (F3 − F1)/2, metres.</summary>
			private void CellDistances(float x, float z, float lambda, out float edge, out float junction)
			{
				float warp = 1.9f * lambda;
				float wx = x + 0.3f * lambda * (Noise(x / warp, z / warp, Site.Seed ^ 0x2Bu) - 0.5f);
				float wz = z + 0.3f * lambda * (Noise(x / warp - 17.1f, z / warp + 5.9f, Site.Seed ^ 0x2Cu) - 0.5f);
				int cx = Mathf.FloorToInt(wx / lambda), cz = Mathf.FloorToInt(wz / lambda);
				float d1 = float.MaxValue, d2 = float.MaxValue, d3 = float.MaxValue;
				for (int j = cz - 1; j <= cz + 1; j++)
				{
					for (int i = cx - 1; i <= cx + 1; i++)
					{
						uint h = Hash(Site.Seed, (uint)i, (uint)j, 0xCE1u);
						float px = (i + 0.15f + 0.7f * Unit(h)) * lambda, pz = (j + 0.15f + 0.7f * Unit(Hash(h, 1u))) * lambda;
						float d = MathF.Sqrt((wx - px) * (wx - px) + (wz - pz) * (wz - pz));
						if (d < d1)
						{
							d3 = d2;
							d2 = d1;
							d1 = d;
						}
						else if (d < d2)
						{
							d3 = d2;
							d2 = d;
						}
						else if (d < d3)
						{
							d3 = d;
						}
					}
				}
				edge = 0.5f * (d2 - d1);
				junction = 0.5f * (d3 - d1);
			}

			private float CellTroughHalf => Mathf.Max(1f, 0.5f * Site.Form.Moat);

			/// <summary>A convection-cell field's height at a point: the cell's middle swelled, its edges cut into a trough, metres.</summary>
			private float CellHeight(float x, float z, float lambda)
			{
				CellDistances(x, z, lambda, out float edge, out _);
				float swell = Site.Height * S01(edge / (0.35f * lambda));
				return swell - Site.Form.MoatDepth * Bump(edge / CellTroughHalf);
			}

			/// <summary>The parts of a cockpit field, a labyrinth and a cell field, before their biome's weight.</summary>
			private float FieldPartMask(BiomeLandformPart part, float x, float z, float lambda)
			{
				if (Site.Form.Form == BiomeLandformForm.Weave)
				{
					WeaveHeight(x, z, lambda, out float crest, out float trough);
					switch (part)
					{
						case BiomeLandformPart.Crest:
						case BiomeLandformPart.Top: return crest;
						case BiomeLandformPart.Channel:
						case BiomeLandformPart.Floor:
						case BiomeLandformPart.Moat: return trough;
						case BiomeLandformPart.Flank: return (1f - trough) * (1f - crest);
						default: return 1f;
					}
				}
				if (Site.Form.Form == BiomeLandformForm.Cells)
				{
					CellDistances(x, z, lambda, out float edge, out float junction);
					float half = CellTroughHalf;
					switch (part)
					{
						case BiomeLandformPart.Channel:
						case BiomeLandformPart.Moat:
						case BiomeLandformPart.Floor: return Bump(edge / half);
						case BiomeLandformPart.Top: return S01((edge - half) / half);
						case BiomeLandformPart.Junction: return 1f - S01((junction - 1.5f * half) / half);
						case BiomeLandformPart.Flank: return S01((edge - 0.5f * half) / (0.5f * half)) * (1f - S01((edge - 1.5f * half) / (0.5f * half)));
						default: return 1f;
					}
				}
				if (Site.Form.Form == BiomeLandformForm.Cockpits)
				{
					float h = CockpitHeight(x, z, lambda);
					float high = S01((h / Mathf.Max(0.01f, Site.Height) - 0.75f) / 0.15f);
					switch (part)
					{
						case BiomeLandformPart.Floor: return 1f - S01(h / 1.5f);
						case BiomeLandformPart.Top:
						case BiomeLandformPart.Crest: return high;
						case BiomeLandformPart.Flank: return S01(h / 1.5f) * (1f - high);
						default: return 1f;
					}
				}
				float valley = LabyrinthValley(x, z, lambda);
				switch (part)
				{
					case BiomeLandformPart.Floor:
					case BiomeLandformPart.Channel: return S01((valley - 0.6f) / 0.3f);
					case BiomeLandformPart.Top: return 1f - S01(valley / 0.2f);
					case BiomeLandformPart.Flank: return S01(valley / 0.2f) * (1f - S01((valley - 0.6f) / 0.3f));
					default: return 1f;
				}
			}

			// ── Crater: the summit caldera ───────────────────────────

			private float CalderaDelta(float dx, float dz, float r)
			{
				BiomeLandform f = Site.Form;
				if (r >= 1.8f)
				{
					return 0f;
				}
				float top = Mathf.Clamp(f.Top, 0.05f, 0.9f);
				float wall = 1f - S01((r - top) / (1f - top));
				// Step-fault benches down its walls (2026-10-25, phase I2g-d: a shield caldera's).
				float delta = r < 1f ? -Site.Height * (f.Steps > 0 ? Stepped(wall, f.Steps + 1) : wall) : 0f;
				float rimHeight = Mathf.Max(0f, f.Rim) * 2f * radius;
				if (rimHeight > 0f)
				{
					float theta = MathF.Atan2(dz, dx);
					int peaks = Mathf.Max(1, f.Count);
					float crest = 0.5f + 0.5f * MathF.Cos(peaks * theta + TwoPi * Unit(Hash(Site.Seed, 0xCA1u)));
					float notch = AngleBetween(theta, TwoPi * Unit(Hash(Site.Seed, 0xCA2u)));
					float outlet = MathF.Exp(-(notch / 0.15f) * (notch / 0.15f));
					delta += rimHeight * (0.55f + 0.45f * crest * crest) * (1f - 0.9f * outlet)
						* MathF.Exp(-((r - 1f) / 0.12f) * ((r - 1f) / 0.12f)) * (1f - S01((r - 1.45f) / 0.3f));
				}
				return delta;
			}

			// ── Crater: the multi-ring basin (decision 55) ───────────

			/// <summary>Wrinkle-ridge arcs: x radius share, y start heading, z span, w the sinuous phase.</summary>
			private Vector4[] basinArcs;
			/// <summary>Radial wrinkle ridges' headings.</summary>
			private float[] basinRadials;
			/// <summary>Radial sculpture grooves' headings, round the ejecta.</summary>
			private float[] basinGrooves;
			/// <summary>Secondary crater chains: x heading, y craterlets, z craterlet radius, metres, w its seed.</summary>
			private Vector4[] basinChains;
			/// <summary>Mare pits: x, z world, radius metres.</summary>
			private Vector3[] basinPits;
			private float basinScarp, basinScarpRun, basinArch, basinKnob;

			private void PrepareBasin()
			{
				uint s = Site.Seed;
				float big = radius;
				int arcs = Site.Form.Count > 0 ? Mathf.Clamp(Site.Form.Count, 1, 8) : 3 + (int)(Hash(s, 0xA0u) % 4u);
				basinArcs = new Vector4[arcs];
				for (int k = 0; k < arcs; k++)
				{
					basinArcs[k] = new Vector4(0.15f + 0.3f * Unit(Hash(s, 0xA1u, (uint)k)), Unit(Hash(s, 0xA2u, (uint)k)) * TwoPi,
						2.1f + 2.1f * Unit(Hash(s, 0xA3u, (uint)k)), Unit(Hash(s, 0xA4u, (uint)k)) * TwoPi);
				}
				int radials = 2 + (int)(Hash(s, 0xA5u) % 3u);
				basinRadials = new float[radials];
				for (int k = 0; k < radials; k++)
				{
					basinRadials[k] = (k + 0.6f * Unit(Hash(s, 0xA6u, (uint)k))) * TwoPi / radials;
				}
				int grooves = Mathf.Clamp((int)(TwoPi * 1.2f * big / 300f), 12, 64);
				basinGrooves = new float[grooves];
				for (int k = 0; k < grooves; k++)
				{
					basinGrooves[k] = (k + 0.3f * (Unit(Hash(s, 0xA7u, (uint)k)) - 0.5f)) * TwoPi / grooves;
				}
				int chains = 3 + (int)(Hash(s, 0xA8u) % 4u);
				basinChains = new Vector4[chains];
				for (int k = 0; k < chains; k++)
				{
					uint h = Hash(s, 0xA9u, (uint)k);
					basinChains[k] = new Vector4((k + 0.7f * Unit(Hash(h, 1u))) * TwoPi / chains, 8 + (int)(Hash(h, 2u) % 13u),
						Mathf.Clamp(7.5f + 22.5f * Unit(Hash(h, 3u)), 1f, 0.08f * big), Hash(h, 4u) & 0xFFFFu);
				}
				int pits = (int)(Hash(s, 0xAAu) % 3u);
				basinPits = new Vector3[pits];
				for (int k = 0; k < pits; k++)
				{
					float a = Unit(Hash(s, 0xABu, (uint)k)) * TwoPi;
					float d = big * (0.1f + 0.25f * Unit(Hash(s, 0xACu, (uint)k)));
					basinPits[k] = new Vector3(Site.Centre.x + MathF.Cos(a) * d, Site.Centre.y + MathF.Sin(a) * d,
						Mathf.Clamp(15f + 25f * Unit(Hash(s, 0xADu, (uint)k)), 1f, 0.03f * big));
				}
				basinScarp = Mathf.Clamp(Mathf.Max(0f, Site.Form.Rim) * 2f * big, Mathf.Min(30f, 0.1f * big), 150f);
				basinScarpRun = Mathf.Clamp(basinScarp / (MathF.Tan(38f * Mathf.Deg2Rad) * big), 0.01f, 0.08f);
				basinArch = Mathf.Clamp(0.006f * big, 3f, 15f);
				basinKnob = Mathf.Clamp(0.004f * big, 2f, 20f);
			}

			/// <summary>The mare pits (centre, radius): where a lava tube's skylight opens on the basin's floor.</summary>
			public System.Collections.Generic.IReadOnlyList<Vector3> BasinPits => basinPits ?? Array.Empty<Vector3>();

			/// <summary>A massif ring's share at a point: its band across, broken into massifs with gaps between.</summary>
			private float BasinRing(float x, float z, float r, float theta, float at, float width, float gap, uint salt)
			{
				float across = Bump((r - at) / width);
				if (across <= 0f)
				{
					return 0f;
				}
				float k = at / (2.5f * width);
				float n = Noise(MathF.Cos(theta) * k + 11f, MathF.Sin(theta) * k - 7f, Site.Seed ^ salt);
				return across * S01((n - gap) / 0.12f);
			}

			private float BasinRings(float x, float z, float r, float theta, out float share)
			{
				float outer = BasinRing(x, z, r, theta, 0.71f, 0.05f, 0.38f, 0xB01u);
				float peak = BasinRing(x, z, r, theta, 0.5f, 0.04f, 0.43f, 0xB02u);
				share = Mathf.Max(outer, peak);
				if (share <= 0f)
				{
					return 0f;
				}
				float knob = 0.75f + 0.5f * Noise(x / (0.035f * radius), z / (0.035f * radius), Site.Seed ^ 0xB03u);
				return (outer * 0.025f + peak * 0.03f) * radius * knob;
			}

			private float BasinFill(float r) => 1f - S01((r - 0.4f) / 0.05f);

			/// <summary>The wrinkle ridges on the mare at a point, metres; <paramref name="crest"/> how much of it is a ridge's narrow crest.</summary>
			private float BasinWrinkles(float r, float theta, out float crest)
			{
				crest = 0f;
				float best = 0f;
				float archHalf = 0.025f * radius;
				foreach (Vector4 arc in basinArcs)
				{
					float t = (theta - arc.y) % TwoPi;
					t = t < 0f ? t + TwoPi : t;
					if (t >= arc.z)
					{
						continue;
					}
					float along = S01(t / 0.3f) * S01((arc.z - t) / 0.3f);
					float d = MathF.Abs(r - arc.x - 0.008f * MathF.Sin(7f * theta + arc.w)) * radius;
					best = Mathf.Max(best, along * (basinArch * Bump(d / archHalf) + 3f * Bump(d / 2.5f)));
					crest = Mathf.Max(crest, along * Bump(d / 2.5f));
				}
				float inside = S01((r - 0.1f) / 0.05f) * (1f - S01((r - 0.42f) / 0.03f));
				if (inside > 0f)
				{
					foreach (float heading in basinRadials)
					{
						float d = AngleBetween(theta + 0.05f * MathF.Sin(r * 40f), heading) * r * radius;
						best = Mathf.Max(best, inside * (basinArch * Bump(d / archHalf) + 3f * Bump(d / 2.5f)));
						crest = Mathf.Max(crest, inside * Bump(d / 2.5f));
					}
				}
				return best;
			}

			/// <summary>The radial sculpture grooves across the ejecta at a point, metres deep; <paramref name="share"/> 0 … 1 of a groove.</summary>
			private float BasinGrooves(float r, float theta, out float share)
			{
				share = 0f;
				float band = S01((r - 1.03f) / 0.08f) * (1f - S01((r - 1.45f) / 0.15f));
				if (band <= 0f)
				{
					return 0f;
				}
				int n = basinGrooves.Length;
				float spacing = TwoPi / n;
				float t = theta < 0f ? theta + TwoPi : theta;
				int k0 = Mathf.RoundToInt(t / spacing);
				float cut = 0f;
				for (int k = k0 - 1; k <= k0 + 1; k++)
				{
					int i = ((k % n) + n) % n;
					uint h = Hash(Site.Seed, 0xB10u, (uint)i);
					float half = 10f + 20f * Unit(Hash(h, 1u));
					float d = AngleBetween(theta, basinGrooves[i]) * r * radius;
					float g = Bump(d / half);
					share = Mathf.Max(share, g * band);
					cut = Mathf.Max(cut, (2f + 6f * Unit(Hash(h, 2u))) * g * band);
				}
				return cut;
			}

			/// <summary>The secondary crater chains out in the ejecta at a point (lines of overlapping craterlets radial to the basin), metres.</summary>
			private float BasinChains(float x, float z, float r, float theta)
			{
				float delta = 0f;
				foreach (Vector4 chain in basinChains)
				{
					float lateral = AngleBetween(theta, chain.x) * r * radius;
					if (lateral > 2f * chain.z)
					{
						continue;
					}
					int count = Mathf.Max(2, (int)chain.y);
					float step = 0.65f / (count - 1);
					int i0 = Mathf.RoundToInt((r - 1.1f) / step);
					for (int i = Mathf.Max(0, i0 - 1); i <= Mathf.Min(count - 1, i0 + 1); i++)
					{
						uint h = Hash((uint)chain.w, (uint)i, 0xB20u);
						float rr = chain.z * (0.6f + 0.6f * Unit(h));
						float ri = (1.1f + step * i) * radius;
						float side = (Unit(Hash(h, 1u)) - 0.5f) * chain.z;
						float cx = Site.Centre.x + MathF.Cos(chain.x) * ri - MathF.Sin(chain.x) * side;
						float cz = Site.Centre.y + MathF.Sin(chain.x) * ri + MathF.Cos(chain.x) * side;
						float ds = MathF.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) / rr;
						if (ds < 1.6f)
						{
							delta += rr * 2f * (-0.18f * Bump(ds) + 0.03f * MathF.Exp(-((ds - 1f) / 0.2f) * ((ds - 1f) / 0.2f)) * (1f - S01((ds - 1.3f) / 0.3f)));
						}
					}
				}
				return delta;
			}

			private float Basin(float x, float z, float current)
			{
				float dx = x - Site.Centre.x, dz = z - Site.Centre.y;
				float r = MathF.Sqrt(dx * dx + dz * dz) / radius;
				if (r >= 1.95f)
				{
					return current;
				}
				float theta = MathF.Atan2(dz, dx);
				float delta = basinScarp * S01((r - (1f - basinScarpRun)) / basinScarpRun) * (1f - S01((r - 1f) / 0.35f));
				float knobby = S01((r - 0.74f) / 0.03f) * (1f - S01((r - 0.96f) / 0.03f));
				if (knobby > 0f)
				{
					delta += basinKnob * knobby * S01((Noise(x / (0.02f * radius), z / (0.02f * radius), Site.Seed ^ 0xB04u) - 0.55f) / 0.15f);
				}
				delta += BasinRings(x, z, r, theta, out _);
				delta -= Site.Height * (1f - S01((r - 0.45f) / 0.1f));
				float target = current + delta;
				float fill = BasinFill(r);
				if (fill > 0f && !float.IsNaN(Site.Level))
				{
					float raised = Mathf.Max(0f, Site.Level - target) * fill;
					target += raised;
					// The flow front's low lip where the mare meets the ring ground, and the wrinkle ridges on the mare.
					target += 3.5f * Bump((r - 0.425f) / 0.02f) * S01(raised / 1f);
					target += fill * BasinWrinkles(r, theta, out _);
				}
				foreach (Vector3 pit in basinPits)
				{
					float ds = MathF.Sqrt((x - pit.x) * (x - pit.x) + (z - pit.y) * (z - pit.y)) / pit.z;
					if (ds < 1f)
					{
						target -= Mathf.Clamp(pit.z * 0.8f, 6f, 40f) * (1f - S01((ds - 0.8f) / 0.2f));
					}
				}
				if (r > 1f)
				{
					target -= BasinGrooves(r, theta, out _);
					target += BasinChains(x, z, r, theta);
				}
				return target;
			}

			private float BasinMask(BiomeLandformPart part, float x, float z, float r)
			{
				float theta = MathF.Atan2(z - Site.Centre.y, x - Site.Centre.x);
				switch (part)
				{
					case BiomeLandformPart.Floor: return BasinFill(r);
					case BiomeLandformPart.Crest:
					{
						BasinWrinkles(r, theta, out float crest);
						return crest * BasinFill(r);
					}
					case BiomeLandformPart.Melt: return S01((r - 0.42f) / 0.02f) * (1f - S01((r - 0.49f) / 0.02f));
					case BiomeLandformPart.Rim:
					case BiomeLandformPart.Top:
					{
						BasinRings(x, z, r, theta, out float share);
						return share;
					}
					case BiomeLandformPart.Knobby: return S01((r - 0.74f) / 0.03f) * (1f - S01((r - 0.96f) / 0.03f));
					case BiomeLandformPart.Flank: return Bump((r - (1f - 0.5f * basinScarpRun)) / (0.75f * basinScarpRun + 0.01f));
					case BiomeLandformPart.Ejecta: return S01((r - 1f) / 0.04f) * (1f - S01((r - 1.6f) / 0.1f));
					case BiomeLandformPart.Rays:
					case BiomeLandformPart.Channel:
					{
						BasinGrooves(r, theta, out float share);
						return share;
					}
					case BiomeLandformPart.Whole: return 1f - S01((r - 1.6f) / 0.1f);
					default: return 0f;
				}
			}

			// ── Mesa: the wave-cut bench and the brough's neck (decision 74) ─

			/// <summary>How much of the bench's cut stands at a height: the ground within 1.5 m of the sea fully, none past 3 m.</summary>
			private float BenchReach(float height)
			{
				float sea = context.SeaLevel;
				return float.IsNegativeInfinity(sea) || float.IsNaN(sea) ? 0f : 1f - S01((MathF.Abs(height - sea) - 1.5f) / 1.5f);
			}

			private float Bench(float x, float z, float current, float original, float slope)
			{
				float w = FieldWeight(x, z, original, slope) * BenchReach(current);
				if (w <= 0f)
				{
					return current;
				}
				float tread = Mathf.Max(0.1f, MathF.Abs(Site.Height));
				// The flags' treads: whole steps of the tread height, held between half a metre under and over the sea.
				float step = MathF.Floor((current - context.SeaLevel) / tread) * tread * 0.35f;
				float target = context.SeaLevel + Mathf.Clamp(step, -0.5f, 0.5f);
				return current + (target - current) * w;
			}

			private float BenchMask(BiomeLandformPart part, float x, float z, float ground, float slope)
			{
				Vector4 b = Site.Bounds;
				if (x < b.x || x > b.z || z < b.y || z > b.w)
				{
					return 0f;
				}
				float w = context.WeightOf(Site.BiomeID, x, z);
				return part == BiomeLandformPart.Whole ? w : w * (1f - S01((MathF.Abs(ground - context.SeaLevel) - 0.7f) / 0.6f));
			}

			private float Neck(float x, float z, float current)
			{
				BiomeLandform f = Site.Form;
				if (Site.Segments == null || Site.Segments.Count == 0 || float.IsNegativeInfinity(context.SeaLevel) || float.IsNaN(context.SeaLevel))
				{
					return current;
				}
				float raised = current;
				foreach (BiomeLandformSegment seg in Site.Segments)
				{
					Vector2 ab = seg.B - seg.A, ap = new Vector2(x, z) - seg.A;
					float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					float d = (ap - ab * t).magnitude;
					float half = Mathf.Max(0.5f, seg.Half);
					float w = 1f - S01((d - 0.6f * half) / (0.9f * half));
					if (w > 0f)
					{
						float crest = context.SeaLevel - seg.Depth;
						raised = Mathf.Max(raised, current + (crest - current) * w);
					}
				}
				return raised;
			}

			// ── Crater: ageing classes and the rampart crater (decision 72) ─

			/// <summary>A crater's floor share: an old ageing crater's lies flat across six tenths of it (its lag floor).</summary>
			private float CraterTop()
			{
				BiomeLandform f = Site.Form;
				float top = Mathf.Clamp(f.Top, 0f, 0.9f);
				return f.Ages && CraterAge(Site.Seed) == 3 ? Mathf.Max(top, 0.6f) : top;
			}

			/// <summary>Where a rampart crater's lobate ejecta ends at a heading, radii: its reach, lobed by ±12 %.</summary>
			private float RampartEdge(float dx, float dz, float r, float reach)
			{
				float c = r > 1e-4f ? dx / (r * radius) : 1f, sn = r > 1e-4f ? dz / (r * radius) : 0f;
				return reach * (1f + 0.24f * (Noise(c * 2.2f + 3f, sn * 2.2f - 1f, Site.Seed ^ 0x8A1u) - 0.5f));
			}

			/// <summary>The rampart crater's lobes at a point, metres: a thin sheet out to the distal ridge, an inner lobe on three in ten.</summary>
			private float RampartLobes(float dx, float dz, float r)
			{
				BiomeLandform f = Site.Form;
				float reach = RampartReach(f);
				float ridge = f.CrownDepth > 0f ? f.CrownDepth : 4f;
				float edge = RampartEdge(dx, dz, r, reach);
				float delta = 0.35f * ridge * S01((r - 0.95f) / 0.1f) * (1f - S01((r - edge) / 0.08f));
				delta += ridge * Bump((r - edge) / 0.09f);
				if (Unit(Hash(Site.Seed, 0x8A2u)) < 0.3f)
				{
					float inner = 1f + 0.6f * (edge - 1f);
					delta += 0.5f * ridge * Bump((r - inner) / 0.07f);
				}
				return delta;
			}

			private float RampartMask(BiomeLandformPart part, float dx, float dz, float r)
			{
				float edge = RampartEdge(dx, dz, r, RampartReach(Site.Form));
				switch (part)
				{
					case BiomeLandformPart.Ejecta: return S01((r - 0.95f) / 0.1f) * (1f - S01((r - edge - 0.05f) / 0.08f));
					case BiomeLandformPart.Crest: return Bump((r - edge) / 0.09f);
					default: return 0f;
				}
			}

			// ── Crater: the ice ring basin (decision 71) ─────────────

			/// <summary>Each graben ring: x its radius, metres; y its half width; z its depth; w the share of its arc broken away.</summary>
			private Vector4[] graben;

			private void PrepareGraben()
			{
				uint s = Site.Seed;
				BiomeLandform f = Site.Form;
				int n = f.Count > 0 ? Mathf.Clamp(f.Count, 1, 12) : 5 + (int)(Hash(s, 0x6A0u) % 6u);
				// From three tenths of the radius out to it, each ring wider than the last by one ratio (×1.15 … ×1.35 for 10 … 5 rings).
				float ratio = n > 1 ? MathF.Pow(1f / 0.3f, 1f / (n - 1)) : 1f;
				Vector2 width = f.Width.x > 0f || f.Width.y > 0f ? f.Width : new Vector2(12f, 40f);
				Vector2 depth = f.Height.x != 0f || f.Height.y != 0f ? f.Height : new Vector2(3f, 10f);
				graben = new Vector4[n];
				for (int k = 0; k < n; k++)
				{
					float at = (n > 1 ? 0.3f * MathF.Pow(ratio, k) : 1f) * (1f + 0.03f * (2f * Unit(Hash(s, 0x6A1u, (uint)k)) - 1f));
					float half = 0.5f * Pick(width, Unit(Hash(s, 0x6A2u, (uint)k)));
					// The outermost ring stays inside the site's box (twice its radius) with room for its scarps.
					float metres = Mathf.Min(at * radius, 1.8f * radius - 1.3f * half);
					graben[k] = new Vector4(metres, Mathf.Max(0.5f, half), Mathf.Abs(Pick(depth, Unit(Hash(s, 0x6A3u, (uint)k)))),
						0.3f + 0.12f * Unit(Hash(s, 0x6A4u, (uint)k)));
				}
			}

			/// <summary>How much of a graben ring stands at a heading: 1 along its arcs, 0 in the breaks between them.</summary>
			private float GrabenArc(in Vector4 ring, int k, float dx, float dz, float distance)
			{
				float scale = Mathf.Max(60f, 6f * ring.y);
				float inv = ring.x / Mathf.Max(1f, distance) / scale;
				return S01((Noise(dx * inv + 17f * k, dz * inv - 5f * k, Site.Seed ^ (0x6B0u + (uint)k)) - ring.w) / 0.08f);
			}

			/// <summary>The ring basin's ground at a point: graben troughs cut out, the palimpsest drawn towards its level.</summary>
			private float Graben(float dx, float dz, float r, float current)
			{
				if (r >= 1.95f)
				{
					return current;
				}
				float distance = MathF.Sqrt(dx * dx + dz * dz);
				float target = current;
				if (!float.IsNaN(Site.Level))
				{
					target += (Site.Level - current) * 0.6f * (1f - S01((r - 0.14f) / 0.08f));
				}
				float cut = 0f;
				for (int k = 0; k < graben.Length; k++)
				{
					Vector4 ring = graben[k];
					float d = MathF.Abs(distance - ring.x);
					if (d >= ring.y)
					{
						continue;
					}
					// A flat floor between two inward-facing scarps.
					float across = 1f - S01((d - 0.55f * ring.y) / (0.45f * ring.y));
					cut = Mathf.Max(cut, ring.z * across * GrabenArc(ring, k, dx, dz, distance));
				}
				return target - cut;
			}

			private float GrabenMask(BiomeLandformPart part, float dx, float dz, float r)
			{
				switch (part)
				{
					case BiomeLandformPart.Floor:
					case BiomeLandformPart.Top:
						return 1f - S01((r - 0.16f) / 0.06f);
					case BiomeLandformPart.Channel:
					case BiomeLandformPart.Moat:
					case BiomeLandformPart.Flank:
					case BiomeLandformPart.Rim:
					{
						float distance = MathF.Sqrt(dx * dx + dz * dz);
						float best = 0f;
						for (int k = 0; k < graben.Length; k++)
						{
							Vector4 ring = graben[k];
							float d = MathF.Abs(distance - ring.x);
							if (d >= ring.y * 1.3f)
							{
								continue;
							}
							float share = part == BiomeLandformPart.Channel || part == BiomeLandformPart.Moat
								? 1f - S01((d - 0.55f * ring.y) / (0.45f * ring.y))
								: Bump((d - 0.78f * ring.y) / (0.3f * ring.y));
							best = Mathf.Max(best, share * GrabenArc(ring, k, dx, dz, distance));
						}
						return best;
					}
					case BiomeLandformPart.Whole: return 1f - S01((r - 1.05f) / 0.1f);
					default: return 0f;
				}
			}
		}
	}
}
