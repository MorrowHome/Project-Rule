using UnityEngine;

namespace RuleGame.Gameplay.Scene.DemonCastle
{
    public sealed class DemonPianoKey : DemonSurfaceTrigger
    {
        [InspectorName("目标曲调锁")]
        public DemonMelodyLock Melody;
        [Min(0)] [InspectorName("音符编号")] public int Note;
        [InspectorName("发声源")]
        public AudioSource Audio;
        [InspectorName("音符音频")]
        public AudioClip Clip;
        [InspectorName("按压外观")]
        public Transform KeyVisual;
        private float _pressed;
        protected override bool Activate()
        {
            if (Melody == null || Melody.Unlocked) return false;
            Melody.PlayNote(Note);
            if (Audio != null && Clip != null) Audio.PlayOneShot(Clip);
            _pressed = .16f; RefreshKey(); return true;
        }
        protected override void TickSurface(float dt)
        {
            if (Audio != null) Audio.UnPause();
            _pressed = Mathf.Max(0f, _pressed - dt); RefreshKey();
        }
        private void Update() { if (Paused && Audio != null) Audio.Pause(); }
        private void RefreshKey() { if (KeyVisual != null) KeyVisual.localPosition = new Vector3(0f, _pressed > 0f ? -.07f : 0f, 0f); }
        protected override object CaptureSurfaceState() => _pressed;
        protected override void RestoreSurfaceState(object state)
        { _pressed = (float)state; RefreshKey(); if (Audio != null) Audio.Stop(); }
    }
}
