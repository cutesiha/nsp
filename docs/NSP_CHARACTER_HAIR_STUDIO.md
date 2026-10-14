# 캐릭터 헤어 스튜디오

8명의 캐릭터에게 머리카락을 하나씩 골라 저장하는 도구다. **게임에는 아직 아무것도 적용되지 않는다** —
고른 결과를 설정 파일로 남길 뿐이고, 실제 모델 교체는 다음 단계의 일이다.

---

## 1. 스튜디오 열기

에디터에서 `res://scenes/tools/CharacterHairStudio.tscn` 을 열고 **F6**.

명령줄로 띄우려면:

```
godot --path . res://scenes/tools/CharacterHairStudio.tscn
```

창 모드로 돌려야 한다. 메인 타이틀이나 게임 흐름과는 연결돼 있지 않다.

### 화면

| 자리 | 내용 |
|---|---|
| 왼쪽 | 캐릭터 8명 · 베이스 성별(MALE/FEMALE) · 지금 고른 내용 · **[선택 결과 저장]** |
| 가운데 | 3D 미리보기. 드래그=회전, 휠=확대/축소, **[상반신 확대]** 로 얼굴 접사, 정면/측면/뒤, 앞뒤 뒤집기 |
| 오른쪽 | 헤어 목록(썸네일) · 길이 필터 · POSITION/ROTATION/SCALE · HAIR COLOR · **[위치 초기화]** |

캐릭터 이름 옆의 `✓` 는 저장된 선택이 있다는 뜻이다. 고르기 전에는 아무도 확정돼 있지 않다.

목록에는 **남녀 헤어 114개가 모두** 올라온다. 남성 팩이 9개뿐이라 그것만으로는 고를 거리가 없어서,
남성 베이스에서도 여성 팩(105개)을 그대로 쓸 수 있게 해 뒀다. 지금 베이스의 팩이 목록 앞에 오고,
`남녀 헤어 모두 보기` 를 끄면 그 성별 팩만 남는다. 크기 맞춤은 **지금 올려둔 베이스의 머리통** 기준이라,
여성 헤어를 남성 베이스에 올려도 그 머리에 맞춰 들어간다.

---

## 2. 분리된 헤어

| 팩 | 원본 | 분리 결과 |
|---|---|---|
| 남성 | `assets/characters/source/male_hair/hair.fbx` | **M-01 ~ M-09** (9개) |
| 여성 | `assets/characters/source/female_hair/nv+toufa.obj` | **F-001 ~ F-105** (105개) |
| 수염 | 같은 FBX 안에 섞여 있던 것 | `MB-01` (1개, 목록에는 넣지 않았다) |

- 남성 FBX 는 노드 10개에 표면 12개였고, 그중 **2개는 같은 메시가 위치만 바꿔 두 번 들어 있던 것**이라
  번호를 주지 않았다(정점을 정렬해 비교한 결과 1mm 안에서 일치).
- 여성 OBJ 의 그룹 105개는 격자 칸이 서로 겹치지 않는다. 실측으로 "그룹 하나 = 헤어 하나" 를 확인했다.
- 번호는 팩 안에서 눈으로 찾기 쉬운 순서다 — 여성은 아래 줄부터 왼쪽→오른쪽.

### 파일

```
assets/characters/hair/
  male/M-01.res … M-09.res          분리한 메시(Godot ArrayMesh, 압축)
  female/F-001.res … F-105.res
  beard/MB-01.res
  thumbs/<번호>.png                  목록용 썸네일 192×192
  hair_catalog.json                 스튜디오가 읽는 명세
  hair_assignments.json             고른 결과 (저장을 눌러야 생긴다)
```

원본 파일은 읽기만 하고 고치지 않았다.

---

## 3. 다시 굽기

원본 팩을 바꿨거나 분리 기준을 손봤을 때만 돌리면 된다.

```
# ① 메시 분리 + 카탈로그 (헤드리스, 10초 남짓)
godot --headless --path . res://scenes/tools/HairAssetBuilder.tscn --quit-after 200000

# ② 썸네일 (창 모드여야 한다 — 헤드리스는 그림을 그리지 않는다)
godot --path . res://scenes/tools/HairThumbnailBaker.tscn

# ③ 검증
godot --headless --path . res://scenes/debug/HairStudioTest.tscn --quit-after 8000
```

