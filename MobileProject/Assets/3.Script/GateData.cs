using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ══════════════════════════════════════════════════════════════
//  GateData.cs
//  게이트 프리팹에 붙는 데이터 컴포넌트.
//  연산자(op)와 피연산자(value)를 담는다.
// ══════════════════════════════════════════════════════════════
public class GateData : MonoBehaviour
{
    [Tooltip("연산자: '+', '-', '*', '/'")]
    public char Operator = '+';

    [Tooltip("피연산자 (예: 10, 2, 5 ...)")]
    public float Value = 10f;

    /// <summary>인스펙터에서 직접 수식 문자열로도 설정 가능하도록 보조 메서드</summary>
    public void SetFormula(char op, float val)
    {
        Operator = op;
        Value = val;
        // TODO: 게이트 UI 텍스트 갱신 (TextMeshPro 등)
        // gateLabel.text = $"{op}{val}";
    }
}
