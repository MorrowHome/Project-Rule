using System;
using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Enemy;
using RuleGame.Presentation.Enemy;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Editor.Enemy
{
    /// <summary>补接交付弹体/激光，修复召唤物实体配置；不重建白盒、主角或战斗参数。</summary>
    public static class EnemyDeliverySetup
    {
        [MenuItem("RuleGame/Enemies/补齐交付弹体、激光与小兵碰撞")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play，再补接交付素材。");
            AssetDatabase.Refresh();
            EnemyArtImporter.ImportSequences("ProjectileCube", "BeamThin", "BeamMedium", "BeamThick");
            EnemyArtManifest manifest = EnemyArtImporter.Load();
            Configure(EnemyKind.BrickBoss, EnemyLabBuilder.CreateAnimationSet(EnemyKind.BrickBoss, manifest));
            Configure(EnemyKind.ClockBoss, EnemyLabBuilder.CreateAnimationSet(EnemyKind.ClockBoss, manifest));
            var brick = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/BrickBoss_Definition.asset");
            brick.RuleAttribute = AttributeId.Mass; brick.RuleRelation = RelationId.Enhance;
            EditorUtility.SetDirty(brick);
            foreach (EnemyKind kind in new[] { EnemyKind.BrickMinion, EnemyKind.BatMinion }) RepairMinion(kind);
            AssetDatabase.SaveAssets();
            Debug.Log("[EnemyDelivery] 四帧积木、三组共二十一帧激光已接入，小兵实体碰撞已接线；场景与主角未重建。");
        }
        internal static void Configure(EnemyKind kind, EnemyAnimationSet animations)
        {
            string root = "Assets/_Project/Data/Enemies/";
            var profile = AssetDatabase.LoadAssetAtPath<EnemyVfxProfile>(root + "Vfx/" + kind + "_Vfx.asset");
            if (profile == null || animations == null) return;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(root + "EnemySprite_Mat.mat");
            if (kind == EnemyKind.BrickBoss)
            {
                EnemySpriteSequence cube = Array.Find(animations.Sequences, s => s.Action == "ProjectileCube");
                if (cube == null) return; // 尚未运行补充导入时，允许旧版特效安装流程继续。
                if (cube.Frames.Length != 4) throw new InvalidOperationException("旋转方块应有四帧。");
                profile.ProjectileAnimations = new[] { cube };
                profile.ProjectileSprite = cube.Frames[0]; profile.ProjectileMaterial = material;
                profile.PhysicalProjectiles = true;
            }
            else if (kind == EnemyKind.ClockBoss)
            {
                var beams = new EnemySpriteSequence[3];
                string[] actions = { "BeamThin", "BeamMedium", "BeamThick" };
                for (int i = 0; i < actions.Length; i++)
                {
                    var sequence = Array.Find(animations.Sequences, s => s.Action == actions[i]);
                    if (sequence == null) return;
                    if (sequence.Frames.Length != 7)
                        throw new InvalidOperationException(actions[i] + " 应有七帧。");
                    beams[i] = sequence;
                }
                profile.BeamAnimations = beams;
                profile.BeamSpriteMaterial = material;
            }
            EditorUtility.SetDirty(profile);
        }
        private static void RepairMinion(EnemyKind kind)
        {
            string path = "Assets/_Project/Prefabs/Enemies/" + kind + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var actor = root.GetComponent<EnemyActor>();
                var body = root.GetComponent<BoxCollider2D>();
                if (body == null) body = root.AddComponent<BoxCollider2D>();
                root.layer = LayerMask.NameToLayer("Enemy");
                body.enabled = true; body.isTrigger = false;
                body.size = actor.Definition.BodySize; body.offset = Vector2.zero;
                root.GetComponent<EnemyMotor>().BodyBlockMask = 1 << root.layer;
                actor.Definition.ContactDamage = true;
                actor.Definition.ContactPadding = Mathf.Max(.05f, actor.Definition.ContactPadding);
                EditorUtility.SetDirty(actor.Definition);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
