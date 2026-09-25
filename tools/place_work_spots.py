# -*- coding: utf-8 -*-
"""
작업실마다 실제 설비에 붙은 작업 자리 6개(이상)를 방 씬의 WorkSpots 에 쓴다.

    python tools/place_work_spots.py

자리마다
  · 무엇을 만지는가(target) — 손이 닿아야 하는 점. 자식 Marker3D "InteractionTarget" 으로 들어간다.
    컨트롤러가 작업 클립의 팔 길이(WorkClipReach)만큼 뒤에 직원을 세우므로 체형이 달라도 손이 그 점에 간다.
  · 어느 쪽을 보는가(yaw, 도) — 캐릭터는 자기 -Z 를 본다: 0 = -Z · 90 = -X · -90 = +X · 180 = +Z
  · 어떤 손동작인가(clip) — 버튼·다이얼·레버·상판 조작·쪼그려 수리·선반·클립보드·앉아서 작업
  · 어떤 업무 때 먼저 쓰는가(tasks) — 그 업무가 진행 중이면 우선. 비어 있어도 사람이 많으면 누구든 쓴다.
앉는 자리는 seat=(x, z, 좌판 높이) — 의자 좌판 중심에 둔다.

유지하는 것: 의무실 병상(환자용), 저장고 운반 동선(SupplyPickSpot · CartDropSpotA), 격리실.
여러 번 돌려도 된다(WorkSpots 안의 다른 자리는 지우고 다시 쓴다).
"""
import math
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
KEEP = {"MedicalBedSpot1", "MedicalBedSpot2", "SupplyPickSpot", "CartDropSpotA"}
STAND_BACK = 0.45   # 에디터에서 보기 좋게 대상 앞 이만큼에 자리 노드를 둔다(실제 설 곳은 런타임에 다시 잰다)

# 코어 콘솔은 35.5° 돌아가 있다 — 콘솔 축으로 자리를 잡는다.
_CX, _CZ = 0.35, -1.55
_AX = (0.81388, -0.58104)   # 콘솔 X 축(바닥)
_AZ = (0.58104, 0.81388)    # 콘솔 앞(+Z) 축


def _core(lx, lz, y):
    return (_CX + _AX[0] * lx + _AZ[0] * lz, y, _CZ + _AX[1] * lx + _AZ[1] * lz)


