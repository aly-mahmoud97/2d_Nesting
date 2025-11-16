# Beam Saw Nesting Algorithm - Grasshopper C# Setup Guide

## Overview

This implementation provides a strict **guillotine cutting algorithm** (beam saw simulation) for 2D panel nesting in Rhino 8 Grasshopper. It follows real-world beam saw constraints where every cut must run completely through the material.

## Key Features

- **Strict Guillotine Cutting**: Every cut runs completely through the sheet or sub-sheet
- **Kerf Compensation**: Accounts for saw blade thickness
- **Wood Grain Direction**: Respects grain constraints for each panel and sheet
- **Rotation Control**: Allows or restricts 90° rotation per panel
- **Cut Sequence**: Generates ordered manufacturing instructions
- **Multiple Strategies**: Various sorting and cutting strategies

## Quick Start

### Step 1: Create C# Script Component

1. Open Grasshopper in Rhino 8
2. Add a **C# Script** component to your canvas (Params → Script → C# Script)
3. Double-click the component to open the editor
4. Copy the **entire contents** of `GrasshopperBeamSawNesting.cs` into the editor
5. Click "OK" to compile

### Step 2: Configure Inputs

Right-click the C# component → **Manage Inputs**. Add the following inputs:

| Name | Type Hint | Access | Description |
|------|-----------|--------|-------------|
| `SheetWidth` | double | item | Width of the sheet material |
| `SheetHeight` | double | item | Height of the sheet material |
| `SheetGrain` | string | item | "Horizontal" or "Vertical" |
| `PanelWidths` | double | list | List of panel widths |
| `PanelHeights` | double | list | List of panel heights |
| `RotationAllowed` | bool | list | Can each panel rotate 90°? (optional) |
| `PanelGrains` | string | list | Grain per panel (optional) |
| `Kerf` | double | item | Saw blade thickness (default: 5.0) |
| `CutOrientation` | string | item | "Horizontal" or "Vertical" (optional) |
| `SortStrategy` | string | item | Panel sorting strategy (optional) |
| `Run` | bool | item | Set to true to run |

### Step 3: Configure Outputs

Right-click the C# component → **Manage Outputs**. The outputs should already be set:

| Name | Description |
|------|-------------|
| `PlacedRectangles` | Rectangles showing placed panel positions |
| `PanelInfo` | Detailed info for each placed panel |
| `SheetRectangles` | Sheet boundaries |
| `CutLines` | All guillotine cut lines |
| `KerfRegions` | Visual representation of material removed |
| `RemainingSheets` | Unused sub-sheet rectangles |
| `CutSequence` | Ordered cutting operations |
| `Statistics` | Summary statistics |
| `Debug` | Debug messages |

## Input Details

### Required Inputs

#### SheetWidth & SheetHeight
- Type: `double`
- Description: Dimensions of your sheet material (e.g., plywood, MDF)
- Example: `1200` (width) × `800` (height) mm

#### SheetGrain
- Type: `string`
- Values: `"Horizontal"` or `"Vertical"`
- Description: The grain direction of the sheet material
- Default: `"Horizontal"` if not specified

#### PanelWidths & PanelHeights
- Type: `List<double>`
- Description: Lists of panel dimensions to nest
- **Important**: Both lists must have the same count
- Example:
  ```
  PanelWidths:  [300, 450, 200, 350]
  PanelHeights: [200, 300, 150, 400]
  ```

#### Run
- Type: `bool`
- Description: Set to `true` to execute the algorithm
- Use a **Boolean Toggle** component

### Optional Inputs

#### RotationAllowed
- Type: `List<bool>`
- Description: Specifies if each panel can be rotated 90°
- Default: All panels can rotate if not specified
- Example: `[true, false, true, true]` - 2nd panel cannot rotate

#### PanelGrains
- Type: `List<string>`
- Values: `"MatchSheet"`, `"FixedHorizontal"`, or `"FixedVertical"`
- Description: Grain constraint for each panel
- Default: `"MatchSheet"` if not specified
- Example: `["MatchSheet", "FixedHorizontal", "MatchSheet", "FixedVertical"]`

**Grain Direction Rules:**
- `MatchSheet`: Panel must align with sheet grain
- `FixedHorizontal`: Panel must be horizontal (width ≥ height)
- `FixedVertical`: Panel must be vertical (height > width)

#### Kerf
- Type: `double`
- Description: Thickness of the saw blade (material removed per cut)
- Default: `5.0` mm
- Typical values: 2-10mm depending on saw type
- Important: Kerf is applied to **every cut**

