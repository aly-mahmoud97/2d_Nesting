# Beam Saw Nesting Algorithm - Critical Bugs Report

**Date:** 2025-11-16
**Severity:** CRITICAL - Multiple bugs affect core functionality
**Status:** Production code has fundamental algorithmic flaws

---

## Executive Summary

The Beam Saw Nesting algorithm contains **6 critical bugs** that violate stated constraints, allow invalid output, and cause silent failures. The most severe issue is that the algorithm **violates its core guillotine cutting constraint**, creating partial cuts that don't extend through the entire material.

**Impact:**
- ❌ Invalid manufacturing instructions (non-guillotine cuts)
- ❌ Panels can exceed sheet boundaries
- ❌ Silent failures with lost panels
- ❌ No error detection or validation
- ⚠️ Suboptimal placement strategies

---

## CRITICAL BUG #1: Violation of Guillotine Cutting Constraint

**Severity:** 🔴 CRITICAL
**Location:** `BeamSawNestingAlgorithm.cs:556-730` (`PerformGuillotineCut`)
**Stated Requirement (Lines 5-7):**
```
* - Cuts must be straight (horizontal or vertical only)
* - Every cut must run completely through the sheet or sub-sheet
* - Cuts cannot stop midway
```

### The Bug

The algorithm creates **partial cuts that stop midway**, directly violating the guillotine constraint.

**Horizontal-First Mode (Lines 620-621):**
```csharp
var vCut = new CutLine(
    nextCutId++,
    CutOrientation.Vertical,
    cutX,
    subSheet.Y,
    subSheet.Y + usedHeight,  // ❌ STOPS AT PANEL HEIGHT, NOT FULL SUB-SHEET
    kerfThickness,
    subSheet.SheetIndex,
    subSheet
);
```

**Vertical-First Mode (Lines 700-701):**
```csharp
var hCut = new CutLine(
    nextCutId++,
    CutOrientation.Horizontal,
    cutY,
    subSheet.X,
    subSheet.X + usedWidth,  // ❌ STOPS AT PANEL WIDTH, NOT FULL SUB-SHEET
    kerfThickness,
    subSheet.SheetIndex,
    subSheet
);
```

### Visual Explanation

**What happens (WRONG):**
```
Sub-sheet: 1000x800mm, Panel placed: 600x400mm
Horizontal-first cut pattern:

+------------------+ 1000mm
|   Top Sub-sheet  |
|  (1000 x 395mm)  |
+-----+------------+ ← Horizontal cut (FULL WIDTH) ✓
|Panel|Right Sub   |
| 600 |  (395 x    |
| x   |   400mm)   |
| 400 +            | ← Vertical cut STOPS HERE ❌
|     | ??? Area   | ← This region is undefined!
+-----+            |
      +------------+ 800mm
         600mm
```

**What should happen (CORRECT):**
```
Option A: Two full guillotine cuts
+------------------+
|   Top Sub-sheet  | ← Horizontal cut all the way across
+------------------+
| Panel | Right    | ← Vertical cut all the way up
|       | Sub      |
+-------+----------+

Option B: Two full guillotine cuts (different order)
+-------+----------+
| Panel | Right    |
|       | Sub      |
|       |          | ← Vertical cut all the way up
+-------+          |
| Btm   |          |
+-------+----------+
```

### Impact

1. **Invalid manufacturing instructions:** A beam saw cannot execute these cuts
2. **Undefined material regions:** Areas that aren't allocated to any sub-sheet
3. **Potential overlaps:** Top-right corner claimed by multiple sub-sheets
4. **Material loss:** Untracked waste in undefined regions
5. **Cascading failures:** Incorrect sub-sheets lead to invalid subsequent placements

### Evidence of Overlap

**Top sub-sheet created (Lines 590-598):**
```csharp
var topSheet = new SubSheet(
    subSheet.X,                  // X = 0
    cutY + kerfThickness,        // Y = 405
    subSheet.Width,              // Width = 1000 (FULL WIDTH!)
    remainingHeight,             // Height = 395
    subSheet.Level + 1,
    hCut.Id,
    subSheet.SheetIndex
);
```

