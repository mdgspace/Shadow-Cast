using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShadowCast.Core.BuildMode
{
    /// <summary>
    /// Manages an Undo/Redo history stack of up to 10 spatial build actions.
    /// Also supports detecting wrist tap gestures (double-tap for Undo, triple-tap for Redo).
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildUndoRedoManager : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Maximum number of actions stored in the undo history stack.")]
        [SerializeField] private int _maxHistoryCount = 10;

        [Header("Wrist Tap Gesture Settings")]
        [Tooltip("Max delay in seconds between consecutive taps to register as a multi-tap gesture.")]
        [SerializeField] private float _tapThresholdTime = 0.35f;

        private readonly LinkedList<IBuildCommand> _undoList = new LinkedList<IBuildCommand>();
        private readonly Stack<IBuildCommand> _redoStack = new Stack<IBuildCommand>();

        // Wrist tap tracking
        private int _tapCount = 0;
        private float _lastTapTime = -1f;

        // Events
        public event Action<bool, bool> OnHistoryStateChanged; // (canUndo, canRedo)
        public event Action<string> OnActionUndone;
        public event Action<string> OnActionRedone;

        public bool CanUndo => _undoList.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public int UndoCount => _undoList.Count;
        public int RedoCount => _redoStack.Count;

        /// <summary>
        /// Records a new command, clears the redo stack, and executes the command if not already executed.
        /// </summary>
        public void RecordCommand(IBuildCommand command, bool executeNow = false)
        {
            if (command == null) return;

            if (executeNow)
            {
                command.Execute();
            }

            _undoList.AddLast(command);

            // Enforce max history capacity
            while (_undoList.Count > Mathf.Max(1, _maxHistoryCount))
            {
                _undoList.RemoveFirst();
            }

            _redoStack.Clear();
            NotifyStateChanged();
        }

        /// <summary>
        /// Undoes the last recorded action.
        /// </summary>
        public bool Undo()
        {
            if (_undoList.Count == 0) return false;

            IBuildCommand cmd = _undoList.Last.Value;
            _undoList.RemoveLast();

            cmd.Undo();
            _redoStack.Push(cmd);

            OnActionUndone?.Invoke(cmd.Description);
            NotifyStateChanged();
            return true;
        }

        /// <summary>
        /// Redoes the most recently undone action.
        /// </summary>
        public bool Redo()
        {
            if (_redoStack.Count == 0) return false;

            IBuildCommand cmd = _redoStack.Pop();
            cmd.Redo();
            _undoList.AddLast(cmd);

            OnActionRedone?.Invoke(cmd.Description);
            NotifyStateChanged();
            return true;
        }

        /// <summary>
        /// Clears all undo and redo history.
        /// </summary>
        public void ClearHistory()
        {
            _undoList.Clear();
            _redoStack.Clear();
            NotifyStateChanged();
        }

        /// <summary>
        /// Call this whenever a wrist tap event is triggered (e.g. from controller or hand tracking).
        /// Handles double-tap (Undo) and triple-tap (Redo) logic.
        /// </summary>
        public void RegisterWristTap()
        {
            float currentTime = Time.time;
            if (currentTime - _lastTapTime <= _tapThresholdTime)
            {
                _tapCount++;
            }
            else
            {
                _tapCount = 1;
            }

            _lastTapTime = currentTime;
        }

        private void Update()
        {
            // Evaluate tap count window expiration
            if (_tapCount > 0 && Time.time - _lastTapTime > _tapThresholdTime)
            {
                if (_tapCount == 2)
                {
                    // Double-tap left wrist for Undo
                    Undo();
                }
                else if (_tapCount >= 3)
                {
                    // Triple-tap left wrist for Redo
                    Redo();
                }

                _tapCount = 0;
            }
        }

        private void NotifyStateChanged()
        {
            OnHistoryStateChanged?.Invoke(CanUndo, CanRedo);
        }
    }
}

