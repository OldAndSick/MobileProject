using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    // ────────────────────────────────────────────────
    //  Inspector
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

    [Tooltip("부스터 게이지 1단위당 발사 힘 배율")]
    public float launchForceBase = 5f;

    [Tooltip("boostGauge가 0일 때도 최소한 이 힘으로 발사 (테스트용)")]
    public float launchForceMin = 10f;

    [Header("=== Phase 3 : Flying ===")]
    [Tooltip("화면 홀드 시 초당 소모되는 부스터 게이지량")]
    public float boostDrainPerSecond = 10f;

    [Tooltip("화면 홀드 추가 추력 (Forward+Up 방향)")]
    public float thrustForce = 15f;

    [Tooltip("비행 중 공기 저항 (인스펙터 조절 → 체공 시간 튜닝)")]
    public float airDrag = 1.5f;

    [Tooltip("비행 중 중력 배율 (1 = 기본값, 0.5 = 절반)")]
    public float flightGravityScale = 0.6f;

    [Tooltip("비행 종료 시 원래 중력으로 복원할지 여부")]
    public bool restoreGravityOnLand = true;

    [Tooltip("발사 후 이 시간(초) 동안은 착지 판정을 무시 (즉시 GameOver 방지)")]
    public float landingGracePeriod = 0.5f;

    [Header("=== Obstacle (장애물) ===")]
    [Tooltip("장애물 충돌 시 감소할 부스터 게이지량")]
    public float obstacleDamage = 15f;

    [Tooltip("장애물 충돌 시 뒤로 튕겨나는 넉백 힘")]
    public float obstacleKnockback = 6f;

    [Tooltip("같은 장애물에 연속 충돌 방지 쿨타임 (초)")]
    public float obstacleCooldown = 0.5f;

    [Tooltip("장애물 충돌 시 재생할 파티클 (없으면 무시)")]
    public ParticleSystem hitParticle;

    [Header("=== VFX ===")]
    [Tooltip("부스터 파티클 시스템 (차량 후방에 부착)")]
    public ParticleSystem boosterParticle;

    // ────────────────────────────────────────────────
    //  Private
    // ────────────────────────────────────────────────
    private Rigidbody _rb;
    private Vector3 _originalGravity;
    private float _originalDrag;
    private float _startPosZ;
    private float _touchStartX;
    private bool _launched;
    private float _landingGraceTimer;
    private float _obstacleHitTimer;   // 장애물 쿨타임 타이머

    // ────────────────────────────────────────────────
    //  Unity 생명주기
    // ────────────────────────────────────────────────
    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _originalGravity = Physics.gravity;
        _originalDrag = _rb.linearDamping;
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
        // 쿨타임 카운트다운
        if (_obstacleHitTimer > 0f) _obstacleHitTimer -= Time.deltaTime;

        switch (GameManager.Instance.CurrentState)
        {
            case GameManager.GameState.Running:
                HandleRunningInput();
                MoveForward();
                break;

            case GameManager.GameState.Flying:
                if (_landingGraceTimer > 0f) _landingGraceTimer -= Time.deltaTime;
                HandleFlyingInput();
                CheckGroundFall();
                break;
        }
    }

    // ────────────────────────────────────────────────
    //  Phase 1 : Running
    // ────────────────────────────────────────────────
    private void MoveForward()
    {
        transform.Translate(Vector3.forward * baseSpeed * Time.deltaTime);
    }

    private void HandleRunningInput()
    {
        float inputX = 0f;

#if UNITY_EDITOR || UNITY_STANDALONE
        if (Input.GetMouseButtonDown(0)) _touchStartX = Input.mousePosition.x;
        if (Input.GetMouseButton(0))
        {
            float pixelDelta = Input.mousePosition.x - _touchStartX;
            inputX = (pixelDelta / Screen.width) * lateralSpeed * laneLimit * 2f;
            _touchStartX = Input.mousePosition.x;
        }
#else
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began) _touchStartX = touch.position.x;
            if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
            {
                float pixelDelta = touch.position.x - _touchStartX;
                inputX           = (pixelDelta / Screen.width) * lateralSpeed * laneLimit * 2f;
                _touchStartX     = touch.position.x;
            }
        }
