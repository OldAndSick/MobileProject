using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 청크 기반 스테이지 매니저.
///
/// ── 동작 흐름 ────────────────────────────────────────────────
///  1. Start()       : 초기 청크 몇 개를 미리 스폰 (플레이어 앞에 깔아둠)
///  2. Update()      : 플레이어 Z 위치 기준으로 앞이 비면 다음 청크 스폰
///  3. 청크 스폰 순서 : 일반청크(chunkPrefabs) × maxChunks → 점프대청크(jumpRampChunk) 1개 → 종료
///  4. 지나친 청크   : 플레이어 뒤로 recycleDistance 이상 벗어나면 풀로 반환(비활성화)
/// ─────────────────────────────────────────────────────────────
/// </summary>
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    // ────────────────────────────────────────────────
    //  Inspector
    // ────────────────────────────────────────────────

    [Header("청크 프리팹")]
    [Tooltip("일반 맵 청크 프리팹 배열 (바닥 + 게이트 + 장애물 포함)")]
    public GameObject[] chunkPrefabs;

    [Tooltip("점프대가 포함된 피니시 청크 프리팹 (마지막 1개)")]
    public GameObject jumpRampChunk;

    [Header("스테이지 설정")]
    [Tooltip("일반 청크 스폰 개수 (이후 점프대 청크 1개 스폰 후 종료)")]
    public int maxChunks = 5;

    [Tooltip("청크 하나의 Z축 길이 (모든 청크 프리팹이 동일한 길이여야 함)")]
    public float chunkLength = 30f;

    [Tooltip("플레이어 앞에 미리 깔아둘 청크 수 (시야 확보용)")]
    public int preloadCount = 3;

    [Tooltip("이 거리만큼 플레이어 뒤에 있는 청크는 풀로 반환")]
    public float recycleDistance = 40f;

    // ────────────────────────────────────────────────
    //  Private
    // ────────────────────────────────────────────────

    // 현재 활성화된 청크 목록 (앞→뒤 순서)
    private readonly List<GameObject> _activeChunks = new List<GameObject>();

    // 청크 종류별 풀 (프리팹 인덱스 기준)
    // -1 = jumpRampChunk 풀
    private readonly Dictionary<int, Queue<GameObject>> _pools = new Dictionary<int, Queue<GameObject>>();

    private Transform _playerTransform;

    private int _spawnedNormalCount = 0;   // 지금까지 스폰한 일반 청크 수
    private bool _jumpRampSpawned = false;
    private float _nextSpawnZ = 0f;  // 다음 청크가 놓일 Z 좌표

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
        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null)
            _playerTransform = player.transform;
        else
            Debug.LogError("[LevelManager] PlayerController를 찾을 수 없습니다.");

        // 시작 위치: 플레이어 Z 기준
        _nextSpawnZ = _playerTransform != null ? _playerTransform.position.z : 0f;

        // 초기 청크 미리 깔기
        for (int i = 0; i < preloadCount; i++)
            TrySpawnNextChunk();
    }

    private void Update()
    {
        if (_playerTransform == null) return;
        if (GameManager.Instance.CurrentState != GameManager.GameState.Running) return;

        // ── 앞쪽 청크 스폰 ──
        // 플레이어 앞에 preloadCount개가 항상 깔려 있도록 유지
        float playerZ = _playerTransform.position.z;
        float visibleZ = playerZ + chunkLength * preloadCount;

        while (_nextSpawnZ < visibleZ)
        {
            if (!TrySpawnNextChunk()) break;  // 더 이상 스폰할 청크 없으면 중단
        }

        // ── 뒤쪽 청크 재활용 ──
        RecycleOldChunks(playerZ);
    }

    // ────────────────────────────────────────────────
    //  스폰 로직
    // ────────────────────────────────────────────────

    /// <summary>
    /// 다음 청크를 스폰한다.
    /// 일반 청크 → 점프대 청크 순서이며, 전부 스폰 완료 시 false 반환.
    /// </summary>
    private bool TrySpawnNextChunk()
    {
        // 모든 청크 스폰 완료
        if (_jumpRampSpawned) return false;

        GameObject prefab;
        int poolKey;

        if (_spawnedNormalCount < maxChunks)
        {
            // 일반 청크: chunkPrefabs 배열에서 랜덤 선택
            if (chunkPrefabs == null || chunkPrefabs.Length == 0)
            {
                Debug.LogError("[LevelManager] chunkPrefabs 배열이 비어 있습니다.");
                return false;
            }
            poolKey = Random.Range(0, chunkPrefabs.Length);
            prefab = chunkPrefabs[poolKey];
            _spawnedNormalCount++;
        }
        else
        {
            // 마지막: 점프대 청크
            if (jumpRampChunk == null)
            {
                Debug.LogError("[LevelManager] jumpRampChunk가 할당되지 않았습니다.");
                return false;
            }
            poolKey = -1;
            prefab = jumpRampChunk;
            _jumpRampSpawned = true;
        }

        GameObject chunk = GetFromPool(poolKey, prefab);
        chunk.transform.position = new Vector3(0f, 0f, _nextSpawnZ);
        chunk.transform.rotation = Quaternion.identity;
        chunk.SetActive(true);

        _activeChunks.Add(chunk);
        _nextSpawnZ += chunkLength;

        Debug.Log($"[LevelManager] 청크 스폰 | poolKey={poolKey} | Z={chunk.transform.position.z} | 일반진행:{_spawnedNormalCount}/{maxChunks}");
        return true;
    }

    // ────────────────────────────────────────────────
    //  재활용(풀링) 로직
    // ────────────────────────────────────────────────

    /// <summary>플레이어 뒤로 recycleDistance 이상 벗어난 청크를 풀로 반환</summary>
    private void RecycleOldChunks(float playerZ)
    {
        for (int i = _activeChunks.Count - 1; i >= 0; i--)
        {
            GameObject chunk = _activeChunks[i];
            if (chunk == null) { _activeChunks.RemoveAt(i); continue; }

            // 청크 뒷끝이 플레이어보다 recycleDistance 이상 뒤에 있으면 반환
            float chunkEndZ = chunk.transform.position.z + chunkLength;
            if (chunkEndZ < playerZ - recycleDistance)
            {
                ReturnToPool(chunk);
                _activeChunks.RemoveAt(i);
            }
        }
    }

    // ────────────────────────────────────────────────
    //  풀 유틸
    // ────────────────────────────────────────────────

    private GameObject GetFromPool(int poolKey, GameObject prefab)
    {
        if (!_pools.ContainsKey(poolKey))
            _pools[poolKey] = new Queue<GameObject>();

        Queue<GameObject> pool = _pools[poolKey];

        if (pool.Count > 0)
            return pool.Dequeue();

        // 풀에 없으면 새로 생성 (이 매니저 하위로)
        return Instantiate(prefab, transform);
    }

    private void ReturnToPool(GameObject chunk)
    {
        chunk.SetActive(false);

        // 어느 풀에 넣을지 판별: jumpRampChunk면 -1, 아니면 프리팹 인덱스
        int poolKey = GetPoolKey(chunk);

        if (!_pools.ContainsKey(poolKey))
            _pools[poolKey] = new Queue<GameObject>();

        _pools[poolKey].Enqueue(chunk);
    }

    /// <summary>활성 청크의 원본 프리팹 인덱스를 역산 (풀 반환 시 사용)</summary>
    private int GetPoolKey(GameObject chunk)
    {
        // jumpRampChunk 인스턴스인지 이름으로 판별 (Instantiate 시 "(Clone)"이 붙음)
        if (jumpRampChunk != null &&
            chunk.name.StartsWith(jumpRampChunk.name))
            return -1;

        if (chunkPrefabs != null)
        {
            for (int i = 0; i < chunkPrefabs.Length; i++)
            {
                if (chunkPrefabs[i] != null &&
                    chunk.name.StartsWith(chunkPrefabs[i].name))
                    return i;
            }
        }

        // 알 수 없으면 공용 -99 풀에 넣음 (재사용 안 되지만 Destroy는 방지)
        return -99;
    }
}