**Right sub-sheet created (Lines 629-638):**
```csharp
var rightSheet = new SubSheet(
    cutX + kerfThickness,        // X = 605
    subSheet.Y,                  // Y = 0
    remainingWidth,              // Width = 395
    usedHeight,                  // Height = 400
    subSheet.Level + 1,
    vCut.Id,
    subSheet.SheetIndex
);
```

**Overlap region:**
- Top sheet covers: X=[0, 1000], Y=[405, 800]
- Right sheet covers: X=[605, 1000], Y=[0, 400]
- **No overlap** in this case, but there's a gap!

**Gap region:** X=[605, 1000], Y=[400, 405] - the kerf area is **not tracked**

---

## CRITICAL BUG #2: Collision Detection Allows Oversized Panels

**Severity:** 🔴 CRITICAL
**Location:** `BeamSawNestingAlgorithm.cs:205-208` (`SubSheet.CanFit`)

### The Bug

Tolerance is applied in the **wrong direction**, allowing panels larger than the sub-sheet.

```csharp
public bool CanFit(double panelWidth, double panelHeight)
{
    return panelWidth <= Width + 1e-6 && panelHeight <= Height + 1e-6;
    //                           ^^^^^ WRONG! Should be MINUS
}
```

### Why This Is Wrong

**Floating-point tolerance** should provide a **safety buffer**, requiring panels to be slightly smaller:
```csharp
return panelWidth <= Width - 1e-6 && panelHeight <= Height - 1e-6;
```

### Impact

**Example:**
- Sub-sheet: `1000.0000000mm`
- Panel: `1000.0000005mm` (5 nanometers larger)
- Current code: `1000.0000005 <= 1000.0000001` → **TRUE** (allowed!) ❌
- Panel exceeds sub-sheet by 5nm

**Accumulation problem:**
- 10 cuts with 5nm overage each = 50nm total
- 100 cuts = 500nm = 0.5 microns
- With actual floating-point errors, accumulation could be worse

### Test Case That Fails

```csharp
[Fact]
public void CanFit_PanelExactlySameSize_ShouldFit()
{
    var subSheet = new SubSheet(0, 0, 1000, 500);
    bool fits = subSheet.CanFit(1000.0, 500.0);
    Assert.True(fits); // Currently passes
}

[Fact]
public void CanFit_PanelSlightlyLarger_ShouldNotFit()
{
    var subSheet = new SubSheet(0, 0, 1000, 500);
    bool fits = subSheet.CanFit(1000.0000005, 500.0);
    Assert.False(fits); // Currently FAILS - it allows the oversized panel!
}
```

---

## CRITICAL BUG #3: No Sheet Boundary Validation

**Severity:** 🔴 CRITICAL
**Location:** `BeamSawNestingAlgorithm.cs:531-551` (`PlacePanel`)

### The Bug

**No validation** that placed panels stay within sheet boundaries.

```csharp
private void PlacePanel(Panel panel, SubSheet subSheet, PanelPlacement placement)
{
    var placed = new PlacedPanel(
        panel,
        subSheet.X,      // ❌ No check: subSheet.X + placement.Width <= sheetWidth
        subSheet.Y,      // ❌ No check: subSheet.Y + placement.Height <= sheetHeight
        placement.Width,
        placement.Height,
        placement.Rotated ? 90 : 0,
        placement.FinalGrainDirection,
        subSheet.SheetIndex
    );
    placedPanels.Add(placed); // Panel added with no validation!
    // ...
}
```

### Impact

Combined with Bug #1 (incorrect sub-sheets) and Bug #2 (wrong tolerance), this creates a **perfect storm**:

1. Bug #1 creates sub-sheets with wrong dimensions
2. Bug #2 allows slightly oversized panels into those sub-sheets
3. Bug #3 provides no safety net - invalid panels are placed
4. **Result: Panels exceed sheet boundaries with no error**

### Test Case