#### CutOrientation
- Type: `string`
- Values: `"Horizontal"` or `"Vertical"`
- Description: Preferred first cut direction
- Default: `"Horizontal"`
- Note: Algorithm may override based on optimal space usage

#### SortStrategy
- Type: `string`
- Values:
  - `"LargestFirst"` - Sort by largest dimension first
  - `"SmallestFirst"` - Sort by smallest dimension first
  - `"AreaDescending"` - Sort by area (largest first) **[DEFAULT]**
  - `"AreaAscending"` - Sort by area (smallest first)
- Description: How panels are ordered before nesting
- Recommendation: `"AreaDescending"` typically gives best results

## Output Details

### PlacedRectangles
- Type: `List<Rectangle3d>`
- Description: Rectangles representing placed panels
- Use for: Visualization, baking for cutting paths

### PanelInfo
- Type: `List<string>`
- Description: Detailed information about each placed panel
- Format: `"Panel {id}: Pos=({x},{y}), Size={w}x{h}, Rotation={deg}°, Grain={dir}, Sheet={idx}"`
- Example: `"Panel 0: Pos=(0.0,0.0), Size=300.0x200.0, Rotation=0°, Grain=Horizontal, Sheet=0"`

### SheetRectangles
- Type: `List<Rectangle3d>`
- Description: Boundary rectangles for each sheet used
- Use for: Visualizing total material usage

### CutLines
- Type: `List<Line>`
- Description: All guillotine cut lines
- Important: These are **full-length cuts** that run completely through material
- Use for: Manufacturing visualization

### KerfRegions
- Type: `List<Rectangle3d>`
- Description: Rectangles representing material removed by saw blade
- Use for: Visualizing wasted material

### RemainingSheets
- Type: `List<Rectangle3d>`
- Description: Unused sub-sheet regions
- Use for: Understanding scrap pieces

### CutSequence
- Type: `List<string>`
- Description: Ordered list of cutting operations
- Format: `"Step {n}: {Orientation} cut at {Axis}={position}"`
- Example:
  ```
  Step 1: Horizontal cut at Y=200.00
  Step 2: Vertical cut at X=300.00
  Step 3: Horizontal cut at Y=505.00
  ```
- Use for: Manufacturing instructions

### Statistics
- Type: `List<string>`
- Description: Summary statistics including:
  - Total sheets used
  - Panels placed
  - Total cuts made
  - Overall efficiency percentage
  - Per-sheet efficiency

### Debug
- Type: `string`
- Description: Debug messages and error information
- Always check this output for warnings or errors

## Example Workflows

### Example 1: Simple Rectangular Panels

```
Components needed:
- Number Slider → SheetWidth (1200)
- Number Slider → SheetHeight (800)
- Panel with "Horizontal" → SheetGrain
- Panel with values [300, 450, 200] → PanelWidths
- Panel with values [200, 300, 150] → PanelHeights
- Number Slider → Kerf (5)
- Boolean Toggle (true) → Run
```

### Example 2: With Grain Constraints

```
Additional components:
- Panel with values [true, false, true] → RotationAllowed
  (2nd panel cannot rotate)
- Panel with ["MatchSheet", "FixedHorizontal", "FixedVertical"] → PanelGrains
  (Different grain rules per panel)
```

### Example 3: Furniture Parts

```
Example furniture cutting list:
SheetWidth: 2440 (8' plywood sheet)
SheetHeight: 1220 (4' plywood sheet)
SheetGrain: "Horizontal"
Kerf: 3 (table saw kerf)

Panels (in mm):
- Tabletop: 1200 × 600 (must be horizontal)
- Side panels (2×): 700 × 300 (can rotate)
- Shelves (3×): 400 × 250 (can rotate)

PanelWidths: [1200, 700, 700, 400, 400, 400]
PanelHeights: [600, 300, 300, 250, 250, 250]
RotationAllowed: [false, true, true, true, true, true]
PanelGrains: ["FixedHorizontal", "MatchSheet", "MatchSheet", ...]
```

## Understanding Guillotine Cutting

### What is Guillotine Cutting?

Guillotine cutting (G1/G2) means:
1. Every cut is a **straight line**
2. Every cut runs **completely through** the sheet or sub-sheet
3. Each cut **divides** the material into **two rectangles**
4. Cuts **cannot stop midway** or be curved

### Example Cut Sequence

