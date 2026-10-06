using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Tornadoes and dust devils: the funnel, the debris cloud round its foot and the flecks thrown round
	/// in it, for every storm whose footprint is a funnel.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Everything seen comes from the vortex and the storm it hangs from.</b> A tornado's peak wind is
	/// the speed limit its air's CAPE sets, grown and spun down with its storm's life; it hangs from the
	/// wall cloud the storm's anatomy puts over it (<see cref="StormAnatomy.WallCloudBase"/>), which is
	/// where the sky draws the storm's lowering; the condensation funnel is the water its low pressure
	/// condenses there, whose flare, taper and tip, and how far below the wall cloud it reaches, are
	/// solved (<see cref="VortexPhysics"/>); what it lifts is its own ground's — dust, soil, spray over
	/// the sea — as much as its wind lifts of it, as high as its wind throws it. A weak tornado's funnel
	/// stays aloft over its debris whirl; a violent one reaches the ground inside its debris cloud; a
	/// wide one under a low base is a wedge, and splits into vortices going round inside it. A dust devil
	/// has no funnel — the air is dry — so it is its dust alone.
	/// </para>
	/// <para>
	/// It was a mesh tube displaced to the condensation radius and hung from the open air's cloud base,
	/// kilometres above the storm's own: an opaque brown horn floating in the sky. Now it is marched as
	/// cloud (FishVortex.shader) through a box round the vortex, lit as the storm's cloud is, with its
	/// debris in the same march; the flecks (FishVortexDebris.shader) are only the pieces a near camera
	/// sees move in it.
	/// </para>
	/// <para>
	/// Nothing is simulated from frame to frame: both shaders work out their shape and motion from the
	/// property block and the clock. It costs the CPU a few numbers per vortex, and every client sees the
	/// same tornado.
	/// </para>
	/// </remarks>
	public sealed class VortexPresenter
	{
		/// <summary>The vortex volume's shader, found by name when the profile has no material for it.</summary>
		public const string FunnelShaderName = "FishMMO/Weather/Vortex";

		/// <summary>The flecks' shader, found by name when the profile has no material for it.</summary>
		public const string DebrisShaderName = "FishMMO/Weather/Vortex Debris";

		/// <summary>
		/// Beyond this a vortex is not drawn. A tornado a few hundred metres wide and a kilometre tall is
		/// still a dozen pixels across at thirty kilometres, which is as far as storm spotters see one;
		/// its box is that small on the screen too, so the march costs little that far off.
		/// </summary>
		public const float FarthestMeters = 30000f;

		/// <summary>The widest a funnel flares where it meets the wall cloud, in core radii: past this it is the wall cloud's.</summary>
		public const float FlareCoreRadii = 6f;

		/// <summary>Within this the flecks are drawn; further off the debris cloud round them is all that shows.</summary>
		public const float FlecksMeters = 3000f;

		/// <summary>The long strides through a vortex's shape. The march shortens them through an edge it has just found and wherever it is dense.</summary>
		public const int Strides = 40;

		/// <summary>Where the march starts, m from the eye: right there, since standing in a debris cloud is standing in it.</summary>
		public const float NearestMeters = 0.5f;

		/// <summary>
		/// The extinction of lifted ground where the wind carries all it can, 1/m: blown sand's at full
		/// strength (the sand term of <see cref="AirPhysics.PrecipitationExtinction"/>), a hundred metres
		/// of which hides the sun.
		/// </summary>
		public const float DustExtinction = 0.04f;

		/// <summary>How far the box goes on below the ground at the foot, so its dust reaches a hollow nearby. Not over the sea.</summary>
		public const float BelowGroundMeters = 30f;

		private static readonly int CentreId = Shader.PropertyToID("_VortexCentre");
		private static readonly int WindId = Shader.PropertyToID("_VortexWind");
		private static readonly int LeanId = Shader.PropertyToID("_VortexLean");
		private static readonly int FlowId = Shader.PropertyToID("_VortexFlow");
		private static readonly int TimeId = Shader.PropertyToID("_VortexTime");
		private static readonly int DustId = Shader.PropertyToID("_VortexDust");
		private static readonly int DustShapeId = Shader.PropertyToID("_VortexDustShape");
		private static readonly int LookId = Shader.PropertyToID("_VortexLook");
		private static readonly int SkyId = Shader.PropertyToID("_VortexSky");
		private static readonly int MarchId = Shader.PropertyToID("_VortexMarch");
		private static readonly int StriaeId = Shader.PropertyToID("_VortexStriae");
		private static readonly int NoiseId = Shader.PropertyToID("_VortexNoise");
		private static readonly int DebrisId = Shader.PropertyToID("_VortexDebris");
		private static readonly int DebrisLookId = Shader.PropertyToID("_VortexDebrisLook");
		private static readonly int FleckId = Shader.PropertyToID("_VortexFleck");
		private static readonly int CloudSunDirId = Shader.PropertyToID("_FishCloudSunDir");
		private static readonly int PaceId = Shader.PropertyToID("_VortexPace");

		/// <summary>
		/// What a tornado tears off bound ground — soil, roots, crops, grass — and the spray a waterspout
		/// whips off the sea. Loose ground is its own substance's colour.
		/// </summary>
		private static readonly Color Soil = new Color(0.36f, 0.31f, 0.25f), Spray = new Color(0.82f, 0.86f, 0.88f);

		/// <summary>
		/// What the ground throws back up at a tornado, as a share of the open sky an upright face sees: a
		/// tenth, about what grassland and farmland reflect. Faces turned down see only this.
		/// </summary>
		private const float GroundLight = 0.1f;

		/// <summary>The ground under each vortex, read again once it has moved this far.</summary>
		private const float GroundRereadMeters = 25f;

		private Mesh box;
		private Mesh debris;
		private int debrisBuilt = -1;
		private Material ownVolume, ownDebris;
		private readonly MaterialPropertyBlock volumeBlock = new MaterialPropertyBlock();
		private readonly MaterialPropertyBlock debrisBlock = new MaterialPropertyBlock();
		private readonly List<(StormCell cell, float distance)> nearest = new List<(StormCell, float)>();
		private readonly Dictionary<ushort, (Vector2 at, GroundTraits ground)> grounds = new Dictionary<ushort, (Vector2, GroundTraits)>();
		private readonly Dictionary<ushort, Pace> paces = new Dictionary<ushort, Pace>();

		/// <summary>
		/// What a vortex's motions are timed by: its form at maturity in its cell's own air, constant for
		/// the cell's life (see <see cref="PaceOf"/>).
		/// </summary>
		private struct Pace
		{
			/// <summary>The cell air it was worked out from; read again when that is.</summary>
			public StormCellAir.Air Air;
			/// <summary>x the climb (m/s), y the core radius (m), z the peak wind (m/s), w the clock's wrap (s).</summary>
			public Vector4 Rates;
			/// <summary>A fleck's life, s, whole in the clock's wrap.</summary>
			public float DebrisLife;
			/// <summary>How far out a fleck goes round half-way up its life, m.</summary>
			public float DebrisSpread;
		}

		/// <summary>Vortices drawn last frame.</summary>
		public int Drawn { get; private set; }

		/// <summary>
		/// A call with the timeline alone: everything else comes from the weather presentation's context —
		/// its scene and settings, which each cell's own air is read in (StormCellAir).
		/// </summary>
		/// <param name="column">Unused: the storm's air is its cell's.</param>
		/// <param name="body">Unused: the world's pull is the cell's air's, offsets and all.</param>
		/// <param name="tick">The fractional server tick it is drawn at (WeatherPresentation.PresentTick).</param>
		public void Draw(WeatherTimeline timeline, double tick, Camera camera, Material funnelMaterial, Material debrisMaterial,
			int limit, int particles, float time, in AirColumn column, FishMMO.Shared.Celestial.WorldBody body)
		{
			WeatherPresentation presentation = WeatherPresentation.Instance;
			if (presentation == null || !presentation.HasContext)
			{
				Drawn = 0;
				return;
			}
			WeatherContext context = presentation.Context;
			context.Timeline = timeline;
			SkySystem sky = SkySystem.Instance;
			WeatherRenderProfile profile = sky != null && sky.Profile != null ? sky.Profile : WeatherRenderProfile.Active;
			Draw(context, tick, camera, funnelMaterial, debrisMaterial, profile != null ? profile.CloudShape : null, limit, particles, time);
		}

		/// <param name="context">The weather at the viewer: its timeline, scene and settings. Each storm's anatomy and each tornado's strength are worked out in the cell's own air (StormCellAir), never the viewer's.</param>
		/// <param name="tick">The fractional server tick it is drawn at (WeatherPresentation.PresentTick): the cells move every frame.</param>
		/// <param name="volumeMaterial">The vortex volume (FishMMO/Weather/Vortex).</param>
		/// <param name="debrisMaterial">The flecks (FishMMO/Weather/Vortex Debris).</param>
		/// <param name="noise">The clouds' shape volume, which the funnel's striations and the dust's billows are read from.</param>
		/// <param name="limit">The most vortices drawn at once, nearest first.</param>
		/// <param name="particles">Flecks for a tornado near the camera; a dust devil takes the same.</param>
		/// <param name="time">Seconds, wrapped — the clock the vortices turn on.</param>
		public void Draw(in WeatherContext context, double tick, Camera camera, Material volumeMaterial, Material debrisMaterial, Texture noise,
			int limit, int particles, float time)
		{
			Drawn = 0;
			WeatherTimeline timeline = context.Timeline;
			if (timeline == null || camera == null || limit <= 0 || timeline.SceneMode != WeatherSceneMode.Own)
			{
				return;
			}
			// The world's air at all: no vortex on an airless world. Each one's own air is its cell's.
			if (!context.Sample.Planet.HasAir || context.Sample.Planet.Gravity <= 0f)
			{
				return;
			}
			// Its life on the whole tick, where it moves on the fractional one.
			// The world time it is drawn at: where the vortices stand and how strong they are, held with the world.
			double now = timeline.WorldSecondsAt(tick);
			volumeMaterial = volumeMaterial != null ? volumeMaterial : Own(ref ownVolume, FunnelShaderName);
			debrisMaterial = debrisMaterial != null ? debrisMaterial : Own(ref ownDebris, DebrisShaderName);
			if (volumeMaterial == null)
			{
				return;
			}
			if (box == null)
			{
				box = CurtainPresenter.BuildBox();
				box.name = "Vortex";
			}
			if (particles > 0 && (debris == null || debrisBuilt != particles))
			{
				debris = BuildDebris(debris, particles);
				debrisBuilt = particles;
			}

			Vector3 viewer = camera.transform.position;
			nearest.Clear();
			foreach (StormCell cell in timeline.Cells)
			{
				if (cell.Shape != StormCellShape.Funnel)
				{
					continue;
				}
				Vector2 centre = cell.CentreAtSeconds(now);
				float distance = Vector2.Distance(centre, new Vector2(viewer.x, viewer.z));
				if (distance <= FarthestMeters)
				{
					nearest.Add((cell, distance));
				}
			}
			if (nearest.Count == 0)
			{
				return;
			}
			nearest.Sort((a, b) => a.distance.CompareTo(b.distance));
			if (grounds.Count > 64)
			{
				grounds.Clear();
			}
			if (paces.Count > 64)
			{
				paces.Clear();
			}
			bool cloudsLit = SkySystem.CloudsReady;
			Vector3 toSun = SunDirection(cloudsLit);

			for (int i = 0; i < nearest.Count && Drawn < limit; i++)
			{
				StormCell cell = nearest[i].cell;
				float envelope = cell.EnvelopeAtSeconds(now);
				if (envelope * cell.PeakIntensity < 0.02f)
				{
					continue;
				}

				/* The air this tornado feeds on: its CAPE is its speed limit, its updraught (spun up by the
				 * mesocyclone) what feeds it, its condensate what its funnel holds. Its CELL's air, read
				 * where and when the cell matured (StormCellAir): it was the viewer's, so two players in
				 * different air saw two funnels on one storm, and the funnel changed as its watcher walked. */
				StormCellAir.Air cellAir = StormCellAir.Of(timeline, context.Settings, context.Scene, cell);
				if (cellAir == null)
				{
					continue;
				}
				WeatherSample own = cellAir.Sample;
				PlanetAir planet = own.Planet;
				if (!planet.HasAir || planet.Gravity <= 0f)
				{
					continue;
				}
				AirColumn open = own.OpenColumn;
				float gravity = planet.Gravity;
				float updraft = Mathf.Max(2f, open.Updraft * VortexPhysics.MesocycloneUpdraftGain);
				float condensate = VortexPhysics.FunnelExtinction(open, planet);
				// And its striations: how far the moisture it brings through its boundary layer's rolls swings
				// the level each streamline condenses at, and how big those rolls are.
				float swing = VortexPhysics.CondensationSwing(open, planet);
				float rolls = VortexPhysics.RollWavelength(open);
				float forwardScatter = CloudClimate.ForwardScatter(open, planet);

				Vector2 centre = cell.CentreAtSeconds(now);
				var motion = new Vector2(cell.VelocityX, cell.VelocityZ);
				float ground = GroundAt(centre, out bool water);
				GroundTraits traits = TraitsAt(cell.ID, centre, ground, water, context.Settings);
				float liftingWind = water ? VortexPhysics.TearsTheSea
					: traits.Loose != null ? WeatherPhysics.LiftingWind(traits.Loose, planet, open.SurfaceKelvin)
					: VortexPhysics.StripsBoundGround;
				Color lifted = water ? Spray : traits.Loose != null ? traits.Loose.FogColor : Soil;
				bool devil = cell.Kind == StormKind.DustDevil;
				float spin = VortexPhysics.Spin(timeline.LatitudeDegrees, cell.Seed, !devil);
				float seed = (cell.Seed & 0xFFFFu) / 65536f;

				VortexPhysics.Form form;
				StormAnatomy anatomy = default;
				if (devil)
				{
					// As tall as the curtains draw its whirl: the same plume.
					form = VortexPhysics.DustDevil(envelope, cell.PeakIntensity, cell.RadiusMeters, cell.ExtentMeters * 1.4f, motion, liftingWind);
				}
				else
				{
					anatomy = StormAnatomy.Of(cell.Kind, own, motion, timeline.LatitudeDegrees, cell.RadiusMeters);
					if (!anatomy.Valid)
					{
						continue;
					}
					form = VortexPhysics.Tornado(open.Cape, envelope, cell.PeakIntensity, now > cell.DecaySeconds, cell.RadiusMeters,
						anatomy.WallCloudBase, anatomy.WallCloudRadius, motion, updraft, gravity, liftingWind);
				}
				if (!form.Valid)
				{
					continue;
				}
				Pace pace = PaceOf(cell, cellAir, anatomy, form, devil, motion, updraft, gravity);

				float core = form.CoreRadius;
				float top = form.Top;
				// Past the wall cloud's base, the funnel only lowers the cloud the sky draws there, and fades
				// out into it; its flare is the wall cloud's past a few core radii.
				float over = form.Condenses ? Mathf.Clamp(0.2f * form.FunnelDepth, 20f, 150f) : 0f;
				float flare = form.Condenses ? Mathf.Min(FlareCoreRadii * core, Mathf.Max(core, anatomy.WallCloudRadius)) : 0f;
				// Below this under the top the funnel is inside two core radii: a fifth of V²/g, where the
				// condensation radius is 1.6 R. Its striations cannot carry it further — they are gone by
				// 2 R (VortexPhysics.StriationsKept); before they were, its damp bands reached out to the
				// flare's limit below here and the tube cut them off square.
				float flareDepth = form.Condenses ? Mathf.Min(top, 0.2f * form.FunnelDepth) : 0f;
				// The march is kept to a cylinder sheared from the foot to the top; the axis bows off that
				// line by a quarter of its lean at most, wanders, and stands still past the top.
				float lean = form.TopOffset.magnitude;
				float inflate = 0.25f * lean + form.Sway + lean * Mathf.Max(over, 0.35f * form.DebrisHeight) / Mathf.Max(1f, top);
				float subReach = form.SubVortices > 0
					? (VortexPhysics.SubVortexOrbit + 6f * VortexPhysics.SubVortexCore / Mathf.Sqrt(form.SubVortices)) * core : 0f;
				float tube = Mathf.Max(Mathf.Max(form.DebrisReachGround, form.DebrisReachTop) * core * 1.2f,
					Mathf.Max(form.Condenses ? 2f * core : 0f, subReach)) + inflate + 5f;

				float dust = DustExtinction * form.Lifted;
				float sunSeen = 1f, skySeen = 1f, skyEdge = 0.5f * Mathf.PI;
				if (!devil)
				{
					StormLight(anatomy, centre, centre + 0.5f * form.TopOffset, 0.5f * top, toSun, out sunSeen, out skySeen, out skyEdge);
				}

				volumeBlock.SetVector(CentreId, new Vector4(centre.x, ground, centre.y, top));
				volumeBlock.SetVector(WindId, new Vector4(core, form.PeakWind, gravity, spin));
				volumeBlock.SetVector(LeanId, new Vector4(form.TopOffset.x, form.TopOffset.y, form.Sway, over));
				volumeBlock.SetVector(FlowId, new Vector4(form.AxialUpdraft, form.SubVortices, form.Hollow, cloudsLit ? 1f : 0f));
				volumeBlock.SetVector(TimeId, new Vector4(time, seed, form.Rope, 0f));
				volumeBlock.SetVector(PaceId, pace.Rates);
				volumeBlock.SetVector(DustId, new Vector4(liftingWind, form.DebrisHeight, dust, form.DebrisThinning));
				volumeBlock.SetVector(DustShapeId, new Vector4(form.DebrisReachGround, form.DebrisReachTop, tube, 0f));
				volumeBlock.SetVector(LookId, new Vector4(lifted.r, lifted.g, lifted.b, form.Condenses ? condensate : 0f));
				volumeBlock.SetVector(SkyId, new Vector4(sunSeen, skySeen, forwardScatter, flare));
				volumeBlock.SetVector(MarchId, new Vector4(Strides, NearestMeters, noise != null ? 1f : 0f, flareDepth));
				volumeBlock.SetVector(StriaeId, new Vector4(form.Condenses ? swing : 0f, rolls, skyEdge, GroundLight / Mathf.Max(GroundLight, skySeen)));
				if (noise != null)
				{
					volumeBlock.SetTexture(NoiseId, noise);
				}

				/* The box: round the foot and round the top, as wide as the widest of the flare, the debris
				 * and the wandering, from under the ground at the foot to the top of whatever stands
				 * highest — the funnel's reach into the cloud, or a dust devil's billows. */
				float radius = Mathf.Max(tube, flare + inflate) + 10f;
				float bottom = ground - (water ? 0f : BelowGroundMeters);
				float roof = ground + Mathf.Max(top + over, 1.35f * form.DebrisHeight) + 20f;
				Vector2 head = centre + form.TopOffset;
				float minX = Mathf.Min(centre.x, head.x) - radius, maxX = Mathf.Max(centre.x, head.x) + radius;
				float minZ = Mathf.Min(centre.y, head.y) - radius, maxZ = Mathf.Max(centre.y, head.y) + radius;
				var position = new Vector3(0.5f * (minX + maxX), 0.5f * (bottom + roof), 0.5f * (minZ + maxZ));
				var size = new Vector3(maxX - minX, roof - bottom, maxZ - minZ);
				var bounds = new Bounds(position, size);
				/* The shader pins the box to the far plane so a tornado past the far clip is still seen; the
				 * culling has to agree, or a box wholly beyond it is dropped before it is drawn. Reach the
				 * bounds in toward the camera, along the way to the box, to just inside it. */
				float far = camera.farClipPlane * 0.9f;
				if (bounds.SqrDistance(viewer) > far * far)
				{
					bounds.Encapsulate(viewer + (position - viewer).normalized * far);
				}
				var volumeParams = new RenderParams(volumeMaterial)
				{
					camera = camera,
					matProps = volumeBlock,
					worldBounds = bounds,
					shadowCastingMode = ShadowCastingMode.Off,
					receiveShadows = false,
				};
				Graphics.RenderMesh(volumeParams, box, 0, Matrix4x4.TRS(position, Quaternion.identity, size));

				/* The flecks: the pieces a near camera sees go round in the debris cloud, as many as its
				 * wind lifts, climbing with the air in the core. Darker than the dust that hides them
				 * further off: torn soil and plants, not the fine grain. */
				if (particles > 0 && debris != null && debrisMaterial != null && nearest[i].distance < FlecksMeters && form.Lifted > 0.01f)
				{
					debrisBlock.SetVector(CentreId, new Vector4(centre.x, ground, centre.y, top));
					debrisBlock.SetVector(WindId, new Vector4(core, form.PeakWind, gravity, spin));
					debrisBlock.SetVector(LeanId, new Vector4(form.TopOffset.x, form.TopOffset.y, form.Sway, over));
					debrisBlock.SetVector(FlowId, new Vector4(form.AxialUpdraft, form.SubVortices, form.Hollow, cloudsLit ? 1f : 0f));
					debrisBlock.SetVector(TimeId, new Vector4(time, seed, form.Rope, 0f));
					debrisBlock.SetVector(PaceId, pace.Rates);
					debrisBlock.SetVector(SkyId, new Vector4(sunSeen, skySeen, forwardScatter, flare));
					debrisBlock.SetVector(FleckId, new Vector4(lifted.r * 0.55f, lifted.g * 0.55f, lifted.b * 0.55f, form.Lifted));
					debrisBlock.SetVector(DebrisId, new Vector4(form.DebrisHeight, form.DebrisReachGround, form.DebrisReachTop, pace.DebrisLife));
					debrisBlock.SetVector(DebrisLookId, new Vector4(BottomHeavy(devil), Mathf.Clamp(core * 0.04f, 0.3f, 4f), FlecksMeters, pace.DebrisSpread));
					float fleckRadius = form.DebrisReachGround * core + inflate + 20f;
					var debrisParams = new RenderParams(debrisMaterial)
					{
						camera = camera,
						matProps = debrisBlock,
						worldBounds = new Bounds(new Vector3(centre.x + 0.5f * form.TopOffset.x, ground + 0.5f * form.DebrisHeight, centre.y + 0.5f * form.TopOffset.y),
							new Vector3(2f * fleckRadius + Mathf.Abs(form.TopOffset.x), form.DebrisHeight + 60f, 2f * fleckRadius + Mathf.Abs(form.TopOffset.y))),
						shadowCastingMode = ShadowCastingMode.Off,
						receiveShadows = false,
					};
					Graphics.RenderMesh(debrisParams, debris, 0, Matrix4x4.identity);
				}
				Drawn++;
			}
		}

		/// <summary>How bottom-heavy the flecks are: a power on how far up their life they have climbed (FishVortexDebris).</summary>
		private static float BottomHeavy(bool devil) => devil ? 1.4f : 2.4f;

		/// <summary>
		/// The pace a vortex's motions are timed by: its form at full strength (envelope 1, not yet
		/// decaying) in its cell's own air, on the ground where the cell matured — so constant for the
		/// cell's life and the same on every client.
		/// </summary>
		/// <remarks>
		/// The shaders turn the clock into motion by multiplying it by rates — the climb over a billow's
		/// length, the sub-vortices' turn, a fleck's life and its angular speed. Taken from the live form,
		/// which grows and spins down every tick, each of those jumped by the change in rate times the whole
		/// clock (hours of it): every billow, sub-vortex and fleck somewhere else each frame. The live form
		/// still sets everything's size and strength; only the timing is the pace's. A cell whose mature
		/// form is no vortex at all keeps the first live form drawn instead.
		/// </remarks>
		private Pace PaceOf(in StormCell cell, StormCellAir.Air cellAir, in StormAnatomy anatomy, in VortexPhysics.Form live, bool devil,
			Vector2 motion, float updraft, float gravity)
		{
			if (paces.TryGetValue(cell.ID, out Pace known) && ReferenceEquals(known.Air, cellAir))
			{
				return known;
			}
			WeatherSample own = cellAir.Sample;
			AirColumn open = own.OpenColumn;
			// What the ground where it matured gives it to lift: its sea, its loose ground, or bound ground.
			float liftingWind = cellAir.Water || own.Ground.Water ? VortexPhysics.TearsTheSea
				: own.Ground.Loose != null ? WeatherPhysics.LiftingWind(own.Ground.Loose, own.Planet, open.SurfaceKelvin)
				: VortexPhysics.StripsBoundGround;
			VortexPhysics.Form form = devil
				? VortexPhysics.DustDevil(1f, cell.PeakIntensity, cell.RadiusMeters, cell.ExtentMeters * 1.4f, motion, liftingWind)
				: VortexPhysics.Tornado(open.Cape, 1f, cell.PeakIntensity, false, cell.RadiusMeters,
					anatomy.WallCloudBase, anatomy.WallCloudRadius, motion, updraft, gravity, liftingWind);
			if (!form.Valid)
			{
				form = live;
			}
			const double Wrap = FishMMO.Shared.Celestial.WorldMotion.SkyWrapSeconds;
			// A fleck's life as the debris shader had it — the debris' height over its climb — whole in the
			// wrap, so the flecks come round to where they were as the clock wraps.
			float life = Mathf.Max(1f, form.DebrisHeight) / Mathf.Max(2f, 0.5f * form.AxialUpdraft);
			double lives = System.Math.Max(1.0, System.Math.Round(Wrap / life));
			float core = Mathf.Max(0.5f, form.CoreRadius);
			float halfWay = Mathf.Sqrt(Mathf.Pow(0.5f, BottomHeavy(devil)));
			var pace = new Pace
			{
				Air = cellAir,
				Rates = new Vector4(form.AxialUpdraft, form.CoreRadius, form.PeakWind, (float)Wrap),
				DebrisLife = (float)(Wrap / lives),
				DebrisSpread = core * Mathf.Lerp(form.DebrisReachGround, form.DebrisReachTop, halfWay),
			};
			paces[cell.ID] = pace;
			return pace;
		}

		/// <summary>
		/// How much of the sun and of the sky reach a tornado under its storm, 0 to 1 each.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The sun reaches it only if its ray gets in under the storm: followed up from the funnel's
		/// middle, the ray is lost if it meets the storm's body where it has a base, the wall cloud, or
		/// the anvil at its own height — kilometres of cloud, which let through nothing worth drawing. So
		/// under a high sun a tornado is in the storm's shadow and grey, and when the sun is low enough
		/// to shine in under the base from the side it lights it gold.
		/// </para>
		/// <para>
		/// The sky it sees is the sky round the storm, below the edge of the storm's base: for a
		/// surface standing upright, the share of its sky that lies below an elevation e is
		/// (e + ½·sin 2e)/(π/2), averaged round it with e the elevation of the base's edge that way. A
		/// tenth more for the light the ground throws back up at it (<see cref="GroundLight"/>).
		/// </para>
		/// <para>
		/// And how high that sky reaches, for the faces of its ledges (VortexPhysics.SkyOnFace): the
		/// elevation of the base's edge, averaged round it — π/2 where it is not under the storm.
		/// </para>
		/// </remarks>
		/// <param name="cell">The storm cell's centre, which its anatomy is laid out from.</param>
		/// <param name="from">The funnel's middle, world x/z.</param>
		/// <param name="height">And its height above the ground, m.</param>
		/// <param name="skyEdge">How high the sky it sees reaches, radians, averaged round it.</param>
		private static void StormLight(in StormAnatomy anatomy, Vector2 cell, Vector2 from, float height, Vector3 toSun, out float sunSeen, out float skySeen, out float skyEdge)
		{
			Vector2 body = cell + anatomy.BodyOffset;
			float blocked = 0f;
			if (toSun.y > 0.01f)
			{
				var across = new Vector2(toSun.x, toSun.z) / toSun.y;
				Vector2 AtHeight(float h) => from + across * Mathf.Max(0f, h - height);
				blocked = Mathf.Max(blocked, Covered(AtHeight(anatomy.WallCloudBase), cell, anatomy.WallCloudRadius));
				blocked = Mathf.Max(blocked, Covered(AtHeight(anatomy.BaseMetres), body, anatomy.CoreRadius));
				blocked = Mathf.Max(blocked, Covered(AtHeight(0.5f * (anatomy.BaseMetres + anatomy.TopMetres)), body, anatomy.CoreRadius));
				if (anatomy.AnvilRadius > 0f)
				{
					blocked = Mathf.Max(blocked, Covered(AtHeight(anatomy.AnvilBase), body + anatomy.AnvilOffset, anatomy.AnvilRadius));
				}
			}
			// A few kilometres of storm cloud: e^-6 of the sun gets through, as the curtains take it.
			sunSeen = 1f - blocked * (1f - Mathf.Exp(-6f));

			float headroom = Mathf.Max(0f, anatomy.BaseMetres - height);
			float open = 0f;
			float reaches = 0f;
			const int Ways = 8;
			for (int k = 0; k < Ways; k++)
			{
				float angle = k * (2f * Mathf.PI / Ways);
				var way = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
				// How far to the edge of the storm's base that way, from under it; nothing overhead if it is not.
				Vector2 under = from - body;
				float b = Vector2.Dot(under, way);
				float c = under.sqrMagnitude - anatomy.CoreRadius * anatomy.CoreRadius;
				float edge = c < 0f ? -b + Mathf.Sqrt(b * b - c) : 0f;
				float elevation = edge > 1f ? Mathf.Atan2(headroom, edge) : 0.5f * Mathf.PI;
				open += (elevation + 0.5f * Mathf.Sin(2f * elevation)) / (0.5f * Mathf.PI);
				reaches += elevation;
			}
			skySeen = Mathf.Clamp01(GroundLight + (1f - GroundLight) * open / Ways);
			skyEdge = reaches / Ways;
		}

		/// <summary>How much of a disc of cloud a point is under: all of it well inside, none past its edge.</summary>
		private static float Covered(Vector2 point, Vector2 centre, float radius)
		{
			if (radius <= 0f)
			{
				return 0f;
			}
			return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f * radius, radius, Vector2.Distance(point, centre)));
		}

		/// <summary>
		/// Toward whatever lights the clouds: the sky's own light when it has published it, the scene's
		/// sun otherwise.
		/// </summary>
		private static Vector3 SunDirection(bool cloudsLit)
		{
			if (cloudsLit)
			{
				Vector4 lit = Shader.GetGlobalVector(CloudSunDirId);
				var direction = new Vector3(lit.x, lit.y, lit.z);
				if (direction.sqrMagnitude > 0.25f)
				{
					return direction.normalized;
				}
			}
			Light sun = RenderSettings.sun;
			return sun != null ? -sun.transform.forward : Vector3.up;
		}

		/// <summary>
		/// What the ground under a vortex gives it to lift: its biome's loose ground, or the sea. Read
		/// again only once the vortex has moved on a little.
		/// </summary>
		private GroundTraits TraitsAt(ushort id, Vector2 at, float ground, bool water, WorldSceneSettings settings)
		{
			if (water)
			{
				return new GroundTraits { Water = true };
			}
			if (grounds.TryGetValue(id, out var known) && (known.at - at).sqrMagnitude < GroundRereadMeters * GroundRereadMeters)
			{
				return known.ground;
			}
			BiomeReading reading = BiomeSampler.Read(new Vector3(at.x, ground, at.y), settings);
			GroundTraits traits = GroundTraits.Of(reading.IsGrounded ? reading.Biome : null, false);
			grounds[id] = (at, traits);
			return traits;
		}

		/// <summary>
		/// The height a vortex stands on: the ground, or the sea over it — a tornado over water is a
		/// waterspout, and what it lifts is spray.
		/// </summary>
		internal static float GroundAt(Vector2 at, out bool water)
		{
			float ground = float.MinValue;
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				Vector3 origin = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;
				if (at.x < origin.x || at.x > origin.x + size.x || at.y < origin.z || at.y > origin.z + size.z)
				{
					continue;
				}
				ground = Mathf.Max(ground, origin.y + terrain.SampleHeight(new Vector3(at.x, 0f, at.y)));
			}
			water = false;
			if (SurfaceWater.TryGetSurfaceAt(at.x, at.y, out float level) && level > ground)
			{
				ground = level;
				water = true;
			}
			// Off every terrain with no sea: the datum, where the sea would be.
			return ground > float.MinValue ? ground : 0f;
		}

		/// <summary>
		/// A quad per fleck, every corner at the origin: which corner, and random numbers — the debris
		/// shader places and moves them all. Seeded, so every client throws the same flecks.
		/// </summary>
		public static Mesh BuildDebris(Mesh mesh, int particles)
		{
			var random = new System.Random(20260927);
			var positions = new Vector3[particles * 4];
			var corners = new Vector4[particles * 4];
			var randoms = new Vector4[particles * 4];
			var indices = new int[particles * 6];
			for (int i = 0; i < particles; i++)
			{
				// The first number is an even ramp, so a share of the particles is that share of them.
				var r = new Vector4((i + 0.5f) / particles, (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
				float size = (float)random.NextDouble();
				int v = i * 4;
				corners[v] = new Vector4(0f, 0f, 0f, size);
				corners[v + 1] = new Vector4(1f, 0f, 0f, size);
				corners[v + 2] = new Vector4(1f, 1f, 0f, size);
				corners[v + 3] = new Vector4(0f, 1f, 0f, size);
				for (int c = 0; c < 4; c++)
				{
					randoms[v + c] = r;
				}
				int t = i * 6;
				indices[t] = v; indices[t + 1] = v + 2; indices[t + 2] = v + 1;
				indices[t + 3] = v; indices[t + 4] = v + 3; indices[t + 5] = v + 2;
			}
			if (mesh == null)
			{
				mesh = new Mesh { name = "Vortex debris", hideFlags = HideFlags.DontSave };
			}
			mesh.Clear();
			mesh.indexFormat = positions.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			mesh.vertices = positions;
			mesh.SetUVs(0, corners);
			mesh.SetUVs(1, randoms);
			mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
			mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
			return mesh;
		}

		private static Material Own(ref Material material, string shaderName)
		{
			if (material == null)
			{
				Shader shader = Shader.Find(shaderName);
				if (shader != null)
				{
					material = new Material(shader) { name = shaderName, hideFlags = HideFlags.DontSave };
				}
			}
			return material;
		}

		public void Dispose()
		{
			Discard(ref box);
			Discard(ref debris);
			Discard(ref ownVolume);
			Discard(ref ownDebris);
			debrisBuilt = -1;
			grounds.Clear();
			paces.Clear();
		}

		private static void Discard<T>(ref T victim) where T : Object
		{
			if (victim != null)
			{
				if (Application.isPlaying) Object.Destroy(victim); else Object.DestroyImmediate(victim);
				victim = null;
			}
		}
	}
}
