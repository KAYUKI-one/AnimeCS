"""Bake two local, pose-driven cloth clearances against the actual skinned legs."""
import math
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree
from motion import pose


def mark_surfaces(body, points, groups, fabric, skip=()):
    # Classify from the source garment groups before their weights are folded onto the game rig.
    mesh = body.data
    hip = (points["LeftUpperLeg"][1].z + points["RightUpperLeg"][1].z) * .5
    cloth = np.zeros(len(mesh.vertices))
    leg = np.zeros_like(cloth)
    for v in mesh.vertices:
        if v.index in skip: continue
        for g in v.groups:
            if fabric[g.group]:
                cloth[v.index] += g.weight
            elif groups[g.group] in ("LeftUpperLeg", "LeftLowerLeg", "RightUpperLeg", "RightLowerLeg"):
                leg[v.index] += g.weight
    flag = mesh.attributes.new("fit_surface", "INT", "FACE")
    for p in mesh.polygons:
        if .17 < p.center.z < hip + .005:
            ids = list(p.vertices)
            flag.data[p.index].value = 1 if cloth[ids].mean() > .26 else 2 if leg[ids].mean() > .66 else 0
    mask = mesh.attributes.new("fit_weight", "FLOAT", "POINT")
    mask.data.foreach_set("value", cloth.astype(np.float32))


def positions(mesh):
    coords = np.empty(len(mesh.vertices) * 3, np.float32)
    mesh.vertices.foreach_get("co", coords)
    return coords.reshape(-1, 3)


def normalized(v):
    return v / max(1e-8, np.linalg.norm(v))