ROOMS = {
    "room_power": [
        dict(id="GenPanelSpot", clip="panel_press", target=(-2.08, 1.15, -1.39), yaw=0,
             tasks=["power_generator_check", "repair_generator"], prio=20),
        dict(id="GenDialSpot", clip="knob_turn", target=(-1.36, 1.14, -1.35), yaw=0,
             tasks=["power_generator_check", "repair_generator"], prio=19),
        dict(id="DistSwitchSpot", clip="panel_press", target=(0.55, 1.10, -2.59), yaw=0,
             tasks=["power_panel_cleaning"], prio=18),
        dict(id="DistGaugeSpot", clip="shelf_low", target=(1.25, 0.62, -2.58), yaw=0,
             tasks=["power_panel_cleaning"], prio=17),
        dict(id="Gen2HatchSpot", clip="crouch_repair", target=(1.53, 0.45, -0.25), yaw=-90,
             tasks=["repair_generator"], prio=16, prop="wrench"),
        dict(id="BreakerLeverSpot", clip="lever_operate", target=(-2.60, 1.05, -0.10), yaw=90, prio=15),
    ],
    "room_vent": [
        dict(id="AirflowConsoleSpot", clip="console_operate", target=(1.30, 0.97, -1.02), yaw=0,
             tasks=["vent_circulation_check"], prio=20),
        dict(id="AirflowGaugeSpot", clip="knob_turn", target=(1.00, 1.12, -1.30), yaw=-90,
             tasks=["vent_circulation_check"], prio=19),
        dict(id="FilterSlotSpot", clip="crouch_repair", target=(-2.15, 0.44, -1.50), yaw=0,
             tasks=["vent_filter_change", "repair_vent"], prio=18, prop="wrench"),
        dict(id="FilterLogSpot", clip="clipboard_check", target=(-1.30, 1.10, -1.50), yaw=0,
             tasks=["vent_filter_change"], prio=17, prop="clipboard"),
        dict(id="GrilleSpot", clip="panel_press", target=(-2.85, 1.00, 1.60), yaw=90, prio=16),
        dict(id="FanHousingSpot", clip="shelf_reach", target=(-2.78, 1.50, -0.35), yaw=90,
             tasks=["repair_vent"], prio=15),
    ],
    "room_maintenance": [
        dict(id="AssemblyDeskSpot", clip="sit_assemble", seat=(1.05, -1.98, 0.47), yaw=0,
             tasks=["equipment_repair", "repair_maintenance"], prio=21),
        dict(id="FabricationBenchSpot", clip="hammer_work", target=(-0.60, 0.98, -2.10), yaw=0,
             tasks=["materials_production"], prio=20),
        dict(id="BenchStoolSpot", clip="sit_assemble", seat=(-1.27, -1.63, 0.69), yaw=0,
             tasks=["materials_production"], prio=19),
        dict(id="BenchPartsSpot", clip="console_operate", target=(-2.10, 0.97, -1.95), yaw=0,
             tasks=["materials_production"], prio=18),
        dict(id="CratePartsSpot", clip="shelf_low", target=(2.15, 0.60, -1.55), yaw=-90,
             tasks=["equipment_repair"], prio=17),
        dict(id="PartsShelfSpot", clip="shelf_reach", target=(-2.50, 1.30, 0.75), yaw=90, prio=16),
    ],
    "room_medical": [
        dict(id="MedicineStationSpot", clip="console_operate", target=(1.20, 0.90, -1.74), yaw=0,
             tasks=["medicine_sorting", "repair_medical"], prio=20),
        dict(id="MedDrawerSpot", clip="shelf_low", target=(1.95, 0.55, -1.64), yaw=0,
             tasks=["medicine_sorting"], prio=19),
        dict(id="MedMonitorSpot", clip="panel_press", target=(-0.05, 1.22, -2.28), yaw=0,
             tasks=["staff_treatment", "repair_medical"], prio=18),
        dict(id="ExamChartSpot", clip="clipboard_check", target=(0.95, 1.00, 0.85), yaw=-90,
             tasks=["staff_treatment"], prio=17, prop="clipboard"),
        dict(id="IvPoleSpot", clip="knob_turn", target=(0.42, 1.20, -1.90), yaw=90,
             tasks=["staff_treatment"], prio=16),
        dict(id="PatientChartSpot", clip="clipboard_check", target=(-0.52, 1.00, -1.10), yaw=90, prio=15,
             prop="clipboard"),
    ],
    "room_guard": [
        dict(id="MainSurveillanceSeat", clip="sit_typing", seat=(-0.80, -1.32, 0.47), yaw=0,
             tasks=["surveillance_monitoring"], prio=21),
        dict(id="CctvPanelSpot", clip="console_operate", target=(0.20, 0.93, -1.92), yaw=0,
             tasks=["surveillance_monitoring", "repair_guard"], prio=20),
        dict(id="GearRackSpot", clip="shelf_low", target=(1.52, 0.60, -2.41), yaw=0,
             tasks=["lockdown_gear_check", "repair_guard"], prio=19),
        dict(id="DeskLeftSpot", clip="console_operate", target=(-1.90, 0.93, -1.92), yaw=0, prio=18),
        dict(id="SecDeskSeat", clip="sit_typing", seat=(-2.05, 0.28, 0.47), yaw=90,
             tasks=["lockdown_gear_check"], prio=17),
        dict(id="CabinetFileSpot", clip="clipboard_check", target=(-2.32, 1.05, 1.40), yaw=90, prio=16,
             prop="clipboard"),
    ],
    "room_core": [
        dict(id="CoreRepairSpot", clip="crouch_repair", target=(-2.07, 0.45, 0.25), yaw=-90,
             tasks=["core_direct_repair", "repair_core"], prio=21, prop="wrench"),
        dict(id="CoreManagementSpot", clip="console_operate", target=_core(-0.38, 0.16, 1.02), yaw=35.5,
             tasks=["core_log_review"], prio=20),
        dict(id="CoreRepairPanelSpot", clip="panel_press", target=(-2.45, 1.00, 0.52), yaw=0,
             tasks=["core_direct_repair", "repair_core"], prio=20),
        dict(id="CoreKnobSpot", clip="knob_turn", target=_core(0.38, 0.10, 1.14), yaw=35.5,
             tasks=["core_log_review"], prio=19),
        dict(id="CoreGaugeSpot", clip="shelf_reach", target=(1.15, 1.55, -2.84), yaw=0, prio=17),
        dict(id="CoreReadingSpot", clip="clipboard_check",
             target=(0.25 - 0.29 * math.sin(math.radians(45)), 1.10, 0.25 - 0.29 * math.cos(math.radians(45))),
             yaw=45, prio=16, prop="clipboard"),
    ],
    "room_storage": [
        dict(id="ShelfRepairSpot", clip="crouch_repair", target=(-0.45, 0.45, -2.02), yaw=0,
             tasks=["repair_storage"], prio=20, prop="wrench"),
        dict(id="RackLowSpot", clip="shelf_low", target=(-2.05, 0.60, -2.02), yaw=0,
             tasks=["inventory_sorting"], prio=18),
        dict(id="RackHighSpot", clip="shelf_reach", target=(-1.30, 1.52, -2.02), yaw=0,
             tasks=["inventory_sorting"], prio=17),
        dict(id="StockCountSpot", clip="clipboard_check", target=(1.90, 1.00, -1.34), yaw=0,
             tasks=["inventory_sorting"], prio=16, prop="clipboard"),
        dict(id="FloorBoxSpot", clip="shelf_low", target=(1.30, 0.55, -0.70), yaw=-90, prio=15),
        dict(id="WallShelfSpot", clip="shelf_reach", target=(0.75, 1.40, -2.49), yaw=0, prio=14),
    ],
}


