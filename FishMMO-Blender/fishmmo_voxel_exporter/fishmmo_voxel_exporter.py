bl_info = {
    "name": "FishMMO Voxel Pro",
    "author": "FishMMO",
    "version": (3, 0, 2),
    "blender": (5, 0, 0),
    "location": "View3D > Sidebar > FishMMO Voxel",
    "description": "High-performance mesh → MagicaVoxel (.vox) exporter",
    "category": "Import-Export",
    "support": "COMMUNITY",
}

import bpy
import bmesh
import struct
import math
from mathutils import Vector
# BVHTree is imported lazily inside eval_mesh() — importing it at module
# level can fail depending on Blender's addon-load initialisation order.
from bpy.types import Operator, Panel, PropertyGroup
from bpy.props import IntProperty, PointerProperty, StringProperty, BoolProperty
from bpy_extras.io_utils import ExportHelper
import traceback


# =========================================================
# MagicaVoxel default 256-color palette (index 0 = unused)
# =========================================================

DEFAULT_PALETTE = [
    (0, 0, 0, 0),
    (255, 255, 255, 255), (255, 255, 204, 255), (255, 255, 153, 255),
    (255, 255, 102, 255), (255, 255, 51, 255), (255, 255, 0, 255),
    (255, 204, 255, 255), (255, 204, 204, 255), (255, 204, 153, 255),
    (255, 204, 102, 255), (255, 204, 51, 255), (255, 204, 0, 255),
    (255, 153, 255, 255), (255, 153, 204, 255), (255, 153, 153, 255),
    (255, 153, 102, 255), (255, 153, 51, 255), (255, 153, 0, 255),
    (255, 102, 255, 255), (255, 102, 204, 255), (255, 102, 153, 255),
    (255, 102, 102, 255), (255, 102, 51, 255), (255, 102, 0, 255),
    (255, 51, 255, 255), (255, 51, 204, 255), (255, 51, 153, 255),
    (255, 51, 102, 255), (255, 51, 51, 255), (255, 51, 0, 255),
    (255, 0, 255, 255), (255, 0, 204, 255), (255, 0, 153, 255),
    (255, 0, 102, 255), (255, 0, 51, 255), (255, 0, 0, 255),
    (204, 255, 255, 255), (204, 255, 204, 255), (204, 255, 153, 255),
    (204, 255, 102, 255), (204, 255, 51, 255), (204, 255, 0, 255),
    (204, 204, 255, 255), (204, 204, 204, 255), (204, 204, 153, 255),
    (204, 204, 102, 255), (204, 204, 51, 255), (204, 204, 0, 255),
    (204, 153, 255, 255), (204, 153, 204, 255), (204, 153, 153, 255),
    (204, 153, 102, 255), (204, 153, 51, 255), (204, 153, 0, 255),
    (204, 102, 255, 255), (204, 102, 204, 255), (204, 102, 153, 255),
    (204, 102, 102, 255), (204, 102, 51, 255), (204, 102, 0, 255),
    (204, 51, 255, 255), (204, 51, 204, 255), (204, 51, 153, 255),
    (204, 51, 102, 255), (204, 51, 51, 255), (204, 51, 0, 255),
    (204, 0, 255, 255), (204, 0, 204, 255), (204, 0, 153, 255),
    (204, 0, 102, 255), (204, 0, 51, 255), (204, 0, 0, 255),
    (153, 255, 255, 255), (153, 255, 204, 255), (153, 255, 153, 255),
    (153, 255, 102, 255), (153, 255, 51, 255), (153, 255, 0, 255),
    (153, 204, 255, 255), (153, 204, 204, 255), (153, 204, 153, 255),
    (153, 204, 102, 255), (153, 204, 51, 255), (153, 204, 0, 255),
    (153, 153, 255, 255), (153, 153, 204, 255), (153, 153, 153, 255),
    (153, 153, 102, 255), (153, 153, 51, 255), (153, 153, 0, 255),
    (153, 102, 255, 255), (153, 102, 204, 255), (153, 102, 153, 255),
    (153, 102, 102, 255), (153, 102, 51, 255), (153, 102, 0, 255),
    (153, 51, 255, 255), (153, 51, 204, 255), (153, 51, 153, 255),
    (153, 51, 102, 255), (153, 51, 51, 255), (153, 51, 0, 255),
    (153, 0, 255, 255), (153, 0, 204, 255), (153, 0, 153, 255),
    (153, 0, 102, 255), (153, 0, 51, 255), (153, 0, 0, 255),
    (102, 255, 255, 255), (102, 255, 204, 255), (102, 255, 153, 255),
    (102, 255, 102, 255), (102, 255, 51, 255), (102, 255, 0, 255),
    (102, 204, 255, 255), (102, 204, 204, 255), (102, 204, 153, 255),
    (102, 204, 102, 255), (102, 204, 51, 255), (102, 204, 0, 255),
    (102, 153, 255, 255), (102, 153, 204, 255), (102, 153, 153, 255),
    (102, 153, 102, 255), (102, 153, 51, 255), (102, 153, 0, 255),
    (102, 102, 255, 255), (102, 102, 204, 255), (102, 102, 153, 255),
    (102, 102, 102, 255), (102, 102, 51, 255), (102, 102, 0, 255),
    (102, 51, 255, 255), (102, 51, 204, 255), (102, 51, 153, 255),
    (102, 51, 102, 255), (102, 51, 51, 255), (102, 51, 0, 255),
    (102, 0, 255, 255), (102, 0, 204, 255), (102, 0, 153, 255),
    (102, 0, 102, 255), (102, 0, 51, 255), (102, 0, 0, 255),
    (51, 255, 255, 255), (51, 255, 204, 255), (51, 255, 153, 255),
    (51, 255, 102, 255), (51, 255, 51, 255), (51, 255, 0, 255),
    (51, 204, 255, 255), (51, 204, 204, 255), (51, 204, 153, 255),
    (51, 204, 102, 255), (51, 204, 51, 255), (51, 204, 0, 255),
    (51, 153, 255, 255), (51, 153, 204, 255), (51, 153, 153, 255),
    (51, 153, 102, 255), (51, 153, 51, 255), (51, 153, 0, 255),
    (51, 102, 255, 255), (51, 102, 204, 255), (51, 102, 153, 255),
    (51, 102, 102, 255), (51, 102, 51, 255), (51, 102, 0, 255),
    (51, 51, 255, 255), (51, 51, 204, 255), (51, 51, 153, 255),
    (51, 51, 102, 255), (51, 51, 51, 255), (51, 51, 0, 255),
    (51, 0, 255, 255), (51, 0, 204, 255), (51, 0, 153, 255),
    (51, 0, 102, 255), (51, 0, 51, 255), (51, 0, 0, 255),
    (0, 255, 255, 255), (0, 255, 204, 255), (0, 255, 153, 255),
    (0, 255, 102, 255), (0, 255, 51, 255), (0, 255, 0, 255),
    (0, 204, 255, 255), (0, 204, 204, 255), (0, 204, 153, 255),
    (0, 204, 102, 255), (0, 204, 51, 255), (0, 204, 0, 255),
    (0, 153, 255, 255), (0, 153, 204, 255), (0, 153, 153, 255),
    (0, 153, 102, 255), (0, 153, 51, 255), (0, 153, 0, 255),
    (0, 102, 255, 255), (0, 102, 204, 255), (0, 102, 153, 255),
    (0, 102, 102, 255), (0, 102, 51, 255), (0, 102, 0, 255),
    (0, 51, 255, 255), (0, 51, 204, 255), (0, 51, 153, 255),
    (0, 51, 102, 255), (0, 51, 51, 255), (0, 51, 0, 255),
    (0, 0, 255, 255), (0, 0, 204, 255), (0, 0, 153, 255),
    (0, 0, 102, 255), (0, 0, 51, 255),
    (238, 238, 238, 255), (221, 221, 221, 255), (187, 187, 187, 255),
    (170, 170, 170, 255), (136, 136, 136, 255), (119, 119, 119, 255),
    (85, 85, 85, 255), (68, 68, 68, 255), (34, 34, 34, 255),
    (17, 17, 17, 255),
    (204, 136, 102, 255), (187, 119, 85, 255), (170, 102, 68, 255),
    (153, 85, 51, 255), (136, 68, 34, 255), (119, 51, 17, 255),
    (221, 187, 153, 255), (204, 170, 136, 255), (187, 153, 119, 255),
    (170, 136, 102, 255), (153, 119, 85, 255), (136, 102, 68, 255),
    (119, 85, 51, 255), (102, 68, 34, 255), (85, 51, 17, 255),
    (68, 34, 0, 255), (51, 17, 0, 255), (34, 0, 0, 255),
    (17, 0, 0, 255),
]
# Pad to exactly 256 with greys — black (0,0,0,0) entries pull dark mesh
# colours toward invisible voxels.
_PAD_COLORS = [
    (50, 50, 50, 255), (60, 60, 60, 255), (70, 70, 70, 255),
    (80, 80, 80, 255), (90, 90, 90, 255), (100, 100, 100, 255),
    (110, 110, 110, 255), (120, 120, 120, 255), (130, 130, 130, 255),
    (140, 140, 140, 255), (150, 150, 150, 255), (160, 160, 160, 255),
    (180, 180, 180, 255), (200, 200, 200, 255), (210, 210, 210, 255),
    (230, 230, 230, 255), (240, 240, 240, 255),
]
_BASE_PALETTE_COUNT = len(DEFAULT_PALETTE)
while len(DEFAULT_PALETTE) < 256:
    idx = len(DEFAULT_PALETTE) - _BASE_PALETTE_COUNT
    if idx < len(_PAD_COLORS):
        DEFAULT_PALETTE.append(_PAD_COLORS[idx])
    else:
        DEFAULT_PALETTE.append((128, 128, 128, 255))


