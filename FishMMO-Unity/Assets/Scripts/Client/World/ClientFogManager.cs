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
		private class FogLerpState
		{
			/// <summary>Current fog color before the transition.</summary>
			public Color Color = Color.white;
			/// <summary>Current fog density before the transition.</summary>
			public float Density;
			/// <summary>Current fog start distance before the transition.</summary>
			public float StartDist;
			/// <summary>Current fog end distance before the transition.</summary>
			public float EndDist;
			/// <summary>Captures the current RenderSettings fog values into this state snapshot.</summary>
			public void Capture() { FogState b = FogComposer.Base; Color = b.Color; Density = b.Density; StartDist = b.StartDistance; EndDist = b.EndDistance; }
		}

		private FogLerpState initialState;
		private Coroutine lerpRoutine;
		private float changeRate;
		private Color finalColor = Color.white;
		private float finalDensity, finalStartDist, finalEndDist;
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

		private void OnChangeFog(FogSettings s)
		{
			Stop();
			if (initialState != null) initialState.Capture();
			FogState fog = FogComposer.Base;
			fog.Enabled = s.Enabled;
			if (!s.Enabled) { FogComposer.Base = fog; return; }
			fog.Mode = s.Mode;
			FogComposer.SetRegionFog(fog);
			if (initialState == null) { initialState = new FogLerpState(); initialState.Capture(); }
			changeRate = s.ChangeRate; finalColor = s.Color; finalDensity = s.Density;
			finalStartDist = s.StartDistance; finalEndDist = s.EndDistance;
			if (initialState.Density == finalDensity) { Write(finalColor, finalDensity, finalStartDist, finalEndDist); }
			else lerpRoutine = owner.StartCoroutine(Lerp());
		}

		private IEnumerator Lerp()
		{
			for (float t = 0.01f; t < changeRate; t += 0.01f)
			{
				float lt = t / changeRate;
				Write(Color.Lerp(initialState.Color, finalColor, lt),
					Mathf.Lerp(initialState.Density, finalDensity, lt),
					Mathf.Lerp(initialState.StartDist, finalStartDist, lt),
					Mathf.Lerp(initialState.EndDist, finalEndDist, lt));
				yield return null;
			}
		}

		private static void Write(Color color, float density, float start, float end)
		{
			FogState fog = FogComposer.Base;
			fog.Color = color;
			fog.Density = density;
			fog.StartDistance = start;
			fog.EndDistance = end;
			FogComposer.SetRegionFog(fog);
		}
	}
}
