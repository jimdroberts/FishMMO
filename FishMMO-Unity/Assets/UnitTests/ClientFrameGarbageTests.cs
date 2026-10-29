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
	/// <summary>
	/// Pins the client GC pass of 2026-10-27: nothing the client runs every frame makes garbage. The profiler then showed a
	/// steady ~9 KB a frame, nearly half of it from one getter (the "Active" profile lookups, ~110 calls a frame at 40 bytes
	/// each), the rest from strings formatted in render passes, setting names rebuilt per read and terrain arrays.
	/// </summary>
	/// <remarks>
	/// Allocation is measured where it can be: with the thread's allocation counter outside Unity (it is exact there), and
	/// with Unity's own GC.Alloc recorder in the editor, where the counter reads a flat zero (a control allocation decides
	/// which). The rest are source scans, as the neighbouring pins are: a formatted string in a render hook compiles and
	/// passes everything else.
	/// </remarks>
	[TestFixture]
	public class ClientFrameGarbageTests
	{
		// ── Measured ────────────────────────────────────────────────

		/// <summary>A cached type of its own, so the test's entry is the only one its cache holds.</summary>
		private sealed class FrameGarbageProbe : CachedScriptableObject<FrameGarbageProbe>, ICachedObject
		{
		}

		[Test]
		public void GetFirst_AllocatesNothing()
		{
			// Made without its constructor (a ScriptableObject's asks the engine for a native object) and filed straight into the
			// cache: AddToCache logs through ScriptableObject.name, an engine call too.
			var probe = (FrameGarbageProbe)FormatterServices.GetUninitializedObject(typeof(FrameGarbageProbe));
			FieldInfo field = typeof(CachedScriptableObject<FrameGarbageProbe>).GetField("resourceCache", BindingFlags.NonPublic | BindingFlags.Static);
			LogAssert.IsTrue(field != null, "CachedScriptableObject must still keep its cache in resourceCache");
			var cache = (Dictionary<Type, Dictionary<int, FrameGarbageProbe>>)field.GetValue(null);
			cache[typeof(FrameGarbageProbe)] = new Dictionary<int, FrameGarbageProbe> { [1] = probe, [2] = probe };
			try
			{
				LogAssert.IsTrue(ReferenceEquals(CachedScriptableObject<FrameGarbageProbe>.GetFirst<FrameGarbageProbe>(), probe),
					"GetFirst must still answer the cached object");
				AssertAllocatesNothing(() => CachedScriptableObject<FrameGarbageProbe>.GetFirst<FrameGarbageProbe>(),
					"GetFirst (behind SolarSystemProfile.Active, WorldAtlas.Active and WeatherRenderProfile.Active, asked ~110 times a frame)");
			}
			finally
			{
				cache.Remove(typeof(FrameGarbageProbe));
			}
		}

		[Test]
		public void ConfigurationReads_AllocateNothingOnRepeat()
		{
			string directory = Path.Combine(Path.GetTempPath(), "FishMMO-FrameGarbage-" + Guid.NewGuid().ToString("N"));
			var configuration = new Configuration(directory);
			try
			{
				configuration.Set("Clouds.Enabled", "true");
				// The client reads settings like this one every frame (ClientCloudSettings.Enabled and its neighbours).
				LogAssert.IsTrue(configuration.TryGetBool("Clouds.Enabled", out bool on, false) && on, "the setting must still be read");
				AssertAllocatesNothing(() => configuration.TryGetBool("Clouds.Enabled", out bool _, false),
					"Configuration.TryGetBool (its environment-variable name was rebuilt on every read)");
			}
			finally
			{
				configuration.Dispose();
			}
		}

		private static void AssertAllocatesNothing(Action action, string what)
		{
			action();
			action();
			if (!AllocationCounterWorks())
			{
				AssertAllocatesNothingInUnity(action);
				return;
			}
			const int Calls = 1000;
			long before = GC.GetAllocatedBytesForCurrentThread();
			for (int i = 0; i < Calls; i++)
			{
				action();
			}
			long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
			LogAssert.IsTrue(bytes == 0, $"{what} allocated {bytes} bytes over {Calls} calls; it must allocate none");
		}

		/// <summary>Whether the thread's allocation counter sees a control allocation (it reads zero under Unity's Mono).</summary>
		private static bool AllocationCounterWorks()
		{
			long before = GC.GetAllocatedBytesForCurrentThread();
			object[] control = new object[32];
			GC.KeepAlive(control);
			return GC.GetAllocatedBytesForCurrentThread() - before > 0;
		}

		/// <summary>Unity's own recorder (in its own method so a runner without the test framework never compiles it).</summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void AssertAllocatesNothingInUnity(Action action)
		{
			Assert.That(() => action(), UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(NUnit.Framework.Is.Not));
		}

		// ── Source pins ─────────────────────────────────────────────

		/// <summary>Where the client's per-frame render code lives.</summary>
		private static readonly string[] RenderRoots =
		{
			"Assets/Scripts/Client",
			"Assets/Plugins/FishMMO Water/Scripts",
		};

		private static readonly Regex HookSignature = new Regex(
			@"(override\s+void\s+RecordRenderGraph|void\s+OnBeginCamera\w*|void\s+OnBeginContext\w*)\s*\(", RegexOptions.Compiled);

		private static readonly Regex Formatting = new Regex(
			@"\$@?""|@\$""|string\.Format|String\.Format|StringBuilder|string\.Concat|string\.Join", RegexOptions.Compiled);

		[Test]
		public void RenderHooks_FormatNoStrings()
		{
			var offenders = new List<string>();
			int hooks = 0;
			foreach (string file in RenderSources())
			{
				string source = Read(file);
				foreach (Match m in HookSignature.Matches(source))
				{
					string body = BodyAfter(source, m.Index + m.Length);
					if (body == null)
					{
						continue;
					}
					hooks++;
					foreach (string line in CodeLines(body))
					{
						// A one-off log (a state change, a census) is fine; the hot path is what is pinned.
						if (Formatting.IsMatch(line) && !line.Contains("Debug.Log"))
						{
							offenders.Add($"{file}: {line.Trim()}");
						}
					}
				}
			}
			LogAssert.IsTrue(hooks >= 30, $"the scan must find the render hooks (found {hooks}): has the signature changed?");
			LogAssert.IsTrue(offenders.Count == 0, "render hooks must not format strings every frame:\n" + string.Join("\n", offenders));
		}

		[Test]
		public void ShadowCascadeCulling_FormatsItsReportOnlyWhenRead()
		{
			string source = Read("Assets/Scripts/Client/World/Terrain/ShadowCascadeCulling.cs");
			string spheres = BodyAfter(source, IndexOrFail(source, "private static int Spheres(") + 1);
			foreach (string line in CodeLines(spheres))
			{
				LogAssert.IsFalse(Formatting.IsMatch(line), $"Spheres runs every frame and must not format its report: {line.Trim()}");
			}
			LogAssert.IsTrue(source.Contains("public static string LastReport => FormatReport();"), "the report must be formatted when read");
		}

		[Test]
		public void GetFirst_UsesNoLinq()
		{
			string source = Read("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/CachedScriptableObject/CachedScriptableObject.cs");
			string body = BodyAfter(source, IndexOrFail(source, "public static U GetFirst<U>()") + 1);
			LogAssert.IsFalse(CodeLines(body).Any(l => l.Contains("FirstOrDefault") || l.Contains(".First(")),
				"Linq's FirstOrDefault over Dictionary.Values boxes its enumerator: 40 bytes a call");
		}

		/// <summary>Per-frame ground and terrain reads: <c>Terrain.activeTerrains</c> builds a new array on every read.</summary>
		private static readonly string[] PerFrameTerrainReaders =
		{
			"Assets/Scripts/Client/World/Weather/Presentation/FogLayerView.cs",
			"Assets/Scripts/Client/World/Weather/Sky/SkyEffects.cs",
			"Assets/Scripts/Client/World/Weather/Sky/VortexPresenter.cs",
			"Assets/Scripts/Client/World/Weather/Sky/CloudTerrainMap.cs",
			"Assets/Scripts/Client/World/Terrain/RockfallField.cs",
			"Assets/Scripts/Client/Audio/World/ThunderPresenter.cs",
			"Assets/Plugins/FishMMO Water/Scripts/InlandWaterRenderer.Lava.cs",
			"Assets/Plugins/FishMMO Water/Scripts/FallRock.cs",
		};

		[Test]
		public void PerFrameTerrainReads_UseTheReusedList()
		{
			foreach (string file in PerFrameTerrainReaders)
			{
				foreach (string line in CodeLines(Read(file)))
				{
					LogAssert.IsFalse(line.Contains("Terrain.activeTerrains"), $"{file} reads Terrain.activeTerrains (a new array a read): {line.Trim()}");
				}
			}
			string plume = Read("Assets/Scripts/Client/World/Weather/Sky/VolcanicPlumePresenter.cs");
			string groundAt = BodyAfter(plume, IndexOrFail(plume, "private static float GroundAt(Scene scene, Vector2 p)") + 1);
			LogAssert.IsFalse(groundAt.Contains("Terrain.activeTerrains"), "VolcanicPlumePresenter.GroundAt runs every frame");
		}

		[Test]
		public void VolcanicPlume_SortsWithoutCapturingLambdas()
		{
			string source = Read("Assets/Scripts/Client/World/Weather/Sky/VolcanicPlumePresenter.cs");
			LogAssert.IsFalse(source.Contains("plumes.Sort((a, b)"), "a lambda capturing the viewer is a closure and a delegate every camera, every frame");
			LogAssert.IsFalse(source.Contains("curtains.Sort((a, b)"), "a lambda capturing the viewer is a closure and a delegate every camera, every frame");
		}

		// ── Follow-up pass (2026-10-27, allocation callstacks) ──────

		[Test]
		public void PerFrameSync_ReusesNoStopwatch()
		{
			foreach (string file in new[] { "Assets/Scripts/Client/World/Terrain/Grass/GrassBladeSystem.cs", "Assets/Scripts/Client/World/Terrain/Scatter/DetailScatterSystem.cs" })
			{
				foreach (string line in CodeLines(Read(file)))
				{
					LogAssert.IsFalse(line.Contains("Stopwatch.StartNew"), $"{file}: Stopwatch.StartNew is an object a frame; read Stopwatch.GetTimestamp: {line.Trim()}");
				}
			}
		}

		[Test]
		public void PlanetSurfaceLibrary_ReadsTheNameOncePerBody()
		{
			string source = Read("Assets/Scripts/Client/World/Weather/Sky/PlanetSurfaceLibrary.cs");
			string get = BodyAfter(source, IndexOrFail(source, "public static Texture2D Get(CelestialBody body)") + 1);
			LogAssert.IsTrue(get.Contains("names.TryGetValue(body"), "Get must look the name up by instance first (Object.name builds a string per read)");
			LogAssert.IsTrue(CodeLines(get).Count(l => l.Contains("body.name")) == 1, "Get may read body.name only on the cache miss");
		}

		[Test]
		public void ShadeWorkers_StartInTheirOwnMethod()
		{
			// A lambda's closure is made on entry to the scope that owns the captured locals: left in the per-frame method, it was
			// 120 / 88 / 56 bytes of garbage a frame for a worker started a few times a minute.
			foreach (string file in new[] { "Assets/Scripts/Client/World/Weather/Sky/MistGroundMap.cs", "Assets/Scripts/Client/World/Weather/Sky/CloudFlowField.cs" })
			{
				string source = Read(file);
				string shade = BodyAfter(source, IndexOrFail(source, "private void Shade(Vector3 toLight") + 1);
				LogAssert.IsFalse(shade.Contains("Task.Run"), $"{file}: Shade runs every frame and must not hold the worker's lambda");
				LogAssert.IsTrue(source.Contains("private void StartShade("), $"{file}: the worker starts in StartShade");
			}
			string breakers = Read("Assets/Plugins/FishMMO Water/Scripts/WaterBreakers.cs");
			string hook = BodyAfter(breakers, IndexOrFail(breakers, "private void OnBeginCameraRendering(") + 1);
			LogAssert.IsFalse(hook.Contains("Task.Run"), "WaterBreakers' camera hook must not hold the trace worker's lambda");
		}

		[Test]
		public void CloudPass_FormatChoicesAreMadeOnce()
		{
			string source = Read("Assets/Scripts/Client/World/Weather/Sky/FishCloudsFeature.cs");
			foreach (string name in new[] { "public static GraphicsFormat MotionFormat()", "public static GraphicsFormat WeightFormat(bool randomWrite)" })
			{
				string body = BodyAfter(source, IndexOrFail(source, name) + 1);
				LogAssert.IsFalse(body.Contains("SystemInfo.") || body.Contains("GraphicsFormat[]"), $"{name} runs every frame: it must answer from its cache");
			}
		}

		[Test]
		public void GrassAndAmbient_MakeNoDelegatesOrSortersPerFrame()
		{
			string grass = Read("Assets/Scripts/Client/World/Terrain/Grass/GrassBladeRenderer.cs");
			LogAssert.IsFalse(grass.Contains("atlas.Sync(terrains, AlbedoFor)"), "a method group is a new delegate on every conversion: cache it");
			string ambient = Read("Assets/Scripts/Client/World/AmbientLife/AmbientLifeSystem.cs");
			string evaluate = BodyAfter(ambient, IndexOrFail(ambient, "private static int Evaluate(") + 1);
			LogAssert.IsFalse(evaluate.Contains(".Sort("), "List.Sort with a comparer makes a delegate per call: SortByDistance sorts in place");
		}

		// ── Helpers ─────────────────────────────────────────────────

		private static IEnumerable<string> RenderSources()
		{
			string root = Directory.GetCurrentDirectory();
			foreach (string relative in RenderRoots)
			{
				string dir = Path.Combine(root, relative);
				LogAssert.IsTrue(Directory.Exists(dir), $"{relative} not found under {root}");
				foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
				{
					string normal = file.Replace('\\', '/');
					if (!normal.Contains("/Editor/"))
					{
						yield return normal.Substring(root.Length + 1);
					}
				}
			}
		}

		private static string Read(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static int IndexOrFail(string source, string anchor)
		{
			int index = source.IndexOf(anchor, StringComparison.Ordinal);
			LogAssert.IsTrue(index >= 0, $"the source must still declare {anchor}");
			return index;
		}

		/// <summary>The braced body of the declaration starting before <paramref name="from"/>; null for one without a body.</summary>
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