```csharp
[Fact]
public void PlacePanel_ExceedsSheetBoundary_ShouldThrowOrReject()
{
    var algo = new BeamSawNestingAlgorithm(2440, 1220, SheetGrainDirection.Horizontal);

    // Manually create invalid sub-sheet (simulates Bug #1)
    var invalidSubSheet = new SubSheet(2000, 1000, 600, 400, 0, -1, 0);
    // This sub-sheet extends to X=2600, Y=1400 - BEYOND the 2440x1220 sheet!

    var panel = new Panel(600, 400);
    var placement = new PanelPlacement {
        Panel = panel,
        SubSheet = invalidSubSheet,
        Rotated = false,
        Width = 600,
        Height = 400,
        FinalGrainDirection = "Horizontal"
    };

    // Current behavior: Places panel at (2000, 1000) with size 600x400
    // Panel extends to (2600, 1400) - far beyond sheet!
    // NO ERROR IS THROWN ❌

    Assert.Throws<InvalidOperationException>(() =>
        algo.PlacePanel(panel, invalidSubSheet, placement));
}
```

---

## CRITICAL BUG #4: Silent Panel Failures (Lost Panels)

**Severity:** 🔴 CRITICAL
**Location:** `BeamSawNestingAlgorithm.cs:362-378` (`Nest`)

### The Bug

When panels fail to place, they are **silently dropped** with only a console warning.

```csharp
foreach (var panel in sortedPanels)
{
    bool placed = TryPlacePanel(panel);

    if (!placed)
    {
        AddNewSheet();
        placed = TryPlacePanel(panel);

        if (!placed)
        {
            // ❌ Panel is LOST - not tracked, not returned, not reported
            Console.WriteLine($"Warning: Panel {panel.Id} (size {panel.Width}x{panel.Height}) is too large for sheet {sheetWidth}x{sheetHeight}");
            // ❌ Loop continues to next panel - this one is gone forever!
        }
    }
}
```

### Impact

**Scenario 1: Panel too large**
```
Input: 10 panels, one is 3000x2000mm (too large for 2440x1220mm sheet)
Output: 9 panels placed, 1 silently dropped
User receives: Incomplete nesting with no error indication
Result: Manufacturing failure - missing part!
```

**Scenario 2: Grain constraint impossible**
```
Input: Panel 800x600mm, NoRotation, FixedVertical grain
Sheet: 2440x1220mm, Horizontal grain
Result: Panel fits dimensionally but fails grain validation
        New sheet added (line 369) - SAME horizontal grain
        Retry fails (line 370)
        Panel dropped (line 375)
        Empty sheet wasted!
User receives: Wasted sheet + missing panel + no error
```

### Missing Features

1. **No failure tracking:**
   ```csharp
   public List<Panel> FailedPanels { get; private set; } // Doesn't exist!
   ```

2. **No return value indicating success:**
   ```csharp
   public void Nest(List<Panel> panels) // Returns void - can't detect failure!
   ```

3. **No exception throwing:**
   ```csharp
   throw new NestingException($"Panel {panel.Id} cannot fit on sheet");
   ```

4. **No retry strategies:**
   - Try different rotation
   - Try different grain orientation
   - Try different placement order
   - Skip panel and retry later

### Test Case

```csharp
[Fact]
public void Nest_PanelTooLarge_ShouldReportFailure()
{
    var algo = new BeamSawNestingAlgorithm(1000, 500, SheetGrainDirection.Horizontal);
    var panels = new List<Panel>
    {
        new Panel(2000, 1000) // Too large!
    };

    algo.Nest(panels);

    // Current behavior: No placed panels, no exception, void return
    Assert.Empty(algo.GetPlacedPanels()); // Passes, but provides no useful info!

    // Desired behavior:
    // Assert.Single(algo.GetFailedPanels());
    // OR: Assert.Throws<NestingException>(() => algo.Nest(panels));
}
```

---

## CRITICAL BUG #5: Operator Precedence Ambiguity

**Severity:** 🟡 MEDIUM
**Location:** `BeamSawNestingAlgorithm.cs:497-498` (`ValidateGrainDirection`)

### The Bug

Missing parentheses create ambiguous logic (works accidentally due to operator precedence).

```csharp
bool isHorizontal = !rotated && panel.Width >= panel.Height ||
                   rotated && panel.Height >= panel.Width;
//                 ^^^^^^ Missing parentheses!
```

