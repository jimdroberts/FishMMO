using System;
using System.Collections.Generic;
using System.Globalization;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The colour and clarity of a biome's fresh water (<see cref="BiomeTemplate.WaterLook"/>, 2026-10-16, phase I2e): what its
	/// lakes, pools, rivers, streams and moats look like, in the inland water's own terms (FishMMO/Water/Inland Water: the
	/// colours of shallow and deep water, what a metre of it absorbs, the silt moving water carries and the colour of its
	/// foam). A peat bog's water is stained black-brown by the peat: it takes the blue and the green out of the light within a
	/// metre and shows its bed amber in the shallows; a glacier's is milky with rock flour; a limestone spring's clear.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Colours are as the material holds them (gamma, as a colour picker gives them); the absorption is per metre, red, green
	/// and blue, as the material's Absorption per metre. Until 2026-10-16 every body of inland water in every biome took the
	/// one material's values; every biome's look is authored from exactly those (<see cref="BiomeWaterLooks.Today"/>), so
	/// nothing changes until a biome's round gives it its own.
	/// </para>
	/// <para>
	/// Which biome's look a body of water takes, and how two bodies that meet blend, is <see cref="BiomeWaterLooks"/>'s.
	/// The sea's colour is the ocean's own and is not read from here.
	/// </para>
	/// </remarks>
	[Serializable]
	public struct BiomeWaterLook
	{
		[Tooltip("The colour of shallow water over its bed (the inland water's Shallow colour).")]
		public Color Shallow;
		[Tooltip("The colour of deep water, where no bed shows through (Deep colour).")]
		public Color Deep;
		[Tooltip("How much of the light's red, green and blue a metre of the water absorbs (Absorption per metre): higher is darker and less clear. Peat-stained water takes blue and green far faster than red.")]
		public Vector3 Absorption;
		[Tooltip("How much silt the water carries where it runs, 0 … 1 (Silt in moving water): rivers and streams, more the faster they run.")]
		[Range(0f, 1f)] public float Turbidity;
		[Tooltip("The colour of that silt (Silt colour).")]
		public Color Silt;
		[Tooltip("The colour of the water's foam where it breaks (Foam colour).")]
		public Color Foam;

		public BiomeWaterLook(Color shallow, Color deep, Vector3 absorption, float turbidity, Color silt, Color foam)
		{
			Shallow = shallow;
			Deep = deep;
			Absorption = absorption;
			Turbidity = turbidity;
			Silt = silt;
			Foam = foam;
		}

		/// <summary>
		/// False for a look nobody filled in (every value zero: a template made before the field existed reads its default,
		/// which is <see cref="BiomeWaterLooks.Today"/>, so this is a look zeroed by hand or by code).
		/// </summary>
		public bool Defined => Shallow.a > 0f || Deep.a > 0f || Silt.a > 0f || Foam.a > 0f || Absorption != Vector3.zero;

		/// <summary>True when every value is within <paramref name="tolerance"/> of <paramref name="other"/>'s (alpha ignored: the water reads none).</summary>
		public bool Approximately(BiomeWaterLook other, float tolerance = 1e-4f)
		{
			return Near(Shallow, other.Shallow, tolerance) && Near(Deep, other.Deep, tolerance) && Near(Silt, other.Silt, tolerance)
				&& Near(Foam, other.Foam, tolerance) && Mathf.Abs(Turbidity - other.Turbidity) <= tolerance
				&& Mathf.Abs(Absorption.x - other.Absorption.x) <= tolerance && Mathf.Abs(Absorption.y - other.Absorption.y) <= tolerance
				&& Mathf.Abs(Absorption.z - other.Absorption.z) <= tolerance;
		}

		private static bool Near(Color a, Color b, float tolerance)
		{
			return Mathf.Abs(a.r - b.r) <= tolerance && Mathf.Abs(a.g - b.g) <= tolerance && Mathf.Abs(a.b - b.b) <= tolerance;
		}

		/// <summary>The look <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>, value by value.</summary>
		public static BiomeWaterLook Lerp(BiomeWaterLook a, BiomeWaterLook b, float t)
		{
			t = Mathf.Clamp01(t);
			return new BiomeWaterLook(Color.Lerp(a.Shallow, b.Shallow, t), Color.Lerp(a.Deep, b.Deep, t), Vector3.Lerp(a.Absorption, b.Absorption, t),
				Mathf.Lerp(a.Turbidity, b.Turbidity, t), Color.Lerp(a.Silt, b.Silt, t), Color.Lerp(a.Foam, b.Foam, t));
		}

		/// <summary>The look as the authoring marks it: every value round-trip exact, in a fixed order.</summary>
		public string Signature()
		{
			var text = new System.Text.StringBuilder(160);
			void Add(float value) => text.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
			void AddColor(Color c)
			{
				Add(c.r);
				Add(c.g);
				Add(c.b);
				Add(c.a);
			}
			AddColor(Shallow);
			AddColor(Deep);
			Add(Absorption.x);
			Add(Absorption.y);
			Add(Absorption.z);
			Add(Turbidity);
			AddColor(Silt);
			AddColor(Foam);
			return text.ToString();
		}

		/// <summary>The look as the authoring reports it.</summary>
		public override string ToString()
		{
			string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
			string C(Color c) => $"({F(c.r)}, {F(c.g)}, {F(c.b)})";
			return $"shallow {C(Shallow)}, deep {C(Deep)}, absorbs ({F(Absorption.x)}, {F(Absorption.y)}, {F(Absorption.z)})/m, silt {F(Turbidity)} {C(Silt)}, foam {C(Foam)}";
		}
	}

	/// <summary>
	/// Which biome's water look a body of inland water takes, and how a river's look runs into the water it leaves and joins
	/// (2026-10-16, phase I2e). Pure: the renderer (InlandWaterRenderer) samples the scene's biome map through these and
	/// hands the shader the looks.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One biome per body, the biome of its shores.</b> Water takes its colour from what it drains and what stands round it
	/// (peat stains a bog's pools and the burns that run out of it), not from the ground under it, which a scene paints as a
	/// water biome (the Lake, River or Estuary template, <see cref="BiomeWaterRole"/>). So a lake's biome is the commonest
	/// land biome just outside its shore, all round it; a river's, the commonest along both its banks over its whole length,
	/// each stretch counted by its length. Water-body biomes are skipped; only where a body has no land biome round it at all
	/// (a lake whose shore is all river) does it take the commonest water biome there. A tie goes to the smaller biome ID, so
	/// the choice is the same on every machine. A moat is a lake (MoatShaper adds it as one) and takes the biome its ring
	/// lies in, the site's.
	/// </para>
	/// <para>
	/// <b>Why not per vertex by the biome under each point.</b> A river is one water from its source to its mouth: a peat
	/// burn leaving the bog is still peat-brown in the meadow below, and colouring each point by the ground there would
	/// change a river's colour at every biome edge it crosses, as no river does. One look per body is also what the draw-once
	/// stencil can show without a seam inside a body.
	/// </para>
	/// <para>
	/// <b>Where water meets water there is no seam</b> (the inland water's rule since 2026-10-05): a river's look runs, over
	/// its last <see cref="BlendMetres"/>, into the look of the water it joins (the lake it enters, the river it flows into,
	/// that river's look where they meet), and over its first, out of the lake it leaves; so where two meshes meet, they are
	/// the same colour. A river running into the sea fades out into the sea's own colour as before.
	/// </para>
	/// </remarks>
	public static class BiomeWaterLooks
	{
		/// <summary>
		/// The inland water's look until 2026-10-16, which every lake and river in every biome took: InlandWater.mat's values
		/// (FishMMO/Water/Inland Water). Every biome's default and what the authoring writes for each (a test holds it equal to
		/// the material).
		/// </summary>
		public static BiomeWaterLook Today => new BiomeWaterLook(
			new Color(0.20f, 0.42f, 0.40f, 1f),
			new Color(0.03f, 0.12f, 0.16f, 1f),
			new Vector3(0.45f, 0.12f, 0.08f),
			0.35f,
			new Color(0.36f, 0.33f, 0.24f, 1f),
			new Color(0.92f, 0.94f, 0.95f, 1f));

		/// <summary>The least run over which a river's look runs into the water it joins or out of the water it leaves, metres.</summary>
		public const float MinBlendMetres = 40f;
		/// <summary>The same run in the river's widths there, when that is longer.</summary>
		public const float BlendWidths = 8f;

		/// <summary>
		/// How far out past a shore, in cells of the scene's biome map, the shore's biome is read: twice, three quarters of a
		/// cell and a cell and a half out, past the water cells the map paints along the shore.
		/// </summary>
		public static readonly float[] ShoreCells = { 0.75f, 1.5f };

		private static readonly HashSet<BiomeTemplate> warned = new HashSet<BiomeTemplate>();

		/// <summary>
		/// A biome's water look (<see cref="BiomeTemplate.WaterLook"/>); <see cref="Today"/>'s, with one warning a session,
		/// for a template whose look is not filled in. Null for no biome.
		/// </summary>
		public static BiomeWaterLook? Of(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return null;
			}
			BiomeWaterLook look = biome.WaterLook;
			if (look.Defined)
			{
				return look;
			}
			if (warned.Add(biome))
			{
				Log.Warning("Biome templates", $"'{biome.name}' has no Water Look; its lakes and rivers take the inland water's own look. Run Author biome art.");
			}
			return Today;
		}

		/// <summary>True for the roles a scene paints under its water (lakes, methane lakes, rivers, estuaries), whose biomes a body's look never comes from while it has land round it.</summary>
		public static bool IsWaterBody(BiomeWaterRole role)
		{
			return role == BiomeWaterRole.Lake || role == BiomeWaterRole.MethaneLake || role == BiomeWaterRole.River || role == BiomeWaterRole.Estuary;
		}

		/// <summary>
		/// The biome a body of water takes its look from: the commonest land biome on its shore, else under it, else the
		/// commonest water-body biome round it; 0 for none. <paramref name="roleOf"/> gives a biome ID's water role, null for an
		/// ID no template answers to (never chosen). Ties go to the smaller ID.
		/// </summary>
		public static int Choose(IReadOnlyDictionary<int, float> shore, IReadOnlyDictionary<int, float> under, Func<int, BiomeWaterRole?> roleOf)
		{
			if (roleOf == null)
			{
				return 0;
			}
			bool Land(int id) => roleOf(id) is BiomeWaterRole role && !IsWaterBody(role);
			bool Known(int id) => roleOf(id).HasValue;
			int best = Commonest(shore, Land);
			if (best == 0)
			{
				best = Commonest(under, Land);
			}
			if (best == 0)
			{
				var all = new Dictionary<int, float>();
				Merge(all, shore);
				Merge(all, under);
				best = Commonest(all, Known);
			}
			return best;
		}

		private static void Merge(Dictionary<int, float> into, IReadOnlyDictionary<int, float> from)
		{
			if (from == null)
			{
				return;
			}
			foreach (KeyValuePair<int, float> pair in from)
			{
				into.TryGetValue(pair.Key, out float weight);
				into[pair.Key] = weight + pair.Value;
			}
		}

		/// <summary>The ID with the most weight that <paramref name="accept"/> takes (never 0); ties to the smaller ID; 0 for none.</summary>
		public static int Commonest(IReadOnlyDictionary<int, float> weights, Func<int, bool> accept)
		{
			int best = 0;
			float most = 0f;
			if (weights == null)
			{
				return 0;
			}
			foreach (KeyValuePair<int, float> pair in weights)
			{
				if (pair.Key == 0 || !(pair.Value > 0f) || accept != null && !accept(pair.Key))
				{
					continue;
				}
				if (best == 0 || pair.Value > most || pair.Value == most && pair.Key < best)
				{
					best = pair.Key;
					most = pair.Value;
				}
			}
			return best;
		}

		private static void Count(Dictionary<int, float> into, int id, float weight)
		{
			if (into == null || id == 0 || !(weight > 0f))
			{
				return;
			}
			into.TryGetValue(id, out float sum);
			into[id] = sum + weight;
		}

		/// <summary>
		/// What lies round a lake: the biome map read just outside every cell of its mask on its edge, on each side that
		/// has no water (<see cref="ShoreCells"/> map cells out), one count each, into <paramref name="shore"/>; and at those
		/// edge cells themselves into <paramref name="under"/>. <paramref name="idAt"/> is the map's biome ID at world x, z;
		/// <paramref name="mapCell"/> its cell, metres.
		/// </summary>
		public static void LakeShore(SceneHydrology.Lake lake, Func<float, float, int> idAt, float mapCell, Dictionary<int, float> shore, Dictionary<int, float> under)
		{
			if (lake == null || idAt == null || lake.Mask == null || lake.Mask.Length == 0 || lake.MaskWidth <= 0 || lake.MaskHeight <= 0 || lake.MaskCell <= 0f)
			{
				return;
			}
			int w = lake.MaskWidth, h = lake.MaskHeight;
			float cell = lake.MaskCell;
			mapCell = Mathf.Max(1f, mapCell);
			bool Wet(int x, int z)
			{
				if (x < 0 || z < 0 || x >= w || z >= h)
				{
					return false;
				}
				int bit = z * w + x;
				return (bit >> 3) < lake.Mask.Length && (lake.Mask[bit >> 3] & (1 << (bit & 7))) != 0;
			}
			for (int z = 0; z < h; z++)
			{
				for (int x = 0; x < w; x++)
				{
					if (!Wet(x, z))
					{
						continue;
					}
					float cx = lake.MaskOrigin.x + (x + 0.5f) * cell, cz = lake.MaskOrigin.y + (z + 0.5f) * cell;
					bool edge = false;
					for (int side = 0; side < 4; side++)
					{
						int dx = side == 0 ? 1 : side == 1 ? -1 : 0, dz = side == 2 ? 1 : side == 3 ? -1 : 0;
						if (Wet(x + dx, z + dz))
						{
							continue;
						}
						edge = true;
						foreach (float cells in ShoreCells)
						{
							float reach = 0.5f * cell + cells * mapCell;
							Count(shore, idAt(cx + dx * reach, cz + dz * reach), 1f);
						}
					}
					if (edge)
					{
						Count(under, idAt(cx, cz), 1f);
					}
				}
			}
		}

		/// <summary>
		/// What lies along a river: the biome map read beside both its banks at every point (<see cref="ShoreCells"/> map
		/// cells past half its width), each point counted by the length of river it stands for, into <paramref name="shore"/>;
		/// and on its line into <paramref name="under"/>.
		/// </summary>
		public static void RiverShore(SceneHydrology.River river, Func<float, float, int> idAt, float mapCell, Dictionary<int, float> shore, Dictionary<int, float> under)
		{
			if (river == null || idAt == null || river.Points == null || river.Points.Length < 2)
			{
				return;
			}
			int n = river.Points.Length;
			mapCell = Mathf.Max(1f, mapCell);
			for (int i = 0; i < n; i++)
			{
				Vector3 p = river.Points[i];
				Vector3 before = river.Points[Mathf.Max(0, i - 1)], after = river.Points[Mathf.Min(n - 1, i + 1)];
				var back = new Vector2(p.x - before.x, p.z - before.z);
				var on = new Vector2(after.x - p.x, after.z - p.z);
				float weight = 0.5f * (back.magnitude + on.magnitude);
				Vector2 tangent = after.x - before.x == 0f && after.z - before.z == 0f ? Vector2.right : new Vector2(after.x - before.x, after.z - before.z).normalized;
				var left = new Vector2(-tangent.y, tangent.x);
				float half = 0.5f * (river.Width != null && i < river.Width.Length ? Mathf.Max(0f, river.Width[i]) : 4f);
				foreach (float cells in ShoreCells)
				{
					float reach = half + cells * mapCell;
					Count(shore, idAt(p.x + left.x * reach, p.z + left.y * reach), weight);
					Count(shore, idAt(p.x - left.x * reach, p.z - left.y * reach), weight);
				}
				Count(under, idAt(p.x, p.z), weight);
			}
		}

		/// <summary>How far a river's look runs into (or out of) the water at its end, for a river <paramref name="width"/> wide there: <see cref="MinBlendMetres"/>, or <see cref="BlendWidths"/> widths.</summary>
		public static float BlendMetres(float width) => Mathf.Max(MinBlendMetres, BlendWidths * Mathf.Max(0f, width));

		/// <summary>
		/// How much of a river's look at <paramref name="along"/> metres down it (of <paramref name="length"/>; negative on the
		/// rows it runs out of a lake, past the length on those it runs on into one) is the look of the water it leaves (x)
		/// and of the water it joins (y): all of it at either end, none past <paramref name="startMetres"/> from its start or
		/// <paramref name="endMetres"/> from its end, smoothly between; the two scaled to share where a short river's runs
		/// overlap. Its own look is the rest, 1 − x − y. Zero for an end that meets no water (a source, the sea, the edge).
		/// </summary>
		public static Vector2 Blend(float along, float length, float startMetres, float endMetres, bool fromStart, bool intoEnd)
		{
			float s = fromStart ? 1f - Smooth(along / Mathf.Max(1e-3f, startMetres)) : 0f;
			float e = intoEnd ? Smooth((along - (length - endMetres)) / Mathf.Max(1e-3f, endMetres)) : 0f;
			float sum = s + e;
			if (sum > 1f)
			{
				s /= sum;
				e /= sum;
			}
			return new Vector2(s, e);
		}

		private static float Smooth(float t)
		{
			t = Mathf.Clamp01(t);
			return t * t * (3f - 2f * t);
		}
	}
}
