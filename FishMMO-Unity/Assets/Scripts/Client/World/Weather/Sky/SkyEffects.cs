using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Lightning: schedules strikes from the weather, flashes the sky and the sun light, draws
	/// the bolts and asks for thunder once the sound would arrive.
	/// </summary>
	public sealed class LightningPresenter
	{
		public const float BoltSeconds = 0.25f;
		public const float SpeedOfSound = 343f;
		private static readonly int BoltColorId = Shader.PropertyToID("_BoltColor");

		private readonly List<LightningStrike> strikes = new List<LightningStrike>();
		private readonly List<LightningStrike> fresh = new List<LightningStrike>();
		private readonly List<(double at, WeatherAudioCue cue, float volume)> thunder = new List<(double, WeatherAudioCue, float)>();
		private readonly List<Vector3> trunk = new List<Vector3>();
		private readonly List<List<Vector3>> branches = new List<List<Vector3>>();
		private readonly List<Vector3> vertices = new List<Vector3>();
		private readonly List<Vector2> uvs = new List<Vector2>();
		private readonly List<Color> colors = new List<Color>();
		private readonly List<int> indices = new List<int>();
		private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
		private Mesh mesh;
		private double lastTime = double.NaN;
		private readonly LightningAir air = new LightningAir();

		/// <summary>0..1 flash brightness now.</summary>
		public float Flash { get; private set; }

		public int ActiveBolts { get; private set; }

		/// <summary>Strikes seen since the presenter started.</summary>
		public int TotalStrikes { get; private set; }

		public void Reset()
		{
			strikes.Clear();
			thunder.Clear();
			air.Clear();
			lastTime = double.NaN;
			Flash = 0f;
		}

		/// <summary>
		/// What the game calls: every strike decided by the weather where it is (<see cref="LightningAir"/>),
		/// read in the context's scene, so each one happens, and where, for every player alike.
		/// </summary>
		public void Update(WeatherTimeline timeline, uint tick, double now, Vector3 viewer, Camera camera, Material material, in WeatherContext context)
		{
			air.Bind(timeline, context.Settings, context.Scene);
			Update(timeline, tick, now, viewer, camera, material, context.Sample, air);
		}

		/// <summary>The same with the viewer's weather standing in for everywhere's: a test bed with no scene.</summary>
		public void Update(WeatherTimeline timeline, uint tick, double now, Vector3 viewer, Camera camera, Material material, in WeatherSample around)
		{
			Update(timeline, tick, now, viewer, camera, material, around, null);
		}

		private void Update(WeatherTimeline timeline, uint tick, double now, Vector3 viewer, Camera camera, Material material, in WeatherSample around, LightningAir where)
		{
			if (double.IsNaN(lastTime) || now < lastTime || now - lastTime > 5.0)
			{
				lastTime = now;
			}
			fresh.Clear();
			SkySchedule.Lightning(timeline, tick, lastTime, now, viewer, fresh, around, null, where);
			lastTime = now;
			foreach (LightningStrike strike in fresh)
			{
				strikes.Add(strike);
				TotalStrikes++;
				float distance = Vector3.Distance(new Vector3(strike.Ground.x, viewer.y, strike.Ground.z), viewer);
				WeatherAudioCue cue = distance < 1200f ? WeatherAudioCue.ThunderNear : WeatherAudioCue.ThunderFar;
				float volume = Mathf.Clamp01(1.2f - distance / 5000f) * strike.Intensity;
				thunder.Add((strike.Time + distance / SpeedOfSound, cue, volume));
			}
			for (int i = thunder.Count - 1; i >= 0; i--)
			{
				if (now >= thunder[i].at)
				{
					WeatherClient.RaiseAudioCue(thunder[i].cue, thunder[i].volume);
					thunder.RemoveAt(i);
				}
			}

			float flash = 0f;
			vertices.Clear();
			uvs.Clear();
			colors.Clear();
			indices.Clear();
			ActiveBolts = 0;
			/* Under water the sky is not seen (Jim, 2026-10-06: strikes were visible from under the sea): no bolts, and
			 * of the flash only a dim, diffuse brightening comes down through the surface, less the deeper the eye. */
			float underwater = 1f;
			bool submerged = false;
			if (camera != null)
			{
				Vector3 eye = camera.transform.position;
				if (SurfaceWater.IsUnder(eye))
				{
					submerged = true;
					float depth = SurfaceWater.TryGetSurfaceAt(eye.x, eye.z, out float surface) ? Mathf.Max(0f, surface - eye.y) : 0f;
					underwater = 0.25f * Mathf.Exp(-depth / 6f);
				}
			}
			for (int i = strikes.Count - 1; i >= 0; i--)
			{
				LightningStrike strike = strikes[i];
				double age = now - strike.Time;
				if (age > 1.5)
				{
					strikes.RemoveAt(i);
					continue;
				}
				if (age < 0.0)
				{
					continue;
				}
				// A double flicker, fading quickly; farther strikes flash less.
				float distance = Vector3.Distance(strike.Ground, viewer);
				float nearness = Mathf.Clamp01(1.3f - distance / 4000f);
				float pulse = Mathf.Exp(-(float)age * 7f) + 0.6f * Mathf.Exp(-Mathf.Abs((float)age - 0.12f) * 25f);
				flash = Mathf.Max(flash, pulse * nearness * strike.Intensity);
				if (age < BoltSeconds && camera != null && !submerged)
				{
					float alpha = (1f - (float)age / BoltSeconds) * strike.Intensity;
					SkySchedule.BoltPath(strike, trunk, branches);
					float width = Mathf.Lerp(2f, 8f, Mathf.Clamp01(distance / 3000f));
					AddRibbon(trunk, width, alpha, camera.transform.position);
					foreach (List<Vector3> branch in branches)
					{
						AddRibbon(branch, width * 0.5f, alpha * 0.7f, camera.transform.position);
					}
					ActiveBolts++;
				}
			}
			Flash = Mathf.Clamp01(flash * underwater);

			if (mesh == null)
			{
				mesh = new Mesh { name = "Lightning", hideFlags = HideFlags.DontSave };
				mesh.MarkDynamic();
			}
			mesh.Clear();
			if (vertices.Count > 0)
			{
				mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
				mesh.SetVertices(vertices);
				mesh.SetUVs(0, uvs);
				mesh.SetColors(colors);
				mesh.SetTriangles(indices, 0, false);
				mesh.RecalculateBounds();
			}
		}

		private void AddRibbon(List<Vector3> points, float width, float alpha, Vector3 cameraPosition)
		{
			for (int i = 0; i < points.Count - 1; i++)
			{
				Vector3 a = points[i], b = points[i + 1];
				Vector3 toCamera = (cameraPosition - (a + b) * 0.5f).normalized;
				Vector3 side = Vector3.Cross(b - a, toCamera).normalized * width * 0.5f;
				int start = vertices.Count;
				vertices.Add(a - side);
				vertices.Add(a + side);
				vertices.Add(b + side);
				vertices.Add(b - side);
				uvs.Add(new Vector2(0f, 0f));
				uvs.Add(new Vector2(1f, 0f));
				uvs.Add(new Vector2(1f, 1f));
				uvs.Add(new Vector2(0f, 1f));
				var color = new Color(1f, 1f, 1f, alpha);
				colors.Add(color);
				colors.Add(color);
				colors.Add(color);
				colors.Add(color);
				indices.Add(start);
				indices.Add(start + 1);
				indices.Add(start + 2);
				indices.Add(start);
				indices.Add(start + 2);
				indices.Add(start + 3);
			}
		}

		public void Draw(Camera camera, Material material)
		{
			if (mesh == null || mesh.vertexCount == 0 || material == null)
			{
				return;
			}
			block.SetColor(BoltColorId, new Color(0.8f, 0.85f, 1f, 3f));
			var rp = new RenderParams(material) { camera = camera, matProps = block, worldBounds = mesh.bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
			Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
		}

		public void Dispose()
		{
			if (mesh != null)
			{
				if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);
				mesh = null;
			}
		}
	}

	/// <summary>
	/// What a distant storm drops, and what a storm's outflow lifts: a volume under each of the nearest
	/// cells beyond the particle field, in the shape the cell covers the ground in.
	/// </summary>
	/// <remarks>
	/// <para>
	/// It was an open cylinder 1300 m tall, a third wider than the cell whatever the cell's shape,
	/// standing up from the camera's own height with streaks scrolled round its wall. A shower, a squall
	/// line and a haboob all came out as the same straight-sided column, because that is what was drawn.
	/// </para>
	/// <para>
	/// Now each is ray-marched (FishCurtain.shader) through a box laid round its footprint: a front's
	/// box turned along its line, everything else's along the world's axes. What fills it is the cell's
	/// own coverage — the shader carries a twin of <see cref="StormCell.Coverage"/> — times the storm's
	/// weather at its heart, as thick as <see cref="AirPhysics.PrecipitationExtinction"/> says that
	/// takes light out, and seen through by Beer and Lambert. Nothing is made opaque by hand: a
	/// downpour a few kilometres across is a grey wall because a few kilometres of downpour is.
	/// </para>
	/// <para>
	/// Three ways to fill it, from what falls and the shape it falls in:
	/// <list type="bullet">
	/// <item>What falls from cloud — rain, snow, hail, ash — hangs from the storm's own base down to the
	/// ground, and is carried downwind of the storm on its way down (see <see cref="Drift"/>). That base
	/// is the storm's anatomy's (<see cref="StormAnatomy"/>), the one the sky draws its cloud down to
	/// (<see cref="WeatherMap"/>), and the rain falls only where that cloud is drawn — so a storm past
	/// the far edge of the weather map drops nothing out of a clear sky. A supercell's rain and hail
	/// fall on its forward flank, not round its tornado.</item>
	/// <item>Dust that anything but a vortex raises is a storm's outflow driving a wall before it: a
	/// haboob, as tall as the outflow is deep (<see cref="StormPhysics.OutflowDepth"/>), densest on the
	/// ground, its top boiling in billows.</item>
	/// <item>Dust under a vortex is a whirl, leaning, wandering and twisting as it turns.</item>
	/// </list>
	/// </para>
	/// </remarks>
	public sealed class CurtainPresenter
	{
		/// <summary>Inside this much of the camera the particle field and the weather's own fog show what is falling, and the march starts beyond it.</summary>
		public const float NearFieldMeters = 120f;

		/// <summary>The long strides through a curtain's box. The march shortens them through an edge it has just found and wherever the medium is thick.</summary>
		public const int Strides = 24;

		/// <summary>
		/// How far below the ground under a cell's centre its box goes on, so what falls reaches a
		/// valley lower than the middle of the storm. The scene's depth stops it on the ground itself.
		/// Not over the sea, below which nothing that falls is seen.
		/// </summary>
		public const float BelowGroundMeters = 150f;

		/// <summary>
		/// The flattest a shaft is ever drawn, as metres of drift at its foot per metre of fall. The
		/// linear shear the drift assumes puts the whole of the storm's gust at the ground; a real
		/// outflow is only a few hundred metres deep, and a slow-falling snow carried the full way would
		/// lie along the ground kilometres long.
		/// </summary>
		public const float MaxDriftPerMetre = 1.2f;

		/// <summary>How the curtain is filled: <c>_CurtainForm.y</c>.</summary>
		public enum Style
		{
			/// <summary>Rain, snow, hail or ash falling from a cloud base.</summary>
			Falling = 0,
			/// <summary>The dust an outflow drives before it: a haboob.</summary>
			DustWall = 1,
			/// <summary>The dust a vortex lifts: a dust devil.</summary>
			DustWhirl = 2,
		}

		private static readonly int CentreId = Shader.PropertyToID("_CurtainCentre");
		private static readonly int ShapeId = Shader.PropertyToID("_CurtainShape");
		private static readonly int FormId = Shader.PropertyToID("_CurtainForm");
		private static readonly int FallId = Shader.PropertyToID("_CurtainFall");
		private static readonly int AmountId = Shader.PropertyToID("_CurtainAmount");
		private static readonly int ColorId = Shader.PropertyToID("_CurtainColor");
		private static readonly int MarchId = Shader.PropertyToID("_CurtainMarch");
		private static readonly int BoxId = Shader.PropertyToID("_CurtainBox");
		private static readonly int NoiseId = Shader.PropertyToID("_CurtainNoise");
		private Mesh box;
		private readonly StormFrames storms = new StormFrames();
		private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
		private static readonly int StormId = Shader.PropertyToID("_CurtainStorm");
		private readonly List<Curtain> nearest = new List<Curtain>();

		/// <summary>One curtain to draw this frame: a storm, what falls from it, and the footprint it falls in.</summary>
		private struct Curtain
		{
			public StormCell Cell;
			/// <summary>How far off its nearest part is, roughly, m.</summary>
			public float Distance;
			public WeatherFrame Frame;
			public WeatherSubstance Substance;
			/// <summary>What falls at its heart now, 0..1.</summary>
			public float Amount;
			public PrecipitationKind Kind;
			public Style Style;
			/// <summary>It falls from a storm's own cloud, which the weather map lays out and the sky draws.</summary>
			public bool OwnCloud;
			public StormAnatomy Anatomy;
			/// <summary>The middle of what falls, world x/z.</summary>
			public Vector2 Centre;
			/// <summary>The shape it falls in: the cell's own, or a supercell's forward flank.</summary>
			public StormCell Footprint;
			/// <summary>The air it is worked out in: the storm's own (StormCellAir), or the viewer's without a scene.</summary>
			public WeatherSample Air;
		}

		public int Drawn { get; private set; }

		/// <summary>The unit cube, −0.5 to 0.5. Drawn from both sides, so which way its faces wind does not matter.</summary>
		public static Mesh BuildBox()
		{
			var vertices = new Vector3[8];
			for (int i = 0; i < 8; i++)
			{
				vertices[i] = new Vector3((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f);
			}
			int[] indices =
			{
				0, 2, 6, 0, 6, 4, // −x
				1, 5, 7, 1, 7, 3, // +x
				0, 4, 5, 0, 5, 1, // −y
				2, 3, 7, 2, 7, 6, // +y
				0, 1, 3, 0, 3, 2, // −z
				4, 6, 7, 4, 7, 5, // +z
			};
			var mesh = new Mesh { name = "Curtain", hideFlags = HideFlags.DontSave };
			mesh.vertices = vertices;
			mesh.SetTriangles(indices, 0);
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>
		/// How far the foot of a shaft is carried from under its top, m on the ground: the wind at the
		/// ground relative to the storm, over the time a drop takes to fall, halved.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A picture of a shaft at one moment is every drop falling from a source that moves with the
		/// storm, so what slants it is the wind RELATIVE to the storm, not the wind: rain from a storm
		/// carried exactly with its wind comes straight down in the picture, however hard it blows. A
		/// storm lags its steering flow and its own gusts drive the wind at the ground past it, so the
		/// drops leave the base moving with the storm and meet more and more of that difference on the
		/// way down. Taken as rising evenly from nothing at the base to all of it at the ground, the
		/// shaft bends as a parabola, upright under the cloud and leaning over toward its foot, and the
		/// foot has moved (wind − storm)·(base ÷ fall speed) ÷ 2.
		/// </para>
		/// <para>
		/// Hail barely leans, rain leans, snow and ash stream out. Held to <see cref="MaxDriftPerMetre"/>.
		/// </para>
		/// </remarks>
		/// <param name="wind">The wind at the ground at the storm's heart, m/s, world x/z.</param>
		/// <param name="storm">The storm's own motion, m/s.</param>
		/// <param name="baseMeters">How far the drops fall from the base to the ground.</param>
		/// <param name="fallSpeed">How fast they fall, m/s.</param>
		public static Vector2 Drift(Vector2 wind, Vector2 storm, float baseMeters, float fallSpeed)
		{
			Vector2 foot = (wind - storm) * (0.5f * Mathf.Max(0f, baseMeters) / Mathf.Max(0.1f, fallSpeed));
			return Vector2.ClampMagnitude(foot, MaxDriftPerMetre * Mathf.Max(0f, baseMeters));
		}

		/// <summary>
		/// A cumulonimbus's optical depth, top to base: about 200.
		/// </summary>
		/// <remarks>
		/// Deep convection starts at an optical depth of 23 in ISCCP's cloud classes (Rossow and
		/// Schiffer 1999), and MODIS retrievals of cumulonimbus cores run into the retrieval's own
		/// ceiling of 100–150 (Platnick et al. 2003); from the water in one — a gram or two a cubic
		/// metre over eight kilometres, drops of 10–15 µm — it is τ = 3·LWP/(2ρr), several hundred.
		/// 200 lets down about 4 % of the light on the storm's top (<see cref="StormCloudThrough"/>),
		/// inside the 1–5 % of noon that is measured under a supercell.
		/// </remarks>
		public const float StormCloudOpticalDepth = 200f;

		/// <summary>The asymmetry of a storm cloud's drops, g: cloud drops of ten microns or so throw 0.85 of their light forward (Hansen and Travis 1974).</summary>
		public const float StormCloudAsymmetry = 0.85f;

		/// <summary>
		/// What a storm's cloud lets down of the light on its top, as diffuse light: the two-stream
		/// transmission of a thick conservative cloud, 1/(1 + ¾(1 − g)τ) — the form the clouds' own
		/// march uses for what gets through a column (FishCloudVolume.hlsl). The shader carries it
		/// as CURTAIN_STORM_THROUGH.
		/// </summary>
		public static float StormCloudThrough => 1f / (1f + 0.75f * (1f - StormCloudAsymmetry) * StormCloudOpticalDepth);

		/// <summary>
		/// The share of the sky above a point <paramref name="gapMetres"/> under a cloud base that it
		/// sees open past the base's edge, <paramref name="edgeMetres"/> off: the sine of the edge's
		/// elevation from the point, which is the share of the upper half of all directions lying
		/// between the horizon and it.
		/// </summary>
		/// <remarks>
		/// The clouds' own march lights the rain hanging under a base by the same figure (half of it,
		/// as a share of all directions). None just under the base, more the further down, all of it
		/// at the edge: a shaft pales toward the ground and toward its sides, where it sees the
		/// horizon under the storm.
		/// </remarks>
		public static float OpenSkyShare(float gapMetres, float edgeMetres)
		{
			float gap = Mathf.Max(0f, gapMetres);
			float edge = Mathf.Max(0f, edgeMetres);
			return gap / Mathf.Max(1e-3f, Mathf.Sqrt(gap * gap + edge * edge));
		}

		/// <summary>
		/// How far off a storm's cloud ends, from its cover at a point (<paramref name="here"/>) and
		/// <paramref name="reachMetres"/> further on (<paramref name="there"/>): where the cover, carried
		/// on as it runs between the two, falls through a half. None when the point is already past
		/// the edge; four reaches at most, when it does not fall at all.
		/// </summary>
		/// <remarks>One more read of the weather map a sample, instead of a march of it toward the edge.</remarks>
		public static float EdgeDistance(float here, float there, float reachMetres)
		{
			if (here <= 0.5f)
			{
				return 0f;
			}
			float fall = here - there;
			return fall > 1e-3f ? Mathf.Min(4f * reachMetres, reachMetres * (here - 0.5f) / fall) : 4f * reachMetres;
		}

		/// <summary>
		/// The diffuse light at a point in a shaft of rain, per unit of what it takes out of the view:
		/// the mean radiance arriving there from every direction.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Water takes out next to nothing of the visible light it scatters (its absorption index
		/// there is about 10⁻⁹; across a 2 mm drop it keeps all but a few parts in 10⁵), so a shaft is
		/// a conservative scatterer, and a conservative scatterer sends on the mean of the light it
		/// stands in whatever its phase function — once or many times over. The direct sun comes on
		/// top of this, only where its ray gets in under the base; the shader adds it.
		/// </para>
		/// <para>
		/// Half of all directions are above: the storm's underside, which lets down
		/// <see cref="StormCloudThrough"/> of the sun and sky on its top, and the open sky low down
		/// past the base's edge (<see cref="OpenSkyShare"/>), which is the horizon's light. Half are
		/// below: the ground, in the storm's shade under it and in the open past it — seen more the
		/// higher the point stands, by the same geometry.
		/// </para>
		/// <para>
		/// It was the viewer's ambient straight up, 0.7 of it under a storm, times the rain's fog
		/// colour: an albedo of 0.6 for water, nothing from the side, and the viewer's own sky — so a
		/// storm twenty kilometres off was as dark as the shade the viewer stood in, and a twentieth
		/// of the cloud base over it.
		/// </para>
		/// </remarks>
		/// <param name="sky">The open sky's mean radiance overhead: what a white surface facing up shows under it.</param>
		/// <param name="horizon">The open sky low down: the horizon's own light.</param>
		/// <param name="ground">What the open ground sends back: its albedo times the sun and sky on it.</param>
		/// <param name="sun">The sun's light, as the light that lights the world carries it.</param>
		/// <param name="sunUp">The sine of the sun's elevation.</param>
		/// <param name="gapMetres">How far under the base the point hangs.</param>
		/// <param name="heightMetres">How far above the ground it stands.</param>
		/// <param name="edgeMetres">How far off the storm's cloud ends (<see cref="EdgeDistance"/>).</param>
		/// <param name="overhead">How much of the storm's cloud stands over it, 0..1.</param>
		public static Color DiffuseLight(Color sky, Color horizon, Color ground, Color sun, float sunUp, float gapMetres, float heightMetres, float edgeMetres, float overhead)
		{
			float open = OpenSkyShare(gapMetres, edgeMetres);
			float openGround = OpenSkyShare(heightMetres, edgeMetres);
			float through = Mathf.Lerp(1f, StormCloudThrough, Mathf.Clamp01(overhead));
			Color onOpen = sky + sun * Mathf.Max(0f, sunUp);
			Color upper = horizon * open + onOpen * (through * (1f - open));
			Color lower = ground * (openGround + (1f - openGround) * through);
			Color mean = (upper + lower) * 0.5f;
			mean.a = 1f;
			return mean;
		}

		/// <summary>
		/// What falling stuff does to the colour of the light it scatters, as the curtain's albedo:
		/// nothing, for anything condensed, and its own colour for dust.
		/// </summary>
		/// <remarks>
		/// Rain, snow and hail — of water, or of methane, ammonia or nitrogen on another world — are
		/// clear or white: none of them absorbs visibly across the size of a drop or a flake. The
		/// curtain took the rain's fog colour instead, (0.55, 0.6, 0.66), which is an albedo of six
		/// tenths and a tint the sky's light already carries. Ash and sand absorb, and are the colour
		/// they are.
		/// </remarks>
		public static Color ScatteringColour(PrecipitationKind kind, Color dust)
		{
			bool condensed = kind == PrecipitationKind.Rain || kind == PrecipitationKind.Snow || kind == PrecipitationKind.Hail;
			return condensed ? Color.white : new Color(dust.r, dust.g, dust.b, 1f);
		}

		/// <param name="around">The weather at the viewer: each distant storm rains what its kind of storm makes in this air.</param>
		/// <param name="body">The world the viewer is on, for how fast things fall there. Null is our own.</param>
		/// <param name="map">The storms' cloud as the sky draws it: rain is never drawn where its cloud is not.</param>
		/// <param name="tick">The fractional server tick it is drawn at (WeatherPresentation.PresentTick): the storms move every frame.</param>
		/// <param name="time">The shared sky clock, wrapped (WorldMotion.SkyWrapSeconds).</param>
		/// <param name="settings">The scene's settings, and <paramref name="scene"/> the scene: each storm's own air is read in them (StormCellAir).</param>
		public void Draw(WeatherTimeline timeline, double tick, Camera camera, Material material, WeatherRenderProfile profile, int limit, float time,
			in WeatherSample around, WorldBody body = null, WeatherMap map = null, FishMMO.Shared.WorldSceneSettings settings = null,
			UnityEngine.SceneManagement.Scene scene = default)
		{
			Drawn = 0;
			if (timeline == null || camera == null || material == null || limit <= 0 || timeline.SceneMode != WeatherSceneMode.Own)
			{
				return;
			}
			if (box == null)
			{
				box = BuildBox();
			}
			Vector3 viewer = camera.transform.position;
			var viewer2 = new Vector2(viewer.x, viewer.z);
			// The world time it is drawn at: where the storms stand and how strong they are, held with the world.
			double now = timeline.WorldSecondsAt(tick);
			storms.Reset(around);
			nearest.Clear();
			foreach (StormCell cell in timeline.Cells)
			{
				/* What it rains, how much, from what base, drifting on what wind: all from the storm's own
				 * air (StormCellAir), read once for its life, so every player sees the same shaft — from the
				 * viewer's air it changed as each viewer walked, and differed between them. Without a scene
				 * (tests, probes) the viewer's air, as before. */
				StormCellAir.Air own = settings != null ? StormCellAir.Of(timeline, settings, scene, cell) : null;
				WeatherSample air = own != null ? own.Sample : around;
				WeatherFrame frame = own != null ? own.Frames.Of(cell.Kind, out WeatherSubstance substance) : storms.Of(cell.Kind, out substance);
				float amount = frame[WeatherChannel.Precipitation] * cell.EnvelopeAtSeconds(now) * cell.PeakIntensity;
				if (amount < 0.05f)
				{
					continue;
				}
				PrecipitationKind kind = frame.DominantPrecipitation;
				Style style = kind == PrecipitationKind.Sand
					? (cell.Shape == StormCellShape.Funnel ? Style.DustWhirl : Style.DustWall)
					: Style.Falling;
				// A whirl of dust is the vortex's to draw (VortexPresenter): its column, spun as the
				// vortex spins it. Drawn here as well, a dust devil stood twice.
				if (style == Style.DustWhirl)
				{
					continue;
				}
				Vector2 centre = cell.CentreAtSeconds(now);
				StormCell footprint = cell;
				StormAnatomy anatomy = default;
				bool ownCloud = false;
				if (style == Style.Falling)
				{
					/* What falls, falls from the storm's own cloud, which the weather map lays out from
					 * its anatomy: the same anatomy here, from the same air, so the rain hangs from the
					 * base the sky draws over it. A supercell's cell is its tornado, on the storm's rear
					 * flank; its rain and hail fall on the forward flank, ahead of the tornado, and not
					 * in a ring round it. */
					anatomy = StormAnatomy.Of(cell.Kind, air, new Vector2(cell.VelocityX, cell.VelocityZ), timeline.LatitudeDegrees, cell.RadiusMeters);
					ownCloud = anatomy.Valid;
					if (ownCloud && cell.Kind == StormKind.Supercell && anatomy.RainRadius > 0f)
					{
						centre += anatomy.BodyOffset + anatomy.RainOffset;
						footprint.Shape = StormCellShape.Disc;
						footprint.RadiusMeters = anatomy.RainRadius;
						footprint.ExtentMeters = 0f;
					}
					/* Rain never shows without its cloud. Past the far edge of the weather map the sky
					 * draws no storm cloud, so nothing may fall there either. A storm with a cloud of
					 * its own is faded with that cloud, sample by sample, in the shader; one whose cloud
					 * is only its air's shield is faded here. */
					float reach = map != null ? map.CloudReach(centre) : 1f;
					if (reach <= 0.01f)
					{
						continue;
					}
					if (!ownCloud)
					{
						amount *= reach;
						if (amount < 0.05f)
						{
							continue;
						}
					}
				}
				Vector2 offset = viewer2 - centre;
				/* The camera is in this one: the particle field and the weather's own fog show it. Asked
				 * of the shape and not the distance to the centre, which is only a disc's test: a front's
				 * centre can be ten kilometres down its line from someone standing in its dust, and
				 * someone in a hurricane's eye is nearer its centre than anyone and in none of its rain,
				 * with the eyewall standing round them. */
				if (footprint.Coverage(offset) >= 0.5f)
				{
					continue;
				}
				nearest.Add(new Curtain
				{
					Cell = cell,
					// Nearest by its nearest part, roughly: a front's centre says little about where its wall is.
					Distance = Mathf.Max(0f, offset.magnitude - footprint.ReachMeters),
					Frame = frame,
					Substance = substance,
					Amount = amount,
					Kind = kind,
					Style = style,
					OwnCloud = ownCloud,
					Anatomy = anatomy,
					Centre = centre,
					Footprint = footprint,
					Air = air,
				});
			}
			nearest.Sort((a, b) => a.Distance.CompareTo(b.Distance));
			Texture noise = profile.CloudShape;
			for (int i = 0; i < nearest.Count && Drawn < limit; i++)
			{
				Curtain curtain = nearest[i];
				StormCell cell = curtain.Cell;
				StormCell footprint = curtain.Footprint;
				WeatherFrame frame = curtain.Frame;
				WeatherSubstance substance = curtain.Substance;
				PrecipitationKind kind = curtain.Kind;
				Style style = curtain.Style;
				float amount = curtain.Amount;
				WeatherChannel channel = kind == PrecipitationKind.Snow ? WeatherChannel.SnowWeight
					: kind == PrecipitationKind.Hail ? WeatherChannel.HailWeight
					: kind == PrecipitationKind.Ash ? WeatherChannel.AshWeight
					: kind == PrecipitationKind.Sand ? WeatherChannel.SandWeight
					: WeatherChannel.RainWeight;
				PrecipitationLook look = profile.LookOf(channel);
				bool water = kind == PrecipitationKind.Rain || kind == PrecipitationKind.Snow || kind == PrecipitationKind.Hail;
				// The substance only when it is what is falling here: the heaviest one in the storm's
				// air can be the sand under a thunderstorm whose rain is what the curtain shows.
				bool itsOwn = substance != null && (kind == PrecipitationKind.Sand ? substance.Cover == WeatherCoverKind.Sand
					: kind == PrecipitationKind.Ash ? substance.Cover == WeatherCoverKind.Ash
					: substance.Condenses);
				/* The streaks fall at the kind's own speed on this world, in metres a second, and the shader
				 * multiplies it by the clock — so it must be constant for the cell. Its drops' size is the
				 * cell's own air's (StormCellAir), read once for its life: from the viewer's air, which
				 * drifts as the viewer walks and the field moves, every change in speed times hours of
				 * clock put every streak somewhere else. */
				StormCellAir.Air cellAir = StormCellAir.Of(timeline, settings, scene, cell);
				float dropSize = cellAir != null ? cellAir.Frames.Of(cell.Kind)[WeatherChannel.DropSize] : frame[WeatherChannel.DropSize];
				float fallSpeed = Mathf.Lerp(look.FallSpeed.x, look.FallSpeed.y, dropSize)
					* (itsOwn ? Mathf.Max(0.01f, substance.FallSpeedScale) : 1f)
					* SurfacePhysics.TerminalSpeedScale(SurfacePhysics.Gravity(body), SurfacePhysics.AirDensity(body), PrecipitationField.TraitsOf(channel).Fine);

				Vector2 centre = curtain.Centre;
				float ground = GroundUnder(centre, out bool overSea);
				float radius = Mathf.Max(1f, footprint.RadiusMeters);
				float extent = Mathf.Max(0f, footprint.ExtentMeters);

				/* How high it stands. Rain from the base of the storm's own cloud; a wall of dust as
				 * deep as the storm's outflow in its own air (the same the storm's size
				 * came from); a whirl as tall as the vortices draw a dust devil's. */
				float top;
				switch (style)
				{
					case Style.DustWall:
						top = StormPhysics.OutflowDepth(curtain.Air.OpenColumn);
						break;
					case Style.DustWhirl:
						top = Mathf.Max(20f, extent * 1.4f);
						break;
					default:
						if (curtain.OwnCloud)
						{
							// The storm's own base, which the sky draws over it: the condensation level
							// of the moist air it feeds on, lower than the open air's.
							top = Mathf.Max(150f, curtain.Anatomy.BaseMetres);
						}
						else
						{
							// A storm with no cloud of its own falls from the cloud its air makes, at
							// the higher of its base and the open air's: at the lower it hung in clear
							// sky under a cloud it never reached.
							top = Mathf.Max(150f, Mathf.Max(frame[WeatherChannel.CloudBase] * 4000f, curtain.Air.OpenColumn.Base));
						}
						break;
				}

				Vector2 drift = Vector2.zero;
				if (style == Style.Falling)
				{
					// The wind the rain falls through: the air round the storm, not the storm's own heart,
					// whose frame carries its gust front and, in a supercell, the spin of its core — a
					// shaft leaned by those streamed out kilometres. Relative to the storm, which moves
					// with its steering wind.
					drift = Drift(curtain.Air.OpenAir.Wind, new Vector2(cell.VelocityX, cell.VelocityZ), top, fallSpeed);
				}

				/* The footprint, in the frame the box is laid out in: a front's own — forward across its
				 * line, along it — and the world's for the rest. Every shape reaches no further than
				 * StormCell.Coverage lets it, and a little more for the ragged edge the shader adds. */
				Vector2 forward = footprint.Shape == StormCellShape.Front ? footprint.Facing : Vector2.right;
				var along = new Vector2(-forward.y, forward.x);
				float minF, maxF, halfA, size;
				switch (footprint.Shape)
				{
					case StormCellShape.Front:
						minF = -radius * 1.6f;
						maxF = radius * 0.45f;
						halfA = Mathf.Max(radius, extent);
						size = radius;
						break;
					case StormCellShape.Eyewall:
						minF = -radius;
						maxF = radius;
						halfA = radius;
						// The wall's own thickness, which is what its rain's structure is the size of.
						size = Mathf.Max(radius - Mathf.Clamp(extent, 0f, radius * 0.6f), radius * 0.25f);
						break;
					case StormCellShape.Funnel:
						float reach = Mathf.Max(extent, radius * 1.5f);
						minF = -reach;
						maxF = reach;
						halfA = reach;
						size = reach;
						break;
					default:
						minF = -radius;
						maxF = radius;
						halfA = radius;
						size = radius;
						break;
				}
				float minA = -halfA, maxA = halfA;
				float margin;
				switch (style)
				{
					case Style.DustWall:
						margin = 0.15f * radius;
						break;
					case Style.DustWhirl:
						// Three times as wide at the top, and swinging round over its foot.
						minF *= 3f; maxF *= 3f; minA *= 3f; maxA *= 3f;
						margin = 0.35f * size;
						break;
					default:
						// Half the shape's size: the body noise carries the ragged sides out by up to a
						// quarter of it, and the shader fades the curtain out over the box's outer tenth
						// or so (CurtainBoxWindow). At a quarter the rain reached the faces and was cut off
						// square — a translucent block with straight edges against the sky.
						margin = 0.5f * size;
						// The foot is carried off: the box holds the footprint at the base and where the
						// fall ends, the parabola staying between the two.
						float lowest = Mathf.Min(1.5f, (top + (overSea ? 0f : BelowGroundMeters)) / top);
						Vector2 foot = drift * (lowest * lowest);
						float footF = Vector2.Dot(foot, forward), footA = Vector2.Dot(foot, along);
						minF = Mathf.Min(minF, minF + footF);
						maxF = Mathf.Max(maxF, maxF + footF);
						minA = Mathf.Min(minA, minA + footA);
						maxA = Mathf.Max(maxA, maxA + footA);
						break;
				}
				minF -= margin; maxF += margin; minA -= margin; maxA += margin;
				float bottom = ground - (overSea ? 0f : BelowGroundMeters);
				// A wall's billows carry its top up to 1.28 of its height.
				float roof = ground + top * (style == Style.DustWall ? 1.3f : 1f) + 10f;
				float sizeF = maxF - minF, sizeA = maxA - minA, sizeY = roof - bottom;
				Vector2 middle = centre + forward * (0.5f * (minF + maxF)) + along * (0.5f * (minA + maxA));
				var position = new Vector3(middle.x, 0.5f * (bottom + roof), middle.y);
				var matrix = new Matrix4x4(
					new Vector4(forward.x * sizeF, 0f, forward.y * sizeF, 0f),
					new Vector4(0f, sizeY, 0f, 0f),
					new Vector4(along.x * sizeA, 0f, along.y * sizeA, 0f),
					new Vector4(position.x, position.y, position.z, 1f));
				var bounds = new Bounds(position, new Vector3(
					Mathf.Abs(forward.x) * sizeF + Mathf.Abs(along.x) * sizeA, sizeY,
					Mathf.Abs(forward.y) * sizeF + Mathf.Abs(along.y) * sizeA));
				/* The shader pins the box to the far plane so a storm past the far clip is still seen;
				 * the culling has to agree, or a box wholly beyond it is dropped before it is drawn.
				 * Reach the bounds in toward the camera, along the way to the box, to just inside it. */
				float far = camera.farClipPlane * 0.9f;
				if (bounds.SqrDistance(viewer) > far * far)
				{
					bounds.Encapsulate(viewer + (position - viewer).normalized * far);
				}

				// What it does to the light it scatters: nothing, for anything condensed (see
				// ScatteringColour), and its own colour for dust. The shader lights it with the day
				// round the storm, which already carries the sky's hue.
				Color color = ScatteringColour(kind, itsOwn ? substance.FogColor : look.FogColor);
				// Water's share of the extinction and dust's, as AirPhysics.PrecipitationExtinction
				// weighs them, and how strongly each throws light forward: big drops most, dust less.
				float waterShare = frame[WeatherChannel.RainWeight] + 5f * frame[WeatherChannel.SnowWeight] + 0.5f * frame[WeatherChannel.HailWeight];
				float dustShare = 0.03f * frame[WeatherChannel.AshWeight] + 0.04f * frame[WeatherChannel.SandWeight];
				float forwardScatter = kind == PrecipitationKind.Rain ? 0.6f
					: kind == PrecipitationKind.Hail ? 0.55f
					: kind == PrecipitationKind.Snow ? 0.5f
					: kind == PrecipitationKind.Ash ? 0.45f
					: 0.55f;
				bool flakes = kind == PrecipitationKind.Snow || kind == PrecipitationKind.Ash;

				block.SetVector(CentreId, new Vector4(centre.x, ground, centre.y, top));
				block.SetVector(ShapeId, new Vector4(forward.x, forward.y, radius, extent));
				block.SetVector(FormId, new Vector4((float)footprint.Shape, (float)style, flakes ? 1f : 0f, (cell.Seed & 0xFFFFu) / 65536f));
				block.SetVector(FallId, new Vector4(fallSpeed, time, drift.x, drift.y));
				block.SetVector(AmountId, new Vector4(Mathf.Clamp01(amount), waterShare, dustShare, forwardScatter));
				block.SetColor(ColorId, color);
				block.SetVector(MarchId, new Vector4(Strides, NearFieldMeters, noise != null ? 1f : 0f, size));
				block.SetVector(BoxId, new Vector4(middle.x, middle.y, 0.5f * sizeF, 0.5f * sizeA));
				// x 1 when it falls from a storm's own cloud: the shader then lets it fall only under
				// that cloud as the weather map lays it out, and lights it by that cloud. y 1 when the
				// sky has published the day the storm's cloud is lit by (as VortexPresenter asks): the
				// curtain is lit by the same, and seen through the same air.
				block.SetVector(StormId, new Vector4(curtain.OwnCloud && map != null ? 1f : 0f, SkySystem.CloudsReady ? 1f : 0f, 0f, 0f));
				if (noise != null)
				{
					block.SetTexture(NoiseId, noise);
				}
				var rp = new RenderParams(material) { camera = camera, matProps = block, worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
				Graphics.RenderMesh(rp, box, 0, matrix);
				Drawn++;
			}
		}

		/// <summary>
		/// The height what a storm drops reaches the ground at under a point: the terrain, or the sea
		/// over it. The same lookup the vortices stand on.
		/// </summary>
		private static float GroundUnder(Vector2 at, out bool overSea)
		{
			float ground = float.MinValue;
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				Vector3 origin = terrain.GetPosition();
				Vector3 extentOf = terrain.terrainData.size;
				if (at.x < origin.x || at.x > origin.x + extentOf.x || at.y < origin.z || at.y > origin.z + extentOf.z)
				{
					continue;
				}
				ground = Mathf.Max(ground, origin.y + terrain.SampleHeight(new Vector3(at.x, 0f, at.y)));
			}
			overSea = false;
			if (SurfaceWater.TryGetSurfaceAt(at.x, at.y, out float level) && level > ground)
			{
				ground = level;
				overSea = true;
			}
			// Off every terrain with no sea: the datum, where the sea would be.
			return ground > float.MinValue ? ground : 0f;
		}

		public void Dispose()
		{
			if (box != null)
			{
				if (Application.isPlaying) Object.Destroy(box); else Object.DestroyImmediate(box);
				box = null;
			}
		}
	}

	/// <summary>Moving cloud shadows: a cookie on the sun light, redrawn when the cloud cover changes.</summary>
	public sealed class CloudShadowPresenter
	{
		private static readonly int CookieParamsId = Shader.PropertyToID("_CookieParams");
		private static readonly int ShadowAreaId = Shader.PropertyToID("_FishCloudShadowArea");
		private static readonly int ShadowFarDimId = Shader.PropertyToID("_FishCloudShadowFarDim");
		private static readonly int ShadowOriginId = Shader.PropertyToID("_FishCloudShadowOrigin");
		private static readonly int ShadowRightId = Shader.PropertyToID("_FishCloudShadowRight");
		private static readonly int ShadowUpId = Shader.PropertyToID("_FishCloudShadowUp");
		private static readonly int ShadowRawId = Shader.PropertyToID("_FishCloudShadowRaw");
		private static readonly int OverheadDrawId = Shader.PropertyToID("_FishCloudOverheadDraw");
		private static readonly int OverheadRectId = Shader.PropertyToID("_FishCloudOverheadRect");
		private static readonly int OverheadId = Shader.PropertyToID("_FishCloudOverhead");
		private static readonly int OverheadFromId = Shader.PropertyToID("_FishCloudOverheadFrom");
		private RenderTexture cookie;
		private RenderTexture raw;
		private RenderTexture overhead;
		private Vector2 overheadCentre = new Vector2(float.NaN, float.NaN);
		private float overheadFrom = float.NaN;
		private int sinceOverhead = int.MaxValue;
		private Vector2 drawnCentre = new Vector2(float.NaN, float.NaN);
		private int sinceDrawn = int.MaxValue;
		private float drawnCover = -1f;

		/// <summary>The shadow as the light reads it, for a panel to show: what is on the ground should look like this.</summary>
		public Texture Cookie => cookie;
		private Light boundLight;
		private Vector2 offset;

		/// <summary>
		/// Redraws the shadow the clouds throw on the world, by marching the same volume the sky
		/// draws — from each patch of ground toward the light — into a cookie on the sun.
		/// </summary>
		/// <remarks>
		/// The cookie is a window on the world, not a tiling pattern: it covers a square around the
		/// camera and moves with it, so the shadow under a cloud is the shadow of that cloud.
		/// </remarks>
		/// <param name="far">What the light gets through the cloud past the window, on average: what the
		/// cookie fades to at its edge (the light carries no other dimming for cloud).</param>
		public void Update(Light light, Material cloudMaterial, Vector3 viewer, float areaMeters, float strength, int steps, bool enabled, float far = 1f)
		{
			// The overhead map whenever there are clouds to map, whether or not their shadow is drawn:
			// the rain needs it on every tier, and it is a quarter of a cookie's cost.
			if (cloudMaterial != null && SkySystem.CloudsReady)
			{
				DrawOverhead(cloudMaterial, viewer, areaMeters, Mathf.Max(2, steps / 2));
			}
			else
			{
				Shader.SetGlobalVector(OverheadRectId, Vector4.zero);
			}
			if (light == null || cloudMaterial == null || !enabled || !SkySystem.CloudsReady)
			{
				Clear(light);
				return;
			}
			if (cookie == null)
			{
				// A single channel, and a LINEAR one, said so. Asked for as RenderTextureFormat.R8 in a
				// linear-colour project, Unity took it to mean R8_SRGB, which this platform has not got,
				// and fell back to a full RGBA target with a warning on every frame. A shadow is a
				// number, not a colour: R8_UNorm is what it always meant.
				var format = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;
				if (!SystemInfo.IsFormatSupported(format, UnityEngine.Experimental.Rendering.GraphicsFormatUsage.Render))
				{
					format = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
				}
				cookie = new RenderTexture(256, 256, 0, format) { name = "Cloud Shadows", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false, hideFlags = HideFlags.DontSave };
				cookie.Create();
				raw = new RenderTexture(256, 256, 0, format) { name = "Cloud Shadows (raw)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false, hideFlags = HideFlags.DontSave };
				raw.Create();
				sinceDrawn = int.MaxValue;
			}
			// The window is in the light's own plane, because that is the plane the light reads a
			// cookie in. Snapped to a texel, so the shadow does not crawl as the camera moves.
			Transform lightTransform = light.transform;
			Vector3 local = lightTransform.InverseTransformPoint(viewer);
			float texel = areaMeters / cookie.width;
			local.x = Mathf.Round(local.x / texel) * texel;
			local.y = Mathf.Round(local.y / texel) * texel;
			Vector3 centre = lightTransform.TransformPoint(new Vector3(local.x, local.y, 0f));
			// x is the cookie's own texel, which is the footprint its march reads the noise at: the
			// shadow pattern cannot be finer than the map it is drawn into, and must not be blurred
			// coarser than it either.
			cloudMaterial.SetVector(ShadowAreaId, new Vector4(texel, Mathf.Clamp01(strength), areaMeters, steps));
			// As how much darker than full sun, so a material that has never been handed it fades to 1.
			cloudMaterial.SetFloat(ShadowFarDimId, 1f - Mathf.Clamp01(far));
			cloudMaterial.SetVector(ShadowOriginId, centre);
			cloudMaterial.SetVector(ShadowRightId, lightTransform.right);
			cloudMaterial.SetVector(ShadowUpId, lightTransform.up);
			// Marched when the window moves, and otherwise every third frame: a cloud crosses one of
			// these texels in about two seconds, and this is a quarter of a million rays. The window
			// and the offset below are always this frame's, so what is drawn late is only the
			// clouds' own drift, by a few centimetres.
			var centreNow = new Vector2(local.x, local.y);
			sinceDrawn++;
			if (sinceDrawn >= 3 || centreNow != drawnCentre || raw == null)
			{
				Graphics.Blit(null, raw, cloudMaterial, 3);
				// Softened, and faded to nothing at the window's edge: the sun's half degree makes a
				// cloud's shadow ten metres soft from a kilometre up, and a clamped lookup would
				// otherwise drag the window's last texel out to the horizon as streaks.
				cloudMaterial.SetTexture(ShadowRawId, raw);
				Graphics.Blit(null, cookie, cloudMaterial, 6);
				drawnCentre = centreNow;
				sinceDrawn = 0;
			}

			if (boundLight != light)
			{
				Clear(boundLight);
				boundLight = light;
			}
			light.cookie = cookie;
			var data = light.GetComponent<UniversalAdditionalLightData>();
			if (data == null)
			{
				data = light.gameObject.AddComponent<UniversalAdditionalLightData>();
			}
			data.lightCookieSize = new Vector2(areaMeters, areaMeters);
			// The cookie follows the camera: the window's middle sits where the viewer stands, in the
			// light's plane. The offset is that point in WORLD units, and positive.
			//
			// Derived rather than guessed, from URP's own LightCookieManager. It builds
			//     cookieMatrix = Ortho(-0.5..0.5) * uvTransform * worldToLight
			// where uvTransform scales by 1/size and translates by -offset/size, and the shader then
			// takes uv = positionLS * 0.5 + 0.5. Composing those gives
			//     uv = (lightSpaceXY - offset) / size + 0.5
			// so the window's middle lands at uv 0.5 exactly when offset equals it.
			//
			// This was "-local / areaMeters": negated, and four thousand times too small. The cookie
			// therefore sat very nearly still in the light's plane while the window that was marched
			// into it tracked the camera — so the shadow on the ground was some other part of the
			// sky's shadow, it slid as the viewer moved rather than with the clouds, and which part
			// you got depended on where you stood, including how high.
			data.lightCookieOffset = new Vector2(local.x, local.y);
			drawnCover = strength;
		}

		/// <summary>
		/// The map of the cloud standing over each patch of ground round the camera, above the
		/// camera's own height, for what falls from cloud to fall only under it and as hard as the
		/// cloud is thick. The same window as the cookie, marched straight up instead of toward the
		/// light, and redrawn on the same cadence.
		/// </summary>
		private void DrawOverhead(Material cloudMaterial, Vector3 viewer, float areaMeters, int steps)
		{
			if (overhead == null)
			{
				var format = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;
				if (!SystemInfo.IsFormatSupported(format, UnityEngine.Experimental.Rendering.GraphicsFormatUsage.Render))
				{
					format = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
				}
				overhead = new RenderTexture(256, 256, 0, format) { name = "Cloud Overhead", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false, hideFlags = HideFlags.DontSave };
				overhead.Create();
				sinceOverhead = int.MaxValue;
			}
			float texel = areaMeters / overhead.width;
			var centre = new Vector2(Mathf.Round(viewer.x / texel) * texel, Mathf.Round(viewer.z / texel) * texel);
			sinceOverhead++;
			// Measured up from the viewer's height (see the pass), so climbing out of a deck redraws it.
			if (sinceOverhead >= 3 || centre != overheadCentre || !(Mathf.Abs(viewer.y - overheadFrom) < 5f))
			{
				cloudMaterial.SetVector(OverheadDrawId, new Vector4(centre.x, centre.y, areaMeters, steps));
				cloudMaterial.SetFloat(OverheadFromId, viewer.y);
				Graphics.Blit(null, overhead, cloudMaterial, 7);
				overheadCentre = centre;
				overheadFrom = viewer.y;
				sinceOverhead = 0;
			}
			Shader.SetGlobalTexture(OverheadId, overhead);
			Shader.SetGlobalVector(OverheadRectId, new Vector4(centre.x, centre.y, areaMeters, 1f));
		}

		public void Clear(Light light)
		{
			if (light != null && light.cookie == cookie && cookie != null)
			{
				light.cookie = null;
			}
			drawnCover = -1f;
		}

		public void Dispose()
		{
			Clear(boundLight);
			if (cookie != null)
			{
				cookie.Release();
				if (Application.isPlaying) Object.Destroy(cookie); else Object.DestroyImmediate(cookie);
				cookie = null;
			}
			if (raw != null)
			{
				raw.Release();
				if (Application.isPlaying) Object.Destroy(raw); else Object.DestroyImmediate(raw);
				raw = null;
			}
			if (overhead != null)
			{
				overhead.Release();
				if (Application.isPlaying) Object.Destroy(overhead); else Object.DestroyImmediate(overhead);
				overhead = null;
			}
		}
	}
}