```
Initial Sheet: 1200×800

Step 1: Place Panel (300×200) at (0,0)
Step 2: Horizontal cut at Y=200
        → Creates: Top sub-sheet (1200×595)
Step 3: Vertical cut at X=300
        → Creates: Right sub-sheet (895×200)

Step 4: Place Panel (450×300) in top sub-sheet at (0,205)
Step 5: Horizontal cut at Y=505
        → Creates: Top sub-sheet (1200×290)
Step 6: Vertical cut at X=450
        → Creates: Right sub-sheet (745×300)

... and so on
```

### Why Kerf Matters

The **kerf** is the material **removed** by the saw blade:
- 5mm kerf means 5mm is lost on every cut
- A 1200mm sheet with 10 cuts loses 50mm total
- Algorithm accounts for this automatically

## Grain Direction Explained

### Sheet Grain

Wood grain runs in one direction:
- **Horizontal**: Grain runs left-right (along width)
- **Vertical**: Grain runs top-bottom (along height)

### Panel Grain Rules

1. **MatchSheet**: Panel grain must match sheet grain
   - If sheet is horizontal, panel's longer dimension must be horizontal
   - Ensures consistent grain across all panels

2. **FixedHorizontal**: Panel must be oriented horizontally
   - Panel's width must be ≥ height
   - Used for parts where horizontal grain is structural

3. **FixedVertical**: Panel must be oriented vertically
   - Panel's height must be > width
   - Used for parts where vertical grain is needed

### Rotation + Grain Validation

The algorithm checks:
1. Can the panel physically fit?
2. If rotated, does it still satisfy grain constraints?
3. If grain constraint fails, try different orientation or skip

## Tips for Best Results

### 1. Start with Realistic Data
```
✓ Use actual sheet sizes (e.g., 2440×1220 for plywood)
✓ Use realistic kerf values (2-5mm for most saws)
✓ Test with 5-20 panels first
```

### 2. Sort Panels Strategically
```
AreaDescending (default): Usually best for mixed sizes
LargestFirst: Good for fitting large panels first
SmallestFirst: Experimental, may reduce waste
```

### 3. Grain Direction Best Practices
```
✓ Set sheet grain to match your material
✓ Use MatchSheet for most panels
✓ Use Fixed{H/V} only when structurally required
✗ Don't over-constrain - reduces nesting efficiency
```

### 4. Visualize Results
```
Connect outputs to Preview components:
- PlacedRectangles → Color (green)
- CutLines → Color (red)
- KerfRegions → Color (yellow)
- RemainingSheets → Color (gray)
```

### 5. Check Debug Output
```
Always connect Debug output to a Panel component
Look for:
- "SUCCESS: Algorithm completed"
- Efficiency percentages
- Warnings about panels that didn't fit
```

## Common Issues & Solutions

### Issue: "Panels placed: 5 / 10"

**Cause**: Some panels didn't fit on available sheets

**Solutions**:
1. Check if panels are too large for sheet
2. Verify grain constraints aren't over-restrictive
3. Add more sheets (algorithm auto-adds as needed)
4. Reduce kerf if unrealistically large

### Issue: Low Efficiency (<50%)

**Causes**:
- Panel sizes don't pack well together
- Too much kerf waste
- Grain constraints too restrictive

**Solutions**:
1. Try different sort strategies
2. Reduce kerf if set too high
3. Allow rotation where possible
4. Consider re-designing panel sizes

### Issue: "Panel X is too large for sheet"

**Cause**: Panel dimensions exceed sheet dimensions

**Solutions**:
1. Increase sheet size
2. Check if rotation would help
3. Split large panel into smaller ones
4. Verify input data is correct

### Issue: Grain Constraint Failures

**Symptom**: Many panels not placed despite space available

**Solutions**:
1. Review PanelGrains list
2. Use "MatchSheet" as default
3. Only use Fixed{H/V} when absolutely necessary
4. Verify SheetGrain is set correctly

## Performance Notes

- **Fast**: 10-50 panels → <1 second
- **Medium**: 50-200 panels → 1-5 seconds
- **Slower**: 200+ panels → 5-30 seconds

Performance depends on:
- Number of panels
- Complexity of grain constraints
- Number of sheets used

## Manufacturing Workflow

### 1. Design Phase (Grasshopper)
```
Define your panels → Run algorithm → Review results
```

### 2. Verification
```
Check Statistics output:
- Efficiency acceptable? (>70% is good)
- All panels placed?
- Review Debug for warnings
```

