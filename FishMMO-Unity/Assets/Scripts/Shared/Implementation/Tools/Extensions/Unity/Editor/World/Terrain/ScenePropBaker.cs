#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Writes a scene's baked props from one source (scatter, cliffs, boulders): the render set
	/// (<see cref="ScenePropSet"/>, client only, drawn instanced) and the collision set
	/// (<see cref="ScenePropCollisionSet"/>, server and client, streamed into physics near characters and the camera
	/// by <see cref="PropColliderStreamer"/>). Nothing per prop is a scene object.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Collision per prefab, exact, shared.</b> A prefab's colliders are its collision: a mesh collider's mesh as it
	/// is (the generator's rocks collide as their lowest level of detail, its trees as their trunk's own surface), boxes,
	/// capsules and spheres as meshes of the same shape. A prefab with one mesh collider on its root is used directly;
	/// several are merged once into a mesh kept with the generated art. A collidable prop is a prototype index and a
	/// transform, so a scene's collision costs no unique geometry on disk: <see cref="PropColliderStreamer"/> merges
	/// a chunk's props into one mesh when the chunk comes near a character or the camera.
	/// </para>
	/// <para>
	/// <b>Only what blocks.</b> A prop whose prefab has no collider, or which stands under <see cref="MinColliderMetres"/>
	/// on every side, has no collision: bushes and small stones are walked through, as grass and flowers are.
	/// </para>
	/// <para>
	/// <b>One source at a time.</b> Each source bakes its own sets (named by source), so re-running one replaces only
	/// its own.
	/// </para>
	/// </remarks>
	public static class ScenePropBaker
	{
		/// <summary>Chunk side props are sorted by, metres: the renderer's chunk.</summary>
		public const float ChunkMetres = 64f;

		/// <summary>A prop smaller than this on every side (its collision's box, scaled) has no collision, metres.</summary>
		public const float MinColliderMetres = 0.75f;

		/// <summary>Where the collision of a prefab with several colliders is kept, merged.</summary>
		public const string CollisionFolder = ProceduralArtCatalogue.Root + "/Collision";

		/// <summary>The source the scatter's trees and large props are baked under.</summary>
		public const string ScatterSource = "Scatter";

		/// <summary>
		/// The terrain folder of the scene being generated, set by the generator for the length of a generation: a
		/// new scene has no path until it is first saved, and without this its baked sets were kept in memory and
		/// serialized into the scene file.
		/// </summary>
		public static string BakeFolder;

		// ── Writing a source ──────────────────────────────────────────

		/// <summary>
		/// Writes <paramref name="source"/>'s props: the render set, and the collision set of those that block (their
		/// prefab's colliders, on <paramref name="collisionLayer"/>, or each collider's own layer when −1). Replaces
		/// the source's last sets; with no props, removes them. Returns the collidable props.
		/// </summary>
		public static int Write(Scene scene, string source, List<ScenePropSet.Prototype> prototypes, List<ScenePropSet.Prop> props, int collisionLayer = -1)
		{
			WriteSet(scene, source, prototypes, props);
			return WriteCollision(scene, source, prototypes, props, collisionLayer);
		}

		/// <summary>Removes <paramref name="source"/>'s render and collision sets from the scene and their assets.</summary>
		public static void Clear(Scene scene, string source)
		{
			ClearSet(scene, source);
			DeleteAsset(AssetPath(scene, $"Collision {source}"));
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == ScenePropColliders.ObjectName && root.TryGetComponent(out ScenePropColliders colliders))
				{
					colliders.Sets.RemoveAll(s => s == null || s.Source == source);
					EditorUtility.SetDirty(colliders);
				}
			}
		}

		// ── Render sets ───────────────────────────────────────────────

		/// <summary>The scene's <see cref="SceneProps"/>, made (on a client-only object at its root) when it has none.</summary>
		public static SceneProps EnsureProps(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == SceneProps.ObjectName && root.TryGetComponent(out SceneProps found))
				{
					return found;
				}
			}
			var go = new GameObject(SceneProps.ObjectName);
			SceneManager.MoveGameObjectToScene(go, scene);
			go.AddComponent<ClientOnlyObject>();
			return go.AddComponent<SceneProps>();
		}

		/// <summary>
		/// Writes <paramref name="source"/>'s render set (an asset beside the terrain when the scene has a folder, in
		/// memory otherwise) and registers it, replacing the source's last. Null, and the last removed, with no props.
		/// </summary>
		public static ScenePropSet WriteSet(Scene scene, string source, List<ScenePropSet.Prototype> prototypes, List<ScenePropSet.Prop> props)
		{
			string path = AssetPath(scene, $"Props {source}");
			if (props == null || props.Count == 0)
			{
				ClearSet(scene, source);
				return null;
			}
			SceneProps owner = EnsureProps(scene);
			// Sorted by chunk, so a chunk's props are contiguous for the renderer and the file reads in order.
			props.Sort((a, b) => ChunkKey(a.Position, ChunkMetres).CompareTo(ChunkKey(b.Position, ChunkMetres)));
			ScenePropSet set = ScriptableObject.CreateInstance<ScenePropSet>();
			set.name = $"{scene.name} Props {source}";
			set.Source = source;
			set.Prototypes = prototypes.ToArray();
			set.Props = props.ToArray();
			set = Store(set, path);
			owner.Put(set);
			EditorUtility.SetDirty(owner);
			EditorSceneManager.MarkSceneDirty(scene);
			return set;
		}

		/// <summary>Removes <paramref name="source"/>'s render set from the scene and its asset (making nothing where there is none).</summary>
		public static void ClearSet(Scene scene, string source)
		{
			DeleteAsset(AssetPath(scene, $"Props {source}"));
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == SceneProps.ObjectName && root.TryGetComponent(out SceneProps props))
				{
					props.Sets.RemoveAll(s => s == null || s.Source == source);
					EditorUtility.SetDirty(props);
				}
			}
		}

		// ── Collision sets ────────────────────────────────────────────

		/// <summary>The scene's <see cref="ScenePropColliders"/>, made (at its root, kept by server builds) when it has none.</summary>
		public static ScenePropColliders EnsureColliders(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == ScenePropColliders.ObjectName && root.TryGetComponent(out ScenePropColliders found))
				{
					return found;
				}
			}
			var go = new GameObject(ScenePropColliders.ObjectName);
			SceneManager.MoveGameObjectToScene(go, scene);
			return go.AddComponent<ScenePropColliders>();
		}

		/// <summary>
		/// Writes <paramref name="source"/>'s collision set: each prototype's shared collision mesh, and every prop big
		/// enough to block. Returns the collidable props.
		/// </summary>
		public static int WriteCollision(Scene scene, string source, List<ScenePropSet.Prototype> prototypes, List<ScenePropSet.Prop> props, int collisionLayer)
		{
			string path = AssetPath(scene, $"Collision {source}");
			// The merged chunk colliders an earlier bake left in the scene.
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == $"{source} Collision")
				{
					Object.DestroyImmediate(root);
				}
			}
			DeleteAsset(AssetPath(scene, $"{source} Collision"));

			var kinds = new ScenePropCollisionSet.Prototype[prototypes != null ? prototypes.Count : 0];
			var sizes = new Vector3[kinds.Length];
			for (int p = 0; p < kinds.Length; p++)
			{
				Mesh mesh = PrototypeCollision(prototypes[p].Prefab, out int layer);
				kinds[p] = new ScenePropCollisionSet.Prototype { Mesh = mesh, Layer = collisionLayer >= 0 ? collisionLayer : layer };
				sizes[p] = mesh != null ? mesh.bounds.size : Vector3.zero;
			}
			var instances = new List<ScenePropCollisionSet.Instance>();
			if (props != null)
			{
				foreach (ScenePropSet.Prop prop in props)
				{
					if (prop.Prototype < 0 || prop.Prototype >= kinds.Length || kinds[prop.Prototype].Mesh == null)
					{
						continue;
					}
					Vector3 size = Vector3.Scale(sizes[prop.Prototype], prop.Scale);
					if (Mathf.Max(Mathf.Abs(size.x), Mathf.Max(Mathf.Abs(size.y), Mathf.Abs(size.z))) < MinColliderMetres)
					{
						continue;   // too small to block: walked through
					}
					instances.Add(new ScenePropCollisionSet.Instance { Prototype = prop.Prototype, Position = prop.Position, Rotation = prop.Rotation, Scale = prop.Scale });
				}
			}
			ScenePropColliders owner = null;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == ScenePropColliders.ObjectName && root.TryGetComponent(out ScenePropColliders found))
				{
					owner = found;
				}
			}
			if (instances.Count == 0)
			{
				DeleteAsset(path);
				if (owner != null)
				{
					owner.Sets.RemoveAll(s => s == null || s.Source == source);
					EditorUtility.SetDirty(owner);
				}
				return 0;
			}
			owner ??= EnsureColliders(scene);
			instances.Sort((a, b) => ChunkKey(a.Position, PropColliderStreamer.CellMetres).CompareTo(ChunkKey(b.Position, PropColliderStreamer.CellMetres)));
			ScenePropCollisionSet set = ScriptableObject.CreateInstance<ScenePropCollisionSet>();
			set.name = $"{scene.name} Collision {source}";
			set.Source = source;
			set.Prototypes = kinds;
			set.Instances = instances.ToArray();
			set = Store(set, path);
			owner.Put(set);
			EditorUtility.SetDirty(owner);
			EditorSceneManager.MarkSceneDirty(scene);
			return instances.Count;
		}

		/// <summary>
		/// A prefab's collision mesh, exact: a single mesh collider on its root is its mesh as it is; otherwise every
		/// collider on it (enabled, not a trigger) in the prefab's own space, merged and kept in <see cref="CollisionFolder"/>,
		/// reused while the prefab's colliders are unchanged and overwritten in place (keeping every scene's reference)
		/// when they change. Null for a prefab with none; <paramref name="layer"/> is its first collider's layer.
		/// </summary>
		public static Mesh PrototypeCollision(GameObject prefab, out int layer)
		{
			layer = 0;
			if (prefab == null)
			{
				return null;
			}
			var pieces = new List<(Mesh mesh, Matrix4x4 matrix)>();
			bool first = true;
			Transform root = prefab.transform;
			foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
			{
				if (collider == null || collider.isTrigger || !collider.enabled)
				{
					continue;
				}
				if (first)
				{
					layer = collider.gameObject.layer;
					first = false;
				}
				Matrix4x4 local = LocalToRoot(root, collider.transform);
				Mesh mesh;
				Matrix4x4 shape;
				switch (collider)
				{
					case MeshCollider m when m.sharedMesh != null:
						mesh = m.sharedMesh;
						shape = Matrix4x4.identity;
						break;
					case BoxCollider b:
						mesh = BoxMesh();
						shape = Matrix4x4.TRS(b.center, Quaternion.identity, b.size);
						break;
					case SphereCollider s:
						mesh = CapsuleMesh(0.5f, 1f);
						shape = Matrix4x4.TRS(s.center, Quaternion.identity, Vector3.one * (2f * s.radius));
						break;
					case CapsuleCollider c:
						mesh = CapsuleMesh(c.radius, Mathf.Max(c.height, 2f * c.radius));
						shape = Matrix4x4.TRS(c.center, c.direction == 0 ? Quaternion.Euler(0f, 0f, 90f) : c.direction == 2 ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity, Vector3.one);
						break;
					default:
						continue;
				}
				pieces.Add((mesh, local * shape));
			}
			if (pieces.Count == 0)
			{
				return null;
			}
			/* A prefab that collides as one mesh on its root (the generator's rocks and trunks: their own collision
			 * level) is used as it is: the exact shape, nothing written. The streamer merges these per chunk. */
			if (pieces.Count == 1 && pieces[0].matrix == Matrix4x4.identity && AssetDatabase.Contains(pieces[0].mesh) && pieces[0].mesh.isReadable)
			{
				return pieces[0].mesh;
			}
			string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
			string path = $"{CollisionFolder}/{WorldEditorAssets.Sanitize(prefab.name)} {(guid.Length >= 8 ? guid.Substring(0, 8) : "local")}.asset";
			Mesh merged = Merge(pieces, $"{prefab.name} Collision");
			if (string.IsNullOrEmpty(guid))
			{
				return merged;   // an in-memory prefab (tests): nothing to keep
			}
			/* One asset per prefab at a fixed path, shared by every scene: unchanged, it is reused; changed, it is
			 * overwritten in place, so it keeps its identity and every scene's reference to it survives. Deleting and
			 * recreating it broke the references of every set already written with it. */
			var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
			if (existing != null)
			{
				if (!SameGeometry(existing, merged))
				{
					string keep = existing.name;
					EditorUtility.CopySerialized(merged, existing);
					existing.name = keep;
					EditorUtility.SetDirty(existing);
				}
				Object.DestroyImmediate(merged);
				return existing;
			}
			if (!AssetDatabase.IsValidFolder(CollisionFolder))
			{
				System.IO.Directory.CreateDirectory(CollisionFolder);
				AssetDatabase.Refresh();
			}
			/* With the GUID its path gives it on every machine, like all generated art: the art is rebuilt locally, and a
			 * scene's collision sets reference these meshes by GUID. A mesh's main object ID is fixed (4300000). */
			var problems = new List<string>();
			Mesh created = ProceduralArtPayload.CreateNative(merged, path, 4300000, problems);
			foreach (string problem in problems)
			{
				Debug.LogWarning($"[Props] {problem}");
			}
			return created;
		}

		private static bool SameGeometry(Mesh a, Mesh b)
		{
			if (a.vertexCount != b.vertexCount || a.GetIndexCount(0) != b.GetIndexCount(0))
			{
				return false;
			}
			Vector3[] va = a.vertices, vb = b.vertices;
			for (int i = 0; i < va.Length; i++)
			{
				if ((va[i] - vb[i]).sqrMagnitude > 1e-8f)
				{
					return false;
				}
			}
			int[] ta = a.GetTriangles(0), tb = b.GetTriangles(0);
			for (int i = 0; i < ta.Length; i++)
			{
				if (ta[i] != tb[i])
				{
					return false;
				}
			}
			return true;
		}

		// ── Terrain trees ─────────────────────────────────────────────

		/// <summary>
		/// Turns every tile's terrain trees (what the scatter placed through the tree channel: trees, bushes and
		/// large props) into the scene's "Scatter" props, then empties the tiles' tree instances: the terrain neither
		/// draws nor collides with them, and nothing about them is a scene object. Each prop keeps its tree's place,
		/// turn and scale and draws on the layer the terrain drew it on; it collides on the terrain's layer, as the
		/// terrain's tree colliders did. What the instanced path cannot draw (Tree Creator / SpeedTree trees,
		/// billboards, skinned meshes) stays on the terrain. Returns the props baked.
		/// </summary>
		public static int BakeTerrainTrees(Scene scene, IReadOnlyList<Terrain> tiles, List<string> notes)
		{
			var prototypes = new List<ScenePropSet.Prototype>();
			var prototypeOf = new Dictionary<(GameObject, int), int>();
			var props = new List<ScenePropSet.Prop>();
			int skipped = 0, kept = 0, collisionLayer = -1;
			var instancable = new Dictionary<GameObject, bool>();
			foreach (Terrain terrain in tiles)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null)
				{
					continue;
				}
				collisionLayer = terrain.gameObject.layer;
				TreePrototype[] kinds = data.treePrototypes;
				TreeInstance[] trees = data.treeInstances;
				Vector3 origin = terrain.transform.position, size = data.size;
				int layer = terrain.preserveTreePrototypeLayers ? -1 : terrain.gameObject.layer;
				var stay = new List<TreeInstance>();
				foreach (TreeInstance tree in trees)
				{
					GameObject prefab = tree.prototypeIndex >= 0 && tree.prototypeIndex < kinds.Length ? kinds[tree.prototypeIndex].prefab : null;
					if (prefab == null)
					{
						skipped++;
						continue;
					}
					if (!instancable.TryGetValue(prefab, out bool ok))
					{
						instancable[prefab] = ok = prefab.GetComponentInChildren<Tree>(true) == null && prefab.GetComponentInChildren<BillboardRenderer>(true) == null
							&& prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) == null;
					}
					if (!ok)
					{
						stay.Add(tree);
						kept++;
						continue;
					}
					if (!prototypeOf.TryGetValue((prefab, layer), out int prototype))
					{
						prototype = prototypes.Count;
						prototypeOf[(prefab, layer)] = prototype;
						prototypes.Add(new ScenePropSet.Prototype { Prefab = prefab, Layer = layer });
					}
					props.Add(new ScenePropSet.Prop
					{
						Prototype = prototype,
						Position = origin + Vector3.Scale(tree.position, size),
						Rotation = Quaternion.AngleAxis(tree.rotation * Mathf.Rad2Deg, Vector3.up),
						Scale = new Vector3(tree.widthScale, tree.heightScale, tree.widthScale),
					});
				}
				if (trees.Length > 0 || kinds.Length > 0)
				{
					/* Only the prototypes the kept trees still use: the terrain data lists its prototypes' prefabs, so a
					 * baked tree's prefab left on the list would carry its meshes and textures into the server build
					 * (ServerTerrainStripper copies the prototypes over) though nothing on the terrain draws it. */
					var used = new Dictionary<int, int>();
					var keptKinds = new List<TreePrototype>();
					for (int i = 0; i < stay.Count; i++)
					{
						TreeInstance tree = stay[i];
						if (!used.TryGetValue(tree.prototypeIndex, out int index))
						{
							used[tree.prototypeIndex] = index = keptKinds.Count;
							keptKinds.Add(kinds[tree.prototypeIndex]);
						}
						tree.prototypeIndex = index;
						stay[i] = tree;
					}
					data.treeInstances = new TreeInstance[0];
					data.treePrototypes = keptKinds.ToArray();
					data.treeInstances = stay.ToArray();
					terrain.Flush();
					EditorUtility.SetDirty(data);
					EditorUtility.SetDirty(terrain);
				}
			}
			int collidable = Write(scene, ScatterSource, prototypes, props, collisionLayer);
			notes?.Add($"Props: {props.Count:N0} trees and large props baked as instanced data in {prototypes.Count} prototype(s), {collidable:N0} of them collidable (streamed near characters){(skipped > 0 ? $"; {skipped:N0} skipped (no prefab)" : string.Empty)}{(kept > 0 ? $"; {kept:N0} left on the terrain (a Tree Creator, SpeedTree, billboard or skinned prefab the instanced path cannot draw)" : string.Empty)}.");
			return props.Count;
		}

		// ── Paths ─────────────────────────────────────────────────────

		/// <summary>
		/// Where a scene's baked asset named <paramref name="what"/> goes: its terrain folder (<see cref="BakeFolder"/>
		/// while it is generated); null for an unsaved scene outside a generation, or one with no terrain folder.
		/// </summary>
		public static string AssetPath(Scene scene, string what)
		{
			if (!string.IsNullOrEmpty(BakeFolder) && AssetDatabase.IsValidFolder(BakeFolder))
			{
				string folderName = System.IO.Path.GetFileName(BakeFolder.TrimEnd('/'));
				string baseName = folderName.EndsWith(" Terrain") ? folderName.Substring(0, folderName.Length - " Terrain".Length) : folderName;
				return $"{BakeFolder.TrimEnd('/')}/{baseName} {what}.asset";
			}
			if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
			{
				return null;
			}
			string folder = System.IO.Path.GetDirectoryName(scene.path)?.Replace('\\', '/');
			string name = System.IO.Path.GetFileNameWithoutExtension(scene.path);
			string terrain = $"{folder}/{name} Terrain";
			return AssetDatabase.IsValidFolder(terrain) ? $"{terrain}/{name} {what}.asset" : null;
		}

		/// <summary>
		/// Keeps a freshly built set at <paramref name="path"/>: copied into the asset already there, so every reference
		/// to it survives a rebake (the scene's, and the saved scene's when a repaint is discarded: a deleted and
		/// recreated asset gets a new GUID and leaves that scene pointing at nothing); created when there is none.
		/// Returns the object to reference. In memory when the scene has no folder.
		/// </summary>
		private static T Store<T>(T fresh, string path) where T : ScriptableObject
		{
			if (path == null)
			{
				return fresh;
			}
			var existing = AssetDatabase.LoadAssetAtPath<T>(path);
			if (existing != null)
			{
				EditorUtility.CopySerialized(fresh, existing);
				Object.DestroyImmediate(fresh);
				EditorUtility.SetDirty(existing);
				return existing;
			}
			DeleteAsset(path);   // something else under the name
			AssetDatabase.CreateAsset(fresh, path);
			return fresh;
		}

		private static void DeleteAsset(string path)
		{
			if (path != null && AssetDatabase.LoadMainAssetAtPath(path) != null)
			{
				AssetDatabase.DeleteAsset(path);
			}
		}

		private static long ChunkKey(Vector3 position, float side)
		{
			int cx = Mathf.FloorToInt(position.x / side), cz = Mathf.FloorToInt(position.z / side);
			return ((long)cx << 32) | (uint)cz;
		}

		private static Matrix4x4 LocalToRoot(Transform root, Transform part)
		{
			Matrix4x4 m = Matrix4x4.identity;
			for (Transform t = part; t != root; t = t.parent)
			{
				if (t == null)
				{
					return root.worldToLocalMatrix * part.localToWorldMatrix;
				}
				m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * m;
			}
			return m;
		}

		// ── Collision meshes ──────────────────────────────────────────

		/// <summary>One mesh from transformed pieces: positions and triangles only, 16-bit indices when they fit.</summary>
		private static Mesh Merge(List<(Mesh mesh, Matrix4x4 matrix)> pieces, string name)
		{
			var vertices = new List<Vector3>();
			var triangles = new List<int>();
			foreach ((Mesh mesh, Matrix4x4 matrix) in pieces)
			{
				int start = vertices.Count;
				foreach (Vector3 v in mesh.vertices)
				{
					vertices.Add(matrix.MultiplyPoint3x4(v));
				}
				// A mirrored piece (negative scale) turns its triangles over; keep them facing out.
				bool flip = matrix.determinant < 0f;
				int[] own = mesh.GetTriangles(0);
				for (int t = 0; t + 2 < own.Length; t += 3)
				{
					triangles.Add(start + own[t]);
					triangles.Add(start + (flip ? own[t + 2] : own[t + 1]));
					triangles.Add(start + (flip ? own[t + 1] : own[t + 2]));
				}
			}
			var result = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
			result.SetVertices(vertices);
			result.SetTriangles(triangles, 0);
			result.RecalculateBounds();
			return result;
		}

		private static Mesh box;
		private static readonly Dictionary<(float, float), Mesh> capsules = new Dictionary<(float, float), Mesh>();

		/// <summary>A unit box (side 1), centred: a low mesh for colliders only.</summary>
		private static Mesh BoxMesh()
		{
			if (box != null)
			{
				return box;
			}
			var v = new List<Vector3>();
			for (int i = 0; i < 8; i++)
			{
				v.Add(new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f));
			}
			int[] t =
			{
				0, 2, 1, 1, 2, 3,   4, 5, 6, 5, 7, 6,   0, 1, 4, 1, 5, 4,
				2, 6, 3, 3, 6, 7,   0, 4, 2, 2, 4, 6,   1, 3, 5, 3, 7, 5,
			};
			box = new Mesh { name = "Collision Box", hideFlags = HideFlags.DontSave };
			box.SetVertices(v);
			box.SetTriangles(t, 0);
			box.RecalculateBounds();
			return box;
		}

		/// <summary>A capsule along y of <paramref name="radius"/> and total <paramref name="height"/> (a sphere when they meet): 8 sides, two rings per cap.</summary>
		private static Mesh CapsuleMesh(float radius, float height)
		{
			var key = (Mathf.Round(radius * 1000f) / 1000f, Mathf.Round(height * 1000f) / 1000f);
			if (capsules.TryGetValue(key, out Mesh known) && known != null)
			{
				return known;
			}
			radius = key.Item1;
			height = key.Item2;
			const int sides = 8, capRings = 2;
			float half = Mathf.Max(0f, 0.5f * height - radius);
			var v = new List<Vector3> { new Vector3(0f, -half - radius, 0f) };
			var rings = new List<(float y, float r)>();
			for (int k = 1; k <= capRings; k++)
			{
				float a = Mathf.PI * 0.5f * (1f - k / (float)(capRings + 1));
				rings.Add((-half - radius * Mathf.Sin(a), radius * Mathf.Cos(a)));
			}
			rings.Add((-half, radius));
			rings.Add((half, radius));
			for (int k = capRings; k >= 1; k--)
			{
				float a = Mathf.PI * 0.5f * (1f - k / (float)(capRings + 1));
				rings.Add((half + radius * Mathf.Sin(a), radius * Mathf.Cos(a)));
			}
			foreach ((float y, float r) in rings)
			{
				for (int s = 0; s < sides; s++)
				{
					float a = s * Mathf.PI * 2f / sides;
					v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
				}
			}
			v.Add(new Vector3(0f, half + radius, 0f));
			var t = new List<int>();
			int top = v.Count - 1;
			for (int s = 0; s < sides; s++)
			{
				t.Add(0); t.Add(1 + s); t.Add(1 + (s + 1) % sides);
			}
			for (int ring = 0; ring + 1 < rings.Count; ring++)
			{
				int a0 = 1 + ring * sides, b0 = a0 + sides;
				for (int s = 0; s < sides; s++)
				{
					int s1 = (s + 1) % sides;
					t.Add(a0 + s); t.Add(b0 + s); t.Add(b0 + s1);
					t.Add(a0 + s); t.Add(b0 + s1); t.Add(a0 + s1);
				}
			}
			int last = 1 + (rings.Count - 1) * sides;
			for (int s = 0; s < sides; s++)
			{
				t.Add(top); t.Add(last + (s + 1) % sides); t.Add(last + s);
			}
			var mesh = new Mesh { name = "Collision Capsule", hideFlags = HideFlags.DontSave };
			mesh.SetVertices(v);
			mesh.SetTriangles(t, 0);
			mesh.RecalculateBounds();
			capsules[key] = mesh;
			return mesh;
		}
	}
}
#endif
