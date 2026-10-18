using RuleGame.Gameplay.Enemy;
using UnityEngine;

namespace RuleGame.Presentation.Audio
{
    [DisallowMultipleComponent, RequireComponent(typeof(EnemyActor))]
    public sealed class EnemySfxPresenter : MonoBehaviour
    {
        private EnemyActor _actor;
        private bool _deathPlayed;

        private void Awake() => _actor = GetComponent<EnemyActor>();
        private void OnEnable()
        {
            _actor.Feedback += Feedback;
            _actor.OnDefeated += Defeated;
            _actor.Restored += Restored;
            _actor.AnimationChanged += AnimationChanged;
            Restored();
        }
        private void OnDisable()
        {
            _actor.Feedback -= Feedback;
            _actor.OnDefeated -= Defeated;
            _actor.Restored -= Restored;
            _actor.AnimationChanged -= AnimationChanged;
        }

        private void Restored() => _deathPlayed = _actor.Defeated || (_actor.Status != null && _actor.Status.Health <= 0f);
        private void AnimationChanged()
        {
            // 可重组小怪恢复生命后，下一次倒地仍允许发声；恢复快照本身不发声。
            if (!_actor.Defeated && _actor.Status != null && _actor.Status.Health > 0f
                && _actor.AnimationName != "Dead") _deathPlayed = false;
        }
        private void Feedback(EnemyFeedback feedback)
        {
            if (_actor.Definition == null) return;
            switch (feedback.Kind)
            {
                case EnemyFeedbackKind.Strike:
                case EnemyFeedbackKind.AreaStrike: Play(SfxCue.EnemyMelee, feedback.Position); break;
                case EnemyFeedbackKind.ProjectileSpawn: Play(SfxCue.EnemyShot, feedback.Position); break;
                case EnemyFeedbackKind.BeamSpawn: Play(SfxCue.EnemyBeam, feedback.Position); break;
                case EnemyFeedbackKind.Hit:
                    if (feedback.Damage <= 0f) return;
                    if (feedback.Lethal) Death();
                    else if (!_deathPlayed) Play(MaterialCue(false), feedback.Position);
                    break;
            }
        }
        private void Defeated(EnemyActor actor) => Death();
        private void Death()
        {
            if (_deathPlayed || _actor.Definition == null) return;
            _deathPlayed = true;
            Play(MaterialCue(true), transform.position);
        }
        private SfxCue MaterialCue(bool death)
        {
            EnemyKind kind = _actor.Definition.Kind;
            if (kind == EnemyKind.ClockBoss || kind == EnemyKind.ClockMouse || kind == EnemyKind.ClockBomb || kind == EnemyKind.SpringToy)
                return death ? SfxCue.EnemyDeathMetal : SfxCue.EnemyHitMetal;
            if (kind == EnemyKind.BrickBoss || kind == EnemyKind.BrickMinion || kind == EnemyKind.StoneGuard
                || kind == EnemyKind.BrickSoldier || kind == EnemyKind.ExplosiveBlock)
                return death ? SfxCue.EnemyDeathWood : SfxCue.EnemyHitWood;
            return death ? SfxCue.EnemyDeathSoft : SfxCue.EnemyHitSoft;
        }
        private void Play(SfxCue cue, Vector3 position)
        {
            if (_actor.Definition == null || _actor.Status?.IsPaused == true) return;
            SfxPlayer.Active?.PlayAt(cue, position, this);
        }
    }
}
