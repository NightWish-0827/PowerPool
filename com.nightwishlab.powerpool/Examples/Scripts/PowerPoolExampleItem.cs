using PowerPool.API;
using UnityEngine;

namespace PowerPool.Examples
{
    /// <summary>
    /// Example pool item component.
    /// Attach this component to the prefab root (or child) and use it as T in the Spawn example.
    /// </summary>
    public sealed class PowerPoolExampleItem : MonoBehaviour, IPoolCleanable
    {
        [SerializeField] private Renderer _renderer;
        [SerializeField] private Rigidbody _rigidbody;

        private Color _baseColor = Color.white;

        private void Reset()
        {
            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>(true);
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();
        }

        public void Init(Color color, Vector3 impulse)
        {
            _baseColor = color;
            ApplyColor(_baseColor);

            if (_rigidbody != null)
            {
                _rigidbody.velocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.AddForce(impulse, ForceMode.Impulse);
            }
        }

        public void OnReturnToPool()
        {
            if (_rigidbody != null)
            {
                _rigidbody.velocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.Sleep();
            }

            ApplyColor(Color.white);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        private void ApplyColor(Color color)
        {
            if (_renderer == null) return;

            // To avoid creating material instances, use MaterialPropertyBlock.
            var block = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            _renderer.SetPropertyBlock(block);
        }
    }
}
