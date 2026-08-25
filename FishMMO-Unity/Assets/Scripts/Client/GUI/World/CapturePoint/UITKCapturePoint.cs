using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit implementation of the capture point objective readout.
	/// Binds to <c>UICapturePoint.uxml</c> / <c>UICapturePoint.uss</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Reads the same <see cref="CapturePointUpdateBroadcast"/> that
	/// <see cref="ClientInteractableStateSystem"/> writes onto the capture point component. The two
	/// are deliberately separate concerns: the system keeps the client's copy of the world correct
	/// for anything that inspects it — the target frame, world labels — while this draws the
	/// transient readout. Neither depends on the other having run.
	/// </para>
	/// <para>
	/// It closes itself. A capture point emits an update only when something changes, so a quiet
	/// objective would otherwise leave a stale bar pinned to the screen for the rest of the
	/// session.
	/// </para>
	/// </remarks>
	public class UITKCapturePoint : UITKCharacterControl
	{
		private const string TITLE_NAME = "capture-title";
		private const string STATE_NAME = "capture-state";
		private const string FILL_NAME = "capture-bar-fill";
		private const string OWNER_NAME = "capture-owner";

		private const string CSS_HIDDEN = "capture-hidden";

		/// <summary>Seconds without an update before the readout hides itself.</summary>
		private const float IDLE_HIDE_SECONDS = 8.0f;

		/// <summary>Seconds since the last update.</summary>
		private float idleSeconds;

		private string objectiveName = string.Empty;
		private string stateText = string.Empty;
		private string ownerText = string.Empty;
		private float progressFraction;

		/// <inheritdoc />
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();
			ApplyPerOpenContent();
		}

		/// <inheritdoc />
		protected override void OnAfterShow()
		{
			ApplyPerOpenContent();
		}

		/// <inheritdoc />
		public override void OnClientSet()
		{
			Client.NetworkManager.ClientManager.RegisterBroadcast<CapturePointUpdateBroadcast>(OnClientCapturePointUpdateBroadcastReceived);
		}

		/// <inheritdoc />
		public override void OnClientUnset()
		{
			Client.NetworkManager.ClientManager.UnregisterBroadcast<CapturePointUpdateBroadcast>(OnClientCapturePointUpdateBroadcastReceived);
		}

		/// <summary>
		/// Draws the objective's reported state.
		/// </summary>
		private void OnClientCapturePointUpdateBroadcastReceived(CapturePointUpdateBroadcast msg, Channel channel)
		{
			CapturePointTemplate template = CapturePointTemplate.Get<CapturePointTemplate>(msg.TemplateID);
			objectiveName = template != null ? template.Name : "Objective";

			stateText = DescribeState(msg.State);

			/* InteractionsToCapture comes from the message rather than the template so the bar
			 * matches the number the server actually counted against, even if the two disagree
			 * after a content change. */
			progressFraction = msg.InteractionsToCapture > 0
				? Mathf.Clamp01((float)msg.CaptureProgress / msg.InteractionsToCapture)
				: 0.0f;

			ApplyOwner(msg.OwnerCharacterID);

			idleSeconds = 0.0f;

			if (!Visible)
			{
				Show();
			}
			else
			{
				ApplyPerOpenContent();
			}
		}

		/// <summary>
		/// Resolves the owner's name, which arrives as an ID.
		/// </summary>
		/// <remarks>
		/// Asynchronous by nature — the naming system may have to ask the server — so the callback
		/// writes straight to the label rather than assuming the panel is still showing the same
		/// objective. It re-reads the current owner text on arrival for exactly that reason.
		/// </remarks>
		private void ApplyOwner(long ownerCharacterID)
		{
			if (ownerCharacterID == 0)
			{
				ownerText = "Uncontrolled";
				return;
			}

			ownerText = "Held by …";
			long requested = ownerCharacterID;
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, requested, name =>
			{
				ownerText = string.IsNullOrWhiteSpace(name) ? "Held" : "Held by " + name;
				Label owner = Root?.Q<Label>(OWNER_NAME);
				if (owner != null)
				{
					owner.text = ownerText;
				}
			});
		}

		/// <summary>
		/// Turns an objective state into something worth reading.
		/// </summary>
		private static string DescribeState(ObjectiveState state)
		{
			switch (state)
			{
				case ObjectiveState.Capturing: return "CAPTURING";
				case ObjectiveState.Contested: return "CONTESTED";
				case ObjectiveState.Captured: return "HELD";
				default: return "NEUTRAL";
			}
		}

		/// <summary>
		/// Hides the readout once the objective goes quiet.
		/// </summary>
		protected override void OnTick()
		{
			if (!Visible)
			{
				return;
			}

			idleSeconds += Time.deltaTime;
			if (idleSeconds >= IDLE_HIDE_SECONDS)
			{
				Hide();
			}
		}

		/// <summary>
		/// Writes everything that has to survive the visual tree being re-cloned.
		/// </summary>
		private void ApplyPerOpenContent()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			Label title = root.Q<Label>(TITLE_NAME);
			if (title != null)
			{
				title.text = objectiveName;
			}

			Label state = root.Q<Label>(STATE_NAME);
			if (state != null)
			{
				state.text = stateText;
			}

			Label owner = root.Q<Label>(OWNER_NAME);
			if (owner != null)
			{
				owner.text = ownerText;
			}

			VisualElement fill = root.Q(FILL_NAME);
			if (fill != null)
			{
				fill.style.width = Length.Percent(progressFraction * 100.0f);
				// A bar at zero is noise on an objective nobody is taking; the state badge already
				// says what is happening.
				fill.EnableInClassList(CSS_HIDDEN, progressFraction <= 0.0f);
			}
		}
	}
}
