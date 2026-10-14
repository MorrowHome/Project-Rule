using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.UI.RuleWheel
{
    /// <summary>右侧纸扇轮盘。扇区与命中计算共用角度参数，字块布局随 UXML 半径调整。</summary>
    [UxmlElement]
    public partial class RuleFan : VisualElement
    {
        [UxmlAttribute] public float OuterRadius { get; set; } = 184f;
        [UxmlAttribute] public float InnerRadius { get; set; } = 102f;
        [UxmlAttribute] public float StartAngle { get; set; } = -84f;
        [UxmlAttribute] public float EndAngle { get; set; } = 84f;
        [UxmlAttribute] public float WordWidth { get; set; } = 88f;
        [UxmlAttribute] public float WordHeight { get; set; } = 36f;
        private string[] _words = Array.Empty<string>();
        private bool[] _available = Array.Empty<bool>();
        private Label[] _labels = Array.Empty<Label>();
        public int SelectedIndex { get; private set; } = -1;
        public Vector2 Center => contentRect.size * 0.5f;

        public RuleFan()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => LayoutWords());
        }

        public void SetOptions(string[] words, bool[] available)
        {
            Clear();
            _words = words;
            _available = available;
            _labels = new Label[words.Length];
            SelectedIndex = -1;
            for (int i = 0; i < words.Length; i++)
            {
                var label = new Label(words[i]) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("wheel-word");
                label.EnableInClassList("long-word", words[i].Length > 4);
                label.EnableInClassList("unavailable", !available[i]);
                Add(label);
                _labels[i] = label;
            }
            LayoutWords();
            MarkDirtyRepaint();
        }

        public void SetAvailable(bool[] available)
        {
            _available = available;
            for (int i = 0; i < _labels.Length; i++) _labels[i].EnableInClassList("unavailable", !available[i]);
            MarkDirtyRepaint();
        }

        public void SetArc(float start, float end)
        {
            if (Mathf.Approximately(StartAngle, start) && Mathf.Approximately(EndAngle, end)) return;
            StartAngle = start; EndAngle = end;
            LayoutWords(); MarkDirtyRepaint();
        }

        public void Select(int index)
        {
            if (index < 0 || index >= _words.Length) index = -1;
            if (index == SelectedIndex) return;
            SelectedIndex = index;
            for (int i = 0; i < _labels.Length; i++) _labels[i].EnableInClassList("selected", i == index);
            MarkDirtyRepaint();
        }

        public int IndexAt(Vector2 localPoint)
        {
            Vector2 delta = localPoint - Center;
            if (delta.magnitude < InnerRadius || delta.magnitude > OuterRadius + 28f || _words.Length == 0) return -1;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            if (angle < StartAngle || angle > EndAngle) return -1;
            return Mathf.Min(_words.Length - 1, Mathf.FloorToInt((angle - StartAngle) / (EndAngle - StartAngle) * _words.Length));
        }

        public Vector2 OptionPoint(int index)
        {
            float angle = Mathf.Lerp(StartAngle, EndAngle, (index + 0.5f) / Mathf.Max(1, _words.Length));
            float radius = (InnerRadius + OuterRadius) * 0.5f;
            return Center + new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
        }

        private void LayoutWords()
        {
            for (int i = 0; i < _labels.Length; i++)
            {
                Vector2 point = OptionPoint(i);
                // 只把算法计算的位置写入运行时；字体、字块尺寸与配色仍由 USS 控制。
                _labels[i].style.left = point.x - WordWidth * .5f;
                _labels[i].style.top = point.y - WordHeight * .5f;
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            if (_words.Length == 0 || contentRect.height < 1f) return;
            var painter = context.painter2D;
            float slice = (EndAngle - StartAngle) / _words.Length;
            for (int i = 0; i < _words.Length; i++)
            {
                float start = StartAngle + slice * i + 1.2f;
                float end = StartAngle + slice * (i + 1) - 1.2f;
                painter.fillColor = i == SelectedIndex && _available[i] ? new Color(0.12f, 0.12f, 0.11f)
                    : _available[i] ? new Color(0.94f, 0.93f, 0.88f) : new Color(0.77f, 0.77f, 0.73f);
                painter.strokeColor = new Color(0.12f, 0.12f, 0.11f);
                painter.lineWidth = i == SelectedIndex ? 3f : 1.6f;
                painter.BeginPath();
                Vector2 inner = Center + new Vector2(Mathf.Cos(start * Mathf.Deg2Rad), Mathf.Sin(start * Mathf.Deg2Rad)) * InnerRadius;
                painter.MoveTo(inner);
                painter.Arc(Center, OuterRadius, Angle.Degrees(start), Angle.Degrees(end));
                painter.Arc(Center, InnerRadius, Angle.Degrees(end), Angle.Degrees(start), ArcDirection.CounterClockwise);
                painter.ClosePath();
                painter.Fill(); painter.Stroke();
            }
        }
    }
}
