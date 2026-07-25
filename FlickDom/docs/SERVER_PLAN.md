# FlickDom Server Plan

FlickDom의 2인 멀티플레이를 위한 서버/네트워크 구현 계획이다. 현재 프로젝트는 로컬 턴제 물리 게임 구조가 이미 잡혀 있으므로, Dedicated Server보다 Host-Client(Listen Server)를 우선 목표로 한다.

## Target Architecture

### Server Model

- 모델: Host-Client(Listen Server)
- Host: P1 방장. 서버 권한과 로컬 클라이언트를 함께 가진다.
- Client: P2 손님. 직접 게임 결과를 확정하지 않고 입력 의도만 Host에 보낸다.
- Dedicated Server: 현재 MVP 범위에서는 제외한다.

이 선택이 맞는 이유:

- FlickDom은 기본 2인 매치다.
- 방 하나에 필요한 상태가 작고, 장시간 영속 서버가 필요하지 않다.
- 물리 판정이 중요해서 한쪽 권위자가 필요하지만, 별도 서버 운영까지는 과하다.
- Unity Relay를 붙이면 포트포워딩 없이 Host-Client 구조를 유지할 수 있다.

### Networking Stack

목표 스택:

- Unity Netcode for GameObjects(NGO)
- Unity Transport
- Unity Relay

단계별 적용:

- 1차: NGO + Unity Transport로 localhost/LAN Host-Client 연결
- 2차: Host 권위 물리와 턴 동기화
- 3차: Relay 연결

주의: 현재 프로젝트는 Unity 6000.5.1f1이며, 이전 로그에 NGO/Transport 패키지 버전 호환 문제로 컴파일 에러가 있었다. 따라서 Relay나 게임 로직보다 먼저 Unity 6.5와 호환되는 NGO/Transport 버전을 고정해야 한다.

## Project Fit

현재 프로젝트에서 네트워크 전환의 중심이 될 코드:

- `Assets/02_Scripts/Core/GameModeManager.cs`
  - 턴, 라운드, 점수, 배치 상태의 중심이다.
  - 최종적으로 Host만 상태 전이를 실행해야 한다.

- `Assets/02_Scripts/Core/LocalFlickTurnTestRig.cs`
  - 현재 로컬 턴 흐름과 말 선택/진행을 묶고 있다.
  - 네트워크 전환 시 Host 권위 매치 매니저가 이 역할을 감싸거나 대체해야 한다.

- `Assets/02_Scripts/Flick_Scripts/TurnBasedFlickPiece.cs`
  - 입력, 물리, 정착 판정, 무효 판정, 시각 표시가 함께 들어 있다.
  - 바로 `NetworkBehaviour`로 바꾸기보다 네트워크 어댑터를 붙이는 방식이 안전하다.

- `Assets/02_Scripts/UI/PlayerScoreHud.cs`
  - 로컬 상태를 UI에 반영한다.
  - 네트워크 전환 후에는 Host가 확정한 상태만 표시해야 한다.

## Core Multiplayer Rule

클라이언트는 "내가 무엇을 하고 싶다"만 보낸다. Host는 "실제로 무엇이 일어났는가"를 결정한다.

예시:

- Client request: `pieceId`, `dragVector`, `strength`, `releasePosition`
- Host validation: 현재 턴인지, 해당 말의 소유자인지, 이미 발사된 말은 아닌지, 힘이 허용 범위인지
- Host authority: 실제 `Rigidbody.AddForce`, settle 판정, 사망/이탈 판정, 배치 후보, 점수 계산, 턴 전환
- Client presentation: Host가 보낸 Transform/State를 따라 화면만 갱신

## Physics Authority

모든 결과를 결정하는 물리 연산은 Host에서만 실행한다.

### Host Turn

- P1은 로컬 입력으로 말을 조작한다.
- 실제 `AddForce`는 Host의 Rigidbody에만 적용된다.
- 움직이는 동안 위치/회전은 네트워크로 Client에 동기화된다.
- 멈춘 뒤 최종 위치, 생존 여부, 점수, 턴 상태를 Host가 확정한다.

### Client Turn

- P2는 자기 화면에서 드래그/조준 입력을 한다.
- P2는 직접 Rigidbody에 힘을 주지 않는다.
- P2는 `ServerRpc`로 입력 의도만 Host에 보낸다.
- Host는 요청을 검증한 뒤 자신의 물리 월드에서 말을 굴린다.
- P2 화면은 Host의 결과 Transform을 따라간다.

### Client Rigidbody Policy

Client 쪽의 네트워크 물리 오브젝트는 결과를 만들면 안 된다.

- Client에서는 Rigidbody를 kinematic 또는 visual follower처럼 다룬다.
- 충돌/이탈/정착 판정은 Host만 한다.
- 움직이는 동안은 `NetworkTransform`으로 추적한다.
- 멈춘 뒤에는 최종 상태를 RPC 또는 NetworkVariable로 명시 확정한다.