# =========================================================
# SAFE MESH EVALUATION (depsgraph correct)
# =========================================================

def eval_mesh(obj):
    from mathutils.bvhtree import BVHTree as _BVHTree

    depsgraph = bpy.context.evaluated_depsgraph_get()
    obj_eval = obj.evaluated_get(depsgraph)

    bm = bmesh.new()
    # Use *obj* (original) so modifiers are applied once via depsgraph.
    # Passing obj_eval (already evaluated) + depsgraph can double-apply.
    bm.from_object(obj, depsgraph)
    bm.transform(obj_eval.matrix_world)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])

    bvh = _BVHTree.FromBMesh(bm)
    return bm, bvh


# =========================================================
# FAST INSIDE TEST (ray parity)
# =========================================================

def inside_mesh(bvh, point):
    direction = Vector((1, 0, 0))
    origin = point.copy()
    hits = 0
    remaining = 1e6
    far = direction * remaining

    while True:
        hit = bvh.ray_cast(origin, far)
        if hit[0] is None:
            break
        hits += 1
        origin = hit[0] + direction * 0.0001
        remaining -= hit[3]
        far = direction * remaining

    return hits % 2 == 1


# =========================================================
# COLOR SAMPLING
# =========================================================

def _nearest_palette(rgba, palette):
    """Return palette index (1–255) closest to *rgba*."""
    best, best_dist = 1, float("inf")
    for i in range(1, 256):
        dr, dg, db = rgba[0] - palette[i][0], rgba[1] - palette[i][1], rgba[2] - palette[i][2]
        d = dr * dr + dg * dg + db * db
        if d < best_dist:
            best_dist = d
            best = i
            if d == 0:
                break
    return best


