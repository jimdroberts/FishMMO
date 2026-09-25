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

		private Mesh mesh;
		private int meshParticles;
		private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
		private readonly Travel[] travel = new Travel[Kinds.Length];
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
				case WeatherChannel.RainWeight: return new FallTraits { Spread = new Vector2(0.5f, 1.25f), FromCloud = true };
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
		public static Vector3 FallVelocity(in WeatherFrame frame, PrecipitationLook look, WeatherChannel kind, float gust,
			float gravity = SurfacePhysics.EarthGravity, float airDensity = SurfacePhysics.EarthAirDensity)
		{
			float drop = frame[WeatherChannel.DropSize];
			// The look's speeds are this world's own, and fall on another as its gravity and air say.
			float fall = Mathf.Lerp(look.FallSpeed.x, look.FallSpeed.y, drop) * SurfacePhysics.TerminalSpeedScale(gravity, airDensity, TraitsOf(kind).Fine);
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
		public void Render(in WeatherFrame frame, Camera camera, WeatherTierSettings tier, WeatherRenderProfile profile, float time,
			WeatherSubstance substance = null, WorldBody body = null)
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
			var box = new Vector3(tier.BoxSize, tier.BoxSize * 0.75f, tier.BoxSize);
			foreach ((WeatherChannel kind, float amount) in drawn)
			{
				PrecipitationLook look = profile.LookOf(kind).As(substance);
				FallTraits traits = TraitsOf(kind);
				// How much faster than on our own world it all happens here.
				float pace = SurfacePhysics.TerminalSpeedScale(gravity, air, traits.Fine);
				float drop = frame[WeatherChannel.DropSize];
				Vector3 fall = FallVelocity(frame, look, kind, gust, gravity, air);
				int slot = System.Array.IndexOf(Kinds, kind);
				Advance(ref travel[slot], fall, seconds, box);
				block.Clear();
				if (profile.PrecipitationAtlas != null)
				{
					block.SetTexture(MainTexId, profile.PrecipitationAtlas);
				}
				block.SetVector(OriginId, new Vector4(origin.x, origin.y, origin.z, time));
				block.SetVector(BoxId, new Vector4(box.x, box.y, box.z, 0.6f));
				block.SetVector(FallId, fall);
				block.SetVector(TravelId, new Vector4((float)travel[slot].X, (float)travel[slot].Fall, (float)travel[slot].Z, 0f));
				block.SetVector(SpreadId, new Vector4(traits.Spread.x, traits.Spread.y, traits.FromCloud ? 1f : 0f, 0f));
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
				block.SetVector(ShapeId, new Vector4(amount, size, stretch, look.AtlasRow));
				// A flake's flutter is its own wake shedding as it falls: it comes as often as the fall allows.
				block.SetVector(FlutterId, new Vector4(look.Sway, look.SwayFrequency * pace, look.Alpha, look.Brightness));
				// Rain, snow and hail are lit by this world's sky; ash and sand are their own colour.
				bool water = kind == WeatherChannel.RainWeight || kind == WeatherChannel.SnowWeight || kind == WeatherChannel.HailWeight;
				block.SetColor(ColorId, water && SkySystem.Instance != null ? SkySystem.Instance.InAir(look.Tint) : look.Tint);
				Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
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
}
