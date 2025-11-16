# Beam Saw Nesting - Quick Reference Card

## 🚀 Quick Start (30 seconds)

1. **Add C# Script component** to Grasshopper
2. **Copy-paste** entire `GrasshopperBeamSawNesting.cs` file
3. **Connect inputs**:
   - SheetWidth, SheetHeight (numbers)
   - PanelWidths, PanelHeights (lists)
   - Run (boolean toggle = true)
4. **View outputs**: PlacedRectangles, CutLines, Statistics

## 📋 Minimum Required Inputs

```
SheetWidth    → 1200 (number slider)
SheetHeight   → 800 (number slider)
SheetGrain    → "Horizontal" (panel)
PanelWidths   → [300, 450, 200] (panel)
PanelHeights  → [200, 300, 150] (panel)
Kerf          → 5 (number slider)
Run           → true (boolean toggle)
```

## 🎛️ Input Quick Reference

| Input | Type | Default | Example |
|-------|------|---------|---------|
| **SheetWidth** | double | *required* | 1200 |
| **SheetHeight** | double | *required* | 800 |
| **SheetGrain** | string | "Horizontal" | "Vertical" |
| **PanelWidths** | list | *required* | [300, 450, 200] |
| **PanelHeights** | list | *required* | [200, 300, 150] |
| **RotationAllowed** | list | all true | [true, false, true] |
| **PanelGrains** | list | "MatchSheet" | ["MatchSheet", "FixedH"] |
| **Kerf** | double | 5.0 | 3.0 |
| **CutOrientation** | string | "Horizontal" | "Vertical" |
| **SortStrategy** | string | "AreaDescending" | "LargestFirst" |
| **Run** | bool | false | true |

## 📤 Output Quick Reference

| Output | Type | Use For |
|--------|------|---------|
| **PlacedRectangles** | Rectangles | Panel positions (bake for cutting) |
| **PanelInfo** | Strings | Detailed panel data |
| **SheetRectangles** | Rectangles | Sheet boundaries |
| **CutLines** | Lines | Guillotine cuts (for manufacturing) |
| **KerfRegions** | Rectangles | Material waste visualization |
| **RemainingSheets** | Rectangles | Scrap pieces |
| **CutSequence** | Strings | Manufacturing instructions |
| **Statistics** | Strings | Efficiency, sheet count, etc. |
| **Debug** | String | Error messages / warnings |

## 🔧 Common Configurations

### Standard Plywood Cutting
```
SheetWidth: 2440
SheetHeight: 1220
SheetGrain: "Horizontal"
Kerf: 3
CutOrientation: "Horizontal"
SortStrategy: "AreaDescending"
```

### MDF Panel Saw
```
SheetWidth: 1830
SheetHeight: 3660
SheetGrain: "Vertical"
Kerf: 5
CutOrientation: "Vertical"
SortStrategy: "LargestFirst"
```

### Small Parts Optimization
```
SheetWidth: 1200
SheetHeight: 800
Kerf: 2
SortStrategy: "SmallestFirst"
```

## 🌾 Grain Direction Values

### SheetGrain
- `"Horizontal"` - Grain runs left-right
- `"Vertical"` - Grain runs top-bottom

### PanelGrains (per panel)
- `"MatchSheet"` - Panel must align with sheet grain
- `"FixedHorizontal"` - Panel must be horizontal (W ≥ H)
- `"FixedVertical"` - Panel must be vertical (H > W)

## 📊 Sort Strategies

| Strategy | Best For |
|----------|----------|
| **AreaDescending** | Mixed sizes (DEFAULT - best overall) |
| **LargestFirst** | When large panels are priority |
| **SmallestFirst** | Experimental - may reduce waste |
| **AreaAscending** | Filling gaps with small panels first |

## ⚡ Performance Guide

| Panel Count | Expected Time |
|-------------|---------------|
| 10-50 | <1 second |
| 50-200 | 1-5 seconds |
| 200+ | 5-30 seconds |

## ✅ Efficiency Targets

| Efficiency | Rating |
|------------|--------|
| >80% | Excellent |
| 70-80% | Good |
| 60-70% | Acceptable |
| <60% | Review design/constraints |

## 🎨 Visualization Setup

```
Connect to Preview components with colors:

PlacedRectangles  → Green   (panels)
CutLines          → Red     (saw cuts)
KerfRegions       → Yellow  (waste)
RemainingSheets   → Gray    (scrap)
SheetRectangles   → Blue    (sheet outline)
```

## 🐛 Troubleshooting Quick Fixes

| Problem | Solution |
|---------|----------|
| "Panels placed: 0 / X" | Check Debug output, verify inputs |
| Low efficiency | Try different SortStrategy |
| Panels don't fit | Increase sheet size or allow rotation |
| Grain errors | Use "MatchSheet" for most panels |
| Too many sheets | Check if panels too large or kerf too high |

