using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>Camera-local HLOD selection. Original geometry remains resident and supplies all nearby shadows.</summary>
	[DisallowMultipleComponent, DefaultExecutionOrder(-10000)]
	public sealed class WorldHlod : MonoBehaviour
	{
		[Tooltip("Experimental opaque aggregation. General GPU gains and full shading equivalence have not been established; see Docs/Rendering/2026-10-29/README.md.")]
		public WorldHlodData Data;
		public sealed class Group
		{
			public WorldHlod Owner;
			public WorldHlodData.Cluster Data;
			public bool Candidate, Selected;
			public int AvailableMembers, Index;
			public readonly List<int> Models = new List<int>();
		}
		public readonly struct Range
		{
			public readonly int Start, Count;
			public readonly Group Group;
			public Range(int start, int count, Group group) { Start = start; Count = count; Group = group; }
		}
		private static readonly List<WorldHlod> live = new List<WorldHlod>();
		private static readonly Dictionary<ScenePropSet, Group[]> membership = new Dictionary<ScenePropSet, Group[]>();
		private static bool featureEnabled = true;
		public static bool Enabled { get => featureEnabled; set { if (featureEnabled != value) { featureEnabled = value; Revision++; ClearSelection(); } } }
		public static int Revision { get; private set; }
		public static bool HasData => live.Count > 0;
		public static bool Available => featureEnabled && HasData;
		private static int[] visible = Array.Empty<int>(), replaced = Array.Empty<int>(), parts = Array.Empty<int>();
		private static int maxModel;
		private static readonly List<WorldHlodCostMath.Cluster> costs = new List<WorldHlodCostMath.Cluster>();
		private static readonly WorldHlodCostMath.Scratch selectionScratch = new WorldHlodCostMath.Scratch();
		private readonly List<Group> groups = new List<Group>();
		private static readonly List<Group> candidates = new List<Group>();
		private static int selectedGroups, selectedParts, savedCommands, selectionVersion, estimatedAvoided, estimatedProxies, rangeExcluded;
		[StructLayout(LayoutKind.Sequential)] private struct CullPart { public Vector4 Sphere; public uint Cluster, Pad0, Pad1, Pad2; }
		private GraphicsBuffer partBuffer, selectedBuffer, argsBuffer;
		private ComputeShader culling;
		private int cullKernel, selectedCount, uploadedSelection = -1;
		private uint[] selectedFlags;
		private readonly Vector4[] planeVectors = new Vector4[6];
		private CommandBuffer cmd;
		private static readonly int PartsId = Shader.PropertyToID("_HlodParts"), SelectedId = Shader.PropertyToID("_HlodSelected"), ArgsId = Shader.PropertyToID("_HlodArgs"), CountId = Shader.PropertyToID("_HlodPartCount"), PlanesId = Shader.PropertyToID("_HlodPlanes");
		private static MaterialPropertyBlock block;
		private static readonly int ContactId = Shader.PropertyToID("_FishContactOn"), FloraId = Shader.PropertyToID("_FishFloraOn");
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetState() { live.Clear(); membership.Clear(); candidates.Clear(); visible = replaced = parts = Array.Empty<int>(); maxModel = 0; featureEnabled = true; Revision++; }
		private void OnEnable()
		{
			if (!Application.isPlaying) return;
			if (Data == null || !Data.SourcesMatch() || Data.CullingCompute == null || !SystemInfo.supportsComputeShaders || !SystemInfo.supportsIndirectArgumentsBuffer || Data.Clusters.Length == 0) { if (Data != null) Debug.LogWarning($"[HLOD] '{name}' has stale source data; using original props."); return; }
			foreach (var source in Data.Sources) if (membership.ContainsKey(source.Set)) { Debug.LogWarning("[HLOD] Duplicate prop source; using original props for this HLOD set."); return; }
			foreach (var source in Data.Sources) membership.Add(source.Set, new Group[source.Props]);
			foreach (var data in Data.Clusters)
			{
				var group = new Group { Owner = this, Data = data, Index = groups.Count }; groups.Add(group);
				foreach (var member in data.Members) membership[Data.Sources[member.Source].Set][member.Prop] = group;
			}
			cmd = new CommandBuffer { name = "FishMMO HLOD culling" };
			culling = Instantiate(Data.CullingCompute); cullKernel = culling.FindKernel("CullHlod");
			var partData = new List<CullPart>(); var args = new List<GraphicsBuffer.IndirectDrawIndexedArgs>();
			foreach (var group in groups) foreach (var part in group.Data.Parts)
			{
				partData.Add(new CullPart { Sphere = new Vector4(group.Data.Bounds.center.x, group.Data.Bounds.center.y, group.Data.Bounds.center.z, group.Data.Bounds.extents.magnitude), Cluster = (uint)group.Index });
				args.Add(new GraphicsBuffer.IndirectDrawIndexedArgs { indexCountPerInstance = part.Mesh.GetIndexCount(0), startIndex = part.Mesh.GetIndexStart(0), baseVertexIndex = (uint)part.Mesh.GetBaseVertex(0) });
			}
			partBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, partData.Count, 32); partBuffer.SetData(partData);
			selectedBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, groups.Count, 4); selectedFlags = new uint[groups.Count];
			argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, args.Count, GraphicsBuffer.IndirectDrawIndexedArgs.size); argsBuffer.SetData(args);
			live.Add(this); Revision++;
		}
		private void OnDisable()
		{
			if (!live.Remove(this)) return;
			foreach (var group in groups) group.Selected = group.Candidate = false;
			foreach (var source in Data.Sources) membership.Remove(source.Set);
			groups.Clear(); partBuffer?.Release(); selectedBuffer?.Release(); argsBuffer?.Release();
			partBuffer = selectedBuffer = argsBuffer = null;
			if (Application.isPlaying) Destroy(culling); else DestroyImmediate(culling);
			culling = null; cmd?.Release(); cmd = null; Revision++;
		}
		private void OnDestroy() => cmd?.Release();
		public static Group Membership(ScenePropSet set, int prop)
			=> set != null && membership.TryGetValue(set, out var map) && prop >= 0 && prop < map.Length ? map[prop] : null;
		public static void RegisterMember(Group group, bool gpuSupported) { if (group != null && gpuSupported) group.AvailableMembers++; }
		public static void UnregisterMember(Group group, bool gpuSupported) { if (group != null && gpuSupported) group.AvailableMembers--; }
		public static bool HasCandidates => candidates.Count > 0;
		private static void ClearSelection() { foreach (var owner in live) { owner.selectedCount = 0; foreach (var group in owner.groups) { group.Selected = group.Candidate = false; group.Models.Clear(); } } selectedGroups = selectedParts = savedCommands = estimatedAvoided = estimatedProxies = rangeExcluded = 0; selectionVersion++; }
		public static void BeginGather(Camera camera, in TerrainTreeField.View view, Plane[] widenedPlanes, bool gpu)
		{
			ClearSelection(); candidates.Clear(); maxModel = 0;
			Array.Clear(visible, 0, visible.Length); Array.Clear(replaced, 0, replaced.Length); Array.Clear(parts, 0, parts.Length);
			if (!Available || !gpu || camera.cameraType != CameraType.Game || view.Orthographic) return;
			float slack = CliffRockInstancing.RegatherMetres;
			foreach (var owner in live) foreach (var group in owner.groups)
			{
				var data = group.Data;
				if (group.AvailableMembers != data.Members.Length || data.Parts.Length == 0) continue;
				float near = TerrainTreeMath.MinDistance(data.Bounds, view.Position) - slack;
				float far = TerrainTreeMath.MaxDistance(data.Bounds, view.Position) + slack;
				if (near <= Mathf.Max(20, data.MinDistanceFactor * view.ScreenFactor) || far >= Mathf.Min(camera.farClipPlane, Mathf.Min(CliffRockInstancing.DrawDistanceFor(data.MinSourceSize) * (1 - CliffRockInstancing.ThinShare), data.MaxDistanceFactor * view.ScreenFactor))) { rangeExcluded++; continue; }
				bool layers = true; foreach (var part in data.Parts) if ((camera.cullingMask & (1 << part.Layer)) == 0) { layers = false; break; }
				if (!layers || !TerrainTreeMath.IntersectsFrustum(widenedPlanes, data.Bounds)) continue;
				group.Candidate = true; candidates.Add(group);
			}
		}
		public static void Account(int model, int count, int originalCommands, Group group)
		{
			if (model < 0 || candidates.Count == 0) return;
			if (visible.Length <= model)
			{
				int n = Mathf.NextPowerOfTwo(model + 1); Array.Resize(ref visible, n); Array.Resize(ref replaced, n); Array.Resize(ref parts, n);
			}
			visible[model] += count; parts[model] = originalCommands; maxModel = Mathf.Max(maxModel, model + 1);
			if (group != null && group.Candidate) { replaced[model] += count; if (!group.Models.Contains(model)) group.Models.Add(model); }
		}
		public static void Select()
		{
			costs.Clear(); foreach (var group in candidates) costs.Add(new WorldHlodCostMath.Cluster(group.Models,group.Data.Parts.Length));
			int saving = WorldHlodCostMath.Select(visible,replaced,parts,costs,selectionScratch,out estimatedAvoided,out estimatedProxies);
			if (saving == 0) return;
			for (int i = 0; i < candidates.Count; i++) if (selectionScratch.Selected[i]) { var group = candidates[i]; group.Selected = true; group.Owner.selectedCount++; selectedGroups++; selectedParts += group.Data.Parts.Length; }
			savedCommands = saving;
		}

		public static int Draw(Camera camera, ScriptableRenderContext context, in TerrainTreeField.View view)
		{
			int draws = 0;
			if (!Available || camera.cameraType != CameraType.Game) return 0;
			if (block == null) block = new MaterialPropertyBlock();
			block.SetFloat(ContactId, 1); block.SetFloat(FloraId, 0);
			foreach (var owner in live)
			{
				if (owner.selectedCount == 0) continue;
				owner.cmd.Clear();
				if (owner.uploadedSelection != selectionVersion)
				{
					foreach (var group in owner.groups) owner.selectedFlags[group.Index] = group.Selected ? 1u : 0u;
					owner.cmd.SetBufferData(owner.selectedBuffer, owner.selectedFlags); owner.uploadedSelection = selectionVersion;
				}
				for (int p = 0; p < 6; p++) { var plane = view.Planes[p]; owner.planeVectors[p] = new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance); }
				owner.cmd.SetComputeVectorArrayParam(owner.culling, PlanesId, owner.planeVectors);
				owner.cmd.SetComputeIntParam(owner.culling, CountId, owner.partBuffer.count);
				owner.cmd.SetComputeBufferParam(owner.culling, owner.cullKernel, PartsId, owner.partBuffer);
				owner.cmd.SetComputeBufferParam(owner.culling, owner.cullKernel, SelectedId, owner.selectedBuffer);
				owner.cmd.SetComputeBufferParam(owner.culling, owner.cullKernel, ArgsId, owner.argsBuffer);
				FishDepthPyramid.Bind(owner.cmd, owner.culling, owner.cullKernel, camera, view.Position, true);
				owner.cmd.DispatchComputeChecked(owner.culling, owner.cullKernel, (owner.partBuffer.count + 63) / 64, 1, 1);
				context.ExecuteCommandBuffer(owner.cmd); owner.cmd.Clear();
				int command = 0;
				foreach (var group in owner.groups) foreach (var part in group.Data.Parts)
				{
					if (group.Selected)
					{
						var rp = new RenderParams(part.Material) { camera = camera, layer = part.Layer, renderingLayerMask = part.RenderingLayerMask, worldBounds = group.Data.Bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = part.ReceiveShadows, lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off, motionVectorMode = MotionVectorGenerationMode.Camera, matProps = block };
						Graphics.RenderMeshIndirect(rp, part.Mesh, owner.argsBuffer, 1, command); draws++;
					}
					command++;
				}
			}
			return draws;
		}
		public static string Describe()
		{
			int ready = 0, total = 0; foreach (var owner in live) foreach (var group in owner.groups) { total++; if (group.AvailableMembers == group.Data.Members.Length) ready++; }
			return $"HLOD {featureEnabled}: {live.Count} sets, {selectedGroups} clusters, {selectedParts} proxy draws, estimated {savedCommands} commands saved; ready {ready}/{total}, range rejected {rangeExcluded}, candidates {candidates.Count}, cost {estimatedProxies} vs {estimatedAvoided} avoided";
		}
	}
}
