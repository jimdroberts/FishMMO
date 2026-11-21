using System.Text;
using UnityEditor;
using UnityEngine;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;

namespace FishMMO.Shared
{
	/// <summary>
	/// The Scene view label of an object spawner (the NPCs module's since 2026-11-20), drawn with the core label helpers in
	/// <see cref="SceneObjectGizmoLabelDrawer"/>.
	/// </summary>
	public static class ObjectSpawnerGizmoLabelDrawer
	{
		private const int MaxSpawnableRows = 4;
		private static readonly Color ObjectSpawnerLabelColor = new Color(1.0f, 0.68f, 0.25f, 1.0f);
		private static readonly GUIStyle ObjectSpawnerLabelStyle = SceneObjectGizmoLabelDrawer.CreateLabelStyle(ObjectSpawnerLabelColor);
		private static readonly GUIStyle ObjectSpawnerBasicLabelStyle = SceneObjectGizmoLabelDrawer.CreateLabelStyle(SceneObjectGizmoLabelDrawer.CreateBasicLabelColor(ObjectSpawnerLabelColor));

		/// <summary>
		/// Draws a descriptive label for an object spawner.
		/// </summary>
		/// <param name="spawner">The object spawner to label.</param>
		/// <param name="gizmoType">The gizmo draw context.</param>
		[DrawGizmo(GizmoType.Active | GizmoType.InSelectionHierarchy | GizmoType.NonSelected | GizmoType.Selected)]
		public static void DrawObjectSpawnerLabel(ObjectSpawner spawner, GizmoType gizmoType)
		{
			if (!SceneObjectGizmoLabelDrawer.ShouldDrawSceneLabels() || spawner == null)
			{
				return;
			}

			GameObject gameObject = spawner.gameObject;
			if (gameObject == null)
			{
				return;
			}
			float distance;
			bool isInDetailedScreenFocus;
			if (!SceneObjectGizmoLabelDrawer.TryGetSceneViewLabelContext(gameObject.transform.position, out distance, out isInDetailedScreenFocus))
			{
				return;
			}

			GUIStyle labelStyle;
			bool useDetailedLabel;
			SceneObjectGizmoLabelDrawer.GetLabelStyle(isInDetailedScreenFocus, ObjectSpawnerLabelStyle, ObjectSpawnerBasicLabelStyle, out labelStyle, out useDetailedLabel);

			Vector3 labelPosition = SceneObjectGizmoLabelDrawer.GetLabelPosition(gameObject.transform, 1.6f, useDetailedLabel);
			string labelText = useDetailedLabel ? BuildSpawnerLabel(spawner) : gameObject.name;
			SceneObjectGizmoLabelDrawer.DrawOrderedLabel(labelPosition, labelText, labelStyle, distance);

			if (useDetailedLabel)
			{
				DrawSpawnerSpawnArea(spawner);
			}
		}


		/// <summary>
		/// Builds a human-readable ObjectSpawner label.
		/// </summary>
		/// <param name="spawner">The object spawner.</param>
		/// <returns>A descriptive label string.</returns>
		private static string BuildSpawnerLabel(ObjectSpawner spawner)
		{
			StringBuilder builder = new StringBuilder(384);
			builder.Append(spawner.gameObject.name);
			builder.Append("\nObjectSpawner | Scene: ");
			builder.Append(spawner.gameObject.scene.name);
			builder.Append("\nType: ");
			builder.Append(spawner.SpawnType);
			builder.Append(" | Count: ");
			builder.Append(spawner.InitialSpawnCount);
			builder.Append('/');
			builder.Append(spawner.MaxSpawnCount);
			builder.Append(" | Entries: ");
			builder.Append(spawner.Spawnables == null ? 0 : spawner.Spawnables.Count);
			if (!spawner.CompareTag(ObjectSpawner.EditorOnlyTag))
			{
				// Anything else ships the spawner to every client build.
				builder.Append(" | NOT EditorOnly");
			}
			builder.Append("\nPosition: ");
			builder.Append(spawner.RandomSpawnPosition ? "Random" : "Fixed");
			builder.Append(" | Bounds: ");
			SceneObjectGizmoLabelDrawer.AppendVector3(builder, spawner.BoundingBoxSize);
			builder.Append(" | Probe: ");
			SceneObjectGizmoLabelDrawer.AppendFloat(builder, spawner.SphereRadius);
			builder.Append(" | Respawn: ");
			builder.Append(spawner.RandomRespawnTime ? "Random" : "Fixed");
			builder.Append("\nRespawn Time: ");
			SceneObjectGizmoLabelDrawer.AppendFloat(builder, spawner.InitialRespawnTime);
			builder.Append("s | OR Conditions: ");
			builder.Append(spawner.OrConditions == null ? 0 : spawner.OrConditions.Count);
			builder.Append(" | AND Conditions: ");
			builder.Append(spawner.TrueConditions == null ? 0 : spawner.TrueConditions.Count);
			AppendSpawnables(builder, spawner.Spawnables);
			return builder.ToString();
		}


