using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// A landform ZONE band on a ground layer or a spawn rule (2026-10-25, phase I2g-b, decision 55: "LANDFORM ZONE MASK as a general
	/// ground/spawn BAND, shared by Crater rays, Dust Sea dust-fill, Cryovolcanic streaks"): the layer is painted, or the rule grows,
	/// only on one part of its biome's sculpted landforms (a crater's rays or rim, a basin's mare or wrinkle ridges, a cell field's
	/// troughs, a dune's lee) — or, turned round, only off it — beside its height, slope, climate, air and heat bands.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The part's mask is the landform's own (BiomeLandforms.Shape.Mask, the very mask its paint entry uses), read from the sites the
	/// cut stored with the scene, so a cut and every repaint band the same. Only the band's own biome's landforms count, and only
	/// those named <see cref="Kind"/> when it names one.
	/// </para>
	/// <para>
	/// Off (the default) a layer and a rule are exactly what they were. On, with no landform stored (a scene cut before, or none
	/// placed), the part is nowhere: an "on the part" band paints nothing, an "off the part" band everywhere.
	/// </para>
	/// </remarks>
	[Serializable]
	public struct BiomeLandformBand
	{
		[Tooltip("Paint this ground (or grow this rule) only on (or, with Outside, only off) one part of its biome's sculpted landforms. Off = as before.")]
		public bool On;
		[Tooltip("The part of the landforms: Floor, Rim, Rays, Flank, Crest, Lee, Ejecta, Channel, Melt, Knobby, Junction…")]
		public BiomeLandformPart Part;
		[Tooltip("Only the landforms of this name (an entry's Kind: ImpactCrater, MultiRingBasin, ConvectionCells…); empty = any of its biome's.")]
		public string Kind;
		[Tooltip("Turn the band round: off the part rather than on it.")]
		public bool Outside;

		/// <summary>A band on (or off) a part of the biome's landforms, of one name or any.</summary>
		public static BiomeLandformBand Of(BiomeLandformPart part, string kind = null, bool outside = false)
			=> new BiomeLandformBand { On = true, Part = part, Kind = string.IsNullOrWhiteSpace(kind) ? string.Empty : kind.Trim(), Outside = outside };

		/// <summary>The band from a part's mask there (0 … 1): itself, or turned round; 1 when off.</summary>
		public float WeightOf(float mask)
		{
			if (!On)
			{
				return 1f;
			}
			mask = Mathf.Clamp01(mask);
			return Outside ? 1f - mask : mask;
		}

		/// <summary>True when a landform of this name counts (an empty Kind: every one).</summary>
		public bool Names(string kind) => string.IsNullOrEmpty(Kind) || string.Equals(Kind, kind, StringComparison.OrdinalIgnoreCase);

		/// <summary>The band as the authoring marks it ("" when off, so a mark written before the band existed still matches).</summary>
		public string Signature() => On ? $"zone {Part}{(string.IsNullOrEmpty(Kind) ? string.Empty : " " + Kind)}{(Outside ? " out" : string.Empty)}" : string.Empty;

		public override string ToString() => On ? Signature() : "off";
	}

	/// <summary>
	/// The landform zones of a scene (decision 55): its stored sites made shapes and bucketed by their boxes, so a ground layer's or a
	/// spawn rule's <see cref="BiomeLandformBand"/> asks a part's mask at a point cheaply (pure; thread-safe once built).
	/// </summary>
	public sealed partial class BiomeLandformZones
	{
		/// <summary>The bucket size, metres.</summary>
		public const float BucketMetres = 128f;

		private readonly List<BiomeLandforms.Shape> shapes = new List<BiomeLandforms.Shape>();
		private readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();

		/// <summary>No landform: every "on" band's part is nowhere.</summary>
		public static readonly BiomeLandformZones None = new BiomeLandformZones(null, null);

		public BiomeLandformZones(IReadOnlyList<BiomeLandformSite> sites, BiomeLandforms.Context context)
		{
			if (sites == null)
			{
				return;
			}
			foreach (BiomeLandformSite site in sites)
			{
				var shape = new BiomeLandforms.Shape(site, context);
				int index = shapes.Count;
				shapes.Add(shape);
				Vector4 b = shape.Bounds;
				int x0 = Mathf.FloorToInt(b.x / BucketMetres), x1 = Mathf.FloorToInt(b.z / BucketMetres);
				int z0 = Mathf.FloorToInt(b.y / BucketMetres), z1 = Mathf.FloorToInt(b.w / BucketMetres);
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						long key = ((long)z << 32) ^ (uint)x;
						if (!buckets.TryGetValue(key, out List<int> list))
						{
							buckets[key] = list = new List<int>();
						}
						list.Add(index);
					}
				}
			}
		}

		/// <summary>How many landforms it holds.</summary>
		public int Count => shapes.Count;

		/// <summary>
		/// The strongest mask of a part at (x, z) over the biome's landforms the band names (0 … 1); the ground's height and slope there
		/// are what the paint reads after the carve.
		/// </summary>
		public float Mask(in BiomeLandformBand band, int biomeID, float x, float z, float ground, float slope)
		{
			long key = ((long)Mathf.FloorToInt(z / BucketMetres) << 32) ^ (uint)Mathf.FloorToInt(x / BucketMetres);
			if (!buckets.TryGetValue(key, out List<int> near))
			{
				return 0f;
			}
			float best = 0f;
			foreach (int i in near)
			{
				BiomeLandforms.Shape shape = shapes[i];
				if (shape.Site.BiomeID != biomeID || !band.Names(shape.Site.Form.Kind))
				{
					continue;
				}
				best = Mathf.Max(best, shape.Mask(band.Part, x, z, ground, slope));
				if (best >= 1f)
				{
					break;
				}
			}
			return best;
		}

		/// <summary>The band's weight at a point: 1 when off; else its part's mask there, or turned round.</summary>
		public float Weight(in BiomeLandformBand band, int biomeID, float x, float z, float ground, float slope)
			=> band.On ? band.WeightOf(Mask(band, biomeID, x, z, ground, slope)) : 1f;

		/// <summary><see cref="Weight"/> on zones that may be missing (null: no landform stored).</summary>
		public static float WeightOf(BiomeLandformZones zones, in BiomeLandformBand band, int biomeID, float x, float z, float ground, float slope)
			=> band.On ? (zones ?? None).Weight(band, biomeID, x, z, ground, slope) : 1f;

		/// <summary>
		/// Talus SIZE GRADING (2026-10-25, phase I2u, decision 79 (d)): the scale a graded rule's instance takes at (x, z) on its biome's talus
		/// cones (RockChutes.ConeKinds): <see cref="RockChutes.ApexScale"/> at a cone's apex to <see cref="RockChutes.ToeScale"/> at its toe,
		/// eased to 1 over the cone's edge; 1 off every cone (and with no zones). The cone holding the point most fully decides.
		/// </summary>
		public float TalusGrade(int biomeID, float x, float z)
		{
			long key = ((long)Mathf.FloorToInt(z / BucketMetres) << 32) ^ (uint)Mathf.FloorToInt(x / BucketMetres);
			if (!buckets.TryGetValue(key, out List<int> near))
			{
				return 1f;
			}
			float best = 0f, along = 0f;
			foreach (int i in near)
			{
				BiomeLandformSite site = shapes[i].Site;
				if (site.BiomeID != biomeID || !RockChutes.IsCone(site.Form.Kind))
				{
					continue;
				}
				float a = RockChutes.ConeAlong(site, x, z, out float weight);
				if (weight > best)
				{
					best = weight;
					along = a;
				}
			}
			return RockChutes.GradeAt(along, best);
		}

		/// <summary><see cref="TalusGrade"/> on zones that may be missing (null: 1).</summary>
		public static float TalusGradeOf(BiomeLandformZones zones, int biomeID, float x, float z) => zones != null ? zones.TalusGrade(biomeID, x, z) : 1f;
	}
}