def f(v):
    s = f"{v:.4f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def basis(yaw_deg):
    y = math.radians(yaw_deg)
    c, s = math.cos(y), math.sin(y)
    # Ry: [c 0 s; 0 1 0; -s 0 c]  → Transform3D 는 행 순서로 적는다.
    return [c, 0, s, 0, 1, 0, -s, 0, c]


def node_block(spec, script_id):
    yaw = spec["yaw"]
    b = basis(yaw)
    lines = []
    if "seat" in spec:
        x, z, h = spec["seat"]
        pos = (x, 0.0, z)
    else:
        tx, ty, tz = spec["target"]
        fwd = (-math.sin(math.radians(yaw)), -math.cos(math.radians(yaw)))
        pos = (tx - fwd[0] * STAND_BACK, 0.0, tz - fwd[1] * STAND_BACK)
    tr = ", ".join(f(v) for v in b) + ", " + ", ".join(f(v) for v in pos)
    lines.append(f'[node name="{spec["id"]}" type="Node3D" parent="WorkSpots"]')
    lines.append(f"transform = Transform3D({tr})")
    lines.append(f'script = ExtResource("{script_id}")')
    lines.append(f'SpotId = "{spec["id"]}"')
    if spec.get("tasks"):
        lines.append("SupportedTaskIds = PackedStringArray(" + ", ".join(f'"{t}"' for t in spec["tasks"]) + ")")
    lines.append(f'AnimationName = "{spec["clip"]}"')
    lines.append(f"Priority = {spec['prio']}")
    if "seat" in spec:
        lines.append("IsSeated = true")
        lines.append(f"SeatHeight = {f(spec['seat'][2])}")
    if spec.get("prop"):
        lines.append(f'HandProp = "{spec["prop"]}"')
    out = "\n".join(lines) + "\n"
    if "target" in spec:
        # 대상 점을 자리 노드 기준 좌표로(회전은 yaw 뿐).
        tx, ty, tz = spec["target"]
        dx, dz = tx - pos[0], tz - pos[2]
        y = math.radians(yaw)
        lx = math.cos(y) * dx - math.sin(y) * dz
        lz = math.sin(y) * dx + math.cos(y) * dz
        out += (f'\n[node name="InteractionTarget" type="Marker3D" parent="WorkSpots/{spec["id"]}"]\n'
                f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {f(lx)}, {f(ty)}, {f(lz)})\n")
    return out


def rewrite(room, specs):
    path = os.path.join(ROOT, "scenes", "rooms", room + ".tscn")
    src = open(path, encoding="utf-8").read()
    script_id = re.search(r'path="res://scripts/view/RoomWorkSpot.cs" id="([^"]+)"', src).group(1)
    blocks = re.split(r"(?=\n\[node )", src)
    kept, removed = [], []
    for b in blocks:
        m = re.match(r'\n\[node name="([^"]+)"[^\]]*parent="(WorkSpots(?:/[^"]*)?)"', b)
        if m:
            name, parent = m.group(1), m.group(2)
            owner = parent.split("/")[1] if "/" in parent else name
            if owner not in KEEP:
                removed.append(name if parent == "WorkSpots" else f"{owner}/{name}")
                continue
        kept.append(b)
    src = "".join(kept)
    # WorkSpots 노드 바로 뒤(다음 노드 전)에 새 자리들을 넣는다.
    m = re.search(r'\[node name="WorkSpots"[^\]]*\]\n', src)
    ins = m.end()
    nxt = src.find("\n[node ", ins)
    nxt = len(src) if nxt < 0 else nxt
    new = "\n" + "\n".join(node_block(s, script_id) for s in specs)
    src = src[:nxt].rstrip("\n") + "\n" + new + src[nxt:]
    open(path, "w", encoding="utf-8", newline="\n").write(src)
    print(f"{room}: -{len([r for r in removed if '/' not in r])} +{len(specs)}  ({', '.join(s['id'] for s in specs)})")


if __name__ == "__main__":
    for room, specs in ROOMS.items():
        rewrite(room, specs)
