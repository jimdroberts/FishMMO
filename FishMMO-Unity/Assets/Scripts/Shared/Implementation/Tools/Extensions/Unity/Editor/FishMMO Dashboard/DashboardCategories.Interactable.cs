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
	/// <summary>The dashboard sidebar's Interactables group.</summary>
	internal sealed class InteractableDashboardCategories : IDashboardCategoryProvider
	{
		/// <inheritdoc />
		public int Order => 600;

		/// <inheritdoc />
		public void AddCategories(DashboardCategoryList list)
		{
			list.Add<DialogueTemplate>("Dialogues", "Interactables",
				"Assets/Templates/Entity/Interactables/Dialogues",
				"FishMMO/Dialogue/Dialogue Template");

			list.Add<ContainerTemplate>("Containers", "Interactables",
				"Assets/Templates/Entity/Interactables/Containers",
				"FishMMO/Character/Container");

			list.Add<GatheringNodeTemplate>("Gathering Nodes", "Interactables",
				"Assets/Templates/Entity/Interactables/GatheringNodes",
				"FishMMO/Interactable/Gathering Node");

			list.Add<MerchantTemplate>("Merchants", "Interactables",
				"Assets/Templates/Entity/Interactables/NPCs",
				"FishMMO/Character/Merchant");

			list.Add<ShrineTemplate>("Shrines", "Interactables",
				"Assets/Templates/Entity/Interactables/Shrines",
				"FishMMO/Interactable/Shrine");

			list.Add<LoreObjectTemplate>("Lore Objects", "Interactables",
				"Assets/Templates/Entity/Interactables/LoreObjects",
				"FishMMO/Interactable/Lore Object");

			list.Add<CapturePointTemplate>("Capture Points", "Interactables",
				"Assets/Templates/Entity/Interactables/CapturePoints",
				"FishMMO/Interactable/Capture Point");
		}
	}
}
#endif
