using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// One answer to "which solar system": <see cref="SolarSystemProfile.Resolve"/> takes the body's own
	/// system, else the atlas's, else the loaded one, else the first by path — so the globe bake, the
	/// scene generator and the map capture cannot each pick a different sun.
	/// </summary>
	[TestFixture]
	public class SolarSystemResolveTests
	{
		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (Object o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			created.Clear();
		}

		private T Make<T>() where T : ScriptableObject
		{
			T o = ScriptableObject.CreateInstance<T>();
			created.Add(o);
			return o;
		}

		[Test]
		public void ASystem_ContainsItsBodiesAndItsHomeWorld_AndNothingElse()
		{
			SolarSystemProfile system = Make<SolarSystemProfile>();
			WorldBody home = Make<WorldBody>();
			WorldBody moon = Make<WorldBody>();
			WorldBody stranger = Make<WorldBody>();
			system.HomeWorld = home;
			system.Bodies.Add(moon);

			Assert.IsTrue(system.Contains(home), "the home world, even when not listed among the bodies");
			Assert.IsTrue(system.Contains(moon));
			Assert.IsFalse(system.Contains(stranger));
			Assert.IsFalse(system.Contains(null));
		}

		[Test]
		public void InEditMode_ABodysOwnSystemAsset_IsTheAnswer()
		{
			// Every body asset in the project resolves to the system that lists it, whatever the atlas says.
			int checkedBodies = 0;
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SolarSystemProfile)))
			{
				var system = AssetDatabase.LoadAssetAtPath<SolarSystemProfile>(AssetDatabase.GUIDToAssetPath(guid));
				if (system == null)
				{
					continue;
				}
				foreach (CelestialBody body in system.Bodies)
				{
					if (body == null)
					{
						continue;
					}
					SolarSystemProfile resolved = SolarSystemProfile.Resolve(body);
					Assert.IsNotNull(resolved, body.name);
					Assert.IsTrue(resolved.Contains(body), $"{body.name} resolved to {resolved.name}, which does not list it");
					checkedBodies++;
				}
			}
			Assume.That(checkedBodies, Is.GreaterThan(0), "the project has no solar system with bodies to check");
		}

		[Test]
		public void InEditMode_WithNoBody_TheAtlasNamesTheSystem()
		{
			Assume.That(SolarSystemProfile.Active, Is.Null, "a loaded profile answers first; this checks the edit-mode tiers");
			WorldAtlas atlas = null;
			var paths = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(WorldAtlas)))
			{
				paths.Add(AssetDatabase.GUIDToAssetPath(guid));
			}
			paths.Sort(global::System.StringComparer.Ordinal);
			foreach (string path in paths)
			{
				atlas = AssetDatabase.LoadAssetAtPath<WorldAtlas>(path);
				if (atlas != null && atlas.SolarSystem != null)
				{
					break;
				}
				atlas = null;
			}
			Assume.That(atlas, Is.Not.Null, "the project has no atlas naming a system");
			Assert.AreSame(atlas.SolarSystem, SolarSystemProfile.Resolve());
		}

		[Test]
		public void ABodyInNoSystem_FallsThroughToTheWorldsSystem()
		{
			WorldBody stranger = Make<WorldBody>();
			Assert.AreSame(SolarSystemProfile.Resolve(), SolarSystemProfile.Resolve(stranger));
		}
	}
}
