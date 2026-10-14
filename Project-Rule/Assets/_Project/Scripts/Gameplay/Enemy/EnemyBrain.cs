using UnityEngine;
using UnityHFSM;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>
    /// Alive 内的动作 FSM + 可强制打断的 Transition/Dead，形成实际 HFSM。
    /// 所有判定计时由 EnemyActor 的游戏物理时钟推进，不使用包的墙钟 Timer。
    /// </summary>
    public abstract class EnemyBrain
    {
        protected readonly EnemyActor Actor;
        protected EnemyDefinition Config => Actor.Definition;
        protected readonly StateMachine Root = new StateMachine();
        protected readonly StateMachine Actions = new StateMachine();
        protected float Age, Elapsed, NextAttack, NextHurt, TransitionDuration;
        protected string TransitionName;
        protected int NextPhase, Pulses;
        public int Phase { get; protected set; } = 1;
        public float Delta { get; private set; }
        public float ActionElapsed => Elapsed;
        public float SimulationAge => Age;
        public string StateName => Root.ActiveStateName == "Alive" ? Actions.ActiveStateName : TransitionName;
        public virtual bool DamageLocked => false;
        protected abstract string[] ActionNames { get; }
        protected virtual string FirstAction => "Idle";
        protected EnemyBrain(EnemyActor actor) { Actor = actor; }

        public static EnemyBrain Create(EnemyActor actor)
        {
            switch (actor.Definition.Kind)
            {
                case EnemyKind.BrickBoss: return new BrickBossBrain(actor);
                case EnemyKind.ClockBoss: return new ClockBossBrain(actor);
                case EnemyKind.RabbitBoss: return new RabbitBossBrain(actor);
                case EnemyKind.BatBoss: return new BatBossBrain(actor);
                default: return new MinionBrain(actor);
            }
        }
        public void Initialize()
        {
            foreach (string action in ActionNames)
            {
                string name = action;
                Actions.AddState(name, new State(onEnter: _ => EnterAction(name), onLogic: _ => TickAction(name)));
            }
            Actions.SetStartState(FirstAction);
            Root.AddState("Alive", Actions);
            Root.AddState("Transition", new State(onEnter: _ =>
            {
                Elapsed = 0f; Pulses = 0;
                Actor.Cue(TransitionName, TransitionDuration);
            }, onLogic: _ =>
            {
                TickTransition();
                if (Root.ActiveStateName == "Transition" && Elapsed >= TransitionDuration)
                {
                    Phase = NextPhase;
                    string completed = TransitionName;
                    Root.RequestStateChange("Alive", true);
                    AfterTransition(completed);
                }
            }));
            Root.AddState("Dead", new State(onEnter: _ =>
            {
                Elapsed = 0f; TransitionName = "Dead";
                Actor.Cue("Dead", Actor.Duration("Dead", 1f));
                Actor.Motor.Stop();
            }, onLogic: _ => { if (Elapsed >= Actor.AnimationDuration) Actor.gameObject.SetActive(false); }));
            Root.SetStartState("Alive");
            Root.Init();
        }
        public void Tick(float dt)
        {
            Delta = dt; Age += dt; Elapsed += dt;
            bool end = Actor.Status.Health <= 0f || (Config.Kind == EnemyKind.RabbitBoss && Actor.Status.Health < 20f);
            if (end && Root.ActiveStateName != "Dead")
            {
                Actor.MarkDefeated();
                Root.RequestStateChange("Dead", true);
            }
            if (!Actor.Defeated) TickGlobal();
            Root.OnLogic();
        }
        protected virtual void EnterAction(string name)
        {
            Elapsed = 0f; Pulses = 0;
            Actor.FaceTarget();
            Actor.Cue(name, Actor.Duration(name), name == "Idle" || name == "Walk" || name == "Fly");
        }
        protected void Go(string action) => Actions.RequestStateChange(action, true);
        protected void Transition(string name, int phase, float duration = -1f)
        {
            TransitionName = name; NextPhase = phase;
            TransitionDuration = duration > 0f ? duration : Actor.Duration(name);
            Root.RequestStateChange("Transition", true);
        }
        protected void WalkToTarget()
        {
            if (Actor.Target == null) { Actor.Motor.Intent = Vector2.zero; return; }
            Actor.FaceTarget();
            Actor.Motor.Intent = Vector2.right * Actor.Facing;
        }
        protected bool Impact(float fraction = -1f)
        {
            if (Pulses != 0 || Elapsed < Actor.AnimationDuration * (fraction < 0f ? Config.ImpactFraction : fraction)) return false;
            Pulses = 1;
            return true;
        }
        protected bool Finished => Elapsed >= Actor.AnimationDuration;
        protected void RestAfterAttack()
        {
            NextAttack = Age + Config.AttackCooldown;
            Go("Idle");
        }
        protected virtual void TickGlobal() { }
        protected virtual void TickTransition() { }
        protected virtual void AfterTransition(string name) { }
        protected abstract void TickAction(string name);
        public virtual float ClampHealth(float health) => Mathf.Max(0f, health);
        public virtual void CommandAttack() { }
        public virtual void OnHit()
        {
            if (Actor.Status.Health > 0f && Root.ActiveStateName == "Alive" && Age >= NextHurt)
            {
                NextHurt = Age + Config.HurtCooldown;
                Go("Hurt");
            }
        }

        private sealed class Data
        {
            public string Root, Action, Transition;
            public float Age, Elapsed, Attack, Hurt, Duration;
            public int Phase, NextPhase, Pulses;
            public object Extra;
        }
        public object Capture() => new Data
        {
            Root = Root.ActiveStateName, Action = Actions.ActiveStateName, Transition = TransitionName,
            Age = Age, Elapsed = Elapsed, Attack = NextAttack, Hurt = NextHurt, Duration = TransitionDuration,
            Phase = Phase, NextPhase = NextPhase, Pulses = Pulses, Extra = CaptureExtra()
        };
        public void Restore(object state)
        {
            if (!(state is Data data)) return;
            TransitionName = data.Transition; TransitionDuration = data.Duration;
            Root.RequestStateChange(data.Root, true);
            if (data.Root == "Alive") Actions.RequestStateChange(data.Action, true);
            Age = data.Age; Elapsed = data.Elapsed; NextAttack = data.Attack; NextHurt = data.Hurt;
            Phase = data.Phase; NextPhase = data.NextPhase; Pulses = data.Pulses;
            RestoreExtra(data.Extra);
        }
        protected virtual object CaptureExtra() => null;
        protected virtual void RestoreExtra(object state) { }
    }

    public sealed class MinionBrain : EnemyBrain
    {
        private bool _commanded;
        private float _nextContact;
        private Vector2 _home;
        protected override string[] ActionNames => new[] { "Walk", "Idle", "Hurt" };
        protected override string FirstAction => "Walk";
        public MinionBrain(EnemyActor actor) : base(actor) { _home = actor.transform.position; }
        public override void CommandAttack() => _commanded = true;
        protected override object CaptureExtra() => new object[] { _commanded, _nextContact, _home };
        protected override void RestoreExtra(object state)
        {
            if (!(state is object[] data)) return;
            _commanded = (bool)data[0]; _nextContact = (float)data[1]; _home = (Vector2)data[2];
        }
        protected override void TickAction(string name)
        {
            if (name == "Hurt") { if (Finished) Go("Walk"); return; }
            if (Actor.Target == null || !Actor.Target.gameObject.activeInHierarchy) { Actor.Motor.Intent = Vector2.zero; return; }
            if (Config.Flying && !_commanded && Actor.Summoner != null)
            {
                Vector2 orbit = _home + new Vector2(Mathf.Sin(Age) * 0.8f, Mathf.Cos(Age) * 0.3f);
                Actor.Motor.Intent = Vector2.ClampMagnitude(orbit - (Vector2)Actor.transform.position, 1f);
                return;
            }
            if (Config.Flying) Actor.Motor.Intent = Actor.TowardTarget;
            else WalkToTarget();
            if (Age >= _nextContact && Actor.Distance < Config.BodySize.x * 0.5f + 0.55f)
            {
                Actor.Attack(Config.BodySize.x + 0.35f, true);
                _nextContact = Age + 0.6f;
            }
        }
    }
}
