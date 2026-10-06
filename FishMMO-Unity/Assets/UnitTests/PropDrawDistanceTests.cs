using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Baked props are drawn as far as their size earns (Jim, 2026-10-06: culled by draw distance, larger objects
	/// further, for realism), never to the camera's 20 km far plane.
	/// </summary>
	[TestFixture]
	public class PropDrawDistanceTests
	{
		[Test]
		public void LargerPropsAreDrawnFurther_WithinTheBounds()
		{
			float bias = Mathf.Sqrt(Mathf.Max(0.1f, QualitySettings.lodBias));
			float boulder = CliffRockInstancing.DrawDistanceFor(3f);
			float tree = CliffRockInstancing.DrawDistanceFor(10f);
			float crag = CliffRockInstancing.DrawDistanceFor(30f);
			Assert.That(boulder, Is.LessThan(tree));
			Assert.That(tree, Is.LessThan(crag));
			Assert.That(CliffRockInstancing.DrawDistanceFor(0.05f), Is.EqualTo(CliffRockInstancing.MinDrawDistance * bias).Within(1e-3f), "never nearer than the floor");
			Assert.That(CliffRockInstancing.DrawDistanceFor(500f), Is.EqualTo(CliffRockInstancing.MaxDrawDistance * bias).Within(1e-3f), "never past the ceiling");
			Assert.That(crag, Is.LessThan(5000f), "nothing is drawn to the far plane any more");
		}
	}
}
