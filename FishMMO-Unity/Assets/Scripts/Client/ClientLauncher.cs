using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
#if UNITY_EDITOR
using UnityEditor;
#endif
using TMPro;
using FishMMO.Shared;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

using HtmlAgilityPack;

namespace FishMMO.Client
{
	public class ClientLauncher : MonoBehaviour
	{
		public Image Background;
		public TMP_Text Title;

		public GameObject HTMLView;

		public GameObject ProgressBarGroup;
		public Slider ProgressSlider;
		public TMP_Text ProgressText;

		public Button QuitButton;
		public Button PlayButton;
		public TMP_Text PlayButtonText;

		public string Host = "http://127.0.0.1:8080/";

		public string HtmlViewURL = "https://github.com/jimdroberts/FishMMO/wiki";
		public string DivClass = "markdown-body";
		public TMP_Text HtmlText;
		public TMPro_TextLinkHandler HtmlTextLinkHandler;

		private string version;
		private string latestversion;
		private string updaterPath;

		[Serializable]
		public struct VersionFetch
		{
			public string latest_version;
		}

		[Serializable]
		public struct PatchFetch
		{
			public string latest_version;
		}

		private void Awake()
		{
			if (HtmlTextLinkHandler != null)
			{
				HtmlTextLinkHandler.OnLinkClicked += (link) =>
				{
					if (link.Contains("http") ||
						link.Contains("www"))
					{
						Application.OpenURL(link);
					}
				};
			}

			StartCoroutine(FetchHtmlFromURL(HtmlViewURL));

			updaterPath = Path.Combine(Client.GetWorkingDirectory(), Constants.Configuration.UpdaterExecutable);

			// load configuration
			Configuration configuration = new Configuration(Client.GetWorkingDirectory());
			if (!configuration.Load(Configuration.DEFAULT_FILENAME + Configuration.EXTENSION))
			{
				// if we failed to load the file.. save a new one
				configuration.Set("Version", Constants.Configuration.Version);
				configuration.Set("Resolution Width", 1280);
				configuration.Set("Resolution Height", 800);
				configuration.Set("Refresh Rate", (uint)60);
				configuration.Set("Fullscreen", false);
				configuration.Set("ShowDamage", true);
				configuration.Set("ShowHeals", true);
				configuration.Set("ShowAchievementCompletion", true);
				configuration.Set("IPFetchHost", "http://127.0.0.1:8080/");
#if !UNITY_EDITOR
				configuration.Save();
#endif
			}

			if (!configuration.TryGetString("Version", out version))
			{
				throw new UnityException("Version could not be found in configuration!");
			}

#if !UNITY_EDITOR
			Screen.SetResolution(1024, 768, FullScreenMode.Windowed, new RefreshRate()
			{
				numerator = 60,
				denominator = 1,
			});
#endif

			// Assign the title name
			Title.text = Constants.Configuration.ProjectName + " v" + version;

			// Clear the launch button events, this is done programmatically.
			PlayButton.onClick.RemoveAllListeners();

			// Progress bar is disabled unless we are downloading updates.
			ProgressBarGroup.SetActive(false);

#if !UNITY_EDITOR
			SetButtonLock(true);

			// Connect to web server and GET /latest_version
			StartCoroutine(GetLatestVersion((e) =>
			{
				Debug.LogError(e);
			},
			(latest_version) =>
			{
				latestversion = latest_version;
				Debug.Log(latest_version);

				// Compare latest_version with the current client version
				if (latest_version.Equals(version))
				{
					// If version matches we can enable the launch button functionality to load the ClientBootstrap scene
					PlayButton.onClick.AddListener(PlayButton_Launch);
					SetButtonLock(false);
				}
				else
				{
					// If version mismatch we need to change PlayButton to Update mode, GET /clientversion, launch DiffUpdater.exe and close the launcher
					PlayButtonText.text = "Update";
					PlayButton.onClick.AddListener(PlayButton_Update);
					SetButtonLock(false);
				}
			}));
#else
			// Editor skips update so we can enable the launch button functionality to load the ClientBootstrap scene
			PlayButton.onClick.AddListener(PlayButton_Launch);
			SetButtonLock(false);
#endif
		}

		private void OnDestroy()
		{
			if (HtmlTextLinkHandler != null)
			{
				HtmlTextLinkHandler.OnLinkClicked -= (link) =>
				{
					if (link.Contains("http") ||
						link.Contains("www"))
					{
						Application.OpenURL(link);
					}
				};
			}
		}

		IEnumerator FetchHtmlFromURL(string url)
		{
			UnityWebRequest www = UnityWebRequest.Get(url);
			yield return www.SendWebRequest();

			if (www.result != UnityWebRequest.Result.Success)
			{
				Debug.LogError("Error fetching HTML: " + www.error);
			}
			else
			{
				string htmlContent = www.downloadHandler.text;
				string extractedText = ExtractTextFromDiv(htmlContent, DivClass);

				extractedText = ConvertLinksToLinkTags(extractedText);

				if (HtmlText != null)
				{
					HtmlText.text = extractedText;
				}
			}
		}

