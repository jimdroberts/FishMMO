using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What a carved cave's run-time cover is made of (2026-10-20): the cover types it grows (<see cref="CaveCoverType"/> bits),
	/// its density and glow, and the colours its glowing cover takes. Decided at the cut from the cave's biome and form
	/// (the editor's CaveCoverSpecies.For) and written on the cave's client-only Visual beside its <see cref="CaveVisibility"/>,
	/// because at run time the client knows neither: the cover renderer (FishMMO.Client CaveCoverSystem) reads it from there.
	/// </summary>
	/// <remarks>
	/// A cave cut before it has none: the renderer then draws <see cref="CaveCoverTypes.Natural"/> with <see cref="Default"/>'s
	/// colours, so an old scene still has cover until its points of interest are regenerated.
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class CaveCoverSite : MonoBehaviour
	{
		[Tooltip("The form the cave was cut as (CaveForm's value: 0 cave, 1 grotto, 2 sea cave, 3 ice cave, 4 lava tube, 5 mine).")]
		[SerializeField] private byte form;
		[Tooltip("The cover types it grows, a bit a CaveCoverType.")]
		[SerializeField] private uint types = CaveCoverTypes.Natural;
		[Tooltip("Its cover's density against the types' own (the biome's climate: arid sparser, wet richer).")]
		[SerializeField] private float density = 1f;
		[Tooltip("Its glowing cover's strength against the settings'.")]
		[SerializeField] private float glow = 1f;
		[Tooltip("The glow of its leading fungus hue (HDR), as the dressing's fungi glow.")]
		[SerializeField] private Color fungus = new Color(0.32f, 1.25f, 0.82f);
		[Tooltip("The glow of its accent fungus hue (HDR).")]
		[SerializeField] private Color accent = new Color(0.3f, 0.7f, 1.45f);
		[Tooltip("The glow of its crystal hue (HDR): mineral glints and frost.")]
		[SerializeField] private Color crystal = new Color(0.95f, 0.45f, 1.6f);

		/// <summary>The choice a cave with none carries.</summary>
		public static readonly Choice Default = new Choice
		{
			Form = 0,
			Types = CaveCoverTypes.Natural,
			Density = 1f,
			Glow = 1f,
			Fungus = new Color(0.32f, 1.25f, 0.82f),
			Accent = new Color(0.3f, 0.7f, 1.45f),
			Crystal = new Color(0.95f, 0.45f, 1.6f),
		};

		/// <summary>A cave's cover, as decided at the cut.</summary>
		public struct Choice
		{
			public byte Form;
			public uint Types;
			public float Density, Glow;
			public Color Fungus, Accent, Crystal;
		}

		/// <summary>The cave's choice as stored.</summary>
		public Choice Current => new Choice { Form = form, Types = types, Density = density, Glow = glow, Fungus = fungus, Accent = accent, Crystal = crystal };

		/// <summary>Set by the cut.</summary>
		public void Configure(in Choice choice)
		{
			form = choice.Form;
			types = choice.Types;
			density = choice.Density;
			glow = choice.Glow;
			fungus = choice.Fungus;
			accent = choice.Accent;
			crystal = choice.Crystal;
		}
	}
}
