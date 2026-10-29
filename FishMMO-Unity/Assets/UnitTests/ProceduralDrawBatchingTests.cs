using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The client's procedural draws off property blocks (2026-10-27): a <see cref="UnityEngine.MaterialPropertyBlock"/> takes a draw
	/// out of the SRP Batcher, and URP then sets up its whole shader state on the CPU (StdRender.ApplyShader). Each moved system now
	/// draws with a material of its own (a DrawMaterials copy, or the runtime material it already made) whose per-draw numbers sit
	/// in the shader's UnityPerMaterial, or (ambient and sea life) from an instance buffer; the backdrop's rings draw in play with a
	/// runtime copy of their material; and the client draws Unity's terrain heightmaps at a coarser pixel error.
	/// </summary>
	[TestFixture]
	public class ProceduralDrawBatchingTests
	{
		private const string Client = "Assets/Scripts/Client/World/";
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";

		/// <summary>Every system whose draws were moved off a property block.</summary>
		private static readonly string[] MovedSystems =
		{
			Client + "Weather/Presentation/PrecipitationField.cs",
			Client + "Weather/Presentation/PrecipitationSplashField.cs",
			Client + "Weather/Presentation/AirMotesField.cs",
			Client + "Weather/Presentation/FallingLeavesPresenter.cs",
			Client + "Weather/Sky/SkyEffects.cs",
			Client + "Weather/Sky/VortexPresenter.cs",
			Client + "Weather/Sky/VolcanicPlumePresenter.cs",
			Client + "Weather/Sky/GeothermalSteamPresenter.cs",
			Client + "Weather/Sky/SkySystem.cs",
			Client + "Terrain/CaveDripField.cs",
			Client + "Terrain/CaveSkylights.cs",
			Client + "Terrain/SeaHallVents.cs",
			Client + "Terrain/CaveWaterLife.cs",
			Client + "Terrain/SiteEmbers.cs",
			Client + "Terrain/LighthouseBeams.cs",
			Client + "Terrain/RockfallField.cs",
			Client + "AmbientLife/AmbientLifeSystem.cs",
			Client + "SeaLife/SeaLifeSystem.cs",
			Client + "SeaLife/SeaLifeSystem.CavePools.cs",
		};

		/// <summary>Each converted shader (or the include that declares its numbers) and per-draw numbers it must keep in UnityPerMaterial.</summary>
		private static readonly (string File, string[] Names)[] PerDrawNumbers =
		{
			(Shaders + "FishPrecipitation.shader", new[] { "_PrecipOrigin", "_PrecipBox", "_PrecipShell", "_PrecipTravel", "_PrecipShape", "_PrecipCrowd" }),
			(Shaders + "FishPrecipitationSplash.shader", new[] { "_FishSplashOrigin", "_FishSplashParams", "_FishSplashColor", "_FishSplashGlint" }),
			(Shaders + "FishAirMotes.shader", new[] { "_MoteOrigin", "_MoteBox", "_MoteTravel", "_MoteShape", "_MoteExtra" }),
			(Shaders + "FishFallingLeaves.shader", new[] { "_LeafOrigin", "_LeafTravel", "_LeafCanopyRect", "_LeafWind" }),
			(Shaders + "FishBolt.shader", new[] { "_BoltColor" }),
			(Shaders + "FishCurtain.shader", new[] { "_CurtainCentre", "_CurtainShape", "_CurtainMarch", "_CurtainStorm" }),
			(Shaders + "FishVortex.hlsl", new[] { "_VortexCentre", "_VortexTime", "_VortexMarch", "_VortexDebris", "_VortexFleck" }),
			(Shaders + "FishVolcanicPlume.shader", new[] { "_PlumeVent", "_PlumeForm", "_PlumePace", "_PlumeHeld" }),
			(Shaders + "FishSkyBody.shader", new[] { "_UseTexture", "_RimColour" }),
			(Shaders + "FishCaveDrips.shader", new[] { "_DripCount" }),
			(Shaders + "FishSkylightShafts.shader", new[] { "_SkylightAir" }),
			(Shaders + "FishSeaHallVents.shader", new[] { "_HallDome", "_HallRadii", "_HallInfo", "_HallWater" }),
			(Shaders + "FishCaveWaterLife.shader", new[] { "_WaterInfo" }),
			(Shaders + "FishSiteEmbers.shader", new[] { "_EmberWind" }),
			(Shaders + "FishSiteFlames.shader", new[] { "_FlameWind" }),
			(Shaders + "FishLighthouseBeams.shader", new[] { "_BeaconAir" }),
			(Shaders + "FishAmbientLife.hlsl", new[] { "_LifeInstanceBase" }),
			(Shaders + "FishSeaLife.hlsl", new[] { "_LifeInstanceBase" }),
		};

		/// <summary>Why a system's code still draws through a property block, or null.</summary>
		private static string NoPropertyBlock(string code)
		{
			if (code.Contains("MaterialPropertyBlock"))
			{
				return "it still makes a MaterialPropertyBlock";
			}
			if (Regex.IsMatch(code, @"\bmatProps\s*="))
			{
				return "a RenderParams still carries a block (matProps)";
			}
			if (code.Contains("DrawMeshInstanced("))
			{
				return "it still draws with Graphics.DrawMeshInstanced, whose per-instance arrays ride a block";
			}
			if (code.Contains("SetPropertyBlock("))
			{
				return "a renderer is still given a block";
			}
			return null;
		}

		[Test]
		public void TheMovedSystems_DrawWithNoPropertyBlock()
		{
			foreach (string path in MovedSystems)
			{
				string code = SourceScanPins.ReadCode(path);
				SourceScanPins.HoldsAndFires(path, code, NoPropertyBlock,
					source => source + "\nprivate readonly MaterialPropertyBlock putBack = new MaterialPropertyBlock();\n",
					"a block for the draw again");
			}
		}

		[Test]
		public void TheMovedSystems_StillDrawProcedurally()
		{
			foreach (string path in MovedSystems)
			{
				string code = SourceScanPins.ReadCode(path);
				LogAssert.IsTrue(code.Contains("Graphics.RenderMesh(") || code.Contains("Graphics.RenderMeshPrimitives(") || path.EndsWith(".CavePools.cs"),
					$"{path} no longer draws: the scan above would be checking a file that does nothing");
			}
		}

		/// <summary>The text of a file's UnityPerMaterial block, or null.</summary>
		private static string PerMaterialBlock(string code)
		{
			int start = code.IndexOf("CBUFFER_START(UnityPerMaterial)", System.StringComparison.Ordinal);
			if (start < 0)
			{
				return null;
			}
			int end = code.IndexOf("CBUFFER_END", start, System.StringComparison.Ordinal);
			return end < 0 ? null : code.Substring(start, end - start);
		}

		private static Regex Declaration(string name) => new Regex(@"\b(float|half|int|uint|bool)[1-4]?(x[1-4])?\s+" + Regex.Escape(name) + @"\s*;");

		/// <summary>Why a shader's per-draw numbers are not all in its UnityPerMaterial (declared once, there), or null.</summary>
		private static System.Func<string, string> InPerMaterial(string[] names) => code =>
		{
			string block = PerMaterialBlock(code);
			if (block == null)
			{
				return "it has no UnityPerMaterial block";
			}
			foreach (string name in names)
			{
				if (!Declaration(name).IsMatch(block))
				{
					return $"{name} is not in UnityPerMaterial: under the SRP Batcher the draw would not read the material's value";
				}
				int declared = Declaration(name).Matches(code).Count;
				if (declared != 1)
				{
					return $"{name} is declared {declared} times";
				}
			}
			return null;
		};

		[Test]
		public void TheConvertedShaders_KeepTheirPerDrawNumbersInUnityPerMaterial()
		{
			foreach ((string file, string[] names) in PerDrawNumbers)
			{
				string code = SourceScanPins.ReadCode(file);
				SourceScanPins.HoldsAndFires(file, code, InPerMaterial(names),
					SourceScanPins.InsertBefore("CBUFFER_START(UnityPerMaterial)", "float4 " + names[0] + ";\n"),
					"the first number declared loose again");
			}
		}

		[Test]
		public void TheVortexDebris_TakesItsNumbersFromTheSharedBlock()
		{
			string debris = SourceScanPins.ReadCode(Shaders + "FishVortexDebris.shader");
			LogAssert.IsTrue(debris.Contains("#define FISH_VORTEX_DEBRIS"), "the debris shader asks FishVortex.hlsl for its numbers");
			LogAssert.IsFalse(Declaration("_VortexDebris").IsMatch(debris), "and declares none of its own beside them");
			LogAssert.IsFalse(SourceScanPins.ReadCode(Shaders + "FishVortex.shader").Contains("#define FISH_VORTEX_DEBRIS"),
				"the volume's block keeps one layout without the debris numbers");
		}

		[Test]
		public void TheBackdrop_KeepsTheBindersNumbersInUnityPerMaterial()
		{
			string input = SourceScanPins.ReadCode(Shaders + "FishWeatherTerrainArrayInput.hlsl");
			string block = PerMaterialBlock(input);
			LogAssert.IsNotNull(block, "the terrain input declares UnityPerMaterial");
			LogAssert.IsNull(SourceScanPins.InOrder(block, "#ifdef FISH_TERRAIN_BACKDROP", "FISH_TERRAIN_ARRAY_NUMBERS", "#endif"),
				"the backdrop's UnityPerMaterial carries the binder's numbers");
			LogAssert.IsNull(SourceScanPins.InOrder(input, "#define FISH_TERRAIN_ARRAY_NUMBERS", "CBUFFER_START(UnityPerMaterial)", "#ifndef FISH_TERRAIN_BACKDROP"),
				"the shared list is defined before both blocks");
			int tiles = input.IndexOf("#ifndef FISH_TERRAIN_BACKDROP", System.StringComparison.Ordinal);
			LogAssert.IsNull(SourceScanPins.InOrder(tiles < 0 ? null : input.Substring(tiles), "#ifndef FISH_TERRAIN_BACKDROP",
				"CBUFFER_START(_FishTerrainArray)", "FISH_TERRAIN_ARRAY_NUMBERS", "CBUFFER_END", "#endif"),
				"the tiles keep them in _FishTerrainArray, which the backdrop no longer declares");
			foreach (string name in new[] { "_FishArrayInfo", "_FishControl0_TexelSize", "_FishPathLayers", "_FishSnowHoleFade_TexelSize" })
			{
				LogAssert.IsTrue(Declaration(name).Matches(input).Count == 1, $"{name} is declared once, in the shared list");
			}
		}

		[Test]
		public void TheBackdropRings_DrawWithACopyInPlay_AndABlockOnlyInEditMode()
		{
			string binder = SourceScanPins.ReadCode("Assets/Scripts/Shared/Implementation/Entity/WorldSceneDetails/Terrain/TerrainArrayBinder.cs");
			string push = SourceScanPins.Body(binder, "private void PushTo(Renderer renderer)");
			LogAssert.IsNull(SourceScanPins.InOrder(push, "if (Application.isPlaying)", "BackdropCopyFor(renderer)", "return;", "BlockFor(renderer)", "SetPropertyBlock(block)"),
				"in play a ring draws with its copy and returns before any block is made");
			string copy = SourceScanPins.Body(binder, "private Material BackdropCopyFor(Renderer renderer)");
			LogAssert.IsNull(SourceScanPins.InOrder(copy, "new Material(source)", "HideFlags.HideAndDontSave", "SetPropertyBlock(null)", "renderer.sharedMaterial = copy"),
				"the copy is never saved, and the ring is left with no block");
			string unbind = SourceScanPins.Body(binder, "private void Unbind()");
			LogAssert.IsNull(SourceScanPins.InOrder(unbind, "backdropCopies", "pair.Key.sharedMaterial = pair.Value.Source", "Destroy(pair.Value.Copy)", "backdropCopies.Clear()"),
				"unbinding puts the ring's own material back and frees the copy");
		}

		[Test]
		public void TheClient_DrawsTerrainHeightmapsAtFourPixels_WithTheCutLeftAtTwo()
		{
			Assert.That(ClientTerrainLod.HeightmapPixelError, Is.EqualTo(4f));
			Assert.That(ClientTerrainLod.DrawnPixelError(2f), Is.EqualTo(4f), "the cut's 2 px is drawn at 4");
			Assert.That(ClientTerrainLod.DrawnPixelError(1f), Is.EqualTo(4f), "nothing finer is drawn");
			Assert.That(ClientTerrainLod.DrawnPixelError(8f), Is.EqualTo(8f), "a coarser tile keeps its own");
			string cut = SourceScanPins.ReadCode("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/SceneGenerator.cs");
			LogAssert.IsTrue(cut.Contains("public const float TerrainPixelError = 2f;"), "the cut still stores 2 px on the tiles: only play draws coarser");
			string lod = SourceScanPins.ReadCode(Client + "Terrain/ClientTerrainLod.cs");
			LogAssert.IsNull(SourceScanPins.InOrder(lod, "if (hooked || !Application.isPlaying)", "SceneManager.sceneLoaded += OnSceneLoaded"),
				"only a play session changes a tile");
			LogAssert.IsTrue(lod.Contains("terrain.heightmapPixelError = DrawnPixelError(own);"), "each tile is set from its own error");
		}
	}
}