def _sample_color(obj, bm, face_index, surface_point=None):
    """Sample the material / vertex-colour from *obj* at the given face.

    Uses Principled BSDF base-colour, or the material's viewport colour.
    Does NOT access image pixels — that path caused uncatchable C-level
    crashes in Blender 5.x on some texture types.
    """
    fallback = (200, 200, 200, 255)

    bm.faces.ensure_lookup_table()
    if face_index is None or face_index < 0 or face_index >= len(bm.faces):
        return fallback

    face = bm.faces[face_index]
    mat_index = face.material_index

    # Try vertex colours first
    color_layer = bm.loops.layers.color.active
    if color_layer is not None:
        loops = list(face.loops)
        if loops:
            rgba = color_layer.data[loops[0].index].color
            return (int(rgba[0] * 255), int(rgba[1] * 255),
                    int(rgba[2] * 255), int(rgba[3] * 255))

    # Try material
    mat = None
    if obj.data.materials and 0 <= mat_index < len(obj.data.materials):
        mat = obj.data.materials[mat_index]

    if mat is None:
        return fallback

    if mat.use_nodes:
        nt = mat.node_tree
        if nt is not None:
            for node in nt.nodes:
                if node.type == 'BSDF_PRINCIPLED':
                    bc = node.inputs.get('Base Color')
                    if bc:
                        c = bc.default_value
                        return (int(c[0] * 255), int(c[1] * 255),
                                int(c[2] * 255), 255)
                    break

        # Node-based material — use viewport diffuse colour
        return (int(mat.diffuse_color[0] * 255),
                int(mat.diffuse_color[1] * 255),
                int(mat.diffuse_color[2] * 255), 255)

    # Simple (non-node) material
    c = mat.diffuse_color
    return (int(c[0] * 255), int(c[1] * 255), int(c[2] * 255), 255)


