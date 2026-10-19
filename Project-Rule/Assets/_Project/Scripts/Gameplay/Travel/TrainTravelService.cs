using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Travel
{
    /// <summary>
    /// 火车快速传送的唯一入口：开门禁、攒列表、黑场、落人、刷复活点。
    /// </summary>
    /// <remarks>
    /// <para><b>它不是 UI</b>：面板住在 <c>RuleGame.UI</c>，靠 <see cref="Opened"/> 事件接上；
    /// 这样依赖方向仍然是 UI → Gameplay，玩法层不认识任何 VisualElement。</para>
    /// <para><b>门禁分两段</b>：面板开着的时候锁输入 + 把时间压到 0.001（与规则轮盘同一套
    /// <c>PushScale(TimeSource.UI, …)</c>，不用 <c>SetPaused</c> —— 那是正式暂停的语义）；
    /// 确认之后一直持到画面重新亮起来，避免玩家在黑屏里乱走。</para>
    /// <para><b>跨场景</b>：目的地不在场时先让 <see cref="WorldStreamer"/> 把分区加载出来，
    /// 再按编号找站。加载期间黑场必须一直盖着 —— 这正是 <see cref="IScreenFade"/> 存在的理由。</para>
    /// <para><b>没接黑场也能跑</b>：<c>_fadeBehaviour</c> 留空就直接瞬移，表现后接。</para>
    /// </remarks>
    [DefaultExecutionOrder(-10)]
    [DisallowMultipleComponent]
    public sealed class TrainTravelService : MonoBehaviour
    {
        public static TrainTravelService Instance { get; private set; }

        [Header("数据")]
        [Tooltip("全量站点目录。留空 = 只能传送到当前在场的站。")]
        [SerializeField] private TrainStationCatalog _catalog;

        [Tooltip("跨场景传送要用的分区加载器。留空 = 不跨场景。")]
        [SerializeField] private WorldStreamer _streamer;

        [Header("表现")]
        [Tooltip("黑场实现（Presentation 里的 SwingCheckpointFadeScreen）。留空 = 直接瞬移。")]
        [SerializeField] private MonoBehaviour _fadeBehaviour;

        [Tooltip("黑屏期间留给相机追平的时间（秒）。相机是阻尼跟随，瞬移 14 米要一会儿才停稳。")]
        [SerializeField, Min(0f)] private float _settleSeconds = 0.5f;

        [Header("门禁")]
        [Tooltip("面板打开时的时间倍率（与规则轮盘一致）。")]
        [SerializeField, Range(0.001f, 1f)] private float _uiTimeScale = 0.001f;

        [Tooltip("判定「打的是哪一站」的半径：一次挥砍同时罩到两台时，取离玩家最近的这台。")]
        [SerializeField, Min(0.5f)] private float _openRange = 3f;

        private readonly List<TrainDestination> _destinations = new List<TrainDestination>();
        private readonly List<string> _seen = new List<string>();

        private IScreenFade _fade;
        private IDisposable _inputLease;
        private PlayerController _player;
        private bool _uiScalePushed;

        /// <summary>玩家身上的车票；没有就还没有任何站被解锁。</summary>
        private TrainPass Pass => _player != null ? _player.GetComponent<TrainPass>() : null;

        /// <summary>面板此刻开着。</summary>
        public bool IsOpen { get; private set; }

        /// <summary>正在传送（黑场里）。</summary>
        public bool IsTravelling { get; private set; }

        /// <summary>面板开着或在传送：两种情况都不接受新的请求。</summary>
        public bool IsBusy => IsOpen || IsTravelling;

        /// <summary>打开面板的那一站（可能是被就近校验替换过的站）。</summary>
        public TrainStation Current { get; private set; }

        /// <summary>本次面板要显示的列表（已排序）。</summary>
        public IReadOnlyList<TrainDestination> Destinations => _destinations;

        /// <summary>面板该开了。</summary>
        public event Action<TrainStation> Opened;

        /// <summary>面板关了（取消 / 开始传送 / 被动关闭）。</summary>
        public event Action Closed;

        /// <summary>出错，参数是给人看的一句话。</summary>
        public event Action<string> Failed;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            Instance = this;
            _fade = _fadeBehaviour as IScreenFade;

            // 常驻宿主可能在另一个场景里（Boot 的 GameRoot），编辑期接不上就运行时找。
            if (_streamer == null) _streamer = FindAnyObjectByType<WorldStreamer>();

            if (_fadeBehaviour != null && _fade == null)
            {
                Debug.LogError("[TrainTravel] 黑场引用没有实现 IScreenFade，已按「没有黑场」处理。", this);
            }
        }

        private void OnDestroy()
        {
            _fade?.Clear();
            ReleaseGates();

            if (Instance == this) Instance = null;
        }

        private void OnDisable()
        {
            // 传送途中对象被停用：协程会停，门禁必须手动收，否则输入永远锁着、画面永远黑着。
            if (!IsOpen && !IsTravelling) return;

            Debug.LogWarning("[TrainTravel] 传送服务在传送途中被停用，已强制收尾。", this);
            _fade?.Clear();

            IsOpen = false;
            IsTravelling = false;
            Current = null;
            ReleaseGates();
        }

        // ==================================================================
        //  打开 / 关闭
        // ==================================================================

        /// <summary>
        /// 请求打开线路面板。<b>返回 false 不代表出错</b>：已经在忙、玩家死了、被 UI 占用，
        /// 都走这一条。
        /// </summary>
        public bool RequestOpen(TrainStation station)
        {
            if (station == null || IsBusy) return false;

            // 没启用的车站完全不能用：连「在这里上车去别处」都不行。
            if (!station.IsUnlocked) return false;

            if (TimeManager.Instance?.IsPaused == true) return false;

            // 没有面板订阅就别开门禁：否则输入被锁、时间被压住，却没有东西能关掉它。
            if (Opened == null)
            {
                Debug.LogWarning("[TrainTravel] 没有 UI 订阅 Opened，线路面板不可用（场景里缺 TrainTravelPanel）。", this);
                return false;
            }

            _player = FindAnyObjectByType<PlayerController>();
            if (_player == null || _player.IsGameplayInputSuppressed) return false;

            var status = _player.GetComponent<EntityStatus>();
            if (status != null && status.Health <= 0f) return false;

            // 一次挥砍可能同时罩到两台车：按物理顺序调过来不保证是最近的那台，这里改判一次。
            Current = TrainNetwork.Nearest(_player.transform.position, _openRange) ?? station;

            // 就近换过来的那台也可能是锁着的 —— 同样不开。
            if (!Current.IsUnlocked) return false;

            IsOpen = true;
            BuildDestinations();
            AcquireGates();

            Opened?.Invoke(Current);
            return true;
        }

        /// <summary>关掉面板（取消 / 传到一半被打断）。</summary>
        public void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            Current = null;
            ReleaseGates();
            Closed?.Invoke();
        }

        // ==================================================================
        //  传送
        // ==================================================================

        /// <summary>面板确认后调用：黑场 → （按需加载）→ 落人 → 刷复活点 → 亮起来。</summary>
        public void Travel(string destinationId)
        {
            if (string.IsNullOrEmpty(destinationId) || IsTravelling) return;

            // 解锁是硬门禁：面板已经置灰了，这里再兜一次 —— 别让别的脚本绕过 UI 直接传。
            if (Pass == null || !Pass.IsUnlocked(destinationId))
            {
                Failed?.Invoke("「" + NameOf(destinationId) + "」还没解锁。先到现场把它启动。");
                return;
            }

            StartCoroutine(TravelRoutine(destinationId));
        }

        private string NameOf(string destinationId)
        {
            for (int i = 0; i < _destinations.Count; i++)
            {
                if (_destinations[i].Id == destinationId) return _destinations[i].Name;
            }

            return destinationId;
        }

        private IEnumerator TravelRoutine(string destinationId)
        {
            IsTravelling = true;
            IsOpen = false;

            string scenePath = null;
            if (_catalog != null && _catalog.TryGet(destinationId, out TrainStationCatalog.Entry entry))
            {
                scenePath = entry.ScenePath;
            }

            if (_player == null) _player = FindAnyObjectByType<PlayerController>();
            if (_player == null)
            {
                FinishTravel(scenePath, "找不到玩家，传送取消。");
                yield break;
            }

            Closed?.Invoke();

            if (_fade != null) yield return _fade.FadeTo(1f);

            TrainStation target = TrainNetwork.Find(destinationId);

            // 目标不在场：先把它的分区加载出来。加载期间画面必须盖着。
            if (target == null && _streamer != null && !string.IsNullOrEmpty(scenePath))
            {
                yield return _streamer.EnsureLoaded(scenePath);

                // 新场景的 OnEnable 在加载完成那一帧才跑，等一帧再找。
                yield return null;
                target = TrainNetwork.Find(destinationId);
            }

            if (target == null)
            {
                FinishTravel(scenePath, "站点「" + destinationId + "」不在场，也没法加载。");
                yield break;
            }

            _player.TeleportTo(target.RidePosition);
            target.NotifyArrived(_player);

            // 世界先恢复（相机是阻尼跟随，倍率还压着就永远追不上），黑屏再留一小段。
            ResumeWorld();

            // ⚠️ 用非缩放时间，不用 WaitForSeconds：它是缩放时间 —— 只要黑屏期间有人把
            // TimeSource.UI 压到 0.001（规则轮盘就是），这 0.5 秒会变成几分钟的「只有淡入没有淡出」。
            float settle = 0f;
            while (settle < _settleSeconds)
            {
                settle += UnityEngine.Time.unscaledDeltaTime;
                yield return null;
            }

            if (_fade != null) yield return _fade.FadeTo(0f);

            FinishTravel(scenePath, null);
        }

        private void FinishTravel(string scenePath, string error)
        {
            if (_fade != null && error != null)
            {
                // 失败时也要把黑场放回去，不能把人卡在黑屏里。
                StartCoroutine(_fade.FadeTo(0f));
            }

            if (!string.IsNullOrEmpty(scenePath) && _streamer != null) _streamer.Unpin(scenePath);

            Current = null;
            ResumeWorld();
            ReleaseGates();
            IsTravelling = false;

            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning("[TrainTravel] " + error, this);
                Failed?.Invoke(error);
            }
        }

        // ==================================================================
        //  列表
        // ==================================================================

        private void BuildDestinations()
        {
            _destinations.Clear();
            _seen.Clear();

            string currentId = Current != null ? Current.Id : null;
            TrainPass pass = Pass;

            // 1) 目录：跨场景的目的地只有它知道。
            if (_catalog != null)
            {
                for (int i = 0; i < _catalog.Count; i++)
                {
                    TrainStationCatalog.Entry entry = _catalog[i];
                    if (string.IsNullOrEmpty(entry.Id) || _seen.Contains(entry.Id)) continue;

                    _seen.Add(entry.Id);
                    bool loaded = TrainNetwork.Find(entry.Id) != null || IsSceneLoaded(entry.ScenePath);
                    bool locked = pass == null || !pass.IsUnlocked(entry.Id);
                    string name = string.IsNullOrEmpty(entry.DisplayName) ? entry.Id : entry.DisplayName;
                    _destinations.Add(new TrainDestination(entry.Id, name, entry.ScenePath, entry.Order, loaded, entry.Id == currentId, locked));
                }
            }

            // 2) 在场的站：没进目录的白盒站也能选。
            IReadOnlyList<TrainStation> stations = TrainNetwork.Stations;
            for (int i = 0; i < stations.Count; i++)
            {
                TrainStation station = stations[i];
                if (station == null || _seen.Contains(station.Id)) continue;

                _seen.Add(station.Id);
                string scenePath = station.gameObject.scene.path;
                bool locked = pass == null || !pass.IsUnlocked(station.Id);
                _destinations.Add(new TrainDestination(station.Id, station.DisplayName, scenePath, station.Order, true, station.Id == currentId, locked));
            }

            _destinations.Sort((a, b) =>
            {
                if (a.Order != b.Order) return a.Order.CompareTo(b.Order);
                return string.CompareOrdinal(a.Id, b.Id);
            });
        }

        private bool IsSceneLoaded(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return false;
            if (_streamer != null && _streamer.IsSceneLoaded(scenePath)) return true;

            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
            return scene.IsValid() && scene.isLoaded;
        }

        // ==================================================================
        //  门禁
        // ==================================================================

        private void AcquireGates()
        {
            if (_player != null && _inputLease == null) _inputLease = _player.BlockGameplayInput();

            if (!_uiScalePushed && TimeManager.Instance != null)
            {
                TimeManager.Instance.PushScale(TimeSource.UI, _uiTimeScale);
                _uiScalePushed = true;
            }
        }

        /// <summary>只撤时间倍率，输入继续锁着（传送中还要用）。</summary>
        private void ResumeWorld()
        {
            if (!_uiScalePushed || TimeManager.Instance == null) return;

            TimeManager.Instance.RemoveScale(TimeSource.UI);
            _uiScalePushed = false;
        }

        private void ReleaseGates()
        {
            ResumeWorld();

            _inputLease?.Dispose();
            _inputLease = null;
        }
    }
}
