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

        private readonly VisualElement layer;
        private readonly IReadOnlyList<string> sourceLines;
        private readonly List<Label> labels = new();
        private IVisualElementScheduledItem tickSchedule;
        private float lastRegionHeight;
        private int cursor;
        private bool reducedMotion;
        private bool started;

        public BackgroundLogRingBuffer(VisualElement layer, IReadOnlyList<string> sourceLines)
        {
            this.layer = layer;
            this.sourceLines = sourceLines ?? Array.Empty<string>();
            this.layer.RegisterCallback<GeometryChangedEvent>(HandleGeometryChanged);
        }

        public void Start(bool useReducedMotion)
        {
            reducedMotion = useReducedMotion;
            started = true;
            cursor = 0;

            if (lastRegionHeight > 0f)
            {
                RebuildLabelsForHeight(lastRegionHeight);
                Render();
            }

            RestartTick();
        }

        public void Stop()
        {
            started = false;
            tickSchedule?.Pause();
            tickSchedule = null;
        }

        private void HandleGeometryChanged(GeometryChangedEvent evt)
        {
            if (Mathf.Approximately(evt.newRect.height, evt.oldRect.height))
            {
                return;
            }

            lastRegionHeight = evt.newRect.height;
            if (!started)
            {
                return;
            }

            RebuildLabelsForHeight(evt.newRect.height);
            Render();
        }

        private void RebuildLabelsForHeight(float regionHeight)
        {
            int targetCount = Mathf.Max(ExtraLines, Mathf.CeilToInt(regionHeight / LineHeight) + ExtraLines);
            if (labels.Count == targetCount)
            {
                return;
            }

            layer.Clear();
            labels.Clear();

            for (int i = 0; i < targetCount; i++)
            {
                Label label = new() { pickingMode = PickingMode.Ignore };
                label.AddToClassList("background-log-line");
                layer.Add(label);
                labels.Add(label);
            }
        }

        private void RestartTick()
        {
            tickSchedule?.Pause();
            tickSchedule = null;

            if (reducedMotion || sourceLines.Count == 0)
            {
                return;
            }

            tickSchedule = layer.schedule.Execute(Advance).Every(TickMilliseconds);
        }

        private void Advance()
        {
            if (sourceLines.Count == 0)
            {
                return;
            }

            cursor = (cursor + 1) % sourceLines.Count;
            Render();
        }

        private void Render()
        {
            if (sourceLines.Count == 0)
            {
                foreach (Label label in labels)
                {
                    label.text = string.Empty;
                }

                return;
            }

            for (int i = 0; i < labels.Count; i++)
            {
                labels[i].text = sourceLines[(cursor + i) % sourceLines.Count];
            }
        }
    }
}
