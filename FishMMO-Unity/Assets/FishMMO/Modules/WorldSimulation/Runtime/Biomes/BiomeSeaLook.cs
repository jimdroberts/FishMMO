using System;
using System.Collections.Generic;
using System.Globalization;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The sea as a diver sees it off a biome's coast (<see cref="BiomeTemplate.SeaLook"/>, 2026-10-18, phase I2h): the
	/// underwater pass's own terms (FishMMO/Water/Underwater, WaterSurface's Underwater settings): the water's colour, how far
	/// one sees through it and how murky it is for that. Peat-brown water running off a bog coast, a mangrove's tannin, a
	/// glacier coast milky with rock flour, a reef's clear turquoise.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The colour is as WaterSurface's Underwater Tint holds it (gamma); the visibility in metres and the murk as the pass's
	/// Visibility and Murk (its extinction is murk ÷ visibility per metre). Until 2026-10-18 every sea took the one set of values
	/// every scene's WaterSurface carries; every biome's look is authored from exactly those (<see cref="BiomeSeaLooks.Today"/>),
	/// and a biome whose look is that one says nothing about the sea, which keeps its scene's own (<see cref="BiomeSeaLooks.Coast"/>):
	/// nothing changes until a biome's round gives it its own.
	/// </para>
	/// <para>The sea's surface keeps its colour from the world's climate (WaterEnvironment); this is the water under it.</para>
	/// </remarks>
	[Serializable]
	public struct BiomeSeaLook
	{
		[Tooltip("The sea's colour seen from under its surface off this biome's coast (the underwater pass's Water colour).")]
		public Color Tint;
		[Tooltip("How far one sees through it, metres (the underwater pass's Visibility).")]
		[Range(1f, 200f)] public float Visibility;
		[Tooltip("How murky it is for that visibility (the underwater pass's Murk): its extinction is murk ÷ visibility per metre.")]
		[Range(0f, 3f)] public float Murk;

		public BiomeSeaLook(Color tint, float visibility, float murk)
		{
			Tint = tint;
			Visibility = visibility;
			Murk = murk;
		}

		/// <summary>False for a look nobody filled in (all zero: zeroed by hand or by code; a template made before the field existed reads <see cref="BiomeSeaLooks.Today"/>).</summary>
		public bool Defined => Tint.a > 0f || Visibility > 0f || Murk > 0f;

		/// <summary>How much of the light a metre of this water takes out, as the underwater pass works it out (murk ÷ visibility, at least a metre's).</summary>
		public float Extinction => Mathf.Max(1e-4f, Murk / Mathf.Max(1f, Visibility));

		/// <summary>True when every value is within <paramref name="tolerance"/> of <paramref name="other"/>'s (alpha ignored).</summary>
		public bool Approximately(BiomeSeaLook other, float tolerance = 1e-4f)
		{
			return Mathf.Abs(Tint.r - other.Tint.r) <= tolerance && Mathf.Abs(Tint.g - other.Tint.g) <= tolerance && Mathf.Abs(Tint.b - other.Tint.b) <= tolerance
				&& Mathf.Abs(Visibility - other.Visibility) <= tolerance * Mathf.Max(1f, Mathf.Abs(other.Visibility)) && Mathf.Abs(Murk - other.Murk) <= tolerance;
		}

		/// <summary>The look as the authoring marks it: every value round-trip exact, in a fixed order.</summary>
		public string Signature()
		{
			var text = new System.Text.StringBuilder(80);
			void Add(float value) => text.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
			Add(Tint.r);
			Add(Tint.g);
			Add(Tint.b);
			Add(Tint.a);
			Add(Visibility);
			Add(Murk);
			return text.ToString();
		}

		/// <summary>The look as the authoring reports it.</summary>
		public override string ToString()
		{
			string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
			return $"sea ({F(Tint.r)}, {F(Tint.g)}, {F(Tint.b)}), {F(Visibility)} m, murk {F(Murk)}";
		}
	}

	/// <summary>
	/// The sea's look along a coast (2026-10-18, phase I2h): which biomes' sea looks reach the water at a point, and how they
	/// blend with each other and with the scene's own sea. Pure: the underwater pass (WaterSurface) asks it at the eye.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One sea, many coasts.</b> The sea is one surface over the whole scene and its underwater pass one full-screen pass
	/// from the eye, so the look is asked for where the eye is, once a frame and only while it is under the sea: the water in
	/// front of a diver is the water round them for the few tens of metres they can see. No texture, nothing more on the GPU.
	/// </para>
	/// <para>
	/// <b>The coast's biomes, out to <see cref="ReachMetres"/>.</b> The scene's biome map is read on a world-fixed lattice
	/// (<see cref="SpacingMetres"/>) round the eye, each point weighted by a smooth bump of its distance that falls to nothing at
	/// the reach (<see cref="Weight"/>). The land biomes of the shore are on the map beside the sea's own (Ocean, Coastal Water,
	/// Coral Reef…), so a bog's peat-brown look stains the sea within the reach of its shore, a reef's own look holds over the
	/// reef, and out past every coast the open ocean's biomes, whose looks are the sea's own, leave it the scene's. The lattice
	/// is fixed to the world and the weights reach nothing at the reach's edge, so the look changes smoothly as the eye moves.
	/// </para>
	/// <para>
	/// <b>A biome whose look is the sea's own (<see cref="Today"/>) says nothing.</b> It neither pulls the water toward the
	/// default nor away from it: the points that carry another look give their share (<paramref name="coverage"/> of
	/// <see cref="Coast"/>), and the rest is the scene's own sea, whatever its WaterSurface says. So a world whose every biome
	/// has today's look draws every sea exactly as before, a scene with its own hand-tuned underwater settings keeps them, and
	/// open water far from any coast with a look of its own is the scene's sea.
	/// </para>
	/// <para>
	/// <b>Murk blends as water mixes.</b> Colours are blended by weight; the extinction (murk ÷ visibility), which adds as
	/// stained water mixes with clear, is blended by weight, and given back as a murk at the scene's own visibility.
	/// </para>
	/// </remarks>
	public static class BiomeSeaLooks
	{
		/// <summary>
		/// The underwater look every sea had until 2026-10-18: WaterSurface's Underwater Tint (0.05, 0.28, 0.36) and Visibility
		/// 22 m, and the underwater pass's Murk 1 (FishWaterVolume.shader; the surface never sets it). Every biome's default and
		/// what the authoring writes for each (a test holds it equal to WaterSurface and the shader).
		/// </summary>
		public static BiomeSeaLook Today => new BiomeSeaLook(new Color(0.05f, 0.28f, 0.36f, 1f), 22f, 1f);

		/// <summary>How far from a stretch of coast its biome's sea look reaches into the water, metres.</summary>
		public const float ReachMetres = 160f;

		/// <summary>The lattice the biome map is read on round the eye, metres (about a hundred points inside the reach).</summary>
		public const float SpacingMetres = 25f;

		private static readonly HashSet<BiomeTemplate> warned = new HashSet<BiomeTemplate>();

		/// <summary>
		/// A biome's sea look (<see cref="BiomeTemplate.SeaLook"/>); <see cref="Today"/>'s, with one warning a session, for a
		/// template whose look is not filled in. Null for no biome.
		/// </summary>
		public static BiomeSeaLook? Of(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return null;
			}
			BiomeSeaLook look = biome.SeaLook;
			if (look.Defined)
			{
				return look;
			}
			if (warned.Add(biome))
			{
				Log.Warning("Biome templates", $"'{biome.name}' has no Sea Look; the sea off it keeps its scene's own. Run Author biome art.");
			}
			return Today;
		}

		/// <summary>True when a look says something of its own about the sea (it is not <see cref="Today"/>'s).</summary>
		public static bool OwnLook(BiomeSeaLook look) => look.Defined && !look.Approximately(Today);

		/// <summary>How much a point <paramref name="distance"/> metres from the eye counts: 1 at the eye, falling smoothly ((1 − (d ÷ reach)²)²) to 0 at <paramref name="reach"/> and past it.</summary>
		public static float Weight(float distance, float reach)
		{
			if (!(reach > 0f) || !(distance < reach))
			{
				return 0f;
			}
			float t = distance / reach;
			float s = 1f - t * t;
			return s * s;
		}

		/// <summary>
		/// The blend of the sea looks of the biomes round (<paramref name="x"/>, <paramref name="z"/>) that have looks of their
		/// own (pure; <paramref name="coverage"/> their share of the weight, 0 … 1, the rest the scene's own sea). The biome map is
		/// read through <paramref name="idAt"/> on the world's lattice of <paramref name="spacing"/> within
		/// <paramref name="reach"/>; <paramref name="lookOf"/> gives an ID's look, null for none (the ID says nothing, as a look of
		/// today's does). With coverage 0 the look returned is <see cref="Today"/>.
		/// </summary>
		public static BiomeSeaLook Coast(float x, float z, Func<float, float, int> idAt, Func<int, BiomeSeaLook?> lookOf, float reach, float spacing, out float coverage)
		{
			coverage = 0f;
			if (idAt == null || lookOf == null || !(reach > 0f) || !(spacing > 0f))
			{
				return Today;
			}
			int i0 = Mathf.CeilToInt((x - reach) / spacing), i1 = Mathf.FloorToInt((x + reach) / spacing);
			int j0 = Mathf.CeilToInt((z - reach) / spacing), j1 = Mathf.FloorToInt((z + reach) / spacing);
			float all = 0f, own = 0f, r = 0f, g = 0f, b = 0f, extinction = 0f;
			for (int j = j0; j <= j1; j++)
			{
				for (int i = i0; i <= i1; i++)
				{
					float px = i * spacing, pz = j * spacing;
					float w = Weight(Mathf.Sqrt((px - x) * (px - x) + (pz - z) * (pz - z)), reach);
					if (!(w > 0f))
					{
						continue;
					}
					all += w;
					BiomeSeaLook? look = lookOf(idAt(px, pz));
					if (look == null || !OwnLook(look.Value))
					{
						continue;
					}
					BiomeSeaLook l = look.Value;
					own += w;
					r += w * l.Tint.r;
					g += w * l.Tint.g;
					b += w * l.Tint.b;
					extinction += w * l.Extinction;
				}
			}
			if (!(own > 0f) || !(all > 0f))
			{
				return Today;
			}
			coverage = Mathf.Clamp01(own / all);
			float visibility = Today.Visibility;
			return new BiomeSeaLook(new Color(r / own, g / own, b / own, 1f), visibility, extinction / own * Mathf.Max(1f, visibility));
		}

		/// <summary>
		/// The sea at the eye (pure): the scene's own (<paramref name="scene"/>, its WaterSurface's) where nothing of the coast's
		/// reaches (<paramref name="coverage"/> 0, returned exactly), else blended toward the coast's (<see cref="Coast"/>) by
		/// its coverage: the colour by weight, the extinction by weight, given back as a murk at the scene's own visibility.
		/// </summary>
		public static BiomeSeaLook Mix(BiomeSeaLook scene, BiomeSeaLook coast, float coverage)
		{
			if (!(coverage > 0f))
			{
				return scene;
			}
			float t = Mathf.Clamp01(coverage);
			float visibility = Mathf.Max(1f, scene.Visibility);
			float extinction = Mathf.Lerp(scene.Extinction, coast.Extinction, t);
			Color tint = Color.Lerp(scene.Tint, coast.Tint, t);
			tint.a = scene.Tint.a;
			return new BiomeSeaLook(tint, scene.Visibility, extinction * visibility);
		}
	}
}
