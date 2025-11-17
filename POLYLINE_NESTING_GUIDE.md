# Polyline Nesting Algorithm - Complete Setup Guide

## Overview

The **Polyline Nesting Algorithm** is a 2D nesting solution for placing arbitrary polyline shapes (closed curves) onto rectangular sheets with minimal waste. Unlike the BeamSaw algorithm which handles only rectangular panels, this algorithm can nest complex geometric shapes like letters, logos, irregular parts, or any closed polyline.

### Key Features

- **Arbitrary Geometry Support**: Nest any closed polyline shape, not just rectangles
- **Flexible Rotation**: Support for no rotation, 90° rotation, 45° increments, or custom angle steps
- **Collision Detection**: Precise polyline-to-polyline intersection testing
- **Multiple Placement Strategies**: Bottom-left, best-fit, or grid placement
- **Spacing Control**: Configurable margins and spacing between items
- **Multi-Sheet Support**: Automatically adds sheets as needed
- **Color Coding**: Visual distinction using golden ratio color generation
- **Detailed Statistics**: Efficiency metrics and utilization reports

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Algorithm Parameters](#algorithm-parameters)
3. [Nesting Strategies](#nesting-strategies)
4. [Rotation Modes](#rotation-modes)
5. [Grasshopper Setup](#grasshopper-setup)
6. [Usage Examples](#usage-examples)
7. [Performance Tips](#performance-tips)
8. [Troubleshooting](#troubleshooting)
9. [Comparison with BeamSaw](#comparison-with-beamsaw)

---

## Quick Start

### Grasshopper Usage (Recommended)

1. **Create a C# Script Component** in Grasshopper
2. **Copy** the entire contents of `GrasshopperPolylineNesting.cs` into the component
3. **Configure inputs**:
   - `Polylines`: Your closed polyline curves
   - `SheetWidth`: Sheet width (e.g., 1200)
   - `SheetHeight`: Sheet height (e.g., 800)
   - `Run`: Set to `True` to execute

4. **View outputs**:
   - `PlacedPolylines`: Nested geometry
   - `Statistics`: Efficiency and utilization
   - `Warnings`: Any issues encountered

### Standalone C# Usage

```csharp
using PolylineNesting;
using Rhino.Geometry;

// Create polyline items
List<PolylineItem> items = new List<PolylineItem>();
foreach (var polyline in myPolylines)
{
    items.Add(new PolylineItem(polyline, id: items.Count, tag: "Part" + items.Count));
}

// Create algorithm
PolylineNestingAlgorithm algorithm = new PolylineNestingAlgorithm(
    sheetWidth: 1200,
    sheetHeight: 800,
    margin: 5.0,
    spacing: 2.0,
    sortStrategy: PolylineSortStrategy.AreaDescending,
    placementStrategy: PlacementStrategy.BottomLeft,
    defaultRotationMode: RotationMode.Rotation90,
    rotationStepDegrees: 15.0,
    gridResolution: 1.0
);

// Run nesting
algorithm.Nest(items);

// Get results
List<Polyline> placedGeometry = algorithm.GetPlacedGeometries();
List<string> statistics = algorithm.GetStatistics();
List<string> warnings = algorithm.GetWarnings();
int sheetCount = algorithm.GetSheetCount();
```

---

## Algorithm Parameters

### Constructor Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `sheetWidth` | `double` | *required* | Width of the nesting sheet (mm) |
| `sheetHeight` | `double` | *required* | Height of the nesting sheet (mm) |
| `margin` | `double` | `5.0` | Distance from sheet edges (mm) |
| `spacing` | `double` | `2.0` | Minimum spacing between polylines (mm) |
| `sortStrategy` | `PolylineSortStrategy` | `AreaDescending` | How to sort items before nesting |
| `placementStrategy` | `PlacementStrategy` | `BottomLeft` | Strategy for positioning items |
| `defaultRotationMode` | `RotationMode` | `Rotation90` | Allowed rotation angles |
| `rotationStepDegrees` | `double` | `15.0` | Custom rotation step size (degrees) |
| `gridResolution` | `double` | `1.0` | Placement grid resolution (mm) |

### PolylineItem Parameters

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Geometry` | `Polyline` | *required* | The polyline curve to nest |
| `Id` | `int` | `0` | Unique identifier |
| `Tag` | `string` | `""` | Custom label (e.g., part number) |
| `AllowedRotation` | `RotationMode` | `Rotation90` | Rotation constraint for this item |

---

## Nesting Strategies

### Sort Strategies

Determines the order in which polylines are processed:

#### 1. **AreaDescending** (Default, Recommended)
- Places largest items first
- Typically achieves highest efficiency
- Best for mixed-size items
```csharp
sortStrategy: PolylineSortStrategy.AreaDescending
```

#### 2. **AreaAscending**
- Places smallest items first
- Useful for filling small gaps
- May leave large items for later sheets
```csharp
sortStrategy: PolylineSortStrategy.AreaAscending
```

#### 3. **LargestDimensionFirst**
- Sorts by maximum bounding box dimension
- Good for long, narrow pieces
```csharp
sortStrategy: PolylineSortStrategy.LargestDimensionFirst
```

#### 4. **PerimeterDescending**
- Sorts by perimeter length
- Useful for complex shapes with long edges
```csharp
sortStrategy: PolylineSortStrategy.PerimeterDescending
```

#### 5. **ComplexityDescending**
- Sorts by number of vertices
- Places most complex shapes first
```csharp
sortStrategy: PolylineSortStrategy.ComplexityDescending
```

### Placement Strategies

Determines how items are positioned on the sheet:

#### 1. **BottomLeft** (Default, Recommended)
- Scans from bottom-left corner
- Tries positions row-by-row, column-by-column
- Fast and efficient
- **Use for**: General purpose nesting

```csharp
placementStrategy: PlacementStrategy.BottomLeft
```

**Algorithm**:
```
For each Y position (bottom to top):
    For each X position (left to right):
        If item fits without collision:
            Place item and continue to next item
```

#### 2. **BestFit**
- Tries to minimize total bounding box
- Attempts placement near existing items
- Slower but more compact results
- **Use for**: Maximizing sheet utilization

```csharp
placementStrategy: PlacementStrategy.BestFit
```

**Algorithm**:
```
1. Try BottomLeft first
2. Also try positions adjacent to existing items:
   - Right of each placed item
   - Above each placed item
3. Use first valid position found
```

#### 3. **Grid**
- Places items in a regular grid pattern
- Predictable, organized layout
- May waste space but easy to cut
- **Use for**: Uniform parts or manual cutting

```csharp
placementStrategy: PlacementStrategy.Grid
```

**Algorithm**:
```
Calculate grid cells based on cell size
For each grid position (row, col):
    If item fits in cell:
        Place item and continue
```

---

## Rotation Modes

### 1. **None** - No Rotation
```csharp
rotationMode: RotationMode.None
```
- Items placed in original orientation only
- Fastest processing
- Use when orientation matters (text, logos, etc.)

**Tested angles**: `0°`

---

### 2. **Rotation90** (Default)
```csharp
rotationMode: RotationMode.Rotation90
```
- Tries 4 orientations: 0°, 90°, 180°, 270°
- Good balance of flexibility and speed
- Suitable for most nesting tasks

**Tested angles**: `0°, 90°, 180°, 270°`

---

### 3. **Rotation45**
```csharp
rotationMode: RotationMode.Rotation45
```
- Tries 8 orientations at 45° intervals
- Better packing for diagonal shapes
- Slower than Rotation90

**Tested angles**: `0°, 45°, 90°, 135°, 180°, 225°, 270°, 315°`

---

### 4. **CustomStep**
```csharp
rotationMode: RotationMode.CustomStep
rotationStepDegrees: 15.0  // Try every 15°
```
- Custom angle increments
- Maximum flexibility
- Slowest option (24 angles at 15° step)
- Use for critical high-efficiency tasks

**Example with 15° step**: `0°, 15°, 30°, 45°, ..., 345°` (24 angles)
**Example with 30° step**: `0°, 30°, 60°, 90°, ..., 330°` (12 angles)

---

## Grasshopper Setup

### Input Configuration

Right-click the C# component and select **"Set Inputs"**:

| Input | Type | Access | Default | Required |
|-------|------|--------|---------|----------|
| `Polylines` | Polyline | List | - | Yes |
| `SheetWidth` | double | Item | - | Yes |
| `SheetHeight` | double | Item | - | Yes |
| `Margin` | double | Item | 5.0 | No |
| `Spacing` | double | Item | 2.0 | No |
| `SortStrategy` | int | Item | 0 | No |
| `PlacementStrategy` | int | Item | 0 | No |
| `RotationMode` | int | Item | 1 | No |
| `RotationStep` | double | Item | 15.0 | No |
| `GridResolution` | double | Item | 1.0 | No |
| `Tags` | string | List | - | No |
| `Run` | bool | Item | False | Yes |

### Output Configuration

Right-click the C# component and select **"Set Outputs"**:

| Output | Type | Description |
|--------|------|-------------|
| `PlacedPolylines` | Polyline | Nested polylines in final positions |
| `BoundingBoxes` | Rectangle3d | Bounding boxes of placed items |
| `Sheets` | Rectangle3d | Sheet boundary rectangles |
| `Margins` | Rectangle3d | Margin area visualization |
| `Info` | string | Detailed info for each placed item |
| `Statistics` | string | Overall nesting statistics |
| `Warnings` | string | Any warnings or errors |
| `SheetCount` | int | Number of sheets used |
| `Colors` | Color | Distinct colors for each polyline |

### Grasshopper Network Example

```
[Closed Curves] ──> Polylines
[Number Slider: 1200] ──> SheetWidth ─┐
[Number Slider: 800] ──> SheetHeight  ├──> [C# Polyline Nesting] ──> PlacedPolylines ──> [Preview]
[Number Slider: 5] ──> Margin         │                            └──> Statistics ──> [Panel]
[Number Slider: 2] ──> Spacing        │                            └──> Warnings ──> [Panel]
[Integer Slider: 0-4] ──> SortStrategy├                            └──> Colors ──> [Custom Preview]
[Integer Slider: 0-2] ──> PlacementStrategy
[Integer Slider: 0-3] ──> RotationMode ┘
[Toggle: True] ──> Run ─────────────────┘
```

---

## Usage Examples

### Example 1: Simple Letter Nesting

```csharp
// Create letter shapes (simplified)
List<Polyline> letters = CreateLetterPolylines("HELLO");

// Create items
List<PolylineItem> items = new List<PolylineItem>();
for (int i = 0; i < letters.Count; i++)
{
    items.Add(new PolylineItem(letters[i], i, $"Letter_{i}", RotationMode.None));
}

// Nest with no rotation (preserve letter orientation)
PolylineNestingAlgorithm algorithm = new PolylineNestingAlgorithm(
    sheetWidth: 1000,
    sheetHeight: 600,
    margin: 10,
    spacing: 5,
    rotationMode: RotationMode.None  // Don't rotate letters!
);

algorithm.Nest(items);
```

**Result**: Letters nested upright, easy to read

---

### Example 2: Irregular Parts with Rotation

```csharp
// Load irregular part geometries
List<Polyline> parts = LoadPartsFromFile("parts.csv");

// Create items with custom tags
List<PolylineItem> items = new List<PolylineItem>();
foreach (var part in parts)
{
    items.Add(new PolylineItem(part, items.Count, $"PART-{items.Count:D4}"));
}

// Nest with 90° rotation for efficiency
PolylineNestingAlgorithm algorithm = new PolylineNestingAlgorithm(
    sheetWidth: 2400,
    sheetHeight: 1200,
    margin: 5,
    spacing: 3,
    sortStrategy: PolylineSortStrategy.AreaDescending,
    placementStrategy: PlacementStrategy.BestFit,
    rotationMode: RotationMode.Rotation90,
    gridResolution: 2.0  // 2mm grid for faster processing
);

algorithm.Nest(items);

// Export results
ExportToCSV(algorithm.GetPlacedPolylines(), "nesting_results.csv");
PrintReport(algorithm.GetStatistics());
```

**Result**: High-efficiency nesting with automatic rotation

---

### Example 3: High-Precision Nesting

```csharp
// Critical parts requiring maximum efficiency
List<Polyline> criticalParts = GetCriticalParts();

List<PolylineItem> items = new List<PolylineItem>();
foreach (var part in criticalParts)
{
    items.Add(new PolylineItem(part, items.Count, "CRITICAL"));
}

// Use custom rotation step for best packing
PolylineNestingAlgorithm algorithm = new PolylineNestingAlgorithm(
    sheetWidth: 3000,
    sheetHeight: 1500,
    margin: 3,
    spacing: 1.5,
    sortStrategy: PolylineSortStrategy.AreaDescending,
    placementStrategy: PlacementStrategy.BestFit,
    rotationMode: RotationMode.CustomStep,
    rotationStepDegrees: 10.0,  // Try every 10° (36 angles)
    gridResolution: 0.5  // Fine grid (slower but precise)
);

algorithm.Nest(items);

double efficiency = CalculateEfficiency(algorithm);
Console.WriteLine($"Efficiency: {efficiency:F2}%");
```

**Result**: Maximum packing density (slower processing)

---

## Performance Tips

### Speed Optimization

1. **Grid Resolution**
   - Use `gridResolution: 5.0` for quick previews
   - Use `gridResolution: 1.0` for final results
   - Finer resolution = slower but more accurate

2. **Rotation Mode**
   - `None`: Fastest (1 angle)
   - `Rotation90`: Fast (4 angles)
   - `Rotation45`: Moderate (8 angles)
   - `CustomStep(15°)`: Slow (24 angles)
   - `CustomStep(10°)`: Very slow (36 angles)

3. **Placement Strategy**
   - `BottomLeft`: Fastest
   - `BestFit`: Moderate
   - `Grid`: Fastest (but lower efficiency)

4. **Sort Strategy**
   - All strategies have similar performance
   - Choose based on geometry, not speed

### Efficiency Optimization

For **maximum material utilization**:

```csharp
sortStrategy: PolylineSortStrategy.AreaDescending
placementStrategy: PlacementStrategy.BestFit
rotationMode: RotationMode.CustomStep
rotationStepDegrees: 10.0
gridResolution: 0.5
```

For **balanced speed and efficiency** (recommended):

```csharp
sortStrategy: PolylineSortStrategy.AreaDescending
placementStrategy: PlacementStrategy.BottomLeft
rotationMode: RotationMode.Rotation90
gridResolution: 1.0
```

For **fast preview**:

```csharp
sortStrategy: PolylineSortStrategy.AreaDescending
placementStrategy: PlacementStrategy.BottomLeft
rotationMode: RotationMode.Rotation90
gridResolution: 5.0
```

---

## Troubleshooting

### Problem: Items Won't Nest

**Symptoms**: Items fail to place, warnings generated

**Possible Causes**:
1. Items too large for sheet (even with rotation)
2. Margin + spacing leaves insufficient space
3. Polylines not closed
4. Invalid geometry (self-intersecting, duplicate points)

**Solutions**:
```csharp
// Check item size
double itemWidth = item.BoundingBox.Max.X - item.BoundingBox.Min.X;
double itemHeight = item.BoundingBox.Max.Y - item.BoundingBox.Min.Y;
if (itemWidth + 2 * margin > sheetWidth || itemHeight + 2 * margin > sheetHeight)
{
    Console.WriteLine("Item too large!");
}

// Validate polyline
if (!polyline.IsClosed)
{
    polyline.Add(polyline[0]);  // Close it
}

// Clean geometry
polyline.DeleteShortSegments(0.01);
```

---

### Problem: Low Efficiency

**Symptoms**: Large unused areas, efficiency < 70%

**Possible Causes**:
1. Wrong sort strategy
2. No rotation allowed
3. Large spacing/margin values
4. Complex shapes with large bounding boxes

**Solutions**:
```csharp
// Try different strategies
PolylineSortStrategy[] strategies = {
    PolylineSortStrategy.AreaDescending,
    PolylineSortStrategy.LargestDimensionFirst,
    PolylineSortStrategy.PerimeterDescending
};

foreach (var strategy in strategies)
{
    algorithm = new PolylineNestingAlgorithm(... sortStrategy: strategy);
    algorithm.Nest(items);
    double efficiency = CalculateEfficiency(algorithm);
    Console.WriteLine($"{strategy}: {efficiency:F2}%");
}

// Enable rotation
rotationMode: RotationMode.Rotation90  // or CustomStep

// Reduce spacing if possible
spacing: 1.0  // instead of 2.0
```

---

### Problem: Slow Performance

**Symptoms**: Takes minutes to nest, UI freezes

**Possible Causes**:
1. Fine grid resolution
2. Many rotation angles
3. Large number of items
4. Complex polylines (many vertices)

**Solutions**:
```csharp
// Increase grid resolution (less accuracy, faster)
gridResolution: 5.0  // instead of 1.0

// Reduce rotation angles
rotationMode: RotationMode.Rotation90  // instead of CustomStep

// Simplify polylines
polyline = SimplifyPolyline(polyline, tolerance: 0.5);

// Process in batches
for (int batch = 0; batch < items.Count; batch += 50)
{
    var batchItems = items.Skip(batch).Take(50).ToList();
    algorithm.Nest(batchItems);
}
```

---

### Problem: Collisions Between Items

**Symptoms**: Placed items overlap or touch

**Possible Causes**:
1. Spacing set to 0 or negative
2. Bug in collision detection (rare)
3. Polylines not closed properly

**Solutions**:
```csharp
// Increase spacing
spacing: 3.0  // Add more buffer

// Verify polylines are closed
foreach (var poly in polylines)
{
    if (!poly.IsClosed)
    {
        poly.Add(poly[0]);
    }
}

// Manual verification
var placed = algorithm.GetPlacedPolylines();
for (int i = 0; i < placed.Count - 1; i++)
{
    for (int j = i + 1; j < placed.Count; j++)
    {
        if (HasCollision(placed[i], placed[j]))
        {
            Console.WriteLine($"Collision: {i} and {j}");
        }
    }
}
```

---

## Comparison with BeamSaw

### BeamSaw Nesting Algorithm

**Designed for**: Rectangular panels
**Best for**: Sheet goods (plywood, MDF, etc.)
**Cutting method**: Guillotine cuts
**Features**:
- Grain direction control
- Kerf compensation
- Cut sequence generation
- Very fast (O(n log n))

### Polyline Nesting Algorithm

**Designed for**: Arbitrary shapes
**Best for**: CNC cutting, laser cutting, waterjet
**Cutting method**: Any path
**Features**:
- Complex geometry support
- Flexible rotation angles
- Collision detection
- Moderate speed (O(n × m))

### When to Use Each

| Use Case | Algorithm | Reason |
|----------|-----------|--------|
| Cutting plywood panels | **BeamSaw** | Faster, grain control, cut sequence |
| CNC routing complex parts | **Polyline** | Handles arbitrary shapes |
| Laser cutting letters | **Polyline** | Complex geometry, rotation control |
| Sheet metal cutting | **Polyline** | Irregular shapes common |
| Cabinet panels | **BeamSaw** | All rectangular, grain matters |
| Signage with logos | **Polyline** | Non-rectangular shapes |
| Material with grain | **BeamSaw** | Grain direction control |
| Material without grain | **Polyline** | More flexible rotation |

### Performance Comparison

For 100 items on a 2400×1200 sheet:

| Algorithm | Processing Time | Typical Efficiency |
|-----------|-----------------|-------------------|
| BeamSaw | 0.1 - 0.5 seconds | 80-90% |
| Polyline (Rotation90) | 2 - 10 seconds | 70-85% |
| Polyline (CustomStep 15°) | 30 - 120 seconds | 75-90% |

---

## Advanced Topics

### Custom Polyline Simplification

For better performance, simplify complex polylines:

```csharp
Polyline SimplifyPolyline(Polyline original, double tolerance)
{
    Curve curve = original.ToNurbsCurve();
    Curve simplified = curve.Simplify(CurveSimplifyOptions.All, tolerance, 0.01);
    Polyline result;
    simplified.TryGetPolyline(out result);
    return result ?? original;
}
```

### Batch Processing

Process large jobs in batches:

```csharp
List<PlacedPolyline> allPlaced = new List<PlacedPolyline>();
int batchSize = 50;

for (int i = 0; i < items.Count; i += batchSize)
{
    var batch = items.Skip(i).Take(batchSize).ToList();

    PolylineNestingAlgorithm algorithm = new PolylineNestingAlgorithm(...);
    algorithm.Nest(batch);

    allPlaced.AddRange(algorithm.GetPlacedPolylines());
}
```

### Export to DXF/SVG

Export nested results for CAM software:

```csharp
void ExportToDXF(List<Polyline> polylines, string filepath)
{
    var doc = RhinoDoc.ActiveDoc;

    foreach (var poly in polylines)
    {
        doc.Objects.AddPolyline(poly);
    }

    var options = new Rhino.FileIO.FileDxfWriteOptions();
    Rhino.FileIO.FileDxf.Write(filepath, doc, options);
}
```

---

## API Reference

### Main Classes

#### `PolylineItem`
Represents a polyline to be nested.

**Properties:**
- `Geometry: Polyline` - The polyline curve
- `Id: int` - Unique identifier
- `Tag: string` - Custom label
- `AllowedRotation: RotationMode` - Rotation constraint
- `BoundingBox: BoundingBox` - Bounding box (read-only)
- `Area: double` - Area (read-only)
- `Perimeter: double` - Perimeter length (read-only)
- `MaxDimension: double` - Largest dimension (read-only)

#### `PlacedPolyline`
Represents a nested polyline with its final transformation.

**Properties:**
- `Item: PolylineItem` - Original item
- `Position: Point3d` - Placement position
- `RotationDegrees: double` - Rotation angle
- `SheetIndex: int` - Which sheet (0, 1, 2, ...)
- `TransformedGeometry: Polyline` - Final geometry (read-only)
- `BoundingBox: BoundingBox` - Final bounding box (read-only)

**Methods:**
- `GetBoundingRectangle(): Rectangle3d` - Gets bounding rectangle

#### `PolylineNestingAlgorithm`
Main algorithm class.

**Methods:**
- `Nest(List<PolylineItem>)` - Execute nesting
- `GetPlacedPolylines(): List<PlacedPolyline>` - Get all placed items
- `GetPlacedGeometries(): List<Polyline>` - Get final geometries
- `GetBoundingBoxes(): List<Rectangle3d>` - Get bounding boxes
- `GetSheetRectangles(): List<Rectangle3d>` - Get sheet boundaries
- `GetMarginRectangles(): List<Rectangle3d>` - Get margin areas
- `GetItemInfo(): List<string>` - Get detailed item info
- `GetStatistics(): List<string>` - Get nesting statistics
- `GetWarnings(): List<string>` - Get warnings
- `GetSheetCount(): int` - Get number of sheets used

---

## Support and Contribution

For issues, questions, or contributions:
- GitHub: [Your Repository URL]
- Documentation: This file
- Related: See `BEAM_SAW_SETUP_GUIDE.md` for rectangular nesting

---

## License

[Your License Here]

---

## Changelog

### Version 1.0.0 (2025)
- Initial release
- Support for arbitrary polyline nesting
- Multiple rotation modes
- Three placement strategies
- Collision detection
- Multi-sheet support
- Color coding
- Comprehensive statistics

---

**END OF GUIDE**
