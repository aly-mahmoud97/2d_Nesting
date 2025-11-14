"""
2D Nesting Algorithm - Balanced Speed & Efficiency Version

Improvements over previous version:
- Multiple MaxRects heuristics for better placement
- Improved waste-minimizing scoring
- Configurable quality vs speed trade-off
- Better panel sorting strategies
- Smart position selection
"""

import Rhino.Geometry as rg
from ghpythonlib.treehelpers import list_to_tree, tree_to_list
import math

# ===== CONFIGURATION PARAMETERS =====
class NestingConfig:
    """Centralized configuration for nesting algorithm"""
    def __init__(self, quality_mode='balanced'):
        """
        quality_mode: 'fast', 'balanced', or 'best'
        - fast: Prioritize speed (like before)
        - balanced: Good balance of speed and quality (recommended)
        - best: Maximum quality, slower
        """
        self.quality_mode = quality_mode

        # Performance tuning based on quality mode
        if quality_mode == 'fast':
            self.GRID_TARGET_CELLS = 5
            self.GRID_MIN_CELLS = 5
            self.GRID_MAX_CELLS = 50
            self.MAX_CANDIDATE_POSITIONS = 40
            self.PRIORITY_POSITIONS = 15
            self.MAX_TEST_PANELS_HIGH = 20
            self.MAX_TEST_PANELS_LOW = 50
            self.USE_MULTIPLE_HEURISTICS = False
            self.PANEL_SORT_STRATEGY = 'area'  # area, perimeter, mixed

        elif quality_mode == 'best':
            self.GRID_TARGET_CELLS = 8
            self.GRID_MIN_CELLS = 10
            self.GRID_MAX_CELLS = 100
            self.MAX_CANDIDATE_POSITIONS = 100
            self.PRIORITY_POSITIONS = 30
            self.MAX_TEST_PANELS_HIGH = 50
            self.MAX_TEST_PANELS_LOW = 100
            self.USE_MULTIPLE_HEURISTICS = True
            self.PANEL_SORT_STRATEGY = 'mixed'

        else:  # balanced
            self.GRID_TARGET_CELLS = 6
            self.GRID_MIN_CELLS = 8
            self.GRID_MAX_CELLS = 70
            self.MAX_CANDIDATE_POSITIONS = 60
            self.PRIORITY_POSITIONS = 20
            self.MAX_TEST_PANELS_HIGH = 30
            self.MAX_TEST_PANELS_LOW = 70
            self.USE_MULTIPLE_HEURISTICS = True
            self.PANEL_SORT_STRATEGY = 'mixed'

        # Packing thresholds
        self.SHEET_FULL_THRESHOLD = 0.95
        self.HIGH_EFFICIENCY_THRESHOLD = 0.90
        self.TEST_PANEL_THRESHOLD = 50

        # Skyline management
        self.MAX_SKYLINE_SEGMENTS = 100
        self.SKYLINE_MERGE_TOLERANCE = 0.1

        # Scoring weights
        self.WEIGHT_FILL_RATIO = 2.0
        self.WEIGHT_PANEL_COUNT = 1.0
        self.WEIGHT_SHEET_AREA = 0.001

        # MaxRects configuration
        self.USE_MAXRECTS = True

        # Debug
        self.VERBOSE = False

# User can override quality mode here
QUALITY_MODE = 'balanced'  # Options: 'fast', 'balanced', 'best'
CONFIG = NestingConfig(QUALITY_MODE)

# KERF COMPENSATION (blade thickness)
kerf = kerf  # Input parameter