# =========================================================
# BONE-WEIGHT SAMPLING  (barycentric interpolation on the
# source face so each voxel cube gets rigid skinning)
# =========================================================

def _bone_weights_at_point(obj, bm, face_idx, point):
    """Return {vertex_group_index: weight} at *point* on *face_idx*.

    Uses barycentric interpolation across the three source vertices.
    Weights are normalised so every voxel cube has a valid skin.
    Returns an empty dict if no vertex groups exist.
    """
    if not obj.vertex_groups:
        return {}

    bm.faces.ensure_lookup_table()
    if face_idx is None or face_idx < 0 or face_idx >= len(bm.faces):
        return {}

    face = bm.faces[face_idx]
    vlist = list(face.verts)
    if len(vlist) < 3:
        return {}

    # --- barycentric coords ---
    v0, v1, v2 = vlist[0].co, vlist[1].co, vlist[2].co
    total = (v1 - v0).cross(v2 - v0).length
    if total < 1e-10:
        return {}

    closest = _closest_point_on_tri_simple(point, v0, v1, v2)
    a0 = (v2 - v1).cross(closest - v1).length
    a1 = (v0 - v2).cross(closest - v2).length
    a2 = (v1 - v0).cross(closest - v0).length
    b0, b1, b2 = a0 / total, a1 / total, a2 / total

    # --- collect vertex groups from the three source verts ---
    mv = obj.data.vertices
    i0, i1, i2 = vlist[0].index, vlist[1].index, vlist[2].index
    all_groups = set()
    for idx in (i0, i1, i2):
        for g in mv[idx].groups:
            all_groups.add(g.group)

    if not all_groups:
        return {}

    # --- helper: weight of a vertex in a group ---
    def _vw(vidx, grp):
        for g in mv[vidx].groups:
            if g.group == grp:
                return g.weight
        return 0.0

    # --- interpolate ---
    weights = {}
    for grp in all_groups:
        w = b0 * _vw(i0, grp) + b1 * _vw(i1, grp) + b2 * _vw(i2, grp)
        if w > 0.001:
            weights[grp] = w

    # --- normalise ---
    total_w = sum(weights.values())
    if total_w > 0:
        weights = {k: v / total_w for k, v in weights.items()}

    return weights


def _closest_point_on_tri_simple(p, a, b, c):
    """Project point *p* onto triangle (a,b,c), returns closest point."""
    ab = b - a; ac = c - a; ap = p - a
    d1 = ab.dot(ap); d2 = ac.dot(ap)
    if d1 <= 0 and d2 <= 0:
        return a.copy()
    bp = p - b; d3 = ab.dot(bp); d4 = ac.dot(bp)
    if d3 >= 0 and d4 <= d3:
        return b.copy()
    vc = d1 * d4 - d3 * d2
    if vc <= 0 and d1 >= 0 and d3 <= 0:
        return a + ab * (d1 / (d1 - d3)) if (d1 - d3) != 0 else a.copy()
    cp = p - c; d5 = ab.dot(cp); d6 = ac.dot(cp)
    if d6 >= 0 and d5 <= d6:
        return c.copy()
    vb = d5 * d2 - d1 * d6
    if vb <= 0 and d2 >= 0 and d6 <= 0:
        return a + ac * (d2 / (d2 - d6)) if (d2 - d6) != 0 else a.copy()
    va = d3 * d6 - d5 * d4
    if va <= 0 and (d4 - d3) >= 0 and (d5 - d6) >= 0:
        denom = (d4 - d3) + (d5 - d6)
        if denom != 0:
            return b + (c - b) * ((d4 - d3) / denom)
        return b.copy()
    denom = va + vb + vc
    if abs(denom) < 1e-30:
        return a.copy()
    inv = 1.0 / denom
    return a + ab * (vb * inv) + ac * (vc * inv)


# =========================================================
# GRID HELPER  (shared by voxelize + preview / live-handler
#               so origin / step never diverge)
# =========================================================

