using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit cast bar bound to the character's ability controller. Displays casting
	/// progress and the cast label, and hides itself when the cast completes or is cancelled.
	/// Progress is driven as a percentage of the fill element's width.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The bar is a pure readout of one fact — "an activation is in progress" — and the previous
	/// version only ever learned that the fact had stopped being true down ONE of the several
	/// routes that can end an activation. It subscribed to <c>OnCancel</c> alone, but
	/// <c>AbilityController.ProcessInterrupt</c> fires <c>OnInterrupt</c> and then calls
	/// <c>Cancel(state, suppressCancelEvent: true)</c> precisely so that a subscriber to both
	/// does not double-handle the end of the cast — so a bar listening only to the suppressed
	/// event was left on screen after every single interrupt, filled to whatever fraction the
	/// cast had reached, until the next cast overwrote it.
	/// </para>
	/// <para>
	/// The other routes are just as real and none of them raise <c>OnCancel</c> either: a reply
	/// the server never sends (<c>OnUpdate</c> simply stops arriving), the character dying,
	/// the ability being denied after the client already predicted it, a zone change, and
	/// quit-to-login. Rather than enumerate hide-triggers one by one and hope the list is
	/// complete, this panel keeps a <see cref="lastUpdateTime"/> stamp and treats "no progress
	/// report for <see cref="CastTimeoutSeconds"/> seconds" as the catch-all: any end-of-cast
	/// nobody explicitly told us about still closes the bar within a bounded time.
	/// </para>
	/// </remarks>
	public class UITKCastBar : UITKCharacterControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		/// <summary>Name of the progress fill element inside the cast-bar UXML.</summary>
		private const string FILL_NAME = "castbar-fill";

		/// <summary>Name of the cast label element inside the cast-bar UXML.</summary>
		private const string LABEL_NAME = "castbar-label";

		/// <summary>
		/// Seconds without an <c>OnUpdate</c> before a visible cast bar hides itself.
		/// </summary>
		/// <remarks>
		/// <c>OnUpdate</c> arrives once per replicate tick while an activation is running, so at
		/// any sane tick rate a gap this long means the activation is over and the event that
		/// would have said so never reached us — a dropped server reply, a despawn, a scene
		/// swap mid-cast. Deliberately generous: it is a safety net, not the primary path, and
		/// hiding a bar that is still legitimately casting would be the worse failure.
		/// </remarks>
		private const float CastTimeoutSeconds = 1.0f;

		/// <summary>The progress fill element whose width represents cast progress.</summary>
		private VisualElement fill;

		/// <summary>The label element displaying the current cast name.</summary>
		private Label castLabel;

		/// <summary>
		/// The label of the activation currently on screen, or null when nothing is casting.
		/// </summary>
		/// <remarks>
		/// Held as data rather than read back out of <see cref="castLabel"/>: the label element
		/// belongs to the visual tree, and <c>UIDocument</c> re-clones that tree on every enable,
		/// so the element is not a reliable record of what the bar is showing. It also lets a
		/// second activation starting while the first is still on screen be recognised as a
		/// different cast, which resets the fill instead of letting the new cast inherit the old
		/// one's progress.
		/// </remarks>
		private string activeCastLabel;

		/// <summary>The fraction (0-1) the fill should currently show.</summary>
		private float activeFraction;

		/// <summary>
		/// <c>Time.unscaledTime</c> of the most recent progress report, used by the timeout sweep.
		/// </summary>
		private float lastUpdateTime;

		/// <summary>
		/// Queries the fill and label elements from the document root.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root != null)
			{
				fill = root.Q(FILL_NAME);
				castLabel = root.Q<Label>(LABEL_NAME);
			}

			/* Subscribed with an unsubscribe first. These are STATIC events, and OnStarting runs
			 * again every time the visual tree is rebuilt (UITKControl.ReinitializeIfTreeReplaced),
			 * so a bare += would add one more copy of every handler on each rebuild and the bar
			 * would end up hiding itself several times per event. Removing a handler that is not
			 * subscribed is a no-op, which makes the pair idempotent. */
			ICharacterDamageController.OnKilled -= DamageController_OnKilled;
			ICharacterDamageController.OnKilled += DamageController_OnKilled;
			IPlayerCharacter.OnStopLocalClient -= PlayerCharacter_OnStopLocalClient;
			IPlayerCharacter.OnStopLocalClient += PlayerCharacter_OnStopLocalClient;
			IPlayerCharacter.OnTeleport -= PlayerCharacter_OnTeleport;
			IPlayerCharacter.OnTeleport += PlayerCharacter_OnTeleport;
			SceneManager.activeSceneChanged -= SceneManager_OnActiveSceneChanged;
			SceneManager.activeSceneChanged += SceneManager_OnActiveSceneChanged;
		}

		/// <summary>
		/// Releases the global lifecycle subscriptions.
		/// </summary>
		public override void OnDestroying()
		{
			ICharacterDamageController.OnKilled -= DamageController_OnKilled;
			IPlayerCharacter.OnStopLocalClient -= PlayerCharacter_OnStopLocalClient;
			IPlayerCharacter.OnTeleport -= PlayerCharacter_OnTeleport;
			SceneManager.activeSceneChanged -= SceneManager_OnActiveSceneChanged;

			base.OnDestroying();
		}

		/// <summary>
		/// Subscribes to ability controller cast update, cancel, interrupt, deny and reset events.
		/// </summary>
		/// <remarks>
		/// <c>UITKCharacterControl.OnAfterStarting</c> calls <see cref="OnPreSetCharacter"/> then
		/// <see cref="OnPostSetCharacter"/> on every tree rebuild so the pair cancels out. The
		/// unsubscribe therefore has to live in the PRE hook, not only in the unset hook, or each
		/// rebuild adds another copy of every handler.
		/// </remarks>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.OnUpdate += AbilityController_OnUpdate;
				abilityController.OnCancel += AbilityController_OnCancel;
				abilityController.OnInterrupt += AbilityController_OnInterrupt;
				abilityController.OnReset += AbilityController_OnReset;
				abilityController.OnAbilityDenied += AbilityController_OnAbilityDenied;
			}
		}

		/// <summary>
		/// Unsubscribes from the outgoing character's ability controller before a new one is set.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			UnsubscribeAbilityController();
		}

		/// <summary>
		/// Unsubscribes from ability controller events and hides the cast bar before the character changes.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			UnsubscribeAbilityController();

			EndCast();
		}

		/// <summary>
		/// Drops every ability controller subscription this panel holds on the current character.
		/// </summary>
		private void UnsubscribeAbilityController()
		{
			if (Character != null &&
				Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.OnUpdate -= AbilityController_OnUpdate;
				abilityController.OnCancel -= AbilityController_OnCancel;
				abilityController.OnInterrupt -= AbilityController_OnInterrupt;
				abilityController.OnReset -= AbilityController_OnReset;
				abilityController.OnAbilityDenied -= AbilityController_OnAbilityDenied;
			}
		}

		/// <summary>
		/// Re-applies the cast currently in progress to a freshly rebuilt visual tree.
		/// </summary>
		/// <remarks>
		/// Runs from both <see cref="OnAfterShow"/> and <c>OnAfterStarting</c>. The first open of
		/// a panel has <c>hasStarted</c> still false, so <c>ReinitializeIfTreeReplaced</c> bails
		/// out and <c>OnAfterShow</c> is the only hook that runs; on every later show the tree may
		/// genuinely have been replaced and both fire. Writing the same state from both is
		/// idempotent and is the only arrangement that is correct in both cases.
		/// </remarks>
		private void ApplyCastState()
		{
			if (castLabel != null)
			{
				castLabel.text = activeCastLabel ?? string.Empty;
			}
			if (fill != null)
			{
				fill.style.width = Length.Percent(Mathf.Clamp01(activeFraction) * 100.0f);
			}
		}

		/// <inheritdoc />
		protected override void OnAfterShow()
		{
			ApplyCastState();
		}

		/// <inheritdoc />
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			ApplyCastState();
		}

		/// <summary>
		/// Closes a cast bar whose activation stopped reporting progress.
		/// </summary>
		/// <remarks>
		/// This is the catch-all for every ending nobody announced: a server reply that never
		/// arrived, a despawn, a scene server hand-off mid-cast. Uses <c>unscaledTime</c> so a
		/// paused or time-scaled client still recovers.
		/// </remarks>
		protected override void OnTick()
		{
			if (activeCastLabel == null)
			{
				return;
			}

			if (Time.unscaledTime - lastUpdateTime >= CastTimeoutSeconds)
			{
				EndCast();
			}
		}

		/// <summary>
		/// Updates the cast bar fill and label based on the remaining and total cast time.
		/// </summary>
		/// <param name="label">The cast label to display.</param>
		/// <param name="remainingTime">The remaining cast time.</param>
		/// <param name="totalTime">The total cast time.</param>
		public void AbilityController_OnUpdate(string label, float remainingTime, float totalTime)
		{
			// If the cast is finished, hide the cast bar.
			if (remainingTime <= 0.001f || totalTime <= 0.0f)
			{
				EndCast();
				return;
			}

			// Mirror the legacy slider value: remainingTime / totalTime.
			float fraction = Mathf.Clamp01(1.0f - ((totalTime - remainingTime) / totalTime));

			// If the fill is near zero, hide the cast bar.
			if (fraction <= 0.001f)
			{
				EndCast();
				return;
			}

			/* A second activation reaching the bar while the first is still on screen is a
			 * different cast, not a continuation of the old one. Recording the label makes that
			 * detectable, and re-applying the state below is what stops the new cast inheriting
			 * the previous one's fill for a frame. */
			activeCastLabel = label ?? string.Empty;
			activeFraction = fraction;
			lastUpdateTime = Time.unscaledTime;

			// Show the cast bar if it is not already visible. Show() ends in OnAfterShow, which
			// applies the state above to the tree the player will actually see.
			if (!Visible)
			{
				Show();
				return;
			}

			ApplyCastState();
		}

		/// <summary>
		/// Handles ability cancel by hiding the cast bar.
		/// </summary>
		public void AbilityController_OnCancel()
		{
			EndCast();
		}

		/// <summary>
		/// Handles ability interrupt by hiding the cast bar.
		/// </summary>
		/// <remarks>
		/// The reason this subscription has to exist: <c>ProcessInterrupt</c> suppresses
		/// <c>OnCancel</c> on the interrupt path, so a panel listening only to cancel never
		/// learns that the cast ended.
		/// </remarks>
		public void AbilityController_OnInterrupt()
		{
			EndCast();
		}

		/// <summary>
		/// Handles a full ability-state reset (character reload / re-sync) by hiding the bar.
		/// </summary>
		public void AbilityController_OnReset()
		{
			EndCast();
		}

		/// <summary>
		/// Handles the server denying an ability this client already began predicting.
		/// </summary>
		/// <param name="abilityID">The denied ability's reference ID.</param>
		public void AbilityController_OnAbilityDenied(long abilityID)
		{
			EndCast();
		}

		/// <summary>
		/// Hides the cast bar when the local character dies mid-cast.
		/// </summary>
		/// <param name="killer">The killer, unused.</param>
		/// <param name="victim">The character that died.</param>
		private void DamageController_OnKilled(ICharacter killer, ICharacter victim)
		{
			if (victim == null || Character == null)
			{
				return;
			}

			if (victim.ID == Character.ID)
			{
				EndCast();
			}
		}

		/// <summary>
		/// Hides the cast bar when the local client stops.
		/// </summary>
		/// <param name="character">The local player character.</param>
		private void PlayerCharacter_OnStopLocalClient(IPlayerCharacter character)
		{
			EndCast();
		}

		/// <summary>
		/// Hides the cast bar when the character teleports (zone change) mid-cast.
		/// </summary>
		/// <param name="character">The teleporting character.</param>
		private void PlayerCharacter_OnTeleport(IPlayerCharacter character)
		{
			if (Character != null && character != null && character.ID != Character.ID)
			{
				return;
			}

			EndCast();
		}

		/// <summary>
		/// Hides the cast bar across a scene change.
		/// </summary>
		private void SceneManager_OnActiveSceneChanged(Scene from, Scene to)
		{
			EndCast();
		}

		/// <inheritdoc />
		public override void OnQuitToLogin()
		{
			EndCast();

			base.OnQuitToLogin();
		}

		/// <summary>
		/// Clears the tracked cast and hides the bar.
		/// </summary>
		/// <remarks>
		/// Clearing the state as well as hiding matters: the panel can be re-shown by something
		/// other than a cast (a tree rebuild re-applying state), and a stale
		/// <see cref="activeCastLabel"/> would put the finished cast back on screen.
		/// </remarks>
		private void EndCast()
		{
			activeCastLabel = null;
			activeFraction = 0.0f;

			if (fill != null)
			{
				fill.style.width = Length.Percent(0.0f);
			}
			if (castLabel != null)
			{
				castLabel.text = string.Empty;
			}

			Hide();
		}
	}
}
