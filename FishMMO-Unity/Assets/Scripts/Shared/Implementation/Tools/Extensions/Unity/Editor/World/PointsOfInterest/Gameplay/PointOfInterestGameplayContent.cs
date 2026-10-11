#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.Core;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The shared assets the point-of-interest gameplay features point at, created on demand and idempotently: the
	/// "Places Discovered" exploration achievement and its "Site Discovered" region trigger. And the one entry point
	/// that makes all the gameplay content (<see cref="GenerateAll"/>).
	/// </summary>
	public static class PointOfInterestGameplayContent
	{
		public const string AchievementPath = "Assets/Templates/Entity/Achievements/Exploration/Places Discovered.asset";
		public const string DiscoveryTriggerPath = "Assets/Templates/Entity/ECA/Interactions/Site Discovered.asset";

		/// <summary>
		/// Everything the generated points of interest need before a cut: gathering content, the NPC table, the discovery
		/// achievement and trigger. Headless:
		/// <c>-executeMethod FishMMO.Shared.WorldDesign.PointOfInterestGameplayContent.GenerateAll</c>.
		/// </summary>
		[DashboardTool(DashboardToolAttribute.Maintenance, "Generate POI Gameplay Content", Section = "Points of Interest", Order = 9,
			Tooltip = "Creates or updates the gathering items, nodes and prefabs, the POI NPC table, and the Places Discovered achievement with its Site Discovered trigger.")]
		public static void GenerateAll()
		{
			EnsureDiscoveryContent();
			PointOfInterestNpcTable.CreateAsset();
			GatheringContentGenerator.Generate();
			AssetDatabase.SaveAssets();
		}

		/// <summary>
		/// The "Site Discovered" trigger (<see cref="DiscoverSiteAction"/>, stopping the chain when the site was already
		/// found, then +1 to "Places Discovered"), made with its achievement if missing. Null only if an asset could not be
		/// written.
		/// </summary>
		public static Trigger EnsureDiscoveryContent()
		{
			AchievementTemplate achievement = AssetDatabase.LoadAssetAtPath<AchievementTemplate>(AchievementPath);
			if (achievement == null)
			{
				EnsureFolder(Path.GetDirectoryName(AchievementPath).Replace('\\', '/'));
				achievement = ScriptableObject.CreateInstance<AchievementTemplate>();
				achievement.Category = AchievementCategory.Exploration;
				achievement.Description = "Places of interest discovered across the world.";
				achievement.Tiers = new List<AchievementTier>
				{
					new AchievementTier { Value = 1, TierCompleteMessage = "You discovered your first place of interest." },
					new AchievementTier { Value = 10, TierCompleteMessage = "Ten places discovered." },
					new AchievementTier { Value = 25, TierCompleteMessage = "Twenty-five places discovered." },
					new AchievementTier { Value = 50, TierCompleteMessage = "Fifty places discovered." },
					new AchievementTier { Value = 100, TierCompleteMessage = "A hundred places discovered." },
				};
				AssetDatabase.CreateAsset(achievement, AchievementPath);
				WorldEditorAssets.RegisterAddressable(achievement);
			}

			Trigger trigger = AssetDatabase.LoadAssetAtPath<Trigger>(DiscoveryTriggerPath);
			if (trigger == null)
			{
				EnsureFolder(Path.GetDirectoryName(DiscoveryTriggerPath).Replace('\\', '/'));
				trigger = ScriptableObject.CreateInstance<Trigger>();
				trigger.OnConditionsMetActions.Add(new DiscoverSiteAction() { StopChainOnFailure = true });
				trigger.OnConditionsMetActions.Add(new AchievementIncrementAction()
				{
					AchievementTemplate = achievement,
					AmountValue = new ConstantValue() { Amount = 1 },
				});
				AssetDatabase.CreateAsset(trigger, DiscoveryTriggerPath);
				WorldEditorAssets.RegisterAddressable(trigger);
				AssetDatabase.SaveAssets();
			}
			return trigger;
		}

		private static void EnsureFolder(string folder)
		{
			if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
			{
				return;
			}
			string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
		}
	}
}
#endif
