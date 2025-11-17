#!/usr/bin/env python3
"""
Test script for 2D Beam Saw Nesting Algorithm
Replicates the C# algorithm logic in Python to test with specific inputs
"""

from enum import Enum
from dataclasses import dataclass
from typing import List, Tuple, Optional
import math


class RotationConstraint(Enum):
    NO_ROTATION = 0
    ROTATION_90_ALLOWED = 1


class PanelGrainDirection(Enum):
    MATCH_SHEET = 0
    FIXED_HORIZONTAL = 1
    FIXED_VERTICAL = 2


class SheetGrainDirection(Enum):
    HORIZONTAL = 0
    VERTICAL = 1


class CutOrientation(Enum):
    HORIZONTAL = 0
    VERTICAL = 1


class PanelSortStrategy(Enum):
    LARGEST_FIRST = 0
    SMALLEST_FIRST = 1
    AREA_DESCENDING = 2
    AREA_ASCENDING = 3


@dataclass
class Panel:
    width: float
    height: float
    rotation_constraint: RotationConstraint = RotationConstraint.ROTATION_90_ALLOWED
    grain_direction: PanelGrainDirection = PanelGrainDirection.MATCH_SHEET
    id: int = 0
    tag: str = ""

    @property
    def area(self) -> float:
        return self.width * self.height

    @property
    def max_dimension(self) -> float:
        return max(self.width, self.height)

    @property
    def min_dimension(self) -> float:
        return min(self.width, self.height)


@dataclass
class PlacedPanel:
    panel: Panel
    x: float
    y: float
    width: float
    height: float
    rotation_degrees: int
    final_grain_direction: str
    sheet_index: int


@dataclass
class SubSheet:
    x: float
    y: float
    width: float
    height: float
    level: int = 0
    parent_cut_id: int = -1
    sheet_index: int = 0

    @property
    def area(self) -> float:
        return self.width * self.height

    def can_fit(self, panel_width: float, panel_height: float) -> bool:
        return panel_width <= self.width + 1e-6 and panel_height <= self.height + 1e-6


@dataclass
class CutLine:
    id: int
    orientation: CutOrientation
    position: float
    start: float
    end: float
    kerf_thickness: float
    sheet_index: int


@dataclass
class PanelPlacement:
    panel: Panel
    sub_sheet: SubSheet
    rotated: bool
    width: float
    height: float
    final_grain_direction: str


