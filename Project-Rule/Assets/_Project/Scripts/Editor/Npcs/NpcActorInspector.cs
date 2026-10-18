using RuleGame.Gameplay.Npcs;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Npcs
{
    [CustomEditor(typeof(NpcActor)), CanEditMultipleObjects]
    public sealed class NpcActorInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (targets.Length != 1) return;
            NpcActor actor = (NpcActor)target;
            if (actor.Definition != null)
                EditorGUILayout.HelpBox(actor.Definition.MissingAnimations, MessageType.Info);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                EditorGUILayout.LabelField("当前状态", actor.State.ToString());
                if (GUILayout.Button("试播当前条件（模拟近距离交互）")) actor.TryInteract(actor.transform);
                if (GUILayout.Button("模拟关闭交易界面")) actor.CloseTrade();
            }
        }
    }
}
