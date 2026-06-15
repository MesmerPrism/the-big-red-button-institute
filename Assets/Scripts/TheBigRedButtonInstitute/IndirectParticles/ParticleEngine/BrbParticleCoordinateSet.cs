using UnityEngine;

namespace TheBigRedButtonInstitute.IndirectParticles
{
    /// <summary>
    /// Unity-side coordinate cloud for BRB indirect particles.
    /// Positions and normals are local to the particle-system GameObject.
    /// </summary>
    [CreateAssetMenu(
        fileName = "BRB Particle Coordinate Set",
        menuName = "Big Red Button/Indirect Particles/Coordinate Set")]
    public sealed class BrbParticleCoordinateSet : ScriptableObject
    {
        [SerializeField] private Vector3[] positions;
        [SerializeField] private Vector3[] normals;
        [SerializeField] private Color[] colors;

        public int Count => positions != null ? positions.Length : 0;

        public bool TryGetPoint(int index, out Vector3 position, out Vector3 normal, out Color color)
        {
            position = Vector3.zero;
            normal = Vector3.forward;
            color = Color.white;

            if (positions == null || index < 0 || index >= positions.Length)
                return false;

            position = positions[index];
            if (normals != null && index < normals.Length && normals[index].sqrMagnitude > 0.000001f)
                normal = normals[index].normalized;

            if (colors != null && index < colors.Length)
                color = colors[index];

            return true;
        }
    }
}
