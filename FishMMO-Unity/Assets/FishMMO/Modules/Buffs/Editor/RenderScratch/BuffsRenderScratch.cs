using System.Collections.Generic;
using FishMMO.Shared;
using UnityEditor;
using UnityEngine;

namespace FishMMO.RenderScratch
{
	/// <summary>The Buffs module's part of the panel render rig: the buff and debuff strips in the HUD probes, and the buff tooltip page.</summary>
	public static class BuffsRenderScratch
	{
		/// <summary>The strips' layouts.</summary>
		private const string BuffUxml = "Assets/FishMMO/Modules/Buffs/Client/Buff/UIBuff.uxml";
		private const string DebuffUxml = "Assets/FishMMO/Modules/Buffs/Client/Buff/UIDebuff.uxml";

		[InitializeOnLoadMethod]
		private static void Install()
		{
			Seed.BuffLoader = LoadBuffFixtures;
			// The strips sit where they did in the HUD lists: after the resource bars, ahead of the crosshair.
			if (!InterfaceScalingRender.Hud.Exists(m => m.Uxml == BuffUxml))
			{
				int crosshair = InterfaceScalingRender.Hud.FindIndex(m => m.PanelType == typeof(FishMMO.Client.UITKCrosshair));
				int at = crosshair < 0 ? InterfaceScalingRender.Hud.Count : crosshair;
				InterfaceScalingRender.Hud.Insert(at, new InterfaceScalingRender.HudMount { Uxml = DebuffUxml, PanelType = typeof(FishMMO.Client.UITKDebuff) });
				InterfaceScalingRender.Hud.Insert(at, new InterfaceScalingRender.HudMount { Uxml = BuffUxml, PanelType = typeof(FishMMO.Client.UITKBuff) });
			}
			if (!HudOverlapProbe.Mounts.Exists(m => m.Name == "UIBuff"))
			{
				int crosshair = HudOverlapProbe.Mounts.FindIndex(m => m.Name == "UICrosshair");
				int at = crosshair < 0 ? HudOverlapProbe.Mounts.Count : crosshair;
				HudOverlapProbe.Mounts.Insert(at, new HudOverlapProbe.Mount { Name = "UIDebuff", Uxml = DebuffUxml, Selector = "buff-root", PanelType = typeof(FishMMO.Client.UITKDebuff) });
				HudOverlapProbe.Mounts.Insert(at, new HudOverlapProbe.Mount { Name = "UIBuff", Uxml = BuffUxml, Selector = "buff-root", PanelType = typeof(FishMMO.Client.UITKBuff) });
			}
			if (!TooltipGalleryRender.ExtraPages.Exists(p => p.label == "Tooltips-buffs"))
			{
				TooltipGalleryRender.ExtraPages.Add(("Tooltips-buffs", () =>
				{
					TooltipSubjects.Load();
					TooltipSubjects.RegisterAll<BaseBuffTemplate>("Assets/FishMMO/Modules/Buffs/Templates");
					return Buffs();
				}));
			}
		}

		/// <summary>Every buff kind, including the three the project ships no example of.</summary>
		public static List<(string, TooltipContent)> Buffs()
		{
			List<(string, TooltipContent)> cards = new List<(string, TooltipContent)>();

			foreach (string guid in AssetDatabase.FindAssets("t:BaseBuffTemplate", new[] { "Assets/FishMMO/Modules/Buffs/Templates" }))
			{
				BaseBuffTemplate buff = AssetDatabase.LoadAssetAtPath<BaseBuffTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (buff != null)
				{
					cards.Add(($"{buff.GetType().Name} — {buff.name}", buff.BuildContent()));
				}
			}

			/* The kinds with no shipped asset. Each has its own tooltip override, so leaving them
			 * out would mean the gallery could not show whether they render at all. */
			cards.Add(("StateBuffTemplate (mock)", MockState().BuildContent()));
			cards.Add(("DamageNegationBuffTemplate (mock)", MockNegation().BuildContent()));
			cards.Add(("DeflectBuffTemplate (mock)", MockDeflect().BuildContent()));
			cards.Add(("CompositeBuffTemplate (mock)", MockComposite().BuildContent()));

			return cards;
		}


		private static StateBuffTemplate MockState()
		{
			StateBuffTemplate buff = TooltipSubjects.Temp<StateBuffTemplate>("Dazed");
			buff.Description = "Leaves the target unable to act.";
			buff.Flag = CharacterFlags.IsStunned;
			return buff;
		}


		private static DamageNegationBuffTemplate MockNegation()
		{
			DamageNegationBuffTemplate buff = TooltipSubjects.Temp<DamageNegationBuffTemplate>("Stone Ward");
			buff.Description = "Absorbs the next blows to land.";
			buff.Amount = 250;
			return buff;
		}


		private static DeflectBuffTemplate MockDeflect()
		{
			DeflectBuffTemplate buff = TooltipSubjects.Temp<DeflectBuffTemplate>("Parry Stance");
			buff.Description = "Turns aside what comes at the front.";
			buff.DeflectAngleDegrees = 120.0f;
			buff.MaxDeflections = 3;
			return buff;
		}


		private static CompositeBuffTemplate MockComposite()
		{
			CompositeBuffTemplate buff = TooltipSubjects.Temp<CompositeBuffTemplate>("Tideborn Blessing");
			buff.Description = "The reef's favour, in several forms at once.";
			buff.Flags = new List<CharacterFlags> { CharacterFlags.IsFrozen };
			return buff;
		}


		/// <remarks>
		/// AddToCache is not called on load — the shipped path is an addressables loader at boot,
		/// so an asset pulled from the AssetDatabase has ID 0 and never enters the lookup.
		/// The project ships no debuff templates, so those are created in memory.
		/// </remarks>
		private static void LoadBuffFixtures()
		{
			BaseBuffTemplate armor = AssetDatabase.LoadAssetAtPath<BaseBuffTemplate>(
				"Assets/FishMMO/Modules/Buffs/Templates/Minor Increase Armor.asset");
			BaseBuffTemplate haste = AssetDatabase.LoadAssetAtPath<BaseBuffTemplate>(
				"Assets/FishMMO/Modules/Buffs/Templates/Minor Increase Move Speed.asset");

			if (armor != null) { armor.AddToCache(armor.name); Seed.BuffArmor = armor.ID; }
			if (haste != null) { haste.AddToCache(haste.name); Seed.BuffHaste = haste.ID; }

			Seed.DebuffPoison = Debuff("Creeping Venom");
			Seed.DebuffSlow = Debuff("Mirebound");
			Seed.DebuffBurn = Debuff("Emberbrand");
		}


		private static int Debuff(string name)
		{
			AttributeBuffTemplate t = ScriptableObject.CreateInstance<AttributeBuffTemplate>();
			t.name = name;
			t.hideFlags = HideFlags.HideAndDontSave;
			t.IsDebuff = true;
			t.AddToCache(name);
			return t.ID;
		}
	}
}
