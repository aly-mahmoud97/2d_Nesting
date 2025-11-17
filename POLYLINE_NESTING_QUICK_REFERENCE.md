# Polyline Nesting - Quick Reference Card

## Grasshopper Input Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| **Polylines** | Polyline (List) | - | Closed polyline curves to nest |
| **SheetWidth** | Number | - | Sheet width (mm) |
| **SheetHeight** | Number | - | Sheet height (mm) |
| **Margin** | Number | 5.0 | Distance from sheet edge (mm) |
| **Spacing** | Number | 2.0 | Gap between polylines (mm) |
| **SortStrategy** | Integer | 0 | Sorting method (0-4) |
| **PlacementStrategy** | Integer | 0 | Placement method (0-2) |
| **RotationMode** | Integer | 1 | Rotation options (0-3) |
| **RotationStep** | Number | 15.0 | Custom rotation step (degrees) |
| **GridResolution** | Number | 1.0 | Placement grid size (mm) |
| **Tags** | Text (List) | - | Optional labels for items |
| **Run** | Boolean | False | Execute nesting |

---

## Grasshopper Outputs

| Output | Type | Description |
|--------|------|-------------|
| **PlacedPolylines** | Polyline | Nested polylines in final positions |
| **BoundingBoxes** | Rectangle | Bounding boxes around items |
| **Sheets** | Rectangle | Sheet boundaries |
| **Margins** | Rectangle | Margin visualization |
| **Info** | Text | Details for each item |
| **Statistics** | Text | Efficiency metrics |
| **Warnings** | Text | Errors and warnings |
| **SheetCount** | Integer | Number of sheets used |
| **Colors** | Color | Distinct colors per item |

---

## Sort Strategies (SortStrategy Parameter)

| Value | Strategy | Use Case |
|-------|----------|----------|
| **0** | AreaDescending | **[Default]** Largest items first - best for mixed sizes |
| **1** | AreaAscending | Smallest items first - fills small gaps |
| **2** | LargestDimensionFirst | By max dimension - good for long pieces |
| **3** | PerimeterDescending | By perimeter - complex shapes |
| **4** | ComplexityDescending | Most vertices first |

---

## Placement Strategies (PlacementStrategy Parameter)

| Value | Strategy | Speed | Efficiency | Use Case |
|-------|----------|-------|------------|----------|
| **0** | BottomLeft | ⚡⚡⚡ Fast | ⭐⭐⭐ Good | **[Default]** General purpose |
| **1** | BestFit | ⚡⚡ Moderate | ⭐⭐⭐⭐ Better | Maximum utilization |
| **2** | Grid | ⚡⚡⚡ Fast | ⭐⭐ Lower | Uniform layout |

---

## Rotation Modes (RotationMode Parameter)

| Value | Mode | Angles Tested | Speed | Use Case |
|-------|------|---------------|-------|----------|
| **0** | None | 1 (0°) | ⚡⚡⚡ Fastest | Text, logos, oriented parts |
| **1** | Rotation90 | 4 (0°, 90°, 180°, 270°) | ⚡⚡ Fast | **[Default]** General use |
| **2** | Rotation45 | 8 (every 45°) | ⚡ Moderate | Diagonal shapes |
| **3** | CustomStep | Varies | ⚙️ Custom | High precision (uses RotationStep) |

### CustomStep Examples

| RotationStep | Angles Tested | Speed | Use Case |
|--------------|---------------|-------|----------|
| 30° | 12 | ⚡⚡ Fast | Quick improvement |
| 15° | 24 | ⚡ Moderate | Good balance |
| 10° | 36 | 🐌 Slow | High precision |
| 5° | 72 | 🐌🐌 Very slow | Maximum packing |

---

## Performance Settings

### Fast Preview
```
GridResolution: 5.0
RotationMode: 1 (Rotation90)
PlacementStrategy: 0 (BottomLeft)
```
⏱️ Processing: **Fast**
📊 Efficiency: **Good**

