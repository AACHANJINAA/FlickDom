# Server TODO

`SERVER_PLAN.md`를 실제 구현 작업으로 옮기기 위한 상세 체크리스트다.

## Priority Order

1. 로컬 게임 로직과 네트워크 권한 경계를 분리한다.
2. NGO 기반 접속과 서버 권한 흐름을 추가한다.
3. 플릭, 배치, 점수 계산을 서버 확정 방식으로 바꾼다.
4. 세션 시작과 종료 흐름을 정리한다.

## Phase 1. Code Refactor Before Networking

### 1. `GameModeManager` 책임 정리

대상 파일:
- `Assets/02_Scripts/Core/GameModeManager.cs`

할 일:
- `MonoBehaviour` 의존 없이도 호출 가능한 규칙 메서드 목록을 정리한다.
- 턴 전환 로직과 라운드 전환 로직을 순수 상태 전이 계층으로 분리한다.
- `PendingPlacementCandidates` 관리 책임을 별도 서비스로 뺄지 결정한다.
- `RoundStarted`, `StateChanged`, `ActivePlayerChanged` 이벤트를 네트워크 동기화 모델과 연결 가능한 형태로 정리한다.

완료 기준:
- `GameModeManager`가 입력 처리나 UI 처리 없이도 경기 흐름을 제어할 수 있다.
- 서버에서 같은 클래스를 그대로 사용해도 동작 의미가 깨지지 않는다.

### 2. `TurnBasedFlickPiece` 책임 분리

대상 파일:
- `Assets/02_Scripts/Flick_Scripts/TurnBasedFlickPiece.cs`

현재 문제:
- 입력
- 물리 발사
- 정지 판정
- 무효 판정
- 시각 표시
- 턴 인터랙션 제어

위 책임이 한 클래스에 섞여 있다.

할 일:
- 입력 처리 전용 계층으로 분리한다.
- 서버 물리 시뮬레이션 책임을 분리한다.
- 상태 표시와 하이라이트를 프리젠테이션 계층으로 분리한다.
- `pieceId`, `owner`, `dead/alive`, `launchedThisTurn` 같은 네트워크 상태 필드를 정리한다.

권장 분리 예시:
- `FlickPieceInputController`
- `FlickPieceSimulation`
- `FlickPiecePresentation`

완료 기준:
- 서버 빌드에서는 입력/시각 처리 없이도 말 시뮬레이션이 가능하다.
- 클라이언트 빌드에서는 서버 상태를 받아 시각 처리만 수행할 수 있다.

### 3. 레거시 로컬 입력 코드 정리

대상 파일:
- `Assets/02_Scripts/Flick_Scripts/FlickMain.cs`

할 일:
- 현재 씬에서 실제 사용 여부를 확인한다.
- 멀티플레이 구조와 충돌하는 경우 제거하거나 테스트 전용으로 격리한다.
- `TurnBasedFlickPiece`와 중복되는 책임을 제거한다.

완료 기준:
- 온라인 경기 흐름에서 로컬 전용 입력 코드가 실수로 실행되지 않는다.

## Phase 2. Network Foundation With NGO

### 4. 패키지 도입

할 일:
- `com.unity.netcode.gameobjects` 추가
- `com.unity.transport` 추가
- 필요 시 dedicated server build 관련 설정 점검

완료 기준:
- 프로젝트가 NGO 기반 런타임을 참조할 수 있다.

### 5. 접속 부트스트랩 구성

대상:
- 새 `NetworkBootstrap` 또는 유사 매니저 추가

할 일:
- 서버 시작 경로 정의
- 클라이언트 접속 경로 정의
- 씬 로딩과 매치 시작 시점 정리
- 로컬 테스트용 `host/client/server` 실행 플로우 정리

완료 기준:
- 서버와 클라이언트가 기본 연결에 성공한다.

### 6. 플레이어 슬롯 모델 정의

할 일:
- `Player1`, `Player2` 슬롯 점유 구조 정의
- 클라이언트 연결 시 슬롯 배정 규칙 구현
- 2명 초과 접속 차단 규칙 구현
- 한 명만 접속한 대기 상태 구현

완료 기준:
- 서버가 어떤 연결이 어떤 플레이어 슬롯인지 확정할 수 있다.

## Phase 3. Authoritative Match State

### 7. 네트워크 상태 모델 설계

정의할 것:
- `GameState`
- `RoundNumber`
- `ActivePlayerId`
- `CurrentTurnIndex`
- `PieceState`
- `BoardState`
- `ScoreState`
- `MatchEndState`

할 일:
- 어떤 값이 `NetworkVariable`인지 결정한다.
- 어떤 값이 RPC 이벤트인지 결정한다.
- 어떤 값이 서버 내부 전용 상태인지 결정한다.

완료 기준:
- 전체 경기 흐름의 상태 소유권이 명확해진다.

### 8. 서버 권한 경기 매니저 추가

권장 신규 구성:
- `NetworkMatchManager`

할 일:
- 기존 `GameModeManager`를 감싸거나 대체하는 서버 권한 진입점을 만든다.
- 상태 전이는 서버만 수행하게 막는다.
- 클라이언트는 읽기와 요청만 가능하게 제한한다.

완료 기준:
- 경기 상태를 임의로 바꿀 수 있는 코드는 서버 쪽에만 남는다.

