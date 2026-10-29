using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// One linked cave as the glow modes read it (2026-10-26, phase I2n-c, decision 86 (h)): the box of its air (the union of its
	/// cave-life spots, padded) and the glow table's row of the interior biome dressing its inside, so a crystal or a glow-worm colony
	/// in it moves by the INTERIOR's glow mode (an EYELID, …) and not by the host land's over it.
	/// </summary>
	public struct SceneGlowCave
	{
		/// <summary>The box's corners, world metres.</summary>
		public Vector3 Min, Max;
		/// <summary>The cave's site (<see cref="PointOfInterestRecord.Id"/>) and its interior biome's id (<see cref="SceneCaveLifeSpot.Biome"/>).</summary>
		public int SiteId, Biome;
		/// <summary>The glow table's row its glows read (−1 until the table gives it one).</summary>
		public int Row;

		/// <summary>Whether a world point is in the box.</summary>
		public bool Holds(Vector3 p) => p.x >= Min.x && p.x <= Max.x && p.z >= Min.z && p.z <= Max.z && p.y >= Min.y && p.y <= Max.y;
	}

	/// <summary>
	/// The linked caves' boxes the glow modes read (pure; the GPU's FishGlowCaveRow mirrors <see cref="RowAt"/>): found from the cave-life
	/// spots every client loads (<see cref="SceneCaveLife"/>: each spot of a linked cave carries its interior biome), so nothing new is
	/// stored. A glow whose origin is inside a box reads the box's row before any anchor disc or the biome grid.
	/// </summary>
	public static class SceneGlowCaves
	{
		/// <summary>The most boxes the GPU holds (FishGlowModes.hlsl's _FishGlowCaves: two float4 each).</summary>
		public const int MaxCaves = 16;
		/// <summary>How far a box reaches past its spots' air, metres (the roof's colonies, the walls' crystals).</summary>
		public const float Pad = 2f;
		/// <summary>The widest and tallest a spot's own air is taken, metres.</summary>
		public const float MostAcross = 12f, MostUp = 30f;

		/// <summary>
		/// The air round one spot, world metres: across it the larger of its extent's x and z (1 to <see cref="MostAcross"/>), and up and
		/// down its extent's y (1.5 to <see cref="MostUp"/>: a floor's air to the roof, a roost's down to the floor, a box's half height).
		/// </summary>
		public static void SpotBounds(in SceneCaveLifeSpot s, out Vector3 min, out Vector3 max)
		{
			float h = Mathf.Clamp(Mathf.Max(s.Extent.x, s.Extent.z), 1f, MostAcross);
			float v = Mathf.Clamp(s.Extent.y, 1.5f, MostUp);
			min = new Vector3(s.Position.x - h, s.Position.y - v, s.Position.z - h);
			max = new Vector3(s.Position.x + h, s.Position.y + v, s.Position.z + h);
		}

		/// <summary>
		/// The boxes of the linked caves among <paramref name="spots"/> (a spot with an interior biome; its mouth spots left out, as they
		/// reach out of the cave), one per site and interior in the order first met, each the union of its spots' air padded by
		/// <see cref="Pad"/>, its row −1. Appended to <paramref name="into"/>.
		/// </summary>
		public static void FromSpots(IReadOnlyList<SceneCaveLifeSpot> spots, List<SceneGlowCave> into)
		{
			if (spots == null || into == null)
			{
				return;
			}
			int first = into.Count;
			for (int i = 0; i < spots.Count; i++)
			{
				SceneCaveLifeSpot s = spots[i];
				if (s.Biome == 0 || s.Kind == SceneCaveLifeKind.Mouth)
				{
					continue;
				}
				SpotBounds(in s, out Vector3 min, out Vector3 max);
				int at = -1;
				for (int k = first; k < into.Count; k++)
				{
					if (into[k].SiteId == s.SiteId && into[k].Biome == s.Biome)
					{
						at = k;
						break;
					}
				}
				if (at < 0)
				{
					into.Add(new SceneGlowCave { Min = min - Vector3.one * Pad, Max = max + Vector3.one * Pad, SiteId = s.SiteId, Biome = s.Biome, Row = -1 });
					continue;
				}
				SceneGlowCave c = into[at];
				c.Min = Vector3.Min(c.Min, min - Vector3.one * Pad);
				c.Max = Vector3.Max(c.Max, max + Vector3.one * Pad);
				into[at] = c;
			}
		}

		/// <summary>The row a glow at a world point reads: the first box holding it with a row, else <paramref name="fallback"/>.</summary>
		public static int RowAt(IReadOnlyList<SceneGlowCave> caves, Vector3 p, int fallback)
		{
			for (int i = 0; caves != null && i < caves.Count && i < MaxCaves; i++)
			{
				if (caves[i].Row >= 0 && caves[i].Holds(p))
				{
					return caves[i].Row;
				}
			}
			return fallback;
		}

		/// <summary>
		/// A box packed as the GPU reads it: (min x, min z, max x, max z) and (min y, max y, row, 0).
		/// </summary>
		public static void Pack(in SceneGlowCave c, out Vector4 a, out Vector4 b)
		{
			a = new Vector4(c.Min.x, c.Min.z, c.Max.x, c.Max.z);
			b = new Vector4(c.Min.y, c.Max.y, c.Row, 0f);
		}
	}
}
