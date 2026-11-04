using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;

namespace FishMMO.UnitTests
{
	public class DirectionalShadowDrawsTests
	{
		[Test]
		public void LocalLightsRejectTheShadowOnlyLayerWhileStillIlluminatingTheOriginalLayer()
		{
			var cameraObject = new GameObject("Shadow route camera");
			var sunObject = new GameObject("Shadow route sun");
			var pointObject = new GameObject("Shadow route point");
			bool enabled = DirectionalShadowDraws.Enabled;
			try
			{
				DirectionalShadowDraws.Enabled = true;
				var camera = cameraObject.AddComponent<Camera>(); camera.cullingMask = -1;
				var sun = sunObject.AddComponent<Light>(); sun.type = LightType.Directional; sun.cullingMask = -1;
				var point = pointObject.AddComponent<Light>(); point.type = LightType.Point; point.cullingMask = -1;
				int layer = DirectionalShadowDraws.Layer;
				Assert.That(layer, Is.GreaterThanOrEqualTo(0));
				Assert.That(DirectionalShadowDraws.LayerFor(camera, sun, 0), Is.EqualTo(layer));
				DirectionalShadowDraws.ConfigurePunctualLight(point);
				Assert.That(point.cullingMask & (1 << layer), Is.Zero);
				Assert.That(point.cullingMask & 1, Is.EqualTo(1));
				Assert.That(sun.cullingMask & (1 << layer), Is.Not.Zero);
				DirectionalShadowDraws.ConfigurePunctualLight(sun);
				Assert.That(sun.cullingMask, Is.EqualTo(-1));
				point.type = LightType.Spot; point.cullingMask = -1;
				DirectionalShadowDraws.ConfigurePunctualLight(point);
				Assert.That(point.cullingMask & (1 << layer), Is.Zero);
			}
			finally
			{
				DirectionalShadowDraws.Enabled = enabled;
				Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(sunObject); Object.DestroyImmediate(pointObject);
			}
		}

		[Test]
		public void CustomizedCameraAndSunMasksKeepTheOriginalRoute()
		{
			var cameraObject = new GameObject("Masked shadow camera");
			var sunObject = new GameObject("Masked shadow sun");
			bool enabled = DirectionalShadowDraws.Enabled;
			try
			{
				DirectionalShadowDraws.Enabled = true;
				var camera = cameraObject.AddComponent<Camera>(); var sun = sunObject.AddComponent<Light>(); sun.type = LightType.Directional;
				int bit = 1 << DirectionalShadowDraws.Layer;
				camera.cullingMask = -1 & ~bit; sun.cullingMask = -1;
				Assert.That(DirectionalShadowDraws.LayerFor(camera, sun, 0), Is.Zero);
				camera.cullingMask = -1 & ~1;
				Assert.That(DirectionalShadowDraws.LayerFor(camera, sun, 0), Is.Zero);
				camera.cullingMask = -1; sun.cullingMask = -1 & ~bit;
				Assert.That(DirectionalShadowDraws.LayerFor(camera, sun, 0), Is.Zero);
				sun.cullingMask = -1 & ~1;
				Assert.That(DirectionalShadowDraws.LayerFor(camera, sun, 0), Is.Zero);
				Assert.That(DirectionalShadowDraws.LayerFor(camera, null, 0), Is.Zero);
				DirectionalShadowDraws.Enabled = false; sun.cullingMask = -1;
				Assert.That(DirectionalShadowDraws.LayerFor(camera, sun, 0), Is.Zero);
			}
			finally
			{
				DirectionalShadowDraws.Enabled = enabled;
				Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(sunObject);
			}
		}
	}
}
