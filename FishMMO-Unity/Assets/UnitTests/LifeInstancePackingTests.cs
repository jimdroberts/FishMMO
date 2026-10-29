using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The ambient and sea life instance buffers (2026-10-27): every drawn animal packed into one buffer per camera and drawn by
	/// one procedural draw per kind, with no property block. Pins the packing against the matrix product it replaced, the
	/// record's layout against the shaders' structs, and that no instancing path is left behind.
	/// </summary>
	[TestFixture]
	public class LifeInstancePackingTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders";

		/// <summary>A T·R·S by hand (a turn about y, a uniform scale), so nothing here asks the engine.</summary>
		private static Matrix4x4 Placed(Vector3 at, float yawRadians, float scale)
		{
			float c = Mathf.Cos(yawRadians) * scale, s = Mathf.Sin(yawRadians) * scale;
			var m = new Matrix4x4();
			m.m00 = c; m.m01 = 0f; m.m02 = s; m.m03 = at.x;
			m.m10 = 0f; m.m11 = scale; m.m12 = 0f; m.m13 = at.y;
			m.m20 = -s; m.m21 = 0f; m.m22 = c; m.m23 = at.z;
			m.m30 = 0f; m.m31 = 0f; m.m32 = 0f; m.m33 = 1f;
			return m;
		}

		[Test]
		public void Pack_GrowsTheMatrixExactlyAsTheScaleProductDid()
		{
			Matrix4x4 m = Placed(new Vector3(1234.5f, 87.25f, -9876.125f), 0.7f, 0.31f);
			const float grow = 1.7f;
			Matrix4x4 product = m * Matrix4x4.Scale(Vector3.one * grow);
			LifeInstanceRecord r = LifeInstancePacking.Pack(m, grow, Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero);
			Assert.That(r.Row0, Is.EqualTo(product.GetRow(0)), "row 0");
			Assert.That(r.Row1, Is.EqualTo(product.GetRow(1)), "row 1");
			Assert.That(r.Row2, Is.EqualTo(product.GetRow(2)), "row 2");
			// Bit for bit, not merely close: the old path drew the product.
			for (int i = 0; i < 4; i++)
			{
				Assert.That(r.Row0[i], Is.EqualTo(product.GetRow(0)[i]).Within(0f));
				Assert.That(r.Row1[i], Is.EqualTo(product.GetRow(1)[i]).Within(0f));
				Assert.That(r.Row2[i], Is.EqualTo(product.GetRow(2)[i]).Within(0f));
			}
			Assert.That(LifeInstancePacking.Origin(r), Is.EqualTo(new Vector3(m.m03, m.m13, m.m23)), "the growth keeps the animal where it is");
		}

		[Test]
		public void Pack_AtGrowOne_IsTheMatrix_AndKeepsTheVectorsInOrder()
		{
			Matrix4x4 m = Placed(new Vector3(-3f, 4f, 5f), -2.1f, 2.5f);
			var a = new Vector4(0.25f, 1f, 0f, 0.3f);
			var b = new Vector4(0.9f, 0.8f, 0.7f, 0.5f);
			var c = new Vector4(16777215f, 65793f, 0f, 3.4f);
			var d = new Vector4(0.6f, 0.2f, 0f, 0f);
			LifeInstanceRecord r = LifeInstancePacking.Pack(m, 1f, a, b, c, d);
			Assert.That(r.Row0, Is.EqualTo(m.GetRow(0)));
			Assert.That(r.Row1, Is.EqualTo(m.GetRow(1)));
			Assert.That(r.Row2, Is.EqualTo(m.GetRow(2)));
			Assert.That(r.A, Is.EqualTo(a), "A is _Anim");
			Assert.That(r.B, Is.EqualTo(b), "B is _Tint");
			Assert.That(r.C, Is.EqualTo(c), "C is _Look (ambient) or _Surface (sea)");
			Assert.That(r.D, Is.EqualTo(d), "D is _Glow (ambient)");
			// A packed look is a whole number up to 2^24: it must arrive unchanged.
			Assert.That(r.C.x, Is.EqualTo(16777215f).Within(0f));
		}

		[Test]
		public void Record_IsSevenFloat4s_TheBuffersStride()
		{
			Assert.That(Marshal.SizeOf<LifeInstanceRecord>(), Is.EqualTo(112));
			Assert.That(LifeInstanceRecord.Stride, Is.EqualTo(Marshal.SizeOf<LifeInstanceRecord>()));
		}

		[Test]
		public void Bounds_HoldEveryAnimal_AtItsScale()
		{
			Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
			Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
			float reach = LifeInstancePacking.Reach(new Vector3(0f, 0.1f, 0f), new Vector3(0.5f, 0.2f, 0.5f), 1.5f);
			Assert.That(reach, Is.GreaterThan(0.5f * 1.5f), "the mesh's furthest corner, widened");
			LifeInstanceRecord near = LifeInstancePacking.Pack(Placed(new Vector3(10f, 2f, -4f), 0.3f, 0.2f), 1f, default, default, default, default);
			LifeInstanceRecord far = LifeInstancePacking.Pack(Placed(new Vector3(-50f, 30f, 80f), 1.9f, 4f), 1f, default, default, default, default);
			Assert.That(LifeInstancePacking.Scale(near), Is.EqualTo(0.2f).Within(1e-6f));
			Assert.That(LifeInstancePacking.Scale(far), Is.EqualTo(4f).Within(1e-5f));
			LifeInstancePacking.Encapsulate(near, reach, ref min, ref max);
			LifeInstancePacking.Encapsulate(far, reach, ref min, ref max);
			Assert.That(min.x, Is.EqualTo(-50f - reach * 4f).Within(1e-4f));
			Assert.That(max.x, Is.EqualTo(10f + reach * 0.2f).Within(1e-4f));
			Assert.That(min.y, Is.EqualTo(2f - reach * 0.2f).Within(1e-4f));
			Assert.That(max.y, Is.EqualTo(30f + reach * 4f).Within(1e-4f));
			Assert.That(min.z, Is.EqualTo(-4f - reach * 0.2f).Within(1e-4f));
			Assert.That(max.z, Is.EqualTo(80f + reach * 4f).Within(1e-4f));
		}

		[Test]
		public void Capacity_DoublesToFit_AndNeverShrinks()
		{
			Assert.That(LifeInstancePacking.Grown(256, 100, 256), Is.EqualTo(256), "room already");
			Assert.That(LifeInstancePacking.Grown(256, 257, 256), Is.EqualTo(512));
			Assert.That(LifeInstancePacking.Grown(256, 3000, 256), Is.EqualTo(4096));
			Assert.That(LifeInstancePacking.Grown(0, 1, 256), Is.EqualTo(256), "the first buffer is the minimum");
			Assert.That(LifeInstancePacking.Grown(0, 5, 0), Is.EqualTo(8), "a zero minimum still grows");
			Assert.That(LifeInstancePacking.Grown(4096, 10, 256), Is.EqualTo(4096), "never shrinks");
		}

		[Test]
		public void Shaders_ReadTheRecord_InItsOrder_WithNoInstancingLeft()
		{
			string ambient = Read(Shaders + "/FishAmbientLife.hlsl");
			string sea = Read(Shaders + "/FishSeaLife.hlsl");
			AssertFields(ambient, "AmbientLifeInstance", "row0", "row1", "row2", "anim", "tint", "look", "glow");
			AssertFields(sea, "SeaLifeInstance", "row0", "row1", "row2", "anim", "tint", "surface", "spare");
			foreach (string source in new[] { ambient, sea, Read(Shaders + "/FishAmbientLife.shader"), Read(Shaders + "/FishSeaLife.shader") })
			{
				StringAssert.DoesNotContain("UNITY_INSTANCING_BUFFER", source);
				StringAssert.DoesNotContain("UNITY_ACCESS_INSTANCED_PROP", source);
				StringAssert.DoesNotContain("multi_compile_instancing", source);
				// DXC-safe: the instance's transform is done here, never by writing the per-draw matrices (FishIndirectInstancing).
				StringAssert.DoesNotContain("FishIndirectInstancing", source);
			}
			foreach (string hlsl in new[] { ambient, sea })
			{
				StringAssert.Contains("StructuredBuffer<", hlsl);
				StringAssert.Contains("_LifeInstanceBase", hlsl);
				StringAssert.Contains("SV_InstanceID", hlsl);
				StringAssert.DoesNotContain("TransformObjectToWorld(", hlsl);
				StringAssert.DoesNotContain("UNITY_MATRIX_M", hlsl);
				// The base is a material value: in UnityPerMaterial, before its end.
				int cbuffer = hlsl.IndexOf("CBUFFER_START(UnityPerMaterial)", System.StringComparison.Ordinal);
				int end = hlsl.IndexOf("CBUFFER_END", cbuffer, System.StringComparison.Ordinal);
				int at = hlsl.IndexOf("float _LifeInstanceBase;", cbuffer, System.StringComparison.Ordinal);
				Assert.That(cbuffer >= 0 && at > cbuffer && at < end, Is.True, "_LifeInstanceBase belongs in UnityPerMaterial");
			}
			foreach (string shader in new[] { Read(Shaders + "/FishAmbientLife.shader"), Read(Shaders + "/FishSeaLife.shader") })
			{
				Assert.That(Regex.Matches(shader, "HLSLPROGRAM").Count, Is.EqualTo(Regex.Matches(shader, @"#pragma use_dxc vulkan").Count), "every pass on DXC");
			}
		}

		[Test]
		public void Systems_DrawFromTheBuffer_WithNoPropertyBlock()
		{
			foreach (string path in new[] { "Assets/Scripts/Client/World/AmbientLife/AmbientLifeSystem.cs", "Assets/Scripts/Client/World/SeaLife/SeaLifeSystem.cs" })
			{
				string source = Read(path);
				StringAssert.DoesNotContain("MaterialPropertyBlock", source, path);
				StringAssert.DoesNotContain("Graphics.DrawMeshInstanced(", source, path);
				StringAssert.DoesNotContain("SetVectorArray(", source, path);
				StringAssert.Contains("Graphics.RenderMeshPrimitives(", source, path);
				StringAssert.Contains("instanceBuffers.Upload(", source, path);
				StringAssert.Contains("instanceBuffers.Release()", source, path);
			}
		}

		private static void AssertFields(string hlsl, string structName, params string[] fields)
		{
			Match m = Regex.Match(hlsl, @"struct\s+" + structName + @"\s*\{([^}]*)\}");
			Assert.That(m.Success, Is.True, structName + " not found");
			MatchCollection declared = Regex.Matches(m.Groups[1].Value, @"float4\s+(\w+)\s*;");
			Assert.That(declared.Count, Is.EqualTo(fields.Length), structName + ": seven float4s, as LifeInstanceRecord");
			for (int i = 0; i < fields.Length; i++)
			{
				Assert.That(declared[i].Groups[1].Value, Is.EqualTo(fields[i]), structName + " field " + i);
			}
		}

		private static string Read(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			Assert.That(File.Exists(path), Is.True, $"{relativePath} not found at {path}");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}
	}
}