## Camera And UI

카메라는 네트워크 오브젝트로 만들지 않는다.

- P1 카메라와 P2 카메라는 각자 로컬에서만 제어한다.
- P2는 보드 반대편 시점 또는 관전 시점으로 초기화할 수 있다.
- 자기 턴이 아닐 때 게임 입력은 차단한다.
- 관전 중 카메라 회전/이동은 허용할 수 있다.
- UI는 Host가 확정한 상태만 표시한다.

## Network Objects

우선 네트워크 대상으로 둘 것:

- 플레이어 말
- 물리로 움직이는 주사위나 토큰
- 매치 상태 관리자

네트워크 대상으로 두지 않을 것:

- Main Camera
- HUD Canvas
- 로컬 입력 프리뷰
- 단순 시각 효과

필수 컴포넌트 후보:

- `NetworkObject`
- `NetworkTransform` 또는 물리 특화 동기화 컴포넌트
- `NetworkFlickPiece` 같은 네트워크 어댑터

## RPC Flow

### Flick Request

1. Active player가 로컬에서 드래그한다.
2. 로컬은 조준선/프리뷰만 보여준다.
3. release 시 `SubmitFlickServerRpc(pieceId, dragVector, strength)`를 보낸다.
4. Host가 요청을 검증한다.
5. Host가 Rigidbody에 힘을 적용한다.
6. Host가 움직임을 동기화한다.
7. Host가 settle을 판정한다.
8. Host가 배치/점수/턴 전환을 확정한다.
9. Client는 확정 상태를 UI와 화면에 반영한다.

### Turn Notification

- Host는 `ActivePlayerId`를 NetworkVariable 또는 ClientRpc로 갱신한다.
- 각 클라이언트는 `IsMyTurn`을 계산해 입력 허용 여부를 결정한다.
- 입력 차단은 UI 차단만으로 끝내지 말고 게임 오브젝트 상호작용 코드에서도 검사한다.

## Implementation Phases

### Phase 0. Package Compatibility

목표: NGO를 넣어도 프로젝트가 깨지지 않는 상태를 만든다.

작업:

- Unity 6000.5.1f1과 호환되는 `com.unity.netcode.gameobjects` 버전 확인
- 호환되는 `com.unity.transport` 버전 확인
- Relay를 나중에 붙이기 위해 Authentication/Relay 패키지 후보 확인
- 패키지 추가 후 컴파일 에러가 없는지 검증

완료 기준:

- `Unity.Netcode`, `Unity.Netcode.Transports.UTP` 네임스페이스 참조가 컴파일된다.
- Netcode 패키지 내부 obsolete API가 error로 승격되어 빌드가 깨지지 않는다.

### Phase 1. Local Logic Boundary Cleanup

목표: 기존 로컬 게임 로직을 Host 권위 구조로 감쌀 수 있게 책임을 나눈다.

작업:

- `GameModeManager`의 순수 규칙 로직과 UI/입력 의존성을 분리한다.
- `TurnBasedFlickPiece`에서 입력 수집, 물리 실행, 시각 표시 책임을 구분한다.
- `FlickMain` 같은 오래된 로컬 입력 코드가 중복 실행되지 않게 정리한다.
- 현재 로컬 모드가 계속 동작하도록 유지한다.

완료 기준:

- 로컬 싱글/테스트 플레이가 기존처럼 동작한다.
- "입력 의도 생성"과 "실제 물리 적용" 경계가 코드상 명확하다.

### Phase 2. Network Foundation

목표: 두 인스턴스가 같은 룸에 들어오고 역할을 배정받는다.

작업:

- `NetworkBootstrap` 또는 유사한 시작 매니저 추가
- Host 시작 버튼/경로 추가
- Client 접속 버튼/경로 추가
- 접속 순서에 따라 P1/P2 역할 배정
- 2명 초과 접속 차단
- NetworkManager 프리팹 또는 부트스트랩 씬 정리

완료 기준:

- 로컬에서 Host 1개, Client 1개가 접속된다.
- Host는 P1, Client는 P2로 안정적으로 배정된다.
- 아직 Relay 없이 LAN/localhost로 검증 가능하다.

### Phase 3. Host-Authoritative State

목표: 턴과 매치 상태를 Host가 확정하고 Client가 따라가게 한다.

동기화 대상:

- `GameState`
- `RoundNumber`
- `ActivePlayerId`
- 각 말의 owner/id/dead/alive
- 점수
- 배치 후보와 선택 결과
- 매치 종료 상태

작업:

- `NetworkMatchManager` 추가
- Host만 `GameModeManager` 상태 전이를 실행하도록 제한
- Client는 상태 변경 요청만 보낼 수 있게 제한
- UI는 네트워크 상태를 구독해 갱신

완료 기준:

- Client가 임의로 턴/점수를 바꿀 수 없다.
- 양쪽 UI의 현재 턴과 점수가 일치한다.

