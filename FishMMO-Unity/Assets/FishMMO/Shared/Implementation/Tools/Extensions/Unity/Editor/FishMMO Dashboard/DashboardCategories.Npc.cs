#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.Shared
{
	/// <summary>The dashboard sidebar's NPCs group.</summary>
	internal sealed class NpcDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 500;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			/* The NPC prefabs themselves come first: that is what a designer is trying to make.
			 * Archetypes next, because one of them is every state and tuning value an
			 * AIController has; the individual state categories below are for authoring the
			 * pieces rather than for wiring a prefab. */
			list.Dashboard.RegisterNPCCategory();

			list.Add(new TemplateCategory
			{
				DisplayName = "AI Archetypes",
				Group = "NPCs",
				AssetType = typeof(AIArchetypeTemplate),
				DefaultAssetDirectory = AIAssetOrganizer.AI_ROOT + "/Archetypes",
				CreateAssetMenuName = "FishMMO/Character/NPC/AI/Archetype",
				GetGroupLabel = asset =>
				{
					// "Pet - Melee" / "Enemy - Caster" group under Pet / Enemy.
					string name = asset != null ? asset.name : string.Empty;
					int dash = name.IndexOf(" - ", StringComparison.Ordinal);
					return dash > 0 ? name.Substring(0, dash) : "Other";
				},
			});

			list.Add(new TemplateCategory
			{
				DisplayName = "AI Personalities",
				Group = "NPCs",
				AssetType = typeof(AICombatPersonality),
				DefaultAssetDirectory = AIAssetOrganizer.AI_ROOT + "/Personalities",
				CreateAssetMenuName = "FishMMO/Character/NPC/AI/Combat Personality",
				GetGroupLabel = asset =>
				{
					AICombatPersonality personality = asset as AICombatPersonality;
					return personality != null ? personality.Style.ToString() : "Unknown";
				},
				GetSortOrder = asset =>
				{
					AICombatPersonality personality = asset as AICombatPersonality;
					return personality != null ? (int)personality.Style : 0;
				},
			});

			list.Add(new TemplateCategory
			{
				DisplayName = "AI Aim Profiles",
				Group = "NPCs",
				AssetType = typeof(AIAimProfile),
				DefaultAssetDirectory = AIAssetOrganizer.AI_ROOT + "/Aim",
				CreateAssetMenuName = "FishMMO/Character/NPC/AI/Aim Profile",
				GetGroupLabel = asset =>
				{
					AIAimProfile profile = asset as AIAimProfile;
					return profile != null ? profile.AimPoint.ToString() : "Unknown";
				},
				GetSortOrder = asset =>
				{
					AIAimProfile profile = asset as AIAimProfile;
					return profile != null ? (int)profile.AimPoint : 0;
				},
			});

			list.Add(new TemplateCategory
			{
				DisplayName = "AI States",
				Group = "NPCs",
				AssetType = typeof(BaseAIState),
				DefaultAssetDirectory = AIAssetOrganizer.AI_ROOT + "/States",
				ConcreteTypes = new List<(string, Type)>
				{
					("Idle State", typeof(IdleState)),
					("Wander State", typeof(WanderState)),
					("Patrol State", typeof(PatrolState)),
					("Orbit State", typeof(OrbitState)),
					("Get Behind State", typeof(GetBehindState)),
					("Return Home State", typeof(ReturnHomeState)),
					("Retreat State", typeof(RetreatState)),
					("Pet Idle State", typeof(PetIdleState)),
					("Attacking State", typeof(BaseAttackingState)),
					("Melee Attacking State", typeof(MeleeAttackingState)),
					("Ranged Attacking State", typeof(RangedAttackingState)),
					("Caster Attacking State", typeof(CasterAttackingState)),
					("Healer Attacking State", typeof(HealerAttackingState)),
					("Defender Attacking State", typeof(DefenderAttackingState)),
					("Rogue Attacking State", typeof(RogueAttackingState)),
					("Pet Attacking State", typeof(PetAttackingState)),
				},
				GetGroupLabel = asset =>
				{
					if (asset is BaseAttackingState) return "Combat";
					if (asset is PetIdleState) return "Pet";
					if (asset is OrbitState || asset is GetBehindState || asset is RetreatState) return "Combat Positioning";
					return "Movement";
				},
				GetSortOrder = asset =>
				{
					if (asset is BaseAttackingState) return 0;
					if (asset is PetIdleState) return 10;
					if (asset is OrbitState || asset is GetBehindState || asset is RetreatState) return 20;
					return 30;
				},
			});

			list.Add(new TemplateCategory
			{
				DisplayName = "AI Ability Rotations",
				Group = "NPCs",
				AssetType = typeof(AIAbilityRotation),
				DefaultAssetDirectory = AIAssetOrganizer.AI_ROOT + "/Rotations",
				CreateAssetMenuName = "FishMMO/Character/NPC/AI/Ability Rotation",
			});

			list.Add(new TemplateCategory
			{
				DisplayName = "AI Conditions",
				Group = "NPCs",
				AssetType = typeof(AIAbilityCondition),
				DefaultAssetDirectory = AIAssetOrganizer.AI_ROOT + "/Conditions",
				ConcreteTypes = new List<(string, Type)>
				{
					("Health Condition", typeof(AIHealthCondition)),
					("Distance Condition", typeof(AIDistanceCondition)),
					("Buff Condition", typeof(AIBuffCondition)),
					("Random Condition", typeof(AIRandomCondition)),
				},
			});

			list.Add(new TemplateCategory
			{
				DisplayName = "AI LOD Settings",
				Group = "NPCs",
				AssetType = typeof(AILodSettings),
				DefaultAssetDirectory = AIAssetOrganizer.AI_ROOT + "/LOD",
				CreateAssetMenuName = "FishMMO/Character/NPC/AI/LOD Settings",
			});

			list.Add<NPCGuildTemplate>("NPC Guilds", "NPCs",
				"Assets/Templates/Entity/NPCs",
				"FishMMO/Character/NPC/NPC Guild");

			list.Add<NPCAttributeDatabase>("NPC Attribute Databases", "NPCs",
				"Assets/Templates/Entity/NPCs/Attributes",
				"FishMMO/Character/NPC/Attribute/Database");

			list.Add<BossScript>("Boss Scripts", "NPCs",
				AIAssetOrganizer.AI_ROOT + "/Boss",
				"FishMMO/Character/NPC/AI/Boss Script");

			list.Add(new TemplateCategory
			{
				DisplayName = "Behavior Trees",
				Group = "NPCs",
				AssetType = typeof(AIBehaviorTree),
				DefaultAssetDirectory = AIAssetOrganizer.AI_ROOT + "/BehaviorTrees",
				CreateAssetMenuName = "FishMMO/Character/NPC/AI/Behavior Tree/Behavior Tree",
			});

			list.Add(new TemplateCategory
			{
				DisplayName = "Behavior Nodes",
				Group = "NPCs",
				AssetType = typeof(AIBehaviorNode),
				DefaultAssetDirectory = AIAssetOrganizer.AI_ROOT + "/BehaviorNodes",
				ConcreteTypes = new List<(string, Type)>
				{
					("Selector", typeof(AISelector)),
					("Sequence", typeof(AISequence)),
					("Inverter", typeof(AIInverter)),
					("Repeater", typeof(AIRepeater)),
					("Condition", typeof(AIConditionNode)),
					("State Transition", typeof(AIStateTransitionNode)),
					("Has Target", typeof(AIHasTargetNode)),
					("Is Dead", typeof(AIIsDeadNode)),
					("Group In Combat", typeof(AIGroupInCombatNode)),
					("Adopt Group Target", typeof(AIAdoptGroupTargetNode)),
				},
			});

			list.AddToolPage(DashboardToolAttribute.AITools, "NPCs");
		}
	}
}
#endif
