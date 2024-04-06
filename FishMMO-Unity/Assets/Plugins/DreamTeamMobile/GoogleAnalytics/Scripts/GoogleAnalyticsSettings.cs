using UnityEngine;

public class GoogleAnalyticsSettings : ScriptableObject
{
    public string GA4MeasurementId;
    public string GA4StreamApiSecret;
    public int DefaultEngagementTimeInSec = 100;
    public bool TrackApplicationLog = false;
}
