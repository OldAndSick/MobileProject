using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 시작 시 totalRunDistance만큼 바닥·게이트·장애물을 한 번에 동적으로 배치하는 레벨 매니저.
///
/// ── 스폰 순서 ────────────────────────────────────────────────
///  1. 바닥(groundPrefab)을 totalRunDistance까지 Z축으로 이어 붙임
///  2. 출발 후 safeZone(20m)은 아무것도 없음
///  3. 이후 spawnIntervalZ마다 한 '열(Row)' 스폰
///     ├─ 3개 차선을 셔플
///     ├─ 1번 차선 : gatePrefab 확정 스폰 + 수식 랜덤 세팅
///     ├─ 2번 차선 : 50% 확률로 obstaclePrefab 스폰
///     └─ 3번 차선 : 확정 빈 공간 (도피처)
///  4. totalRunDistance 끝에 jumpRampPrefab 스폰
/// </summary>
public class DynamicLevelManager : MonoBehaviour
{
    public static DynamicLevelManager Instance { get; private set; }

    // ────────────────────────────────────────────────
    //  Inspector
    // ────────────────────────────────────────────────

    [Header("거리 설정")]
    [Tooltip("플레이어가 달리는 총 거리 (m)")]
    public float totalRunDistance = 200f;

    [Tooltip("바닥 프리팹 하나의 Z축 길이")]
    public float groundLength = 30f;

    [Header("프리팹")]
    [Tooltip("바닥 프리팹")]
    public GameObject groundPrefab;

    [Tooltip("사칙연산 게이트 프리팹 (GateData 컴포넌트 포함)")]
    public GameObject gatePrefab;

    [Tooltip("장애물 프리팹")]
    public GameObject obstaclePrefab;

    [Tooltip("끝 지점 점프대 프리팹")]
    public GameObject jumpRampPrefab;

    [Header("차선 설정")]
    [Tooltip("좌 / 중 / 우 3개 차선의 X 좌표")]
    public float[] lanePositionsX = { -1.75f, 0f, 1.75f };

    [Header("스폰 규칙")]
    [Tooltip("출발 후 오브젝트를 스폰하지 않는 안전 구간 (m)")]
    public float safeZone = 20f;

    [Tooltip("열(Row) 스폰 간격 (m)")]
    public float spawnIntervalZ = 15f;

    [Tooltip("장애물이 2번 차선에 스폰될 확률 (0~1)")]
    [Range(0f, 1f)]
    public float obstacleChance = 0.5f;

    [Header("게이트 수식 설정")]
    [Tooltip("사용할 연산자 목록")]
    public char[] operators = { '+', '-', '*', '/' };

    [Tooltip("피연산자 최솟값")]
    public float valueMin = 2f;

    [Tooltip("피연산자 최댓값")]
    public float valueMax = 15f;

    // ────────────────────────────────────────────────
    //  Private
    // ────────────────────────────────────────────────

    // 스폰된 모든 오브젝트 추적 (씬 리셋 등 대비)
    private readonly List<GameObject> _spawnedObjects = new List<GameObject>();

