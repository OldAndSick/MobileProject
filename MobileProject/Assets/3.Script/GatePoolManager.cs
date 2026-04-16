using System.Collections.Generic;
using UnityEngine;

// ══════════════════════════════════════════════════════════════
//  GatePoolManager.cs
//  Queue 기반 오브젝트 풀링으로 게이트를 재사용한다.
//
//  ─ 동작 흐름 ─────────────────────────────────────────────
//  1. Start() : prefab을 poolSize만큼 미리 생성 → 풀에 대기
//  2. SpawnGate() : 풀에서 꺼내 위치·수식 세팅 후 활성화
//  3. ReturnGate() : 충돌 후 비활성화 → 풀에 반환
//  4. SpawnRoutine : 코루틴으로 spawnInterval마다 자동 스폰
// ══════════════════════════════════════════════════════════════
public class GatePoolManager : MonoBehaviour
{
    // ────────────────────────────────────────────────
    //  싱글톤
    // ────────────────────────────────────────────────
    public static GatePoolManager Instance { get; private set; }

    // ────────────────────────────────────────────────
    //  Inspector 변수
    // ────────────────────────────────────────────────

    [Header("Pool Settings")]
    [Tooltip("게이트 프리팹 (Collider IsTrigger = true, Tag = 'MathGate', GateData 컴포넌트 포함)")]
    public GameObject gatePrefab;

    [Tooltip("사전 생성할 풀 크기 (스폰 속도에 비해 여유 있게 설정)")]
    public int poolSize = 10;

    [Header("Spawn Settings")]
    [Tooltip("플레이어 앞에 게이트를 스폰할 Z 오프셋")]
    public float spawnOffsetZ = 40f;

    [Tooltip("게이트 스폰 간격 (초)")]
    public float spawnInterval = 2.5f;

    [Tooltip("게이트 X 위치 후보 (좌/우 두 개 레인)")]
    public float[] lanePositionsX = { -1.75f, 1.75f };

    [Header("Formula Settings")]
    [Tooltip("사용 가능한 연산자 풀")]
    public char[] operators = { '+', '-', '*', '/' };

    [Tooltip("피연산자 최솟값")]
    public float valueMin = 2f;

    [Tooltip("피연산자 최댓값")]
    public float valueMax = 20f;

    // ────────────────────────────────────────────────
    //  Private
    // ────────────────────────────────────────────────
    private Queue<GameObject> _pool = new Queue<GameObject>();
    private Transform _playerTransform;

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
        // 풀 초기화
        for (int i = 0; i < poolSize; i++)
        {
            GameObject go = Instantiate(gatePrefab, transform); // GatePoolManager 하위로
            go.SetActive(false);
            _pool.Enqueue(go);
        }

        // 플레이어 참조 (PlayerController가 붙은 오브젝트)
        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null) _playerTransform = player.transform;

        // 자동 스폰 코루틴 시작
        StartCoroutine(SpawnRoutine());
    }

    // ────────────────────────────────────────────────
    //  스폰 코루틴
    // ────────────────────────────────────────────────
    private System.Collections.IEnumerator SpawnRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(spawnInterval);

            // Running 상태일 때만 스폰
            if (GameManager.Instance.CurrentState == GameManager.GameState.Running)
                SpawnGate();
        }
    }

    // ────────────────────────────────────────────────
    //  풀링 API
    // ────────────────────────────────────────────────

    /// <summary>풀에서 게이트 하나를 꺼내 세팅 후 반환</summary>
    public GameObject SpawnGate()
    {
        if (_pool.Count == 0)
        {
            // 풀 소진 시 비상 확장 (경고 로그)
            Debug.LogWarning("[GatePool] Pool exhausted! Consider increasing poolSize.");
            GameObject extra = Instantiate(gatePrefab, transform);
            _pool.Enqueue(extra);
        }

        GameObject gate = _pool.Dequeue();

        // 위치 결정
        float spawnZ = (_playerTransform != null)
                     ? _playerTransform.position.z + spawnOffsetZ
                     : spawnOffsetZ;
        float spawnX = lanePositionsX[Random.Range(0, lanePositionsX.Length)];
        gate.transform.position = new Vector3(spawnX, 0f, spawnZ);

        // 수식 랜덤 설정
        GateData data = gate.GetComponent<GateData>();
        if (data != null)
        {
            char  op  = operators[Random.Range(0, operators.Length)];
            float val = Mathf.Round(Random.Range(valueMin, valueMax));

            // 나누기일 때 0 방지 + 정수 배수로 제한 (게임성)
            if (op == '/')
                val = Mathf.Max(2f, Mathf.Round(val / 2f) * 2f);

            data.SetFormula(op, val);
        }

        gate.SetActive(true);
        return gate;
    }

    /// <summary>충돌 후 게이트를 풀로 반환 (비활성화)</summary>
    public void ReturnGate(GameObject gate)
    {
        gate.SetActive(false);
        _pool.Enqueue(gate);
    }
}
