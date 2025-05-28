using System.Collections;
using System.Linq;  // LINQ 확장 메서드를 위해 추가
using UnityEngine;
using Fusion;
using TMPro;

namespace CuteDuckGame
{
    /// <summary>
    /// Fusion2 Shared Mode에서 게임의 핵심 네트워킹 로직을 관리하는 중앙 관리자
    /// - 52초 주기 타이머 관리 (Host Authority)
    /// - 오리 생성 신호 브로드캐스트  
    /// - 플레이어 수 추적
    /// - 채팅 시스템
    /// </summary>
    public class GameSessionManager : NetworkBehaviour
    {
        [Header("게임 설정")]
        [SerializeField] private float duckSpawnCycle = 52f; // 52초 주기
        [SerializeField] private float duckSpawnDuration = 3f; // 오리 생성 지속 시간
        
        [Header("UI 연결")]
        [SerializeField] private TextMeshProUGUI timerText;
        [SerializeField] private TextMeshProUGUI playerCountText;
        
        // ==============================================
        // Fusion2 NetworkProperty들 - 자동 동기화됨
        // ==============================================
        
        /// <summary>
        /// 서버 마스터 타이머 - Host가 관리하고 모든 클라이언트에 동기화
        /// 이것이 "동시성"의 핵심! 모든 사람이 같은 타이머를 본다
        /// </summary>
        [Networked] public float ServerTimer { get; set; }
        
        /// <summary>
        /// 현재 접속자 수 - 오리 생성량 결정에 사용
        /// Shared Mode에서는 자동으로 플레이어 수가 관리됨
        /// </summary>
        [Networked] public int ConnectedPlayers { get; set; }
        
        /// <summary>
        /// 오리 생성 플래그 - 모든 클라이언트가 동시에 오리 생성 시작
        /// true가 되면 모든 클라이언트에서 로컬 오리 생성 시작
        /// </summary>
        [Networked] public bool ShouldSpawnDucks { get; set; }
        
        /// <summary>
        /// 게임 세션이 시작되었는지 여부
        /// Host만 이 값을 변경할 수 있음
        /// </summary>
        [Networked] public bool IsGameActive { get; set; }

        // ==============================================
        // 로컬 변수들
        // ==============================================
        
        private bool lastSpawnState = false; // 오리 생성 상태 변화 감지용
        
        // ==============================================
        // Fusion2 생명주기 메서드들
        // ==============================================
        
        /// <summary>
        /// 네트워크 오브젝트가 생성될 때 호출
        /// Host와 Client 모두에서 실행됨
        /// </summary>
        public override void Spawned()
        {
            Debug.Log($"[GameSessionManager] Spawned - HasStateAuthority: {Object.HasStateAuthority}");
            
            // Host인 경우에만 타이머 초기화
            if (Object.HasStateAuthority)
            {
                Debug.Log("[GameSessionManager] Host로 시작 - 타이머 초기화");
                ServerTimer = duckSpawnCycle;
                IsGameActive = true;
                ConnectedPlayers = Runner.ActivePlayers.Count();
            }
            
            // 모든 클라이언트에서 UI 초기화
            InitializeUI();
        }
        
        /// <summary>
        /// 매 네트워크 틱마다 호출 (기본 60Hz)
        /// Host에서만 게임 로직 실행, 클라이언트는 동기화만 받음
        /// </summary>
        public override void FixedUpdateNetwork()
        {
            // Host만 타이머 관리
            if (Object.HasStateAuthority && IsGameActive)
            {
                UpdateServerTimer();
                UpdatePlayerCount();
            }
            
            // 모든 클라이언트에서 오리 생성 상태 체크
            CheckDuckSpawnState();
        }
        
        // ==============================================
        // 타이머 시스템 (Host Authority)
        // ==============================================
        
