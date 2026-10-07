using UnityEngine;

namespace RuleGame.Core.Rules
{
    /// <summary>
    /// 规则系统的启动器与心跳。
    /// </summary>
    /// <remarks>
    /// 挂在常驻场景对象上（或做成跨场景的 Prefab）。
    /// 负责两件事：<b>初始化单例</b>、<b>每帧 Tick</b>。
    /// </remarks>
    [DefaultExecutionOrder(-100)]   // 比其他脚本先 Awake
    public class RuleSystemDriver : MonoBehaviour
    {
        [Tooltip("规则目录资产，放在 Assets/_Project/Data/Rules/")]
        [SerializeField] private RuleCatalog _catalog;

        private void Awake()
        {
            if (_catalog == null)
            {
                Debug.LogError("[RuleSystemDriver] 没有指定 RuleCatalog，规则系统无法初始化。", this);
                return;
            }
            RuleSystem.Initialize(_catalog);
        }

        private void Update()
        {
            // 用 deltaTime：暂停时自动冻结
            RuleSystem.Instance?.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            // 让下次进 Play / 切场景重新初始化
            RuleSystem.Reset();
        }
    }
}
