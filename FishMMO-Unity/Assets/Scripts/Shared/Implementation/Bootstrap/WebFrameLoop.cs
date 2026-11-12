using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace FishMMO.Shared
{
	/// <summary>
	/// Caps the client's render rate without taking the web player off the browser's animation frames.
	/// </summary>
	/// <remarks>
	/// On the web, any <see cref="Application.targetFrameRate"/> but -1 makes Unity schedule every frame with setTimeout,
	/// out of step with the display; Firefox then presented the canvas out of order and the whole picture snapped back
	/// and forth (FrameLoop.jslib has the measurement). There the loop stays on animation frames
	/// (<c>targetFrameRate</c> -1) and the cap is applied as every Nth display frame. Everywhere else this is
	/// <see cref="Application.targetFrameRate"/> as before.
	/// </remarks>
	public static class WebFrameLoop
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		[DllImport("__Internal")]
		private static extern void FishMMOSetFrameCap(int framesPerSecond);
#endif

		/// <summary>Caps the render rate at <paramref name="framesPerSecond"/> (on the web, at least that, in whole display frames).</summary>
		public static void SetCap(int framesPerSecond)
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			Application.targetFrameRate = -1;
			FishMMOSetFrameCap(framesPerSecond);
#else
			Application.targetFrameRate = framesPerSecond;
#endif
		}
	}
}