### Balanced (Recommended)
```
GridResolution: 1.0
RotationMode: 1 (Rotation90)
PlacementStrategy: 0 (BottomLeft)
SortStrategy: 0 (AreaDescending)
```
⏱️ Processing: **Moderate**
📊 Efficiency: **Very Good**

### Maximum Efficiency
```
GridResolution: 0.5
RotationMode: 3 (CustomStep)
RotationStep: 10.0
PlacementStrategy: 1 (BestFit)
SortStrategy: 0 (AreaDescending)
```
⏱️ Processing: **Slow**
📊 Efficiency: **Excellent**

---

## Common Settings by Application

### CNC Router (Wood, Plastics)
```
Margin: 10.0
Spacing: 5.0
SortStrategy: 0 (AreaDescending)
PlacementStrategy: 0 (BottomLeft)
RotationMode: 1 (Rotation90)
GridResolution: 2.0
```

### Laser Cutter (Acrylic, Wood)
```
Margin: 5.0
Spacing: 2.0
SortStrategy: 0 (AreaDescending)
PlacementStrategy: 1 (BestFit)
RotationMode: 1 (Rotation90)
GridResolution: 1.0
```

### Waterjet (Metal)
```
Margin: 10.0
Spacing: 3.0
SortStrategy: 0 (AreaDescending)
PlacementStrategy: 1 (BestFit)
RotationMode: 3 (CustomStep)
RotationStep: 15.0
GridResolution: 1.0
```

### Vinyl Cutter (Stickers, Decals)
```
Margin: 5.0
Spacing: 1.0
SortStrategy: 0 (AreaDescending)
PlacementStrategy: 0 (BottomLeft)
RotationMode: 0 (None) [if orientation matters]
GridResolution: 0.5
```

### Signs & Letters
```
Margin: 10.0
Spacing: 5.0
SortStrategy: 0 (AreaDescending)
PlacementStrategy: 0 (BottomLeft)
RotationMode: 0 (None) [preserve orientation]
GridResolution: 2.0
```

---

## Typical Efficiency by Application

| Application | Expected Efficiency | Notes |
|-------------|---------------------|-------|
| Rectangular parts | 75-90% | Use BeamSaw instead for best results |
| Simple shapes (circles, ellipses) | 70-85% | Good packing possible |
| Complex irregular shapes | 60-75% | Depends on shape variety |
| Letters and text | 50-70% | Lots of negative space |
| Mixed sizes (large + small) | 70-85% | Small items fill gaps |
| Similar-sized items | 65-80% | Less gap filling |

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| **Items won't place** | • Reduce Margin/Spacing<br>• Check if items too large<br>• Verify polylines are closed |
| **Low efficiency (<60%)** | • Try SortStrategy=0<br>• Enable rotation (RotationMode≥1)<br>• Reduce Spacing<br>• Try PlacementStrategy=1 |
| **Slow processing** | • Increase GridResolution to 5.0<br>• Use RotationMode=1 instead of 3<br>• Simplify polylines (reduce vertices) |
| **Items overlap** | • Increase Spacing<br>• Check polylines are properly closed |
| **Too many sheets** | • Reduce Margin/Spacing<br>• Enable more rotation options<br>• Try different SortStrategy |

---

## C# Code Snippet

### Basic Usage
```csharp
using PolylineNesting;

// Create items
List<PolylineItem> items = new List<PolylineItem>();
foreach (var poly in polylines)
    items.Add(new PolylineItem(poly, items.Count));

// Create algorithm
var algo = new PolylineNestingAlgorithm(
    1200, 800,           // Sheet: 1200×800
    margin: 5.0,         // 5mm margin
    spacing: 2.0         // 2mm spacing
);

// Run and get results
algo.Nest(items);
var placed = algo.GetPlacedGeometries();
var stats = algo.GetStatistics();
```

