using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 关卡出生点：一个场景里可以有多个，用 <see cref="Id"/> 区分。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么会有多个</b>：世界是分区流式加载的，分区之间玩家是**连续走过去**的，
    /// 所以日常移动不需要落点。出生点只服务两种时刻：开局、重开 / 读档恢复。
    /// 多个点用 <see cref="Id"/> 区分，将来存档要指定「回到哪个点」时用得上。</para>
    /// <para><b>谁来应用</b>：正常流程由常驻宿主上的 <see cref="WorldStreamer"/> 在开局
    /// （第一片分区加载完成）时挑一个调 <see cref="Apply"/>。直接打开分区场景按 Play 调试时
    /// 没有宿主，默认点（id = start）会自己兜底，保住调试便利。</para>
    /// <para><b>执行时机</b>：无论走哪条路都发生在 Start 或「场景加载完成」时 ——
    /// 此时 MotionMover 已建好且速度为零，第一个物理帧还没跑，直接写位置是安全的。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        /// <summary>关卡入口用的默认出生点编号。</summary>
        public const string DefaultId = "start";

        [Tooltip("出生点编号。门上的「目标落点」填这个值；关卡入口固定用 start。")]
        [SerializeField] private string _id = DefaultId;

        [Tooltip("出生朝向：1 朝右，−1 朝左。目前只作为标记数据。")]
        [SerializeField] private int _facing = 1;

        [Tooltip("没有常驻宿主时（直接打开关卡场景调试）默认点自己把玩家搬过来")]
        [SerializeField] private bool _applyWhenStandalone = true;

        /// <summary>出生点编号；留空按默认点处理。</summary>
        public string Id => string.IsNullOrWhiteSpace(_id) ? DefaultId : _id;

        /// <summary>出生朝向：+1 右，−1 左。</summary>
        public int Facing => _facing >= 0 ? 1 : -1;

        private void Start()
        {
            if (!_applyWhenStandalone) return;
            if (GameRoot.Instance != null) return;   // 有宿主：交给 WorldStreamer 开局挑
            if (Id != DefaultId) return;             // 兜底只认默认点，避免多个点互相抢

            Apply();
        }

        /// <summary>把玩家搬到本物体的位置。找不到玩家时只警告，不抛异常。</summary>
        public void Apply()
        {
            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player == null)
            {
                Debug.LogWarning("[PlayerSpawnPoint] 场景里没有 PlayerController，出生点没有生效。", this);
                return;
            }

            Vector2 spawn = transform.position;

            Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                // Kinematic 刚体：位置直接写。速度由 MotionMover 每物理帧整体覆盖，
                // 开局它本来就是零，这里清一次只是防止预制体上留了脏值。
                body.position = spawn;
                body.linearVelocity = Vector2.zero;
            }

            Vector3 p = player.transform.position;
            player.transform.position = new Vector3(spawn.x, spawn.y, p.z);
        }

        /// <remarks>
        /// 用 OnDrawGizmosSelected 而不是 OnDrawGizmos：
        /// 后者在 Game 视图开启 Gizmos 时也会画出来，跑起来能看见一个绿圈。
        /// 选中时才画，既不干扰试玩，摆位置时又看得见。
        /// </remarks>
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.35f);
            Gizmos.DrawLine(transform.position, transform.position + new Vector3(Facing * 0.6f, 0f, 0f));
        }
    }
}
