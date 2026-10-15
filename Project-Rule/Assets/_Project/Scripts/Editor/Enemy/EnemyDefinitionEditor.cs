using System.Collections.Generic;
using RuleGame.Gameplay.Enemy;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Enemy
{
    /// <summary>仅翻译编辑器显示，不重命名序列化字段；保留范围约束、Undo 和多对象编辑。</summary>
    [CustomEditor(typeof(EnemyDefinition)), CanEditMultipleObjects]
    public sealed class EnemyDefinitionEditor : UnityEditor.Editor
    {
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { nameof(EnemyDefinition.Kind), "敌人类型" },
            { nameof(EnemyDefinition.DisplayName), "显示名称" },
            { nameof(EnemyDefinition.BodySize), "身体碰撞尺寸" },
            { nameof(EnemyDefinition.Health), "最大生命" },
            { nameof(EnemyDefinition.Damage), "基础伤害" },
            { nameof(EnemyDefinition.ContactDamage), "启用接触伤害" },
            { nameof(EnemyDefinition.ContactInterval), "接触伤害间隔（秒）" },
            { nameof(EnemyDefinition.ContactPadding), "接触判定外扩距离" },
            { nameof(EnemyDefinition.Energy), "最大能量" },
            { nameof(EnemyDefinition.EnergyRegen), "每秒能量恢复" },
            { nameof(EnemyDefinition.RuleAttribute), "施法属性" },
            { nameof(EnemyDefinition.RuleRelation), "施法关系" },
            { nameof(EnemyDefinition.MoveSpeed), "基础移动速度" },
            { nameof(EnemyDefinition.AttackRange), "近战攻击距离" },
            { nameof(EnemyDefinition.AttackCooldown), "攻击冷却（秒）" },
            { nameof(EnemyDefinition.BossRestDuration), "进攻后休息时长（秒）" },
            { nameof(EnemyDefinition.ClockBeamVolleysBeforeRest), "每轮指针攻击次数" },
            { nameof(EnemyDefinition.BrickAdvanceDuration), "压近最长时长（秒）" },
            { nameof(EnemyDefinition.BrickAdvanceSpeedMultiplier), "压近速度倍率" },
            { nameof(EnemyDefinition.BrickAdvanceCooldown), "压近冷却（秒）" },
            { nameof(EnemyDefinition.BrickComboLimit), "后续阶段连招上限" },
            { nameof(EnemyDefinition.ClockChargeDuration), "冲撞最长时长（秒）" },
            { nameof(EnemyDefinition.ClockChargeSpeedMultiplier), "冲撞速度倍率" },
            { nameof(EnemyDefinition.ClockChargeCooldown), "冲撞冷却（秒）" },
            { nameof(EnemyDefinition.ClockBeamWarning), "激光预警时长（秒）" },
            { nameof(EnemyDefinition.ClockBeamSpread), "激光封路夹角（度）" },
            { nameof(EnemyDefinition.RabbitBurrowDuration), "藏身撤退最长时长（秒）" },
            { nameof(EnemyDefinition.RabbitBurrowSpeedMultiplier), "藏身撤退速度倍率" },
            { nameof(EnemyDefinition.RabbitBurrowCooldown), "藏身撤退冷却（秒）" },
            { nameof(EnemyDefinition.RabbitEscapeDistance), "触发藏身的近身距离" },
            { nameof(EnemyDefinition.RabbitBurstCount), "翻转后散射轮数" },
            { nameof(EnemyDefinition.RabbitBurstInterval), "散射轮次间隔（秒）" },
            { nameof(EnemyDefinition.PreferredDistance), "理想作战距离" },
            { nameof(EnemyDefinition.SpacingTolerance), "距离调整缓冲" },
            { nameof(EnemyDefinition.RangedRange), "远程攻击距离" },
            { nameof(EnemyDefinition.RetreatSpeedMultiplier), "后撤速度倍率" },
            { nameof(EnemyDefinition.StaggerDamageThreshold), "累计破韧伤害阈值" },
            { nameof(EnemyDefinition.StaggerResetDelay), "破韧累计重置间隔（秒）" },
            { nameof(EnemyDefinition.HurtCooldown), "硬直保护冷却（秒）" },
            { nameof(EnemyDefinition.BossHurtDuration), "普通受击硬直上限（秒）" },
            { nameof(EnemyDefinition.GravityScale), "自身重力倍率" },
            { nameof(EnemyDefinition.Flying), "启用飞行移动" },
            { nameof(EnemyDefinition.FlightAcceleration), "飞行加速度" },
            { nameof(EnemyDefinition.BatOrbitDistance), "空中换位水平距离" },
            { nameof(EnemyDefinition.BatHoverHeight), "战斗换位高度差" },
            { nameof(EnemyDefinition.BatDashCooldown), "俯冲冷却（秒）" },
            { nameof(EnemyDefinition.BatDashWindup), "俯冲蓄力时长（秒）" },
            { nameof(EnemyDefinition.BatDashDuration), "俯冲最长时长（秒）" },
            { nameof(EnemyDefinition.BatEvadeCooldown), "近身撤离冷却（秒）" },
            { nameof(EnemyDefinition.BatEvadeDuration), "近身撤离时长（秒）" },
            { nameof(EnemyDefinition.BatRecoverDuration), "俯冲后恢复时长（秒）" },
            { nameof(EnemyDefinition.BatApproachDistance), "主动近身水平距离" },
            { nameof(EnemyDefinition.BatApproachHeight), "主动近身高度差" },
            { nameof(EnemyDefinition.BatApproachDuration), "尝试近身时长（秒）" },
            { nameof(EnemyDefinition.BatApproachSpeedMultiplier), "主动近身速度倍率" },
            { nameof(EnemyDefinition.BatRestHeightRatio), "休息巡飞高度比例" },
            { nameof(EnemyDefinition.BatRestPatrolDistance), "休息巡飞单侧距离" },
            { nameof(EnemyDefinition.BatRestSpeedMultiplier), "休息巡飞速度倍率" },
            { nameof(EnemyDefinition.ImpactFraction), "动作命中时点比例" },
            { nameof(EnemyDefinition.RabbitCycle), "兔子周期藏身间隔（秒）" },
            { nameof(EnemyDefinition.RabbitThrowInterval), "普通投掷动作时长（秒）" },
            { nameof(EnemyDefinition.ClockHourInterval), "指针旋转基础周期（秒）" },
            { nameof(EnemyDefinition.ClockCoreWindow), "核心暴露时长（秒）" },
            { nameof(EnemyDefinition.ClockCoreHealth), "核心生命" },
            { nameof(EnemyDefinition.Timings), "动作时长配置" },
        };
        private static readonly Dictionary<string, string> EnumLabels = new Dictionary<string, string>
        {
            { "BrickMinion", "积木小怪" }, { "BrickBoss", "积木 Boss" }, { "ClockBoss", "发条 Boss" },
            { "RabbitBoss", "兔子 Boss" }, { "BatBoss", "蝙蝠 Boss" }, { "BatMinion", "蝙蝠小怪" },
            { "Gravity", "重力" }, { "Time", "时间" }, { "Friction", "摩擦" },
            { "Mass", "质量" }, { "Damage", "伤害" }, { "Causality", "因果" },
            { "Revert", "反转" }, { "Nullify", "失效" }, { "Constant", "恒定" },
            { "Enhance", "增强" }, { "Weaken", "削弱" }
        };
        private readonly List<SerializedProperty> _properties = new List<SerializedProperty>();
        private void OnEnable()
        {
            _properties.Clear();
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                _properties.Add(iterator.Copy());
            }
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            foreach (var property in _properties)
            {
                if (property.name == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(property, new GUIContent("脚本"));
                    continue;
                }
                string caption = Labels.TryGetValue(property.name, out string translated) ? translated : property.displayName;
                var label = new GUIContent(caption, property.tooltip);
                if (property.propertyType == SerializedPropertyType.Enum) DrawEnum(property, label);
                else if (property.name == nameof(EnemyDefinition.Timings)) DrawTimings(property, label);
                else EditorGUILayout.PropertyField(property, label, true);
            }
            serializedObject.ApplyModifiedProperties();
        }
        private static void DrawEnum(SerializedProperty property, GUIContent label)
        {
            string[] names = property.enumNames;
            var options = new GUIContent[names.Length];
            for (int i = 0; i < names.Length; i++)
                options[i] = new GUIContent(EnumLabels.TryGetValue(names[i], out string translated) ? translated : names[i]);
            bool mixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup(label, property.enumValueIndex, options);
            if (EditorGUI.EndChangeCheck()) property.enumValueIndex = selected;
            EditorGUI.showMixedValue = mixed;
        }
        private static void DrawTimings(SerializedProperty property, GUIContent label)
        {
            property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, label, true);
            if (!property.isExpanded) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(property.FindPropertyRelative("Array.size"), new GUIContent("动作数量"));
                for (int i = 0; i < property.arraySize; i++)
                {
                    var entry = property.GetArrayElementAtIndex(i);
                    EditorGUILayout.LabelField($"动作 {i + 1}", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("Action"), new GUIContent("动作标识", "与动画动作名保持一致，例如 Slash。"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("Duration"), new GUIContent("时长（秒）"));
                }
            }
        }
    }
}
