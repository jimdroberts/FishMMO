#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/* Namespace is FishMMO.Shared, matching the other 15 editor scripts in this folder. Declaring
   FishMMO.Shared.Editor here made the name "Editor" resolve to that namespace inside every file
   in the assembly, so every existing `Editor` type reference broke with CS0118 — 15 errors in
   files this change never touched. */
namespace FishMMO.Shared
{
	/// <summary>
	/// Reports equippable item templates whose <see cref="ItemSlot"/> disagrees with the folder
	/// they are filed under.
	/// </summary>
	/// <remarks>
	/// This exists because a slot mismatch has no symptom a developer would recognise. Inserting
	/// <c>Shoulders</c> into the middle of <see cref="ItemSlot"/> renumbered every value below
	/// <c>Hands</c>, and because templates serialize the integer rather than the name, every
	/// already-authored asset silently changed meaning: leggings became shoulders, boots became
	/// legs, a sword became feet. Nothing threw, nothing logged, and every value was still a legal
	/// slot — the only evidence was a sword equipping to a character's feet.
	/// <para>
	/// The folder is the cross-check because it records what a human meant when they filed the
	/// asset, independently of the number stored inside it. Two independent statements of the same
	/// fact are what make the drift detectable at all.
	/// </para>
	/// <para>
	/// A mismatch is reported, not corrected. A template deliberately filed somewhere that does
	/// not match its slot is legitimate — this cannot tell that apart from a mistake, so it says
	/// what it found and leaves the decision alone.
	/// </para>
	/// </remarks>
	public static class EquipmentSlotValidator
	{
		[MenuItem("FishMMO/Validate/Equipment Item Slots", priority = 200)]
		public static void Validate()
		{
			string[] guids = AssetDatabase.FindAssets($"t:{nameof(EquippableItemTemplate)}");
			var mismatches = new List<string>();
			int checkedCount = 0;
			int unmatchedFolder = 0;

			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				EquippableItemTemplate template = AssetDatabase.LoadAssetAtPath<EquippableItemTemplate>(path);
				if (template == null)
				{
					continue;
				}

				++checkedCount;

				// Walk up from the asset: the first folder naming a slot is the claim to test.
				ItemSlot? claimed = null;
				DirectoryInfo dir = Directory.GetParent(path);
				while (dir != null && claimed == null)
				{
					if (System.Enum.TryParse(dir.Name, ignoreCase: true, out ItemSlot parsed))
					{
						claimed = parsed;
					}
					dir = dir.Parent;
				}

				if (claimed == null)
				{
					++unmatchedFolder;
					continue;
				}

				if (claimed.Value != template.Slot)
				{
					mismatches.Add(
						$"  {template.name}\n" +
						$"      filed under : {claimed.Value} ({(int)claimed.Value})\n" +
						$"      slot says   : {template.Slot} ({(int)template.Slot})\n" +
						$"      {path}");
				}
			}

			var sb = new StringBuilder();
			sb.AppendLine($"[EquipmentSlotValidator] Checked {checkedCount} equippable template(s).");

			if (mismatches.Count > 0)
			{
				sb.AppendLine($"\n{mismatches.Count} disagree with the folder they are filed under:\n");
				sb.AppendLine(string.Join("\n", mismatches));
				sb.AppendLine("\nIf the folder is right, fix the template's Slot. If the template is right, move the asset.");
			}
			else
			{
				sb.AppendLine("Every template whose folder names a slot agrees with it.");
			}

			if (unmatchedFolder > 0)
			{
				sb.AppendLine($"\n{unmatchedFolder} template(s) are not filed under a slot-named folder and were not checked.");
			}

			if (mismatches.Count > 0)
			{
				Debug.LogWarning(sb.ToString());
			}
			else
			{
				Debug.Log(sb.ToString());
			}
		}
	}
}
#endif