class Panel:
    """Panel with proper kerf compensation"""
    def __init__(self, w, h, id, tag=None):
        self.orig_w = float(w)
        self.orig_h = float(h)
        self.w = float(w)
        self.h = float(h)
        self.area = self.w * self.h
        self.perimeter = 2 * (self.w + self.h)
        self.aspect_ratio = max(self.w, self.h) / min(self.w, self.h)
        self.rotated = False
        self.id = id
        self.tag = tag
        self._placed_w_cache = None
        self._placed_h_cache = None

    def rotate(self):
        """Rotate panel 90 degrees"""
        self.w, self.h = self.h, self.w
        self.rotated = not self.rotated
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

    def fits_in_rect(self, w, h, allow_rotation=True):
        """Check if panel fits in rectangle"""
        if self.get_placed_width() <= w and self.get_placed_height() <= h:
            return True, False
        if allow_rotation and self.get_placed_height() <= w and self.get_placed_width() <= h:
            return True, True
        return False, False

class FreeRectangle:
    """Represents a free rectangle in MaxRects algorithm"""
    def __init__(self, x, y, w, h):
        self.x = float(x)
        self.y = float(y)
        self.w = float(w)
        self.h = float(h)

    def area(self):
        return self.w * self.h

    def contains_point(self, x, y):
        """Check if point is inside rectangle"""
        return (self.x <= x < self.x + self.w and
                self.y <= y < self.y + self.h)

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
    """Sheet with improved MaxRects algorithm"""
    def __init__(self, w, h, type_id=0, avg_panel_size=None):
        self.orig_w = float(w)
        self.orig_h = float(h)
        self.w = float(w)
        self.h = float(h)
        self.panels = []
        self.type_id = type_id
        self.filled_area = 0

        # MaxRects: maintain list of free rectangles
        self.free_rects = [FreeRectangle(0, 0, self.w, self.h)]

        # Adaptive grid based on average panel size
        if avg_panel_size:
            target_cell_size = avg_panel_size * CONFIG.GRID_TARGET_CELLS
            self.grid_cols = max(CONFIG.GRID_MIN_CELLS,
                               min(CONFIG.GRID_MAX_CELLS,
                                   int(self.w / target_cell_size)))
            self.grid_rows = max(CONFIG.GRID_MIN_CELLS,
                               min(CONFIG.GRID_MAX_CELLS,
                                   int(self.h / target_cell_size)))
        else:
            self.grid_cols = 20
            self.grid_rows = 20

        self.cell_w = self.w / self.grid_cols
        self.cell_h = self.h / self.grid_rows
        self.grid = [[[] for _ in range(self.grid_cols)] for _ in range(self.grid_rows)]

        # Track skyline
        self.skyline = [(0, 0, self.w)]

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

    def fits(self, panel, x, y):
        """Fast grid-based collision check"""
        pw = panel.get_placed_width()
        ph = panel.get_placed_height()

        epsilon = 1e-6
        if x + pw > self.w + epsilon or y + ph > self.h + epsilon or x < -epsilon or y < -epsilon:
            return False

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
        """Place panel and update all tracking structures"""
        panel_idx = len(self.panels)
        self.panels.append((panel, x, y))

        pw = panel.get_placed_width()
        ph = panel.get_placed_height()
        cells = self._get_grid_cells(x, y, pw, ph)

        for row, col in cells:
            self.grid[row][col].append(panel_idx)

        self.filled_area += pw * ph
        self._update_skyline(x, y, pw, ph)

        if CONFIG.USE_MAXRECTS:
            self._update_free_rects(x, y, pw, ph)

    def _update_skyline(self, x, y, w, h):
        """Update skyline with proper merging"""
        new_segment = [x, y + h, w]

        updated_skyline = []
        merged = False

        for seg_x, seg_y, seg_w in self.skyline:
            if x < seg_x + seg_w and x + w > seg_x:
                if abs(seg_y - new_segment[1]) < CONFIG.SKYLINE_MERGE_TOLERANCE:
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

        if len(updated_skyline) > CONFIG.MAX_SKYLINE_SEGMENTS:
            updated_skyline = updated_skyline[:CONFIG.MAX_SKYLINE_SEGMENTS]

        self.skyline = updated_skyline

    def _update_free_rects(self, placed_x, placed_y, placed_w, placed_h):
        """Update free rectangles using MaxRects algorithm"""
        placed_rect = FreeRectangle(placed_x, placed_y, placed_w, placed_h)
        new_free_rects = []

        for rect in self.free_rects:
            if not rect.intersects(placed_rect):
                new_free_rects.append(rect)
            else:
                # Left split
                if placed_x > rect.x:
                    new_free_rects.append(FreeRectangle(
                        rect.x, rect.y,
                        placed_x - rect.x, rect.h
                    ))

                # Right split
                if placed_x + placed_w < rect.x + rect.w:
                    new_free_rects.append(FreeRectangle(
                        placed_x + placed_w, rect.y,
                        rect.x + rect.w - (placed_x + placed_w), rect.h
                    ))

                # Bottom split
                if placed_y > rect.y:
                    new_free_rects.append(FreeRectangle(
                        rect.x, rect.y,
                        rect.w, placed_y - rect.y
                    ))

                # Top split
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

    def get_candidate_positions_maxrects(self, panel, heuristic='all'):
        """
        Get candidate positions using MaxRects with multiple heuristics

        Heuristics:
        - BSSF: Best Short Side Fit (minimizes leftover short side)
        - BLSF: Best Long Side Fit (minimizes leftover long side)
        - BAF: Best Area Fit (minimizes leftover area)
        - BL: Bottom-Left (lowest Y, then lowest X)
        """
        positions = []
        pw = panel.get_placed_width()
        ph = panel.get_placed_height()

        for rect in self.free_rects:
            if pw <= rect.w and ph <= rect.h:
                # Calculate different scores
                leftover_horizontal = rect.w - pw
                leftover_vertical = rect.h - ph
                leftover_short = min(leftover_horizontal, leftover_vertical)
                leftover_long = max(leftover_horizontal, leftover_vertical)
                leftover_area = rect.area() - (pw * ph)

                if CONFIG.USE_MULTIPLE_HEURISTICS and heuristic == 'all':
                    # Use weighted combination of heuristics
                    # Prioritize minimizing waste
                    score = (leftover_short * 1.0 +      # Tight fit
                            leftover_long * 0.5 +         # Secondary fit
                            leftover_area * 0.001 +       # Total waste
                            rect.y * 0.1 +                # Prefer lower
                            rect.x * 0.01)                # Prefer left
                elif heuristic == 'BSSF':
                    score = leftover_short * 10000 + rect.y * 100 + rect.x
                elif heuristic == 'BLSF':
                    score = leftover_long * 10000 + rect.y * 100 + rect.x
                elif heuristic == 'BAF':
                    score = leftover_area * 100 + rect.y * 10 + rect.x
                else:  # BL (Bottom-Left)
                    score = rect.y * 10000 + rect.x

                positions.append((rect.x, rect.y, score))

        # Sort by score
        positions.sort(key=lambda p: p[2])

        # Return just x, y (limit based on config)
        return [(x, y) for x, y, _ in positions[:CONFIG.MAX_CANDIDATE_POSITIONS]]

    def find_best_position(self, panel):
        """Find best position for panel with rotation testing"""
        if CONFIG.USE_MAXRECTS:
            positions = self.get_candidate_positions_maxrects(panel, heuristic='all')
        else:
            positions = self.get_candidate_positions_hybrid()

        if not positions:
            return None, False

        best_position = None
        best_score = float('inf')
        best_rotated = False

        for x, y in positions:
            # Try original orientation
            if self.fits(panel, x, y):
                score = self._calculate_placement_score(panel, x, y)
                if score < best_score:
                    best_score = score
                    best_position = (x, y)
                    best_rotated = False

            # Try rotated orientation
            panel.rotate()
            if self.fits(panel, x, y):
                score = self._calculate_placement_score(panel, x, y)
                if score < best_score:
                    best_score = score
                    best_position = (x, y)
                    best_rotated = True
            panel.rotate()  # Rotate back

        return best_position, best_rotated

    def _calculate_placement_score(self, panel, x, y):
        """
        Calculate placement quality score - IMPROVED
        Lower score = better placement
        """
        pw = panel.get_placed_width()
        ph = panel.get_placed_height()

        # Base score: prefer bottom-left
        score = y * 100 + x

        # Calculate waste (important for efficiency)
        # Find the free rectangle this would fit in and calculate waste
        waste = 0
        for rect in self.free_rects:
            if rect.contains_point(x, y) and x + pw <= rect.x + rect.w and y + ph <= rect.y + rect.h:
                # This is the free rect we're using
                waste = (rect.w - pw) * (rect.h - ph)
                # Penalize creating large waste areas
                score += waste * 0.5
                break

        # Bonus for touching edges or panels (reduces fragmentation)
        touching_score = 0
        epsilon = 0.1

        # Edge touching
        if x < epsilon:
            touching_score -= 20  # Left edge
        if y < epsilon:
            touching_score -= 20  # Bottom edge
        if x + pw > self.w - epsilon:
            touching_score -= 10  # Right edge
        if y + ph > self.h - epsilon:
            touching_score -= 10  # Top edge

        # Panel adjacency (prevents scattered placement)
        adjacent_count = 0
        for p, px, py in self.panels:
            p_pw = p.get_placed_width()
            p_ph = p.get_placed_height()

            # Check if adjacent (sharing an edge)
            if (abs(x + pw - px) < epsilon and not (y + ph <= py or y >= py + p_ph)) or \
               (abs(px + p_pw - x) < epsilon and not (y + ph <= py or y >= py + p_ph)) or \
               (abs(y + ph - py) < epsilon and not (x + pw <= px or x >= px + p_pw)) or \
               (abs(py + p_ph - y) < epsilon and not (x + pw <= px or x >= px + p_pw)):
                adjacent_count += 1

        # Bonus for being adjacent to multiple panels
        touching_score -= adjacent_count * 15

        score += touching_score

        return score

    def get_candidate_positions_hybrid(self):
        """Get candidate positions using hybrid approach (fallback)"""
        if not self.panels:
            return [(0, 0)]

        positions = set()

        # Skyline positions
        for x, y, w in self.skyline[:20]:
            positions.add((x, y))
            if x + w < self.w:
                positions.add((x + w, y))

        # Corner positions
        recent_panels = self.panels[-min(10, len(self.panels)):]

        for p, px, py in recent_panels:
            pw = p.get_placed_width()
            ph = p.get_placed_height()

            positions.add((px + pw, py))
            positions.add((px, py + ph))

            if len(positions) < CONFIG.MAX_CANDIDATE_POSITIONS:
                positions.add((px + pw, py + ph))
                positions.add((0, py + ph))

        valid_positions = [(x, y) for x, y in positions
                          if x >= 0 and y >= 0 and x < self.w and y < self.h]

        valid_positions.sort(key=lambda p: (p[1], p[0]))

        if len(valid_positions) > CONFIG.MAX_CANDIDATE_POSITIONS:
            priority = valid_positions[:CONFIG.PRIORITY_POSITIONS]
            remaining = valid_positions[CONFIG.PRIORITY_POSITIONS:]

            sample_size = CONFIG.MAX_CANDIDATE_POSITIONS - CONFIG.PRIORITY_POSITIONS
            if sample_size > 0 and remaining:
                step = max(1, len(remaining) // sample_size)
                sampled = remaining[::step][:sample_size]
                valid_positions = priority + sampled
            else:
                valid_positions = priority

        return valid_positions

    def try_add_panel(self, panel):
        """Try to add panel, returns (success, x, y, rotated)"""
        position, rotated = self.find_best_position(panel)

        if position:
            return True, position[0], position[1], rotated

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
        return self.efficiency() > CONFIG.SHEET_FULL_THRESHOLD

def sort_panels(panels, strategy='mixed'):
    """
    Sort panels using different strategies

    Strategies:
    - area: Largest area first (good general purpose)
    - perimeter: Largest perimeter first (good for long pieces)
    - mixed: Combination approach (best for varied panels)
    """
    if strategy == 'area':
        return sorted(panels, key=lambda p: p.area, reverse=True)
    elif strategy == 'perimeter':
        return sorted(panels, key=lambda p: p.perimeter, reverse=True)
    elif strategy == 'mixed':
        # Sort by combination: area * (1 + aspect_ratio penalty)
        # This tends to place large, difficult panels first
        return sorted(panels, key=lambda p: p.area * (1 + 0.2 * p.aspect_ratio), reverse=True)
    else:
        return sorted(panels, key=lambda p: p.area, reverse=True)

def nest_panels(panels, sheet_sizes):
    """Optimized nesting algorithm with improved sheet selection"""

    if not panels:
        return []

    # Calculate average panel size for adaptive grid
    total_area = sum(p.area for p in panels)
    avg_panel_size = (total_area / len(panels)) ** 0.5

    # Sort panels using configured strategy
    panels = sort_panels(panels, CONFIG.PANEL_SORT_STRATEGY)

    sheets = []
    remaining = panels[:]
    iteration = 0

    while remaining:
        iteration += 1
        if CONFIG.VERBOSE:
            print(f"Iteration {iteration}: {len(remaining)} panels remaining")

        # Try to fit panels into existing sheets first
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

        # Need a new sheet - find best configuration
        best_config = None
        best_score = -1

        # Adaptive testing based on remaining panels
        if len(remaining) > CONFIG.TEST_PANEL_THRESHOLD:
            test_limit = CONFIG.MAX_TEST_PANELS_HIGH
        else:
            test_limit = min(len(remaining), CONFIG.MAX_TEST_PANELS_LOW)

        for w, h, type_id in sheet_sizes:
            # Test both orientations
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

                        fill_ratio = test_area / (sheet_w * sheet_h)
                        if fill_ratio > CONFIG.HIGH_EFFICIENCY_THRESHOLD:
                            break

                if test_count > 0:
                    sheet_area = sheet_w * sheet_h
                    fill_ratio = test_area / sheet_area
                    panel_ratio = test_count / max(1, min(len(remaining), test_limit))

                    # Improved scoring: heavily weight fill ratio
                    score = (fill_ratio * CONFIG.WEIGHT_FILL_RATIO +
                            panel_ratio * CONFIG.WEIGHT_PANEL_COUNT) * test_count - \
                            sheet_area * CONFIG.WEIGHT_SHEET_AREA

                    if score > best_score:
                        best_score = score
                        best_config = (sheet_w, sheet_h, type_id)

        # Create new sheet with best configuration
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
                print(f"ERROR: Could not place any panels in selected sheet {sheet_w}x{sheet_h}")
                break
        else:
            # Last resort: find smallest sheet that fits the largest remaining panel
            panel = remaining[0]
            placed = False

            sorted_sheets = sorted(sheet_sizes, key=lambda s: s[0] * s[1])

            for w, h, type_id in sorted_sheets:
                fits, needs_rotation = panel.fits_in_rect(w, h)
                if fits:
                    new_sheet = Sheet(w, h, type_id, avg_panel_size)
                    panel_copy = panel.copy()
                    if needs_rotation:
                        panel_copy.rotate()
                    if new_sheet.add(panel_copy):
                        sheets.append(new_sheet)
                        remaining.pop(0)
                        placed = True
                        break

            if not placed:
                print(f"WARNING: Cannot place panel {panel.id} ({panel.orig_w:.1f}x{panel.orig_h:.1f}) - too large for all sheets")
                remaining.pop(0)

        # Safety check
        if iteration > len(panels) * 2:
            print(f"ERROR: Too many iterations ({iteration}), terminating. {len(remaining)} panels unplaced.")
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
print(f"Quality mode: {QUALITY_MODE}")

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
    raise ValueError("No valid panel dimensions found in panel_dimensions_tree input")

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
