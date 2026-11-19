using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// One orchard a biome's farmland plants (<see cref="BiomeTemplate.Orchards"/>, 2026-10-18, phase I2j-b, decision 23): a field
	/// set out in a grid of one tree species, drawn against the biome's crops by <see cref="Weight"/> (the same draw), with an
	/// understorey sown under the trees or the meadow left to grow there.
	/// </summary>
	/// <remarks>
	/// One species to an orchard (an olive grove, an almond orchard, a date grove), as orchards are planted; the biome's orchards
	/// share its fields by their weights. An orchard on a slope may be terraced (<see cref="Surface"/>, <see cref="Riser"/>): its
	/// trees then stand in rows along the middles of its benches.
	/// </remarks>
	[Serializable]
	public struct BiomeOrchard
	{
		[Tooltip("The tree the orchard is planted with (a generated tree prefab, Tree_*).")]
		public GameObject Tree;
		[Tooltip("How often a field here is this orchard, against the biome's crops (Field Crops) and its other orchards.")]
		[Min(0f)] public float Weight;
		[Tooltip("How far apart its trees stand, metres, both ways (0: 6 m).")]
		[Min(0f)] public float Spacing;
		[Tooltip("What is sown under the trees (a detail prefab such as a grass or a clover); none: the meadow that grew there is left.")]
		public GameObject Understorey;
		[Tooltip("How its ground is laid: Flat, or Terraced (benches stepped into a slope, the trees along their middles).")]
		public BiomeFieldSurface Surface;
		[Tooltip("How a terraced orchard's steps are faced: Drystone, Earth Bund or Grass Bank (none: drystone).")]
		public BiomeTerraceRiser Riser;

		public BiomeOrchard(GameObject tree, float weight, float spacing = 0f, GameObject understorey = null, BiomeFieldSurface surface = BiomeFieldSurface.Flat,
			BiomeTerraceRiser riser = BiomeTerraceRiser.None)
		{
			Tree = tree;
			Weight = weight;
			Spacing = spacing;
			Understorey = understorey;
			Surface = surface;
			Riser = riser;
		}

		/// <summary>The entry as the authoring marks and reports it: every value.</summary>
		public override string ToString() => (Tree != null ? Tree.name : "<none>") + "×" + Weight.ToString("R", CultureInfo.InvariantCulture)
			+ " @" + BiomeOrchards.SpacingOf(this).ToString("R", CultureInfo.InvariantCulture) + "m"
			+ (Understorey != null ? " over " + Understorey.name : string.Empty)
			+ (Surface == BiomeFieldSurface.Flat ? string.Empty : "|" + Surface)
			+ (Riser == BiomeTerraceRiser.None ? string.Empty : "|" + Riser);
	}

	/// <summary>The orchard rules that do not depend on a template asset (pure).</summary>
	public static class BiomeOrchards
	{
		/// <summary>How far apart an orchard's trees stand when its entry says 0, metres.</summary>
		public const float DefaultSpacing = 6f;

		/// <summary>No orchards: every biome's default.</summary>
		public static List<BiomeOrchard> None() => new List<BiomeOrchard>();

		/// <summary>An orchard's spacing, metres (<see cref="DefaultSpacing"/> for 0).</summary>
		public static float SpacingOf(BiomeOrchard orchard) => orchard.Spacing > 0f ? orchard.Spacing : DefaultSpacing;

		/// <summary>
		/// The rows of an orchard <paramref name="width"/> metres across (pure): each row's offset from the middle, metres, as many as
		/// fit <paramref name="spacing"/> apart with half a spacing to spare at either side (one in the middle when none fits).
		/// </summary>
		public static List<float> Rows(float width, float spacing)
		{
			var rows = new List<float>();
			float s = Mathf.Max(1f, spacing);
			int n = width >= s ? Mathf.FloorToInt((width - s) / s + 1e-4f) + 1 : 1;
			for (int i = 0; i < n; i++)
			{
				rows.Add((i - 0.5f * (n - 1)) * s);
			}
			return rows;
		}

		/// <summary>An orchard as a field-crop entry of the same draw: its tree, weight, surface and riser, its spacing as its rows.</summary>
		public static BiomeFieldCrop AsCrop(BiomeOrchard orchard) => new BiomeFieldCrop(orchard.Tree, orchard.Weight, orchard.Surface, orchard.Riser, SpacingOf(orchard));
	}

	/// <summary>
	/// How a biome lays its settlements' fields out (<see cref="BiomeTemplate.FieldLayout"/>, 2026-10-18, phase I2j-b): fields kept to the
	/// bottomland near rivers and lakes, and fields set square to a Roman-style centuriation grid. Both off (0) by default: fields
	/// lie where they always did.
	/// </summary>
	[Serializable]
	public struct BiomeFieldLayout
	{
		[Tooltip("Keep the fields within this many metres of a river or lake where the ground allows (a river bottom's farms): 0 anywhere.")]
		[Min(0f)] public float Bottomland;
		[Tooltip("Set the fields square to a centuriation grid this many metres a side (the Roman 710 m), each in a cell of it, the grid turned by the scene: 0 none.")]
		[Min(0f)] public float Centuriation;

		public BiomeFieldLayout(float bottomland, float centuriation)
		{
			Bottomland = bottomland;
			Centuriation = centuriation;
		}

		/// <summary>True when either is set.</summary>
		public bool Any => Bottomland > 0f || Centuriation > 0f;

		/// <summary>Every value, exactly: the authoring's mark.</summary>
		public string Signature() => Bottomland.ToString("R", CultureInfo.InvariantCulture) + "|" + Centuriation.ToString("R", CultureInfo.InvariantCulture);

		public override string ToString() => !Any ? "fields anywhere"
			: (Bottomland > 0f ? $"fields within {Bottomland.ToString("0.#", CultureInfo.InvariantCulture)} m of rivers" : "fields anywhere")
				+ (Centuriation > 0f ? $", on a {Centuriation.ToString("0.#", CultureInfo.InvariantCulture)} m centuriation grid" : string.Empty);
	}

	/// <summary>
	/// The centuriation grid's geometry (pure; 2026-10-18, I2j-b): one grid a scene, turned and offset by the scene's seed, each of
	/// its squares divided into cells about <see cref="CellTarget"/> across, a field set in the middle of a cell square to the grid.
	/// </summary>
	public static class BiomeCenturiation
	{
		/// <summary>The cell a square is divided into, metres, about.</summary>
		public const float CellTarget = 40f;
		/// <summary>The ground kept between a cell's field and the next, metres (a lane or ditch).</summary>
		public const float Gap = 6f;
		/// <summary>The smallest field side a cell is laid with, metres.</summary>
		public const float MinSide = 12f;

		/// <summary>The grid's turn in a scene, degrees (0 … 90), from the scene's seed.</summary>
		public static float GridYaw(int sceneSeed) => 90f * BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(sceneSeed, 0x43454E, 1, 0));

		/// <summary>The grid's origin in a scene, metres (each axis 0 … spacing), from the scene's seed.</summary>
		public static Vector2 GridOrigin(int sceneSeed, float spacing) => new Vector2(
			spacing * BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(sceneSeed, 0x43454E, 2, 0)),
			spacing * BiomeLinearFeatures.Unit(BiomeLinearFeatures.Hash(sceneSeed, 0x43454E, 3, 0)));

		/// <summary>The cell a square of <paramref name="spacing"/> metres is divided into, metres: the square over a whole number of cells about <see cref="CellTarget"/>.</summary>
		public static float CellOf(float spacing) => spacing / Mathf.Max(1, Mathf.RoundToInt(spacing / CellTarget));

		/// <summary>
		/// A drawn field set square to the scene's grid (pure): turned to the grid's yaw (or a quarter turn more, whichever is nearer the
		/// drawn yaw), its middle moved to the middle of the cell it lies in, its sides no longer than a cell less <see cref="Gap"/>.
		/// </summary>
		public static void Snap(Vector2 centre, float yaw, Vector2 size, int sceneSeed, float spacing, out Vector2 snappedCentre, out float snappedYaw, out Vector2 snappedSize)
		{
			float gridYaw = GridYaw(sceneSeed);
			float quarters = Mathf.Round(Mathf.DeltaAngle(gridYaw, yaw) / 90f);
			snappedYaw = gridYaw + 90f * ((((int)quarters % 2) + 2) % 2);
			float cell = CellOf(spacing);
			Vector2 origin = GridOrigin(sceneSeed, spacing);
			float r = gridYaw * Mathf.Deg2Rad;
			var axisX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
			var axisZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			Vector2 p = centre - origin;
			float a = Vector2.Dot(p, axisX), b = Vector2.Dot(p, axisZ);
			a = (Mathf.Floor(a / cell) + 0.5f) * cell;
			b = (Mathf.Floor(b / cell) + 0.5f) * cell;
			snappedCentre = origin + axisX * a + axisZ * b;
			float most = Mathf.Max(MinSide, cell - Gap);
			snappedSize = new Vector2(Mathf.Clamp(size.x, MinSide, most), Mathf.Clamp(size.y, MinSide, most));
		}
	}

	/// <summary>
	/// The old terraces round a biome's settlements (<see cref="BiomeTemplate.AbandonedTerraces"/>, 2026-10-18, phase I2j-b, decision
	/// 29): flights of benches stepped into the hillsides near a hamlet and long given up, their walls tumbled, broom and heath grown
	/// over them. Carved at the cut as a terraced field is; nothing sown. None (0 flights) by default.
	/// </summary>
	[Serializable]
	public struct BiomeAbandonedTerraces
	{
		[Tooltip("How many flights of old terraces stand round each of its settlements (where the slopes allow): 0 none.")]
		[Min(0)] public int Flights;
		[Tooltip("How their steps are faced: Drystone (tumbled walls), Earth Bund or Grass Bank.")]
		public BiomeTerraceRiser Riser;
		[Tooltip("The gentlest and steepest mean slope a flight is cut into, degrees.")]
		[Range(0f, 40f)] public float SlopeMin;
		[Range(0f, 40f)] public float SlopeMax;
		[Tooltip("The share of their walls tumbled or gone (0 whole … 1 none left).")]
		[Range(0f, 1f)] public float Breaks;

		public BiomeAbandonedTerraces(int flights, BiomeTerraceRiser riser = BiomeTerraceRiser.Drystone, float slopeMin = 8f, float slopeMax = 25f, float breaks = 0.5f)
		{
			Flights = flights;
			Riser = riser;
			SlopeMin = slopeMin;
			SlopeMax = slopeMax;
			Breaks = breaks;
		}

		/// <summary>None: every biome's default.</summary>
		public static BiomeAbandonedTerraces None => new BiomeAbandonedTerraces(0);

		/// <summary>True when a settlement here has any.</summary>
		public bool Any => Flights > 0 && SlopeMax > SlopeMin;

		/// <summary>Every value, exactly: the authoring's mark.</summary>
		public string Signature() => Flights + "|" + Riser + "|" + SlopeMin.ToString("R", CultureInfo.InvariantCulture) + "|"
			+ SlopeMax.ToString("R", CultureInfo.InvariantCulture) + "|" + Breaks.ToString("R", CultureInfo.InvariantCulture);

		public override string ToString() => !Any ? "none"
			: $"{Flights} flight(s) a settlement, {Riser} risers, {SlopeMin.ToString("0.#", CultureInfo.InvariantCulture)}–{SlopeMax.ToString("0.#", CultureInfo.InvariantCulture)}°, {Breaks.ToString("0.##", CultureInfo.InvariantCulture)} broken";
	}

	/// <summary>
	/// The ground a biome paints along its water channels (<see cref="BiomeTemplate.ChannelMargin"/>, 2026-10-18, phase I2j-b, decision
	/// 38): one of its own detail ground layers (a roggia's wet margin of gley silt and cress) laid in a strip either side of every
	/// channel the last placing of the scene's sites carried. The channels are laid after the ground is painted, so their lines are
	/// stored with the scene (GeneratedChannelLines) and the NEXT repaint paints along them. None by default.
	/// </summary>
	[Serializable]
	public struct BiomeChannelMargin
	{
		[Tooltip("The detail ground layer painted along channels (0 = the first detail layer, d0); −1 none.")]
		public int DetailLayer;
		[Tooltip("How far out from a channel's edge the margin reaches, metres.")]
		[Min(0f)] public float Width;

		public BiomeChannelMargin(int detailLayer, float width = 3f)
		{
			DetailLayer = detailLayer;
			Width = width;
		}

		/// <summary>None: every biome's default.</summary>
		public static BiomeChannelMargin None => new BiomeChannelMargin(-1, 0f);

		/// <summary>True when the biome paints a margin.</summary>
		public bool Defined => DetailLayer >= 0 && Width > 0f;

		/// <summary>Every value, exactly: the authoring's mark.</summary>
		public string Signature() => DetailLayer + "|" + Width.ToString("R", CultureInfo.InvariantCulture);

		public override string ToString() => !Defined ? "none" : $"detail layer {DetailLayer} {Width.ToString("0.#", CultureInfo.InvariantCulture)} m along channels";

		/// <summary>
		/// How much of a channel margin a point at <paramref name="distance"/> metres from a channel's line takes (pure), the channel
		/// <paramref name="half"/> metres in half width: all of it to the edge and over the first two thirds of the width, none past it.
		/// </summary>
		public static float Weight(float distance, float half, float width)
		{
			if (!(width > 0f))
			{
				return 0f;
			}
			float beyond = distance - Mathf.Max(0f, half);
			if (beyond <= 0f)
			{
				return 1f;
			}
			float t = Mathf.Clamp01((beyond - 0.66f * width) / (0.34f * width));
			return 1f - t * t * (3f - 2f * t);
		}
	}
}