## 📐 Example: Furniture Cut List

```
// Kitchen cabinet parts
SheetWidth: 2440
SheetHeight: 1220
SheetGrain: "Horizontal"
Kerf: 3

PanelWidths:  [600, 600, 500, 500, 400, 400, 300]
PanelHeights: [700, 700, 350, 350, 250, 250, 200]

RotationAllowed: [false, false, true, true, true, true, true]
                  ↑ Door panels cannot rotate

PanelGrains: ["FixedHorizontal", "FixedHorizontal",
              "MatchSheet", "MatchSheet", ...]
              ↑ Doors must be horizontal grain
```

## 🔄 Typical Workflow

```
1. Design → Define panel list
            ↓
2. Nest → Run algorithm
            ↓
3. Review → Check Statistics output
            ↓
4. Adjust → Try different strategies if needed
            ↓
5. Bake → Export PlacedRectangles
            ↓
6. Manufacture → Follow CutSequence
```

## ⚙️ Algorithm Parameters Impact

| Parameter | Increase → Effect |
|-----------|-------------------|
| **Kerf** | More waste, fewer panels per sheet |
| **Sheet Size** | More panels per sheet, higher efficiency |
| **Panel Size** | Fewer panels per sheet |
| **Rotation Allowed** | Better packing, higher efficiency |
| **Grain Constraints** | Lower efficiency, more sheets |

## 💡 Pro Tips

1. **Always check Debug output first** - tells you exactly what went wrong
2. **Start with AreaDescending** - best general-purpose strategy
3. **Use realistic kerf values** - typical: 2-5mm
4. **Allow rotation by default** - restrict only when necessary
5. **MatchSheet for grain** - unless structurally required
6. **Visualize with colors** - easier to verify results
7. **Connect Statistics to Panel** - quick efficiency check

## 📏 Common Sheet Sizes (mm)

| Material | Size | Notes |
|----------|------|-------|
| Plywood 4×8 | 1220 × 2440 | Most common |
| Plywood 5×5 | 1525 × 1525 | Square sheets |
| MDF 6×12 | 1830 × 3660 | Large format |
| OSB 4×8 | 1220 × 2440 | Standard |

## 🔍 Reading the Debug Output

```
=== BEAM SAW NESTING ALGORITHM ===
Sheet: 1200 x 800                     ← Your sheet size
Panels to nest: 6                     ← Total panels
Kerf: 5                               ← Blade thickness
Sheet grain: Horizontal               ← Grain direction

Panels created successfully            ← ✅ Inputs valid

=== NESTING RESULTS ===
Sheets used: 1                        ← Total sheets
Panels placed: 6 / 6                  ← All placed!
Overall efficiency: 78.45%            ← Good efficiency
Total cuts: 11                        ← Number of cuts

SUCCESS: Algorithm completed          ← ✅ No errors
```

## 🚨 Common Error Messages

| Message | Meaning | Fix |
|---------|---------|-----|
| "PanelWidths and PanelHeights must have the same count" | List length mismatch | Make lists equal length |
| "Panel X is too large for sheet" | Panel > Sheet | Increase sheet size |
| "SheetWidth and SheetHeight must be positive" | Invalid sheet size | Use positive numbers |
| "Kerf cannot be negative" | Invalid kerf | Use kerf ≥ 0 |

## 📦 Output Data Format

### PanelInfo Format
```
"Panel {id}: Pos=({x},{y}), Size={w}x{h}, Rotation={deg}°, Grain={dir}, Sheet={idx}"

Example:
"Panel 0: Pos=(0.0,0.0), Size=300.0x200.0, Rotation=0°, Grain=Horizontal, Sheet=0"
```

### CutSequence Format
```
"Step {n}: {Orientation} cut at {Axis}={position}"

Example:
"Step 1: Horizontal cut at Y=200.00"
"Step 2: Vertical cut at X=305.00"
```

## 🎯 When to Use What

### Use "Horizontal" Cut Orientation
- When saw is easier to operate horizontally
- When sheet grain is horizontal
- For longer continuous cuts

### Use "Vertical" Cut Orientation
- When saw setup favors vertical cuts
- When sheet grain is vertical
- For narrower panels

### Use "LargestFirst" Strategy
- When large panels are most important
- When you want large panels placed first
- For priority-based cutting

### Use "AreaDescending" Strategy
- Mixed panel sizes (DEFAULT)
- General-purpose nesting
- Best overall efficiency

## 🔗 File Reference

| File | Purpose |
|------|---------|
| `GrasshopperBeamSawNesting.cs` | **Copy this into Grasshopper** |
| `BeamSawNestingAlgorithm.cs` | Standalone C# library |
| `BEAM_SAW_SETUP_GUIDE.md` | Full documentation |
| `BEAM_SAW_QUICK_REFERENCE.md` | This file |

---

**💡 Remember**: Start simple, test with 5-10 panels, then scale up!
