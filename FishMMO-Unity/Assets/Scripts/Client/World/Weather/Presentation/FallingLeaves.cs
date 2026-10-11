using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Leaves coming down under and downwind of the trees: how many and when, and how a leaf falls. Pure
	/// functions, tested; <see cref="FallingLeavesPresenter"/> draws them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>They fall exactly while the canopies thin.</b> The vegetation shader strips a deciduous crown by the
	/// season (FishVegetationPasses.hlsl, <c>VegSeasonTint</c>): how bare a plant is, from its phase's distance
	/// to midwinter, shifted ±0.02 of a year plant by plant. Every leaf that disappears from a crown has to
	/// come down, so the rate leaves fall at is the rate the canopy's bareness grows — its derivative,
	/// averaged over the plants' spread (<see cref="SeasonalFall"/>). That is a trapezoid in the year: it rises
	/// from nothing at phase 0.84, holds its peak from 0.88 to 0.90 and is over by 0.94, while the leaf-out in
	/// spring, when bareness falls again, drops nothing.
	/// </para>
	/// <para>
	/// <b>Some all year</b> (<see cref="AllYearDeciduous"/>, <see cref="AllYearEvergreen"/>): a broadleaf wood
	/// sheds damaged and shaded leaves through the summer, and an evergreen sheds its oldest year-round — a
	/// few percent of the autumn peak. <b>More in a gust</b>: a leaf whose stalk has sealed off hangs on until
	/// the wind breaks it, so the autumn fall comes in bursts on the gusts (<see cref="WindRelease"/>).
	/// </para>
	/// <para>
	/// <b>How a leaf falls</b> is a flat plate at a Reynolds number of a few thousand: it settles at a terminal
	/// speed of about a metre a second (v = √(2mg / ρC_dA) for a 0.3 g, 30 cm² leaf with C_d ≈ 1), and does it
	/// in one of two ways (Field et al. 1997, Nature): FLUTTER, a side-to-side pendulum swing — tilting up at
	/// each end of the swing, gliding through the middle level and fast — or TUMBLE, turning end over end
	/// about a horizontal axis and falling faster, autorotation's lift carrying it sideways. Which one is
	/// the plate's moment of inertia against the air's (Smith 1971); a wood's leaves do both.
	/// </para>
	/// </remarks>
	public static class FallingLeaves
	{
		// ── The canopy's own curve (FishVegetationPasses.hlsl, VegSeasonTint) ──

		/// <summary>The year phase of deepest bareness: <c>abs(phase - 0.02)</c> in the shader.</summary>
		public const float BareCentre = 0.02f;
		/// <summary>How far from <see cref="BareCentre"/> (a share of the year) bareness begins: the shader's 0.16.</summary>
		public const float BareReach = 0.16f;
		/// <summary>The share of the year it takes to go from full leaf to bare: the shader's / 0.06.</summary>
		public const float BareSpan = 0.06f;
		/// <summary>Half the plants' spread in phase: the shader's <c>(variation - 0.5) * 0.04</c>.</summary>
		public const float PlantSpread = 0.02f;

		/// <summary>
		/// The share of the autumn peak a broadleaf wood drops through the rest of the year. Above the few percent a real wood
		/// sheds: leaves on the air are what make a wood feel alive, and at 6% one went by unseen (Jim, 2026-10-10).
		/// </summary>
		public const float AllYearDeciduous = 0.2f;
		/// <summary>The same for evergreens, whose old leaves and needles come down a few at a time year round.</summary>
		public const float AllYearEvergreen = 0.08f;

		/// <summary>The terminal speed of an ordinary broadleaf leaf, m/s: the slowest flutterer and the fastest tumbler bracket it.</summary>
		public static readonly Vector2 TerminalSpeed = new Vector2(0.9f, 1.8f);

		/// <summary>The share of leaves that tumble rather than flutter.</summary>
		public const float TumbleShare = 0.3f;

		/// <summary>
		/// How bare one plant is at a phase (0 in leaf .. 1 bare): the shader's own expression, exactly.
		/// </summary>
		/// <param name="variation">The plant's own 0..1 (its tint variation in the shader).</param>
		/// <param name="strength">How strongly the year swings here (<c>_FishSeason.w</c>); 0 is no season known.</param>
		public static float Bare(float phase, float variation, float strength)
		{
			if (strength < 1e-4f)
			{
				return 0f;
			}
			float fromWinter = Mathf.Abs(phase - BareCentre);
			fromWinter = Mathf.Min(fromWinter, 1f - fromWinter);
			return Mathf.Clamp01((BareReach - fromWinter + (variation - 0.5f) * 2f * PlantSpread) / BareSpan) * strength;
		}

		/// <summary>
		/// How fast the canopies are losing their leaves, 0..1 of the peak (multiplied by the season's
		/// strength): the derivative of the mean bareness over the plants' spread, on the autumn side of
		/// midwinter only. Zero through spring's leaf-out, when bareness falls, and through the summer.
		/// </summary>
		/// <remarks>
		/// Each plant's bareness ramps linearly over <see cref="BareSpan"/> of the year, so its rate is a box;
		/// the plants' phases are spread evenly over ±<see cref="PlantSpread"/>, so the mean rate is that box
		/// smeared by another — a trapezoid. With x = <see cref="BareReach"/> − (distance to midwinter), the
		/// share of plants mid-ramp is the overlap of [−x, <see cref="BareSpan"/> − x] with ±<see cref="PlantSpread"/>.
		/// </remarks>
		public static float SeasonalFall(float phase, float strength)
		{
			if (strength < 1e-4f)
			{
				return 0f;
			}
			// Signed offset from midwinter, wrapped into (-0.5, 0.5]: negative is autumn, coming toward it.
			float offset = Mathf.Repeat(phase - BareCentre + 0.5f, 1f) - 0.5f;
			if (offset >= 0f)
			{
				return 0f;
			}
			float x = BareReach + offset;   // BareReach - |offset|
			float overlap = Mathf.Min(PlantSpread, BareSpan - x) - Mathf.Max(-PlantSpread, -x);
			return Mathf.Clamp01(overlap / (2f * PlantSpread)) * Mathf.Clamp01(strength);
		}

		/// <summary>
		/// How much the wind adds to what is ready to fall, a multiplier: half the leaves that are going come
		/// down in still air, the rest wait for a gust to break them off.
		/// </summary>
		/// <param name="wind01">The wind speed channel (0..1 = 0..30 m/s).</param>
		/// <param name="gust">The gust now, 0..1.</param>
		public static float WindRelease(float wind01, float gust)
		{
			return 0.5f + 0.5f * Mathf.Clamp01(Mathf.Clamp01(wind01) * 3f + Mathf.Clamp01(gust) * 1.2f) * 2f;
		}

		/// <summary>
		/// The share of the field's particles shown under a full canopy of each kind: deciduous (seasonal and
		/// all year, the seasonal part released by the wind) and evergreen (all year). The shader multiplies
		/// each by its canopy's cover where the leaf came from.
		/// </summary>
		public static Vector2 Rates(float phase, float strength, float wind01, float gust)
		{
			float release = WindRelease(wind01, gust);
			float deciduous = SeasonalFall(phase, strength) * release + AllYearDeciduous * Mathf.Lerp(1f, release, 0.5f);
			float evergreen = AllYearEvergreen * Mathf.Lerp(1f, release, 0.5f);
			return new Vector2(Mathf.Clamp01(deciduous), Mathf.Clamp01(evergreen));
		}

		/// <summary>
		/// How much of what falls now has turned (autumn colours) rather than still green: all of the autumn
		/// fall — a leaf drops after its stalk has sealed and the green has gone — and half the summer trickle.
		/// </summary>
		public static float TurnedShare(float phase, float strength, float wind01, float gust)
		{
			float release = WindRelease(wind01, gust);
			float seasonal = SeasonalFall(phase, strength) * release;
			float trickle = AllYearDeciduous * Mathf.Lerp(1f, release, 0.5f);
			return (seasonal + 0.5f * trickle) / Mathf.Max(1e-5f, seasonal + trickle);
		}

		/// <summary>
		/// How far downwind of its tree a leaf at this height has come, m: it left the crown at
		/// <paramref name="releaseHeight"/>, fell at <paramref name="fallSpeed"/> and went with the air the whole
		/// way (a leaf matches the air's motion within v/g, a tenth of a second). The shader looks this far
		/// upwind for the canopy a leaf could have come from.
		/// </summary>
		public static float Downwind(float releaseHeight, float height, float fallSpeed, float windSpeed)
		{
			return Mathf.Max(0f, windSpeed) * Mathf.Max(0f, releaseHeight - height) / Mathf.Max(0.1f, fallSpeed);
		}

		/// <summary>
		/// The air's speed among the trunks against the ten-metre wind: a canopy's drag holds the wind
		/// under it to some 0.3–0.5 of the open wind (Cionco's canopy profile), and leaves fall through it.
		/// </summary>
		public const float UnderCanopyWind = 0.45f;
	}

	/// <summary>The falling leaves' look and budget, on the weather render profile.</summary>
	[System.Serializable]
	public class FallingLeavesSettings
	{
		[Tooltip("Draw leaves coming down from the trees near the camera.")]
		public bool Enabled = true;

		[Tooltip("Particles in the field, per quality level. 0 switches the leaves off on that level.")]
		[Min(0)] public int ParticlesPerformant = 0;
		[Min(0)] public int ParticlesBalanced = 3000;
		[Min(0)] public int ParticlesHigh = 6000;

		[Tooltip("The box the field wraps in round the camera, m across. Leaves are small: past ~20 m they are a pixel.")]
		[Range(10f, 80f)] public float BoxSize = 36f;

		[Tooltip("The box's height, m: tall enough to hold the leaves from a crown's top to the ground.")]
		[Range(8f, 60f)] public float BoxHeight = 30f;

		[Tooltip("A leaf's length, m: the smallest and the largest.")]
		public Vector2 LeafSize = new Vector2(0.04f, 0.09f);

		[Tooltip("A fresh leaf's own colour (linear), before the trees' tint. The trees' leaf atlas is not readable at run time, so this stands in for its mean.")]
		public Color LeafAlbedo = new Color(0.12f, 0.2f, 0.04f, 1f);

		[Tooltip("A dead, dry leaf's colour (linear): the brown share of an autumn fall.")]
		public Color DryAlbedo = new Color(0.14f, 0.085f, 0.04f, 1f);

		[Tooltip("The share of turned leaves that have already browned through.")]
		[Range(0f, 1f)] public float BrownShare = 0.3f;

		[Tooltip("How long a landed leaf lies before it fades, s.")]
		[Range(0f, 10f)] public float LieSeconds = 2.5f;

		[Tooltip("The map of the canopy round the camera, m a texel (32 texels across).")]
		[Range(2f, 8f)] public float CanopyTexelMetres = 4f;

		[Tooltip("Opacity of a leaf.")]
		[Range(0f, 1f)] public float Alpha = 1f;

		/// <summary>The particles for a quality level (0 Performant, 1 Balanced, 2+ High Fidelity).</summary>
		public int ParticlesFor(int qualityLevel)
		{
			return qualityLevel <= 0 ? ParticlesPerformant : qualityLevel == 1 ? ParticlesBalanced : ParticlesHigh;
		}
	}
}
