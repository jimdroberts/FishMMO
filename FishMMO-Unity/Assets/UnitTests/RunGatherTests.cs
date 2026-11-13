using System.Collections.Generic;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// TerrainGpuRenderer.RunGather: a gathered run's candidacy and levels, decided from its own bounds
	/// (TerrainGpuMath.RunCandidates), must hold everything the cull kernel keeps of it while the work list is kept: the
	/// camera moved up to the regather slack, it and the sun turned up to the regather angle. Checked against the kernel's
	/// CPU mirror (TerrainGpuMath.Cull) over random runs, cameras and suns. A main-view instance only counts where its mesh
	/// box meets the moved camera's view (elsewhere it draws no pixel, whether submitted or not).
	/// </summary>
	public class RunGatherTests
	{
		private const float Slack = 1.5f, TurnDegrees = 3f, DrawDistance = 1000f, ShadowDistance = 150f, Fov = 60f, Aspect = 16f / 9f;
		/// <summary>QualitySettings.maximumLODLevel as shipped: 0, every level allowed (3 clamped every choice to the last level).</summary>
		private const int MaximumLodLevel = 0;

		private sealed class Model
		{
			public FishModelData Data;
			public Bounds LocalBounds;
			public float[] Transitions, Widths;
		}

		private static Model Make(Bounds local, Vector3 reference, float[] transitions, uint shadowMask)
		{
			var model = new Model
			{
				LocalBounds = local,
				Transitions = transitions,
				Widths = new float[transitions.Length],
				Data = new FishModelData
				{
					LocalReference = reference,
					Size = Mathf.Max(local.size.x, Mathf.Max(local.size.y, local.size.z)),
					SphereCentre = local.center,
					SphereRadius = local.extents.magnitude,
					LevelCount = (uint)transitions.Length,
					ShadowMask = shadowMask,
				},
			};
			for (int l = 0; l < transitions.Length; l++)
			{
				model.Widths[l] = TerrainTreeMath.FadeWidth(transitions, l, 0f);
				model.Data.Transitions[l] = transitions[l];
				model.Data.FadeWidths[l] = model.Widths[l];
			}
			return model;
		}

		private static Plane[] Frustum(Vector3 eye, Quaternion rotation)
		{
			Matrix4x4 worldToCamera = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(eye, rotation, Vector3.one).inverse;
			return GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Perspective(Fov, Aspect, 0.3f, 1500f) * worldToCamera);
		}

		private static Quaternion Turned(Quaternion q, System.Random random)
		{
			Vector3 axis = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
			return Quaternion.AngleAxis((float)random.NextDouble() * TurnDegrees * 0.999f, axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up) * q;
		}

		[Test]
		public void TheRunsCandidacyAndLevels_HoldEverythingTheKernelKeeps_WhileTheListIsKept()
		{
			var models = new[]
			{
				// A lone building: wide, low, its reference at its middle, every level casting.
				Make(new Bounds(new Vector3(0f, 4f, 0f), new Vector3(20f, 8f, 12f)), new Vector3(0f, 4f, 0f), new[] { 0.3f, 0.1f, 0.01f }, 0b111),
				// A tree: tall, its reference up the trunk, the billboard not casting.
				Make(new Bounds(new Vector3(0f, 9f, 0f), new Vector3(8f, 18f, 8f)), new Vector3(0f, 6f, 0f), new[] { 0.25f, 0.08f, 0.002f }, 0b011),
			};
			var random = new System.Random(20261104);
			var emits = new List<TerrainGpuMath.Emit>();
			float margin = Slack + DrawDistance * TurnDegrees * Mathf.Deg2Rad;
			int checkedMain = 0, checkedShadow = 0, runsRefused = 0, mainRefused = 0, shadowRefused = 0, mainTighter = 0, shadowTighter = 0;
			for (int trial = 0; trial < 4000; trial++)
			{
				Model model = models[trial % models.Length];
				var eye = new Vector3((float)random.NextDouble() * 200f - 100f, 20f + (float)random.NextDouble() * 200f, (float)random.NextDouble() * 200f - 100f);
				Quaternion rotation = Quaternion.Euler((float)random.NextDouble() * 60f - 10f, (float)random.NextDouble() * 360f, 0f);
				Plane[] planes = Frustum(eye, rotation);
				Plane[] rawPlanes = (Plane[])planes.Clone();
				for (int i = 0; i < 6; i++)
				{
					planes[i] = new Plane(planes[i].normal, planes[i].distance + margin);
				}
				Vector3 light = Quaternion.Euler(20f + (float)random.NextDouble() * 65f, (float)random.NextDouble() * 360f, 0f) * Vector3.forward;
				float factor = TerrainTreeMath.ScreenFactor(Fov, false, 0f, 1f);

				// The run: one to four instances round a spot up to 600 m off.
				var centre = eye + Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f) * Vector3.forward * ((float)random.NextDouble() * 600f);
				centre.y = (float)random.NextDouble() * 60f;
				int count = 1 + random.Next(4);
				var matrices = new Matrix4x4[count];
				Bounds bounds = default;
				float maxSize = 0f, minSize = float.MaxValue, radius = 0f;
				for (int k = 0; k < count; k++)
				{
					Vector3 at = centre + new Vector3((float)random.NextDouble() * 40f - 20f, 0f, (float)random.NextDouble() * 40f - 20f);
					float s = 0.7f + (float)random.NextDouble() * 0.7f;
					Matrix4x4 m = Matrix4x4.TRS(at, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), new Vector3(s, s * (0.9f + 0.2f * (float)random.NextDouble()), s));
					matrices[k] = m;
					float scale = Mathf.Max(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude);
					maxSize = Mathf.Max(maxSize, model.Data.Size * scale);
					minSize = Mathf.Min(minSize, model.Data.Size * scale);
					radius = Mathf.Max(radius, model.LocalBounds.extents.magnitude * Mathf.Max(scale, m.GetColumn(2).magnitude));
					// As CliffRockInstancing builds a run's bounds.
					Bounds own = TerrainTreeMath.TransformBounds(m, model.LocalBounds);
					own.Encapsulate(m.GetColumn(3));
					own.Encapsulate(m.MultiplyPoint3x4(model.Data.LocalReference));
					if (k == 0) bounds = own; else bounds.Encapsulate(own);
				}

				bool any = TerrainGpuMath.RunCandidates(planes, bounds, maxSize, radius, eye, Slack, DrawDistance, true, light, ShadowDistance, TurnDegrees,
					out float near, out float far, out bool main, out bool shadow);
				// The same run against the camera's own planes, each box widened by what the kept range can bring it (AngularMargins).
				TerrainGpuMath.RunCandidates(rawPlanes, bounds, maxSize, radius, eye, Slack, DrawDistance, true, light, ShadowDistance, TurnDegrees,
					out _, out _, out bool mainTurned, out bool shadowTurned, rawPlanes: true);
				mainTighter += main && !mainTurned ? 1 : 0;
				shadowTighter += shadow && !shadowTurned ? 1 : 0;
				int mask = any ? TerrainGpuMath.LevelMask(TerrainTreeMath.RelativeHeight(maxSize, near, factor, false),
					TerrainTreeMath.RelativeHeight(minSize, far, factor, false), model.Transitions, model.Widths, MaximumLodLevel) : 0;
				// Each instance's own levels (TerrainGpuRenderer.ExactRunLevels), from the same eye.
				int exact = 0;
				foreach (Matrix4x4 m in matrices)
				{
					exact |= TerrainGpuMath.InstanceLevelMask(m, model.Data.Size, model.Data.LocalReference, eye, Slack, factor, false, model.Transitions, model.Widths, MaximumLodLevel);
				}
				runsRefused += any ? 0 : 1;
				mainRefused += main ? 0 : 1;
				shadowRefused += shadow ? 0 : 1;

				// The kernel, from somewhere the list is still kept.
				for (int moves = 0; moves < 3; moves++)
				{
					Vector3 step = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
					Vector3 eye2 = eye + (step.sqrMagnitude > 1e-6f ? step.normalized : Vector3.up) * ((float)random.NextDouble() * Slack * 0.999f);
					Quaternion rotation2 = Turned(rotation, random);
					Vector3 light2 = Turned(Quaternion.LookRotation(light), random) * Vector3.forward;
					Plane[] planes2 = Frustum(eye2, rotation2);
					foreach (Matrix4x4 m in matrices)
					{
						TerrainGpuMath.Cull(m, in model.Data, DrawDistance, 1f, eye2, planes2, new Vector4(light2.x, light2.y, light2.z, ShadowDistance), new Vector3(factor, 0f, MaximumLodLevel), emits);
						foreach (TerrainGpuMath.Emit e in emits)
						{
							if (e.View == 0)
							{
								if (!TerrainTreeMath.IntersectsFrustum(planes2, TerrainTreeMath.TransformBounds(m, model.LocalBounds)))
								{
									continue;
								}
								checkedMain++;
								LogAssert.IsTrue(main && (mask & (1 << e.Level)) != 0, $"trial {trial}: a main-view L{e.Level} instance the run left out (main {main}, mask {mask})");
								LogAssert.IsTrue((exact & (1 << e.Level)) != 0, $"trial {trial}: a main-view L{e.Level} instance its own levels left out ({exact})");
								LogAssert.IsTrue(mainTurned, $"trial {trial}: a main-view instance the turned-margin test left out");
							}
							else
							{
								checkedShadow++;
								LogAssert.IsTrue(shadow && (mask & (1 << e.Level)) != 0, $"trial {trial}: a shadow L{e.Level} caster the run left out (shadow {shadow}, mask {mask})");
								LogAssert.IsTrue((exact & (1 << e.Level)) != 0, $"trial {trial}: a shadow L{e.Level} caster its own levels left out ({exact})");
								LogAssert.IsTrue(shadowTurned, $"trial {trial}: a shadow caster the turned-margin test left out");
							}
						}
					}
				}
			}
			// The check means something only if both views were exercised and the run decisions did refuse work.
			LogAssert.IsTrue(checkedMain > 1000 && checkedShadow > 300, $"exercised: {checkedMain} main, {checkedShadow} shadow emits");
			LogAssert.IsTrue(runsRefused > 100 && mainRefused > 500 && shadowRefused > 500, $"refused: {runsRefused} runs, {mainRefused} main, {shadowRefused} shadow");
			LogAssert.IsTrue(mainTighter > 100 && shadowTighter > 100, $"the turned margins refused {mainTighter} main and {shadowTighter} shadow candidacies the far plane's margin gave");
		}

		[Test]
		public void AnInstancesOwnLevels_HoldTheKernelsLevel_AndAreNarrowerThanTheRunsBoxWhereItStraddles()
		{
			// The level choice alone (no view): from the gather's eye, each instance's own levels (InstanceLevelMask) hold the level
			// the kernel picks for it from anywhere within the slack, and never more than the run's box gives.
			var random = new System.Random(511);
			Model model = Make(new Bounds(new Vector3(0f, 4f, 0f), new Vector3(20f, 8f, 12f)), new Vector3(0f, 4f, 0f), new[] { 0.3f, 0.1f, 0.01f }, 0b111);
			float factor = TerrainTreeMath.ScreenFactor(Fov, false, 0f, 1f);
			int tighter = 0, wider = 0, held = 0;
			for (int trial = 0; trial < 3000; trial++)
			{
				var eye = new Vector3(0f, 30f, 0f);
				float distance = (float)random.NextDouble() * 400f;
				Vector3 at = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f) * Vector3.forward * distance;
				float s = 0.7f + (float)random.NextDouble() * 0.7f;
				Matrix4x4 m = Matrix4x4.TRS(at, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), new Vector3(s, s, s));
				float size = model.Data.Size * Mathf.Max(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude);
				Bounds box = TerrainTreeMath.TransformBounds(m, model.LocalBounds);
				box.Encapsulate(m.GetColumn(3));
				box.Encapsulate(m.MultiplyPoint3x4(model.Data.LocalReference));
				// TerrainTreeMath.MinDistance / MaxDistance, in managed maths (Bounds.SqrDistance is native).
				Vector3 lo = box.min, hi = box.max;
				Vector3 nearest = new Vector3(Mathf.Clamp(eye.x, lo.x, hi.x), Mathf.Clamp(eye.y, lo.y, hi.y), Mathf.Clamp(eye.z, lo.z, hi.z));
				Vector3 farthest = new Vector3(eye.x < box.center.x ? hi.x : lo.x, eye.y < box.center.y ? hi.y : lo.y, eye.z < box.center.z ? hi.z : lo.z);
				float near = Mathf.Max(0f, Vector3.Distance(nearest, eye) - Slack), far = Vector3.Distance(farthest, eye) + Slack;
				int run = TerrainGpuMath.LevelMask(TerrainTreeMath.RelativeHeight(size, near, factor, false), TerrainTreeMath.RelativeHeight(size, far, factor, false), model.Transitions, model.Widths, MaximumLodLevel);
				int own = TerrainGpuMath.InstanceLevelMask(m, model.Data.Size, model.Data.LocalReference, eye, Slack, factor, false, model.Transitions, model.Widths, MaximumLodLevel);
				tighter += (run & ~own) != 0 ? 1 : 0;
				wider += (own & ~run) != 0 ? 1 : 0;
				for (int k = 0; k < 4; k++)
				{
					Vector3 step = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
					Vector3 eye2 = eye + (step.sqrMagnitude > 1e-6f ? step.normalized : Vector3.up) * ((float)random.NextDouble() * Slack * 0.999f);
					float height = TerrainTreeMath.RelativeHeight(size, Vector3.Distance(m.MultiplyPoint3x4(model.Data.LocalReference), eye2), factor, false);
					int level = TerrainTreeMath.SelectLodFaded(height, model.Transitions, model.Widths, MaximumLodLevel, out _, out int partner, out _);
					if (level >= 0)
					{
						held++;
						LogAssert.IsTrue((own & (1 << level)) != 0 && (partner < 0 || (own & (1 << partner)) != 0), $"trial {trial}: L{level} (partner {partner}) outside its own levels {own}");
					}
				}
			}
			System.Console.WriteLine($"own levels narrower than the run's box in {tighter} of 3000, wider in {wider}; {held} kernel choices held");
			LogAssert.IsTrue(wider == 0, $"an instance's own levels went past the run's box in {wider} trials");
			LogAssert.IsTrue(tighter > 100 && held > 5000, $"exercised: {tighter} narrower, {held} choices");
		}

		[Test]
		public void TheGatherers_DecideByRun_AndTheListIsRegatheredWhenTheSwitchFlips()
		{
			string props = ProjectFiles.ReadAllText("Assets/Scripts/Client/World/Terrain/CliffRockInstancing.cs");
			LogAssert.IsTrue(props.Contains("TerrainGpuRenderer.RunGather && !RunWork("), "the props' gather asks the run");
			LogAssert.IsTrue(props.Contains("TerrainGpuMath.RunCandidates(angular ? view.Planes : planes, own, model.Size * scale,") && props.Contains("mainLevels |= instanceMain ? levels : 0;")
				&& props.Contains("shadowLevels |= instanceShadow ? levels : 0;") && props.Contains("gatheredExactLevels != TerrainGpuRenderer.ExactRunLevels"),
				"a small run is decided instance by instance (a run of one each), and a kept list is not reused across that switch");
			string gpu = ProjectFiles.ReadAllText("Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.cs");
			LogAssert.IsTrue(gpu.Contains("if (mainCandidate) m.MainMask |= mainLevels >= 0 ? mainLevels : levelMask;")
				&& gpu.Contains("int casts = (shadowLevels >= 0 ? shadowLevels : levelMask) & (int)m.Data.ShadowMask;"),
				"the camera's and the shadow's levels are each their own instances'");
			LogAssert.IsTrue(props.Contains("gatheredRunGather != TerrainGpuRenderer.RunGather"), "a kept list is not reused across the switch");
			LogAssert.IsTrue(props.Contains("own.Encapsulate(m.GetColumn(3));") && props.Contains("own.Encapsulate(m.MultiplyPoint3x4(model.LocalReference));"),
				"a run's bounds hold every pivot and reference point");
			string trees = ProjectFiles.ReadAllText("Assets/Scripts/Client/World/Terrain/TerrainTreeField.cs");
			LogAssert.IsTrue(trees.Contains("mainCandidate: keptInView"), "the trees' shadow-only chunks are no main-view candidates");
			string diagnostics = ProjectFiles.ReadAllText("Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.Diagnostics.cs");
			LogAssert.IsTrue(diagnostics.Contains("(item.Padding & 1u) == 0u"), "the self-check's mirror culls a shadow-only work item as the kernel does");
		}
	}
}
