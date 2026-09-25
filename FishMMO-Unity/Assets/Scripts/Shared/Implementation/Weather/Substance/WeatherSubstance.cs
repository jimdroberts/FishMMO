using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// What the stuff falling out of the sky actually IS, as distinct from how it behaves.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this exists instead of more channels.</b> A weather frame mixes precipitation across a
	/// fixed five — rain, snow, hail, ash, sand — and those five describe how something falls: a
	/// drop streaks, a flake drifts, a stone drops, a cinder floats. They do not describe what it is
	/// made of. Nitrogen snow on a cold moon falls exactly like water snow and is a completely
	/// different substance; cryovolcanic tephra falls like ash and is ice; the rain on a runaway
	/// greenhouse world is sulphuric acid.
	/// </para>
	/// <para>
	/// Adding a channel per substance would widen the frame, the delta serializer and every authored
	/// layer for something that never changes how the blend works. So the mix keeps its five, and
	/// the substance rides on the LAYER — which both peers already resolve from the timeline's
	/// template and preset IDs. Nothing new goes on the wire.
	/// </para>
	/// <para>
	/// <b>It carries both halves.</b> The look and sound, because the client needs them; and the
	/// melt point, the cover it leaves and whether it is breathable, because the server needs those
	/// to decide what lying snow does and what standing in it costs. One asset, so the two can never
	/// disagree about what is falling.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Weather Substance", menuName = "FishMMO/Weather/Substance", order = 8)]
	public class WeatherSubstance : CachedScriptableObject<WeatherSubstance>, ICachedObject
	{
		[Tooltip("Shown in tooltips and tools. The asset's name is used when empty.")]
		public string DisplayName;

		[TextArea]
		[Tooltip("What this is, for whoever authors with it. Editor only.")]
		public string Description;

		[Header("How it looks")]
		[Tooltip("Tint for the falling particles. The kind's default look supplies everything this does not override.")]
		public Color Tint = Color.white;

		[Tooltip("Colour the air takes on while this is falling thickly.")]
		public Color FogColor = new Color(0.6f, 0.62f, 0.66f, 1f);

		[Tooltip("Multiplies the kind's fall speed for what the particles are made of: denser falls faster (sulphuric acid is nearly twice as dense as water), lighter slower. The substance only: the world's own gravity and air are applied separately, so do not fold thin air in here.")]
		[Range(0.1f, 4f)] public float FallSpeedScale = 1f;

		[Tooltip("Multiplies how far a particle is stretched by its own speed. Above 1 for something that streaks.")]
		[Range(0f, 4f)] public float StretchScale = 1f;

		[Tooltip("How much light it gives off on its own: 0 for snow, above 0 for glowing tephra or an irradiated dust.")]
		[Range(0f, 1f)] public float Emission;

		[Header("What it leaves")]
		[Tooltip("The cover it builds on the ground. None means it falls and vanishes, like rain on dry sand.")]
		public WeatherCoverKind Cover = WeatherCoverKind.None;

		[Tooltip("Tint of that cover where it lies.")]
		public Color CoverTint = Color.white;

		[Tooltip("Local temperature above which the cover melts away. 2 means it never does, which is right for ash and sand.")]
		[Range(-1f, 2f)] public float MeltsAbove = 0f;

		[Header("What it does to whoever is in it")]
		[Tooltip("Safe to breathe. Off for ash, acid and anything on an airless world — the exposure states can read this.")]
		public bool Breathable = true;

		[Tooltip("How harsh it is to stand in, 0..1, before any exposure state is authored. A guide for authoring, and readable by one.")]
		[Range(0f, 1f)] public float Harshness;

		[Header("Sound")]
		[Tooltip("Audio cue name for this substance falling. Empty uses the kind's own cue.")]
		public string AudioCue;

		public string ResolvedDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName;
	}

	/// <summary>What a substance leaves lying on the ground.</summary>
	/// <remarks>
	/// Deliberately the same four the surface shaders already build — wetness, snow, ash and sand —
	/// because a substance changes the COLOUR and the melt point of a cover, not the machinery that
	/// accumulates it. Nitrogen snow lies as snow, tinted blue-white, that melts far below zero.
	/// </remarks>
	public enum WeatherCoverKind : byte
	{
		/// <summary>Falls and leaves nothing behind.</summary>
		None = 0,
		/// <summary>Wets what it lands on: rain, and anything else liquid.</summary>
		Wet = 1,
		/// <summary>Settles and builds up: water snow, nitrogen snow, cryo-tephra.</summary>
		Snow = 2,
		/// <summary>A dry layer that does not melt: volcanic ash, soot, tholin.</summary>
		Ash = 3,
		/// <summary>Drifts and piles: sand, regolith dust.</summary>
		Sand = 4,
	}
}
