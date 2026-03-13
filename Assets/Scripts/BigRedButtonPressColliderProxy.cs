using UnityEngine;

namespace TheBigRedButtonInstitute
{
    [DisallowMultipleComponent]
    public sealed class BigRedButtonPressColliderProxy : MonoBehaviour
    {
        [SerializeField] BigRedButtonPressInteractor owner;
        int _lastOverlapFrame = -1;

        public BigRedButtonPressInteractor Owner => owner;
        public bool WasHighlightedRecently => Time.frameCount - _lastOverlapFrame <= 1;

        public void Configure(BigRedButtonPressInteractor targetOwner)
        {
            owner = targetOwner;
        }

        public void MarkOverlap()
        {
            _lastOverlapFrame = Time.frameCount;
        }
    }
}