## Phase 4. Authoritative Flick

### 9. 클라이언트 입력 요청 RPC 구현

요청 예시:
- `SubmitFlickRequest(pieceId, dragVector, strength)`

검증 항목:
- 현재 상태가 `PlayerFlicking`인지
- 요청자가 현재 턴 플레이어인지
- 해당 말이 요청자 소유인지
- 이미 발사된 말이 아닌지
- 힘 크기가 허용 범위 안인지

완료 기준:
- 클라이언트는 로컬에서 발사를 확정하지 못하고 서버 승인 경로만 사용한다.

### 10. 서버 물리 적용 및 정지 판정

할 일:
- 서버가 실제 `Rigidbody`에 힘을 가한다.
- 서버가 정지 시점을 판정한다.
- 서버가 이탈/낙하를 판정한다.
- 서버가 턴 종료를 확정한다.

완료 기준:
- 말의 최종 결과는 서버 기준으로만 확정된다.

### 11. 클라이언트 시각 동기화

할 일:
- 발사 시작 이벤트를 클라이언트에 전달한다.
- 최종 위치와 상태를 반영한다.
- 하이라이트와 턴 UI는 서버 상태를 기반으로만 갱신한다.

완료 기준:
- 각 클라이언트는 같은 경기 결과를 본다.

## Phase 5. Placement And Score Authority

### 12. 서버 배치 후보 계산

대상 파일:
- `Assets/02_Scripts/Board/GridCellCandidateResolver.cs`
- `Assets/02_Scripts/Core/GameModeManager.cs`

할 일:
- 멈춘 말의 셀 후보 계산을 서버 책임으로 고정한다.
- 후보 목록 직렬화 방식을 정의한다.

완료 기준:
- 배치 후보는 서버가 만들고 클라이언트는 표시만 한다.

### 13. 배치 선택 제출 및 확정

할 일:
- 클라이언트의 셀 선택을 서버 RPC로 보낸다.
- 서버가 유효 셀인지 검사한다.
- 서버가 `TokenMapManager` 기준으로 점유를 확정한다.

완료 기준:
- 보드 점유 상태가 클라이언트 로컬 로직으로 바뀌지 않는다.

### 14. 카드 매칭 및 점수 서버 확정

대상 연관 파일:
- `Assets/02_Scripts/Cards/*`
- `Assets/02_Scripts/UI/PlayerScoreHud.cs`

할 일:
- 카드 판정과 점수 계산을 서버만 수행하게 정리한다.
- 점수 UI는 서버 동기화 값만 읽도록 바꾼다.
- 승리 조건 판정도 서버에서 수행한다.

완료 기준:
- 점수와 승패가 클라이언트마다 다르게 보이지 않는다.

## Phase 6. Session Lifecycle

### 15. 매치 시작 조건 정리

할 일:
- 두 플레이어가 모두 접속했을 때만 시작하게 한다.
- 준비 상태와 시작 상태를 구분한다.
- 타임아웃이나 취소 흐름을 정의한다.

완료 기준:
- 경기 시작 조건이 명확하고 자동화된다.

### 16. 이탈 및 종료 처리

할 일:
- 한쪽 이탈 시 즉시 패배 처리할지 결정한다.
- 남은 플레이어에게 결과를 전달한다.
- 세션 종료 후 서버 정리 흐름을 만든다.

완료 기준:
- 이탈 상황에서도 서버 상태가 꼬이지 않는다.

## Scene / Prefab Checklist

확인할 것:
- 네트워크 동기화 대상 말 프리팹 분리 여부
- `NetworkObject` 부착 대상 정의
- 경기 매니저의 씬 배치 위치
- 카메라/HUD의 클라이언트 전용 처리 여부

완료 기준:
- 서버 전용 오브젝트와 클라이언트 전용 오브젝트가 섞이지 않는다.

## Testing Checklist

### Local Multiplayer Test

- 서버 1개 + 클라이언트 2개 연결
- 턴 순서 정상 동기화
- 잘못된 플레이어 입력 거부
- 말 발사 후 양쪽 최종 위치 일치
- 보드 밖 이탈 동일 처리
- 배치 후보 동일 표시
- 점수 동일 반영
- 경기 종료 동일 표시

### Failure Test

- 접속 중 한 클라이언트 이탈
- 턴 도중 이탈
- 중복 입력 요청
- 허용 범위를 넘는 힘 요청
- 이미 죽은 말 요청

## Suggested First Coding Tasks

바로 시작할 작업 3개:

1. `GameModeManager`를 순수 경기 흐름 중심으로 정리하는 리팩터링 초안 작성
2. `TurnBasedFlickPiece`를 입력/시뮬레이션/표현 계층으로 나누는 클래스 설계
3. NGO 도입 전 필요한 네트워크 상태 모델 초안 작성

## Definition Of Ready For Networking

아래 조건을 만족하면 네트워크 구현을 시작해도 된다.

- 로컬 입력 코드와 경기 규칙 코드의 경계가 정리되어 있다.
- 서버가 소유해야 할 상태 목록이 문서화되어 있다.
- 클라이언트가 보내는 요청 타입이 정의되어 있다.
- 씬과 프리팹에서 네트워크 대상 오브젝트가 식별되어 있다.
