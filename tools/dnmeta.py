"""Minimal .NET metadata reader: dumps TypeDefs with their methods/fields and the #US string heap."""
import struct, sys, json

def rd(fmt, b, o):
    return struct.unpack_from(fmt, b, o)

def load(path):
    b = open(path, 'rb').read()
    pe = rd('<I', b, 0x3c)[0]
    nsec = rd('<H', b, pe + 6)[0]
    optsz = rd('<H', b, pe + 20)[0]
    opt = pe + 24
    magic = rd('<H', b, opt)[0]
    dd = opt + (96 if magic == 0x10b else 112)
    cli_rva, _ = rd('<II', b, dd + 14 * 8)
    secs = []
    so = opt + optsz
    for i in range(nsec):
        vs, va, rs, ra = rd('<IIII', b, so + i * 40 + 8)
        secs.append((va, max(vs, rs), ra))
    def off(rva):
        for va, sz, ra in secs:
            if va <= rva < va + sz:
                return rva - va + ra
        raise ValueError(hex(rva))
    cli = off(cli_rva)
    md_rva, md_sz = rd('<II', b, cli + 8)
    md = off(md_rva)
    vlen = rd('<I', b, md + 12)[0]
    p = md + 16 + vlen + 2
    nstreams = rd('<H', b, p)[0]; p += 2
    streams = {}
    for _ in range(nstreams):
        so_, ss = rd('<II', b, p); p += 8
        e = b.index(b'\0', p)
        name = b[p:e].decode()
        p = (e + 4) & ~3
        streams[name] = (md + so_, ss)
    return b, streams

def heap_str(b, streams, idx):
    o, _ = streams['#Strings']
    e = b.index(b'\0', o + idx)
    return b[o + idx:e].decode('utf-8', 'replace')

def user_strings(b, streams):
    o, sz = streams['#US']
    out = []
    i = 1
    while i < sz:
        x = b[o + i]
        if x & 0x80 == 0:
            ln, h = x, 1
        elif x & 0xC0 == 0x80:
            ln, h = ((x & 0x3f) << 8) | b[o + i + 1], 2
        else:
            ln, h = ((x & 0x1f) << 24) | (b[o+i+1] << 16) | (b[o+i+2] << 8) | b[o+i+3], 4
        if ln == 0:
            i += h; continue
        s = b[o + i + h:o + i + h + ln - 1].decode('utf-16le', 'replace')
        out.append(s)
        i += h + ln
    return out

def tables(b, streams):
    o, _ = streams.get('#~') or streams['#-']
    hs = b[o + 6]
    valid, = rd('<Q', b, o + 8)
    p = o + 24
    rows = {}
    for t in range(64):
        if valid >> t & 1:
            rows[t] = rd('<I', b, p)[0]; p += 4
    si = 4 if hs & 1 else 2
    gi = 4 if hs & 2 else 2
    bi = 4 if hs & 4 else 2
    def idx(t):
        return 4 if rows.get(t, 0) > 0xffff else 2
    def coded(ts, bits):
        m = max(rows.get(t, 0) for t in ts)
        return 4 if m >= (1 << (16 - bits)) else 2
    TDOR = coded([2, 1, 27], 2)
    RS = coded([0, 26, 35, 1], 2)
    HC = coded([4, 8, 23], 2)
    HCA = coded([6,4,1,2,8,9,10,0,14,23,20,17,26,27,32,35,38,39,40,42,44,43], 5)
    HFM = coded([4, 8], 1)
    HDS = coded([2, 6, 32], 2)
    MRP = coded([2, 1, 26, 6, 27], 3)
    CAT = coded([6, 10], 3)
    HSem = coded([20, 23], 1)
    MDOR = coded([6, 10], 1)
    MF = coded([4, 6], 1)
    IMPL = coded([38, 35, 39], 2)
    TOMD = coded([2, 6], 1)
    sizes = {
        0: 2 + si + gi * 3, 1: RS + si * 2, 2: 4 + si * 2 + TDOR + idx(4) + idx(6),
        3: idx(4), 4: 2 + si + bi, 5: idx(6), 6: 4 + 2 + 2 + si + bi + idx(8),
        7: idx(8), 8: 4 + si, 9: idx(2) + TDOR, 10: MRP + si + bi, 11: 2 + HC + bi,
        12: HCA + CAT + bi, 13: HFM + bi, 14: 2 + HDS + bi, 15: 2 + 4 + idx(2),
        16: 4 + idx(4), 17: bi, 18: idx(2) + idx(20), 19: idx(20), 20: 2 + si + bi,
        21: idx(2) + idx(23), 22: idx(23), 23: 2 + si + bi, 24: 2 + idx(6) + HSem,
        25: idx(2) + MDOR * 2, 26: si, 27: bi, 28: 2 + MF + si + idx(26), 29: 4 + idx(4),
        30: 8, 31: 4, 32: 4 + 2 * 4 + 4 + bi + si * 2, 33: 4, 34: 4 * 3, 35: 2 * 4 + 4 + bi * 2 + si * 2,
        36: 4 + idx(35), 37: 4 * 3 + idx(35), 38: 4 + si + bi, 39: 4 + 4 + si * 2 + IMPL,
        40: 4 + 4 + si + IMPL, 41: idx(2) * 2, 42: 2 + 2 + TOMD + si, 43: MDOR + bi, 44: idx(42) + TDOR,
    }
    starts = {}
    for t in sorted(rows):
        starts[t] = p
        p += rows[t] * sizes[t]
    def u(o_, n):
        return rd('<I' if n == 4 else '<H', b, o_)[0]
    # TypeDef
    tds = []
    o2 = starts.get(2)
    for r in range(rows.get(2, 0)):
        q = o2 + r * sizes[2]
        fl = u(q, 4); q += 4
        nm = u(q, si); q += si
        ns = u(q, si); q += si
        q += TDOR
        fld = u(q, idx(4)); q += idx(4)
        mth = u(q, idx(6))
        tds.append([heap_str(b, streams, ns), heap_str(b, streams, nm), fld, mth])
    meths = []
    o6 = starts.get(6)
    for r in range(rows.get(6, 0)):
        q = o6 + r * sizes[6] + 8
        meths.append(heap_str(b, streams, u(q, si)))
    flds = []
    o4 = starts.get(4)
    for r in range(rows.get(4, 0)):
        q = o4 + r * sizes[4] + 2
        flds.append(heap_str(b, streams, u(q, si)))
    res = []
    for i, (ns, nm, f, m) in enumerate(tds):
        f2 = tds[i + 1][2] if i + 1 < len(tds) else len(flds) + 1
        m2 = tds[i + 1][3] if i + 1 < len(tds) else len(meths) + 1
        res.append({'ns': ns, 'name': nm, 'fields': flds[f - 1:f2 - 1], 'methods': meths[m - 1:m2 - 1]})
    return res

if __name__ == '__main__':
    b, st = load(sys.argv[1])
    out = sys.argv[2]
    json.dump({'types': tables(b, st), 'us': user_strings(b, st)}, open(out, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print('ok')
