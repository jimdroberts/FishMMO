using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Scene view tool for the generated props (structures, cliffs, boulders, trees), which are data, not objects, and so
	/// cannot be clicked: pick one, then move, turn or scale it with handles or exact numbers, remove it, or restore it to
	/// where the generator put it. Every change is a hand edit (<see cref="ScenePropEdits"/>), undoable, written into the
	/// prop's set and collision at once and laid over every later bake, so a regenerate keeps it.
	/// </summary>
	/// <remarks>
	/// A structure's doors (their objects under its site), the rooms it darkens and the smoke and light it gives off
	/// (the scene's points-of-interest asset) move or go with it at once; the ground dug under its floor follows on the
	/// next POI regenerate, which lays the edit as the piece is made. Rebake the NavMesh after moving anything that blocks.
	/// </remarks>
	[EditorTool("Edit Generated Props")]
	public sealed class ScenePropEditTool : EditorTool
	{
		private enum Mode { Move, Rotate, Scale }

		/// <summary>A prop's drawn shape in its prefab's own space: the box picking tests first, and the meshes it then tests.</summary>
		private sealed class Shape
		{
			public Bounds Bounds;
			public float Radius;
			public readonly List<(Mesh Mesh, Matrix4x4 Local)> Parts = new List<(Mesh, Matrix4x4)>();
		}

		private static readonly Dictionary<GameObject, Shape> shapes = new Dictionary<GameObject, Shape>();
		private static readonly Dictionary<Mesh, (Vector3[] Vertices, int[] Triangles)> triangles = new Dictionary<Mesh, (Vector3[], int[])>();

		private static ScenePropSet pickedSet;
		private static int pickedIndex = -1;
		private static Mode mode;
		private static bool dragging;
		private static Vector3 pendingPosition, pendingScale;
		private static Quaternion pendingRotation;
		private static string sourceFilter = string.Empty;
		private static string status = string.Empty;
		private static Vector2 panelScroll;

		private GUIContent icon;

		public override GUIContent toolbarIcon => icon ??= new GUIContent(EditorGUIUtility.IconContent("d_Prefab Icon").image,
			"Edit Generated Props: pick a generated structure, cliff, boulder or tree and move, turn, scale, remove or restore it.");

		public override void OnWillBeDeactivated()
		{
			Deselect();
		}

		private static bool HasPick => pickedSet != null && pickedIndex >= 0 && pickedIndex < pickedSet.Props.Length;

		private static void Deselect()
		{
			pickedSet = null;
			pickedIndex = -1;
			dragging = false;
		}

		public override void OnToolGUI(EditorWindow window)
		{
			if (!(window is SceneView view))
			{
				return;
			}
			Event e = Event.current;
			int control = GUIUtility.GetControlID(FocusType.Passive);
			if (e.type == EventType.Layout)
			{
				HandleUtility.AddDefaultControl(control);
			}
			if (!HasPick)
			{
				Deselect();
			}
			// A handle let go: the placement it was dragged to becomes the edit.
			if (dragging && GUIUtility.hotControl == 0)
			{
				dragging = false;
				if (HasPick)
				{
					Commit(pendingPosition, pendingRotation, pendingScale, false);
				}
			}
			if (HasPick)
			{
				DrawHandles();
			}
			Panel(view);
			if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && HandleUtility.nearestControl == control)
			{
				Pick(HandleUtility.GUIPointToWorldRay(e.mousePosition));
				e.Use();
			}
			if (e.type == EventType.KeyDown && GUIUtility.keyboardControl == 0)
			{
				switch (e.keyCode)
				{
					case KeyCode.W: mode = Mode.Move; e.Use(); break;
					case KeyCode.E: mode = Mode.Rotate; e.Use(); break;
					case KeyCode.R: mode = Mode.Scale; e.Use(); break;
					case KeyCode.Escape when HasPick: Deselect(); e.Use(); break;
					case KeyCode.F when HasPick: view.Frame(WorldBounds(pickedSet.Props[pickedIndex]), false); e.Use(); break;
					case KeyCode.Delete when HasPick:
					case KeyCode.Backspace when HasPick:
						Commit(default, default, default, true);
						e.Use();
						break;
				}
			}
		}

		// ── Picking ───────────────────────────────────────────────────

		/// <summary>The generated prop under the ray: the nearest whose box it enters, then the nearest whose triangles it hits.</summary>
		private static void Pick(Ray ray)
		{
			var candidates = new List<(float Distance, ScenePropSet Set, int Index)>();
			foreach (SceneProps props in Object.FindObjectsByType<SceneProps>())
			{
				if (props == null || !props.gameObject.scene.isLoaded)
				{
					continue;
				}
				foreach (ScenePropSet set in props.Sets)
				{
					if (set == null || (sourceFilter.Length > 0 && set.Source != sourceFilter))
					{
						continue;
					}
					var setShapes = new Shape[set.Prototypes.Length];
					for (int p = 0; p < setShapes.Length; p++)
					{
						setShapes[p] = ShapeOf(set.Prototypes[p].Prefab);
					}
					for (int i = 0; i < set.Props.Length; i++)
					{
						ScenePropSet.Prop prop = set.Props[i];
						Shape shape = prop.Prototype >= 0 && prop.Prototype < setShapes.Length ? setShapes[prop.Prototype] : null;
						if (shape == null)
						{
							continue;
						}
						// A sphere round the prop first: the ray's nearest pass of it.
						Vector3 toProp = prop.Position - ray.origin;
						float along = Vector3.Dot(toProp, ray.direction);
						float reach = shape.Radius * Mathf.Max(Mathf.Abs(prop.Scale.x), Mathf.Max(Mathf.Abs(prop.Scale.y), Mathf.Abs(prop.Scale.z)));
						if (along < -reach || (toProp - ray.direction * along).sqrMagnitude > reach * reach)
						{
							continue;
						}
						Matrix4x4 m = prop.Matrix;
						float d = ScenePropEditMath.RayBoxDistance(ray.origin, ray.direction, m.inverse, m, shape.Bounds);
						if (!float.IsPositiveInfinity(d))
						{
							candidates.Add((d, set, i));
						}
					}
				}
			}
			candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
			float best = float.PositiveInfinity;
			ScenePropSet bestSet = null;
			int bestIndex = -1;
			for (int c = 0; c < candidates.Count && c < 24; c++)
			{
				(float boxDistance, ScenePropSet set, int index) = candidates[c];
				if (boxDistance >= best)
				{
					break;
				}
				ScenePropSet.Prop prop = set.Props[index];
				float hit = MeshDistance(ray, prop.Matrix, ShapeOf(set.Prototypes[prop.Prototype].Prefab));
				if (hit < best)
				{
					best = hit;
					bestSet = set;
					bestIndex = index;
				}
			}
			if (bestSet == null && candidates.Count > 0)
			{
				// No readable mesh to test: the nearest box.
				(_, bestSet, bestIndex) = candidates[0];
			}
			pickedSet = bestSet;
			pickedIndex = bestIndex;
			status = bestSet == null ? "Nothing generated under the cursor." : string.Empty;
			SceneView.RepaintAll();
		}

		/// <summary>The distance along the ray to the prop's own triangles, +∞ when it misses them (or they cannot be read).</summary>
		private static float MeshDistance(Ray ray, Matrix4x4 prop, Shape shape)
		{
			float best = float.PositiveInfinity;
			foreach ((Mesh mesh, Matrix4x4 local) in shape.Parts)
			{
				if (!TrianglesOf(mesh, out Vector3[] v, out int[] t))
				{
					continue;
				}
				Matrix4x4 toWorld = prop * local, toLocal = toWorld.inverse;
				Vector3 o = toLocal.MultiplyPoint3x4(ray.origin), d = toLocal.MultiplyVector(ray.direction);
				for (int k = 0; k + 2 < t.Length; k += 3)
				{
					float along = ScenePropEditMath.RayTriangle(o, d, v[t[k]], v[t[k + 1]], v[t[k + 2]]);
					if (!float.IsPositiveInfinity(along))
					{
						best = Mathf.Min(best, Vector3.Distance(ray.origin, toWorld.MultiplyPoint3x4(o + d * along)));
					}
				}
			}
			return best;
		}

		private static bool TrianglesOf(Mesh mesh, out Vector3[] vertices, out int[] tris)
		{
			if (!triangles.TryGetValue(mesh, out var cached))
			{
				cached = mesh != null && mesh.isReadable ? (mesh.vertices, mesh.triangles) : (null, null);
				triangles[mesh] = cached;
			}
			vertices = cached.Vertices;
			tris = cached.Triangles;
			return vertices != null && tris != null;
		}

		/// <summary>A prefab's drawn shape at its first level of detail, in its own space (cached).</summary>
		private static Shape ShapeOf(GameObject prefab)
		{
			if (prefab == null)
			{
				return null;
			}
			if (shapes.TryGetValue(prefab, out Shape shape))
			{
				return shape;
			}
			shape = new Shape();
			Renderer[] renderers;
			LODGroup group = prefab.GetComponentInChildren<LODGroup>();
			LOD[] lods = group != null ? group.GetLODs() : null;
			renderers = lods != null && lods.Length > 0 ? lods[0].renderers : prefab.GetComponentsInChildren<Renderer>();
			Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;
			bool any = false;
			foreach (Renderer r in renderers)
			{
				if (r == null || !r.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
				{
					continue;
				}
				Matrix4x4 local = rootInverse * r.transform.localToWorldMatrix;
				shape.Parts.Add((filter.sharedMesh, local));
				Bounds b = filter.sharedMesh.bounds;
				for (int c = 0; c < 8; c++)
				{
					Vector3 corner = local.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) != 0 ? 1 : -1, (c & 2) != 0 ? 1 : -1, (c & 4) != 0 ? 1 : -1)));
					if (!any)
					{
						shape.Bounds = new Bounds(corner, Vector3.zero);
						any = true;
					}
					else
					{
						shape.Bounds.Encapsulate(corner);
					}
				}
			}
			if (!any)
			{
				shape = null;
			}
			else
			{
				shape.Radius = shape.Bounds.center.magnitude + shape.Bounds.extents.magnitude;
			}
			shapes[prefab] = shape;
			return shape;
		}

		private static Bounds WorldBounds(ScenePropSet.Prop prop)
		{
			Shape shape = ShapeOf(pickedSet.Prototypes[prop.Prototype].Prefab);
			Bounds local = shape != null ? shape.Bounds : new Bounds(Vector3.zero, Vector3.one);
			Matrix4x4 m = prop.Matrix;
			var world = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
			for (int c = 0; c < 8; c++)
			{
				world.Encapsulate(m.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3((c & 1) != 0 ? 1 : -1, (c & 2) != 0 ? 1 : -1, (c & 4) != 0 ? 1 : -1))));
			}
			return world;
		}

		// ── Handles ───────────────────────────────────────────────────

		private static void DrawHandles()
		{
			ScenePropSet.Prop prop = pickedSet.Props[pickedIndex];
			Vector3 position = dragging ? pendingPosition : prop.Position;
			Quaternion rotation = dragging ? pendingRotation : prop.Rotation;
			Vector3 scale = dragging ? pendingScale : prop.Scale;
			Shape shape = ShapeOf(pickedSet.Prototypes[prop.Prototype].Prefab);
			if (shape != null)
			{
				Matrix4x4 was = Handles.matrix;
				Handles.color = dragging ? new Color(1f, 0.85f, 0.2f) : new Color(0.3f, 0.9f, 1f);
				Handles.matrix = Matrix4x4.TRS(position, rotation, scale);
				Handles.DrawWireCube(shape.Bounds.center, shape.Bounds.size);
				if (dragging)
				{
					// Where it stands now, while it is dragged.
					Handles.color = new Color(1f, 1f, 1f, 0.35f);
					Handles.matrix = prop.Matrix;
					Handles.DrawWireCube(shape.Bounds.center, shape.Bounds.size);
				}
				Handles.matrix = was;
				if (dragging && Event.current.type == EventType.Repaint)
				{
					DrawGhost(pickedSet.Prototypes[prop.Prototype], Matrix4x4.TRS(position, rotation, scale), shape);
				}
			}
			EditorGUI.BeginChangeCheck();
			Vector3 newPosition = position;
			Quaternion newRotation = rotation;
			Vector3 newScale = scale;
			Quaternion handleFrame = Tools.pivotRotation == PivotRotation.Local ? rotation : Quaternion.identity;
			switch (mode)
			{
				case Mode.Move: newPosition = Handles.PositionHandle(position, handleFrame); break;
				case Mode.Rotate: newRotation = Handles.RotationHandle(rotation, position); break;
				case Mode.Scale: newScale = Handles.ScaleHandle(scale, position, rotation); break;
			}
			if (EditorGUI.EndChangeCheck())
			{
				pendingPosition = newPosition;
				pendingRotation = newRotation;
				pendingScale = newScale;
				dragging = true;
			}
		}

		/// <summary>The prop drawn where it is being dragged to (its first level, its own materials or its dressing's).</summary>
		private static void DrawGhost(ScenePropSet.Prototype prototype, Matrix4x4 at, Shape shape)
		{
			Material[] dress = prototype.Dressed ? prototype.Materials : null;
			int dressAt = 0;
			foreach ((Mesh mesh, Matrix4x4 local) in shape.Parts)
			{
				Renderer renderer = null;
				foreach (MeshFilter f in prototype.Prefab.GetComponentsInChildren<MeshFilter>())
				{
					if (f.sharedMesh == mesh && f.TryGetComponent(out renderer))
					{
						break;
					}
				}
				Material[] materials = renderer != null ? renderer.sharedMaterials : null;
				for (int s = 0; s < mesh.subMeshCount; s++)
				{
					Material material = dress != null && dressAt < dress.Length ? dress[dressAt] : materials != null && s < materials.Length ? materials[s] : null;
					dressAt++;
					if (material != null)
					{
						Graphics.DrawMesh(mesh, at * local, material, 0, SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null, s);
					}
				}
			}
		}

		// ── Panel ─────────────────────────────────────────────────────

		private static void Panel(SceneView view)
		{
			Handles.BeginGUI();
			var area = new Rect(10f, view.position.height - 330f, 360f, 300f);
			GUILayout.BeginArea(area, GUI.skin.box);
			panelScroll = GUILayout.BeginScrollView(panelScroll);
			GUILayout.Label("Edit Generated Props", EditorStyles.boldLabel);
			sourceFilter = SourcePopup(sourceFilter);
			if (!HasPick)
			{
				GUILayout.Label("Click a generated structure, cliff, boulder or tree to select it. W, E and R pick the handle; F frames; Delete removes.", EditorStyles.wordWrappedMiniLabel);
			}
			else
			{
				ScenePropSet.Prop prop = pickedSet.Props[pickedIndex];
				ScenePropSet.Prototype prototype = pickedSet.Prototypes[prop.Prototype];
				using (new EditorGUI.DisabledScope(true))
				{
					EditorGUILayout.ObjectField("Prefab", prototype.Prefab, typeof(GameObject), false);
				}
				Scene scene = SceneOf(pickedSet);
				ScenePropEdits edits = ScenePropEdits.For(scene, false);
				int at = edits != null ? ScenePropEditMath.FindByPlacement(edits.Edits, pickedSet.Source, ScenePropEdits.GuidOf(prototype.Prefab), prop.Position) : -1;
				GUILayout.Label($"{pickedSet.Source} · {(at >= 0 ? "edited by hand" : "as generated")}", EditorStyles.miniLabel);
				mode = (Mode)GUILayout.Toolbar((int)mode, new[] { "Move (W)", "Rotate (E)", "Scale (R)" });
				EditorGUI.BeginChangeCheck();
				Vector3 position = EditorGUILayout.Vector3Field("Position", prop.Position);
				Vector3 euler = EditorGUILayout.Vector3Field("Rotation", prop.Rotation.eulerAngles);
				Vector3 scale = EditorGUILayout.Vector3Field("Scale", prop.Scale);
				if (EditorGUI.EndChangeCheck())
				{
					Commit(position, Quaternion.Euler(euler), scale, false);
				}
				using (new GUILayout.HorizontalScope())
				{
					if (GUILayout.Button("Snap to ground"))
					{
						Commit(OnGround(prop.Position), prop.Rotation, prop.Scale, false);
					}
					using (new EditorGUI.DisabledScope(at < 0))
					{
						if (GUILayout.Button("Restore as generated"))
						{
							ScenePropEdits.Edit e = edits.Edits[at];
							Commit(e.Origin, e.OriginRotation, e.OriginScale, false);
						}
					}
					if (GUILayout.Button("Remove"))
					{
						Commit(default, default, default, true);
					}
				}
			}
			if (status.Length > 0)
			{
				EditorGUILayout.HelpBox(status, MessageType.Info);
			}
			GUILayout.Label("A structure's doors, rooms and smoke move with it; the ground under it is dug again on the next POI regenerate. Rebake the NavMesh after moving anything that blocks.", EditorStyles.wordWrappedMiniLabel);
			using (new GUILayout.HorizontalScope())
			{
				Scene active = SceneManager.GetActiveScene();
				ScenePropEdits sceneEdits = ScenePropEdits.For(active, false);
				using (new EditorGUI.DisabledScope(sceneEdits == null))
				{
					if (GUILayout.Button($"Edits ({(sceneEdits != null ? sceneEdits.Edits.Count : 0)})"))
					{
						Selection.activeObject = sceneEdits;
					}
				}
				if (GUILayout.Button("Rebake NavMesh"))
				{
					var notes = new List<string>();
					SceneNavMeshBaker.Bake(active, notes);
					status = notes.Count > 0 ? string.Join("\n", notes) : "NavMesh rebaked.";
				}
			}
			GUILayout.EndScrollView();
			GUILayout.EndArea();
			Handles.EndGUI();
		}

		private static string SourcePopup(string current)
		{
			var sources = new List<string> { string.Empty };
			foreach (SceneProps props in Object.FindObjectsByType<SceneProps>())
			{
				foreach (ScenePropSet set in props.Sets)
				{
					if (set != null && !sources.Contains(set.Source))
					{
						sources.Add(set.Source);
					}
				}
			}
			int index = Mathf.Max(0, sources.IndexOf(current));
			var labels = new string[sources.Count];
			for (int i = 0; i < labels.Length; i++)
			{
				labels[i] = i == 0 ? "Pick from every source" : $"Pick only {sources[i]}";
			}
			return sources[EditorGUILayout.Popup(index, labels)];
		}

		/// <summary>The ground under a point (the terrain there), or the point as it is off every terrain.</summary>
		private static Vector3 OnGround(Vector3 at)
		{
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				Vector3 origin = terrain.GetPosition();
				Vector3 size = terrain.terrainData.size;
				if (at.x >= origin.x && at.x <= origin.x + size.x && at.z >= origin.z && at.z <= origin.z + size.z)
				{
					return new Vector3(at.x, origin.y + terrain.SampleHeight(at), at.z);
				}
			}
			return at;
		}

		private static Scene SceneOf(ScenePropSet set)
		{
			foreach (SceneProps props in Object.FindObjectsByType<SceneProps>())
			{
				if (props.Sets.Contains(set))
				{
					return props.gameObject.scene;
				}
			}
			return SceneManager.GetActiveScene();
		}

		// ── Committing ────────────────────────────────────────────────

		/// <summary>
		/// The picked prop to a new placement (or removed): its edit recorded, its set and collision changed in place, and a
		/// structure's doors, rooms and emitters moved (or removed) with it. One undo step.
		/// </summary>
		private static void Commit(Vector3 position, Quaternion rotation, Vector3 scale, bool remove)
		{
			ScenePropSet set = pickedSet;
			int index = pickedIndex;
			Scene scene = SceneOf(set);
			ScenePropEdits edits = ScenePropEdits.For(scene, true);
			if (edits == null)
			{
				status = "Save the scene first: its edits are kept beside its terrain.";
				return;
			}
			ScenePropSet.Prop old = set.Props[index];
			ScenePropSet.Prototype prototype = set.Prototypes[old.Prototype];
			string guid = ScenePropEdits.GuidOf(prototype.Prefab);
			ScenePropCollisionSet collision = CollisionSetOf(scene, set.Source);
			ScenePointsOfInterest points = set.Source == PointOfInterestGenerator.PropSource ? PointsOf(scene) : null;
			var undo = new List<Object> { edits, set };
			if (collision != null) undo.Add(collision);
			if (points != null) undo.Add(points);
			Undo.RecordObjects(undo.ToArray(), remove ? "Remove generated prop" : "Edit generated prop");

			int at = ScenePropEditMath.FindByPlacement(edits.Edits, set.Source, guid, old.Position);
			ScenePropEdits.Edit edit = at >= 0 ? edits.Edits[at] : new ScenePropEdits.Edit
			{
				Source = set.Source, Prefab = prototype.Prefab, PrefabGuid = guid,
				Origin = old.Position, OriginRotation = old.Rotation, OriginScale = old.Scale, Matched = true,
			};
			edit.Removed = remove;
			edit.Position = position;
			edit.Rotation = rotation;
			edit.Scale = scale;
			bool asGenerated = !remove && (position - edit.Origin).sqrMagnitude < 1e-8f && Quaternion.Angle(rotation, edit.OriginRotation) < 1e-3f
				&& (scale - edit.OriginScale).sqrMagnitude < 1e-10f;
			if (asGenerated)
			{
				if (at >= 0) edits.Edits.RemoveAt(at);
			}
			else if (at >= 0)
			{
				edits.Edits[at] = edit;
			}
			else
			{
				edits.Edits.Add(edit);
			}

			Matrix4x4 from = old.Matrix;
			if (remove)
			{
				var kept = new List<ScenePropSet.Prop>(set.Props);
				kept.RemoveAt(index);
				set.Props = kept.ToArray();
			}
			else
			{
				set.Props[index] = new ScenePropSet.Prop { Prototype = old.Prototype, Position = position, Rotation = rotation, Scale = scale };
			}
			if (collision != null)
			{
				MoveCollision(collision, old, remove, position, rotation, scale);
			}
			int carried = 0;
			Shape shape = ShapeOf(prototype.Prefab);
			if (set.Source == PointOfInterestGenerator.PropSource && shape != null)
			{
				Matrix4x4 to = remove ? Matrix4x4.zero : Matrix4x4.TRS(position, rotation, scale);
				carried = CarryAttachments(scene, points, from, to, shape.Bounds, remove);
			}

			EditorUtility.SetDirty(edits);
			// The edits themselves are small (text) and are what every later bake reads: kept on disk at once. The sets, the
			// collision and the scene are saved with the scene, as any other change.
			AssetDatabase.SaveAssetIfDirty(edits);
			EditorUtility.SetDirty(set);
			if (collision != null) EditorUtility.SetDirty(collision);
			if (points != null) EditorUtility.SetDirty(points);
			EditorSceneManager.MarkSceneDirty(scene);
			SceneProps.NotifySetsEdited();
			status = remove ? $"Removed{(carried > 0 ? $", with {carried} door(s), room(s) and emitter(s)" : "")}."
				: asGenerated ? "Restored to where it was generated." : carried > 0 ? $"Moved, with {carried} door(s), room(s) and emitter(s)." : string.Empty;
			if (remove)
			{
				Deselect();
			}
			SceneView.RepaintAll();
		}

		/// <summary>Moves a set's prop as the tool's handles do (one undoable edit, laid over every later bake); returns what was done.</summary>
		public static string MoveProp(ScenePropSet set, int index, Vector3 position, Quaternion rotation, Vector3 scale)
		{
			pickedSet = set;
			pickedIndex = index;
			status = string.Empty;
			Commit(position, rotation, scale, false);
			return status;
		}

		/// <summary>Removes a set's prop as the tool's Delete does; returns what was done.</summary>
		public static string RemoveProp(ScenePropSet set, int index)
		{
			pickedSet = set;
			pickedIndex = index;
			status = string.Empty;
			Commit(default, default, default, true);
			return status;
		}

		/// <summary>
		/// Forgets an edit, putting its prop back where it was generated: a moved one through <see cref="Commit"/> (its doors
		/// and emitters with it), a removed one laid again in its set and collision (its doors and emitters come back on the
		/// next POI regenerate). An unmatched edit is just dropped. Returns what was done.
		/// </summary>
		public static string Forget(ScenePropEdits edits, int index)
		{
			ScenePropEdits.Edit e = edits.Edits[index];
			Scene scene = default;
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene s = SceneManager.GetSceneAt(i);
				if (s.isLoaded && ScenePropEdits.PathFor(s) == AssetDatabase.GetAssetPath(edits))
				{
					scene = s;
				}
			}
			ScenePropSet set = null;
			if (scene.IsValid())
			{
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					if (root.TryGetComponent(out SceneProps props))
					{
						foreach (ScenePropSet candidate in props.Sets)
						{
							set = candidate != null && candidate.Source == e.Source ? candidate : set;
						}
					}
				}
			}
			if (set == null)
			{
				Undo.RecordObject(edits, "Forget prop edit");
				edits.Edits.RemoveAt(index);
				EditorUtility.SetDirty(edits);
				return "Its scene is not open: the edit is dropped, and the next bake lays the prop as generated.";
			}
			if (!e.Removed)
			{
				for (int i = 0; i < set.Props.Length; i++)
				{
					ScenePropSet.Prop prop = set.Props[i];
					if (ScenePropEdits.GuidOf(set.Prototypes[prop.Prototype].Prefab) == e.PrefabGuid && (prop.Position - e.Position).sqrMagnitude <= ScenePropEdits.MatchMetres * ScenePropEdits.MatchMetres)
					{
						pickedSet = set;
						pickedIndex = i;
						Commit(e.Origin, e.OriginRotation, e.OriginScale, false);
						return "Put back where it was generated.";
					}
				}
				Undo.RecordObject(edits, "Forget prop edit");
				edits.Edits.RemoveAt(index);
				EditorUtility.SetDirty(edits);
				return "Its prop is not in the set (unmatched): the edit is dropped.";
			}
			int prototype = -1;
			for (int p = 0; p < set.Prototypes.Length && prototype < 0; p++)
			{
				prototype = ScenePropEdits.GuidOf(set.Prototypes[p].Prefab) == e.PrefabGuid ? p : -1;
			}
			ScenePropCollisionSet collision = CollisionSetOf(scene, e.Source);
			var undo = new List<Object> { edits, set };
			if (collision != null) undo.Add(collision);
			Undo.RecordObjects(undo.ToArray(), "Forget prop edit");
			edits.Edits.RemoveAt(index);
			if (prototype >= 0)
			{
				var props = new List<ScenePropSet.Prop>(set.Props) { new ScenePropSet.Prop { Prototype = prototype, Position = e.Origin, Rotation = e.OriginRotation, Scale = e.OriginScale } };
				set.Props = props.ToArray();
				if (collision != null && prototype < collision.Prototypes.Length && collision.Prototypes[prototype].Mesh != null)
				{
					Vector3 size = Vector3.Scale(collision.Prototypes[prototype].Mesh.bounds.size, e.OriginScale);
					if (Mathf.Max(Mathf.Abs(size.x), Mathf.Max(Mathf.Abs(size.y), Mathf.Abs(size.z))) >= ScenePropBaker.MinColliderMetres)
					{
						var instances = new List<ScenePropCollisionSet.Instance>(collision.Instances)
						{
							new ScenePropCollisionSet.Instance { Prototype = prototype, Position = e.Origin, Rotation = e.OriginRotation, Scale = e.OriginScale },
						};
						collision.Instances = instances.ToArray();
						EditorUtility.SetDirty(collision);
					}
				}
			}
			EditorUtility.SetDirty(edits);
			EditorUtility.SetDirty(set);
			EditorSceneManager.MarkSceneDirty(scene);
			SceneProps.NotifySetsEdited();
			return prototype >= 0
				? $"Laid again where it was generated{(e.Source == PointOfInterestGenerator.PropSource ? "; its doors, rooms and emitters come back on the next POI regenerate" : "")}."
				: "Its prefab is no longer in the set: the edit is dropped.";
		}

		private static ScenePropCollisionSet CollisionSetOf(Scene scene, string source)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.TryGetComponent(out ScenePropColliders colliders))
				{
					foreach (ScenePropCollisionSet set in colliders.Sets)
					{
						if (set != null && set.Source == source)
						{
							return set;
						}
					}
				}
			}
			return null;
		}

		private static ScenePointsOfInterest PointsOf(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (SceneSiteEmitters emitters in root.GetComponentsInChildren<SceneSiteEmitters>(true))
				{
					if (emitters.Points != null)
					{
						return emitters.Points;
					}
				}
			}
			return null;
		}

		/// <summary>The prop's collision instance (same prototype, where it stood) moved or removed with it.</summary>
		private static void MoveCollision(ScenePropCollisionSet collision, ScenePropSet.Prop old, bool remove, Vector3 position, Quaternion rotation, Vector3 scale)
		{
			for (int i = 0; i < collision.Instances.Length; i++)
			{
				ScenePropCollisionSet.Instance c = collision.Instances[i];
				if (c.Prototype != old.Prototype || (c.Position - old.Position).sqrMagnitude > 1e-6f || Quaternion.Angle(c.Rotation, old.Rotation) > 0.01f)
				{
					continue;
				}
				if (remove)
				{
					var kept = new List<ScenePropCollisionSet.Instance>(collision.Instances);
					kept.RemoveAt(i);
					collision.Instances = kept.ToArray();
				}
				else
				{
					collision.Instances[i] = new ScenePropCollisionSet.Instance { Prototype = c.Prototype, Position = position, Rotation = rotation, Scale = scale };
				}
				return;
			}
		}

		/// <summary>
		/// A structure's attachments moved from <paramref name="from"/> to <paramref name="to"/> (or removed): its door objects
		/// under the scene's sites, its rooms and its emitters, each found inside its old box (grown a little). Returns how many.
		/// </summary>
		private static int CarryAttachments(Scene scene, ScenePointsOfInterest points, Matrix4x4 from, Matrix4x4 to, Bounds local, bool remove)
		{
			Matrix4x4 inverse = from.inverse;
			Bounds box = local;
			box.Expand(0.6f);
			bool Inside(Vector3 world) => box.Contains(inverse.MultiplyPoint3x4(world));
			Matrix4x4 delta = remove ? Matrix4x4.identity : to * inverse;
			Quaternion turn = remove ? Quaternion.identity : to.rotation * Quaternion.Inverse(from.rotation);
			int carried = 0;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (ScenePointOfInterest site in root.GetComponentsInChildren<ScenePointOfInterest>(true))
				{
					var doors = new List<Transform>();
					foreach (Transform child in site.GetComponentsInChildren<Transform>(true))
					{
						if (child != site.transform && child.name.StartsWith("Door ") && Inside(child.position))
						{
							doors.Add(child);
						}
					}
					foreach (Transform door in doors)
					{
						carried++;
						if (remove)
						{
							Undo.DestroyObjectImmediate(door.gameObject);
							continue;
						}
						Undo.RecordObject(door, "Edit generated prop");
						door.SetPositionAndRotation(delta.MultiplyPoint3x4(door.position), turn * door.rotation);
					}
				}
			}
			if (points != null)
			{
				for (int i = points.Emitters.Count - 1; i >= 0; i--)
				{
					SceneEmitter emitter = points.Emitters[i];
					if (!Inside(emitter.Position))
					{
						continue;
					}
					carried++;
					if (remove)
					{
						points.Emitters.RemoveAt(i);
						continue;
					}
					emitter.Position = delta.MultiplyPoint3x4(emitter.Position);
					points.Emitters[i] = emitter;
				}
				for (int i = points.Rooms.Count - 1; i >= 0; i--)
				{
					SceneRoom room = points.Rooms[i];
					if (!Inside(room.Centre))
					{
						continue;
					}
					carried++;
					if (remove)
					{
						points.Rooms.RemoveAt(i);
						continue;
					}
					room.Centre = delta.MultiplyPoint3x4(room.Centre);
					room.Yaw += turn.eulerAngles.y;
					points.Rooms[i] = room;
				}
			}
			return carried;
		}
	}
}
