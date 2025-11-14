"""
2D Nesting Algorithm - Hybrid: Simple Strategy + Performance Optimizations

Combines:
- Proven height-first sorting (from simple version)
- Bottom-left placement heuristic (from simple version)
- Optimized grid collision detection (from complex version)
- Better free rectangle tracking (from complex version)
- Caching for performance (new)
"""

import Rhino.Geometry as rg
from ghpythonlib.treehelpers import list_to_tree, tree_to_list
import math

# KERF COMPENSATION (blade thickness)
kerf = kerf  # Input parameter

class Panel:
    """Panel with proper kerf compensation and caching"""
    def __init__(self, w, h, id, tag=None):
        self.orig_w = float(w)
        self.orig_h = float(h)
        self.w = float(w)
        self.h = float(h)
        self.area = self.w * self.h
        self.rotated = False
        self.id = id
        self.tag = tag
        # Cache for placed dimensions (with kerf)
        self._placed_w_cache = None
        self._placed_h_cache = None

    def rotate(self):
        """Rotate panel 90 degrees"""
        self.w, self.h = self.h, self.w
        self.rotated = not self.rotated
        # Invalidate cache
        self._placed_w_cache = None
        self._placed_h_cache = None

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

    def copy(self):
        """Create a safe copy of the panel"""
        p = Panel(self.orig_w, self.orig_h, self.id, self.tag)
        if self.rotated:
            p.rotate()
        return p

