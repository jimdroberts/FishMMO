using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The fog and cloud audit of 2026-10-09, pinned: the defects its renders found, each checked against a copy
	/// of the source with the defect put back, so a pin that stops checking anything fails too.
	/// </summary>
	/// <remarks>
	/// The ground mist took its drops' whole extinction out of what stood behind it and gave back, away from the
	/// sun, a twentieth of the beam: it lay over the land as a dark veil. Water drawn after the cloud march took the march's fog from the cloud buffer and then the analytic
	/// fog again (the swash glowed through a fog as a pale band), or skipped the falls' mist the buffer does not
	/// hold. The rain haze under a wet column repeated, in front of the ground, the falling rain the pipeline's
	/// own fog already lays over every surface. The height fog and the froxel volume found their shaders by name,
	/// which a build cannot do for a shader nothing references, and the build's fog stripping was left to guess
	/// from scenes that have no fog. And the flag that says a camera's clouds were marched was cleared once a
	/// frame, so a second camera inherited the first's.
	/// </remarks>
	[TestFixture]
	public class FogAuditPinTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";
		private const string Water = "Assets/Plugins/FishMMO Water/Shaders/";

		[Test]
		public void TheRaysPhaseIsBlueNoiseTurnedByTheFrame()
		{
			// The lattice over every cloud base: each rebuilt pixel keeps a fixed error drawn in the pattern of the
			// per-texel part of its rays' phase, and interleaved gradient noise is a lattice however it is moved (an
			// offset only adds a constant inside its frac). Blue noise fixed to the texel, turned by the frame's phase.
			string phase = SourceScanPins.ReadCode(Shaders + "FishCloudPhase.hlsl");
			SourceScanPins.HoldsAndFires("ray phase", phase,
				code =>
				{
					string body = SourceScanPins.Body(code, "float FishCloudRayPhase(");
					return body != null && body.Contains("LOAD_TEXTURE2D(_FishCloudBlueNoise, uint2(texel) & 63u).a")
						&& body.Contains("return frac(tile + _FishCloudPhaseState.y);")
						? null
						: "the rays' phase is not the blue-noise tile turned by the frame";
				},
				SourceScanPins.Replace("return frac(tile + _FishCloudPhaseState.y);", "return FishCloudIgn(texel + 5.588238 * fmod(_FishCloudPhaseState.x, 64.0));"),
				"the moved gradient noise");
			string feature = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Sky/FishCloudsFeature.cs");
			LogAssert.IsTrue(feature.Contains("material.SetVector(PhaseStateId, new Vector4(Mathf.Max(0, state.Frame) % 4096, options.w, blueNoise != null ? 1f : 0f, 0f));"),
				"the feature hands the march the frame's phase and says whether the tile is bound");
			string clouds = SourceScanPins.ReadCode(Shaders + "FishClouds.shader");
			int marches = clouds.Split(new[] { "float jitter = FishCloudRayPhase(input.positionCS.xy);" }, System.StringSplitOptions.None).Length - 1;
			LogAssert.AreEqual(2, marches, "the near march and the far band both take the ray phase");
		}

		[Test]
		public void EveryRayComingUpIntoABaseFindsIt()
		{
			// The grain over a raining storm's base (2026-10-09): the haze tapers to nothing just under the base, so the
			// sample before it was haze on one texel and clear air on the next, by the phase — and only the first kind
			// found the base; the second walked into the cloud with the haze's stride from wherever its step landed.
			// Raw frames in Flo Monolith's storm: 4.8 % of the base's brightness changed from frame to frame, 1.9 % after
			// (1.45 % with no rain at all).
			string volume = SourceScanPins.ReadCode(Shaders + "FishCloudVolume.hlsl");
			SourceScanPins.HoldsAndFires("base entry", volume,
				code => code.Contains("if (cloudHere && wasUnderBase && prevKnown && at > prevAt + 1.0)") ? null : "the base is found only after a sample that held haze",
				SourceScanPins.Replace("if (cloudHere && wasUnderBase && prevKnown && at > prevAt + 1.0)", "if (cloudHere && wasUnderBase && lastWasHaze && prevKnown && at > prevAt + 1.0)"),
				"haze required before the base");
			SourceScanPins.HoldsAndFires("cloud entry from clear air", volume,
				code =>
				{
					int start = code.IndexOf("travelled = 0.5 * (outside + within);", System.StringComparison.Ordinal);
					if (start < 0)
					{
						return "the clear-air edge search is gone";
					}
					int end = code.IndexOf("continue;", start, System.StringComparison.Ordinal);
					string entry = end < 0 ? code.Substring(start) : code.Substring(start, end - start);
					return entry.Contains("wasUnderBase = false;") && entry.Contains("insideSamples = 0.0;") && entry.Contains("tauInCloud = 0.0;")
						? null
						: "a ray coming into a cloud from clear air keeps what it learnt walking the haze under a base";
				},
				SourceScanPins.Replace("                    wasUnderBase = false;\n                    insideSamples = 0.0;", "                    insideSamples = 0.0;"),
				"the smooth medium's stride carried into the cloud");
		}

		[Test]
		public void NearCloudIsWalkedFiner()
		{
			// Near Step Scale (2026-10-09): the cloud within a few kilometres walked at a finer step, eased back smoothly
			// to Step Scale beyond — the grain on a storm's base 1.2 % → 0.9 % of its brightness at the bed's 180x clock
			// for about half a millisecond at 1440p, where refining the whole sky cost the same for almost nothing more.
			string volume = SourceScanPins.ReadCode(Shaders + "FishCloudVolume.hlsl");
			SourceScanPins.HoldsAndFires("near step", volume,
				code => code.Contains("cloudStepBase *= lerp(nearStep, 1.0, smoothstep(_FishCloudNearStep.y, _FishCloudNearStep.y + _FishCloudNearStep.z, travelled));")
					? null
					: "the near cloud is not walked at its own step, eased back with distance",
				SourceScanPins.Replace("cloudStepBase *= lerp(nearStep, 1.0, smoothstep(_FishCloudNearStep.y, _FishCloudNearStep.y + _FishCloudNearStep.z, travelled));", ""),
				"one step scale for the whole sky");
			string asset = SourceScanPins.ReadSource("Assets/Prefabs/Client/Weather/Weather Render Profile.asset");
			LogAssert.IsTrue(asset.Contains("NearStepScale: 0.35"), "the shipped profile walks near cloud at 0.35");
		}

		[Test]
		public void TheCloudsMarchToTheirOwnDistanceNotTheCamerasFarPlane()
		{
			// The far tail and the far band's last slice began at the camera's far plane, which the scene backdrop raises
			// to 20 km: what the clouds cost and looked like changed with a setting about the horizon ring (2026-10-09).
			string feature = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Sky/FishCloudsFeature.cs");
			SourceScanPins.HoldsAndFires("full march", feature,
				code => code.Contains("material.SetVector(FarTailId, new Vector4(Mathf.Max(1000f, sky.CloudFullMarchMetres)")
					&& code.Contains("float farPlane = FullMarchFor(sky, band);") && !code.Contains("cameraData.camera.farClipPlane, sky.CloudFarTailSteps")
					? null
					: "the clouds' full march follows the camera's far plane",
				SourceScanPins.Replace("float farPlane = FullMarchFor(sky, band);", "float farPlane = Mathf.Max(band * FarSplitB + 1000f, cameraData.camera.farClipPlane);"),
				"the band's last slice at the camera's far plane");
			// 13 km, Jim's choice for the frames; the far tail's steps grow from the ordinary step there (geometric).
			LogAssert.IsTrue(SourceScanPins.ReadSource("Assets/Prefabs/Client/Weather/Weather Render Profile.asset").Contains("FullMarchMetres: 13000"),
				"the shipped profile marches the clouds in full to 13 km");
			string volume = SourceScanPins.ReadCode(Shaders + "FishCloudVolume.hlsl");
			SourceScanPins.HoldsAndFires("geometric far tail", volume,
				code => code.Contains("stepHere = max(stepHere, tailStep);") && code.Contains("tailStep = stepHere * growth;") ? null : "the far tail's steps are spread evenly",
				SourceScanPins.Replace("stepHere = max(stepHere, tailStep);", "stepHere = max(stepHere, tailRest / tailLeft);"),
				"the even tail");
		}

		[Test]
		public void TheMistPassesOnWhatItThrowsStraightOn()
		{
			// Lit with its whole extinction and a g = 0.8 lobe, the mist took the light of what stood behind it and
			// gave back a twentieth of the beam: a dark veil. Its forward spike is counted as unscattered
			// (delta-Eddington): extinction × (1 − g²), the rest thrown with g / (1 + g).
			string mist = SourceScanPins.ReadCode(Shaders + "FishMist.hlsl");
			SourceScanPins.HoldsAndFires("mist extinction", mist,
				code =>
				{
					string march = SourceScanPins.Body(code, "float4 FishMistMarch(");
					if (march == null || !march.Contains("FishMistDensity(at, above, ready, dt) * fade * FishMistExtinctionShare()"))
					{
						return "the mist takes its forward spike out of what is behind it";
					}
					if (!march.Contains("FishMistLight(direction, fogAbove, shadow, FishFogSunShare(at))"))
					{
						return "the mist is not lit as a mist (FishMistLight)";
					}
					string share = SourceScanPins.Body(code, "float FishMistExtinctionShare(");
					string light = SourceScanPins.Body(code, "float3 FishMistLight(");
					return share != null && share.Contains("return 1.0 - g * g;") && light != null && light.Contains("float gMist = g / (1.0 + g);")
						? null
						: "the delta-Eddington scaling is not g² out, g / (1 + g) left";
				},
				SourceScanPins.Replace("FishMistDensity(at, above, ready, dt) * fade * FishMistExtinctionShare()", "FishMistDensity(at, above, ready, dt) * fade"),
				"the whole extinction taken out");
			SourceScanPins.HoldsAndFires("mist phase", mist,
				code => SourceScanPins.Body(code, "float3 FishMistLight(")?.Contains("FishFogPhase(dot(ray, toLight), gMist)") == true
					? null
					: "the mist scatters its light with the drops' whole asymmetry",
				SourceScanPins.Replace("FishFogPhase(dot(ray, toLight), gMist)", "FishFogPhase(dot(ray, toLight), g)"),
				"the g = 0.8 lobe");
		}

		[Test]
		public void TheMistIsShadedWhereItsBodyStands()
		{
			string mist = SourceScanPins.ReadCode(Shaders + "FishMist.hlsl");
			SourceScanPins.HoldsAndFires("mist shade", mist,
				code => code.Contains("FishTerrainSunlit(float3(at.x, max(at.y, ground.x + FISH_MIST_SHALLOW), at.z))")
					? null
					: "the mist's terrain shade is not read at the patch's body",
				SourceScanPins.Replace("FishTerrainSunlit(float3(at.x, max(at.y, ground.x + FISH_MIST_SHALLOW), at.z))", "FishTerrainSunlit(at)"),
				"shade read at the ground");
		}

		[Test]
		public void WaterUnderTheCloudBufferTakesOnlyTheFogItDoesNotHold()
		{
			string fog = SourceScanPins.ReadCode(Water + "FishWaterFog.hlsl");
			SourceScanPins.HoldsAndFires("air fog over the cloud buffer", fog,
				code =>
				{
					string over = SourceScanPins.Body(code, "half3 FishWaterAirFogOver(");
					return over != null && over.Contains("_FishAirFogRange.z < 0.5") ? null : "FishWaterAirFogOver does not leave the layer to the march";
				},
				SourceScanPins.Replace("FishWaterAirFogParts(color, positionWS, screenUV, _FishAirFogRange.z < 0.5, keep)",
					"FishWaterAirFogParts(color, positionWS, screenUV, true, keep)"),
				"the layer applied over a buffer that already holds it");

			// Every surface the cloud buffer is laid over: none takes the whole air fog on top of it.
			foreach (string file in new[] { "FishWaterShading.hlsl", "FishInlandWater.hlsl", "FishWaterfall.shader", "FishWaterShore.shader",
				"FishWaterCaustics.shader", "FishLavaLight.hlsl" })
			{
				string code = SourceScanPins.ReadCode(Water + file);
				SourceScanPins.HoldsAndFires(file, code,
					c => c.Contains("FishWaterAirFogOver(") && !c.Contains("FishWaterAirFog(") ? null : $"{file} takes the whole air fog under the cloud buffer",
					SourceScanPins.Replace("FishWaterAirFogOver(", "FishWaterAirFog("),
					"the whole air fog again");
			}
			// The spray is drawn over the buffer, so nothing has fogged it yet: it takes it all.
			LogAssert.IsTrue(SourceScanPins.ReadCode(Water + "FishWaterSpray.shader").Contains("FishWaterAirFog(color, input.positionWS"),
				"the spray takes the whole air fog");
		}

		[Test]
		public void TheRainHazeHangsOnlyOverTheSky()
		{
			string volume = SourceScanPins.ReadCode(Shaders + "FishCloudVolume.hlsl");
			SourceScanPins.HoldsAndFires("rain hang", volume,
				code => code.Contains("_FishCloudSub.w * FishCloudRainHangScale *") ? null : "the rain haze is not scaled by what the ray ends on",
				SourceScanPins.Replace("_FishCloudSub.w * FishCloudRainHangScale *", "_FishCloudSub.w *"),
				"the haze drawn in front of the ground too");

			string clouds = SourceScanPins.ReadCode(Shaders + "FishClouds.shader");
			int set = clouds.Split(new[] { "FishCloudRainHangScale = isSky ? 1.0 : 0.0;" }, System.StringSplitOptions.None).Length - 1;
			LogAssert.AreEqual(2, set, "the near march and the far band each say whether their ray reaches the sky");
		}

		[Test]
		public void TheFallbackFogShadersReachABuild()
		{
			foreach (string tier in new[] { "Performant", "Balanced", "HighFidelity" })
			{
				string renderer = SourceScanPins.ReadSource($"Assets/Settings/URP-{tier}-Renderer.asset");
				LogAssert.IsTrue(renderer.Contains("FogShader: {fileID: 4800000, guid: 0dbb995e33d6cf2ff938338521db28c1, type: 3}"),
					$"{tier}'s height fog references its shader (Hidden/FishMMO/Weather/HeightFog)");
				LogAssert.IsTrue(renderer.Contains("ApplyShader: {fileID: 4800000, guid: 261dcc0cea9c2133480615767c020944, type: 3}"),
					$"{tier}'s froxel volume references its compositing shader");
			}
			string height = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Sky/FishHeightFogFeature.cs");
			SourceScanPins.HoldsAndFires("height fog shader", height,
				code => code.Contains("FogShader != null ? FogShader : Shader.Find(ShaderName)") ? null : "the height fog does not use its referenced shader",
				SourceScanPins.Replace("FogShader != null ? FogShader : Shader.Find(ShaderName)", "Shader.Find(ShaderName)"),
				"found by name alone");
			string froxel = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Sky/FishVolumetricFogFeature.cs");
			SourceScanPins.HoldsAndFires("froxel apply shader", froxel,
				code => code.Contains("ApplyShader != null ? ApplyShader : Shader.Find(ApplyShaderName)") ? null : "the froxel volume does not use its referenced shader",
				SourceScanPins.Replace("ApplyShader != null ? ApplyShader : Shader.Find(ApplyShaderName)", "Shader.Find(ApplyShaderName)"),
				"found by name alone");
			// The material made at run time is the feature's own, never written into the serialized field, which
			// Dispose would otherwise destroy whatever it held — an assigned material asset included.
			LogAssert.IsTrue(height.Contains("created = CoreUtils.CreateEngineMaterial(shader);") && !height.Contains("FogMaterial = CoreUtils.CreateEngineMaterial"),
				"the height fog keeps the material it makes apart from the serialized one");
			LogAssert.IsTrue(froxel.Contains("created = CoreUtils.CreateEngineMaterial(shader);") && !froxel.Contains("ApplyMaterial = CoreUtils.CreateEngineMaterial"),
				"the froxel volume keeps the material it makes apart from the serialized one");

			// Custom fog stripping, keeping every mode: the only scene in the build has fog off, so Automatic could strip
			// the fog variants every surface shader needs once the weather turns fog on.
			string graphics = SourceScanPins.ReadSource("ProjectSettings/GraphicsSettings.asset");
			LogAssert.IsTrue(graphics.Contains("m_FogStripping: 1") && graphics.Contains("m_FogKeepLinear: 1")
				&& graphics.Contains("m_FogKeepExp: 1") && graphics.Contains("m_FogKeepExp2: 1"), "the build keeps every fog mode");
		}

		[Test]
		public void EveryCameraStartsWithoutACloudBuffer()
		{
			string sky = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Sky/SkySystem.cs");
			SourceScanPins.HoldsAndFires("cloud buffer flag", sky,
				code => SourceScanPins.InOrder(SourceScanPins.Body(code, "private void OnBeginCamera("),
					"Shader.SetGlobalVector(CloudScreenId, Vector4.zero);", "if (camera != wanted)"),
				SourceScanPins.Replace("Shader.SetGlobalVector(CloudScreenId, Vector4.zero);", string.Empty),
				"cleared once a frame only");
		}
	}
}
