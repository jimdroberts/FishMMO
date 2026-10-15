using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// How far a kind takes the looks of the biome it lives in (<see cref="BiomeLooks"/>): a rabbit's coat is the colour of
	/// its ground, a crow is a crow on any of them. Per kind, by body: FishAmbientLife.hlsl's <c>_LookMix</c> and the
	/// group's size.
	/// </summary>
	public struct AmbientLookMix
	{
		/// <summary>How far the back and belly move from the kind's own toward the look's coat and belly, 0..1.</summary>
		public float Coat;
		/// <summary>How far the accents (bill, legs, wing tips, a scut) move toward the look's markings, 0..1.</summary>
		public float Accent;
		/// <summary>How strongly the look's pattern (stripes, spots, bands, mottling) is drawn on the coat, 0..1.</summary>
		public float Markings;
		/// <summary>
		/// How much of the kind's own brightness the coat keeps, 0..1: at 1 a black bird takes only the look's hue and stays
		/// black (a crow is dark on pale sand), at 0 it takes the look's colour outright.
		/// </summary>
		public float KeepBrightness;
		/// <summary>How far the group's body length follows the look's size, 0..1 (0 for swarms: their insects are a speck).</summary>
		public float Size;

		/// <summary>The shader's <c>_LookMix</c>: coat, accent, markings, brightness kept.</summary>
		public Vector4 Shader => new Vector4(Coat, Accent, Markings, KeepBrightness);
	}

	/// <summary>
	/// The ambient animals dressed for the biome under them (2026-10-15: "Grass, flowers, critters, and other detail objects
	/// look identical on all biomes and/or planets"). Each group, when placed, takes one of its biome's twelve looks
	/// (<see cref="FloraPaletteMap.LookAt"/>) by a weighted pick from its own seed, so the same group wears the same coat
	/// for every player and every frame; its members all wear it, each a little lighter or darker by its own tint.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One float4 an instance.</b> The look rides next to <c>_Tint</c> as <c>_Look</c>: x, y and z the coat, belly and
	/// markings as 8-bit sRGB packed into a whole number (exact in a float up to 2^24), w = 1 + the pattern + its phase
	/// (a fraction), or 0 for no look, where the shader draws the kind's own colours exactly as before. Small enough that
	/// it goes through the same MaterialPropertyBlock arrays as the rest (1023 a batch).
	/// </para>
	/// <para>
	/// <b>Colour roles, not colours.</b> The meshes keep the kind's own colours in COLOR and carry, in TEXCOORD2, how much
	/// of each vertex's colour came from the kind's back, belly and accent; the shader swaps each role's own colour for the
	/// look's by <see cref="AmbientLookMix"/>, so countershading, dark eyes and shaded wing undersides survive any coat.
	/// </para>
	/// <para>
	/// <b>Camouflage for the ground, identity for the air.</b> What lives on the ground (mammals, lizards, frogs, crabs,
	/// beetles) and the insects take most of the look: the colour of their country. Birds and bats take less, and keep
	/// their own brightness: a gull stays pale, a crow stays dark, only their tones follow the land. Swarms take half on
	/// their specks; a firefly's light is its own.
	/// </para>
	/// </remarks>
	public static class AmbientLooks
	{
		/// <summary>The look's pattern sits in w as 1 + pattern; its phase is the fraction, scaled under one.</summary>
		private const float PhaseScale = 0.95f;

		/// <summary>The most a look's size stretches or shrinks a group: past these an animal reads as another kind.</summary>
		public const float MinSize = 0.85f, MaxSize = 1.2f;

		/// <summary>How far a kind takes its biome's looks (see <see cref="AmbientLookMix"/>).</summary>
		public static AmbientLookMix MixOf(AmbientCreatureKind kind)
		{
			if (kind == null)
			{
				return default;
			}
			switch (kind.Shape)
			{
				// Ground mammals: coats the colour of their ground, markings plain to see up close.
				case AmbientShape.Rat:
				case AmbientShape.Mouse:
				case AmbientShape.Squirrel:
				case AmbientShape.Rabbit:
					return Mix(0.7f, 0.3f, 0.8f, 0f, 1f);
				// Reptiles and amphibians: the most camouflaged of all, their markings bold.
				case AmbientShape.Lizard:
					return Mix(0.8f, 0.5f, 1f, 0f, 1f);
				case AmbientShape.Frog:
					return Mix(0.75f, 0.4f, 0.9f, 0f, 1f);
				// Shells: a crab and a beetle take the ground's tone, keeping a little of their sheen's own darkness.
				case AmbientShape.Crab:
					return Mix(0.65f, 0.4f, 0.7f, 0.15f, 1f);
				case AmbientShape.Beetle:
					return Mix(0.7f, 0.4f, 0.8f, 0.2f, 1f);
				// Winged insects: a butterfly's wings are the flowers' (the look's markings borrow its blooms).
				case AmbientShape.Butterfly:
					return Mix(0.65f, 0.6f, 1f, 0f, 0.5f);
				case AmbientShape.Dragonfly:
					return Mix(0.5f, 0.3f, 0.6f, 0.2f, 0.5f);
				// Birds: their species first. Their tones follow the land; their brightness is their own.
				case AmbientShape.Crow:
					return Mix(0.3f, 0.05f, 0.25f, 1f, 0.3f);
				case AmbientShape.Gull:
					return Mix(0.3f, 0.1f, 0.35f, 0.9f, 0.4f);
				case AmbientShape.Duck:
				case AmbientShape.Goose:
					return Mix(0.35f, 0.1f, 0.4f, 0.7f, 0.4f);
				case AmbientShape.Raptor:
				case AmbientShape.Vulture:
					return Mix(0.35f, 0.15f, 0.5f, 0.6f, 0.5f);
				case AmbientShape.Owl:
					return Mix(0.4f, 0.15f, 0.6f, 0.5f, 0.5f);
				case AmbientShape.Bat:
					return Mix(0.3f, 0.15f, 0.3f, 0.7f, 0.5f);
				// Another world's flier: no earthly identity to keep, so it wears its sky's colours a little more.
				case AmbientShape.Ray:
					return Mix(0.4f, 0.3f, 0.6f, 0.4f, 0.5f);
				// A swarm's specks: half the look, no pattern on a speck, no size (the cloud is the kind's). A glowing swarm's
				// bodies matter less still: its light is what is seen, and that stays its own.
				case AmbientShape.Swarm:
					return kind.Glow > 0f ? Mix(0.3f, 0f, 0f, 0.3f, 0f) : Mix(0.5f, 0f, 0f, 0.2f, 0f);
				// Songbirds and the rest.
				default:
					return Mix(0.4f, 0.15f, 0.5f, 0.5f, 0.5f);
			}
		}

		private static AmbientLookMix Mix(float coat, float accent, float markings, float keep, float size)
			=> new AmbientLookMix { Coat = coat, Accent = accent, Markings = markings, KeepBrightness = keep, Size = size };

		/// <summary>
		/// A group's look where it lives: one of the biome's looks there by a weighted pick from the group's seed (the same
		/// for everyone, every frame), packed for the shader, and how much it scales the group's length. False (and no look)
		/// where the world has no looks built: the kind's own colours and size, as before.
		/// </summary>
		public static bool TryChoose(IAmbientWorld world, AmbientCreatureKind kind, uint groupSeed, Vector3 at, out Vector4 packed, out int variant, out float sizeScale)
		{
			packed = Vector4.zero;
			variant = -1;
			sizeScale = 1f;
			if (world == null || kind == null || !world.LookAt(at, out BiomeLooks.Look look) || look == null || look.Variants == null || look.Variants.Length == 0)
			{
				return false;
			}
			variant = Mathf.Clamp(look.Pick(SeaLifePlacement.Unit(SeaLifePlacement.Mix(groupSeed ^ 0xC0A7u))), 0, look.Variants.Length - 1);
			BiomeLooks.Variant v = look.Variants[variant];
			float phase = SeaLifePlacement.Unit(SeaLifePlacement.Mix(groupSeed ^ 0xC0A8u));
			packed = Pack(v.Coat, v.Belly, v.Marking, v.Pattern, phase);
			sizeScale = Mathf.Lerp(1f, Mathf.Clamp(v.Size, MinSize, MaxSize), Mathf.Clamp01(MixOf(kind).Size));
			return true;
		}

		/// <summary>A look as the shader's <c>_Look</c>: three packed sRGB colours, and 1 + pattern + phase.</summary>
		public static Vector4 Pack(Color coat, Color belly, Color marking, BiomeLooks.CoatPattern pattern, float phase)
		{
			return new Vector4(PackSrgb(coat), PackSrgb(belly), PackSrgb(marking), 1f + (int)pattern + Mathf.Clamp01(phase) * PhaseScale);
		}

		/// <summary>An sRGB colour as a whole number r + 256 g + 65536 b (8 bits each), exact in a float.</summary>
		public static float PackSrgb(Color srgb)
		{
			uint r = (uint)Mathf.RoundToInt(Mathf.Clamp01(srgb.r) * 255f);
			uint g = (uint)Mathf.RoundToInt(Mathf.Clamp01(srgb.g) * 255f);
			uint b = (uint)Mathf.RoundToInt(Mathf.Clamp01(srgb.b) * 255f);
			return r | (g << 8) | (b << 16);
		}

		/// <summary>The sRGB colour a packed number holds (the shader's unpacking, for tests and the log).</summary>
		public static Color UnpackSrgb(float packed)
		{
			uint u = (uint)packed;
			return new Color((u & 255u) / 255f, ((u >> 8) & 255u) / 255f, ((u >> 16) & 255u) / 255f, 1f);
		}

		/// <summary>Whether a packed look is one (w 0: the kind's own colours).</summary>
		public static bool Has(Vector4 packed) => packed.w >= 0.5f;

		/// <summary>The pattern a packed look draws.</summary>
		public static BiomeLooks.CoatPattern PatternOf(Vector4 packed) => (BiomeLooks.CoatPattern)Mathf.Max(0, Mathf.FloorToInt(packed.w) - 1);
	}
}
