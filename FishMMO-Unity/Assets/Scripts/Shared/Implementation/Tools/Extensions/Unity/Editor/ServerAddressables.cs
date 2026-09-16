using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Registers server-only data assets in the <c>Server_Static_Permanent</c> addressables group.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The scene server loads that label at boot, and every client build drops any group whose
	/// name contains "Server". Registering an asset there explicitly keeps it out of client
	/// bundles by construction, instead of depending on it being reachable only from something
	/// that already is in the group.
	/// </para>
	/// <para>
	/// Idempotent: an asset already registered with the right address and label is left alone, so
	/// callers can run this on every bake without touching the group file.
	/// </para>
	/// </remarks>
	public static class ServerAddressables
	{
		/// <summary>The server's permanent addressables group.</summary>
		public const string GroupName = "Server_Static_Permanent";

		/// <summary>The label the server loads at boot.</summary>
		public const string Label = "Server_Static_Permanent";

		/// <summary>
		/// Puts an asset in <see cref="GroupName"/> with <see cref="Label"/>, addressed by its path.
		/// </summary>
		/// <param name="assetPath">The asset's project path.</param>
		/// <returns>True when the asset is registered afterwards.</returns>
		public static bool Register(string assetPath)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				Debug.LogWarning($"[ServerAddressables] No Addressables settings; '{assetPath}' was not registered.");
				return false;
			}

			string guid = AssetDatabase.AssetPathToGUID(assetPath);
			if (string.IsNullOrEmpty(guid))
			{
				return false;
			}

			AddressableAssetGroup group = settings.FindGroup(GroupName);
			if (group == null)
			{
				Debug.LogWarning($"[ServerAddressables] No '{GroupName}' group; '{assetPath}' was not registered.");
				return false;
			}

			AddressableAssetEntry entry = settings.FindAssetEntry(guid);
			bool changed = false;
			if (entry == null || entry.parentGroup != group)
			{
				entry = settings.CreateOrMoveEntry(guid, group, false, false);
				if (entry == null)
				{
					return false;
				}
				changed = true;
			}
			if (entry.address != assetPath)
			{
				entry.SetAddress(assetPath, false);
				changed = true;
			}
			if (!entry.labels.Contains(Label))
			{
				entry.SetLabel(Label, true, true, false);
				changed = true;
			}

			if (changed)
			{
				settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
			}
			return true;
		}
	}
}
