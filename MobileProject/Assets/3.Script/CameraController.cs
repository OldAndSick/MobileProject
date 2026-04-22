using UnityEngine;

/// <summary>
/// 플레이어를 따라가는 카메라 컨트롤러.
/// Running : Y축 완전 고정, X각도만 Lerp (eulerAngles 360도 랩핑 버그 수정)
/// Flying  : 플레이어 방향으로 Y축 자유 회전
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Running 오프셋 (Phase 1)")]
    public Vector3 runOffset = new Vector3(0f, 4f, -8f);
    public float runLookDownAngle = 15f;

    [Header("Flying 오프셋 (Phase 3)")]
    public Vector3 flyOffset = new Vector3(0f, 8f, -18f);
    public float flyLookDownAngle = 20f;

    [Header("Smoothing")]
    [Tooltip("위치 추적 부드러움 (낮을수록 찰싹)")]
    public float positionSmoothTime = 0.15f;
    [Tooltip("Running↔Flying 오프셋 전환 속도")]
    public float offsetTransitionSpeed = 3f;
    [Tooltip("X축(상하) 회전 속도")]
    public float rotationSmoothTime = 0.1f;

    // ────────────────────────────────────────────────
    private Vector3 _currentOffset;
    private Vector3 _posVelocity;
    private bool _isFrozen;

    // ★ eulerAngles 대신 Quaternion을 직접 보간 → 360도 랩핑 버그 없음
    private Quaternion _currentRotation;

    private void Awake()
    {
        _currentOffset = runOffset;
        _currentRotation = Quaternion.Euler(runLookDownAngle, 0f, 0f);
    }

    private void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged += HandleStateChanged;
        else
            Debug.LogError("[CameraController] GameManager.Instance가 null입니다.");

        if (target != null)
        {
            transform.position = target.position + runOffset;
            transform.rotation = _currentRotation;
        }
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void LateUpdate()
    {
        if (target == null || _isFrozen) return;

        bool isFlying = GameManager.Instance.CurrentState == GameManager.GameState.Flying;

        // ── 오프셋 보간 ──
        Vector3 targetOffset = isFlying ? flyOffset : runOffset;
        _currentOffset = Vector3.Lerp(_currentOffset, targetOffset, offsetTransitionSpeed * Time.deltaTime);

        // ── 위치 추적 ──
        Vector3 desiredPos = target.position + _currentOffset;
        transform.position = Vector3.SmoothDamp(
            transform.position, desiredPos, ref _posVelocity, positionSmoothTime);

        // ── 회전 ──
        // ★ eulerAngles.x를 읽지 않고 Quaternion끼리 Slerp로 보간
        //    → 0도↔360도 경계에서 튀는 현상 완전 차단
        Quaternion targetRotation;
        if (isFlying)
        {
            // Flying : 플레이어 방향을 실제로 바라봄
            Vector3 lookDir = (target.position - transform.position).normalized;
            if (lookDir != Vector3.zero)
                targetRotation = Quaternion.LookRotation(lookDir);
            else
                targetRotation = _currentRotation;
        }
        else
        {
            // Running : Y축 0 고정, X각도만 목표값으로
            targetRotation = Quaternion.Euler(runLookDownAngle, 0f, 0f);
        }

        _currentRotation = Quaternion.Slerp(_currentRotation, targetRotation, rotationSmoothTime / Time.deltaTime * Time.deltaTime);
        transform.rotation = _currentRotation;
    }

    private void HandleStateChanged(GameManager.GameState newState)
    {
        if (newState == GameManager.GameState.GameOver)
            _isFrozen = true;
    }
}