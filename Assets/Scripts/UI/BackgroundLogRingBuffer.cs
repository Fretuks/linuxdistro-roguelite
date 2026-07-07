using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KernelPanic.UI
{
    /// <summary>
    /// Keeps the ambient boot-log background filled by rewriting a fixed stack of labels.
    /// </summary>
    public sealed class BackgroundLogRingBuffer
    {
        private const float LineHeight = 18f;
        private const int ExtraLines = 2;
        private const int TickMilliseconds = 450;

        private readonly VisualElement _layer;
        private readonly IReadOnlyList<string> _sourceLines;
        private readonly List<Label> _labels = new();
        private IVisualElementScheduledItem _tickSchedule;
        private float _lastRegionHeight;
        private int _cursor;
        private bool _reducedMotion;
        private bool _started;

        public BackgroundLogRingBuffer(VisualElement layer, IReadOnlyList<string> sourceLines)
        {
            this._layer = layer;
            this._sourceLines = sourceLines ?? Array.Empty<string>();
            this._layer.RegisterCallback<GeometryChangedEvent>(HandleGeometryChanged);
        }

        public void Start(bool useReducedMotion)
        {
            _reducedMotion = useReducedMotion;
            _started = true;
            _cursor = 0;

            if (_lastRegionHeight > 0f)
            {
                RebuildLabelsForHeight(_lastRegionHeight);
                Render();
            }

            RestartTick();
        }

        public void Stop()
        {
            _started = false;
            _tickSchedule?.Pause();
            _tickSchedule = null;
        }

        private void HandleGeometryChanged(GeometryChangedEvent evt)
        {
            if (Mathf.Approximately(evt.newRect.height, evt.oldRect.height))
            {
                return;
            }

            _lastRegionHeight = evt.newRect.height;
            if (!_started)
            {
                return;
            }

            RebuildLabelsForHeight(evt.newRect.height);
            Render();
        }

        private void RebuildLabelsForHeight(float regionHeight)
        {
            int targetCount = Mathf.Max(ExtraLines, Mathf.CeilToInt(regionHeight / LineHeight) + ExtraLines);
            if (_labels.Count == targetCount)
            {
                return;
            }

            _layer.Clear();
            _labels.Clear();

            for (int i = 0; i < targetCount; i++)
            {
                Label label = new() { pickingMode = PickingMode.Ignore };
                label.AddToClassList("background-log-line");
                _layer.Add(label);
                _labels.Add(label);
            }
        }

        private void RestartTick()
        {
            _tickSchedule?.Pause();
            _tickSchedule = null;

            if (_reducedMotion || _sourceLines.Count == 0)
            {
                return;
            }

            _tickSchedule = _layer.schedule.Execute(Advance).Every(TickMilliseconds);
        }

        private void Advance()
        {
            if (_sourceLines.Count == 0)
            {
                return;
            }

            _cursor = (_cursor + 1) % _sourceLines.Count;
            Render();
        }

        private void Render()
        {
            if (_sourceLines.Count == 0)
            {
                foreach (Label label in _labels)
                {
                    label.text = string.Empty;
                }

                return;
            }

            for (int i = 0; i < _labels.Count; i++)
            {
                _labels[i].text = _sourceLines[(_cursor + i) % _sourceLines.Count];
            }
        }
    }
}
