using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// The login screen's world: a small generated scene loaded beside the login UI (and unloaded with it on entering
	/// the game). Frames the login view, owns the character select fly-in, and makes its scene the active one so the
	/// sky, weather and lighting follow this world rather than whatever world was left.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The camera belongs to the boot scene and is handed back on Quit To Login with no follow target, after which nothing
	/// moves it; this puts it at <see cref="LoginView"/> each time the backdrop comes up.
	/// </para>
	/// <para>
	/// The character select screen lives in another scene and cannot hold a reference into this one, so it finds the
	/// fly-in through <see cref="Current"/>.
	/// </para>
	/// </remarks>
	public sealed class LoginBackdrop : MonoBehaviour
	{
		/// <summary>The backdrop on screen, or null.</summary>
		public static LoginBackdrop Current { get; private set; }

		[Tooltip("Where the camera sits for the login screen.")]
		public Transform LoginView;

		[Tooltip("The fly-in from the login view to the character, played on the character select screen.")]
		public CinematicCamera Cinematic;

		private void OnEnable()
		{
			Current = this;
			SceneManager.sceneLoaded += OnSceneLoaded;
			ApplyView();
		}

		private void Start()
		{
			// A scene still loading reports itself not loaded while its objects wake, so it is made active from here (and
			// again when another scene of the boot finishes loading and takes the active slot with ActivateOnLoad).
			MakeSceneActive();
			// Again once every scene of the boot has woken: the camera may have arrived after this did.
			ApplyView();
		}

		private void OnDisable()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			if (Current == this)
			{
				Current = null;
			}
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			// The login UI loads beside this world and is set active when it finishes; the sky, weather and lighting
			// follow the active scene, so this world takes it back. Never from a world scene: entering the game loads
			// one, which must keep the active slot while this backdrop is being unloaded.
			MakeSceneActive();
			if (scene == gameObject.scene)
			{
				ApplyView();
			}
		}

		/// <summary>Puts the camera at the login view.</summary>
		public void ApplyView()
		{
			Camera camera = Camera.main;
			if (camera != null && LoginView != null)
			{
				camera.transform.SetPositionAndRotation(LoginView.position, LoginView.rotation);
			}
		}

		private void MakeSceneActive()
		{
			Scene scene = gameObject.scene;
			if (!scene.IsValid() || !scene.isLoaded || SceneManager.GetActiveScene() == scene || AnotherWorldLoaded(scene))
			{
				return;
			}
			SceneManager.SetActiveScene(scene);
		}

		/// <summary>Whether a game world is loaded beside this one (entering the game): that world keeps the active slot.</summary>
		private static bool AnotherWorldLoaded(Scene own)
		{
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene other = SceneManager.GetSceneAt(i);
				if (other != own && other.isLoaded && FishMMO.Shared.WorldSceneSettings.TryGetForScene(other, out _))
				{
					return true;
				}
			}
			return false;
		}
	}
}
