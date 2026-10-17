#nullable enable
using UnityEngine;

namespace Tavern.Unity.App
{
    /// <summary>Scales the fixed 1920x1080 stage to fit the screen (letterbox).</summary>
    public sealed class StageFitter : MonoBehaviour
    {
        public RectTransform Stage = null!;
        void LateUpdate()
        {
            var canvas = (RectTransform)transform;
            var s = Mathf.Min(canvas.rect.width / 1920f, canvas.rect.height / 1080f);
            Stage.localScale = new Vector3(s, s, 1);
        }
    }
}