def _compute_grid(obj, resolution):
    """Return (origin, step, rx, ry, rz) for a cubic-voxel grid.

    *resolution* is the target count along the **longest** world-space axis.
    The grid is rectangular — shorter axes get fewer cells.
    """
    bbox = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    raw_min = Vector((min(v.x for v in bbox),
                      min(v.y for v in bbox),
                      min(v.z for v in bbox)))
    raw_max = Vector((max(v.x for v in bbox),
                      max(v.y for v in bbox),
                      max(v.z for v in bbox)))
    extent = raw_max - raw_min
    step = max(extent.x, extent.y, extent.z) / resolution

    # Pad by half a cell so edge voxels are always captured
    origin = raw_min - Vector((step * 0.5, step * 0.5, step * 0.5))
    padded_extent = (raw_max + Vector((step * 0.5,) * 3)) - origin

    rx = max(1, int(math.ceil(padded_extent.x / step)))
    ry = max(1, int(math.ceil(padded_extent.y / step)))
    rz = max(1, int(math.ceil(padded_extent.z / step)))
    return origin, step, rx, ry, rz


# =========================================================
# VOXELIZATION CORE (colour-aware, optional surface refine)
# =========================================================

def voxelize(obj, resolution=64, solid=False, refine=False):
    """Voxelize *obj* into a coloured grid.

    Returns:
        (voxels, (rx, ry, rz), origin, step, voxel_weights)
        — each voxel is (x, y, z, palette_index).
        — voxel_weights[i] is a {group_idx: weight} dict (or empty dict).
    """
    surface_res = resolution * 2 if refine else resolution
    interior_res = resolution

    bm, bvh = eval_mesh(obj)

    # Use the shared grid helper at *surface* resolution.
    # VOX format stores coords as single bytes — cap each axis at 255.
    origin, surface_step, rx, ry, rz = _compute_grid(obj, surface_res)
    rx = min(rx, 255)
    ry = min(ry, 255)
    rz = min(rz, 255)
    half_diag = surface_step * 0.866

    voxels = []
    voxel_weights = []  # parallel list of {group_idx: weight}

    # --- first pass: surface at surface_res ---
    for z in range(rz):
        for y in range(ry):
            for x in range(rx):
                p = Vector((
                    origin.x + x * surface_step + surface_step * 0.5,
                    origin.y + y * surface_step + surface_step * 0.5,
                    origin.z + z * surface_step + surface_step * 0.5,
                ))
                loc, _, face_idx, dist = bvh.find_nearest(p)
                on_surface = loc and dist <= half_diag
                if on_surface:
                    rgba = _sample_color(obj, bm, face_idx, surface_point=loc)
                    ci = _nearest_palette(rgba, DEFAULT_PALETTE)
                    voxels.append((x, y, z, ci))
                    voxel_weights.append(
                        _bone_weights_at_point(obj, bm, face_idx, loc))

    # --- second pass: interior only (at base resolution), if solid ---
    if solid:
        ds = surface_res // interior_res  # 2 when refine=True, 1 otherwise
        for z in range(0, rz, ds):
            for y in range(0, ry, ds):
                for x in range(0, rx, ds):
                    p = Vector((
                        origin.x + x * surface_step + surface_step * ds * 0.5,
                        origin.y + y * surface_step + surface_step * ds * 0.5,
                        origin.z + z * surface_step + surface_step * ds * 0.5,
                    ))
                    loc, _, face_idx, dist = bvh.find_nearest(p)
                    on_surface = loc and dist <= (surface_step * ds * 0.866)
                    if on_surface:
                        continue
                    if inside_mesh(bvh, p):
                        rgba = _sample_color(obj, bm, face_idx, surface_point=loc)
                        ci = _nearest_palette(rgba, DEFAULT_PALETTE)
                        w = _bone_weights_at_point(obj, bm, face_idx, loc)
                        for dz in range(ds):
                            for dy in range(ds):
                                for dx in range(ds):
                                    voxels.append((x + dx, y + dy, z + dz, ci))
                                    voxel_weights.append(w)

    bm.free()
    return voxels, (rx, ry, rz), origin, surface_step, voxel_weights


# =========================================================
# VOX WRITER (MagicaVoxel 150 format)
# =========================================================

def u32(x): return struct.pack("<I", x)
def i32(x): return struct.pack("<i", x)


def chunk(tag, content=b"", children=b""):
    return tag + u32(len(content)) + u32(len(children)) + content + children


