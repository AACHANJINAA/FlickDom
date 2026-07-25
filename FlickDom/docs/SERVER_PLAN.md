# Server Plan

`FlickDom`을 Unity 문서의 권장 방향에 맞춰 멀티플레이 세션형 게임으로 전환하기 위한 계획 문서다.

기준 문서:
- Unity: https://unity.com/kr/how-to/how-build-design-your-multiplayer-game

## Goal

- `FlickDom`을 2인 온라인 대전이 가능한 짧은 세션형 멀티플레이 게임으로 전환한다.
- 게임 서버는 `authoritative dedicated server`로 구성한다.
- 게임 서버 프로세스는 가능한 한 단순하게 유지한다.
- 매치메이킹, 로비, 운영성 기능은 게임 서버 밖으로 분리한다.

## Why This Fits FlickDom

현재 프로젝트는 아래 특성 때문에 Unity 문서가 설명하는 구조와 잘 맞는다.

- 게임 진행이 `라운드`, `턴`, `배치`, `카드 매칭` 중심이다.
- 한 매치의 참여 인원이 적다.
- MMO처럼 영구 월드를 유지할 필요가 없다.
- 짧은 세션 기반으로 서버를 만들기 쉽다.

즉, `한 매치 = 한 서버 인스턴스` 모델이 가장 단순하고 안전하다.

## Target Architecture

### 1. Client

클라이언트 책임:

- 로컬 입력 처리
- 드래그 프리뷰, 카메라, HUD, 연출
- 서버 상태 수신 후 화면 반영
- 서버에 `의도(intent)`만 전송

클라이언트가 직접 확정하면 안 되는 것:

- 턴 유효성 판정
- 최종 물리 결과 판정
- 보드 밖 이탈 판정
- 배치 후보 결정
- 점수 계산
- 카드 매칭 결과

### 2. Game Server

게임 서버 책임:

- 현재 라운드와 턴 관리
- 어떤 플레이어가 어떤 말을 조작할 수 있는지 검증
- 플릭 힘 적용
- 물리 시뮬레이션 결과 확정
- 말 정지 판정
- 보드 밖 이탈 및 무효 처리
- 배치 후보 계산
- 배치 확정
- 카드 매칭 및 점수 계산
- 라운드 종료와 다음 라운드 시작

게임 서버는 가급적 `단일 실행 파일 + 단일 게임 세션 책임` 구조를 유지한다.

### 3. Backend Services

게임 서버 밖으로 분리할 책임:

- 매치메이킹
- 로비
- 서버 할당
- 재접속 토큰 관리
- 메트릭 수집
- 로그 수집
- 운영 대시보드

## Recommended Stack

MVP 기준 권장 선택:

- Unity Transport
- Netcode for GameObjects
- Dedicated Server Build

이유:

- 현재 프로젝트 규모에서는 가장 진입 비용이 낮다.
- 2인 턴 기반 세션 게임에 충분하다.
- 이후 Unity Gaming Services 또는 자체 백엔드와 연결하기 쉽다.

## Current Project Assessment

현재 코드에서 멀티플레이 전환의 핵심 지점은 다음과 같다.

- `Assets/02_Scripts/Core/GameModeManager.cs`
  - 라운드와 턴 상태 머신이 이미 존재한다.
  - 서버 권한 로직의 중심이 되기 좋다.
- `Assets/02_Scripts/Core/FlickDomGameState.cs`
  - 네트워크 동기화 대상이 되는 상태 enum이다.
- `Assets/02_Scripts/Flick_Scripts/TurnBasedFlickPiece.cs`
  - 현재 입력, 물리, 정지 판정, 상태 표시가 한 클래스에 섞여 있다.
  - 서버 시뮬레이션과 클라이언트 프리젠테이션으로 분리해야 한다.
