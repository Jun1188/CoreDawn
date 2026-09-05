"""Restore the armature object's 0.21 scale on core.glb.

The Blender glTF exporter normalizes the armature object transform: the
SpaceShip_Rig node came out with no transform at all and the (constant 1.0)
scale channel of SpaceShip_Landing would additionally pin it to 1.0.
This rewrites the GLB JSON chunk so that:
  * node "SpaceShip_Rig" carries scale [0.21, 0.21, 0.21]
  * the constant-1.0 scale channel targeting that node is removed
Geometry, skins and every other channel are untouched (BIN chunk copied
byte for byte).
"""
import json
import struct
import sys

SCALE = 0.20999999344348907
TARGET = 'SpaceShip_Rig'


def main(path):
    with open(path, 'rb') as f:
        data = f.read()
    magic, ver, length = struct.unpack_from('<4sII', data, 0)
    assert magic == b'glTF' and length == len(data)
    off = 12
    chunks = []
    while off < length:
        clen, ctype = struct.unpack_from('<I4s', data, off)
        off += 8
        chunks.append((ctype, data[off:off + clen]))
        off += clen
    js = None
    binc = b''
    for ctype, payload in chunks:
        if ctype == b'JSON':
            js = json.loads(payload.decode('utf-8'))
        elif ctype == b'BIN\x00':
            binc = payload
    idx = [i for i, n in enumerate(js['nodes']) if n.get('name') == TARGET]
    assert len(idx) == 1, idx
    ni = idx[0]
    js['nodes'][ni]['scale'] = [SCALE, SCALE, SCALE]
    print('set scale on node %d (%s)' % (ni, TARGET))

    for a in js.get('animations', []):
        removed = [c for c in a['channels']
                   if c['target'].get('node') == ni and c['target']['path'] == 'scale']
        if not removed:
            continue
        a['channels'] = [c for c in a['channels'] if c not in removed]
        used = sorted({c['sampler'] for c in a['channels']})
        remap = {old: new for new, old in enumerate(used)}
        a['samplers'] = [a['samplers'][i] for i in used]
        for c in a['channels']:
            c['sampler'] = remap[c['sampler']]
        print('%s: dropped %d constant scale channel(s) on %s -> %d channels / %d samplers'
              % (a['name'], len(removed), TARGET, len(a['channels']), len(a['samplers'])))

    jsonb = json.dumps(js, separators=(',', ':')).encode('utf-8')
    jsonb += b' ' * ((4 - len(jsonb) % 4) % 4)
    binb = binc + b'\x00' * ((4 - len(binc) % 4) % 4)
    total = 12 + 8 + len(jsonb) + (8 + len(binb) if binb else 0)
    out = bytearray()
    out += struct.pack('<4sII', b'glTF', 2, total)
    out += struct.pack('<I4s', len(jsonb), b'JSON') + jsonb
    if binb:
        out += struct.pack('<I4s', len(binb), b'BIN\x00') + binb
    assert len(out) == total
    with open(path, 'wb') as f:
        f.write(out)
    print('rewrote %s (%d bytes)' % (path, total))


if __name__ == '__main__':
    main(sys.argv[1])