def write_vox(path, voxels, size):
    sx, sy, sz = size

    size_chunk = chunk(b"SIZE", i32(sx) + i32(sy) + i32(sz))

    xyzi = i32(len(voxels))
    for x, y, z, c in voxels:
        xyzi += bytes((x, y, z, c))

    xyzi_chunk = chunk(b"XYZI", xyzi)

    rgba = bytearray()
    for r, g, b, a in DEFAULT_PALETTE:
        rgba += bytes((r, g, b, a))

    rgba_chunk = chunk(b"RGBA", bytes(rgba))

    main = chunk(b"MAIN", b"", size_chunk + xyzi_chunk + rgba_chunk)

    with open(path, "wb") as f:
        f.write(b"VOX ")
        f.write(i32(150))
        f.write(main)


# =========================================================
# PREVIEW (merged mesh, one cube per voxel)
# =========================================================

PREVIEW_COLLECTION = "FishMMO_VoxelPreview"

_CUBE_VERTS = [
    (-1,-1,-1), ( 1,-1,-1), ( 1, 1,-1), (-1, 1,-1),
    (-1,-1, 1), ( 1,-1, 1), ( 1, 1, 1), (-1, 1, 1),
]
_CUBE_FACES = [
    (0,1,2), (0,2,3), (5,4,7), (5,7,6),
    (4,0,3), (4,3,7), (1,5,6), (1,6,2),
    (3,2,6), (3,6,7), (4,5,1), (4,1,0),
]


def build_preview(voxels, origin, step, palette, source_obj=None,
                  voxel_weights=None):
    """Create a single merged mesh of coloured cubes for the voxel grid.

    If *source_obj* has an armature modifier, each voxel cube is skinned
    rigidly using the pre-computed *voxel_weights* (barycentric-interpolated
    from the source face).  Every vertex of a cube gets the same weights
    so the cube stays rigid under deformation.
    """
    col = bpy.data.collections.get(PREVIEW_COLLECTION)
    if col is None:
        col = bpy.data.collections.new(PREVIEW_COLLECTION)
        bpy.context.scene.collection.children.link(col)

    # Build the palette-index → material-slot remap upfront.
    used = sorted(set(ci for _, _, _, ci in voxels))
    ci_to_slot = {ci: i for i, ci in enumerate(used)}

    mesh = bpy.data.meshes.new("FV_VoxelPreview")
    verts, faces, mat_slots = [], [], []
    vi = 0

    for i, (x, y, z, ci) in enumerate(voxels):
        cx = origin.x + x * step + step * 0.5
        cy = origin.y + y * step + step * 0.5
        cz = origin.z + z * step + step * 0.5
        hs = step * 0.5

        verts.extend([
            (cx - hs, cy - hs, cz - hs), (cx + hs, cy - hs, cz - hs),
            (cx + hs, cy + hs, cz - hs), (cx - hs, cy + hs, cz - hs),
            (cx - hs, cy - hs, cz + hs), (cx + hs, cy - hs, cz + hs),
            (cx + hs, cy + hs, cz + hs), (cx - hs, cy + hs, cz + hs),
        ])
        slot = ci_to_slot[ci]
        for f in _CUBE_FACES:
            faces.append((vi + f[0], vi + f[1], vi + f[2]))
        mat_slots.extend([slot] * 12)
        vi += 8

    mesh.from_pydata(verts, [], faces)
    mesh.validate()
    mesh.update()

    for ci in used:
        mat = bpy.data.materials.new(f"FV_VoxelMat_{ci}")
        mat.use_nodes = False
        r, g, b, a = palette[ci]
        mat.diffuse_color = (r / 255.0, g / 255.0, b / 255.0, a / 255.0)
        mesh.materials.append(mat)

    mesh.polygons.foreach_set("material_index", mat_slots)

    obj = bpy.data.objects.new("FV_VoxelPreview", mesh)
    col.objects.link(obj)

    # --- Armature binding with manual rigid-cube skinning ---
    armature = None
    if source_obj:
        for mod in source_obj.modifiers:
            if mod.type == 'ARMATURE' and mod.object:
                armature = mod.object
                break

    if armature and source_obj.vertex_groups and voxel_weights:
        # Build group_index → Blender vertex-group lookup
        src_vgroups = {vg.index: vg for vg in source_obj.vertex_groups}
        # Create matching groups on the preview mesh
        for vg in source_obj.vertex_groups:
            obj.vertex_groups.new(name=vg.name)

        # Assign weights: each voxel cube's 8 verts get the same weights
        vert_idx = 0
        for i in range(len(voxels)):
            wdict = voxel_weights[i] if i < len(voxel_weights) else {}
            for grp_idx, weight in wdict.items():
                vg = src_vgroups.get(grp_idx)
                if vg is None:
                    continue
                for v in range(8):
                    obj.vertex_groups[vg.name].add(
                        [vert_idx + v], weight, 'REPLACE')
            vert_idx += 8

        # Add armature modifier
        arm_mod = obj.modifiers.new(name="FV_Armature", type='ARMATURE')
        arm_mod.object = armature
    else:
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj

    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    return obj


