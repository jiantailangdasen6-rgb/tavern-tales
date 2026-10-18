#nullable enable
using System;
using UnityEngine;

namespace Tavern.Unity.App
{
    /// <summary>
    /// -audiocheck only: sits next to the AudioListener, measures the final mix and then clears it, so the check proves
    /// sound reaches the output while nothing comes out of the speakers.
    /// </summary>
    public sealed class OutputMeter : MonoBehaviour
    {
        double _sum;
        long _count;
        float _peak;

        void OnAudioFilterRead(float[] data, int channels)
        {
            for (int i = 0; i < data.Length; i++)
            {
                var v = data[i];
                _sum += v * v;
                if (Math.Abs(v) > _peak) _peak = Math.Abs(v);
            }
            _count += data.Length;
            Array.Clear(data, 0, data.Length);   // silent
        }

        /// <summary>RMS and peak of everything mixed so far, in dBFS.</summary>
        public (double rms, double peak) Read()
        {
            var rms = _count == 0 ? 0 : Math.Sqrt(_sum / _count);
            return (Db(rms), Db(_peak));
        }

        static double Db(double v) => 20 * Math.Log10(Math.Max(v, 1e-9));
    }
}
