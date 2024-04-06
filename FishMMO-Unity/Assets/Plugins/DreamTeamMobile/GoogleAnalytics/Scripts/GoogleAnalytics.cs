using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace DreamTeamMobile
{
	public class GoogleAnalytics
	{
		private GoogleAnalyticsSettings _settings;
		private readonly GoogleAnalyticsGA4Api _gaClient;
		private static GoogleAnalytics _instance;

		public static GoogleAnalytics Instance
		{
			get
			{
				if (_instance == null)
					_instance = new GoogleAnalytics();
				return _instance;
			}
		}

		private GoogleAnalytics()
		{
			_settings = Resources.LoadAll<GoogleAnalyticsSettings>("GoogleAnalyticsSettings").FirstOrDefault();
			if (ValidSettings())
			{
				_gaClient = new GoogleAnalyticsGA4Api(_settings.GA4MeasurementId, _settings.GA4StreamApiSecret, GetDeviceId());

				if (_settings.TrackApplicationLog)
				{
					Application.logMessageReceived += Application_logMessageReceived;
				}
			}
		}

		private bool ValidSettings()
		{
			if (_settings == null)
			{
				Debug.Log("GoogleAnalytics settings are not found. Please use the Window/GoogleAnalytics menu option to generate the settings.");
				return false;
			}
			if (string.IsNullOrWhiteSpace(_settings.GA4MeasurementId))
			{
				Debug.Log("GoogleAnalytics GA4MeasurementId is not configured properly. Please use the Window/GoogleAnalytics menu option to locate and configure the settings.");
				return false;
			}
			if (string.IsNullOrWhiteSpace(_settings.GA4StreamApiSecret))
			{
				Debug.Log("GoogleAnalytics GA4StreamApiSecret is not configured properly. Please use the Window/GoogleAnalytics menu option to locate and configure the settings.");
				return false;
			}
			return true;
		}

		private string GetDeviceId()
		{
			var deviceId = SystemInfo.deviceUniqueIdentifier;
			if (!string.IsNullOrWhiteSpace(deviceId) && deviceId != "n/a")
				return deviceId;

			deviceId = PlayerPrefs.GetString(nameof(deviceId));
			if (string.IsNullOrWhiteSpace(deviceId))
			{
				deviceId = Guid.NewGuid().ToString();
				PlayerPrefs.SetString(nameof(deviceId), deviceId);
			}

			return deviceId;
		}

		/// <summary>
		/// Limitations:
		/// - Requests can have a maximum of 25 events.
		/// - Events can have a maximum of 25 parameters.
		/// - Events can have a maximum of 25 user properties.
		/// - User property names must be 24 characters or fewer.
		/// - User property values must be 36 characters or fewer.
		/// - Event names must be 40 characters or fewer, can only contain alpha-numeric characters and underscores, and must start with an alphabetic character.
		/// - Parameter names including item parameters must be 40 characters or fewer, can only contain alpha-numeric characters and underscores, and must start with an alphabetic character.
		/// - Parameter values including item parameter values must be 100 characters or fewer.
		/// - Item parameters can have a maximum of 10 custom parameters.
		/// </summary>
		public void TrackEvent<T>(string eventName, Dictionary<string, T> eventParams = null)
		{
			if (!ValidSettings())
			{
				return;
			}

			Debug.Log($"[DTM GA4] Tracking event: {eventName}, params: {eventParams?.Count}");
			_gaClient.TrackEvent(eventName, eventParams);
		}

		public void TrackLogMessage(LogType logType, string logMessage, [CallerMemberName] string memberName = "")
		{
			if (string.IsNullOrWhiteSpace(logMessage) || !ValidSettings())
				return;

			Debug.Log($"[DTM GA4] Tracking log message: {logMessage}");
			_gaClient.TrackEvent($"{logType}_{memberName}", new Dictionary<string, string>
			{
				{ "Message", logMessage }
			});
		}

		private void Application_logMessageReceived(string condition, string stackTrace, LogType logType)
		{
			var logMessage = $"{logType}: {condition}\r\n{stackTrace}\r\n";

			TrackLogMessage(logType, logMessage);
		}
	}
}