`res://scenes/tools/HairSourceInspector.tscn` 은 원본 FBX 안을 표면 단위로 들여다보는 조사용이다.
`res://scenes/debug/HairStudioShot.tscn -- <폴더>` 는 스튜디오 화면을 PNG 로 떠서 눈으로 확인하는 용도다.

---

## 4. 자동 맞춤이 하는 일

헤어마다 원본 좌표계가 달라서(남성 FBX 는 100배 + Z-up, 여성 OBJ 는 센티미터 격자) 그대로는 못 쓴다.
분리할 때 전부 **미터 단위 · 원점 = AABB 중심** 으로 맞춰 두고, 스튜디오가 올릴 때 이렇게 계산한다.

1. 베이스 모델에서 **귀 위쪽 머리통** 을 실측한다. 머리통 전체 폭은 귀가 끼어 2cm 넘게 부풀어 있어서
   그대로 쓰면 머리카락이 커진다.
2. 헤어의 **정수리 뚜껑** 폭을 거기에 맞춘다. 뚜껑은 꼭대기에서 8cm 안쪽인데, 묶은 머리처럼 가느다란
   것이 솟아 있으면 건너뛰고 "충분히 넓은 첫 층" 부터 잰다. 그러지 않으면 머리끈 끝을 정수리로 알고
   머리카락 전체가 얼굴까지 내려앉는다.
3. 그 뚜껑의 꼭대기를 모델의 정수리에 4mm 띄워 올리고, 좌우·앞뒤를 머리통 중심에 맞춘다.

**어디까지나 출발점이다.** 슬라이더로 바로 고칠 수 있고, `위치 초기화` 가 이 계산으로 되돌린다.
팩이 바라보는 방향(앞/뒤)은 정점 분포로 추정하면 앞머리가 있는 헤어에서 반대로 나와서, 화면으로 확인한
값을 `HairCatalog.PackYaw` 에 적어 뒀다. 두 팩 모두 돌릴 필요가 없다. 개별 조정은 `ROTATION Y` 로 한다.

---

## 5. 저장 형식

`assets/characters/hair/hair_assignments.json`

```json
{
  "sheep": {
    "body": "female",
    "hair": "F-004",
    "hair_path": "res://assets/characters/hair/female/F-004.res",
    "color": "#684A39",
    "color_name": "갈색",
    "position": [0.001, 0.12, -0.004],
    "rotation": [0.0, 0.0, 2.0],
    "scale": [0.93, 0.93, 0.93]
  }
}
```

- 키는 **기존 게임 데이터의 직원 ID** 와 같다(`rabbit` `cat` `fox` `sheep` `wolf` `dog`). 여기에 `admin`(관리자)과
  `director`(총괄관리자)를 더해 8명이다.
- `position` / `rotation` 은 **머리 본(`Head`) 기준** 이다. 다음 단계에서 `BoneAttachment3D` 에 그대로 넣으면 된다.
- 직원의 초기 베이스 성별은 `data/employees/*.tres` 의 `Gender` 를 읽어서 정한다. 코드에 박아 두지 않았다.

---

## 6. 아직 안 한 것

- **게임 캐릭터 모델은 그대로다.** 가면·의상·색·애니메이션·게임 로직 전부 손대지 않았다.
- 관리자 셔츠(`assets/characters/source/admin_outfit/Shirt OBJ.obj`)는 **임포트되는지만 확인**했다.
  메시 표면 45개로 들어온다. `.mtl` 이 원작자 PC 의 절대 경로(`I:\3D Models For Sales\…`)를 가리켜서
  텍스처는 전부 빠진다 — 의상 작업을 할 때 텍스처를 따로 구해 경로를 맞춰야 한다.
- 베이스 모델의 눈썹 텍스처(`T_Hair_1_BaseColor.png` 등)와 눈 노멀맵(`T_Eye_Normal_png.png`)이 팩에 빠져 있다.
  스튜디오에서는 **미리보기 한정** 으로 눈썹을 머리색에 맞춰 칠한다. 원본 파일은 건드리지 않았다.
- Blender 가 이 환경에 없어서 GLB 변환 경로는 쓰지 않았다. 분리본은 Godot 네이티브 메시(`.res`)다.
  필요하면 Blender 를 설치한 뒤 같은 메시를 GLB 로 다시 뽑으면 된다.
