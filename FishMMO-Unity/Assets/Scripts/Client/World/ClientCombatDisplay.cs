using FishMMO.Shared;
using FishMMO.Shared.Core;
using KinematicCharacterController;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Handles client-side display of combat events: floating damage/heal numbers,
	/// death dialog, resurrect dialog hide, and achievement popups.
	/// Extracted from Client.cs.
	/// </summary>
	public class ClientCombatDisplay
	{
		/// <summary>Subscribes to damage/heal/kill/resurrect/achievement events. Call during startup.</summary>
	public void Initialize()
		{
			ICharacterDamageController.OnDamaged += OnDamaged;
			ICharacterDamageController.OnHealed += OnHealed;
			ICharacterDamageController.OnKilled += OnKilled;
			ICharacterDamageController.OnResurrected += OnResurrected;
			IAchievementController.OnCompleteAchievement += OnAchievement;
		}

		/// <summary>Unsubscribes from all combat/achievement events. Call during teardown.</summary>
	public void Shutdown()
		{
			ICharacterDamageController.OnDamaged -= OnDamaged;
			ICharacterDamageController.OnHealed -= OnHealed;
			ICharacterDamageController.OnKilled -= OnKilled;
			ICharacterDamageController.OnResurrected -= OnResurrected;
			IAchievementController.OnCompleteAchievement -= OnAchievement;
		}

		private static float GetDisplayHeight(ICharacter character)
		{
			float h = 1f;
			var col = character.GameObject.GetComponent<Collider>();
			if (col != null) { col.TryGetDimensions(out h, out _); }
			else if (character is IPlayerCharacter pc) { h = pc.CharacterController.FullCapsuleHeight; }
			return h;
		}

		private void OnDamaged(ICharacter attacker, ICharacter target, int amount, DamageAttributeTemplate dmg)
		{
			if (target == null || !Config("ShowDamage")) return;
			var pos = target.Transform.position;
			pos.y += GetDisplayHeight(target);
			int fx = 0; fx.EnableBit(LabelEffect.FloatRandom); fx.EnableBit(LabelEffect.FadeOut);
			LabelMaker.Display3D(amount.ToString(), pos, dmg.DisplayColor, 2.0f, 1.0f, false, fx);
		}

		private void OnHealed(ICharacter healer, ICharacter healed, int amount)
		{
			if (healed == null || !Config("ShowHeals")) return;
			var pos = healed.Transform.position;
			pos.y += GetDisplayHeight(healed);
			int fx = 0; fx.EnableBit(LabelEffect.FloatUp); fx.EnableBit(LabelEffect.FadeOut);
			LabelMaker.Display3D(amount.ToString(), pos, new TinyColor(64, 64, 255).ToUnityColor(), 4.0f, 1.0f, false, fx);
		}

		private void OnKilled(ICharacter killer, ICharacter victim)
		{
			if (victim == null || !victim.NetworkObject.IsOwner) return;
			if (UIManager.TryGetTK("UITKDeathDialog", out UITKDeathDialog d)) d.ShowDeathDialog();
		}

		private void OnResurrected(ICharacter resurrector, ICharacter resurrected)
		{
			if (resurrected == null || !resurrected.NetworkObject.IsOwner) return;
			if (UIManager.TryGetTK("UITKDeathDialog", out UITKDeathDialog d)) d.Hide();
		}

		private void OnAchievement(ICharacter character, AchievementTemplate template, AchievementTier tier)
		{
			if (character == null || template == null || !Config("ShowAchievementCompletion")) return;
			var pos = character.Transform.position;
			pos.y += GetDisplayHeight(character);
			int fx = 0; fx.EnableBit(LabelEffect.FadeIn); fx.EnableBit(LabelEffect.FadeOut); fx.EnableBit(LabelEffect.Bounce);
			LabelMaker.Display3D("Achievement: " + template.Name + "\r\n" + tier.TierCompleteMessage, pos, Color.yellow, 2.0f, 4.0f, false, fx);
		}

		private static bool Config(string key) =>
			Configuration.GlobalSettings.TryGetBool(key, out bool r) && r;
	}
}
