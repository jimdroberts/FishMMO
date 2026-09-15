using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The equipment visual controller and the client naming system release what they acquire
	/// per spawn, per equip and per name request.
	/// </summary>
	/// <remarks>
	/// <para>Source scans, because every defect pinned here was invisible to behaviour: the
	/// picture was right and the memory was wrong. They came out of the 2026-09-15 hunt for a
	/// client that grew about a megabyte a minute in a world scene and were verified by reading
	/// the Addressables 3.1 source, not by a profiler — so the pins say exactly what the code
	/// must keep doing, and a profiler run in the client is what confirms the rate.</para>
	/// <list type="bullet">
	/// <item>A shared <c>AssetReference</c> holds one operation; loading through it a second
	/// time logs an error, returns an invalid handle, and subscribing to that handle throws.
	/// Equipment loads go by runtime key, one refcounted handle per equip.</item>
	/// <item>A <c>Mesh</c> baked from a skinned weapon prefab is a native object the weapon's
	/// GameObject does not own; it is destroyed with the slot.</item>
	/// <item>The static skeleton bone cache is keyed by root instance ID; a race model reloaded
	/// on a live character must clear the outgoing root's entry.</item>
	/// <item>The server answers a debounced name request with nothing; the client re-sends a
	/// pending request instead of appending callbacks to it forever.</item>
	/// </list>
	/// </remarks>
	[TestFixture]
	public class EquipmentVisualLifetimeTests
	{
		private const string ControllerPath = "Assets/Scripts/Shared/Implementation/Entity/Appearance/EquipmentVisualController.cs";
		private const string NamingPath = "Assets/Scripts/Client/ClientNamingSystem.cs";

		[Test]
		public void EquipmentLoadsGoByRuntimeKeyNotThroughTheSharedAssetReference()
		{
			string code = CodeOnly(Read(ControllerPath));
			LogAssert.IsFalse(code.Contains("assetRef.LoadAssetAsync"), "EquipmentVisualController must not load through the template's AssetReference: it holds one operation and refuses a second concurrent load with an invalid handle");
			LogAssert.IsTrue(code.Contains("Addressables.LoadAssetAsync<GameObject>(assetRef.RuntimeKey)"), "equipment prefabs load by runtime key, one handle per equip");
		}

		[Test]
		public void ABakedWeaponMeshIsOwnedBySlotAndDestroyedOnRelease()
		{
			string code = CodeOnly(Read(ControllerPath));
			LogAssert.IsTrue(code.Contains("renderer.BakedMesh = bm;"), "the baked mesh is recorded on the slot that displays it");

			string release = MethodBody(code, "private void ReleaseSlotRenderer(SlotRenderer renderer)");
			LogAssert.IsTrue(release.Contains("renderer.BakedMesh"), "ReleaseSlotRenderer destroys the baked mesh; destroying the weapon GameObject does not free it");
			LogAssert.IsTrue(release.Contains("Destroy(renderer.BakedMesh)"), "the baked mesh is destroyed, not merely dropped");
		}

		[Test]
		public void ReloadingTheModelClearsTheOutgoingSkeletonsBoneCache()
		{
			string body = MethodBody(CodeOnly(Read(ControllerPath)), "private bool TryRefreshModelState()");
			LogAssert.IsTrue(body.Contains("SkeletonBinder.ClearBoneCache(previousRoot)"), "TryRefreshModelState clears the bone cache of the root it is replacing; TearDownVisuals only ever clears the current one");
		}

		[Test]
		public void APendingNameRequestIsSentAgainInsteadOfWaitingForever()
		{
			string body = MethodBody(CodeOnly(Read(NamingPath)), "public static void SetName(NamingSystemType type, long id, Action<string> action)");
			LogAssert.IsTrue(body.Contains("NameRequestRetrySeconds"), "SetName re-sends a request that has been pending longer than the retry window");
			LogAssert.AreEqual(2, CountOccurrences(body, "new NamingBroadcast()"), "one send for a new request and one re-send for a stale pending one");
		}

		// ── helpers ───────────────────────────────────────────────────────────

		private static string Read(string path)
		{
			LogAssert.IsTrue(File.Exists(path), path + " exists");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		private static int CountOccurrences(string text, string needle)
		{
			int count = 0;
			for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
			{
				++count;
			}
			return count;
		}

		/// <summary>Brace-matches the body following the first occurrence of <paramref name="signature"/>.</summary>
		private static string MethodBody(string source, string signature)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "found: " + signature);
			int open = source.IndexOf('{', start);
			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{') ++depth;
				else if (source[i] == '}' && --depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
			LogAssert.Fail("unterminated body for " + signature);
			return string.Empty;
		}

		/// <summary>Strips block comments and comment lines so prose cannot trip a code scan.</summary>
		private static string CodeOnly(string source)
		{
			while (true)
			{
				int open = source.IndexOf("/*", StringComparison.Ordinal);
				if (open < 0)
				{
					break;
				}

				int close = source.IndexOf("*/", open + 2, StringComparison.Ordinal);
				source = close < 0 ? source.Substring(0, open) : source.Remove(open, close - open + 2);
			}

			StringBuilder kept = new StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}

				kept.Append(line).Append('\n');
			}

			return kept.ToString();
		}
	}
}
