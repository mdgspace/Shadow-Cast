using System;
using UnityEngine;

namespace ShadowCast.Core.BuildMode
{
    /// <summary>
    /// Command representing the spawning of a buildable object into the scene.
    /// </summary>
    public class SpawnCommand : IBuildCommand
    {
        private readonly GameObject _spawnedInstance;
        private readonly Action<GameObject> _onRestore;
        private readonly Action<GameObject> _onDestroy;

        public string Description => $"Spawn {_spawnedInstance.name}";

        public SpawnCommand(GameObject instance, Action<GameObject> onRestore = null, Action<GameObject> onDestroy = null)
        {
            _spawnedInstance = instance;
            _onRestore = onRestore;
            _onDestroy = onDestroy;
        }

        public void Execute()
        {
            if (_spawnedInstance != null)
            {
                _spawnedInstance.SetActive(true);
                _onRestore?.Invoke(_spawnedInstance);
            }
        }

        public void Undo()
        {
            if (_spawnedInstance != null)
            {
                _spawnedInstance.SetActive(false);
                _onDestroy?.Invoke(_spawnedInstance);
            }
        }

        public void Redo()
        {
            Execute();
        }
    }

    /// <summary>
    /// Command representing the deletion / removal of an object from the scene.
    /// </summary>
    public class DeleteCommand : IBuildCommand
    {
        private readonly GameObject _targetObject;
        private readonly Action<GameObject> _onDeleted;
        private readonly Action<GameObject> _onRestored;

        public string Description => $"Delete {_targetObject.name}";

        public DeleteCommand(GameObject targetObject, Action<GameObject> onDeleted = null, Action<GameObject> onRestored = null)
        {
            _targetObject = targetObject;
            _onDeleted = onDeleted;
            _onRestored = onRestored;
        }

        public void Execute()
        {
            if (_targetObject != null)
            {
                _targetObject.SetActive(false);
                _onDeleted?.Invoke(_targetObject);
            }
        }

        public void Undo()
        {
            if (_targetObject != null)
            {
                _targetObject.SetActive(true);
                _onRestored?.Invoke(_targetObject);
            }
        }

        public void Redo()
        {
            Execute();
        }
    }

    /// <summary>
    /// Command representing transformation changes (Move, Rotate, Scale) on an object.
    /// </summary>
    public class TransformCommand : IBuildCommand
    {
        private readonly Transform _target;
        private readonly Vector3 _startPosition;
        private readonly Quaternion _startRotation;
        private readonly Vector3 _startScale;
        private readonly Vector3 _endPosition;
        private readonly Quaternion _endRotation;
        private readonly Vector3 _endScale;

        public string Description => $"Transform {_target.name}";

        public TransformCommand(
            Transform target,
            Vector3 startPos, Quaternion startRot, Vector3 startScale,
            Vector3 endPos, Quaternion endRot, Vector3 endScale)
        {
            _target = target;
            _startPosition = startPos;
            _startRotation = startRot;
            _startScale = startScale;
            _endPosition = endPos;
            _endRotation = endRot;
            _endScale = endScale;
        }

        public void Execute()
        {
            if (_target == null) return;
            _target.position = _endPosition;
            _target.rotation = _endRotation;
            _target.localScale = _endScale;
        }

        public void Undo()
        {
            if (_target == null) return;
            _target.position = _startPosition;
            _target.rotation = _startRotation;
            _target.localScale = _startScale;
        }

        public void Redo()
        {
            Execute();
        }
    }
}

