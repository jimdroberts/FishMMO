using System.Collections;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Manages fog transitions triggered by region changes. Extracted from Client.cs.
	/// </summary>
	/// <remarks>
	/// Region fog is the base the weather is laid over, so this writes <see cref="FogComposer.Base"/>
	/// rather than <see cref="RenderSettings"/>; the composer writes the scene's fog.
	/// </remarks>
	public class ClientFogManager
	{
		/// <summary>
		/// The shortest a region's fog takes to arrive or leave, in seconds. A region authored with a
		/// change rate of nothing still eases over this: fog that cuts in or out between one frame
		/// and the next reads as a fault, not as weather.
		/// </summary>
		public const float MinimumSeconds = 1.5f;

		/// <summary>A distance at which linear fog fogs nothing: what "no fog" is, for a fog that is on.</summary>
		private const float Nowhere = 100000f;

		private Coroutine lerpRoutine;
		private readonly MonoBehaviour owner;

		/// <summary>Creates a ClientFogManager that uses the given MonoBehaviour for coroutines.</summary>
		/// <param name="owner">The MonoBehaviour used to start/stop fog lerp coroutines.</param>
		public ClientFogManager(MonoBehaviour owner) { this.owner = owner; }

		/// <summary>Subscribes to ChangeFogAction events. Call during client initialization.</summary>
		public void Initialize() => ChangeFogAction.OnChangeFog += OnChangeFog;
		/// <summary>Unsubscribes from ChangeFogAction and stops active lerp. Call during teardown.</summary>
		public void Shutdown() { ChangeFogAction.OnChangeFog -= OnChangeFog; Stop(); }

		/// <summary>Stops the current fog lerp coroutine if one is active.</summary>
		public void Stop()
		{
			if (lerpRoutine != null) { owner.StopCoroutine(lerpRoutine); lerpRoutine = null; }
		}

		/// <summary>
		/// Eases the region's fog from whatever it is now to what the region asks for.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Walking into a fog region used to switch the fog on at once, at whatever density and
		/// distances the base happened to hold, and only then begin easing toward the region's own —
		/// so it arrived at full strength and drifted. Walking out switched it off between one frame
		/// and the next: there was no way out at all, only an early return. A region whose density
		/// matched the last one's was written straight to its final values — which is every region
		/// using linear fog, where the density is not used and is nearly always the same — and one
		/// authored with a change rate of zero never had its values written at all. The easing
		/// itself counted frames, not seconds, and stopped one step short of the end.
		/// </para>
		/// <para>
		/// The thing that makes a clean transition possible is that "no fog" can be written as a fog
		/// that is switched on and fogs nothing: no density, or a linear fog that starts and ends
		/// nowhere. Arriving is an ease from that to the region's fog; leaving is an ease back to it,
		/// and only then is the fog switched off. A change of mode goes out through it and back in,
		/// since two modes have no halfway.
		/// </para>
		/// </remarks>
		private void OnChangeFog(FogSettings s)
		{
			Stop();
			lerpRoutine = owner.StartCoroutine(Transition(s));
		}

		private IEnumerator Transition(FogSettings s)
		{
			float seconds = Mathf.Max(MinimumSeconds, s.ChangeRate);
			FogState from = FogComposer.Base;
			bool wanted = s.Enabled;
			FogState target = wanted
				? new FogState { Enabled = true, Mode = s.Mode, Color = s.Color, Density = s.Density, StartDistance = s.StartDistance, EndDistance = s.EndDistance }
				: Nothing(from);

			if (wanted && (!from.Enabled || from.Mode != s.Mode))
			{
				if (from.Enabled)
				{
					// Another kind of fog is up: take it out first, in half the time.
					seconds *= 0.5f;
					yield return Ease(from, Nothing(from), seconds);
				}
				// The new fog, switched on and fogging nothing. Invisible, so not a pop.
				from = Nothing(target);
			}
			else if (!from.Enabled)
			{
				// Asked to leave a fog that is not there.
				lerpRoutine = null;
				yield break;
			}

			yield return Ease(from, target, seconds);
			if (!wanted)
			{
				FogState off = FogComposer.Base;
				off.Enabled = false;
				FogComposer.Base = off;
			}
			lerpRoutine = null;
		}

		/// <summary>The same fog, switched on and fogging nothing.</summary>
		private static FogState Nothing(FogState like)
		{
			like.Enabled = true;
			like.Density = 0f;
			// Kept in the same proportion, so the ease below brings both in together.
			float ratio = like.EndDistance > 1f ? Mathf.Clamp01(like.StartDistance / like.EndDistance) : 0f;
			like.EndDistance = Nowhere;
			like.StartDistance = Nowhere * ratio;
			return like;
		}

		private static IEnumerator Ease(FogState from, FogState to, float seconds)
		{
			for (float elapsed = 0f; elapsed < seconds; elapsed += Time.deltaTime)
			{
				float t = Mathf.SmoothStep(0f, 1f, elapsed / seconds);
				Write(from, to, t);
				yield return null;
			}
			Write(from, to, 1f);
		}

		private static void Write(FogState from, FogState to, float t)
		{
			FogState fog = to;
			fog.Enabled = true;
			fog.Color = Color.Lerp(from.Color, to.Color, t);
			fog.Density = Mathf.Lerp(from.Density, to.Density, t);
			// Linear fog is eased by the reciprocal of its distances. How thick it looks goes as one
			// over the end distance, so eased by the distance itself a fog coming in from "nowhere"
			// spends nearly the whole transition invisible and then arrives all at once in the last
			// few frames — a snap with a delay in front of it.
			float inverseEnd = Mathf.Lerp(1f / Mathf.Max(1f, from.EndDistance), 1f / Mathf.Max(1f, to.EndDistance), t);
			fog.EndDistance = 1f / Mathf.Max(1e-6f, inverseEnd);
			float fromRatio = from.EndDistance > 1f ? Mathf.Clamp01(from.StartDistance / from.EndDistance) : 0f;
			float toRatio = to.EndDistance > 1f ? Mathf.Clamp01(to.StartDistance / to.EndDistance) : 0f;
			fog.StartDistance = fog.EndDistance * Mathf.Lerp(fromRatio, toRatio, t);
			FogComposer.SetRegionFog(fog);
		}
	}
}
