using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Water
{
	/// <summary>
	/// What makes a floating body move as it does: its mass, the water it displaces, its waterplane
	/// and where its centres of mass and buoyancy sit. Measured from the mesh by whatever made it.
	/// </summary>
	/// <remarks>
	/// Heights are metres above the body's pivot, which is its still waterline; the axes are the
	/// body's own: x along its length (pitch turns about z), z across it (roll turns about x).
	/// </remarks>
	[Serializable]
	public struct FloatingBody
	{
		[Tooltip("Mass, kg.")]
		public float Mass;
		[Tooltip("Water displaced at rest, m³ (mass over the water's density when it floats freely).")]
		public float DisplacedVolume;
		[Tooltip("Area of the body's section at its waterline, m².")]
		public float WaterplaneArea;
		[Tooltip("Second moment of the waterplane about the long (x) axis, m⁴: resists roll.")]
		public float WaterplaneInertiaRoll;
		[Tooltip("Second moment of the waterplane about the short (z) axis, m⁴: resists pitch.")]
		public float WaterplaneInertiaPitch;
		[Tooltip("Height of the centre of buoyancy above the waterline, m (negative: it is below).")]
		public float BuoyancyHeight;
		[Tooltip("Height of the centre of mass above the waterline, m.")]
		public float GravityHeight;
		[Tooltip("Radius of gyration about the long (x) axis through the centre of mass, m.")]
		public float GyrationRoll;
		[Tooltip("Radius of gyration about the short (z) axis through the centre of mass, m.")]
		public float GyrationPitch;
		[Tooltip("Half the waterplane's length and width, m: where the sea is sampled.")]
		public float HalfLength, HalfWidth;

		/// <summary>True when every number a period needs is there.</summary>
		public bool IsValid => Mass > 0f && DisplacedVolume > 0f && WaterplaneArea > 0f;

		/// <summary>Displaced volume over waterplane area: the draught of the equivalent wall-sided body, m.</summary>
		public float MeanDraught => WaterplaneArea > 0f ? DisplacedVolume / WaterplaneArea : 0f;

		/// <summary>Metacentric height in roll (about x), m. Positive is stable.</summary>
		public float MetacentricHeightRoll => WaterFloater.MetacentricHeight(DisplacedVolume, WaterplaneInertiaRoll, BuoyancyHeight, GravityHeight);

		/// <summary>Metacentric height in pitch (about z), m.</summary>
		public float MetacentricHeightPitch => WaterFloater.MetacentricHeight(DisplacedVolume, WaterplaneInertiaPitch, BuoyancyHeight, GravityHeight);

		/// <summary>Undamped natural period of heave, seconds.</summary>
		public float HeavePeriod(float gravity) =>
			WaterFloater.NaturalPeriod(WaterFloater.HeaveNaturalFrequency(Mass, WaterplaneArea, WaterFloater.SeaWaterDensity, gravity));

		/// <summary>Undamped natural period of roll, seconds.</summary>
		public float RollPeriod(float gravity) =>
			WaterFloater.NaturalPeriod(WaterFloater.TiltNaturalFrequency(Mass, GyrationRoll, DisplacedVolume, MetacentricHeightRoll,
				WaterplaneArea, WaterFloater.SeaWaterDensity, gravity));

		/// <summary>Undamped natural period of pitch, seconds.</summary>
		public float PitchPeriod(float gravity) =>
			WaterFloater.NaturalPeriod(WaterFloater.TiltNaturalFrequency(Mass, GyrationPitch, DisplacedVolume, MetacentricHeightPitch,
				WaterplaneArea, WaterFloater.SeaWaterDensity, gravity));

		/// <summary>
		/// A rough body from a mesh's bounds alone, for something placed by hand with no measured numbers:
		/// an elliptical cylinder floating on its pivot at the given bulk density.
		/// </summary>
		public static FloatingBody Estimate(Bounds localBounds, float density)
		{
			Vector3 size = localBounds.size;
			float length = Mathf.Max(0.05f, size.x), width = Mathf.Max(0.05f, size.z);
			float draught = Mathf.Max(0.02f, -localBounds.min.y);
			float freeboard = Mathf.Max(0f, localBounds.max.y);
			float area = Mathf.PI * 0.25f * length * width;
			// A body that tapers to its keel displaces about two thirds of its waterplane prism.
			float displaced = area * draught * 0.66f;
			float mass = WaterFloater.SeaWaterDensity * displaced;
			float volume = mass / Mathf.Max(1f, density);
			float above = Mathf.Max(0f, volume - displaced);
			float zB = -0.4f * draught;
			float zG = volume > 0f ? (displaced * zB + above * 0.35f * freeboard) / volume : zB;
			float height = draught + freeboard;
			return new FloatingBody
			{
				Mass = mass,
				DisplacedVolume = displaced,
				WaterplaneArea = area,
				WaterplaneInertiaRoll = Mathf.PI / 64f * length * width * width * width,
				WaterplaneInertiaPitch = Mathf.PI / 64f * width * length * length * length,
				BuoyancyHeight = zB,
				GravityHeight = zG,
				GyrationRoll = Mathf.Sqrt((width * width + height * height) / 12f),
				GyrationPitch = Mathf.Sqrt((length * length + height * height) / 12f),
				HalfLength = length * 0.5f * 0.87f,
				HalfWidth = width * 0.5f * 0.87f,
			};
		}
	}

	/// <summary>
	/// Makes a placed iceberg, floe or anything else afloat ride the sea: heave, pitch and roll from
	/// the drawn surface under its footprint, with the response a body of its size and mass has.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A damped oscillator per motion, driven by the sea under it.</b> The surface is sampled at
	/// three points spread over the waterplane (<see cref="WaterSurface.HeightAt"/>, the sea as it is
	/// drawn) and a plane is fitted to them: its mean height drives heave, its two slopes pitch and
	/// roll. Each motion then follows m·ẍ = C·(η − x) + B·(η̇ − ẋ): hydrostatic stiffness C pulling
	/// it toward the water, and damping B against the water's own motion, not against still water —
	/// so a heavily damped body (a flat floe) follows the surface rather than lagging it, which is
	/// what the Haskind relation says a body small against the waves does.
	/// </para>
	/// <para>
	/// <b>The numbers are the body's.</b> Heave stiffness is ρgA_wp and its mass the body's own plus
	/// the added mass of the water it must move, (4/3)ρa³ for a waterplane of equivalent radius a (half
	/// a deeply submerged disc's); so the natural period is 2π√((m + a₃₃)/(ρgA_wp)). Pitch and roll
	/// stiffness is ρgV·GM, GM = BM − BG with BM = I_wp/V, against the body's own moment of inertia
	/// plus (8/45)ρa⁵. Radiation damping comes from the same small-body estimate (Newman's Haskind
	/// relation with Froude–Krylov excitation): ζ₃₃ = ρω²A²e^{−2kd}/(4g·m_eff), and a small viscous
	/// share on top. The sea's push is attenuated by e^{−kd}, d the mean draught and k the peak
	/// wave number — the pressure of a wave dies away with depth, so a berg with a 100 m keel feels
	/// almost nothing of a six-second sea. The result: a growler (natural period ~3 s) bobs with every
	/// wave, a medium berg (~15–20 s) barely stirs, and a floe, damped many times over, lies on the
	/// water like a raft.
	/// </para>
	/// <para>
	/// <b>On the sea's clock.</b> Time is the sea's own <see cref="WaterSurface.Clock"/>, so a world
	/// paused or slowed by <c>WorldMotion</c> pauses and slows the berg with its waves. The
	/// integration is implicit (backward Euler in the stiff terms) and sub-stepped to ω·h ≤ 0.2, so
	/// a hitch cannot blow it up.
	/// </para>
	/// <para>
	/// <b>Cheap enough for dozens.</b> A height query is a spectrum sum (tens of microseconds), so
	/// queries are rationed: each body asks only as often as its fastest motion needs (an eighth of
	/// its shortest natural or wave period, between 1/30 s and 0.75 s), never while none of its
	/// renderers is visible, and all floaters together share <see cref="QueryBudgetPerFrame"/>,
	/// served round-robin. Between samples the target is carried forward on its last rate of change.
	/// One pump per frame drives every floater.
	/// </para>
	/// <para>
	/// <b>What it does not model.</b> No drift: wind, current and wave drift would move a berg, and a
	/// moved berg would need its position shared with every client — this is presentation only, on
	/// each client, with no networking, so the berg stays where the scene put it. No capsizing: the
	/// motion is linear about upright, and a body measured unstable (GM ≤ 0) is given a small positive
	/// stiffness rather than rolled over. No grounding: a berg whose keel is in the sea floor should
	/// have <see cref="Response"/> at 0. No yaw, no surge or sway, no wave reflection, no slamming,
	/// and no melting or calving. On lava it does nothing at all: a lava surface has no waves.
	/// </para>
	/// <para>
	/// <b>Colliders stay put.</b> Moving a collider every frame is expensive and, on a client, would
	/// disagree with the server's still one. Put the renderers on a child and point
	/// <see cref="Target"/> at it; the collider on this object stays where the scene put it.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	[AddComponentMenu("FishMMO/Water/Water Floater")]
	public sealed class WaterFloater : MonoBehaviour
	{
		/// <summary>Sea water, kg/m³.</summary>
		public const float SeaWaterDensity = 1025f;
		/// <summary>Bulk glacial ice with its bubbles, kg/m³: what an estimated body assumes.</summary>
		public const float GlacialIceDensity = 900f;

		/// <summary>Height queries all floaters together may make in one frame.</summary>
		public static int QueryBudgetPerFrame = 24;

		/// <summary>Points the sea is sampled at per update.</summary>
		public const int SamplePoints = 3;

		[Tooltip("The sea this rides. Found in the scene when left empty.")]
		public WaterSurface Surface;
		[Tooltip("What moves: a child holding the renderers, so the collider here stays still. Empty moves this object.")]
		public Transform Target;
		[Tooltip("Measured by whatever made the mesh. Left empty, it is estimated from the mesh's bounds.")]
		public FloatingBody Body;
		[Tooltip("Damping ratio from viscosity and eddies, on top of the radiation damping worked out from the body.")]
		[Range(0f, 0.5f)] public float ViscousDamping = 0.05f;
		[Tooltip("Scales the motion: 1 free, 0 still (a berg aground).")]
		[Range(0f, 1f)] public float Response = 1f;

		private static readonly List<WaterFloater> active = new List<WaterFloater>();
		private static int pumpedFrame = -1;
		private static int cursor;
		private static WaterSurface foundSurface;
		private static float nextSearch;

		// Where the scene put it.
		private Vector3 restPosition;
		private Quaternion restRotation;
		private Vector3 targetRestPosition;
		private Quaternion targetRestRotation;
		private float restOffset;
		private bool hasRestOffset;

		// The motion: heave in metres from the still water, pitch and roll in radians.
		private float heave, heaveRate, pitch, pitchRate, roll, rollRate;

		// The sea under it: the last two fitted planes (x mean height over still water, y slope
		// along length, z slope across), and when they were taken on the sea's clock.
		private Vector3 sample, previousSample;
		private float sampleTime, previousSampleTime;
		private int samples;
		private float seaTime;
		private double lastClock;
		private bool hasClock;
		private float stepSeconds;

		// The body's response, refreshed with every sample (the sea state moves slowly).
		private float heaveOmega, heaveZeta, pitchOmega, pitchZeta, rollOmega, rollZeta;
		private float attenuation, interval;
		private bool coefficientsReady;

		private Renderer[] renderers;
		private WaterSurface sea;
		private WaterEnvironment environment;
		private bool moved;

		// Sample offsets in the body's frame (x along, z across): an equilateral spread whose second
		// moments match the waterplane's.
		private readonly float[] sampleX = new float[SamplePoints];
		private readonly float[] sampleZ = new float[SamplePoints];
		private readonly float[] sampleHeight = new float[SamplePoints];

		private void Awake()
		{
#if UNITY_SERVER
			// Presentation only: a dedicated server has no sea to draw and nobody to show it to.
			enabled = false;
#endif
		}

		private void OnEnable()
		{
			if (Target == null)
			{
				Target = transform;
			}
			restPosition = transform.position;
			restRotation = transform.rotation;
			targetRestPosition = Target.position;
			targetRestRotation = Target.rotation;
			renderers = Target.GetComponentsInChildren<Renderer>();
			if (!Body.IsValid)
			{
				Body = FloatingBody.Estimate(LocalBounds(), GlacialIceDensity);
			}
			heave = heaveRate = pitch = pitchRate = roll = rollRate = 0f;
			samples = 0;
			hasClock = false;
			hasRestOffset = false;
			coefficientsReady = false;
			moved = false;
			active.Add(this);
		}

		private void OnDisable()
		{
			active.Remove(this);
			if (moved && Target != null)
			{
				Target.SetPositionAndRotation(targetRestPosition, targetRestRotation);
				moved = false;
			}
		}

		private void LateUpdate()
		{
			if (Time.frameCount != pumpedFrame)
			{
				pumpedFrame = Time.frameCount;
				Pump();
			}
		}

		/// <summary>One frame for every floater: clocks, then rationed sampling, then the motion.</summary>
		private static void Pump()
		{
			int count = active.Count;
			for (int i = 0; i < count; i++)
			{
				active[i].Tick();
			}
			if (count == 0)
			{
				return;
			}
			int budget = QueryBudgetPerFrame;
			cursor %= count;
			int next = cursor;
			for (int k = 0; k < count && budget >= SamplePoints; k++)
			{
				int index = (cursor + k) % count;
				WaterFloater floater = active[index];
				if (floater.Due())
				{
					floater.Sample();
					budget -= SamplePoints;
					next = (index + 1) % count;
				}
			}
			// The next frame starts after the last one served, so a tight budget is shared out fairly.
			cursor = next;
			for (int i = 0; i < count; i++)
			{
				active[i].Move();
			}
		}

		private WaterSurface ResolveSea()
		{
			if (Surface != null)
			{
				return Surface;
			}
			if (foundSurface == null && Time.unscaledTime >= nextSearch)
			{
				nextSearch = Time.unscaledTime + 1f;
				foundSurface = FindAnyObjectByType<WaterSurface>();
			}
			return foundSurface;
		}

		/// <summary>Advances this floater's view of the sea's clock.</summary>
		private void Tick()
		{
			stepSeconds = 0f;
			WaterSurface current = ResolveSea();
			if (current != sea)
			{
				sea = current;
				environment = sea != null ? sea.GetComponent<WaterEnvironment>() : null;
				hasClock = false;
				coefficientsReady = false;
			}
			if (sea == null || sea.IsLava)
			{
				return;
			}
			if (!hasRestOffset)
			{
				// Placed against mean sea level; it then rides the tide with the water.
				restOffset = restPosition.y - sea.MeanSeaLevel;
				hasRestOffset = true;
			}
			double clock = sea.Clock;
			if (hasClock)
			{
				double delta = clock - lastClock;
				// The clock wraps every few hours and can be set: neither is motion.
				stepSeconds = delta > 0.0 && delta < 1.0 ? (float)delta : 0f;
			}
			lastClock = clock;
			hasClock = true;
			seaTime += stepSeconds;
		}

		private bool Due()
		{
			if (sea == null || sea.IsLava || Response <= 0f)
			{
				return false;
			}
			if (samples > 0 && seaTime - sampleTime < interval)
			{
				return false;
			}
			if (samples > 0 && !AnyVisible())
			{
				return false;
			}
			return true;
		}

		private bool AnyVisible()
		{
			if (renderers == null || renderers.Length == 0)
			{
				return true;
			}
			foreach (Renderer r in renderers)
			{
				if (r != null && r.isVisible)
				{
					return true;
				}
			}
			return false;
		}

		private void Sample()
		{
			if (!coefficientsReady || samples % 16 == 0)
			{
				RefreshCoefficients();
			}
			Vector3 along = Flat(restRotation * Vector3.right, Vector3.right);
			Vector3 across = Flat(restRotation * Vector3.forward, Vector3.forward);
			for (int i = 0; i < SamplePoints; i++)
			{
				Vector3 world = restPosition + along * sampleX[i] + across * sampleZ[i];
				sampleHeight[i] = sea.HeightAt(world);
			}
			FitPlane(sampleX, sampleZ, sampleHeight, SamplePoints, out float mean, out float slopeAlong, out float slopeAcross);
			previousSample = sample;
			previousSampleTime = sampleTime;
			sample = new Vector3(mean - sea.SeaLevel, slopeAlong, slopeAcross);
			sampleTime = seaTime;
			samples++;
		}

		private static Vector3 Flat(Vector3 v, Vector3 fallback)
		{
			v.y = 0f;
			return v.sqrMagnitude > 1e-8f ? v.normalized : fallback;
		}

		private void RefreshCoefficients()
		{
			float g = Mathf.Max(0.05f, sea.Gravity);
			const float rho = SeaWaterDensity;
			FloatingBody b = Body;
			float area = b.WaterplaneArea;
			float draught = b.MeanDraught;

			heaveOmega = HeaveNaturalFrequency(b.Mass, area, rho, g);
			heaveZeta = HeaveDampingRatio(heaveOmega, area, b.Mass, draught, rho, g, ViscousDamping);

			// No capsizing: an unstable body is held upright by a small positive stiffness.
			float floorGM = 0.02f * Mathf.Max(0.1f, Mathf.Min(b.HalfLength, b.HalfWidth));
			float gmRoll = Mathf.Max(floorGM, b.MetacentricHeightRoll);
			float gmPitch = Mathf.Max(floorGM, b.MetacentricHeightPitch);
			rollOmega = TiltNaturalFrequency(b.Mass, b.GyrationRoll, b.DisplacedVolume, gmRoll, area, rho, g);
			pitchOmega = TiltNaturalFrequency(b.Mass, b.GyrationPitch, b.DisplacedVolume, gmPitch, area, rho, g);
			float rollInertia = b.Mass * b.GyrationRoll * b.GyrationRoll + TiltAddedInertia(area, rho);
			float pitchInertia = b.Mass * b.GyrationPitch * b.GyrationPitch + TiltAddedInertia(area, rho);
			rollZeta = TiltDampingRatio(rollOmega, b.WaterplaneInertiaRoll, rollInertia, draught, rho, g, ViscousDamping);
			pitchZeta = TiltDampingRatio(pitchOmega, b.WaterplaneInertiaPitch, pitchInertia, draught, rho, g, ViscousDamping);

			float peak = environment != null && environment.PeakPeriod > 0f ? environment.PeakPeriod : PeakPeriod(sea.WindSpeed, g);
			attenuation = DepthAttenuation(DeepWaterWavenumber(peak, g), draught);

			float fastest = Mathf.Min(peak, Mathf.Min(NaturalPeriod(heaveOmega), Mathf.Min(NaturalPeriod(rollOmega), NaturalPeriod(pitchOmega))));
			interval = Mathf.Clamp(fastest / 8f, 1f / 30f, 0.75f);

			// Three points at 90°, 210° and 330° on an ellipse √2 times the waterplane's radii of
			// gyration: their mean is the centre and their second moments the waterplane's.
			float kx = b.HalfLength / Mathf.Sqrt(3f) * Mathf.Sqrt(2f);
			float kz = b.HalfWidth / Mathf.Sqrt(3f) * Mathf.Sqrt(2f);
			for (int i = 0; i < SamplePoints; i++)
			{
				float a = (90f + 120f * i) * Mathf.Deg2Rad;
				sampleX[i] = kx * Mathf.Cos(a);
				sampleZ[i] = kz * Mathf.Sin(a);
			}
			coefficientsReady = true;
		}

		/// <summary>Integrates the motion over this frame's sea time and poses the target.</summary>
		private void Move()
		{
			if (sea == null || sea.IsLava || samples == 0 || !coefficientsReady)
			{
				return;
			}
			float dt = stepSeconds;
			if (dt > 0f)
			{
				// The plane the body is chasing now, carried forward on its last rate of change.
				Vector3 rate = Vector3.zero;
				float span = sampleTime - previousSampleTime;
				if (samples > 1 && span > 1e-4f)
				{
					rate = (sample - previousSample) / span;
				}
				Vector3 now = sample + rate * Mathf.Clamp(seaTime - sampleTime, 0f, interval);
				float push = attenuation * Response;
				float heaveTarget = now.x * push, heaveTargetRate = rate.x * push;
				// Pitch about z lifts +x for a sea rising along x; roll about x lifts +z for a negative angle.
				float pitchTarget = Mathf.Atan(now.y) * push, pitchTargetRate = rate.y * push;
				float rollTarget = -Mathf.Atan(now.z) * push, rollTargetRate = -rate.z * push;

				float fastest = Mathf.Max(heaveOmega, Mathf.Max(pitchOmega, rollOmega));
				int steps = Mathf.Clamp(Mathf.CeilToInt(fastest * dt / 0.2f), 1, 8);
				float h = dt / steps;
				for (int i = 0; i < steps; i++)
				{
					Step(ref heave, ref heaveRate, heaveTarget, heaveTargetRate, heaveOmega, heaveZeta, h);
					Step(ref pitch, ref pitchRate, pitchTarget, pitchTargetRate, pitchOmega, pitchZeta, h);
					Step(ref roll, ref rollRate, rollTarget, rollTargetRate, rollOmega, rollZeta, h);
				}
			}

			var pivot = new Vector3(restPosition.x, sea.SeaLevel + restOffset + heave, restPosition.z);
			Quaternion tilt = Quaternion.AngleAxis(pitch * Mathf.Rad2Deg, Vector3.forward) * Quaternion.AngleAxis(roll * Mathf.Rad2Deg, Vector3.right);
			// A rigid motion about the pivot: everything under the target turns and rises with it.
			Quaternion turn = restRotation * tilt * Quaternion.Inverse(restRotation);
			Target.SetPositionAndRotation(pivot + turn * (targetRestPosition - restPosition), turn * targetRestRotation);
			moved = true;
		}

		private Bounds LocalBounds()
		{
			MeshFilter filter = Target != null ? Target.GetComponentInChildren<MeshFilter>() : null;
			if (filter != null && filter.sharedMesh != null)
			{
				Bounds b = filter.sharedMesh.bounds;
				Vector3 scale = filter.transform.lossyScale;
				return new Bounds(Vector3.Scale(b.center, scale), Vector3.Scale(b.size, scale));
			}
			return new Bounds(Vector3.zero, Vector3.one);
		}

		// ── The physics, as pure functions ────────────────────────────

		/// <summary>Radius of the circle with this area, m.</summary>
		public static float EquivalentRadius(float area) => Mathf.Sqrt(Mathf.Max(0f, area) / Mathf.PI);

		/// <summary>
		/// Heave added mass of a waterplane, kg: (4/3)ρa³, half the 8/3·ρa³ of a disc accelerating
		/// broadside in open water — the high-frequency value for a body floating at the surface.
		/// </summary>
		public static float HeaveAddedMass(float area, float fluidDensity)
		{
			float a = EquivalentRadius(area);
			return 4f / 3f * fluidDensity * a * a * a;
		}

		/// <summary>Undamped heave frequency, rad/s: √(ρgA / (m + a₃₃)).</summary>
		public static float HeaveNaturalFrequency(float mass, float area, float fluidDensity, float gravity)
		{
			float effective = Mathf.Max(1e-6f, mass + HeaveAddedMass(area, fluidDensity));
			return Mathf.Sqrt(Mathf.Max(0f, fluidDensity * gravity * area) / effective);
		}

		/// <summary>Period of an angular frequency, seconds.</summary>
		public static float NaturalPeriod(float omega) => omega > 1e-6f ? 2f * Mathf.PI / omega : float.PositiveInfinity;

		/// <summary>
		/// Heave damping ratio: <paramref name="viscous"/> plus radiation, ρω²A²e^{−2kd}/(4g·m_eff) with
		/// k = ω²/g — the Haskind relation for a body small against the waves it makes.
		/// </summary>
		public static float HeaveDampingRatio(float omega, float area, float mass, float meanDraught, float fluidDensity, float gravity, float viscous)
		{
			float effective = Mathf.Max(1e-6f, mass + HeaveAddedMass(area, fluidDensity));
			float k = omega * omega / Mathf.Max(1e-3f, gravity);
			float radiation = fluidDensity * omega * omega * area * area * Mathf.Exp(-2f * k * Mathf.Max(0f, meanDraught)) / (4f * gravity * effective);
			return Mathf.Clamp(viscous + radiation, 0f, 8f);
		}

		/// <summary>Metacentric height GM = I_wp/V + z_B − z_G, m. Positive is stable upright.</summary>
		public static float MetacentricHeight(float displacedVolume, float waterplaneInertia, float buoyancyHeight, float gravityHeight)
		{
			return waterplaneInertia / Mathf.Max(1e-9f, displacedVolume) + buoyancyHeight - gravityHeight;
		}

		/// <summary>Added moment of inertia in pitch or roll, kg·m²: (8/45)ρa⁵, half a disc's 16/45·ρa⁵.</summary>
		public static float TiltAddedInertia(float area, float fluidDensity)
		{
			float a = EquivalentRadius(area);
			return 8f / 45f * fluidDensity * a * a * a * a * a;
		}

		/// <summary>Undamped pitch or roll frequency, rad/s: √(ρgV·GM / (m·k² + added)).</summary>
		public static float TiltNaturalFrequency(float mass, float gyration, float displacedVolume, float metacentricHeight, float area, float fluidDensity, float gravity)
		{
			float inertia = Mathf.Max(1e-6f, mass * gyration * gyration + TiltAddedInertia(area, fluidDensity));
			float stiffness = fluidDensity * gravity * displacedVolume * metacentricHeight;
			return Mathf.Sqrt(Mathf.Max(0f, stiffness) / inertia);
		}

		/// <summary>
		/// Pitch or roll damping ratio: <paramref name="viscous"/> plus radiation, ρω⁶I_wp²e^{−2kd}/(8g³·I_eff),
		/// from the Froude–Krylov moment ρgkI_wp of a long wave and the Haskind relation.
		/// </summary>
		public static float TiltDampingRatio(float omega, float waterplaneInertia, float inertia, float meanDraught, float fluidDensity, float gravity, float viscous)
		{
			float k = omega * omega / Mathf.Max(1e-3f, gravity);
			float w3 = omega * omega * omega;
			float radiation = fluidDensity * w3 * w3 * waterplaneInertia * waterplaneInertia * Mathf.Exp(-2f * k * Mathf.Max(0f, meanDraught))
				/ (8f * gravity * gravity * gravity * Mathf.Max(1e-6f, inertia));
			return Mathf.Clamp(viscous + radiation, 0f, 8f);
		}

		/// <summary>Deep-water wave number of a period, rad/m: (2π/T)²/g.</summary>
		public static float DeepWaterWavenumber(float period, float gravity)
		{
			float omega = 2f * Mathf.PI / Mathf.Max(0.1f, period);
			return omega * omega / Mathf.Max(1e-3f, gravity);
		}

		/// <summary>How much of a wave's pressure reaches a depth: e^{−kd}.</summary>
		public static float DepthAttenuation(float wavenumber, float depth) => Mathf.Exp(-Mathf.Max(0f, wavenumber) * Mathf.Max(0f, depth));

		/// <summary>Peak period of a fully developed sea (Pierson–Moskowitz, ω_p = 0.877g/U), seconds.</summary>
		public static float PeakPeriod(float windSpeed, float gravity)
		{
			return Mathf.Clamp(2f * Mathf.PI * Mathf.Max(0f, windSpeed) / (0.877f * Mathf.Max(0.05f, gravity)), 1f, 25f);
		}

		/// <summary>
		/// Motion over wave amplitude at frequency ω for a body whose damping acts against the water's
		/// motion: |1 + 2iζr| / |1 − r² + 2iζr|, r = ω/ωₙ.
		/// </summary>
		public static float ResponseAmplitude(float omega, float naturalOmega, float zeta)
		{
			float r = omega / Mathf.Max(1e-6f, naturalOmega);
			float d = 2f * zeta * r;
			float a = 1f - r * r;
			return Mathf.Sqrt((1f + d * d) / Mathf.Max(1e-12f, a * a + d * d));
		}

		/// <summary>
		/// One implicit step of ẍ = ω²(target − x) + 2ζω(targetRate − ẋ): stable for any step, any ω.
		/// </summary>
		public static void Step(ref float x, ref float v, float target, float targetRate, float omega, float zeta, float dt)
		{
			float w2 = omega * omega;
			float c = 2f * zeta * omega;
			v = (v + dt * (w2 * (target - x) + c * targetRate)) / (1f + c * dt + w2 * dt * dt);
			x += dt * v;
		}

		/// <summary>Least-squares plane h = mean + sx·x + sz·z through <paramref name="count"/> samples.</summary>
		public static void FitPlane(float[] x, float[] z, float[] h, int count, out float mean, out float slopeX, out float slopeZ)
		{
			// Centred normal equations: the fit through the samples' centroid, slopes from the 2×2.
			double mx = 0, mz = 0, mh = 0;
			for (int i = 0; i < count; i++)
			{
				mx += x[i];
				mz += z[i];
				mh += h[i];
			}
			mx /= count;
			mz /= count;
			mh /= count;
			double sxx = 0, szz = 0, sxz = 0, sxh = 0, szh = 0;
			for (int i = 0; i < count; i++)
			{
				double dx = x[i] - mx, dz = z[i] - mz, dh = h[i] - mh;
				sxx += dx * dx;
				szz += dz * dz;
				sxz += dx * dz;
				sxh += dx * dh;
				szh += dz * dh;
			}
			double det = sxx * szz - sxz * sxz;
			double ax = 0, az = 0;
			if (Math.Abs(det) > 1e-12)
			{
				ax = (sxh * szz - szh * sxz) / det;
				az = (szh * sxx - sxh * sxz) / det;
			}
			slopeX = (float)ax;
			slopeZ = (float)az;
			// The plane's height at the body's centre (x = z = 0), not at the samples' centroid.
			mean = (float)(mh - ax * mx - az * mz);
		}
	}
}
