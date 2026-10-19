using System.Collections;
using RuleGame.Gameplay.Common;
using UnityEngine;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>
    /// 把现成的 <see cref="SwingCheckpointFade"/>（Alpha 字段 + OnGUI 画黑）包成
    /// <see cref="IScreenFade"/>，供玩法层（火车传送等）使用。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不直接改 <c>SwingCheckpointFade</c></b>：那个组件是两个系统共用的
    /// （过场 + 传送），让它同时实现玩法接口会把「表现」和「玩法」焊死。
    /// 适配层只做一件事：按时间把 Alpha 插值，其余一概不管。以后黑场换成 Canvas/UI 覆盖层，
    /// 只改这个适配器，传送那边一行都不用动。</para>
    /// <para><b>必须用非缩放时间</b>：传送面板会把 <c>Time.timeScale</c> 压到 0.001
    /// （<c>TimeSource.UI</c>），用 <c>Time.deltaTime</c> 的话一次淡入要 250 秒。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SwingCheckpointFadeScreen : MonoBehaviour, IScreenFade
    {
        [Tooltip("要驱动的黑场（SwingCheckpointFade）。留空则本组件自动在本物体上找。")]
        [SerializeField] private SwingCheckpointFade _fade;

        [Tooltip("一次淡入/淡出的时长（秒，非缩放时间）。0 = 瞬间。")]
        [SerializeField, Min(0f)] private float _fadeSeconds = 0.28f;

        public bool IsCovered => _fade != null && _fade.Alpha >= 0.999f;

        private void Awake()
        {
            if (_fade == null) _fade = GetComponent<SwingCheckpointFade>();
        }

        public IEnumerator FadeTo(float alpha)
        {
            if (_fade == null) yield break;

            alpha = Mathf.Clamp01(alpha);
            float start = Mathf.Clamp01(_fade.Alpha);

            if (_fadeSeconds <= 0.0001f)
            {
                _fade.Alpha = alpha;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < _fadeSeconds)
            {
                // 不用 TimeManager.DeltaTime：它被暂停与时间规则同时影响，黑场不该被拖慢。
                elapsed += UnityEngine.Time.unscaledDeltaTime;
                _fade.Alpha = Mathf.Lerp(start, alpha, Mathf.Clamp01(elapsed / _fadeSeconds));
                yield return null;
            }

            _fade.Alpha = alpha;
        }

        /// <summary>立刻透明。收尾用，不走插值。</summary>
        public void Clear()
        {
            if (_fade != null) _fade.Alpha = 0f;
        }

        private void OnDisable()
        {
            // 组件被关掉时把画面放回来，别把人留在黑屏里。
            Clear();
        }
    }
}
