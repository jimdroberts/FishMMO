#if UNITY_EDITOR
using System;
using System.Threading.Tasks;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How plateaus are stepped.</summary>
	public sealed class PlateauSettings
	{
		/// <summary>Beds per step: a step of the staircase is one of the rock's cycles, soft beds under a hard cap (<see cref="PlateauTerrace.BedsPerCycle"/>).</summary>
		public float BedsPerStep = PlateauTerrace.BedsPerCycle;

		/// <summary>The fewest metres a step stands, whatever the beds: below it the staircase reads as noise.</summary>
		public float MinimumStepMetres = 12f;

		/// <summary>
		/// How steep a riser stands, in degrees, wherever the ground's broad slope lets a tread form:
		/// steep enough for the biomes' cliff paint (35–42°) and so for the cliff rocks that stand
		/// on it, which is what makes a riser read as a stepped rock wall; not so steep that a
		/// heightmap two metres a sample draws it as a one-sample step.
		/// </summary>
		public float RiserDegrees = 58f;

		/// <summary>The scale the ground's broad shape is read at, in metres: what is turned into a staircase.</summary>
		public float ShapeMetres = 250f;

		/// <summary>Share of the ground's finer relief kept on the treads, so they are not glass-flat.</summary>
		public float DetailKept = 0.25f;
	}

	/// <summary>
	/// Turns ground over flat-lying rock into a staircase of benches — the start of mesas, buttes and
	/// canyons.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why benches.</b> Canyon country is flat-lying beds of alternating hardness lifted high above
	/// where its rivers drain. Each hard bed caps a flat surface and breaks off at its edge in a
	/// cliff; each soft bed below it wears back into a slope. Seen across a region that is a
	/// staircase — Utah's Grand Staircase is the famous one — and rivers cutting down through it
	/// leave canyons whose walls repeat the steps, and mesas and buttes where the benches are worn
	/// back to islands. Noise-built ground has none of that structure; this gives it the staircase,
	/// and the landscape evolution model that runs next cuts the canyons.
	/// </para>
	/// <para>
	/// <b>Steps on the rock's own planes.</b> The ground's broad shape is placed in the rock's stack
	/// of beds (its altitude plus the province's offset — see <see cref="GeologyColumn.Offset"/>) and
	/// stepped there, a step every few beds, then carried back. So the benches follow the beds as they
	/// dip, and the steps line up with the hardness the erosion will meet.
	/// </para>
	/// <para>
	/// <b>A soft step</b>: flat for most of each step, then a smooth riser standing at
	/// <see cref="PlateauSettings.RiserDegrees"/> however gently the ground beneath slopes, so the
	/// staircase is continuous, every riser is steep enough for the biomes' cliff paint and the cliff
	/// rocks that stand on it, and the erosion that follows works its walls back bed by bed. Finer
	/// relief rides on top at a share, so treads are not glass-flat.
	/// </para>
	/// <para>Pure arithmetic per sample, in parallel; deterministic.</para>
	/// </remarks>
	public static class PlateauTerrace
	{
		/// <summary>
		/// Beds in one of flat-lying sediment's cycles: soft beds capped by a hard one. The geology
		/// lays its beds this way (<see cref="GeologyColumn.CapEvery"/>) and the plateau's steps are one
		/// cycle tall, so every tread lands on a cap.
		/// </summary>
		public const int BedsPerCycle = 3;

		/// <summary>Steps <paramref name="height"/> in place wherever <paramref name="weight"/> is above zero.</summary>
		/// <param name="weight">Per sample, how much of a plateau the ground becomes, 0 … 1.</param>
		/// <param name="offset">Per sample, metres added to an altitude to place it in the rock's stack of beds.</param>
		/// <param name="stepMetres">Per sample, the height of one step, in metres.</param>
		/// <param name="baseLevel">A sea's surface: ground under it is left alone.</param>
		public static void Apply(float[] height, int width, int depth, float cellMetres, float[] weight, float[] offset, float[] stepMetres,
			float baseLevel, PlateauSettings settings)
		{
			int radius = Math.Max(1, (int)MathF.Round(settings.ShapeMetres / cellMetres / 3f));
			var shape = new float[height.Length];
			LandscapeEvolution.SmoothInto(height, shape, width, depth, radius);
			float riserTangent = MathF.Tan(Math.Clamp(settings.RiserDegrees, 5f, 85f) * MathF.PI / 180f);

			Parallel.For(0, depth, z =>
			{
				for (int x = 0; x < width; x++)
				{
					int i = z * width + x;
					float w = weight[i];
					if (w <= 0f || height[i] < baseLevel)
					{
						continue;
					}
					float step = Math.Max(settings.MinimumStepMetres, stepMetres[i]);
					/* The riser's share of each step, from the broad slope: a step rises step metres over
					 * step ÷ slope of ground, and a smoothstep's steepest is 1.5 times its mean, so a riser
					 * standing at the target angle takes 1.5 × slope ÷ tan of it. Steeper broad ground than
					 * that has no room for treads and keeps its slope. */
					int xa = Math.Max(0, x - 1), xb = Math.Min(width - 1, x + 1), za = Math.Max(0, z - 1), zb = Math.Min(depth - 1, z + 1);
					float gx = (shape[z * width + xb] - shape[z * width + xa]) / ((xb - xa) * cellMetres);
					float gz = (shape[zb * width + x] - shape[za * width + x]) / ((zb - za) * cellMetres);
					float riser = Math.Clamp(1.5f * MathF.Sqrt(gx * gx + gz * gz) / riserTangent, 0.03f, 1f);
					float strata = shape[i] + offset[i];
					float stepped = Step(strata / step, riser) * step - offset[i];
					float detail = (height[i] - shape[i]) * settings.DetailKept;
					height[i] += w * (stepped + detail - height[i]);
				}
			});
		}

		/// <summary>
		/// A soft staircase over a coordinate in steps: each whole number a flat tread (1 − riser)
		/// wide centred on it, joined to the next by a smoothstep riser. Continuous, monotone, and
		/// symmetric about every tread, so it keeps the ground's mean height — and the treads land on
		/// whole steps, which are the caps of the rock's cycles.
		/// </summary>
		public static float Step(float t, float riser)
		{
			float tread = 1f - riser;
			float shifted = t + 0.5f * tread;
			float floor = MathF.Floor(shifted);
			float within = shifted - floor;
			if (within <= tread)
			{
				return floor;
			}
			float u = (within - tread) / riser;
			return floor + u * u * (3f - 2f * u);
		}
	}
}
#endif
