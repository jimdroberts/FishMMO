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

			EnsureFolder(TravelAbilityFolder);
			EnsureFolder(TravelEventFolder);

			AbilityOnHitEvent portalToBindEvent = SaveEvent<AbilityOnHitEvent>(TravelEventFolder, "Portal to Bind Event", ev =>
			{
				ev.TargetSelector = new InitiatorTargetSelector();
				ev.OnConditionsMetActions.Add(portalToBind);
			});
			abilities.Add(SaveAbility(TravelAbilityFolder, "Portal to Bind", a =>
			{
				a.Description = $"After {PortalToBindCastSeconds:0} seconds of casting, a portal carries you to your home point, wherever it is. It can be cast in combat, but any damage you take breaks the cast.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = PortalToBindCastSeconds;
				a.Cooldown = PortalToBindCooldownSeconds;
				a.HitCount = 1;
				a.InterruptedByDamage = true;
				a.ActivationConditions.Add(Cost(mana, PortalToBindManaCost));
				a.OnHitEvents.Add(portalToBindEvent);
			}));

			AbilityOnHitEvent markEvent = SaveEvent<AbilityOnHitEvent>(TravelEventFolder, "Mark Event", ev =>
			{
				ev.TargetSelector = new InitiatorTargetSelector();
				ev.OnConditionsMetActions.Add(markLocation);
			});
			abilities.Add(SaveAbility(TravelAbilityFolder, "Mark", a =>
			{
				a.Description = "Marks the spot you stand on as the place Recall returns you to, replacing your old mark. It cannot be cast inside an instance or where you could not safely be put back down. Any damage you take breaks the cast.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = MarkCastSeconds;
				a.Cooldown = MarkCooldownSeconds;
				a.HitCount = 1;
				a.InterruptedByDamage = true;
				a.ActivationConditions.Add(Cost(mana, MarkManaCost));
				a.OnHitEvents.Add(markEvent);
			}));

			AbilityOnHitEvent recallEvent = SaveEvent<AbilityOnHitEvent>(TravelEventFolder, "Recall Event", ev =>
			{
				ev.TargetSelector = new InitiatorTargetSelector();
				ev.OnConditionsMetActions.Add(recall);
			});
			abilities.Add(SaveAbility(TravelAbilityFolder, "Recall", a =>
			{
				a.Description = $"After {RecallCastSeconds:0} seconds of casting, returns you to the location you marked, wherever it is. It can be cast in combat, but any damage you take breaks the cast.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = RecallCastSeconds;
				a.Cooldown = RecallCooldownSeconds;
				a.HitCount = 1;
				a.InterruptedByDamage = true;
				a.ActivationConditions.Add(Cost(mana, RecallManaCost));
				a.OnHitEvents.Add(recallEvent);
			}));

			AbilityOnHitEvent gateEvent = SaveEvent<AbilityOnHitEvent>(TravelEventFolder, "Gate Event", ev =>
			{
				ev.TargetSelector = new InitiatorTargetSelector();
				ev.OnConditionsMetActions.Add(openGate);
			});
			abilities.Add(SaveAbility(TravelAbilityFolder, "Gate", a =>
			{
				a.Description = "Calls up a gate. When the cast ends, choose a Gate Stone you have discovered on the map, in any land you have visited; a portal then opens in front of you to it for a short while. Anyone may step through, and each traveller can return to where the gate was cast through the lit Gate Stone until the gate closes. Damage breaks the cast and the choice.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = GateCastSeconds;
				a.Cooldown = GateCooldownSeconds;
				a.HitCount = 1;
				a.InterruptedByDamage = true;
				a.ActivationConditions.Add(Cost(mana, GateManaCost));
				a.OnHitEvents.Add(gateEvent);
			}));

			AbilityOnHitEvent callToArmsEvent = SaveEvent<AbilityOnHitEvent>(TravelEventFolder, "Call to Arms Event", ev =>
			{
				ev.TargetSelector = new InitiatorTargetSelector();
				ev.OnConditionsMetActions.Add(callToArms);
			});
			abilities.Add(SaveAbility(TravelAbilityFolder, "Call to Arms", a =>
			{
				a.Description = "Calls every member of your party to your side, wherever they are. Each is asked, and those who accept are brought to you. It cannot be cast inside an instance. Any damage you take breaks the cast.";
				a.AbilitySpawnTarget = AbilitySpawnTarget.Self;
				a.Type = AbilityType.GroundedMagic;
				a.ActivationTime = CallToArmsCastSeconds;
				a.Cooldown = CallToArmsCooldownSeconds;
				a.HitCount = 1;
				a.InterruptedByDamage = true;
				a.ActivationConditions.Add(Cost(mana, CallToArmsManaCost));
				a.OnHitEvents.Add(callToArmsEvent);
			}));
		}

		/// <summary>A new instance of a module's action, found by its assembly-qualified name, or null without the module.</summary>
		private static BaseAction CreateModuleAction(string assemblyQualifiedName)
		{
			Type type = Type.GetType(assemblyQualifiedName, throwOnError: false);
			return type != null && typeof(BaseAction).IsAssignableFrom(type) ? (BaseAction)Activator.CreateInstance(type) : null;
		}
	}
}
