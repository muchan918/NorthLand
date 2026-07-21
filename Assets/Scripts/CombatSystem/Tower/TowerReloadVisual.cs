using UnityEngine;

namespace NorthLand.Combat
{
    // 발사 순간 장전된 탄약 모형(예: 캐논 포신 위 사탕)을 숨겼다가, 짧은 지연 후 다시 보여준다.
    // 그 뒤로는 다음 발사 전까지 "장전된 채 대기" 상태로 계속 보인다 — 쿨다운 내내 숨겨두면 연속 공격 중엔
    // 거의 안 보이게 되므로, 재장전 완료(Tower 쿨다운)가 아니라 발사 직후 지연으로 재표시 시점을 잡는다.
    // Tower.OnFired를 구독하는 얇은 연출 컴포넌트 — 어떤 타워 종류든 같은 방식의 "탄약 유무" 연출에 재사용 가능.
    [RequireComponent(typeof(Tower))]
    public class TowerReloadVisual : MonoBehaviour
    {
        [SerializeField] GameObject ammoVisual;
        [SerializeField] float reappearDelay = 0.15f; // 발사 후 다음 탄이 장전돼 보이기까지의 지연

        Tower _tower;

        void Awake()
        {
            _tower = GetComponent<Tower>();
        }

        void OnEnable()
        {
            _tower.OnFired += HandleFired;
            if (ammoVisual != null) ammoVisual.SetActive(true); // 활성화 시점엔 장전된 상태로 시작
        }

        void OnDisable()
        {
            _tower.OnFired -= HandleFired;
            CancelInvoke(nameof(ShowAmmo));
        }

        void HandleFired()
        {
            CancelInvoke(nameof(ShowAmmo)); // 연속 발사 시 이전 재표시 예약을 취소하고 다시 숨김부터 시작
            if (ammoVisual != null) ammoVisual.SetActive(false);
            Invoke(nameof(ShowAmmo), reappearDelay);
        }

        void ShowAmmo()
        {
            if (ammoVisual != null) ammoVisual.SetActive(true);
        }
    }
}
