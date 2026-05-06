using UnityEngine;
using TMPro;

/// <summary>
/// 게이트 오브젝트에 붙는 데이터 컴포넌트.
/// SetFormula() 호출 즉시 gateText(TMP)에 수식 문자열이 반영된다.
///
/// ── 인스펙터 연결 순서 ───────────────────────────────────────
///  1. 게이트 프리팹 안의 텍스트 오브젝트에 TMP_Text 컴포넌트를 붙인다.
///  2. 이 스크립트의 [Gate Text] 슬롯에 해당 TMP_Text를 드래그한다.
///  3. DynamicLevelManager가 스폰 직후 SetFormula()를 호출하면
///     텍스트가 자동으로 갱신된다.
/// </summary>
public class GateData : MonoBehaviour
{
    [Header("수식 데이터")]
    [Tooltip("연산자: '+', '-', '*', '/'")]
    public char Operator = '+';

    [Tooltip("피연산자 (예: 10, 2, 5 ...)")]
    public float Value = 10f;

    [Header("UI 연동")]
    [Tooltip("수식을 표시할 TextMeshPro 텍스트 오브젝트")]
    public TMP_Text gateText;

    // ────────────────────────────────────────────────
    //  Unity 생명주기
    // ────────────────────────────────────────────────

    private void OnEnable()
    {
        // 풀에서 꺼내 재활성화될 때 현재 값으로 UI 복원
        RefreshText();
    }

    // ────────────────────────────────────────────────
    //  Public API
    // ────────────────────────────────────────────────

    /// <summary>
    /// 수식 설정 + TMP 텍스트 즉시 갱신.
    /// DynamicLevelManager.SetupGate()에서 스폰 직후 호출된다.
    ///
    /// 표시 형식:
    ///   +  → "+10"
    ///   -  → "-5"
    ///   *  → "×2"   (곱셈은 'x' 대신 '×' 기호로 가독성 향상)
    ///   /  → "÷4"
    /// </summary>
    public void SetFormula(char op, float val)
    {
        Operator = op;
        Value = val;
        RefreshText();
    }

    // ────────────────────────────────────────────────
    //  Private
    // ────────────────────────────────────────────────
    private void RefreshText()
    {
        if (gateText == null) return;

        // 연산자 기호를 가독성 높은 문자로 매핑
        string symbol = Operator switch
        {
            '+' => "+",
            '-' => "-",
            '*' => "×",
            '/' => "÷",
            _ => Operator.ToString()
        };

        // 피연산자는 정수로 표시 (소수점 제거)
        gateText.text = $"{symbol}{(int)Value}";
    }
}