		/// <summary>
		/// Appends a compact spawnable summary.
		/// </summary>
		/// <param name="builder">The string builder to append to.</param>
		/// <param name="spawnables">The spawnable settings list.</param>
		private static void AppendSpawnables(StringBuilder builder, System.Collections.Generic.List<SpawnableSettings> spawnables)
		{
			if (spawnables == null || spawnables.Count < 1)
			{
				builder.Append("\nSpawnables: None");
				return;
			}

			int rowCount = Mathf.Min(spawnables.Count, MaxSpawnableRows);
			for (int index = 0; index < rowCount; index++)
			{
				SpawnableSettings settings = spawnables[index];
				builder.Append("\n[");
				builder.Append(index);
				builder.Append("] ");
				AppendSpawnableName(builder, settings);
				builder.Append(" | Chance: ");
				SceneObjectGizmoLabelDrawer.AppendFloat(builder, settings == null ? 0.0f : settings.SpawnChance);
				builder.Append(" | Respawn: ");
				SceneObjectGizmoLabelDrawer.AppendFloat(builder, settings == null ? 0.0f : settings.MinimumRespawnTime);
				builder.Append('-');
				SceneObjectGizmoLabelDrawer.AppendFloat(builder, settings == null ? 0.0f : settings.MaximumRespawnTime);
				builder.Append('s');
			}

			if (spawnables.Count > MaxSpawnableRows)
			{
				builder.Append("\n...");
				builder.Append(spawnables.Count - MaxSpawnableRows);
				builder.Append(" more spawnables");
			}
		}


		/// <summary>
		/// Appends the best display name for spawnable settings.
		/// </summary>
		/// <param name="builder">The string builder to append to.</param>
		/// <param name="settings">The spawnable settings.</param>
		private static void AppendSpawnableName(StringBuilder builder, SpawnableSettings settings)
		{
			if (settings == null)
			{
				builder.Append("Null settings");
				return;
			}

			builder.Append(settings.GetType().Name);
			builder.Append(": ");
			if (settings.NetworkObject == null)
			{
				builder.Append(SceneObjectGizmoLabelDrawer.UnassignedText);
				return;
			}

			builder.Append(settings.NetworkObject.name);
			ItemSpawnableSettings itemSettings = settings as ItemSpawnableSettings;
			if (itemSettings != null && itemSettings.ItemTemplate != null)
			{
				builder.Append(" -> ");
				builder.Append(itemSettings.ItemTemplate.Name);
			}
		}


		/// <summary>
		/// Draws additional spawner area indicators.
		/// </summary>
		/// <param name="spawner">The object spawner.</param>
		private static void DrawSpawnerSpawnArea(ObjectSpawner spawner)
		{
			if (spawner == null || !spawner.RandomSpawnPosition)
			{
				return;
			}

			Color previousColor = Handles.color;
			Handles.color = new Color(1.0f, 0.35f, 0.1f, 0.8f);
			Handles.DrawWireCube(spawner.transform.position, spawner.BoundingBoxSize);
			Handles.CubeHandleCap(0, spawner.transform.position, Quaternion.identity, 0.2f, EventType.Repaint);
			Handles.DrawWireDisc(spawner.transform.position, Vector3.up, spawner.SphereRadius);
			Handles.color = previousColor;
		}

	}
}
