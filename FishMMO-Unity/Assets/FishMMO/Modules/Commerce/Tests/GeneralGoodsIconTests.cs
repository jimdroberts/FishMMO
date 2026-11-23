using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Everything the General Goods merchant sells on its Abilities and Effects tabs has an icon.
	/// </summary>
	/// <remarks>
	/// The two kinds hold their icon differently: an ability an <c>AssetReferenceSprite</c> (loaded into the cache), an effect
	/// (<see cref="AbilityEvent"/>, a Trigger) a direct <see cref="Sprite"/>. The icon generator once wrote an
	/// AssetReference block into the 36 effects; Unity read it as no sprite and the Effects tab drew none.
	/// </remarks>
	[TestFixture]
	public class GeneralGoodsIconTests
	{
		private const string MerchantPath = "Assets/FishMMO/Modules/Commerce/Templates/NPCs/Human/Merchants/GeneralGoods/GeneralGoods.asset";

		[Test]
		public void EveryEffectAndAbilityOnSale_HasAnIcon()
		{
			MerchantTemplate merchant = AssetDatabase.LoadAssetAtPath<MerchantTemplate>(MerchantPath);
			Assert.That(merchant, Is.Not.Null, MerchantPath);

			var missing = new List<string>();
			foreach (AbilityEvent effect in merchant.AbilityEvents)
			{
				if (effect != null && effect.Icon == null)
				{
					missing.Add("effect " + effect.name);
				}
			}
			foreach (AbilityTemplate ability in merchant.Abilities)
			{
				if (ability == null)
				{
					continue;
				}
				SerializedProperty guid = new SerializedObject(ability).FindProperty("icon.m_AssetGUID");
				if (guid == null || string.IsNullOrEmpty(guid.stringValue) || AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid.stringValue)) == null)
				{
					missing.Add("ability " + ability.name);
				}
			}

			Assert.That(merchant.AbilityEvents.Count, Is.GreaterThan(0), "General Goods sells effects");
			Assert.That(missing, Is.Empty, string.Join(", ", missing));
		}
	}
}
