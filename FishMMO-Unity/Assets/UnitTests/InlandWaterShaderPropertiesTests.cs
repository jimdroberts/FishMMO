using System.Collections.Generic;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	public class InlandWaterShaderPropertiesTests
	{
		[TestCase("FishMMO/Water/Inland Water")]
		[TestCase("FishMMO/Water/Waterfall")]
		[TestCase("FishMMO/Water/Waterfall Spray")]
		public void PerSurfaceFieldsAreDeclaredAsMaterialProperties(string name)
		{
			Shader shader = Shader.Find(name);
			Assert.That(shader, Is.Not.Null);
			var fields = new List<string> { name.EndsWith("Inland Water") ? "_InlandLookOn" : "_FallLookOn" };
			if (name.EndsWith("Inland Water"))
			{
				fields.AddRange(new[] { "_RiverFlowInfo", "_RiverFallCount", "_RiverFlowField" });
				for (int i = 0; i < 4; i++) fields.Add("_RiverFall" + i);
				for (int i = 0; i < 15; i++) fields.Add("_InlandLook" + i);
			}
			else fields.AddRange(name.EndsWith("Spray")
				? new[] { "_FallSpray", "_FallMist", "_FallPath", "_FallOrigin", "_FallMistNoiseCells", "_FallPathTex", "_FallMistNoise" }
				: new[] { "_FallWater", "_FallFoam", "_FallAbsorb" });
			foreach (string field in fields) Assert.That(shader.FindPropertyIndex(field), Is.GreaterThanOrEqualTo(0), field);
		}
	}
}