**Actual evaluation (due to && binding tighter than ||):**
```csharp
bool isHorizontal = (!rotated && panel.Width >= panel.Height) ||
                   (rotated && panel.Height >= panel.Width);
```

### Why This Is Dangerous

1. **Unclear intent:** Future developers might misread this
2. **Fragile:** Easy to break during refactoring
3. **Not immediately obvious:** Works correctly by accident

### Fix

```csharp
bool isHorizontal = (!rotated && panel.Width >= panel.Height) ||
                   (rotated && panel.Height >= panel.Width);
```

---

## CRITICAL BUG #6: Greedy Placement Strategy (Suboptimal)

**Severity:** 🟡 LOW (Not a bug, but suboptimal)
**Location:** `BeamSawNestingAlgorithm.cs:420-452` (`TryPlacePanel`)

### The Issue

Algorithm uses **first-fit strategy** - places panel in first available sub-sheet without considering if rotation might be better.

```csharp
foreach (var subSheet in sortedSubSheets)
{
    // Try without rotation
    if (CanPlacePanelInSubSheet(panel, subSheet, false, out var placement1))
    {
        PlacePanel(panel, subSheet, placement1);
        return true; // ❌ Immediately returns - never considers rotation!
    }

    // Only tries rotation if non-rotated fails
    if (panel.RotationConstraint == RotationConstraint.Rotation90Allowed)
    {
        if (CanPlacePanelInSubSheet(panel, subSheet, true, out var placement2))
        {
            PlacePanel(panel, subSheet, placement2);
            return true;
        }
    }
}
```

### Impact

**Example:**
```
Sub-sheet: 1000x500mm
Panel: 800x400mm, Rotation allowed

Without rotation: 800x400 → Remaining space: 200x500 + 800x100 = 180,000mm²
With rotation: 400x800 → Doesn't fit! ❌

Algorithm chooses non-rotated (correct for fitting, but may be suboptimal for future panels)
```

### Better Strategy

Try **both** orientations and choose the one that:
- Minimizes waste
- Maximizes remaining usable space
- Creates more rectangular (less fragmented) sub-sheets

---

## Additional Critical Issues

### 7. Performance Issues

**Location:** `BeamSawNestingAlgorithm.cs:420-452` (nested loops)

```csharp
foreach (var panel in sortedPanels)           // O(n)
{
    var sortedSubSheets = remainingSubSheets  // O(m log m) per panel
        .Where(...)
        .OrderBy(s => s.Area)
        .ToList();

    foreach (var subSheet in sortedSubSheets) // O(m)
    {
        // Placement logic
    }
}
// Overall: O(n * m * log m) where n=panels, m=sub-sheets
```

**Issue:** For 200 panels, each creating ~2-3 sub-sheets:
- m grows to ~400-600 sub-sheets
- Sorting 600 items ~200 times is expensive

**Impact:** Noticeable slowdown with >100 panels

---

### 8. Data Input Handling (Grasshopper Script)

**Location:** `GrasshopperBeamSawNesting.cs:703-725`

**Issues:**
1. **No DataTree support** - only handles List<double>
2. **No null safety** - assumes inputs exist
3. **Length mismatches** - only checks PanelWidths vs PanelHeights, not other optional inputs
4. **No input validation** - negative/zero/NaN values not checked until algorithm runs

**Example failure:**
```csharp
// If user provides:
PanelWidths: [1000, 800, 600]
PanelHeights: [500, 400]  // Oops, forgot one!

// Line 644-647 catches this:
if (PanelWidths.Count != PanelHeights.Count)
{
    A = "ERROR: PanelWidths and PanelHeights must have the same count";
    return;
}

// But what about:
RotationAllowed: [true]  // Only one value for 3 panels!
// Lines 708-711 handle this with default:
if (RotationAllowed != null && i < RotationAllowed.Count && !RotationAllowed[i])
// Uses default for missing values - NO ERROR! Could be unexpected.
```

---

## Test Coverage Recommendations

### Immediate Priority: Tests for Critical Bugs

