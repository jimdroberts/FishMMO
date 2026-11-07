using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.CustomBuildTool.Addressables;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Plan = FishMMO.Shared.CustomBuildTool.Addressables.AddressablesBuildPlan;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The build planner on an in-memory project: which bundles each build has, and which implicit
	/// dependencies two or more of them would copy. These are the rules the 2026-11-06 server build
	/// broke (46 GB of textures copied into hundreds of bundles) without any check noticing.
	/// </summary>
	[TestFixture]
	public class AddressablesBuildPlanTests
	{
		private static Plan.Sources Sources(Dictionary<string, string[]> graph) => new Plan.Sources
		{
			DirectDependencies = path => graph.TryGetValue(path, out string[] deps) ? deps : Array.Empty<string>(),
			MainType = TypeOf,
			IsBundled = path => !path.EndsWith(".cs", StringComparison.Ordinal),
		};

		private static Type TypeOf(string path)
		{
			switch (Path.GetExtension(path))
			{
				case ".mat": return typeof(Material);
				case ".png": return typeof(Texture2D);
				case ".prefab": return typeof(GameObject);
				case ".unity": return typeof(SceneAsset);
				case ".fbx": return typeof(Mesh);
				default: return typeof(ScriptableObject);
			}
		}

		[Test]
		public void PackSeparatelyEntriesOfOneGroupAreDifferentBundles_SoWhatTheyShareIsShared()
		{
			// The old check counted groups: two props in one PackSeparately group "shared" nothing.
			var graph = new Dictionary<string, string[]>
			{
				["Assets/A.prefab"] = new[] { "Assets/Look.mat", "Assets/Rock.fbx" },
				["Assets/B.prefab"] = new[] { "Assets/Look.mat", "Assets/Rock.fbx" },
				["Assets/Look.mat"] = new[] { "Assets/LookArray.png", "Assets/Look.cs" },
			};
			var entries = new[]
			{
				new Plan.Entry("Assets/A.prefab", "Scene_Shared", Plan.Packing.Separately),
				new Plan.Entry("Assets/B.prefab", "Scene_Shared", Plan.Packing.Separately),
			};
			Plan client = Plan.Compute(entries, false, Plan.ExclusionsFor(false), Sources(graph));
			Assert.That(client.BundleEntries.Count, Is.EqualTo(2));
			Assert.That(client.Shared, Is.EquivalentTo(new[] { "Assets/Look.mat", "Assets/LookArray.png", "Assets/Rock.fbx" }));
			Assert.That(client.ImplicitUsers.ContainsKey("Assets/Look.cs"), Is.False, "scripts are never bundled");

			// The server strips everything visual: only the mesh is shared there.
			Plan server = Plan.Compute(entries, true, Plan.ExclusionsFor(true), Sources(graph));
			Assert.That(server.Shared, Is.EquivalentTo(new[] { "Assets/Rock.fbx" }));
		}

		[Test]
		public void PackTogetherEntriesAreOneBundle_SoTheirCommonDependencyIsNotShared()
		{
			var graph = new Dictionary<string, string[]>
			{
				["Assets/A.asset"] = new[] { "Assets/Data.asset" },
				["Assets/B.asset"] = new[] { "Assets/Data.asset" },
			};
			var entries = new[]
			{
				new Plan.Entry("Assets/A.asset", "Shared_Static_Permanent", Plan.Packing.Together),
				new Plan.Entry("Assets/B.asset", "Shared_Static_Permanent", Plan.Packing.Together),
			};
			Plan plan = Plan.Compute(entries, false, Plan.ExclusionsFor(false), Sources(graph));
			Assert.That(plan.BundleEntries.Count, Is.EqualTo(1));
			Assert.That(plan.Shared, Is.Empty);
			Assert.That(plan.ImplicitUsers["Assets/Data.asset"].Count, Is.EqualTo(1));
		}

		[Test]
		public void AnAddressableDependencyIsReferencedNotCopied_ButOnlyInBuildsThatIncludeItsGroup()
		{
			var graph = new Dictionary<string, string[]>
			{
				["Assets/A.prefab"] = new[] { "Assets/Body.fbx" },
				["Assets/B.prefab"] = new[] { "Assets/Body.fbx" },
				["Assets/Body.fbx"] = new[] { "Assets/Rig.asset" },
			};
			var entries = new[]
			{
				new Plan.Entry("Assets/A.prefab", "Shared_Dynamic", Plan.Packing.Separately),
				new Plan.Entry("Assets/B.prefab", "Shared_Dynamic", Plan.Packing.Separately),
				new Plan.Entry("Assets/Body.fbx", "Client_Dynamic", Plan.Packing.Separately),
			};
			Plan client = Plan.Compute(entries, false, Plan.ExclusionsFor(false), Sources(graph));
			Assert.That(client.Shared, Is.Empty, "the client includes Client_Dynamic: the model is one bundle, referenced by both");
			Assert.That(client.ImplicitUsers["Assets/Rig.asset"], Is.EquivalentTo(new[] { "Client_Dynamic|Assets/Body.fbx" }));

			// The server drops every Client group, so the model is implicit there and both prefabs would copy it.
			Plan server = Plan.Compute(entries, true, Plan.ExclusionsFor(true), Sources(graph));
			Assert.That(server.Explicit.Contains("Assets/Body.fbx"), Is.False);
			Assert.That(server.Shared, Is.EquivalentTo(new[] { "Assets/Body.fbx", "Assets/Rig.asset" }));
		}

		[Test]
		public void ScenesAndAssetsOfOnePackTogetherGroupAreSeparateBundles_AndLabelsSplitByLabel()
		{
			Assert.That(Plan.BundleId(new Plan.Entry("Assets/S.unity", "G", Plan.Packing.Together)),
				Is.Not.EqualTo(Plan.BundleId(new Plan.Entry("Assets/A.asset", "G", Plan.Packing.Together))));
			string ab = Plan.BundleId(new Plan.Entry("Assets/A.asset", "G", Plan.Packing.TogetherByLabel, new[] { "b", "a" }));
			string ba = Plan.BundleId(new Plan.Entry("Assets/B.asset", "G", Plan.Packing.TogetherByLabel, new[] { "a", "b" }));
			string c = Plan.BundleId(new Plan.Entry("Assets/C.asset", "G", Plan.Packing.TogetherByLabel, new[] { "c" }));
			Assert.That(ab, Is.EqualTo(ba));
			Assert.That(ab, Is.Not.EqualTo(c));
		}

		[Test]
		public void TheServerPlanLeavesOutExplicitVisualAssets()
		{
			var entries = new[]
			{
				new Plan.Entry("Assets/Ui.png", "Shared_Dynamic", Plan.Packing.Separately),
				new Plan.Entry("Assets/Look.mat", "Shared_Dynamic", Plan.Packing.Separately),
				new Plan.Entry("Assets/Data.asset", "Shared_Dynamic", Plan.Packing.Separately),
			};
			Plan server = Plan.Compute(entries, true, Plan.ExclusionsFor(true), Sources(new Dictionary<string, string[]>()));
			Assert.That(server.Explicit, Is.EquivalentTo(new[] { "Assets/Data.asset" }));
			Assert.That(server.EntryCount, Is.EqualTo(1));
		}

		[Test]
		public void BuildTimeEntriesCountAsAddressable()
		{
			var graph = new Dictionary<string, string[]>
			{
				["Assets/A.prefab"] = new[] { "Assets/Generated/Mesh.asset" },
				["Assets/B.prefab"] = new[] { "Assets/Generated/Mesh.asset" },
			};
			Plan.Sources sources = Sources(graph);
			sources.IsBuildTimeEntry = (path, server) => path.StartsWith("Assets/Generated/", StringComparison.Ordinal);
			var entries = new[]
			{
				new Plan.Entry("Assets/A.prefab", "Scene_Shared", Plan.Packing.Separately),
				new Plan.Entry("Assets/B.prefab", "Scene_Shared", Plan.Packing.Separately),
			};
			Assert.That(Plan.Compute(entries, false, Plan.ExclusionsFor(false), sources).Shared, Is.Empty);
		}

		[Test]
		public void SharedDependencyLabelsDependOnTheSetOfBundles_NotItsOrder()
		{
			string ab = SharedDependencyBuildGroup.LabelFor(new[] { "x|a", "x|b" });
			Assert.That(SharedDependencyBuildGroup.LabelFor(new[] { "x|b", "x|a" }), Is.EqualTo(ab));
			Assert.That(SharedDependencyBuildGroup.LabelFor(new[] { "x|a", "x|c" }), Is.Not.EqualTo(ab));
			Assert.That(ab, Does.StartWith(SharedDependencyBuildGroup.LabelPrefix));
			Assert.That(ab.IndexOf("Client", StringComparison.OrdinalIgnoreCase) < 0 &&
				ab.IndexOf("Server", StringComparison.OrdinalIgnoreCase) < 0, Is.True);
		}
	}
}
