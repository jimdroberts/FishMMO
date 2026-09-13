using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Fills the rigged character with plausible contents, using the project's own template assets
	/// wherever they exist so icons, names and tooltips are the shipped ones.
	/// </summary>
	/// <remarks>
	/// Templates are registered with <c>AddToCache</c> on load. That is not automatic: the shipped
	/// path is an addressables loader at boot, so an asset pulled from the AssetDatabase has ID 0
	/// and never enters the lookup the panels use.
	/// </remarks>
	public static class Fixtures
	{
		public static void Apply(ICharacter character)
		{
			Items(character);
			Attributes(character);
			Achievements(character);
			Factions(character);
			Friends(character);
		}

		/// <summary>Loads every asset of a type under a folder and registers it in the ID cache.</summary>
		private static List<T> Load<T>(string folder) where T : UnityEngine.Object
		{
			List<T> loaded = new List<T>();
			foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder }))
			{
				T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
				if (asset == null) { continue; }

				if (asset is ICachedObject)
				{
					// Same registration the boot-time loader performs.
					RegisterInCache(asset);
				}
				loaded.Add(asset);
			}
			return loaded;
		}

		private static void RegisterInCache(UnityEngine.Object asset)
		{
			var method = asset.GetType().GetMethod("AddToCache",
				System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
			try { method?.Invoke(asset, new object[] { asset.name }); } catch { }
		}

		// ── Items ───────────────────────────────────────────────────

		private static void Items(ICharacter character)
		{
			/* Attributes first: an ItemAttribute resolves its template by ID through the same cache,
			 * so without these registered every generated attribute carries a null Template and
			 * building an item's tooltip throws before any panel gets to draw it. */
			Load<ItemAttributeTemplate>("Assets/Templates/Entity/Items/ItemAttributes");

			List<BaseItemTemplate> templates = Load<BaseItemTemplate>("Assets/Templates/Entity/Items");
			if (templates.Count == 0) { return; }

			long id = 5000;

			if (character.TryGet(out IEquipmentController equipment))
			{
				// One item per slot the project actually ships a template for.
				foreach (BaseItemTemplate t in templates)
				{
					if (!TryResolveSlot(t, out ItemSlot slot)) { continue; }
					equipment.SetItemSlot(new Item(id++, 0, t, 1), (int)slot);
				}
			}

			if (character.TryGet(out IInventoryController inventory))
			{
				// A partly-filled bag: an empty slot is what the player drops onto, so leaving
				// gaps is more representative than filling every slot.
				int slot = 0;
				foreach (BaseItemTemplate t in templates)
				{
					if (slot >= inventory.Items.Count) { break; }
					inventory.SetItemSlot(new Item(id++, 0, t, (uint)UnityEngine.Random.Range(1, 4)), slot);
					slot += 2;
				}
			}

			if (character.TryGet(out IBankController bank))
			{
				int slot = 0;
				foreach (BaseItemTemplate t in templates)
				{
					if (slot >= bank.Items.Count) { break; }
					bank.SetItemSlot(new Item(id++, 0, t, (uint)UnityEngine.Random.Range(1, 9)), slot);
					slot += 3;
				}
			}
		}

		/// <summary>
		/// Reads the equipment slot an item template declares.
		/// </summary>
		/// <remarks>
		/// <c>Slot</c> is a public FIELD on <c>EquippableItemTemplate</c>, not a property. The
		/// property-only lookup this used to do matched nothing, on any template, ever — so every
		/// equipment container in every capture came out empty, and a panel that reads one read an
		/// empty one. Both member kinds are tried so the seam survives the field becoming a
		/// property, which is the obvious tidy-up somebody will eventually make.
		/// </remarks>
		private static bool TryResolveSlot(BaseItemTemplate template, out ItemSlot slot)
		{
			slot = default;
			object value = null;

			PropertyInfo prop = template.GetType().GetProperty("Slot")
				?? template.GetType().GetProperty("EquipmentSlot");
			if (prop != null)
			{
				try { value = prop.GetValue(template); } catch { }
			}

			if (!(value is ItemSlot))
			{
				for (Type t = template.GetType(); t != null && !(value is ItemSlot); t = t.BaseType)
				{
					FieldInfo field = t.GetField("Slot", BindingFlags.Public | BindingFlags.Instance)
						?? t.GetField("EquipmentSlot", BindingFlags.Public | BindingFlags.Instance);
					if (field != null)
					{
						try { value = field.GetValue(template); } catch { }
					}
				}
			}

			if (value is ItemSlot s) { slot = s; return true; }
			return false;
		}

		// ── Attributes ──────────────────────────────────────────────

		private static void Attributes(ICharacter character)
		{
			if (!character.TryGet(out ICharacterAttributeController controller)) { return; }
			FakeAttributes fake = controller as FakeAttributes;

			List<CharacterAttributeTemplate> templates =
				Load<CharacterAttributeTemplate>("Assets/Templates/Entity/CharacterAttributes");

			CharacterAttributeTemplate health = templates.FirstOrDefault(t => t.name == "Health");
			CharacterAttributeTemplate mana = templates.FirstOrDefault(t => t.name == "Mana");
			CharacterAttributeTemplate stamina = templates.FirstOrDefault(t => t.name == "Stamina");

			if (health != null) { controller.SetResourceAttribute(health.ID, 2400, 1968f); }
			if (mana != null) { controller.SetResourceAttribute(mana.ID, 1750, 1120f); }
			if (stamina != null) { controller.SetResourceAttribute(stamina.ID, 900, 731f); }

			fake?.BindResources(health?.ID ?? -1, mana?.ID ?? -1, stamina?.ID ?? -1);

			// Everything else gets a plausible flat value so stat lists are not empty.
			int roll = 12;
			foreach (CharacterAttributeTemplate t in templates)
			{
				if (t == health || t == mana || t == stamina) { continue; }
				controller.SetAttribute(t.ID, roll);
				roll = 6 + (roll * 3) % 87;
			}
		}

		// ── Achievements ────────────────────────────────────────────

		private static void Achievements(ICharacter character)
		{
			if (!character.TryGet(out IAchievementController controller)) { return; }

			List<AchievementTemplate> templates =
				Load<AchievementTemplate>("Assets/Templates/Entity/Achievements");

			byte tier = 0;
			uint value = 5;
			foreach (AchievementTemplate t in templates)
			{
				controller.SetAchievement(t.ID, tier, value);
				tier = (byte)((tier + 1) % 3);
				value = value * 4 + 13;
			}
		}

		// ── Factions ────────────────────────────────────────────────

		private static void Factions(ICharacter character)
		{
			if (!character.TryGet(out IFactionController controller)) { return; }

			List<FactionTemplate> templates = Load<FactionTemplate>("Assets/Templates/Entity/Factions");

			// A spread across allied / neutral / hostile so all three groups render.
			int[] standings = { 8200, -4500, 0, 15000, -12000, 350, 0 };
			for (int i = 0; i < templates.Count; ++i)
			{
				controller.SetFaction(templates[i].ID, standings[i % standings.Length]);
			}
		}

		// ── Friends ─────────────────────────────────────────────────

		private static void Friends(ICharacter character)
		{
			if (!character.TryGet(out IFriendController controller)) { return; }
			foreach ((long id, string _) in Seed.People)
			{
				if (id != 1001) { controller.AddFriend(id); }
			}
		}
	}
}