```csharp
namespace BeamSawNesting.Tests
{
    public class CriticalBugTests
    {
        [Fact]
        public void Bug1_PerformGuillotineCut_ShouldCreateFullLengthCuts()
        {
            // Verify all cuts extend full length of sub-sheet
        }

        [Fact]
        public void Bug1_PerformGuillotineCut_ShouldNotCreateOverlappingSubSheets()
        {
            // Verify no overlapping regions
        }

        [Fact]
        public void Bug1_PerformGuillotineCut_ShouldAccountForAllMaterial()
        {
            // Panel area + kerf area + remaining sub-sheet areas = original sub-sheet area
        }

        [Fact]
        public void Bug2_CanFit_PanelSlightlyLarger_ShouldReject()
        {
            var subSheet = new SubSheet(0, 0, 1000, 500);
            Assert.False(subSheet.CanFit(1000.0000001, 500.0));
        }

        [Fact]
        public void Bug3_PlacePanel_ExceedingBoundary_ShouldThrow()
        {
            // Create scenario where panel exceeds sheet boundary
            // Verify exception or rejection
        }

        [Fact]
        public void Bug4_Nest_FailedPanel_ShouldBeTracked()
        {
            var algo = new BeamSawNestingAlgorithm(1000, 500, SheetGrainDirection.Horizontal);
            var panels = new List<Panel> { new Panel(2000, 1000) };

            algo.Nest(panels);

            // Assert.Single(algo.GetFailedPanels()); // Requires implementation
        }

        [Fact]
        public void Bug5_ValidateGrainDirection_SquarePanel_ShouldBeConsistent()
        {
            // Test square panels (width == height) with rotation
        }
    }
}
```

---

## Recommended Fixes

### Fix #1: Correct Guillotine Cutting

```csharp
private void PerformGuillotineCut(SubSheet subSheet, double usedWidth, double usedHeight)
{
    double remainingWidth = subSheet.Width - usedWidth - kerfThickness;
    double remainingHeight = subSheet.Height - usedHeight - kerfThickness;

    // Choose cut orientation
    bool cutVerticalFirst = remainingWidth > remainingHeight;

    if (cutVerticalFirst && remainingWidth > 1e-6)
    {
        // VERTICAL CUT - full height through sub-sheet
        double cutX = subSheet.X + usedWidth;
        var vCut = new CutLine(
            nextCutId++,
            CutOrientation.Vertical,
            cutX,
            subSheet.Y,
            subSheet.Y + subSheet.Height,  // ✓ FULL HEIGHT
            kerfThickness,
            subSheet.SheetIndex,
            subSheet
        );
        cutLines.Add(vCut);

        // Create right sub-sheet (full height)
        var rightSheet = new SubSheet(
            cutX + kerfThickness,
            subSheet.Y,
            remainingWidth,
            subSheet.Height,  // ✓ FULL HEIGHT
            subSheet.Level + 1,
            vCut.Id,
            subSheet.SheetIndex
        );
        remainingSubSheets.Add(rightSheet);

        cutSequence.Add(new CutOperation(cutSequence.Count + 1, vCut,
            $"Vertical cut at X={cutX:F2}", rightSheet, null));
    }

    if (remainingHeight > 1e-6)
    {
        // HORIZONTAL CUT - full width through remaining left section
        double cutY = subSheet.Y + usedHeight;
        double cutWidth = cutVerticalFirst ? usedWidth : subSheet.Width;

        var hCut = new CutLine(
            nextCutId++,
            CutOrientation.Horizontal,
            cutY,
            subSheet.X,
            subSheet.X + cutWidth,  // ✓ Appropriate width
            kerfThickness,
            subSheet.SheetIndex,
            subSheet
        );
        cutLines.Add(hCut);

        // Create top sub-sheet
        var topSheet = new SubSheet(
            subSheet.X,
            cutY + kerfThickness,
            cutWidth,
            remainingHeight,
            subSheet.Level + 1,
            hCut.Id,
            subSheet.SheetIndex
        );
        remainingSubSheets.Add(topSheet);

        cutSequence.Add(new CutOperation(cutSequence.Count + 1, hCut,
            $"Horizontal cut at Y={cutY:F2}", null, topSheet));
    }
}
```

### Fix #2: Correct Tolerance Direction

