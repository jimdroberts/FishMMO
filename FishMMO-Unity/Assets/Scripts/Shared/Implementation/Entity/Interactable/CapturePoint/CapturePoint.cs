using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Serializing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Capture point interactable for PvP or general objective capture.
	/// Tracks ownership, capture progress, and objective state.
	/// Fires <see cref="OnCaptured"/> when a player successfully captures the point.
	/// Configured via a <see cref="CapturePointTemplate"/> ScriptableObject asset.
	/// </summary>
	[RequireComponent(typeof(SceneObjectNamer))]
	public class CapturePoint : Interactable, ICapturePoint
	{
		/// <summary>
		/// Template defining capture parameters.
		/// </summary>
		public CapturePointTemplate Template;

		/// <summary>
		/// Achievement to increment when a player captures this point.
		/// </summary>
		public AchievementTemplate AchievementTemplate;

		/// <inheritdoc />
		CapturePointTemplate ICapturePoint.Template => Template;

		/// <inheritdoc />
		AchievementTemplate ICapturePoint.AchievementTemplate => AchievementTemplate;

		/// <summary>
		/// Character ID of the player who currently owns this capture point. 0 means neutral.
		/// </summary>
		public long OwnerCharacterID { get; set; }

		/// <summary>
		/// Current number of capture interactions applied toward the current capture attempt.
		/// </summary>
		public int CaptureProgress { get; set; }

		/// <summary>
		/// Character ID of the player currently attempting to capture this point.
		/// </summary>
		public long CapturingCharacterID { get; set; }

		/// <summary>
		/// Current objective state of this capture point.
		/// </summary>
		public ObjectiveState State { get; set; }

		private string title = "Capture Point";

		/// <summary>
		/// Display title shown above the capture point.
		/// </summary>
		public override string Title { get { return title; } }

		/// <summary>
		/// Title color for the capture point UI label. Uses olive for a distinct PvP aesthetic.
		/// </summary>
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.olive); } }

		public override void OnAwake()
		{
			base.OnAwake();

			if (Template != null)
			{
				title = Template.Name;
			}
		}

		public override bool CanInteract(IPlayerCharacter character)
		{
			if (Template == null ||
				!base.CanInteract(character))
			{
				return false;
			}

			// Cannot capture a point you already own
			if (OwnerCharacterID == character.ID)
			{
				return false;
			}

			return true;
		}

		/// <summary>
		/// Applies one capture interaction toward this point.
		/// Returns true if the capture is now complete.
		/// </summary>
		/// <param name="characterID">The character attempting to capture.</param>
		/// <returns>True if capture completed, false if still in progress.</returns>
		public bool ApplyCapture(long characterID)
		{
			// If a different player is capturing, reset progress
			if (CapturingCharacterID != 0 && CapturingCharacterID != characterID)
			{
				CaptureProgress = 0;
				SetState(ObjectiveState.Contested);
			}

			CapturingCharacterID = characterID;
			CaptureProgress++;

			if (CaptureProgress >= Template.InteractionsToCapture)
			{
				OwnerCharacterID = characterID;
				CapturingCharacterID = 0;
				CaptureProgress = 0;
				SetState(ObjectiveState.Captured);
				ICapturePoint.OnCaptured?.Invoke(this, characterID);
				return true;
			}

			SetState(ObjectiveState.Capturing);
			return false;
		}

		/// <summary>
		/// Sets the objective state and fires the <see cref="OnStateChanged"/> event.
		/// </summary>
		private void SetState(ObjectiveState newState)
		{
			if (State == newState)
			{
				return;
			}
			State = newState;
			ICapturePoint.OnStateChanged?.Invoke(this, newState);
		}

		/// <summary>
		/// Writes capture point state to the network payload.
		/// </summary>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			base.WritePayload(connection, writer);
			writer.WriteInt64(OwnerCharacterID);
			writer.WriteUInt8Unpacked((byte)State);
		}

		/// <summary>
		/// Reads capture point state from the network payload.
		/// </summary>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			base.ReadPayload(connection, reader);
			OwnerCharacterID = reader.ReadInt64();
			State = (ObjectiveState)reader.ReadInt8Unpacked();
		}
	}
}