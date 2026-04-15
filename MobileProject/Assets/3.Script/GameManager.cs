using UnityEngine;

/// <summary>
/// 게임 전체 상태(State Machine)를 관리하는 싱글톤 매니저.
/// 모든 시스템은 GameManager.Instance.CurrentState를 참조해 동작을 결정한다.
/// </summary>
public class GameManager : MonoBehaviour
{
    // ────────────────────────────────────────────────
    //  싱글톤
    // ────────────────────────────────────────────────
    public static GameManager Instance { get; private set; }

    // ────────────────────────────────────────────────
    //  State Machine
    // ────────────────────────────────────────────────
    public enum GameState { Running, Jumping, Flying, GameOver }

    private GameState _currentState;
    public GameState CurrentState => _currentState;

    // 상태 변경 시 구독할 이벤트 (UI·파티클 등 외부 시스템용)
    public event System.Action<GameState> OnStateChanged;

    // ────────────────────────────────────────────────
    //  게임 데이터
    // ────────────────────────────────────────────────
    [Header("Boost Gauge")]
    [Tooltip("Phase 1에서 사칙연산 게이트로 누적되는 부스터 게이지")]
    public float boostGauge = 0f;

    [Tooltip("부스터 게이지가 힘(Force)으로 변환될 때의 배율")]
    public float boostToForceMultiplier = 1.0f;

    [Header("Reward")]
    public float finalGold = 0f;
    public float goldPerMeter = 1.0f;   // 1m 당 지급 골드

    // ────────────────────────────────────────────────
    //  Unity 생명주기
    // ────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        ChangeState(GameState.Running);
    }

    // ────────────────────────────────────────────────
    //  Public API
    // ────────────────────────────────────────────────

    /// <summary>상태 전환 – 동일 상태 중복 전환 방지 포함</summary>
    public void ChangeState(GameState newState)
    {
        if (_currentState == newState) return;

        Debug.Log($"[GameManager] State: {_currentState} → {newState}");
        _currentState = newState;
        OnStateChanged?.Invoke(newState);
    }

    /// <summary>
    /// 사칙연산 게이트 통과 시 호출.
    /// 연산자(+,-,*,/) 와 피연산자를 받아 부스터 게이지를 갱신한다.
    /// </summary>
    public void ApplyGateFormula(char op, float value)
    {
        switch (op)
        {
            case '+': boostGauge += value; break;
            case '-': boostGauge -= value; break;
            case '*': boostGauge *= value; break;
            case '/': boostGauge = (value != 0) ? boostGauge / value : boostGauge; break;
        }

        // 음수 방지 (게이지는 0 이상)
        boostGauge = Mathf.Max(0f, boostGauge);

        UIManager.Instance?.RefreshBoostGaugeUI(boostGauge);
        Debug.Log($"[GameManager] Gate({op}{value}) → BoostGauge: {boostGauge}");
    }

    /// <summary>
    /// 비행 구간에서 부스터를 delta만큼 소모.
    /// 게이지가 0이 되면 false 반환 → PlayerController가 추력 중단에 활용.
    /// </summary>
    public bool ConsumeBoost(float delta)
    {
        boostGauge -= delta;
        if (boostGauge <= 0f)
        {
            boostGauge = 0f;
            UIManager.Instance?.RefreshBoostGaugeUI(boostGauge);
            return false;   // 게이지 소진
        }
        UIManager.Instance?.RefreshBoostGaugeUI(boostGauge);
        return true;
    }

    /// <summary>착지 시 최종 보상을 계산하고 GameOver 상태로 전환</summary>
    public void TriggerGameOver(float travelDistanceZ)
    {
        finalGold = travelDistanceZ * goldPerMeter;
        Debug.Log($"[GameManager] GameOver | Distance: {travelDistanceZ:F1}m | Gold: {finalGold:F0}");
        ChangeState(GameState.GameOver);
        UIManager.Instance?.ShowResultScreen(travelDistanceZ, finalGold);
    }
}
