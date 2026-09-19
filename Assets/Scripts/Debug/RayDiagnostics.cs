using System.Collections;
using System.Text;
using UnityEngine;

/// <summary>
/// TEMPORARY DIAGNOSTIC - delete once the duplicate-ray issue is closed.
///
/// Dumps every enabled LineRenderer in every loaded scene, with its full hierarchy path, so a
/// second ray visible only on device can be identified without attaching a debugger. Self-installs
/// via RuntimeInitializeOnLoadMethod, so nothing needs wiring in the Inspector.
///
/// Reading it on a Quest, with the headset connected over USB:
///     adb logcat -s Unity:I | findstr RAYDIAG        (Windows)
///
/// Re-dumps every few seconds, because the interesting state is after the menu has loaded and
/// after the participant has changed hands - not at frame zero.
/// </summary>
public static class RayDiagnostics
{
    private const float FirstDumpDelay = 5f;
    private const float RepeatInterval = 10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        GameObject host = new GameObject("~RayDiagnostics");
        host.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(host);
        host.AddComponent<RayDiagnosticsRunner>();
    }

    internal static void Dump()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("RAYDIAG ===== enabled LineRenderers =====");

        int count = 0;

        // FindObjectsInactive.Exclude: an inactive interactor draws nothing, so it is not a
        // suspect. includeInactive would bury the real one in noise.
        LineRenderer[] lines = Object.FindObjectsByType<LineRenderer>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (LineRenderer line in lines)
        {
            if (line == null || !line.enabled) continue;
            count++;
            report.AppendLine(
                $"RAYDIAG [{count}] {Path(line.transform)}" +
                $"  scene='{line.gameObject.scene.name}'" +
                $"  positions={line.positionCount}" +
                $"  width={line.startWidth:F3}->{line.endWidth:F3}" +
                $"  enabled={line.enabled}" +
                $"  activeInHierarchy={line.gameObject.activeInHierarchy}");
        }

        report.AppendLine($"RAYDIAG ===== {count} enabled LineRenderer(s) total =====");
        Debug.Log(report.ToString());
    }

    private static string Path(Transform t)
    {
        StringBuilder path = new StringBuilder(t.name);
        for (Transform p = t.parent; p != null; p = p.parent)
            path.Insert(0, p.name + "/");
        return path.ToString();
    }

    private class RayDiagnosticsRunner : MonoBehaviour
    {
        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(FirstDumpDelay);

            while (true)
            {
                Dump();
                yield return new WaitForSecondsRealtime(RepeatInterval);
            }
        }
    }
}
