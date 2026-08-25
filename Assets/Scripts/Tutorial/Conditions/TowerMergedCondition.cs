using System;
using UnityEngine;

// 타워 합성이 한 번 끝나면 충족된다. 어떤 레시피였는지는 보지 않는다 —
// 아처 3개를 고르면 후보가 둘(아처×3 삼연사 · 아처×2 개틀링) 뜨는데, 어느 쪽을 골라도
// "합성이란 이런 것"은 똑같이 배우기 때문이다.
//
// 판정 시점은 재료 소모가 아니라 결과 타워 배치가 확정된 순간이다(TowerFusionController.Fused).
// 배치를 취소하면 합성 자체가 되감기므로, 소모 시점에 넘기면 화면에 아무것도 안 남은 채 다음 단계로 간다.
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public class TowerMergedCondition : TutorialCondition
{
    private TowerFusionController _fusion;

    public override void Begin(TutorialContext context)
    {
        _fusion = context.TowerFusion;

        if (_fusion == null)
        {
            Debug.LogWarning($"[{nameof(TowerMergedCondition)}] 씬에서 TowerFusionController를 찾지 못해 이 단계를 넘어갈 수 없다.");

            return;
        }

        _fusion.Fused += OnFused;
    }

    public override void End()
    {
        if (_fusion != null)
        {
            _fusion.Fused -= OnFused;
            _fusion = null;
        }
    }

    private void OnFused(TowerRecipe recipe)
    {
        Fire();
    }
}
