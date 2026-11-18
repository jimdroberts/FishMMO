using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every character attribute template is an Addressables entry.
	/// </summary>
	/// <remarks>
	/// A template gets its runtime ID when the Addressables load registers it (<c>CachedScriptableObject.AddToCache</c>).
	/// One the attribute databases list but no group holds keeps ID 0, so <c>CharacterAttribute</c>'s constructor finds
	/// no template and every character that carries it throws in <c>Awake</c>: on 2026-11-17 the new "Pet Slots"
	/// attribute had no entry, and every NPC spawn and every player login on the scene server failed at 90% loading.
	/// </remarks>
	[TestFixture]
	public class CharacterAttributeAddressablesTests
	{
		[Test]
		public void EveryCharacterAttributeTemplate_HasAnAddressablesEntry()
		{
			var addressable = new HashSet<string>();
			foreach (string group in Directory.GetFiles(ProjectFiles.Folder("Assets/AddressableAssetsData/AssetGroups"), "*.asset"))
			{
				foreach (Match m in Regex.Matches(ProjectFiles.ReadAllText(group), @"m_GUID: ([0-9a-f]{32})"))
				{
					addressable.Add(m.Groups[1].Value);
				}
			}

			string[] guids = AssetDatabase.FindAssets("t:" + nameof(CharacterAttributeTemplate));
			LogAssert.IsTrue(guids.Length > 10, "the project's character attributes were found");
			List<string> missing = guids.Where(g => !addressable.Contains(g)).Select(AssetDatabase.GUIDToAssetPath).ToList();
			LogAssert.IsTrue(missing.Count == 0,
				"these attributes have no Addressables entry, so the server never gives them an ID and every character carrying them throws: " +
				string.Join(", ", missing));
		}
	}
}
