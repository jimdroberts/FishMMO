using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// The Items module's part of the panel render rig (moved with the module, 2026-11-20): the inventory, bank and equipment
	/// windows in the all-panels render, the grid fit probe and the interface scaling render. The rig's characters carry the
	/// core's fake containers (<see cref="FakeInventory"/>, <see cref="FakeBank"/>, <see cref="FakeEquipment"/>), which the
	/// windows read through the container contracts.
	/// </summary>
	public static class ItemsRenderScratch
	{
		/// <summary>The inventory window's layout.</summary>
		public const string InventoryUxml = "Assets/FishMMO/Modules/Items/Client/UIInventory.uxml";

		/// <summary>The bank window's layout.</summary>
		public const string BankUxml = "Assets/FishMMO/Modules/Items/Client/UIBank.uxml";

		/// <summary>The character sheet the equipment window mounts (the core's: the inspect window shares it).</summary>
		public const string CharacterSheetUxml = "Assets/FishMMO/Client/GUI/World/CharacterSheet/UICharacterSheet.uxml";

		[InitializeOnLoadMethod]
		private static void Install()
		{
			AllPanelsRender.ExtraPopulators["UIInventory"] = Inventory;
			AllPanelsRender.ExtraPopulators["UIBank"] = Bank;
			AllPanelsRender.CharacterSheetEquipment = Equipment;

			// In the order the probe always checked them.
			AddGridFit(InventoryUxml, Inventory);
			AddGridFit(BankUxml, Bank);
			AddGridFit(CharacterSheetUxml, Equipment);

			if (!InterfaceScalingRender.Windows.Exists(w => w.uxml == CharacterSheetUxml))
			{
				InterfaceScalingRender.Windows.Add((CharacterSheetUxml, 100, Equipment));
			}
			if (!InterfaceScalingRender.Windows.Exists(w => w.uxml == InventoryUxml))
			{
				InterfaceScalingRender.Windows.Add((InventoryUxml, 101, Inventory));
			}
		}

		private static void AddGridFit(string uxml, System.Action<GameObject, UIDocument> populate)
		{
			if (!GridFitProbe.Windows.Exists(w => w.uxml == uxml))
			{
				GridFitProbe.Windows.Add((uxml, populate));
			}
		}

		/// <summary>Populates the inventory window for the rig's character.</summary>
		public static void Inventory(GameObject h, UIDocument d) => Panels.Character<UITKInventory>(h, d);

		/// <summary>Populates the bank window for the rig's character.</summary>
		public static void Bank(GameObject h, UIDocument d) => Panels.Character<UITKBank>(h, d);

		/// <summary>
		/// The player's own character sheet — the same window <see cref="Panels.Inspect"/> captures, showing
		/// the viewer's own character and the attribute list the inspect variant holds back.
		/// </summary>
		/// <remarks>
		/// Not the <c>Character&lt;T&gt;</c> one-liner, because this window draws the character: without
		/// a body and a preview camera on the rig its viewport renders the label over an empty frame,
		/// and the pair of captures stops being a comparison — the difference between them would be the
		/// harness's rigging rather than the two components' options.
		/// <para>
		/// The rigging comes BEFORE the panel starts, which is the order the client lives in: a
		/// character has its mesh root and its camera long before anybody opens this window. It also
		/// matters to the framing — the preview frames itself on the first tick that can see a body and
		/// keeps what it measured, so a body attached afterwards is photographed by a camera that was
		/// framed without it.
		/// </para>
		/// </remarks>
		public static void Equipment(GameObject h, UIDocument d)
		{
			PlayerCharacter character = Rig.Build(h);
			Rig.AttachPreview(character);

			UITKEquipment panel = h.AddComponent<UITKEquipment>();
			panel.Document = d;
			panel.SetCharacter(character);
			panel.OnStarting();
			Panels.Tick(panel);
		}
	}
}
