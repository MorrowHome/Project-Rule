using System.Collections;
using System.Text;
using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Development.Testing
{
    /// <summary>
    /// 拼装缺口四件套的**冒烟测试**：在 Play 里把整条链路跑一遍，逐条打 ✓ / ✗。
    /// </summary>
    /// <remarks>
    /// <para>跑法：打开 <c>Scenes/Test_FitSocket.unity</c> → Play → 看 Console。
    /// 场景由菜单 <c>Tools/规则游戏/地图组件/⑮b</c> 生成，测试物自己接线。</para>
    ///
    /// <para><b>它测的是物理，不是规则表</b>：质量 / 摩擦 / 重力都直接改物理参数
    /// （<c>Rigidbody2D.mass</c>、材质摩擦、<c>Physics2D.gravity</c>），因为规则系统本身另有验证菜单。
    /// 这里要证明的是：<b>参数改到位之后，件真的会按策划说的那样动 / 不动</b>。</para>
    /// </remarks>
    public sealed class FitPuzzleSmokeTest : MonoBehaviour
    {
        [Header("被测对象（由生成工具接线）")]
        [SerializeField] private FitBlock _triangle;
        [SerializeField] private FitBlock _ball;
        [SerializeField] private FitSocket _triangleSocket;
        [SerializeField] private FitSocket _ballSocket;
        [SerializeField] private FitPuzzle _puzzle;
        [SerializeField] private Transform _gate;

        [Header("参数")]
        [Tooltip("玩家推力（N）。与 PlayerController._pushStrength 一致。")]
        [SerializeField] private float _pushForce = 60f;

        [Tooltip("单步最多等多久（秒）。")]
        [SerializeField] private float _timeout = 6f;

        private readonly StringBuilder _log = new StringBuilder();
        private int _pass;
        private int _fail;
        private Vector3 _gateClosed;

        /// <summary>接线（生成工具在编辑期调用，字段会写进场景）。</summary>
        public void Configure(FitBlock triangle, FitBlock ball, FitSocket triangleSocket, FitSocket ballSocket,
                              FitPuzzle puzzle, Transform gate)
        {
            _triangle = triangle;
            _ball = ball;
            _triangleSocket = triangleSocket;
            _ballSocket = ballSocket;
            _puzzle = puzzle;
            _gate = gate;
        }

        private IEnumerator Start()
        {
            _gateClosed = _gate != null ? _gate.position : Vector3.zero;
            Vector2 gravity = Physics2D.gravity;

            yield return new WaitForSeconds(0.8f);

            _log.AppendLine("[拼装缺口冒烟] 开始");

            if (_triangle == null || _ball == null || _triangleSocket == null || _ballSocket == null || _puzzle == null)
            {
                Debug.LogError("[拼装缺口冒烟] 接线不全 —— 用菜单 ⑮b 重新生成场景。", this);
                yield break;
            }

            Check("三角块是 Dynamic", _triangle.Body.bodyType == RigidbodyType2D.Dynamic, "bodyType=" + _triangle.Body.bodyType);
            Check("三角块 180 kg", Mathf.Abs(_triangle.Body.mass - 180f) < 0.01f, "mass=" + _triangle.Body.mass.ToString("F1"));
            Check("挂了 RuleAffectedBody", _triangle.GetComponent<RuleAffectedBody>() != null, "");
            Check("在 Ground 层", _triangle.gameObject.layer == LayerMask.NameToLayer("Ground"),
                "layer=" + LayerMask.LayerToName(_triangle.gameObject.layer));

            // --- ① 常规形态推不动
            Vector2 start = _triangle.Body.position;
            yield return Push(_triangle, Vector2.left, 1.5f, null);
            float moved = Vector2.Distance(start, _triangle.Body.position);
            Check("常规形态 60 N 推不动", moved < 0.08f, "位移 " + moved.ToString("F3") + " m");

            // --- ② 摩擦 × 失效：同一块件的第二条路（60 N ÷ 180 kg，慢但推得动）
            Collider2D triangleCollider = _triangle.GetComponent<Collider2D>();
            PhysicsMaterial2D originalMaterial = triangleCollider != null ? triangleCollider.sharedMaterial : null;
            if (originalMaterial != null)
            {
                PhysicsMaterial2D slick = Object.Instantiate(originalMaterial);
                slick.friction = 0f;
                triangleCollider.sharedMaterial = slick;
            }

            Vector2 beforeFriction = _triangle.Body.position;
            yield return Push(_triangle, Vector2.left, 2.5f, null);
            float slickMoved = Vector2.Distance(beforeFriction, _triangle.Body.position);
            Check("摩擦×失效后 180 kg 也推得动（慢）", slickMoved > 0.15f, "位移 " + slickMoved.ToString("F3") + " m");

            if (originalMaterial != null) triangleCollider.sharedMaterial = originalMaterial;
            _triangle.Body.position = start;
            _triangle.Body.linearVelocity = Vector2.zero;
            yield return new WaitForFixedUpdate();

            // --- ③ 质量 × 失效 → 推得动 → 推进凹位
            _triangle.Body.mass = 9f;   // 180 × 0.05
            yield return Push(_triangle, Vector2.left, _timeout * 1.6f, () => _triangleSocket.IsSeated);
            Check("质量×失效后可推入凹位", _triangleSocket.IsSeated,
                "x=" + _triangle.Body.position.x.ToString("F2") + " / 座位 " + _triangleSocket.AnchorPosition.x.ToString("F2"));

            // --- ④ 球在井底：推得动也举不起来
            Check("球在井底（够得着、但举不起来）", _ball.Body.position.y < -0.5f,
                "y=" + _ball.Body.position.y.ToString("F2"));

            float t = 0f;

            // --- ⑤ 重力 × 反转 → 球直上顶进天花板圆窝
            Vector2 triangleBefore = _triangle.Body.position;
            Physics2D.gravity = new Vector2(0f, 9.81f);

            t = 0f;
            while (t < 4f && !_ballSocket.IsSeated)
            {
                t += Time.deltaTime;
                yield return null;
            }

            Check("重力×反转把球顶进圆窝", _ballSocket.IsSeated,
                "y=" + _ball.Body.position.y.ToString("F2") + " / 用时 " + t.ToString("F1") + " s");
            Check("球停在座位高度 y≈5.4", Mathf.Abs(_ball.Body.position.y - 5.4f) < 0.2f,
                "y=" + _ball.Body.position.y.ToString("F2"));
            Check("穿过了井里的跳穿平台（y 越过 3.2）", _ball.Body.position.y > 3.2f,
                "y=" + _ball.Body.position.y.ToString("F2"));
            Check("翻重力没把已入位的三角带飞",
                Vector2.Distance(triangleBefore, _triangle.Body.position) < 0.02f, "");

            // --- ⑥ 重力恢复：球不掉
            Physics2D.gravity = gravity;
            yield return new WaitForSeconds(0.8f);
            Check("重力恢复后球不掉", _ballSocket.IsSeated && Mathf.Abs(_ball.Body.position.y - 5.4f) < 0.2f,
                "y=" + _ball.Body.position.y.ToString("F2"));

            // --- ⑥ 机关解锁
            Check("机关判为已解开", _puzzle.IsSolved, "");

            t = 0f;
            while (t < 3f && _gate != null && _gate.position.y < _gateClosed.y + 1.9f)
            {
                t += Time.deltaTime;
                yield return null;
            }

            Check("闸门升起 2 m", _gate != null && _gate.position.y > _gateClosed.y + 1.8f,
                _gate != null ? "y=" + _gate.position.y.ToString("F2") : "没有闸门");

            // --- 收尾
            if (_fail == 0) Debug.Log("[拼装缺口冒烟] 全部通过 " + _pass + "/" + _pass + "\n" + _log, this);
            else Debug.LogError("[拼装缺口冒烟] 通过 " + _pass + "，失败 " + _fail + "\n" + _log, this);
        }

        private IEnumerator Push(FitBlock block, Vector2 direction, float seconds, System.Func<bool> stopWhen)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (stopWhen != null && stopWhen()) yield break;

                block.ReceivePush(direction * _pushForce);
                t += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
        }

        private void Check(string what, bool ok, string detail)
        {
            if (ok)
            {
                _pass++;
                _log.AppendLine("  [OK] " + what + (string.IsNullOrEmpty(detail) ? string.Empty : "  (" + detail + ")"));
            }
            else
            {
                _fail++;
                _log.AppendLine("  [FAIL] " + what + "  (" + detail + ")");
            }
        }
    }
}
