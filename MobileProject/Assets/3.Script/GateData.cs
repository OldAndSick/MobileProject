using UnityEngine;

/// <summary>
/// 청크 프리팹 안의 게이트 오브젝트에 붙는 데이터 컴포넌트.
/// 연산자(Operator)와 피연산자(Value)를 보유하며,
/// Inspector에서 직접 설정하거나 SetFormula()로 런타임 변경 가능.
///
/// ── 청크 프리팹 세팅 방법 ────────────────────────────────────
///  1. 청크 프리팹 안 게이트 오브젝트에 이 컴포넌트를 붙인다.
///  2. Tag를 'MathGate'로 설정한다.
///  3. Collider의 Is Trigger를 true로 설정한다.
///  4. Inspector에서 Operator와 Value를 원하는 수식으로 고정하거나,
///     LevelManager 또는 별도 초기화 스크립트에서 SetFormula()를 호출해 랜덤 설정한다.
/// </summary>
public class GateData : MonoBehaviour
{
    [Tooltip("연산자: '+', '-', '*', '/'")]
    public char Operator = '+';

    [Tooltip("피연산자 (예: 10, 2, 5 ...)")]
    public float Value = 10f;

    [Header("UI 연동 (선택)")]
    [Tooltip("게이트 수식을 표시할 TextMeshPro 텍스트 (없으면 무시)")]
    public TMPro.TMP_Text gateLabel;

    private void OnEnable()
    {
        // 풀에서 꺼내 재활성화될 때 UI도 함께 갱신
        RefreshLabel();
    }

    /// <summary>수식 설정 + UI 갱신</summary>
    public void SetFormula(char op, float val)
    {
        Operator = op;
        Value = val;
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        if (gateLabel == null) return;
        gateLabel.text = Value >= 0
            ? $"{Operator}{Value:F0}"
            : $"{Operator}({Value:F0})";
    }
}