- `Assets/02_Scripts/Flick_Scripts/FlickMain.cs`
  - 로컬 입력 중심 예전 방식 코드다.
  - 멀티플레이 전환 시 제거하거나 테스트 전용으로 격리하는 편이 낫다.

## Core Rule For Multiplayer Migration

멀티플레이 전환의 핵심 원칙은 하나다.

- 클라이언트는 `무엇을 하고 싶다`만 보내고
- 서버는 `실제로 무엇이 일어났는가`를 결정한다

예시:

- 클라이언트 전송: `pieceId`, `drag direction`, `force magnitude`
- 서버 확정: `이 플레이어가 이 말을 지금 튕길 수 있는지`, `최종 위치`, `사망 여부`, `배치 후보`

## Refactor Plan

## Phase 1. Rules And Responsibilities Split

목표:
- 로컬 싱글플레이 구조를 네트워크 친화 구조로 바꾼다.

작업:

- `GameModeManager`에서 순수 규칙 로직을 분리한다.
- 턴 전환, 라운드 종료, 배치 확정, 점수 계산을 입력/UI와 분리한다.
- `TurnBasedFlickPiece`를 아래 역할로 분리한다.
  - 입력 수집
  - 서버 시뮬레이션
  - 시각 표시
- 네트워크 없이도 호출 가능한 서비스 계층을 만든다.

권장 결과물 예시:

- `MatchState`
- `TurnService`
- `PlacementResolutionService`
- `ScoreService`

## Phase 2. Network Foundation

목표:
- 게임 룸 접속과 기본 상태 동기화를 만든다.

작업:

- `Netcode for GameObjects` 패키지를 추가한다.
- `NetworkManager` 기반 접속 흐름을 만든다.
- 호스트 모드가 아닌 `dedicated server` 기준 흐름을 설계한다.
- 플레이어 접속 시 `Player1`, `Player2` 할당 규칙을 만든다.
- 매치 시작 전 대기 상태를 추가한다.

필수 동기화 상태:

- 접속 플레이어 목록
- 플레이어 슬롯 점유 상태
- 매치 시작 가능 여부
- 현재 게임 상태
- 현재 활성 플레이어

## Phase 3. Authoritative Flick Flow

목표:
- 플릭 입력을 서버 권한 방식으로 바꾼다.

작업:

- 클라이언트는 드래그 프리뷰만 로컬에서 처리한다.
- 발사 시점에 서버 RPC로 플릭 요청을 보낸다.
- 서버가 요청을 검증한다.
- 서버가 물리 힘을 실제로 적용한다.
- 서버가 정지 여부를 판정한다.
- 결과를 클라이언트에 동기화한다.

서버 검증 항목:

- 현재 상태가 `PlayerFlicking`인지
- 요청한 플레이어가 현재 턴 플레이어인지
- 요청한 말이 해당 플레이어 소유인지
- 이미 발사한 말을 다시 요청한 것은 아닌지
- 힘 벡터가 허용 범위를 넘지 않는지

## Phase 4. Placement And Scoring Sync

목표:
- 발사 후 보드 반영과 점수 계산을 서버 단일 기준으로 확정한다.

작업:

- 서버가 멈춘 말의 후보 셀을 계산한다.
- 서버가 `PlacementSelection` 상태를 연다.
- 클라이언트는 후보 UI만 표시한다.
- 플레이어 선택은 서버에 제출한다.
- 서버가 유효성 검사 후 맵 점유를 확정한다.
- 서버가 카드 매칭과 점수를 계산한다.
- 결과를 모든 클라이언트에 반영한다.

## Phase 5. Session Lifecycle

목표:
- 짧은 세션형 운영 흐름을 완성한다.

작업:

- 매치 생성
- 플레이어 2명 입장
- 매치 시작
- 경기 종료
- 결과 표시
- 서버 종료 또는 풀 복귀

이 단계에서 고려할 것:

- 재접속 허용 여부
- 한쪽 이탈 시 처리 규칙
- 타임아웃 규칙
- 강제 종료 처리

