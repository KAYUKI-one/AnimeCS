"""Reduce weighted PMX hair and cloth chains to a bounded deform palette for real-time skinning."""
import re
from collections import defaultdict

# Source conventions are import metadata. The exported bone names are engine independent.
HAIR = re.compile(r"hair|^左右?后\d|^[左右]后\d|^前\d|^H\d*_\d|前髪|側髪|邊髪|劉海|刘海|馬尾|马尾|辫子|^Tail_|^toujin_|^后带子_", re.I)
CLOTH = re.compile(r"スカート|skirt|裙|dress|^q_\d|^下摆|^下擺|^belt_[a-e]_|^cloak_|^衣領|^外套|^后带|^cloth_[mn]_|^piaodai", re.I)


def add_secondary(body, armature, points, basis, humanoid_target, tailor):
    masses = defaultdict(float)
    groups = list(body.vertex_groups)
    for vertex in body.data.vertices:
        for assignment in vertex.groups:
            masses[groups[assignment.group].name] += assignment.weight
    buckets = defaultdict(dict)
    for bone in armature.data.bones:
        name = bone.name
        if masses[name] < .05:
            continue
        source = re.sub(r"^[左右]", "", name)
        kind = "Hair" if HAIR.search(source) or HAIR.search(name) else "Cloth" if CLOTH.search(source) else None
        if not kind or "veil" in source.lower():
            continue
        anchor = humanoid_target(name)
        if anchor not in points:
            continue
        head, tail = basis @ bone.head_local, basis @ bone.tail_local
        if kind == "Cloth":
            head, tail = tailor(head), tailor(tail)
        buckets[kind, anchor][name] = (bone, head, tail)

    mapped = {}
    for (kind, anchor), nodes in buckets.items():
        # Traverse original connections, bridging unweighted MMD helper joints.
        parents = {}
        for name, (bone, _, _) in nodes.items():
            parent = bone.parent
            while parent and parent.name not in nodes:
                parent = parent.parent
            parents[name] = parent.name if parent else None
        paths = []
        for tip in sorted(set(nodes) - set(parents.values())):
            path = [tip]
            while parents[path[-1]]:
                path.append(parents[path[-1]])
            path.reverse()
            coords = [nodes[n][1] for n in path] + [nodes[tip][2]]
            lengths = [0.]
            for a, b in zip(coords, coords[1:]):
                lengths.append(lengths[-1] + (b-a).length)
            if lengths[-1] < .018:
                continue
            def sample(t):
                distance = lengths[-1] * t
                for i, high in enumerate(lengths[1:]):
                    if high >= distance:
                        return coords[i].lerp(coords[i+1], (distance-lengths[i])/max(high-lengths[i], .00001))
                return coords[-1]
            paths.append((path, [sample(t) for t in (0, 1/3, 2/3, 1)], sum(masses[n] for n in path), lengths[-1]))
        if not paths:
            continue
        def distance(a, b):
            return sum((v-w).length_squared for v,w in zip(a[1], b[1]))
        chosen = [max(paths, key=lambda p: p[2])]
        while len(chosen) < min(len(paths), 7 if kind == "Hair" else 9):
            far = max(paths, key=lambda p: min(distance(p,c) for c in chosen))
            if min(distance(far,c) for c in chosen) < .004:
                break
            chosen.append(far)
        segments = []
        for index, (_, coords, _, length) in enumerate(chosen):
            prefix = f"Spring{kind}_{anchor}_{index:02d}"
            parent = anchor
            if length < .075:
                coords = [coords[0],coords[-1]]
            for i, (head,tail) in enumerate(zip(coords,coords[1:])):
                name = f"{prefix}_{i}"
                points[name] = (parent,head,tail)
                segments.append((name, (head+tail)*.5))
                parent = name
            tip = coords[-1]
            points[prefix+"_End"] = (parent,tip,tip+(tip-coords[-2]).normalized()*.008)
        for name, (_, head, tail) in nodes.items():
            center = (head+tail)*.5
            mapped[name] = min(segments,key=lambda s:(s[1]-center).length_squared)[0]
    return mapped
