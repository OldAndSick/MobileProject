using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    // ────────────────────────────────────────────────
    //  Inspector
    // ────────────────────────────────────────────────

    [Header("=== Phase 1 : Running ===")]
    [Tooltip("Z축 자동 전진 속도")]
    public float baseSpeed = 10f;

    [Header("3차선 스냅 이동")]
    [Tooltip("좌 / 중 / 우 3개 차선 X 좌표 (DynamicLevelManager와 동일하게 맞출 것)")]
    public float[] lanes = { -2.5f, 0f, 2.5f };

    [Tooltip("차선 변경 시 Lerp 속도 (높을수록 빠르게 스냅)")]
    public float laneChangeSpeed = 12f;

    [Tooltip("모바일 스와이프 최소 인식 거리 (px) — 이보다 짧은 스와이프는 무시")]
    public float swipeMinPixels = 50f;

    [Header("=== Phase 2 : Jumping ===")]
    [Range(10f, 80f)]
    public float launchAngleDeg = 45f;
    public float launchForceBase = 5f;
    public float launchForceMin = 10f;

    [Header("=== Phase 3 : Flying ===")]
    public float boostDrainPerSecond = 10f;
    public float thrustForce = 15f;
    public float airDrag = 1.5f;
    public float flightGravityScale = 0.6f;
    public bool restoreGravityOnLand = true;
    public float landingGracePeriod = 0.5f;

    [Header("=== Obstacle ===")]
    [Tooltip("장애물 충돌 시 감소할 부스터 게이지량")]
    public float obstacleDamage = 10f;
    public ParticleSystem hitParticle;

    [Header("=== VFX ===")]
    public ParticleSystem boosterParticle;

    // ────────────────────────────────────────────────
    //  Private — 차선 이동
    // ────────────────────────────────────────────────
    private int _currentLaneIndex = 1;          // 시작: 가운데 차선
    private float _targetX;                        // 목표 X 좌표 (Lerp 목적지)

    // 스와이프 감지용
    private Vector2 _touchStartPos;
    private bool _swipeProcessed;                 // 한 터치당 한 번만 차선 변경

    // ────────────────────────────────────────────────
    //  Private — 물리 / 상태
    // ────────────────────────────────────────────────
    private Rigidbody _rb;
    private Vector3 _originalGravity;
    private float _originalDrag;
    private float _startPosZ;
    private bool _launched;
    private float _landingGraceTimer;

    // ────────────────────────────────────────────────
    //  Unity 생명주기
    // ────────────────────────────────────────────────
    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _originalGravity = Physics.gravity;
        _originalDrag = _rb.linearDamping;

        // 시작 위치: 가운데 차선
        _targetX = lanes[_currentLaneIndex];
        Vector3 pos = transform.position;
        pos.x = _targetX;
        transform.position = pos;
    }

    private void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged += HandleStateChanged;
        else
            Debug.LogError("[PlayerController] GameManager.Instance가 null입니다.");
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void Update()
    {
        if (_landingGraceTimer > 0f) _landingGraceTimer -= Time.deltaTime;

        switch (GameManager.Instance.CurrentState)
        {
            case GameManager.GameState.Running:
                HandleLaneInput();
                ApplyLaneSnap();
                MoveForward();
                break;

            case GameManager.GameState.Flying:
                HandleFlyingInput();
                CheckGroundFall();
                break;
        }
    }

    // ────────────────────────────────────────────────
    //  Phase 1 : 전진
    // ────────────────────────────────────────────────
    private void MoveForward()
    {
        transform.Translate(Vector3.forward * baseSpeed * Time.deltaTime);
    }

    // ────────────────────────────────────────────────
    //  Phase 1 : 차선 입력 (PC 방향키 + 모바일 스와이프)
    // ────────────────────────────────────────────────
    private void HandleLaneInput()
    {
        // ── PC: 방향키 ──
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) ChangeLane(-1);
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) ChangeLane(+1);

        // ── 모바일: 스와이프 ──
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Began)
            {
                _touchStartPos = touch.position;
                _swipeProcessed = false;
            }

            if (!_swipeProcessed && touch.phase == TouchPhase.Moved)
            {
                float deltaX = touch.position.x - _touchStartPos.x;

                // 최소 인식 거리 미만이면 무시 (오작동 방지)
                if (Mathf.Abs(deltaX) < swipeMinPixels) return;

                ChangeLane(deltaX > 0 ? +1 : -1);
                _swipeProcessed = true;   // 한 터치당 차선 변경 1회만
            }
        }
    }

    /// <summary>차선 인덱스를 ±1 변경하고 목표 X를 갱신</summary>
    private void ChangeLane(int direction)
    {
        _currentLaneIndex = Mathf.Clamp(_currentLaneIndex + direction, 0, lanes.Length - 1);
        _targetX = lanes[_currentLaneIndex];
    }

    /// <summary>매 프레임 현재 X를 목표 X로 Lerp — 쫀득한 스냅 연출</summary>
    private void ApplyLaneSnap()
    {
        Vector3 pos = transform.position;
        pos.x = Mathf.Lerp(pos.x, _targetX, laneChangeSpeed * Time.deltaTime);
        transform.position = pos;
    }

    // ────────────────────────────────────────────────
    //  충돌 감지 (Trigger + Collision 둘 다)
    // ────────────────────────────────────────────────
    private void OnTriggerEnter(Collider other) => HandleContact(other.gameObject, other.tag);
    private void OnCollisionEnter(Collision collision) => HandleContact(collision.gameObject, collision.gameObject.tag);

    private void HandleContact(GameObject other, string otherTag)
    {
        var state = GameManager.Instance.CurrentState;

        switch (otherTag)
        {
            case "MathGate":
                if (state != GameManager.GameState.Running) return;
                GateData gate = other.GetComponent<GateData>();
                if (gate != null)
                    GameManager.Instance.ApplyGateFormula(gate.Operator, gate.Value);
                other.SetActive(false);
                break;

            case "JumpRamp":
                if (state != GameManager.GameState.Running) return;
                Launch();
                break;

            case "Ground":
                if (state != GameManager.GameState.Flying) return;
                if (_landingGraceTimer <= 0f) Land();
                break;

            case "Obstacle":
                if (state == GameManager.GameState.GameOver) return;
                HandleObstacleHit(other);
                break;
        }
    }

    // ────────────────────────────────────────────────
    //  장애물 피격
    // ────────────────────────────────────────────────
    private void HandleObstacleHit(GameObject obstacle)
    {
        // ① 게이지 감소 (0 미만 방지)
        GameManager.Instance.boostGauge =
            Mathf.Max(0f, GameManager.Instance.boostGauge - obstacleDamage);

        // ② UI 즉시 갱신
        UIManager.Instance?.RefreshBoostGaugeUI(GameManager.Instance.boostGauge);

        // ③ 장애물 즉시 비활성화 → 연속 데미지(억까) 완전 차단
        obstacle.SetActive(false);

        // ④ 히트 파티클
        if (hitParticle != null)
        {
            hitParticle.transform.position = transform.position;
            hitParticle.Play();
        }

        Debug.Log($"[Player] 장애물 피격 | 게이지 잔량: {GameManager.Instance.boostGauge:F1}");
    }

    // ────────────────────────────────────────────────
    //  Launch
    // ────────────────────────────────────────────────
    private void Launch()
    {
        if (_launched) return;
        _launched = true;

        GameManager.Instance.ChangeState(GameManager.GameState.Jumping);

        _rb.isKinematic = false;
        // 달리던 속도를 물리 초기값으로 이어줌 → 포물선 보장
        _rb.linearVelocity = Vector3.forward * baseSpeed;

        float rad = launchAngleDeg * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad)).normalized;
        float forceMag = Mathf.Max(
            launchForceMin,
            GameManager.Instance.boostGauge
            * GameManager.Instance.boostToForceMultiplier
            * launchForceBase
        );

        _rb.AddForce(dir * forceMag, ForceMode.Impulse);
        Debug.Log($"[Player] Launch! force={forceMag:F1}  boost={GameManager.Instance.boostGauge:F1}");

        GameManager.Instance.ChangeState(GameManager.GameState.Flying);
        EnterFlyingMode();
    }

    // ────────────────────────────────────────────────
    //  Flying
    // ────────────────────────────────────────────────
    private void EnterFlyingMode()
    {
        _startPosZ = transform.position.z;
        _rb.linearDamping = airDrag;
        Physics.gravity = _originalGravity * flightGravityScale;
        _landingGraceTimer = landingGracePeriod;
    }

    private void HandleFlyingInput()
    {
        bool holding = Input.GetMouseButton(0) || (Input.touchCount > 0);

        if (holding)
        {
            bool hasBoost = GameManager.Instance.ConsumeBoost(boostDrainPerSecond * Time.deltaTime);
            if (hasBoost)
            {
                _rb.AddForce((Vector3.forward + Vector3.up).normalized * thrustForce * Time.deltaTime, ForceMode.Force);
                if (boosterParticle != null && !boosterParticle.isPlaying) boosterParticle.Play();
            }
            else StopBoosterParticle();
        }
        else StopBoosterParticle();
    }

    private void CheckGroundFall()
    {
        if (_landingGraceTimer > 0f) return;
        if (transform.position.y <= 0f) Land();
    }

    // ────────────────────────────────────────────────
    //  Land
    // ────────────────────────────────────────────────
    private void Land()
    {
        if (GameManager.Instance.CurrentState == GameManager.GameState.GameOver) return;

        StopBoosterParticle();
        _rb.linearVelocity = Vector3.zero;
        _rb.isKinematic = true;
        _rb.linearDamping = _originalDrag;
        if (restoreGravityOnLand) Physics.gravity = _originalGravity;

        GameManager.Instance.TriggerGameOver(
            Mathf.Max(0f, transform.position.z - _startPosZ));
    }

    private void HandleStateChanged(GameManager.GameState newState)
    {
        if (newState == GameManager.GameState.GameOver)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.isKinematic = true;
        }
    }

    private void StopBoosterParticle()
    {
        if (boosterParticle != null && boosterParticle.isPlaying) boosterParticle.Stop();
    }
}