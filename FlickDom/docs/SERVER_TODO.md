# Server TODO

`good_Scene` 기준 2인 Host-Client 구현 상태를 추적하는 체크리스트다.

## Network Foundation

- [O] `good_Scene`에서만 네트워크 부트스트랩이 자동 생성되도록 제한
- [O] 런타임 `NetworkManager` + `UnityTransport` 생성
- [O] Host 방 생성, Client IP/Port 접속 UI 추가
- [O] 2명 초과 접속 차단
- [O] Host는 `Player1`, Client는 `Player2`로 로컬 역할 배정
- [O] Host/Client 시작과 Shutdown 로그 출력
- [O] 빌드 실행 파일용 `-host`, `-client`, `-address`, `-port` 인자 지원
- [O] Multiplayer Play Mode 호환 문제로 제거하고 Editor Host + Windows build Client 테스트 방식으로 정리
- [ ] Unity Relay 연결 추가
- [ ] Relay Join Code 기반 접속 UI로 확장

## Lobby And Match Start

- [O] 중앙 로비 UI 추가
- [O] 로비 인원 `Players: 1/2`, `2/2` 동기화
- [O] 2명 접속 후 Host만 `Start Game` 가능
- [O] 게임 시작 후 로비 UI 숨김
- [O] StartGame 중복 수신으로 로컬 게임이 두 번 시작되지 않도록 guard 추가
- [ ] 준비 완료 상태와 게임 시작 상태 분리
- [ ] 한쪽 이탈 시 로비/게임 상태 복구 정책 정의

## Turn And Input Authority

- [O] 네트워크 세션 중 자기 `LocalPlayerId` 말만 선택/플릭 가능
- [O] Client의 로컬 디버그 상태 진행 단축키 차단
- [O] Host만 경기 상태 전이를 수행하도록 입력 게이트 추가
- [O] Host가 `GameState`, `ActivePlayer`, `RoundNumber`를 Client에 브로드캐스트
- [O] Client가 Host 상태 스냅샷을 적용해 턴 표시와 입력 허용 기준을 따라가도록 처리
- [ ] `CurrentTurnIndex`까지 포함한 완전한 턴 상태 동기화
- [ ] 라운드 종료/다음 라운드 시작 흐름 네트워크 검증

## Piece Order Selection

- [O] 말 선택 순서를 네트워크 게임에서도 사용하도록 복구
- [O] Client의 P2 선택을 Host에 요청
- [O] Host가 선택 유효성을 확인하고 확정
- [O] Host가 확정한 선택 순서를 Client에 브로드캐스트
- [O] P1/P2 양쪽 선택 순서 표시가 같은 리스트를 기준으로 보이도록 수정
- [ ] 선택 순서 후보/완료 상태를 재접속 Client에도 복구할 수 있게 스냅샷화

## Authoritative Flick

- [O] Client 플릭 입력을 로컬 물리 적용 대신 Host 요청으로 전송
- [O] Host가 현재 턴/소유자/말 ID를 검사한 뒤 Rigidbody에 힘 적용
- [O] Host가 물리 정지와 보드 이탈 판정 담당
- [O] Host가 말 Transform을 주기적으로 Client에 전송
- [O] Client가 Host Transform을 받아 시각 위치를 따라가도록 처리
- [O] Host가 Client 플릭 요청 승인 시 `FlickAccepted`를 전송
- [O] Client가 `FlickAccepted` 기준으로 순서 인덱스와 launched 상태 갱신
- [O] Host가 직접 플릭한 P1 말도 Client에 `FlickAccepted` 전송
- [O] Client에서 승인된 말은 자체 물리를 켜지 않고 Host Transform follower로 유지
- [O] Host의 말 사망/삭제 상태를 Transform 동기화에 포함
- [ ] 플릭 힘/방향 범위 검증 강화
- [ ] 패킷 지연 시 위치 보간 처리

## Piece Sync Stability

- [O] `good_Scene`의 P2 중복 `TurnBasedFlickPiece` 컴포넌트 제거
- [O] 런타임 자동 복제 말에서 중복 `TurnBasedFlickPiece` 컴포넌트 제거 안전장치 추가
- [O] Host Transform 송신 시 같은 `Owner/PieceId` 중복 송신 방지
- [ ] 말 ID/소유자/초기 위치를 명시적인 스폰 데이터로 분리
- [ ] 자동 복제 테스트 구조를 네트워크용 프리팹 구조로 전환

## Placement And Tile Sync

- [O] Host만 배치 후보 셀을 계산하도록 고정
- [O] Host가 후보 셀 목록을 Client에 브로드캐스트
- [O] Client의 타일 클릭을 Host에 배치 요청으로 전송
- [O] Host가 `TokenMapManager.TryClaimCell`로 점유 확정
- [O] 확정된 점령칸, 이전 소유자, 재배치 source를 Client에 브로드캐스트
- [O] Client `TokenMapGridView`가 Host 보드 상태를 스냅샷으로 적용하도록 변경
- [O] 재배치가 필요한 경우 Client 요청/Host 승인 흐름 추가

## Score And Card Sync

- [O] Host만 카드 매칭과 점수 계산 수행
- [O] `PatternCardManager`의 P1/P2 점수 상태를 Client에 동기화
- [ ] 활성 카드, 완료 카드, 카드 라운드 변경 상태 동기화
- [O] 승리자와 최종 점수 동기화
- [O] `PlayerScoreHud`가 Host 점수 스냅샷으로 갱신되도록 변경
- [O] 승리 후 재시작/메뉴 복귀 네트워크 흐름 정의
- [O] 승리 UI의 `RESTART`/`MENU` 버튼을 네트워크 명령으로 연결

## Session Lifecycle

- [ ] Client 중도 이탈 처리
- [ ] Host 종료 시 Client 안내 및 로비 복귀
- [ ] 게임 중 새 Client 접속 거부 또는 관전 처리 결정
- [O] 승리 후 `RESTART` 시 양쪽 점수/보드/턴 상태 초기화 흐름 추가
- [O] 승리 후 `MENU` 시 방 연결은 유지하고 양쪽을 로비 UI 상태로 복귀하는 흐름 추가
- [ ] 세션 종료 후 NetworkManager 정리 확인

## Test Checklist

- [ ] Editor Host + Windows build Client 접속
- [ ] Host/Client 모두 `Players: 2/2` 표시
- [ ] Host `Start Game` 후 양쪽 로비 UI 숨김
- [ ] P1/P2 선택 순서 동일 표시
- [ ] P1 플릭 움직임이 P2 화면에 표시
- [ ] P2 플릭 요청이 Host 물리로 실행되고 P2 화면에 표시
- [ ] P2 두 번째 턴 이후에도 턴 진행
- [ ] P2 말 3개가 겹치지 않고 선택 가능
- [ ] 배치 후보 셀 양쪽 동일 표시
- [ ] 타일 점유 상태 양쪽 동일 표시
- [ ] 점수와 승리 UI 양쪽 동일 표시
- [ ] 승리 후 Host/Client 중 한쪽이 `RESTART`를 눌러도 양쪽 새 경기 시작
- [ ] 승리 후 Host/Client 중 한쪽이 `MENU`를 눌러도 양쪽 로비 UI 복귀
