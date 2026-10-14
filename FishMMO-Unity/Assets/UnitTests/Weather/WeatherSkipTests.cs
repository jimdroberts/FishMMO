using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// Work left out because it would draw nothing must be left out only then. The light shafts' gather is skipped on the CPU
	/// when it could find nothing in the frame (<see cref="FishCloudsFeature.GodRaysReachFrame"/>): held here to the shader's
	/// own per-pixel arithmetic over many lights and pixels. And the rain draws only the prefix of its threshold-ordered
	/// field that can show (<see cref="PrecipitationField.PrefixFor"/>): every particle under the share must be in it.
	/// </summary>
	public class WeatherSkipTests
	{
		/// <summary>A camera's projection as Unity builds it (OpenGL convention), made here.</summary>
		private static Matrix4x4 Perspective(float fovDegrees, float aspect, float near, float far)
		{
			float f = 1f / Mathf.Tan(fovDegrees * 0.5f * Mathf.Deg2Rad);
			var m = new Matrix4x4();
			m.m00 = f / aspect;
			m.m11 = f;
			m.m22 = (far + near) / (near - far);
			m.m23 = 2f * far * near / (near - far);
			m.m32 = -1f;
			return m;
		}

		/// <summary>The view of a camera at <paramref name="eye"/> turned <paramref name="yawDegrees"/> about y and pitched up by <paramref name="pitchDegrees"/>: Unity's, which looks down −z.</summary>
		private static Matrix4x4 View(Vector3 eye, float yawDegrees, float pitchDegrees)
		{
			float yaw = yawDegrees * Mathf.Deg2Rad, pitch = pitchDegrees * Mathf.Deg2Rad;
			var forward = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
			Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.up, forward));
			Vector3 up = Vector3.Cross(forward, right);
			var m = Matrix4x4.identity;
			m.SetRow(0, new Vector4(right.x, right.y, right.z, -Vector3.Dot(right, eye)));
			m.SetRow(1, new Vector4(up.x, up.y, up.z, -Vector3.Dot(up, eye)));
			m.SetRow(2, new Vector4(-forward.x, -forward.y, -forward.z, Vector3.Dot(forward, eye)));
			m.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
			return m;
		}

		/// <summary>FishClouds pass 4's early outs at one pixel, as the shader has them: true when it returns 0 there.</summary>
		private static bool ShaderGathersNothing(Matrix4x4 viewProjection, Vector3 eye, Vector3 toLight, float reach, float aspect, Vector2 uv)
		{
			Vector4 clip = viewProjection * new Vector4(eye.x + toLight.x * 1e6f, eye.y + toLight.y * 1e6f, eye.z + toLight.z * 1e6f, 1f);
			if (clip.w <= 1e-5f)
			{
				return true;
			}
			var light = new Vector2(clip.x / clip.w * 0.5f + 0.5f, clip.y / clip.w * 0.5f + 0.5f);
			var outside = new Vector2(Mathf.Max(0f, Mathf.Max(-light.x, light.x - 1f)) * aspect, Mathf.Max(0f, Mathf.Max(-light.y, light.y - 1f)));
			float t = Mathf.Clamp01(outside.magnitude / 0.6f);
			float framed = 1f - t * t * (3f - 2f * t);
			if (framed <= 0f)
			{
				return true;
			}
			Vector2 toward = light - uv;
			float span = new Vector2(toward.x * aspect, toward.y).magnitude;
			return span / Mathf.Max(1e-4f, reach) >= 1f;
		}

		[Test]
		public void TheShaftsAreLeftOut_OnlyWhereTheGatherFindsNothingAtAnyPixel()
		{
			const float aspect = 16f / 9f;
			Matrix4x4 projection = Perspective(60f, aspect, 0.3f, 40000f);
			var random = new System.Random(4242);
			int skipped = 0, drawn = 0;
			foreach (float reach in new[] { 0.9f, 0.35f })
			{
				for (int d = 0; d < 1500; d++)
				{
					var eye = new Vector3((float)random.NextDouble() * 4000f - 2000f, (float)random.NextDouble() * 600f, (float)random.NextDouble() * 4000f - 2000f);
					Matrix4x4 viewProjection = projection * View(eye, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 120f - 60f);
					var toLight = Vector3.Normalize(new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f,
						(float)random.NextDouble() * 2f - 1f));
					if (FishCloudsFeature.GodRaysReachFrame(viewProjection, eye, toLight, reach, aspect))
					{
						drawn++;
						continue;
					}
					skipped++;
					for (int y = 0; y < 18; y++)
					{
						for (int x = 0; x < 32; x++)
						{
							var uv = new Vector2((x + 0.5f) / 32f, (y + 0.5f) / 18f);
							if (!ShaderGathersNothing(viewProjection, eye, toLight, reach, aspect, uv))
							{
								Assert.Fail($"left out, yet the gather finds the light at pixel {uv} (reach {reach}, light {toLight})");
							}
						}
					}
				}
			}
			// A control: both ways must have happened, or nothing was tested.
			LogAssert.IsTrue(skipped > 100 && drawn > 100, $"{skipped} left out, {drawn} drawn");
		}

		[Test]
		public void TheShafts_AreDrawnWithTheSunInTheFrame_AndNotWithItBehind()
		{
			const float aspect = 16f / 9f;
			Matrix4x4 viewProjection = Perspective(60f, aspect, 0.3f, 40000f) * View(Vector3.zero, 0f, 0f);
			LogAssert.IsTrue(FishCloudsFeature.GodRaysReachFrame(viewProjection, Vector3.zero, Vector3.Normalize(new Vector3(0.1f, 0.2f, 1f)), 0.9f, aspect),
				"the sun ahead");
			LogAssert.IsFalse(FishCloudsFeature.GodRaysReachFrame(viewProjection, Vector3.zero, Vector3.Normalize(new Vector3(0f, 0.3f, -1f)), 0.9f, aspect),
				"the sun behind");
			// Just past the frame's side (half the field across is about 46°): its shafts still reach in.
			LogAssert.IsTrue(FishCloudsFeature.GodRaysReachFrame(viewProjection, Vector3.zero,
				new Vector3(Mathf.Sin(50f * Mathf.Deg2Rad), 0f, Mathf.Cos(50f * Mathf.Deg2Rad)), 0.9f, aspect), "just outside the frame");
		}

		[Test]
		public void TheRainsPrefix_HoldsEveryParticleTheShareCanShow()
		{
			const int n = 1000;
			var thresholds = new float[n];
			for (int i = 0; i < n; i++)
			{
				thresholds[i] = (i + 0.5f) / n;
			}
			LogAssert.AreEqual(-1, PrecipitationField.PrefixFor(thresholds, 0f), "nothing shown, nothing drawn");
			LogAssert.AreEqual(-1, PrecipitationField.PrefixFor(thresholds, thresholds[0]), "a share at the lowest threshold shows none");
			LogAssert.AreEqual(PrecipitationField.Prefixes - 1, PrecipitationField.PrefixFor(thresholds, 1f), "all of it");
			int last = -1;
			for (int s = 0; s <= 2000; s++)
			{
				float shown = s / 2000f;
				int prefix = PrecipitationField.PrefixFor(thresholds, shown);
				int under = 0;
				while (under < n && thresholds[under] < shown)
				{
					under++;
				}
				int quads = prefix < 0 ? 0 : PrecipitationField.PrefixQuads(prefix, n);
				if (quads < under)
				{
					Assert.Fail($"share {shown}: {under} particles can show, the prefix holds {quads}");
				}
				if (prefix < last)
				{
					Assert.Fail($"share {shown}: a larger share drew a smaller prefix");
				}
				last = prefix;
			}
			LogAssert.AreEqual(n, PrecipitationField.PrefixQuads(PrecipitationField.Prefixes - 1, n), "the last prefix is the whole field");
		}

		[Test]
		public void TheOrderedField_IsTheSameQuads_InThresholdOrder()
		{
			Mesh plain = PrecipitationField.BuildMesh(500, 1234);
			Mesh ordered = PrecipitationField.BuildMesh(500, 1234, out float[] thresholds, true);
			try
			{
				var a = new List<Vector3>();
				var b = new List<Vector3>();
				plain.GetVertices(a);
				ordered.GetVertices(b);
				var ra = new List<Vector4>();
				var rb = new List<Vector4>();
				plain.GetUVs(1, ra);
				ordered.GetUVs(1, rb);
				var quads = new Dictionary<float, (Vector3, Vector4)>();
				for (int q = 0; q < 500; q++)
				{
					quads[ra[q * 4].x] = (a[q * 4], ra[q * 4]);
				}
				for (int q = 0; q < 500; q++)
				{
					LogAssert.AreEqual(thresholds[q], rb[q * 4].x);
					LogAssert.IsTrue(q == 0 || rb[q * 4].x > rb[(q - 1) * 4].x, "lowest threshold first");
					(Vector3 position, Vector4 randoms) = quads[rb[q * 4].x];
					LogAssert.AreEqual(position, b[q * 4], "each quad keeps its own position");
					LogAssert.AreEqual(randoms, rb[q * 4], "and its own randoms");
				}
				LogAssert.AreEqual(PrecipitationField.Prefixes, ordered.subMeshCount);
				LogAssert.AreEqual((uint)(500 * 6), ordered.GetIndexCount(PrecipitationField.Prefixes - 1));
				LogAssert.AreEqual(1, plain.subMeshCount, "the other fields built from it keep their one sub-mesh");
				LogAssert.AreEqual(IndexFormat.UInt16, ordered.indexFormat);
			}
			finally
			{
				Object.DestroyImmediate(plain);
				Object.DestroyImmediate(ordered);
			}
		}
	}
}