```csharp
public bool CanFit(double panelWidth, double panelHeight)
{
    const double TOLERANCE = 1e-6; // Safety margin
    return panelWidth <= Width - TOLERANCE && panelHeight <= Height - TOLERANCE;
}
```

### Fix #3: Add Boundary Validation

```csharp
private void PlacePanel(Panel panel, SubSheet subSheet, PanelPlacement placement)
{
    // Validate panel stays within sheet boundaries
    if (subSheet.X + placement.Width > sheetWidth + 1e-6 ||
        subSheet.Y + placement.Height > sheetHeight + 1e-6)
    {
        throw new InvalidOperationException(
            $"Panel placement at ({subSheet.X:F2}, {subSheet.Y:F2}) " +
            $"with size ({placement.Width:F2} x {placement.Height:F2}) " +
            $"exceeds sheet boundaries ({sheetWidth} x {sheetHeight})");
    }

    var placed = new PlacedPanel(/* ... */);
    placedPanels.Add(placed);
    remainingSubSheets.Remove(subSheet);
    PerformGuillotineCut(subSheet, placement.Width, placement.Height);
}
```

### Fix #4: Track Failed Panels

```csharp
public class BeamSawNestingAlgorithm
{
    private List<Panel> failedPanels;

    public BeamSawNestingAlgorithm(/* ... */)
    {
        // ...
        this.failedPanels = new List<Panel>();
    }

    public void Nest(List<Panel> panels)
    {
        var sortedPanels = SortPanels(panels);
        AddNewSheet();

        foreach (var panel in sortedPanels)
        {
            bool placed = TryPlacePanel(panel);

            if (!placed)
            {
                AddNewSheet();
                placed = TryPlacePanel(panel);

                if (!placed)
                {
                    failedPanels.Add(panel);
                    Console.WriteLine($"Warning: Panel {panel.Id} (size {panel.Width}x{panel.Height}) could not be placed");
                }
            }
        }

        // Optionally throw exception if any failures
        if (failedPanels.Count > 0)
        {
            throw new NestingException($"{failedPanels.Count} panel(s) could not be placed");
        }
    }

    public List<Panel> GetFailedPanels() => failedPanels;
}
```

---

## Summary Table

| Bug # | Issue | Severity | Line(s) | Fix Complexity |
|-------|-------|----------|---------|----------------|
| 1 | Non-guillotine cuts | 🔴 CRITICAL | 556-730 | HIGH - Algorithm redesign |
| 2 | Wrong tolerance direction | 🔴 CRITICAL | 205-208 | LOW - One line change |
| 3 | No boundary validation | 🔴 CRITICAL | 531-551 | LOW - Add validation |
| 4 | Lost panels | 🔴 CRITICAL | 362-378 | MEDIUM - Add tracking + exceptions |
| 5 | Operator precedence | 🟡 MEDIUM | 497-498 | LOW - Add parentheses |
| 6 | Greedy placement | 🟡 LOW | 420-452 | MEDIUM - Try all options |
| 7 | Performance | 🟡 LOW | Various | MEDIUM - Optimize data structures |
| 8 | Input validation | 🟡 LOW | GH script | LOW - Add null checks |

---

## Recommendations

1. **Immediate Actions:**
   - ✅ Fix Bug #2 (tolerance) - 1 line change
   - ✅ Fix Bug #5 (parentheses) - 1 line change
   - ✅ Add boundary validation (Bug #3) - 5-10 lines

2. **High Priority:**
   - ⚠️ Fix Bug #1 (guillotine cuts) - Requires algorithm redesign
   - ⚠️ Fix Bug #4 (track failures) - Add error handling infrastructure

3. **Testing:**
   - 🧪 Create comprehensive test suite covering all bugs
   - 🧪 Add integration tests with real-world scenarios
   - 🧪 Add boundary condition tests

4. **Long Term:**
   - 🔄 Optimize placement strategy (Bug #6)
   - 🔄 Improve performance (Bug #7)
   - 🔄 Better input validation (Bug #8)

---

**Next Steps:** Would you like me to:
1. Create a test suite for these bugs?
2. Implement the critical fixes (#1-#4)?
3. Create a refactored version of the algorithm?
