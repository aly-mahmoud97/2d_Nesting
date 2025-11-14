"""
2D Nesting Algorithm - Planar Surface Support
Supports non-rectangular shapes including polygons, trimmed surfaces, and complex boundaries

Evolution from rectangular-only nesting:
- Input: Planar surfaces (Brep, Surface) instead of dimensions
- Collision: Polygon-polygon intersection instead of rectangle-only
- Rotation: Geometric transformation with 90-degree increments
- Future: Support for shapes with holes
"""

import Rhino.Geometry as rg
from ghpythonlib.treehelpers import list_to_tree, tree_to_list
import math

# KERF COMPENSATION (blade thickness)
kerf = kerf  # Input parameter

class PlanarPanel:
    """
    Panel class for planar surfaces (polygons, trimmed surfaces, etc.)
    Handles arbitrary 2D shapes, not just rectangles
    """
    def __init__(self, surface, id, tag=None):
        self.id = id
        self.tag = tag
        self.original_surface = surface

        # Extract boundary curve and ensure it's in XY plane
        self.boundary_curve = self._extract_boundary(surface)

        # Get bounding box for efficient collision pre-check
        bbox = self.boundary_curve.GetBoundingBox(True)
        self.bbox_min = bbox.Min
        self.bbox_max = bbox.Max

        # Calculate dimensions from bounding box
        self.w = bbox.Max.X - bbox.Min.X
        self.h = bbox.Max.Y - bbox.Min.Y

        # Calculate actual area (more accurate than bbox)
        self.area = self._calculate_area()

        # Track rotation state
        self.rotation_angle = 0  # 0, 90, 180, 270 degrees
        self.rotated = False

        # Cache for placed dimensions (with kerf)
        self._placed_w_cache = None
        self._placed_h_cache = None
        self._placed_boundary_cache = None

        # Store boundary points for collision detection
        self.boundary_points = self._get_boundary_points()

    def _extract_boundary(self, surface):
        """Extract boundary curve from planar surface and project to XY plane"""
        # Handle different input types
        if isinstance(surface, rg.Brep):
            # Get outer boundary from first face
            if surface.Faces.Count > 0:
                face = surface.Faces[0]
                loops = face.Loops

                # Find outer loop (typically the first one)
                for loop in loops:
                    if loop.LoopType == rg.BrepLoopType.Outer:
                        curve = loop.To3dCurve()
                        break
                else:
                    # Fallback to first loop if no outer loop found
                    curve = loops[0].To3dCurve()
            else:
                raise ValueError(f"Brep surface {self.id} has no faces")

        elif isinstance(surface, rg.Surface):
            # Get isocurve boundary
            curves = []
            curves.append(surface.IsoCurve(0, surface.Domain(1).Min))
            curves.append(surface.IsoCurve(1, surface.Domain(0).Max))
            curves.append(surface.IsoCurve(0, surface.Domain(1).Max))
            curves.append(surface.IsoCurve(1, surface.Domain(0).Min))
            curve = rg.Curve.JoinCurves(curves)[0]

        elif isinstance(surface, rg.Curve):
            curve = surface

        else:
            raise ValueError(f"Unsupported surface type: {type(surface)}")

        # Project to XY plane
        plane = rg.Plane.WorldXY
        curve_projected = rg.Curve.ProjectToPlane(curve, plane)

        return curve_projected

    def _calculate_area(self):
        """Calculate area using curve"""
        # Try to get area from curve properties
        mp = rg.AreaMassProperties.Compute(self.boundary_curve)
        if mp:
            return abs(mp.Area)
        else:
            # Fallback to bounding box area
            return self.w * self.h

    def _get_boundary_points(self):
        """Get boundary points as list of Point3d for collision detection"""
        # Convert curve to polyline for easier collision detection
        polyline = None

        if isinstance(self.boundary_curve, rg.PolylineCurve):
            polyline = self.boundary_curve.ToPolyline()
        else:
            # Discretize curve
            params = self.boundary_curve.DivideByCount(50, True)
            if params:
                points = [self.boundary_curve.PointAt(t) for t in params]
                polyline = rg.Polyline(points)

        if polyline:
            return list(polyline)
        else:
            # Fallback: use bbox corners
            return [
                rg.Point3d(self.bbox_min.X, self.bbox_min.Y, 0),
                rg.Point3d(self.bbox_max.X, self.bbox_min.Y, 0),
                rg.Point3d(self.bbox_max.X, self.bbox_max.Y, 0),
                rg.Point3d(self.bbox_min.X, self.bbox_max.Y, 0),
            ]

    def rotate(self):
        """Rotate panel 90 degrees clockwise"""
        # Update rotation angle
        self.rotation_angle = (self.rotation_angle + 90) % 360
        self.rotated = (self.rotation_angle != 0)

        # Swap dimensions (bounding box rotates)
        self.w, self.h = self.h, self.w

        # Rotate boundary curve and points
        # Create rotation transformation around origin
        center = rg.Point3d(0, 0, 0)
        axis = rg.Vector3d(0, 0, 1)
        angle_radians = math.radians(90)

        rotation = rg.Transform.Rotation(angle_radians, axis, center)

        # Rotate boundary curve
        new_curve = self.boundary_curve.Duplicate()
        new_curve.Transform(rotation)
        self.boundary_curve = new_curve

        # Rotate boundary points
        new_points = []
        for pt in self.boundary_points:
            new_pt = rg.Point3d(pt)
            new_pt.Transform(rotation)
            new_points.append(new_pt)
        self.boundary_points = new_points

        # Update bounding box
        bbox = self.boundary_curve.GetBoundingBox(True)
        self.bbox_min = bbox.Min
        self.bbox_max = bbox.Max

        # Invalidate caches
        self._placed_w_cache = None
        self._placed_h_cache = None
        self._placed_boundary_cache = None

    def get_placed_width(self):
        """Get width including kerf for placement (cached)"""
        if self._placed_w_cache is None:
            self._placed_w_cache = self.w + kerf
        return self._placed_w_cache

    def get_placed_height(self):
        """Get height including kerf for placement (cached)"""
        if self._placed_h_cache is None:
            self._placed_h_cache = self.h + kerf
        return self._placed_h_cache

    def get_placed_boundary(self, x, y):
        """Get boundary curve transformed to placement position with kerf offset"""
        # Create offset boundary with kerf
        offset_curve = self.boundary_curve.Offset(
            rg.Plane.WorldXY,
            kerf / 2.0,
            0.01,  # tolerance
            rg.CurveOffsetCornerStyle.Sharp
        )

        if offset_curve and len(offset_curve) > 0:
            curve = offset_curve[0]
        else:
            # Fallback: use original curve
            curve = self.boundary_curve.Duplicate()

        # Translate to position
        translation = rg.Transform.Translation(x, y, 0)
        curve.Transform(translation)

        return curve

    def get_transformed_boundary_points(self, x, y):
        """Get boundary points transformed to placement position"""
        translation = rg.Vector3d(x, y, 0)
        return [pt + translation for pt in self.boundary_points]

    def copy(self):
        """Create a copy of the panel"""
        p = PlanarPanel(self.original_surface, self.id, self.tag)

        # Apply same rotations
        rotations_needed = self.rotation_angle // 90
        for _ in range(rotations_needed):
            p.rotate()

        return p