#endif
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x + inputX, -laneLimit, laneLimit);
        transform.position = pos;
    }

    // ────────────────────────────────────────────────
    //  충돌 감지 (Trigger + Collision 둘 다)
    // ────────────────────────────────────────────────
    private void OnTriggerEnter(Collider other)
    {
        HandleContact(other.gameObject, other.tag);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleContact(collision.gameObject, collision.gameObject.tag);
    }

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
                // 청크 기반이므로 게이트는 풀 반환 없이 그냥 비활성화
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
                // Running / Flying 양쪽 모두 장애물 피격 처리
                if (state == GameManager.GameState.GameOver) return;
                HandleObstacleHit(other);
                break;
        }
    }

    // ────────────────────────────────────────────────
    //  장애물 피격
    // ────────────────────────────────────────────────

    /// <summary>
    /// 장애물 충돌 시 호출.
    /// Running : 부스터 게이지 감소 + 뒤로 넉백
    /// Flying  : 부스터 게이지 감소 + 반대 방향 튕김
    /// </summary>
    private void HandleObstacleHit(GameObject obstacle)
    {
        // 쿨타임 중이면 무시 (같은 장애물에 여러 프레임 연속 충돌 방지)
        if (_obstacleHitTimer > 0f) return;
        _obstacleHitTimer = obstacleCooldown;

        // 게이지 감소
        GameManager.Instance.boostGauge =
            Mathf.Max(0f, GameManager.Instance.boostGauge - obstacleDamage);
        UIManager.Instance?.RefreshBoostGaugeUI(GameManager.Instance.boostGauge);

        // 넉백 방향: 장애물 → 플레이어 방향의 수평 성분
        Vector3 knockDir = transform.position - obstacle.transform.position;
        knockDir.y = 0f;
        if (knockDir == Vector3.zero) knockDir = -Vector3.forward; // 폴백
        knockDir = knockDir.normalized;

        var state = GameManager.Instance.CurrentState;

        if (state == GameManager.GameState.Running)
        {
            // Running 중에는 Kinematic이라 직접 위치를 밀어줌
            transform.position += knockDir * obstacleKnockback * Time.fixedDeltaTime * 10f;
        }
        else if (state == GameManager.GameState.Flying)
        {
            // Flying 중에는 Rigidbody에 힘을 가함
            _rb.AddForce(knockDir * obstacleKnockback, ForceMode.Impulse);
        }

        // 히트 파티클 재생
        if (hitParticle != null)
        {
            hitParticle.transform.position = transform.position;
            hitParticle.Play();
        }

        Debug.Log($"[Player] 장애물 피격! 게이지: {GameManager.Instance.boostGauge:F1}  넉백방향: {knockDir}");
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

        // Running 구간에서 달리던 속도를 Rigidbody 초기값으로 심어줌
        // → 포물선 궤적 보장 (이 값이 없으면 수직으로 올랐다가 바로 낙하)
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
        Debug.Log($"[Player] Launch! force={forceMag:F1}  boostGauge={GameManager.Instance.boostGauge:F1}");

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
                Vector3 thrustDir = (Vector3.forward + Vector3.up).normalized;
                _rb.AddForce(thrustDir * thrustForce * Time.deltaTime, ForceMode.Force);
                if (boosterParticle != null && !boosterParticle.isPlaying)
                    boosterParticle.Play();
            }
            else { StopBoosterParticle(); }
        }
        else { StopBoosterParticle(); }
    }

    private void CheckGroundFall()
    {
        if (_landingGraceTimer > 0f) return;
        if (transform.position.y <= 0f) Land();
    }

    // ────────────────────────────────────────────────
    //  Land / GameOver
    // ────────────────────────────────────────────────
    private void Land()
    {
        if (GameManager.Instance.CurrentState == GameManager.GameState.GameOver) return;

        StopBoosterParticle();
        _rb.linearVelocity = Vector3.zero;
        _rb.isKinematic = true;
        _rb.linearDamping = _originalDrag;
        if (restoreGravityOnLand) Physics.gravity = _originalGravity;

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
            _rb.linearVelocity = Vector3.zero;
            _rb.isKinematic = true;
        }
    }

    private void StopBoosterParticle()
    {
        if (boosterParticle != null && boosterParticle.isPlaying)
            boosterParticle.Stop();
    }
}