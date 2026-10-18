using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.UI.RuleWheel
{
    /// <summary>朝角色前方展开的纸扇轮盘。绘制、标签与命中共用角度及镜像参数。</summary>
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
        private bool[] _active = Array.Empty<bool>();
        private Label[] _labels = Array.Empty<Label>();
        public int SelectedIndex { get; private set; } = -1;
        public Vector2 Center => contentRect.size * 0.5f;
        public bool Mirrored { get; private set; }

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
            _active = new bool[words.Length];
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

        /// <summary>标记当前生效的规则格位；扇区填充与标签配色一起表现高亮。</summary>
        /// <remarks>可用性每 0.1 秒刷新一次，所以先比一遍，没变化就不重绘。</remarks>
        public void SetActiveMarks(bool[] active)
        {
            if (SameMarks(active)) return;
            // 必须复制：调用方复用同一块数组，存引用会让「上一次」跟着一起变，高亮永远不刷新。
            _active = active == null ? Array.Empty<bool>() : (bool[])active.Clone();
            for (int i = 0; i < _labels.Length; i++)
                _labels[i].EnableInClassList("active", i < _active.Length && _active[i]);
            MarkDirtyRepaint();
        }

        private bool SameMarks(bool[] active)
        {
            int count = active?.Length ?? 0;
            if (count != _active.Length) return false;
            for (int i = 0; i < count; i++) if (active[i] != _active[i]) return false;
            return true;
        }

        public void SetArc(float start, float end)
        {
            if (Mathf.Approximately(StartAngle, start) && Mathf.Approximately(EndAngle, end)) return;
            StartAngle = start; EndAngle = end;
            LayoutWords(); MarkDirtyRepaint();
        }

        public void SetMirrored(bool mirrored)
        {
            if (Mirrored == mirrored) return;
            Mirrored = mirrored;
            LayoutWords(); MarkDirtyRepaint();
        }

        private Vector2 Direction(float angle)
        {
            float x = Mathf.Cos(angle * Mathf.Deg2Rad);
            return new Vector2(Mirrored ? -x : x, Mathf.Sin(angle * Mathf.Deg2Rad));
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
            // 先把左侧输入还原到同一套逻辑角度，鼠标和手柄保持相同的词条顺序。
            if (Mirrored) delta.x = -delta.x;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            if (angle < StartAngle || angle > EndAngle) return -1;
            return Mathf.Min(_words.Length - 1, Mathf.FloorToInt((angle - StartAngle) / (EndAngle - StartAngle) * _words.Length));
        }

        public Vector2 OptionPoint(int index)
        {
            float angle = Mathf.Lerp(StartAngle, EndAngle, (index + 0.5f) / Mathf.Max(1, _words.Length));
            float radius = (InnerRadius + OuterRadius) * 0.5f;
            return Center + Direction(angle) * radius;
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
                bool highlighted = i < _active.Length && _active[i];
                bool selected = i == SelectedIndex && _available[i];
                // 生效中的格位用暖金色填充；被选中时改用金边包住深色块，
                // 让「这是当前生效的规则」和「我现在指着它」能同时读出来。
                painter.fillColor = selected ? new Color(0.12f, 0.12f, 0.11f)
                    : highlighted ? new Color(0.95f, 0.79f, 0.40f)
                    : _available[i] ? new Color(0.94f, 0.93f, 0.88f) : new Color(0.77f, 0.77f, 0.73f);
                painter.strokeColor = highlighted && selected
                    ? new Color(0.95f, 0.79f, 0.40f) : new Color(0.12f, 0.12f, 0.11f);
                painter.lineWidth = selected || highlighted ? 3f : 1.6f;
                painter.BeginPath();
                // 只镜像几何和标签位置，不翻转 VisualElement，避免中文也被反向显示。
                Vector2 inner = Center + Direction(start) * InnerRadius;
                float visualStart = Mirrored ? 180f - start : start;
                float visualEnd = Mirrored ? 180f - end : end;
                painter.MoveTo(inner);
                painter.Arc(Center, OuterRadius, Angle.Degrees(visualStart), Angle.Degrees(visualEnd),
                    Mirrored ? ArcDirection.CounterClockwise : ArcDirection.Clockwise);
                painter.Arc(Center, InnerRadius, Angle.Degrees(visualEnd), Angle.Degrees(visualStart),
                    Mirrored ? ArcDirection.Clockwise : ArcDirection.CounterClockwise);
                painter.ClosePath();
                painter.Fill(); painter.Stroke();
            }
        }
    }
}