def bake_clearance(label, body, rig):
    mesh = body.data
    flags = mesh.attributes["fit_surface"]
    polys = [tuple(p.vertices) for p in mesh.polygons]
    panels = [p for p, f in zip(polys, flags.data) if f.value == 1]
    legs = [p for p, f in zip(polys, flags.data) if f.value == 2]
    cloth_weight = np.empty(len(mesh.vertices), np.float32)
    mesh.attributes["fit_weight"].data.foreach_get("value", cloth_weight)
    mesh.attributes.remove(flags)
    mesh.attributes.remove(mesh.attributes["fit_weight"])
    if not panels or not legs:
        return
    rest = positions(mesh)
    # Work on a compact index set. Coincident seam vertices remain distinct at export.
    indices = np.unique([i for p in panels + legs for i in p] + list(np.flatnonzero(cloth_weight > .08)))
    to_local = np.full(len(mesh.vertices), -1, int)
    to_local[indices] = np.arange(len(indices))
    panels = [tuple(to_local[list(p)]) for p in panels]
    legs = [tuple(to_local[list(p)]) for p in legs]
    original = rest[indices].astype(float)
    weights = cloth_weight[indices]
    cloth_ids = np.flatnonzero(weights > .08)
    tree = KDTree(len(cloth_ids))
    for i in cloth_ids:
        tree.insert(original[i], int(i))
    tree.balance()
    panel_set = [set(p) for p in panels]

    def pairs(shell, limbs):
        return {(c, l) for c, l in shell.overlap(limbs) if panel_set[c].isdisjoint(legs[l])}

    def bvh(vertices, faces):
        return BVHTree.FromPolygons(vertices.tolist(), faces, all_triangles=False)

    rest_limb = bvh(original, legs)
    at_bind = pairs(bvh(original, panels), rest_limb)
    # Loose hems near a bent knee follow the closest point on the original leg, including the
    # original clearance. Use a smooth falloff across wide pleats and preserve the waistband.
    bindings = []
    if label in ("Cantarella", "BlackMiku", "Mornye"):
        for vi in cloth_ids:
            _, _, li, distance = rest_limb.find_nearest(Vector(original[vi]))
            if li is None or distance > .17:
                continue
            tri = legs[li][:3]
            a, b, c = original[list(tri)]
            normal = normalized(np.cross(b-a, c-a))
            basis = np.column_stack((b-a, c-a, normal))
            if abs(np.linalg.det(basis)) < 1e-9: continue
            abc = np.linalg.solve(basis, original[vi]-a)
            falloff = np.clip((.17-distance)/.09, 0., 1.)
            bindings.append((vi, list(tri), abc, falloff*falloff*(3.-2.*falloff)*min(1.,2.*weights[vi])))
    bones = list(rig.pose.bones)
    group_bone = {g.index: next(i for i, b in enumerate(bones) if b.name == g.name) for g in body.vertex_groups}
    ids = np.zeros((len(indices), 4), int)
    blend = np.zeros((len(indices), 4), float)
    for i, source in enumerate(indices):
        for j, g in enumerate(mesh.vertices[int(source)].groups):
            ids[i, j], blend[i, j] = group_bone[g.group], g.weight

    def sample(action, phase):
        pose(rig, action, phase, garment=label)
        bpy.context.view_layer.update()
        base = positions(body.evaluated_get(bpy.context.evaluated_depsgraph_get()).data)[indices].astype(float)
        matrices = np.array([np.asarray((b.matrix @ b.bone.matrix_local.inverted()).to_3x3()) for b in bones])
        transforms = (matrices[ids] * blend[:, :, None, None]).sum(axis=1)
        return base, transforms, np.linalg.pinv(transforms), bvh(base, legs)

    body.shape_key_add(name="Basis")
    for action, key_name in (("Idle", "DrapeStand"), ("DuckIdle", "DrapeCrouch")):
        samples = [sample(action, phase) for phase in (0., .125, .25, .375, .5, .625, .75, .875)]
        delta = np.zeros_like(original)
        if action == "DuckIdle" and bindings:
            base, _, inv, _ = samples[0]
            for vi, tri, abc, influence in bindings:
                a, b, c = base[tri]
                target = a + (b-a)*abc[0] + (c-a)*abc[1] + normalized(np.cross(b-a,c-a))*abc[2]
                d = inv[vi] @ (target-base[vi])
                delta[vi] = d * min(1., .16/max(1e-8, np.linalg.norm(d))) * influence
            # A long back train hangs behind the hips when the thighs swing forwards.
            # Keep the weighted hem above the ground as the hip drops, instead of rotating it through the calves.
            h = rig.data.bones["Hips"].head_local
            hip = rig.pose.bones["Hips"].head
            rest_height = np.clip(original[:,2] / max(h.z, .1), 0., 1.)
            target = original.copy()
            target[:,0] += hip.x-h.x
            target[:,1] += hip.y-h.y + .07*(1.-rest_height)
            target[:,2] += (hip.z-h.z)*rest_height
            if label == "Cantarella":
                # Its closed underskirt hangs between both knees. Keep its hem facing the floor
                # and open the front over the knees instead of rotating the entire hem with a thigh.
                front = np.clip(-original[:,1]/.12,0.,1.)
                target[:,1] -= .23*front*np.sin(np.pi*np.clip(rest_height,0.,1.))
                # A continuous front sheet spans the two knees, including the gap between them.
                limb_points = base[np.unique([v for face in legs for v in face])]
                levels = np.arange(limb_points[:,2].min(),limb_points[:,2].max()+.031,.018)
                leading = []
                for z in levels:
                    stripe = limb_points[np.abs(limb_points[:,2]-z)<.033,1]
                    leading.append(stripe.min()-.042 if len(stripe) else leading[-1])
                edge = np.interp(target[:,2],levels,leading)
                neutral_front = hip.y-h.y + .07*(1.-rest_height) - .02 - .23*np.sin(np.pi*rest_height)
                # Offset a whole depth layer together; clamping individual vertices to the same
                # knee surface would collapse the lining and the embroidered outer layer into each other.
                target[:,1] -= np.clip((.13-original[:,1])/.16,0.,1.)*np.maximum(0.,neutral_front-edge)
                target[:,0] += .055*np.tanh(original[:,0]/.09)*np.sin(np.pi*rest_height)
                rear = np.ones(len(original))
            else:
                rear = np.clip((original[:,1]-.055)/.115, 0., 1.)
            rear *= rear*(3.-2.*rear)*np.minimum(1.,weights*1.8)
            rear_delta = np.einsum("nij,nj->ni", inv, target-base)
            rear *= np.clip((h.z+.08-original[:,2])/.14,0.,1.)
            limit = .30 + .25*np.clip(-original[:,1]/.085,0.,1.) if label == 'Cantarella' else .30
            rear_delta *= np.minimum(1., limit / np.maximum(np.linalg.norm(rear_delta,axis=1), 1e-8))[:,None]
            delta = delta*(1.-rear[:,None])+rear_delta*rear[:,None]
        draped = delta.copy()
        best_delta, best_score, first_score = delta.copy(), math.inf, None
        for iteration in range(48):
            accumulated = np.zeros_like(delta)
            influence = np.zeros(len(delta))
            score = 0
            for base, matrix, inverse, limbs in samples:
                coords = base + np.einsum("nij,nj->ni", matrix, delta)
                crossings = pairs(bvh(coords, panels), limbs) - at_bind
                score += len(crossings)
                # Each colliding cloth face contributes once even when the leg skin is densely tessellated.
                touched = sorted({c for c, _ in crossings})
                for face_id in touched:
                    face = list(panels[face_id])
                    centre = coords[face].mean(axis=0)
                    rest_centre = original[face].mean(axis=0)
                    # Follow the outside of the panel through the bent thigh, preserving front/back/slit orientation.
                    outward = np.array([rest_centre[0], rest_centre[1], .0])
                    if abs(outward[1]) > .022:
                        outward[0] *= .40
                    direction = normalized(matrix[face].mean(axis=0) @ normalized(outward))
                    if label == "Cantarella" and action == "DuckIdle" and rest_centre[1] < -.045:
                        direction = normalized(direction*.17 + np.array([rest_centre[0]*1.5,-1.,0.]))
                    if bindings and action == "DuckIdle" and rest_centre[1] > .11:
                        direction = normalized(direction*.25 + np.array([rest_centre[0]*1.2,1.,0.]))
                    if np.linalg.norm(direction) < .8:
                        continue
                    def distance_out(d):
                        hit, _, _, length = limbs.ray_cast(Vector(centre + d * .32), Vector(-d), .35)
                        return .007 if hit is None else max(.006, .32 - length + .006)
                    clearance = distance_out(direction)
                    if action == "DuckIdle" and label in ("BlackMiku","Mornye","Cantarella"):
                        _, surface_normal, _, _ = limbs.find_nearest(Vector(centre))
                        if surface_normal is not None:
                            normal = np.asarray(surface_normal)
                            if np.dot(normal, direction) < -.12:
                                normal = -normal
                            for d in (normalized(direction + normal), normalized(normal + .20*direction)):
                                trial = distance_out(d)
                                if trial < clearance * .83:
                                    direction, clearance = d, trial
                    step = inverse[face].mean(axis=0) @ (direction * min(.030, clearance))
                    # A soft neighbourhood keeps duplicate points and neighbouring fabric layers together.
                    radius = .046 if action == "Idle" else .062
                    for _, index, distance in tree.find_range(Vector(rest_centre), radius):
                        w = (1.0 - (distance / radius) ** 2) ** 2
                        accumulated[index] += step * w
                        influence[index] += w
            if first_score is None:
                first_score = score
            if score < best_score:
                best_delta, best_score = delta.copy(), score
            if score == 0 or iteration == 47:
                break
            active = influence > 0.
            delta[active] += accumulated[active] / np.maximum(1.6, influence[active, None]) * np.minimum(1., weights[active, None] * 2.)
            extra = delta - draped
            lengths = np.linalg.norm(extra, axis=1)
            delta = draped + extra * np.minimum(1., (.066 if action == "Idle" else .145) / np.maximum(lengths, 1e-8))[:, None]
        if best_score and action == "DuckIdle":
            from collections import Counter
            base, matrix, _, limbs = samples[0]
            settled = base + np.einsum("nij,nj->ni", matrix, best_delta)
            remainder = pairs(bvh(settled, panels), limbs) - at_bind
            by_weight = Counter(tuple(round(weights[v],1) for v in panels[c]) for c,l in remainder)
            by_position = Counter((round(original[list(panels[c])].mean(axis=0)[2],1),
                                   round(original[list(panels[c])].mean(axis=0)[1],1)) for c,l in remainder)
            print("Remaining regions",label,by_weight.most_common(6),by_position.most_common(8), flush=True)
        values = rest.copy()
        values[indices] += best_delta
        key = body.shape_key_add(name=key_name, from_mix=False)
        key.data.foreach_set("co", values.astype(np.float32).ravel())
        key.value = 0.
        print("DRAPE FIT", label, action, first_score, best_score,
              "max cm", round(np.max(np.linalg.norm(best_delta, axis=1)) * 100., 2), flush=True)
    for b in rig.pose.bones:
        b.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()

