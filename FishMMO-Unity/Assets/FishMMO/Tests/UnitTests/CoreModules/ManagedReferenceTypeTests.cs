using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every [SerializeReference] value in a template or prefab resolves to a type that exists.
	/// </summary>
	/// <remarks>
	/// ECA actions and conditions are stored by class, namespace and ASSEMBLY. Moving one into a module's assembly without
	/// <c>[MovedFrom(true, sourceNamespace: "FishMMO.Shared", sourceAssembly: "FishMMO.Shared")]</c> leaves every asset that
	/// used it holding a reference Unity cannot resolve: the trigger silently loses that step (second-wave moves).
	/// With every module installed nothing may be unresolved; a module's own assets are allowed to go unresolved only in a
	/// build without that module.
	/// </remarks>
	[TestFixture]
	public class ManagedReferenceTypeTests
	{
		[Test]
		public void EveryTemplateAndPrefab_ResolvesItsSerializedReferences()
		{
			var paths = new List<string>();
			foreach (string root in new[] { "Assets/FishMMO", "Assets/Prefabs" })
			{
				if (!Directory.Exists(root))
				{
					continue;
				}
				paths.AddRange(Directory.GetFiles(root, "*.asset", SearchOption.AllDirectories));
				paths.AddRange(Directory.GetFiles(root, "*.prefab", SearchOption.AllDirectories));
			}

			var broken = new List<string>();
			int checkedCount = 0;
			foreach (string path in paths.Select(p => p.Replace('\\', '/')))
			{
				if (!File.ReadAllText(path).Contains("type: {class:"))
				{
					continue;
				}
				++checkedCount;
				foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(path))
				{
					if (obj != null && SerializationUtility.HasManagedReferencesWithMissingTypes(obj))
					{
						string missing = string.Join(", ", SerializationUtility.GetManagedReferencesWithMissingTypes(obj)
							.Select(r => $"{r.namespaceName}.{r.className} ({r.assemblyName})").Distinct());
						broken.Add($"{path} [{obj.name}]: {missing}");
					}
				}
			}

			LogAssert.IsTrue(checkedCount > 0, "no asset with serialized references was found");
			LogAssert.AreEqual(0, broken.Count, string.Join("\n", broken.Take(40)));
		}
	}
}
