using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Client;
using FishMMO.Shared;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.TestHarness.World.Editor
{
	/// <summary>Lossless last-LOD aggregation. Does not change collision, source assets or shadow geometry.</summary>
	public sealed class WorldHlodBaker : IProcessSceneWithReport
	{
		public int callbackOrder => 900;
		public void OnProcessScene(Scene scene, BuildReport report)
		{
			if (report == null) return;
			foreach (var root in scene.GetRootGameObjects()) foreach (var owner in root.GetComponentsInChildren<WorldHlod>(true))
			{
				var data = owner.Data;
				if (data == null || !data.SourcesMatch()) throw new BuildFailedException("HLOD source transforms changed; rebake " + scene.path);
				foreach (var source in data.Sources) if (source.EditorDependencyHash != DependencyHash(source.Set)) throw new BuildFailedException("HLOD source geometry/materials changed; rebake " + scene.path);
			}
		}
		private sealed class Placed
		{
			public TerrainTreeModel Model;
			public Matrix4x4 Matrix;
			public int Source, Prop;
			public float Size;
		}
		private sealed class Palette
		{
			public Material Source, Material;
			public readonly Dictionary<Material,int> Looks = new Dictionary<Material,int>();
			public readonly List<TerrainLookMath.LookValues> Values = new List<TerrainLookMath.LookValues>();
		}
		private sealed class Family
		{
			public Palette Palette;
			public Family(Palette palette) { Palette = palette; Looks = palette.Looks; Values = palette.Values; }
			public Material Source;
			public RenderParams Params;
			public readonly Dictionary<Material, int> Looks;
			public readonly List<TerrainLookMath.LookValues> Values;
			public readonly List<Vector3> Positions = new List<Vector3>(), Normals = new List<Vector3>();
			public readonly List<Vector4> Tangents = new List<Vector4>(), LookOrigins = new List<Vector4>();
			public readonly List<Vector2> Uv = new List<Vector2>();
			public readonly List<int> Indices = new List<int>();
			public int Look(Material m) { if (Looks.TryGetValue(m, out int row)) return row; row = Values.Count; Looks.Add(m, row); Values.Add(TerrainGpuRenderer.ReadLook(m)); return row; }
		}
		public static string DependencyHash(ScenePropSet set) => AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(set)).ToString();
		[MenuItem("FishMMO/Rendering/Bake experimental HLOD for open scene")]
		public static void BakeOpen() => Bake(SceneManager.GetActiveScene());
		public static void BakeMoron2()
		{
			var scene = EditorSceneManager.OpenScene("Assets/Scenes/WorldScene/Moron/Moron 2.unity", OpenSceneMode.Single);
			Bake(scene); EditorSceneManager.SaveScene(scene); EditorApplication.Exit(0);
		}
		public static void Bake(Scene scene)
		{
			if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Save the scene before baking HLOD.");
			var sources = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SceneProps>(true)).SelectMany(p => p.Sets).Where(s => s != null).Distinct().ToArray();
			var cells = new Dictionary<(int Side,int X,int Y,int Z,int Size,int Lod), List<Placed>>(); var allModels = new List<TerrainTreeModel>();
			int eligible = 0, rejected = 0;
			var familyKeys = new Dictionary<Material, string>();
			string Key(Material material) { if (!familyKeys.TryGetValue(material, out string key)) familyKeys.Add(material, key = TerrainGpuRenderer.LookFamilyKey(material)); return key; }
			try
			{
				for (int si = 0; si < sources.Length; si++)
				{
					var source = sources[si]; var models = new TerrainTreeModel[source.Prototypes.Length];
					for (int i = 0; i < models.Length; i++)
					{
						var prototype = source.Prototypes[i];
						var model = TerrainTreeModel.Build(prototype.Prefab, prototype.Layer, prototype.Materials, out _);
						if (model == null) continue; allModels.Add(model);
						if (Eligible(model)) models[i] = model;
					}
					for (int pi = 0; pi < source.Props.Length; pi++)
					{
						var prop = source.Props[pi]; var model = prop.Prototype >= 0 && prop.Prototype < models.Length ? models[prop.Prototype] : null;
						if (model == null || prop.Scale.x <= 0 || prop.Scale.y <= 0 || prop.Scale.z <= 0) { rejected++; continue; }
						float size = model.Size * Mathf.Max(prop.Scale.x, prop.Scale.y);
						// Separate size bands so a small piece cannot prematurely cull a cluster of large props.
						int last = model.Levels.Length - 1;
						float previous = last > 0 ? model.Transitions[last - 1] : 1f;
						float low = last > 0 && previous > 0 ? size / previous * 1.7320508f : 20f;
						float cutoff = model.Transitions[last] + model.FadeWidths[last];
						float high = Mathf.Min(Mathf.Clamp(size * CliffRockInstancing.MetresPerMetreOfSize, CliffRockInstancing.MinDrawDistance, CliffRockInstancing.MaxDrawDistance) * 1.4142136f * (1 - CliffRockInstancing.ThinShare), cutoff > 0 ? size / cutoff * 1.7320508f : float.MaxValue);
						if (high <= low + 6) { rejected++; continue; }
						int side = Mathf.Clamp(Mathf.ClosestPowerOfTwo((int)((high-low) / 4)),4,64);
						var key = (Side:side,X:Mathf.FloorToInt(prop.Position.x / side),Y:Mathf.FloorToInt(prop.Position.y / side),Z:Mathf.FloorToInt(prop.Position.z / side),Size:Mathf.FloorToInt(Mathf.Log(Mathf.Max(.01f,size),2)),Lod:Mathf.FloorToInt(Mathf.Log(Mathf.Max(.0001f,previous),2)));
						if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<Placed>());
						list.Add(new Placed { Model = model, Matrix = prop.Matrix, Source = si, Prop = pi, Size = size }); eligible++;
					}
				}
				var shader = Shader.Find("FishMMO/World HLOD"); if (shader == null) throw new InvalidOperationException("HLOD shader missing");
				var data = ScriptableObject.CreateInstance<WorldHlodData>(); data.name = scene.name + " HLOD"; data.ScenePath = scene.path;
				data.Sources = sources.Select(s => new WorldHlodData.Source { Set = s, Props = s.Props.Length, TransformHash = WorldHlodData.SourceHash(s), EditorDependencyHash = DependencyHash(s) }).ToArray();
				data.CullingCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Prefabs/Client/Weather/Shaders/FishWorldHlodCull.compute");
				var clusters = new List<WorldHlodData.Cluster>(); var assets = new List<Object>();
				var palettes = new Dictionary<string,Palette>();
				foreach (var cell in cells.OrderByDescending(c => (double)c.Value.Select(p => p.Model).Distinct().Count() / Math.Max(1,c.Value.Sum(p => p.Model.Levels.Last().Parts.Sum(part => (long)part.Mesh.vertexCount)))).ThenBy(c => c.Key.X).ThenBy(c => c.Key.Y).ThenBy(c => c.Key.Z))
				{
					var distinct = new HashSet<string>(); var families = new Dictionary<string, Family>();
					foreach (var placed in cell.Value)
						foreach (var part in placed.Model.Levels.Last().Parts) for (int slot = 0; slot < part.Params.Length; slot++)
						{
							var rp = part.Params[slot]; var mat = rp.material;
							distinct.Add(part.Mesh.GetInstanceID() + "/" + part.Submesh[slot] + "/" + mat.GetInstanceID());
							string key = Key(mat) + "/" + rp.layer + "/" + rp.renderingLayerMask + "/" + rp.receiveShadows;
							if (!palettes.TryGetValue(key,out var palette)) palettes.Add(key,palette = new Palette { Source = mat });
							if (!families.TryGetValue(key, out var family)) families.Add(key, family = new Family(palette) { Source = mat, Params = rp });
						}
					if (distinct.Count < families.Count + 4) continue;
					var cluster = new WorldHlodData.Cluster { MinSourceSize = float.MaxValue, MaxDistanceFactor = float.MaxValue, OriginalParts = distinct.Count, Members = cell.Value.Select(p => new WorldHlodData.Member { Source = p.Source, Prop = p.Prop }).ToArray() };
					bool first = true;
					foreach (var placed in cell.Value)
					{
						var model = placed.Model; int last = model.Levels.Length - 1;
						Bounds bounds = TerrainTreeMath.TransformBounds(placed.Matrix, model.LocalBounds);
						bounds.Encapsulate(placed.Matrix.MultiplyPoint3x4(model.LocalReference)); bounds.Encapsulate(placed.Matrix.GetColumn(3));
						if (first) { cluster.Bounds = bounds; first = false; } else cluster.Bounds.Encapsulate(bounds);
						cluster.MinSourceSize = Mathf.Min(cluster.MinSourceSize, placed.Size);
						if (last > 0 && model.Transitions[last - 1] > 0) cluster.MinDistanceFactor = Mathf.Max(cluster.MinDistanceFactor, placed.Size / model.Transitions[last - 1]);
						float cutoff = model.Transitions[last] + model.FadeWidths[last];
						if (cutoff > 0) cluster.MaxDistanceFactor = Mathf.Min(cluster.MaxDistanceFactor, placed.Size / cutoff);
						foreach (var part in model.Levels[last].Parts) for (int slot = 0; slot < part.Params.Length; slot++)
						{
							var rp = part.Params[slot]; string key = Key(rp.material) + "/" + rp.layer + "/" + rp.renderingLayerMask + "/" + rp.receiveShadows;
							Append(families[key], placed.Matrix, part.Mesh, part.Submesh[slot], rp.material);
						}
					}
					float entry = Mathf.Max(20,cluster.MinDistanceFactor*1.7320508f), exit = Mathf.Min(Mathf.Clamp(cluster.MinSourceSize*CliffRockInstancing.MetresPerMetreOfSize,CliffRockInstancing.MinDrawDistance,CliffRockInstancing.MaxDrawDistance)*1.4142136f*(1-CliffRockInstancing.ThinShare),cluster.MaxDistanceFactor*1.7320508f);
					if (exit <= entry + cluster.Bounds.extents.magnitude*2 + 3) continue;
					long bytes = families.Values.Sum(f => (long)f.Positions.Count * 64 + (long)f.Indices.Count * 4);
					if (data.MeshBytes + bytes > 64L * 1024 * 1024) continue;
					var proxyParts = new List<WorldHlodData.Part>();
					foreach (var family in families.Values)
					{
						var mesh = new Mesh { name = "HLOD " + clusters.Count + "/" + proxyParts.Count, indexFormat = IndexFormat.UInt32 };
						mesh.SetVertices(family.Positions); mesh.SetNormals(family.Normals); mesh.SetTangents(family.Tangents); mesh.SetUVs(0, family.Uv); mesh.SetUVs(3, family.LookOrigins); mesh.SetIndices(family.Indices, MeshTopology.Triangles, 0); mesh.RecalculateBounds();
						var palette = family.Palette;
						if (palette.Material == null)
						{
							palette.Material = new Material(palette.Source) { name = palette.Source.name + " (HLOD family)",shader = shader,enableInstancing = true };
							palette.Material.SetTextureScale("_BaseMap",Vector2.one); palette.Material.SetTextureOffset("_BaseMap",Vector2.zero);
						}
						var material = palette.Material;
						proxyParts.Add(new WorldHlodData.Part { Mesh = mesh, Material = material, Layer = family.Params.layer, RenderingLayerMask = family.Params.renderingLayerMask, ReceiveShadows = family.Params.receiveShadows });
						assets.Add(mesh); cluster.ProxyTriangles += family.Indices.Count / 3;
					}
					cluster.SourceTriangles = cluster.ProxyTriangles; cluster.Origin = cluster.Bounds.center; cluster.Parts = proxyParts.ToArray(); clusters.Add(cluster); data.MeshBytes += bytes;
				}
				foreach (var palette in palettes.Values) if (palette.Material != null)
				{
					var table = new Texture2D(TerrainLookMath.TableColumns,TerrainLookMath.TableRows(palette.Values.Count),TextureFormat.RGBAHalf,false,true) { name = palette.Material.name + " looks",filterMode = FilterMode.Point,wrapMode = TextureWrapMode.Clamp };
					var pixels = new Color[table.width*table.height]; for (int row = 0; row < palette.Values.Count; row++) TerrainLookMath.WriteRow(palette.Values[row],pixels,table.width,row);
					table.SetPixels(pixels); table.Apply(false,true); palette.Material.SetTexture(TerrainLookMath.TableName,table); assets.Add(table); assets.Add(palette.Material);
				}
				data.Clusters = clusters.ToArray();
				string folder = Path.GetDirectoryName(scene.path).Replace('\\', '/') + "/" + scene.name + " HLOD";
				Directory.CreateDirectory(folder); AssetDatabase.Refresh();
				// A new bake is reviewable and does not destroy an existing asset or other scene objects.
				string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/WorldHlod.asset"); AssetDatabase.CreateAsset(data, path);
				foreach (var asset in assets) AssetDatabase.AddObjectToAsset(asset, data);
				AssetDatabase.SaveAssetIfDirty(data);
				var owners = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WorldHlod>(true)).ToArray();
				WorldHlod owner;
				if (owners.Length == 0) { var host = new GameObject("World HLOD"); SceneManager.MoveGameObjectToScene(host, scene); host.AddComponent<ClientOnlyObject>(); owner = host.AddComponent<WorldHlod>(); }
				else if (owners.Length == 1) owner = owners[0]; else throw new InvalidOperationException("Multiple HLOD owners in scene");
				owner.Data = data; EditorUtility.SetDirty(owner); EditorSceneManager.MarkSceneDirty(scene);
				Debug.Log($"[HLOD Bake] {scene.name}: {eligible} eligible / {rejected} excluded props; {clusters.Count} clusters, {clusters.Sum(c => c.Parts.Length)} proxy parts, {palettes.Values.Count(p => p.Material != null)} shared materials, {data.MeshBytes / 1048576f:F2} MiB; {path}");
			}
			finally { foreach (var model in allModels) model.Dispose(); }
		}
		private static bool Eligible(TerrainTreeModel model)
		{
			foreach (var level in model.Levels) foreach (var part in level.Parts)
			{
				if (!part.Identity || part.Mesh == null || !part.Mesh.isReadable || part.Mesh.normals.Length != part.Mesh.vertexCount || part.Mesh.tangents.Length != part.Mesh.vertexCount || part.Mesh.uv.Length != part.Mesh.vertexCount) return false;
				foreach (var rp in part.Params)
				{
					var m = rp.material;
					if (m == null || m.shader == null || m.shader.name != "FishMMO/Weather Lit" || !m.IsKeywordEnabled(TerrainLookMath.Keyword) || !TerrainLookMath.KeywordsAllowLooks(m.shaderKeywords) || m.renderQueue >= 2450 || m.GetFloat("_Surface") != 0) return false;
				}
			}
			return true;
		}
		public static void AppendGeometry(Matrix4x4 matrix, Mesh mesh, int submesh, Vector4 st, int look, List<Vector3> positions, List<Vector3> normals, List<Vector4> tangents, List<Vector2> uv, List<Vector4> lookOrigins, List<int> indices)
		{
			var p = mesh.vertices; var n = mesh.normals; var t = mesh.tangents; var u = mesh.uv;
			if (n.Length != p.Length || t.Length != p.Length || u.Length != p.Length) throw new InvalidOperationException("HLOD source missing normals/tangents/UV0: " + mesh.name);
			var sourceIndices = mesh.GetIndices(submesh); var used = new bool[p.Length]; var remap = new int[p.Length];
			foreach (int index in sourceIndices) used[index] = true;
			var normalMatrix = matrix.inverse.transpose; Vector3 origin = matrix.GetColumn(3);
			for (int i = 0; i < p.Length; i++)
			{
				if (!used[i]) continue; remap[i] = positions.Count;
				positions.Add(matrix.MultiplyPoint3x4(p[i])); normals.Add(normalMatrix.MultiplyVector(n[i]).normalized);
				Vector3 tangent = matrix.MultiplyVector(new Vector3(t[i].x, t[i].y, t[i].z)).normalized;
				tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, t[i].w)); uv.Add(new Vector2(u[i].x * st.x + st.z, u[i].y * st.y + st.w)); lookOrigins.Add(new Vector4(look, origin.x, origin.y, origin.z));
			}
			foreach (int index in sourceIndices) indices.Add(remap[index]);
		}
		private static void Append(Family family, Matrix4x4 matrix, Mesh mesh, int submesh, Material material)
		{
			int look = family.Look(material); AppendGeometry(matrix, mesh, submesh, family.Values[look].BaseMapST, look, family.Positions, family.Normals, family.Tangents, family.Uv, family.LookOrigins, family.Indices);
		}
	}
}