class FreeRectangle:
    """Represents a free rectangle for better space tracking"""
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
    """Sheet with optimized collision detection and simple placement strategy"""
    def __init__(self, w, h, type_id=0, avg_panel_size=None):
        self.orig_w = float(w)
        self.orig_h = float(h)
        self.w = float(w)
        self.h = float(h)
        self.panels = []  # (panel, x, y) tuples
        self.type_id = type_id
        self.filled_area = 0  # Track filled area for early exit

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
        self.skyline = [(0, 0, self.w)]  # (x, y, width) segments

        # Track free rectangles for better space awareness
        self.free_rects = [FreeRectangle(0, 0, self.w, self.h)]

    def _get_grid_cells(self, x, y, w, h):
        """Get grid cells that rectangle occupies (FIXED boundary calculation)"""
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

    def fits(self, panel, x, y):
        """Fast grid-based collision check"""
        pw = panel.get_placed_width()
        ph = panel.get_placed_height()

        # Boundary check with epsilon for floating point
        epsilon = 1e-6
        if x + pw > self.w + epsilon or y + ph > self.h + epsilon or x < -epsilon or y < -epsilon:
            return False

        # Grid-based collision check
        cells = self._get_grid_cells(x, y, pw, ph)
        checked = set()

        for row, col in cells:
            for panel_idx in self.grid[row][col]:
                if panel_idx in checked:
                    continue
                checked.add(panel_idx)

                p, px, py = self.panels[panel_idx]
                p_pw = p.get_placed_width()
                p_ph = p.get_placed_height()

                if not (x + pw <= px or x >= px + p_pw or
                       y + ph <= py or y >= py + p_ph):
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

        # Update filled area
        self.filled_area += pw * ph

        # Update skyline
        self._update_skyline(x, y, pw, ph)

        # Update free rectangles
        self._update_free_rects(x, y, pw, ph)

    def _update_skyline(self, x, y, w, h):
        """Update skyline after placing a panel (improved merging)"""
        new_segment = [x, y + h, w]

        updated_skyline = []
        merged = False

        for seg_x, seg_y, seg_w in self.skyline:
            # Check if new segment overlaps this segment in X
            if x < seg_x + seg_w and x + w > seg_x:
                # Same height - can merge
                if abs(seg_y - new_segment[1]) < 0.1:
                    if not merged:
                        new_segment[0] = min(new_segment[0], seg_x)
                        new_segment[2] = max(new_segment[0] + new_segment[2],
                                            seg_x + seg_w) - new_segment[0]
                        merged = True
                    continue
                elif seg_y < new_segment[1]:
                    # Shadowed segment
                    continue

            updated_skyline.append((seg_x, seg_y, seg_w))

        updated_skyline.append(tuple(new_segment))
        updated_skyline.sort(key=lambda s: (s[1], s[0]))

        # Prune if too many
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
                # Split the rectangle
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

        # Remove contained rectangles
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

        # Strategy 1: Skyline positions
        for x, y, w in self.skyline[:30]:  # Increased from 20
            positions.add((x, y))
            if x + w < self.w:
                positions.add((x + w, y))

        # Strategy 2: Corner positions of existing panels
        for p, px, py in self.panels:
            pw = p.get_placed_width()
            ph = p.get_placed_height()

            # Primary corners
            positions.add((px + pw, py))      # Right
            positions.add((px, py + ph))      # Top
            positions.add((px + pw, py + ph)) # Top-right

            # Edge positions
            positions.add((0, py + ph))       # Left edge at panel top
            positions.add((px + pw, 0))       # Bottom edge at panel right

        # Remove positions outside bounds
        valid_positions = [(x, y) for x, y in positions
                          if x >= 0 and y >= 0 and x < self.w and y < self.h]

        # Sort by Y first (bottom preference), then X (left preference)
        # This is KEY for bottom-left heuristic
        return sorted(valid_positions, key=lambda p: (p[1], p[0]))

    def try_add_panel(self, panel):
        """
        Try to add panel using SIMPLE bottom-left heuristic
        This is the key to good packing!
        """
        positions = self.get_candidate_positions()

        # Smart sampling if too many positions (from simple version)
        if len(positions) > 50:
            positions = (positions[:20] +
                        positions[-10:] +
                        positions[20:-10:max(1, (len(positions)-30)//20)])[:50]

        best_position = None
        best_waste = float('inf')
        best_rotated = False

        for x, y in positions:
            # Try original orientation
            if self.fits(panel, x, y):
                # SIMPLE waste calculation: just Y + height (bottom-left preference)
                waste = y + panel.get_placed_height()
                if waste < best_waste:
                    best_waste = waste
                    best_position = (x, y)
                    best_rotated = False

            # Try rotated
            panel.rotate()
            if self.fits(panel, x, y):
                waste = y + panel.get_placed_height()
                if waste < best_waste:
                    best_waste = waste
                    best_position = (x, y)
                    best_rotated = True
            panel.rotate()  # Rotate back

        if best_position:
            return True, best_position[0], best_position[1], best_rotated

        return False, 0, 0, False

    def add(self, panel):
        """Add panel to sheet"""
        success, x, y, rotated = self.try_add_panel(panel)
        if success:
            if rotated:
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
    Optimized nesting with HEIGHT-FIRST sorting
    This is crucial for good packing!
    """

    if not panels:
        return []

    # Calculate average panel size for adaptive grid
    total_area = sum(p.area for p in panels)
    avg_panel_size = (total_area / len(panels)) ** 0.5

    # HEIGHT-FIRST sorting (KEY to good packing!)
    # This is what makes the simple version work so well
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

        # Adaptive testing
        test_limit = min(len(remaining), 20 if len(remaining) > 50 else len(remaining))

        for w, h, type_id in sheet_sizes:
            # Test both orientations
            for sheet_w, sheet_h in [(w, h), (h, w)]:
                # Skip duplicate for square sheets
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

                    # Scoring from simple version
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
                elif panel.h <= w and panel.w <= h:
                    panel_copy = panel.copy()
                    panel_copy.rotate()
                    new_sheet = Sheet(w, h, type_id, avg_panel_size)
                    if new_sheet.add(panel_copy):
                        sheets.append(new_sheet)
                        remaining.pop(0)
                        placed = True
                        break

            if not placed:
                print(f"WARNING: Cannot place panel {panel.id} ({panel.orig_w:.1f}x{panel.orig_h:.1f})")
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

# Extract panel dimensions
try:
    panel_data = tree_to_list(panel_dimensions_tree)
except:
    panel_data = panel_dimensions_tree

panels_list = []

if isinstance(panel_data, list):
    for item in panel_data:
        try:
            if hasattr(item, '__len__') and len(item) >= 2:
                w = float(item[0])
                h = float(item[1])
                if w > 0 and h > 0:
                    panels_list.append((w, h))
                else:
                    print(f"Warning: Ignoring invalid panel dimensions ({w}, {h})")
        except (ValueError, TypeError, IndexError, AttributeError):
            continue

if not panels_list:
    raise ValueError("No valid panel dimensions found")

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

# Create panels
panels = []
for i, (w, h) in enumerate(panels_list):
    tag = tags[i] if i < len(tags) else None
    panels.append(Panel(w, h, i, tag))

print(f"Processing {len(panels)} panels with kerf={kerf}")

# ===== NESTING =====
sheets = nest_panels(panels, sheet_sizes)

# ===== OUTPUT GENERATION =====
print("\n=== NESTING RESULTS ===")
total_sheet_area = 0.0
total_panel_area = 0.0

for i, sheet in enumerate(sheets):
    sheet_area = sheet.orig_w * sheet.orig_h
    panel_area = sum(p.w * p.h for p, _, _ in sheet.panels)
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
panel_rects_list = []
panel_ids_list = []
panel_tags_list = []
panel_info_list = []
sheet_rects = []
sheet_types = []

x_offset = 0.0
gap = 10.0

for sheet_idx, sheet in enumerate(sheets):
    sheet_rect = rg.Rectangle3d(
        rg.Plane.WorldXY,
        rg.Interval(x_offset, x_offset + sheet.orig_w),
        rg.Interval(0, sheet.orig_h)
    )
    sheet_rects.append(sheet_rect)
    sheet_types.append(sheet.type_id)

    rects = []
    ids = []
    tags_out = []
    info = []

    for panel, x, y in sheet.panels:
        panel_rect = rg.Rectangle3d(
            rg.Plane.WorldXY,
            rg.Interval(x_offset + x, x_offset + x + panel.w),
            rg.Interval(y, y + panel.h)
        )

        rects.append(panel_rect)
        ids.append(panel.id)
        tags_out.append(panel.tag if panel.tag else "")

        info.append({
            'id': panel.id,
            'tag': panel.tag,
            'original_size': (panel.orig_w, panel.orig_h),
            'placed_size': (panel.w, panel.h),
            'rotated': panel.rotated,
            'sheet_type': sheet.type_id,
            'sheet_index': sheet_idx,
            'position': (x, y)
        })

    panel_rects_list.append(rects)
    panel_ids_list.append(ids)
    panel_tags_list.append(tags_out)
    panel_info_list.append(info)

    x_offset += sheet.orig_w + gap

# Outputs
a = list_to_tree(panel_rects_list)
b = list_to_tree(panel_ids_list)
c = len(sheets)
d = sheet_rects
e = panel_info_list
f = list_to_tree(panel_tags_list)
g = sheet_types
