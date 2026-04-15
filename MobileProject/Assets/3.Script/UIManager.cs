using UnityEngine;
using UnityEngine.UI;
using TMPro;   // TextMeshPro 사용 (없으면 Text로 대체)

/// <summary>
/// 화면 상단 부스터 게이지 UI + 결과 화면을 담당하는 UI 매니저.
///
/// ── 연결 필요 오브젝트 (Inspector) ────────────────────────
///  boostFillImage  : 게이지 바 Image (Image Type = Filled, Fill Method = Horizontal)
///  boostGaugeText  : 현재 게이지 수치 레이블 (TMP_Text)
///  resultPanel     : 결과 패널 (비활성화 상태로 시작)
///  distanceText    : 비행 거리 텍스트
///  goldText        : 획득 골드 텍스트
///  retryButton     : 재시작 버튼
/// ─────────────────────────────────────────────────────────
/// </summary>
public class UIManager : MonoBehaviour
{
    // ────────────────────────────────────────────────
    //  싱글톤
    // ────────────────────────────────────────────────
    public static UIManager Instance { get; private set; }

    // ────────────────────────────────────────────────
    //  Inspector 연결
    // ────────────────────────────────────────────────

    [Header("Boost Gauge UI")]
    [Tooltip("게이지 바 (Image Type: Filled)")]
    public Image boostFillImage;

    [Tooltip("게이지 수치 레이블")]
    public TMP_Text boostGaugeText;

    [Tooltip("게이지 바의 표시 최대값 (UI 비율 계산용 – 실제 게이지 상한과 무관)")]
    public float gaugeUIMaxDisplay = 100f;

    [Header("Result Screen")]
    public GameObject resultPanel;
    public TMP_Text distanceText;
    public TMP_Text goldText;
    public Button retryButton;

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
        // 결과 패널 숨기기
        if (resultPanel != null) resultPanel.SetActive(false);

        // 재시작 버튼 이벤트 등록
        if (retryButton != null)
            retryButton.onClick.AddListener(OnRetryClicked);

        RefreshBoostGaugeUI(0f);
    }

    // ────────────────────────────────────────────────
    //  Public API
    // ────────────────────────────────────────────────

    /// <summary>부스터 게이지 UI 갱신 (GameManager에서 호출)</summary>
    public void RefreshBoostGaugeUI(float currentGauge)
    {
        if (boostFillImage != null)
            boostFillImage.fillAmount = Mathf.Clamp01(currentGauge / gaugeUIMaxDisplay);

        if (boostGaugeText != null)
            boostGaugeText.text = $"{currentGauge:F0}";
    }

    /// <summary>결과 화면 표시 (GameManager.TriggerGameOver에서 호출)</summary>
    public void ShowResultScreen(float distance, float gold)
    {
        if (resultPanel != null) resultPanel.SetActive(true);
        if (distanceText != null) distanceText.text = $"{distance:F1} m";
        if (goldText != null)     goldText.text     = $"{gold:F0} G";
    }

    // ────────────────────────────────────────────────
    //  버튼 콜백
    // ────────────────────────────────────────────────
    private void OnRetryClicked()
    {
        // 씬 재시작 (SceneManager로 처리)
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }
}