class FreeRectangle:
    """Represents a free rectangle for space tracking"""
    def __init__(self, x, y, w, h):
        self.x = float(x)
        self.y = float(y)
        self.w = float(w)
        self.h = float(h)

    def area(self):
        return self.w * self.h

    def intersects(self, other):
        """Check if this rectangle intersects with another"""
        return not (self.x + self.w <= other.x or
                   other.x + other.w <= self.x or
                   self.y + self.h <= other.y or
                   other.y + other.h <= self.y)

    def contains(self, other):
        """Check if this rectangle fully contains another"""
        return (other.x >= self.x and
                other.y >= self.y and
                other.x + other.w <= self.x + self.w and
                other.y + other.h <= self.y + self.h)


class Sheet:
    """Sheet with polygon collision detection support"""
    def __init__(self, w, h, type_id=0, avg_panel_size=None):
        self.orig_w = float(w)
        self.orig_h = float(h)
        self.w = float(w)
        self.h = float(h)
        self.panels = []  # (panel, x, y) tuples
        self.type_id = type_id
        self.filled_area = 0

        # Adaptive grid based on average panel size
        if avg_panel_size:
            target_cell_size = avg_panel_size * 5
            self.grid_cols = max(5, min(50, int(self.w / target_cell_size)))
            self.grid_rows = max(5, min(50, int(self.h / target_cell_size)))
        else:
            self.grid_cols = 20
            self.grid_rows = 20

        self.cell_w = self.w / self.grid_cols
        self.cell_h = self.h / self.grid_rows
        self.grid = [[[] for _ in range(self.grid_cols)] for _ in range(self.grid_rows)]

        # Track skyline for position finding
        self.skyline = [(0, 0, self.w)]

        # Track free rectangles
        self.free_rects = [FreeRectangle(0, 0, self.w, self.h)]

    def _get_grid_cells(self, x, y, w, h):
        """Get grid cells that rectangle occupies"""
        x0 = max(0, min(self.grid_cols - 1, int(math.floor(x / self.cell_w))))
        y0 = max(0, min(self.grid_rows - 1, int(math.floor(y / self.cell_h))))
        x1 = max(0, min(self.grid_cols - 1, int(math.ceil((x + w) / self.cell_w))))
        y1 = max(0, min(self.grid_rows - 1, int(math.ceil((y + h) / self.cell_h))))

        cells = []
        for row in range(y0, y1 + 1):
            for col in range(x0, x1 + 1):
                if row < self.grid_rows and col < self.grid_cols:
                    cells.append((row, col))
        return cells

    def _check_polygon_collision(self, panel1_points, panel2_points):
        """
        Check if two polygons collide using point-in-polygon and edge intersection tests
        """
        # Quick check: if any point of panel1 is inside panel2, they collide
        for pt in panel1_points[:5]:  # Check subset for performance
            if self._point_in_polygon(pt, panel2_points):
                return True

        # Check if any point of panel2 is inside panel1
        for pt in panel2_points[:5]:
            if self._point_in_polygon(pt, panel1_points):
                return True

        # Check edge intersections
        if self._edges_intersect(panel1_points, panel2_points):
            return True

        return False

    def _point_in_polygon(self, point, polygon_points):
        """Ray casting algorithm for point in polygon test"""
        x, y = point.X, point.Y
        n = len(polygon_points)
        inside = False

        p1 = polygon_points[0]
        for i in range(1, n + 1):
            p2 = polygon_points[i % n]

            if y > min(p1.Y, p2.Y):
                if y <= max(p1.Y, p2.Y):
                    if x <= max(p1.X, p2.X):
                        if p1.Y != p2.Y:
                            xinters = (y - p1.Y) * (p2.X - p1.X) / (p2.Y - p1.Y) + p1.X
                        if p1.X == p2.X or x <= xinters:
                            inside = not inside
            p1 = p2

        return inside

    def _edges_intersect(self, poly1_points, poly2_points):
        """Check if any edges of two polygons intersect"""
        # Check subset of edges for performance
        n1 = len(poly1_points)
        n2 = len(poly2_points)

        # Sample edges if too many
        step1 = max(1, n1 // 20)
        step2 = max(1, n2 // 20)

        for i in range(0, n1, step1):
            p1 = poly1_points[i]
            p2 = poly1_points[(i + 1) % n1]

            for j in range(0, n2, step2):
                p3 = poly2_points[j]
                p4 = poly2_points[(j + 1) % n2]

                if self._line_segments_intersect(p1, p2, p3, p4):
                    return True

        return False

    def _line_segments_intersect(self, p1, p2, p3, p4):
        """Check if two line segments intersect"""
        def ccw(A, B, C):
            return (C.Y - A.Y) * (B.X - A.X) > (B.Y - A.Y) * (C.X - A.X)

        return ccw(p1, p3, p4) != ccw(p2, p3, p4) and ccw(p1, p2, p3) != ccw(p1, p2, p4)

    def fits(self, panel, x, y):
        """Check if panel fits at position using bounding box + polygon collision"""
        pw = panel.get_placed_width()
        ph = panel.get_placed_height()

        # Boundary check
        epsilon = 1e-6
        if x + pw > self.w + epsilon or y + ph > self.h + epsilon or x < -epsilon or y < -epsilon:
            return False

        # Fast grid-based bounding box collision check first
        cells = self._get_grid_cells(x, y, pw, ph)
        checked = set()

        # Get panel boundary points transformed to position
        panel_points = panel.get_transformed_boundary_points(x, y)

        for row, col in cells:
            for panel_idx in self.grid[row][col]:
                if panel_idx in checked:
                    continue
                checked.add(panel_idx)

                p, px, py = self.panels[panel_idx]
                p_pw = p.get_placed_width()
                p_ph = p.get_placed_height()

                # Bounding box collision
                if not (x + pw <= px or x >= px + p_pw or
                       y + ph <= py or y >= py + p_ph):

                    # Bounding boxes collide, now check actual polygon collision
                    placed_panel_points = p.get_transformed_boundary_points(px, py)

                    if self._check_polygon_collision(panel_points, placed_panel_points):
                        return False

        return True

    def place(self, panel, x, y):
        """Place panel and update tracking structures"""
        panel_idx = len(self.panels)
        self.panels.append((panel, x, y))

        # Update grid
        pw = panel.get_placed_width()
        ph = panel.get_placed_height()
        cells = self._get_grid_cells(x, y, pw, ph)

        for row, col in cells:
            self.grid[row][col].append(panel_idx)

        # Update filled area (use actual panel area, not bbox)
        self.filled_area += panel.area

        # Update skyline
        self._update_skyline(x, y, pw, ph)

        # Update free rectangles
        self._update_free_rects(x, y, pw, ph)

    def _update_skyline(self, x, y, w, h):
        """Update skyline after placing a panel"""
        new_segment = [x, y + h, w]

        updated_skyline = []
        merged = False

        for seg_x, seg_y, seg_w in self.skyline:
            if x < seg_x + seg_w and x + w > seg_x:
                if abs(seg_y - new_segment[1]) < 0.1:
                    if not merged:
                        new_segment[0] = min(new_segment[0], seg_x)
                        new_segment[2] = max(new_segment[0] + new_segment[2],
                                            seg_x + seg_w) - new_segment[0]
                        merged = True
                    continue
                elif seg_y < new_segment[1]:
                    continue

            updated_skyline.append((seg_x, seg_y, seg_w))

        updated_skyline.append(tuple(new_segment))
        updated_skyline.sort(key=lambda s: (s[1], s[0]))

        if len(updated_skyline) > 100:
            updated_skyline = updated_skyline[:100]

        self.skyline = updated_skyline

    def _update_free_rects(self, placed_x, placed_y, placed_w, placed_h):
        """Update free rectangles after placement"""
        placed_rect = FreeRectangle(placed_x, placed_y, placed_w, placed_h)
        new_free_rects = []

        for rect in self.free_rects:
            if not rect.intersects(placed_rect):
                new_free_rects.append(rect)
            else:
                if placed_x > rect.x:
                    new_free_rects.append(FreeRectangle(
                        rect.x, rect.y,
                        placed_x - rect.x, rect.h
                    ))

                if placed_x + placed_w < rect.x + rect.w:
                    new_free_rects.append(FreeRectangle(
                        placed_x + placed_w, rect.y,
                        rect.x + rect.w - (placed_x + placed_w), rect.h
                    ))

                if placed_y > rect.y:
                    new_free_rects.append(FreeRectangle(
                        rect.x, rect.y,
                        rect.w, placed_y - rect.y
                    ))

                if placed_y + placed_h < rect.y + rect.h:
                    new_free_rects.append(FreeRectangle(
                        rect.x, placed_y + placed_h,
                        rect.w, rect.y + rect.h - (placed_y + placed_h)
                    ))

        self.free_rects = self._prune_free_rects(new_free_rects)

    def _prune_free_rects(self, rects):
        """Remove rectangles that are fully contained in others"""
        if len(rects) <= 1:
            return rects

        pruned = []
        for i, rect in enumerate(rects):
            is_contained = False
            for j, other in enumerate(rects):
                if i != j and other.contains(rect):
                    is_contained = True
                    break
            if not is_contained:
                pruned.append(rect)

        return pruned

    def get_candidate_positions(self):
        """Get candidate positions using skyline and panel corners"""
        if not self.panels:
            return [(0, 0)]

        positions = set()

        # Skyline positions
        for x, y, w in self.skyline[:30]:
            positions.add((x, y))
            if x + w < self.w:
                positions.add((x + w, y))

        # Corner positions of existing panels
        for p, px, py in self.panels:
            pw = p.get_placed_width()
            ph = p.get_placed_height()

            positions.add((px + pw, py))
            positions.add((px, py + ph))
            positions.add((px + pw, py + ph))
            positions.add((0, py + ph))
            positions.add((px + pw, 0))

        # Remove positions outside bounds
        valid_positions = [(x, y) for x, y in positions
                          if x >= 0 and y >= 0 and x < self.w and y < self.h]

        # Sort by Y first (bottom-left heuristic)
        return sorted(valid_positions, key=lambda p: (p[1], p[0]))

    def try_add_panel(self, panel):
        """Try to add panel using bottom-left heuristic"""
        positions = self.get_candidate_positions()

        # Smart sampling
        if len(positions) > 50:
            positions = (positions[:20] +
                        positions[-10:] +
                        positions[20:-10:max(1, (len(positions)-30)//20)])[:50]

        best_position = None
        best_waste = float('inf')
        best_rotated = 0

        # Try different rotations (0, 90, 180, 270 degrees)
        for rotation_count in range(4):
            for x, y in positions:
                if self.fits(panel, x, y):
                    waste = y + panel.get_placed_height()
                    if waste < best_waste:
                        best_waste = waste
                        best_position = (x, y)
                        best_rotated = rotation_count

            # Rotate for next iteration
            panel.rotate()

        if best_position:
            return True, best_position[0], best_position[1], best_rotated

        return False, 0, 0, 0

    def add(self, panel):
        """Add panel to sheet"""
        success, x, y, rotations_needed = self.try_add_panel(panel)
        if success:
            # Panel is already rotated 4 times, rotate back to correct orientation
            current_rotation = panel.rotation_angle // 90
            additional_rotations = (rotations_needed - current_rotation) % 4
            for _ in range(additional_rotations):
                panel.rotate()
            self.place(panel, x, y)
        return success

    def efficiency(self):
        """Calculate packing efficiency"""
        if not self.panels:
            return 0.0
        return self.filled_area / (self.w * self.h)

    def should_stop_adding(self):
        """Check if we should stop trying to add more panels"""
        return self.efficiency() > 0.95


def nest_panels(panels, sheet_sizes):
    """
    Nesting with HEIGHT-FIRST sorting (works for arbitrary shapes too!)
    """
    if not panels:
        return []

    # Calculate average panel size for adaptive grid
    total_area = sum(p.area for p in panels)
    avg_panel_size = (total_area / len(panels)) ** 0.5

    # HEIGHT-FIRST sorting
    panels = sorted(panels, key=lambda p: (p.h, p.w), reverse=True)

    sheets = []
    remaining = panels[:]
    iteration = 0

    while remaining:
        iteration += 1

        # Try to fit panels into existing sheets
        placed_indices = []

        for sheet in sheets:
            if sheet.should_stop_adding():
                continue

            sheet_placed = []
            for i, panel in enumerate(remaining):
                if i not in placed_indices:
                    panel_copy = panel.copy()
                    if sheet.add(panel_copy):
                        remaining[i] = panel_copy
                        sheet_placed.append(i)
                        if sheet.should_stop_adding():
                            break

            placed_indices.extend(sheet_placed)

        # Remove placed panels
        if placed_indices:
            for i in reversed(sorted(placed_indices)):
                remaining.pop(i)

        if not remaining:
            break

        # Find best sheet configuration
        best_config = None
        best_score = -1

        test_limit = min(len(remaining), 20 if len(remaining) > 50 else len(remaining))

        for w, h, type_id in sheet_sizes:
            for sheet_w, sheet_h in [(w, h), (h, w)]:
                if sheet_w == sheet_h and (w, h) != (sheet_w, sheet_h):
                    continue

                test_sheet = Sheet(sheet_w, sheet_h, type_id, avg_panel_size)

                test_area = 0
                test_count = 0

                for panel in remaining[:test_limit]:
                    test_panel = panel.copy()
                    success, _, _, _ = test_sheet.try_add_panel(test_panel)
                    if success:
                        test_count += 1
                        test_area += test_panel.area

                        if test_area / (sheet_w * sheet_h) > 0.9:
                            break

                if test_count > 0:
                    fill_ratio = test_area / (sheet_w * sheet_h)
                    panel_ratio = test_count / max(1, min(len(remaining), test_limit))

                    score = (fill_ratio * 2 + panel_ratio) * test_count

                    if score > best_score:
                        best_score = score
                        best_config = (sheet_w, sheet_h, type_id)

        # Create new sheet
        if best_config:
            sheet_w, sheet_h, type_id = best_config
            new_sheet = Sheet(sheet_w, sheet_h, type_id, avg_panel_size)

            placed_indices = []
            for i, panel in enumerate(remaining):
                panel_copy = panel.copy()
                if new_sheet.add(panel_copy):
                    remaining[i] = panel_copy
                    placed_indices.append(i)
                    if new_sheet.should_stop_adding():
                        break

            if placed_indices:
                sheets.append(new_sheet)
                for i in reversed(sorted(placed_indices)):
                    remaining.pop(i)
            else:
                print(f"ERROR: Could not place panels in {sheet_w}x{sheet_h}")
                break
        else:
            # Last resort
            panel = remaining[0]
            placed = False

            for w, h, type_id in sorted(sheet_sizes, key=lambda s: s[0] * s[1]):
                if panel.w <= w and panel.h <= h:
                    new_sheet = Sheet(w, h, type_id, avg_panel_size)
                    panel_copy = panel.copy()
                    if new_sheet.add(panel_copy):
                        sheets.append(new_sheet)
                        remaining.pop(0)
                        placed = True
                        break

            if not placed:
                print(f"WARNING: Cannot place panel {panel.id}")
                remaining.pop(0)

        # Safety check
        if iteration > len(panels) * 2:
            print(f"ERROR: Too many iterations, {len(remaining)} panels unplaced")
            break

    return sheets


# ===== INPUT PROCESSING =====

def extract_values(data, param_name):
    """Extract numeric values with validation"""
    values = []

    if isinstance(data, (int, float)):
        return [float(data)]

    try:
        tree_data = tree_to_list(data)
        for branch in tree_data:
            if isinstance(branch, list):
                for item in branch:
                    try:
                        val = float(item)
                        if val > 0:
                            values.append(val)
                        else:
                            print(f"Warning: Ignoring non-positive value {val} in {param_name}")
                    except (ValueError, TypeError):
                        pass
            else:
                try:
                    val = float(branch)
                    if val > 0:
                        values.append(val)
                    else:
                        print(f"Warning: Ignoring non-positive value {val} in {param_name}")
                except (ValueError, TypeError):
                    pass
    except:
        try:
            val = float(data)
            if val > 0:
                values = [val]
            else:
                print(f"Warning: Ignoring non-positive value {val} in {param_name}")
        except:
            pass

    if not values:
        raise ValueError(f"{param_name} must contain at least one positive value")

    return values


def extract_planar_surfaces(data):
    """Extract planar surfaces from tree input"""
    surfaces = []

    try:
        tree_data = tree_to_list(data)
        for branch in tree_data:
            if isinstance(branch, list):
                for item in branch:
                    if isinstance(item, (rg.Brep, rg.Surface, rg.Curve)):
                        surfaces.append(item)
            else:
                if isinstance(branch, (rg.Brep, rg.Surface, rg.Curve)):
                    surfaces.append(branch)
    except:
        # Direct input
        if isinstance(data, (rg.Brep, rg.Surface, rg.Curve)):
            surfaces = [data]
        elif isinstance(data, list):
            for item in data:
                if isinstance(item, (rg.Brep, rg.Surface, rg.Curve)):
                    surfaces.append(item)

    return surfaces


# Extract sheet sizes
try:
    widths = extract_values(sheet_width, "Sheet width")
    heights = extract_values(sheet_height, "Sheet height")
except ValueError as e:
    raise ValueError(f"Sheet size error: {e}")

n = min(len(widths), len(heights))
if n == 0:
    raise ValueError("No valid sheet sizes found")

sheet_sizes = [(widths[i], heights[i], i) for i in range(n)]
print(f"Sheet sizes: {[(w, h) for w, h, _ in sheet_sizes]}")

# Extract planar surfaces
surfaces = extract_planar_surfaces(planar_surfaces_tree)

if not surfaces:
    raise ValueError("No valid planar surfaces found in input")

# Extract tags
tags = []
if panel_tags_tree:
    try:
        tag_data = tree_to_list(panel_tags_tree)
        for branch in tag_data:
            if isinstance(branch, list):
                tags.extend(branch)
            else:
                tags.append(branch)
    except:
        pass

# Create panels from planar surfaces
panels = []
for i, surface in enumerate(surfaces):
    try:
        tag = tags[i] if i < len(tags) else None
        panel = PlanarPanel(surface, i, tag)
        panels.append(panel)
    except Exception as ex:
        print(f"Warning: Could not process surface {i}: {ex}")

if not panels:
    raise ValueError("No valid panels could be created from surfaces")

print(f"Processing {len(panels)} planar panels with kerf={kerf}")

# ===== NESTING =====
sheets = nest_panels(panels, sheet_sizes)

# ===== OUTPUT GENERATION =====
print("\n=== NESTING RESULTS ===")
total_sheet_area = 0.0
total_panel_area = 0.0

for i, sheet in enumerate(sheets):
    sheet_area = sheet.orig_w * sheet.orig_h
    panel_area = sum(p.area for p, _, _ in sheet.panels)
    total_sheet_area += sheet_area
    total_panel_area += panel_area

    eff = 100 * panel_area / sheet_area

    print(f"Sheet {i+1} [{sheet.orig_w:.1f}x{sheet.orig_h:.1f}]: {len(sheet.panels)} panels, {eff:.1f}% usage")

if total_sheet_area > 0:
    overall_efficiency = 100 * total_panel_area / total_sheet_area
    print(f"\nTotal: {len(sheets)} sheets, {overall_efficiency:.1f}% overall efficiency")
else:
    print("\nNo sheets created")

# Generate output data
placed_surfaces_list = []
panel_ids_list = []
panel_tags_list = []
panel_info_list = []
sheet_rects = []
sheet_types = []

x_offset = 0.0
gap = 10.0

for sheet_idx, sheet in enumerate(sheets):
    # Sheet rectangle
    sheet_rect = rg.Rectangle3d(
        rg.Plane.WorldXY,
        rg.Interval(x_offset, x_offset + sheet.orig_w),
        rg.Interval(0, sheet.orig_h)
    )
    sheet_rects.append(sheet_rect)
    sheet_types.append(sheet.type_id)

    surfaces_out = []
    ids = []
    tags_out = []
    info = []

    for panel, x, y in sheet.panels:
        # Transform panel surface to placed position
        placed_curve = panel.get_placed_boundary(x_offset + x, y)
        surfaces_out.append(placed_curve)

        ids.append(panel.id)
        tags_out.append(panel.tag if panel.tag else "")

        info.append({
            'id': panel.id,
            'tag': panel.tag,
            'area': panel.area,
            'bbox_size': (panel.w, panel.h),
            'rotated': panel.rotated,
            'rotation_angle': panel.rotation_angle,
            'sheet_type': sheet.type_id,
            'sheet_index': sheet_idx,
            'position': (x, y)
        })

    placed_surfaces_list.append(surfaces_out)
    panel_ids_list.append(ids)
    panel_tags_list.append(tags_out)
    panel_info_list.append(info)

    x_offset += sheet.orig_w + gap

# Outputs
a = list_to_tree(placed_surfaces_list)  # Placed surface boundaries
b = list_to_tree(panel_ids_list)        # Panel IDs
c = len(sheets)                          # Number of sheets
d = sheet_rects                          # Sheet rectangles
e = panel_info_list                      # Panel info
f = list_to_tree(panel_tags_list)       # Panel tags
g = sheet_types                          # Sheet types
