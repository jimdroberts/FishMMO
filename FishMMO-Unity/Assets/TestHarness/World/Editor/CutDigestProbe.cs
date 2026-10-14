using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.TestHarness.World.Editor
{
	/// <summary>
	/// What a cut made, as digests of its content rather than its file bytes: a re-cut's terrain, scene and asset files
	/// differ byte for byte even from identical code (Unity numbers each new object's local id at random, and terrain and
	/// NavMesh data carry their own), so "is this cut the same" is asked of the data. One line per terrain (heights, splat
	/// maps, holes, each detail layer, trees), per mesh (vertices, normals, UVs, colours, indices), per other asset object
	/// and per scene component (every serialized value, object references as asset paths or hierarchy paths, never ids).
	/// </summary>
	/// <remarks>
	/// <c>-executeMethod FishMMO.TestHarness.World.Editor.CutDigestProbe.Run</c> with FISHMMO_DIGEST_SCENE (a scene path),
	/// FISHMMO_DIGEST_FOLDER (the folder of its generated assets) and FISHMMO_DIGEST_OUT (the report). Compare two reports
	/// with diff.
	/// </remarks>
	public static class CutDigestProbe
	{
		public static void Run()
		{
			int code = 1;
			try
			{
				// FISHMMO_DIGEST_JOBS="scene|folder|out;…" digests several cuts in one launch; else the single-job variables.
				string jobs = Environment.GetEnvironmentVariable("FISHMMO_DIGEST_JOBS")
					?? $"{Environment.GetEnvironmentVariable("FISHMMO_DIGEST_SCENE")}|{Environment.GetEnvironmentVariable("FISHMMO_DIGEST_FOLDER")}|{Environment.GetEnvironmentVariable("FISHMMO_DIGEST_OUT")}";
				foreach (string job in jobs.Split(';').Where(j => j.Length > 0))
				{
					string[] parts = job.Split('|');
					DigestCut(parts[0], parts[1], parts[2]);
				}
				code = 0;
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(code);
			}
		}

		private static void DigestCut(string scenePath, string folder, string output)
		{
			{
				var lines = new List<string>();
				if (!string.IsNullOrEmpty(folder))
				{
					foreach (string path in AssetDatabase.FindAssets(string.Empty, new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, StringComparer.Ordinal))
					{
						if (AssetDatabase.IsValidFolder(path))
						{
							continue;
						}
						string file = Path.GetFileName(path);
						if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
						{
							// A generated texture is its PNG: the pixels as written, not the import's copy.
							using (var sha = SHA1.Create())
							{
								lines.Add($"{file} | png | {BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)), 0, 8).Replace("-", string.Empty)}");
							}
							continue;
						}
						foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path).Where(o => o != null).OrderBy(o => o.GetType().Name).ThenBy(o => o.name, StringComparer.Ordinal))
						{
							lines.Add($"{file} | {o.GetType().Name} '{o.name}' | {Digest(o)}");
						}
					}
				}
				if (!string.IsNullOrEmpty(scenePath))
				{
					Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
					foreach (GameObject root in scene.GetRootGameObjects())
					{
						Walk(root.transform, root.name, lines);
					}
				}
				File.WriteAllLines(output, lines);
				Debug.Log($"[Cut digest] {lines.Count} line(s) to {output}");
			}
		}

		/// <summary>
		/// <c>-executeMethod …CutDigestProbe.MeshRefs</c>: FISHMMO_DIGEST_SCENE's every MeshFilter and MeshCollider as
		/// "hierarchy | component | mesh name", to FISHMMO_DIGEST_OUT.
		/// </summary>
		public static void MeshRefs()
		{
			int code = 1;
			try
			{
				Scene scene = EditorSceneManager.OpenScene(Environment.GetEnvironmentVariable("FISHMMO_DIGEST_SCENE"), OpenSceneMode.Single);
				var lines = new List<string>();
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
					{
						lines.Add($"{Hierarchy(f.transform)} | MeshFilter | {(f.sharedMesh != null ? f.sharedMesh.name : "null")}");
					}
					foreach (MeshCollider c in root.GetComponentsInChildren<MeshCollider>(true))
					{
						lines.Add($"{Hierarchy(c.transform)} | MeshCollider | {(c.sharedMesh != null ? c.sharedMesh.name : "null")}");
					}
				}
				File.WriteAllLines(Environment.GetEnvironmentVariable("FISHMMO_DIGEST_OUT"), lines);
				code = 0;
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(code);
			}
		}

		private static void Walk(Transform t, string path, List<string> lines)
		{
			Component[] components = t.GetComponents<Component>();
			for (int i = 0; i < components.Length; i++)
			{
				Component c = components[i];
				lines.Add(c == null ? $"{path} | #{i} missing script" : $"{path} | #{i} {c.GetType().Name} | {Digest(c)}");
			}
			for (int i = 0; i < t.childCount; i++)
			{
				Transform child = t.GetChild(i);
				Walk(child, $"{path}/{child.name}[{i}]", lines);
			}
		}

		private static string Digest(Object o)
		{
			using (var sha = SHA1.Create())
			{
				var w = new BinaryWriter(new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write));
				switch (o)
				{
					case TerrainData data: Terrain(data, w); break;
					case Mesh mesh: Geometry(mesh, w); break;
					default: Serialized(o, w); break;
				}
				w.Flush();
				((CryptoStream)w.BaseStream).FlushFinalBlock();
				return BitConverter.ToString(sha.Hash, 0, 8).Replace("-", string.Empty);
			}
		}

		private static void Terrain(TerrainData d, BinaryWriter w)
		{
			int res = d.heightmapResolution;
			w.Write(res);
			w.Write(d.size.x); w.Write(d.size.y); w.Write(d.size.z);
			foreach (float h in d.GetHeights(0, 0, res, res)) w.Write(h);
			w.Write(d.alphamapWidth); w.Write(d.alphamapHeight); w.Write(d.alphamapLayers);
			foreach (float a in d.GetAlphamaps(0, 0, d.alphamapWidth, d.alphamapHeight)) w.Write(a);
			foreach (TerrainLayer layer in d.terrainLayers) w.Write(layer != null ? AssetDatabase.GetAssetPath(layer) + "/" + layer.name : "null");
			foreach (bool hole in d.GetHoles(0, 0, d.holesResolution, d.holesResolution)) w.Write(hole);
			w.Write(d.detailResolution); w.Write(d.detailResolutionPerPatch);
			DetailPrototype[] details = d.detailPrototypes;
			w.Write(details.Length);
			for (int l = 0; l < details.Length; l++)
			{
				DetailPrototype p = details[l];
				w.Write(p.prototype != null ? AssetDatabase.GetAssetPath(p.prototype) : p.prototypeTexture != null ? AssetDatabase.GetAssetPath(p.prototypeTexture) : "none");
				w.Write(p.minWidth); w.Write(p.maxWidth); w.Write(p.minHeight); w.Write(p.maxHeight); w.Write(p.density); w.Write((int)p.renderMode);
				foreach (int n in d.GetDetailLayer(0, 0, d.detailWidth, d.detailHeight, l)) w.Write(n);
			}
			TreePrototype[] trees = d.treePrototypes;
			w.Write(trees.Length);
			foreach (TreePrototype p in trees) w.Write(p.prefab != null ? AssetDatabase.GetAssetPath(p.prefab) : "none");
			foreach (TreeInstance t in d.treeInstances)
			{
				w.Write(t.prototypeIndex); w.Write(t.position.x); w.Write(t.position.y); w.Write(t.position.z);
				w.Write(t.widthScale); w.Write(t.heightScale); w.Write(t.rotation); w.Write(t.color.r); w.Write(t.color.g); w.Write(t.color.b); w.Write(t.color.a);
				w.Write(t.lightmapColor.r);
			}
		}

		private static void Geometry(Mesh m, BinaryWriter w)
		{
			if (!m.isReadable)
			{
				Serialized(m, w);
				return;
			}
			foreach (Vector3 v in m.vertices) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
			foreach (Vector3 v in m.normals) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
			foreach (Vector4 v in m.tangents) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); }
			foreach (Vector2 v in m.uv) { w.Write(v.x); w.Write(v.y); }
			foreach (Color32 c in m.colors32) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
			for (int s = 0; s < m.subMeshCount; s++)
			{
				foreach (int i in m.GetIndices(s)) w.Write(i);
			}
		}

		private static void Serialized(Object o, BinaryWriter w)
		{
			var so = new SerializedObject(o);
			SerializedProperty p = so.GetIterator();
			bool enter = true;
			while (p.Next(enter))
			{
				enter = true;
				if (p.name == "m_LocalIdentfierInFile" || p.name == "m_ImageContentsHash" || p.name == "m_StreamData")
				{
					// Unity's own bookkeeping, not content.
					enter = false;
					continue;
				}
				w.Write(p.propertyPath);
				switch (p.propertyType)
				{
					case SerializedPropertyType.ObjectReference:
						// Its file and path ids are Unity's own numbering, different every cut: the referenced object is named instead.
						enter = false;
						Object r = p.objectReferenceValue;
						w.Write(r == null ? "null" : EditorUtility.IsPersistent(r) ? AssetDatabase.GetAssetPath(r) + "#" + r.name + ":" + r.GetType().Name
							: r is Component rc ? Hierarchy(rc.transform) + ":" + r.GetType().Name : r is GameObject rg ? Hierarchy(rg.transform) : r.name);
						break;
					case SerializedPropertyType.ManagedReference:
						w.Write(p.managedReferenceFullTypename ?? "null");
						break;
					case SerializedPropertyType.Integer: w.Write(p.longValue); break;
					case SerializedPropertyType.Boolean: w.Write(p.boolValue); break;
					case SerializedPropertyType.Float: w.Write(p.doubleValue); break;
					case SerializedPropertyType.String: w.Write(p.stringValue ?? string.Empty); break;
					case SerializedPropertyType.Enum: w.Write(p.enumValueIndex); break;
					case SerializedPropertyType.ArraySize: w.Write(p.intValue); break;
					case SerializedPropertyType.Character: w.Write(p.intValue); break;
					default:
						// Composite values (vectors, colours, bounds) are visited through their children.
						break;
				}
			}
		}

		private static string Hierarchy(Transform t)
		{
			var parts = new List<string>();
			for (; t != null; t = t.parent)
			{
				parts.Add(t.parent != null ? $"{t.name}[{t.GetSiblingIndex()}]" : t.name);
			}
			parts.Reverse();
			return string.Join("/", parts);
		}
	}
}