    // 차선 인덱스 셔플용 배열 [0, 1, 2]
    private readonly int[] _laneOrder = { 0, 1, 2 };

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
        GenerateLevel();
    }

    // ────────────────────────────────────────────────
    //  레벨 생성 (게임 시작 시 한 번 호출)
    // ────────────────────────────────────────────────
    public void GenerateLevel()
    {
        // 이전 레벨 정리 (재시작 대비)
        ClearLevel();

        SpawnGround();
        SpawnObjectRows();
        SpawnJumpRamp();

        Debug.Log($"[DynamicLevelManager] 레벨 생성 완료 | 총거리: {totalRunDistance}m | 스폰 오브젝트: {_spawnedObjects.Count}개");
    }

    // ────────────────────────────────────────────────
    //  1단계: 바닥 깔기
    // ────────────────────────────────────────────────
    private void SpawnGround()
    {
        if (groundPrefab == null) { Debug.LogError("[DynamicLevelManager] groundPrefab이 없습니다."); return; }

        float z = 0f;
        while (z < totalRunDistance + groundLength) // 끝 지점을 살짝 넘어서까지 커버
        {
            Spawn(groundPrefab, new Vector3(0f, 0f, z));
            z += groundLength;
        }
    }

    // ────────────────────────────────────────────────
    //  2단계: 게이트 & 장애물 열(Row) 스폰
    // ────────────────────────────────────────────────
    private void SpawnObjectRows()
    {
        if (gatePrefab == null) { Debug.LogError("[DynamicLevelManager] gatePrefab이 없습니다."); return; }

        // safeZone 이후부터 spawnIntervalZ 간격으로 열을 배치
        // 단, 점프대 바로 앞 한 칸은 비워서 여유 확보
        float lastRowLimit = totalRunDistance - spawnIntervalZ;

        for (float z = safeZone; z < lastRowLimit; z += spawnIntervalZ)
        {
            SpawnRow(z);
        }
    }

    /// <summary>
    /// 한 열(Row) 스폰 핵심 로직.
    ///
    /// ── 억까 방지 룰 ───────────────────────────────
    ///  셔플된 차선 배열 기준:
    ///  [0] 무조건 게이트   → 항상 통과 가능한 차선 보장
    ///  [1] 50% 장애물      → 긴장감
    ///  [2] 무조건 빈 공간  → 확정 도피처
    /// </summary>
    private void SpawnRow(float z)
    {
        // 차선 순서 셔플 (Fisher-Yates)
        ShuffleLaneOrder();

        // [0] 게이트 확정 스폰
        int gateLane = _laneOrder[0];
        GameObject gateObj = Spawn(gatePrefab, new Vector3(lanePositionsX[gateLane], 0f, z));
        SetupGate(gateObj);

        // [1] 50% 확률로 장애물 스폰
        int obstacleLane = _laneOrder[1];
        if (obstaclePrefab != null && Random.value <= obstacleChance)
        {
            Spawn(obstaclePrefab, new Vector3(lanePositionsX[obstacleLane], 0f, z));
        }

        // [2] 3번 차선은 항상 빈 공간 → 코드 작업 없음
    }

    // ────────────────────────────────────────────────
    //  3단계: 점프대 스폰
    // ────────────────────────────────────────────────
    private void SpawnJumpRamp()
    {
        if (jumpRampPrefab == null) { Debug.LogError("[DynamicLevelManager] jumpRampPrefab이 없습니다."); return; }

        Spawn(jumpRampPrefab, new Vector3(0f, 0f, totalRunDistance));
        Debug.Log($"[DynamicLevelManager] 점프대 스폰 @ Z={totalRunDistance}");
    }

    // ────────────────────────────────────────────────
    //  게이트 수식 세팅
    // ────────────────────────────────────────────────

    /// <summary>
    /// 스폰된 게이트 오브젝트에 GateData를 찾아 랜덤 수식을 주입한다.
    ///
    /// ── 수식 생성 규칙 ──────────────────────────────
    ///  • 연산자 : operators 배열에서 랜덤 선택
    ///  • 피연산자 : valueMin~valueMax 범위 내 정수 랜덤
    ///  • 나누기('/') 보정 : 소수점 방지를 위해 피연산자를 짝수로 강제
    ///    (짝수는 대부분의 짝수 게이지를 나누어 떨어지게 하는 최소 안전장치)
    /// </summary>
    private void SetupGate(GameObject gateObj)
    {
        if (gateObj == null) return;

        GateData data = gateObj.GetComponent<GateData>();
        if (data == null)
        {
            Debug.LogWarning($"[DynamicLevelManager] {gateObj.name}에 GateData 컴포넌트가 없습니다.");
            return;
        }

        char op = operators[Random.Range(0, operators.Length)];
        float val = Mathf.Round(Random.Range(valueMin, valueMax));

        // 나누기 보정: 소수점 방지를 위해 짝수로 올림
        if (op == '/')
            val = Mathf.Max(2f, Mathf.Round(val / 2f) * 2f);

        data.SetFormula(op, val);
    }

    // ────────────────────────────────────────────────
    //  유틸
    // ────────────────────────────────────────────────

    /// <summary>오브젝트 스폰 후 추적 목록에 등록</summary>
    private GameObject Spawn(GameObject prefab, Vector3 position)
    {
        GameObject obj = Instantiate(prefab, position, Quaternion.identity, transform);
        _spawnedObjects.Add(obj);
        return obj;
    }

    /// <summary>Fisher-Yates 셔플로 _laneOrder[0,1,2]를 제자리 섞음</summary>
    private void ShuffleLaneOrder()
    {
        _laneOrder[0] = 0; _laneOrder[1] = 1; _laneOrder[2] = 2;

        for (int i = 2; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int temp = _laneOrder[i];
            _laneOrder[i] = _laneOrder[j];
            _laneOrder[j] = temp;
        }
    }

    /// <summary>스폰된 오브젝트 전체 제거 (씬 재시작 시 호출)</summary>
    public void ClearLevel()
    {
        foreach (var obj in _spawnedObjects)
            if (obj != null) Destroy(obj);
        _spawnedObjects.Clear();
    }
}