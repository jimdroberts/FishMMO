using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// A milestone shown over a character as a floating label: an achievement's completed tier, announced by the
	/// Achievements module. Split out of the combat display (2026-11-20) when the combat text became the Combat module's.
	/// </summary>
	/// <remarks>
	/// Rare (one label per milestone), so nothing is cached per character: the label's height is read from the character's
	/// collider, or the player's capsule, when one is shown. Off with the "show achievements" gameplay option.
	/// </remarks>
	public static class ClientMilestoneDisplay
	{
		/// <summary>True between <see cref="Initialize"/> and <see cref="Shutdown"/> (the client's lifetime).</summary>
		private static bool active;

		/// <summary>Starts showing milestones. The client calls it as it starts.</summary>
		public static void Initialize()
		{
			active = true;
		}

		/// <summary>Stops showing milestones. The client calls it as it is destroyed.</summary>
		public static void Shutdown()
		{
			active = false;
		}

		/// <summary>
		/// Shows a milestone over a character. Modules call it (the Achievements module on a completed tier); nothing happens
		/// before the client started, or with the "show achievements" option off.
		/// </summary>
		/// <param name="character">The character it is shown over.</param>
		/// <param name="headline">The first line, for example "Achievement: Explorer".</param>
		/// <param name="detail">The second line; may be empty.</param>
		public static void ShowMilestone(ICharacter character, string headline, string detail)
		{
			if (!active || character == null || character.Transform == null || string.IsNullOrEmpty(headline) ||
				Configuration.GlobalSettings == null || !ClientSettings.GetGameplayToggle(ClientSettings.ShowAchievementsKey))
			{
				return;
			}
			Vector3 position = character.Transform.position;
			position.y += HeightOf(character);
			int fx = 0; fx.EnableBit(LabelEffect.FadeIn); fx.EnableBit(LabelEffect.FadeOut); fx.EnableBit(LabelEffect.Bounce);
			UITKLabelMaker.Display3D(string.IsNullOrEmpty(detail) ? headline : headline + "\r\n" + detail, position, Color.yellow, 0.6f, 4.0f, false, fx);
		}

		/// <summary>How far above the character's origin a label sits: its collider's height, or the player's capsule.</summary>
		private static float HeightOf(ICharacter character)
		{
			float height = 1f;
			GameObject go = character.GameObject;
			if (go == null)
			{
				return height;
			}
			Collider collider = go.GetComponent<Collider>();
			if (collider != null)
			{
				collider.TryGetDimensions(out height, out _);
			}
			else if (character is IPlayerCharacter player && player.CharacterController != null)
			{
				height = player.CharacterController.FullCapsuleHeight;
			}
			return height;
		}
	}
}
