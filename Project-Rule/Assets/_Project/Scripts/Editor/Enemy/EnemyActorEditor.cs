using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using RuleGame.Gameplay.Enemy;

namespace RuleGame.Editor.Enemy
{
    [CustomEditor(typeof(EnemyActor)), CanEditMultipleObjects]
    public sealed class EnemyActorEditor : UnityEditor.Editor
    {
        private readonly BoxBoundsHandle _bounds = new BoxBoundsHandle();

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new HelpBox("拖入关卡即可自动寻找玩家、接入场景伤害结算。索敌和追击仍受配置中的距离限制。", HelpBoxMessageType.Info));
            Add(root, "Definition", "敌人配置（生命、攻击、索敌等）");
            Add(root, "UseSpawnRelativeArena", "以放置点设置活动范围");
            var relative = new VisualElement();
            Add(relative, "ArenaSize", "活动范围宽高");
            Add(relative, "ArenaOffset", "范围中心偏移");
            relative.Add(new HelpBox("黄色框是移动和寻路边界，可在 Scene 视图拖拽边框。运行时固定在出生点，不随敌人移动。", HelpBoxMessageType.None));
            root.Add(relative);
            var world = new VisualElement();
            Add(world, "ArenaX", "左右边界（世界坐标）");
            Add(world, "ArenaY", "上下边界（世界坐标）");
            root.Add(world);
            var mode = serializedObject.FindProperty("UseSpawnRelativeArena");
            void UpdateMode(SerializedProperty property)
            {
                relative.style.display = property.boolValue || property.hasMultipleDifferentValues ? DisplayStyle.Flex : DisplayStyle.None;
                world.style.display = !property.boolValue || property.hasMultipleDifferentValues ? DisplayStyle.Flex : DisplayStyle.None;
            }
            UpdateMode(mode);
            root.TrackPropertyValue(mode, UpdateMode);

            var advanced = new Foldout { text = "高级设置（通常无需修改）", value = false };
            var primary = new HashSet<string> { "m_Script", "Definition", "UseSpawnRelativeArena", "ArenaSize", "ArenaOffset", "ArenaX", "ArenaY" };
            var iterator = serializedObject.GetIterator();
            if (iterator.NextVisible(true))
            {
                do
                {
                    if (!primary.Contains(iterator.name)) advanced.Add(new PropertyField(iterator.Copy()));
                } while (iterator.NextVisible(false));
            }
            root.Add(advanced);
            return root;
        }

        private void Add(VisualElement parent, string property, string label)
            => parent.Add(new PropertyField(serializedObject.FindProperty(property), label));

        private void OnSceneGUI()
        {
            if (Application.isPlaying || targets.Length != 1) return;
            var actor = (EnemyActor)target;
            if (!actor.UseSpawnRelativeArena) return;
            _bounds.axes = PrimitiveBoundsHandle.Axes.X | PrimitiveBoundsHandle.Axes.Y;
            _bounds.center = actor.ArenaAnchor + actor.ArenaOffset;
            _bounds.size = new Vector3(actor.ArenaSize.x, actor.ArenaSize.y, 0f);
            _bounds.SetColor(new Color(1f, .75f, .15f));
            EditorGUI.BeginChangeCheck();
            _bounds.DrawHandle();
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(actor, "调整小怪活动范围");
            Vector2 minimum = actor.Definition != null ? actor.Definition.BodySize + Vector2.one * .1f : Vector2.one * .1f;
            actor.ArenaSize = new Vector2(Mathf.Max(minimum.x, _bounds.size.x), Mathf.Max(minimum.y, _bounds.size.y));
            actor.ArenaOffset = (Vector2)_bounds.center - actor.ArenaAnchor;
            PrefabUtility.RecordPrefabInstancePropertyModifications(actor);
            EditorUtility.SetDirty(actor);
        }
    }
}