        /// <summary>
        /// 서버 타이머 업데이트 - Host에서만 실행
        /// 이유: 모든 클라이언트가 정확히 같은 타이밍을 봐야 하므로
        /// </summary>
        private void UpdateServerTimer()
        {
            ServerTimer -= Runner.DeltaTime;
            
            // 타이머가 0에 도달하면 오리 생성 신호 발송
            if (ServerTimer <= 0f)
            {
                Debug.Log("[GameSessionManager] 타이머 완료! 오리 생성 시작");
                TriggerDuckSpawn();
                ServerTimer = duckSpawnCycle; // 타이머 리셋
            }
        }
        
        /// <summary>
        /// 오리 생성 트리거 - Host에서만 호출
        /// ShouldSpawnDucks를 true로 설정하면 모든 클라이언트에서 감지됨
        /// </summary>
        private void TriggerDuckSpawn()
        {
            ShouldSpawnDucks = true;
            
            // 일정 시간 후 자동으로 생성 중단
            StartCoroutine(StopDuckSpawnAfterDelay());
        }
        
        /// <summary>
        /// 오리 생성 중단 - 너무 오래 생성되지 않도록 제한
        /// </summary>
        private IEnumerator StopDuckSpawnAfterDelay()
        {
            yield return new WaitForSeconds(duckSpawnDuration);
            ShouldSpawnDucks = false;
            Debug.Log("[GameSessionManager] 오리 생성 중단");
        }
        
        // ==============================================
        // 플레이어 수 관리
        // ==============================================
        
        /// <summary>
        /// 접속자 수 업데이트 - Host에서만 실행
        /// 오리 생성량 결정에 사용됨
        /// </summary>
        private void UpdatePlayerCount()
        {
            int currentPlayerCount = Runner.ActivePlayers.Count();
            if (ConnectedPlayers != currentPlayerCount)
            {
                ConnectedPlayers = currentPlayerCount;
                Debug.Log($"[GameSessionManager] 플레이어 수 변경: {ConnectedPlayers}명");
            }
        }
        
        // ==============================================
        // 오리 생성 상태 감지 (All Clients)
        // ==============================================
        
        /// <summary>
        /// 오리 생성 상태 변화 감지 - 모든 클라이언트에서 실행
        /// NetworkProperty 변화를 감지해서 로컬 오리 생성 시작
        /// 이것이 "동시성"의 핵심! 모든 클라이언트가 동시에 반응
        /// </summary>
        private void CheckDuckSpawnState()
        {
            if (ShouldSpawnDucks != lastSpawnState)
            {
                lastSpawnState = ShouldSpawnDucks;
                
                if (ShouldSpawnDucks)
                {
                    Debug.Log("[GameSessionManager] 오리 생성 신호 수신! 로컬에서 오리 생성 시작");
                    OnDuckSpawnTriggered();
                }
            }
        }
        
        /// <summary>
        /// 오리 생성 이벤트 - 모든 클라이언트에서 호출됨
        /// 여기서 실제 오리 생성 로직을 호출하거나 다른 컴포넌트에 신호 전달
        /// </summary>
        private void OnDuckSpawnTriggered()
        {
            // TODO: DuckSpawner에게 신호 전달
            // DuckSpawner.Instance?.SpawnDucks(ConnectedPlayers);
            
            // 임시로 채팅에 메시지 전송 (테스트용)
            SendChatMessageRpc($"🦆 오리 {GetDuckCountBasedOnPlayers()}마리가 나타났습니다! (플레이어 {ConnectedPlayers}명)");
        }
        
        /// <summary>
        /// 플레이어 수에 따른 오리 생성량 계산
        /// 많은 사람이 접속할수록 더 많은 오리 생성 (협력적 게임플레이)
        /// </summary>
        private int GetDuckCountBasedOnPlayers()
        {
            // 플레이어 2명당 오리 1마리, 최소 1마리, 최대 8마리
            return Mathf.Clamp(ConnectedPlayers / 2 + 1, 1, 8);
        }
        