### Advanced Usage
```csharp
// Custom settings per item
var item1 = new PolylineItem(
    geometry: poly1,
    id: 1,
    tag: "PART-001",
    rotation: RotationMode.None  // Don't rotate
);

var item2 = new PolylineItem(
    geometry: poly2,
    id: 2,
    tag: "PART-002",
    rotation: RotationMode.Rotation90  // Allow rotation
);

// High-efficiency algorithm
var algo = new PolylineNestingAlgorithm(
    sheetWidth: 2400,
    sheetHeight: 1200,
    margin: 5.0,
    spacing: 2.0,
    sortStrategy: PolylineSortStrategy.AreaDescending,
    placementStrategy: PlacementStrategy.BestFit,
    defaultRotationMode: RotationMode.CustomStep,
    rotationStepDegrees: 15.0,
    gridResolution: 1.0
);

algo.Nest(new List<PolylineItem> { item1, item2 });
```

---

## Key Differences from BeamSaw

| Feature | BeamSaw | Polyline Nesting |
|---------|---------|------------------|
| **Geometry** | Rectangles only | Any closed polyline |
| **Rotation** | 0° or 90° | 0° to 360° (configurable) |
| **Grain control** | ✅ Yes | ❌ No |
| **Kerf/blade width** | ✅ Yes | ❌ No (use Spacing instead) |
| **Cut sequence** | ✅ Yes | ❌ No |
| **Guillotine cuts** | ✅ Yes | ❌ No |
| **Speed** | ⚡⚡⚡ Very fast | ⚡ Moderate |
| **Collision detection** | Simple (rectangles) | Complex (polylines) |
| **Best for** | Sheet goods | CNC/Laser cutting |

---

## Formulas

### Sheet Count Estimate
```
Estimated Sheets = ⌈(Total Item Area) / (Usable Sheet Area × Expected Efficiency)⌉

Usable Sheet Area = (Width - 2×Margin) × (Height - 2×Margin)
Expected Efficiency = 0.70 to 0.85 (depending on settings)
```

### Example
```
Sheet: 2400×1200 mm
Margin: 5 mm
Items: 50 parts, total area = 1,800,000 mm²

Usable = (2400-10) × (1200-10) = 2,842,100 mm²
Estimated Sheets = 1,800,000 / (2,842,100 × 0.75) ≈ 0.85 ≈ 1 sheet
```

---

## Best Practices

1. **Start Simple**: Use defaults first, then optimize
2. **Preview Fast**: GridResolution=5 for quick checks
3. **Sort Matters**: AreaDescending works best for most cases
4. **Enable Rotation**: Unless orientation is critical
5. **Watch Spacing**: Too small = collisions, too large = waste
6. **Simplify Geometry**: Reduce vertices for speed
7. **Batch Large Jobs**: Process 50-100 items at a time
8. **Export Statistics**: Track efficiency over time
9. **Verify Results**: Check for collisions visually
10. **Document Settings**: Record parameters for reproducibility

---

## Keyboard Shortcuts in Grasshopper

| Action | Shortcut |
|--------|----------|
| Enable/disable component | **Ctrl+E** |
| Preview on/off | **Ctrl+Q** |
| Zoom selected | **Ctrl+M** |
| Create number slider | Double-click + type "slider" |
| Create panel | Double-click + type "panel" |
| Create toggle | Double-click + type "toggle" |

---

## Getting Help

1. **Check warnings output** - Often explains the issue
2. **Review statistics** - See efficiency and sheet count
3. **Test with simple geometry** - Isolate the problem
4. **Adjust one parameter at a time** - Find the cause
5. **Consult full guide** - See `POLYLINE_NESTING_GUIDE.md`

---

## Version Info

**Version**: 1.0.0
**Date**: 2025
**Platform**: Rhino 8 + Grasshopper
**Language**: C# (.NET)

---

**END OF QUICK REFERENCE**
