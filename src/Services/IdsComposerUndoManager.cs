using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using CJKPlayground.Models;

namespace CJKPlayground.Services
{
    public class IdsComposerSnapshot : IDisposable
    {
        public string ActionName { get; set; }
        public string IdsFormula { get; set; }
        public IdsNode Ast { get; set; }
        public Dictionary<string, NodeCustomTransform> TransformsMap { get; set; }
        public List<GraphicsPath> CustomStrokes { get; set; }
        public Dictionary<string, GraphicsPath> ComponentVectorPaths { get; set; }
        public Dictionary<string, System.Drawing.RectangleF> ComponentBaseRects { get; set; }
        public Dictionary<string, NodeCustomTransform> ComponentBaseTransforms { get; set; }
        public string SelectedNodeId { get; set; }
        public DateTime Timestamp { get; set; }

        public IdsComposerSnapshot()
        {
            Timestamp = DateTime.UtcNow;
            CustomStrokes = new List<GraphicsPath>();
            ComponentVectorPaths = new Dictionary<string, GraphicsPath>();
            ComponentBaseRects = new Dictionary<string, System.Drawing.RectangleF>();
            ComponentBaseTransforms = new Dictionary<string, NodeCustomTransform>();
            TransformsMap = new Dictionary<string, NodeCustomTransform>();
        }

        public static IdsComposerSnapshot Create(
            string actionName,
            string formula,
            IdsNode ast,
            Dictionary<string, NodeCustomTransform> transforms,
            List<GraphicsPath> strokes,
            string selectedId,
            Dictionary<string, GraphicsPath> componentVectorPaths = null,
            Dictionary<string, System.Drawing.RectangleF> componentBaseRects = null,
            Dictionary<string, NodeCustomTransform> componentBaseTransforms = null)
        {
            var snap = new IdsComposerSnapshot
            {
                ActionName = actionName ?? "Thao tác",
                IdsFormula = formula ?? "",
                SelectedNodeId = selectedId,
                Timestamp = DateTime.UtcNow
            };

            // Deep clone AST
            if (ast != null)
            {
                snap.Ast = ast.Clone();
            }

            // Deep clone Transforms
            if (transforms != null)
            {
                foreach (var kvp in transforms)
                {
                    snap.TransformsMap[kvp.Key] = kvp.Value != null ? kvp.Value.Clone() : new NodeCustomTransform();
                }
            }

            // Deep clone Custom Strokes
            if (strokes != null)
            {
                foreach (var p in strokes)
                {
                    if (p != null)
                    {
                        snap.CustomStrokes.Add((GraphicsPath)p.Clone());
                    }
                }
            }

            // Deep clone Component Vector Paths
            if (componentVectorPaths != null)
            {
                foreach (var kvp in componentVectorPaths)
                {
                    if (kvp.Value != null)
                    {
                        snap.ComponentVectorPaths[kvp.Key] = (GraphicsPath)kvp.Value.Clone();
                    }
                }
            }

            // Deep clone Component Base Rects & Transforms
            if (componentBaseRects != null)
            {
                foreach (var kvp in componentBaseRects)
                {
                    snap.ComponentBaseRects[kvp.Key] = kvp.Value;
                }
            }
            if (componentBaseTransforms != null)
            {
                foreach (var kvp in componentBaseTransforms)
                {
                    snap.ComponentBaseTransforms[kvp.Key] = kvp.Value != null ? kvp.Value.Clone() : new NodeCustomTransform();
                }
            }

            return snap;
        }

        public void Dispose()
        {
            if (CustomStrokes != null)
            {
                foreach (var p in CustomStrokes)
                {
                    try { p.Dispose(); } catch { }
                }
                CustomStrokes.Clear();
            }
            if (ComponentVectorPaths != null)
            {
                foreach (var kvp in ComponentVectorPaths)
                {
                    try { if (kvp.Value != null) kvp.Value.Dispose(); } catch { }
                }
                ComponentVectorPaths.Clear();
            }
        }
    }

    public class UndoRedoManager
    {
        private readonly Stack<IdsComposerSnapshot> _undoStack = new Stack<IdsComposerSnapshot>();
        private readonly Stack<IdsComposerSnapshot> _redoStack = new Stack<IdsComposerSnapshot>();
        private readonly int _maxCapacity;

        public event EventHandler StateChanged;

        public bool CanUndo { get { return _undoStack.Count > 0; } }
        public bool CanRedo { get { return _redoStack.Count > 0; } }

        public int UndoCount { get { return _undoStack.Count; } }
        public int RedoCount { get { return _redoStack.Count; } }

        public string CurrentUndoAction
        {
            get { return _undoStack.Count > 0 ? _undoStack.Peek().ActionName : null; }
        }

        public string CurrentRedoAction
        {
            get { return _redoStack.Count > 0 ? _redoStack.Peek().ActionName : null; }
        }

        public UndoRedoManager(int maxCapacity = 50)
        {
            _maxCapacity = Math.Max(10, maxCapacity);
        }

        public void Push(IdsComposerSnapshot snapshot)
        {
            if (snapshot == null) return;

            // Clear redo stack on new action
            while (_redoStack.Count > 0)
            {
                var s = _redoStack.Pop();
                s.Dispose();
            }

            _undoStack.Push(snapshot);

            // Limit history capacity
            if (_undoStack.Count > _maxCapacity)
            {
                var list = new List<IdsComposerSnapshot>(_undoStack);
                var dropped = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                dropped.Dispose();
                _undoStack.Clear();
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    _undoStack.Push(list[i]);
                }
            }

            OnStateChanged();
        }

        public IdsComposerSnapshot Undo(IdsComposerSnapshot currentState)
        {
            if (_undoStack.Count == 0) return null;

            if (currentState != null)
            {
                _redoStack.Push(currentState);
            }

            var previous = _undoStack.Pop();
            OnStateChanged();
            return previous;
        }

        public IdsComposerSnapshot Redo(IdsComposerSnapshot currentState)
        {
            if (_redoStack.Count == 0) return null;

            if (currentState != null)
            {
                _undoStack.Push(currentState);
            }

            var next = _redoStack.Pop();
            OnStateChanged();
            return next;
        }

        public void Clear()
        {
            while (_undoStack.Count > 0)
            {
                _undoStack.Pop().Dispose();
            }
            while (_redoStack.Count > 0)
            {
                _redoStack.Pop().Dispose();
            }
            OnStateChanged();
        }

        private void OnStateChanged()
        {
            if (StateChanged != null)
            {
                try { StateChanged(this, EventArgs.Empty); } catch { }
            }
        }
    }
}
