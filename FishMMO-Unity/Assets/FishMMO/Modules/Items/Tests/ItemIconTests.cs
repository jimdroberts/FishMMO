using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every item template has an icon, and the icon is a sprite that exists.
	/// </summary>
	/// <remarks>
	/// 2026-11-21: 300 of 306 items had none, and the other 6 held the icon field's older direct-sprite form, which the
	/// AssetReferenceSprite cannot read, so they showed none either. Icons are drawn by FishMMO-Art/Icons/generate_item_icons.py
	/// (run with --assign for a new item).
	/// </remarks>
	[TestFixture]
	public class ItemIconTests
	{
		[Test]
		public void EveryItemTemplate_HasAnIconSprite()
		{
			var missing = new List<string>();
			int checkedCount = 0;
			foreach (string guid in AssetDatabase.FindAssets("t:BaseItemTemplate", new[] { "Assets/FishMMO" }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				BaseItemTemplate item = AssetDatabase.LoadAssetAtPath<BaseItemTemplate>(path);
				if (item == null)
				{
					continue;
				}
				++checkedCount;
				SerializedProperty icon = new SerializedObject(item).FindProperty("icon.m_AssetGUID");
				string iconPath = icon != null ? AssetDatabase.GUIDToAssetPath(icon.stringValue) : null;
				if (string.IsNullOrEmpty(iconPath) || AssetDatabase.LoadAssetAtPath<Sprite>(iconPath) == null)
				{
					missing.Add(Path.GetFileNameWithoutExtension(path));
				}
			}
			Assert.That(checkedCount, Is.GreaterThan(0), "no item templates found");
			Assert.That(missing, Is.Empty, "items without an icon (run generate_item_icons.py --assign): " + string.Join(", ", missing));
		}
	}
}
