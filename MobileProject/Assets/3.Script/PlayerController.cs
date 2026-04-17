using UnityEngine;

/// <summary>
/// Phase 1(Running) → Phase 2(Jumping) → Phase 3(Flying) 전 구간을 담당하는 플레이어 컨트롤러.
/// Unity 6 대응 : velocity → linearVelocity / drag → linearDamping
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

    [Header("=== VFX ===")]
    [Tooltip("부스터 파티클 시스템 (차량 후방에 부착)")]
    public ParticleSystem boosterParticle;

    // ────────────────────────────────────────────────
    //  Private 변수
    // ────────────────────────────────────────────────
    private Rigidbody _rb;
    private Vector3 _originalGravity;
    private float _originalDrag;
    private float _startPosZ;
    private float _touchStartX;
    private bool _launched;   // Launch 중복 호출 방지

    // ────────────────────────────────────────────────
    //  Unity 생명주기
    // ────────────────────────────────────────────────
    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _originalGravity = Physics.gravity;
        _originalDrag = _rb.linearDamping;   // Unity 6
    }

    private void Start()
    {
        // ★ OnEnable에서 하면 GameManager.Instance가 아직 null일 수 있으므로 Start에서 구독
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged += HandleStateChanged;
        else
            Debug.LogError("[PlayerController] GameManager.Instance가 null입니다. 씬에 GameManager 오브젝트가 있는지 확인하세요.");
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
    //  Phase 2 : 충돌 감지
    //  ★ 큐브 점프대가 Is Trigger가 아닌 일반 Collider일 경우를 위해
    //    OnCollisionEnter 폴백을 추가했습니다.
    //    → 큐브 점프대에 Is Trigger 설정이 없어도 정상 작동합니다.
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

        if (otherTag == "MathGate" && state == GameManager.GameState.Running)
        {
            GateData gate = other.GetComponent<GateData>();
            if (gate != null)
                GameManager.Instance.ApplyGateFormula(gate.Operator, gate.Value);
            GatePoolManager.Instance?.ReturnGate(other);
            return;
        }

        if (otherTag == "JumpRamp" && state == GameManager.GameState.Running)
        {
            Launch();
            return;
        }

        if (otherTag == "Ground" && state == GameManager.GameState.Flying)
        {
            Land();
        }
    }

    // ────────────────────────────────────────────────
    //  Launch
    // ────────────────────────────────────────────────
    private void Launch()
    {
        if (_launched) return;   // 중복 호출 방지
        _launched = true;

        GameManager.Instance.ChangeState(GameManager.GameState.Jumping);

        _rb.isKinematic = false;
        _rb.linearVelocity = Vector3.zero;   // Unity 6

        float rad = launchAngleDeg * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad)).normalized;

        // boostGauge가 0이어도 launchForceMin 만큼은 발사 보장 (테스트 편의용)
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
        _rb.linearDamping = airDrag;                         // Unity 6
        Physics.gravity = _originalGravity * flightGravityScale;
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
        if (transform.position.y <= 0f) Land();
    }

    // ────────────────────────────────────────────────
    //  Land / GameOver
    // ────────────────────────────────────────────────
    private void Land()
    {
        if (GameManager.Instance.CurrentState == GameManager.GameState.GameOver) return;

        StopBoosterParticle();
        _rb.linearVelocity = Vector3.zero;   // Unity 6
        _rb.isKinematic = true;
        _rb.linearDamping = _originalDrag;  // Unity 6
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
            _rb.linearVelocity = Vector3.zero;   // Unity 6
            _rb.isKinematic = true;
        }
    }

    private void StopBoosterParticle()
    {
        if (boosterParticle != null && boosterParticle.isPlaying)
            boosterParticle.Stop();
    }
}