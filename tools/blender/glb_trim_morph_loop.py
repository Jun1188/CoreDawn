"""모프 루프 클립 다듬기 — 벨트 glb 의 weights 애니메이션에서 (1) 첫 키가 기본형(가중치 전부 0)이고 둘째 키(타깃 0)가 기본형과 같은 자세면
첫 키를 버리고, (2) 마지막 키가 타깃 0 으로 되돌아가는 "닫는 키"면 그것도 버린다. 남는 것은 고유 자세 N개가 한 프레임씩 이어진 클립이고,
Unity 의 WrapMode.Loop 는 마지막 자세에서 첫 자세로 즉시 되돌아간다(선형 보간 없음).

왜: Blender 액션의 첫 프레임(아무 셰이프키도 안 켜진 기본형)과 마지막 닫는 키가 그대로 샘플링되면, 기본형→타깃0 사이 41ms 멈칫과
타깃N→타깃0 사이 41ms 선형 보간(스트립이 오그라듦)이 생긴다(2026-09-04 사용자 보고). 되돌기는 보간이 아니라 스냅이어야 한다.

방법: JSON 청크의 accessor 만 고친다 — input(시각)·output(가중치) accessor 의 byteOffset/count 를 옮기고, 시각은 BIN 에서 첫 시각만큼
빼서 0부터 시작하게 다시 쓴다(같은 자리, 같은 길이). 다른 바이트는 그대로.

사용: python tools/blender/glb_trim_morph_loop.py belt belt_curve_l belt_curve_r   (팩 models/ 아래 이름)
"""
import struct, json, sys, os

MODELS = 'Assets/StreamingAssets/packs/coredawn/models'


def patch(path):
    f = open(path, 'rb').read()
    assert f[:4] == b'glTF', path
    ln = struct.unpack('<I', f[12:16])[0]
    j = json.loads(f[20:20 + ln])
    off = 20 + ln
    bl = struct.unpack('<I', f[off:off + 4])[0]
    assert f[off + 4:off + 8] == b'BIN\0'
    bin_start = off + 8
    binc = bytearray(f[bin_start:bin_start + bl])

    def acc_range(a):
        bv = j['bufferViews'][a['bufferView']]
        assert 'byteStride' not in bv, 'strided animation buffer view'
        return bv.get('byteOffset', 0) + a.get('byteOffset', 0)

    changed = []
    for anim in j.get('animations', []):
        for ch in anim['channels']:
            if ch['target']['path'] != 'weights':
                continue
            s = anim['samplers'][ch['sampler']]
            ain, aout = j['accessors'][s['input']], j['accessors'][s['output']]
            keys = ain['count']
            ntargets = aout['count'] // keys
            tin_off, wout_off = acc_range(ain), acc_range(aout)
            times = list(struct.unpack_from('<%df' % keys, binc, tin_off))
            weights = [list(struct.unpack_from('<%df' % ntargets, binc, wout_off + k * ntargets * 4)) for k in range(keys)]

            def state(k):   # 어느 타깃이 켜져 있나: (index, weight) 목록, 전부 0이면 []
                return [(i, w) for i, w in enumerate(weights[k]) if abs(w) > 1e-6]

            drop_first = state(0) == [] and state(1) == [(0, 1.0)]   # 기본형 키 + 타깃0(=기본형 자세) 키가 나란히
            drop_last = keys >= 3 and state(keys - 1) == [(0, 1.0)] and state(keys - 2) == [(ntargets - 1, 1.0)]   # 닫는 키
            if not drop_first and not drop_last:
                changed.append(f"{anim['name']}: 손댈 것 없음 (keys={keys})")
                continue
            first = 1 if drop_first else 0
            last = keys - 1 if drop_last else keys   # exclusive
            new_keys = last - first
            t0 = times[first]
            # 시각을 0부터 다시 쓴다 — 같은 자리에 같은 개수
            for k in range(first, last):
                struct.pack_into('<f', binc, tin_off + k * 4, times[k] - t0)
            ain['byteOffset'] = ain.get('byteOffset', 0) + first * 4
            ain['count'] = new_keys
            ain['min'] = [0.0]
            ain['max'] = [times[last - 1] - t0]
            aout['byteOffset'] = aout.get('byteOffset', 0) + first * ntargets * 4
            aout['count'] = new_keys * ntargets
            changed.append(f"{anim['name']}: keys {keys} → {new_keys} (첫 기본형 키 {'삭제' if drop_first else '유지'}, 닫는 키 {'삭제' if drop_last else '없음'}), 길이 {times[-1]:.4f}s → {ain['max'][0]:.4f}s")

    js = json.dumps(j, separators=(',', ':')).encode('utf-8')
    while len(js) % 4:
        js += b' '
    out = b'glTF' + struct.pack('<II', 2, 12 + 8 + len(js) + 8 + len(binc)) + struct.pack('<I', len(js)) + b'JSON' + js + struct.pack('<I', len(binc)) + b'BIN\0' + bytes(binc)
    open(path, 'wb').write(out)
    for c in changed:
        print(os.path.basename(path), c)


if __name__ == '__main__':
    for k in sys.argv[1:]:
        patch(os.path.join(MODELS, k + '.glb'))
