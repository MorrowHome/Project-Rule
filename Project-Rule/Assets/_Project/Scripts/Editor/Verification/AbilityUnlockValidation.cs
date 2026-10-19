using System.Reflection;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure.Rules;
using RuleGame.UI.RuleWheel;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.Editor.Verification
{
    /// <summary>能力位 / 规则属性×关系 / 宝箱领取 / LevelRuntime 接线的一次性自检。</summary>
    public static class AbilityUnlockValidation
    {
        [MenuItem("规则游戏/验证/能力与词条解锁（纯函数 + 预制体接线）")]
        public static void Run()
        {
            int passed = 0;
            var root = new GameObject("AbilityUnlockValidation");
            try
            {
                var progress = root.AddComponent<PlayerProgress>();
                progress.StartNewGame();

                Check(progress.IsUnlocked(AttributeId.Gravity, RelationId.Revert), "开局只有重力×反转", ref passed);
                Check(!progress.IsUnlocked(AttributeId.Friction, RelationId.Revert), "摩擦开局锁着", ref passed);
                Check(!progress.IsUnlocked(AttributeId.Gravity, RelationId.Nullify), "失效开局锁着", ref passed);

                int attributeEvents = 0, relationEvents = 0;
                progress.AttributeUnlocked += _ => attributeEvents++;
                progress.RelationUnlocked += _ => relationEvents++;

                Check(progress.UnlockAttribute(AttributeId.Friction), "解锁摩擦属性", ref passed);
                Check(progress.IsUnlocked(AttributeId.Friction, RelationId.Revert), "摩擦×反转可用", ref passed);
                Check(!progress.IsUnlocked(AttributeId.Friction, RelationId.Nullify), "摩擦×失效仍锁着", ref passed);

                Check(progress.UnlockRelation(RelationId.Nullify), "解锁失效关系", ref passed);
                Check(progress.IsUnlocked(AttributeId.Friction, RelationId.Nullify), "摩擦×失效可用", ref passed);
                Check(!progress.IsUnlocked(AttributeId.Mass, RelationId.Nullify), "质量×失效仍锁着", ref passed);
                Check(attributeEvents == 1 && relationEvents == 1, "解锁事件各一次", ref passed);

                const string id = "validation-chest";
                Check(progress.TryGrant(id, new CityReward(CityRewardKind.RuleAttribute, attribute: AttributeId.Mass)), "宝箱发质量属性", ref passed);
                Check(progress.IsClaimed(id), "宝箱编号已记账", ref passed);
                Check(!progress.TryGrant(id, new CityReward(CityRewardKind.RuleRelation, relation: RelationId.Enhance)), "同一编号不重复领取", ref passed);

                Check((PlayerAbility.All & PlayerAbility.WallClimb) != 0, "All 包含爬墙", ref passed);
                Check((PlayerAbility.All & PlayerAbility.AirJump) != 0, "All 包含二段跳", ref passed);

                // 攻击宝箱：不按 E，直接走 IContactSwitch 的撞击入口。
                var chestObject = new GameObject("AbilityChestValidation");
                try
                {
                    var chest = chestObject.AddComponent<CityRewardSource>();
                    chest.SourceId = "validation-attack-chest";
                    chest.Kind = CityRewardKind.RuleAttribute;
                    chest.Attribute = AttributeId.Time;
                    chest.OpenByAttack = true;
                    chest.Receiver = progress;
                    Check(!chest.Available, "攻击宝箱不响应 E（Available=false）", ref passed);

                    int eventsBeforeImpact = attributeEvents;
                    chest.ImpactByContact(Vector2.zero);
                    Check(progress.IsUnlocked(AttributeId.Time, RelationId.Revert), "攻击宝箱解锁时间属性", ref passed);
                    Check(progress.IsClaimed("validation-attack-chest"), "攻击宝箱编号已记账", ref passed);

                    chest.ImpactByContact(Vector2.zero);
                    Check(attributeEvents == eventsBeforeImpact + 1, "同一宝箱只发一次奖", ref passed);
                }
                finally
                {
                    Object.DestroyImmediate(chestObject);
                }

                // 直接读 BuildTuning：锁住时不该打包出二段跳 / 贴墙参数。
                var contents = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/LevelRuntime.prefab");
                try
                {
                    var runtimePlayer = contents.GetComponentInChildren<PlayerController>(true);
                    MethodInfo build = typeof(PlayerController).GetMethod("BuildTuning",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    runtimePlayer.SetUnlockedAbilities(PlayerAbility.None);
                    var locked = (MotionTuning)build.Invoke(runtimePlayer, null);
                    Check(locked.MaxAirJumps == 0 && !locked.WallClimbEnabled, "锁住时打包参数没有二段跳 / 贴墙", ref passed);

                    runtimePlayer.SetUnlockedAbilities(PlayerAbility.All);
                    var unlocked = (MotionTuning)build.Invoke(runtimePlayer, null);
                    Check(unlocked.MaxAirJumps == 1 && unlocked.WallClimbEnabled, "解锁后打包参数恢复二段跳 / 贴墙", ref passed);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/LevelRuntime.prefab");
                var player = prefab != null ? prefab.GetComponentInChildren<PlayerController>(true) : null;
                Check(player != null, "LevelRuntime 有玩家", ref passed);

                var prefabProgress = player != null ? player.GetComponent<PlayerProgress>() : null;
                Check(prefabProgress != null, "玩家挂了 PlayerProgress", ref passed);

                var wheel = prefab != null ? prefab.GetComponentInChildren<RuleWheelPresenter>(true) : null;
                Check(wheel != null, "有规则轮盘", ref passed);
                if (wheel != null && prefabProgress != null)
                {
                    var so = new SerializedObject(wheel);
                    Check(so.FindProperty("_unlocks").objectReferenceValue == prefabProgress, "轮盘指向 PlayerProgress", ref passed);
                }

                if (player != null)
                {
                    var so = new SerializedObject(player);
                    Check(so.FindProperty("_unlockedAbilities").intValue == (int)PlayerAbility.All,
                        "预制体默认能力全开（正式开局由 GameRoot 锁）", ref passed);
                }

                if (prefabProgress != null)
                {
                    var so = new SerializedObject(prefabProgress);
                    Check(so.FindProperty("_unlockAllRulesForDevelopment").boolValue,
                        "开发期规则全开开关默认勾上", ref passed);
                }

                var vitalsUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Launch/PlayerVitals.uxml");
                Check(vitalsUxml != null, "PlayerVitals.uxml 可加载", ref passed);
                if (vitalsUxml != null)
                {
                    var tree = vitalsUxml.Instantiate();
                    Check(tree.Q("unlockToast") != null, "解锁提示标签存在", ref passed);
                }

                Debug.Log("[AbilityUnlock] 通过 " + passed + " 项");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Check(bool condition, string label, ref int passed)
        {
            if (!condition) throw new System.InvalidOperationException("[AbilityUnlock] 失败：" + label);
            passed++;
        }
    }
}
