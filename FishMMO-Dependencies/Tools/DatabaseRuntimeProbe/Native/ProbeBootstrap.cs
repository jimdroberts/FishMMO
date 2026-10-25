using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
public sealed class ProbeBootstrap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var host = new GameObject("DatabaseRuntimeProbe").AddComponent<ProbeBootstrap>();
        DontDestroyOnLoad(host.gameObject);
        host.StartCoroutine(host.RunChecks());
    }

    IEnumerator RunChecks()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--db-settings");
        // The diagnostic calls GetAwaiter().GetResult() around async library APIs.
        // Do not block UnitySynchronizationContext while waiting for continuations.
        var task = Task.Run(() => index >= 0 ? Probe.Run(args[index + 1]) : 2);
        while (!task.IsCompleted) yield return null;
        int code = task.IsFaulted ? 1 : task.Result;
        Debug.Log("FISHMMO_DB_PROBE_EXIT=" + code);
        Application.Quit(code);
    }
}
