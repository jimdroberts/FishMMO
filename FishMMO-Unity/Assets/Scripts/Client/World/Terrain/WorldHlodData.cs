using System;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>Prebuilt, source-stamped aggregate geometry for distant static opaque props.</summary>
	[PreferBinarySerialization]
	public sealed class WorldHlodData : ScriptableObject
	{
		public const int CurrentVersion = 1;
		[Serializable] public struct Source
		{
			public ScenePropSet Set;
			public int Props;
			public uint TransformHash;
			public string EditorDependencyHash;
		}
		[Serializable] public struct Member { public int Source, Prop; }
		[Serializable] public struct Part
		{
			public Mesh Mesh;
			public Material Material;
			public int Layer;
			public uint RenderingLayerMask;
			public bool ReceiveShadows;
		}
		[Serializable] public sealed class Cluster
		{
			public Bounds Bounds;
			public Vector3 Origin;
			// A cluster is used only when every source is fully on its last LOD and before any source thins/culls.
			public float MinDistanceFactor, MaxDistanceFactor, MinSourceSize;
			public Member[] Members = Array.Empty<Member>();
			public Part[] Parts = Array.Empty<Part>();
			public int OriginalParts, SourceTriangles, ProxyTriangles;
		}
		public int Version = CurrentVersion;
		public string ScenePath;
		public float CellMetres = 64;
		public Source[] Sources = Array.Empty<Source>();
		public Cluster[] Clusters = Array.Empty<Cluster>();
		public ComputeShader CullingCompute;
		public long MeshBytes;
		public static uint SourceHash(ScenePropSet set)
		{
			uint hash = 2166136261;
			void Add(uint word) { hash = unchecked((hash ^ word) * 16777619); }
			void F(float f) => Add(unchecked((uint)BitConverter.SingleToInt32Bits(f)));
			if (set == null) return 0;
			Add((uint)set.Prototypes.Length); Add((uint)set.Props.Length);
			foreach (var p in set.Props)
			{
				Add((uint)p.Prototype); F(p.Position.x); F(p.Position.y); F(p.Position.z);
				F(p.Rotation.x); F(p.Rotation.y); F(p.Rotation.z); F(p.Rotation.w);
				F(p.Scale.x); F(p.Scale.y); F(p.Scale.z);
			}
			return hash;
		}
		public bool SourcesMatch()
		{
			if (Version != CurrentVersion || Sources == null || Clusters == null) return false;
			var seenSources = new System.Collections.Generic.HashSet<ScenePropSet>();
			foreach (var source in Sources) if (source.Set == null || !seenSources.Add(source.Set)) return false;
			var members = new System.Collections.Generic.HashSet<long>();
			foreach (var cluster in Clusters)
			{
				if (cluster == null || cluster.Members == null || cluster.Parts == null || cluster.Members.Length == 0 || cluster.Parts.Length == 0 || cluster.MinSourceSize <= 0) return false;
				foreach (var part in cluster.Parts) if (part.Mesh == null || part.Material == null || part.Mesh.subMeshCount != 1) return false;
				foreach (var member in cluster.Members) if (member.Source < 0 || member.Source >= Sources.Length || member.Prop < 0 || member.Prop >= Sources[member.Source].Props || !members.Add(((long)member.Source << 32) | (uint)member.Prop)) return false;
			}
			foreach (var source in Sources)
				if (source.Set == null || source.Set.Props.Length != source.Props || SourceHash(source.Set) != source.TransformHash) return false;
			return true;
		}
	}
}
