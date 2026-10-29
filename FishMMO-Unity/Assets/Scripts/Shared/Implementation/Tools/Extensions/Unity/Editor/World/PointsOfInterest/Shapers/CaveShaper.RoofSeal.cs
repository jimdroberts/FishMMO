#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class CaveShaper
	{
		// ── The roof sealed under the ground as it now is (2026-10-27) ─────────────────

		/// <summary>How far round an opening the cut made the ground is never raised, metres: its collar's ring frames it there (CaveSystem.Field).</summary>
		public const float SealOpeningClear = 4f;

		/// <summary>How steeply a raise falls away round the columns that need it, metres down per metre out: a swell in the hillside, not a step.</summary>
		public const float SealFalloff = 0.5f;

		/// <summary>
		/// Where and how much the ground over a cave must rise for its rock to roof it (<see cref="CaveSystem.SealLevel"/>), read on a
		/// grid and eased out round each column that needs it (<see cref="SealFalloff"/>). Pure: built from the ground as given.
		/// </summary>
		public sealed class RoofSeal
		{
			private readonly float minX, minZ, spacing;
			private readonly int countX, countZ, reach;
			private readonly float[] raise;

			/// <summary>The columns raised, those whose air no rise roofs (an opening the cut never made), and the most any rose, metres.</summary>
			public readonly int Raised, Unsealed;
			public readonly float Most;

			/// <summary>The area the raise reaches (world east/north), eased edges included; empty when nothing rises.</summary>
			public readonly Rect Area;

			/// <summary>A point that no rise roofs, for the cut's notes (world), or NaN.</summary>
			public readonly Vector3 FirstUnsealed;

			public RoofSeal(CaveSystem system, Func<float, float, float> groundAt, Func<float, float, bool> nearOpening, float spacing)
			{
				FirstUnsealed = new Vector3(float.NaN, float.NaN, float.NaN);
				this.spacing = Mathf.Max(0.25f, spacing);
				Bounds e = system.Extent;
				minX = system.Origin.x + e.min.x;
				minZ = system.Origin.z + e.min.z;
				countX = Mathf.Max(1, Mathf.CeilToInt(e.size.x / this.spacing) + 1);
				countZ = Mathf.Max(1, Mathf.CeilToInt(e.size.z / this.spacing) + 1);
				raise = new float[countX * countZ];
				if (!CaveSystem.SealsRoof(system.Form))
				{
					return;
				}
				int i0 = int.MaxValue, i1 = int.MinValue, k0 = int.MaxValue, k1 = int.MinValue;
				for (int k = 0; k < countZ; k++)
				{
					float z = minZ + k * this.spacing;
					for (int i = 0; i < countX; i++)
					{
						float x = minX + i * this.spacing;
						if (nearOpening != null && nearOpening(x, z))
						{
							continue;
						}
						float g = groundAt(x, z);
						if (float.IsNaN(g))
						{
							continue;
						}
						float level = system.SealLevel(x, z, g);
						if (float.IsNaN(level))
						{
							Unsealed++;
							if (float.IsNaN(FirstUnsealed.x))
							{
								FirstUnsealed = new Vector3(x, g, z);
							}
							continue;
						}
						if (level > g + 1e-3f)
						{
							raise[k * countX + i] = level - g;
							Raised++;
							i0 = Mathf.Min(i0, i);
							i1 = Mathf.Max(i1, i);
							k0 = Mathf.Min(k0, k);
							k1 = Mathf.Max(k1, k);
							Most = Mathf.Max(Most, level - g);
						}
					}
				}
				reach = Mathf.CeilToInt(Most / SealFalloff / this.spacing);
				float grow = reach * this.spacing;
				// Round the columns that rise only, eased edges and all (what EditHeights walks).
				Area = Raised > 0 ? Rect.MinMaxRect(minX + i0 * this.spacing - grow, minZ + k0 * this.spacing - grow, minX + i1 * this.spacing + grow, minZ + k1 * this.spacing + grow)
					: default;
			}

			/// <summary>How much the ground at (east, north) rises: the most any column's raise left after easing out to it, metres.</summary>
			public float Raise(float east, float north)
			{
				if (Raised == 0)
				{
					return 0f;
				}
				int ci = Mathf.RoundToInt((east - minX) / spacing), ck = Mathf.RoundToInt((north - minZ) / spacing);
				float best = 0f;
				for (int k = Mathf.Max(0, ck - reach); k <= Mathf.Min(countZ - 1, ck + reach); k++)
				{
					for (int i = Mathf.Max(0, ci - reach); i <= Mathf.Min(countX - 1, ci + reach); i++)
					{
						float r = raise[k * countX + i];
						if (r <= best)
						{
							continue;
						}
						float dx = minX + i * spacing - east, dz = minZ + k * spacing - north;
						best = Mathf.Max(best, r - SealFalloff * Mathf.Sqrt(dx * dx + dz * dz));
					}
				}
				return best;
			}
		}

		/// <summary>
		/// Raises the ground over a system where it has come down to its rock since the plan (CaveSystem.RoofSeal.cs), away from the
		/// openings the cut made, before the rock is meshed against it. Noted with the site's label.
		/// </summary>
		private static void SealRoof(PointOfInterestPlaceContext context, CaveSystem system, string label)
		{
			if (!CaveSystem.SealsRoof(system.Form) || context.Terrains == null || context.GroundAt == null
				|| !HoleGrid(context.Terrains, context.Tiles, out float step, out _, out _))
			{
				return;
			}
			// The cut's own openings round every mouth (the collar frames those).
			var cells = new List<Vector2>();
			foreach (CaveMouth mouth in system.Mouths)
			{
				Vector3 at = system.Origin + mouth.Floor;
				float reach = mouth.Zone + 2f * mouth.Radius + 10f;
				cells.AddRange(PointOfInterestTerrain.HolesIn(context.Terrains, context.Tiles, new Rect(at.x - reach, at.z - reach, 2f * reach, 2f * reach)));
				if (mouth.Kind == CaveMouthKind.Skylight)
				{
					Vector3 top = system.Origin + CaveSystem.SkylightOpening(mouth);
					float r = mouth.Radius * (1f + CaveSystem.SkylightFlare) + mouth.Collar + 10f;
					cells.AddRange(PointOfInterestTerrain.HolesIn(context.Terrains, context.Tiles, new Rect(top.x - r, top.z - r, 2f * r, 2f * r)));
				}
			}
			var openings = new PointOfInterestCaveOpeningClearance.OpeningIndex(cells, step);
			var seal = new RoofSeal(system, context.GroundAt, (x, z) => openings.Within(new Vector2(x, z), SealOpeningClear), context.Tiles.MetresPerSample);
			if (seal.Raised > 0)
			{
				PointOfInterestTerrain.EditHeights(context.Terrains, context.Tiles, seal.Area, (east, north, h) => h + seal.Raise(east, north));
				context.Notes?.Add($"{label}: the ground over its rock raised in {seal.Raised:N0} column(s) (up to {seal.Most:0.0} m) where it had come down to the cave's air.");
			}
			if (seal.Unsealed > 0)
			{
				context.Notes?.Add($"{label}: {seal.Unsealed:N0} column(s) of its air stand more than {CaveSystem.MaxSeal:0} m into the ground's skin with no opening cut "
					+ $"(first at ({seal.FirstUnsealed.x:F0}, {seal.FirstUnsealed.y:F0}, {seal.FirstUnsealed.z:F0})): open to the sky there.");
			}
		}
	}
}
#endif
