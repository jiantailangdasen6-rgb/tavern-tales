#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Tavern.Unity.Boot
{
    /// <summary>
    /// Automated checks from the command line (Claude verifies screens this way):
    ///   -screenshot &lt;path.png&gt; -shotat &lt;s1,s2,..&gt;   saves path_1.png, path_2.png ... at those seconds
    ///   -autoquit &lt;s&gt;                                  quits after s seconds (also mutes the game)
    /// </summary>
    public sealed class DevShot : MonoBehaviour
    {
        static string? Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        IEnumerator Start()
        {
            var quitArg = Arg("-autoquit");
            if (quitArg != null) { AudioListener.volume = 0f; StartCoroutine(QuitAt(float.Parse(quitArg, System.Globalization.CultureInfo.InvariantCulture))); }   // on time, even with shots still to come
            var path = Arg("-screenshot");
            if (path != null)
            {
                var times = (Arg("-shotat") ?? "2").Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                for (int i = 0; i < times.Length; i++)
                {
                    while (Time.realtimeSinceStartup < times[i]) yield return null;
                    yield return new WaitForEndOfFrame();
                    var file = times.Length == 1 ? path : Path.ChangeExtension(path, null) + $"_{i + 1}.png";
                    ScreenCapture.CaptureScreenshot(file);
                    Debug.Log("[DevShot] " + file);
                }
            }
        }

        static IEnumerator QuitAt(float t)
        {
            while (Time.realtimeSinceStartup < t) yield return null;
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }
    }
}