class BeamSawNestingAlgorithm:
    def __init__(self,
                 sheet_width: float,
                 sheet_height: float,
                 sheet_grain: SheetGrainDirection,
                 kerf_thickness: float = 5.0,
                 preferred_cut: CutOrientation = CutOrientation.HORIZONTAL,
                 sort_strategy: PanelSortStrategy = PanelSortStrategy.AREA_DESCENDING):
        self.sheet_width = sheet_width
        self.sheet_height = sheet_height
        self.sheet_grain = sheet_grain
        self.kerf_thickness = kerf_thickness
        self.preferred_cut_orientation = preferred_cut
        self.sort_strategy = sort_strategy

        self.placed_panels: List[PlacedPanel] = []
        self.remaining_sub_sheets: List[SubSheet] = []
        self.cut_lines: List[CutLine] = []
        self.current_sheet_index = 0
        self.next_cut_id = 0

    def nest(self, panels: List[Panel]):
        """Run the nesting algorithm"""
        sorted_panels = self._sort_panels(panels)
        self._add_new_sheet()

        for panel in sorted_panels:
            placed = self._try_place_panel(panel)

            if not placed:
                self._add_new_sheet()
                placed = self._try_place_panel(panel)

                if not placed:
                    print(f"Warning: Panel {panel.id} (size {panel.width}x{panel.height}) "
                          f"is too large for sheet {self.sheet_width}x{self.sheet_height}")

    def _sort_panels(self, panels: List[Panel]) -> List[Panel]:
        """Sort panels according to strategy"""
        if self.sort_strategy == PanelSortStrategy.LARGEST_FIRST:
            return sorted(panels, key=lambda p: p.max_dimension, reverse=True)
        elif self.sort_strategy == PanelSortStrategy.SMALLEST_FIRST:
            return sorted(panels, key=lambda p: p.max_dimension)
        elif self.sort_strategy == PanelSortStrategy.AREA_DESCENDING:
            return sorted(panels, key=lambda p: p.area, reverse=True)
        elif self.sort_strategy == PanelSortStrategy.AREA_ASCENDING:
            return sorted(panels, key=lambda p: p.area)
        return panels

    def _add_new_sheet(self):
        """Add a new sheet"""
        if len(self.remaining_sub_sheets) > 0 or len(self.placed_panels) > 0:
            self.current_sheet_index += 1

        new_sheet = SubSheet(0, 0, self.sheet_width, self.sheet_height, 0, -1, self.current_sheet_index)
        self.remaining_sub_sheets.append(new_sheet)

    def _try_place_panel(self, panel: Panel) -> bool:
        """Try to place a panel in available sub-sheets"""
        current_sheet_sub_sheets = [s for s in self.remaining_sub_sheets
                                     if s.sheet_index == self.current_sheet_index]
        sorted_sub_sheets = sorted(current_sheet_sub_sheets, key=lambda s: s.area)

        for sub_sheet in sorted_sub_sheets:
            # Try without rotation
            placement = self._can_place_panel_in_sub_sheet(panel, sub_sheet, False)
            if placement:
                self._place_panel(panel, sub_sheet, placement)
                return True

            # Try with rotation if allowed
            if panel.rotation_constraint == RotationConstraint.ROTATION_90_ALLOWED:
                placement = self._can_place_panel_in_sub_sheet(panel, sub_sheet, True)
                if placement:
                    self._place_panel(panel, sub_sheet, placement)
                    return True

        return False

    def _can_place_panel_in_sub_sheet(self, panel: Panel, sub_sheet: SubSheet,
                                      rotate: bool) -> Optional[PanelPlacement]:
        """Check if panel can be placed with grain validation"""
        panel_w = panel.height if rotate else panel.width
        panel_h = panel.width if rotate else panel.height

        if not sub_sheet.can_fit(panel_w, panel_h):
            return None

        final_grain_dir = self._validate_grain_direction(panel, rotate)
        if final_grain_dir is None:
            return None

        return PanelPlacement(
            panel=panel,
            sub_sheet=sub_sheet,
            rotated=rotate,
            width=panel_w,
            height=panel_h,
            final_grain_direction=final_grain_dir
        )

    def _validate_grain_direction(self, panel: Panel, rotated: bool) -> Optional[str]:
        """Validate grain direction constraints"""
        is_horizontal = (not rotated and panel.width >= panel.height) or \
                       (rotated and panel.height >= panel.width)

        if panel.grain_direction == PanelGrainDirection.MATCH_SHEET:
            final_grain_dir = "Horizontal" if self.sheet_grain == SheetGrainDirection.HORIZONTAL else "Vertical"
            if self.sheet_grain == SheetGrainDirection.HORIZONTAL and not is_horizontal:
                return None
            if self.sheet_grain == SheetGrainDirection.VERTICAL and is_horizontal:
                return None
        elif panel.grain_direction == PanelGrainDirection.FIXED_HORIZONTAL:
            final_grain_dir = "Horizontal"
            if not is_horizontal:
                return None
        elif panel.grain_direction == PanelGrainDirection.FIXED_VERTICAL:
            final_grain_dir = "Vertical"
            if is_horizontal:
                return None
        else:
            final_grain_dir = "Unknown"

        return final_grain_dir

    def _place_panel(self, panel: Panel, sub_sheet: SubSheet, placement: PanelPlacement):
        """Place panel and perform guillotine cut"""
        placed = PlacedPanel(
            panel=panel,
            x=sub_sheet.x,
            y=sub_sheet.y,
            width=placement.width,
            height=placement.height,
            rotation_degrees=90 if placement.rotated else 0,
            final_grain_direction=placement.final_grain_direction,
            sheet_index=sub_sheet.sheet_index
        )
        self.placed_panels.append(placed)
        self.remaining_sub_sheets.remove(sub_sheet)
        self._perform_guillotine_cut(sub_sheet, placement.width, placement.height)

    def _perform_guillotine_cut(self, sub_sheet: SubSheet, used_width: float, used_height: float):
        """Perform guillotine cut and create remaining sub-sheets"""
        remaining_width = sub_sheet.width - used_width - self.kerf_thickness
        remaining_height = sub_sheet.height - used_height - self.kerf_thickness

        cut_horizontal_first = self.preferred_cut_orientation == CutOrientation.HORIZONTAL

        if abs(remaining_width - remaining_height) > 1e-6:
            cut_horizontal_first = remaining_height > remaining_width

        if cut_horizontal_first:
            # Horizontal cut first
            if remaining_height > 1e-6:
                cut_y = sub_sheet.y + used_height
                h_cut = CutLine(
                    id=self.next_cut_id,
                    orientation=CutOrientation.HORIZONTAL,
                    position=cut_y,
                    start=sub_sheet.x,
                    end=sub_sheet.x + sub_sheet.width,
                    kerf_thickness=self.kerf_thickness,
                    sheet_index=sub_sheet.sheet_index
                )
                self.cut_lines.append(h_cut)
                self.next_cut_id += 1

                top_sheet = SubSheet(
                    x=sub_sheet.x,
                    y=cut_y + self.kerf_thickness,
                    width=sub_sheet.width,
                    height=remaining_height,
                    level=sub_sheet.level + 1,
                    parent_cut_id=h_cut.id,
                    sheet_index=sub_sheet.sheet_index
                )
                self.remaining_sub_sheets.append(top_sheet)

            # Vertical cut
            if remaining_width > 1e-6:
                cut_x = sub_sheet.x + used_width
                v_cut = CutLine(
                    id=self.next_cut_id,
                    orientation=CutOrientation.VERTICAL,
                    position=cut_x,
                    start=sub_sheet.y,
                    end=sub_sheet.y + used_height,
                    kerf_thickness=self.kerf_thickness,
                    sheet_index=sub_sheet.sheet_index
                )
                self.cut_lines.append(v_cut)
                self.next_cut_id += 1

                right_sheet = SubSheet(
                    x=cut_x + self.kerf_thickness,
                    y=sub_sheet.y,
                    width=remaining_width,
                    height=used_height,
                    level=sub_sheet.level + 1,
                    parent_cut_id=v_cut.id,
                    sheet_index=sub_sheet.sheet_index
                )
                self.remaining_sub_sheets.append(right_sheet)
        else:
            # Vertical cut first
            if remaining_width > 1e-6:
                cut_x = sub_sheet.x + used_width
                v_cut = CutLine(
                    id=self.next_cut_id,
                    orientation=CutOrientation.VERTICAL,
                    position=cut_x,
                    start=sub_sheet.y,
                    end=sub_sheet.y + sub_sheet.height,
                    kerf_thickness=self.kerf_thickness,
                    sheet_index=sub_sheet.sheet_index
                )
                self.cut_lines.append(v_cut)
                self.next_cut_id += 1

                right_sheet = SubSheet(
                    x=cut_x + self.kerf_thickness,
                    y=sub_sheet.y,
                    width=remaining_width,
                    height=sub_sheet.height,
                    level=sub_sheet.level + 1,
                    parent_cut_id=v_cut.id,
                    sheet_index=sub_sheet.sheet_index
                )
                self.remaining_sub_sheets.append(right_sheet)

            # Horizontal cut
            if remaining_height > 1e-6:
                cut_y = sub_sheet.y + used_height
                h_cut = CutLine(
                    id=self.next_cut_id,
                    orientation=CutOrientation.HORIZONTAL,
                    position=cut_y,
                    start=sub_sheet.x,
                    end=sub_sheet.x + used_width,
                    kerf_thickness=self.kerf_thickness,
                    sheet_index=sub_sheet.sheet_index
                )
                self.cut_lines.append(h_cut)
                self.next_cut_id += 1

                top_sheet = SubSheet(
                    x=sub_sheet.x,
                    y=cut_y + self.kerf_thickness,
                    width=used_width,
                    height=remaining_height,
                    level=sub_sheet.level + 1,
                    parent_cut_id=h_cut.id,
                    sheet_index=sub_sheet.sheet_index
                )
                self.remaining_sub_sheets.append(top_sheet)

    def get_sheet_count(self) -> int:
        return self.current_sheet_index + 1

    def get_overall_efficiency(self) -> float:
        total_sheet_area = self.get_sheet_count() * self.sheet_width * self.sheet_height
        total_panel_area = sum(p.width * p.height for p in self.placed_panels)
        return (total_panel_area / total_sheet_area) * 100.0 if total_sheet_area > 0 else 0.0

    def get_sheet_utilization(self) -> List[float]:
        utilization = []
        sheet_area = self.sheet_width * self.sheet_height

        for i in range(self.get_sheet_count()):
            panels_on_sheet = [p for p in self.placed_panels if p.sheet_index == i]
            used_area = sum(p.width * p.height for p in panels_on_sheet)
            utilization.append((used_area / sheet_area) * 100.0 if sheet_area > 0 else 0.0)

        return utilization


