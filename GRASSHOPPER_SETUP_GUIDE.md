# Grasshopper Setup Guide - Planar Surface Nesting

## Quick Start

### 1. Create a Python Component in Grasshopper

1. Drop a **GhPython Script** component on your canvas
2. Right-click → Set **GhPython** to use **Python 2** (Rhino's IronPython)

### 2. Set Up Inputs

Right-click the component → **Set Inputs**:

| Input Name | Type Hint | Description |
|------------|-----------|-------------|
| `planar_surfaces` | No Type Hint | List of surfaces to nest |
| `sheet_width` | float | Sheet width(s) |
| `sheet_height` | float | Sheet height(s) |
| `kerf` | float | Blade thickness / spacing |
| `panel_tags` | No Type Hint | (Optional) Tags for panels |

**Important:**
- For `planar_surfaces`: Do NOT set a type hint - let it accept any geometry
- For `panel_tags`: Do NOT set a type hint - leave as "No Type Hint"

### 3. Set Up Outputs

Right-click → **Set Outputs**:

| Output Name | Description |
|-------------|-------------|
| `a` | Placed panel boundary curves |
| `b` | Panel IDs |
| `c` | Number of sheets used |
| `d` | Sheet rectangles |
| `e` | Panel info (detailed) |
| `f` | Panel tags |
| `g` | Sheet type IDs |

### 4. Paste the Code

Copy all the code from `optimized_nesting_planar.py` into the Python component.

### 5. Connect Your Geometry

#### Example 1: Simple Polygons

```
[Rectangle Component] → Flatten → planar_surfaces
[Number Slider: 1200] → sheet_width
[Number Slider: 800] → sheet_height
[Number Slider: 3] → kerf
```

#### Example 2: Custom Curves

```
[Your Curve/Brep/Surface] → Flatten → planar_surfaces
[Panel: 1200,2400] → sheet_width
[Panel: 800,1200] → sheet_height
[Number Slider: 5] → kerf
```

#### Example 3: With Tags

```
[Your Surfaces] → Flatten → planar_surfaces
[Series: 1,1,N] → panel_tags
```

## Common Input Issues & Solutions

### Issue 1: "No valid planar surfaces found"

**Check the debug output** - it will show you what was received:

```
=== EXTRACTING PLANAR SURFACES ===
Input type: <type 'list'>
Input value: [...]
  -> List detected with 3 items
     Item 0: Valid Brep
     Item 1: Valid Curve
     Item 2: Skipped <type 'Point3d'>  ← Problem here!
```

**Solutions:**
- Make sure you're connecting **Brep, Surface, or Curve** objects
- Use a **Flatten** component before connecting to ensure it's a list
- Remove any **Point**, **Vector**, or other non-surface objects

### Issue 2: "Brep has no faces"

**Cause:** You're passing an empty or invalid Brep

**Solutions:**
- Check your Brep is valid using a **Brep Analysis** component
- Make sure surfaces are properly created/joined
- Try using the **Curve** boundary instead of the Brep

### Issue 3: Panels not rotating

**This is expected!** The algorithm tests rotations automatically. Check the output panel info:

```python
# Output 'e' contains rotation info:
[{
  'id': 0,
  'rotation_angle': 90,  ← This panel was rotated 90°
  'rotated': True,
  ...
}]
```

### Issue 4: Poor packing efficiency

**Causes & Solutions:**

1. **Kerf too large** - Reduces usable space
   - Solution: Use realistic kerf values (2-5mm for laser cutting)

2. **Complex shapes** - Harder to pack efficiently
   - Solution: This is expected, complex shapes have lower efficiency than rectangles

3. **Sheet size mismatch** - Sheets too small for panels
   - Solution: Check min panel size vs sheet size

## Reading the Debug Output

The script provides detailed debug output in the Grasshopper panel:

```
=== EXTRACTING PLANAR SURFACES ===
Input type: <type 'list'>
  -> List detected with 5 items
     Item 0: Valid Brep
     Item 1: Valid Curve
     ...

Successfully extracted 5 surfaces

=== CREATING PANELS ===
Panel 0: Created successfully - Area: 450.00, BBox: 30.0x15.0
Panel 1: Created successfully - Area: 320.50, BBox: 25.0x12.8
...

=== NESTING CONFIGURATION ===
Panels to nest: 5
Sheet sizes: [(1200.0, 800.0)]
Kerf: 3.0
Total panel area: 1520.50

=== NESTING RESULTS ===
Sheet 1 [1200.0x800.0]: 5 panels, 87.3% usage

Total: 1 sheets, 87.3% overall efficiency
```

**What to look for:**
1. ✅ "Successfully extracted N surfaces" - Your input is valid
2. ✅ "Panel X: Created successfully" - Each panel processed
3. ✅ "Sheet 1: X panels, Y% usage" - Nesting results
4. ❌ "FAILED" or "Warning" messages - Problems to fix

## Input Types Accepted

### Brep (Best)
```
Works with:
- Planar surfaces
- Trimmed surfaces
- Polygon surfaces
- ANY closed planar Brep

The script extracts the outer boundary loop automatically.
```

### Curve (Simple)
```
Works with:
- Closed curves
- Polylines
- Rectangles
- Circles
- Any closed planar curve

Direct boundary - fastest processing.
```

### Surface (Advanced)
```
Works with:
- Untrimmed surfaces
- Planar surfaces

The script extracts boundary using isocurves.
```

## Tips for Best Results

### 1. Flatten Your Input
Always use a **Flatten** component before connecting to `planar_surfaces`:
```
[Your Geometry] → [Flatten] → planar_surfaces
```

### 2. Simplify Complex Curves
For curves with many control points:
```
[Your Curve] → [Simplify Curve] → planar_surfaces
```
This speeds up collision detection significantly!

### 3. Use Multiple Sheet Sizes
```
[Panel: 1200, 2400, 3000] → sheet_width
[Panel: 800, 1200, 1500] → sheet_height
```
The algorithm will choose the best sheet for each nesting iteration.

### 4. Test with Simple Shapes First
Start with rectangles/simple polygons to verify everything works:
```
[Rectangle] → planar_surfaces
```
Then move to complex shapes once you confirm it's working.

### 5. Check Panel Info for Details
Connect output `e` to a **Panel** to see detailed info about each placed panel:
- Original area
- Bounding box size
- Rotation angle
- Exact position
- Which sheet it's on

## Example Workflow

```
1. Create your cut shapes (any planar geometry)
   ↓
2. Flatten to list
   ↓
3. Connect to planar_surfaces input
   ↓
4. Set sheet sizes and kerf
   ↓
5. Run!
   ↓
6. Output 'a' gives placed boundaries for cutting
   ↓
7. Bake curves and send to laser cutter/CNC
```

## Troubleshooting Checklist

- [ ] Using Python 2 (IronPython) in GhPython component?
- [ ] Input is Flattened?
- [ ] Input contains valid Brep/Surface/Curve objects?
- [ ] Sheet sizes are larger than your largest panel?
- [ ] Kerf value is reasonable (not negative or huge)?
- [ ] Check debug output in panel for specific errors?

## Performance Notes

**Fast:** 10-50 panels → < 1 second
**Medium:** 50-200 panels → 1-5 seconds
**Slow:** 200+ panels → 5-30 seconds

**To speed up:**
- Simplify complex curves (reduce vertex count)
- Use simpler shapes when possible
- Reduce number of test positions (modify code)

## Next Steps

Once you have basic nesting working:

1. **Add shapes with holes** - Future version will support inner loops
2. **Custom rotation angles** - Modify code to test more angles
3. **Export for manufacturing** - Bake output curves and export DXF
4. **Optimize parameters** - Tune kerf, test different sheet sizes

---

Need help? Check the debug output first - it tells you exactly what's wrong!
