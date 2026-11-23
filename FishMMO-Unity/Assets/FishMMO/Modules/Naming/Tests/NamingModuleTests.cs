using System.Linq;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.NameGeneration;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Naming module: the generator names the scene objects through the
	/// core's <see cref="SceneObjectNames"/>, and its race registry follows the races the core loads.
	/// </summary>
	[TestFixture]
	public class NamingModuleTests
	{
		[Test]
		public void TheGeneratorIsTheScenesNameSource()
		{
			NamingInstaller.Install();
			LogAssert.IsTrue(SceneObjectNames.Source is SceneObjectNameSource, "the module installs its source");
			int seed = SceneObjectNames.Source.DeriveSeed(new SceneObjectNamingSettings { RegionSeed = "Ashford", ObjectSeed = "banker" }, "Banker");
			LogAssert.IsTrue(seed != 0, "a seeded name has a seed (0 means keep the authored name)");
			LogAssert.AreEqual(seed, SceneObjectNames.Source.DeriveSeed(new SceneObjectNamingSettings { RegionSeed = "Ashford", ObjectSeed = "banker" }, "Banker"), "the same every time");
		}

		[Test]
		public void TheNameModifierPickerFindsTheModulesTemplate()
		{
			var field = typeof(SceneObjectNamingSettings).GetField(nameof(SceneObjectNamingSettings.ModifierID));
			var attribute = (TemplateReferenceAttribute)field.GetCustomAttributes(typeof(TemplateReferenceAttribute), false).Single();
			LogAssert.AreEqual(typeof(NameModifierTemplate), attribute.TemplateType, "the core field names the module's template by name");
		}
	}
}
