using UnityEngine;

/// <summary>
/// Phase 1(Running) → Phase 2(Jumping) → Phase 3(Flying) 전 구간을 담당하는 플레이어 컨트롤러.
///
/// ── 상태별 동작 요약 ──────────────────────────────────────────
///  Running  : Transform.Translate로 Z 전진 + 좌우 스와이프 입력
///  Jumping  : Rigidbody 활성화 + 45° 방향 AddForce(Impulse)
///  Flying   : 화면 홀드 → 부스터 소모 + 추가 AddForce
///  GameOver : 모든 입력/물리 중단
/// ────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    // ────────────────────────────────────────────────
    //  Inspector 노출 변수
    // ────────────────────────────────────────────────

    [Header("=== Phase 1 : Running ===")]
    [Tooltip("Z축 자동 전진 속도 (게이트 통과 시 절대 변하지 않음)")]
    public float baseSpeed = 10f;

    [Tooltip("좌우 이동 속도")]
    public float lateralSpeed = 8f;

    [Tooltip("도로 X축 이동 제한 범위 (±laneLimit)")]
    public float laneLimit = 3.5f;

    [Header("=== Phase 2 : Jumping ===")]
    [Tooltip("발사 방향 각도 (degrees, 0=정면 90=수직)")]
    [Range(10f, 80f)]
    public float launchAngleDeg = 45f;

    [Tooltip("부스터 게이지 1단위당 발사 힘 배율 (GameManager.boostToForceMultiplier와 곱해짐)")]
    public float launchForceBase = 5f;

    [Header("=== Phase 3 : Flying ===")]
    [Tooltip("화면 홀드 시 초당 소모되는 부스터 게이지량")]
    public float boostDrainPerSecond = 10f;

    [Tooltip("화면 홀드 추가 추력 (Forward+Up 방향)")]
    public float thrustForce = 15f;

    [Tooltip("비행 중 공기 저항 (인스펙터 조절 → 체공 시간 튜닝)")]
    public float airDrag = 1.5f;

    [Tooltip("비행 중 중력 배율 (1 = 기본 Physics.gravity, 0.5 = 절반)")]
    public float flightGravityScale = 0.6f;

    [Tooltip("비행 종료 시 원래 중력으로 복원할지 여부")]
    public bool restoreGravityOnLand = true;

    [Header("=== VFX ===")]
    [Tooltip("부스터 파티클 시스템 (차량 후방에 부착)")]
    public ParticleSystem boosterParticle;

    // ────────────────────────────────────────────────
    //  Private 변수
    // ────────────────────────────────────────────────
    private Rigidbody _rb;
    private Vector3 _originalGravity;
    private float _originalDrag;
    private float _startPosZ;           // Flying 진입 시점의 Z 좌표 (거리 계산용)

    // 터치/마우스 입력 캐시
    private float _touchStartX;
    private float _lastFrameX;
    private bool _isHolding;

    // ────────────────────────────────────────────────
    //  Unity 생명주기
    // ────────────────────────────────────────────────
    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        // ── 초기 Rigidbody 설정 ──
        // Phase 1에서는 물리 연산 꺼둠 (Transform.Translate 전진 방식)
        _rb.isKinematic = true;

        _originalGravity = Physics.gravity;
        _originalDrag    = _rb.linearDamping;
    }

    private void OnEnable()
    {
        GameManager.Instance.OnStateChanged += HandleStateChanged;
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    // ────────────────────────────────────────────────
    //  Update – 상태 머신 분기
    // ────────────────────────────────────────────────
    private void Update()
    {
        switch (GameManager.Instance.CurrentState)
        {
            case GameManager.GameState.Running:
                HandleRunningInput();
                MoveForward();
                break;

            case GameManager.GameState.Flying:
                HandleFlyingInput();
                CheckGroundFall();
                break;

            // Jumping / GameOver : Update에서 할 일 없음
        }
    }

    // ────────────────────────────────────────────────
    //  Phase 1 : Running
    // ────────────────────────────────────────────────

    /// <summary>Z축 자동 전진 (속도 고정 – 게이트 통과해도 변하지 않음)</summary>
    private void MoveForward()
    {
        transform.Translate(Vector3.forward * baseSpeed * Time.deltaTime);
    }

    /// <summary>좌우 스와이프 / 마우스 드래그 처리</summary>
    private void HandleRunningInput()
    {
        float inputX = 0f;

#if UNITY_EDITOR || UNITY_STANDALONE
        // 에디터 테스트용 : 마우스 드래그
        if (Input.GetMouseButtonDown(0)) _touchStartX = Input.mousePosition.x;
        if (Input.GetMouseButton(0))
        {
            float delta = (Input.mousePosition.x - _touchStartX) / Screen.width;
            inputX = delta * lateralSpeed * 10f;
            _touchStartX = Input.mousePosition.x; // 매 프레임 기준점 갱신 → 누적 방지
        }
#else
        // 모바일 터치
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
                _touchStartX = touch.position.x;

            if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
            {
                float delta = (touch.position.x - _touchStartX) / Screen.width;
                inputX = delta * lateralSpeed * 10f;
                _touchStartX = touch.position.x;
            }
        }
#endif

        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x + inputX * Time.deltaTime, -laneLimit, laneLimit);
        transform.position = pos;
    }

    // ────────────────────────────────────────────────
    //  Phase 2 : Jumping (점프대 트리거 → Launch)
    // ────────────────────────────────────────────────
    private void OnTriggerEnter(Collider other)
    {
        // ── 사칙연산 게이트 충돌 ──
        if (other.CompareTag("MathGate") && GameManager.Instance.CurrentState == GameManager.GameState.Running)
        {
            GateData gate = other.GetComponent<GateData>();
            if (gate != null)
                GameManager.Instance.ApplyGateFormula(gate.Operator, gate.Value);

            // 게이트 풀로 반환
            GatePoolManager.Instance?.ReturnGate(other.gameObject);
            return;
        }

        // ── 점프대 충돌 → 도약 ──
        if (other.CompareTag("JumpRamp") && GameManager.Instance.CurrentState == GameManager.GameState.Running)
        {
            Launch();
        }

        // ── 바닥(Multiplier 타일) 착지 ──
        if (other.CompareTag("Ground") && GameManager.Instance.CurrentState == GameManager.GameState.Flying)
        {
            Land();
        }
    }

    /// <summary>Rigidbody 활성화 후 45° 방향으로 Impulse 발사</summary>
    private void Launch()
    {
        GameManager.Instance.ChangeState(GameManager.GameState.Jumping);

        // ── Kinematic 해제 → 물리 모드 진입 ──
        _rb.isKinematic = false;
        _rb.linearVelocity     = Vector3.zero;

        // ── 발사 방향 계산 (launchAngleDeg 기반) ──
        float rad       = launchAngleDeg * Mathf.Deg2Rad;
        Vector3 dir     = new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad)).normalized;

        // ── 힘의 크기 = 부스터 게이지 × 배율 ──
        float forceMag  = GameManager.Instance.boostGauge
                        * GameManager.Instance.boostToForceMultiplier
                        * launchForceBase;

        _rb.AddForce(dir * forceMag, ForceMode.Impulse);

        Debug.Log($"[Player] Launch! force={forceMag:F1}, dir={dir}");

        // 한 프레임 뒤 Flying 상태로 전환 (Impulse가 적용된 직후)
        GameManager.Instance.ChangeState(GameManager.GameState.Flying);
        EnterFlyingMode();
    }

    // ────────────────────────────────────────────────
    //  Phase 3 : Flying
    // ────────────────────────────────────────────────

    /// <summary>Flying 진입 시 물리 파라미터 세팅</summary>
    private void EnterFlyingMode()
    {
        _startPosZ          = transform.position.z;

        // 공기 저항 증가 → 체공 시간 연장
        _rb.linearDamping            = airDrag;

        // 중력 약화 → 체공 시간 연장
        Physics.gravity     = _originalGravity * flightGravityScale;
    }

    /// <summary>화면 홀드 중 부스터 소모 + 추가 추력 적용</summary>
    private void HandleFlyingInput()
    {
        bool holding = Input.GetMouseButton(0) || (Input.touchCount > 0);

        if (holding)
        {
            // 부스터 소모 (소진 시 false 반환)
            bool hasBoost = GameManager.Instance.ConsumeBoost(boostDrainPerSecond * Time.deltaTime);
            if (hasBoost)
            {
                // 추가 추력 (전방 상단 방향)
                Vector3 thrustDir = (Vector3.forward + Vector3.up).normalized;
                _rb.AddForce(thrustDir * thrustForce * Time.deltaTime, ForceMode.Force);

                // 파티클 재생
                if (boosterParticle != null && !boosterParticle.isPlaying)
                    boosterParticle.Play();
            }
            else
            {
                StopBoosterParticle();
            }
        }
        else
        {
            StopBoosterParticle();
        }
    }

    /// <summary>Y=0 이하로 추락하면 즉시 착지 처리 (바닥 태그 없는 경우의 폴백)</summary>
    private void CheckGroundFall()
    {
        if (transform.position.y <= 0f)
            Land();
    }

    /// <summary>착지 처리 → 물리 정지 + GameOver</summary>
    private void Land()
    {
        if (GameManager.Instance.CurrentState == GameManager.GameState.GameOver) return;

        StopBoosterParticle();
        _rb.linearVelocity        = Vector3.zero;
        _rb.isKinematic     = true;

        // 물리 파라미터 복원
        _rb.linearDamping            = _originalDrag;
        if (restoreGravityOnLand)
            Physics.gravity = _originalGravity;

        float distance = transform.position.z - _startPosZ;
        GameManager.Instance.TriggerGameOver(Mathf.Max(0f, distance));
    }

    // ────────────────────────────────────────────────
    //  상태 변경 콜백
    // ────────────────────────────────────────────────
    private void HandleStateChanged(GameManager.GameState newState)
    {
        if (newState == GameManager.GameState.GameOver)
        {
            // 혹시 아직 물리가 켜져 있다면 정지
            _rb.linearVelocity    = Vector3.zero;
            _rb.isKinematic = true;
        }
    }

    // ────────────────────────────────────────────────
    //  유틸
    // ────────────────────────────────────────────────
    private void StopBoosterParticle()
    {
        if (boosterParticle != null && boosterParticle.isPlaying)
            boosterParticle.Stop();
    }
}