def clear_preview():
    col = bpy.data.collections.get(PREVIEW_COLLECTION)
    if col is None:
        return
    # Remove objects first, then their orphan mesh data
    meshes_to_check = []
    for obj in list(col.objects):
        if obj.data and isinstance(obj.data, bpy.types.Mesh):
            meshes_to_check.append(obj.data)
        bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in meshes_to_check:
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)
    bpy.data.collections.remove(col)
    for m in list(bpy.data.materials):
        if m.name.startswith("FV_VoxelMat_") and m.users == 0:
            bpy.data.materials.remove(m)
    # Also check for any leftover preview meshes (e.g. from previous crashes)
    for mesh in list(bpy.data.meshes):
        if mesh.name.startswith("FV_VoxelPreview") and mesh.users == 0:
            bpy.data.meshes.remove(mesh)


# =========================================================
# LIVE PREVIEW (frame-change handler + safe toggle callback)
# Must be defined BEFORE VOXELPRO_Settings.
# =========================================================

_live_state = None


def _live_handler(scene):
    """Frame-change callback — re-voxelizes and updates preview mesh.

    If the preview is already armature-bound, frame changes are handled
    automatically by Blender's deformer — we skip re-voxelization.
    """
    global _live_state
    if _live_state is None:
        return
    obj = bpy.data.objects.get(_live_state["obj_name"])
    if obj is None:
        return
    try:
        settings = scene.voxelpro_settings
        if not settings.live_preview:
            return

        # If the preview already exists and is armature-bound, the armature
        # deformer handles frame changes — no need to re-voxelize.
        preview_obj = bpy.data.objects.get("FV_VoxelPreview")
        if preview_obj is not None:
            for mod in preview_obj.modifiers:
                if mod.type == 'ARMATURE' and mod.object:
                    return  # already bound, animation handled by deformer

        # First-time setup — voxelize and bind to armature
        res = settings.preview_resolution
        voxels, grid, origin, step, vw = voxelize(obj, res, solid=False, refine=False)

        if voxels:
            clear_preview()
            build_preview(voxels, origin, step, DEFAULT_PALETTE,
                          source_obj=obj, voxel_weights=vw)
    except Exception:
        print("FishMMO Voxel: live handler error:")
        traceback.print_exc()


def _toggle_live(self, context):
    """Update callback — sets state and triggers immediate refresh."""
    global _live_state

    if context.scene.voxelpro_settings.live_preview:
        obj = context.active_object
        if obj and obj.type == 'MESH':
            _live_state = {"obj_name": obj.name}
            # Trigger immediate preview at current frame
            _live_handler(context.scene)
    else:
        _live_state = None
        clear_preview()


# =========================================================
# SETTINGS
# =========================================================

class VOXELPRO_Settings(PropertyGroup):
    resolution: IntProperty(
        name="Export Res",
        description="Voxel count along longest axis for .vox export (8–512)",
        default=64,
        min=8,
        max=512,
    )
    preview_resolution: IntProperty(
        name="Preview Res",
        description="Voxel count for viewport preview (8–128, lower = faster)",
        default=32,
        min=8,
        max=128,
    )
    live_preview: BoolProperty(
        name="Live",
        description="Auto-refresh preview when the timeline frame changes",
        default=False,
        update=_toggle_live,
    )
    refine: BoolProperty(
        name="2× Refine",
        description="Double surface resolution to capture thin features (fins, antennae)",
        default=False,
    )


# =========================================================
# EXPORT OPERATOR
# =========================================================