def run_test():
    """Run test with specified parameters"""
    print("=" * 80)
    print("BEAM SAW NESTING ALGORITHM TEST")
    print("=" * 80)
    print()

    # Test parameters
    sheet_width = 2.40
    sheet_height = 1.20
    sheet_grain = SheetGrainDirection.HORIZONTAL
    kerf = 0.005

    panel_widths = [0.54, 0.1, 0.452, 0.54, 0.1, 0.452, 0.452, 0.372587, 0.512]
    panel_heights = [1.294, 1.258, 0.54, 1.594, 1.576, 0.54, 0.54, 0.512, 0.372587]
    rotation_allowed = [False] * 9

    print(f"Sheet Size: {sheet_width} x {sheet_height}")
    print(f"Sheet Grain: {sheet_grain.name}")
    print(f"Kerf: {kerf}")
    print(f"Number of Panels: {len(panel_widths)}")
    print()

    # Create panels
    panels = []
    for i in range(len(panel_widths)):
        rot_constraint = RotationConstraint.NO_ROTATION if not rotation_allowed[i] \
            else RotationConstraint.ROTATION_90_ALLOWED
        panel = Panel(
            width=panel_widths[i],
            height=panel_heights[i],
            rotation_constraint=rot_constraint,
            grain_direction=PanelGrainDirection.MATCH_SHEET,
            id=i,
            tag=f"Panel_{i}"
        )
        panels.append(panel)
        print(f"Panel {i}: {panel.width} x {panel.height}, "
              f"Rotation: {'Allowed' if rotation_allowed[i] else 'Not Allowed'}, "
              f"Area: {panel.area:.6f}")

    print()
    print("=" * 80)
    print("RUNNING NESTING ALGORITHM...")
    print("=" * 80)
    print()

    # Run algorithm
    algorithm = BeamSawNestingAlgorithm(
        sheet_width=sheet_width,
        sheet_height=sheet_height,
        sheet_grain=sheet_grain,
        kerf_thickness=kerf,
        preferred_cut=CutOrientation.HORIZONTAL,
        sort_strategy=PanelSortStrategy.AREA_DESCENDING
    )

    algorithm.nest(panels)

    # Display results
    print("=" * 80)
    print("RESULTS")
    print("=" * 80)
    print()
    print(f"Total Sheets Used: {algorithm.get_sheet_count()}")
    print(f"Panels Placed: {len(algorithm.placed_panels)} / {len(panels)}")
    print(f"Overall Efficiency: {algorithm.get_overall_efficiency():.2f}%")
    print(f"Total Cuts: {len(algorithm.cut_lines)}")
    print()

    # Sheet utilization
    utilization = algorithm.get_sheet_utilization()
    for i, util in enumerate(utilization):
        print(f"Sheet {i} Efficiency: {util:.2f}%")
    print()

    # Placed panels details
    print("=" * 80)
    print("PLACED PANELS")
    print("=" * 80)
    print()
    for i, placed in enumerate(algorithm.placed_panels):
        print(f"Panel {placed.panel.id}:")
        print(f"  Position: ({placed.x:.4f}, {placed.y:.4f})")
        print(f"  Size: {placed.width:.4f} x {placed.height:.4f}")
        print(f"  Rotation: {placed.rotation_degrees}°")
        print(f"  Grain: {placed.final_grain_direction}")
        print(f"  Sheet: {placed.sheet_index}")
        print()

    # Remaining sub-sheets
    print("=" * 80)
    print("REMAINING SUB-SHEETS (WASTE)")
    print("=" * 80)
    print()
    total_waste_area = 0
    for i, sub in enumerate(algorithm.remaining_sub_sheets):
        print(f"Sub-sheet {i}: Position ({sub.x:.4f}, {sub.y:.4f}), "
              f"Size {sub.width:.4f} x {sub.height:.4f}, "
              f"Area: {sub.area:.6f}, Sheet: {sub.sheet_index}")
        total_waste_area += sub.area
    print(f"\nTotal Waste Area: {total_waste_area:.6f}")
    print()

    # Cut lines summary
    print("=" * 80)
    print("CUT LINES")
    print("=" * 80)
    print()
    for i, cut in enumerate(algorithm.cut_lines):
        orient = "Horizontal" if cut.orientation == CutOrientation.HORIZONTAL else "Vertical"
        print(f"Cut {cut.id}: {orient} at {cut.position:.4f}, "
              f"from {cut.start:.4f} to {cut.end:.4f}, Sheet {cut.sheet_index}")
    print()

    print("=" * 80)
    print("TEST COMPLETE")
    print("=" * 80)


if __name__ == "__main__":
    run_test()
