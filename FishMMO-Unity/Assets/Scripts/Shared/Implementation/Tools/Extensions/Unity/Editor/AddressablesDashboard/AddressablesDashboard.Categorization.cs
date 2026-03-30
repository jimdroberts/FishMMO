#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace FishMMO.Shared
{
	public partial class AddressablesDashboard
	{
		private const string SharedDependenciesGroupName = "_SharedDependencies";

		// ──────────────────────────────────────────────
		// Smart Group Categorization
		// ──────────────────────────────────────────────

		/// <summary>
		/// Target groups for smart asset categorization during Fix All.
		/// </summary>
		private static class SmartGroups
		{
			public const string ClientStaticPermanent = "Client_Static_Permanent";
			public const string ServerStaticPermanent = "Server_Static_Permanent";
			public const string SharedStaticPermanent = "Shared_Static_Permanent";
			public const string ClientDynamic = "Client_Dynamic";
			public const string ServerDynamic = "Server_Dynamic";
			public const string SharedDynamic = "Shared_Dynamic";
			public const string SceneShared = "Scene_Shared";
			public const string SceneClient = "Scene_Client";
			public const string SceneServer = "Scene_Server";

			/// <summary>
			/// All smart group names. Used to strip stale labels from entries.
			/// </summary>
			public static readonly string[] All =
			{
				ClientStaticPermanent,
				ServerStaticPermanent,
				SharedStaticPermanent,
				ClientDynamic,
				ServerDynamic,
				SharedDynamic,
				SceneShared,
				SceneClient,
				SceneServer,
			};
		}

		/// <summary>
		/// Categorization result returned by the smart routing logic.
		/// </summary>
		private struct AssetCategory
		{
			public string GroupName;
			public string Reason;
			public bool IsPluginWarning;
		}

		/// <summary>
		/// Determines which smart group an asset should be placed in based on its path
		/// and which existing groups reference it.
		/// </summary>
		/// <param name="assetPath">The asset path to categorize.</param>
		/// <param name="referencingGroupNames">Names of groups that reference this asset (null if unknown).</param>
		/// <returns>The categorization result with target group name and reason.</returns>
		private static AssetCategory CategorizeAsset(string assetPath, HashSet<string> referencingGroupNames)
		{
			// Normalize separators for reliable matching
			string normalized = assetPath.Replace('\\', '/');

			// ── Plugins ──
			if (normalized.StartsWith("Assets/Plugins/", StringComparison.OrdinalIgnoreCase))
			{
				// TextMesh Pro runtime assets (fonts, shaders, materials) are client-only
				// because the server runs headless — no rendering.
				if (normalized.StartsWith("Assets/Plugins/TextMesh Pro/", StringComparison.OrdinalIgnoreCase) &&
					!IsEditorOnlyPath(normalized))
				{
					return new AssetCategory
					{
						GroupName = SmartGroups.ClientStaticPermanent,
						Reason = "TextMesh Pro runtime asset — client-only (server is headless)",
					};
				}

				// All other plugins: warn, do not fix
				return new AssetCategory
				{
					GroupName = null,
					Reason = "Plugin asset — replace with production assets instead of making addressable",
					IsPluginWarning = true,
				};
			}

			// ── Prefabs: only Client, Server, Shared subdirectories are addressable ──
			if (normalized.StartsWith("Assets/Prefabs/", StringComparison.OrdinalIgnoreCase))
			{
				bool isAllowed = normalized.StartsWith("Assets/Prefabs/Client/", StringComparison.OrdinalIgnoreCase) ||
								 normalized.StartsWith("Assets/Prefabs/Server/", StringComparison.OrdinalIgnoreCase) ||
								 normalized.StartsWith("Assets/Prefabs/Shared/", StringComparison.OrdinalIgnoreCase);
				if (!isAllowed)
				{
					return new AssetCategory
					{
						GroupName = null,
						Reason = "Editor-only prefab (outside Client/Server/Shared)",
					};
				}
			}

			// ── Templates: always shared + permanent ──
			if (normalized.StartsWith("Assets/Templates/", StringComparison.OrdinalIgnoreCase))
			{
				return new AssetCategory
				{
					GroupName = SmartGroups.SharedStaticPermanent,
					Reason = "Template — required at all times by client and server",
				};
			}

			// ── Sprites: client-only (server is headless, no rendering) ──
			if (normalized.StartsWith("Assets/Sprites/", StringComparison.OrdinalIgnoreCase))
			{
				return new AssetCategory
				{
					GroupName = SmartGroups.ClientStaticPermanent,
					Reason = "Sprite — client-only (server is headless)",
				};
			}

			// ── Render assets: client-only (server is headless) ──
			// Textures, fonts, materials, shaders, and render textures are never
			// needed by the headless server.
			if (IsRenderAsset(normalized))
			{
				// Still respect explicit Server paths — don't override those
				if (!ContainsSegment(normalized, "Server"))
				{
					return new AssetCategory
					{
						GroupName = SmartGroups.ClientStaticPermanent,
						Reason = "Render asset — client-only (server is headless)",
					};
				}
			}

			// ── Scenes ──
			if (normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
				normalized.StartsWith("Assets/Scenes/", StringComparison.OrdinalIgnoreCase))
			{
				if (ContainsSegment(normalized, "WorldScene"))
				{
					return new AssetCategory
					{
						GroupName = SmartGroups.SceneShared,
						Reason = "World scene — shared between client and server",
					};
				}
				if (ContainsSegment(normalized, "Client"))
				{
					return new AssetCategory
					{
						GroupName = SmartGroups.SceneClient,
						Reason = "Client scene",
					};
				}
				if (ContainsSegment(normalized, "Server") ||
					ContainsSegment(normalized, "LoginServer") ||
					ContainsSegment(normalized, "WorldServer") ||
					ContainsSegment(normalized, "SceneServer"))
				{
					return new AssetCategory
					{
						GroupName = SmartGroups.SceneServer,
						Reason = "Server scene",
					};
				}
				// Fallback for scenes without clear ownership
				return new AssetCategory
				{
					GroupName = SmartGroups.SceneShared,
					Reason = "Scene — no clear client/server indicator, defaulting to shared",
				};
			}

			// ── Explicit Client paths ──
			if (ContainsSegment(normalized, "Client"))
			{
				bool isStatic = ContainsSegment(normalized, "UI") ||
								ContainsSegment(normalized, "Input") ||
								ContainsSegment(normalized, "Launcher");
				return new AssetCategory
				{
					GroupName = isStatic ? SmartGroups.ClientStaticPermanent : SmartGroups.ClientDynamic,
					Reason = isStatic ? "Client static asset (UI/Input/Launcher)" : "Client dynamic asset",
				};
			}

			// ── Explicit Server paths ──
			if (ContainsSegment(normalized, "Server") ||
				ContainsSegment(normalized, "LoginServer") ||
				ContainsSegment(normalized, "WorldServer") ||
				ContainsSegment(normalized, "SceneServer"))
			{
				return new AssetCategory
				{
					GroupName = SmartGroups.ServerStaticPermanent,
					Reason = "Server asset",
				};
			}

			// ── Shared terrain: dynamic because terrain data is loaded per-scene ──
			if (ContainsSegment(normalized, "Shared") && ContainsSegment(normalized, "Terrain"))
			{
				return new AssetCategory
				{
					GroupName = SmartGroups.SharedDynamic,
					Reason = "Shared terrain — loaded per-scene, not permanent",
				};
			}

			// ── Shared explicit paths ──
			if (ContainsSegment(normalized, "Shared"))
			{
				return new AssetCategory
				{
					GroupName = SmartGroups.SharedStaticPermanent,
					Reason = "Shared asset (explicit Shared directory)",
				};
			}

			// ── Infer from referencing groups ──
			if (referencingGroupNames != null && referencingGroupNames.Count > 0)
			{
				bool hasClient = false;
				bool hasServer = false;
				foreach (string g in referencingGroupNames)
				{
					string gl = g.ToLowerInvariant();
					if (gl.Contains("client")) hasClient = true;
					if (gl.Contains("server")) hasServer = true;
				}

				if (hasClient && hasServer)
				{
					return new AssetCategory
					{
						GroupName = SmartGroups.SharedDynamic,
						Reason = $"Referenced by both client and server groups",
					};
				}
				if (hasClient)
				{
					return new AssetCategory
					{
						GroupName = SmartGroups.ClientDynamic,
						Reason = "Referenced only by client groups",
					};
				}
				if (hasServer)
				{
					return new AssetCategory
					{
						GroupName = SmartGroups.ServerDynamic,
						Reason = "Referenced only by server groups",
					};
				}
			}

			// ── Default: shared dynamic ──
			return new AssetCategory
			{
				GroupName = SmartGroups.SharedDynamic,
				Reason = "No clear ownership — defaulting to shared dynamic",
			};
		}

		/// <summary>
		/// Removes all smart-group labels from an entry, then sets only the correct one.
		/// </summary>
		private static void SetExclusiveSmartLabel(AddressableAssetSettings settings,
			AddressableAssetEntry entry, string correctLabel)
		{
			for (int i = 0; i < SmartGroups.All.Length; i++)
			{
				string label = SmartGroups.All[i];
				if (entry.labels.Contains(label) &&
					!string.Equals(label, correctLabel, StringComparison.OrdinalIgnoreCase))
				{
					entry.SetLabel(label, false);
				}
			}
			settings.AddLabel(correctLabel);
			entry.SetLabel(correctLabel, true);
		}

		/// <summary>
		/// Checks if a path contains a directory segment (between '/' separators).
		/// </summary>
		private static bool ContainsSegment(string path, string segment)
		{
			int idx = path.IndexOf(segment, StringComparison.OrdinalIgnoreCase);
			while (idx >= 0)
			{
				// Check the character before: must be '/' or start-of-string
				bool startOk = idx == 0 || path[idx - 1] == '/';
				int end = idx + segment.Length;
				// Check the character after: must be '/' or end-of-string
				bool endOk = end >= path.Length || path[end] == '/' || path[end] == '.';
				if (startOk && endOk) return true;

				idx = path.IndexOf(segment, end, StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}

		/// <summary>
		/// Returns true if the asset is a render-related type that only the client needs.
		/// The server runs headless — textures, sprites, fonts, materials, shaders,
		/// and render textures are never loaded server-side.
		/// </summary>
		private static bool IsRenderAsset(string normalizedPath)
		{
			string ext = System.IO.Path.GetExtension(normalizedPath).ToLowerInvariant();
			switch (ext)
			{
				// Textures / sprites
				case ".png":
				case ".jpg":
				case ".jpeg":
				case ".tga":
				case ".bmp":
				case ".psd":
				case ".gif":
				case ".hdr":
				case ".exr":
				case ".tif":
				case ".tiff":
				// Fonts
				case ".ttf":
				case ".otf":
				case ".fnt":
				// TMP font assets
				case ".fontsettings":
				// Materials / shaders
				case ".mat":
				case ".shader":
				case ".shadergraph":
				case ".shadersubgraph":
				case ".compute":
				// Render textures
				case ".rendertexture":
				case ".cubemap":
				// Models (meshes / animations are visual)
				case ".fbx":
				case ".obj":
				case ".blend":
				case ".dae":
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// Finds an existing group by name or creates a new one with BundledAssetGroupSchema.
		/// Newly created groups have their packing mode set based on the group name:
		/// Scene and Dynamic groups use PackSeparately, Static_Permanent groups use PackTogether.
		/// </summary>
		private static AddressableAssetGroup GetOrCreateGroup(AddressableAssetSettings settings, string groupName,
			Dictionary<string, AddressableAssetGroup> cache)
		{
			if (cache.TryGetValue(groupName, out AddressableAssetGroup cached))
			{
				return cached;
			}

			foreach (var group in settings.groups)
			{
				if (group != null && string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase))
				{
					cache[groupName] = group;
					return group;
				}
			}

			var newGroup = settings.CreateGroup(groupName, false, false, true, null, typeof(BundledAssetGroupSchema));
			if (newGroup != null)
			{
				ApplyGroupPackingMode(newGroup, groupName);
				cache[groupName] = newGroup;
			}
			return newGroup;
		}

		/// <summary>
		/// Sets the BundlePackingMode on a group based on its name convention.
		/// Scene and Dynamic groups use PackSeparately for on-demand loading.
		/// Static_Permanent groups use PackTogether for single-bundle bootstrap loading.
		/// </summary>
		/// <param name="group">The Addressable group.</param>
		/// <param name="groupName">The group name used for convention matching.</param>
		private static void ApplyGroupPackingMode(AddressableAssetGroup group, string groupName)
		{
			var schema = group.GetSchema<BundledAssetGroupSchema>();
			if (schema == null) return;

			string lower = groupName.ToLowerInvariant();
			if (lower.Contains("scene") || lower.Contains("dynamic"))
			{
				schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
			}
			else
			{
				schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
			}

			EditorUtility.SetDirty(group);
		}
	}
}
#endif