## Network Data Model

최소 동기화 대상 권장안:

- `GameState`
- `RoundNumber`
- `ActivePlayerId`
- `PieceNetworkState`
- `Dead/Alive`
- `PlacementCandidates`
- `BoardClaimState`
- `ScoreState`
- `MatchResult`

중요:

- 매 프레임 전체 보드를 무겁게 동기화하지 않는다.
- 이벤트 중심 동기화와 필요한 상태 복제만 사용한다.

## Proposed Server RPC / Event Flow

예시 흐름:

1. 클라이언트가 드래그를 시작한다.
2. 클라이언트는 로컬에서 프리뷰만 보여준다.
3. 클라이언트가 발사 시 `SubmitFlickRequest(pieceId, force)`를 서버로 보낸다.
4. 서버가 요청을 검증한다.
5. 서버가 물리 힘을 적용한다.
6. 서버가 정지까지 시뮬레이션한다.
7. 서버가 무효 여부와 위치를 확정한다.
8. 서버가 필요하면 배치 후보를 생성한다.
9. 서버가 다음 상태로 전이한다.
10. 클라이언트는 서버 상태를 화면에 반영한다.

## Dedicated Server Principles

반드시 지킬 원칙:

- 게임 서버 안에 매치메이커를 넣지 않는다.
- 게임 서버 안에 운영용 로그 수집기를 별도 프로세스로 붙이지 않는다.
- 게임 서버는 한 세션 책임만 가진다.
- 서버 프로세스 종료 시 보조 자원이 남지 않게 한다.
- 운영성 기능은 외부 서비스에 위임한다.

## MVP Scope

첫 번째 온라인 MVP는 아래 범위로 제한하는 것이 좋다.

- 2인 매치만 지원
- 관전 미지원
- 재접속 미지원 또는 간단한 타임아웃만 지원
- 랭크 미지원
- 커스텀 매치메이킹 없이 수동 룸 생성만 지원 가능
- 전용 서버 1개에서 방 1개 실행

이 범위를 넘기면 구현 속도보다 운영 복잡도가 먼저 증가한다.

## Risks

주요 리스크:

- 클라이언트와 서버 물리 결과 불일치
- 입력, 물리, UI가 한 클래스에 섞여 있어 분리 비용이 큼
- 짧은 세션 구조에서 서버 시작/종료 비용 최적화 필요
- 잘못된 상태 동기화 설계 시 턴 진행 desync 발생 가능

특히 `TurnBasedFlickPiece`의 책임 분리가 가장 먼저 해결되어야 한다.

## Suggested Work Order

실제 작업 순서 권장안:

1. `GameModeManager` 규칙 로직 정리
2. `TurnBasedFlickPiece` 책임 분리
3. 로컬 전용 입력 코드 정리
4. NGO 기반 네트워크 연결 추가
5. 서버 권한 플릭 요청 구현
6. 배치/점수 서버 확정 구현
7. 접속/퇴장/종료 흐름 구현
8. 서버 빌드 및 세션 운영 점검

## Definition Of Done

아래를 만족하면 1차 목표 달성으로 본다.

- 두 클라이언트가 같은 룸에 접속할 수 있다.
- 서버가 현재 턴과 유효 입력을 통제한다.
- 클라이언트는 서버 승인 없이 말 결과를 확정할 수 없다.
- 플릭 후 최종 위치와 상태가 모든 클라이언트에서 동일하다.
- 배치, 카드 매칭, 점수 계산이 서버 기준으로 동일하게 확정된다.
- 경기 종료 후 세션을 정상 종료할 수 있다.

## Next Implementation Task

가장 먼저 할 일:

- `GameModeManager`와 `TurnBasedFlickPiece`를 기준으로
  - 순수 규칙 로직
  - 네트워크 동기화 대상 상태
  - 클라이언트 전용 시각 처리
  로 분리하는 리팩터링 설계를 확정한다.
