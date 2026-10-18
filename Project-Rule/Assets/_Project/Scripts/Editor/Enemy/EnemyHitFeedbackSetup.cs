using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Enemy
{
    /// <summary>只生成受伤反馈材质；现有动画/白盒视图会自动接入，不重写场景和 Prefab。</summary>
    public static class EnemyHitFeedbackSetup
    {
        private const string Folder = "Assets/_Project/Resources/EnemyHitFeedback";

        [MenuItem("RuleGame/Enemies/生成受伤反馈材质")]
        public static void InstallMaterials()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            Shader shader = Shader.Find("RuleGame/Enemy/HitFeedback");
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("EnemyHitFeedback shader 未通过编译。");
            CreateMaterial("Flash", shader, 0f);
            CreateMaterial("Impact", shader, 1f);
            AssetDatabase.SaveAssets();
            Debug.Log("[EnemyHitFeedback] 受伤反馈材质已生成，所有敌人视图自动接入。");
        }

        private static void CreateMaterial(string name, Shader shader, float mode)
        {
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetFloat("_Mode", mode);
            EditorUtility.SetDirty(material);
        }
    }
}