		string ExtractTextFromDiv(string htmlContent, string divClass)
		{
			HtmlDocument htmlDoc = new HtmlDocument();
			htmlDoc.LoadHtml(htmlContent);

			// Find the div with the specified class
			HtmlNode divNode = htmlDoc.DocumentNode.SelectSingleNode($"//div[contains(@class, '{divClass}')]");
			if (divNode != null)
			{
				// Extract the text from all <p> tags within the div
				var paragraphs = divNode.SelectNodes(".//p");
				if (paragraphs != null)
				{
					System.Text.StringBuilder sb = new System.Text.StringBuilder();
					foreach (var paragraph in paragraphs)
					{
						sb.AppendLine(paragraph.InnerHtml.Trim() + "\r\n");
					}
					return sb.ToString();
				}
			}

			Debug.LogError($"Div with class '{divClass}' not found.");
			return string.Empty;
		}

		public static string ConvertLinksToLinkTags(string html)
		{
			// Regular expression to find <a> tags and replace them with <link> tags
			string pattern = @"<a\s+href=""(.*?)"".*?>(.*?)</a>";
			string replacement = @"<color=#00FF00><link=""$1"">$2</link></color>";

			string result = Regex.Replace(html, pattern, replacement, RegexOptions.IgnoreCase);

			return result;
		}

		public IEnumerator GetLatestVersion(Action<string> onFetchFail, Action<string> onFetchComplete)
		{
			using (UnityWebRequest request = UnityWebRequest.Get(Host + "latest_version"))
			{
				yield return request.SendWebRequest();

				if (request.result == UnityWebRequest.Result.ConnectionError ||
					request.result == UnityWebRequest.Result.ProtocolError)
				{
					onFetchFail?.Invoke("Error: " + request.error);
				}
				else
				{
					// Parse JSON response
					string jsonResponse = request.downloadHandler.text;
					jsonResponse = jsonResponse.ToString();
					VersionFetch result = JsonUtility.FromJson<VersionFetch>(jsonResponse);
					onFetchComplete?.Invoke(result.latest_version);
				}
			}
		}

		public IEnumerator GetPatch(Action<string> onFetchFail, Action onFetchComplete, Action<float> onProgressUpdate)
		{
			using (UnityWebRequest request = UnityWebRequest.Get(Host + version))
			{
				// Define the file path to save the downloaded patch file
				string filePath = Path.Combine(Client.GetWorkingDirectory(), "patches", $"{version}-{latestversion}.patch");

				// Create the file stream
				request.downloadHandler = new DownloadHandlerFile(filePath)
				{
					removeFileOnAbort = true
				};

				// Send the request
				UnityWebRequestAsyncOperation operation = request.SendWebRequest();

				while (!operation.isDone)
				{
					// Update the progress
					onProgressUpdate?.Invoke(operation.progress);
					yield return null;
				}

				// Update the progress one last time so we can reach 100%
				onProgressUpdate?.Invoke(operation.progress);

				if (request.result == UnityWebRequest.Result.ConnectionError ||
					request.result == UnityWebRequest.Result.ProtocolError)
				{
					onFetchFail?.Invoke("Error: " + request.error);
				}
				else
				{
					// Log the success
					Debug.Log($"Patch downloaded and saved to {filePath}");

					// Optionally, handle the patch data here
					onFetchComplete?.Invoke();
				}
			}
		}

		public void PlayButton_Launch()
		{
			SetButtonLock(true);
			SceneManager.LoadScene("ClientBootstrap", LoadSceneMode.Single);
		}

		public void PlayButton_Update()
		{
			SetButtonLock(true);
			StartCoroutine(GetPatch((e) =>
			{
				Debug.LogError(e);
			},
			() =>
			{
				try
				{
					// Start the updater
					ProcessStartInfo startInfo = new ProcessStartInfo(updaterPath);
					startInfo.Arguments = $"-version={latestversion} -pid={Process.GetCurrentProcess().Id} -exe={Constants.Configuration.ClientExecutable}";
					startInfo.UseShellExecute = false;
					Process.Start(startInfo);

					// Exit the launcher
					Quit();
				}
				catch (ArgumentNullException argNullEx)
				{
					Debug.Log($"Error: Application path is null or empty.");
					Debug.Log(argNullEx.Message);
				}
				catch (System.ComponentModel.Win32Exception win32Ex)
				{
					Debug.Log($"Error: Failed to start the updater.");
					Debug.Log(win32Ex.Message);
				}
				catch (Exception ex)
				{
					Debug.Log($"Error: {ex.Message}");
				}
			},
			(progress) =>
			{
				// Progress bar is enabled during downloads
				ProgressBarGroup.SetActive(true);
				ProgressSlider.value = progress;
				ProgressText.text = (int)(progress * 100) + "%";
			}));
		}

		public void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}

		/// <summary>
		/// Sets locked state for the launch button.
		/// </summary>
		public void SetButtonLock(bool locked)
		{
			PlayButton.interactable = !locked;
		}
	}
}