class VOXELPRO_OT_export(Operator, ExportHelper):
    bl_idname = "voxelpro.export"
    bl_label = "Export .vox"
    filename_ext = ".vox"
    filter_glob: StringProperty(default="*.vox", options={'HIDDEN'})

    def execute(self, context):
        try:
            obj = context.active_object
            if not obj or obj.type != 'MESH':
                self.report({'ERROR'}, "Select mesh")
                return {'CANCELLED'}

            settings = context.scene.voxelpro_settings
            voxels, size, _, _, _ = voxelize(obj, settings.resolution,
                                              solid=True, refine=settings.refine)

            if not voxels:
                self.report({'WARNING'}, "No voxels generated")
                return {'CANCELLED'}

            write_vox(self.filepath, voxels, size)
            self.report({'INFO'}, f"Exported {len(voxels)} voxels")
            return {'FINISHED'}

        except Exception:
            self.report({'ERROR'}, traceback.format_exc())
            return {'CANCELLED'}


class VOXELPRO_OT_preview(Operator):
    bl_idname = "voxelpro.preview"
    bl_label = "Preview Voxels"
    bl_description = "Build a 3D preview of the voxelized mesh"

    def execute(self, context):
        try:
            obj = context.active_object
            if not obj or obj.type != 'MESH':
                self.report({'ERROR'}, "Select mesh")
                return {'CANCELLED'}

            clear_preview()
            settings = context.scene.voxelpro_settings
            res = settings.preview_resolution
            refine = settings.refine

            # Warn if large
            eff = res * 2 if refine else res
            total = eff ** 3
            if total > 2_000_000:
                self.report(
                    {'WARNING'},
                    f"{total:,} cells — may be slow. Lower Preview Res."
                )

            voxels, grid, origin, step, vw = voxelize(obj, res, solid=False,
                                                       refine=refine)

            if not voxels:
                self.report({'WARNING'}, "No voxels generated")
                return {'CANCELLED'}

            build_preview(voxels, origin, step, DEFAULT_PALETTE,
                          source_obj=obj, voxel_weights=vw)
            self.report(
                {'INFO'},
                f"Preview: {len(voxels)} voxels "
                f"({grid[0]}x{grid[1]}x{grid[2]})"
            )
            return {'FINISHED'}

        except Exception:
            self.report({'ERROR'}, traceback.format_exc())
            return {'CANCELLED'}


class VOXELPRO_OT_clear_preview(Operator):
    bl_idname = "voxelpro.clear_preview"
    bl_label = "Clear Preview"
    bl_description = "Remove the voxel preview from the scene"

    def execute(self, context):
        clear_preview()
        self.report({'INFO'}, "Preview cleared")
        return {'FINISHED'}


# =========================================================
# UI PANEL
# =========================================================

class VOXELPRO_PT_panel(Panel):
    bl_label = "Voxel Pro"
    bl_idname = "VOXELPRO_PT_panel"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "FishMMO Voxel"

    def draw(self, context):
        layout = self.layout
        s = context.scene.voxelpro_settings

        row = layout.row(align=True)
        row.prop(s, "preview_resolution")
        row.prop(s, "live_preview", toggle=True)
        layout.prop(s, "resolution")
        layout.prop(s, "refine")

        row = layout.row(align=True)
        row.scale_y = 1.4
        row.operator("voxelpro.preview", text="Preview", icon="HIDE_OFF")
        row.operator("voxelpro.export", text="Export .vox", icon="EXPORT")

        layout.operator("voxelpro.clear_preview", text="Clear Preview", icon="X")


# =========================================================
# REGISTER
# =========================================================

classes = (
    VOXELPRO_Settings,
    VOXELPRO_OT_export,
    VOXELPRO_OT_preview,
    VOXELPRO_OT_clear_preview,
    VOXELPRO_PT_panel,
)


def register():
    try:
        print("FishMMO Voxel Pro registering...")
        for c in classes:
            bpy.utils.register_class(c)

        bpy.types.Scene.voxelpro_settings = PointerProperty(
            type=VOXELPRO_Settings
        )

        # Install the frame-change handler once at registration time
        if _live_handler not in bpy.app.handlers.frame_change_post:
            bpy.app.handlers.frame_change_post.append(_live_handler)

        print("FishMMO Voxel Pro registered OK")
    except Exception:
        print(traceback.format_exc())
        raise


def unregister():
    global _live_state
    _live_state = None
    if _live_handler in bpy.app.handlers.frame_change_post:
        bpy.app.handlers.frame_change_post.remove(_live_handler)

    for c in reversed(classes):
        bpy.utils.unregister_class(c)

    del bpy.types.Scene.voxelpro_settings
    print("FishMMO Voxel Pro unregistered")


if __name__ == "__main__":
    register()