### Phase 4. Authoritative Flick

목표: P2 턴에서도 Host 물리 결과를 모두가 보게 한다.

작업:

- `NetworkFlickPiece` 또는 유사 어댑터 추가
- `SubmitFlickServerRpc(pieceId, dragVector, strength)` 구현
- Host 요청 검증 구현
- Host에서만 Rigidbody force 적용
- Client Rigidbody는 결과 생성에 관여하지 않도록 설정
- 움직임 중 Transform 동기화
- 정착 후 최종 위치/회전/상태 확정

완료 기준:

- P1이 던진 결과가 P2에 보인다.
- P2가 던진 입력이 Host에서 물리 실행되고 P2에 보인다.
- 잘못된 턴/잘못된 말/과도한 힘 요청은 거절된다.

### Phase 5. Placement And Scoring Authority

목표: 배치 후보, 보드 점유, 카드 매칭, 점수를 Host 기준으로 확정한다.

작업:

- Host가 멈춘 말의 배치 후보를 계산한다.
- Client는 후보 UI만 표시한다.
- 배치 선택은 ServerRpc로 제출한다.
- Host가 선택 유효성을 검증한다.
- Host가 `TokenMapManager`, 카드 매칭, 점수를 확정한다.
- 결과를 모든 클라이언트에 반영한다.

완료 기준:

- 양쪽 보드 점유 상태가 일치한다.
- 점수와 승패가 양쪽에서 동일하다.

### Phase 6. Relay Integration

목표: 포트포워딩 없이 외부 네트워크에서 2인이 접속할 수 있게 한다.

작업:

- Unity Authentication 초기화
- Host가 Relay allocation 생성
- Join code 생성 및 표시
- Client가 Join code로 Relay 접속
- Unity Transport에 Relay server data 설정
- Relay 연결 실패/취소 UI 처리

완료 기준:

- 서로 다른 네트워크의 두 플레이어가 Join code로 접속된다.
- Host-Client 물리/턴 흐름이 Relay 경유에서도 유지된다.

## Minimal Data Model

초기 MVP에서 필요한 네트워크 데이터:

- `PlayerId`
- `ActivePlayerId`
- `FlickDomGameState`
- `RoundNumber`
- `PieceId`
- `PieceOwner`
- `PieceAlive`
- `PiecePosition`
- `PieceRotation`
- `ScoreP1`
- `ScoreP2`
- `PlacementCandidates`
- `BoardClaimState`
- `MatchResult`

모든 데이터를 매 프레임 보내지 않는다. 움직임은 Transform 동기화, 규칙 상태는 이벤트/NetworkVariable 중심으로 보낸다.

## Risks

- NGO/Transport 버전이 Unity 6000.5.1f1과 맞지 않으면 프로젝트가 컴파일 단계에서 막힌다.
- `TurnBasedFlickPiece`에 입력/물리/시각 책임이 섞여 있어 바로 네트워크화하면 회귀가 커진다.
- Client에서도 Rigidbody가 실제 충돌을 만들면 Host와 desync가 난다.
- `NetworkTransform`만 믿으면 정착 순간의 최종 상태가 미세하게 다를 수 있다.
- Relay까지 한 번에 붙이면 연결 문제와 게임 로직 문제를 구분하기 어려워진다.

## Suggested Work Order

1. NGO/Transport 호환 버전 확정
2. Host/Client 로컬 접속 부트스트랩 작성
3. P1/P2 역할 배정 구현
4. `GameModeManager` 상태를 Host 권위로 감싸기
5. `TurnBasedFlickPiece`에 네트워크 어댑터 추가
6. `SubmitFlickServerRpc`와 Host 물리 실행 구현
7. Transform 동기화와 최종 상태 확정 구현
8. 배치/점수/턴 전환 Host 확정 구현
9. Relay 연결 추가
10. 외부 네트워크 2인 테스트

## MVP Definition Of Done

- Host 1명과 Client 1명이 같은 게임에 접속할 수 있다.
- P1/P2 역할이 안정적으로 배정된다.
- 자기 턴이 아닌 플레이어는 게임 오브젝트를 조작할 수 없다.
- P2의 입력은 직접 물리를 실행하지 않고 Host에 요청된다.
- 모든 물리 결과와 점수는 Host 기준으로 확정된다.
- 양쪽 화면에서 말 위치, 보드 상태, 점수, 턴이 일치한다.
- Relay Join code로 외부 네트워크 접속이 가능하다.

## Next Implementation Task

가장 먼저 할 일은 Relay가 아니라 NGO/Transport 호환성 검증이다.

1. Unity 6000.5.1f1에서 컴파일 가능한 NGO/Transport 버전을 고른다.
2. `NetworkManager`와 `UnityTransport`가 컴파일되는 최소 부트스트랩을 만든다.
3. 기존 로컬 턴 게임을 건드리지 않고 Host/Client 접속만 먼저 검증한다.
