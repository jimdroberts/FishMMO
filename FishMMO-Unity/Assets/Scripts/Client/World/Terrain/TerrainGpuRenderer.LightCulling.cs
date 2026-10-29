using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>Conservative tests against Unity's actual shadow-split caster planes, including point-light cube faces.</summary>
	public static class TerrainShadowCullingMath
	{
		public static Vector4 WorldSphere(in FishInstance instance, Vector3 centre, float radius)
		{
			Vector4 p = new Vector4(centre.x, centre.y, centre.z, 1f);
			float sx = new Vector3(instance.Row0.x, instance.Row1.x, instance.Row2.x).magnitude;
			float sy = new Vector3(instance.Row0.y, instance.Row1.y, instance.Row2.y).magnitude;
			float sz = new Vector3(instance.Row0.z, instance.Row1.z, instance.Row2.z).magnitude;
			return new Vector4(Vector4.Dot(instance.Row0, p), Vector4.Dot(instance.Row1, p), Vector4.Dot(instance.Row2, p), radius * Mathf.Max(sx, Mathf.Max(sy, sz)));
		}

		public static ushort SplitMask(Vector4 sphere, NativeArray<Plane> planes, NativeArray<CullingSplit> splits, ushort wanted = ushort.MaxValue)
		{
			return BoundsMask(new Bounds(new Vector3(sphere.x, sphere.y, sphere.z), Vector3.one * (2f * sphere.w)), planes, splits, wanted, sphere.w);
		}

		public static ushort BoundsMask(Bounds bounds, NativeArray<Plane> planes, NativeArray<CullingSplit> splits, ushort wanted = ushort.MaxValue, float sphereRadius = -1f)
		{
			if (splits.Length == 0) return (ushort)(wanted & 1);
			ushort result = 0;
			for (int s = 0; s < Mathf.Min(splits.Length, 16); s++)
			{
				int bit = 1 << s;
				if ((wanted & bit) == 0) continue;
				var split = splits[s];
				int end = split.cullingPlaneOffset + split.cullingPlaneCount;
				bool visible = true;
				// Missing/unrecognized planes must keep the draw, never discard a potentially valid shadow.
				if (split.cullingPlaneOffset >= 0 && end <= planes.Length)
					for (int p = split.cullingPlaneOffset; p < end; p++)
					{
						Plane plane = planes[p];
						Vector3 n = plane.normal;
						float reach = sphereRadius >= 0f ? sphereRadius * n.magnitude : Vector3.Dot(new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z)), bounds.extents);
						if (plane.GetDistanceToPoint(bounds.center) < -reach - 0.01f) { visible = false; break; }
					}
				if (visible) result |= (ushort)bit;
			}
			return result;
		}
	}

	/// <summary>Owns a light's split-mask snapshot. Reuse requires exact plane/work revisions, not a hash match.</summary>
	public sealed class TerrainShadowSplitCache
	{
		private int instanceVersion = -1, count;
		private long workRevision = -1;
		private Plane[] planes = Array.Empty<Plane>();
		private CullingSplit[] splits = Array.Empty<CullingSplit>();
		private ushort[] masks = Array.Empty<ushort>();
		public int LastFrame;

		public bool Matches(int version, long revision, NativeArray<Plane> currentPlanes, NativeArray<CullingSplit> currentSplits, int modelCount)
		{
			if (instanceVersion != version || workRevision != revision || count != modelCount || planes.Length != currentPlanes.Length || splits.Length != currentSplits.Length) return false;
			for (int i = 0; i < planes.Length; i++)
				if (!planes[i].normal.Equals(currentPlanes[i].normal) || !planes[i].distance.Equals(currentPlanes[i].distance)) return false;
			for (int i = 0; i < splits.Length; i++)
				if (splits[i].cullingPlaneOffset != currentSplits[i].cullingPlaneOffset || splits[i].cullingPlaneCount != currentSplits[i].cullingPlaneCount) return false;
			return true;
		}

		public void Capture(int version, long revision, NativeArray<Plane> currentPlanes, NativeArray<CullingSplit> currentSplits, ushort[] currentMasks, int modelCount)
		{
			instanceVersion = version; workRevision = revision; count = modelCount;
			if (planes.Length != currentPlanes.Length) planes = new Plane[currentPlanes.Length];
			if (splits.Length != currentSplits.Length) splits = new CullingSplit[currentSplits.Length];
			if (masks.Length < modelCount) masks = new ushort[modelCount];
			currentPlanes.CopyTo(planes); currentSplits.CopyTo(splits); Array.Copy(currentMasks, masks, count);
		}

		public void CopyMasksTo(ushort[] target) => Array.Copy(masks, target, count);
	}

	public sealed partial class TerrainGpuRenderer
	{
		/// <summary>A/B diagnostic: off restores the original all-commands/all-splits light submissions.</summary>
		public static bool CullLightCommands = true;
		private sealed class ShadowRange
		{
			public int Version = -1;
			public Vector4[] Spheres;
			public Bounds Bounds;
		}
		private readonly Dictionary<(uint Start, uint Count, uint Model), ShadowRange> shadowRanges = new Dictionary<(uint, uint, uint), ShadowRange>();
		private ushort[] lightModelMasks = Array.Empty<ushort>();
		private readonly Dictionary<EntityId, TerrainShadowSplitCache> lightSplitCaches = new Dictionary<EntityId, TerrainShadowSplitCache>();
		private FishWork[] lightWorkSnapshot = Array.Empty<FishWork>();
		private int[] lightWorkModelMasks = Array.Empty<int>();
		private int lightWorkCount = -1, lightWorkModels = -1;
		private long lightWorkRevision;
		private bool lightWorkPrepared;

		private void PrepareLightWork()
		{
			lightWorkPrepared = true;
			bool changed = lightWorkCount != workCount || lightWorkModels != models.Count;
			if (!changed)
			{
				for (int i = 0; i < models.Count && !changed; i++) changed = lightWorkModelMasks[i] != models[i].ShadowMask;
				for (int i = 0; i < workCount && !changed; i++)
				{
					FishWork a = work[i], b = lightWorkSnapshot[i];
					changed = a.Start != b.Start || a.Count != b.Count || a.Model != b.Model;
				}
			}
			if (!changed) return;
			lightWorkRevision++;
			lightWorkCount = workCount; lightWorkModels = models.Count;
			if (lightWorkSnapshot.Length < workCount) lightWorkSnapshot = new FishWork[workCount];
			if (lightWorkModelMasks.Length < models.Count) lightWorkModelMasks = new int[models.Count];
			for (int i = 0; i < workCount; i++) lightWorkSnapshot[i] = work[i];
			for (int i = 0; i < models.Count; i++) lightWorkModelMasks[i] = models[i].ShadowMask;
		}
		public int CameraLightCallbacks { get; private set; }
		public int CameraLightFacesBeforeCull { get; private set; }
		public int CameraLightFacesAfterCull { get; private set; }
		public int LastBrgLightCommandsBeforeCull { get; private set; }
		public int LastBrgLightFacesBeforeCull { get; private set; }
		public int LastBrgLightFacesAfterCull { get; private set; }

		private void CullLight(in BatchCullingContext context)
		{
			if (!lightWorkPrepared) PrepareLightWork();
			if (lightModelMasks.Length < models.Count) lightModelMasks = new ushort[models.Count];
			EntityId light = context.viewID.GetEntityId();
			if (!lightSplitCaches.TryGetValue(light, out TerrainShadowSplitCache cached))
			{
				// Recycle the oldest entry to bound memory for transient spell/projectile lights.
				if (lightSplitCaches.Count >= 64)
				{
					EntityId oldest = default; int frame = int.MaxValue;
					foreach (var pair in lightSplitCaches) if (pair.Value.LastFrame < frame) { oldest = pair.Key; frame = pair.Value.LastFrame; cached = pair.Value; }
					lightSplitCaches.Remove(oldest);
				}
				cached ??= new TerrainShadowSplitCache();
				lightSplitCaches.Add(light, cached);
				// A recycled cache belongs to another light; its planes are checked below as well.
			}
			cached.LastFrame = Time.frameCount;
			if (cached.Matches(RockVersion, lightWorkRevision, context.cullingPlanes, context.cullingSplits, models.Count))
			{
				cached.CopyMasksTo(lightModelMasks);
				return;
			}
			Array.Clear(lightModelMasks, 0, models.Count);
			ushort all = TerrainBrgMath.SplitMask(context.cullingSplits.Length);
			for (int w = 0; w < workCount; w++)
			{
				FishWork item = work[w];
				int m = (int)item.Model;
				if (models[m].ShadowMask == 0 || lightModelMasks[m] == all) continue;
				ushort needed = (ushort)(all & ~lightModelMasks[m]);
				var key = (item.Start, item.Count, item.Model);
				if (!shadowRanges.TryGetValue(key, out ShadowRange range))
				{
					range = new ShadowRange { Spheres = new Vector4[item.Count] };
					shadowRanges.Add(key, range);
				}
				if (range.Version != RockVersion)
				{
					FishModelData model = models[m].Data;
					// Same deformation allowance as the existing caster shader, with extra room for root-relative scaling.
					float radius = model.SphereRadius * CasterSphereGrowth + CasterSphereMetres + model.SphereCentre.magnitude * (FloraMaxScale - 1f);
					for (int i = 0; i < item.Count; i++)
					{
						Vector4 sphere = TerrainShadowCullingMath.WorldSphere(mirror[(int)item.Start + i], model.SphereCentre, radius);
						range.Spheres[i] = sphere;
						var bounds = new Bounds(new Vector3(sphere.x, sphere.y, sphere.z), Vector3.one * (2f * sphere.w));
						if (i == 0) range.Bounds = bounds; else range.Bounds.Encapsulate(bounds);
					}
					range.Version = RockVersion;
				}
				needed = TerrainShadowCullingMath.BoundsMask(range.Bounds, context.cullingPlanes, context.cullingSplits, needed);
				for (int i = 0; i < range.Spheres.Length && needed != 0; i++)
				{
					ushort visible = TerrainShadowCullingMath.SplitMask(range.Spheres[i], context.cullingPlanes, context.cullingSplits, needed);
					lightModelMasks[m] |= visible;
					needed &= (ushort)~visible;
				}
			}
			cached.Capture(RockVersion, lightWorkRevision, context.cullingPlanes, context.cullingSplits, lightModelMasks, models.Count);
		}

		private ushort CommandLightMask(int command, ushort all, bool cull) => cull ? lightModelMasks[commands[command].Model] : all;
		private static int FaceCount(ushort mask)
		{
			int n = 0;
			for (; mask != 0; mask &= (ushort)(mask - 1)) n++;
			return n;
		}
	}
}
