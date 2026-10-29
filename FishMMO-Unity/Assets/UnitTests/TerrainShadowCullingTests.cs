using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Client;

namespace FishMMO.UnitTests
{
	public class TerrainShadowCullingTests
	{
		[TestCase(5, 0, 0, 1)]
		[TestCase(-5, 0, 0, 2)]
		[TestCase(0, 5, 0, 4)]
		[TestCase(0, -5, 0, 8)]
		[TestCase(0, 0, 5, 16)]
		[TestCase(0, 0, -5, 32)]
		[TestCase(5, 0, 5, 17)]
		[TestCase(20, 0, 0, 0)]
		public void PointLightRejectsOtherFacesAndCastersBeyondItsRange(float x, float y, float z, int expected)
		{
			var allPlanes = new Plane[36];
			var allSplits = new CullingSplit[6];
			Vector3[] directions = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
			for (int face = 0; face < 6; face++)
			{
				Vector3 direction = directions[face];
				Quaternion rotation = Quaternion.LookRotation(direction, Mathf.Abs(direction.y) > 0.9f ? Vector3.forward : Vector3.up);
				Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(Vector3.zero, rotation, Vector3.one).inverse;
				Plane[] facePlanes = GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Perspective(90, 1, 0.1f, 11) * view);
				for (int p = 0; p < 6; p++) allPlanes[face * 6 + p] = facePlanes[p];
				allSplits[face] = new CullingSplit { cullingPlaneOffset = face * 6, cullingPlaneCount = 6 };
			}
			using var planes = new NativeArray<Plane>(allPlanes, Allocator.Temp);
			using var splits = new NativeArray<CullingSplit>(allSplits, Allocator.Temp);
			Assert.That(TerrainShadowCullingMath.SplitMask(new Vector4(x, y, z, 0.05f), planes, splits), Is.EqualTo(expected));
		}

		[Test]
		public void SphereAtAPlaneIsKept_AndScaledBoundsAreConservative()
		{
			using var planes = new NativeArray<Plane>(new[] { new Plane(Vector3.right, 0) }, Allocator.Temp);
			using var splits = new NativeArray<CullingSplit>(new[] { new CullingSplit { cullingPlaneOffset = 0, cullingPlaneCount = 1 } }, Allocator.Temp);
			Assert.That(TerrainShadowCullingMath.SplitMask(new Vector4(-1, 0, 0, 1), planes, splits), Is.EqualTo(1));
			Assert.That(TerrainShadowCullingMath.SplitMask(new Vector4(-1.1f, 0, 0, 1), planes, splits), Is.Zero);
			var matrix = Matrix4x4.TRS(new Vector3(4, 5, 6), Quaternion.Euler(20, 45, 15), new Vector3(-2, 3, 4));
			Vector3 localCentre = new Vector3(1, 2, 3);
			Vector4 sphere = TerrainShadowCullingMath.WorldSphere(FishInstance.From(matrix), localCentre, 2);
			Assert.That(Vector3.Distance(new Vector3(sphere.x, sphere.y, sphere.z), matrix.MultiplyPoint3x4(localCentre)), Is.LessThan(0.001f));
			Assert.That(sphere.w, Is.EqualTo(8).Within(0.001f));
		}

		[Test]
		public void RangeBoundsNeverRejectASplitContainingOneOfTheirSpheres()
		{
			using var planes = new NativeArray<Plane>(new[] { new Plane(Vector3.right, 0), new Plane(Vector3.left, -5) }, Allocator.Temp);
			using var splits = new NativeArray<CullingSplit>(new[] { new CullingSplit { cullingPlaneOffset = 0, cullingPlaneCount = 1 }, new CullingSplit { cullingPlaneOffset = 1, cullingPlaneCount = 1 } }, Allocator.Temp);
			var bounds = new Bounds(Vector3.zero, Vector3.zero);
			ushort exact = 0;
			for (int i = -10; i < 10; i++)
			{
				var sphere = new Vector4(i, i * 0.2f, 0, 0.25f);
				bounds.Encapsulate(new Bounds(new Vector3(sphere.x, sphere.y, sphere.z), Vector3.one * .5f));
				exact |= TerrainShadowCullingMath.SplitMask(sphere, planes, splits);
			}
			Assert.That(TerrainShadowCullingMath.BoundsMask(bounds, planes, splits) & exact, Is.EqualTo(exact));
		}

		[Test]
		public void CachedMasksAreInvalidatedByLightPlanesSplitLayoutAndInstanceWorkChanges()
		{
			var planes = new NativeArray<Plane>(new[] { new Plane(Vector3.right, 0) }, Allocator.Temp);
			var splits = new NativeArray<CullingSplit>(new[] { new CullingSplit { cullingPlaneOffset = 0, cullingPlaneCount = 1 } }, Allocator.Temp);
			try
			{
				var cache = new TerrainShadowSplitCache();
				ushort[] masks = { 1, 0 };
				cache.Capture(5, 7, planes, splits, masks, 2);
				Assert.That(cache.Matches(5, 7, planes, splits, 2), Is.True);
				Assert.That(cache.Matches(6, 7, planes, splits, 2), Is.False, "uploaded positions changed");
				Assert.That(cache.Matches(5, 8, planes, splits, 2), Is.False, "camera candidate ranges changed");
				Assert.That(cache.Matches(5, 7, planes, splits, 3), Is.False, "new model");
				planes[0] = new Plane(Vector3.right, 1);
				Assert.That(cache.Matches(5, 7, planes, splits, 2), Is.False, "light moved or changed range");
				planes[0] = new Plane(Vector3.right, 0);
				splits[0] = new CullingSplit { cullingPlaneOffset = 0, cullingPlaneCount = 0 };
				Assert.That(cache.Matches(5, 7, planes, splits, 2), Is.False, "split layout changed");
				masks[0] = 0;
				var restored = new ushort[2]; cache.CopyMasksTo(restored);
				Assert.That(restored[0], Is.EqualTo(1), "the cache owns its masks");
			}
			finally { planes.Dispose(); splits.Dispose(); }
		}

		[Test]
		public void MissingSplitPlanesKeepTheCaster()
		{
			using var planes = new NativeArray<Plane>(0, Allocator.Temp);
			using var splits = new NativeArray<CullingSplit>(new[] { new CullingSplit { cullingPlaneOffset = 10, cullingPlaneCount = 6 } }, Allocator.Temp);
			Assert.That(TerrainShadowCullingMath.SplitMask(new Vector4(100, 200, 300, 1), planes, splits), Is.EqualTo(1));
		}
	}
}
