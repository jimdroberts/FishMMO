using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// What a ground is made of, read from its layer's name: x living (grass, moss, heath), y lichen and coral, z mineral (rock,
	/// sand, soil, ash), as a terrain layer's makeup stores it. Core (Phase 4 of the core and modules plan, 2026-11-18) so the
	/// baked terrain arrays read it without the World Simulation module; BiomeLooks.GroundShareOf forwards here.
	/// </summary>
	/// <remarks>
	/// A FALLBACK only, since 2026-10-15: each generated ground declares its makeup and the terrain array bake carries it per
	/// layer. A layer read by its name here is one whose set was baked before that, or hand-made art nobody gave a makeup.
	/// A frost or snow is mostly white whatever the world's ground (a frost on a rust world is still white), lava none (it
	/// glows its own colour).
	/// </remarks>
	public static class GroundMakeup
	{
		/// <summary>The makeup of a layer from its name: x living, y lichen and coral, z mineral.</summary>
		public static Vector3 OfLayerName(string layerName)
		{
			string n = (layerName ?? string.Empty).ToLowerInvariant();
			if (n.Contains("lava") || n.Contains("pahoehoe"))
			{
				return Vector3.zero;
			}
			if (n.Contains("lichen"))
			{
				return new Vector3(0f, 1f, 0f);
			}
			if (n.Contains("coral"))
			{
				return new Vector3(0f, 0.6f, 0.4f);
			}
			float living =
				n.Contains("grassdry") ? 0.55f :
				n.Contains("grass") || n.Contains("moss") || n.Contains("sphagnum") ? 1f :
				n.Contains("heath") ? 0.85f :
				n.Contains("jungle") ? 0.7f :
				n.Contains("forestfloor") || n.Contains("forest floor") ? 0.4f :
				n.Contains("needle") || n.Contains("peat") ? 0.3f :
				n.Contains("litter") ? 0.25f : 0f;
			float frozen = n.Contains("snow") || n.Contains("ice") || n.Contains("frost") ? 0.3f : 1f;
			return new Vector3(living, 0f, (1f - living) * frozen);
		}
	}
}