        // ==============================================
        // 채팅 시스템 (RPC 기반)
        // ==============================================
        
        /// <summary>
        /// 채팅 메시지 전송 - 모든 클라이언트에게 브로드캐스트
        /// RPC를 사용하는 이유: 실시간성이 중요하고, 메시지는 일회성이므로
        /// NetworkProperty와 달리 상태 저장이 필요 없음
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void SendChatMessageRpc(string message, string playerName = "System")
        {
            Debug.Log($"[Chat] {playerName}: {message}");
            
            // TODO: UI 채팅 패널에 메시지 추가
            // ChatUI.Instance?.AddMessage(playerName, message);
        }
        
        // ==============================================
        // UI 관리
        // ==============================================
        
        /// <summary>
        /// UI 컴포넌트 초기화
        /// Inspector에서 연결되지 않았다면 자동으로 찾기 시도
        /// </summary>
        private void InitializeUI()
        {
            if (timerText == null)
                timerText = GameObject.Find("TimerText")?.GetComponent<TextMeshProUGUI>();
                
            if (playerCountText == null)
                playerCountText = GameObject.Find("PlayerCountText")?.GetComponent<TextMeshProUGUI>();
                
            Debug.Log($"[GameSessionManager] UI 초기화 완료 - Timer: {timerText != null}, PlayerCount: {playerCountText != null}");
        }
        
        /// <summary>
        /// 매 프레임 UI 업데이트
        /// NetworkProperty 변화를 UI에 반영
        /// 안전성 체크: Spawned된 후에만 실행
        /// </summary>
        void Update()
        {
            // NetworkBehaviour가 Spawn되었을 때만 UI 업데이트
            if (Object != null && Object.IsValid)
            {
                UpdateUI();
            }
        }
        
        /// <summary>
        /// UI 업데이트 - 모든 클라이언트에서 실행
        /// NetworkProperty 값을 UI에 표시
        /// 안전성 체크: 네트워크 오브젝트가 유효할 때만 실행
        /// </summary>
        private void UpdateUI()
        {
            // NetworkProperty에 접근하기 전 안전성 체크
            if (Object == null || !Object.IsValid)
            {
                // 아직 Spawn되지 않았다면 기본값 표시
                if (timerText != null)
                    timerText.text = "--";
                if (playerCountText != null)
                    playerCountText.text = "접속자: --명";
                return;
            }
            
            // 네트워크 오브젝트가 유효할 때만 NetworkProperty 접근
            if (timerText != null)
            {
                int seconds = Mathf.CeilToInt(ServerTimer);
                timerText.text = $"{seconds:00}";
            }
            
            if (playerCountText != null)
            {
                playerCountText.text = $"접속자: {ConnectedPlayers}명";
            }
        }
        
        // ==============================================
        // 디버그 및 테스트 메서드
        // ==============================================
        
        /// <summary>
        /// 디버그용 - 즉시 오리 생성 트리거
        /// </summary>
        [ContextMenu("Force Duck Spawn")]
        public void ForceDuckSpawn()
        {
            if (Object.HasStateAuthority)
            {
                TriggerDuckSpawn();
            }
            else
            {
                Debug.LogWarning("[GameSessionManager] Host만 강제 오리 생성 가능");
            }
        }
        
        /// <summary>
        /// 현재 네트워크 상태 로그 출력
        /// </summary>
        [ContextMenu("Log Network State")]
        public void LogNetworkState()
        {
            Debug.Log($"[GameSessionManager] 네트워크 상태:" +
                     $"\n- HasStateAuthority: {Object.HasStateAuthority}" +
                     $"\n- ServerTimer: {ServerTimer:F1}" +
                     $"\n- ConnectedPlayers: {ConnectedPlayers}" +
                     $"\n- ShouldSpawnDucks: {ShouldSpawnDucks}" +
                     $"\n- IsGameActive: {IsGameActive}");
        }
    }
}