using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleGame.Editor.Level
{
    /// <summary>仅列机关的魔王城平铺菜单；保留 Prefab 连接、撤销和 Prefab Stage 放置。</summary>
    public static class DemonCastleMenu
    {
        private static Vector3 Center()
        {
            Vector3 point = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            point.z = 0f; return point;
        }
        public static GameObject PlaceAt(string name, Vector3 position, Transform parent = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return null;
            string path = DemonCastleBuilder.Folder + "/" + name + ".prefab";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                DemonCastleBuilder.BuildPrefabs(); asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) throw new InvalidOperationException("无法加载预制体：" + path);
            }
            if (parent != null && EditorUtility.IsPersistent(parent)) throw new InvalidOperationException("父物体必须属于当前场景。");
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && parent == null) parent = stage.prefabContentsRoot.transform;
            Scene scene = parent != null ? parent.gameObject.scene : SceneManager.GetActiveScene();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("放置魔王城组件");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            Undo.RegisterCreatedObjectUndo(instance, "放置魔王城组件");
            if (parent != null) Undo.SetTransformParent(instance.transform, parent, "设置组件父物体");
            position.z = 0f; instance.transform.position = position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            Undo.CollapseUndoOperations(group); Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene); return instance;
        }
        private static void Quick(string name) => PlaceAt(name, Center());
        [MenuItem("Tools/规则游戏/地图组件/魔王城/烟花箱（需连接开关）", false, 104)]
        public static void Place4() => Quick("DemonFirework");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/烟花箱与踩踏开关（已接线）", false, 105)]
        public static void Place5() => Quick("DemonFireworkSwitch");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/跳跃弹簧", false, 106)]
        public static void Place6() => Quick("DemonSpring");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/生锈铁块", false, 107)]
        public static void Place7() => Quick("DemonRustBlock");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/钟乳石·短", false, 108)]
        public static void Place8() => Quick("DemonStalactiteShort");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/钟乳石·中", false, 109)]
        public static void Place9() => Quick("DemonStalactiteMedium");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/钟乳石·长", false, 110)]
        public static void Place10() => Quick("DemonStalactiteLong");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/翻转平台", false, 111)]
        public static void Place11() => Quick("DemonFlipPlatform");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/横向发条（需指定拼图）", false, 112)]
        public static void Place12() => Quick("DemonWindingHorizontal");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/纵向发条（需指定拼图）", false, 113)]
        public static void Place13() => Quick("DemonWindingVertical");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/拼图本体（需配发条）", false, 114)]
        public static void Place14() => Quick("DemonSlidingPuzzle");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/拼图房（已接线）", false, 115)]
        public static void Place15() => Quick("DemonPuzzleRoom");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/升降木马", false, 116)]
        public static void Place16() => Quick("DemonCarousel");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/钢琴白键（需指定曲调锁）", false, 117)]
        public static void Place17() => Quick("DemonPianoWhite");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/钢琴黑键（需指定曲调锁）", false, 118)]
        public static void Place18() => Quick("DemonPianoBlack");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/钢琴房（已接线）", false, 119)]
        public static void Place19() => Quick("DemonPianoRoom");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/滑板车", false, 120)]
        public static void Place20() => Quick("DemonSkateboard");
    }
}
