using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>Split from ClientFrameGarbageTests: the World Simulation module's half.</summary>
	public class WeatherFrameGarbageTests
	{
		[Test]
		public void PerFrameTerrainReads_UseTheReusedList()
		{
			foreach (string file in PerFrameTerrainReaders)
			{
				if (InstalledModules.IsAbsent(file))
				{
					continue;
				}
				foreach (string line in CodeLines(Read(file)))
				{
					LogAssert.IsFalse(line.Contains("Terrain.activeTerrains"), $"{file} reads Terrain.activeTerrains (a new array a read): {line.Trim()}");
				}
			}
			string plume = Read("Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Sky/VolcanicPlumePresenter.cs");
			string groundAt = BodyAfter(plume, IndexOrFail(plume, "private static float GroundAt(Scene scene, Vector2 p)") + 1);
			LogAssert.IsFalse(groundAt.Contains("Terrain.activeTerrains"), "VolcanicPlumePresenter.GroundAt runs every frame");
		}

		[Test]
		public void VolcanicPlume_SortsWithoutCapturingLambdas()
		{
			string source = Read("Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Sky/VolcanicPlumePresenter.cs");
			LogAssert.IsFalse(source.Contains("plumes.Sort((a, b)"), "a lambda capturing the viewer is a closure and a delegate every camera, every frame");
			LogAssert.IsFalse(source.Contains("curtains.Sort((a, b)"), "a lambda capturing the viewer is a closure and a delegate every camera, every frame");
		}

		[Test]
		public void PlanetSurfaceLibrary_ReadsTheNameOncePerBody()
		{
			string source = Read("Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Sky/PlanetSurfaceLibrary.cs");
			string get = BodyAfter(source, IndexOrFail(source, "public static Texture2D Get(CelestialBody body)") + 1);
			LogAssert.IsTrue(get.Contains("names.TryGetValue(body"), "Get must look the name up by instance first (Object.name builds a string per read)");
			LogAssert.IsTrue(CodeLines(get).Count(l => l.Contains("body.name")) == 1, "Get may read body.name only on the cache miss");
		}

		[Test]
		public void ShadeWorkers_StartInTheirOwnMethod()
		{
			// A lambda's closure is made on entry to the scope that owns the captured locals: left in the per-frame method, it was
			// 120 / 88 / 56 bytes of garbage a frame for a worker started a few times a minute.
			foreach (string file in new[] { "Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Sky/MistGroundMap.cs", "Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Sky/CloudFlowField.cs" })
			{
				string source = Read(file);
				string shade = BodyAfter(source, IndexOrFail(source, "private void Shade(Vector3 toLight") + 1);
				LogAssert.IsFalse(shade.Contains("Task.Run"), $"{file}: Shade runs every frame and must not hold the worker's lambda");
				LogAssert.IsTrue(source.Contains("private void StartShade("), $"{file}: the worker starts in StartShade");
			}
			string breakers = Read("Assets/FishMMO/Modules/Water/Scripts/WaterBreakers.cs");
			string hook = BodyAfter(breakers, IndexOrFail(breakers, "private void OnBeginCameraRendering(") + 1);
			LogAssert.IsFalse(hook.Contains("Task.Run"), "WaterBreakers' camera hook must not hold the trace worker's lambda");
		}

		[Test]
		public void CloudPass_FormatChoicesAreMadeOnce()
		{
			string source = Read("Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Sky/FishCloudsFeature.cs");
			foreach (string name in new[] { "public static GraphicsFormat MotionFormat()", "public static GraphicsFormat WeightFormat(bool randomWrite)" })
			{
				string body = BodyAfter(source, IndexOrFail(source, name) + 1);
				LogAssert.IsFalse(body.Contains("SystemInfo.") || body.Contains("GraphicsFormat[]"), $"{name} runs every frame: it must answer from its cache");
			}
		}

		/// <summary>Per-frame ground and terrain reads: <c>Terrain.activeTerrains</c> builds a new array on every read.</summary>
		private static readonly string[] PerFrameTerrainReaders =
		{
			"Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Presentation/FogLayerView.cs",
			"Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Sky/SkyEffects.cs",
			"Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Sky/VortexPresenter.cs",
			"Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Sky/CloudTerrainMap.cs",
			"Assets/FishMMO/Modules/AmbientLife/Client/SiteLife/RockfallField.cs",
			"Assets/FishMMO/Modules/WorldSimulation/Client/Audio/World/ThunderPresenter.cs",
			"Assets/FishMMO/Modules/Water/Scripts/InlandWaterRenderer.Lava.cs",
			"Assets/FishMMO/Modules/Water/Scripts/FallRock.cs",
		};

		private static string Read(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(ProjectFiles.Exists(path), $"{relativePath} not found at {path}");
			return ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static int IndexOrFail(string source, string anchor)
		{
			int index = source.IndexOf(anchor, StringComparison.Ordinal);
			LogAssert.IsTrue(index >= 0, $"the source must still declare {anchor}");
			return index;
		}

		private static string BodyAfter(string source, int from)
		{
			int open = source.IndexOf('{', from);
			int semicolon = source.IndexOf(';', from);
			if (open < 0 || (semicolon >= 0 && semicolon < open))
			{
				return null;
			}
			int depth = 0;
			for (int i = open; i < source.Length; i++)
			{
				if (source[i] == '{')
				{
					depth++;
				}
				else if (source[i] == '}' && --depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
			return source.Substring(open);
		}

		/// <summary>Lines that are code, not comment.</summary>
		private static IEnumerable<string> CodeLines(string text)
		{
			foreach (string line in text.Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
				{
					continue;
				}
				int comment = trimmed.IndexOf("//", StringComparison.Ordinal);
				yield return comment >= 0 && !trimmed.Contains("\"") ? trimmed.Substring(0, comment) : trimmed;
			}
		}
	}
}
