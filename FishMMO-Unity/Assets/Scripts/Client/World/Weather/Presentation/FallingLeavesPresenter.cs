using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws the leaves coming down round the camera (<see cref="FallingLeaves"/>): a pre-built field of quads,
	/// as the precipitation is (<see cref="PrecipitationField"/>), placed, turned and lit entirely in the vertex
	/// shader. Stateless and on the shared motion clock, so every player sees the same leaf at the same place.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Gated by the trees.</b> Nothing is drawn unless the canopy map round the camera has crowns in it
	/// (<see cref="LeafCanopyMap"/>), and in the shader a leaf is shown only where a crown upwind of it could
	/// have let it go: it looks back along the wind by as far as the air has carried it since it left the
	/// crown, and is drawn as often as that crown's cover times the season's rate. Under a wood in autumn the
	/// air is full of them; in a meadow downwind of the wood's edge a stream, thinning out; in open country
	/// none.
	/// </para>
	/// <para>
	/// <b>Hooked, not placed.</b> Nothing is added to any scene: a <see cref="RuntimeInitializeOnLoadMethodAttribute"/>
	/// subscribes to the pipeline's camera callback, and it draws for the weather presenter's camera — the same
	/// camera the precipitation is drawn for, with the same profile.
	/// </para>
	/// </remarks>
	public static class FallingLeavesPresenter
	{
		public const string ShaderName = "FishMMO/Weather/Falling Leaves";

		private static readonly int SeasonId = Shader.PropertyToID("_FishSeason");
		private static readonly int OriginId = Shader.PropertyToID("_LeafOrigin");
		private static readonly int BoxId = Shader.PropertyToID("_LeafBox");
		private static readonly int FallId = Shader.PropertyToID("_LeafFall");
		private static readonly int TravelId = Shader.PropertyToID("_LeafTravel");
		private static readonly int SpreadId = Shader.PropertyToID("_LeafSpread");
		private static readonly int RatesId = Shader.PropertyToID("_LeafRates");
		private static readonly int SizeId = Shader.PropertyToID("_LeafSize");
		private static readonly int ColorId = Shader.PropertyToID("_LeafColor");
		private static readonly int AutumnId = Shader.PropertyToID("_LeafAutumn");
		private static readonly int DryId = Shader.PropertyToID("_LeafDry");
		private static readonly int CanopyId = Shader.PropertyToID("_LeafCanopy");
		private static readonly int CanopyRectId = Shader.PropertyToID("_LeafCanopyRect");
		private static readonly int WindId = Shader.PropertyToID("_LeafWind");

		/// <summary>The typical leaf's speed, m/s, the field's fall is integrated at (each leaf a whole 64th of it).</summary>
		public const float TypicalFall = 1.2f;

		/// <summary>How much faster than a flutterer a tumbler comes down: autorotation's lift is spent sideways, not holding it up.</summary>
		public const float TumbleSpeedup = 1.35f;

		/// <summary>How far above the camera the field's box is centred, m: the leaves come from crowns over it.</summary>
		public const float BoxLift = 6f;

		private static bool hooked;
		private static Mesh mesh;
		private static int meshParticles;
		private static Material material;
		private static Shader materialShader;
		private static MaterialPropertyBlock block;
		private static PrecipitationField.Travel travel;
		private static float lastTime = float.NaN;
		private static readonly float GustPaceA = WholeTurns(0.7);
		private static readonly float GustPaceB = WholeTurns(0.23);

		/// <summary>An angular pace snapped to whole turns in the motion clock's wrap (PrecipitationField's own).</summary>
		private static float WholeTurns(double radiansPerSecond)
		{
			double turns = System.Math.Round(radiansPerSecond * WorldMotion.ShaderWrapSeconds / (2.0 * System.Math.PI));
			return (float)(turns * 2.0 * System.Math.PI / WorldMotion.ShaderWrapSeconds);
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Unhook();
			mesh = null;
			material = null;
			materialShader = null;
			block = null;
			travel = default;
			lastTime = float.NaN;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked)
			{
				return;
			}
			hooked = true;
			RenderPipelineManager.beginCameraRendering += OnBeginCamera;
			Application.quitting += Shutdown;
		}

		private static void Unhook()
		{
			if (!hooked)
			{
				return;
			}
			hooked = false;
			RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
			Application.quitting -= Shutdown;
		}

		private static void Shutdown()
		{
			Unhook();
			if (mesh != null)
			{
				Object.Destroy(mesh);
				mesh = null;
			}
			if (material != null)
			{
				Object.Destroy(material);
				material = null;
			}
			LeafCanopyMap.Release();
		}

		private static readonly Unity.Profiling.ProfilerMarker Marker = new Unity.Profiling.ProfilerMarker("Weather.FallingLeaves");

		private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
		{
			WeatherPresentation presentation = WeatherPresentation.Instance;
			if (presentation == null || camera == null)
			{
				return;
			}
			Camera wanted = presentation.TargetCamera != null ? presentation.TargetCamera : Camera.main;
			if (camera != wanted)
			{
				return;
			}
			WeatherRenderProfile profile = presentation.Profile != null ? presentation.Profile : WeatherRenderProfile.Active;
			if (profile == null || profile.FallingLeaves == null || !profile.FallingLeaves.Enabled)
			{
				return;
			}
			Marker.Begin();
			Render(camera, presentation.Shown, profile);
			Marker.End();
		}

		private static void Render(Camera camera, in WeatherFrame frame, WeatherRenderProfile profile)
		{
			FallingLeavesSettings settings = profile.FallingLeaves;
			float time = WorldMotion.Wrapped(WorldMotion.ShaderWrapSeconds);
			// The step since the last frame for the integrated travel, held to a quarter of a second (PrecipitationField.Render).
			float seconds = float.IsNaN(lastTime) ? 0f : Mathf.Clamp(time - lastTime, 0f, 0.25f);
			lastTime = time;

			int particles = settings.ParticlesFor(QualitySettings.GetQualityLevel());
			Vector3 origin = camera.transform.position;
			// Nothing falls through the sea: from under it the leaves are up in the air, out of sight.
			if (particles <= 0 || SurfaceWater.IsUnder(origin))
			{
				return;
			}

			// The season the canopies are in, as the vegetation shader reads it.
			Vector4 season = Shader.GetGlobalVector(SeasonId);
			float wind01 = frame[WeatherChannel.WindSpeed];
			// The gust now: the frame's gustiness, beating on the shared clock (the precipitation's own paces).
			float gust = frame[WeatherChannel.WindGust] * (0.5f + 0.5f * Mathf.Sin(time * GustPaceA) * Mathf.Sin(time * GustPaceB + 1f));
			Vector2 rates = FallingLeaves.Rates(season.x, season.w, wind01, gust);
			if (rates.x < 1e-3f && rates.y < 1e-3f)
			{
				return;
			}
			if (!LeafCanopyMap.Update(origin, settings.CanopyTexelMetres) || LeafCanopyMap.Texture == null)
			{
				return;
			}
			Material leafMaterial = ResolveMaterial(profile);
			if (leafMaterial == null)
			{
				return;
			}
			if (mesh == null || meshParticles != particles)
			{
				if (mesh != null)
				{
					Object.Destroy(mesh);
				}
				mesh = PrecipitationField.BuildMesh(particles, 4321);
				mesh.name = $"Falling Leaves ({particles})";
				meshParticles = particles;
			}
			block ??= new MaterialPropertyBlock();

			// The air among the trunks, gusting; leaves go with it (FallingLeaves.UnderCanopyWind).
			Vector2 direction = WeatherShaderGlobals.WindDirection(frame[WeatherChannel.WindHeading]);
			float air = wind01 * 30f * FallingLeaves.UnderCanopyWind * (1f + 0.6f * gust);
			var velocity = new Vector3(direction.x * air, -TypicalFall, direction.y * air);
			var box = new Vector3(settings.BoxSize, settings.BoxHeight, settings.BoxSize);
			PrecipitationField.Advance(ref travel, velocity, seconds, box);

			float turned = FallingLeaves.TurnedShare(season.x, season.w, wind01, gust);
			Color green = settings.LeafAlbedo * LeafCanopyMap.Healthy;

			block.Clear();
			block.SetTexture(CanopyId, LeafCanopyMap.Texture);
			block.SetVector(CanopyRectId, LeafCanopyMap.Rect);
			block.SetVector(OriginId, new Vector4(origin.x, origin.y, origin.z, time));
			block.SetVector(BoxId, new Vector4(box.x, box.y, box.z, BoxLift));
			block.SetVector(FallId, new Vector4(velocity.x, velocity.y, velocity.z, TypicalFall));
			block.SetVector(TravelId, new Vector4((float)travel.X, (float)travel.Fall, (float)travel.Z, 0f));
			block.SetVector(SpreadId, new Vector4(FallingLeaves.TerminalSpeed.x / TypicalFall, FallingLeaves.TerminalSpeed.y / TypicalFall / TumbleSpeedup,
				FallingLeaves.TumbleShare, TumbleSpeedup));
			block.SetVector(RatesId, new Vector4(rates.x, rates.y, turned, settings.BrownShare));
			block.SetVector(SizeId, new Vector4(settings.LeafSize.x, Mathf.Max(settings.LeafSize.x, settings.LeafSize.y), settings.Alpha, settings.LieSeconds));
			block.SetColor(ColorId, green);
			block.SetColor(AutumnId, LeafCanopyMap.Autumn);
			block.SetColor(DryId, settings.DryAlbedo);
			block.SetVector(WindId, new Vector4(direction.x, direction.y, air, gust));

			var rp = new RenderParams(leafMaterial)
			{
				camera = camera,
				matProps = block,
				worldBounds = new Bounds(origin + Vector3.up * BoxLift, box * 1.5f),
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = camera.gameObject.layer,
			};
			Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
		}

		private static Material ResolveMaterial(WeatherRenderProfile profile)
		{
			Shader shader = profile.FallingLeavesShader != null ? profile.FallingLeavesShader : Shader.Find(ShaderName);
			if (shader == null || !shader.isSupported)
			{
				return null;
			}
			if (material == null || materialShader != shader)
			{
				if (material != null)
				{
					Object.Destroy(material);
				}
				material = new Material(shader) { name = "Falling Leaves", hideFlags = HideFlags.HideAndDontSave };
				materialShader = shader;
			}
			return material;
		}
	}
}
