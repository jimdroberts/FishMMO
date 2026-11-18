#if UNITY_EDITOR
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/*
	 * The rock over a cave where nothing holed it (2026-10-27, Moron 2: "we are still seeing holes in cave walls from the interior
	 * facing out"). The shell stops a skin under the ground (Field's `buried`), so wherever the cave's air comes up into that skin
	 * and the terrain over it was not holed, the cave has no roof but the terrain, which is drawn from above only: from inside, a
	 * hole onto the sky. The layout keeps its ways MinCover under the ground it was planned on, but the ground is not left as it
	 * was planned: the ways are carved, other sites' pads levelled, hot ground dug, after the cave was laid out, and the layout's
	 * own cover tests sample a bore at a few points. So before a cave is meshed the ground over it is read again, and wherever
	 * the air is closer to the skin than SealRock (and no opening is cut near), the ground is raised till it is not
	 * (CaveShaper.SealRoof): sealed by the hill itself, never by a lid of rock standing on the terrain.
	 */
	public sealed partial class CaveSystem
	{
		/// <summary>The least rock kept between the cave's air and the skin under unholed ground, metres: two of the mesher's cells, and a metre.</summary>
		public float SealRock => Mathf.Max(2f * MeshCell, 1f);

		/// <summary>The most the ground is raised to seal the rock over a cave, metres; air standing higher than that is an opening the cut never made.</summary>
		public const float MaxSeal = 4f;

		/// <summary>Whether a form's rock is sealed under its ground: every dry cave's (an awash grotto's or sea cave's ground is its shore and bed).</summary>
		public static bool SealsRoof(CaveForm form)
			=> form == CaveForm.Cave || form == CaveForm.IceCave || form == CaveForm.LavaTube || form == CaveForm.Mine;

		/// <summary>
		/// The ground (world y) the column at (<paramref name="worldX"/>, <paramref name="worldZ"/>) needs for the cave's air in it
		/// to stay <see cref="SealRock"/> under the skin: <paramref name="groundY"/> where it already does (or the column holds no air
		/// that near), more where its air comes closer, NaN where its air stands so high no rise up to <see cref="MaxSeal"/> roofs
		/// it. Measured down the column itself (air beside a column is its neighbour's to seal), traced by the field's distances.
		/// Pure.
		/// </summary>
		public float SealLevel(float worldX, float worldZ, float groundY)
		{
			if (float.IsNaN(groundY))
			{
				return groundY;
			}
			if (segA == null)
			{
				Prepare();
			}
			float need = SealRock;
			float x = worldX - Origin.x, z = worldZ - Origin.z, clip = groundY - Origin.y - Skin;
			float top = clip + MaxSeal, bottom = clip - need;
			// Nothing of the cave within reach of the column's span: the common case, one reading.
			float v = Void(new Vector3(x, top, z));
			if (v >= top - bottom)
			{
				return groundY;
			}
			// Down from the highest a seal reaches to the first air (the column's highest within it), by the field's distances.
			float y = top;
			while (v >= 0f)
			{
				y -= Mathf.Max(0.1f, v);
				if (y < bottom)
				{
					return groundY;
				}
				v = Void(new Vector3(x, y, z));
			}
			if (y >= top - 1e-3f)
			{
				// Air at the top of the reach already: higher than any seal.
				return float.NaN;
			}
			// Up to the air's top (bisected), then the ground that leaves the rock over it.
			float lo = y, hi = Mathf.Min(top, y + Mathf.Max(0.1f, -v) + 0.1f);
			while (hi < top && Void(new Vector3(x, hi, z)) < 0f)
			{
				lo = hi;
				hi = Mathf.Min(top, hi + 0.25f);
			}
			if (Void(new Vector3(x, hi, z)) < 0f)
			{
				return float.NaN;
			}
			for (int i = 0; i < 8; i++)
			{
				float mid = 0.5f * (lo + hi);
				if (Void(new Vector3(x, mid, z)) < 0f)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			float lift = hi + need - clip;
			if (lift > MaxSeal)
			{
				return float.NaN;
			}
			return groundY + Mathf.Max(0f, lift);
		}
	}
}
#endif
