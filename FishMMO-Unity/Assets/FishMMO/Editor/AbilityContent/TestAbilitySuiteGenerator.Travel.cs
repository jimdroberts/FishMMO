using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.AbilityContent
{
	/// <summary>
	/// The travel spells (issue #241): Portal to Bind, Mark, Recall, Gate and Call to Arms.
	/// </summary>
	/// <remarks>
	/// <para><b>The assets belong to the Travel module</b> (<see cref="TravelAbilityFolder"/>), because their effects are
	/// Travel actions: deleting the module deletes its spells with it.</para>
	/// <para><b>No compile-time reference to Travel.</b> This assembly is core editor content; naming a Travel type would make
	/// it fail to compile without the module. The actions are found by name, and without the module the generator reports
	/// that and writes nothing.</para>
	/// </remarks>
	public static partial class TestAbilitySuiteGenerator
	{
		public const string TravelAbilityFolder = "Assets/FishMMO/Modules/Travel/Templates/Abilities";
		public const string TravelEventFolder = "Assets/FishMMO/Modules/Travel/Templates/Abilities/Events";

		/// <summary>Assembly-qualified names of the Travel module's spell actions.</summary>
		private const string PortalToBindActionType = "FishMMO.Shared.PortalToBindAction, FishMMO.Travel";
		private const string MarkLocationActionType = "FishMMO.Shared.MarkLocationAction, FishMMO.Travel";
		private const string RecallActionType = "FishMMO.Shared.RecallAction, FishMMO.Travel";
		private const string OpenGateActionType = "FishMMO.Shared.OpenGateAction, FishMMO.Travel";
		private const string CallToArmsActionType = "FishMMO.Shared.CallToArmsAction, FishMMO.Travel";

		// Tuning, named once (first pass): long enough to be stopped by an attacker, since damage breaks it.
		private const float PortalToBindCastSeconds = 5f, PortalToBindCooldownSeconds = 120f;
		private const int PortalToBindManaCost = 20;
		private const float MarkCastSeconds = 3f, MarkCooldownSeconds = 10f, RecallCastSeconds = 5f, RecallCooldownSeconds = 120f;
		private const int MarkManaCost = 15, RecallManaCost = 20;
		// Gate: a short cast, then the choice on the map (the action's ChooseSeconds); the cooldown starts when the cast ends.
		private const float GateCastSeconds = 2f, GateCooldownSeconds = 60f, CallToArmsCastSeconds = 3f, CallToArmsCooldownSeconds = 300f;
		private const int GateManaCost = 30, CallToArmsManaCost = 40;

		// The spells' colours: home gold, the mark's arcane violet (shared with the blink), the gate's lavender (its portal's glow), a rally's red.
		private static readonly Palette HomePalette = new Palette(new Color(1.0f, 0.72f, 0.25f), new Color(1.0f, 0.96f, 0.82f));
		private static readonly Palette GatePalette = new Palette(new Color(0.45f, 0.35f, 1.0f), new Color(0.86f, 0.80f, 1.0f));
		private static readonly Palette RallyPalette = new Palette(new Color(0.85f, 0.14f, 0.08f), new Color(1.0f, 0.80f, 0.30f));

		[DashboardTool(DashboardToolAttribute.Maintenance, "Generate Travel Spells", Section = "Ability Content", Order = 4,
			Tooltip = "Generates the travel spells (Portal to Bind, Mark, Recall, Gate, Call to Arms) into the Travel module, registers them as addressables and adds them to the General Goods merchant. Needs the Travel module. Touches nothing else.")]
		public static void GenerateTravelSpellsOnly()
		{
			written.Clear();
			problems.Clear();
			try
			{
				LoadDependencies();
				if (problems.Count > 0)
				{
					throw new InvalidOperationException("Missing dependencies:\n" + string.Join("\n", problems));
				}

				List<AbilityTemplate> abilities = new List<AbilityTemplate>();
				GenerateTravelSpells(abilities);
				if (abilities.Count > 0)
				{
					// The effects are not sold: a travel effect crafted onto another ability is not a thing.
					WireMerchant(abilities, new List<AbilityEvent>(), new List<PremadeAbilityTemplate>());
				}

				AssetDatabase.SaveAssets();
				RegisterAddressables();
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();

				foreach (string path in written)
				{
					Debug.Log($"[TestAbilitySuiteGenerator] asset: {path}");
				}
				foreach (string problem in problems)
				{
					Debug.LogWarning($"[TestAbilitySuiteGenerator] PROBLEM: {problem}");
				}
				Debug.Log($"[TestAbilitySuiteGenerator] DONE assets={written.Count} problems={problems.Count}");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[TestAbilitySuiteGenerator] FAILED: {ex}");
				throw;
			}
		}

		private static void GenerateTravelSpells(List<AbilityTemplate> abilities)
		{
			BaseAction portalToBind = CreateModuleAction(PortalToBindActionType);
			BaseAction markLocation = CreateModuleAction(MarkLocationActionType);
			BaseAction recall = CreateModuleAction(RecallActionType);
			BaseAction openGate = CreateModuleAction(OpenGateActionType);
			BaseAction callToArms = CreateModuleAction(CallToArmsActionType);
			if (portalToBind == null || markLocation == null || recall == null || openGate == null || callToArms == null)
			{
				problems.Add("The Travel module is not in this project: no travel spells generated.");
				return;
			}

			GameObject areaPulse = Require<GameObject>(PathAreaPulse);
			if (areaPulse == null)
			{
				return;
			}

			EnsureFolder(TravelAbilityFolder);
			EnsureFolder(TravelEventFolder);
			EnsureFolder(FXFolder);

			// ── Effects: each spell's own colour, a ring on the ground and a flash about the body. ──
			GameObject homeRing = BuildEffect(explosionFX, "Portal to Bind Ring", go => { Recolour(go, HomePalette); MakeGroundRing(go); });
			GameObject homeFlash = BuildEffect(explosionFX, "Portal to Bind Flash", go => Recolour(go, HomePalette));
			GameObject markRune = BuildEffect(explosionFX, "Mark Rune", go => { Recolour(go, ArcanePalette); MakeGroundRing(go); });
			GameObject markGather = BuildEffect(explosionFX, "Mark Gather", go => { Recolour(go, ArcanePalette); MakeImplosion(go); });
			GameObject recallFlash = BuildEffect(explosionFX, "Recall Flash", go => Recolour(go, ArcanePalette));
			GameObject gateRing = BuildEffect(explosionFX, "Gate Call Ring", go => { Recolour(go, GatePalette); MakeGroundRing(go); });
			GameObject gateFlash = BuildEffect(explosionFX, "Gate Call Flash", go => { Recolour(go, GatePalette); MakeImplosion(go); });
			GameObject rallyRing = BuildEffect(explosionFX, "Call to Arms Ring", go => { Recolour(go, RallyPalette); MakeGroundRing(go); });
			GameObject rallyFlash = BuildEffect(explosionFX, "Call to Arms Flash", go => Recolour(go, RallyPalette));

			/* Each spell is a short-lived, invisible object at the caster's feet (Area Pulse, like Lesser Teleport), not a self-cast:
			 * a self-cast runs only on the server and the caster's own client, so nobody else ever saw it. The object is sent to
			 * observers, its spawn chain plays the effects on every peer, and the travel action in the same chain does the work
			 * on the server alone (each action refuses on a client). The effects come first so they stay where the cast was. */
			abilities.Add(TravelSpell("Portal to Bind",
				$"After {PortalToBindCastSeconds:0} seconds of casting, a portal carries you to your home point, wherever it is. It can be cast in combat, but any damage you take breaks the cast.",
				PortalToBindCastSeconds, PortalToBindCooldownSeconds, PortalToBindManaCost, portalToBind,
				Fx(homeRing, 3f, 0f), Fx(homeFlash, 2.5f, 1f)));
			abilities.Add(TravelSpell("Mark",
				"Marks the spot you stand on as the place Recall returns you to, replacing your old mark. It cannot be cast inside an instance or where you could not safely be put back down. Any damage you take breaks the cast.",
				MarkCastSeconds, MarkCooldownSeconds, MarkManaCost, markLocation,
				Fx(markRune, 2f, 0f), Fx(markGather, 1.5f, 1f)));
			abilities.Add(TravelSpell("Recall",
				$"After {RecallCastSeconds:0} seconds of casting, returns you to the location you marked, wherever it is. It can be cast in combat, but any damage you take breaks the cast.",
				RecallCastSeconds, RecallCooldownSeconds, RecallManaCost, recall,
				Fx(markRune, 2.5f, 0f), Fx(recallFlash, 2.5f, 1f)));
			abilities.Add(TravelSpell("Gate",
				"Calls up a gate. When the cast ends, choose a Gate Stone you have discovered on the map, in any land you have visited; a portal then opens in front of you to it for a short while. Anyone may step through, and each traveller can return to where the gate was cast through the lit Gate Stone until the gate closes. Damage breaks the cast and the choice.",
				GateCastSeconds, GateCooldownSeconds, GateManaCost, openGate,
				Fx(gateRing, 3.5f, 0f), Fx(gateFlash, 2f, 1f)));
			abilities.Add(TravelSpell("Call to Arms",
				"Calls every member of your party to your side, wherever they are. Each is asked, and those who accept are brought to you. It cannot be cast inside an instance. Any damage you take breaks the cast.",
				CallToArmsCastSeconds, CallToArmsCooldownSeconds, CallToArmsManaCost, callToArms,
				Fx(rallyRing, 8f, 0f), Fx(rallyFlash, 2f, 1f)));

			PlayFXAction Fx(GameObject prefab, float size, float height) => new PlayFXAction { FXPrefab = prefab, Size = size, HeightOffset = height };

			AbilityTemplate TravelSpell(string name, string description, float castSeconds, float cooldownSeconds, int manaCost,
				BaseAction effect, params PlayFXAction[] fx)
			{
				AbilityOnSpawnEvent spawn = SaveEvent<AbilityOnSpawnEvent>(TravelEventFolder, $"{name} Event", ev =>
				{
					foreach (PlayFXAction play in fx)
					{
						ev.OnConditionsMetActions.Add(play);
					}
					ev.OnConditionsMetActions.Add(effect);
				});
				return SaveAbility(TravelAbilityFolder, name, a =>
				{
					a.Description = description;
					a.AbilityObjectPrefab = areaPulse; // invisible, at the caster's feet: carries the cast to observers
					a.AbilitySpawnTarget = AbilitySpawnTarget.PointBlank;
					a.Type = AbilityType.GroundedMagic;
					a.ActivationTime = castSeconds;
					a.Speed = 0f;
					// Long enough for an observer's copy to be drawn (and its effects released) before it ends.
					a.LifeTime = 0.5f;
					a.Cooldown = cooldownSeconds;
					a.HitCount = 1;
					a.InterruptedByDamage = true;
					a.ActivationConditions.Add(Cost(mana, manaCost));
					a.OnSpawnEvents.Add(spawn);
				});
			}
		}

		/// <summary>A new instance of a module's action, found by its assembly-qualified name, or null without the module.</summary>
		private static BaseAction CreateModuleAction(string assemblyQualifiedName)
		{
			Type type = Type.GetType(assemblyQualifiedName, throwOnError: false);
			return type != null && typeof(BaseAction).IsAssignableFrom(type) ? (BaseAction)Activator.CreateInstance(type) : null;
		}
	}
}
