using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The weather's fog layer as the renderer draws it: how thick it is at the ground, how deep, how
	/// far lifted, how soft its top is, how patchy its body, how it drifts — and the one writer of the
	/// shader globals that say so (FishFogLayer.hlsl).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The physics (<see cref="FogLayer"/>) says how deep the fog lies and whether it has lifted; the fog
	/// channel says how much of it there is, which <see cref="AirPhysics.FogExtinction"/> turns into the
	/// light its drops take out of each metre. Everything else here is how a layer of that depth in that
	/// wind looks from inside, and each follows the same two things — the wind and how far the fog has
	/// come:
	/// </para>
	/// <list type="bullet">
	/// <item><b>Its top</b> is a smooth fade of its density with height, not a surface
	/// (<see cref="TopShare"/>): the whole of a thin still mist, which is densest at the ground and thins
	/// all the way up; the upper sixth of a deep, mixed fog, whose water falls away under its inversion;
	/// a third of one the wind rolls, its Kelvin–Helmholtz waves smeared into the mean. Nothing heaves it
	/// or frays it: from above, a fog's top is a soft even whiteness (FishFogLayer.hlsl says why it no
	/// longer billows).</item>
	/// <item><b>Its body.</b> A mist forms first in patches and in the hollows, so its density wanders by
	/// most of an e-folding either way; a thick fog has had the night to mix and is nearly even; a deep
	/// haze through the day's mixed layer is evener still. The patches are banks a few hundred metres
	/// across, and nothing finer.</item>
	/// <item><b>Its motion.</b> It drifts with the air it is made of — the wind at its own height, which
	/// near the ground is a good deal less than the wind the weather reports at ten metres
	/// (<see cref="DriftWind"/>) — and its banks change slowly as they go, over some twenty minutes,
	/// faster in wind.</item>
	/// </list>
	/// <para>
	/// <b>Who draws it.</b> A fog is cloud whose base is the ground, and the cloud march draws it as one
	/// (FishCloudVolume.hlsl): its banks read from the clouds' shape volume, walked into and out of like
	/// any cloud, seen from a hill lying in the low ground with a soft top. Where the march does not run, the froxel
	/// volume and the analytic pass draw the same layer instead, from the same functions
	/// (<see cref="FishVolumetricFogFeature"/>, <see cref="FishHeightFogFeature"/>) — and stand down
	/// whenever the march has drawn it (<see cref="MarchedThisFrame"/>). Wherever it is drawn at all,
	/// the pipeline's distance fog leaves its drops out (<see cref="Drawn"/>).
	/// </para>
	/// <para>
	/// The drift is added up here, frame by frame, on the world's own motion clock, in doubles, and
	/// wrapped to the tile the banks are read at, so they never jump at the wrap.
	/// </para>
	/// </remarks>
	public struct FogLayerView
	{
		/// <summary>The fog's extinction at the ground, 1/m: its drops alone, not what is falling.</summary>
		public float Extinction;
		/// <summary>How deep it lies, m.</summary>
		public float Depth;
		/// <summary>How far it has lifted off the ground, 0..1 (<see cref="FogLayer.Lift"/>).</summary>
		public float Lift;
		/// <summary>Half the thickness its top fades over, m (<see cref="TopShare"/> of its depth).</summary>
		public float TopSoftness;
		/// <summary>
		/// Always 0: the fog's top is no longer displaced, only faded (FishFogLayer.hlsl). Kept only because
		/// <see cref="FishVolumetricFogFeature"/> still uploads it into <c>_FishFogLayerShape.x</c>, which no
		/// shader reads any more; remove the two together.
		/// </summary>
		public float Heave;
		/// <summary>How patchy it is: the spread of its density about its mean, in e-foldings.</summary>
		public float Patchiness;
		/// <summary>The wind it drifts on, m/s: the wind at its own height (<see cref="DriftWind"/>).</summary>
		public float WindSpeed;

		/// <summary>Whether there is a layer to draw at all.</summary>
		public bool Visible => Extinction > 1e-6f && Depth > 0.01f;

		/// <summary>The tile the fog's banks are read at, m (FISH_FOG_BANK_TILE).</summary>
		public const float BankTile = 1600f;
		/// <summary>The skin of fog left over ground that stands above the pooled air, as a share of the depth (FISH_FOG_RIDGE_SKIN).</summary>
		public const float RidgeSkin = 0.1f;

		/// <summary>
		/// How far under the terrain map's ground a fog lying on the ground reaches, m: deeper than any
		/// valley the map can hide (FISH_FOG_UNDER_MAP). The map is a hundred metres to the texel and
		/// blurred, so a narrow valley's floor lies well under its ground; a lying fog fills down to the
		/// real floor, where the depth buffer ends the ray — down to sea level, and no further.
		/// </summary>
		public const float UnderMap = 1000f;
		/// <summary>
		/// How long the banks take to rise through one whole tile of the shape volume in still air, s — a
		/// whole tile, so the wrap is not a jump. A bank itself is gone in an eighth of that, some twenty
		/// minutes: the volume's correlation falls to a quarter over sixteen of its 128 texels.
		/// </summary>
		/// <remarks>
		/// It was 1200 s for the whole tile, which changed a bank in two and a half minutes — a fog seething
		/// where a real one's patches take the best part of an hour to fill in (Mazoyer et al. 2017) and hold
		/// their place while they drift. Now the change over twenty minutes the comment always claimed.
		/// </remarks>
		public const float BankTurnSeconds = 9600f;
		/// <summary>
		/// How far forward a fog's drops throw the light: about 0.85 for drops of five to twenty microns,
		/// a little of which is the diffraction spike the sun's own glare already draws.
		/// </summary>
		public const float PhaseAsymmetry = 0.8f;
		/// <summary>
		/// What one scattering of the beam gives the eye on average over every direction, as a share of
		/// the light's colour: the phase per steradian times π, averaged — a quarter (FishFogPhase).
		/// </summary>
		public const float AveragePhase = 0.25f;
		/// <summary>
		/// Half the fade of a mixed, optically thick fog's top, as a share of its depth: the water falls
		/// away over the upper 10–15 % of a developed fog (Costabloz et al. 2025, ACP 25, 6539: tethered-
		/// balloon profiles in SOFOG3D; one rose to 215 m and was gone by 255 m, 16 %), so a fade 15 % wide.
		/// </summary>
		public const float MixedTopShare = 0.075f;
		/// <summary>
		/// Half the fade of a thin, still mist's top, as a share of its depth: the whole of it — an
		/// optically thin fog under a stable profile has its water "maximal at the ground and decreasing
		/// with height" to its top (Costabloz et al. 2025), so the fade starts at the ground and holds the
		/// same fog, centred on the physics' top, as a sharp one would.
		/// </summary>
		public const float StillTopShare = 1f;
		/// <summary>
		/// Half the mean fade of a top the wind rolls, as a share of the depth: Kelvin–Helmholtz rolls a
		/// third of the fog's depth deep, some 500 m long, ride a sheared fog's top (Bergot 2013, QJRMS 139;
		/// Mazoyer et al. 2017, ACP 17), and averaged along a ray their swing is a fade a third of the
		/// depth wide. Drawn as that fade, not as the rolls: the rolls would be a displaced surface.
		/// </summary>
		public const float RolledTopShare = 1f / 6f;
		/// <summary>
		/// The depths over which a fog goes from thin to optically thick to its own longwave radiation, m:
		/// from then its top, not the ground, does the cooling, and over some hours its profile turns from
		/// stable to saturated-adiabatic, well mixed (Price 2011, "Radiation fog. Part I", BLM 139: about
		/// 100 m; Mazoyer et al. 2017: about 80 m; Costabloz 2025: the fogs under 50 m were all thin).
		/// </summary>
		public const float ThinBelow = 50f, ThickAbove = 100f;
		/// <summary>A metre's margin on each side of the fog's shell, so no comparison with it is decided by rounding.</summary>
		public const float ShellMargin = 1f;

		private static readonly int LayerId = Shader.PropertyToID("_FishFogLayer");
		private static readonly int ShapeId = Shader.PropertyToID("_FishFogLayerShape");
		private static readonly int DriftId = Shader.PropertyToID("_FishFogLayerDrift");
		private static readonly int LightId = Shader.PropertyToID("_FishFogLight");
		private static readonly int LightColorId = Shader.PropertyToID("_FishFogLightColor");
		private static readonly int AmbientId = Shader.PropertyToID("_FishFogAmbient");
		/// <summary>The fog's shell for the cloud march: x floor, y ceiling (m), z 1 while the march draws it (<see cref="Shell"/>).</summary>
		public static readonly int ShellId = Shader.PropertyToID("_FishFogShell");
		private static readonly int CloudScreenId = Shader.PropertyToID("_FishCloudScreen");

		/// <summary>
		/// Whether the cloud march draws the fog: whenever the sky's clouds are drawn at all, since the
		/// fog is walked by the same march (FishCloudVolume.hlsl).
		/// </summary>
		public static bool DrawnByClouds => SkySystem.CloudsReady;

		/// <summary>
		/// Whether anything draws the fog's layer — the cloud march, or the fallback passes where it does
		/// not run. While it is, the pipeline's distance fog must leave the fog's drops out, or the same
		/// drops take their light twice (<see cref="WeatherFogPresenter.UniformExtinction"/>).
		/// </summary>
		public static bool Drawn => DrawnByClouds || FishHeightFogFeature.DrawsTheLayer;

		/// <summary>
		/// Whether the cloud march has drawn the fog for the camera being recorded now: the fallback
		/// passes, recorded after it, stand down when it has. The cloud feature raises
		/// <c>_FishCloudScreen</c> once it has recorded for a camera, and the sky clears it each frame.
		/// </summary>
		public static bool MarchedThisFrame => DrawnByClouds && Shader.GetGlobalVector(CloudScreenId).x > 0.5f;

		/// <summary>The layer being shown now, as last published.</summary>
		public static FogLayerView Current { get; private set; }

		/// <summary>What the last published layer's banks are doing: xy drift (m), z their turn, w 0 (it was the wisps' turn: there are none).</summary>
		public static Vector4 Motion { get; private set; }

		private static double driftX, driftZ, bankTurn;
		private static float lastTime = float.NaN;

		/// <summary>The layer a frame carries, and how it looks.</summary>
		public static FogLayerView Of(in WeatherFrame frame)
		{
			FogLayer layer = FogLayer.In(frame);
			float fog = Mathf.Clamp01(frame[WeatherChannel.FogDensity]);
			// The weather's wind is the ten-metre wind (WindProfile.ReferenceHeight).
			float wind = Mathf.Clamp01(frame[WeatherChannel.WindSpeed]) * 30f;
			// How hard the wind works the fog: nothing in a calm, fully by a fresh breeze.
			float windy = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 8f, wind));
			float depth = layer.Depth;
			return new FogLayerView
			{
				Extinction = AirPhysics.FogExtinction(fog),
				Depth = depth,
				Lift = layer.Lift,
				TopSoftness = Mathf.Max(0.5f, depth * TopShare(depth, windy, layer.Lift)),
				Patchiness = (Mathf.Lerp(0.9f, 0.35f, fog) + 0.15f * windy) * Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(200f, 800f, depth)),
				WindSpeed = DriftWind(wind, depth, layer.Lift),
			};
		}

		/// <summary>
		/// How soft a fog's top is: half the thickness its density fades out over, as a share of its depth.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A radiation fog's top is its inversion, and how its water falls away under it depends on whether
		/// the fog is mixed. A thin fog under a stable profile holds the most water at the ground and less
		/// all the way up (<see cref="StillTopShare"/>): a ground mist that thins upward, the shape every
		/// game's exponential height fog draws. Mixed — optically thick enough to cool from its own top
		/// (<see cref="ThinBelow"/>…<see cref="ThickAbove"/>), stirred by the wind, or lifted into stratus by
		/// wind or morning — its water rises with height nearly adiabatically and falls away over the top
		/// sixth (<see cref="MixedTopShare"/>). In shear its top rolls, and the rolls' swing is the mean
		/// fade (<see cref="RolledTopShare"/>).
		/// </para>
		/// <para>
		/// It was 5 % of the depth in a calm and 25 % in wind, with the top then heaved by 10–30 % of the
		/// depth and billowed by eddies — a sheet with a textured surface. It is a fade now, and the only
		/// thing that shapes the top: its body stays at the physics' extinction up to the fade (the adiabatic
		/// rise of a mixed fog's water is not drawn — the fog channel is the visibility at the ground).
		/// </para>
		/// </remarks>
		/// <param name="depth">How deep the fog lies, m.</param>
		/// <param name="windy">How hard the wind works it, 0 calm … 1 a fresh breeze.</param>
		/// <param name="lift">How far it has lifted off the ground, 0..1.</param>
		public static float TopShare(float depth, float windy, float lift)
		{
			float thick = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ThinBelow, ThickAbove, depth));
			float mixed = Mathf.Max(thick, Mathf.Max(Mathf.Clamp01(windy), Mathf.Clamp01(lift)));
			float share = Mathf.Lerp(StillTopShare, MixedTopShare, mixed);
			return Mathf.Max(share, RolledTopShare * Mathf.Clamp01(windy));
		}

		/// <summary>
		/// The wind a fog drifts on, m/s: the wind at the middle of the layer, from the ten-metre wind the
		/// weather reports, down (or up) the law of the wall (<see cref="WindProfile"/>).
		/// </summary>
		/// <remarks>
		/// The air near the ground is slowed by it, logarithmically: over open country the wind at two
		/// metres is about three quarters of the ten-metre wind, at fifty metres about a quarter more. A
		/// fog a few metres deep creeps along at a walking pace in a breeze that tears the top off a deep
		/// one. It was the ten-metre wind for every fog, which sent a shallow mist sliding over the ground
		/// faster than the air it is made of. A lifted fog drifts at the middle of its sheet, not of the
		/// clear air under it. Not the wind of the whole column: this is the surface layer, well under
		/// any cloud base, where the logarithmic law is the whole of it — a WindProfile whose free wind is
		/// the law's own at the top of the layer, so the blend it makes toward the free wind adds nothing.
		/// Integrated frame by frame (<see cref="Publish"/>), so a change of wind changes how fast the fog
		/// moves and never where it is: this is not a drift worked out from a clock, which would jump.
		/// </remarks>
		/// <param name="windAtTenMetres">The weather's wind, m/s.</param>
		/// <param name="depth">How deep the fog lies, m.</param>
		/// <param name="lift">How far it has lifted off the ground, 0..1.</param>
		public static float DriftWind(float windAtTenMetres, float depth, float lift)
		{
			float wind = Mathf.Max(0f, windAtTenMetres);
			if (wind <= 0f || depth <= 0f)
			{
				return 0f;
			}
			// The middle of what is there: from the ground for a lying fog, from its base for a sheet.
			float baseHeight = Mathf.Clamp01(lift) * FogLayer.LiftedBase * depth;
			float middle = 0.5f * (baseHeight + depth);
			const float Layer = 1000f;
			var surface = new WindProfile(wind, Layer, WindProfile.FreeWind(wind, Layer), 0f, 12000f);
			return surface.At(Mathf.Max(FogLayer.WindHeight, middle));
		}

		/// <summary>
		/// The fog's structure as a factor on its extinction, from how far the structure stands from its
		/// mean in spreads (<paramref name="s"/>) and how patchy the fog is (<paramref name="spread"/>):
		/// log-normal, exp(σs − σ²/2). The twin of FishFogDensity's body.
		/// </summary>
		/// <remarks>
		/// Its mean over a structure of unit spread is exactly one, so the banks and hollows move the
		/// fog about without changing how much of it there is: the visibility the physics set is the
		/// visibility on average, however patchy the fog is drawn.
		/// </remarks>
		public static float StructureFactor(float s, float spread)
		{
			return Mathf.Exp(spread * s - 0.5f * spread * spread);
		}

		/// <summary>
		/// The shell the cloud march walks the fog in, over a window of ground whose highest point is
		/// <paramref name="highestGround"/> and whose pooled air stands at most
		/// <paramref name="highestPooled"/>: x its floor, y its ceiling, m. (0, −1) when there is no fog
		/// to walk.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The ceiling is the highest the fog's top can stand anywhere over that ground: the layer's depth
		/// over the highest pooled air, or its skin over the highest ground, faded out over one softness
		/// more — which is where FishFogDensity is last above zero. Nothing moves the top off that any
		/// more (it used to be heaved and billowed, and the shell stood that much higher). Beyond the map
		/// the ground is sea level, so never less than the depth itself. Everything above it the march
		/// skips in one step, as it skips the empty air between cloud bands.
		/// </para>
		/// <para>
		/// The floor is sea level. A fog lying on the ground fills down to whatever ground a ray meets
		/// (the Cov Viaduct gorge, narrower than the terrain map's texel, whose floor the map puts far too
		/// high), and the depth buffer ends the ray there; the march's range has to reach every such
		/// floor, and every one is above the sea. Under it there is no fog: a fog lies on the water
		/// (<see cref="Over"/>).
		/// </para>
		/// </remarks>
		public Vector2 Shell(float highestGround, float highestPooled)
		{
			if (!Visible)
			{
				return new Vector2(0f, -1f);
			}
			float depth = Mathf.Max(0f, Depth);
			float soft = Softness(TopSoftness, depth);
			float top = Mathf.Max(Mathf.Max(highestPooled + depth, highestGround + depth * RidgeSkin), depth);
			float ceiling = top + soft + ShellMargin;
			return new Vector2(-ShellMargin, ceiling);
		}

		/// <summary>
		/// Works out the layer the frame being shown carries, carries its structure along on the wind, and
		/// publishes both, with the light on the ground that lights it. Called by
		/// <see cref="WeatherShaderGlobals.Apply"/>, so a cleared weather clears it.
		/// </summary>
		/// <param name="time">The presentation's own clock, s: the world's motion, which stops when the world does.</param>
		public static void Publish(in WeatherFrame frame, float time)
		{
			FogLayerView view = Of(frame);
			Current = view;

			float dt = float.IsNaN(lastTime) ? 0f : Mathf.Clamp(time - lastTime, 0f, 1f);
			lastTime = time;
			Vector2 toward = WeatherShaderGlobals.WindDirection(frame[WeatherChannel.WindHeading]);
			driftX = Wrap(driftX + toward.x * view.WindSpeed * dt, BankTile);
			driftZ = Wrap(driftZ + toward.y * view.WindSpeed * dt, BankTile);
			// Stirred faster in wind: twice as fast by a fresh breeze.
			double pace = 1.0 + Mathf.Clamp01(view.WindSpeed / 8f);
			bankTurn = Wrap(bankTurn + dt * pace / BankTurnSeconds, 1.0);
			Motion = new Vector4((float)driftX, (float)driftZ, (float)bankTurn, 0f);

			Shader.SetGlobalVector(LayerId, view.Visible ? new Vector4(view.Extinction, view.Depth, view.Lift, view.TopSoftness) : Vector4.zero);
			// w: whether anything draws the layer before the transparent queue, for what is drawn in
			// front of it after (FishFogLayer.hlsl's _FishFogLayerShape).
			Shader.SetGlobalVector(ShapeId, new Vector4(0f, view.Patchiness, (float)bankTurn, Drawn ? 1f : 0f));
			Shader.SetGlobalVector(DriftId, new Vector4((float)driftX, (float)driftZ, 0f, 0f));
			// Every frame, for the cloud march, which draws the fog and runs whether or not any fallback
			// pass does: it used to be published only by those passes, which the march outlives.
			PublishLighting();
		}

		private static double Wrap(double value, double period)
		{
			double wrapped = value % period;
			return wrapped < 0.0 ? wrapped + period : wrapped;
		}

		// ── The light ───────────────────────────────────────────────────

		/// <summary>What lights the fog: the light that leads the sky, and the sky's own.</summary>
		public struct Lighting
		{
			/// <summary>xyz toward the light, w the drops' asymmetry.</summary>
			public Vector4 ToLight;
			/// <summary>
			/// rgb what the light brings, in the colour space the pipeline lights in, through the clouds
			/// on average over the view; w what the rgb is multiplied by, with the cloud shadow read at a
			/// point, to be the light a surface there gets — the reciprocal of that average — or 0 when
			/// the light carries no cloud shadow (<see cref="CookieShare"/>).
			/// </summary>
			public Vector4 LightColor;
			/// <summary>rgb the sky's light from all round.</summary>
			public Vector4 Ambient;
			/// <summary>The cloud shadow on the light, or null when it carries none.</summary>
			public Texture Cookie;
			/// <summary>The world onto the cookie, across: u = dot(xyz, p) + w (<see cref="CookieRows"/>).</summary>
			public Vector4 CookieU;
			/// <summary>The world onto the cookie, up: v = dot(xyz, p) + w.</summary>
			public Vector4 CookieV;
		}

		private static readonly int SunCookieId = Shader.PropertyToID("_FishFogSunCookie");
		private static readonly int SunCookieUId = Shader.PropertyToID("_FishFogSunCookieU");
		private static readonly int SunCookieVId = Shader.PropertyToID("_FishFogSunCookieV");

		/// <summary>
		/// What the fog's light, published through the clouds on average (<paramref name="averageThrough"/>),
		/// is multiplied by with the cloud shadow read at a point to be the light a surface there is lit
		/// by: the reciprocal of that average, while the light carries a cloud shadow; 0 (read nothing)
		/// while it does not.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The light that carries the cloud shadow is handed over as the open light: SkySystem divides the
		/// average through-share back out of it, and the cookie takes the share at each point back off, on
		/// every surface URP lights. The fog was lit by that average instead (the fog had no cookie), which
		/// is right on average and wrong wherever the cloud over a place is not the average — and most
		/// wrong under a storm's core, the one place snow and rain are heaviest: the cookie lets down a
		/// few per cent there, the view's average a good deal more, and the fog was lit several times
		/// brighter than the ground and every flake in it (FishFogLayer.hlsl's FishFogLight says what it did).
		/// </para>
		/// <para>
		/// Published this way the average stays in the rgb for whatever cannot read the shadow — the
		/// froxel fallback, the water's air colour (FishHeightFogFeature) — and those that can multiply it
		/// back out and the shadow in (FishFogSunShare), which gives exactly the light's own colour times
		/// its cookie: the surfaces' light.
		/// </para>
		/// </remarks>
		public static float CookieShare(float averageThrough, bool hasCookie)
		{
			return hasCookie ? 1f / Mathf.Max(1e-3f, averageThrough) : 0f;
		}

		/// <summary>
		/// The rows that take a world point onto a directional light's cookie as URP reads it: the light's
		/// own plane (<paramref name="worldToLight"/>), less the cookie's offset, over its size, about the
		/// middle — uv = (xy − offset) / size + ½ (URP's LightCookieManager builds Ortho(±½) · uv · worldToLight
		/// and the shader takes positionLS · ½ + ½; CloudShadowPresenter derives the same, and so sets the
		/// offset to the window's middle).
		/// </summary>
		public static void CookieRows(Matrix4x4 worldToLight, Vector2 size, Vector2 offset, out Vector4 u, out Vector4 v)
		{
			float sx = Mathf.Max(1e-3f, size.x), sy = Mathf.Max(1e-3f, size.y);
			Vector4 r0 = worldToLight.GetRow(0), r1 = worldToLight.GetRow(1);
			u = new Vector4(r0.x / sx, r0.y / sx, r0.z / sx, (r0.w - offset.x) / sx + 0.5f);
			v = new Vector4(r1.x / sy, r1.y / sy, r1.z / sy, (r1.w - offset.y) / sy + 0.5f);
		}

		/// <summary>
		/// The light the fog is lit by, as the pipeline would light a surface: the scene's sun — whichever
		/// light the sky has put in charge, the sun by day and the moon by night — and the ambient the sky
		/// hands the scene.
		/// </summary>
		/// <remarks>
		/// A drop is lit from every side, so the ambient is the sky's light from all round: mostly the sky
		/// overhead, some from the horizon and a little back up off the ground.
		/// </remarks>
		public static Lighting CurrentLighting()
		{
			Light light = RenderSettings.sun;
			if (light == null || !light.isActiveAndEnabled)
			{
				SkySystem sky = SkySystem.Instance;
				light = sky != null ? sky.Sun : null;
			}
			bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
			Vector3 toLight = light != null ? -light.transform.forward : Vector3.up;
			Color bright = Color.black;
			float share = 0f;
			Texture cookie = null;
			Vector4 cookieU = Vector4.zero, cookieV = Vector4.zero;
			if (light != null && light.isActiveAndEnabled)
			{
				Color color = linear ? light.color.linear : light.color;
				if (light.useColorTemperature)
				{
					color *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
				}
				bright = color * light.intensity;
				// The light carrying the clouds' shadow cookie has had its intensity raised by the share of
				// it the clouds let through on average, because the cookie takes that share back out, texel
				// by texel, on the ground (SkySystem). The rgb is lit by that average, for what cannot read
				// the cookie; what can takes the average back out and the cookie in (CookieShare).
				if (light.cookie != null && light.type == LightType.Directional)
				{
					float average = 1f;
					SkySystem sky = SkySystem.Instance;
					if (sky != null)
					{
						float altitude = Mathf.Asin(Mathf.Clamp(toLight.y, -1f, 1f)) * Mathf.Rad2Deg;
						average = Mathf.Clamp01(sky.CloudLightThrough(altitude));
					}
					bright *= average;
					var data = light.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
					if (data != null)
					{
						cookie = light.cookie;
						share = CookieShare(average, true);
						CookieRows(light.transform.worldToLocalMatrix, data.lightCookieSize, data.lightCookieOffset, out cookieU, out cookieV);
					}
				}
			}
			Color ambient;
			if (RenderSettings.ambientMode == AmbientMode.Trilight)
			{
				ambient = Lin(RenderSettings.ambientSkyColor, linear) * 0.45f + Lin(RenderSettings.ambientEquatorColor, linear) * 0.35f
					+ Lin(RenderSettings.ambientGroundColor, linear) * 0.2f;
			}
			else
			{
				ambient = Lin(RenderSettings.ambientLight, linear);
			}
			ambient *= RenderSettings.ambientIntensity;
			return new Lighting
			{
				ToLight = new Vector4(toLight.x, toLight.y, toLight.z, PhaseAsymmetry),
				LightColor = new Vector4(bright.r, bright.g, bright.b, share),
				Ambient = new Vector4(ambient.r, ambient.g, ambient.b, 1f),
				Cookie = cookie,
				CookieU = cookieU,
				CookieV = cookieV,
			};
		}

		private static Color Lin(Color c, bool linear) => linear ? c.linear : c;

		/// <summary>Publishes the light to the pixel passes, and returns it for a compute pass to be handed.</summary>
		public static Lighting PublishLighting()
		{
			Lighting lighting = CurrentLighting();
			Shader.SetGlobalVector(LightId, lighting.ToLight);
			Shader.SetGlobalVector(LightColorId, lighting.LightColor);
			Shader.SetGlobalVector(AmbientId, lighting.Ambient);
			// The cloud shadow, for FishFogSunShare: white when there is none, which is never read then.
			Shader.SetGlobalTexture(SunCookieId, lighting.Cookie != null ? lighting.Cookie : Texture2D.whiteTexture);
			Shader.SetGlobalVector(SunCookieUId, lighting.CookieU);
			Shader.SetGlobalVector(SunCookieVId, lighting.CookieV);
			return lighting;
		}

		// ── The layer's shape: a twin of FishFogLayer.hlsl ───────────────

		/// <summary>The layer over one place, in altitudes (FishFogColumn).</summary>
		public struct Column
		{
			/// <summary>The middle of the top's fade, m.</summary>
			public float Top;
			/// <summary>Half the thickness the top fades over, m.</summary>
			public float Soft;
			/// <summary>The middle of the base's fade, m: under the ground while the fog lies on it.</summary>
			public float Base;
			/// <summary>Half the thickness the base fades over, m.</summary>
			public float BaseSoft;

			/// <summary>The mean profile at an altitude, 0..1 of the extinction at the ground (FishFogProfile).</summary>
			public float Profile(float y)
			{
				return Fall(y, Top, Soft) * (1f - Fall(y, Base, BaseSoft));
			}

			/// <summary>
			/// A unit step falling smoothly from 1 to 0 across [edge − width, edge + width]: HLSL's
			/// 1 − smoothstep(edge − width, edge + width, y) (FishFogFall).
			/// </summary>
			public static float Fall(float y, float edge, float width)
			{
				float x = Mathf.Clamp01((y - (edge - width)) / (2f * width));
				return 1f - x * x * (3f - 2f * x);
			}

			/// <summary>How much of the layer lies above an altitude, m of it at full strength.</summary>
			public float Above(float y)
			{
				return Mathf.Max(0f, RampAbove(y, Top, Soft) - RampAbove(y, Base, BaseSoft));
			}

			/// <summary>Metres of the layer at full strength a straight ray crosses between two distances along it.</summary>
			public float Path(float y0, float dy, float t0, float t1)
			{
				float span = Mathf.Max(0f, t1 - t0);
				float ya = y0 + dy * t0;
				float yb = y0 + dy * t1;
				if (Mathf.Abs(yb - ya) < 0.5f)
				{
					return Profile(0.5f * (ya + yb)) * span;
				}
				return Mathf.Max(0f, (Above(ya) - Above(yb)) / dy);
			}

			/// <summary>∫ from y up of <see cref="Fall"/>, m (FishFogRampAbove).</summary>
			private static float RampAbove(float y, float edge, float width)
			{
				if (y <= edge - width)
				{
					return edge - y;
				}
				float x = Mathf.Clamp01((y - (edge - width)) / (2f * width));
				float x2 = x * x;
				return 2f * width * (0.5f - x + x2 * x - 0.5f * x2 * x2);
			}
		}

		/// <summary>
		/// Half the thickness a top fades over, as the shaders hold it (FishFogColumnAt): no less than a
		/// quarter of a metre, and no more than the depth — a fade that wide starts at the pooled ground.
		/// </summary>
		public static float Softness(float topSoftness, float depth)
		{
			return Mathf.Clamp(topSoftness, 0.25f, Mathf.Max(0.25f, depth));
		}

		/// <summary>
		/// The layer over ground at this height, where the pooled air lies at that one (FishFogColumnAt).
		/// Over the sea the ground is the water's surface, sea level, not the bed under it.
		/// </summary>
		public Column Over(float ground, float pooledGround)
		{
			ground = Mathf.Max(0f, ground);
			pooledGround = Mathf.Max(0f, pooledGround);
			float depth = Mathf.Max(0f, Depth);
			var c = new Column
			{
				Soft = Softness(TopSoftness, depth),
				Top = Mathf.Max(pooledGround + depth, ground + depth * RidgeSkin),
			};
			float lifted = Mathf.Clamp01(Lift) * FogLayer.LiftedBase * depth;
			// Lying on the ground, no base: it fills down to whatever ground there is (FishFogColumnAt).
			float lying = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.15f, Mathf.Clamp01(Lift)));
			c.BaseSoft = Mathf.Max(0.25f, lifted * 0.35f);
			c.Base = Mathf.Min(Mathf.Min(ground, pooledGround) - 2f + lifted - lying * UnderMap, c.Top - c.Soft - c.BaseSoft);
			// Never under the sea: a fog lies on the water and stops at its surface (FishFogColumnAt).
			c.Base = Mathf.Max(c.Base, c.BaseSoft);
			return c;
		}

		/// <summary>
		/// The fog's light at an altitude inside it, looked at from the side (FishFogLight, with the
		/// beam's forward throw averaged away): for what has to be told one colour for the whole fog.
		/// </summary>
		/// <remarks>
		/// Averaged over every direction one scattering of the beam gives a quarter of the light's colour
		/// (FishFogPhase: the phase per steradian times π); what the fog above has already turned arrives
		/// diffuse, as a white card would show it.
		/// </remarks>
		public Color LightAt(float y, in Column column, in Lighting lighting)
		{
			float g = lighting.ToLight.w;
			float carry = 0.75f * (1f - g);
			float above = Extinction * column.Above(y);
			float risen = Mathf.Clamp01(lighting.ToLight.y * 20f + 1f);
			float slant = above / Mathf.Max(0.05f, lighting.ToLight.y);
			float beam = Mathf.Exp(-slant);
			float diffuse = Mathf.Max(0f, 1f / (1f + carry * slant) - beam);
			var sun = new Color(lighting.LightColor.x, lighting.LightColor.y, lighting.LightColor.z) * (risen * (AveragePhase * beam + diffuse));
			var sky = new Color(lighting.Ambient.x, lighting.Ambient.y, lighting.Ambient.z) / (1f + carry * above);
			Color light = sun + sky;
			light.a = 1f;
			return light;
		}

		/// <summary>The ground under a point, m: the highest terrain there, or sea level where there is none (as CloudTerrainMap reads it).</summary>
		public static float GroundUnder(Vector3 at)
		{
			float height = 0f;
			Terrain[] terrains = Terrain.activeTerrains;
			for (int i = 0; i < terrains.Length; i++)
			{
				Terrain terrain = terrains[i];
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				Vector3 origin = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;
				if (at.x < origin.x || at.z < origin.z || at.x > origin.x + size.x || at.z > origin.z + size.z)
				{
					continue;
				}
				height = Mathf.Max(height, terrain.SampleHeight(at) + origin.y);
			}
			return height;
		}
	}
}
