using Client;
using FishNet.Managing;
using FishNet.Managing.Scened;
using Server;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UILoadingScreen : UIControl
{
    private NetworkManager networkManager;

    [Header("Loading Screen Parameters")]
    public Slider LoadingProgress;
    public Image LoadingImage;
    public WorldSceneDetailsCache Details;

    public override void OnStarting()
    {
        networkManager = FindObjectOfType<NetworkManager>();
        networkManager.SceneManager.OnLoadStart += OnSceneStartLoad;
        networkManager.SceneManager.OnLoadPercentChange += OnSceneProgressUpdate;
        networkManager.SceneManager.OnLoadEnd += OnSceneEndLoad;

        networkManager.ClientManager.RegisterBroadcast<SceneWorldReconnectBroadcast>(OnServerChange);
    }

    public override void OnDestroying()
    {
        networkManager.SceneManager.OnLoadStart -= OnSceneStartLoad;
        networkManager.SceneManager.OnLoadPercentChange -= OnSceneProgressUpdate;
        networkManager.SceneManager.OnLoadEnd -= OnSceneEndLoad;

        networkManager.ClientManager.UnregisterBroadcast<SceneWorldReconnectBroadcast>(OnServerChange);
    }

    private void ShowLoadingScreen()
    {
        visible = true;
        LoadingProgress.value = 0;
        LoadingImage.gameObject.SetActive(LoadingImage.sprite != null);
    }

    #region Network Events
    public void OnServerChange(SceneWorldReconnectBroadcast reconnect)
    {
        if(reconnect.sceneName != null &&
            reconnect.teleporterName != null &&
            Details.scenes.TryGetValue(reconnect.sceneName, out WorldSceneDetails details) &&
            details.teleporters.TryGetValue(reconnect.teleporterName, out SceneTeleporterDetails teleporter))
        {
            LoadingImage.sprite = teleporter.sceneTransitionImage;
        }
        else
        {
            LoadingImage.sprite = null;
        }

        ShowLoadingScreen();
    }
    #endregion

    #region Scene Events
    private void OnSceneStartLoad(SceneLoadStartEventArgs startEvent)
    {
        ShowLoadingScreen();
    }

    private void OnSceneProgressUpdate(SceneLoadPercentEventArgs percentEvent)
    {
        LoadingProgress.value = percentEvent.Percent;
    }

    private void OnSceneEndLoad(SceneLoadEndEventArgs endEvent)
    {
        visible = false;
    }
    #endregion
}
