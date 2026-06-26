using System.Collections;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Manages fog transitions triggered by region changes. Extracted from Client.cs.
	/// </summary>
	public class ClientFogManager
	{
		private class FogLerpState
		{
			public Color Color = Color.white;
			public float Density, StartDist, EndDist;
			public void Capture() { Color = RenderSettings.fogColor; Density = RenderSettings.fogDensity; StartDist = RenderSettings.fogStartDistance; EndDist = RenderSettings.fogEndDistance; }
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
			RenderSettings.fog = s.Enabled;
			if (!s.Enabled) return;
			RenderSettings.fogMode = s.Mode;
			if (initialState == null) { initialState = new FogLerpState(); initialState.Capture(); }
			changeRate = s.ChangeRate; finalColor = s.Color; finalDensity = s.Density;
			finalStartDist = s.StartDistance; finalEndDist = s.EndDistance;
			if (initialState.Density == finalDensity) { RenderSettings.fogColor = finalColor; RenderSettings.fogDensity = finalDensity; RenderSettings.fogStartDistance = finalStartDist; RenderSettings.fogEndDistance = finalEndDist; }
			else lerpRoutine = owner.StartCoroutine(Lerp());
		}

		private IEnumerator Lerp()
		{
			for (float t = 0.01f; t < changeRate; t += 0.01f)
			{
				float lt = t / changeRate;
				RenderSettings.fogColor = Color.Lerp(initialState.Color, finalColor, lt);
				RenderSettings.fogDensity = Mathf.Lerp(initialState.Density, finalDensity, lt);
				RenderSettings.fogStartDistance = Mathf.Lerp(initialState.StartDist, finalStartDist, lt);
				RenderSettings.fogEndDistance = Mathf.Lerp(initialState.EndDist, finalEndDist, lt);
				yield return null;
			}
		}
	}
}
