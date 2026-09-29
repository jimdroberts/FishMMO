using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws what is falling around the camera: one pre-built field of quads per quality budget,
	/// drawn once per kind that is falling, animated entirely in the vertex shader.
	/// </summary>
	public sealed class PrecipitationField
	{
		/// <summary>Kinds drawn at once, strongest first.</summary>
		public const int MaxKindsAtOnce = 3;

		private static readonly int OriginId = Shader.PropertyToID("_PrecipOrigin");
		private static readonly int BoxId = Shader.PropertyToID("_PrecipBox");
		private static readonly int FallId = Shader.PropertyToID("_PrecipFall");
		private static readonly int TravelId = Shader.PropertyToID("_PrecipTravel");
		private static readonly int SpreadId = Shader.PropertyToID("_PrecipSpread");
		private static readonly int ShapeId = Shader.PropertyToID("_PrecipShape");
		private static readonly int FlutterId = Shader.PropertyToID("_PrecipFlutter");
		private static readonly int ColorId = Shader.PropertyToID("_PrecipColor");
		private static readonly int GrainId = Shader.PropertyToID("_PrecipGrain");
		private static readonly int TileId = Shader.PropertyToID("_PrecipTile");
		private static readonly int CrowdId = Shader.PropertyToID("_PrecipCrowd");
		private static readonly int ShellId = Shader.PropertyToID("_PrecipShell");
		private static readonly int MotionId = Shader.PropertyToID("_PrecipMotion");
		private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

		private static readonly WeatherChannel[] Kinds =
		{
			WeatherChannel.RainWeight, WeatherChannel.SnowWeight, WeatherChannel.HailWeight, WeatherChannel.AshWeight, WeatherChannel.SandWeight,
		};

		/// <summary>
		/// How finely a particle's own speed is stepped against its kind's: it falls at a whole number
		/// of these, so the fall can be wrapped without moving anything (see <see cref="Advance"/>).
		/// </summary>
		public const int SpeedSteps = 64;

		/// <summary>Tiles across the precipitation atlas (the shader's <c>* 0.25</c>; WeatherTextureBaker.Columns).</summary>
		public const int AtlasColumns = 4;

		// ── Grains smaller than a pixel ─────────────────────────────────

		/// <summary>
		/// The fewest pixels across a camera-facing grain's quad is drawn over. A smaller grain is drawn
		/// over this many and made fainter by the area it gained (<see cref="Coverage"/>).
		/// </summary>
		/// <remarks>
		/// <para>
		/// A flake is millimetres across, and a pixel a few metres off is already a few millimetres of
		/// the world (a 60° view 1080 pixels high: a millimetre of it per metre away). Drawn at its own
		/// size it falls between the pixel centres and blinks in and out as it moves; drawn bigger at
		/// full strength it becomes a snowball. So it is drawn over enough pixels to be sampled
		/// steadily, and exactly as bright IN SUM as it is: its area times its opacity is kept. That
		/// is also why the far flakes go: a millimetre flake twenty metres off covers a few
		/// ten-thousandths of a pixel, and the snowfall's haze (<c>AirPhysics.PrecipitationExtinction</c>, in the fog)
		/// carries what they add up to. Nothing is counted twice: the haze is the view's loss through
		/// the snow, and the particles past a few metres are too faint to be seen on top of it.
		/// </para>
		/// <para>
		/// Three pixels holds the dot the grain becomes (<see cref="SubPixelSigma"/>) to three standard
		/// deviations either side of its centre, where a Gaussian keeps 99.5 % of itself.
		/// </para>
		/// </remarks>
		public const float MinQuadPixels = 3f;

		/// <summary>
		/// The spread of the soft dot an unresolved grain is drawn as, in pixels: a Gaussian of this
		/// standard deviation, about 1.2 pixels across at half its height.
		/// </summary>
		/// <remarks>
		/// A Gaussian sampled on a grid of pixels sums to its integral to within 2·exp(−2π²σ²) on each
		/// axis (the Poisson summation formula): 1.4 % at half a pixel, 8.5 % at 0.4 — a flake of the
		/// latter would pulse by as much on each axis as it crossed the pixel grid.
		/// </remarks>
		public const float SubPixelSigma = 0.5f;

		/// <summary>
		/// How many pixels across the grain itself (not its quad) must be before its sprite's shape is
		/// drawn: under 1.5 it is the soft dot, from 1.5 its shape blends in over three more pixels.
		/// </summary>
		public const float ResolvedFromPixels = 1.5f;

		/// <summary>
		/// The light a grain drawn over a larger quad than its own carries, against that quad drawn at
		/// full strength: its own area over the drawn one, times how many grains' light the particle
		/// carries (<see cref="Crowd"/>). Past 1 only for a crowd: the shader holds a drawn shape to one
		/// grain's opacity and lets the soft dot take the rest, up to opaque at its heart
		/// (<see cref="SubPixelAlpha"/>), so opacity times area is never more than the grains it stands for.
		/// </summary>
		/// <param name="crowd">How many grains' light it carries: 1 for one grain's (<see cref="Crowd"/>).</param>
		public static float Coverage(float ownQuad, float drawnQuad, float crowd = 1f)
		{
			if (ownQuad <= 0f || drawnQuad <= 0f)
			{
				return 0f;
			}
			float r = Mathf.Min(1f, ownQuad / drawnQuad);
			return Mathf.Max(1f, crowd) * r * r;
		}

		/// <summary>
		/// How many grains' light one particle carries when it is drawn for <paramref name="grainsPerParticle"/>
		/// of them: the square root.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why the snow vanished.</b> A heavy snowfall holds thousands of flakes in a cubic metre —
		/// its water falling through, over the mass and the speed of a flake (<see cref="SnowflakeSize.FlakesPerCubicMetre"/>):
		/// about a thousand wet 11 mm aggregates at the heaviest, three thousand 5 mm flakes, tens of
		/// thousands of grains of powder — and the field has under one particle a cubic metre (8000 in a
		/// box 24 m across). Each particle was drawn as ONE flake, its light held to one flake's, so the
		/// field drew one flake in ten thousand: a millimetre flake a few metres off is a hundredth of
		/// a pixel, and the snow was not there. Drawn with the light of all of them, a particle is a
		/// saucer — which is what the flakes were before.
		/// </para>
		/// <para>
		/// <b>What the eye reads as snowfall is the speckle.</b> The flakes' MEAN light is a veil over
		/// the view, and that is the snowfall's haze (<c>AirPhysics.PrecipitationExtinction</c>, in the
		/// fog). What makes it snow is how the light varies from one pixel to the next: flakes are
		/// scattered at random, so a pixel's light varies about its mean by the square root of how many
		/// it holds — N flakes of light a each vary by a²N. A particle drawn for n flakes and carrying
		/// the light of c of them varies by c²·N/n, which is the snowfall's own when c = √n: the field
		/// then speckles exactly as the snow would, with the grain of flakes the size they are. What it
		/// adds to the mean is only a 1/√n share of the snow's light — a fortieth to a three-hundredth,
		/// where the haze carries the whole — so nothing is counted twice to any extent that shows, and nothing is ever
		/// brighter than the snow it stands for.
		/// </para>
		/// <para>
		/// A flake near enough to be drawn at its own size stays one flake: the crowd only fills in the
		/// light of those smaller than the pixels they are drawn over (<see cref="Coverage"/>), so it
		/// never makes a saucer.
		/// </para>
		/// <para>
		/// Each of snow's shells (<see cref="SnowShells"/>) has its own n: a thousand and more flakes to a
		/// particle in the outer one, a few in the innermost, where the particles are very nearly the flakes.
		/// </para>
		/// </remarks>
		public static float Crowd(float grainsPerParticle) => Mathf.Sqrt(Mathf.Max(1f, grainsPerParticle));

		/// <summary>
		/// The opacity of an unresolved grain at a pixel: the whole of its tile's mean coverage over the
		/// quad, gathered into a Gaussian dot. The shader's twin is <c>PrecipSubPixel</c>.
		/// </summary>
		/// <param name="meanCoverage">The sprite's mean opacity over its tile (the atlas at <c>tileMip</c>).</param>
		/// <param name="quadPixels">How many pixels across the quad is drawn.</param>
		/// <param name="offsetPixels">The pixel's distance from the quad's centre, px.</param>
		/// <param name="coverage">The light it carries against its quad's at full strength (<see cref="Coverage"/>).</param>
		/// <param name="streakPixels">How far the grain moves over the eye's moment, px (<see cref="StreakSeconds"/>): the dot is drawn along that segment, the same light spread over it.</param>
		/// <remarks>
		/// <paramref name="offsetPixels"/> is the distance from the grain's path over the moment, not from
		/// its centre, for a streak: a Gaussian swept along a segment of length L integrates to
		/// 2πσ² + √(2π)·σ·L, which is what the light is divided by, so a streak holds the grain's light
		/// however long it is.
		/// </remarks>
		public static float SubPixelAlpha(float meanCoverage, float quadPixels, float offsetPixels, float coverage = 1f, float streakPixels = 0f)
		{
			float s2 = SubPixelSigma * SubPixelSigma;
			float spread = 2f * Mathf.PI * s2 + Mathf.Sqrt(2f * Mathf.PI) * SubPixelSigma * Mathf.Max(0f, streakPixels);
			return Mathf.Min(1f, coverage * meanCoverage * quadPixels * quadPixels * Mathf.Exp(-offsetPixels * offsetPixels / (2f * s2)) / spread);
		}

		// ── Near enough to count: the snow's shells ─────────────────────

		/// <summary>How many nested fields snow is drawn in, each a third the size of the one outside it.</summary>
		/// <remarks>
		/// <para>
		/// <b>Why the snow was not there.</b> A heavy snowfall holds a few hundred (wet aggregates) to tens
		/// of thousands (powder) of flakes in every cubic metre (<see cref="SnowflakeSize.FlakesPerCubicMetre"/>),
		/// and the field held 8000 particles in a box 24 m across: under one a cubic metre, and some ten of
		/// them within three metres of the eye. What the eye reads as heavy snow is the near field — dozens
		/// to hundreds of flakes within a few metres, a few pixels each and streaking as they fall — and
		/// the field had next to none there; every particle it had was ten metres off and under a pixel,
		/// carrying a crowd's light in a dot (<see cref="Crowd"/>).
		/// </para>
		/// <para>
		/// <b>Shells.</b> The same field is drawn again in a box a third the size, and again in one a third
		/// of that — 24, 8 and 2.7 m on the balanced tier — each drawn only over the distances the next one
		/// in does not reach (<see cref="ShellFades"/>). Each shell is 27 times as dense as the one outside
		/// it: under one a cubic metre past three metres, some twenty from one to three, some five hundred
		/// inside one, which is a real snowfall's own count — the near flakes are drawn one for one, at
		/// their own size, and the crowd only speckles in for the far ones. It costs twice the field again
		/// in vertices (24 000 quads on the balanced tier, a few hundred thousand vertices) and next to
		/// nothing in pixels: the near shell's flakes are a few pixels each. On a Python port of this
		/// shader over the same-storm-frozen view, two shells put some 290 particles within three metres
		/// and three put 950, against ten with one; a sprite of several flakes was the other way to
		/// fill the near field, and would draw its flakes at one depth, sliding together.
		/// </para>
		/// </remarks>
		public const int SnowShells = 3;

		/// <summary>How much smaller each snow shell's box is than the one outside it.</summary>
		public const float ShellStep = 3f;

		/// <summary>The nearest a particle is drawn, m, and the distance it fades in over past that.</summary>
		/// <remarks>
		/// Inside a quarter of a metre a flake is at the camera's near plane and would be clipped through;
		/// it was 0.3 m and a fade of 0.6, which with one field hardly mattered, and with a near shell of
		/// real flakes hid most of it.
		/// </remarks>
		public const float NearestMetres = 0.25f, NearestFade = 0.25f;

		/// <summary>
		/// The eye's moment, s: how long a moving flake's light is gathered over, and so how long its
		/// streak. A sixtieth of a second — the photopic end of the eye's integration time (Bloch's
		/// law's critical duration, some 10–100 ms, shortest in daylight), and one frame at 60 Hz.
		/// </summary>
		/// <remarks>
		/// A flake falls at about a metre a second (<see cref="SnowflakeSize.FallSpeed"/>): in a sixtieth
		/// of a second it moves 1.7 cm, which a metre off is some fifteen pixels and at three metres five.
		/// Near flakes are streaks, not dots — as they are to the eye and to any camera — and the streak
		/// holds the flake's light, not more (<see cref="SubPixelAlpha"/>).
		/// </remarks>
		public const float StreakSeconds = 1f / 60f;

		/// <summary>
		/// Where one shell of a field is drawn: x, y the distances it fades in over, z, w those it fades
		/// out over, m. The outermost fades out over 0.3–0.5 of its box, as the one field always did; each
		/// inner one fades out over the same share of its own box, and the one outside it fades in over
		/// exactly those distances; the innermost starts at <see cref="NearestMetres"/>.
		/// </summary>
		/// <param name="box">The OUTERMOST box's size across, m.</param>
		/// <param name="shell">Which shell, 0 the outermost.</param>
		/// <param name="shells">How many there are.</param>
		public static Vector4 ShellFades(float box, int shell, int shells)
		{
			shells = Mathf.Max(1, shells);
			shell = Mathf.Clamp(shell, 0, shells - 1);
			float own = ShellBox(box, shell);
			Vector2 inner = shell == shells - 1
				? new Vector2(NearestMetres, NearestMetres + NearestFade)
				: new Vector2(0.3f * ShellBox(box, shell + 1), 0.5f * ShellBox(box, shell + 1));
			return new Vector4(inner.x, inner.y, 0.3f * own, 0.5f * own);
		}

		/// <summary>The size across of one shell's box, m.</summary>
		public static float ShellBox(float box, int shell) => box / Mathf.Pow(ShellStep, Mathf.Max(0, shell));

		/// <summary>
		/// How strongly a particle at a distance is drawn in its shell (the shader's twin): in over the
		/// first fade as a sine, out over the second as a cosine.
		/// </summary>
		/// <remarks>
		/// Where two shells meet, one fades out as the other fades in, and the squares of the two add to
		/// one: sin² + cos² = 1. What the snow's speckle is, is the variance of the light, which goes as the
		/// square of each particle's (<see cref="Crowd"/>), so the snowfall is as speckled across the seam as
		/// either side of it. A straight cross-fade would leave it half as speckled at the middle — a ring
		/// of thinner snow round the camera.
		/// </remarks>
		public static float ShellWeight(float distance, Vector4 fades)
		{
			float fadeIn = Mathf.Clamp01((distance - fades.x) / Mathf.Max(1e-4f, fades.y - fades.x));
			float fadeOut = Mathf.Clamp01((distance - fades.z) / Mathf.Max(1e-4f, fades.w - fades.z));
			return Mathf.Sin(0.5f * Mathf.PI * fadeIn) * Mathf.Cos(0.5f * Mathf.PI * fadeOut);
		}

		/// <summary>
		/// The share of a shell's particles shown: the kind's own share, but never more particles to a
		/// cubic metre than the snowfall has flakes — a near shell drawn one for one in a light snow would
		/// otherwise hold more flakes than the sky let fall.
		/// </summary>
		/// <param name="amount">The kind's visible share, 0..1.</param>
		/// <param name="flakesPerCubicMetre">The snowfall's own count (<see cref="SnowflakeSize.FlakesPerCubicMetre"/>).</param>
		/// <param name="particles">The field's particles.</param>
		/// <param name="volume">The shell's box, m³.</param>
		public static float ShownShare(float amount, float flakesPerCubicMetre, int particles, float volume)
		{
			float full = Mathf.Max(1, particles) / Mathf.Max(1e-6f, volume);
			return Mathf.Clamp01(Mathf.Min(amount, Mathf.Max(0f, flakesPerCubicMetre) / full));
		}

		/// <summary>
		/// How warm the air at the ground is against what falls' freezing point, K, from the weather
		/// last presented: its column's surface temperature and its world's condensate.
		/// </summary>
		/// <remarks>
		/// With nothing presented yet, the mix of rain and snow says it: the frame is typed rain above
		/// the scale's 0.05 (+1.7 °C) and snow below −0.15 (−5 °C), linearly between
		/// (<see cref="WeatherFrame.RetypeForTemperature"/>), so a mix names its temperature; snow alone
		/// is the cold edge of that band.
		/// </remarks>
		public static float GroundWarmth(in WeatherFrame frame)
		{
			WeatherSample sample = WeatherClient.LastContext.Sample;
			if (sample.Column.SurfaceKelvin > 0f)
			{
				return sample.Column.SurfaceKelvin - AirPhysics.FreezingKelvin(sample.Planet.Condensate);
			}
			float rain = frame[WeatherChannel.RainWeight], snow = frame[WeatherChannel.SnowWeight];
			float frozen = rain + snow > 1e-4f ? snow / (rain + snow) : 1f;
			return Mathf.Lerp(0.05f, -0.15f, frozen) * (float)FishMMO.Shared.Biomes.ClimateModel.KelvinPerUnit;
		}

		private Mesh mesh;
		private int meshParticles;
		private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
		private readonly Travel[] travel = new Travel[Kinds.Length * SnowShells];
		private float lastTime = float.NaN;

		/// <summary>How far one kind's air and particles have gone, wrapped.</summary>
		public struct Travel
		{
			/// <summary>Metres the air has carried everything, east and north.</summary>
			public double X, Z;
			/// <summary>Metres a particle falling at the kind's own speed has fallen.</summary>
			public double Fall;
		}

		/// <summary>How a kind falls, from what it is. Physics rather than looks, so not on the profile.</summary>
		public struct FallTraits
		{
			/// <summary>The slowest and fastest particle in one shower, against the kind's speed.</summary>
			public Vector2 Spread;
			/// <summary>Grains fine enough that the air's viscosity holds them up as well (see <see cref="SurfacePhysics.TerminalSpeedScale"/>).</summary>
			public bool Fine;
			/// <summary>Shed by a cloud, so it falls only under one and as hard as the cloud is thick. Not sand: the wind lifts that off the ground.</summary>
			public bool FromCloud;
			/// <summary>
			/// A clear drop, seen THROUGH: a lens showing the light behind it (<see cref="ClearDropSun"/>).
			/// Everything else is an opaque grain, seen by the light on its face (<see cref="GrainSun"/>).
			/// </summary>
			public bool Clear;
		}
		private readonly List<(WeatherChannel kind, float amount)> drawn = new List<(WeatherChannel, float)>(MaxKindsAtOnce);

		/// <summary>The kinds drawn last frame and how much of each.</summary>
		public IReadOnlyList<(WeatherChannel kind, float amount)> Drawn => drawn;

		/// <summary>
		/// A field of <paramref name="particles"/> quads. Every corner of a quad shares its
		/// position in the unit box; the shader places and shapes it.
		/// </summary>
		public static Mesh BuildMesh(int particles, int seed = 1234)
		{
			particles = Mathf.Max(1, particles);
			var random = new System.Random(seed);
			var positions = new Vector3[particles * 4];
			var corners = new Vector2[particles * 4];
			var randoms = new List<Vector4>(particles * 4);
			var indices = new int[particles * 6];
			for (int i = 0; i < particles; i++)
			{
				var p = new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
				// The show threshold is an even ramp so density scales linearly with the share shown.
				var r = new Vector4((i + 0.5f) / particles, (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
				int v = i * 4;
				for (int c = 0; c < 4; c++)
				{
					positions[v + c] = p;
					randoms.Add(r);
				}
				corners[v] = new Vector2(0f, 0f);
				corners[v + 1] = new Vector2(1f, 0f);
				corners[v + 2] = new Vector2(1f, 1f);
				corners[v + 3] = new Vector2(0f, 1f);
				int t = i * 6;
				indices[t] = v;
				indices[t + 1] = v + 2;
				indices[t + 2] = v + 1;
				indices[t + 3] = v;
				indices[t + 4] = v + 3;
				indices[t + 5] = v + 2;
			}
			// Shuffle the thresholds so the visible share is spread through the box, not in index order.
			for (int i = particles - 1; i > 0; i--)
			{
				int j = random.Next(i + 1);
				float a = randoms[i * 4].x, b = randoms[j * 4].x;
				for (int c = 0; c < 4; c++)
				{
					Vector4 ri = randoms[i * 4 + c];
					Vector4 rj = randoms[j * 4 + c];
					ri.x = b;
					rj.x = a;
					randoms[i * 4 + c] = ri;
					randoms[j * 4 + c] = rj;
				}
			}
			var mesh = new Mesh
			{
				name = $"Precipitation Field ({particles})",
				indexFormat = positions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
				hideFlags = HideFlags.DontSave,
			};
			mesh.SetVertices(positions);
			mesh.SetUVs(0, corners);
			mesh.SetUVs(1, randoms);
			mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
			mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
			return mesh;
		}

		/// <summary>The kinds worth drawing, strongest first, with each one's visible share.</summary>
		public static void Choose(in WeatherFrame frame, List<(WeatherChannel kind, float amount)> into)
		{
			into.Clear();
			float p = frame[WeatherChannel.Precipitation];
			if (p <= 0.005f)
			{
				return;
			}
			foreach (WeatherChannel kind in Kinds)
			{
				float amount = p * frame[kind];
				if (amount > 0.01f)
				{
					into.Add((kind, Mathf.Clamp01(amount)));
				}
			}
			into.Sort((a, b) => b.amount.CompareTo(a.amount));
			if (into.Count > MaxKindsAtOnce)
			{
				into.RemoveRange(MaxKindsAtOnce, into.Count - MaxKindsAtOnce);
			}
		}

		/// <summary>How a kind of precipitation falls: the spread of speeds in one shower, and its drag.</summary>
		public static FallTraits TraitsOf(WeatherChannel kind)
		{
			switch (kind)
			{
				// A shower holds every size of drop at once, and a drop falls faster the bigger it
				// is, up to about nine metres a second, past which it breaks apart (Gunn and Kinzer).
				// Half the typical speed to a quarter over it tops out there in the heaviest rain.
				// A raindrop is clear. Snow is ice scattered many times over in a flake's lattice, and
				// hail is layered clear and milky ice, the milky layers opaque: both are white grains.
				// Ash and sand are grains of their own colour.
				case WeatherChannel.RainWeight: return new FallTraits { Spread = new Vector2(0.5f, 1.25f), FromCloud = true, Clear = true };
				// A bigger flake catches more air as well as weighing more, so snow falls within a
				// fifth of its speed whatever the size of the flake.
				case WeatherChannel.SnowWeight: return new FallTraits { Spread = new Vector2(0.8f, 1.2f), FromCloud = true };
				// Hail runs from peas to walnuts, and its speed goes as the root of its size.
				case WeatherChannel.HailWeight: return new FallTraits { Spread = new Vector2(0.6f, 1.4f), FromCloud = true };
				// Ash settles out of the plume overhead; sand is lifted off the ground by the wind.
				case WeatherChannel.AshWeight: return new FallTraits { Spread = new Vector2(0.5f, 1.5f), Fine = true, FromCloud = true };
				case WeatherChannel.SandWeight: return new FallTraits { Spread = new Vector2(0.5f, 1.5f), Fine = true };
				default: return new FallTraits { Spread = new Vector2(0.5f, 1.5f) };
			}
		}

		/// <summary>
		/// How much of the sun a clear drop shows, against the sun on a white card facing it: the sun's
		/// share of the light behind the drop, the cosine of its angle from the line of sight
		/// continued through the drop, and nothing when it is on the camera's side.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A raindrop is a lens, not a mirror. Refraction gathers light from about 165° of what lies
		/// behind it into the drop's disc, so a drop shows the mean of its surroundings on the far
		/// side — mostly the sky, whatever the drop is seen against — and reflection adds only a few
		/// per cent (Garg and Nayar, "Vision and Rain", IJCV 75, 2007). That wide field, weighted as a
		/// lens weights it, is near enough the scene's ambient evaluated along the line of sight (the
		/// SH is the cosine-weighted mean of the sky about a direction), and the sun is in it as a
		/// point with exactly this weight. So a streak is the sky's own grey-white, and bright only
		/// against the light.
		/// </para>
		/// <para>
		/// It was the sky straight up plus 0.35 of the main light, whatever the angle, with that light
		/// read bare — without the cloud shadow on it, which carries the storm's shade and which the
		/// sun's light has divided back out of it. Under a storm the streaks were lit by the open sun,
		/// and with the sun low that is orange.
		/// </para>
		/// </remarks>
		/// <param name="cosFromBehind">The cosine between the line of sight (camera to drop) and the way to the sun.</param>
		public static float ClearDropSun(float cosFromBehind) => Mathf.Max(0f, cosFromBehind);

		/// <summary>
		/// How much of the sun an opaque grain shows, against the sun on a white card facing it: the
		/// disc-averaged light of a Lambertian sphere at the phase angle α between the sun and the
		/// camera, (2/3)·[sin α + (π − α)·cos α]/π.
		/// </summary>
		/// <remarks>
		/// Two thirds with the sun behind the camera — the disc averages the cosine over the face it
		/// shows — and nothing with the sun behind the grain, its lit face turned away: the Lambert
		/// sphere's phase law (Russell 1916). A flake, a hailstone and a grain of sand tumble, so a
		/// sphere is their mean. The sky round them is the ambient toward the camera.
		/// </remarks>
		/// <param name="cosPhase">The cosine of the phase angle: between the way to the sun and the way to the camera, from the grain.</param>
		public static float GrainSun(float cosPhase)
		{
			float alpha = Mathf.Acos(Mathf.Clamp(cosPhase, -1f, 1f));
			return (2f / 3f) * (Mathf.Sin(alpha) + (Mathf.PI - alpha) * Mathf.Cos(alpha)) / Mathf.PI;
		}

		/// <summary>The height the weather's wind is quoted at, in metres: the standard surface wind.</summary>
		public const float WindReferenceHeight = 10f;

		/// <summary>How rough the ground is to the wind, in metres: open country.</summary>
		public const float GroundRoughness = 0.03f;

		/// <summary>The height precipitation is watched at, in metres.</summary>
		public const float EyeHeight = 2f;

		/// <summary>The wind at a height against the ten-metre wind.</summary>
		/// <remarks>
		/// Friction slows the air toward the ground along a logarithmic profile, u(z) ∝ ln(z / z0).
		/// At eye height, over open ground three centimetres rough, that is 0.72 of the ten-metre
		/// figure — most of why snow once fell far too fast: it took the full ten-metre wind, doubled
		/// again in gusts.
		/// </remarks>
		public static float WindShareAt(float heightMetres)
		{
			float z = Mathf.Max(heightMetres, GroundRoughness * 1.5f);
			return Mathf.Log(z / GroundRoughness) / Mathf.Log(WindReferenceHeight / GroundRoughness);
		}

		/// <summary>
		/// How much of the ten-metre wind a particle falling at this speed is moving with at eye height.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Everything that falls goes where the air goes.</b> A particle takes on the air's
		/// sideways motion over a time of v/g, whatever it is made of, and in that time it falls v²/g.
		/// So a flake at a metre a second moves with the air at eye height, and a raindrop at seven
		/// still carries the wind from five metres higher, which is faster.
		/// </para>
		/// <para>
		/// Each kind had a response of its own instead: rain took 0.35 of the wind and hail 0.2,
		/// which is the physics backwards. What makes snow look wind-blown and rain not is only how
		/// slowly snow comes down, and that is already in its speed.
		/// </para>
		/// </remarks>
		public static float DriftShare(float fallSpeed, float gravity)
		{
			float lag = fallSpeed * fallSpeed / Mathf.Max(0.05f, gravity);
			return WindShareAt(EyeHeight + lag);
		}

		/// <summary>How far a gust lifts the wind above its mean at full gustiness.</summary>
		/// <remarks>
		/// A peak gust over land runs about 1.4 times the mean wind. The gust here is also held by a
		/// modulation tens of seconds long, so it has to top out at a gust's peak — it was
		/// <c>1 + gust</c>, which held the whole field at double the wind for half a minute at a time.
		/// </remarks>
		public const float GustFactor = 0.4f;

		/// <summary>
		/// A kind's velocity at its typical particle: down at the speed its drag and its weight settle
		/// on for this world, and along with the air.
		/// </summary>
		/// <param name="gravity">The world's gravity, m/s² (<see cref="SurfacePhysics.Gravity"/>).</param>
		/// <param name="airDensity">Its air, kg/m³ (<see cref="SurfacePhysics.AirDensity"/>).</param>
		/// <param name="hailStoneMetres">
		/// How big the hail landing here is, m across (<see cref="WeatherPhysics.HailStoneMetres"/>), or 0 to
		/// fall at the look's own speeds. A stone's speed is its own weight against the air's drag.
		/// </param>
		/// <param name="snowflakeMillimetres">
		/// How big the typical flake falling here is, mm across (<see cref="SnowflakeSize.MeanMillimetres"/>),
		/// or 0 to fall at the look's own speeds by the drop-size channel. A flake's speed goes with its
		/// size, weakly (<see cref="SnowflakeSize.FallSpeed"/>).
		/// </param>
		public static Vector3 FallVelocity(in WeatherFrame frame, PrecipitationLook look, WeatherChannel kind, float gust,
			float gravity = SurfacePhysics.EarthGravity, float airDensity = SurfacePhysics.EarthAirDensity, float hailStoneMetres = 0f,
			float snowflakeMillimetres = 0f)
		{
			float drop = frame[WeatherChannel.DropSize];
			// The look's speeds are this world's own, and fall on another as its gravity and air say.
			float scale = SurfacePhysics.TerminalSpeedScale(gravity, airDensity, TraitsOf(kind).Fine);
			float fall = kind == WeatherChannel.HailWeight && hailStoneMetres > 0f
				? WeatherPhysics.HailFallSpeed(hailStoneMetres, gravity, airDensity)
				: kind == WeatherChannel.SnowWeight && snowflakeMillimetres > 0f
					? SnowflakeSize.FallSpeed(snowflakeMillimetres, look.FallSpeed) * scale
					: Mathf.Lerp(look.FallSpeed.x, look.FallSpeed.y, drop) * scale;
			Vector2 dir = WeatherShaderGlobals.WindDirection(frame[WeatherChannel.WindHeading]);
			float wind = frame[WeatherChannel.WindSpeed] * 30f * DriftShare(fall, gravity) * (1f + GustFactor * gust);
			return new Vector3(dir.x * wind, -fall, dir.y * wind);
		}

		/// <summary>
		/// Moves one kind on by a step at its velocity now.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Integrated, never speed × elapsed.</b> The shader placed every particle at its velocity
		/// times the seconds since the weather started, which is only where it would be if the
		/// velocity had never changed. The wind gusts all the time and the weather drifts, and the
		/// derivative of <c>v·t</c> is <c>v + t·dv/dt</c>: a particle's apparent speed grew with how
		/// long the game had been running. Ten minutes in, an ordinary gusting breeze swung snow about
		/// at hundreds of metres a second — it teleported every frame, which read as snow falling
		/// "really fast". The weather driver's drift was once broken the same way and says so.
		/// </para>
		/// <para>
		/// <b>Wrapped so it keeps its precision.</b> The shader wraps particles in a box round the
		/// camera, so the sideways travel can be taken modulo the box: every particle moves with the
		/// same air, and a whole box on is where it started. The fall cannot — each particle falls at
		/// its own speed — unless those speeds are whole numbers of 64ths of the kind's, which the
		/// shader rounds them to: then 64 boxes of the kind's fall is a whole number of boxes for
		/// every one of them.
		/// </para>
		/// </remarks>
		public static void Advance(ref Travel travel, Vector3 velocity, float seconds, Vector3 box)
		{
			travel.X = Wrap(travel.X + velocity.x * seconds, box.x);
			travel.Z = Wrap(travel.Z + velocity.z * seconds, box.z);
			travel.Fall = Wrap(travel.Fall - velocity.y * seconds, box.y * SpeedSteps);
		}

		private static double Wrap(double value, double period)
		{
			return period > 0.0 ? value - System.Math.Floor(value / period) * period : value;
		}

		/// <param name="substance">
		/// What is falling, or null for the kinds' own looks. The kind still decides how it falls;
		/// the substance decides what it is — see <see cref="PrecipitationLook.As"/>.
		/// </param>
		/// <param name="body">The world it is falling on, for its gravity and its air. Null is our own.</param>
		/// <param name="hailStoneMetres">How big the hail landing here is, m across, from the air overhead; 0 for the look's own.</param>
		/// <param name="degreesAboveFreezing">
		/// How far the air at the ground is above what falls' freezing point, K (negative below it): how
		/// sticky the snow is, and so how big its flakes (<see cref="SnowflakeSize"/>). NaN reads it from
		/// the weather last presented (<see cref="GroundWarmth"/>).
		/// </param>
		public void Render(in WeatherFrame frame, Camera camera, WeatherTierSettings tier, WeatherRenderProfile profile, float time,
			WeatherSubstance substance = null, WorldBody body = null, float hailStoneMetres = 0f, float degreesAboveFreezing = float.NaN)
		{
			// The step since the last frame, for Advance: held to a quarter of a second, so a hitch, or
			// a stopped clock starting again, does not throw everything a long way at once.
			float seconds = float.IsNaN(lastTime) ? 0f : Mathf.Clamp(time - lastTime, 0f, 0.25f);
			lastTime = time;
			Choose(frame, drawn);
			if (drawn.Count == 0 || camera == null || profile.PrecipitationMaterial == null)
			{
				return;
			}
			if (mesh == null || meshParticles != tier.Particles)
			{
				Dispose();
				mesh = BuildMesh(tier.Particles);
				meshParticles = tier.Particles;
			}

			Vector3 origin = camera.transform.position;
			float gust = frame[WeatherChannel.WindGust] * (0.5f + 0.5f * Mathf.Sin(time * 0.7f) * Mathf.Sin(time * 0.23f + 1f));
			var rp = new RenderParams(profile.PrecipitationMaterial)
			{
				camera = camera,
				matProps = block,
				worldBounds = new Bounds(origin, Vector3.one * tier.BoxSize * 2f),
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
			};
			float gravity = SurfacePhysics.Gravity(body);
			float air = SurfacePhysics.AirDensity(body);
			float warmth = float.IsNaN(degreesAboveFreezing) ? GroundWarmth(frame) : degreesAboveFreezing;
			// The mip at which one texel is a whole tile of the atlas: its mean coverage, for a grain
			// too small on screen to show its shape (see the shader's PrecipSubPixel).
			float tileMip = profile.PrecipitationAtlas != null ? Mathf.Log(Mathf.Max(1f, profile.PrecipitationAtlas.width / (float)AtlasColumns), 2f) : 0f;
			foreach ((WeatherChannel kind, float amount) in drawn)
			{
				PrecipitationLook look = profile.LookOf(kind).As(substance);
				FallTraits traits = TraitsOf(kind);
				// How much faster than on our own world it all happens here.
				float pace = SurfacePhysics.TerminalSpeedScale(gravity, air, traits.Fine);
				float drop = frame[WeatherChannel.DropSize];
				// A flake is the size the snowfall and the warmth of the air make it (SnowflakeSize), and
				// falls at the speed that size falls at.
				float flakeMillimetres = kind == WeatherChannel.SnowWeight
					? SnowflakeSize.MeanMillimetres(SnowflakeSize.RateMillimetresPerHour(frame[WeatherChannel.Precipitation], frame[WeatherChannel.SnowWeight]),
						warmth, look.Size.x * 1000f)
					: 0f;
				Vector3 fall = FallVelocity(frame, look, kind, gust, gravity, air, hailStoneMetres, flakeMillimetres);
				// Heavier rain reads as longer streaks: stretch follows the fall speed.
				float heavy = Mathf.Pow(Mathf.Clamp01(amount), 1.5f);
				float storm = Mathf.Clamp01(frame[WeatherChannel.LightningRate] * 1.5f);
				float growth = (0.85f + 2.3f * heavy) * (1f + 0.6f * storm);
				// The streak's length is size times stretch, so it grew with the drops — three times
				// as long as well as three times as wide, which was a curtain of rods. The width is
				// the drop's; the length grows only as the square root of it.
				// And a streak is the drop's motion over the eye's moment, so on a world where rain
				// falls slower it is shorter.
				float stretch = look.Stretch > 1.01f ? Mathf.Max(1.02f, look.Stretch * pace * Mathf.Lerp(0.6f, 1f, drop) / Mathf.Sqrt(growth)) : 1f;
				// Heavier weather is made of bigger drops, not just more of them: a downpour that
				// only adds particles reads as drizzle at any strength.
				// And a storm's drops are bigger again: a thunderstorm's updraught holds a drop up
				// until it is several times a shower's. The storm is read off the lightning, which is
				// the tower's, so a heavy frontal rain is heavy and a storm is heavy AND coarse.
				// A downpour's drops are several times a drizzle's, not half again: the growth is steep
				// toward the top of the range — a shower at half strength is only a little coarser,
				// heavy rain is three times the size, and a storm on top of that is nearly five.
				float size = Mathf.Lerp(look.Size.x, look.Size.y, drop) * growth;
				// How the shader spreads the sizes about that one, and how much of its quad the sprite
				// fills: ±30 % and the whole quad, unless the kind says otherwise.
				var grain = new Vector4(0f, 0f, 0f, 1f);
				float flakes = 0f;
				int shells = 1;
				float exposure = 0f;
				if (kind == WeatherChannel.HailWeight)
				{
					// A hailstone is the size its updraught grew it to, less what melted on the way
					// down — the air overhead says, not how hard it is raining. Swelling it with the
					// rain's growth drew walnut hail thirty centimetres across.
					size = hailStoneMetres > 0f ? hailStoneMetres : Mathf.Lerp(look.Size.x, look.Size.y, drop);
					stretch = 1f;
				}
				else if (kind == WeatherChannel.SnowWeight)
				{
					/* A snowflake is not a raindrop: it does not grow with the rate the way a drop does,
					 * and its size is its own physics (SnowflakeSize) — millimetres of crystal in cold
					 * air, centimetre aggregates only near melting. It took rain's growth law, 3 to 8 cm
					 * swollen up to fivefold, and a blizzard was drawn in flakes forty centimetres across.
					 * The shader draws each flake's size from the snowfall's exponential spread about
					 * this mean, between the smallest flake and the largest the warmth allows, and
					 * divides by how much of its quad the flake sprite fills, so what is drawn is the
					 * flake's size and not its quad's. */
					size = flakeMillimetres * 0.001f;
					stretch = 1f;
					float largest = SnowflakeSize.LargestMillimetres(warmth, look.Size.y * 1000f);
					grain = new Vector4(1f, look.Size.x, largest * 0.001f, SnowflakeSize.SpriteFill);
					// How many flakes a cubic metre of this snowfall holds, for each shell's crowd below.
					flakes = SnowflakeSize.FlakesPerCubicMetre(SnowflakeSize.RateMillimetresPerHour(frame[WeatherChannel.Precipitation], frame[WeatherChannel.SnowWeight]),
						flakeMillimetres, largest, look.Size.x * 1000f, look.FallSpeed, pace);
					// Drawn in nested shells, so the near field holds a snowfall's own count
					// (SnowShells), and streaked over the eye's moment (StreakSeconds).
					shells = SnowShells;
					exposure = StreakSeconds;
				}
				int slot = System.Array.IndexOf(Kinds, kind);
				for (int shell = 0; shell < shells; shell++)
				{
					// Each shell's box is a third of the one outside it, and wraps its own travel.
					float across = ShellBox(tier.BoxSize, shell);
					var shellBox = new Vector3(across, across * 0.75f, across);
					ref Travel moved = ref travel[slot * SnowShells + shell];
					Advance(ref moved, fall, seconds, shellBox);
					float shown = amount;
					float crowd = 1f;
					if (kind == WeatherChannel.SnowWeight)
					{
						// How many flakes each particle is drawn for: the snow's own number in a cubic metre
						// over the particles this shell shows there (Crowd says why this matters) — and never
						// more particles than there are flakes (ShownShare).
						float volume = shellBox.x * shellBox.y * shellBox.z;
						shown = ShownShare(amount, flakes, tier.Particles, volume);
						float particles = tier.Particles * shown / Mathf.Max(1e-6f, volume);
						crowd = Crowd(flakes / Mathf.Max(1e-6f, particles));
					}
					block.Clear();
					if (profile.PrecipitationAtlas != null)
					{
						block.SetTexture(MainTexId, profile.PrecipitationAtlas);
					}
					block.SetVector(OriginId, new Vector4(origin.x, origin.y, origin.z, time));
					block.SetVector(BoxId, new Vector4(shellBox.x, shellBox.y, shellBox.z, 0f));
					block.SetVector(ShellId, ShellFades(tier.BoxSize, shell, shells));
					block.SetVector(MotionId, new Vector4(exposure, 0f, 0f, 0f));
					block.SetVector(FallId, fall);
					block.SetVector(TravelId, new Vector4((float)moved.X, (float)moved.Fall, (float)moved.Z, 0f));
					block.SetVector(SpreadId, new Vector4(traits.Spread.x, traits.Spread.y, traits.FromCloud ? 1f : 0f, traits.Clear ? 1f : 0f));
					block.SetVector(ShapeId, new Vector4(shown, size, stretch, look.AtlasRow));
					block.SetVector(GrainId, grain);
					block.SetVector(TileId, new Vector4(tileMip, SubPixelSigma, MinQuadPixels, ResolvedFromPixels));
					block.SetVector(CrowdId, new Vector4(crowd, 0f, 0f, 0f));
					// A flake's flutter is its own wake shedding as it falls: it comes as often as the fall allows.
					block.SetVector(FlutterId, new Vector4(look.Sway, look.SwayFrequency * pace, look.Alpha, look.Brightness));
					/* What it does to the light: nothing for water, its own colour for ash and sand. The
					 * shader lights it with the scene's own sky and sun, which carry this world's hue
					 * already; rain and snow were passed through the air's hue as well (SkySystem.InAir),
					 * which coloured them twice — brown under a warm sky, as hail once was. */
					block.SetColor(ColorId, look.Tint);
					Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
				}
			}
		}

		public void Dispose()
		{
			if (mesh != null)
			{
				if (Application.isPlaying)
				{
					Object.Destroy(mesh);
				}
				else
				{
					Object.DestroyImmediate(mesh);
				}
				mesh = null;
			}
		}
	}

	/// <summary>
	/// How big snowflakes are, from how hard it is snowing and how warm the air is: a pure model, in
	/// millimetres, of what reaches the ground.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What is falling.</b> A snowfall's flakes spread exponentially in size, N(D) = N₀·exp(−ΛD),
	/// many small and a few large (Gunn and Marshall 1958; near-exponential at the ground by video
	/// disdrometer too, Brandes et al. 2007, J. Appl. Meteor. Climatol. 46). Heavier snow is made of
	/// bigger flakes: melted to drops, Λ = 2.29·R^−0.45 mm⁻¹ for R mm of water an hour (Sekhon and
	/// Srivastava 1970, J. Atmos. Sci. 27, 299: 22.9·R^−0.45 cm⁻¹).
	/// </para>
	/// <para>
	/// <b>How big that water is as a flake</b> is how it has stuck together. An aggregate of dendrites
	/// weighs m = 0.073·D^1.4 mg for D mm across (Locatelli and Hobbs 1974, J. Geophys. Res. 79, 2185,
	/// fitted over 2–10 mm), so a millimetre drop's worth of ice is a flake four millimetres across,
	/// and averaging that over the melted spread gives <see cref="AggregateMeanMillimetres"/>.
	/// Crystals only stick when they are warm: above −5 °C aggregates are more likely than not
	/// (Hobbs, Chang and Locatelli 1974, J. Geophys. Res. 79, 2199), their surfaces wet and sticky
	/// toward 0 °C, which is where the big wet flakes of a few centimetres come from; in cold air snow
	/// falls as the single crystals it grew as, whose spread narrows as the air cools: Λ = 0.96·exp(−0.056·T)
	/// mm⁻¹ at T °C (Houze, Hobbs, Herzegh and Parsons 1979, J. Atmos. Sci. 36, 156, frontal clouds,
	/// −42 to +6 °C).
	/// </para>
	/// <para>
	/// <b>What it replaced.</b> The flakes took rain's law: the look's 3–8 cm, swollen up to fivefold
	/// with the rate and a storm. A blizzard was drawn in flakes forty centimetres across; the largest
	/// ever measured by an instrument are about five.
	/// </para>
	/// <para>
	/// Another world's snow is taken to stick the same way at the same distance below its own freezing
	/// point, which is an assumption: nobody has watched nitrogen snow aggregate.
	/// </para>
	/// </remarks>
	public static class SnowflakeSize
	{
		/// <summary>
		/// The precipitation channel's full scale, mm of water an hour. It goes as the square of the
		/// channel: <c>AirPhysics.PrecipitationExtinction</c> reads it the same way.
		/// </summary>
		public const float FullScaleMillimetresPerHour = 50f;

		/// <summary>
		/// The heaviest snowfall the sizes are worked out for, mm of water an hour. Past it the snow
		/// is no heavier to the flakes.
		/// </summary>
		/// <remarks>
		/// The fastest snowfalls measured are a foot of snow in an hour (Copenhagen, New York, 1966) —
		/// fluffy lake-effect snow, a centimetre or so of water — and 3.5 inches an hour for nineteen
		/// hours at Bessans in 1959, some 9 mm of water an hour at ordinary densities. The channel runs
		/// on to rain's 50 mm an hour, where the exponential law would draw flakes half a metre across.
		/// </remarks>
		public const float HeaviestMillimetresPerHour = 10f;

		/// <summary>The melted spread's slope in a snowfall of 1 mm/h, mm⁻¹ (Sekhon and Srivastava 1970).</summary>
		public const float MeltedSlope = 2.29f;
		/// <summary>How the melted slope falls as the snowfall rises: Λ ∝ R^this.</summary>
		public const float MeltedSlopeExponent = -0.45f;

		/// <summary>An aggregate of dendrites: m = this·D^<see cref="AggregateMassExponent"/>, mg for D mm (Locatelli and Hobbs 1974).</summary>
		public const float AggregateMassCoefficient = 0.073f;
		/// <summary>See <see cref="AggregateMassCoefficient"/>.</summary>
		public const float AggregateMassExponent = 1.4f;

		/// <summary>The single crystals' slope at 0 °C, mm⁻¹ (Houze et al. 1979).</summary>
		public const float CrystalSlopeAtFreezing = 0.96f;
		/// <summary>How much steeper the crystals' spread gets per kelvin colder: Λ = 0.96·exp(−0.056·T).</summary>
		public const float CrystalSlopePerKelvin = 0.056f;

		/// <summary>
		/// The smallest flake drawn, mm: what the distributions are measured, and seen, above.
		/// </summary>
		/// <remarks>
		/// The exponential runs down to nothing, but a snowfall at the ground is counted from about
		/// 0.4 mm (the video disdrometer's floor, Brandes et al. 2007); smaller ice is the snowfall's
		/// haze and not flakes. The snow look's smallest size is this.
		/// </remarks>
		public const float SmallestMillimetres = 0.4f;

		/// <summary>
		/// The largest single crystal, mm: a big fern-like stellar dendrite. Snow crystals are a few
		/// millimetres across; the largest single one ever photographed was 10.1 mm tip to tip
		/// (K. G. Libbrecht, as shown in UBC's ATSC 113 notes on snow crystal habits).
		/// </summary>
		public const float LargestCrystalMillimetres = 10f;

		/// <summary>
		/// The largest aggregate, mm: the flakes of 4–5 cm instruments record in wet snow at 0 °C.
		/// Older eyewitness giants (38 cm, Fort Keogh 1887) are unmeasured. The snow look's largest size is this.
		/// </summary>
		public const float LargestAggregateMillimetres = 50f;

		/// <summary>Where aggregates start to form, °C below freezing, and where half the snow is aggregated (Hobbs et al. 1974's −5 °C).</summary>
		public const float AggregationStarts = -10f;

		/// <summary>
		/// How much of its quad the flake sprite fills, across: the atlas's flakes
		/// (WeatherTextureBaker.Flake) reach 0.30–0.42 of a tile from its centre, 0.72 of it across on
		/// average, and their soft halo fades out by 0.41. Measured on the baked atlas, the arms reach
		/// 0.32–0.38 (opacity over 0.2), 0.65–0.77 across.
		/// </summary>
		public const float SpriteFill = 0.72f;

		/// <summary>The snowfall, mm of water an hour, from the precipitation channel and snow's share of it.</summary>
		public static float RateMillimetresPerHour(float precipitation01, float snowShare)
		{
			float p = Mathf.Clamp01(precipitation01);
			return FullScaleMillimetresPerHour * p * p * Mathf.Clamp01(snowShare);
		}

		/// <summary>
		/// How much of the snow is aggregated at a temperature, 0..1: none at −10 °C, half at −5,
		/// all at freezing and above.
		/// </summary>
		/// <remarks>
		/// Hobbs et al. (1974) found aggregates more likely than not above −5 °C, and rare in colder air
		/// outside the dendrite band near −15 °C, whose interlocking arms make a second, smaller peak
		/// that the ground temperature cannot see (those flakes grew aloft). The rise is smooth about
		/// −5 °C: HLSL's smoothstep(−10, 0, T).
		/// </remarks>
		public static float Aggregated(float degreesAboveFreezing)
		{
			return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(AggregationStarts, 0f, degreesAboveFreezing));
		}

		/// <summary>
		/// The mean size of single crystals at a temperature, mm: the smallest flake plus the mean of
		/// Houze's exponential above it.
		/// </summary>
		public static float CrystalMeanMillimetres(float degreesAboveFreezing, float smallestMillimetres = SmallestMillimetres)
		{
			float t = Mathf.Min(0f, degreesAboveFreezing);
			return Mathf.Max(0f, smallestMillimetres) + Mathf.Exp(CrystalSlopePerKelvin * t) / CrystalSlopeAtFreezing;
		}

		/// <summary>
		/// The mean size of the aggregates in a snowfall, mm: each melted drop's ice as a flake of
		/// dendrites, averaged over Sekhon and Srivastava's melted spread.
		/// </summary>
		/// <remarks>
		/// A drop Dₘ across weighs (π/6)·ρw·Dₘ³, and a flake of that mass is D = c·Dₘ^(3/b) across with
		/// c = (π·ρw / 6a)^(1/b); over an exponential in Dₘ of slope Λ its mean is c·Γ(1 + 3/b)·Λ^(−3/b).
		/// With a and b of dendrite aggregates that is about 1.59·R^0.96 mm: a millimetre and a half in a
		/// snowfall of a millimetre an hour, nearly fifteen at the heaviest.
		/// </remarks>
		public static float AggregateMeanMillimetres(float rateMillimetresPerHour)
		{
			float rate = Mathf.Clamp(rateMillimetresPerHour, 1e-3f, HeaviestMillimetresPerHour);
			float slope = MeltedSlope * Mathf.Pow(rate, MeltedSlopeExponent);
			return AggregatePrefactor * Mathf.Pow(slope, -3f / AggregateMassExponent);
		}

		/// <summary>c·Γ(1 + 3/b) of <see cref="AggregateMeanMillimetres"/>, with water at 1 mg/mm³.</summary>
		private static readonly float AggregatePrefactor =
			(float)(System.Math.Pow(System.Math.PI / 6.0 / AggregateMassCoefficient, 1.0 / AggregateMassExponent) * Gamma(1.0 + 3.0 / AggregateMassExponent));

		/// <summary>
		/// The mean flake in a snowfall, mm: single crystals in cold air, and as the air warms toward
		/// freezing more of them stuck together into the aggregates the snowfall's rate would make.
		/// </summary>
		/// <param name="rateMillimetresPerHour">The snowfall, mm of water an hour (<see cref="RateMillimetresPerHour"/>).</param>
		/// <param name="degreesAboveFreezing">The air at the ground against the snow's freezing point, K.</param>
		/// <param name="smallestMillimetres">The smallest flake counted (the snow look's smallest size).</param>
		public static float MeanMillimetres(float rateMillimetresPerHour, float degreesAboveFreezing, float smallestMillimetres = SmallestMillimetres)
		{
			float crystal = CrystalMeanMillimetres(degreesAboveFreezing, smallestMillimetres);
			// An aggregate is never smaller than the crystals it is made of: in the lightest snow the
			// rate's aggregates would be, and the crystals are what falls.
			float aggregate = Mathf.Max(crystal, AggregateMeanMillimetres(rateMillimetresPerHour));
			return Mathf.Lerp(crystal, aggregate, Aggregated(degreesAboveFreezing));
		}

		/// <summary>
		/// The largest flake at a temperature, mm: a big crystal in cold air, a big wet aggregate at freezing.
		/// </summary>
		public static float LargestMillimetres(float degreesAboveFreezing, float largestAggregateMillimetres = LargestAggregateMillimetres)
		{
			return Mathf.Lerp(LargestCrystalMillimetres, Mathf.Max(LargestCrystalMillimetres, largestAggregateMillimetres), Aggregated(degreesAboveFreezing));
		}

		/// <summary>
		/// One flake drawn from the snowfall, mm: exponential about the mean above the smallest, held
		/// at the largest. The shader's twin draws it from the particle's own random.
		/// </summary>
		/// <param name="uniform">A random number in [0, 1).</param>
		public static float Draw(float meanMillimetres, float largestMillimetres, float uniform, float smallestMillimetres = SmallestMillimetres)
		{
			float e = -Mathf.Log(Mathf.Max(1f - uniform, 1e-4f));
			float smallest = Mathf.Max(0f, smallestMillimetres);
			return Mathf.Min(largestMillimetres, smallest + Mathf.Max(0f, meanMillimetres - smallest) * e);
		}

		/// <summary>
		/// How many flakes a cubic metre of this snowfall holds: its water coming down, over what each
		/// flake weighs times how fast it falls, averaged over the snowfall's spread of sizes.
		/// </summary>
		/// <remarks>
		/// Water falls through a square metre at R/3600 kg a second for R mm an hour, and it is carried
		/// by the flakes in the air above at their own speeds, so N·E[m·v] = R/3600. A flake's mass is
		/// the dendrite aggregates' (Locatelli and Hobbs 1974, m = 0.073·D^1.4 mg), which at a
		/// millimetre or two is also the mass of their cold-air aggregates of plates and columns
		/// (0.037·D^1.9): the one law serves crystals as well, to within the scatter of either fit. The
		/// rate stops at the heaviest snowfall measured, as the sizes do: past it the snow is no heavier
		/// to the flakes, in number or in size. For scale, Sekhon and Srivastava's own spread holds
		/// N₀/Λ = 1.09·10³·R^−0.49 flakes a cubic metre — about a thousand at 1 mm/h, 350 at 10 — of
		/// aggregates; cold snow of single crystals, much lighter each, is several times as many.
		/// </remarks>
		/// <param name="speeds">The snow look's FallSpeed (<see cref="FallSpeed"/>).</param>
		/// <param name="speedScale">How much faster than on our own world things fall here (SurfacePhysics.TerminalSpeedScale).</param>
		public static float FlakesPerCubicMetre(float rateMillimetresPerHour, float meanMillimetres, float largestMillimetres,
			float smallestMillimetres, Vector2 speeds, float speedScale = 1f)
		{
			if (rateMillimetresPerHour <= 0f)
			{
				return 0f;
			}
			float rate = Mathf.Min(rateMillimetresPerHour, HeaviestMillimetresPerHour);
			const int steps = 32;
			double massFlux = 0.0;
			for (int i = 0; i < steps; i++)
			{
				float d = Draw(meanMillimetres, largestMillimetres, (i + 0.5f) / steps, smallestMillimetres);
				double kilograms = AggregateMassCoefficient * System.Math.Pow(d, AggregateMassExponent) * 1e-6;
				massFlux += kilograms * FallSpeed(d, speeds) * Mathf.Max(0.01f, speedScale);
			}
			massFlux /= steps;
			return (float)(rate / 3600.0 / System.Math.Max(1e-15, massFlux));
		}

		/// <summary>
		/// How fast a flake this size falls on our own world, m/s: V = 0.8·D^0.16 for D mm (Locatelli and
		/// Hobbs 1974, aggregates of dendrites), from the look's speed at a millimetre up to its fastest.
		/// </summary>
		/// <remarks>
		/// A bigger flake catches more air as it gains weight, so its speed barely follows its size: a
		/// millimetre crystal and a three-centimetre aggregate fall at 0.8 and 1.4 m/s, which is the
		/// snow look's range. It was that range spread by the drop-size channel, which the driver sets
		/// from how unstable the air is: a flake's speed followed the convection and not the flake.
		/// </remarks>
		/// <param name="speeds">The look's FallSpeed: x at a 1 mm flake (Locatelli and Hobbs' 0.8), y the fastest.</param>
		public static float FallSpeed(float millimetres, Vector2 speeds)
		{
			float v = speeds.x * Mathf.Pow(Mathf.Max(SmallestMillimetres, millimetres), 0.16f);
			return Mathf.Min(v, Mathf.Max(speeds.x, speeds.y));
		}

		/// <summary>Γ(x) for x &gt; 0.5 (Lanczos, g = 7, nine terms).</summary>
		public static double Gamma(double x)
		{
			if (x < 0.5)
			{
				return System.Math.PI / (System.Math.Sin(System.Math.PI * x) * Gamma(1.0 - x));
			}
			x -= 1.0;
			double a = 0.99999999999980993;
			double t = x + 7.5;
			double[] g =
			{
				676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
				12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
			};
			for (int i = 0; i < g.Length; i++)
			{
				a += g[i] / (x + i + 1.0);
			}
			return System.Math.Sqrt(2.0 * System.Math.PI) * System.Math.Pow(t, x + 0.5) * System.Math.Exp(-t) * a;
		}
	}
}
