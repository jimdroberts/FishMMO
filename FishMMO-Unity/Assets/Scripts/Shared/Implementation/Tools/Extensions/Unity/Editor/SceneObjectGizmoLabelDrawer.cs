using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Draws descriptive Scene view labels for interactables, object spawners, and teleporters.
	/// </summary>
	public static class SceneObjectGizmoLabelDrawer
	{
		private const string UnassignedText = "Unassigned";
		private const string MissingText = "Missing";
		private const int MaxSpawnableRows = 4;
		private const int MaxIncomingTeleporterRows = 4;
		private const float LabelPadding = 0.35f;
		private const float DetailedLabelScreenRadius = 0.035f;
		private const int MaxLabelDepth = 10000;
		private const float BasicLabelBrightnessMultiplier = 0.7f;
		private const float BasicLabelAlpha = 0.86f;

		private static readonly Color InteractableLabelColor = new Color(0.55f, 0.9f, 1.0f, 1.0f);
		private static readonly Color ObjectSpawnerLabelColor = new Color(1.0f, 0.68f, 0.25f, 1.0f);
		private static readonly Color SceneTeleporterLabelColor = new Color(1.0f, 0.45f, 0.95f, 1.0f);
		private static readonly Color TeleporterDestinationLabelColor = new Color(0.55f, 1.0f, 0.55f, 1.0f);

		private static readonly GUIStyle InteractableLabelStyle = CreateLabelStyle(InteractableLabelColor);
		private static readonly GUIStyle InteractableBasicLabelStyle = CreateLabelStyle(CreateBasicLabelColor(InteractableLabelColor));
		private static readonly GUIStyle ObjectSpawnerLabelStyle = CreateLabelStyle(ObjectSpawnerLabelColor);
		private static readonly GUIStyle ObjectSpawnerBasicLabelStyle = CreateLabelStyle(CreateBasicLabelColor(ObjectSpawnerLabelColor));
		private static readonly GUIStyle SceneTeleporterLabelStyle = CreateLabelStyle(SceneTeleporterLabelColor);
		private static readonly GUIStyle SceneTeleporterBasicLabelStyle = CreateLabelStyle(CreateBasicLabelColor(SceneTeleporterLabelColor));
		private static readonly GUIStyle TeleporterDestinationLabelStyle = CreateLabelStyle(TeleporterDestinationLabelColor);
		private static readonly GUIStyle TeleporterDestinationBasicLabelStyle = CreateLabelStyle(CreateBasicLabelColor(TeleporterDestinationLabelColor));

		private static TeleporterCache teleporterCache;

		/// <summary>
		/// Draws a label containing the GameObject name and concrete interactable type.
		/// </summary>
		/// <param name="interactable">The interactable to label.</param>
		/// <param name="gizmoType">The gizmo draw context.</param>
		[DrawGizmo(GizmoType.Active | GizmoType.InSelectionHierarchy | GizmoType.NonSelected | GizmoType.Selected)]
		public static void DrawInteractableLabel(Interactable interactable, GizmoType gizmoType)
		{
			if (!ShouldDrawSceneLabels() || interactable == null)
			{
				return;
			}

			GameObject gameObject = interactable.gameObject;
			if (gameObject == null)
			{
				return;
			}
			float distance;
			bool isInDetailedScreenFocus;
			if (!TryGetSceneViewLabelContext(gameObject.transform.position, out distance, out isInDetailedScreenFocus))
			{
				return;
			}

			GUIStyle labelStyle;
			bool useDetailedLabel;
			GetLabelStyle(isInDetailedScreenFocus, InteractableLabelStyle, InteractableBasicLabelStyle, out labelStyle, out useDetailedLabel);

			Vector3 labelPosition = GetLabelPosition(gameObject.transform, 1.2f, useDetailedLabel);
			string labelText = useDetailedLabel ? BuildInteractableLabel(interactable) : gameObject.name;
			DrawOrderedLabel(labelPosition, labelText, labelStyle, distance);

			Teleporter teleporter = interactable as Teleporter;
			if (useDetailedLabel && teleporter != null)
			{
				DrawTeleporterConnection(teleporter.transform, teleporter.Target, teleporter.DestinationID, Color.cyan);
			}
		}

		/// <summary>
		/// Draws a descriptive label for an object spawner.
		/// </summary>
		/// <param name="spawner">The object spawner to label.</param>
		/// <param name="gizmoType">The gizmo draw context.</param>
		[DrawGizmo(GizmoType.Active | GizmoType.InSelectionHierarchy | GizmoType.NonSelected | GizmoType.Selected)]
		public static void DrawObjectSpawnerLabel(ObjectSpawner spawner, GizmoType gizmoType)
		{
			if (!ShouldDrawSceneLabels() || spawner == null)
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
			if (!TryGetSceneViewLabelContext(gameObject.transform.position, out distance, out isInDetailedScreenFocus))
			{
				return;
			}

			GUIStyle labelStyle;
			bool useDetailedLabel;
			GetLabelStyle(isInDetailedScreenFocus, ObjectSpawnerLabelStyle, ObjectSpawnerBasicLabelStyle, out labelStyle, out useDetailedLabel);

			Vector3 labelPosition = GetLabelPosition(gameObject.transform, 1.6f, useDetailedLabel);
			string labelText = useDetailedLabel ? BuildSpawnerLabel(spawner) : gameObject.name;
			DrawOrderedLabel(labelPosition, labelText, labelStyle, distance);

			if (useDetailedLabel)
			{
				DrawSpawnerSpawnArea(spawner);
			}
		}

		/// <summary>
		/// Draws a descriptive label for a scene teleporter.
		/// </summary>
		/// <param name="teleporter">The scene teleporter to label.</param>
		/// <param name="gizmoType">The gizmo draw context.</param>
		[DrawGizmo(GizmoType.Active | GizmoType.InSelectionHierarchy | GizmoType.NonSelected | GizmoType.Selected)]
		public static void DrawSceneTeleporterLabel(SceneTeleporter teleporter, GizmoType gizmoType)
		{
			if (!ShouldDrawSceneLabels() || teleporter == null)
			{
				return;
			}

			GameObject gameObject = teleporter.gameObject;
			if (gameObject == null)
			{
				return;
			}
			float distance;
			bool isInDetailedScreenFocus;
			if (!TryGetSceneViewLabelContext(gameObject.transform.position, out distance, out isInDetailedScreenFocus))
			{
				return;
			}

			GUIStyle labelStyle;
			bool useDetailedLabel;
			GetLabelStyle(isInDetailedScreenFocus, SceneTeleporterLabelStyle, SceneTeleporterBasicLabelStyle, out labelStyle, out useDetailedLabel);

			Vector3 labelPosition = GetLabelPosition(gameObject.transform, 1.4f, useDetailedLabel);
			string labelText = useDetailedLabel ? BuildSceneTeleporterLabel(teleporter) : gameObject.name;
			DrawOrderedLabel(labelPosition, labelText, labelStyle, distance);
			if (useDetailedLabel)
			{
				DrawTeleporterConnection(teleporter.transform, null, teleporter.DestinationID, Color.magenta);
			}
		}

		/// <summary>
		/// Draws a descriptive label for a teleporter destination.
		/// </summary>
		/// <param name="destination">The teleporter destination to label.</param>
		/// <param name="gizmoType">The gizmo draw context.</param>
		[DrawGizmo(GizmoType.Active | GizmoType.InSelectionHierarchy | GizmoType.NonSelected | GizmoType.Selected)]
		public static void DrawTeleporterDestinationLabel(TeleporterDestination destination, GizmoType gizmoType)
		{
			if (!ShouldDrawSceneLabels() || destination == null)
			{
				return;
			}

			GameObject gameObject = destination.gameObject;
			if (gameObject == null)
			{
				return;
			}
			float distance;
			bool isInDetailedScreenFocus;
			if (!TryGetSceneViewLabelContext(gameObject.transform.position, out distance, out isInDetailedScreenFocus))
			{
				return;
			}

			GUIStyle labelStyle;
			bool useDetailedLabel;
			GetLabelStyle(isInDetailedScreenFocus, TeleporterDestinationLabelStyle, TeleporterDestinationBasicLabelStyle, out labelStyle, out useDetailedLabel);

			Vector3 labelPosition = GetLabelPosition(gameObject.transform, 1.4f, useDetailedLabel);
			string labelText = useDetailedLabel ? BuildTeleporterDestinationLabel(destination) : gameObject.name;
			DrawOrderedLabel(labelPosition, labelText, labelStyle, distance);
		}

		/// <summary>
		/// Returns whether label drawing should run for the current editor event.
		/// </summary>
		/// <returns>True when labels should be drawn.</returns>
		private static bool ShouldDrawSceneLabels()
		{
			Event currentEvent = Event.current;
			return currentEvent == null || currentEvent.type == EventType.Repaint;
		}

		/// <summary>
		/// Creates a consistently aligned Scene view label style.
		/// </summary>
		/// <param name="color">The label text color.</param>
		/// <returns>The configured label style.</returns>
		private static GUIStyle CreateLabelStyle(Color color)
		{
			return new GUIStyle(EditorStyles.boldLabel)
			{
				normal = { textColor = color },
				alignment = TextAnchor.UpperLeft,
			};
		}

		/// <summary>
		/// Creates the basic label color as a 30 percent darker match for a detail color.
		/// </summary>
		/// <param name="detailColor">The detailed label color.</param>
		/// <returns>The matching basic label color.</returns>
		private static Color CreateBasicLabelColor(Color detailColor)
		{
			return new Color(
				detailColor.r * BasicLabelBrightnessMultiplier,
				detailColor.g * BasicLabelBrightnessMultiplier,
				detailColor.b * BasicLabelBrightnessMultiplier,
				BasicLabelAlpha);
		}

		/// <summary>
		/// Gets the label style for the current screen focus state.
		/// </summary>
		/// <param name="isInDetailedScreenFocus">True when the label is within the mouse detailed screen radius.</param>
		/// <param name="detailedStyle">The detailed label style.</param>
		/// <param name="basicStyle">The basic label style.</param>
		/// <param name="labelStyle">The selected label style.</param>
		/// <param name="useDetailedLabel">True when the detailed label should be built and drawn.</param>
		private static void GetLabelStyle(bool isInDetailedScreenFocus, GUIStyle detailedStyle, GUIStyle basicStyle, out GUIStyle labelStyle, out bool useDetailedLabel)
		{
			useDetailedLabel = isInDetailedScreenFocus;
			labelStyle = useDetailedLabel ? detailedStyle : basicStyle;
		}

		/// <summary>
		/// Draws a Scene view label with GUI depth derived from camera distance so closer labels draw on top.
		/// </summary>
		/// <param name="position">The world-space label position.</param>
		/// <param name="text">The label text.</param>
		/// <param name="style">The label style.</param>
		/// <param name="distance">The Scene view camera distance.</param>
		private static void DrawOrderedLabel(Vector3 position, string text, GUIStyle style, float distance)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return;
			}

			int previousDepth = GUI.depth;
			GUI.depth = Mathf.Clamp(Mathf.RoundToInt(distance * 10.0f), 0, MaxLabelDepth);
			Handles.Label(position, text, style);
			GUI.depth = previousDepth;
		}

		/// <summary>
		/// Builds a human-readable Interactable label.
		/// </summary>
		/// <param name="interactable">The interactable.</param>
		/// <returns>A descriptive label string.</returns>
		private static string BuildInteractableLabel(Interactable interactable)
		{
			StringBuilder builder = new StringBuilder(192);
			builder.Append(interactable.gameObject.name);
			builder.Append("\nInteractable: ");
			builder.Append(interactable.GetType().Name);
			builder.Append(" | Title: ");
			builder.Append(string.IsNullOrWhiteSpace(interactable.Title) ? UnassignedText : interactable.Title);
			builder.Append("\nRange: ");
			AppendFloat(builder, interactable.InteractionRange);
			builder.Append(" | Rate: ");
			builder.Append(interactable.InteractRateLimit);
			builder.Append("ms | Triggers: ");
			builder.Append(interactable.OnInteractTriggers == null ? 0 : interactable.OnInteractTriggers.Count);

			Teleporter teleporter = interactable as Teleporter;
			if (teleporter != null)
			{
				AppendTeleporterDestination(builder, teleporter.Target, teleporter.DestinationID);
			}

			return builder.ToString();
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
			builder.Append(" | Alive: ");
			builder.Append(Application.isPlaying && spawner.Spawned != null ? spawner.Spawned.Count : 0);
			builder.Append("\nPosition: ");
			builder.Append(spawner.RandomSpawnPosition ? "Random" : "Fixed");
			builder.Append(" | Bounds: ");
			AppendVector3(builder, spawner.BoundingBoxSize);
			builder.Append(" | Probe: ");
			AppendFloat(builder, spawner.SphereRadius);
			builder.Append(" | Respawn: ");
			builder.Append(spawner.RandomRespawnTime ? "Random" : "Fixed");
			builder.Append("\nRespawn Time: ");
			AppendFloat(builder, spawner.InitialRespawnTime);
			builder.Append("s | OR Conditions: ");
			builder.Append(spawner.OrConditions == null ? 0 : spawner.OrConditions.Count);
			builder.Append(" | AND Conditions: ");
			builder.Append(spawner.TrueConditions == null ? 0 : spawner.TrueConditions.Count);
			AppendSpawnables(builder, spawner.Spawnables);
			return builder.ToString();
		}

		/// <summary>
		/// Builds a human-readable SceneTeleporter label.
		/// </summary>
		/// <param name="teleporter">The scene teleporter.</param>
		/// <returns>A descriptive label string.</returns>
		private static string BuildSceneTeleporterLabel(SceneTeleporter teleporter)
		{
			StringBuilder builder = new StringBuilder(256);
			builder.Append(teleporter.gameObject.name);
			builder.Append("\nSceneTeleporter | Scene: ");
			builder.Append(teleporter.gameObject.scene.name);
			AppendTeleporterDestination(builder, null, teleporter.DestinationID);
			AppendCachedTeleporterStatus(builder, teleporter);
			return builder.ToString();
		}

		/// <summary>
		/// Builds a human-readable TeleporterDestination label.
		/// </summary>
		/// <param name="destination">The teleporter destination.</param>
		/// <returns>A descriptive label string.</returns>
		private static string BuildTeleporterDestinationLabel(TeleporterDestination destination)
		{
			StringBuilder builder = new StringBuilder(256);
			builder.Append(destination.gameObject.name);
			builder.Append("\nTeleporterDestination | Scene: ");
			builder.Append(destination.gameObject.scene.name);
			builder.Append("\nID: ");
			AppendShortId(builder, destination.DestinationID);
			builder.Append(" | Position: ");
			AppendVector3(builder, destination.transform.position);
			AppendIncomingTeleporters(builder, destination.DestinationID);
			return builder.ToString();
		}

		/// <summary>
		/// Appends destination details for a direct target or cached DestinationID.
		/// </summary>
		/// <param name="builder">The string builder to append to.</param>
		/// <param name="target">The direct target, if assigned.</param>
		/// <param name="destinationID">The cached destination ID, if assigned.</param>
		private static void AppendTeleporterDestination(StringBuilder builder, Transform target, string destinationID)
		{
			if (target != null)
			{
				builder.Append("\nTarget: ");
				builder.Append(target.name);
				builder.Append(" @ ");
				AppendVector3(builder, target.position);
				return;
			}

			builder.Append("\nDestination: ");
			if (string.IsNullOrWhiteSpace(destinationID))
			{
				builder.Append(UnassignedText);
				return;
			}

			TeleporterCacheEntry destinationEntry = GetDestinationEntry(destinationID);
			if (destinationEntry == null)
			{
				builder.Append(MissingText);
				builder.Append(" (");
				AppendShortId(builder, destinationID);
				builder.Append(')');
				return;
			}

			builder.Append(destinationEntry.SceneName);
			builder.Append('/');
			builder.Append(destinationEntry.DisplayName);
			builder.Append(" @ ");
			AppendVector3(builder, destinationEntry.Position);
			builder.Append(" | ID: ");
			AppendShortId(builder, destinationEntry.DestinationID);
		}

		/// <summary>
		/// Appends cache health details for a SceneTeleporter.
		/// </summary>
		/// <param name="builder">The string builder to append to.</param>
		/// <param name="teleporter">The scene teleporter.</param>
		private static void AppendCachedTeleporterStatus(StringBuilder builder, SceneTeleporter teleporter)
		{
			TeleporterCache cache = GetTeleporterCache();
			if (cache == null || cache.Teleporters == null)
			{
				builder.Append("\nCache: Missing TeleporterCache asset");
				return;
			}

			string cacheKey = teleporter.gameObject.scene.name + "/" + teleporter.gameObject.name.Trim();
			builder.Append("\nCache Key: ");
			builder.Append(cacheKey);
			builder.Append(" | Cache: ");
			builder.Append(cache.Teleporters.ContainsKey(cacheKey) ? "Registered" : "Not registered");
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
				AppendFloat(builder, settings == null ? 0.0f : settings.SpawnChance);
				builder.Append(" | Respawn: ");
				AppendFloat(builder, settings == null ? 0.0f : settings.MinimumRespawnTime);
				builder.Append('-');
				AppendFloat(builder, settings == null ? 0.0f : settings.MaximumRespawnTime);
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
				builder.Append(UnassignedText);
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
		/// Appends incoming teleporter links for a destination.
		/// </summary>
		/// <param name="builder">The string builder to append to.</param>
		/// <param name="destinationID">The destination ID to inspect.</param>
		private static void AppendIncomingTeleporters(StringBuilder builder, string destinationID)
		{
			if (string.IsNullOrWhiteSpace(destinationID))
			{
				builder.Append("\nIncoming: ");
				builder.Append(UnassignedText);
				return;
			}

			TeleporterCache cache = GetTeleporterCache();
			if (cache == null || cache.Teleporters == null)
			{
				builder.Append("\nIncoming: Cache unavailable");
				return;
			}

			int incomingCount = 0;
			int displayedCount = 0;
			builder.Append("\nIncoming:");
			foreach (System.Collections.Generic.KeyValuePair<string, SceneTeleporterCacheEntry> pair in cache.Teleporters)
			{
				SceneTeleporterCacheEntry entry = pair.Value;
				if (entry == null || entry.DestinationID != destinationID)
				{
					continue;
				}

				incomingCount++;
				if (displayedCount >= MaxIncomingTeleporterRows)
				{
					continue;
				}

				builder.Append("\n <- ");
				builder.Append(entry.SceneName);
				builder.Append('/');
				builder.Append(entry.TeleporterName);
				displayedCount++;
			}

			if (incomingCount == 0)
			{
				builder.Append(' ');
				builder.Append("None");
			}
			else if (incomingCount > displayedCount)
			{
				builder.Append("\n <- ...");
				builder.Append(incomingCount - displayedCount);
				builder.Append(" more");
			}
		}

		/// <summary>
		/// Draws a teleporter connection when the target position is available.
		/// </summary>
		/// <param name="source">The teleporter transform.</param>
		/// <param name="directTarget">The direct target transform, if assigned.</param>
		/// <param name="destinationID">The cached destination ID, if assigned.</param>
		/// <param name="color">The line color.</param>
		private static void DrawTeleporterConnection(Transform source, Transform directTarget, string destinationID, Color color)
		{
			if (source == null)
			{
				return;
			}

			Vector3 destinationPosition;
			if (!TryGetDestinationPosition(source.gameObject.scene, directTarget, destinationID, out destinationPosition))
			{
				return;
			}

			Color previousColor = Handles.color;
			Handles.color = color;
			Handles.DrawDottedLine(source.position, destinationPosition, 4.0f);
			Handles.DrawWireDisc(destinationPosition, Vector3.up, 0.75f);
			Handles.color = previousColor;
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
			Handles.DrawWireDisc(spawner.transform.position, Vector3.up, spawner.SphereRadius);
			Handles.color = previousColor;
		}

		/// <summary>
		/// Resolves a destination position for local gizmo lines.
		/// </summary>
		/// <param name="sourceScene">The source scene.</param>
		/// <param name="directTarget">A direct target transform, if assigned.</param>
		/// <param name="destinationID">A cached destination ID, if assigned.</param>
		/// <param name="destinationPosition">The resolved position.</param>
		/// <returns>True if a usable local destination position is available.</returns>
		private static bool TryGetDestinationPosition(Scene sourceScene, Transform directTarget, string destinationID, out Vector3 destinationPosition)
		{
			if (directTarget != null)
			{
				destinationPosition = directTarget.position;
				return true;
			}

			TeleporterCacheEntry destinationEntry = GetDestinationEntry(destinationID);
			if (destinationEntry != null && destinationEntry.SceneName == sourceScene.name)
			{
				destinationPosition = destinationEntry.Position;
				return true;
			}

			destinationPosition = default;
			return false;
		}

		/// <summary>
		/// Gets a cached destination entry by ID.
		/// </summary>
		/// <param name="destinationID">The destination ID.</param>
		/// <returns>The cache entry, or null when unavailable.</returns>
		private static TeleporterCacheEntry GetDestinationEntry(string destinationID)
		{
			if (string.IsNullOrWhiteSpace(destinationID))
			{
				return null;
			}

			TeleporterCache cache = GetTeleporterCache();
			if (cache == null || cache.Destinations == null)
			{
				return null;
			}

			TeleporterCacheEntry destinationEntry;
			return cache.Destinations.TryGetValue(destinationID, out destinationEntry) ? destinationEntry : null;
		}

		/// <summary>
		/// Gets the TeleporterCache asset without creating or mutating it from gizmo drawing.
		/// </summary>
		/// <returns>The TeleporterCache asset, or null when missing.</returns>
		private static TeleporterCache GetTeleporterCache()
		{
			if (teleporterCache == null)
			{
				teleporterCache = AssetDatabase.LoadAssetAtPath<TeleporterCache>(TeleporterCache.CACHE_FULL_PATH);
			}

			return teleporterCache;
		}

		/// <summary>
		/// Appends a compact vector display.
		/// </summary>
		/// <param name="builder">The string builder to append to.</param>
		/// <param name="value">The vector value.</param>
		private static void AppendVector3(StringBuilder builder, Vector3 value)
		{
			builder.Append('(');
			AppendFloat(builder, value.x);
			builder.Append(", ");
			AppendFloat(builder, value.y);
			builder.Append(", ");
			AppendFloat(builder, value.z);
			builder.Append(')');
		}

		/// <summary>
		/// Appends a compact float display.
		/// </summary>
		/// <param name="builder">The string builder to append to.</param>
		/// <param name="value">The float value.</param>
		private static void AppendFloat(StringBuilder builder, float value)
		{
			builder.Append(value.ToString("0.##"));
		}

		/// <summary>
		/// Appends a shortened ID for compact labels.
		/// </summary>
		/// <param name="builder">The string builder to append to.</param>
		/// <param name="id">The ID to shorten.</param>
		private static void AppendShortId(StringBuilder builder, string id)
		{
			if (string.IsNullOrWhiteSpace(id))
			{
				builder.Append(UnassignedText);
				return;
			}

			int length = Mathf.Min(8, id.Length);
			builder.Append(id.Substring(0, length));
		}

		/// <summary>
		/// Gets a label position above an object, using collider bounds when available.
		/// </summary>
		/// <param name="transform">The object's transform.</param>
		/// <param name="fallbackHeight">Fallback height when no collider exists.</param>
		/// <param name="useColliderBounds">True to use collider bounds for precise placement.</param>
		/// <returns>The world-space label position.</returns>
		private static Vector3 GetLabelPosition(Transform transform, float fallbackHeight, bool useColliderBounds)
		{
			if (useColliderBounds)
			{
				Collider collider = transform.GetComponent<Collider>();
				if (collider != null)
				{
					Bounds bounds = collider.bounds;
					return new Vector3(bounds.center.x, bounds.max.y + LabelPadding, bounds.center.z);
				}
			}

			return transform.position + Vector3.up * fallbackHeight;
		}

		/// <summary>
		/// Tries to get the active Scene view camera context for label rendering.
		/// </summary>
		/// <param name="position">The world position to test.</param>
		/// <param name="distance">The resolved Scene view camera distance.</param>
		/// <param name="isInDetailedScreenFocus">True when the position is near the Scene view mouse cursor.</param>
		/// <returns>True if labels should draw for this position.</returns>
		private static bool TryGetSceneViewLabelContext(Vector3 position, out float distance, out bool isInDetailedScreenFocus)
		{
			SceneView sceneView = SceneView.currentDrawingSceneView;
			Camera camera = sceneView == null ? null : sceneView.camera;
			if (camera == null)
			{
				distance = 0.0f;
				isInDetailedScreenFocus = true;
				return true;
			}

			Vector3 offset = camera.transform.position - position;
			distance = offset.magnitude;
			if (camera.WorldToViewportPoint(position).z <= camera.nearClipPlane)
			{
				isInDetailedScreenFocus = false;
				return false;
			}

			isInDetailedScreenFocus = IsInDetailedScreenFocus(camera, position);
			return true;
		}

		/// <summary>
		/// Returns whether a world position is within the mouse-centered detailed-label screen radius.
		/// </summary>
		/// <param name="camera">The Scene view camera.</param>
		/// <param name="position">The world position to test.</param>
		/// <returns>True when the position is inside the mouse-centered screen focus radius.</returns>
		private static bool IsInDetailedScreenFocus(Camera camera, Vector3 position)
		{
			Vector3 viewportPosition = camera.WorldToViewportPoint(position);
			if (viewportPosition.z <= camera.nearClipPlane)
			{
				return false;
			}

			float screenWidth = camera.pixelWidth;
			float screenHeight = camera.pixelHeight;
			if (screenWidth <= 0.0f || screenHeight <= 0.0f)
			{
				return false;
			}

			Event currentEvent = Event.current;
			if (currentEvent == null)
			{
				return false;
			}

			Vector2 labelGuiPosition = HandleUtility.WorldToGUIPoint(position);
			Vector2 mousePosition = currentEvent.mousePosition;
			float offsetX = labelGuiPosition.x - mousePosition.x;
			float offsetY = labelGuiPosition.y - mousePosition.y;
			float focusRadius = Mathf.Min(screenWidth, screenHeight) * DetailedLabelScreenRadius / EditorGUIUtility.pixelsPerPoint;
			return offsetX * offsetX + offsetY * offsetY <= focusRadius * focusRadius;
		}
	}
}
