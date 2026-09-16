using System.Collections.Generic;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
{
	/// <summary>
	/// Design-time placement of a spawner in a world scene. Does nothing at runtime.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Authoring only.</b> The server never reads this component. The spawn table baker
	/// (<c>FishMMO Dashboard → World → Spawn Tables → Rebuild Spawn Tables</c>, and automatically whenever a world scene is
	/// saved or a build starts) copies every spawner in a scene into that scene's
	/// <see cref="SceneSpawnTable"/>, and <see cref="SpawnerSystem"/> runs the table. World scenes
	/// are built into one bundle shared by clients and servers, so the spawner's GameObject is
	/// tagged <c>EditorOnly</c> and removed from every build: a client never receives what a
	/// spawner produces, where, or how often.
	/// </para>
	/// <para>
	/// It used to be a <c>NetworkBehaviour</c> that ran itself on the server and disabled itself
	/// everywhere else, which shipped every spawner's configuration to every player and spent a
	/// scene network object on each one.
	/// </para>
	/// </remarks>
	[AddComponentMenu("FishMMO/Server/Object Spawner")]
	[DisallowMultipleComponent]
	public class ObjectSpawner : MonoBehaviour
	{
		/// <summary>
		/// The tag that keeps a spawner out of every build.
		/// </summary>
		public const string EditorOnlyTag = "EditorOnly";

		/// <summary>
		/// If any of these conditions return true, the object will respawn. This list is checked first (logical OR).
		/// </summary>
		[SerializeReference, SubclassSelector]
		public List<RespawnCondition> OrConditions = new List<RespawnCondition>();

		/// <summary>
		/// All conditions must return true for the object to respawn. This list is checked second (logical AND).
		/// </summary>
		[SerializeReference, SubclassSelector]
		public List<RespawnCondition> TrueConditions = new List<RespawnCondition>();

		/// <summary>
		/// Respawn delay, in seconds, for an object that has no settings and no cadence of its own.
		/// </summary>
		public float InitialRespawnTime = 0.0f;

		/// <summary>
		/// The number of objects to spawn when the scene starts.
		/// </summary>
		public int InitialSpawnCount = 0;

		/// <summary>
		/// The maximum number of objects that can be spawned by this spawner.
		/// </summary>
		[Tooltip("The maximum number of objects that can be spawned by this spawner.")]
		public int MaxSpawnCount = 1;

		/// <summary>
		/// When true, at most one live instance of each entry in <see cref="Spawnables"/> may exist
		/// at a time.
		/// </summary>
		/// <remarks>
		/// Off by default, because the normal case is a spawner filling a zone with several of the
		/// same creature. Turn it on where each entry names a distinct individual — a zone listing
		/// several named NPCs would otherwise draw the same one repeatedly and stand two copies of
		/// it side by side, since the spawn index is chosen without regard to what is already alive.
		///
		/// This caps each entry at one, not the spawner: MaxSpawnCount still governs the total, so
		/// with this on the effective ceiling is the smaller of MaxSpawnCount and the number of
		/// assigned spawnables.
		/// </remarks>
		[Tooltip("Allow at most one live instance of each spawnable. Off by default. Turn on when every entry is a distinct individual that should never be duplicated.")]
		public bool UniqueSpawnables = false;

		/// <summary>
		/// The type of spawn selection (Linear, Random, Weighted).
		/// </summary>
		public ObjectSpawnType SpawnType = ObjectSpawnType.Linear;

		/// <summary>
		/// When true, every prefab this spawner can produce is instantiated into the object pool
		/// at scene start, up to <see cref="MaxSpawnCount"/> each.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is what makes a map's memory footprint deterministic. Without it the pool fills
		/// lazily — the first NPC of each kind is instantiated the moment a player walks into range
		/// — so a freshly loaded map hitches as it is explored and only reaches its true heap size
		/// once every spawner has fired at least once. Neither behaviour can be planned against.
		/// </para>
		/// <para>
		/// Turn it off for spawners whose prefabs are large and rarely used, where paying the cost
		/// on demand is preferable to paying it always.
		/// </para>
		/// </remarks>
		[Header("Pooling")]
		[Tooltip("Instantiate this spawner's prefabs into the pool at scene start for a fixed memory footprint.")]
		public bool PrewarmPool = true;

		/// <summary>
		/// Extra instances reserved beyond <see cref="MaxSpawnCount"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Slack, not a requirement. A corpse holds its spawner slot for the whole of its decay —
		/// the despawn is what frees the slot and starts the respawn clock, and a corpse does not
		/// reach it until the decay timer expires — so this spawner can never have more than
		/// <see cref="MaxSpawnCount"/> live instances and the reservation alone would do.
		/// </para>
		/// <para>
		/// The headroom is kept because the pool is shared: <see cref="SpawnerPool"/>
		/// de-duplicates reservations across every spawner using the same prefab, taking the
		/// largest single demand rather than the sum, so a prefab used by many spawners at once
		/// genuinely can need more instances than any one spawner reserved. This covers that
		/// without making the reservation quadratic.
		/// </para>
		/// </remarks>
		[Tooltip("Extra pooled instances beyond MaxSpawnCount, as slack for prefabs shared between spawners.")]
		[Min(0)]
		public int PrewarmHeadroom = 1;

		/// <summary>
		/// If true, a random respawn time is selected within the minimum and maximum range. Otherwise, the maximum respawn time is used.
		/// </summary>
		[Tooltip("If true a random number will be selected within the minimum and maximum range provided. Otherwise the maximum respawn time will be used.")]
		public bool RandomRespawnTime = true;

		/// <summary>
		/// Shortest delay between respawn checks, in seconds.
		/// </summary>
		[Tooltip("Shortest delay between respawn checks, in seconds. Respawn deadlines are wall-clock, so this only sets how soon after a deadline the object appears - it does not change respawn timing itself.")]
		public float RespawnCheckIntervalMinimum = 3.0f;

		/// <summary>
		/// Longest delay between respawn checks, in seconds.
		/// </summary>
		[Tooltip("Longest delay between respawn checks, in seconds. Each check picks a fresh random delay in this range so spawners do not all poll on the same frame.")]
		public float RespawnCheckIntervalMaximum = 6.0f;

		/// <summary>
		/// If true, a random spawn position is picked inside the bounding box using the current position as the center.
		/// </summary>
		[Tooltip("If true a random spawn position will be picked inside of the bounding box using the current position as the center.")]
		public bool RandomSpawnPosition = true;

		/// <summary>
		/// SphereCast radius used for spawning objects in the world.
		/// </summary>
		[Tooltip("SphereCast radius used for spawning objects in the world.")]
		public float SphereRadius = 0.5f;

		/// <summary>
		/// The size of the bounding box used for random spawn position selection.
		/// </summary>
		public Vector3 BoundingBoxSize = Vector3.one;

		/// <summary>
		/// The list of spawnable settings used to configure each spawnable object.
		/// Supports polymorphic subclasses via <see cref="SerializeReference"/> for type-specific data injection.
		/// </summary>
		[SerializeReference, SubclassSelector]
		public List<SpawnableSettings> Spawnables;

#if UNITY_EDITOR
		/// <summary>
		/// The color used to draw the spawner's gizmo in the editor.
		/// </summary>
		public Color GizmoColor = Color.red;

		/// <summary>
		/// Tags a newly added spawner so it never reaches a build.
		/// </summary>
		private void Reset()
		{
			gameObject.tag = EditorOnlyTag;
		}

		/// <summary>
		/// Re-tags the spawner whenever it is loaded, pasted or edited, so nobody can leave one
		/// untagged. A tag changed on the GameObject afterwards does not call this; the scene-save
		/// hook in <c>SpawnerTagEnforcer</c> catches that before the scene is written.
		/// </summary>
		private void OnValidate()
		{
			if (Application.isPlaying || CompareTag(EditorOnlyTag))
			{
				return;
			}

			// A tag cannot be set from inside OnValidate while Unity is loading or importing the object.
			UnityEditor.EditorApplication.delayCall += () =>
			{
				if (this != null && !Application.isPlaying)
				{
					EnforceEditorOnlyTag(this);
				}
			};
		}

		/// <summary>
		/// Tags a spawner's GameObject <see cref="EditorOnlyTag"/>, recording an undo step and
		/// dirtying its scene or prefab.
		/// </summary>
		/// <param name="spawner">The spawner.</param>
		/// <returns>True if the tag was changed.</returns>
		public static bool EnforceEditorOnlyTag(ObjectSpawner spawner)
		{
			if (spawner == null || spawner.CompareTag(EditorOnlyTag))
			{
				return false;
			}

			GameObject go = spawner.gameObject;
			UnityEditor.Undo.RecordObject(go, "Tag Spawner EditorOnly");
			string previous = go.tag;
			go.tag = EditorOnlyTag;
			UnityEditor.EditorUtility.SetDirty(go);
			if (go.scene.IsValid() && !UnityEditor.EditorUtility.IsPersistent(go))
			{
				UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
			}
			Debug.Log($"[ObjectSpawner] {go.name}: tag '{previous}' -> '{EditorOnlyTag}'. Spawners are authoring-only and must never ship to clients.", go);
			return true;
		}

		/// <summary>
		/// Draws the spawner's bounding box or collider gizmo in the editor for visualization.
		/// </summary>
		void OnDrawGizmos()
		{
			Collider collider = gameObject.GetComponent<Collider>();
			if (collider != null)
			{
				collider.DrawGizmo(GizmoColor);
			}
			else
			{
				Gizmos.color = GizmoColor;
				Gizmos.DrawWireCube(transform.position, BoundingBoxSize);
				ColliderExtensions.DrawCenterMarker(transform.position, GizmoColor);
			}
		}
#endif
	}
}
