# Flick Board Scene Authoring

`SinWoo Scene`의 플릭 보드와 말은 런타임 복제가 아니라 씬에 배치된 모델을 사용한다.

## Scene hierarchy

권장 구조:

```text
Board
├─ PlayBoard
├─ Cells
│  └─ Cell_00 ... Cell_44
└─ Tokens
   ├─ FlickDisk_P1_1 ... FlickDisk_P1_3
   └─ FlickDisk_P2_1 ... FlickDisk_P2_3
```

- 모델러는 `Board` 아래의 모델, 위치, 회전, 스케일을 편집한다.
- `Tokens` 아래의 디스크는 플레이 시작 전에 원하는 시작 위치에 놓는다.
- 원본 FBX 파일에 게임플레이 컴포넌트를 추가할 필요는 없다.

## LocalFlickTurnTestRig setup

`Game Systems` 오브젝트의 `LocalFlickTurnTestRig`에서 다음 항목을 사용한다.

- `Player 1 Piece Objects`: P1 디스크 3개
- `Player 2 Piece Objects`: P2 디스크 3개
- 배열 순서: 기본 말 순서와 `P1_1`/`P2_1` 형식의 ID 순서
- `Configure Authored Piece Components`: 활성화
- `Auto Create Missing Pieces`: 비활성화

플레이 시 등록된 씬 오브젝트 자체에 Rigidbody, Collider, `TokenSetup`,
`FlickVisuals`, `TurnBasedFlickPiece`가 필요한 경우에만 추가된다. 시각 모델을
복제하거나 시작 위치를 다시 정렬하지 않는다.

## Replacing art

1. 새 디스크 모델을 `Tokens` 아래 원하는 위치에 배치한다.
2. 기존 이름 규칙을 유지하면 Hierarchy를 읽기 쉽다. 이름은 런타임 연결 키로 사용하지 않는다.
3. 해당 플레이어의 `Piece Objects` 배열에서 참조를 새 Transform으로 교체한다.
4. 배열 순서와 `Token Data Sequence`의 Wood/Iron/Rubber 순서가 맞는지 확인한다.
5. 기존 디스크를 씬에서 제거한다.

## Board boundaries

현재 플릭 좌표 판정은 `GridCellCandidateResolver`의 5x5 설정을 사용한다.
모델의 셀 배치를 바꾸면 다음 값도 함께 확인한다.

- `Board Size`
- `Cell Size`
- `Board Origin`
- `Board Y`

향후 점령 턴에 사용하는 `TokenMapGridView`는 별도 시스템이다. 플릭 보드의
아트 배치 변경을 위해 해당 그리드를 수정할 필요는 없다.
