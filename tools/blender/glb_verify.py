import struct, json, sys, os


def load(path):
    d = open(path, 'rb').read()
    assert d[:4] == b'glTF', 'not a glb'
    off = 12
    js = None
    bins = None
    while off < len(d):
        ln, ty = struct.unpack_from('<II', d, off)
        chunk = d[off+8:off+8+ln]
        if ty == 0x4E4F534A:
            js = json.loads(chunk.decode('utf-8'))
        elif ty == 0x004E4942:
            bins = chunk
        off += 8 + ln + ((-ln) % 4)
    return js, bins


def acc_minmax(g, i):
    a = g['accessors'][i]
    return a.get('min'), a.get('max'), a.get('count')


def world_matrix(g, ni, cache):
    # returns 4x4 as nested list
    import itertools
    parent = cache['parent']
    def local(n):
        nd = g['nodes'][n]
        if 'matrix' in nd:
            m = nd['matrix']
            return [[m[0], m[4], m[8], m[12]], [m[1], m[5], m[9], m[13]],
                    [m[2], m[6], m[10], m[14]], [m[3], m[7], m[11], m[15]]]
        t = nd.get('translation', [0, 0, 0])
        r = nd.get('rotation', [0, 0, 0, 1])
        s = nd.get('scale', [1, 1, 1])
        x, y, z, w = r
        R = [[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
             [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
             [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]]
        M = [[R[i][j]*s[j] for j in range(3)] + [t[i]] for i in range(3)] + [[0, 0, 0, 1]]
        return M
    def mul(A, B):
        return [[sum(A[i][k]*B[k][j] for k in range(4)) for j in range(4)] for i in range(4)]
    M = [[1 if i == j else 0 for j in range(4)] for i in range(4)]
    chain = []
    n = ni
    while n is not None:
        chain.append(n)
        n = parent.get(n)
    for n in reversed(chain):
        M = mul(M, local(n))
    return M


def report(path):
    g, b = load(path)
    out = {'file': os.path.basename(path), 'bytes': os.path.getsize(path)}
    parent = {}
    for i, nd in enumerate(g['nodes']):
        for c in nd.get('children', []):
            parent[c] = i
    cache = {'parent': parent}
    sc = g['scenes'][g.get('scene', 0)]
    out['scene_roots'] = [g['nodes'][i].get('name') for i in sc['nodes']]

    def tree(i, depth, lines, maxd=3):
        nd = g['nodes'][i]
        tag = []
        if 'mesh' in nd:
            tag.append('mesh=' + str(g['meshes'][nd['mesh']].get('name')))
        if 'skin' in nd:
            tag.append('skin=' + str(nd['skin']))
        lines.append('  '*depth + str(nd.get('name')) + (' [' + ','.join(tag) + ']' if tag else ''))
        if depth < maxd:
            for c in nd.get('children', []):
                tree(c, depth+1, lines, maxd)
        elif nd.get('children'):
            lines.append('  '*(depth+1) + '... %d more' % len(nd['children']))
    lines = []
    for i in sc['nodes']:
        tree(i, 0, lines)
    out['hierarchy'] = lines
    out['node_count'] = len(g['nodes'])
    out['skins'] = [{'name': s.get('name'), 'joints': len(s['joints']),
                     'skeleton': g['nodes'][s['skeleton']].get('name') if 'skeleton' in s else None}
                    for s in g.get('skins', [])]
    out['materials'] = [m.get('name') for m in g.get('materials', [])]
    out['images'] = len(g.get('images', []))
    out['textures'] = len(g.get('textures', []))
    ms = []
    for m in g.get('meshes', []):
        prims = []
        for p in m['primitives']:
            mi = p.get('material')
            mn, mx, cnt = acc_minmax(g, p['attributes']['POSITION'])
            prims.append({'material': g['materials'][mi].get('name') if mi is not None else None,
                          'verts': cnt, 'pos_min': [round(x, 4) for x in mn],
                          'pos_max': [round(x, 4) for x in mx],
                          'has_joints': 'JOINTS_0' in p['attributes']})
        ms.append({'name': m.get('name'), 'primitives': prims})
    out['meshes'] = ms

    # world bbox of skinned meshes (using node world matrices; for skinned meshes
    # gltf ignores node transform, so also give skeleton-space estimate)
    gmn = [1e18]*3
    gmx = [-1e18]*3
    for i, nd in enumerate(g['nodes']):
        if 'mesh' not in nd:
            continue
        M = world_matrix(g, i, cache)
        if 'skin' in nd:
            # glTF: the skinned mesh node transform is ignored; vertices are placed by
            # joint_global * inverseBindMatrix.  At rest that product is identity when the
            # exporter is consistent, so world bounds == POSITION bounds.  Verify joint 0.
            sk = g['skins'][nd['skin']]
            acc = g['accessors'][sk['inverseBindMatrices']]
            bv = g['bufferViews'][acc['bufferView']]
            o0 = bv.get('byteOffset', 0) + acc.get('byteOffset', 0)
            ibm = struct.unpack_from('<16f', b, o0)
            IB = [[ibm[c*4+r] for c in range(4)] for r in range(4)]
            G = world_matrix(g, sk['joints'][0], cache)
            P = [[sum(G[r][k]*IB[k][c] for k in range(4)) for c in range(4)] for r in range(4)]
            err = max(abs(P[r][c] - (1.0 if r == c else 0.0)) for r in range(4) for c in range(4))
            out['rest_bind_identity_err'] = round(err, 6)
            M = [[1 if r == c else 0 for c in range(4)] for r in range(4)]
        for p in g['meshes'][nd['mesh']]['primitives']:
            mn, mx, _ = acc_minmax(g, p['attributes']['POSITION'])
            for xi in (mn[0], mx[0]):
                for yi in (mn[1], mx[1]):
                    for zi in (mn[2], mx[2]):
                        v = [xi, yi, zi, 1]
                        w = [sum(M[r][c]*v[c] for c in range(4)) for r in range(3)]
                        for k in range(3):
                            gmn[k] = min(gmn[k], w[k])
                            gmx[k] = max(gmx[k], w[k])
    out['world_bbox_min'] = [round(x, 4) for x in gmn]
    out['world_bbox_max'] = [round(x, 4) for x in gmx]
    out['world_size_xyz'] = [round(gmx[i]-gmn[i], 4) for i in range(3)]
    out['height_m'] = round(gmx[1]-gmn[1], 4)   # gltf Y up

    anims = []
    for a in g.get('animations', []):
        dur = 0.0
        tmin = 1e18
        paths = {}
        for ch in a['channels']:
            s = a['samplers'][ch['sampler']]
            mn, mx, cnt = acc_minmax(g, s['input'])
            if mx:
                dur = max(dur, mx[0])
            if mn:
                tmin = min(tmin, mn[0])
            paths[ch['target']['path']] = paths.get(ch['target']['path'], 0) + 1
        anims.append({'name': a.get('name'), 'channels': len(a['channels']),
                      't0': round(tmin, 4), 'dur_s': round(dur, 4), 'paths': paths})
    out['animations'] = anims
    return out


if __name__ == '__main__':
    res = [report(p) for p in sys.argv[1:]]
    print(json.dumps(res, indent=1))