### 3. Export for Cutting
```
1. Bake PlacedRectangles to Rhino layer "Panels"
2. Bake CutLines to layer "Cuts"
3. Add labels/dimensions
4. Export to DXF or other CAM format
```

### 4. Manufacturing
```
Use CutSequence output as cutting instructions:
- Follow step-by-step order
- Each cut goes completely through material
- Account for kerf in setup
```

## Advanced Configuration

### Custom Panel Priorities

To prioritize specific panels, sort your input lists manually:
```
Example: Place critical panels first
PanelWidths: [1200, 700, 400, ...]  ← Tabletop first
             ↑ Most important
```

### Multiple Sheet Sizes

For future enhancement, algorithm could support:
```
SheetWidths: [1200, 2440]
SheetHeights: [800, 1220]
→ Algorithm chooses best sheet per panel
```

*Note: Current version uses single sheet size*

### Optimizing for Minimal Cuts

Use `CutOrientation` to influence cutting:
```
"Horizontal": Prefers horizontal cuts
"Vertical": Prefers vertical cuts

Choose based on:
- Saw setup (which direction is easier)
- Grain direction
- Desired scrap piece sizes
```

## Algorithm Theory

### Guillotine Cutting Classes

This implementation is **G1/G2** (Guillotine):
- **G1**: All cuts through entire sheet (what we implement)
- **G2**: Cuts through sub-sheets only
- **Non-guillotine**: Cuts can stop midway (NOT supported by beam saws)

### Best-Fit Sub-Sheet Selection

Algorithm uses **best-fit decreasing**:
1. Sort panels by area (largest first)
2. For each panel, find smallest sub-sheet that fits
3. Place panel in that sub-sheet
4. Guillotine cut to create new sub-sheets

This approach minimizes wasted space while respecting constraints.

### Complexity

- Time: O(n × m) where n = panels, m = sub-sheets
- Space: O(n + m)
- Typical m ≈ 2n for guillotine cutting

## File Structure

```
BeamSawNestingAlgorithm.cs
  - Core algorithm classes (Panel, SubSheet, CutLine, etc.)
  - Main nesting logic
  - Standalone C# file (can be used outside Grasshopper)

GrasshopperBeamSawNesting.cs
  - Complete Grasshopper C# Script component
  - Includes all algorithm code
  - Grasshopper-specific input/output handling
  - Ready to copy-paste into Grasshopper

BEAM_SAW_SETUP_GUIDE.md
  - This documentation file
```

## Troubleshooting Checklist

- [ ] Rhino 8 with Grasshopper installed?
- [ ] C# Script component compiles without errors?
- [ ] All required inputs connected?
- [ ] PanelWidths.Count == PanelHeights.Count?
- [ ] SheetWidth and SheetHeight > 0?
- [ ] Kerf >= 0?
- [ ] Run input set to `true`?
- [ ] Checked Debug output for messages?

## References

**Guillotine Cutting Problem**:
- [Wikipedia: Cutting stock problem](https://en.wikipedia.org/wiki/Cutting_stock_problem)
- Beam saw constraints: All cuts must be through-cuts
- Wood grain direction: Material science consideration

**Implementation Notes**:
- Language: C#
- Platform: Rhino 8 Grasshopper
- Dependencies: Rhino.Geometry (built-in)
- Algorithm: Best-fit decreasing with guillotine constraints

## Support & Further Development

**Current Features**:
- ✅ Strict guillotine cutting
- ✅ Kerf compensation
- ✅ Grain direction validation
- ✅ Rotation constraints
- ✅ Cut sequence generation
- ✅ Multiple sheets support

**Potential Enhancements**:
- Multiple sheet sizes
- Panel priorities/weights
- Cost optimization
- 3D visualization
- Export to CNC formats
- Optimization algorithms (genetic, simulated annealing)

## Example Output

```
=== BEAM SAW NESTING ALGORITHM ===
Sheet: 1200 x 800
Panels to nest: 6
Kerf: 5
Sheet grain: Horizontal

=== NESTING RESULTS ===
Sheets used: 1
Panels placed: 6 / 6
Overall efficiency: 78.45%
Total cuts: 11

Statistics:
Total sheets: 1
Panels placed: 6 / 6
Total cuts: 11
Overall efficiency: 78.45%
Sheet 0 efficiency: 78.45%

SUCCESS: Algorithm completed
```

---

**Author**: Claude Code
**Date**: 2025-11-16
**Version**: 1.0
**License**: Open source for educational and commercial use
